// charrefprobe.cpp -- native fixture for `ref char` / `out char` DllImport
// parameters (tests/cases/pinvoke-charref).
//
// MEASURED net48 contract (Framework64 v4.0.30319, host CP_ACP=936 zh-CN):
// the CHAR SET picks the native cell --
//   * CharSet.Ansi     -> a 1-BYTE cell holding the SYSTEM ANSI code page
//     image. U+4E2D arrives as 0xD6 (the CP936 lead byte); a lone 0xD6
//     decodes back to '?' (0x003F); 0xFF decodes to 0xF8F5.
//   * CharSet.Unicode/Auto -> a 2-byte UTF-16 code unit, NO conversion.
// Therefore the Ansi entries take `uint8_t*` and the Unicode entries take
// `uint16_t*` (NOT char*, NOT wchar_t*).
//
// Every writer takes ONLY the cell pointer: no second parameter whose own
// marshalling width could confound the measurement.
#include <cstdint>

extern "C" {

// ---- Ansi: 1-byte CP_ACP cell ------------------------------------------------
// ref: report the byte the callee sees, DO NOT write (the copy-back still
// round-trips through CP_ACP, so a non-ASCII unit does NOT keep its value).
int32_t a_read8(uint8_t* p) { return (int32_t)*p; }
// ref: report the in-byte, write a known ASCII byte.
int32_t a_ref8(uint8_t* p) { int32_t in = (int32_t)*p; *p = (uint8_t)0x5A; return in; }
// out: report the pre-call slot byte, write a known ASCII byte.
int32_t a_out8(uint8_t* p) { int32_t in = (int32_t)*p; *p = (uint8_t)0x79; return in; }
// out: write-only fixed bytes (ASCII / DBCS lead byte / invalid / NUL).
void a_fill8_41(uint8_t* p) { *p = (uint8_t)0x41; }
void a_fill8_d6(uint8_t* p) { *p = (uint8_t)0xD6; }
void a_fill8_ff(uint8_t* p) { *p = (uint8_t)0xFF; }
void a_fill8_00(uint8_t* p) { *p = (uint8_t)0x00; }
// byref char + Ansi string in one call (the STRING CharSet must not change the
// char cell).
int32_t a_pair(uint8_t* p, const char* s) {
    int32_t in = (int32_t)*p; *p = (uint8_t)0x42; return in + (int32_t)s[0];
}
// MULTI-PARAMETER with an `out`: the Seq projection must be independent of the
// parameter count (a return row shifts only if it is indexed positionally).
// Reports the pre-call cell byte and writes a different one; `int32_t*` is the
// ordinary 4-byte byref neighbour.
int32_t a_pair_out(uint8_t* p, int32_t* q) {
    int32_t in = (int32_t)*p; *p = (uint8_t)0x43; *q = 0x1234; return in;
}

// ---- Unicode / Auto: 2-byte UTF-16 cell (no conversion) ---------------------
int32_t u_read16(uint16_t* p) { return (int32_t)*p; }
int32_t u_ref16(uint16_t* p) { int32_t in = (int32_t)*p; *p = (uint16_t)0x005A; return in; }
int32_t u_out16(uint16_t* p) { int32_t in = (int32_t)*p; *p = (uint16_t)0x6587; return in; }
void u_fill16_4e2d(uint16_t* p) { *p = (uint16_t)0x4E2D; }
void u_fill16_ffff(uint16_t* p) { *p = (uint16_t)0xFFFF; }
void u_fill16_0000(uint16_t* p) { *p = (uint16_t)0x0000; }

} // extern "C"