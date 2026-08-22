using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// =============================================================================
// MethodMap directory conventions (MethodRefResolver wiring manual -- README section)
// =============================================================================
//
// MethodRefResolver is a partial class split by BCL domain across the
// src/il2cppc/Emit/MethodMap.<Domain>.cs files. Each file has exactly one
// registration entry point TryMap<Domain> with a fully uniform signature:
//
//   private static ResolvedMethod? TryMap<Domain>(
//       string owner, string name, byte[] ps, int count, byte[]? genArgs,
//       string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
//
//   owner             declaring type full name ("System.Math"); multi-owner
//                     domains (IO/ValueType/...) switch on owner first, then name
//   name              member name ("Abs")
//   ps                per-parameter raw type bytes decoded by RawSignature
//                     (0x08=i4, 0x09=u4, 0x0A=i8, 0x0B=u8, 0x0C=f4, 0x0D=f8,
//                     0x0E=string, 0x02=bool, 0x03=char, 0x1C=object, 0x1D=array,
//                     0x12=class, 0x11=valuetype)
//   count             parameter count (= ps.Length; reserved for call sites that
//                     dispatch on count)
//   genArgs           MethodSpec generic instantiation arguments (Array.Sort<T>
//                     etc.; null for ordinary calls)
//   receiverType      fold-tracked managed type of the receiver (callvirt folding)
//   receiverIsAddress the receiver is an ldloca address (value-type BCL method)
//   arg0Type/arg1Type fold-tracked managed types of the first/second stack values
//
// Registration: inside the "BCL domain dispatch" #region of the main file
// BclResolveCore, exactly ONE case line per domain (multi-owner domains may
// stack multiple case labels). A miss (null return) falls through to the
// generated-name path; known-but-unwireable members must throw
// NotSupportedException (LOUD).
//
// Wiring agent procedure (new domain mapping):
//   1. Create src/il2cppc/Emit/MethodMap.<Domain>.cs (copy the template from
//      any existing file): namespace Il2Cpp.Transpiler.Emit; + partial class +
//      TryMap<Domain> (uniform signature). Fill the body with switch(name)
//      (switch(owner) first for multi-owner), building ResolvedMethod with the
//      I(...) helper (defined in the main file).
//   2. In MethodRefResolver.cs append one line before `default:` at the end of
//      the "BCL domain dispatch" #region: case "X.Y": return TryMap<Domain>(
//      owner, name, ps, count, genArgs, receiverType, receiverIsAddress,
//      arg0Type, arg1Type);
//      (multi-owner: stacked case labels; format domains must precede
//      value-semantics domains: use the ?? fallback, see the Int32 group)
//   3. Build + unit-test (dotnet build src/il2cppc && dotnet test tests/unit).
//
// Conflict avoidance: each wiring agent writes only its own
// MethodMap.<Domain>.cs plus one line at the end of the region; never overlaps
// another agent's files. Dispatch uses compile-time switch (no reflection, no
// dictionary, no string lookup).
//
// ── 34-domain parallel wiring skeleton (generated 2026-08-03) ───────────────
// The main-file dispatch region already reserves case lines for the following
// 34 domains (each case calls TryMap<Domain>), backed by 34
// MethodMap.<Domain>.cs skeleton files (bodies contain only `return null;`,
// no real mappings):
//
//   Regex · List · Dictionary · Stopwatch · Interlocked · IntPtr · UIntPtr ·
//   CultureInfo (already has real mappings, no wiring needed) · Queue · Stack ·
//   StreamIO · BinaryIO · Encoding · Guid · Version · Uri · Enum · Attribute ·
//   Type · ArraySort · Buffer · BitConverter · Base64 · MathExtra · TimeExt2 ·
//   StringExt3 · NumberFormat · Debug · StackQueue · DateTimeExtra ·
//   ConvertExtra · RandomExtra · EnvironmentExtra · Monitor
//
// Wiring agent procedure (edit only your own file, zero conflicts):
//   1. Open src/il2cppc/Emit/MethodMap.<Domain>.cs (the skeleton already
//      exists; no need to create it).
//   2. In the TryMap<Domain> body, fill switch(name) following the existing
//      MethodMap.Math.cs style (switch(owner) first for multi-owner domains),
//      building ResolvedMethod with the I(...) helper from the main-file
//      Helpers section; ps byte meanings are the RawSignature table above; the
//      `?? fallback` chains are already wired in the skeletons (e.g.
//      Math→MathExtra, Convert→BitConverter→Base64→ConvertExtra,
//      Array→ArraySort, String→StringExt3, DateTime/TimeSpan→TimeExt2→
//      DateTimeExtra, Queue/Stack→StackQueue, Random→RandomExtra,
//      Environment→EnvironmentExtra).
//   3. Return null for unmatched names (fall through to the generated-name
//      path); throw NotSupportedException for known-but-unwireable members
//      (LOUD, see the main-file BclResolve wrapper).
//   4. dotnet build src/il2cppc && dotnet test tests/unit → once all green the
//      domain is done.
//   5. Do not edit MethodRefResolver.cs (the dispatch case lines already
//      exist), do not git commit, do not touch src/runtime/**.
// =============================================================================
//------------------------------------------------------------------------------

//------------------------------------------------------------------------------
// ResolvedMethod -- the transpiler's answer for one call target.
//
//   CppName    : generated C++ function name (signature-hashed, unique per overload)
//   ParamCount : number of explicit parameters (this excluded for instance methods)
//   ReturnsValue: the method returns a value (vs void)
//   ReturnsRef : the return value is a managed reference (Il2CppObject*)
//   ParamIsRef : per-parameter classification (true = managed reference slot)
//
// The ref classifications drive generated C++ typing: reference slots are
// Il2CppObject* and value slots are int32_t (M2 type set). The emitter uses
// them for the call's argument casting and for temporary-variable typing in
// the body (a temp that holds a call result must be declared as Il2CppObject*
// when the call returns a reference).
//
// M5 BCL wiring (ParamTypeNames): for runtime icall mappings the entries are
// C++ argument-cast codes, applied by the emitter at the call sites:
//   ""            no cast (managed reference slot -- already Il2CppObject*)
//   "i"/"l"/"d"   (int32_t)/(int64_t)/(double)
//   "c"/"b"/"q"   (char16_t)/(bool)/(uint64_t)
//   "deref:i|d"   first arg is a VALUE-TYPE receiver address (ldloca): deref
//                 (*(int32_t*)(..))/(*(double*)(..)) -- value-type BCL methods
//   "unbox:i|d"   first arg is a BOXED value: il2cpp_unbox_int32/double(..)
//   "fcap"        StringBuilder.Capacity: read the runtime struct field
//   "drop"        skip the argument (format strings not yet honored)
//------------------------------------------------------------------------------
public sealed record ResolvedMethod(
    string CppName, int ParamCount, bool IsConsoleWriteLine, bool ReturnsValue,
    bool ReturnsRef, bool[] ParamIsRef, bool IsInstance, bool IsPInvoke = false,
    string[]? ParamTypeNames = null, bool SetLastError = false,
    // M5 BCL wiring: the C++ result kind for value-typed results that are not
    // int32 ("l" int64_t / "d" double; null = int32, refs follow ReturnsRef).
    string? ResultKind = null,
    // M5 BCL wiring: String.Concat(string,string[,..]) -> il2cpp_string_concat_many
    // takes an explicit leading count -- the emitter injects ParamCount.
    bool PrependCountArg = false,
    // M5 BCL wiring: Hashtable() -> icall_hashtable_ctor needs an explicit
    // capacity argument the IL does not push -- the emitter injects 0.
    bool PrependZeroArg = false,
    // M7R3 (hashtable2/hashtablecase cmake build): the injected 0 lands AFTER
    // the arguments instead of before. The Hashtable() 0-arg ctor's runtime
    // signature is icall_hashtable_ctor(Il2CppObject* self, int32_t capacity)
    // -- the receiver (args[0], the newobj temp) must come FIRST, then the
    // injected capacity 0. PrependZeroArg would emit `(0, t0)` (0 in the
    // self slot, the object in the capacity slot -- "cannot initialize a
    // parameter of type int32_t with an lvalue of type Il2CppObject*").
    bool AppendZeroArg = false,
    // M5 BCL wiring: the MANAGED type name of the call result when it is a
    // reference ("System.Text.StringBuilder" for Append/Insert/Remove...).
    // The fold records it in objTypes so later calls on the result
    // (WriteLine(object) of a StringBuilder, ToString chains) resolve.
    string? ReturnTypeName = null,
    // M5 BCL wiring: VALUE-TYPE ctor icalls (DateTime/TimeSpan .ctor) return
    // the initialized value INSTEAD of mutating the receiver in place. The
    // emitter turns the vctor call into `this = fn(args)` (the receiver slot
    // is the assignment target, not an argument).
    bool ResultIsReceiver = false,
    // decimal-domain C44 (decgap): literal C++ arguments appended AFTER the
    // IL argument list (the AppendZeroArg generalization). A runtime icall
    // may declare MORE parameters than the C# overload pushes (decimal.Round
    // 1-arg -> icall_decimal_round(result, d, decimals=0, mode=0)); the
    // resolver injects the missing trailing literals here.
    string[]? AppendLiterals = null,
    // enumdeep (M7R2): an ICALL that takes the enum type's CLASS DESCRIPTOR
    // as its FIRST parameter (il2cpp_enum_to_string(Il2CppClass*, int64_t)).
    // The transpiler cannot build the descriptor expression from the arg
    // list (the enum type is metadata, not an IL argument), so the emitter
    // PREPENDS `&{TypeCppName}_class` (the enum's generated Il2CppClass
    // descriptor -- name-bearing, so FindInfo finds the registered
    // metadata). Only the parameterless enum-value ToString fold uses it.
    string? PrependTypeClass = null,
    // P5 BCL wiring: when non-null, the icall returns a ValueTuple and the
    // array contains the field type codes ("d" double / "f" float / "l" int64
    // / "i" int32). The CodeGenerator allocates a ValueTuple local, passes
    // &tuple.Field addresses as the LAST arguments to the icall, and
    // returns the filled tuple. Used by Math.SinCos / MathF.SinCos /
    // Math.ModF / MathF.ModF.
    string[]? TupleReturnFields = null)
{
    // MemberInfo::get_Name on a GetType() result is an identity (the GetType
    // icall already returns the type NAME string); the fold recognizes this
    // marker and pushes the receiver instead of emitting a call.
    public const string IdentityMarker = "::identity::";
    // object.ReferenceEquals(a, b): the fold replaces the call with a
    // C++ pointer-equality comparison (the generated code's reference
    // representation is Il2CppObject*, so == is reference identity).
    public const string RefEqMarker = "::refeq::";
    // CultureInfo::get_InvariantCulture: the runtime has no culture object;
    // the call folds to null (every consumer in the wired surface ignores
    // the culture -- DateTime.ToString(string, IFormatProvider) drops it).
    public const string NullMarker = "::null::";
    // CultureInfo::get_CurrentCulture: the runtime pins en-US (documented
    // culture-fixing divergence); the call folds to the literal string
    // "en-US" (CppName = "::lit::en-US").
    public const string LiteralMarker = "::lit::";
    // Work order #24: Path.Combine(string,string,string) -- the params-array
    // form. The runtime icall takes exactly (string, string)
    // (il2cpp_io.h has no multi-arg combine), so the 3-arg call folds to
    // nested icall_path_combine(icall_path_combine(a,b), c). The resolver
    // cannot express two calls in one ResolvedMethod -- it emits this marker
    // and StackFold expands it into the nested CallExpr tree at fold time
    // (StackFold.cs NestedCombineMarker handling).
    public const string NestedCombineMarker = "::nested_combine::";
    // D-LO2-1 (linqorder2): Enumerable.ThenBy / ThenByDescending -- the
    // CHAINED-SORTER surface. OrderBy(k1).ThenBy(k2) is a FUSED composite
    // sort (net48 EnumerableSorter chain); the eager array runtime has no
    // post-hoc primary-key recovery, so the resolver emits this marker and
    // StackFold rewrites the receiver (a direct il2cpp_linq_order_by_key_i4
    // call) into a single il2cpp_linq_then_by_key[_desc]_i4(src, k1, k2)
    // call (StackFold.cs ThenByCombineMarker handling).
    public const string ThenByCombineMarker = "::thenby_combine::";
    public const string ThenByDescCombineMarker = "::thenby_desc_combine::";
    // COM stage 14 (GetComInterfaceForObject): the managed Type -> IID
    // resolution happens at TRANSPILE time (the runtime Type object cannot be
    // resolved to an IID at compile time), so the resolver cannot pick the IID
    // from the signature bytes -- it emits this marker and StackFold inspects
    // the FOLDED Type argument (a typeof(IUnknown)/typeof(IDispatch) constant
    // StringConstExpr) and emits the icall with the matching IID constant.
    public const string ComGetInterfaceMarker = "::com_get_interface::";
}

