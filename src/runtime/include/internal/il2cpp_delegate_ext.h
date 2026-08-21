#pragma once
//------------------------------------------------------------------------------
// il2cpp_delegate_ext.h -- multicast delegate extensions (delegateext domain).
//
// Extends the single-cast delegate (il2cpp_delegate.h) with the multicast
// composition API of System.Delegate / System.MulticastDelegate:
//
//   Combine(d1, d2)            -- Delegate.Combine (the C# + operator)
//   Remove(source, value)      -- Delegate.Remove  (the C# - operator)
//   GetInvocationList(d)       -- Delegate.GetInvocationList()
//   Equals(a, b)               -- Delegate.Equals
//   GetTarget(d)               -- Delegate.Target
//   multicast invoke (i4)      -- calling the whole chain in order
//
// REPRESENTATION
// ---------------
// A multicast combination is an IMMUTABLE binary composition tree. One tree
// node is allocated per Combine step -- no input delegate is ever mutated
// (Combine is pure, mirroring .NET):
//
//   struct Il2CppMulticastDelegate
//   {
//       Il2CppObject    obj;     // managed object header (it IS a GC object)
//       Il2CppDelegate* first;   // left sub-chain (leaf or nested container)
//       Il2CppDelegate* second;  // right sub-chain
//   };
//
//   Combine(a, b) allocates one node { a, b }. The invocation order of a
//   node is: first's list, then second's list -- so the flattened leaf list
//   of any tree equals the .NET invocation list, regardless of tree shape
//   (Combine is associative in list order; verified against net48:
//   a(bc).Equals(abc) == true).
//
// GC SAFETY
// ---------
// The container is a real managed object: its class descriptor
// ("System.MulticastDelegate") marks BOTH child slots (first/second,
// bitmask 0b00000110) as GC references, so the collector traces the whole
// chain and every leaf's own target field (the leaf delegate's descriptor
// marks slot 1). A rooted chain therefore keeps every element alive.
//
// SEMANTICS (verified against the .NET Framework 4.8 reference run of
// tests/cases/delegatecase/delegatecase.cs -- these differ from common
// assumptions and from .NET Core in places):
//   * Combine is PURE CONCATENATION: no dedup, no identity shortcut.
//     a + a has a 2-element invocation list; ab + ab (same object) has a
//     4-element list and is NOT identical to ab (net48 ground truth).
//   * Combine(d, null) == d; Combine(null, d) == d; Combine(null, null)
//     == null (reference identity of the surviving input).
//   * Remove removes the LAST occurrence of the value's invocation list as
//     a CONTIGUOUS sub-list (single values and multicast values alike;
//     net48: [a,b,a] - a == [a,b]; [a,b,c,a,b] - [a,b] == [a,b,c]).
//   * Remove with an absent value returns the ORIGINAL source object
//     (reference identity, never a copy).
//   * Remove producing an empty list returns null; a single remaining
//     element is returned as the bare delegate (no wrapper container).
//   * Equals compares the flattened invocation LISTS pairwise (methodPtr +
//     target); order matters, tree shape does not.
//   * Invocation calls every leaf in order through its methodPtr and returns
//     the LAST result (.NET multicast return-value semantics). A leaf bound
//     to an instance method (target != null) is called with the generated
//     instance ABI {ret} M(Il2CppObject* this, ...) -- the stored target is
//     passed in the `this` slot (target, a0, a1, a2, a3); a static leaf is
//     called through int32_t(*)(int32_t, int32_t, int32_t, int32_t) with
//     (a0..a3). The binding decision is per leaf, so mixed static + instance
//     chains invoke correctly.
//
//   * GetInvocationList returns a reference array ([System.Object] class)
//     holding the leaf delegates in invocation order.
//   * GetTarget returns the leaf's target (null for static-method
//     delegates); for a multicast chain it is the LAST leaf's target
//     (.NET MulticastDelegate.Target contract).
//   * GetMethodName (Delegate.Method.Name) resolves the bound method's
//     SHORT name through a method-name registry (methodPtr -> name). The
//     delegate stores only a raw method pointer, so the name is registered
//     once per method by generated startup code (il2cpp_delegate_register_method,
//     the class_register analogue) and looked up by the last leaf's
//     methodPtr at query time (MulticastDelegate.Method returns the LAST
//     invocation-list entry's method -- same contract as Target).
//
// Namespace layout follows the other runtime domains: the object-model
// struct lives in namespace il2cpp (next to Il2CppDelegate); the icalls
// live in namespace il2cpp::runtime (generated-code contract).
//------------------------------------------------------------------------------

#include "il2cpp_delegate.h"

