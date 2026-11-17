//------------------------------------------------------------------------------
// exception_synth.cpp -- lazily synthesised class descriptors for exceptions
// that the runtime does not model with a static descriptor.
//
// The runtime registers static descriptors for only five exceptions
// (metadata.cpp:823-829: Exception / SystemException / ArgumentException /
// ArgumentNullException / ArgumentOutOfRangeException). Every other exception
// type -- ThreadStateException, InvalidOperationException, IndexOutOfRange
// Exception, FormatException, and any user-declared one -- has no descriptor at
// all, so a caught exception cannot be represented as an object that answers
// isinst/GetType correctly.
//
// This module synthesises those descriptors on demand from the throw-time type
// chain ("Type,Type,Base1,Base2,..." -- see CodeGenerator.ExceptionChain). The
// result is a real Il2CppClass whose name is the managed full name and whose
// baseClass is the descriptor of the nearest modelled ancestor, so
//   il2cpp_is_instance(carrier, &AnyExceptionClass)  -> true
//   carrier->klass->name                             -> the thrown type name
//
// SYNTHESIS ORDER is base-first: a descriptor's baseClass must already exist,
// so the chain is walked from its TAIL. Walking from the head would ask for a
// base that is not there yet, and a naive "chain[1] is the base" read is wrong
// outright -- ExceptionChain repeats the thrown type as segment 1 (its own
// documented layout), so segment 1 is the type itself, not its parent.
//
// CACHING: a fixed-size table, separate from the 256-slot global registry, so
// synthesised exceptions cannot push real types out of it. Name comparison is
// by contents (the transpiler emits identical literals per translation unit).
//------------------------------------------------------------------------------

#include "il2cpp_runtime.h"
#include "il2cpp_metadata.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace il2cpp {