public static partial class MethodRefResolver
{
    // Supports MethodDefinition (0x06, user method calls), MemberReference
    // (0x0A, incl. System.Console icalls) and MethodSpecification (0x2B,
    // generic instance methods -- Array.Sort<T> etc.) tokens.
    public static ResolvedMethod Resolve(MetadataReader md, int token, IReadOnlyList<TypeDef> knownTypes)
        => Resolve(md, token, knownTypes, null, false, null);

    // M5 BCL wiring: receiver/argument-type-aware resolution. receiverType =
    // the tracked type of the call's FIRST stack value (newobj/box/GetType
    // temp) -- used when the MemberRef's own declaring type is not a
    // translatable mapping (callvirt Object::ToString on a StringBuilder
    // temp, WriteLine(object) with a known boxed value, MemberInfo::get_Name
    // on a GetType() result). receiverIsAddress: the receiver came from
    // ldloca (value-type BCL method -- the receiver is an ADDRESS, not a
    // boxed object). arg0Type: same tracking for the first explicit argument
    // of static calls.
    public static ResolvedMethod Resolve(MetadataReader md, int token, IReadOnlyList<TypeDef> knownTypes,
        string? receiverType, bool receiverIsAddress, string? arg0Type)
        => Resolve(md, token, knownTypes, receiverType, receiverIsAddress, arg0Type, null);

    // M5 BCL wiring: arg1Type = the tracked managed type of the SECOND stack
    // value (String.Concat(object, object) dispatches the numeric convenience
    // icall on the SECOND argument's box type).
    public static ResolvedMethod Resolve(MetadataReader md, int token, IReadOnlyList<TypeDef> knownTypes,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        var handle = MetadataTokens.Handle(token);

        if (handle.Kind == HandleKind.MethodDefinition)
            return ResolveMethodDefinition(md, (MethodDefinitionHandle)handle);

        // M5 BCL wiring: a generic-instantiation call (Array.Sort<T>(T[]) etc.)
        // carries a MethodSpecification token: the underlying method + the
        // instantiation arguments. The array domain dispatches on the generic
        // argument type (T=string -> sort_strings, T=int32 -> sort).
        // M6: a MethodSpec on a USER generic method (Id<int>) expands by
        // concrete type to the definition's generated name -- the definition
        // body transpiles with T = int32_t and the shared signature encoder
        // maps both VAR and MVAR to "i", so the call-site hash input equals
        // the definition's.
        if (handle.Kind == HandleKind.MethodSpecification)
        {
            var ms = md.GetMethodSpecification((MethodSpecificationHandle)handle);
            var genSig = md.GetBlobReader(ms.Signature);
            var header = genSig.ReadByte();
            if (header != 0x0A)
                throw new NotSupportedException($"generic instantiation signature byte 0x{header:X2} not GENERICINST(0x0A) in M5");
            var argc = genSig.ReadCompressedInteger();
            var genArgs = new byte[argc];
            for (var i = 0; i < argc; i++) genArgs[i] = ReadParamByte(md, ref genSig);
            // D-MAR-1 (Marshal struct family): the generic-argument byte alone
            // cannot name a VALUETYPE argument (SizeOf<Rectangle>() and
            // SizeOf<Point>() are both [0x11]) -- the Marshal SizeOf/OffsetOf
            // mappings need the arg's FULL TYPE NAME to select the per-struct
            // runtime function. Re-read the MethodSpec signature to capture
            // the name of every VALUETYPE argument (null for the others).
            var genArgNames = GenericArgValueTypeNames(md, ms.Signature, genArgs);

            // M6: same-assembly instantiations (Id<int>) -- Roslyn emits the
            // MethodSpec on the MethodDefinition (genlist evidenced; the probe's
            // MemberRef form is the cross-assembly shape).
            if (ms.Method.Kind == HandleKind.MethodDefinition)
            {
                var dm = md.GetMethodDefinition((MethodDefinitionHandle)ms.Method);
                // M7 genlist: a single STRING (0x0E) generic argument routes to
                // the emitted string-typed variant of the definition
                // ({defCppName}_string -- CodeGenerator emits a pass-through
                // identity body for generic methods under that name, the
                // genlist `T Id<T>(T x) => x` shape). Any other non-int32
                // argument stays LOUD (no generated variant).
                if (genArgs.Length == 1 && genArgs[0] == 0x0E)
                {
                    var def = ResolveMethodDefinition(md, (MethodDefinitionHandle)ms.Method);
                    return def with
                    {
                        CppName = def.CppName + "_string",
                        ReturnsRef = true,
                        ParamIsRef = def.ParamIsRef.Select(_ => true).ToArray(),
                        ReturnTypeName = "System.String",
                    };
                }
                EnsureInt32DefaultInstantiation(md.GetFullName(dm.GetDeclaringType()), md.GetString(dm.Name), genArgs);
                return ResolveMethodDefinition(md, (MethodDefinitionHandle)ms.Method);
            }

            var mr = md.GetMemberReference((MemberReferenceHandle)ms.Method);
            var owner = ResolveOwnerFullName(md, mr.Parent);
            var name = md.GetString(mr.Name);
            // D-LINQC-1: thread the receiver/arg-type hints into the MethodSpec
            // BCL resolution. Before this, the generic operators (Enumerable.
            // Count<int32> etc.) resolved with receiverType/arg0Type == null, so
            // TryMapLinq could not see a ToList() result's tracked List`1 type
            // and always emitted the array cast -- a List receiver was mis-cast
            // to Il2CppArray* (a wrong count at runtime).
            var bcl = BclResolve(md, owner, name, mr.Signature, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type,
                genArgNames: genArgNames);
            if (bcl != null) return bcl;
            // M6: a MethodSpec on a USER-type member (MemberRef form) expands
            // only when the definition lives in this assembly (the generated
            // name must match the emitted function). Its static/instance
            // shape comes from the definition -- the name heuristic would
            // misclassify the static Id as instance (StackFold pops this).
            if (mr.Parent.Kind == HandleKind.TypeDefinition
                && FindMethodIsStatic(md, (TypeDefinitionHandle)mr.Parent, name) is bool defIsStatic)
            {
                EnsureInt32DefaultInstantiation(owner, name, genArgs);
                return ResolveMemberReference(md, (MemberReferenceHandle)ms.Method, knownTypes,
                    receiverType, receiverIsAddress, arg0Type, arg1Type, defIsStatic);
            }
            throw new NotSupportedException($"generic instantiation of {owner}.{name} has no BCL mapping in M5");
        }

        return ResolveMemberReference(md, (MemberReferenceHandle)handle, knownTypes,
            receiverType, receiverIsAddress, arg0Type, arg1Type);
    }

    //--------------------------------------------------------------------------
    // MethodDefinition -> the definition's generated name (M1/M4). Generic
    // method definitions (Id<T>) decode their MVAR signature to the "i"
    // encoding -- the SAME hash input a MethodSpec call site produces
    // (MethodSignatureCoder maps 0x13/0x1E both to "i"), so a concrete
    // instantiation binds to the emitted function.
    //--------------------------------------------------------------------------
    //--------------------------------------------------------------------------
    // Generated C++ function name for user methods (shared with the M6 tail-call
    // optimization): a single encoding
    //   {Sanitize(owner)}_{Sanitize(name)}_{SigHash(owner.name(paramTypes))}
    // Three sites must agree -- the method-body emission (CodeGenerator.MethodCppName),
    // the call-site resolution (ResolveMethodDefinition/MemberReference user path),
    // and the tail-call self-recursion detection (StackFold.ApplyTailCallLoop
    // compares ctx.MethodCppName against Resolved.CppName).
    //--------------------------------------------------------------------------
    public static string UserMethodCppName(string ownerFullName, string name, string paramTypes)
        => $"{Sanitize(ownerFullName)}_{Sanitize(name)}_{SigHash.Hash(ownerFullName + "." + name + "(" + paramTypes + ")")}";

