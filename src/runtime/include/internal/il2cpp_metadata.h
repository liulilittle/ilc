#pragma once
//------------------------------------------------------------------------------
// il2cpp_metadata.h -- runtime type descriptor (Il2CppClass).
//
// One static Il2CppClass instance is emitted per translated type by the
// transpiler and registered with the runtime (il2cpp_class_register). It is
// the runtime's source of truth for everything the collector and the object
// model need to know about a type:
//
//   name            -- full type name ("Calc.Foo"), used by the type registry
//   instanceSize    -- byte size of one instance: for a REFERENCE type this
//                      includes the object header (>= sizeof(Il2CppObject));
//                      for a VALUE type (base chain reaches System.ValueType)
//                      it is the payload size WITHOUT the header (== sizeof(T))
//   refFieldBitmap  -- bit i set <=> the field at offset i*sizeof(void*) is a
//                      GC reference; drives exact mark-sweep field scanning
//   refFieldCount   -- number of reference-typed fields (root iteration help)
//   baseClass       -- parent descriptor, null for System.Object
//
// M2 scope note: only these fields exist. The vtable pointer, interface map
// and other runtime metadata arrive in later M2 tasks (virtual dispatch /
// interface dispatch) and M3 (full reflection).
//------------------------------------------------------------------------------

#include <cstddef>
#include <cstdint>

namespace il2cpp {

// One interface-dispatch map entry (see Il2CppClass.interfaces): the
// interface's own descriptor plus the slot map (iface slot -> this type's
// vtable slot). Forward-declared so Il2CppClass can hold a pointer array.
struct InterfaceMapEntry;

// Runtime type descriptor; one static instance per translated type, emitted by
// the transpiler and registered with the runtime at startup.
struct Il2CppClass
{
    const char*     name;           // full type name, e.g. "Calc.Foo"
    int32_t         instanceSize;   // total size in bytes of one instance (aligned)
    uint32_t        refFieldBitmap; // bit i set => field at i*sizeof(void*) is a GC reference
    int32_t         refFieldCount;  // number of reference-typed fields (for root iteration)
    Il2CppClass*    baseClass;      // null for System.Object

    // Virtual dispatch table (Task 7): one function pointer per virtual slot,
    // compiled-time generated as a static array. Slot i is the table entry at
    // index i; callvirt reads obj->klass->vtable[slot] and calls through it.
    // Null until the type has virtuals (the transpiler emits the table only
    // for types with virtual methods; the field is always present so the
    // descriptor layout is uniform).
    void**          vtable;
    int32_t         vtableSize;     // number of virtual slots

    // Interface dispatch maps (Task 8): one entry per implemented interface.
    //   InterfaceMapEntry { Il2CppClass* iface; int32_t* slotMap; }
    // slotMap[i] = this type's vtable slot implementing interface slot i.
    // Interface callvirt: find the entry by iface descriptor, then
    // vtable[slotMap[ifaceSlot]]. Null when the type implements no interfaces.
    const InterfaceMapEntry* interfaces;
    int32_t                  interfaceCount;
};

// One interface-dispatch map entry (see Il2CppClass.interfaces above).
struct InterfaceMapEntry
{
    Il2CppClass* iface;    // the interface's own descriptor (method slots)
    int32_t*     slotMap;  // iface slot -> this type's vtable slot
};

// Register every builtin System.* descriptor (System.Object / System.String /
// the box classes / the BCL interfaces) with the type registry. Idempotent;
// call before any isinst/castclass/class_by_name that may target a builtin
// System type. Definitions live in metadata.cpp.
void il2cpp_ensure_builtin_system_classes();

// p-refl2-gapmatrix (C06): enumerate the registered class registry. The AOT
// runtime's class registry is the only type-identity store (Il2CppTypeInfo is
// a reflection overlay), so Assembly.GetTypes() on a BCL assembly enumerates
// it -- the builtin System.* descriptors plus every translated user type. The
// count is 0 when nothing has registered yet (the registry is populated by
// il2cpp_runtime_bootstrap -> il2cpp_ensure_builtin_system_classes and by the
// generated per-type il2cpp_class_register calls). il2cpp_class_at returns
// nullptr for an out-of-range index.
int32_t il2cpp_class_count();
const Il2CppClass* il2cpp_class_at(int32_t index);

} // namespace il2cpp

