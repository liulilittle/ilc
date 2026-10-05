# GAPS.md -- uriparse recorded findings (e2e batch C80, 2026-08-13)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 67-line
byte-exact). Active probes cover the URI parsing depth: the RELATIVE throw
matrix (14 invalid inputs -- empty, bare word, ?/#, dot/dotdot relative,
digit-leading / space-containing / empty schemes, empty hosts, empty
network authority -- each verified to throw a SystemException on BOTH
runtimes), the COMPOSITION identity (Scheme + "://" + Host + AbsolutePath
== ToString and the ToString round-trip stability over 10 hierarchical
URIs), the RESERVED-ESCAPE verbatim rule (%3F/%23/%40/%3B/%2F stay escaped
in both AbsolutePath and ToString), and the OPAQUE forms (urn: with a
reserved escape, mailto: userinfo@host split, custom: rest-verbatim,
network-path "//host" -> file scheme). Gaps below are RECORDED, NOT FIXED
(this batch creates test cases only).

## D-UP-1 -- net48 unescapes UNRESERVED escapes, ILC keeps the text verbatim

- **Phenomenon**: net48 canonicalizes unreserved escape sequences (space
  %20, ~ %7E, A %41, '-' %2d) -- `new Uri("https://h/a%20b")` reports
  AbsolutePath "/a%20b" but ToString "https://h/a b", and
  `"https://h/%7Eusr"` -> AbsolutePath "/~usr"; the ILC domain (versionuri.h)
  does NO escape handling and keeps every byte verbatim in both accessors.
  These probes would diverge byte-for-byte, so they are EXCLUDED from the
  active surface and recorded here instead.
- **Confirmed**: only RESERVED escapes (%3F/%23/%40/%3B/%2F) are probed --
  net48 keeps THOSE escaped in both AbsolutePath and ToString, matching
  ILC's verbatim rule (all 15 escape probes pass). Unreserved escapes are
  parked.
- **Suspected root file**: `src/runtime/src/versionuri.cpp`
  icall_uri_ctor/UriAsciiFromManaged (no unescape pass -- documented
  simplification; the divergence is a net48 canonicalization the domain
  does not implement).

## D-UP-2 -- net48 rejects "://"-less scheme parts that ILC parses as opaque

- **Phenomenon**: `new Uri("http:")` and `new Uri("http:/x")` THROW on
  net48 (a non-"://" scheme with a "/"-leading or empty part is not a
  valid opaque URI), while ILC's grammar (versionuri.h) accepts ANY
  "scheme:rest" opaque form whose scheme is RFC-valid. Similarly
  `new Uri("a:b")` (single-letter scheme) throws on net48 (DOS-drive
  interpretation) but is accepted by ILC. These three are excluded from the
  relative-throw matrix (the matrix keeps the 14 inputs where both runtimes
  agree).
- **Confirmed**: the 14 relative throws + null probe all pass byte-exact on
  both runtimes; the divergent forms are recorded here.
- **Suspected root file**: `src/runtime/src/versionuri.cpp`
  il2cpp_uri_try_create (opaque-form acceptance is broader than net48).