    private static ResolvedMethod ResolveMethodDefinition(MetadataReader md, MethodDefinitionHandle handle)
    {
        var m = md.GetMethodDefinition(handle);
        var name = md.GetString(m.Name);
        var ownerFullName = md.GetFullName(m.GetDeclaringType());
        var (paramCount, returnType, paramTypes, _) = MethodSignatureCoder.Decode(md, m.Signature);
        var isInstance = (m.Attributes & System.Reflection.MethodAttributes.Static) == 0;
        // M4 Task 1: P/Invoke -- the C++ name is the native entry symbol
        // (extern "C" declaration emitted by the transpiler).
        var import = m.GetImport();
        if (!import.Module.IsNil)
        {
            var entry = md.GetString(import.Name);
            var setLastError = ((int)import.Attributes & 0x40) != 0; // MethodImportAttributes.SetLastError
            // wf2 (winforms1): CharSet decoding -- MethodImportAttributes
            // CharSet mask = 0x06: 0x02 Ansi, 0x04 Unicode, 0x06 Auto. On
            // Windows Auto resolves to the W (Unicode) entry. A CharSet.Unicode
            // P/Invoke must marshal string args as UTF-16 (char16_t*) -- the
            // Win32 W-API reads UTF-16; passing the UTF-8 marshalled buffer
            // misreads it (class-name corruption). The "u16:System.String"
            // encoding (vs the default "r:System.String") drives StackFold's
            // utf16 alloc/free and PinvokeParamList's const char16_t*.
            var charSet = (int)import.Attributes & 0x06;
            var utf16Strings = charSet is 0x04 or 0x06; // CharSetUnicode / CharSetAuto
            // ParamTypeNames = the RAW MethodSignatureCoder encodings (NOT
            // emitter cast codes): they drive the fold's P/Invoke string-arg
            // detection ("r:System.String" -> StringArgIndices -> cstr temps,
            // StackFold) and the emitter's "r:System.String" passthrough case
            // (EmitIcallCallArgs -- the pre-marshalled char* temp matches the
            // native const char* parameter). Int32 etc. encode to "i"/"l"/"d"
            // which the emitter's cast table maps to identity casts.
            // WF-M2 (2026-08-07): a VALUETYPE struct param encodes to
            // "V:<FullName>" (BYREF -> "B:V:<FullName>"). The registered
            // structs (il2cpp_pinvoke_struct.h) are translated to their
            // STABLE cast codes ("rect"/"point"/"msg" -- wfm2 report P4) so
            // EmitIcallCallArgs can dispatch on a small switch; the emitter
            // cases themselves are CG-FIX work (P3). Anything else keeps its
            // raw encoding (the emitter LOUD-rejects unknown cast codes, never
            // silently emitting).
            return new ResolvedMethod(entry, paramCount, false,
                returnType != SignatureTypeCode.Void, IsRefType(returnType), ParamRefFlags(paramTypes), false,
                IsPInvoke: true, ParamTypeNames: paramTypes.Split(',').Select(p =>
                    utf16Strings && p == "r:System.String" ? "u16:System.String" : PInvokeCastCode(p)).ToArray(),
                SetLastError: setLastError,
                // M4.5: IntPtr/UIntPtr-returning P/Invoke -> "ptr" result kind
                // (the emitter declares the result temp intptr_t instead of
                // int32_t -- an int32_t temp truncates the x64 pointer).
                ResultKind: returnType is SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr ? "ptr" : null);
        }
        return new ResolvedMethod(
            UserMethodCppName(ownerFullName, name, paramTypes),
            paramCount, false, returnType != SignatureTypeCode.Void,
            IsRefType(returnType), ParamRefFlags(paramTypes), isInstance,
            // batch 177: a USER method returning IntPtr/UIntPtr must carry
            // "ptr" like the P/Invoke path (the caller temp is then declared
            // intptr_t -- a null ResultKind falls to the default int32_t and
            // truncates the 64-bit handle on x64).
            ResultKind: returnType is SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr ? "ptr" : null);
    }

    //--------------------------------------------------------------------------
    // MemberReference -> the generated-name path (M1/M3/M5): BCL table first,
    // then the ctor special cases, then the sanitized owner/name + signature
    // hash. isStaticOverride (M6) pins the static/instance shape from the
    // method DEFINITION (the MemberRef-form MethodSpec expansion). The default
    // shape comes from the signature header's HAS_THIS bit (0x20) -- the CLI
    // authority -- NOT the name (the old get_*/else name heuristic classified
    // instance getters like Delegate::get_Method as static and static
    // operators like op_Equality as instance, work order #44).
    //--------------------------------------------------------------------------
    private static ResolvedMethod ResolveMemberReference(MetadataReader md, MemberReferenceHandle handle,
        IReadOnlyList<TypeDef> knownTypes, string? receiverType, bool receiverIsAddress, string? arg0Type,
        string? arg1Type, bool? isStaticOverride = null)
    {
        var mr2 = md.GetMemberReference(handle);
        var mrName = md.GetString(mr2.Name);
        var owner2 = ResolveOwnerFullName(md, mr2.Parent);
        var (pc, retType, pt, _) = MethodSignatureCoder.Decode(md, mr2.Signature);

        // D-ICMP-1 (icomparedeep): a callvirt whose declaring owner is the
        // mscorlib IComparable / IComparer INTERFACE. The interface has no
        // translated TypeDef (VirtualTarget / il2cpp_interface_dispatch cannot
        // resolve it -- GAPS.md D-ICMP-1), so with a KNOWN receiver concrete
        // type the call lowers to the runtime interface-dispatch entry
        // (il2cpp_icomparable_compare_to / il2cpp_icomparer_compare --
        // arraysort.h). The runtime resolves the receiver's method vtable slot
        // through the registered typeinfo, so the mapping is receiver-agnostic
        // and a first-pass arg0Type hint is as valid as the re-resolved
        // receiverType hint. The receiver hint must name a translated type that
        // actually declares the method -- a stale hint returns null and keeps
        // the generated-name path (loud undefined symbol downstream).
        if (owner2 is "System.IComparable" or "System.Collections.IComparer")
        {
            var ifaceMapped = TryMapComparerInterface(owner2, mrName, pc, knownTypes,
                receiverType ?? arg0Type);
            if (ifaceMapped != null) return ifaceMapped;
        }

        // M3 generics: the C++ name PREFIX is the instantiation name
        // (Box_1_int), the HASH input is the definition name (Box`1) -- the
        // generated functions live under the instantiated type.
        string cppPrefix = owner2;
        string hashInput = owner2;
        if (mr2.Parent.Kind == HandleKind.TypeSpecification)
        {
            cppPrefix = GenericInstantiationName(md, (TypeSpecificationHandle)mr2.Parent);
            hashInput = owner2; // definition name (from ResolveOwnerFullName)
        }

        // M5 BCL wiring: the big mapping table (Console/Math/Convert/Int32/
        // String/StringBuilder/Hashtable/Random/Environment/IO/GC/Array/...)
        // -- dispatched per-domain by the MethodMap.* partial files (see the
        // MethodMap convention block at the top of this file).
        // D-XMLW-1 (xmlwrite): arg1Type IS forwarded to the maps -- the
        // resolver's 7-arg contract documents it as "the tracked managed type
        // of the SECOND stack value", but the dispatch dropped it here, so a
        // two-argument static call's FIRST argument type never reached the
        // domain maps (XmlWriter.Create(StringBuilder, XmlWriterSettings) could
        // not tell the StringBuilder target from the settings -- the settings
        // is pushed LAST, so arg0Type sees it). The maps that consume it
        // (TryMapString Concat bothNumbers, TryMapXmlWriter Create) now receive
        // the real hint; nothing else reads it.
        // batch 160 (Task<long>): cppPrefix for a TypeSpec parent IS the
        // generic INSTANTIATION name ("System.Runtime.CompilerServices.
        // TaskAwaiter`1_int64") -- the Task domain dispatches the int64
        // result carrier on it (the definition-name owner alone cannot
        // distinguish Task<int> from Task<long> member calls).
        var bcl2 = BclResolve(md, owner2, mrName, mr2.Signature, null, receiverType, receiverIsAddress, arg0Type, arg1Type,
            instName: mr2.Parent.Kind == HandleKind.TypeSpecification ? cppPrefix : null);
        if (bcl2 != null) return bcl2;

        // M5 BCL wiring: System.Object::.ctor and BCL exception ctors stay
        // no-ops in the carrier model (M2/M3 + audit H8) -- must run BEFORE
        // the BCL table (the table does not map .ctor). The ctor's explicit
        // arguments (exception messages) are POPPED by the fold (paramCount)
        // but DROPPED at emission -- il2cpp_object_ctor_nop takes only this.
        if (owner2 == "System.Object" && mrName == ".ctor")
        {
            return new ResolvedMethod("il2cpp::runtime::il2cpp_object_ctor_nop", 0, false, false, false, Array.Empty<bool>(), true);
        }
        // D-ATTR-1 (2026-08-11): System.Attribute::.ctor -- a custom attribute
        // class (`class X : Attribute`) has an implicit `.ctor` whose first
        // instruction is `ldarg.0; call System.Attribute::.ctor`. Without a
        // mapping the call resolves to the mangled generated-name path
        // (System_Attribute__ctor_<hash> -- an undefined symbol, the BCL class
        // has no generated body). System.Attribute has no fields or behavior
        // beyond the object header, so the parameterless base ctor is the same
        // carrier no-op as System.Object::.ctor above (the runtime
        // il2cpp_object_ctor_nop takes only this_; the explicit ctor args, if
        // any, are popped by the fold and dropped at emission). The same shape
        // as the BCL exception ctor case below, but the "*Exception" suffix
        // rule does not match "System.Attribute".
        if (owner2 == "System.Attribute" && mrName == ".ctor")
        {
            return new ResolvedMethod("il2cpp::runtime::il2cpp_object_ctor_nop", 0, false, false, false, Array.Empty<bool>(), true);
        }
        if (mrName == ".ctor" && owner2.EndsWith("Exception", StringComparison.Ordinal)
            && !knownTypes.Any(t => t.FullName == owner2))
        {
            var drops = pt.Split(',').Select(_ => "drop").ToArray();
            return new ResolvedMethod("il2cpp::runtime::il2cpp_object_ctor_nop", pc, false, false, false, ParamRefFlags(pt), true,
                ParamTypeNames: drops);
        }

        // M1: resolve only known user-type methods (method names are sanitized:
        // .ctor/.cctor etc. are not valid C++ identifiers).
        // Hash input includes the parameter-type signature ({owner}.{name}({paramTypes}))
        // -- overloads are distinguished here.
        // Static/instance classification: signature header HAS_THIS bit (0x20) -- the
        // CLI authority (work order #44: Delegate::get_Method header 0x20 = instance
        // getter; op_Equality header 0x00 = static; the old name heuristic got both wrong).
        var isInstanceMr = isStaticOverride is bool s
            ? !s
            : md.GetBlobReader(mr2.Signature).ReadSignatureHeader().IsInstance;
        return new ResolvedMethod(
            $"{Sanitize(cppPrefix)}_{Sanitize(mrName)}_{SigHash.Hash(hashInput + "." + mrName + "(" + pt + ")")}",
            pc, false, retType != SignatureTypeCode.Void,
            IsRefType(retType), ParamRefFlags(pt), isInstanceMr,
            // batch 177: same user-method IntPtr/UIntPtr result-kind fix as
            // ResolveMethodDefinition -- the caller temp must be intptr_t.
            ResultKind: retType is SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr ? "ptr" : null);
    }

