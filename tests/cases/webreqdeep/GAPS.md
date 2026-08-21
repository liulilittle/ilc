# GAPS.md -- webreqdeep recorded findings (e2e batch C59, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 17-line byte-exact).

## D-HTTP-1 -- net48 WebClient.DownloadString decodes with Encoding.Default, NOT charset=utf-8

- **Phenomenon**: with the server sending `Content-Type: text/plain;
  charset=utf-8` and a multibyte UTF-8 body, the net48 oracle decoded the
  body as GBK (this machine's ANSI codepage): "涓栫晫" for 世界, dl_len
  mismatch -- while the ILC runtime hardcodes UTF-8. The two decoders can
  therefore ONLY agree on pure-ASCII bytes.
- **Evidence** (probe, 2026-08-12): `HttpWebResponse.CharacterSet` DOES
  parse "utf-8" from the header correctly, but `WebClient.DownloadString`
  still produced the Encoding.Default (GBK) string -- a .NET Framework
  4.8 WebClient behavior (charset ignored on the DownloadString path).
- **Disposition**: the case's download body is pure ASCII ("Hello, ILC!
  Download OK 2026" -- 28 bytes), so both decoders produce the identical
  string. The multibyte encoding SURFACE of the runtime's UTF-8 decode is
  therefore NOT byte-exactly oracle-verifiable against net48 WebClient; it
  stays runtime-unit-tested only.
- **Suspect**: net48 framework behavior (not fixable in ILC); the runtime's
  hardcoded UTF-8 decode stays.

## D-STR-UTF8-1 -- runtime UTF-8 decoder counts an astral char as ONE UTF-16 unit

- **Phenomenon**: for a body containing U+1F600, net48 `string.Length`
  returns 2 (surrogate pair) while the runtime returns 1 (probe: len=21 vs
  len=20). BMP characters (世/é/€) count identically on both sides.
- **Causal chain**: the runtime's `il2cpp_string_new_from_utf8` decode
  stores an astral (4-byte) sequence as a single UTF-16 unit instead of a
  surrogate pair, so `.Length` (UTF-16 unit count) is off by one per astral
  char (the re-encode to UTF-8 for console output is still correct).
- **Disposition**: the case avoids astral characters in the download body
  (BMP-only, then ASCII-only after D-HTTP-1). Not probed further here.
- **Suspect**: runtime string-from-utf8 decoder (astral -> surrogate pair).

## D-NET-RACE-1 -- served-count probe had an accept-thread race

- **Phenomenon**: the first mingw run FAIL'd on `served=False`: the
  assertion was read before the accept thread incremented the counter
  (increment was placed AFTER the response was sent/closed).
- **Disposition**: the counter now increments on ACCEPT (before serving),
  so by the time a client has finished a download its connection is already
  counted. Fixed in the case's mylib (case-local, not a runtime defect).