// The synthesised descriptors are internal to this translation unit group;
// only il2cpp_exception_class_by_name is part of the runtime contract and it
// lives at il2cpp:: scope (see il2cpp_runtime.h).
namespace {


// Capacity: the C# exception hierarchy is wide but shallow; 64 synthetic
// descriptors is far more than any single program throws. Overflow is a hard
// error rather than a silent nullptr -- a silently missing descriptor would
// turn isinst into a wrong answer.
constexpr int32_t kMaxSynthClasses = 64;

Il2CppClass* g_synthClasses[kMaxSynthClasses];
int32_t g_synthClassCount = 0;

// The descriptor for a modelled ancestor, by the type name the chain carries.
// Returns nullptr when the name is not modelled (the caller then falls back to
// System_Exception_class, the universal root).
Il2CppClass* ModelledAncestor(const char* name)
{
    if (name == nullptr)
    {
        return nullptr;
    }
    if (std::strcmp(name, "System.Exception") == 0)
    {
        return &System_Exception_class;
    }
    if (std::strcmp(name, "System.SystemException") == 0)
    {
        return &System_SystemException_class;
    }
    if (std::strcmp(name, "System.ArgumentException") == 0)
    {
        return &System_ArgumentException_class;
    }
    if (std::strcmp(name, "System.ArgumentNullException") == 0)
    {
        return &System_ArgumentNullException_class;
    }
    if (std::strcmp(name, "System.ArgumentOutOfRangeException") == 0)
    {
        return &System_ArgumentOutOfRangeException_class;
    }
    return nullptr;
}

// Copy one comma-separated segment [begin, end) into a static buffer. The
// descriptors outlive every throw, so the name storage must be permanent; a
// small pool is enough because each distinct exception type is synthesised once.
constexpr int32_t kNamePoolBytes = 8192;
char g_namePool[kNamePoolBytes];
int32_t g_namePoolUsed = 0;

const char* InternName(const char* begin, const char* end)
{
    const size_t len = static_cast<size_t>(end - begin);
    if (len == 0 || len >= static_cast<size_t>(kNamePoolBytes) ||
        g_namePoolUsed + static_cast<int32_t>(len) + 1 > kNamePoolBytes)
    {
        std::fprintf(stderr, "exception_synth: class name pool exhausted\n");
        std::abort();
    }
    char* dst = g_namePool + g_namePoolUsed;
    std::memcpy(dst, begin, len);
    dst[len] = 0;
    g_namePoolUsed += static_cast<int32_t>(len) + 1;
    return dst;
}

// Find an already-synthesised descriptor by contents.
Il2CppClass* FindSynth(const char* name)
{
    for (int32_t i = 0; i < g_synthClassCount; ++i)
    {
        if (std::strcmp(g_synthClasses[i]->name, name) == 0)
        {
                        return g_synthClasses[i];
        }
    }
    return nullptr;
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_exception_class_lookup -- descriptor for 'name' if one already exists.
//
// Lookup only: a type the program has actually thrown answers its synthesised
// descriptor. Anything else is nullptr, which lets a conformance test
// (isinst/castclass) answer "no" without creating a descriptor for a type the
// program never threw.
//
// The static five are DELIBERATELY not consulted by name here: an exact-name
// match against them would answer a static descriptor for the modelled type
// itself, which is right, but ModelledAncestor is a NAME lookup that the
// synthesis path uses to mean "already has a descriptor anywhere in the chain".
// For a conformance test only the exact type matters, so this walks its own
// table and never widens the match.
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_exception_class_lookup(const char* name)
{
    if (name == nullptr)
    {
        return nullptr;
    }
    Il2CppClass* cached = FindSynth(name);
    if (cached != nullptr)
    {
        return cached;
    }
    if (std::strcmp(name, "System.Exception") == 0)
    {
        return &System_Exception_class;
    }
    if (std::strcmp(name, "System.SystemException") == 0)
    {
        return &System_SystemException_class;
    }
    if (std::strcmp(name, "System.ArgumentException") == 0)
    {
        return &System_ArgumentException_class;
    }
    if (std::strcmp(name, "System.ArgumentNullException") == 0)
    {
        return &System_ArgumentNullException_class;
    }
    if (std::strcmp(name, "System.ArgumentOutOfRangeException") == 0)
    {
        return &System_ArgumentOutOfRangeException_class;
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// il2cpp_exception_class_by_name -- descriptor for 'name', synthesising it when
// the runtime has no static one.
//
// 'chain' is the throw-time type chain ("Type,Type,Base1,..."); it supplies the
// base for a descriptor that has to be created. A null chain, or a name that is
// not in the chain, still resolves: the base then falls back to the nearest
// modelled ancestor of the name itself (System.Exception at worst).
//
// Returns nullptr only for a null name. Never aborts on an unknown type: the
// universal root always exists, so a caught exception can always be represented.
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_exception_class_by_name(const char* name, const char* chain)
{
    if (name == nullptr)
    {
        return nullptr;
    }
    // A modelled exception keeps its static descriptor.
    if (Il2CppClass* modelled = ModelledAncestor(name))
    {
        return modelled;
    }
    // Already synthesised.
    if (Il2CppClass* cached = FindSynth(name))
    {
        return cached;
    }

    // Gather the base names from the chain. The chain's layout repeats the
    // thrown type as its second segment, so the segments are scanned for the
    // first entry that names a DIFFERENT type -- that is the parent.
    const char* baseName = nullptr;
    const char* baseRest = nullptr; // the chain SUFFIX starting at baseName
    size_t baseNameLen = 0;
    if (chain != nullptr)
    {
        const char* p = chain;
        while (*p != '\0')
        {
            const char* comma = std::strchr(p, ',');
            const size_t len = (comma != nullptr) ? static_cast<size_t>(comma - p) : std::strlen(p);
            // Skip the segments that repeat the thrown type itself and the
            // ones that are empty.
            if (len > 0 && !(len == std::strlen(name) && std::strncmp(p, name, len) == 0))
            {
                baseName = p;
                baseNameLen = len;
                baseRest = p;
                break;
            }
            if (comma == nullptr)
            {
                break;
            }
            p = comma + 1;
        }
    }

    // Resolve the base descriptor, synthesising it first when it is itself an
    // unmodelled exception.
    //
    // The recursion passes 'baseRest' -- the chain SUFFIX that begins at the
    // base's own name -- not the whole chain. Passing the whole chain makes the
    // child's name scan past its own repetition, land on the PARENT segment and
    // recurse back into the parent, which scans forward to the child again: an
    // unbounded mutual recursion that overflows the stack. With the suffix the
    // pair (name, chain position) advances strictly forward on every call, so
    // termination is structural rather than guarded.
    Il2CppClass* base = nullptr;
    if (baseName != nullptr)
    {
        char baseBuf[512];
        const size_t n = baseNameLen < sizeof(baseBuf) - 1 ? baseNameLen : sizeof(baseBuf) - 1;
        std::memcpy(baseBuf, baseName, n);
        baseBuf[n] = 0;
        base = il2cpp_exception_class_by_name(baseBuf, baseRest);
    }
    if (base == nullptr)
    {
        base = &System_Exception_class;
    }

    // Allocate the descriptor and its name copy.
    if (g_synthClassCount >= kMaxSynthClasses)
    {
        std::fprintf(stderr, "exception_synth: descriptor table overflow (%d types)\n", kMaxSynthClasses);
        std::abort();
    }
    Il2CppClass* klass = static_cast<Il2CppClass*>(std::calloc(1, sizeof(Il2CppClass)));
    if (klass == nullptr)
    {
        std::abort();
    }
    klass->name = InternName(name, name + std::strlen(name));
    klass->instanceSize = static_cast<int32_t>(sizeof(Il2CppObject));
    klass->refFieldBitmap = 0u;
    klass->refFieldCount = 0;
    klass->baseClass = base;
    klass->vtable = nullptr;
    klass->vtableSize = 0;
    klass->interfaces = nullptr;
    klass->interfaceCount = 0;
    g_synthClasses[g_synthClassCount++] = klass;
    return klass;
}

} // namespace il2cpp
