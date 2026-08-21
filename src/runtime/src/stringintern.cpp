//------------------------------------------------------------------------------
// stringintern.cpp -- string literal interning (see il2cpp_string_intern.h).
//
// The intern pool is an open-addressing hash table stored in a MANAGED
// reference array (Il2CppObject*[], "[System.Object" descriptor): the
// collector scans the payload element-wise (gc.cpp ScanObject, '[' prefix +
// isRefElem), so every interned string stays reachable from the table alone.
// The table pointer is a C++ static global registered as a GC static root --
// the pool is immortal (.NET intern pool semantics).
//
// GC discipline (physical contract -- see il2cpp_gc.h):
//   * il2cpp_gc_alloc_size can trigger a collection (collect-then-retry).
//     Between any allocation and the next use of a table pointer, the table
//     address is re-read from the global: Phase C of a compacting collection
//     rewrites root slots, never C++ locals.
//   * INSERT-THEN-GROW ordering: a newly interned string is written into the
//     current table FIRST (a pure pointer store, no allocation); only then
//     may a grow allocate a fresh table. A collection inside the grow can
//     therefore never reclaim the not-yet-published string -- it is already
//     rooted by the old table.
//   * Every reference-slot store goes through il2cpp_write_barrier first
//     (old-gen storing object -> card marked) -- the same convention as
//     arrayext2/hashtable. Stores into the global root itself are plain
//     assignments (a C++ global is not a managed slot; the root is scanned
//     directly by mark and rewritten by Phase C).
//------------------------------------------------------------------------------

#include "il2cpp_string_intern.h"

#include "il2cpp_array.h"
#include "il2cpp_gc.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>

