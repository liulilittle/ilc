using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// VTablePlanner -- virtual-slot layout for the translated type graph.
//
// The .NET virtual dispatch model (ECMA-335 II.9.3 + CLR behaviour):
//   * every virtual method of a type occupies a slot; the slot table is
//     inherited from the base type and extended by new virtuals;
//   * an override REPLACES the base slot with the derived implementation
//     (same signature = same slot);
//   * a derived type that declares a virtual with the SAME signature but
//     WITHOUT override ('new' keyword) gets a NEW slot.
//
// M2 simplification:
//   * the base of the hierarchy is System.Object (runtime type, no translated
//     methods) -- its slot count is 0, so the first translated virtual starts
//     at slot 0;
//   * override matching is (Name, ParamTypes) -- the same key the method-name
//     hash uses, so a base/derived override pair is recognized across the
//     type graph;
//   * user-declared base types must be in the translated set (the planner
//     throws on an unresolvable base with virtuals -- a compile-time closure
//     error, per the trimming rules).
//
// Output (per TypeDef):
//   VirtualMethods : the type's EFFECTIVE virtual table (inherited slots +
//                    appended/replaced), in slot order -- index = slot
//   MethodDef.VTableSlot : the slot of each virtual method (for callvirt
//                    emission), -1 for non-virtuals
//   Reflected into the Il2CppClass descriptor as the vtable pointer.
//------------------------------------------------------------------------------
public static class VTablePlanner
{
    public static void PlanAll(IReadOnlyList<TypeDef> types)
    {
        // Topological order: bases before deriveds (stable by FullName for
        // determinism -- generation must be reproducible). R2b: the dedup key
        // is InstantiationKey (FullName of the DEFINITION + "|" + suffix for
        // closed clones) -- a clone shares the definition's FullName (the
        // C++ symbol/hash channels key on the definition name), so FullName
        // alone would make the planner skip it.
        var planned = new HashSet<string>();
        foreach (var t in types.OrderBy(t => t.InstantiationKey))
            Plan(t, planned);

        // Task 8: interface dispatch maps -- for each CONCRETE type, resolve
        // each implemented interface's method slots to the type's own slots.
        // A type's vtable must CONTAIN the interface methods (interface impls
        // are virtuals added by Plan via signature matching or append).
        foreach (var t in types.Where(t => !t.IsInterface))
            BuildInterfaceMaps(t);

        // Devirt S1 (G3, closed-world sealing): derived-count pass -- every
        // type's DerivedCount = the number of translated types whose base chain
        // passes through it. A type with DerivedCount == 0 has NO subclass in
        // the translation set; since the transpiler compiles a single assembly
        // (the `types` list IS the closed world), such a type is effectively
        // sealed and its callvirt slots resolve statically (IsEffectivelySealed).
        foreach (var t in types) t.DerivedCount = 0;
        foreach (var t in types)
            for (var b = t.BaseType; b != null; b = b.BaseType)
                b.DerivedCount++;

        // Devirt S3 (closed-world interface single-implementer): for each
        // interface, count the translated types implementing it. The
        // InterfaceSlotMaps key set (filled by BuildInterfaceMaps above) is
        // exactly the implementation set -- own + inherited through the base
        // chain (audit H7). An interface with InterfaceImplementerCount == 1
        // has ONE statically-known implementation: any object assigned to the
        // interface must be that single type, so its dispatch slots resolve
        // statically (interface callvirt -> direct call, §S3).
        //
        // Safety: ALL non-interface implementers are counted -- including value
        // types (a struct implementing the interface can be boxed into an
        // interface-typed receiver) and abstract classes. A single-implementer
        // interface therefore has NO other translated implementer of any kind,
        // so no class/struct can diverge at runtime from the static resolution.
        foreach (var iface in types.Where(t => t.IsInterface))
        {
            iface.InterfaceImplementerCount = types.Count(t => !t.IsInterface
                && t.InterfaceSlotMaps.ContainsKey(iface));
        }
    }

    //--------------------------------------------------------------------------
    // BuildInterfaceMaps -- per-type interface slot maps (Task 8).
    //
    //   map[interface slot] = this type's vtable slot implementing it.
    //   Matching rule: the type's effective vtable contains a method whose
    //   signature equals the interface method's signature. When the interface
    //   method was appended as a NEW slot (its signature was not among the
    //   class's declared virtuals at Plan time -- e.g. implicit interface
    //   implementation of a non-virtual public method), Plan must have made
    //   the class method a virtual slot; the map then points at it.
    //--------------------------------------------------------------------------
    private static void BuildInterfaceMaps(TypeDef t)
    {
        // Audit H7: a derived type INHERITS its base's interfaces even when it
        // does not re-declare them (C# `class D : Base {}` where Base : IFoo).
        // The derived descriptor must carry maps for the whole interface set,
        // or il2cpp_interface_dispatch aborts on a Derived instance. The
        // inherited interface methods are already in the derived vtable (the
        // planner copies base slots), so signature matching resolves them.
        var interfaces = new List<TypeDef>();
        void Add(TypeDef type)
        {
            foreach (var iface in type.InterfaceTypes)
                if (!interfaces.Contains(iface)) interfaces.Add(iface);
        }
        Add(t); // the type's own interfaces first (re-implementation wins)
        for (var b = t.BaseType; b != null; b = b.BaseType) Add(b);

        foreach (var iface in interfaces)
        {
            var slotMap = new int[iface.VirtualMethods.Count];
            for (var islot = 0; islot < iface.VirtualMethods.Count; islot++)
            {
                var sig = SigKey(iface.VirtualMethods[islot]);
                var slot = t.VirtualMethods.FindIndex(m => m != null && SigKey(m) == sig);
                if (slot < 0)
                {
                    Console.Error.WriteLine($"warning: interface {iface.FullName}::{sig} not implemented by {t.FullName} vtable -- skipping");
                    slotMap[islot] = -1;
                    continue;
                }
                slotMap[islot] = slot;
            }
            t.InterfaceSlotMaps[iface] = slotMap;
        }
    }

