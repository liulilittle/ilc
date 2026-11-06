using System.Reflection.Metadata;

namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Linq -- System.Linq.Enumerable wiring (linq.h / linq_map.h /
// linq_sort.h -- the AOT LINQ form: array-family runtime icalls with
// delegate-object callbacks).
// Owners: System.Linq.Enumerable
//
// WIRED (gen2-b23, 2026-08-09 -- the operator surface, i4 family):
//   * ToList<TSource>          -> il2cpp_linq_to_list (List<int32> materializer)
//   * Count() / Count(pred)    -> il2cpp_linq_count / il2cpp_linq_count_i4
//   * Any() / Any(pred)        -> il2cpp_linq_any / il2cpp_linq_any_i4
//   * All(pred)                -> il2cpp_linq_all_i4
//   * Sum / Min / Max          -> il2cpp_linq_sum_i4 / min_i4 / max_i4
//   * Where(pred)              -> il2cpp_linq_where_i4
//   * Select(sel)              -> il2cpp_linq_select_i4_i4
//   * First / FirstOrDefault   -> il2cpp_linq_first_i4 / first_or_default_i4
//   * First(predicate)         -> il2cpp_linq_first_predicate (walks from the
//     start, returns the first match; no match -> InvalidOperationException)
//   * FirstOrDefault(predicate) -> il2cpp_linq_first_or_default_predicate
//     (walks from the start, returns the first match; no match or empty ->
//     the int32 default 0, never throws)
//   * ToArray()                -> il2cpp_linq_to_array
//   * Take(count) / Skip(count) -> il2cpp_linq_take_i4 / il2cpp_linq_skip_i4
//     (net48 slice semantics, probe-verified: a NEGATIVE count does NOT throw
//     -- Take(-n) is an empty result, Skip(-n) a full copy; count > length
//     clamps. Eager materialization into a fresh int32[], tracked for
//     downstream operators.)
//   * OrderBy(keySelector)     -> il2cpp_linq_order_by_key_i4 (the D-LINQ-1
//     ABI bridge, linq_sort.h: Enumerable.OrderBy passes a 1-arg KEY SELECTOR
//     Func<TSource,TKey>, NOT a 2-arg Comparison<T>; the runtime caches each
//     element's key EXACTLY ONCE -- net48 EnumerableSorter semantics -- and
//     runs the stable sort on the cached keys. int32 elements AND int32 keys
//     only; a non-int32 key family is LOUD.)
//   * Concat(source, second)   -> il2cpp_linq_concat_i4 (D-LINQZ-1): order AND
//     duplicates preserved, length = sum of the parts, empty operand ok.
//   * Distinct(source)         -> il2cpp_linq_distinct_i4 (D-LINQZ-1):
//     duplicates removed keeping FIRST-APPEARANCE order (net48 default
//     ordinal int32 equality).
//   * Except(source, second)   -> il2cpp_linq_except_i4 (D-LINQZ-1): elements
//     of source not in second (set membership), first-appearance order,
//     deduplicated output.
//   * Zip(source, second, sel) -> il2cpp_linq_zip_i4 (D-LINQZ-1): element-wise
//     pairing to the SHORTER length; the 2-arg selector
//     Func<TSource,TSecond,TResult> runs EXACTLY ONCE per output element
//     (int32 elements AND int32 result only -- the 2-arg selector ABI
//     int32_t(*)(int32_t, int32_t), linq_map.h).
//   All gate on genArgs[0] == 0x08 (int32 element family) -- the ONLY family
//   with the full predicate/selector icall surface. The reference-element
//   entries that DO exist (count_ref/any_ref/where_ref/select_ref_ref/
//   first_ref/first_or_default_ref) are wired for their exact shapes in the
//   ref-source block (P0-10/P0-11); every other ref-source operator stays
//   LOUD. The delegate producer is shared: a reference parameter renders as
//   Il2CppObject* (ParamTypesCpp), so il2cpp_delegate_new supplies the
//   bool(*)(Il2CppObject*) / Il2CppObject*(*)(Il2CppObject*) methodPtr
//   (instance leaves put `this` in the first slot, per the runtime
//   per-binding dispatch).
//
//   ARG SHAPE (static extension calls): the receiver (the source int32[] the
//   generated code holds for the enumerable) is the FIRST IL argument; the
//   predicate/selector DELEGATE (an Il2CppObject* produced by
//   il2cpp_delegate_new) is the second. Casts ["a", ""] map array ->
//   Il2CppArray* and pass the delegate through as the managed ref slot
//   (the same single-cast static-methodPtr ABI as Array.Find<int>).
//
// LOUD (read-only runtime surface gaps -- documented, not wireable here):
//   * Instance-bound delegates (linqcase's p.IsEven): the runtime callback
//     contract (linq.h:19-27) rejects target-bound delegates with
//     InvalidOperationException -- a read-only runtime contract.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    // b5-gaps section 56: does the call carry a user comparer parameter
    // (IEqualityComparer`1 / IComparer`1 / the non-generic IComparer)? A
    // comparer overload this surface does not wire must go LOUD instead of
    // silently binding the plain kernel and dropping the comparer.
    private static bool LinqHasComparerParam(MetadataReader md, BlobHandle sigHandle)
    {
        // The signature decode may read the TYPE DEF/REF rows behind a
        // parameter (a Func`2 / IEqualityComparer`1 coded row). Synthetic PEs in
        // the unit suite carry only the Enumerable TypeRef, so a decode there
        // throws -- treat it as "no comparer" (a real comparer call always
        // carries the resolved TypeRef it came from, which decodes fine).
        try
        {
            var ps = LinqEncodedParamTypes(md, sigHandle);
            for (int pi = 0; pi < ps.Length; pi++)
            {
                string def = GenericDefNameOf(ps[pi]);
                if (def == "System.Collections.Generic.IEqualityComparer`1" ||
                    def == "System.Collections.Generic.IComparer`1" ||
                    def == "System.Collections.IComparer")
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or NotSupportedException)
        {
            return false;
        }
        return false;
    }

    private static ResolvedMethod? TryMapLinq(MetadataReader md, BlobHandle sigHandle, string owner, string name,
        byte[] ps, int count, byte[]? genArgs, string[]? genArgNames, string? receiverType, bool receiverIsAddress,
        string? arg0Type, string? arg1Type)
    {
        // [0] the source array, [1] the predicate/selector delegate.
        string[] TwoArg = { "a", "" };
        string[] OneArg = { "a" };

        // b5-gaps section 68: Enumerable.Range(start, count) -> a fresh
        // int32[] of `count` consecutive integers beginning at `start`. The
        // runtime validates net48's front-loaded argument contract (count < 0
        // or start + count - 1 overflow) and answers the same sequence an
        // eager array, so the results feed every array source below.
        if (name == "Range")
        {
            if (count != 2) return null;
            return I("il2cpp::il2cpp_linq_range", 2, true, true, false,
                new[] { "", "" }, returnTypeName: "System.Int32[]");
        }

        // b5-gaps section 69: Enumerable.Repeat(element, count) and
        // Enumerable.Empty<T>(). The element cell picks the runtime entry
        // (references as the shared-pointer array, int32-family via the i4
        // entry writing at the cell width, int64 / float / double at their
        // own); decimal/DateTime/TimeSpan/Guid/DateTimeOffset cells stay LOUD.
        if (name == "Repeat" || name == "Empty")
        {
            if (genArgs is not { Length: >= 1 }) return null;
            int expect = name == "Repeat" ? 2 : 0;
            if (count != expect) return null;
            string? repCell = LinqCellCodeOfElemByte(genArgs[0])
                ?? (IsRefFamilyByte(genArgs[0]) ? "15" : null);
            if (repCell == null)
            {
                throw new NotSupportedException(
                    $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])}> is not " +
                    "wired: no element cell (b5-gaps section 69, docs/audit/b5-gaps.md).");
            }
            if (name == "Empty")
            {
                return I("il2cpp::il2cpp_linq_empty", 0, true, true, false, null,
                    returnTypeName: LinqArrayTypeNameExact(genArgs[0], genArgNames, 0),
                    appendLiterals: new[] { repCell });
            }
            string? repCpp = repCell switch
            {
                "15" => "il2cpp::il2cpp_linq_repeat_ref",
                "8" => "il2cpp::il2cpp_linq_repeat_i8",
                "9" or "10" => "il2cpp::il2cpp_linq_repeat_d",
                "0" or "1" or "2" or "3" or "4" or "5" or "6" or "7" =>
                    "il2cpp::il2cpp_linq_repeat_i4",
                _ => null,
            };
            if (repCpp == null)
            {
                throw new NotSupportedException(
                    $"Enumerable.Repeat<{GenericArgTypeName(genArgs[0])}> is not " +
                    "wired: decimal/DateTime/TimeSpan/Guid/DateTimeOffset element " +
                    "cells stay LOUD (b5-gaps section 69, docs/audit/b5-gaps.md).");
            }
            return I(repCpp, 2, true, true, false, new[] { "", "" },
                returnTypeName: LinqArrayTypeNameExact(genArgs[0], genArgNames, 0),
                appendLiterals: repCell == "15" ? null : new[] { repCell });
        }

        // b5-gaps section 58: Enumerable.ToHashSet(source[, comparer]). The
        // element byte picks the HashSet instantiation tag (the tracked
        // receiver for the downstream HashSet members) and the CELL literal
        // that seeds the shared set object through the element family's
        // IEnumerable<T> ctor (il2cpp_linq_to_hash_set[_cmp]).
        if (name == "ToHashSet")
        {
            if (genArgs is not { Length: >= 1 }) return null;
            string? tag = genArgs[0] switch
            {
                0x08 => "int",
                0x09 => "uint",
                0x0A => "long",
                0x0D => "double",
                0x11 => "System.Decimal",
                0x0E => "string",
                0x15 => "object",
                0x12 => "class",
                _ => null,
            };
            if (tag == null)
            {
                throw new NotSupportedException(
                    $"Enumerable.ToHashSet<{GenericArgTypeName(genArgs[0])}> is not wired: " +
                    "the HashSet seeding surface covers int32/uint32/int64/double/decimal/" +
                    "string/object/class elements only (b5-gaps section 58, docs/audit/b5-gaps.md).");
            }
            string? cell = genArgs[0] switch
            {
                0x08 => "7",
                0x09 => "6",
                0x0A => "8",
                0x0D => "10",
                0x11 => "11",
                0x0E or 0x12 or 0x15 => "15",
                _ => null,
            };
            bool hasCmp = LinqHasComparerParam(md, sigHandle);
            if (hasCmp ? count != 2 : count != 1) return null;
            // At the call site the LAST IL operand is on the stack top, so
            // arg0Type names the comparer for the 2-argument form and the
            // array's type lives in arg1Type (verified by probe).
            string? srcTypeName = count >= 2 ? arg1Type : arg0Type;
            bool arraySrc = srcTypeName != null
                && srcTypeName.EndsWith("[]", StringComparison.Ordinal);
            string srcCast = arraySrc ? "a" : "";
            return I(hasCmp ? "il2cpp::il2cpp_linq_to_hash_set_cmp"
                            : "il2cpp::il2cpp_linq_to_hash_set",
                count, true, true, false,
                hasCmp ? new[] { srcCast, "" } : new[] { srcCast },
                returnTypeName: $"System.Collections.Generic.HashSet`1_{tag}",
                appendLiterals: new[] { cell! });
        }

        // b5-gaps sections 59/60/61: Enumerable.SelectMany.
        //  - 2-arg (source, collectionSelector) over ARRAY sources
        //    (il2cpp_linq_select_many) and List<T> sources
        //    (il2cpp_linq_list_select_many, list-kind literal appended).
        //  - 3-arg (source, collectionSelector, resultSelector) over ARRAY
        //    sources (il2cpp_linq_select_many3; the source element cell, the
        //    inner element cell and the TResult cell ride trailing literals).
        //    There is no arg2Type hint for the source, so the runtime guards
        //    array-ness from the object descriptor (klass name '[') BEFORE any
        //    array-shaped read -- the D-2 guard for a mis-cast List receiver.
        if (name == "SelectMany")
        {
            if (count == 3)
            {
                if (genArgs is not { Length: >= 3 }) return null;
                string? smxSrc = LinqCellCodeOfElemByte(genArgs[0])
                    ?? (IsRefFamilyByte(genArgs[0]) ? "15" : null);
                string? smxColl = LinqCellCodeOfElemByte(genArgs[1])
                    ?? (IsRefFamilyByte(genArgs[1]) ? "15" : null);
                string? smxRes = LinqCellCodeOfElemByte(genArgs[2])
                    ?? (IsRefFamilyByte(genArgs[2]) ? "15" : null);
                if (smxSrc == null || smxColl == null || smxRes == null)
                {
                    throw new NotSupportedException(
                        $"Enumerable.SelectMany<{GenericArgTypeName(genArgs[0])}," +
                        $"{GenericArgTypeName(genArgs[1])},{GenericArgTypeName(genArgs[2])}> " +
                        "is not wired: no selector ABI for a source / inner / result cell " +
                        "(b5-gaps section 61, docs/audit/b5-gaps.md).");
                }
                return I("il2cpp::il2cpp_linq_select_many3", 3, true, true, false,
                    new[] { "a", "", "" },
                    returnTypeName: LinqArrayTypeNameExact(genArgs[2], genArgNames, 2),
                    appendLiterals: new[] { smxSrc, smxColl, smxRes });
            }
            if (genArgs is not { Length: >= 2 } || count != 2) return null;
            string? srcCell = LinqCellCodeOfElemByte(genArgs[0])
                ?? (IsRefFamilyByte(genArgs[0]) ? "15" : null);
            string? resCell = LinqCellCodeOfElemByte(genArgs[1])
                ?? (IsRefFamilyByte(genArgs[1]) ? "15" : null);
            if (srcCell == null || resCell == null)
            {
                throw new NotSupportedException(
                    $"Enumerable.SelectMany<{GenericArgTypeName(genArgs[0])}," +
                    $"{GenericArgTypeName(genArgs[1])}> is not wired: no cell ABI for " +
                    "the source / result element (b5-gaps section 59).");
            }
            string? srcTypeName = count >= 2 ? arg1Type : arg0Type;
            if (srcTypeName == null) return null;
            if (srcTypeName.EndsWith("[]", StringComparison.Ordinal))
            {
                return I("il2cpp::il2cpp_linq_select_many", 2, true, true, false,
                    new[] { "a", "" },
                    returnTypeName: LinqArrayTypeNameExact(genArgs[1], genArgNames, 1),
                    appendLiterals: new[] { srcCell, resCell });
            }
            // b5-gaps section 60: a List<T> source snapshots into its element
            // cell. The list kind literal selects the snapshot family; the
            // source element cell (selector ABI) and TResult cell ride as for
            // the array form.
            if (srcTypeName.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal))
            {
string? smListKind = LinqListKindOfElemByte(genArgs[0], genArgNames, 0)
                    ?? (IsRefFamilyByte(genArgs[0]) ? "1" : null);
                if (smListKind == null) return null;
                return I("il2cpp::il2cpp_linq_list_select_many", 2, true, true, false,
                    new[] { "", "" },
                    returnTypeName: LinqArrayTypeNameExact(genArgs[1], genArgNames, 1),
                    appendLiterals: new[] { srcCell, resCell, smListKind });
            }
            return null;
        }

        // b5-gaps section 62: Enumerable.Join(outer, inner, outerKeySelector,
        // innerKeySelector, resultSelector) over ARRAY sources. genArgs =
        // [TOuter, TInner, TKey, TResult]; the outer element cell, the inner
        // element cell, the TKey cell and the TResult cell ride trailing
        // literals. The array sources ride the "a" cast; array-ness is
        // enforced by the runtime D-2 guard (a 5-argument operator reaches no
        // argument-hint for the sources).
        if (name == "Join")
        {
            if (genArgs is not { Length: >= 4 }) return null;
            if (count != 5 && count != 6) return null;
            string? jOuter = LinqCellCodeOfElemByte(genArgs[0])
                ?? (IsRefFamilyByte(genArgs[0]) ? "15" : null);
            string? jInner = LinqCellCodeOfElemByte(genArgs[1])
                ?? (IsRefFamilyByte(genArgs[1]) ? "15" : null);
            string? jKey = LinqCellCodeOfElemByte(genArgs[2])
                ?? (IsRefFamilyByte(genArgs[2]) ? "15" : null);
            string? jRes = LinqCellCodeOfElemByte(genArgs[3])
                ?? (IsRefFamilyByte(genArgs[3]) ? "15" : null);
            if (jOuter == null || jInner == null || jKey == null || jRes == null)
            {
                throw new NotSupportedException(
                    $"Enumerable.Join<{GenericArgTypeName(genArgs[0])}," +
                    $"{GenericArgTypeName(genArgs[1])},{GenericArgTypeName(genArgs[2])}," +
                    $"{GenericArgTypeName(genArgs[3])}> is not wired: no cell code " +
                    "for a source / key / result element (b5-gaps section 62, " +
                    "docs/audit/b5-gaps.md).");
            }
            if (count == 5)
            {
                return I("il2cpp::il2cpp_linq_join", 5, true, true, false,
                    new[] { "a", "a", "", "", "" },
                    returnTypeName: LinqArrayTypeNameExact(genArgs[3], genArgNames, 3),
                    appendLiterals: new[] { jOuter, jInner, jKey, jRes });
            }
            // b5-gaps section 64/66: the IEqualityComparer<TKey> 6-argument
            // form binds REFERENCE TKeys (section 64) AND the value-key
            // families with a comparer GetHashCode/Equals wiring (section 66:
            // int32 / narrow / int64 / float / double). The runtime dispatches
            // per-key-keycell.
            return I("il2cpp::il2cpp_linq_join_cmp", 6, true, true, false,
                new[] { "a", "a", "", "", "", "" },
                returnTypeName: LinqArrayTypeNameExact(genArgs[3], genArgNames, 3),
                appendLiterals: new[] { jOuter, jInner, jKey, jRes });
        }

        // b5-gaps section 63: Enumerable.GroupJoin -- the LEFT-JOIN shape
        // over ARRAY sources; every outer element yields exactly one result
        // (its matching inner elements materialized as a fresh TInner[]). The
        // cell literal layout mirrors Join.
        if (name == "GroupJoin")
        {
            if (genArgs is not { Length: >= 4 }) return null;
            if (count != 5 && count != 6) return null;
            string? gjOuter = LinqCellCodeOfElemByte(genArgs[0])
                ?? (IsRefFamilyByte(genArgs[0]) ? "15" : null);
            string? gjInner = LinqCellCodeOfElemByte(genArgs[1])
                ?? (IsRefFamilyByte(genArgs[1]) ? "15" : null);
            string? gjKey = LinqCellCodeOfElemByte(genArgs[2])
                ?? (IsRefFamilyByte(genArgs[2]) ? "15" : null);
            string? gjRes = LinqCellCodeOfElemByte(genArgs[3])
                ?? (IsRefFamilyByte(genArgs[3]) ? "15" : null);
            if (gjOuter == null || gjInner == null || gjKey == null || gjRes == null)
            {
                throw new NotSupportedException(
                    $"Enumerable.GroupJoin<{GenericArgTypeName(genArgs[0])}," +
                    $"{GenericArgTypeName(genArgs[1])},{GenericArgTypeName(genArgs[2])}," +
                    $"{GenericArgTypeName(genArgs[3])}> is not wired: no cell code " +
                    "for a source / key / result element (b5-gaps section 63, " +
                    "docs/audit/b5-gaps.md).");
            }
            if (count == 5)
            {
                return I("il2cpp::il2cpp_linq_group_join", 5, true, true, false,
                    new[] { "a", "a", "", "", "" },
                    returnTypeName: LinqArrayTypeNameExact(genArgs[3], genArgNames, 3),
                    appendLiterals: new[] { gjOuter, gjInner, gjKey, gjRes });
            }
            // b5-gaps section 65/66: the IEqualityComparer<TKey> 6-argument form
            // binds REFERENCE TKeys (section 65) and the value-key families
            // with a comparer GetHashCode/Equals wiring (section 66; see the
            // Join comparer arm).
            return I("il2cpp::il2cpp_linq_group_join_cmp", 6, true, true, false,
                new[] { "a", "a", "", "", "", "" },
                returnTypeName: LinqArrayTypeNameExact(genArgs[3], genArgNames, 3),
                appendLiterals: new[] { gjOuter, gjInner, gjKey, gjRes });
        }

        // nb9 FIX: the SOURCE receiver hint. For an extension call the source
        // is the FIRST IL argument -- the stack's SECOND value for arity >= 2
        // (arg1Type) and the ONLY value for arity 1 (arg0Type); an instance
        // call (get_Key) carries receiverType. The old code read arg0Type
        // unconditionally, so `g.Where(pred)` (arity 2) saw the DELEGATE's
        // tracked type instead of the grouping and fell through to the array
        // icalls.
        string? srcType = receiverType ?? (count >= 2 ? arg1Type : arg0Type);
        // Zip is the ONE operator whose FIRST argument is the source while the
        // arity is >= 2 (the shape is Zip(first, second, selector)): the generic
        // rule above would pick the SECOND sequence as the source and then
        // dispatch a `List<byte>.Zip(byte[])` receiver into the ARRAY path --
        // casting a List object to Il2CppArray* reads its items pointer as a
        // length (the D-2 defect class).
        if (name == "Zip")
            srcType = receiverType ?? arg0Type;

        // b5-gaps §54: Enumerable.Cast<TResult> / OfType<TResult> over the
        // non-generic IEnumerable form. The source elements are BOXED; the
        // runtime classifies each box and unboxes through the family's own
        // icall (Cast: unbox.any semantics; OfType: drop non-matching/nulls).
        // A VALUE-element array source is rejected at runtime; here the source
        // must be a TRACKED array, because casting a List object to
        // Il2CppArray* would read its items pointer as a length (the D-2
        // defect class).
        if (name is "Cast" or "OfType")
        {
            if (count != 1 || genArgs is not { Length: >= 1 })
            {
                throw new NotSupportedException(
                    $"Enumerable.{name} is not wired for this shape (expected the "
                    + "single-generic result over one non-generic IEnumerable source) "
                    + "-- see docs/audit/b5-gaps.md");
            }
            if (srcType is { } srcIndicated
                && !srcIndicated.EndsWith("[]", StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"Enumerable.{name} over '{srcIndicated}' is not wired: only an "
                    + "object-element ARRAY source is supported (a List or another "
                    + "IEnumerable receiver needs its own element-kind snapshot) "
                    + "-- see docs/audit/b5-gaps.md");
            }
            byte castRes = genArgs[0];
            bool castIsRef = IsRefFamilyByte(castRes);
            string? castFam = castRes switch
            {
                0x08 => "0",  // int32
                0x0A => "1",  // int64
                0x0B => "2",  // uint64
                0x09 => "3",  // uint32
                0x04 => "4",  // sbyte
                0x05 => "5",  // byte
                0x06 => "6",  // int16
                0x07 => "7",  // uint16
                0x02 => "8",  // bool
                0x03 => "9",  // char
                0x0C => "10", // single
                0x0D => "11", // double
                _ => null,
            };
            if (castFam is null && castRes == 0x11)
            {
                castFam = LinqStructNameOf(genArgNames is { Length: >= 1 } ? genArgNames[0] : null) switch
                {
                    "System.Decimal" => "12",
                    "System.DateTime" => "13",
                    "System.TimeSpan" => "14",
                    "System.Guid" => "15",
                    "System.DateTimeOffset" => "16",
                    _ => null,
                };
            }
            string castOp = name == "Cast" ? "cast" : "of_type";
            if (castFam != null)
            {
                return I($"il2cpp::il2cpp_linq_{castOp}", 1, true, true, false, OneArg,
                    returnTypeName: LinqArrayTypeNameExact(castRes, genArgNames, 0),
                    appendLiterals: new[] { castFam });
            }
            if (castIsRef)
            {
                string castName = castRes == 0x0E
                    ? "System.String"
                    : genArgNames is { Length: >= 1 }
                        ? LinqStructNameOf(genArgNames[0]) : "System.Object";
                return I($"il2cpp::il2cpp_linq_{castOp}_ref", 1, true, true, false, OneArg,
                    returnTypeName: castName + "[]",
                    appendLiterals: new[] { LinqClassSymbolOf(castName) });
            }
            throw new NotSupportedException(
                $"Enumerable.{name} TResult '{GenericArgTypeName(castRes)}' is not "
                + "wired: the cast surface covers the primitive/narrow families, "
                + "System.Decimal/DateTime/TimeSpan/Guid/DateTimeOffset and reference "
                + "types -- see docs/audit/b5-gaps.md");
        }

        // Sum/Min/Max are special: net48 carries a CONCRETE non-generic
        // overload family Sum(this IEnumerable<int32>) / Min / Max (no
        // MethodSpec -- the il2cpp_linq_*_i4 aggregate). The element byte is
        // decoded from the raw signature's first GenericInst arg (the
        // BclResolveCore LINQ case synthesizes it for these names).
        //
        // nb9 FIX: the per-bucket (IGrouping) and whole-result (IGroupingResult)
        // forms are dispatched here, BEFORE the i4 gate -- a grouping receiver
        // is an OBJECT, never an int32[] (the old gate LOUD-rejected Min/Max
        // and silently routed Sum(selector) to the array kernel).
        if (name is "Sum" or "Min" or "Max" or "Average")
        {
            var aggRecv = srcType;
            // A List<T> receiver is a LIST OBJECT, not an array: the
            // source-only aggregates must go through the list domain. The old
            // path emitted il2cpp_linq_sum_i4(reinterpret_cast<Il2CppArray*>
            // (list)), reading the items pointer as an array header -> a wrong
            // value or an index-out-of-range on a non-empty list.
            var aggFam = LinqSourceFamily(genArgs, receiverType, count >= 2 ? arg1Type : arg0Type);
            if (LinqListKind(srcType, genArgs, aggFam, genArgNames) is { } aggListKind)
            {
                var listAgg = TryMapLinqList(md, sigHandle, name, count, genArgs, aggListKind,
                    aggFam, srcType, TwoArg, OneArg, genArgNames);
                if (listAgg != null) return listAgg;
            }
            if (aggRecv is "System.Linq.IGrouping" or "System.Linq.IGroupingResult")
            {
                // p0grp / BATCH4: a DECIMAL-source bucket holds 16-byte
                // System.Decimal cells. Route the decimal aggregate family
                // (Sum/Min/Max/Average) to the decimal bucket kernels instead
                // of reinterpreting every cell as an int32; the remaining
                // operators stay LOUD (registered in docs/audit/b5-gaps.md).
                //
                // impl-linqfix: the 0x11 byte covers EVERY value type, so the
                // DECIMAL bucket kernels are gated by the SOURCE NAME
                // (IsDecimalSourceNamed -- the same authority the decimal
                // source block uses): a DateTime / TimeSpan / Guid / user
                // struct bucket has NO bucket kernel in the runtime. The
                // decimal entries would read 16-byte decimal cells and the
                // int32 entries 4-byte cells out of the bucket's own 8/16-byte
                // payload -- both a SILENT wrong value -- so a non-decimal
                // value-type bucket stays LOUD.
                bool decBucketAgg = IsLinqDecimalSource(genArgs, receiverType, arg0Type)
                    && IsDecimalSourceNamed(genArgs, genArgNames, receiverType, arg0Type);
                bool valueBucketAgg = IsLinqValueTypeSource(genArgs, receiverType, arg0Type)
                    && !decBucketAgg;
                if (decBucketAgg)
                {
                    if (count < 2)
                    {
                        var decEntry = name switch
                        {
                            "Sum" => "il2cpp::il2cpp_linq_grouping_sum_dec",
                            "Min" => "il2cpp::il2cpp_linq_grouping_min_dec",
                            "Max" => "il2cpp::il2cpp_linq_grouping_max_dec",
                            "Average" => "il2cpp::il2cpp_linq_grouping_average_dec",
                            _ => null,
                        };
                        if (decEntry != null)
                            return I(decEntry, 1, true, false, false, new[] { "" }, resultKind: "dec");
                    }
                    else
                    {
                        // The SELECTOR forms (net48 Sum/Min/Max/Average<decimal>
                        // (bucket, Func<decimal,decimal>)): the bucket's decimal
                        // cells feed the selector and the fold runs over the
                        // projected values. The projection MUST be decimal (the
                        // net48 Sum<decimal> selector returns decimal); anything
                        // else has no decimal bucket kernel.
                        var decProj = LinqSelectorProjectionType(md, sigHandle);
                        if (decProj is "System.Decimal" or "V:System.Decimal")
                        {
                            var decSelEntry = name switch
                            {
                                "Sum" => "il2cpp::il2cpp_linq_grouping_sum_dec_sel",
                                "Min" => "il2cpp::il2cpp_linq_grouping_min_dec_sel",
                                "Max" => "il2cpp::il2cpp_linq_grouping_max_dec_sel",
                                "Average" => "il2cpp::il2cpp_linq_grouping_average_dec_sel",
                                _ => null,
                            };
                            if (decSelEntry != null)
                                return I(decSelEntry, 2, true, false, false,
                                    new[] { "", "" }, resultKind: "dec");
                        }
                        // impl-linq2 (B-2): a NON-decimal projection (net48
                        // Sum/Min/Max/Average<decimal>(bucket,
                        // Func<decimal,int32/int64/float/double>) -- the generic
                        // forms Enumerable.cs:1496-1536/1703-1739/1907-1943/
                        // 2101-2137). The bucket's decimal cells feed the
                        // selector and the fold runs over the PROJECTED values;
                        // the old mapping LOUD-rejected every non-decimal
                        // projection.
                        string? projSuffix = decProj switch
                        {
                            "i" or "c" => "i4",
                            "l" => "i8",
                            "f" => "f",
                            "d" => "d",
                            _ => null,
                        };
                        if (projSuffix != null)
                        {
                            var projEntry = name switch
                            {
                                "Sum" => $"il2cpp::il2cpp_linq_grouping_sum_dec_proj_{projSuffix}",
                                "Min" => $"il2cpp::il2cpp_linq_grouping_min_dec_proj_{projSuffix}",
                                "Max" => $"il2cpp::il2cpp_linq_grouping_max_dec_proj_{projSuffix}",
                                "Average" => $"il2cpp::il2cpp_linq_grouping_average_dec_proj_{projSuffix}",
                                _ => null,
                            };
                            if (projEntry != null)
                            {
                                string? prk = name == "Average"
                                    ? (projSuffix == "f" ? "d" : "d")
                                    : projSuffix == "i8" ? "l"
                                    : projSuffix is "f" or "d" ? "d" : null;
                                return I(projEntry, 2, true, false, false,
                                    new[] { "", "" }, resultKind: prk);
                            }
                        }
                    }
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a System.Decimal-element GroupBy bucket is not wired: " +
                        "the decimal bucket surface covers Sum()/Min()/Max()/Average() and their decimal " +
                        "selector forms (the decimal kernels); the int32 predicate/selector operators have " +
                        "no decimal bucket kernel (see docs/audit/b5-gaps.md)");
                }
                if (valueBucketAgg)
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a {ValueTypeSourceName(genArgs, genArgNames, receiverType, arg0Type)} " +
                        "GroupBy bucket is not wired: the runtime has no bucket aggregate kernel for a " +
                        "non-decimal value-type element family (the decimal bucket entries read 16-byte " +
                        "decimal cells and the int32 entries 4-byte cells out of the bucket's own " +
                        "8/16-byte payload -- both would be a silent wrong value; see docs/audit/b5-gaps.md)");
                // floatgrp: a System.Single bucket holds REAL 4-byte float cells.
                // The int32 entries below would read those cells as int32 (the
                // exact 7.5f -> 1089470464 misread) and declare an int32 result,
                // so every aggregate rides the float bucket kernels.
                if (genArgs is { Length: >= 1 } && genArgs[0] == 0x0C)
                {
                    if (count >= 2)
                        throw new NotSupportedException(
                            $"Enumerable.{name} over a System.Single GroupBy bucket with a selector is " +
                            "not wired (the runtime float bucket surface has the source-only form; see " +
                            "docs/audit/b5-gaps.md)");
                    var fAggEntry = name switch
                    {
                        "Sum" => "il2cpp::il2cpp_linq_grouping_sum_f",
                        "Min" => "il2cpp::il2cpp_linq_grouping_min_f",
                        "Max" => "il2cpp::il2cpp_linq_grouping_max_f",
                        "Average" => "il2cpp::il2cpp_linq_grouping_average_f",
                        _ => null,
                    };
                    if (fAggEntry != null)
                        return I(fAggEntry, 1, true, false, false, new[] { "" }, resultKind: "d");
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a System.Single GroupBy bucket is not wired (see " +
                        "docs/audit/b5-gaps.md)");
                }
                if (name == "Sum")
                {
                    // Sum() -> the per-bucket checked sum; Sum(selector) ->
                    // Select(selector) then the same checked sum.
                    if (count >= 2)
                        return I("il2cpp::il2cpp_linq_grouping_sum_sel", 2, true, false, false,
                            new[] { "", "" });
                    return I("il2cpp::il2cpp_linq_grouping_sum", 1, true, false, false, new[] { "" });
                }
                if (name == "Average")
                {
                    // Average()/Average(selector) -> double over the bucket.
                    return count >= 2
                        ? I("il2cpp::il2cpp_linq_grouping_average_sel", 2, true, false, false,
                            new[] { "", "" }, resultKind: "d")
                        : I("il2cpp::il2cpp_linq_grouping_average_i4", 1, true, false, false,
                            new[] { "" }, resultKind: "d");
                }
                // Min()/Max() over the bucket's int32 elements. NOTE: `count`
                // is the EXTENSION-METHOD arity -- the source itself is
                // parameter 1, so Min() arrives as count == 1.
                if (count > 1) return null;
                return I(name == "Min"
                        ? "il2cpp::il2cpp_linq_grouping_min_i4"
                        : "il2cpp::il2cpp_linq_grouping_max_i4",
                    1, true, false, false, new[] { "" });
            }
            if (count < 2)
            {
                // nb9 fix P0-4: the SOURCE-ONLY form is element-family
                // agnostic (Sum()/Min()/Max()/Average() over int64[]/double[]
                // /float-as-double[]). A reference source still falls through
                // to the generated-name path.
                return LinqFamilyAggregate(name, genArgs, receiverType, arg0Type, genArgNames);
            }
            // loud-p0-probe: a REFERENCE-element source with a selector. The
            // selector-aggregate surface is int32 (0x08) element-family only for
            // the source, but a string/object source CAN carry an int32-slot
            // projection (Func<string,char/int32>): the kernel projects inline
            // and takes the ordinal max/min (Comparer<char/int32>.Default).
            if (genArgs is { Length: >= 1 } && IsRefFamilyByte(genArgs[0]))
            {
                // The reference-source selector-aggregate family (net48
                // Sum/Min/Max/Average<TSource>(Func<TSource,TResult>)). The
                // projection's RESULT slot picks the kernel: the int32-slot
                // primitives (char/int32) ride _i4, long/ulong _i8, and
                // float/double the F-stack carrier _d / _f. net48 has every one
                // of these overloads, so none may stay LOUD.
                var refProj = LinqSelectorProjectionType(md, sigHandle);
                // impl-linq2 (B-1): a DECIMAL projection rides the 16-byte
                // result-slot kernels (net48 has the Sum/Min/Max/Average
                // Func<TSource,decimal> overloads -- Enumerable.cs:1528/1735/
                // 1939/2133). The old mapping LOUD-rejected it.
                string suffix = refProj switch
                {
                    "i" or "c" => "i4",
                    "l" => "i8",
                    "d" => "d",
                    "f" => "f",
                    "System.Decimal" or "V:System.Decimal" => "dec",
                    _ => "",
                };
                if (suffix.Length == 0)
                {
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])},...> with a selector is not wired: " +
                        "the reference selector-aggregate surface covers an int32-slot (char/int32), int64, " +
                        "double, float or decimal projection only");
                }
                string entry = name switch
                {
                    "Sum" => $"il2cpp::il2cpp_linq_sum_sel_ref_{suffix}",
                    "Min" => $"il2cpp::il2cpp_linq_min_sel_ref_{suffix}",
                    "Max" => $"il2cpp::il2cpp_linq_max_sel_ref_{suffix}",
                    _ => $"il2cpp::il2cpp_linq_average_sel_ref_{suffix}",
                };
                string? rk = suffix == "dec" ? "dec"
                    : name == "Average" ? "d"
                    : suffix == "i8" ? "l"
                    : suffix is "f" or "d" ? "d" : null;
                return I(entry, 2, true, false, false, TwoArg, resultKind: rk);
            }
            if (genArgs is not { Length: >= 1 })
            {
                throw new NotSupportedException(
                    $"Enumerable.{name} with a selector not wired: the element family is unknown");
            }
            if (genArgs[0] != 0x08)
            {
                // b5-gaps §52: the BY-VALUE value-struct SOURCE families
                // (System.DateTime / System.TimeSpan / System.Guid /
                // System.DateTimeOffset). The selector receives the ELEMENT by
                // value through the family's own struct ABI, so an int32 or
                // reference kernel would call it through a foreign ABI (a silent
                // wrong value). The SOURCE NAME decides the family (the 0x11
                // byte is shared by every value type; a user struct has no cell
                // ABI and keeps the LOUD below).
                if (genArgs[0] == 0x11
                    && LinqValueFamilyCellCode(genArgs, genArgNames, receiverType, arg0Type)
                        is { } vtCell)
                {
                    string vtFam = vtCell switch
                    {
                        "12" => "dt",
                        "13" => "ts",
                        "14" => "guid",
                        "16" => "dtoffset",
                        _ => "",
                    };
                    string vtResultKind = vtCell switch
                    {
                        "14" => "guidv",
                        "16" => "dto",
                        _ => vtFam,
                    };
                    if (vtFam.Length != 0)
                    {
                        // The selector entries' source suffix spells the
                        // DateTimeOffset family "dto" (the element entries use
                        // the full "dtoffset" name).
                        string vtSrcSuffix = vtCell == "16" ? "dto" : vtFam;
                        if (count < 2)
                        {
                            // net48 has no selector-less Sum/Average over a
                            // non-numeric element; Min/Max use
                            // Comparer<TSource>.Default (the family's own
                            // CompareTo order).
                            if (name == "Min")
                                return I($"il2cpp::il2cpp_linq_min_{vtFam}", 1, true,
                                    false, false, OneArg, resultKind: vtResultKind);
                            if (name == "Max")
                                return I($"il2cpp::il2cpp_linq_max_{vtFam}", 1, true,
                                    false, false, OneArg, resultKind: vtResultKind);
                            throw new NotSupportedException(
                                $"Enumerable.{name} over a {GenericArgTypeName(genArgs[0])} " +
                                "source WITHOUT a selector is not a net48 overload");
                        }
                        string? vtProj = LinqSelectorProjectionType(md, sigHandle) switch
                        {
                            "i" or "c" => "i4",
                            "l" => "i8",
                            "d" => "d",
                            "f" => "f",
                            "V:System.Decimal" or "System.Decimal" => "dec",
                            _ => null,
                        };
                        if (vtProj is null)
                        {
                            throw new NotSupportedException(
                                $"Enumerable.{name} over a {GenericArgTypeName(genArgs[0])} source " +
                                "with a selector not wired: the value-struct source surface covers " +
                                "the System.DateTime/System.TimeSpan/System.Guid/" +
                                "System.DateTimeOffset elements with an int32/int64/double/float/" +
                                "decimal projection only (see docs/audit/b5-gaps.md)");
                        }
                        string vtEntry = name switch
                        {
                            "Sum" => $"il2cpp::il2cpp_linq_sum_sel_{vtSrcSuffix}src_{vtProj}",
                            "Min" => $"il2cpp::il2cpp_linq_min_sel_{vtSrcSuffix}src_{vtProj}",
                            "Max" => $"il2cpp::il2cpp_linq_max_sel_{vtSrcSuffix}src_{vtProj}",
                            _ => $"il2cpp::il2cpp_linq_average_sel_{vtSrcSuffix}src_{vtProj}",
                        };
                        string? vtRk = vtProj == "dec" ? "dec"
                            : name == "Average" ? "d"
                            : vtProj == "i8" ? "l"
                            : vtProj is "f" or "d" ? "d" : null;
                        return I(vtEntry, 2, true, false, false, TwoArg, resultKind: vtRk);
                    }
                }
                // The WIDE source families (long / float / double) and the
                // NARROW-INTEGRAL source families (sbyte / byte / short /
                // ushort / uint / char -- impl-linq3 A): the selector's RESULT
                // slot picks the kernel, exactly like the reference family.
                // A narrow element is widened to int32 (the arrayext2 narrow
                // value model), so the selector ABI is the int32 one and the
                // kernel suffix mirrors the arrayext2 narrow families. net48
                // has every overload, so none stays LOUD (the stage-1 surface
                // was int32-only here).
                string? srcSuffix = genArgs[0] switch
                {
                    0x0A or 0x0B => "i8src",
                    0x0C => "fsrc",
                    0x0D => "dsrc",
                    0x04 => "i1src", // sbyte
                    0x05 => "u1src", // byte
                    0x06 => "i2src", // short
                    0x07 => "u2src", // ushort
                    0x09 => "u4src", // uint
                    0x03 => "csrc",  // char
                    _ => null,
                };
                string? projSuffix = srcSuffix is null ? null : LinqSelectorProjectionType(md, sigHandle) switch
                {
                    "i" or "c" => "i4",
                    "l" => "i8",
                    "d" => "d",
                    "f" => "f",
                    _ => null,
                };
                if (srcSuffix is null || projSuffix is null)
                {
                    string argType = GenericArgTypeName(genArgs[0]);
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{argType},...> with a selector not wired: " +
                        "the value-source selector-aggregate surface covers sbyte/byte/short/" +
                        "ushort/uint/char/long/float/double elements with an int32/int64/double/" +
                        "float projection only (see docs/audit/b5-gaps.md)");
                }
                string entry = name switch
                {
                    "Sum" => $"il2cpp::il2cpp_linq_sum_sel_{srcSuffix}_{projSuffix}",
                    "Min" => $"il2cpp::il2cpp_linq_min_sel_{srcSuffix}_{projSuffix}",
                    "Max" => $"il2cpp::il2cpp_linq_max_sel_{srcSuffix}_{projSuffix}",
                    _ => $"il2cpp::il2cpp_linq_average_sel_{srcSuffix}_{projSuffix}",
                };
                string? rk = name == "Average" ? "d"
                    : projSuffix == "i8" ? "l"
                    : projSuffix is "f" or "d" ? "d" : null;
                return I(entry, 2, true, false, false, TwoArg, resultKind: rk);
            }
            // nb9 FIX (M21): the SELECTOR forms project every element FIRST
            // and aggregate the projected values. The old mapping ignored
            // `count` and returned the selector-less aggregate with
            // ParamCount 1 -- a silent wrong value (and a stack desync).
            if (count >= 2)
            {
                // net48's selector overloads are generic only in TSource; the
                // PROJECTION type lives in the selector's own generic argument
                // (Func<TSource,int> / <TSource,long> / <TSource,double>).
                var proj = LinqSelectorProjectionType(md, sigHandle);
                switch (proj)
                {
                    case "i":
                        return name switch
                        {
                            "Sum" => I("il2cpp::il2cpp_linq_sum_sel_i4", 2, true, false, false, TwoArg),
                            "Min" => I("il2cpp::il2cpp_linq_min_sel_i4", 2, true, false, false, TwoArg),
                            "Max" => I("il2cpp::il2cpp_linq_max_sel_i4", 2, true, false, false, TwoArg),
                            _ => I("il2cpp::il2cpp_linq_average_sel_i4", 2, true, false, false,
                                TwoArg, resultKind: "d"),
                        };
                    case "l":
                        return name switch
                        {
                            "Sum" => I("il2cpp::il2cpp_linq_sum_sel_i8", 2, true, false, false,
                                TwoArg, resultKind: "l"),
                            "Min" => I("il2cpp::il2cpp_linq_min_sel_i8", 2, true, false, false,
                                TwoArg, resultKind: "l"),
                            "Max" => I("il2cpp::il2cpp_linq_max_sel_i8", 2, true, false, false,
                                TwoArg, resultKind: "l"),
                            _ => I("il2cpp::il2cpp_linq_average_sel_i8", 2, true, false, false,
                                TwoArg, resultKind: "d"),
                        };
                    case "d":
                        return name switch
                        {
                            "Sum" => I("il2cpp::il2cpp_linq_sum_sel_d", 2, true, false, false,
                                TwoArg, resultKind: "d"),
                            "Min" => I("il2cpp::il2cpp_linq_min_sel_d", 2, true, false, false,
                                TwoArg, resultKind: "d"),
                            "Max" => I("il2cpp::il2cpp_linq_max_sel_d", 2, true, false, false,
                                TwoArg, resultKind: "d"),
                            _ => I("il2cpp::il2cpp_linq_average_sel_d", 2, true, false, false,
                                TwoArg, resultKind: "d"),
                        };
                    case "f":
                        // nb9 fix P0-1: float selector projection. net48's
                        // Sum/Average(Func<T,float>) return float; the callback
                        // returns float (its F-stack parameters arrive as
                        // double), so this needs its own kernel.
                        return name switch
                        {
                            "Sum" => I("il2cpp::il2cpp_linq_sum_sel_f", 2, true, false, false,
                                TwoArg, resultKind: "d"),
                            "Min" => I("il2cpp::il2cpp_linq_min_sel_f", 2, true, false, false,
                                TwoArg, resultKind: "d"),
                            "Max" => I("il2cpp::il2cpp_linq_max_sel_f", 2, true, false, false,
                                TwoArg, resultKind: "d"),
                            _ => I("il2cpp::il2cpp_linq_average_sel_f", 2, true, false, false,
                                TwoArg, resultKind: "d"),
                        };
                    case "V:System.Decimal":
                    case "System.Decimal":
                        // A DECIMAL projection: the 16-byte result rides an
                        // explicit result slot (the decimal T3 contract --
                        // ResultKind "dec" prepends it).
                        return name switch
                        {
                            "Sum" => I("il2cpp::il2cpp_linq_sum_sel_dec", 2, true, false, false,
                                TwoArg, resultKind: "dec"),
                            "Min" => I("il2cpp::il2cpp_linq_min_sel_dec", 2, true, false, false,
                                TwoArg, resultKind: "dec"),
                            "Max" => I("il2cpp::il2cpp_linq_max_sel_dec", 2, true, false, false,
                                TwoArg, resultKind: "dec"),
                            _ => I("il2cpp::il2cpp_linq_average_sel_dec", 2, true, false, false,
                                TwoArg, resultKind: "dec"),
                        };
                    default:
                        throw new NotSupportedException(
                            $"Enumerable.{name} with selector projection '{proj}' not wired: " +
                            "the stage-1 selector-aggregate surface covers int32/int64/double/decimal projections " +
                            "(a float projection is wired below)");
                }
            }
            return name switch
            {
                "Sum" => I("il2cpp::il2cpp_linq_sum_i4", 1, true, false, false, OneArg),
                "Min" => I("il2cpp::il2cpp_linq_min_i4", 1, true, false, false, OneArg),
                "Max" => I("il2cpp::il2cpp_linq_max_i4", 1, true, false, false, OneArg),
                _ => I("il2cpp::il2cpp_linq_average_i4", 1, true, false, false,
                    OneArg, resultKind: "d"),
            };
        }

        // D-LINQ-4 grouping surface: the GroupBy pseudo-types dispatch here.
        // "System.Linq.IGrouping" is one per-bucket grouping (int32 keys and
        // int32 elements only); it arrives as an arg0/receiver hint on
        // Enumerable static calls, or as the REAL BCL owner
        // "System.Linq.IGrouping`2" on instance get_Key (routed by
        // MethodRefResolver). Casts are [""] -- OBJECT receivers, never
        // int32[] (the "a" cast would read object memory as an array header).
        // NOTE: no get_Item branch -- the BCL GroupBy result has no indexer
        // (ILookup's this[key] is key-based with no stage-1 runtime entry);
        // index-th bucket reads stay inside the runtime enumerator.
        //
        // nb9 FIX: EVERY Enumerable operator over a grouping receiver is now
        // a thin wrapper over the bucket's exact element snapshot (the runtime
        // reuses the int32 array kernels), so no operator falls through to the
        // array-family icall with an object cast.
        if (srcType == "System.Linq.IGrouping"
            || (srcType != null && srcType.StartsWith("System.Linq.IGrouping_key_", StringComparison.Ordinal))
            || (owner == "System.Linq.IGrouping`2" && name == "get_Key"))
        {
            // b5-gaps section 56: a comparer overload over a GroupBy bucket /
            // its operators is not wired -- the bucket arms would silently
            // drop the comparer (see docs/audit/b5-gaps.md).
            if (LinqHasComparerParam(md, sigHandle))
            {
                throw new NotSupportedException(
                    $"Enumerable.{name} over a GroupBy bucket with a user comparer is not wired yet: " +
                    "the bucket kernels would silently drop the comparer (b5-gaps section 56).");
            }
            if (name == "get_Key" && count == 0)
            {
                // INSTANCE property accessor: ParamCount is the EXPLICIT
                // parameter count (0); the emitter prepends the receiver.
                // BATCH4: dispatch on the tracked grouping type's key-family
                // marker ("System.Linq.IGrouping_key_i8/_dec/_dt/_guid").
                var keyMarker = srcType != null && srcType.StartsWith("System.Linq.IGrouping_key_", StringComparison.Ordinal)
                    ? srcType["System.Linq.IGrouping".Length..] : "";
                if (keyMarker.Length == 0 && srcType != null
                    && srcType.StartsWith("System.Linq.IGrouping`2<", StringComparison.Ordinal))
                {
                    // b5-gaps §55: the comparer-GroupBy lambda parameter decodes
                    // as the CLOSED name "System.Linq.IGrouping`2<r:System.
                    // String, ...>" -- the FIRST generic argument is the KEY
                    // family. Without it the default int get_key bound a
                    // string-key bucket (an int64-return ABI into an
                    // Il2CppObject* slot: a compile error).
                    var grInner = srcType.Substring("System.Linq.IGrouping`2<".Length);
                    var grComma = grInner.IndexOf(',');
                    var grTKey = grComma >= 0 ? grInner[..grComma] : grInner;
                    keyMarker = grTKey switch
                    {
                        "l" or "l8" => "_key_i8",
                        "d" => "_key_d",
                        "r:System.String" => "_key_ref",
                        "V:System.Decimal" => "_key_dec",
                        "V:System.DateTime" => "_key_dt",
                        "V:System.Guid" => "_key_guid",
                        "V:System.TimeSpan" => "_key_ts",
                        "V:System.DateTimeOffset" => "_key_dto",
                        _ when grTKey.StartsWith("r:", StringComparison.Ordinal) => "_key_ref",
                        _ => "",
                    };
                }
                return keyMarker switch
                {
                    "_key_i8" => I("il2cpp::il2cpp_linq_grouping_get_key_i8", 0, true, false, true,
                        resultKind: "l"),
                    "_key_dec" => I("il2cpp::il2cpp_linq_grouping_get_key_dec", 0, true, false, true,
                        new[] { "" }, resultKind: "dec"),
                    "_key_dt" => I("il2cpp::il2cpp_linq_grouping_get_key_dt", 0, true, false, true,
                        new[] { "" }, resultKind: "dt"),
                    "_key_guid" => I("il2cpp::il2cpp_linq_grouping_get_key_guid", 0, true, false, true,
                        new[] { "" }, resultKind: "guid"),
                    // p0grp: a REFERENCE key (System.String / class / object).
                    "_key_ref" => I("il2cpp::il2cpp_linq_grouping_get_key_ref", 0, true, true, true,
                        new[] { "" }),
                    // impl-groupbykey: the TimeSpan (ticks) and DateTimeOffset
                    // (UTC instant + offset) key carriers.
                    "_key_ts" => I("il2cpp::il2cpp_linq_grouping_get_key_ts", 0, true, false, true,
                        resultKind: "ts"),
                    "_key_dto" => I("il2cpp::il2cpp_linq_grouping_get_key_dto", 0, true, false, true,
                        resultKind: "dto"),
                    // floatgrp: the System.Double key accessor.
                    "_key_d" => I("il2cpp::il2cpp_linq_grouping_get_key_d", 0, true, false, true,
                        resultKind: "d"),
                    _ => I("il2cpp::il2cpp_linq_grouping_get_key", 0, true, false, true),
                };
            }
            // p0grp / BATCH4: a DECIMAL-source bucket (il2cpp_linq_group_by_dec_key)
            // holds 16-byte System.Decimal cells. The int32 operator family below
            // would reinterpret every decimal as an int32 -- a silent wrong value,
            // and an ill-typed C++ temp for a decimal-typed result. The decimal
            // aggregate family is dispatched in the Sum/Min/Max/Average block
            // above; the family-agnostic members (Count()/Any()/ToArray()/
            // get_Key) stay here.
            // impl-linqfix: the 0x11 byte covers EVERY value type, so the
            // DECIMAL-bucket kernels are gated by the SOURCE NAME
            // (IsDecimalSourceNamed -- the same authority the decimal source
            // block uses): a DateTime / TimeSpan / Guid / user struct bucket
            // has NO bucket kernel in the runtime. The decimal entries would
            // read 16-byte decimal cells and the int32 entries 4-byte cells
            // out of the bucket's own 8/16-byte payload -- both a SILENT wrong
            // value. A non-decimal value-type bucket keeps the family-agnostic
            // entries (the header-only Count / Any and the ToArray snapshot)
            // and LOUDs every cell-reading operator.
            bool decBucket = IsLinqDecimalSource(genArgs, receiverType, arg0Type)
                && IsDecimalSourceNamed(genArgs, genArgNames, receiverType, arg0Type);
            bool valueBucket = IsLinqValueTypeSource(genArgs, receiverType, arg0Type) && !decBucket;
            // floatgrp: a System.Single bucket holds REAL 4-byte float cells.
            // The int32 entries below would reinterpret every cell as an int32
            // (a silent wrong value) and declare an int32 result, so the whole
            // cell-reading surface gets its own family (the runtime wrappers
            // snapshot the bucket to float[] and reuse the float array kernels).
            bool floatBucket = genArgs is { Length: >= 1 } && genArgs[0] == 0x0C;
            switch (name)
            {
                case "Where" when floatBucket:
                    return I("il2cpp::il2cpp_linq_grouping_where_f", 2, true, true, false,
                        new[] { "", "" }, returnTypeName: "System.Single[]");
                case "Select" when floatBucket:
                {
                    if (genArgs is not { Length: >= 2 }) return null;
                    string? fResSuffix = LinqValueResultSuffix(genArgs[1]);
                    if (fResSuffix is null)
                    {
                        // A reference result: the float snapshot feeds a
                        // reference-returning selector (resKind 0).
                        if (!IsRefFamilyByte(genArgs[1])) return null;
                        return I("il2cpp::il2cpp_linq_grouping_select_f", 2, true, true, false,
                            new[] { "", "" }, returnTypeName: LinqRefArrayTypeName(new[] { genArgs[1] }, null),
                            appendLiterals: new[] { "0" });
                    }
                    string fResKind = fResSuffix switch
                    {
                        "i4" => "1",
                        "i8" => "2",
                        "f" => "3",
                        "d" => "4",
                        _ => "0",
                    };
                    return I("il2cpp::il2cpp_linq_grouping_select_f", 2, true, true, false,
                        new[] { "", "" }, returnTypeName: LinqValueResultArrayName(fResSuffix),
                        appendLiterals: new[] { fResKind });
                }
                case "Count" when count >= 2 && floatBucket:
                    return I("il2cpp::il2cpp_linq_grouping_count_pred_f", 2, true, false, false,
                        new[] { "", "" });
                case "Any" when count >= 2 && floatBucket:
                    return I("il2cpp::il2cpp_linq_grouping_any_pred_f", 2, true, false, false,
                        new[] { "", "" });
                case "All" when floatBucket:
                    return I("il2cpp::il2cpp_linq_grouping_all_f", 2, true, false, false,
                        new[] { "", "" });
                case "First" when count < 2 && floatBucket:
                    return I("il2cpp::il2cpp_linq_grouping_first_f", 1, true, false, false,
                        new[] { "" }, resultKind: "d");
                case "First" when floatBucket:
                    throw new NotSupportedException(
                        "Enumerable.First over a System.Single GroupBy bucket with a predicate is not " +
                        "wired (the runtime float bucket surface has the source-only form; see " +
                        "docs/audit/b5-gaps.md)");
                case "FirstOrDefault" when count < 2 && floatBucket:
                    return I("il2cpp::il2cpp_linq_grouping_first_or_default_f", 1, true, false, false,
                        new[] { "" }, resultKind: "d");
                case "Last" when count < 2 && floatBucket:
                    return I("il2cpp::il2cpp_linq_grouping_last_f", 1, true, false, false,
                        new[] { "" }, resultKind: "d");
                case "LastOrDefault" when count < 2 && floatBucket:
                    return I("il2cpp::il2cpp_linq_grouping_last_or_default_f", 1, true, false, false,
                        new[] { "" }, resultKind: "d");
                case "Sum" when floatBucket:
                    if (count >= 2)
                        throw new NotSupportedException(
                            "Enumerable.Sum over a System.Single GroupBy bucket with a selector is not " +
                            "wired (the runtime float bucket surface has the source-only form; see " +
                            "docs/audit/b5-gaps.md)");
                    return I("il2cpp::il2cpp_linq_grouping_sum_f", 1, true, false, false,
                        new[] { "" }, resultKind: "d");
                case "Min" when floatBucket:
                    if (count >= 2) return null;
                    return I("il2cpp::il2cpp_linq_grouping_min_f", 1, true, false, false,
                        new[] { "" }, resultKind: "d");
                case "Max" when floatBucket:
                    if (count >= 2) return null;
                    return I("il2cpp::il2cpp_linq_grouping_max_f", 1, true, false, false,
                        new[] { "" }, resultKind: "d");
                case "Average" when floatBucket:
                    if (count >= 2)
                        throw new NotSupportedException(
                            "Enumerable.Average over a System.Single GroupBy bucket with a selector is " +
                            "not wired (the runtime float bucket surface has the source-only form; see " +
                            "docs/audit/b5-gaps.md)");
                    return I("il2cpp::il2cpp_linq_grouping_average_f", 1, true, false, false,
                        new[] { "" }, resultKind: "d");
                // impl-linq2 (B-3): the DECIMAL-BUCKET operator surface -- each
                // entry snapshots the bucket's exact-size decimal cells (the
                // runtime RequireDecGroupElements) and reuses the DECIMAL-SOURCE
                // kernel, the same operator set net48 exposes over
                // IEnumerable<decimal> (Enumerable.cs:591-1213 / 1341-1352 /
                // 605-661 / 732-744). The old mapping rejected every
                // non-agnostic operator over a decimal bucket ("the int32
                // predicate/selector operators have no decimal bucket kernel").
                // The element returns ride the decimal result slot; the
                // predicate crosses as bool(*)(decimal) BY VALUE.
                case "Where" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_where_dec", 2, true, true, false,
                        new[] { "", "" }, returnTypeName: "System.Decimal[]");
                case "First" when count < 2 && decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_first_dec", 1, true, false, false,
                        new[] { "" }, resultKind: "dec");
                case "FirstOrDefault" when count < 2 && decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_first_or_default_dec", 1, true, false, false,
                        new[] { "" }, resultKind: "dec");
                case "First" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_first_pred_dec", 2, true, false, false,
                        new[] { "", "" }, resultKind: "dec");
                case "FirstOrDefault" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_first_or_default_pred_dec", 2, true, false, false,
                        new[] { "", "" }, resultKind: "dec");
                case "Last" when count < 2 && decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_last_dec", 1, true, false, false,
                        new[] { "" }, resultKind: "dec");
                case "LastOrDefault" when count < 2 && decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_last_or_default_dec", 1, true, false, false,
                        new[] { "" }, resultKind: "dec");
                case "Last" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_last_pred_dec", 2, true, false, false,
                        new[] { "", "" }, resultKind: "dec");
                case "LastOrDefault" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_last_or_default_pred_dec", 2, true, false, false,
                        new[] { "", "" }, resultKind: "dec");
                case "Single" when count < 2 && decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_single_dec", 1, true, false, false,
                        new[] { "" }, resultKind: "dec");
                case "SingleOrDefault" when count < 2 && decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_single_or_default_dec", 1, true, false, false,
                        new[] { "" }, resultKind: "dec");
                case "Single" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_single_pred_dec", 2, true, false, false,
                        new[] { "", "" }, resultKind: "dec");
                case "SingleOrDefault" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_single_or_default_pred_dec", 2, true, false, false,
                        new[] { "", "" }, resultKind: "dec");
                case "ElementAt" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_element_at_dec", 2, true, false, false,
                        new[] { "", "i" }, resultKind: "dec");
                case "Reverse" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_reverse_dec", 1, true, true, false,
                        new[] { "" }, returnTypeName: "System.Decimal[]");
                case "Take" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_take_dec", 2, true, true, false,
                        new[] { "", "i" }, returnTypeName: "System.Decimal[]");
                case "Skip" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_skip_dec", 2, true, true, false,
                        new[] { "", "i" }, returnTypeName: "System.Decimal[]");
                case "TakeWhile" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_take_while_dec", 2, true, true, false,
                        new[] { "", "" }, returnTypeName: "System.Decimal[]");
                case "SkipWhile" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_skip_while_dec", 2, true, true, false,
                        new[] { "", "" }, returnTypeName: "System.Decimal[]");
                case "Distinct" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_distinct_dec", 1, true, true, false,
                        new[] { "" }, returnTypeName: "System.Decimal[]");
                case "Except" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_except_dec", 2, true, true, false,
                        new[] { "", "a" }, returnTypeName: "System.Decimal[]");
                case "Concat" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_concat_dec", 2, true, true, false,
                        new[] { "", "a" }, returnTypeName: "System.Decimal[]");
                case "DefaultIfEmpty" when count >= 2 && decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_default_if_empty_dec", 2, true, true, false,
                        new[] { "", "" }, returnTypeName: "System.Decimal[]");
                case "DefaultIfEmpty" when decBucket:
                    // The source-only form: the empty bucket yields the single
                    // default(decimal) cell (the empty bucket itself is NOT the
                    // result -- net48 DefaultIfEmpty, Enumerable.cs:998).
                    return I("il2cpp::il2cpp_linq_default_if_empty1_dec", 1, true, true, false,
                        new[] { "" }, returnTypeName: "System.Decimal[]");
                case "SequenceEqual" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_sequence_equal_dec", 2, true, false, false,
                        new[] { "", "a" });
                case "LongCount" when count < 2 && decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_long_count_dec", 1, true, false, false,
                        new[] { "" }, resultKind: "l");
                case "Count" when count >= 2 && decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_count_pred_dec", 2, true, false, false,
                        new[] { "", "" });
                case "Any" when count >= 2 && decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_any_pred_dec", 2, true, false, false,
                        new[] { "", "" });
                case "All" when decBucket:
                    return I("il2cpp::il2cpp_linq_grouping_all_dec", 2, true, false, false,
                        new[] { "", "" });
                case "OrderBy" or "OrderByDescending" when decBucket:
                {
                    // The bucket keyed sort: the key selector projects a decimal
                    // element to a key of the int32/int64/double/decimal
                    // families (the same dispatch as the decimal-source
                    // OrderBy); a DateTime/Guid key rides the struct carriers.
                    bool desc = name == "OrderByDescending";
                    byte keyByte = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                    // impl-linqleft: DateTime / Guid keys (the same carriers
                    // the decimal-source sort already uses: ticks / field-wise).
                    if (keyByte == 0x11 && LinqStructKeyFamilyName(genArgNames, 1) is { } structKey)
                        return I($"il2cpp::il2cpp_linq_grouping_order_by_key{(desc ? "_desc" : "")}_dec_{structKey}",
                            2, true, true, false, new[] { "", "" }, returnTypeName: "System.Decimal[]");
                    string? decKeyFam = keyByte switch
                    {
                        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i4",
                        0x0A or 0x0B => "i8",
                        // impl-orderfam: float gets its OWN entry (b5-gaps
                        // §14 -- the 4-byte float return is never read
                        // through the double reader).
                        0x0C => "f",
                        0x0D => "d",
                        0x11 when IsDecimalGenArg(genArgNames, 1) => "dec",
                        _ => null,
                    };
                    if (decKeyFam == null)
                        throw new NotSupportedException(
                            $"Enumerable.{name}<System.Decimal,{GenericArgTypeName(keyByte)}> over a " +
                            "System.Decimal bucket not wired: the bucket keyed sort covers int32/int64/" +
                            "double/float/decimal keys only (see docs/audit/b5-gaps.md)");
                    return I($"il2cpp::il2cpp_linq_grouping_order_by_key{(desc ? "_desc" : "")}_dec_{decKeyFam}",
                        2, true, true, false, new[] { "", "" }, returnTypeName: "System.Decimal[]");
                }
                case "Count" when count < 2:
                    return I("il2cpp::il2cpp_linq_grouping_get_count", 1, true, false, false, new[] { "" });
                case "Count" when !valueBucket:
                    return I("il2cpp::il2cpp_linq_grouping_count_pred", 2, true, false, false, new[] { "", "" });
                case "Where" when !valueBucket:
                    return I("il2cpp::il2cpp_linq_grouping_where", 2, true, true, false, new[] { "", "" },
                        returnTypeName: "System.Int32[]");
                case "Select" when decBucket:
                {
                    // impl-linq3 (C): the DECIMAL-bUCKET Select -- the bucket
                    // snapshot feeds the DECIMAL-SOURCE select_dec_* kernels
                    // (the same dispatch as the decimal-source Select). The
                    // (element, index) selector form has no decimal index
                    // reference-result entry and stays LOUD. Casts ["", ""]:
                    // OBJECT receivers, never Il2CppArray*.
                    if (genArgs is not { Length: >= 2 })
                        return null;
                    bool selIdx = LinqSecondParamGenericArgCount(md, sigHandle) == 3;
                    if (genArgs[1] == 0x11 && IsDecimalGenArg(genArgNames, 1))
                        return I(selIdx
                                ? "il2cpp::il2cpp_linq_grouping_select_idx_dec_dec"
                                : "il2cpp::il2cpp_linq_grouping_select_dec_dec",
                            2, true, true, false, new[] { "", "" }, returnTypeName: "System.Decimal[]");
                    string? vs = LinqValueResultSuffix(genArgs[1]);
                    if (vs is null)
                    {
                        if (IsRefFamilyByte(genArgs[1]))
                        {
                            return I(selIdx
                                    ? "il2cpp::il2cpp_linq_grouping_select_idx_dec_ref"
                                    : "il2cpp::il2cpp_linq_grouping_select_dec_ref",
                                2, true, true, false, new[] { "", "" },
                                returnTypeName: LinqRefArrayTypeName(new[] { genArgs[1] }, null));
                        }
                        throw new NotSupportedException(
                            $"Enumerable.Select over a System.Decimal GroupBy bucket with result " +
                            $"{GenericArgTypeName(genArgs[1])} not wired: the decimal-bucket select " +
                            "surface covers a System.Decimal result, the int32/int64/double/float " +
                            "VALUE results and a reference result (see docs/audit/b5-gaps.md)");
                    }
                    return I($"il2cpp::il2cpp_linq_grouping_select{(selIdx ? "_idx" : "")}_dec_{vs}",
                        2, true, true, false, new[] { "", "" }, returnTypeName: LinqValueResultArrayName(vs));
                }
                case "Select" when !valueBucket:
                    return I("il2cpp::il2cpp_linq_grouping_select", 2, true, true, false, new[] { "", "" },
                        returnTypeName: "System.Int32[]");
                case "ToArray":
                    // The exact-size snapshot IS the get_elements contract
                    // (the bucket's backing store is growable), so no separate
                    // wrapper icall is needed. impl-linq2 (C): a DECIMAL bucket
                    // stores 16-byte decimal cells -- the tracked type follows
                    // the bucket family (the old tracking said System.Int32[]
                    // and downstream element reads resolved the int32 family
                    // against a 16-byte array).
                    //
                    // impl-linqfix: the bucket ELEMENT family comes from the
                    // generic-argument NAME (the 0x11 byte is shared by every
                    // value type): a DateTime / TimeSpan / Guid bucket
                    // snapshots its own cell width, so the tracked array name
                    // must name it (the byte-only LinqArrayTypeName would say
                    // "System.Decimal[]" for every 0x11 source).
                    return I("il2cpp::il2cpp_linq_grouping_get_elements", 1, true, true, false, new[] { "" },
                        returnTypeName: decBucket ? "System.Decimal[]"
                            // floatgrp: the float bucket's snapshot is a REAL
                            // float[] (4-byte cells), not int32[].
                            : floatBucket ? "System.Single[]"
                            : valueBucket && genArgs is { Length: >= 1 }
                                ? LinqArrayTypeNameExact(genArgs[0], genArgNames, 0)
                            : "System.Int32[]");
                case "Any" when count < 2:
                    return I("il2cpp::il2cpp_linq_grouping_any", 1, true, false, false, new[] { "" });
                case "Any" when !valueBucket:
                    return I("il2cpp::il2cpp_linq_grouping_any_pred", 2, true, false, false, new[] { "", "" });
                case "All" when !valueBucket:
                    return I("il2cpp::il2cpp_linq_grouping_all", 2, true, false, false, new[] { "", "" });
                case "First" when count < 2 && !valueBucket:
                    return I("il2cpp::il2cpp_linq_grouping_first_i4", 1, true, false, false, new[] { "" });
                case "FirstOrDefault" when count < 2 && !valueBucket:
                    return I("il2cpp::il2cpp_linq_grouping_first_or_default_i4", 1, true, false, false, new[] { "" });
                case "Last" when count < 2 && !valueBucket:
                    return I("il2cpp::il2cpp_linq_grouping_last_i4", 1, true, false, false, new[] { "" });
                case "ElementAt" when !valueBucket:
                    return I("il2cpp::il2cpp_linq_grouping_element_at_i4", 2, true, false, false,
                        new[] { "", "i" });
                case "Contains" when !valueBucket:
                    // BATCH4: a decimal bucket's Contains takes the decimal by
                    // value (the decimal equality kernel); an int32 bucket rides
                    // the int32 entry.
                    if (decBucket)
                        return I("il2cpp::il2cpp_linq_grouping_contains_dec", 2, true, false, false,
                            new[] { "", "" });
                    return I("il2cpp::il2cpp_linq_grouping_contains_i4", 2, true, false, false,
                        new[] { "", "i" });
                default:
                    break;
            }
            if (decBucket)
            {
                // Every operator the decimal-bucket surface does not cover stays
                // LOUD instead of falling into the int32 bucket kernels or the
                // generic resolver (which would report the phantom "no BCL
                // mapping" or silently run an int32 kernel over 16-byte cells).
                throw new NotSupportedException(
                    $"Enumerable.{name} over a System.Decimal-element GroupBy bucket is not wired: " +
                    "the decimal bucket surface covers the aggregate family (Sum/Min/Max/Average with " +
                    "and without a selector), the element/predicate operators (Where/First/Last/Single " +
                    "with and without a predicate, ElementAt, Contains), the range operators (Reverse/" +
                    "Take/Skip/Distinct/TakeWhile/SkipWhile/Except/Concat/DefaultIfEmpty), the counts " +
                    "(Count/LongCount/Any/All/SequenceEqual), OrderBy/OrderByDescending and get_Key " +
                    "(see docs/audit/b5-gaps.md)");
            }
            if (valueBucket)
            {
                // impl-linqfix: a NON-decimal value-type bucket (DateTime /
                // TimeSpan / Guid / a user struct) has NO bucket kernel in the
                // runtime: the decimal-bucket entries read 16-byte decimal
                // cells and the int32-bucket entries 4-byte cells out of the
                // bucket's own 8/16-byte payload -- both a SILENT wrong value.
                // The family-agnostic entries (the header-only Count / Any and
                // the ToArray snapshot) are served above; every cell-reading
                // operator stays LOUD.
                throw new NotSupportedException(
                    $"Enumerable.{name} over a {ValueTypeSourceName(genArgs, genArgNames, receiverType, arg0Type)} " +
                    "GroupBy bucket is not wired: the runtime has no bucket kernel for a non-decimal " +
                    "value-type element family (the decimal bucket entries read 16-byte decimal cells and " +
                    "the int32 entries 4-byte cells out of the bucket's own 8/16-byte payload -- both would " +
                    "be a silent wrong value; see docs/audit/b5-gaps.md)");
            }
            if (floatBucket)
            {
                // floatgrp: the runtime float bucket surface covers Where /
                // Select / Count(pred) / Any(pred) / All / First / FirstOr-
                // Default / Last / LastOrDefault / Sum / Min / Max / Average.
                // Everything else stays LOUD: the int32 bucket entries below
                // would read 4-byte float cells as int32 (a silent wrong value)
                // and declare an int32 result for a float element.
                throw new NotSupportedException(
                    $"Enumerable.{name} over a System.Single GroupBy bucket is not wired: the float " +
                    "bucket surface covers Where/Select/Count(pred)/Any(pred)/All/First/FirstOrDefault/" +
                    "Last/LastOrDefault/Sum/Min/Max/Average and get_Key (see docs/audit/b5-gaps.md)");
            }
        }

        // nb9 FIX (M15): the whole GroupBy RESULT (IEnumerable<IGrouping>) --
        // the gate below used to reject it because TSource is IGrouping (0x15).
        if (srcType == "System.Linq.IGroupingResult")
        {
            switch (name)
            {
                case "Count" when count < 2:
                    return I("il2cpp::il2cpp_linq_grouping_result_get_count", 1, true, false, false, new[] { "" });
                case "Any" when count < 2:
                    return I("il2cpp::il2cpp_linq_grouping_result_any", 1, true, false, false, new[] { "" });
                case "ToArray":
                    return I("il2cpp::il2cpp_linq_grouping_result_to_array", 1, true, true, false, new[] { "" },
                        returnTypeName: "System.Object[]");
                case "ToList":
                    return I("il2cpp::il2cpp_linq_grouping_result_to_list", 1, true, true, false, new[] { "" },
                        returnTypeName: "System.Collections.Generic.List`1_object");
                case "Select":
                {
                    // b5-gaps §55: Select over the WHOLE result materializes the
                    // bucket object[] first (il2cpp_linq_grouping_result_select);
                    // a direct treat-the-result-as-array cast reads its header
                    // as a length (the D-2 class). resKind: 0 ref/1 i4/2 i8/3 f/4 d.
                    if (LinqSecondParamGenericArgCount(md, sigHandle) == 3)
                    {
                        throw new NotSupportedException(
                            "Enumerable.Select (index form) over the GroupBy result is not wired: "
                            + "the materializing result-select takes the element and index-form "
                            + "selector together -- see docs/audit/b5-gaps.md");
                    }
                    byte selResByte = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x0E;
                    string? grResKind = selResByte switch
                    {
                        0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "1",
                        0x0A or 0x0B => "2",
                        0x0C => "3",
                        0x0D => "4",
                        _ when IsRefFamilyByte(selResByte) => "0",
                        _ => null,
                    };
                    if (grResKind == null)
                    {
                        throw new NotSupportedException(
                            $"Enumerable.Select TResult '{GenericArgTypeName(selResByte)}' over the "
                            + "GroupBy result is not wired: the materializing result-select covers "
                            + "the reference and the i4/i8/f/d result families -- see docs/audit/b5-gaps.md");
                    }
                    string grResType = IsRefFamilyByte(selResByte)
                        ? (genArgNames is { Length: >= 2 } ? LinqStructNameOf(genArgNames[1]) : "System.Object") + "[]"
                        : LinqArrayTypeNameExact(selResByte, genArgNames, 1);
                    string? grRk = grResKind == "2" ? "l"
                        : grResKind is "3" or "4" ? "d" : null;
                    return I("il2cpp::il2cpp_linq_grouping_result_select", 2, true, true, false,
                        new[] { "", "" }, returnTypeName: grResType, resultKind: grRk,
                        appendLiterals: new[] { grResKind });
                }
                case "First" when count < 2:
                    return I("il2cpp::il2cpp_linq_grouping_result_first", 1, true, true, false, new[] { "" },
                        returnTypeName: "System.Linq.IGrouping");
                case "FirstOrDefault" when count < 2:
                    return I("il2cpp::il2cpp_linq_grouping_result_first_or_default", 1, true, true, false,
                        new[] { "" }, returnTypeName: "System.Linq.IGrouping");
                default:
                    break;
            }
        }

        //--------------------------------------------------------------------------
        // P0-10 / P0-11 / B5-FIX: SOURCE-FAMILY dispatch.
        //
        // The receiver (an extension method's first IL argument) can be:
        //   * an ARRAY of any element family (int32 / int64 / float / double /
        //     narrow integral / reference) -- dispatched to the array kernels;
        //   * a LIST<T> OBJECT (List<int>, List<string>, List<long>, ...) --
        //     NEVER cast to Il2CppArray* (that reads the items pointer as a
        //     length: offsetof(Il2CppArray,length) == offsetof(Il2CppListI4,
        //     items) == 8). The list domain (linq_list.h) snapshots the list
        //     through its own ToArray accessor and runs the operation on the
        //     snapshot.
        //
        // The element family comes from the MethodSpec generic argument (the
        // authoritative metadata) and, for a tracked List receiver, from the
        // AOT expansion suffix (List`1_int / List`1_string / List`1_long /
        // List`1_double / List`1_byte / ...). A receiver hint is NEVER guessed
        // from arg0Type alone (that is the delegate slot for arity >= 2).
        //--------------------------------------------------------------------------
        var linqSrcFam = LinqSourceFamily(genArgs, receiverType, count >= 2 ? arg1Type : arg0Type);
        var listKind = LinqListKind(srcType, genArgs, linqSrcFam, genArgNames);
        if (listKind is { } lk)
        {
            var listDispatch = TryMapLinqList(md, sigHandle, name, count, genArgs, lk, linqSrcFam,
                srcType, TwoArg, OneArg, genArgNames);
            if (listDispatch != null) return listDispatch;
        }

        // P0LINQ1 (D3): a VALUE-TYPE-element ARRAY source. LinqSourceFamily
        // folds 0x11 into "ref" (every value type shares that byte), so this
        // block must run BEFORE the reference-source block or a decimal[] source
        // would be dispatched into the ref kernels (which require isRefElem).
        // The implemented operator is Select with a decimal result; the
        // source-only aggregates are served earlier by LinqFamilyAggregate.
        //
        // impl-linqfix: the 0x11 byte covers EVERY value type (decimal /
        // DateTime / TimeSpan / Guid / a user struct), but the kernels this
        // block binds for the element/predicate operators are the DECIMAL
        // ones (il2cpp_linq_first_dec / where_dec / take_dec / count_pred_dec /
        // ...). The byte-only gate let a DateTime[] / TimeSpan[] / Guid[] source
        // fall into them: the typed-result entries (first_dec / last_dec /
        // single_dec / element_at_dec / ...) then emitted
        // il2cpp_linq_first_dec(&dateTimeResult, dtArray) -- an
        // Il2CppDateTime* into an Il2CppDecimal* slot (the zmatrix compile
        // failure), and the cell-reading entries (where_dec / take_dec /
        // count_pred_dec / ...) read 16-byte decimal cells out of an 8/16-byte
        // payload (a SILENT wrong value).
        //
        // The source NAME is the authority (IsDecimalSourceNamed -- the same
        // rule LinqFamilyAggregate and the decimal-source GroupBy arm use), so
        // every DECIMAL-kernel arm below carries the `decSource` gate. The
        // VALUE-FAMILY arms that DO exist (the DateTime/Guid-source GroupBy)
        // are name-guarded in place and stay reachable; a non-decimal
        // value-type source that reaches no arm falls through to the LOUD
        // gates below (the runtime has no value-family kernel for these
        // operators -- a foreign ABI would be worse than a rejection).
        if (listKind == null && IsLinqDecimalSource(genArgs, receiverType, arg0Type)
            && !IsLinqStringSource(genArgs, receiverType, arg0Type))
        {
            bool decSource = IsDecimalSourceNamed(genArgs, genArgNames, receiverType, arg0Type);
            if (decSource && name == "Select")
            {
                if (genArgs is not { Length: >= 2 })
                    return null;
                bool selIdx = LinqSecondParamGenericArgCount(md, sigHandle) == 3;
                // A DECIMAL result (net48 Select<decimal,decimal> /
                // Select<decimal,int,decimal>): the 16-byte-cell result array.
                if (genArgs[1] == 0x11 && IsDecimalGenArg(genArgNames, 1))
                    return I(selIdx
                            ? "il2cpp::il2cpp_linq_select_idx_dec_dec"
                            : "il2cpp::il2cpp_linq_select_dec_dec",
                        2, true, true, false, TwoArg, returnTypeName: "System.Decimal[]");
                // A VALUE result (net48 Select<decimal,int32/long/double/float>):
                // the element crosses as a 16-byte decimal cell BY VALUE.
                string? vs = LinqValueResultSuffix(genArgs[1]);
                if (vs is null)
                {
                    // p0grp: a REFERENCE result (net48 Select<decimal,TResult> with a
                    // reference TResult, e.g. x => x.ToString()). The selector
                    // returns Il2CppObject*; the result is a fresh reference array.
                    // The (element, index) selector form has no decimal index
                    // reference-result entry and stays LOUD.
                    if (IsRefFamilyByte(genArgs[1]))
                    {
                        // impl-linqleft: the (element, index) reference-result
                        // form rides il2cpp_linq_select_idx_dec_ref (the same
                        // Il2CppObject* selector ABI, now with the index).
                        return I(selIdx
                                ? "il2cpp::il2cpp_linq_select_idx_dec_ref"
                                : "il2cpp::il2cpp_linq_select_dec_ref",
                            2, true, true, false, TwoArg,
                            returnTypeName: LinqRefArrayTypeName(new[] { genArgs[1] }, null));
                    }
                    throw new NotSupportedException(
                        $"Enumerable.Select<System.Decimal,{GenericArgTypeName(genArgs[1])}> " +
                        (selIdx ? "with an (element, index) selector " : "") +
                        "not wired: the decimal-source select surface covers a System.Decimal result, " +
                        "the int32/int64/double/float VALUE results and a reference result " +
                        "(il2cpp_linq_select[_idx]_dec_dec/_i4/_i8/_d/_f/_ref)");
                }
                return I($"il2cpp::il2cpp_linq_select{(selIdx ? "_idx" : "")}_dec_{vs}",
                    2, true, true, false, TwoArg, returnTypeName: LinqValueResultArrayName(vs));
            }
            // p0grp: Where over a DECIMAL source (net48 Where<decimal>(source,
            // Func<decimal,bool>)). The predicate takes the decimal BY VALUE; the
            // result is a fresh decimal[].
            if (decSource && name == "Where")
            {
                if (genArgs is not { Length: >= 1 })
                    return null;
                bool predIdx = LinqSecondParamGenericArgCount(md, sigHandle) == 3;
                if (predIdx)
                    throw new NotSupportedException(
                        "Enumerable.Where over a System.Decimal source with an (element, index) predicate " +
                        "is not wired (the runtime has no decimal index-predicate entry)");
                return I("il2cpp::il2cpp_linq_where_dec", 2, true, true, false, TwoArg,
                    returnTypeName: "System.Decimal[]");
            }
            // p0grp: the DECIMAL-source element operators (net48 First / Last /
            // ElementAt / Contains / Reverse / Take / Skip). The element-returning
            // entries return the decimal BY VALUE (ResultKind "dec"); the range
            // entries materialize a fresh decimal[].
            if (decSource && name == "First" && count < 2)
                return I("il2cpp::il2cpp_linq_first_dec", 1, true, false, false, OneArg, resultKind: "dec");
            if (decSource && name == "Last" && count < 2)
                return I("il2cpp::il2cpp_linq_last_dec", 1, true, false, false, OneArg, resultKind: "dec");
            if (decSource && name == "ElementAt")
                return I("il2cpp::il2cpp_linq_element_at_dec", 2, true, false, false,
                    new[] { "a", "i" }, resultKind: "dec");
            if (decSource && name == "Contains")
                return I("il2cpp::il2cpp_linq_contains_dec", 2, true, false, false,
                    new[] { "a", "" });
            if (decSource && name == "Reverse")
                return I("il2cpp::il2cpp_linq_reverse_dec", 1, true, true, false, OneArg,
                    returnTypeName: "System.Decimal[]");
            if (decSource && name == "Take")
                return I("il2cpp::il2cpp_linq_take_dec", 2, true, true, false,
                    new[] { "a", "i" }, returnTypeName: "System.Decimal[]");
            if (decSource && name == "Skip")
                return I("il2cpp::il2cpp_linq_skip_dec", 2, true, true, false,
                    new[] { "a", "i" }, returnTypeName: "System.Decimal[]");
            // impl-linq2 (B-3): the remaining DECIMAL-source element / predicate
            // operators (net48 FirstOrDefault/LastOrDefault/Single/SingleOrDefault
            // with and without a predicate -- Enumerable.cs:1056/1109/1157/1176/
            // 1194/1213 -- plus Count(pred)/Any(pred)/All/LongCount/TakeWhile/
            // SkipWhile at :1341/1352/605/618/647/661 and the source-only
            // DefaultIfEmpty<decimal>() at :998). The element returns ride the
            // decimal result slot; the predicate crosses as bool(*)(decimal) BY
            // VALUE. Count(pred)/Any(pred) MUST be served here: the generic
            // int32 entries below are element-family agnostic and would
            // silently read the 16-byte decimal cells as int32 pairs.
            if (decSource && name == "FirstOrDefault" && count < 2)
                return I("il2cpp::il2cpp_linq_first_or_default_dec", 1, true, false, false,
                    OneArg, resultKind: "dec");
            if (decSource && name == "LastOrDefault" && count < 2)
                return I("il2cpp::il2cpp_linq_last_or_default_dec", 1, true, false, false,
                    OneArg, resultKind: "dec");
            if (decSource && name == "Single" && count < 2)
                return I("il2cpp::il2cpp_linq_single_dec", 1, true, false, false,
                    OneArg, resultKind: "dec");
            if (decSource && name == "SingleOrDefault" && count < 2)
                return I("il2cpp::il2cpp_linq_single_or_default_dec", 1, true, false, false,
                    OneArg, resultKind: "dec");
            if (decSource && name == "First" && count >= 2)
                return I("il2cpp::il2cpp_linq_first_pred_dec", 2, true, false, false,
                    TwoArg, resultKind: "dec");
            if (decSource && name == "FirstOrDefault" && count >= 2)
                return I("il2cpp::il2cpp_linq_first_or_default_pred_dec", 2, true, false, false,
                    TwoArg, resultKind: "dec");
            if (decSource && name == "Last" && count >= 2)
                return I("il2cpp::il2cpp_linq_last_pred_dec", 2, true, false, false,
                    TwoArg, resultKind: "dec");
            if (decSource && name == "LastOrDefault" && count >= 2)
                return I("il2cpp::il2cpp_linq_last_or_default_pred_dec", 2, true, false, false,
                    TwoArg, resultKind: "dec");
            if (decSource && name == "Single" && count >= 2)
                return I("il2cpp::il2cpp_linq_single_pred_dec", 2, true, false, false,
                    TwoArg, resultKind: "dec");
            if (decSource && name == "SingleOrDefault" && count >= 2)
                return I("il2cpp::il2cpp_linq_single_or_default_pred_dec", 2, true, false, false,
                    TwoArg, resultKind: "dec");
            if (decSource && name == "Count" && count >= 2)
                return I("il2cpp::il2cpp_linq_count_pred_dec", 2, true, false, false, TwoArg);
            if (decSource && name == "Any" && count >= 2)
                return I("il2cpp::il2cpp_linq_any_pred_dec", 2, true, false, false, TwoArg);
            if (decSource && name == "All")
                return I("il2cpp::il2cpp_linq_all_dec", 2, true, false, false, TwoArg);
            if (decSource && name == "LongCount" && count < 2)
                return I("il2cpp::il2cpp_linq_long_count_dec", 1, true, false, false,
                    OneArg, resultKind: "l");
            if (decSource && name == "TakeWhile")
                return I("il2cpp::il2cpp_linq_take_while_dec", 2, true, true, false, TwoArg,
                    returnTypeName: "System.Decimal[]");
            if (decSource && name == "SkipWhile")
                return I("il2cpp::il2cpp_linq_skip_while_dec", 2, true, true, false, TwoArg,
                    returnTypeName: "System.Decimal[]");
            // p0grp: the DECIMAL-source set / concat / equality operators (net48
            // Distinct / Except / Concat / DefaultIfEmpty / SequenceEqual).
            if (decSource && name == "Distinct")
                return I("il2cpp::il2cpp_linq_distinct_dec", 1, true, true, false, OneArg,
                    returnTypeName: "System.Decimal[]");
            if (decSource && name == "Except")
                return I("il2cpp::il2cpp_linq_except_dec", 2, true, true, false, new[] { "a", "a" },
                    returnTypeName: "System.Decimal[]");
            if (decSource && name == "Concat")
                return I("il2cpp::il2cpp_linq_concat_dec", 2, true, true, false, new[] { "a", "a" },
                    returnTypeName: "System.Decimal[]");
            if (decSource && name == "DefaultIfEmpty" && count >= 2)
                return I("il2cpp::il2cpp_linq_default_if_empty_dec", 2, true, true, false,
                    new[] { "a", "" }, returnTypeName: "System.Decimal[]");
            if (decSource && name == "DefaultIfEmpty")
                // impl-linq2 (B-3): the source-only form -- the old mapping
                // pointed this 1-arity call at the 2-argument kernel (an
                // under-arg'd call); the empty source now yields the single
                // default(decimal) cell.
                return I("il2cpp::il2cpp_linq_default_if_empty1_dec", 1, true, true, false,
                    OneArg, returnTypeName: "System.Decimal[]");
            if (decSource && name == "SequenceEqual")
                return I("il2cpp::il2cpp_linq_sequence_equal_dec", 2, true, false, false, new[] { "a", "a" });
            // p0grp: OrderBy / OrderByDescending over a DECIMAL source (net48
            // OrderBy<decimal,TKey>). The int32-key family rides the decimal
            // source sort; the result is a fresh decimal[].
            if (decSource && (name == "OrderBy" || name == "OrderByDescending"))
            {
                bool desc = name == "OrderByDescending";
                byte keyByte = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                // impl-linq3 (B): a DateTime / Guid key rides the struct
                // carrier sort over the decimal source (the ticks comparison /
                // the net48 Guid field-wise contract).
                if (keyByte == 0x11 && LinqStructKeyFamilyName(genArgNames, 1) is { } sk)
                {
                    return I($"il2cpp::il2cpp_linq_order_by_key{(desc ? "_desc" : "")}_dec_{sk}",
                        2, true, true, false, TwoArg, returnTypeName: "System.Decimal[]");
                }
                string? decKeyFam = keyByte switch
                {
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i4",
                    0x0A or 0x0B => "i8",
                    // impl-orderfam: float gets its OWN entry (a
                    // Func<decimal,float> leaf returns a 4-byte float per the
                    // MS x64 ABI; the shared _dec_d entry read it through a
                    // double(*) fn-pointer and mixed the callee's XMM0 residue
                    // into every key -- b5-gaps §14, thenby-decfkey).
                    0x0C => "f",
                    0x0D => "d",
                    // 0x11 is every value type; a real System.Decimal key has
                    // the decimal key comparison (other value types LOUD).
                    0x11 when IsDecimalGenArg(genArgNames, 1) => "dec",
                    _ => null,
                };
                if (decKeyFam == null)
                    throw new NotSupportedException(
                        $"Enumerable.{name}<System.Decimal,{GenericArgTypeName(keyByte)}> not wired: the " +
                        "decimal-source keyed sort covers int32/int64/double/float/decimal and " +
                        "System.DateTime/System.Guid keys only (see docs/audit/b5-gaps.md)");
                string decKeyEntry = (decKeyFam == "i4" ? "i4"
                    : decKeyFam == "i8" ? "i8"
                    : decKeyFam == "d" ? "d"
                    : decKeyFam == "f" ? "f" : "dec");
                return I($"il2cpp::il2cpp_linq_order_by_key{(desc ? "_desc" : "")}_dec_{decKeyEntry}",
                    2, true, true, false, TwoArg, returnTypeName: "System.Decimal[]");
            }
            // impl-linqfix2: the VALUE-FAMILY (System.DateTime / System.TimeSpan
            // / System.Guid) source operators. The family-generic kernels
            // (linq_family.h) read/write the element at its REAL width when
            // the caller passes the cell code as the trailing literal
            // (12 / 13 / 14), so the operators the runtime already covers ride
            // them instead of falling through to the LOUD gates. The source
            // NAME decides the family (the 0x11 byte is shared by every value
            // type); a USER STRUCT has no cell ABI and stays LOUD.
            //
            // impl-linq-vtpred: the SOURCE PREDICATE family (Where / All /
            // TakeWhile / SkipWhile / Count(pred) / Any(pred)) and the
            // source-only First / FirstOrDefault / Last / LastOrDefault now
            // have their own per-family entries -- the generated predicate
            // leaf takes the element BY VALUE, so a foreign (i4/ref) ABI would
            // be a silent wrong value. The operator arms below bind those
            // entries; every other value-family operator keeps riding the
            // family-generic cell kernels (see above).
            string? valCell = LinqValueFamilyCellCode(genArgs, genArgNames, receiverType, arg0Type);
            if (valCell is { } vc)
            {
                // The kernel suffix (dt / ts / guid / dtoffset) and the by-value
                // result kind of the element-returning entries. "guidv" is the
                // BY-VALUE Guid return (the plain "guid" kind is the Guid
                // icall out-buffer ABI -- guid.h writes through a byte
                // pointer, while the family kernels RETURN the value);
                // "dto" is the by-value DateTimeOffset carrier.
                //
                // impl-listvtops: the "16" (DateTimeOffset) cell had NO arm,
                // so it fell through to the GUID kernels -- a 16-byte cell of
                // the WRONG family (the runtime cell guard then threw, and the
                // element-returning entries produced an Il2CppGuid into an
                // Il2CppDateTimeOffset slot: a compile failure).
                string vcFam = vc switch
                {
                    "12" => "dt",
                    "13" => "ts",
                    "14" => "guid",
                    "16" => "dtoffset",
                    _ => "guid",
                };
                string vcResult = vc switch
                {
                    "14" => "guidv",
                    "16" => "dto",
                    "12" => "dt",
                    "13" => "ts",
                    _ => vcFam,
                };
                string valArrayType = LinqArrayTypeNameExact(genArgs![0], genArgNames, 0);
                // impl-linq-vtpred: the SOURCE PREDICATE family over the
                // by-value struct families. The generated predicate leaf takes
                // the element BY VALUE (Il2CppDateTime / Il2CppTimeSpan /
                // Il2CppGuid), so each operator has its own per-family entry --
                // the i4 predicate kernels would read 4-byte cells and call
                // through a foreign ABI (a silent wrong value). The index-aware
                // overloads (Func<TSource,int,bool>) have their own *_idx
                // entries; the index is the element's 0-based SOURCE position.
                bool valPredIdx = LinqSecondParamGenericArgCount(md, sigHandle) == 3;
                if (name == "Where")
                    return I($"il2cpp::il2cpp_linq_where{(valPredIdx ? "_idx" : "")}_{vcFam}",
                        2, true, true, false, TwoArg, returnTypeName: valArrayType);
                if (name == "All")
                    return I($"il2cpp::il2cpp_linq_all_{vcFam}", 2, true, false, false, TwoArg);
                if (name == "TakeWhile" || name == "SkipWhile")
                {
                    string rangeOp = name == "TakeWhile" ? "take_while" : "skip_while";
                    return I($"il2cpp::il2cpp_linq_{rangeOp}{(valPredIdx ? "_idx" : "")}_{vcFam}",
                        2, true, true, false, TwoArg, returnTypeName: valArrayType);
                }
                if (name == "Count" && count >= 2)
                    return I($"il2cpp::il2cpp_linq_count_pred_{vcFam}", 2, true, false, false, TwoArg);
                if (name == "Any" && count >= 2)
                    return I($"il2cpp::il2cpp_linq_any_pred_{vcFam}", 2, true, false, false, TwoArg);
                // impl-linq-vtpred: First / FirstOrDefault / Last /
                // LastOrDefault had NO family entry at all; the source-only
                // forms now return the element BY VALUE through the family
                // result slot (the *OrDefault forms answer default(T)).
                if (name == "First" && count < 2)
                    return I($"il2cpp::il2cpp_linq_first_{vcFam}", 1, true, false, false,
                        OneArg, resultKind: vcResult);
                if (name == "FirstOrDefault" && count < 2)
                    return I($"il2cpp::il2cpp_linq_first_or_default_{vcFam}", 1, true, false, false,
                        OneArg, resultKind: vcResult);
                if (name == "Last" && count < 2)
                    return I($"il2cpp::il2cpp_linq_last_{vcFam}", 1, true, false, false,
                        OneArg, resultKind: vcResult);
                if (name == "LastOrDefault" && count < 2)
                    return I($"il2cpp::il2cpp_linq_last_or_default_{vcFam}", 1, true, false, false,
                        OneArg, resultKind: vcResult);
                // The element-returning entries (the element crosses by value).
                if (name == "ElementAt")
                    return I($"il2cpp::il2cpp_linq_element_at_fam_{vcFam}", 2, true, false, false,
                        new[] { "a", "i" }, resultKind: vcResult, appendLiterals: new[] { vc });
                if (name == "Single" && count < 2)
                    return I($"il2cpp::il2cpp_linq_single_fam_{vcFam}", 1, true, false, false,
                        OneArg, resultKind: vcResult, appendLiterals: new[] { vc });
                if (name == "SingleOrDefault" && count < 2)
                    return I($"il2cpp::il2cpp_linq_single_or_default_fam_{vcFam}", 1, true, false, false,
                        OneArg, resultKind: vcResult, appendLiterals: new[] { vc });
                // Contains: the per-family VALUE entry -- the value rides the
                // family's own parameter slot, so the cell code is implied and
                // no literal is appended.
                if (name == "Contains")
                    return I($"il2cpp::il2cpp_linq_contains_family_{vcFam}", 2, true, false, false,
                        new[] { "a", "" });
                // The ARRAY-returning entries (a fresh array of the source's
                // own cell width).
                if (name == "Take")
                    return I("il2cpp::il2cpp_linq_take_family", 2, true, true, false,
                        new[] { "a", "i" }, returnTypeName: valArrayType, appendLiterals: new[] { vc });
                if (name == "Skip")
                    return I("il2cpp::il2cpp_linq_skip_family", 2, true, true, false,
                        new[] { "a", "i" }, returnTypeName: valArrayType, appendLiterals: new[] { vc });
                if (name == "Reverse")
                    return I("il2cpp::il2cpp_linq_reverse_family", 1, true, true, false,
                        OneArg, returnTypeName: valArrayType, appendLiterals: new[] { vc });
                if (name == "Concat")
                    return I("il2cpp::il2cpp_linq_concat_family", 2, true, true, false,
                        new[] { "a", "a" }, returnTypeName: valArrayType, appendLiterals: new[] { vc });
                if (name == "Distinct")
                    return I("il2cpp::il2cpp_linq_distinct_family", 1, true, true, false,
                        OneArg, returnTypeName: valArrayType, appendLiterals: new[] { vc });
                if (name == "Except")
                    return I("il2cpp::il2cpp_linq_except_family", 2, true, true, false,
                        new[] { "a", "a" }, returnTypeName: valArrayType, appendLiterals: new[] { vc });
                if (name == "Union")
                    return I("il2cpp::il2cpp_linq_union_family", 2, true, true, false,
                        new[] { "a", "a" }, returnTypeName: valArrayType, appendLiterals: new[] { vc });
                if (name == "Intersect")
                    return I("il2cpp::il2cpp_linq_intersect_family", 2, true, true, false,
                        new[] { "a", "a" }, returnTypeName: valArrayType, appendLiterals: new[] { vc });
                // The source-only form (DefaultIfEmpty(source)) answers the
                // family default cell; the 2-argument form carries the caller's
                // value BY VALUE through its own struct ABI (impl-loudvalstruct
                // B: the runtime ships the four by-value entries and writes the
                // cell at its real width -- a DateTime cell holds the packed
                // ticks+kind image, never a raw struct copy).
                if (name == "DefaultIfEmpty" && count < 2)
                    return I("il2cpp::il2cpp_linq_default_if_empty_family", 1, true, true, false,
                        OneArg, returnTypeName: valArrayType, appendLiterals: new[] { vc });
                if (name == "DefaultIfEmpty")
                {
                    string defEntry = vc switch
                    {
                        "12" => "il2cpp::il2cpp_linq_default_if_empty_family_dt",
                        "13" => "il2cpp::il2cpp_linq_default_if_empty_family_ts",
                        "14" => "il2cpp::il2cpp_linq_default_if_empty_family_guid",
                        "16" => "il2cpp::il2cpp_linq_default_if_empty_family_dtoffset",
                        _ => "",
                    };
                    if (defEntry.Length == 0)
                        throw new NotSupportedException(
                            $"Enumerable.DefaultIfEmpty<{ValueTypeSourceName(genArgs, genArgNames, receiverType, arg0Type)}> " +
                            "with a default value is not wired: the by-value value-struct surface covers " +
                            "System.DateTime / System.TimeSpan / System.Guid / System.DateTimeOffset " +
                            "(a user struct has no cell ABI -- see docs/audit/b5-gaps.md)");
                    return I(defEntry, 2, true, true, false, new[] { "a", "" },
                        returnTypeName: valArrayType, appendLiterals: new[] { vc });
                }
                if (name == "LongCount" && count < 2)
                    return I("il2cpp::il2cpp_linq_long_count_family", 1, true, false, false,
                        OneArg, resultKind: "l", appendLiterals: new[] { vc });
                if (name == "SequenceEqual")
                    return I("il2cpp::il2cpp_linq_sequence_equal_family", 2, true, false, false,
                        new[] { "a", "a" }, appendLiterals: new[] { vc });
            }
            // p0grp: GroupBy over a DateTime[] SOURCE (net48
            // GroupBy<TSource,TKey> with TSource = System.DateTime). The
            // DateTime element crosses as the 8-byte runtime value struct BY
            // VALUE (the InvokeSelDt* ABIs); the bucket stores the DateTime
            // cells (GroupElemKind::DateTime).
            if (name == "GroupBy" && genArgs is { Length: >= 1 } && genArgs[0] == 0x11
                && genArgNames is { Length: >= 1 }
                && (genArgNames[0] == "System.DateTime" || genArgNames[0] == "V:System.DateTime"))
            {
                int dtKeyKind = KeyKindForNamed(genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08,
                    genArgNames, 1);
                if (genArgs is not { Length: >= 2 } || dtKeyKind < 0)
                    throw new NotSupportedException(
                        "Enumerable.GroupBy<System.DateTime,...> not wired: the DateTime-source grouping " +
                        "surface buckets by a key carrier of int32-slot/int64/16-byte struct/reference " +
                        "(see docs/audit/b4-gaps.md)");
                var dtParams = LinqEncodedParamTypes(md, sigHandle);
                int dtElemArg = -1, dtCmpArg = -1, dtResArg = -1;
                for (int pi = 2; pi < dtParams.Length; pi++)
                {
                    var dtdef = GenericDefNameOf(dtParams[pi]);
                    if (dtdef == "System.Func`2") dtElemArg = pi;
                    else if (dtdef == "System.Func`3") dtResArg = pi;
                    else if (dtdef == "System.Collections.Generic.IEqualityComparer`1") dtCmpArg = pi;
                }
                byte? dtElemByte = dtElemArg >= 0 && genArgs.Length >= 3 ? genArgs[2] : null;
                byte? dtResByte = dtResArg >= 0 && genArgs.Length >= 3
                    ? (genArgs.Length >= 4 ? genArgs[3] : genArgs[2]) : null;
                string DtElemFamily(byte b) => b switch
                {
                    0x08 => "0",
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "0",
                    0x0A or 0x0B => "2",
                    0x0D => "3",
                    // floatgrp: a System.Single bucket element (4-byte cell).
                    0x0C => "7",
                    0x0E or 0x12 or 0x1C or 0x1D or 0x15 or 0x13 or 0x1E => "1",
                    0x11 => "5", // System.DateTime
                    _ => "",
                };
                string DtResultFamily(byte b) => b switch
                {
                    0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "1",
                    0x0A or 0x0B => "3",
                    0x0D => "4",
                    0x0C => "5",
                    _ => "2",
                };
                string dtEk = dtElemByte is { } dtb ? DtElemFamily(dtb) : "5";
                if (dtEk.Length == 0)
                    throw new NotSupportedException(
                        $"Enumerable.GroupBy TElement '{GenericArgTypeName(dtElemByte!.Value)}' " +
                        "over a System.DateTime source is not a supported element family");
                string dtRk = dtResByte is { } dtrb ? DtResultFamily(dtrb) : "0";
                int dtElemIdx = dtElemArg >= 0 ? dtElemArg : -1;
                int dtCmpIdx = dtCmpArg >= 0 ? dtCmpArg : -1;
                int dtResIdx = dtResArg >= 0 ? dtResArg : -1;
                var dtArgOrder = new[] { 0, 1, dtElemIdx, dtCmpIdx, dtResIdx };
                var dtCasts = new[] { "a", "", "", "", "" };
                var dtLiterals = new[] { dtEk, dtRk,
                    dtKeyKind.ToString(System.Globalization.CultureInfo.InvariantCulture), "3" };
                var dtRetType = dtRk == "0"
                    ? "System.Linq.IGroupingResult"
                    : dtRk == "1" ? "System.Int32[]"
                    : dtRk == "3" ? "System.Int64[]"
                    : dtRk == "4" ? "System.Double[]"
                    : dtRk == "5" ? "System.Double[]" : "System.Object[]";
                return I("il2cpp::il2cpp_linq_group_by_full_key", count, true, true, false, dtCasts,
                    returnTypeName: dtRetType, appendLiterals: dtLiterals, argOrder: dtArgOrder);
            }
            // vtsrc: GroupBy over a Guid[] SOURCE (net48 GroupBy<TSource,TKey>
            // with TSource = System.Guid). The Guid element crosses as the
            // 16-byte value struct BY VALUE (the InvokeSelGuid* ABIs); the
            // bucket stores the Guid cells (GroupElemKind::Guid).
            if (name == "GroupBy" && genArgs is { Length: >= 1 } && genArgs[0] == 0x11
                && genArgNames is { Length: >= 1 }
                && (genArgNames[0] == "System.Guid" || genArgNames[0] == "V:System.Guid"))
            {
                int guidKeyKind = KeyKindForNamed(genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08,
                    genArgNames, 1);
                if (genArgs is not { Length: >= 2 } || guidKeyKind < 0)
                    throw new NotSupportedException(
                        "Enumerable.GroupBy<System.Guid,...> not wired: the Guid-source grouping " +
                        "surface buckets by a key carrier of int32-slot/int64/16-byte struct/reference " +
                        "(see docs/audit/b4-gaps.md)");
                var guidParams = LinqEncodedParamTypes(md, sigHandle);
                int guidElemArg = -1, guidCmpArg = -1, guidResArg = -1;
                for (int pi = 2; pi < guidParams.Length; pi++)
                {
                    var guiddef = GenericDefNameOf(guidParams[pi]);
                    if (guiddef == "System.Func`2") guidElemArg = pi;
                    else if (guiddef == "System.Func`3") guidResArg = pi;
                    else if (guiddef == "System.Collections.Generic.IEqualityComparer`1") guidCmpArg = pi;
                }
                byte? guidElemByte = guidElemArg >= 0 && genArgs.Length >= 3 ? genArgs[2] : null;
                byte? guidResByte = guidResArg >= 0 && genArgs.Length >= 3
                    ? (genArgs.Length >= 4 ? genArgs[3] : genArgs[2]) : null;
                string GuidElemFamily(byte b, int nameIndex)
                {
                    switch (b)
                    {
                        case 0x08:
                        case 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09:
                            return "0";
                        case 0x0A or 0x0B: return "2";
                        case 0x0D: return "3";
                        // floatgrp: a System.Single bucket element (4-byte cell).
                        case 0x0C: return "7";
                        case 0x0E or 0x12 or 0x1C or 0x1D or 0x15 or 0x13 or 0x1E: return "1";
                        case 0x11:
                        {
                            var n = genArgNames is { Length: > 0 } && nameIndex < genArgNames.Length
                                ? genArgNames[nameIndex] : null;
                            if (n is not null && n.StartsWith("V:", StringComparison.Ordinal)) n = n[2..];
                            return n switch
                            {
                                "System.Decimal" => "4",
                                "System.DateTime" => "5",
                                "System.Guid" => "6",
                                _ => "",
                            };
                        }
                        default: return "";
                    }
                }
                string GuidResultFamily(byte b) => b switch
                {
                    0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "1",
                    0x0A or 0x0B => "3",
                    0x0D => "4",
                    0x0C => "5",
                    _ => "2",
                };
                string guidEk = guidElemByte is { } gb ? GuidElemFamily(gb, 2) : "6";
                if (guidEk.Length == 0)
                {
                    string elemName = genArgNames is { Length: > 2 } ? genArgNames[2] : "?";
                    throw new NotSupportedException(
                        $"Enumerable.GroupBy TElement '{elemName}' over a System.Guid source is not " +
                        "wired: whitelist 3 -- a USER-STRUCT bucket element has no runtime cell ABI");
                }
                string guidRk = guidResByte is { } grb ? GuidResultFamily(grb) : "0";
                int guidElemIdx = guidElemArg >= 0 ? guidElemArg : -1;
                int guidCmpIdx = guidCmpArg >= 0 ? guidCmpArg : -1;
                int guidResIdx = guidResArg >= 0 ? guidResArg : -1;
                var guidArgOrder = new[] { 0, 1, guidElemIdx, guidCmpIdx, guidResIdx };
                var guidCasts = new[] { "a", "", "", "", "" };
                var guidLiterals = new[] { guidEk, guidRk,
                    guidKeyKind.ToString(System.Globalization.CultureInfo.InvariantCulture), "4" };
                var guidRetType = guidRk == "0"
                    ? "System.Linq.IGroupingResult"
                    : guidRk == "1" ? "System.Int32[]"
                    : guidRk == "3" ? "System.Int64[]"
                    : guidRk == "4" ? "System.Double[]"
                    : guidRk == "5" ? "System.Double[]" : "System.Object[]";
                return I("il2cpp::il2cpp_linq_group_by_full_key", count, true, true, false, guidCasts,
                    returnTypeName: guidRetType, appendLiterals: guidLiterals, argOrder: guidArgOrder);
            }
            // impl-vtsel: GroupBy over a TimeSpan[] / DateTimeOffset[] SOURCE.
            // The element crosses by value in its own struct slot and the
            // bucket keeps the family's cell (8-byte ticks / 16-byte
            // instant+offset). srcKind 9 / 10 selects the source reader.
            if (name == "GroupBy" && genArgs is { Length: >= 1 } && genArgs[0] == 0x11
                && genArgNames is { Length: >= 1 }
                && (LinqStructNameOf(genArgNames[0]) is "System.TimeSpan" or "System.DateTimeOffset"))
            {
                string tsdSrcKind = LinqStructNameOf(genArgNames[0]) == "System.TimeSpan" ? "9" : "10";
                string tsdDefaultEk = tsdSrcKind == "9" ? "8" : "9";
                int tsdKeyKind = KeyKindForNamed(
                    genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08, genArgNames, 1);
                if (genArgs is not { Length: >= 2 } || tsdKeyKind < 0)
                    throw new NotSupportedException(
                        $"Enumerable.GroupBy<{LinqStructNameOf(genArgNames[0])}," +
                        $"{(genArgNames is { Length: >= 2 } ? genArgNames[1] : "?")}> not wired: the " +
                        "struct-source grouping surface buckets by an int32-slot / int64 / struct / " +
                        "reference key carrier only");
                var tsdParams = LinqEncodedParamTypes(md, sigHandle);
                int tsdElemArg = -1, tsdCmpArg = -1, tsdResArg = -1;
                for (int pi = 2; pi < tsdParams.Length; pi++)
                {
                    var tsddef = GenericDefNameOf(tsdParams[pi]);
                    if (tsddef == "System.Func`2") tsdElemArg = pi;
                    else if (tsddef == "System.Func`3") tsdResArg = pi;
                    else if (tsddef == "System.Collections.Generic.IEqualityComparer`1") tsdCmpArg = pi;
                }
                byte? tsdElemByte = tsdElemArg >= 0 && genArgs.Length >= 3 ? genArgs[2] : null;
                byte? tsdResByte = tsdResArg >= 0 && genArgs.Length >= 3
                    ? (genArgs.Length >= 4 ? genArgs[3] : genArgs[2]) : null;
                string tsdEk = tsdElemByte is { } teb
                    ? LinqGroupElemFamilyCode(teb, genArgNames, 2) : tsdDefaultEk;
                if (tsdEk.Length == 0)
                    throw new NotSupportedException(
                        $"Enumerable.GroupBy TElement '{GenericArgTypeName(tsdElemByte!.Value)}' over a " +
                        $"{LinqStructNameOf(genArgNames[0])} source is not a supported bucket element family");
                string tsdRk = tsdResByte is { } trb ? LinqGroupResultFamilyCode(trb) : "0";
                return I("il2cpp::il2cpp_linq_group_by_full_key_cell", count, true, true, false,
                    new[] { "a", "", "", "", "" },
                    returnTypeName: LinqGroupResultTypeName(tsdRk),
                    appendLiterals: new[]
                    {
                        tsdEk, tsdRk,
                        tsdKeyKind.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        tsdSrcKind, "0",
                    },
                    argOrder: new[] { 0, 1, tsdElemArg, tsdCmpArg, tsdResArg });
            }
            // p0grp: GroupBy over a decimal[] SOURCE with an int32-slot key.
            // Before this arm the call fell past the decimal block, past the
            // ref block (no GroupBy there) and into the int32-only gate at
            // :522, which did not even LIST GroupBy -- so the MethodSpec
            // resolved to null and the resolver reported the phantom
            // "generic instantiation of System.Linq.Enumerable.GroupBy has no
            // BCL mapping in M5" (MethodRefResolver.cs:459). The real gate was
            // the SOURCE family, not the key family: the misleading message
            // and the runtime stub are both fixed by wiring the shape here.
            //
            // Wired: the KEY-SELECTOR-ONLY overload (count == 2) with an
            // int32-slot TKey (KeyKindFor >= 0). The runtime entry
            // il2cpp_linq_group_by_dec_key invokes the selector with the
            // decimal element BY VALUE and stores 16-byte decimal VALUE cells
            // in the bucket, so a bucket's ToArray is a real decimal[].
            //
            // Still LOUD: an element selector / comparer / result selector
            // over a decimal source (their callbacks need additional by-value
            // struct ABIs), and a non-int32-slot key (long/ulong/decimal/
            // DateTime/Guid) -- see docs/audit/b4-gaps.md.
            if (name == "GroupBy")
            {
                if (genArgs is not { Length: >= 2 })
                    return null;
                // The 0x11 byte covers EVERY value type; only a real
                // System.Decimal source has the 16-byte decimal reader. A
                // named non-decimal value type (DateTime / user struct) keeps
                // the LOUD rejection instead of a wild 16-byte reinterpret.
                if (!IsDecimalGenArg(genArgNames, 0))
                    throw new NotSupportedException(
                        $"Enumerable.GroupBy<{(genArgNames is { Length: >= 1 } ? genArgNames[0] : "valuetype")},...> " +
                        "over a USER-STRUCT source is not wired: whitelist 3 -- the runtime has no " +
                        "descriptor/layout for an arbitrary user struct, so no by-value key-selector ABI " +
                        "can be generated (System.Decimal / System.DateTime / System.Guid sources are wired; " +
                        "see docs/audit/b4-gaps.md)");
                int decKeyKind = KeyKindForNamed(genArgs[1], genArgNames, 1);
                if (decKeyKind < 0)
                {
                    string decKeyName = genArgNames is { Length: >= 2 } && genArgNames[1] != "?"
                        ? genArgNames[1]
                        : GenericArgTypeName(genArgs[1]);
                    throw new NotSupportedException(
                        $"Enumerable.GroupBy<System.Decimal,{decKeyName}> not wired: " +
                        "the decimal-source grouping surface buckets by an integer key that fits the " +
                        "int32 slot (int/bool/char/sbyte/byte/short/ushort/uint); " +
                        $"{DescribeRejectedKey(genArgs[1], decKeyName)} " +
                        "has no runtime key carrier yet (see docs/audit/b4-gaps.md)");
                }
                if (count == 2)
                {
                    return I("il2cpp::il2cpp_linq_group_by_dec_key", 2, true, true, false,
                        TwoArg, returnTypeName: "System.Linq.IGroupingResult",
                        appendLiterals: new[]
                        {
                            decKeyKind.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        });
                }
                // count >= 3: an element selector / comparer / result selector.
                // The runtime BuildGroups reads the decimal source and projects
                // the element through the InvokeSelDec* ABIs.
                {
                    var dParams = LinqEncodedParamTypes(md, sigHandle);
                    int dElemArg = -1, dCmpArg = -1, dResArg = -1;
                    for (int pi = 2; pi < dParams.Length; pi++)
                    {
                        var ddef = GenericDefNameOf(dParams[pi]);
                        if (ddef == "System.Func`2") dElemArg = pi;
                        else if (ddef == "System.Func`3") dResArg = pi;
                        else if (ddef == "System.Collections.Generic.IEqualityComparer`1") dCmpArg = pi;
                    }
                    byte? dElemByte = dElemArg >= 0 && genArgs.Length >= 3 ? genArgs[2] : null;
                    byte? dResByte = dResArg >= 0 && genArgs.Length >= 3
                        ? (genArgs.Length >= 4 ? genArgs[3] : genArgs[2]) : null;
                    string DElemFamily(byte b) => b switch
                    {
                        0x08 => "0",
                        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "0",
                        0x0A or 0x0B => "2",
                        0x0D => "3",
                        // floatgrp: a System.Single bucket element (4-byte cell).
                        0x0C => "7",
                        0x0E or 0x12 or 0x1C or 0x1D or 0x15 or 0x13 or 0x1E => "1",
                        _ => "",
                    };
                    // The 0x11 value types need the NAME: System.Decimal maps to
                    // the decimal bucket family (4); DateTime/TimeSpan/Guid have
                    // no bucket storage.
                    string DElemFamilyNamed(byte b, string[]? names, int index)
                    {
                        if (b != 0x11) return DElemFamily(b);
                        if (names is not { Length: > 0 } || index >= names.Length) return "";
                        var n = names[index];
                        if (n.StartsWith("V:", StringComparison.Ordinal)) n = n[2..];
                        return n == "System.Decimal" ? "4" : "";
                    }
                    string DResultFamily(byte b) => b switch
                    {
                        0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "1",
                        0x0A or 0x0B => "3",
                        0x0D => "4",
                        0x0C => "5",
                        _ => "2",
                    };
                    string dEk = dElemByte is { } deb ? DElemFamilyNamed(deb, genArgNames, 2) : "0";
                    if (dEk.Length == 0)
                        throw new NotSupportedException(
                            $"Enumerable.GroupBy TElement '{GenericArgTypeName(dElemByte!.Value)}' " +
                            "over a System.Decimal source is not a supported element family");
                    string dRk = dResByte is { } drb ? DResultFamily(drb) : "0";
                    int dElemIdx = dElemArg >= 0 ? dElemArg : -1;
                    int dCmpIdx = dCmpArg >= 0 ? dCmpArg : -1;
                    int dResIdx = dResArg >= 0 ? dResArg : -1;
                    var dArgOrder = new[] { 0, 1, dElemIdx, dCmpIdx, dResIdx };
                    var dCasts = new[] { "a", "", "", "", "" };
                    var dLiterals = new[] { dEk, dRk,
                        decKeyKind.ToString(System.Globalization.CultureInfo.InvariantCulture), "2" };
                    var dRetType = dRk == "0"
                        ? "System.Linq.IGroupingResult"
                        : dRk == "1" ? "System.Int32[]"
                        : dRk == "3" ? "System.Int64[]"
                        : dRk == "4" ? "System.Double[]"
                        : dRk == "5" ? "System.Double[]" : "System.Object[]";
                    return I("il2cpp::il2cpp_linq_group_by_full_key", count, true, true, false, dCasts,
                        returnTypeName: dRetType, appendLiterals: dLiterals, argOrder: dArgOrder);
                }
            }
        }

        // delta 2: Select over a NON-decimal VALUE-TYPE source (DateTime /
        // TimeSpan / Guid / DateTimeOffset) with a DateTimeOffset result.
        // net48 Select<TSource,TResult> is generic, and every one of these
        // source structs has a by-value selector ABI plus a runtime kernel
        // (il2cpp_linq_select_<src>_dtoffset). The result is a fresh 16-byte
        // non-reference DateTimeOffset[] tracked by its exact element name.
        // A user struct source has no such ABI and falls through to the LOUD
        // gate below.
        if (listKind == null && name == "Select"
            && IsLinqValueTypeSource(genArgs, receiverType, arg0Type)
            && genArgs is { Length: >= 2 } && genArgs[1] == 0x11
            && genArgNames is { Length: >= 2 } && genArgNames[1] == "System.DateTimeOffset")
        {
            string? srcFam = LinqValueFamilyCellCode(genArgs, genArgNames, receiverType, arg0Type) switch
            {
                "12" => "dt",
                "13" => "ts",
                "14" => "guid",
                "16" => "dto",
                _ => null,
            };
            if (srcFam != null)
            {
                bool srcSelIdx = LinqSecondParamGenericArgCount(md, sigHandle) == 3;
                return I($"il2cpp::il2cpp_linq_select_{(srcSelIdx ? "idx_" : "")}{srcFam}_dtoffset",
                    2, true, true, false, TwoArg, returnTypeName: "System.DateTimeOffset[]");
            }
        }

        // impl-vtsel: OrderBy / OrderByDescending / ThenBy / ThenByDescending
        // over a DateTime / TimeSpan / Guid / DateTimeOffset ARRAY source. The
        // element crosses the key selector in its own struct slot
        // (il2cpp_linq_order_by_key_vt / _then_by_key_vt).
        if (listKind == null
            && (name is "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending")
            && IsLinqValueTypeSource(genArgs, receiverType, arg0Type))
        {
            string? ordSrcKind = LinqValueFamilyCellCode(genArgs, genArgNames, receiverType, arg0Type) switch
            {
                "12" => "13", // DateTime
                "13" => "14", // TimeSpan
                "14" => "15", // Guid
                "16" => "16", // DateTimeOffset
                _ => null,
            };
            if (ordSrcKind != null)
            {
                string? ordKeyFam = LinqVtKeyFamilyCode(genArgs, genArgNames, 1)
                    ?? throw new NotSupportedException(
                        $"Enumerable.{name} over a {LinqStructNameOf(genArgNames?[0])} array with the key " +
                        $"family '{GenericArgTypeName(genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08)}' " +
                        "is not wired: the struct-source key-selector surface covers int32/int64/double/" +
                        "float/System.DateTime/System.Guid/System.TimeSpan/System.DateTimeOffset keys");
                if (name is "ThenBy" or "ThenByDescending")
                {
                    return I(name == "ThenBy"
                            ? ResolvedMethod.ThenByCombineMarker
                            : ResolvedMethod.ThenByDescCombineMarker,
                        2, false, false, false, new[] { "", "" },
                        returnTypeName: LinqArrayTypeNameExact(genArgs![0], genArgNames, 0),
                        appendLiterals: new[] { ordKeyFam });
                }
                return I("il2cpp::il2cpp_linq_order_by_key_vt", 2, true, true, false, TwoArg,
                    returnTypeName: LinqArrayTypeNameExact(genArgs![0], genArgNames, 0),
                    appendLiterals: new[]
                    {
                        name == "OrderByDescending" ? "1" : "0", ordSrcKind, ordKeyFam,
                    });
            }
        }

        // impl-vtsel: Select over a DateTime / TimeSpan / Guid / DateTimeOffset
        // ARRAY source with ANY result family. The element crosses the selector
        // in its own by-value slot and the result is the family's typed array
        // (il2cpp_linq_select_family's srcKind/resKind literals). The narrower
        // dtoffset-result arm above keeps its dedicated entry.
        if (listKind == null && name == "Select"
            && IsLinqValueTypeSource(genArgs, receiverType, arg0Type)
            && genArgs is { Length: >= 2 })
        {
            string? srcVtKind = LinqValueFamilyCellCode(genArgs, genArgNames, receiverType, arg0Type) switch
            {
                "12" => "13", // DateTime
                "13" => "14", // TimeSpan
                "14" => "15", // Guid
                "16" => "16", // DateTimeOffset
                _ => null,
            };
            if (srcVtKind != null)
            {
                bool vtIdx = LinqSecondParamGenericArgCount(md, sigHandle) == 3;
                string vtRes = genArgs[1] switch
                {
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "0",
                    0x0A or 0x0B => "2",
                    0x0C => "3",
                    0x0D => "4",
                    0x11 => LinqValueResultSuffixWide(0x11, genArgNames, 1) switch
                    {
                        "dt" => "5",
                        "ts" => "6",
                        "guid" => "7",
                        "dtoffset" => "8",
                        _ => throw new NotSupportedException(
                            $"Enumerable.Select over a {LinqStructNameOf(genArgNames?[0])} array with " +
                            $"TResult '{GenericArgTypeName(genArgs[1])}' is not wired: the value-struct " +
                            "result carrier covers System.DateTime/System.TimeSpan/System.Guid/" +
                            "System.DateTimeOffset only (see docs/audit/b5-gaps.md)"),
                    },
                    _ => "1",
                };
                return I(vtIdx ? "il2cpp::il2cpp_linq_select_family_idx"
                               : "il2cpp::il2cpp_linq_select_family",
                    2, true, true, false, TwoArg,
                    returnTypeName: LinqArrayTypeNameExact(genArgs[1], genArgNames, 1),
                    appendLiterals: new[] { srcVtKind, vtRes });
            }
        }

        // Reference-element ARRAY source: the ref-family entries that exist.
        // A 0x11 VALUETYPE source (decimal/DateTime/TimeSpan/Guid/user struct)
        // is folded into "ref" by LinqSourceFamily but is NOT a managed
        // reference family: routing its operators through the ref kernels
        // (il2cpp_linq_first_or_default_ref etc.) emitted an Il2CppObject*
        // result into a value-typed temp -- the zmatrix decimal/DateTime/Guid
        // "cannot convert Il2CppObject*/nullptr to Il2CppDecimal/Il2CppGuid"
        // compile failures. Those operators have no value-family kernel, so
        // they must stay LOUD (the familyAgnostic gate below rejects them)
        // instead of silently binding the ref ABI.
        if (linqSrcFam == "ref" && listKind == null
            && !(srcType != null
                && srcType.StartsWith("System.Linq.IGrouping", StringComparison.Ordinal))
            && !IsLinqValueTypeSource(genArgs, receiverType, arg0Type))
        {
            switch (name)
            {
                case "Any" when count < 2:
                    // il2cpp_linq_any reads only the array header (a GroupBy
                    // bucket / result receiver is OBJECT, never an array -- the
                    // ref-array arms are skipped by the IGrouping guard above).
                    return I("il2cpp::il2cpp_linq_any", 1, true, false, false, OneArg);
                case "Any":
                    return I("il2cpp::il2cpp_linq_any_ref", 2, true, false, false, TwoArg);
                case "Count" when count >= 2:
                    return I("il2cpp::il2cpp_linq_count_ref", 2, true, false, false, TwoArg);
                case "All":
                    return I("il2cpp::il2cpp_linq_all_ref", 2, true, false, false, TwoArg);
                case "Where" when count >= 2:
                    // The index overload Where(Func<TSource,int,bool>) carries a
                    // 3-argument Func -- route it to the reference index kernel
                    // (il2cpp_linq_where_idx_ref: bool(*)(Il2CppObject*, int32_t)).
                    if (LinqSecondParamGenericArgCount(md, sigHandle) == 3)
                        return I("il2cpp::il2cpp_linq_where_idx_ref", 2, true, true, false, TwoArg,
                            returnTypeName: LinqRefArrayTypeName(genArgs, srcType));
                    return I("il2cpp::il2cpp_linq_where_ref", 2, true, true, false, TwoArg,
                        returnTypeName: LinqRefArrayTypeName(genArgs, srcType));
                case "Select":
                {
                    // The index overload Select(Func<TSource,int,TResult>) carries
                    // a 3-argument Func. A reference TResult routes to the
                    // reference index kernel (il2cpp_linq_select_idx_ref_ref:
                    // Il2CppObject*(*)(Il2CppObject*, int32_t)); a VALUE TResult
                    // routes to the reference index value kernel
                    // (il2cpp_linq_select_idx_ref_i4/_i8/_d/_f).
                    if (LinqSecondParamGenericArgCount(md, sigHandle) == 3)
                    {
                        if (genArgs is not { Length: >= 2 })
                            return null;
                        if (IsRefFamilyByte(genArgs[1]))
                            return I("il2cpp::il2cpp_linq_select_idx_ref_ref", 2, true, true, false, TwoArg,
                                returnTypeName: LinqRefArrayTypeName(new[] { genArgs[1] }, null));
                        // impl-linqleft: a DECIMAL result over a reference
                        // source with the index selector (net48
                        // Select<TSource,int,decimal>); the 0x11 byte is
                        // name-resolved.
                        if (genArgs[1] == 0x11 && IsDecimalGenArg(genArgNames, 1))
                            return I("il2cpp::il2cpp_linq_select_idx_ref_dec", 2, true, true, false, TwoArg,
                                returnTypeName: "System.Decimal[]");
                        string? idxVs = LinqValueResultSuffixWide(genArgs[1], genArgNames, 1);
                        if (idxVs is null)
                            throw new NotSupportedException(
                                $"Enumerable.Select<{GenericArgTypeName(genArgs[0])},{GenericArgTypeName(genArgs[1])}> " +
                                "with a (element, index) selector over a reference-element source is not wired: " +
                                "the reference index-selector surface covers a REFERENCE TResult, the " +
                                "int32/int64/double/float VALUE results and the DateTime/TimeSpan/Guid value results");
                        return I($"il2cpp::il2cpp_linq_select_idx_ref_{idxVs}", 2, true, true, false, TwoArg,
                            returnTypeName: LinqValueResultArrayName(idxVs));
                    }
                    if (genArgs is not { Length: >= 2 })
                        return null;
                    // p0linq4 (D3): a DECIMAL result over a reference source --
                    // the ref -> decimal kernel (il2cpp_linq_select_ref_dec).
                    // The 0x11 byte covers every value type, so the decoded
                    // MethodSpec FULL NAME is the only authority for decimal.
                    if (genArgs[1] == 0x11 && IsDecimalGenArg(genArgNames, 1))
                        return I("il2cpp::il2cpp_linq_select_ref_dec", 2, true, true, false, TwoArg,
                            returnTypeName: "System.Decimal[]");
                    if (IsRefFamilyByte(genArgs[1]))
                        return I("il2cpp::il2cpp_linq_select_ref_ref", 2, true, true, false, TwoArg,
                            returnTypeName: LinqRefArrayTypeName(new[] { genArgs[1] }, null));
                    // A VALUE TResult over a reference source (net48
                    // Select<TSource,TResult>): int32 / int64 / double / float
                    // and the DateTime/TimeSpan/Guid value structs (the 0x11
                    // byte is name-resolved by LinqValueResultSuffixWide).
                    string? vs = LinqValueResultSuffixWide(genArgs[1], genArgNames, 1);
                    if (vs is null)
                        throw new NotSupportedException(
                            $"Enumerable.Select<{GenericArgTypeName(genArgs[0])},{GenericArgTypeName(genArgs[1])}> " +
                            "not wired: the runtime reference-source select surface is ref -> ref " +
                            "(il2cpp_linq_select_ref_ref), ref -> decimal (il2cpp_linq_select_ref_dec), " +
                            "ref -> int32/int64/double/float (il2cpp_linq_select_ref_i4/_i8/_d/_f) and " +
                            "ref -> DateTime/TimeSpan/Guid (il2cpp_linq_select_ref_dt/_ts/_guid)");
                    return I($"il2cpp::il2cpp_linq_select_ref_{vs}", 2, true, true, false, TwoArg,
                        returnTypeName: LinqValueResultArrayName(vs));
                }
                case "First" when count < 2:
                    return I("il2cpp::il2cpp_linq_first_ref", 1, true, true, false, OneArg,
                        returnTypeName: LinqRefElementTypeName(genArgs, srcType));
                // impl-linq-vtpred: the reference-source PREDICATE forms of
                // First / FirstOrDefault. The i4 entries (first_predicate /
                // first_or_default_predicate) call the predicate through the
                // int32 ABI; a string[]/object[]/class[] source needs the
                // reference ABI (bool(*)(Il2CppObject*)). FirstOrDefault
                // answers nullptr for an empty / no-match source.
                case "First":
                    return I("il2cpp::il2cpp_linq_first_pred_ref", 2, true, true, false, TwoArg,
                        returnTypeName: LinqRefElementTypeName(genArgs, srcType));
                case "FirstOrDefault" when count < 2:
                    return I("il2cpp::il2cpp_linq_first_or_default_ref", 1, true, true, false, OneArg,
                        returnTypeName: LinqRefElementTypeName(genArgs, srcType));
                case "FirstOrDefault":
                    return I("il2cpp::il2cpp_linq_first_or_default_pred_ref", 2, true, true, false, TwoArg,
                        returnTypeName: LinqRefElementTypeName(genArgs, srcType));
                case "Last" when count < 2:
                    return I("il2cpp::il2cpp_linq_last_ref", 1, true, true, false, OneArg,
                        returnTypeName: LinqRefElementTypeName(genArgs, srcType));
                case "Last" when count >= 2:
                    return I("il2cpp::il2cpp_linq_last_pred_ref", 2, true, true, false, TwoArg,
                        returnTypeName: LinqRefElementTypeName(genArgs, srcType));
                case "LastOrDefault" when count < 2:
                    return I("il2cpp::il2cpp_linq_last_or_default_ref", 1, true, true, false, OneArg,
                        returnTypeName: LinqRefElementTypeName(genArgs, srcType));
                case "LastOrDefault" when count >= 2:
                    return I("il2cpp::il2cpp_linq_last_or_default_pred_ref", 2, true, true, false, TwoArg,
                        returnTypeName: LinqRefElementTypeName(genArgs, srcType));
                // D-LINQ-FAM (source-only): Single / SingleOrDefault over a
                // REFERENCE-element array source. The family kernel reads the
                // ref cell and the result rides the Il2CppObject* ABI. The
                // predicate overloads (count >= 2) have no index-aware ref
                // kernel and stay LOUD.
                case "Single" when count < 2:
                    return I("il2cpp::il2cpp_linq_single_fam_ref", 1, true, true, false, OneArg,
                        returnTypeName: LinqRefElementTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "SingleOrDefault" when count < 2:
                    return I("il2cpp::il2cpp_linq_single_or_default_fam_ref", 1, true, true, false, OneArg,
                        returnTypeName: LinqRefElementTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                // The predicate forms (count >= 2): net48 Single(predicate) /
                // SingleOrDefault(predicate) walk every element and count the
                // matches; the reference result rides the Il2CppObject* ABI.
                case "Single":
                    return I("il2cpp::il2cpp_linq_single_pred_ref", 2, true, true, false, TwoArg,
                        returnTypeName: LinqRefElementTypeName(genArgs, srcType));
                case "SingleOrDefault":
                    return I("il2cpp::il2cpp_linq_single_or_default_pred_ref", 2, true, true, false, TwoArg,
                        returnTypeName: LinqRefElementTypeName(genArgs, srcType));
                // B5-R2: OrderBy / OrderByDescending over a REFERENCE source.
                // Enumerable.OrderBy passes a 1-arg key selector, so it must NOT
                // go through il2cpp_linq_order_by_ref/string (2-arg Comparison<T>
                // -- calling a 1-arg function with 2 args is UB). The TKey family
                // picks the entry suffix; the result is a NEW reference array of
                // the same element type.
                case "OrderBy" when count >= 2:
                case "OrderByDescending" when count >= 2:
                {
                    if (genArgs is not { Length: >= 2 })
                        return null;
                    if (LinqSecondParamGenericArgCount(md, sigHandle) == 3)
                        throw new NotSupportedException(
                            "Enumerable.OrderBy with a (element, index) key selector over a reference-element " +
                            "source is not wired: LOUD whitelist �?-- net48 has no (element, index) key " +
                            "selector overload. MS Learn Enumerable.OrderBy (view=netframework-4.8.1) and the " +
                            "net48 Reference Source System.Core/System/Linq/Enumerable.cs list only " +
                            "OrderBy<TSource,TKey>(IEnumerable<TSource>, Func<TSource,TKey>) and " +
                            "OrderBy<TSource,TKey>(..., IComparer<TKey>); the (element, index) shape is a " +
                            ".NET 9 Index() family form (see docs/audit/b5-gaps.md)");
                    string keySuffix = LinqKeyFamilySuffixNamed(genArgs[1], genArgNames, 1);
                    // b5-gaps section 56: OrderBy(source, keySelector, comparer):
                    // ANY reference TKey rides the user IComparer (the key
                    // family suffix is irrelevant -- the comparer decides).
                    if (count >= 3)
                    {
                        return I("il2cpp::il2cpp_linq_order_by_key_ref_cmp", 3, true, true, false,
                            new[] { "a", "", "" },
                            returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                            appendLiterals: new[] { name == "OrderBy" ? "0" : "1" });
                    }
                    // impl-collation: a STATICALLY System.String key (0x0E) is a
                    // real, statically analysable statement now. It binds the
                    // dedicated culture-collation entry, which pins
                    // Comparer<string>.Default (CultureInfo.CurrentCulture,
                    // CompareOptions.None) at the call site instead of letting a
                    // string key drift into the reference/IComparable dispatch.
                    if (genArgs[1] == 0x0E)
                        keySuffix = "string";
                    if (keySuffix.Length == 0)
                        throw new NotSupportedException(
                            $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])},{GenericArgTypeName(genArgs[1])}> " +
                            "not wired: the reference-source key-selector surface covers int32/int64/" +
                            "double/float, reference and System.DateTime/System.Guid keys only");
                    // The desc flag is an APPENDED literal (0/1), never a
                    // consumed IL argument -- ParamCount stays the IL arity.
                    return I($"il2cpp::il2cpp_linq_order_by_key_ref_{keySuffix}", 2, true, true, false,
                        TwoArg,
                        returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { name == "OrderBy" ? "0" : "1" });
                }
                // impl-collation: ThenBy / ThenByDescending over a
                // REFERENCE-element source. The chained sorter is fused by
                // StackFold into il2cpp_linq_then_by_key_ref (the receiver must
                // be an OrderBy(keySelector) assignment, exactly as for the
                // numeric composites). The marker carries the SECONDARY key
                // family code: 4 = System.String (culture collation), 5 = any
                // other reference (instance dispatch -- a runtime string key
                // still gets culture collation, everything else defers to
                // IComparable.CompareTo). A value-type secondary key over a
                // reference source has no reference composite reader, so it
                // stays LOUD.
                case "ThenBy":
                case "ThenByDescending":
                {
                    if (genArgs is not { Length: >= 2 })
                        return null;
                    // The reference composite reads the SECONDARY key in its own
                    // family slot: 0=int32, 1=int64, 2=double, 3=float,
                    // 4=System.String (culture collation), 5=other reference
                    // (instance dispatch). Both keys are cached once each.
                    string? refK2 = genArgs[1] == 0x0E ? "4"
                        : IsRefFamilyByte(genArgs[1]) ? "5"
                        : LinqValKeyFamilyCode(genArgs[1]);
                    if (refK2 is null)
                        throw new NotSupportedException(
                            $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])}," +
                            $"{GenericArgTypeName(genArgs[1])}> over a reference-element source is not " +
                            "wired: the reference composite covers int32/int64/double/float, " +
                            "System.String or other reference secondary keys " +
                            "(il2cpp_linq_then_by_key_ref)");
                    return I(name == "ThenBy"
                            ? ResolvedMethod.ThenByCombineMarker
                            : ResolvedMethod.ThenByDescCombineMarker,
                        2, false, false, false, new[] { "", "" },
                        returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { refK2 });
                }
                // D-LINQ-FAM: the family-generic element operators over a
                // REFERENCE source (string[]/object[]/class[]). The runtime
                // ships ONE kernel per operator (linq_family.h) with the cell
                // code as the trailing literal; the reference cell is
                // IL2CPP_LINQ_CELL_REF (15). The result is a fresh reference
                // array -- the same null-class ref array the existing ref
                // kernels allocate (linq.cpp), so downstream ref operators
                // resolve on it.
                case "Take":
                    return I("il2cpp::il2cpp_linq_take_family", 2, true, true, false,
                        new[] { "a", "i" }, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "Skip":
                    return I("il2cpp::il2cpp_linq_skip_family", 2, true, true, false,
                        new[] { "a", "i" }, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "Reverse":
                    return I("il2cpp::il2cpp_linq_reverse_family", 1, true, true, false,
                        OneArg, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "Concat":
                    return I("il2cpp::il2cpp_linq_concat_family", 2, true, true, false,
                        new[] { "a", "a" }, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "Distinct" when count < 2:
                    return I("il2cpp::il2cpp_linq_distinct_family", 1, true, true, false,
                        OneArg, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "Distinct":
                    // b5-gaps section 56: Distinct(source, comparer) -- the
                    // user comparer rides the last IL slot; the result keeps
                    // FIRST-APPEARANCE order (net48).
                    return I("il2cpp::il2cpp_linq_distinct_family_cmp", 2, true, true, false,
                        new[] { "a", "" }, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "Except" when count < 3:
                    return I("il2cpp::il2cpp_linq_except_family", 2, true, true, false,
                        new[] { "a", "a" }, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "Except":
                    return I("il2cpp::il2cpp_linq_except_family_cmp", 3, true, true, false,
                        new[] { "a", "a", "" }, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "Union" when count < 3:
                    return I("il2cpp::il2cpp_linq_union_family", 2, true, true, false,
                        new[] { "a", "a" }, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "Union":
                    return I("il2cpp::il2cpp_linq_union_family_cmp", 3, true, true, false,
                        new[] { "a", "a", "" }, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "Intersect" when count < 3:
                    return I("il2cpp::il2cpp_linq_intersect_family", 2, true, true, false,
                        new[] { "a", "a" }, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "Intersect":
                    return I("il2cpp::il2cpp_linq_intersect_family_cmp", 3, true, true, false,
                        new[] { "a", "a", "" }, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "DefaultIfEmpty" when count < 2:
                    return I("il2cpp::il2cpp_linq_default_if_empty_family", 1, true, true, false,
                        OneArg, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "DefaultIfEmpty":
                    // DefaultIfEmpty(source, defaultValue) over a reference-
                    // element source: non-empty -> the source as-is; empty ->
                    // a one-element array holding `value` (the reference slot).
                    return I("il2cpp::il2cpp_linq_default_if_empty_family_ref", 2, true, true, false,
                        new[] { "a", "" }, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "LongCount" when count < 2:
                    return I("il2cpp::il2cpp_linq_long_count_family", 1, true, false, false,
                        OneArg, resultKind: "l", appendLiterals: new[] { "15" });
                case "SequenceEqual" when count < 3:
                    return I("il2cpp::il2cpp_linq_sequence_equal_family", 2, true, false, false,
                        new[] { "a", "a" }, appendLiterals: new[] { "15" });
                case "SequenceEqual":
                    // b5-gaps section 56: SequenceEqual(source, second, comparer).
                    return I("il2cpp::il2cpp_linq_sequence_equal_family_cmp", 3, true, false, false,
                        new[] { "a", "a", "" }, appendLiterals: new[] { "15" });
                case "Contains" when count < 3:
                    return I("il2cpp::il2cpp_linq_contains_family_ref", 2, true, false, false,
                        new[] { "a", "" }, appendLiterals: new[] { "15" });
                case "Contains":
                    // b5-gaps section 56: Contains(source, value, comparer) --
                    // the comparer rides the last IL slot (never the value's).
                    return I("il2cpp::il2cpp_linq_contains_family_ref_cmp", 3, true, false, false,
                        new[] { "a", "", "" }, appendLiterals: new[] { "15" });
                case "ElementAt":
                    return I("il2cpp::il2cpp_linq_element_at_fam_ref", 2, true, true, false,
                        new[] { "a", "i" },
                        returnTypeName: LinqRefElementTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                // p0grp: GroupBy over a REFERENCE-element array source. The
                // grouping kernel reads either int32 cells (BuildGroups /
                // I4At) or 16-byte System.Decimal cells
                // (il2cpp_linq_group_by_dec_key), so a string[]/object[]/
                // class[] source has no reader at all. Rejecting it HERE keeps
                // the message self-describing: without this arm the call fell
                // through to the int32-only gate whose list did not name
                // GroupBy, and the resolver reported the phantom "generic
                // instantiation of System.Linq.Enumerable.GroupBy has no BCL
                // mapping in M5" instead of the real reason.
                case "GroupBy":
                {
                    // p0grp: GroupBy over a REFERENCE-element array source. The
                    // runtime grouping kernel reads int32 cells (BuildGroups /
                    // I4At), 16-byte System.Decimal cells
                    // (il2cpp_linq_group_by_dec_key) OR reference cells
                    // (elemKind 1 -- BuildGroups' Ref family), so the
                    // reference-element family is served here.
                    if (genArgs is not { Length: >= 2 })
                        return null;
                    int refKeyKind = KeyKindForNamed(genArgs[1], genArgNames, 1);
                    if (refKeyKind < 0)
                    {
                        string refElemArgType = GenericArgTypeName(genArgs[0]);
                        string refKeyArgType = genArgNames is { Length: >= 2 } && genArgNames[1] != "?"
                            ? genArgNames[1] : GenericArgTypeName(genArgs[1]);
                        throw new NotSupportedException(
                            $"Enumerable.GroupBy<{refElemArgType},{refKeyArgType}> over a reference-element " +
                            "source is not wired: the reference-source grouping surface buckets by a key " +
                            "carrier of int32-slot (int/bool/char/sbyte/byte/short/ushort/uint), int64 " +
                            "(long/ulong) or 16-byte struct (System.Decimal/System.DateTime/System.Guid); " +
                            $"{DescribeRejectedKey(genArgs[1], refKeyArgType)} has no runtime key carrier " +
                            "(see docs/audit/b5-gaps.md)");
                    }
                    var refParams = LinqEncodedParamTypes(md, sigHandle);
                    int refElemArg = -1, refCmpArg = -1, refResArg = -1;
                    for (int pi = 2; pi < refParams.Length; pi++)
                    {
                        var rdef = GenericDefNameOf(refParams[pi]);
                        if (rdef == "System.Func`2") refElemArg = pi;
                        else if (rdef == "System.Func`3") refResArg = pi;
                        else if (rdef == "System.Collections.Generic.IEqualityComparer`1") refCmpArg = pi;
                    }
                    byte? refElemByte = refElemArg >= 0 && genArgs.Length >= 3 ? genArgs[2] : null;
                    byte? refResByte = refResArg >= 0 && genArgs.Length >= 3
                        ? (genArgs.Length >= 4 ? genArgs[3] : genArgs[2]) : null;
                    string RefElemFamily(byte b) => b switch
                    {
                        0x08 => "0",
                        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "0",
                        0x0A or 0x0B => "2",
                        0x0D => "3",
                        // floatgrp: a System.Single bucket element (4-byte cell).
                        0x0C => "7",
                        0x0E or 0x12 or 0x1C or 0x1D or 0x15 or 0x13 or 0x1E => "1",
                        _ => "",
                    };
                    string RefResultFamily(byte b) => b switch
                    {
                        0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "1",
                        0x0A or 0x0B => "3",
                        0x0D => "4",
                        0x0C => "5",
                        _ => "2",
                    };
                    string refEk = refElemByte is { } reb ? RefElemFamily(reb) : "1";
                    if (refEk.Length == 0)
                        throw new NotSupportedException(
                            $"Enumerable.GroupBy TElement '{GenericArgTypeName(refElemByte!.Value)}' " +
                            "is not a supported element family");
                    string refRk = refResByte is { } rrb ? RefResultFamily(rrb) : "0";
                    int refElemIdx = refElemArg >= 0 ? refElemArg : -1;
                    int refCmpIdx = refCmpArg >= 0 ? refCmpArg : -1;
                    int refResIdx = refResArg >= 0 ? refResArg : -1;
                    var refArgOrder = new[] { 0, 1, refElemIdx, refCmpIdx, refResIdx };
                    var refCasts = new[] { "a", "", "", "", "" };
                    var refLiterals = new[] { refEk, refRk,
                        refKeyKind.ToString(System.Globalization.CultureInfo.InvariantCulture), "1" };
                    var refRetType = refRk == "0"
                        ? "System.Linq.IGroupingResult"
                        : refRk == "1" ? "System.Int32[]"
                        : refRk == "3" ? "System.Int64[]"
                        : refRk == "4" ? "System.Double[]"
                        : refRk == "5" ? "System.Double[]" : "System.Object[]";
                    return I("il2cpp::il2cpp_linq_group_by_full_key", count, true, true, false, refCasts,
                        returnTypeName: refRetType, appendLiterals: refLiterals,
                        argOrder: refArgOrder);
                }
                default:
                    break;
            }
        }

        // b5-gaps section 56/57: a user-comparer call that reached PAST the arms
        // above (Reference-element siblings / GroupBy consume their comparer)
        // has no wired kernel here UNLESS it is a section-57 VALUE array
        // operator (i4/i8/f/d/narrow Distinct/Except/Union/Intersect/
        // Contains/SequenceEqual -- those fall through to the numeric family
        // blocks, which run their comparer-twinned arms). Everything else
        // would silently DROP the comparer, so it must go LOUD (see
        // docs/audit/b5-gaps.md).
        bool isValueArrayCmpOp = listKind == null
            && (linqSrcFam is "i4" or "i8" or "f" or "d" or "narrow")
            && name is "Distinct" or "Except" or "Union" or "Intersect"
                or "Contains" or "SequenceEqual"
                // b5-gaps section 71: OrderBy / OrderByDescending over a VALUE
                // source accepts the user key comparer -- the value-source
                // arms below (LinqOrderByKeyEntry and TryMapLinqWide) emit the
                // *_cmp / *_ref_cmp comparator kernels.
                or "OrderBy" or "OrderByDescending";
        if (name != "GroupBy" && LinqHasComparerParam(md, sigHandle) && !isValueArrayCmpOp)
        {
            throw new NotSupportedException(
                $"Enumerable.{name} over this source with a user comparer is not wired yet: " +
                "the plain kernel would silently drop the comparer (b5-gaps section 56 " +
                "pending value-family / List comparer kernels).");
        }

        // Wide / narrow VALUE-element ARRAY source (int64 / float / double /
        // byte / sbyte / short / ushort / char / bool / uint). Each entry reads
        // the cell at its REAL width; a narrow cell widens into the int32
        // delegate slot (the CLR's parameter rule) -- never a 4-byte over-read.
        if (listKind == null && LinqWideNarrowKind(linqSrcFam, genArgs) is { } wk)
        {
            var wideDispatch = TryMapLinqWide(md, sigHandle, name, count, genArgs, wk, linqSrcFam,
                TwoArg, OneArg, genArgNames);
            if (wideDispatch != null) return wideDispatch;
        }

        // The generic operators (Where/Select/Count/Any/All/First/FirstOr-
        // Default/ToArray/OrderBy) all carry a MethodSpec with the int32
        // element byte. A non-int32 generic argument (or a non-generic call)
        // has no stage-1 runtime entry. ToList is EXCLUDED here -- its own
        // case carries the precise "List<int32> only" LOUD message.
        //
        // nb9 fix P0-4: Count / ToArray / Sum / Min / Max / Average /
        // Aggregate are ELEMENT-FAMILY AGNOSTIC in their storage handling
        // (count reads the array header, ToArray clones the array by its own
        // elemSize/isRefElem, and the aggregate kernels have per-family
        // entries), so they are excluded from the int32-only gate and served
        // below. Everything else still rejects a non-int32 source.
        // P0-11: Any() reads only the array header too -- element-family
        // agnostic (the ref-source predicate form is dispatched above).
        // B5-R5: Zip is family-agnostic as well -- both of its sources carry
        // their OWN family (the wide/narrow arm reads each at its own width) and
        // a reference source is rejected with a precise message there. The
        // int32-only gate below would otherwise reject every narrow/wide Zip
        // before that arm can run.
        // p0linq4 (D4): ThenBy / ThenByDescending are family-agnostic in the
        // SAME sense -- their own arm below validates the (element, key) pair
        // (int32 elements with int32 keys, int64 elements with int32/int64
        // keys) and returns the combine marker. The int32-only gate here would
        // otherwise reject every int64-source ThenBy before that arm runs.
        bool familyAgnostic = name is "Count" or "ToArray" or "Sum" or "Min" or "Max"
            or "Average" or "Aggregate" or "Any" or "Zip" or "ThenBy" or "ThenByDescending";
        if (!familyAgnostic && name != "ToList" &&
            (genArgs is not { Length: >= 1 } || genArgs[0] != 0x08))
        {
            // The generic argument name for the LOUD message. The guard above
            // enters this block for a null/empty genArgs too (a plain
            // MemberRef call to a generic operator -- no MethodSpec), so the
            // null-safe read must not dereference genArgs.
            string argType = genArgs is { Length: > 0 } ? GenericArgTypeName(genArgs[0]) : "?";
            return name switch
            {
                "Count" or "Any" or "Where" or "Select" or "OrderBy" or "OrderByDescending" or "First" or "FirstOrDefault" or "ToArray" or "All" or "Take" or "Skip" or "TakeWhile" or "SkipWhile" or "Concat" or "Distinct" or "Except" or "Zip" or "Average" or "Reverse" or "Last" or "LastOrDefault" or "ElementAt" or "Contains" or "Aggregate" or "LongCount" or "SequenceEqual" or "DefaultIfEmpty" or "GroupBy" =>
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{argType}> not wired: " +
                        "the stage-1 runtime LINQ surface is int32 (0x08) element-family for this operator " +
                        "(the wired entries are the int32 family, the reference-source " +
                        "Any/Count/Where/Select/First/FirstOrDefault, and the DateTime/TimeSpan/Guid " +
                        "predicate + First/Last entries)"),
                _ => null,
            };
        }

        switch (name)
        {
            case "ToList":
                // il2cpp_linq_to_list materializes List<int32>; the result is
                // tracked as a List`1 reference so downstream List members
                // (get_Count / get_Item) resolve through MethodMap.List.
                //
                // P0LINQ1 (D1): net48's Enumerable.ToList<TSource> is generic
                // in TSource, so a NON-int32 source must materialize the
                // matching List<T> instead of being rejected. The runtime has
                // one entry per implemented element family, each carrying the
                // listobj domain (the same List<T> object the reference/value
                // List surface already uses):
                //   ref (string/object/class[]) -> il2cpp_linq_to_list_ref
                //   int64  -> il2cpp_linq_to_list_i8
                //   double -> il2cpp_linq_to_list_d
                //   float  -> il2cpp_linq_to_list_f
                // The tracked return type is the CONCRETE List`1_<elem>
                // instantiation name so MethodMap.List's receiver gate selects
                // the right element domain (the bare "List`1" pseudo-type only
                // serves the unsuffixed int32 case). Any other element family
                // (a narrow integral, an enum) stays LOUD.
                //
                // p0linq4 (D1): the remaining VALUE families are now wired too.
                // The listobj storage is a reference array, so a 16-byte decimal
                // CANNOT ride an element slot -- every value element is BOXED on
                // entry and unboxed on exit (listobj.h):
                //   decimal  -> il2cpp_linq_to_list_dec   (icall_box_decimal)
                //   DateTime -> il2cpp_linq_to_list_dt    (Int64 box of the CLR
                //               array image: ticks + 2-bit kind)
                //   TimeSpan -> il2cpp_linq_to_list_ts    (Int64 box of the ticks)
                //   user struct -> il2cpp_linq_to_list_struct (icall_box_user_struct
                //               with the struct's OWN descriptor + sizeof(T))
                // The tracked List name carries the element so MethodMap.List
                // dispatches the boxed arms.
                if (genArgs is not { Length: >= 1 })
                    return null;
                switch (genArgs[0])
                {
                    case 0x08:
                        return I("il2cpp::il2cpp_linq_to_list", 1, true, true, false,
                            OneArg, returnTypeName: "System.Collections.Generic.List`1");
                    case 0x0A:
                    case 0x0B:
                        return I("il2cpp::il2cpp_linq_to_list_i8", 1, true, true, false,
                            OneArg, returnTypeName: "System.Collections.Generic.List`1_long");
                    case 0x0C:
                        return I("il2cpp::il2cpp_linq_to_list_f", 1, true, true, false,
                            OneArg, returnTypeName: "System.Collections.Generic.List`1_float");
                    case 0x0D:
                        return I("il2cpp::il2cpp_linq_to_list_d", 1, true, true, false,
                            OneArg, returnTypeName: "System.Collections.Generic.List`1_double");
                    case 0x0E:
                        return I("il2cpp::il2cpp_linq_to_list_ref", 1, true, true, false,
                            OneArg, returnTypeName: "System.Collections.Generic.List`1_string");
                    case 0x1C:
                        return I("il2cpp::il2cpp_linq_to_list_ref", 1, true, true, false,
                            OneArg, returnTypeName: "System.Collections.Generic.List`1_object");
                    case 0x12:
                        // A CLASS element is a managed reference: the listobj
                        // domain carries it. The MethodSpec full name (when
                        // decoded) gives the EXACT element type so downstream
                        // member calls resolve on it (DottedRefArg rule). Only a
                        // SIMPLE dotted name is tracked: a nested ('+') or
                        // generic ('`') name has no listobj element tag and
                        // would emit an unresolvable receiver hint, so those
                        // fall back to the generic reference-element hint.
                        return I("il2cpp::il2cpp_linq_to_list_ref", 1, true, true, false,
                            OneArg, returnTypeName: genArgNames is { Length: >= 1 }
                                && genArgNames[0] is { } toListElem
                                && toListElem.Contains('.')
                                && !toListElem.Contains('+') && !toListElem.Contains('`')
                                ? "System.Collections.Generic.List`1_" + toListElem
                                : "System.Collections.Generic.List`1_class");
                    case 0x15 or 0x1D or 0x14:
                        // A generic-instance / array element is a managed
                        // reference too; the element type is tracked as
                        // System.Object (no per-instantiation List domain).
                        return I("il2cpp::il2cpp_linq_to_list_ref", 1, true, true, false,
                            OneArg, returnTypeName: "System.Collections.Generic.List`1_object");
                    case 0x11:
                    {
                        // A VALUE type: the 0x11 byte covers decimal, DateTime,
                        // TimeSpan, Guid, DateTimeOffset and every user struct --
                        // the decoded MethodSpec FULL NAME is the only authority.
                        string? elemFull = genArgNames is { Length: >= 1 } ? genArgNames[0] : null;
                        switch (elemFull)
                        {
                            case "System.Decimal":
                                return I("il2cpp::il2cpp_linq_to_list_dec", 1, true, true, false,
                                    OneArg, returnTypeName: "System.Collections.Generic.List`1_System.Decimal");
                            case "System.DateTime":
                                return I("il2cpp::il2cpp_linq_to_list_dt", 1, true, true, false,
                                    OneArg, returnTypeName: "System.Collections.Generic.List`1_System.DateTime");
                            case "System.TimeSpan":
                                return I("il2cpp::il2cpp_linq_to_list_ts", 1, true, true, false,
                                    OneArg, returnTypeName: "System.Collections.Generic.List`1_System.TimeSpan");
                            case "System.Guid":
                                return I("il2cpp::il2cpp_linq_to_list_guid", 1, true, true, false,
                                    OneArg, returnTypeName: "System.Collections.Generic.List`1_System.Guid");
                            case "System.DateTimeOffset":
                                // delta 2: the 16-byte DateTimeOffset value is
                                // boxed into the listobj storage through
                                // icall_box_date_time_offset; the tracked List
                                // name carries the element so MethodMap.List
                                // dispatches the dtoffset family.
                                return I("il2cpp::il2cpp_linq_to_list_dtoffset", 1, true, true, false,
                                    OneArg, returnTypeName: "System.Collections.Generic.List`1_System.DateTimeOffset");
                        }
                        // A USER STRUCT (a value type defined in this assembly):
                        // the box needs the struct's own generated descriptor and
                        // its payload size, both of which only exist for a
                        // translated user value type.
                        if (elemFull != null && TypeEmitter.SessionTypes is { } knownTypes)
                        {
                            var elemDef = knownTypes.FirstOrDefault(t => t.MatchesFullName(elemFull));
                            if (elemDef != null && elemDef.IsValueType
                                && elemDef.BaseTypeName != "System.Enum")
                            {
                                TypeEmitter.LayoutInstanceFields(elemDef);
                                string elemCpp = TypeEmitter.TypeCppName(elemDef);
                                return I("il2cpp::il2cpp_linq_to_list_struct", 1, true, true, false,
                                    OneArg,
                                    returnTypeName: "System.Collections.Generic.List`1_" + elemFull,
                                    appendLiterals: new[]
                                    {
                                        $"&{elemCpp}_class",
                                        $"static_cast<int32_t>(sizeof({elemCpp}))",
                                    });
                            }
                        }
                        throw new NotSupportedException(
                            $"Enumerable.ToList<{GenericArgTypeName(genArgs[0])}> not wired: the runtime " +
                            "materializes List<T> over the reference/int64/double/float families " +
                            "(il2cpp_linq_to_list_ref/_i8/_d/_f), the 16-byte decimal family " +
                            "(il2cpp_linq_to_list_dec) and DateTime/TimeSpan/user-struct elements " +
                            "(il2cpp_linq_to_list_dt/_ts/_struct) through the listobj domain (boxed " +
                            "elements); this value type has no List<T> carrier");
                    }
                    default:
                        throw new NotSupportedException(
                            $"Enumerable.ToList<{GenericArgTypeName(genArgs[0])}> not wired: the runtime materializes " +
                            "List<int32> (il2cpp_linq_to_list / the listi4 domain) and List<T> over the reference, " +
                            "int64, double and float element families (il2cpp_linq_to_list_ref/_i8/_d/_f -- the " +
                            "listobj domain, linq_map.h), plus decimal/DateTime/TimeSpan/user-struct elements " +
                            "(_dec/_dt/_ts/_struct); no List<T> storage domain exists for this element type");
                }
            case "Count":
                // D-LINQC-1: Enumerable.Count over a List<int32> receiver (the
                // il2cpp_linq_to_list materializer -- its result is tracked as
                // the "System.Collections.Generic.List`1" pseudo-type by the
                // ToList ReturnTypeName; a new List<int32> newobj is the AOT
                // expansion name "System.Collections.Generic.List`1_int") must
                // route to the listi4 get_Count icall. il2cpp_linq_count takes
                // Il2CppArray* -- the emitter's "a" cast would
                // reinterpret_cast the List OBJECT as an array and read the
                // List's memory as an array header (a wrong/garbage count).
                // net48's Enumerable.Count fast-path returns
                // ICollection<T>.Count for a List -- exactly the listi4 count.
                var countRecv = receiverType ?? arg0Type;
                if (count < 2 && countRecv is "System.Collections.Generic.List`1"
                        or "System.Collections.Generic.List`1_int")
                    return I("il2cpp::il2cpp_list_i4_get_count", 1, true, false, false, new[] { "" });
                // D-LINQ-4 grouping counts: per-bucket (IGrouping) and whole
                // result (IGroupingResult). OBJECT receivers (cast [""]).
                if (count < 2 && countRecv == "System.Linq.IGrouping")
                    return I("il2cpp::il2cpp_linq_grouping_get_count", 1, true, false, false, new[] { "" });
                if (count < 2 && countRecv == "System.Linq.IGroupingResult")
                    return I("il2cpp::il2cpp_linq_grouping_result_get_count", 1, true, false, false, new[] { "" });
                // b5-gaps §55: a receiver whose tracked type is System.Object
                // (a GroupBy bucket/result element inside a lambda) has no
                // grouping tag, yet the runtime object IS a grouping. Dispatch
                // on the runtime class instead of reinterpreting it as an
                // array header.
                if (count < 2
                    && (countRecv is "System.Object" or null or "System.Collections.IEnumerable"
                        || (countRecv != null
                            && countRecv.StartsWith("System.Linq.IGrouping", StringComparison.Ordinal))))
                    return I("il2cpp::il2cpp_linq_count_object", 1, true, false, false, new[] { "" });
                // impl-linqfix: the PREDICATE form of Count over a VALUE-TYPE
                // source (DateTime / TimeSpan / Guid / a user struct) must not
                // ride the int32 predicate kernel: il2cpp_linq_count_i4 calls
                // the predicate through the int32 ABI and reads 4-byte cells
                // out of a source whose payload is 8/16 bytes wide -- a SILENT
                // wrong count. The runtime has no predicate kernel for those
                // families (the System.Decimal source has its own count_pred_dec
                // entry), so the operator stays LOUD.
                if (count >= 2 && IsLinqNonDecimalValueTypeSource(genArgs, genArgNames, receiverType, arg0Type))
                    throw new NotSupportedException(
                        $"Enumerable.Count with a predicate over a {ValueTypeSourceName(genArgs, genArgNames, receiverType, arg0Type)} source is not wired: " +
                        "the runtime has no predicate kernel for a non-decimal value-type element family " +
                        "(il2cpp_linq_count_i4 reads 4-byte int32 cells and calls the predicate through the int32 ABI)");
                return count >= 2
                    ? I("il2cpp::il2cpp_linq_count_i4", 2, true, false, false, TwoArg)
                    : I("il2cpp::il2cpp_linq_count", 1, true, false, false, OneArg);
            case "Any":
                // Any() / Any(predicate) -- bool result (int32 slot).
                // impl-linqfix: the PREDICATE form over a non-decimal
                // VALUE-TYPE source stays LOUD (see the Count arm above).
                var anyRecv = receiverType ?? arg0Type;
                if (count >= 2 && IsLinqNonDecimalValueTypeSource(genArgs, genArgNames, receiverType, arg0Type))
                    throw new NotSupportedException(
                        $"Enumerable.Any with a predicate over a {ValueTypeSourceName(genArgs, genArgNames, receiverType, arg0Type)} source is not wired: " +
                        "the runtime has no predicate kernel for a non-decimal value-type element family " +
                        "(il2cpp_linq_any_i4 reads 4-byte int32 cells and calls the predicate through the int32 ABI)");
                if (count < 2
                    && (anyRecv is "System.Object" or null or "System.Collections.IEnumerable"
                        || (anyRecv != null
                            && anyRecv.StartsWith("System.Linq.IGrouping", StringComparison.Ordinal))))
                    return I("il2cpp::il2cpp_linq_any_object", 1, true, false, false, new[] { "" });
                return count >= 2
                    ? I("il2cpp::il2cpp_linq_any_i4", 2, true, false, false, TwoArg)
                    : I("il2cpp::il2cpp_linq_any", 1, true, false, false, OneArg);
            case "All":
                return I("il2cpp::il2cpp_linq_all_i4", 2, true, false, false, TwoArg);
            case "Sum":
                return LinqFamilyAggregate("Sum", genArgs, receiverType, arg0Type, genArgNames);
            case "Min":
                return LinqFamilyAggregate("Min", genArgs, receiverType, arg0Type, genArgNames);
            case "Max":
                return LinqFamilyAggregate("Max", genArgs, receiverType, arg0Type, genArgNames);
            case "Average":
                // Average(int32[]) -> double (resultKind "d", linq.h). net48
                // Enumerable.Average(this IEnumerable<int>) returns double.
                return LinqFamilyAggregate("Average", genArgs, receiverType, arg0Type, genArgNames);
            case "Where":
                // Returns a fresh int32[] -- tracked as "System.Int32[]" so a
                // downstream .ToArray()/.Count()/indexer resolves on the array.
                // The index overload Where(Func<TSource,int,bool>) carries a
                // 3-argument Func, which the 1-argument predicate icall cannot
                // represent -- route it to the *_idx entry (same discriminator
                // as TakeWhile/SkipWhile).
                if (LinqSecondParamGenericArgCount(md, sigHandle) == 3)
                    return I("il2cpp::il2cpp_linq_where_idx_i4", 2, true, true, false, TwoArg,
                        returnTypeName: "System.Int32[]");
                return I("il2cpp::il2cpp_linq_where_i4", 2, true, true, false, TwoArg,
                    returnTypeName: "System.Int32[]");
            case "Select":
                // int32 -> int32 family (select_i4_i4). nb9 FIX (M19): the
                // RESULT family is generic -- a non-int32 TResult must NOT go
                // through the int32 entry (the result array layout and the
                // foreach carrier would disagree: an access violation).
                // The index overload Select(Func<TSource,int,TResult>) carries
                // a 3-argument Func -- route it to the *_idx entry.
                {
                    bool selIdx = LinqSecondParamGenericArgCount(md, sigHandle) == 3;
                    byte tRes = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                    switch (tRes)
                    {
                        case 0x08:
                        case 0x02:
                        case 0x03:
                        case 0x04:
                        case 0x05:
                        case 0x06:
                        case 0x07:
                        case 0x09:
                            return I(selIdx
                                    ? "il2cpp::il2cpp_linq_select_idx_i4_i4"
                                    : "il2cpp::il2cpp_linq_select_i4_i4",
                                2, true, true, false, TwoArg, returnTypeName: "System.Int32[]");
                        case 0x0A:
                        case 0x0B:
                            return I(selIdx
                                    ? "il2cpp::il2cpp_linq_select_idx_i4_i8"
                                    : "il2cpp::il2cpp_linq_select_i4_i8",
                                2, true, true, false, TwoArg, returnTypeName: "System.Int64[]");
                        case 0x0C:
                            // nb9 fix P0-1: a float TResult is a float-returning
                            // selector; materialize it as a double[] (the F
                            // stack family) through the float ABI. Mapping it
                            // to the double kernel read the 32-bit float
                            // return as a double (garbage upper bits).
                            return I(selIdx
                                    ? "il2cpp::il2cpp_linq_select_idx_i4_f"
                                    : "il2cpp::il2cpp_linq_select_i4_f",
                                2, true, true, false, TwoArg, returnTypeName: "System.Double[]");
                        case 0x0D:
                            return I(selIdx
                                    ? "il2cpp::il2cpp_linq_select_idx_i4_d"
                                    : "il2cpp::il2cpp_linq_select_i4_d",
                                2, true, true, false, TwoArg, returnTypeName: "System.Double[]");
                        case 0x11:
                            // P0LINQ1 (D3): a DECIMAL result. decimal is a
                            // 16-byte value type, so the result is a decimal[]
                            // (elemSize 16, non-reference) and the selector
                            // returns the struct BY VALUE through its own ABI --
                            // the runtime picks the entry by the SOURCE family
                            // (i4/i8/d/f/dec). The 0x11 byte covers every value
                            // type, so the decoded MethodSpec FULL NAME is the
                            // only authority: the DateTime/TimeSpan/Guid results
                            // ride the int32-source by-value selector kernels,
                            // decimal its own, and any other value type stays LOUD.
                        {
                            string? resName = genArgNames is { Length: > 1 } ? genArgNames[1] : null;
                            if (resName is "System.DateTime" or "System.TimeSpan" or "System.Guid"
                                or "System.DateTimeOffset")
                            {
                                string suffix = resName == "System.DateTime" ? "dt"
                                    : resName == "System.TimeSpan" ? "ts"
                                    : resName == "System.Guid" ? "guid" : "dtoffset";
                                return I($"il2cpp::il2cpp_linq_select_{(selIdx ? "idx_" : "")}i4_{suffix}",
                                    2, true, true, false, TwoArg,
                                    returnTypeName: resName + "[]");
                            }
                            if (!IsDecimalGenArg(genArgNames, 1))
                                throw new NotSupportedException(
                                    $"Enumerable.Select<{GenericArgTypeName(genArgs![0])}," +
                                    $"{GenericArgTypeName(tRes)}> not wired: the value-type result " +
                                    "family with a kernel is System.Decimal (il2cpp_linq_select_<src>_dec) " +
                                    "and DateTime/TimeSpan/Guid (il2cpp_linq_select[_idx]_i4_dt/_ts/_guid)");
                            if (selIdx)
                            {
                                // p0linq4 (D2): the (element, index) selector
                                // returning decimal has a kernel for the int32
                                // source only (il2cpp_linq_select_idx_i4_dec --
                                // the same 16-byte result-slot ABI plus the
                                // source index).
                                if (LinqSourceFamily(genArgs, receiverType,
                                        count >= 2 ? arg1Type : arg0Type) != "i4")
                                    throw new NotSupportedException(
                                        "Enumerable.Select with a (element, index) selector returning " +
                                        "System.Decimal is wired for an int32 source only " +
                                        "(il2cpp_linq_select_idx_i4_dec)");
                                return I("il2cpp::il2cpp_linq_select_idx_i4_dec", 2, true, true,
                                    false, TwoArg, returnTypeName: "System.Decimal[]");
                            }
                            string decEntry = LinqSourceFamily(genArgs, receiverType,
                                count >= 2 ? arg1Type : arg0Type) switch
                            {
                                "i4" => "il2cpp::il2cpp_linq_select_i4_dec",
                                "i8" => "il2cpp::il2cpp_linq_select_i8_dec",
                                "f" => "il2cpp::il2cpp_linq_select_f_dec",
                                "d" => "il2cpp::il2cpp_linq_select_d_dec",
                                // A REFERENCE source is handled earlier by the
                                // reference-source block (il2cpp_linq_select_ref_dec),
                                // so this switch is only reached for the VALUE
                                // source families; the "ref" arm is unreachable
                                // dead code and is intentionally omitted.
                                _ => "il2cpp::il2cpp_linq_select_dec_dec",
                            };
                            return I(decEntry, 2, true, true, false, TwoArg,
                                returnTypeName: "System.Decimal[]");
                        }
                        default:
                            // A reference TResult -> a reference array. The
                            // element name is tracked so downstream member
                            // calls resolve on the array's element type.
                            return I(selIdx
                                    ? "il2cpp::il2cpp_linq_select_idx_i4_ref"
                                    : "il2cpp::il2cpp_linq_select_i4_ref",
                                2, true, true, false, TwoArg,
                                returnTypeName: GenericArgTypeName(tRes) + "[]");
                    }
                }
            case "ToArray":
                // D-LINQ-4: ToArray over one IGrouping bucket materializes an
                // exact-size int32[] copy (get_elements contract), tracked for
                // downstream operators.
                var toArrRecv = receiverType ?? arg0Type;
                if (toArrRecv == "System.Linq.IGrouping")
                    return I("il2cpp::il2cpp_linq_grouping_get_elements", 1, true, true, false,
                        new[] { "" }, returnTypeName: "System.Int32[]");
                // nb9 fix P0-4: ToArray is element-family agnostic -- the runtime
                // clones the array by its own elemSize/isRefElem. The tracked
                // return type follows TSource so downstream members resolve.
                //
                // impl-linqfix: the 0x11 value types need the generic-argument
                // NAME (the byte-only LinqArrayTypeName says "System.Decimal[]"
                // for every 0x11 source, so a DateTime[] / TimeSpan[] / Guid[]
                // snapshot was tracked as a decimal[] and the downstream
                // operators resolved the decimal family against it).
                {
                    var toArrType = genArgs is { Length: >= 1 }
                        ? LinqArrayTypeNameExact(genArgs[0], genArgNames, 0)
                        : (toArrRecv != null && toArrRecv.EndsWith("[]", StringComparison.Ordinal)
                            ? toArrRecv : "System.Int32[]");
                    return I("il2cpp::il2cpp_linq_to_array_family", 1, true, true, false, new[] { "a" },
                        returnTypeName: toArrType);
                }
            case "First":
                // First() -> first_i4 (source-only); First(source, predicate)
                // -> first_predicate (walks from the start, returns the first
                // match; no match -> InvalidOperationException).
                if (count >= 2)
                    return I("il2cpp::il2cpp_linq_first_predicate", 2, true, false, false, TwoArg);
                return I("il2cpp::il2cpp_linq_first_i4", 1, true, false, false, OneArg);
            case "FirstOrDefault":
                // FirstOrDefault() -> first_or_default_i4 (source-only);
                // FirstOrDefault(source, predicate) -> first_or_default_predicate
                // (walks from the start, returns the first match; no match or
                // empty source -> 0, the int32 default -- never throws).
                if (count >= 2)
                    return I("il2cpp::il2cpp_linq_first_or_default_predicate", 2, true, false, false, TwoArg);
                return I("il2cpp::il2cpp_linq_first_or_default_i4", 1, true, false, false, OneArg);
            case "Take":
                // Take(count) -- the first `count` elements (net48, probe-
                // verified: count <= 0 -> empty, count >= length -> full copy,
                // negative count -> empty WITHOUT throwing). Eager
                // materialization into a fresh int32[] tracked for downstream
                // operators (same shape as Where). The count rides the "i"
                // cast (int32 slot -> int32_t); the "a" cast maps the source
                // array.
                return I("il2cpp::il2cpp_linq_take_i4", 2, true, true, false,
                    new[] { "a", "i" }, returnTypeName: "System.Int32[]");
            case "Skip":
                // Skip(count) -- everything after the first `count` elements
                // (net48, probe-verified: count <= 0 -> full copy, count >=
                // length -> empty, negative count -> full copy WITHOUT
                // throwing). Eager materialization into a fresh int32[].
                return I("il2cpp::il2cpp_linq_skip_i4", 2, true, true, false,
                    new[] { "a", "i" }, returnTypeName: "System.Int32[]");
            case "TakeWhile":
            case "SkipWhile":
                // TakeWhile(source, predicate) -> il2cpp_linq_take_while_i4 /
                // SkipWhile(source, predicate) -> il2cpp_linq_skip_while_i4
                // (D-LT-2, net48 semantics, probe-verified): TakeWhile yields
                // the longest PREFIX of elements satisfying the predicate and
                // stops at the first failing element (excluded); SkipWhile
                // yields everything after the prefix -- the first failing
                // element and the rest are kept, and the predicate is NEVER
                // called again after the prefix ends. Both run the predicate
                // EXACTLY ONCE per visited element and return a fresh int32[]
                // tracked for downstream operators. The INDEX overloads
                // (Func<TSource,int,bool> -- the predicate receives the
                // element AND its source index) route to the *_idx_i4 entries
                // when the predicate's GenericInst arg count is 3 (the two
                // overloads are otherwise indistinguishable by arity/bytes --
                // LinqSecondParamGenericArgCount).
                {
                    bool idx = LinqSecondParamGenericArgCount(md, sigHandle) == 3;
                    string icall = name switch
                    {
                        "TakeWhile" => idx
                            ? "il2cpp::il2cpp_linq_take_while_idx_i4"
                            : "il2cpp::il2cpp_linq_take_while_i4",
                        _ => idx
                            ? "il2cpp::il2cpp_linq_skip_while_idx_i4"
                            : "il2cpp::il2cpp_linq_skip_while_i4",
                    };
                    return I(icall, 2, true, true, false, TwoArg,
                        returnTypeName: "System.Int32[]");
                }
            case "OrderBy":
                // OrderBy(source, keySelector) -> the D-LINQ-1 ABI bridge
                // (linq_sort.h): the runtime caches each element's key EXACTLY
                // ONCE (net48 EnumerableSorter semantics) and runs a stable sort
                // on the cached keys. The result is a fresh int32[] tracked for
                // downstream operators. The KEY family is int32 (the 0x08-slot
                // primitives), int64 (long/ulong) or double/float (the F-stack
                // carrier); anything else has no int32-source key icall.
                //
                // b5-gaps section 71: OrderBy(source, keySelector, comparer)
                // over an int32-source applies the USER key comparer -- the
                // same cached-once / stable sort, but the comparer's own
                // Compare decides, dispatching through the reflected value slot
                // (il2cpp_linq_order_by_key_i4_cmp) or the ref-key kernel
                // (il2cpp_linq_order_by_key_i4_ref_cmp for a string/object
                // TKey). The KEY cell is appended as a literal.
                if (LinqHasComparerParam(md, sigHandle))
                {
                    return LinqOrderByKeyEntryCmp(md, sigHandle, genArgs, genArgNames,
                        desc: false, srcSuffix: "i4");
                }
                return LinqOrderByKeyEntry(genArgs, genArgNames, desc: false);
            case "OrderByDescending":
                // OrderByDescending(source, keySelector) ->
                // il2cpp_linq_order_by_key[_desc]_* (the DESCENDING bridge):
                // identical to OrderBy but with DESCENDING key order. The same
                // key families as OrderBy.
                if (LinqHasComparerParam(md, sigHandle))
                {
                    return LinqOrderByKeyEntryCmp(md, sigHandle, genArgs, genArgNames,
                        desc: true, srcSuffix: "i4");
                }
                return LinqOrderByKeyEntry(genArgs, genArgNames, desc: true);
            case "ThenBy":
            case "ThenByDescending":
                // D-LO2-1 (linqorder2): Enumerable.ThenBy / ThenByDescending
                // -- the CHAINED-SORTER surface. OrderBy(k1).ThenBy(k2) is a
                // FUSED composite sort (net48 EnumerableSorter chain: (k1 asc,
                // k2 asc), stable; ThenByDescending flips only the secondary
                // key). The eager array runtime cannot recover the primary key
                // at the ThenBy call, so this arm returns the
                // ThenByCombineMarker / ThenByDescCombineMarker and StackFold
                // rewrites the receiver (a direct OrderBy(keySelector) call)
                // into ONE il2cpp_linq_then_by_key[_desc]_i4(src, k1, k2)
                // call -- each element's k1 and k2 are each cached EXACTLY
                // ONCE (net48 EnumerableSorter). Requires int32 elements AND
                // int32 keys (the stage-1 key family).
                //
                // p0linq4 (D4): an INT64-element source (long[]/ulong[]) rides
                // the *_i8_<keyfam> entries -- 8-byte cells in, a fresh int64[]
                // (elemKind I8) out; the key family is int32 or int64. The
                // StackFold fold recognises the matching OrderBy assignment
                // (il2cpp_linq_order_by_key[_desc]_i8_<keyfam>) and rewrites it
                // into the i8 composite.
                //
                // impl-linqleft: a DECIMAL element (decimal[] source or a
                // decimal GroupBy bucket) with a System.DateTime / System.Guid
                // secondary key has a fused composite too
                // (il2cpp_linq_[grouping_]then_by_key[_desc]_dec_<k1>_<k2>).
                // The secondary key family is carried in AppendLiterals; the
                // primary family is read off the OrderBy entry in StackFold.
                //
                // impl-thenbyfam: the NUMERIC element surface (int32/int64
                // source, int32/int64/double/float keys) carries the SECONDARY
                // key family in AppendLiterals too (0=int32, 1=int64,
                // 2=double, 3=float -- LinqValKeyFamilyCode): the fused
                // composite picks the key reader PER KEY, and a mixed pair
                // (OrderBy(int).ThenBy(double) /
                // OrderBy(double).ThenBy(int)) that dropped the secondary
                // family read the secondary key from the WRONG callback return
                // slot -- a silent mis-order on x64. StackFold folds this into
                // il2cpp_linq_then_by_key_val_i4 / _val_i8 with both family
                // literals.
                //
                // b5-gaps section 47: a DOUBLE / FLOAT ELEMENT source folds its
                // ThenBy into the chain surface (then_by_chain_d / _f). net48
                // fuses OrderBy(k1).ThenBy(k2) over an IEnumerable<double> /
                // IEnumerable<float> exactly like over int32/int64; the
                // secondary KEY family is 0=int32, 1=int64, 2=double, 3=float
                // (LinqValKeyFamilyCode). The OrderBy primaries carry the SOURCE
                // and primary key families in the entry name, which StackFold
                // reads out of the receiver's producing assignment.
                if (genArgs is { Length: >= 2 } && linqSrcFam is "d" or "f"
                    && LinqValKeyFamilyCode(genArgs[1]) is { } wideSecondary)
                {
                    return I(name == "ThenBy"
                            ? ResolvedMethod.ThenByCombineMarker
                            : ResolvedMethod.ThenByDescCombineMarker,
                        2, false, false, false, new[] { "", "" },
                        returnTypeName: linqSrcFam == "d" ? "System.Double[]" : "System.Single[]",
                        appendLiterals: new[] { wideSecondary });
                }
                if (genArgs is { Length: >= 2 } && genArgs[0] == 0x11
                    && IsDecimalGenArg(genArgNames, 0)
                    && LinqKeyFamilyCode(genArgs[1], genArgNames, 1) is { } decSecondary)
                {
                    return I(name == "ThenBy"
                            ? ResolvedMethod.ThenByCombineMarker
                            : ResolvedMethod.ThenByDescCombineMarker,
                        2, false, false, false, new[] { "", "" },
                        returnTypeName: "System.Decimal[]",
                        appendLiterals: new[] { decSecondary });
                }
                if (genArgs is { Length: >= 2 }
                    && ThenByKernelSuffix(genArgs[0], genArgs[1]) is not null
                    && LinqValKeyFamilyCode(genArgs[1]) is { } numSecondary)
                {
                    // impl-thenbyfam: the SECONDARY key family must ride the
                    // marker (the decimal carriers already do): the fused
                    // composite picks ONE key reader per key, and a mixed pair
                    // (e.g. OrderBy(int).ThenBy(double)) that dropped the
                    // secondary family read the secondary key from the WRONG
                    // callback return slot -- a silent mis-order on x64.
                    // LinqValKeyFamilyCode is 0=int32, 1=int64, 2=double,
                    // 3=float for the numeric families ThenByKernelSuffix
                    // already gated.
                    return I(name == "ThenBy"
                            ? ResolvedMethod.ThenByCombineMarker
                            : ResolvedMethod.ThenByDescCombineMarker,
                        2, false, false, false, new[] { "", "" },
                        returnTypeName: genArgs[0] == 0x08 ? "System.Int32[]" : "System.Int64[]",
                        appendLiterals: new[] { numSecondary });
                }
                if (genArgs is not { Length: >= 2 }
                    || ThenByKernelSuffix(genArgs[0], genArgs[1]) is null)
                {
                    string elemArgType = genArgs is { Length: >= 1 }
                        ? GenericArgTypeName(genArgs[0]) : "?";
                    string keyArgType = genArgs is { Length: >= 2 }
                        ? GenericArgTypeName(genArgs[1]) : "?";
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{elemArgType},{keyArgType}> not wired: " +
                        "the then-by surface (il2cpp_linq_then_by_key_i4 / _i8_<keyfam>) covers int32 " +
                        "elements with int32 keys and int64 elements with int32/int64 keys only -- the " +
                        "runtime fuses the (k1, k2) composite sort");
                }
                return I(name == "ThenBy"
                        ? ResolvedMethod.ThenByCombineMarker
                        : ResolvedMethod.ThenByDescCombineMarker,
                    2, false, false, false, new[] { "", "" },
                    returnTypeName: genArgs[0] == 0x08 ? "System.Int32[]" : "System.Int64[]");
            case "GroupBy":
                // GroupBy(source, keySelector) -> il2cpp_linq_group_by_i4
                // (D-LINQ-4): the runtime builds a tracked grouping result
                // whose iteration yields one IGrouping per distinct key in
                // FIRST-APPEARANCE order (net48, probe-verified). The result
                // is NOT an int32[] -- it is tracked as the pseudo-type
                // "System.Linq.IGroupingResult" so the enumerator surface
                // (GetEnumerator / get_Current / get_Key / per-bucket
                // operators) dispatches on the receiver hint.
                //
                // nb9 FIX (M1/M2/M16): the 3-argument overloads are AMBIGUOUS
                // by bytes (Func`2 element selector, IEqualityComparer`1
                // comparer and Func`3 result selector are all GENERICINST
                // 0x15), so the third parameter's GENERIC DEFINITION NAME
                // decides. The family of the element / result is genArgs:
                //   GroupBy<TSource,TKey,TElement>          -> genArgs[2] = TElement
                //   GroupBy<TSource,TKey,TResult>           -> genArgs[2] = TResult
                //   GroupBy<TSource,TKey,TElement,TResult>  -> genArgs[3] = TResult
                // Everything is served by il2cpp_linq_group_by_full with the
                // two family codes as trailing literals.
                // B4-1 (P0-04): the runtime bucket key is an int32 slot
                // (linq_map.h:249/400), so the wireable key families are the
                // ones whose VALUE fits that slot AND whose callback result can
                // be narrowed back to the declared TKey width (KeyKindFor):
                // int/bool/char/sbyte/byte/short/ushort/uint. The gate below is
                // kept EXACTLY equal to KeyKindFor's supported set -- a family
                // the runtime cannot bucket correctly must NOT be let through
                // (that mismatch is what produced silent wrong buckets).
                // long (0x0A) is deliberately NOT here: the int32 bucket would
                // truncate 64-bit keys. Every rejection is a TRANSPILE-TIME
                // LOUD, and the complete list of deliberately-rejected shapes
                // (long/ulong/decimal/DateTime/Guid/any 0x11 key, non-int32
                // source arrays) with the required ABI change surface lives in
                // docs/audit/b4-gaps.md -- never widen this gate without it.
                int keyKind = genArgs is { Length: >= 2 }
                    ? KeyKindForNamed(genArgs[1], genArgNames, 1) : -1;
                if (genArgs is not { Length: >= 2 } || keyKind < 0)
                {
                    // The FULL type name (GenericArgFullNames) is the only way to
                    // tell the 0x11 value-type keys apart -- every struct/enum
                    // key byte is 0x11, so the byte alone cannot say whether the
                    // rejected key is System.Decimal, System.DateTime or a user
                    // struct. Use the name when the slot has one.
                    string elemArgType = genArgs is { Length: >= 1 }
                        ? GenericArgTypeName(genArgs[0]) : "?";
                    string keyArgType = genArgs is { Length: >= 2 }
                        ? (genArgNames is { Length: >= 2 } && genArgNames[1] != "?"
                            ? genArgNames[1]
                            : GenericArgTypeName(genArgs[1]))
                        : "?";
                    throw new NotSupportedException(
                        $"Enumerable.GroupBy<{elemArgType},{keyArgType}> not wired: " +
                        "the grouping surface buckets int32 (0x08) elements by a key carrier of " +
                        "int32-slot (int/bool/char/sbyte/byte/short/ushort/uint), int64 (long/ulong) " +
                        "or 16-byte struct (System.Decimal/System.DateTime/System.Guid); " +
                        $"{DescribeRejectedKey(genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0, keyArgType)} " +
                        "has no runtime key carrier (see docs/audit/b5-gaps.md)");
                }
                {
                    var gbParams = LinqEncodedParamTypes(md, sigHandle);
                    string ElemFamily(byte b) => b switch
                    {
                        0x08 => "0",                                   // int32
                        // nb9 fix P0-3: the int32-slot primitives (bool/char/
                        // sbyte/byte/short/ushort/uint) ride the int32 bucket
                        // family -- net48 boxes them as int32-width values and
                        // the enumerator's i4 carrier unboxes them.
                        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "0",
                        0x0A or 0x0B => "2",                        // int64 / uint64 (boxed; bit-identical)
                        0x0D => "3",                                   // double (boxed)
                        // floatgrp: a REAL 4-byte float cell (kElemKindF). It must
                        // NOT ride the int32 family: the 4-byte store would keep
                        // the float bits, but every int32 reader/enumerator would
                        // reinterpret them (silent wrong value), and ToArray would
                        // emit int32[] instead of net48's float[].
                        0x0C => "7",                                   // float (real 4-byte cell)
                        0x0E or 0x12 or 0x1C or 0x1D or 0x15 or 0x13 or 0x1E => "1", // reference
                        _ => "",
                    };
                    // nb9 H-F4: TResult selects the result array's REAL element
                    // layout and the selector's return ABI (a float/double/
                    // long TResult was previously run through the reference
                    // ABI and materialized as object[]).
                    string ResultFamily(byte b) => b switch
                    {
                        0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "1",
                        0x0A or 0x0B => "3",
                        0x0D => "4",
                        0x0C => "5",
                        _ => "2",
                    };

                    // Which IL argument is the element selector / comparer /
                    // result selector (decided by the generic DEFINITION name).
                    int elemArg = -1;
                    int cmpArg = -1;
                    int resArg = -1;
                    for (int pi = 2; pi < gbParams.Length; pi++)
                    {
                        var def = GenericDefNameOf(gbParams[pi]);
                        if (def == "System.Func`2") elemArg = pi;
                        else if (def == "System.Func`3") resArg = pi;
                        else if (def == "System.Collections.Generic.IEqualityComparer`1") cmpArg = pi;
                    }

                    byte? elemByte = elemArg >= 0 && genArgs.Length >= 3 ? genArgs[2] : null;
                    byte? resByte = resArg >= 0 && genArgs.Length >= 3
                        ? (genArgs.Length >= 4 ? genArgs[3] : genArgs[2])
                        : null;

                    string ek = elemByte is { } eb ? ElemFamily(eb) : "0";
                    if (ek.Length == 0)
                    {
                        throw new NotSupportedException(
                            $"Enumerable.GroupBy TElement '{GenericArgTypeName(elemByte!.Value)}' is not a supported element family");
                    }
                    string rk = resByte is { } rb ? ResultFamily(rb) : "0";

                    // The runtime entry takes (source, keySelector,
                    // elementSelector, comparer, resultSelector, elemKind,
                    // rsKind). The IL argument order differs per overload
                    // (source, key, [element], [comparer], [result]), so
                    // ArgOrder maps each ICALL slot to its IL argument index
                    // (-1 = absent -> nullptr); the two family codes are
                    // appended literals.
                    int elemIdx = elemArg >= 0 ? elemArg : -1;
                    int cmpIdx = cmpArg >= 0 ? cmpArg : -1;
                    int resIdx = resArg >= 0 ? resArg : -1;
                    var argOrder = new[] { 0, 1, elemIdx, cmpIdx, resIdx };
                    var casts = new[] { "a", "", "", "", "" };
                    // B4-1 (P0-04): the THIRD literal is the key family the
                    // runtime narrows the callback result to (KeyKindFor).
                    var literals = new[] { ek, rk,
                        keyKind.ToString(System.Globalization.CultureInfo.InvariantCulture), "0" };
                    var retType = rk == "0"
                        ? "System.Linq.IGroupingResult"
                        : rk == "1" ? "System.Int32[]"
                        : rk == "3" ? "System.Int64[]"
                        : rk == "4" ? "System.Double[]"
                        : rk == "5" ? "System.Double[]" : "System.Object[]";
                    // The result is ALWAYS a managed reference (a grouping
                    // result object or a result array), so the result temp is
                    // the Il2CppObject* slot (rv=true, rr=true).
                    return I("il2cpp::il2cpp_linq_group_by_full_key", count, true, true, false, casts,
                        returnTypeName: retType, appendLiterals: literals,
                        argOrder: argOrder);
                }
            case "Concat":
                // Concat(source, second) -> il2cpp_linq_concat_i4 (D-LINQZ-1):
                // a NEW int32[] holding ALL of source then ALL of second,
                // order AND duplicates preserved (net48 Concat); length = sum
                // of the parts; an empty operand contributes nothing. Both
                // array slots ride the "a" cast.
                return I("il2cpp::il2cpp_linq_concat_i4", 2, true, true, false,
                    new[] { "a", "a" }, returnTypeName: "System.Int32[]");
            case "Distinct" when count < 2:
                // Distinct(source) -> il2cpp_linq_distinct_i4 (D-LINQZ-1):
                // remove duplicates keeping FIRST-APPEARANCE order (net48
                // default ordinal int32 equality); the result is a fresh
                // int32[] tracked for downstream operators.
                return I("il2cpp::il2cpp_linq_distinct_i4", 1, true, true, false,
                    OneArg, returnTypeName: "System.Int32[]");
            case "Distinct":
                // b5-gaps section 57: Distinct(source, comparer) over int32.
                return I("il2cpp::il2cpp_linq_distinct_value_cmp", 2, true, true, false,
                    new[] { "a", "" }, returnTypeName: "System.Int32[]",
                    appendLiterals: new[] { "7" });
            case "Except" when count < 3:
                // Except(source, second) -> il2cpp_linq_except_i4 (D-LINQZ-1):
                // the elements of source NOT present in second (set
                // membership -- duplicates in second are irrelevant),
                // first-appearance order, DEDUPLICATED output (net48).
                return I("il2cpp::il2cpp_linq_except_i4", 2, true, true, false,
                    new[] { "a", "a" }, returnTypeName: "System.Int32[]");
            case "Except":
                return I("il2cpp::il2cpp_linq_except_value_cmp", 3, true, true, false,
                    new[] { "a", "a", "" }, returnTypeName: "System.Int32[]",
                    appendLiterals: new[] { "7" });
            case "Intersect" when count < 3:
                // Intersect(source, second) -> il2cpp_linq_intersect_family
                // (cell 7): the elements of source also in second,
                // first-appearance order, DEDUPLICATED output (net48).
                return I("il2cpp::il2cpp_linq_intersect_family", 2, true, true, false,
                    new[] { "a", "a" }, returnTypeName: "System.Int32[]",
                    appendLiterals: new[] { "7" });
            case "Intersect":
                return I("il2cpp::il2cpp_linq_intersect_value_cmp", 3, true, true, false,
                    new[] { "a", "a", "" }, returnTypeName: "System.Int32[]",
                    appendLiterals: new[] { "7" });
            case "Union" when count < 3:
                // Union(source, second) -> il2cpp_linq_union_family (D-LINQZ-2):
                // first's elements then second's, deduplicated by
                // first-appearance (net48 Union = distinct concat). The int32
                // cell code (IL2CPP_LINQ_CELL_I4 = 7) is an APPENDED literal;
                // both array slots ride the "a" cast.
                return I("il2cpp::il2cpp_linq_union_family", 2, true, true, false,
                    new[] { "a", "a" }, returnTypeName: "System.Int32[]",
                    appendLiterals: new[] { "7" });
            case "Union":
                return I("il2cpp::il2cpp_linq_union_value_cmp", 3, true, true, false,
                    new[] { "a", "a", "" }, returnTypeName: "System.Int32[]",
                    appendLiterals: new[] { "7" });
            case "Zip":
            {
                // Zip over an int32 ARRAY source (the common case). The unified
                // runtime entry takes IL2CPP_LIST_KIND_* codes for BOTH sides and
                // decides List-vs-array from the object descriptor.
                byte ztFirst = genArgs is { Length: >= 1 } ? genArgs[0] : (byte)0x08;
                byte ztSecond = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                byte ztRes = genArgs is { Length: >= 3 } ? genArgs[2] : (byte)0x08;
                string zFirstKind = IsRefFamilyByte(ztFirst) ? "1" : LinqListKindOfElemByte(ztFirst, genArgNames, 0) ?? "";
                string zSecondKind = IsRefFamilyByte(ztSecond) ? "1" : LinqListKindOfElemByte(ztSecond, genArgNames, 1) ?? "";
                if (zFirstKind.Length == 0 || zSecondKind.Length == 0)
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(ztFirst)},{GenericArgTypeName(ztSecond)},...> " +
                        "not wired: an element family with no cell ABI (see docs/audit/b5-gaps.md)");
                // BATCH4: a reference-element source rides the mixed entry (it
                // reads the element as Il2CppObject* and calls the selector
                // through the statically declared ABI); the result cell may be a
                // reference array (6) or a narrow 1/2-byte cell (7..12).
                string? zResKind = LinqZipResKindOfElemByte(ztRes, genArgNames, 2);
                if (zResKind == null)
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(ztFirst)},{GenericArgTypeName(ztSecond)}," +
                        $"{GenericArgTypeName(ztRes)}> not wired: no result cell ABI for this TResult " +
                        "(see docs/audit/b5-gaps.md)");
                return I("il2cpp::il2cpp_linq_zip_family", 3, true, true, false,
                    new[] { "", "", "" }, returnTypeName: LinqArrayTypeNameExact(ztRes, genArgNames, 2),
                    appendLiterals: new[] { zFirstKind, zSecondKind, zResKind });
            }
            case "Reverse":
                // Reverse(source) -> il2cpp_linq_reverse_i4: a NEW int32[] with
                // elements in reverse order (net48). Eager materialization.
                return I("il2cpp::il2cpp_linq_reverse_i4", 1, true, true, false,
                    OneArg, returnTypeName: "System.Int32[]");
            case "Last" when count < 2:
                // Last() -> last_i4: the last element; empty ->
                // InvalidOperationException.
                return I("il2cpp::il2cpp_linq_last_i4", 1, true, false, false, OneArg);
            case "Last":
                // Last(source, predicate): FORWARD full walk keeping the last
                // match (net48 4.8 has no reverse optimization for this form,
                // so the predicate runs once per element).
                return I("il2cpp::il2cpp_linq_last_pred_i4", 2, true, false, false, TwoArg);
            case "LastOrDefault" when count < 2:
                return I("il2cpp::il2cpp_linq_last_or_default_i4", 1, true, false, false, OneArg);
            case "LastOrDefault":
                return I("il2cpp::il2cpp_linq_last_or_default_pred_i4", 2, true, false, false, TwoArg);
            case "Single" when count < 2:
                // Single<TSource>(IEnumerable<TSource>) -> the family kernel
                // (int32 cell). net48: empty or >1 -> InvalidOperationException.
                return I("il2cpp::il2cpp_linq_single_fam_i4", 1, true, false, false, OneArg,
                    appendLiterals: new[] { "7" });
            case "Single":
                // Single(source, predicate): net48 walks every element and
                // counts the matches (0 -> InvalidOperationException; >1 ->
                // InvalidOperationException; 1 -> that element).
                return I("il2cpp::il2cpp_linq_single_pred_i4", 2, true, false, false, TwoArg);
            case "SingleOrDefault" when count < 2:
                return I("il2cpp::il2cpp_linq_single_or_default_fam_i4", 1, true, false, false, OneArg,
                    appendLiterals: new[] { "7" });
            case "SingleOrDefault":
                return I("il2cpp::il2cpp_linq_single_or_default_pred_i4", 2, true, false, false, TwoArg);
            case "ElementAt":
                // ElementAt(source, index) -> il2cpp_linq_element_at_i4.
                // index < 0 or >= length -> ArgumentOutOfRangeException.
                return I("il2cpp::il2cpp_linq_element_at_i4", 2, true, false, false,
                    new[] { "a", "i" });
            case "Contains" when count < 3:
                // Contains(source, value) -> il2cpp_linq_contains_i4: ordinal
                // int32 equality (EqualityComparer<int>.Default).
                return I("il2cpp::il2cpp_linq_contains_i4", 2, true, false, false,
                    new[] { "a", "i" });
            case "Contains":
                // b5-gaps section 57: Contains(source, value, comparer).
                return I("il2cpp::il2cpp_linq_contains_value_cmp_i4", 3, true, false, false,
                    new[] { "a", "i", "" }, appendLiterals: new[] { "7" });
            case "Aggregate":
                // Aggregate(source, func) -> il2cpp_linq_aggregate_i4: fold
                // left-to-right seeded from the first element (net48 Aggregate
                // without a seed). Empty -> InvalidOperationException.
                // Aggregate(source, seed, func) -> the accumulator family the
                // TAccumulate metadata byte selects (nb9 FIX M3: the old code
                // truncated every non-int32 seed and ran the callback through
                // the int32 ABI -- a silent wrong result).
                // Aggregate(source, seed, func, resultSelector) (nb9 M16): the
                // selector runs ONCE on the final accumulator.
                if (count == 2)
                {
                    // nb9 fix P0-1: the no-seed accumulator IS TSource, so the
                    // source element family decides the kernel (a float/double
                    // Select result is a double[] here).
                    //
                    // impl-linq2 (A): a DECIMAL source folds decimal x decimal
                    // (net48 Aggregate<decimal>(source, Func<decimal,decimal>)
                    // -- Enumerable.cs:1379). The old mapping answered null (the
                    // phantom "no BCL mapping").
                    if (IsLinqDecimalSource(genArgs, receiverType, arg0Type))
                    {
                        if (!IsDecimalSourceNamed(genArgs, genArgNames, receiverType, arg0Type))
                            throw new NotSupportedException(
                                "LOUD whitelist (no runtime value-struct fold ABI): Enumerable.Aggregate " +
                                $"over a {(genArgNames is { Length: >= 1 } ? genArgNames[0] : "value-type")} " +
                                "source is not wired: the runtime folds decimal x decimal and the primitive " +
                                "families only; a DateTime/TimeSpan/Guid/user-struct fold has no by-value " +
                                "callback ABI [Reference Source System.Core/System/Linq/Enumerable.cs:1379]");
                        return I("il2cpp::il2cpp_linq_aggregate_dec", 2, true, false, false, TwoArg,
                            resultKind: "dec");
                    }
                    var noSeedFam = LinqElementFamily(genArgs, receiverType, arg0Type);
                    return noSeedFam switch
                    {
                        "i4" => I("il2cpp::il2cpp_linq_aggregate_i4", 2, true, false, false, TwoArg),
                        "i8" => I("il2cpp::il2cpp_linq_aggregate_i8", 2, true, false, false,
                            TwoArg, resultKind: "l"),
                        // float TSource: the float Select materializes a
                        // double[] (the F-stack carrier convention, see H-F2),
                        // so the source read is double and the accumulator
                        // stays float.
                        "f" => I("il2cpp::il2cpp_linq_aggregate_f_d", 2, true, false, false,
                            TwoArg, resultKind: "d"),
                        "d" => I("il2cpp::il2cpp_linq_aggregate_d", 2, true, false, false,
                            TwoArg, resultKind: "d"),
                        _ => null,
                    };
                }
                if (count == 3 || count == 4)
                {
                    if (genArgs is not { Length: >= 2 })
                        return null;
                    byte accByte = genArgs[1];
                    // The result-selector family (only for count == 4).
                    byte resByte = count == 4 && genArgs.Length >= 3 ? genArgs[2] : (byte)0;
                    string acc = accByte switch
                    {
                        0x08 => "i4",
                        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "i4",
                        0x0A => "i8",
                        // B4-1 (P0-06): ulong TAccumulate rides the SAME int64
                        // accumulator slot as long (the value bit pattern is
                        // width-identical; net48's ulong fold is unchecked).
                        0x0B => "i8",
                        // nb9 fix P0-1: float is its OWN accumulator family.
                        // The old code folded 0x0C into "d", so the float
                        // return was read back as a double (upper 32 bits
                        // garbage -> 10.5000019418076 instead of 10.5).
                        0x0C => "f",
                        0x0D => "d",
                        0x0E or 0x12 or 0x1C or 0x1D or 0x15 or 0x13 or 0x1E => "ref",
                        _ => "",
                    };
                    // impl-linq2 (A): the DECIMAL accumulator family. The
                    // SOURCE family decides the fold's element reader: a
                    // decimal[] source folds decimal x decimal (the DecBinDec
                    // ABI), an int32 source the int32 reader (DecAccFn).
                    if (accByte == 0x11 && IsDecimalGenArg(genArgNames, 1))
                    {
                        bool decSrc = IsLinqDecimalSource(genArgs, receiverType, arg0Type)
                            && IsDecimalSourceNamed(genArgs, genArgNames, receiverType, arg0Type);
                        // impl-aggvt: a NON-decimal VALUE-TYPE source has its own
                        // cell-width family (System.DateTime -> 8-byte cells,
                        // System.TimeSpan -> 8-byte ticks, System.Guid -> 16
                        // bytes). The int32 reader of `aggregate_seed_dec` sliced
                        // 4 bytes out of such a cell (a silent wrong value for the
                        // Days fold, a wild read for the packed DateTime cell).
                        // A USER STRUCT has no cell family: it stays LOUD.
                        string? vtCell = decSrc
                            ? null
                            : LinqValueFamilyCellCode(genArgs, genArgNames, receiverType, arg0Type);
                        if (!decSrc && vtCell is null
                            && IsLinqValueTypeSource(genArgs, receiverType, arg0Type))
                        {
                            // A non-decimal value-type source with a decimal
                            // accumulator: the fold's element reader is the
                            // struct's own -- no by-value callback ABI.
                            throw new NotSupportedException(
                                "LOUD whitelist (no runtime value-struct fold ABI): Enumerable.Aggregate " +
                                $"over a {(genArgNames is { Length: >= 1 } ? genArgNames[0] : "value-type")} " +
                                "source with a System.Decimal accumulator is not wired: the runtime folds " +
                                "decimal x decimal, the DateTime/TimeSpan/Guid cell families and the " +
                                "primitive families only [Reference Source " +
                                "System.Core/System/Linq/Enumerable.cs:1391]");
                        }
                        string? vtBase = vtCell switch
                        {
                            "12" => "il2cpp::il2cpp_linq_aggregate_seed_dt_dec",
                            "13" => "il2cpp::il2cpp_linq_aggregate_seed_ts_dec",
                            "14" => "il2cpp::il2cpp_linq_aggregate_seed_guid_dec",
                            _ => null,
                        };
                        if (count == 3)
                        {
                            if (vtBase != null)
                                return I(vtBase, 3, true, false, false,
                                    new[] { "a", "dec", "" }, resultKind: "dec");
                            if (decSrc)
                                return I("il2cpp::il2cpp_linq_aggregate_seed_dec_dec", 3, true, false, false,
                                    new[] { "a", "dec", "" }, resultKind: "dec");
                            // impl-arrayagg: a DECIMAL accumulator over a
                            // non-int32 numeric/reference source. The base
                            // `seed_dec` reads a 4-byte int32 out of every
                            // cell; the cell matrix reads the element at its
                            // REAL width and calls the decimal leaf ABI.
                            string? decCell = LinqArrayCellCode(genArgs, genArgNames, receiverType, arg0Type);
                            if (decCell is null or "7")
                                return I("il2cpp::il2cpp_linq_aggregate_seed_dec", 3, true, false, false,
                                    new[] { "a", "dec", "" }, resultKind: "dec");
                            return I("il2cpp::il2cpp_linq_aggregate_seed_cell_dec", 3, true, false, false,
                                new[] { "a", "dec", "" }, resultKind: "dec",
                                appendLiterals: new[] { decCell });
                        }
                        // count == 4: the result selector runs ONCE on the
                        // final decimal accumulator (net48
                        // Aggregate(seed, func, resultSelector) --
                        // Enumerable.cs:1399). The TResult byte picks the
                        // projection ABI. The old mapping answered the
                        // 3-parameter seed kernel here (the resultSelector was
                        // dropped and the emitter consumed one stack value too
                        // few -- a stack desync).
                        // impl-count4fix: the decimal accumulator's FOLD must also
                        // follow the SOURCE element family. The base
                        // `seed_dec_rs` reads a 4-byte int32 out of every cell,
                        // so a long[]/float[]/double[]/reference[] source folded
                        // the low 32 bits (a SILENT wrong value). The cell matrix
                        // reads the element at its REAL width and calls the
                        // decimal leaf ABI. The decimal source and the
                        // DateTime/TimeSpan/Guid cells keep their own kernels.
                        string decBase = vtBase != null
                            ? vtBase + "_rs"
                            : decSrc
                                ? "il2cpp::il2cpp_linq_aggregate_seed_decsrc_rs"
                                : "il2cpp::il2cpp_linq_aggregate_seed_dec_rs";
                        string? decCell4 = null;
                        if (vtBase == null && !decSrc)
                        {
                            string? dCell = LinqArrayCellCode(genArgs, genArgNames, receiverType, arg0Type);
                            if (dCell is not null and not "7")
                            {
                                decBase = "il2cpp::il2cpp_linq_aggregate_seed_cell_dec_rs";
                                decCell4 = dCell;
                            }
                        }
                        var decCasts = new[] { "a", "dec", "", "" };
                        string[]? decLiterals = decCell4 == null ? null : new[] { decCell4 };
                        if (resByte == 0x11)
                        {
                            if (!IsDecimalGenArg(genArgNames, 2))
                            {
                                // impl-loudvalstruct (C): a by-value value-struct
                                // TResult (DateTime / TimeSpan / Guid /
                                // DateTimeOffset) now has a LIST projection
                                // entry (il2cpp_linq_list_aggregate_seed_<acc>_
                                // <elem>_rs_dt/_ts/_guid/_dtoffset); the ARRAY
                                // surface still has none.
                                //
                                // The HINT-LESS first pass cannot tell the two
                                // apart (the aggregate's arguments leave the
                                // source below the stack top, so arg0Type is
                                // the LAMBDA), so it must NOT throw -- it
                                // answers the decimal-carrier entry and lets
                                // StackFold's post-pop re-resolve dispatch the
                                // List domain (the same deferral defect A
                                // needed; without it the List shapes never
                                // reached the list matrix). A source PROVEN to
                                // be an array keeps the LOUD.
                                string? resStructFam = LinqStructResultSuffix(genArgNames, 2);
                                bool arrayProven = (receiverType ?? arg0Type) is { } an0
                                    && an0.EndsWith("[]", StringComparison.Ordinal);
                                if (resStructFam == null || arrayProven)
                                    throw new NotSupportedException(
                                        "LOUD whitelist (no runtime descriptor for the TResult struct): " +
                                        "Enumerable.Aggregate TResult " +
                                        $"{(genArgNames is { Length: >= 3 } ? genArgNames[2] : "valuetype")} " +
                                        "is not a supported result family -- the runtime projects the decimal " +
                                        "accumulator to int32/int64/float/double/decimal/reference and (over a " +
                                        "List) the System.DateTime/System.TimeSpan/System.Guid/" +
                                        "System.DateTimeOffset value structs only; the ARRAY surface has no " +
                                        "value-struct result projection [Reference Source " +
                                        "System.Core/System/Linq/Enumerable.cs:1399]");
                                return I(decBase + "_dec", 4, true, false, false, decCasts, resultKind: "dec", appendLiterals: decLiterals);
                            }
                            return I(decBase + "_dec", 4, true, false, false, decCasts, resultKind: "dec", appendLiterals: decLiterals);
                        }
                        switch (resByte)
                        {
                            case 0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09:
                                return I(decBase + "_i4", 4, true, false, false, decCasts, appendLiterals: decLiterals);
                            case 0x0A or 0x0B:
                                return I(decBase + "_i8", 4, true, false, false, decCasts, resultKind: "l", appendLiterals: decLiterals);
                            case 0x0C:
                                return I(decBase + "_f", 4, true, false, false, decCasts, resultKind: "d", appendLiterals: decLiterals);
                            case 0x0D:
                                return I(decBase + "_d", 4, true, false, false, decCasts, resultKind: "d", appendLiterals: decLiterals);
                            case 0x0E or 0x12 or 0x1C or 0x1D or 0x15 or 0x13 or 0x1E:
                                return I(decBase + "_ref", 4, true, true, false, decCasts, appendLiterals: decLiterals);
                            default:
                                throw new NotSupportedException(
                                    "LOUD whitelist (no runtime descriptor for the TResult struct): " +
                                    "Enumerable.Aggregate TResult " +
                                    $"{GenericArgTypeName(resByte)} is not a supported result family -- " +
                                    "the runtime projects the decimal accumulator to int32/int64/float/" +
                                    "double/decimal/reference only [Reference Source System.Core/System/" +
                                    "Linq/Enumerable.cs:1399]");
                        }
                    }
                    // impl-linq2 (A): a DECIMAL source with a NON-decimal
                    // accumulator. The old mapping silently routed it through
                    // the ref/int32 folds (a wild read of the 16-byte cells).
                    //
                    // impl-loudvalstruct (A): the runtime DOES have a static
                    // kernel for both receiver shapes --
                    //   * List<decimal>: the full list seed + result-selector
                    //     matrix (il2cpp_linq_list_aggregate_seed_<acc>_dec
                    //     [_rs_<res>], linq_list.cpp LST_AGG_SEED_ENTRY(i4_dec,..)
                    //     /i8_dec/d_dec + LST_AGG_RS6(...,dec));
                    //   * decimal[]: the ARRAY CELL MATRIX
                    //     (il2cpp_linq_aggregate_seed_cell_<acc>), whose reader
                    //     (CellAggNext) has an IL2CPP_LINQ_CELL_DECIMAL arm, so
                    //     the 16-byte cell is read at its REAL width and the
                    //     accumulator's own leaf ABI is called. The OLD gate was
                    //     correct only for the pre-cell-matrix kernels.
                    //
                    // The throw therefore survives only where the runtime really
                    // has no decimal-element array fold: count == 4 (the array
                    // cell matrix ships no _rs_<res> entries), and only for a
                    // source PROVEN to be an array (the tracked name ends in
                    // "[]" -- the shape the post-pop re-resolve passes). A
                    // hint-less first pass carries the LAMBDA in arg0Type, so it
                    // binds the array cell matrix and the re-resolve
                    // (StackFold.cs:4870-4878) replaces it: List -> the list
                    // entry, decimal[] -> this LOUD.
                    if (IsLinqDecimalSource(genArgs, receiverType, arg0Type)
                        && IsDecimalSourceNamed(genArgs, genArgNames, receiverType, arg0Type)
                        && acc.Length > 0
                        && count == 4
                        && (receiverType ?? arg0Type) is { } srcName0
                        && srcName0.EndsWith("[]", StringComparison.Ordinal))
                    {
                        throw new NotSupportedException(
                            "Enumerable.Aggregate over a System.Decimal[] source with a non-decimal " +
                            $"accumulator ({GenericArgTypeName(accByte)}) and a result selector is not " +
                            "wired: the array surface has no decimal-element result-selector kernel " +
                            "(the LIST surface has the full matrix) (see docs/audit/b5-gaps.md)");
                    }
                    if (acc.Length == 0 && accByte == 0x11 && IsDecimalGenArg(genArgNames, 1))
                    {
                        // DECIMAL accumulator (net48 Aggregate<TSource,decimal>):
                        // the 16-byte result rides an explicit result slot (the
                        // decimal T3 contract) and the seed crosses as a decimal
                        // value slot.
                        return I("il2cpp::il2cpp_linq_aggregate_seed_dec", 3, true, false, false,
                            new[] { "a", "dec", "" }, resultKind: "dec");
                    }
                    if (acc.Length == 0)
                    {
                        throw new NotSupportedException(
                            $"Enumerable.Aggregate TAccumulate '{GenericArgTypeName(accByte)}' is not a supported accumulator family");
                    }
                    // impl-linqagg (A cluster): a NON-decimal value-type source
                    // (System.DateTime / System.TimeSpan / System.Guid) with an
                    // int32/int64/float/double accumulator. The cells are 8 / 8 /
                    // 16 bytes and the lambda takes the struct BY VALUE, while the
                    // int32-element base kernels (seed_i4/seed_i8/seed_f/seed_d)
                    // read 4 bytes and call through an int32 element slot. The
                    // DECIMAL accumulator family is handled above (impl-aggvt).
                    string? vtAccCell = LinqValueFamilyCellCode(genArgs, genArgNames, receiverType, arg0Type);
                    if (count == 3 && vtAccCell is "12" or "13" or "14"
                        && acc is "i4" or "i8" or "f" or "d")
                    {
                        string vtFam = vtAccCell == "12" ? "dt" : vtAccCell == "13" ? "ts" : "guid";
                        return I($"il2cpp::il2cpp_linq_aggregate_seed_{vtFam}_{acc}",
                            3, true, false, false,
                            new[] { "a", acc switch { "i4" => "i", "i8" => "l", _ => "d" }, "" },
                            resultKind: acc == "i8" ? "l" : acc is "f" or "d" ? "d" : null);
                    }
                    var srcFam = LinqElementFamily(genArgs, receiverType, arg0Type);
                    // B4-1 (P0-06): a ulong accumulator is CARRIED as int64
                    // (same width/bit pattern), so its tracked result type is
                    // Int64 -- the raw UInt64 tracked type is not a known
                    // value slot downstream (the root-clear pass cleared the
                    // int64 temp as if it were a GC root). Values above
                    // long.MaxValue keep their bit pattern but print signed.
                    string? i8Track = accByte == 0x0B ? "System.Int64" : null;
                    if (count == 3)
                    {
                        // impl-arrayagg: the ARRAY (element family, accumulator
                        // family) matrix. The pre-existing entries are
                        // diagonal/one-family kernels whose element reader is
                        // fixed at int32 (seed_i4/seed_i8/seed_d/seed_f) or at
                        // the DIAGONAL width (seed_i8_i8 / seed_d_d); every
                        // NON-DIAGONAL pair bound them and read the wrong
                        // number of bytes out of the cell. The DIAGONAL/int32
                        // cases keep their historical entries; every other
                        // (element, accumulator) pair rides the cell matrix
                        // (seed_cell_<acc>), which reads the element at its
                        // REAL width and calls the accumulator's own leaf ABI.
                        string srcFamCell = LinqSourceFamily(genArgs, receiverType, arg0Type);
                        string? cell = LinqArrayCellCode(genArgs, genArgNames, receiverType, arg0Type);
                        // impl-listvtops: an Aggregate's SOURCE sits BELOW the
                        // seed / lambda on the IL stack, so the hint-less first
                        // pass sees the lambda in arg0Type and cannot tell a
                        // List<DateTime/TimeSpan/Guid/DateTimeOffset> from a
                        // value-struct ARRAY. A 0x11 element that is not a
                        // decimal has no ARRAY cell code, so the matrix below
                        // would throw and abort the whole method fold before
                        // StackFold's post-pop re-resolve can rebind the List
                        // domain (the same deferral the decimal-source arm
                        // already relies on). Answer the int32-element base
                        // entry here -- the shape a List<int> aggregate binds
                        // in this pass -- and let the re-resolve replace it.
                        // A source PROVEN to be an array keeps the LOUD.
                        if (LinqVtAggDeferral(acc, genArgs, genArgNames, receiverType, arg0Type) is { } vtDefer)
                            return vtDefer;
                        switch (acc)
                        {
                            case "i4":
                                if (srcFamCell == "i4")
                                    return I("il2cpp::il2cpp_linq_aggregate_seed_i4", 3, true, false, false,
                                        new[] { "a", "i", "" });
                                if (cell == null)
                                    throw new NotSupportedException(
                                        "Enumerable.Aggregate over this ARRAY element family with an int32 " +
                                        "accumulator is not wired: no cell ABI for the source element");
                                return I("il2cpp::il2cpp_linq_aggregate_seed_cell_i4", 3, true, false, false,
                                    new[] { "a", "i", "" }, appendLiterals: new[] { cell });
                            case "i8":
                                if (srcFamCell == "i4")
                                    return I("il2cpp::il2cpp_linq_aggregate_seed_i8", 3, true, false, false,
                                        new[] { "a", "l", "" }, resultKind: "l", returnTypeName: i8Track);
                                if (srcFamCell == "i8")
                                    return I("il2cpp::il2cpp_linq_aggregate_seed_i8_i8", 3, true, false, false,
                                        new[] { "a", "l", "" }, resultKind: "l", returnTypeName: i8Track);
                                if (cell == null)
                                    throw new NotSupportedException(
                                        "Enumerable.Aggregate over this ARRAY element family with an int64 " +
                                        "accumulator is not wired: no cell ABI for the source element");
                                return I("il2cpp::il2cpp_linq_aggregate_seed_cell_i8", 3, true, false, false,
                                    new[] { "a", "l", "" }, resultKind: "l", returnTypeName: i8Track,
                                    appendLiterals: new[] { cell });
                            case "f":
                                if (srcFamCell == "i4")
                                    return I("il2cpp::il2cpp_linq_aggregate_seed_f", 3, true, false, false,
                                        new[] { "a", "d", "" }, resultKind: "d");
                                if (srcFamCell == "f")
                                    return I("il2cpp::il2cpp_linq_aggregate_seed_f_f", 3, true, false, false,
                                        new[] { "a", "d", "" }, resultKind: "d");
                                if (srcFamCell == "d")
                                    return I("il2cpp::il2cpp_linq_aggregate_seed_f_d", 3, true, false, false,
                                        new[] { "a", "d", "" }, resultKind: "d");
                                if (cell == null)
                                    throw new NotSupportedException(
                                        "Enumerable.Aggregate over this ARRAY element family with a float " +
                                        "accumulator is not wired: no cell ABI for the source element");
                                return I("il2cpp::il2cpp_linq_aggregate_seed_cell_f", 3, true, false, false,
                                    new[] { "a", "d", "" }, resultKind: "d", appendLiterals: new[] { cell });
                            case "d":
                                if (srcFamCell == "i4")
                                    return I("il2cpp::il2cpp_linq_aggregate_seed_d", 3, true, false, false,
                                        new[] { "a", "d", "" }, resultKind: "d");
                                if (srcFamCell == "d")
                                    return I("il2cpp::il2cpp_linq_aggregate_seed_d_d", 3, true, false, false,
                                        new[] { "a", "d", "" }, resultKind: "d");
                                if (cell == null)
                                    throw new NotSupportedException(
                                        "Enumerable.Aggregate over this ARRAY element family with a double " +
                                        "accumulator is not wired: no cell ABI for the source element");
                                return I("il2cpp::il2cpp_linq_aggregate_seed_cell_d", 3, true, false, false,
                                    new[] { "a", "d", "" }, resultKind: "d", appendLiterals: new[] { cell });
                            default:
                                // nb9 H-B6: the ref accumulator reads the SOURCE
                                // element by family (ref pointer, int64, double,
                                // float widened, or int32). A NARROW (1/2-byte)
                                // source needs the cell reader -- the base
                                // int32 read over-reads the cell.
                                //
                                // impl-loudvalstruct (A): a 0x11 VALUETYPE source
                                // (System.Decimal / DateTime / TimeSpan / Guid)
                                // folds into "ref" in LinqSourceFamily, so the
                                // refsrc entry would read an 8-byte reference slot
                                // out of a 16-byte decimal cell (a wild pointer
                                // dereference in the lambda). The CELL MATRIX
                                // (seed_cell_ref) reads the element at its REAL
                                // width with the leaf's own by-value ABI -- the
                                // same entry the narrow families use.
                                if (srcFamCell == "narrow"
                                    || (cell is not null and not "15"))
                                {
                                    if (cell == null)
                                        throw new NotSupportedException(
                                            "Enumerable.Aggregate over this ARRAY element family with a " +
                                            "reference accumulator is not wired: no cell ABI for the source element");
                                    return I("il2cpp::il2cpp_linq_aggregate_seed_cell_ref", 3, true, true, false,
                                        new[] { "a", "", "" }, appendLiterals: new[] { cell });
                                }
                                return I("il2cpp::il2cpp_linq_aggregate_seed_ref" + AggRefSrcSuffix(srcFam),
                                    3, true, true, false, new[] { "a", "", "" });
                        }
                    }
                    // count == 4: TResult decides the result carrier. 0x0B
                    // (ulong) rides the int64 carrier like 0x0A (B4-1/P0-06).
                    bool resRef = resByte is not (0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06
                        or 0x07 or 0x09 or 0x0A or 0x0B or 0x0C or 0x0D);
                    string seedCast = acc switch
                    {
                        "i4" => "i",
                        "i8" => "l",
                        "f" => "d", // float seed rides the F stack family as double
                        "d" => "d",
                        _ => "",
                    };
                    // nb9 H-F3: TResult selects the RESULT ABI, not just the
                    // declared width. The old mapping always used *_rs_i4 and
                    // the int32 projector, truncating long TResult and reading
                    // a float TResult's C++ float return as an integer.
                    string resSuffix = resRef ? "_rs_ref"
                        : resByte is 0x0A or 0x0B ? "_rs_i8"
                        : resByte == 0x0C ? "_rs_f"
                        : resByte == 0x0D ? "_rs_d"
                        : "_rs_i4";
                    // impl-count4fix: the FOLD is picked from the SOURCE ELEMENT
                    // family, exactly like the count==3 matrix -- only the int32
                    // element (and the int64/double DIAGONAL) has a fixed-width
                    // kernel. The old mapping picked it from the ACCUMULATOR
                    // alone, so a float/double/int64/narrow/reference element was
                    // consumed 4 bytes at a time: Aggregate(0.5f, (a,x)=>a+x,
                    // sel) over Select(x=>(float)x) returned the SEED, and a
                    // long[]/double[]/byte[]/string[] source folded the low
                    // 32 bits of every cell -- a SILENT wrong value.
                    string srcFamCell4 = LinqSourceFamily(genArgs, receiverType, arg0Type);
                    string? cell4 = LinqArrayCellCode(genArgs, genArgNames, receiverType, arg0Type);
                    // impl-listvtops: the same hint-less deferral the 3-argument
                    // arm needs (see LinqVtAggDeferral): without it a
                    // List<DateTime/...> Aggregate(seed, func, sel) aborts the
                    // method fold at the ARRAY cell gate before the post-pop
                    // re-resolve can rebind the list `val` entry.
                    if (LinqVtAggDeferral(acc, genArgs, genArgNames, receiverType, arg0Type,
                        resSuffix, resRef) is { } vtDefer4)
                        return vtDefer4;
                    string cppBase;
                    string[]? cell4Literals = null;
                    switch (acc)
                    {
                        case "i4":
                            if (srcFamCell4 == "i4")
                            {
                                cppBase = "il2cpp::il2cpp_linq_aggregate_seed";
                                break;
                            }
                            if (cell4 == null) goto NoCellAbi;
                            cppBase = "il2cpp::il2cpp_linq_aggregate_seed_cell_i4";
                            cell4Literals = new[] { cell4 };
                            break;
                        case "i8":
                            if (srcFamCell4 == "i4")
                            {
                                cppBase = "il2cpp::il2cpp_linq_aggregate_seed_i8";
                                break;
                            }
                            if (srcFamCell4 == "i8")
                            {
                                cppBase = "il2cpp::il2cpp_linq_aggregate_seed_i8_i8";
                                break;
                            }
                            if (cell4 == null) goto NoCellAbi;
                            cppBase = "il2cpp::il2cpp_linq_aggregate_seed_cell_i8";
                            cell4Literals = new[] { cell4 };
                            break;
                        case "f":
                            if (srcFamCell4 == "i4")
                            {
                                cppBase = "il2cpp::il2cpp_linq_aggregate_seed_f";
                                break;
                            }
                            if (cell4 == null) goto NoCellAbi;
                            cppBase = "il2cpp::il2cpp_linq_aggregate_seed_cell_f";
                            cell4Literals = new[] { cell4 };
                            break;
                        case "d":
                            if (srcFamCell4 == "i4")
                            {
                                cppBase = "il2cpp::il2cpp_linq_aggregate_seed_d";
                                break;
                            }
                            if (srcFamCell4 == "d")
                            {
                                cppBase = "il2cpp::il2cpp_linq_aggregate_seed_d_d";
                                break;
                            }
                            if (cell4 == null) goto NoCellAbi;
                            cppBase = "il2cpp::il2cpp_linq_aggregate_seed_cell_d";
                            cell4Literals = new[] { cell4 };
                            break;
                        default:
                            // nb9 H-B6 / impl-arrayagg: the reference accumulator
                            // reads the element by SOURCE family (ref pointer,
                            // int64, double, float widened, int32). A NARROW
                            // (1/2-byte) source needs the cell reader -- the
                            // int32 read over-reads the cell.
                            if (srcFamCell4 == "narrow")
                            {
                                if (cell4 == null) goto NoCellAbi;
                                cppBase = "il2cpp::il2cpp_linq_aggregate_seed_cell_ref";
                                cell4Literals = new[] { cell4 };
                                break;
                            }
                            cppBase = "il2cpp::il2cpp_linq_aggregate_seed_ref" + AggRefSrcSuffix(srcFam);
                            break;
                    }
                    return I(cppBase + resSuffix, 4, true, resRef, false,
                        new[] { "a", seedCast, "", "" },
                        resultKind: resRef ? null
                            : (resByte is 0x0A or 0x0B ? "l"
                               : resByte is 0x0C or 0x0D ? "d" : null),
                        returnTypeName: resByte == 0x0B ? "System.Int64" : null,
                        appendLiterals: cell4Literals);
                NoCellAbi:
                    throw new NotSupportedException(
                        "Enumerable.Aggregate(" + GenericArgTypeName(accByte) + " accumulator) over this " +
                        "ARRAY element family is not wired: no cell ABI exists for the source element, so " +
                        "the fold would read the cell at the wrong width (a silent wrong value). The " +
                        "runtime covers int32/int64/float/double/narrow/reference cells " +
                        "[Reference Source System.Core/System/Linq/Enumerable.cs:1411]");
                }
                return null;
            case "LongCount":
                // LongCount(source) -> il2cpp_linq_long_count: the number of
                // elements as int64 (net48 Enumerable.LongCount returns long).
                return I("il2cpp::il2cpp_linq_long_count", 1, true, false, false, OneArg, resultKind: "l");
            case "SequenceEqual" when count < 3:
                // SequenceEqual(source, second) -> il2cpp_linq_sequence_equal_i4:
                // true iff same length and every pair equal (net48 ordinal).
                return I("il2cpp::il2cpp_linq_sequence_equal_i4", 2, true, false, false,
                    new[] { "a", "a" });
            case "SequenceEqual":
                return I("il2cpp::il2cpp_linq_sequence_equal_value_cmp", 3, true, false, false,
                    new[] { "a", "a", "" }, appendLiterals: new[] { "7" });
            case "DefaultIfEmpty" when count < 2:
                // DefaultIfEmpty(source) -> il2cpp_linq_default_if_empty_i4:
                // non-empty -> source as-is; empty -> new int32[]{0}.
                return I("il2cpp::il2cpp_linq_default_if_empty_i4", 1, true, true, false,
                    OneArg, returnTypeName: "System.Int32[]");
            case "DefaultIfEmpty":
                // DefaultIfEmpty(source, defaultValue): non-empty -> the source
                // as-is; empty -> a one-element array holding `value` at the
                // int32 cell width (net48 Enumerable.cs:1010).
                return I("il2cpp::il2cpp_linq_default_if_empty_family_i4", 2, true, true, false,
                    new[] { "a", "i" }, returnTypeName: "System.Int32[]",
                    appendLiterals: new[] { "7" });
            default:
                return null;
        }
    }

    //--------------------------------------------------------------------------
    // LinqArrayTypeName -- the TRACKED full array type name for an element
    // family byte (GenericArgTypeName returns short names meant for messages).
    //--------------------------------------------------------------------------
    private static string LinqArrayTypeName(byte b) => b switch
    {
        0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "System.Int32[]",
        0x0A or 0x0B => "System.Int64[]",
        0x0C => "System.Double[]",
        0x0D => "System.Double[]",
        0x0E => "System.String[]",
        0x11 => "System.Decimal[]", // P0LINQ1 (D3): the 16-byte decimal array
        _ => "System.Object[]",
    };

    //--------------------------------------------------------------------------
    // P0-10: reference-element family predicates/tracked names. The runtime
    // ref entries accept string[]/object[]/class[]/generic-instance[] as the
    // Il2CppObject* element family (linq_map.h RequireRefFamily).
    //--------------------------------------------------------------------------
    private static bool IsRefFamilyByte(byte b) =>
        b is 0x0E or 0x12 or 0x1C or 0x15 or 0x13 or 0x1E or 0x1D or 0x14 or 0x40;

    private static string LinqRefArrayTypeName(byte[]? genArgs, string? srcType)
    {
        if (genArgs is { Length: >= 1 })
        {
            return genArgs[0] switch
            {
                0x0E => "System.String[]",
                0x1C or 0x12 => "System.Object[]",
                _ => "System.Object[]",
            };
        }
        return srcType != null && srcType.EndsWith("[]", StringComparison.Ordinal)
            ? srcType : "System.Object[]";
    }

    private static string LinqRefElementTypeName(byte[]? genArgs, string? srcType)
    {
        if (genArgs is { Length: >= 1 })
            return genArgs[0] == 0x0E ? "System.String" : "System.Object";
        return srcType != null && srcType.EndsWith("[]", StringComparison.Ordinal)
            ? srcType[..^2] : "System.Object";
    }

    // b5-gaps §54: the class-descriptor EXPRESSION for a Cast/OfType reference
    // TResult. The runtime compares with il2cpp_is_instance, so a boxed element
    // that derives from (or IS) the target passes -- isinst semantics. The
    // primitives/specials resolve to the metadata globals (the same table
    // CodeGenerator's BoxClassDescriptorCpp uses); every other reference type
    // uses the generated `_class` symbol the isinst/castclass emitter already
    // relies on.
    private static string LinqClassSymbolOf(string? typeName) => typeName switch
    {
        null or "?" or "System.Object" => "&System_Object_class",
        "System.String" => "&System_String_class",
        _ => "&" + TypeEmitter.Sanitize(typeName) + "_class",
    };

    //--------------------------------------------------------------------------
    // LinqElementFamily -- the SOURCE element family of an Enumerable operator.
    // Prefers the MethodSpec generic argument (TSource); falls back to the
    // tracked receiver/arg0 array name (a Select result carries
    // "System.Int64[]" / "System.Double[]" / "System.String[]").
    //   i4 | i8 | f | d | ref
    //--------------------------------------------------------------------------
    // nb9 H-B6: the runtime entry suffix for the ref-accumulator fold by the
    // SOURCE element family (the base entry reads int32 elements).
    private static string AggRefSrcSuffix(string srcFam) => srcFam switch
    {
        "ref" => "_refsrc",
        "i8" => "_i8src",
        "d" => "_dsrc",
        "f" => "_fsrc",
        _ => "",
    };

    //--------------------------------------------------------------------------
    // P0LINQ1 (D2): the two families that LinqElementFamily folds into "ref"
    // but that the AGGREGATE surface must tell apart.
    //
    //   * DECIMAL (0x11 VALUETYPE System.Decimal / "System.Decimal[]"): a real
    //     16-byte value family with a decimal kernel -- Sum/Min/Max/Average are
    //     implemented (il2cpp_linq_*_dec).
    //   * STRING (0x0E / "System.String[]"): Min/Max exist in net48 but use
    //     Comparer<string>.Default, i.e. CULTURE-aware collation. There is no
    //     collation infrastructure here, and an ordinal comparison would be a
    //     SILENT WRONG VALUE (probe: new[]{"B","a"}.Min() == "a", while ordinal
    //     compares 'B' (0x42) < 'a' (0x61)). It must stay LOUD.
    //
    // The authoritative key is the MethodSpec generic argument; the tracked
    // receiver/arg0 array name is the fallback (a source-only aggregate called
    // through a non-MethodSpec MemberRef carries LinqAggregateElementByte's
    // element byte, which for decimal/string is 0x11/0x0E).
    //--------------------------------------------------------------------------
    private static bool IsLinqDecimalSource(byte[]? genArgs, string? receiverType, string? arg0Type)
    {
        if (genArgs is { Length: >= 1 }) return genArgs[0] == 0x11;
        var name = receiverType ?? arg0Type;
        return name != null && name.EndsWith("System.Decimal[]", StringComparison.Ordinal);
    }

    // impl-linq2 (A): is the 0x11 VALUETYPE source really System.Decimal? The
    // 0x11 byte covers every value type, so the decoded MethodSpec FULL NAME is
    // the authority -- the same rule LinqFamilyAggregate applies.
    //
    // impl-linq-0x11 (audit loud-inv §3.1 Δ0): an UNDECODABLE name must never be
    // assumed to be decimal. The old fallback treated genArgNames == "?" (the
    // decoder's placeholder for a VALUETYPE generic argument whose TypeDefOrRef
    // was not resolvable) as System.Decimal, so ANY 0x11 source with a lost name
    // bound the 16-byte decimal kernels -- a SILENT wrong value (an 8-byte
    // DateTime cell read as a decimal half, a 4-byte read of the same array ...).
    // NOTE the "?" placeholder is generic in GenericArgFullNames: it also stands
    // for slots the name table never names (int/long/VAR/arrays). For THIS
    // function "?" only has the "lost 0x11 type name" meaning because every
    // caller asks after IsLinqDecimalSource/IsLinqValueTypeSource proved
    // genArgs[0] == 0x11 (a legal TypeDefOrRef cannot decode to nil, so the
    // placeholder here comes from truncated/corrupt metadata only).
    // The rule now is "prove it or refuse":
    //   * decoded name System.Decimal -> decimal;
    //   * decoded/tracked DateTime/TimeSpan/Guid family -> NOT decimal (the
    //     family kernels that exist use the real cell code, everything else
    //     stays LOUD);
    //   * tracked source array System.Decimal[] -> decimal;
    //   * a NULL name table is the NON-generic MemberRef surface
    //     (LinqAggregateElementByte): on net48 its only 0x11 overload is
    //     System.Decimal (int/long/float/double have their own bytes and the
    //     Nullable<T> forms are GenericInst 0x15), so the historical decimal
    //     answer is kept for it;
    //   * anything else (a "?" name with no tracked array, a user struct, an
    //     empty name) answers false -- the caller falls through to the LOUD
    //     gates instead of fabricating a decimal binding.
    //
    // INVARIANT behind the NULL-table arm (`genArgNames is null &&
    // genArgs[0] == 0x11`) -- do not read it as "any non-MethodSpec 0x11 source
    // is decimal":
    //   (a) a MethodSpec can never produce a null table with a 0x11 slot:
    //       GenericArgFullNames sets any=true for every 0x11/0x12/0x40/0x15
    //       argument (MethodRefResolver.cs:2933-2967) and returns null only
    //       when NO argument is nameful (:2985);
    //   (b) the only producer of genArgs outside the MethodSpec path is the
    //       LinqAggregateElementByte backfill, gated to
    //       Sum/Min/Max/Average with no MethodSpec (MethodRefResolver.cs:2302-2303).
    // If a future operator also backfills a non-generic element byte, this arm
    // must be re-checked (restrict by operator or require a tracked
    // System.Decimal[]): the net48 "0x11 => decimal" argument is specific to
    // the four non-generic aggregate overloads.
    private static bool IsDecimalSourceNamed(byte[]? genArgs, string[]? genArgNames,
        string? receiverType, string? arg0Type)
    {
        var tracked = receiverType ?? arg0Type;
        bool trackedDecimal = tracked != null
            && tracked.EndsWith("System.Decimal[]", StringComparison.Ordinal);
        if (genArgs is not { Length: >= 1 }) return trackedDecimal;
        if (IsDecimalGenArg(genArgNames, 0)) return true;
        if (LinqValueFamilyCellCode(genArgs, genArgNames, receiverType, arg0Type) is not null)
            return false;
        if (trackedDecimal) return true;
        return genArgNames is null && genArgs[0] == 0x11;
    }

    // IsLinqValueTypeSource -- the source element is a 0x11 VALUETYPE (decimal /
    // DateTime / TimeSpan / Guid / user struct). LinqSourceFamily folds 0x11 into
    // "ref", but the reference-source kernels must NOT serve a value-typed
    // element (see the ref-block guard above).
    private static bool IsLinqValueTypeSource(byte[]? genArgs, string? receiverType, string? arg0Type)
    {
        if (genArgs is { Length: >= 1 }) return genArgs[0] == 0x11;
        var name = receiverType ?? arg0Type;
        return name != null && (name.EndsWith("System.Decimal[]", StringComparison.Ordinal)
            || name.EndsWith("System.DateTime[]", StringComparison.Ordinal)
            || name.EndsWith("System.TimeSpan[]", StringComparison.Ordinal)
            || name.EndsWith("System.Guid[]", StringComparison.Ordinal)
            || name.EndsWith("System.DateTimeOffset[]", StringComparison.Ordinal));
    }

    // IsLinqNonDecimalValueTypeSource -- the source element is a 0x11
    // VALUETYPE that is NOT System.Decimal (DateTime / TimeSpan / Guid / a user
    // struct). The decimal source has its own kernels (count_pred_dec /
    // any_dec / ...); every other value family has NO predicate kernel, so the
    // int32-family entries (count_i4 / any_i4) must not serve them -- they
    // call the predicate through the int32 ABI and read 4-byte cells out of a
    // source whose payload is 8/16 bytes wide (a SILENT wrong count).
    private static bool IsLinqNonDecimalValueTypeSource(byte[]? genArgs, string[]? genArgNames,
        string? receiverType, string? arg0Type)
    {
        if (!IsLinqValueTypeSource(genArgs, receiverType, arg0Type)) return false;
        return !IsDecimalSourceNamed(genArgs, genArgNames, receiverType, arg0Type);
    }

    // The IL2CPP_LINQ_CELL_* code of a VALUE-TYPE source (linq_family.h):
    // System.DateTime -> 12, System.TimeSpan -> 13, System.Guid -> 14. The
    // 0x11 dispatch byte covers EVERY value type, so the NAME decides (the
    // same authority IsDecimalSourceNamed applies to the decimal source); the
    // tracked array name is the fallback when the generic-argument name table
    // could not be decoded. A USER STRUCT has no cell ABI (null -> the
    // operator stays LOUD: the family kernels know decimal / DateTime /
    // TimeSpan / Guid only).
    private static string? LinqValueFamilyCellCode(byte[]? genArgs, string[]? genArgNames,
        string? receiverType, string? arg0Type)
    {
        if (genArgs is not { Length: >= 1 } || genArgs[0] != 0x11) return null;
        string? name = genArgNames is { Length: >= 1 } ? genArgNames[0] : null;
        if (name is { Length: > 0 } && name != "?")
        {
            if (name.StartsWith("V:", StringComparison.Ordinal)) name = name[2..];
            return name switch
            {
                "System.DateTime" => "12",
                "System.TimeSpan" => "13",
                "System.Guid" => "14",
                "System.DateTimeOffset" => "16",
                _ => null,
            };
        }
        var tracked = receiverType ?? arg0Type;
        if (tracked == null) return null;
        if (tracked.EndsWith("System.DateTime[]", StringComparison.Ordinal)) return "12";
        if (tracked.EndsWith("System.TimeSpan[]", StringComparison.Ordinal)) return "13";
        if (tracked.EndsWith("System.Guid[]", StringComparison.Ordinal)) return "14";
        if (tracked.EndsWith("System.DateTimeOffset[]", StringComparison.Ordinal)) return "16";
        return null;
    }

    // ValueTypeSourceName -- the source element name for a LOUD message: the
    // decoded generic-argument name when it is known, else the tracked array
    // name, else the element byte's generic name.
    private static string ValueTypeSourceName(byte[]? genArgs, string[]? genArgNames,
        string? receiverType, string? arg0Type)
    {
        if (genArgNames is { Length: >= 1 } && genArgNames[0] != "?") return genArgNames[0];
        var tracked = receiverType ?? arg0Type;
        if (tracked != null) return tracked;
        return genArgs is { Length: >= 1 } ? GenericArgTypeName(genArgs[0]) : "?";
    }

    private static bool IsLinqStringSource(byte[]? genArgs, string? receiverType, string? arg0Type)
    {
        if (genArgs is { Length: >= 1 }) return genArgs[0] == 0x0E;
        var name = receiverType ?? arg0Type;
        return name != null && name.EndsWith("System.String[]", StringComparison.Ordinal);
    }

    // IsDecimalGenArg -- is the generic-argument slot at 'index' really
    // System.Decimal? The 0x11 byte covers EVERY value type (decimal, DateTime,
    // user structs), so the decoded MethodSpec FULL NAMES are the only
    // authority. A null name table (a shape that carries no names) answers
    // false: a fabricated decimal kernel for a struct result would be a wild
    // 16-byte read.
    private static bool IsDecimalGenArg(string[]? genArgNames, int index) =>
        genArgNames is { } names && index < names.Length && names[index] == "System.Decimal";

    // impl-linq3 (B): the name-aware STRUCT key family of a 0x11 generic
    // argument slot: "dt" for System.DateTime, "guid" for System.Guid, null
    // for every other value type (a user struct has no field-wise comparison
    // carrier and stays LOUD). The GroupBy key-marker dispatch (LinqGroupingKeyMarker)
    // accepts the bare type names too; a 0x11 VALUETYPE arg always carries a
    // resolvable full name here, so the bare aliases are accepted defensively
    // (the same "V:" prefix the tracked-type names carry).
    private static string? LinqStructKeyFamilyName(string[]? genArgNames, int index)
    {
        if (genArgNames is not { } names || index >= names.Length) return null;
        var n = names[index];
        if (n is "System.DateTime" or "V:System.DateTime" or "DateTime") return "dt";
        if (n is "System.Guid" or "V:System.Guid" or "Guid") return "guid";
        return null;
    }

    // impl-linqleft: the DECIMAL-element fused-composite key-family CODE of a
    // generic key argument (the trailing literal of the runtime dispatch):
    //   0 int32 (incl. bool/char/narrow), 1 int64, 2 double,
    //   3 decimal, 4 System.DateTime, 5 System.Guid, 6 float.
    // impl-orderfam: float (0x0C) is deliberately SEPARATE from double (0x0D)
    // -- a Func<decimal,float> leaf returns a 4-byte float per the MS x64 ABI,
    // and the old "2" fold read it through the double(*) reader, mixing the
    // callee's XMM0 residue into every key (the b5-gaps §14 misread,
    // thenby-decfkey). The pre-existing 0..5 codes keep their meaning; the
    // float column mirrors the impl-floatkey LinqValKeyFamilyCode split on the
    // runtime SortDecTwoKeysDispatch matrix (code 6).
    // null when the key has no composite carrier (a user struct / reference).
    private static string? LinqKeyFamilyCode(byte b, string[]? genArgNames, int index) => b switch
    {
        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "0",
        0x0A or 0x0B => "1",
        0x0D => "2",
        0x0C => "6",
        0x11 when IsDecimalGenArg(genArgNames, index) => "3",
        0x11 when LinqStructKeyFamilyName(genArgNames, index) == "dt" => "4",
        0x11 when LinqStructKeyFamilyName(genArgNames, index) == "guid" => "5",
        _ => null,
    };

    // impl-floatkey: the NUMERIC-element fused-composite key-family CODE (the
    // il2cpp_linq_then_by_key_val_i4 / _val_i8 dispatch):
    //   0 int32 (incl. bool/char/narrow), 1 int64, 2 double, 3 float.
    // float (0x0C) is deliberately SEPARATE from double (0x02): a
    // Func<TSource,float> leaf returns a 4-byte float per the MS x64 ABI, and
    // reading that return through a double(*) fn-pointer mixes the callee's
    // XMM0 residue into every key -- the silent mis-order b5-gaps §14
    // measured (thenby-floatprobe). This code space is disjoint from the
    // DECIMAL composite's LinqKeyFamilyCode (which also assigns 3=decimal,
    // 4=DateTime, 5=Guid on its own runtime dispatch).
    private static string? LinqValKeyFamilyCode(byte b) => b switch
    {
        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "0",
        0x0A or 0x0B => "1",
        0x0D => "2",
        0x0C => "3",
        _ => null,
    };

    private static string LinqElementFamily(byte[]? genArgs, string? receiverType, string? arg0Type)
    {
        if (genArgs is { Length: >= 1 })
        {
            switch (genArgs[0])
            {
                case 0x08: case 0x02: case 0x03: case 0x04:
                case 0x05: case 0x06: case 0x07: case 0x09:
                    return "i4";
                case 0x0A: case 0x0B: return "i8";
                case 0x0C: return "f";
                case 0x0D: return "d";
                default: return "ref";
            }
        }
        var name = receiverType ?? arg0Type;
        if (name != null)
        {
            if (name.EndsWith("System.Int32[]", StringComparison.Ordinal)) return "i4";
            if (name.EndsWith("System.Int64[]", StringComparison.Ordinal)) return "i8";
            if (name.EndsWith("System.Single[]", StringComparison.Ordinal)) return "f";
            if (name.EndsWith("System.Double[]", StringComparison.Ordinal)) return "d";
            if (name.EndsWith("[]", StringComparison.Ordinal)) return "ref";
        }
        return "i4";
    }

    //--------------------------------------------------------------------------
    // LinqFamilyAggregate -- the SOURCE-ONLY Sum/Min/Max/Average overloads.
    // The result carrier follows the element family (net48):
    //   int32  -> int32   (Average -> double)
    //   int64  -> int64   (Average -> double)
    //   float  -> float   (Average -> float)
    //   double -> double  (Average -> double)
    //   decimal -> decimal (Sum/Min/Max/Average all return decimal -- the
    //             16-byte value slot, so the icall takes the result slot FIRST
    //             and ResultKind "dec" carries that shape to the emitter)
    // A STRING source (Min/Max) is rejected LOUDLY here: net48's
    // Comparer<string>.Default is CULTURE-aware (probe: new[]{"B","a"}.Min()
    // is "a" -- ordinal would answer "B"), and this runtime has no collation
    // infrastructure, so an ordinal kernel would be a SILENT WRONG VALUE (the
    // loud-p0-14-strkey-loud precedent).
    //--------------------------------------------------------------------------
    private static ResolvedMethod? LinqFamilyAggregate(string name, byte[]? genArgs,
        string? receiverType, string? arg0Type, string[]? genArgNames = null)
    {
        // decimal and string are BOTH "ref" in LinqElementFamily (0x11 / 0x0E
        // fall into the default arm). The aggregate surface must tell them
        // apart BEFORE that table runs: decimal has a real 16-byte kernel,
        // while string Min/Max is culture-aware and must stay LOUD.
        if (IsLinqStringSource(genArgs, receiverType, arg0Type))
        {
            // impl-collation: string Min/Max is now REAL. net48's
            // Comparer<string>.Default is CultureInfo.CurrentCulture.CompareInfo
            // with CompareOptions.None, i.e. CULTURE collation ("a" sorts before
            // "B"; for a case-only difference the lowercase string wins). The
            // runtime answers it with runtime::il2cpp_string_compare_culture --
            // the same OS NLS primitive (CompareStringEx with the current
            // locale) net48 itself uses on Windows -- so this is not an ordinal
            // shortcut. net48 semantics the kernels reproduce: an empty or
            // all-null sequence yields null, a null never wins once a value
            // exists, and the winning ELEMENT is returned.
            //
            // Sum/Average have no System.String overload in net48 at all, so a
            // source that reached here can only be a malformed program; keep
            // them LOUD rather than inventing a numeric result.
            if (name is "Min" or "Max")
                return I(name == "Min" ? "il2cpp::il2cpp_linq_min_string"
                                       : "il2cpp::il2cpp_linq_max_string",
                    1, true, true, false, new[] { "a" }, returnTypeName: "System.String");
            throw new NotSupportedException(
                $"Enumerable.{name}<System.String> not wired: net48 has no Sum/Average " +
                "overload for System.String [Reference Source: " +
                "System.Core/System/Linq/Enumerable.cs]");
        }
        if (IsLinqDecimalSource(genArgs, receiverType, arg0Type))
        {
            // The 0x11 byte covers EVERY value type (decimal, DateTime,
            // TimeSpan, Guid, user structs). Only a REAL System.Decimal source
            // has the 16-byte aggregate kernel; routing DateTime/TimeSpan/Guid
            // through il2cpp_linq_*_dec emitted an Il2CppDateTime*/Il2CppGuid*
            // result slot into an Il2CppDecimal* parameter -- the zmatrix
            // dt/ts/gd Min/Max compile failures. Keep those LOUD.
            // An UNKNOWN name (the synthetic "?" the name table emits for a
            // VALUETYPE arg with no resolvable TypeRef) is NOT decimal any more
            // (impl-linq-0x11 / audit loud-inv §3.1 Δ0): the old code kept the
            // historical decimal-kernel answer for "?", so a DateTime/TimeSpan/
            // Guid source whose name was lost bound the 16-byte dec kernel -- a
            // SILENT wrong value. The single authority is IsDecimalSourceNamed
            // (decoded name / tracked Decimal[] / the NULL-table non-generic
            // MemberRef surface, whose only 0x11 overload IS decimal); every
            // other unknown shape falls into the non-decimal branch below and
            // stays LOUD rather than borrowing a foreign ABI.
            //
            // A NULL name table means genArgs came from
            // LinqAggregateElementByte, i.e. a NON-generic (header 0x00)
            // MemberRef. On this concrete surface the only value-type element
            // overload is System.Decimal (int/long/float/double have dedicated
            // non-generic overloads with their own bytes, and Min/Max<object>
            // is 0x0E reference), so a null table with a 0x11 byte IS decimal
            // unless the tracked source array names a DateTime/TimeSpan/Guid
            // family. A MethodSpec always carries a name for a 0x11 arg (at
            // least "?"), so the null table can never hide a DateTime/TimeSpan/
            // Guid source and the LOUD guard above stays intact.
            bool isDecimal = IsDecimalSourceNamed(genArgs, genArgNames, receiverType, arg0Type);
            if (!isDecimal)
            {
                // delta 1: DateTime / TimeSpan / Guid are COMPARABLE value types
                // with a net48 Min<TSource>/Max<TSource> overload; they order by
                // ticks / field-wise Guid CompareTo. Net48 has no Sum/Average
                // for them (non-numeric), and a user struct has no comparison
                // carrier, so those stay LOUD.
                string? aggCell = LinqValueFamilyCellCode(genArgs, genArgNames, receiverType, arg0Type);
                if ((name == "Min" || name == "Max")
                    && aggCell is "12" or "13" or "14" or "16")
                {
                    string aggFam = aggCell == "12" ? "dt"
                        : aggCell == "13" ? "ts"
                        : aggCell == "14" ? "guid" : "dtoffset";
                    // A Guid rides the BY-VALUE return kind "guidv" (the
                    // GuidValueBytePtr/result-slot contract); DateTime/TimeSpan
                    // return their runtime struct by value directly and the
                    // 16-byte DateTimeOffset carrier is "dto".
                    string aggKind = aggFam switch
                    {
                        "guid" => "guidv",
                        "dtoffset" => "dto",
                        _ => aggFam,
                    };
                    return I($"il2cpp::il2cpp_linq_{name.ToLowerInvariant()}_{aggFam}",
                        1, true, false, false, new[] { "a" }, resultKind: aggKind);
                }
                throw new NotSupportedException(
                    $"Enumerable.{name} over a value-type source is not wired: the source-only " +
                    "aggregate surface covers int32/int64/float/double/decimal, plus " +
                    "Min/Max over System.DateTime/System.TimeSpan/System.Guid/" +
                    "System.DateTimeOffset (Comparer<T>.Default); Sum/Average and user " +
                    "structs stay LOUD " +
                    "[Reference Source: System.Core/System/Linq/Enumerable.cs]");
            }
            // decimal: the 16-byte result slot (decimal T3 contract). The
            // kernel returns decimal for Average too (net48's overload).
            return I($"il2cpp::il2cpp_linq_{name.ToLowerInvariant()}_dec", 1, true, false, false,
                new[] { "a" }, resultKind: "dec");
        }
        var fam = LinqElementFamily(genArgs, receiverType, arg0Type);
        if (fam == "ref") return null;
        bool avg = name == "Average";
        switch (fam)
        {
            case "i4":
                return avg
                    ? I("il2cpp::il2cpp_linq_average_i4", 1, true, false, false, new[] { "a" }, resultKind: "d")
                    : I($"il2cpp::il2cpp_linq_{name.ToLowerInvariant()}_i4", 1, true, false, false, new[] { "a" });
            case "i8":
                return avg
                    ? I("il2cpp::il2cpp_linq_average_i8", 1, true, false, false, new[] { "a" }, resultKind: "d")
                    : I($"il2cpp::il2cpp_linq_{name.ToLowerInvariant()}_i8", 1, true, false, false,
                        new[] { "a" }, resultKind: "l");
            case "f":
                return avg
                    ? I("il2cpp::il2cpp_linq_average_f", 1, true, false, false, new[] { "a" }, resultKind: "d")
                    : I($"il2cpp::il2cpp_linq_{name.ToLowerInvariant()}_f", 1, true, false, false,
                        new[] { "a" }, resultKind: "d");
            default:
                return avg
                    ? I("il2cpp::il2cpp_linq_average_d", 1, true, false, false, new[] { "a" }, resultKind: "d")
                    : I($"il2cpp::il2cpp_linq_{name.ToLowerInvariant()}_d", 1, true, false, false,
                        new[] { "a" }, resultKind: "d");
        }
    }

    //--------------------------------------------------------------------------
    // nb9 FIX helpers -- overload discrimination by the ENCODED PARAMETER TYPE
    // TEXT (MethodSignatureCoder.Decode(...).ParamTypes, the same authority the
    // DateTimeOffset overload dispatch uses). A GENERICINST parameter encodes
    // as "r:Definition`N<args>", so the generic DEFINITION name is everything
    // before the first '<'. The raw parameter bytes CANNOT tell
    // IEqualityComparer`1 from Func`2 / Func`3 (all 0x15).
    //--------------------------------------------------------------------------
    private static string[] LinqEncodedParamTypes(MetadataReader md, BlobHandle sigHandle)
    {
        var pt = MethodSignatureCoder.Decode(md, sigHandle).ParamTypes;
        return pt.Length == 0 ? Array.Empty<string>() : CodeGenerator.SplitTopLevel(pt).ToArray();
    }

    // impl-vtsel: the STRUCT-SOURCE key-family literal for
    // il2cpp_linq_order_by_key_vt / il2cpp_linq_then_by_key_vt:
    //   0 int32 | 1 int64 | 2 double | 3 float | 4 DateTime | 5 Guid |
    //   6 TimeSpan | 7 DateTimeOffset
    // Anything else (decimal, a reference key, a user struct) has no key reader
    // over a value-struct element source -> null (the caller LOUDs).
    private static string? LinqVtKeyFamilyCode(byte[]? genArgs, string[]? genArgNames, int index)
    {
        if (genArgs is null || genArgs.Length <= index) return null;
        return genArgs[index] switch
        {
            0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "0",
            0x0A or 0x0B => "1",
            0x0D => "2",
            0x0C => "3",
            0x11 => LinqStructResultSuffix(genArgNames, index) switch
            {
                "dt" => "4",
                "guid" => "5",
                "ts" => "6",
                "dtoffset" => "7",
                _ => null,
            },
            _ => null,
        };
    }

    // impl-vtsel: the GroupBy BUCKET element-family code for an arbitrary
    // TElement byte (the 0x11 value types are name-resolved):
    //   0 int32 | 1 ref | 2 int64 | 3 double | 4 decimal | 5 DateTime |
    //   6 Guid | 7 float | 8 TimeSpan | 9 DateTimeOffset
    private static string LinqGroupElemFamilyCode(byte b, string[]? genArgNames, int index)
    {
        if (b == 0x11)
        {
            return LinqStructResultSuffix(genArgNames, index) switch
            {
                "dt" => "5",
                "guid" => "6",
                "ts" => "8",
                "dtoffset" => "9",
                _ => IsDecimalGenArg(genArgNames, index) ? "4" : "",
            };
        }
        return b switch
        {
            0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "0",
            0x0A or 0x0B => "2",
            0x0D => "3",
            0x0C => "7",
            0x0E or 0x12 or 0x1C or 0x1D or 0x15 or 0x13 or 0x1E => "1",
            _ => "",
        };
    }

    // impl-vtsel: the GroupBy RESULT-selector family code (the runtime rsKind).
    private static string LinqGroupResultFamilyCode(byte b) => b switch
    {
        0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "1",
        0x0A or 0x0B => "3",
        0x0D => "4",
        0x0C => "5",
        _ => "2",
    };

    // impl-vtsel: the TRACKED result type of a GroupBy whose result selector
    // runs (rsKind 0 keeps the grouping pseudo-type).
    private static string LinqGroupResultTypeName(string rk) => rk switch
    {
        "0" => "System.Linq.IGroupingResult",
        "1" => "System.Int32[]",
        "3" => "System.Int64[]",
        "4" or "5" => "System.Double[]",
        _ => "System.Object[]",
    };

    //--------------------------------------------------------------------------
    // B4-1 (P0-04): KeyKindFor -- the GroupBy KEY family code the runtime uses
    // to narrow a narrow-TKey selector result back to its declared width
    // (linq_map.cpp NarrowKey). MUST stay in lockstep with the runtime switch
    // and with the GroupBy gate: a family without a correct narrowing rule has
    // no key carrier and must be LOUD, never bucketed from a raw register read.
    //   -1 unsupported (LOUD) | 0 int32 | 1 uint8 | 2 int8 | 3 uint16 |
    //    4 int16 | 5 uint32 | 6 bool
    // char (0x03) is a 16-bit unsigned value in an int32 slot -> uint16 rule.
    //--------------------------------------------------------------------------
    private static int KeyKindFor(byte keyByte) => keyByte switch
    {
        0x08 => 0, // int32   (identity)
        0x05 => 1, // byte
        0x04 => 2, // sbyte
        0x07 => 3, // ushort
        0x03 => 3, // char -> uint16 narrowing
        0x06 => 4, // short
        0x09 => 5, // uint (bit pattern; equality only)
        0x02 => 6, // bool
        0x0A or 0x0B => 7, // BATCH4: long / ulong (the 64-bit key carrier)
        // floatgrp: the System.Double key. The carrier keeps the raw 64-bit
        // image and the equality kernel compares VALUES (NaN == NaN, +0 == -0
        // -- net48 Double.Equals), never the bit pattern. A System.Single KEY
        // stays LOUD: its leaf returns in the 32-bit XMM lane, a different
        // return ABI from this double carrier.
        0x0D => 15,
        _ => -1,   // decimal/DateTime/Guid (name-resolved below) / reference: LOUD
    };

    // BATCH4: the name-aware KeyKindFor. A 0x11 VALUETYPE key byte is shared by
    // every struct/enum, so the byte alone cannot say whether the key is a
    // System.Decimal / System.DateTime / System.Guid (all wireable through the
    // 16-byte key carrier) or a user struct (still LOUD). The generic-argument
    // NAME is the authority (the same metadata-first rule LinqListKind uses).
    private static int KeyKindForNamed(byte keyByte, string[]? genArgNames, int index)
    {
        if (keyByte == 0x11 && genArgNames is { Length: > 0 } && index < genArgNames.Length)
        {
            var n = genArgNames[index];
            if (n is "System.Decimal" or "decimal") return 8;
            if (n is "System.DateTime" or "DateTime") return 9;
            if (n is "System.Guid" or "Guid") return 10;
            // impl-groupbykey: the remaining wireable value-type key families.
            //   System.TimeSpan       -> ticks carrier (12)
            //   System.DateTimeOffset -> UTC instant + offset carrier (13)
            //   a USER ENUM           -> its int32 value__ (14): the translated
            //                            enum is a 16-byte struct with the value
            //                            at offset 8, so the key selector's
            //                            return is a 16-byte aggregate whose
            //                            payload the runtime reads at that
            //                            offset (Il2CppEnumKeyImage).
            //   a BCL ENUM            -> the int32 slot (0): the translated
            //                            value of a whitelisted BCL enum is a
            //                            plain int32_t, not a struct.
            // Anything else (a user STRUCT) still has no key carrier -> -1.
            if (n is "System.TimeSpan" or "TimeSpan") return 12;
            if (n is "System.DateTimeOffset" or "DateTimeOffset") return 13;
            if (IsUserEnumTypeName(n)) return 14;
            if (CodeGenerator.IsKnownBclEnumName(n)) return 0;
        }
        // p0grp: a REFERENCE key (System.String / class / object) rides the
        // bucket's keyRef slot (keyKind 11).
        if (IsRefFamilyByte(keyByte)) return 11;
        return KeyKindFor(keyByte);
    }

    // impl-groupbykey: is `name` a USER enum (a TypeDef of THIS session whose
    // base is System.Enum)? The metadata is the authority (the same rule
    // TypeEmitter's field-shape resolver uses), so an enum is never confused
    // with an arbitrary user struct that shares the 0x11 byte.
    private static bool IsUserEnumTypeName(string name)
    {
        if (name.StartsWith("V:", StringComparison.Ordinal)) name = name[2..];
        if (name.IndexOf('.') < 0) return false;
        if (TypeEmitter.SessionTypes is not { } knownTypes) return false;
        var def = knownTypes.FirstOrDefault(t => t.MatchesFullName(name));
        return def != null && def.BaseTypeName == "System.Enum";
    }

    // BATCH4: the grouping key-family marker carried in the tracked enumerator /
    // grouping type names so get_Current and get_Key can dispatch on the REAL
    // key family. The marker is decided at the GroupBy call and flows through
    // the IEnumerator<IGrouping<TKey,TElement>> instantiation name.
    private static string LinqGroupingKeyMarker(string? keyTypeName) =>
        keyTypeName switch
        {
            "long" or "ulong" or "System.Int64" or "System.UInt64" => "_key_i8",
            "System.Decimal" or "decimal" => "_key_dec",
            "System.DateTime" or "DateTime" => "_key_dt",
            "System.Guid" or "Guid" => "_key_guid",
            // p0grp: a REFERENCE key (System.String / class / object).
            "string" or "String" or "System.String" => "_key_ref",
            // impl-groupbykey: the TimeSpan (ticks) and DateTimeOffset (UTC
            // instant + offset) key carriers. A user-ENUM key keeps the DEFAULT
            // int32 get_Key accessor (its payload is value__), so it needs no
            // marker.
            "System.TimeSpan" or "TimeSpan" => "_key_ts",
            "System.DateTimeOffset" or "DateTimeOffset" => "_key_dto",
            // floatgrp: a System.Double key (the raw 64-bit image; the get_Key
            // accessor returns it as a double).
            "System.Double" or "double" => "_key_d",
            _ => "",
        };

    // Extract the TKey NAME from an IGrouping`2<TKey,TElement> instantiation
    // name ("...IGrouping`2_long_int" -> "long"). The token ends at the first
    // '_' after the marker; a nested generic key (List`1_int) is not a wireable
    // key family, so its token simply yields the empty marker.
    private static string? LinqGroupingKeyTypeName(string? instName)
    {
        if (instName == null) return null;
        const string marker = "IGrouping`2_";
        int idx = instName.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        int start = idx + marker.Length;
        int end = instName.IndexOf('_', start);
        string token = end < 0 ? instName[start..] : instName[start..end];
        return token.Length > 0 ? token : null;
    }

    // D6: the rejected GroupBy KEY described in the LOUD message. The old
    // template called every non-integer key a "value-type", so a ulong key was
    // reported as "value-type (0x0B)" -- misleading, since 0x0B is a 64-bit
    // integer. Split the two cases so the message names the real family.
    private static string DescribeRejectedKey(byte keyByte, string keyArgType) => keyByte switch
    {
        0x0A => $"a 64-bit integer (0x0A long / {keyArgType}) key",
        0x0B => $"a 64-bit unsigned integer (0x0B ulong / {keyArgType}) key",
        0x11 => $"a value-type ({keyArgType}) key",
        0x0E => $"a reference ({keyArgType}) key",
        _ => $"a non-integer ({keyArgType}) key",
    };

    private static string GenericDefNameOf(string encoded)
    {
        var s = encoded.StartsWith("r:", StringComparison.Ordinal) ? encoded[2..] : encoded;
        var lt = s.IndexOf('<');
        return lt >= 0 ? s[..lt] : s;
    }

    //--------------------------------------------------------------------------
    // LinqSelectorProjectionType -- the encoded RESULT type of the SECOND
    // parameter when it is a Func`2 ("r:System.Func`2<i,i>" -> "i"), or "" when
    // the shape does not apply. The selector-aggregate overloads
    // (Sum/Min/Max/Average(source, Func<TSource,TResult>)) are generic only in
    // TSource, so TResult is NOT in genArgs -- it must come from the
    // parameter's own generic argument list.
    //--------------------------------------------------------------------------
    //--------------------------------------------------------------------------
    // nb15 B5 FIX -- source-family / List carrier resolution.
    //
    // LinqSourceFamily is the AUTHORITATIVE element family:
    //   i4 | narrow | i8 | f | d | ref
    // "narrow" is the 1/2/4-byte integral family whose CLR parameter ABI is the
    // int32 slot (byte/sbyte/short/ushort/char/bool/uint) -- the old code folded
    // it into "i4" and read 4 bytes from a 1/2-byte cell (the D-1 over-read).
    //--------------------------------------------------------------------------
    private static string LinqSourceFamily(byte[]? genArgs, string? receiverType, string? arg0Type)
    {
        if (genArgs is { Length: >= 1 })
        {
            return genArgs[0] switch
            {
                0x08 => "i4",
                0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "narrow",
                0x0A or 0x0B => "i8",
                0x0C => "f",
                0x0D => "d",
                _ => "ref",
            };
        }
        var name = receiverType ?? arg0Type;
        if (name != null)
        {
            if (name.EndsWith("System.Int32[]", StringComparison.Ordinal)) return "i4";
            if (name.EndsWith("System.Int64[]", StringComparison.Ordinal)
                || name.EndsWith("System.UInt64[]", StringComparison.Ordinal)) return "i8";
            if (name.EndsWith("System.Single[]", StringComparison.Ordinal)) return "f";
            if (name.EndsWith("System.Double[]", StringComparison.Ordinal)) return "d";
            if (name.EndsWith("System.Byte[]", StringComparison.Ordinal)
                || name.EndsWith("System.SByte[]", StringComparison.Ordinal)
                || name.EndsWith("System.Int16[]", StringComparison.Ordinal)
                || name.EndsWith("System.UInt16[]", StringComparison.Ordinal)
                || name.EndsWith("System.Char[]", StringComparison.Ordinal)
                || name.EndsWith("System.Boolean[]", StringComparison.Ordinal)
                || name.EndsWith("System.UInt32[]", StringComparison.Ordinal)) return "narrow";
            if (name.EndsWith("[]", StringComparison.Ordinal)) return "ref";
        }
        return "i4";
    }

    // The narrow CELL kind code (linq.h IL2CPP_LINQ_NARROW_*) of a generic
    // argument byte; null when the byte is not a narrow integral family.
    // The KEY-family suffix of the reference-source OrderBy entries
    // (il2cpp_linq_order_by_key_ref_<suffix>): "" when the key family has no
    // kernel. int32-slot primitives (incl. the narrow ones and bool/char) ride
    // the i4 entry, double rides the d entry (the F-stack carrier), and float
    // rides its OWN f entry (impl-orderfam: a Func<T,float> leaf returns a
    // 4-byte float per the MS x64 ABI; the shared _ref_d entry read it through
    // a double(*) fn-pointer and mixed the callee's XMM0 residue into every
    // key -- the b5-gaps §14 misread, the thenby-refkey probe).
    private static string LinqKeyFamilySuffix(byte b) => b switch
    {
        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i4",
        0x0A or 0x0B => "i8",
        0x0C => "f",
        0x0D => "d",
        0x0E or 0x12 or 0x13 or 0x15 or 0x1C or 0x1D or 0x1E => "ref",
        _ => "",
    };

    // impl-linq3 (B): the name-aware key-family suffix. A 0x11 VALUETYPE key
    // byte is every struct, so the NAME decides: System.DateTime -> "dt" (the
    // ticks carrier sort), System.Guid -> "guid" (the net48 field-wise
    // contract); every other value type stays "" (LOUD).
    private static string LinqKeyFamilySuffixNamed(byte b, string[]? genArgNames, int index) => b switch
    {
        0x11 => LinqStructKeyFamilyName(genArgNames, index) ?? "",
        _ => LinqKeyFamilySuffix(b),
    };

    // p0linq4 (D4): the runtime entry suffix of the fused composite sort for a
    // (TSource, TKey) byte pair, or null when no kernel exists:
    //   int32  source + int32  key -> "i4"
    //   int32  source + int64  key -> "i4_i8"
    //   int32  source + double key -> "i4_d"
    //   int64  source + int32  key -> "i8_i4"
    //   int64  source + int64  key -> "i8_i8"
    //   int64  source + double key -> "i8_d"
    private static string? ThenByKernelSuffix(byte elemByte, byte keyByte)
    {
        bool keyI4 = keyByte is 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09;
        bool keyI8 = keyByte is 0x0A or 0x0B;
        bool keyD = keyByte is 0x0C or 0x0D;
        if (elemByte == 0x08) return keyI4 ? "i4" : keyI8 ? "i4_i8" : keyD ? "i4_d" : null;
        if (elemByte is 0x0A or 0x0B) return keyI4 ? "i8_i4" : keyI8 ? "i8_i8" : keyD ? "i8_d" : null;
        return null;
    }

    //--------------------------------------------------------------------------
    // LinqOrderByKeyEntry -- the int32-source OrderBy / OrderByDescending entry
    // for a WIDE key family (int64 / double). The int32-key entries read the
    // key from the int32 slot; a long/double key crosses in the int64 / F-stack
    // slot, so it needs its own runtime entry (linq_sort.h). The key families
    // are exactly the ones the runtime ships; anything else (a reference key --
    // string is culture-aware -- or a user struct key) stays LOUD.
    //--------------------------------------------------------------------------
    private static ResolvedMethod LinqOrderByKeyEntry(byte[]? genArgs, string[]? genArgNames, bool desc)
    {
        string member = desc ? "OrderByDescending" : "OrderBy";
        if (genArgs is not { Length: >= 2 })
        {
            string elemArgType = genArgs is { Length: >= 1 } ? GenericArgTypeName(genArgs[0]) : "?";
            throw new NotSupportedException(
                $"Enumerable.{member}<{elemArgType},?> not wired: the runtime int32-source " +
                "key-selector surface covers int32/int64/double/float keys only " +
                "(il2cpp_linq_order_by_key[_desc]_i4 / _i4_i8 / _i4_f / _i4_d)");
        }
        // impl-linq3 (B): a DateTime / Guid key rides the struct-carrier sort
        // (the ticks comparison / the net48 Guid field-wise contract); other
        // 0x11 value types (user structs) stay LOUD.
        if (genArgs[1] == 0x11)
        {
            string? structKey = LinqStructKeyFamilyName(genArgNames, 1);
            if (structKey == "dt")
                return I(desc ? "il2cpp::il2cpp_linq_order_by_key_desc_i4_dt"
                              : "il2cpp::il2cpp_linq_order_by_key_i4_dt",
                    2, true, true, false, new[] { "a", "" }, returnTypeName: "System.Int32[]");
            if (structKey == "guid")
                return I(desc ? "il2cpp::il2cpp_linq_order_by_key_desc_i4_guid"
                              : "il2cpp::il2cpp_linq_order_by_key_i4_guid",
                    2, true, true, false, new[] { "a", "" }, returnTypeName: "System.Int32[]");
        }
        string entry = genArgs[1] switch
        {
            0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09
                => desc ? "il2cpp::il2cpp_linq_order_by_key_desc_i4"
                         : "il2cpp::il2cpp_linq_order_by_key_i4",
            0x0A or 0x0B
                => desc ? "il2cpp::il2cpp_linq_order_by_key_desc_i4_i8"
                         : "il2cpp::il2cpp_linq_order_by_key_i4_i8",
            // impl-floatkey: float gets its OWN entry -- a Func<int,float>
            // leaf returns a 4-byte float per the MS x64 ABI, so the entry
            // must read the key through a float(*) reader (the shared _i4_d
            // entry read it as a double and mixed the callee's XMM0 residue
            // into every key -- b5-gaps §14, thenby-floatprobe).
            0x0C
                => desc ? "il2cpp::il2cpp_linq_order_by_key_desc_i4_f"
                         : "il2cpp::il2cpp_linq_order_by_key_i4_f",
            0x0D
                => desc ? "il2cpp::il2cpp_linq_order_by_key_desc_i4_d"
                         : "il2cpp::il2cpp_linq_order_by_key_i4_d",
            _ => "",
        };
        if (entry.Length == 0)
        {
            throw new NotSupportedException(
                $"Enumerable.{member}<{GenericArgTypeName(genArgs[0])},{GenericArgTypeName(genArgs[1])}> " +
                "not wired: the runtime int32-source key-selector surface covers int32/int64/" +
                "double/float keys only (il2cpp_linq_order_by_key[_desc]_i4 / _i4_i8 / _i4_f / _i4_d)");
        }
        return I(entry, 2, true, true, false, new[] { "a", "" }, returnTypeName: "System.Int32[]");
    }

    //--------------------------------------------------------------------------
    // LinqOrderByKeyEntryCmp -- the comparer-twin entry of the VALUE-source
    // OrderBy / OrderByDescending (b5-gaps section 71). Every net48
    // OrderBy<TSource,TKey>(source, keySelector, comparer) over a concrete
    // VALUE element array rides one of the runtime comparator kernels:
    //   * il2cpp_linq_order_by_key_<src>_cmp      -- VALUE TKey; the KEY cell
    //     rides trailing literals {keyCell, desc} (3 IL args: a/a/"").
    //   * il2cpp_linq_order_by_key_<src>_ref_cmp  -- REFERENCE TKey (string/
    //     object -- the selector returns a boxed/class object); keys cached in
    //     a rooted ref array, {desc} literal only.
    // The desc literal mirrors the non-comparator entries (0=ascending,
    // 1=descending); it is never a consumed IL argument.
    //--------------------------------------------------------------------------
    private static ResolvedMethod LinqOrderByKeyEntryCmp(
        MetadataReader md, BlobHandle sigHandle, byte[]? genArgs, string[]? genArgNames,
        bool desc, string srcSuffix)
    {
        if (genArgs is not { Length: >= 2 })
            throw new NotSupportedException(
                $"Enumerable.OrderBy<...,?> comparison entry: missing key generic argument.");
        string member = desc ? "OrderByDescending" : "OrderBy";
        byte keyByte = genArgs[1];
        string returnTypeName = srcSuffix switch
        {
            "i4" => "System.Int32[]",
            "i8" => "System.Int64[]",
            "d" => "System.Double[]",
            "f" => "System.Single[]",
            _ => "System.Int32[]",
        };
        if (IsRefFamilyByte(keyByte))
        {
            return I($"il2cpp::il2cpp_linq_order_by_key_{srcSuffix}_ref_cmp",
                3, true, true, false, new[] { "a", "", "" },
                returnTypeName: returnTypeName,
                appendLiterals: new[] { desc ? "1" : "0" });
        }
        string? keyCell = LinqCellCodeOfElemByte(keyByte);
        if (keyCell == null)
            throw new NotSupportedException(
                $"Enumerable.{member}<{GenericArgTypeName(genArgs[0])}," +
                $"{GenericArgTypeName(keyByte)}> not wired: the value-source comparer " +
                "surface covers int32/uint32/narrow/int64/double/float keys and reference " +
                "keys only (b5-gaps section 71: decimal/DateTime/TimeSpan/Guid/" +
                "DateTimeOffset comparers stay LOUD).");
        return I($"il2cpp::il2cpp_linq_order_by_key_{srcSuffix}_cmp",
            3, true, true, false, new[] { "a", "", "" },
            returnTypeName: returnTypeName,
            appendLiterals: new[] { keyCell, desc ? "1" : "0" });
    }

    private static string? LinqNarrowCode(byte b) => b switch
    {
        0x04 => "0", // sbyte
        0x05 => "1", // byte
        0x02 => "2", // bool
        0x06 => "3", // short
        0x07 => "4", // ushort
        0x03 => "5", // char
        0x09 => "6", // uint
        _ => null,
    };

    // The zip RESULT family code of an element type BYTE:
    //   0 int32 / 2 int64 / 3 float / 4 double / 5 uint32 (the 4/8-byte cells)
    //   6 reference (a REFERENCE array)
    //   7 sbyte / 8 byte / 12 bool (1-byte cells)
    //   9 short / 10 ushort / 11 char (2-byte cells)
    // or null when the result cell has no ABI. A ulong result rides the int64
    // family: the cell width and the write ABI are identical (documented
    // deviation in docs/audit/b5-gaps.md section 3).
    private static string? LinqZipResKindOfElemByte(byte b) => b switch
    {
        0x08 => "0",  // int32
        0x09 => "5",  // uint32
        0x0A or 0x0B => "2",  // int64 / uint64
        0x0C => "3",  // float -> the 8-byte double carrier
        0x0D => "4",  // double
        0x04 => "7",  // sbyte (1-byte cell)
        0x05 => "8",  // byte (1-byte cell)
        0x06 => "9",  // short (2-byte cell)
        0x07 => "10", // ushort (2-byte cell)
        0x03 => "11", // char (2-byte cell)
        0x02 => "12", // bool (1-byte cell)
        0x0E or 0x12 or 0x13 or 0x15 or 0x1C or 0x1D or 0x1E => "6", // reference
        _ => null,
    };

    // The zip RESULT family code for a 0x11 VALUE TYPE (the byte alone cannot
    // tell System.Decimal / System.DateTime / System.TimeSpan / System.Guid
    // apart -- they share the valuetype byte). The cell widths differ:
    //   13 System.Decimal (16-byte cell)
    //   14 System.DateTime / System.TimeSpan (8-byte cell)
    //   15 System.Guid (16-byte cell)
    // or null when the name is not one of the four (a user struct has no zip
    // result cell ABI).
    private static string? LinqZipResKindOfValueTypeName(string? name)
    {
        if (name is null) return null;
        var n = name.StartsWith("V:", StringComparison.Ordinal) ? name[2..] : name;
        return n switch
        {
            "System.Decimal" => "13",
            "System.DateTime" => "14",
            "System.Guid" => "15",
            // impl-vtsel: TimeSpan / DateTimeOffset have their own result ABI
            // (8-byte ticks / 16-byte instant+offset); sharing DateTime's code
            // would make the selector return the wrong struct type.
            "System.TimeSpan" => "16",
            "System.DateTimeOffset" => "17",
            _ => null,
        };
    }

    // The zip RESULT cell ABI code of an element type BYTE, name-resolved for the
    // 0x11 value types (see LinqZipResKindOfValueTypeName).
    private static string? LinqZipResKindOfElemByte(byte b, string[]? genArgNames, int index) =>
        b == 0x11 && genArgNames is { Length: > 0 }
            ? LinqZipResKindOfValueTypeName(index < genArgNames.Length ? genArgNames[index] : null)
            : LinqZipResKindOfElemByte(b);

    // The EXACT tracked array type name of a zip RESULT element: the 0x11 value
    // types need the generic-argument NAME (System.Decimal[] / System.DateTime[]
    // / System.TimeSpan[] / System.Guid[]).
    private static string LinqArrayTypeNameExact(byte b, string[]? genArgNames, int index)
    {
        if (b == 0x11 && genArgNames is { Length: > 0 } && index < genArgNames.Length)
        {
            var n = genArgNames[index];
            if (n is { Length: > 0 } && n != "?")
                return n.StartsWith("V:", StringComparison.Ordinal) ? n[2..] + "[]" : n + "[]";
        }
        return LinqArrayTypeNameExact(b);
    }

    // A decoded generic-argument name without the "V:" value-type prefix, for a
    // LOUD message (the raw encoding is "V:System.DateTime").
    private static string LinqStructNameOf(string? name) =>
        name is { Length: > 2 } && name.StartsWith("V:", StringComparison.Ordinal)
            ? name[2..] : name ?? "?";

    // impl-loudvalstruct (C): the projection-matrix suffix of a by-value
    // VALUE-STRUCT result (dt / ts / guid / dtoffset), or null when the 0x11
    // byte names something with no result ABI (a user struct, decimal -- the
    // caller tests decimal first).
    private static string? LinqStructResultSuffix(string[]? genArgNames, int index)
    {
        if (genArgNames is not { Length: > 0 } || index >= genArgNames.Length) return null;
        return LinqStructNameOf(genArgNames[index]) switch
        {
            "System.DateTime" => "dt",
            "System.TimeSpan" => "ts",
            "System.Guid" => "guid",
            "System.DateTimeOffset" => "dtoffset",
            _ => null,
        };
    }

    // The List<T> element-kind literal (linq_list.h IL2CPP_LIST_KIND_*) of an
    // element TYPE BYTE, or null when the byte is not a value family (a
    // reference element has no cell ABI of its own). This is the ONLY mapping
    // from a metadata element byte to IL2CPP_LIST_KIND_*: the narrow element
    // bytes are 1/2-byte cells whose family cannot be recovered from the cell
    // width, and IL2CPP_LIST_KIND_* is a DIFFERENT enumeration from the zip
    // side codes -- LinqNarrowCode's 0..6 must never be reused as a list kind
    // (byte -> "1" would be IL2CPP_LIST_KIND_REF, an 8-byte reference read of a
    // 1-byte cell).
    private static string? LinqListKindOfElemByte(byte b) => b switch
    {
        0x08 => "0",  // int32
        0x0A or 0x0B => "2",  // int64 / uint64
        0x0C => "3",  // float
        0x0D => "4",  // double
        0x04 => "5",  // sbyte
        0x05 => "6",  // byte
        0x02 => "7",  // bool
        0x06 => "8",  // short
        0x07 => "9",  // ushort
        0x03 => "10", // char
        0x09 => "11", // uint
        _ => null,
    };

    // The List<T> element-kind literal, name-resolved for the 0x11 value types.
    // System.Decimal keeps its own list cell kind (IL2CPP_LIST_KIND_DEC = 12);
    // impl-loudvalstruct (D) added the remaining by-value value structs --
    // DateTime 13, TimeSpan 14, Guid 15, DateTimeOffset 16 -- whose list
    // storage is a BOXED element the snapshot unboxes into the family's cell. A
    // USER struct has no cell ABI and stays null (the downstream LOUD gates).
    private static string? LinqListKindOfElemByte(byte b, string[]? genArgNames, int index)
    {
        if (b != 0x11 || genArgNames is not { Length: > 0 } || index >= genArgNames.Length)
            return LinqListKindOfElemByte(b);
        return LinqStructNameOf(genArgNames[index]) switch
        {
            "System.Decimal" => "12",
            "System.DateTime" => "13",
            "System.TimeSpan" => "14",
            "System.Guid" => "15",
            "System.DateTimeOffset" => "16",
            _ => null,
        };
    }

    // D-LINQ-FAM: the IL2CPP_LINQ_CELL_* code of an element TYPE byte
    // (linq_family.h), the FAMILY-GENERIC kernel trailing literal. This is a
    // DIFFERENT enumeration from LinqListKindOfElemByte (IL2CPP_LIST_KIND_*):
    // int32 is CELL 7 but LIST_KIND 0, and float is CELL 9 but LIST_KIND 3 --
    // reusing one for the other would read the wrong cell width (the
    // LinqRequireCell elemSize guard would throw at runtime).
    private static string? LinqCellCodeOfElemByte(byte b) => b switch
    {
        0x04 => "0",  // sbyte
        0x05 => "1",  // byte
        0x02 => "2",  // bool
        0x06 => "3",  // short
        0x07 => "4",  // ushort
        0x03 => "5",  // char
        0x09 => "6",  // uint
        0x08 => "7",  // int32
        0x0A or 0x0B => "8",  // int64 / uint64
        0x0C => "9",  // float
        0x0D => "10", // double
        _ => null,
    };

    // impl-listvtops: the Aggregate DEFERRAL for a hint-less pass over a
    // 0x11 (non-decimal) source. An Aggregate's source sits BELOW the seed /
    // lambda on the IL stack, so the pre-pop pass sees the lambda in arg0Type
    // and cannot tell a List<DateTime/TimeSpan/Guid/DateTimeOffset> from a
    // value-struct ARRAY. Without this the ARRAY cell matrix throws (a 0x11
    // element has no ARRAY cell code) and the whole method fold is aborted
    // before StackFold's post-pop re-resolve can rebind the List domain.
    // Answering the int32-element base entry (the shape a List<int> aggregate
    // binds in this pass) keeps the fold alive; the re-resolve replaces it
    // with the list `val` entry (or, for a proven array, the LOUD stands).
    private static ResolvedMethod? LinqVtAggDeferral(string acc, byte[]? genArgs,
        string[]? genArgNames, string? receiverType, string? arg0Type,
        string resSuffix = "", bool resRef = false)
    {
        if (genArgs is not { Length: >= 1 } || genArgs[0] != 0x11) return null;
        if (IsDecimalGenArg(genArgNames, 0)) return null;
        if (IsDecimalSourceNamed(genArgs, genArgNames, receiverType, arg0Type)) return null;
        if ((receiverType ?? arg0Type) is { } proven
            && proven.EndsWith("[]", StringComparison.Ordinal)) return null;
        string seedCast = acc switch
        {
            "i4" => "i",
            "i8" => "l",
            "f" => "d",
            "d" => "d",
            _ => "",
        };
        string baseName = acc switch
        {
            "i4" => "il2cpp::il2cpp_linq_aggregate_seed",
            "i8" => "il2cpp::il2cpp_linq_aggregate_seed_i8",
            "f" => "il2cpp::il2cpp_linq_aggregate_seed_f",
            "d" => "il2cpp::il2cpp_linq_aggregate_seed_d",
            _ => "il2cpp::il2cpp_linq_aggregate_seed_ref",
        };
        bool rs = resSuffix.Length != 0;
        return I(baseName + resSuffix, rs ? 4 : 3, true, acc == "ref" || resRef, false,
            rs ? new[] { "a", seedCast, "", "" } : new[] { "a", seedCast, "" },
            resultKind: acc == "ref" ? null
                : acc == "i8" ? "l"
                : acc is "f" or "d" ? "d" : null);
    }

    // impl-arrayagg: the IL2CPP_LINQ_CELL_* literal of an ARRAY source's
    // element family -- the trailing literal of the cell-matrix seed entries.
    // Null when the source is a value struct (the dedicated dt/ts/guid entries
    // serve it) or an unknown shape.
    private static string? LinqArrayCellCode(byte[]? genArgs, string[]? genArgNames,
        string? receiverType, string? arg0Type)
    {
        if (genArgs is { Length: >= 1 })
        {
            var b = genArgs[0];
            if (b == 0x11) return IsDecimalGenArg(genArgNames, 0) ? "11" : null;
            if (IsRefFamilyByte(b)) return "15";
            return LinqCellCodeOfElemByte(b);
        }
        var name = receiverType ?? arg0Type;
        if (name == null) return null;
        if (name.EndsWith("System.Decimal[]", StringComparison.Ordinal)) return "11";
        if (name.EndsWith("System.Int32[]", StringComparison.Ordinal)) return "7";
        if (name.EndsWith("System.Int64[]", StringComparison.Ordinal)
            || name.EndsWith("System.UInt64[]", StringComparison.Ordinal)) return "8";
        if (name.EndsWith("System.Single[]", StringComparison.Ordinal)) return "9";
        if (name.EndsWith("System.Double[]", StringComparison.Ordinal)) return "10";
        if (name.EndsWith("System.SByte[]", StringComparison.Ordinal)) return "0";
        if (name.EndsWith("System.Byte[]", StringComparison.Ordinal)) return "1";
        if (name.EndsWith("System.Boolean[]", StringComparison.Ordinal)) return "2";
        if (name.EndsWith("System.Int16[]", StringComparison.Ordinal)) return "3";
        if (name.EndsWith("System.UInt16[]", StringComparison.Ordinal)) return "4";
        if (name.EndsWith("System.Char[]", StringComparison.Ordinal)) return "5";
        if (name.EndsWith("System.UInt32[]", StringComparison.Ordinal)) return "6";
        if (name.EndsWith("[]", StringComparison.Ordinal)) return "15";
        return null;
    }

    // impl-loudvalstruct (D): the List element-kind literal of a tracked ELEMENT
    // TYPE NAME (the angle form of a tracked generic receiver, or a bare name).
    // Only the value families whose list storage has a real cell ABI answer a
    // code; everything else is the reference tag (the same default the
    // underscore-suffix table uses for a nested generic element).
    private static string LinqListKindOfElemName(string name)
    {
        var n = LinqStructNameOf(name.Trim());
        return n switch
        {
            "System.Decimal" => "12",
            "System.DateTime" => "13",
            "System.TimeSpan" => "14",
            "System.Guid" => "15",
            "System.DateTimeOffset" => "16",
            "System.Int32" or "System.Int16" or "System.UInt16" or "System.Char"
                or "System.SByte" or "System.Byte" or "System.Boolean" or "System.UInt32" => "0",
            "System.Int64" or "System.UInt64" => "2",
            "System.Single" => "3",
            "System.Double" => "4",
            _ => "1",
        };
    }

    // The List<T> element-kind literal (linq_list.h IL2CPP_LIST_KIND_*), or
    // null when the receiver is not a List<T>. The tracked List receiver names
    // come from the newobj AOT expansion (List`1_int / List`1_string / ...) or
    // from the ToList ReturnTypeName ("System.Collections.Generic.List`1").
    private static string? LinqListKind(string? receiverType, byte[]? genArgs, string srcFam,
        string[]? genArgNames = null)
    {
        if (receiverType == null) return null;

        if (!receiverType.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal))
            return null;
        var suffix = receiverType.Length > "System.Collections.Generic.List`1".Length
            ? receiverType["System.Collections.Generic.List`1".Length..]
            : "";
        // ELEMENT AUTHORITY: the MethodSpec generic argument decides the
        // element family. The tracked receiver NAME is only a heuristic --
        // MEASURED: a `List<double>` receiver inside a method that also builds a
        // List<int> was tracked as "System.Collections.Generic.List`1_int", and
        // the zip then read the double cells as int32 (K=0). genArgs[0] is
        // TSource/TSource-of-the-extension, i.e. the element type the runtime
        // must read. The name suffix is used only when the call has no
        // MethodSpec at all (and for the reference-element names below).
        if (genArgs is { Length: >= 1 })
        {
            // impl-arrayagg (defect 2): the 0x11 value-type byte needs the
            // NAME-aware overload. The old call used the byte-only overload,
            // which answers null for 0x11, so a `List<decimal>` fell through
            // to the reference tag ("1") -- the list kernels then read 8-byte
            // reference slots out of 16-byte decimal cells (a silent wrong
            // value). Only System.Decimal has a list cell kind (12); the other
            // value types have no list storage kind and stay on the reference
            // tag for the downstream LOUD gates.
            if (LinqListKindOfElemByte(genArgs[0], genArgNames, 0) is { } eleKind) return eleKind;
            // The non-generic aggregate surface (Sum/Min/Max/Average) carries
            // a synthesized single element byte with NO name table, so the
            // tracked receiver name is the only decimal evidence.
            //
            // impl-loudvalstruct (D): the name arrives in EITHER form -- the AOT
            // underscore form ("List`1_System.Decimal") or the tracked generic
            // form ("List`1<V:System.Decimal>"). Both must be resolved BEFORE
            // the srcFam fallback below: the 0x11 byte folds into "ref" in
            // LinqSourceFamily, so an unnamed value-type element returned the
            // REFERENCE tag and the source-only aggregate emitted the int32
            // kernel into a decimal slot (MEASURED: audit-fix3-d2k,
            // "no match for 'operator=' (Il2CppDecimal = int32_t)").
            if (genArgs[0] == 0x11)
            {
                if (suffix is "_decimal" or "_System.Decimal") return "12";
                if (suffix.StartsWith("<", StringComparison.Ordinal)
                    && suffix.EndsWith(">", StringComparison.Ordinal))
                {
                    var angKind = LinqListKindOfElemName(suffix[1..^1]);
                    if (angKind != "1") return angKind;
                }
            }
            if (srcFam == "ref") return "1";
        }
        return suffix switch
        {
            "_int" => "0",
            "_long" => "2",
            "_float" => "3",
            "_double" => "4",
            "_sbyte" => "5",
            "_byte" => "6",
            "_bool" => "7",
            "_short" => "8",
            "_ushort" => "9",
            "_char" => "10",
            "_uint" => "11",
            // impl-arrayagg (defect 2): the boxed decimal List storage kind.
            // The AOT expansion suffix is "_System.Decimal"; a collapsed /
            // synthetic name may carry the short "_decimal".
            "_decimal" or "_System.Decimal" => "12",
            "_string" or "_class" or "_object" or "_array" => "1",
            // impl-loudvalstruct (D): a METHOD PARAMETER / FIELD of a generic
            // List is tracked in the ANGLE form ("List`1<V:System.Decimal>"),
            // not the AOT underscore form. Without this arm the suffix was
            // "<V:System.Decimal>" -- length > 1 -- and fell into the reference
            // tag, so a `void Run(List<decimal> l) { l.Sum(); }` emitted the
            // int32 source-only kernel and assigned an int32 into a decimal
            // slot (MEASURED: audit-fix3-d2k, "no match for 'operator='
            // (Il2CppDecimal = int32_t)").
            _ when suffix.StartsWith("<", StringComparison.Ordinal)
                && suffix.EndsWith(">", StringComparison.Ordinal)
                => LinqListKindOfElemName(suffix[1..^1]),
            _ when suffix.Length > 1 => "1", // any nested generic element is a reference
            // The unsuffixed pseudo-type (ToList's tracked result): the family
            // comes from the call's own generic argument. The LIST kind must be
            // read from the element BYTE (LinqListKindOfElemByte) -- the narrow
            // families cannot be folded into the srcFam text alone: "narrow"
            // covers eight different LIST kinds, and the old LinqNarrowCode
            // fallback below returned the NARROW code space as a LIST kind
            // (byte -> "1" = IL2CPP_LIST_KIND_REF: an 8-byte reference read of a
            // 1-byte cell).
            _ when genArgs is { Length: >= 1 } => srcFam == "ref"
                ? "1"
                : LinqListKindOfElemByte(genArgs[0], genArgNames, 0) ?? "1",
            _ => srcFam switch
            {
                "ref" => "1",
                "i8" => "2",
                "f" => "3",
                "d" => "4",
                _ => "0",
            },
        };
    }

    // The tracked array type name for a source/result family byte (narrow
    // families keep their real element type -- not System.Int32[]).
    private static string LinqArrayTypeNameExact(byte b) => b switch
    {
        0x08 => "System.Int32[]",
        0x09 => "System.UInt32[]",
        0x02 => "System.Boolean[]",
        0x03 => "System.Char[]",
        0x04 => "System.SByte[]",
        0x05 => "System.Byte[]",
        0x06 => "System.Int16[]",
        0x07 => "System.UInt16[]",
        0x0A => "System.Int64[]",
        0x0B => "System.UInt64[]",
        0x0C => "System.Single[]",
        0x0D => "System.Double[]",
        0x0E => "System.String[]",
        _ => "System.Object[]",
    };

    // The VALUE-result ABI suffix of the reference-source Select kernels for a
    // non-reference TResult (int32-slot primitives -> _i4, long/ulong -> _i8,
    // float -> _f, double -> _d), or null when the TResult is a reference /
    // value type with no kernel.
    private static string? LinqValueResultSuffix(byte b) => b switch
    {
        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i4",
        0x0A or 0x0B => "i8",
        0x0C => "f",
        0x0D => "d",
        _ => null,
    };

    // The tracked array type of a reference-source VALUE-result Select: the
    // int32-slot primitives and long ride their own array; a float result rides
    // the F-stack carrier (an 8-byte double cell stamped elemKind F).
    private static string LinqValueResultArrayName(string suffix) => suffix switch
    {
        "i4" => "System.Int32[]",
        "i8" => "System.Int64[]",
        "dt" => "System.DateTime[]",
        "ts" => "System.TimeSpan[]",
        "guid" => "System.Guid[]",
        "dtoffset" => "System.DateTimeOffset[]",
        _ => "System.Double[]",
    };

    // The 0x11 VALUETYPE byte covers decimal/DateTime/TimeSpan/Guid (and every
    // user struct), so the decoded MethodSpec FULL NAME is the only authority.
    // The dt/ts/guid results ride their own by-value selector kernels
    // (il2cpp_linq_select_[idx_]ref_dt/_ts/_guid); anything else stays null.
    private static string? LinqValueResultSuffixWide(byte b, string[]? genArgNames, int idx)
    {
        if (b != 0x11) return LinqValueResultSuffix(b);
        var n = genArgNames is { Length: > 0 } && idx < genArgNames.Length ? genArgNames[idx] : null;
        return n switch
        {
            "System.DateTime" => "dt",
            "System.TimeSpan" => "ts",
            "System.Guid" => "guid",
            "System.DateTimeOffset" => "dtoffset",
            _ => null,
        };
    }

    // The RESULT ABI suffix of the element-operator entries for a List kind.
    private static string LinqListAbiSuffix(string listKind) => listKind switch
    {
        "1" => "ref",
        "2" => "i8",
        "3" or "4" => "d",
        _ => "i4",
    };

    // The RESULT ABI suffix of the element-operator entries for an ARRAY
    // source family ("narrow" rides the int32 slot).
    private static string LinqArrayAbiSuffix(string srcFam) => srcFam switch
    {
        "ref" => "ref",
        "i8" => "i8",
        "f" or "d" => "d",
        _ => "i4",
    };

    //--------------------------------------------------------------------------
    // TryMapLinqList -- every operator over a List<T> receiver. The list object
    // is passed through UNCAST (cast "") -- never as Il2CppArray* -- and the
    // element-kind literal is appended after the IL arguments.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapLinqList(MetadataReader md, BlobHandle sigHandle, string name,
        int count, byte[]? genArgs, string listKind, string srcFam, string? srcType,
        string[] TwoArg, string[] OneArg, string[]? genArgNames = null)
    {
        string K = listKind;
        string abi = LinqListAbiSuffix(K);
        // A List<T> receiver is a LIST OBJECT: the slot is passed through
        // UNCAST ("" -- never the "i" Il2CppArray* cast). Casting it would
        // reinterpret the items pointer's low 32 bits as an array length.
        var L1 = new[] { "" };
        var L2 = new[] { "", "" };
        var L3 = new[] { "", "", "" };
        // impl-loudvalstruct (D): a 0x11 element is EVERY value type, so the
        // tracked result-array name needs the generic-argument NAME table (a
        // DateTime list's snapshot is a System.DateTime[], not an object[]).
        var elemTracked = genArgs is { Length: >= 1 }
            ? LinqArrayTypeNameExact(genArgs[0], genArgNames, 0)
            : (srcFam == "ref" ? "System.Object[]" : "System.Int32[]");
        var elemScalar = genArgs is { Length: >= 1 }
            ? LinqArrayTypeNameExact(genArgs[0], genArgNames, 0).TrimEnd('[', ']')
            : (srcFam == "ref" ? "System.Object" : "System.Int32");

        // b5-gaps section 56: a user-comparer call over a List<T> that reached
        // here (no List arm consumes a comparer yet) must go LOUD rather than
        // silently drop the comparer on the plain list kernel.
        if (name != "GroupBy" && LinqHasComparerParam(md, sigHandle))
        {
            throw new NotSupportedException(
                $"Enumerable.{name} over this List<T> with a user comparer is not wired yet: " +
                "the plain list kernel would silently drop the comparer (b5-gaps section 56).");
        }

        // impl-listvtops: the by-value VALUE-STRUCT list element kinds
        // (13..16). Before this seat EVERY operator except Count/Any/
        // LongCount/ToArray/DefaultIfEmpty was a transpile-time LOUD, because
        // LinqListAbiSuffix answers "i4" for an unknown kind and an unguarded
        // arm would read a 4-byte slice out of an 8/16-byte cell (a silent
        // wrong value) and call the lambda through a foreign ABI.
        //
        // The runtime now carries the by-value surface:
        //   * kind-generic list kernels (element copies + PredicateOn /
        //     PredicateOnIdx) serve Take/Skip/Reverse/Concat/Distinct/Except/
        //     Union/Intersect/SequenceEqual/Where/All/Count(pred)/Any(pred)/
        //     TakeWhile/SkipWhile(+idx)/DefaultIfEmpty/ToArray/ToList;
        //   * per-family entries (il2cpp_linq_list_<op>_dt/_ts/_guid/
        //     _dtoffset) serve First/Last/Single/ElementAt/Contains and their
        //     predicate forms, plus Aggregate (element family `val`).
        // A USER struct never reaches this block (LinqListKind answers null, so
        // the kind is the reference tag "1"), so only the operators with NO
        // by-value delegate ABI at all (Select / OrderBy / ThenBy / GroupBy /
        // Zip and the numeric aggregates) stay LOUD -- with a message that
        // names the real reason.
        bool vtElem = K is "13" or "14" or "15" or "16";
        string vtFam = K switch
        {
            "13" => "dt",
            "14" => "ts",
            "15" => "guid",
            _ => "dtoffset",
        };
        // The BY-VALUE result kind (the CodeGenerator carrier): DateTime and
        // TimeSpan use their own name, Guid rides "guidv" (the by-value return;
        // the plain "guid" kind is the out-buffer icall ABI) and
        // DateTimeOffset rides "dto".
        string vtResult = vtFam switch
        {
            "guid" => "guidv",
            "dtoffset" => "dto",
            _ => vtFam,
        };
        if (vtElem)
        {
            // impl-vtsel: OrderBy / OrderByDescending / ThenBy / ThenByDescending
            // / GroupBy / Zip over a by-value STRUCT element list are WIRED. The
            // key-selector ABI takes the element in its own struct slot and the
            // key in its own family slot (il2cpp_linq_list_order_by_key_vt /
            // _then_by_key_vt), so no foreign ABI is used.
            if (name is "OrderBy" or "OrderByDescending")
            {
                if (count < 2) return null;
                if (LinqSecondParamGenericArgCount(md, sigHandle) == 3)
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a List<{LinqStructNameOf(genArgNames?[0])}> with an " +
                        "(element, index) key selector is not wired: net48 Enumerable.OrderBy has no " +
                        "(element, index) key-selector overload (MS Learn Enumerable.OrderBy " +
                        "view=netframework-4.8.1)");
                string vtKeyFam = LinqVtKeyFamilyCode(genArgs, genArgNames, 1)
                    ?? throw new NotSupportedException(
                        $"Enumerable.{name} over a List<{LinqStructNameOf(genArgNames?[0])}> with the key " +
                        $"family '{GenericArgTypeName(genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08)}' " +
                        "is not wired: the struct-source key-selector surface covers int32/int64/double/" +
                        "float/System.DateTime/System.Guid/System.TimeSpan/System.DateTimeOffset keys");
                return I("il2cpp::il2cpp_linq_list_order_by_key_vt", 2, true, true, false, L2,
                    returnTypeName: elemTracked,
                    appendLiterals: new[]
                    {
                        name == "OrderByDescending" ? "1" : "0",
                        K, vtKeyFam,
                    });
            }
            if (name is "ThenBy" or "ThenByDescending")
            {
                if (count < 2) return null;
                string vtK2 = LinqVtKeyFamilyCode(genArgs, genArgNames, 1)
                    ?? throw new NotSupportedException(
                        $"Enumerable.{name} over a List<{LinqStructNameOf(genArgNames?[0])}> with the secondary " +
                        $"key family '{GenericArgTypeName(genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08)}' " +
                        "is not wired: the fused struct-source composite covers int32/int64/double/float/" +
                        "System.DateTime/System.Guid/System.TimeSpan/System.DateTimeOffset keys");
                return I(name == "ThenBy"
                        ? ResolvedMethod.ThenByCombineMarker
                        : ResolvedMethod.ThenByDescCombineMarker,
                    2, false, false, false, new[] { "", "" },
                    returnTypeName: elemTracked,
                    appendLiterals: new[] { vtK2 });
            }
            if (name == "GroupBy")
            {
                int vtKeyKind = KeyKindForNamed(
                    genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08, genArgNames, 1);
                if (vtKeyKind < 0)
                    throw new NotSupportedException(
                        $"Enumerable.GroupBy<{LinqStructNameOf(genArgNames?[0])}," +
                        $"{(genArgNames is { Length: >= 2 } ? genArgNames[1] : GenericArgTypeName(genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08))}> " +
                        "over a List is not wired: the grouping key carrier has no rule for this TKey " +
                        "(the wired key families are the int32 slot, int64, System.Decimal, System.DateTime, " +
                        "System.Guid, System.TimeSpan, System.DateTimeOffset, a reference and a user enum)");
                var vtParams = LinqEncodedParamTypes(md, sigHandle);
                int vtElemArg = -1, vtCmpArg = -1, vtResArg = -1;
                for (int pi = 2; pi < vtParams.Length; pi++)
                {
                    var pdef = GenericDefNameOf(vtParams[pi]);
                    if (pdef == "System.Func`2") vtElemArg = pi;
                    else if (pdef == "System.Func`3") vtResArg = pi;
                    else if (pdef == "System.Collections.Generic.IEqualityComparer`1") vtCmpArg = pi;
                }
                byte? vtElemByte = vtElemArg >= 0 && genArgs is { Length: >= 3 } ? genArgs[2] : null;
                byte? vtResByte = vtResArg >= 0 && genArgs is { Length: >= 3 }
                    ? (genArgs.Length >= 4 ? genArgs[3] : genArgs[2]) : null;
                string vtEk = vtElemByte is { } vb ? LinqGroupElemFamilyCode(vb, genArgNames, 2) : K switch
                {
                    "13" => "5",   // DateTime bucket
                    "14" => "8",   // TimeSpan bucket
                    "15" => "6",   // Guid bucket
                    "16" => "9",   // DateTimeOffset bucket
                    _ => "0",
                };
                if (vtEk.Length == 0)
                    throw new NotSupportedException(
                        $"Enumerable.GroupBy TElement '{GenericArgTypeName(vtElemByte!.Value)}' over a " +
                        $"List<{LinqStructNameOf(genArgNames?[0])}> is not a supported bucket element family");
                string vtRk = vtResByte is { } vrb ? LinqGroupResultFamilyCode(vrb) : "0";
                // The runtime signature is (elemKind, rsKind, kind, keyKind,
                // srcKind): `kind` is the LIST snapshot kind (IL2CPP_LIST_KIND_*,
                // 13..16) and `srcKind` is the SOURCE element family code
                // (il2cpp_linq_group_by_full_key_cell: 3 DateTime | 4 Guid |
                // 9 TimeSpan | 10 DateTimeOffset). Feeding the list kind into the
                // srcKind slot made SnapshotOf() snapshot through kind 9
                // (IL2CPP_LIST_KIND_USHORT) -> list_obj_to_array_ushort on a
                // DateTime list.
                string vtGsrc = K switch
                {
                    "13" => "3",
                    "14" => "9",
                    "15" => "4",
                    _ => "10",
                };
                return I("il2cpp::il2cpp_linq_list_group_by_full_kind", count, true, true, false,
                    new[] { "", "", "", "", "" },
                    returnTypeName: LinqGroupResultTypeName(vtRk),
                    appendLiterals: new[]
                    {
                        vtEk, vtRk,
                        K,
                        vtKeyKind.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        vtGsrc,
                    },
                    argOrder: new[] { 0, 1, vtElemArg, vtCmpArg, vtResArg });
            }
            if (name is "Sum" or "Min" or "Max" or "Average")
            {
                // b5-gaps §53: the aggregates over a List of a BY-VALUE
                // value-struct element. The selector-less Min/Max exist in
                // net48 (Comparer<TSource>.Default); the SELECTOR forms take
                // the element BY VALUE through the family ABI. Every entry
                // snapshots the list ONCE at the kind below, so no kind
                // literal is passed. Sum/Average WITHOUT a selector do not
                // exist for these elements in net48.
                string listFam = K switch
                {
                    "13" => "dt",
                    "14" => "ts",
                    "15" => "guid",
                    _ => "dtoffset",
                };
                string listSrc = K switch
                {
                    "13" => "dt",
                    "14" => "ts",
                    "15" => "guid",
                    _ => "dto",
                };
                string listResult = K switch
                {
                    "13" => "dt",
                    "14" => "ts",
                    "15" => "guidv",
                    _ => "dto",
                };
                if (count < 2)
                {
                    if (name == "Min")
                        return I($"il2cpp::il2cpp_linq_list_min_{listFam}", 1, true,
                            false, false, L1, resultKind: listResult);
                    if (name == "Max")
                        return I($"il2cpp::il2cpp_linq_list_max_{listFam}", 1, true,
                            false, false, L1, resultKind: listResult);
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a List<{LinqStructNameOf(genArgNames?[0])}> " +
                        "WITHOUT a selector is not a net48 overload");
                }
                string? listProj = LinqSelectorProjectionType(md, sigHandle) switch
                {
                    "i" or "c" => "i4",
                    "l" => "i8",
                    "d" => "d",
                    "f" => "f",
                    "V:System.Decimal" or "System.Decimal" => "dec",
                    _ => null,
                };
                if (listProj is null)
                {
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a List<{LinqStructNameOf(genArgNames?[0])}> " +
                        "with a selector is not wired: the value-struct list source surface covers " +
                        "the System.DateTime/System.TimeSpan/System.Guid/" +
                        "System.DateTimeOffset elements with an int32/int64/double/float/" +
                        "decimal projection only (see docs/audit/b5-gaps.md)");
                }
                string listOp = name switch
                {
                    "Sum" => "sum",
                    "Min" => "min",
                    "Max" => "max",
                    _ => "average",
                };
                string? listRk = listProj == "dec" ? "dec"
                    : name == "Average" ? "d"
                    : listProj == "i8" ? "l"
                    : listProj is "f" or "d" ? "d" : null;
                return I($"il2cpp::il2cpp_linq_list_{listOp}_sel_{listSrc}_{listProj}",
                    2, true, false, false, L2, resultKind: listRk);
            }
            bool unsupported = name switch
            {
                // The predicate forms of Single have no by-value kernel.
                "Single" or "SingleOrDefault" => count >= 2,
                _ => false,
            };
            if (unsupported)
            {
                throw new NotSupportedException(
                    $"Enumerable.{name} over a List<{LinqStructNameOf(genArgs is { Length: >= 1 } ? genArgNames?[0] : null)}> " +
                    "with a predicate is not wired: the runtime ships no by-value value-struct " +
                    "predicate kernel for this operator (the wired predicate forms are First/Last/" +
                    "FirstOrDefault/LastOrDefault/Single-free Count/Any/All/Where) " +
                    " -- see docs/audit/b5-gaps.md");
            }
        }

        // impl-listvtops: the per-family BY-VALUE entries. The result kind is
        // the family's own carrier and the tracked result type is the exact
        // element name, so a downstream operator keeps resolving.
        if (vtElem)
        {
            string vtOp;
            switch (name)
            {
                case "First" when count < 2: vtOp = "first"; break;
                case "FirstOrDefault" when count < 2: vtOp = "first_or_default"; break;
                case "Last" when count < 2: vtOp = "last"; break;
                case "LastOrDefault" when count < 2: vtOp = "last_or_default"; break;
                case "Single" when count < 2: vtOp = "single"; break;
                case "SingleOrDefault" when count < 2: vtOp = "single_or_default"; break;
                case "First" when count >= 2: vtOp = "first_pred"; break;
                case "FirstOrDefault" when count >= 2: vtOp = "first_or_default_pred"; break;
                case "Last" when count >= 2: vtOp = "last_pred"; break;
                case "LastOrDefault" when count >= 2: vtOp = "last_or_default_pred"; break;
                default: vtOp = ""; break;
            }
            if (vtOp.Length != 0)
            {
                bool predForm = count >= 2;
                return I($"il2cpp::il2cpp_linq_list_{vtOp}_{vtFam}", predForm ? 2 : 1,
                    true, false, false, predForm ? L2 : L1,
                    resultKind: vtResult,
                    returnTypeName: elemScalar,
                    appendLiterals: new[] { K });
            }
            if (name == "ElementAt")
                return I($"il2cpp::il2cpp_linq_list_element_at_{vtFam}", 2, true, false, false,
                    new[] { "", "i" }, resultKind: vtResult,
                    returnTypeName: elemScalar, appendLiterals: new[] { K });
            if (name == "ElementAtOrDefault")
                // net48: an out-of-range index answers default(T) instead of
                // throwing (the only difference from ElementAt).
                return I($"il2cpp::il2cpp_linq_list_element_at_or_default_{vtFam}", 2, true,
                    false, false, new[] { "", "i" }, resultKind: vtResult,
                    returnTypeName: elemScalar, appendLiterals: new[] { K });
            if (name == "Contains")
                // The value crosses in the family's own by-value parameter slot.
                return I($"il2cpp::il2cpp_linq_list_contains_{vtFam}", 2, true, false, false,
                    new[] { "", "" }, appendLiterals: new[] { K });
            if (name is "TakeWhile" or "SkipWhile"
                && LinqSecondParamGenericArgCount(md, sigHandle) == 3)
            {
                // The INDEX overload: the predicate receives the element AND its
                // zero-based source index (the 1-arg list entries would call a
                // Func<TSource,int,bool> through the wrong ABI).
                string idxOp = name == "TakeWhile" ? "take_while_idx" : "skip_while_idx";
                return I($"il2cpp::il2cpp_linq_list_{idxOp}", 2, true, true, false,
                    L2, returnTypeName: elemTracked, appendLiterals: new[] { K });
            }
            if (name == "Aggregate")
            {
                if (count == 2)
                {
                    // Aggregate(func) over a List<T>: TAccumulate is TSource, so
                    // the accumulator is the element struct itself.
                    return I($"il2cpp::il2cpp_linq_list_aggregate_{vtFam}", 2, true, false, false,
                        L2, resultKind: vtResult, returnTypeName: elemScalar);
                }
                if (count >= 3)
                {
                    byte accByte = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                    bool accDec = accByte == 0x11 && genArgNames is { Length: >= 2 } an
                        && an[1] is "System.Decimal" or "V:System.Decimal";
                    string accFam = accDec ? "dec" : accByte switch
                    {
                        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i4",
                        0x0A or 0x0B => "i8",
                        0x0C => "f",
                        0x0D => "d",
                        _ when IsRefFamilyByte(accByte) => "ref",
                        _ => "",
                    };
                    if (accFam.Length == 0)
                        throw new NotSupportedException(
                            $"Enumerable.Aggregate TAccumulate '{GenericArgTypeName(accByte)}' is not a " +
                            "supported accumulator family over a List (the list seed matrix covers " +
                            "int32/int64/float/double/decimal/reference accumulators; a value-struct " +
                            "accumulator has no list fold entry yet -- see docs/audit/b5-gaps.md)");
                    if (count == 4)
                    {
                        byte resByte = genArgs is { Length: >= 3 } ? genArgs[2] : (byte)0x08;
                        bool resDec = resByte == 0x11 && genArgNames is { Length: >= 3 } rn
                            && rn[2] is "System.Decimal" or "V:System.Decimal";
                        string? resStruct = resByte == 0x11
                            ? LinqStructResultSuffix(genArgNames, 2) : null;
                        string resFam = resDec ? "dec" : resStruct ?? resByte switch
                        {
                            0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i4",
                            0x0A or 0x0B => "i8",
                            0x0C => "f",
                            0x0D => "d",
                            _ when IsRefFamilyByte(resByte) => "ref",
                            _ => "",
                        };
                        if (resFam.Length == 0)
                            throw new NotSupportedException(
                                "Enumerable.Aggregate TResult " +
                                $"{GenericArgTypeName(resByte)} over a List is not a supported result " +
                                "family (the runtime projects to int32/int64/float/double/decimal/" +
                                "reference and the System.DateTime/System.TimeSpan/System.Guid/" +
                                "System.DateTimeOffset value structs only) [Reference Source " +
                                "System.Core/System/Linq/Enumerable.cs:1411]");
                        string[] rsCasts = accFam switch
                        {
                            "dec" => new[] { "", "dec", "", "" },
                            "ref" => new[] { "", "", "", "" },
                            "i4" => new[] { "", "i", "", "" },
                            "i8" => new[] { "", "l", "", "" },
                            _ => new[] { "", "d", "", "" },
                        };
                        return I($"il2cpp::il2cpp_linq_list_aggregate_seed_{accFam}_val_rs_{resFam}",
                            4, true, resFam == "ref", false, rsCasts,
                            resultKind: resFam == "dec" ? "dec"
                                : resFam == "i8" ? "l"
                                : resFam is "f" or "d" ? "d"
                                : resFam is "dt" ? "dt"
                                : resFam is "ts" ? "ts"
                                : resFam is "guid" ? "guidv"
                                : resFam is "dtoffset" ? "dto" : null,
                            appendLiterals: new[] { K },
                            returnTypeName: resStruct == null ? null : LinqArrayTypeNameExact(
                                resByte, genArgNames, 2).TrimEnd('[', ']'));
                    }
                    string[] casts = accFam switch
                    {
                        "dec" => new[] { "", "dec", "" },
                        "ref" => new[] { "", "", "" },
                        "i4" => new[] { "", "i", "" },
                        "i8" => new[] { "", "l", "" },
                        _ => new[] { "", "d", "" },
                    };
                    return I($"il2cpp::il2cpp_linq_list_aggregate_seed_{accFam}_val",
                        3, true, accFam == "ref", false, casts,
                        resultKind: accFam == "dec" ? "dec"
                            : accFam == "i8" ? "l"
                            : accFam is "f" or "d" ? "d" : null,
                        appendLiterals: new[] { K });
                }
                return null;
            }
        }

        switch (name)
        {
            case "Count" when count < 2:
                return I("il2cpp::il2cpp_linq_list_count", 1, true, false, false, L1);
            case "Count":
                return I("il2cpp::il2cpp_linq_list_count_pred", 2, true, false, false,
                    L2, appendLiterals: new[] { K });
            case "Any" when count < 2:
                return I("il2cpp::il2cpp_linq_list_any", 1, true, false, false, L1);
            case "Any":
                return I("il2cpp::il2cpp_linq_list_any_pred", 2, true, false, false,
                    L2, appendLiterals: new[] { K });
            case "All":
                return I("il2cpp::il2cpp_linq_list_all_pred", 2, true, false, false,
                    L2, appendLiterals: new[] { K });
            case "Where":
            {
                // The INDEX overload Where(Func<TSource,int,bool>) carries a
                // 3-argument Func -- route it to the list index-predicate entry
                // (the element AND its zero-based source index).
                if (LinqSecondParamGenericArgCount(md, sigHandle) == 3)
                    return I("il2cpp::il2cpp_linq_list_where_idx", 2, true, true, false,
                        L2, returnTypeName: elemTracked, appendLiterals: new[] { K });
                return I("il2cpp::il2cpp_linq_list_where", 2, true, true, false,
                    L2, returnTypeName: elemTracked, appendLiterals: new[] { K });
            }
            case "Select":
            {
                // The INDEX overload Select(Func<TSource,int,TResult>) carries
                // a 3-argument Func -- route it to the list index-selector entry
                // (the element AND its zero-based source index).
                bool selIdx = LinqSecondParamGenericArgCount(md, sigHandle) == 3;
                byte tRes = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                // Value-type tuple result: the selector returns the tuple struct
                // by value; the runtime boxes it into the reference result array
                // (the shared tuple-box contract).
                if (tRes == 0x15 && genArgNames is { Length: >= 2 }
                    && ValueTupleSupport.IsValueTupleName(genArgNames[1])
                    && ValueTupleSupport.KeyKinds(genArgNames[1]) is { } tupleKinds)
                {
                    return I("il2cpp::il2cpp_linq_list_select_tuple", 2, true, true, false, L2,
                        returnTypeName: genArgNames[1] + "[]",
                        appendLiterals: new[] { K, "\"" + tupleKinds + "\"" });
                }
                // impl-vtsel: the RESULT family codes. 0..4 are the pre-existing
                // scalar/reference carriers; 5..8 are the by-value value structs
                // (DateTime / TimeSpan / Guid / DateTimeOffset), which the
                // family-generic struct selector ABI returns through their OWN
                // slot. Before this seat a 0x11 result byte fell into "1" (a
                // REFERENCE result), so a struct-returning lambda was called
                // through an Il2CppObject*(*) pointer -- a silent wrong value.
                string resKind = tRes switch
                {
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "0",
                    0x0A or 0x0B => "2",
                    0x0C => "3",
                    0x0D => "4",
                    0x11 => LinqValueResultSuffixWide(0x11, genArgNames, 1) switch
                    {
                        "dt" => "5",
                        "ts" => "6",
                        "guid" => "7",
                        "dtoffset" => "8",
                        // decimal has no entry on this surface; the reference
                        // carrier would silently mis-read it, so stay LOUD.
                        var unknown => throw new NotSupportedException(
                            $"Enumerable.Select over a List<{LinqStructNameOf(genArgNames?[0])}> with a " +
                            $"TResult '{GenericArgTypeName(tRes)}' is not wired: the value-struct result " +
                            "carrier covers System.DateTime/System.TimeSpan/System.Guid/" +
                            "System.DateTimeOffset only (a decimal result over a List source has no " +
                            "selector kernel -- see docs/audit/b5-gaps.md)"),
                    },
                    _ => "1",
                };
                bool structResult = resKind is "5" or "6" or "7" or "8";
                if (vtElem || structResult)
                {
                    // The struct-typed selector ABI: the element crosses in its
                    // own by-value slot (dt/ts/guid/dtoffset) and the result is
                    // materialized as its typed array.
                    return I(selIdx ? "il2cpp::il2cpp_linq_list_select_idx" : "il2cpp::il2cpp_linq_list_select",
                        2, true, true, false, L2,
                        returnTypeName: LinqArrayTypeNameExact(tRes, genArgNames, 1),
                        appendLiterals: new[] { K, resKind });
                }
                return I(selIdx ? "il2cpp::il2cpp_linq_list_select_idx" : "il2cpp::il2cpp_linq_list_select",
                    2, true, true, false, L2,
                    returnTypeName: LinqArrayTypeNameExact(tRes), appendLiterals: new[] { K, resKind });
            }
            case "First" when count < 2:
                return I($"il2cpp::il2cpp_linq_list_first_{abi}", 1, true, abi == "ref", false,
                    L1, resultKind: abi == "i8" ? "l" : abi == "d" ? "d" : null,
                    returnTypeName: abi == "ref" ? elemScalar : null, appendLiterals: new[] { K });
            case "FirstOrDefault" when count < 2:
                return I($"il2cpp::il2cpp_linq_list_first_or_default_{abi}", 1, true, abi == "ref", false,
                    L1, resultKind: abi == "i8" ? "l" : abi == "d" ? "d" : null,
                    returnTypeName: abi == "ref" ? elemScalar : null, appendLiterals: new[] { K });
            case "First":
                return I($"il2cpp::il2cpp_linq_list_first_pred_{abi}", 2, true, abi == "ref", false,
                    L2, resultKind: abi == "i8" ? "l" : abi == "d" ? "d" : null,
                    returnTypeName: abi == "ref" ? elemScalar : null, appendLiterals: new[] { K });
            case "FirstOrDefault":
                return I($"il2cpp::il2cpp_linq_list_first_or_default_pred_{abi}", 2, true, abi == "ref", false,
                    L2, resultKind: abi == "i8" ? "l" : abi == "d" ? "d" : null,
                    returnTypeName: abi == "ref" ? elemScalar : null, appendLiterals: new[] { K });
            case "Last" when count < 2:
                return I($"il2cpp::il2cpp_linq_list_last_{abi}", 1, true, abi == "ref", false,
                    L1, resultKind: abi == "i8" ? "l" : abi == "d" ? "d" : null,
                    returnTypeName: abi == "ref" ? elemScalar : null, appendLiterals: new[] { K });
            case "LastOrDefault" when count < 2:
                return I($"il2cpp::il2cpp_linq_list_last_or_default_{abi}", 1, true, abi == "ref", false,
                    L1, resultKind: abi == "i8" ? "l" : abi == "d" ? "d" : null,
                    returnTypeName: abi == "ref" ? elemScalar : null, appendLiterals: new[] { K });
            // Single / SingleOrDefault (source-only): exactly one element
            // (Single throws for an empty OR a multi-element source;
            // SingleOrDefault answers default(T) for an empty source and throws
            // only for more than one). The list snapshot is exact storage, so
            // the single element is index 0. The predicate overloads (count
            // >= 2) have no list kernel and stay LOUD.
            case "Single" when count < 2:
                return I($"il2cpp::il2cpp_linq_list_single_{abi}", 1, true, abi == "ref", false,
                    L1, resultKind: abi == "i8" ? "l" : abi == "d" ? "d" : null,
                    returnTypeName: abi == "ref" ? elemScalar : null, appendLiterals: new[] { K });
            case "SingleOrDefault" when count < 2:
                return I($"il2cpp::il2cpp_linq_list_single_or_default_{abi}", 1, true, abi == "ref", false,
                    L1, resultKind: abi == "i8" ? "l" : abi == "d" ? "d" : null,
                    returnTypeName: abi == "ref" ? elemScalar : null, appendLiterals: new[] { K });
            case "ToArray":
                return I("il2cpp::il2cpp_linq_list_to_array", 1, true, true, false,
                    L1, returnTypeName: elemTracked, appendLiterals: new[] { K });
            case "ToList":
                // impl-listvtops: a by-value VALUE-STRUCT element list must be
                // tracked as its OWN List instantiation -- the unsuffixed
                // "List`1" pseudo-type is the int32 domain, so a downstream
                // l.Count / l[0] on a List<DateTime> would route to the listi4
                // entries (a 4-byte read of an 8/16-byte cell).
                return I("il2cpp::il2cpp_linq_list_to_list", 1, true, true, false,
                    L1, returnTypeName: vtElem
                        ? "System.Collections.Generic.List`1_" + LinqStructNameOf(genArgNames?[0])
                        : "System.Collections.Generic.List`1", appendLiterals: new[] { K });
            case "LongCount":
                return I("il2cpp::il2cpp_linq_list_long_count", 1, true, false, false,
                    L1, resultKind: "l");
            case "ElementAt":
                return I($"il2cpp::il2cpp_linq_list_element_at_{abi}", 2, true, abi == "ref", false,
                    new[] { "", "i" }, resultKind: abi == "i8" ? "l" : abi == "d" ? "d" : null,
                    returnTypeName: abi == "ref" ? elemScalar : null, appendLiterals: new[] { K });
            case "Contains":
                return I($"il2cpp::il2cpp_linq_list_contains_{abi}", 2, true, false, false,
                    new[] { "", abi == "ref" ? "" : abi == "i8" ? "l" : abi == "d" ? "d" : "i" },
                    appendLiterals: new[] { K });
            case "Reverse":
                return I("il2cpp::il2cpp_linq_list_reverse", 1, true, true, false,
                    L1, returnTypeName: elemTracked, appendLiterals: new[] { K });
            case "DefaultIfEmpty" when count < 2:
                return I("il2cpp::il2cpp_linq_list_default_if_empty", 1, true, true, false,
                    L1, returnTypeName: elemTracked, appendLiterals: new[] { K });
            case "DefaultIfEmpty":
            {
                // DefaultIfEmpty(source, defaultValue) over a List<T>: the source
                // when non-empty, else a one-element array holding `value` at the
                // LIST element kind's REAL cell width (net48 Enumerable.cs:1010).
                // The value rides the element family's own parameter slot. A
                // List<decimal> element has no distinct LIST kind byte of its own
                // (0x11 is shared by every value type), so the generic-argument
                // NAME table decides decimal vs reference -- the same rule the
                // Aggregate arm above uses.
                //
                // impl-loudvalstruct (D): a NON-decimal value-struct element
                // (List<DateTime> / List<TimeSpan> / List<Guid> /
                // List<DateTimeOffset>) now has its own list storage kind
                // (IL2CPP_LIST_KIND_DT/TS/GUID/DTOFFSET 13..16) and a snapshot
                // that unboxes into the family's cell, so the default crosses
                // in the struct's own by-value ABI. A USER struct still has no
                // cell ABI and stays a transpile-time LOUD.
                if (genArgNames is { Length: >= 1 } lstEn && genArgs is { Length: >= 1 }
                    && genArgs[0] == 0x11
                    && lstEn[0] is not ("System.Decimal" or "V:System.Decimal"))
                {
                    string? structEntry = LinqStructResultSuffix(lstEn, 0) switch
                    {
                        "dt" => "il2cpp::il2cpp_linq_list_default_if_empty_val_dt",
                        "ts" => "il2cpp::il2cpp_linq_list_default_if_empty_val_ts",
                        "guid" => "il2cpp::il2cpp_linq_list_default_if_empty_val_guid",
                        "dtoffset" => "il2cpp::il2cpp_linq_list_default_if_empty_val_dtoffset",
                        _ => null,
                    };
                    if (structEntry == null)
                        throw new NotSupportedException(
                            $"Enumerable.DefaultIfEmpty<{LinqStructNameOf(lstEn[0])}> over a List<" +
                            $"{LinqStructNameOf(lstEn[0])}> is not wired: a USER value struct has no " +
                            "list element cell ABI (the wired value structs are System.Decimal, " +
                            "System.DateTime, System.TimeSpan, System.Guid and System.DateTimeOffset; " +
                            "the ARRAY surface has the same limit) (see docs/audit/b5-gaps.md)");
                    return I(structEntry, 2, true, true, false, new[] { "", "" },
                        returnTypeName: elemTracked, appendLiterals: new[] { K });
                }
                bool elemDec = genArgNames is { Length: >= 1 } en
                    && en[0] is "System.Decimal" or "V:System.Decimal";
                string kEff = elemDec ? "12" : K;
                string fam = elemDec ? "dec" : K switch
                {
                    "0" or "5" or "6" or "7" or "8" or "9" or "10" or "11" => "i4",
                    "1" => "ref",
                    "2" => "i8",
                    "3" or "4" => "d",
                    "12" => "dec",
                    _ => "",
                };
                if (fam.Length == 0) return null;
                string entry = fam switch
                {
                    "i8" => "il2cpp::il2cpp_linq_list_default_if_empty_val_i8",
                    "d" => "il2cpp::il2cpp_linq_list_default_if_empty_val_d",
                    "ref" => "il2cpp::il2cpp_linq_list_default_if_empty_val_ref",
                    "dec" => "il2cpp::il2cpp_linq_list_default_if_empty_val_dec",
                    _ => "il2cpp::il2cpp_linq_list_default_if_empty_val_i4",
                };
                string[] casts = fam switch
                {
                    "i8" => new[] { "", "l" },
                    "d" => new[] { "", "d" },
                    "ref" or "dec" => new[] { "", "" },
                    _ => new[] { "", "i" },
                };
                return I(entry, 2, true, true, false, casts,
                    returnTypeName: elemTracked, appendLiterals: new[] { kEff });
            }
            case "Take":
                return I("il2cpp::il2cpp_linq_list_take", 2, true, true, false,
                    new[] { "", "i" }, returnTypeName: elemTracked, appendLiterals: new[] { K });
            case "Skip":
                return I("il2cpp::il2cpp_linq_list_skip", 2, true, true, false,
                    new[] { "", "i" }, returnTypeName: elemTracked, appendLiterals: new[] { K });
            case "TakeWhile":
                return I("il2cpp::il2cpp_linq_list_take_while", 2, true, true, false,
                    L2, returnTypeName: elemTracked, appendLiterals: new[] { K });
            case "SkipWhile":
                return I("il2cpp::il2cpp_linq_list_skip_while", 2, true, true, false,
                    L2, returnTypeName: elemTracked, appendLiterals: new[] { K });
            case "Concat":
                return I("il2cpp::il2cpp_linq_list_concat", 2, true, true, false,
                    new[] { "", "" }, returnTypeName: elemTracked, appendLiterals: new[] { K });
            case "Distinct":
                return I("il2cpp::il2cpp_linq_list_distinct", 1, true, true, false,
                    L1, returnTypeName: elemTracked, appendLiterals: new[] { K });
            case "Except":
                return I("il2cpp::il2cpp_linq_list_except", 2, true, true, false,
                    new[] { "", "" }, returnTypeName: elemTracked, appendLiterals: new[] { K });
            case "Union":
                // impl-listvtops: Union over a List receiver had NO list entry
                // at all, so it fell through to the ARRAY kernels with a List
                // object cast (the D-2 defect class). The list kernel is
                // kind-generic (ElemEquals at the kind's real cell width).
                return I("il2cpp::il2cpp_linq_list_union", 2, true, true, false,
                    new[] { "", "" }, returnTypeName: elemTracked, appendLiterals: new[] { K });
            case "Intersect":
                return I("il2cpp::il2cpp_linq_list_intersect", 2, true, true, false,
                    new[] { "", "" }, returnTypeName: elemTracked, appendLiterals: new[] { K });
            case "Zip":
            {
                // The selector's TResult family decides the result array (a
                // double/float projection must not be truncated to int32), and
                // the SECOND source's family decides how its cells are read (the
                // two sources may differ -- List<double>.Zip(int[])).
                byte tRes = genArgs is { Length: >= 3 } ? genArgs[2] : (byte)0x08;
                byte tFirst = genArgs is { Length: >= 1 } ? genArgs[0] : (byte)0x08;
                byte tSecond = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                // BATCH4: a reference-element List rides the mixed entry (it
                // reads the element as Il2CppObject* and calls the selector
                // through the statically declared ABI); the result cell may be a
                // reference array (6) or a narrow 1/2-byte cell (7..12).
                string? resKind = LinqZipResKindOfElemByte(tRes, genArgNames, 2);
                if (resKind == null)
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(tFirst)},{GenericArgTypeName(tSecond)}," +
                        $"{GenericArgTypeName(tRes)}> over a List not wired: no result cell ABI for " +
                        "this TResult (see docs/audit/b5-gaps.md)");
                string? secondKind = IsRefFamilyByte(tSecond) ? "1" : LinqListKindOfElemByte(tSecond, genArgNames, 1);
                if (secondKind == null)
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(tFirst)},{GenericArgTypeName(tSecond)},...> " +
                        "over a List not wired: the second source's element family has no cell ABI " +
                        "(see docs/audit/b5-gaps.md)");
                return I("il2cpp::il2cpp_linq_zip_family", 3, true, true, false,
                    new[] { "", "", "" }, returnTypeName: LinqArrayTypeNameExact(tRes, genArgNames, 2),
                    appendLiterals: new[] { K, secondKind, resKind });
            }
            case "SequenceEqual":
                return I("il2cpp::il2cpp_linq_list_sequence_equal", 2, true, false, false,
                    new[] { "", "" }, appendLiterals: new[] { K });
            case "OrderBy":
            case "OrderByDescending":
            {
                // A List<int> rides the int32 key-selector surface. A
                // REFERENCE-element list snapshots to a reference array and
                // rides the reference key-selector surface (the list entry
                // takes the desc flag as an appended literal).
                bool desc = name == "OrderByDescending";
                if (K == "0")
                {
                    return I(desc
                            ? "il2cpp::il2cpp_linq_list_order_by_key_desc_i4"
                            : "il2cpp::il2cpp_linq_list_order_by_key_i4",
                        2, true, true, false, L2,
                        returnTypeName: "System.Int32[]", appendLiterals: new[] { K });
                }
                if (K == "3")
                {
                    // b5-gaps section 49: a FLOAT-ELEMENT list. The snapshot is
                    // a float array; the key selector crosses in its own family
                    // slot (int32/int64/double/float all wired -- the array
                    // float-source face covers all four, so the list face
                    // mirrors it per key family).
                    if (genArgs is not { Length: >= 2 })
                        return null;
                    string? fKeyFam = genArgs[1] switch
                    {
                        0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "i4",
                        0x0A or 0x0B => "i8",
                        0x0D => "d",
                        0x0C => "f",
                        _ => null,
                    };
                    if (fKeyFam == null)
                        throw new NotSupportedException(
                            $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])},{GenericArgTypeName(genArgs[1])}> " +
                            "over a List<float> not wired: the list float key-selector surface covers a " +
                            "int32/int64/double/float key only (il2cpp_linq_list_order_by_key[_desc]_f_<keyfam>)");
                    return I(desc
                            ? $"il2cpp::il2cpp_linq_list_order_by_key_desc_f_{fKeyFam}"
                            : $"il2cpp::il2cpp_linq_list_order_by_key_f_{fKeyFam}",
                        2, true, true, false, L2,
                        returnTypeName: "System.Single[]", appendLiterals: new[] { K });
                }
                if (K == "4")
                {
                    // A DOUBLE-element list: the snapshot is a double array and
                    // the key crosses in its own family slot. A DOUBLE key rides
                    // the pre-existing il2cpp_linq_list_order_by_key[_desc]_d;
                    // a FLOAT key rides its OWN _f entries (impl-orderfam: a
                    // Func<double,float> leaf returns a 4-byte float per the
                    // MS x64 ABI, and the shared _d entry read it through a
                    // double(*) fn-pointer, mixing the callee's XMM0 residue
                    // into every key -- b5-gaps §14, list-orderkey-float).
                    // b5-gaps section 49: int32/int64 keys now ride their own
                    // _d_i4 / _d_i8 entries (the array double-source face covers
                    // all four key families).
                    if (genArgs is not { Length: >= 2 })
                        return null;
                    string? dKeyFam = genArgs[1] switch
                    {
                        0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "i4",
                        0x0A or 0x0B => "i8",
                        0x0D => "d",
                        0x0C => "f",
                        _ => null,
                    };
                    if (dKeyFam == null)
                        throw new NotSupportedException(
                            $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])},{GenericArgTypeName(genArgs[1])}> " +
                            "over a List<double> not wired: the list double key-selector surface covers a " +
                            "int32/int64/double/float key only (il2cpp_linq_list_order_by_key[_desc]{_d_<kf> / _f})");
                    string dEntry = dKeyFam switch
                    {
                        "i4" => desc
                            ? "il2cpp::il2cpp_linq_list_order_by_key_desc_d_i4"
                            : "il2cpp::il2cpp_linq_list_order_by_key_d_i4",
                        "i8" => desc
                            ? "il2cpp::il2cpp_linq_list_order_by_key_desc_d_i8"
                            : "il2cpp::il2cpp_linq_list_order_by_key_d_i8",
                        "d" => desc
                            ? "il2cpp::il2cpp_linq_list_order_by_key_desc_d"
                            : "il2cpp::il2cpp_linq_list_order_by_key_d",
                        _ => desc
                            ? "il2cpp::il2cpp_linq_list_order_by_key_desc_f"
                            : "il2cpp::il2cpp_linq_list_order_by_key_f",
                    };
                    return I(dEntry, 2, true, true, false, L2,
                        returnTypeName: "System.Double[]", appendLiterals: new[] { K });
                }
                if (K == "12")
                {
                    // b5-gaps section 50: a DECIMAL-ELEMENT list. The snapshot
                    // is a decimal array; the key selector crosses in its own
                    // family slot. The array decimal source supports the
                    // int32/int64/double/float/decimal primary families plus
                    // a System.DateTime / System.Guid struct key, so the list
                    // face mirrors the full seven-family surface.
                    if (genArgs is not { Length: >= 2 })
                        return null;
                    string? decKeyFam = genArgs[1] switch
                    {
                        0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "i4",
                        0x0A or 0x0B => "i8",
                        0x0D => "d",
                        0x0C => "f",
                        0x11 when IsDecimalGenArg(genArgNames, 1) => "dec",
                        0x11 when LinqStructKeyFamilyName(genArgNames, 1) is { } sk2
                            and ("dt" or "guid") => sk2,
                        _ => null,
                    };
                    if (decKeyFam == null)
                        throw new NotSupportedException(
                            $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])},{GenericArgTypeName(genArgs[1])}> " +
                            "over a List<decimal> not wired: the list decimal key-selector surface covers " +
                            "int32/int64/double/float/decimal and the System.DateTime/System.Guid keys only " +
                            "(il2cpp_linq_list_order_by_key[_desc]_dec_<keyfam>)");
                    return I(desc
                            ? $"il2cpp::il2cpp_linq_list_order_by_key_desc_dec_{decKeyFam}"
                            : $"il2cpp::il2cpp_linq_list_order_by_key_dec_{decKeyFam}",
                        2, true, true, false, L2,
                        returnTypeName: "System.Decimal[]", appendLiterals: new[] { K });
                }
                if (K != "1")
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a List of the element family kind {K} is not wired " +
                        "(the list key-selector surface covers int32, double and reference elements)");
                if (genArgs is not { Length: >= 2 })
                    return null;
                // impl-collation: a STATICALLY System.String key over the
                // reference-element list source binds the culture-collation
                // entry (net48 Comparer<string>.Default), the same contract as
                // the array reference-source face.
                string keySuffix = genArgs[1] == 0x0E
                    ? "string"
                    : LinqKeyFamilySuffix(genArgs[1]);
                if (keySuffix.Length == 0)
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])},{GenericArgTypeName(genArgs[1])}> " +
                        "not wired: the reference-source key-selector surface covers int32/int64/" +
                        "double/float and reference keys only");
                return I($"il2cpp::il2cpp_linq_list_order_by_key_ref_{keySuffix}", 2, true, true, false,
                    L2, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                    appendLiterals: new[] { desc ? "1" : "0", K });
            }
            case "ThenBy":
            case "ThenByDescending":
            {
                // impl-listchain: ThenBy over a List<T>. The fused composite is
                // built by StackFold from the OrderBy assignment; this marker
                // carries the SECONDARY key family code plus the list's element
                // kind literal.
                //   * int32 elements (kind "0"): the numeric families 0..3 --
                //     the LIST entry snaps the list ONCE and then runs the array
                //     chain kernel, so the marker also carries the kind;
                //   * reference elements (kind "1"): 4 = System.String (culture
                //     collation), 5 = any other reference, or 0..3 numeric.
                if (K == "0")
                {
                    if (genArgs is not { Length: >= 2 })
                        return null;
                    string? numK2 = LinqValKeyFamilyCode(genArgs[1]);
                    if (numK2 is not ("0" or "1" or "2" or "3"))
                        throw new NotSupportedException(
                            $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])}," +
                            $"{GenericArgTypeName(genArgs[1])}> over a List is not wired: the fused " +
                            "int32-element composite covers int32/int64/double/float secondary keys " +
                            "(il2cpp_linq_list_then_by_key_i4)");
                    return I(name == "ThenBy"
                            ? ResolvedMethod.ThenByCombineMarker
                            : ResolvedMethod.ThenByDescCombineMarker,
                        2, false, false, false, new[] { "", "" },
                        returnTypeName: "System.Int32[]",
                        appendLiterals: new[] { numK2, K });
                }
                if (K is "3" or "4")
                {
                    // b5-gaps section 49: a FLOAT/DOUBLE-ELEMENT List ThenBy.
                    // Folds into the list chain surface; the secondary key
                    // family is 0=int32, 1=int64, 2=double, 3=float.
                    if (genArgs is not { Length: >= 2 })
                        return null;
                    string? wideK2 = LinqValKeyFamilyCode(genArgs[1]);
                    if (wideK2 is not ("0" or "1" or "2" or "3"))
                        throw new NotSupportedException(
                            $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])}," +
                            $"{GenericArgTypeName(genArgs[1])}> over a List is not wired: the fused " +
                            "wide-element composite covers int32/int64/double/float secondary keys " +
                            "(il2cpp_linq_list_then_by_chain_d / _f)");
                    return I(name == "ThenBy"
                            ? ResolvedMethod.ThenByCombineMarker
                            : ResolvedMethod.ThenByDescCombineMarker,
                        2, false, false, false, new[] { "", "" },
                        returnTypeName: K == "4" ? "System.Double[]" : "System.Single[]",
                        appendLiterals: new[] { wideK2, K });
                }
                if (K != "1")
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a List of the element family kind {K} is not wired " +
                        "(the fused composite covers int32 and reference elements)");
                if (genArgs is not { Length: >= 2 })
                    return null;
                string? listK2 = genArgs[1] == 0x0E ? "4"
                    : IsRefFamilyByte(genArgs[1]) ? "5"
                    : LinqValKeyFamilyCode(genArgs[1]);
                if (listK2 is null)
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])}," +
                        $"{GenericArgTypeName(genArgs[1])}> over a List is not wired: the fused " +
                        "reference composite covers int32/int64/double/float, System.String or other " +
                        "reference secondary keys (il2cpp_linq_list_then_by_key_ref)");
                return I(name == "ThenBy"
                        ? ResolvedMethod.ThenByCombineMarker
                        : ResolvedMethod.ThenByDescCombineMarker,
                    2, false, false, false, new[] { "", "" },
                    returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                    appendLiterals: new[] { listK2, K });
            }
            case "Sum" or "Min" or "Max" or "Average":
            {
                string shortName = name.ToLowerInvariant();
                if (count < 2)
                {
                    // Source-only: the result family follows the list kind.
                    //
                    // A System.Decimal element list (kind 12) has its own
                    // source-only entries: the int32/i8/d/f ones read their own
                    // cell width, so routing a decimal list through the int32
                    // entry read a 4-byte slice of every 16-byte cell and
                    // assigned an int32 into a decimal slot (MEASURED: "no match
                    // for 'operator=' (Il2CppDecimal = int32_t)" on
                    // audit-fix3-d2k). The decimal result rides the explicit T3
                    // slot, so ResultKind "dec" prepends it.
                    if (K == "12")
                        return I($"il2cpp::il2cpp_linq_list_{shortName}_dec", 1, true, false, false,
                            L1, resultKind: "dec", appendLiterals: new[] { K });
                    // impl-collation: a List<string> Min/Max. Without this arm
                    // the reference kind ("1") fell through to the _i4 suffix
                    // below -- a SILENT wrong value (an int32 cell read out of a
                    // pointer-sized string slot). The net48 answer is the
                    // culture-collated winner (Comparer<string>.Default), with
                    // empty/all-null -> null and nulls never winning.
                    if (K == "1" && genArgs is { Length: >= 1 } && genArgs[0] == 0x0E)
                    {
                        if (name is "Min" or "Max")
                            return I($"il2cpp::il2cpp_linq_list_{shortName}_string", 1, true, true, false,
                                L1, returnTypeName: "System.String", appendLiterals: new[] { K });
                        throw new NotSupportedException(
                            $"Enumerable.{name}<System.String> over a List not wired: net48 has no " +
                            "Sum/Average overload for System.String");
                    }
                    if (K == "1")
                        throw new NotSupportedException(
                            $"Enumerable.{shortName} over a List of a reference element type is not wired " +
                            "unless the element is System.String (Min/Max only)");
                    string suffix = K switch
                    {
                        "2" => "i8",
                        "3" => "f",
                        "4" => "d",
                        _ => "i4",
                    };
                    string? rk = name == "Average" ? "d" : suffix == "i8" ? "l" : suffix is "f" or "d" ? "d" : null;
                    return I($"il2cpp::il2cpp_linq_list_{shortName}_{suffix}", 1, true, false, false,
                        L1, resultKind: rk, appendLiterals: new[] { K });
                }
                // Selector form: the projection type decides the entry suffix.
                var proj = LinqSelectorProjectionType(md, sigHandle);
                // A DECIMAL projection rides the 16-byte result-slot kernels (the
                // decimal T3 contract: the slot is the FIRST icall parameter and
                // ResultKind "dec" prepends it). The encoded name carries the
                // value-type marker ("V:System.Decimal").
                if (proj is "System.Decimal" or "V:System.Decimal")
                {
                    return I($"il2cpp::il2cpp_linq_list_{shortName}_sel_dec", 2, true, false, false,
                        L2, resultKind: "dec", appendLiterals: new[] { K });
                }
                string psuffix = proj switch
                {
                    "l" => "i8",
                    "d" => "d",
                    "f" => "f",
                    "i" => "i4",
                    _ => "",
                };
                if (psuffix.Length == 0)
                    throw new NotSupportedException(
                        $"Enumerable.{name} with selector projection '{proj}' over a List source is not wired " +
                        "(the list selector-aggregate surface covers int32/int64/double/float/decimal projections)");
                string? prk = name == "Average" ? "d" : psuffix == "i8" ? "l" : psuffix is "f" or "d" ? "d" : null;
                return I($"il2cpp::il2cpp_linq_list_{shortName}_sel_{psuffix}", 2, true, false, false,
                    L2, resultKind: prk, appendLiterals: new[] { K });
            }
            case "Aggregate":
                // The seed/accumulator family decides the entry (no-seed reads
                // the source element family).
                if (count == 2)
                {
                    // floatfix: K=3 (float elements) keeps its OWN entry -- the
                    // list snapshot is REAL 4-byte float cells
                    // (il2cpp_list_obj_to_array_float), so the double-family
                    // kernel would read 8-byte cells out of a 4-byte snapshot
                    // AND accumulate in double (net48 rounds every step to R4).
                    string suffix = K switch { "2" => "i8", "3" => "f", "4" => "d", _ => "i4" };
                    return I($"il2cpp::il2cpp_linq_list_aggregate_{suffix}", 2, true, false, false,
                        L2, resultKind: suffix == "i8" ? "l" : suffix is "f" or "d" ? "d" : null,
                        appendLiterals: new[] { K });
                }
                if (count >= 3)
                {
                    byte accByte = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                    // The 0x11 byte covers EVERY value type: only a real
                    // System.Decimal has a list cell/storage kind.
                    bool elemDec = genArgNames is { Length: >= 1 } en
                        && en[0] is "System.Decimal" or "V:System.Decimal";
                    bool accDec = accByte == 0x11 && genArgNames is { Length: >= 2 } an
                        && an[1] is "System.Decimal" or "V:System.Decimal";
                    // ELEMENT family of the snapshot cells (the LIST kind K):
                    //   int32-slot (int32 + the 1/2-byte narrow/uint families)
                    //   -> i4;  int64 -> i8;  real 4-byte float -> f;
                    //   8-byte double -> d;  reference slot -> ref;
                    //   16-byte System.Decimal -> dec.
                    string elemFam = elemDec ? "dec" : K switch
                    {
                        "0" or "5" or "6" or "7" or "8" or "9" or "10" or "11" => "i4",
                        "2" => "i8",
                        "3" => "f",
                        "4" => "d",
                        "1" => "ref",
                        "12" => "dec",
                        _ => "",
                    };
                    if (elemFam.Length == 0)
                        throw new NotSupportedException(
                            $"Enumerable.Aggregate over a List of the element family kind {K} is not wired");
                    if (elemFam == "ref" && genArgs is { Length: >= 1 } && genArgs[0] == 0x11)
                        throw new NotSupportedException(
                            "Enumerable.Aggregate over a List<DateTime/TimeSpan/Guid/user-struct> " +
                            "is not wired: the list domain has no element storage kind for a by-value " +
                            "value struct (a reference snapshot would pass a pointer where the lambda " +
                            "takes the struct by value); see docs/audit/b5-gaps.md");
                    // ACCUMULATOR family: the TAccumulate metadata byte (with the
                    // 0x11 name split decimal vs the other value types).
                    string accFam = accDec ? "dec" : accByte switch
                    {
                        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i4",
                        0x0A or 0x0B => "i8",
                        0x0C => "f",
                        0x0D => "d",
                        _ when IsRefFamilyByte(accByte) => "ref",
                        _ => "",
                    };
                    if (accFam.Length == 0)
                        throw new NotSupportedException(
                            $"Enumerable.Aggregate TAccumulate '{GenericArgTypeName(accByte)}' is not a " +
                            "supported accumulator family over a List (the list seed matrix covers " +
                            "int32/int64/float/double/decimal/reference accumulators; see " +
                            "docs/audit/b5-gaps.md)");
                    // The 4-parameter form (seed, func, resultSelector): the
                    // fold is the SAME (accumulator, element) entry as the
                    // 3-parameter form; the result selector runs ONCE on the
                    // final accumulator. The runtime ships the full 6x6x6
                    // matrix (il2cpp_linq_list_aggregate_seed_<acc>_<elem>_rs_
                    // <res>), so EVERY (TAccumulate, TResult) pair the fold
                    // matrix supports is statically emittable -- no LOUD.
                    if (count == 4)
                    {
                        byte resByte = genArgs is { Length: >= 3 } ? genArgs[2] : (byte)0x08;
                        bool resDec = resByte == 0x11 && genArgNames is { Length: >= 3 } rn
                            && rn[2] is "System.Decimal" or "V:System.Decimal";
                        // impl-loudvalstruct (C): a 0x11 TResult is every value
                        // type. The four BY-VALUE struct families ride the same
                        // register-return convention the element accessors use,
                        // so the projection matrix carries them too (the LOUD
                        // below used to reject every one of them).
                        string? resStruct = resByte == 0x11
                            ? LinqStructResultSuffix(genArgNames, 2) : null;
                        string resFam = resDec ? "dec" : resStruct ?? resByte switch
                        {
                            0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i4",
                            0x0A or 0x0B => "i8",
                            0x0C => "f",
                            0x0D => "d",
                            _ when IsRefFamilyByte(resByte) => "ref",
                            _ => "",
                        };
                        if (resFam.Length == 0)
                            throw new NotSupportedException(
                                "LOUD whitelist (no runtime descriptor for the TResult struct): " +
                                "Enumerable.Aggregate TResult " +
                                $"{GenericArgTypeName(resByte)} over a List is not a supported result " +
                                "family -- the runtime projects the accumulator to int32/int64/float/" +
                                "double/decimal/reference and the System.DateTime/System.TimeSpan/" +
                                "System.Guid/System.DateTimeOffset value structs only (a USER struct " +
                                "has no result ABI) [Reference Source " +
                                "System.Core/System/Linq/Enumerable.cs:1411]");
                        string[] rsCasts = accFam switch
                        {
                            "dec" => new[] { "", "dec", "", "" },
                            "ref" => new[] { "", "", "", "" },
                            "i4" => new[] { "", "i", "", "" },
                            "i8" => new[] { "", "l", "", "" },
                            _ => new[] { "", "d", "", "" },
                        };
                        bool resRef = resFam == "ref";
                        return I($"il2cpp::il2cpp_linq_list_aggregate_seed_{accFam}_{elemFam}_rs_{resFam}",
                            4, true, resRef, false, rsCasts,
                            resultKind: resFam == "dec" ? "dec"
                                : resFam == "i8" ? "l"
                                : resFam is "f" or "d" ? "d"
                                : resFam is "dt" ? "dt"
                                : resFam is "ts" ? "ts"
                                : resFam is "guid" ? "guidv"
                                : resFam is "dtoffset" ? "dto" : null,
                            appendLiterals: new[] { elemDec ? "12" : K },
                            returnTypeName: resStruct == null ? null : LinqArrayTypeNameExact(
                                resByte, genArgNames, 2).TrimEnd('[', ']'));
                    }
                    // The float seed rides the double F stack; the decimal seed
                    // crosses as a const value-slot pointer (the T3 result-slot
                    // contract, ResultKind "dec").
                    string[] casts = accFam switch
                    {
                        "dec" => new[] { "", "dec", "" },
                        "ref" => new[] { "", "", "" },
                        "i4" => new[] { "", "i", "" },
                        "i8" => new[] { "", "l", "" },
                        _ => new[] { "", "d", "" },
                    };
                    bool refResult = accFam == "ref";
                    return I($"il2cpp::il2cpp_linq_list_aggregate_seed_{accFam}_{elemFam}",
                        3, true, refResult, false, casts,
                        resultKind: accFam == "dec" ? "dec"
                            : accFam == "i8" ? "l"
                            : accFam is "f" or "d" ? "d" : null,
                        appendLiterals: new[] { elemDec ? "12" : K });
                }
                return null;
            case "GroupBy":
                // floatgrp: a List<System.Single> source. The list domain's own
                // entry snapshots through IL2CPP_LIST_KIND_I4 and calls the
                // int32-source kernel -- it would read every boxed Single as an
                // int32 and drop the key family (a silent wrong result). The
                // float entry snapshots the list to its REAL float[] first and
                // rides the float-source kernel, so the KEY family must ride
                // along as an appended literal.
                if (K == "3")
                {
                    int fKeyKind = genArgs is { Length: >= 2 }
                        ? KeyKindForNamed(genArgs[1], genArgNames, 1) : -1;
                    // The optional elementSelector / comparer / resultSelector
                    // slots must be mapped by ARG ORDER (the runtime entry takes
                    // all five plus three appended literals); an unmapped slot
                    // is passed as nullptr by the emitter.
                    var fParams = LinqEncodedParamTypes(md, sigHandle);
                    int fElemArg = -1, fCmpArg = -1, fResArg = -1;
                    for (int pi = 2; pi < fParams.Length; pi++)
                    {
                        var fdef = GenericDefNameOf(fParams[pi]);
                        if (fdef == "System.Func`2") fElemArg = pi;
                        else if (fdef == "System.Func`3") fResArg = pi;
                        else if (fdef == "System.Collections.Generic.IEqualityComparer`1") fCmpArg = pi;
                    }
                    string fDefaultElem = "7";
                    byte? fElemByte = genArgs is { Length: >= 3 } ? genArgs[2] : null;
                    string fEk = fElemByte is { } feb
                        ? (feb switch
                        {
                            0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "0",
                            0x0A or 0x0B => "2",
                            0x0D => "3",
                            0x0C => "7",
                            0x0E or 0x12 or 0x1C or 0x1D or 0x15 or 0x13 or 0x1E => "1",
                            _ => "",
                        })
                        : fDefaultElem;
                    if (fEk.Length == 0 || fKeyKind < 0)
                    {
                        throw new NotSupportedException(
                            "Enumerable.GroupBy over a System.Collections.Generic.List<System.Single> " +
                            "source is not wired for this TElement/TKey combination (see " +
                            "docs/audit/b5-gaps.md)");
                    }
                    byte? fResByte = genArgs is { Length: >= 4 } ? genArgs[3]
                        : genArgs is { Length: >= 3 } ? genArgs[2] : null;
                    string fRk = fResByte is { } frb
                        ? (frb switch
                        {
                            0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "1",
                            0x0A or 0x0B => "3",
                            0x0D => "4",
                            0x0C => "5",
                            _ => "2",
                        })
                        : "0";
                    string fRetType = fRk == "0"
                        ? "System.Linq.IGroupingResult"
                        : fRk == "1" ? "System.Int32[]"
                        : fRk == "3" ? "System.Int64[]"
                        : fRk is "4" or "5" ? "System.Double[]" : "System.Object[]";
                    return I("il2cpp::il2cpp_linq_list_float_group_by_full", count, true, true, false,
                        new[] { "", "", "", "", "" },
                        returnTypeName: fRetType,
                        appendLiterals: new[]
                        {
                            fEk, fRk,
                            fKeyKind.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        },
                        argOrder: new[] { 0, 1, fElemArg, fCmpArg, fResArg });
                }
                // impl-listselnum F4: the list entry MUST know the receiver's
                // element family -- its old body snapshotted EVERY list as
                // IL2CPP_LIST_KIND_I4, so a List<long>/List<double>/List<string>
                // source reached the int32 grouping kernel (the key selector got
                // a truncated element: a silent wrong grouping). The element and
                // result families, the TKey carrier and the list kind itself now
                // ride as trailing literals.
                {
                    var gbListParams = LinqEncodedParamTypes(md, sigHandle);
                    int lElemArg = -1, lCmpArg = -1, lResArg = -1;
                    for (int pi = 2; pi < gbListParams.Length; pi++)
                    {
                        var ldef = GenericDefNameOf(gbListParams[pi]);
                        if (ldef == "System.Func`2") lElemArg = pi;
                        else if (ldef == "System.Func`3") lResArg = pi;
                        else if (ldef == "System.Collections.Generic.IEqualityComparer`1") lCmpArg = pi;
                    }
                    byte? lElemByte = lElemArg >= 0 && genArgs is { Length: >= 3 } ? genArgs[2] : null;
                    byte? lResByte = lResArg >= 0 && genArgs is { Length: >= 3 }
                        ? (genArgs.Length >= 4 ? genArgs[3] : genArgs[2])
                        : null;
                    // The element family: a reference element is "1"; a float
                    // element is "7" (a real 4-byte cell, never the int32 family).
                    string lEk = lElemByte is { } leb
                        ? (leb switch
                        {
                            0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "0",
                            0x0A or 0x0B => "2",
                            0x0D => "3",
                            0x0C => "7",
                            0x0E or 0x12 or 0x1C or 0x1D or 0x15 or 0x13 or 0x1E => "1",
                            _ => "",
                        })
                        : "0";
                    if (lEk.Length == 0)
                    {
                        throw new NotSupportedException(
                            $"Enumerable.GroupBy TElement '{GenericArgTypeName(lElemByte!.Value)}' " +
                            "over a List source is not a supported element family");
                    }
                    string lRk = lResByte is { } lrb
                        ? (lrb switch
                        {
                            0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "1",
                            0x0A or 0x0B => "3",
                            0x0D => "4",
                            0x0C => "5",
                            _ => "2",
                        })
                        : "0";
                    int lKeyKind = genArgs is { Length: >= 2 }
                        ? KeyKindForNamed(genArgs[1], genArgNames, 1) : -1;
                    if (lKeyKind < 0)
                    {
                        string lKeyName = genArgNames is { Length: >= 2 } ? genArgNames[1] : "?";
                        throw new NotSupportedException(
                            $"Enumerable.GroupBy over a List source not wired: {DescribeRejectedKey(genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0, lKeyName)} " +
                            "has no runtime key carrier (see docs/audit/b5-gaps.md)");
                    }
                    var lArgOrder = new[] { 0, 1, lElemArg, lCmpArg, lResArg };
                    var lRetType = lRk == "0"
                        ? "System.Linq.IGroupingResult"
                        : lRk == "1" ? "System.Int32[]"
                        : lRk == "3" ? "System.Int64[]"
                        : lRk is "4" or "5" ? "System.Double[]" : "System.Object[]";
                    return I("il2cpp::il2cpp_linq_list_group_by_full", count, true, true, false,
                        new[] { "", "", "", "", "" },
                        returnTypeName: lRetType,
                        appendLiterals: new[]
                        {
                            lEk, lRk, K,
                            lKeyKind.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        },
                        argOrder: lArgOrder);
                }
            default:
                return null;
        }
    }

    // The wide/narrow ARRAY family selector: null when the family is i4 or ref
    // (those have their own paths), otherwise the runtime cell-kind literal for
    // narrow sources or "" for i8/f/d.
    private static string? LinqWideNarrowKind(string srcFam, byte[]? genArgs)
    {
        if (srcFam is "i8" or "f" or "d") return "";
        if (srcFam != "narrow") return null;
        if (genArgs is { Length: >= 1 } && LinqNarrowCode(genArgs[0]) is { } nc) return nc;
        // Receiver-name fallback (a Select result of a narrow family).
        return null;
    }

    //--------------------------------------------------------------------------
    // TryMapLinqWide -- Any/Count/All/Where/Select/First/FirstOrDefault/Last/
    // LastOrDefault over the int64/float/double/narrow ARRAY families. Each
    // entry reads the element at its REAL cell width.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapLinqWide(MetadataReader md, BlobHandle sigHandle, string name,
        int count, byte[]? genArgs, string kindCode, string srcFam, string[] TwoArg, string[] OneArg,
        string[]? genArgNames = null)
    {
        bool narrow = srcFam == "narrow";
        string suffix = srcFam switch { "i8" => "i8", "f" => "f", "d" => "d", _ => "narrow" };
        // The narrow cell-kind code is an APPENDED literal, never a consumed IL
        // argument (ParamCount stays the IL arity -- a 3-slot ParamCount would
        // desync the fold's stack).
        var kindAppend = narrow ? new[] { kindCode } : null;
        string abi = LinqArrayAbiSuffix(srcFam);
        var elemTracked = genArgs is { Length: >= 1 } ? LinqArrayTypeNameExact(genArgs[0])
            : srcFam switch
            {
                "i8" => "System.Int64[]",
                "f" => "System.Single[]",
                "d" => "System.Double[]",
                _ => "System.Object[]",
            };
        var elemScalar = elemTracked.TrimEnd('[', ']');

        // D-LINQ-FAM: the family-generic kernel trailing literal (the CELL
        // code of the source element). The runtime ships ONE kernel per
        // operator (linq_family.h) and reads/writes the element at its REAL
        // width, so the int64/float/double/narrow array families ride the
        // same entry. Null for an element byte with no cell ABI -- the
        // operator then falls through to the int32-only gate and keeps its
        // LOUD message (never a fabricated 4-byte read of an 8-byte cell).
        string? cellCode = genArgs is { Length: >= 1 } ? LinqCellCodeOfElemByte(genArgs[0]) : null;

        switch (name)
        {
            // D-LINQ-FAM: the family-generic element operators. Each returns
            // a fresh array of the SAME cell (except the scalar-returning
            // LongCount/Contains/SequenceEqual/ElementAt) and is tracked as
            // the source's element type so downstream operators resolve.
            case "Take":
                // Take(count) -> the first `count` elements (net48 slice
                // semantics: count <= 0 -> empty, count >= length -> full
                // copy). The count rides the "i" cast, the source the "a".
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_take_family", 2, true, true, false,
                    new[] { "a", "i" }, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "Skip":
                // Skip(count) -> everything after the first `count` elements
                // (net48: count <= 0 -> full copy, count >= length -> empty).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_skip_family", 2, true, true, false,
                    new[] { "a", "i" }, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "Reverse":
                // Reverse() -> a fresh array with the elements in reverse
                // order (net48).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_reverse_family", 1, true, true, false,
                    OneArg, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "Concat":
                // Concat(source, second) -> a fresh array holding ALL of
                // source then ALL of second, order AND duplicates preserved
                // (net48; length = sum of the parts).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_concat_family", 2, true, true, false,
                    new[] { "a", "a" }, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "Distinct" when count < 2:
                // Distinct() -> duplicates removed keeping FIRST-APPEARANCE
                // order (net48 default equality).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_distinct_family", 1, true, true, false,
                    OneArg, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "Distinct":
                // b5-gaps section 57: Distinct(source, comparer) over a value
                // cell -- first-appearance under the user comparer.
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_distinct_value_cmp", 2, true, true, false,
                    new[] { "a", "" }, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "Except" when count < 3:
                // Except(source, second) -> the elements of source NOT in
                // second, first-appearance order, DEDUPLICATED output (net48).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_except_family", 2, true, true, false,
                    new[] { "a", "a" }, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "Except":
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_except_value_cmp", 3, true, true, false,
                    new[] { "a", "a", "" }, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "Union" when count < 3:
                // Union(source, second) -> first's elements then second's,
                // deduplicated by first-appearance (net48 Union = distinct
                // concat).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_union_family", 2, true, true, false,
                    new[] { "a", "a" }, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "Union":
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_union_value_cmp", 3, true, true, false,
                    new[] { "a", "a", "" }, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "Intersect" when count < 3:
                // Intersect(source, second) -> the elements of source that
                // are also in second, first-appearance order, DEDUPLICATED
                // (net48).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_intersect_family", 2, true, true, false,
                    new[] { "a", "a" }, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "Intersect":
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_intersect_value_cmp", 3, true, true, false,
                    new[] { "a", "a", "" }, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "DefaultIfEmpty" when count < 2:
                // DefaultIfEmpty() -> non-empty source as-is; an empty source
                // yields a single default(T) element (net48).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_default_if_empty_family", 1, true, true, false,
                    OneArg, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "DefaultIfEmpty":
                // DefaultIfEmpty(source, defaultValue) over a wide/narrow value
                // source: non-empty -> the source as-is; empty -> a one-element
                // array holding `value` at the family's REAL cell width. The
                // value crosses in the family's own parameter slot (int32 for
                // the narrow cells, int64 for int64, double for float/double).
                if (cellCode == null) return null;
                return srcFam switch
                {
                    "i8" => I("il2cpp::il2cpp_linq_default_if_empty_family_i8", 2, true, true, false,
                        new[] { "a", "l" }, returnTypeName: elemTracked,
                        appendLiterals: new[] { cellCode }),
                    "f" or "d" => I("il2cpp::il2cpp_linq_default_if_empty_family_d", 2, true, true, false,
                        new[] { "a", "d" }, returnTypeName: elemTracked,
                        appendLiterals: new[] { cellCode }),
                    _ => I("il2cpp::il2cpp_linq_default_if_empty_family_i4", 2, true, true, false,
                        new[] { "a", "i" }, returnTypeName: elemTracked,
                        appendLiterals: new[] { cellCode }),
                };
            case "LongCount" when count < 2:
                // LongCount() -> the element count as int64 (net48).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_long_count_family", 1, true, false, false,
                    OneArg, resultKind: "l", appendLiterals: new[] { cellCode });
            case "SequenceEqual" when count < 3:
                // SequenceEqual(source, second) -> true iff same length and
                // every pair equal (net48 default equality).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_sequence_equal_family", 2, true, false, false,
                    new[] { "a", "a" }, appendLiterals: new[] { cellCode });
            case "SequenceEqual":
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_sequence_equal_value_cmp", 3, true, false, false,
                    new[] { "a", "a", "" }, appendLiterals: new[] { cellCode });
            case "Contains" when count < 3:
                // Contains(value) -> ordinal/default equality. The value
                // rides the family's OWN parameter slot: the int32 slot for
                // the narrow/int32 cells, the int64 slot for int64, the
                // double slot for float/double.
                if (cellCode == null) return null;
                return srcFam switch
                {
                    "i8" => I("il2cpp::il2cpp_linq_contains_family_i8", 2, true, false, false,
                        new[] { "a", "l" }, appendLiterals: new[] { cellCode }),
                    "f" or "d" => I("il2cpp::il2cpp_linq_contains_family_d", 2, true, false, false,
                        new[] { "a", "d" }, appendLiterals: new[] { cellCode }),
                    _ => I("il2cpp::il2cpp_linq_contains_family_i4", 2, true, false, false,
                        new[] { "a", "i" }, appendLiterals: new[] { cellCode }),
                };
            case "Contains":
                // b5-gaps section 57: Contains(value, comparer) -- the comparer
                // rides the LAST slot, the value its family slot.
                if (cellCode == null) return null;
                return srcFam switch
                {
                    "i8" => I("il2cpp::il2cpp_linq_contains_value_cmp_i8", 3, true, false, false,
                        new[] { "a", "l", "" }, appendLiterals: new[] { cellCode }),
                    "f" or "d" => I("il2cpp::il2cpp_linq_contains_value_cmp_d", 3, true, false, false,
                        new[] { "a", "d", "" }, appendLiterals: new[] { cellCode }),
                    _ => I("il2cpp::il2cpp_linq_contains_value_cmp_i4", 3, true, false, false,
                        new[] { "a", "i", "" }, appendLiterals: new[] { cellCode }),
                };
            case "ElementAt":
                // ElementAt(index) -> the element at `index`; index < 0 or
                // >= length -> ArgumentOutOfRangeException. The result rides
                // the family's result ABI (int32 for the narrow/int32 cells,
                // int64 for int64, double for float/double).
                if (cellCode == null) return null;
                return srcFam switch
                {
                    "i8" => I("il2cpp::il2cpp_linq_element_at_fam_i8", 2, true, false, false,
                        new[] { "a", "i" }, resultKind: "l",
                        appendLiterals: new[] { cellCode }),
                    "f" or "d" => I("il2cpp::il2cpp_linq_element_at_fam_d", 2, true, false, false,
                        new[] { "a", "i" }, resultKind: "d",
                        appendLiterals: new[] { cellCode }),
                    _ => I("il2cpp::il2cpp_linq_element_at_fam_i4", 2, true, false, false,
                        new[] { "a", "i" }, appendLiterals: new[] { cellCode }),
                };
            // p0conv2 (D3): OrderBy / OrderByDescending over an INT64 value
            // SOURCE with a key selector. The int32-source entries
            // (il2cpp_linq_order_by_key_i4) read 4-byte cells and materialize
            // an int32[] result, so a long[]/ulong[] source needs the i8-source
            // family (il2cpp_linq_order_by_key[_desc]_i8_<keyfam>): 8-byte
            // element cells in, a fresh int64[] (elemKind I8) out. The KEY
            // family is int32 (0x08-slot primitives) or int64 (0x0A/0x0B);
            // anything else has no i8-source key kernel.
            case "OrderBy" when srcFam == "i8" && count >= 2:
            case "OrderByDescending" when srcFam == "i8" && count >= 2:
            {
                if (LinqSecondParamGenericArgCount(md, sigHandle) == 3)
                    throw new NotSupportedException(
                        $"Enumerable.{name} over an int64-element source with an (element, index) key selector " +
                        "is not wired: LOUD whitelist �?-- net48 has no (element, index) key selector overload. " +
                        "MS Learn Enumerable.OrderBy (view=netframework-4.8.1) and the net48 Reference Source " +
                        "System.Core/System/Linq/Enumerable.cs list only OrderBy<TSource,TKey>(IEnumerable<TSource>, " +
                        "Func<TSource,TKey>) and OrderBy<TSource,TKey>(..., IComparer<TKey>); the (element, index) " +
                        "shape is a .NET 9 Index() family form (see docs/audit/b5-gaps.md)");
                // b5-gaps section 71: OrderBy(source, keySelector, comparer)
                // over an int64 source -> the comparator kernel (value key via
                // the cell-carrier dispatch, reference key via the rooted ref
                // kernel). The (element,index) guard above already rejected the
                // .NET 9 index form; a comparer param means count >= 3.
                if (LinqHasComparerParam(md, sigHandle))
                {
                    return LinqOrderByKeyEntryCmp(md, sigHandle, genArgs, genArgNames,
                        desc: name == "OrderByDescending", srcSuffix: "i8");
                }
                byte keyByte = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                // impl-linq3 (B): a DateTime / Guid key rides the struct
                // carrier sort over the int64 source.
                if (keyByte == 0x11 && LinqStructKeyFamilyName(genArgNames, 1) is { } sk)
                {
                    return I($"il2cpp::il2cpp_linq_order_by_key{(name == "OrderBy" ? "" : "_desc")}_i8_{sk}",
                        2, true, true, false, TwoArg, returnTypeName: "System.Int64[]");
                }
                string? keyFam = keyByte switch
                {
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i4",
                    0x0A or 0x0B => "i8",
                    // impl-floatkey: float gets its OWN entry (a 4-byte float
                    // return read through a double(*) fn-pointer mixes the
                    // callee's XMM0 residue into every key -- b5-gaps §14).
                    0x0C => "f",
                    0x0D => "d",
                    _ => null,
                };
                if (keyFam == null)
                {
                    string elemName = genArgs is { Length: >= 1 } ? GenericArgTypeName(genArgs[0]) : "?";
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{elemName},{GenericArgTypeName(keyByte)}> " +
                        "not wired: the int64-source key-selector surface covers int32, int64, " +
                        "double/float and System.DateTime/System.Guid keys only (a reference key " +
                        "has no int64-source kernel)");
                }
                return I($"il2cpp::il2cpp_linq_order_by_key{(name == "OrderBy" ? "" : "_desc")}_i8_{keyFam}",
                    2, true, true, false, TwoArg, returnTypeName: "System.Int64[]");
            }
            // impl-orderfam: OrderBy / OrderByDescending over a DOUBLE / FLOAT
            // value SOURCE with a key selector (net48 OrderBy<double/float,
            // TKey>). The int32-source entries read 4-byte cells and
            // materialize an int32[] result, so a wide source needs its own
            // element family: the elements are 8-byte double / 4-byte float
            // cells and the result is a fresh array of the SOURCE's family
            // (elemKind D / F). The element crosses to the selector in the
            // F-stack slot (a float element widens to double -- the
            // InvokePredF / FPredFn convention) and the key returns in its own
            // family slot (a float key through the float(*) reader --
            // impl-floatkey, b5-gaps §14). The source NAME gates the surface:
            // ONLY System.Double[] / System.Single[] sources are wired here;
            // every other element family keeps its own gate.
            case "OrderBy" when (srcFam == "d" || srcFam == "f") && count >= 2:
            case "OrderByDescending" when (srcFam == "d" || srcFam == "f") && count >= 2:
            {
                if (LinqSecondParamGenericArgCount(md, sigHandle) == 3)
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a double/float-element source with an (element, index) key selector " +
                        "is not wired: LOUD whitelist �?-- net48 has no (element, index) key selector overload. " +
                        "MS Learn Enumerable.OrderBy (view=netframework-4.8.1) and the net48 Reference Source " +
                        "System.Core/System/Linq/Enumerable.cs list only OrderBy<TSource,TKey>(IEnumerable<TSource>, " +
                        "Func<TSource,TKey>) and OrderBy<TSource,TKey>(..., IComparer<TKey>); the (element, index) " +
                        "shape is a .NET 9 Index() family form (see docs/audit/b5-gaps.md)");
                // b5-gaps section 71: the comparer overload over a double/float
                // source -> the comparator kernel (_cmp value key / _ref_cmp
                // reference key), one per source family.
                if (LinqHasComparerParam(md, sigHandle))
                {
                    return LinqOrderByKeyEntryCmp(md, sigHandle, genArgs, genArgNames,
                        desc: name == "OrderByDescending", srcSuffix: srcFam);
                }
                byte keyByte = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                string? keyFam = keyByte switch
                {
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i4",
                    0x0A or 0x0B => "i8",
                    // impl-floatkey: float gets its OWN entry (the 4-byte
                    // float return is never read through a double fn-pointer).
                    0x0C => "f",
                    0x0D => "d",
                    _ => null,
                };
                if (keyFam == null)
                {
                    string elemName = genArgs is { Length: >= 1 } ? GenericArgTypeName(genArgs[0]) : "?";
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{elemName},{GenericArgTypeName(keyByte)}> " +
                        "not wired: the double/float-source key-selector surface covers int32, int64, " +
                        "double and float keys only (a reference / struct key has no wide-source kernel)");
                }
                // The pre-existing plain _d entry IS the double-source
                // double-key face; every other (source, key) pair rides the
                // _<src>_<key> composite suffix (the int64-source naming).
                string wideEntry = (srcFam == "d" && keyFam == "d")
                    ? $"il2cpp::il2cpp_linq_order_by_key{(name == "OrderBy" ? "" : "_desc")}_d"
                    : $"il2cpp::il2cpp_linq_order_by_key{(name == "OrderBy" ? "" : "_desc")}_{srcFam}_{keyFam}";
                return I(wideEntry, 2, true, true, false, TwoArg,
                    returnTypeName: srcFam == "d" ? "System.Double[]" : "System.Single[]");
            }
            case "Any" when count < 2:
                // Family-agnostic: reads only the array header.
                return I("il2cpp::il2cpp_linq_any", 1, true, false, false, OneArg);
            case "Any":
                return I($"il2cpp::il2cpp_linq_any_{suffix}", 2, true, false, false, TwoArg,
                    appendLiterals: kindAppend);
            case "Count" when count >= 2:
                return I($"il2cpp::il2cpp_linq_count_{suffix}", 2, true, false, false, TwoArg,
                    appendLiterals: kindAppend);
            case "All":
                return I($"il2cpp::il2cpp_linq_all_{suffix}", 2, true, false, false, TwoArg,
                    appendLiterals: kindAppend);
            case "Where":
                return I($"il2cpp::il2cpp_linq_where_{suffix}", 2, true, true, false,
                    TwoArg, returnTypeName: elemTracked, appendLiterals: kindAppend);
            case "Select":
            {
                byte tRes = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                // delta 2: a System.DateTimeOffset result over the wide source
                // families (i8/f/d). DateTimeOffset has its own 16-byte by-value
                // selector ABI, so it does NOT ride select_wide's resKind space.
                // The narrow source families (1/2/4-byte integral) would need a
                // cell-kind-aware dtoffset kernel and stay LOUD.
                if (tRes == 0x11 && genArgNames is { Length: >= 2 }
                    && genArgNames[1] == "System.DateTimeOffset")
                {
                    if (narrow)
                        throw new NotSupportedException(
                            "Enumerable.Select over a narrow-element source with a " +
                            "System.DateTimeOffset result is not wired (no cell-kind-aware " +
                            "dtoffset selector kernel)");
                    bool wideSelIdx = LinqSecondParamGenericArgCount(md, sigHandle) == 3;
                    return I($"il2cpp::il2cpp_linq_select_{(wideSelIdx ? "idx_" : "")}{suffix}_dtoffset",
                        2, true, true, false, TwoArg,
                        returnTypeName: "System.DateTimeOffset[]");
                }
                string resKind = tRes switch
                {
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "0",
                    0x0A or 0x0B => "2",
                    0x0C => "3",
                    0x0D => "4",
                    _ => "1",
                };
                string srcKind = srcFam switch { "i8" => "2", "f" => "3", "d" => "4", _ => kindCode };
                return I("il2cpp::il2cpp_linq_select_wide", 2, true, true, false, TwoArg,
                    returnTypeName: LinqArrayTypeNameExact(tRes),
                    appendLiterals: new[] { srcKind, resKind });
            }
            case "First" when count < 2:
                return I($"il2cpp::il2cpp_linq_first_{suffix}", 1, true, false, false,
                    OneArg, appendLiterals: kindAppend,
                    resultKind: abi == "i8" ? "l" : abi == "d" ? "d" : null);
            case "FirstOrDefault" when count < 2:
                return I($"il2cpp::il2cpp_linq_first_or_default_{suffix}", 1, true, false, false,
                    OneArg, appendLiterals: kindAppend,
                    resultKind: abi == "i8" ? "l" : abi == "d" ? "d" : null);
            case "Last" when count < 2:
                return I($"il2cpp::il2cpp_linq_last_{suffix}", 1, true, false, false,
                    OneArg, appendLiterals: kindAppend,
                    resultKind: abi == "i8" ? "l" : abi == "d" ? "d" : null);
            case "LastOrDefault" when count < 2:
                return I($"il2cpp::il2cpp_linq_last_or_default_{suffix}", 1, true, false, false,
                    OneArg, appendLiterals: kindAppend,
                    resultKind: abi == "i8" ? "l" : abi == "d" ? "d" : null);
            case "Zip":
            {
                // Zip over an ARRAY (or List) source. The unified runtime entry
                // takes IL2CPP_LIST_KIND_* codes for BOTH sides (each side is
                // materialized/read at its OWN family, so the two may differ)
                // and decides List-vs-array from the object descriptor -- the
                // transpiler cannot (Zip's arity puts the selector on top of the
                // stack, and a List<T> local is tracked as its element array).
                byte tFirst = genArgs is { Length: >= 1 } ? genArgs[0] : (byte)0x08;
                byte tSecond = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                byte tRes = genArgs is { Length: >= 3 } ? genArgs[2] : (byte)0x08;
                string firstKind = IsRefFamilyByte(tFirst) ? "1" : LinqListKindOfElemByte(tFirst, genArgNames, 0) ?? "";
                string secondKind = IsRefFamilyByte(tSecond) ? "1" : LinqListKindOfElemByte(tSecond, genArgNames, 1) ?? "";
                if (firstKind.Length == 0 || secondKind.Length == 0)
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(tFirst)},{GenericArgTypeName(tSecond)},...> " +
                        "not wired: an element family with no cell ABI (see docs/audit/b5-gaps.md)");
                // BATCH4: a reference-element source rides the mixed entry (it
                // reads the element as Il2CppObject* and calls the selector
                // through the statically declared ABI); the result cell may be a
                // reference array (6) or a narrow 1/2-byte cell (7..12).
                string? resKind = LinqZipResKindOfElemByte(tRes, genArgNames, 2);
                if (resKind == null)
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(tFirst)},{GenericArgTypeName(tSecond)}," +
                        $"{GenericArgTypeName(tRes)}> not wired: no result cell ABI for this TResult " +
                        "(see docs/audit/b5-gaps.md)");
                return I("il2cpp::il2cpp_linq_zip_family", 3, true, true, false,
                    new[] { "", "", "" }, returnTypeName: LinqArrayTypeNameExact(tRes, genArgNames, 2),
                    appendLiterals: new[] { firstKind, secondKind, resKind });
            }
            // D-LINQ-FAM (source-only): Single / SingleOrDefault over the
            // int64 / float / double / narrow array families. The family kernel
            // validates the cell and reads the single element at its REAL width
            // (the narrow cells widen into the int32 result slot). Single throws
            // for an empty OR a multi-element source; SingleOrDefault answers
            // default(T) for an empty source and throws only for more than one.
            // The predicate overloads (count >= 2) have no kernel and stay LOUD.
            case "Single" when count < 2:
            {
                if (cellCode == null) return null;
                return srcFam switch
                {
                    "i8" => I("il2cpp::il2cpp_linq_single_fam_i8", 1, true, false, false,
                        OneArg, resultKind: "l", appendLiterals: new[] { cellCode }),
                    "f" or "d" => I("il2cpp::il2cpp_linq_single_fam_d", 1, true, false, false,
                        OneArg, resultKind: "d", appendLiterals: new[] { cellCode }),
                    _ => I("il2cpp::il2cpp_linq_single_fam_i4", 1, true, false, false,
                        OneArg, appendLiterals: new[] { cellCode }),
                };
            }
            case "SingleOrDefault" when count < 2:
            {
                if (cellCode == null) return null;
                return srcFam switch
                {
                    "i8" => I("il2cpp::il2cpp_linq_single_or_default_fam_i8", 1, true, false, false,
                        OneArg, resultKind: "l", appendLiterals: new[] { cellCode }),
                    "f" or "d" => I("il2cpp::il2cpp_linq_single_or_default_fam_d", 1, true, false, false,
                        OneArg, resultKind: "d", appendLiterals: new[] { cellCode }),
                    _ => I("il2cpp::il2cpp_linq_single_or_default_fam_i4", 1, true, false, false,
                        OneArg, appendLiterals: new[] { cellCode }),
                };
            }
            case "GroupBy":
            {
                // impl-groupbykey: GroupBy over an int64 / double / narrow ARRAY
                // source (b5-gaps 17.2 (3): "GroupBy<int64,DateTime>"). The
                // runtime kernel reads the element at its REAL width (srcKind
                // 5/6/7 + the narrow cell code) and buckets it through the
                // SHARED key-family dispatch, so every key carrier the int32
                // source supports is available here too.
                // floatgrp: a System.Single[] source. The bucket stores REAL
                // 4-byte float cells (kElemKindF) so ToArray / foreach see
                // net48's float[]; srcKind 8 tells the kernel to read the
                // source cell at its REAL width and widen into the selector
                // slot (the F-stack carrier takes/returns float as a double).
                if (genArgs is not { Length: >= 2 }) return null;
                int wKeyKind = KeyKindForNamed(genArgs[1], genArgNames, 1);
                if (wKeyKind < 0)
                {
                    string wElemArgType = GenericArgTypeName(genArgs[0]);
                    string wKeyArgType = genArgNames is { Length: >= 2 } && genArgNames[1] != "?"
                        ? genArgNames[1] : GenericArgTypeName(genArgs[1]);
                    throw new NotSupportedException(
                        $"Enumerable.GroupBy<{wElemArgType},{wKeyArgType}> over a " +
                        $"{elemScalar} source is not wired: the grouping surface buckets by a key " +
                        "carrier of int32-slot (int/enum/bool/char/sbyte/byte/short/ushort/uint), " +
                        "int64 (long/ulong) or 16-byte struct (System.Decimal/System.DateTime/" +
                        "System.Guid/System.TimeSpan/System.DateTimeOffset); " +
                        $"{DescribeRejectedKey(genArgs[1], wKeyArgType)} has no runtime key carrier " +
                        "(see docs/audit/b5-gaps.md)");
                }
                var wParams = LinqEncodedParamTypes(md, sigHandle);
                int wElemArg = -1, wCmpArg = -1, wResArg = -1;
                for (int pi = 2; pi < wParams.Length; pi++)
                {
                    var wdef = GenericDefNameOf(wParams[pi]);
                    if (wdef == "System.Func`2") wElemArg = pi;
                    else if (wdef == "System.Func`3") wResArg = pi;
                    else if (wdef == "System.Collections.Generic.IEqualityComparer`1") wCmpArg = pi;
                }
                byte? wElemByte = wElemArg >= 0 && genArgs.Length >= 3 ? genArgs[2] : null;
                byte? wResByte = wResArg >= 0 && genArgs.Length >= 3
                    ? (genArgs.Length >= 4 ? genArgs[3] : genArgs[2]) : null;
                string WElemFamily(byte b, int nameIndex)
                {
                    switch (b)
                    {
                        case 0x08:
                        case 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09:
                            return "0";
                        case 0x0A or 0x0B: return "2";
                        case 0x0D: return "3";
                        // floatgrp: a System.Single bucket element (4-byte cell).
                        case 0x0C: return "7";
                        case 0x0E or 0x12 or 0x1C or 0x1D or 0x15 or 0x13 or 0x1E: return "1";
                        case 0x11:
                        {
                            var n = genArgNames is { Length: > 0 } && nameIndex < genArgNames.Length
                                ? genArgNames[nameIndex] : null;
                            if (n is not null && n.StartsWith("V:", StringComparison.Ordinal)) n = n[2..];
                            return n switch
                            {
                                "System.Decimal" => "4",
                                "System.DateTime" => "5",
                                "System.Guid" => "6",
                                _ => "",
                            };
                        }
                        default: return "";
                    }
                }
                string WResultFamily(byte b) => b switch
                {
                    0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x09 => "1",
                    0x0A or 0x0B => "3",
                    0x0D => "4",
                    0x0C => "5",
                    _ => "2",
                };
                // key-only: the element IS the source element of the SOURCE
                // family (int64 -> boxed-int64 bucket, double -> boxed-double,
                // narrow -> the int32 slot the cell widens into).
                string wDefaultElem = srcFam switch { "i8" => "2", "d" => "3", "f" => "7", _ => "0" };
                string wEk = wElemByte is { } web ? WElemFamily(web, 2) : wDefaultElem;
                if (wEk.Length == 0)
                    throw new NotSupportedException(
                        $"Enumerable.GroupBy TElement '{GenericArgTypeName(wElemByte!.Value)}' " +
                        $"over a {elemScalar} source is not a supported element family");
                string wRk = wResByte is { } wrb ? WResultFamily(wrb) : "0";
                int wElemIdx = wElemArg >= 0 ? wElemArg : -1;
                int wCmpIdx = wCmpArg >= 0 ? wCmpArg : -1;
                int wResIdx = wResArg >= 0 ? wResArg : -1;
                var wArgOrder = new[] { 0, 1, wElemIdx, wCmpIdx, wResIdx };
                var wCasts = new[] { "a", "", "", "", "" };
                string wSrcKind = srcFam switch { "i8" => "5", "d" => "6", "f" => "8", _ => "7" };
                var wLiterals = narrow
                    ? new[] { wEk, wRk,
                        wKeyKind.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        wSrcKind, kindCode }
                    : new[] { wEk, wRk,
                        wKeyKind.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        wSrcKind };
                var wRetType = wRk == "0"
                    ? "System.Linq.IGroupingResult"
                    : wRk == "1" ? "System.Int32[]"
                    : wRk == "3" ? "System.Int64[]"
                    : wRk == "4" ? "System.Double[]"
                    : wRk == "5" ? "System.Double[]" : "System.Object[]";
                return I(narrow
                        ? "il2cpp::il2cpp_linq_group_by_full_key_cell"
                        : "il2cpp::il2cpp_linq_group_by_full_key",
                    count, true, true, false, wCasts,
                    returnTypeName: wRetType, appendLiterals: wLiterals, argOrder: wArgOrder);
            }
            default:
                return null;
        }
    }
    private static string LinqSelectorProjectionType(MetadataReader md, BlobHandle sigHandle)
    {
        var parts = LinqEncodedParamTypes(md, sigHandle);
        if (parts.Length < 2) return "";
        var s = parts[1];
        if (s.StartsWith("r:", StringComparison.Ordinal)) s = s[2..];
        if (!s.StartsWith("System.Func`2<", StringComparison.Ordinal)) return "";
        var inner = s["System.Func`2<".Length..];
        var gt = inner.LastIndexOf('>');
        if (gt >= 0) inner = inner[..gt];
        var args = CodeGenerator.SplitTopLevel(inner).ToArray();
        return args.Length >= 2 ? args[1] : "";
    }
}








