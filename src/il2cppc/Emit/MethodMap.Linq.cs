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
    private static ResolvedMethod? TryMapLinq(MetadataReader md, BlobHandle sigHandle, string owner, string name,
        byte[] ps, int count, byte[]? genArgs, string[]? genArgNames, string? receiverType, bool receiverIsAddress,
        string? arg0Type, string? arg1Type)
    {
        // [0] the source array, [1] the predicate/selector delegate.
        string[] TwoArg = { "a", "" };
        string[] OneArg = { "a" };

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
            if (LinqListKind(srcType, genArgs, aggFam) is { } aggListKind)
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
                if (genArgs is { Length: >= 1 } && genArgs[0] == 0x11)
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
                    }
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a System.Decimal-element GroupBy bucket is not wired: " +
                        "the decimal bucket surface covers Sum()/Min()/Max()/Average() and their decimal " +
                        "selector forms (the decimal kernels); the int32 predicate/selector operators have " +
                        "no decimal bucket kernel (see docs/audit/b5-gaps.md)");
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
                string suffix = refProj switch
                {
                    "i" or "c" => "i4",
                    "l" => "i8",
                    "d" => "d",
                    "f" => "f",
                    _ => "",
                };
                if (suffix.Length == 0)
                {
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])},...> with a selector is not wired: " +
                        "the reference selector-aggregate surface covers an int32-slot (char/int32), int64, " +
                        "double or float projection only");
                }
                string entry = name switch
                {
                    "Sum" => $"il2cpp::il2cpp_linq_sum_sel_ref_{suffix}",
                    "Min" => $"il2cpp::il2cpp_linq_min_sel_ref_{suffix}",
                    "Max" => $"il2cpp::il2cpp_linq_max_sel_ref_{suffix}",
                    _ => $"il2cpp::il2cpp_linq_average_sel_ref_{suffix}",
                };
                string? rk = name == "Average" ? "d"
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
                // The WIDE source families (long / float / double): the
                // selector's RESULT slot picks the kernel, exactly like the
                // reference family. net48 has every overload, so none stays
                // LOUD (the stage-1 surface was int32-only here).
                string? srcSuffix = genArgs[0] switch
                {
                    0x0A or 0x0B => "i8src",
                    0x0C => "fsrc",
                    0x0D => "dsrc",
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
                        "the wide-source selector-aggregate surface covers long/float/double elements " +
                        "with an int32/int64/double/float projection only");
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
            if (name == "get_Key" && count == 0)
            {
                // INSTANCE property accessor: ParamCount is the EXPLICIT
                // parameter count (0); the emitter prepends the receiver.
                // BATCH4: dispatch on the tracked grouping type's key-family
                // marker ("System.Linq.IGrouping_key_i8/_dec/_dt/_guid").
                var keyMarker = srcType != null && srcType.StartsWith("System.Linq.IGrouping_key_", StringComparison.Ordinal)
                    ? srcType["System.Linq.IGrouping".Length..] : "";
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
            bool decBucket = genArgs is { Length: >= 1 } && genArgs[0] == 0x11;
            if (decBucket)
            {
                bool agnostic = name == "ToArray"
                    || (name == "Count" && count < 2)
                    || (name == "Any" && count < 2)
                    || (name == "Contains" && count >= 2);
                if (!agnostic)
                {
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a System.Decimal-element GroupBy bucket is not wired: " +
                        "the decimal bucket surface covers the aggregate family (Sum/Min/Max/Average) " +
                        "plus Count()/Any()/Contains()/ToArray()/get_Key; the int32 predicate/selector " +
                        "operators have no decimal bucket kernel (see docs/audit/b5-gaps.md)");
                }
            }
            switch (name)
            {
                case "Count" when count < 2:
                    return I("il2cpp::il2cpp_linq_grouping_get_count", 1, true, false, false, new[] { "" });
                case "Count":
                    return I("il2cpp::il2cpp_linq_grouping_count_pred", 2, true, false, false, new[] { "", "" });
                case "Where":
                    return I("il2cpp::il2cpp_linq_grouping_where", 2, true, true, false, new[] { "", "" },
                        returnTypeName: "System.Int32[]");
                case "Select":
                    return I("il2cpp::il2cpp_linq_grouping_select", 2, true, true, false, new[] { "", "" },
                        returnTypeName: "System.Int32[]");
                case "ToArray":
                    // The exact-size snapshot IS the get_elements contract
                    // (the bucket's backing store is growable), so no separate
                    // wrapper icall is needed.
                    return I("il2cpp::il2cpp_linq_grouping_get_elements", 1, true, true, false, new[] { "" },
                        returnTypeName: "System.Int32[]");
                case "Any" when count < 2:
                    return I("il2cpp::il2cpp_linq_grouping_any", 1, true, false, false, new[] { "" });
                case "Any":
                    return I("il2cpp::il2cpp_linq_grouping_any_pred", 2, true, false, false, new[] { "", "" });
                case "All":
                    return I("il2cpp::il2cpp_linq_grouping_all", 2, true, false, false, new[] { "", "" });
                case "First" when count < 2:
                    return I("il2cpp::il2cpp_linq_grouping_first_i4", 1, true, false, false, new[] { "" });
                case "FirstOrDefault" when count < 2:
                    return I("il2cpp::il2cpp_linq_grouping_first_or_default_i4", 1, true, false, false, new[] { "" });
                case "Last" when count < 2:
                    return I("il2cpp::il2cpp_linq_grouping_last_i4", 1, true, false, false, new[] { "" });
                case "ElementAt":
                    return I("il2cpp::il2cpp_linq_grouping_element_at_i4", 2, true, false, false,
                        new[] { "", "i" });
                case "Contains":
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
        var listKind = LinqListKind(srcType, genArgs, linqSrcFam);
        if (listKind is { } lk)
        {
            var listDispatch = TryMapLinqList(md, sigHandle, name, count, genArgs, lk, linqSrcFam,
                srcType, TwoArg, OneArg, genArgNames);
            if (listDispatch != null) return listDispatch;
        }

        // P0LINQ1 (D3): a DECIMAL-element ARRAY source. LinqSourceFamily folds
        // 0x11 into "ref" (every value type shares that byte), so this block
        // must run BEFORE the reference-source block or a decimal[] source
        // would be dispatched into the ref kernels (which require isRefElem).
        // The implemented operator is Select with a decimal result; the
        // source-only aggregates are served earlier by LinqFamilyAggregate.
        if (listKind == null && IsLinqDecimalSource(genArgs, receiverType, arg0Type)
            && !IsLinqStringSource(genArgs, receiverType, arg0Type))
        {
            if (name == "Select")
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
                    throw new NotSupportedException(
                        $"Enumerable.Select<System.Decimal,{GenericArgTypeName(genArgs[1])}> " +
                        (selIdx ? "with an (element, index) selector " : "") +
                        "not wired: the decimal-source select surface covers a System.Decimal result " +
                        "and the int32/int64/double/float VALUE results " +
                        "(il2cpp_linq_select[_idx]_dec_dec/_i4/_i8/_d/_f)");
                return I($"il2cpp::il2cpp_linq_select{(selIdx ? "_idx" : "")}_dec_{vs}",
                    2, true, true, false, TwoArg, returnTypeName: LinqValueResultArrayName(vs));
            }
            // p0grp: Where over a DECIMAL source (net48 Where<decimal>(source,
            // Func<decimal,bool>)). The predicate takes the decimal BY VALUE; the
            // result is a fresh decimal[].
            if (name == "Where")
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
            if (name == "First" && count < 2)
                return I("il2cpp::il2cpp_linq_first_dec", 1, true, false, false, OneArg, resultKind: "dec");
            if (name == "Last" && count < 2)
                return I("il2cpp::il2cpp_linq_last_dec", 1, true, false, false, OneArg, resultKind: "dec");
            if (name == "ElementAt")
                return I("il2cpp::il2cpp_linq_element_at_dec", 2, true, false, false,
                    new[] { "a", "i" }, resultKind: "dec");
            if (name == "Contains")
                return I("il2cpp::il2cpp_linq_contains_dec", 2, true, false, false,
                    new[] { "a", "" });
            if (name == "Reverse")
                return I("il2cpp::il2cpp_linq_reverse_dec", 1, true, true, false, OneArg,
                    returnTypeName: "System.Decimal[]");
            if (name == "Take")
                return I("il2cpp::il2cpp_linq_take_dec", 2, true, true, false,
                    new[] { "a", "i" }, returnTypeName: "System.Decimal[]");
            if (name == "Skip")
                return I("il2cpp::il2cpp_linq_skip_dec", 2, true, true, false,
                    new[] { "a", "i" }, returnTypeName: "System.Decimal[]");
            // p0grp: OrderBy / OrderByDescending over a DECIMAL source (net48
            // OrderBy<decimal,TKey>). The int32-key family rides the decimal
            // source sort; the result is a fresh decimal[].
            if (name == "OrderBy" || name == "OrderByDescending")
            {
                bool desc = name == "OrderByDescending";
                byte keyByte = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                string? decKeyFam = keyByte switch
                {
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i4",
                    _ => null,
                };
                if (decKeyFam == null)
                    throw new NotSupportedException(
                        $"Enumerable.{name}<System.Decimal,{GenericArgTypeName(keyByte)}> not wired: the " +
                        "decimal-source keyed sort covers int32/int64/double/decimal keys only " +
                        "(see docs/audit/b5-gaps.md)");
                return I(desc
                        ? "il2cpp::il2cpp_linq_order_by_key_desc_dec_i4"
                        : "il2cpp::il2cpp_linq_order_by_key_dec_i4",
                    2, true, true, false, TwoArg, returnTypeName: "System.Decimal[]");
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
            && !IsLinqValueTypeSource(genArgs, receiverType, arg0Type))
        {
            switch (name)
            {
                case "Any" when count < 2:
                    // il2cpp_linq_any reads only the array header.
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
                case "FirstOrDefault" when count < 2:
                    return I("il2cpp::il2cpp_linq_first_or_default_ref", 1, true, true, false, OneArg,
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
                            "source is not wired: LOUD whitelist 鈶?-- net48 has no (element, index) key " +
                            "selector overload. MS Learn Enumerable.OrderBy (view=netframework-4.8.1) and the " +
                            "net48 Reference Source System.Core/System/Linq/Enumerable.cs list only " +
                            "OrderBy<TSource,TKey>(IEnumerable<TSource>, Func<TSource,TKey>) and " +
                            "OrderBy<TSource,TKey>(..., IComparer<TKey>); the (element, index) shape is a " +
                            ".NET 9 Index() family form (see docs/audit/b5-gaps.md)");
                    string keySuffix = LinqKeyFamilySuffix(genArgs[1]);
                    if (genArgs[1] == 0x0E)
                        throw new NotSupportedException(
                            $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])},System.String> not wired: " +
                            "string key ordering requires culture-aware collation, which the runtime does not " +
                            "implement (see docs/audit/b5-gaps.md); ordinal would silently misorder mixed-case " +
                            "input (net48 Comparer<string>.Default sorts a before A)");
                    if (keySuffix.Length == 0)
                        throw new NotSupportedException(
                            $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])},{GenericArgTypeName(genArgs[1])}> " +
                            "not wired: the reference-source key-selector surface covers int32/int64/" +
                            "double/float and reference keys only");
                    // The desc flag is an APPENDED literal (0/1), never a
                    // consumed IL argument -- ParamCount stays the IL arity.
                    return I($"il2cpp::il2cpp_linq_order_by_key_ref_{keySuffix}", 2, true, true, false,
                        TwoArg,
                        returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { name == "OrderBy" ? "0" : "1" });
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
                case "Distinct":
                    return I("il2cpp::il2cpp_linq_distinct_family", 1, true, true, false,
                        OneArg, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "Except":
                    return I("il2cpp::il2cpp_linq_except_family", 2, true, true, false,
                        new[] { "a", "a" }, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "Union":
                    return I("il2cpp::il2cpp_linq_union_family", 2, true, true, false,
                        new[] { "a", "a" }, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "Intersect":
                    return I("il2cpp::il2cpp_linq_intersect_family", 2, true, true, false,
                        new[] { "a", "a" }, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "DefaultIfEmpty":
                    return I("il2cpp::il2cpp_linq_default_if_empty_family", 1, true, true, false,
                        OneArg, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                        appendLiterals: new[] { "15" });
                case "LongCount" when count < 2:
                    return I("il2cpp::il2cpp_linq_long_count_family", 1, true, false, false,
                        OneArg, resultKind: "l", appendLiterals: new[] { "15" });
                case "SequenceEqual":
                    return I("il2cpp::il2cpp_linq_sequence_equal_family", 2, true, false, false,
                        new[] { "a", "a" }, appendLiterals: new[] { "15" });
                case "Contains":
                    return I("il2cpp::il2cpp_linq_contains_family_ref", 2, true, false, false,
                        new[] { "a", "" }, appendLiterals: new[] { "15" });
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
                        "(the wired reference-source entries are Any/Count/Where/Select ref->ref/First/FirstOrDefault)"),
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
                return count >= 2
                    ? I("il2cpp::il2cpp_linq_count_i4", 2, true, false, false, TwoArg)
                    : I("il2cpp::il2cpp_linq_count", 1, true, false, false, OneArg);
            case "Any":
                // Any() / Any(predicate) -- bool result (int32 slot).
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
                            if (resName is "System.DateTime" or "System.TimeSpan" or "System.Guid")
                            {
                                string suffix = resName == "System.DateTime" ? "dt"
                                    : resName == "System.TimeSpan" ? "ts" : "guid";
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
                {
                    var toArrType = genArgs is { Length: >= 1 }
                        ? LinqArrayTypeName(genArgs[0])
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
                return LinqOrderByKeyEntry(genArgs, desc: false);
            case "OrderByDescending":
                // OrderByDescending(source, keySelector) ->
                // il2cpp_linq_order_by_key[_desc]_* (the DESCENDING bridge):
                // identical to OrderBy but with DESCENDING key order. The same
                // key families as OrderBy.
                return LinqOrderByKeyEntry(genArgs, desc: true);
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
            case "Distinct":
                // Distinct(source) -> il2cpp_linq_distinct_i4 (D-LINQZ-1):
                // remove duplicates keeping FIRST-APPEARANCE order (net48
                // default ordinal int32 equality); the result is a fresh
                // int32[] tracked for downstream operators.
                return I("il2cpp::il2cpp_linq_distinct_i4", 1, true, true, false,
                    OneArg, returnTypeName: "System.Int32[]");
            case "Except":
                // Except(source, second) -> il2cpp_linq_except_i4 (D-LINQZ-1):
                // the elements of source NOT present in second (set
                // membership -- duplicates in second are irrelevant),
                // first-appearance order, DEDUPLICATED output (net48).
                return I("il2cpp::il2cpp_linq_except_i4", 2, true, true, false,
                    new[] { "a", "a" }, returnTypeName: "System.Int32[]");
            case "Union":
                // Union(source, second) -> il2cpp_linq_union_family (D-LINQZ-2):
                // first's elements then second's, deduplicated by
                // first-appearance (net48 Union = distinct concat). The int32
                // cell code (IL2CPP_LINQ_CELL_I4 = 7) is an APPENDED literal;
                // both array slots ride the "a" cast.
                return I("il2cpp::il2cpp_linq_union_family", 2, true, true, false,
                    new[] { "a", "a" }, returnTypeName: "System.Int32[]",
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
            case "Contains":
                // Contains(source, value) -> il2cpp_linq_contains_i4: ordinal
                // int32 equality (EqualityComparer<int>.Default).
                return I("il2cpp::il2cpp_linq_contains_i4", 2, true, false, false,
                    new[] { "a", "i" });
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
                        return acc switch
                        {
                            "i4" => I("il2cpp::il2cpp_linq_aggregate_seed_i4", 3, true, false, false,
                                new[] { "a", "i", "" }),
                            "i8" => srcFam == "i8"
                                ? I("il2cpp::il2cpp_linq_aggregate_seed_i8_i8", 3, true, false, false,
                                    new[] { "a", "l", "" }, resultKind: "l", returnTypeName: i8Track)
                                : I("il2cpp::il2cpp_linq_aggregate_seed_i8", 3, true, false, false,
                                    new[] { "a", "l", "" }, resultKind: "l", returnTypeName: i8Track),
                            "f" => srcFam is "f" or "d"
                                ? I("il2cpp::il2cpp_linq_aggregate_seed_f_d", 3, true, false, false,
                                    new[] { "a", "d", "" }, resultKind: "d")
                                : I("il2cpp::il2cpp_linq_aggregate_seed_f", 3, true, false, false,
                                    new[] { "a", "d", "" }, resultKind: "d"),
                            "d" => srcFam is "f" or "d"
                                ? I("il2cpp::il2cpp_linq_aggregate_seed_d_d", 3, true, false, false,
                                    new[] { "a", "d", "" }, resultKind: "d")
                                : I("il2cpp::il2cpp_linq_aggregate_seed_d", 3, true, false, false,
                                    new[] { "a", "d", "" }, resultKind: "d"),
                            // nb9 H-B6: the ref accumulator reads the SOURCE
                            // element by family (ref pointer, int64, double,
                            // float widened, or int32).
                            _ => I("il2cpp::il2cpp_linq_aggregate_seed_ref" + AggRefSrcSuffix(srcFam),
                                3, true, true, false, new[] { "a", "", "" }),
                        };
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
                    string cppBase = acc switch
                    {
                        "i4" => "il2cpp::il2cpp_linq_aggregate_seed",
                        "i8" => srcFam == "i8" ? "il2cpp::il2cpp_linq_aggregate_seed_i8_i8" : "il2cpp::il2cpp_linq_aggregate_seed_i8",
                        "f" => "il2cpp::il2cpp_linq_aggregate_seed_f",
                        "d" => srcFam == "d" ? "il2cpp::il2cpp_linq_aggregate_seed_d_d" : "il2cpp::il2cpp_linq_aggregate_seed_d",
                        _ => "il2cpp::il2cpp_linq_aggregate_seed_ref" + AggRefSrcSuffix(srcFam),
                    };
                    return I(cppBase + resSuffix, 4, true, resRef, false,
                        new[] { "a", seedCast, "", "" },
                        resultKind: resRef ? null
                            : (resByte is 0x0A or 0x0B ? "l"
                               : resByte is 0x0C or 0x0D ? "d" : null),
                        returnTypeName: resByte == 0x0B ? "System.Int64" : null);
                }
                return null;
            case "LongCount":
                // LongCount(source) -> il2cpp_linq_long_count: the number of
                // elements as int64 (net48 Enumerable.LongCount returns long).
                return I("il2cpp::il2cpp_linq_long_count", 1, true, false, false, OneArg, resultKind: "l");
            case "SequenceEqual":
                // SequenceEqual(source, second) -> il2cpp_linq_sequence_equal_i4:
                // true iff same length and every pair equal (net48 ordinal).
                return I("il2cpp::il2cpp_linq_sequence_equal_i4", 2, true, false, false,
                    new[] { "a", "a" });
            case "DefaultIfEmpty":
                // DefaultIfEmpty(source) -> il2cpp_linq_default_if_empty_i4:
                // non-empty -> source as-is; empty -> new int32[]{0}.
                return I("il2cpp::il2cpp_linq_default_if_empty_i4", 1, true, true, false,
                    OneArg, returnTypeName: "System.Int32[]");
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
            || name.EndsWith("System.Guid[]", StringComparison.Ordinal));
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
            // Sum/Average do not even EXIST for string in net48 (no overload);
            // Min/Max do and are culture-aware. Both are unwireable here.
            throw new NotSupportedException(
                $"Enumerable.{name}<System.String> not wired: string Min/Max uses " +
                "Comparer<string>.Default, which is culture-aware collation (net48 sorts " +
                "\"a\" before \"B\"); ordinal would silently misorder mixed-case input and " +
                "this runtime has no collation infrastructure (see docs/audit/b5-gaps.md)");
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
            // VALUETYPE arg with no resolvable TypeRef) keeps the historical
            // decimal-kernel answer (MethodMapTests.Linq decimal-aggregate
            // guard); a real assembly always resolves the name.
            //
            // A NULL name table means genArgs came from
            // LinqAggregateElementByte, i.e. a NON-generic (header 0x00)
            // MemberRef. On this concrete surface the only value-type element
            // overload is System.Decimal (int/long/float/double have dedicated
            // non-generic overloads with their own bytes, and Min/Max<object>
            // is 0x0E reference), so a null table with a 0x11 byte IS decimal.
            // A MethodSpec always carries a name for a 0x11 arg (at least "?"),
            // so the null table can never hide a DateTime/TimeSpan/Guid source
            // and the LOUD guard above stays intact.
            bool isDecimal = genArgs is { Length: >= 1 }
                ? genArgNames is null
                    || IsDecimalGenArg(genArgNames, 0)
                    || (genArgNames is { Length: >= 1 } && genArgNames[0] == "?")
                : (receiverType ?? arg0Type) is { } n
                    && n.EndsWith("System.Decimal[]", StringComparison.Ordinal);
            if (!isDecimal)
            {
                throw new NotSupportedException(
                    $"Enumerable.{name} over a value-type source is not wired: the source-only " +
                    "aggregate surface covers int32/int64/float/double/decimal only " +
                    "(see docs/audit/b4-gaps.md)");
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
        }
        // p0grp: a REFERENCE key (System.String / class / object) rides the
        // bucket's keyRef slot (keyKind 11).
        if (IsRefFamilyByte(keyByte)) return 11;
        return KeyKindFor(keyByte);
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
    // the i4 entry, float/double ride the d entry (the F-stack carrier).
    private static string LinqKeyFamilySuffix(byte b) => b switch
    {
        0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i4",
        0x0A or 0x0B => "i8",
        0x0C or 0x0D => "d",
        0x0E or 0x12 or 0x13 or 0x15 or 0x1C or 0x1D or 0x1E => "ref",
        _ => "",
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
    // string is culture-aware -- or a value-type key) stays LOUD.
    //--------------------------------------------------------------------------
    private static ResolvedMethod LinqOrderByKeyEntry(byte[]? genArgs, bool desc)
    {
        string member = desc ? "OrderByDescending" : "OrderBy";
        if (genArgs is not { Length: >= 2 })
        {
            string elemArgType = genArgs is { Length: >= 1 } ? GenericArgTypeName(genArgs[0]) : "?";
            throw new NotSupportedException(
                $"Enumerable.{member}<{elemArgType},?> not wired: the runtime int32-source " +
                "key-selector surface covers int32/int64/double/float keys only " +
                "(il2cpp_linq_order_by_key[_desc]_i4 / _i4_i8 / _i4_d)");
        }
        string entry = genArgs[1] switch
        {
            0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09
                => desc ? "il2cpp::il2cpp_linq_order_by_key_desc_i4"
                         : "il2cpp::il2cpp_linq_order_by_key_i4",
            0x0A or 0x0B
                => desc ? "il2cpp::il2cpp_linq_order_by_key_desc_i4_i8"
                         : "il2cpp::il2cpp_linq_order_by_key_i4_i8",
            0x0C or 0x0D
                => desc ? "il2cpp::il2cpp_linq_order_by_key_desc_i4_d"
                         : "il2cpp::il2cpp_linq_order_by_key_i4_d",
            _ => "",
        };
        if (entry.Length == 0)
        {
            throw new NotSupportedException(
                $"Enumerable.{member}<{GenericArgTypeName(genArgs[0])},{GenericArgTypeName(genArgs[1])}> " +
                "not wired: the runtime int32-source key-selector surface covers int32/int64/" +
                "double/float keys only (il2cpp_linq_order_by_key[_desc]_i4 / _i4_i8 / _i4_d)");
        }
        return I(entry, 2, true, true, false, new[] { "a", "" }, returnTypeName: "System.Int32[]");
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
            "System.DateTime" or "System.TimeSpan" => "14",
            "System.Guid" => "15",
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

    // The List<T> element-kind literal, name-resolved for the 0x11 value types:
    // only System.Decimal has a Zip cell ABI (IL2CPP_LIST_KIND_DEC = 12);
    // DateTime/TimeSpan/Guid stay LOUD (the Zip element surface covers the
    // decimal value type only).
    private static string? LinqListKindOfElemByte(byte b, string[]? genArgNames, int index)
    {
        if (b != 0x11 || genArgNames is not { Length: > 0 } || index >= genArgNames.Length)
            return LinqListKindOfElemByte(b);
        var n = genArgNames[index];
        if (n.StartsWith("V:", StringComparison.Ordinal)) n = n[2..];
        return n == "System.Decimal" ? "12" : null;
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

    // The List<T> element-kind literal (linq_list.h IL2CPP_LIST_KIND_*), or
    // null when the receiver is not a List<T>. The tracked List receiver names
    // come from the newobj AOT expansion (List`1_int / List`1_string / ...) or
    // from the ToList ReturnTypeName ("System.Collections.Generic.List`1").
    private static string? LinqListKind(string? receiverType, byte[]? genArgs, string srcFam)
    {
        if (receiverType == null) return null;
        if (!receiverType.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal))
            return null;
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
            if (srcFam == "ref") return "1";
            if (LinqListKindOfElemByte(genArgs[0]) is { } eleKind) return eleKind;
        }
        var suffix = receiverType.Length > "System.Collections.Generic.List`1".Length
            ? receiverType["System.Collections.Generic.List`1".Length..]
            : "";
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
            "_string" or "_class" or "_object" or "_array" => "1",
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
                : LinqListKindOfElemByte(genArgs[0]) ?? "1",
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
        // UNCAST ("" -- never the "a" Il2CppArray* cast). Casting it would
        // reinterpret the items pointer's low 32 bits as an array length.
        var L1 = new[] { "" };
        var L2 = new[] { "", "" };
        var L3 = new[] { "", "", "" };
        var elemTracked = genArgs is { Length: >= 1 } ? LinqArrayTypeNameExact(genArgs[0])
            : (srcFam == "ref" ? "System.Object[]" : "System.Int32[]");
        var elemScalar = genArgs is { Length: >= 1 } ? LinqArrayTypeNameExact(genArgs[0]).TrimEnd('[', ']')
            : (srcFam == "ref" ? "System.Object" : "System.Int32");

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
                string resKind = tRes switch
                {
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "0",
                    0x0A or 0x0B => "2",
                    0x0C => "3",
                    0x0D => "4",
                    _ => "1",
                };
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
                return I("il2cpp::il2cpp_linq_list_to_list", 1, true, true, false,
                    L1, returnTypeName: "System.Collections.Generic.List`1", appendLiterals: new[] { K });
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
            case "DefaultIfEmpty":
                return I("il2cpp::il2cpp_linq_list_default_if_empty", 1, true, true, false,
                    L1, returnTypeName: elemTracked, appendLiterals: new[] { K });
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
                if (K == "4")
                {
                    // A DOUBLE-element list: the snapshot is a double array and
                    // the key crosses in the F-stack slot
                    // (il2cpp_linq_list_order_by_key[_desc]_d). A non-double key
                    // over a double list has no list kernel.
                    if (genArgs is not { Length: >= 2 })
                        return null;
                    if (genArgs[1] is not (0x0C or 0x0D))
                        throw new NotSupportedException(
                            $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])},{GenericArgTypeName(genArgs[1])}> " +
                            "over a List<double> not wired: the list double key-selector surface covers a " +
                            "double/float key only (il2cpp_linq_list_order_by_key[_desc]_d)");
                    return I(desc
                            ? "il2cpp::il2cpp_linq_list_order_by_key_desc_d"
                            : "il2cpp::il2cpp_linq_list_order_by_key_d",
                        2, true, true, false, L2,
                        returnTypeName: "System.Double[]", appendLiterals: new[] { K });
                }
                if (K != "1")
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a List of the element family kind {K} is not wired " +
                        "(the list key-selector surface covers int32, double and reference elements)");
                if (genArgs is not { Length: >= 2 })
                    return null;
                if (genArgs[1] == 0x0E)
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])},System.String> over a List not wired: " +
                        "string key ordering requires culture-aware collation, which the runtime does not " +
                        "implement (see docs/audit/b5-gaps.md); ordinal would silently misorder mixed-case " +
                        "input (net48 Comparer<string>.Default sorts a before A)");
                string keySuffix = LinqKeyFamilySuffix(genArgs[1]);
                if (keySuffix.Length == 0)
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{GenericArgTypeName(genArgs[0])},{GenericArgTypeName(genArgs[1])}> " +
                        "not wired: the reference-source key-selector surface covers int32/int64/" +
                        "double/float and reference keys only");
                return I($"il2cpp::il2cpp_linq_list_order_by_key_ref_{keySuffix}", 2, true, true, false,
                    L2, returnTypeName: LinqRefArrayTypeName(genArgs, srcType),
                    appendLiterals: new[] { desc ? "1" : "0", K });
            }
            case "Sum" or "Min" or "Max" or "Average":
            {
                string shortName = name.ToLowerInvariant();
                if (count < 2)
                {
                    // Source-only: the result family follows the list kind.
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
                    string suffix = K switch { "2" => "i8", "3" or "4" => "d", _ => "i4" };
                    return I($"il2cpp::il2cpp_linq_list_aggregate_{suffix}", 2, true, false, false,
                        L2, resultKind: suffix == "i8" ? "l" : suffix == "d" ? "d" : null,
                        appendLiterals: new[] { K });
                }
                if (count >= 3)
                {
                    byte accByte = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                    string suffix = accByte switch
                    {
                        0x0A or 0x0B => "i8",
                        0x0C or 0x0D => "d",
                        _ => "i4",
                    };
                    string seedCast = suffix == "i8" ? "l" : suffix == "d" ? "d" : "i";
                    return I($"il2cpp::il2cpp_linq_list_aggregate_seed_{suffix}", 3, true, false, false,
                        new[] { "", seedCast, "" }, resultKind: suffix == "i8" ? "l" : suffix == "d" ? "d" : null,
                        appendLiterals: new[] { K });
                }
                return null;
            case "GroupBy":
                return I("il2cpp::il2cpp_linq_list_group_by_full", count, true, true, false,
                    new[] { "", "", "", "", "" },
                    returnTypeName: "System.Linq.IGroupingResult");
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
            case "Distinct":
                // Distinct() -> duplicates removed keeping FIRST-APPEARANCE
                // order (net48 default equality).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_distinct_family", 1, true, true, false,
                    OneArg, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "Except":
                // Except(source, second) -> the elements of source NOT in
                // second, first-appearance order, DEDUPLICATED output (net48).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_except_family", 2, true, true, false,
                    new[] { "a", "a" }, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "Union":
                // Union(source, second) -> first's elements then second's,
                // deduplicated by first-appearance (net48 Union = distinct
                // concat).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_union_family", 2, true, true, false,
                    new[] { "a", "a" }, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "Intersect":
                // Intersect(source, second) -> the elements of source that
                // are also in second, first-appearance order, DEDUPLICATED
                // (net48).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_intersect_family", 2, true, true, false,
                    new[] { "a", "a" }, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "DefaultIfEmpty":
                // DefaultIfEmpty() -> non-empty source as-is; an empty source
                // yields a single default(T) element (net48).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_default_if_empty_family", 1, true, true, false,
                    OneArg, returnTypeName: elemTracked,
                    appendLiterals: new[] { cellCode });
            case "LongCount" when count < 2:
                // LongCount() -> the element count as int64 (net48).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_long_count_family", 1, true, false, false,
                    OneArg, resultKind: "l", appendLiterals: new[] { cellCode });
            case "SequenceEqual":
                // SequenceEqual(source, second) -> true iff same length and
                // every pair equal (net48 default equality).
                if (cellCode == null) return null;
                return I("il2cpp::il2cpp_linq_sequence_equal_family", 2, true, false, false,
                    new[] { "a", "a" }, appendLiterals: new[] { cellCode });
            case "Contains":
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
                        "is not wired: LOUD whitelist 鈶?-- net48 has no (element, index) key selector overload. " +
                        "MS Learn Enumerable.OrderBy (view=netframework-4.8.1) and the net48 Reference Source " +
                        "System.Core/System/Linq/Enumerable.cs list only OrderBy<TSource,TKey>(IEnumerable<TSource>, " +
                        "Func<TSource,TKey>) and OrderBy<TSource,TKey>(..., IComparer<TKey>); the (element, index) " +
                        "shape is a .NET 9 Index() family form (see docs/audit/b5-gaps.md)");
                byte keyByte = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                string? keyFam = keyByte switch
                {
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i4",
                    0x0A or 0x0B => "i8",
                    0x0C or 0x0D => "d",
                    _ => null,
                };
                if (keyFam == null)
                {
                    string elemName = genArgs is { Length: >= 1 } ? GenericArgTypeName(genArgs[0]) : "?";
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{elemName},{GenericArgTypeName(keyByte)}> " +
                        "not wired: the int64-source key-selector surface covers int32, int64 and " +
                        "double/float keys only (a reference key has no int64-source kernel)");
                }
                return I($"il2cpp::il2cpp_linq_order_by_key{(name == "OrderBy" ? "" : "_desc")}_i8_{keyFam}",
                    2, true, true, false, TwoArg, returnTypeName: "System.Int64[]");
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
