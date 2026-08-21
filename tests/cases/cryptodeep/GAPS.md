# GAPS.md — cryptodeep recorded findings (e2e batch C69, 2026-08-12)

Case status: net48 oracle 28-line byte-exact reference written; dual-compiler
PASS verified via `pwsh scripts/run-tests.ps1` (mingw + msvc).

## D-CRYPTO-1 — incremental streaming (TransformBlock/TransformFinalBlock) still LOUD

- Inherited from crypto2's DEFECT-CRYPTO-1: the incremental hashing surface
  throws NotSupportedException (MethodMap.Crypto.cs, v2 work item). The
  range form ComputeHash(byte[], int, int) is the deepest partial-input probe
  available; a true feed-chunk-by-chunk roundtrip is NOT testable until v2.
  This case's multi-block sweeps (55..1000 bytes) exercise the block loop +
  padding-length handling through one-shot and (0,len) range calls instead.

## D-CRYPTO-2 — ComputeHash(Stream) and HashAlgorithm.Create(string) LOUD

- `ComputeHash(Stream)` (ps[0]==0x12) and the by-name `Create()` factory
  (CryptoConfig) are loud rejections in MethodMap.Crypto.cs. Not probed.

## D-CRYPTO-3 — HMAC / AES / RNG surfaces not exercised here

- HMACSHAx/Aes/RNG are wired (crypto_obj.cpp) but out of scope for this
  MD5/SHA1 vectors + roundtrip case; aescase/crypto2/other crypto cases own
  them.
