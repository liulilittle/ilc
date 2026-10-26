using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Il2Cpp.Transpiler.Model;
using Il2Cpp.Transpiler.Opt;

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
// -- 34-domain parallel wiring skeleton (generated 2026-08-03) ---------------
// The main-file dispatch region already reserves case lines for the following
// 34 domains (each case calls TryMap<Domain>), backed by 34
// MethodMap.<Domain>.cs skeleton files (bodies contain only `return null;`,
// no real mappings):
//
//   Regex ' List ' Dictionary ' Stopwatch ' Interlocked ' IntPtr ' UIntPtr '
//   CultureInfo (already has real mappings, no wiring needed) ' Queue ' Stack '
//   StreamIO ' BinaryIO ' Encoding ' Guid ' Version ' Uri ' Enum ' Attribute '
//   Type ' ArraySort ' Buffer ' BitConverter ' Base64 ' MathExtra ' TimeExt2 '
//   StringExt3 ' NumberFormat ' Debug ' StackQueue ' DateTimeExtra '
//   ConvertExtra ' RandomExtra ' EnvironmentExtra ' Monitor
//
// Wiring agent procedure (edit only your own file, zero conflicts):
//   1. Open src/il2cppc/Emit/MethodMap.<Domain>.cs (the skeleton already
//      exists; no need to create it).
//   2. In the TryMap<Domain> body, fill switch(name) following the existing
//      MethodMap.Math.cs style (switch(owner) first for multi-owner domains),
//      building ResolvedMethod with the I(...) helper from the main-file
//      Helpers section; ps byte meanings are the RawSignature table above; the
//      `?? fallback` chains are already wired in the skeletons (e.g.
//      Math->MathExtra, Convert->BitConverter->Base64->ConvertExtra,
//      Array->ArraySort, String->StringExt3, DateTime/TimeSpan->TimeExt2->
//      DateTimeExtra, Queue/Stack->StackQueue, Random->RandomExtra,
//      Environment->EnvironmentExtra).
//   3. Return null for unmatched names (fall through to the generated-name
//      path); throw NotSupportedException for known-but-unwireable members
//      (LOUD, see the main-file BclResolve wrapper).
//   4. dotnet build src/il2cppc && dotnet test tests/unit -> once all green the
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
    string[]? TupleReturnFields = null,
    // P26 (bcldeep/charconv): the MemberReference fallthrough reached the
    // generated-name path for an EXTERNAL BCL OWNER (System.* type not defined
    // in the input assembly) -- the generated C++ function can never exist.
    // The fold must REJECT this LOUDLY at its FINAL resolution (after the
    // receiver-hinted re-resolve): a hint-less first pass is a DEFERRAL (the
    // Encoding/Data/Hash/TimeZone/ValueType mappers re-resolve with the
    // receiver), so the throw lives in StackFold, not inside BclResolve.
    bool IsFictionalBclFallback = false,
    // cultureprobe (provider routing): the 2-arg ToString(string,
    // IFormatProvider) overload of the double/decimal custom formatters.
    // The cast list still carries "drop" for the provider slot (the mapping
    // shape is unit-pinned), but the runtime ABI now takes the provider as a
    // trailing Il2CppObject*: this flag tells the emitter to PASS the dropped
    // argument through instead of skipping it. false everywhere else (the
    // "drop" skip contract is untouched).
    bool ProviderCulture = false,
    // B4 (dtoprobe): per-resolution icall argument order -- slot j takes
    // IL argument ArgOrder[j]; -1 emits the nullptr literal (a slot with
    // no IL source, e.g. the null format before a provider-only
    // argument). Null = the IcallArgOrder table, then identity. The CG7
    // table stays untouched (it keys shared icalls globally; a
    // per-resolution order would collide shapes there).
    int[]? ArgOrder = null,
    // P-D4: the [DllImport] library name ("mylib" / "kernel32" / ...). Only a
    // REAL DllImport carries it -- a mapped QCall that resolves to an icall
    // (MethodMap.Emit.cs `with { IsPInvoke = true }`) leaves it null. The
    // emitter uses it to route the call site: a library absent from the
    // static-link manifest is resolved DYNAMICALLY via il2cpp_pinvoke_resolve
    // (and its extern "C" declaration is suppressed). Null = static direct call.
    string? PinvokeModule = null,
    // GN-17 P0 (pinvokewide): the native C++ RETURN type of a real DllImport,
    // derived from the method signature (mirrors PinvokeReturnTypeCpp:
    // int64_t/double/uint64_t/float/intptr_t/struct). The il2cpp_pinvoke_call
    // wrapper's Ret must equal the native return width -- deriving it from
    // ResultKind narrows int64/double/float/uint64 to int32_t (the P0
    // 4000000001 -> -294967295 regression). Only a REAL DllImport (MethodDef
    // with an Import) carries it; mapped QCalls stay null.
    string? PinvokeRetType = null,
    // RF-REFL: a hint-less resolver arm that is a GENERIC fallback -- the fold
    // must RE-RESOLVE it with the tracked receiver type when one is available,
    // so a more specific arm (e.g. MemberInfo.get_Name on a ConstructorInfo vs
    // an untracked Type VALUE) wins. Without it the first-pass result starts
    // with "il2cpp::" and the fold's re-resolve gate never fires (the b20
    // ConstructorInfo ".ctor" regression). False everywhere else.
    bool ReresolveWithReceiver = false)
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
    // excmsg: decode a newobj token's CTOR parameter types (MethodSignatureCoder
    // encoding, e.g. "r:System.String,r:System.Exception"). The Argument*
    // exception overloads share parameter BYTES, so the throw site needs the
    // decoded text to pick the right net48 shape. Returns null when the token
    // does not name a readable MemberReference/MethodDefinition signature.
    public static string? CtorParamTypesOf(MetadataReader md, int token)
    {
        try
        {
            var handle = MetadataTokens.Handle(token);
            BlobHandle sig;
            switch (handle.Kind)
            {
                case HandleKind.MemberReference:
                    sig = md.GetMemberReference((MemberReferenceHandle)handle).Signature;
                    break;
                case HandleKind.MethodDefinition:
                    sig = md.GetMethodDefinition((MethodDefinitionHandle)handle).Signature;
                    break;
                default:
                    return null;
            }
            return MethodSignatureCoder.Decode(md, sig).ParamTypes;
        }
        catch (Exception)
        {
            return null;
        }
    }

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
            // D-MAR-1 (Marshal struct family) + P-D2 #3 (GDFFP typeId): the
            // generic-argument FULL NAMES (VALUETYPE structs AND CLASS/GenericInst
            // delegate types -- GenericArgFullNames; null for the others).
            var genArgNames = GenericArgFullNames(md, ms.Signature, genArgs);

            // M6: same-assembly instantiations (Id<int>) -- Roslyn emits the
            // MethodSpec on the MethodDefinition (genlist evidenced; the probe's
            // MemberRef form is the cross-assembly shape).
            if (ms.Method.Kind == HandleKind.MethodDefinition)
            {
                var dm = md.GetMethodDefinition((MethodDefinitionHandle)ms.Method);
                // P-G3 R-C: a generic METHOD on a SHARED definition (SwapRef<T>
                // -- the arity-0 class Swap is in the active share set) collapses
                // EVERY instantiation onto the definition's single shared object
                // body (the definition IS the object template; its byref T
                // params are Il2CppObject**). Bypass the M6 `_string` pass-through
                // variant (which is only emitted for NON-shared pure-passthrough
                // methods like genlist's Id<T>) and the EnsureInt32 default gate
                // so int and string calls bind to the SAME shared symbol.
                if (GenericUsageAnalyzer.SharedGenericDefs?.Contains(md.GetFullName(dm.GetDeclaringType())) == true)
                {
                    var sharedRes = ResolveMethodDefinition(md, (MethodDefinitionHandle)ms.Method);
                    // D5: a SHARED method-level generic method whose signature
                    // returns an MVAR renders the return as the boxed object
                    // slot (Il2CppObject* -- the shared clone's C++ return
                    // type). The raw decode classifies a GenericMethodParameter
                    // return as a value slot; lift it to a reference so the
                    // caller result temp is declared Il2CppObject*.
                    return sharedRes.ReturnsRef || !MethodSignatureReturnsMvar(md, dm.Signature)
                        ? sharedRes
                        : sharedRes with { ReturnsRef = true };
                }
                // P0GEN (generic-method variant routing): the concrete slot
                // variants ({name}_string/_long/_double/_<Struct>) exist ONLY
                // for a definition the pre-emission scan classified as
                // TYPE-AGNOSTIC (GenericUsageAnalyzer.GenericMethodVariants --
                // the SAME map the emission side reads). The old shape appended
                // the suffix UNCONDITIONALLY, so a type-DIRECTED generic method
                // (S<T>(T v) => v.ToString()) resolved to a symbol that was
                // never emitted: transpile OK, then cmake "'<name>_string' was
                // not declared in this scope" -- a phantom symbol, strictly
                // worse than a rejection.
                //
                // When the token is absent the BASE definition symbol is the
                // only correct answer (it is always emitted), so the resolution
                // falls back to it instead of fabricating a suffix.
                //
                // The MVAR SHAPE decides the per-slot typing: only the slots the
                // definition's signature actually declares as the method's own
                // generic parameter become the concrete slot type; everything
                // else keeps its declared type. Forcing every slot to a
                // reference (the old _string arm) made a non-MVAR return
                // `static int Ignore<T>(T v) => 42` resolve as ReturnsRef and
                // the caller emitted `reinterpret_cast<Il2CppObject*>(int32_t)`.
                //
                // TRI-STATE contract with the pre-scan:
                //   map null / scanned from ANOTHER assembly -> the scan does not
                //                       cover this module (synthetic-PE unit
                //                       tests, a nested Generate). Keep the
                //                       historical unconditional routing; the
                //                       shape is unknown so the legacy "MVAR
                //                       everywhere" defaults apply.
                //   map for THIS assembly -> authoritative: an entry with
                //                       EmitsVariants means the variants exist
                //                       (with its shape); an entry WITHOUT it, or
                //                       no entry at all, means they do not (base
                //                       symbol).
                var variantMap = GenericUsageAnalyzer.GenericMethodVariants;
                var variantMapApplies = variantMap != null
                    && GenericUsageAnalyzer.GenericMethodVariantsAssembly is { } scannedMvid
                    && md.GetGuid(md.GetModuleDefinition().Mvid) == scannedMvid;
                GenericUsageAnalyzer.GenericMethodVariantShape? variantShape = null;
                var variantsKnown = false;
                var hasVariant = true;
                if (genArgs.Length == 1 && variantMapApplies)
                {
                    variantsKnown = true;
                    // The key is the definition's generated C++ name -- the SAME
                    // string the emission side passes to EmitVariant, so the two
                    // sides cannot drift (and no metadata token aliasing).
                    // EmitsVariants is the verdict itself: a scanned
                    // type-DIRECTED definition has an entry too (so "absent"
                    // keeps meaning "never scanned"), but emits no variant.
                    hasVariant = variantMap!.TryGetValue(
                            ResolveMethodDefinition(md, (MethodDefinitionHandle)ms.Method).CppName, out var vs)
                        && vs.EmitsVariants;
                    if (hasVariant) variantShape = vs;
                }
                // The definition's MVAR return flag: an absent shape (scan not
                // run) keeps the legacy "the return is the generic parameter"
                // assumption.
                bool ReturnIsMvar() => variantShape?.ReturnMvar ?? true;
                // Per-slot reference classification of a variant: an MVAR slot
                // becomes the requested slot type (reference only for the string
                // variant); a NON-MVAR slot keeps the definition's own
                // classification. Without a shape, every slot is an MVAR slot
                // (the legacy arm).
                bool[] VariantParamRefs(ResolvedMethod d, bool mvarIsRef) =>
                    variantShape is { } sh
                        ? sh.ParamMvar.Select((mv, i) => mv ? mvarIsRef : i < d.ParamIsRef.Length && d.ParamIsRef[i]).ToArray()
                        : d.ParamIsRef.Select(_ => mvarIsRef).ToArray();
                if (genArgs.Length == 1 && genArgs[0] == 0x0E)
                {
                    var def = ResolveMethodDefinition(md, (MethodDefinitionHandle)ms.Method);
                    if (variantsKnown && !hasVariant) return def;
                    var returnsRef = ReturnIsMvar();
                    return def with
                    {
                        CppName = def.CppName + "_string",
                        ReturnsRef = returnsRef,
                        ParamIsRef = VariantParamRefs(def, mvarIsRef: true),
                        ReturnTypeName = returnsRef ? "System.String" : def.ReturnTypeName,
                    };
                }
                // GN-N4 stage-1 (GN-5 by-value MVAR, 1.5): a single LONG
                // (0x0A) / DOUBLE (0x0D) by-value generic argument routes to
                // the emitted int64_t/double slot variant of the definition
                // ({defCppName}_long / {defCppName}_double). The caller result
                // temp gets the width ResultKind ("l"/"d") ONLY when the
                // definition's RETURN is the MVAR; a concrete-width return keeps
                // its signature-derived kind (StackFold.UserCallResultKind).
                if (genArgs.Length == 1 && genArgs[0] == 0x0A)
                {
                    var def = ResolveMethodDefinition(md, (MethodDefinitionHandle)ms.Method);
                    if (variantsKnown && !hasVariant) return def;
                    return def with
                    {
                        CppName = def.CppName + "_long",
                        ParamIsRef = VariantParamRefs(def, mvarIsRef: false),
                        ResultKind = ReturnIsMvar() ? "l" : def.ResultKind,
                    };
                }
                if (genArgs.Length == 1 && genArgs[0] == 0x0D)
                {
                    var def = ResolveMethodDefinition(md, (MethodDefinitionHandle)ms.Method);
                    if (variantsKnown && !hasVariant) return def;
                    return def with
                    {
                        CppName = def.CppName + "_double",
                        ParamIsRef = VariantParamRefs(def, mvarIsRef: false),
                        ResultKind = ReturnIsMvar() ? "d" : def.ResultKind,
                    };
                }
                // GN-N4 stage-2 (Id<Pt> by-value MVAR): a single USER VALUE
                // TYPE (0x11 VALUETYPE) generic argument routes to the emitted
                // concrete-struct variant ({defCppName}_{structCppName}). The
                // result temp is the struct C++ type (ResultKind "us:{CppName}"
                // -- the same slot kind the P25b struct box/unbox uses) only
                // when the RETURN is the MVAR. The decoded genArgNames[0]
                // (GenericArgFullNames) carries the struct's FULL name; the C++
                // name comes from the types collection (TypeEmitter.TypeCppName
                // -- matches the emission key). No scan entry / unknown shape
                // binds the base definition symbol (never a fabricated name).
                if (genArgs.Length == 1 && genArgs[0] == 0x11
                    && genArgNames is { Length: >= 1 } && genArgNames[0] is { } structFullName)
                {
                    var structDef = knownTypes.FirstOrDefault(t => t.MatchesFullName(structFullName));
                    if (structDef != null && structDef.IsValueType)
                    {
                        var def = ResolveMethodDefinition(md, (MethodDefinitionHandle)ms.Method);
                        if (variantsKnown && !hasVariant) return def;
                        var scpp = TypeEmitter.TypeCppName(structDef);
                        return def with
                        {
                            CppName = def.CppName + "_" + scpp,
                            ParamIsRef = VariantParamRefs(def, mvarIsRef: false),
                            ResultKind = ReturnIsMvar() ? "us:" + scpp : def.ResultKind,
                        };
                    }
                }
                EnsureInt32DefaultInstantiation(md.GetFullName(dm.GetDeclaringType()), md.GetString(dm.Name), genArgs);
                return ResolveMethodDefinition(md, (MethodDefinitionHandle)ms.Method);
            }

            var mr = md.GetMemberReference((MemberReferenceHandle)ms.Method);
            var owner = ResolveOwnerFullName(md, mr.Parent);
            var name = md.GetString(mr.Name);
            // tuple1 (ValueTuple): the STATIC factory
            // System.ValueTuple.Create<T1..TN>(...) -- the elements are the
            // METHOD's generic arguments, so the closed instance name comes
            // from the MethodSpec blob (the same table the local decode uses).
            // It returns the tuple BY VALUE through the generated _New helper
            // (the _ctor helper takes the receiver ADDRESS -- binding the first
            // element to `self` was the old, broken route; see
            // ValueTupleSupport.EmitNew).
            if (name == "Create" && genArgs.Length > 0
                && (ValueTupleSupport.IsValueTupleName(owner)
                    || owner == "System.ValueTuple"))
            {
                var created = ValueTupleSupport.ClosedNameFromArgBytes(md, ms.Signature, genArgs.Length);
                if (created != null)
                {
                    ValueTupleSupport.Require(created);
                    return new ResolvedMethod(ValueTupleSupport.StructName(created) + "_New", genArgs.Length,
                        false, true, false, Enumerable.Repeat(false, genArgs.Length).ToArray(), false,
                        ResultKind: "vt:" + created);
                }
            }
            // tuple1 (Tuple): the STATIC factory System.Tuple.Create<T1..TN>(...)
            // -- the elements are the METHOD's generic arguments; the closed
            // Tuple`N instance name comes from the MethodSpec blob with the
            // System.Tuple` prefix. The generated Tuple`N struct has the SAME
            // layout/semantics as ValueTuple`N (the C# tuple syntax), so the
            // _New helper serves both.
            if (name == "Create" && genArgs.Length > 0 && owner == "System.Tuple")
            {
                var created = ValueTupleSupport.ClosedNameFromArgBytes(md, ms.Signature, genArgs.Length,
                    ValueTupleSupport.TupleDefPrefix);
                if (created != null)
                {
                    ValueTupleSupport.Require(created);
                    return new ResolvedMethod(ValueTupleSupport.StructName(created) + "_New", genArgs.Length,
                        false, true, false, Enumerable.Repeat(false, genArgs.Length).ToArray(), false,
                        ResultKind: "vt:" + created);
                }
            }
            // r77 fix: System.Tuple`N.get_Rest/get_Item* return Il2CppObject* (not int32_t)
            if (owner.StartsWith("System.Tuple`", StringComparison.Ordinal) &&
                (name == "get_Rest" || name.StartsWith("get_Item", StringComparison.Ordinal)))
            {
                return new ResolvedMethod(Sanitize(owner) + "_" + name, 0, true, false, false, Array.Empty<bool>(), false, false, null, false, null, false, false, false, null, false, null, null, null, false);
            }
            // P-D2 #2: GetFunctionPointerForDelegate<T>(T) routes through the
            // per-signature GfpS dispatcher (il2cpp_delegate_gfp_<hash> --
            // static single-cast delegates keep returning the raw methodPtr,
            // instance-bound delegates get a permanent strong-rooted slot thunk).
            // The delegate Invoke signature is resolved from the TRACKED
            // argument type (arg0Type -- StackFold objTypes); a signature that
            // cannot be resolved (e.g. a BCL generic delegate absent from the
            // input assembly metadata) falls through to the existing
            // icall_delegate_get_fn_ptr mapping below (static only, instance
            // stays LOUD at runtime -- the conservative baseline).
            if (owner == "System.Runtime.InteropServices.Marshal" && name == "GetFunctionPointerForDelegate")
            {
                var gfp = BuildDelegateGfpDispatch(md, arg0Type);
                if (gfp != null) return gfp;
            }
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

    // D5: whether a method signature's RETURN element is a METHOD-level generic
    // parameter (0x1E MVAR). A SHARED method-level generic method renders an
    // MVAR return as the boxed object slot; the caller result must be a
    // reference (the shared clone's C++ return type is Il2CppObject*).
    private static bool MethodSignatureReturnsMvar(MetadataReader md, BlobHandle sigHandle)
    {
        var sig = md.GetBlobReader(sigHandle);
        var hdr = sig.ReadSignatureHeader();
        if (hdr.IsGeneric) sig.ReadCompressedInteger();
        sig.ReadCompressedInteger(); // param count
        return MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte()) == 0x1E;
    }

    private static ResolvedMethod ResolveMethodDefinition(MetadataReader md, MethodDefinitionHandle handle)
    {
        var m = md.GetMethodDefinition(handle);
        var name = md.GetString(m.Name);
        var ownerFullName = md.GetFullName(m.GetDeclaringType());
        var (paramCount, returnType, paramTypes, returnTypeName) = MethodSignatureCoder.Decode(md, m.Signature);
        var isInstance = (m.Attributes & System.Reflection.MethodAttributes.Static) == 0;
        // M4 Task 1: P/Invoke -- the C++ name is the native entry symbol
        // (extern "C" declaration emitted by the transpiler).
        var import = m.GetImport();
        if (!import.Module.IsNil)
        {
            var entry = md.GetString(import.Name);
            // P-D4 P1: an ordinal entry point (net48 EntryPoint="#N") cannot
            // be expressed -- '#' is not a valid C++ identifier (the old
            // emission produced uncompilable generated code) and plat_dl_sym
            // resolves by NAME only. Reject LOUDLY at resolution (the decl
            // emitter guards the declared-but-never-called path); full ordinal
            // support is deferred to P3.
            if (entry.StartsWith("#", StringComparison.Ordinal))
                throw new NotSupportedException(
                    $"P/Invoke EntryPoint='{entry}' (ordinal entry) is not supported: '#' is not a valid native symbol name in this transpiler (P-D4)");
            // QCall short-circuit fix: same-assembly QCall callees never
            // reach the MethodMap tables (MemberReference path only), so a
            // mapped QCall (e.g. Environment.GetProcessorCount) would emit a
            // naked undefined extern. Consult the map first; unmapped QCalls
            // keep the legacy naked-decl path below.
            var importModuleName = md.GetString(md.GetModuleReference(import.Module).Name);
            if (importModuleName.Equals("QCall", StringComparison.Ordinal))
            {
                var qmap = BclResolve(md, ownerFullName, name, m.Signature, null, null, false, null, null);
                if (qmap != null) return qmap;
            }
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
                IsPInvoke: true, ParamTypeNames: CodeGenerator.SplitTopLevel(paramTypes).Select(p =>
                    utf16Strings && p == "r:System.String" ? "u16:System.String" : PInvokeCastCode(p)).ToArray(),
                SetLastError: setLastError,
                // M4.5/P-D1 (result-width mirrors, GN-17 P0): the result kind
                // must mirror PinvokeReturnTypeCpp / StackFold.UserCallResultKind
                // for EVERY native width -- int64/uint64 -> "l" (int64_t),
                // float/double -> "d" (double), native intptr -> "ptr", and
                // fnptr -> "p" (void*). The old "IntPtr/UIntPtr only" mapping
                // left int64/double/float/uint64 returns null, so BOTH the
                // result temp AND the GN-17 il2cpp_pinvoke_call<Ret> wrapper
                // collapsed them to int32_t (pinvokewide 4000000001 -> a
                // -294967295 truncation after the 5eacaad3 wrapper wrap).
                ResultKind: returnType switch
                {
                    SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr => "ptr",
                    SignatureTypeCode.Int64 or SignatureTypeCode.UInt64 => "l",
                    SignatureTypeCode.Single or SignatureTypeCode.Double => "d",
                    SignatureTypeCode.FunctionPointer => "p",
                    _ => null,
                },
                // GN-17 P0 (pinvokewide): the native C++ return type, so
                // il2cpp_pinvoke_call<Ret> carries the FULL native width
                // (deriving Ret from ResultKind narrows int64/double/float/
                // uint64 to int32_t -- the P0 regression). Mirrors
                // PinvokeReturnTypeCpp; struct returns go through the same
                // raw-name path (PinvokeRetCpp returns null -> the struct
                // type is resolved by the emitter as before).
                PinvokeRetType: PinvokeRetCpp(returnType),
                // P-D4: carry the [DllImport] library name so the call site
                // can route static vs dynamic (see the record field).
                PinvokeModule: importModuleName);
        }
        // tuple1 (ValueTuple): a method DEFINITION returning a tuple (the
        // same-assembly call shape) must carry the struct result kind, exactly
        // like the MemberRef path above.
        var vtRetDef = ValueTupleSupport.ReturnTupleName(md, m.Signature);
        if (vtRetDef != null) ValueTupleSupport.Require(vtRetDef);
        return new ResolvedMethod(
            UserMethodCppName(ownerFullName, name, paramTypes),
            paramCount, false, returnType != SignatureTypeCode.Void,
            vtRetDef == null && IsRefType(returnType), ParamRefFlags(paramTypes), isInstance,
            // batch 177: a USER method returning IntPtr/UIntPtr must carry
            // "ptr" like the P/Invoke path (the caller temp is then declared
            // intptr_t -- a null ResultKind falls to the default int32_t and
            // truncates the 64-bit handle on x64).
            ResultKind: vtRetDef != null ? "vt:" + vtRetDef
                : returnType is SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr ? "ptr" : null,
            // P25c (enprobe): a USER GetEnumerator returning the generic
            // iterator interface must carry the receiver hint -- the caller
            // stores the result into a local and the foreach MOVENEXT then
            // resolves through the NON-GENERIC System.Collections.IEnumerator
            // owner with that local as receiver. Without the ReturnTypeName the
            // receiver stays untracked, the receiver-hint re-resolve never
            // fires, and MoveNext falls to the generated-name path (undefined
            // symbol). The definition name (generics stripped) is what the
            // IEnumerator owner arm matches against.
            ReturnTypeName: IteratorInterfaceDefName(returnTypeName));
    }

    // G1 (C1): a multidim ARRAY accessor MemberRef -- the multidim indexer
    // reads/writes route to a rank-fixed runtime icall. Owner shape is the
    // "[<elemSuffix><rank>]" token (e.g. "[int2]" for int[,], "[int3]" for
    // int[,,]); element/rank are decoded from the token. Runtime surface
    // (runtime multidim support, GN-3 commit (2)):
    //   il2cpp::il2cpp_array_get_i4_<rank>d(Il2CppArray*, i, j, ...) -> elem
    //   il2cpp::il2cpp_array_set_i4_<rank>d(Il2CppArray*, i, j, ..., v) -> void
    // The receiver cast is "a" (get_Length precedent); the index args are
    // int32; Set's value arg is the last. Only int32 multidim elements have
    // the rank-fixed accessor surface (probe scope); everything else is LOUD.
    private static ResolvedMethod? TryMapMultidimAccessor(string owner, string name, int pc)
    {
        if (owner.Length < 3 || owner[0] != '[' || owner[^1] != ']') return null;
        var inner = owner[1..^1];
        // The element suffix is the longest known M3 suffix at the head; the
        // rest is the rank.
        string? elem = null; int rank = 0;
        foreach (var (s, w) in MdAccessorSuffixes)
        {
            if (inner.StartsWith(s, StringComparison.Ordinal) && s.Length > (elem?.Length ?? 0))
            {
                elem = s;
                rank = int.TryParse(inner[s.Length..], out var r) && r >= 1 ? r : 0;
            }
        }
        if (elem == null || rank == 0) return null;
        // Get: pc == rank (the indices). Set: pc == rank + 1 (indices + value).
        // Casts: slot 0 is the RECEIVER (the multidim array, Il2CppObject* ->
        // "a"); slots 1..pc are the index/value slots (int32 -- the rank-fixed
        // accessor signatures take int32_t indices and an int32_t value, the
        // "i" cast, the icall_array_get_length index precedent). The array is
        // sized pc+1 so EVERY emitted slot is covered (a shorter array falls
        // back to raw passthrough for the trailing value slot).
        if (name == "Get" && pc == rank)
        {
            var casts = Enumerable.Repeat("i", pc + 1).ToArray();
            casts[0] = "a"; // the receiver
            return I($"il2cpp::il2cpp_array_get_i4_{rank}d", pc, true, false, true, casts);
        }
        if (name == "Set" && pc == rank + 1)
        {
            var casts = Enumerable.Repeat("i", pc + 1).ToArray();
            casts[0] = "a"; // the receiver
            return I($"il2cpp::il2cpp_array_set_i4_{rank}d", pc, false, false, true, casts);
        }
        return null;
    }

    // The M3 element suffix/width table for the multidim accessor owner
    // decode (int32-only accessor surface; the element width isn't needed for
    // the i4 accessor names -- the suffix table drives the rank parse).
    private static readonly (string Suffix, int Width)[] MdAccessorSuffixes =
    {
        ("bool", 1), ("char", 2), ("sbyte", 1), ("byte", 1),
        ("short", 2), ("ushort", 2), ("int", 4), ("uint", 4),
        ("long", 8), ("ulong", 8), ("float", 4), ("double", 8),
        ("string", IntPtr.Size), ("object", IntPtr.Size),
    };

    // P25c (yieldprobe/enprobe): a user method (e.g. GetEnumerator) whose
    // return type decodes to the generic iterator interface carries the
    // receiver hint for the foreach MoveNext re-resolve. The encoded name from
    // MethodSignatureCoder is "r:System.Collections.Generic.IEnumerator`1<i>"
    // (generics retained); strip the "r:" prefix and the "<...>" argument list
    // to the plain DEFINITION name ("System.Collections.Generic.IEnumerator`1")
    // -- the name the IEnumerator-owner arm + enum receiver dispatch matches.
    private static string? IteratorInterfaceDefName(string? encodedReturnTypeName)
    {
        if (encodedReturnTypeName == null) return null;
        var bare = encodedReturnTypeName.StartsWith("r:", StringComparison.Ordinal)
            ? encodedReturnTypeName[2..]
            : encodedReturnTypeName;
        var lt = bare.IndexOf('<');
        var def = lt >= 0 ? bare[..lt] : bare;
        return def is "System.Collections.Generic.IEnumerator`1" or "System.Collections.IEnumerator"
            ? def
            : null;
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

        // G1 (C1): a multidim ARRAY accessor -- the multidim indexer
        // (`m[0,1]` -> int32_t[0...,0...]::Get(int32,int32)) or element write
        // (`m[1,1,0]=7` -> ::Set). The owner token is the element- AND
        // rank-aware "[<elem><rank>]" shape; the call routes to a rank-fixed
        // runtime multidim accessor (il2cpp_array_get_i4_<rank>d / _set_.
        // The receiver arrives as Il2CppObject* -> "a" cast (the get_Length
        // precedent); the indices are int32, the Set value is last.
        var mdAccessor = TryMapMultidimAccessor(owner2, mrName, pc);
        if (mdAccessor != null) return mdAccessor;

        // Tuple Rest/Item chain (8-tuple long chains): get_Rest always yields
        // a Tuple receiver (reference slot); get_ItemK yields the K-th
        // instantiation arg (int -> value, string/object/class -> reference).
        // The signature carries VAR (0x13) so the return kind cannot come
        // from retType -- decode the parent TypeSpec args instead.
        if (owner2.StartsWith("System.Tuple`", StringComparison.Ordinal) &&
            (mrName == "get_Rest" || mrName.StartsWith("get_Item", StringComparison.Ordinal)))
        {
            var isRef = true;
            string? resultKind = null;
            if (mrName.StartsWith("get_Item", StringComparison.Ordinal) &&
                mr2.Parent.Kind == HandleKind.TypeSpecification &&
                int.TryParse(mrName["get_Item".Length..], out var itemK) && itemK >= 1)
            {
                isRef = TupleItemIsReference(md, (TypeSpecificationHandle)mr2.Parent, itemK);
            }
            else if (mrName == "get_Rest" && mr2.Parent.Kind == HandleKind.TypeSpecification)
            {
                // The arity-8 Rest is a nested Tuple`1<...> VALUE (the C# tuple
                // syntax nests the 8th+ elements through TRest) -- return it by
                // value with the nested struct as the result kind so the
                // get_Item1/tostring chain on the result resolves.
                var restName = ValueTupleSupport.RestElementName(md, (TypeSpecificationHandle)mr2.Parent);
                // Only a nested TUPLE Rest (the C# 8th-slot nesting) is a value
                // struct; a primitive TRest (Tuple`8<int x8>) keeps the legacy
                // reference-slot answer (TupleRestChainTests).
                if (restName != null && ValueTupleSupport.IsValueTupleName(restName))
                {
                    ValueTupleSupport.Require(restName);
                    isRef = false;
                    resultKind = "vt:" + restName;
                }
            }
            return new ResolvedMethod($"{Sanitize(cppPrefixForTuple(md, mr2.Parent, owner2))}_{Sanitize(mrName)}",
                0, false, true, isRef, Array.Empty<bool>(), true, ResultKind: resultKind);
        }

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
        // RF-REFL: the STRUCTURAL comparison interfaces. A TUPLE receiver is
        // intercepted by the fold's tuple member handling (ValueTupleSupport's
        // per-element {T}_CompareTo / _CompareToComparer / _Equals helpers) --
        // the box of a ValueTuple is a passthrough (the tuple VALUE), so the
        // structural element-wise compare must be built at transpile time.
        // For a NON-tuple receiver (System.Tuple and any user structural
        // comparable) the call lowers to the IComparable / object-equality
        // dispatch (arraysort.cpp il2cpp_icomparable_compare_to /
        // il2cpp_object_equals): the receiver's own CompareTo / Equals override
        // IS the structural comparison for those types. The comparer / equality
        // comparer argument rides as a dropped slot (a custom comparer over a
        // non-tuple structural comparable is not honored -- documented).
        if (owner2 is "System.Collections.IStructuralComparable" && mrName == "CompareTo" && pc == 2)
            return I("il2cpp::il2cpp_icomparable_compare_to", pc, true, false, true,
                new[] { "", "", "drop" });
        if (owner2 is "System.Collections.IStructuralEquatable" && mrName == "Equals" && pc == 2)
            return I("il2cpp::runtime::icall_object_equals", pc, true, false, true,
                new[] { "", "", "drop" });

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

        // tuple1 (ValueTuple): System.ValueTuple`N has NO TypeDef in the
        // translated assembly (a BCL TypeRef), so every member reference fell
        // to the phantom generated-name fallback (undefined symbol at link).
        // The struct + its member helpers are emitted by ValueTupleSupport, so
        // the resolver maps the three member families to those helpers:
        //   .ctor(args)                 -> struct field stores (emitter-side)
        //   ToString()                  -> {T}_ToString(self)
        //   GetHashCode()               -> {T}_GetHashCode(self)
        //   Equals(ValueTuple`N)        -> {T}_Equals(self, other)
        // Equals(object) stays on the Object path (boxing semantics).
        //
        // The STATIC factory ValueTuple.Create(...) arrives as a MethodSpec on
        // the ValueTuple`N DEFINITION; it is the same field-initialization
        // shape as the instance ctor, so it maps to the same helper.
        if (ValueTupleSupport.IsValueTupleName(owner2) && mrName == "Create" && pc == 0)
        {
            // ValueTuple.Create() -- the arity-0 factory returns the
            // System.ValueTuple 0-tuple by value (net48 h.zero:
            // GetHashCode() == 0). pc == 0 implies the non-generic 0-tuple.
            ValueTupleSupport.Require(ValueTupleSupport.ZeroName);
            return new ResolvedMethod(ValueTupleSupport.NewFn(ValueTupleSupport.ZeroName), 0, false, true, false,
                Array.Empty<bool>(), false, ResultKind: "vt:" + ValueTupleSupport.ZeroName);
        }
        if (ValueTupleSupport.IsValueTupleName(owner2) && mrName == "Create" && pc > 0)
        {
            // The non-MethodSpec shape (a direct call, no generic table) has no
            // element names -- LOUD rather than a guessed struct.
            throw new NotSupportedException(
                "System.ValueTuple.Create requires a generic method instantiation (no element type table at this call site)");
        }
        if (mr2.Parent.Kind == HandleKind.TypeSpecification
            && ValueTupleSupport.IsValueTupleName(cppPrefix))
        {
            ValueTupleSupport.Require(cppPrefix);
            var isTupleInstance = md.GetBlobReader(mr2.Signature).ReadSignatureHeader().IsInstance;
            // A user METHOD returning a tuple carries the struct as its result
            // kind ("vt:<closedName>") so the call-site temp is declared with
            // the struct type (not Il2CppObject*).
            var vtResultKind = "vt:" + cppPrefix;
            switch (mrName)
            {
                case "ToString" when pc == 0:
                    return new ResolvedMethod(ValueTupleSupport.ToStringFn(cppPrefix), 0, false, true, true,
                        new[] { true }, isTupleInstance, ParamTypeNames: new[] { "tupref" });
                case "GetHashCode" when pc == 0:
                    // GetHashCode returns int32 (a VALUE, not a managed
                    // reference) -- ReturnsRef must be false or the result slot
                    // is declared Il2CppObject* and every int32 store fails.
                    return new ResolvedMethod(ValueTupleSupport.GetHashCodeFn(cppPrefix), 0, false, true, false,
                        new[] { true }, isTupleInstance, ParamTypeNames: new[] { "tupref" });
                case "Equals" when pc == 1:
                    // The strongly-typed overload (the argument is the same
                    // instantiation) is structural; the object overload is the
                    // ValueType default (reference/box identity), which the
                    // Object domain already answers.
                    if (SignatureArgIsSameTuple(md, mr2.Signature, cppPrefix))
                        return new ResolvedMethod(ValueTupleSupport.EqualsFn(cppPrefix), 1, false, true, false,
                            new[] { true, true }, isTupleInstance, ParamTypeNames: new[] { "tupref", "tupref" });
                    break;
                case ".ctor":
                    // The emitter lowers this to per-field stores (see
                    // CodeGenerator's vctor_ ValueTuple arm). ParamCount must
                    // stay the IL ctor's PARAMETER count -- the fold pops that
                    // many values before the emitter sees the receiver.
                    return new ResolvedMethod(ValueTupleSupport.StructName(cppPrefix) + "_ctor", pc, false, false,
                        false, Enumerable.Repeat(true, pc).ToArray(), isTupleInstance,
                        ParamTypeNames: Enumerable.Repeat("tupctor", pc).ToArray());
            }
            _ = vtResultKind;
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
        // GN-2 (Nullable): a System.Nullable`1<T> member call reaches here with
        // a TypeSpec parent, so the owner-keyed BCL dispatch below sees the
        // DEFINITION name "System.Nullable`1" but not the underlying T -- the
        // md-aware arm decodes T from the parent TypeSpec and returns the
        // synthetic marker WITH its slot kind (l/d/dec/dt/ts/guid/us:...).
        var nullableMr = TryMapNullableMember(md, mr2.Parent, mrName, pc);
        if (nullableMr != null) return nullableMr;

        // The generic KEY type of the parent instantiation (the Dictionary`2
        // tuple-key mapper derives the element kinds from it). null for a
        // non-generic parent.
        string? genericKeyTypeName = null;
        string? genericValueTypeName = null;
        if (mr2.Parent.Kind == HandleKind.TypeSpecification)
        {
            var specArgs = GenericInstantiationArgs(md, (TypeSpecificationHandle)mr2.Parent);
            if (specArgs is { Length: > 0 }) genericKeyTypeName = specArgs[0];
            if (specArgs is { Length: > 1 }) genericValueTypeName = specArgs[1];
        }
        var bcl2 = BclResolve(md, owner2, mrName, mr2.Signature, null, receiverType, receiverIsAddress, arg0Type, arg1Type,
            instName: mr2.Parent.Kind == HandleKind.TypeSpecification ? cppPrefix : null,
            keyTypeName: genericKeyTypeName, valueTypeName: genericValueTypeName);
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
            && !TypeLookup.Any(knownTypes, owner2))
        {
            var drops = CodeGenerator.SplitTopLevel(pt).Select(_ => "drop").ToArray();
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
        // tuple1 (ValueTuple): a USER method whose RETURN type is a tuple --
        // the GENERICINST return is normally a managed reference, so the
        // call-site temp would be Il2CppObject* and `return <struct>;` would
        // not compile. Override ReturnsRef and carry the struct as the result
        // kind ("vt:<closedName>", resolved by ExprKind / the declaration arm).
        var vtRet = ValueTupleSupport.ReturnTupleName(md, mr2.Signature);
        if (vtRet != null) ValueTupleSupport.Require(vtRet);
        return new ResolvedMethod(
            $"{Sanitize(cppPrefix)}_{Sanitize(mrName)}_{SigHash.Hash(hashInput + "." + mrName + "(" + pt + ")")}",
            pc, false, retType != SignatureTypeCode.Void,
            vtRet == null && IsRefType(retType), ParamRefFlags(pt), isInstanceMr,
            // batch 177: same user-method IntPtr/UIntPtr result-kind fix as
            // ResolveMethodDefinition -- the caller temp must be intptr_t.
            ResultKind: vtRet != null ? "vt:" + vtRet
                : retType is SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr ? "ptr" : null,
            // P26 (bcldeep/charconv): the MemberReference fallthrough for an
            // EXTERNAL BCL owner is a PHANTOM generated name -- flag it so
            // StackFold.EmitCall REJECTS it loudly at the FINAL resolution
            // (the hint-less first pass must stay a silent DEFERRAL for the
            // receiver-typed re-resolve). User-defined owners (defined in the
            // input assembly) are real generated functions -- no flag.
            IsFictionalBclFallback: IsExternalBclOwner(md, owner2));
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
        var bytes = string.Join(", ", offending.Select(o => $"0x{o.Byte:X2}"));
        var kinds = string.Join(", ", offending.Select(o => $"argument #{o.Index + 1} {GenericArgTypeName(o.Byte)} ({bytes})"));
        throw new NotSupportedException($"no BCL mapping: generic instantiation of {owner}.{name} with unsupported generic argument(s) [{bytes}] ({kinds}; M6 model generates only T->int32/uint32)");
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
        0x0B => "uint64",
        0x0D => "double",
        0x02 => "bool",
        0x03 => "char",
        0x04 => "sbyte",
        0x05 => "byte",
        0x06 => "int16",
        0x07 => "uint16",
        0x11 => "valuetype",
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
    // P26 (bcldeep/charconv): the FINAL-resolution loud guard. StackFold calls
    // this after its receiver-hinted re-resolve (StackFold.cs:2415) -- the
    // moment at which a MemberReference can no longer be deferred. A
    // phantom-shaped result (generated-name fallback on an EXTERNAL BCL owner)
    // would emit a function that can never link: reject it here with a
    // self-identifying message instead of a late cmake "use of undeclared
    // identifier" failure.
    public static ResolvedMethod? RequireResolvable(MetadataReader? md, int token, ResolvedMethod? resolved)
    {
        if (md != null && resolved is { IsFictionalBclFallback: true })
            throw new NotSupportedException(M5PhantomMessage(md, token));
        return resolved;
    }

    private static string M5PhantomMessage(MetadataReader md, int token)
    {
        var mr = md.GetMemberReference(MetadataTokens.MemberReferenceHandle(token));
        var name = md.GetString(mr.Name);
        var owner = mr.Parent.Kind switch
        {
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)mr.Parent),
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)mr.Parent),
            _ => "unknown-owner",
        };
        return $"{owner}::{name}: not wired in M5 (BCL owner has no mapping arm and is not defined in the input assembly -- a phantom generated-name call would never link)";
    }

    public static ResolvedMethod? BclResolve(MetadataReader md, string owner, string name, BlobHandle sigHandle,
        byte[]? genArgs, string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type = null,
        string[]? genArgNames = null, string? instName = null, string? keyTypeName = null,
        string? valueTypeName = null)
    {
        try
        {
            var r = BclResolveCore(md, owner, name, sigHandle, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type,
                genArgNames, instName, keyTypeName, valueTypeName);
            if (r == null && IsExternalBclOwner(md, owner) && receiverType != null)
            {
                // P26 (bcldeep): the generated-name fallback for an EXTERNAL
                // BCL owner is a PHANTOM -- the input assembly never defines
                // System.* types, so the generated C++ function can never
                // exist and the cmake build fails with "use of undeclared
                // identifier System_Xxx_<hash>" AFTER the transpile. That
                // silent-late-failure must be a LOUD early gap instead.
                //
                // charconv G2 (P26 QA): the throw must NOT fire on a hint-less
                // FIRST pass -- the ReceiverType==null pass is the DEFERRAL
                // contract of the Encoding/Data/Dictionary/Hash/TimeZone/
                // ValueType mappers ("if (receiverType == null) return null"),
                // which StackFold re-resolves with the tracked receiver at
                // StackFold.cs:2415. A receiver-typed attempt that still comes
                // back null is a REAL gap: throw here. A hint-less final that
                // stays unresolved is rejected by StackFold's
                // RequireResolvable (IsFictionalBclFallback) instead.
                throw new NotSupportedException($"{owner}::{name}: not wired in M5 (BCL owner has no mapping arm and is not defined in the input assembly -- a phantom generated-name call would never link; receiverType='{receiverType ?? "<null>"}' arg0Type='{arg0Type ?? "<null>"}')");
            }
            return r;
        }
        catch (NotSupportedException ex)
        {
            // M5 wiring: every wiring gap must be LOUD and self-identifying.
            throw new NotSupportedException($"{owner}::{name}: {ex.Message}");
        }
    }

    // Is this owner a BCL type the input assembly NEVER defines (there is no
    // TypeDefinition for it -> the generated-name fallback would be a phantom)?
    // EXEMPTIONS: System.Object / System.ValueType (their virtual members are
    // re-resolved by StackFold with the receiver's tracked type AFTER a null
    // first pass); the BCL DELEGATE families (Func`/Action`/Converter`/... --
    // their .ctors are consumed by StackFold's EmitNewObj delegate path, which
    // needs the raw resolution to proceed). The guard must not pre-empt either.
    private static bool IsExternalBclOwner(MetadataReader md, string owner)
    {
        if (owner != "System" && !owner.StartsWith("System.", StringComparison.Ordinal)
            && !owner.StartsWith("Microsoft.", StringComparison.Ordinal))
            return false;
        if (owner is "System.Object" or "System.ValueType")
            return false;
        if (owner.StartsWith("System.Func`", StringComparison.Ordinal)
            || owner.StartsWith("System.Action`", StringComparison.Ordinal)
            || owner.StartsWith("System.Predicate`", StringComparison.Ordinal)
            || owner.StartsWith("System.Converter`", StringComparison.Ordinal)
            || owner.StartsWith("System.Comparison`", StringComparison.Ordinal)
            || owner is "System.Threading.ThreadStart" or "System.Threading.WaitCallback"
                or "System.Threading.TimerCallback" or "System.Timers.ElapsedEventHandler"
                or "System.EventHandler" or "System.EventHandler`1")
            return false;
        foreach (var tdh in md.TypeDefinitions)
        {
            var full = md.GetFullName(tdh);
            if (full == owner)
                return false;
        }
        return true;
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
        string[]? genArgNames = null, string? instName = null, string? keyTypeName = null,
        string? valueTypeName = null)
    {
        var (count, ret, ps) = RawSignature(md, sigHandle);
        // excmsg: the Argument*Exception family resolves through the EXCEPTION
        // domain (MethodMap.Exception.cs) for EVERY member -- Message and
        // ParamName split on the RECEIVER shape there (catch carrier vs a
        // constructed instance), and the ctors map by (owner, param types).
        // The old blanket intercepts here routed every receiver to the
        // catch-carrier reads (thread-local paramName / __il2cpp_ex message),
        // which is a phantom outside a catch block and a silent wrong value on
        // a constructed instance.
        if (owner is "System.ArgumentException" or "System.ArgumentNullException"
            or "System.ArgumentOutOfRangeException" or "System.SystemException")
        {
            return TryMapException(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type,
                MethodSignatureCoder.Decode(md, sigHandle).ParamTypes);
        }
        // DateTimeOffset arithmetic overloads (Add(TimeSpan) vs the DateTime
        // operand forms; Subtract(TimeSpan) vs Subtract(DateTimeOffset)) carry
        // two valuetype parameter bytes (0x11) and cannot be told apart from
        // ps alone -- the decoded parameter-type text disambiguates.
        var dtoParamTypes = owner == "System.DateTimeOffset"
            ? MethodSignatureCoder.Decode(md, sigHandle).ParamTypes
            : null;
        // LOUD P0-19: System.Math's decimal overloads. A decimal PARAMETER
        // (ldarg) is not tracked by StackFold's objTypes, so arg0Type stays
        // null and the 0x11 signature byte alone cannot name the type
        // (DateTime/enum/user structs share it). The DECODED parameter text
        // ("V:System.Decimal") is the authoritative signature key: fill the
        // hint for value-type parameters only, leaving primitive params
        // untracked (their maps never consult it).
        if (owner == "System.Math")
        {
            var mathParams = MethodSignatureCoder.Decode(md, sigHandle).ParamTypes.Split(',');
            if (arg0Type == null && mathParams.Length >= 1
                && mathParams[0].StartsWith("V:", StringComparison.Ordinal))
                arg0Type = mathParams[0][2..];
            if (arg1Type == null && mathParams.Length >= 2
                && mathParams[1].StartsWith("V:", StringComparison.Ordinal))
                arg1Type = mathParams[1][2..];
        }
        // nb9 H-B3: System.Array.Sort<T>(T[], IComparer<T>) and
        // Sort<T>(T[], Comparison<T>) share the same GENERICINST parameter
        // bytes; the decoded second-parameter DEFINITION name disambiguates.
        var arrayParamTypes = owner == "System.Array"
            ? MethodSignatureCoder.Decode(md, sigHandle).ParamTypes
            : null;
        // misc3 console alignment: the Write/WriteLine VALUETYPE overloads
        // (decimal) share the 0x11 raw byte with DateTime/enum/user structs,
        // so the DECODED parameter text names them.
        var consoleParamTypes = owner == "System.Console"
            ? MethodSignatureCoder.Decode(md, sigHandle).ParamTypes
            : null;
        // #region BCL domain dispatch
        switch (owner)
        {
            case "System.Console": return TryMapConsole(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, consoleParamTypes);
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
            case "System.UInt64":
                // Format first (uint/ulong ToString(format) ride the signed
                // custom engines -- see MethodMap.Format.cs), ValueType
                // fallback for the rest.
                return TryMapFormat(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                    ?? TryMapValueType(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Char":
                // D-CHA-2: the char STATIC surface (IsDigit/IsLetter/ToUpper
                // ...) is TryMapChar (MethodMap.Char.cs); the instance surface
                // (ToString()/Equals(object)) falls through to TryMapValueType.
                return TryMapChar(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                    ?? TryMapValueType(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.ValueType":
                // Value-type GetHashCode wiring (audit section 5 #5): the constrained
                // value-type receiver's first-instance-field kind comes from
                // the user-struct TypeDef metadata and the per-kind runtime
                // target is selected here (the emitter cannot inject the
                // typeHash/fieldKind constants icall_value_type_get_hash_code
                // needs -- see MethodMap.ValueType.cs for the mapping table).
                return TryMapValueTypeGetHashCode(md, owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.String": return TryMapString(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapStringExt3(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // GN-2 (Nullable): System.Nullable`1<T> members (get_HasValue /
            // get_Value) map to synthetic markers the CodeGenerator inlines
            // against the boxed-object Nullable representation (MethodMap.Nullable.cs).
            case "System.Nullable`1": return TryMapNullable(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Object":
            case "System.IConvertible":
            // P0-narrow (2026-10-22): System.IFormattable had NO dispatch case
            // at all -- `((IFormattable)sb).ToString("X", inv)` fell to the
            // phantom generated-name path and the whole METHOD was dropped
            // from the output. The explicit-interface call on a boxed
            // primitive is the composite formatter (MethodMap.Object.cs
            // TryMapObject).
            case "System.IFormattable":
            case "System.Reflection.MemberInfo":
                // tuple1 (ValueTuple/Tuple): the net48 reference assemblies
                // declare the Tuple/ValueTuple ToString/GetHashCode overrides,
                // but csc still routes `tuple.ToString()` / `.GetHashCode()`
                // through Object::ToString / Object::GetHashCode (the callvirt
                // token's declaring type is System.Object). The receiver's
                // tracked type names the generated tuple struct, so dispatch to
                // its member helper instead of the object icall (which would
                // cast the struct value to Il2CppObject* -- uncompilable).
                if (receiverType != null && ValueTupleSupport.IsValueTupleName(receiverType)
                    && TryMapTupleObjectMember(receiverType, name, count) is { } tupleObj)
                    return tupleObj;
                return name == "get_Module"
                    ? TryMapModule(md, owner, name, count)
                    : TryMapObject(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // raudit3 (2026-09-13): the MemberInfo-derived reflection objects
            // emit the BASE ==/!= operator (csc never calls the derived
            // overload); the runtime models a member value as its name string
            // (Type.GetField -> il2cpp_typeinfo_get_field, P1.32), so the
            // family maps to ordinal string (in)equality -- the Type operator
            // precedent. Other members on these owners keep the LOUD default.
            case "System.Reflection.FieldInfo":
            case "System.Reflection.PropertyInfo":
            case "System.Reflection.MethodInfo":
            case "System.Reflection.MethodBase":
            case "System.Reflection.ConstructorInfo":
            case "System.Reflection.EventInfo":
                return name == "get_Module"
                    ? TryMapModule(md, owner, name, count)
                    : TryMapReflectionMember(owner, name, ps);
            // REFL6B (item B3): the Module VALUE (the module-name string the
            // get_Module fold produces) dispatches its own get_Name identity.
            case "System.Reflection.Module":
                return TryMapModule(md, owner, name, count);
            // REFL3 (item 2): the ParameterInfo property surface. The receiver
            // is the ParameterInfo VALUE string ("name|typeFullName|position|
            // flags") il2cpp_typeinfo_get_parameters produces; get_Name /
            // get_ParameterType / get_Position / get_IsOut decode it.
            case "System.Reflection.ParameterInfo":
                return TryMapParameterInfo(owner, name, ps, count);
            case "System.Text.StringBuilder": return TryMapStringBuilder(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // P25f (dhprobe): the net6+ interpolation lowering -- an
            // address-taken DefaultInterpolatedStringHandler VALUE local + the
            // .ctor/AppendLiteral/AppendFormatted<T>/ToStringAndClear instance
            // calls (a StringBuilder-fold; TryMapDefaultInterpHandler).
            case "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler":
                return TryMapDefaultInterpHandler(owner, name, ps, count, genArgs, genArgNames, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Collections.Hashtable": return TryMapHashtable(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // D-ICMP-2 (icomparedeep): System.Collections.Comparer (the
            // non-generic DEFAULT comparer -- Comparer.Default / DefaultInvariant
            // getters + Compare(object, object)) maps to the runtime comparer
            // domain (il2cpp_comparer_default / il2cpp_comparer_compare,
            // arraysort.h). The getter result is tracked under the pseudo-type
            // "System.Collections.Comparer" so the later Compare callvirt
            // resolves on the receiver hint.
            case "System.Collections.Comparer": return TryMapComparer(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // a5reds (strsort_ordinal): System.StringComparer static getters
            // (Ordinal / OrdinalIgnoreCase) -> the runtime singleton comparers
            // (arraysort.h il2cpp_string_comparer_*); the result is tracked
            // under the klass name so the Array.Sort<T> arm can route it to
            // the comparer sort instead of the delegate sort.
            case "System.StringComparer": return TryMapStringComparer(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Collections.Generic.EqualityComparer`1": return TryMapGenericEqualityComparer(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, instName);
            case "System.Collections.Generic.Comparer`1": return TryMapGenericComparer(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, instName);
            case "System.Random": return TryMapRandom(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapRandomExtra(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Environment": return TryMapEnvironment(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapEnvironmentExtra(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.IO.File":
            case "System.IO.Directory":
            case "System.IO.Path":
            case "System.IO.FileInfo": return TryMapIO(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Reflection.Emit.ModuleBuilder":
            case "System.Security.Principal.Win32":
            case "System.Security.Policy.PEFileEvidenceFactory":
            case "System.IO.IsolatedStorage.IsolatedStorageFile":
                return TryMapEmit(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.GC":
            // GCSettings (System.Runtime.GCSettings) owns only GC properties
            // (get_IsServerGC/get_LatencyMode/set_LatencyMode). It must NOT
            // ride the WeakReference bucket below: TryMapWeakReference THROWS
            // on foreign members (only WeakReference* owners belong there), so
            // the `?? TryMapGC` fallback never ran and GCSettings.IsServerGC
            // died as a phantom (dscmp fold-skip). Dispatch it to the GC map
            // directly.
            case "System.Runtime.GCSettings":
                return TryMapGC(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // System.Diagnostics.StackTrace has the SAME ownership trap as
            // System.Runtime.GCSettings: it is listed in the WeakReference
            // bucket below, and TryMapWeakReference THROWS on foreign members,
            // so the `?? TryMapGC` fallback never runs -- GetStackFramesInternal
            // died as a WeakReference phantom (GN-20 P1 audit realErr). The
            // StackTrace members all live in the GC map (TryMapGC); dispatch
            // directly so the `GetStackFramesInternal` arm actually fires.
            // (net48 semantics -- live-stack frame enumeration -- are recorded
            // as W7 in the p-stacktrace GAPS.md: not statically reconstructible;
            // the icall STILL must map so user methods calling `new StackTrace()`
            // do not LOUD-reject/fold-skip.)
            case "System.Diagnostics.StackTrace":
                return TryMapGC(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // netcore21/31 audit surfaces routed into the GC map (the
            // GetMemoryInfo out-fills, the Number formatting PNSE entries,
            // RhBulkMoveWithWriteBarrier, the TracingStatusChangedEventArgs
            // ETW payload -- see MethodMap.GC.cs tail).
            case "System.Runtime.RuntimeImports":
            case "Windows.Foundation.Diagnostics.TracingStatusChangedEventArgs":
            case "System.Runtime.GCFrameRegistration":
            case "System.ComAwareWeakReference":
            case "System.Runtime.JitInfo":
            case "System.Runtime.Loader.AssemblyLoadContext":
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
            case "System.MarshalByRefObject":
            case "System.MissingMemberException":
            case "System.WeakReference`1":
                return TryMapWeakReference(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                    ?? TryMapGC(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.CLRConfig":
            case "System.Diagnostics.Assert":
            case "System.Diagnostics.Log":
            case "System.BaseConfigHandler":
            case "System.Security.SecurityContext":
            case "System.Security.SecurityManager":
            case "System.Security.SecurityRuntime": return TryMapGC(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Delegate": return TryMapDelegate(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // excext (fold-skip family): the NON-generic System.Action (the
            // parameterless BCL delegate, no `1 arity marker) fell to the P26
            // phantom guard -> the whole Run() body fold died. Its Invoke
            // resolves to the delegate-invoke fold marker (NOT an "il2cpp::"
            // name -- IsRuntimeDirect must not short-circuit; the emitter's
            // DelegateInvokeExpr multicast ABIs own the actual call).
            // ReturnsValue follows the signature (Action.Invoke -> void).
            case "System.Action":
                return name == "Invoke" ? I("System.Delegate_Invoke", 0, ret != 0x01, false, true) : null;
            case "System.Linq.Expressions.Expression":
            case "System.Linq.Expressions.LambdaExpression": return TryMapExprTree(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Array": return TryMapArray(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, arrayParamTypes)
                ?? TryMapArraySort(owner, name, ps, count, genArgs, genArgNames, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.DateTime": return TryMapDateTime(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapTimeExt2(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapDateTimeExtra(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.DateTimeOffset": return TryMapDtoffset(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, dtoParamTypes);
            case "System.TimeSpan": return TryMapTimeSpan(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapTimeExt2(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapDateTimeExtra(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Threading.Thread":
            // netcore21 audit surface: the Internal.Runtime.Augments
            // RuntimeThread mirrors the Thread instance icalls 1:1 (the same
            // threadobj runtime entries serve both owners).
            case "Internal.Runtime.Augments.RuntimeThread":
            case "System.Threading.SynchronizationContext":
            case "System.Threading.HostExecutionContextManager": return TryMapThread(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // nb4 GC-SPOT: System.Threading.Volatile -- the static fence
            // read/write class (Read/Write arms live in TryMapThread,
            // MethodMap.Thread.cs). Dispatching the owner here keeps the
            // Volatile surface in the same file as Thread.VolatileRead/Write.
            case "System.Threading.Volatile": return TryMapThread(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Threading.Semaphore":
            case "System.Threading.Mutex":
            case "System.Threading.EventWaitHandle":
            case "System.Threading.AutoResetEvent":
            case "System.Threading.ManualResetEvent": return TryMapWaitHandle(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // nb4 SYNC-LITE: the lightweight sync objects (SemaphoreSlim /
            // CountdownEvent / SpinLock / Barrier) -- the il2cpp_synclite.h
            // domain (MethodMap.SyncLite.cs).
            case "System.Threading.SemaphoreSlim":
            case "System.Threading.CountdownEvent":
            case "System.Threading.Barrier":
            case "System.Threading.SpinLock": return TryMapSyncLite(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // nb5 RWLS-LOCK: ReaderWriterLockSlim (il2cpp_rwls.h /
            // MethodMap.Rwls.cs) -- the NoRecursion minimum state machine.
            case "System.Threading.ReaderWriterLockSlim": return TryMapRwls(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
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
            case "System.Exception":
            case "System.SystemException":
            // excmsg: the Argument* exception family resolves through the SAME
            // exception domain -- get_Message / get_ParamName split on the
            // receiver shape (catch carrier vs constructed instance) and the
            // ctors map by (owner, parameter types).
            case "System.ArgumentException":
            case "System.ArgumentNullException":
            case "System.ArgumentOutOfRangeException":
                return TryMapException(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type,
                    MethodSignatureCoder.Decode(md, sigHandle).ParamTypes);
            // excapi1 (2026-10-24): System.Exception.Data is exposed as an
            // IDictionary; the runtime models the store as a Hashtable object,
            // so the interface's Count / this[key] get/set ride the Hashtable
            // icalls over that object (the receiver is the dictionary object).
            case "System.Collections.IDictionary":
                return TryMapExceptionData(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // === 34-domain parallel-wiring skeleton (2026-08-03) ===
            // One case line per MethodMap.<Domain>.cs skeleton file. Agents fill
            // only their own file's TryMap body -- DO NOT edit this region.
            case "System.Text.RegularExpressions.Regex": return TryMapRegex(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // nb5 OBJ-1: the Regex.Match object model -- Match / Group /
            // GroupCollection property + indexer icalls (MethodMap.RegexObj.cs,
            // regex_obj.h). The receiver-typed fold routes here once a
            // Regex.Match result is tracked as System.Text.RegularExpressions.Match.
            case "System.Text.RegularExpressions.Match":
            case "System.Text.RegularExpressions.Group":
            case "System.Text.RegularExpressions.Capture":
            case "System.Text.RegularExpressions.GroupCollection":
            // nb6 OBJ-2: the Regex.Matches result -- MatchCollection Count /
            // this[int] / GetEnumerator (MethodMap.RegexObj.cs).
            case "System.Text.RegularExpressions.MatchCollection":
                return TryMapRegexObj(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Threading.Tasks.Task`1": return TryMapTask(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, genArgNames, ret, instName);
            case "System.Threading.Tasks.Task": return TryMapTask(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, genArgNames, ret, instName);
            // batch 156c (asynccase): the async state-machine surface --
            // AsyncTaskMethodBuilder`1<T> (Create/Start/SetResult/SetException/
            // SetStateMachine/get_Task/AwaitUnsafeOnCompleted) and TaskAwaiter`1<T>
            // (get_IsCompleted/GetResult/OnCompleted/UnsafeOnCompleted).
            case "System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1":
            // batch diffexc001 (2026-10-05): the NON-generic
            // AsyncTaskMethodBuilder (the value type behind `async Task`
            // builders) rides the same opaque builder-object carrier as the
            // generic `1<T> -- its value-type locals lower to Il2CppObject*
            // (CodeGenerator v:... branch) and Create/get_Task/SetStateMachine
            // forward to the same task.cpp icalls.
            case "System.Runtime.CompilerServices.AsyncTaskMethodBuilder": return TryMapTask(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, genArgNames, ret, instName);
            case "System.Runtime.CompilerServices.TaskAwaiter`1": return TryMapTask(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, genArgNames, ret, instName);
            case "System.Collections.Generic.List`1": return TryMapList(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, instName);
            // P25 (listp25): the List<int>.Enumerator STRUCT is address-taken
            // by the C# compiler (ldloca), so foreach over List<int> calls
            // MoveNext/get_Current DIRECTLY on the concrete nested type
            // ("System.Collections.Generic.List`1+Enumerator" -- a nested-type
            // owner from the TypeSpec parent; the '+' is the ECMA nesting
            // separator). This owner case routes to the enumerator icalls
            // (TryMapCollectionInterface's List`1.Enumerator arm -- the
            // generic IEnumerator surface shares the same runtime).
            // IEnumerable<T>.GetEnumerator (via the interface owner) also
            // dispatches here through the receiver hint.
            case "System.Collections.Generic.List`1+Enumerator": return TryMapCollectionInterface(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // P25c (yieldprobe/enprobe): the GENERIC iterator interfaces. A
            // foreach over a user-enumerable / yield-iterator emits callvirts
            // whose declaring owner is this interface (the compiler never
            // routes a state machine's members through a translated TypeDef --
            // the nested compiler-generated type has none). The interface has
            // no mapping of its own, so the member lowers to the runtime
            // interface dispatch (TryMapEnumIface); instName carries the
            // generic INSTANTIATION name and selects the get_Current carrier.
            case "System.Collections.Generic.IEnumerable`1":
            case "System.Collections.Generic.IEnumerator`1":
                // P3-extra: the SortedList<string,int>.Values view enumerator
                // (tracked "System.Collections.SortedList.Enumerator") pre-
                // checks here; unknown receivers keep the interface dispatch.
                return TryMapSortedListSiView(owner, name, count, receiverType)
                    ?? TryMapEnumIface(owner, name, count, instName);
            case "System.Collections.Generic.Dictionary`2": return TryMapDictionary(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, instName, keyTypeName, valueTypeName);
            // P25d (dictp25): the Dictionary<string,int> enumeration struct
            // owners (Dictionary`2+Enumerator / +KeyCollection+Enumerator /
            // +ValueCollection+Enumerator and the KeyValuePair`2 struct) -- the
            // compiler calls their members directly on the concrete nested
            // types (lsloca address receivers); the KeyCollection / ValueCollection
            // class owners (views over the Keys/Values snapshot arrays) and the
            // GENERIC interface owners (IDictionary`2 / ICollection`1 -- the
            // receiver hint routes to the dictsi wrapper / snapshot arrays).
            case "System.Collections.Generic.Dictionary`2+Enumerator":
            case "System.Collections.Generic.KeyValuePair`2":
            case "System.Collections.Generic.Dictionary`2+KeyCollection+Enumerator":
            case "System.Collections.Generic.Dictionary`2+ValueCollection+Enumerator":
            // sorte1: the SortedDictionary<string,int> / SortedList<string,int>
            // foreach pair-enumerator reuses the SHARED dictsi pair-enumerator
            // object (icall_sortedlist_si_get_enumerator), so its Enumerator
            // struct members resolve through the same dictsi arms.
            case "System.Collections.Generic.SortedDictionary`2+Enumerator":
            case "System.Collections.Generic.SortedList`2+Enumerator":
                return TryMapDictEnumeration(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Collections.Generic.Dictionary`2+KeyCollection":
            case "System.Collections.Generic.Dictionary`2+ValueCollection":
                return TryMapDictCollection(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, instName);
            case "System.Collections.Generic.IDictionary`2":
            case "System.Collections.Generic.ICollection`1":
                // ilistException: ICollection`1 with a tracked List receiver
                // (Count on an interface-typed list) routes to the List
                // interface arms first; dict/snapshot receivers fall through
                // to the dict arms unchanged. P3-extra: the SortedList`2
                // Values view (tracked pseudo-type) pre-checks to the live
                // get_Count before both.
                return TryMapSortedListSiView(owner, name, count, receiverType)
                    ?? TryMapListInterface(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                    ?? TryMapDictInterface(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Collections.Generic.IList`1":
                // ilistException: `ilist[i]` on an interface-typed List --
                // the declaring owner is the interface, so the concrete
                // List`1 arms never see it. Receiver-gated like ICollection`1.
                // P3-extra: the SortedList`2 Values indexer pre-checks to the
                // live by-index icall.
                // G4 residual: IList`1.get_Item on the Keys String[] snapshot
                // (the tracked P3 get_Keys receiver) reads the array slot.
                return TryMapSortedListSiView(owner, name, count, receiverType)
                    ?? TryMapSnapshotArrayInterface(owner, name, count, receiverType)
                    ?? TryMapListInterface(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // hset-b23 (D-HSET-1): HashSet<string> -- the string AOT expansion
            // (MethodMap.HashSet.cs, runtime hashset.h -- a set-of-unique-
            // members wrapper over the dictsi domain). GAPS.md recorded the
            // owner-case gap: the newobj fallback emitted
            // &System_Collections_Generic_HashSet_1_string_class + generated-
            // name member calls (undefined symbols). Now the members map to
            // icall_hashset_string_*.
            case "System.Collections.Generic.HashSet`1": return TryMapHashSet(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, instName, MethodSignatureCoder.Decode(md, sigHandle).ParamTypes);
            // G4: the HashSet<int> enumerator STRUCT members (the tracked
            // pseudo-type routes them; the runtime object is the dictsi
            // keycollection wrapper over the boxed-int snapshot).
            case "System.Collections.Generic.HashSet`1+Enumerator":
                return TryMapHashSetEnumerator(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // G4 B2 (b2b8): the generic Queue`1<int> / Stack`1<string> struct
            // enumerators (owner-direct nested-type calls; the finally Dispose
            // arrives through the IDisposable arm below with the tracked
            // "...Enumerator_int/_string" receiver hint).
            case "System.Collections.Generic.Queue`1+Enumerator":
                return TryMapCollectionInterface(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Collections.Generic.Stack`1+Enumerator":
                return TryMapCollectionInterface(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Diagnostics.Stopwatch": return TryMapStopwatch(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Threading.Interlocked": return TryMapInterlocked(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.IntPtr": return TryMapIntPtr(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, ret);
            case "System.UIntPtr": return TryMapUIntPtr(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, ret);
            case "System.Collections.Queue": return TryMapQueue(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapStackQueue(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Collections.Stack": return TryMapStack(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapStackQueue(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // G4: the generic Queue`1<int> / Stack`1<string> expansions ride
            // the same runtime objects through the receiver-aware arms.
            case "System.Collections.Generic.Queue`1": return TryMapQueue(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, instName);
            case "System.Collections.Generic.Stack`1": return TryMapStack(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, instName);
            case "System.Collections.Generic.SortedSet`1": return TryMapSortedSet(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, instName, MethodSignatureCoder.Decode(md, sigHandle).ParamTypes);
            case "System.Collections.Generic.SortedSet`1+Enumerator":
                return TryMapSortedSetEnumerator(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
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
                    // P25b: List<int> enumerator Dispose -- the foreach over
                    // List<int> lowers to the same try/finally isinst shape;
                    // route to the List enumerator dispose icall when the
                    // receiver hint is the List enumerator pseudo-type. The
                    // receiver is the address-taken struct ((&loc)), so the
                    // icall's Il2CppObject* parameter needs the deref:r cast
                    // (the same shape the MoveNext/get_Current arms use).
                    if (receiverType == "System.Collections.Generic.List`1.Enumerator")
                        return I("il2cpp::il2cpp_list_i4_enumerator_dispose", 0, false, false, true, new[] { "deref:r" });
                    // listrefdeep: the reference-element expansions track
                    // "...List`1.Enumerator_<element>" (ListEnumeratorTrackedType)
                    // -- route to the listobj enumerator dispose.
                    if (receiverType != null && receiverType.StartsWith("System.Collections.Generic.List`1.Enumerator_", StringComparison.Ordinal))
                        return I("il2cpp::il2cpp_list_obj_enumerator_dispose", 0, false, false, true, new[] { "deref:r" });
                    // G4 B2 (b2b8): the generic Queue<int> / Stack<string>
                    // struct enumerators (the GetEnumerator ReturnTypeName
                    // tokens). The runtime Dispose is a no-op, so the shared
                    // null-safe il2cpp_ienum_dispose is the mapping (the
                    // containers have no reflected Dispose slot).
                    // W1: the reference-element expansions track
                    // "...Enumerator_<elem>" (QueueEnumeratorTrackedType /
                    // StackEnumeratorTrackedType), so the gate is a PREFIX match
                    // (the List`1.Enumerator_<elem> precedent at :1191).
                    if (receiverType != null
                        && (receiverType.StartsWith("System.Collections.Generic.Queue`1.Enumerator_", StringComparison.Ordinal)
                            || receiverType.StartsWith("System.Collections.Generic.Stack`1.Enumerator_", StringComparison.Ordinal)))
                        return I("il2cpp::il2cpp_ienum_dispose", 0, false, false, true, new[] { "deref:r" });
                    // disprobe: the NON-GENERIC snapshot enumerator tokens
                    // (System.Collections.Stack.Enumerator /
                    // System.Collections.Queue.Enumerator -- the C42
                    // GetEnumerator ReturnTypeName). The struct enumerators
                    // do NOT implement IDisposable (net48: an explicit
                    // ((IDisposable)e).Dispose() throws InvalidCastException
                    // at the castclass -- the thrower owns the throw), so a
                    // Dispose call past a successful cast is unreachable;
                    // the shared null-safe no-op keeps the dead call
                    // compilable (plain ref receiver, the P26 arm shape).
                    var stackQueueEnumLike = receiverType ?? arg0Type;
                    if (stackQueueEnumLike is "System.Collections.Stack.Enumerator"
                            or "System.Collections.Queue.Enumerator")
                        return I("il2cpp::il2cpp_ienum_dispose", 0, false, false, true);
                    // P25d (dictp25): the Dictionary<string,int> enumerator
                    // structs implement IDisposable EXPLICITLY, so the foreach
                    // finally's `constrained..callvirt IDisposable::Dispose`
                    // arrives with this owner. Route the tracked enumerator
                    // receivers to the (no-op) dict enumerator dispose.
                    // P3-extra: the SortedList`2 Values enumerator (tracked
                    // "System.Collections.SortedList.Enumerator") maps to the
                    // core (no-op) snapshot enumerator dispose.
                    var dictEnumLike = receiverType ?? arg0Type;
                    // G4 residual (g4sorteddict): the SortedDictionary
                    // KeyCollection enumerator (tracked pseudo-type, see
                    // TryMapSortedDictionaryKeyCollection) is a struct-local
                    // address receiver -- deref:r (the P25b List<int>.Enumerator
                    // shape) lands the snapshot enumerator object.
                    if (receiverType is "System.Collections.Generic.SortedDictionary`2+KeyCollection.Enumerator"
                            || arg0Type is "System.Collections.Generic.SortedDictionary`2+KeyCollection.Enumerator")
                        return I("il2cpp::runtime::il2cpp_sortedlist_enumerator_dispose", 0, false, false, true, new[] { "deref:r" });
                    // sorte1: the SortedList/SortedDictionary<string,int>
                    // Values shared snapshot enumerator -- the receiver is the
                    // ldloca'd address (deref:r, the KeyCollection enumerator
                    // shape); the core dispose is a no-op.
                    if (receiverType is "System.Collections.SortedList.Enumerator"
                            || arg0Type is "System.Collections.SortedList.Enumerator")
                        return I("il2cpp::runtime::il2cpp_sortedlist_enumerator_dispose", 0, false, false, true, new[] { "deref:r" });
                    if (dictEnumLike is "System.Collections.Generic.Dictionary`2.Enumerator"
                            or "System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator"
                            or "System.Collections.Generic.Dictionary`2.ValueCollection.Enumerator"
                            or "System.Collections.Generic.Dictionary`2.ValueCollection.Enumerator_string"
                            or "System.Collections.Generic.HashSet`1.Enumerator_int"
                            or "System.Collections.Generic.HashSet`1.Enumerator_string")
                        return I("il2cpp::il2cpp_dict_si_enumerator_dispose", 0, false, false, true, new[] { "deref:r" });
                    // Phase-1 dictgen: the int_string snapshot enumerator
                    // (tracked "...Dictionary`2.Enumerator_int_string") hits
                    // the same foreach-finally route -- same no-op dispose,
                    // own family, same address-receiver deref.
                    if (dictEnumLike is "System.Collections.Generic.Dictionary`2.Enumerator_int_string")
                        return I("il2cpp::il2cpp_dict_gen_enumerator_dispose", 0, false, false, true, new[] { "deref:r" });
                    // T2 W2/W3: the SortedSet/HashSet long/double/uint/ref struct
                    // enumerator tokens ride the same dictsi keycollection wrapper
                    // object -- the foreach-finally Dispose is the same no-op
                    // with the deref:r cast (the G4 int precedent above).
                    if (dictEnumLike != null
                        && (dictEnumLike.StartsWith("System.Collections.Generic.HashSet`1.Enumerator_", StringComparison.Ordinal)
                            || dictEnumLike.StartsWith("System.Collections.Generic.SortedSet`1.Enumerator_", StringComparison.Ordinal)))
                        return I("il2cpp::il2cpp_dict_si_enumerator_dispose", 0, false, false, true, new[] { "deref:r" });
                    // P26 (yieldprobe): a TRACKED receiver that is a USER enumerator OR the
                    // GENERIC-INTERFACE shuttle -- the foreach-finally isinst
                    // shape over a user YIELD state machine arrives with the
                    // receiver typed as the OPEN `IEnumerator`1` interface
                    // (not the concrete state-machine type, which the fold
                    // never track-exposes). Pre-P26 this silently fell to the
                    // phantom generated-name (late cmake "use of undeclared
                    // identifier" -- the P26 guard turned it into a LOUD
                    // transpile gap). The interface Dispose IS the P25c
                    // untracked-receiver shape: il2cpp_ienum_dispose RUNS the
                    // real Dispose slot (or no-ops when the object has none).
                    // A BCL receiver the mapper cannot route still stays LOUD
                    // (the P26 guard below), preserving "real gap must shout".
var userRecvLike = receiverType ?? arg0Type;
                    if (userRecvLike == "System.Collections.IEnumerator" || userRecvLike == "System.Collections.IEnumerable"
                            || userRecvLike == "System.Collections.Generic.IEnumerator`1" || userRecvLike == "System.Collections.Generic.IEnumerable`1"
                            // BATCH4: the grouping enumerator carries the key-family
                            // marker ("...IEnumerator`1_System.Linq.IGrouping[_key_*]");
                            // the foreach-finally Dispose is the same no-op dispatch.
                            || (userRecvLike != null && userRecvLike.StartsWith(
                                "System.Collections.Generic.IEnumerator`1_System.Linq.IGrouping", StringComparison.Ordinal))
                            || (userRecvLike != null && !userRecvLike.StartsWith("System.", StringComparison.Ordinal)
                                && !userRecvLike.StartsWith("Microsoft.", StringComparison.Ordinal)))
                        return I("il2cpp::il2cpp_ienum_dispose", 0, false, false, true);
                    if (receiverType == null && arg0Type == null)
                        // P25c: the untracked-receiver IDisposable.Dispose (the
                        // foreach isinst guard over a user enumerator / yield
                        // state machine). Rerouted to the enumerator dispatch:
                        // il2cpp_ienum_dispose is null-safe AND a no-op when
                        // the receiver has no reflected Dispose slot, so the
                        // SortedList snapshot enumerator (not IDisposable, the
                        // isinst guard's null branch never calls) keeps its
                        // no-op semantics, while a user enumerator with a real
                        // Dispose/state-machine slot RUNS it.
                        return I("il2cpp::il2cpp_ienum_dispose", 0, false, false, true);
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
                // P25c (yieldprobe/enprobe): a user IEnumerator<int> receiver
                // gets no snapshot branch (TryMapCollectionInterface returns
                // null), so the NON-GENERIC interface members fall through to
                // the runtime interface dispatch too (MoveNext resets the
                // state machine; get_Current returns the boxed object).
                // NOTE: only when a receiver is actually TRACKED. A null
                // receiver is the FIRST-PASS deferral -- handing il2cpp_ienum_*
                // back then would steal the receiver-aware re-resolve that the
                // SortedList snapshot receivers rely on (the TextReader defer
                // pattern): the snapshot KeyList/ValueList receiverType arrives
                // AFTER the first pass and selects il2cpp_sortedlist_snapshot_*.
                return TryMapCollectionInterface(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                    ?? (receiverType != null ? TryMapEnumIface(owner, name, count, instName) : null)
                    // excapi1: System.Exception.Data (an IDictionary) inherits
                    // Count from ICollection; its Count maps to the Hashtable
                    // icall over the dictionary object. GATED on the tracked
                    // IDictionary receiver so the FIRST pass (receiverType==null)
                    // DEFERS instead of handing back an il2cpp:: CppName: the
                    // receiver-aware re-resolve then selects the SortedList
                    // Keys/Values snapshot count (TryMapCollectionInterface
                    // above) rather than this arm shadowing it with the
                    // Hashtable icall (the enumeratordeep / icollectiondeep /
                    // sldeep regression introduced by fix-io 104dcb54).
                    ?? (receiverType == "System.Collections.IDictionary"
                        ? TryMapExceptionData(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                        : null);
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
                // nb7 PROC-STARTINFO: Process.StandardOutput / StandardError
                // (the PID identity folds, tracked as "System.Diagnostics.
                // Process" / "...Process.Stderr") own ReadToEnd in the Process
                // domain (the full pipe drain) -- route there before the
                // StringIO arm (ReadToEnd is declared on TextReader, so the
                // callvirt owner lands HERE, not in StreamIO).
                if (receiverType is "System.Diagnostics.Process" or "System.Diagnostics.Process.Stderr" or "System.Diagnostics.Process.Stdin")
                    return TryMapProcess(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
                if (receiverType is "System.IO.StreamReader" or "System.IO.StreamWriter")
                    return TryMapStreamIO(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
                if (receiverType is "System.IO.StringReader" or "System.IO.StringWriter")
                    return TryMapStringIO(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, MethodSignatureCoder.Decode(md, sigHandle).ParamTypes);
                // console3 (2026-08-09): Console.Out / Console.Error stream
                // receivers -- get_Out/get_Error (MethodMap.Console.cs) return
                // the console stream objects tracked under the distinct
                // pseudo-types "System.Console.OutStream" / "System.Console.ErrorStream".
                // A TextWriter member call on such a receiver routes to the
                // console-stream icalls (TryMapConsoleStream, MethodMap.Console.cs)
                // -- the ONLY TextWriter surface the Console domain owns.
                // StringIO/StreamIO receivers keep the mappings above.
                if (receiverType is "System.Console.OutStream" or "System.Console.ErrorStream")
                    return TryMapConsoleStream(owner, name, ps, count, receiverType);
                // misc3 Console.In: the stdin TextReader object (get_In tracks
                // it as "System.Console.InReader"). Read/Peek/ReadLine/
                // ReadToEnd route to the stdin icalls; the receiver is the
                // process-stdin singleton (the runtime ignores it).
                if (receiverType == "System.Console.InReader")
                    return TryMapConsoleStream(owner, name, ps, count, receiverType);
                if (owner is "System.IO.TextReader" or "System.IO.TextWriter" && receiverType == null)
                    return null; // base owner, first pass -- defer to the receiver-aware re-resolve
                return TryMapStringIO(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, MethodSignatureCoder.Decode(md, sigHandle).ParamTypes);            case "System.IO.BinaryReader":
            case "System.IO.BinaryWriter": return TryMapBinaryIO(md, sigHandle, owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Text.Encoding":
            case "System.Text.UTF8Encoding":
            case "System.Text.ASCIIEncoding":
            case "System.Text.UnicodeEncoding": return TryMapEncoding(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Guid": return TryMapGuid(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Decimal": return TryMapFormat(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapDecimal(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, ret);
            case "System.Numerics.BigInteger": return TryMapBigInteger(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, ret);
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
            // REFL3 (item 1): the generic CustomAttributeExtensions extension
            // surface (GetCustomAttribute<T>/GetCustomAttributes<T>). The
            // generic instantiation is a MethodSpec; BclResolveCore receives
            // the owner + genArgNames. The arms route the receiver element and
            // the T full name to the existing _single/_custom_ex_named
            // attribute registry (attribute.cpp).
            case "System.Reflection.CustomAttributeExtensions": return TryMapCustomAttributeExtensions(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, genArgNames);
            // batch 162 (eventcase): System.EventArgs -- get_Empty folds to
            // null (the event-handler `e` argument; every wired handler
            // ignores it).
            case "System.EventArgs":
                if (name == "get_Empty" && count == 0)
                    return I(ResolvedMethod.NullMarker, 0, true, true, false);
                return null;
            case "System.Type": return TryMapReflect(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapType(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapTypeAssembly(md, owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapModule(md, owner, name, count)
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
            case "System.Collections.Generic.SortedList`2": return TryMapSortedList(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, instName);
            case "System.Collections.Generic.SortedDictionary`2": return TryMapSortedDictionary(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type, instName);
            // G4 residual (g4sorteddict): the SortedDictionary KeyCollection
            // enumeration pattern (nested owners; the frozen String[] Keys
            // snapshot enumerator -- see TryMapSortedDictionaryKeyCollection).
            case "System.Collections.Generic.SortedDictionary`2+KeyCollection":
            case "System.Collections.Generic.SortedDictionary`2+KeyCollection+Enumerator":
                return TryMapSortedDictionaryKeyCollection(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // sorte1: the concrete nested ValueCollection owner (the compiler's
            // pattern foreach over SortedDictionary/SortedList<string,int>.Values
            // calls it directly; see TryMapSortedListValueCollection).
            case "System.Collections.Generic.SortedList`2+ValueCollection":
            case "System.Collections.Generic.SortedDictionary`2+ValueCollection":
                return TryMapSortedListValueCollection(owner, name, count, receiverType);
            // sorte1: the concrete nested ValueCollection+Enumerator members
            // (MoveNext/get_Current/Reset on the shared enumerator object).
            case "System.Collections.Generic.SortedList`2+ValueCollection+Enumerator":
            case "System.Collections.Generic.SortedDictionary`2+ValueCollection+Enumerator":
                return TryMapSortedListValueEnumerator(owner, name, count, receiverType);
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
            // nb7 PROC-STARTINFO: the ProcessStartInfo object model
            // (MethodMap.ProcessInfo.cs, pathproc.h GLOBAL descriptor) -- the
            // ctor + the FileName/Arguments/UseShellExecute/Redirect*/CreateNoWindow
            // accessors.
            case "System.Diagnostics.ProcessStartInfo": return TryMapProcessInfo(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
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
            // xmaudit2 (codegen-decl): the XmlNodeList that SelectNodes
            // returns (the hit object[] tracked as XmlNodeList) needs its
            // Count / indexer members on the same handle-gap surface.
            case "System.Xml.XmlNodeList": return TryMapXml(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
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
            // audit section 3 b-class foldables (GetHashCode/Equals/GetObjectValue)
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
                if (name is "Sum" or "Min" or "Max" or "Average" && genArgs == null)
                    genArgs = LinqAggregateElementByte(md, sigHandle);
                return TryMapLinq(md, sigHandle, owner, name, ps, count, genArgs, genArgNames,
                    receiverType, receiverIsAddress, arg0Type, arg1Type);
            // D-LINQ-4 grouping instance surface: the REAL BCL owner of an
            // IGrouping<TKey,TElement> instance property call (gr.Key ->
            // get_Key). Routes to the same TryMapLinq grouping block; the
            // owner itself proves grouping-ness when no hint flows.
            case "System.Linq.IGrouping`2":
                return TryMapLinq(md, sigHandle, owner, name, ps, count, genArgs, genArgNames,
                    receiverType, receiverIsAddress, arg0Type, arg1Type);
            // Legacy LOUD family (MethodMap.Legacy.cs): System.Runtime.
            // Remoting.* / AppDomain / ArgIterator / ReaderWriterLock -- the
            // net48 audit section 3/section 6 LOUD rejection list (no runtime support).
            case "System.Runtime.Remoting.RemotingServices":
            case "System.Runtime.Remoting.Proxies.RealProxy":
            case "System.Runtime.Remoting.Contexts.Context":
            case "System.Runtime.Remoting.Messaging.Message":
            case "System.Runtime.Remoting.Messaging.StackBuilderSink":
            case "System.Runtime.Remoting.Channels.ChannelServices":
            case "System.Runtime.Remoting.ObjectHandle":
            case "System.AppDomain":
            case "System.AppDomainSetup":
            case "System.ArgIterator":
            case "System.Threading.ReaderWriterLock": return TryMapLegacy(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapEmit(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
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
                ?? TryMapResourceManager(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                ?? TryMapAssembly(md, owner, name, ps, count);
            // p-refl2-gapmatrix (C04/C07/C08): the AssemblyName VALUE is the
            // assembly's simple-name string -- get_Name is identity, get_FullName
            // answers the non-null net48-shaped display name.
            case "System.Reflection.AssemblyName": return TryMapAssemblyName(owner, name, ps, count);
            case "System.Reflection.RuntimeAssembly": return TryMapRuntimeAssembly(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // GN-20 legacy AppContext domain (MethodMap.AppContext.cs):
            // System.AppContext's net48 managed surface (managed methods
            // reading the no-config-empty data/switch stores) + the audited
            // System.Runtime.Versioning.CompatibilitySwitch internal calls
            // (icall-inventory-net48.md row 780 -- GetAppContextOverrides
            // InternalCall was a realGap). All no-config defaults are
            // byte-exact constants on this runtime (probe p-appcontext).
            case "System.AppContext":
            case "System.Runtime.Versioning.CompatibilitySwitch":
                return TryMapAppContext(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // ReflectionExtensions domain (MethodMap.ReflectionExtensions.cs): the
            // Microsoft.Reflection.ReflectionExtensions typecode primitive wrapper.
            case "Microsoft.Reflection.ReflectionExtensions": return TryMapReflectionExtensions(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // REFL4 (item 2): System.Reflection.IntrospectionExtensions.GetTypeInfo
            // (fold-identity -- a TypeInfo is a Type subclass, its members are the
            // Type members) + the System.Reflection.TypeInfo-only members
            // (Declared*/GetDeclared*) which fail LOUD.
            case "System.Reflection.IntrospectionExtensions": return TryMapIntrospectionExtensions(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            case "System.Reflection.TypeInfo": return TryMapIntrospectionExtensions(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // GN-20 legacy WindowsIdentity domain (MethodMap.WindowsIdentity.cs): the
            // public net48 surface of System.Security.Principal.WindowsIdentity,
            // substituted at the operand boundary by the identity.h icalls (the
            // token-handle value gap; the audited Win32.OpenThreadToken inner icall
            // is mapped in MethodMap.Emit.cs). Probe p-identity.
            case "System.Security.Principal.WindowsIdentity": return TryMapWindowsIdentity(owner, name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type);
            // IsAuthenticated / Name are VIRTUAL on the ClaimsIdentity BASE --
            // the C# callvirt tokens declare System.Security.Claims.ClaimsIdentity
            // as the owner while the tracked receiver stays a WindowsIdentity
            // (the token-handle value gap). Forward the base-owner member to the
            // WindowsIdentity map ONLY when the receiver hint identifies one (the
            // Calendar-base dispatch precedent); any other ClaimsIdentity receiver
            // keeps the un-mapped generated-name path.
            case "System.Security.Claims.ClaimsIdentity":
                return receiverType == "System.Security.Principal.WindowsIdentity"
                    ? TryMapWindowsIdentity("System.Security.Principal.WindowsIdentity", name, ps, count, genArgs, receiverType, receiverIsAddress, arg0Type, arg1Type)
                    : null;
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
        // P-G3/paircase Cmp: IComparable::CompareTo(object) on a STRING
        // receiver lowers to the instance string comparator (stringext.cpp
        // icall_string_compare_to -- clamped -1/0/+1, net48 CompareTo sign).
        // The receiver is the first arg (Il2CppObject*), the other the boxed
        // object; a box of a reference T is identity, so the value reaches
        // the icall directly.
        if (owner == "System.IComparable" && name == "CompareTo" && count == 1
            && recvType == "System.String")
            return I("il2cpp::runtime::icall_string_compare_to", 1, true, false, true);
        // G2 (D3): IComparable::CompareTo lowers RECEIVER-AGNOSTICALLY when no
        // concrete receiver hint is available -- a boxed primitive reached
        // through an interface-typed slot (`((IComparable)boxed).CompareTo(x)`,
        // the GAP-BOX-3 record: the interface cast erases the receiver type).
        // The runtime dispatches through the RAW boxed-value table first
        // (arraysort.cpp TryCompareBoxedBuiltin -- Int32/Int64/UInt64/Single/
        // Double/Char/Boolean + String), then the reflected vtable; a receiver
        // with no table entry AND no vtable slot throws NotSupportedException
        // LOUD (never a silent generated-name call to an undefined extern).
        if (owner == "System.IComparable" && name == "CompareTo" && count == 1)
            return I("il2cpp::il2cpp_icomparable_compare_to", 1, true, false, true);
        if (recvType == null) return null;
        var t = knownTypes.FirstOrDefault(tt => TypeLookup.Matches(tt, recvType)
            || (tt.GenericArity > 0 && recvType.StartsWith(tt.FullName + "_", StringComparison.Ordinal)));
        if (t == null) return null;
        if (!t.Methods.Any(m => m.Name == name && m.ParamCount == count && !m.IsStatic))
            return null;
        var cpp = owner == "System.IComparable"
            ? "il2cpp::il2cpp_icomparable_compare_to"
            : "il2cpp::il2cpp_icomparer_compare";
        return I(cpp, count, true, false, true);
    }

    //--------------------------------------------------------------------------
    // TryMapEnumIface -- P25c (yieldprobe/enprobe): lower a callvirt on the
    // BCL iterator interfaces -- the GENERIC IEnumerable<T>/IEnumerator<T> and
    // the non-generic IEnumerable/IEnumerator -- to the runtime interface
    // dispatch entries (il2cpp_ienum_*, arraysort.h). A foreach over a user-
    // written enumerable (or a yield-returning iterator, whose compiler
    // state machine is a nested compiler-generated type) emits callvirts whose
    // declaring owner is one of these interfaces; the interfaces have no
    // translated TypeDef, so the runtime resolves the receiver's member vtable
    // slot through the typeinfo registry JOINED BY THE RECEIVER'S KLASS POINTER
    // and dispatches through the receiver's vtable (receiver-agnostic -- the
    // D-ICMP-1 pattern, so no receiver hint is required).
    //
    //   owner    the declaring interface full name
    //   name     the interface member name
    //   count    the explicit parameter count (every iterator member is 0-arg)
    //   instName the generic INSTANTIATION name for a TypeSpec parent
    //            ("System.Collections.Generic.IEnumerator`1_int") -- selects the
    //            get_Current CARRIER: the runtime keeps one entry per C++ result
    //            slot (i4/i8/d/ref) and the carrier must match the T instantiation
    //            so the value lands in the right temp slot.
    //--------------------------------------------------------------------------
    private static ResolvedMethod? TryMapEnumIface(string owner, string name, int count, string? instName)
    {
        if (count != 0) return null;
        switch (owner)
        {
            case "System.Collections.Generic.IEnumerable`1":
                if (name == "GetEnumerator")
                {
                    // BATCH4: an IEnumerable<IGrouping<TKey,TElement>> carries the
                    // grouping KEY family in its instantiation name; propagate it
                    // into the tracked enumerator type so get_Current -> get_Key
                    // can dispatch on the REAL key carrier (int32/int64/16-byte
                    // struct) instead of assuming the int32 bucket key.
                    var keyMarker = LinqGroupingKeyMarker(LinqGroupingKeyTypeName(instName));
                    return I("il2cpp::il2cpp_ienum_get_enumerator", 0, true, true, true,
                        returnTypeName: "System.Collections.Generic.IEnumerator`1_System.Linq.IGrouping" + keyMarker);
                }
                return null;
            case "System.Collections.Generic.IEnumerator`1":
                switch (name)
                {
                    case "get_Current":
                        // The generic IEnumerator<T>.get_Current carrier from
                        // the T instantiation (the Task instName pattern).
                        if (instName?.Contains("_valuetype", StringComparison.Ordinal) == true)
                            throw new NotSupportedException(
                                $"IEnumerator<T>.get_Current for VALUETYPE T (instName '{instName}') is not built -- " +
                                "the 16-byte struct carriers are out of scope; only the primitive/ref carriers exist");
                        // nb9 fix P0-3: a GROUPING element is ALWAYS a managed
                        // reference (the bucket object), whatever TElement is.
                        // This arm must run BEFORE the primitive-name probes:
                        // for IEnumerator<IGrouping<int,long>> the instantiation
                        // name carries the element family too ("..._long"), so
                        // the old order picked the int64 carrier and emitted
                        // get_key(int64) -- ill-typed C++.
                        if (instName?.Contains("IGrouping", StringComparison.Ordinal) == true)
                        {
                            // BATCH4: propagate the key-family marker carried by the
                            // IEnumerator<IGrouping<TKey,TElement>> instantiation
                            // name into the tracked grouping type so get_Key
                            // dispatches on the REAL key carrier.
                            var marker = LinqGroupingKeyMarker(LinqGroupingKeyTypeName(instName));
                            return I("il2cpp::il2cpp_ienum_get_current_ref", 0, true, true, true,
                                returnTypeName: "System.Linq.IGrouping" + marker);
                        }
                        // p0grp: a DECIMAL element carrier (IEnumerator<decimal>).
                        // It MUST come after the IGrouping arm above: the
                        // instantiation of IEnumerator<IGrouping<int,decimal>>
                        // also contains "System.Decimal". The 16-byte struct
                        // cannot ride a register return slot, so the runtime
                        // entry follows the decimal T3 result-slot contract
                        // (the slot is the FIRST C++ parameter). Every OTHER
                        // value type keeps the LOUD path below.
                        if (instName?.EndsWith("_System.Decimal", StringComparison.Ordinal) == true
                            || instName?.EndsWith("System.Decimal", StringComparison.Ordinal) == true)
                            return I("il2cpp::il2cpp_ienum_get_current_dec", 0, true, false, true,
                                resultKind: "dec", returnTypeName: "System.Decimal");
                        if (instName?.Contains("_long", StringComparison.Ordinal) == true
                            || instName?.Contains("_ulong", StringComparison.Ordinal) == true)
                            return I("il2cpp::il2cpp_ienum_get_current_i8", 0, true, false, true, resultKind: "l");
                        if (instName?.Contains("_double", StringComparison.Ordinal) == true
                            || instName?.Contains("_float", StringComparison.Ordinal) == true)
                            return I("il2cpp::il2cpp_ienum_get_current_d", 0, true, false, true, resultKind: "d");
                        if (instName?.Contains("_string", StringComparison.Ordinal) == true
                            || instName?.Contains("_object", StringComparison.Ordinal) == true
                            || instName?.Contains("_class", StringComparison.Ordinal) == true
                            || instName?.Contains("_array", StringComparison.Ordinal) == true)
                            return I("il2cpp::il2cpp_ienum_get_current_ref", 0, true, true, true);
                        // B6 (ilistException): a dotted user-class element
                        // ("IEnumerator`1_System.Exception") rides the same
                        // reference arm (the DottedRefArg kind rule).
                        if (DottedRefArg(instName) != null)
                            return I("il2cpp::il2cpp_ienum_get_current_ref", 0, true, true, true);
                        // Value-type tuple element (a LINQ Select result): the
                        // snapshot array holds BOXED tuples; get_Current copies
                        // the box payload into the caller's struct slot.
                        if (instName != null
                            && instName.StartsWith("System.Collections.Generic.IEnumerator`1_System.ValueTuple`", StringComparison.Ordinal)
                            && ValueTupleSupport.KeyKinds(instName["System.Collections.Generic.IEnumerator`1_".Length..]) is { } enumKinds)
                        {
                            var tupleName = instName["System.Collections.Generic.IEnumerator`1_".Length..];
                            return I($"il2cpp::il2cpp_ienum_get_current_tuple_t<{ValueTupleSupport.StructName(tupleName)}>",
                                0, true, false, true, Array.Empty<string>(),
                                resultKind: "vt:" + tupleName, returnTypeName: tupleName,
                                appendLiterals: new[] { "\"" + enumKinds + "\"" });
                        }
                        // int32-slot primitives (int/bool/char/sbyte/byte/
                        // short/ushort/uint) and unknown instantiations.
                        return I("il2cpp::il2cpp_ienum_get_current_i4", 0, true, false, true);
                    case "MoveNext":
                        return I("il2cpp::il2cpp_ienum_move_next", 0, true, false, true);
                    case "Reset":
                        return I("il2cpp::il2cpp_ienum_reset", 0, false, false, true);
                    case "Dispose":
                        return I("il2cpp::il2cpp_ienum_dispose", 0, false, false, true);
                }
                return null;
            case "System.Collections.IEnumerable":
                if (name == "GetEnumerator")
                    return I("il2cpp::il2cpp_ienum_get_enumerator_nongeneric", 0, true, true, true,
                        returnTypeName: "System.Collections.IEnumerator");
                return null;
            case "System.Collections.IEnumerator":
                switch (name)
                {
                    case "get_Current":
                        return I("il2cpp::il2cpp_ienum_get_current_obj", 0, true, true, true);
                    case "MoveNext":
                        return I("il2cpp::il2cpp_ienum_move_next", 0, true, false, true);
                    case "Reset":
                        return I("il2cpp::il2cpp_ienum_reset", 0, false, false, true);
                    case "Dispose":
                        return I("il2cpp::il2cpp_ienum_dispose", 0, false, false, true);
                }
                return null;
        }
        return null;
    }

    private static ResolvedMethod I(string cpp, int pc, bool rv, bool rr, bool inst,
        string[]? casts = null, string? resultKind = null, bool prependCount = false, bool prependZero = false,
        bool isConsole = false, string? returnTypeName = null, bool resultIsReceiver = false,
        bool appendZero = false, string[]? appendLiterals = null,
        string[]? tupleReturnFields = null, bool providerCulture = false, int[]? argOrder = null,
        bool reresolveWithReceiver = false)
    {
        // exc001 (Task.Run): "run:i"/"run:r" ride the FromResult value slot
        // (int32-family "" / ref "" shape) -- the invoke wrapper consumes
        // the delegate object exactly where the plain/ref value would sit.
        var refs = casts == null
            ? Enumerable.Repeat(rr, pc).ToArray() // placeholder, replaced below
            : casts.Select(c => c.Length == 0 || c == "drop" || c is "run:i" or "run:r" || c.StartsWith("fcap", StringComparison.Ordinal)).ToArray();
        return new ResolvedMethod(cpp, pc, isConsole, rv, rr, refs, inst,
            ParamTypeNames: casts, ResultKind: resultKind,
            PrependCountArg: prependCount, PrependZeroArg: prependZero,
            ReturnTypeName: returnTypeName, ResultIsReceiver: resultIsReceiver,
            AppendZeroArg: appendZero, AppendLiterals: appendLiterals,
            TupleReturnFields: tupleReturnFields, ProviderCulture: providerCulture,
            ArgOrder: argOrder, ReresolveWithReceiver: reresolveWithReceiver);
    }

    private static bool[] ParamRefFlags(string paramTypes)
    {
        if (paramTypes.Length == 0) return Array.Empty<bool>();
        return CodeGenerator.SplitTopLevel(paramTypes).Select(p => p.StartsWith("r:", StringComparison.Ordinal)).ToArray();
    }

    // WF-M2 (2026-08-07): P/Invoke param encoding -> the emitter cast code.
    // MethodSignatureCoder encodes a struct param as "V:<FullName>" (BYREF as
    // "B:V:<FullName>"); the emitter's cast switch (EmitIcallCallArgs) keys on
    // short STABLE codes. The three registered structs (il2cpp_pinvoke_struct.h
    // -- wfm2 report section 2: System.Drawing.Rectangle/Point + System.Windows.Forms.
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

    // GN-17 P0 (pinvokewide): the native C++ RETURN type of a P/Invoke method,
    // derived from the signature's return element type. Mirrors
    // CodeGenerator.PinvokeReturnTypeCpp (int64/uint64 -> int64_t/uint64_t,
    // float/double -> float/double, native int -> intptr_t/uintptr_t, fnptr ->
    // void*). null = not a full-width primitive (int32-family, struct, etc.) --
    // the emitter falls back to the ResultKind-derived carrier exactly as
    // before. This is the width the il2cpp_pinvoke_call<Ret> wrapper MUST use;
    // the old ResultKind-only mapping narrowed every non-"ptr" return to
    // int32_t (int64 4000000001 -> a truncated -294967295).
    private static string? PinvokeRetCpp(SignatureTypeCode returnType) => returnType switch
    {
        SignatureTypeCode.Int64 => "int64_t",
        SignatureTypeCode.UInt64 => "uint64_t",
        SignatureTypeCode.Single => "float",
        SignatureTypeCode.Double => "double",
        SignatureTypeCode.IntPtr => "intptr_t",
        SignatureTypeCode.UIntPtr => "uintptr_t",
        SignatureTypeCode.FunctionPointer => "void*",
        _ => null,
    };

    // Decode the signature blob -> (param count, return type byte, per-param raw
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
        // ECMA-335 II.23.1.16: strip custom modifiers (0x1F/0x20) on the
        // return type before dispatch (volatile-like modreq/IsReadOnly).
        var ret = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
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
        // G2 (bitconvcase): an SZARRAY return (0x1D, e.g. BitConverter.GetBytes
        // -> byte[]) is followed by its element byte -- not consuming it leaks the
        // element byte into ps[0] and every GetBytes overload resolves to the same
        // indistinguishable shape. Consume recursively (the element itself may carry
        // trailing data).
        else if (ret is 0x1D) _ = ReadParamByte(md, ref sig);
        var ps = new byte[count];
        for (var i = 0; i < count; i++) ps[i] = ReadParamByte(md, ref sig);
        return (count, ret, ps);
    }

    // M7 linqcase: Sum/Min/Max are CONCRETE non-generic net48 overloads
    // (Sum(this IEnumerable<int32>), Min/Max likewise -- no MethodSpec, so
    // the generic-argument array is null at dispatch). The i4 aggregate gate
    // in TryMapLinq needs the ELEMENT byte; it lives inside the first
    // parameter's GENERICINST signature. Decode the header + param count +
    // return type, then read the first param -- a GenericInst (0x15) with the
    // CLASS/VALUETYPE marker + TypeDefOrRef + arg count + one element per
    // argument. Returns the first argument's element byte (int32 for the i4
    // family), or null when the signature does not have that shape.
    //
    // REACHABILITY (B1 review): Roslyn emits a MethodSpec for every
    // generic-instantiated LINQ aggregate, so genArgs is non-null for the
    // selector forms and this decoder never sees them. What actually reaches
    // it are the non-generic (header 0x00) Sum/Min/Max/Average MemberRefs.
    // The generic-header handling below is defensive hardening for
    // definition-shaped MemberRefs; no e2e case in this tree produces that
    // shape. It is covered by tests/unit/MethodRefResolverLinqAggregateTests.cs
    // (generic header + VALUETYPE/VAR/MVAR/GENERICINST + exact-end asserts).
    private static byte[]? LinqAggregateElementByte(MetadataReader md, BlobHandle sigHandle)
    {
        var sig = md.GetBlobReader(sigHandle);
        return DecodeLinqAggregateElementByte(md, ref sig);
    }

    // Test seam for the consumption contract: the caller owns the BlobReader so
    // it can assert the exact cursor position after decoding.
    internal static byte[]? DecodeLinqAggregateElementByte(MetadataReader md, ref BlobReader sig)
    {
        var hdr = sig.ReadSignatureHeader();
        // ECMA-335 II.23.2.1: a GENERIC method-definition signature carries the
        // GenericParamCount right after the header. Defensive only (see the
        // REACHABILITY note above): Roslyn call sites carry a MethodSpec, so
        // this shape is exercised by the resolver-level unit tests, not e2e.
        // Without consuming it, count/return/first-param reads drift by one
        // byte and the GenericInst source is never seen.
        if (hdr.IsGeneric) _ = sig.ReadCompressedInteger();
        var count = sig.ReadCompressedInteger();
        if (count < 1) return null;
        // Return type: consume its whole payload with the ECMA-335 table
        // (GENERICINST/SZARRAY/VAR/MVAR/...), not only its leading byte.
        _ = ReadParamByte(md, ref sig);
        var first = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        if (first != 0x15) return null; // not a GenericInst source parameter
        var marker = sig.ReadByte();    // 0x12 CLASS / 0x11 VALUETYPE
        if (marker is not (0x12 or 0x11)) return null;
        _ = sig.ReadTypeHandle();
        var argc = sig.ReadCompressedInteger();
        if (argc < 1) return null;
        // Element: ReadParamByte consumes the trailing payload of VAR/MVAR
        // (index), GENERICINST (marker+handle+args), SZARRAY, ... and returns
        // the dispatch code -- VAR/MVAR stay the 0x13/0x1E placeholders, never
        // a concrete element family.
        var elem = ReadParamByte(md, ref sig);
        for (var i = 1; i < argc; i++) _ = ReadParamByte(md, ref sig);
        // Walk every remaining METHOD parameter too, then require the cursor to
        // sit exactly at the blob end: a surplus/short read means the decoder
        // drifted and no hint may be returned.
        for (var i = 1; i < count; i++) _ = ReadParamByte(md, ref sig);
        if (sig.RemainingBytes != 0) return null;
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
        var ret = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
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
        // ECMA-335 II.23.1.16: custom modifiers (0x1F CMOD_REQD / 0x20
        // CMOD_OPT) precede the modified type -- strip and dispatch on the
        // real byte (SZARRAY is 0x1D only).
        var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        switch (b)
        {
            case 0x10: return ReadParamByte(md, ref sig); // BYREF: element follows, ps carries it
            // PTR-SIG (2026-08-22): POINTER types -- the pointed-to element
            // follows and MUST be consumed, otherwise it leaks into the next
            // param slot and shifts every following mapping (the String
            // char*/sbyte* ctor family: (char*,int,int) misread as
            // [0F,03,08]). Returning the ELEMENT byte discriminates naturally:
            // char* -> 0x03, sbyte* -> 0x04 (no plain-char ctor shape collides).
            case 0x0F: return ReadParamByte(md, ref sig);
            case 0x11 or 0x12 or 0x40: _ = sig.ReadTypeHandle(); break;
            case 0x1D: _ = ReadParamByte(md, ref sig); break; // SZARRAY: element follows
            // GN-10 (i): multidim ARRAY (0x14) -- element type + rank +
            // sizes + lobounds must ALL be consumed, otherwise the payload
            // leaks into the next param/arg slot and shifts every following
            // mapping. Mirrors the GenericArgFullNames name table's rule.
            case 0x14: ConsumeMultidimPayload(md, ref sig); break;
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

    // GN-10 (i): consume a multidim ARRAY (0x14) type payload -- element type
    // (through the full ReadParamByte table so nested shapes recurse), rank,
    // sizes and lobounds. Shared by ReadParamByte (the genArgs producer) and
    // GenericArgFullNames (the name table) so both stay byte-parallel.
    private static void ConsumeMultidimPayload(MetadataReader md, ref BlobReader sig)
    {
        _ = ReadParamByte(md, ref sig); // element type
        _ = sig.ReadCompressedInteger(); // rank
        var numSizes = sig.ReadCompressedInteger();
        for (var i = 0; i < numSizes; i++) _ = sig.ReadCompressedInteger();
        var numLoBounds = sig.ReadCompressedInteger();
        for (var i = 0; i < numLoBounds; i++) _ = sig.ReadCompressedInteger();
    }

    // D-MAR-1 + P-D2 #3: capture the FULL TYPE NAME of every nameful generic
    // argument in a MethodSpec signature -- VALUETYPE (0x11, the Marshal struct
    // family SizeOf<Rectangle>() needs "System.Drawing.Rectangle"), CLASS (0x12)
    // and TypeHandle (0x40, custom delegate types -- the GDFFP typeId source)
    // and GenericInst (0x15, Func`N/Action`N delegate instantiations). The blob
    // is re-read from the start with the SAME consumption rules as ReadParamByte,
    // so the returned array stays parallel to the caller's genArgs byte array.
    // Returns null when NO argument is nameful (the common case -- the callers
    // avoid the extra work).
    private static string[]? GenericArgFullNames(MetadataReader md, BlobHandle sigHandle, byte[] genArgs)
    {
        var sig = md.GetBlobReader(sigHandle);
        if (sig.ReadByte() != 0x0A) return null; // not GENERICINST(0x0A)
        var argc = sig.ReadCompressedInteger();
        // B4-1: the name table is indexed against the genArgs producer's
        // array; a count drift is a broken contract, not a "no names" case.
        if (argc != genArgs.Length)
        {
            throw new NotSupportedException(
                $"GenericArgFullNames: MethodSpec arg count {argc} != genArgs.Length {genArgs.Length}");
        }
        string?[] names = new string?[argc];
        var any = false;
        for (var i = 0; i < argc; i++)
        {
            var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
            // B4-1: BYREF (0x10) / PTR (0x0F) dispatch on their ELEMENT in
            // ReadParamByte, so the consumer-visible genArgs byte is the
            // pointee's; consume with the same rule and compare against the
            // producer's byte.
            var canonical = b is 0x10 or 0x0F ? ReadParamByte(md, ref sig) : b;
            switch (b)
            {
                case 0x11 or 0x12 or 0x40:
                {
                    var n = ResolveGenericArgTypeName(md, sig.ReadTypeHandle());
                    names[i] = n;
                    any = true;
                    break;
                }
                // P-D2 #3: a GenericInst argument (GetDelegateForFunctionPointer
                // <Func`N/..>) -- named with the same canonical M3 instance name a
                // delegate newobj of that instantiation stamps (so the GDFFP
                // typeId matches a normally-created delegate's at Combine/Equals).
                case 0x15:
                {
                    names[i] = GenericInstArgName(md, ref sig);
                    any = true;
                    break;
                }
                case 0x1D or 0x1F:
                    // GN-10 (i): SZARRAY -- the element type follows and must be
                    // consumed through the SAME recursive rule as ReadParamByte
                    // (a bare ReadByte leaves a struct/array element's payload
                    // to be misread as the next argument).
                    _ = ReadParamByte(md, ref sig);
                    break;
                case 0x14:
                    // GN-10 (i): multidim ARRAY -- element + rank + sizes +
                    // lobounds (the ReadParamByte-parallel consumption).
                    ConsumeMultidimPayload(md, ref sig);
                    break;
                case 0x13 or 0x1E:
                    _ = sig.ReadCompressedInteger(); // VAR/MVAR: index follows
                    break;
            }
            // B4-1: slot-parallel contract with the genArgs producer (both
            // walk the same blob); a divergent byte means the consumer would
            // index the wrong slot.
            if (canonical != genArgs[i])
            {
                throw new NotSupportedException(
                    $"GenericArgFullNames: slot {i} decodes 0x{canonical:X2} != genArgs 0x{genArgs[i]:X2} (name table / producer drift)");
            }
        }
        // B4-1: both passes must consume the blob EXACTLY -- leftover bytes
        // mean a type's trailing payload leaked and every later slot is
        // suspect (the pre-fix silent-null behavior hid this).
        if (sig.RemainingBytes != 0)
        {
            throw new NotSupportedException(
                $"GenericArgFullNames: {sig.RemainingBytes} MethodSpec blob byte(s) unconsumed after {argc} generic argument(s)");
        }
        return any ? names.Select(n => n ?? "?").ToArray() : null;
    }

    private static string? ResolveGenericArgTypeName(MetadataReader md, EntityHandle th) =>
        th.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)th),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)th),
            HandleKind.TypeSpecification => GenericDefinitionName(md, (TypeSpecificationHandle)th),
            _ => null,
        };

    // P-D2 #3: GenericInst generic-argument name -- the canonical M3 instance
    // name "{definition name}_{arg...}" (the EXACT form StackFold's
    // GenericInstantiationName produces for a delegate newobj typeId input),
    // decoded from the MethodSpec blob position (marker + definition handle +
    // count + one byte per arg).
    private static string GenericInstArgName(MetadataReader md, ref BlobReader sig)
    {
        sig.ReadByte(); // CLASS/VALUETYPE marker byte
        var defHandle = sig.ReadTypeHandle();
        string defName = defHandle.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
            HandleKind.TypeSpecification => GenericDefinitionName(md, (TypeSpecificationHandle)defHandle),
            _ => "?",
        };
        var n = sig.ReadCompressedInteger();
        var args = new string[n];
        for (var i = 0; i < n; i++)
        {
            var ab = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
            args[i] = ab switch
            {
                // Mirrors StackFold.ReadM3Arg (the M3 generic-arg name table) --
                // kept in sync so a GDFFP<Func<..>> typeId equals a delegate
                // newobj's typeId.
                0x02 => "bool", 0x03 => "char", 0x04 => "sbyte", 0x05 => "byte",
                0x06 => "short", 0x07 => "ushort", 0x08 => "int", 0x09 => "uint",
                0x0A => "long", 0x0B => "ulong", 0x0C => "float", 0x0D => "double",
                0x0E => "string", 0x1C => "object", 0x1D => ReadM3ArrayArg(md, ref sig),
                0x14 => ReadM3MultidimArg(md, ref sig),
                0x11 or 0x12 or 0x40 => MethodSignatureCoder.ResolveM3ArgTypeName(md, ref sig),
                0x15 => GenericInstArgName(md, ref sig),
                // GN-14 (a-1): 0x13/0x1E VAR/MVAR. The compressed parameter
                // index MUST be consumed (the sibling GEN-12/GN-13 VAR arms --
                // StackFold/CodeGenerator ReadM3VarArg, MethodRefResolver
                // .ReadM3VarArg -- all consume it); the old literal "int" left
                // the index byte in the stream, misaligning the next argument
                // (the "byte 0x00 in P-D2 #3 GDFFP typeId naming" crash).
                // Outside a body pass ReadM3VarArg's hint is null -> "int".
                0x13 or 0x1E => ReadM3VarArg(ref sig),
                var other => throw new NotSupportedException($"generic argument byte 0x{other:X2} in P-D2 #3 GDFFP typeId naming"),
            };
        }
        return defName + "_" + string.Join("_", args);
    }

    //--------------------------------------------------------------------------
    // P-D2 #2: GetFunctionPointerForDelegate -> GfpS dispatch resolution.
    // The delegate argument's Invoke signature (full-width leaf-ABI C++ types,
    // the SAME mapping as the #1 multicast helper cast table) determines the
    // GfpS module (thunks + strong-root slots + dispatcher) the resolver
    // registers; the returned ResolvedMethod names the dispatcher so the
    // emitter emits `il2cpp_delegate_gfp_<hash>(d)` (void* / "ptr" result).
    //--------------------------------------------------------------------------
    private static ResolvedMethod? BuildDelegateGfpDispatch(MetadataReader md, string? delegateTypeName)
    {
        var name = DelegateGfpForPinvokeParam(md, delegateTypeName);
        if (name == null) return null;
        return I(name, 1, true, false, false, new[] { "" }, "ptr");
    }

    // P-D2 #2/#4 SHARED helper: resolve a delegate type name to its per-signature
    // GfpS dispatcher -- decodes the Invoke MethodDef signature (full-width
    // leaf-ABI C++ types), registers the GfpS module for the generated.h flush,
    // and returns the dispatcher name ("il2cpp_delegate_gfp_<hash>"). Null when
    // the name is not a resolvable delegate (absent from the input assembly
    // metadata / array / object / BCL generic types).
    internal static string? DelegateGfpForPinvokeParam(MetadataReader md, string? delegateTypeName)
    {
        if (string.IsNullOrEmpty(delegateTypeName)) return null;
        var sig = ResolveDelegateGfpSignature(md, delegateTypeName);
        if (sig == null) return null;
        var hash = SigHash.Hash(sig.Value.Ret + "(" + string.Join(",", sig.Value.Params) + ")");
        CodeGenerator.RegisterDelegateGfpModule(sig.Value.Ret, sig.Value.Params, hash);
        return $"il2cpp_delegate_gfp_{hash}";
    }

    // P-D2 #2: find the delegate TypeDef (full name match in the input
    // assembly metadata -- the SAME md.GetFullName form StackFold's tracked
    // arg0Type carries) and decode its Invoke MethodDef signature into the
    // leaf-ABI C++ types. Null when the type is absent / has no Invoke / has an
    // unsupported signature arm (the caller falls back to the conservative
    // icall_delegate_get_fn_ptr path).
    private static (string Ret, string[] Params)? ResolveDelegateGfpSignature(MetadataReader md, string? delegateTypeName)
    {
        if (string.IsNullOrEmpty(delegateTypeName)) return null;
        foreach (var th in md.TypeDefinitions)
        {
            if (md.GetFullName(th) != delegateTypeName) continue;
            // P2-HARDEN #2: the type must be a REAL delegate -- its base chain
            // reaches System.MulticastDelegate / System.Delegate. The old
            // name+Invoke heuristic misclassified a non-delegate class that
            // happens to declare an Invoke method (a GFPD generic argument),
            // and the runtime would flatten the non-delegate object's garbage
            // as invocation-list leaves. Corpus had zero such hits -- pure
            // defensive hardening (mirrors StackFold.IsDelegateType's chain).
            if (!IsDelegateSubclass(md, th)) return null;
            foreach (var mh in md.GetTypeDefinition(th).GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                if (md.GetString(m.Name) != "Invoke") continue;
                var (_, returnType, paramTypes, _) = MethodSignatureCoder.Decode(md, m.Signature);
                var retCpp = DelegateGfpReturnCpp(returnType);
                if (retCpp == null) return null;
                var parts = paramTypes.Length == 0
                    ? Array.Empty<string>()
                    : CodeGenerator.SplitTopLevel(paramTypes).ToArray();
                var paramCpp = new string[parts.Length];
                for (var i = 0; i < parts.Length; i++)
                {
                    var pc = DelegateGfpParamCpp(parts[i]);
                    if (pc == null) return null;
                    paramCpp[i] = pc;
                }
                return (retCpp, paramCpp);
            }
            return null; // the type is found but has no Invoke method
        }
        return null;
    }

    // P2-HARDEN #2: whether a TypeDef's base-class chain reaches
    // System.MulticastDelegate / System.Delegate (the metadata-only mirror of
    // StackFold.IsDelegateType's model walk -- the resolver has no TypeDef
    // list here). In-module user bases (HandleKind.TypeDefinition) are walked;
    // an out-of-module base (BCL / cross-assembly TypeReference) is terminal:
    // a real custom delegate's DIRECT base is the MulticastDelegate TypeRef,
    // so a terminal non-MulticastDelegate name (e.g. System.Object) proves it
    // is not a delegate.
    private static bool IsDelegateSubclass(MetadataReader md, TypeDefinitionHandle handle)
    {
        for (var cur = handle; !cur.IsNil;)
        {
            var baseHandle = md.GetTypeDefinition(cur).BaseType;
            if (baseHandle.IsNil) return false;
            string? name;
            TypeDefinitionHandle? next = null;
            if (baseHandle.Kind == HandleKind.TypeReference)
                name = md.GetFullName((TypeReferenceHandle)baseHandle);
            else if (baseHandle.Kind == HandleKind.TypeDefinition)
            {
                name = md.GetFullName((TypeDefinitionHandle)baseHandle);
                next = (TypeDefinitionHandle)baseHandle;
            }
            else
                return false; // TypeSpec base (generic) -- never a delegate
            if (name == "System.MulticastDelegate" || name == "System.Delegate") return true;
            if (next == null) return false;
            cur = next.Value;
        }
        return false;
    }

    // Return-type -> leaf-ABI C++ (ReturnTypeCpp-equivalent; float RETURN is
    // float -- the #1 asymmetry). Null = unsupported (value type / fnptr /
    // byref / ptr / typedref / generic params).
    private static string? DelegateGfpReturnCpp(SignatureTypeCode code) => code switch
    {
        SignatureTypeCode.Void => "void",
        SignatureTypeCode.Boolean or SignatureTypeCode.Char or SignatureTypeCode.SByte or
        SignatureTypeCode.Byte or SignatureTypeCode.Int16 or SignatureTypeCode.UInt16 or
        SignatureTypeCode.Int32 or SignatureTypeCode.UInt32 => "int32_t",
        SignatureTypeCode.Int64 or SignatureTypeCode.UInt64 => "int64_t",
        SignatureTypeCode.Single => "float",
        SignatureTypeCode.Double => "double",
        SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr => "intptr_t",
        SignatureTypeCode.String or SignatureTypeCode.Object or SignatureTypeCode.SZArray or
        SignatureTypeCode.Array or SignatureTypeCode.GenericTypeInstance or SignatureTypeCode.TypeHandle => "Il2CppObject*",
        (SignatureTypeCode)0x12 => "Il2CppObject*", // CLASS return (raw byte convention)
        _ => null,
    };

    // Parameter part -> leaf-ABI C++ (ParamTypesCpp-equivalent; float PARAM is
    // double -- the #1 asymmetry). Null = unsupported (V:/F:/P:/B:/T:).
    private static string? DelegateGfpParamCpp(string part) => part switch
    {
        "i" or "b" or "c" or "i1" or "u1" or "i2" or "u2" or "tr" => "int32_t",
        "l" or "q" => "int64_t",
        "f" or "d" => "double",
        "n" => "intptr_t",
        var r when r.StartsWith("r:", StringComparison.Ordinal) => "Il2CppObject*",
        _ => null,
    };

    // Whether the return type is a managed reference. Note:
    // System.Reflection.Metadata.SignatureTypeCode has no Class member (0x12 CLASS
    // is remapped to TypeHandle(0x40)), so the decision is made on the raw byte:
    //   0x0E STRING / 0x12 CLASS / 0x1C OBJECT / 0x40 TypeHandle (nested class/ref)
    //   / 0x15 GENERICINST (delegate/List`N -- always a reference object in the
    //   reachable surface) -> ref;
    //   everything else (0x08 I4 etc.) -> value.
    // (0x15 matches ReturnTypeCpp's GenericTypeInstance arm in CodeGenerator.cs
    // -- delecb2 residual: MakeAdder returned Func<int,int> but IsRefType missed
    // 0x15, so the result temp/local degraded to int32_t.)
    private static bool IsRefType(SignatureTypeCode code)
    {
        var b = (byte)code;
        return b is 0x0E or 0x12 or 0x1C or 0x40 or 0x15 or 0x1D or 0x1F;
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

    // TypeSpec -> generic DEFINITION name ("Box`1<int>" -> "Box`1" -- the method
    // hash input uses the definition name).
    private static string GenericDefinitionName(MetadataReader md, TypeSpecificationHandle handle)
    {
        var spec = md.GetTypeSpecification(handle);
        var sig = md.GetBlobReader(spec.Signature);
        var header = sig.ReadByte();
        if (header != 0x15)
        {
            // G1 (C1): a multidim ARRAY (0x14) TypeSpec as the MemberRef OWNER
            // (`int32_t[0...,0...]::Get(int32,int32)` -- the multidim indexer
            // lower: Get/Set are runtime-synthesized array methods whose owner
            // is the multidim array type). The token is element- and
            // rank-aware "[<elem><rank>]" -- the SAME token the four-place
            // generic-arg rule emits, so the multidim owner's method hash
            // binds to the multidim icall the MethodMap emits.
            if (header == 0x14)
                return ReadM3MultidimArg(md, ref sig);
            throw new NotSupportedException($"TypeSpec signature byte 0x{header:X2} not GenericInst(0x15) in M3 (owner-resolution context)");
        }
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

    private static string cppPrefixForTuple(MetadataReader md, EntityHandle parent, string ownerDef)
    {
        if (parent.Kind == HandleKind.TypeSpecification)
        {
            try { return GenericInstantiationName(md, (TypeSpecificationHandle)parent); }
            catch { return ownerDef; }
        }
        return ownerDef;
    }

    // Whether the SINGLE parameter of a ValueTuple`N::Equals member reference
    // is the same instantiation (the strongly-typed IEquatable overload) --
    // the object overload keeps the ValueType default path.
    private static bool SignatureArgIsSameTuple(MetadataReader md, BlobHandle sigHandle, string tupleInstanceName)
    {
        try
        {
            var sig = md.GetBlobReader(sigHandle);
            sig.ReadSignatureHeader();
            if (sig.ReadCompressedInteger() != 1) return false;
            var retByte = sig.ReadByte();
            if (retByte == 0x01) { } // void
            else if (retByte is 0x11 or 0x12) _ = sig.ReadTypeHandle();
            var pb = sig.ReadByte();
            if (pb != 0x15) return false;
            sig.ReadByte(); // CLASS/VALUETYPE marker
            var defHandle = sig.ReadTypeHandle();
            var defName = defHandle.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
                _ => null,
            };
            if (defName == null || !ValueTupleSupport.IsValueTupleName(defName)) return false;
            // Compare the ARITY only: csc emits Equals(ValueTuple`N<...>) with
            // the same instantiation, and the arity is what the helper shape
            // depends on (both sides are the receiver's own struct type).
            return ValueTupleSupport.Arity(defName) == ValueTupleSupport.Arity(tupleInstanceName);
        }
        catch { return false; }
    }

    private static bool TupleItemIsReference(MetadataReader md, TypeSpecificationHandle parent, int itemK)
    {
        try
        {
            var spec = md.GetTypeSpecification(parent);
            var sig = md.GetBlobReader(spec.Signature);
            if (sig.ReadByte() != 0x15) return false;
            _ = sig.ReadByte();
            _ = sig.ReadTypeHandle();
            var argCount = sig.ReadCompressedInteger();
            if (itemK < 1 || itemK > argCount) return itemK > argCount;
            for (var i = 1; i <= argCount; i++)
            {
                var b = sig.ReadByte();
                // Nested generic-inst arg (0x15) carries extra bytes; skip via helper.
                if (b == 0x15)
                {
                    _ = sig.ReadByte();
                    _ = sig.ReadTypeHandle();
                    var nc = sig.ReadCompressedInteger();
                    for (var j = 0; j < nc; j++) _ = sig.ReadByte();
                    if (i == itemK) return true;
                    continue;
                }
                // SZARRAY/ARRAY carry an element byte.
                if (b is 0x1D or 0x1F)
                {
                    _ = sig.ReadByte();
                    if (i == itemK) return true;
                    continue;
                }
                // CLASS/VALUETYPE carry a coded index.
                if (b is 0x11 or 0x12)
                {
                    _ = sig.ReadCompressedInteger();
                    if (i == itemK) return true;
                    continue;
                }
                if (i == itemK)
                    return b is 0x0E or 0x1C or 0x12;
            }
        }
        catch { }
        return false;
    }

    // Object::ToString / Object::GetHashCode on a Tuple/ValueTuple receiver:
    // the receiver's tracked type is the generated tuple struct, so the call
    // lowers to the struct's member helper (the object icall would cast the
    // struct value to Il2CppObject* -- uncompilable). Equals stays on the
    // Object path (the boxed-equality icall).
    private static ResolvedMethod? TryMapTupleObjectMember(string receiverType, string name, int count)
    {
        if (!ValueTupleSupport.IsValueTupleName(receiverType)) return null;
        ValueTupleSupport.Require(receiverType);
        switch (name)
        {
            case "ToString" when count == 0:
                return new ResolvedMethod(ValueTupleSupport.ToStringFn(receiverType), 0, false, true, true,
                    new[] { true }, true, ParamTypeNames: new[] { "tupref" });
            case "GetHashCode" when count == 0:
                // GetHashCode returns int32 (a VALUE, not a managed reference) --
                // ReturnsRef must be false or the result slot is declared
                // Il2CppObject* and every int32 store fails.
                return new ResolvedMethod(ValueTupleSupport.GetHashCodeFn(receiverType), 0, false, true, false,
                    new[] { true }, true, ParamTypeNames: new[] { "tupref" });
            default:
                return null;
        }
    }

    // TypeSpec -> generic INSTANTIATION name (same encoding as StackFold:
    // "Box`1<int>" -> "Box`1_int").
    // R2b P2 (closed generic clone context): while a closed instantiation's
    // method body is folded/emitted, a VAR (0x13) generic argument in an
    // inner TypeSpec denotes THAT instantiation's T -- not the metadata
    // definition's M3 default ("int"). CodeGenerator sets this to the
    // current type's InstantiationSuffix (null for definitions) around the
    // body pass. Consumers: GenericInstantiationName's 0x13 arm (below) and
    // StackFold's ReadM3Arg (field owners / newobj type names). Callers
    // outside a body pass (load time, non-generic contexts) see null and
    // keep the historical "int" encoding.
    public static string? InstantiationSuffixHint { get; set; }

    // GN-13 fix4: the per-argument components of the current clone's
    // InstantiationSuffix (see TypeDef.InstantiationSuffixParts). Set in
    // lockstep with InstantiationSuffixHint around each body pass. A VAR
    // (0x13) argument inside an open self/owner TypeSpec is resolved by INDEX
    // through InstantiationVarSuffix so an arity>=2 clone does not echo the
    // whole joined suffix once per argument (the doubling this batch fixes).
    // Null for arity-1 / definition bodies (legacy path).
    public static string[]? InstantiationSuffixHintParts { get; set; }

    // GN-13 fix4: the clone argument at generic-parameter index `idx`, or null
    // when no per-argument array is active (arity-1 / definition / non-generic
    // pass -> the caller keeps the legacy whole-suffix behavior). Single
    // implementation shared by the four 0x13 arms (StackFold / CodeGenerator /
    // MethodRefResolver x2) so all producers name the same token.
    public static string? InstantiationVarSuffix(int idx)
        => InstantiationSuffixHintParts is { Length: > 1 } parts
           && (uint)idx < (uint)parts.Length
            ? parts[idx]
            : null;

    // P-G3/paircase Cmp: resolve a VAR (0x13) TypeSpec to the CONCRETE type
    // name through the enclosing clone's suffix (InstantiationSuffixHint --
    // the R2b P2 mechanism). "string_string" -> VAR:0 = "string" -> the
    // managed full name "System.String"; null when the token is not a VAR
    // TypeSpec or no suffix is active (a definition body / non-generic pass).
    public static string? SharedCloneVarTypeName(MetadataReader md, int token)
    {
        var h = MetadataTokens.Handle(token);
        if (h.Kind != HandleKind.TypeSpecification) return null;
        var sig = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)h).Signature);
        if (sig.ReadByte() != 0x13) return null;
        var vIdx = sig.ReadCompressedInteger();
        var hint = InstantiationSuffixHint;
        if (hint == null) return null;
        // GN-13 fix4: an arity>=2 clone resolves the index through the
        // per-argument array (a component may itself contain '_'); arity-1
        // keeps the legacy single-suffix split (StackFoldTests.Ldtoken).
        var part = InstantiationSuffixHintParts is { Length: > 1 }
            ? InstantiationVarSuffix(vIdx)
            : hint.Split('_') is { } pp && vIdx < pp.Length ? pp[vIdx] : null;
        if (part == null) return null;
        return SuffixToManagedTypeName(part);
    }

    // P-G3/paircase Cmp: true when the VAR TypeSpec's concrete clone arg is a
    // REFERENCE type (System.String / a user class). The boxed value types and
    // user structs are false. Used to (a) deref a constrained callvirt receiver
    // loaded by address (ldarga.s) back to the object, and (b) turn `box !0`
    // into identity for a reference-typed T.
    public static bool SharedCloneVarTypeIsRef(MetadataReader md, int token, IReadOnlyList<TypeDef>? types)
    {
        var concrete = SharedCloneVarTypeName(md, token);
        if (concrete == null) return false;
        var t = types == null ? null : TypeLookup.Find(types, concrete);
        if (t != null) return !t.IsValueType;
        return concrete is not ("System.Int32" or "System.Int64" or "System.UInt64"
            or "System.Double" or "System.Single" or "System.Boolean" or "System.Char"
            or "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16"
            or "System.UInt32" or "System.Decimal");
    }

    // Map one clone-suffix generic argument (the M3 short encoding) to the
    // managed TYPE FULL NAME. Shared with StackFold's constrained-marker decode.
    private static string SuffixToManagedTypeName(string suffix) => suffix switch
    {
        "int" => "System.Int32",
        "uint" => "System.UInt32",
        "long" => "System.Int64",
        "ulong" => "System.UInt64",
        "double" => "System.Double",
        "float" => "System.Single",
        "bool" => "System.Boolean",
        "char" => "System.Char",
        "byte" => "System.Byte",
        "sbyte" => "System.SByte",
        "short" => "System.Int16",
        "ushort" => "System.UInt16",
        "string" => "System.String",
        "object" => "System.Object",
        "array" => "System.Array",
        // G1 (C1): an array-argument token "[<elementSuffix>]" decodes to the
        // managed ARRAY type name (recursion maps the element).
        var t when t.Length >= 2 && t[0] == '[' && t[^1] == ']'
            => SuffixToManagedTypeName(t[1..^1]) + "[]",
        _ => suffix, // user class args already carry their full name
    };

    private static string GenericInstantiationName(MetadataReader md, TypeSpecificationHandle handle)
    {
        var spec = md.GetTypeSpecification(handle);
        var sig = md.GetBlobReader(spec.Signature);
        var header = sig.ReadByte();
        if (header != 0x15)
        {
            // G1 (C1): a multidim ARRAY (0x14) TypeSpec -- the element- and
            // rank-aware token (the same four-place multidim rule).
            if (header == 0x14)
                return ReadM3MultidimArg(md, ref sig);
            throw new NotSupportedException($"TypeSpec signature byte 0x{header:X2} not GenericInst(0x15) in M3");
        }
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
            // ECMA-335 II.23.1.16: strip custom modifiers (0x1F/0x20) first.
            var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
            args[i] = ReadM3GenericArgName(md, ref sig, b);
        }
        GenericUsageAnalyzer.CollapseInstantiation(defName, args); // P-G2 shared collapse
        // GN-N5: disambiguate colliding generic-instance names (Sanitize maps
        // '.' and '_' identically -- "Box<Cases.A_B>" vs "Box<Cases.A.B").
        return defName + "_" + string.Join("_", args)
            + TypeEmitter.GenericInstanceCppNameSuffix(defName, args);
    }

    // The generic-ARGUMENT names of a TypeSpec parent (the decode
    // GenericInstantiationName performs, minus the name join) -- the
    // Dictionary`2 mapper needs the KEY type to derive a tuple-key shape.
    // null when the TypeSpec is not a GenericInst.
    private static string[]? GenericInstantiationArgs(MetadataReader md, TypeSpecificationHandle handle)
    {
        try
        {
            var spec = md.GetTypeSpecification(handle);
            var sig = md.GetBlobReader(spec.Signature);
            if (sig.ReadByte() != 0x15) return null;
            sig.ReadByte(); // CLASS/VALUETYPE marker
            _ = sig.ReadTypeHandle();
            var argCount = sig.ReadCompressedInteger();
            var args = new string[argCount];
            for (var i = 0; i < argCount; ++i)
                args[i] = ReadM3GenericArgName(md, ref sig,
                    MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte()));
            return args;
        }
        catch (NotSupportedException) { return null; }
    }

    // ONE generic-argument byte -> its M3 name token (shared by
    // GenericInstantiationName and GenericInstantiationArgs).
    private static string ReadM3GenericArgName(MetadataReader md, ref BlobReader sig, byte b)
    {
        // M6 generic-argument expansion (the same byte table as
        // StackFold.cs GenericInstantiationName / CodeGenerator.cs
        // GenericInstantiationName): dictcase hit 0x0E
        // (Dictionary<string,int>), linqcase hit 0x02 (Func<int,bool>)
        // -- before the expansion every non-int32 arg threw here and
        // the transpile died before codegen. The suffix feeds the
        // generic-instance C++ name ({def}_<arg>_...), matching
        // TypeEmitter's {TypeCppName}_int encoding.
        return b switch
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
             0x11 => MethodSignatureCoder.ResolveM3ArgTypeName(md, ref sig),
             0x12 => MethodSignatureCoder.ResolveM3ArgTypeName(md, ref sig),
            0x1C => "object",
            0x1D => ReadM3ArrayArg(md, ref sig),
            0x14 => ReadM3MultidimArg(md, ref sig),
            0x15 => ReadM3GenericArgNested(md, ref sig),
            // B2/B5 family (boxunbox/generics; MyList<T> definition
            // bodies): an OPEN TypeSpec arg (VAR 0x13 + index) denotes
            // the enclosing closed clone's T -- the R2b P2
            // InstantiationSuffixHint mechanism (the nested arm above is
            // the precedent; the 2031-2039 comment documents this arm as
            // intended). Outside a body pass the hint is null and the
            // historical "int" encoding holds.
            0x13 => ReadM3VarArg(ref sig),
            var other => throw new NotSupportedException($"generic argument byte 0x{other:X2} in M3"),
        };
    }

    // VAR (0x13) generic-argument consumption: the index byte follows.
    private static string ReadM3VarArg(ref BlobReader sig)
    {
        // GN-13 fix4: per-argument resolution when the clone carries a
        // per-argument array (arity>=2); legacy whole-suffix otherwise.
        var i = sig.ReadCompressedInteger();
        return InstantiationVarSuffix(i) ?? InstantiationSuffixHint ?? "int";
    }

    // G1 (C1): an SZARRAY (0x1D) generic argument -- element-aware
    // "[<elementSuffix>]" token (the MethodRefResolver mirror of the four-place
    // ReadM3ArrayArg rule; MethodBodyReader.SimpleGenericArgSuffix is the
    // local-kind naming authority). Simple element shapes (primitives / string
    // / object / user class / user struct) decode through the shared arg table.
    // GN-10 (ii): a COMPLEX element that is itself an ARRAY (SZARRAY 0x1D /
    // multidim 0x14) is decoded RECURSIVELY so the token matches
    // MethodBodyReader.ArrayGenericArgSuffix ("int[][]" -> "[[int]]",
    // "int[][,]" -> "[[int2]]"); a nested GenericInst (0x15) / VAR (0x13)
    // element keeps the historical "array" token (the local-kind scan's own
    // 0x15 special case agrees).
    private static string ReadM3ArrayArg(MetadataReader md, ref BlobReader sig)
    {
        var elem = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        var elemSuffix = elem switch
        {
            0x02 => "bool",
            0x03 => "char",
            0x04 => "sbyte",
            0x05 => "byte",
            0x06 => "short",
            0x07 => "ushort",
            0x08 => "int",
            0x09 => "uint",
            0x0A => "long",
            0x0B => "ulong",
            0x0C => "float",
            0x0D => "double",
            0x0E => "string",
            0x1C => "object",
            0x11 or 0x12 => MethodSignatureCoder.ResolveM3ArgTypeName(md, ref sig),
            _ => null,
        };
        if (elemSuffix != null) return "[" + elemSuffix + "]";
        // Complex element: consume the payload so the next arg stays aligned.
        switch (elem)
        {
            case 0x1D: return "[" + ReadM3ArrayArg(md, ref sig) + "]";
            case 0x14: return "[" + ReadM3MultidimArg(md, ref sig) + "]";
            case 0x15: _ = ReadM3GenericArgNested(md, ref sig); return "array";
            case 0x13: _ = ReadM3VarArg(ref sig); return "array";
            default:
                throw new NotSupportedException($"generic argument byte 0x{elem:X2} in M3 (SZARRAY element)");
        }
    }

    // G1 (C1): a multidim ARRAY (0x14) generic argument -- element- AND
    // rank-aware "[<elementSuffix><rank>]" token (the MethodRefResolver mirror
    // of the four-place rule; the SZARRAY sibling above is the precedent). The
    // blob is ELEMENT_TYPE_ARRAY: <element type> <rank:compressed> <numSizes:
    // compressed> [sizes...] <numLoBounds:compressed> [lobounds...].
    // GN-10 (ii): a COMPLEX element that is itself an ARRAY (0x1D/0x14) is
    // decoded recursively so the token matches MethodBodyReader.
    // MultidimArrayArgSuffix ("int[,][,]" -> "[[int2]2]", "int[,][]" ->
    // "[[int]2]").
    private static string ReadM3MultidimArg(MetadataReader md, ref BlobReader sig)
    {
        var elem = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        var elemSuffix = elem switch
        {
            0x02 => "bool",
            0x03 => "char",
            0x04 => "sbyte",
            0x05 => "byte",
            0x06 => "short",
            0x07 => "ushort",
            0x08 => "int",
            0x09 => "uint",
            0x0A => "long",
            0x0B => "ulong",
            0x0C => "float",
            0x0D => "double",
            0x0E => "string",
            0x1C => "object",
            0x11 or 0x12 => MethodSignatureCoder.ResolveM3ArgTypeName(md, ref sig),
            // Complex element (nested generic / SZARRAY element / multidim
            // element / VAR): consume the payload so the next arg stays aligned.
            var ce => ConsumeComplexM3ArgPayload(md, ref sig, ce),
        };
        var rank = sig.ReadCompressedInteger();
        var numSizes = sig.ReadCompressedInteger();
        for (var i = 0; i < numSizes; i++) _ = sig.ReadCompressedInteger();
        var numLoBounds = sig.ReadCompressedInteger();
        for (var i = 0; i < numLoBounds; i++) _ = sig.ReadCompressedInteger();
        return "[" + elemSuffix + rank + "]";
    }

    private static string ConsumeComplexM3ArgPayload(MetadataReader md, ref BlobReader sig, byte elem)
    {
        switch (elem)
        {
            // GN-10 (ii): an ARRAY element (SZARRAY/multidim) decodes
            // recursively to the element-aware token; nested GenericInst / VAR
            // keep the shared fallback token.
            case 0x1D: return ReadM3ArrayArg(md, ref sig);
            case 0x14: return ReadM3MultidimArg(md, ref sig);
            case 0x15: _ = ReadM3GenericArgNested(md, ref sig); return "array";
            case 0x13: _ = ReadM3VarArg(ref sig); return "int";
            default:
                throw new NotSupportedException($"generic argument byte 0x{elem:X2} in M3 (multidim ARRAY element)");
        }
    }

    // A generic-argument that is ITSELF a generic instantiation
    // (ELEMENT_TYPE_GENERICINST 0x15 -- e.g. IEnumerable`1<KeyValuePair`2
    // <string,int>>). The nested GenericInst encoding follows the same shape
    // as the outer one (defCLASS/VALUETYPE marker + TypeHandle + argCount +
    // args); encode it recursively so the outer instance name carries the
    // fully nested suffix, mirroring MetadataLoader.ReadTypeName 0x15.
    private static string ReadM3GenericArgNested(MetadataReader md, ref BlobReader sig)
    {
        _ = sig.ReadByte(); // CLASS(0x12)/VALUETYPE(0x11) marker
        EntityHandle nestedDef = sig.ReadTypeHandle();
        string nestedName = nestedDef.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)nestedDef),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)nestedDef),
            HandleKind.TypeSpecification => GenericInstantiationName(md, (TypeSpecificationHandle)nestedDef),
            _ => throw new NotSupportedException($"nested generic definition kind {nestedDef.Kind} in M3"),
        };
        var nestedCount = sig.ReadCompressedInteger();
        var nestedArgs = new string[nestedCount];
        for (var j = 0; j < nestedCount; ++j)
        {
            var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
            nestedArgs[j] = b switch
            {
                0x02 => "bool",
                0x03 => "char",
                0x04 => "sbyte",
                0x05 => "byte",
                0x06 => "short",
                0x07 => "ushort",
                0x08 => "int",
                0x09 => "uint",
                0x0A => "long",
                0x0B => "ulong",
                0x0C => "float",
                0x0D => "double",
                0x0E => "string",
                 0x11 => MethodSignatureCoder.ResolveM3ArgTypeName(md, ref sig),
                 0x12 => MethodSignatureCoder.ResolveM3ArgTypeName(md, ref sig),
                 // GN-12 diagnostic: a nested VAR (0x13) / MVAR (0x1E) -- the
                 // compressed parameter index MUST be consumed (the sibling
                 // StackFold/CodeGenerator ReadM3NestedGeneric go through
                 // ReadM3Arg which consumes it) or every later nested byte
                 // misaligns. Name mirrors StackFold.ReadM3Arg's 0x13 arm.
                 0x13 or 0x1E => ReadNestedVarArgName(ref sig),
                 0x1C => "object",
                 0x1D => ReadM3ArrayArg(md, ref sig),
                 0x14 => ReadM3MultidimArg(md, ref sig),
                 0x15 => ReadM3GenericArgNested(md, ref sig),
                var other => throw new NotSupportedException($"nested generic argument byte 0x{other:X2} in M3"),
            };
        }
        return nestedName + "_" + string.Join("_", nestedArgs);
    }

    // GN-12 diagnostic: consume a nested VAR (0x13) / MVAR (0x1E) compressed
    // parameter index and return the enclosing clone's closed suffix name.
    // GN-13 fix4: for an arity>=2 clone resolve the index through the
    // per-argument array (the 0x1E MVAR shape does not occur in a TypeSpec).
    private static string ReadNestedVarArgName(ref BlobReader sig)
    {
        var i = sig.ReadCompressedInteger();
        return InstantiationVarSuffix(i) ?? InstantiationSuffixHint ?? "int";
    }

    // Same rule set as TypeEmitter.Sanitize EXCEPT the V_ prefix strip (this
    // copy has never stripped it, and changing that would rename existing
    // symbols). The whitelist tail step IS shared: a metadata name carrying
    // any character outside [A-Za-z0-9_] (e.g. the '|' of a local function's
    // "<Add>g__Local|1_0") must never reach a C++ identifier.
    private static string Sanitize(string s)
        => TypeEmitter.SanitizeIdentChars(string.Join("_", s.Split('.', '+', '`', '<', '>', ',', '[', ']')));

    // Dotted reference-element rule (A5/B1/B6 myIList/baudit2/
    // ilistException; A1 Task<Point>): the suffix after the LAST "`1_" of a
    // generic instantiation name. A dotted full name with no arity marker
    // (e.g. "System.Exception", "Cases.Baudit2+P") names a
    // namespace-qualified type and rides the family reference domain (the
    // Queue queueDottedAlloc precedent). BCL value-type full names and
    // nested generic instantiations (backtick) are excluded and stay LOUD.
    // Returns the element suffix, or null when the rule does not apply.
    // Residual hazard: user STRUCTS also match (the 0x11/0x12 marker is
    // dropped by the M3 full-name encoding); they fail loudly at g++ with
    // a C++ type mismatch -- the same visibility as today's
    // transpile-LOUD. The pool carries no List/Task<user-struct> case
    // (verified 2026-09-19).
    internal static string? DottedRefArg(string? instTag) =>
        DottedSuffix(instTag) is string s && !IsKnownValueTypeName(s) ? s : null;

    // Raw dotted suffix (dot, no backtick) after the last "`1_", or null.
    internal static string? DottedSuffix(string? instTag)
    {
        if (instTag == null) return null;
        var li = instTag.LastIndexOf("`1_", StringComparison.Ordinal);
        if (li < 0) return null;
        var suffix = instTag[(li + 3)..];
        if (!suffix.Contains('.') || suffix.Contains('`')) return null;
        return suffix;
    }

    // Closed BCL struct full names: must never ride a reference domain
    // (no per-type box domain exists). User structs are NOT listed (open
    // set -- the documented residual hazard above).
    internal static bool IsKnownValueTypeName(string suffix) => suffix switch
    {
        "System.DateTime" => true,
        "System.Guid" => true,
        "System.Decimal" => true,
        "System.TimeSpan" => true,
        "System.DateTimeOffset" => true,
        "System.IntPtr" => true,
        "System.UIntPtr" => true,
        _ => false,
    };
}

