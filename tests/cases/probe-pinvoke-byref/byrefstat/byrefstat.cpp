// byrefstat.cpp -- STATIC-link fixture for ref/out SCALAR DllImport parameters.
//
// The generated project links this library into the exe, so the call sites use
// the transpiler's OWN extern "C" declaration (the STATIC call shape) instead
// of the runtime il2cpp_pinvoke_resolve cast byrefprobe.dll uses. The same
// ref/out semantics must hold on both shapes.
#include <cstdint>

extern "C" {

int32_t stat_bump_i32(int32_t* p) { int32_t in = *p; *p = in + 1; return in; }

int32_t stat_bump_i16(int16_t* p) { int16_t in = *p; *p = (int16_t)(in + 1); return in; }

float stat_bump_f32(float* p) { float in = *p; *p = in + 1.5f; return in; }

struct RawGuid { uint32_t a; uint16_t b; uint16_t c; uint8_t d[8]; };
void stat_fill_guid(RawGuid* g) {
    g->a = 0x00112233u; g->b = 0x4455; g->c = 0x6677;
    for (int i = 0; i < 8; ++i) g->d[i] = (uint8_t)(0x88 + i);
}

} // extern "C"