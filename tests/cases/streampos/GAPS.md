# GAPS.md -- streampos recorded findings (e2e batch C72, 2026-08-12)

Case status: net48 oracle 63-line byte-exact reference written (expected.txt);
dual-compiler PASS verified via `pwsh scripts/run-tests.ps1` (mingw + msvc).

## Verified byte-exact contract (this case pins, no gap)

- Position round-trip over MemoryStream: write advances Position to the byte
  count; Length == Position; Position = 0 + full Read returns every byte and
  leaves Position == Length.
- Absolute repositioning: Position = 3 + partial Read(4) -> Position 7 and the
  window bytes match (3..6).
- Position PAST the end -> Read returns 0 (no exception) and Position is
  UNCHANGED; Position == Length -> clean EOF Read returns 0.
- Writing at a Position past the end extends Length to position+count (12+2 ->
  Length 14, Position 14) and ToArray() returns the extended length.
- Negative Position -> ArgumentOutOfRangeException.
- CanRead / CanWrite true, CanTimeout false (both stream kinds).
- FileStream mirrors: write chain, seek to 0 + full read, mid re-position +
  partial read, overwrite at a repositioned offset, EOF read returns 0 with
  Position pinned to Length.
- **D-STP-2 (FIXED 2026-08-12)**: Seek(long, SeekOrigin) / CanSeek /
  SetLength(long) now wired (MethodMap.StreamIO.cs -> icall_stream_{seek,
  get_can_seek, set_length}, streamio.cpp). Origin-aware Seek returns the new
  position (Begin/Current/End); invalid origin -> ArgumentException; negative
  result -> IOException; SetLength truncates (clamping a position past the new
  end) and extends ZERO-FILLED; negative SetLength -> ArgumentOutOfRangeException.
  net48 probe pinned: an APPEND-mode FileStream still reports CanSeek=true and
  Seek succeeds (only write placement differs -- "ab" writes land at EOF).
- **D-STP-3 (FIXED 2026-08-12)**: ReadByte()/WriteByte(byte) now wired
  (icall_stream_{read,write}_byte, streamio.cpp). ReadByte returns the byte as
  an int (0..255) and -1 at end-of-stream (net48); WriteByte writes a single
  byte, extending the stream at EOF.

## Recorded defects / unwired surface (NOT fixed -- batch rule)

- **D-STP-1** MemoryStream gap-zeroing on write past the end: net48 ZERO-FILLS
  the byte gap between the old Length and the write position (probe-verified:
  after Position=12 on a 10-byte stream + Write 2, the gap [10,12) reads 0),
  while the runtime il2cpp_stream_write (streamio.cpp:405) memcpy's only the
  written span and leaves the gap bytes UNINITIALIZED (MemoryEnsure realloc's
  without clearing). ToArray() after such a write therefore diverges in the
  gap bytes. The case probes only Position/Length/array-length (equal on both
  sides), never the gap CONTENT. (SetLength EXTENSIONS are zero-filled --
  D-STP-2 -- but an out-of-position WRITE still leaves the gap uninitialized.)
