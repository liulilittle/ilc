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
//   with predicate/selector icalls in the stage-1 runtime (linq.h / linq_map.h
//   define the ref-family entries count_ref/any_ref/where_ref/select_*_ref,
//   but no transpiler path constructs a reference-element LINQ chain today;
//   newarr emits only int32[] and string[]/object[] -- the ref callback
//   ABI bool(*)(Il2CppObject*) has no producer yet).
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
        byte[] ps, int count, byte[]? genArgs, string? receiverType, bool receiverIsAddress,
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
            if (aggRecv is "System.Linq.IGrouping" or "System.Linq.IGroupingResult")
            {
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
            if (genArgs is not { Length: >= 1 } || genArgs[0] != 0x08)
            {
                string argType = genArgs is { Length: > 0 } ? GenericArgTypeName(genArgs[0]) : "IEnumerable<non-int32>";
                throw new NotSupportedException(
                    $"Enumerable.{name}<{argType}> not wired: " +
                    "the stage-1 runtime LINQ surface is int32 (0x08) element-family only " +
                    "(the ref-family icalls have no transpiler producer for their bool(*)(Il2CppObject*) callback ABI)");
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
                    return I("il2cpp::il2cpp_linq_grouping_to_array", 1, true, true, false, new[] { "" },
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

        // The generic operators (Where/Select/Count/Any/All/First/FirstOr-
        // Default/ToArray/OrderBy) all carry a MethodSpec with the int32
        // element byte. A non-int32 generic argument (or a non-generic call)
        // has no stage-1 runtime entry. ToList is EXCLUDED here -- its own
        // case carries the precise "List<int32> only" LOUD message.
        if (name != "ToList" && (genArgs is not { Length: >= 1 } || genArgs[0] != 0x08))
        {
            // The generic argument name for the LOUD message. The guard above
            // enters this block for a null/empty genArgs too (a plain
            // MemberRef call to a generic operator -- no MethodSpec), so the
            // null-safe read must not dereference genArgs.
            string argType = genArgs is { Length: > 0 } ? GenericArgTypeName(genArgs[0]) : "?";
            return name switch
            {
                "Count" or "Any" or "Where" or "Select" or "OrderBy" or "OrderByDescending" or "First" or "FirstOrDefault" or "ToArray" or "All" or "Take" or "Skip" or "TakeWhile" or "SkipWhile" or "Concat" or "Distinct" or "Except" or "Zip" or "Average" or "Reverse" or "Last" or "LastOrDefault" or "ElementAt" or "Contains" or "Aggregate" or "LongCount" or "SequenceEqual" or "DefaultIfEmpty" =>
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{argType}> not wired: " +
                        "the stage-1 runtime LINQ surface is int32 (0x08) element-family only " +
                        "(the ref-family icalls have no transpiler producer for their bool(*)(Il2CppObject*) callback ABI)"),
                _ => null,
            };
        }

        switch (name)
        {
            case "ToList":
                // il2cpp_linq_to_list materializes List<int32>; the result is
                // tracked as a List`1 reference so downstream List members
                // (get_Count / get_Item) resolve through MethodMap.List.
                if (genArgs is not { Length: >= 1 })
                    return null;
                if (genArgs[0] != 0x08)
                    throw new NotSupportedException(
                        $"Enumerable.ToList<{GenericArgTypeName(genArgs[0])}> not wired: the runtime il2cpp_linq_to_list materializes only List<int32> (the listi4 domain -- linq_map.h:84-93); a List<{GenericArgTypeName(genArgs[0])}> runtime does not exist in stage 1");
                return I("il2cpp::il2cpp_linq_to_list", 1, true, true, false,
                    OneArg, returnTypeName: "System.Collections.Generic.List`1");
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
                return I("il2cpp::il2cpp_linq_sum_i4", 1, true, false, false, OneArg);
            case "Min":
                return I("il2cpp::il2cpp_linq_min_i4", 1, true, false, false, OneArg);
            case "Max":
                return I("il2cpp::il2cpp_linq_max_i4", 1, true, false, false, OneArg);
            case "Average":
                // Average(int32[]) -> double (resultKind "d", linq.h). net48
                // Enumerable.Average(this IEnumerable<int>) returns double.
                return I("il2cpp::il2cpp_linq_average_i4", 1, true, false, false,
                    OneArg, resultKind: "d");
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
                        case 0x0D:
                            return I(selIdx
                                    ? "il2cpp::il2cpp_linq_select_idx_i4_d"
                                    : "il2cpp::il2cpp_linq_select_i4_d",
                                2, true, true, false, TwoArg, returnTypeName: "System.Double[]");
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
                return I("il2cpp::il2cpp_linq_to_array", 1, true, true, false, OneArg,
                    returnTypeName: "System.Int32[]");
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
                if (genArgs is not { Length: >= 2 } || genArgs[1] != 0x08)
                {
                    string elemArgType = genArgs is { Length: >= 1 }
                        ? GenericArgTypeName(genArgs[0]) : "?";
                    string keyArgType = genArgs is { Length: >= 2 }
                        ? GenericArgTypeName(genArgs[1]) : "?";
                    throw new NotSupportedException(
                        $"Enumerable.{name}<{elemArgType},{keyArgType}> not wired: " +
                        "the stage-1 then-by surface (il2cpp_linq_then_by_key_i4) is int32 (0x08) " +
                        "elements AND int32 (0x08) keys only -- the runtime fuses the (k1, k2) composite sort");
                }
                return I(name == "ThenBy"
                        ? ResolvedMethod.ThenByCombineMarker
                        : ResolvedMethod.ThenByDescCombineMarker,
                    2, false, false, false, new[] { "", "" },
                    returnTypeName: "System.Int32[]");
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
                if (genArgs is not { Length: >= 2 } || genArgs[1] != 0x08)
                {
                    string elemArgType = genArgs is { Length: >= 1 }
                        ? GenericArgTypeName(genArgs[0]) : "?";
                    string keyArgType = genArgs is { Length: >= 2 }
                        ? GenericArgTypeName(genArgs[1]) : "?";
                    throw new NotSupportedException(
                        $"Enumerable.GroupBy<{elemArgType},{keyArgType}> not wired: " +
                        "the stage-1 runtime grouping surface is int32 (0x08) " +
                        "elements AND int32 (0x08) keys only -- the runtime buckets by int32 keys");
                }
                {
                    var gbParams = LinqEncodedParamTypes(md, sigHandle);
                    string ElemFamily(byte b) => b switch
                    {
                        0x08 => "0",                                   // int32
                        0x0A => "2",                                   // int64 (boxed)
                        0x0D => "3",                                   // double (boxed)
                        0x0E or 0x12 or 0x1C or 0x1D or 0x15 or 0x13 or 0x1E => "1", // reference
                        _ => "",
                    };
                    string ResultFamily(byte b) => b switch
                    {
                        0x08 => "1",
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
                    var literals = new[] { ek, rk };
                    var retType = rk == "0"
                        ? "System.Linq.IGroupingResult"
                        : rk == "1" ? "System.Int32[]" : "System.Object[]";
                    // The result is ALWAYS a managed reference (a grouping
                    // result object or a result array), so the result temp is
                    // the Il2CppObject* slot (rv=true, rr=true).
                    return I("il2cpp::il2cpp_linq_group_by_full", count, true, true, false, casts,
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
                // Zip(source, second, resultSelector) -> il2cpp_linq_zip_i4
                // (D-LINQZ-1): element-wise pairing to the SHORTER length
                // (net48), the two-arg selector Func<TSource,TSecond,TResult>
                // invoked exactly once per output element. The receiver and
                // the second array ride the "a" cast; the selector delegate
                // passes through as the managed ref slot. All three generic
                // arguments must be int32 (the 2-arg selector ABI is
                // int32_t(*)(int32_t, int32_t) -- linq_map.h).
                if (genArgs is not { Length: >= 3 } || genArgs[1] != 0x08 || genArgs[2] != 0x08)
                {
                    string ts = genArgs is { Length: >= 1 } ? GenericArgTypeName(genArgs[0]) : "?";
                    string t2 = genArgs is { Length: >= 2 } ? GenericArgTypeName(genArgs[1]) : "?";
                    string tr = genArgs is { Length: >= 3 } ? GenericArgTypeName(genArgs[2]) : "?";
                    throw new NotSupportedException(
                        $"Enumerable.Zip<{ts},{t2},{tr}> not wired: " +
                        "the stage-1 runtime zip surface (il2cpp_linq_zip_i4) is int32 (0x08) " +
                        "elements AND an int32 (0x08) result -- the 2-arg selector ABI is int32(*)(int32_t, int32_t)");
                }
                return I("il2cpp::il2cpp_linq_zip_i4", 3, true, true, false,
                    new[] { "a", "a", "" }, returnTypeName: "System.Int32[]");
            case "Reverse":
                // Reverse(source) -> il2cpp_linq_reverse_i4: a NEW int32[] with
                // elements in reverse order (net48). Eager materialization.
                return I("il2cpp::il2cpp_linq_reverse_i4", 1, true, true, false,
                    OneArg, returnTypeName: "System.Int32[]");
            case "Last":
                // Last() -> last_i4: the last element; empty ->
                // InvalidOperationException. Last(source, predicate) is not
                // wired in stage 1 (no runtime entry).
                return I("il2cpp::il2cpp_linq_last_i4", 1, true, false, false, OneArg);
            case "LastOrDefault":
                return I("il2cpp::il2cpp_linq_last_or_default_i4", 1, true, false, false, OneArg);
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
                    return I("il2cpp::il2cpp_linq_aggregate_i4", 2, true, false, false, TwoArg);
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
                        0x0C or 0x0D => "d",
                        0x0E or 0x12 or 0x1C or 0x1D or 0x15 or 0x13 or 0x1E => "ref",
                        _ => "",
                    };
                    if (acc.Length == 0)
                    {
                        throw new NotSupportedException(
                            $"Enumerable.Aggregate TAccumulate '{GenericArgTypeName(accByte)}' is not a supported accumulator family");
                    }
                    if (count == 3)
                    {
                        return acc switch
                        {
                            "i4" => I("il2cpp::il2cpp_linq_aggregate_seed_i4", 3, true, false, false,
                                new[] { "a", "i", "" }),
                            "i8" => I("il2cpp::il2cpp_linq_aggregate_seed_i8", 3, true, false, false,
                                new[] { "a", "l", "" }, resultKind: "l"),
                            "d" => I("il2cpp::il2cpp_linq_aggregate_seed_d", 3, true, false, false,
                                new[] { "a", "d", "" }, resultKind: "d"),
                            _ => I("il2cpp::il2cpp_linq_aggregate_seed_ref", 3, true, true, false,
                                new[] { "a", "", "" }),
                        };
                    }
                    // count == 4: TResult decides the result carrier.
                    bool resRef = resByte is not (0x08 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06
                        or 0x07 or 0x09 or 0x0A or 0x0C or 0x0D);
                    string seedCast = acc switch { "i4" => "i", "i8" => "l", "d" => "d", _ => "" };
                    string cpp = acc switch
                    {
                        "i4" => resRef ? "il2cpp::il2cpp_linq_aggregate_seed_rs_ref"
                                       : "il2cpp::il2cpp_linq_aggregate_seed_rs_i4",
                        "i8" => resRef ? "il2cpp::il2cpp_linq_aggregate_seed_i8_rs_ref"
                                       : "il2cpp::il2cpp_linq_aggregate_seed_i8_rs_i4",
                        "d" => resRef ? "il2cpp::il2cpp_linq_aggregate_seed_d_rs_ref"
                                      : "il2cpp::il2cpp_linq_aggregate_seed_d_rs_i4",
                        _ => resRef ? "il2cpp::il2cpp_linq_aggregate_seed_ref_rs_ref"
                                    : "il2cpp::il2cpp_linq_aggregate_seed_ref_rs_i4",
                    };
                    return I(cpp, 4, true, resRef, false,
                        new[] { "a", seedCast, "", "" },
                        resultKind: resRef ? null : (resByte == 0x0A ? "l" : resByte == 0x0C || resByte == 0x0D ? "d" : null));
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