#include <cstdint>

namespace il2cpp {

// One multicast composition node (see header comment): an immutable binary
// tree whose leaves are single-cast Il2CppDelegate objects.
struct Il2CppMulticastDelegate
{
    Il2CppObject    obj;     // managed object header (klass pointer)
    Il2CppDelegate* first;   // left sub-chain (leaf delegate or nested container)
    Il2CppDelegate* second;  // right sub-chain
    // BUG-2 (batch 177b): the chain's common delegate-type ID (copied from
    // the validated operands at Combine; 0 when untracked). Kept so a
    // container operand's type is O(1) readable at the next Combine check
    // (no flattening) -- a valid chain is homogeneous by construction.
    uint64_t        typeId;
};

//------------------------------------------------------------------------------
// Delegate method-name registry (delegate2's Delegate.get_Method data source).
//
// A delegate stores ONLY a raw methodPtr (a code address) -- there is no
// managed MethodInfo object and no way to recover the method's SHORT name
// from the pointer alone. The name therefore rides in a runtime registry
// keyed by methodPtr:
//
//   il2cpp_delegate_register_method(&Cases_Delegate2_Ten_x, "Ten");
//
// mirrored on generated startup code (one call per delegate-created method),
// exactly like il2cpp_class_register / il2cpp_typeinfo_register. At query
// time icall_delegate_get_method_name flattens the chain and looks up the
// LAST leaf's methodPtr (MulticastDelegate.Method returns the last entry's
// method -- net48 contract, symmetric with Target). Re-registering the same
// methodPtr REPLACES its name (last registration wins). Unregistered
// methodPtrs are LOUDLY rejected (InvalidOperationException) -- the runtime
// cannot invent a name it never saw.
//------------------------------------------------------------------------------
void il2cpp_delegate_register_method(void* methodPtr, const char* name);

namespace runtime {

// Delegate.Combine: concatenate two invocation lists. Pure: allocates one
// new container node and never mutates the inputs (see header comment for
// the net48-verified concatenation semantics).
//
// BUG-2 (batch 177b): net48 throws ArgumentException when the two operand
// delegates are not the SAME type (probe-verified: Func<int,int> +
// Action<int> -> ArgumentException; two Action<int> -> OK). The runtime
// cannot recover a delegate's type from its raw methodPtr, so every delegate
// carries its type ID from creation (il2cpp_delegate_new's typeId arg --
// FNV-1a of the full type name stamped by the transpiler). This icall
// compares the operands' type IDs directly (containers read their stored
// common id), raising ArgumentException when both ids are non-zero and
// differ -- with the null checks FIRST, matching net48's order. An id of 0
// (untracked creation path) skips the check conservatively. The container
// stores the validated common id so nested combines stay O(1).
Il2CppObject* icall_delegate_combine(Il2CppObject* a, Il2CppObject* b);

// BUG-3 (batch 177b): Delegate.GetHashCode -- FNV-1a over the flattened
// invocation list's leaf methodPtrs. net48's hash is delegate-TYPE derived
// (probe-verified: same method + different targets -> SAME hash; reference
// source: GetType().GetHashCode()) -- the target NEVER contributes, and the
// hash must be stable across a compacting GC. A code address (methodPtr) is
// never moved and equal delegates share identical methodPtr lists
// (icall_delegate_equals compares methodPtr + target), so this satisfies the
// net48 hash contract (Equal => same hash). Null input raises a
// NullReferenceException (managed exception carrier).
int32_t icall_delegate_get_hash_code(Il2CppObject* d);

// Delegate.Remove: remove the LAST occurrence of value's invocation list
// (contiguous sub-list match, methodPtr + target per element). Returns the
// original source object when nothing matches; null when the result is
// empty; the single remaining delegate when one element survives.
Il2CppObject* icall_delegate_remove(Il2CppObject* source, Il2CppObject* value);

// Delegate.GetInvocationList: a reference array (Il2CppObject* elements)
// holding every leaf delegate in invocation order. Returns a 1-element
// array for a single-cast delegate; null for null input.
Il2CppObject* icall_delegate_get_invocation_list(Il2CppObject* d);

// Delegate.Equals: pairwise invocation-list comparison (methodPtr + target
// per leaf; order matters, tree shape does not). null == null is true;
// null vs. a delegate is false.
bool icall_delegate_equals(Il2CppObject* a, Il2CppObject* b);

// Delegate.Target: the leaf's instance target (null for static-method
// delegates); for a multicast chain, the LAST leaf's target.
Il2CppObject* icall_delegate_get_target(Il2CppObject* d);

// Delegate.Method.Name (delegate2): the SHORT name of the bound method as a
// managed string, resolved through the method-name registry
// (il2cpp_delegate_register_method). For a multicast chain the LAST leaf's
// method is returned (.NET MulticastDelegate.Method contract). Null input
// raises a NullReferenceException (managed exception carrier); a leaf whose
// methodPtr was never registered raises an InvalidOperationException (the
// runtime cannot derive the name from a bare code address).
Il2CppObject* icall_delegate_get_method_name(Il2CppObject* d);

// Multicast invocation: call every leaf's methodPtr in invocation order
// with (a0, a1, a2, a3) and return the LAST result (.NET multicast
// semantics). A leaf with a non-null target (instance-method binding) is
// invoked as (target, a0, a1, a2, a3) through the generated instance ABI
// int32_t(*)(Il2CppObject*, int32_t, int32_t, int32_t, int32_t); a static
// leaf through int32_t(*)(int32_t, int32_t, int32_t, int32_t). Null input
// raises a NullReferenceException (managed exception carrier).
int32_t icall_multicast_invoke_i4(Il2CppObject* list, int32_t a0, int32_t a1, int32_t a2, int32_t a3);
// batch 161: two managed-ref args, void return -- the event-handler ABI
// (`void Handler(object, EventArgs)` -- System.Timers.Timer Elapsed, WinForms
// events). Per-leaf static/instance dispatch like the i4 variant.
void icall_multicast_invoke_rr_void(Il2CppObject* list, Il2CppObject* a0, Il2CppObject* a1);
// BUG-1 (batch 172): ONE managed-ref arg, void return -- the 1-ref action ABI
// (`void Handler(string)` -- System.Action<string>, System.Action<object>,
// custom 1-ref void delegates). Per-leaf static/instance dispatch like the i4
// variant.
void icall_multicast_invoke_r_void(Il2CppObject* list, Il2CppObject* a0);

// Delegate -> native function pointer (delegatecb domain): the address the
// transpiler passes when a managed delegate is handed to a native function
// (C#: NativeFunc(callbackDelegate)). The native callee receives the bare
// SINGLE-CAST STATIC-method methodPtr -- the same value .NET marshals for a
// static-method delegate.
//
// LOUD REJECTION (correctness, not convenience): the returned pointer is a
// raw code address with NO invocation context, so
//   * null input raises a NullReferenceException;
//   * a MULTICAST container raises an InvalidOperationException (the pointer
//     would silently cover only one leaf of the invocation list);
//   * an INSTANCE-method delegate (target != null) raises an
//     InvalidOperationException (the pointer carries no 'this' for the
//     callback to bind -- calling it would corrupt the call).
// Only a static-method single-cast delegate yields a pointer.
void* icall_delegate_get_fn_ptr(Il2CppObject* d);

// Delegate -> named callback table entry (delegatecb domain): registers the
// delegate's methodPtr in the P/Invoke callback registry (pinvokeext) under
// 'name' so the native side can retrieve it with
// il2cpp_pinvoke_find_callback(name) and call it. Registration replaces any
// existing entry with the same name (last registration wins); validation is
// identical to icall_delegate_get_fn_ptr (null -> NullReferenceException,
// multicast/instance-bound -> InvalidOperationException). A null name aborts
// loudly (programming error, same policy as icall_pinvoke_register_callback).
void icall_delegate_to_callback(Il2CppObject* d, const char* name);

// P6: Delegate internal helpers -- the BCL calls these for delegate
// infrastructure but the ILC runtime reimplements delegates natively.
// Stubs return sensible defaults (null/false) since the native implementation
// handles these operations directly.

// InternalAllocLike: allocate memory for a delegate (like another delegate).
Il2CppObject* icall_delegate_internal_alloc_like(Il2CppObject* d);

// GetMulticastInvoke: get the multicast invoke method pointer.
void* icall_delegate_get_multicast_invoke(Il2CppObject* d);

// GetInvokeMethod: get the invoke method pointer.
void* icall_delegate_get_invoke_method(Il2CppObject* d);

// FindMethodHandle: find the method handle for a delegate.
Il2CppObject* icall_delegate_find_method_handle(Il2CppObject* d);

// InternalEqualMethodHandles: compare two method handles.
bool icall_delegate_internal_equal_method_handles(Il2CppObject* d1, Il2CppObject* d2);

// AdjustTarget: adjust the delegate target.
Il2CppObject* icall_delegate_adjust_target(Il2CppObject* d, Il2CppObject* target);

// GetCallStub: get the call stub for a delegate.
void* icall_delegate_get_call_stub(Il2CppObject* d);

} // namespace runtime

} // namespace il2cpp
