//------------------------------------------------------------------------------
// delegateext.cpp -- multicast delegate extension icalls (delegateext domain).
//
// See il2cpp_delegate_ext.h for the full representation and semantics.
// Summary: a multicast combination is an IMMUTABLE binary composition tree
// (one Il2CppMulticastDelegate node per Combine step, children are leaves or
// nested containers); the flattened leaf list is the .NET invocation list
// (first subtree then second subtree). All semantics were verified against a
// .NET Framework 4.8 reference run -- notably:
//   * Combine is pure concatenation (no dedup: a + a has length 2; no
//     identity shortcut: ab + ab has length 4 and a fresh container).
//   * Remove drops the LAST occurrence of the value's contiguous sub-list.
//
// The container is a managed GC object: its class descriptor marks both
// child slots as references (bitmask 0b00000110), so the collector traces
// the whole chain and each leaf's target. Flattening uses an iterative
// explicit-stack traversal (never recursion -- tree height is unbounded for
// append-heavy chains, and recursion would overflow the C++ stack).
//
// M3 exception note: the invoke-null error path raises the managed
// NullReferenceException carrier (il2cpp_throw_new), which generated
// try/catch code matches through il2cpp_exception_matches.
//------------------------------------------------------------------------------

#include "il2cpp_delegate_ext.h"

#include "il2cpp_array.h"
#include "il2cpp_exception.h"
#include "il2cpp_gc.h"
#include "il2cpp_pinvoke_ext.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <vector>

