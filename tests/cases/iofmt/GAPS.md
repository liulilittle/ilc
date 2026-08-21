# GAPS.md — iofmt unwired API surface (2026-08-06)

The case probes ONLY the wired surface. Everything below was verified
against the current MethodMap + runtime headers and is deliberately NOT
called from `iofmt.cs` — each entry would abort the transpile with a LOUD
`NotSupportedException`.

## BinaryReader / BinaryWriter — ctor blocks the whole roundtrip

The binary PROTOCOL is wired (binaryio.h: `il2cpp_binary_write_{int32,int64,
uint32,double,single,bool,string}` / `il2cpp_binary_read_*`, plus the member
maps in `MethodMap.BinaryIO.cs`), so the task's planned
"BinaryWriter 写 → BinaryReader 读往返 (int/double/string/bool)" cannot run:

| Member | Status |
|--------|--------|
| `BinaryWriter.Write(int32/int64/uint32/double/single/bool/string)` | mapped (MethodMap.BinaryIO.cs:50-58) |
| `BinaryReader.ReadInt32/ReadInt64/ReadUInt32/ReadDouble/ReadSingle/ReadBoolean/ReadString` | mapped (MethodMap.BinaryIO.cs:74-80) |
| `BinaryWriter/BinaryReader .ctor(Stream)` | **LOUD rejection**: "System.IO.Stream has no carrier representation in M5 (StreamIO domain unwired) -- no stream object can exist" (MethodMap.BinaryIO.cs:65-66, 89-90) |
| `get_BaseStream` | LOUD rejection (MethodMap.BinaryIO.cs:67-68, 91-92) |

Root cause: the runtime has only HANDLE-FACTORY protocol icalls
(`il2cpp_binary_writer_create()` returning an opaque handle) — there is NO
object-typed binary icall surface (`grep icall_binary* src/runtime` → 0
hits) and no Stream carrier to build the reader/writer over. Closing this
gap needs: (a) a Stream carrier (StreamIO object closure), and (b)
object-typed ctor icalls (`icall_binarywriter_ctor(Il2CppObject* self,
Il2CppObject* stream)` style) backed by a handle-registry in the managed
object, mirroring the stringrw.h object model.

## MemoryStream — ctor not wired

`MemoryStream()` ctor is a LOUD rejection: "il2cpp_stream_memory_create() is
a handle FACTORY, not an object-typed ctor icall -- needs
icall_stream_memory_create(Il2CppObject* self) storing the handle into the
stream object" (MethodMap.StreamIO.cs:115-117). `ToArray()` is mapped
(MethodMap.StreamIO.cs:66-68) but unreachable without an instance.

## StringWriter — non-string Write variants not wired

The object surface is closed only for `(string)` arguments
(MethodMap.StringIO.cs:75-103):

| Member | Status |
|--------|--------|
| `Write(string)` / `WriteLine(string)` / `ToString()` / `Close()` | wired — probed in iofmt.cs |
| `Write(int/double/bool)` | LOUD rejection: the icall takes exactly `(self, string)` (MethodMap.StringIO.cs:78-80) |
| `Write(char)` / `Write(char[])` / `WriteLine(char)` | LOUD rejection (same line) |
| `WriteLine()` (0-arg) | LOUD rejection: needs a null-text variant (MethodMap.StringIO.cs:85-87) |
| `.ctor(IFormatProvider)` | LOUD rejection (MethodMap.StringIO.cs:127-128) |

Probe status: iofmt.cs covers the wired string variants only
(chain / null / empty / WriteLine / null-line / ToString snapshot /
writer→reader roundtrip).

## Untested (not unwired) — String.Format with boxed numbers

`String.Format("{0} {1}", int, double)` maps to `icall_string_format_2`
(MethodMap.String.cs:161-162), but no e2e case exercises the boxed-object
composite-format kernel (grep `String.Format(` in tests/cases → 0 hits), so
its net48-parity is unproven. iofmt.cs uses the filestreamcase-proven
`Concat(string, object)` numeric conveniences instead. A follow-up format
case can close this.