    private static void Plan(TypeDef t, HashSet<string> planned)
    {
        if (!planned.Add(t.InstantiationKey)) return; // already planned (idempotent)

        // Recurse into the base chain first (base slots precede derived slots).
        if (t.BaseType != null) Plan(t.BaseType, planned);

        // Start from the base's effective table (deep copy of slot order).
        var slots = t.BaseType != null
            ? new List<MethodDef>(t.BaseType.VirtualMethods)
            : new List<MethodDef>();

        // Declared virtuals: a virtual whose signature matches an inherited
        // slot OVERRIDES it (same slot); a virtual with no matching base slot
        // appends a new slot. The ReuseSlot/NewSlot attribute bits are NOT
        // authoritative for the slot layout (Roslyn emits neither for C#
        // 'override' -- verification below), so signature matching is the
        // single rule (ECMA-335 II.9.3 semantics: derived slot replaces base
        // slot when names+signatures match).
        //
        // Task 8: implicit interface implementations are also virtual slots.
        // A concrete method whose signature matches an interface method must
        // be dispatchable through the interface slot even when it is NOT
        // declared virtual (C# allows implicit interface impls without the
        // 'virtual' keyword). We collect such methods as pseudo-virtuals.
        var interfaceSigs = t.InterfaceTypes
            .SelectMany(i => i.VirtualMethods)
            .Select(SigKey)
            .ToHashSet();
        var slotCandidates = t.Methods
            .Where(m => !m.IsStatic)
            .Where(m => m.IsVirtual || interfaceSigs.Contains(SigKey(m)))
            .OrderBy(m => m.Name).ThenBy(m => m.ParamTypes)
            .ToList();

        foreach (var m in slotCandidates)
        {
            var sig = SigKey(m);
            var existing = slots.FindIndex(s => s != null && SigMatchesSlot(s, m, sig));
            if (existing >= 0)
            {
                // Signature match on an inherited slot: derived implementation
                // replaces the base slot (C# override semantics).
                slots[existing] = m;
                m.VTableSlot = existing;
            }
            else
            {
                m.VTableSlot = slots.Count;
                slots.Add(m);
            }
        }

        foreach (var m in t.VirtualMethods) m.VTableSlot = -1; // reset stale
        t.VirtualMethods.Clear();
        foreach (var m in slots) t.VirtualMethods.Add(m);
    }

    // Slot-identity key: (Name, ParamTypes) -- identical to the method-name
    // hash input, so override pairs are recognized consistently.
    internal static string SigKey(MethodDef m) => $"{m.Name}({m.ParamTypes})";

    // R2b P2 (closed clone override): a reference instantiation's VAR slots
    // carry the managed-reference ABI (Il2CppObject*) while the metadata
    // signature keeps the definition's "i" encoding (the hash channel is
    // untouched). A derived override's decoded argument ("r:System.String")
    // must match such a slot; exact keys always win, and a clone VAR slot
    // matches any "r:" argument. Non-clone slots keep exact matching only.
    private static bool SigMatchesSlot(MethodDef slot, MethodDef derived, string derivedKey)
    {
        if (SigKey(slot) == derivedKey) return true;
        if (slot.Name != derived.Name) return false;
        var suffix = slot.Owner.InstantiationSuffix;
        if (suffix == null || suffix == "int") return false;
        var sp = slot.ParamTypes.Length == 0
            ? Array.Empty<string>()
            : CodeGenerator.SplitTopLevel(slot.ParamTypes).ToArray();
        var dp = derived.ParamTypes.Length == 0
            ? Array.Empty<string>()
            : CodeGenerator.SplitTopLevel(derived.ParamTypes).ToArray();
        if (sp.Length != dp.Length || sp.Length == 0) return false;
        for (var i = 0; i < sp.Length; i++)
        {
            if (sp[i] == dp[i]) continue;
            var isVar = slot.ParamIsVar is { } piv && i < piv.Length && piv[i];
            if (isVar && dp[i].StartsWith("r:", StringComparison.Ordinal)) continue;
            return false;
        }
        return true;
    }
}