namespace il2cpp {

namespace {

// The multicast container class descriptor: two reference fields (first at
// slot 1, second at slot 2). The GC scans both children; leaf delegates are
// scanned through their own descriptor (slot 1 = target).
Il2CppClass g_multicastClass = {
    "System.MulticastDelegate",       // name
    static_cast<int32_t>(sizeof(Il2CppMulticastDelegate)), // instanceSize
    0b00000110u,                      // refFieldBitmap: slots 1 (first) + 2 (second)
    2,                                // refFieldCount
    &il2cpp::System_Delegate_class,   // baseClass (batch 162: isinst walks up to System.Delegate)
    nullptr,                          // vtable
    0,                                // vtableSize
    nullptr,                          // interfaces
    0,                                // interfaceCount
};

bool g_multicastRegistered = false;

void EnsureMulticastRegistered()
{
    if (g_multicastRegistered) return;
    il2cpp_class_register(&g_multicastClass);
    g_multicastRegistered = true;
}

//------------------------------------------------------------------------------
// Method-name registry (see il2cpp_delegate_ext.h): methodPtr -> SHORT method
// name. A delegate stores only a raw code address, so Delegate.Method.Name
// resolves through this table. Bounded flat static storage (constraint #12:
// STATIC array, never malloc / never a dynamic container), keyed by methodPtr
// (identity key); last registration of a pointer wins.
//
// CAPACITY: 8192 entries (16 B each -> 128 KB of .bss). The earlier 256 was an
// artificial ceiling -- every DISTINCT delegate leaf the transpiler emits
// registers here (one entry per lambda / method group), so a real program with
// 257 lambdas aborted at startup with
//   "il2cpp_delegate_register_method: registry overflow (256 methods)".
// That is a harness bound, not a .NET semantic. The overflow branch below
// still FAILS LOUD (a programming error is never silently truncated).
//------------------------------------------------------------------------------
constexpr int32_t kMaxRegisteredMethods = 8192;

struct MethodNameEntry
{
    void*       methodPtr; // the bound method's code address (identity key)
    const char* name;      // SHORT method name (literal, owned by the caller)
};

MethodNameEntry g_methodNames[kMaxRegisteredMethods];
int32_t g_methodNameCount = 0;

const char* LookupMethodName(void* methodPtr)
{
    if (methodPtr == nullptr) return nullptr;
    for (int32_t i = 0; i < g_methodNameCount; ++i)
    {
        if (g_methodNames[i].methodPtr == methodPtr) return g_methodNames[i].name;
    }
    return nullptr;
}

// A tree node is a container iff its klass is the container descriptor above.
// Anything else is a single-cast leaf delegate (delegates can only be created
// through il2cpp_delegate_new, which stamps the delegate class descriptor).
bool IsContainer(Il2CppObject* obj)
{
    return obj != nullptr && obj->klass == &g_multicastClass;
}

// Leaf equality (.NET: same method pointer AND same target).
bool LeafEqual(const Il2CppDelegate* x, const Il2CppDelegate* y)
{
    return x->methodPtr == y->methodPtr && x->target == y->target;
}

// rlaudit4: the transpiler-stamped delegate type ID (0 = untracked creation
// path; see icall_delegate_combine). Containers and leaves both carry it.
uint64_t DelegateTypeId(Il2CppObject* d)
{
    return IsContainer(d)
        ? reinterpret_cast<Il2CppMulticastDelegate*>(d)->typeId
        : reinterpret_cast<Il2CppDelegate*>(d)->typeId;
}

// Flatten a composition tree into its leaf delegates in invocation order:
// for every container node the FIRST subtree is emitted before the SECOND
// subtree (matches .NET list concatenation under every combine shape).
// Iterative explicit-stack DFS (no recursion -- see file header).
void Flatten(Il2CppObject* root, std::vector<Il2CppDelegate*>& out)
{
    if (root == nullptr) return;
    std::vector<Il2CppObject*> stack;
    stack.push_back(root);
    while (!stack.empty())
    {
        Il2CppObject* node = stack.back();
        stack.pop_back();
        if (IsContainer(node))
        {
            Il2CppMulticastDelegate* m = reinterpret_cast<Il2CppMulticastDelegate*>(node);
            // LIFO order: push second first so the first subtree pops first.
            stack.push_back(reinterpret_cast<Il2CppObject*>(m->second));
            stack.push_back(reinterpret_cast<Il2CppObject*>(m->first));
        }
        else
        {
            out.push_back(reinterpret_cast<Il2CppDelegate*>(node));
        }
    }
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_delegate_register_method -- register (or replace) the SHORT name of
// a method (see il2cpp_delegate_ext.h). Keyed by the method's code address;
// re-registering the same methodPtr replaces the name (last wins). Null
// inputs are caller bugs and are ignored; table overflow aborts loudly
// (a programming error -- the transpiler emits a bounded set of methods).
//------------------------------------------------------------------------------
void il2cpp_delegate_register_method(void* methodPtr, const char* name)
{
    if (methodPtr == nullptr || name == nullptr) return;
    for (int32_t i = 0; i < g_methodNameCount; ++i)
    {
        if (g_methodNames[i].methodPtr == methodPtr)
        {
            g_methodNames[i].name = name;
            return;
        }
    }
    if (g_methodNameCount >= kMaxRegisteredMethods)
    {
        std::fprintf(stderr, "il2cpp_delegate_register_method: registry overflow (%d methods)\n",
                     static_cast<int>(kMaxRegisteredMethods));
        std::abort();
    }
    g_methodNames[g_methodNameCount].methodPtr = methodPtr;
    g_methodNames[g_methodNameCount].name = name;
    ++g_methodNameCount;
}

namespace runtime {

//------------------------------------------------------------------------------
// icall_delegate_combine -- Delegate.Combine (pure concatenation), with the
// BUG-2 (batch 177b) delegate-TYPE compatibility guard.
//
// net48 throws ArgumentException when the two operands are not the SAME
// delegate type (probe-verified: Func<int,int> + Action<int> ->
// ArgumentException; two Action<int> -> OK). The runtime cannot recover a
// delegate's managed type from its raw methodPtr (every leaf carries only the
// shared System.Delegate descriptor), so the TRANSPILER stamps every delegate
// at creation with its type ID (il2cpp_delegate_new's typeId arg -- FNV-1a of
// the full type name); containers store their validated common id. Semantics
// (net48's null-first ordering):
//   * null operand(s)  -> the surviving input (null check BEFORE the type
//     check, exactly like net48 Delegate.Combine).
//   * both ids non-zero and DIFFERENT -> ArgumentException (net48
//     Arg_DlgtTypeMis "Cannot combine delegates with different base types.").
//   * equal ids, or either id == 0 (untracked creation path) -> concatenate.
//     The untracked path is CONSERVATIVE: net48 would compare the runtime
//     types there, but we cannot -- allowing is still safe (the invoke side
//     dispatches per leaf by its own ABI), it just misses a throw net48 would
//     raise. (Every transpiled Func`/Action`/EventHandler/custom delegate
//     carries a non-zero id; only exotic ctor paths are untracked.)
//------------------------------------------------------------------------------
Il2CppObject* icall_delegate_combine(Il2CppObject* a, Il2CppObject* b)
{
    if (a == nullptr) return b;
    if (b == nullptr) return a;
    const uint64_t ta = IsContainer(a)
        ? reinterpret_cast<Il2CppMulticastDelegate*>(a)->typeId
        : reinterpret_cast<Il2CppDelegate*>(a)->typeId;
    const uint64_t tb = IsContainer(b)
        ? reinterpret_cast<Il2CppMulticastDelegate*>(b)->typeId
        : reinterpret_cast<Il2CppDelegate*>(b)->typeId;
    if (ta != 0 && tb != 0 && ta != tb)
    {
        il2cpp_throw_new("System.ArgumentException,System.Exception",
                         "Cannot combine delegates with different base types.");
    }
    EnsureMulticastRegistered();
    Il2CppMulticastDelegate* node = reinterpret_cast<Il2CppMulticastDelegate*>(
        il2cpp_gc_alloc(&g_multicastClass));
    node->first = reinterpret_cast<Il2CppDelegate*>(a);
    node->second = reinterpret_cast<Il2CppDelegate*>(b);
    node->typeId = ta != 0 ? ta : tb;
    return reinterpret_cast<Il2CppObject*>(node);
}

//------------------------------------------------------------------------------
// BUG-3 (batch 177b): icall_delegate_get_hash_code -- Delegate.GetHashCode.
// net48 derives the hash from the delegate TYPE only (probe-verified: same
// method + DIFFERENT targets -> SAME hash; the reference source is simply
// GetType().GetHashCode() -- the target never contributes). Our runtime has
// no per-delegate-type descriptors, so the closest faithful hash is FNV-1a
// over the flattened invocation list's leaf methodPtrs:
//   * GC-stable: methodPtr is a CODE ADDRESS (never moved by the compacting
//     collector) -- the old icall_object_get_hash_code fallback hashed the
//     delegate OBJECT's address (ptr >> 3), which changed after compaction.
//   * Equals-consistent: icall_delegate_equals compares methodPtr + target
//     per leaf, so two EQUAL delegates carry identical methodPtr lists and
//     hash identically (the net48 hash contract: Equal => same hash).
//   * target deliberately EXCLUDED, mirroring the measured net48 behavior.
// Null input raises a NullReferenceException (managed exception carrier).
//------------------------------------------------------------------------------
int32_t icall_delegate_get_hash_code(Il2CppObject* d)
{
    if (d == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    std::vector<Il2CppDelegate*> leaves;
    Flatten(d, leaves);
    uint64_t h = 14695981039346656037ULL;
    for (size_t i = 0; i < leaves.size(); ++i)
    {
        h ^= reinterpret_cast<uintptr_t>(leaves[i]->methodPtr);
        h *= 1099511628211ULL;
    }
    return static_cast<int32_t>(h ^ (h >> 32));
}

//------------------------------------------------------------------------------
// icall_delegate_remove -- Delegate.Remove (last occurrence, contiguous
// sub-list match). Pure: the source tree is never mutated; a hit rebuilds a
// fresh chain and returns null / the single survivor / a new container.
//------------------------------------------------------------------------------
Il2CppObject* icall_delegate_remove(Il2CppObject* source, Il2CppObject* value)
{
    if (source == nullptr) return nullptr;
    if (value == nullptr) return source;

    std::vector<Il2CppDelegate*> src;
    std::vector<Il2CppDelegate*> val;
    Flatten(source, src);
    Flatten(value, val);

    const int32_t n = static_cast<int32_t>(src.size());
    const int32_t m = static_cast<int32_t>(val.size());
    if (m == 0 || m > n) return source;

    // Scan for the LAST occurrence of the value's sub-list (net48 removes the
    // last occurrence; documented as such and verified by the reference run).
    int32_t found = -1;
    for (int32_t i = 0; i <= n - m; ++i)
    {
        bool match = true;
        for (int32_t j = 0; j < m; ++j)
        {
            if (!LeafEqual(src[static_cast<size_t>(i) + j], val[static_cast<size_t>(j)]))
            {
                match = false;
                break;
            }
        }
        if (match) found = i; // keep scanning: later occurrences win
    }
    if (found < 0) return source; // absent: identity (never a copy)

    // Rebuild the surviving elements as a fresh chain (pure combine; Combine
    // is concatenation so the public icall is exactly the right builder).
    Il2CppObject* result = nullptr;
    for (int32_t k = 0; k < n; ++k)
    {
        if (k >= found && k < found + m) continue;
        result = icall_delegate_combine(result, reinterpret_cast<Il2CppObject*>(src[static_cast<size_t>(k)]));
    }
    return result;
}

//------------------------------------------------------------------------------
// icall_delegate_remove_all -- Delegate.RemoveAll (EVERY occurrence). Same
// contiguous sub-list match as Remove, but every matching window is dropped
// (net48 RemoveAll semantics; overlapping windows mark their shared elements).
// Pure: the source tree is never mutated; absent value -> identity.
//------------------------------------------------------------------------------
Il2CppObject* icall_delegate_remove_all(Il2CppObject* source, Il2CppObject* value)
{
    if (source == nullptr) return nullptr;
    if (value == nullptr) return source;

    std::vector<Il2CppDelegate*> src;
    std::vector<Il2CppDelegate*> val;
    Flatten(source, src);
    Flatten(value, val);

    const int32_t n = static_cast<int32_t>(src.size());
    const int32_t m = static_cast<int32_t>(val.size());
    if (m == 0 || m > n) return source;

    std::vector<bool> drop(static_cast<size_t>(n), false);
    for (int32_t i = 0; i + m <= n; ++i)
    {
        bool match = true;
        for (int32_t j = 0; j < m; ++j)
        {
            if (!LeafEqual(src[static_cast<size_t>(i) + j], val[static_cast<size_t>(j)]))
            {
                match = false;
                break;
            }
        }
        if (match)
        {
            for (int32_t j = 0; j < m; ++j)
            {
                drop[static_cast<size_t>(i) + j] = true;
            }
        }
    }

    Il2CppObject* result = nullptr;
    for (int32_t k = 0; k < n; ++k)
    {
        if (drop[static_cast<size_t>(k)]) continue;
        result = icall_delegate_combine(result, reinterpret_cast<Il2CppObject*>(src[static_cast<size_t>(k)]));
    }
    return result;
}

//------------------------------------------------------------------------------
// icall_delegate_get_invocation_list -- Delegate.GetInvocationList(): a
// reference array holding every leaf delegate in invocation order.
//------------------------------------------------------------------------------
Il2CppObject* icall_delegate_get_invocation_list(Il2CppObject* d)
{
    if (d == nullptr) return nullptr;
    std::vector<Il2CppDelegate*> list;
    Flatten(d, list);
    const int32_t count = static_cast<int32_t>(list.size());
    Il2CppArray* arr = il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)),
                                        true, count);
    for (int32_t i = 0; i < count; ++i)
    {
        Il2CppObject** slot = reinterpret_cast<Il2CppObject**>(
            il2cpp_array_get_elem_ptr(arr, i));
        *slot = reinterpret_cast<Il2CppObject*>(list[static_cast<size_t>(i)]);
    }
    return reinterpret_cast<Il2CppObject*>(arr);
}

//------------------------------------------------------------------------------
// icall_delegate_equals -- Delegate.Equals: pairwise invocation-list
// comparison (methodPtr + target per leaf; order matters, shape does not).
//------------------------------------------------------------------------------
bool icall_delegate_equals(Il2CppObject* a, Il2CppObject* b)
{
    if (a == b) return true; // covers both null and identity
    if (a == nullptr || b == nullptr) return false;
    // net48 Delegate.Equals -> InternalEqualTypes: different runtime TYPES are
    // never equal, even with the same target method (probe: dx.Equals(dy)
    // False). typeId 0 = untracked path -> conservative skip (see Combine).
    const uint64_t ta = DelegateTypeId(a);
    const uint64_t tb = DelegateTypeId(b);
    if (ta != 0 && tb != 0 && ta != tb) return false;
    std::vector<Il2CppDelegate*> la;
    std::vector<Il2CppDelegate*> lb;
    Flatten(a, la);
    Flatten(b, lb);
    if (la.size() != lb.size()) return false;
    for (size_t i = 0; i < la.size(); ++i)
    {
        if (!LeafEqual(la[i], lb[i])) return false;
    }
    return true;
}

//------------------------------------------------------------------------------
// icall_delegate_get_target -- Delegate.Target: the leaf's instance target;
// for a multicast chain, the LAST leaf's target (.NET contract).
//------------------------------------------------------------------------------
Il2CppObject* icall_delegate_get_target(Il2CppObject* d)
{
    if (d == nullptr) return nullptr;
    std::vector<Il2CppDelegate*> list;
    Flatten(d, list);
    if (list.empty()) return nullptr; // unreachable for a non-null delegate
    return list.back()->target;
}

//------------------------------------------------------------------------------
// icall_delegate_get_method_name -- Delegate.Method.Name (see header). The
// LAST leaf's methodPtr resolves through the method-name registry to a
// managed string. Null input raises a NullReferenceException; an unregistered
// methodPtr is LOUDLY rejected (the runtime cannot invent a name it never
// saw -- same policy as icall_delegate_get_fn_ptr).
//------------------------------------------------------------------------------
Il2CppObject* icall_delegate_get_method_name(Il2CppObject* d)
{
    if (d == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    std::vector<Il2CppDelegate*> list;
    Flatten(d, list);
    if (list.empty()) return nullptr; // unreachable for a non-null delegate
    const char* name = LookupMethodName(list.back()->methodPtr);
    if (name == nullptr)
    {
        il2cpp_throw_new("System.InvalidOperationException,System.Exception",
                         "The delegate's bound method has no registered name "
                         "(il2cpp_delegate_register_method was never called for it).");
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_string_new_from_cs(name));
}

//------------------------------------------------------------------------------
// icall_multicast_invoke_i4 -- call every leaf's methodPtr in invocation
// order with (a0..a3) and return the LAST result (.NET multicast semantics).
//
// Per-leaf dispatch by binding:
//   * static-method leaf (target == null): plain generated ABI
//     int32_t(*)(int32_t, int32_t, int32_t, int32_t) -- (a0..a3).
//   * instance-method leaf (target != null): the generated instance ABI is
//     {ret} M(Il2CppObject* this, ...) with `this` as the FIRST parameter
//     (CodeGenerator: instance arg0 = this), so the call is
//     int32_t(*)(Il2CppObject*, int32_t, int32_t, int32_t, int32_t) with the
//     leaf's stored target in the `this` slot: (target, a0, a1, a2, a3).
//     The callee reads exactly the parameters its signature declares; any
//     padding slots are ignored (x64: R8/R9/stack).
//   A mixed static+instance chain therefore invokes correctly: the binding
//   decision is made per leaf, not per chain.
//------------------------------------------------------------------------------
int32_t icall_multicast_invoke_i4(Il2CppObject* list, int32_t a0, int32_t a1, int32_t a2, int32_t a3)
{
    if (list == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    std::vector<Il2CppDelegate*> leaves;
    Flatten(list, leaves);
    typedef int32_t (*I4Fn)(int32_t, int32_t, int32_t, int32_t);
    typedef int32_t (*I4ThisFn)(Il2CppObject*, int32_t, int32_t, int32_t, int32_t);
    int32_t result = 0;
    for (size_t i = 0; i < leaves.size(); ++i)
    {
        Il2CppDelegate* leaf = leaves[i];
        if (leaf->target != nullptr)
            result = reinterpret_cast<I4ThisFn>(leaf->methodPtr)(leaf->target, a0, a1, a2, a3);
        else
            result = reinterpret_cast<I4Fn>(leaf->methodPtr)(a0, a1, a2, a3);
    }
    return result;
}

//------------------------------------------------------------------------------
// batch 161: icall_multicast_invoke_rr_void -- call every leaf's methodPtr in
// invocation order with (a0, a1) -- TWO MANAGED-REFERENCE args, VOID return
// (the event-handler ABI: `void Handler(object sender, EventArgs e)` --
// System.Timers.Timer's ElapsedEventHandler, WinForms event handlers, ...).
//
// Per-leaf dispatch by binding (mirrors icall_multicast_invoke_i4):
//   * static-method leaf (target == null): void(*)(Il2CppObject*, Il2CppObject*)
//     called as (a0, a1).
//   * instance-method leaf (target != null): the generated instance ABI is
//     void M(Il2CppObject* this, ...) -- call
//     void(*)(Il2CppObject*, Il2CppObject*, Il2CppObject*) as (target, a0, a1).
//   Mixed static+instance chains dispatch per leaf, same as the i4 variant.
//------------------------------------------------------------------------------
void icall_multicast_invoke_rr_void(Il2CppObject* list, Il2CppObject* a0, Il2CppObject* a1)
{
    if (list == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.Exception",
                         "Object reference not set to an instance of an object.");
        return; // unreachable (il2cpp_throw_new never returns)
    }
    std::vector<Il2CppDelegate*> leaves;
    Flatten(list, leaves);
    typedef void (*RRFn)(Il2CppObject*, Il2CppObject*);
    typedef void (*RRThisFn)(Il2CppObject*, Il2CppObject*, Il2CppObject*);
    for (size_t i = 0; i < leaves.size(); ++i)
    {
        Il2CppDelegate* leaf = leaves[i];
        if (leaf->target != nullptr)
            reinterpret_cast<RRThisFn>(leaf->methodPtr)(leaf->target, a0, a1);
        else
            reinterpret_cast<RRFn>(leaf->methodPtr)(a0, a1);
    }
}

//------------------------------------------------------------------------------
// BUG-1 (batch 172): icall_multicast_invoke_r_void -- call every leaf's
// methodPtr in invocation order with ONE MANAGED-REFERENCE arg, VOID return
// (the 1-ref action ABI: `void Handler(string)` -- System.Action<string>,
// System.Action<object>, custom 1-ref void delegates). Same flatten + per-leaf
// static/instance dispatch as the rr_void variant, generalized to one ref arg.
//------------------------------------------------------------------------------
void icall_multicast_invoke_r_void(Il2CppObject* list, Il2CppObject* a0)
{
    if (list == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.Exception",
                         "Object reference not set to an instance of an object.");
        return; // unreachable (il2cpp_throw_new never returns)
    }
    std::vector<Il2CppDelegate*> leaves;
    Flatten(list, leaves);
    typedef void (*RFn)(Il2CppObject*);
    typedef void (*RThisFn)(Il2CppObject*, Il2CppObject*);
    for (size_t i = 0; i < leaves.size(); ++i)
    {
        Il2CppDelegate* leaf = leaves[i];
        if (leaf->target != nullptr)
            reinterpret_cast<RThisFn>(leaf->methodPtr)(leaf->target, a0);
        else
            reinterpret_cast<RFn>(leaf->methodPtr)(a0);
    }
}

//------------------------------------------------------------------------------
// rlaudit4: icall_multicast_invoke_r -- ref-returning, ZERO-arg multicast
// (`Func<string> f; f()`): call every leaf's methodPtr in invocation order,
// return the LAST result (.NET multicast semantics). Per-leaf dispatch by
// binding (mirrors the sibling variants): static -> Il2CppObject*(*)(void);
// instance -> Il2CppObject*(*)(Il2CppObject*) called as (target).
//------------------------------------------------------------------------------
Il2CppObject* icall_multicast_invoke_r(Il2CppObject* list)
{
    if (list == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    std::vector<Il2CppDelegate*> leaves;
    Flatten(list, leaves);
    typedef Il2CppObject* (*RFn)(void);
    typedef Il2CppObject* (*RThisFn)(Il2CppObject*);
    Il2CppObject* result = nullptr;
    for (size_t i = 0; i < leaves.size(); ++i)
    {
        Il2CppDelegate* leaf = leaves[i];
        if (leaf->target != nullptr)
            result = reinterpret_cast<RThisFn>(leaf->methodPtr)(leaf->target);
        else
            result = reinterpret_cast<RFn>(leaf->methodPtr)();
    }
    return result;
}

//------------------------------------------------------------------------------
// icall_delegate_get_fn_ptr -- Delegate -> native function pointer (see the
// header for the full contract). Returns the SINGLE-CAST STATIC-method
// delegate's methodPtr; null, multicast containers and target-bound
// (instance-method) delegates are REJECTED loudly -- a raw code address
// carries no invocation context, so handing those out would be a
// silent-misbehavior class bug.
//------------------------------------------------------------------------------
void* icall_delegate_get_fn_ptr(Il2CppObject* d)
{
    if (d == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    if (IsContainer(d))
    {
        il2cpp_throw_new("System.InvalidOperationException,System.Exception",
                         "A multicast delegate has no single native function pointer.");
    }
    Il2CppDelegate* leaf = reinterpret_cast<Il2CppDelegate*>(d);
    if (leaf->target != nullptr)
    {
        il2cpp_throw_new("System.InvalidOperationException,System.Exception",
                         "An instance-method delegate has no native function pointer (no target binding).");
    }
    return leaf->methodPtr;
}

//------------------------------------------------------------------------------
// P-D2 #3: icall_delegate_from_fn_ptr -- GetDelegateForFunctionPointer<T>
// (see the header): wrap a native function pointer into a single-cast
// delegate. Null pointer -> ArgumentNullException; otherwise the delegate is
// allocated through il2cpp_delegate_new with a null target and the supplied
// typeId (the transpiler stamps T's FNV-1a type ID so Combine/Equals type
// checks work -- the SAME stamp a normally-created delegate of T carries).
// The wrapped delegate's Invoke is a DIRECT native call through methodPtr.
//------------------------------------------------------------------------------
Il2CppObject* icall_delegate_from_fn_ptr(intptr_t ptr, uint64_t typeId)
{
    if (ptr == 0)
    {
        il2cpp_throw_new("System.ArgumentNullException,System.Exception",
                         "Value cannot be null.\nParameter name: ptr");
    }
    return reinterpret_cast<Il2CppObject*>(il2cpp_delegate_new(reinterpret_cast<void*>(ptr), nullptr, typeId));
}

//------------------------------------------------------------------------------
// icall_delegate_get_fn_ptr_target -- (methodPtr, target) extraction that
// ACCEPTS an instance-bound delegate (see the header). Used by the ThreadStart
// ctor: the target rides back to the caller so the spawned thread can invoke
// the generated instance ABI `void M(Il2CppObject* this)`.
//------------------------------------------------------------------------------
void* icall_delegate_get_fn_ptr_target(Il2CppObject* d, Il2CppObject** outTarget)
{
    if (outTarget != nullptr) *outTarget = nullptr;
    if (d == nullptr)
    {
        il2cpp_throw_new("System.NullReferenceException,System.Exception",
                         "Object reference not set to an instance of an object.");
    }
    if (IsContainer(d))
    {
        il2cpp_throw_new("System.InvalidOperationException,System.Exception",
                         "A multicast delegate has no single native function pointer.");
    }
    Il2CppDelegate* leaf = reinterpret_cast<Il2CppDelegate*>(d);
    if (outTarget != nullptr) *outTarget = leaf->target;
    return leaf->methodPtr;
}

//------------------------------------------------------------------------------
// icall_delegate_to_callback -- Delegate -> named callback table entry (see
// the header). Validation is shared with get_fn_ptr; on success the
// methodPtr is registered in the pinvokeext callback registry under 'name'
// (replacing any existing entry -- last registration wins).
//------------------------------------------------------------------------------
void icall_delegate_to_callback(Il2CppObject* d, const char* name)
{
    void* fn = icall_delegate_get_fn_ptr(d);
    icall_pinvoke_register_callback(name, fn);
}

// P6: Delegate internal helpers -- the BCL calls these for delegate
// infrastructure but the ILC runtime reimplements delegates natively.
// Stubs return sensible defaults since the native implementation handles
// these operations directly.

Il2CppObject* icall_delegate_internal_alloc_like(Il2CppObject* d)
{
    return d; // identity: allocate like this delegate
}

void* icall_delegate_get_multicast_invoke(Il2CppObject* d)
{
    return icall_delegate_get_fn_ptr(d); // return the function pointer
}

void* icall_delegate_get_invoke_method(Il2CppObject* d)
{
    return icall_delegate_get_fn_ptr(d); // return the function pointer
}

Il2CppObject* icall_delegate_find_method_handle(Il2CppObject* d)
{
    (void)d;
    return nullptr; // no method handle in the simplified model
}

bool icall_delegate_internal_equal_method_handles(Il2CppObject* d1, Il2CppObject* d2)
{
    return icall_delegate_equals(d1, d2); // delegate equality
}

Il2CppObject* icall_delegate_adjust_target(Il2CppObject* d, Il2CppObject* target)
{
    (void)target;
    return d; // identity: no target adjustment needed
}

void* icall_delegate_get_call_stub(Il2CppObject* d)
{
    return icall_delegate_get_fn_ptr(d); // return the function pointer
}

} // namespace runtime

} // namespace il2cpp
