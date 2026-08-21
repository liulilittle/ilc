# GAPS.md — crypto2 notes (2026-08-06)

26 probes PASS vs net48 (mingw, -O0): one-shot vs range(0, Length) roundtrips,
offset/count slicing ("abc" head slice at offset 0, "def" mid slice at
offset 3), empty input (ComputeHash(empty), range count=0, empty-array Hex),
repeated-call independence on a single instance, digest byte lengths
(16/20/32) and hex lengths (32/40/64), Hex formatting (leading zero byte ->
"00..", lowercase, empty input -> empty string), and the NIST/well-known
vectors for MD5/SHA1/SHA256 over "abc", "" and the quick-brown-fox sentence.

## DEFECT-CRYPTO-1 — incremental streaming hashing is a LOUD rejection (v2)

The .NET incremental surface -- HashAlgorithm.TransformBlock /
TransformFinalBlock -- throws NotSupportedException in MethodMap.Crypto.cs
("Incremental streaming hashing (TransformBlock/TransformFinalBlock) is not
wired (v2)"), as does ComputeHash(Stream) ("requires a streaming read
loop"). The range form ComputeHash(byte[], offset, count) is the deepest
partial-input probe the wired surface can express today; a true incremental
roundtrip (feed chunk 1, feed chunk 2, finalize) is NOT testable until v2
lands.
