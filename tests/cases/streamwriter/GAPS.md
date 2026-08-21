# GAPS.md -- streamwriter recorded findings (e2e batch C83, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 36-line
byte-exact). Active probes drive the StreamWriter write/encoding/flush faces
on the WIRED surface: (Stream) ctor + WriteLine chain with post-Flush
Length/Position, (string path) ctor, UTF-8 no-BOM byte accuracy (CJK 3-byte,
astral 4-byte, CRLF, raw-byte read-back), post-Flush flush idempotence +
Close auto-flush visibility, single-byte WriteByte/ReadByte (EOF -1), and the
exception paths. Gaps below are RECORDED, NOT FIXED.

## D-SW-1 -- Encoding ctors / non-string writer members unwired (LOUD)

- **Phenomenon**: `StreamWriter(string, Encoding)` and
  `StreamWriter(Stream, Encoding)` throw NotSupportedException in
  MethodMap.StreamIO.cs MapStreamCtor (the object icalls take exactly (Stream)
  or (string path) -- "the encoding overloads need extra icall variants").
  Likewise `StreamWriter.Write(string)` / `Write(char)` / `Write(char[])` /
  `WriteLine(int)` throw (the icall takes exactly (string)). The default-UTF-8
  ctor shapes are byte-exact; explicit-encoding probes are NOT reachable e2e.
- **Suspected root file**: `src/il2cppc/Emit/MethodMap.StreamIO.cs`
  (MapStreamCtor + WriteLine arm); `src/runtime/src/streamio.cpp`
  (no encoding-parameter ctor / non-string writer icalls).

## D-SW-2 -- Pre-Flush Length divergence (net48 buffers, runtime writes through)

- **Phenomenon**: net48 StreamWriter BUFFERS internally -- a WriteLine does
  NOT reach the file until Flush/Close, so `fs.Length`/`fs.Position` stay 0
  pre-Flush (net48 oracle probe: len_before_flush=0, len_after_flush=5). The
  runtime's icall_stream_write_line writes through to the stream immediately,
  so pre-Flush Length would report the written bytes. The byte-EXACT common
  ground is the POST-Flush observation, so every Length/Position probe in
  this case reads AFTER Flush/Close (Flush idempotence + Close auto-flush
  visibility are pinned as the flush face). Pre-Flush Length probes are
  deliberately absent.
- **Suspected root file**: `src/runtime/src/streamio.cpp`
  (icall_stream_write_line -- no user-space buffer stage).

## Notes

- `new StreamWriter(file)` (string-path ctor) writes through the same UTF-8
  no-BOM codec as io.cpp WriteAllLines (streamio.cpp header contract), which
  is why the byte-length probes (Encoding.UTF8.GetBytes == written length)
  are exact on both sides.
- Read-after-Close / write-after-Close diverge (net48 throws
  ObjectDisposedException; the runtime returns -1/no-op) -- streamops's
  known deviation, not probed here.