namespace il2cpp {

namespace {

constexpr int32_t kInitialSlots = 64;    // power of two (mask arithmetic)
constexpr int32_t kMaxSlots = 1 << 20;   // capacity cap -- overflow reports loudly

// The intern table: a managed reference array used as an open-addressing
// hash table (null slot = empty). Registered as a GC static root on first
// use; every interned string is reachable from it and thus immortal.
//
// THREAD SAFETY (net48 parity): String.Intern is documented thread-safe, and
// this runtime spawns real OS threads (threadobj Start, threadpool workers)
// whose generated callbacks can reach the pool through ldstr
// (il2cpp_string_intern_literal) and String.Intern (il2cpp_string_intern) --
// including a concurrent GROW while another thread probes. Every public entry
// point therefore runs under the recursive mutex below (one lock covers
// find/insert/grow/root).
//
// RECURSIVE by design: the two entry points nest (intern_literal delegates to
// intern for its null input), and a pool operation may ALLOCATE (grow's
// il2cpp_array_new, the create-on-miss path) -- a collection triggered by
// that allocation runs finalizers on the SAME thread, and a finalizer may
// itself call intern. A plain mutex would deadlock on either re-entry; a
// recursive mutex makes them safe.
//
// Lock-order analysis (D11): the pool mutex is the ONLY lock this domain
// takes, and it is never held while acquiring another domain's lock. The GC
// does not take it (the collector scans the pool's static root directly), so
// no ordering cycle exists -- holding it across a triggered collection is
// safe (the collecting thread already owns the pool). Pairing (1:1): every
// acquire is a scoped lock_guard that releases on scope exit; there is no
// manual unlock anywhere.
Il2CppArray* g_internSlots = nullptr;
bool         g_internRooted = false;
int32_t      g_internCount = 0; // live entries (for the load-factor policy)
std::recursive_mutex g_internMutex; // serializes every pool operation

Il2CppObject** SlotPtr(Il2CppArray* table, int32_t index)
{
    return reinterpret_cast<Il2CppObject**>(table->data) + index;
}

// Ordinal content hash: FNV-1a over UTF-16 units + avalanche finalizer.
// The finalizer is REQUIRED: plain FNV over 16-bit units leaves the low
// index bits dominated by the last unit (multiplication only mixes upward),
// so a slot mask would collide all strings sharing a final character. The
// mixer spreads every unit over the low bits used by the mask.
uint32_t HashChars(const char16_t* chars, int32_t length)
{
    uint32_t h = 2166136261u; // FNV-1a offset basis
    for (int32_t i = 0; i < length; ++i)
    {
        h ^= static_cast<uint32_t>(chars[i]);
        h *= 16777619u; // FNV-1a prime
    }
    h ^= h >> 16;
    h *= 0x85EBCA6Bu;
    h ^= h >> 13;
    h *= 0xC2B2AE35u;
    h ^= h >> 16;
    return h;
}

// ASCII fast-path hash: FNV-1a over the raw BYTES. For pure-ASCII content
// this produces exactly HashChars over the created string's UTF-16 units
// (each ASCII byte b becomes unit b with high byte 0, and the step h ^= b is
// identical to h ^= unit for b < 0x80) -- so the fast-path probe and the
// insert probe always agree on the bucket. The finalizer matches HashChars.
uint32_t HashAsciiBytes(const char* ascii, int32_t length)
{
    uint32_t h = 2166136261u;
    for (int32_t i = 0; i < length; ++i)
    {
        h ^= static_cast<uint32_t>(static_cast<unsigned char>(ascii[i]));
        h *= 16777619u;
    }
    h ^= h >> 16;
    h *= 0x85EBCA6Bu;
    h ^= h >> 13;
    h *= 0xC2B2AE35u;
    h ^= h >> 16;
    return h;
}

// Exact ordinal comparison of a pool entry against pure-ASCII content
// (byte i <-> UTF-16 unit i, values identical for ASCII -- no reinterpret
// tricks, no byte pairing, so it can never falsely match).
bool AsciiMatches(const Il2CppString* e, const char* ascii, int32_t length)
{
    if (e->length != length)
    {
        return false;
    }
    for (int32_t i = 0; i < length; ++i)
    {
        if (e->chars[i] != static_cast<char16_t>(static_cast<unsigned char>(ascii[i])))
        {
            return false;
        }
    }
    return true;
}

// Register the table as a GC root and allocate it. The allocation may
// collect; nothing is interned yet and the (null) root slot is safe.
void EnsureInternTable()
{
    if (g_internRooted)
    {
        return;
    }
    il2cpp_gc_add_static_root(reinterpret_cast<void**>(&g_internSlots));
    g_internRooted = true;
    g_internSlots = reinterpret_cast<Il2CppArray*>(
        il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, kInitialSlots));
}

// Probe the table for an entry with the given ordinal content. The table is
// read from the global once: this function performs NO allocation, so no
// collection can move the table while it runs.
Il2CppObject* InternFind(const char16_t* chars, int32_t length)
{
    Il2CppArray* table = g_internSlots;
    const int32_t mask = table->length - 1;
    uint32_t idx = HashChars(chars, length) & static_cast<uint32_t>(mask);
    for (;;)
    {
        Il2CppObject* entry = *SlotPtr(table, static_cast<int32_t>(idx));
        if (entry == nullptr)
        {
            return nullptr; // empty slot: content is not interned
        }
        const Il2CppString* e = reinterpret_cast<const Il2CppString*>(entry);
        if (e->length == length &&
            (length == 0 || std::memcmp(e->chars, chars, static_cast<size_t>(length) * sizeof(char16_t)) == 0))
        {
            return entry;
        }
        idx = (idx + 1) & static_cast<uint32_t>(mask);
    }
}

// Grow the table by doubling (capped). The new-array allocation may collect:
// the old table and every interned string are already rooted via the static
// root, and the old table pointer is RE-READ from the global afterwards
// (Phase C may have moved it). The rehash itself performs no allocation, so
// both table pointers stay valid for its duration. At the cap with the table
// full, reports loudly and aborts (never silently stops interning).
void GrowInternTable()
{
    const int32_t oldSize = g_internSlots->length;
    if (oldSize >= kMaxSlots)
    {
        std::fprintf(stderr,
                     "il2cpp string intern pool exhausted: %d distinct interned "
                     "strings exceed the %d-slot capacity cap\n",
                     g_internCount, kMaxSlots);
        std::abort();
    }
    const int32_t newSize = oldSize * 2;
    Il2CppArray* fresh = reinterpret_cast<Il2CppArray*>(
        il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, newSize));
    Il2CppArray* old = g_internSlots; // re-read AFTER the allocation
    for (int32_t i = 0; i < oldSize; ++i)
    {
        Il2CppObject* entry = *SlotPtr(old, i);
        if (entry == nullptr)
        {
            continue;
        }
        const Il2CppString* e = reinterpret_cast<const Il2CppString*>(entry);
        const int32_t mask = fresh->length - 1;
        uint32_t idx = HashChars(e->chars, e->length) & static_cast<uint32_t>(mask);
        for (;;)
        {
            Il2CppObject** slot = SlotPtr(fresh, static_cast<int32_t>(idx));
            if (*slot == nullptr)
            {
                il2cpp_write_barrier(reinterpret_cast<void**>(slot), entry);
                *slot = entry;
                break;
            }
            idx = (idx + 1) & static_cast<uint32_t>(mask);
        }
    }
    // Publish. g_internSlots is a C++ global (GC root), NOT a managed slot:
    // a plain assignment -- no write barrier (mark reads the root directly,
    // Phase C rewrites it).
    g_internSlots = fresh;
}

