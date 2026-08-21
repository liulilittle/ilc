# GAPS.md — fileops2 notes (2026-08-06)

26 probes PASS vs net48 (mingw, -O0). The batch-21 wide-path fix is verified
end-to-end: the UTF-8 Chinese file name `宽路径_测试.txt` survives the
WriteAllText -> Exists -> ReadAllText -> Delete chain (plat_file_open via
`_wfopen` + plat_file_remove via `DeleteFileW` + the wide stat behind
plat_path_is_file). FileInfo.Length agrees on the no-BOM UTF-8 byte length
("宽内容" = 9 bytes on both runtimes).

## DEFECT-FILE-1..5 — the System.IO.File surface ends at the MethodMap.IO.cs mappings

File.Copy / File.Move / File.GetAttributes / File.SetAttributes /
File.GetLastWriteTime are NOT wired (MethodMap.IO.cs maps Exists/Delete/
Write*Text/Read*Text/Write*Lines/Read*Lines/Path.*/Directory.*/FileInfo
only). Each member falls through to the generated-name path -- the
transpiler does NOT abort (silent), and emits a call to an UNDEFINED symbol.
Verified by transpiling a dedicated probe: the generated Cases_*.cpp calls

    System_IO_File_Copy_7e7bc9cf97118130(...)
    System_IO_File_Move_3aaf01e7b896b680(...)
    System_IO_File_GetAttributes_6f53b5b914355c58(...)
    System_IO_File_SetAttributes_218124114a3bc648(...)
    System_IO_File_GetLastWriteTime_b5d24b57321b5a61(...)

No definition exists for a BCL type, so the link/build aborts with "use of
undeclared identifier" -- the same mechanism as typeops DEFECT-TYPE-2/3/4.
The Copy/Move/attribute probes are therefore NOT called here.

Related: `Directory.Delete(path, recursive)` is a LOUD NotSupportedException
in MethodMap.IO.cs (the runtime icall is non-recursive); `Directory.Delete
(missing)` differs from net48 (status error vs DirectoryNotFoundException)
-- neither is probed.