    //--------------------------------------------------------------------------
    // M6: user generic methods expand by concrete type to the model's ONLY
    // generated instantiation -- T -> int32_t (the definition body transpiles
    // with T as int32_t; 0x08 i4 / 0x09 u4 share the int32 slot). Any other
    // generic argument has no generated function -- LOUD, self-identifying:
    // the message DECODES the offending argument type (0x0E -> "string"), pins
    // its position (argument #n -- a multi-arg instantiation Id<int,string>
    // must not report the valid int32 as the failure), retains the raw byte
    // for greppability, and names the supported instantiation set -- so a
    // future concrete-type expansion has a precise hook.
    //--------------------------------------------------------------------------
    private static void EnsureInt32DefaultInstantiation(string owner, string name, byte[] genArgs)
    {
        var offending = genArgs
            .Select((g, i) => (Byte: g, Index: i))
            .Where(a => a.Byte is not (0x08 or 0x09))
            .ToArray();
        if (offending.Length == 0) return;
        var detail = string.Join(", ", offending.Select(a =>
            $"argument #{a.Index + 1} '{GenericArgTypeName(a.Byte)}' (0x{a.Byte:X2})"));
        throw new NotSupportedException(
            $"generic instantiation of {owner}.{name} with unsupported generic {detail} has no expansion in M6 -- " +
            "the model generates only the T->int32 (0x08 i4) / T->uint32 (0x09 u4) default instantiations " +
            "(the definition body transpiles once with T = int32_t; the u4 case shares the int32 slot); " +
            "wire a concrete-type expansion in the emitter before use");
    }

    // M6: generic instantiation argument type name for the loud (only the
    // dispatch byte reaches genArgs -- ReadParamByte consumes the trailing
    // CLASS/VAR/SZARRAY payload). Unknown bytes fall back to raw hex.
    private static string GenericArgTypeName(byte b) => b switch
    {
        0x08 => "int32",
        0x09 => "uint32",
        0x0E => "string",
        0x0A => "int64",
        0x0D => "double",
        0x02 => "bool",
        0x03 => "char",
        0x1C => "object",
        0x12 => "class",
        0x13 => "var(T)",
        0x1E => "mvar(T)",
        0x1D => "SZArray",
        _ => $"0x{b:X2}",
    };

    // M6: the definition's static/instance shape (the MemberRef-form MethodSpec
    // expansion needs it; the definition necessarily lives in this assembly
    // for the generated name to exist). null = no matching definition.
    private static bool? FindMethodIsStatic(MetadataReader md, TypeDefinitionHandle owner, string name)
    {
        foreach (var mh in md.GetTypeDefinition(owner).GetMethods())
        {
            var m = md.GetMethodDefinition(mh);
            if (md.GetString(m.Name) == name)
                return (m.Attributes & System.Reflection.MethodAttributes.Static) != 0;
        }
        return null;
    }

    //--------------------------------------------------------------------------
    // M5 BCL mapping table. Returns null when the owner/name has no mapping
    // (the caller falls through to the generated-name path -- or throws for
    // known-but-unwireable members, which MUST be loud).
    //--------------------------------------------------------------------------
    private static ResolvedMethod? BclResolve(MetadataReader md, string owner, string name, BlobHandle sigHandle,
        byte[]? genArgs, string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type = null,
        string[]? genArgNames = null, string? instName = null)
    {
        try
        {
            return BclResolveCore(md, owner, name, sigHandle, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type,
                genArgNames, instName);
        }
        catch (NotSupportedException ex)
        {
            // M5 wiring: every wiring gap must be LOUD and self-identifying.
            throw new NotSupportedException($"{owner}::{name}: {ex.Message}");
        }
    }

