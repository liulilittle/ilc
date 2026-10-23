//------------------------------------------------------------------------------
// metadata.cpp -- runtime type registry (name -> Il2CppClass lookup).
//
// M2 simplified model:
//   The transpiler emits one static Il2CppClass instance per translated type
//   directly inline in the generated code (no runtime construction of
//   descriptors). Generated startup code registers each descriptor exactly
//   once via il2cpp_class_register, and il2cpp_class_by_name answers the
//   only question the M2 runtime asks of the registry: "given a type name,
//   hand me the descriptor" (used by cross-type references and future
//   isinst/castclass resolution).
//
//   Full reflection (assembly-qualified names, hierarchy queries, method
//   metadata) replaces this table in M3; the registry API itself stays.
//
// Storage constraint (#12 -- no global-constructor heap allocation):
//   The registry is a fixed-size static array of pointers. Static storage is
//   zero-initialized at process load and never allocates; every byte of state
//   is either constant or set explicitly at runtime. Overflow is a
//   programming error and fails loudly (std::abort) instead of corrupting
//   neighbouring slots.
//------------------------------------------------------------------------------

#include "il2cpp_metadata.h"
#include "il2cpp_object.h"
#include "il2cpp_string.h"
#include "dictsi.h"
// rtdict-b23 (DICT-GC): the Dictionary`2<string,int> GC-object wrapper
// descriptor extern (System_Collections_Generic_Dictionary_2_string_int_class)
// lives in dictsi_obj.h -- the wrapper's single home. (dictsi.h carries only
// the RAW dictsi domain now; the stale embedded wrapper section was removed.)
#include "dictsi_obj.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>   // std::once_flag / std::call_once (builtin ensure once-guard)

namespace il2cpp {

namespace {

// Upper bound on distinct translated types a program may register. Exceeded
// only if the transpiler emits more types than this constant allows, which is
// a build-time invariant the generator must respect.
constexpr int kMaxRegisteredClasses = 256;

// The registry itself: a flat pointer table + running count. Zero-initialized
// static storage (constraint #12), appended to in registration order.
Il2CppClass* g_registeredClasses[kMaxRegisteredClasses];
int g_registeredClassCount = 0;

} // namespace

} // namespace il2cpp

//------------------------------------------------------------------------------
// Builtin System.* descriptors (GLOBAL scope -- see il2cpp_metadata.h).
//
// These are the symbols the transpiler references from generated isinst/
// castclass code (`&System_IConvertible_class`), so they MUST have external
// linkage at global scope exactly like the user-type descriptors. Boxing /
// string allocation goes through these same descriptors, keeping pointer
// identity: a boxed Int32's klass IS &System_Int32_class, so
// il2cpp_is_instance's pointer walk (baseClass chain + interfaces scan)
// matches what generated code passes in.
//
// Interface tables: the box classes implement the .NET core interfaces
// (IConvertible / IComparable / IFormattable for numerics). Dispatch is NOT
// wired for boxed primitives (vtableSize 0) -- the slotMap entries are dummy
// {0}; any il2cpp_interface_dispatch attempt aborts loudly ("slot map out of
// range"), which is the correct M2 behavior. isinst/as only compares
// interfaces[i].iface pointers (boxing.cpp:134), so the tables make
// `od is IConvertible` etc. true without enabling dispatch.
//------------------------------------------------------------------------------

using il2cpp::Il2CppClass;
using il2cpp::InterfaceMapEntry;

// System.String implements System.IConvertible (net48 typecheckcase:
// `(IConvertible)boxedStr` must succeed -- the cast scans the interface
// table). Forward-declared: the IConvertible descriptor is defined below
// System_String_class.
extern Il2CppClass System_IConvertible_class;

// Slot map for the String interface table: dispatch is unwired (vtableSize 0)
// exactly like the box-class tables below -- isinst/as only compares
// interfaces[i].iface, the slotMap is never dereferenced for String.
static int32_t kStringUnwiredSlotMap[] = { 0 };
static InterfaceMapEntry kStringInterfaces[] = {
    { &System_IConvertible_class, kStringUnwiredSlotMap },
};

