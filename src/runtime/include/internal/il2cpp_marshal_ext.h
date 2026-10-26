#pragma once
//------------------------------------------------------------------------------
// il2cpp_marshal_ext.h -- P/Invoke struct marshalling primitives (M4, marshal
// extension layer, namespace il2cpp::runtime).
//
// SCOPE
//   M4 has no managed struct type. This layer provides MEMORY-BLOCK
//   primitives the transpiler will map onto when it adds struct parameters
//   (M5): a struct is represented as a native byte buffer of CONSECUTIVE
//   int32 slots. The managed side never sees the struct's type -- it
//   allocates a buffer, reads/writes int32 fields by index, and converts
//   the buffer to/from int32[] for managed-side inspection.
//
//   M4 struct model (the ONLY layout this layer understands):
//
//     struct S { int32 f0; int32 f1; ... }   // fieldCount fields
//     buffer size = round_up(fieldCount * 4, 8)   // 8-byte alignment
//
//   Field i lives at byte offset i*4 (contiguous, no padding -- all fields
//   are 4 bytes). The size formula is provided by
//   icall_marshal_size_of_struct; the transpiler computes it once and reuses
//   the value for every allocation of that struct type.
//
// ABI: POINTERS AS int64 TOKENS (64-bit truncation risk -- read carefully)
//   The M4 call ABI is int32-only, so a native struct pointer crosses the
//   boundary as an int64 TOKEN carrying the FULL native address:
//
//     int64 token = icall_marshal_alloc_struct(size);  // full address
//     void*  ptr  = (void*)(intptr_t)token;            // exact re-expansion
//     icall_marshal_write_int32_field(ptr, i, v);
//     ...
//     icall_marshal_free_struct(ptr);
//
//   The runtime NEVER truncates: the token is the complete address, so the
//   round trip is exact on 32-bit AND 64-bit hosts. The M4 int32 ABI
//   truncation is a TRANSPILER-side decision, documented here so it is
//   explicit: truncating the token to int32 (e.g. passing it through a
//   ildc.i4 slot) is safe ONLY when the address fits in 32 bits (32-bit
//   targets, or guaranteed-low heaps). On 64-bit hosts heap addresses are
//   routinely >= 4 GiB (measured: msvcrt calloc at 0x132f2296f60 on this
//   dev box), where truncation is silent memory corruption -- the transpiler
//   MUST keep the full int64 there. The M4 tests exercise the int64 path.
//
// ALLOCATION TRACKING (bounds defense)
//   Every buffer from icall_marshal_alloc_struct is registered in an
//   internal {ptr,size} table (bounded static storage, single-threaded by
//   contract -- M4). Field reads/writes and array conversions look the size
//   up and bounds-check the byte range against it, so an out-of-range field
//   index is DETECTED and raised, never a silent out-of-bounds write:
//
//     fieldIndex is valid  <=>  fieldIndex*4 + 4 <= registered size
//
//   The bound is the BUFFER size, not a field count: a caller that
//   misreports fieldCount vs. the allocated size can still touch slots
//   inside the buffer -- memory safety is guaranteed, the semantic field
//   count is the caller's contract (the transpiler derives both from the
//   same struct definition). A pointer NOT in the table (foreign buffer,
//   double free) ABORTS loudly.
//
// EXCEPTION MAPPING (managed semantics via il2cpp_exception_ext.h)
//   null structPtr / null array          -> ArgumentNullException
//   negative fieldIndex / fieldCount     -> ArgumentOutOfRangeException
//   fieldIndex*4+4 > buffer size         -> ArgumentOutOfRangeException
//   array length*4 > buffer size         -> ArgumentOutOfRangeException
//   non-int32 array element kind         -> NotSupportedException
//   fieldSizes entry != 4                -> NotSupportedException
//   foreign ptr / double free / overflow / OOM / >32-bit address -> abort
//
// OWNERSHIP
//   Every buffer from icall_marshal_alloc_struct MUST be released exactly
//   once with icall_marshal_free_struct (null token -> no-op, mirroring
//   icall_marshal_free_hglobal). alloc_struct(0 / negative) returns token 0
//   ("no buffer", mirroring alloc_hglobal's null on negative size). Buffers
//   from icall_marshal_alloc_hglobal are NOT tracked and are invalid for the
//   field/array primitives below (lookup aborts).
//
//   M5 note: this layer is a stopgap. The real struct type system (layout,
//   blittable checks, StructureToPtr/StructureFromPtr semantics) replaces
//   these primitives; their signatures are the transpiler's contract until
//   then.
//------------------------------------------------------------------------------

