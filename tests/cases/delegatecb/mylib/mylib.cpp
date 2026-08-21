// mylib.cpp -- native library for the delegate-callback tests (delegatecb).
// Exported with C linkage so the transpiled import declaration binds.
//
// call_callback receives a NATIVE function pointer and invokes it -- the
// C side of the "managed delegate passed into a native function" pattern:
// the managed side marshals the delegate (GetFunctionPointerForDelegate on
// net48; icall_delegate_get_fn_ptr on this runtime) and the native side
// calls through the pointer.

#include <cstdint>

extern "C" {

// Call the caller-supplied function pointer: fn(x).
int32_t call_callback(int32_t (*fn)(int32_t), int32_t x)
{
    return fn(x);
}

} // extern "C"
