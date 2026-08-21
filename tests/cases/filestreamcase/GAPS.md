# GAPS.md — filestreamcase unwired API surface

All four gaps listed below were **wired on 2026-08-05** (transpiler mappings in
`MethodMap.IO.cs`, runtime implementations in `io.cpp` / declarations in
`il2cpp_io.h`) and their probes are **active** in `filestreamcase.cs`. The case
transpiles, builds and diffs byte-identical against the net48 reference
(22 lines, verified via `run-tests.ps1 -Case filestreamcase` pipeline).

| API | Probe status | Wiring |
|-----|--------------|--------|
| `File.AppendAllText(path, contents)` | ACTIVE: `append_roundtrip_equal` (write "A" + append "B" + read-back "AB") | `icall_file_append_all_text` (`il2cpp_io.h` + `io.cpp`; `std::ios::app`), MethodMap.IO.cs File switch |
| `Path.ChangeExtension(path, extension)` | ACTIVE: `change_ext_eq` (.txt->.dat) + `change_ext_strip_eq` (null strip) | `icall_path_change_extension` (io.cpp thin alias of `il2cpp_path_change_extension`, pathproc.cpp:344 — pathproc.h is NOT in generated.h's include chain, so the alias exposes it via il2cpp_io.h), MethodMap.IO.cs Path switch |
| `Directory.Delete(path)` | ACTIVE: `dir_exists_after_delete=0` (create -> delete -> gone) | `icall_directory_delete` (`IL2CPP_IO_RMDIR`; 0=success, non-zero on missing/not-a-dir/not-empty), MethodMap.IO.cs Directory switch (was NotSupportedException throw) |
| `FileInfo.Length` | ACTIVE: `fileinfo_length_eq` + `fileinfo_length=5` (size-after-write) | FileInfo object closure: `System_IO_FileInfo_class` descriptor + `Il2CppFileInfoObject` (header + path GC-ref, bitmap 0b10) + `icall_fileinfo_ctor` + `icall_fileinfo_get_length` (stat st_size; 0 when missing — M3 result conventions), MethodMap.IO.cs FileInfo switch + dispatcher line |

## Consequences (resolved)

- The temp **directory** (`filestreamcase_dir`) is now removed at the end of
  the case (`Directory.Delete` wired) — no longer left behind.
- The temp **file** is removed at start AND end (File.Delete wired), so re-runs
  stay idempotent.
