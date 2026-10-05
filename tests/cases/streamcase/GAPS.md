# GAPS.md — streamcase unwired surface (handle-gap exploration, 2026-08-06)

Case status: **NOT transpilable yet — deliberate.** The case is built first as
the net48 reference oracle (`expected.txt`, 20/20 probes True) and as the
precise blocker list for the stream objectification task. The runtime stream
icalls (`streamio.h`/`streamio.cpp`) are handle FACTORIES returning an opaque
`il2cpp_stream_t`; nothing associates the handle with a managed object, and the
transpiler maps stream ctors to LOUD rejections (MethodMap.StreamIO.cs
`MapStreamCtor`).

## Live transpile blocker (exact transcript)

```
il2cppc error: System.IO.FileStream::.ctor: FileStream ctor not wired: il2cpp_stream_file_open(path, mode) is a handle FACTORY, not an object-typed ctor icall -- needs icall_stream_file_open(Il2CppObject* self, Il2CppObject* path, int32 mode) storing the handle into the stream object (and FileMode/FileAccess -> runtime mode translation)
```

Hit first (the case's first statement), so the remaining rejections below never
surface in a live run — they are known statically from
`src/il2cppc/Emit/MethodMap.StreamIO.cs`.

## Static gap list (MethodMap.StreamIO.cs)

| Member | Blocker (exact message) |
|--------|-------------------------|
| `FileStream::.ctor` (any arity) | "FileStream ctor not wired: il2cpp_stream_file_open(path, mode) is a handle FACTORY, not an object-typed ctor icall -- needs icall_stream_file_open(Il2CppObject* self, Il2CppObject* path, int32 mode) storing the handle into the stream object (and FileMode/FileAccess -> runtime mode translation)" |
| `MemoryStream::.ctor` (any arity) | "MemoryStream ctor not wired: il2cpp_stream_memory_create() is a handle FACTORY, not an object-typed ctor icall -- needs icall_stream_memory_create(Il2CppObject* self) storing the handle into the stream object" |
| `StreamReader::.ctor` (any arity) | "StreamReader ctor not wired: needs an object-typed reader-open ctor icall (the raw file_open requires a mode constant the IL does not push -- no literal-injection mechanism exists)" |
| `StreamWriter::.ctor` (any arity) | "StreamWriter ctor not wired: needs an object-typed writer-open ctor icall (the raw file_open requires a mode constant the IL does not push -- no literal-injection mechanism exists)" |
| `Stream.Read(byte[],int,int)` / `ReadByte` | "Read/ReadByte not wired: the runtime read(void*, count) needs a managed-array icall (byte[] + offset)" (exercised by `ms.Read`) |
| `Stream.Write(byte[],int,int)` / `WriteByte` | "Write/WriteByte not wired: the runtime write(void*, count) needs a managed-array icall (byte[] + offset)" (exercised by `ms.Write`) |
| `Stream.Dispose(bool)` | "Dispose(bool) not wired: the runtime icall has no disposing flag (only Close)" |
| `StreamWriter.WriteLine(non-string)` | "WriteLine with N arg(s) not wired: the runtime icall takes exactly (string)" |

## Runtime handle gap (blocks end-to-end even after ctor wiring)

MethodMap.StreamIO.cs header note: `streamio.cpp` keeps stream state in an
opaque `StreamState*` never associated with a managed object — no class
descriptors, no handle table, no object-typed icall. The emitter passes the
managed receiver (`Il2CppObject*`) as arg[0]; no cast code can convert
`Il2CppObject*` -> `StreamState*`, so the (already wired) instance-method
mappings (`ReadLine`/`WriteLine`/`Flush`/`Close`/`Position`/`Length`/
`CanRead`/`CanWrite`/`ToArray`) generate C++ that does not compile. Required
end-state: object-typed entry points `icall_stream_*(Il2CppObject*)` backed by
an object-keyed handle registry (or a handle field in registered stream class
descriptors) + `BclAllocExpr` entries + runtime `System_IO_*_class`
descriptors (CodeGenerator.cs BclAllocExpr lists only StringBuilder/Hashtable/
Random today).

## Desired end-state surface exercised by the case

- `new FileStream(file, FileMode.Create, FileAccess.Write)` ->
  `StreamWriter.WriteLine` x3 -> `Flush` -> `fs.Position == 10` -> `Close`
- `new FileStream(file, FileMode.Open, FileAccess.Read)` -> `StreamReader.ReadLine`
  x3 + EOF null -> `fr.Length == 32` -> `Close`; `CanRead`/`CanWrite` on both
- `new MemoryStream()` -> `Write`/`Read`/`Position` set/get/`ToArray` roundtrip

## Verification evidence (2026-08-06)

- `dotnet build streamcase.csproj -c Release -o out` -> 0 errors / 0 warnings
- `out\streamcase.exe` (net48) -> `expected.txt` 20/20 `True`; temp file
  `streamcase_tmp.txt` deleted at start AND end (re-run idempotent)
- `dotnet ...\il2cppc.dll out\streamcase.exe <tmp-gen>` -> loud FileStream
  ctor rejection above (transpiler dll 10:55:17 build; MethodMap.StreamIO.cs
  mtime 2026-08-03 21:55:49 included)
