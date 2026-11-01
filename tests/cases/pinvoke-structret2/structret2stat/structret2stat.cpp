//------------------------------------------------------------------------------
// structret2stat.cpp -- STATIC-link native fixture for the by-value struct
// return work (tests/cases/pinvoke-structret2).
//
// The static-direct P/Invoke path declares the extern "C" prototype from the
// method signature and links this archive into il2cpp_gen_exe. Every function
// returns a KNOWN aggregate so the net48 oracle and the transpiled runtime must
// print the SAME field bytes:
//   * Ret2Pair  { int32, int32 }  8-byte aggregate
//   * Ret2Wide  { int64 }         8-byte aggregate, full 64-bit payload
//   * Ret2Mixed { int32, int64 }  16-byte aggregate (padding after the int32)
// The mode entry returns the int32 backing of the managed enum (the CLR
// marshals an enum return through its underlying int32).
//------------------------------------------------------------------------------
#include <cstdint>

extern "C" {

struct Ret2Pair
{
    int32_t x;
    int32_t y;
};

struct Ret2Wide
{
    int64_t v;
};

struct Ret2Mixed
{
    int32_t tag;
    int64_t big;
};

Ret2Pair mk_pair(int32_t seed)
{
    Ret2Pair p = {seed, seed * 2 + 1};
    return p;
}

Ret2Wide mk_wide(int64_t seed)
{
    Ret2Wide w = {seed};
    return w;
}

Ret2Mixed mk_mixed(int32_t seed)
{
    Ret2Mixed m = {seed, static_cast<int64_t>(seed) * 0x0102030405LL};
    return m;
}

// Enum return: the native ABI is the int32 backing.
int32_t mk_mode(int32_t seed)
{
    return seed;
}

int32_t sum_pair(Ret2Pair p)
{
    return p.x * 1000 + p.y;
}

int64_t sum_wide(Ret2Wide w)
{
    return w.v ^ 0x1122334455667788LL;
}

int64_t sum_mixed(Ret2Mixed m)
{
    return static_cast<int64_t>(m.tag) * 1000000 + (m.big & 0x7fffffff);
}

} // extern "C"
