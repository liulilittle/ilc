namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.ProcessInfo -- System.Diagnostics.ProcessStartInfo object-model
// icall mappings (pathproc.h / pathproc.cpp -- the GLOBAL
// System_Diagnostics_ProcessStartInfo_class descriptor).
// Owner: System.Diagnostics.ProcessStartInfo.
//
// nb7 PROC-STARTINFO: ProcessStartInfo is a light managed object (header + the
// FileName/Arguments string fields + the boolean flags). The transpiler emits
// no descriptor for it; the newobj fallback allocates through
// il2cpp_object_new(&System_Diagnostics_ProcessStartInfo_class) (the
// System.Version contract) and the icalls below own the field storage:
//
//   .ctor()                    -> icall_processstartinfo_ctor (net48 defaults:
//                                 FileName/Arguments "" + UseShellExecute true)
//   .ctor(string)              -> icall_processstartinfo_ctor_fn
//   .ctor(string, string)      -> icall_processstartinfo_ctor_fn_args
//   get_/set_FileName   (string)
//   get_/set_Arguments  (string)
//   get_/set_UseShellExecute        (bool)
//   get_/set_RedirectStandardOutput (bool)
//   get_/set_RedirectStandardError  (bool)
//   get_/set_CreateNoWindow         (bool)
//
// The receiver passes through the "" slot; int32 args use "i"; string args ""
// and the string result rides a managed reference. Only these members are
// wired -- anything else falls through to the generated-name path.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapProcessInfo(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            case ".ctor":
                if (count == 0)
                    return I("il2cpp::runtime::icall_processstartinfo_ctor", 0, false, false, true);
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_processstartinfo_ctor_fn", 1, false, false, true,
                        new[] { "", "" });
                if (count == 2 && ps[0] == 0x0E && ps[1] == 0x0E)
                    return I("il2cpp::runtime::icall_processstartinfo_ctor_fn_args", 2, false, false, true,
                        new[] { "", "", "" });
                throw new NotSupportedException(
                    $"ProcessStartInfo ctor with {count} param(s) not wired (only (), (string), (string, string))");

            case "get_FileName":
                if (count == 0)
                    return I("il2cpp::runtime::icall_processstartinfo_get_filename", 0, true, true, true,
                        returnTypeName: "System.String");
                return null;
            case "set_FileName":
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_processstartinfo_set_filename", 1, false, false, true,
                        new[] { "", "" });
                return null;

            case "get_Arguments":
                if (count == 0)
                    return I("il2cpp::runtime::icall_processstartinfo_get_arguments", 0, true, true, true,
                        returnTypeName: "System.String");
                return null;
            case "set_Arguments":
                if (count == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_processstartinfo_set_arguments", 1, false, false, true,
                        new[] { "", "" });
                return null;

            case "get_UseShellExecute":
                if (count == 0)
                    return I("il2cpp::runtime::icall_processstartinfo_get_useshellexecute", 0, true, false, true);
                return null;
            case "set_UseShellExecute":
                // bool setter: the IL boolean (0x02) rides the int32 slot ("i").
                if (count == 1 && ps[0] == 0x02)
                    return I("il2cpp::runtime::icall_processstartinfo_set_useshellexecute", 1, false, false, true,
                        new[] { "", "i" });
                return null;

            case "get_RedirectStandardOutput":
                if (count == 0)
                    return I("il2cpp::runtime::icall_processstartinfo_get_redirectstdout", 0, true, false, true);
                return null;
            case "set_RedirectStandardOutput":
                if (count == 1 && ps[0] == 0x02)
                    return I("il2cpp::runtime::icall_processstartinfo_set_redirectstdout", 1, false, false, true,
                        new[] { "", "i" });
                return null;

            case "get_RedirectStandardError":
                if (count == 0)
                    return I("il2cpp::runtime::icall_processstartinfo_get_redirectstderr", 0, true, false, true);
                return null;
            case "set_RedirectStandardError":
                if (count == 1 && ps[0] == 0x02)
                    return I("il2cpp::runtime::icall_processstartinfo_set_redirectstderr", 1, false, false, true,
                        new[] { "", "i" });
                return null;

            case "get_CreateNoWindow":
                if (count == 0)
                    return I("il2cpp::runtime::icall_processstartinfo_get_createnowindow", 0, true, false, true);
                return null;
            case "set_CreateNoWindow":
                if (count == 1 && ps[0] == 0x02)
                    return I("il2cpp::runtime::icall_processstartinfo_set_createnowindow", 1, false, false, true,
                        new[] { "", "i" });
                return null;

            default:
                return null;
        }
    }
}