    //--------------------------------------------------------------------------
    // BCL domain dispatch. ONE case line per MethodMap.<Domain>.cs file.
    // WIRING AGENTS: add a mapping by (1) creating MethodMap.<Domain>.cs with
    // TryMap<Domain> (uniform signature, template in any MethodMap file) and
    // (2) appending ONE case line below, before `default:`. Multi-owner
    // domains share one case (stacked labels); Format must be tried BEFORE
    // ValueType for the numeric owners (the `??` fallback below).
    //--------------------------------------------------------------------------
    private static ResolvedMethod? BclResolveCore(MetadataReader md, string owner, string name, BlobHandle sigHandle,
        byte[]? genArgs, string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type,
        string[]? genArgNames = null, string? instName = null)
    {
        var (count, ret, ps) = RawSignature(md, sigHandle);
        // #region BCL domain dispatch
        switch (owner)
        {
            case "System.Console": return TryMapConsole(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Math": return TryMapMath(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapMathExtra(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Convert":
            case "System.BitConverter": return TryMapConvert(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapBitConverter(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapBase64(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapConvertExtra(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Int32":
            case "System.Int64":
            case "System.Single":
                // Format first (ToString(string format)), ValueType fallback
                // (ToString()/Parse/Equals/GetHashCode) -- split so format
                // wiring agents never collide with value-semantics agents.
                return TryMapFormat(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                    ?? TryMapValueType(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Double":
                // D-ME2-2 (2026-08-12): System.Double STATIC predicates
                // (IsNaN/IsPositiveInfinity/IsNegativeInfinity/IsInfinity) --
                // TryMapDouble (MethodMap.Math.cs) tried AFTER the Format /
                // ValueType chains (which own ToString/Parse/TryParse/etc.) so
                // the value-semantics surface stays put and the predicate
                // names that fell to the generated-name path (C++ undeclared
                // identifier) resolve to the IEEE bit-check icalls.
                return TryMapFormat(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                    ?? TryMapValueType(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                    ?? TryMapDouble(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Boolean":
            case "System.Byte":
            case "System.SByte":
            case "System.Int16":
            case "System.UInt16":
            case "System.UInt32":
            case "System.UInt64": return TryMapValueType(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Char":
                // D-CHA-2: the char STATIC surface (IsDigit/IsLetter/ToUpper
                // ...) is TryMapChar (MethodMap.Char.cs); the instance surface
                // (ToString()/Equals(object)) falls through to TryMapValueType.
                return TryMapChar(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                    ?? TryMapValueType(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.ValueType":
                // Value-type GetHashCode wiring (audit §5 #5): the constrained
                // value-type receiver's first-instance-field kind comes from
                // the user-struct TypeDef metadata and the per-kind runtime
                // target is selected here (the emitter cannot inject the
                // typeHash/fieldKind constants icall_value_type_get_hash_code
                // needs -- see MethodMap.ValueType.cs for the mapping table).
                return TryMapValueTypeGetHashCode(md, owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.String": return TryMapString(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapStringExt3(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Object":
            case "System.IConvertible":
            case "System.Reflection.MemberInfo": return TryMapObject(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Text.StringBuilder": return TryMapStringBuilder(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Collections.Hashtable": return TryMapHashtable(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // D-ICMP-2 (icomparedeep): System.Collections.Comparer (the
            // non-generic DEFAULT comparer -- Comparer.Default / DefaultInvariant
            // getters + Compare(object, object)) maps to the runtime comparer
            // domain (il2cpp_comparer_default / il2cpp_comparer_compare,
            // arraysort.h). The getter result is tracked under the pseudo-type
            // "System.Collections.Comparer" so the later Compare callvirt
            // resolves on the receiver hint.
            case "System.Collections.Comparer": return TryMapComparer(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Random": return TryMapRandom(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapRandomExtra(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Environment": return TryMapEnvironment(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapEnvironmentExtra(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.IO.File":
            case "System.IO.Directory":
            case "System.IO.Path":
            case "System.IO.FileInfo": return TryMapIO(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.GC":
            case "System.Runtime.GCSettings":
            case "System.Runtime.GCFrameRegistration":
            case "System.ComAwareWeakReference":
            case "System.Runtime.JitInfo":
            case "System.Runtime.Loader.AssemblyLoadContext":
            case "System.Diagnostics.StackTrace":
            case "System.Number":
            case "System.ParseNumbers":
            case "System.CurrentSystemTimeZone":
            case "System.Globalization.CultureData":
            case "System.Globalization.CalendarData":
            case "System.Globalization.EncodingTable":
            case "System.Security.CodeAccessSecurityEngine":
            case "System.Threading.CompressedStack":
            case "System.Threading.DomainCompressedStack":
            case "System.Threading.OverlappedData":
            case "System.Security.Cryptography.Utils":
            case "System.Security.Cryptography.X509Certificates.X509Utils":
            case "System.DefaultBinder":
            case "System.AppDomainSetup":
            case "System.MarshalByRefObject":
            case "System.MissingMemberException":
            case "System.WeakReference`1":
            case "System.CLRConfig":
            case "System.Diagnostics.Assert":
            case "System.Diagnostics.Log":
            case "System.BaseConfigHandler":
            case "System.Security.SecurityContext":
            case "System.Security.SecurityManager":
            case "System.Security.SecurityRuntime":
            case "System.Security.Principal.Win32": return TryMapGC(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Delegate": return TryMapDelegate(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Linq.Expressions.Expression":
            case "System.Linq.Expressions.LambdaExpression": return TryMapExprTree(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Array": return TryMapArray(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapArraySort(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.DateTime": return TryMapDateTime(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapTimeExt2(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapDateTimeExtra(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.DateTimeOffset": return TryMapDtoffset(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.TimeSpan": return TryMapTimeSpan(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapTimeExt2(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapDateTimeExtra(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Threading.Thread":
            case "System.Threading.SynchronizationContext":
            case "System.Threading.HostExecutionContextManager": return TryMapThread(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Threading.Semaphore":
            case "System.Threading.Mutex":
            case "System.Threading.EventWaitHandle": return TryMapWaitHandle(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Threading.WaitHandle":
                // The MemberRef declares the inherited members (WaitOne) on
                // the WaitHandle BASE. WaitOne is the SAME icall for all three
                // concrete handles (Semaphore/Mutex/EventWaitHandle), so route
                // directly -- no receiver disambiguation needed (a static-field
                // receiver like `g_sem.WaitOne()` may not carry the concrete
                // receiverType hint). The concrete-owner cases above handle the
                // ctor/Release/Set/Reset members.
                return TryMapWaitHandle(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Globalization.CultureInfo": return TryMapCultureInfo(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapResourceManager(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Globalization.GregorianCalendar": return TryMapCalendar(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Globalization.Calendar":
                // The MemberRef declares the inherited members (GetYear/
                // GetMonth/GetDayOfMonth/GetDayOfYear/GetDayOfWeek/IsLeapYear/
                // GetDaysInMonth/GetDaysInYear) on the Calendar BASE; the
                // tracked receiverType disambiguates the concrete calendar.
                // The fold resolves callvirt TWICE (first receiverType==null,
                // then with the receiver hint): on the FIRST pass we cannot
                // tell the concrete calendar for the base-typed owner, so
                // DEFER (null -> generated-name, triggering the receiver-aware
                // re-resolve) instead of handing a member to the wrong domain.
                if (receiverType == "System.Globalization.GregorianCalendar")
                    return TryMapCalendar(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
                return null;
            case "System.WeakReference": return TryMapWeakReference(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Exception": return TryMapException(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // === 34-domain parallel-wiring skeleton (2026-08-03) ===
            // One case line per MethodMap.<Domain>.cs skeleton file. Agents fill
            // only their own file's TryMap body -- DO NOT edit this region.
            case "System.Text.RegularExpressions.Regex": return TryMapRegex(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Threading.Tasks.Task`1": return TryMapTask(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, genArgNames, ret, instName);
            case "System.Threading.Tasks.Task": return TryMapTask(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, genArgNames, ret, instName);
            // batch 156c (asynccase): the async state-machine surface --
            // AsyncTaskMethodBuilder`1<T> (Create/Start/SetResult/SetException/
            // SetStateMachine/get_Task/AwaitUnsafeOnCompleted) and TaskAwaiter`1<T>
            // (get_IsCompleted/GetResult/OnCompleted/UnsafeOnCompleted).
            case "System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1": return TryMapTask(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, genArgNames, ret, instName);
            case "System.Runtime.CompilerServices.TaskAwaiter`1": return TryMapTask(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, genArgNames, ret, instName);
            case "System.Collections.Generic.List`1": return TryMapList(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Collections.Generic.Dictionary`2": return TryMapDictionary(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // hset-b23 (D-HSET-1): HashSet<string> -- the string AOT expansion
            // (MethodMap.HashSet.cs, runtime hashset.h -- a set-of-unique-
            // members wrapper over the dictsi domain). GAPS.md recorded the
            // owner-case gap: the newobj fallback emitted
            // &System_Collections_Generic_HashSet_1_string_class + generated-
            // name member calls (undefined symbols). Now the members map to
            // icall_hashset_string_*.
            case "System.Collections.Generic.HashSet`1": return TryMapHashSet(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Diagnostics.Stopwatch": return TryMapStopwatch(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Threading.Interlocked": return TryMapInterlocked(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.IntPtr": return TryMapIntPtr(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.UIntPtr": return TryMapUIntPtr(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Collections.Queue": return TryMapQueue(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapStackQueue(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Collections.Stack": return TryMapStack(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapStackQueue(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.IO.Stream":
            case "System.IO.FileStream":
            case "System.IO.MemoryStream":
            case "System.IO.StreamReader":
            case "System.IO.StreamWriter":
                // P18 deflate: a DeflateStream receiver routes the inherited
                // Write/Read/Close to the deflate icalls (the MemberRef
                // declares them on the Stream base -- the receiver-aware
                // re-resolve carries the DeflateStream hint).
                if (receiverType == "System.IO.Compression.DeflateStream")
                    return TryMapDeflate(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
                return TryMapStreamIO(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                    ?? TryMapProcess(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.IO.Compression.DeflateStream": return TryMapDeflate(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.IDisposable":
                // streamops: a `using (StreamWriter usw = ...)` block compiles
                // to a callvirt on the IDisposable INTERFACE (Dispose()). The
                // receiver-aware re-resolve (fold resolves callvirt twice --
                // null receiverType first, then the tracked hint) must route a
                // stream-wrapper receiver to the StreamIO close icall; the
                // first pass (receiverType==null) defers so the hint-driven
                // second pass dispatches.
                if (name == "Dispose" && count == 0)
                {
                    // The stream check accepts BOTH the receiver hint (second
                    // pass receiverType) and the first-stack-value hint (first
                    // pass arg0Type -- for a 0-arg callvirt the stack top IS
                    // the receiver). Either way the stream wrapper routes to
                    // icall_stream_close.
                    var streamLike = receiverType ?? arg0Type;
                    if (streamLike is "System.IO.FileStream" or "System.IO.MemoryStream"
                            or "System.IO.StreamReader" or "System.IO.StreamWriter")
                        return TryMapStreamIO("System.IO.Stream", name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
                    // batch 158 (timercase): a `using (Timer t = ...)` block
                    // compiles to a callvirt on the IDisposable INTERFACE
                    // (Dispose()). Route a Timer receiver to the timer dispose
                    // icall (stop + join). Same receiver-hint mechanism as the
                    // stream arm above.
                    if (streamLike is "System.Threading.Timer")
                        return TryMapTimer("System.Threading.Timer", name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
                    // batch 161 (timerscase): the same using-block shape for
                    // System.Timers.Timer (Dispose stops + joins + nulls the
                    // handle; idempotent).
                    if (streamLike is "System.Timers.Timer")
                        return TryMapTimers("System.Timers.Timer", name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
                    // D-SL-3 (sldeep): the foreach over a non-generic
                    // collection lowers to a try/finally that emits
                    // `isinst System.IDisposable; if (d != null) d.Dispose()`
                    // over the enumerator. The snapshot enumerator is NOT
                    // IDisposable (sortedlist.cpp), so the isinst guard never
                    // runs Dispose at runtime -- but the generated C++ still
                    // references the symbol, so it must map to a (null-safe)
                    // no-op rather than the undefined generated name. The
                    // receiver (the isinst result) is untracked, so this arm
                    // fires exactly when no stream hint exists.
                    if (receiverType == null && arg0Type == null)
                        return I("il2cpp::runtime::il2cpp_sortedlist_enumerator_dispose", 0, false, false, true);
                }
                return null;
            case "System.Collections.ICollection":
            case "System.Collections.IEnumerable":
            case "System.Collections.IEnumerator":
                // D-SL-3 (sldeep): the ICollection/IEnumerable/IEnumerator
                // interface surface over the SortedList Keys/Values snapshots
                // (MethodMap.Collections.cs -- dispatches on the receiver's
                // tracked pseudo-type: get_Keys/get_Values ReturnTypeName and
                // the get_enumerator result).
                return TryMapCollectionInterface(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.IO.StringReader":
            case "System.IO.StringWriter":
            case "System.IO.TextReader":
            case "System.IO.TextWriter":
                // TextReader/TextWriter are the SHARED base of the StringIO
                // (StringReader/StringWriter) AND StreamIO (StreamReader/
                // StreamWriter) domains. The MemberRef declares the member on
                // the base type; the tracked receiverType disambiguates: a
                // stream-wrapper receiver routes to the StreamIO object icalls
                // (icall_stream_*), a string-wrapper receiver stays in StringIO.
                // The fold resolves callvirt TWICE (first receiverType==null,
                // then with the receiver hint): on the FIRST pass we cannot
                // tell the wrapper kind for the BASE-typed owner, so DEFER
                // (null -> generated-name, triggering the receiver-aware
                // re-resolve) instead of handing a stream member to StringIO's
                // wrong stringwrapper icall. Concrete StringReader/StringWriter
                // owners resolve directly (no ambiguity).
                if (receiverType is "System.IO.StreamReader" or "System.IO.StreamWriter")
                    return TryMapStreamIO(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
                if (receiverType is "System.IO.StringReader" or "System.IO.StringWriter")
                    return TryMapStringIO(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
                // console3 (2026-08-09): Console.Out / Console.Error stream
                // receivers -- get_Out/get_Error (MethodMap.Console.cs) return
                // the console stream objects tracked under the distinct
                // pseudo-types "System.Console.OutStream" / "System.Console.ErrorStream".
                // A TextWriter member call on such a receiver routes to the
                // console-stream icalls (TryMapConsoleStream, MethodMap.Console.cs)
                // -- the ONLY TextWriter surface the Console domain owns.
                // StringIO/StreamIO receivers keep the mappings above.
                if (receiverType is "System.Console.OutStream" or "System.Console.ErrorStream")
                    return TryMapConsoleStream(owner, name, ps, count);
                if (owner is "System.IO.TextReader" or "System.IO.TextWriter" && receiverType == null)
                    return null; // base owner, first pass -- defer to the receiver-aware re-resolve
                return TryMapStringIO(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);            case "System.IO.BinaryReader":
            case "System.IO.BinaryWriter": return TryMapBinaryIO(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Text.Encoding":
            case "System.Text.UTF8Encoding":
            case "System.Text.ASCIIEncoding":
            case "System.Text.UnicodeEncoding": return TryMapEncoding(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Guid": return TryMapGuid(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Decimal": return TryMapFormat(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapDecimal(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Numerics.BigInteger": return TryMapBigInteger(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Version": return TryMapVersion(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Uri": return TryMapUri(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Enum": return TryMapEnum(owner, name, ps, count, genArgs, genArgNames, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Data.DataTable":
            case "System.Data.DataRow":
            case "System.Data.DataColumn":
            case "System.Data.DataColumnCollection":
            case "System.Data.DataRowCollection":
            case "System.Data.InternalDataCollectionBase":
            case "System.Data.DataSet":
            case "System.Data.DataTableCollection":
                // ADO stage 1 (batch 152f): the minimal in-memory surface
                // (il2cpp_datatable.h). DataColumnCollection/DataRowCollection
                // pseudo-types (the Columns/Rows properties) fold to the table
                // via ::identity:: and re-resolve with the collection owner.
                // InternalDataCollectionBase is the shared base of both
                // collections (get_Count declares there).
                return TryMapData(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Attribute": return TryMapAttribute(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // batch 162 (eventcase): System.EventArgs -- get_Empty folds to
            // null (the event-handler `e` argument; every wired handler
            // ignores it).
            case "System.EventArgs":
                if (name == "get_Empty" && count == 0)
                    return I(ResolvedMethod.NullMarker, 0, true, true, false);
                return null;
            case "System.Type": return TryMapReflect(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapType(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapResourceManager(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Activator": return TryMapReflect(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Buffer": return TryMapBuffer(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Globalization.NumberFormatInfo":
            case "System.Globalization.TextInfo": return TryMapNumberFormat(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Diagnostics.Debug":
            case "System.Diagnostics.Trace": return TryMapDebug(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Diagnostics.Debugger": return TryMapDebugger(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Threading.Monitor": return TryMapMonitor(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Threading.ThreadPool": return TryMapThreadPool(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // batch 158 (timercase): System.Threading.Timer managed surface
            // (ctor/Change/Dispose -- timer.cpp handle-slot object).
            case "System.Threading.Timer": return TryMapTimer(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // batch 161 (timerscase): System.Timers.Timer -- the Component
            // wrapper (Elapsed event + AutoReset/Interval/Start/Stop).
            case "System.Timers.Timer": return TryMapTimers(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // batch 161: System.Timers.Timer's BASE -- System.ComponentModel.
            // Component.Dispose() (the using-block callvirt resolves to the
            // base). The receiver-hinted re-resolve dispatches a Timers.Timer
            // receiver to the timers dispose icall; a null hint falls through
            // to the generated-name path (Component has no other surface).
            case "System.ComponentModel.Component":
                if (name == "Dispose" && count == 0
                    && (receiverType ?? arg0Type) == "System.Timers.Timer")
                    return TryMapTimers("System.Timers.Timer", name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
                return null;
            case "System.Collections.ArrayList": return TryMapArrayList(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // M7 hash2 (2026-08-08): System.Collections.SortedList domain
            // (MethodMap.SortedList.cs) -- the sorted-array icall surface
            // (sortedlist.h). Currently wires the indexer SET (set_Item ->
            // il2cpp_sortedlist_set_by_key); the remaining surface is listed
            // in MethodMap.SortedList.cs for follow-up wiring agents.
            case "System.Collections.SortedList": return TryMapSortedList(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Runtime.InteropServices.Marshal": return TryMapMarshal(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, genArgNames);
            case "System.Runtime.InteropServices.SafeHandle": return TryMapSafeHandle(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Security.Cryptography.MD5":
            case "System.Security.Cryptography.SHA1":
            case "System.Security.Cryptography.SHA256":
            case "System.Security.Cryptography.SHA384":
            case "System.Security.Cryptography.SHA512":
            case "System.Security.Cryptography.HashAlgorithm":
            case "System.Security.Cryptography.MD5CryptoServiceProvider":
            case "System.Security.Cryptography.SHA1CryptoServiceProvider":
            case "System.Security.Cryptography.SHA256CryptoServiceProvider":
            case "System.Security.Cryptography.SHA384CryptoServiceProvider":
            case "System.Security.Cryptography.SHA512CryptoServiceProvider":
            case "System.Security.Cryptography.SHA1Managed":
            case "System.Security.Cryptography.SHA256Managed":
            case "System.Security.Cryptography.SHA384Managed":
            case "System.Security.Cryptography.SHA512Managed":
            case "System.Security.Cryptography.HMACSHA1":
            case "System.Security.Cryptography.HMACSHA256":
            case "System.Security.Cryptography.HMACSHA384":
            case "System.Security.Cryptography.HMACSHA512":
            case "System.Security.Cryptography.KeyedHashAlgorithm":
            case "System.Security.Cryptography.Aes":
            case "System.Security.Cryptography.SymmetricAlgorithm":
            case "System.Security.Cryptography.ICryptoTransform":
            case "System.Security.Cryptography.RNGCryptoServiceProvider":
            case "System.Security.Cryptography.RandomNumberGenerator": return TryMapCrypto(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Diagnostics.Process": return TryMapProcess(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // TimeZone domain (MethodMap.TimeZone.cs): TimeZoneInfo simplification
            // (Local/Utc singletons + offset/conversion/daylight probes) and the
            // net48 TimeZone.CurrentTimeZone old API -- timezone.h surface.
            case "System.TimeZoneInfo":
            case "System.TimeZone": return TryMapTimeZone(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // XmlDocument basic domain (MethodMap.Xml.cs): handle-gap mode --
            // LoadXml stores the root-node handle INTO the receiver local
            // (ResultIsReceiver), get_OuterXml serializes it back. The
            // callvirt's MemberRef declares OuterXml on the BASE type
            // (XmlNode -- csc emits the declaring type, xmlcase evidenced),
            // so the base owner routes to the same mapping.
            case "System.Xml.XmlDocument":
            case "System.Xml.XmlNode":
            case "System.Xml.XmlElement": return TryMapXml(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // XmlWriter minimal write surface (D-XMLW-1, MethodMap.Xml.cs
            // TryMapXmlWriter): System.Xml.XmlWriter / XmlWriterSettings route
            // to the xml_write.h handle-gap domain (Create -> the writer handle,
            // the Write* members, Close/Flush, and the settings setters). Before
            // this case they fell to the generic generated-name path -> undefined
            // C++ externs -> cmake_build FAIL (tests/cases/xmlwrite gate).
            case "System.Xml.XmlWriter":
            case "System.Xml.XmlWriterSettings": return TryMapXmlWriter(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // System.Net minimal domain (MethodMap.Net.cs): IPAddress routes to
            // the ipaddr.h parse layer; Socket/WebClient are LOUD rejections
            // (handle-gap / wrapper-gap -- webcase work order, without these cases
            // they silently fell to the generated-name path -> undefined C++).
            case "System.Net.IPAddress":
            case "System.Net.Sockets.Socket":
            case "System.Net.WebClient": return TryMapNet(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // RuntimeHelpers domain (MethodMap.RuntimeHelpers.cs): the net48
            // audit §3 b-class foldables (GetHashCode/Equals/GetObjectValue)
            // + LOUD for the JIT-prepare / stack-probe / CER hints.
            case "System.Runtime.CompilerServices.RuntimeHelpers": return TryMapRuntimeHelpers(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // LINQ domain (MethodMap.Linq.cs): System.Linq.Enumerable --
            // P1 construction order wires the ToList<int> materializer
            // (il2cpp_linq_to_list, linq_map.h:93).
            case "System.Linq.Enumerable":
                // Sum/Min/Max are CONCRETE non-generic overloads in net48
                // (Sum(this IEnumerable<int32>) etc. -- no MethodSpec, so
                // genArgs is null here). Decode the first GenericInst
                // parameter's element byte from the raw signature so the
                // TryMapLinq i4-family gate sees it. A non-int32 element
                // overload (IEnumerable<double>) decodes to its byte and is
                // LOUD-rejected by the gate.
                if (name is "Sum" or "Min" or "Max" && genArgs == null)
                    genArgs = LinqAggregateElementByte(md, sigHandle);
                return TryMapLinq(md, sigHandle, owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // Legacy LOUD family (MethodMap.Legacy.cs): System.Runtime.
            // Remoting.* / AppDomain / ArgIterator / ReaderWriterLock -- the
            // net48 audit §3/§6 LOUD rejection list (no runtime support).
            case "System.Runtime.Remoting.RemotingServices":
            case "System.Runtime.Remoting.Proxies.RealProxy":
            case "System.Runtime.Remoting.Contexts.Context":
            case "System.Runtime.Remoting.Messaging.Message":
            case "System.Runtime.Remoting.Messaging.StackBuilderSink":
            case "System.Runtime.Remoting.Channels.ChannelServices":
            case "System.AppDomain":
            case "System.ArgIterator":
            case "System.Threading.ReaderWriterLock": return TryMapLegacy(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // MathF domain (MethodMap.MathF.cs): System.MathF -- the corert
            // float-semantics math surface (float params 0x0C, results ride the
            // "d" double carrier -- the float-in-double M-stage model).
            case "System.MathF": return TryMapMathF(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // GCHandle domain (MethodMap.GCHandle.cs): the gchandle.cpp handle
            // table (Alloc/Free/Target/AddrOfPinnedObject/IsAllocated). The
            // value is a single int32 handle (the GCHandle slot).
            case "System.Runtime.InteropServices.GCHandle": return TryMapGCHandle(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // DependentHandle domain (MethodMap.DependentHandle.cs): the
            // degraded weak-primary handle table (dependhandle.cpp).
            case "System.Runtime.DependentHandle":
            case "System.Runtime.CompilerServices.DependentHandle": return TryMapDependentHandle(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // Reflection-handle domain (MethodMap.ReflectHandle.cs): the
            // RuntimeTypeHandle/RuntimeMethodHandle/RuntimeFieldHandle value
            // ops (get_Value/Equals/GetHashCode/GetRuntimeType).
            case "System.RuntimeTypeHandle":
            case "System.RuntimeMethodHandle":
            case "System.RuntimeFieldHandle": return TryMapReflectHandle(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // Resources domain (MethodMap.Resources.cs): Assembly manifest
            // resource queries + ResourceManager.GetString -- the RES-3
            // embedded-manifest surface (il2cpp_resources.h).
            case "System.Reflection.Assembly":
            case "System.Resources.ResourceManager": return TryMapResources(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapResourceManager(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            default: return null;
        }
        // #endregion
    }

    //--------------------------------------------------------------------------
    // Helpers. (The per-domain dispatchers live in the MethodMap.*.cs files.)
    //--------------------------------------------------------------------------

    //--------------------------------------------------------------------------
    // TryMapComparerInterface -- D-ICMP-1 (icomparedeep): lower a callvirt on
    // the mscorlib IComparable / IComparer interfaces to the runtime interface
    // dispatch when the receiver's concrete type is a translated user type.
    //
    //   owner          "System.IComparable" (CompareTo) / "System.Collections.
    //                  IComparer" (Compare) -- the DECLARING interface
    //   name          the interface member name
    //   count         the explicit parameter count (CompareTo: 1, Compare: 2)
    //   recvType      the receiver's tracked concrete type (receiverType hint,
    //                  then the arg0Type hint -- the fold passes both on the
    //                  receiver-aware re-resolve)
    //
    // The runtime entry resolves the method's vtable slot by NAME through the
    // registered typeinfo (arraysort.cpp FindVirtualSlotByName) and dispatches
    // through the receiver's vtable -- no interface-table machinery needed, so
    // a class that implements the mscorlib interface WITHOUT a translated
    // interface TypeDef still dispatches correctly. The receiver hint must
    // name a translated type whose methods include the member (a stale hint
    // from arg0Type on the first pass is rejected -- the re-resolve with the
    // true receiverType then fires).
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapComparerInterface(string owner, string name, int count,
        IReadOnlyList<TypeDef> knownTypes, string? recvType)
    {
        if (recvType == null) return null;
        var t = knownTypes.FirstOrDefault(tt => tt.FullName == recvType
            || (tt.GenericArity > 0 && recvType.StartsWith(tt.FullName + "_", StringComparison.Ordinal)));
        if (t == null) return null;
        if (!t.Methods.Any(m => m.Name == name && m.ParamCount == count && !m.IsStatic))
            return null;
        var cpp = owner == "System.IComparable"
            ? "il2cpp::il2cpp_icomparable_compare_to"
            : "il2cpp::il2cpp_icomparer_compare";
        return I(cpp, count, true, false, true);
    }

    private static ResolvedMethod I(string cpp, int pc, bool rv, bool rr, bool inst,
        string[]? casts = null, string? resultKind = null, bool prependCount = false, bool prependZero = false,
        bool isConsole = false, string? returnTypeName = null, bool resultIsReceiver = false,
        bool appendZero = false, string[]? appendLiterals = null,
        string[]? tupleReturnFields = null)
    {
        var refs = casts == null
            ? Enumerable.Repeat(rr, pc).ToArray() // placeholder, replaced below
            : casts.Select(c => c.Length == 0 || c == "drop" || c.StartsWith("fcap", StringComparison.Ordinal)).ToArray();
        return new ResolvedMethod(cpp, pc, isConsole, rv, rr, refs, inst,
            ParamTypeNames: casts, ResultKind: resultKind,
            PrependCountArg: prependCount, PrependZeroArg: prependZero,
            ReturnTypeName: returnTypeName, ResultIsReceiver: resultIsReceiver,
            AppendZeroArg: appendZero, AppendLiterals: appendLiterals,
            TupleReturnFields: tupleReturnFields);
    }

    private static bool[] ParamRefFlags(string paramTypes)
    {
        if (paramTypes.Length == 0) return Array.Empty<bool>();
        return paramTypes.Split(',').Select(p => p.StartsWith("r:", StringComparison.Ordinal)).ToArray();
    }

    // WF-M2 (2026-08-07): P/Invoke param encoding -> the emitter cast code.
    // MethodSignatureCoder encodes a struct param as "V:<FullName>" (BYREF as
    // "B:V:<FullName>"); the emitter's cast switch (EmitIcallCallArgs) keys on
    // short STABLE codes. The three registered structs (il2cpp_pinvoke_struct.h
    // -- wfm2 report §2: System.Drawing.Rectangle/Point + System.Windows.Forms.
    // Message) translate to "rect"/"point"/"msg"; the same code serves both the
    // by-value and by-ref shapes (the emitter distinguishes them by the arg
    // expression shape -- LocAddrExpr address vs value). Everything else keeps
    // its raw encoding: the emitter LOUD-rejects unknown cast codes (never a
    // silent emit), and "r:System.String" must stay untouched for StackFold's
    // string-arg marshalling detection.
    private static string PInvokeCastCode(string token) => token switch
    {
        "V:System.Drawing.Rectangle" or "B:V:System.Drawing.Rectangle" => "rect",
        "V:System.Drawing.Point" or "B:V:System.Drawing.Point" => "point",
        "V:System.Windows.Forms.Message" or "B:V:System.Windows.Forms.Message" => "msg",
        _ => token,
    };

    // Decode the signature blob → (param count, return type byte, per-param raw
    // type bytes) -- overload dispatch needs the raw bytes (char/int/bool share
    // the int32 slot in the MethodSignatureCoder encoding, but Append(char) and
    // Append(int) are different icalls).
    private static (int Count, byte Ret, byte[] Params) RawSignature(MetadataReader md, BlobHandle sigHandle)
    {
        var sig = md.GetBlobReader(sigHandle);
        // randomcase (Join<byte>): a GENERIC method signature (calling
        // convention 0x10/0x18) carries a GenericParameterCount BETWEEN the
        // header and the real param count -- not consuming it misparses the
        // count as the generic param count and shifts every following byte
        // (ret reads the count, ps reads ret...). Same consumption as
        // MethodSignatureCoder.Decode (M2 generics precedent).
        var hdr = sig.ReadSignatureHeader();
        if (hdr.IsGeneric) _ = sig.ReadCompressedInteger(); // GenericParameterCount
        var count = sig.ReadCompressedInteger();
        var ret = sig.ReadByte();
        // P5: GENERICINST (0x15) return types (e.g. Math.SinCos -> ValueTuple)
        // require full consumption of marker + TypeDefOrRef + arg count + elements
        // (ReadParamByte logic, line 1270-1280). ReadTypeHandle() does NOT consume
        // the GENERICINST arguments, so they leak into ps[0] and the mapping never
        // matches. Replicate the ReadParamByte GENERICINST logic here.
        if (ret is 0x15)
        {
            _ = sig.ReadByte(); // CLASS/VALUETYPE marker
            _ = sig.ReadTypeHandle();
            var n = sig.ReadCompressedInteger();
            for (var i = 0; i < n; i++) _ = ReadParamByte(md, ref sig);
        }
        else if (ret is 0x11 or 0x12 or 0x40) _ = sig.ReadTypeHandle();
        // e2e_ilgen: a GENERIC method's ret can be the method's own type
        // parameter -- Interlocked.Exchange<T>/CompareExchange<T> return T
        // (VAR 0x13 / MVAR 0x1E + compressed index). Not consuming the index
        // leaked it into ps[0] ([0x00,0x13] instead of [0x13,0x13]) and the
        // generic Interlocked gate (ps[0]==0x13) never matched. Same
        // consumption ReadParamByte applies to VAR/MVAR params.
        else if (ret is 0x13 or 0x1E) _ = sig.ReadCompressedInteger();
        // G2 (bitconvcase): an SZARRAY return (0x1D/0x1F, e.g. BitConverter.GetBytes
        // → byte[]) is followed by its element byte -- not consuming it leaks the
        // element byte into ps[0] and every GetBytes overload resolves to the same
        // indistinguishable shape. Consume recursively (the element itself may carry
        // trailing data).
        else if (ret is 0x1D or 0x1F) _ = ReadParamByte(md, ref sig);
        var ps = new byte[count];
        for (var i = 0; i < count; i++) ps[i] = ReadParamByte(md, ref sig);
        return (count, ret, ps);
    }

    // M7 linqcase: Sum/Min/Max are CONCRETE non-generic net48 overloads
    // (Sum(this IEnumerable<int32>), Min/Max likewise -- no MethodSpec, so
    // the generic-argument array is null at dispatch). The i4 aggregate gate
    // in TryMapLinq needs the ELEMENT byte; it lives inside the first
    // parameter's GENERICINST signature. Decode: skip header + param count +
    // return type, then read the first param -- a GenericInst (0x15) with the
    // CLASS/VALUETYPE marker + TypeDefOrRef + arg count + one element byte
    // per argument. Returns the first argument's element byte (int32 for the
    // i4 family), or null when the signature does not have that shape.
    private static byte[]? LinqAggregateElementByte(MetadataReader md, BlobHandle sigHandle)
    {
        var sig = md.GetBlobReader(sigHandle);
        sig.ReadSignatureHeader();
        var count = sig.ReadCompressedInteger();
        if (count < 1) return null;
        var ret = sig.ReadByte();
        if (ret is 0x11 or 0x12 or 0x40) _ = sig.ReadTypeHandle();
        else if (ret is 0x1D or 0x1F) _ = ReadParamByte(md, ref sig);
        var first = sig.ReadByte();
        if (first != 0x15) return null; // not a GenericInst source parameter
        var marker = sig.ReadByte();    // 0x12 CLASS / 0x11 VALUETYPE
        if (marker is not (0x12 or 0x11)) return null;
        _ = sig.ReadTypeHandle();
        var argc = sig.ReadCompressedInteger();
        if (argc < 1) return null;
        var elem = sig.ReadByte();
        return new[] { elem };
    }

    // D-LT-2 (linqtake): Enumerable.TakeWhile/SkipWhile carry TWO net48
    // overloads with IDENTICAL arity and identical parameter bytes --
    // (source, Func<TSource,bool>) and (source, Func<TSource,int,bool>) --
    // so `count`/`ps` cannot distinguish them (both are 2 params, both
    // 0x15 GenericInst). The difference lives in the PREDICATE's GenericInst
    // argument count: Func<TSource,bool> = 2, Func<TSource,int,bool> = 3.
    // Decode the method signature blob up to the SECOND parameter and return
    // its GenericInst arg count (0 when it is not a GenericInst -- a shape
    // the LINQ predicate surface cannot wire anyway). Same consumption rules
    // as LinqAggregateElementByte.
    private static int LinqSecondParamGenericArgCount(MetadataReader md, BlobHandle sigHandle)
    {
        var sig = md.GetBlobReader(sigHandle);
        var hdr = sig.ReadSignatureHeader();
        if (hdr.IsGeneric) _ = sig.ReadCompressedInteger(); // GenericParameterCount
        var count = sig.ReadCompressedInteger();
        if (count < 2) return 0;
        var ret = sig.ReadByte();
        // NOTE: the return type of the LINQ operators is IEnumerable<T> -- a
        // GenericInst (0x15) whose CLASS/VALUETYPE marker + type handle +
        // arg count + args follow the 0x15 byte. It MUST be consumed before
        // the params are read. ReadParamByte CANNOT be used for a GenericInst
        // ret: the 0x15 byte is already consumed as `ret`, so ReadParamByte
        // would read the CLASS marker (0x12) as a standalone type code and
        // misparse the payload (a SZARRAY 0x1D/0x1F ret works because its
        // payload IS an element type byte). Consume the payload manually --
        // the same shape LinqAggregateElementByte walks.
        if (ret is 0x11 or 0x12 or 0x40) _ = sig.ReadTypeHandle();
        else if (ret is 0x1D or 0x1F) _ = ReadParamByte(md, ref sig);
        else if (ret is 0x15)
        {
            _ = sig.ReadByte();              // CLASS/VALUETYPE marker
            _ = sig.ReadTypeHandle();
            var n = sig.ReadCompressedInteger();
            for (var i = 0; i < n; i++) _ = ReadParamByte(md, ref sig);
        }
        _ = ReadParamByte(md, ref sig);   // param 1 (the source array)
        var b = sig.ReadByte();           // param 2 (the predicate)
        if (b != 0x15) return 0;          // not a GenericInst predicate
        var marker = sig.ReadByte();      // 0x12 CLASS / 0x11 VALUETYPE
        if (marker is not (0x12 or 0x11)) return 0;
        _ = sig.ReadTypeHandle();
        return sig.ReadCompressedInteger();
    }

    // Consume one parameter type byte plus all of its trailing data; return the
    // dispatch byte.
    // BYREF (0x10): the element byte is the dispatch payload (ps carries the element
    // byte rather than the 0x10 marker -- Increment(ref int) and Increment(ref long)
    // are therefore distinguishable; the byref semantics at emission are carried by
    // the ldloca LocAddrExpr shape, unrelated to ps).
    private static byte ReadParamByte(MetadataReader md, ref BlobReader sig)
    {
        var b = sig.ReadByte();
        switch (b)
        {
            case 0x10: return ReadParamByte(md, ref sig); // BYREF: element follows, ps carries it
            case 0x11 or 0x12 or 0x40: _ = sig.ReadTypeHandle(); break;
            case 0x1D or 0x1F: _ = ReadParamByte(md, ref sig); break; // SZARRAY: element follows
            case 0x13 or 0x1E: _ = sig.ReadCompressedInteger(); break; // VAR/MVAR: index follows
            case 0x15:                                                // GenericInst
                // CLASS/VALUETYPE marker byte precedes the definition index
                // (MethodSignatureCoder.GenericInst precedent, delechain evidenced
                // `15 12 35 02 08 08`) -- without consuming the marker the
                // index reads the marker byte and the arg count reads the
                // index (read out of bounds for real generic params).
                sig.ReadByte();
                _ = sig.ReadTypeHandle();
                var n = sig.ReadCompressedInteger();
                for (var i = 0; i < n; i++) _ = ReadParamByte(md, ref sig);
                break;
        }
        return b;
    }

    // D-MAR-1 (Marshal struct family): capture the FULL TYPE NAME of every
    // VALUETYPE generic argument in a MethodSpec signature (SizeOf<Rectangle>()
    // needs "System.Drawing.Rectangle", not the raw 0x11 byte). The blob is
    // re-read from the start with the SAME consumption rules as ReadParamByte,
    // so the returned array stays parallel to the caller's genArgs byte array.
    // Returns null when NO argument is a VALUETYPE (the common case -- the
    // callers avoid the extra work).
    private static string[]? GenericArgValueTypeNames(MetadataReader md, BlobHandle sigHandle, byte[] genArgs)
    {
        var sig = md.GetBlobReader(sigHandle);
        if (sig.ReadByte() != 0x0A) return null; // not GENERICINST(0x0A)
        var argc = sig.ReadCompressedInteger();
        if (argc != genArgs.Length) return null;
        string?[] names = new string?[argc];
        var any = false;
        for (var i = 0; i < argc; i++)
        {
            var b = sig.ReadByte();
            switch (b)
            {
                case 0x11:
                {
                    var th = sig.ReadTypeHandle();
                    var n = th.Kind switch
                    {
                        HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)th),
                        HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)th),
                        _ => null,
                    };
                    names[i] = n;
                    any = true;
                    break;
                }
                case 0x12 or 0x40:
                    _ = sig.ReadTypeHandle();
                    break;
                case 0x1D or 0x1F:
                    _ = sig.ReadByte(); // SZARRAY: one element byte follows
                    break;
                case 0x13 or 0x1E:
                    _ = sig.ReadCompressedInteger(); // VAR/MVAR: index follows
                    break;
            }
        }
        return any ? names.Select(n => n ?? "?").ToArray() : null;
    }

    // Whether the return type is a managed reference. Note:
    // System.Reflection.Metadata.SignatureTypeCode has no Class member (0x12 CLASS
    // is remapped to TypeHandle(0x40)), so the decision is made on the raw byte:
    //   0x0E STRING / 0x12 CLASS / 0x1C OBJECT / 0x40 TypeHandle (nested class/ref)
    //   / 0x15 GENERICINST (delegate/List`N -- always a reference object in the
    //   reachable surface) → ref;
    //   everything else (0x08 I4 etc.) → value.
    // (0x15 matches ReturnTypeCpp's GenericTypeInstance arm in CodeGenerator.cs
    // -- delecb2 residual: MakeAdder returned Func<int,int> but IsRefType missed
    // 0x15, so the result temp/local degraded to int32_t.)
    private static bool IsRefType(SignatureTypeCode code)
    {
        var b = (byte)code;
        return b is 0x0E or 0x12 or 0x1C or 0x40 or 0x15;
    }

    private static string ResolveOwnerFullName(MetadataReader md, EntityHandle parent) =>
        parent.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)parent),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)parent),
            // M3 generics: a MemberRef on a generic instantiation carries a
            // TypeSpec parent. The METHOD NAME HASH uses the generic
            // DEFINITION's full name (the instantiation shares the definition
            // body); the instantiation suffix is dropped for the hash input.
            HandleKind.TypeSpecification => GenericDefinitionName(md, (TypeSpecificationHandle)parent),
            _ => "?",
        };

    // TypeSpec → generic DEFINITION name ("Box`1<int>" → "Box`1" -- the method
    // hash input uses the definition name).
    private static string GenericDefinitionName(MetadataReader md, TypeSpecificationHandle handle)
    {
        var spec = md.GetTypeSpecification(handle);
        var sig = md.GetBlobReader(spec.Signature);
        var header = sig.ReadByte();
        if (header != 0x15)
            throw new NotSupportedException($"TypeSpec signature byte 0x{header:X2} not GenericInst(0x15) in M3");
        var defType = sig.ReadByte();
        _ = defType;
        var defHandle = sig.ReadTypeHandle();
        return defHandle.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
            HandleKind.TypeSpecification => GenericDefinitionName(md, (TypeSpecificationHandle)defHandle),
            _ => throw new NotSupportedException($"generic definition kind {defHandle.Kind} in M3"),
        };
    }

    // TypeSpec → generic INSTANTIATION name (same encoding as StackFold:
    // "Box`1<int>" → "Box`1_int").
    private static string GenericInstantiationName(MetadataReader md, TypeSpecificationHandle handle)
    {
        var spec = md.GetTypeSpecification(handle);
        var sig = md.GetBlobReader(spec.Signature);
        var header = sig.ReadByte();
        if (header != 0x15)
            throw new NotSupportedException($"TypeSpec signature byte 0x{header:X2} not GenericInst(0x15) in M3");
        // GenericInst definition: ELEMENT_TYPE_CLASS(0x12)/VALUETYPE(0x11)/
        // TypeSpec(0x1B) + TypeDefOrRef coded index (Roslyn references the
        // generic definition via CLASS).
        var defType = sig.ReadByte();
        _ = defType; // consumed; M3 accepts CLASS/VALUETYPE
        EntityHandle defHandle = sig.ReadTypeHandle();
        string defName = defHandle.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
            HandleKind.TypeSpecification => GenericInstantiationName(md, (TypeSpecificationHandle)defHandle),
            _ => throw new NotSupportedException($"generic definition kind {defHandle.Kind} in M3"),
        };
        var argCount = sig.ReadCompressedInteger();
        var args = new string[argCount];
        for (var i = 0; i < argCount; ++i)
        {
            var b = sig.ReadByte();
            // M6 generic-argument expansion (the same byte table as
            // StackFold.cs GenericInstantiationName / CodeGenerator.cs
            // GenericInstantiationName): dictcase hit 0x0E
            // (Dictionary<string,int>), linqcase hit 0x02 (Func<int,bool>)
            // -- before the expansion every non-int32 arg threw here and
            // the transpile died before codegen. The suffix feeds the
            // generic-instance C++ name ({def}_<arg>_...), matching
            // TypeEmitter's {TypeCppName}_int encoding.
            args[i] = b switch
            {
                0x02 => "bool",
                0x03 => "char",
                0x04 => "sbyte",
                0x05 => "byte",
                0x06 => "short",
                0x07 => "ushort",
                0x08 => "int", // int32 argument
                0x09 => "uint",
                0x0A => "long",
                0x0B => "ulong",
                0x0C => "float",
                0x0D => "double",
                0x0E => "string",
                0x11 => "valuetype",
                0x12 => "class",
                0x13 => "int", // generic parameter argument (M3 single-instantiation convention: T -> int)
                0x1C => "object",
                0x1D or 0x1F => "array",
                var other => throw new NotSupportedException($"generic argument byte 0x{other:X2} in M3"),
            };
        }
        return defName + "_" + string.Join("_", args);
    }

    private static string Sanitize(string s) => string.Join("_", s.Split('.', '+', '`', '<', '>', ',', '[', ']'));
}
