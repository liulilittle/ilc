# GAPS.md — streamops unwired surface (2026-08-07, CLOSED 2026-08-09 b23)

STATUS (CLOSED): the case now transpiles, builds (mingw) and runs
BYTE-EXACT against the net48 oracle (16/16 lines MATCH; verified 2026-08-09
~23:10). The remaining unwired surface below is documentation of the old
gaps as they stood before the b23 closure; the current wired surface is in
MethodMap.StreamIO.cs (StreamWriter(string path) -> icall_stream_writer_path_ctor,
runtime streamio.cpp, plus the IDisposable::Dispose using-block routing in
MethodRefResolver.cs).

Original status (2026-08-07): the net48 oracle (16 lines, expected.txt) is
byte-exact and every probe is net48-verified True, but the case CANNOT
transpile — `pwsh scripts/run-tests.ps1 -Compiler mingw -Case streamops`
FAILS at the transpile stage. The ROOT cause was a transpiler/runtime
version skew: the RUNTIME stream OBJECT layer landed (commit 1066ad7,
runtime batch 17) but the transpiler's MethodMap.StreamIO.cs was not yet
updated to it.

## D-STO-1 — FileStream ctor LOUD: transpiler not updated to the object-typed ctor

- **Phenomenon** (the case's first `new FileStream(...)` aborts the transpile):
  ```
  il2cppc error: System.IO.FileStream::.ctor: FileStream ctor not wired:
  il2cpp_stream_file_open(path, mode) is a handle FACTORY, not an object-typed
  ctor icall -- needs icall_stream_file_open(Il2CppObject* self, ...)
  ```
- **Causal chain**: the runtime ALREADY implements the object-typed ctor —
  `icall_stream_file_ctor(Il2CppObject* self, Il2CppObject* path, int32_t
  fileMode, int32_t fileAccess)` (il2cpp_streamio.h:90, streamio.cpp:761) with
  FileMode/FileAccess translation and FileNotFoundException/ArgumentException
  throwing — but `MapStreamCtor` (MethodMap.StreamIO.cs:108-126) still LOUD-
  rejects every stream ctor and its error message even names the OLD proposal
  (`icall_stream_file_open`) instead of the landed `icall_stream_file_ctor`.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.StreamIO.cs` (MapStreamCtor).

## D-STO-2 — StreamReader/StreamWriter ctor LOUD

- Same skew as D-STO-1: `MapStreamCtor` LOUD-rejects `System.IO.StreamReader`
  / `System.IO.StreamWriter` ctors (MethodMap.StreamIO.cs:118-123) although the
  runtime has `icall_stream_reader_ctor` / `icall_stream_writer_ctor`
  (il2cpp_streamio.h:102/107, streamio.cpp:802/823) wrapping an existing stream
  with refCounted shared backend state.

## D-STO-3 — Stream.Read/Write LOUD + member mapping stale

- `Stream.Read(byte[], int, int)` / `Write(...)` LOUD-reject "the runtime
  read(void*, count) needs a managed-array icall (byte[] + offset)"
  (MethodMap.StreamIO.cs:89-97) although the runtime HAS the managed-array
  `icall_stream_read_bytes(Il2CppObject*, Il2CppArray*, int32, int32)` /
  `icall_stream_write_bytes` (il2cpp_streamio.h:123/129).
- The WriteLine/ReadLine member mappings still point at the OLD HANDLE-layer
  names (`il2cpp::runtime::il2cpp_stream_write_line/read_line`, streamio.h)
  rather than the object-typed `icall_stream_write_line` / `icall_stream_read_line`
  (il2cpp_streamio.h:155/160) — the receiver is Il2CppObject*, so even the
  member calls would not compile as emitted.

## Closure note (what the wired surface should look like)

Once the MethodMap.StreamIO.cs wiring task lands (map the 4 object ctors +
read/write bytes + swap the member icall names), the streamops oracle probes
are all net48-consistent (verified live: w_len=20 for CRLF "line one"/"line
two", zh_len=17 for "中文往返123"+CRLF UTF-8, using_flushed=True,
missing_throws=True FileNotFoundException, read_writeonly_throws=True
NotSupportedException). Two net48 deviations of the runtime model are
deliberately NOT probed (streamio.h documents each): read/position after Close
returns -1/nullptr (net48 throws ObjectDisposedException); StreamWriter writes
UTF-8 no BOM (net48 default) with CRLF terminator.
