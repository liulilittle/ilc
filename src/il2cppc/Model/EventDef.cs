namespace Il2Cpp.Transpiler.Model;

//------------------------------------------------------------------------------
// EventDef -- one metadata event of a translated type (COM-C3 / RF-N1).
//
// The Event table row (ECMA-335 II.22.14) carries the event name and the
// handler DELEGATE type; the METHOD SEMANTICS table associates the add (0x58)
// / remove (0x59) / raise (0x5A) accessor methods with the event. Loaded from
// the EventMap table (ECMA-335 II.22.13: per-type event ranges) by
// MetadataLoader and emitted into the type's Il2CppTypeInfo event sub-table.
//
// Runtime contract (typeinfo.h / the Il2CppPropertyInfo precedent):
//   * add/remove are stored as METHOD NAMES (const char*), NOT function
//     pointers -- "single source of truth": the runtime resolves the accessor
//     by name through the SAME methods table (never a second fnPtr copy).
//   * eventIID = the folded [Guid] of the event's COM SOURCE interface (the
//     "event -> source interface -> [Guid]" chain, 16 raw bytes, .NET Guid
//     byte layout -- Data1/2/3 little-endian, Data4 in order). A zeroed IID
//     means a MANAGED-ONLY event (no COM event-source interface resolved) --
//     still enumerated by GetEvents; the C3-2 RCW attach path LOUD-rejects it.
//------------------------------------------------------------------------------
public sealed class EventDef
{
    // Event name (metadata Event.Name, e.g. "Tick").
    public required string Name { get; init; }

    // Declaring type (the TypeDef whose EventMap range the row belongs to).
    public required TypeDef Owner { get; init; }

    // Add accessor METHOD NAME (the compiler's add_Tick), null when absent.
    public string? AddName { get; init; }

    // Remove accessor METHOD NAME (the compiler's remove_Tick), null when absent.
    public string? RemoveName { get; init; }

    // The COM event-source interface IID (16 raw bytes; zeroed = managed-only).
    public byte[] EventIID { get; set; } = new byte[16];
}
