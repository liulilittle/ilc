# GAPS.md -- binaryiodeep (e2e batch C56)

## 1. BinaryReader / BinaryWriter object surface is blocked (not fixed per batch rules)

`MethodMap.BinaryIO.cs` maps the `Write(int32/int64/uint32/double/single/bool/
string)` and `ReadInt32/ReadInt64/ReadUInt32/ReadDouble/ReadSingle/ReadBoolean/
ReadString` icalls, but **every `.ctor` is a LOUD `NotSupportedException`**
(MethodMap.BinaryIO.cs:65-66, 89-90):

    System.IO.BinaryWriter::.ctor: ctor takes a System.IO.Stream, which has no
    carrier representation in M5 (StreamIO domain unwired)

Empirically verified on this HEAD (2026-08-12, bc1c0b9): a probe
`new BinaryWriter(new MemoryStream())` fails the transpile with exactly that
reject. Additionally, the runtime binaryio domain (`binaryio.cpp`) has **no
managed-object class descriptor** for BinaryReader/BinaryWriter -- the icalls
operate on opaque C++ handles (`Il2CppBinaryWriter*`/`Il2CppBinaryReader*`,
created only through `il2cpp_binary_writer_create`/`il2cpp_binary_reader_create`),
so even a runtime-side object closure is absent (contrast Stopwatch's
`System_Diagnostics_Stopwatch_class`).

Consequence: **no BinaryReader/BinaryWriter instance is constructible, so the
round-trip / type surface (Write->Read over the protocol) is unreachable from
generated code.**

This case therefore pins the same byte-level contract through the reachable
composition of `MemoryStream` (byte-stream carrier) + `BitConverter` (exact
little-endian encoders): the LE int32/int64/uint32/double/single layouts and
the 1-byte bool width are exactly the `BinaryWriter.Write` protocol widths.

Fix path (NOT applied): (a) runtime -- add a `System_IO_BinaryReader/Writer`
object class + `icall_binary_{reader,writer}_ctor(Il2CppObject*, Il2CppObject* stream)`
mirroring the stream wrapper ctors (`icall_stream_reader_ctor`, streamio.cpp);
(b) transpiler -- map the .ctor(Stream) form to it (receiver "" passthrough).

## 2. BitConverter startIndex overload drops the index (documented transpiler gap)

`BitConverter.ToT(byte[], int)` drops `startIndex` and always reads index 0
(MethodMap.BitConverter.cs MapToT). The case therefore decodes only dedicated
buffers at index 0 (never a nonzero offset). `BitConverter.get_IsLittleEndian`
is a LOUD reject -- never referenced.
