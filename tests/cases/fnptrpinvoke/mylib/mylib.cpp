// mylib.cpp -- native library for the fnptrpinvoke e2e case (P-D3 P0/P1).
// Exported with C linkage so the transpiled import declarations bind.
//
// make_inc / make_add RETURN function pointers to the native side's callers
// (the managed DllImport declares the return as delegate* unmanaged[Cdecl]).
// apply2 receives a function pointer VALUE and INVOKES it -- the native side
// of the "fnptr value passed back into native code" pattern.

#include <cstdint>

extern "C" {

static int32_t _inc_impl(int32_t x)
{
    return x + 1;
}

static int32_t _add_impl(int32_t a, int32_t b)
{
    return a + b;
}

// Return a pointer to the unary increment function.
int32_t (*make_inc(void))(int32_t)
{
    return &_inc_impl;
}

// Return a pointer to the binary add function.
int32_t (*make_add(void))(int32_t, int32_t)
{
    return &_add_impl;
}

// Call the caller-supplied function pointer: cb(a, b).
int32_t apply2(int32_t (*cb)(int32_t, int32_t), int32_t a, int32_t b)
{
    return cb(a, b);
}

} // extern "C"