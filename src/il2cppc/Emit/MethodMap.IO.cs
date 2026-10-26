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
                        // net48 has exactly File.Copy(string,string) and
                        // File.Copy(string,string,bool) -- any other arity is
                        // not a net48 overload (whitelist category 4), so this
                        // guard is unreachable from net48-compiled IL.
                        throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): File.Copy with {ps.Length} " +
                            "param(s) -- net48 has only (string,string) and (string,string,bool) " +
                            "[Reference Source file.cs:85,110]");
                    case "Move":
                        if (ps.Length == 2)
                            return I("il2cpp::runtime::icall_file_move", 2, false, false, false);
                        // net48 has exactly File.Move(string,string) -- any
                        // other arity is not a net48 overload (whitelist
                        // category 4).
                        throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): File.Move with {ps.Length} " +
                            "param(s) -- net48 has only (string,string) [Reference Source file.cs:1222]");
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
                        // net48 has exactly Directory.Delete(string) and
                        // Delete(string,bool) -- any other arity is not a net48
                        // overload (whitelist category 4).
                        throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): Directory.Delete with {count} " +
                            "param(s) -- net48 has only (string) and (string,bool) " +
                            "[Reference Source directory.cs:1281,1290]");
                    // nb2 DIR-FILES: Directory.GetFiles / GetDirectories /
                    // GetCurrentDirectory -- all four arms were missing
                    // (phantom symbols, nb2 red probe). GetFiles/GetDirectories
                    // share one runtime entry per name with defaulted trailing
                    // params (path / path+pattern / path+pattern+SearchOption);
                    // the SearchOption enum slot is an int32 carrier ("i").
                    // GetCurrentDirectory reuses the Environment icall.
                    case "GetCurrentDirectory":
                        return I("il2cpp::runtime::icall_environment_get_current_directory", 0, true, true, false,
                            returnTypeName: "System.String");
                    case "GetFiles":
                        if (count == 1)
                            return I("il2cpp::runtime::icall_directory_get_files", 1, true, true, false,
                                returnTypeName: "System.String[]");
                        if (count == 2)
                            return I("il2cpp::runtime::icall_directory_get_files", 2, true, true, false,
                                returnTypeName: "System.String[]");
                        if (count == 3)
                            return I("il2cpp::runtime::icall_directory_get_files", 3, true, true, false,
                                new[] { "", "", "i" }, returnTypeName: "System.String[]");
                        // net48 has exactly GetFiles(string), (string,string) and
                        // (string,string,SearchOption) -- any other arity is not a
                        // net48 overload (whitelist category 4).
                        throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): Directory.GetFiles with {count} " +
                            "param(s) -- net48 has only (string), (string,string) and (string,string,SearchOption) " +
                            "[Reference Source directory.cs:594,608,624]");
                    case "GetDirectories":
                        if (count == 1)
                            return I("il2cpp::runtime::icall_directory_get_directories", 1, true, true, false,
                                returnTypeName: "System.String[]");
                        if (count == 2)
                            return I("il2cpp::runtime::icall_directory_get_directories", 2, true, true, false,
                                returnTypeName: "System.String[]");
                        if (count == 3)
                            return I("il2cpp::runtime::icall_directory_get_directories", 3, true, true, false,
                                new[] { "", "", "i" }, returnTypeName: "System.String[]");
                        // net48 has exactly GetDirectories(string),
                        // (string,string) and (string,string,SearchOption) -- any
                        // other arity is not a net48 overload (whitelist
                        // category 4).
                        throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): Directory.GetDirectories with " +
                            $"{count} param(s) -- net48 has only (string), (string,string) and " +
                            "(string,string,SearchOption) [Reference Source directory.cs:666,680,696]");
                    default: return null;
                }
            case "System.IO.Path":
                switch (name)
                {
                    case "Combine":
                        // Combine(string[]) -- the params-array overload (a
                        // single SZARRAY param): the runtime icall folds every
                        // element left-to-right through the 2-arg semantics.
                        if (ps.Length == 1 && ps[0] == 0x1D)
                            return I("il2cpp::runtime::icall_path_combine_many", 1, true, true, false,
                                returnTypeName: "System.String");
                        if (ps.Length == 2) return I("il2cpp::runtime::icall_path_combine", 2, true, true, false);
                        // Work order #24 + nb2: Combine(string,string,string[,string])
                        // is the params-array form -- the IL pushes all args in
                        // one call, but the runtime icall takes exactly
                        // (string, string) (il2cpp_io.h has no multi-arg
                        // combine). The resolver emits the NestedCombineMarker
                        // and StackFold expands the N-ary call into nested
                        // icall_path_combine(combine(a,b),c) calls (StackFold.cs
                        // NestedCombineMarker handling -- 3-arg folded since
                        // Work order #24, extended to 4-arg in nb2).
                        if (ps.Length >= 3)
                            return new ResolvedMethod(ResolvedMethod.NestedCombineMarker, ps.Length, false, true, true,
                                new bool[ps.Length], false, ReturnTypeName: "System.String");
                        // net48 has exactly Combine(string,string),
                        // (string,string,string), (string,string,string,string) and
                        // the params-array form -- any other arity is not a
                        // net48 overload (whitelist category 4).
                        throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): Path.Combine with {ps.Length} " +
                            "arg(s) -- net48 has only the 2/3/4-arg and params-array forms " +
                            "[Reference Source path.cs:1197,1207,1218,1230]");
                    case "GetFileName": return I("il2cpp::runtime::icall_path_get_file_name", 1, true, true, false);
                    case "GetExtension": return I("il2cpp::runtime::icall_path_get_extension", 1, true, true, false);
                    case "ChangeExtension": return I("il2cpp::runtime::icall_path_change_extension", 2, true, true, false);
                    // nb2 IO-PATH2: the pathproc.h extension icalls had
                    // runtime implementations but NO mapping arms -- real calls
                    // resolved to phantom generated-name symbols (silent fold
                    // fall-through -> C++ compile error, nb2 red probe). All
                    // return the managed path string.
                    case "GetTempPath": return I("il2cpp::runtime::il2cpp_path_get_temp_path", 0, true, true, false,
                        returnTypeName: "System.String");
                    case "GetFullPath": return I("il2cpp::runtime::il2cpp_path_get_full_path", 1, true, true, false,
                        returnTypeName: "System.String");
                    case "HasExtension": return I("il2cpp::runtime::il2cpp_path_has_extension", 1, true, false, false);
                    // nb2 IO-PATH2: NET-NEW runtime extensions (pathproc.h /
                    // pathproc.cpp) -- pure string splitting, net48
                    // edge cases pinned in p-nb2-iopath.
                    case "GetDirectoryName": return I("il2cpp::runtime::il2cpp_path_get_directory_name", 1, true, true, false,
                        returnTypeName: "System.String");
                    case "GetFileNameWithoutExtension": return I("il2cpp::runtime::il2cpp_path_get_file_name_without_extension", 1, true, true, false,
                        returnTypeName: "System.String");
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
                        // net48 has exactly FileInfo(string) -- any other arity
                        // is not a net48 overload (whitelist category 4).
                        throw new NotSupportedException(
                            $"LOUD whitelist (net48 has no such overload): FileInfo ctor with {count} " +
                            "param(s) -- net48 has only (string) [Reference Source fileinfo.cs:69]");
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