//------------------------------------------------------------------------------
// Builtin System.* descriptors -- GLOBAL scope contract.
//
// The transpiler emits isinst/castclass as
//
//     il2cpp::il2cpp_is_instance(obj, &System_IConvertible_class)
//
// from generated code at GLOBAL scope (the same pattern as the user-type
// descriptor externs in generated.h: `extern Il2CppClass IBox_class;`).
// The runtime therefore defines one external-linkage Il2CppClass per core
// System type under the mangled global symbol (Sanitize: '.' -> '_'), and
// these externs are the single declarations every translation unit -- the
// runtime itself (boxing.cpp / string.cpp) and runtime tests -- shares.
//
//   System.Object     -- root descriptor; box/string descriptors hang off it
//                        via baseClass so `x is object` walks to it.
//   System.String     -- the System.String descriptor (string.cpp allocates
//                        through it; il2cpp_string_class() returns it).
//   System.Int32 / System.UInt32 / System.Boolean / System.Char / System.Double
//   System.Int64 / System.Single / System.Byte / System.SByte / System.Int16 /
//   System.UInt16 / System.UInt64 / System.Decimal
//                      -- the box class descriptors (metadata.cpp defines them;
//                        boxing.cpp / boxingext.cpp / boxingext2.cpp allocate
//                        through them). Each carries an interface table so
//                        isinst/as against IConvertible & co. hit.
//   System.IConvertible / System.IComparable / System.IFormattable
//                      -- interface descriptors (instanceSize 0, never
//                        allocated); box classes reference them in their
//                        interfaces tables.
//
// NOTE: EVERY primitive box descriptor is defined in metadata.cpp and exposed
// above -- there are no file-local duplicates. The transpiler resolves the
// box-only primitives (Byte/SByte/Int16/UInt16/Int64/UInt64/Single/Decimal)
// through il2cpp_class_by_name because their GLOBAL symbols are not part of
// the generated extern set; identity holds because the box icalls stamp the
// SAME metadata.cpp descriptor class_by_name returns.
//------------------------------------------------------------------------------
extern il2cpp::Il2CppClass System_Object_class;
extern il2cpp::Il2CppClass System_ValueType_class;
extern il2cpp::Il2CppClass System_String_class;
extern il2cpp::Il2CppClass System_Int32_class;
extern il2cpp::Il2CppClass System_UInt32_class;
extern il2cpp::Il2CppClass System_Boolean_class;
extern il2cpp::Il2CppClass System_Char_class;
extern il2cpp::Il2CppClass System_Double_class;
extern il2cpp::Il2CppClass System_Int64_class;   // nb9 D10: primitive array kind
extern il2cpp::Il2CppClass System_Single_class;  // nb9 D10: primitive array kind
// box-identity: the remaining primitive box descriptors. ONE descriptor per
// .NET primitive -- the box icalls and generated isinst/castclass share these
// (a file-local duplicate makes class_by_name miss the box's klass).
extern il2cpp::Il2CppClass System_Byte_class;
extern il2cpp::Il2CppClass System_SByte_class;
extern il2cpp::Il2CppClass System_Int16_class;
extern il2cpp::Il2CppClass System_UInt16_class;
extern il2cpp::Il2CppClass System_UInt64_class;
extern il2cpp::Il2CppClass System_Decimal_class;
// Identity-only descriptors: generated `is IntPtr` / `is UIntPtr` reference
// these symbols (the transpiler's default descriptor arm). Boxing IntPtr is a
// transpiler reject, so no object ever carries them -> `is` is always False
// (net48: a boxed IntPtr is the only True source).
extern il2cpp::Il2CppClass System_IntPtr_class;
extern il2cpp::Il2CppClass System_UIntPtr_class;
// excmsg: the managed exception hierarchy. Generated code references these for
// `new Argument*Exception(...)` allocations (il2cpp_object_new(&System_..._class))
// and for isinst/castclass on constructed exception instances; the base chain
// matches net48 (ArgumentException : SystemException : Exception).
extern il2cpp::Il2CppClass System_Exception_class;
extern il2cpp::Il2CppClass System_SystemException_class;
extern il2cpp::Il2CppClass System_ArgumentException_class;
extern il2cpp::Il2CppClass System_ArgumentNullException_class;
extern il2cpp::Il2CppClass System_ArgumentOutOfRangeException_class;
extern il2cpp::Il2CppClass System_IConvertible_class;
extern il2cpp::Il2CppClass System_IComparable_class;
extern il2cpp::Il2CppClass System_IFormattable_class;
// nullable (GN-2): the DateTime/TimeSpan/Guid box descriptors (a Nullable<T>
// value is a box of the underlying T; see il2cpp_nullable.h).
extern il2cpp::Il2CppClass System_DateTime_class;
extern il2cpp::Il2CppClass System_TimeSpan_class;
extern il2cpp::Il2CppClass System_Guid_class;
// dtoffset (delta 2): the System.DateTimeOffset box identity (List<T> carrier).
extern il2cpp::Il2CppClass System_DateTimeOffset_class;
