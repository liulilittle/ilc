namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.RegexObj -- System.Text.RegularExpressions.Match / Group /
// GroupCollection property + indexer icall mappings (regex_obj.h,
// implementation regex_obj.cpp).
// Owners: System.Text.RegularExpressions.{Match,Group,Capture,GroupCollection}.
//
// nb5 OBJ-1: Regex.Match now returns a Match object graph (MethodMap.Regex.cs
// returnTypeName "System.Text.RegularExpressions.Match"); the receiver-typed
// fold tracks the result so the property / indexer chains below resolve:
//
//   Match.Success / Index / Length / Value / ToString()   (get_* / ToString)
//   Match.Groups            -> GroupCollection (tracked result)
//   GroupCollection.Count   -> get_Count
//   GroupCollection[int]    -> get_Item -> Group (tracked result)
//   Group.Value / Index / Length / Success / Name
//
// ROUTING: Match : Group : Capture is the net48 inheritance chain, so the C#
// compiler emits the MemberRef on the DECLARING type -- Index/Length/Value/
// ToString on Capture, Success/Name on Group, Groups on Match. The receiver
// object's true type (receiverType, tracked from the Regex.Match / get_Groups
// / get_Item result) therefore selects the arm; the declaring owner is only a
// fallback when no receiver hint reached this arm.
//
// Every icall is an instance call on the managed object (receiver passes
// through the "" slot); int32 args use "i". Named groups / GroupCollection
//[string] / MatchCollection are not in this batch (OBJ-2/OBJ-3).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapRegexObj(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        // The receiver's true type wins over the declaring owner (the chain
        // emits members on the base Capture/Group); fall back to the owner.
        string rt = receiverType ?? owner;
        switch (rt)
        {
            case "System.Text.RegularExpressions.Match":
                switch (name)
                {
                    case "get_Success":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_regex_match_get_success", 0, true, false, true);
                        return null;
                    case "get_Index":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_regex_match_get_index", 0, true, false, true);
                        return null;
                    case "get_Length":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_regex_match_get_length", 0, true, false, true);
                        return null;
                    case "get_Value":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_regex_match_get_value", 0, true, true, true,
                                returnTypeName: "System.String");
                        return null;
                    case "ToString":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_regex_match_to_string", 0, true, true, true,
                                returnTypeName: "System.String");
                        return null;
                    case "get_Groups":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_regex_match_get_groups", 0, true, true, true,
                                returnTypeName: "System.Text.RegularExpressions.GroupCollection");
                        return null;
                    default:
                        return null;
                }
            case "System.Text.RegularExpressions.Group":
                switch (name)
                {
                    case "get_Success":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_regex_group_get_success", 0, true, false, true);
                        return null;
                    case "get_Index":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_regex_group_get_index", 0, true, false, true);
                        return null;
                    case "get_Length":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_regex_group_get_length", 0, true, false, true);
                        return null;
                    case "get_Value":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_regex_group_get_value", 0, true, true, true,
                                returnTypeName: "System.String");
                        return null;
                    case "get_Name":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_regex_group_get_name", 0, true, true, true,
                                returnTypeName: "System.String");
                        return null;
                    default:
                        return null;
                }
            case "System.Text.RegularExpressions.GroupCollection":
                switch (name)
                {
                    case "get_Count":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_regex_gc_get_count", 0, true, false, true);
                        return null;
                    case "get_Item":
                        // GroupCollection[int index] -> Group.
                        if (count == 1 && ps[0] is 0x08 or 0x09)
                            return I("il2cpp::runtime::icall_regex_gc_get_item_int", 1, true, true, true,
                                new[] { "", "i" }, returnTypeName: "System.Text.RegularExpressions.Group");
                        // GroupCollection[string name] -> Group (OBJ-3): the
                        // string slot carries the "s" cast.
                        if (count == 1 && ps[0] == 0x0E)
                            return I("il2cpp::runtime::icall_regex_gc_get_item_str", 1, true, true, true,
                                new[] { "", "s" }, returnTypeName: "System.Text.RegularExpressions.Group");
                        return null;
                    default:
                        return null;
                }
            case "System.Text.RegularExpressions.MatchCollection":
                // nb6 OBJ-2: Regex.Matches result -- Count / this[int] -> Match
                // / GetEnumerator (foreach; the enumerator is tracked under the
                // shared snapshot-enumerator pseudo-type so the IEnumerator
                // member arms route it).
                switch (name)
                {
                    case "get_Count":
                        if (count == 0)
                            return I("il2cpp::runtime::icall_regex_mc_get_count", 0, true, false, true);
                        return null;
                    case "get_Item":
                        if (count == 1 && ps[0] is 0x08 or 0x09)
                            return I("il2cpp::runtime::icall_regex_mc_get_item_int", 1, true, true, true,
                                new[] { "", "i" }, returnTypeName: "System.Text.RegularExpressions.Match");
                        return null;
                    case "GetEnumerator":
                        if (count == 0)
                            return I("il2cpp::runtime::il2cpp_regex_mc_get_enumerator", 0, true, true, true,
                                new[] { "" }, returnTypeName: "System.Collections.SortedList.Enumerator");
                        return null;
                    default:
                        return null;
                }
            default:
                return null;
        }
    }
}
