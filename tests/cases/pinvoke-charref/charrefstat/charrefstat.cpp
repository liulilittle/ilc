// charrefstat.cpp -- STATIC-link fixture for `ref char` / `out char`.
//
// The generated project links this library into the exe, so the call sites use
// the transpiler's OWN extern "C" declaration (the STATIC call shape) instead
// of the runtime il2cpp_pinvoke_resolve cast charrefprobe.dll uses. Both cell
// widths are pinned here: Ansi = 1 byte, Unicode = 2 bytes.
#include <cstdint>

extern "C" {

int32_t stat_a_ref(uint8_t* p) { int32_t in = (int32_t)*p; *p = (uint8_t)0x61; return in; }
void stat_a_fill(uint8_t* p) { *p = (uint8_t)0x2D; }

int32_t stat_u_ref(uint16_t* p) { int32_t in = (int32_t)*p; *p = (uint16_t)0x0061; return in; }
void stat_u_fill(uint16_t* p) { *p = (uint16_t)0x6587; }

} // extern "C"