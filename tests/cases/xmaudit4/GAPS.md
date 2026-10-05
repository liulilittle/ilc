# GAPS — xmaudit4 (XmlWriter empty-element shape + use-after-Close)

Status: permanent DIVERGENCE PIN / CRASH. e2e-frozen2.sh: transpile/build/run
PASS on both compilers; diff FAIL; runtime exits 0xC0000374.

## Observed (net48 expected.txt vs runtime)

- P1 empty element: net48 `<?xml version="1.0" encoding="utf-16"?><a />`
  (self-closing WITH a space); runtime code path emits `<a></a>`
  (xml_write.cpp:280-287). The runtime P1 byte shape is code-verified; the
  e2e run's stdout is lost to the P2 crash (see below).
- P2 second `Close()`: net48 idempotent -> `p2=none`; runtime
  `il2cpp_xml_writer_close` (xml_write.cpp:327-341) `delete`s the writer
  struct with no idempotency guard -> the second call is a use-after-free /
  double free.
- P3 `WriteStartElement` after Close: net48 -> `InvalidOperationException`
  (NOT ObjectDisposedException -- oracle-pinned); runtime never reached.
- Runtime outcome: `ilc.out` EMPTY (sha e3b0c44298fc1c14 = empty), process
  exit code -1073740940 = 0xC0000374 (STATUS_HEAP_CORRUPTION) on BOTH
  mingw and msvc -- the double Close corrupts the heap.

## Fix candidates (not fixed here)

- Render no-content end tags as `<name />` (net48 shape).
- Make Close idempotent (guard/null the handle) and/or throw
  InvalidOperationException on use-after-Close, matching net48.
