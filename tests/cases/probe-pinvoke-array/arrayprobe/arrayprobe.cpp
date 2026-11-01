// arrayprobe.cpp -- native fixture for managed ARRAY DllImport parameters
// (tests/cases/probe-pinvoke-array).
//
// Every entry reads/writes the raw buffer the marshal stub hands over, so the
// net48 oracle's stdout pins the exact pointer semantics:
//   * which bytes the CALLEE observes (copy-in happened or the array is
//     pinned to its own storage),
//   * which bytes the MANAGED side observes after the call (copy-out),
//   * the element width (char[] ANSI vs UTF-16),
//   * the null-array and zero-length-array pointer values.
#include <cstdint>
#include <cstring>

extern "C" {

// --- read the first byte/element the callee receives. ---------------------
int32_t byte0(const uint8_t* p) { return p == nullptr ? -1 : (int32_t)p[0]; }
int32_t int0(const int32_t* p) { return p == nullptr ? -1 : p[0]; }
int64_t long0(const int64_t* p) { return p == nullptr ? -1 : p[0]; }
int32_t short0(const int16_t* p) { return p == nullptr ? -1 : (int32_t)p[0]; }

// --- write a known element, leave the rest untouched. --------------------
void set_byte(uint8_t* p, int32_t idx, int32_t v) { p[idx] = (uint8_t)v; }
void set_int(int32_t* p, int32_t idx, int32_t v) { p[idx] = v; }
void set_long(int64_t* p, int32_t idx, int64_t v) { p[idx] = v; }
void set_short(int16_t* p, int32_t idx, int32_t v) { p[idx] = (int16_t)v; }

// --- element-width probe: the low 4 bytes of the buffer, little-endian. --
int32_t first4(const void* p) {
    if (p == nullptr) return -1;
    const uint8_t* b = (const uint8_t*)p;
    return (int32_t)((uint32_t)b[0] | ((uint32_t)b[1] << 8) | ((uint32_t)b[2] << 16) | ((uint32_t)b[3] << 24));
}

// --- primitive probes for float / IntPtr / Guid elements -------------------
float f0(const float* p) { return p == nullptr ? -1.0f : p[0]; }
void set_f(float* p, int32_t idx, float v) { p[idx] = v; }
int64_t ip0(const int64_t* p) { return p == nullptr ? -1 : p[0]; }
void set_ip(int64_t* p, int32_t idx, int64_t v) { p[idx] = v; }
int32_t guid_byte0(const uint8_t* p) { return p == nullptr ? -1 : (int32_t)p[0]; }
void guid_set_byte(uint8_t* p, int32_t off, int32_t v) { p[off] = (uint8_t)v; }

// --- struct array (4 x int32 per element) ---------------------------------
int32_t rect_sum(const int32_t* p, int32_t n) {
    if (p == nullptr) return -1;
    int32_t s = 0;
    for (int32_t i = 0; i < n * 4; ++i) s += p[i];
    return s;
}
void rect_set_first_left(int32_t* p, int32_t v) { p[0] = v; }

// --- pointer identity / null / zero-length --------------------------------
int32_t is_null(const void* p) { return p == nullptr ? 1 : 0; }
int32_t same_ptr(const void* a, const void* b) { return a == b ? 1 : 0; }

// --- GC between two writes (the callback collects managed memory) --------
// Sequence: read in[0], write out[0]=0x77, call cb(), write out[1]=0x88.
// A marshal model that hands over a COPY must copy back from the REFRESHED
// array after the callback; a pinned model writes the array in place.
int32_t gc_then_write(uint8_t* p, void (*cb)(void)) {
    int32_t in = p[0];
    p[0] = 0x77;
    cb();
    p[1] = 0x88;
    return in;
}

} // extern "C"
