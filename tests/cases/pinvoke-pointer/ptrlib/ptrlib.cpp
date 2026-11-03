// ptrlib.cpp -- native side for the pointer P/Invoke oracle.
#include <cstdint>

extern "C" {

// Returns the pointer it was given (identity). Proves the managed pointer
// VALUE crosses the boundary unchanged.
int32_t* ptr_identity(int32_t* p) { return p; }

// Reads the pointee. Proves the pointer is a real address, not a copy.
int32_t ptr_deref(int32_t* p) { return p ? *p : -1; }

// Writes through the pointer. Proves aliasing: the managed caller must
// observe its OWN storage changing.
void ptr_store(int32_t* p, int32_t v) { if (p) *p = v; }

// Returns a pointer into a static table -- a value the managed side could
// never have produced itself.
static int32_t g_table[4] = {11, 22, 33, 44};
int32_t* ptr_table() { return g_table; }

// Pointer arithmetic on a native table: an offset pointer.
int32_t* ptr_at(int32_t* p, int32_t n) { return p + n; }

// void* round-trip: the same address expressed through an untyped pointer.
void* ptr_void_identity(void* p) { return p; }

// Reads through void* after a cast (proves the void* is not mangled).
int32_t ptr_void_deref(void* p) { return p ? *static_cast<int32_t*>(p) : -1; }

// A single int32 the managed side can point at, for a stable address probe.
int32_t g_slot = 1234;
int32_t* ptr_slot() { return &g_slot; }

} // extern "C"
