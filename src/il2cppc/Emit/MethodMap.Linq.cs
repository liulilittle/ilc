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
                    aggFam, srcType, TwoArg, OneArg);
                if (listAgg != null) return listAgg;
            }
            if (aggRecv is "System.Linq.IGrouping" or "System.Linq.IGroupingResult")
            {
                // p0grp: a DECIMAL-source bucket holds 16-byte System.Decimal
                // cells; the per-bucket aggregate entries are int32 kernels.
                // Reject loudly instead of reading a decimal as an int32 (and
                // emitting an ill-typed temp for the decimal-typed result).
                if (genArgs is { Length: >= 1 } && genArgs[0] == 0x11)
                {
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a System.Decimal-element GroupBy bucket is not wired: " +
                        "the per-bucket aggregate surface is the int32 kernel family " +
                        "(see docs/audit/b4-gaps.md); a decimal bucket supports Count()/Any()/" +
                        "ToArray() and foreach");
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
                return LinqFamilyAggregate(name, genArgs, receiverType, arg0Type);
            }
            if (genArgs is not { Length: >= 1 } || genArgs[0] != 0x08)
            {
                string argType = genArgs is { Length: > 0 } ? GenericArgTypeName(genArgs[0]) : "IEnumerable<non-int32>";
                throw new NotSupportedException(
                    $"Enumerable.{name}<{argType}> with a selector not wired: " +
                    "the stage-1 selector-aggregate surface is int32 (0x08) element-family only");
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
                    default:
                        throw new NotSupportedException(
                            $"Enumerable.{name} with selector projection '{proj}' not wired: " +
                            "the stage-1 selector-aggregate surface covers int32/int64/double projections " +
                            "(a decimal projection has no runtime aggregate kernel)");
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
            || (owner == "System.Linq.IGrouping`2" && name == "get_Key"))
        {
            if (name == "get_Key" && count == 0)
                // INSTANCE property accessor: ParamCount is the EXPLICIT
                // parameter count (0); the emitter prepends the receiver.
                return I("il2cpp::il2cpp_linq_grouping_get_key", 0, true, false, true);
            // p0grp: a DECIMAL-source bucket (il2cpp_linq_group_by_dec_key)
            // holds 16-byte System.Decimal cells. The per-bucket operator
            // family below is int32-kernel only (it snapshots the bucket and
            // reuses the int32 array kernels), so it would reinterpret every
            // decimal as an int32 -- a silent wrong value, and an ill-typed
            // C++ temp for a decimal-typed result. Only the family-agnostic
            // members are let through: Count()/Any() (bucket metadata),
            // ToArray() (get_elements copies by element width) and get_Key.
            bool decBucket = genArgs is { Length: >= 1 } && genArgs[0] == 0x11;
            if (decBucket)
            {
                bool agnostic = name == "ToArray"
                    || (name == "Count" && count < 2)
                    || (name == "Any" && count < 2);
                if (!agnostic)
                {
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a System.Decimal-element GroupBy bucket is not wired: " +
                        "the per-bucket operator surface is the int32 kernel family (Where/Select/" +
                        "Sum/Min/Max/Average/First/FirstOrDefault/Last/ElementAt/Contains/Count(pred)/" +
                        "Any(pred)/All); a decimal bucket only supports Count()/Any()/ToArray() and " +
                        "foreach (see docs/audit/b4-gaps.md)");
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
                srcType, TwoArg, OneArg);
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
                if (LinqSecondParamGenericArgCount(md, sigHandle) == 3)
                    throw new NotSupportedException(
                        "Enumerable.Select with a (element, index) selector over a decimal-element " +
                        "source is not wired: the runtime has no il2cpp_linq_select_idx_*_dec entry");
                if (genArgs is not { Length: >= 2 })
                    return null;
                if (genArgs[1] != 0x11 || !IsDecimalGenArg(genArgNames, 1))
                    throw new NotSupportedException(
                        $"Enumerable.Select<System.Decimal,{GenericArgTypeName(genArgs[1])}> not wired: " +
                        "the decimal-element source surface covers a System.Decimal result only " +
                        "(il2cpp_linq_select_dec_dec, linq_map.h)");
                return I("il2cpp::il2cpp_linq_select_dec_dec", 2, true, true, false, TwoArg,
                    returnTypeName: "System.Decimal[]");
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
                        "over a value-type source is not wired: the decimal-source grouping surface " +
                        "covers System.Decimal[] only (see docs/audit/b4-gaps.md)");
                int decKeyKind = KeyKindFor(genArgs[1]);
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
                if (count != 2)
                {
                    throw new NotSupportedException(
                        $"Enumerable.GroupBy over a System.Decimal[] source with {count - 2} " +
                        "additional argument(s) not wired: the decimal-source surface covers the " +
                        "key-selector-only overload (an element selector / comparer / result " +
                        "selector over a decimal source has no runtime callback ABI; " +
                        "see docs/audit/b4-gaps.md)");
                }
                return I("il2cpp::il2cpp_linq_group_by_dec_key", 2, true, true, false,
                    TwoArg, returnTypeName: "System.Linq.IGroupingResult",
                    appendLiterals: new[]
                    {
                        decKeyKind.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    });
            }
        }

        // Reference-element ARRAY source: the ref-family entries that exist.
        if (linqSrcFam == "ref" && listKind == null)
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
                    if (LinqSecondParamGenericArgCount(md, sigHandle) == 3)
                        throw new NotSupportedException(
                            "Enumerable.Where with a (element, index) predicate over a reference-element " +
                            "source is not wired: the runtime has no il2cpp_linq_where_idx_ref entry");
                    return I("il2cpp::il2cpp_linq_where_ref", 2, true, true, false, TwoArg,
                        returnTypeName: LinqRefArrayTypeName(genArgs, srcType));
                case "Select":
                {
                    if (LinqSecondParamGenericArgCount(md, sigHandle) == 3)
                        throw new NotSupportedException(
                            "Enumerable.Select with a (element, index) selector over a reference-element " +
                            "source is not wired: the runtime has no il2cpp_linq_select_idx_ref_* entry");
                    if (genArgs is not { Length: >= 2 })
                        return null;
                    // p0linq4 (D3): a DECIMAL result over a reference source --
                    // the ref -> decimal kernel (il2cpp_linq_select_ref_dec).
                    // The 0x11 byte covers every value type, so the decoded
                    // MethodSpec FULL NAME is the only authority for decimal.
                    if (genArgs[1] == 0x11 && IsDecimalGenArg(genArgNames, 1))
                        return I("il2cpp::il2cpp_linq_select_ref_dec", 2, true, true, false, TwoArg,
                            returnTypeName: "System.Decimal[]");
                    if (!IsRefFamilyByte(genArgs[1]))
                    {
                        throw new NotSupportedException(
                            $"Enumerable.Select<{GenericArgTypeName(genArgs[0])},{GenericArgTypeName(genArgs[1])}> " +
                            "not wired: the runtime reference-source select surface is ref -> ref " +
                            "(il2cpp_linq_select_ref_ref) and ref -> decimal " +
                            "(il2cpp_linq_select_ref_dec); the decimal-result entries over a VALUE source " +
                            "cover the int32/int64/double/float/decimal SOURCE families " +
                            "(il2cpp_linq_select_i4_dec/_i8_dec/_d_dec/_f_dec/_dec_dec)");
                    }
                    return I("il2cpp::il2cpp_linq_select_ref_ref", 2, true, true, false, TwoArg,
                        returnTypeName: LinqRefArrayTypeName(new[] { genArgs[1] }, null));
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
                            "source is not wired (the runtime has no index key-selector entry)");
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
                    throw new NotSupportedException(
                        $"Enumerable.GroupBy<{(genArgs is { Length: >= 1 } ? GenericArgTypeName(genArgs[0]) : "?")},...> " +
                        "over a reference-element source is not wired: the runtime grouping kernel reads " +
                        "int32 cells or 16-byte System.Decimal cells only (see docs/audit/b4-gaps.md)");
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
                TwoArg, OneArg);
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
                return LinqFamilyAggregate("Sum", genArgs, receiverType, arg0Type);
            case "Min":
                return LinqFamilyAggregate("Min", genArgs, receiverType, arg0Type);
            case "Max":
                return LinqFamilyAggregate("Max", genArgs, receiverType, arg0Type);
            case "Average":
                // Average(int32[]) -> double (resultKind "d", linq.h). net48
                // Enumerable.Average(this IEnumerable<int>) returns double.
                return LinqFamilyAggregate("Average", genArgs, receiverType, arg0Type);
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
                            // (i4/i8/d/f/dec). A non-decimal 0x11 (DateTime, a
                            // user struct) has no kernel and stays LOUD.
                        {
                            if (!IsDecimalGenArg(genArgNames, 1))
                                throw new NotSupportedException(
                                    $"Enumerable.Select<{GenericArgTypeName(genArgs![0])}," +
                                    $"{GenericArgTypeName(tRes)}> not wired: the value-type result " +
                                    "family with a kernel is System.Decimal only " +
                                    "(il2cpp_linq_select_<src>_dec)");
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
                                "ref" => throw new NotSupportedException(
                                    "Enumerable.Select over a REFERENCE-element source returning " +
                                    "System.Decimal is not wired: the runtime has no " +
                                    "il2cpp_linq_select_ref_dec entry"),
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
                // OrderBy(source, keySelector) -> il2cpp_linq_order_by_key_i4
                // (the D-LINQ-1 ABI bridge, linq_sort.h): the runtime caches
                // each element's key EXACTLY ONCE (net48 EnumerableSorter
                // semantics) and runs a stable sort on the cached keys. The
                // result is a fresh int32[] tracked for downstream operators.
                // Requires int32 keys (TKey == 0x08); a non-int32 key family
                // has no stage-1 key icall.
                if (genArgs is not { Length: >= 2 } || genArgs[1] != 0x08)
                {
                    string elemArgType = genArgs is { Length: >= 1 }
                        ? GenericArgTypeName(genArgs[0]) : "?";
                    string keyArgType = genArgs is { Length: >= 2 }
                        ? GenericArgTypeName(genArgs[1]) : "?";
                    throw new NotSupportedException(
                        $"Enumerable.OrderBy<{elemArgType},{keyArgType}> not wired: " +
                        "the stage-1 runtime key-selector surface (il2cpp_linq_order_by_key_i4) is int32 (0x08) " +
                        "elements AND int32 (0x08) keys only -- the runtime caches int32 keys for the stable sort");
                }
                return I("il2cpp::il2cpp_linq_order_by_key_i4", 2, true, true, false, TwoArg,
                    returnTypeName: "System.Int32[]");
            case "OrderByDescending":
                // OrderByDescending(source, keySelector) ->
                // il2cpp_linq_order_by_key_desc_i4 (the D-LINQ-1 DESCENDING
                // bridge): identical to OrderBy but with DESCENDING key order.
                // Requires int32 keys (TKey == 0x08).
                if (genArgs is not { Length: >= 2 } || genArgs[1] != 0x08)
                {
                    string elemArgType = genArgs is { Length: >= 1 }
                        ? GenericArgTypeName(genArgs[0]) : "?";
                    string keyArgType = genArgs is { Length: >= 2 }
                        ? GenericArgTypeName(genArgs[1]) : "?";
                    throw new NotSupportedException(
                        $"Enumerable.OrderByDescending<{elemArgType},{keyArgType}> not wired: " +
                        "the stage-1 runtime key-selector surface (il2cpp_linq_order_by_key_desc_i4) is int32 (0x08) " +
                        "elements AND int32 (0x08) keys only -- the runtime caches int32 keys for the stable sort");
                }
                return I("il2cpp::il2cpp_linq_order_by_key_desc_i4", 2, true, true, false, TwoArg,
                    returnTypeName: "System.Int32[]");
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
                int keyKind = genArgs is { Length: >= 2 } ? KeyKindFor(genArgs[1]) : -1;
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
                        "the stage-1 runtime grouping surface is int32 (0x08) elements AND an integer " +
                        "key that fits the int32 bucket (int/bool/char/sbyte/byte/short/ushort/uint); " +
                        $"{DescribeRejectedKey(genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0, keyArgType)} " +
                        "has no runtime key carrier yet (see docs/audit/b4-gaps.md)");
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
                        0x0A => "2",                                   // int64 (boxed)
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
                        0x0A => "3",
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
                    var literals = new[] { ek, rk, keyKind.ToString(System.Globalization.CultureInfo.InvariantCulture) };
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
            case "Zip":
            {
                // Zip over an int32 ARRAY source (the common case). The unified
                // runtime entry takes IL2CPP_LIST_KIND_* codes for BOTH sides and
                // decides List-vs-array from the object descriptor.
                byte ztFirst = genArgs is { Length: >= 1 } ? genArgs[0] : (byte)0x08;
                byte ztSecond = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                byte ztRes = genArgs is { Length: >= 3 } ? genArgs[2] : (byte)0x08;
                string zFirstKind = IsRefFamilyByte(ztFirst) ? "1" : LinqListKindOfElemByte(ztFirst) ?? "";
                string zSecondKind = IsRefFamilyByte(ztSecond) ? "1" : LinqListKindOfElemByte(ztSecond) ?? "";
                if (zFirstKind.Length == 0 || zSecondKind.Length == 0)
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(ztFirst)},{GenericArgTypeName(ztSecond)},...> " +
                        "not wired: an element family with no cell ABI (see docs/audit/b5-gaps.md)");
                // A reference-element source is NOT wired: the reference branch of
                // il2cpp_linq_zip_family calls the selector through the
                // Il2CppObject* -> Il2CppObject* ABI, so a VALUE TResult thunk
                // (int32 in EAX) would be stored as a GC pointer -- a wild write
                // (measured: 0xC0000005). Rejecting the source keeps that
                // combination out of the runtime entirely.
                if (zFirstKind == "1" || zSecondKind == "1")
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(ztFirst)},{GenericArgTypeName(ztSecond)}," +
                        $"{GenericArgTypeName(ztRes)}> not wired: Enumerable.Zip over a reference-element " +
                        "source is not wired (see docs/audit/b5-gaps.md)");
                string? zResKind = LinqZipResKindOfElemByte(ztRes);
                if (zResKind == null)
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(ztFirst)},{GenericArgTypeName(ztSecond)}," +
                        $"{GenericArgTypeName(ztRes)}> not wired: the zip surface writes a 4- or 8-byte " +
                        "result cell (int32/uint32/int64/double/float), so a 1/2-byte integral, " +
                        "bool/char or reference TResult has no cell ABI (see docs/audit/b5-gaps.md)");
                return I("il2cpp::il2cpp_linq_zip_family", 3, true, true, false,
                    new[] { "", "", "" }, returnTypeName: LinqArrayTypeNameExact(ztRes),
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
        string? receiverType, string? arg0Type)
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
        _ => -1,   // long/ulong/decimal/DateTime/Guid/reference: LOUD
    };

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
    //   int64  source + int32  key -> "i8_i4"
    //   int64  source + int64  key -> "i8_i8"
    private static string? ThenByKernelSuffix(byte elemByte, byte keyByte)
    {
        bool keyI4 = keyByte is 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09;
        bool keyI8 = keyByte is 0x0A or 0x0B;
        if (elemByte == 0x08) return keyI4 ? "i4" : null;
        if (elemByte is 0x0A or 0x0B) return keyI4 ? "i8_i4" : keyI8 ? "i8_i8" : null;
        return null;
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

    // The zip RESULT family code of an element type BYTE (0 int32 / 2 int64 /
    // 3 float / 4 double / 5 uint32), or null when the result cell has no ABI
    // (1/2-byte integral, bool/char, reference). A ulong result rides the int64
    // family: the cell width and the write ABI are identical (documented
    // deviation in docs/audit/b5-gaps.md section 3).
    private static string? LinqZipResKindOfElemByte(byte b) => b switch
    {
        0x08 => "0",  // int32
        0x09 => "5",  // uint32
        0x0A or 0x0B => "2",  // int64 / uint64
        0x0C => "3",  // float -> the 8-byte double carrier
        0x0D => "4",  // double
        _ => null,
    };

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
        string[] TwoArg, string[] OneArg)
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
                if (LinqSecondParamGenericArgCount(md, sigHandle) == 3)
                    throw new NotSupportedException(
                        "Enumerable.Where with a (element, index) predicate over a List source is not wired " +
                        "(the runtime has no list index-predicate entry)");
                return I("il2cpp::il2cpp_linq_list_where", 2, true, true, false,
                    L2, returnTypeName: elemTracked, appendLiterals: new[] { K });
            }
            case "Select":
            {
                if (LinqSecondParamGenericArgCount(md, sigHandle) == 3)
                    throw new NotSupportedException(
                        "Enumerable.Select with a (element, index) selector over a List source is not wired " +
                        "(the runtime has no list index-selector entry)");
                byte tRes = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                string resKind = tRes switch
                {
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "0",
                    0x0A or 0x0B => "2",
                    0x0C => "3",
                    0x0D => "4",
                    _ => "1",
                };
                return I("il2cpp::il2cpp_linq_list_select", 2, true, true, false, L2,
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
                if (K == "1")
                {
                    // Reference-element List: NOT wired. The runtime's reference
                    // branch calls the selector through the
                    // Il2CppObject*(Il2CppObject*, Il2CppObject*) ABI, but a value
                    // TResult thunk returns int32 in EAX -- stored as a GC
                    // pointer it is a wild write (measured: 0xC0000005). A
                    // reference TResult has no cell ABI either. Both shapes are
                    // rejected here, and the runtime branch was removed (see
                    // docs/audit/b5-gaps.md).
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(tFirst)},{GenericArgTypeName(tSecond)}," +
                        $"{GenericArgTypeName(tRes)}> over a reference-element List not wired: " +
                        "Enumerable.Zip over a reference-element source is not wired " +
                        "(see docs/audit/b5-gaps.md)");
                }
                string? resKind = tRes switch
                {
                    0x08 => "0",        // int32
                    0x09 => "5",        // uint32 (4-byte cell, elemKind 5)
                    0x0A or 0x0B => "2", // int64 / uint64 (8-byte cell, elemKind 2)
                    0x0C => "3",        // float -> the 8-byte double carrier
                    0x0D => "4",        // double
                    _ => null,
                };
                if (resKind == null)
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(tFirst)},{GenericArgTypeName(tSecond)}," +
                        $"{GenericArgTypeName(tRes)}> over a List not wired: the wide/narrow zip surface " +
                        "writes a 4- or 8-byte result cell (int32/uint32/int64/double/float), so a " +
                        "1/2-byte integral, bool/char or reference TResult has no cell ABI " +
                        "(see docs/audit/b5-gaps.md)");
                string? secondKind = LinqListKindOfElemByte(tSecond);
                if (secondKind == null)
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(tFirst)},{GenericArgTypeName(tSecond)},...> " +
                        "over a List not wired: the second source's element family has no value-cell ABI " +
                        "(a reference-element second source cannot be paired with a value-family List)");
                return I("il2cpp::il2cpp_linq_zip_family", 3, true, true, false,
                    new[] { "", "", "" }, returnTypeName: LinqArrayTypeNameExact(tRes),
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
                if (K != "1")
                    throw new NotSupportedException(
                        $"Enumerable.{name} over a List of the element family kind {K} is not wired " +
                        "(the list key-selector surface covers int32 and reference elements)");
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
                        "(the list selector-aggregate surface covers int32/int64/double/float projections)");
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
        int count, byte[]? genArgs, string kindCode, string srcFam, string[] TwoArg, string[] OneArg)
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

        switch (name)
        {
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
                        "is not wired (the runtime has no index key-selector entry)");
                byte keyByte = genArgs is { Length: >= 2 } ? genArgs[1] : (byte)0x08;
                string? keyFam = keyByte switch
                {
                    0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 => "i4",
                    0x0A or 0x0B => "i8",
                    _ => null,
                };
                if (keyFam == null)
                {
                    string elemName = genArgs is { Length: >= 1 } ? GenericArgTypeName(genArgs[0]) : "?";
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{elemName},{GenericArgTypeName(keyByte)}> " +
                        "not wired: the int64-source key-selector surface covers int32 and int64 keys only " +
                        "(a float/double/reference key has no int64-source kernel)");
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
                string firstKind = IsRefFamilyByte(tFirst) ? "1" : LinqListKindOfElemByte(tFirst) ?? "";
                string secondKind = IsRefFamilyByte(tSecond) ? "1" : LinqListKindOfElemByte(tSecond) ?? "";
                if (firstKind.Length == 0 || secondKind.Length == 0)
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(tFirst)},{GenericArgTypeName(tSecond)},...> " +
                        "not wired: an element family with no cell ABI (see docs/audit/b5-gaps.md)");
                // A reference-element source is NOT wired (see the int32 arm
                // above): the reference branch would call a VALUE-result thunk
                // through the Il2CppObject* ABI and store EAX as a GC pointer.
                if (firstKind == "1" || secondKind == "1")
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(tFirst)},{GenericArgTypeName(tSecond)}," +
                        $"{GenericArgTypeName(tRes)}> not wired: Enumerable.Zip over a reference-element " +
                        "source is not wired (see docs/audit/b5-gaps.md)");
                string? resKind = LinqZipResKindOfElemByte(tRes);
                if (resKind == null)
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{GenericArgTypeName(tFirst)},{GenericArgTypeName(tSecond)}," +
                        $"{GenericArgTypeName(tRes)}> not wired: the zip surface writes a 4- or 8-byte " +
                        "result cell (int32/uint32/int64/double/float), so a 1/2-byte integral, " +
                        "bool/char or value-result-over-reference-source has no cell ABI " +
                        "(see docs/audit/b5-gaps.md)");
                return I("il2cpp::il2cpp_linq_zip_family", 3, true, true, false,
                    new[] { "", "", "" }, returnTypeName: LinqArrayTypeNameExact(tRes),
                    appendLiterals: new[] { firstKind, secondKind, resKind });
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
