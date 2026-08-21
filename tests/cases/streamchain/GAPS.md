# GAPS.md -- streamchain recorded findings (e2e batch C77, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 24-line
byte-exact). Active probes cover the CHAIN dimension of the wired stream
surface (MethodMap.StreamIO.cs -> the object-typed stream icalls, streamio.h):
MemoryStream->ToArray->FileStream write/read conversion, byte[] <-> MemoryStream
double round trip, StreamWriter->Stream->StreamReader text chains (incl. blank
line + EOF), WriteByte->ReadByte byte loops (D-STP-3), Seek(Current/End)+
SetLength truncation chains (D-STP-2), MemoryStream->StreamWriter->ToArray->
StreamReader(Stream) composition, and the after-Flush length observation. Gaps
below are RECORDED, NOT FIXED (this batch creates test cases only).

## D-SCH-1 -- StreamWriter writes through; net48 buffers until Flush

- **Phenomenon**: `new StreamWriter(fs); sw.WriteLine("zeta"); fs.Length` is 0
  on net48 BEFORE Flush (the writer buffers the line) but > 0 on the runtime
  (the writer icall icall_stream_write_line appears to write through to the
  underlying stream immediately). The AFTER-Flush observation (Length > 0)
  agrees on both sides.
- **Confirmed**: probe removed (c7_prelen). The flush-observable guard now
  asserts only the post-Flush length.
- **Suspected root file**: `src/runtime/src/streamio.cpp` icall_stream_write_line
  (buffering behavior vs write-through).
