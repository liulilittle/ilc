namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.IO -- System.IO icall mappings (streamio.h).
// Owners: System.IO.File / System.IO.Directory / System.IO.Path /
// System.IO.FileInfo
// (multi-owner domain: dispatch on `owner` first, then `name`).
//
// System.IO.FileInfo (M6 object closure -- the StringReader/StringWriter
// precedent, il2cpp_io.h): the runtime owns the System_IO_FileInfo_class
// descriptor (Il2CppFileInfoObject: header + path GC-ref slot) and the
// object icalls below. Generated newobj allocates through
// il2cpp_object_new(&System_IO_FileInfo_class) -- the CodeGenerator.cs:1491
// default emits exactly this mangled global symbol, so NO BclAllocExpr entry
// is required.
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapIO(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.IO.File":
                switch (name)
                {
                    case "Exists": return I("il2cpp::runtime::icall_file_exists", 1, true, false, false);
                    case "Delete": return I("il2cpp::runtime::icall_file_delete", 1, false, false, false);
                    case "WriteAllText": return I("il2cpp::runtime::icall_file_write_all_text", 2, false, false, false);
                    case "AppendAllText": return I("il2cpp::runtime::icall_file_append_all_text", 2, false, false, false);
                    case "ReadAllText": return I("il2cpp::runtime::icall_file_read_all_text", 1, true, true, false);
                    // P1 IO-symmetry (2026-09-19): File.ReadAllBytes/WriteAllBytes
                    // -- previously unmapped (the generated call referenced an
                    // undeclared symbol). ReadAllBytes returns System.Byte[]
                    // (net48: throws when the file cannot be opened; the runtime
                    // signals failure with null, the M3 convention). WriteAllBytes
                    // takes the byte[] with the "a" (Il2CppArray*) cast; a null
                    // array writes an empty file (net48 throws ArgumentNullException
                    // -- the runtime follows the WriteAllText null-contents shape).
                    case "ReadAllBytes": return I("il2cpp::runtime::icall_file_read_all_bytes", 1, true, true, false, returnTypeName: "System.Byte[]");
                    case "WriteAllBytes": return I("il2cpp::runtime::icall_file_write_all_bytes", 2, false, false, false, new[] { "", "a" });
                    case "WriteAllLines": return I("il2cpp::runtime::icall_file_write_all_lines", 2, false, false, false);
                    case "ReadAllLines": return I("il2cpp::runtime::icall_file_read_all_lines", 1, true, true, false);
                    // M7 io3 (2026-08-08): the File full-API gap closures
                    // (io.cpp: icall_file_copy / icall_file_copy_overwrite /
                    // icall_file_move / icall_file_get_attributes /
                    // icall_file_set_attributes / icall_file_get_last_write_time).
                    // All follow the il2cpp_io.h conventions: 0 success /
                    // non-zero failure (-1 null path), value funcs 0 on
                    // failure; the status ints the .NET methods discard are
                    // dropped (ReturnsValue false).
                    case "Copy":
                        if (ps.Length == 2)
                            return I("il2cpp::runtime::icall_file_copy", 2, false, false, false);
                        if (ps.Length == 3)
                            return I("il2cpp::runtime::icall_file_copy_overwrite", 3, false, false, false);
                        throw new NotSupportedException($"File.Copy with {ps.Length} param(s) not wired (expected (string, string) or (string, string, bool))");
                    case "Move":
                        if (ps.Length == 2)
                            return I("il2cpp::runtime::icall_file_move", 2, false, false, false);
                        throw new NotSupportedException($"File.Move with {ps.Length} param(s) not wired (expected (string, string))");
                    case "GetAttributes":
                        return I("il2cpp::runtime::icall_file_get_attributes", 1, true, false, false);
                    case "SetAttributes":
                        return I("il2cpp::runtime::icall_file_set_attributes", 2, false, false, false);
                    case "GetLastWriteTime":
                        // DateTime result -- "dt" result kind (the emitter's
                        // value-type temp carrier, DateTime precedent).
                        return I("il2cpp::runtime::icall_file_get_last_write_time", 1, true, false, false, null, "dt");
                    default: return null;
                }
            case "System.IO.Directory":
                switch (name)
                {
                    case "CreateDirectory":
                        // The .NET return value (DirectoryInfo) is not modeled; the
                        // runtime icall returns a status int that callers discard.
                        return I("il2cpp::runtime::icall_directory_create_directory", 1, false, false, false);
                    case "Exists": return I("il2cpp::runtime::icall_directory_exists", 1, true, false, false);
                    case "Delete":
                        // 1-arg Delete(path): maps to the non-recursive icall
                        // (a missing path is a status error -- M3 convention).
                        if (count == 1)
                            return I("il2cpp::runtime::icall_directory_delete", 1, false, false, false);
                        // 2-arg Delete(path, recursive): the recursive icall
                        // (iocase, rtdel-b23 -- icall_directory_delete_recursive,
                        // il2cpp_io.h). "s" casts the managed string to
                        // Il2CppString* (the icall's path shape); the bool
                        // travels as an int32 carrier with the "" raw
                        // passthrough (icall_file_copy_overwrite's overwrite
                        // flag convention). A non-empty tree with
                        // recursive==0 is a status error (tree intact), with
                        // recursive!=0 the whole tree is removed; null path
                        // is a status error.
                        if (count == 2)
                            return I("il2cpp::runtime::icall_directory_delete_recursive", 2, false, false, false,
                                new[] { "s", "" });
                        throw new NotSupportedException($"Directory.Delete with {count} param(s) not wired (expected (string) or (string, bool))");
                    default: return null;
                }
            case "System.IO.Path":
                switch (name)
                {
                    case "Combine":
                        if (ps.Length == 2) return I("il2cpp::runtime::icall_path_combine", 2, true, true, false);
                        // Work order #24: Path.Combine(string, string, string)
                        // is the params-array form of Combine -- the IL pushes
                        // all args in one call, but the runtime icall takes
                        // exactly (string, string) (il2cpp_io.h has no
                        // multi-arg combine). The resolver emits the
                        // NestedCombineMarker and StackFold expands the 3-arg
                        // call into nested icall_path_combine(combine(a,b),c)
                        // calls (StackFold.cs NestedCombineMarker handling).
                        if (ps.Length == 3)
                            return new ResolvedMethod(ResolvedMethod.NestedCombineMarker, 3, false, true, true,
                                new[] { false, false, false }, false, ReturnTypeName: "System.String");
                        throw new NotSupportedException($"Path.Combine with {ps.Length} arg(s) not wired: the runtime icall takes exactly (string, string); only the 2-arg form and the 3-arg nested fold are wired");
                    case "GetFileName": return I("il2cpp::runtime::icall_path_get_file_name", 1, true, true, false);
                    case "GetExtension": return I("il2cpp::runtime::icall_path_get_extension", 1, true, true, false);
                    case "ChangeExtension": return I("il2cpp::runtime::icall_path_change_extension", 2, true, true, false);
                    default: return null;
                }
            case "System.IO.FileInfo":
                switch (name)
                {
                    case ".ctor":
                        // FileInfo(string) -- object icall: stores the path in the
                        // GC-ref slot (receiver-first call shape, like StringIO).
                        if (count == 1 && ps[0] == 0x0E)
                            return I("il2cpp::runtime::icall_fileinfo_ctor", 1, false, false, true);
                        throw new NotSupportedException($"FileInfo ctor with {count} param(s) not wired (expected exactly (string))");
                    case "get_Length":
                        // FileInfo.Length (instance long): stat the stored path at
                        // call time; 0 when it cannot be stat'ed (M3 result
                        // conventions, see il2cpp_io.h).
                        return I("il2cpp::runtime::icall_fileinfo_get_length", 0, true, false, true, null, "l");
                    default: return null;
                }
            default: return null;
        }
    }
}