#include "il2cpp_array.h"
#include "il2cpp_object.h"
#include "il2cpp_pinvoke_struct.h"
#include "il2cpp_string.h"

#include <cstddef>
#include <cstdint>

namespace il2cpp {
namespace runtime {

// Compute the byte size of an M4 all-int32 struct: round_up(fieldCount*4, 8).
//   fieldCount : number of int32 fields (>= 0; negative -> ArgumentOutOfRangeException)
//   fieldSizes : optional per-field size table (fieldCount entries). M4 contract:
//                every entry MUST be 4 (any other value -> NotSupportedException);
//                nullptr means "all fields are int32" (the M4 default).
//   Returns the total buffer size to pass to icall_marshal_alloc_struct.
int32_t icall_marshal_size_of_struct(int32_t fieldCount, int32_t* fieldSizes);

// Allocate a native struct buffer of 'size' bytes (calloc-backed, zeroed,
// 8-byte aligned). Returns the int64 pointer TOKEN (the FULL native address
// -- see the ABI note above; the runtime never truncates). size <= 0
// returns token 0 ("no buffer"). The buffer is registered for bounds
// defense and MUST be released with icall_marshal_free_struct.
int64_t icall_marshal_alloc_struct(int32_t size);

// Write value into field 'fieldIndex' (byte offset fieldIndex*4) of the
// struct buffer. Validates null pointer and the buffer-size bound (see
// header). NULL structPtr -> ArgumentNullException.
void icall_marshal_write_int32_field(void* structPtr, int32_t fieldIndex, int32_t value);

// Read field 'fieldIndex' (byte offset fieldIndex*4) of the struct buffer.
// Same validation as icall_marshal_write_int32_field. Returns the slot's
// value (0 in an untouched, freshly calloc'd buffer).
int32_t icall_marshal_read_int32_field(void* structPtr, int32_t fieldIndex);

// Convert a struct buffer into a NEW managed int32[] with 'fieldCount'
// elements (elements copied from slots 0..fieldCount-1). fieldCount must be
// >= 0 and fieldCount*4 <= buffer size. The returned array is a GC object.
Il2CppObject* icall_marshal_struct_to_int32_array(void* structPtr, int32_t fieldCount);

// Copy a managed int32[] into a struct buffer: elements 0..length-1 are
// copied into slots 0..length-1. Requires an int32 element-kind array
// (other kinds -> NotSupportedException) and array length*4 <= buffer size.
void icall_marshal_int32_array_to_struct(Il2CppObject* array, void* structPtr);

// Release a struct buffer from icall_marshal_alloc_struct (removes it from
// the tracking table, then frees). Null pointer -> no-op. A pointer not
// tracked (foreign buffer / double free) ABORTS loudly.
void icall_marshal_free_struct(void* ptr);

//------------------------------------------------------------------------------
// D-HG-1 (2026-08-12): Marshal.ReAllocHGlobal(IntPtr, IntPtr) -- the realloc
// half of the hglobal alloc family (net48 realloc semantics, implemented in
// marshalext.cpp):
//   * OLD pointer is INVALID after the call (realloc may MOVE the block).
//   * a NEW pointer is returned and MUST be used for every later access and
//     for icall_marshal_free_hglobal (never the pre-realloc pointer).
//   * the first min(oldSize, newSize) bytes are preserved.
//   * realloc failure throws OutOfMemoryException (net48).
// Windows: GlobalReAlloc with GMEM_MOVEABLE (pairs with the GlobalAlloc
// GMEM_FIXED alloc side / GlobalFree free side in runtime.cpp). POSIX:
// std::realloc on the calloc'd block. The new size is an intptr_t byte
// count (the IntPtr cb carrier); negative sizes are rejected.
//------------------------------------------------------------------------------
intptr_t icall_marshal_realloc_hglobal(intptr_t ptr, intptr_t size);

//------------------------------------------------------------------------------
// Marshal gap batch (net48 inventory section 5 items 6/8/9 + the SEH queries).
// These complete the Marshal surface the transpiler maps onto; see
// marshalext3.cpp for the full net48-semantics contract.
//------------------------------------------------------------------------------

// Marshal.UnsafeAddrOfPinnedArrayElement(Array, int): the address of element
// 'index' (array data base + index * elemSize). Returns the full address as
// an intptr_t (the IntPtr carrier -- "ptr" result kind). Null array ->
// ArgumentNullException; out-of-range index -> IndexOutOfRangeException.
//
// PIN LIMITATION (documented divergence): ILC has no real pinning -- the GC
// is a generational COMPACTING collector, so the returned address is only
// valid until the next collection. net48 requires the array to be pinned
// (GCHandle.Alloc(arr, Pinned) or a stackalloc) before calling this; ILC
// returns the address without pinning and the caller must keep the array
// rooted and treat the address as short-lived (no collection in between).
intptr_t icall_marshal_unsafe_addr_of_pinned_array_element(Il2CppArray* array, int32_t index);

// Marshal.GetHRForException(Exception): the exception's HRESULT. net48 reads
// Exception.HResult (0 for null). ILC exceptions carry no instance _HResult
// field -- the managed Exception object is the catch handler's interned
// type-name string -- so this maps the exception TYPE to its net48 default
// HResult (the table lives in marshalext3.cpp). null -> 0; an unknown or
// non-string object -> COR_E_EXCEPTION (0x80131500), the base-class default.
int32_t icall_marshal_get_hr_for_exception(Il2CppObject* exception);

// Marshal.GetExceptionCode() / GetExceptionPointers(): x86-only SEH queries
// (valid only inside a native SEH filter). ILC runs in a fully managed
// context with no SEH -- GetExceptionCode always returns 0 and
// GetExceptionPointers always returns IntPtr.Zero (documented semantics on
// every platform, including Windows).
int32_t icall_marshal_get_exception_code();
intptr_t icall_marshal_get_exception_pointers();

//------------------------------------------------------------------------------
// D-MAR-1 (2026-08-12): Marshal.SizeOf / OffsetOf for the REGISTERED struct
// family (System.Drawing.Point / Rectangle / System.Windows.Forms.Message).
// The native layout registry (il2cpp_pinvoke_struct.h) is the single layout
// authority. Implemented in marshalext3.cpp.
//
// The named forms receive the ILC Type VALUE -- the type's full-name string
// (typeof(X) folds to it) -- and resolve it through
// il2cpp_pinvoke_struct_kind_by_name. The per-struct forms bake the kind into
// the function (the transpiler selects them from the compile-time generic
// argument, so no name crosses the boundary); MSG's pointer-width-dependent
// size stays runtime-computed.
//
// Exception mapping (net48 semantics):
//   SizeOf:   null type-name string     -> ArgumentNullException("type")
//             unregistered type         -> NotSupportedException
//   OffsetOf: null type-name/field name -> ArgumentNullException
//             unregistered type         -> NotSupportedException
//             unknown field name        -> ArgumentException
//------------------------------------------------------------------------------

// Marshal.SizeOf(typeof(T)): native byte size of a registered struct by its
// full-name string (Point=8, Rectangle=16, Message=48 x64 / 28 x86).
int32_t il2cpp_marshal_size_of_struct_named(Il2CppString* typeName);

// Marshal.OffsetOf(typeof(T), string): field byte offset by net48 field name
// (Rectangle: x/y/width/height at 0/4/8/12; Point: x/y at 0/4; MSG: the Win32
// tagMSG names).
int32_t il2cpp_marshal_offset_of_struct_named(Il2CppString* typeName, Il2CppString* fieldName);

// Marshal.SizeOf<T>() generic forms (per registered struct).
int32_t il2cpp_marshal_size_of_struct_point();
int32_t il2cpp_marshal_size_of_struct_rect();
int32_t il2cpp_marshal_size_of_struct_msg();

// impl-io2: Marshal.SizeOf<T>() for the PRIMITIVE value types and the
// pointer-width types (IntPtr/UIntPtr). The transpiler knows the generic
// argument at compile time and passes T's full name as a C++ string literal
// ("System.Int32" -> 4, "System.Double" -> 8, "System.IntPtr" -> pointer
// width); an unregistered name throws the managed NotSupportedException (no
// runtime layout descriptor). The registered struct family keeps its per-struct
// icalls above (the transpiler selects those first).
int32_t icall_marshal_size_of_named_ascii(const char* typeName);

// Marshal.OffsetOf<T>(string) generic forms (per registered struct): the
// field name string only; the kind is baked in.
int32_t il2cpp_marshal_offset_of_struct_point(Il2CppString* fieldName);
int32_t il2cpp_marshal_offset_of_struct_rect(Il2CppString* fieldName);
int32_t il2cpp_marshal_offset_of_struct_msg(Il2CppString* fieldName);

// PtrToStructure<T>/StructureToPtr<T> byte semantics for the REGISTERED
// struct family: load reads the native buffer into a managed struct VALUE
// (PtrToStructure's return), store writes the managed struct value into the
// native buffer. Every registered struct is blittable, so both directions are
// size-checked memcpy. Null buffer/value -> ArgumentNullException (net48).
// NOTE: the transpiler cannot emit these today -- a PtrToStructure result
// needs a struct-typed local/temp (CodeGenerator TypeCppName / result-kind
// extension, documented in MethodMap.Marshal.cs). Runtime unit tests
// (test_marshalstruct.cpp) pin the byte round-trip + null semantics.
Il2CppPinvokePoint il2cpp_marshal_struct_point_load(const void* src);
Il2CppPinvokeRect il2cpp_marshal_struct_rect_load(const void* src);
Il2CppPinvokeMsg il2cpp_marshal_struct_msg_load(const void* src);
void il2cpp_marshal_struct_point_store(void* dst, const Il2CppPinvokePoint* value);
void il2cpp_marshal_struct_rect_store(void* dst, const Il2CppPinvokeRect* value);
void il2cpp_marshal_struct_msg_store(void* dst, const Il2CppPinvokeMsg* value);
void il2cpp_marshal_struct_store(Il2CppObject* structure, void* dst, bool fDeleteOld);

//------------------------------------------------------------------------------
// Marshal medium-priority gap batch (net48 inventory section 4 line 971:
// DestroyStructure; section 4/section 5 task picks: SizeOf-object and elem-width Copy).
// Implemented in marshalext4.cpp.
//------------------------------------------------------------------------------

// Marshal.DestroyStructure(IntPtr, Type): frees the substructures (BSTR,
// delegates, arrays) inside the native memory block described by the struct
// layout. ILC has no COM/BSTR marshalling and the M4 struct is an all-int32
// byte buffer with no embedded substructures, so after argument validation
// this is a documented NO-OP (nothing to free). net48 semantics:
//   ptr == 0                 -> ArgumentNullException("ptr")
//   structureType == nullptr -> ArgumentNullException("structuretype")
//   structureType is not a Type-name string -> ArgumentException
void icall_marshal_destroy_structure(intptr_t ptr, Il2CppString* structureType);

// Marshal.SizeOf(object) / SizeOf<T>(T): the marshal size of a boxed value's
// runtime type. net48: null -> ArgumentNullException("structure"); the value
// is resolved by its runtime type (the box class name -> net48 TypeCode ->
// il2cpp_marshal_size_of). A non-marshalable boxed type (String/Object/
// unknown) raises NotSupportedException (the established no-fixed-native-size
// policy of il2cpp_marshal_size_of).
int32_t icall_marshal_size_of_object(Il2CppObject* structure);

// Marshal.Copy elem-width-aware forms: copy 'length' ELEMENTS of the array's
// element size (arr->elemSize) between the array and a native buffer. This
// is the "Copy remaining forms" completion -- the runtime.cpp int32-stride icalls only
// handle elemSize==4. net48 Copy semantics per element type (byte/short/char/
// int/long/float/double/IntPtr arrays all dispatch through arr->elemSize):
//   to_unmanaged: null array -> ArgumentNullException("source");
//                 null dest  -> ArgumentNullException("destination");
//                 reference-element array (string[]/object[]) ->
//                 NotSupportedException (raw object-ref bytes are a GC hazard;
//                 net48 throws ArgumentException for non-blittable element
//                 types);
//                 start/length out of range -> ArgumentOutOfRangeException.
//   from_unmanaged: the mirror (null src/array, same range/type checks).
void icall_marshal_copy_to_unmanaged_elem(Il2CppArray* array, int32_t startIndex, void* destination, int32_t length);
void icall_marshal_copy_from_unmanaged_elem(void* source, Il2CppArray* array, int32_t startIndex, int32_t length);

//------------------------------------------------------------------------------
// Marshal COM ProgID resolution + activation -- r127 PNSE stubs RETIRED in C1
// (A1): Marshal.GetTypeFromProgID now routes to the Type-COM icall
// il2cpp_com_type_from_progid (il2cpp_com.h) and Marshal.CreateObject to
// icall_com_create_object (com.cpp). The stub symbols were removed; do NOT
// reference them.
//------------------------------------------------------------------------------


//------------------------------------------------------------------------------
// Marshal VARIANT + COM-identity gap batch (net48 inventory section 3 Marshal rows:
// GetObjectForNativeVariant / GetNativeVariantForObject /
// GetObjectsForNativeVariants / IsComObject). Implemented in marshalext5.cpp.
//
// ILC VARIANT subset (documented divergence -- no full COM runtime):
//   A native VARIANT is a 16-byte buffer with the Windows layout:
//     offset 0 (uint16): vt            (VARTYPE)
//     offset 2 (6 bytes): reserved     (zeroed on write)
//     offset 8 (8 bytes): data union   (the primitive payload)
//   The subset supports VT_EMPTY / VT_NULL / VT_I1 / VT_UI1 / VT_I2 / VT_UI2 /
//   VT_I4 / VT_UI4 / VT_I8 / VT_UI8 / VT_R4 / VT_R8 / VT_BOOL / VT_ERROR /
//   VT_INT / VT_UINT. VT_ERROR (an int32 SCODE -- no COM substrate) decodes as
//   null for S_OK (0) and as a boxed Int32 for any other value. The remaining
//   VARIANT types (VT_BSTR, VT_CY, VT_DATE, VT_DECIMAL,
//   VT_ARRAY, VT_RECORD, VT_DISPATCH, VT_UNKNOWN and the VT_BYREF forms) raise
//   NotSupportedException -- ILC has no BSTR allocator, SAFEARRAY, DECIMAL-in-
//   variant, DATE conversion or COM identity. net48 semantics are the
//   reference: coreclr olevariant.cpp MarshalObjectForOleVariant /
//   MarshalOleVariantForObject map each VT to its .NET boxed type.
//------------------------------------------------------------------------------

// Marshal.GetObjectForNativeVariant(IntPtr): decode one VARIANT into a managed
// object. VT_EMPTY / VT_NULL -> null; every supported VT -> the boxed
// primitive; VT_BOOL -> boxed Boolean (nonzero VARIANT_BOOL is true);
// VT_ERROR -> null for S_OK (0) or a boxed Int32 for any other SCODE;
// unsupported VTs -> NotSupportedException. Null pointer ->
// ArgumentNullException("pSrcNativeVariant").
Il2CppObject* icall_marshal_get_object_for_native_variant(intptr_t pSrcNativeVariant);

// Marshal.GetNativeVariantForObject(object, IntPtr): encode a boxed primitive
// into a 16-byte VARIANT. null obj -> VT_EMPTY (the buffer is zeroed); a boxed
// primitive maps to its VT (Byte->VT_UI1, SByte->VT_I1, Int16->VT_I2,
// UInt16/Char->VT_UI2, Int32->VT_I4, UInt32->VT_UI4, Int64->VT_I8,
// UInt64->VT_UI8, Single->VT_R4, Double->VT_R8, Boolean->VT_BOOL with
// VARIANT_BOOL -1/0); String / Decimal / non-primitive -> NotSupportedException
// (documented subset). Null destination -> ArgumentNullException.
void icall_marshal_get_native_variant_for_object(Il2CppObject* obj, intptr_t pDstNativeVariant);

// Marshal.GetObjectsForNativeVariants(IntPtr, int): decode 'cVars' consecutive
// 16-byte VARIANTs at the base pointer into a new managed object[] (each slot
// follows icall_marshal_get_object_for_native_variant's rules -- an unsupported
// VT propagates NotSupportedException). Null base -> ArgumentNullException;
// negative cVars -> ArgumentOutOfRangeException. Returns the object[] as a GC
// object (Il2CppObject*).
Il2CppObject* icall_marshal_get_objects_for_native_variants(intptr_t aSrcNativeVariant, int32_t cVars);

// Marshal.IsComObject(object): net48 returns whether the object is a COM RCW /
// CCW; null -> ArgumentNullException. ILC has no COM runtime, so a non-null
// object is never a COM wrapper -- this always returns false (documented:
// net48 would return true only for objects ILC can never produce).
bool icall_marshal_is_com_object(Il2CppObject* o);

//------------------------------------------------------------------------------
// Marshal.Read/Write primitive family + StructureToPtr VALUE form (marshalrw,
// 2026-09-13). Direct little-endian memory access at a caller-supplied byte
// offset (base + offset). net48 contract: NO bounds checks and NO exception
// paths -- the caller owns the memory's validity.
//
//   write_* : 3-arg (base, offset, value); the 2-arg IL overload (IntPtr,
//             value) is the C++ OVERLOAD that forwards offset 0.
//   read_*  : 2-arg (base, offset); the 1-arg IL overload (IntPtr) forwards
//             offset 0.
//
// Value widths follow the IL ABI slot: byte/int16/int32 arrive as int32_t
// (narrowed/truncated inside -- net48 unchecked semantics); int64 as int64_t;
// IntPtr as intptr_t. Reads return the widened int32/int64/intptr_t value
// (net48 ReadByte zero-extends, ReadInt16 sign-extends).
//------------------------------------------------------------------------------
void il2cpp_marshal_write_byte(uint8_t* base, int32_t offset, int32_t value);
void il2cpp_marshal_write_byte(uint8_t* base, int32_t value);
void il2cpp_marshal_write_int16(uint8_t* base, int32_t offset, int32_t value);
void il2cpp_marshal_write_int16(uint8_t* base, int32_t value);
void il2cpp_marshal_write_int32(uint8_t* base, int32_t offset, int32_t value);
void il2cpp_marshal_write_int32(uint8_t* base, int32_t value);
void il2cpp_marshal_write_int64(uint8_t* base, int32_t offset, int64_t value);
void il2cpp_marshal_write_int64(uint8_t* base, int64_t value);
void il2cpp_marshal_write_intptr(uint8_t* base, int32_t offset, intptr_t value);
void il2cpp_marshal_write_intptr(uint8_t* base, intptr_t value);

int32_t il2cpp_marshal_read_byte(const uint8_t* base, int32_t offset);
int32_t il2cpp_marshal_read_byte(const uint8_t* base);
int32_t il2cpp_marshal_read_int16(const uint8_t* base, int32_t offset);
int32_t il2cpp_marshal_read_int16(const uint8_t* base);
int32_t il2cpp_marshal_read_int32(const uint8_t* base, int32_t offset);
int32_t il2cpp_marshal_read_int32(const uint8_t* base);
int64_t il2cpp_marshal_read_int64(const uint8_t* base, int32_t offset);
int64_t il2cpp_marshal_read_int64(const uint8_t* base);
intptr_t il2cpp_marshal_read_intptr(const uint8_t* base, int32_t offset);
intptr_t il2cpp_marshal_read_intptr(const uint8_t* base);

// StructureToPtr<T>(T, IntPtr, bool) VALUE form: the transpiler passes
// &value, sizeof(T) and the generated class descriptor. Dispatch matches
// il2cpp_marshal_struct_store (registered kinds via the layout registry,
// reference-free user structs copied byte for byte, reference-bearing /
// unknown -> NotSupportedException, fDeleteOld no-op for blittable data);
// no box is materialized. Declared here, implemented next to struct_store in
// marshalext3.cpp.
void il2cpp_marshal_struct_store_value(Il2CppClass* klass, const void* value, void* dst,
                                       bool fDeleteOld, size_t payloadSize);

} // namespace runtime
} // namespace il2cpp
