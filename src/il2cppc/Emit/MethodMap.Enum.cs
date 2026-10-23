namespace Il2Cpp.Transpiler.Emit;

using Il2Cpp.Transpiler.Model;

//------------------------------------------------------------------------------
// MethodMap.Enum -- System.Enum icall mappings (enumext.h).
// Owners: System.Enum
//
// Runtime model (see enumext.h): enum metadata lives in a SEPARATE
// name-keyed registry (il2cpp_enum_register), NOT in Il2CppClass -- value
// types have no class descriptor in this runtime. All API values travel as
// int64_t (the int64 carrier; the generated code widens the enum's raw
// int32 slot before the call). The registry lookup is by Il2CppClass NAME;
// a null / unknown descriptor takes the documented Int32 no-metadata
// assumption (see enumext.h's fallback table).
//
// WIRED SURFACE (enumface closure, D-STREN-2 -- the name-string ABI):
//   GetName(Type, object)       -> il2cpp_enum_get_name_by_type_name
//   GetNames(Type)              -> il2cpp_enum_get_names_by_type_name
//                                  (string[], declaration order)
//   GetUnderlyingType(Type)     -> il2cpp_enum_get_underlying_type
//   Parse(Type, string)         -> il2cpp_enum_parse_boxed (boxed result,
//                                  ArgumentException on failure; the
//                                  TryParse-shaped il2cpp_enum_parse has an
//                                  int64_t* out slot -- no ref/out support
//                                  in the M-stage transpiler, so Parse maps
//                                  to the value-returning boxed variant)
//   HasFlag(Enum)               -> il2cpp_enum_has_flag (boxed-receiver
//                                  flags bit test, (self & flag) == flag)
//   ToString()                  -> il2cpp_enum_to_string (interim ABI:
//                                  PrependZeroArg injects a null class
//                                  descriptor, so the runtime takes its
//                                  documented Int32 no-metadata path --
//                                  numeric decimal string; the named-value
//                                  path becomes reachable once the
//                                  descriptor emission lands, see the
//                                  register wiring report below)
// Known-but-unwireable members (Format / IsDefined / GetValues / ToObject /
//   TryParse / ToString(string) / Parse(Type,string,bool)) throw
//   NotSupportedException (LOUD, the BCL wiring convention). Anything else
//   returns null -> the generated-name fallback.
//
// TYPE-ARGUMENT SLOT (name-string ABI, enumface): the runtime's enum
// primitives traditionally took the enum type as an Il2CppClass* descriptor
// (the registry lookup dereferences enumClass->name, enumext.cpp FindInfo).
// The transpiler's typeof(T) fold produces the type's FULL-NAME STRING (the
// ldtoken + Type::GetTypeFromHandle fold, StackFold.cs), NOT a descriptor --
// so the GetName/GetNames/Parse mappings above target the NAME-STRING
// runtime variants (il2cpp_enum_*_by_type_name / il2cpp_enum_parse_boxed),
// which resolve the registry by exactly the folded name. The VALUE slot is
// the boxed enum's int64 carrier ("unbox:l" -- icall_unbox_long), matching
// enumext.h's int64-travels-by-design contract.
//
// ENUM-INSTANCE ToString DISPATCH (probe-verified against net48): a real
// enum-VALUE ToString() compiles to ldloca + constrained. + callvirt
// instance string System.Object::ToString() -- the MemberRef owner is
// System.Object, NOT System.Enum -- so this dispatch case never sees it; it
// routes through TryMapObject (MethodMap.Object.cs), whose receiver-typed
// ToString list has no enum handling today (and cannot distinguish an enum
// receiver from other value types without knownTypes access). The System.Enum
// owner DOES occur for Enum-typed REFERENCES (boxed enum values, e.g.
// System.Enum e = Color.Red; e.ToString();) -- the mapping below covers that
// shape. Both paths are currently unreachable end-to-end: box of an enum
// value is LOUD-rejected in CodeGenerator's box_ switch (no enum box helper
// yet), and the constrained/ldloca shape has an untracked receiver. See the
// report below for the full gap chain.
//
// REGISTER WIRING REPORT -- il2cpp_enum_register in the enum's static init.
// The register side cannot be emitted from this file (the dispatch table and
// the code generator are outside this domain); the scheme, grounded in the
// two existing static-init mechanisms:
//
//   a. Metadata: MetadataLoader must read each enum TypeDef's STATIC LITERAL
//      fields (td.GetFields(), field.Attributes & FieldAttributes.Literal +
//      field.GetDefaultValue() -> the constant blob) -- member names + values
//      in declaration order (the runtime matches by value, order-insensitive);
//      the underlying TypeCode from the enum's base type (SByte=5 .. UInt64=12,
//      enumext.h kEnumUnderlying*).
//   b. Generated statics (per enum type, in its own .cpp, next to the type's
//      other statics): a caller-owned Il2CppEnumInfo --
//         static const char* const s_{T}_names[] = { "Red", "Green" };
//         static const int64_t      s_{T}_values[] = { 1, 2 };
//         static const il2cpp::runtime::Il2CppEnumInfo s_{T}_enumInfo =
//             { 9, 2, s_{T}_names, s_{T}_values };
//      (registry stores pointers only -- the statics must outlive it).
//   c. Registration call sites (the two existing static-init mechanisms):
//      - Enum types WITH a .cctor: inside ensure_cctor_{T} (CodeGenerator.cs
//        cctor emission), right after the done-flag is set and BEFORE the
//        .cctor body call -- once-only, before any user code runs (the same
//        hook the attribute-registration scheme uses, MethodMap.Attribute.cs).
//      - Enum types WITHOUT a .cctor: in il2cpp_runtime_init (entry.cpp
//        emission) -- startup, single-threaded, before user code. NOTE: the
//        existing type-registration loop there SKIPS value types
//        (`if (t.IsValueType) continue;` -- enums have no descriptor, and
//        il2cpp_class_register must NOT be called for them); the register
//        emission needs its OWN loop over the assembly's enum types.
//      Both satisfy enumext.h's startup-only, no-sync contract.
//   d. Capacity: kMaxRegisteredEnums = 32 (enumext.cpp) -- the transpiler
//      must count enum types at compile time and LOUD-reject beyond 32 (the
//      compile-time count is the right place to surface it; same pattern as
//      the attribute registry, MethodMap.Attribute.cs).
//   e. What makes the registered metadata REACHABLE (the remaining gap
//      chain, all outside this file's domain):
//        1. ldtoken/typeof emission (IlDecoder/StackFold/CodeGenerator) --
//           every GetName/Parse/GetUnderlyingType call site starts with
//           ldtoken + Type::GetTypeFromHandle (probe-verified IL), and
//           ldtoken has no fold today.
//        2. Enum class descriptors: the API icalls take Il2CppClass* and
//           FindInfo reads enumClass->name -- value types have NO descriptor
//           (CodeGenerator descriptor emission gate); the typeof milestone
//           must emit a name-bearing descriptor for enum types (or the
//           icalls must take the name string -- a runtime-side decision).
//        3. Enum box/unbox helpers: GetName/Parse values arrive boxed
//           (`box Color` in the IL) and CodeGenerator's box_ switch
//           LOUD-rejects enum types today (no box helper); the "unbox:l"
//           casts above are the forward ABI for the boxed value slot.
//        4. The Object::ToString dispatch route (see the dispatch note
//           above) for the constrained/ldloca enum-value shape.
//
// REGISTER WIRING STATUS (2026-08-05, box milestone): the register side is
// emission work in CodeGenerator.cs / TypeEmitter.cs / MetadataLoader.cs
// (outside this file's domain -- the dispatch table and the code generator
// are other agents' files). Implemented in THIS domain so far:
//   - Console enum arm (MethodMap.Console.cs box-type switch, timecase work order
//     2): WriteLine(object) with a boxed enum value routes through
//     il2cpp_enum_to_string + write_line_s ("enum:l" cast, CodeGenerator
//     EmitIcallCallArgs). The null class descriptor takes the Int32
//     no-metadata path (numeric decimal) -- the named-value path becomes
//     reachable once items a-c + the name-bearing box class land, exactly
//     the forward ABI this report documents for the ToString mapping.
//   - REGISTER EMISSION (timecase work order 3, this session): the runtime side
//     LANDED FIRST by a parallel agent (enumext.cpp): DayOfWeek built-in
//     table (kDayOfWeekInfo, Sunday=0..Saturday=6) registered at static
//     init + a null-descriptor fallback in il2cpp_enum_to_string that
//     prints the built-in member NAME when the value matches AND no
//     registered non-built-in enum owns it -- timecase L8 now prints
//     "Monday" byte-identical to net48 (verified this session, mingw).
//   - The transpiler-side register helpers LANDED in THIS domain (below):
//     TryGetBclEnumRegisterInfo (System.DayOfWeek table, mirroring the
//     runtime built-in), TryGetUserEnumRegisterInfo (TypeDef STATIC LITERAL
//     fields, declaration order), EmitEnumRegisterBlock (the static-init
//     shape: s_{T}_names / s_{T}_values / s_{T}_enumInfo + the register
//     call) and EnsureEnumRegisterCapacity (32, item d). All TDD-covered in
//     tests/unit/MethodMapTests.Enum.cs (EnumRegisterWiringTests).
//   - The a-e scheme below stands as the handoff work order (per item); the
//     remaining hook is item c's call sites in CodeGenerator.cs
//     (ensure_cctor / il2cpp_runtime_init -- occupied by a parallel agent's
//     refactor at the time of writing; the emission helper is ready to
//     splice):
//       a. MetadataLoader: read each enum TypeDef's STATIC LITERAL fields
//          (td.GetFields(), FieldAttributes.Literal + GetDefaultValue()) --
//          names + values in declaration order + the underlying TypeCode
//          from the enum's base type.
//       b. Generated statics: per enum type, next to the type's other
//          statics (TypeEmitter.EmitStaticStorage) --
//          s_{T}_names[] / s_{T}_values[] / s_{T}_enumInfo (registry stores
//          pointers only; the statics must outlive it).
//       c. Registration call sites (static init -- the register is hooked at
//          static initialization):
//          - Enum types WITH a .cctor: inside ensure_cctor_{T}
//            (CodeGenerator.cs cctor emission), after the done-flag, before
//            the .cctor body call (the attribute-registration hook shape).
//          - Enum types WITHOUT a .cctor (the common case): in
//            il2cpp_runtime_init (CodeGenerator.cs EmitMain), its OWN loop
//            over the assembly's enum types -- the existing type-registration
//            loop SKIPS value types (`if (t.IsValueType) continue;`).
//          Both satisfy enumext.h's startup-only, no-sync contract.
//       d. Capacity: kMaxRegisteredEnums = 32 (enumext.cpp) -- count enum
//          types at compile time and LOUD-reject beyond 32.
//       e. Reachability (the remaining gap chain): ldtoken/typeof emission
//          (e.1), name-bearing enum class descriptors (e.2 -- also what the
//          boxed-enum "enum:l" cast needs for the named path), and the
//          enum box helper (e.3 -- CodeGenerator box_ switch, in flight).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapEnum(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string?[]? genArgNames, string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (name)
        {
            // string GetName(Type, object): the result is the member name
            // (null when the value has no name -- the runtime contract).
            // ABI FIX (enumface, D-STREN-2, 2026-08-12): BOTH arguments are
            // managed references passed through uncast. The first is the
            // FOLDED typeof(T) FULL-NAME STRING (the ldtoken + GetTypeFromHandle
            // fold, StackFold.cs) -- the runtime resolves the enum registry by
            // exactly that name. The second is the BOXED VALUE (the enum box
            // -- icall_box_enum stamps it with the enum's own klass, so the
            // int64-carrier "unbox:l" cast would fail the Int64-box klass
            // check); the runtime reads the width-aware payload itself
            // (BoxedEnumPayload). The old target (il2cpp_enum_get_name) took
            // an Il2CppClass* descriptor and would not compile against the
            // folded string.
            case "GetName":
                return I("il2cpp::runtime::il2cpp_enum_get_name_by_type_name", 2, true, true, false,
                    new[] { "", "" }, returnTypeName: "System.String");
            // Type GetUnderlyingType(Type): the underlying primitive's full
            // name as a managed string ("System.Int32" -- simplified Type).
            // ABI FIX (enumdeep, 2026-08-07): the IL argument is the FOLDED
            // typeof(T) value -- the type's FULL-NAME STRING (the ldtoken +
            // GetTypeFromHandle fold, StackFold.cs), but the runtime icall
            // takes a CLASS DESCRIPTOR (Il2CppClass*, enumext.h:160) whose
            // name field FindInfo dereferences. Passing the string where the
            // descriptor belongs fails to compile; there is no name-string ->
            // descriptor conversion in the emitter cast table, so the string
            // is DROPPED and PrependZeroArg injects the null descriptor --
            // the runtime's documented Int32 no-metadata assumption
            // ("System.Int32"), which is EXACT for every M3-supported enum
            // (value types load through the Int32 slot; the same null-
            // descriptor interim as the Enum.ToString mapping below). The
            // named-value path becomes reachable once typeof/ldtoken emits a
            // name-bearing descriptor (REGISTER WIRING REPORT e.1-e.2).
            case "GetUnderlyingType":
                // raudit4: the runtime resolves the registered enum metadata by
                // the folded typeof NAME string (the descriptor variant stays
                // for internal callers). Returns "System.Int64" for a
                // long-backed enum.
                // VERIFY-FIX (pre-apply verification): prependZero must be FALSE --
                // the runtime by_type_name variant takes exactly ONE parameter
                // (Il2CppObject* typeName, enumext.cpp); the patch's
                // prependZero=true emitted `(0, name)` (2 args) and broke the
                // generated-C++ compile.
                return I("il2cpp::runtime::il2cpp_enum_get_underlying_type_by_type_name", 1, true, true, false,
                    new[] { "" }, null, false, false, false, "System.String");
            // object Parse(Type, string[, bool]): WIRED for the 2-arg
            // overload (enumface, D-STREN-2, 2026-08-12) and the 3-arg
            // ignoreCase overload (batch 148: il2cpp_enum_parse_boxed_ic,
            // ASCII case-insensitive NAME matching). The runtime
            // icall (il2cpp_enum_parse) is TryParse-SHAPED -- it returns
            // 1/0 and writes an int64_t* out slot, and the M-stage
            // transpiler has no ref/out support (the same rejection as
            // Int32.TryParse). Instead the mapping targets the VALUE-
            // RETURNING boxed primitive (il2cpp_enum_parse_boxed): it parses
            // the same grammar and returns the result as a BOXED ENUM (the
            // enum's own klass), throwing ArgumentException on failure --
            // net48 Enum.Parse semantics. All arguments are managed refs
            // (the folded typeof name string + the text string, + the bool
            // ignoreCase as "b"); the result is the boxed object.
            case "Parse":
                if (ps.Length == 2)
                    return I("il2cpp::runtime::il2cpp_enum_parse_boxed", 2, true, true, false,
                        new[] { "", "" });
                if (ps.Length == 3)
                    // (Type, string, bool ignoreCase): batch 148 wiring-gap
                    // closure -- the runtime primitive does ASCII
                    // case-insensitive NAME matching (net48 semantics).
                    return I("il2cpp::runtime::il2cpp_enum_parse_boxed_ic", 3, true, true, false,
                        new[] { "", "", "b" });
                throw new NotSupportedException($"Enum.Parse with {ps.Length} parameter(s) not wired: only the (Type, string) and (Type, string, bool) overloads map to il2cpp_enum_parse_boxed(_ic)");
            case "TryParse":
                // bool TryParse<T>(string, out T) / (string, bool, out T) --
                // batch 148e wiring-gap closure: the runtime icall
                // (il2cpp_enum_parse) is TryParse-SHAPED (1/0 + int64_t* out),
                // which the "addr:l" ref/out wiring supports (Int64.TryParse
                // precedent, MethodMap.ValueType). The enum's klass descriptor
                // is injected via PrependTypeClass from the METHODSPEC's
                // VALUETYPE generic-argument NAME (D-MAR-1 genArgNames
                // mechanism -- the T of TryParse<Color> is a VALUETYPE, so the
                // full type name is captured), giving the runtime the member
                // metadata for the NAME path. The 3-arg ignoreCase overload
                // routes to the ic variant.
                if (genArgNames is { Length: > 0 } && genArgNames[0] != null)
                {
                    return ps.Length switch
                    {
                        // The out slot is the enum's value__ carrier
                        // ("enumaddr" -- the "addr:l" base-reinterpret would
                        // hit the _obj header, batch 148e).
                        2 => new ResolvedMethod("il2cpp::runtime::il2cpp_enum_parse", 2, false, true, false,
                            new[] { false, false }, false, ParamTypeNames: new[] { "", "enumaddr" },
                            PrependTypeClass: genArgNames[0]),
                        3 => new ResolvedMethod("il2cpp::runtime::il2cpp_enum_parse_ic", 3, false, true, false,
                            new[] { false, false, false }, false, ParamTypeNames: new[] { "", "b", "enumaddr" },
                            PrependTypeClass: genArgNames[0]),
                        _ => throw new NotSupportedException($"Enum.TryParse with {ps.Length} parameter(s) not wired (only the (string, out T) and (string, bool, out T) overloads)"),
                    };
                }
                throw new NotSupportedException("Enum.TryParse not wired: the generic argument T is not a VALUETYPE with a resolvable name");
            // instance string ToString(): the System.Enum-owner shape
            // (Enum-typed references). The runtime needs (class, value):
            // the class descriptor is not producible yet (no typeof/ldtoken
            // emission -- forward ABI), so PrependZeroArg injects the null
            // descriptor and the runtime takes its documented Int32
            // no-metadata path (numeric decimal string). The value slot is
            // the boxed enum's int64 carrier (unbox:l -- the receiver is an
            // Enum-typed reference; an ldloca address shape reads the slot).
            case "ToString":
                if (ps.Length == 0)
                    // BOX path (receiver is a boxed enum reference -- the IL
                    // boxed the value): il2cpp_enum_boxed_to_string reads the
                    // klass from the box (batch 148; the old "unbox:l" shape
                    // hit icall_unbox_long, which rejected enum boxes). The
                    // ADDRESS path (ldloca + constrained, StackFold) keeps
                    // the klass+value icall with PrependTypeClass.
                    return receiverIsAddress
                        ? I("il2cpp::runtime::il2cpp_enum_to_string", 0, true, true, true,
                            new[] { "deref:l" }, null, false, true)
                        : I("il2cpp::runtime::il2cpp_enum_boxed_to_string", 0, true, true, true,
                            new[] { "boxed" }, null, false, false);
                if (ps.Length == 1)
                    // ToString(string format) -- batch 148 wiring-gap closure:
                    // the format-specifier primitive (D/G/F/X, case-insensitive)
                    // over the same boxed-enum ABI + the format string. Null/
                    // empty format falls back to "G" (net48).
                    return receiverIsAddress
                        ? I("il2cpp::runtime::il2cpp_enum_to_string_format", 1, true, true, true,
                            new[] { "deref:l", "" }, null, false, true)
                        : I("il2cpp::runtime::il2cpp_enum_boxed_to_string_format", 1, true, true, true,
                            new[] { "boxed", "" }, null, false, false);
                throw new NotSupportedException($"Enum.ToString with {ps.Length} parameter(s) not wired (only the parameterless and single-format overloads map)");
            // bool Equals(object): the boxed-enum equality icall (ICALL-C,
            // 2026-08-07 -- il2cpp_enum_equals, enumext.h). The runtime takes
            // two Il2CppObject* (self, other); the bool result rides the int32
            // slot (rv=true -- the ValueType icall_boxed_equals convention).
            // null self -> NullReferenceException; null other -> 0; a klass
            // NAME mismatch (different enum types / non-enum boxes) -> 0.
            // Both the receiver and the argument pass through uncast: this is
            // the boxed-enum-reference ABI (the same forward ABI as ToString /
            // GetName -- the enum VALUE ldloca+constrained shape stays
            // unreachable until the enum box helper lands, documented above).
            case "Equals":
                return I("il2cpp::runtime::il2cpp_enum_equals", 1, true, false, true, new[] { "", "" });
            // The remaining System.Enum surface has no runtime primitive on
            // the enumext.h surface (no array returns, no flags semantics,
            // no ignore-case parse) -- LOUD, per the wiring convention.
            case "Format":
                // string Format(Type, object, string) -- batch 148 wiring-gap
                // closure: the static form of ToString(format) (D/G/F/X via
                // il2cpp_enum_to_string_format; null/empty format -> "G").
                return I("il2cpp::runtime::il2cpp_enum_format", 3, true, true, false,
                    new[] { "", "", "" });
            case "IsDefined":
                // bool IsDefined(Type, object) -- batch 148 wiring-gap
                // closure: string values match member NAMES (case-sensitive),
                // boxed values match the numeric member values. The bool
                // result rides the int32 slot (rv=true, rr=false).
                return I("il2cpp::runtime::il2cpp_enum_is_defined", 2, true, false, false,
                    new[] { "", "" });
            case "GetNames":
                // string[] GetNames(Type): WIRED (enumface, D-STREN-2). The
                // runtime primitive (il2cpp_enum_get_names_by_type_name)
                // resolves the enum metadata by the folded type-name STRING
                // and returns a string[] of the member names in DECLARATION
                // order (net48) -- the same Il2CppArray* string[] ABI as
                // String.Split, so the result indexes like any string[].
                return I("il2cpp::runtime::il2cpp_enum_get_names_by_type_name", 1, true, true, false,
                    new[] { "" });
            case "GetValues":
                // Array GetValues(Type) -- batch 148 wiring-gap closure: an
                // int32[] of the member values (Int32 no-metadata width). The
                // return type name steers foreach to the array-enumerator
                // path (a plain System.Array result would resolve through
                // the unwired IEnumerator surface).
                return I("il2cpp::runtime::il2cpp_enum_get_values_by_type_name", 1, true, true, false,
                    new[] { "" }, null, false, false, false, "System.Int32[]");
            case "ToObject":
                // object ToObject(Type, object) -- batch 148 wiring-gap
                // closure: the boxed enum with the enum's own klass
                // descriptor (il2cpp_class_by_name + il2cpp_box_enum).
                return I("il2cpp::runtime::il2cpp_enum_to_object", 2, true, true, false,
                    new[] { "", "" });
            case "HasFlag":
                // bool HasFlag(Enum flag): WIRED (enumface, D-STREN-2). The
                // receiver and the argument are BOXED enum references
                // (Il2CppObject*, the boxed-enum forward ABI -- the enum
                // VALUE ldloca+constrained shape stays unreachable until the
                // enum value-shape fold lands, D-ENU-2); the runtime reads
                // both width-aware payloads and returns (self & flag) == flag
                // (net48), with the net48 error contract (null flag ->
                // ArgumentNullException; klass-name mismatch ->
                // ArgumentException).
                return I("il2cpp::runtime::il2cpp_enum_has_flag", 1, true, false, true,
                    new[] { "", "" });
            case "CompareTo":
                // int CompareTo(object) -- batch 148 wiring-gap closure: the
                // width-aware payload comparison (-1/0/1; null other -> 1,
                // other-type -> ArgumentException). The constrained receiver
                // arrives as its int64 carrier VALUE ("enumval"); the
                // argument is a boxed enum. The result rides the int32 slot.
                return I("il2cpp::runtime::il2cpp_enum_compare_to", 1, true, false, true,
                    new[] { "enumval", "" });
            // P6: Enum.InternalGetUnderlyingType (stub).
            case "InternalGetUnderlyingType": return I("il2cpp::runtime::icall_enum_internal_get_underlying_type", 1, true, true, false, new[] { "" });
            default:
                return null;
        }
    }

    //--------------------------------------------------------------------------
    // REGISTER EMISSION (timecase work order 3; the file's a-e work order, items b-d
    // implementable in this domain). The RUNTIME side ships DayOfWeek as a
    // built-in (enumext.cpp kDayOfWeekInfo + the null-descriptor fallback in
    // il2cpp_enum_to_string -- the agent-landed path that makes timecase L8
    // print "Monday" today); this transpiler side produces the name-keyed
    // register call for enum types the runtime cannot know (user enum
    // TypeDefs) and the transpiler-side table for BCL enums (the two sources
    // must agree -- the BCL table below mirrors the runtime built-ins).
    //
    // The emission target shape (static-init, item b+c):
    //     static const char* const s_{T}_names[] = { "Red", "Green" };
    //     static const int64_t      s_{T}_values[] = { 1, 2 };
    //     static const il2cpp::runtime::Il2CppEnumInfo s_{T}_enumInfo =
    //         { 9, 2, s_{T}_names, s_{T}_values, 0 };
    //     il2cpp::runtime::il2cpp_enum_register("Calc.Color", &s_{T}_enumInfo);
    // The trailing column is the REAL [Flags] attribute bit (1 for a
    // [Flags] enum) -- see enumext.h's Il2CppEnumInfo::isFlags.
    // Call sites (item c, CodeGenerator.cs -- hooks occupied by a parallel
    // agent, per the session report): enum types WITH a .cctor go inside
    // ensure_cctor_{T} after the done-flag; types without one go in
    // il2cpp_runtime_init's OWN loop over the assembly's enum types (the
    // existing type-registration loop skips value types). Both satisfy
    // enumext.h's startup-only, no-sync contract.
    //--------------------------------------------------------------------------
    public const int MaxRegisteredEnums = 32; // enumext.cpp kMaxRegisteredEnums

    // net48 TypeCode of the enum underlying primitives (enumext.h
    // kEnumUnderlying* -- consistent with icall_object_get_type_code).
    public const int UnderlyingInt32 = 9;

    private static readonly Dictionary<string, (int Underlying, string[] Names, long[] Values, bool IsFlags)> BclEnumRegisterInfo =
        new()
        {
            // Mirrors enumext.cpp kDayOfWeekNames/kDayOfWeekValues (the
            // runtime built-in table is the authority for the null-descriptor
            // fallback; this table is the authority for the name-keyed
            // register emission -- a mismatch would make the two paths
            // disagree on the same value).
            // IsFlags=false: System.DayOfWeek is a plain enum (net48 prints
            // "Wednesday" for 3 and the numeric "9" for 9 -- never a
            // decomposition; probe dowF3/dowG3).
            ["System.DayOfWeek"] = (UnderlyingInt32,
                new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" },
                new long[] { 0, 1, 2, 3, 4, 5, 6 }, false),
        };

    // BCL enums (TypeRefs -- their members live in mscorlib, invisible to
    // MetadataLoader): the transpiler-side register table.
    public static bool TryGetBclEnumRegisterInfo(string fullName, out EnumRegisterInfo info)
    {
        if (BclEnumRegisterInfo.TryGetValue(fullName, out var entry))
        {
            info = new EnumRegisterInfo(fullName, entry.Underlying, entry.Names, entry.Values, entry.IsFlags);
            return true;
        }
        info = null!;
        return false;
    }

    // Does this BCL enum have a MEMBER-NAME table (the register table above)?
    //
    // bcldenum1: the constrained-enum ToString fold may only route a BCL enum
    // through il2cpp_enum_to_string when the runtime registry can answer with
    // MEMBER NAMES. KnownBclEnumNames is a WIDER set (it also lists enums the
    // transpiler only needs an int32 carrier for -- GCLatencyMode/TypeCode have
    // NO name table here and NO runtime built-in), and routing those to the
    // enum icall would print the NUMERIC value where net48 prints a name --
    // a silent wrong value. Those stay on the pre-existing LOUD path.
    public static bool HasBclEnumNameTable(string fullName)
        => BclEnumRegisterInfo.ContainsKey(fullName);

    // User enum TypeDefs: the member table comes from the type's STATIC
    // LITERAL fields (declaration order -- MetadataLoader already reads them
    // with ConstantValue). The underlying TypeCode is the value__ field's
    // type; M3 loads value types through the Int32 slot only, so user enums
    // default to Int32 (TypeCode 9) today.
    public static bool TryGetUserEnumRegisterInfo(TypeDef t, out EnumRegisterInfo info)
    {
        if (t.BaseTypeName != "System.Enum")
        {
            info = null!;
            return false;
        }
        var literals = t.StaticFields.Where(f => f.IsLiteral).ToList();
        // raudit4: the underlying width comes from the compiler-generated
        // `value__` instance field (System.Int64 for `enum LE : long`); the
        // old hard-coded Int32 truncated 64-bit members at registration.
        var underlying = UnderlyingInt32;
        var valueField = t.InstanceFields.FirstOrDefault(f => f.Name == "value__");
        if (valueField != null)
        {
            underlying = valueField.FieldTypeName switch
            {
                "System.SByte"  => 5,
                "System.Byte"   => 6,
                "System.Int16"  => 7,
                "System.UInt16" => 8,
                "System.Int32"  => UnderlyingInt32,
                "System.UInt32" => 10,
                "System.Int64"  => 11,
                "System.UInt64" => 12,
                _ => UnderlyingInt32,
            };
        }
        info = new EnumRegisterInfo(
            TypeEmitter.ReflectionFullName(t),
            underlying,
            literals.Select(f => f.Name).ToArray(),
            literals.Select(f => long.TryParse(f.ConstantValue, out var v) ? v : 0L).ToArray(),
            // flagsenum: the REAL [Flags] attribute bit decoded by the loader
            // (MetadataLoader.HasFlagsAttribute), never a member-shape guess.
            t.IsFlags);
        return true;
    }

    // The static-init emission block: caller-owned statics + the register
    // call. The registry stores pointers only -- the statics must outlive it
    // (file-scope static storage satisfies that).
    public static string EmitEnumRegisterBlock(string typeName, int underlying,
        string[] names, long[] values, bool isFlags)
    {
        var w = new CppWriter();
        var cpp = TypeEmitter.Sanitize(typeName);
        w.Line($"// Enum metadata for {typeName} (il2cpp_enum_register).");
        w.Line($"static const char* const s_{cpp}_names[] = {{ {string.Join(", ", names.Select(n => $"\"{n}\""))} }};");
        w.Line($"static const int64_t s_{cpp}_values[] = {{ {string.Join(", ", values)} }};");
        w.Line($"static const il2cpp::runtime::Il2CppEnumInfo s_{cpp}_enumInfo = {{ {underlying}, {names.Length}, s_{cpp}_names, s_{cpp}_values, {(isFlags ? 1 : 0)} }};");
        w.Line($"il2cpp::runtime::il2cpp_enum_register(\"{typeName}\", &s_{cpp}_enumInfo);");
        return w.ToString();
    }

    // Item d: the compile-time capacity check (kMaxRegisteredEnums = 32).
    // The compile-time count is the right place to surface it (same pattern
    // as the attribute registry, MethodMap.Attribute.cs).
    public static void EnsureEnumRegisterCapacity(int enumCount)
    {
        if (enumCount > MaxRegisteredEnums)
            throw new NotSupportedException(
                $"enum register capacity exceeded: {enumCount} enum types need il2cpp_enum_register metadata, but the runtime registry holds at most {MaxRegisteredEnums} (enumext.cpp kMaxRegisteredEnums)");
    }
}

// One enum type's register metadata (the transpiler-side view of the
// runtime's Il2CppEnumInfo -- see enumext.h). IsFlags is the REAL
// System.FlagsAttribute bit (TypeDef.IsFlags), the runtime's only authority
// for ToString decomposition -- never inferred from the member shape.
public sealed record EnumRegisterInfo(
    string TypeName, int UnderlyingTypeCode, string[] Names, long[] Values, bool IsFlags);
