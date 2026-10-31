// probehandleref.cpp -- native library for the HandleRef P/Invoke probe.
//
// The generated extern "C" declarations name the HandleRef parameter as
// intptr_t (the managed handle decay); these definitions keep the pointer
// spelling -- same width, same ABI, so the linked call binds and the native
// side observes the handle bit pattern the CLR would pass.

#include <cstdint>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
#endif

extern "C" {

// F(HandleRef, int, HandleRef) -- the ImageList_Add parameter shape.
// Returns 1 only when both handles are identical and non-null and the scalar
// sits in its own positional slot (7).
int32_t probe_mixed(void* first, int32_t tag, void* second)
{
    return (first != nullptr && first == second && tag == 7) ? 1 : 0;
}

// F(HandleRef, fnptr) -- invokes the managed callback while the native call
// is still on the stack; the callback forces a full GC.
int32_t probe_keepalive(void* handle, int32_t (*callback)())
{
    (void)handle;
    return callback();
}

} // extern "C"
