// byrefprobe.cpp -- native fixture for ref/out SCALAR DllImport parameters
// (tests/cases/probe-pinvoke-byref).
//
// Every entry reports the in-value the CALLEE observed and writes a KNOWN
// value back, so the oracle's stdout pins:
//   (a) `ref` keeps the managed in-value,
//   (b) `out` does NOT zero before the call (the callee sees the caller's
//       prior slot content -- net48 .locals-init zeroes at method ENTRY only),
//   (c) the exact native cell WIDTH per scalar kind (a `short*` / `byte*` /
//       `float*` callee writes 2/1/4 bytes, not a widened 4/8-byte slot).
#include <cstdint>
#include <cstring>

extern "C" {

// ref: read the in-value, then write a different one.
int32_t bump_i32(int32_t* p) { int32_t in = *p; *p = in + 1; return in; }

// out: read *p BEFORE writing (the caller's pre-call content is observable).
int32_t read_then_fill_i32(int32_t* p) { int32_t in = *p; *p = 777; return in; }

// out: write only.
void fill_i32(int32_t* p) { *p = 42; }

uint32_t bump_u32(uint32_t* p) { uint32_t in = *p; *p = in + 1u; return in; }
int64_t bump_i64(int64_t* p) { int64_t in = *p; *p = in + 1; return in; }
uint64_t bump_u64(uint64_t* p) { uint64_t in = *p; *p = in + 1u; return in; }
int32_t bump_i16(int16_t* p) { int16_t in = *p; *p = (int16_t)(in + 1); return in; }
int32_t bump_u16(uint16_t* p) { uint16_t in = *p; *p = (uint16_t)(in + 1u); return in; }
int32_t bump_u8(uint8_t* p) { uint8_t in = *p; *p = (uint8_t)(in + 1u); return in; }
int32_t bump_i8(int8_t* p) { int8_t in = *p; *p = (int8_t)(in + 1); return in; }
float bump_f32(float* p) { float in = *p; *p = in + 1.5f; return in; }
double bump_f64(double* p) { double in = *p; *p = in + 1.25; return in; }

// bool: native BOOL is 4 bytes; writing 2 (non-zero, not 1) proves whether the
// managed side normalizes the copy-back (net48 does: `b == true` is True).
int32_t set_bool2(int32_t* p) { int32_t in = *p; *p = 2; return in; }
int32_t read_then_fill_bool(int32_t* p) { int32_t in = *p; *p = 1; return in; }

// IntPtr& : same 8-byte cell as int64_t on x64.
int64_t bump_ptr(void** p) { int64_t in = (int64_t)(intptr_t)*p; *p = (void*)((intptr_t)*p + 0x10); return in; }

// Guid& : 16 bytes.
struct RawGuid { uint32_t a; uint16_t b; uint16_t c; uint8_t d[8]; };
void fill_guid(RawGuid* g) {
    g->a = 0x00112233u; g->b = 0x4455; g->c = 0x6677;
    for (int i = 0; i < 8; ++i) g->d[i] = (uint8_t)(0x88 + i);
}
int32_t read_guid_first(RawGuid* g) { return (int32_t)g->a; }

// Two byref params in one call.
int32_t add_two_i32(int32_t* a, int32_t* b) { int32_t r = *a + *b; *a += 100; *b += 200; return r; }

// byref + string (Ansi).
int32_t ref_and_string(int32_t* p, const char* s) {
    int32_t in = *p; *p = in + (int32_t)std::strlen(s); return in;
}

// byref + string (UTF-16).
int32_t ref_and_wide(int32_t* p, const wchar_t* s) {
    int32_t in = *p; int32_t n = 0; while (s[n] != 0) ++n; *p = in + n; return in;
}

} // extern "C"