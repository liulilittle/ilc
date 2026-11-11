using System;
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Model;
using Il2Cpp.Transpiler.Opt;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Il2Cpp.Transpiler.Ir;

public sealed class MethodContext
{
    public int ArgCount { get; init; }
    public string[] LocNames { get; init; } = Array.Empty<string>();
    // M5 wiring: coarse IL local kinds ("i"/"l"/"d"/"r"/"v", see
    // MethodBodyInfo.LocalKinds) -- the conv-fold needs to know whether a
    // LocExpr operand is an int64/double slot (conv.i4/conv.i8/conv.r8 on a
    // local must route to the matching runtime conversion icall).
    public string[] LocalKinds { get; init; } = Array.Empty<string>();
    // Method return type (void -> ret pops no return value; leftover join stack
    // values are dead for a void method and must be discarded, else the
    // generated `return <residual>` fails to compile).
    public bool ReturnsVoid { get; init; }
    // F2 (decimal-domain): the C++ type of a VALUETYPE return (e.g.
    // il2cpp::runtime::Il2CppDecimal) -- null for every other return shape.
    // Consumed by the C4716 trailing-return emission (TrailingReturn).
    public string? StructReturnCpp { get; init; }
    // Exception regions (passthrough of MethodBodyInfo.ExceptionRegions): the
    // 'leave' fold validation needs to know the try/handler region the leave is
    // in and its natural end offset (audit H2).
    public IReadOnlyList<IlExceptionRegion>? ExceptionRegions { get; init; }
    // Full name of the type whose .cctor is being folded (null otherwise):
    // inside the .cctor, accessing the type's own static fields / instantiation
    // / static calls make the guard permanently redundant -- ensure_cctor_*
    // sets done=true BEFORE invoking the .cctor body, so the flag is always
    // true while that body runs. Guards for other types are emitted as usual
    // (the cctor dependency order is unchanged).
    public string? CctorOwnerFullName { get; init; }
    public string? MethodFullName { get; init; }
    // Tail-call optimization (recursion-audit section 6a): the generated C++ function
    // name of the current method (MethodCppName-isomorphic encoding) -- self-
    // recursion is detected when the call's Resolved.CppName equals it.
    // null (the pure-IR fold path) -> self-recursion is never recognized; no
    // transform is ever applied.
    public string? MethodCppName { get; init; }
    // Tail-call conservative gate: only STATIC methods transform (an instance
    // method's arg0 = this is a GC-root param slot; loop re-assignment would
    // break the receiver identity / frame-root semantics). Default false = no
    // transform.
    public bool IsStatic { get; init; }
    // Tail-call conservative gate: parameter type names (the comma-separated
    // items of MethodDef.ParamTypes) -- the "r:" prefix marks a managed
    // reference parameter (a GC root); any reference param -> no transform (the
    // temp relay window has no frame-root protection). Default null = no
    // reference params.
    public string[]? ParamTypeNames { get; init; }
}

public static class StackFold
{
    // p-refl2-gapmatrix (B08): the member-VALUE provenance for a Type.GetMethod /
    // Type.GetField call whose receiver is a folded typeof-chain TYPE VALUE (a
    // StringConstExpr carrying the ldtoken TYPE TOKEN). The simplified member
    // model represents a member VALUE as its name string, which loses the
    // declaring type -- net48's GetMethod on a base type returns the BASE
    // declaration (the abstract slot), not the derived override -- so the fold
    // records (declaring type token, member name, isField) for a downstream
    // member flag predicate to resolve the exact declaration. Null when the
    // call is not a GetMethod/GetField or the receiver carries no type token.
    private static (int TypeToken, string MemberName, bool IsField)? MemberProvenanceOf(
        MetadataReader? md, int token, IReadOnlyList<Expr> args)
    {
        if (md == null || args.Count < 2) return null;
        if (args[0] is not StringConstExpr recv || recv.TypeToken is not int typeToken) return null;
        if (args[1] is not StringConstExpr memberName) return null;
        var handle = MetadataTokens.Handle(token);
        if (handle.Kind != HandleKind.MemberReference) return null;
        var mr = md.GetMemberReference((MemberReferenceHandle)handle);
        if (mr.Parent.Kind != HandleKind.TypeReference) return null;
        if (md.GetFullName((TypeReferenceHandle)mr.Parent) != "System.Type") return null;
        return md.GetString(mr.Name) switch
        {
            "GetMethod" => (typeToken, memberName.Value, false),
            "GetField" => (typeToken, memberName.Value, true),
            _ => null,
        };
    }

    public static string[]? _curClosureArgs;
    // md/types optional: null = pure-IR folding (the existing test path);
    // non-null resolves call arg counts / return values + field tokens -> full
    // names.
    // accessors (optional, rule 2j property folding): the simple-property
    // accessor table produced by the AccessorAnalyzer pre-scan -- on a hit,
    // EmitCall folds directly to a field access (FieldExpr/FieldAssignStmt/
    // StaticFieldExpr/AssignStmt), producing no function call.
    public static List<Stmt> Fold(List<IlInstruction> il, MethodContext ctx,
        MetadataReader? md = null, IReadOnlyList<TypeDef>? types = null,
        IReadOnlyDictionary<int, FoldedAccessor>? accessors = null,
        PEReader? pe = null)
    {
        var stmts = new List<Stmt>();
        var stack = new Stack<Expr>();
        var temps = new List<string>();
        // Leave sites that need EXPLICIT finally expansion (see the
        // ILOpCode.Leave arm): the statement index the expansion is inserted
        // at, the leave's own IL offset, the branch target offset, and the
        // finally regions the leave exits (innermost first). Filled during the
        // fold (the finally handler bodies are only folded later in the IL
        // stream) and materialized right after the loop.
        var leaveSites = new List<(int Index, int Offset, int Target, List<IlExceptionRegion> ExitedFinallys, int BodyEnd, int TryStart, bool NaturalTarget)>();
        // Audit H8: newobj temp -> the allocated type's full name. The throw
        // site uses it to emit the REAL managed exception type (a catch of a
        // derived type must match; before the fix every throw was a fixed
        // System.Exception and derived catches never matched).
        var objTypes = new Dictionary<string, string>();
        // floatfix: seed the parameter slots' tracked managed types from the
        // method signature. A managed-typed PARAMETER (ldarg -> argN) carried
        // no objTypes entry before, so a helper method taking List<T> hid the
        // receiver type from every resolver hint (the static re-resolve below
        // reads args[0]'s tracked type -- null for a parameter) and List
        // LINQ operators inside helper methods fell to the array family with
        // the raw List object cast to Il2CppArray*. The "r:"-prefixed model
        // names (MethodSignatureCoder: CLASS/GENERICINST/SZARRAY encode the
        // managed full name) are exact metadata -- the same shapes the
        // newobj/ToList temp tracking already carries for locals. Byref
        // parameters push ADDRESSES, not values -- they stay unseeded.
        if (ctx.ParamTypeNames is { Length: > 0 })
        {
            var firstParamArg = ctx.IsStatic ? 0 : 1; // instance: arg0 = this
            for (var pi = 0; pi < ctx.ParamTypeNames.Length; pi++)
            {
                var ptn = ctx.ParamTypeNames[pi];
                if (ptn.Length == 0 || !ptn.StartsWith("r:", StringComparison.Ordinal)
                    || ptn.EndsWith("&", StringComparison.Ordinal))
                    continue;
                var argIdx = firstParamArg + pi;
                if (argIdx >= ctx.ArgCount) continue;
                objTypes[ArgName(argIdx)] = ptn[2..];
            }
        }
        // p-refl2-gapmatrix (B08): local name -> the member-VALUE provenance
        // recorded for a folded typeof-chain GetMethod/GetField call whose
        // result was stored into the local. Lets a downstream member flag
        // predicate resolve the exact declaration from the metadata (the
        // name-string member VALUE alone cannot -- the declaring type is lost).
        var memberProvenanceByLocal = new Dictionary<string, (int TypeToken, string MemberName, bool IsField)>();
        // D5: temp -> the METADATA TOKEN of the shared-clone concrete VAR/
        // MVAR return (Box<Pt>.Get's result temp -> the Cases.Pt TypeDef
        // token). Recorded alongside objTypes so a later store into a USER
        // VALUE TYPE local can build the unbox_{token} bridge.
        var sharedReturnTokens = new Dictionary<string, int>();
        // excext (m7m): newobj temp -> the BCL-exception ctor's arguments
        // (the .ctor(string) message / (string, string) paramName+message,
        // captured in original order AFTER the implicit `this`). The throw
        // site forwards them to the emitter so the managed exception carrier
        // carries the REAL message instead of the hardcoded "exception thrown".
        var exceptionMessages = new Dictionary<string, List<Expr>>();
        // excmsg: newobj temp -> the BCL-exception ctor's PARAMETER TYPES
        // (MethodSignatureCoder encoding). The Argument* family's overloads
        // share parameter BYTES (string vs Exception), so the throw site needs
        // the decoded text to tell (msg,param) from (msg,inner) -- and a single
        // string is the PARAM name on ArgumentNullException but the MESSAGE on
        // ArgumentException.
        var exceptionCtorParamTypes = new Dictionary<string, string>();
        // RF-N4: the typeof-chain IsGenericTypeDefinition decision. The
        // typeof fold resolves a closed GENERICINST (typeof(Box<int>)) and
        // the open definition (typeof(Box<>)) to the SAME full-name string
        // ("Cases.Box`1" -- the simplified Type model), so the string alone
        // cannot tell a definition from an instance. When the GetTypeFromHandle
        // fold creates the folded name string, record whether the ldtoken was
        // an OPEN generic DEFINITION (TypeDef/TypeRef with arity > 0), keyed
        // by the GetTypeFromHandle CALL's offset. The consumer fold for
        // get_IsGenericTypeDefinition (prevIns == that call) looks it up:
        // definition -> 1, closed instance / non-generic -> 0.
        var typeFromHandleGenericDef = new Dictionary<int, bool>();
        // REFL6B (item B5): Type.get_ContainsGenericParameters on the FOLDED
        // typeof chain -- net48 (p-refl6-mprops oracle): open generic
        // definition -> 1, closed instantiation / non-generic / array -> 0,
        // NESTED-in-open (typeof(Outer<>.Inner)) -> 1, nested-in-closed
        // (typeof(Outer<int>.Inner)) -> 0. This is a SUPERSET of the
        // IsGenericTypeDefinition record above: a nested type whose DECLARING
        // chain holds an open generic definition also "contains generic
        // parameters" (net48 walks the outer type's params). Recorded at the
        // SAME GetTypeFromHandle-fold site, keyed the same way.
        var typeFromHandleContainsGenParams = new Dictionary<int, bool>();
        // GN-18 P2/D1 (S3): the ldtoken TYPE token, keyed by the
        // GetTypeFromHandle CALL's offset -- the GetGenericArguments
        // consumption fold decodes the closed GENERICINST TypeSpec blob from
        // it (arity + managed argument names). Filled at the SAME fold site
        // and with the SAME key as typeFromHandleGenericDef above, so it is
        // that mechanism's natural companion, not a side channel. A receiver
        // that is not a folded typeof (obj.GetType()) is a lookup MISS.
        var typeFromHandleToken = new Dictionary<int, int>();
        // GN-18 P2/D1 (S3): the offsets of the instructions the
        // GetGenericArguments adjacent-chain fold CONSUMED as part of its own
        // segment (the ldlen / ldc.i4 + Ldelem_ref that read the folded array
        // result). The main walk removes them from this set and skips them
        // entirely: the fold already emitted the constant that replaces the
        // whole segment, so running them again would consume that constant a
        // second time -- the observed il2cpp_array_get_length(1) -> 0xC0000005
        // crash. LOCAL to this Fold invocation (per-method IL offsets), so it
        // can never leak across methods.
        var gn18SkipOffsets = new HashSet<int>();
        // impl-sb (B): the net9 span-params lowering element capture -- local
        // name -> (element index -> the stored value expr). The C# 13
        // `params ReadOnlySpan<object?>` lowering stores every argument
        // through `<PrivateImplementationDetails>::InlineArrayElementRef`
        // byrefs (`stind.ref` into element i), then materializes the span
        // with `InlineArrayAsReadOnlySpan` -- the fold records the stores
        // here so the span materialization can rebuild the object[] the
        // composite kernels consume. LOCAL to this Fold invocation.
        var spanElements = new Dictionary<string, SortedDictionary<int, Expr>>();
        // GN-18 P2/D1 (S3): the offset of the Ldelem_ref the GetGenericArguments
        // element fold CONSUMED (the last instruction of the folded
        // `GetGenericArguments(); ldc.i4.i; ldelem_ref` segment). The element
        // fold leaves a TYPE value -- a StringConstExpr holding the argument's
        // managed full name -- on the stack, and its immediate get_Name
        // consumer must truncate it to the SHORT name. The typeof-chain get_Name
        // fold recognises its folded Type value by `prevIns == GetTypeFromHandle`,
        // but here prevIns is this consumed Ldelem_ref (the fold call itself is
        // three instructions back), so the offset is recorded to let the
        // get_Name fold recognise the element-fold receiver too. LOCAL to this
        // Fold invocation (per-method IL offsets).
        var gn18TypeValueLdelems = new HashSet<int>();
        // refl2 (A): TYPE-VALUE display registry. A folded typeof chain pushes a
        // StringConstExpr whose value is the typeinfo REGISTRY identity, not the
        // net48 display name. Each producer that yields a TYPE VALUE records its
        // instruction offset here with the exact net48 (FullName, Name, assembly
        // suffix) triple, so the CONSUMER folds (get_Name / get_FullName /
        // get_AssemblyQualifiedName / GetElementType / GetGenericTypeDefinition /
        // Make*Type) can answer without re-deriving the token -- and so a chain
        // like typeof(List<int>).GetGenericTypeDefinition().Name keeps working
        // (the producer is a CALL, not the ldtoken pair).
        var typeValueDisplay = new Dictionary<int, (string Full, string Short, string AsmSuffix)>();
        // valdeep L26: temp VALUE width ("l"/"d" = 64-bit slot, else int32).
        // LocKindOf only knows "loc" locals; temps (t*) materialized from
        // calls/joins/snapshots carry their width here so the conv.u8 fold
        // can zero-extend a 32-bit source without mis-extending an int64 one
        // ((ulong)longCallResult is a bit-identity, (ulong)uintVar is not).
        var tempWidths = new Dictionary<string, string>();
        var labels = new Dictionary<int, string>();
        // joinRestore[label] = shared temp names reaching that join point
        // (bottom-to-top stack order). When a branch instruction jumps away
        // with a non-empty stack, the whole stack is spilled to these temps
        // (assigned before the branch) and restored/normalized at the join
        // label, so the "false arm 200" and "true arm 100" edges converge on
        // the same temp slots (`x > 3 ? 100 : 200` semantics).
        var joinRestore = new Dictionary<string, List<string>>();
        IlInstruction? prevIns = null; // previous instruction: whether a label has real sequential fall-in (dead flow after br/ret)

        // First pass: emit a label for every branch target. The switch (0x45)
        // jump-table targets (SwitchTargets, absolute offsets) and the default
        // arm (out-of-range/negative -> the switch's next instruction in the
        // flow) also need labels.
        // switchDefault[switchOffset] = absolute offset of the default arm
        // (for the unconditional jump at the tail of the main comparison
        // chain).
        var switchDefault = new Dictionary<int, int>();
        for (var i = 0; i < il.Count; i++)
        {
            var ins = il[i];
            if (ins.OperandKind == IlOperandKind.BranchTarget)
                labels.TryAdd((int)ins.Operand, $"L_{ins.Operand}");
            else if (ins.OpCode == ILOpCode.Switch)
            {
                foreach (var t in ins.SwitchTargets ?? Array.Empty<int>())
                    labels.TryAdd(t, $"L_{t}");
                if (i + 1 < il.Count)
                {
                    var def = il[i + 1].Offset;
                    switchDefault[ins.Offset] = def;
                    labels.TryAdd(def, $"L_{def}");
                }
            }
        }

        // P0-2 constant-local propagation pre-scan: count each local's stloc
        // occurrences and collect the address-taken (ldloca/ldloca_s) locals.
        // Propagation applies ONLY to "single-assignment and not address-taken"
        // locals (no second assignment overwriting, no byref modification
        // path), and is invalidated at merge points / exception handler starts
        // (see the locConst clear points in the main loop).
        var stlocCount = new Dictionary<string, int>();
        var addrTaken = new HashSet<string>();
        foreach (var pre in il)
        {
            switch (pre.OpCode)
            {
                case ILOpCode.Stloc_0:
                case ILOpCode.Stloc_1:
                case ILOpCode.Stloc_2:
                case ILOpCode.Stloc_3:
                case ILOpCode.Stloc:
                case ILOpCode.Stloc_s:
                {
                    var name = LocName(ctx, StlocIndex(pre.OpCode, (int)pre.Operand));
                    stlocCount.TryGetValue(name, out var c);
                    stlocCount[name] = c + 1;
                    break;
                }
                case ILOpCode.Ldloca:
                case ILOpCode.Ldloca_s:
                    addrTaken.Add(LocName(ctx, (int)pre.Operand));
                    break;
            }
        }
        // locConst[name] = known constant (single-assignment + not-address-
        // taken locals only); propagatedLocs feeds the dead-store removal.
        // The table is cleared at labels (merge points) and handler starts --
        // these points can be entered via paths that bypass the assignment.
        var locConst = new Dictionary<string, long>();
        var propagatedLocs = new HashSet<string>();
        // Filter regions: their FILTER block (not just the handler) is an entry
        // point too -- nothing falls through into it, and the exception object
        // arrives on the stack there. Treat FilterOffset as a handler start so
        // local constant propagation is reset at both entry points.
        var handlerStarts = ctx.ExceptionRegions == null
            ? new HashSet<int>()
            : ctx.ExceptionRegions
                .SelectMany(r => r.Kind == ExceptionRegionKind.Filter
                    ? new[] { r.HandlerOffset, r.FilterOffset }
                    : new[] { r.HandlerOffset })
                .ToHashSet();

        // M5 wiring: .NET CATCH handlers begin with the exception object on
        // the stack. Push a synthetic entry (ExceptionEntryExpr -- the
        // exception's SHORT type name as an interned string) ONLY when the
        // handler's FIRST instruction consumes a stack value (stloc binds the
        // catch variable; call/callvirt uses it inline -- the compiler's two
        // shapes). Handlers that never bind the variable start with a
        // pushing instruction -- no entry is pushed, the handler's stack
        // stays empty (the pre-existing M3 shape).
        var catchEntryOffsets = new HashSet<int>();
        if (ctx.ExceptionRegions != null)
        {
            foreach (var r in ctx.ExceptionRegions.Where(r => r.Kind is ExceptionRegionKind.Catch or ExceptionRegionKind.Filter))
            {
                var idx = il.FindIndex(i => i.Offset >= r.HandlerOffset && i.Offset < r.HandlerOffset + r.HandlerLength);
                if (idx >= 0 && OpcodeConsumesStack(il[idx].OpCode))
                    catchEntryOffsets.Add(r.HandlerOffset);
                // A FILTER block ALWAYS begins with the exception object on the
                // stack (ECMA-335 II.16.4 / III.3.21). C# emits this shape for
                // `catch (E e) when (...)`, and the first instruction there is
                // commonly `isinst` -- which does NOT consume a stack value, so
                // the generic "first instruction consumes" test would miss it
                // (the fold then failed with "Stack empty"). Push unconditionally.
                if (r.Kind == ExceptionRegionKind.Filter)
                    catchEntryOffsets.Add(r.FilterOffset);
            }
        }

        foreach (var ins in il)
        {
            // GN-18 P2/D1 (S3): an instruction already consumed by the
            // GetGenericArguments adjacent-chain fold (its offset is in
            // gn18SkipOffsets, filled at the call site). The fold has already
            // emitted the constant that replaces the whole `GetGenericArguments();
            // <consumer>` segment, so this instruction MUST be skipped: running it
            // would read that constant a second time (the observed
            // il2cpp_array_get_length(1) -> 0xC0000005). It is deliberately
            // checked BEFORE the label / exception-entry handling: these offsets
            // are the sequential successors of the folded call (consumed
            // instructions are never branch targets or catch starts -- a label
            // between the call and its consumer would break the adjacency the
            // fold requires), and continuing past the label checks keeps the
            // join-slot / handler semantics untouched. prevIns is maintained
            // explicitly here because `continue` would skip the assignment at the
            // end of the loop body.
            if (gn18SkipOffsets.Remove(ins.Offset))
            {
                prevIns = ins;
                continue;
            }
            if (handlerStarts.Contains(ins.Offset) || labels.ContainsKey(ins.Offset))
                locConst.Clear(); // P0-2: merge points / exception-path entries make local values unknown

            if (catchEntryOffsets.Contains(ins.Offset))
                Push(new ExceptionEntryExpr(), ins);

            if (labels.TryGetValue(ins.Offset, out var labelName))
            {
                // Dead sequential flow: the previous instruction is an
                // unconditional br or ret -> this block is only entered via a
                // branch; restore the join slots if present, otherwise clear
                // the residual stack.
                var deadFallThrough = prevIns is not null &&
                    (prevIns.Value.OpCode is ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Ret);
                if (joinRestore.TryGetValue(labelName, out var joinVars))
                {
                    if (deadFallThrough)
                    {
                        stmts.Add(new LabelStmt { Name = labelName, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                        stack.Clear();
                        foreach (var jv in joinVars) stack.Push(new LocExpr { Name = jv });
                    }
                    else
                    {
                        // A fall-through arm reaches the join: write the
                        // current stack values into the shared join slots
                        // (heights must match), then rebuild the stack from
                        // the join slots. The assignments must precede the
                        // LabelStmt -- only the fall-through arm executes
                        // them; a branch jump lands directly on the label and
                        // skips the assignments.
                        var bottomToTop = BottomToTop(stack);
                        if (bottomToTop.Count != joinVars.Count)
                            throw new NotSupportedException($"join {labelName}: fall-through stack height {bottomToTop.Count} != join slots {joinVars.Count}");
                        for (var i = 0; i < joinVars.Count; i++)
                            stmts.Add(new AssignStmt { Target = joinVars[i], Value = bottomToTop[i], SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                        // A2 review: the fall-through arm repopulates the same
                        // shared slots -- reconcile the hint identically.
                        ReconcileJoinHint(joinVars, bottomToTop);
                        stmts.Add(new LabelStmt { Name = labelName, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                        stack.Clear();
                        foreach (var jv in joinVars) stack.Push(new LocExpr { Name = jv });
                    }
                }
                else
                {
                    stmts.Add(new LabelStmt { Name = labelName, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                    if (deadFallThrough) stack.Clear();
                }
            }

            switch (ins.OpCode)
            {
                case ILOpCode.Ldc_i4_m1: Push(new IntConstExpr { Value = -1 }, ins); break;
                case ILOpCode.Ldc_i4_0: Push(new IntConstExpr { Value = 0 }, ins); break;
                case ILOpCode.Ldc_i4_1: Push(new IntConstExpr { Value = 1 }, ins); break;
                case ILOpCode.Ldc_i4_2: Push(new IntConstExpr { Value = 2 }, ins); break;
                case ILOpCode.Ldc_i4_3: Push(new IntConstExpr { Value = 3 }, ins); break;
                case ILOpCode.Ldc_i4_4: Push(new IntConstExpr { Value = 4 }, ins); break;
                case ILOpCode.Ldc_i4_5: Push(new IntConstExpr { Value = 5 }, ins); break;
                case ILOpCode.Ldc_i4_6: Push(new IntConstExpr { Value = 6 }, ins); break;
                case ILOpCode.Ldc_i4_7: Push(new IntConstExpr { Value = 7 }, ins); break;
                case ILOpCode.Ldc_i4_8: Push(new IntConstExpr { Value = 8 }, ins); break;
                case ILOpCode.Ldc_i4_s:
                case ILOpCode.Ldc_i4:
                    Push(new IntConstExpr { Value = ins.Operand }, ins); break;
                case ILOpCode.Ldc_i8:
                    // Audit C3 + M5 wiring: int64 constants outside the int32
                    // range are carried by Int64ConstExpr (they flow into
                    // int64 locals/params and the runtime int64 icalls -- the
                    // emitter folds int32 arithmetic only, so mixed int64
                    // expressions stay int64 in C++ and widen correctly).
                    if (ins.Operand < int.MinValue || ins.Operand > int.MaxValue)
                        Push(new Int64ConstExpr { Value = ins.Operand }, ins);
                    else
                    {
                        // In-range int64 constants are bit-identical to int32.
                        // IsInt64 records the int64 provenance (see Expr.cs).
                        Push(new IntConstExpr { Value = ins.Operand, IsInt64 = true }, ins);
                    }
                    break;
                case ILOpCode.Ldc_r8:
                    // M5 wiring: ldc.r8 -- the operand is the int64 bit pattern
                    // (IlDecoder stores DoubleToInt64Bits); restore the double.
                    Push(new DoubleConstExpr { Value = BitConverter.Int64BitsToDouble(ins.Operand) }, ins);
                    break;
                case ILOpCode.Ldc_r4:
                    // M5 wiring: ldc.r4 -- float literals live in double slots.
                    Push(new DoubleConstExpr { Value = BitConverter.Int32BitsToSingle((int)ins.Operand) }, ins);
                    break;
                case ILOpCode.Ldstr:
                    Push(new StringConstExpr { Value = ins.StringOperand ?? "" }, ins); break;
                case ILOpCode.Ldnull:
                    Push(new NullExpr(), ins); break;
                case ILOpCode.Ldarg_0:
                case ILOpCode.Ldarg_1:
                case ILOpCode.Ldarg_2:
                case ILOpCode.Ldarg_3:
                    Push(new LocExpr { Name = ArgName(ins.OpCode - ILOpCode.Ldarg_0) }, ins); break;
                case ILOpCode.Ldarg:
                case ILOpCode.Ldarg_s:
                    Push(new LocExpr { Name = ArgName((int)ins.Operand) }, ins); break;
                case ILOpCode.Starg:
                case ILOpCode.Starg_s:
                    EmitStore(ArgName((int)ins.Operand), ins); break;
                case ILOpCode.Ldloc_0:
                case ILOpCode.Ldloc_1:
                case ILOpCode.Ldloc_2:
                case ILOpCode.Ldloc_3:
                {
                    var ldName = LocName(ctx, ins.OpCode - ILOpCode.Ldloc_0);
                    // P0-2: single-assignment constant local -> substitute the
                    // constant directly (drops the dependency on the loc).
                    Push(locConst.TryGetValue(ldName, out var lc) ? new IntConstExpr { Value = lc } : new LocExpr { Name = ldName }, ins);
                    break;
                }
                case ILOpCode.Ldloc:
                case ILOpCode.Ldloc_s:
                {
                    var ldName = LocName(ctx, (int)ins.Operand);
                    Push(locConst.TryGetValue(ldName, out var lc) ? new IntConstExpr { Value = lc } : new LocExpr { Name = ldName }, ins);
                    break;
                }
                case ILOpCode.Stloc_0:
                case ILOpCode.Stloc_1:
                case ILOpCode.Stloc_2:
                case ILOpCode.Stloc_3:
                case ILOpCode.Stloc:
                case ILOpCode.Stloc_s:
                {
                    var stName = LocName(ctx, StlocIndex(ins.OpCode, (int)ins.Operand));
                    var stValue = stack.Count > 0 ? stack.Peek() : null; // EmitStore pops the stack; peek first
                    // M5 wiring: the catch-variable binding stores the
                    // exception ENTRY -- the value IS the type-name string;
                    // GetType()/get_Name on it fold to identities.
                    if (stValue is ExceptionEntryExpr)
                        objTypes[stName] = "System.Type";

                    // b5-gaps section 104: propagate a call's ReturnTypeName
                    // through the store (the LINQ ToDictionary result's tracked
                    // dictionary type -- the Dictionary dispatcher keys on it
                    // to tell a LINQ-produced string->int32 wrapper from the
                    // instance-path SI domain's own wrapper, whose object
                    // layout is DIFFERENT). Without this the get_Item dispatch
                    // on the local loses the hint and misroutes to the si
                    // icalls (probe-todict-i4val segfault).
                    else if (stValue is CallExpr stRtnCall
                        && stRtnCall.Resolved?.ReturnTypeName is { } stRtn)
                        objTypes[stName] = BareManagedName(stRtn);
                    // tuple1 (ValueTuple): propagate the tracked tuple type
                    // through the store so a later ldloca+callvirt (ToString /
                    // GetHashCode / Equals) sees the receiver hint.
                    else if (stValue is LocExpr stSrc && objTypes.TryGetValue(stSrc.Name, out var stSrcType)
                        && ValueTupleSupport.IsValueTupleName(stSrcType))
                        objTypes[stName] = stSrcType;
                    // tuple1 (Tuple): a Tuple.Create result is a "vt:"-kind call
                    // (the generated Tuple`N struct). A Tuple local is a managed
                    // REFERENCE in the IL, so csc emits a plain callvirt (no
                    // constrained. marker) -- the receiver hint must come from the
                    // store, or GetHashCode/Equals fall through to Object.
                    else if (stValue is CallExpr stCall
                        && stCall.ResultKind is { } stVk && stVk.StartsWith("vt:", StringComparison.Ordinal)
                        && ValueTupleSupport.IsValueTupleName(stVk["vt:".Length..]))
                        objTypes[stName] = stVk["vt:".Length..];
                    // b5-gaps 13: a MULTIDIM array store propagates the
                    // "[<elem><rank>]" token (the Ldelema rank decode keys on
                    // it -- `DateTime[,] d; ... d[i,j] = new DateTime(...)`
                    // lowers to ldelema on the STORED local, whose tracked
                    // temp token must survive the stloc).
                    else if (stValue is LocExpr stMdSrc && objTypes.TryGetValue(stMdSrc.Name, out var stMdTok)
                        && stMdTok.Length >= 4 && stMdTok[0] == '[' && stMdTok[^1] == ']')
                        objTypes[stName] = stMdTok;
                    // p-refl2-gapmatrix (B08): propagate the member-VALUE
                    // provenance through the store (a folded typeof-chain
                    // GetMethod/GetField result, possibly via its materialized
                    // temp).
                    if (stValue is CallExpr stProvCall && stProvCall.MemberProvenance is { } stProv)
                        memberProvenanceByLocal[stName] = stProv;
                    else if (stValue is LocExpr stProvSrc && memberProvenanceByLocal.TryGetValue(stProvSrc.Name, out var stProv2))
                        memberProvenanceByLocal[stName] = stProv2;
                    EmitStore(stName, ins);
                    if (stValue is IntConstExpr sc
                        && stlocCount.TryGetValue(stName, out var scnt) && scnt == 1
                        && !addrTaken.Contains(stName))
                    {
                        locConst[stName] = sc.Value;
                        propagatedLocs.Add(stName);
                    }
                    else
                    {
                        locConst.Remove(stName);
                    }
                    break;
                }
                case ILOpCode.Ldloca:
                case ILOpCode.Ldloca_s:
                {
                    // ldloca: local variable address (M3 Task 11: value-type ctor this/byref).
                    var ldaName = LocName(ctx, (int)ins.Operand);
                    // tuple1 (ValueTuple): a tuple local initialized in place by
                    // its ctor (the `var t = (1, "a")` shape) has its type in
                    // the IL local signature ("vt:<closedName>") -- record the
                    // receiver hint so a later callvirt resolves to the tuple
                    // member helpers.
                    if (!objTypes.ContainsKey(ldaName) && ctx.LocalKinds is { Length: > 0 } lk
                        && ldaName.StartsWith("loc", StringComparison.Ordinal)
                        && int.TryParse(ldaName.AsSpan(3), out var ldaIdx)
                        && ldaIdx >= 0 && ldaIdx < lk.Length
                        && lk[ldaIdx] is { } ldaKind && ldaKind.StartsWith("vt:", StringComparison.Ordinal))
                        objTypes[ldaName] = ldaKind["vt:".Length..];
                    Push(new LocAddrExpr { Name = ldaName }, ins);
                    break;
                }
                case ILOpCode.Ldarga:
                case ILOpCode.Ldarga_s:
                    // ldarga: argument address (M3 byref scenario).
                    Push(new LocAddrExpr { Name = ArgName((int)ins.Operand) }, ins);
                    break;
                case ILOpCode.Dup:
                    // Duplicate the stack top (`x != 4` short-circuit
                    // evaluation, `n-- > 0` argument post-decrement, etc. dup).
                    // IR expressions are immutable value trees (CallExpr is
                    // materialized into a temp LocExpr before being pushed), so
                    // sharing the reference is safe.
                    if (stack.Count == 0)
                        throw new NotSupportedException($"dup on empty stack at offset 0x{ins.Offset:X4}");
                    stack.Push(stack.Peek()); break;
                case ILOpCode.Neg:
                    Push(new UnaryExpr { Op = UnaryOp.Neg, Operand = stack.Pop() }, ins); break;
                case ILOpCode.Not:
                    Push(new UnaryExpr { Op = UnaryOp.Not, Operand = stack.Pop() }, ins); break;
                case ILOpCode.Ldsfld:
                    EnsureCctor(stmts, ctx, md, ins, types);
                    // M5 wiring: the BCL string.Empty constant folds to the
                    // interned empty literal (the generated code has no BCL
                    // static-field storage).
                    if (md != null && FieldName(md, ins) == "System.String.Empty")
                    {
                        Push(new StringConstExpr { Value = "" }, ins);
                        break;
                    }
                    // bitconvcase L12: System.BitConverter.IsLittleEndian is a
                    // static readonly FIELD in net48 (never a get_ property,
                    // so the field mapper is not involved) -- same external
                    // BCL storage gap as String.Empty. Fold to the literal 1:
                    // little-endian is the documented platform contract
                    // (bitconverter.h baseline, Windows x86-64 = little-endian).
                    if (md != null && FieldName(md, ins) == "System.BitConverter.IsLittleEndian")
                    {
                        Push(new IntConstExpr { Value = 1 }, ins);
                        break;
                    }
                    // batch 162 (eventcase): System.EventArgs.Empty is a BCL
                    // static readonly FIELD -- folds to null (the event
                    // handler's `e` argument; every wired handler ignores
                    // it; the runtime bridge passes null for the args
                    // placeholder).
                    if (md != null && FieldName(md, ins) == "System.EventArgs.Empty")
                    {
                        Push(new NullExpr { TypeName = "System.EventArgs" }, ins);
                        break;
                    }
                {
                    var sf = new StaticFieldExpr { FieldName = FieldName(md, ins) };
                    // impl-arrayagg (defect 2): a List<T> FIELD load must carry
                    // its declared type into objTypes -- the LINQ receiver hint
                    // is read off the pushed slot. Materialize the load into a
                    // typed temp (the same shape Push gives a call result).
                    var sft = FieldTypeFullName(md, ins);
                    if (sft != null && sft.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal))
                    {
                        var sfn = $"t{temps.Count}";
                        temps.Add(sfn);
                        tempWidths[sfn] = "r";
                        stmts.Add(new AssignStmt { Target = sfn, Value = sf, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                        objTypes[sfn] = sft;
                        stack.Push(new LocExpr { Name = sfn });
                    }
                    else Push(sf, ins);
                    break;
                }
                case ILOpCode.Ldsflda:
                    // ldsflda (ECMA 0x7F): address of a static field -- the
                    // Ldflda counterpart (work order #10). `"A:" + order` with
                    // a static int compiles to `ldsflda; call instance
                    // Int32::ToString()` -- the byref receiver. The address
                    // shape routes the receiver through the resolver's
                    // "deref:*" cast arm (a LOAD of the field value, exactly
                    // Ldflda/ldloca semantics); a plain value fold would
                    // misroute to "unbox:*" (pointer-cast of the value).
                    EnsureCctor(stmts, ctx, md, ins, types);
                    Push(new StaticFieldAddrExpr { FieldName = FieldName(md, ins) }, ins); break;
                case ILOpCode.Stsfld:
                    EnsureCctor(stmts, ctx, md, ins, types);
                    {
                        // Postfix increment on a static field
                        // (`ldsfld SF; dup; ldc.i4.1; add; stsfld SF`): the dup
                        // copy is a LAZY StaticFieldExpr read, so a consumer
                        // placed after the store would observe the NEW value.
                        // net48 hands the ORIGINAL value to the postfix
                        // expression's consumer -- snapshot every live stack
                        // read of this field into a temp BEFORE the store
                        // (same contract as the local/param EmitStore path).
                        var sfTarget = FieldName(md, ins);
                        var sfValue = stack.Pop();
                        SnapshotStackReads(stmts, stack, sfTarget, ins, ReferencesStaticField,
                            md != null ? FieldWidthFromSignature(ResolveField(md, ins).Signature) : null,
                            staticFieldSlot: true);
                        stmts.Add(new AssignStmt { Target = sfTarget, Value = sfValue, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                    }
                    break;
                case ILOpCode.Ldfld:
                {
                    var fobj = stack.Pop();
                    var fe = new FieldExpr
                    {
                        Object = fobj,
                        FieldCppName = SanitizeField(FieldToken(md, ins)),
                        OwnerFullName = OwnerFullName(md, ins),
                        IsRefField = IsRefField(md, ins),
                        IsValueTypeObject = types != null && IsValueTypeOwner(OwnerFullName(md, ins), types)
                    };
                    // impl-arrayagg (defect 2): a captured local's List<T>
                    // field (the closure display class) is the SAME receiver
                    // hint gap as a static List field -- materialize the load
                    // into a typed temp so the LINQ gate sees the List.
                    var fet = FieldTypeFullName(md, ins);
                    if (fet != null && fet.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal))
                    {
                        var fen = $"t{temps.Count}";
                        temps.Add(fen);
                        tempWidths[fen] = "r";
                        stmts.Add(new AssignStmt { Target = fen, Value = fe, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                        objTypes[fen] = fet;
                        stack.Push(new LocExpr { Name = fen });
                    }
                    else Push(fe, ins);
                    break;
                }
                case ILOpCode.Ldflda:
                    Push(new FieldAddrExpr
                    {
                        Object = stack.Pop(),
                        FieldCppName = SanitizeField(FieldToken(md, ins)),
                        OwnerFullName = OwnerFullName(md, ins),
                        // batch 156c (asynccase): mirror of the Ldfld
                        // IsValueTypeObject flag -- the emitter's FieldAddrExpr
                        // emission (CodeGenerator.cs:2787) would otherwise take
                        // the null-checked REFERENCE path and cast the value-
                        // type struct address to Il2CppObject* (a static_cast
                        // error: _F_d__1* is not related to Il2CppObject).
                        IsValueTypeObject = types != null && IsValueTypeOwner(OwnerFullName(md, ins), types)
                    }, ins);
                    break;
                case ILOpCode.Stfld:
                    var stfldValue = stack.Pop();
                    var stfldObj = stack.Pop();
                    var isRef = IsRefField(md, ins);
                    var vto = types != null && IsValueTypeOwner(OwnerFullName(md, ins), types);
                    stmts.Add(new FieldAssignStmt
                    {
                        Object = stfldObj,
                        FieldCppName = SanitizeField(FieldToken(md, ins)),
                        OwnerFullName = OwnerFullName(md, ins),
                        IsRefField = isRef,
                        RefTargetFullName = isRef ? RefTargetFullName(md, ins) : "",
                        IsValueTypeObject = vto,
                        Value = stfldValue,
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode)
                    });
                    break;
                case ILOpCode.Add:
                case ILOpCode.Sub:
                case ILOpCode.Mul:
                case ILOpCode.Div:
                case ILOpCode.Div_un:
                case ILOpCode.Rem:
                case ILOpCode.And:
                case ILOpCode.Or:
                case ILOpCode.Xor:
                case ILOpCode.Shl:
                case ILOpCode.Shr:
                case ILOpCode.Shr_un:
                case ILOpCode.Ceq:
                case ILOpCode.Cgt:
                case ILOpCode.Clt:
                case ILOpCode.Cgt_un:
                case ILOpCode.Clt_un:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    Push(new BinaryExpr { Op = MapBinary(ins.OpCode), Left = left, Right = right }, ins);
                    break;
                }
                case ILOpCode.Call:
                    EnsureCctorForCall(stmts, ctx, md, ins, types);
                    EmitCall(stmts, stack, temps, ins, md, types, virtualDispatch: false);
                    break;
                case ILOpCode.Callvirt:
                    EnsureCctorForCall(stmts, ctx, md, ins, types);
                    EmitCall(stmts, stack, temps, ins, md, types, virtualDispatch: true);
                    break;
                case ILOpCode.Calli:
                    // calli (0x29, ECMA-335 I.12.3.3.3 / III.3.19): indirect
                    // call through a function pointer. The CLI stack is
                    // (bottom-to-top) [arg1..argN, fnptr] -- the fnptr is
                    // pushed LAST (on top), the args below it (Roslyn fnptr
                    // call IL verified: ldarg.0; stloc.0; ldc.i4.3; ldc.i4.4;
                    // ldloc.0; calli -- the reloaded fnptr is the stack top;
                    // corert ImportCalli pops the fnptr first, then reads the
                    // args below). The operand is a StandAloneSig (0x11) token
                    // whose signature describes the target (calling
                    // convention + params + return). Fold to a CallExpr
                    // (Function "calli_{token}", Args = [fnptr, args...]); the
                    // emitter decodes the signature and renders the indirect
                    // call ((ret(*)(params))fnptr)(args) with a null-pointer
                    // guard (NRE).
                    EmitCalli(stmts, stack, ins, md);
                    break;
                case ILOpCode.Newobj:
                    EnsureCctorForNewObj(stmts, ctx, md, ins, types);
                    EmitNewObj(stmts, stack, temps, ins, md, types, exceptionMessages, exceptionCtorParamTypes);
                    break;
                case ILOpCode.Ret:
                    stmts.Add(new RetStmt
                    {
                        // void method: ret pops no return value; stack values
                        // left over after a join (compiler-preserved constants
                        // etc.) are dead for void and must be discarded, else
                        // `return <residual>` -> a compile error on a void
                        // function's return value.
                        Value = ctx.ReturnsVoid ? null : (stack.Count > 0 ? stack.Pop() : null),
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode)
                    });
                    break;
                case ILOpCode.Br:
                case ILOpCode.Br_s:
                    SpillAtBranch(labels[(int)ins.Operand], ins, unconditional: true);
                    stmts.Add(new BranchStmt { TargetLabel = labels[(int)ins.Operand], SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                    break;
                case ILOpCode.Brfalse:
                case ILOpCode.Brfalse_s:
                    var brFalseCond = Not(stack.Pop());
                    SpillAtBranch(labels[(int)ins.Operand], ins, unconditional: false);
                    stmts.Add(new BranchStmt { TargetLabel = labels[(int)ins.Operand], Condition = brFalseCond, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                    break;
                case ILOpCode.Brtrue:
                case ILOpCode.Brtrue_s:
                    var brTrueCond = stack.Pop();
                    SpillAtBranch(labels[(int)ins.Operand], ins, unconditional: false);
                    stmts.Add(new BranchStmt { TargetLabel = labels[(int)ins.Operand], Condition = brTrueCond, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                    break;
                case ILOpCode.Switch:
                {
                    // switch table -> Ceq comparison chain (no new IR nodes):
                    // for each table entry i emit `if (sel == i) goto
                    // L_target_i;` (table order preserved), then an
                    // unconditional goto to the default arm at the chain tail
                    // -- a negative/out-of-range sel (no Ceq hit) always lands
                    // on default.
                    var sel = stack.Pop();
                    if (!switchDefault.TryGetValue(ins.Offset, out var defOffset))
                        throw new NotSupportedException($"switch at offset 0x{ins.Offset:X4}: no default arm (switch is the last instruction)");
                    var targets = ins.SwitchTargets ?? Array.Empty<int>();
                    for (var i = 0; i < targets.Length; i++)
                    {
                        SpillAtBranch(labels[targets[i]], ins, unconditional: false);
                        stmts.Add(new BranchStmt
                        {
                            TargetLabel = labels[targets[i]],
                            Condition = new BinaryExpr
                            {
                                Op = BinaryOp.Ceq,
                                Left = sel,
                                Right = new IntConstExpr { Value = i }
                            },
                            SourceOffset = ins.Offset,
                            SourceOpName = OpName(ins.OpCode)
                        });
                    }
                    SpillAtBranch(labels[defOffset], ins, unconditional: true);
                    stmts.Add(new BranchStmt { TargetLabel = labels[defOffset], SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                    break;
                }
                case ILOpCode.Beq:
                case ILOpCode.Beq_s:
                case ILOpCode.Bne_un:
                case ILOpCode.Bne_un_s:
                case ILOpCode.Bge:
                case ILOpCode.Bge_s:
                case ILOpCode.Bgt:
                case ILOpCode.Bgt_s:
                case ILOpCode.Ble:
                case ILOpCode.Ble_s:
                case ILOpCode.Blt:
                case ILOpCode.Blt_s:
                case ILOpCode.Bge_un:
                case ILOpCode.Bge_un_s:
                case ILOpCode.Bgt_un:
                case ILOpCode.Bgt_un_s:
                case ILOpCode.Ble_un:
                case ILOpCode.Ble_un_s:
                case ILOpCode.Blt_un:
                case ILOpCode.Blt_un_s:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    var cond = MapBranchCompare(ins.OpCode, left, right);
                    SpillAtBranch(labels[(int)ins.Operand], ins, unconditional: false);
                    stmts.Add(new BranchStmt
                    {
                        TargetLabel = labels[(int)ins.Operand],
                        Condition = cond,
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode)
                    });
                    break;
                }
                case ILOpCode.Conv_i4:
                case ILOpCode.Conv_i:
                    // M5 wiring: value narrowing/identity conversions.
                    //   int64 const -> low 32 bits (.NET unchecked semantics)
                    //   double const/var -> icall_conv_i4_d (truncate toward
                    //     zero; overflow/NaN/Inf -> int32.Min, cvttsd2si)
                    //   int64 var -> icall_conv_i4_i8 (low 32 bits)
                    //   int32 -> identity (ldlen etc. produce int32)
                    if (stack.Count > 0 && stack.Peek() is Int64ConstExpr lc4)
                    {
                        stack.Pop();
                        Push(new IntConstExpr { Value = unchecked((int)(lc4.Value & 0xFFFFFFFFL)) }, ins);
                    }
                    else if (stack.Count > 0 && stack.Peek() is IntConstExpr { IsInt64: true } ic4)
                    {
                        stack.Pop();
                        Push(new IntConstExpr { Value = ic4.Value }, ins); // in-range: bit-identical
                    }
                    else if (stack.Count > 0)
                    {
                        // numchain (e2e line 158): `(int)double.Parse("2.9") == 2`
                        // -- a double-typed CALL RESULT (ResultKind "d") was
                        // dropped here (the pattern only matched consts/locals),
                        // so the double 2.9 stayed on the stack and `== 2` never
                        // compared the truncated 2. ResultKind "l" (int64 call
                        // results) had the same drop. Push() materializes a call
                        // result into a temp t*, so in practice the stack top is
                        // a LocExpr -- LocKindOf only knows "loc" locals, so the
                        // width probe falls back to LocWidth (temps via
                        // tempWidths, args via ParamTypeNames) to recover "d"/"l".
                        // D-RND2-3 (randomdeep2): `(int)(NextDouble() * 10)` in a
                        // COMPARISON -- the multiply stays an unmaterialized
                        // double-typed BinaryExpr on the stack, which the old
                        // gate (consts/locals/call-results only) never matched,
                        // so the conv.i4 truncation silently VANISHED and the
                        // comparison ran in double space (cons10 False vs net48
                        // True). ConvI4SourceWidth probes expression TREE width
                        // (BinaryExpr/UnaryExpr via their operands -- the
                        // emitter's ExprIsDouble precedent), not just leaf kinds.
                        var width = ConvI4SourceWidth(stack.Peek(), ctx, tempWidths);
                        if (width is "d" or "l")
                        {
                            var top = stack.Pop();
                            var conv = new CallExpr
                            {
                                Function = width == "l"
                                    ? "il2cpp::runtime::icall_conv_i4_i8"
                                    : "il2cpp::runtime::icall_conv_i4_d"
                            };
                            conv.Args.Add(top);
                            Push(conv, ins);
                        }
                    }
                    break;
                case ILOpCode.Conv_i8:
                    // M5 wiring: int32 -> int64 widening (identity; the value
                    // is bit-identical -- the TYPE flows via the IL local/arg
                    // kinds), int64 -> identity, double const/var -> 
                    // icall_conv_i8_d (cvttsd2si semantics; NaN/overflow ->
                    // int64.Min).
                    // C0 (2026-09-19): the double-source probe MUST use
                    // ConvI4SourceWidth (tempWidths + expression trees), not
                    // LocKindOf (locals only). A double-typed CALL RESULT /
                    // temp / BinaryExpr reached here as a LocExpr/BinaryExpr
                    // and fell through to the "identity" break -- `(long)d`
                    // emitted the raw double bits (DoubleToInt64Bits) instead
                    // of the numeric truncation (TimeSpan.TotalMilliseconds
                    // probe: 123456.789 -> 4683220299151006858). The conv.i4
                    // arm already used ConvI4SourceWidth (numchain/RND2-3);
                    // conv.i8 was the odd one out.
                    if (stack.Count > 0 && stack.Peek() is IntConstExpr ic8)
                    {
                        stack.Pop();
                        Push(new Int64ConstExpr { Value = ic8.Value }, ins);
                    }
                    else if (stack.Count > 0 && ConvI4SourceWidth(stack.Peek(), ctx, tempWidths) == "d")
                    {
                        var top = stack.Pop();
                        var conv = new CallExpr { Function = "il2cpp::runtime::icall_conv_i8_d", ResultKind = "l" };
                        conv.Args.Add(top);
                        Push(conv, ins);
                    }
                    else if (stack.Count > 0 && ConvI4SourceWidth(stack.Peek(), ctx, tempWidths) == "i")
                    {
                        // C0 (2026-09-19): int32 -> int64 is sign extension. It
                        // is NOT an identity at the SLOT level: the int32 temp
                        // stays 4 bytes, and a downstream int64 consumer that
                        // binds the value through its address
                        // (`*reinterpret_cast<int64_t*>(&tmp)` -- the inlined
                        // callee argument path) reads 8 bytes from a 4-byte
                        // slot (adjacent-stack garbage; strict-aliasing UB).
                        // Emit the real widening icall so the result is a
                        // genuine int64 temp. `TimeSpan.GetHashCode` (int32)
                        // fed to `L(string,long)` exposed this: the emitted
                        // int64 read of the int32 hash temp yielded a
                        // 64-bit-varying garbage value.
                        var topi = stack.Pop();
                        var convi = new CallExpr { Function = "il2cpp::runtime::icall_conv_i8_i4", ResultKind = "l" };
                        convi.Args.Add(topi);
                        Push(convi, ins);
                    }
                    break; // int64 identity
                case ILOpCode.Conv_u8:
                    // conv.u8 from a 32-bit source ZERO-extends (int32 ->
                    // uint32 -> int64): NOT an identity for negative int32
                    // bit patterns. csc emits `ldc.i4 0x80000000; conv.u8`
                    // for int64 constants whose low 32 bits are >= 2^31
                    // (e.g. 2147483648L) -- zero-extension gives +2147483648
                    // while sign-extension (conv.i8 semantics) would give
                    // -2147483648 (convertcase L26: Convert.ToInt32(2147483648L)
                    // passed the in-range negative and never threw the
                    // checked OverflowException).
                    // valdeep L26: the SAME zero-extension is required for
                    // non-constant 32-bit sources (uint locals/args/temps --
                    // (ulong)uint.MaxValue must widen 0xFFFFFFFF to
                    // 4294967295, not -1). csc never emits conv.u8 on a
                    // signed int32 value (it uses conv.i8 for int -> ulong),
                    // so every 32-bit source here carries uint semantics.
                    // 64-bit sources (int64/uint64 locals, args, "l"-kind
                    // temps) are bit identities; double stays unchanged.
                    if (stack.Count > 0 && stack.Peek() is IntConstExpr icu8)
                    {
                        stack.Pop();
                        Push(new Int64ConstExpr { Value = (long)(uint)icu8.Value }, ins);
                    }
                    else if (stack.Count > 0 && ConvI4SourceWidth(stack.Peek(), ctx, tempWidths) == "d")
                    {
                        // C0 (2026-09-19): (ulong)double -- was an identity
                        // (raw double bits); the numeric truncation is
                        // icall_conv_u8_d (cvttsd2si to unsigned semantics).
                        var topd = stack.Pop();
                        var convd = new CallExpr { Function = "il2cpp::runtime::icall_conv_u8_d", ResultKind = "l" };
                        convd.Args.Add(topd);
                        Push(convd, ins);
                    }
                    else if (stack.Count > 0 && Is32BitConvU8Source(stack.Peek(), ctx, tempWidths))
                    {
                        var top = stack.Pop();
                        var conv = new CallExpr { Function = "il2cpp::runtime::icall_conv_u8_i4", ResultKind = "l" };
                        conv.Args.Add(top);
                        Push(conv, ins);
                    }
                    break; // int64 identity / double paths unchanged
                case ILOpCode.Conv_u4:
                    // C0 (2026-09-19): (uint)double -- was folded into the
                    // identity group (raw double bits); numeric truncation is
                    // icall_conv_u4_d (low 32 bits of the truncation).
                    if (stack.Count > 0 && ConvI4SourceWidth(stack.Peek(), ctx, tempWidths) == "d")
                    {
                        var topu4 = stack.Pop();
                        var convu4 = new CallExpr { Function = "il2cpp::runtime::icall_conv_u4_d" };
                        convu4.Args.Add(topu4);
                        Push(convu4, ins);
                    }
                    break;
                case ILOpCode.Conv_r8:
                case ILOpCode.Conv_r4:
                case ILOpCode.Conv_r_un:
                case ILOpCode.Conv_u1:
                case ILOpCode.Conv_u2:
                case ILOpCode.Conv_i1:
                case ILOpCode.Conv_i2:
                case ILOpCode.Conv_u:
                    // int32/int64 -> double is an implicit C++ promotion
                    // (exact); float lives in double slots; the 8/16-bit
                    // integer widths live zero/sign-extended in int32 slots.
                    // All identities.
                    break;
                case ILOpCode.Conv_ovf_i4:
                    // checked int64 -> int32 (runtime range check via icall;
                    // in-range int64 constants pass, out-of-range would be a
                    // Roslyn CS0221 and never reach the IL). Input WIDTH
                    // dispatch (batch 152): int32 identity; int64 ->
                    // icall_conv_ovf_i4_i8; double -> icall_conv_ovf_i4_d
                    // (was misrouted through the int64 icall -- a double bit
                    // pattern read as int64 would silently pass or wrap).
                    if (stack.Count > 0 && stack.Peek() is IntConstExpr)
                    {
                        break; // int32 -> int32 identity
                    }
                    if (stack.Count > 0)
                    {
                        var top = stack.Pop();
                        var width = ExprWidth(top, ctx, tempWidths);
                        var conv = new CallExpr
                        {
                            Function = width == "d"
                                ? "il2cpp::runtime::icall_conv_ovf_i4_d"
                                : "il2cpp::runtime::icall_conv_ovf_i4_i8"
                        };
                        conv.Args.Add(top);
                        Push(conv, ins);
                    }
                    break;
                case ILOpCode.Conv_ovf_u4:
                    // checked -> uint32: int32 -> icall_conv_ovf_u4_i4
                    // (negative throws); int64 -> u4_i8; double -> u4_d.
                    // A uint32 source is same-type and never emits this op.
                    // A NEGATIVE int32 CONSTANT must still throw (csc folds
                    // `int negi = -1; checked((uint)negi)` into the constant
                    // -- the identity break would silently succeed).
                    if (stack.Count > 0 && stack.Peek() is IntConstExpr cu4 && cu4.Value >= 0)
                    {
                        stack.Pop();
                        Push(new Int64ConstExpr { Value = (long)(uint)cu4.Value }, ins);
                    }
                    else if (stack.Count > 0)
                    {
                        var top = stack.Pop();
                        var width = ExprWidth(top, ctx, tempWidths);
                        var conv = new CallExpr
                        {
                            Function = width switch
                            {
                                "d" => "il2cpp::runtime::icall_conv_ovf_u4_d",
                                "l" => "il2cpp::runtime::icall_conv_ovf_u4_i8",
                                _ => "il2cpp::runtime::icall_conv_ovf_u4_i4",
                            }
                        };
                        conv.Args.Add(top);
                        Push(conv, ins);
                    }
                    break;
                case ILOpCode.Conv_ovf_u8:
                    // checked -> uint64: int32 -> u8_i4 (negative throws);
                    // int64 -> u8_i8; double -> u8_d. Negative int32
                    // CONSTANTS keep the checked icall (throws at runtime).
                    if (stack.Count > 0 && stack.Peek() is IntConstExpr cu8 && cu8.Value >= 0)
                    {
                        stack.Pop();
                        Push(new Int64ConstExpr { Value = (long)(ulong)cu8.Value }, ins);
                    }
                    else if (stack.Count > 0)
                    {
                        var top = stack.Pop();
                        var width = ExprWidth(top, ctx, tempWidths);
                        var conv = new CallExpr
                        {
                            Function = width switch
                            {
                                "d" => "il2cpp::runtime::icall_conv_ovf_u8_d",
                                "i" => "il2cpp::runtime::icall_conv_ovf_u8_i4",
                                _ => "il2cpp::runtime::icall_conv_ovf_u8_i8",
                            }
                        };
                        conv.Args.Add(top);
                        Push(conv, ins);
                    }
                    break;
                case ILOpCode.Conv_ovf_i8:
                    // checked int32 -> int64: never overflows -- promote.
                    if (stack.Count > 0 && stack.Peek() is IntConstExpr ov8)
                    {
                        stack.Pop();
                        Push(new Int64ConstExpr { Value = ov8.Value }, ins);
                    }
                    else if (stack.Count > 0 && LocKindOf(ctx, stack.Peek()) == "l")
                    {
                        break; // int64 local: exact identity
                    }
                    else if (stack.Count > 0)
                    {
                        // checked double (const / "d" local / call result) ->
                        // int64: the runtime range-checked icall (net48
                        // conv.ovf.i8 throws OverflowException on
                        // out-of-range/NaN; the plain icall_conv_i8_d would
                        // silently wrap -- batch 152 correctness fix). An
                        // int32-typed call result here is a Roslyn-folded
                        // same-arity case (checked casts of int expressions
                        // are constant-folded or already widened); the
                        // double-interpreted bit pattern is rejected by the
                        // icall's range check instead of silently wrapping.
                        var top = stack.Pop();
                        var conv = new CallExpr { Function = "il2cpp::runtime::icall_conv_ovf_i8_d", ResultKind = "l" };
                        conv.Args.Add(top);
                        Push(conv, ins);
                    }
                    break; // int64 identity
                case ILOpCode.Conv_ovf_u1:
                case ILOpCode.Conv_ovf_i1:
                case ILOpCode.Conv_ovf_u2:
                case ILOpCode.Conv_ovf_i2:
                    // checked narrow casts (byte/sbyte/ushort/short) --
                    // batch 152e: the runtime range-checked icalls
                    // (negative constants keep the checked path and throw).
                    if (stack.Count > 0)
                    {
                        var top = stack.Pop();
                        var width = ExprWidth(top, ctx, tempWidths);
                        var suffix = ins.OpCode switch
                        {
                            ILOpCode.Conv_ovf_u1 => "u1",
                            ILOpCode.Conv_ovf_i1 => "i1",
                            ILOpCode.Conv_ovf_u2 => "u2",
                            _ => "i2",
                        };
                        var conv = new CallExpr
                        {
                            Function = width == "d"
                                ? $"il2cpp::runtime::icall_conv_ovf_{suffix}_d"
                                : $"il2cpp::runtime::icall_conv_ovf_{suffix}_i8"
                        };
                        conv.Args.Add(top);
                        Push(conv, ins);
                    }
                    break;
                case ILOpCode.Conv_ovf_i4_un:
                    // checked uint32 -> int32 (the UN-suffixed form: the
                    // input bit pattern is UNSIGNED). The i4_u8 icall takes a
                    // uint64; sign-extension of a negative bit pattern
                    // (>= 2^31) yields a huge uint64 that fails its > int.Max
                    // range test -- the checked throw. Batch 154b.
                    if (stack.Count > 0)
                    {
                        var top = stack.Pop();
                        var conv = new CallExpr { Function = "il2cpp::runtime::icall_conv_ovf_i4_u8" };
                        conv.Args.Add(top);
                        Push(conv, ins);
                    }
                    break;
                case ILOpCode.Conv_ovf_i8_un:
                    // checked ulong -> long: same bit-pattern trick via
                    // i8_u8 (sign-extended uint64 > long.Max throws).
                    if (stack.Count > 0)
                    {
                        var top = stack.Pop();
                        var conv = new CallExpr { Function = "il2cpp::runtime::icall_conv_ovf_i8_u8", ResultKind = "l" };
                        conv.Args.Add(top);
                        Push(conv, ins);
                    }
                    break;
                case ILOpCode.Conv_ovf_u4_un:
                    // checked ulong -> uint: u4_u8 (uint64 > uint.Max
                    // throws; negative bit patterns sign-extend and throw).
                    if (stack.Count > 0)
                    {
                        var top = stack.Pop();
                        var conv = new CallExpr { Function = "il2cpp::runtime::icall_conv_ovf_u4_u8" };
                        conv.Args.Add(top);
                        Push(conv, ins);
                    }
                    break;
                case ILOpCode.Conv_ovf_u8_un:
                    // checked ulong -> ulong: identity (never overflows).
                    break;
                case ILOpCode.Conv_ovf_u1_un:
                case ILOpCode.Conv_ovf_i1_un:
                case ILOpCode.Conv_ovf_u2_un:
                case ILOpCode.Conv_ovf_i2_un:
                    // checked unsigned-input narrow casts: the *_u8 range
                    // icalls (a negative bit pattern sign-extends past the
                    // unsigned range and throws). Batch 154b.
                    if (stack.Count > 0)
                    {
                        var top = stack.Pop();
                        var suffix = ins.OpCode switch
                        {
                            ILOpCode.Conv_ovf_u1_un => "u1",
                            ILOpCode.Conv_ovf_i1_un => "i1",
                            ILOpCode.Conv_ovf_u2_un => "u2",
                            _ => "i2",
                        };
                        var conv = new CallExpr
                        {
                            Function = $"il2cpp::runtime::icall_conv_ovf_{suffix}_u8"
                        };
                        conv.Args.Add(top);
                        Push(conv, ins);
                    }
                    break;
                case ILOpCode.Conv_ovf_i_un:
                    // checked native-int (32-bit) unsigned input: same as i4_un.
                    if (stack.Count > 0)
                    {
                        var top = stack.Pop();
                        var conv = new CallExpr { Function = "il2cpp::runtime::icall_conv_ovf_i4_u8" };
                        conv.Args.Add(top);
                        Push(conv, ins);
                    }
                    break;
                case ILOpCode.Conv_ovf_u_un:
                    // checked native-uint: same as u4_un.
                    if (stack.Count > 0)
                    {
                        var top = stack.Pop();
                        var conv = new CallExpr { Function = "il2cpp::runtime::icall_conv_ovf_u4_u8" };
                        conv.Args.Add(top);
                        Push(conv, ins);
                    }
                    break;
                case ILOpCode.Conv_ovf_i:
                case ILOpCode.Conv_ovf_u:
                    // checked native-int conversions: on the 64-bit target
                    // native int IS int64/uint64, so the conversion is an
                    // exact identity (csc emits these only for IntPtr/UIntPtr
                    // arithmetic, which never narrows).
                    break;
                case ILOpCode.Leave:
                case ILOpCode.Leave_s:
                    // leave = clear the evaluation stack, RUN the finally
                    // handlers of every enclosing try block being exited
                    // (innermost first) and jump to the target.
                    //
                    // The emitted C++ shape is "try block inline, handler
                    // after it", with a finally's body duplicated: the
                    // normal-path copy INSIDE the try block right after the
                    // try body, and the exceptional-path copy in the
                    // catch-all. A leave is therefore a pure no-op ONLY when
                    // falling out of the try body naturally lands exactly
                    // where the leave would go: the leave is the LAST
                    // instruction of the innermost exited try range and its
                    // target is that construct's chain end (or the method's
                    // terminal instruction). Everything else -- `return`
                    // inside a try, `break`/`continue` out of a try, a
                    // conditional early exit -- must run the exited finally
                    // bodies explicitly and then jump; that expansion is
                    // recorded here and materialized after the fold loop
                    // (the finally handler bodies are only folded later in
                    // the IL stream). See MaterializeLeaveSites.
                    {
                        var leaveTarget = (int)ins.Operand;
                        // Containment chain: every region whose try or handler
                        // contains the leave offset, innermost first (smallest
                        // extent -- deterministic regardless of the EH table
                        // order).
                        var chain = ctx.ExceptionRegions?
                            .Where(r =>
                                (ins.Offset >= r.TryOffset && ins.Offset < r.TryOffset + r.TryLength)
                                || (ins.Offset >= r.HandlerOffset && ins.Offset < r.HandlerOffset + r.HandlerLength))
                            .OrderBy(r => (r.TryLength + r.HandlerLength))
                            .ToList();
                        if (chain == null || chain.Count == 0)
                            throw new NotSupportedException(
                                $"leave at offset 0x{ins.Offset:X4} is outside any try/handler region (audit H2)");
                        // Sibling-catch chains: Roslyn ends EVERY handler of a
                        // multi-catch construct with a leave to the CHAIN end
                        // (past the last sibling), not to its own handler end
                        // (verified: exdeep f9 @0x153 -> 0x162, f17 @0x27f ->
                        // 0x28e, f18 @0x2a5 -> 0x2c1). And catch-then-finally
                        // constructs (CatchRet: catch leave @0x11 -> 0x1e = the
                        // FINALLY handler's end) leave to the OUTERMOST
                        // containing construct's end. Lock lowering (lockcase
                        // probe 2, nested lock) adds the MIDDLE case: the inner
                        // try body's normal-completion leave targets the
                        // IMMEDIATELY enclosing construct's chain end.
                        long ChainEnd(IlExceptionRegion r)
                        {
                            var e = r.HandlerOffset + r.HandlerLength;
                            if (ctx.ExceptionRegions != null)
                                foreach (var s in ctx.ExceptionRegions)
                                    if (s.TryOffset == r.TryOffset && s.TryLength == r.TryLength)
                                        e = Math.Max(e, s.HandlerOffset + s.HandlerLength);
                            return e;
                        }
                        var chainEnds = chain.Select(ChainEnd).ToList();
                        // batch 156c (asynccase): the async state machine's
                        // catch handler (and the AwaitUnsafeOnCompleted
                        // suspension path) `leave` to the METHOD'S FINAL ret
                        // -- the "return from the state machine" shape
                        // (SetResult must NOT run on the exception path).
                        var terminalOffset = il.Count > 0 ? il.Max(i => i.Offset) : -1;
                        // The target label is only emitted when an INSTRUCTION
                        // exists at the target offset; a jump to an offset with
                        // no instruction would render an undefined C++ label.
                        var targetHasInstruction = il.Any(x => x.Offset == leaveTarget);
                        var naturalTarget = chainEnds.Contains(leaveTarget) || leaveTarget == terminalOffset;
                        // A leave must never land INSIDE a handler clause (ECMA
                        // allows only targets outside every handler). Roslyn
                        // never emits one; a jump into a C++ catch body is
                        // illegal, so such an input keeps the old loud-ish
                        // "skip the fold" behavior instead of an unbuildable
                        // goto.
                        var targetInsideHandler = ctx.ExceptionRegions != null
                            && ctx.ExceptionRegions.Any(r =>
                                leaveTarget >= r.HandlerOffset
                                && leaveTarget < r.HandlerOffset + r.HandlerLength);

                        // Finally regions this leave EXITS: the leave sits in
                        // the region's TRY block and the target lies outside
                        // it. Nested try ranges are strictly contained, so
                        // ascending TryLength is innermost-first -- exactly the
                        // order the finallys must run in.
                        var exitedFinallys = chain
                            .Where(r => r.Kind == ExceptionRegionKind.Finally
                                        && ins.Offset >= r.TryOffset && ins.Offset < r.TryOffset + r.TryLength
                                        && !(leaveTarget >= r.TryOffset && leaveTarget < r.TryOffset + r.TryLength))
                            .OrderBy(r => r.TryLength)
                            .ToList();

                        // The END of the region body the leave sits in (the try
                        // range, or the handler range when the leave is inside a
                        // handler). The emitted C++ only falls through to the
                        // target when nothing else in that body still has to
                        // run, i.e. the leave is the body's LAST instruction.
                        // This single rule covers all three leave shapes:
                        //   * a normal-completion leave at the end of a try body
                        //     (Roslyn's usual shape) -- a no-op;
                        //   * a conditional `return`/`break`/`continue` in the
                        //     MIDDLE of a try body -- must jump (the rest of the
                        //     body is not part of that path);
                        //   * a leave at the end of a catch handler -- a no-op.
                        var innermost = chain[0];
                        var bodyEnd = ins.Offset >= innermost.HandlerOffset
                                      && ins.Offset < innermost.HandlerOffset + innermost.HandlerLength
                            ? innermost.HandlerOffset + innermost.HandlerLength
                            : innermost.TryOffset + innermost.TryLength;
                        if (!targetHasInstruction || (targetInsideHandler && !naturalTarget))
                        {
                            Console.Error.WriteLine($"warning: leave target 0x{leaveTarget:X4} is not a reachable label position in {ctx.MethodFullName} -- skipping fold");
                            return stmts;
                        }
                        // The "does the emitted shape already land there" decision
                        // is made AFTER the fold (MaterializeLeaveSites): only
                        // then is the full statement list known (a later `nop`
                        // run or a folded-away instruction must not count as
                        // "more code follows").
                        leaveSites.Add((stmts.Count, ins.Offset, leaveTarget, exitedFinallys, bodyEnd, innermost.TryOffset, naturalTarget));
                    }
                    break;
                case ILOpCode.Nop:
                case ILOpCode.Break:
                    // nop: no operation (debug/alignment placeholder).
                    // break (0x01): a debugger breakpoint -- no executable
                    // semantics in the generated C++ (a no-op, like nop).
                    break;
                case ILOpCode.Endfinally:
                    // endfinally: end marker of a finally block (M3 dual-path
                    // emission already handles finally semantics -- the normal
                    // path after the try, the exception path in
                    // catch-all+rethrow).
                    break;
                case ILOpCode.Pop:
                    // pop: discard the stack-top value (cleanup after exception
                    // handling / expression evaluation).
                    // sk6 (delegate2 e2e): a discarded NON-VOID delegate Invoke
                    // in statement position (`try { ex(); }` -- IntFn returns
                    // int) must NOT drop the call itself: the Invoke runs the
                    // multicast leaves (side effects) and can throw (the
                    // exception must propagate to the enclosing try/catch).
                    // DelegateInvokeExpr is pushed LAZILY (Push materializes
                    // only CallExpr/VirtualCallExpr/NewObjExpr into temps), so
                    // a bare pop silently discarded the whole call -- delegate2
                    // gen showed an empty try body. Emit the same dinvoke_
                    // CallStmt the void-delegate path produces (the emitter's
                    // dinvoke_ arm renders EmitExpr(dinvoke); -- the call runs,
                    // the result value is dropped). Pure value expressions
                    // (constants, arithmetic) remain dead and droppable.
                    if (stack.Count > 0)
                    {
                        var popped = stack.Pop();
                        if (popped is DelegateInvokeExpr poppedInvoke)
                        {
                            var stmt = new CallStmt
                            {
                                Function = $"dinvoke_{poppedInvoke.MethodToken}",
                                SourceOffset = ins.Offset,
                                SourceOpName = OpName(ins.OpCode)
                            };
                            stmt.Args.AddRange(poppedInvoke.Args);
                            stmts.Add(stmt);
                        }
                    }
                    break;
                case ILOpCode.Throw:
                {
                    // throw: pops the exception object. Audit H8: the object's
                    // type (tracked from its newobj) selects the managed
                    // exception type -- the throw site emits il2cpp_throw_new
                    // with the real type + base chain, so catch(derived type)
                    // matches. Unknown types (e.g. a throw of a call result)
                    // fall back to System.Exception.
                    // `throw null` (ldnull; throw, exdeep f16): .NET throws the
                    // null object, which the runtime converts to
                    // NullReferenceException (net48 empirical) -- type the
                    // null constant as NRE, whose baked chain equals the
                    // il2cpp_throw_null_reference_exception factory chain.
                    var thrown = stack.Pop();
                    var thrownType = thrown is NullExpr ? "System.NullReferenceException"
                        : thrown is LocExpr tl && objTypes.TryGetValue(tl.Name, out var tt)
                            ? tt
                            : "System.Exception";
                    var thrownStmt = new CallStmt { Function = $"throw_{thrownType}", SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) };
                    // excext (m7m): forward the newobj ctor args (message /
                    // paramName, captured in EmitNewObj) so the managed
                    // exception carrier carries the REAL message instead of
                    // the hardcoded "exception thrown".
                    // excmsg: also forward the ctor's PARAMETER TYPES for the
                    // Argument* family -- the throw site maps the overload
                    // shape to the right semantic slot (a single string is a
                    // paramName on ArgumentNullException, a message on
                    // ArgumentException, and (string, Exception) is always
                    // (message, inner)).
                    if (thrown is LocExpr tl2 && exceptionMessages.TryGetValue(tl2.Name, out var msgArgs))
                    {
                        thrownStmt.Args.AddRange(msgArgs);
                        if (exceptionCtorParamTypes.TryGetValue(tl2.Name, out var ctorPs))
                        {
                            thrownStmt.ThrowCtorParamTypes = ctorPs;
                        }
                    }
                    stmts.Add(thrownStmt);
                    break;
                }
                case ILOpCode.Rethrow:
                    // rethrow (0xFE1A, bare `throw;`): re-throws the current
                    // exception. The exception object is implicit (not on the
                    // IL stack) -- nothing is popped. Emitted as a call
                    // statement "rethrow"; CodeGenerator special-cases it to a
                    // C++ bare `throw;`: inside the generated catch
                    // (il2cpp::DotNetException& __il2cpp_ex) block, a bare
                    // throw; re-throws the current exception preserving its
                    // original type (.NET semantics; exdeep f6/f7/f21 depend on
                    // it).
                    stmts.Add(new CallStmt { Function = "rethrow", SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                    break;
                case ILOpCode.Ldftn:
                case ILOpCode.Ldvirtftn:
                    // ldftn/ldvirtftn: method token -> method pointer. M3:
                    // delegate scenario (the method address is taken before
                    // newobj). ldvirtftn additionally pops the receiver
                    // object (ECMA III.3.48) -- it becomes the delegate
                    // target, so the popped this is dropped (the emitted
                    // delegate ctor re-binds target from the stack in the
                    // newobj sequence). The token is carried by the dedicated
                    // FuncPtrExpr; the method CppName is resolved at emission
                    // time.
                    if (ins.OpCode == ILOpCode.Ldvirtftn && stack.Count > 0)
                    {
                        stack.Pop();
                    }
                    Push(new FuncPtrExpr { MethodToken = (int)ins.Operand }, ins);
                    break;
                case ILOpCode.Box:
                    // M2: box only supports int32 value types (type token =
                    // System.Int32).
                    // IR: CallExpr -> il2cpp_box_int32(&box_class, value).
                    // M5 wiring: box result types are recorded in objTypes so
                    // later calls on the boxed value (WriteLine(object) with a
                    // known box type, ToString on a boxed primitive) resolve
                    // through the receiver-type-aware path.
                    {
                        var boxTypeName = TypeFullNameFromToken(md, (int)ins.Operand);
                        // RF-REFL: the box of a ValueTuple is a PASSTHROUGH in
                        // this model (the emitter's box_ switch maps the
                        // GENERICINST to System.Object), so the result temp must
                        // be declared with the tuple STRUCT type -- otherwise
                        // the "r" (Il2CppObject*) temp cannot hold the value and
                        // the tuple member helpers fail to compile (vtmeta1).
                        var isVtBox = ValueTupleSupport.IsValueTupleName(boxTypeName);
                        var boxed = new CallExpr { Function = $"box_{ins.Operand}", ReturnsRef = !isVtBox };
                        boxed.Args.Add(stack.Pop());
                        // A ValueTuple box is a NON-ref carrier: RootClearPass
                        // must not emit a `t = nullptr` rootclear (it would
                        // overwrite the struct kind with "ref" -- vtmeta1).
                        if (isVtBox)
                            boxed.ResultKind = "vt:" + boxTypeName;
                        Push(boxed, ins);
                        if (stack.Peek() is LocExpr boxTemp)
                            objTypes[boxTemp.Name] = TypeFullNameFromToken(md, (int)ins.Operand);
                    }
                    break;
                case ILOpCode.Unbox_any:
                case ILOpCode.Unbox:
                    // M2: unbox only supports int32. IR: CallExpr ->
                    // il2cpp_unbox_int32(obj).
                    // M5 wiring: the unboxed VALUE keeps the boxed type's width
                    // (unbox double -> double temp, int64 -> int64_t temp).
                    {
                        var unboxType = TypeFullNameFromToken(md, (int)ins.Operand);
                        // P25b (user struct box): a USER value type (a TypeDef
                        // with a System.ValueType base -- NOT an enum, NOT a BCL
                        // struct) unboxes to the C++ struct type. The ResultKind
                        // carries the struct's C++ name; CodeGenerator.ExprKind
                        // maps the "us:" prefix to the declaration type and the
                        // unbox_ emit site casts the runtime payload pointer.
                        string? kind = unboxType is "System.Double" or "System.Single" ? "d"
                            : unboxType is "System.Int64" or "System.UInt64" ? "l"
                            // decimal-domain T3: a boxed decimal unboxes to the
                            // 16-byte value ("dec"-kind temp declaration).
                            : unboxType is "System.Decimal" ? "dec"
                            // boxwire (nullable GN-2 follow-up): the three BCL
                            // value structs unbox to their runtime value types
                            // -- "dt"/"ts" declare Il2CppDateTime/Il2CppTimeSpan,
                            // "guid" declares Il2CppGuid (the same kinds the
                            // boxed-side icall results use). Without these the
                            // temp collapsed to int32_t and truncated the
                            // 8/16-byte payload even once the unbox helper was
                            // wired.
                            : unboxType is "System.DateTime" ? "dt"
                            : unboxType is "System.TimeSpan" ? "ts"
                            : unboxType is "System.Guid" ? "guid"
                            // dtoiface: the 16-byte DateTimeOffset unbox temp
                            // must be the runtime value struct too -- without
                            // this the temp collapsed to int32_t and the
                            // icall_unbox_date_time_offset result could not be
                            // assigned (a compile error, proven by
                            // tests/cases/dtoiformattable1).
                            : unboxType is "System.DateTimeOffset" ? "dto"
                            : UnboxUserStructKind(unboxType, types);
                        Push(new CallExpr { Function = $"unbox_{ins.Operand}", ReturnsRef = false, ResultKind = kind, Args = { stack.Pop() } }, ins);
                    }
                    break;
                case ILOpCode.Isinst:
                    // isinst: obj -> il2cpp_is_instance(obj, &Type_class).
                    Push(new CallExpr { Function = $"isinst_{ins.Operand}", ReturnsRef = true, Args = { stack.Pop() } }, ins);
                    break;
                case ILOpCode.Castclass:
                    // castclass: obj -> type validation (M2: isinst check;
                    // false is InvalidCast).
                    // M3 Task 8 + delechain: the cast is an IDENTITY for the
                    // object pointer (the runtime validates, then returns the
                    // same object) -- the operand's tracked type follows into
                    // the result temp. csc emits `Func<int,int> ab = a + b`
                    // as Combine + castclass + stloc, so without this the
                    // combine result is untracked and later receiver-typed
                    // folds (Equals -> icall_delegate_equals) miss it.
                    {
                        var ccArg = stack.Pop();
                        Push(new CallExpr { Function = $"castclass_{ins.Operand}", ReturnsRef = true, Args = { ccArg } }, ins);
                        if (stack.Peek() is LocExpr ccres)
                        {
                            string? ccType = (ccArg is LocExpr ccl && objTypes.TryGetValue(ccl.Name, out var cct)) ? cct : null;
                            // nb6 OBJ-2: a cast from an UNTRACKED (or merely
                            // object-typed) receiver -- the foreach element
                            // conversion `object IEnumerator::get_Current();
                            // castclass Match` -- carries no hint, so the
                            // cast TARGET type is the only static type the
                            // result has. Fall back to the token type so a
                            // receiver-typed member on the cast result
                            // (Match.Value) resolves.
                            if (ccType == null || ccType == "System.Object")
                            {
                                var target = TypeFullNameFromToken(md, (int)ins.Operand);
                                if (target != null) ccType = target;
                            }
                            if (ccType != null) objTypes[ccres.Name] = ccType;
                        }
                    }
                    break;
                case ILOpCode.Newarr:
                    // newarr: pops the length -> allocates the array. M3:
                    // int32 arrays (type token = Int32).
                    // IR: CallExpr -> il2cpp_array_new(nullptr, 4, false, length).
                    {
                        var newArr = new CallExpr { Function = $"newarr_{ins.Operand}", ReturnsRef = true };
                        newArr.Args.Add(stack.Pop());
                        Push(newArr, ins);
                        // ilistException: `var arr = new Exception[2]` --
                        // record the SZARRAY element type on the result temp
                        // ("System.Exception[]", the ToArray convention) so a
                        // later ldelem_ref off this array types its result
                        // (arr[0].Message needs the element hint or it dies
                        // at the P26 phantom guard). The temp flows into the
                        // named local through the standard LocExpr store path.
                        // GN-18 B5 (fnptr ARRAY ELEMENT): a `delegate*[]` newarr
                        // records the DISTINCT marker "FNPTR[]" so a later
                        // ldelem/stelem on the array routes the element as a
                        // void* ("p" slot) instead of the fixed-width "i"
                        // (int64_t) arm -- the fnptr TypeSpecName fallback
                        // would mislabel it "TaskAwaiter`1[]".
                        if (md != null && stack.Peek() is LocExpr nalt)
                        {
                            try
                            {
                                objTypes[nalt.Name] = IsFnPtrTypeToken(md, (int)ins.Operand)
                                    ? "FNPTR[]"
                                    : TypeFullNameFromToken(md, (int)ins.Operand) + "[]";
                            }
                            catch { }
                        }
                    }
                    break;
                case ILOpCode.Ldlen:
                    // ldlen: array reference -> length. IR: CallExpr ->
                    // il2cpp_array_get_length(arr).
                    Push(new CallExpr { Function = $"ldlen_{ins.Operand}", ReturnsRef = false, Args = { stack.Pop() } }, ins);
                    break;
                case ILOpCode.Ldelem_i4:
                case ILOpCode.Ldelem_ref:
                case ILOpCode.Ldelem_u1:
                case ILOpCode.Ldelem_i1:
                case ILOpCode.Ldelem_u2:
                case ILOpCode.Ldelem_i2:
                case ILOpCode.Ldelem_u4:
                case ILOpCode.Ldelem_i8:
                case ILOpCode.Ldelem_i:
                case ILOpCode.Ldelem_r4:
                case ILOpCode.Ldelem_r8:
                    // ldelem: pops the index + array -> element value. IR:
                    // CallExpr -> il2cpp_array_get_elem_ptr read.
                    // The function name encodes the OPCODE (not the token) --
                    // the emitter decides from it whether the element is a
                    // reference (audit C4 fix: the old `ldelem_{token}`
                    // encoding meant "ldelem_ref" never matched and ldelem_ref
                    // on reference arrays was always read as int32).
                    // b23 (arrayops): the M3 fixed-width set now covers the
                    // full element width family -- Ldelem_u4 (uint32), Ldelem_i8
                    // (int64/uint64; the CodeGenerator "i8" arm reads
                    // int64_t) and Ldelem_r4/r8 (float/double reads into a
                    // double slot). The receiving temp must be 64-bit typed
                    // (ResultKind "l"/"d") or a long[] element read truncates
                    // into the int32 slot before the consumer sees it.
                    {
                        var ldIdx = stack.Pop();
                        var ldArr = stack.Pop();
                        var ldelemIsRef = ins.OpCode == ILOpCode.Ldelem_ref;
                        // GN-18 B5 (fnptr array element): a `delegate*[]`
                        // SZARRAY read -- the element is a void* ("p" slot),
                        // NOT the fixed-width "i" (int64_t) read of IntPtr/long
                        // arrays. Dispatch on the newarr marker recorded in
                        // objTypes ("FNPTR[]") to the dedicated "fnptr" ldelem
                        // arm. ResultKind "p" types the receiving temp void*
                        // (the calli/null-check consumers ride the pointer).
                        var ldelemIsFnPtr = !ldelemIsRef
                            && ldArr is LocExpr lafd && objTypes.TryGetValue(lafd.Name, out var laft) && laft == "FNPTR[]";
                        var ld = new CallExpr
                        {
                            Function = ldelemIsFnPtr ? "ldelem_fnptr" : $"ldelem_{ins.OpCode}",
                            ReturnsRef = ldelemIsRef,
                            ResultKind = ldelemIsFnPtr ? "p" : ldelemIsRef ? null : LdelemResultKind(ins.OpCode),
                        };
                        ld.Args.Add(ldArr);
                        ld.Args.Add(ldIdx);
                        Push(ld, ins);
                        // ilistException: `arr[0].Message` -- a reference
                        // element read off a type-tracked array
                        // ("System.Exception[]" from the newarr site or a
                        // ToArray-style ReturnTypeName) types the result temp
                        // with the ELEMENT type, so later calls on it
                        // (get_Message) resolve with a hint.
                        if (ldelemIsRef && ldArr is LocExpr laa
                            && objTypes.TryGetValue(laa.Name, out var aat)
                            && aat.EndsWith("[]", StringComparison.Ordinal)
                            && stack.Peek() is LocExpr ldr)
                            objTypes[ldr.Name] = aat[..^2];
                    }
                    break;
                case ILOpCode.Stelem_i4:
                case ILOpCode.Stelem_ref:
                case ILOpCode.Stelem_i1:
                case ILOpCode.Stelem_i2:
                case ILOpCode.Stelem_i8:
                case ILOpCode.Stelem_i:
                case ILOpCode.Stelem_r4:
                case ILOpCode.Stelem_r8:
                    // stelem: pops the value + index + array -> writes.
                    // b23: the fixed-width set covers the full width family
                    // (i8/r4/r8 -- the CodeGenerator arms emit the typed
                    // store).
                    {
                        var stVal = stack.Pop();
                        var stIdx = stack.Pop();
                        var stArr = stack.Pop();
                        var stelemIsRef = ins.OpCode == ILOpCode.Stelem_ref;
                        // GN-18 B5 (fnptr array element): a `delegate*[]`
                        // SZARRAY store -- the value is a void* ("p" slot),
                        // NOT the fixed-width "i" (int64_t) write of
                        // IntPtr/long arrays. Dispatch on the newarr marker
                        // recorded in objTypes ("FNPTR[]") to the dedicated
                        // "fnptr" stelem arm (which writes through void**).
                        var stelemIsFnPtr = !stelemIsRef
                            && stArr is LocExpr safd && objTypes.TryGetValue(safd.Name, out var saft) && saft == "FNPTR[]";
                        var st = new CallExpr { Function = stelemIsFnPtr ? "stelem_fnptr" : $"stelem_{ins.OpCode}", ReturnsRef = false };
                        st.Args.Add(stArr);
                        st.Args.Add(stIdx);
                        st.Args.Add(stVal);
                        // The write has side effects: emitted as a statement
                        // (Push would create a temp and keep the value -- a
                        // side-effecting statement is more correctly a direct
                        // CallStmt; but stelem's result never enters the stack,
                        // so use CallStmt).
                        var stmt = new CallStmt { Function = st.Function, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) };
                        stmt.Args.AddRange(st.Args);
                        stmts.Add(stmt);
                    }
                    break;
                case ILOpCode.Ldind_i1:
                case ILOpCode.Ldind_u1:
                case ILOpCode.Ldind_i2:
                case ILOpCode.Ldind_u2:
                case ILOpCode.Ldind_i4:
                case ILOpCode.Ldind_u4:
                case ILOpCode.Ldind_i8:
                case ILOpCode.Ldind_i:
                case ILOpCode.Ldind_r4:
                case ILOpCode.Ldind_r8:
                case ILOpCode.Ldind_ref:
                    // ldind.* (ECMA-335 III.4.5, pointer dereference family):
                    // pop the ADDRESS, push the value loaded through it. IL gap
                    // batch b23: the decoder reads these (no operand) and the
                    // fold carries them as CallExpr{Function="ldind_{op}"} --
                    // the emitter substitutes a typed dereference
                    // (*reinterpret_cast<T*>(addr)) [CodeGenerator patch,
                    // documented in the ilop2 report]. ResultKind/ReturnsRef
                    // type the receiving temp (int64 -> "l", float/double ->
                    // "d", native int -> "ptr", ref -> a GC reference).
                    Push(new CallExpr
                    {
                        Function = $"ldind_{ins.OpCode}",
                        ReturnsRef = ins.OpCode == ILOpCode.Ldind_ref,
                        ResultKind = LdindResultKind(ins.OpCode),
                        Args = { stack.Pop() },
                    }, ins);
                    break;
                case ILOpCode.Stind_ref:
                case ILOpCode.Stind_i1:
                case ILOpCode.Stind_i2:
                case ILOpCode.Stind_i4:
                case ILOpCode.Stind_i8:
                case ILOpCode.Stind_r4:
                case ILOpCode.Stind_r8:
                case ILOpCode.Stind_i:
                    // stind.* (ECMA-335 III.4.6): pop the VALUE then the
                    // ADDRESS (IL pushes the address first, the value on top),
                    // store the value through the address. Side effect ->
                    // CallStmt; the emitter substitutes
                    // (*reinterpret_cast<T*>(addr) = value).
                    {
                        var stindValue = stack.Pop();
                        var stindAddr = stack.Pop();
                        // impl-sb (B): the C# 13 span-params lowering stores
                        // each argument through an InlineArrayElementRef byref
                        // (the "__iae_<local>_<index>" LocAddrExpr marker the
                        // call arm pushed). Record the element instead of
                        // emitting a store through the unrepresentable marker
                        // -- the InlineArrayAsReadOnlySpan materialization
                        // rebuilds the object[] from these. A marker consumed
                        // by anything else keeps the LOUD default (the emitter
                        // rejects the unknown symbol at cmake).
                        if (stindAddr is LocAddrExpr iaeMarker
                            && iaeMarker.Name.StartsWith("__iae_", StringComparison.Ordinal)
                            && TryParseInlineArrayMarker(iaeMarker.Name, out var iaeLocal, out var iaeIndex))
                        {
                            if (!spanElements.TryGetValue(iaeLocal, out var iaeSlot))
                            {
                                iaeSlot = new SortedDictionary<int, Expr>();
                                spanElements[iaeLocal] = iaeSlot;
                            }
                            iaeSlot[iaeIndex] = stindValue;
                            break;
                        }
                        stmts.Add(new CallStmt
                        {
                            Function = $"stind_{ins.OpCode}",
                            Args = { stindAddr, stindValue },
                            SourceOffset = ins.Offset,
                            SourceOpName = OpName(ins.OpCode),
                        });
                    }
                    break;
                case ILOpCode.Ldobj:
                    // ldobj <T> (0x71, ECMA-335 III.4.5 -- value-type load by
                    // type token): pop the ADDRESS, push the T value. The same
                    // shape as ldind.* but the width comes from the type TOKEN
                    // (md required). Folded as CallExpr{Function="ldobj_{token}"}
                    // -- the emitter patch resolves the token to the C++ type.
                    {
                        var ldobjName = RequireTypeName(ins);
                        var ldobjIsRef = IsRefTypeName(ldobjName, types);
                        Push(new CallExpr
                        {
                            Function = $"ldobj_{ins.Operand}",
                            ReturnsRef = ldobjIsRef,
                            ResultKind = ldobjIsRef ? null : ldobjName switch
                            {
                                "System.Double" or "System.Single" => "d",
                                "System.Int64" or "System.UInt64" => "l",
                                "System.Decimal" => "dec",
                                _ => null,
                            },
                            Args = { stack.Pop() },
                        }, ins);
                    }
                    break;
                case ILOpCode.Stobj:
                    // stobj <T> (0x81, ECMA-335 III.4.6): pop the VALUE then
                    // the ADDRESS, store the T value through the address.
                    {
                        _ = RequireTypeName(ins);
                        var stobjValue = stack.Pop();
                        var stobjAddr = stack.Pop();
                        stmts.Add(new CallStmt
                        {
                            Function = $"stobj_{ins.Operand}",
                            Args = { stobjAddr, stobjValue },
                            SourceOffset = ins.Offset,
                            SourceOpName = OpName(ins.OpCode),
                        });
                    }
                    break;
                case ILOpCode.Initobj:
                    // initobj <T> (0xFE15, ECMA-335 III.4.9 -- zero-init the
                    // value type at the address): pop the ADDRESS. A CallStmt;
                    // the emitter patch zero-fills the slot (int/ref -> *addr =
                    // 0 / nullptr, structs -> memset).
                    {
                        _ = RequireTypeName(ins);
                        stmts.Add(new CallStmt
                        {
                            Function = $"initobj_{ins.Operand}",
                            Args = { stack.Pop() },
                            SourceOffset = ins.Offset,
                            SourceOpName = OpName(ins.OpCode),
                        });
                    }
                    break;
                case ILOpCode.Sizeof:
                    // sizeof <T> (0xFE1C, ECMA-335 III.4.33): push the size of
                    // the value type T in bytes. Primitive widths are known
                    // constants at fold time (ElementWidth); user value types
                    // fold to CallExpr{Function="sizeof_{token}"} -- the
                    // emitter patch substitutes sizeof({TypeCppName}) on the
                    // generated struct (its C++ size equals the transpiled
                    // managed size).
                    {
                        var sizeofName = RequireTypeName(ins);
                        var sizeofWidth = ElementWidth(sizeofName);
                        if (sizeofWidth != null)
                        {
                            Push(new IntConstExpr { Value = sizeofWidth.Value }, ins);
                        }
                        else
                        {
                            Push(new CallExpr { Function = $"sizeof_{ins.Operand}" }, ins);
                        }
                    }
                    break;
                case ILOpCode.Cpobj:
                    // cpobj <T> (0x70, ECMA-335 III.4.8 -- copy a value type):
                    // pop SRC then DEST (IL pushes dest first), copy the T
                    // value. CallStmt; the emitter patch emits a typed copy
                    // (*dest = *src / memcpy for structs).
                    {
                        var cpSrc = stack.Pop();
                        var cpDest = stack.Pop();
                        stmts.Add(new CallStmt
                        {
                            Function = $"cpobj_{ins.Operand}",
                            Args = { cpDest, cpSrc },
                            SourceOffset = ins.Offset,
                            SourceOpName = OpName(ins.OpCode),
                        });
                    }
                    break;
                case ILOpCode.Ldelema:
                    // ldelema <T> (0x8F): pop the index + array, push the
                    // ADDRESS of element [idx]. The element type token flows to
                    // the emitter (the address read is width-independent --
                    // il2cpp_array_get_elem_ptr), so no type resolution is
                    // needed at fold time. ResultKind "ptr" (intptr_t temp).
                    // O3 (user-struct arrays): a TRANSLATED user value type's
                    // element address is a typed STRUCT pointer (the caller
                    // dereferences `->field` at the address, not an intptr_t
                    // carrier), so the result carries "sp:{CppName}" -- the
                    // emitter emits reinterpret_cast<{CppName}*> and the
                    // loc-kind analysis declares the temp `{CppName}*` (the
                    // "us:" unbox precedent). Everything else keeps "ptr"
                    // (intptr_t carrier, unchanged).
                    //
                    // b5-gaps 13: the MULTIDIM form carries RANK indices (the
                    // array temp's "[<elem><rank>]" objTypes token records it
                    // -- `arr[i,j] = new T(...)` lowers to ldelema + the
                    // in-place ctor writing through the element address). The
                    // address expression routes to the rank-fixed
                    // il2cpp_array_md_elem_ptr_<rank>d helper; an untracked
                    // receiver keeps the 1-D interpretation (unchanged).
                    {
                        if (md != null && stack.Count > 1
                            && stack.Peek() is LocExpr mdLArr
                            && objTypes.TryGetValue(mdLArr.Name, out var mdLTok)
                            && mdLTok.Length >= 4 && mdLTok[0] == '[' && mdLTok[^1] == ']'
                            && int.TryParse(mdLTok[1..^1][MultidimElemNamePrefixLen(mdLTok[1..^1])..], out var mdLdRank)
                            && mdLdRank >= 2)
                        {
                            var mdLdIdxs = new Expr[mdLdRank];
                            for (var k = mdLdRank - 1; k >= 0; k--) mdLdIdxs[k] = stack.Pop();
                            var mdLdArr = stack.Pop();
                            var mdLdCall = new CallExpr { Function = $"ldelema_md_{mdLdRank}", ResultKind = "ptr" };
                            mdLdCall.Args.Add(mdLdArr);
                            mdLdCall.Args.AddRange(mdLdIdxs);
                            Push(mdLdCall, ins);
                            break;
                        }
                        var ldelemaIdx = stack.Pop();
                        var ldelemaArr = stack.Pop();
                        Push(new CallExpr
                        {
                            Function = $"ldelema_{ins.Operand}",
                            ResultKind = LdelemaUserStructKind(md, (int)ins.Operand, types) ?? "ptr",
                            Args = { ldelemaArr, ldelemaIdx },
                        }, ins);
                    }
                    break;
                case ILOpCode.Ldelem:
                    // ldelem <T> (0xA3 -- the generic-token form): pop index +
                    // array, push element [idx]. The element type comes from the
                    // TypeDefOrRef/TypeSpec TOKEN. Primitive and reference
                    // elements map onto the EXISTING fixed-width ldelem
                    // emission (ldelem_i1..ref -- no CodeGenerator change);
                    // 8-byte primitives and user value types have no fixed-width
                    // emitter arm -> LOUD (the report documents the token-based
                    // emitter patch).
                    {
                        // batch 2026-10-05 (iopairprobe B_decimal): a decimal[]
                        // element load has no fixed-width opcode -- route to the
                        // dedicated 16-byte "dec" reader arm.
                        if (IsDecimalElement(md, (int)ins.Operand, types))
                        {
                            var decIdx = stack.Pop();
                            var decArr = stack.Pop();
                            Push(new CallExpr
                            {
                                Function = "ldelem_dec",
                                ResultKind = "dec",
                                Args = { decArr, decIdx },
                            }, ins);
                            break;
                        }
                        // LOUD P0-02/03: DateTime/TimeSpan 8-byte cells and Guid
                        // 16-byte cells have no fixed-width opcode either.
                        if (IsTokenElement(md, (int)ins.Operand, types, "System.Guid"))
                        {
                            var gIdx = stack.Pop();
                            var gArr = stack.Pop();
                            Push(new CallExpr
                            {
                                Function = "ldelem_guid",
                                ResultKind = "guid",
                                Args = { gArr, gIdx },
                            }, ins);
                            break;
                        }
                        if (IsTokenElement(md, (int)ins.Operand, types, "System.DateTime")
                            || IsTokenElement(md, (int)ins.Operand, types, "System.TimeSpan"))
                        {
                            var isDt = IsTokenElement(md, (int)ins.Operand, types, "System.DateTime");
                            var vIdx = stack.Pop();
                            var vArr = stack.Pop();
                            Push(new CallExpr
                            {
                                Function = isDt ? "ldelem_dt" : "ldelem_ts",
                                ResultKind = isDt ? "dt" : "ts",
                                Args = { vArr, vIdx },
                            }, ins);
                            break;
                        }
                        // LOUD P0-02/03 residual: the 16-byte DateTimeOffset
                        // value element (il2cpp_dtoffset.h) reads through the
                        // "dto" token arm.
                        if (IsTokenElement(md, (int)ins.Operand, types, "System.DateTimeOffset"))
                        {
                            var dIdx = stack.Pop();
                            var dArr = stack.Pop();
                            Push(new CallExpr
                            {
                                Function = "ldelem_dto",
                                ResultKind = "dto",
                                Args = { dArr, dIdx },
                            }, ins);
                            break;
                        }
                        var genericLdOp = MapGenericLdelemOp(md, (int)ins.Operand, types);
                        if (genericLdOp == null)
                        {
                            // p0linq4 (D1): a USER VALUE TYPE element read -- a
                            // typed struct copy from the element pointer.
                            if (UserStructElementCpp(md, (int)ins.Operand, types) is { } usLdCpp)
                            {
                                var usLdIdx = stack.Pop();
                                var usLdArr = stack.Pop();
                                Push(new CallExpr
                                {
                                    Function = "ldelem_us_" + usLdCpp,
                                    ResultKind = "us:" + usLdCpp,
                                    Args = { usLdArr, usLdIdx },
                                }, ins);
                                break;
                            }
                            throw new NotSupportedException(
                                $"ldelem at offset 0x{ins.Offset:X4}: element type needs the token-based emitter (8-byte primitive or user value type) -- CodeGenerator patch, see the ilop2 report");
                        }
                        var genericLdIdx = stack.Pop();
                        var genericLdArr = stack.Pop();
                        var genericLdRef = genericLdOp == ILOpCode.Ldelem_ref;
                        Push(new CallExpr
                        {
                            Function = $"ldelem_{genericLdOp}",
                            ReturnsRef = genericLdRef,
                            ResultKind = genericLdRef ? null : LdelemResultKind(genericLdOp!.Value),
                            Args = { genericLdArr, genericLdIdx },
                        }, ins);
                    }
                    break;
                case ILOpCode.Stelem:
                    // stelem <T> (0xA4 -- the generic-token form): pop the
                    // value + index + array, write element [idx]. Same token
                    // classification as Ldelem; the mapped fixed-width stelem
                    // reuses the existing emission.
                    {
                        // batch 2026-10-05 (iopairprobe B_decimal): a decimal[]
                        // element store has no fixed-width opcode -- route to
                        // the dedicated 16-byte "dec" writer arm.
                        if (IsDecimalElement(md, (int)ins.Operand, types))
                        {
                            var decVal = stack.Pop();
                            var decIdx = stack.Pop();
                            var decArr = stack.Pop();
                            stmts.Add(new CallStmt
                            {
                                Function = "stelem_dec",
                                Args = { decArr, decIdx, decVal },
                                SourceOffset = ins.Offset,
                                SourceOpName = OpName(ins.OpCode),
                            });
                            break;
                        }
                        // LOUD P0-02/03: DateTime/TimeSpan 8-byte cells and Guid
                        // 16-byte cells -- dedicated token writer arms.
                        // DateTime[] uses the CLR packed image (ticks + kind);
                        // Guid[] is the raw 16-byte memcpy.
                        var vElemName = IsTokenElement(md, (int)ins.Operand, types, "System.Guid")
                            ? "System.Guid"
                            : IsTokenElement(md, (int)ins.Operand, types, "System.DateTime")
                            ? "System.DateTime"
                            : IsTokenElement(md, (int)ins.Operand, types, "System.TimeSpan")
                            ? "System.TimeSpan"
                            : IsTokenElement(md, (int)ins.Operand, types, "System.DateTimeOffset")
                            ? "System.DateTimeOffset"
                            : null;
                        if (vElemName != null)
                        {
                            var vVal = stack.Pop();
                            var vIdx = stack.Pop();
                            var vArr = stack.Pop();
                            stmts.Add(new CallStmt
                            {
                                Function = vElemName switch
                                {
                                    "System.Guid" => "stelem_guid",
                                    "System.DateTime" => "stelem_dt",
                                    "System.DateTimeOffset" => "stelem_dto",
                                    _ => "stelem_ts",
                                },
                                Args = { vArr, vIdx, vVal },
                                SourceOffset = ins.Offset,
                                SourceOpName = OpName(ins.OpCode),
                            });
                            break;
                        }
                        var genericStOp = MapGenericStelemOp(md, (int)ins.Operand, types);
                        if (genericStOp == null)
                        {
                            // p0linq4 (D1): a USER VALUE TYPE element (a
                            // translated struct) -- the element access is a
                            // typed struct assignment through the element
                            // pointer (the C++ type is carried in the marker
                            // name; the emitter renders the dereference).
                            if (UserStructElementCpp(md, (int)ins.Operand, types) is { } usStCpp)
                            {
                                var usStVal = stack.Pop();
                                var usStIdx = stack.Pop();
                                var usStArr = stack.Pop();
                                stmts.Add(new CallStmt
                                {
                                    Function = "stelem_us_" + usStCpp,
                                    Args = { usStArr, usStIdx, usStVal },
                                    SourceOffset = ins.Offset,
                                    SourceOpName = OpName(ins.OpCode),
                                });
                                break;
                            }
                            throw new NotSupportedException(
                                $"stelem at offset 0x{ins.Offset:X4}: element type needs the token-based emitter (8-byte primitive or user value type) -- CodeGenerator patch, see the ilop2 report");
                        }
                        var genericStVal = stack.Pop();
                        var genericStIdx = stack.Pop();
                        var genericStArr = stack.Pop();
                        var genericSt = new CallStmt
                        {
                            Function = $"stelem_{genericStOp}",
                            SourceOffset = ins.Offset,
                            SourceOpName = OpName(ins.OpCode),
                        };
                        genericSt.Args.Add(genericStArr);
                        genericSt.Args.Add(genericStIdx);
                        genericSt.Args.Add(genericStVal);
                        stmts.Add(genericSt);
                    }
                    break;
                case ILOpCode.Cpblk:
                    // cpblk (0xFE17, ECMA-335 III.3.32 -- memory block copy):
                    // pop COUNT, SRC, DEST (IL pushes dest first, count last),
                    // copy count bytes. CallStmt; the emitter patch emits
                    // memcpy(dest, src, count).
                    {
                        var cpblkCount = stack.Pop();
                        var cpblkSrc = stack.Pop();
                        var cpblkDest = stack.Pop();
                        stmts.Add(new CallStmt
                        {
                            Function = "cpblk",
                            Args = { cpblkDest, cpblkSrc, cpblkCount },
                            SourceOffset = ins.Offset,
                            SourceOpName = OpName(ins.OpCode),
                        });
                    }
                    break;
                case ILOpCode.Initblk:
                    // initblk (0xFE18, ECMA-335 III.3.46 -- memory block fill):
                    // pop COUNT, VALUE (a byte), DEST, fill count bytes with the
                    // byte value. CallStmt; the emitter patch emits
                    // memset(dest, value, count).
                    {
                        var initblkCount = stack.Pop();
                        var initblkValue = stack.Pop();
                        var initblkDest = stack.Pop();
                        stmts.Add(new CallStmt
                        {
                            Function = "initblk",
                            Args = { initblkDest, initblkValue, initblkCount },
                            SourceOffset = ins.Offset,
                            SourceOpName = OpName(ins.OpCode),
                        });
                    }
                    break;
                case ILOpCode.Localloc:
                    // localloc (0xFE0F, ECMA-335 III.3.41): pop the size, push
                    // a pointer to a block of that size allocated on the current
                    // STACK FRAME (the frame lives until the method returns --
                    // the .NET semantics; alloca must be emitted INLINE at the
                    // call site, so this folds to a marker CallExpr with a
                    // "ptr" result temp and the emitter patch substitutes
                    // alloca(static_cast<size_t>(size)) -- NOT a runtime
                    // helper, alloca cannot leave the caller's frame).
                    Push(new CallExpr
                    {
                        Function = "il2cpp::il2cpp_localloc",
                        ResultKind = "ptr",
                        Args = { stack.Pop() },
                    }, ins);
                    break;
                case ILOpCode.Ckfinite:
                    // ckfinite (0xC3, ECMA-335 III.3.20): pop the double; if it
                    // is not finite (NaN/Infinity) throw OverflowException, else
                    // push it back. Folded as a runtime conversion icall (the
                    // icall_conv_* precedent); the runtime patch adds
                    // icall_ckfinite(double) to the conv domain.
                    Push(new CallExpr
                    {
                        Function = "il2cpp::runtime::icall_ckfinite",
                        ResultKind = "d",
                        Args = { stack.Pop() },
                    }, ins);
                    break;
                case ILOpCode.Add_ovf:
                case ILOpCode.Add_ovf_un:
                case ILOpCode.Sub_ovf:
                case ILOpCode.Sub_ovf_un:
                case ILOpCode.Mul_ovf:
                case ILOpCode.Mul_ovf_un:
                    // checked arithmetic (ECMA-335 III.3.1/III.3.54/III.3.51):
                    // add.ovf/sub.ovf/mul.ovf throw OverflowException on
                    // overflow instead of wrapping; the _un forms compare
                    // UNSIGNED. The existing checked_div/checked_rem precedent
                    // (il2cpp_checked_div/rem in exception.cpp) extends to the
                    // whole family: each op folds to a runtime helper call
                    // (il2cpp_checked_add/sub/mul [+_un]) -- the runtime patch
                    // is documented in the ilop2 report. i64ck-b23: the fold
                    // carries the operand WIDTH -- 64-bit operands (int64/
                    // uint64, the "l" slot) set ResultKind "l" so the emitter
                    // routes to the il2cpp_checked_*_64 helpers (exception.cpp)
                    // and the receiving temp is typed int64_t (the int32
                    // helpers LOUD-rejected 64-bit operands).
                    {
                        var ovfRight = stack.Pop();
                        var ovfLeft = stack.Pop();
                        Push(new CallExpr
                        {
                            Function = $"il2cpp::il2cpp_checked_{CheckedOpName(ins.OpCode)}",
                            ResultKind = ExprWidth(ovfLeft, ctx, tempWidths) == "l"
                                || ExprWidth(ovfRight, ctx, tempWidths) == "l" ? "l" : null,
                            Args = { ovfLeft, ovfRight },
                        }, ins);
                    }
                    break;
                case ILOpCode.Rem_un:
                    // rem.un (0x5E, unsigned int32 remainder): the Div/Rem
                    // precedent substitutes a checked runtime helper at the
                    // DIVIDE-BY-ZERO case; the unsigned variant needs the
                    // uint32 modulo (il2cpp_checked_rem is signed) -- fold to
                    // the runtime helper il2cpp_checked_rem_un (runtime patch,
                    // see the ilop2 report). i64ck-b23: uint64 operands (kind
                    // "l") route to il2cpp_checked_rem_un_64 with ResultKind
                    // "l" (temp typed int64_t).
                    {
                        var remUnRight = stack.Pop();
                        var remUnLeft = stack.Pop();
                        Push(new CallExpr
                        {
                            Function = "il2cpp::il2cpp_checked_rem_un",
                            ResultKind = ExprWidth(remUnLeft, ctx, tempWidths) == "l"
                                || ExprWidth(remUnRight, ctx, tempWidths) == "l" ? "l" : null,
                            Args = { remUnLeft, remUnRight },
                        }, ins);
                    }
                    break;
                case ILOpCode.Jmp:
                    // jmp <method> (0x27, ECMA-335 III.3.39 -- tail jump to a
                    // method): transfer control to the target method, popping
                    // its arguments; the callee returns directly to the jmp's
                    // caller. This is EXACTLY a tail call (the transpiler's own
                    // TailCall loop handles self-recursion; any other target
                    // folds to CallStmt{Function="jmp_{token}"} and the emitter
                    // patch emits `return {CppName}(args...);` -- the callee's
                    // return value is the caller's (no stack frame between).
                    // The target's arity comes from the method token signature
                    // (md required).
                    {
                        if (md == null)
                            throw new NotSupportedException(
                                $"jmp at offset 0x{ins.Offset:X4}: the target arity requires metadata (pure-byte folding cannot decode the method signature)");
                        var jmpResolved = MethodRefResolver.Resolve(md, (int)ins.Operand, types ?? Array.Empty<TypeDef>());
                        var jmpArgs = new List<Expr>();
                        for (var i = 0; i < jmpResolved.ParamCount; i++) jmpArgs.Insert(0, stack.Pop());
                        if (jmpResolved.IsInstance)
                        {
                            if (stack.Count == 0)
                                throw new NotSupportedException($"jmp at offset 0x{ins.Offset:X4}: resolved {jmpResolved.CppName} as INSTANCE (pops this) but the stack is empty");
                            jmpArgs.Insert(0, stack.Pop());
                        }
                        var jmpStmt = new CallStmt
                        {
                            Function = $"jmp_{ins.Operand}",
                            SourceOffset = ins.Offset,
                            SourceOpName = OpName(ins.OpCode),
                        };
                        jmpStmt.Args.AddRange(jmpArgs);
                        stmts.Add(jmpStmt);
                    }
                    break;
                case ILOpCode.Arglist:
                    // arglist (0xFE00, ECMA-335 III.3.9 -- varargs): pushes the
                    // runtime varargs handle (System.RuntimeArgumentHandle) of
                    // the CURRENT method. The handle is modelled as an
                    // intptr_t: every varargs method receives one trailing
                    // `intptr_t va_frame` parameter (the caller-built typed
                    // frame: {types[], values[], count}), and the handle is
                    // exactly that parameter. The vararg CALLER packs the
                    // frame at the call site (the call-site signature carries
                    // the declared element types -- see
                    // MethodSignatureCoder.DecodeVarArgs), the callee's
                    // ArgIterator walks it (il2cpp_argiterator.h).
                    Push(new LocExpr { Name = "va_frame" }, ins);
                    break;
                case ILOpCode.Endfilter:
                    // endfilter (0xFE11, ECMA-335 III.3.21): pops the int32
                    // filter result and ends the filter clause. C# DOES emit
                    // filter clauses -- `catch (E e) when (cond)` -- so the
                    // result must feed the catch dispatch. (The earlier note
                    // claiming "VB-only, C# never produces them" was wrong;
                    // MEASURED on net48, .opencode/tmp/ehfilter.) The filter
                    // body is terminal: nothing of its stack survives into the
                    // handler, which starts from its own exception entry.
                    {
                        // ECMA-335 III.3.21: endfilter is legal ONLY inside a
                        // filter block. Rejecting it anywhere else keeps the
                        // "malformed IL" diagnostics honest (the old blanket
                        // LOUD also caught this shape, which is why the stub
                        // fixture in StubReportTests used it).
                        var inFilterBlock = ctx.ExceptionRegions != null
                            && ctx.ExceptionRegions.Any(r =>
                                r.Kind == ExceptionRegionKind.Filter
                                && ins.Offset >= r.FilterOffset && ins.Offset < r.HandlerOffset);
                        if (!inFilterBlock)
                            throw new NotSupportedException(
                                $"endfilter at offset 0x{ins.Offset:X4}: not inside an exception FILTER block " +
                                "(ECMA-335 III.3.21: endfilter is valid only as the last instruction of a filter clause)");
                        var filterResult = stack.Count > 0 ? stack.Pop() : new IntConstExpr { Value = 0 };
                        stack.Clear();
                        stmts.Add(new FilterReturnStmt
                        {
                            Value = filterResult,
                            SourceOffset = ins.Offset,
                            SourceOpName = OpName(ins.OpCode),
                        });
                    }
                    break;
                case ILOpCode.Mkrefany:
                    // mkrefany <token> (0xC6, ECMA-335 III.4.22): pop the
                    // ADDRESS of the referenced storage and push the
                    // TypedReference carrier { declared type, storage address }.
                    // The address producers are the existing ldloca/ldarga/
                    // ldsflda/ldflda/ldelema expressions -- mkrefany records
                    // the address, never a value copy (net48 alias semantics,
                    // probe-measured: writing the variable after __makeref is
                    // visible through __refvalue).
                    // ResultKind "tr" -> an Il2CppTypedReference slot.
                    {
                        _ = RequireTypeName(ins);
                        var mkrefAddr = stack.Pop();
                        Push(new CallExpr
                        {
                            Function = $"typedref_make_{ins.Operand}",
                            ResultKind = "tr",
                            Args = { mkrefAddr },
                        }, ins);
                    }
                    break;
                case ILOpCode.Refanyval:
                    // refanyval <token> (0xC2, ECMA-335 III.4.26): pop the
                    // carrier and push the storage ADDRESS presented as the
                    // token type. The result rides the existing address model
                    // (ldind/ldobj/ldfld/stind all reinterpret_cast their
                    // address operand): a translated user struct gets the
                    // typed "sp:<Cpp>" pointer kind (the ldelema precedent);
                    // everything else -- primitives, enums (the int32 carrier),
                    // reference types -- is the generic "p" void* slot.
                    // The runtime accessor enforces net48's EXACT type identity
                    // and throws System.InvalidCastException on a mismatch.
                    {
                        _ = RequireTypeName(ins);
                        var rvTr = stack.Pop();
                        Push(new CallExpr
                        {
                            Function = $"typedref_value_{ins.Operand}",
                            ResultKind = UserStructElementCpp(md, (int)ins.Operand, types) is { } rvCpp
                                ? "sp:" + rvCpp
                                : "p",
                            Args = { rvTr },
                        }, ins);
                    }
                    break;
                case ILOpCode.Refanytype:
                    // refanytype (0xFE1D, ECMA-335 III.4.23): pop the carrier
                    // and push the declared type's HANDLE (the descriptor
                    // address as an intptr_t). C# always consumes it with
                    // System.Type::GetTypeFromHandle, so the handle flows into
                    // the EXISTING type-handle path and comes out as the same
                    // full-name-string System.Type the typeof chain produces
                    // (R3 -- no second type object).
                    {
                        var rtTr = stack.Pop();
                        Push(new CallExpr
                        {
                            Function = "typedref_handle",
                            ResultKind = "ptr",
                            Args = { rtTr },
                        }, ins);
                    }
                    break;
                case ILOpCode.Ldtoken:
                    // typeof(T) support (reflection audit F1): ldtoken's
                    // operand is a metadata token of TypeDefOrRef / FieldDef /
                    // MethodDef kind -- the classification is the HANDLE kind,
                    // not the opcode. A TYPE token is the first half of
                    // typeof(T); the marker (IntConstExpr token) is folded to
                    // the type's FULL-NAME string at the consuming
                    // System.Type::GetTypeFromHandle call (EmitCall). FIELD
                    // tokens are the RuntimeHelpers.InitializeArray
                    // array-initializer pattern (net48 `new byte[] {...}`
                    // emits ldtoken<RVA field>; call InitializeArray,
                    // base64case) -- no fold, loud. METHOD tokens likewise.
                    if (md == null)
                        throw new NotSupportedException($"ldtoken at offset 0x{ins.Offset:X4}: metadata unavailable to classify token 0x{ins.Operand:X8}");
                    switch (MetadataTokens.Handle((int)ins.Operand).Kind)
                    {
                        case HandleKind.TypeDefinition:
                        case HandleKind.TypeReference:
                        // raudit1: typeof(List<int>) folds ldtoken with a
                        // TypeSpecification operand (GenericInst). The marker
                        // is the same; the name resolution happens at the
                        // GetTypeFromHandle fold below.
                        case HandleKind.TypeSpecification:
                            Push(new IntConstExpr { Value = ins.Operand }, ins); // marker for the GetTypeFromHandle fold
                            break;
                        case HandleKind.FieldDefinition:
                            // RuntimeHelpers.InitializeArray array-initializer
                            // pattern (net48 `new byte[] {...}` / `new int[]
                            // {...}`): ldtoken<RVA field>; call InitializeArray.
                            // The FIELD TOKEN marker is folded to a stelem
                            // sequence loaded from the field's RVA blob at the
                            // consuming InitializeArray call (EmitCall); any
                            // other consumer stays loud (the guard there).
                            Push(new IntConstExpr { Value = ins.Operand }, ins); // marker for the InitializeArray fold
                            break;
                        case HandleKind.MethodDefinition:
                            throw new NotSupportedException(
                                $"ldtoken METHOD token 0x{ins.Operand:X8} at offset 0x{ins.Offset:X4} not supported in M3");
                        default:
                            throw new NotSupportedException(
                                $"ldtoken token kind {MetadataTokens.Handle((int)ins.Operand).Kind} (0x{ins.Operand:X8}) at offset 0x{ins.Offset:X4} not supported in M3");
                    }
                    break;
                default:
                    throw new NotSupportedException($"fold: opcode {ins.OpCode} not supported in M3");
            }

            prevIns = ins;
        }

        // Materialize the explicit leave expansions recorded by the Leave arm.
        // Whether a leave needs one is decided HERE, on the complete statement
        // list: the emitted shape already lands on the target only when the
        // leave is the LAST emitted statement of the body it sits in and the
        // target is that construct's natural continuation. Everything else (a
        // conditional return/break/continue, an early exit) gets, innermost
        // finally first: a RENAMED copy of each exited finally body, then the
        // jump. Renaming keeps the copy from colliding with the finally body
        // the emitter duplicates for the normal/exceptional paths; the suffix
        // derives from the leave offset, so each site gets its own labels.
        if (leaveSites.Count > 0)
        {
            var natural = new bool[leaveSites.Count];
            for (var li = 0; li < leaveSites.Count; li++)
            {
                var s0 = leaveSites[li];
                // Statements after the leave but still inside the same body:
                // the emitted fall-through would run them, so the leave must
                // jump. Judged on EMITTED statements -- a trailing nop run, or
                // instructions that fold to nothing, do not count.
                var moreInBody = false;
                for (var j = s0.Index; j < stmts.Count; j++)
                {
                    if (stmts[j].SourceOffset > s0.Offset && stmts[j].SourceOffset < s0.BodyEnd)
                    {
                        moreInBody = true;
                        break;
                    }
                }
                // An exited finally must actually be emitted for the natural
                // shape to run it; when the try range produced no statement at
                // all (ehnest1: an empty try body), the emitter never opens the
                // try block and the finally copies are skipped entirely.
                var tryOpened = stmts.Any(s =>
                    s.SourceOffset >= s0.TryStart && s.SourceOffset < s0.BodyEnd
                    && s.SourceOffset != s0.Offset);
                natural[li] = !moreInBody && s0.NaturalTarget
                              && (s0.ExitedFinallys.Count == 0 || tryOpened);
            }

            // Insertion happens at a recorded statement index; process the
            // sites in REVERSE so an earlier insertion never shifts a later
            // index.
            for (var li = leaveSites.Count - 1; li >= 0; li--)
            {
                if (natural[li]) continue;
                var site = leaveSites[li];
                var expansion = new List<Stmt>();
                foreach (var fr in site.ExitedFinallys)
                {
                    var body = stmts
                        .Where(s => s.SourceOffset >= fr.HandlerOffset
                                 && s.SourceOffset < fr.HandlerOffset + fr.HandlerLength)
                        .ToList();
                    // Drop the body's own branch targets that stay inside it --
                    // they are re-registered by the label scan below.
                    var labelMap = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var bs in body)
                        if (bs is LabelStmt ls && !labelMap.ContainsKey(ls.Name))
                            labelMap[ls.Name] = $"{ls.Name}_lv{site.Offset:X4}";
                    foreach (var bs in body)
                    {
                        // DEEP clone: the handler statements stay in `stmts`
                        // (the emitter duplicates them for the normal and
                        // exceptional paths) and must keep their original
                        // handler offsets -- re-stamping a SHARED node would
                        // move the finally body out of its handler range and
                        // silently delete both emitted copies.
                        var copy = IrNode.CloneStmt(bs);
                        if (copy is LabelStmt ls2 && labelMap.TryGetValue(ls2.Name, out var nv))
                            copy = new LabelStmt { Name = nv, SourceOffset = site.Offset, SourceOpName = ls2.SourceOpName };
                        else if (copy is BranchStmt br)
                            copy = new BranchStmt
                            {
                                TargetLabel = labelMap.TryGetValue(br.TargetLabel, out var nv2) ? nv2 : br.TargetLabel,
                                Condition = br.Condition == null ? null : IrNode.CloneExpr(br.Condition),
                                SourceOffset = site.Offset,
                                SourceOpName = br.SourceOpName,
                            };
                        // Re-stamp every copied statement to the LEAVE offset:
                        // the emitter attributes EH wrappers by SourceOffset,
                        // and the copy executes in the try body, not in the
                        // handler (a handler offset would make the emitter skip
                        // it as a finally-body statement).
                        copy.SourceOffset = site.Offset;
                        expansion.Add(copy);
                    }
                }
                expansion.Add(new BranchStmt
                {
                    TargetLabel = labels[site.Target],
                    SourceOffset = site.Offset,
                    SourceOpName = "leave",
                });
                stmts.InsertRange(site.Index, expansion);
            }
        }

        // P0-2 dead-store removal: a local whose reads were all replaced by
        // constants has its sole stloc become "written-never-read" -- delete
        // that assignment, else the generated code has a set-but-unused local
        // (-Wunused-but-set-variable).
        // Conservative: delete only when the name has exactly one assignment in
        // the statement list (avoid mis-deleting a same-named join temp slot)
        // and zero remaining LocExpr references.
        foreach (var name in propagatedLocs)
        {
            AssignStmt? store = null;
            var storeCount = 0;
            foreach (var s in stmts)
            {
                if (s is not AssignStmt a || a.Target != name) continue;
                storeCount++;
                if (a.Value is IntConstExpr) store ??= a;
            }
            if (storeCount != 1 || store == null) continue;
            if (StmtReferencesName(stmts, name)) continue;
            stmts.Remove(store);
        }
        // cctor guard optimization (audit #5): inside a single-block loop with
        // no code that can trigger a cctor, hoist the loop's static-field
        // access guards to the loop preheader (conservative subset in the
        // method comment).
        // Tail-call optimization (recursion-audit section 6a): self-recursive tail
        // calls -> a goto loop, O(1) stack.
        // Must run AFTER OptimizeTemps/HoistLoopGuards: the optimizer inlines
        // "single-assigned + single-read" temps back into param slots
        // (`arg0 = <expr>`), regressing the alias bug -- TailSum's
        // `arg1 = arg1 + arg0` would read the already-updated arg0 (wrong
        // evaluation order). The temps this transformation creates stay pure
        // values via the IsPureValueArg gate and are declared int32_t at
        // emission, no GC roots.
        return ApplyTailCallLoop(HoistLoopGuards(OptimizeTemps(stmts), types), ctx, objTypes);

        // Value push: CallExpr/VirtualCallExpr/NewObjExpr results need a temp
        // (M2 simplification: one temp per call).
        // Reference-typed results (ReturnsRef CallExpr / VirtualCallExpr /
        // NewObjExpr) are declared Il2CppObject* at emission, inferred from the
        // AssignStmt's Value type (CodeGenerator.EmitBody's temp type analysis).
        void Push(Expr e, IlInstruction ins)
        {
            if (e is CallExpr or VirtualCallExpr or NewObjExpr)
            {
                var name = $"t{temps.Count}";
                temps.Add(name);
                // valdeep L26: record the temp's value width so a later
                // conv.u8 on it knows whether to zero-extend (int32) or
                // treat it as a bit identity (int64/double).
                tempWidths[name] = e switch
                {
                    CallExpr ce2 => ce2.ResultKind ?? "i",
                    VirtualCallExpr ve2 => ve2.ResultKind ?? (ve2.ReturnsRef ? "r" : "i"),
                    _ => "r",
                };
                stmts.Add(new AssignStmt
                {
                    Target = name,
                    Value = e,
                    SourceOffset = ins.Offset,
                    SourceOpName = OpName(ins.OpCode)
                });
                // tuple1 (Tuple/ValueTuple): a "vt:"-kind call result (the
                // generated tuple struct) materializes into this temp -- record
                // the tuple type so a later callvirt on the temp (Object::
                // ToString / GetHashCode, routed through System.Object by the
                // net48 reference assemblies) re-resolves with the receiver hint.
                var rkTuple = (e as CallExpr)?.ResultKind ?? (e as VirtualCallExpr)?.ResultKind;
                if (rkTuple is { } rk3 && rk3.StartsWith("vt:", StringComparison.Ordinal)
                    && ValueTupleSupport.IsValueTupleName(rk3["vt:".Length..]))
                    objTypes[name] = rk3["vt:".Length..];
                // FILTER clause (`catch (E e) when (...)`): csc binds the caught
                // variable through `isinst T; dup; brtrue; stloc`, and the
                // isinst result materializes into THIS temp before the store.
                // Carry the catch-carrier hint ("System.Type" -- the model's
                // exception-carrier convention) to the temp; without it the
                // later `e.Message` callvirt resolves with no receiver and dies
                // at the phantom-call guard.
                if (e is CallExpr isq && isq.Args.Count == 1 && isq.Args[0] is ExceptionEntryExpr
                    && isq.Function.StartsWith("isinst_", StringComparison.Ordinal))
                    objTypes[name] = "System.Type";
                // p-refl2-gapmatrix (B08): a folded typeof-chain GetMethod/
                // GetField result materializes into this temp -- carry its
                // member provenance so a downstream member flag predicate on
                // the temp resolves the exact declaration.
                if (e is CallExpr ceProv && ceProv.MemberProvenance is { } cpProv)
                    memberProvenanceByLocal[name] = cpProv;
                // M4 Task 4: capture the OS error code right after a
                // SetLastError=true P/Invoke call.
                if (e is CallExpr ce && ce.SetLastError)
                {
                    stmts.Add(new CallStmt { Function = "capture_last_error", SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                }
                stack.Push(new LocExpr { Name = name });
            }
            else
            {
                stack.Push(e);
            }
        }

        // Join spill when a branch instruction jumps away with a non-empty
        // stack: read the current values bottom-to-top (no pops), assign them
        // to the join's shared temps (reuse existing slots and assert equal
        // heights); after an unconditional br the sequential flow is dead ->
        // clear the stack.
        void SpillAtBranch(string targetLabel, IlInstruction ins, bool unconditional)
        {
            if (stack.Count == 0) return;
            var bottomToTop = BottomToTop(stack);
            var joinVars = RegisterJoin(targetLabel, bottomToTop, ins);
            stack.Clear();
            if (!unconditional)
                for (var i = 0; i < joinVars.Count; i++)
                    stack.Push(new LocExpr { Name = joinVars[i] });
        }

        // Writes the bottom-to-top stack values into targetLabel's shared join
        // slots; the first time allocates temps (same counter as the existing
        // temps), reusing the names when slots already exist (multiple branch
        // arms write the same slots -- normalization at the join makes them
        // converge).
        List<string> RegisterJoin(string targetLabel, List<Expr> bottomToTop, IlInstruction ins)
        {
            if (joinRestore.TryGetValue(targetLabel, out var existing))
            {
                if (existing.Count != bottomToTop.Count)
                    throw new NotSupportedException($"join {targetLabel}: branch stack height {bottomToTop.Count} != registered {existing.Count}");
                for (var i = 0; i < existing.Count; i++)
                    stmts.Add(new AssignStmt { Target = existing[i], Value = bottomToTop[i], SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                // A2 review: reused slot -- reconcile the recorded hint (drop
                // on disagreement, keep on equal, adopt a first non-null).
                ReconcileJoinHint(existing, bottomToTop);
                return existing;
            }
            var names = new List<string>(bottomToTop.Count);
            for (var i = 0; i < bottomToTop.Count; i++)
            {
                var name = $"t{temps.Count}";
                temps.Add(name);
                tempWidths[name] = ExprWidth(bottomToTop[i], ctx, tempWidths);
                names.Add(name);
                stmts.Add(new AssignStmt { Target = name, Value = bottomToTop[i], SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                // M5 receiver hint: a join temp spilled from a typed value must
                // keep the managed type -- the receiver-aware resolver reads
                // objTypes after the merge (TextReader::ReadToEnd shape).
                PropagateObjType(name, bottomToTop[i]);
            }
            joinRestore[targetLabel] = names;
            return names;
        }

        // Reads bottom-to-top (Stack.ToArray is top-first; reversing gives
        // bottom-to-top), without modifying the stack.
        static List<Expr> BottomToTop(Stack<Expr> s)
        {
            var arr = s.ToArray();
            var list = new List<Expr>(arr.Length);
            for (var i = arr.Length - 1; i >= 0; i--) list.Add(arr[i]);
            return list;
        }

        // M5 join/store hint propagation: the managed-type rules for a value
        // written into a local or a branch-join temp (objTypes is the receiver
        // hint the BCL resolver reads after a merge point).
        string? ObjTypeOf(Expr value)
        {
            if (value is LocExpr vl && objTypes.TryGetValue(vl.Name, out var vt))
                return vt;
            if (value is NullExpr nn && nn.TypeName != null)
                return nn.TypeName;
            // FILTER clause (`catch (E e) when (...)`): csc binds the caught
            // variable through `isinst T` over the exception ENTRY, not through
            // a direct entry store. The entry IS the model's exception carrier
            // (tracked as "System.Type" -- the catch-variable convention), so
            // the isinst result keeps that hint through every spill temp and the
            // final stloc; without it `e.Message` inside a filter resolves with
            // no receiver hint and dies at the phantom-call guard (MEASURED:
            // the first ehfilter1 run failed exactly there).
            if (value is CallExpr ii && ii.Args.Count == 1 && ii.Args[0] is ExceptionEntryExpr
                && ii.Function.StartsWith("isinst_", StringComparison.Ordinal))
                return "System.Type";
            if (value is ValueTypeNewExpr vn2)
                return vn2.TypeFullName;
            if (value is CallExpr rc && rc.Resolved?.ResultKind is string rk)
            {
                return rk switch
                {
                    "dt" => "System.DateTime",
                    "ts" => "System.TimeSpan",
                    "dec" => "System.Decimal",
                    "guid" => "System.Guid",
                    _ => null,
                };
            }
            // A callvirt result carries the same value-kind marker (the List<T>
            // indexer/get_Item arms set ResultKind "dec"/"dt"/"ts"/"guid"), so a
            // later value-type member call (e.g. `list[0].ToString()`) resolves
            // with the element's managed type instead of falling back to the
            // Object default (which would pass the value ADDRESS as an
            // Il2CppObject*).
            if (value is VirtualCallExpr vc2 && vc2.ResultKind is string rk2)
            {
                return rk2 switch
                {
                    "dt" => "System.DateTime",
                    "ts" => "System.TimeSpan",
                    "dec" => "System.Decimal",
                    "guid" => "System.Guid",
                    _ => null,
                };
            }
            // nb9: a call result's MANAGED type (Resolved.ReturnTypeName) must
            // survive a stloc into a local -- the foreach element of a GroupBy
            // result is stored that way, and the per-bucket Count/Sum dispatch
            // needs the "System.Linq.IGrouping" hint (without it Count fell
            // through to the int32[] array icall and read the bucket KEY as a
            // length).
            if (value is CallExpr rc3 && rc3.Resolved?.ReturnTypeName is string rtn3)
                return rtn3;
            // P-G3 D1: a SHARED clone's VAR/MVAR return is the boxed T slot --
            // the stloc store into a local carries the concrete T so a later
            // value-type BCL call on the local (ldloca + Int32::ToString)
            // knows the slot holds a BOXED value, not an int32 address.
            if (value is VirtualCallExpr vc && md != null && vc.ReturnsRef)
                return SharedReturnConcreteType(md, vc.MethodToken);
            return null;
        }

        void PropagateObjType(string target, Expr value)
        {
            var t = ObjTypeOf(value);
            if (t != null) objTypes[target] = t;
        }

        // floatfix: the user-call ReturnTypeName carries the signature
        // coder's "r:" managed-reference prefix (MethodSignatureCoder's
        // CLASS/GENERICINST/SZARRAY encodings), while the objTypes values are
        // BARE managed names (the newobj/ToList tracking convention). A
        // List-returning user method's result temp must record the bare name
        // -- the List gate prefix-matches "System.Collections.Generic.List`1"
        // -- or List LINQ operators on the call result fall to the array
        // family (the MakeFloatList().Aggregate funnel).
        // The strip is only meaningful for a NON-NULL name: every caller
        // guards ReturnTypeName != null first, and the objTypes map stores
        // non-null values. The non-nullable signature states that contract
        // (a null name would have to be handled by the caller, never
        // silently propagated into the type-hint map).
        static string BareManagedName(string name) =>
            name.StartsWith("r:", StringComparison.Ordinal) ? name[2..] : name;

        // M5-JOIN (A2 review): a REUSED join slot must not keep a stale hint
        // -- if any arm's value type disagrees with the recorded one the join
        // is AMBIGUOUS: drop the hint so the resolver defers instead of
        // possibly misdispatching. Equal hints are kept.
        void ReconcileJoinHint(List<string> slots, List<Expr> values)
        {
            for (var i = 0; i < slots.Count; i++)
            {
                var newType = ObjTypeOf(values[i]);
                if (objTypes.TryGetValue(slots[i], out var oldType))
                {
                    if (!string.Equals(oldType, newType, StringComparison.Ordinal))
                        objTypes.Remove(slots[i]);
                }
                else if (newType != null)
                {
                    objTypes[slots[i]] = newType;
                }
            }
        }

        // A stloc/starg assignment invalidates lazy references to its target
        // (local/param) still alive on the stack -- the `n-- > 0` pattern
        // `ldarg; dup; ldc; sub; starg; ldc.0; bgt`: bgt must compare the OLD
        // n, but the dup copy is a lazy LocExpr(arg0); reading it after the
        // assignment would yield the new value (one fewer loop iteration).
        // Fix: snapshot any stack value still referencing the target into a
        // temp BEFORE the assignment statement (evaluated before it), replacing
        // the stack top with the snapshot temp.
        //
        // The SAME invalidation applies to a STATIC FIELD store (postfix
        // increment: `ldsfld SF; dup; ldc.i4.1; add; stsfld SF` -- the dup
        // copy is a lazy StaticFieldExpr read and must observe the pre-store
        // value). The Stsfld branch and the folded static-property setter
        // route through this helper too (probe-postfixinc2).
        void SnapshotStackReads(List<Stmt> sink, Stack<Expr> stackArg, string target, IlInstruction ins,
            Func<Expr, string, bool> readsTarget, string? readWidth = null, bool staticFieldSlot = false)
        {
            var bottomToTop = BottomToTop(stackArg);
            var rebuilt = new List<Expr>(bottomToTop.Count);
            foreach (var e in bottomToTop)
            {
                if (readsTarget(e, target))
                {
                    // A STATIC-FIELD snapshot uses a non-"tN" name on purpose:
                    // TempSlotPass's metadata-free kind mirror classifies a
                    // StaticFieldExpr read "val", so a `tN` snapshot of a LONG
                    // static field was merged into an int32 slot and the
                    // preserved old value truncated (probe-postfixinc2:
                    // 5000000000 -> 705032704). A non-candidate name is
                    // poisoned by the pass, so the slot keeps the width the
                    // emitter seeds for it (CodeGenerator locKind
                    // System.Int64/System.UInt64 -> "l").
                    var name = staticFieldSlot ? $"tsnap{temps.Count}" : $"t{temps.Count}";
                    temps.Add(name);
                    tempWidths[name] = readWidth ?? ExprWidth(e, ctx, tempWidths);
                    sink.Add(new AssignStmt { Target = name, Value = e, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                    rebuilt.Add(new LocExpr { Name = name });
                }
                else rebuilt.Add(e);
            }
            stackArg.Clear();
            foreach (var e in rebuilt) stackArg.Push(e); // rebuilt is bottom-to-top; pushing in order reproduces the same order
        }

        // Width of a field's VALUE slot from its decoded signature blob
        // ("06 <elem>", the ResolveField encoding). A `long` static-field read
        // that must be preserved across a store needs a full-width snapshot
        // temp -- ExprWidth cannot classify a StaticFieldExpr (no metadata
        // there) and would hand out int32, truncating the old value
        // (probe-postfixinc2: 5000000000 -> 705032704).
        static string FieldWidthFromSignature(string signature)
        {
            if (signature.Length < 4) return "i";
            return int.Parse(signature.Substring(2, 2), System.Globalization.NumberStyles.HexNumber) switch
            {
                0x0A or 0x0B => "l",   // I8 / U8
                // probe-sfieldfloat: R4/R8 statics hold a FLOATING value -- the
                // preserved old value of `D++` must keep the floating width or
                // the snapshot temp truncates it (3.5 -> 3). "d" covers both:
                // the IR holds every float in a double slot (the emitter's
                // locKind seed declares the snapshot double for 0x0C too).
                0x0C or 0x0D => "d",
                _ => "i",
            };
        }

        void EmitStore(string target, IlInstruction ins)
        {
            var value = stack.Pop();
            SnapshotStackReads(stmts, stack, target, ins, ReferencesTarget);
            stmts.Add(new AssignStmt { Target = target, Value = SharedReturnToValueLocal(value, target), SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
            // M5 wiring: propagate the recorded managed type through the
            // store (`object d = 3.14;` -- the box temp's type follows into
            // the local so WriteLine(d) resolves the boxed type).
            PropagateObjType(target, value);
        }

        // D5: a SHARED clone's VAR/MVAR return stored into a USER VALUE TYPE
        // local (Box<Pt>.Get() -> `Pt p`). The shared object ABI returns the
        // BOXED object slot; the store into the concrete struct/enum local
        // must unbox it (`p = (*reinterpret_cast<Cases_Pt*>(il2cpp_unbox_user_
        // struct(obj)))`), or the assignment Il2CppObject* -> Cases_Pt fails
        // to compile. Returns an unbox CallExpr (the unbox_{token} CodeGenerator
        // emission path, same as the P25b per-instance unbox) for a user VALUE
        // return; the original value otherwise.
        Expr SharedReturnToValueLocal(Expr value, string targetName)
        {
            string? concrete = null;
            int? token = null;
            if (value is VirtualCallExpr vcr && md != null && vcr.ReturnsRef)
            {
                concrete = SharedReturnConcreteType(md, vcr.MethodToken);
                if (concrete != null) token = SharedReturnConcreteToken(md, vcr.MethodToken);
            }
            else if (value is LocExpr tlv && objTypes.TryGetValue(tlv.Name, out var tracked))
            {
                concrete = tracked;
                if (sharedReturnTokens.TryGetValue(tlv.Name, out var st)) token = st;
            }
            if (concrete == null || token == null) return value;
            // Only USER value types (struct/enum) need the return unbox; a
            // reference-typed return is already the object (the consumer's
            // local is Il2CppObject*-typed).
            var concreteDef = types?.FirstOrDefault(t => t.MatchesFullName(concrete));
            if (concreteDef == null || !IsUserValueType(concreteDef)) return value;
            var isEnum = concreteDef.BaseTypeName == "System.Enum";
            if (!isEnum)
            {
                // User STRUCT: the target local must be a value local of the
                // same struct ("v:<FullName>" or "us:<CppName>") -- the
                // object-typed slot is left untouched.
                var targetKind = LocWidth(targetName, ctx, tempWidths);
                if (!targetKind.StartsWith("v:", StringComparison.Ordinal)
                    && targetKind != "us:" + TypeEmitter.TypeCppName(concreteDef)) return value;
            }
            // User ENUM: the enum value rides the int32 carrier slot; the boxed
            // enum's payload is the int64 carrier (icall_box_enum), so the
            // return unbox reads it back via icall_unbox_long (accepts enum
            // boxes) -- NOT unbox_{token}, whose user-enum branch is void (the
            // model's IsValueType is false for enums -- base is System.Enum).
            // The int64 result implicitly narrows to the enum's int32 slot.
            if (isEnum)
            {
                // GN-N6 obs1: mirror the struct gate above -- only unbox into
                // an ENUM value slot ("v:" or "us:"); an object-typed target
                // (e.g. `object o = box.Get();` for a shared VAR enum return)
                // must keep the BOXED value, not the int64 carrier.
                var targetKind = LocWidth(targetName, ctx, tempWidths);
                if (!targetKind.StartsWith("v:", StringComparison.Ordinal)
                    && targetKind != "us:" + TypeEmitter.TypeCppName(concreteDef)) return value;
                return new CallExpr
                {
                    Function = "il2cpp::runtime::icall_unbox_long",
                    ReturnsRef = false,
                    ResultKind = "l",
                    Args = { value },
                };
            }
            return new CallExpr
            {
                Function = $"unbox_{token}",
                ReturnsRef = false,
                ResultKind = "us:" + TypeEmitter.TypeCppName(concreteDef),
                Args = { value },
            };
        }

        // Whether the expression tree directly/indirectly references a local or
        // param named target (for snapshot invalidation detection).
        static bool ReferencesTarget(Expr e, string target) => e switch
        {
            LocExpr l => l.Name == target,
            BinaryExpr b => ReferencesTarget(b.Left, target) || ReferencesTarget(b.Right, target),
            UnaryExpr u => ReferencesTarget(u.Operand, target),
            CallExpr c => c.Args.Any(a => ReferencesTarget(a, target)),
            VirtualCallExpr v => v.Args.Any(a => ReferencesTarget(a, target)),
            DelegateInvokeExpr d => d.Args.Any(a => ReferencesTarget(a, target)),
            MarshalUtf8Expr m => ReferencesTarget(m.Arg, target),
            FieldExpr f => ReferencesTarget(f.Object, target),
            FieldAddrExpr f => ReferencesTarget(f.Object, target),
            NewObjExpr => false, // pure allocation, no argument expressions
            _ => false
        };

        // Static-field counterpart of ReferencesTarget: a StaticFieldExpr is a
        // LAZY value read of the field's storage, so a later stsfld to the SAME
        // field invalidates it (the postfix-increment `ldsfld; dup; ...;
        // stsfld` shape). StaticFieldAddrExpr is deliberately NOT matched: an
        // address (`ldsflda`) keeps .NET byref semantics -- reading through it
        // after the store must observe the NEW value, exactly like net48.
        static bool ReferencesStaticField(Expr e, string target) => e switch
        {
            StaticFieldExpr s => s.FieldName == target,
            BinaryExpr b => ReferencesStaticField(b.Left, target) || ReferencesStaticField(b.Right, target),
            UnaryExpr u => ReferencesStaticField(u.Operand, target),
            CallExpr c => c.Args.Any(a => ReferencesStaticField(a, target)),
            VirtualCallExpr v => v.Args.Any(a => ReferencesStaticField(a, target)),
            DelegateInvokeExpr d => d.Args.Any(a => ReferencesStaticField(a, target)),
            MarshalUtf8Expr m => ReferencesStaticField(m.Arg, target),
            FieldExpr f => ReferencesStaticField(f.Object, target),
            FieldAddrExpr f => ReferencesStaticField(f.Object, target),
            _ => false
        };

        // Width of a STATIC field's value slot from the model (FieldTypeName).
        // The folded static-property setter has no field SIGNATURE at hand --
        // its FoldedAccessor carries only the raw field name -- so a long
        // backing field would otherwise get an int32 snapshot temp and
        // truncate the preserved old value.
        static string? StaticFieldWidthByName(IReadOnlyList<TypeDef>? types, string ownerFullName, string fieldName)
        {
            if (types == null) return null;
            var owner = types.FirstOrDefault(t => t.FullName == ownerFullName
                || TypeEmitter.ReflectionFullName(t) == ownerFullName);
            var field = owner?.StaticFields.FirstOrDefault(f => f.Name == fieldName);
            if (field == null) return null;
            return field.FieldTypeName switch
            {
                "System.Int64" or "System.UInt64" => "l",
                _ => "i",
            };
        }

        // RuntimeHelpers.InitializeArray fold body (e2e G1): read the field's
        // RVA blob (FieldDefinition.GetRelativeVirtualAddress + PE section
        // mapping) and emit a stelem sequence into the array temp produced by
        // the newarr instruction. The newarr's ELEMENT TOKEN gives the width
        // (the RVA field's declared type is a size-encoded
        // __StaticArrayInitTypeSize=N value type) and its constant length arg
        // the COUNT -- csc dedupes prefix blobs (base64case's 1/2-byte arrays
        // reuse the 3-byte "Man" blob), so the newarr length bounds the copy,
        // never the blob size. Void call: nothing is pushed, the dup'd array
        // reference stays on the stack.
        void FoldInitializeArray(List<Stmt> stmts, IlInstruction ins, MetadataReader md,
            PEReader? pe, IntConstExpr marker, Expr arrRef)
        {
            var fieldHandle = MetadataTokens.Handle((int)marker.Value);
            if (fieldHandle.Kind != HandleKind.FieldDefinition)
                throw new NotSupportedException(
                    $"InitializeArray at offset 0x{ins.Offset:X4}: the ldtoken marker is not a FieldDefinition (kind {fieldHandle.Kind})");
            var field = md.GetFieldDefinition((FieldDefinitionHandle)fieldHandle);
            var rva = field.GetRelativeVirtualAddress();
            if (rva == 0)
                throw new NotSupportedException(
                    $"InitializeArray at offset 0x{ins.Offset:X4} on field {md.GetString(field.Name)}: the field has no RVA data -- the pattern needs a .data-backed static field (net48 `new T[] {{...}}`)");
            if (pe == null)
                throw new NotSupportedException(
                    $"InitializeArray at offset 0x{ins.Offset:X4}: the array-initializer fold needs the PE image to read the field's RVA blob -- the StackFold.Fold call site must pass the PEReader (CodeGenerator.cs:275 / StringPoolPass.cs:228)");

            if (arrRef is not LocExpr arrLoc)
                throw new NotSupportedException(
                    $"InitializeArray at offset 0x{ins.Offset:X4}: the array argument is not the newarr temp ({arrRef.GetType().Name})");
            // Locate the allocation assignment: the array length/dims and
            // element type are both in it. Two allocation shapes:
            //   * newarr_<token>   -- the SZARRAY `new T[N] {{...}}` (one
            //     length arg; element token -> width).
            //   * mdnew_<elem><rank> -- the multidim `new T[a,b] {{...}}`
            //     (the dims as args; the RVA blob is row-major flat, so the
            //     stelems run flat index 0..product(dims)-1 exactly like the
            //     1-D case -- same data layout).
            var mdAlloc = stmts.LastOrDefault(s => s is AssignStmt a && a.Target == arrLoc.Name
                && a.Value is CallExpr c && c.Function.StartsWith("mdnew_", StringComparison.Ordinal));
            if (mdAlloc is AssignStmt { Value: CallExpr mdArr })
            {
                var mdCount = 1;
                foreach (var d in mdArr.Args)
                {
                    if (d is not IntConstExpr dimConst || dimConst.Value <= 0)
                        throw new NotSupportedException(
                            $"InitializeArray at offset 0x{ins.Offset:X4}: a multidim dimension length is not a constant (the initializer pattern requires `new T[..] {{...}}` with constant dims)");
                    mdCount *= (int)dimConst.Value;
                }
                var mdElemName = MultidimElemName(mdArr.Function); // suffix after "mdnew_"
                var mdWidth = MdSuffixWidth(mdElemName);
                if (mdWidth == null)
                    throw new NotSupportedException(
                        $"InitializeArray at offset 0x{ins.Offset:X4}: multidim element {mdElemName} not supported (the array-initializer fold supports byte/sbyte/bool, char/short, int/uint/float, long/ulong/double and the b5-gaps-13 value families dt/ts/dec/guid/dto)");
                // b5-gaps 13: the 16-byte value families route through the
                // width-16 writers in EmitStelems, which key on the BCL ELEMENT
                // NAME -- map the mdnew suffix to it (the mOp below is unused
                // on that path, exactly like the 1-D width-16 arm).
                var mdWriterName = mdElemName switch
                {
                    "dec" => "System.Decimal",
                    "guid" => "System.Guid",
                    "dto" => "System.DateTimeOffset",
                    _ => mdElemName,
                };
                var mOp = (mdWidth, mdElemName) switch
                {
                    (1, _) => ILOpCode.Stelem_i1,
                    (2, _) => ILOpCode.Stelem_i2,
                    (4, "float") => ILOpCode.Stelem_r4,
                    (4, _) => ILOpCode.Stelem_i4,
                    (8, "double") => ILOpCode.Stelem_r8,
                    (8, _) => ILOpCode.Stelem_i8,
                    // width 16: EmitStelems dispatches the dedicated dec/guid/
                    // dto writers BEFORE the opcode loop -- this arm is never
                    // consumed (the same "stelemOp = default" shape as the
                    // 1-D path above).
                    (16, _) => ILOpCode.Stelem_i4,
                    _ => throw new NotSupportedException(
                        $"InitializeArray at offset 0x{ins.Offset:X4}: multidim element {mdElemName} (width {mdWidth}) not supported"),
                };
                EmitStelems(stmts, ins, md, pe, rva, field, arrRef, mdWidth.GetValueOrDefault(), mdWriterName, mOp, need: mdCount * mdWidth.GetValueOrDefault());
                return;
            }
            var newarrStmt = stmts.LastOrDefault(s => s is AssignStmt a && a.Target == arrLoc.Name
                && a.Value is CallExpr c && c.Function.StartsWith("newarr_", StringComparison.Ordinal));
            if (newarrStmt is not AssignStmt { Value: CallExpr newArr }
                || !int.TryParse(newArr.Function[7..], out var elemToken))
                throw new NotSupportedException(
                    $"InitializeArray at offset 0x{ins.Offset:X4}: cannot locate the newarr allocation for temp {arrLoc.Name}");
            if (newArr.Args.Count != 1 || newArr.Args[0] is not IntConstExpr lenConst)
                throw new NotSupportedException(
                    $"InitializeArray at offset 0x{ins.Offset:X4}: the array length is not a constant (the initializer pattern requires `new T[N] {{...}}` with constant N)");
            var count1D = lenConst.Value;
            if (count1D <= 0)
                throw new NotSupportedException(
                    $"InitializeArray at offset 0x{ins.Offset:X4}: non-positive array length {count1D} in the initializer pattern");
            var count = count1D;
            var elemName = TypeFullNameFromToken(md, elemToken);
            var width = RvaElementWidthForToken(md, elemToken, elemName);
            if (width == null)
                throw new NotSupportedException(
                    $"InitializeArray at offset 0x{ins.Offset:X4}: element type {elemName} not supported (the array-initializer fold supports byte/sbyte/bool, char/short, int/uint/float, long/ulong/double)");
            // LOUD P0-03: 16-byte elements (decimal) have no fixed-width
            // ILOpCode -- EmitStelems handles them with the dedicated dec
            // writer. The default value is unused on that path.
            ILOpCode stelemOp = default;
            if (width != 16)
            {
                stelemOp = (width, elemName) switch
                {
                    (1, _) => ILOpCode.Stelem_i1,
                    (2, _) => ILOpCode.Stelem_i2,
                    (4, "System.Single") => ILOpCode.Stelem_r4,
                    (4, _) => ILOpCode.Stelem_i4,
                    (8, "System.Double") => ILOpCode.Stelem_r8,
                    (8, _) => ILOpCode.Stelem_i8,
                    _ => throw new NotSupportedException(
                        $"InitializeArray at offset 0x{ins.Offset:X4}: element type {elemName} (width {width}) not supported"),
                };
            }
            EmitStelems(stmts, ins, md, pe, rva, field, arrRef, width.GetValueOrDefault(), elemName, stelemOp, need: (int)(count * width.GetValueOrDefault()));
        }

        // Emit the RVA stelem sequence for the multidim/SZARRAY initializer
        // fold. count is the TOTAL element count (product of dims for
        // multidim; the length arg for SZARRAY); the RVA blob is copied
        // element-by-element into row-major flat order.
        static void EmitStelems(List<Stmt> stmts, IlInstruction ins, MetadataReader md, PEReader? pe, int rva,
            FieldDefinition field, Expr arrRef, int width, string elemName, ILOpCode stelemOp, int need)
        {
            var bytes = ReadRvaBytes(pe!, rva, need);
            if (bytes == null)
                throw new NotSupportedException(
                    $"InitializeArray at offset 0x{ins.Offset:X4}: cannot read {need} byte(s) of RVA data at 0x{rva:X8} (field {md.GetString(field.Name)})");
            var count = need / Math.Max(width, 1);
            // A 16-byte element is sliced as ONE unit: every family has its
            // own runtime image contract, so the writer is chosen by the
            // element NAME (never by width alone):
            //   * System.Decimal -- the .NET decimal image
            //     (lo32/mid32/hi32/flags, little endian) rebuilt into the
            //     runtime Il2CppDecimal aggregate. Never split it into two
            //     8-byte pieces (the 12-byte flags/hi32 boundary is not
            //     8-byte aligned).
            //   * System.Guid -- the raw net48 16-byte field image, which is
            //     exactly Il2CppGuid.b[16] (guid.h).
            //   * System.DateTimeOffset -- the net48 struct image
            //     (_dateTime ticks/int64 at 0, _offsetMinutes int16 at 8); the
            //     runtime value is (utcTicks, offsetMinutes), so convert the
            //     fold-time constant: utcTicks = localTicks - offset*TicksPerMinute.
            if (width == 16)
            {
                for (var i = 0; i < count; i++)
                {
                    var off = i * 16;
                    CallStmt stmt;
                    if (elemName == "System.Decimal")
                    {
                        var decVal = new CallExpr { Function = "decimal_rva_value", ResultKind = "dec" };
                        decVal.Args.Add(new IntConstExpr { Value = BitConverter.ToInt32(bytes, off + 12) });          // flags
                        decVal.Args.Add(new IntConstExpr { Value = unchecked((int)BitConverter.ToUInt32(bytes, off + 8)) }); // hi32
                        decVal.Args.Add(new Int64ConstExpr { Value = unchecked((long)BitConverter.ToUInt64(bytes, off)) });   // lo64
                        stmt = new CallStmt { Function = "stelem_dec", Args = { arrRef, new IntConstExpr { Value = i }, decVal } };
                    }
                    else if (elemName == "System.Guid")
                    {
                        var guidVal = new CallExpr { Function = "guid_rva_value", ResultKind = "guid" };
                        for (var b = 0; b < 16; b++)
                            guidVal.Args.Add(new IntConstExpr { Value = bytes[off + b] });
                        stmt = new CallStmt { Function = "stelem_guid", Args = { arrRef, new IntConstExpr { Value = i }, guidVal } };
                    }
                    else if (elemName == "System.DateTimeOffset")
                    {
                        var localTicks = unchecked((long)BitConverter.ToUInt64(bytes, off)) & 0x3FFFFFFFFFFFFFFFL;
                        var offsetMinutes = (int)BitConverter.ToInt16(bytes, off + 8);
                        var utcTicks = unchecked(localTicks - offsetMinutes * 600000000L);
                        var dtoVal = new CallExpr { Function = "dto_rva_value", ResultKind = "dto" };
                        dtoVal.Args.Add(new Int64ConstExpr { Value = utcTicks });
                        dtoVal.Args.Add(new IntConstExpr { Value = offsetMinutes });
                        stmt = new CallStmt { Function = "stelem_dto", Args = { arrRef, new IntConstExpr { Value = i }, dtoVal } };
                    }
                    else
                    {
                        throw new NotSupportedException(
                            $"InitializeArray at offset 0x{ins.Offset:X4}: element type {elemName} (width 16) not supported (no 16-byte token writer for this type)");
                    }
                    stmt.SourceOffset = ins.Offset;
                    stmt.SourceOpName = "InitializeArray";
                    stmts.Add(stmt);
                }
                return;
            }
            for (var i = 0; i < count; i++)
            {
                Expr val = (width, elemName) switch
                {
                    (1, _) => new IntConstExpr { Value = bytes[i] },
                    (2, _) => new IntConstExpr { Value = BitConverter.ToInt16(bytes, i * 2) },
                    (4, "float") or (4, "System.Single") => new DoubleConstExpr { Value = BitConverter.ToSingle(bytes, i * 4) },
                    (4, _) => new IntConstExpr { Value = BitConverter.ToInt32(bytes, i * 4) },
                    (8, "double") or (8, "System.Double") => new DoubleConstExpr { Value = BitConverter.ToDouble(bytes, i * 8) },
                    (8, _) => new IntConstExpr { Value = BitConverter.ToInt64(bytes, i * 8) },
                    _ => throw new NotSupportedException(
                        $"InitializeArray at offset 0x{ins.Offset:X4}: element type {elemName} (width {width}) not supported"),
                };
                stmts.Add(new CallStmt
                {
                    Function = $"stelem_{stelemOp}",
                    Args = { arrRef, new IntConstExpr { Value = i }, val },
                    SourceOffset = ins.Offset,
                    SourceOpName = "InitializeArray",
                });
            }
        }

        // Resolve the call: pop the args from the stack per the target
        // method's param count; a return value is pushed via Push (CallExpr ->
        // a temp assignment), a void method emits a CallStmt directly. Function
        // keeps the call_{token} placeholder; MethodRefResolver substitutes the
        // real CppName at emission.
        // virtualDispatch=true (callvirt): emits a VirtualCallExpr -- the
        // receiver is the first arg, dispatched at emission through the vtable
        // slot of the receiver's runtime type descriptor (Task 7).
        // calli (0x29, fnptr domain b23): fold an indirect call through a
        // function pointer. The CLI stack is (bottom-to-top) [arg1..argN,
        // fnptr] -- the fnptr is the TOP of the stack (pushed last; Roslyn
        // emits the fnptr load immediately before calli, corert ImportCalli
        // pops it first), so pop the fnptr first, then the args in reverse.
        // The StandAloneSig (0x11) token's signature carries the target's
        // unmanaged calling convention + param count + return + param types.
        // IR: CallExpr{Function="calli_{token}", Args=[fnptr, arg1..argN]};
        // the emitter decodes the signature and renders
        // ((ret(*)(params))fnptr)(args) with a null-pointer guard (NRE).
        void EmitCalli(List<Stmt> stmts, Stack<Expr> stack, IlInstruction ins, MetadataReader? md)
        {
            if (md == null)
                throw new NotSupportedException(
                    $"calli at offset 0x{ins.Offset:X4}: the StandAloneSig signature (target calling convention/params) requires a MetadataReader -- pure-byte folding cannot decode the target arity");
            var handle = MetadataTokens.Handle((int)ins.Operand);
            if (handle.Kind != HandleKind.StandaloneSignature)
                throw new NotSupportedException($"calli at offset 0x{ins.Offset:X4}: operand token 0x{ins.Operand:X8} is not a StandAloneSig");
            var (_, hasThis, paramCount, returnType, _) =
                MethodSignatureCoder.DecodeStandalone(md, md.GetStandaloneSignature((StandaloneSignatureHandle)handle).Signature);
            // it as the FIRST arg (below the explicit args, above nothing);
            // the emitted C++ function pointer includes it as a leading
            // Il2CppObject* param.
            var popCount = paramCount + (hasThis ? 1 : 0) + 1; // args (+ this) + fnptr
            if (stack.Count < popCount)
                throw new NotSupportedException($"calli at offset 0x{ins.Offset:X4}: pops {popCount} slots (fnptr + {paramCount} args{(hasThis ? " + this" : "")}) but the stack has only {stack.Count}");
            var fnptr = stack.Pop();
            var args = new List<Expr>();
            for (var i = 0; i < paramCount + (hasThis ? 1 : 0); i++) args.Insert(0, stack.Pop());
            var calli = new CallExpr
            {
                Function = $"calli_{ins.Operand}",
                // A managed-reference return type (string/object/class/array)
                // keeps the uniform Il2CppObject* IR type; the exact C++ type
                // is derived from the signature at emission. (Raw bytes: 0x0E
                // String, 0x1C Object, 0x12 CLASS, 0x1D/0x1F SZARRAY -- the
                // SignatureTypeCode enum lacks a CLASS member, 0x12 is unnamed.)
                ReturnsRef = (byte)returnType is 0x0E or 0x1C or 0x12 or 0x1D or 0x1F,
                // P-D3 (section 7.3 width assertions): a calli RESULT is typed from
                // its StandAloneSig return byte -- the single-use probes only
                // worked because OptimizeTemps INLINED the call into the
                // consumer; a MULTI-USE result lands in a temp whose
                // declaration falls to ExprKind(CallExpr). With ResultKind
                // null it became int32_t and silently TRUNCATED the x64
                // int64/double/pointer return (callimulti probe: `int32_t t0
                // = (int64_t)(loc0)(...)`). Mirror UserCallResultKind's
                // widths so the result slot matches CalliReturnCpp.
                // floatfix: the float (0x0C) SLOT stays "d" even though
                // CalliReturnCpp now renders the call as float(*)() -- the
                // float result widens into the double carrier exactly
                // (lossless), the same float-in-double slot convention as a
                // USER call's float return (UserCallResultKind 0x0C -> "d").
                ResultKind = (byte)returnType switch
                {
                    0x0A or 0x0B => "l",
                    0x0C or 0x0D => "d",
                    0x0F or 0x1B => "p",
                    0x18 => "ptr",
                    _ => null,
                },
            };
            calli.Args.Add(fnptr);
            calli.Args.AddRange(args);
            // A void-returning calli pushes nothing on the IL stack -> a
            // CallStmt; a value-returning calli is pushed via Push (CallExpr
            // results land in a temp).
            if (returnType == SignatureTypeCode.Void)
            {
                var stmt = new CallStmt
                {
                    Function = $"calli_{ins.Operand}",
                    SourceOffset = ins.Offset,
                    SourceOpName = OpName(ins.OpCode),
                };
                stmt.Args.AddRange(calli.Args);
                stmts.Add(stmt);
            }
            else
            {
                Push(calli, ins);
            }
        }

        void EmitCall(List<Stmt> stmts, Stack<Expr> stack, List<string> temps, IlInstruction ins,
            MetadataReader? md, IReadOnlyList<TypeDef>? types, bool virtualDispatch)
        {
            // M5 wiring: receiver/argument-type-aware resolution. The call's
            // FIRST stack value is the receiver (callvirt) or the first
            // explicit argument (call). When it is a tracked temp (newobj /
            // box result / GetType result), its recorded type lets the
            // resolver pick the right BCL mapping:
            //   * callvirt System.Object::ToString on a StringBuilder ->
            //     icall_stringbuilder_to_string (the vtable slot for a BCL
            //     receiver is not translatable);
            //   * Console.WriteLine(object) with a KNOWN boxed value ->
            //     the typed WriteLine + an unboxed receiver;
            //   * Int32/Boolean/...::ToString after ldloca -> a deref receiver.
            // The hint must reach the FIRST resolution: without it the static
            // dispatcher THROWS for WriteLine(object) before the re-resolve
            // below could run.
            string? arg0Type = null;
            bool arg0IsAddr = false;
            string? arg1Type = null;
            string? arg2Type = null;
            // p-refl2-gapmatrix (B08): the member-VALUE provenance recorded for
            // a folded typeof-chain GetMethod/GetField call (the declaring type
            // token + the member name), attached to the emitted CallExpr so a
            // downstream member flag predicate can resolve the exact
            // declaration. Null unless the call qualifies.
            (int TypeToken, string MemberName, bool IsField)? pendingMemberProvenance = null;
            if (md != null && stack.Count > 0)
            {
                var first = stack.Peek();
                if (first is LocExpr fl && objTypes.TryGetValue(fl.Name, out var tt)) arg0Type = tt;
                // P-G3 D1: a SHARED clone's VALUE-slot return is the BOXED
                // object (decision A -- the shared object ABI). When the IL
                // address-takes such a slot for a value-type BCL method
                // (`ldloca slot; call int32::ToString()`), the slot is an
                // Il2CppObject* holding the boxed value -- the receiver is the
                // OBJECT (unbox:i), not an int32 address (deref:i would read
                // the boxed pointer's low bytes). Swap the address for the
                // object and let the resolver pick the unbox arm.
                if (first is LocAddrExpr la && objTypes.TryGetValue(la.Name, out var laType)
                    && IsBoxedValueSlotName(laType))
                {
                    stack.Pop();
                    stack.Push(new LocExpr { Name = la.Name });
                    arg0Type = laType;
                    first = stack.Peek();
                }
                // typeof-chain wiring: a bare string-literal argument IS a
                // string -- Console.WriteLine(object) with a folded Type
                // value (or any (object)"literal") routes through the string
                // path. Gated on WriteLine: the other arg0Type consumers
                // (String.Concat(object, object) picks concat_2 on
                // "System.String") must NOT see the hint -- AsString on a
                // non-string object would be a garbage read.
                if (arg0Type == null && first is StringConstExpr && IsConsoleWriteLine(md, (int)ins.Operand))
                    arg0Type = "System.String";
                // D-NEST-2 (nesteddeep GAPS.md): ldflda pushes a FieldAddrExpr
                // -- the byref receiver of `ldflda V; call Int32::ToString()`
                // (Roslyn's `"..." + intField` shape). All three address shapes
                // (ldloca LocAddrExpr, ldsflda StaticFieldAddrExpr, ldflda
                // FieldAddrExpr) must route the value-type ToString/GetHashCode
                // receiver through the resolver's "deref:*" LOAD arm -- treating
                // the ADDRESS as a boxed object ("unbox:*") would emit
                // static_cast<Il2CppObject*>(&obj->V) -- an ill-formed cast
                // from int32_t* to Il2CppObject* (the field address is not a
                // boxed object).
                arg0IsAddr = first is LocAddrExpr or StaticFieldAddrExpr or FieldAddrExpr
                    // D-RND2-2 (randomdeep2 GAPS): `hist += buckets[i]` --
                    // Roslyn calls Int32::ToString() on the ldelema ADDRESS
                    // (the byref receiver), which Push() materialized into a
                    // "ptr"-kind temp. The ldloca/ldsflda/ldflda address
                    // shapes above already route value-type receivers through
                    // the resolver's "deref:*" LOAD arm; a "ptr"-width temp is
                    // exactly the same ADDRESS shape (intptr_t, never a boxed
                    // object), so it must too. Without it the resolver picked
                    // "unbox:i" and emitted
                    //   il2cpp_unbox_int32(static_cast<Il2CppObject*>(t15))
                    // on the intptr_t address -- "cannot cast from type
                    // 'intptr_t' to 'Il2CppObject*'" build abort.
                    //
                    // TypedReference (refanyval): a "p" temp (void*) is the
                    // SAME generic-address shape (the refanyval storage
                    // address / ldloca spill). Without this arm the boxed
                    // caller (`__refvalue(tr,int)` -> ldind + box) resolved to
                    // the "unbox:i" arm and read the ADDRESS as a boxed object
                    // (access violation).
                    || first is LocExpr ptrTemp
                        && LocWidth(ptrTemp.Name, ctx, tempWidths) is "ptr" or "p";
                // M5 wiring: the SECOND stack value's tracked type dispatches
                // String.Concat(object, object) numeric conveniences (the
                // second argument is the boxed number).
                if (stack.Count > 1 && stack.ElementAt(1) is LocExpr sl && objTypes.TryGetValue(sl.Name, out var st1))
                    arg1Type = st1;
                // b5-gaps section 89: the THIRD stack value's tracked type. A
                // THREE-argument LINQ call (Union/Intersect/Except/SequenceEqual
                // with an IEqualityComparer) pushes [source, second, cmp], so
                // the SOURCE sits at stack depth 2 -- invisible to the two-slot
                // hint above, which then mistook the SECOND sequence (or the
                // comparer) for the source and let a grouping-enumerable call
                // fall through to the array/ref kernels (a runtime family
                // mismatch). Threading this hint lets TryMapLinq see the real
                // source for the 3-arg two-sequence operators.
                if (stack.Count > 2 && stack.ElementAt(2) is LocExpr s2 && objTypes.TryGetValue(s2.Name, out var st2))
                    arg2Type = st2;
            }

            // typeof(T) fold (reflection audit F1): ldtoken <type>; call
            // System.Type::GetTypeFromHandle. The Ldtoken case pushed the
            // TYPE TOKEN as an IntConstExpr marker (prevIns == Ldtoken
            // guarantees it is the stack top -- ldtoken pushes exactly one
            // value and nothing can consume it in between). Fold the pair to
            // the type's FULL-NAME string -- the simplified System.Type model
            // (il2cpp_typeext.h assumption 1: a Type VALUE is the full name
            // string, the exact output the runtime GetTypeFromHandle icall
            // produces, byte-identical with obj.GetType().ToString()).
            if (md != null && prevIns is { OpCode: ILOpCode.Ldtoken }
                && stack.Count > 0 && stack.Peek() is IntConstExpr ldtMarker
                && IsGetTypeFromHandle(md, (int)ins.Operand))
            {
                stack.Pop();
                // RF-N4: record whether the folded type is an OPEN generic
                // definition (the ldtoken was a TypeDef/TypeRef with arity>0
                // vs a closed GENERICINST TypeSpec / a non-generic type). The
                // get_IsGenericTypeDefinition consumer fold (below) needs this
                // -- the folded NAME string cannot distinguish a definition
                // from its clones (TypeEmitter.ReflectionFullName registers
                // both under the definition's name).
                typeFromHandleGenericDef[ins.Offset] = IsOpenGenericDefToken(md, (int)ldtMarker.Value);
                // REFL6B (item B5): the ContainsGenericParameters classification
                // -- the type OR any DECLARING-chain type is an open generic
                // definition (net48 nested-in-open True).
                typeFromHandleContainsGenParams[ins.Offset] = ContainsGenericParamsToken(md, (int)ldtMarker.Value);
                // GN-18 P2/D1 (S3): remember the ldtoken TYPE TOKEN itself,
                // keyed by this GetTypeFromHandle CALL's offset. The
                // GetGenericArguments consumption fold below decodes the closed
                // GENERICINST TypeSpec blob from it (arity + managed argument
                // names). Same pattern / same key as the
                // typeFromHandleGenericDef record above.
                typeFromHandleToken[ins.Offset] = (int)ldtMarker.Value;
                // refl2 (A): record the net48 display triple for this TYPE VALUE
                // (see typeValueDisplay). Every downstream Type-domain consumer
                // fold reads it instead of re-deriving the token.
                typeValueDisplay[ins.Offset] = (
                    ReflectionDisplayName(md, (int)ldtMarker.Value, true),
                    ReflectionDisplayShortName(md, (int)ldtMarker.Value),
                    AssemblySuffix(md, (int)ldtMarker.Value));
                Push(new StringConstExpr { Value = TypeNameForReflection(md, (int)ldtMarker.Value), TypeToken = (int)ldtMarker.Value }, ins);
                return;
            }
            // typeof-chain consumer folds (audit F1 follow-up): the
            // GetTypeFromHandle fold above left the type's FULL-NAME string
            // on the stack. A StringConstExpr directly on top with prevIns
            // being the GetTypeFromHandle call IS that folded value (the
            // only producer of a bare string there); fold the consuming call:
            //   * Type::get_Name -> the SHORT name (net48: typeof(int).Name
            //     == "Int32") -- truncate the full name at the last '.';
            //   * Type::GetTypeCode (static 1-param form) -> the typecode
            //     CONSTANT (net48 values, mirror of the runtime's
            //     icall_object_get_type_code table in typecheck.cpp: Int32=9
            //     Boolean=3 Char=4 Double=14 String=18, else 1). The runtime
            //     icall takes a DESCRIPTOR (Il2CppClass*), not the name
            //     string -- folding avoids the ABI mismatch (the emitted
            //     il2cpp_type_get_typecode((Il2CppObject*)"System.Int32")
            //     fails to compile).
            // typeof-chain consumer folds (audit F1 follow-up + refl2 A): the
            // stack top is a TYPE VALUE (a StringConstExpr) whose display triple
            // was registered by its producer (typeValueDisplay, keyed by the
            // producing CALL's offset). Fold the consuming Type-domain call:
            //   * get_Name / get_FullName / get_AssemblyQualifiedName -- the
            //     net48 display strings (arrays keep their rank, constructed
            //     generics the assembly-qualified argument list);
            //   * GetTypeCode (static 1-param) -- the typecode CONSTANT;
            //   * get_IsGenericTypeDefinition / get_ContainsGenericParameters --
            //     the fold-time open-generic decisions;
            //   * the A-class predicates / GetArrayRank / Make*Type /
            //     get_MetadataToken / get_BaseType / Equals;
            //   * GetGenericArguments -- the adjacent-consumer element fold.
            // p-refl2-gapmatrix (B07/B08/B11/B17/B18): a member flag predicate
            // (IsAbstract / IsVirtual / IsGenericMethod / IsInitOnly / IsLiteral)
            // on a member VALUE whose declaring type the fold knows (a folded
            // typeof-chain GetMethod/GetField result, possibly stored in a
            // local). The simplified member model represents a member VALUE as
            // its name string, which loses the declaring type -- net48's
            // GetMethod on a base type returns the BASE declaration (the
            // abstract slot), not the derived override -- so the fold answers
            // from the metadata. The runtime fallback (name-only resolution)
            // stays for a member value without provenance.
            if (md != null && stack.Count > 0
                && IsMemberFlagPredicate(md, (int)ins.Operand, out var flagKind)
                && TryMemberProvenance(stack.Peek(), out var flagProv))
            {
                stack.Pop();
                Push(new IntConstExpr { Value = MemberFlagAnswer(md, flagProv, flagKind) }, ins);
                return;
            }
            if (md != null && prevIns is IlInstruction { OpCode: ILOpCode.Call or ILOpCode.Callvirt } prevCall
                && typeValueDisplay.TryGetValue(prevCall.Offset, out var typeValue)
                && stack.Count > 0 && stack.Peek() is StringConstExpr foldedType)
            {
                if (IsTypeGetName(md, (int)ins.Operand))
                {
                    stack.Pop();
                    Push(new StringConstExpr { Value = typeValue.Short }, ins);
                    return;
                }
                if (IsTypeGetFullName(md, (int)ins.Operand))
                {
                    stack.Pop();
                    Push(new StringConstExpr { Value = typeValue.Full }, ins);
                    return;
                }
                if (IsTypeGetAssemblyQualifiedName(md, (int)ins.Operand))
                {
                    stack.Pop();
                    Push(new StringConstExpr { Value = typeValue.Full + typeValue.AsmSuffix }, ins);
                    return;
                }
                if (IsTypeGetTypeCode(md, (int)ins.Operand))
                {
                    stack.Pop();
                    Push(new IntConstExpr { Value = TypeCodeOfTypeName(foldedType.Value, types) }, ins);
                    return;
                }
                // RF-N4: Type.get_IsGenericTypeDefinition on a FOLDED typeof
                // chain -- decided AT FOLD TIME (the runtime registry cannot
                // distinguish a definition from its closed clones).
                if (IsTypeGetIsGenericTypeDefinition(md, (int)ins.Operand)
                    && typeFromHandleGenericDef.TryGetValue(prevCall.Offset, out var isDef))
                {
                    stack.Pop();
                    Push(new IntConstExpr { Value = isDef ? 1 : 0 }, ins);
                    return;
                }
                // REFL6B (item B5): Type.get_ContainsGenericParameters on the
                // FOLDED typeof chain (same fold site, wider predicate).
                if (IsTypeGetContainsGenericParameters(md, (int)ins.Operand)
                    && typeFromHandleContainsGenParams.TryGetValue(prevCall.Offset, out var cgp))
                {
                    stack.Pop();
                    Push(new IntConstExpr { Value = cgp ? 1 : 0 }, ins);
                    return;
                }
                // GN-18 P2/D1 (S3 / Plan B): Type::GetGenericArguments on the
                // FOLDED typeof chain -- INTERCEPTED AT THE CONSUMPTION POINT.
                // The segment folds to the constant its ADJACENT consumer wants:
                //   next == ldlen                          -> IntConstExpr(arity)
                //   next == ldc.i4.i (0<=i<arity) &&
                //     next2 == ldelem_ref                 -> StringConstExpr(names[i])
                // ONE decision covers both consumers. EVERY other shape keeps the
                // existing runtime path, which LOUDs for arity > 0.
                if (IsTypeGetGenericArguments(md, (int)ins.Operand)
                    && typeFromHandleToken.TryGetValue(prevCall.Offset, out var genToken))
                {
                    var genArity = GenericInstArity(md, genToken);
                    var genNames = GenericInstArgNames(md, genToken);
                    if (genArity > 0 && genNames != null && genNames.Count == genArity)
                    {
                        var genIdx = il.FindIndex(x => x.Offset == ins.Offset);
                        if (genIdx >= 0 && genIdx + 1 < il.Count)
                        {
                            var genNext = il[genIdx + 1];
                            // .Length -> the arity constant. The consumed ldlen
                            // is recorded in gn18SkipOffsets so the main walk
                            // does not run it again.
                            if (genNext.OpCode is ILOpCode.Ldlen)
                            {
                                stack.Pop();
                                gn18SkipOffsets.Add(genNext.Offset);
                                Push(new IntConstExpr { Value = genArity }, ins);
                                return;
                            }
                            // [i] -> the i-th argument's managed name (matched BY
                            // VALUE across the whole ldc.i4 immediate family).
                            if (genIdx + 2 < il.Count
                                && LdcI4ConstValue(genNext) is { } genElemIndex
                                && genElemIndex >= 0 && genElemIndex < genNames.Count
                                && il[genIdx + 2].OpCode is ILOpCode.Ldelem_ref)
                            {
                                stack.Pop();
                                gn18SkipOffsets.Add(genNext.Offset);       // the ldc.i4.i
                                gn18SkipOffsets.Add(il[genIdx + 2].Offset); // the Ldelem_ref
                                gn18TypeValueLdelems.Add(il[genIdx + 2].Offset); // element-fold Type-value terminal
                                Push(new StringConstExpr { Value = genNames[genElemIndex] }, ins);
                                return;
                            }
                        }
                    }
                }
                //--------------------------------------------------------------------------
                // refl2 (A): the typeof-chain TYPE-KIND predicates. A BCL TypeRef
                // carries no typeinfo table at runtime, so the answers come from
                // the metadata TypeDef when present and the curated BCL shape
                // table otherwise (documented boundary).
                //--------------------------------------------------------------------------
                if (typeFromHandleToken.TryGetValue(prevCall.Offset, out var memberToken))
                {
                    if (TypeKindPredicateOf(md, (int)ins.Operand) is { } kindPred)
                    {
                        stack.Pop();
                        Push(new IntConstExpr { Value = TypeKindFlag(md, memberToken, kindPred, types) ? 1 : 0 }, ins);
                        return;
                    }
                    if (IsTypeGetBaseType(md, (int)ins.Operand))
                    {
                        var baseName = BaseTypeNameOf(md, memberToken);
                        stack.Pop();
                        if (baseName == null)
                        {
                            Push(new NullExpr { TypeName = "System.Type" }, ins);
                        }
                        else
                        {
                            Push(new StringConstExpr { Value = baseName }, ins);
                            // The base is ANOTHER Type VALUE -- register its
                            // display triple so `.Name` / `.FullName` on it fold.
                            typeValueDisplay[ins.Offset] = (
                                baseName, ShortTypeName(baseName), AssemblySuffixOfName(md, baseName));
                        }
                        return;
                    }
                    if (IsTypeGetMetadataToken(md, (int)ins.Operand))
                    {
                        stack.Pop();
                        Push(new IntConstExpr { Value = MetadataTokenOfType(md, memberToken) }, ins);
                        return;
                    }
                    // p-refl2-gapmatrix (C04/C05/C06/C09): Type.Assembly on a
                    // FOLDED typeof chain. The Assembly VALUE is the DEFINING
                    // assembly's SIMPLE NAME (net48: typeof(int).Assembly is
                    // mscorlib, typeof(GapDerived).Assembly is the input
                    // assembly -- the pre-refl2 REFL4 arm returned the input
                    // assembly unconditionally, which is wrong for a BCL type).
                    if (IsTypeGetAssembly(md, (int)ins.Operand))
                    {
                        stack.Pop();
                        Push(new StringConstExpr { Value = AssemblySimpleNameOfToken(md, memberToken) }, ins);
                        return;
                    }
                    if (IsTypeGetIsConstructedGenericType(md, (int)ins.Operand))
                    {
                        stack.Pop();
                        Push(new IntConstExpr { Value = IsConstructedGenericToken(md, memberToken) ? 1 : 0 }, ins);
                        return;
                    }
                    if (IsTypeGetTypeAttributePredicate(md, (int)ins.Operand) is { } attrPred)
                    {
                        stack.Pop();
                        Push(new IntConstExpr { Value = TypeAttributeFlag(md, memberToken, attrPred) }, ins);
                        return;
                    }
                    if (IsTypeGetArrayRank(md, (int)ins.Operand) && ArrayRankOfToken(md, memberToken) is { } rank)
                    {
                        stack.Pop();
                        Push(new IntConstExpr { Value = rank }, ins);
                        return;
                    }
                    // MakeArrayType() / MakeByRefType() / MakePointerType() and
                    // GetElementType(): the result is ANOTHER Type VALUE, so its
                    // display triple is registered for the next consumer.
                    if (IsTypeMakeArrayType(md, (int)ins.Operand, out var makeRank))
                    {
                        stack.Pop();
                        var elemFull = typeValue.Full;
                        Push(new StringConstExpr { Value = "[" + elemFull }, ins);
                        typeValueDisplay[ins.Offset] = (
                            elemFull + "[" + new string(',', Math.Max(makeRank, 1) - 1) + "]",
                            typeValue.Short + "[]",
                            typeValue.AsmSuffix);
                        return;
                    }
                    if (IsTypeMakeByRefType(md, (int)ins.Operand))
                    {
                        stack.Pop();
                        Push(new StringConstExpr { Value = typeValue.Full + "&" }, ins);
                        typeValueDisplay[ins.Offset] = (
                            typeValue.Full + "&", typeValue.Short + "&", typeValue.AsmSuffix);
                        return;
                    }
                    if (IsTypeMakePointerType(md, (int)ins.Operand))
                    {
                        stack.Pop();
                        Push(new StringConstExpr { Value = typeValue.Full + "*" }, ins);
                        typeValueDisplay[ins.Offset] = (
                            typeValue.Full + "*", typeValue.Short + "*", typeValue.AsmSuffix);
                        return;
                    }
                    if (IsTypeGetElementType(md, (int)ins.Operand))
                    {
                        var elem = ElementTypeDisplayOf(md, memberToken);
                        stack.Pop();
                        if (elem == null)
                        {
                            Push(new NullExpr { TypeName = "System.Type" }, ins);
                        }
                        else
                        {
                            Push(new StringConstExpr { Value = elem.Value.Identity }, ins);
                            typeValueDisplay[ins.Offset] = (elem.Value.Full, elem.Value.Short, elem.Value.AsmSuffix);
                        }
                        return;
                    }
                    // Type.GetGenericTypeDefinition() on a FOLDED constructed
                    // generic -- the DEFINITION's Type VALUE (identity for a
                    // definition receiver; InvalidOperationException for a
                    // non-generic one, which stays on the runtime path).
                    if (IsTypeGetGenericTypeDefinition(md, (int)ins.Operand)
                        && GenericDefinitionDisplayOf(md, memberToken) is { } defDisplay)
                    {
                        stack.Pop();
                        Push(new StringConstExpr { Value = defDisplay.Identity }, ins);
                        typeValueDisplay[ins.Offset] = (defDisplay.Full, defDisplay.Short, defDisplay.AsmSuffix);
                        return;
                    }
                }
                // Type.Equals(object) with BOTH sides folded Type values: the
                // Type VALUEs are name strings, so net48 reference identity
                // reduces to ordinal name equality.
                if (IsTypeEqualsMember(md, (int)ins.Operand)
                    && stack.Count >= 2 && stack.ElementAt(1) is StringConstExpr otherType)
                {
                    stack.Pop();
                    stack.Pop();
                    Push(new IntConstExpr
                    {
                        Value = string.Equals(foldedType.Value, otherType.Value, StringComparison.Ordinal) ? 1 : 0,
                    }, ins);
                    return;
                }
                // p-refl2-gapmatrix (E01/E02): Type.TypeHandle is mapped by
                // the resolver to il2cpp_typeinfo_get_type_handle (the handle
                // VALUE is the type descriptor's intptr_t address, the
                // reflecthandle.h value model); the immediate
                // Type.GetTypeFromHandle consumer maps to
                // il2cpp_type_get_type_from_handle_value. No fold needed.
            }
            // GN-18 P2/D1 (S3): the GetGenericArguments element fold leaves a
            // TYPE value on the stack -- a StringConstExpr holding the
            // argument's managed FULL name (e.g. "System.Int32"). Its immediate
            // get_Name consumer must truncate that to the SHORT name, exactly as
            // the typeof-chain get_Name fold above does (net48: "Int32"). At
            // this call prevIns is the Ldelem_ref the element fold CONSUMED
            // (recorded in gn18TypeValueLdelems), NOT a GetTypeFromHandle call,
            // so the typeof-chain gate above cannot see the receiver. Without
            // this fold the StringConstExpr would be classified as a plain
            // System.String receiver (StackFold recvType inference) and
            // MemberInfo::get_Name would fold to the identity -- emitting the
            // full name where the short name is required.
            if (md != null && IsTypeGetName(md, (int)ins.Operand)
                && prevIns is IlInstruction { OpCode: ILOpCode.Ldelem_ref } elemTerminal
                && gn18TypeValueLdelems.Contains(elemTerminal.Offset)
                && stack.Count > 0 && stack.Peek() is StringConstExpr elemTypeName)
            {
                stack.Pop();
                Push(new StringConstExpr { Value = ShortTypeName(elemTypeName.Value) }, ins);
                return;
            }
            // RuntimeHelpers.InitializeArray array-initializer fold (e2e G1):
            // `new byte[] {...}` / `new int[] {...}` compiles to
            //   ldc.i4.N; newarr <elem>; dup; ldtoken <RVA field>; call
            //   InitializeArray(Array, RuntimeFieldHandle)
            // (net48 csc; the dup'd array stays on the stack -- InitializeArray
            // is void). prevIns == Ldtoken + the IntConstExpr marker on top
            // (the FieldDefinition token from the Ldtoken case) identifies the
            // pattern. Fold to a stelem sequence loaded from the field's RVA
            // blob (FieldDefinition.GetRelativeVirtualAddress + PE section
            // mapping). The element WIDTH and COUNT come from the newarr
            // instruction -- csc DEDUPES prefix blobs (base64case's 1/2-byte
            // arrays reuse the 3-byte "Man" blob), so the newarr length bounds
            // the copy, never the blob size.
            if (md != null && prevIns is { OpCode: ILOpCode.Ldtoken }
                && stack.Count >= 2 && stack.Peek() is IntConstExpr initMarker
                && IsInitializeArray(md, (int)ins.Operand))
            {
                stack.Pop(); // the field-handle marker
                var initArr = stack.Pop(); // the dup'd newarr result (LocExpr temp)
                FoldInitializeArray(stmts, ins, md, pe, initMarker, initArr);
                return;
            }
            // Loud guard: an ldtoken result consumed by anything but
            // GetTypeFromHandle / InitializeArray must not flow into emission
            // as a raw token (silent miscompile).
            if (md != null && prevIns is { OpCode: ILOpCode.Ldtoken })
            {
                throw new NotSupportedException($"ldtoken at offset 0x{ins.Offset:X4}: no fold for this consumer (ldtoken handles fold only in the typeof/InitializeArray patterns)");
            }

            // rv=false (C API void) with a NON-VOID .NET signature: the IL
            // stack still carries the value the mapping drops -- the caller's
            // `pop` discards it (statement-expression call). Mirror that slot
            // with a DiscardedValueExpr marker so the shadow stack stays in
            // lockstep with IL; without it the next dup/pop misreads the
            // stack ("dup on empty stack" -- collchain 0x0523). Void-IL calls
            // (Hashtable.Add, set_Item, Clear...) push nothing. The marker is
            // consumed only by a pop; any other consumer is rejected loudly
            // at emission (unknown IR expr) -- the map's "callers must
            // discard" contract.
            void PushDiscardSlot(IlInstruction ins)
            {
                if (md != null && IlCallReturnsValue(md, (int)ins.Operand))
                    Push(new DiscardedValueExpr { CallToken = (int)ins.Operand, Offset = ins.Offset }, ins);
            }

            // caudit2 (G6-N second layer): `EqualityComparer<T>.Default` for a
            // USER value type T. net48's default comparer routes to
            // IEquatable<T>.Equals, i.e. the call is SEMANTICALLY `a.Equals(b)`
            // -- and the direct user-struct call already emits the concrete
            // generated method. The BCL owner has no arm for a user
            // instantiation (the generic-instance NAME channel collapses every
            // VALUETYPE argument to "valuetype"), so fold the pair here:
            //   call get_Default (0 params) -> a typed null receiver marker;
            //   callvirt/call Equals(T,T) on that marker -> the user method
            //   with this = &a and one explicit argument b (the comparer
            //   receiver is dropped).
            // Any OTHER receiver keeps the ordinary path (the P26 loud gap) --
            // a user-supplied comparer must never be rewritten.
            if (md != null && TryDecodeUserEqualityComparerMember(md, (int)ins.Operand,
                    out var eqMemberName, out var eqTypeFullName)
                && types?.FirstOrDefault(t => t.MatchesFullName(eqTypeFullName!)) is { IsValueType: true } eqType)
            {
                var eqPseudo = UserEqualityComparerPseudoType(eqTypeFullName!);
                if (eqMemberName == "get_Default")
                {
                    Push(new NullExpr { TypeName = eqPseudo }, ins);
                    return;
                }
                if (eqMemberName == "Equals" && stack.Count >= 3)
                {
                    var eqRecv = stack.ElementAt(2);
                    var eqRecvTracked = eqRecv is NullExpr { TypeName: string ert } && ert == eqPseudo
                        || eqRecv is LocExpr erl && objTypes.TryGetValue(erl.Name, out var erot) && erot == eqPseudo;
                    var eqMethod = eqRecvTracked
                        ? eqType.Methods.FirstOrDefault(m => m.Name == "Equals" && !m.IsStatic && m.ParamCount == 1
                            && UserMethodParamTypeMatches(m.ParamTypes, TypeEmitter.ReflectionFullName(eqType)))
                        : null;
                    if (eqMethod != null)
                    {
                        var eqSig = MethodSignatureCoder.Decode(md,
                            md.GetMemberReference((MemberReferenceHandle)MetadataTokens.Handle((int)ins.Operand)).Signature);
                        var eqThis = stack.ElementAt(1) switch
                        {
                            LocAddrExpr or StaticFieldAddrExpr or FieldAddrExpr => stack.ElementAt(1),
                            LocExpr el => (Expr)new LocAddrExpr { Name = el.Name },
                            _ => null,
                        };
                        if (eqSig.ParamCount == 2 && eqThis != null)
                        {
                            var eqB = stack.Pop();
                            _ = stack.Pop(); // the first T argument -- consumed as the by-address `this` above
                            stack.Pop();     // the folded Default-comparer receiver
                            var eqCall = new CallExpr
                            {
                                Function = $"call_{ins.Operand}",
                                ReturnsRef = false,
                                Resolved = new ResolvedMethod(
                                    MethodRefResolver.UserMethodCppName(TypeEmitter.ReflectionFullName(eqType),
                                        eqMethod.Name, eqMethod.ParamTypes),
                                    1, false, true, false, new[] { false }, true),
                            };
                            eqCall.Args.Add(eqThis);
                            eqCall.Args.Add(eqB);
                            Push(eqCall, ins);
                            return;
                        }
                    }
                }
            }

            //--------------------------------------------------------------------------
            // impl-sb (B/D): the net6+/net9 SPAN-CARRIER call patterns. The C# 13
            // `params ReadOnlySpan<object?>` lowering (net9 StringBuilder.
            // AppendFormat, reference :1522/:1569) materializes the argument
            // list through the compiler-synthesized inline-array helpers, and
            // the netcoreapp2.1+ Append(ReadOnlySpan<char>) shape carries its
            // span as the backing object. Both are unrepresentable in the IR
            // as written (byref-returning helpers, span structs) -- fold them
            // to the carrier model the wired arms consume:
            //   * InlineArrayElementRef(ref buf, i) -> the "__iae_<loc>_<i>"
            //     marker the stind.* arm captures (no emission);
            //   * InlineArrayAsReadOnlySpan(ref buf, n) -> a materialized
            //     object[] temp (newarr + stelem_ref) pushed as ONE slot -- the
            //     composite kernels' argsArray form (the popped count stays the
            //     signature arity);
            //   * Span`1/ReadOnlySpan`1<char>::op_Implicit(char[]) and
            //     MemoryExtensions::AsSpan(string) -> the backing-object
            //     identity (the string/array IS the span carrier the
            //     icall_stringbuilder_append_span kernel dispatches on).
            // Any shape outside these gates falls through to the normal
            // resolution (the external-BCL LOUD guard -- never a silent wrong
            // carrier).
            //--------------------------------------------------------------------------
            if (md != null && SpanCarrierCallTarget(md, (int)ins.Operand) is { } sct)
            {
                var (sctName, sctOwner) = sct;
                if (sctOwner == "<PrivateImplementationDetails>" && sctName == "InlineArrayElementRef"
                    && stack.Count >= 2 && stack.Peek() is IntConstExpr
                    && stack.ElementAt(1) is LocAddrExpr iaeBuf)
                {
                    var iaeIdx = (IntConstExpr)stack.Pop();
                    stack.Pop(); // the buffer local address
                    // The carrier marker is a synthetic LocAddrExpr (NOT a
                    // CallExpr): the fold's Push() materializes every CallExpr
                    // into an assignment temp, which would both emit the
                    // undefined helper symbol and lose the marker shape. The
                    // LocAddrExpr rides the shadow stack untouched; the stind.*
                    // arm consumes it (anything else reaching emission is a
                    // LOUD undefined-identifier cmake failure, never a silent
                    // wrong store).
                    stack.Push(new LocAddrExpr { Name = $"__iae_{iaeBuf.Name}_{iaeIdx.Value}" });
                    return;
                }
                if (sctOwner == "<PrivateImplementationDetails>" && sctName == "InlineArrayAsReadOnlySpan"
                    && stack.Count >= 2 && stack.Peek() is IntConstExpr iasLen
                    && stack.ElementAt(1) is LocAddrExpr iasBuf)
                {
                    if (spanElements.TryGetValue(iasBuf.Name, out var iasElems)
                        && iasLen.Value is >= 0 and <= int.MaxValue
                        && Enumerable.Range(0, (int)iasLen.Value).All(i => iasElems.ContainsKey(i)))
                    {
                        stack.Pop(); // the span length
                        stack.Pop(); // the buffer local address
                        // The compiler NEVER emits a zero-argument span form here
                        // (params with 0 args lower to a static Empty span), but a
                        // synthesized 0-length array is still the correct carrier:
                        // the composite engine treats it as "no placeholders".
                        var iasObjTok = SystemObjectTypeToken(md)
                            ?? throw new NotSupportedException(
                                "span-params lowering: no System.Object TypeRef in the assembly -- the inline-array element carrier cannot be materialized (impl-sb B LOUD whitelist)");
                        var iasTemp = $"t{temps.Count}";
                        temps.Add(iasTemp);
                        var iasArr = new CallExpr { Function = $"newarr_{iasObjTok}", ReturnsRef = true };
                        iasArr.Args.Add(new IntConstExpr { Value = iasLen.Value });
                        stmts.Add(new AssignStmt
                        {
                            Target = iasTemp,
                            Value = iasArr,
                            SourceOffset = ins.Offset,
                            SourceOpName = OpName(ins.OpCode),
                        });
                        for (var i = 0; i < iasLen.Value; i++)
                        {
                            stmts.Add(new CallStmt
                            {
                                Function = "stelem_ref",
                                Args = { new LocExpr { Name = iasTemp }, new IntConstExpr { Value = i }, iasElems[i] },
                                SourceOffset = ins.Offset,
                                SourceOpName = OpName(ins.OpCode),
                            });
                        }
                        objTypes[iasTemp] = "System.Object[]";
                        Push(new LocExpr { Name = iasTemp }, ins);
                        return;
                    }
                    // Missing/short element capture: fall through to the LOUD
                    // default (the resolver rejects the foreign generic shape).
                }
                if ((sctOwner == "System.Span`1" || sctOwner == "System.ReadOnlySpan`1")
                    && sctName == "op_Implicit"
                    && SpanCarrierIdentitySig(md, (int)ins.Operand, "r:c[]", "r:System.Char[]", "r:i[]")
                    && stack.Count > 0)
                {
                    // The char[]-backed carrier: the array IS the span. The
                    // reachable encoding is the OPEN-GENERIC operator (the
                    // MemberRef parent is the TypeSpec ReadOnlySpan`1<char>,
                    // the operator signature carries VAR -- MethodSignatureCoder
                    // consumes VAR to "i", so the T[] param decodes "r:i[]");
                    // the closed element type is invisible at this layer and
                    // not needed (the array object IS the carrier -- the
                    // append_span kernel dispatches on the runtime class).
                    // "r:c[]" / "r:System.Char[]" stay as the defensive
                    // closed-element shapes.
                    var carrier = stack.Pop();
                    Push(carrier, ins);
                    return;
                }
                if (sctOwner == "System.MemoryExtensions" && sctName == "AsSpan"
                    && SpanCarrierIdentitySig(md, (int)ins.Operand, "r:System.String")
                    && stack.Count > 0)
                {
                    // The string-backed carrier: the string IS the span.
                    var carrier = stack.Pop();
                    Push(carrier, ins);
                    return;
                }
                if (sctOwner == "System.Text.CompositeFormat" && sctName == "Parse"
                    && SpanCarrierIdentitySig(md, (int)ins.Operand, "r:System.String")
                    && stack.Count > 0)
                {
                    // C (impl-sb): the pre-parsed CompositeFormat is the FORMAT
                    // STRING itself in the carrier model -- the composite
                    // kernels re-parse the format text (the documented
                    // pre-parse deviation; the validation surface of the
                    // netcore pre-parse is not modeled). Without the identity
                    // the static Parse call falls to the fictional generated
                    // name (CompositeFormat has no generated body) and dies at
                    // cmake -- the C arms' only value source.
                    var carrier = stack.Pop();
                    Push(carrier, ins);
                    return;
                }
            }

            var resolved = md != null
                ? MethodRefResolver.Resolve(md, (int)ins.Operand, types ?? Array.Empty<TypeDef>(), null, arg0IsAddr, arg0Type, arg1Type, arg2Type)
                : null;
            var paramCount = resolved?.ParamCount ?? 0;
            // varargs (ECMA-335 II.23.2.2): the call-site MemberRef signature
            // is [fixed types...][SENTINEL 0x41][vararg types...], and
            // ParamCount counts BOTH groups. MethodSignatureCoder.Decode
            // returns the FIXED count only (the hash/ABI channel), so the
            // fold must add the call-site vararg count back before popping --
            // otherwise every vararg element stays stranded on the IL stack.
            var varArgCount = 0;
            var varArgTypes = Array.Empty<string>();
            if (md != null && MetadataTokens.Handle((int)ins.Operand).Kind == HandleKind.MemberReference)
            {
                var mrSig = md.GetMemberReference((MemberReferenceHandle)MetadataTokens.Handle((int)ins.Operand)).Signature;
                var varArgSig = MethodSignatureCoder.DecodeVarArgs(md, mrSig);
                if (varArgSig.IsVarArgs)
                {
                    varArgTypes = varArgSig.VarArgTypes;
                    varArgCount = varArgTypes.Length;
                }
            }
            var args = new List<Expr>();
            try
            {
                for (var i = 0; i < paramCount + varArgCount; i++) args.Insert(0, stack.Pop());
            }
            catch (InvalidOperationException)
            {
                throw new NotSupportedException($"call at offset 0x{ins.Offset:X4} pops {paramCount + varArgCount} args but the stack has only {stack.Count} (resolved={resolved?.CppName ?? "null"})");
            }

            // delechain: delegate == / != -- System.Delegate::op_Equality /
            // op_Inequality. The default resolver classifies op_* methods as
            // INSTANCE (its heuristic knows only get_/.ctor/.cctor) and the
            // Delegate map has no op_* entries, so the old behaviour threw
            // "pops this but the stack is empty". Operators are ALWAYS static;
            // fold == / != to the null-safe structural icall_delegate_equals
            // (a==b -> true, one-null -> false, else pairwise list compare --
            // exactly the .NET op_Equality contract; != is the negation).
            // Post-#44 the signature header (0x00 -- no HAS_THIS) resolves
            // op_* as STATIC, and MethodMap.Delegate maps both to the equals
            // icall: accept that resolution too (the CppName guard), so the
            // fold's negation (`!=` -> the "== 0" shape) still engages.
            if (md != null && args.Count == 2
                && (resolved is { IsInstance: true }
                    || resolved?.CppName == "il2cpp::runtime::icall_delegate_equals")
                && DelegateEqualityOperator(md, (int)ins.Operand, out var isInequality))
            {
                var eq = new CallExpr
                {
                    Function = $"call_{ins.Operand}",
                    ReturnsRef = false,
                    Resolved = new ResolvedMethod("il2cpp::runtime::icall_delegate_equals", 2, false, true, false,
                        new[] { false, false }, false),
                };
                eq.Args.AddRange(args);
                Push(isInequality ? Not(eq) : eq, ins);
                return;
            }

            if (virtualDispatch)
            {
                // callvirt: receiver = the implicit this on the stack (first
                // slot of an instance method). CLI stack order:
                // [this, arg1..argN] (this pushed first) -> after popping the
                // explicit args the stack top is this.
                // M2 conservative: the callvirt target must be an instance
                // method (virtual dispatch); a static resolution is an error.
                if (stack.Count == 0)
                    throw new NotSupportedException($"callvirt at offset 0x{ins.Offset:X4} (token {ins.Operand}): stack empty when popping this -- likely a catch-handler variable binding");
                args.Insert(0, stack.Pop()); // this is the receiver, slot 0 of the arg list
                // p-refl2-gapmatrix (B08): a GetMethod/GetField call whose
                // receiver is a folded typeof-chain TYPE VALUE (a StringConstExpr
                // carrying the ldtoken TYPE TOKEN) -- record the member
                // provenance (declaring type + member name). The simplified
                // member model represents the member VALUE as its name string,
                // which loses the declaring type; the fold keeps it so a
                // downstream flag predicate resolves the exact declaration
                // (net48's GetMethod on a base type returns the BASE
                // declaration, not the derived override).
                pendingMemberProvenance = MemberProvenanceOf(md, (int)ins.Operand, args);
                // M5 wiring: re-resolve with the RECEIVER's tracked type
                // (args[0] -- the pre-pop stack top was the LAST explicit
                // argument, not the receiver). Needed when the token's own
                // declaring type is not translatable (Object::ToString on a
                // StringBuilder temp, MemberInfo::get_Name on a GetType()
                // result -- the default resolution of get_* marks these
                // STATIC and would reject the callvirt below).
                var recvType = args[0] is ExceptionEntryExpr ? "System.Type"
                    : args[0] is StringConstExpr ? "System.String"
                    : args[0] is LocExpr rl2 && objTypes.TryGetValue(rl2.Name, out var rt2) ? rt2
                    // P25 (listp25): a VALUE-TYPE receiver that was ldloca'd
                    // (a struct enumerator -- List<int>.Enumerator as a value
                    // type is address-taken by the C# compiler) arrives as a
                    // LocAddrExpr whose slot is the same tracked managed local
                    // (il2cpp_list_i4_get_enumerator stores Il2CppObject* into
                    // the local; the foreach body re-loads it via ldloca). The
                    // track follows the SLOT NAME, so the address form must hit
                    // the same objTypes lookup -- otherwise MoveNext/Dispose on
                    // the enumerator lose the "List`1.Enumerator" receiver hint
                    // and fall through to the generated-name fallback.
                    : args[0] is LocAddrExpr rl2a && objTypes.TryGetValue(rl2a.Name, out var rt2a) ? rt2a
                    // M6 (encoding domain): a null folded from an Encoding
                    // singleton getter carries the tracked encoding type
                    // (NullExpr.TypeName) -- GetBytes/GetString dispatch on it.
                    : args[0] is NullExpr ne && ne.TypeName != null ? ne.TypeName
                    : null;
                // constrained. VALUE-TYPE receiver (guidcase shape -- the
                // Roslyn `ldloca <slot>; constrained. T; callvirt
                // System.Object::ToString()` sequence): the callvirt
                // MemberRef parent is System.Object (value types route
                // through Object::ToString), so the receiver's DECLARED
                // type comes from the constrained. TypeToken marker (the
                // decoder rewrites the prefix to a Nop with the TypeToken
                // operand preserved -- IlDecoder.cs constrained case).
                // ECMA III.4.2: the constrained type IS the receiver's
                // type, and the marker is always the immediate predecessor
                // of the callvirt -- the exact hint the value-type domain
                // mappings need. constrainedRecvType records WHERE the
                // hint came from (only the marker shape gets the direct
                // value-type ToString fold below).
                string? constrainedRecvType = null;
                int constrainedToken = -1; // the constrained. TypeToken operand (for the value-receiver box)
                if (md != null && recvType == null
                    && prevIns is { OpCode: ILOpCode.Nop, OperandKind: IlOperandKind.TypeToken } constrainedMarker)
                {
                    constrainedToken = (int)constrainedMarker.Operand;
                    var cHandle = MetadataTokens.Handle((int)constrainedMarker.Operand);
                    // TypeSpecification markers (generic VAR, e.g. constrained.
                    // !0 on <>f__AnonymousType0<T>.ToString): resolve through
                    // the M3 convention (VAR -> System.Int32) like every
                    // other token site.
                    if (cHandle.Kind is HandleKind.TypeReference or HandleKind.TypeDefinition
                        or HandleKind.TypeSpecification)
                    {
                        // P-G3/paircase Cmp: a VAR TypeSpec constrained marker
                        // resolves through the enclosing clone's suffix (the
                        // concrete T), not the M3 "int" default -- the
                        // interface callvirt on a generic parameter then gets
                        // the real receiver hint.
                        recvType = ConstrainedRecvFullName(md, (int)constrainedMarker.Operand);
                        constrainedRecvType = recvType;
                        // P-G3/paircase Cmp: the constrained callvirt's receiver
                        // is loaded BY ADDRESS (csc emits `ldarga.s` -- the
                        // generic param may be a value type). In a REFERENCE
                        // clone (T1=string) the VAR slot holds the managed object
                        // directly: `&arg1` is an Il2CppObject**, but the BCL
                        // interface icalls (icall_string_compare_to /
                        // il2cpp_icomparable_compare_to) take the OBJECT
                        // (Il2CppObject*). Deref the address receiver to the
                        // object value for a reference-typed clone arg.
                        if (args[0] is LocAddrExpr recvAddr
                            && MethodRefResolver.SharedCloneVarTypeIsRef(md, (int)constrainedMarker.Operand, types))
                        {
                            args[0] = new LocExpr { Name = recvAddr.Name };
                            arg0IsAddr = false;
                        }
                        // GN-N3 (p-iface): the constrained receiver may also be
                        // an INSTANCE FIELD address (`item.CompareTo(other)` on
                        // the `T item` field -- csc emits ldflda, a FieldAddrExpr).
                        // A reference-typed clone holds the object directly in the
                        // field (Il2CppObject*); deref the field address to the
                        // field VALUE (FieldExpr) so the interface icall receives
                        // the object, not `Il2CppObject**`.
                        else if (args[0] is FieldAddrExpr recvFieldAddr
                            && MethodRefResolver.SharedCloneVarTypeIsRef(md, (int)constrainedMarker.Operand, types))
                        {
                            args[0] = new FieldExpr
                            {
                                Object = recvFieldAddr.Object,
                                FieldCppName = recvFieldAddr.FieldCppName,
                                OwnerFullName = recvFieldAddr.OwnerFullName,
                                IsRefField = true,
                                IsValueTypeObject = recvFieldAddr.IsValueTypeObject,
                            };
                            arg0IsAddr = false;
                        }
                    }
                }
                // USER VALUE TYPE (struct) constrained receiver -- the same
                // `ldloca a; constrained. P; callvirt Object::ToString()`
                // shape for ToString / GetHashCode / Equals(object). ECMA
                // III.4.2: constrained. on a VALUE type does NOT box -- net48
                // dispatches DIRECTLY to the struct's own override. Without
                // this arm the fold left the ldloca ADDRESS in the receiver
                // slot of the boxed-object icalls (icall_object_to_string /
                // icall_object_get_hash_code / icall_boxed_equals_ext2): the
                // C++ "cannot convert 'Cases_P*' to 'Il2CppObject*'" compile
                // failure, and a lost override even if it had compiled.
                // The translated struct's own methods take `{T}* this`
                // (TypeEmitter value-type instance ABI), so the ADDRESS
                // receiver passes through unchanged.
                if (md != null && constrainedRecvType != null && types != null
                    // RF-REFL: a ValueTuple instantiation is a BCL struct the
                    // transpiler SYNTHESIZES (ValueTupleSupport), not a user
                    // value type -- its members dispatch through the tuple
                    // helper path below, not the user-struct ABI.
                    && !ValueTupleSupport.IsValueTupleName(constrainedRecvType)
                    && types.FirstOrDefault(t => t.MatchesFullName(constrainedRecvType)
                        && t.IsValueType && t.BaseTypeName != "System.Enum") is { } vtRecvDef
                    && ObjectInstanceMemberName(md, (int)ins.Operand) is { } vtObjMember)
                {
                    var vtMethod = vtRecvDef.Methods.FirstOrDefault(m => !m.IsStatic
                        && m.Name == vtObjMember
                        && m.ParamCount == (vtObjMember == "Equals" ? 1 : 0)
                        // Equals(object) ONLY -- a struct's Equals(P) overload
                        // (IEquatable<T>) has a different signature and must
                        // not be bound to the Object::Equals call.
                        && (vtObjMember != "Equals" || m.ParamTypes == "r:System.Object"));
                    if (vtMethod != null)
                    {
                        var vtThis = args[0] switch
                        {
                            LocAddrExpr or StaticFieldAddrExpr or FieldAddrExpr => args[0],
                            // A "sp:"-kind temp is ALREADY a {T}* struct
                            // pointer (the ldelema element address of a
                            // value-type array) -- taking its address again
                            // would emit {T}**.
                            LocExpr vtLe when tempWidths.TryGetValue(vtLe.Name, out var vtW)
                                && vtW.StartsWith("sp:", StringComparison.Ordinal) => args[0],
                            LocExpr vtLe => (Expr)new LocAddrExpr { Name = vtLe.Name },
                            _ => args[0],
                        };
                        var vtParamRefs = vtMethod.ParamTypes.Length == 0
                            ? Array.Empty<bool>()
                            : CodeGenerator.SplitTopLevel(vtMethod.ParamTypes)
                                .Select(p => p.StartsWith("r:", StringComparison.Ordinal)).ToArray();
                        var vtCall = new CallExpr
                        {
                            Function = $"call_{ins.Operand}",
                            ReturnsRef = vtMethod.ResultKind == MethodResultKind.Reference,
                            Resolved = new ResolvedMethod(
                                // Same symbol shape as TypeEmitter.MethodSymbolName
                                // (Sanitize(ReflectionFullName) == TypeCppName).
                                MethodRefResolver.UserMethodCppName(
                                    TypeEmitter.ReflectionFullName(vtRecvDef), vtMethod.Name, vtMethod.ParamTypes),
                                vtMethod.ParamCount, false, !vtMethod.ReturnsVoid,
                                vtMethod.ResultKind == MethodResultKind.Reference, vtParamRefs, true),
                        };
                        vtCall.Args.Add(vtThis);
                        for (var vi = 1; vi < args.Count; vi++) vtCall.Args.Add(args[vi]);
                        Push(vtCall, ins);
                        return;
                    }
                    // No override: net48 System.ValueType.ToString() returns the
                    // type's FULL NAME (nested types keep the '+' separator) --
                    // a compile-time constant, so no receiver evaluation and no
                    // box is needed. Generic instantiations print the closed
                    // name ("Box`1[System.Int32]") and keep the existing path.
                    if (vtObjMember == "ToString" && vtRecvDef.GenericArity == 0)
                    {
                        Push(new StringConstExpr { Value = TypeEmitter.ReflectionFullName(vtRecvDef) }, ins);
                        return;
                    }
                }
                // G2 (audit recurse-recheck f14b): callvirt Object::ToString on
                // an UNTRACKED receiver -- a plain `object no = null;` local,
                // which objTypes does not record (newobj/box/GetType results
                // only). Without a hint TryMapObject returns null, the call
                // falls to the generated-name path and the vtable emitter
                // throws "callvirt owner System.Object not in translated
                // types". The receiver's DECLARED type is System.Object --
                // the same hint a `new object()` receiver gets -- so the
                // existing re-resolve below routes it to the runtime direct
                // icall_object_to_string (klass/box dispatch; NRE on null).
                // ToString-specific: every other Object member (GetHashCode/
                // Equals) maps without a receiver hint.
                if (md != null && recvType == null && args[0] is LocExpr && IsObjectToString(md, (int)ins.Operand))
                    recvType = "System.Object";
                // delechain: instance Delegate.Equals(object) -- csc emits the
                // callvirt with parent System.Object (Equals is inherited from
                // Object; the same Roslyn quirk as MemberInfo::get_Name), so
                // the default resolution routes it to the boxed-equality icall
                // (identity/klass compare -- WRONG for delegates: net48
                // compares the invocation LISTS: same methodPtr+target per
                // leaf, order matters, tree shape does not). Fold on a
                // delegate-typed receiver to the structural
                // icall_delegate_equals -- the same semantics as the
                // == / != fold above.
                if (md != null && args.Count == 2 && recvType != null && IsDelegateType(recvType, types)
                    && IsDelegateEqualsCall(md, (int)ins.Operand))
                {
                    var eq = new CallExpr
                    {
                        Function = $"call_{ins.Operand}",
                        ReturnsRef = false,
                        Resolved = new ResolvedMethod("il2cpp::runtime::icall_delegate_equals", 2, false, true, false,
                            new[] { false, false }, false),
                    };
                    eq.Args.AddRange(args);
                    Push(eq, ins);
                    return;
                }
                // M5 wiring: re-resolve even when the FIRST resolution came
                // back null -- MemberInfo::get_Name on a tracked GetType()
                // result resolves only with the receiver hint (the default
                // get_* path marks it STATIC and would reject the callvirt).
                // batch 158 (timercase): the receiver-hinted re-resolve ALSO
                // overrides an il2cpp:: first-pass result when the hint is a
                // System.Threading.Timer receiver -- the IDisposable::Dispose
                // using-block callvirt first resolves (hint-less) to the
                // sortedlist-enumerator no-op fallback (MethodRefResolver
                // IDisposable arm); the Timer hint must dispatch to
                // icall_timer_dispose instead.
                if (md != null && recvType != null
                    && (resolved == null || !resolved.CppName.StartsWith("il2cpp::", StringComparison.Ordinal)
                        // RF-REFL: the hint-less MemberInfo.get_Name fallback is a
                        // generic arm -- a tracked receiver must re-resolve so the
                        // more specific arm (ConstructorInfo ".ctor", member
                        // identity, tracked Type) wins (the b20 regression).
                        || resolved.ReresolveWithReceiver
                        || recvType == "System.Threading.Timer"
                        // batch 2026-10-05 (iopairprobe): a PTR-WIDTH receiver
                        // (the intptr_t temp an ldelema pushes -- the byref
                        // receiver of `vs[i].ToString(Inv)` on a value-type
                        // array) must be recognized as an ADDRESS so the
                        // value-type loads re-resolve to deref:* instead of
                        // unbox:* (the first pass read only the stack TOP --
                        // the trailing IFormatProvider arg -- and resolved
                        // "unbox:i", emitting the ill-formed
                        // `il2cpp_unbox_int32(static_cast<Il2CppObject*>(t4))`
                        // intptr_t->Il2CppObject* cast). Same doctrine as
                        // D-RND2-2 (line 2279) but for the by-the-time-args-known
                        // re-resolve.
                        || (args[0] is LocExpr ptrRecv
                            && LocWidth(ptrRecv.Name, ctx, tempWidths) is "ptr" or "p")
                        // icallwide: the Object::ToString/GetHashCode default
                        // arms are receiver-agnostic first-pass results -- a
                        // tracked typed receiver (Version/TimeSpan/...) must
                        // re-resolve to the domain icall.
                        || resolved.CppName.EndsWith("icall_object_get_hash_code")
                        || resolved.CppName.EndsWith("icall_object_to_string")
                        // boxwire: Object::Equals(object) on a tracked VALUE
                        // receiver (Guid / IPAddress). Roslyn emits a
                        // CONSTRAINED callvirt (ldloca + constrained. T), so
                        // the receiver arrives as the value ADDRESS while the
                        // hint-less first pass resolves the generic
                        // icall_boxed_equals_ext2 -- whose Il2CppObject*
                        // parameter cannot accept the byte-slot address. The
                        // receiver-hinted re-resolve routes to the
                        // value-vs-object helper (il2cpp_guid_equals_object /
                        // il2cpp_ipaddress_equals_object).
                        || (recvType is "System.Guid" or "System.Net.IPAddress"
                            && resolved.CppName.EndsWith("icall_boxed_equals_ext2"))
                        // ValueType.GetHashCode hint-less fallback (OfPtr) --
                        // a tracked typed receiver must re-resolve to the
                        // precise per-kind icall.
                        || resolved.CppName.EndsWith("icall_value_type_get_hash_code_of_ptr")
                        // P25 (listp25): the List<int>.Enumerator used in a
                        // foreach bodies ... the MoveNext/get_Current on the
                        // address-taken struct first resolve (hint-less) to the
                        // sortedlist-enumerator no-op fallback or the
                        // generated-name path; the tracked enumerator hint must
                        // re-resolve to the List enumerator icalls.
                        || recvType == "System.Collections.Generic.List`1.Enumerator"
                        // listrefdeep: the reference-element expansions track
                        // "...List`1.Enumerator_<element>" -- the tracked hint
                        // must re-resolve the hint-less i4 enumerator icall to
                        // the listobj enumerator arm (get_Current returns a
                        // managed reference there).
                        || (recvType != null && recvType.StartsWith("System.Collections.Generic.List`1.Enumerator_", StringComparison.Ordinal))
                        // P18 deflate: a DeflateStream receiver must re-resolve
                        // the inherited Stream Write/Read/Close to the deflate
                        // icalls -- the first resolution (TryMapStreamIO) returns
                        // an "il2cpp::" icall, which would otherwise skip the
                        // receiver-aware re-resolve.
                        || recvType == "System.IO.Compression.DeflateStream"
                        // P25d (dictp25): the Dictionary<string,int>.Enumerator
                        // (and the KeyCollection/ValueCollection enumerators)
                        // used in a foreach body push a ldloca ADDRESS receiver
                        // (LocAddrExpr) -- the first IDisposable::Dispose pass
                        // resolves hint-less to the ienum_dispose no-op fallback;
                        // the tracked enumerator hint must re-resolve to the
                        // (no-op) dict enumerator dispose with the deref:r cast.
                        || recvType == "System.Collections.Generic.Dictionary`2.Enumerator"
                        || recvType == "System.Collections.Generic.Dictionary`2.KeyCollection.Enumerator"
                        || recvType == "System.Collections.Generic.Dictionary`2.ValueCollection.Enumerator"
                        // Phase-1 dictgen: the int_string snapshot enumerator
                        // needs the same ldloca'd foreach-finally Dispose
                        // re-resolve (resolver maps it to the gen dispose
                        // with deref:r).
                        || recvType == "System.Collections.Generic.Dictionary`2.Enumerator_int_string"
                        // G4 B2 (b2b8): the generic Queue`1<int> /
                        // Stack`1<string> / HashSet<string> struct enumerator
                        // tokens -- the foreach finally (IDisposable::Dispose
                        // on the ldloca'd enumerator) must re-resolve from the
                        // hint-less ienum_dispose fallback to the mapped
                        // deref:r arm.
                        // W1: the reference-element expansions track
                        // "...Enumerator_<elem>" (the List precedent at :2603),
                        // so the gate is a PREFIX match.
                        || (recvType != null && recvType.StartsWith("System.Collections.Generic.Queue`1.Enumerator_", StringComparison.Ordinal))
                        || (recvType != null && recvType.StartsWith("System.Collections.Generic.Stack`1.Enumerator_", StringComparison.Ordinal))
                        || recvType == "System.Collections.Generic.HashSet`1.Enumerator_string"
                        // T2 W2/W3: the SortedSet/HashSet long/double/uint/ref
                        // struct enumerator tokens need the same ldloca'd
                        // foreach-finally Dispose re-resolve.
                        || (recvType != null && recvType.StartsWith("System.Collections.Generic.HashSet`1.Enumerator_", StringComparison.Ordinal))
                        || (recvType != null && recvType.StartsWith("System.Collections.Generic.SortedSet`1.Enumerator_", StringComparison.Ordinal))
                        // G4 residual (g4sorteddict): the SortedDictionary
                        // KeyCollection snapshot enumerator -- same ldloca'd
                        // foreach-finally Dispose re-resolve (the resolver arm
                        // at MethodRefResolver's IDisposable gate maps it to
                        // the sortedlist enumerator dispose with deref:r).
                        || recvType == "System.Collections.Generic.SortedDictionary`2+KeyCollection.Enumerator"
                        || recvType == "System.Collections.Generic.SortedDictionary`2.KeyCollection.Enumerator"
                        // sorte1: the SortedDictionary/SortedList<string,int>
                        // Values shared snapshot enumerator (tracked
                        // "System.Collections.SortedList.Enumerator") -- the
                        // foreach-finally Dispose on the ldloca'd enumerator
                        // needs the same re-resolve to the deref:r arm.
                        || recvType == "System.Collections.SortedList.Enumerator"))
                    resolved = MethodRefResolver.Resolve(md, (int)ins.Operand, types ?? Array.Empty<TypeDef>(),
                        recvType, args[0] is LocAddrExpr or StaticFieldAddrExpr
                            || (args[0] is LocExpr ptrRecv2 && LocWidth(ptrRecv2.Name, ctx, tempWidths) == "ptr"),
                        recvType);
                // P26 (bcldeep/charconv): FINAL resolution -- no further deferral
                // is possible after the receiver-hinted re-resolve. A phantom
                // (generated-name fallback on an external BCL owner) can never
                // link: reject it loud here instead of a late cmake failure. The
                // hint-less first pass (resolved from the 2230 call) is a legal
                // deferral -- this check runs after every remap had its chance.
                resolved = MethodRefResolver.RequireResolvable(md, (int)ins.Operand, resolved);

                // G2 (D3, constrained VALUE clone): a constrained callvirt on a
                // VALUE-typed generic parameter (`PairCon<T1,T2>.Cmp(T1 a)` with
                // T1=int -- the DEF body / per-instance clone) has an ADDRESS
                // receiver (csc emits `ldarga.s`). The lowered BCL interface
                // icall il2cpp_icomparable_compare_to takes the BOXED object
                // (Il2CppObject*); net48 `constrained.` boxes the value receiver
                // before the interface call. Box the value at the address --
                // mirror of the `box !0` IL the ARGUMENT already takes (the
                // emitter's box_{token} arm picks the value-width box by the
                // concrete clone arg).
                if (resolved != null && resolved.CppName == "il2cpp::il2cpp_icomparable_compare_to"
                    && constrainedToken >= 0 && args.Count > 0 && args[0] is LocAddrExpr vRecvAddr)
                {
                    var boxedRecv = new CallExpr { Function = $"box_{constrainedToken}", ReturnsRef = true };
                    boxedRecv.Args.Add(new LocExpr { Name = vRecvAddr.Name });
                    args[0] = boxedRecv;
                    arg0IsAddr = false;
                }
                // GN-N3 (p-iface): the constrained VALUE receiver may be an
                // INSTANCE FIELD address (`item.CompareTo(other)` with T=item
                // int32 -- ldflda, a FieldAddrExpr). Box the field VALUE (the
                // same box_{constrainedToken} wraps a FieldExpr -- the emitter
                // picks the value-width box by the concrete clone arg), exactly
                // like the LocAddrExpr arm above boxes the local VALUE.
                else if (resolved != null && resolved.CppName == "il2cpp::il2cpp_icomparable_compare_to"
                    && constrainedToken >= 0 && args.Count > 0 && args[0] is FieldAddrExpr vRecvFieldAddr)
                {
                    var boxedRecv = new CallExpr { Function = $"box_{constrainedToken}", ReturnsRef = true };
                    boxedRecv.Args.Add(new FieldExpr
                    {
                        Object = vRecvFieldAddr.Object,
                        FieldCppName = vRecvFieldAddr.FieldCppName,
                        OwnerFullName = vRecvFieldAddr.OwnerFullName,
                        IsRefField = false,
                        IsValueTypeObject = vRecvFieldAddr.IsValueTypeObject,
                    });
                    args[0] = boxedRecv;
                    arg0IsAddr = false;
                }

                // GN-2 (Nullable): a constrained callvirt on a Nullable`1<T>
                // receiver (ToString / Equals / GetHashCode -- the compiler
                // emits `ldloca; constrained. Nullable`1<T>; callvirt
                // Object::<member>`). The receiver is the ADDRESS of the
                // Il2CppObject* slot; the marker carries the underlying T in
                // ParamTypeNames[0] ("nullable:<T>") because the MemberRef
                // parent is System.Object (the token cannot be decoded).
                if (md != null && constrainedRecvType != null
                    && constrainedRecvType.StartsWith("System.Nullable`1_", StringComparison.Ordinal))
                {
                    var nbSuffix = constrainedRecvType["System.Nullable`1_".Length..];
                    var nbT = SuffixToManagedTypeName(nbSuffix);
                    if (IsObjectMember(md, (int)ins.Operand, "ToString", 0))
                    {
                        resolved = new ResolvedMethod("il2cpp::runtime::il2cpp_nullable_to_string", 0,
                            false, true, true, new[] { false }, true,
                            ParamTypeNames: new[] { "nullable:" + nbT }, ReturnTypeName: "System.String");
                    }
                    else if (IsObjectMember(md, (int)ins.Operand, "Equals", 1))
                    {
                        resolved = new ResolvedMethod("il2cpp::runtime::il2cpp_nullable_equals", 1,
                            false, true, false, new[] { false, false }, true,
                            ParamTypeNames: new[] { "nullable:" + nbT, "" });
                    }
                    else if (IsObjectMember(md, (int)ins.Operand, "GetHashCode", 0))
                    {
                        resolved = new ResolvedMethod("il2cpp::runtime::il2cpp_nullable_get_hash_code", 0,
                            false, true, false, new[] { false }, true,
                            ParamTypeNames: new[] { "nullable:" + nbT });
                    }
                }

                // constrained. VALUE-TYPE ToString (guidcase): even with the
                // constrained receiver hint, the Object-owner MemberRef
                // resolves to nothing -- TryMapObject's receiver-typed
                // ToString list has no Guid/DateTime entries (MethodMap.
                // Object.cs) and the BCL dispatch is owner-keyed. Fold to
                // the value-type domain icalls directly, mirroring the
                // exact shapes of MethodMap.Guid.cs (the "g" byte-pointer
                // cast for the Guid receiver slot) and MethodMap.DateTime.cs
                // (deref:dt); the delegate-equality fold above is the same
                // inline-construction precedent. Unmapped value types keep
                // the existing path (loud vtable failure downstream).
                if (md != null && constrainedRecvType != null && !IsRuntimeDirect(resolved)
                    && IsObjectToString(md, (int)ins.Operand))
                {
                    resolved = constrainedRecvType switch
                    {
                        // "g" NOT "": the synthetic resolution bypasses the
                        // MethodMap.Guid.cs ParamTypeNames, so the receiver's
                        // uint8_t[16]-slot ldloca address would otherwise
                        // reach the emitter castless (uint8_t (*)[16] does
                        // not decay -- the b59c6d9 blanket used to cover it;
                        // interlockedcase regression fix, 2026-08-05).
                        "System.Guid" => new ResolvedMethod("il2cpp::il2cpp_guid_to_string", 0, false, true, true,
                            new[] { true }, true, ParamTypeNames: new[] { "g" }),
                        "System.DateTime" => new ResolvedMethod("il2cpp::runtime::icall_datetime_to_string", 0, false, true, true,
                            new[] { false }, true, ParamTypeNames: new[] { "deref:dt" }),
                        // decimal-domain T3: constrained ToString on a decimal
                        // receiver -- the ldloca ADDRESS passes through uncast
                        // (MethodMap.Decimal.cs pointer ABI).
                        "System.Decimal" => new ResolvedMethod("il2cpp::runtime::icall_decimal_to_string", 0, false, true, true,
                            new[] { false }, true, ParamTypeNames: new[] { "" }),
                        // VAR-instantiation (M3 T->int): constrained ToString
                        // on an int32 receiver (ldobj !0 / box !0 shapes in
                        // generic bodies compiled for int, e.g.
                        // <>f__AnonymousType0<T>.ToString). "deref:i" reads
                        // the int32 value through the ldloca address (the
                        // Decimal-arm shape with a value-taking icall).
                        "System.Int32" => new ResolvedMethod("il2cpp::runtime::icall_int32_to_string", 0, false, true, true,
                            new[] { false }, true, ParamTypeNames: new[] { "deref:i" }),
                        // enumdeep (M7R2): constrained ToString on a USER ENUM
                        // value -- il2cpp_enum_to_string(Il2CppClass*, int64_t).
                        // PrependTypeClass injects the name-bearing enum class
                        // descriptor (&Cases_Color_class) so the runtime registry
                        // lookup finds the registered metadata and prints the
                        // member NAME ("Red" -- net48-identical); "enumval" reads
                        // the slot's value__ through the ldloca address.
                        // The metadata token decodes a NESTED enum to the '+'
                        // form ("Cases.Outer+E") while TypeDef.FullName is the
                        // SHORT name ("E"): match through TypeLookup (the single
                        // nested-name authority, Model/TypeLookup.cs).
                        _ when types?.Any(t => TypeLookup.Matches(t, constrainedRecvType)
                            && t.BaseTypeName == "System.Enum") == true
                            => new ResolvedMethod("il2cpp::runtime::il2cpp_enum_to_string", 0, false, true, true,
                                new[] { false }, true, ParamTypeNames: new[] { "enumval" },
                                PrependTypeClass: constrainedRecvType),
                        // bcldenum1: a BCL enum (System.DayOfWeek, ...) is a
                        // TypeRef -- it has NO TypeDef in the translated set, so
                        // the user-enum arm above misses it and the fold fell to
                        // the BOXED-enum icall (il2cpp_enum_boxed_to_string),
                        // whose `static_cast<Il2CppObject*>((&loc))` of an
                        // int32_t* enum carrier does not compile. The value slot
                        // is the SAME int32 carrier (D5), and the runtime already
                        // ships the name-bearing metadata for the whitelisted
                        // BCL enums (enumext.cpp's built-in registrar + the
                        // transpiler's BclEnumRegisterInfo register block), so
                        // the user-enum mapping is exactly right -- the class
                        // descriptor expression resolves through
                        // BoxClassDescriptorCpp, which registers a name-only
                        // descriptor for a whitelisted BCL enum (no _class
                        // symbol otherwise exists).
                        _ when MethodRefResolver.HasBclEnumNameTable(constrainedRecvType)
                            => new ResolvedMethod("il2cpp::runtime::il2cpp_enum_to_string", 0, false, true, true,
                                new[] { false }, true, ParamTypeNames: new[] { "enumval" },
                                PrependTypeClass: constrainedRecvType),
                        // nullable (GN-2) / user value types: a constrained
                        // ToString on a TRANSLATED user struct -- the runtime
                        // helper finds the type's reflected ToString override
                        // and calls it with the VALUE ADDRESS (the value-type
                        // this ABI). PrependTypeClass supplies the descriptor.
                        _ when constrainedRecvType != null && types?.Any(t =>
                                TypeLookup.Matches(t, constrainedRecvType) && t.IsValueType) == true
                            => new ResolvedMethod("il2cpp::runtime::il2cpp_user_struct_to_string", 0, false, true, true,
                                new[] { false }, true, ParamTypeNames: new[] { "vaddr" },
                                PrependTypeClass: constrainedRecvType),
                        _ => resolved,
                    };
                }

                // bcldenum1 (second gap, same root cause): constrained
                // GetHashCode on an enum VALUE CARRIER. `DayOfWeek.Saturday.
                // GetHashCode()` folds to the receiver-agnostic
                // icall_object_get_hash_code(Il2CppObject*), and the int32
                // enum slot's ldloca ADDRESS does not convert -- the same
                // "int32_t* -> Il2CppObject*" compile error as ToString. The
                // net48 hash of an enum is the underlying value's hash, so the
                // enum domain icall takes (klass, value) exactly like ToString.
                // NOTE: unlike the ToString arm above this one deliberately
                // overrides a RESOLVED runtime icall -- the receiver-agnostic
                // Object::GetHashCode mapping is precisely the wrong one here,
                // and the enum receiver type is the authority.
                if (md != null && constrainedRecvType != null
                    && IsObjectGetHashCode(md, (int)ins.Operand)
                    && (types?.Any(t => t.FullName == constrainedRecvType
                            && t.BaseTypeName == "System.Enum") == true
                        || MethodRefResolver.HasBclEnumNameTable(constrainedRecvType)))
                {
                    resolved = new ResolvedMethod("il2cpp::runtime::il2cpp_enum_get_hash_code", 0, false, true, false,
                        new[] { false }, true, ParamTypeNames: new[] { "enumval" },
                        PrependTypeClass: constrainedRecvType);
                }

                if (resolved is { IsInstance: false })
                {
                    throw new NotSupportedException($"callvirt on non-instance method (token {ins.Operand})");
                }

                // Rule 2j property folding: callvirt call sites of non-virtual
                // property accessors fold the same way (FieldExpr emission has
                // its own null check -- the semantics match callvirt's
                // null-check).
                if (md != null && accessors != null && accessors.TryGetValue((int)ins.Operand, out var accCallvirt))
                {
                    EmitFoldedAccessorCall(stmts, stack, ins, args, accCallvirt, types);
                    return;
                }

                // M5 wiring: MemberInfo::get_Name on a tracked GetType() result
                // is an identity -- the GetType icall already returns the type
                // NAME string. Fold the property to its receiver.
                if (resolved != null && resolved.CppName == ResolvedMethod.IdentityMarker)
                {
                    if (resolved.ReturnsValue || IlCallReturnsValue(md, (int)ins.Operand))
                    {
                        // ADO stage 1 (batch 152f): the folded Columns/Rows
                        // property carries its collection type into the
                        // receiver tracking (Rows.Count vs Columns.Count
                        // dispatch on the concrete collection).
                        if (resolved.ReturnTypeName != null && args.Count > 0 && args[0] is LocExpr rt)
                            objTypes[rt.Name] = BareManagedName(resolved.ReturnTypeName);
                        Push(args[0], ins);
                    }
                    return;
                }

                // M6 wiring (culturecase): the callvirt path must apply the
                // same marker folds as the call path -- NumberFormatInfo.
                // CurrentInfo.NumberDecimalSeparator emits `call
                // get_CurrentInfo` (-> NullMarker null) then `callvirt
                // get_NumberDecimalSeparator` on the folded-null receiver.
                // TryMapNumberFormat maps the getter to LiteralMarker + ".";
                // without this fold the ::lit:: marker leaked into the emitted
                // C++ (`::lit::.(nullptr)` -- uncompilable).
                if (resolved != null && resolved.CppName.StartsWith(ResolvedMethod.LiteralMarker, StringComparison.Ordinal))
                {
                    Push(new StringConstExpr { Value = resolved.CppName[ResolvedMethod.LiteralMarker.Length..] }, ins);
                    return;
                }
                if (resolved != null && resolved.CppName == ResolvedMethod.NullMarker)
                {
                    Push(new NullExpr { TypeName = resolved.ReturnTypeName }, ins);
                    return;
                }

                // tuple1 (ValueTuple): a callvirt on a tuple receiver.
                // Roslyn emits ToString/GetHashCode/Equals against
                // System.Object (the struct's overrides), so the first
                // resolution is the receiver-agnostic Object icall. The
                // receiver hint (objTypes, recorded by the newobj fold) names
                // the closed tuple instance -- route to the generated member
                // helpers ValueTupleSupport emits.
                if (md != null && recvType != null && ValueTupleSupport.IsValueTupleName(recvType)
                    && args.Count > 0)
                {
                    var tupleMember = MemberSimpleName(md, (int)ins.Operand);
                    var tupleResolved = tupleMember switch
                    {
                        "ToString" when args.Count == 1 =>
                            new ResolvedMethod(ValueTupleSupport.ToStringFn(recvType), 0, false, true, true,
                                new[] { true }, true, ParamTypeNames: new[] { "tupref" }),
                        // RF-REFL: GetHashCode returns int32 -- the result slot
                        // is a VALUE (int32_t), exactly like the Equals arm
                        // below. Marking it ReturnsRef=true made the IL int32
                        // temp an Il2CppObject* carrier, so the int32 CompareTo
                        // result assigned to the same slot failed to compile
                        // (vtmeta1).
                        "GetHashCode" when args.Count == 1 =>
                            // GetHashCode returns int32 (a VALUE, not a managed
                            // reference) -- ReturnsRef must be false or the
                            // result slot is declared Il2CppObject* and every
                            // int32 store into it fails (vtcreate1 loc4).
                            new ResolvedMethod(ValueTupleSupport.GetHashCodeFn(recvType), 0, false, true, false,
                                new[] { true }, true, ParamTypeNames: new[] { "tupref" }),
                        "Equals" when args.Count == 2 =>
                            new ResolvedMethod(ValueTupleSupport.EqualsFn(recvType), 1, false, true, false,
                                new[] { true, true }, true, ParamTypeNames: new[] { "tupref", "tupref" }),
                        // RF-REFL: IComparable.CompareTo(object) on a tuple --
                        // the structural element-wise default-order comparison
                        // (the receiver is a passthrough box, the tuple VALUE).
                        "CompareTo" when args.Count == 2 =>
                            new ResolvedMethod(ValueTupleSupport.CompareToFn(recvType), 1, false, true, false,
                                new[] { true, true }, true, ParamTypeNames: new[] { "tupref", "tupref" }),
                        // RF-REFL: IStructuralComparable.CompareTo(object,
                        // IComparer) -- the comparer-aware structural compare.
                        "CompareTo" when args.Count == 3 =>
                            new ResolvedMethod(ValueTupleSupport.CompareToComparerFn(recvType), 2, false, true, false,
                                new[] { true, true, true }, true, ParamTypeNames: new[] { "tupref", "tupref", "" }),
                        // RF-REFL: IStructuralEquatable.Equals(object,
                        // IEqualityComparer) -- the structural equality (the
                        // default element order; the comparer rides as a
                        // dropped slot, matching the default-comparer
                        // semantics the probes pin).
                        "Equals" when args.Count == 3 =>
                            new ResolvedMethod(ValueTupleSupport.EqualsFn(recvType), 1, false, true, false,
                                new[] { true, true, true }, true, ParamTypeNames: new[] { "tupref", "tupref", "drop" }),
                        _ => null,
                    };
                    if (tupleResolved != null)
                    {
                        ValueTupleSupport.Require(recvType);
                        var tcall = new CallExpr
                        {
                            Function = $"call_{ins.Operand}",
                            ReturnsRef = tupleResolved.ReturnsRef,
                            ResultKind = tupleResolved.ResultKind,
                            Resolved = tupleResolved,
                        };
                        tcall.Args.AddRange(args);
                        Push(tcall, ins);
                        return;
                    }
                }

                // M4 Task 5: System.String instance members map to runtime
                // helpers (Length etc.) -- a direct call, not vtable dispatch.
                // M5 wiring: the same holds for EVERY runtime icall instance
                // method (StringBuilder/Random/Hashtable/String members --
                // CppName starts with "il2cpp::").
                if (resolved is { IsPInvoke: false } && IsRuntimeDirect(resolved))
                {
                    var shelp = new CallExpr { Function = $"call_{ins.Operand}", ReturnsRef = resolved.ReturnsRef, ResultKind = resolved.ResultKind, Resolved = resolved, MemberProvenance = pendingMemberProvenance };
                    shelp.Args.AddRange(args);
                    if (resolved.ReturnsValue)
                    {
                        Push(shelp, ins);
                        // M5 wiring: record the result's managed type (GetType
                        // -> "System.Type"; StringBuilder methods -> the
                        // builder) so later calls on it resolve.
                        if (resolved.CppName == "il2cpp::runtime::icall_object_get_type" && stack.Peek() is LocExpr gt)
                        {
                            objTypes[gt.Name] = "System.Type"; // GetType() -> the type NAME string; .Name on it is identity
                        }
                        else if (resolved.ReturnTypeName != null && stack.Peek() is LocExpr rt)
                            objTypes[rt.Name] = BareManagedName(resolved.ReturnTypeName);
                    }
                    else
                    {
                        var stmt = new CallStmt { Function = $"call_{ins.Operand}", SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode), Resolved = resolved };
                        stmt.Args.AddRange(args);
                        stmts.Add(stmt);
                        // rv=false with a non-void .NET signature: mirror the
                        // IL return slot the mapping drops (see
                        // DiscardedValueExpr) -- keeps the shadow stack in
                        // lockstep with IL ("dup on empty stack" fix).
                        PushDiscardSlot(ins);
                    }
                    return;
                }

                // M3 Task 8: delegate Invoke -- the receiver is a delegate
                // object; the call dispatches through its methodPtr.
                if (IsDelegateInvoke(resolved, md, (int)ins.Operand, types))
                {
                    var invoke = new DelegateInvokeExpr
                    {
                        MethodToken = (int)ins.Operand,
                        ReturnsRef = resolved?.ReturnsRef ?? false,
                        // P2-HARDEN #3: carry the Invoke RETURN width so a
                        // result landing on a compiler temp (branch-merge join
                        // slots) is declared wide (int64_t/double/float) -- the
                        // full-width P-D2 #1 helper result must not truncate to
                        // an int32_t slot.
                        ResultKind = DelegateInvokeResultKind(md, (int)ins.Operand),
                    };
                    invoke.Args.AddRange(args);
                    if (resolved == null || resolved.ReturnsValue)
                    {
                        Push(invoke, ins);
                    }
                    else
                    {
                        var stmt = new CallStmt
                        {
                            Function = $"dinvoke_{ins.Operand}",
                            SourceOffset = ins.Offset,
                            SourceOpName = OpName(ins.OpCode)
                        };
                        stmt.Args.AddRange(args);
                        stmts.Add(stmt);
                        PushDiscardSlot(ins);
                    }
                    return;
                }
                var vcall = new VirtualCallExpr
                {
                    MethodToken = (int)ins.Operand,
                    ResultKind = resolved?.ResultKind,
                    // R2b P2 (ilistException): a VAR RETURN on a closed
                    // reference instantiation (IMyList`1<class System.Exception>
                    // -> System.Exception) IS a managed reference even when a
                    // resolved fallback (generated-name path) claims otherwise
                    // -- the receiver/slot declaration depends on it. A non-VAR
                    // return (get_Count -> int32) keeps the resolver's answer.
                    ReturnsRef = resolved?.ReturnsRef == true
                        || (md != null && ReturnIsVar(md, (int)ins.Operand)
                            && (VarArgRefFullName(md, (int)ins.Operand) != null
                                || SharedDefReturningObject(md, MetadataTokens.Handle((int)ins.Operand)))),
                    // Devirt S1 (G2): carry the receiver's tracked static type
                    // (objTypes/newobj/constrained results computed above) to
                    // the emitter -- EmitVirtualCall devirtualizes a call on an
                    // effectively-sealed receiver to a direct call.
                    ReceiverTypeName = recvType,
                };
                vcall.Args.AddRange(args);
                if (resolved == null || resolved.ReturnsValue)
                {
                    Push(vcall, ins);
                    // R2b P2 (ilistException receiver tracking): a callvirt on
                    // a closed generic instantiation whose method returns VAR
                    // (T) yields the instantiation's CONCRETE T -- the parent
                    // TypeSpec is authoritative. Record it on the result temp
                    // so the next call (ex.Message) resolves with a receiver
                    // hint. Only REFERENCE args are recorded (a value-type T
                    // keeps the existing slot semantics).
                    // P-G3 D1: a SHARED clone's VAR return is the boxed object
                    // slot (decision A -- the shared object ABI). Record the
                    // concrete T for VALUE instantiations too, so a later
                    // `ldloca slot; call int32::ToString()` knows the slot
                    // holds a BOXED value (the object, not an int32 address)
                    // and consumes it via unbox (SharedReturnConcreteType).
                    // D5: a USER VALUE TYPE shared return (Box<Color>.Get ->
                    // the boxed enum, Box<Pt>.Get -> the boxed struct) is
                    // UNBOXED HERE, at the return site: every consumer (a
                    // comparison `c == Color.Green`, WriteLine_i, a value-local
                    // store) then sees the concrete value, not the object.
                    // The pushed boxed temp is replaced by the unboxed value.
                    if (md != null && ReturnIsVar(md, (int)ins.Operand)
                        && VarReturnIsObjectSlot(md, (int)ins.Operand)
                        && (VarArgRefFullName(md, (int)ins.Operand) ?? SharedReturnConcreteType(md, (int)ins.Operand)) is string sharedRet)
                    {
                        var isUserValue = types?.FirstOrDefault(t => t.MatchesFullName(sharedRet) && IsUserValueType(t)) != null;
                        var retToken = SharedReturnConcreteToken(md, (int)ins.Operand);
                        if (isUserValue && retToken != null && stack.Peek() is LocExpr rtv)
                        {
                            // Replace the boxed temp on the stack with the
                            // unboxed value. Enum: icall_unbox_long (accepts
                            // enum boxes, int64 carrier) narrowing into the
                            // int32 slot; struct: unbox_{token} (the P25b cast
                            // emission). Push materializes the unbox into its
                            // own temp (ResultKind -> the value's slot width).
                            var concreteDef = types!.First(t => t.MatchesFullName(sharedRet));
                            Expr unboxed;
                            if (concreteDef.BaseTypeName == "System.Enum")
                                unboxed = new CallExpr
                                {
                                    Function = "il2cpp::runtime::icall_unbox_long",
                                    ReturnsRef = false,
                                    ResultKind = "l",
                                    Args = { rtv },
                                };
                            else
                                unboxed = new CallExpr
                                {
                                    Function = $"unbox_{retToken}",
                                    ReturnsRef = false,
                                    ResultKind = "us:" + TypeEmitter.TypeCppName(concreteDef),
                                    Args = { rtv },
                                };
                            var popped = stack.Pop();
                            Push(unboxed, ins);
                            _ = popped;
                        }
                        // D5: a BCL ENUM shared return (Box<DayOfWeek>.Get --
                        // System.DayOfWeek, a VALUETYPE TypeRef invisible to the
                        // user-assembly `types` list) is unboxed the same way
                        // (icall_unbox_long accepts enum boxes) so consumers
                        // compare the int32 carrier, not the boxed object.
                        else if (CodeGenerator.IsKnownBclEnumName(sharedRet) && stack.Peek() is LocExpr bclEnumRtv)
                        {
                            var popped = stack.Pop();
                            Push(new CallExpr
                            {
                                Function = "il2cpp::runtime::icall_unbox_long",
                                ReturnsRef = false,
                                ResultKind = "l",
                                Args = { bclEnumRtv },
                            }, ins);
                            _ = popped;
                        }
                        else if (stack.Peek() is LocExpr rtv2)
                        {
                            objTypes[rtv2.Name] = sharedRet;
                            // D5: record the concrete type's metadata token (only
                            // when it IS the instance's TypeSpec arg -- the VAR/object
                            // fallback has none) so a later store into a USER VALUE
                            // TYPE local can build the unbox_{token} bridge.
                            if (retToken is int srt)
                                sharedReturnTokens[rtv2.Name] = srt;
                        }
                    }
                }
                else
                {
                    var stmt = new CallStmt
                    {
                        Function = $"vcall_{ins.Operand}",
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode)
                    };
                    stmt.Args.AddRange(args);
                    stmts.Add(stmt);
                    PushDiscardSlot(ins);
                }
                return;
            }

            var call = new CallExpr
            {
                Function = $"call_{ins.Operand}",
                ReturnsRef = resolved?.ReturnsRef ?? false,
                ResultKind = resolved?.ResultKind,
                Resolved = resolved,
                // p-refl2-gapmatrix (B08): the member-VALUE provenance for a
                // non-virtual GetMethod/GetField (`call`, not callvirt).
                MemberProvenance = pendingMemberProvenance,
                // M4 Task 3: P/Invoke string args are marshalled at the call site.
                IsPInvoke = resolved?.IsPInvoke ?? false,
                // M4 Task 4: [DllImport(SetLastError=true)].
                SetLastError = resolved?.SetLastError ?? false,
            };
            if (resolved?.IsPInvoke == true && resolved.ParamTypeNames != null)
            {
                for (var i = 0; i < resolved.ParamTypeNames.Length; i++)
                    if (resolved.ParamTypeNames[i] is "r:System.String" or "u16:System.String" or "a:System.String")
                        call.StringArgIndices.Add(i);
            }
            // Instance method (non-virtual call): CLI stack order
            // [this, arg1..argN], this below the explicit args.
            // this is call arg 0 (the generated function signature arg0 = this).
            // Static methods have no this.
            if (resolved is { IsInstance: true })
            {
                if (stack.Count == 0)
                    throw new NotSupportedException($"call at offset 0x{ins.Offset:X4}: resolved {resolved.CppName} as INSTANCE (pops this) but the stack is empty -- likely a wrong static/instance classification");
                args.Insert(0, stack.Pop());
                // p-refl2-gapmatrix (B08): csc emits `call` (not callvirt) for
                // the non-virtual GetMethod/GetField -- record the member
                // provenance here too (the callvirt path records it above).
                pendingMemberProvenance = MemberProvenanceOf(md, (int)ins.Operand, args);
                call.MemberProvenance = pendingMemberProvenance;
            }

            // M5 wiring: for STATIC calls, re-resolve with the FIRST explicit
            // argument's tracked type -- Console.WriteLine(object) with a
            // known boxed value becomes the typed WriteLine (unboxed arg);
            // unknown object types fall through to the default mapping.
            // For non-virtual INSTANCE calls the receiver is args[0] -- its
            // address/value shape decides the receiver cast (ldloca -> deref,
            // boxed -> unbox); the pre-pop stack top was the LAST argument.
            // floatfix: a Linq AGGREGATE binding also re-resolves here. The
            // seed/result-selector forms are 3/4-arg static extensions -- the
            // SOURCE (a List<T> receiver) sits BELOW the seed/lambda on the IL
            // stack, so the pre-pop resolution can never see its tracked type
            // (arg0Type = the lambda, arg1Type = the seed) and the array-family
            // arm binds with the raw List object cast to Il2CppArray* (reading
            // the items pointer as a length). After the pop, args[0] IS the
            // first IL argument, so the aggregate icall re-resolves with the
            // real source type and the List gate dispatches the list domain.
            // b5-gaps section 74: the USER-COMPARER set operators over a List
            // receiver are arity-3 (source, value/second, comparer), so the
            // pre-pop resolution sees arg1Type = the VALUE and binds the ARRAY
            // comparer kernel with the List object cast to Il2CppArray* (the
            // D-2 defect class). When the FIRST explicit argument's tracked
            // type is a List`1, re-resolve so the receiver hint reaches the
            // list domain (the same List-receiver re-resolve the aggregate
            // family already relies on above).
            bool a0IsTrackedList = false;
            if (md != null && args.Count > 0 && resolved is { IsInstance: false })
            {
                var a0t = args[0] is LocExpr afl && objTypes.TryGetValue(afl.Name, out var att)
                    ? att
                    : args[0] is LocAddrExpr afla && objTypes.TryGetValue(afla.Name, out var atta)
                        ? atta
                        : null;
                a0IsTrackedList = a0t != null
                    && a0t.StartsWith("System.Collections.Generic.List`1", StringComparison.Ordinal);
            }
            if (md != null && args.Count > 0 && resolved is { IsInstance: false }
                && (a0IsTrackedList
                    || !resolved.CppName.StartsWith("il2cpp::", StringComparison.Ordinal)
                    || resolved.CppName.StartsWith("il2cpp::il2cpp_linq_aggregate", StringComparison.Ordinal)))
            {
                var a0Type = args[0] is LocExpr fl2 && objTypes.TryGetValue(fl2.Name, out var tt2) ? tt2 : null;
                // b5-gaps section 92: the re-resolve is a SECOND CHANCE for the
                // receiver hint -- it must never destroy a good first mapping.
                // It passes (receiverType = arg0Type = a0Type, arg1Type = null),
                // so for a List-source SelectMany the first resolution -- which
                // DID see arg1Type = the List and bound
                // il2cpp_linq_list_select_many -- fails inside Resolve (the
                // MethodSpec path throws "not wired in M5" instead of returning
                // null) and the exception took the whole method down
                // (probe-selectmanylist: a regression from section 74b's
                // a0IsTrackedList extension, unnoticed because the recent
                // regression lists omitted that anchor).
                if (a0Type != null)
                {
                    ResolvedMethod? reresolved = null;
                    try
                    {
                        reresolved = MethodRefResolver.Resolve(md, (int)ins.Operand, types ?? Array.Empty<TypeDef>(),
                            a0Type, args[0] is LocAddrExpr, a0Type);
                    }
                    catch (NotSupportedException)
                    {
                        // keep the first resolution
                    }
                    if (reresolved != null)
                        resolved = reresolved;
                }
            }
            else if (md != null && args.Count > 0 && resolved is { IsInstance: true })
            {
                // M5 wiring: value-type BCL receivers (Int32::ToString via
                // ldloca + call) -- the receiver cast depends on the receiver
                // being an ADDRESS (deref) vs a boxed object (unbox).
                // listrefdeep: the ADDRESS form must ALSO hit the objTypes
                // lookup by slot name -- the struct enumerator's members are
                // emitted as non-virtual `call`s on an ldloca receiver, and
                // without the hint the List<string> enumerator falls to the
                // i4 arm (get_Current returns int32 -> uncompilable). Same
                // P25 reasoning as the callvirt path above (line 2331).
                var rvType = args[0] is LocExpr rl3 && objTypes.TryGetValue(rl3.Name, out var rt3) ? rt3
                    : args[0] is LocAddrExpr rl3a && objTypes.TryGetValue(rl3a.Name, out var rt3a) ? rt3a
                    // timezone domain: the folded-null singletons
                    // (TimeZoneInfo.Local/Utc -- MethodMap.TimeZone.cs) carry
                    // the zone tag on the NullExpr; the call-path re-resolve
                    // dispatches the Local-vs-Utc offset branch (callvirt
                    // precedent: StackFold.cs:1283 -- NullExpr.TypeName).
                    : args[0] is NullExpr ne3 && ne3.TypeName != null ? ne3.TypeName
                    : null;
                // batch 2026-10-05 (iopairprobe): a PTR-WIDTH receiver (the
                // intptr_t temp an ldelema pushes -- `call Int32::ToString(
                // IFormatProvider)` on a value-type array element with an
                // explicit trailing arg) is an ADDRESS, never a boxed object.
                // Without it the first resolution read only the stack TOP
                // (the trailing arg) and picked "unbox:*" -- emitting the
                // ill-formed `il2cpp_unbox_int32(static_cast<Il2CppObject*>
                // (t4))` intptr_t->Il2CppObject* cast (same doctrine as
                // D-RND2-2 at line 2279, applied to the call path).
                var addr = args[0] is LocAddrExpr or StaticFieldAddrExpr or FieldAddrExpr
                    || (args[0] is LocExpr ptrR3 && LocWidth(ptrR3.Name, ctx, tempWidths) == "ptr");
                if (addr || rvType != null)
                    resolved = MethodRefResolver.Resolve(md, (int)ins.Operand, types ?? Array.Empty<TypeDef>(),
                        rvType, addr, rvType);
            }

            // Rule 2j property folding: a call site hitting the accessor table
            // -> field access.
            if (md != null && accessors != null && accessors.TryGetValue((int)ins.Operand, out var accCall))
            {
                EmitFoldedAccessorCall(stmts, stack, ins, args, accCall, types);
                return;
            }

            // M5 wiring: fold-time markers (see ResolvedMethod). These calls
            // are NOT emitted -- they fold to expressions:
            //   "::refeq::"  object.ReferenceEquals -> pointer comparison
            //   "::null::"   culture getters          -> null
            //   "::lit::X"   culture-fixed constants  -> the string literal
            //   "::identity::" String.ToString/...    -> the receiver
            if (resolved != null && resolved.CppName == ResolvedMethod.RefEqMarker)
            {
                if (args.Count != 2)
                    throw new NotSupportedException($"ReferenceEquals with {args.Count} arg(s) not supported");
                Push(new BinaryExpr { Op = BinaryOp.Ceq, Left = args[0], Right = args[1] }, ins);
                return;
            }
            if (resolved != null && resolved.CppName == ResolvedMethod.NullMarker)
            {
                // M6 (encoding domain): the folded null carries the getter's
                // tracked type (ReturnTypeName) -- Encoding.get_UTF8 etc. set
                // it so a callvirt GetBytes/GetString on the null singleton
                // receiver re-resolves with the receiver hint.
                Push(new NullExpr { TypeName = resolved.ReturnTypeName }, ins);
                return;
            }
            if (resolved != null && resolved.CppName.StartsWith(ResolvedMethod.LiteralMarker, StringComparison.Ordinal))
            {
                Push(new StringConstExpr { Value = resolved.CppName[ResolvedMethod.LiteralMarker.Length..] }, ins);
                return;
            }
            if (resolved != null && resolved.CppName == ResolvedMethod.IdentityMarker)
            {
                if (resolved.ReturnsValue || IlCallReturnsValue(md, (int)ins.Operand)) Push(args[0], ins);
                return;
            }
            // COM stage 14 (GetComInterfaceForObject): the managed Type -> IID
            // resolution happens at TRANSPILE time (the runtime Type object
            // cannot be resolved to an IID at compile time). The resolver
            // emitted the ComGetInterfaceMarker; inspect the FOLDED Type
            // argument (args[1] -- a typeof(IUnknown)/typeof(IDispatch)
            // constant StringConstExpr, the typeof fold at line 2106) and emit
            // the icall with the matching IID constant. Any other Type arg
            // (a non-constant Type, or a constant for an unsupported
            // interface) is LOUD -- never silently emitted.
            if (resolved != null && resolved.CppName == ResolvedMethod.ComGetInterfaceMarker)
            {
                if (args.Count != 2)
                    throw new NotSupportedException(
                        $"GetComInterfaceForObject with {args.Count} arg(s) not supported (expected exactly 2: object + Type)");
                string? iidConst = null;
                if (args[1] is StringConstExpr comTypeName)
                {
                    if (comTypeName.Value == "System.Runtime.InteropServices.ComTypes.IUnknown")
                        iidConst = "il2cpp::runtime::kComIidIUnknown";
                    else if (comTypeName.Value == "System.Runtime.InteropServices.ComTypes.IDispatch")
                        iidConst = "il2cpp::runtime::kComIidIDispatch";
                }
                if (iidConst == null)
                    throw new NotSupportedException(
                        "GetComInterfaceForObject supports only the constant typeof(IUnknown) / typeof(IDispatch) Type argument " +
                        "(the runtime Type object cannot be resolved to an IID at compile time)");
                var comCall = new CallExpr
                {
                    Function = $"call_{ins.Operand}",
                    ReturnsRef = false,
                    ResultKind = "ptr",
                    Resolved = new ResolvedMethod(
                        "il2cpp::runtime::icall_com_get_com_interface_for_object", 2, false, true, false,
                        new[] { false, false }, false,
                        ParamTypeNames: new[] { "" },
                        ResultKind: "ptr",
                        AppendLiterals: new[] { iidConst }),
                };
                comCall.Args.Add(args[0]); // the object (the Type arg is replaced by the IID literal)
                Push(comCall, ins);
                return;
            }
            // impl-p0b G2: the Marshal COM METADATA folds -- GetStartComSlot /
            // GetEndComSlot / IsTypeVisibleFromCom. Every answer is a pure
            // function of the type's metadata, so the fold pushes an int
            // CONSTANT (no runtime icall, no runtime cost). Oracle: net48 x64,
            // tests/cases/probe-p0b G2 section.
            //   GetStartComSlot(interface) = 3 + (4 unless the interface is
            //     [InterfaceType(InterfaceIsIUnknown)] -- the CLR default is
            //     InterfaceIsDual, so an attribute-less interface answers 7);
            //   GetEndComSlot = start + OWN instance-method count - 1 (a
            //     0-method IUnknown interface answers 2; INHERITED interface
            //     methods are NOT counted; property accessors and event
            //     add/remove are ordinary MethodDefs and ARE counted);
            //   a non-interface type answers -1 from BOTH members;
            //   a type hidden from COM ([ComVisible(false)] on the type, else
            //     on the assembly, else visible) makes BOTH slot members throw
            //     ArgumentException -- including a CLASS (measured: the
            //     visibility check runs BEFORE the -1 answer). A constant fold
            //     cannot throw at runtime, so that shape is rejected LOUD here
            //     (never a silently wrong constant).
            //   IsTypeVisibleFromCom = the same predicate as a bool constant.
            //     [ComVisible] is NOT inherited (a type deriving from a hidden
            //     base answers True).
            if (resolved != null && resolved.CppName is ResolvedMethod.ComStartSlotMarker
                                    or ResolvedMethod.ComEndSlotMarker
                                    or ResolvedMethod.ComTypeVisibleMarker)
            {
                string ComMemberName() => resolved!.CppName == ResolvedMethod.ComStartSlotMarker
                    ? "GetStartComSlot"
                    : resolved!.CppName == ResolvedMethod.ComEndSlotMarker ? "GetEndComSlot" : "IsTypeVisibleFromCom";
                if (args.Count != 1 || args[0] is not StringConstExpr comTypeValue
                    || comTypeValue.TypeToken is not int comToken)
                    throw new NotSupportedException(
                        $"Marshal.{ComMemberName()} requires a CONSTANT typeof(T) Type argument " +
                        "(the runtime Type object cannot be resolved to metadata at transpile time)");
                var comHandle = MetadataTokens.Handle(comToken);
                if (md == null || comHandle.Kind != HandleKind.TypeDefinition)
                    throw new NotSupportedException(
                        $"Marshal.{ComMemberName()}: only a type DEFINED in the transpiled assembly is " +
                        "supported (an external-assembly TypeRef or a generic/array TypeSpec has no " +
                        "readable metadata in this reader)");
                var comTdHandle = (TypeDefinitionHandle)comHandle;
                var comTd = md.GetTypeDefinition(comTdHandle);
                // net48 [ComVisible] resolution: type-level attribute wins, then
                // the ASSEMBLY-level one, then the CLR default (visible).
                // ComVisibleAttribute is NOT inherited (measured: a type
                // deriving from a hidden base answers True).
                var comVisible = MetadataLoader.ComVisibleAttributeValue(md, comTd.GetCustomAttributes())
                    ?? (md.IsAssembly
                        ? MetadataLoader.ComVisibleAttributeValue(
                            md, md.GetAssemblyDefinition().GetCustomAttributes())
                        : null);
                var comVisibleFinal = comVisible ?? true;
                if (resolved.CppName == ResolvedMethod.ComTypeVisibleMarker)
                {
                    Push(new IntConstExpr { Value = comVisibleFinal ? 1 : 0 }, ins);
                    return;
                }
                if (!comVisibleFinal)
                    throw new NotSupportedException(
                        $"Marshal.{ComMemberName()}(typeof({md.GetFullName(comTdHandle)})): the type is not visible " +
                        "from COM ([ComVisible(false)]), which net48 answers with ArgumentException -- a " +
                        "transpile-time constant fold cannot throw at runtime, so this call site is rejected LOUD");
                if ((comTd.Attributes & System.Reflection.TypeAttributes.Interface) == 0)
                {
                    Push(new IntConstExpr { Value = -1 }, ins);
                    return;
                }
                var comIfaceType = MetadataLoader.InterfaceTypeAttributeValue(md, comTd.GetCustomAttributes());
                // InterfaceType values: 1 = InterfaceIsIUnknown, 2 = InterfaceIsIDispatch,
                // 3 = InterfaceIsDual; absent = the CLR default (dual).
                var comStart = 3 + (comIfaceType == 1 ? 0 : 4);
                if (resolved.CppName == ResolvedMethod.ComStartSlotMarker)
                {
                    Push(new IntConstExpr { Value = comStart }, ins);
                    return;
                }
                var comOwn = 0;
                foreach (var comMh in comTd.GetMethods())
                {
                    var comMd = md.GetMethodDefinition(comMh);
                    if ((comMd.Attributes & System.Reflection.MethodAttributes.Static) == 0) comOwn++;
                }
                Push(new IntConstExpr { Value = comStart + comOwn - 1 }, ins);
                return;
            }
            // impl-p0b G3: Marshal.GetTypedObjectForIUnknown(pUnk, Type) -- the
            // Type argument becomes the IID literal (transpile-time resolution,
            // the GetComInterfaceForObject precedent). typeof(object) keeps the
            // NULL iid (net48 performs no QI for it); any other type must be an
            // interface DEFINED in this assembly carrying [Guid], whose 16
            // bytes are emitted as a byte-escaped string literal (the .NET
            // Guid byte layout the runtime compares). Everything else is LOUD.
            if (resolved != null && resolved.CppName == ResolvedMethod.ComTypedObjectMarker)
            {
                if (args.Count != 2)
                    throw new NotSupportedException(
                        $"GetTypedObjectForIUnknown with {args.Count} arg(s) not supported (expected 2: IntPtr + Type)");
                string iidExpr;
                if (args[1] is StringConstExpr typedTypeValue)
                {
                    if (typedTypeValue.Value == "System.Object")
                    {
                        iidExpr = "nullptr";
                    }
                    else if (typedTypeValue.TypeToken is int typedTok
                             && md != null && MetadataTokens.Handle(typedTok).Kind == HandleKind.TypeDefinition)
                    {
                        var typedTd = (TypeDefinitionHandle)MetadataTokens.Handle(typedTok);
                        if ((md.GetTypeDefinition(typedTd).Attributes & System.Reflection.TypeAttributes.Interface) == 0)
                            throw new NotSupportedException(
                                $"GetTypedObjectForIUnknown(typeof({md.GetFullName(typedTd)})): only typeof(object) and a " +
                                "[Guid]-annotated interface defined in the transpiled assembly are supported");
                        var typedIid = MetadataLoader.GuidAttributeValue(
                            md, md.GetTypeDefinition(typedTd).GetCustomAttributes());
                        if (typedIid == null)
                            throw new NotSupportedException(
                                $"GetTypedObjectForIUnknown(typeof({md.GetFullName(typedTd)})): the interface carries no " +
                                "[Guid] attribute, so its IID cannot be resolved at transpile time");
                        var sb = new System.Text.StringBuilder("reinterpret_cast<const uint8_t*>(\"");
                        foreach (var by in typedIid) sb.Append("\\x").Append(by.ToString("x2"));
                        sb.Append("\")");
                        iidExpr = sb.ToString();
                    }
                    else
                    {
                        throw new NotSupportedException(
                            "GetTypedObjectForIUnknown requires a constant typeof(T) whose type is defined in the " +
                            "transpiled assembly (an external-assembly TypeRef has no readable metadata here)");
                    }
                }
                else
                {
                    throw new NotSupportedException(
                        "GetTypedObjectForIUnknown requires a CONSTANT typeof(T) Type argument " +
                        "(the runtime Type object cannot be resolved to an IID at transpile time)");
                }
                var typedCall = new CallExpr
                {
                    Function = $"call_{ins.Operand}",
                    ReturnsRef = true,
                    Resolved = new ResolvedMethod(
                        "il2cpp::runtime::icall_com_get_typed_object_for_iunknown", 2, false, true, false,
                        new[] { false, false }, false,
                        ParamTypeNames: new[] { "n" },
                        AppendLiterals: new[] { iidExpr }),
                };
                typedCall.Args.Add(args[0]); // the IntPtr (the Type arg is replaced by the IID literal)
                Push(typedCall, ins);
                return;
            }
            // impl-p0b G4: Marshal.GetMethodInfoForComSlot(Type, int, ref
            // ComMemberType). The slot -> method mapping is METADATA (the same
            // own-method declaration order GetStartComSlot/GetEndComSlot count),
            // so the fold resolves the name + ComMemberType and emits a call to
            // the runtime helper, which writes the ComMemberType through the
            // caller's ref local and returns the method name as the MethodInfo
            // VALUE (the name-only member model Type.GetMethod uses).
            // net48 x64 oracle (tests/cases/probe-p0b G4): a slot outside
            // [start, end] -> ArgumentOutOfRangeException; a property accessor
            // reports ComMemberType.PropGet / PropSet and the PROPERTY name
            // (get_B -> "B").
            if (resolved != null && resolved.CppName == ResolvedMethod.ComMethodInfoMarker)
            {
                if (args.Count != 3 || args[0] is not StringConstExpr miTypeValue
                    || miTypeValue.TypeToken is not int miToken
                    || args[1] is not IntConstExpr miSlotArg
                    || args[2] is not LocAddrExpr)
                    throw new NotSupportedException(
                        "GetMethodInfoForComSlot requires a CONSTANT typeof(T) first argument and an ldc.i4 slot " +
                        "with the ref ComMemberType local as the third argument");
                if (md == null || MetadataTokens.Handle(miToken).Kind != HandleKind.TypeDefinition)
                    throw new NotSupportedException(
                        "GetMethodInfoForComSlot: only a type DEFINED in the transpiled assembly is supported " +
                        "(an external-assembly TypeRef has no readable metadata in this reader)");
                var miTdHandle = (TypeDefinitionHandle)MetadataTokens.Handle(miToken);
                var miTd = md.GetTypeDefinition(miTdHandle);
                var miVisible = MetadataLoader.ComVisibleAttributeValue(md, miTd.GetCustomAttributes())
                    ?? (md.IsAssembly
                        ? MetadataLoader.ComVisibleAttributeValue(
                            md, md.GetAssemblyDefinition().GetCustomAttributes())
                        : null) ?? true;
                if (!miVisible)
                    throw new NotSupportedException(
                        $"GetMethodInfoForComSlot(typeof({md.GetFullName(miTdHandle)})): the type is not visible from " +
                        "COM ([ComVisible(false)]), which net48 answers with ArgumentException -- a transpile-time " +
                        "fold cannot throw at runtime, so this call site is rejected LOUD");
                if ((miTd.Attributes & System.Reflection.TypeAttributes.Interface) == 0)
                    throw new NotSupportedException(
                        $"GetMethodInfoForComSlot(typeof({md.GetFullName(miTdHandle)})): a non-interface type is not " +
                        "supported (net48 answers ArgumentOutOfRangeException for it)");
                var miIfaceType = MetadataLoader.InterfaceTypeAttributeValue(md, miTd.GetCustomAttributes());
                var miStart = 3 + (miIfaceType == 1 ? 0 : 4);
                var miOwn = 0;
                foreach (var miMh in miTd.GetMethods())
                {
                    var miMd = md.GetMethodDefinition(miMh);
                    if ((miMd.Attributes & System.Reflection.MethodAttributes.Static) != 0) continue;
                    miOwn++;
                }
                var miIndex = miSlotArg.Value - miStart;
                var miValid = miIndex >= 0 && miIndex < miOwn;
                var miName = "";
                var miMemberType = 0; // ComMemberType.Method
                if (miValid)
                {
                    var miOrdinal = 0;
                    foreach (var miMh in miTd.GetMethods())
                    {
                        var miMd = md.GetMethodDefinition(miMh);
                        if ((miMd.Attributes & System.Reflection.MethodAttributes.Static) != 0) continue;
                        if (miOrdinal++ != miIndex) continue;
                        var raw = md.GetString(miMd.Name);
                        if (raw.StartsWith("get_", StringComparison.Ordinal))
                        {
                            miName = raw[4..];
                            miMemberType = 1; // ComMemberType.PropGet
                        }
                        else if (raw.StartsWith("set_", StringComparison.Ordinal))
                        {
                            miName = raw[4..];
                            miMemberType = 2; // ComMemberType.PropSet
                        }
                        else
                        {
                            miName = raw;
                        }
                        break;
                    }
                }
                var miCall = new CallExpr
                {
                    Function = $"call_{ins.Operand}",
                    ReturnsRef = true,
                    Resolved = new ResolvedMethod(
                        "il2cpp::runtime::icall_com_method_info_for_slot", 4, false, true, false,
                        new[] { false, false, false, false }, false,
                        ParamTypeNames: new[] { "i", "i", "", "addr:i" }),
                };
                miCall.Args.Add(new IntConstExpr { Value = miValid ? 1 : 0 });
                miCall.Args.Add(new IntConstExpr { Value = miMemberType });
                miCall.Args.Add(new StringConstExpr { Value = miName });
                miCall.Args.Add(args[2]); // the ref ComMemberType local address
                Push(miCall, ins);
                return;
            }
            // Work order #24 (3-arg) + nb2 IO-PATH2 (4-arg): Path.Combine(
            // string, string, string[, string]) -- the params-array form.
            // The resolver emits the NestedCombineMarker (the runtime icall
            // takes exactly (string,string)); expand the N-ary call into a
            // nested CallExpr tree icall_path_combine(icall_path_combine(a,b),c)
            // (left fold, matching the 2-arg semantics order). The emitter
            // handles a CallExpr as an argument (EmitIcallCallArgs recurses
            // through EmitExpr), so the nested tree emits as nested C++
            // calls. (iocase line 69: Directory.CreateDirectory(
            // Path.Combine(dir, "nested", "deep")).)
            if (resolved != null && resolved.CppName == ResolvedMethod.NestedCombineMarker)
            {
                if (args.Count < 2)
                    throw new NotSupportedException($"nested-combine marker with {args.Count} arg(s) (expected >= 2)");
                var combine2 = new ResolvedMethod("il2cpp::runtime::icall_path_combine", 2, false, true, true,
                    new[] { false, false }, false);
                CallExpr acc = new CallExpr
                {
                    Function = $"call_{ins.Operand}",
                    ReturnsRef = true,
                    Resolved = combine2,
                };
                acc.Args.Add(args[0]);
                acc.Args.Add(args[1]);
                for (int ai = 2; ai < args.Count; ++ai)
                {
                    var outer = new CallExpr
                    {
                        Function = $"call_{ins.Operand}",
                        ReturnsRef = true,
                        Resolved = combine2,
                    };
                    outer.Args.Add(acc);
                    outer.Args.Add(args[ai]);
                    acc = outer;
                }
                Push(acc, ins);
                if (stack.Peek() is LocExpr ncTemp)
                    objTypes[ncTemp.Name] = "System.String";
                return;
            }
            // p0linq4 (D4): the fused-composite family suffix of a resolved
            // PRIMARY OrderBy(keySelector) entry, or null when the entry is not
            // one of them. The suffix is "_i4" for the int32 source
            // (il2cpp_linq_order_by_key[_desc]_i4) and "_i8_i4"/"_i8_i8" for the
            // int64 source (il2cpp_linq_order_by_key[_desc]_i8_<keyfam>).
            static string? ThenByPrimaryEntrySuffix(string? cppName) => cppName switch
            {
                "il2cpp::il2cpp_linq_order_by_key_i4"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_i4" => "_i4",
                "il2cpp::il2cpp_linq_order_by_key_i4_i8"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_i4_i8" => "_i4_i8",
                "il2cpp::il2cpp_linq_order_by_key_i4_d"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_i4_d" => "_i4_d",
                // impl-floatkey: the float-key primary entries (the float
                // family has its own reader -- b5-gaps 搂14).
                "il2cpp::il2cpp_linq_order_by_key_i4_f"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_i4_f" => "_i4_f",
                "il2cpp::il2cpp_linq_order_by_key_i8_i4"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_i8_i4" => "_i8_i4",
                "il2cpp::il2cpp_linq_order_by_key_i8_i8"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_i8_i8" => "_i8_i8",
                "il2cpp::il2cpp_linq_order_by_key_i8_d"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_i8_d" => "_i8_d",
                "il2cpp::il2cpp_linq_order_by_key_i8_f"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_i8_f" => "_i8_f",
                // impl-linqleft: the DECIMAL element carriers (a decimal[]
                // source and a decimal GroupBy bucket) for EVERY primary key
                // family the decimal OrderBy supports. The secondary family
                // code is appended from the ThenBy marker's AppendLiterals.
                "il2cpp::il2cpp_linq_order_by_key_dec_i4"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_dec_i4" => "src_dec_i4",
                "il2cpp::il2cpp_linq_order_by_key_dec_i8"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_dec_i8" => "src_dec_i8",
                "il2cpp::il2cpp_linq_order_by_key_dec_d"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_dec_d" => "src_dec_d",
                // impl-orderfam: the DECIMAL-source FLOAT-key primaries (the
                // float family has its own reader -- b5-gaps 搂14).
                "il2cpp::il2cpp_linq_order_by_key_dec_f"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_dec_f" => "src_dec_f",
                "il2cpp::il2cpp_linq_order_by_key_dec_dec"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_dec_dec" => "src_dec_dec",
                "il2cpp::il2cpp_linq_order_by_key_dec_dt"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_dec_dt" => "src_dec_dt",
                "il2cpp::il2cpp_linq_order_by_key_dec_guid"
                    or "il2cpp::il2cpp_linq_order_by_key_desc_dec_guid" => "src_dec_guid",
                "il2cpp::il2cpp_linq_grouping_order_by_key_dec_i4"
                    or "il2cpp::il2cpp_linq_grouping_order_by_key_desc_dec_i4" => "grp_dec_i4",
                "il2cpp::il2cpp_linq_grouping_order_by_key_dec_i8"
                    or "il2cpp::il2cpp_linq_grouping_order_by_key_desc_dec_i8" => "grp_dec_i8",
                "il2cpp::il2cpp_linq_grouping_order_by_key_dec_d"
                    or "il2cpp::il2cpp_linq_grouping_order_by_key_desc_dec_d" => "grp_dec_d",
                // impl-orderfam: the DECIMAL-bucket FLOAT-key primaries.
                "il2cpp::il2cpp_linq_grouping_order_by_key_dec_f"
                    or "il2cpp::il2cpp_linq_grouping_order_by_key_desc_dec_f" => "grp_dec_f",
                "il2cpp::il2cpp_linq_grouping_order_by_key_dec_dec"
                    or "il2cpp::il2cpp_linq_grouping_order_by_key_desc_dec_dec" => "grp_dec_dec",
                "il2cpp::il2cpp_linq_grouping_order_by_key_dec_dt"
                    or "il2cpp::il2cpp_linq_grouping_order_by_key_desc_dec_dt" => "grp_dec_dt",
                "il2cpp::il2cpp_linq_grouping_order_by_key_dec_guid"
                    or "il2cpp::il2cpp_linq_grouping_order_by_key_desc_dec_guid" => "grp_dec_guid",
                // impl-vtsel: the value-struct element-source primaries
                // (DateTime / TimeSpan / Guid / DateTimeOffset). The source family
                // and the PRIMARY key family ride the OrderBy entry's appended
                // literals; the SECONDARY key family rides the ThenBy marker's.
                "il2cpp::il2cpp_linq_order_by_key_vt" => "src_vt",
                // impl-collation: the REFERENCE-element primaries. The
                // reference OrderBy entries keep the direction in an appended
                // literal (0 = ascending, 1 = descending) rather than in the
                // entry name, so the suffix here only names the key family and
                // the source shape ("arr" for an array source, "list" for a
                // List<T> receiver -- the list entry keeps the element kind
                // after the desc flag).
                "il2cpp::il2cpp_linq_order_by_key_ref_i4" => "_ref_arr_i4",
                "il2cpp::il2cpp_linq_order_by_key_ref_i8" => "_ref_arr_i8",
                "il2cpp::il2cpp_linq_order_by_key_ref_d" => "_ref_arr_d",
                "il2cpp::il2cpp_linq_order_by_key_ref_f" => "_ref_arr_f",
                "il2cpp::il2cpp_linq_order_by_key_ref_ref" => "_ref_arr_ref",
                "il2cpp::il2cpp_linq_order_by_key_ref_string" => "_ref_arr_string",
                // impl-collation: the same over a List<T> receiver (the list is
                // snapped once; the desc flag is AppendLiterals[0]).
                // impl-listchain: the INT32-element List<T> primaries. The
                // list entry snaps the list ONCE and runs the array key
                // kernel, so a ThenBy over it can fuse exactly like the array
                // source (the composite stays on the list entry). Without
                // these arms a two-key ThenBy over List<int> fell through to
                // the "does not name an OrderBy" LOUD.
                "il2cpp::il2cpp_linq_list_order_by_key_i4" => "list_i4",
                "il2cpp::il2cpp_linq_list_order_by_key_desc_i4" => "list_i4",
                "il2cpp::il2cpp_linq_list_order_by_key_ref_i4" => "_ref_list_i4",
                "il2cpp::il2cpp_linq_list_order_by_key_ref_i8" => "_ref_list_i8",
                "il2cpp::il2cpp_linq_list_order_by_key_ref_d" => "_ref_list_d",
                "il2cpp::il2cpp_linq_list_order_by_key_ref_f" => "_ref_list_f",
                "il2cpp::il2cpp_linq_list_order_by_key_ref_ref" => "_ref_list_ref",
                "il2cpp::il2cpp_linq_list_order_by_key_ref_string" => "_ref_list_string",
                // impl-vtsel: the same over a List<struct> receiver (the list
                // snapshot rides the array kernel; the composite stays on the
                // list entry so the snapshot happens once).
                "il2cpp::il2cpp_linq_list_order_by_key_vt" => "list_vt",
                _ => null,
            };
            // b5-gaps §44: read the ALREADY-FUSED key list out of a ThenBy
            // composite call. The two-key numeric composite carries
            // (k1desc, k2desc, k1fam, k2fam) with args (src, k1, k2); a chain
            // call carries (d1..d4, f1..f4) with args (src, k1..k4) and a
            // family of -1 terminating the list. Anything else (decimal /
            // value-struct / reference / List composites) returns false and
            // keeps the two-key (or LOUD) behaviour -- those families have no
            // chain kernel yet.
            // impl-listchain: a List<int> OrderBy is a legitimate CHAIN SEED
            // even though it has no two-key composite with a direction column
            // (the runtime list_then_by_key_i4 entries take no k1desc). The
            // chained entry accepts both directions and the element kind, so a
            // two-key ThenBy over List<int> rides the chain with two levels.
            static bool ListPrimarySeed(CallExpr call, out Expr src, out Expr sel,
                out string desc, out string listKind)
            {
                src = null!;
                sel = null!;
                desc = "0";
                listKind = "";
                var name = call.Resolved?.CppName;
                if (name is not ("il2cpp::il2cpp_linq_list_order_by_key_i4"
                                 or "il2cpp::il2cpp_linq_list_order_by_key_desc_i4"))
                    return false;
                // The List<int> OrderBy arm declares TWO arguments (the list and
                // the key selector) plus the element-kind literal.
                if (call.Args.Count != 2 || call.Resolved!.AppendLiterals is not { Length: >= 1 })
                    return false;
                src = call.Args[0];
                sel = call.Args[1];
                desc = name.EndsWith("_desc_i4", StringComparison.Ordinal) ? "1" : "0";
                listKind = call.Resolved.AppendLiterals[^1];
                return true;
            }
            // b5-gaps section 47: a bare DOUBLE/FLOAT-ELEMENT array OrderBy
            // primary is a legal chain seed (it has no two-key composite with a
            // direction column, so a plain 2-level ThenBy over a double[] /
            // float[] source also rides the chain). The entry name encodes BOTH
            // the source family and the primary key family:
            //   order_by_key_d        -- double source, double key
            //   order_by_key_d_i4     -- double source, int32 key
            //   order_by_key_f_i8     -- float source, int64 key ...
            // and the descending variants prefix "_desc".
            static bool WideArrayPrimarySeed(CallExpr call, out Expr src,
                out Expr sel, out string desc, out string priFam, out string srcFam)
            {
                src = null!;
                sel = null!;
                desc = "0";
                priFam = "";
                srcFam = "";
                var name = call.Resolved?.CppName;
                if (name == null) return false;
                // b5-gaps section 49: the name-based parser below also matches
                // the LIST wide primaries (list_order_by_key_d / _f ...) -- the
                // array chain entry got a List where an Il2CppArray* was
                // expected. A `list_` name belongs to WideListPrimarySeed.
                if (name.Contains("list_", StringComparison.Ordinal)) return false;
                string? body;
                if (name.Contains("order_by_key_desc", StringComparison.Ordinal))
                {
                    desc = "1";
                    var i = name.IndexOf("order_by_key_desc", StringComparison.Ordinal);
                    body = name.Substring(i + "order_by_key_desc".Length);
                }
                else if (name.Contains("order_by_key", StringComparison.Ordinal))
                {
                    var i = name.IndexOf("order_by_key", StringComparison.Ordinal);
                    body = name.Substring(i + "order_by_key".Length);
                }
                else body = null;
                if (body == null || body.Length < 1) return false;
                if (body.StartsWith("_", StringComparison.Ordinal)) body = body.Substring(1);
                if (body.Length < 1 || body[0] is not ('d' or 'f')) return false;
                srcFam = body[0] == 'd' ? "d" : "f";
                string keyPart = body.Substring(1);
                if (keyPart.Length == 0)
                {
                    // No underscore: the key family is the SOURCE family (the
                    // plain _d / _f double-key / float-key faces).
                    priFam = srcFam == "d" ? "2" : "3";
                }
                else
                {
                    if (keyPart[0] != '_') return false;
                    string kf = keyPart.Substring(1);
                    priFam = kf switch
                    {
                        "i4" => "0",
                        "i8" => "1",
                        "d" => "2",
                        "f" => "3",
                        _ => "",
                    };
                    if (priFam.Length == 0) return false;
                }
                if (call.Args.Count != 2) return false;
                src = call.Args[0];
                sel = call.Args[1];
                return true;
            }
            // b5-gaps section 73: a NARROW-ELEMENT array OrderBy primary is a
            // chain seed. The single narrow OrderBy entry
            // (il2cpp_linq_order_by_key_narrow) keeps its DROPCOVERS in
            // AppendLiterals = {keyCell, srcCell, desc}: the KEY CELL code
            // (0..10), the SOURCE cell code (0..6) and the direction flag.
            // The chain key-family code is derived from the KEY cell exactly
            // like the numeric chain families: 0..7 -> 0 (int32 carrier incl.
            // narrow/uint/int32 keys), 8 -> 1 (int64), 10 -> 2 (double),
            // 9 -> 3 (float).
            static bool NarrowArrayPrimarySeed(CallExpr call, out Expr src,
                out Expr sel, out string desc, out string priFam, out string srcCell)
            {
                src = null!;
                sel = null!;
                desc = "0";
                priFam = "";
                srcCell = "";
                var name = call.Resolved?.CppName;
                if (name != "il2cpp::il2cpp_linq_order_by_key_narrow") return false;
                if (call.Args.Count != 2 || call.Resolved!.AppendLiterals is not { Length: 3 })
                    return false;
                var lits = call.Resolved.AppendLiterals;
                src = call.Args[0];
                sel = call.Args[1];
                desc = lits[2] == "1" ? "1" : "0";
                srcCell = lits[1];
                priFam = lits[0] switch
                {
                    "0" or "1" or "2" or "3" or "4" or "5" or "6" or "7" => "0",
                    "8" => "1",
                    "10" => "2",
                    "9" => "3",
                    _ => "",
                };
                return priFam.Length != 0 && srcCell.Length != 0;
            }
            // b5-gaps section 49: a WIDE-ELEMENT List<T> OrderBy primary is also
            // a legal chain seed. The list primaries are
            //   il2cpp_linq_list_order_by_key[_desc]_d    -- List<double>, double key
            //   il2cpp_linq_list_order_by_key[_desc]_f    -- List<double>, float key
            //   il2cpp_linq_list_order_by_key[_desc]_f_<kf> -- List<float>, <kf> key
            //   (kf in {i4,i8,d,f}).
            // The element kind literal is AppendLiterals[^1] (K: "3"=float,
            // "4"=double). The primary key family is derived from the name.
            static bool WideListPrimarySeed(CallExpr call, out Expr src,
                out Expr sel, out string desc, out string priFam, out string srcFam,
                out string listKind)
            {
                src = null!;
                sel = null!;
                desc = "0";
                priFam = "";
                srcFam = "";
                listKind = "";
                var name = call.Resolved?.CppName;
                if (name == null) return false;
                var lit = call.Resolved!.AppendLiterals;
                if (call.Args.Count != 2 || lit is not { Length: >= 1 }) return false;
                listKind = lit[^1];
                if (listKind is not ("3" or "4")) return false;
                string? body;
                if (name.Contains("list_order_by_key_desc", StringComparison.Ordinal))
                {
                    desc = "1";
                    var i = name.IndexOf("list_order_by_key_desc", StringComparison.Ordinal);
                    body = name.Substring(i + "list_order_by_key_desc".Length);
                }
                else if (name.Contains("list_order_by_key", StringComparison.Ordinal))
                {
                    var i = name.IndexOf("list_order_by_key", StringComparison.Ordinal);
                    body = name.Substring(i + "list_order_by_key".Length);
                }
                else body = null;
                if (body != null && body.StartsWith("_", StringComparison.Ordinal))
                    body = body.Substring(1);
                // List<double>: the entry is `d` (double key), `f` (float key) or
                // `d_<keyfam>` (int32/int64 key -- b5-gaps section 49).
                if (body == "d")
                {
                    srcFam = "d";
                    priFam = "2";
                }
                else if (body == "f")
                {
                    srcFam = "d";
                    priFam = "3";
                }
                else if (body is { Length: >= 3 } && body.StartsWith("f_", StringComparison.Ordinal))
                {
                    // List<float>: `f_<keyfam>`.
                    srcFam = "f";
                    priFam = body.Substring(2) switch
                    {
                        "i4" => "0",
                        "i8" => "1",
                        "d" => "2",
                        "f" => "3",
                        _ => "",
                    };
                    if (priFam.Length == 0) return false;
                }
                else if (body is { Length: >= 3 } && body.StartsWith("d_", StringComparison.Ordinal))
                {
                    // List<double>: `d_<keyfam>` (int32/int64 key).
                    srcFam = "d";
                    priFam = body.Substring(2) switch
                    {
                        "i4" => "0",
                        "i8" => "1",
                        "d" => "2",
                        "f" => "3",
                        _ => "",
                    };
                    if (priFam.Length == 0) return false;
                }
                else return false;
                src = call.Args[0];
                sel = call.Args[1];
                return true;
            }
            // b5-gaps section 50: a DECIMAL-ELEMENT array OrderBy primary is a
            // legal chain seed (the decimal source has the 7-family dispatch
            // and the array decimal chain kernel reads them). The entry name
            // carries the KEY family suffix:
            //   order_by_key[_desc]_dec_i4   -- decimal source, int32 key
            //   order_by_key[_desc]_dec_i8   -- decimal source, int64 key
            //   order_by_key[_desc]_dec_d    -- decimal source, double key
            //   order_by_key[_desc]_dec_f    -- decimal source, float key
            //   order_by_key[_desc]_dec_dec  -- decimal source, decimal key
            //   order_by_key[_desc]_dec_dt   -- decimal source, DateTime key
            //   order_by_key[_desc]_dec_guid -- decimal source, Guid key
            // The family code is the DECIMAL dispatch code (0=int32,1=int64,
            // 2=double,3=decimal,4=DateTime,5=Guid,6=float).
            static bool DecArrayPrimarySeed(CallExpr call, out Expr src,
                out Expr sel, out string desc, out string priFam)
            {
                src = null!;
                sel = null!;
                desc = "0";
                priFam = "";
                var name = call.Resolved?.CppName;
                if (name == null) return false;
                // b5-gaps section 50: the name-based parser below also matches
                // the LIST decimal primaries (list_order_by_key_dec_* -- they
                // contain "order_by_key"). A `list_` name belongs to
                // DecListPrimarySeed; the array chain entry has no way to take
                // a List (the §49 mis-emission lesson).
                if (name.Contains("list_", StringComparison.Ordinal)) return false;
                if (call.Args.Count != 2) return false;
                string? body;
                if (name.Contains("order_by_key_desc", StringComparison.Ordinal))
                {
                    desc = "1";
                    var i = name.IndexOf("order_by_key_desc", StringComparison.Ordinal);
                    body = name.Substring(i + "order_by_key_desc".Length);
                }
                else if (name.Contains("order_by_key", StringComparison.Ordinal))
                {
                    var i = name.IndexOf("order_by_key", StringComparison.Ordinal);
                    body = name.Substring(i + "order_by_key".Length);
                }
                else body = null;
                if (body != null && body.StartsWith("_", StringComparison.Ordinal))
                    body = body.Substring(1);
                if (body is not { Length: >= 4 } || !body.StartsWith("dec_", StringComparison.Ordinal))
                    return false;
                priFam = body.Substring(4) switch
                {
                    "i4" => "0",
                    "i8" => "1",
                    "d" => "2",
                    "dec" => "3",
                    "dt" => "4",
                    "guid" => "5",
                    "f" => "6",
                    _ => "",
                };
                if (priFam.Length == 0) return false;
                src = call.Args[0];
                sel = call.Args[1];
                return true;
            }
            // b5-gaps section 50: the DECIMAL-ELEMENT List<T> OrderBy primary (a
            // chain seed like the List<double>/List<float> primaries). The entry
            // is list_order_by_key[_desc]_dec_<keyfam> and the element kind
            // literal is AppendLiterals[^1] ("12" = IL2CPP_LIST_KIND_DEC).
            static bool DecListPrimarySeed(CallExpr call, out Expr src,
                out Expr sel, out string desc, out string priFam, out string listKind)
            {
                src = null!;
                sel = null!;
                desc = "0";
                priFam = "";
                listKind = "";
                var name = call.Resolved?.CppName;
                if (name == null) return false;
                var lit = call.Resolved!.AppendLiterals;
                if (call.Args.Count != 2 || lit is not { Length: >= 1 }) return false;
                listKind = lit[^1];
                if (listKind != "12") return false;
                string? body;
                if (name.Contains("list_order_by_key_desc", StringComparison.Ordinal))
                {
                    desc = "1";
                    var i = name.IndexOf("list_order_by_key_desc", StringComparison.Ordinal);
                    body = name.Substring(i + "list_order_by_key_desc".Length);
                }
                else if (name.Contains("list_order_by_key", StringComparison.Ordinal))
                {
                    var i = name.IndexOf("list_order_by_key", StringComparison.Ordinal);
                    body = name.Substring(i + "list_order_by_key".Length);
                }
                else body = null;
                if (body != null && body.StartsWith("_", StringComparison.Ordinal))
                    body = body.Substring(1);
                if (body is not { Length: >= 4 } || !body.StartsWith("dec_", StringComparison.Ordinal))
                    return false;
                priFam = body.Substring(4) switch
                {
                    "i4" => "0",
                    "i8" => "1",
                    "d" => "2",
                    "dec" => "3",
                    "dt" => "4",
                    "guid" => "5",
                    "f" => "6",
                    _ => "",
                };
                if (priFam.Length == 0) return false;
                src = call.Args[0];
                sel = call.Args[1];
                return true;
            }
            // b5-gaps section 51: a VALUE-STRUCT-element array OrderBy primary (single
            // runtime entry il2cpp_linq_order_by_key_vt distinguished by its
            // literal table [desc, srcKind, keyFam]). The chain seed's primary
            // key family is the keyFam literal (0..7); srcKind (13..16) is
            // carried through to the array vt chain entry. A user struct (no
            // by-value cell ABI) never reaches here -- MethodMap's
            // IsLinqValueTypeSource gate.
            static bool VtArrayPrimarySeed(CallExpr call, out Expr src,
                out Expr sel, out string desc, out string priFam, out string srcKind)
            {
                src = null!;
                sel = null!;
                desc = "0";
                priFam = "";
                srcKind = "";
                var name = call.Resolved?.CppName;
                if (name != "il2cpp::il2cpp_linq_order_by_key_vt") return false;
                if (call.Args.Count != 2) return false;
                var lit = call.Resolved!.AppendLiterals;
                if (lit is not { Length: >= 3 }) return false;
                if (lit[0] is not ("0" or "1")) return false;
                if (lit[1] is not ("13" or "14" or "15" or "16")) return false;
                priFam = lit[2];
                if (int.TryParse(priFam, out var pf) == false || pf < 0 || pf > 7)
                    return false;
                desc = lit[0];
                srcKind = lit[1];
                src = call.Args[0];
                sel = call.Args[1];
                return true;
            }
            // b5-gaps section 51: the VALUE-STRUCT-element List<T> OrderBy
            // primary (il2cpp_linq_list_order_by_key_vt with literals
            // [desc, kind, keyFam] -- the kind literal is the value-struct
            // element kind 13..16).
            static bool VtListPrimarySeed(CallExpr call, out Expr src,
                out Expr sel, out string desc, out string priFam, out string srcKind,
                out string listKind)
            {
                src = null!;
                sel = null!;
                desc = "0";
                priFam = "";
                srcKind = "";
                listKind = "";
                var name = call.Resolved?.CppName;
                if (name != "il2cpp::il2cpp_linq_list_order_by_key_vt") return false;
                if (call.Args.Count != 2) return false;
                var lit = call.Resolved!.AppendLiterals;
                if (lit is not { Length: >= 3 }) return false;
                if (lit[0] is not ("0" or "1")) return false;
                if (lit[1] is not ("13" or "14" or "15" or "16")) return false;
                priFam = lit[2];
                if (int.TryParse(priFam, out var pf) == false || pf < 0 || pf > 7)
                    return false;
                desc = lit[0];
                srcKind = lit[1];
                listKind = lit[1];
                src = call.Args[0];
                sel = call.Args[1];
                return true;
            }
            static bool ChainLevelsOf(CallExpr call, out List<string> descs,
                out List<string> fams, out List<Expr> selectors, out string? listKind)
            {
                descs = new List<string>();
                fams = new List<string>();
                selectors = new List<Expr>();
                listKind = null;
                var name = call.Resolved?.CppName;
                if (name == null) return false;
                if (name is "il2cpp::il2cpp_linq_then_by_chain_i4"
                    or "il2cpp::il2cpp_linq_then_by_chain_i8"
                    or "il2cpp::il2cpp_linq_then_by_chain_f"
                    or "il2cpp::il2cpp_linq_then_by_chain_d"
                    or "il2cpp::il2cpp_linq_then_by_chain_dec"
                    or "il2cpp::il2cpp_linq_then_by_chain_vt"
                    or "il2cpp::il2cpp_linq_then_by_chain_narrow"
                    or "il2cpp::il2cpp_linq_then_by_chain_ref"
                    or "il2cpp::il2cpp_linq_list_then_by_chain_i4"
                    or "il2cpp::il2cpp_linq_list_then_by_chain_ref"
                    or "il2cpp::il2cpp_linq_list_then_by_chain_d"
                    or "il2cpp::il2cpp_linq_list_then_by_chain_f"
                    or "il2cpp::il2cpp_linq_list_then_by_chain_dec"
                    or "il2cpp::il2cpp_linq_list_then_by_chain_vt")
                {
                    var lits = call.Resolved!.AppendLiterals;
                    if (lits is not { Length: 8 or 9 }) return false;
                    for (var l = 0; l < 4; ++l)
                    {
                        if (lits[4 + l] == "-1") break;
                        if (1 + l >= call.Args.Count) return false;
                        descs.Add(lits[l]);
                        fams.Add(lits[4 + l]);
                        selectors.Add(call.Args[1 + l]);
                    }
                    bool isList = name.StartsWith("il2cpp::il2cpp_linq_list_", StringComparison.Ordinal);
                    if (isList)
                    {
                        // The list kind is the NINTH trailing literal of a list
                        // chain call (AppendLiterals follow the arguments), NOT
                        // an argument -- the list itself occupies Args[0].
                        if (fams.Count < 2 || lits.Length != 9) return false;
                        listKind = lits[8];
                    }
                    return fams.Count >= 2;
                }
                var lits2 = call.Resolved!.AppendLiterals;
                if (name is "il2cpp::il2cpp_linq_then_by_key_val_i4"
                    or "il2cpp::il2cpp_linq_then_by_key_val_i8")
                {
                    if (lits2 is not { Length: 4 } || call.Args.Count != 3) return false;
                    descs.Add(lits2[0]);
                    descs.Add(lits2[1]);
                    fams.Add(lits2[2]);
                    fams.Add(lits2[3]);
                    selectors.Add(call.Args[1]);
                    selectors.Add(call.Args[2]);
                    return true;
                }
                // impl-collation / impl-listchain: a REFERENCE-element source's
                // two-key composite. The ARRAY entry takes (src, k1, k2) plus
                // (k1desc, k2desc, k1fam, k2fam); the LIST entry additionally
                // carries the element kind as its LAST argument.
                if (name is "il2cpp::il2cpp_linq_then_by_key_ref"
                    or "il2cpp::il2cpp_linq_list_then_by_key_ref")
                {
                    if (lits2 is not { Length: 4 } || call.Args.Count < 3) return false;
                    descs.Add(lits2[0]);
                    descs.Add(lits2[1]);
                    fams.Add(lits2[2]);
                    fams.Add(lits2[3]);
                    selectors.Add(call.Args[1]);
                    selectors.Add(call.Args[2]);
                    // A List<T> two-key composite is INHERENTLY the
                    // reference-element entry (the list is snapped at the
                    // reference kind); unlike the chain entries it carries NO
                    // element-kind argument, so the kind must be derived from
                    // the entry itself rather than read out of Args[^1].
                    if (name == "il2cpp::il2cpp_linq_list_then_by_key_ref")
                        listKind = "1";
                    return true;
                }
                return false;
            }
            // The SOURCE family of a chainable composite. "ref" means a
            // reference-element source (Il2CppObject* slots); the numeric chain
            // entry is chosen for i4 / i8. A LIST source keeps its own entries
            // (the chain must snap the list once).
            static string? ChainSourceSuffix(CallExpr call)
            {
                var name = call.Resolved?.CppName;
                if (name == null) return null;
if (name.EndsWith("list_then_by_chain_ref", StringComparison.Ordinal)) return "list_ref";
                if (name.EndsWith("list_then_by_chain_i4", StringComparison.Ordinal)) return "list_i4";
                if (name.EndsWith("list_then_by_chain_d", StringComparison.Ordinal)) return "list_d";
                if (name.EndsWith("list_then_by_chain_f", StringComparison.Ordinal)) return "list_f";
                if (name.EndsWith("list_then_by_chain_dec", StringComparison.Ordinal)) return "list_dec";
                if (name.EndsWith("list_then_by_chain_vt", StringComparison.Ordinal)) return "list_vt";
                if (name.EndsWith("list_then_by_key_i4", StringComparison.Ordinal)) return "list_i4";
                if (name.EndsWith("list_then_by_key_ref", StringComparison.Ordinal)) return "list_ref";
                if (name.EndsWith("then_by_chain_ref", StringComparison.Ordinal)) return "ref";
                if (name.EndsWith("then_by_chain_i8", StringComparison.Ordinal)) return "i8";
                if (name.EndsWith("then_by_chain_f", StringComparison.Ordinal)) return "f";
                if (name.EndsWith("then_by_chain_d", StringComparison.Ordinal)) return "d";
                if (name.EndsWith("then_by_chain_dec", StringComparison.Ordinal)) return "dec";
                if (name.EndsWith("then_by_chain_vt", StringComparison.Ordinal)) return "vt";
                if (name.EndsWith("then_by_chain_narrow", StringComparison.Ordinal)) return "narrow";
                if (name.EndsWith("then_by_chain_i4", StringComparison.Ordinal)) return "i4";
                if (name.EndsWith("then_by_key_val_i8", StringComparison.Ordinal)) return "i8";
                if (name.EndsWith("then_by_key_val_i4", StringComparison.Ordinal)) return "i4";
                if (name.EndsWith("then_by_key_ref", StringComparison.Ordinal)) return "ref";
                return null;
            }
            // b5-gaps §44: CHAINED ThenBy, depth >= 3 --
            // OrderBy(k1).ThenBy(k2).ThenBy(k3)[.ThenBy(k4)]. net48 builds an
            // EnumerableSorter CHAIN: the key phase runs LEVEL BY LEVEL in
            // chain order (measured "1"*n "2"*n "3"*n, probe in
            // .opencode/tmp/thenby3) and the comparison walks the levels in
            // tuple order. A third ThenBy is therefore not unimplementable --
            // it is the same tuple with one more column. This fold EXTENDS the
            // already-fused two-key composite (or an earlier chain) into ONE
            // il2cpp_linq_then_by_chain_i4/_i8 call so every selector is still
            // invoked exactly once.
            if (resolved != null &&
                (resolved.CppName == ResolvedMethod.ThenByCombineMarker ||
                 resolved.CppName == ResolvedMethod.ThenByDescCombineMarker)
                && args.Count == 2 && args[0] is LocExpr chainRecv
                && chainRecv.Name.Length != 0)
            {
                // Follow pure temp-to-temp copies back to the producing
                // assignment (the csc shape for a chained extension call).
                int chainIndex = -1;
                var chase = chainRecv.Name;
                // impl-listchain: a List<int> OrderBy seed (it has no two-key
                // composite with a direction column, so a plain 2-level ThenBy
                // over List<int> also rides the chain).
                // b5-gaps section 47: a double[]/float[] OrderBy primary is a
                // similar seed; the chain kernel
                // (then_by_chain_d/_f) carries the element family.
                Expr? seedSrc = null, seedSel = null;
                string seedDesc = "0", seedKind = "";
                string? wideSrcFam = null;
                string widePriFam = "0";
                bool listWideSeed = false;
                // b5-gaps section 50: a DECIMAL-element seed (decimal[] or
                // List<decimal> source). The entry name carries the decimal key
                // family and the suffix becomes "dec" (array) / "list_dec".
                string? decSrcFam = null;
                // b5-gaps section 51: a VALUE-STRUCT-element seed (a
                // DateTime/TimeSpan/Guid/DateTimeOffset array or List source).
                // The single vt OrderBy entry is distinguished by its literal
                // table; the suffix becomes "vt" (array) / "list_vt" and the
                // source element kind literal carries through to the chain.
                string vtSrcKind = "";
                // b5-gaps section 73: a NARROW-element array seed (sbyte[]/
                // byte[]/bool[]/short[]/ushort[]/char[]). The single narrow
                // OrderBy entry keeps its source cell in AppendLiterals[1];
                // the suffix becomes "narrow" and the source cell literal
                // carries through to the chain (the chain re-materializes the
                // source's own narrow cell).
                string narrowSrcCell = "";
                for (var si = stmts.Count - 1; si >= 0; --si)
                {
                    if (stmts[si] is not AssignStmt cand || cand.Target != chase) continue;
                    if (cand.Value is CallExpr candSeed
                        && ListPrimarySeed(candSeed, out var ps, out var pk, out var pd, out var pkind))
                    {
                        chainIndex = si;
                        seedSrc = ps;
                        seedSel = pk;
                        seedDesc = pd;
                        seedKind = pkind;
                        break;
                    }
                    if (cand.Value is CallExpr candWide
                        && WideArrayPrimarySeed(candWide, out var ws, out var wk,
                            out var wd, out var wp, out var wf))
                    {
                        chainIndex = si;
                        seedSrc = ws;
                        seedSel = wk;
                        seedDesc = wd;
                        wideSrcFam = wf;
                        widePriFam = wp;
                        break;
                    }
                    if (cand.Value is CallExpr candNarrow
                        && NarrowArrayPrimarySeed(candNarrow, out var ns, out var nk,
                            out var nd, out var np, out var nsc))
                    {
                        chainIndex = si;
                        seedSrc = ns;
                        seedSel = nk;
                        seedDesc = nd;
                        widePriFam = np;
                        decSrcFam = "narrow";
                        narrowSrcCell = nsc;
                        break;
                    }
                    if (cand.Value is CallExpr candWideList
                        && WideListPrimarySeed(candWideList, out var wl, out var wlk,
                            out var wld, out var wlp, out var wlf, out var wlkind))
                    {
                        chainIndex = si;
                        seedSrc = wl;
                        seedSel = wlk;
                        seedDesc = wld;
                        wideSrcFam = wlf;
                        widePriFam = wlp;
                        seedKind = wlkind;
                        listWideSeed = true;
                        break;
                    }
                    if (cand.Value is CallExpr candDec
                        && DecArrayPrimarySeed(candDec, out var ds, out var dk, out var dd, out var dp))
                    {
                        chainIndex = si;
                        seedSrc = ds;
                        seedSel = dk;
                        seedDesc = dd;
                        decSrcFam = "dec";
                        widePriFam = dp;
                        break;
                    }
                    if (cand.Value is CallExpr candDecList
                        && DecListPrimarySeed(candDecList, out var dls, out var dlk,
                            out var dld, out var dlp, out var dlkind))
                    {
                        chainIndex = si;
                        seedSrc = dls;
                        seedSel = dlk;
                        seedDesc = dld;
                        decSrcFam = "list_dec";
                        widePriFam = dlp;
                        seedKind = dlkind;
                        listWideSeed = true;
                        break;
                    }
                    if (cand.Value is CallExpr candVt
                        && VtArrayPrimarySeed(candVt, out var vs, out var vk, out var vd, out var vp, out var vsk))
                    {
                        chainIndex = si;
                        seedSrc = vs;
                        seedSel = vk;
                        seedDesc = vd;
                        decSrcFam = "vt";
                        widePriFam = vp;
                        vtSrcKind = vsk;
                        break;
                    }
                    if (cand.Value is CallExpr candVtList
                        && VtListPrimarySeed(candVtList, out var vls, out var vlk,
                            out var vld, out var vlp, out var vlsk, out var vlkind))
                    {
                        chainIndex = si;
                        seedSrc = vls;
                        seedSel = vlk;
                        seedDesc = vld;
                        decSrcFam = "list_vt";
                        widePriFam = vlp;
                        vtSrcKind = vlsk;
                        seedKind = vlkind;
                        listWideSeed = true;
                        break;
                    }
                    if (cand.Value is CallExpr candChain && ChainLevelsOf(candChain, out _, out _, out _, out _)
                        && ChainSourceSuffix(candChain) != null)
                    {
                        chainIndex = si;
                        break;
                    }
                    if (cand.Value is LocExpr copyFrom && IsTempName(copyFrom.Name))
                    {
                        chase = copyFrom.Name;
                        continue;
                    }
                    break;
                }
                if (chainIndex >= 0)
                {
                    var chainAssign = (AssignStmt)stmts[chainIndex];
                    var prevChain = (CallExpr)chainAssign.Value;
                    // A List<int> OrderBy seed becomes a two-level chain here;
                    // a double[]/float[] OrderBy seed likewise (the wide seed
                    // carries the source family and the primary key family);
                    // an existing chain / two-key composite is extended.
                    bool seedShape = seedSrc != null;
                    var suffix = seedShape
                        ? decSrcFam != null ? decSrcFam
                        : listWideSeed ? "list_" + wideSrcFam
                        : wideSrcFam != null ? wideSrcFam
                        : "list_i4"
                        : ChainSourceSuffix(prevChain)!;
                    // b5-gaps section 51: extending an existing VALUE-STRUCT
                    // chain (a 4th level over a vt source): the 9th trailing
                    // literal of the prior chain call is the source element
                    // kind (13..16), which the rebuilt chain must carry again.
                    if (!seedShape && (suffix == "vt" || suffix == "list_vt"))
                    {
                        var priorLits = prevChain.Resolved?.AppendLiterals;
                        if (priorLits is { Length: >= 9 } && priorLits[8] is "13" or "14" or "15" or "16")
                        {
                            vtSrcKind = priorLits[8];
                        }
                    }
                    // b5-gaps section 73: extending an existing NARROW chain (a
                    // 4th level over an sbyte/byte/bool/short/ushort/char
                    // source): the 9th trailing literal of the prior chain call
                    // is the SOURCE cell (0..6), which the rebuilt chain must
                    // carry again.
                    if (!seedShape && (suffix == "narrow"))
                    {
                        var priorLits = prevChain.Resolved?.AppendLiterals;
                        if (priorLits is { Length: >= 9 } && priorLits[8].Length != 0)
                        {
                            narrowSrcCell = priorLits[8];
                        }
                    }
                    var priorDescs = new List<string>();
                    var priorFams = new List<string>();
                    var priorSels = new List<Expr>();
                    string? priorListKind = seedShape ? seedKind : null;
                    Expr chainSource = seedShape ? seedSrc! : prevChain.Args[0];
                    if (seedShape)
                    {
                        priorDescs.Add(seedDesc);
                        priorSels.Add(seedSel!);
                        if (wideSrcFam != null || decSrcFam != null)
                        {
                            priorFams.Add(widePriFam);
                        }
                        else
                        {
                            // The List<int> OrderBy entries are int32-element +
                            // int32-key only, so the seed's primary family is 0.
                            priorFams.Add("0");
                        }
                    }
                    else
                    {
                        ChainLevelsOf(prevChain, out priorDescs, out priorFams,
                                      out priorSels, out priorListKind);
                    }
                    if (priorFams.Count >= 4)
                    {
                        throw new NotSupportedException(
                            "Enumerable.ThenBy: a ThenBy chain deeper than four levels " +
                            "(OrderBy + three ThenBy) is not wired; the fused chain kernel takes " +
                            "four keys (b5-gaps §44)");
                    }
                    var newFamily = resolved.AppendLiterals is { Length: >= 1 } chainLit
                        ? chainLit[0] : "0";
                    // A numeric chain (int32[]/int64[] or List<int>) can only
                    // carry the numeric families; a REFERENCE chain (array or
                    // List) additionally carries 4 (System.String -> culture
                    // collation) and 5 (any other reference). Anything else
                    // would need a reader the kernel does not have, so it stays
                    // LOUD.
                    // impl-listchain: a LIST source keeps its own chain entries,
                    // which snap the ORIGINAL list once.
                    bool isListChain = suffix is "list_ref" or "list_i4" or "list_d" or "list_f" or "list_dec" or "list_vt";
                    bool refChain = suffix is "ref" or "list_ref";
                    // b5-gaps section 50: a DECIMAL chain carries the decimal
                    // dispatch key families 0..6 (int32/int64/double/decimal/
                    // DateTime/Guid/float) -- a wider set than the numeric
                    // chain's 0..3 (decimal/DateTime/Guid need the 16/8/16-byte
                    // decimal cells, so only the decimal kernel reads them).
                    bool decChain = suffix is "dec" or "list_dec";
                    // b5-gaps section 51: a VALUE-STRUCT chain carries the vt
                    // dispatch key families 0..7 (int32/int64/double/float/
                    // DateTime/Guid/TimeSpan/DateTimeOffset -- LinqVtKeyFamilyCode).
                    bool vtChain = suffix is "vt" or "list_vt";
                    bool familyOk = refChain
                        ? newFamily is "0" or "1" or "2" or "3" or "4" or "5"
                        : decChain
                        ? newFamily is "0" or "1" or "2" or "3" or "4" or "5" or "6"
                        : vtChain
                        ? newFamily is "0" or "1" or "2" or "3" or "4" or "5" or "6" or "7"
                        : newFamily is "0" or "1" or "2" or "3";
                    if (!familyOk)
                    {
                        throw new NotSupportedException(
                            $"Enumerable.ThenBy receiver '{chainRecv.Name}' marker carries key family " +
                            $"'{newFamily}', which the {(refChain ? "reference" : decChain ? "decimal" : vtChain ? "value-struct" : "numeric")} chained " +
                            "composite cannot read (b5-gaps §44)");
                    }
                    var newDesc = resolved.CppName == ResolvedMethod.ThenByDescCombineMarker ? "1" : "0";
                    priorDescs.Add(newDesc);
                    priorFams.Add(newFamily);
                    priorSels.Add(args[1]);
                    // Pad every level list to the kernel's fixed four slots: an
                    // unused level is direction 0, family -1 and a null
                    // selector (the runtime terminates the chain at -1).
                    while (priorFams.Count < 4)
                    {
                        priorDescs.Add("0");
                        priorFams.Add("-1");
                        priorSels.Add(new NullExpr());
                    }
                    var chainCall = new CallExpr
                    {
                        Function = $"call_{ins.Operand}",
                        ReturnsRef = true,
                        ResultKind = null,
                        IsPInvoke = false,
                        SetLastError = false,
                        Resolved = new ResolvedMethod(
                            isListChain
                                ? suffix == "list_ref"
                                    ? "il2cpp::il2cpp_linq_list_then_by_chain_ref"
                                    : suffix == "list_d"
                                    ? "il2cpp::il2cpp_linq_list_then_by_chain_d"
                                    : suffix == "list_f"
                                    ? "il2cpp::il2cpp_linq_list_then_by_chain_f"
                                    : suffix == "list_dec"
                                    ? "il2cpp::il2cpp_linq_list_then_by_chain_dec"
                                    : suffix == "list_vt"
                                    ? "il2cpp::il2cpp_linq_list_then_by_chain_vt"
                                    : "il2cpp::il2cpp_linq_list_then_by_chain_i4"
                                : suffix == "ref"
                                ? "il2cpp::il2cpp_linq_then_by_chain_ref"
                                : suffix == "i8"
                                ? "il2cpp::il2cpp_linq_then_by_chain_i8"
                                : suffix == "f"
                                ? "il2cpp::il2cpp_linq_then_by_chain_f"
                                : suffix == "d"
                                ? "il2cpp::il2cpp_linq_then_by_chain_d"
                                : suffix == "dec"
                                ? "il2cpp::il2cpp_linq_then_by_chain_dec"
                                : suffix == "vt"
                                ? "il2cpp::il2cpp_linq_then_by_chain_vt"
                                : suffix == "narrow"
                                ? "il2cpp::il2cpp_linq_then_by_chain_narrow"
                                : "il2cpp::il2cpp_linq_then_by_chain_i4",
                            // A list chain takes the element kind as the NINTH
                            // trailing literal (AppendLiterals always follow the
                            // declared arguments), so the parameter count stays
                            // five and the FIRST parameter is the List object
                            // itself (Il2CppObject*), not an Il2CppArray*.
                            5, false, true, true,
                            new[] { false, false, false, false, false },
                            false,
                            ParamTypeNames: isListChain
                                ? new[] { "", "", "", "", "" }
                                : new[] { "a", "", "", "", "" },
                            // The eight trailing literals are the four per-level
                            // directions then the four per-level key families;
                            // an unused level is family -1 with a null selector.
                            AppendLiterals: isListChain
                                ? new[]
                                {
                                    priorDescs[0], priorDescs[1], priorDescs[2], priorDescs[3],
                                    priorFams[0], priorFams[1], priorFams[2], priorFams[3],
                                    priorListKind ?? throw new NotSupportedException(
                                        "Enumerable.ThenBy over a List source carries no element kind"),
                                }
                                : suffix == "vt"
                                ? new[]
                                {
                                    priorDescs[0], priorDescs[1], priorDescs[2], priorDescs[3],
                                    priorFams[0], priorFams[1], priorFams[2], priorFams[3],
                                    vtSrcKind.Length != 0 ? vtSrcKind : throw new NotSupportedException(
                                        "Enumerable.ThenBy over a value-struct array source carries no element kind"),
                                }
                                : suffix == "narrow"
                                ? new[]
                                {
                                    priorDescs[0], priorDescs[1], priorDescs[2], priorDescs[3],
                                    priorFams[0], priorFams[1], priorFams[2], priorFams[3],
                                    narrowSrcCell.Length != 0 ? narrowSrcCell : throw new NotSupportedException(
                                        "Enumerable.ThenBy over a narrow-element array source carries no source cell"),
                                }
                                : new[]
                                {
                                    priorDescs[0], priorDescs[1], priorDescs[2], priorDescs[3],
                                    priorFams[0], priorFams[1], priorFams[2], priorFams[3],
                                },
                            ReturnTypeName: suffix == "list_ref" || suffix == "ref"
                                || suffix == "list_vt" || suffix == "vt"
                                || suffix == "narrow"
                                ? prevChain.Resolved!.ReturnTypeName
                                : suffix == "list_dec" || suffix == "dec" ? "System.Decimal[]"
                                : suffix == "list_d" ? "System.Double[]"
                                : suffix == "list_f" ? "System.Single[]"
                                : suffix == "i8" ? "System.Int64[]"
                                : suffix == "f" ? "System.Single[]"
                                : suffix == "d" ? "System.Double[]"
                                : "System.Int32[]"),
                    };
                    chainCall.Args.Add(chainSource);
                    for (var l = 0; l < 4; ++l)
                    {
                        chainCall.Args.Add(l < priorSels.Count ? priorSels[l] : new NullExpr());
                    }
                    stmts.RemoveAt(chainIndex);
                    stmts.Add(new AssignStmt
                    {
                        Target = chainAssign.Target,
                        Value = chainCall,
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode),
                    });
                    if (chainRecv.Name != chainAssign.Target)
                    {
                        stmts.Add(new AssignStmt
                        {
                            Target = chainRecv.Name,
                            Value = new LocExpr { Name = chainAssign.Target },
                            SourceOffset = ins.Offset,
                            SourceOpName = OpName(ins.OpCode),
                        });
                    }
                    Push(chainRecv, ins);
                    return;
                }
            }
            // D-LO2-1 (linqorder2): Enumerable.ThenBy / ThenByDescending --
            // the CHAINED-SORTER marker (ResolvedMethod.ThenByCombineMarker /
            // ThenByDescCombineMarker). OrderBy(k1).ThenBy(k2) is a FUSED
            // composite sort (net48 EnumerableSorter chain); the eager array
            // runtime has no post-hoc primary-key recovery. Push materializes
            // the OrderBy result into a temp, so the receiver here is a LocExpr
            // naming the statement `t = il2cpp_linq_order_by_key_i4(src, k1)`
            // already appended to `stmts`. The fold REWRITES that assignment
            // into the single composite
            // `t = il2cpp_linq_then_by_key[_desc]_i4(src, k1, k2)` -- appended
            // at the CURRENT end (after the secondary-key delegate statement,
            // so no use-before-definition) -- and the ThenBy call folds to the
            // receiver temp. The original order_by statement is removed (a
            // leftover would re-invoke the PRIMARY selector -- breaking the
            // net48 exactly-once contract). Minimal surface: the receiver MUST
            // name an OrderBy(keySelector) assignment; anything else is LOUD.
            if (resolved != null &&
                (resolved.CppName == ResolvedMethod.ThenByCombineMarker ||
                 resolved.CppName == ResolvedMethod.ThenByDescCombineMarker))
            {
                if (args.Count != 2 && args.Count != 3)
                    throw new NotSupportedException(
                        $"Enumerable.ThenBy needs exactly 2 args (receiver + keySelector) or 3 (with a user comparer), got {args.Count}");
                if (args[0] is not LocExpr recvLoc || recvLoc.Name.Length == 0)
                    throw new NotSupportedException(
                        "Enumerable.ThenBy is supported directly on a single OrderBy(keySelector) result only " +
                        "(OrderBy(k1).ThenBy(k2) -- D-LO2-1 stage-1 surface); deeper ThenBy chains and non-OrderBy " +
                        "receivers have no fused-composite runtime entry");
                // The OrderBy call may have been stored into a temp and then
                // COPIED through one or more branch-join spill temps before the
                // ThenBy call reads it (`t4 = order_by(..); t5 = t4;
                // then_by(t5, k2)` -- the csc shape for a chained extension
                // call over a conditional). Follow pure temp-to-temp copies
                // back to the OrderBy assignment; the composite REPLACES that
                // assignment (so the primary selector is still invoked exactly
                // once) and the receiver temp is re-read from the composite.
                int obIndex = -1;
                var chaseName = recvLoc.Name;
                for (int si = stmts.Count - 1; si >= 0; --si)
                {
                    if (stmts[si] is not AssignStmt cand || cand.Target != chaseName) continue;
                    if (cand.Value is CallExpr candCall
                        && candCall.Args.Count == 2
                        && ThenByPrimaryEntrySuffix(candCall.Resolved?.CppName) is not null)
                    {
                        obIndex = si;
                        break;
                    }
                    if (cand.Value is LocExpr copyFrom && IsTempName(copyFrom.Name))
                    {
                        chaseName = copyFrom.Name;
                        continue;
                    }
                    break;
                }
                if (obIndex < 0)
                    throw new NotSupportedException(
                        $"Enumerable.ThenBy receiver '{recvLoc.Name}' does not name an OrderBy(keySelector) assignment " +
                        "(OrderBy(k1).ThenBy(k2) -- D-LO2-1 stage-1 surface); deeper ThenBy chains and non-OrderBy " +
                        "receivers have no fused-composite runtime entry");
                var obAssign = (AssignStmt)stmts[obIndex];
                var obValue = (CallExpr)obAssign.Value;
                var thenDesc = resolved.CppName == ResolvedMethod.ThenByDescCombineMarker;
                // p0linq4 (D4): the composite entry follows the PRIMARY OrderBy
                // entry's element/key family. The receiver here is the OrderBy
                // result tracked by the arm above (System.Int32[] or
                // System.Int64[]), so the family suffix is read off the
                // resolved primary entry: "_i4" -> _i4, "_i8_i4" -> _i8_i4,
                // "_i8_i8" -> _i8_i8.
                var familySuffix = ThenByPrimaryEntrySuffix(obValue.Resolved!.CppName)!;
                // The primary OrderBy direction is part of the fused sort: a
                // descending primary must keep its descending order (both the
                // decimal and the int32/int64 families carry the flag).
                bool primaryDesc = obValue.Resolved!.CppName!.Contains(
                    "order_by_key_desc_", StringComparison.Ordinal);
                string compositeEntry;
                string[] compositeParamNames;
                string compositeReturnType;
                string[]? compositeLiterals = null;
                if (familySuffix.StartsWith("src_dec_", StringComparison.Ordinal)
                    || familySuffix.StartsWith("grp_dec_", StringComparison.Ordinal))
                {
                    // impl-linqleft: DECIMAL element composite. The primary
                    // key family is encoded in the suffix; the secondary key
                    // family rides the ThenBy marker's AppendLiterals. Both
                    // arrive as trailing literals on the single runtime
                    // dispatch entry.
                    bool grouping = familySuffix.StartsWith("grp_dec_", StringComparison.Ordinal);
                    string primaryFam = familySuffix.Substring(
                        (grouping ? "grp_dec_" : "src_dec_").Length);
                    string primaryCode = primaryFam switch
                    {
                        "i4" => "0",
                        "i8" => "1",
                        "d" => "2",
                        "dec" => "3",
                        "dt" => "4",
                        "guid" => "5",
                        // impl-orderfam: the float family has its own reader
                        // column on the runtime dec dispatch (b5-gaps 搂14).
                        "f" => "6",
                        _ => "3",
                    };
                    string secondaryCode = resolved.AppendLiterals is { Length: >= 1 }
                        ? resolved.AppendLiterals[0] : "3";
                    compositeEntry = grouping
                        ? "il2cpp::il2cpp_linq_grouping_then_by_key_dec"
                        : "il2cpp::il2cpp_linq_then_by_key_dec";
                    compositeParamNames = grouping
                        ? new[] { "", "", "" }
                        : new[] { "a", "", "" };
                    compositeReturnType = "System.Decimal[]";
                    // The primary OrderBy direction is part of the fused sort: a
                    // descending primary must keep its descending order.
                    compositeLiterals = new[]
                    {
                        primaryDesc ? "1" : "0", thenDesc ? "1" : "0",
                        primaryCode, secondaryCode,
                    };
                }
                else if (familySuffix is "src_vt" or "list_vt")
                {
                    // impl-vtsel: the value-struct element-source composite. The
                    // primary OrderBy entry carries (desc, srcKind, keyFam) as
                    // appended literals and the ThenBy marker carries the
                    // SECONDARY key family; the fused entry takes all of them.
                    string vtSrcKind = obValue.Resolved!.AppendLiterals is { Length: >= 2 }
                        ? obValue.Resolved.AppendLiterals[1] : "13";
                    string vtK1Fam = obValue.Resolved.AppendLiterals is { Length: >= 3 }
                        ? obValue.Resolved.AppendLiterals[2] : "0";
                    string? vtK2Fam = resolved.AppendLiterals is { Length: >= 1 }
                        ? resolved.AppendLiterals[0] : null;
                    if (vtK2Fam is not ("0" or "1" or "2" or "3" or "4" or "5" or "6" or "7"))
                    {
                        throw new NotSupportedException(
                            $"Enumerable.ThenBy receiver '{recvLoc.Name}' marker carries no value-struct " +
                            "secondary key family literal (impl-vtsel); the fused composite must know " +
                            "both key families to pick the right key readers");
                    }
                    bool listRecv = familySuffix == "list_vt";
                    compositeEntry = listRecv
                        ? "il2cpp::il2cpp_linq_list_then_by_key_vt"
                        : "il2cpp::il2cpp_linq_then_by_key_vt";
                    compositeParamNames = listRecv
                        ? new[] { "", "", "" }
                        : new[] { "a", "", "" };
                    compositeReturnType = obValue.Resolved.ReturnTypeName ?? "System.DateTime[]";
                    compositeLiterals = new[]
                    {
                        primaryDesc ? "1" : "0", thenDesc ? "1" : "0",
                        vtSrcKind, vtK1Fam, vtK2Fam,
                    };
                }
                else if (familySuffix.StartsWith("_ref_arr_", StringComparison.Ordinal)
                    || familySuffix.StartsWith("_ref_list_", StringComparison.Ordinal))
                {
                    // impl-collation: the REFERENCE-element composite
                    // (string[]/object[]/List<string> sources). The primary key
                    // family is encoded in the suffix; the secondary family
                    // rides the ThenBy marker's AppendLiterals (4 = System.String
                    // -- culture collation, 5 = another reference type).
                    bool refList = familySuffix.StartsWith("_ref_list_", StringComparison.Ordinal);
                    string refPrimaryFam = familySuffix.Substring(
                        (refList ? "_ref_list_" : "_ref_arr_").Length);
                    string refPrimaryCode = refPrimaryFam switch
                    {
                        "i4" => "0",
                        "i8" => "1",
                        "d" => "2",
                        "f" => "3",
                        "string" => "4",
                        "ref" => "5",
                        _ => throw new NotSupportedException(
                            $"Enumerable.ThenBy over primary entry suffix '{familySuffix}' has no " +
                            "reference fused-composite mapping (impl-collation)"),
                    };
                    string? refSecondaryCode = resolved.AppendLiterals is { Length: >= 1 }
                        ? resolved.AppendLiterals[0] : null;
                    if (refSecondaryCode is not ("0" or "1" or "2" or "3" or "4" or "5"))
                    {
                        throw new NotSupportedException(
                            $"Enumerable.ThenBy receiver '{recvLoc.Name}' marker carries no reference " +
                            "secondary key family literal (impl-collation); the fused composite must " +
                            "know both key families to pick the right key readers");
                    }
                    // BOTH reference primaries keep the direction in
                    // AppendLiterals[0] (0 = ascending, 1 = descending) instead
                    // of in the entry name, so the fused sort must read it from
                    // there -- the list entry then carries the element kind in
                    // AppendLiterals[1].
                    bool refPrimaryDesc = obValue.Resolved!.AppendLiterals is { Length: >= 1 } refLits
                        && refLits[0] == "1";
                    compositeEntry = refList
                        ? "il2cpp::il2cpp_linq_list_then_by_key_ref"
                        : "il2cpp::il2cpp_linq_then_by_key_ref";
                    compositeParamNames = refList
                        ? new[] { "", "", "" }
                        : new[] { "a", "", "" };
                    compositeReturnType = obValue.Resolved!.ReturnTypeName ?? "System.Object[]";
                    compositeLiterals = new[]
                    {
                        refPrimaryDesc ? "1" : "0", thenDesc ? "1" : "0",
                        refPrimaryCode, refSecondaryCode,
                    };
                }
                else
                {
                    // impl-thenbyfam: the numeric composite dispatches on BOTH
                    // key families. The primary family is encoded in the
                    // primary OrderBy entry's suffix; the SECONDARY family
                    // rides the ThenBy marker's AppendLiterals (0=int32,
                    // 1=int64, 2=double, 3=float -- LinqValKeyFamilyCode).
                    // Both
                    // directions ride trailing literals. The old per-family
                    // entries (then_by_key_i4 / _i4_i8 / ...) keyed the
                    // SECONDARY reader off the primary family: a mixed pair
                    // (OrderBy(int).ThenBy(double) / OrderBy(double).ThenBy
                    // (int)) read the secondary key from the WRONG callback
                    // return slot -- a silent mis-order on x64.
                    string? secondaryCode = resolved.AppendLiterals is { Length: >= 1 }
                        ? resolved.AppendLiterals[0] : null;
                    if (secondaryCode is not ("0" or "1" or "2" or "3"))
                    {
                        throw new NotSupportedException(
                            $"Enumerable.ThenBy receiver '{recvLoc.Name}' marker carries no numeric " +
                            "secondary key family literal (impl-thenbyfam); the fused composite " +
                            "must know both key families to pick the right key readers");
                    }
                    string primaryCode = familySuffix switch
                    {
                        "_i4" => "0",
                        "_i4_i8" => "1",
                        "_i4_d" => "2",
                        "_i4_f" => "3",
                        "_i8_i4" => "0",
                        "_i8_i8" => "1",
                        "_i8_d" => "2",
                        "_i8_f" => "3",
                        _ => throw new NotSupportedException(
                            $"Enumerable.ThenBy over primary entry suffix '{familySuffix}' has no " +
                            "numeric fused-composite mapping (impl-thenbyfam)"),
                    };
                    compositeEntry = familySuffix.StartsWith("_i4", StringComparison.Ordinal)
                        ? "il2cpp::il2cpp_linq_then_by_key_val_i4"
                        : "il2cpp::il2cpp_linq_then_by_key_val_i8";
                    compositeParamNames = new[] { "a", "", "" };
                    compositeReturnType = familySuffix.StartsWith("_i4", StringComparison.Ordinal)
                        ? "System.Int32[]" : "System.Int64[]";
                    // b5-gaps section 74: the THEN-BY marker with a user
                    // comparer carries a second literal "1"; the fused
                    // composite becomes the comparer twin
                    // (il2cpp_linq_then_by_key_val_i4_cmp / _i8_cmp), the
                    // SECONDARY key is compared by the user IComparer<TKey2>
                    // (the primary keeps the default), and the comparer
                    // operand (args[2]) is appended after the two selectors.
                    bool cmpThenBy = resolved.AppendLiterals is { Length: >= 2 }
                        && resolved.AppendLiterals[1] == "1";
                    if (cmpThenBy)
                    {
                        compositeEntry = familySuffix.StartsWith("_i4", StringComparison.Ordinal)
                            ? "il2cpp::il2cpp_linq_then_by_key_val_i4_cmp"
                            : "il2cpp::il2cpp_linq_then_by_key_val_i8_cmp";
                        compositeParamNames = new[] { "a", "", "", "" };
                    }
                    compositeLiterals = new[]
                    {
                        primaryDesc ? "1" : "0", thenDesc ? "1" : "0",
                        primaryCode, secondaryCode,
                    };
                }
                var composite = new CallExpr
                {
                    Function = $"call_{ins.Operand}",
                    ReturnsRef = true,
                    ResultKind = null,
                    Resolved = new ResolvedMethod(
                        compositeEntry,
                        compositeParamNames.Length, false, true, true,
                        Enumerable.Repeat(false, compositeParamNames.Length).ToArray(), false,
                        ParamTypeNames: compositeParamNames,
                        ReturnTypeName: compositeReturnType,
                        AppendLiterals: compositeLiterals),
                    IsPInvoke = false,
                    SetLastError = false,
                };
                composite.Args.Add(obValue.Args[0]); // the ORIGINAL source array
                composite.Args.Add(obValue.Args[1]); // the PRIMARY key selector
                composite.Args.Add(args[1]);         // the SECONDARY key selector
                // b5-gaps section 74: the comparer twin appends the USER
                // comparer operand (the ThenBy marker's third argument).
                if (args.Count == 3)
                {
                    composite.Args.Add(args[2]);
                }
                // Replace the order_by assignment (remove from its old spot --
                // before the secondary delegate -- and append at the current
                // end so every referenced delegate is already defined). The
                // composite stores into the ORIGINAL OrderBy assignment's
                // target; the ThenBy receiver temp (which may be one or more
                // temp-to-temp copies further down) is re-read from it so the
                // value flowing on is the composite result.
                stmts.RemoveAt(obIndex);
                stmts.Add(new AssignStmt
                {
                    Target = obAssign.Target,
                    Value = composite,
                    SourceOffset = ins.Offset,
                    SourceOpName = OpName(ins.OpCode),
                });
                if (recvLoc.Name != obAssign.Target)
                {
                    stmts.Add(new AssignStmt
                    {
                        Target = recvLoc.Name,
                        Value = new LocExpr { Name = obAssign.Target },
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode),
                    });
                }
                Push(recvLoc, ins);
                return;
            }

            call.Resolved = resolved;
            // The re-resolve below (receiver-typed M5 wiring) can replace the
            // resolved method AFTER `call` was constructed -- ReturnsRef must
            // follow the FINAL resolution or the result temp is typed int32_t
            // for an object-returning icall (cryptocase hmac ComputeHash).
            // ReturnsRef is init-only: rebuild the call with the corrected
            // flag when the final resolution disagrees with the initial one.
            if (call.ReturnsRef != (resolved?.ReturnsRef ?? false))
            {
                var fixedCall = new CallExpr
                {
                    Function = call.Function,
                    ReturnsRef = resolved?.ReturnsRef ?? false,
                    ResultKind = call.ResultKind,
                    Resolved = call.Resolved,
                    IsPInvoke = call.IsPInvoke,
                    SetLastError = call.SetLastError,
                };
                fixedCall.StringArgIndices.AddRange(call.StringArgIndices);
                fixedCall.Args.AddRange(call.Args);
                call = fixedCall;
            }
            // P0 (recursive_arith e2e): the resolver's USER-method path never
            // records ResultKind, so a call to a user method returning
            // int64/double declared its result temp int32_t (15! truncated to
            // 2004310016). Decode the callee's RETURN element type ourselves:
            // I8/U8 -> "l" (int64_t), R4/R8 -> "d" (double); BCL mappings
            // already carry ResultKind and win.
            call.ResultKind = resolved?.ResultKind ?? UserCallResultKind(md, (int)ins.Operand);
            // P-G3 shared-clone result: a call to a SHARED generic object clone
            // (Pair`2<...>/Box`1<string> -- CppName carries the
            // "_object"@arity instance suffix) returns the T slot as
            // Il2CppObject*. UserCallResultKind's VAR byte yields null (M3
            // int default) and would declare the result temp int32_t -- the
            // shared object return then type-mismatches the caller slot.
            if (call.ResultKind == null && call.Resolved?.CppName is { } cn
                && cn.Contains("_object_", StringComparison.Ordinal))
                call.ResultKind = "r";
            call.Args.AddRange(args);

            // Audit H5: P/Invoke string arguments must NOT share the runtime's
            // rotating static marshal buffer -- two string args in one call
            // would alias (the callee sees the last-marshalled string twice).
            // Each string arg is marshalled into its OWN heap buffer (a temp
            // of kind "cstr") BEFORE the call and freed AFTER it. This also
            // removes the runtime's 4096-byte truncation (heap buffers are
            // unbounded).
            var pinvokeFreeTemps = new List<(string Name, string FreeFn)>();
            if (call.IsPInvoke && call.StringArgIndices.Count > 0)
            {
                for (var i = 0; i < call.Args.Count; i++)
                {
                    if (!call.StringArgIndices.Contains(i)) continue;
                    var mtemp = $"t{temps.Count}";
                    temps.Add(mtemp);
                    // wf2 (winforms1): CharSet.Unicode -> UTF-16 marshal
                    // (il2cpp_pinvoke_string_to_utf16_alloc / utf16_free).
                    // stransi: CharSet.Ansi/None -> CP_ACP marshal
                    // (il2cpp_pinvoke_string_to_ansi_alloc / ansi_free); the
                    // generic "r:System.String" encoding stays UTF-8.
                    var enc = call.Resolved?.ParamTypeNames != null
                        && i < call.Resolved.ParamTypeNames.Length
                        ? call.Resolved.ParamTypeNames[i] : "r:System.String";
                    var isUtf16 = enc == "u16:System.String";
                    var isAnsi = enc == "a:System.String";
                    stmts.Add(new AssignStmt
                    {
                        Target = mtemp,
                        Value = new MarshalUtf8Expr { Arg = call.Args[i], Utf16 = isUtf16, Ansi = isAnsi },
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode)
                    });
                    call.Args[i] = new LocExpr { Name = mtemp };
                    pinvokeFreeTemps.Add((mtemp, isUtf16 ? "pinvoke_utf16_free"
                        : isAnsi ? "pinvoke_ansi_free" : "pinvoke_utf8_free"));
                }
                call.StringArgIndices.Clear(); // args are pre-marshalled now
            }
            // P-G3 R-E (GENERIC-SHARE-DESIGN.md section 5.2): a SHARED generic
            // method's byref T params are Il2CppObject** slots (the shared
            // object ABI). A VALUE argument (int32 local) must be BOXED into a
            // bridge temp whose ADDRESS is passed, then UNBOXED back into the
            // local after the call:
            //   tN = il2cpp_box_int32(x);   call(fn, &tN);  x = il2cpp_unbox_int32(tN);
            // A REFERENCE slot (&string local) passes through untouched (the
            // slot already IS an Il2CppObject*, reinterpreted as
            // Il2CppObject**). The bridge temp is declared Il2CppObject*
            // (ExprKind of the box call -> "ref") and GC-rooted.
            var byrefBridgeSuffix = new List<Stmt>();
            var sharedByref = md != null ? SharedByrefVarSlots(md, (int)ins.Operand) : null;
            if (sharedByref != null)
            {
                var argOffset = resolved is { IsInstance: true } ? 1 : 0; // this precedes explicit params
                for (var bi = 0; bi < sharedByref.Length && bi + argOffset < call.Args.Count; bi++)
                {
                    if (!sharedByref[bi]) continue;
                    var bArgIdx = bi + argOffset;
                    var bArg = call.Args[bArgIdx];
                    if (bArg is not LocAddrExpr laB || LocWidth(laB.Name, ctx, tempWidths) == "r")
                    {
                        // P-G4 (2): a field ADDRESS argument (ref obj.field) to a
                        // SHARED byref slot. A REFERENCE-typed field's address
                        // is &f == Il2CppObject** -> the callee's Il2CppObject**
                        // slot matches directly (zero-copy, correct).
                        if (!SharedByrefFieldArgSlotIsValue(md, types, bArg))
                            continue; // reference slot: &slot is Il2CppObject**-compatible (zero-copy)
                        // P-G5: `ref obj.Field` / `ref Type.Field` over a VALUE
                        // slot. MEASURED on net48 (case swapfield): SwapRef(ref
                        // o.I, ref p.I) swaps the two FIELDS (30,10), the same
                        // holds for int64/double fields, and Bump(ref o.I)
                        // leaves the field at default(T). So the bridge is
                        // exactly the local one -- box the field VALUE into a
                        // rooted temp, pass the box's address, then write the
                        // unboxed result back INTO THE FIELD.
                        if (bArg is FieldAddrExpr faB)
                        {
                            var faRead = new FieldExpr
                            {
                                Object = faB.Object,
                                FieldCppName = faB.FieldCppName,
                                OwnerFullName = faB.OwnerFullName,
                                IsRefField = false,
                                IsValueTypeObject = faB.IsValueTypeObject,

                            };
                            AddSharedByrefBridge(bi, bArg, faRead, v => new FieldAssignStmt
                            {
                                Object = faB.Object,
                                FieldCppName = faB.FieldCppName,
                                OwnerFullName = faB.OwnerFullName,
                                IsRefField = false,
                                IsValueTypeObject = faB.IsValueTypeObject,
                                RefTargetFullName = "",
                                Value = v,
                                SourceOffset = ins.Offset,
                                SourceOpName = OpName(ins.OpCode),
                            });
                            continue;
                        }
                        if (bArg is StaticFieldAddrExpr sfaB)
                        {
                            // The static field's C++ name IS "Owner.Field" (the
                            // same target the stsfld fold writes), so the box
                            // read is a StaticFieldExpr and the write-back is a
                            // plain dotted AssignStmt.
                            var sfaRead = new StaticFieldExpr { FieldName = sfaB.FieldName };
                            AddSharedByrefBridge(bi, bArg, sfaRead, v => new AssignStmt
                            {
                                Target = sfaB.FieldName,
                                Value = v,
                                SourceOffset = ins.Offset,
                                SourceOpName = OpName(ins.OpCode),
                            });
                            continue;
                        }
                        throw new NotSupportedException(
                            $"shared byref arg {bArg.GetType().Name} over a VALUE slot has no write-back bridge " +
                            "(only a local address, an instance field address and a static field address are bridged)");
                    }
                    AddSharedByrefBridge(bi, laB, new LocExpr { Name = laB.Name }, v => new AssignStmt
                    {
                        Target = laB.Name,
                        Value = v,
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode),
                    });
                }

                // One byref-T bridge: box the value behind `addr` into a rooted
                // temp, hand the box's ADDRESS to the shared object-ABI call,
                // and register `writeBack` so the callee's mutation reaches the
                // caller's storage once the call returns.
                void AddSharedByrefBridge(int paramIdx, Expr addr, Expr boxSource, Func<Expr, Stmt> writeBack)
                {
                    var btemp = $"t{temps.Count}";
                    temps.Add(btemp);
                    tempWidths[btemp] = "r";
                    // D5 (surface 4): the box/unbox bridge is TYPE-AWARE -- a
                    // VALUE slot's box/unbox helper is keyed on the shared MVAR
                    // param's CONCRETE type (the MethodSpec genArg: int32 ->
                    // il2cpp_box_int32, double -> icall_box_double, ...).
                    var br = SharedByrefConcreteBridge(md!, (int)ins.Operand, paramIdx, types);
                    string boxFn, boxDeref, unboxFn, unboxKind;
                    bool userValueBox = false;
                    if (br?.IsUserValue == true)
                    {
                        // D5: a USER VALUE TYPE byref slot. The box goes through
                        // the SAME box_{token} emission path the per-instance
                        // P25b path uses (struct -> the struct's own {T}_class
                        // descriptor + byte-exact payload copy; enum ->
                        // icall_box_enum). The VALUE is read as a value
                        // expression (the box_{token} emitter wraps &(value)
                        // itself). The unbox: user structs use the unbox_{token}
                        // cast emitter (*reinterpret_cast<T*>(...)); user enums
                        // unbox through icall_unbox_long (accepts enum boxes,
                        // int64 carrier -- the enum local slot is int32_t).
                        boxFn = $"box_{br.UserValueToken}";
                        boxDeref = "";
                        unboxFn = br.IsEnum
                            ? "il2cpp::runtime::icall_unbox_long"
                            : $"unbox_{br.UserValueToken}";
                        unboxKind = br.UnboxKind;
                        userValueBox = true;
                    }
                    else
                    {
                        (boxFn, boxDeref, unboxFn, unboxKind) =
                            br != null
                            ? (br.BoxFn, br.BoxDeref, br.UnboxFn, br.UnboxKind)
                            : ("il2cpp::il2cpp_box_int32", "deref:i", "il2cpp::il2cpp_unbox_int32", "i");
                    }
                    var boxCall = new CallExpr
                    {
                        Function = boxFn,
                        ReturnsRef = true,
                        Resolved = boxFn == "il2cpp::il2cpp_box_int32"
                            ? new ResolvedMethod(boxFn, 2, false, true, true,
                                new[] { false, false }, false, ParamTypeNames: new[] { "", boxDeref })
                            : new ResolvedMethod(boxFn, 1, false, true, true,
                                new[] { false }, false, ParamTypeNames: new[] { boxDeref }),
                    };
                    if (boxFn == "il2cpp::il2cpp_box_int32")
                        boxCall.Args.Add(new NullExpr());
                    // D5 user value types: pass the VALUE (the box_{token}
                    // emitter adds the &(...) itself). Primitives pass the
                    // ADDRESS (the boxDeref cast reads the value through it).
                    boxCall.Args.Add(userValueBox ? boxSource : addr);
                    stmts.Add(new AssignStmt
                    {
                        Target = btemp,
                        Value = boxCall,
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode),
                    });
                    call.Args[paramIdx + argOffset] = new LocAddrExpr { Name = btemp };
                    var unboxCall = new CallExpr
                    {
                        Function = unboxFn,
                        ReturnsRef = false,
                        ResultKind = unboxKind,
                        Resolved = new ResolvedMethod(unboxFn, 1, false, true, false,
                            new[] { true }, false),
                    };
                    unboxCall.Args.Add(new LocExpr { Name = btemp });
                    // The callee is the SHARED object-ABI clone: its `a =
                    // default(T)` lowers to a null store into the byref slot.
                    // MEASURED on net48 (swapfield Bump(ref o.I)): the caller's
                    // field becomes default(T), i.e. 0 -- not a
                    // NullReferenceException. So a null box on the way back means
                    // "the callee reset the slot": write default(T) instead of
                    // unboxing null (every unbox helper throws NRE on null).
                    var boxSlot = new LocExpr { Name = btemp };
                    var lblDefault = $"L_byrefdef_{temps.Count}";
                    var lblAfter = $"L_byrefend_{temps.Count}";
                    byrefBridgeSuffix.Add(new BranchStmt
                    {
                        TargetLabel = lblDefault,
                        Condition = new BinaryExpr { Op = BinaryOp.Ceq, Left = boxSlot, Right = new IntConstExpr { Value = 0 } },
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode),
                    });
                    byrefBridgeSuffix.Add(writeBack(unboxCall));
                    byrefBridgeSuffix.Add(new BranchStmt
                    {
                        TargetLabel = lblAfter,
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode),
                    });
                    byrefBridgeSuffix.Add(new LabelStmt { Name = lblDefault, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                    byrefBridgeSuffix.Add(writeBack(SharedByrefDefaultValue(unboxKind)));
                    byrefBridgeSuffix.Add(new LabelStmt { Name = lblAfter, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                }
            }
            if (resolved == null || resolved.ReturnsValue)
            {
                Push(call, ins);
                // M5 wiring: record the result's managed type (StringBuilder
                // method results, boxed writes are recorded at the Box site).
                // GetType on a NON-virtual call (csc emits `call` for sealed
                // receivers -- `"hi".GetType()` is call, not callvirt) takes
                // the same tracking as the callvirt arm above: the icall's
                // ReturnTypeName is null, so the CppName special-case must
                // match here too or a later MemberInfo::get_Name on the
                // result loses its receiver hint and misclassifies as static
                // (typechain callvirt-on-non-instance).
                if (resolved?.CppName == "il2cpp::runtime::icall_object_get_type" && stack.Peek() is LocExpr gt2)
                    objTypes[gt2.Name] = "System.Type"; // GetType() -> the type NAME string; .Name on it is identity
                else if (resolved?.ReturnTypeName != null && stack.Peek() is LocExpr rt2)
                    objTypes[rt2.Name] = BareManagedName(resolved.ReturnTypeName);
                // D5 (surface 4): a SHARED method-level generic method's MVAR
                // return (PickUser2::Pick<int> -- the callee emits the boxed
                // object slot) records the CONCRETE T on the result temp. The
                // callvirt arm already does this via SharedReturnConcreteType;
                // a STATIC shared method arrives through the `call` path, so it
                // is recorded here too -- the consumer (Console.WriteLine /
                // Int32::ToString) then unboxes by the tracked type.
                else if (md != null && stack.Peek() is LocExpr rtc
                    && VarReturnIsObjectSlot(md, (int)ins.Operand)
                    && SharedReturnConcreteType(md, (int)ins.Operand) is string callSharedRet)
                    objTypes[rtc.Name] = callSharedRet;
                foreach (var mt in pinvokeFreeTemps)
                    stmts.Add(new CallStmt { Function = mt.FreeFn, Args = { new LocExpr { Name = mt.Name } }, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
            }
            else
            {
                var stmt = new CallStmt
                {
                    Function = call.Function,
                    SourceOffset = ins.Offset,
                    SourceOpName = OpName(ins.OpCode),
                    Resolved = call.Resolved
                };
                stmt.Args.AddRange(call.Args);
                stmts.Add(stmt);
                foreach (var mt in pinvokeFreeTemps)
                    stmts.Add(new CallStmt { Function = mt.FreeFn, Args = { new LocExpr { Name = mt.Name } }, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                // rv=false with a non-void .NET signature: the IL stack still
                // carries the dropped value (the caller's `pop` discards it).
                // Mirror that slot or the shadow stack desyncs (collchain
                // "dup on empty stack" -- see DiscardedValueExpr).
                PushDiscardSlot(ins);
            }
            // P-G3 R-E suffix: write the swapped values back into the caller's
            // locals AFTER the shared call completed (the callee mutated the
            // boxed bridge objects; unboxing reads the final object).
            foreach (var suf in byrefBridgeSuffix)
                stmts.Add(suf);
        }

        // Rule 2j property folding: fold a confirmed simple accessor call into
        // a field access.
        //   getter: this (or none) -> Push(FieldExpr/StaticFieldExpr) -- reuses
        //           the Ldfld/Ldsfld branch construction (incl. the
        //           IsRefField/IsValueTypeObject/EnsureCctor semantics).
        //   setter: this+value -> FieldAssignStmt/AssignStmt -- reuses the
        //           Stfld/Stsfld branches.
        //   setter is void: no Push (consistent with the existing void-call
        //   branch).
        void EmitFoldedAccessorCall(List<Stmt> stmts, Stack<Expr> stack, IlInstruction ins,
            List<Expr> args, FoldedAccessor acc, IReadOnlyList<TypeDef>? types)
        {
            var fieldName = acc.OwnerFullName + "." + acc.FieldName;
            if (acc.IsGetter)
            {
                if (acc.IsStatic)
                {
                    Push(new StaticFieldExpr { FieldName = fieldName }, ins);
                }
                else
                {
                    Push(new FieldExpr
                    {
                        Object = args[0],
                        FieldCppName = SanitizeField(acc.FieldName),
                        OwnerFullName = acc.OwnerFullName,
                        IsRefField = acc.FieldIsRef,
                        IsValueTypeObject = types != null && IsValueTypeOwner(acc.OwnerFullName, types)
                    }, ins);
                }
            }
            else
            {
                if (acc.IsStatic)
                {
                    // Postfix increment on a STATIC property (`get_SP; dup;
                    // ...; set_SP`) folds to a static field read + store; the
                    // dup'd getter value is a lazy StaticFieldExpr and must be
                    // snapshotted BEFORE the store (net48 hands the ORIGINAL
                    // value to the postfix expression's consumer).
                    SnapshotStackReads(stmts, stack, fieldName, ins, ReferencesStaticField,
                        StaticFieldWidthByName(types, acc.OwnerFullName, acc.FieldName),
                        staticFieldSlot: true);
                    stmts.Add(new AssignStmt { Target = fieldName, Value = args[0], SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                }
                else
                {
                    stmts.Add(new FieldAssignStmt
                    {
                        Object = args[0],
                        FieldCppName = SanitizeField(acc.FieldName),
                        OwnerFullName = acc.OwnerFullName,
                        IsRefField = acc.FieldIsRef,
                        RefTargetFullName = acc.FieldIsRef ? acc.RefTargetFullName : "",
                        IsValueTypeObject = types != null && IsValueTypeOwner(acc.OwnerFullName, types),
                        Value = args[1],
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode)
                    });
                }
            }
        }

        // newobj: allocate the object + call the instance ctor. IL semantics:
        // pop the ctor args (this implicit), allocate a zero-initialized
        // object, call .ctor(this, args...), push the object reference.
        // IR fold (two steps, performance-critical: the alloc temp is used
        // directly by the ctor, no extra copies):
        //   tN = il2cpp_object_new(&<T>_class)      (AssignStmt, Value = NewObjExpr allocation)
        //   <T>__ctor_<hash>(tN, args...)            (CallStmt, Function = call_{token})
        //   push tN                                  (object reference pushed)
        void EmitNewObj(List<Stmt> stmts, Stack<Expr> stack, List<string> temps, IlInstruction ins,
            MetadataReader? md, IReadOnlyList<TypeDef>? types,
            Dictionary<string, List<Expr>> exceptionMessages,
            Dictionary<string, string> exceptionCtorParamTypes)
        {
            var resolved = md != null
                ? MethodRefResolver.Resolve(md, (int)ins.Operand, types ?? Array.Empty<TypeDef>())
                : null;
            var paramCount = resolved?.ParamCount ?? 0;
            var args = new List<Expr>();
            for (var i = 0; i < paramCount; i++) args.Insert(0, stack.Pop());

            // Full type name: the .ctor's declaring type (TypeReference or
            // TypeDefinition).
            var typeFullName = md != null ? CtorTypeFullName(md, (int)ins.Operand) : "?";

            // GN-2 (Nullable): `newobj System.Nullable`1<T>::.ctor(T value)`
            // constructs a nullable VALUE. The runtime models a Nullable as an
            // Il2CppObject* (null for no value, boxed T for a value -- the
            // TypeEmitter M6 contract), so the ctor IS the boxing of the
            // underlying T. Fold it to the box_{ctorToken} CallExpr -- the
            // emitter's box arm decodes the Nullable-ctor MemberRef token and
            // dispatches on the underlying T (int32/long/double/user-struct...).
            // The general alloc+ctor path below would fabricate an object_new
            // of a descriptor that does not exist (System_Nullable_1_int_class).
            if (typeFullName.StartsWith("System.Nullable`1", StringComparison.Ordinal))
            {
                var nbName = $"t{temps.Count}";
                temps.Add(nbName);
                var nbBox = new CallExpr { Function = $"box_{ins.Operand}", ReturnsRef = true };
                nbBox.Args.AddRange(args);
                stmts.Add(new AssignStmt
                {
                    Target = nbName,
                    Value = nbBox,
                    SourceOffset = ins.Offset,
                    SourceOpName = OpName(ins.OpCode)
                });
                objTypes[nbName] = typeFullName;
                stack.Push(new LocExpr { Name = nbName });
                return;
            }
            // G1 (C1): a multidim ARRAY ctor (`new int[2,2]` lowers to
            // `newobj int32_t[0...,0...,0...]::.ctor(int32,int32)` -- the ctor's
            // DECLARING TYPE is the multidim array TypeSpec). The ctor args are
            // the per-dimension lengths; the allocation is a runtime multidim
            // array (il2cpp_array_new_multi<N> -- rank-fixed, row-major flat
            // data, GC scan unchanged). ReturnsRef: multidim arrays are
            // references (Il2CppArray*).
            if (md != null && TryDecodeMultidimCtor(md, (int)ins.Operand, out var mdElemSuffix, out var mdRank))
            {
                var mname = $"t{temps.Count}";
                temps.Add(mname);
                var mdAlloc = new CallExpr { Function = $"mdnew_{mdElemSuffix}{mdRank}", ReturnsRef = true };
                mdAlloc.Args.AddRange(args); // per-dim lengths (ctor args)
                stmts.Add(new AssignStmt
                {
                    Target = mname,
                    Value = mdAlloc,
                    SourceOffset = ins.Offset,
                    SourceOpName = OpName(ins.OpCode)
                });
                objTypes[mname] = MultidimTypeName(md, (int)ins.Operand); // e.g. "[int2]" for Get/Set owner routing
                stack.Push(new LocExpr { Name = mname });
                return;
            }

            // M3 Task 11: value-type newobj -- a struct instance is
            // initialized IN PLACE (no GC allocation; the value lives in the
            // declaring scope). The newobj folds to: {T} __t; {T}__ctor(&__t, args);
            // M5 wiring: the BCL 8-byte value types (System.DateTime /
            // System.TimeSpan) are runtime structs (Il2CppDateTime /
            // Il2CppTimeSpan) -- the same in-place path, with the ctor
            // resolved to the runtime icall (ResultIsReceiver).
            var isValueTypeNewobj = (TypeLookup.Find(types ?? Array.Empty<TypeDef>(), typeFullName) is { IsValueType: true })
                || typeFullName is "System.DateTime" or "System.TimeSpan" or "System.Decimal"
                // LOUD P0-02/03: Guid is a 16-byte value (uint8_t[16] slot);
                // the ctor writes the bytes in place (Guid.Parse ABI), not a
                // reference allocation.
                || typeFullName is "System.Guid"
                // P14 dtoffset: System.DateTimeOffset is a BCL value type
                // (Il2CppDateTimeOffset -- the same in-place path).
                || typeFullName is "System.DateTimeOffset"
                // P17 biginteger: System.Numerics.BigInteger is a BCL value
                // type (Il2CppBigInteger -- the same in-place path).
                || typeFullName is "System.Numerics.BigInteger"
                // wf2 (winforms1): System.IntPtr/UIntPtr are BCL VALUE types.
                || typeFullName is "System.IntPtr" or "System.UIntPtr"
                // handleref: System.Runtime.InteropServices.HandleRef is a BCL
                // VALUE type (two fields: _wrapper / _handle). It has no
                // TypeDef here, so the reference-allocation path emitted
                // `il2cpp_object_new(&System_Runtime_InteropServices_
                // HandleRef_class)` for an undeclared symbol (the winforms
                // probe's C++ build failure). The in-place path declares the
                // runtime mirror struct and the ctor writes its fields; the
                // P/Invoke bridge decays it to .Handle (see
                // src/runtime/include/internal/il2cpp_handleref.h).
                || typeFullName is "System.Runtime.InteropServices.HandleRef"
                // varargs: System.ArgIterator is a BCL value type. `new
                // ArgIterator(__arglist)` initializes the struct IN PLACE; the
                // ctor icall takes the slot address + the frame handle
                // (il2cpp_argiterator.h). Without this arm the newobj took the
                // reference-allocation path and emitted object_new on a
                // nonexistent descriptor.
                || typeFullName is "System.ArgIterator"
                // tuple1 (ValueTuple): System.ValueTuple`N is an ordinary BCL
                // VALUE type (a struct with Item1..ItemN + a .ctor). It has no
                // TypeDef here, so without this arm the ctor took the
                // reference-allocation path and emitted
                // `il2cpp_object_new(&System_ValueTuple_2_int_string_class)` +
                // a call to a ctor that is never generated (cmake: "not
                // declared"). The in-place path declares the struct temp and
                // lets the emitter lower the ctor to field stores.
                || ValueTupleSupport.IsValueTupleName(typeFullName);
            if (isValueTypeNewobj)
            {
                var vname = $"t{temps.Count}";
                temps.Add(vname);
                stmts.Add(new AssignStmt
                {
                    Target = vname,
                    Value = new ValueTypeNewExpr { TypeFullName = typeFullName, CtorToken = (int)ins.Operand },
                    SourceOffset = ins.Offset,
                    SourceOpName = OpName(ins.OpCode)
                });
                // icallwide: track the VALUE-TYPE receiver's declared type too
                // -- a later `loc.ToString()` / `loc.GetHashCode()` callvirt
                // (Roslyn emits parent System.Object for the overrides) needs
                // the hint to route to the domain icall (the same doctrine as
                // the delegate tracking below).
                objTypes[vname] = typeFullName;
                var vctor = new CallStmt
                {
                    Function = $"vctor_{ins.Operand}",
                    SourceOffset = ins.Offset,
                    SourceOpName = OpName(ins.OpCode)
                };
                vctor.Args.Add(new LocExpr { Name = vname });
                vctor.Args.AddRange(args);
                stmts.Add(vctor);
                stack.Push(new LocExpr { Name = vname });
                return;
            }

            // M3 Task 8 + delechain: delegate construction -- newobj
            // <DelegateType>::.ctor(object, native int) wraps the ldftn method
            // pointer (FuncPtrExpr) into a delegate object. Func`N as before;
            // delechain: ANY MulticastDelegate-derived type (custom delegates,
            // Action`N) goes through the same il2cpp_delegate_new path.
            // The construction is: t = il2cpp_delegate_new(&<method>, target);
            // push t. The ctor arguments are (target, method pointer) -- the
            // FuncPtrExpr is the native-int argument, the OTHER arg is the
            // bound instance (#42): ldnull (static binding -> nullptr) or the
            // bound object (instance binding -> the instance ABI's `this`).
            if (IsDelegateType(typeFullName, types))
            {
                var fptr = args.OfType<FuncPtrExpr>().FirstOrDefault();
                if (fptr != null)
                {
                    var target = args.FirstOrDefault(a => a is not FuncPtrExpr) ?? new NullExpr();
                    var dname = $"t{temps.Count}";
                    temps.Add(dname);
                    stmts.Add(new AssignStmt
                    {
                        Target = dname,
                        // BUG-2 (batch 177b): stamp the delegate with its TYPE
                        // ID (FNV-1a of the full type name). The runtime
                        // compares these at Combine time and raises
                        // ArgumentException on a heterogeneous combine
                        // (net48). The id rides WITH the object, so it survives
                        // every flow -- event backing fields, the compiler's
                        // cached-lambda static fields, joins, method args.
                        Value = new DelegateNewExpr { MethodToken = fptr.MethodToken, Target = target, TypeId = SigHash.UInt64Literal(typeFullName) },
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode)
                    });
                    objTypes[dname] = typeFullName; // delechain: delegate receivers need a tracked type (Equals fold / re-resolve hints)
                    stack.Push(new LocExpr { Name = dname });
                    return;
                }
            }

            // 1) Allocate: zero-initialized object -> temp (declared
            //    Il2CppObject* at emission).
            // M3: System.Exception newobj does not really allocate (the
            // exception object is not modeled -- throw uses a typed carrier);
            // the allocation is skipped and null is pushed (the ctor call is a
            // nop).
            // M5 wiring: the same holds for EVERY BCL exception type
            // (InvalidOperationException/... -- no runtime class descriptor
            // exists; the typed throw carries the carrier).
            // excmsg: EXCEPT the four Argument*/Exception carriers the runtime
            // now models with real descriptors (metadata.cpp) -- a constructed
            // instance answers GetType().Name / isinst / Message / ParamName,
            // so it must be a REAL object (the side table keys by pointer).
            var name = $"t{temps.Count}";
            temps.Add(name);
            objTypes[name] = typeFullName; // audit H8: the throw site needs the object.s type
            var isBclException = typeFullName.EndsWith("Exception", StringComparison.Ordinal)
                // A5 (myIList): a generic INSTANTIATION over System.Exception
                // ("List`1_System.Exception") is not an exception type -- the
                // backtick marks an instantiation name. Without this guard
                // the list itself took the BCL-exception null model (stfld
                // nullptr) instead of a real allocation.
                && !typeFullName.Contains('`')
                && !TypeLookup.Any(types ?? Array.Empty<TypeDef>(), typeFullName);
            // excmsg: the modelled exception hierarchy. System.Exception keeps
            // the ilistException rule (only a message-carrying instance needs
            // identity); the Argument* family ALWAYS allocates because the
            // constructed instance is an observable value (ParamName/Message/
            // GetType). SystemException is the shared base (never thrown
            // directly by user code, but keep the identity consistent).
            var isModelledException = typeFullName is "System.ArgumentException"
                or "System.ArgumentNullException" or "System.ArgumentOutOfRangeException"
                or "System.SystemException";
            // ilistException: a System.Exception built with a message
            // (`new Exception("a")`) and USED as a value (added to a list,
            // .Message read) needs a REAL object identity -- the message
            // side table keys by pointer, and a null model has no identity
            // to key on. Allocate really (BclAllocExpr routes System.Exception
            // to a plain-object allocation); the throw-site carrier path
            // above is untouched. All other BCL exceptions keep the null
            // model (throw-only paths).
            var isMessageCarrierException = (typeFullName == "System.Exception" && (resolved?.ParamCount ?? 0) >= 1)
                || isModelledException;
            if (isBclException && !isMessageCarrierException)
            {
                stmts.Add(new AssignStmt
                {
                    Target = name,
                    Value = new NullExpr(),
                    SourceOffset = ins.Offset,
                    SourceOpName = OpName(ins.OpCode)
                });
            }
            else
            {
                stmts.Add(new AssignStmt
                {
                    Target = name,
                    Value = new NewObjExpr { TypeFullName = typeFullName },
                    SourceOffset = ins.Offset,
                    SourceOpName = OpName(ins.OpCode)
                });
                // icallwide: track the reference-newobj receiver's declared
                // type (new Version(...).GetHashCode() chained callvirts need
                // the hint to route Object::GetHashCode to the domain icall).
                objTypes[name] = typeFullName;
            }
            // excext (m7m): remember the BCL-exception ctor args so the throw
            // site can forward the real message/paramName to il2cpp_throw_new.
            // excmsg: the Argument* family additionally records the ctor's
            // PARAMETER TYPES -- the throw site maps the overload SHAPE (a
            // single string is a paramName on ArgumentNullException but a
            // message on ArgumentException, and (string, Exception) is always
            // (message, inner)).
            if (isBclException)
            {
                exceptionMessages[name] = new List<Expr>(args);
                var ctorParamTypes = md != null
                    ? MethodRefResolver.CtorParamTypesOf(md, (int)ins.Operand)
                    : null;
                if (ctorParamTypes != null)
                {
                    exceptionCtorParamTypes[name] = ctorParamTypes;
                }
            }
            // 2) Construct: this = the temp object; args passed in original
            //    order.
            var ctor = new CallStmt
            {
                Function = $"call_{ins.Operand}",
                SourceOffset = ins.Offset,
                SourceOpName = OpName(ins.OpCode)
            };
            ctor.Args.Add(new LocExpr { Name = name });
            ctor.Args.AddRange(args);
            stmts.Add(ctor);
            // 3) The object reference is pushed.
            stack.Push(new LocExpr { Name = name });
        }

        // Whether the type is a value type (M3 Task 11: a struct has no object
        // header; field access uses '.').
        static bool IsValueTypeOwner(string ownerFullName, IReadOnlyList<TypeDef> types)
        {
            // D6 (valuetuple-local design): BCL System.ValueTuple`N instances
            // have no TypeDef in the transpiled set, but their locals are the
            // raw tuple STRUCT (D3 declaration) -- field reads must use
            // `(&loc)->ItemK` / `.ItemK`, NOT the null-check + object-cast
            // roundtrip (a struct does not convert to Il2CppObject*).
            if (ownerFullName.StartsWith("System.ValueTuple`", StringComparison.Ordinal))
                return true;
            // tuple1 (Tuple): System.Tuple`N (the reference Tuple class) is
            // modelled with the SAME generated struct layout as ValueTuple`N
            // (the C# tuple syntax) -- field reads use the struct path too.
            if (ownerFullName.StartsWith("System.Tuple`", StringComparison.Ordinal))
                return true;
            // Nested value types (async state machines): the field owner
            // resolves via metadata GetFullName ('+' form) while expanded
            // nested TypeDefs key by SHORT FullName -- match either form or
            // the struct falls to the reference null-check path (static_cast
            // of a struct pointer, asynccase cmake FAIL).
            var t = types.FirstOrDefault(x => x.MatchesFullName(ownerFullName));
            return t != null && t.IsValueType;
        }

        // P25b (user struct box): the unbox ResultKind for a USER value type --
        // "us:{CppName}" (CodeGenerator.ExprKind strips the prefix to the C++
        // struct declaration type; the unbox_ emit site casts the runtime
        // payload). Returns null for everything else (primitives handle their
        // own kinds; enums/BCL structs are not routed here).
        static string? UnboxUserStructKind(string fullName, IReadOnlyList<TypeDef>? types)
        {
            if (types == null) return null;
            var t = types.FirstOrDefault(x => x.MatchesFullName(fullName));
            if (t == null || !t.IsValueType) return null;
            return "us:" + TypeEmitter.TypeCppName(t);
        }

        // O3 (user-struct arrays): the ldelema ResultKind for a TRANSLATED
        // user VALUE type -- "sp:{CppName}" (CodeGenerator.ExprKind strips the
        // prefix to the C++ STRUCT POINTER declaration type "{CppName}*" and the
        // ldelema_ emit site reinterprets the array element pointer to that
        // type; FieldAccess on the address then dereferences the struct's
        // fields). Returns null for everything else (primitives/references/Bcl
        // structs keep the intptr_t "ptr" carrier, unchanged).
        static string? LdelemaUserStructKind(MetadataReader? md, int token, IReadOnlyList<TypeDef>? types)
        {
            if (md == null) return null;
            // D/E (value-element TypeSpec): a System.ValueTuple`N instantiation
            // is a generated struct -- its element address is a typed struct
            // pointer (the following ldflda ItemK dereferences fields through
            // it), exactly like a translated user struct.
            var (specIsValue, specDef, specClosed) = DecodeGenericInstSpec(md, token);
            if (specIsValue && ValueTupleSupport.IsValueTupleName(specClosed))
            {
                ValueTupleSupport.Require(specClosed!);
                return "sp:" + ValueTupleSupport.StructName(specClosed!);
            }
            // D/E (value-element TypeSpec): a System.Nullable`1<T> element is
            // ONE Il2CppObject* cell (the boxed-object nullable
            // representation), so its address is an Il2CppObject** slot --
            // "naddr" (the temp/consumer declarations need the double
            // pointer, and the nullable members deref it to the object).
            if (specIsValue && specDef == "System.Nullable`1")
                return "naddr";
            _ = specDef;
            if (types == null) return null;
            var fullName = TypeFullNameFromToken(md, token);
            var t = types.FirstOrDefault(x => x.MatchesFullName(fullName));
            if (t == null || !t.IsValueType) return null;
            return "sp:" + TypeEmitter.TypeCppName(t);
        }

        // M4 Task 5 + M5 wiring: runtime icall methods (resolved by
        // MethodRefResolver to names under "il2cpp::" -- System.String helpers,
        // StringBuilder/Random/Hashtable members, Object::GetType, ...) are
        // DIRECT calls, not vtable dispatch -- the receiver (this) is the
        // first argument and the runtime function does the dispatch/checks.
        static bool IsRuntimeDirect(ResolvedMethod? resolved)
        {
            return resolved != null && resolved.CppName.StartsWith("il2cpp::", StringComparison.Ordinal);
        }

        // M3 Task 8 + delechain: delegate Invoke detection -- the callvirt
        // target is a delegate type's Invoke. Recognized: (1) System.Func`N /
        // System.Action`N (generic delegates; a TypeSpec parent matches by
        // definition name); (2) user-defined delegates -- the base-class chain
        // reaches System.MulticastDelegate / System.Delegate (Roslyn emits a
        // custom delegate's Invoke as a MemberRef whose parent is the delegate
        // TypeDefinition).
        static bool IsDelegateInvoke(ResolvedMethod? resolved, MetadataReader? md, int token, IReadOnlyList<TypeDef>? types)
        {
            if (md == null || resolved == null) return false;
            var handle = MetadataTokens.Handle(token);
            string? owner = null;
            string? name = null;
            if (handle.Kind == HandleKind.MethodDefinition)
            {
                var m = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                owner = md.GetFullName(m.GetDeclaringType());
                name = md.GetString(m.Name);
            }
            else if (handle.Kind == HandleKind.MemberReference)
            {
                var mr = md.GetMemberReference((MemberReferenceHandle)handle);
                owner = mr.Parent.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)mr.Parent),
                    // delechain: a custom delegate declared in this module --
                    // the Invoke MemberRef's parent is the delegate TypeDef.
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)mr.Parent),
                    // M3 generics: a generic delegate (Func`2<int,int>) carries
                    // a TypeSpec parent; match the definition name.
                    HandleKind.TypeSpecification => GenericDefinitionNameOf(md, (TypeSpecificationHandle)mr.Parent),
                    _ => null,
                };
                name = md.GetString(mr.Name);
            }
            return name == "Invoke" && (IsDelegateType(owner, types)
                // excext (fold-skip family): the NON-generic System.Action --
                // IsDelegateType matches the `N generic forms only; the
                // parameterless BCL delegate still invokes through the same
                // DelegateInvokeExpr multicast ABIs.
                || owner == "System.Action");
        }

        // delechain: whether the call token is Equals(object) on a delegate
        // receiver -- csc emits `delegate.Equals(x)` with parent System.Object
        // (Equals is declared on Object, the same Roslyn quirk as
        // MemberInfo::get_Name; the System.Delegate parent shape is accepted
        // too). MemberRef shape only (BCL methods are always MemberRefs in
        // user code).
        static bool IsDelegateEqualsCall(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (md.GetString(mr.Name) != "Equals" || mr.Parent.Kind != HandleKind.TypeReference) return false;
            var parent = md.GetFullName((TypeReferenceHandle)mr.Parent);
            return parent == "System.Object" || parent == "System.Delegate";
        }

        // delechain: whether the call token is System.Delegate::op_Equality /
        // op_Inequality (C# always emits ==/!= for any delegate type to the
        // base System.Delegate static operators). isInequality = the result
        // must be negated (!= semantics).
        static bool DelegateEqualityOperator(MetadataReader md, int token, out bool isInequality)
        {
            isInequality = false;
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            if (md.GetFullName((TypeReferenceHandle)mr.Parent) != "System.Delegate") return false;
            var name = md.GetString(mr.Name);
            isInequality = name == "op_Inequality";
            return name == "op_Equality" || name == "op_Inequality";
        }

        // The name of a PARAMETERLESS-compatible System.Object instance member
        // reached through a `constrained. T; callvirt` -- "ToString" (0 params),
        // "GetHashCode" (0 params) or "Equals" (1 object param). Returns null
        // for every other call (a static, a non-Object owner, another arity),
        // so the caller's user-struct arm can never bind an unrelated method.
        static string? ObjectInstanceMemberName(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            string? parentName = null;
            string? methodName = null;
            BlobHandle sigHandle = default;
            if (handle.Kind == HandleKind.MemberReference)
            {
                var mr = md.GetMemberReference((MemberReferenceHandle)handle);
                parentName = mr.Parent.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)mr.Parent),
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)mr.Parent),
                    _ => null,
                };
                methodName = md.GetString(mr.Name);
                sigHandle = mr.Signature;
            }
            else if (handle.Kind == HandleKind.MethodDefinition)
            {
                var mdef = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                parentName = md.GetFullName(mdef.GetDeclaringType());
                methodName = md.GetString(mdef.Name);
                sigHandle = mdef.Signature;
            }
            else return null;
            if (parentName != "System.Object") return null;
            var sig = md.GetBlobReader(sigHandle);
            sig.ReadSignatureHeader();
            var pc = sig.ReadCompressedInteger();
            return methodName switch
            {
                "ToString" or "GetHashCode" when pc == 0 => methodName,
                "Equals" when pc == 1 => methodName,
                _ => null,
            };
        }

        // G2 (audit recurse-recheck f14b): whether the call is
        // System.Object::ToString() -- Roslyn emits ToString on an `object`
        // typed local as a MemberRef on the System.Object TypeRef (BCL members
        // never carry a MethodDefinition token). Read the param count from the
        // signature header: only the 0-param (the sole .NET Object.ToString
        // signature). On a hit, a callvirt with an unknown receiver type falls
        // back to icall_object_to_string instead of the vtable.
        // The simple member name of a call token (ValueTuple member dispatch).
        static string MemberSimpleName(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            return handle.Kind switch
            {
                HandleKind.MemberReference => md.GetString(md.GetMemberReference((MemberReferenceHandle)handle).Name),
                HandleKind.MethodDefinition => md.GetString(md.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
                _ => "",
            };
        }

        static bool IsObjectToString(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            string? parentName = null;
            string? methodName = null;
            BlobHandle sigHandle = default;
            if (handle.Kind == HandleKind.MemberReference)
            {
                var mr = md.GetMemberReference((MemberReferenceHandle)handle);
                // The parent is a TypeReference from user assemblies but a
                // TypeDefinition for calls INSIDE mscorlib itself.
                parentName = mr.Parent.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)mr.Parent),
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)mr.Parent),
                    _ => null,
                };
                methodName = md.GetString(mr.Name);
                sigHandle = mr.Signature;
            }
            else if (handle.Kind == HandleKind.MethodDefinition)
            {
                // Same-assembly callvirt (Roslyn emits a MethodDef token for
                // System.Object::ToString inside mscorlib -- the intra-
                // mscorlib constrained-ToString gap).
                var mdef = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                parentName = md.GetFullName(mdef.GetDeclaringType());
                methodName = md.GetString(mdef.Name);
                sigHandle = mdef.Signature;
            }
            else return false;
            if (parentName != "System.Object") return false;
            if (methodName != "ToString") return false;
            var sig = md.GetBlobReader(sigHandle);
            sig.ReadSignatureHeader();
            return sig.ReadCompressedInteger() == 0;
        }

        // Whether the call token is System.Object::GetHashCode() (the
        // parameterless one) -- the GetHashCode mirror of IsObjectToString.
        // Used by the constrained-enum value-carrier fold: Roslyn emits
        // `ldloca; constrained. <enum>; callvirt Object::GetHashCode`.
        static bool IsObjectGetHashCode(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            string? parentName = null;
            string? methodName = null;
            BlobHandle sigHandle = default;
            if (handle.Kind == HandleKind.MemberReference)
            {
                var mr = md.GetMemberReference((MemberReferenceHandle)handle);
                parentName = mr.Parent.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)mr.Parent),
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)mr.Parent),
                    _ => null,
                };
                methodName = md.GetString(mr.Name);
                sigHandle = mr.Signature;
            }
            else if (handle.Kind == HandleKind.MethodDefinition)
            {
                var mdef = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                parentName = md.GetFullName(mdef.GetDeclaringType());
                methodName = md.GetString(mdef.Name);
                sigHandle = mdef.Signature;
            }
            else return false;
            if (parentName != "System.Object") return false;
            if (methodName != "GetHashCode") return false;
            var sig = md.GetBlobReader(sigHandle);
            sig.ReadSignatureHeader();
            return sig.ReadCompressedInteger() == 0;
        }

        // typeof(T) fold (reflection audit F1): whether the call token is
        // System.Type::GetTypeFromHandle -- a BCL static method, always a
        // MemberRef with parent TypeRef(System.Type) in user code (same shape
        // as IsObjectToString).
        static bool IsGetTypeFromHandle(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            return md.GetFullName((TypeReferenceHandle)mr.Parent) == "System.Type"
                && md.GetString(mr.Name) == "GetTypeFromHandle";
        }

        // IsObjectMember: whether a call token targets System.Object::<name>
        // with the given explicit parameter count (the constrained-call shape
        // of a value type's inherited virtual members).
        static bool IsObjectMember(MetadataReader md, int token, string name, int paramCount)
        {
            var handle = MetadataTokens.Handle(token);
            string? parentName;
            string? methodName;
            BlobHandle sigHandle;
            if (handle.Kind == HandleKind.MemberReference)
            {
                var mr = md.GetMemberReference((MemberReferenceHandle)handle);
                parentName = mr.Parent.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)mr.Parent),
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)mr.Parent),
                    _ => null,
                };
                methodName = md.GetString(mr.Name);
                sigHandle = mr.Signature;
            }
            else if (handle.Kind == HandleKind.MethodDefinition)
            {
                var mdef = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                parentName = md.GetFullName(mdef.GetDeclaringType());
                methodName = md.GetString(mdef.Name);
                sigHandle = mdef.Signature;
            }
            else return false;
            if (parentName != "System.Object" || methodName != name) return false;
            var sig = md.GetBlobReader(sigHandle);
            sig.ReadSignatureHeader();
            return sig.ReadCompressedInteger() == paramCount;
        }

        // InitializeArray fold (e2e G1): whether the call token is
        // RuntimeHelpers::InitializeArray -- a BCL static method, always a
        // MemberRef with a TypeRef parent in user code. Both parents are
        // accepted: System.Runtime.CompilerServices.RuntimeHelpers (the real
        // csc emission shape) and System.Array (the synthetic test-harness
        // historical shape).
        static bool IsInitializeArray(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            var parent = md.GetFullName((TypeReferenceHandle)mr.Parent);
            return (parent == "System.Runtime.CompilerServices.RuntimeHelpers" || parent == "System.Array")
                && md.GetString(mr.Name) == "InitializeArray";
        }

        // InitializeArray fold: primitive element type -> byte width. The
        // newarr ELEMENT TOKEN decides the width (the RVA field's declared
        // type is a size-encoded __StaticArrayInitTypeSize=N value type -- no
        // element type in the field signature). 8-byte elements are
        // recognized but rejected loudly (no stelem_i8 emission support).
        static int? ElementWidth(string typeName) => typeName switch
        {
            "System.Byte" or "System.SByte" or "System.Boolean" => 1,
            "System.Int16" or "System.UInt16" or "System.Char" => 2,
            "System.Int32" or "System.UInt32" or "System.Single" => 4,
            "System.Int64" or "System.UInt64" or "System.Double" => 8,
            _ => null,
        };

        // LOUD P0-02/03: the RVA-initializer element widths. These are the CLR
        // array-cell strides (DateTime/TimeSpan 8, Guid/Decimal/DateTimeOffset
        // 16) that the InitializeArray fold must slice per element; kept
        // SEPARATE from ElementWidth so `sizeof <T>` keeps its existing fold
        // shape (the runtime struct size is emitted by the sizeof_{token} arm).
        static int? RvaElementWidth(string typeName) => typeName switch
        {
            "System.DateTime" or "System.TimeSpan" => 8,
            "System.Guid" or "System.Decimal" or "System.DateTimeOffset" => 16,
            _ => ElementWidth(typeName),
        };

        // The RVA fold also accepts a user ENUM element: the .data blob is
        // laid out with the UNDERLYING type's width (csc emits the same width
        // family for the element-wise stelem.i1/i2/i4/i8 stores), and the
        // newarr token names the enum TypeDef. The width comes from the raw
        // `value__` field signature -- the translated model collapses every
        // enum backing field to int32, which would slice a byte-based blob
        // four bytes per element. A TypeRef enum lives in a referenced
        // assembly whose metadata this reader does not load -> width unknown.
        static int? RvaElementWidthForToken(MetadataReader md, int elemToken, string typeName)
            => RvaElementWidth(typeName) ?? EnumUnderlyingWidth(md, elemToken);

        static int? EnumUnderlyingWidth(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.TypeDefinition) return null;
            var typeDef = md.GetTypeDefinition((TypeDefinitionHandle)handle);
            foreach (var fieldHandle in typeDef.GetFields())
            {
                var field = md.GetFieldDefinition(fieldHandle);
                if (md.GetString(field.Name) != "value__") continue;
                var sig = md.GetBlobReader(field.Signature);
                if (sig.ReadByte() != 0x06) return null; // FIELD calling convention
                return sig.ReadByte() switch
                {
                    0x02 or 0x04 or 0x05 => 1, // bool / sbyte / byte
                    0x03 or 0x06 or 0x07 => 2, // char / short / ushort
                    0x08 or 0x09 or 0x0C => 4, // int / uint / float
                    0x0A or 0x0B or 0x0D => 8, // long / ulong / double
                    _ => null,
                };
            }
            return null;
        }

        // b5-gaps 13: a multidim VALUE element -> the allocation/accessor
        // (suffix, cell width) channel, or null when the element has no value
        // channel (a user struct -- the GC reference-field bitmap is not
        // modeled here). The width conventions are the arrayinit-vt batch's:
        // DateTime/TimeSpan are 8-byte packed-image cells (kind in the top
        // bits), Guid/Decimal/DateTimeOffset are the 16-byte runtime value
        // structs, and an ENUM element rides its UNDERLYING type's primitive
        // suffix (the raw value__ signature byte -- the int32 carrier model
        // must not widen a byte/short/long enum's cell).
        static (string Suffix, int Width)? MultidimValueElemSuffix(MetadataReader md, EntityHandle handle)
        {
            if (handle.Kind is not (HandleKind.TypeReference or HandleKind.TypeDefinition)) return null;
            var name = handle.Kind == HandleKind.TypeReference
                ? md.GetFullName((TypeReferenceHandle)handle)
                : md.GetFullName((TypeDefinitionHandle)handle);
            switch (name)
            {
                case "System.DateTime": return ("dt", 8);
                case "System.TimeSpan": return ("ts", 8);
                case "System.Guid": return ("guid", 16);
                case "System.Decimal": return ("dec", 16);
                case "System.DateTimeOffset": return ("dto", 16);
            }
            // A user enum (a TypeDef whose base is System.Enum): the suffix is
            // the UNDERLYING primitive (the cell width the RVA blob and the
            // accessor both honor). A BCL enum TypeRef (no local metadata)
            // maps to the int32 carrier (the whitelist enums are all
            // int32-underlying).
            if (handle.Kind == HandleKind.TypeDefinition)
            {
                var td = md.GetTypeDefinition((TypeDefinitionHandle)handle);
                var baseName = td.BaseType.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)td.BaseType),
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)td.BaseType),
                    _ => null,
                };
                if (baseName == "System.Enum")
                {
                    var underlying = EnumUnderlyingSuffix(md, (TypeDefinitionHandle)handle);
                    return underlying ?? ("int", 4);
                }
                return null; // a user STRUCT: no value channel (b5-gaps residual)
            }
            return CodeGenerator.IsKnownBclEnumName(name) ? ("int", 4) : null;
        }

        // The user enum's underlying primitive (suffix, width) from the raw
        // value__ field signature (the EnumUnderlyingWidth decode with the
        // suffix attached).
        static (string Suffix, int Width)? EnumUnderlyingSuffix(MetadataReader md, TypeDefinitionHandle handle)
        {
            var typeDef = md.GetTypeDefinition(handle);
            foreach (var fieldHandle in typeDef.GetFields())
            {
                var field = md.GetFieldDefinition(fieldHandle);
                if (md.GetString(field.Name) != "value__") continue;
                var sig = md.GetBlobReader(field.Signature);
                if (sig.ReadByte() != 0x06) return null; // FIELD calling convention
                return sig.ReadByte() switch
                {
                    0x02 => ("bool", 1),
                    0x03 => ("char", 2),
                    0x04 => ("sbyte", 1),
                    0x05 => ("byte", 1),
                    0x06 => ("short", 2),
                    0x07 => ("ushort", 2),
                    0x08 => ("int", 4),
                    0x09 => ("uint", 4),
                    0x0A => ("long", 8),
                    0x0B => ("ulong", 8),
                    _ => null,
                };
            }
            return null;
        }

        // IL gap batch b23: the temp width of an ldind.* read. int32-width
        // reads stay in the uniform int32 slot (null); int64 -> "l";
        // float/double -> "d"; native-int -> "ptr" (intptr_t).
        static string? LdindResultKind(ILOpCode op) => op switch
        {
            ILOpCode.Ldind_i8 => "l",
            ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8 => "d",
            ILOpCode.Ldind_i => "ptr",
            _ => null,
        };

        // b23 (arrayops): the temp width of a fixed-width ldelem.* read --
        // mirror of LdindResultKind. int32-width reads (i4/u4/i1/u1/i2/u2)
        // stay in the uniform int32 slot (null); int64 -> "l"; float/double
        // -> "d" (the emitter reads r4 into a double slot).
        static string? LdelemResultKind(ILOpCode op) => op switch
        {
            ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_i => "l",
            ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8 => "d",
            _ => null,
        };

        // Whether the full type name is a managed reference (vs a value type).
        // User types come from the translated TypeDef list; a TypeRef to a
        // BCL type falls back to the closed set of BCL value types (everything
        // else -- classes, arrays, objects -- is a reference).
        static bool IsRefTypeName(string name, IReadOnlyList<TypeDef>? types)
        {
            if (types != null)
            {
                var td = TypeLookup.Find(types, name);
                if (td != null) return !td.IsValueType;
            }
            return name is not ("System.Byte" or "System.SByte" or "System.Boolean"
                or "System.Int16" or "System.UInt16" or "System.Char"
                or "System.Int32" or "System.UInt32" or "System.Single"
                or "System.Int64" or "System.UInt64" or "System.Double"
                or "System.IntPtr" or "System.UIntPtr"
                or "System.DateTime" or "System.TimeSpan" or "System.Decimal" or "System.Guid"
                or "System.DateTimeOffset");
        }

        // D/E (value-element TypeSpec): decode a GENERIC INSTANTIATION TypeSpec
        // into (isValueType, defFullName, closedInstanceName). The closed name
        // is produced only for shapes with a C++ struct (ValueTupleSupport);
        // every other instantiation answers null for it.
        static (bool IsValue, string? DefName, string? ClosedName) DecodeGenericInstSpec(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.TypeSpecification) return (false, null, null);
            var specHandle = (TypeSpecificationHandle)handle;
            var sig = md.GetBlobReader(md.GetTypeSpecification(specHandle).Signature);
            if (sig.ReadByte() != 0x15) return (false, null, null);
            var marker = sig.ReadByte();
            var def = sig.ReadTypeHandle();
            var defName = def.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)def),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)def),
                _ => null,
            };
            var isValue = marker == 0x11;
            var closed = isValue && ValueTupleSupport.IsValueTupleName(defName)
                ? ValueTupleSupport.ClosedNameFromSpec(md, specHandle)
                : null;
            return (isValue, defName, closed);
        }

        // Classify a generic array/pointer element TypeDefOrRef/TypeSpec token
        // for the fixed-width ldelem/stelem emission: (IsRef, Width, Name).
        // TypeSpecs: a VAR generic parameter (0x13) instantiates to int32 under
        // the M3 single-instantiation convention; a GenericInst (0x15) maps by
        // definition kind (CLASS -> reference, VALUETYPE -> value, width
        // unresolvable). Returns Width=null + IsRef=false for value types the
        // fixed-width emitter cannot render (8-byte primitives / user structs).
        static (bool IsRef, int? Width, string Name) ElementAccessInfo(
            MetadataReader? md, int token, IReadOnlyList<TypeDef>? types)
        {
            if (md == null) return (false, null, "?");
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference)
            {
                var name = TypeFullNameFromToken(md, token);
                return (IsRefTypeName(name, types), ElementWidth(name), name);
            }
            if (handle.Kind == HandleKind.TypeSpecification)
            {
                var spec = md.GetTypeSpecification((TypeSpecificationHandle)handle);
                var sig = md.GetBlobReader(spec.Signature);
                var header = sig.ReadByte();
                if (header == 0x13) return (false, 4, "T"); // VAR generic parameter -> int32 (M3 convention)
                if (header == 0x15)
                {
                    var defType = sig.ReadByte(); // CLASS(0x12) / VALUETYPE(0x11) marker
                    if (defType != 0x11) return (true, null, "?"); // CLASS inst -> ref
                    // D/E (value-element TypeSpec): a VALUETYPE generic
                    // instantiation. Two shapes are expressible:
                    //   * System.Nullable`1<T> -- this runtime models a
                    //     nullable VALUE as an Il2CppObject* slot (null = no
                    //     value, boxed T = value; TypeEmitter M6 contract), so
                    //     a nullable array element IS a GC reference.
                    //   * System.ValueTuple`N<...> -- a real generated struct
                    //     (ValueTupleSupport), accessed through the typed
                    //     struct-copy path (ldelem_us_/stelem_us_).
                    // Any other value instantiation stays width-unknown (the
                    // token-based emitter has no C++ name for it).
                    var defHandle = sig.ReadTypeHandle();
                    var defName = defHandle.Kind switch
                    {
                        HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
                        HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
                        _ => null,
                    };
                    if (defName == "System.Nullable`1") return (true, null, defName);
                    if (ValueTupleSupport.IsValueTupleName(defName))
                    {
                        var closed = ValueTupleSupport.ClosedNameFromSpec(md, (TypeSpecificationHandle)handle);
                        if (closed != null)
                        {
                            ValueTupleSupport.Require(closed);
                            return (false, null, closed);
                        }
                    }
                    return (false, null, "?");
                }
                throw new NotSupportedException($"type token 0x{token:X8}: TypeSpec header 0x{header:X2} not VAR(0x13)/GenericInst(0x15)");
            }
            throw new NotSupportedException($"type token kind {handle.Kind} not supported for element access");
        }

        // ldelem <T> (token form) -> the fixed-width ILOpCode the existing
        // emitter renders, or null when the element needs the token-based
        // emitter (CodeGenerator patch). Signedness drives i1-vs-u1 / i2-vs-u2
        // (sign vs zero extension into the int32 slot); b23 adds the 8-byte
        // element family (int64/uint64 -> i8, double -> r8) and the float
        // element (r4 -- a float[] element read as int32_t would be garbage
        // bit interpretation).
        //
        // p0linq4 (D1): the C++ name of a USER VALUE TYPE array element (a
        // translated struct), or null when the token is not one. The token
        // ldelem/stelem arms use it to emit a typed struct copy (the emitter
        // renders the dereference at the element pointer).
        static string? UserStructElementCpp(MetadataReader? md, int token, IReadOnlyList<TypeDef>? types)
        {
            if (md == null || types == null) return null;
            var (isRef, _, name) = ElementAccessInfo(md, token, types);
            if (isRef || name == "?") return null;
            // D/E (value-element TypeSpec): a System.ValueTuple`N instantiation
            // is a generated struct (ValueTupleSupport), not a TypeDef -- the
            // typed struct copy path is exactly right for it.
            if (ValueTupleSupport.IsValueTupleName(name))
            {
                ValueTupleSupport.Require(name);
                return ValueTupleSupport.StructName(name);
            }
            var def = types.FirstOrDefault(t => t.MatchesFullName(name)
                && t.IsValueType && t.BaseTypeName != "System.Enum");
            if (def == null) return null;
            TypeEmitter.LayoutInstanceFields(def);
            return TypeEmitter.TypeCppName(def);
        }

        static ILOpCode? MapGenericLdelemOp(MetadataReader? md, int token, IReadOnlyList<TypeDef>? types)
        {
            var (isRef, width, name) = ElementAccessInfo(md, token, types);
            if (isRef) return ILOpCode.Ldelem_ref;
            return width switch
            {
                1 => name == "System.SByte" ? ILOpCode.Ldelem_i1 : ILOpCode.Ldelem_u1,
                2 => name == "System.Int16" ? ILOpCode.Ldelem_i2 : ILOpCode.Ldelem_u2,
                4 => name == "System.Single" ? ILOpCode.Ldelem_r4 : ILOpCode.Ldelem_i4,
                8 => name == "System.Double" ? ILOpCode.Ldelem_r8 : ILOpCode.Ldelem_i8,
                _ => null,
            };
        }

        // stelem <T> (token form) -> the fixed-width ILOpCode, or null when the
        // element needs the token-based emitter.
        static ILOpCode? MapGenericStelemOp(MetadataReader? md, int token, IReadOnlyList<TypeDef>? types)
        {
            var (isRef, width, name) = ElementAccessInfo(md, token, types);
            if (isRef) return ILOpCode.Stelem_ref;
            return width switch
            {
                1 => ILOpCode.Stelem_i1,
                2 => ILOpCode.Stelem_i2,
                4 => name == "System.Single" ? ILOpCode.Stelem_r4 : ILOpCode.Stelem_i4,
                8 => name == "System.Double" ? ILOpCode.Stelem_r8 : ILOpCode.Stelem_i8,
                _ => null,
            };
        }

        // batch 2026-10-05 (iopairprobe B_decimal): whether a generic
        // ldelem/stelem element token is System.Decimal -- the 16-byte
        // value-element array access (the fixed-width ILOpCode switch above
        // caps at 8 bytes). These get the dedicated "dec" accessor arms
        // (ldelem_dec / stelem_dec) in the emitter.
        static bool IsDecimalElement(MetadataReader? md, int token, IReadOnlyList<TypeDef>? types)
            => IsTokenElement(md, token, types, "System.Decimal");

        // LOUD P0-02/03: the other value-element array tokens whose CLR stride
        // has no fixed-width ILOpCode (DateTime/TimeSpan 8, Guid 16). They get
        // dedicated token accessor arms in the emitter.
        static bool IsTokenElement(MetadataReader? md, int token, IReadOnlyList<TypeDef>? types, string typeName)
        {
            var (_, _, name) = ElementAccessInfo(md, token, types);
            return name == typeName;
        }

        // Token-based value-type ops (ldobj/stobj/initobj/sizeof): the operand
        // is a TypeDefOrRef whose resolution needs metadata (the pure-byte fold
        // path cannot resolve the type -- loud gap until md is supplied).
        string RequireTypeName(IlInstruction ins)
        {
            if (md == null)
                throw new NotSupportedException(
                    $"{OpName(ins.OpCode)} at offset 0x{ins.Offset:X4}: the operand is a TypeDefOrRef token -- folding needs metadata to resolve the type (pure-byte path cannot)");
            return TypeFullNameFromToken(md, (int)ins.Operand);
        }

        // checked-op family name suffix (Add_ovf -> "add", Add_ovf_un -> "add_un").
        static string CheckedOpName(ILOpCode op) => op switch
        {
            ILOpCode.Add_ovf => "add",
            ILOpCode.Add_ovf_un => "add_un",
            ILOpCode.Sub_ovf => "sub",
            ILOpCode.Sub_ovf_un => "sub_un",
            ILOpCode.Mul_ovf => "mul",
            ILOpCode.Mul_ovf_un => "mul_un",
            _ => throw new NotSupportedException($"checked {op}"),
        };

        // InitializeArray fold: map a field RVA to file bytes via the PE
        // section headers. Returns null when the RVA lies outside every
        // section or the image is too short (callers treat it as a loud
        // malformed-input case).
        static byte[]? ReadRvaBytes(PEReader pe, int rva, int need)
        {
            var img = pe.GetEntireImage();
            foreach (var s in pe.PEHeaders.SectionHeaders)
            {
                var span = Math.Max(s.VirtualSize, s.SizeOfRawData);
                if (rva < s.VirtualAddress || rva >= s.VirtualAddress + span) continue;
                var fileOff = rva - s.VirtualAddress + s.PointerToRawData;
                if (fileOff < 0 || fileOff + need > img.Length) return null;
                return img.GetContent(fileOff, need).ToArray();
            }
            return null;
        }

        // typeof-chain (F1 follow-up): whether the call token is get_Name --
        // .Name on the folded value takes the short name. Roslyn emits the Name
        // MemberRef with parent the declaring type System.Reflection.MemberInfo
        // (not System.Type) -- both parents are accepted (matching the
        // resolver's TryMapType/TryMapObject MemberInfo dual path).
        static bool IsTypeGetName(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            var parent = md.GetFullName((TypeReferenceHandle)mr.Parent);
            return (parent == "System.Type" || parent == "System.Reflection.MemberInfo")
                && md.GetString(mr.Name) == "get_Name";
        }

        // refl2 (A): whether the call token is Type::get_FullName (the typeof
        // fold chain renders the net48 FullName -- see ReflectionDisplayName).
        static bool IsTypeGetFullName(MetadataReader md, int token)        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            var parent = md.GetFullName((TypeReferenceHandle)mr.Parent);
            return (parent == "System.Type" || parent == "System.Reflection.MemberInfo")
                && md.GetString(mr.Name) == "get_FullName";
        }

        // refl2 (A): the typeof-chain Type predicates / constructors. Each
        // helper recognizes ONE net48 member by name on the System.Type owner;
        // the fold decides the answer from the ldtoken token.
        static bool IsTypeMemberNamed(MetadataReader md, int token, string memberName)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            return md.GetFullName((TypeReferenceHandle)mr.Parent) == "System.Type"
                && md.GetString(mr.Name) == memberName;
        }

        static bool IsTypeGetIsConstructedGenericType(MetadataReader md, int token)
            => IsTypeMemberNamed(md, token, "get_IsConstructedGenericType");

        // The TypeAttributes predicate family: get_IsAbstract / get_IsSealed /
        // get_IsPublic / get_IsNested. Returns the flag mask (typeinfo.h
        // typeFlags bit) the fold must test, or null when the member is not one
        // of them.
        static int? IsTypeGetTypeAttributePredicate(MetadataReader md, int token)
        {
            if (IsTypeMemberNamed(md, token, "get_IsAbstract")) return 0x01;
            if (IsTypeMemberNamed(md, token, "get_IsSealed")) return 0x02;
            if (IsTypeMemberNamed(md, token, "get_IsPublic")) return 0x04;
            if (IsTypeMemberNamed(md, token, "get_IsNested")) return 0x08;
            return null;
        }

        static bool IsTypeGetArrayRank(MetadataReader md, int token)
            => IsTypeMemberNamed(md, token, "GetArrayRank");

        // MakeArrayType() -- the 0-arg form folds; the (int rank) overload rides
        // the IL stack and keeps the runtime path.
        static bool IsTypeMakeArrayType(MetadataReader md, int token, out int rank)
        {
            rank = 1;
            if (!IsTypeMemberNamed(md, token, "MakeArrayType")) return false;
            var handle = MetadataTokens.Handle(token);
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            var sig = md.GetBlobReader(mr.Signature);
            sig.ReadSignatureHeader();
            return sig.ReadCompressedInteger() == 0;
        }

        static bool IsTypeMakeByRefType(MetadataReader md, int token)
            => IsTypeMemberNamed(md, token, "MakeByRefType");

        static bool IsTypeMakePointerType(MetadataReader md, int token)
            => IsTypeMemberNamed(md, token, "MakePointerType");

        // refl2 (A): the typeof-chain TYPE-KIND predicates. Returns the
        // predicate key (used by TypeKindFlag) or null.
        static string? TypeKindPredicateOf(MetadataReader md, int token)
        {
            if (IsTypeMemberNamed(md, token, "get_IsEnum")) return "enum";
            if (IsTypeMemberNamed(md, token, "get_IsInterface")) return "interface";
            if (IsTypeMemberNamed(md, token, "get_IsValueType")) return "valuetype";
            if (IsTypeMemberNamed(md, token, "get_IsClass")) return "class";
            if (IsTypeMemberNamed(md, token, "get_IsPrimitive")) return "primitive";
            if (IsTypeMemberNamed(md, token, "get_IsArray")) return "array";
            if (IsTypeMemberNamed(md, token, "get_IsGenericType")) return "generic";
            if (IsTypeMemberNamed(md, token, "get_IsGenericTypeDefinition")) return "genericdef";
            return null;
        }

        static bool IsTypeGetBaseType(MetadataReader md, int token)
            => IsTypeMemberNamed(md, token, "get_BaseType");

        // The assembly suffix of a full type NAME (a resolved base name has no
        // token at hand): a System.* name belongs to mscorlib, anything else to
        // this assembly.
        static string AssemblySuffixOfName(MetadataReader md, string fullName)
            => fullName.StartsWith("System.", StringComparison.Ordinal)
                ? BclAssemblySuffix()
                : ThisAssemblySuffix(md);

        static bool IsTypeGetElementType(MetadataReader md, int token)
            => IsTypeMemberNamed(md, token, "GetElementType");

        static bool IsTypeGetGenericTypeDefinition(MetadataReader md, int token)
            => IsTypeMemberNamed(md, token, "GetGenericTypeDefinition");

        // The ELEMENT type's display triple for an array receiver, or null when
        // the receiver is not an array (net48 GetElementType -> null).
        static (string Identity, string Full, string Short, string AsmSuffix)? ElementTypeDisplayOf(
            MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.TypeSpecification) return null;
            var sig = md.GetBlobReader(
                md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
            var et = sig.ReadByte();
            if (et is not (0x1D or 0x14)) return null;
            // Re-read the blob and decode the element with its defining token.
            var sig2 = md.GetBlobReader(
                md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
            _ = sig2.ReadByte();
            var elemFull = DecodeDisplayType(md, ref sig2, true, out var elemTok);
            var elemShort = ShortTypeName(elemFull);
            return (elemFull, elemFull, elemShort, AssemblySuffixOfToken(md, elemTok));
        }

        // The DEFINITION's display triple for a constructed generic receiver, or
        // null when the receiver is not a closed GENERICINST (a non-generic
        // receiver keeps the runtime path, which raises net48's
        // InvalidOperationException).
        static (string Identity, string Full, string Short, string AsmSuffix)? GenericDefinitionDisplayOf(
            MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.TypeSpecification) return null;
            var sig = md.GetBlobReader(
                md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
            if (sig.ReadByte() != 0x15) return null;
            _ = sig.ReadByte();
            var defTok = sig.ReadTypeHandle();
            var defName = defTok.Kind switch
            {
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defTok),
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defTok),
                HandleKind.TypeSpecification => TypeNameForReflection(
                    md, MetadataTokens.GetToken(defTok)),
                _ => null,
            };
            if (defName == null) return null;
            return (defName, defName, ShortTypeName(defName), AssemblySuffixOfToken(md, defTok));
        }

        // The predicate answer for a typeof token. A TypeDef answers from its
        // real metadata shape; a TypeRef (BCL) from the curated core table
        // (documented boundary -- only the types the net48 probes reach are
        // pinned; anything else answers the conservative default).
        static bool TypeKindFlag(MetadataReader md, int token, string pred,
            IReadOnlyList<TypeDef>? types = null)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind == HandleKind.TypeSpecification)
            {
                var sig = md.GetBlobReader(
                    md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                var et = sig.ReadByte();
                switch (pred)
                {
                    case "array":
                        return et is 0x1D or 0x14;
                    case "valuetype":
                        if (et is 0x1D or 0x14) return false;
                        if (et == 0x15)
                        {
                            // A constructed generic is a value type only for
                            // System.Nullable`1 (the net48 builtin struct).
                            _ = sig.ReadByte();
                            var d = sig.ReadTypeHandle();
                            var dn = d.Kind switch
                            {
                                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)d),
                                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)d),
                                _ => null,
                            };
                            return dn == "System.Nullable`1";
                        }
                        if (et == 0x13)
                        {
                            // VAR (generic type parameter T) -- resolve through the
                            // enclosing clone's InstantiationSuffixHint, mirroring
                            // TypeNameForReflection (:10126). The M3 single-instantiation
                            // default is System.Int32, which IS a value type.
                            var vIdx = sig.ReadCompressedInteger();
                            var hint = MethodRefResolver.InstantiationSuffixHint;
                            if (hint != null)
                            {
                                var part = MethodRefResolver.InstantiationSuffixHintParts
                                    is { Length: > 1 }
                                    ? MethodRefResolver.InstantiationVarSuffix(vIdx)
                                    : hint.Split('_') is { } pp
                                        && vIdx < pp.Length ? pp[vIdx] : null;
                                if (part != null)
                                {
                                    var full = SuffixToManagedTypeName(part);
                                    return BclTypeKindFlag(full, "valuetype")
                                        || (types != null && IsValueTypeOwner(full, types));
                                }
                            }
                            return BclTypeKindFlag("System.Int32", "valuetype");
                        }
                        return false;
                    case "class":
                        return !TypeKindFlag(md, token, "valuetype")
                            && !TypeKindFlag(md, token, "interface");
                    case "generic":
                        return et == 0x15;
                    case "genericdef":
                        return false; // a TypeSpec is a CONSTRUCTED instance
                    case "interface":
                    case "enum":
                        return false;
                    case "primitive":
                        return et is 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07
                            or 0x08 or 0x09 or 0x0A or 0x0B or 0x0C or 0x0D
                            or 0x18 or 0x19;
                    default:
                        return false;
                }
            }
            if (handle.Kind == HandleKind.TypeDefinition)
            {
                var td = md.GetTypeDefinition((TypeDefinitionHandle)handle);
                var baseName = BaseTypeNameOfToken(md, td.BaseType);
                var isEnum = baseName == "System.Enum";
                var isValueType = baseName == "System.ValueType";
                var isIface = (td.Attributes & System.Reflection.TypeAttributes.Interface) != 0;
                switch (pred)
                {
                    case "enum": return isEnum;
                    case "interface": return isIface;
                    case "valuetype": return isValueType || isEnum;
                    case "class": return !isValueType && !isEnum && !isIface;
                    case "primitive": return false;
                    case "array": return false;
                    case "generic": return td.GetGenericParameters().Count > 0;
                    case "genericdef": return td.GetGenericParameters().Count > 0;
                    default: return false;
                }
            }
            // BCL TypeRef: the curated core table.
            var name = handle.Kind == HandleKind.TypeReference
                ? md.GetFullName((TypeReferenceHandle)handle)
                : null;
            if (name == null) return false;
            return BclTypeKindFlag(name, pred);
        }

        // The curated BCL shape table (a TypeRef carries no metadata rows in this
        // reader). Documented boundary: the core mscorlib types the net48 probes
        // reach; anything else answers false.
        static bool BclTypeKindFlag(string fullName, string pred)
        {
            bool IsPrimitiveName() => fullName is "System.Boolean" or "System.Char"
                or "System.SByte" or "System.Byte" or "System.Int16" or "System.UInt16"
                or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64"
                or "System.Single" or "System.Double" or "System.IntPtr" or "System.UIntPtr";
            bool IsEnumName() => fullName == "System.DayOfWeek"
                || MethodRefResolver.HasBclEnumNameTable(fullName);
            switch (pred)
            {
                case "primitive": return IsPrimitiveName();
                case "enum": return IsEnumName();
                case "interface":
                    return fullName is "System.IDisposable" or "System.ICloneable"
                        or "System.IComparable" or "System.IFormattable"
                        or "System.IConvertible" or "System.IEquatable`1"
                        or "System.Collections.IEnumerable" or "System.Collections.IEnumerator"
                        or "System.Collections.Generic.IEnumerable`1"
                        or "System.Collections.Generic.IEnumerator`1"
                        or "System.Collections.Generic.IList`1"
                        or "System.Collections.Generic.ICollection`1"
                        or "System.Collections.Generic.IDictionary`2";
                case "valuetype":
                    return IsPrimitiveName() || IsEnumName()
                        || fullName is "System.Decimal" or "System.DateTime"
                            or "System.TimeSpan" or "System.Guid" or "System.ValueType"
                            or "System.Enum" or "System.Nullable`1";
                case "class":
                    return !BclTypeKindFlag(fullName, "valuetype")
                        && !BclTypeKindFlag(fullName, "interface");
                case "array": return false;
                case "generic":
                    return GenericArityFromName(fullName) > 0;
                case "genericdef":
                    return GenericArityFromName(fullName) > 0;
                default: return false;
            }
        }

        // The base type's full name for a typeof token (null for System.Object /
        // an interface / an unregistered BCL name).
        static string? BaseTypeNameOf(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                {
                    var td = md.GetTypeDefinition((TypeDefinitionHandle)handle);
                    if ((td.Attributes & System.Reflection.TypeAttributes.Interface) != 0) return null;
                    var baseName = BaseTypeNameOfToken(md, td.BaseType);
                    if (baseName == null) return null;
                    // A root class (base System.Object) answers System.Object; a
                    // System.Object receiver answers null (net48).
                    if (baseName == "System.Object"
                        && md.GetFullName((TypeDefinitionHandle)handle) == "System.Object")
                        return null;
                    return baseName;
                }
                case HandleKind.TypeReference:
                    return BclBaseTypeName(md.GetFullName((TypeReferenceHandle)handle));
                case HandleKind.TypeSpecification:
                {
                    var sig = md.GetBlobReader(
                        md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                    var et = sig.ReadByte();
                    if (et is 0x1D or 0x14) return "System.Array";
                    if (et == 0x15)
                    {
                        _ = sig.ReadByte();
                        var d = sig.ReadTypeHandle();
                        var dn = d.Kind switch
                        {
                            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)d),
                            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)d),
                            _ => null,
                        };
                        if (dn == "System.Nullable`1") return "System.ValueType";
                        return "System.Object";
                    }
                    return null;
                }
                default:
                    return null;
            }
        }

        static string? BaseTypeNameOfToken(MetadataReader md, EntityHandle h)
        {
            if (h.IsNil) return null;
            return h.Kind switch
            {
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)h),
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)h),
                HandleKind.TypeSpecification => TypeNameForReflection(md, MetadataTokens.GetToken(h)),
                _ => null,
            };
        }

        // The curated BCL base table (documented boundary: the core mscorlib
        // hierarchy the net48 probes reach).
        static string? BclBaseTypeName(string fullName) => fullName switch
        {
            "System.Object" => null,
            "System.ValueType" => "System.Object",
            "System.Enum" => "System.ValueType",
            "System.Array" => "System.Object",
            "System.Delegate" => "System.Object",
            "System.MulticastDelegate" => "System.Delegate",
            "System.Exception" => "System.Object",
            "System.SystemException" => "System.Exception",
            "System.ArgumentException" => "System.SystemException",
            "System.ArgumentNullException" => "System.ArgumentException",
            "System.ArgumentOutOfRangeException" => "System.ArgumentException",
            "System.InvalidOperationException" => "System.SystemException",
            "System.NotSupportedException" => "System.SystemException",
            "System.NullReferenceException" => "System.SystemException",
            "System.IndexOutOfRangeException" => "System.SystemException",
            "System.String" => "System.Object",
            "System.Int32" => "System.ValueType",
            "System.Int64" => "System.ValueType",
            "System.Boolean" => "System.ValueType",
            "System.Char" => "System.ValueType",
            "System.Double" => "System.ValueType",
            "System.Single" => "System.ValueType",
            "System.Byte" => "System.ValueType",
            "System.SByte" => "System.ValueType",
            "System.Int16" => "System.ValueType",
            "System.UInt16" => "System.ValueType",
            "System.UInt32" => "System.ValueType",
            "System.UInt64" => "System.ValueType",
            "System.Decimal" => "System.ValueType",
            "System.DateTime" => "System.ValueType",
            "System.TimeSpan" => "System.ValueType",
            "System.Guid" => "System.ValueType",
            "System.IntPtr" => "System.ValueType",
            "System.UIntPtr" => "System.ValueType",
            "System.DayOfWeek" => "System.Enum",
            _ when fullName.EndsWith("Exception", StringComparison.Ordinal) => "System.SystemException",
            _ => "System.Object",
        };

        // Type.Equals(object) -- the net48 instance equality (the MemberRef
        // parent is System.Type, or System.Reflection.MemberInfo for the same
        // Roslyn-quirk family as get_Name).
        static bool IsTypeEqualsMember(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            var parent = md.GetFullName((TypeReferenceHandle)mr.Parent);
            return (parent == "System.Type" || parent == "System.Reflection.MemberInfo")
                && md.GetString(mr.Name) == "Equals";
        }

        // Type.MetadataToken -- csc emits the MemberRef with parent
        // System.Reflection.MemberInfo (the same Roslyn quirk as get_Name), so
        // BOTH owners are accepted.
        static bool IsTypeGetMetadataToken(MetadataReader md, int token)
        {
            if (IsTypeMemberNamed(md, token, "get_MetadataToken")) return true;
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            return md.GetFullName((TypeReferenceHandle)mr.Parent) == "System.Reflection.MemberInfo"
                && md.GetString(mr.Name) == "get_MetadataToken";
        }

        // A closed GENERICINST TypeSpec with no open (VAR/MVAR) argument.
        static bool IsConstructedGenericToken(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.TypeSpecification) return false;
            var sig = md.GetBlobReader(
                md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
            if (sig.ReadByte() != 0x15) return false;
            _ = sig.ReadByte();
            _ = sig.ReadTypeHandle();
            var argc = sig.ReadCompressedInteger();
            for (var i = 0; i < argc; i++)
            {
                var et = sig.ReadByte();
                if (et is 0x13 or 0x1E) return false; // open parameter
                if (et is 0x11 or 0x12) { _ = sig.ReadTypeHandle(); continue; }
                if (et is 0x1D or 0x14 or 0x0F or 0x10 or 0x15)
                {
                    _ = DecodeDisplayType(md, ref sig, false, out _);
                    continue;
                }
                // primitives / string / object: closed
            }
            return true;
        }

        // The net48 TypeAttributes predicate answer for a typeof token. A
        // TypeDef answers the exact metadata bits; a TypeRef (BCL / other
        // assembly) has no attribute rows in this reader, so the well-known core
        // table answers. Arrays are sealed public classes (net48).
        static int TypeAttributeFlag(MetadataReader md, int token, int mask)
        {
            var handle = MetadataTokens.Handle(token);
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                {
                    var td = md.GetTypeDefinition((TypeDefinitionHandle)handle);
                    var attrs = td.Attributes;
                    if (mask == 0x01) return (attrs & System.Reflection.TypeAttributes.Abstract) != 0 ? 1 : 0;
                    if (mask == 0x02) return (attrs & System.Reflection.TypeAttributes.Sealed) != 0 ? 1 : 0;
                    if (mask == 0x04) return ((int)attrs & 0x7) == 0x1 ? 1 : 0;
                    if (mask == 0x08) return td.IsNested ? 1 : 0;
                    return 0;
                }
                case HandleKind.TypeReference:
                    return BclTypeAttributeFlag(md.GetFullName((TypeReferenceHandle)handle), mask);
                case HandleKind.TypeSpecification:
                {
                    var sig = md.GetBlobReader(
                        md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                    var et = sig.ReadByte();
                    if (et is 0x1D or 0x14)
                    {
                        if (mask == 0x02) return 1; // sealed
                        if (mask == 0x04) return 1; // public
                        return 0;
                    }
                    if (et == 0x15)
                    {
                        _ = sig.ReadByte();
                        var defTok = sig.ReadTypeHandle();
                        if (mask == 0x04) return 1;
                        return defTok.Kind switch
                        {
                            HandleKind.TypeDefinition => TypeAttributeFlag(
                                md, MetadataTokens.GetToken(defTok), mask),
                            HandleKind.TypeReference => BclTypeAttributeFlag(
                                md.GetFullName((TypeReferenceHandle)defTok), mask),
                            _ => 0,
                        };
                    }
                    return 0;
                }
                default:
                    return 0;
            }
        }

        // The BCL core-type attribute table (a TypeRef has no attribute rows in
        // this reader). Only the shapes the net48 probes reach are pinned; any
        // other TypeRef answers 0 (documented boundary).
        static int BclTypeAttributeFlag(string fullName, int mask)
        {
            switch (mask)
            {
                case 0x01: // IsAbstract
                    return fullName is "System.ValueType" or "System.Enum"
                        or "System.Delegate" or "System.MulticastDelegate"
                        or "System.Array" ? 1 : 0;
                case 0x02: // IsSealed
                    return fullName is "System.Int32" or "System.UInt32"
                        or "System.Int16" or "System.UInt16" or "System.Int64"
                        or "System.UInt64" or "System.Byte" or "System.SByte"
                        or "System.Boolean" or "System.Char" or "System.Single"
                        or "System.Double" or "System.Decimal" or "System.String"
                        or "System.IntPtr" or "System.UIntPtr" ? 1 : 0;
                case 0x04: // IsPublic
                    return 1;
                case 0x08: // IsNested
                    return fullName.Contains('+') ? 1 : 0;
                default:
                    return 0;
            }
        }

        // net48 GetArrayRank on a typeof token: null when the token is not an
        // array (the caller keeps the runtime path, which raises net48's
        // ArgumentException).
        static int? ArrayRankOfToken(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.TypeSpecification) return null;
            var sig = md.GetBlobReader(
                md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
            var et = sig.ReadByte();
            if (et == 0x1D) return 1;               // SZARRAY
            if (et == 0x14)                          // ARRAY
            {
                _ = DecodeDisplayType(md, ref sig, false, out _);
                return Math.Max(sig.ReadCompressedInteger(), 1);
            }
            return null;
        }

        // p-refl2-gapmatrix (B08): the member-VALUE provenance of an expression
        // -- a local holding a stored member value, or a CallExpr whose
        // provenance the GetMethod/GetField fold attached. (Captures the
        // per-Fold provenance table, so it is a non-static local function.)
        bool TryMemberProvenance(Expr e, out (int TypeToken, string MemberName, bool IsField) prov)
        {
            if (e is LocExpr le && memberProvenanceByLocal.TryGetValue(le.Name, out prov)) return true;
            if (e is CallExpr ce && ce.MemberProvenance is { } cp) { prov = cp; return true; }
            prov = default;
            return false;
        }

        // p-refl2-gapmatrix (B07/B08/B11/B17/B18): whether the call token is a
        // member flag predicate whose answer the fold can resolve from the
        // metadata (given the member's declaring type). Returns the predicate
        // key or null.
        static bool IsMemberFlagPredicate(MetadataReader md, int token, out string kind)
        {
            kind = "";
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            var parent = md.GetFullName((TypeReferenceHandle)mr.Parent);
            if (parent is not ("System.Reflection.MethodInfo" or "System.Reflection.MethodBase"
                or "System.Reflection.FieldInfo" or "System.Reflection.PropertyInfo"
                or "System.Reflection.ConstructorInfo" or "System.Reflection.EventInfo"
                or "System.Reflection.MemberInfo"))
                return false;
            switch (md.GetString(mr.Name))
            {
                case "get_IsAbstract": kind = "abstract"; return true;
                case "get_IsVirtual": kind = "virtual"; return true;
                case "get_IsGenericMethod": kind = "genericmethod"; return true;
                case "get_IsInitOnly": kind = "initonly"; return true;
                case "get_IsLiteral": kind = "literal"; return true;
                default: return false;
            }
        }

        // p-refl2-gapmatrix (B08): the net48 flag answer for a member of the
        // declaring type the fold recorded. A TypeDef answers from its own
        // metadata; a TypeSpec resolves to its definition; anything else
        // (a TypeRef -- a member of another assembly) answers 0 (documented
        // boundary, the runtime fallback covers it).
        static int MemberFlagAnswer(MetadataReader md, (int TypeToken, string MemberName, bool IsField) prov, string kind)
        {
            var handle = MetadataTokens.Handle(prov.TypeToken);
            if (handle.Kind == HandleKind.TypeSpecification)
            {
                var sig = md.GetBlobReader(
                    md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                _ = DecodeDisplayType(md, ref sig, false, out var defTok);
                handle = defTok;
            }
            if (handle.Kind != HandleKind.TypeDefinition) return 0;
            var td = md.GetTypeDefinition((TypeDefinitionHandle)handle);
            if (prov.IsField)
            {
                foreach (var fh in td.GetFields())
                {
                    var fd = md.GetFieldDefinition(fh);
                    if (md.GetString(fd.Name) != prov.MemberName) continue;
                    if (kind == "literal") return (fd.Attributes & System.Reflection.FieldAttributes.Literal) != 0 ? 1 : 0;
                    if (kind == "initonly") return (fd.Attributes & System.Reflection.FieldAttributes.InitOnly) != 0 ? 1 : 0;
                }
                return 0;
            }
            foreach (var mh in td.GetMethods())
            {
                var mdef = md.GetMethodDefinition(mh);
                if (md.GetString(mdef.Name) != prov.MemberName) continue;
                switch (kind)
                {
                    case "abstract": return (mdef.Attributes & System.Reflection.MethodAttributes.Abstract) != 0 ? 1 : 0;
                    case "virtual": return (mdef.Attributes & System.Reflection.MethodAttributes.Virtual) != 0 ? 1 : 0;
                    case "genericmethod": return mdef.GetGenericParameters().Count > 0 ? 1 : 0;
                }
            }
            return 0;
        }

        // p-refl2-gapmatrix (C04/C05/C06/C09): whether the call token is
        // Type::get_Assembly (the typeof-chain Assembly VALUE fold).
        static bool IsTypeGetAssembly(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            return md.GetFullName((TypeReferenceHandle)mr.Parent) == "System.Type"
                && md.GetString(mr.Name) == "get_Assembly";
        }

        // p-refl2-gapmatrix (C04/C05/C06/C09): the DEFINING assembly's SIMPLE
        // NAME for a typeof token -- the Assembly VALUE the get_Assembly fold
        // produces. A TypeDef -> this assembly; a TypeRef -> its AssemblyRef
        // name (mscorlib for the core library's scope); a TypeSpec -> its
        // element / generic-definition token (net48 arrays and closed generics
        // carry their element's assembly).
        static string AssemblySimpleNameOfToken(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind == HandleKind.TypeSpecification)
            {
                var sig = md.GetBlobReader(
                    md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                _ = DecodeDisplayType(md, ref sig, false, out var defTok);
                return AssemblySimpleNameOfToken(md, MetadataTokens.GetToken(defTok));
            }
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                    return ThisAssemblyName(md);
                case HandleKind.TypeReference:
                {
                    var tr = md.GetTypeReference((TypeReferenceHandle)handle);
                    var scope = tr.ResolutionScope;
                    if (scope.Kind == HandleKind.TypeReference)
                        return AssemblySimpleNameOfToken(md, MetadataTokens.GetToken(scope));
                    if (scope.Kind == HandleKind.AssemblyReference)
                        return md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
                    return "mscorlib";
                }
                default:
                    return "mscorlib";
            }
        }

        static string ThisAssemblyName(MetadataReader md)
        {
            try { return md.GetString(md.GetAssemblyDefinition().Name); }
            catch { return ""; }
        }

        // net48 Type.MetadataToken of a typeof token: the ECMA token.
        //   * a TypeDef -> the definition's own TypeDef token (this assembly);
        //   * a TypeRef -> the referenced type's DEFINING assembly TypeDef
        //     token (net48 resolves the type through its AssemblyRef -- e.g.
        //     typeof(int).MetadataToken is mscorlib's System.Int32 TypeDef
        //     row 251 = 0x020000FB, NOT the input assembly's TypeRef row).
        //     This reader has no rows for the referenced assembly, so the
        //     well-known core-library table answers (the net48 mscorlib token
        //     set is fixed -- v4.0.30319); any other TypeRef falls back to the
        //     input-assembly TypeRef token (documented boundary);
        //   * a TypeSpec -> its row (arrays / constructed generics carry no
        //     own TypeDef row).
        static int MetadataTokenOfType(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                    return 0x02000000 | MetadataTokens.GetRowNumber((EntityHandle)handle);
                case HandleKind.TypeReference:
                {
                    var fullName = md.GetFullName((TypeReferenceHandle)handle);
                    if (BclMetadataToken(fullName) is { } bclToken) return bclToken;
                    return 0x01000000 | MetadataTokens.GetRowNumber((EntityHandle)handle);
                }
                case HandleKind.TypeSpecification:
                    return 0x1B000000 | MetadataTokens.GetRowNumber((EntityHandle)handle);
                default:
                    return 0;
            }
        }

        // The well-known mscorlib (v4.0.30319) TypeDef tokens for the core
        // library types the net48 probes reflect. This reader cannot resolve a
        // TypeRef's defining-assembly TypeDef row (it only holds the input
        // assembly's metadata), so the fixed mscorlib token set is carried
        // here -- the same curated-core-table convention as BclTypeAttributeFlag
        // below. A miss returns null (the caller keeps the TypeRef token).
        static int? BclMetadataToken(string fullName)
        {
            switch (fullName)
            {
                case "System.Object": return 0x0200003d;
                case "System.Action`1": return 0x02000040;
                case "System.Action": return 0x02000041;
                case "System.Func`1": return 0x02000045;
                case "System.Func`2": return 0x02000046;
                case "System.Array": return 0x02000055;
                case "System.IComparable": return 0x02000058;
                case "System.IComparable`1": return 0x02000059;
                case "System.IEquatable`1": return 0x0200005a;
                case "System.String": return 0x02000073;
                case "System.Exception": return 0x0200007c;
                case "System.DateTime": return 0x0200007d;
                case "System.SystemException": return 0x02000080;
                case "System.Delegate": return 0x02000085;
                case "System.MulticastDelegate": return 0x02000087;
                case "System.ArgumentException": return 0x020000a6;
                case "System.ArgumentNullException": return 0x020000a7;
                case "System.ArgumentOutOfRangeException": return 0x020000a8;
                case "System.Attribute": return 0x020000ad;
                case "System.AttributeUsageAttribute": return 0x020000af;
                case "System.Boolean": return 0x020000b2;
                case "System.Byte": return 0x020000b4;
                case "System.Char": return 0x020000b6;
                case "System.DayOfWeek": return 0x020000d1;
                case "System.Decimal": return 0x020000d3;
                case "System.Double": return 0x020000d7;
                case "System.Enum": return 0x020000da;
                case "System.EventHandler": return 0x020000e0;
                case "System.FlagsAttribute": return 0x020000e3;
                case "System.Guid": return 0x020000eb;
                case "System.IDisposable": return 0x020000ee;
                case "System.IFormattable": return 0x020000f0;
                case "System.Int16": return 0x020000fa;
                case "System.Int32": return 0x020000fb;
                case "System.Int64": return 0x020000fc;
                case "System.IntPtr": return 0x020000fd;
                case "System.IConvertible": return 0x02000103;
                case "System.Math": return 0x0200010b;
                case "System.ObsoleteAttribute": return 0x0200011a;
                case "System.Random": return 0x02000126;
                case "System.SByte": return 0x0200013c;
                case "System.SerializableAttribute": return 0x0200013d;
                case "System.Single": return 0x0200013f;
                case "System.TimeSpan": return 0x02000143;
                case "System.Type": return 0x02000148;
                case "System.UInt16": return 0x02000150;
                case "System.UInt32": return 0x02000151;
                case "System.UInt64": return 0x02000152;
                case "System.UIntPtr": return 0x02000153;
                case "System.ValueType": return 0x02000159;
                case "System.Void": return 0x0200015c;
                case "System.IO.MemoryStream": return 0x02000199;
                case "System.IO.Stream": return 0x020001a1;
                case "System.IO.TextReader": return 0x020001a6;
                case "System.IO.TextWriter": return 0x020001a7;
                case "System.Globalization.CultureInfo": return 0x020003a9;
                case "System.Globalization.NumberFormatInfo": return 0x020003d9;
                case "System.Collections.IEnumerable": return 0x0200049e;
                case "System.Collections.Generic.Dictionary`2": return 0x020004bf;
                case "System.Collections.Generic.ICollection`1": return 0x020004d0;
                case "System.Collections.Generic.IComparer`1": return 0x020004d1;
                case "System.Collections.Generic.IEnumerable`1": return 0x020004d3;
                case "System.Collections.Generic.IEnumerator`1": return 0x020004d4;
                case "System.Collections.Generic.IEqualityComparer`1": return 0x020004d5;
                case "System.Collections.Generic.IList`1": return 0x020004d6;
                case "System.Collections.Generic.List`1": return 0x020004dc;
                case "System.Threading.Thread": return 0x02000516;
                case "System.Reflection.Assembly": return 0x020005b1;
                case "System.Reflection.AssemblyName": return 0x020005c7;
                case "System.Reflection.ConstructorInfo": return 0x020005d2;
                case "System.Reflection.FieldInfo": return 0x020005e5;
                case "System.Reflection.MemberInfo": return 0x02000601;
                case "System.Reflection.MethodInfo": return 0x02000608;
                case "System.Reflection.PropertyInfo": return 0x0200061b;
                case "System.Text.StringBuilder": return 0x02000a58;
                default: return null;
            }
        }

        // refl2 (A): whether the call token is Type::get_AssemblyQualifiedName.
        static bool IsTypeGetAssemblyQualifiedName(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            return md.GetFullName((TypeReferenceHandle)mr.Parent) == "System.Type"
                && md.GetString(mr.Name) == "get_AssemblyQualifiedName";
        }

        // typeof-chain (F1 follow-up): whether the call token is Type::
        // GetTypeCode's STATIC 1-param form (GetTypeCode(Type)) -- only that
        // form folds to a typecode constant; the 0-param INSTANCE form
        // (IConvertible implementation) stays LOUD.
        static bool IsTypeGetTypeCode(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            if (md.GetFullName((TypeReferenceHandle)mr.Parent) != "System.Type") return false;
            if (md.GetString(mr.Name) != "GetTypeCode") return false;
            var sig = md.GetBlobReader(mr.Signature);
            sig.ReadSignatureHeader();
            return sig.ReadCompressedInteger() == 1;
        }

        // typeof-chain (RF-N4): whether the call token is Type::
        // get_IsGenericTypeDefinition -- the fold-time generic-definition
        // predicate. Roslyn emits it with parent System.Type (a bool getter).
        static bool IsTypeGetIsGenericTypeDefinition(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            return md.GetFullName((TypeReferenceHandle)mr.Parent) == "System.Type"
                && md.GetString(mr.Name) == "get_IsGenericTypeDefinition";
        }

        // RF-N4: whether the ldtoken TYPE token is an OPEN generic DEFINITION
        // (TypeDef/TypeRef with arity > 0). A closed GENERICINST TypeSpec
        // (typeof(Box<int>)) and a typeof(T) VAR both fold to a name string
        // but are NOT definitions -> false. The arity of a TypeReference is
        // read from its "`N" suffix (a cross-assembly ref has no GenericParam
        // rows in this reader).
        static bool IsOpenGenericDefToken(MetadataReader md, int token)
        {
            var t = MetadataTokens.Handle(token);
            switch (t.Kind)
            {
                case HandleKind.TypeDefinition:
                    return md.GetTypeDefinition((TypeDefinitionHandle)t).GetGenericParameters().Count > 0;
                case HandleKind.TypeReference:
                    return GenericArityFromName(md.GetFullName((TypeReferenceHandle)t)) > 0;
                default:
                    return false; // TypeSpec (GENERICINST instance / VAR), other kinds
            }
        }

        // REFL6B (item B5): Type.get_ContainsGenericParameters on the FOLDED
        // typeof chain -- whether the ldtoken TYPE token "contains generic
        // parameters" (net48 semantics, p-refl6-mprops oracle):
        //   * a TypeDef/TypeRef that is an open generic definition -- arity > 0
        //     (own or any DECLARING-chain type -- typeof(Outer<>.Inner) where
        //     Outer<> is open -> net48 True because the outer's parameter is
        //     unassigned);
        //   * a closed GENERICINST TypeSpec (typeof(Box<int>)) -> False;
        //   * a nested-in-closed (typeof(Outer<int>.Inner)) -> False;
        //   * a non-generic type / SZARRAY (typeof(int[])) -> False.
        // Mirrors IsOpenGenericDefToken's token-shape split but walks the
        // declaring chain for the nested-in-open case.
        static bool ContainsGenericParamsToken(MetadataReader md, int token)
        {
            var t = MetadataTokens.Handle(token);
            switch (t.Kind)
            {
                case HandleKind.TypeDefinition:
                    for (var cur = (TypeDefinitionHandle)t; !cur.IsNil; )
                    {
                        if (md.GetTypeDefinition(cur).GetGenericParameters().Count > 0) return true;
                        cur = md.GetTypeDefinition(cur).GetDeclaringType();
                    }
                    return false;
                case HandleKind.TypeReference:
                    return GenericArityFromName(md.GetFullName((TypeReferenceHandle)t)) > 0;
                case HandleKind.TypeSpecification:
                    // typeof(Box<int>) -> closed GENERICINST -> False;
                    // typeof(Outer<>.Inner) (a GENERICINST whose argument is the
                    // open type parameter VAR !0) -> True (net48 nested-in-open).
                    var sig = md.GetBlobReader(
                        md.GetTypeSpecification((TypeSpecificationHandle)t).Signature);
                    var hdr = sig.ReadByte();
                    if (hdr != 0x15) return false; // SZARRAY/ARRAY/PTR/other -> closed
                    _ = sig.ReadByte();            // ELEMENT_TYPE_CLASS / VALUETYPE
                    _ = sig.ReadTypeHandle();      // the generic type (def or nested)
                    var argc = sig.ReadCompressedInteger();
                    for (var i = 0; i < argc; i++)
                    {
                        var et = sig.ReadByte();
                        // VAR (0x13) / MVAR (0x1E) = an UNASSIGNED type parameter
                        // -> the instantiation still contains generic parameters.
                        if (et is 0x13 or 0x1E) return true;
                        // Closed CLASS/VALUETYPE args carry a TypeDefOrRef token.
                        if (et is 0x11 or 0x12)
                        {
                            _ = sig.ReadTypeHandle();
                            continue;
                        }
                        // Any other (non-nameful) arg shape: conservatively NOT
                        // open (primitives/string = closed). A nested open
                        // GENERICINST serving as an argument would need a deeper
                        // walk, but the probe shapes never nest there.
                        return false;
                    }
                    return false;
                default:
                    return false;
            }
        }

        // REFL6B (item B5): whether the call token is Type::
        // get_ContainsGenericParameters -- the fold-time open-generic-parameter
        // predicate. Mirrors IsTypeGetIsGenericTypeDefinition.
        static bool IsTypeGetContainsGenericParameters(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            return md.GetFullName((TypeReferenceHandle)mr.Parent) == "System.Type"
                && md.GetString(mr.Name) == "get_ContainsGenericParameters";
        }

        // GN-18 P2/D1 (S3): whether the call token is Type::GetGenericArguments
        // (the 0-param instance overload -- the (Type[]) overload does not
        // exist on System.Type; the param count is asserted to catch a
        // wrongly-shaped signature). Mirrors IsTypeGetIsGenericTypeDefinition.
        static bool IsTypeGetGenericArguments(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            if (md.GetFullName((TypeReferenceHandle)mr.Parent) != "System.Type") return false;
            if (md.GetString(mr.Name) != "GetGenericArguments") return false;
            var sig = md.GetBlobReader(mr.Signature);
            sig.ReadSignatureHeader();
            return sig.ReadCompressedInteger() == 0;
        }

        // GN-18 P2/D1 (S3): the compile-time VALUE of an ldc.i4 instruction
        // across the whole ECMA-335 immediate family (ldc.i4.m1 / ldc.i4.0 ..
        // ldc.i4.8 / ldc.i4.s / ldc.i4), or null for any other opcode. The
        // element fold needs the index BY VALUE: csc emits `arr[0]` as
        // ldc.i4.0, a distinct opcode from ldc.i4 -- an opcode-name match on
        // Ldc_i4/Ldc_i4_s never sees it (the observed "[i] not folded"
        // defect). A null result means "not a compile-time constant index" and
        // the caller keeps the LOUD guard.
        static int? LdcI4ConstValue(IlInstruction ins) => ins.OpCode switch
        {
            ILOpCode.Ldc_i4_m1 => -1,
            ILOpCode.Ldc_i4_0 => 0,
            ILOpCode.Ldc_i4_1 => 1,
            ILOpCode.Ldc_i4_2 => 2,
            ILOpCode.Ldc_i4_3 => 3,
            ILOpCode.Ldc_i4_4 => 4,
            ILOpCode.Ldc_i4_5 => 5,
            ILOpCode.Ldc_i4_6 => 6,
            ILOpCode.Ldc_i4_7 => 7,
            ILOpCode.Ldc_i4_8 => 8,
            ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 => ins.Operand is { } v ? (int)v : null,
            _ => null,
        };

        // GN-18 P2/D1 (S3): decode the ARGUMENT COUNT of a closed GENERICINST
        // TypeSpec (0x15). Returns -1 for any other shape (open definition /
        // VAR / non-GENERICINST), which the caller treats as "not foldable" --
        // the arity is read from the blob itself, never guessed from the
        // definition name (an arity-N definition can hold an arity-M instance
        // only for M == N, so the count and the name agree, but the blob is the
        // authority). ELEMENT_TYPE_CLASS / VALUETYPE precedes the type handle
        // and does not change the count.
        static int GenericInstArity(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.TypeSpecification) return -1;
            var sig = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
            if (sig.ReadByte() != 0x15) return -1;
            _ = sig.ReadByte();                 // ELEMENT_TYPE_CLASS / VALUETYPE
            _ = sig.ReadTypeHandle();           // the definition token
            return sig.ReadCompressedInteger(); // the argument count
        }

        // GN-18 P2/D1 (S3): the MANAGED FULL NAME of each argument of a closed
        // GENERICINST TypeSpec (0x15), in declaration order. Each argument is
        // itself an encoded element type; only primitives / well-known
        // System.* TypeDefs and TypeRefs resolve here (the suffixed mapping of
        // the emit-side constants is deliberately NOT used). Returns null when
        // any argument is not resolvable (VAR / MVAR / nested generic /
        // ARRAY / SZARRAY / PTR / BYREF ...) -- the caller then keeps the
        // runtime LOUD path instead of fabricating a name.
        static List<string>? GenericInstArgNames(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.TypeSpecification) return null;
            var sig = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
            if (sig.ReadByte() != 0x15) return null;
            _ = sig.ReadByte();          // ELEMENT_TYPE_CLASS / VALUETYPE
            _ = sig.ReadTypeHandle();    // the definition token
            var argc = sig.ReadCompressedInteger();
            var names = new List<string>(argc);
            for (var i = 0; i < argc; i++)
            {
                var name = GenericArgName(md, ref sig);
                if (name == null) return null;
                names.Add(name);
            }
            return names;
        }

        // One encoded argument type -> its managed full name, or null when the
        // element type is outside the decode table (the caller LOUDs).
        static string? GenericArgName(MetadataReader md, ref BlobReader sig)
        {
            var et = sig.ReadByte();
            switch (et)
            {
                case 0x01: return "System.Void";
                case 0x02: return "System.Boolean";
                case 0x03: return "System.Char";
                case 0x04: return "System.SByte";
                case 0x05: return "System.Byte";
                case 0x06: return "System.Int16";
                case 0x07: return "System.UInt16";
                case 0x08: return "System.Int32";
                case 0x09: return "System.UInt32";
                case 0x0A: return "System.Int64";
                case 0x0B: return "System.UInt64";
                case 0x0C: return "System.Single";
                case 0x0D: return "System.Double";
                // ELEMENT_TYPE_STRING (0x0E) per ECMA-335 / this repo's own
                // coding ("string" in MethodRefResolver's signature table).
                case 0x0E: return "System.String";
                case 0x18: return "System.IntPtr";
                case 0x19: return "System.UIntPtr";
                case 0x1C: return "System.Object";
                // PTR (0x0F) / SZARRAY (0x1D) / TypedReference (0x16) and every
                // other shape have NO managed full name in this decode table
                // (an array argument is the emit-side suffix channel, which is
                // forbidden here) -- null keeps the LOUD path instead of a
                // fabricated name.
                // CLASS / VALUETYPE: a compressed TypeDefOrRef coded token.
                case 0x11:
                case 0x12:
                    var tok = sig.ReadTypeHandle();
                    return tok.Kind switch
                    {
                        HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)tok),
                        HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)tok),
                        // A nested instance / definition token -- not supported
                        // (the emit-side suffix would be needed).
                        _ => null,
                    };
                default:
                    return null; // PTR/SZARRAY/TYPEDREF/VAR/MVAR/ARRAY/BYREF/GENERICINST
            }
        }


        // "Cases.Box`1" -> 1, "Cases.Box`2" -> 2, "System.Int32" -> 0.
        static int GenericArityFromName(string name)
        {
            var tick = name.IndexOf('`');
            if (tick < 0 || tick + 1 >= name.Length) return 0;
            var n = 0;
            for (var i = tick + 1; i < name.Length && char.IsDigit(name[i]); i++)
                n = n * 10 + (name[i] - '0');
            return n;
        }

        // typeof-chain (F1 follow-up): whether the call token is
        // System.Console::WriteLine -- the trusted consumer of the arg0Type
        // string hint (String.Concat(object,object) and the other arg0Type
        // consumers must NOT receive the hint).
        static bool IsConsoleWriteLine(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeReference) return false;
            return md.GetFullName((TypeReferenceHandle)mr.Parent) == "System.Console"
                && md.GetString(mr.Name) == "WriteLine";
        }

        //--------------------------------------------------------------------------
        // impl-sb (B/D): the span-carrier call target decode. Returns
        // (member name, owner name) for the tokens the interception gates on:
        //   * the MethodSpecs on the compiler-synthesized
        //     <PrivateImplementationDetails> helpers (the owner is the bare
        //     metadata name -- the module-level synthetic type);
        //   * the MemberRefs whose parent is a TypeReference OR a TypeSpec
        //     generic instance (Span`1<char>::op_Implicit -- the owner
        //     normalizes to the generic DEFINITION name, the same rule
        //     ResolveOwnerFullName applies).
        // Everything else answers null (the normal resolution path).
        //--------------------------------------------------------------------------
        static (string Name, string Owner)? SpanCarrierCallTarget(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind == HandleKind.MethodSpecification)
            {
                var ms = md.GetMethodSpecification((MethodSpecificationHandle)handle);
                if (ms.Method.Kind != HandleKind.MethodDefinition) return null;
                var m = md.GetMethodDefinition((MethodDefinitionHandle)ms.Method);
                return (md.GetString(m.Name), md.GetString(md.GetTypeDefinition(m.GetDeclaringType()).Name));
            }
            if (handle.Kind != HandleKind.MemberReference) return null;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            var name = md.GetString(mr.Name);
            string? owner;
            switch (mr.Parent.Kind)
            {
                case HandleKind.TypeReference:
                    owner = md.GetFullName((TypeReferenceHandle)mr.Parent);
                    break;
                case HandleKind.TypeSpecification:
                {
                    var spec = md.GetTypeSpecification((TypeSpecificationHandle)mr.Parent);
                    var sig = md.GetBlobReader(spec.Signature);
                    if (sig.ReadByte() != 0x15) return null;
                    // The GENERICINST header is followed by the UNDERLYING type
                    // marker (0x11 VALUETYPE / 0x12 CLASS) BEFORE the TypeDefOrRef
                    // coded index -- not consuming it made ReadTypeHandle decode
                    // the MARKER as the coded index, so every TypeSpec-parent
                    // span call (ReadOnlySpan`1<char>::op_Implicit) resolved a
                    // garbage owner and the char[] carrier gate never matched
                    // (P3-1閳?e2e red: "op_Implicit not wired in M5").
                    _ = sig.ReadByte();
                    var th = sig.ReadTypeHandle();
                    owner = th.Kind switch
                    {
                        HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)th),
                        HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)th),
                        _ => null,
                    };
                    break;
                }
                default:
                    return null;
            }
            return owner == null ? null : (name, owner);
        }

        // The identity-carrier gate: the call signature must be EXACTLY one of
        // paramTypesShapes (a sliced AsSpan(string,int,int) or a foreign
        // op_Implicit overload must not ride the identity -- LOUD default
        // instead). The shapes are an ANY-OF ALTERNATIVE list: the JOINED
        // ParamTypes string already pins the whole parameter sequence (each
        // parameter renders one segment), so the old
        // ParamCount==paramTypesShapes.Length precondition compared the CALL's
        // parameter count against the ALTERNATIVE COUNT -- every multi-shape
        // gate with a 1-parameter call was structurally dead (P3-1閳?e2e red:
        // the char[] op_Implicit carrier never matched either shape).
        static bool SpanCarrierIdentitySig(MetadataReader md, int token, params string[] paramTypesShapes)
        {
            var handle = MetadataTokens.Handle(token);
            BlobHandle sigHandle;
            if (handle.Kind == HandleKind.MemberReference)
                sigHandle = md.GetMemberReference((MemberReferenceHandle)handle).Signature;
            else if (handle.Kind == HandleKind.MethodSpecification)
                sigHandle = md.GetMemberReference((MemberReferenceHandle)md.GetMethodSpecification((MethodSpecificationHandle)handle).Method).Signature;
            else return false;
            var decoded = MethodSignatureCoder.Decode(md, sigHandle);
            return paramTypesShapes.Contains(decoded.ParamTypes);
        }

        // The System.Object TypeRef token (the inline-array materialization's
        // element class). Every assembly the span lowering compiles into
        // references System.Object; a miss is a LOUD whitelist (never a
        // fabricated token).
        static int? SystemObjectTypeToken(MetadataReader md)
        {
            foreach (var trh in md.TypeReferences)
            {
                var tr = md.GetTypeReference(trh);
                if (md.GetString(tr.Namespace) == "System" && md.GetString(tr.Name) == "Object"
                    && tr.ResolutionScope.Kind != HandleKind.TypeReference)
                    return MetadataTokens.GetToken(trh);
            }
            return null;
        }

        // "__iae_<local>_<index>" -> (local, index). The local names are the
        // emitter's "loc<N>" shapes (no underscores), so the LAST '_' splits
        // the index; a foreign marker shape answers false (the stind keeps
        // its LOUD default).
        static bool TryParseInlineArrayMarker(string function, out string local, out int index)
        {
            local = "";
            index = -1;
            var body = function["__iae_".Length..];
            var split = body.LastIndexOf('_');
            if (split <= 0 || split == body.Length - 1) return false;
            local = body[..split];
            return int.TryParse(body[(split + 1)..], out index);
        }

        // Type full name -> short name (net48 semantics of typeof(X).Name):
        // the segment after the last '.' AND the last '+' (nested types
        // carry the declaring chain as "Outer+Inner" -- Name is "Inner").
        static string ShortTypeName(string fullName)
        {
            var dot = fullName.LastIndexOf('.');
            var seg = dot < 0 ? fullName : fullName[(dot + 1)..];
            var plus = seg.LastIndexOf('+');
            return plus < 0 ? seg : seg[(plus + 1)..];
        }

        //--------------------------------------------------------------------------
        // refl2 (A): net48 type-DISPLAY names on the typeof fold chain.
        //
        // The folded identity string is the typeinfo REGISTRY key (a simplified
        // model carrier: arrays are "[<elem>", a constructed generic is its
        // DEFINITION's name). net48's Type.Name / FullName /
        // AssemblyQualifiedName are DIFFERENT strings, and the ldtoken token
        // recorded beside the identity (typeFromHandleToken) decodes them
        // exactly:
        //   typeof(int[]).FullName      -> "System.Int32[]"
        //   typeof(int[,]).FullName     -> "System.Int32[,]"
        //   typeof(int?).FullName       -> "System.Nullable`1[[System.Int32,
        //                                   mscorlib, Version=4.0.0.0,
        //                                   Culture=neutral,
        //                                   PublicKeyToken=b77a5c561934e089]]"
        //   typeof(int[][]).FullName    -> "System.Int32[][]"
        //   typeof(int[]).Name          -> "Int32[]"
        //   typeof(List<int>).Name      -> "List`1"
        //   typeof(Dictionary<string,int>).FullName -> arity-paired,
        //                                   assembly-qualified argument list.
        // Generic arguments are themselves assembly-qualified in the FullName
        // form; the Name form keeps only the definition's short name.
        //--------------------------------------------------------------------------
        static string ReflectionDisplayName(MetadataReader md, int token, bool qualify)
        {
            var handle = MetadataTokens.Handle(token);
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                    return md.GetFullName((TypeDefinitionHandle)handle);
                case HandleKind.TypeReference:
                    return md.GetFullName((TypeReferenceHandle)handle);
                case HandleKind.TypeSpecification:
                {
                    var sig = md.GetBlobReader(
                        md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                    return DecodeDisplayType(md, ref sig, qualify, out _);
                }
                default:
                    throw new NotSupportedException(
                        $"typeof display name: token kind {handle.Kind} not supported in refl2");
            }
        }

        // net48 Type.Name of a typeof token: the short name of the DISPLAY name
        // (arrays keep their "[]"/"[,]" suffix, generics their "`N" arity).
        static string ReflectionDisplayShortName(MetadataReader md, int token)
        {
            var full = ReflectionDisplayName(md, token, false);
            return ShortTypeName(full);
        }

        // One encoded type in a TypeSpec blob -> its net48 display name.
        // `defTok` reports the type's DEFINING TypeDef/TypeRef handle (arrays /
        // pointers / byrefs report their ELEMENT's handle) so the caller can
        // append the net48 assembly identity.
        static string DecodeDisplayType(MetadataReader md, ref BlobReader sig, bool qualify,
            out EntityHandle defTok)
        {
            var et = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
            switch (et)
            {
                case 0x01: defTok = default; return "System.Void";
                case 0x02: defTok = default; return "System.Boolean";
                case 0x03: defTok = default; return "System.Char";
                case 0x04: defTok = default; return "System.SByte";
                case 0x05: defTok = default; return "System.Byte";
                case 0x06: defTok = default; return "System.Int16";
                case 0x07: defTok = default; return "System.UInt16";
                case 0x08: defTok = default; return "System.Int32";
                case 0x09: defTok = default; return "System.UInt32";
                case 0x0A: defTok = default; return "System.Int64";
                case 0x0B: defTok = default; return "System.UInt64";
                case 0x0C: defTok = default; return "System.Single";
                case 0x0D: defTok = default; return "System.Double";
                case 0x0E: defTok = default; return "System.String";
                case 0x18: defTok = default; return "System.IntPtr";
                case 0x19: defTok = default; return "System.UIntPtr";
                case 0x1C: defTok = default; return "System.Object";
                case 0x11: // VALUETYPE
                case 0x12: // CLASS
                {
                    var tok = sig.ReadTypeHandle();
                    defTok = tok;
                    return tok.Kind switch
                    {
                        HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)tok),
                        HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)tok),
                        HandleKind.TypeSpecification => TypeNameForReflection(
                            md, MetadataTokens.GetToken(tok)),
                        _ => throw new NotSupportedException(
                            $"typeof display name: definition kind {tok.Kind} in refl2"),
                    };
                }
                case 0x1D: // SZARRAY
                {
                    var elem = DecodeDisplayType(md, ref sig, qualify, out defTok);
                    return elem + "[]";
                }
                case 0x14: // ARRAY (multidim)
                {
                    var elem = DecodeDisplayType(md, ref sig, qualify, out defTok);
                    var rank = sig.ReadCompressedInteger();
                    var numSizes = sig.ReadCompressedInteger();
                    for (var i = 0; i < numSizes; i++) _ = sig.ReadCompressedInteger();
                    var numLoBounds = sig.ReadCompressedInteger();
                    for (var i = 0; i < numLoBounds; i++) _ = sig.ReadCompressedInteger();
                    return elem + "[" + new string(',', Math.Max(rank, 1) - 1) + "]";
                }
                case 0x0F: // PTR
                {
                    var elem = DecodeDisplayType(md, ref sig, qualify, out defTok);
                    return elem + "*";
                }
                case 0x10: // BYREF
                {
                    var elem = DecodeDisplayType(md, ref sig, qualify, out defTok);
                    return elem + "&";
                }
                case 0x13: // VAR (generic type parameter)
                {
                    var vIdx = sig.ReadCompressedInteger();
                    defTok = default;
                    var hint = MethodRefResolver.InstantiationSuffixHint;
                    if (hint != null)
                    {
                        var part = MethodRefResolver.InstantiationSuffixHintParts is { Length: > 1 }
                            ? MethodRefResolver.InstantiationVarSuffix(vIdx)
                            : hint.Split('_') is { } pp && vIdx < pp.Length ? pp[vIdx] : null;
                        if (part != null) return SuffixToManagedTypeName(part);
                    }
                    return "System.Int32"; // the M3 single-instantiation default
                }
                case 0x15: // GENERICINST
                {
                    _ = sig.ReadByte(); // ELEMENT_TYPE_CLASS / VALUETYPE marker
                    var defHandle = sig.ReadTypeHandle();
                    defTok = defHandle;
                    var defName = defHandle.Kind switch
                    {
                        HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
                        HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
                        HandleKind.TypeSpecification => TypeNameForReflection(
                            md, MetadataTokens.GetToken(defHandle)),
                        _ => throw new NotSupportedException(
                            $"typeof display name: generic definition kind {defHandle.Kind}"),
                    };
                    var argc = sig.ReadCompressedInteger();
                    if (!qualify)
                    {
                        // Name form: consume the argument blobs without rendering.
                        for (var i = 0; i < argc; i++)
                            _ = DecodeDisplayType(md, ref sig, false, out _);
                        return defName;
                    }
                    var args = new List<string>(argc);
                    for (var i = 0; i < argc; i++)
                    {
                        var arg = DecodeDisplayType(md, ref sig, true, out var argTok);
                        args.Add("[" + arg + AssemblySuffixOfToken(md, argTok) + "]");
                    }
                    return defName + "[" + string.Join(",", args) + "]";
                }
                default:
                    throw new NotSupportedException(
                        $"typeof display name: signature byte 0x{et:X2} not supported in refl2");
            }
        }

        static string BclAssemblySuffix() =>
            ", mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";


        // net48 assembly identity suffix (", <name>, Version=<v>, Culture=
        // neutral, PublicKeyToken=<tok|null>") of a defining type token.
        // A TypeDef -> THIS assembly; a TypeRef -> its AssemblyReference (or
        // mscorlib for the core library's self-reference-less scope); a
        // TypeSpec -> its first defining token (arrays take the element).
        static string AssemblySuffixOfToken(MetadataReader md, EntityHandle tok)
        {
            switch (tok.Kind)
            {
                case HandleKind.TypeDefinition:
                    return ThisAssemblySuffix(md);
                case HandleKind.TypeReference:
                {
                    var tr = md.GetTypeReference((TypeReferenceHandle)tok);
                    var scope = tr.ResolutionScope;
                    if (scope.Kind == HandleKind.TypeReference)
                        return AssemblySuffixOfToken(md, scope);
                    if (scope.Kind == HandleKind.AssemblyReference)
                    {
                        var ar = md.GetAssemblyReference((AssemblyReferenceHandle)scope);
                        var name = md.GetString(ar.Name);
                        if (name == "mscorlib") return BclAssemblySuffix();
                        var tokStr = ar.PublicKeyOrToken.IsNil
                            ? "null"
                            : PublicKeyTokenHex(md, ar.PublicKeyOrToken);
                        return ", " + name + ", Version=" + ar.Version
                            + ", Culture=neutral, PublicKeyToken=" + tokStr;
                    }
                    return BclAssemblySuffix();
                }
                default:
                    return BclAssemblySuffix();
            }
        }

        static string ThisAssemblySuffix(MetadataReader md)
        {
            string name = "";
            string ver = "1.0.0.0";
            try
            {
                var ad = md.GetAssemblyDefinition();
                name = md.GetString(ad.Name);
                ver = ad.Version.ToString();
            }
            catch { }
            return ", " + name + ", Version=" + ver
                + ", Culture=neutral, PublicKeyToken=" + (AssemblyPublicKeyToken(md) ?? "null");
        }

        // The public-key TOKEN of this assembly (net48 prints the token, not the
        // key). The single-assembly test inputs are unsigned -> "null"; for a
        // signed input the SHA-1 token's last 8 bytes are the ECMA layout.
        static string? AssemblyPublicKeyToken(MetadataReader md)
        {
            try
            {
                var pk = md.GetAssemblyDefinition().PublicKey;
                if (pk.IsNil) return null;
                var bytes = md.GetBlobBytes(pk);
                if (bytes.Length < 8) return null;
                using var sha1 = System.Security.Cryptography.SHA1.Create();
                var hash = sha1.ComputeHash(bytes);
                var sb = new System.Text.StringBuilder(16);
                for (var i = hash.Length - 1; i >= hash.Length - 8; i--) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
            catch { return null; }
        }

        static string PublicKeyTokenHex(MetadataReader md, BlobHandle token)
        {
            var bytes = md.GetBlobBytes(token);
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            for (var i = bytes.Length - 1; i >= 0; i--) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        // The net48 assembly suffix of a typeof token.
        static string AssemblySuffix(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind == HandleKind.TypeSpecification)
            {
                // The assembly comes from the ELEMENT / generic definition, the
                // same rule net48 uses for an array / pointer / byref / closed
                // generic type.
                var sig = md.GetBlobReader(
                    md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                _ = DecodeDisplayType(md, ref sig, false, out var defTok);
                return AssemblySuffixOfToken(md, defTok);
            }
            return AssemblySuffixOfToken(md, (EntityHandle)handle);
        }
        // Type full name -> TypeCode value: the net48 constant table, a mirror
        // of the runtime's icall_object_get_type_code (typecheck.cpp 181-215)
        // -- Int32=9 Boolean=3 Char=4 Double=14 String=18, else 1
        // (TypeCode.Object). Matches the runtime table entry-for-entry
        // (Single=13 and other net48 values are outside the runtime's M4 scope;
        // both paths fall back to 1 -- keeping consistency).
        //
        // REFL4 (item 4/B7): a user ENUM type (BaseTypeName == "System.Enum")
        // folds to its UNDERLYING TypeCode (net48: typeof(enum) -> the value__
        // field's TypeCode -- Byte=6 Int16=7 Int32=9 Int64=11, etc.), NOT the
        // pre-refl4 Object(1) fallback. The underlying width comes from the
        // compiler-generated `value__` instance field's type (the same surface
        // MethodMap.Enum.TryGetUserEnumRegisterInfo reads). A BCL enum
        // (TypeRef -- System.DayOfWeek etc.) is NOT a TypeDef, so no value__
        // field is visible; net48 uses the metadata underlying type, which the
        // transpiler does not decode for TypeRefs -- those keep the Object(1)
        // fallback (W7: only user enums are folded; the probes use user enums).
        static int TypeCodeOfTypeName(string fullName, IReadOnlyList<TypeDef>? types) => fullName switch
        {
            "System.Int32" => 9,
            "System.Boolean" => 3,
            "System.Char" => 4,
            "System.Double" => 14,
            "System.String" => 18,
            // decimal-domain T3: net48 TypeCode.Decimal == 15 (mirrors the
            // typecheck.cpp/typeext.cpp table entries -- keep all three in sync).
            "System.Decimal" => 15,
            _ => ForecastTypeCodeOfEnum(fullName, types),
        };

        // REFL4 (item 4/B7): the enum-underlying TypeCode for a USER enum
        // TypeDef (raw metadata value__ width -> the net48 TypeCode; SByte=5
        // Byte=6 Int16=7 UInt16=8 Int32=9 UInt32=10 Int64=11 UInt64=12).
        // Non-enum / unregistered / BCL-TypeRef names fall back to 1 (Object).
        static int ForecastTypeCodeOfEnum(string fullName, IReadOnlyList<TypeDef>? types)
        {
            var def = types?.FirstOrDefault(t => t.MatchesFullName(fullName));
            if (def == null || def.BaseTypeName != "System.Enum") return 1;
            var valueField = def.InstanceFields.FirstOrDefault(f => f.Name == "value__");
            return valueField?.FieldTypeName switch
            {
                "System.SByte" => 5,
                "System.Byte" => 6,
                "System.Int16" => 7,
                "System.UInt16" => 8,
                "System.Int32" => 9,
                "System.UInt32" => 10,
                "System.Int64" => 11,
                "System.UInt64" => 12,
                _ => 9, // the M3 default underlying int32 (like MethodMap.Enum.cs)
            };
        }

        // Whether the full type name is a delegate: generic BCL delegates by
        // name prefix (Func`N / Action`N, incl. the generic instance name
        // "System.Func`2_int_int"); user types via the TypeDef base-class chain
        // (BaseTypeName reaching System.MulticastDelegate / System.Delegate).
        static bool IsDelegateType(string? fullName, IReadOnlyList<TypeDef>? types)
        {
            if (fullName == null) return false;
            if (fullName.StartsWith("System.Func`", StringComparison.Ordinal)
                || fullName.StartsWith("System.Action`", StringComparison.Ordinal)
                // threadobj domain: the BCL ThreadStart delegate (new Thread
                // wiring) is constructed through the same il2cpp_delegate_new
                // path as Func`/Action` (threadcase e2e).
                || fullName == "System.Threading.ThreadStart"
                // threadpool domain: the BCL WaitCallback delegate
                // (ThreadPool.QueueUserWorkItem wiring) takes the same
                // il2cpp_delegate_new path (threadpoolcase e2e).
                || fullName == "System.Threading.WaitCallback"
                // batch 158 (timercase): the BCL TimerCallback delegate
                // (System.Threading.Timer wiring) -- same il2cpp_delegate_new
                // path (the ctor unwraps its methodPtr in the runtime).
                || fullName == "System.Threading.TimerCallback"
                // batch 161 (timerscase): the BCL ElapsedEventHandler delegate
                // (System.Timers.Timer's Elapsed event) -- same
                // il2cpp_delegate_new path (the bridge dispatches the
                // 2-ref-arg void ABI).
                || fullName == "System.Timers.ElapsedEventHandler"
                // batch 162 (eventcase): the BCL EventHandler delegates
                // (event backing fields / Raise) -- same il2cpp_delegate_new
                // path (the invoke routes through the 2-ref-arg void
                // multicast icall).
                || fullName == "System.EventHandler"
                || fullName == "System.EventHandler`1"
                // icallwide (arrayext2 predicate/converter domain): the
                // Array.Find*/Exists/TrueForAll/ConvertAll delegate types ride
                // the same il2cpp_delegate_new path (the single-cast static
                // methodPtr ABI the arrayext2 icalls invoke directly).
                || fullName.StartsWith("System.Predicate`", StringComparison.Ordinal)
                || fullName.StartsWith("System.Converter`", StringComparison.Ordinal)
                || fullName.StartsWith("System.Comparison`", StringComparison.Ordinal)
                // P0 batch A (RuntimeHelpers.ExecuteCodeWithGuaranteedCleanup):
                // the CER delegate pair is NESTED in the BCL type
                // (System.Runtime.CompilerServices.RuntimeHelpers+TryCode /
                // +CleanupCode). They are ordinary single-cast delegates
                // (TryCode(object), CleanupCode(object, bool)) the runtime
                // helper invokes directly, so they ride the same
                // il2cpp_delegate_new path -- without this the newobj fell to
                // the reference-alloc path and emitted undefined
                // System_Runtime_CompilerServices_RuntimeHelpers_TryCode_class
                // / __ctor_<hash> symbols.
                || fullName == "System.Runtime.CompilerServices.RuntimeHelpers+TryCode"
                || fullName == "System.Runtime.CompilerServices.RuntimeHelpers+CleanupCode")
                return true;
            if (types == null) return false;
            // Nested-name match: nested TypeDefs carry Namespace="" (FullName is
            // the short name), but metadata queries use the '+' full name
            // ("Cases.Outer+ChainFn") -- MatchesFullName covers both forms, so
            // custom delegates nested in user classes are recognized.
            var cur = types.FirstOrDefault(t => t.MatchesFullName(fullName));
            while (cur != null)
            {
                if (cur.BaseTypeName == "System.MulticastDelegate" || cur.BaseTypeName == "System.Delegate")
                    return true;
                cur = cur.BaseType;
            }
            return false;
        }

        // TypeSpec -> generic definition name ("System.Func`2<int,int>" ->
        // "System.Func`2").
        static string GenericDefinitionNameOf(MetadataReader md, TypeSpecificationHandle handle)
        {
            var spec = md.GetTypeSpecification(handle);
            var sig = md.GetBlobReader(spec.Signature);
            var header = sig.ReadByte();
            if (header != 0x15)
            {
                // G1 (C1): a multidim ARRAY (0x14) TypeSpec owner -- the
                // element- and rank-aware token (the same four-place multidim
                // rule); multidim Get/Set/length MemberRefs resolve against it.
                if (header == 0x14)
                    return ReadM3MultidimArrayArg(md, ref sig);
                throw new NotSupportedException($"TypeSpec signature byte 0x{header:X2} not GenericInst(0x15) in M3");
            }
            var defType = sig.ReadByte();
            _ = defType;
            var defHandle = sig.ReadTypeHandle();
            return defHandle.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
                _ => throw new NotSupportedException($"generic definition kind {defHandle.Kind} in M3"),
            };
        }

        // M5 wiring: TypeDefOrRef token (box/newarr/ldelem element tokens) ->
        // the type's full name. Not a TypeSpec: box/newarr tokens reference
        // TypeDefOrRef directly.
        static string TypeFullNameFromToken(MetadataReader? md, int token)
        {
            if (md == null) return "?";
            var handle = MetadataTokens.Handle(token);
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)handle),
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)handle),
                // TypeSpecification: decode the shape instead of guessing.
                // VAR (0x13, generic type parameter) instantiates to int32
                // under the M3 single-instantiation convention (the proven
                // ldobj-!0 case: <>f__AnonymousType0<T>.ToString compiled for
                // int). Every other shape keeps the legacy TaskAwaiter name
                // (r103 behavior -- initobj's token-kind check only; the
                // emitter re-resolves GenericInst to the legacy Object
                // no-op, so fold/emitter judgments stay paired).
                HandleKind.TypeSpecification => TypeSpecName(md, (TypeSpecificationHandle)handle),
                _ => throw new NotSupportedException($"type token kind {handle.Kind} not supported in M2"),
            };
        }

        // caudit2: `EqualityComparer`1<user T>` member detection. The token's
        // parent is a TypeSpecification whose generic argument is the USER
        // type -- the real identity the generic-instance NAME channel loses
        // (VALUETYPE args collapse to "valuetype"). Returns the member name and
        // the instantiation's generic-argument full name (only for a single
        // VALUETYPE/CLASS argument of EqualityComparer`1).
        static bool TryDecodeUserEqualityComparerMember(MetadataReader md, int token,
            out string memberName, out string? typeArgFullName)
        {
            memberName = "";
            typeArgFullName = null;
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            if (mr.Parent.Kind != HandleKind.TypeSpecification) return false;
            var sig = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)mr.Parent).Signature);
            if (sig.ReadByte() != 0x15) return false; // GENERICINST
            _ = sig.ReadByte(); // CLASS/VALUETYPE marker of the generic definition
            var defHandle = sig.ReadTypeHandle();
            var defName = defHandle.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
                _ => null,
            };
            if (defName != "System.Collections.Generic.EqualityComparer`1") return false;
            if (sig.ReadCompressedInteger() != 1) return false;
            var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
            if (b is not (0x11 or 0x12)) return false;
            var argHandle = sig.ReadTypeHandle();
            typeArgFullName = argHandle.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)argHandle),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)argHandle),
                _ => null,
            };
            if (typeArgFullName == null) return false;
            memberName = md.GetString(mr.Name);
            return true;
        }

        // caudit2: the tracked pseudo-type of the folded
        // EqualityComparer<T>.Default receiver (the get_Default fold pushes it
        // as a typed null marker; the Equals fold matches it back).
        static string UserEqualityComparerPseudoType(string typeArgFullName)
            => "System.Collections.Generic.EqualityComparer`1_" + typeArgFullName;

        // caudit2: does a user method's encoded param list name this value type?
        // ("V:Cases.St" / "B:V:Cases.St" / "r:Cases.St" encodings.)
        static bool UserMethodParamTypeMatches(string paramTypes, string typeFullName)
        {
            var p = paramTypes;
            if (p.StartsWith("B:V:", StringComparison.Ordinal)) p = p[4..];
            else if (p.StartsWith("V:", StringComparison.Ordinal) || p.StartsWith("r:", StringComparison.Ordinal)) p = p[2..];
            return p == typeFullName;
        }

        static string TypeSpecName(MetadataReader md, TypeSpecificationHandle handle)
        {
            var sig = md.GetBlobReader(md.GetTypeSpecification(handle).Signature);
            var header = sig.ReadByte();
            if (header == 0x13)
            {
                _ = sig.ReadCompressedInteger(); // VAR index (single instantiation: T -> int)
                return "System.Int32";
            }
            // GN-2 (Nullable): a `System.Nullable`1<T>` instantiation names
            // itself "System.Nullable`1_<T>" so the constrained-call fold can
            // recognise the boxed-object Nullable receiver (the generic
            // fallback below is the TaskAwaiter legacy default and would make
            // a Nullable receiver look like a Task awaiter).
            if (header == 0x15)
            {
                _ = sig.ReadByte(); // CLASS/VALUETYPE marker
                var d = sig.ReadTypeHandle();
                var dn = d.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)d),
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)d),
                    _ => null,
                };
                if (dn == "System.Nullable`1")
                {
                    var argc = sig.ReadCompressedInteger();
                    var args = new string[argc];
                    for (int i = 0; i < argc; ++i)
                        args[i] = ReadM3Arg(md, ref sig, sig.ReadByte());
                    return dn + "_" + string.Join("_", args);
                }
                // RF-REFL: a ValueTuple instantiation names itself
                // "System.ValueTuple`N_<args>" so the fold's tuple member
                // handling (CompareTo / structural Equals) recognises the
                // boxed-tuple receiver (the TaskAwaiter legacy default made
                // `((IStructuralComparable)ca).CompareTo(...)` fall to the
                // phantom generated-name path -- vtmeta1).
                if (dn != null && ValueTupleSupport.IsValueTupleName(dn))
                    return ValueTupleSupport.ClosedNameFromSpec(md, handle) ?? "System.Object";
            }
            return "System.Runtime.CompilerServices.TaskAwaiter`1";
        }

        // GN-18 B5 (fnptr array element): whether a TYPE TOKEN is an
        // ELEMENT_TYPE_FNPTR (0x1B) TypeSpec -- a `delegate*<...>` used as a
        // newarr element. The element is a NON-reference pointer-sized slot
        // (void* "p" carrier, 8 bytes on x64). The TypeSpecName fallback maps
        // an FNPTR TypeSpec to "TaskAwaiter`1", so classification must read the
        // RAW TypeSpec signature byte here.
        static bool IsFnPtrTypeToken(MetadataReader? md, int token)
        {
            if (md == null) return false;
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.TypeSpecification) return false;
            var sig = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
            return sig.ReadByte() == 0x1B;
        }

        // P-G3/paircase Cmp: resolve a `constrained. !T` marker's concrete
        // receiver type THROUGH the enclosing clone (InstantiationSuffixHint
        // -- the R2b P2 mechanism). A VAR (0x13) TypeSpec in a closed clone
        // body names that clone's generic argument ("string_string" -> VAR:0 =
        // "string" -> System.String), so the interface callvirt on a generic
        // parameter (IComparable::CompareTo on `where T : IComparable`) gets
        // the real concrete hint instead of the M3 "int" default. Outside a
        // body pass the hint is null and the historical Int32 default holds.
        static string ConstrainedRecvFullName(MetadataReader md, int token)
        {
            var h = MetadataTokens.Handle(token);
            if (h.Kind == HandleKind.TypeSpecification)
            {
                var sig = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)h).Signature);
                if (sig.ReadByte() == 0x13)
                {
                    var vIdx = sig.ReadCompressedInteger();
                    var hint = MethodRefResolver.InstantiationSuffixHint;
                    if (hint != null)
                    {
                        // GN-13 fix4: an arity>=2 clone resolves the index
                        // through the per-argument array; arity-1 keeps the
                        // legacy single-suffix split.
                        var part = MethodRefResolver.InstantiationSuffixHintParts is { Length: > 1 }
                            ? MethodRefResolver.InstantiationVarSuffix(vIdx)
                            : hint.Split('_') is { } pp && vIdx < pp.Length ? pp[vIdx] : null;
                        if (part != null)
                            return SuffixToManagedTypeName(part);
                    }
                    return "System.Int32"; // M3 single-instantiation default
                }
                // tuple1 (ValueTuple): a constrained receiver on a C# tuple
                // field (`t8.Rest.ToString()` -- the Rest field is a nested
                // ValueTuple value type) arrives as a TypeSpec of
                // System.ValueTuple`N. The legacy TypeFullNameFromToken
                // fallback names it "TaskAwaiter`1", so the tuple member
                // routing never fired and the call fell through to
                // Object::ToString (a wrong icall on a struct address). Resolve
                // the CLOSED instance name so the tuple helpers bind.
                var vtClosed = ValueTupleSupport.ClosedNameFromSpec(md, (TypeSpecificationHandle)h);
                if (vtClosed != null) return vtClosed;
            }
            return TypeFullNameFromToken(md, token);
        }

        // Map one clone-suffix generic argument (ReadM3GenericArg's short
        // encoding: "int"/"string"/"object"/...) to the managed TYPE FULL NAME
        // (the objTypes receiver-hint form the BCL mappers dispatch on).
        static string SuffixToManagedTypeName(string suffix) => suffix switch
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
            // G1 (C1): an array-argument token "[<elementSuffix>]" decodes to
            // the managed ARRAY type name (recursion maps the element).
            var t when t.Length >= 2 && t[0] == '[' && t[^1] == ']'
                => SuffixToManagedTypeName(t[1..^1]) + "[]",
            _ => suffix, // user class args already carry their full name
        };

        // raudit1: the typeof(T) name resolution. Unlike TypeSpecName (legacy
        // TaskAwaiter`1/Int32 strings consumed by other call sites), a
        // GenericInst resolves to its DEFINITION's full name so the
        // Type::get_Name fold (ShortTypeName) yields the net48 Name:
        //   typeof(List<int>).Name            -> "List`1"
        //   typeof(int?).Name                 -> "Nullable`1"
        //   typeof(Dictionary<string,int>).Name -> "Dictionary`2"
        static string TypeNameForReflection(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.TypeSpecification)
            {
                return TypeFullNameFromToken(md, token);
            }
            var hs = (TypeSpecificationHandle)handle;
            var sig = md.GetBlobReader(md.GetTypeSpecification(hs).Signature);
            var b = sig.ReadByte();
            if (b == 0x13) // VAR (single instantiation: T -> int32)
            {
                var vIdx = sig.ReadCompressedInteger();
                // R1/B1: inside a CLONE body the InstantiationSuffixHint carries
                // the closed argument (CodeGenerator sets it per body pass, see
                // the ConstrainedRecvFullName precedent) -- typeof(T) on a
                // cloned generic argument resolution through the hint instead of
                // the hardcoded Int32. Outside a body (definition / hint null)
                // the historical M3 single-instantiation default holds.
                var hint = MethodRefResolver.InstantiationSuffixHint;
                if (hint != null)
                {
                    // GN-13 fix4: an arity>=2 clone resolves the index through
                    // the per-argument array; arity-1 keeps the legacy
                    // single-suffix split (the array-hint "[int]" token is a
                    // single component with no '_').
                    var part = MethodRefResolver.InstantiationSuffixHintParts is { Length: > 1 }
                        ? MethodRefResolver.InstantiationVarSuffix(vIdx)
                        : hint.Split('_') is { } pp && vIdx < pp.Length ? pp[vIdx] : null;
                    if (part != null)
                        return SuffixToManagedTypeName(part);
                }
                return "System.Int32";
            }
            if (b == 0x15) // GENERICINST
            {
                _ = sig.ReadByte(); // ELEMENT_TYPE_CLASS / VALUETYPE
                var def = sig.ReadTypeHandle();
                return def.Kind switch
                {
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)def),
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)def),
                    // nested generic instantiation (e.g. List<List<int>>):
                    // recurse for the definition chain
                    HandleKind.TypeSpecification => TypeNameForReflection(
                        md, MetadataTokens.GetToken(def)),
                    _ => throw new NotSupportedException(
                        $"typeof TypeSpec definition kind {def.Kind} not supported"),
                };
            }
            // refl2 (predicates): a typeof SZARRAY token (0x1D -- `typeof(int[])`)
            // folds to the ARRAY DESCRIPTOR name ("[<element full name>", the
            // array.cpp convention -- the SAME identity the runtime's
            // il2cpp_class_by_name / GetType() use for an array). The A-class
            // predicates (get_IsArray / GetElementType / ...) operate on that
            // name-string shape ('[' prefix = array). The pre-refl2 fallback
            // produced the bogus "TaskAwaiter`1" (TypeSpecName legacy), which
            // made every array predicate miss.
            if (b == 0x1D) // SZARRAY
            {
                return "[" + SzArrayElementFullName(md, ref sig);
            }
            // refl2 (multidim): a typeof ARRAY (0x14) token (`typeof(int[,])`)
            // folds to the SAME element-prefixed identity the array runtime uses
            // ("[System.Int32"), not the legacy TaskAwaiter`1 fallback that made
            // every multidim type look like a Task awaiter (the pre-refl2
            // `.Name`/`.FullName`/`GetElementType` breakage). The rank rides the
            // DISPLAY name only (ReflectionDisplayName), so the registry key
            // stays element-identity based like the single-dim case.
            if (b == 0x14) // ARRAY (multidim)
            {
                var elemName = SzArrayElementFullName(md, ref sig);
                _ = sig.ReadCompressedInteger(); // rank
                var numSizes = sig.ReadCompressedInteger();
                for (var i = 0; i < numSizes; i++) _ = sig.ReadCompressedInteger();
                var numLoBounds = sig.ReadCompressedInteger();
                for (var i = 0; i < numLoBounds; i++) _ = sig.ReadCompressedInteger();
                return "[" + elemName;
            }
            return TypeSpecName(md, hs); // remaining legacy shapes unchanged
        }

        // refl2 (predicates): the managed FULL NAME of one SZARRAY element type,
        // matching array.cpp's "[<element full name>" descriptor naming so a
        // folded typeof(int[]) produces the registry identity "System.Int32" ->
        // "[System.Int32". The element is an encoded type; primitives resolve
        // by their System.* full name, CLASS/VALUETYPE by the TypeDefOrRef
        // handle, and a NESTED SZARRAY (jagged) recurses to the bracket name
        // (int[][] -> "[[System.Int32" -- the same nesting the runtime strips
        // one '[' at a time). Throws for a generic VAR/MVAR element (no concrete
        // name in the metadata -- the fold rejects it LOUD instead of an
        // invented name).
        static string SzArrayElementFullName(MetadataReader md, ref BlobReader sig)
        {
            var eb = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
            switch (eb)
            {
                case 0x01: return "System.Void";
                case 0x02: return "System.Boolean";
                case 0x03: return "System.Char";
                case 0x04: return "System.SByte";
                case 0x05: return "System.Byte";
                case 0x06: return "System.Int16";
                case 0x07: return "System.UInt16";
                case 0x08: return "System.Int32";
                case 0x09: return "System.UInt32";
                case 0x0A: return "System.Int64";
                case 0x0B: return "System.UInt64";
                case 0x0C: return "System.Single";
                case 0x0D: return "System.Double";
                case 0x0E: return "System.String";
                case 0x18: return "System.IntPtr";
                case 0x19: return "System.UIntPtr";
                case 0x1C: return "System.Object";
                case 0x11:
                case 0x12:
                    var tok = sig.ReadTypeHandle();
                    return tok.Kind switch
                    {
                        HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)tok),
                        HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)tok),
                        // A generic-instance / array element DEFINITION (a
                        // TypeSpec handle): decode it so an array of a closed
                        // generic (List<int>[]) keeps the element identity.
                        HandleKind.TypeSpecification => TypeNameForReflection(
                            md, MetadataTokens.GetToken(tok)),
                        _ => throw new NotSupportedException(
                            $"typeof SZARRAY element kind {tok.Kind} not supported in refl2"),
                    };
                // refl2 (A): a GENERICINST element (`typeof(List<int>[])`). The
                // array identity key is the ELEMENT's registry name (the
                // definition's full name -- the typeinfo convention), so decode
                // the definition and consume the argument blobs.
                case 0x15:
                {
                    _ = sig.ReadByte(); // CLASS / VALUETYPE marker
                    var defTok = sig.ReadTypeHandle();
                    var defName = defTok.Kind switch
                    {
                        HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defTok),
                        HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defTok),
                        HandleKind.TypeSpecification => TypeNameForReflection(
                            md, MetadataTokens.GetToken(defTok)),
                        _ => throw new NotSupportedException(
                            $"typeof SZARRAY generic element kind {defTok.Kind} not supported in refl2"),
                    };
                    var argc = sig.ReadCompressedInteger();
                    for (var i = 0; i < argc; i++) _ = DecodeDisplayType(md, ref sig, false, out _);
                    return defName;
                }
                // A generic type parameter element (`typeof(T[])`): the M3
                // single-instantiation convention resolves it to Int32.
                case 0x13:
                {
                    _ = sig.ReadCompressedInteger();
                    var hint = MethodRefResolver.InstantiationSuffixHint;
                    if (hint != null) return SuffixToManagedTypeName(hint.Split('_')[0]);
                    return "System.Int32";
                }
                case 0x1D: return "[" + SzArrayElementFullName(md, ref sig);
                case 0x14:
                    // multidim element: decode to the "[elem]" name (the shared
                    // multidim token) and re-bracket it.
                    return "[" + ReadM3MultidimArrayArg(md, ref sig);
                default:
                    throw new NotSupportedException(
                        $"typeof SZARRAY element byte 0x{eb:X2} not supported in refl2");
            }
        }

        // R2b P2 (ilistException receiver tracking): whether a call's RETURN
        // signature slot is a generic parameter (0x13 VAR / 0x1E MVAR).
        static bool ReturnIsVar(MetadataReader md, int token)
        {
            // D5: a MethodSpec token recurses to the generic method's own
            // signature (a shared method-level def call -- the spec wraps the
            // underlying MethodDef). Cross-assembly (MemberRef) specs stay BCL.
            var h = MetadataTokens.Handle(token);
            BlobHandle sigHandle;
            switch (h.Kind)
            {
                case HandleKind.MethodDefinition:
                    sigHandle = md.GetMethodDefinition((MethodDefinitionHandle)h).Signature;
                    break;
                case HandleKind.MethodSpecification:
                    var ms = md.GetMethodSpecification((MethodSpecificationHandle)h);
                    if (ms.Method.Kind != HandleKind.MethodDefinition) return false;
                    sigHandle = md.GetMethodDefinition((MethodDefinitionHandle)ms.Method).Signature;
                    break;
                default:
                    sigHandle = md.GetMemberReference((MemberReferenceHandle)h).Signature;
                    break;
            }
            var rs = md.GetBlobReader(sigHandle);
            var hdr = rs.ReadSignatureHeader();
            if (hdr.IsGeneric) _ = rs.ReadCompressedInteger();
            _ = rs.ReadCompressedInteger(); // parameter count
            var rb = rs.ReadByte();
            return rb is 0x13 or 0x1E;
        }

        // The CONCRETE reference type of a single-argument generic TypeSpec
        // parent (IMyList`1<class System.Exception> -> "System.Exception").
        // Null when the parent is not a 1-arg GenericInst or the argument is
        // not a reference type (value-type args keep the existing slot
        // semantics -- caller records only a non-null result).
        static string? VarArgRefFullName(MetadataReader md, int token)
        {
            var h = MetadataTokens.Handle(token);
            EntityHandle parent = h.Kind == HandleKind.MethodDefinition
                ? md.GetMethodDefinition((MethodDefinitionHandle)h).GetDeclaringType()
                : md.GetMemberReference((MemberReferenceHandle)h).Parent;
            if (parent.Kind != HandleKind.TypeSpecification) return null;
            var sig = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
            if (sig.ReadByte() != 0x15) return null; // GENERICINST
            _ = sig.ReadByte();                      // CLASS(0x12)/VALUETYPE(0x11) marker
            _ = sig.ReadTypeHandle();                // generic definition
            if (sig.ReadCompressedInteger() != 1) return null;
            var b = sig.ReadByte();
            if (b is 0x11 or 0x40) return null;      // value type / TypeHandle: not a reference
            if (b == 0x12)
            {
                var th = sig.ReadTypeHandle();
                return th.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)th),
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)th),
                    _ => null,
                };
            }
            return b switch
            {
                0x0E => "System.String",
                0x1C => "System.Object",
                _ => null,
            };
        }

        // newobj token -> full name of the ctor's owning type ("Calc.Point" ->
        // the generated struct Calc_Point).
        // Generic instance (M3 Task 7): MemberRef.Parent is a TypeSpec ->
        // decode GenericInst -> instance name "{definition name}_{encoded arg}"
        // ("Box`1<int>" -> "Box_int").
        static string CtorTypeFullName(MetadataReader md, int token)
        {
            var handle = MetadataTokens.Handle(token);
            EntityHandle owner = handle.Kind switch
            {
                HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType(),
                HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)handle).Parent,
                _ => throw new NotSupportedException($"newobj token kind {handle.Kind} not supported in M2"),
            };
            return owner.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)owner),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)owner),
                HandleKind.TypeSpecification => GenericInstantiationName(md, (TypeSpecificationHandle)owner),
                _ => throw new NotSupportedException($"newobj owner kind {owner.Kind} not supported in M2"),
            };
        }

        // G1 (C1): whether a newobj token is a MULTIDIM array ctor (`new
        // int[2,2]` -> the MemberRef int32_t[0...,0...,0...]::.ctor). True when
        // the ctor's declaring TypeSpec blob opens with ELEMENT_TYPE_ARRAY
        // (0x14); the element byte and rank are decoded for the allocation.
        static bool TryDecodeMultidimCtor(MetadataReader md, int token, out string elemSuffix, out int rank)
        {
            elemSuffix = "object"; rank = 0;
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind != HandleKind.MemberReference) return false;
            var parent = md.GetMemberReference((MemberReferenceHandle)handle).Parent;
            if (parent.Kind != HandleKind.TypeSpecification) return false;
            var spec = md.GetTypeSpecification((TypeSpecificationHandle)parent).Signature;
            var sig = md.GetBlobReader(spec);
            if (sig.ReadByte() != 0x14) return false;
            var elem = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
            // GN-11: the element type is a self-delimiting FULL type -- it has to
            // be consumed BEFORE the outer rank/sizes/lobounds are read. A
            // complex element (array / nested generic) otherwise leaves its
            // payload under the rank read (e.g. `new int[2,2][,]` read the inner
            // int32 0x08 byte as rank 8 instead of 2).
            elemSuffix = ReadMultidimElemSuffix(md, ref sig, elem);
            rank = sig.ReadCompressedInteger();
            var numSizes = sig.ReadCompressedInteger();
            for (var i = 0; i < numSizes; i++) _ = sig.ReadCompressedInteger();
            var numLoBounds = sig.ReadCompressedInteger();
            for (var i = 0; i < numLoBounds; i++) _ = sig.ReadCompressedInteger();
            return true;
        }

        // GN-11: the multidim ELEMENT byte -> the M3 suffix, FULLY consuming the
        // element type. A reference element (string/object/class/SZARRAY/
        // multidim/nested reference generic) maps to the existing "object" slot
        // (an IntPtr-size reference); primitive elements keep their width; a
        // non-primitive VALUE element (VALUETYPE, or a VALUETYPE nested generic)
        // is LOUD -- the multidim allocator has no value-element width for it.
        static string ReadMultidimElemSuffix(MetadataReader md, ref BlobReader sig, byte elem)
        {
            switch (elem)
            {
                case 0x02: return "bool";
                case 0x03: return "char";
                case 0x04: return "sbyte";
                case 0x05: return "byte";
                case 0x06: return "short";
                case 0x07: return "ushort";
                case 0x08: return "int";
                case 0x09: return "uint";
                case 0x0A: return "long";
                case 0x0B: return "ulong";
                case 0x0C: return "float";
                case 0x0D: return "double";
                case 0x0E: return "string";
                case 0x1C: return "object";
                case 0x12: // CLASS (reference element) -> IntPtr-size ref slot
                    _ = sig.ReadTypeHandle();
                    return "object";
                case 0x11: // VALUETYPE element: the value channel (b5-gaps 13)
                {
                    var vtHandle = sig.ReadTypeHandle();
                    if (MultidimValueElemSuffix(md, vtHandle) is { } vt)
                        return vt.Suffix;
                    throw new NotSupportedException(
                        $"multidim array element byte 0x11 (VALUETYPE) in M3 (allocation): no value-element channel for the element type");
                }
                case 0x1D: // SZARRAY -> reference element ("object" slot)
                    _ = ReadM3ArrayArg(md, ref sig);
                    return "object";
                case 0x14: // multidim ARRAY -> reference element ("object" slot)
                    _ = ReadM3MultidimArrayArg(md, ref sig);
                    return "object";
                case 0x15: // GENERICINST (nested): reference -> "object", value -> LOUD
                {
                    var peek = sig; // BlobReader copy: inspect the marker without consuming
                    if (peek.ReadByte() == 0x11)
                        throw new NotSupportedException("multidim array element byte 0x15 (VALUETYPE nested generic) in M3 (allocation)");
                    _ = ReadM3NestedGeneric(md, ref sig);
                    return "object";
                }
                default:
                    throw new NotSupportedException($"multidim array element byte 0x{elem:X2} in M3 (allocation)");
            }
        }

        // G1 (C1): the multidim array TYPE NAME for a newobj token -- the
        // element- and rank-aware bracket token ("[int2]") the Get/Set owner
        // resolution and the generic-arg rule use.
        static string MultidimTypeName(MetadataReader md, int token)
        {
            _ = TryDecodeMultidimCtor(md, token, out var elemSuffix, out var rank);
            return "[" + elemSuffix + rank + "]";
        }

        // G1 (C1): the element SUFFIX of a `mdnew_<elem><rank>` allocation
        // function name -- the rank digits are stripped (the suffix is the
        // longest known element name at the head of the body). b5-gaps 13:
        // the value families join the table ("dto" BEFORE "dt" -- "dto2"
        // starts with "dt", so the prefix-related pair must be matched
        // longest-first).
        static string MultidimElemName(string function)
        {
            var body = function["mdnew_".Length..];
            foreach (var s in new[] { "bool", "char", "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "string", "object", "guid", "dec", "dto", "ts", "dt" })
            {
                if (body.StartsWith(s, StringComparison.Ordinal)) return s;
            }
            return body;
        }

        // b5-gaps 13: the element-suffix prefix length of a "[<elem><rank>]"
        // token body (the rank digits follow the non-digit element name).
        static int MultidimElemNamePrefixLen(string inner)
        {
            var i = 0;
            while (i < inner.Length && !char.IsDigit(inner[i])) i++;
            return i;
        }

        // G1 (C1): the multidim element SUFFIX -> element width (the M3 suffix
        // table, shared with ReadMultidimElemSuffix). b5-gaps 13: the value
        // families ride the arrayinit-vt cell widths (DateTime/TimeSpan 8,
        // Guid/Decimal/DateTimeOffset 16).
        static int? MdSuffixWidth(string suffix) => suffix switch
        {
            "bool" or "char" or "sbyte" or "byte" => 1,
            "short" or "ushort" => 2,
            "int" or "uint" or "float" => 4,
            "long" or "ulong" or "double" => 8,
            "dt" or "ts" => 8,
            "dec" or "guid" or "dto" => 16,
            _ => null,
        };

        // TypeSpec signature -> generic instance name. Signature: 0x15
        // (GenericInst) + TypeDefOrRef (generic definition) + arg count + arg
        // types. M3: an int32 arg -> "int"; instance name =
        // {definition}_{arg}.
        static string GenericInstantiationName(MetadataReader md, TypeSpecificationHandle handle)
        {
            var spec = md.GetTypeSpecification(handle);
            var sig = md.GetBlobReader(spec.Signature);
            var header = sig.ReadByte();
            if (header != 0x15)
            {
                // G1 (C1): a multidim ARRAY (0x14) TypeSpec as the newobj
                // ctor owner (`new int[2,2]` lowers to `newobj
                // int32_t[0...,0...,0...]::.ctor(int32,int32)` -- the ctor's
                // DECLARING TYPE is the multidim array, a reference slot). The
                // element + rank decode below is the canonical multidim token
                // (the same "[<elem><rank>]" the four-place generic-arg rule
                // uses), so the ctor-type name for the multidim allocation
                // stays element- and rank-aware.
                if (header == 0x14)
                    return ReadM3MultidimArrayArg(md, ref sig);
                throw new NotSupportedException($"TypeSpec signature byte 0x{header:X2} not GenericInst(0x15) in M3 (newobj ctor-type resolution)");
            }
            // GenericInst definition: ELEMENT_TYPE_CLASS(0x12)/VALUETYPE(0x11)/
            // TypeSpec(0x1B) + TypeDefOrRef coded index (Roslyn references the
            // generic definition with CLASS).
            var defType = sig.ReadByte();
            _ = defType; // the element-type byte is consumed; M3 accepts CLASS/VALUETYPE
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
                args[i] = ReadM3Arg(md, ref sig, b);
            }
            // Definition name (incl. the arity suffix "Box`1") + args (M3:
            // int32 -> "int"). The emitter's TypeCppName sanitizes (` -> _),
            // aligning with TypeEmitter's generic instance name
            // {TypeCppName}_int ("Box`1" -> "Box_1_int").
            GenericUsageAnalyzer.CollapseInstantiation(defName, args); // P-G2 shared collapse
            // GN-N5: append the deterministic disambiguation suffix when this
            // generic-instance name collides with another's ("Box<Cases.A_B>"
            // vs "Box<Cases.A.B>" -- Sanitize maps '.' and '_' identically).
            return defName + "_" + string.Join("_", args)
                + TypeEmitter.GenericInstanceCppNameSuffix(defName, args);
        }

        // The M3 generic-ARGUMENT byte table (0x15 = a NESTED generic
        // instantiation, e.g. IEnumerable`1<KeyValuePair`2<string,int>> --
        // recurse so the outer instance name carries the nested suffix).
        // ECMA-335 II.23.1.16: custom modifiers (0x1F CMOD_REQD / 0x20
        // CMOD_OPT) are stripped before the modified type (SZARRAY is 0x1D
        // only -- the historical 0x1F-as-SZARRAY convention was wrong).
        static string ReadM3Arg(MetadataReader md, ref BlobReader s, byte b)
        {
            b = MethodSignatureCoder.SkipCustomModifiers(ref s, b);
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
                 0x11 => MethodSignatureCoder.ResolveM3ArgTypeName(md, ref s),
                 0x12 => MethodSignatureCoder.ResolveM3ArgTypeName(md, ref s),
                0x1C => "object",
                0x1D => ReadM3ArrayArg(md, ref s),
                0x14 => ReadM3MultidimArrayArg(md, ref s),
                0x15 => ReadM3NestedGeneric(md, ref s),
                // B2/B5 (boxunbox/generics): an OPEN TypeSpec arg (VAR 0x13 +
                // index, e.g. `this.item` parented on Box<T> inside a generic
                // definition body) denotes the enclosing closed clone's T --
                // resolve through the body pass's InstantiationSuffixHint
                // (the R2b P2 mechanism; MethodRefResolver's nested arm is
                // the precedent). Outside a body pass the hint is null and
                // the historical "int" encoding holds.
                0x13 => ReadM3VarArg(ref s),
                var other => throw new NotSupportedException($"generic argument byte 0x{other:X2} in M3"),
            };
        }

        static string ReadM3VarArg(ref BlobReader s)
        {
            // GN-13 fix4: resolve the VAR index per-argument when the clone
            // carries a per-argument array (arity>=2); otherwise keep the
            // legacy whole-suffix behavior (arity-1 / definition bodies).
            var i = s.ReadCompressedInteger(); // VAR index byte follows 0x13
            return MethodRefResolver.InstantiationVarSuffix(i)
                ?? MethodRefResolver.InstantiationSuffixHint ?? "int";
        }

        // G1 (C1): an SZARRAY (0x1D) generic argument -- element-aware
        // "[<elementSuffix>]" token (see CodeGenerator.ReadM3ArrayArg; this is
        // the StackFold mirror of the four-place rule). Simple element shapes
        // decode through ReadM3Arg. GN-10 (ii): a COMPLEX element that is
        // itself an ARRAY (0x1D/0x14) is decoded recursively so the token
        // matches MethodBodyReader.ArrayGenericArgSuffix ("int[][]" ->
        // "[[int]]"); a nested GenericInst (0x15) / VAR (0x13) element keeps
        // the historical "array" token.
        static string ReadM3ArrayArg(MetadataReader md, ref BlobReader s)
        {
            var elem = MethodSignatureCoder.SkipCustomModifiers(ref s, s.ReadByte());
            switch (elem)
            {
                case 0x02: case 0x03: case 0x04: case 0x05: case 0x06: case 0x07:
                case 0x08: case 0x09: case 0x0A: case 0x0B: case 0x0C: case 0x0D:
                case 0x0E: case 0x1C:
                case 0x11: case 0x12:
                    return "[" + ReadM3Arg(md, ref s, elem) + "]";
                case 0x1D: return "[" + ReadM3ArrayArg(md, ref s) + "]";
                case 0x14: return "[" + ReadM3MultidimArrayArg(md, ref s) + "]";
                default:
                    _ = ReadM3Arg(md, ref s, elem); // consume the element payload
                    return "array";
            }
        }

        // G1 (C1): a multidim ARRAY (0x14) generic argument. The blob is
        // ELEMENT_TYPE_ARRAY: <element type> <rank:compressed> <numSizes:
        // compressed> [sizes...] <numLoBounds:compressed> [lobounds...]. The
        // token is element- AND rank-aware "[<elementSuffix><rank>]" (e.g.
        // int[,] -> "[int2]", int[,,] -> "[int3]") so closed instantiations
        // with different multidim shapes are DISTINCT (Box<int[,]> !=
        // Box<int[,,]>). It mirrors SZARRAY's single-rank array token; the
        // SimpleGenericArgSuffix authority produces the same token on the
        // local-kind side. The bracket shape keeps IsArrayArgToken true
        // (per-instance array emission, never the object-collapse).
        static string ReadM3MultidimArrayArg(MetadataReader md, ref BlobReader s)
        {
            var elem = MethodSignatureCoder.SkipCustomModifiers(ref s, s.ReadByte());
            var elemSuffix = elem switch
            {
                0x02 => "bool", 0x03 => "char", 0x04 => "sbyte", 0x05 => "byte",
                0x06 => "short", 0x07 => "ushort", 0x08 => "int", 0x09 => "uint",
                0x0A => "long", 0x0B => "ulong", 0x0C => "float", 0x0D => "double",
                0x0E => "string", 0x1C => "object",
                0x11 or 0x12 => MethodSignatureCoder.ResolveM3ArgTypeName(md, ref s),
                // GN-10 (ii): a COMPLEX element that is itself an ARRAY
                // (0x1D/0x14) decodes recursively to the element-aware token
                // (the MethodBodyReader.MultidimArrayArgSuffix rule); nested
                // GenericInst / VAR keep the shared fallback token.
                var ce => ConsumeComplexM3ArrayElem(md, ref s, ce),
            };
            var rank = s.ReadCompressedInteger();
            var numSizes = s.ReadCompressedInteger();
            for (var i = 0; i < numSizes; i++) _ = s.ReadCompressedInteger();
            var numLoBounds = s.ReadCompressedInteger();
            for (var i = 0; i < numLoBounds; i++) _ = s.ReadCompressedInteger();
            return "[" + elemSuffix + rank + "]";
        }

        static string ConsumeComplexM3ArrayElem(MetadataReader md, ref BlobReader s, byte elem)
        {
            switch (elem)
            {
                case 0x1D: return ReadM3ArrayArg(md, ref s);
                case 0x14: return ReadM3MultidimArrayArg(md, ref s);
                case 0x15: _ = ReadM3NestedGeneric(md, ref s); return "array";
                case 0x13: _ = ReadM3VarArg(ref s); return "int";
                default:
                    throw new NotSupportedException($"multidim array generic-argument element byte 0x{elem:X2} in M3");
            }
        }

        static string ReadM3NestedGeneric(MetadataReader md, ref BlobReader s)
        {
            _ = s.ReadByte(); // CLASS(0x12)/VALUETYPE(0x11) marker
            EntityHandle nestedDef = s.ReadTypeHandle();
            string nestedName = nestedDef.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)nestedDef),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)nestedDef),
                HandleKind.TypeSpecification => GenericInstantiationName(md, (TypeSpecificationHandle)nestedDef),
                _ => throw new NotSupportedException($"nested generic definition kind {nestedDef.Kind} in M3"),
            };
            var nestedCount = s.ReadCompressedInteger();
            var nestedArgs = new string[nestedCount];
            for (var j = 0; j < nestedCount; ++j)
                nestedArgs[j] = ReadM3Arg(md, ref s, s.ReadByte());
            return nestedName + "_" + string.Join("_", nestedArgs);
        }

        // Type full name -> generated struct name ("Calc.Point" -> Calc_Point).
        static string TypeCppName(string fullName) => TypeEmitter.Sanitize(fullName);

        //--------------------------------------------------------------------------
        // Lazy static-constructor triggering (beforefieldinit semantics,
        // Task 6).
        // Rule (.NET semantics): the .cctor must have run before a type's first
        // "use" --
        //   (1) static field access (ldsfld/stsfld)
        //   (2) instantiation (newobj)
        //   (3) static method call (call; M2 conservatively includes instance
        //       method this needs? no -- instance method calls do not trigger;
        //       M2 covers only the field/instantiation/static-call three sites
        //       of (1)(2)(3))
        // Emission (audit #5 guard optimization): an inlined done check
        // `if (done == false) ensure_cctor_X();` at the access site -- the hot
        // path is one load + test + predicted-not-taken branch instead of a
        // NOINLINE call (the .cctor body stays NOINLINE to keep frame records).
        // Adjacent same-type guards merge (the flag only ever turns true); the
        // .cctor itself emits no self-guard (done is always true).
        //--------------------------------------------------------------------------
        static void EnsureCctor(List<Stmt> stmts, MethodContext ctx, MetadataReader? md, IlInstruction ins, IReadOnlyList<TypeDef>? types)
        {
            if (md == null) return;
            var owner = FieldOwnerFullName(md, ins);
            if (owner == null || !TypeHasCctor(md, owner)) return;
            if (ctx.CctorOwnerFullName == owner) return; // inside the .cctor: self-guard is redundant
            EmitEnsureCall(stmts, owner, ins);
        }

        static void EnsureCctorForCall(List<Stmt> stmts, MethodContext ctx, MetadataReader? md, IlInstruction ins, IReadOnlyList<TypeDef>? types)
        {
            if (md == null) return;
            var owner = CallOwnerFullName(md, ins);
            if (owner == null || !TypeHasCctor(md, owner)) return;
            if (ctx.CctorOwnerFullName == owner) return; // inside the .cctor: self-guard is redundant
            EmitEnsureCall(stmts, owner, ins);
        }

        static void EnsureCctorForNewObj(List<Stmt> stmts, MethodContext ctx, MetadataReader? md, IlInstruction ins, IReadOnlyList<TypeDef>? types)
        {
            if (md == null) return;
            var owner = CtorTypeFullName(md, (int)ins.Operand);
            if (owner == null || !TypeHasCctor(md, owner)) return;
            if (ctx.CctorOwnerFullName == owner) return; // inside the .cctor: self-guard is redundant
            EmitEnsureCall(stmts, owner, ins);
        }

        static void EmitEnsureCall(List<Stmt> stmts, string ownerFullName, IlInstruction ins)
        {
            var fn = $"ensure_cctor_{TypeCppName(ownerFullName)}";
            // Adjacent same-type guard merge (basic-block level, audit #5): the
            // done flag only ever turns true, so the second of two adjacent
            // same-type guards is always redundant (adjacent = no intervening
            // control flow -- a LabelStmt/BranchStmt/assignment all break
            // adjacency). Typical shapes: ldsfld+stsfld read/write pairs,
            // call+field-access pairs.
            if (stmts.Count > 0 && stmts[^1] is CallStmt prev && prev.Function == fn) return;
            stmts.Add(new CallStmt
            {
                Function = fn,
                SourceOffset = ins.Offset,
                SourceOpName = OpName(ins.OpCode)
            });
        }

        // Field token -> declaring type's full name (null when unresolvable).
        static string? FieldOwnerFullName(MetadataReader md, IlInstruction ins)
        {
            var (_, owner, _) = ResolveField(md, ins);
            return owner;
        }

        // Call token -> target method's declaring type full name (MethodDef or
        // MemberReference).
        static string? CallOwnerFullName(MetadataReader md, IlInstruction ins)
        {
            var handle = MetadataTokens.Handle((int)ins.Operand);
            EntityHandle? owner = handle.Kind switch
            {
                HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType(),
                HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)handle).Parent,
                _ => null,
            };
            if (owner == null) return null;
            return owner.Value.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)owner.Value),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)owner.Value),
                _ => null,
            };
        }

        // Whether the type declares a .cctor (a static ctor with a method body,
        // RVA != 0). A TypeReference (BCL type) is not in this assembly -> no
        // user .cctor, returns false.
        static bool TypeHasCctor(MetadataReader md, string fullName)
        {
            foreach (var tdh in md.TypeDefinitions)
            {
                var td = md.GetTypeDefinition(tdh);
                if (md.GetFullName(tdh) != fullName) continue;
                foreach (var mh in td.GetMethods())
                {
                    var m = md.GetMethodDefinition(mh);
                    if (md.GetString(m.Name) == ".cctor" && m.RelativeVirtualAddress != 0)
                        return true;
                }
                return false;
            }
            return false;
        }

        // Unified resolution of a field token -> (FieldDefHandle or
        // MemberReferenceHandle). Generic field access (M3 Task 7): ldfld/stfld
        // tokens inside a generic definition are MemberRefs (0x0A); non-generic
        // fields are FieldDefs (0x06) -- both paths return the field
        // name/declaring type uniformly.
        static (string Name, string Owner, string Signature) ResolveField(MetadataReader md, IlInstruction ins)
        {
            var handle = MetadataTokens.Handle((int)ins.Operand);
            if (handle.Kind == HandleKind.FieldDefinition)
            {
                var fd = md.GetFieldDefinition((FieldDefinitionHandle)handle);
                // Nested-owner fix (arrayext2): nested TypeDefs carry an empty
                // namespace, so ns+"."+name degrades to the SHORT name ("<>c")
                // while the definition side uses DeclaringTypeName+Name
                // ("ArrayExt2Demo___c") -- static-field stores then reference an
                // undeclared symbol. md.GetFullName walks the declaring chain
                // ("A.<>c"), exactly like the MemberReference path below.
                var owner = md.GetFullName(fd.GetDeclaringType());
                return (md.GetString(fd.Name), owner, md.GetBlobBytes(fd.Signature).Length > 0 ? string.Join("", md.GetBlobBytes(fd.Signature).Select(b => b.ToString("X2"))) : "");
            }
            if (handle.Kind == HandleKind.MemberReference)
            {
                var mr = md.GetMemberReference((MemberReferenceHandle)handle);
                var owner = mr.Parent.Kind switch
                {
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)mr.Parent),
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)mr.Parent),
                    // M3 generics: a generic-instance field access carries a
                    // TypeSpec parent -> the instantiation name.
                    HandleKind.TypeSpecification => GenericInstantiationName(md, (TypeSpecificationHandle)mr.Parent),
                    _ => "?",
                };
                return (md.GetString(mr.Name), owner, md.GetBlobBytes(mr.Signature).Length > 0 ? string.Join("", md.GetBlobBytes(mr.Signature).Select(b => b.ToString("X2"))) : "");
            }
            throw new NotSupportedException($"field token kind {handle.Kind} not supported in M3");
        }

        // impl-arrayagg (defect 2): the DECLARED managed type of an
        // ldfld/ldsfld field, decoded from its signature
        // ("System.Collections.Generic.List`1_System.Decimal" for a
        // List<decimal> field). A List receiver loaded from a FIELD (a captured
        // local's display-class field, a static/instance List field) carried no
        // objTypes hint, so a LINQ operator on it fell to the ARRAY family and
        // reinterpreted the List object as Il2CppArray* (its items pointer read
        // as a length). Null when the signature shape is not decodable -- the
        // caller then keeps the historical untyped FieldExpr.
        static string? FieldTypeFullName(MetadataReader? md, IlInstruction ins)
        {
            if (md == null) return null;
            try
            {
                var handle = MetadataTokens.Handle((int)ins.Operand);
                BlobHandle sig;
                if (handle.Kind == HandleKind.FieldDefinition)
                    sig = md.GetFieldDefinition((FieldDefinitionHandle)handle).Signature;
                else if (handle.Kind == HandleKind.MemberReference)
                    sig = md.GetMemberReference((MemberReferenceHandle)handle).Signature;
                else return null;
                var s = md.GetBlobReader(sig);
                if (s.ReadByte() != 0x06) return null; // FIELD signature
                return ReadM3Arg(md, ref s, s.ReadByte());
            }
            catch
            {
                return null;
            }
        }

        // Field name: with md non-null, resolve the token ->
        // "{Owner.FullName}.{FieldName}" (mapped to the C++ storage name at
        // emission); with md null (pure-fold tests) fall back to the token
        // placeholder string.
        static string FieldName(MetadataReader? md, IlInstruction ins)
        {
            if (md == null) return ins.StringOperand ?? $"field_{ins.Operand}";
            var (name, owner, _) = ResolveField(md, ins);
            return owner + "." + name;
        }

        // ldfld/stfld/ldflda field token -> the raw field name (the C++ struct
        // member name = Sanitize(raw name)).
        static string FieldToken(MetadataReader? md, IlInstruction ins)
        {
            if (md == null) return ins.StringOperand ?? $"field_{ins.Operand}";
            var (name, _, _) = ResolveField(md, ins);
            return name;
        }

        // Full name of the type owning the ldfld/stfld field ("Calc.Point" ->
        // the struct Calc_Point selector).
        static string OwnerFullName(MetadataReader? md, IlInstruction ins)
        {
            if (md == null) return "?";
            var (_, owner, _) = ResolveField(md, ins);
            return owner;
        }

        // Whether the field is a GC reference field. Read the field signature
        // bytes directly (FIELD=0x06 + type): 0x08 I4 -> value field;
        // 0x0E/0x12/0x1C -> reference fields; 0x13 generic T -> value by the
        // M3 single-instantiation convention; 0x15 GENERICINST -> reference
        // only for reference-carriers (IsGenericInstRefCarrier: CLASS
        // instantiations + the async TaskAwaiter/builder family, which ride
        // as task references -- other valuetype instantiations such as
        // Nullable`1 stay value slots).
        static bool IsRefField(MetadataReader? md, IlInstruction ins)
        {
            if (md == null) return false;
            var (_, _, signature) = ResolveField(md, ins);
            if (signature.Length < 2) return false;
            var typeByte = int.Parse(signature.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            // nb9 H-B5: SZARRAY (0x1D) / ARRAY (0x14) fields are GC references
            // too. A closure's captured array field (<>c__DisplayClass.a1) was
            // classified as a value field, so the generated temp was int32_t
            // and the object pointer assignment was ill-typed (the reported
            // nested-closure 0xC0000005 / C0 build failure).
            if (typeByte is 0x0E or 0x12 or 0x1C or 0x1D or 0x14) return true;
            // tuple1 (ValueTuple): the Item fields of a tuple carry a bare VAR
            // field signature ("06 13 <index>"), so the element's real type is
            // only in the PARENT TypeSpec's generic arguments -- check this
            // BEFORE the generic-inst arm (which the VAR byte never reaches).
            var tupleItemRef = ValueTupleItemFieldIsRef(md, ins);
            if (tupleItemRef.HasValue) return tupleItemRef.Value;
            if (typeByte == 0x15)
            {
                return IsGenericInstRefCarrier(md, ins);
            }
            return false;
        }

        // tuple1 (ValueTuple): whether a FIELD token is a ValueTuple item and,
        // if so, whether the ELEMENT is a reference.
        //
        // The field SIGNATURE of Item1/Item2 on a generic instantiation is a
        // bare VAR ("06 13 <index>") -- the element's real type lives in the
        // PARENT TypeSpec's generic arguments, so the signature alone can never
        // answer this. The member reference's parent is
        // `System.ValueTuple`N<...>`; decode its generic argument at the field
        // index. Returns null when the field is not a ValueTuple item.
        static bool? ValueTupleItemFieldIsRef(MetadataReader md, IlInstruction ins)
        {
            try
            {
                var handle = MetadataTokens.Handle((int)ins.Operand);
                if (handle.Kind != HandleKind.MemberReference) return null;
                var mr = md.GetMemberReference((MemberReferenceHandle)handle);
                var name = md.GetString(mr.Name);
                if (!name.StartsWith("Item", StringComparison.Ordinal)) return null;
                if (!int.TryParse(name.AsSpan(4), out var itemIndex) || itemIndex < 1) return null;
                if (mr.Parent.Kind != HandleKind.TypeSpecification) return null;
                var spec = md.GetTypeSpecification((TypeSpecificationHandle)mr.Parent);
                var sig = md.GetBlobReader(spec.Signature);
                if (sig.ReadByte() != 0x15) return null;
                sig.ReadByte(); // CLASS/VALUETYPE marker
                var defHandle = sig.ReadTypeHandle();
                var defName = defHandle.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
                    _ => null,
                };
                if (!ValueTupleSupport.IsValueTupleName(defName)) return null;
                var argc = sig.ReadCompressedInteger();
                if (itemIndex > argc) return null;
                var elemByte = (byte)0;
                for (var i = 1; i <= argc; i++)
                {
                    var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
                    if (i == itemIndex) { elemByte = b; break; }
                    ConsumeTupleElemPayload(md, ref sig, b);
                }
                return elemByte is 0x0E or 0x12 or 0x1C or 0x1D or 0x14;
            }
            catch { return null; }
        }

        // Consume one generic-argument payload (keeping the blob aligned).
        static void ConsumeTupleElemPayload(MetadataReader md, ref BlobReader sig, byte b)
        {
            switch (b)
            {
                case 0x11:
                case 0x12:
                case 0x40:
                    _ = sig.ReadTypeHandle();
                    break;
                case 0x1D:
                    ConsumeTupleElemPayload(md, ref sig,
                        MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte()));
                    break;
                case 0x14:
                    ConsumeTupleElemPayload(md, ref sig,
                        MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte()));
                    _ = sig.ReadCompressedInteger();
                    var ns = sig.ReadCompressedInteger();
                    for (var i = 0; i < ns; i++) _ = sig.ReadCompressedInteger();
                    var nl = sig.ReadCompressedInteger();
                    for (var i = 0; i < nl; i++) _ = sig.ReadCompressedInteger();
                    break;
                case 0x15:
                {
                    sig.ReadByte();
                    _ = sig.ReadTypeHandle();
                    var n = sig.ReadCompressedInteger();
                    for (var i = 0; i < n; i++)
                        ConsumeTupleElemPayload(md, ref sig,
                            MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte()));
                    break;
                }
                case 0x13:
                case 0x1E:
                    _ = sig.ReadCompressedInteger();
                    break;
            }
        }

        // Whether a GENERICINST (0x15) field type rides as a GC reference:
        // CLASS instantiations always do; VALUETYPE instantiations do only
        // for the async ref-carrier family (TaskAwaiter`1 /
        // AsyncTaskMethodBuilder`1 -- batch 156c: the state-machine slots
        // hold the task object). Decoded via BlobReader (the hex-signature
        // fast path above cannot resolve the definition name); any decode
        // failure keeps the historical value-slot behavior.
        static bool IsGenericInstRefCarrier(MetadataReader md, IlInstruction ins)
        {
            try
            {
                var handle = MetadataTokens.Handle((int)ins.Operand);
                BlobReader sig;
                if (handle.Kind == HandleKind.FieldDefinition)
                    sig = md.GetBlobReader(md.GetFieldDefinition((FieldDefinitionHandle)handle).Signature);
                else if (handle.Kind == HandleKind.MemberReference)
                    sig = md.GetBlobReader(md.GetMemberReference((MemberReferenceHandle)handle).Signature);
                else return false;
                if (sig.ReadByte() != 0x06) return false;
                if (sig.ReadByte() != 0x15) return false;
                var marker = sig.ReadByte();
                if (marker == 0x12) return true;
                if (marker != 0x11) return false;
                var defHandle = sig.ReadTypeHandle();
                var defName = defHandle.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
                    _ => "",
                };
                return defName.StartsWith("System.Runtime.CompilerServices.TaskAwaiter`1", StringComparison.Ordinal)
                    || defName.StartsWith("System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1", StringComparison.Ordinal);
            }
            catch { return false; }
        }

        // Full name of the reference field's target type ("Calc.Node" ->
        // struct selector). Value fields return "".
        static string RefTargetFullName(MetadataReader? md, IlInstruction ins)
        {
            if (md == null) return "";
            var (_, _, signature) = ResolveField(md, ins);
            if (signature.Length < 2) return "";
            var typeByte = int.Parse(signature.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            if (typeByte == 0x0E) return "System.String";
            if (typeByte == 0x1C) return "System.Object";
            // 0x15 GENERICINST reference-carriers (the async awaiter/builder
            // slots) cast through the object identity -- no dedicated struct
            // exists, so the target stays System.Object.
            if (typeByte == 0x15) return IsGenericInstRefCarrier(md, ins) ? "System.Object" : "";
            // 0x12 CLASS: the type name is encoded in the signature's following
            // bytes -- M3 resolves it via MemberRef (generic scenario).
            // Simplified: a non-generic reference field's RefTarget resolved
            // from OwnerFullName + FieldDef suffices; generic fields T->int32
            // (non-reference) never reach here.
            if (typeByte == 0x12) return ResolveFieldRefTarget(md, ins);
            return "";
        }

        static string ResolveFieldRefTarget(MetadataReader md, IlInstruction ins)
        {
            var handle = MetadataTokens.Handle((int)ins.Operand);
            if (handle.Kind == HandleKind.FieldDefinition)
            {
                var fd = md.GetFieldDefinition((FieldDefinitionHandle)handle);
                var sig = md.GetBlobReader(fd.Signature);
                sig.ReadByte(); // FIELD header
                if (sig.ReadByte() == 0x12)
                    return ResolveHandleName(md, sig.ReadTypeHandle());
                return "";
            }
            // MemberRef field: the signature encodes the field type (M3:
            // reference-typed generic fields are not yet modeled -- return "").
            return "";
        }

        static string ResolveHandleName(MetadataReader md, EntityHandle handle) =>
            handle.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)handle),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)handle),
                _ => throw new NotSupportedException($"field ref type handle kind {handle.Kind} not supported in M2"),
            };

        // C++ member name sanitize (same rule set as TypeEmitter.Sanitize:
        // illegal C++ identifier characters -> '_'). A field that sanitizes
        // to _obj is renamed to _obj_field (the object header member is named
        // _obj -- see TypeEmitter.EmitClassStruct).
        static string SanitizeField(string name)
        {
            // TypeEmitter.SanitizeIdentChars is the shared whitelist tail (the
            // Split list below is a blacklist and would let '|' through -- the
            // local-function metadata-name defect).
            var s = TypeEmitter.SanitizeIdentChars(
                string.Join("_", name.Split('.', '+', '`', '<', '>', ',', '[', ']')));
            return s == "_obj" ? "_obj_field" : s;
        }

        // ECMA-style lowercase opcode names (Stloc_0 -> stloc.0, Ldc_i4_s ->
        // ldc.i4.s), for English IL provenance comments.
        static string OpName(ILOpCode op) => op.ToString().ToLowerInvariant().Replace('_', '.');

        static string ArgName(int index) => $"arg{index}";
        static string LocName(MethodContext ctx, int index) => index < ctx.LocNames.Length ? ctx.LocNames[index] : $"loc{index}";

        // M5 wiring: the coarse IL kind of a local slot ("i"/"l"/"d"/"r"/"v",
        // see MethodBodyInfo.LocalKinds) -- the conv-fold uses it to route
        // conversions on local operands to the matching runtime icall
        // (conv.i4 of an int64 local must call icall_conv_i4_i8, of a double
        // local icall_conv_i4_d). Non-locals (args, temps) return null.
        static string? LocKindOf(MethodContext ctx, Expr e) =>
            e is LocExpr l && l.Name.StartsWith("loc", StringComparison.Ordinal)
                && int.TryParse(l.Name.AsSpan(3), out var li) && li < ctx.LocalKinds.Length
                ? ctx.LocalKinds[li]
                : null;

        // valdeep L26: the value WIDTH of an expression ("l"/"d" = 64-bit
        // slot, "i" = int32 slot, "r" = reference). The conv.u8 fold uses it
        // to decide zero-extension: only 32-bit sources widen UNSIGNED.
        static string ExprWidth(Expr e, MethodContext ctx, IReadOnlyDictionary<string, string>? tempWidths) => e switch
        {
            Int64ConstExpr => "l",
            DoubleConstExpr => "d",
            IntConstExpr => "i",
            LocExpr l => LocWidth(l.Name, ctx, tempWidths),
            BinaryExpr b => b.Kind ?? "i",
            UnaryExpr => "i",
            // p0linq4 (D4): a REFERENCE-returning call is a managed pointer,
            // not an int32 slot. Without this arm a branch-join spill temp that
            // received an array-returning LINQ call (the csc delegate-cache
            // join in OrderBy(k1).ThenBy(k2)) was declared int32_t and the
            // pointer store was truncated.
            CallExpr c => c.ResultKind ?? (c.ReturnsRef ? "r" : "i"),
            _ => "i", // int32 default; refs/others never appear under conv.u8 in valid IL
        };

        // D-RND2-3 (randomdeep2): the WIDTH of a conv.i4 source expression.
        // ExprWidth reads BinaryExpr.Kind, but the arithmetic fold never sets
        // it -- a double-typed BinaryExpr (`(int)(NextDouble() * 10)`, the
        // multiply stays unmaterialized on the stack) reported "i", so the
        // conv.i4 fold dropped the truncation in comparison/expression
        // context. This probe recurses into the operand tree exactly like the
        // emitter's ExprIsDouble (CodeGenerator.cs) -- a BinaryExpr/UnaryExpr
        // whose operand is double-typed is a DOUBLE source. LocExpr widths
        // come from LocWidth (int64/double locals, "ptr" temps) so the int64
        // local path keeps icall_conv_i4_i8. Int32 sources ("i") stay
        // identities (unchanged); int64 BinaryExpr stays "i" as before (a
        // pre-existing out-of-scope gap, not touched here).
        static string ConvI4SourceWidth(Expr e, MethodContext ctx, IReadOnlyDictionary<string, string>? tempWidths) => e switch
        {
            Int64ConstExpr => "l",
            DoubleConstExpr => "d",
            IntConstExpr => "i",
            LocExpr l => LocWidth(l.Name, ctx, tempWidths),
            CallExpr c => c.ResultKind ?? "i",
            BinaryExpr b => ConvOperandIsDouble(b.Left, ctx, tempWidths) || ConvOperandIsDouble(b.Right, ctx, tempWidths) ? "d" : "i",
            UnaryExpr u => ConvOperandIsDouble(u.Operand, ctx, tempWidths) ? "d" : "i",
            _ => "i",
        };

        // D-RND2-3: whether an expression tree is double-typed (the fold-time
        // mirror of CodeGenerator.ExprIsDouble, using LocWidth for slot
        // widths -- the emitter's version reads the emitter-side local-kind
        // table).
        static bool ConvOperandIsDouble(Expr e, MethodContext ctx, IReadOnlyDictionary<string, string>? tempWidths) => e switch
        {
            DoubleConstExpr => true,
            LocExpr l => LocWidth(l.Name, ctx, tempWidths) == "d",
            CallExpr c => c.ResultKind == "d",
            BinaryExpr b => b.Kind == "d" || ConvOperandIsDouble(b.Left, ctx, tempWidths) || ConvOperandIsDouble(b.Right, ctx, tempWidths),
            UnaryExpr u => ConvOperandIsDouble(u.Operand, ctx, tempWidths),
            _ => false,
        };

        // The width of a LocExpr slot: "loc" locals via LocalKinds, "arg"
        // params via ParamTypeNames (arg0 is `this` for instance methods --
        // ParamTypeNames excludes it), "t" temps via the fold's tempWidths
        // map (materialized at the Push/join/snapshot sites). Unknown slots
        // default to int32 (the IR's uniform value slot).
        static string LocWidth(string name, MethodContext ctx, IReadOnlyDictionary<string, string>? tempWidths)
        {
            if (name.StartsWith("loc", StringComparison.Ordinal)
                && int.TryParse(name.AsSpan(3), out var li) && li < ctx.LocalKinds.Length)
                return ctx.LocalKinds[li];
            if (name.StartsWith("arg", StringComparison.Ordinal) && int.TryParse(name.AsSpan(3), out var ai)
                && ctx.ParamTypeNames != null)
            {
                var pi = ai - (ctx.IsStatic ? 0 : 1); // instance: arg0 = this
                if (pi >= 0 && pi < ctx.ParamTypeNames.Length)
                    return ctx.ParamTypeNames[pi] switch
                    {
                        "l" or "q" => "l", // int64/uint64 slot
                        "d" or "f" => "d", // double slot
                        _ => "i",
                    };
            }
            if (name.StartsWith("t", StringComparison.Ordinal) && tempWidths != null
                && tempWidths.TryGetValue(name, out var w))
                return w;
            return "i";
        }

        // conv.u8 zero-extension applies to every 32-bit (or unknown-width)
        // non-const source; 64-bit sources are bit identities; double and
        // reference values never widen here (csc emits conv.u8 on a double
        // only for double -> ulong -- an unchanged-known-gap, see discover).
        static bool Is32BitConvU8Source(Expr e, MethodContext ctx, IReadOnlyDictionary<string, string>? tempWidths) => e switch
        {
            Int64ConstExpr or DoubleConstExpr => false,
            // ptr-pinvoke: a pointer/IntPtr slot is already 64-bit -- it must
            // not take the 32-bit zero-extension helper (which would truncate
            // a `(ulong)ptr` to 32 bits before widening).
            LocExpr l => LocWidth(l.Name, ctx, tempWidths) is not ("l" or "d" or "r" or "ptr" or "uptr"),
            BinaryExpr b => b.Kind is not ("l" or "d"),
            UnaryExpr => true,
            IntConstExpr => false, // handled by the const fold above
            _ => false,
        };
        // stloc instruction -> local index (short forms 0..3 encoded in the
        // opcode, the long form in the operand).
        static int StlocIndex(ILOpCode op, int operand) => op switch
        {
            ILOpCode.Stloc_0 => 0,
            ILOpCode.Stloc_1 => 1,
            ILOpCode.Stloc_2 => 2,
            ILOpCode.Stloc_3 => 3,
            _ => operand,
        };
        // Whether the statement list contains a (not-yet-replaced) read of name
        // (for the dead-store removal decision).
        static bool StmtReferencesName(List<Stmt> stmts, string name)
        {
            foreach (var s in stmts)
            {
                switch (s)
                {
                    case AssignStmt a:
                        if (ReferencesTarget(a.Value, name)) return true;
                        break;
                    case ExprStmt es:
                        if (ReferencesTarget(es.Value, name)) return true;
                        break;
                    case RetStmt r:
                        if (r.Value != null && ReferencesTarget(r.Value, name)) return true;
                        break;
                    case FilterReturnStmt fr:
                        if (ReferencesTarget(fr.Value, name)) return true;
                        break;
                    case BranchStmt b:
                        if (b.Condition != null && ReferencesTarget(b.Condition, name)) return true;
                        break;
                    case CallStmt c:
                        if (c.Args.Any(x => ReferencesTarget(x, name))) return true;
                        break;
                    case FieldAssignStmt f:
                        if (ReferencesTarget(f.Object, name) || ReferencesTarget(f.Value, name)) return true;
                        break;
                }
            }
            return false;
        }
        static Expr Not(Expr e) => new BinaryExpr { Op = BinaryOp.Ceq, Left = e, Right = new IntConstExpr { Value = 0 } };
        // M5 wiring: does the opcode POP at least one stack value? Used to
        // detect catch handlers that bind/use the exception entry value as
        // their FIRST instruction (the compiler's two shapes: stloc for the
        // catch variable, call/callvirt for single-use inline consumption).
        static bool OpcodeConsumesStack(ILOpCode op) => op switch
        {
            ILOpCode.Stloc or ILOpCode.Stloc_s or ILOpCode.Stloc_0 or ILOpCode.Stloc_1
                or ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or ILOpCode.Starg or ILOpCode.Starg_s
                or ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Pop
                or ILOpCode.Stfld or ILOpCode.Stsfld or ILOpCode.Throw
                or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2 or ILOpCode.Stelem_i4 or ILOpCode.Stelem_ref
                // IL gap batch b23: the pointer-deref / value-type / block /
                // checked-op families all POP stack values -- a catch handler
                // whose first instruction is one of these must get the catch
                // entry pushed (OpcodeConsumesStack drives the entry detection).
                or ILOpCode.Ldind_i1 or ILOpCode.Ldind_u1 or ILOpCode.Ldind_i2 or ILOpCode.Ldind_u2
                or ILOpCode.Ldind_i4 or ILOpCode.Ldind_u4 or ILOpCode.Ldind_i8 or ILOpCode.Ldind_i
                or ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8 or ILOpCode.Ldind_ref
                or ILOpCode.Stind_ref or ILOpCode.Stind_i1 or ILOpCode.Stind_i2 or ILOpCode.Stind_i4
                or ILOpCode.Stind_i8 or ILOpCode.Stind_r4 or ILOpCode.Stind_r8 or ILOpCode.Stind_i
                or ILOpCode.Ldobj or ILOpCode.Stobj or ILOpCode.Initobj or ILOpCode.Cpobj
                or ILOpCode.Ldelema or ILOpCode.Ldelem or ILOpCode.Stelem
                or ILOpCode.Cpblk or ILOpCode.Initblk or ILOpCode.Localloc or ILOpCode.Ckfinite
                or ILOpCode.Add_ovf or ILOpCode.Add_ovf_un or ILOpCode.Sub_ovf or ILOpCode.Sub_ovf_un
                or ILOpCode.Mul_ovf or ILOpCode.Mul_ovf_un or ILOpCode.Rem_un or ILOpCode.Jmp
                or ILOpCode.Mkrefany or ILOpCode.Refanyval or ILOpCode.Refanytype or ILOpCode.Endfilter
                => true,
            _ => false,
        };
        static BinaryOp MapBinary(ILOpCode op) => op switch
        {
            ILOpCode.Add => BinaryOp.Add,
            ILOpCode.Sub => BinaryOp.Sub,
            ILOpCode.Mul => BinaryOp.Mul,
            ILOpCode.Div => BinaryOp.Div,
            ILOpCode.Div_un => BinaryOp.DivUn,
            ILOpCode.Rem => BinaryOp.Rem,
            ILOpCode.And => BinaryOp.And,
            ILOpCode.Or => BinaryOp.Or,
            ILOpCode.Xor => BinaryOp.Xor,
            ILOpCode.Shl => BinaryOp.Shl,
            ILOpCode.Shr => BinaryOp.Shr,
            ILOpCode.Shr_un => BinaryOp.ShrUn,
            ILOpCode.Ceq => BinaryOp.Ceq,
            ILOpCode.Cgt => BinaryOp.Cgt,
            ILOpCode.Clt => BinaryOp.Clt,
            ILOpCode.Cgt_un => BinaryOp.CgtUn,
            ILOpCode.Clt_un => BinaryOp.CltUn,
            _ => throw new NotSupportedException($"binary {op}")
        };
        // Branch compare condition mapping. Signed Bge/Ble must NEVER use the
        // unsigned CgtUn/CltUn: Bge(>=) == !(left<right) = Not(Clt),
        // Ble(<=) == !(left>right) = Not(Cgt), Bne_un(!=) = Not(Ceq).
        // Not(e) = Ceq(e, 0), the same helper as Brfalse.
        static Expr MapBranchCompare(ILOpCode op, Expr left, Expr right) => op switch
        {
            ILOpCode.Beq or ILOpCode.Beq_s =>
                new BinaryExpr { Op = BinaryOp.Ceq, Left = left, Right = right },
            ILOpCode.Bne_un or ILOpCode.Bne_un_s =>
                Not(new BinaryExpr { Op = BinaryOp.Ceq, Left = left, Right = right }),
            ILOpCode.Bgt or ILOpCode.Bgt_s =>
                new BinaryExpr { Op = BinaryOp.Cgt, Left = left, Right = right },
            ILOpCode.Bgt_un or ILOpCode.Bgt_un_s =>
                new BinaryExpr { Op = BinaryOp.CgtUn, Left = left, Right = right },
            ILOpCode.Blt or ILOpCode.Blt_s =>
                new BinaryExpr { Op = BinaryOp.Clt, Left = left, Right = right },
            ILOpCode.Blt_un or ILOpCode.Blt_un_s =>
                new BinaryExpr { Op = BinaryOp.CltUn, Left = left, Right = right },
            ILOpCode.Bge or ILOpCode.Bge_s =>
                Not(new BinaryExpr { Op = BinaryOp.Clt, Left = left, Right = right }),
            ILOpCode.Bge_un or ILOpCode.Bge_un_s =>
                Not(new BinaryExpr { Op = BinaryOp.CltUn, Left = left, Right = right }),
            ILOpCode.Ble or ILOpCode.Ble_s =>
                Not(new BinaryExpr { Op = BinaryOp.Cgt, Left = left, Right = right }),
            ILOpCode.Ble_un or ILOpCode.Ble_un_s =>
                Not(new BinaryExpr { Op = BinaryOp.CgtUn, Left = left, Right = right }),
            _ => throw new NotSupportedException($"branch compare {op}")
        };
    }

    //--------------------------------------------------------------------------
    // P0 (recursive_arith e2e): user-method call results must propagate their
    // int64/double return width. MethodRefResolver's user-method ResolvedMethod
    // never records ResultKind (null = int32), so the Fact recursion result
    // temp was declared int32_t and 15! = 1307674368000 truncated to
    // 2004310016. Decode the callee's RETURN element type from the method
    // signature blob:
    //   I8(0x0A)/U8(0x0B) -> "l" (int64_t)   R4(0x0C)/R8(0x0D) -> "d" (double)
    // The emitter's ExprKind maps these to the temp declarations. BCL mappings
    // carry ResultKind already -- this only fills the user-method gap.
    //--------------------------------------------------------------------------
    static string? UserCallResultKind(MetadataReader? md, int token)
    {
        if (md == null) return null;
        var handle = MetadataTokens.Handle(token);
        BlobHandle sigHandle;
        if (handle.Kind == HandleKind.MethodDefinition)
            sigHandle = md.GetMethodDefinition((MethodDefinitionHandle)handle).Signature;
        else if (handle.Kind == HandleKind.MemberReference)
            sigHandle = md.GetMemberReference((MemberReferenceHandle)handle).Signature;
        else if (handle.Kind == HandleKind.MethodSpecification)
        {
            // D5: a MethodSpec on a MethodDef may name a SHARED method-level
            // generic method (PickUser2::Pick<int>). Decode the underlying
            // definition signature so a 0x1E MVAR return can be classified
            // against the share set below. Cross-assembly MethodSpecs
            // (MemberReference) stay BCL-mapped (their ResultKind).
            var ms = md.GetMethodSpecification((MethodSpecificationHandle)handle);
            if (ms.Method.Kind != HandleKind.MethodDefinition) return null;
            sigHandle = md.GetMethodDefinition((MethodDefinitionHandle)ms.Method).Signature;
        }
        else
            return null;
        var sig = md.GetBlobReader(sigHandle);
        sig.ReadByte();              // calling convention
        sig.ReadCompressedInteger(); // param count
        var ret = sig.ReadByte();    // return element type
        // M4.5: native-int returns (0x17 ELEMENT_TYPE_I / 0x18 ELEMENT_TYPE_U --
        // the RAW IntPtr/UIntPtr encodings, reverse_static blob 00-01-18-0E)
        // get the "ptr" width like I8/R8 get "l"/"d".
        // P-G3: a VAR/MVAR (0x13/0x1E) return from a SHARED generic definition
        // (Pair`2<...>/Box`1<string>) is the object slot -- the shared clone
        // returns Il2CppObject*. The M3 single-instantiation default (null =
        // int32) would declare the result temp int32_t; the shared object
        // return then type-mismatches the caller slot. Non-shared definitions
        // keep the legacy null (M3 int default).
        if (ret is (byte)0x13 or (byte)0x1E)
            return SharedDefReturningObject(md, handle) ? "r" : null;
        // P-D3 P0 (fnptr DllImport RETURN): ELEMENT_TYPE_FNPTR (0x1B) return
        // gets the "p" (void*) carrier -- the call result temp must hold the
        // full-width raw function pointer (the old null collapsed to int32_t,
        // truncating the x64 address before the calli could cast it).
        // VALUETYPE (0x11) returns: the runtime-struct slot kinds.
        //   * System.DateTime / System.TimeSpan -> "dt" / "ts" (A3: the old
        //     null declared the temp int32_t, so passing a helper's DateTime
        //     result to a typed runtime entry failed to compile).
        //   * System.Guid / System.Decimal -> "guid" / "dec" (16-byte value
        //     structs; a 4-byte slot truncates the value and rejects the
        //     assignment outright).
        //   * System.DateTimeOffset / System.Numerics.BigInteger -> "dto" / "bi"
        //     (same truncation, same bridge).
        // The owner is decoded from the signature's TypeDefOrRef coded index.
        if (ret == 0x11)
        {
            var vtHandle = sig.ReadTypeHandle();
            var vtName = vtHandle.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)vtHandle),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)vtHandle),
                _ => null,
            };
            return vtName switch
            {
                "System.DateTime" => "dt",
                "System.TimeSpan" => "ts",
                "System.Guid" => "guid",
                "System.Decimal" => "dec",
                "System.DateTimeOffset" => "dto",
                "System.Numerics.BigInteger" => "bi",
                _ => null,
            };
        }
        // ptr-pinvoke: 0x0F (ELEMENT_TYPE_PTR) is the unsafe-pointer return --
        // the full-width pointer-width integer slot ("ptr" -> intptr_t), the same
        // carrier an IntPtr return uses. Without it the result temp stayed int32_t
        // and the x64 address truncated before any deref (measured on net48).
        return ret is 0x0A or 0x0B ? "l"
            : ret is 0x0C or 0x0D ? "d"
            : ret is 0x17 or 0x18 ? "ptr"
            : ret is 0x1B ? "p"
            : ret is 0x0F ? "ptr"
            : null;
    }

    // P2-HARDEN #3: the RESULT WIDTH of a delegate Invoke call (the
    // DelegateInvokeExpr ResultKind channel). The P-D2 #1 multicast helper /
    // ref-ABI icall returns the full-width leaf type; a result stored on a
    // COMPILER temp must be declared that width. Decodes the Invoke signature's
    // return element type: I8/U8 -> "l", R8 -> "d", R4 -> "float" (the helper
    // returns float for a float delegate, not double). null = int32-family.
    // BCL generic delegates (Func`N<...>) carry a VAR/MVAR return substituted
    // through the parent TypeSpec's generic args.
    static string? DelegateInvokeResultKind(MetadataReader? md, int token)
    {
        if (md == null) return null;
        var handle = MetadataTokens.Handle(token);
        BlobHandle sigHandle;
        var genArgs = Array.Empty<byte>();
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            sigHandle = md.GetMethodDefinition((MethodDefinitionHandle)handle).Signature;
        }
        else if (handle.Kind == HandleKind.MemberReference)
        {
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            sigHandle = mr.Signature;
            if (mr.Parent.Kind == HandleKind.TypeSpecification)
                genArgs = DelegateInvokeTypeSpecGenBytes(md, (TypeSpecificationHandle)mr.Parent);
        }
                else
                {
            return null; // MethodSpec etc.: int32 default
        }
        var sig = md.GetBlobReader(sigHandle);
        var header = sig.ReadSignatureHeader();
        if (header.IsGeneric) _ = sig.ReadCompressedInteger(); // generic param count
        _ = sig.ReadCompressedInteger();                       // param count
        var ret = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        if (ret is 0x13 or 0x1E) // VAR/MVAR: resolve through the TypeSpec genargs
        {
            var idx = sig.ReadCompressedInteger();
            if (idx < genArgs.Length) ret = genArgs[idx];
        }
        return ret is 0x0A or 0x0B ? "l" : ret is 0x0D ? "d" : ret is 0x0C ? "float" : null;
    }

    // P2-HARDEN #3: read a generic delegate TypeSpec's instantiation args as
    // their LEADING type bytes (mirrors CodeGenerator.DelegateTypeSpecGenericArgs
    // -- the width mapping only needs the element byte).
    static byte[] DelegateInvokeTypeSpecGenBytes(MetadataReader md, TypeSpecificationHandle handle)
    {
        var spec = md.GetTypeSpecification(handle);
        var sig = md.GetBlobReader(spec.Signature);
        if (sig.ReadByte() != 0x15) return Array.Empty<byte>();
        sig.ReadByte(); // CLASS/VALUETYPE marker
        _ = sig.ReadTypeHandle();
        var count = sig.ReadCompressedInteger();
        var args = new byte[count];
        for (var i = 0; i < count; ++i)
            args[i] = ConsumeDelegateTypeLead(md, ref sig);
        return args;
    }

    // P2-HARDEN #3: consume one type byte + trailing payload; return the
    // leading byte (mirrors CodeGenerator.ConsumeDelegateTypeByte).
    static byte ConsumeDelegateTypeLead(MetadataReader md, ref BlobReader sig)
    {
        var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        switch (b)
        {
            case 0x10: return ConsumeDelegateTypeLead(md, ref sig); // BYREF
            case 0x11 or 0x12 or 0x40: _ = sig.ReadTypeHandle(); break;
            case 0x1D or 0x1F: _ = ConsumeDelegateTypeLead(md, ref sig); break; // SZARRAY element
            case 0x13 or 0x1E: _ = sig.ReadCompressedInteger(); break;          // VAR/MVAR index
            case 0x15:                                                        // GenericInst
                sig.ReadByte();
                _ = sig.ReadTypeHandle();
                var n = sig.ReadCompressedInteger();
                for (var i = 0; i < n; i++) _ = ConsumeDelegateTypeLead(md, ref sig);
                break;
            case 0x14:                                                        // multidim ARRAY
                _ = ConsumeDelegateTypeLead(md, ref sig);
                _ = sig.ReadCompressedInteger(); // rank
                var sizes = sig.ReadCompressedInteger();
                for (var i = 0; i < sizes; i++) _ = sig.ReadCompressedInteger();
                var lob = sig.ReadCompressedInteger();
                for (var i = 0; i < lob; i++) _ = sig.ReadCompressedInteger();
                break;
            case 0x0F: _ = ConsumeDelegateTypeLead(md, ref sig); break;        // PTR pointee
            case 0x1B:                                                        // FNPTR
                sig.ReadByte(); // calling convention
                var nf = sig.ReadCompressedInteger();
                _ = ConsumeDelegateTypeLead(md, ref sig); // return type
                for (var i = 0; i < nf; i++) _ = ConsumeDelegateTypeLead(md, ref sig);
                break;
        }
        return b;
    }

    // P-G3: whether the method token's DECLARING generic definition is in the
    // active share set AND the call is a CLOSED SHARED INSTANCE whose clones
    // return the T slots as Il2CppObject*. The all-int default instantiation
    // (Box`1<Int32> -- the definition itself) is NOT collapsed onto the
    // object clone (CollapseInstantiation keeps it per-instance), so its
    // calls return the raw int32 slot and must NOT be labeled object-returning
    // (genericvirtdeep regression: `Il2CppObject* t = Box_1_int_Get(...)`
    // with an int32_t return -> ill-formed). A MethodDef token likewise names
    // the definition's own default-int method -- never the shared clone.
    static bool SharedDefReturningObject(MetadataReader md, Handle callHandle)
    {
        return callHandle.Kind switch
        {
            // P-G3: MethodDef = the definition's default-int body (all-int
            // instantiation is per-instance); its VAR returns are int32.
            HandleKind.MethodDefinition => false,
            // P-G3 extended (GN-8): a MemberRef on a closed GenericInst TypeSpec
            // whose generic DEFINITION is a USER TypeDefinition (shared or not)
            // and whose args are not the all-int default emits a per-instance
            // clone whose VAR-return ABI is the object slot (RefVar renders any
            // non-"int" closed arg as Il2CppObject*, EnsureClone RefVar). The
            // shared-only gate missed NON-shared user clones and typed the
            // call-site result temp int32_t -- invalid conversion against the
            // emitted Il2CppObject* return. Shared user defs are unchanged
            // (shared set is a subset of the in-assembly TypeDefinitions).
            // BCL generic defs arrive as TypeReference (cross-assembly) and have
            // no per-instance clone -- they keep the legacy gate (never
            // object-typed through this branch).
            HandleKind.MemberReference => MemberRefReturnSlotIsRef(md, (MemberReferenceHandle)callHandle),
            // D5: a MethodSpec call names a generic method of a SHARED
            // definition (PickUser2::Pick<int> -- the arity-0 def's method-level
            // share body). The VAR/MVAR-return guard at the callers means this
            // only fires for those signatures; the shared clone returns the
            // boxed object slot. Cross-assembly (MemberRef) MethodSpecs stay BCL.
            HandleKind.MethodSpecification => md.GetMethodSpecification((MethodSpecificationHandle)callHandle).Method.Kind
                    == HandleKind.MethodDefinition
                && GenericUsageAnalyzer.SharedGenericDefs
                    ?.Contains(md.GetFullName(md.GetMethodDefinition((MethodDefinitionHandle)md.GetMethodSpecification((MethodSpecificationHandle)callHandle).Method).GetDeclaringType())) == true,
            _ => false,
        };
    }

    // GN-16: whether a closed GenericInst TypeSpec collapses onto the SHARED
    // OBJECT clone -- the SAME verdict the code generator computes
    // (GenericUsageAnalyzer.CollapseInstantiation) so these return-slot
    // classifiers agree with the ABI the clone is actually emitted with. A
    // collapsing definition (in the active share set, no all-int default, no
    // array argument, no user value-type argument) emits ONE clone whose EVERY
    // T slot is Il2CppObject* (the shared object ABI). ANY VAR return of its
    // methods is therefore the object slot, regardless of the per-argument
    // value/reference classification. Without this gate Pair<int,string> (all
    // args non-value/non-array -> collapse) had GetFirst() typed by the
    // per-arg refinement (index 0 arg "int" -> value slot) while the emitted
    // clone returns Il2CppObject* (the GN-14 (c) paircase regression). The
    // decode mirrors MethodBodyReader.TryClassGenericInstanceKind's defName +
    // per-arg suffix tokens so both sides decide identically; an undecodable
    // arg returns false (the caller keeps the legacy per-arg logic).
    static bool InstantiationCollapses(MetadataReader md, TypeSpecificationHandle tsHandle)
    {
        var sig = md.GetBlobReader(md.GetTypeSpecification(tsHandle).Signature);
        if (sig.ReadByte() != 0x15) return false; // not GENERICINST
        sig.ReadByte();                           // CLASS/VALUETYPE marker
        var defHandle = sig.ReadTypeHandle();
        var defName = defHandle.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
            _ => null,
        };
        if (defName == null) return false;
        var argc = sig.ReadCompressedInteger();
        var args = new string[argc];
        for (var i = 0; i < argc; i++)
        {
            var s = MethodBodyReader.SimpleGenericArgSuffix(md, ref sig, out _);
            if (s == null) return false; // undecodable argument: keep legacy logic
            args[i] = s;
        }
        return GenericUsageAnalyzer.CollapseInstantiation(defName, args);
    }

    // GN-14 (c): whether a VAR (0x13) return is the OBJECT slot -- the shared
    // object clone (all args "object") or a reference argument; a per-instance
    // clone's VALUE argument keeps the raw value slot (so no unbox bridge is
    // applied). Undecodable shapes keep the legacy object classification.
    static bool VarReturnIsObjectSlot(MetadataReader md, int token)
    {
        var h = MetadataTokens.Handle(token);
        if (h.Kind == HandleKind.MethodSpecification) return true;
        if (h.Kind != HandleKind.MemberReference) return true;
        var mr = md.GetMemberReference((MemberReferenceHandle)h);
        if (mr.Parent.Kind != HandleKind.TypeSpecification) return true;
        var par = (TypeSpecificationHandle)mr.Parent;
        // GN-16: a collapsing (shared object) clone returns the object slot for
        // EVERY VAR, ahead of the per-argument refinement below.
        if (InstantiationCollapses(md, par)) return true;
        var kind = MethodBodyReader.TryClassGenericInstanceKind(md, par);
        if (kind == null) return true;
        var ip = kind[2..].Split(':');
        if (ip.Length < 2) return true;
        var suffix = ip[1];
        string[]? parts = ip.Length > 2 ? ip[2..] : null;
        if (suffix == "object" || (parts != null && parts.Length > 0 && parts.All(p => p == "object")))
            return true;
        var rs = md.GetBlobReader(mr.Signature);
        var hdr = rs.ReadSignatureHeader();
        if (hdr.IsGeneric) _ = rs.ReadCompressedInteger();
        _ = rs.ReadCompressedInteger();
        var rb = MethodSignatureCoder.SkipCustomModifiers(ref rs, rs.ReadByte());
        if (rb != 0x13) return true;
        var retIdx = rs.ReadCompressedInteger();
        return TypeDef.CloneArgSlotIsRef(suffix, parts, retIdx);
    }

    // GN-14 (c): the call-side per-argument refinement of the MemberReference
    // branch above. A closed GenericInst TypeSpec whose generic DEFINITION is a
    // USER reference class renders its VAR-return ABI as the object slot ONLY
    // for a REFERENCE argument; an arity>=2 clone with a VALUE argument at the
    // return's VAR index keeps the raw value slot (mirrors
    // TypeDef.CloneArgSlotIsRef on the declaration side). arity-1 / an
    // un-decodable shape keep the legacy whole-suffix answer (true -- the
    // historical object classification).
    static bool MemberRefReturnSlotIsRef(MetadataReader md, MemberReferenceHandle mrh)
    {
        var mr = md.GetMemberReference(mrh);
        if (mr.Parent.Kind != HandleKind.TypeSpecification) return false;
        var par = (TypeSpecificationHandle)mr.Parent;
        // GN-16: a collapsing (shared object) clone returns the object slot for
        // EVERY VAR, ahead of the per-argument refinement below -- the shared
        // object clone stores all T slots as Il2CppObject* regardless of the
        // closed argument's value/reference classification. Without this gate
        // Pair<int,string> (collapse) had GetFirst() typed by the per-arg rule
        // (index 0 "int" -> value slot) against the emitted Il2CppObject*
        // return (the paircase regression).
        if (InstantiationCollapses(md, par)) return true;
        if (GenericInstAllInt(md, par) || !GenericInstDefIsUserRefClass(md, par)) return false;
        // Decode the method's return VAR (0x13) index; a non-VAR return on a
        // ref-class owner keeps the legacy object classification.
        var rs = md.GetBlobReader(mr.Signature);
        var hdr = rs.ReadSignatureHeader();
        if (hdr.IsGeneric) _ = rs.ReadCompressedInteger();
        _ = rs.ReadCompressedInteger(); // parameter count
        var rb = MethodSignatureCoder.SkipCustomModifiers(ref rs, rs.ReadByte());
        if (rb != 0x13) return true;
        var retIdx = rs.ReadCompressedInteger();
        var kind = MethodBodyReader.TryClassGenericInstanceKind(md, par);
        if (kind == null) return true;
        var ip = kind[2..].Split(':');
        if (ip.Length < 2) return true;
        var suffix = ip[1];
        string[]? parts = ip.Length > 2 ? ip[2..] : null;
        return TypeDef.CloneArgSlotIsRef(suffix, parts, retIdx);
    }

    // P-G3 D1: whether a closed generic-instance TypeSpec's arguments are ALL the
    // int32 default (0x08) -- the M3 definition-default instantiation that
    // CollapseInstantiation refuses to share. Mirrors
    // CodeGenerator.SharedCallInstanceAllInt's decode (same blob shape).
    static bool GenericInstAllInt(MetadataReader md, TypeSpecificationHandle tsHandle)
    {
        var sig = md.GetBlobReader(md.GetTypeSpecification(tsHandle).Signature);
        if (sig.ReadByte() != 0x15) return false; // not a GenericInst
        sig.ReadByte();                          // CLASS/VALUETYPE marker
        sig.ReadTypeHandle();
        var argc = sig.ReadCompressedInteger();
        for (var i = 0; i < argc; i++)
        {
            var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
            if (b != 0x08) return false; // only the exact int32 default counts
        }
        return true;
    }

    // GN-8: whether a closed GenericInst TypeSpec's GENERIC DEFINITION is a USER
    // REFERENCE CLASS in this assembly -- the exact clone scope of EnsureClone
    // (CodeGenerator:577 `if (def.IsInterface || def.IsValueType) return;`). Only
    // these emit per-instance clones whose VAR-return ABI is the object slot
    // (RefVar renders every non-"int" closed arg as Il2CppObject*). Interfaces
    // and value types (struct/enum) are explicitly excluded -- their generic
    // VAR returns are NOT object-typed and labeling them "r" would mis-type the
    // caller. BCL generic defs arrive as TypeReference (cross-assembly) and
    // have no per-instance clone -- they must never be object-typed through
    // this gate.
    static bool GenericInstDefIsUserRefClass(MetadataReader md, TypeSpecificationHandle tsHandle)
    {
        var sig = md.GetBlobReader(md.GetTypeSpecification(tsHandle).Signature);
        if (sig.ReadByte() != 0x15) return false; // not a GenericInst
        sig.ReadByte();                          // CLASS/VALUETYPE marker
        var defHandle = sig.ReadTypeHandle();
        if (defHandle.Kind != HandleKind.TypeDefinition) return false;
        var td = md.GetTypeDefinition((TypeDefinitionHandle)defHandle);
        if ((td.Attributes & System.Reflection.TypeAttributes.Interface) != 0) return false;
        var baseName = td.BaseType.IsNil ? "System.Object" : td.BaseType.Kind switch
        {
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)td.BaseType),
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)td.BaseType),
            _ => null,
        };
        return baseName != "System.ValueType" && baseName != "System.Enum";
    }

    // P-G3 D1: whether an objTypes entry names a BOXED primitive VALUE slot
    // (a shared clone's value T return / a `box` result) -- the slot holds an
    // Il2CppObject* whose payload must be UNBOXED, never dereferenced as an
    // int32 address. User value types / enums are intentionally not listed
    // (their managed type names are also reference-receiver hints; the
    // primitive box set is the reachable shared-value surface).
    static bool IsBoxedValueSlotName(string name) => name is
        "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64"
        or "System.Single" or "System.Double" or "System.Boolean" or "System.Char"
        or "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16";

    // D5: whether a TypeDef is a USER VALUE TYPE (user struct OR user enum).
    // The model's IsValueType flag is set ONLY for a System.ValueType base
    // (MetadataLoader.cs:62); a user ENUM's base is System.Enum, so
    // IsValueType is false for enums. Both need the concrete box/unbox bridge
    // (their {T}_class descriptors / int32 enum carrier exist).
    static bool IsUserValueType(TypeDef t) =>
        t.IsValueType || t.BaseTypeName == "System.Enum";

    // P-G3 R-E: the per-explicit-parameter byref-VAR flags of a call to a
    // SHARED generic method (SwapRef<T> -> [true, true]). The shared object
    // ABI declares those params Il2CppObject** and the caller must bridge
    // VALUE slots (box -> &temp -> unbox). Returns null for non-shared calls
    // (genlist's Id<T> etc. keep the legacy M6 path untouched). Decode the
    // underlying MethodDefinition signature (MethodSpec on a MethodDef, or a
    // plain MethodDef token); MemberRef MethodSpecs (cross-assembly) are
    // out of the current shared surface.
    static bool[]? SharedByrefVarSlots(MetadataReader md, int token)
    {
        if (md == null) return null;
        var h = MetadataTokens.Handle(token);
        MethodDefinitionHandle mdh;
        switch (h.Kind)
        {
            case HandleKind.MethodSpecification:
                var ms = md.GetMethodSpecification((MethodSpecificationHandle)h);
                if (ms.Method.Kind != HandleKind.MethodDefinition) return null;
                mdh = (MethodDefinitionHandle)ms.Method;
                break;
            case HandleKind.MethodDefinition:
                mdh = (MethodDefinitionHandle)h;
                break;
            default:
                return null;
        }
        var dm = md.GetMethodDefinition(mdh);
        if (GenericUsageAnalyzer.SharedGenericDefs?.Contains(md.GetFullName(dm.GetDeclaringType())) != true)
            return null;
        var (_, _, byrefVar, _, _) = MethodSignatureCoder.DecodeVarFlags(md, dm.Signature);
        return byrefVar;
    }

    // D5: the box/unbox bridge pair for a SHARED method-level generic method's
    // byref MVAR param, keyed on the MethodSpec's CONCRETE generic argument
    // (the value slot's managed type: int32 -> il2cpp_box_int32 /
    // il2cpp_unbox_int32, double -> icall_box_double / icall_unbox_double, ...).
    // The old bridge hardcoded int32; a double slot would box/unbox only its
    // low 32 bits. GN-1 (ruling (2)): an UNKNOWN concrete type (user struct / enum /
    // GenericInst value type) is now LOUD-rejected instead of silently falling
    // to the int32 pair -- the legal System.Int32 branch keeps the legacy
    // int32 pair explicitly. Returns null for non-shared / non-byref shapes
    // (caller keeps the zero-copy pass-through).
    // D5: the shared-byref box/unbox bridge result. For the PRIMITIVE (scalar)
    // concrete arguments it carries the simple (boxFn, boxDeref, unboxFn,
    // unboxKind) tuple the caller historically built. For a USER VALUE TYPE
    // (user struct / user enum) it carries the concrete type's metadata token
    // and C++ name; the caller then emits box_{token}/unbox_{token} CallExprs
    // (the SAME CodeGenerator handlers the per-instance P25b path uses for
    // user structs/enums -- byte-exact payload copy + cast unbox).
    sealed record SharedByrefBridge(
        string BoxFn, string BoxDeref, string UnboxFn, string UnboxKind,
        int? UserValueToken = null, string? UserStructCpp = null, bool IsEnum = false)
    {
        public bool IsUserValue => UserValueToken != null;
    }

    // The `default(T)` value written back when a shared object-ABI callee
    // RESET its byref T slot (the box came back null). MEASURED on net48
    // (swapfield `Bump(ref o.I)`): the caller's field ends up 0, not a
    // NullReferenceException. Keyed on the box/unbox kind code the bridge
    // already carries, so every primitive family (and the reference family)
    // gets a well-formed literal.
    static Expr SharedByrefDefaultValue(string unboxKind) => unboxKind switch
    {
        "d" => new DoubleConstExpr { Value = 0.0 },
        "l" or "ul" => new Int64ConstExpr { Value = 0 },
        "b" or "c" or "i" or "u1" or "u2" or "ui" => new IntConstExpr { Value = 0 },
        "r" => new NullExpr(),
        var us when us.StartsWith("us:", StringComparison.Ordinal) => new ValueTypeNewExpr
        {
            TypeFullName = us.Substring(3),
            CtorToken = 0,
        },
        _ => new IntConstExpr { Value = 0 },
    };

    static SharedByrefBridge? SharedByrefConcreteBridge(
        MetadataReader md, int token, int paramIndex, IReadOnlyList<TypeDef>? types)
    {
        var h = MetadataTokens.Handle(token);
        if (h.Kind != HandleKind.MethodSpecification) return null;
        var ms = md.GetMethodSpecification((MethodSpecificationHandle)h);
        if (ms.Method.Kind != HandleKind.MethodDefinition) return null;
        var dm = md.GetMethodDefinition((MethodDefinitionHandle)ms.Method);
        if (GenericUsageAnalyzer.SharedGenericDefs?.Contains(md.GetFullName(dm.GetDeclaringType())) != true) return null;
        var mvIdx = ByrefParamMvarIndex(md, dm, paramIndex);
        if (mvIdx < 0) return null;
        var concrete = MethodSpecGenArgFullName(md, ms.Signature, mvIdx);
        // D5: a USER value type concrete argument (user struct / user enum)
        // now gets a real shared byref box/unbox bridge instead of LOUD. The
        // box allocates an object with the struct's OWN generated descriptor
        // (&{T}_class -- byte-exact payload copy) and the unbox reads the
        // payload back as a {T} struct value; the callee (the shared object
        // ABI) swaps the two object POINTERS, which round-trips the struct
        // bytes exactly (net48: the byte-exact struct swap). The {T}_class
        // descriptor must exist (bounded: user types in this assembly).
        var structDef = concrete != null && types != null
            ? types.FirstOrDefault(t => t.MatchesFullName(concrete))
            : null;
        if (concrete != null && structDef != null && IsUserValueType(structDef))
        {
            var typeToken = MethodSpecGenArgToken(md, ms.Signature, mvIdx);
            var cpp = TypeEmitter.TypeCppName(structDef);
            // Struct or enum? Enums have the System.Enum base: box via
            // icall_box_enum (the value is an int32 carrier widened to int64),
            // unbox via icall_unbox_long narrowed back to int32 -- the enum
            // local slot is int32_t. User structs box/unbox their own
            // descriptor ({T}_class / il2cpp_unbox_user_struct).
            if (structDef.BaseTypeName == "System.Enum")
                return new SharedByrefBridge("", "", "", "i", typeToken, cpp, IsEnum: true);
            return new SharedByrefBridge("", "", "", "us:" + cpp, typeToken, cpp, IsEnum: false);
        }
        // Primitives keep the exact legacy scalar bridge.
        return concrete switch
        {
            "System.Double" => new SharedByrefBridge("il2cpp::runtime::icall_box_double", "deref:d", "il2cpp::runtime::icall_unbox_double", "d"),
            "System.Int64" => new SharedByrefBridge("il2cpp::runtime::icall_box_long", "deref:l", "il2cpp::runtime::icall_unbox_long", "l"),
            "System.Boolean" => new SharedByrefBridge("il2cpp::runtime::icall_box_bool", "deref:b", "il2cpp::runtime::icall_unbox_bool", "b"),
            "System.Char" => new SharedByrefBridge("il2cpp::runtime::icall_box_char", "deref:c", "il2cpp::runtime::icall_unbox_char", "c"),
            "System.Byte" => new SharedByrefBridge("il2cpp::runtime::icall_box_byte", "deref:u1", "il2cpp::runtime::icall_unbox_byte", "b"),
            "System.SByte" => new SharedByrefBridge("il2cpp::runtime::icall_box_sbyte", "deref:i", "il2cpp::runtime::icall_unbox_sbyte", "b"),
            "System.Int16" => new SharedByrefBridge("il2cpp::runtime::icall_box_short", "deref:u2", "il2cpp::runtime::icall_unbox_short", "b"),
            "System.UInt16" => new SharedByrefBridge("il2cpp::runtime::icall_box_ushort", "deref:u2", "il2cpp::runtime::icall_unbox_ushort", "c"),
            "System.UInt32" => new SharedByrefBridge("il2cpp::runtime::icall_box_uint", "deref:ui", "il2cpp::runtime::icall_unbox_uint", "i"),
            "System.UInt64" => new SharedByrefBridge("il2cpp::runtime::icall_box_ulong", "deref:ul", "il2cpp::runtime::icall_unbox_ulong", "l"),
            "System.Single" => new SharedByrefBridge("il2cpp::runtime::icall_box_float", "deref:d", "il2cpp::runtime::icall_unbox_float", "d"),
            // GN-1 (ruling (2)): the legal Int32 branch -- the shared ABI's dedicated
            // box helper (takes the null class-slot first arg, see the caller).
            "System.Int32" => new SharedByrefBridge("il2cpp::il2cpp_box_int32", "deref:i", "il2cpp::il2cpp_unbox_int32", "i"),
            // GN-1 (ruling (2)): unknown concrete argument (nested GenericInst
            // value type / undecodable shape) is NO LONGER silently
            // boxed/unboxed as int32 -- that would corrupt the value.
            // LOUD-reject: the fold catch turns the enclosing method into the
            // M5 stub (runtime throws a managed NotSupportedException). The
            // per-instance (P25b) path or an explicit Int32 instantiation are
            // the supported ways to use a struct/enum T.
            var other => throw new NotSupportedException(
                $"shared byref-MVAR generic argument '{other ?? "<?>"}' has no box/unbox bridge " +
                "(only Int32 + primitives + user value types share the byref bridge); nested GenericInst value types must use per-instance generic (P25b) or explicit Int32 specialization"),
        };
    }

    // D5: the METHOD-level generic parameter index (!!i) of a BYREF-wrapped
    // generic parameter at the given explicit-param index of a method
    // definition, or -1 when the param is not a byref MVAR. The shared object
    // ABI only shares methods whose MVAR params are byref T slots.
    static int ByrefParamMvarIndex(MetadataReader md, MethodDefinition dm, int paramIndex)
    {
        var sig = md.GetBlobReader(dm.Signature);
        var hdr = sig.ReadSignatureHeader();
        if (hdr.IsGeneric) sig.ReadCompressedInteger();
        var pc = sig.ReadCompressedInteger();
        var ret = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        if (ret is 0x13 or 0x1E) sig.ReadCompressedInteger();
        else if (ret is 0x11 or 0x12 or 0x40) sig.ReadTypeHandle();
        for (var i = 0; i < pc; i++)
        {
            var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
            var mvar = -1;
            if (b == 0x10) // BYREF wrapper
            {
                var pointee = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
                if (pointee == 0x1E) mvar = sig.ReadCompressedInteger();
            }
            if (i == paramIndex) return mvar;
        }
        return -1;
    }

    // P-G4 (2): whether a shared-byref ARGUMENT that is a FIELD ADDRESS (ldflda
    // -> FieldAddrExpr / ldsflda -> StaticFieldAddrExpr) targets a VALUE slot.
    // A REFERENCE-typed field's address is &f == Il2CppObject** -- the shared
    // callee's Il2CppObject** byref slot matches it directly (zero-copy pass-
    // through is CORRECT). A VALUE slot field's address is &f == int32_t*/...
    // -- ABI-mismatched, and no field-write-back bridge exists yet. The audit
    // ruling (P-G4-AUDIT-2.md section 11.2): only the value-slot shape is loudly
    // rejected; reference field addresses pass through. An unresolvable field
    // is treated as a value slot (conservative reject). Field kind is decoded
    // from the metadata FIELD signature (mirrors IsRefField / CodeGenerator.
    // StaticFieldIsReference), NOT from FieldDef.TypeKind -- static fields are
    // carried type-less in the model (FieldDef.cs:44-52), so the metadata
    // signature byte is the authoritative reference/value source.
    static bool SharedByrefFieldArgSlotIsValue(MetadataReader? md, IReadOnlyList<TypeDef>? types, Expr arg)
    {
        if (md == null) return true; // no metadata -> conservative reject
        string owner;
        string fieldCpp;
        switch (arg)
        {
            case FieldAddrExpr fa:
                owner = fa.OwnerFullName;
                fieldCpp = fa.FieldCppName;
                break;
            case StaticFieldAddrExpr sf:
                var dot = sf.FieldName.LastIndexOf('.');
                if (dot <= 0) return true; // unresolvable -> conservative reject
                owner = sf.FieldName[..dot];
                fieldCpp = CppFieldName(sf.FieldName[(dot + 1)..]);
                break;
            default:
                return false; // not a field address -> not this LOUD path
        }
        return !FieldCppIsReference(md, owner, fieldCpp);
    }

    // Whether the field identified by (owner full name, SANITIZED C++ field
    // name) is a REFERENCE-typed slot (Il2CppObject*). Signature byte test:
    // 0x0E String / 0x12 Class / 0x1C Object / 0x15 GenericInst / 0x1D SZARRAY
    // / 0x14 ARRAY -> reference; everything else (0x08 I4, 0x0A I8, 0x0D R8,
    // 0x11 VALUETYPE, 0x13 VAR) -> value. A field that cannot be resolved is
    // reported as NOT reference (the caller then loud-rejects -- conservative,
    // "prefer specialization").
    static bool FieldCppIsReference(MetadataReader md, string ownerFullName, string fieldCppName)
    {
        // P-G5 FINDING-2: match the OWNER by its FULL name. GetFullName on a
        // TypeDefinitionHandle returns the componentized full name (nested
        // types are "Outer+Inner", MetadataExtensions.cs:18-28); truncating to
        // the last '+' segment ("Inner") could never match a nested owner, so a
        // nested type's reference-typed field was wrongly reported as a value
        // slot (over-rejected, safe but FALSE-NEGATIVE). Match the full owner
        // name verbatim; a generic-instantiation owner string (ResolveField's
        // GenericInstantiationName, e.g. "Box`1_string") still never matches a
        // TypeDefinition full name -> stays a value slot (conservative reject).
        foreach (var tdh in md.TypeDefinitions)
        {
            if (md.GetFullName(tdh) != ownerFullName) continue;
            var td = md.GetTypeDefinition(tdh);
            foreach (var fh in td.GetFields())
            {
                var fd = md.GetFieldDefinition(fh);
                if (CppFieldName(md.GetString(fd.Name)) != fieldCppName) continue;
                var sig = md.GetBlobReader(fd.Signature);
                if (sig.ReadByte() != 0x06) return false; // not a FIELD sig
                var typeByte = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
                return typeByte is 0x0E or 0x12 or 0x1C or 0x15 or 0x1D or 0x14;
            }
            return false; // owner found, field missing -> not reference
        }
        return false; // owner not found -> not reference (conservative)
    }

    // C++ member-name sanitize (same rule set as the Fold-local SanitizeField
    // -- illegal C++ identifier characters -> '_'; a field that sanitizes to
    // _obj is renamed to _obj_field, the object header member). Class-level
    // copy so the (2) field-address helper can classify field slots outside
    // Fold's scope.
    static string CppFieldName(string name)
    {
        var s = TypeEmitter.SanitizeIdentChars(
            string.Join("_", name.Split('.', '+', '`', '<', '>', ',', '[', ']')));
        return s == "_obj" ? "_obj_field" : s;
    }

    // P-G3: the generic DEFINITION FullName of a closed generic instance TypeSpec
    // (Pair`2<int,string> -> "Pair`2"), reading the GenericInst's def handle.
    static string? GenericInstDefName(MetadataReader md, TypeSpecificationHandle tsHandle)
    {
        var sig = md.GetBlobReader(md.GetTypeSpecification(tsHandle).Signature);
        if (sig.ReadByte() != 0x15) return null; // not a GenericInst
        sig.ReadByte();                          // CLASS/VALUETYPE marker
        var defHandle = sig.ReadTypeHandle();
        return defHandle.Kind switch
        {
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
            _ => null,
        };
    }

    // P-G3 D1: the CONCRETE generic-argument type of a SHARED clone's VAR/MVAR
    // return (Pair`2<int,string>.GetFirst -> "System.Int32", Box`1<string>.Get
    // -> "System.String"). The shared clone returns the T slot as a boxed
    // Il2CppObject*; the caller's downstream value consumption needs the
    // concrete T to unbox/dispatch. Decode the MemberRef signature's return
    // VAR index, then read that TypeSpec GenericInst argument. Returns null
    // for non-instance tokens, non-VAR returns, open (VAR/MVAR) arguments, or
    // undecodable shapes (the caller keeps the existing slot semantics).
    static string? SharedReturnConcreteType(MetadataReader md, int token)
    {
        var h = MetadataTokens.Handle(token);
        // D5: a MethodSpec call to a SHARED method-level generic method
        // (PickUser2::Pick<System.Int32>) -- the MVAR return's CONCRETE T is
        // the instantiation's genArg, not a MemberRef parent TypeSpec.
        if (h.Kind == HandleKind.MethodSpecification)
            return MethodSpecMvarReturnConcreteType(md, (MethodSpecificationHandle)h);
        if (h.Kind != HandleKind.MemberReference) return null;
        var mr = md.GetMemberReference((MemberReferenceHandle)h);
        if (mr.Parent.Kind != HandleKind.TypeSpecification) return null;
        var par = (TypeSpecificationHandle)mr.Parent;
        // Only closed SHARED instances (MemberRef-on-TypeSpec, non-all-int) --
        // the same gate as SharedDefReturningObject.
        if (GenericInstAllInt(md, par)) return null;
        var gdn = GenericInstDefName(md, par);
        if (gdn == null || GenericUsageAnalyzer.SharedGenericDefs?.Contains(gdn) != true) return null;
        var sig = md.GetBlobReader(mr.Signature);
        sig.ReadByte();                    // calling convention
        sig.ReadCompressedInteger();       // param count
        var ret = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        int varIdx;
        if (ret is 0x13 or 0x1E) varIdx = sig.ReadCompressedInteger(); else return null;
        var ts = md.GetBlobReader(md.GetTypeSpecification(par).Signature);
        if (ts.ReadByte() != 0x15) return null; // not a GenericInst
        ts.ReadByte();                    // CLASS/VALUETYPE marker
        ts.ReadTypeHandle();
        var argc = ts.ReadCompressedInteger();
        var argBytes = new byte[argc];
        for (var i = 0; i < argc; i++)
            argBytes[i] = MethodSignatureCoder.SkipCustomModifiers(ref ts, ts.ReadByte());
        if ((uint)varIdx >= (uint)argBytes.Length) return null;
        var b = argBytes[varIdx];
        if (b is 0x11 or 0x12)
        {
            // Re-read the concrete argument's TypeDefOrRef full name.
            var ts2 = md.GetBlobReader(md.GetTypeSpecification(par).Signature);
            if (ts2.ReadByte() != 0x15) return null;
            ts2.ReadByte();
            ts2.ReadTypeHandle();
            var argc2 = ts2.ReadCompressedInteger();
            for (var i = 0; i < varIdx; i++)
            {
                var prior = MethodSignatureCoder.SkipCustomModifiers(ref ts2, ts2.ReadByte());
                if (prior is 0x11 or 0x12) _ = ts2.ReadTypeHandle();
            }
            _ = MethodSignatureCoder.SkipCustomModifiers(ref ts2, ts2.ReadByte());
            var th = ts2.ReadTypeHandle();
            return th.Kind switch
            {
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)th),
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)th),
                _ => null,
            };
        }
        return b switch
        {
            0x02 => "System.Boolean",
            0x03 => "System.Char",
            0x04 => "System.SByte",
            0x05 => "System.Byte",
            0x06 => "System.Int16",
            0x07 => "System.UInt16",
            0x08 => "System.Int32",
            0x09 => "System.UInt32",
            0x0A => "System.Int64",
            0x0B => "System.UInt64",
            0x0C => "System.Single",
            0x0D => "System.Double",
            0x0E => "System.String",
            0x1C => "System.Object",
            _ => null, // arrays/nested-generic/open-VAR: keep the legacy semantics
        };
    }

    // D5: the METADATA TOKEN of the concrete generic-argument type of a
    // SHARED clone's VAR/MVAR return (Box<Pt>.Get -> the Cases.Pt TypeDef
    // token, for the unbox_{token} emission path). Mirrors
    // SharedReturnConcreteType's decode but keeps the handle token. Null for
    // non-VALUETYPE/CLASS args / undecodable shapes.
    static int? SharedReturnConcreteToken(MetadataReader md, int token)
    {
        var h = MetadataTokens.Handle(token);
        if (h.Kind == HandleKind.MethodSpecification)
            return MethodSpecMvarReturnConcreteToken(md, (MethodSpecificationHandle)h);
        if (h.Kind != HandleKind.MemberReference) return null;
        var mr = md.GetMemberReference((MemberReferenceHandle)h);
        if (mr.Parent.Kind != HandleKind.TypeSpecification) return null;
        var par = (TypeSpecificationHandle)mr.Parent;
        if (GenericInstAllInt(md, par)) return null;
        var gdn = GenericInstDefName(md, par);
        if (gdn == null || GenericUsageAnalyzer.SharedGenericDefs?.Contains(gdn) != true) return null;
        var sig = md.GetBlobReader(mr.Signature);
        sig.ReadByte();                    // calling convention
        sig.ReadCompressedInteger();       // param count
        var ret = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        int varIdx;
        if (ret is 0x13 or 0x1E) varIdx = sig.ReadCompressedInteger(); else return null;
        var ts = md.GetBlobReader(md.GetTypeSpecification(par).Signature);
        if (ts.ReadByte() != 0x15) return null; // not a GenericInst
        ts.ReadByte();                    // CLASS/VALUETYPE marker
        ts.ReadTypeHandle();
        var argc = ts.ReadCompressedInteger();
        for (var i = 0; i < argc; i++)
        {
            var b = MethodSignatureCoder.SkipCustomModifiers(ref ts, ts.ReadByte());
            if (b is 0x11 or 0x12 && i < varIdx) _ = ts.ReadTypeHandle();
            if (b is 0x11 or 0x12 && i == varIdx)
            {
                var th = ts.ReadTypeHandle();
                return MetadataTokens.GetToken(th);
            }
            if (i >= varIdx) break;
        }
        return null;
    }

    // D5: the concrete generic-argument type of a SHARED method-level generic
    // method's MVAR return (PickUser2::Pick<System.Int32> -> "System.Int32").
    // The shared clone returns the boxed object slot; the caller unboxes by
    // this concrete T. Decode the underlying MethodDef signature's return
    // VAR/MVAR index, then read that MethodSpec genArg. Returns null for
    // non-shared callees, non-VAR returns, open arguments, or undecodable
    // shapes (the caller keeps the existing slot semantics).
    static string? MethodSpecMvarReturnConcreteType(MetadataReader md, MethodSpecificationHandle msHandle)
    {
        var ms = md.GetMethodSpecification(msHandle);
        if (ms.Method.Kind != HandleKind.MethodDefinition) return null;
        var dm = md.GetMethodDefinition((MethodDefinitionHandle)ms.Method);
        if (GenericUsageAnalyzer.SharedGenericDefs?.Contains(md.GetFullName(dm.GetDeclaringType())) != true) return null;
        var sig = md.GetBlobReader(dm.Signature);
        var hdr = sig.ReadSignatureHeader();
        if (hdr.IsGeneric) sig.ReadCompressedInteger();
        sig.ReadCompressedInteger(); // param count
        var ret = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        int mvarIdx;
        if (ret is 0x13 or 0x1E) mvarIdx = sig.ReadCompressedInteger(); else return null;
        return MethodSpecGenArgFullName(md, ms.Signature, mvarIdx);
    }

    // D5: the METADATA TOKEN of the concrete generic argument of a SHARED
    // method-level generic method's MVAR return (PickUser2::Pick<Pt> -> the
    // Cases.Pt TypeDef token, for the unbox_{token} emission path).
    static int? MethodSpecMvarReturnConcreteToken(MetadataReader md, MethodSpecificationHandle msHandle)
    {
        var ms = md.GetMethodSpecification(msHandle);
        if (ms.Method.Kind != HandleKind.MethodDefinition) return null;
        var dm = md.GetMethodDefinition((MethodDefinitionHandle)ms.Method);
        if (GenericUsageAnalyzer.SharedGenericDefs?.Contains(md.GetFullName(dm.GetDeclaringType())) != true) return null;
        var sig = md.GetBlobReader(dm.Signature);
        var hdr = sig.ReadSignatureHeader();
        if (hdr.IsGeneric) sig.ReadCompressedInteger();
        sig.ReadCompressedInteger(); // param count
        var ret = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        int mvarIdx;
        if (ret is 0x13 or 0x1E) mvarIdx = sig.ReadCompressedInteger(); else return null;
        return MethodSpecGenArgToken(md, ms.Signature, mvarIdx);
    }

    // Read the concrete generic-argument FULL NAME at genIndex from a MethodSpec
    // GENERICINST (0x0A) blob, or null when the argument is open/unboxable.
    // The same byte table as SharedReturnConcreteType's MemberRef TypeSpec-arg
    // decode (primitives/string/object/class/value-type).
    static string? MethodSpecGenArgFullName(MetadataReader md, in BlobHandle genSigHandle, int genIndex)
    {
        // D5: the token variant shares the decoded TypeDefOrRef handle; a
        // VALUETYPE/CLASS arg's coded index doubles as the metadata token the
        // CodeGenerator box_/unbox_ branches key on.
        return MethodSpecGenArgHandle(md, genSigHandle, genIndex) is { } fh
            ? fh.Kind switch
            {
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)fh),
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)fh),
                _ => null,
            }
            : MethodSpecGenArgScalarName(md, genSigHandle, genIndex);
    }

    // D5: the metadata TOKEN of a VALUETYPE/CLASS MethodSpec generic argument
    // (TypeDefinition / TypeReference coded index), or null for primitive /
    // open / undecodable args. The shared-byref bridge routes user structs and
    // enums through the CodeGenerator box_{token}/unbox_{token} emission
    // paths, which resolve the concrete type from THIS token.
    static int? MethodSpecGenArgToken(MetadataReader md, in BlobHandle genSigHandle, int genIndex)
    {
        if (MethodSpecGenArgHandle(md, genSigHandle, genIndex) is not { } th) return null;
        return MetadataTokens.GetToken(th);
    }

    // Re-shared decode: skip to the genArg at genIndex, return the
    // TypeDefOrRef handle for VALUETYPE (0x11) / CLASS (0x12) args, null for
    // every other shape. The prior-arg walk consumes VALUETYPE/CLASS handles
    // so multi-arg MethodSpecs decode at the right offset (D5 decode fix:
    // the element byte 0x11 must NEVER be read as a TypeDefOrRef coded index).
    static EntityHandle? MethodSpecGenArgHandle(MetadataReader md, in BlobHandle genSigHandle, int genIndex)
    {
        var gsig = md.GetBlobReader(genSigHandle);
        if (gsig.ReadByte() != 0x0A) return null; // not GENERICINST
        var argc = gsig.ReadCompressedInteger();
        if ((uint)genIndex >= (uint)argc) return null;
        byte b = 0;
        for (var i = 0; i <= genIndex; i++)
            b = MethodSignatureCoder.SkipCustomModifiers(ref gsig, gsig.ReadByte());
        if (b is not (0x11 or 0x12)) return null;
        // Re-read to the CURRENT arg's handle: earlier VALUETYPE/CLASS args
        // carry a trailing TypeHandle that the element-only skip must consume.
        var gs2 = md.GetBlobReader(genSigHandle);
        if (gs2.ReadByte() != 0x0A) return null;
        var argc2 = gs2.ReadCompressedInteger();
        for (var i = 0; i < genIndex; i++)
        {
            var prior = MethodSignatureCoder.SkipCustomModifiers(ref gs2, gs2.ReadByte());
            if (prior is 0x11 or 0x12) _ = gs2.ReadTypeHandle();
        }
        _ = MethodSignatureCoder.SkipCustomModifiers(ref gs2, gs2.ReadByte());
        return gs2.ReadTypeHandle();
    }

    // The scalar (primitive/String/Object) arm of the genArg name decode --
    // unchanged original byte table, kept separate so the D5 handle variant
    // does not perturb the primitive path.
    static string? MethodSpecGenArgScalarName(MetadataReader md, in BlobHandle genSigHandle, int genIndex)
    {
        var gsig = md.GetBlobReader(genSigHandle);
        if (gsig.ReadByte() != 0x0A) return null; // not GENERICINST
        var argc = gsig.ReadCompressedInteger();
        if ((uint)genIndex >= (uint)argc) return null;
        byte b = 0;
        for (var i = 0; i <= genIndex; i++)
            b = MethodSignatureCoder.SkipCustomModifiers(ref gsig, gsig.ReadByte());
        return b switch
        {
            0x02 => "System.Boolean",
            0x03 => "System.Char",
            0x04 => "System.SByte",
            0x05 => "System.Byte",
            0x06 => "System.Int16",
            0x07 => "System.UInt16",
            0x08 => "System.Int32",
            0x09 => "System.UInt32",
            0x0A => "System.Int64",
            0x0B => "System.UInt64",
            0x0C => "System.Single",
            0x0D => "System.Double",
            0x0E => "System.String",
            0x1C => "System.Object",
            _ => null, // VALUETYPE/CLASS (handle decode) / arrays/nested-generic/open-VAR
        };
    }

    //--------------------------------------------------------------------------
    // IlCallReturnsValue -- does the callee's SIGNATURE declare a non-void
    // return? .NET call semantics ALWAYS push the result of a non-void
    // signature onto the evaluation stack, even when the caller discards it
    // with a `pop` (statement-expression call). An rv=false mapping drops
    // that value (the C API returns void), but the fold must mirror the IL
    // stack slot anyway (DiscardedValueExpr) or the shadow stack desyncs.
    // Decode the signature blob's return element (ELEMENT_TYPE_VOID = 0x01);
    // MethodSpecification recurses to the generic method's signature. Mirrors
    // UserCallResultKind's decode (same blob shape).
    //--------------------------------------------------------------------------
    static bool IlCallReturnsValue(MetadataReader? md, int token)
    {
        if (md == null) return false;
        var handle = MetadataTokens.Handle(token);
        BlobHandle sigHandle;
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                sigHandle = md.GetMethodDefinition((MethodDefinitionHandle)handle).Signature;
                break;
            case HandleKind.MemberReference:
                sigHandle = md.GetMemberReference((MemberReferenceHandle)handle).Signature;
                break;
            case HandleKind.MethodSpecification:
                var spec = md.GetMethodSpecification((MethodSpecificationHandle)handle);
                return IlCallReturnsValue(md, MetadataTokens.GetToken(spec.Method));
            default:
                return false;
        }
        var sig = md.GetBlobReader(sigHandle);
        // wir9-b23 (arrfuncs unlock): a GENERIC method signature (calling
        // convention 0x10/0x18 -- Array.Resize<T>, any generic MethodSpec)
        // carries the GenericParameterCount BETWEEN the header and the real
        // param count -- not consuming it reads the param count as the return
        // type, so `void Resize<T>` decodes as non-void and a spurious
        // DiscardedValueExpr is pushed (no matching pop in the void IL ->
        // leaked into the next statement's emission). Same consumption as
        // MethodRefResolver.RawSignature.
        var hdr = sig.ReadSignatureHeader();
        if (hdr.IsGeneric) _ = sig.ReadCompressedInteger(); // GenericParameterCount
        _ = sig.ReadCompressedInteger(); // param count
        var ret = sig.ReadByte();    // return element type
        return ret != 0x01;          // ELEMENT_TYPE_VOID
    }

    //--------------------------------------------------------------------------
    // P1 temp folding (stackframe-audit P0-1/P0-2 + P1-2): a linear post-pass
    // over the statement list. Every rule requires the temp to be assigned
    // exactly once, referenced exactly once, and consumed by the IMMEDIATELY
    // NEXT statement -- adjacency preserves side-effect order (nothing can run
    // between the producing statement and the use):
    //   1. box->unbox round trip: t0 = box(v); t1 = unbox(t0) (same type
    //      token, t0 single-use) -> t1 = v. The box allocation is dead work
    //      (box->unbox of the same type is an identity) -- drops the temp
    //      AND its GC root slot.
    //   2. copy-rename: t = <call>; X = t (a PURE copy in the next statement)
    //      -> X = <call>. The call keeps its statement position, so this is a
    //      rename of the assignment target -- safe for reference results too
    //      (covers t30 -> loc0 chains and the join single-use source-arm
    //      copies t11 = t10 -> t11 = vcall directly).
    //   3. single-use forward substitution: t = <call>; <next statement uses
    //      t> -> inline the call at the use, drop the temp. VALUE-typed call
    //      results only (reference-typed inlining needs the P2-3 MayTriggerGc
    //      whitelist, per the audit); the consumer must not contain any other
    //      call-like node (a second call in one C++ statement would make the
    //      relative evaluation order unspecified).
    //--------------------------------------------------------------------------
    static List<Stmt> OptimizeTemps(List<Stmt> stmts)
    {
        for (var i = 0; i < stmts.Count; i++)
        {
            if (stmts[i] is not AssignStmt a || !IsTempName(a.Target)) continue;
            if (i + 1 >= stmts.Count) continue;
            if (CountTempAssignments(stmts, a.Target) != 1) continue;
            if (CountTempUses(stmts, a.Target) != 1) continue;
            var use = FindTempUse(stmts, i + 1, a.Target);
            if (use != i + 1) continue; // not consumed by the next statement

            // 1. box -> unbox round trip (matching type tokens; the boxed
            //    value is the unbox argument). The unbox expression itself
            //    must be replaced by the boxed VALUE (box->unbox of the same
            //    type is an identity) -- keeping the unbox call would leave
            //    `t1 = unbox(5)` (a call on a non-object) behind.
            if (a.Value is CallExpr box && box.Function.StartsWith("box_", StringComparison.Ordinal)
                && stmts[use] is AssignStmt ub && ub.Value is CallExpr unbox
                && unbox.Function == "unbox_" + box.Function["box_".Length..]
                && unbox.Args.Count == 1 && unbox.Args[0] is LocExpr ul && ul.Name == a.Target)
            {
                stmts[use] = new AssignStmt
                {
                    Target = ub.Target,
                    Value = box.Args[0],
                    SourceOffset = ub.SourceOffset,
                    SourceOpName = ub.SourceOpName,
                };
                stmts.RemoveAt(i);
                i--;
                continue;
            }

            // 2. pure-copy rename: X = t.
            if (stmts[use] is AssignStmt copy && copy.Value is LocExpr cl && cl.Name == a.Target)
            {
                stmts[use] = new AssignStmt
                {
                    Target = copy.Target,
                    Value = a.Value,
                    SourceOffset = copy.SourceOffset,
                    SourceOpName = copy.SourceOpName,
                };
                stmts.RemoveAt(i);
                i--;
                continue;
            }

            // 3. forward substitution of a value-typed call result.
            // decimal-domain C44 (decgap): value-slot icalls ("dec"/"dec2"/
            // "guid"/"ip4"/"bi" -- the icall writes through an explicit
            // RESULT SLOT parameter, fn(&t, ...)) MUST keep their assignment
            // statement; inlining them into an expression position (return /
            // arg / condition) is uncompilable -- the emitter LOUD-rejects it
            // (CodeGenerator "call in expression position" guards exactly
            // guid/dec/dec2/ip4/bi). `return x + y` on decimals folds
            // `t0 = add(x,y); return t0` and was inlined into
            // `return add(x,y)` here -- the C43/C44 temp-slot conflict.
            // binrw3: r.ReadDecimal() is "dec2" (out-last); `"d=" + Read`
            // inlined it into the concat arg without this exclusion.
            if (a.Value is CallExpr cslot && cslot.ResultKind is "dec" or "dec2" or "guid" or "ip4" or "bi")
                continue;
            if (a.Value is CallExpr or VirtualCallExpr
                && !HasRefResult(a.Value)
                && NoOtherCallIn(stmts[use], a.Target))
            {
                stmts[use] = ReplaceInStatement(stmts[use], a.Target, a.Value);
                stmts.RemoveAt(i);
                i--;
            }
        }
        return stmts;
    }

    static bool IsTempName(string name)
    {
        if (name.Length < 2 || name[0] != 't') return false;
        for (var i = 1; i < name.Length; i++)
            if (name[i] < '0' || name[i] > '9') return false;
        return true;
    }

    static int CountTempAssignments(List<Stmt> stmts, string name)
    {
        var n = 0;
        foreach (var s in stmts)
            if (s is AssignStmt a && a.Target == name) n++;
        return n;
    }

    static int CountTempUses(List<Stmt> stmts, string name)
    {
        var n = 0;
        foreach (var s in stmts) n += CountUsesInStatement(s, name);
        return n;
    }

    static int FindTempUse(List<Stmt> stmts, int from, string name)
    {
        for (var i = from; i < stmts.Count; i++)
            if (CountUsesInStatement(stmts[i], name) > 0) return i;
        return -1;
    }

    static int CountUsesInStatement(Stmt s, string name) => s switch
    {
        AssignStmt a => CountUsesInExpr(a.Value, name),
        ExprStmt es => CountUsesInExpr(es.Value, name),
        RetStmt r => r.Value == null ? 0 : CountUsesInExpr(r.Value, name),
        FilterReturnStmt fr => CountUsesInExpr(fr.Value, name),
        BranchStmt b => b.Condition == null ? 0 : CountUsesInExpr(b.Condition, name),
        CallStmt c => c.Args.Sum(x => CountUsesInExpr(x, name)),
        FieldAssignStmt f => CountUsesInExpr(f.Object, name) + CountUsesInExpr(f.Value, name),
        _ => 0,
    };

    static int CountUsesInExpr(Expr e, string name) => e switch
    {
        LocExpr l => l.Name == name ? 1 : 0,
        BinaryExpr b => CountUsesInExpr(b.Left, name) + CountUsesInExpr(b.Right, name),
        UnaryExpr u => CountUsesInExpr(u.Operand, name),
        CallExpr c => c.Args.Sum(x => CountUsesInExpr(x, name)),
        VirtualCallExpr v => v.Args.Sum(x => CountUsesInExpr(x, name)),
        DelegateInvokeExpr d => d.Args.Sum(x => CountUsesInExpr(x, name)),
        FieldExpr f => CountUsesInExpr(f.Object, name),
        FieldAddrExpr f => CountUsesInExpr(f.Object, name),
        MarshalUtf8Expr m => CountUsesInExpr(m.Arg, name),
        _ => 0,
    };

    static bool HasRefResult(Expr e) => e switch
    {
        CallExpr c => c.ReturnsRef,
        VirtualCallExpr v => v.ReturnsRef,
        _ => true, // NewObjExpr etc. -- never inlined
    };

    // The consumer statement must not contain any call-like node other than
    // the single temp reference (a second call in the same C++ statement
    // would leave the relative evaluation order unspecified).
    static bool NoOtherCallIn(Stmt s, string temp) => s switch
    {
        AssignStmt a => NoOtherCallInExpr(a.Value, temp),
        ExprStmt es => NoOtherCallInExpr(es.Value, temp),
        RetStmt r => r.Value == null || NoOtherCallInExpr(r.Value, temp),
        FilterReturnStmt fr => NoOtherCallInExpr(fr.Value, temp),
        BranchStmt b => b.Condition == null || NoOtherCallInExpr(b.Condition, temp),
        CallStmt c => c.Args.All(x => NoOtherCallInExpr(x, temp)),
        FieldAssignStmt f => NoOtherCallInExpr(f.Object, temp) && NoOtherCallInExpr(f.Value, temp),
        _ => false,
    };

    static bool NoOtherCallInExpr(Expr e, string temp) => e switch
    {
        LocExpr => true,
        CallExpr or VirtualCallExpr or NewObjExpr or DelegateInvokeExpr
            or DelegateNewExpr or ValueTypeNewExpr or MarshalUtf8Expr => false,
        BinaryExpr b => NoOtherCallInExpr(b.Left, temp) && NoOtherCallInExpr(b.Right, temp),
        UnaryExpr u => NoOtherCallInExpr(u.Operand, temp),
        FieldExpr f => NoOtherCallInExpr(f.Object, temp),
        FieldAddrExpr f => NoOtherCallInExpr(f.Object, temp),
        _ => true,
    };

    static Stmt ReplaceInStatement(Stmt s, string temp, Expr value) => s switch
    {
        AssignStmt a => new AssignStmt
        {
            Target = a.Target,
            Value = ReplaceExpr(a.Value, temp, value),
            SourceOffset = a.SourceOffset,
            SourceOpName = a.SourceOpName,
        },
        ExprStmt es => new ExprStmt
        {
            Value = ReplaceExpr(es.Value, temp, value),
            SourceOffset = es.SourceOffset,
            SourceOpName = es.SourceOpName,
        },
        RetStmt r => new RetStmt
        {
            Value = ReplaceExpr(r.Value!, temp, value),
            SourceOffset = r.SourceOffset,
            SourceOpName = r.SourceOpName,
        },
        FilterReturnStmt fr => new FilterReturnStmt
        {
            Value = ReplaceExpr(fr.Value, temp, value),
            SourceOffset = fr.SourceOffset,
            SourceOpName = fr.SourceOpName,
        },
        BranchStmt b => new BranchStmt
        {
            TargetLabel = b.TargetLabel,
            Condition = ReplaceExpr(b.Condition!, temp, value),
            SourceOffset = b.SourceOffset,
            SourceOpName = b.SourceOpName,
        },
        CallStmt c => ReplaceArgs(c.Args, c, temp, value),
        FieldAssignStmt f => new FieldAssignStmt
        {
            Object = ReplaceExpr(f.Object, temp, value),
            FieldCppName = f.FieldCppName,
            OwnerFullName = f.OwnerFullName,
            IsRefField = f.IsRefField,
            IsValueTypeObject = f.IsValueTypeObject,
            RefTargetFullName = f.RefTargetFullName,
            Value = ReplaceExpr(f.Value, temp, value),
            SourceOffset = f.SourceOffset,
            SourceOpName = f.SourceOpName,
        },
        _ => s,
    };

    static T ReplaceArgs<T>(List<Expr> args, T host, string temp, Expr value) where T : notnull
    {
        for (var k = 0; k < args.Count; k++) args[k] = ReplaceExpr(args[k], temp, value);
        return host;
    }

    static Expr ReplaceExpr(Expr e, string temp, Expr value) => e switch
    {
        LocExpr l when l.Name == temp => value,
        BinaryExpr b => new BinaryExpr
        {
            Op = b.Op,
            Left = ReplaceExpr(b.Left, temp, value),
            Right = ReplaceExpr(b.Right, temp, value),
            Kind = b.Kind,
        },
        UnaryExpr u => new UnaryExpr { Op = u.Op, Operand = ReplaceExpr(u.Operand, temp, value) },
        CallExpr c => ReplaceArgs(c.Args, c, temp, value),
        VirtualCallExpr v => ReplaceArgs(v.Args, v, temp, value),
        DelegateInvokeExpr d => ReplaceArgs(d.Args, d, temp, value),
        FieldExpr f => new FieldExpr
        {
            Object = ReplaceExpr(f.Object, temp, value),
            FieldCppName = f.FieldCppName,
            OwnerFullName = f.OwnerFullName,
            IsRefField = f.IsRefField,
            IsValueTypeObject = f.IsValueTypeObject,
        },
        FieldAddrExpr f => new FieldAddrExpr
        {
            Object = ReplaceExpr(f.Object, temp, value),
            FieldCppName = f.FieldCppName,
            OwnerFullName = f.OwnerFullName,
            IsValueTypeObject = f.IsValueTypeObject,
        },
        MarshalUtf8Expr m => new MarshalUtf8Expr { Arg = ReplaceExpr(m.Arg, temp, value), Utf16 = m.Utf16, Ansi = m.Ansi },
        _ => e,
    };

    //------------------------------------------------------------------------------
    // Tail-call optimization (recursion-audit section 6a): self-recursive tail calls
    // -> a goto loop, O(1) stack.
    //
    // Detection (transform only when ALL hold):
    //   1. Self-recursion: `tN = call_{token}(args)`'s Resolved.CppName == the
    //      current method's CppName (ctx.MethodCppName -- same signature, same
    //      hash, overloads naturally distinguished).
    //   2. Tail position: the IMMEDIATELY next statement is `return tN;` (the
    //      call result is consumed directly by ret, no further computation; ret
    //      terminates the basic block, no statements between a and r).
    //   3. No EH: any exception-handler region on the method rejects the
    //      transform (a handler needs a real return to unwind).
    //   4. GC-frame safe + pure self-recursion (conservative subset):
    //      * STATIC methods only (instance method arg0 = this is a GC-root
    //        param slot);
    //      * no managed reference params (ParamTypeNames has no "r:" prefix);
    //      * every arg expression is a pure value node (IntConst/Int64Const/
    //        DoubleConst/Binary/Unary/LocExpr), and the LocExprs are not
    //        tracked references recorded in objTypes (no allocation point -> a
    //        GC cannot occur in the new-temp relay window).
    //
    // Transform shape (temps evaluate first to prevent aliasing -- `arg1 =
    // arg1 + arg0` would read the new value after arg0 is updated, so ALL new
    // args must first evaluate into independent temps, then write the param
    // slots):
    //   t{next} = argExpr0; t{next+1} = argExpr1; ...
    //   arg0 = t{next}; arg1 = t{next+1}; ...
    //   goto L_tail_entry;
    // The L_tail_entry label is inserted before the function body's first
    // statement -- the frame push runs once in the prologue and the goto only
    // re-enters the body (audit section 6a condition 4: the frame slot addresses are
    // unchanged across the loop reuse, a single push stays valid).
    //
    // Semantic-equivalence proof (audit section 6a): the self-recursive tail call
    // `return F(newArgs)` and `{ evaluate new args; update param slots; goto
    // entry; }` are observably identical on all execution paths (no EH, no
    // frame side effects, the return-value path converges uniquely). The
    // ensure_cctor_* guards the original call site would trigger are skipped
    // across loop iterations -- but the first entry into this method necessarily
    // passes the external call site's guard, the cctor has run (idempotent
    // flag), so semantics are unchanged.
    //------------------------------------------------------------------------------
    public const string TailEntryLabel = "L_tail_entry";

    public static List<Stmt> ApplyTailCallLoop(List<Stmt> stmts, MethodContext ctx,
        IReadOnlyDictionary<string, string>? objTypes = null)
    {
        if (ctx.MethodCppName == null || !ctx.IsStatic) return stmts;
        // Condition 3: any exception-handler region -> no transform
        // (conservative: the whole method is rejected, not just the covered
        // region).
        if (ctx.ExceptionRegions is { Count: > 0 }) return stmts;
        // Condition 4b: any reference param -> no transform.
        if (ctx.ParamTypeNames?.Any(p => p.StartsWith("r:", StringComparison.Ordinal)) == true) return stmts;

        // New-temp naming: scan the existing AssignStmt targets for the max tN
        // (the transform runs after OptimizeTemps, so the temps counter is
        // settled; using the names inside stmts avoids clashing with residual
        // temps).
        var maxTemp = -1;
        foreach (var s in stmts)
            if (s is AssignStmt a && IsTempName(a.Target)
                && int.TryParse(a.Target.AsSpan(1), out var n) && n > maxTemp)
                maxTemp = n;

        var result = new List<Stmt>(stmts.Count + 8);
        var labelInserted = false;
        for (var i = 0; i < stmts.Count; i++)
        {
            // Conditions 1+2, shape 1: AssignStmt{Target=tN, Value=CallExpr
            // (self-recursive)} immediately followed by RetStmt{Value=LocExpr
            // {tN}} (tN is the call result's sole consumer).
            if (stmts[i] is AssignStmt a
                && a.Value is CallExpr call
                && call.Resolved is { } resolved
                && resolved.CppName == ctx.MethodCppName
                && IsTempName(a.Target)
                && i + 1 < stmts.Count
                && stmts[i + 1] is RetStmt ret
                && ret.Value is LocExpr retLoc
                && retLoc.Name == a.Target
                && IsPureValueArgs(call.Args, objTypes))
            {
                EmitTailJump(result, call, resolved, ref maxTemp, ref labelInserted);
                i++; // skip the replaced RetStmt
                continue;
            }
            // Conditions 1+2, shape 2: RetStmt{Value=CallExpr (self-recursive)}
            // -- OptimizeTemps rule 3 forward-substitutes the "single-assigned +
            // single-read" call temp into the ret, so the real pipeline arrives
            // in this shape (TailSum probe evidence: `return F(args);` emitted
            // as-is).
            if (stmts[i] is RetStmt r2
                && r2.Value is CallExpr rc
                && rc.Resolved is { } rres
                && rres.CppName == ctx.MethodCppName
                && IsPureValueArgs(rc.Args, objTypes))
            {
                EmitTailJump(result, rc, rres, ref maxTemp, ref labelInserted);
                continue;
            }
            result.Add(stmts[i]);
        }
        return result;

        // Transform core (shared by both shapes): entry label + new-arg
        // evaluation temps + param-slot update + goto.
        void EmitTailJump(List<Stmt> sink, CallExpr call, ResolvedMethod resolved,
            ref int maxTemp, ref bool labelInserted)
        {
            // Condition 4c: the arg count must match the param-slot count
            // (defensive: the call's ParamCount matches the current method's
            // ArgCount -- implied by the identical signature hash, asserted
            // here explicitly).
            if (call.Args.Count > ctx.ArgCount)
                throw new NotSupportedException(
                    $"tail call to {resolved.CppName} pops {call.Args.Count} args but the method has {ctx.ArgCount} param slots");

            if (!labelInserted)
            {
                // The entry label must be inserted at the function body's
                // first statement (head): after the goto jumps back, all entry
                // code (guards, exit checks, etc.) re-runs -- consistent with
                // "re-entering the function with new args". Inserting at the
                // first tail-call site would skip the n==0 exit check -> an
                // infinite loop (TailSum evidence).
                sink.Insert(0, new LabelStmt { Name = TailEntryLabel });
                labelInserted = true;
            }
            // New-arg evaluation temps (all derived from the old param values;
            // any order is safe).
            var temps = new List<string>();
            foreach (var arg in call.Args)
            {
                var name = $"t{++maxTemp}";
                temps.Add(name);
                sink.Add(new AssignStmt { Target = name, Value = arg });
            }
            // Param-slot update (temp references, not the original expressions
            // -- prevents the optimizer inlining regressing the alias bug).
            for (var k = 0; k < temps.Count; k++)
                sink.Add(new AssignStmt { Target = $"arg{k}", Value = new LocExpr { Name = temps[k] } });
            // Jump back to the entry (unconditional).
            sink.Add(new BranchStmt { TargetLabel = TailEntryLabel });
        }
    }

    // Condition 4c: every arg expression is a pure value node (no calls /
    // allocations / references / fields / strings) -- the new temps are thereby
    // determined to be int32_t value slots (no GC roots), and evaluation has no
    // side effects.
    static bool IsPureValueArgs(List<Expr> args, IReadOnlyDictionary<string, string>? objTypes)
    {
        foreach (var a in args)
            if (!IsPureValueExpr(a, objTypes)) return false;
        return true;
    }

    static bool IsPureValueExpr(Expr e, IReadOnlyDictionary<string, string>? objTypes) => e switch
    {
        IntConstExpr or Int64ConstExpr or DoubleConstExpr => true,
        LocExpr l => !objTypes?.ContainsKey(l.Name) ?? true,
        BinaryExpr b => IsPureValueExpr(b.Left, objTypes) && IsPureValueExpr(b.Right, objTypes),
        UnaryExpr u => IsPureValueExpr(u.Operand, objTypes),
        _ => false,
    };

    //--------------------------------------------------------------------------
    // Cctor guard loop hoisting (generated-code audit #5, phase 2 -- the
    // conservative subset). The .NET JIT hoists beforefieldinit type-init
    // checks out of loops; with the inline-check shape (phase 1) the C++
    // compiler cannot hoist them itself (the in-loop NOINLINE ensure_cctor_*
    // writes a flag, which is opaque). This pass moves the loop body's
    // ensure_cctor_* calls into the loop preheader, making each iteration have
    // zero guard cost.
    //
    // Safety (hoist only the spec-allowed subset):
    //   * BeforeFieldInit types only -- ECMA-335 II.7.5.4.1 lets their
    //     initializer run at any time before the first static-field access, so
    //     "running the cctor at loop entry (even if the body never executes)"
    //     is still in-spec; explicit static ctor (precise-init) types keep
    //     their per-access checks.
    //   * Loop shape: a single-block loop -- a header label H, exactly one back
    //     edge B targeting H, no other label/branch/ret in the body
    //     (break/continue/early-exit/nested loops are all rejected).
    //   * Unique entry: either a sequential fall-in to H (do-while shape) or a
    //     single unconditional branch into the condition label (while shape);
    //     verified by counting the branch targets of H and C (multiple entries
    //     would bypass the hoisted guards).
    //   * The body runs no code that could trigger a cctor (no calls /
    //     allocations / newobj / delegates / box materialization) -- otherwise
    //     hoisting would change the relative execution order of multiple types'
    //     cctors (the dependency-order-unchanged iron rule).
    //--------------------------------------------------------------------------
    public static List<Stmt> HoistLoopGuards(List<Stmt> stmts, IReadOnlyList<TypeDef>? types)
    {
        if (types == null || stmts.Count < 3) return stmts;

        // C++ name -> TypeDef (for the BeforeFieldInit check).
        var typeByCpp = new Dictionary<string, TypeDef>();
        foreach (var t in types)
        {
            var cpp = TypeEmitter.TypeCppName(t);
            if (!typeByCpp.ContainsKey(cpp)) typeByCpp[cpp] = t;
        }

        // Branch-target counts: H may only be referenced by the back edge; C
        // only by the entry branch (or by nobody).
        var targetCounts = new Dictionary<string, int>();
        foreach (var b in stmts.OfType<BranchStmt>())
            targetCounts[b.TargetLabel] = targetCounts.TryGetValue(b.TargetLabel, out var n) ? n + 1 : 1;

        // Re-scan after every successful hoist (a previous hoist shifts the
        // indices); stop when no hoist is possible.
        while (true)
        {
            var hoisted = false;
            for (var h = 0; h < stmts.Count; h++)
            {
                if (stmts[h] is not LabelStmt header) continue;
                var headerName = header.Name;
                if (targetCounts.TryGetValue(headerName, out var hc) && hc != 1) continue; // multiple entries / no back edge

                // Locate the back edge: the first branch after the header that
                // targets it; no other branch/ret/extra label between them.
                var backEdge = -1;
                var labelInBody = -1;
                var bad = false;
                for (var j = h + 1; j < stmts.Count; j++)
                {
                    var s = stmts[j];
                    if (s is BranchStmt bs)
                    {
                        if (bs.TargetLabel == headerName) { backEdge = j; break; }
                        bad = true; // other branch in the body (break/continue/early exit) -> not a single-block loop
                        break;
                    }
                    if (s is RetStmt) { bad = true; break; }
                    if (s is LabelStmt ls)
                    {
                        if (labelInBody >= 0) { bad = true; break; } // a second label -> not single-block
                        labelInBody = j;
                        continue;
                    }
                    if (s is CallStmt cs && !cs.Function.StartsWith("ensure_cctor_", StringComparison.Ordinal))
                    {
                        bad = true; // any call could trigger another type's cctor -> the order would flip
                        break;
                    }
                    if (s is AssignStmt a && HasCctorTrigger(a.Value)) { bad = true; break; }
                    if (s is FieldAssignStmt fa && HasCctorTrigger(fa.Value)) { bad = true; break; }
                    if (s is ExprStmt es && HasCctorTrigger(es.Value)) { bad = true; break; }
                }
                if (bad || backEdge < 0) continue;
                if (stmts[backEdge] is not BranchStmt back || back.Condition == null) continue; // unconditional back edge = infinite loop; skip conservatively

                // The in-body label must be the conditional label immediately
                // before the back edge (no other label).
                if (labelInBody >= 0)
                {
                    if (labelInBody != backEdge - 1) continue;
                    if (((LabelStmt)stmts[labelInBody]).Name == headerName) continue; // defensive: two labels at the same offset are impossible
                }
                var condName = labelInBody >= 0 ? ((LabelStmt)stmts[labelInBody]).Name : null;

                // Entry determination: preheader = the statement before the
                // header.
                //   while shape: an unconditional branch to the condition label
                //     -> insert the hoisted guards before it.
                //   do-while shape: an ordinary statement falls through to the
                //     header -> insert the guards after it (before the header).
                //   Conditional entry (could bypass the guards) -> rejected.
                var p = h - 1;
                var insertAt = -1;
                if (p < 0) continue; // no predecessor statement -> cannot locate the preheader
                if (stmts[p] is BranchStmt entryBr)
                {
                    if (entryBr.Condition != null) continue;
                    if (condName == null || entryBr.TargetLabel != condName) continue;
                    if (targetCounts[condName] != 1) continue; // C allows only this single entry
                    insertAt = p; // insert before the entry branch
                }
                else
                {
                    if (condName != null && targetCounts[condName] != 0) continue; // the condition label referenced by a branch = multiple entries
                    insertAt = h; // insert before the header label (= after the predecessor statement)
                }

                // Collect the in-body ensures (order preserved).
                var ensures = new List<CallStmt>();
                for (var j = h + 1; j < backEdge; j++)
                    if (stmts[j] is CallStmt cs && cs.Function.StartsWith("ensure_cctor_", StringComparison.Ordinal))
                        ensures.Add(cs);
                if (ensures.Count == 0) continue;

                // All ensure types must be BeforeFieldInit (the spec allows
                // early initialization); types not in the model are
                // conservatively not hoisted.
                var hoistable = true;
                foreach (var e in ensures)
                {
                    var fn = e.Function["ensure_cctor_".Length..];
                    if (!typeByCpp.TryGetValue(fn, out var td) || !td.BeforeFieldInit)
                    {
                        hoistable = false;
                        break;
                    }
                }
                if (!hoistable) continue;

                // Adjacent same-type guard merge (same rule as the call sites:
                // the flag only ever turns true).
                var merged = new List<CallStmt>();
                foreach (var e in ensures)
                    if (merged.Count == 0 || merged[^1].Function != e.Function) merged.Add(e);

                // Move: remove the ensures first, then insert into the
                // preheader (removing before the header does not shift its
                // index).
                foreach (var e in ensures) stmts.Remove(e);
                stmts.InsertRange(insertAt, merged);
                hoisted = true;
                break;
            }
            if (!hoisted) break;
        }
        return stmts;
    }

    // Whether the expression tree can trigger a cctor (user-code calls /
    // allocation materialization -- hoisting would change their relative order
    // against the hoisted guards). Conservative: every CallExpr/NewObjExpr/
    // delegate materialization is rejected.
    static bool HasCctorTrigger(Expr e) => e switch
    {
        CallExpr or VirtualCallExpr or NewObjExpr or DelegateInvokeExpr or DelegateNewExpr => true,
        BinaryExpr b => HasCctorTrigger(b.Left) || HasCctorTrigger(b.Right),
        UnaryExpr u => HasCctorTrigger(u.Operand),
        FieldExpr f => HasCctorTrigger(f.Object),
        FieldAddrExpr f => HasCctorTrigger(f.Object),
        MarshalUtf8Expr m => HasCctorTrigger(m.Arg),
        _ => false,
    };

    //--------------------------------------------------------------------------
    // DiscardedValueExpr -- the fold's mirror of an IL stack slot whose value
    // is DROPPED by an rv=false mapping (the C API returns void while the .NET
    // signature returns a value, e.g. ArrayList::Add -> il2cpp_arraylist_add).
    // .NET call semantics ALWAYS push the result of a non-void signature; the
    // caller's `pop` discards it. The fold must mirror that slot exactly or
    // the shadow stack desyncs by one and the next dup/pop misreads it
    // ("dup on empty stack at offset 0x0523" on the collchain
    // `newobj; dup; ldstr; callvirt Add; pop; dup` chain). The marker is
    // consumed ONLY by an IL `pop` (dup copies it -- both copies pop); any
    // other consumer (stloc / call arg / ret / arithmetic) carries it into a
    // statement, where the emitter's unknown-Expr default throw rejects it
    // loudly -- the map's "callers must discard the dropped value" contract
    // (MethodMap.ArrayList.cs), loud instead of a silent wrong value.
    //--------------------------------------------------------------------------
    public sealed class DiscardedValueExpr : Expr
    {
        public required int CallToken { get; init; }
        public required int Offset { get; init; }
    }
}













