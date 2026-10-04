// mylib.cpp -- static native library stub for the P-D3 fnptr unit-only
// fixtures (fnptrret / fnptrparam). build-fixtures.ps1 never compiles this
// (the fixtures are transpile-shape checks only); it exists so P-D4's
// per-library routing walks up and finds a STATIC mylib manifest, keeping the
// fixtures on the static direct-call path their tests assert. Symbols mirror
// the fnptrpinvoke e2e mylib (the fixtures share the same import names).

#include <cstdint>

extern "C" {

int32_t (*make_inc(void))(int32_t)
{
    return nullptr;
}

int32_t (*make_add(void))(int32_t, int32_t)
{
    return nullptr;
}

int32_t apply_cb(int32_t (*cb)(int32_t, int32_t), int32_t a, int32_t b)
{
    return cb(a, b);
}

} // extern "C"
