// mylib.cpp -- native library for the delegategdfp case: a native function
// that the managed side wraps with GetDelegateForFunctionPointer<T>, plus an
// address-returning helper (the managed side has no &triple_fn operator).
#include <cstdint>

extern "C" {

int32_t triple_fn(int32_t x)
{
    return x * 3;
}

void* triple_addr(void)
{
    return reinterpret_cast<void*>(&triple_fn);
}

} // extern "C"