// Root descriptor: no base, no fields, header only.
Il2CppClass System_Object_class = {
    "System.Object",                       // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)), // instanceSize: header only
    0u,                                    // refFieldBitmap
    0,                                     // refFieldCount
    nullptr,                               // baseClass (the root)
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

// System.ValueType: the implicit base of every user struct (the transpiler
// wires generated {T}_class descriptors for user value types to this). It is
// never allocated directly (instanceSize 0); it exists so the isinst/castclass
// base-chain walk in il2cpp_is_instance can answer `boxedStruct is ValueType`
// and `boxedStruct is object` (via baseClass System.Object). Registered in
// il2cpp_ensure_builtin_system_classes alongside the other System.* roots.
Il2CppClass System_ValueType_class = {
    "System.ValueType",                    // name
    0,                                     // instanceSize (abstract; never allocated)
    0u,                                    // refFieldBitmap
    0,                                     // refFieldCount
    &System_Object_class,                  // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

// System.String: base of the object model under Object; the string
// allocation paths (string.cpp) allocate through this descriptor.
Il2CppClass System_String_class = {
    "System.String",                       // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppString)), // instanceSize (header + length + 1-unit stub)
    0u,                                    // refFieldBitmap: no reference fields
    0,                                     // refFieldCount
    &System_Object_class,                  // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    kStringInterfaces,                     // interfaces (IConvertible)
    1,                                     // interfaceCount
};

// BCL interface descriptors: never allocated (instanceSize 0), referenced by
// box-class interface tables and by generated isinst/castclass code.
Il2CppClass System_IConvertible_class = {
    "System.IConvertible",                 // name
    0,                                     // instanceSize (interface: never allocated)
    0u,                                    // refFieldBitmap
    0,                                     // refFieldCount
    nullptr,                               // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

Il2CppClass System_IComparable_class = {
    "System.IComparable",                  // name
    0,                                     // instanceSize (interface)
    0u,                                    // refFieldBitmap
    0,                                     // refFieldCount
    nullptr,                               // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

Il2CppClass System_IFormattable_class = {
    "System.IFormattable",                 // name
    0,                                     // instanceSize (interface)
    0u,                                    // refFieldBitmap
    0,                                     // refFieldCount
    nullptr,                               // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

// Box-class interface tables. Shared dummy slot maps: dispatch on boxed
// primitives is unwired (vtableSize 0) so any dispatch attempt aborts loudly.
// isinst/as never reads slotMap (only interfaces[i].iface is compared).
static int32_t kBoxUnwiredSlotMap[] = { 0 };

// Numeric boxes (Int32 / UInt32 / Double): IConvertible + IComparable + IFormattable.
static InterfaceMapEntry kBoxNumericInterfaces[] = {
    { &System_IConvertible_class, kBoxUnwiredSlotMap },
    { &System_IComparable_class,  kBoxUnwiredSlotMap },
    { &System_IFormattable_class, kBoxUnwiredSlotMap },
};

// Boolean / Char: IConvertible + IComparable (no IFormattable -- .NET).
static InterfaceMapEntry kBoxComparableInterfaces[] = {
    { &System_IConvertible_class, kBoxUnwiredSlotMap },
    { &System_IComparable_class,  kBoxUnwiredSlotMap },
};

// Boxed Int32: header + one aligned 8-byte payload slot (instanceSize 16).
Il2CppClass System_Int32_class = {
    "System.Int32",                        // name
    16,                                    // instanceSize: header + aligned value slot
    0u,                                    // refFieldBitmap
    0,                                     // refFieldCount
    &System_Object_class,                  // baseClass (a boxed int IS-A object)
    nullptr,                               // vtable
    0,                                     // vtableSize
    kBoxNumericInterfaces,                 // interfaces
    3,                                     // interfaceCount
};

Il2CppClass System_Double_class = {
    "System.Double",                       // name
    16,                                    // instanceSize: header + aligned value slot
    0u,                                    // refFieldBitmap
    0,                                     // refFieldCount
    &System_Object_class,                  // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    kBoxNumericInterfaces,                 // interfaces
    3,                                     // interfaceCount
};

// nb9 D10: Int64/Single element descriptors (the primitive array element-kind
// registry needs a stable pointer identity for long[]/float[]). Same shape as
// the Int32/Double boxes.
Il2CppClass System_Int64_class = {
    "System.Int64",
    16,
    0u,
    0,
    &System_Object_class,
    nullptr,
    0,
    kBoxNumericInterfaces,
    3,
};

Il2CppClass System_Single_class = {
    "System.Single",
    16,
    0u,
    0,
    &System_Object_class,
    nullptr,
    0,
    kBoxNumericInterfaces,
    3,
};

//------------------------------------------------------------------------------
// box-identity (box_long_is / objectops): the REMAINING primitive boxes live
// here too, NOT as file-local duplicates in boxingext2.cpp.
//
// Root cause this closes: the transpiler emits `is`/`as` on a box-only
// primitive as il2cpp_is_instance(obj, il2cpp_class_by_name("<full name>"))
// (CodeGenerator.BoxClassDescriptorCpp), and il2cpp_is_instance is a POINTER
// compare. A second descriptor with the same .NET name made class_by_name
// return the metadata one while the box icall stamped the boxingext2 one, so
// `boxed long is long` answered False. ONE descriptor per .NET type is the
// invariant: boxingext2.cpp now aliases these (g_*BoxClass references) and
// registers nothing of its own, so box stamps == class_by_name result.
//
// Shape is identical to the Int32/Double boxes (header + aligned payload slot;
// Decimal carries a 16-byte slot, instanceSize 32). Boolean/Char already use
// the narrower kBoxComparableInterfaces table above.
//------------------------------------------------------------------------------
Il2CppClass System_Byte_class = {
    "System.Byte",
    16,
    0u,
    0,
    &System_Object_class,
    nullptr,
    0,
    kBoxNumericInterfaces,
    3,
};

Il2CppClass System_SByte_class = {
    "System.SByte",
    16,
    0u,
    0,
    &System_Object_class,
    nullptr,
    0,
    kBoxNumericInterfaces,
    3,
};

Il2CppClass System_Int16_class = {
    "System.Int16",
    16,
    0u,
    0,
    &System_Object_class,
    nullptr,
    0,
    kBoxNumericInterfaces,
    3,
};

Il2CppClass System_UInt16_class = {
    "System.UInt16",
    16,
    0u,
    0,
    &System_Object_class,
    nullptr,
    0,
    kBoxNumericInterfaces,
    3,
};

Il2CppClass System_UInt64_class = {
    "System.UInt64",
    16,
    0u,
    0,
    &System_Object_class,
    nullptr,
    0,
    kBoxNumericInterfaces,
    3,
};

Il2CppClass System_Decimal_class = {
    "System.Decimal",
    32, // header + aligned 16-byte payload slot (Il2CppDecimal)
    0u,
    0,
    &System_Object_class,
    nullptr,
    0,
    kBoxNumericInterfaces,
    3,
};

// nullable (GN-2): the 8-byte / 16-byte BCL VALUE-type box descriptors.
// A Nullable<T> value is represented as an Il2CppObject* holding a box of the
// underlying T, so DateTime/TimeSpan/Guid need real box classes (exactly like
// the primitive boxes: header + aligned payload slot, System.Object base, no
// reference fields) -- icall_object_to_string / icall_object_get_hash_code /
// icall_boxed_equals_ext2 dispatch on klass->name, and a boxed value must
// carry its OWN type identity. TimeSpan is 8 bytes (ticks), DateTime is 8
// bytes (ticks; kind is normalized to 0 by the box), Guid is 16 bytes.
Il2CppClass System_DateTime_class = {
    "System.DateTime",
    16, // header + aligned 8-byte ticks payload
    0u,
    0,
    &System_Object_class,
    nullptr,
    0,
    nullptr,
    0,
};

Il2CppClass System_TimeSpan_class = {
    "System.TimeSpan",
    16,
    0u,
    0,
    &System_Object_class,
    nullptr,
    0,
    nullptr,
    0,
};

Il2CppClass System_Guid_class = {
    "System.Guid",
    32, // header + aligned 16-byte payload
    0u,
    0,
    &System_Object_class,
    nullptr,
    0,
    nullptr,
    0,
};

// box-identity (audit A-c): the transpiler's default descriptor arm emits
// `&System_<Name>_class` for every type without an explicit mapping, so
// `obj is IntPtr` / `obj is UIntPtr` referenced these symbols -- which did not
// exist (link/compile error, proven with a standalone probe). They are
// identity-only descriptors here: the transpiler REJECTS boxing an IntPtr
// ("box of System.IntPtr not supported"), so no box can ever carry them and
// `is IntPtr` is False for every existing object, exactly like net48 (a
// boxed IntPtr is the only way to make it True). Shape mirrors the box layout
// (16 bytes, no reference fields) so a future IntPtr box needs no rework.
Il2CppClass System_IntPtr_class = {
    "System.IntPtr",
    16,
    0u,
    0,
    &System_Object_class,
    nullptr,
    0,
    nullptr,
    0,
};

Il2CppClass System_UIntPtr_class = {
    "System.UIntPtr",
    16,
    0u,
    0,
    &System_Object_class,
    nullptr,
    0,
    nullptr,
    0,
};

// Boxed UInt32: identical shape to the Int32 box (header + aligned 8-byte
// payload slot, the full 32-bit pattern -- see boxingext2.cpp), plus the .NET
// interface table. net48 semantics: a boxed UInt32 IS-A System.Object (base
// chain) and implements IConvertible/IComparable/IFormattable, exactly like
// System_Int32_class. Registered with the builtins (il2cpp_ensure_builtin_
// system_classes) so il2cpp_class_by_name("System.UInt32") and generated
// isinst/castclass (`&System_UInt32_class`) resolve before any box exists.
// boxingext2.cpp's g_uintBoxClass is an alias of this descriptor (pointer
// identity: boxed uints stamp the SAME descriptor the type checks compare).
Il2CppClass System_UInt32_class = {
    "System.UInt32",                       // name
    16,                                    // instanceSize: header + aligned value slot
    0u,                                    // refFieldBitmap
    0,                                     // refFieldCount
    &System_Object_class,                  // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    kBoxNumericInterfaces,                 // interfaces
    3,                                     // interfaceCount
};

Il2CppClass System_Boolean_class = {
    "System.Boolean",                      // name
    16,                                    // instanceSize: header + aligned value slot
    0u,                                    // refFieldBitmap
    0,                                     // refFieldCount
    &System_Object_class,                  // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    kBoxComparableInterfaces,              // interfaces
    2,                                     // interfaceCount
};

Il2CppClass System_Char_class = {
    "System.Char",                         // name
    16,                                    // instanceSize: header + aligned value slot
    0u,                                    // refFieldBitmap
    0,                                     // refFieldCount
    &System_Object_class,                  // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    kBoxComparableInterfaces,              // interfaces
    2,                                     // interfaceCount
};

// System.Resources.ResourceManager + System.Globalization.CultureInfo:
// HEADER-ONLY handle descriptors (resmgmt/resops wiring, b23). The
// transpiler's ResourceManager ctor icall resolves the "<BaseName>.resources"
// blob and stores it INTO the receiver local (ResultIsReceiver handle-gap --
// the XmlDocument.LoadXml precedent), so the allocated RM object is a
// discarded placeholder never dereferenced. CultureInfo objects are only ever
// passed as the DROPPED culture argument of GetString(name, culture) -- the
// invariant-pinned ILC model -- so they are never dereferenced either.
// instanceSize = header only (no payload); refFieldBitmap 0 (no GC refs).
Il2CppClass System_Resources_ResourceManager_class = {
    "System.Resources.ResourceManager",    // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)), // instanceSize: header only
    0u,                                    // refFieldBitmap: no reference fields
    0,                                     // refFieldCount
    &System_Object_class,                  // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

Il2CppClass System_Globalization_CultureInfo_class = {
    "System.Globalization.CultureInfo",    // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)), // instanceSize: header only
    0u,                                    // refFieldBitmap: no reference fields
    0,                                     // refFieldCount
    &System_Object_class,                  // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

//------------------------------------------------------------------------------
// Managed EXCEPTION descriptors (excmsg): `new ArgumentException("bad","p")`
// must be a REAL object with a class identity, because GetType().Name and
// isinst/as on a CONSTRUCTED instance read obj->klass (typecheck.cpp) and the
// message/paramName side table keys by object pointer (stubs.cpp). Before these
// descriptors existed the Argument* allocations folded to nullptr, so every
// identity query saw a null receiver.
//
// The base chain mirrors .NET Framework 4.8 exactly:
//   System.Exception : System.Object
//   System.SystemException : System.Exception
//   System.ArgumentException : SystemException
//   System.ArgumentNullException : ArgumentException
//   System.ArgumentOutOfRangeException : ArgumentException
// so isinst walks the SAME inheritance relations the throw-carrier chains
// spell out (il2cpp_exception_matches), keeping the constructed-instance path
// and the catch-carrier path consistent.
//
// instanceSize: header + THREE reference slots (message, paramName, inner) --
// the payload lives in the runtime side table (stubs.cpp), not in the object,
// so the slots only need to exist for the allocation to be a valid managed
// object. refFieldBitmap stays 0: the side table roots its entries itself
// (il2cpp_gc_add_static_root), so the collector must NOT scan these objects.
//------------------------------------------------------------------------------
Il2CppClass System_Exception_class = {
    "System.Exception",                    // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)), // instanceSize: header only
    0u,                                    // refFieldBitmap: side table owns the refs
    0,                                     // refFieldCount
    &System_Object_class,                  // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

Il2CppClass System_SystemException_class = {
    "System.SystemException",              // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)), // instanceSize: header only
    0u,                                    // refFieldBitmap
    0,                                     // refFieldCount
    &System_Exception_class,               // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

Il2CppClass System_ArgumentException_class = {
    "System.ArgumentException",            // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)), // instanceSize: header only
    0u,                                    // refFieldBitmap
    0,                                     // refFieldCount
    &System_SystemException_class,         // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

Il2CppClass System_ArgumentNullException_class = {
    "System.ArgumentNullException",        // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)), // instanceSize: header only
    0u,                                    // refFieldBitmap
    0,                                     // refFieldCount
    &System_ArgumentException_class,       // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

Il2CppClass System_ArgumentOutOfRangeException_class = {
    "System.ArgumentOutOfRangeException",  // name
    static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)), // instanceSize: header only
    0u,                                    // refFieldBitmap
    0,                                     // refFieldCount
    &System_ArgumentException_class,       // baseClass
    nullptr,                               // vtable
    0,                                     // vtableSize
    nullptr,                               // interfaces
    0,                                     // interfaceCount
};

namespace il2cpp {

//------------------------------------------------------------------------------
// il2cpp_class_register -- add a type descriptor to the registry.
//
//   Idempotent: registering the same descriptor twice (e.g. from two
//   translation units) is a no-op, keeping the table free of duplicates.
//   Null and registry-overflow inputs are rejected: null is a caller bug that
//   must not crash the table, overflow is a hard error surfaced as abort().
//------------------------------------------------------------------------------
void il2cpp_class_register(Il2CppClass* klass)
{
    if (klass == nullptr)
    {
        return;
    }
    for (int i = 0; i < g_registeredClassCount; ++i)
    {
        if (g_registeredClasses[i] == klass)
        {
            return;
        }
    }
    if (g_registeredClassCount >= kMaxRegisteredClasses)
    {
        std::fprintf(stderr, "il2cpp_class_register: registry overflow (%d types)\n", kMaxRegisteredClasses);
        std::abort();
    }
    g_registeredClasses[g_registeredClassCount] = klass;
    ++g_registeredClassCount;
}

//------------------------------------------------------------------------------
// il2cpp_class_by_name -- linear search for a descriptor by full type name.
//
//   M2 registry is small (O(#types) <= 256) so a linear scan is both simpler
//   and cache-friendlier than any hashed structure. Names are compared as raw
//   C strings; the transpiler emits identical name literals for the same type
//   in every translation unit, so pointer/contents equality agree. Returns
//   nullptr when the name is unknown or the input is null -- callers must
//   treat null as "type not registered".
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_class_by_name(const char* name)
{
    if (name == nullptr)
    {
        return nullptr;
    }
    for (int i = 0; i < g_registeredClassCount; ++i)
    {
        if (std::strcmp(g_registeredClasses[i]->name, name) == 0)
        {
            return g_registeredClasses[i];
        }
    }
    return nullptr;
}

//------------------------------------------------------------------------------
// il2cpp_interface_dispatch -- resolve an interface call's vtable slot.
//
//   Linear search over the interface maps for the entry whose iface
//   descriptor matches; the entry's slotMap maps the interface slot to the
//   type's vtable slot. Returns the ADDRESS of the vtable slot (a void** the
//   caller casts to a function pointer and calls). M2 keeps the search linear
//   (interface counts are small and the map is cache-friendly); a hash-indexed
//   interface map is an M3 optimization.
//
//   Audit H7: the map may live on a BASE class -- a derived type inherits
//   its base's interfaces without re-declaring them. The scan walks the
//   baseClass chain (same pattern as il2cpp_is_instance's interface scan).
//   The SLOT resolves in the RECEIVER's vtable: a derived override replaces
//   the base slot at the same index (the planner's slot layout is inherited
//   base-first), so dispatch through the inherited map reaches the most
//   derived implementation -- .NET semantics.
//
//   Failures are programming errors (the compiler guarantees the receiver
//   implements the interface at every call site) and abort loudly.
//------------------------------------------------------------------------------
void** il2cpp_interface_dispatch(Il2CppObject* obj, Il2CppClass* iface, int32_t ifaceSlot)
{
    Il2CppClass* klass = obj->klass;
    if (klass == nullptr)
    {
        std::fprintf(stderr, "il2cpp_interface_dispatch: receiver has no class descriptor\n");
        std::abort();
    }
    if (ifaceSlot < 0)
    {
        std::fprintf(stderr, "il2cpp_interface_dispatch: malformed interface table\n");
        std::abort();
    }
    for (Il2CppClass* scan = klass; scan != nullptr; scan = scan->baseClass)
    {
        if (scan->interfaces == nullptr && scan->interfaceCount > 0)
        {
            std::fprintf(stderr, "il2cpp_interface_dispatch: malformed interface table\n");
            std::abort();
        }
        for (int32_t i = 0; i < scan->interfaceCount; ++i)
        {
            const InterfaceMapEntry& entry = scan->interfaces[i];
            if (entry.iface == iface)
            {
                if (entry.slotMap == nullptr)
                {
                    std::fprintf(stderr, "il2cpp_interface_dispatch: malformed slot map\n");
                    std::abort();
                }
                const int32_t typeSlot = entry.slotMap[ifaceSlot];
                if (typeSlot < 0 || typeSlot >= klass->vtableSize)
                {
                    std::fprintf(stderr, "il2cpp_interface_dispatch: slot map out of range\n");
                    std::abort();
                }
                return &klass->vtable[typeSlot];
            }
        }
    }
    std::fprintf(stderr, "il2cpp_interface_dispatch: %s does not implement %s\n",
        klass->name != nullptr ? klass->name : "(unnamed)", iface->name != nullptr ? iface->name : "(unnamed)");
    // Unreachable in correct programs; abort() is [[noreturn]] on every
    // supported compiler, so no return statement follows (a trailing return
    // would trigger MSVC /W4 C4702 unreachable-code).
    std::abort();
}

//------------------------------------------------------------------------------
// il2cpp_ensure_builtin_system_classes -- register every builtin descriptor.
//
//   Runs the registration block EXACTLY ONCE (std::call_once): every string
//   allocation goes through this function (string.cpp EnsureStringRegistered),
//   and the earlier per-allocation full re-run made each builtin register do a
//   fresh linear scan of the growing registry (bench v7 str_concat regression:
//   10 builtins -> 55 compares/allocation, 13 -> 91; the 500K-allocation
//   str_concat loop amplified it 0.81 -> 0.65). call_once is thread-safe and
//   idempotent -- concurrent first callers serialize; the registration block
//   itself stays exactly the list below (il2cpp_class_register is already a
//   no-op for a re-registered pointer, so the once-guard changes no semantics,
//   only the cost).
//
//   Call before any isinst/castclass/class_by_name that may target a builtin
//   System type (see il2cpp_metadata.h).
//------------------------------------------------------------------------------
void il2cpp_ensure_builtin_system_classes()
{
    static std::once_flag s_builtinSystemOnce;
    std::call_once(s_builtinSystemOnce, [] {
        il2cpp_class_register(&System_Object_class);
        il2cpp_class_register(&System_ValueType_class);
        il2cpp_class_register(&System_String_class);
        il2cpp_class_register(&System_Int32_class);
        il2cpp_class_register(&System_UInt32_class);
        il2cpp_class_register(&System_Boolean_class);
        il2cpp_class_register(&System_Char_class);
        il2cpp_class_register(&System_Double_class);
    // nb9 D10: the primitive element-kind descriptors for long[]/float[].
    il2cpp_class_register(&System_Int64_class);
    il2cpp_class_register(&System_Single_class);
        // box-identity: the remaining primitive box descriptors (registered
        // BEFORE any box can be allocated, so class_by_name always answers the
        // descriptor the box icalls stamp -- no first-use ordering hazard).
        il2cpp_class_register(&System_Byte_class);
        il2cpp_class_register(&System_SByte_class);
        il2cpp_class_register(&System_Int16_class);
        il2cpp_class_register(&System_UInt16_class);
        il2cpp_class_register(&System_UInt64_class);
        il2cpp_class_register(&System_Decimal_class);
        // box-identity (audit A-c): identity-only IntPtr/UIntPtr descriptors
        // (generated `is IntPtr` references the symbol; boxing IntPtr is a
        // transpiler reject, so nothing ever carries them).
        il2cpp_class_register(&System_IntPtr_class);
        il2cpp_class_register(&System_UIntPtr_class);
        // excmsg: the managed exception hierarchy (constructed instances need a
        // real klass for GetType()/isinst; see the descriptor block above).
        il2cpp_class_register(&System_Exception_class);
        il2cpp_class_register(&System_SystemException_class);
        il2cpp_class_register(&System_ArgumentException_class);
        il2cpp_class_register(&System_ArgumentNullException_class);
        il2cpp_class_register(&System_ArgumentOutOfRangeException_class);
        il2cpp_class_register(&System_IConvertible_class);
        il2cpp_class_register(&System_IComparable_class);
        il2cpp_class_register(&System_IFormattable_class);
        // nullable (GN-2): the DateTime/TimeSpan/Guid box descriptors (the
        // Nullable<T> representation boxes the underlying T, so these must
        // resolve by name before the first nullable box is allocated).
        il2cpp_class_register(&System_DateTime_class);
        il2cpp_class_register(&System_TimeSpan_class);
        il2cpp_class_register(&System_Guid_class);
        // rtdict-b23 (DICT-GC): the Dictionary`2<string,int> GC-object wrapper
        // descriptor (defined in dictsi_obj.cpp, extern in dictsi_obj.h).
        // Registered here so
        // il2cpp_class_by_name("System.Collections.Generic.Dictionary`2<...>")
        // and generated isinst/castclass conformance scans resolve it before any
        // dictionary object exists (http.cpp WebClient precedent).
        il2cpp_class_register(&System_Collections_Generic_Dictionary_2_string_int_class);
        // resmgmt/resops (b23): the ResourceManager + CultureInfo handle
        // descriptors (defined above, global scope). Registered so
        // il2cpp_class_by_name / generated isinst/castclass conformance scans
        // resolve them before any instance exists (WebClient precedent).
        il2cpp_class_register(&System_Resources_ResourceManager_class);
        il2cpp_class_register(&System_Globalization_CultureInfo_class);
    });
}

} // namespace il2cpp