// Insert 'str' into the table; returns the pool instance for its content.
//   hit  -> the existing pool entry
//   miss -> 'str' itself, after insertion (strong reference)
Il2CppObject* InternInsert(Il2CppString* str)
{
    EnsureInternTable();
    Il2CppArray* table = g_internSlots; // snapshot: no allocation before the store
    const int32_t mask = table->length - 1;
    uint32_t idx = HashChars(str->chars, str->length) & static_cast<uint32_t>(mask);
    for (;;)
    {
        Il2CppObject** slot = SlotPtr(table, static_cast<int32_t>(idx));
        Il2CppObject* entry = *slot;
        if (entry == nullptr)
        {
            // INSERT FIRST (pure store, no allocation): the string becomes
            // rooted the moment it is in the table, so a collection inside a
            // later grow can never reclaim it.
            il2cpp_write_barrier(reinterpret_cast<void**>(slot), reinterpret_cast<Il2CppObject*>(str));
            *slot = reinterpret_cast<Il2CppObject*>(str);
            ++g_internCount;
            if (g_internCount * 100 > table->length * 70)
            {
                GrowInternTable();
            }
            return reinterpret_cast<Il2CppObject*>(str);
        }
        const Il2CppString* e = reinterpret_cast<const Il2CppString*>(entry);
        if (e->length == str->length &&
            (str->length == 0 ||
             std::memcmp(e->chars, str->chars, static_cast<size_t>(str->length) * sizeof(char16_t)) == 0))
        {
            return entry;
        }
        idx = (idx + 1) & static_cast<uint32_t>(mask);
    }
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_string_is_interned -- check if a string is in the intern pool
// WITHOUT adding it. Returns the interned string if found, nullptr if not.
// (.NET String.IsInterned semantics: returns null for non-interned strings.)
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_string_is_interned(Il2CppObject* str)
{
    if (str == nullptr)
    {
        return nullptr;
    }
    if (str->klass != il2cpp_string_class())
    {
        return nullptr; // defensive: content lookup requires System.String
    }
    std::lock_guard<std::recursive_mutex> lock(g_internMutex);
    EnsureInternTable();
    Il2CppString* s = reinterpret_cast<Il2CppString*>(str);
    return InternFind(s->chars, s->length);
}

// il2cpp_string_intern -- intern an existing string (see header for contract).
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_string_intern(Il2CppObject* str)
{
    if (str == nullptr)
    {
        return nullptr; // defensive (header contract)
    }
    if (str->klass != il2cpp_string_class())
    {
        return str; // defensive: content lookup requires System.String
    }
    std::lock_guard<std::recursive_mutex> lock(g_internMutex);
    EnsureInternTable();
    Il2CppString* s = reinterpret_cast<Il2CppString*>(str);
    Il2CppObject* hit = InternFind(s->chars, s->length);
    if (hit != nullptr)
    {
        return hit;
    }
    return InternInsert(s);
}

//------------------------------------------------------------------------------
// il2cpp_string_intern_literal -- ldstr path (see header for contract).
//
// Lookup FIRST (no allocation on a hit): pure-ASCII input is probed with an
// exact byte-wise hash + comparison (HashAsciiBytes / AsciiMatches -- for
// ASCII, byte i and UTF-16 unit i are the same value, so the probe key is
// faithful to the created string; no reinterpretation, no allocation).
// Non-ASCII input falls back to create-first (multi-byte sequences are
// skipped by the M3 transcoder, so only the created string is a faithful
// key). On a miss, the created string is inserted and returned.
//------------------------------------------------------------------------------
Il2CppObject* il2cpp_string_intern_literal(const char* utf8)
{
    std::lock_guard<std::recursive_mutex> lock(g_internMutex);
    EnsureInternTable();
    if (utf8 == nullptr)
    {
        return il2cpp_string_intern(reinterpret_cast<Il2CppObject*>(il2cpp_string_new(nullptr, 0))); // defensive: empty string
    }
    const size_t byteLen = std::strlen(utf8);
    bool ascii = true;
    for (size_t i = 0; i < byteLen; ++i)
    {
        if (static_cast<unsigned char>(utf8[i]) >= 0x80)
        {
            ascii = false;
            break;
        }
    }
    if (ascii)
    {
        const int32_t length = static_cast<int32_t>(byteLen);
        Il2CppArray* table = g_internSlots;
        const int32_t mask = table->length - 1;
        uint32_t idx = HashAsciiBytes(utf8, length) & static_cast<uint32_t>(mask);
        for (;;)
        {
            Il2CppObject* entry = *SlotPtr(table, static_cast<int32_t>(idx));
            if (entry == nullptr)
            {
                break; // miss -- create and insert below
            }
            if (AsciiMatches(reinterpret_cast<const Il2CppString*>(entry), utf8, length))
            {
                return entry; // hit -- no allocation on this path
            }
            idx = (idx + 1) & static_cast<uint32_t>(mask);
        }
    }
    return InternInsert(reinterpret_cast<Il2CppString*>(il2cpp_string_new_from_utf8(utf8)));
}

} // namespace il2cpp
