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
    // Tail-call optimization (recursion-audit §6a): the generated C++ function
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
        // Audit H8: newobj temp -> the allocated type's full name. The throw
        // site uses it to emit the REAL managed exception type (a catch of a
        // derived type must match; before the fix every throw was a fixed
        // System.Exception and derived catches never matched).
        var objTypes = new Dictionary<string, string>();
        // excext (m7m): newobj temp -> the BCL-exception ctor's arguments
        // (the .ctor(string) message / (string, string) paramName+message,
        // captured in original order AFTER the implicit `this`). The throw
        // site forwards them to the emitter so the managed exception carrier
        // carries the REAL message instead of the hardcoded "exception thrown".
        var exceptionMessages = new Dictionary<string, List<Expr>>();
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
        var handlerStarts = ctx.ExceptionRegions?.Select(r => r.HandlerOffset).ToHashSet() ?? new HashSet<int>();

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
            foreach (var r in ctx.ExceptionRegions.Where(r => r.Kind == ExceptionRegionKind.Catch))
            {
                var idx = il.FindIndex(i => i.Offset >= r.HandlerOffset && i.Offset < r.HandlerOffset + r.HandlerLength);
                if (idx >= 0 && OpcodeConsumesStack(il[idx].OpCode))
                    catchEntryOffsets.Add(r.HandlerOffset);
            }
        }

        foreach (var ins in il)
        {
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
                    // ldloca: local variable address (M3 Task 11: value-type ctor this/byref).
                    Push(new LocAddrExpr { Name = LocName(ctx, (int)ins.Operand) }, ins);
                    break;
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
                    Push(new StaticFieldExpr { FieldName = FieldName(md, ins) }, ins); break;
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
                    stmts.Add(new AssignStmt { Target = FieldName(md, ins), Value = stack.Pop(), SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) }); break;
                case ILOpCode.Ldfld:
                    Push(new FieldExpr
                    {
                        Object = stack.Pop(),
                        FieldCppName = SanitizeField(FieldToken(md, ins)),
                        OwnerFullName = OwnerFullName(md, ins),
                        IsRefField = IsRefField(md, ins),
                        IsValueTypeObject = types != null && IsValueTypeOwner(OwnerFullName(md, ins), types)
                    }, ins);
                    break;
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
                    EmitNewObj(stmts, stack, temps, ins, md, types, exceptionMessages);
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
                    // leave: normal exit of a try/finally block. M3 emits the
                    // try block inline and the finally/catch handler after it,
                    // so a leave is a no-op ONLY when its target is the code
                    // right AFTER the enclosing region's HANDLER -- the
                    // emitted C++ shape's natural continuation (try closes,
                    // handler runs, control continues after the construct).
                    // Real Roslyn output targets handlerEnd from BOTH the try
                    // body and the handler body (verified: TryDiv leave @0x06
                    // -> 0x0D = handlerEnd, never tryEnd). Any other target
                    // (break/continue/goto out of the try, e.g.) would
                    // silently produce wrong control flow (audit H2) --
                    // reject loudly; the full leave semantics (run finally,
                    // then jump) is a future milestone.
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
                        // IMMEDIATELY enclosing construct's chain end
                        // (leave @0x00C1 -> 0x00DB = the middle lock's end,
                        // while the inner finally ends at 0x00CF and the
                        // outermost at 0x00F6). The emitted C++ shape (M3)
                        // lands at EVERY chain end of the containment chain
                        // naturally: each construct's finally is emitted
                        // inline right after its try body, so falling through
                        // from the innermost body runs exactly the enclosing
                        // finallys the leave semantics would run and arrives
                        // at the target. Hence ANY chain end along the
                        // containment chain is a valid no-op; anything else
                        // stays rejected (audit H2).
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
                        // (SetResult must NOT run on the exception path). The
                        // emitted C++ naturally falls off the end of the
                        // handler chain to the function's closing brace; in
                        // the minimal synchronous surface both paths are
                        // runtime-unreachable (SetException rethrows out of
                        // MoveNext; AwaitUnsafeOnCompleted throws loudly), so
                        // the fall-through never executes.
                        var terminalOffset = il.Count > 0 ? il.Max(i => i.Offset) : -1;
                        if (!chainEnds.Contains(leaveTarget) && leaveTarget != terminalOffset)
                        {
                            Console.Error.WriteLine($"warning: leave target 0x{leaveTarget:X4} != any chain end in {ctx.MethodFullName} -- skipping fold");
                            return stmts;
                        }
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
                    if (thrown is LocExpr tl2 && exceptionMessages.TryGetValue(tl2.Name, out var msgArgs))
                    {
                        thrownStmt.Args.AddRange(msgArgs);
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
                    // IR: CallExpr -> il2cpp_box_int32(&box_class, value)。
                    // M5 wiring: box result types are recorded in objTypes so
                    // later calls on the boxed value (WriteLine(object) with a
                    // known box type, ToString on a boxed primitive) resolve
                    // through the receiver-type-aware path.
                    {
                        var boxed = new CallExpr { Function = $"box_{ins.Operand}", ReturnsRef = true };
                        boxed.Args.Add(stack.Pop());
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
                            : UnboxUserStructKind(unboxType, types);
                        Push(new CallExpr { Function = $"unbox_{ins.Operand}", ReturnsRef = false, ResultKind = kind, Args = { stack.Pop() } }, ins);
                    }
                    break;
                case ILOpCode.Isinst:
                    // isinst: obj -> il2cpp_is_instance(obj, &Type_class)。
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
                        if (ccArg is LocExpr ccl && objTypes.TryGetValue(ccl.Name, out var cct) && stack.Peek() is LocExpr ccres)
                            objTypes[ccres.Name] = cct;
                    }
                    break;
                case ILOpCode.Newarr:
                    // newarr: pops the length -> allocates the array. M3:
                    // int32 arrays (type token = Int32).
                    // IR: CallExpr -> il2cpp_array_new(nullptr, 4, false, length)。
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
                        if (md != null && stack.Peek() is LocExpr nalt)
                        {
                            try { objTypes[nalt.Name] = TypeFullNameFromToken(md, (int)ins.Operand) + "[]"; }
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
                        var ld = new CallExpr
                        {
                            Function = $"ldelem_{ins.OpCode}",
                            ReturnsRef = ldelemIsRef,
                            ResultKind = ldelemIsRef ? null : LdelemResultKind(ins.OpCode),
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
                        var st = new CallExpr { Function = $"stelem_{ins.OpCode}", ReturnsRef = false };
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
                    {
                        var ldelemaIdx = stack.Pop();
                        var ldelemaArr = stack.Pop();
                        Push(new CallExpr
                        {
                            Function = $"ldelema_{ins.Operand}",
                            ResultKind = "ptr",
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
                        var genericLdOp = MapGenericLdelemOp(md, (int)ins.Operand, types);
                        if (genericLdOp == null)
                            throw new NotSupportedException(
                                $"ldelem at offset 0x{ins.Offset:X4}: element type needs the token-based emitter (8-byte primitive or user value type) -- CodeGenerator patch, see the ilop2 report");
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
                        var genericStOp = MapGenericStelemOp(md, (int)ins.Operand, types);
                        if (genericStOp == null)
                            throw new NotSupportedException(
                                $"stelem at offset 0x{ins.Offset:X4}: element type needs the token-based emitter (8-byte primitive or user value type) -- CodeGenerator patch, see the ilop2 report");
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
                    // arglist (0xFE00, ECMA-335 III.3.9 -- varargs): pushes a
                    // runtime varargs handle (System.RuntimeArgumentHandle) for
                    // the C# __arglist / __arglist_reference varargs protocol.
                    // The transpiler has no vararg calling-convention model
                    // (the generated C++ functions have fixed signatures), so
                    // the fold stays LOUD (research + decision in the ilop2
                    // report).
                    throw new NotSupportedException(
                        $"arglist at offset 0x{ins.Offset:X4}: varargs (System.RuntimeArgumentHandle) has no transpiler model (research + decision in the ilop2 report)");
                case ILOpCode.Endfilter:
                    // endfilter (0xFE11, ECMA-335 III.3.21 -- end a filter
                    // clause): pops the int32 filter result and transfers to the
                    // matching catch. The transpiler models catch/finally but
                    // not filter clauses (VB-only emission; C# never produces
                    // them), so the fold stays LOUD.
                    throw new NotSupportedException(
                        $"endfilter at offset 0x{ins.Offset:X4}: filter exception clauses have no EH model (VB-only IL; research + decision in the ilop2 report)");
                case ILOpCode.Mkrefany:
                case ILOpCode.Refanyval:
                case ILOpCode.Refanytype:
                    // TypedReference triple (mkrefany 0xC6 / refanyval 0xC2 /
                    // refanytype 0xFE1D, ECMA-335 III.4.22/III.4.26/III.4.23):
                    // a typed reference (16 bytes on net48: RuntimeType type +
                    // IntPtr value) has no IR/runtime representation -- no
                    // carrier exists for the 2-slot value the instructions
                    // pass. The fold stays LOUD (net48 TypedReference research +
                    // minimal-design proposal in the ilop2 report).
                    throw new NotSupportedException(
                        $"{OpName(ins.OpCode)} at offset 0x{ins.Offset:X4}: TypedReference (mkrefany/refanyval/refanytype) has no IR/runtime representation (net48 semantics research + design in the ilop2 report)");
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
        // Tail-call optimization (recursion-audit §6a): self-recursive tail
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
                    VirtualCallExpr ve2 => ve2.ReturnsRef ? "r" : "i",
                    _ => "r",
                };
                stmts.Add(new AssignStmt
                {
                    Target = name,
                    Value = e,
                    SourceOffset = ins.Offset,
                    SourceOpName = OpName(ins.OpCode)
                });
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
        void EmitStore(string target, IlInstruction ins)
        {
            var value = stack.Pop();
            var bottomToTop = BottomToTop(stack);
            var rebuilt = new List<Expr>(bottomToTop.Count);
            foreach (var e in bottomToTop)
            {
                if (ReferencesTarget(e, target))
                {
                    var name = $"t{temps.Count}";
                    temps.Add(name);
                    tempWidths[name] = ExprWidth(e, ctx, tempWidths);
                    stmts.Add(new AssignStmt { Target = name, Value = e, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
                    rebuilt.Add(new LocExpr { Name = name });
                }
                else rebuilt.Add(e);
            }
            stack.Clear();
            foreach (var e in rebuilt) stack.Push(e); // rebuilt is bottom-to-top; pushing in order reproduces the same order
            stmts.Add(new AssignStmt { Target = target, Value = value, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
            // M5 wiring: propagate the recorded managed type through the
            // store (`object d = 3.14;` -- the box temp's type follows into
            // the local so WriteLine(d) resolves the boxed type).
            PropagateObjType(target, value);
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
            // Locate the newarr allocation assignment: the array length and
            // element type token are both in it.
            var newarrStmt = stmts.LastOrDefault(s => s is AssignStmt a && a.Target == arrLoc.Name
                && a.Value is CallExpr c && c.Function.StartsWith("newarr_", StringComparison.Ordinal));
            if (newarrStmt is not AssignStmt { Value: CallExpr newArr }
                || !int.TryParse(newArr.Function[7..], out var elemToken))
                throw new NotSupportedException(
                    $"InitializeArray at offset 0x{ins.Offset:X4}: cannot locate the newarr allocation for temp {arrLoc.Name}");
            if (newArr.Args.Count != 1 || newArr.Args[0] is not IntConstExpr lenConst)
                throw new NotSupportedException(
                    $"InitializeArray at offset 0x{ins.Offset:X4}: the array length is not a constant (the initializer pattern requires `new T[N] {{...}}` with constant N)");
            var count = lenConst.Value;
            if (count <= 0)
                throw new NotSupportedException(
                    $"InitializeArray at offset 0x{ins.Offset:X4}: non-positive array length {count} in the initializer pattern");
            var elemName = TypeFullNameFromToken(md, elemToken);
            var width = ElementWidth(elemName);
            if (width == null)
                throw new NotSupportedException(
                    $"InitializeArray at offset 0x{ins.Offset:X4}: element type {elemName} not supported (the array-initializer fold supports byte/sbyte/bool, char/short, int/uint/float, long/ulong/double)");
            var stelemOp = (width, elemName) switch
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
            var need = (int)(count * width);
            var bytes = ReadRvaBytes(pe, rva, need);
            if (bytes == null)
                throw new NotSupportedException(
                    $"InitializeArray at offset 0x{ins.Offset:X4}: cannot read {need} byte(s) of RVA data at 0x{rva:X8} (field {md.GetString(field.Name)})");
            for (var i = 0; i < count; i++)
            {
                Expr val = (width, elemName) switch
                {
                    (1, _) => new IntConstExpr { Value = bytes[i] },
                    (2, _) => new IntConstExpr { Value = BitConverter.ToInt16(bytes, i * 2) },
                    (4, "System.Single") => new DoubleConstExpr { Value = BitConverter.ToSingle(bytes, i * 4) },
                    (4, _) => new IntConstExpr { Value = BitConverter.ToInt32(bytes, i * 4) },
                    (8, "System.Double") => new DoubleConstExpr { Value = BitConverter.ToDouble(bytes, i * 8) },
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
                // P-D3 (§7.3 width assertions): a calli RESULT is typed from
                // its StandAloneSig return byte -- the single-use probes only
                // worked because OptimizeTemps INLINED the call into the
                // consumer; a MULTI-USE result lands in a temp whose
                // declaration falls to ExprKind(CallExpr). With ResultKind
                // null it became int32_t and silently TRUNCATED the x64
                // int64/double/pointer return (callimulti probe: `int32_t t0
                // = (int64_t)(loc0)(...)`). Mirror UserCallResultKind's
                // widths so the result slot matches CalliReturnCpp.
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
                    || first is LocExpr ptrTemp && LocWidth(ptrTemp.Name, ctx, tempWidths) == "ptr";
                // M5 wiring: the SECOND stack value's tracked type dispatches
                // String.Concat(object, object) numeric conveniences (the
                // second argument is the boxed number).
                if (stack.Count > 1 && stack.ElementAt(1) is LocExpr sl && objTypes.TryGetValue(sl.Name, out var st1))
                    arg1Type = st1;
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
                Push(new StringConstExpr { Value = TypeNameForReflection(md, (int)ldtMarker.Value) }, ins);
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
            if (md != null && prevIns is IlInstruction { OpCode: ILOpCode.Call } prevCall
                && IsGetTypeFromHandle(md, (int)prevCall.Operand)
                && stack.Count > 0 && stack.Peek() is StringConstExpr foldedType)
            {
                if (IsTypeGetName(md, (int)ins.Operand))
                {
                    stack.Pop();
                    Push(new StringConstExpr { Value = ShortTypeName(foldedType.Value) }, ins);
                    return;
                }
                if (IsTypeGetTypeCode(md, (int)ins.Operand))
                {
                    stack.Pop();
                    Push(new IntConstExpr { Value = TypeCodeOfTypeName(foldedType.Value) }, ins);
                    return;
                }
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

            var resolved = md != null
                ? MethodRefResolver.Resolve(md, (int)ins.Operand, types ?? Array.Empty<TypeDef>(), null, arg0IsAddr, arg0Type, arg1Type)
                : null;
            var paramCount = resolved?.ParamCount ?? 0;
            var args = new List<Expr>();
            try
            {
                for (var i = 0; i < paramCount; i++) args.Insert(0, stack.Pop());
            }
            catch (InvalidOperationException)
            {
                throw new NotSupportedException($"call at offset 0x{ins.Offset:X4} pops {paramCount} args but the stack has only {stack.Count} (resolved={resolved?.CppName ?? "null"})");
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
                if (md != null && recvType == null
                    && prevIns is { OpCode: ILOpCode.Nop, OperandKind: IlOperandKind.TypeToken } constrainedMarker)
                {
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
                        || (args[0] is LocExpr ptrRecv && LocWidth(ptrRecv.Name, ctx, tempWidths) == "ptr")
                        // icallwide: the Object::ToString/GetHashCode default
                        // arms are receiver-agnostic first-pass results -- a
                        // tracked typed receiver (Version/TimeSpan/...) must
                        // re-resolve to the domain icall.
                        || resolved.CppName.EndsWith("icall_object_get_hash_code")
                        || resolved.CppName.EndsWith("icall_object_to_string")
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
                        || recvType == "System.Collections.Generic.SortedDictionary`2.KeyCollection.Enumerator"))
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
                        _ when types?.Any(t => t.FullName == constrainedRecvType
                            && t.BaseTypeName == "System.Enum") == true
                            => new ResolvedMethod("il2cpp::runtime::il2cpp_enum_to_string", 0, false, true, true,
                                new[] { false }, true, ParamTypeNames: new[] { "enumval" },
                                PrependTypeClass: constrainedRecvType),
                        _ => resolved,
                    };
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
                            objTypes[rt.Name] = resolved.ReturnTypeName;
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

                // M4 Task 5: System.String instance members map to runtime
                // helpers (Length etc.) -- a direct call, not vtable dispatch.
                // M5 wiring: the same holds for EVERY runtime icall instance
                // method (StringBuilder/Random/Hashtable/String members --
                // CppName starts with "il2cpp::").
                if (resolved is { IsPInvoke: false } && IsRuntimeDirect(resolved))
                {
                    var shelp = new CallExpr { Function = $"call_{ins.Operand}", ReturnsRef = resolved.ReturnsRef, ResultKind = resolved.ResultKind, Resolved = resolved };
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
                            objTypes[rt.Name] = resolved.ReturnTypeName;
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
                    if (md != null && stack.Peek() is LocExpr rtv
                        && ReturnIsVar(md, (int)ins.Operand)
                        && (VarArgRefFullName(md, (int)ins.Operand) ?? SharedReturnConcreteType(md, (int)ins.Operand)) is string sharedRet)
                        objTypes[rtv.Name] = sharedRet;
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
                // M4 Task 3: P/Invoke string args are marshalled at the call site.
                IsPInvoke = resolved?.IsPInvoke ?? false,
                // M4 Task 4: [DllImport(SetLastError=true)].
                SetLastError = resolved?.SetLastError ?? false,
            };
            if (resolved?.IsPInvoke == true && resolved.ParamTypeNames != null)
            {
                for (var i = 0; i < resolved.ParamTypeNames.Length; i++)
                    if (resolved.ParamTypeNames[i] is "r:System.String" or "u16:System.String")
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
            }

            // M5 wiring: for STATIC calls, re-resolve with the FIRST explicit
            // argument's tracked type -- Console.WriteLine(object) with a
            // known boxed value becomes the typed WriteLine (unboxed arg);
            // unknown object types fall through to the default mapping.
            // For non-virtual INSTANCE calls the receiver is args[0] -- its
            // address/value shape decides the receiver cast (ldloca -> deref,
            // boxed -> unbox); the pre-pop stack top was the LAST argument.
            if (md != null && args.Count > 0 && resolved is { IsInstance: false }
                && !resolved.CppName.StartsWith("il2cpp::", StringComparison.Ordinal))
            {
                var a0Type = args[0] is LocExpr fl2 && objTypes.TryGetValue(fl2.Name, out var tt2) ? tt2 : null;
                if (a0Type != null)
                    resolved = MethodRefResolver.Resolve(md, (int)ins.Operand, types ?? Array.Empty<TypeDef>(),
                        a0Type, args[0] is LocAddrExpr, a0Type);
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
            // Work order #24: Path.Combine(string,string,string) -- the
            // params-array form. The resolver emits the NestedCombineMarker
            // (the runtime icall takes exactly (string,string)); expand the
            // 3-arg call into a nested CallExpr tree
            // icall_path_combine(icall_path_combine(a,b), c). The emitter
            // handles a CallExpr as an argument (EmitIcallCallArgs recurses
            // through EmitExpr), so the nested tree emits as nested C++
            // calls. (iocase line 69: Directory.CreateDirectory(
            // Path.Combine(dir, "nested", "deep")).)
            if (resolved != null && resolved.CppName == ResolvedMethod.NestedCombineMarker)
            {
                if (args.Count != 3)
                    throw new NotSupportedException($"nested-combine marker with {args.Count} arg(s) (expected exactly 3)");
                var combine2 = new ResolvedMethod("il2cpp::runtime::icall_path_combine", 2, false, true, true,
                    new[] { false, false }, false);
                var inner = new CallExpr
                {
                    Function = $"call_{ins.Operand}",
                    ReturnsRef = true,
                    Resolved = combine2,
                };
                inner.Args.Add(args[0]);
                inner.Args.Add(args[1]);
                var outer = new CallExpr
                {
                    Function = $"call_{ins.Operand}",
                    ReturnsRef = true,
                    Resolved = combine2,
                };
                outer.Args.Add(inner);
                outer.Args.Add(args[2]);
                Push(outer, ins);
                if (stack.Peek() is LocExpr ncTemp)
                    objTypes[ncTemp.Name] = "System.String";
                return;
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
                if (args.Count != 2)
                    throw new NotSupportedException(
                        $"Enumerable.ThenBy needs exactly 2 args (receiver + keySelector), got {args.Count}");
                if (args[0] is not LocExpr recvLoc || recvLoc.Name.Length == 0)
                    throw new NotSupportedException(
                        "Enumerable.ThenBy is supported directly on a single OrderBy(keySelector) result only " +
                        "(OrderBy(k1).ThenBy(k2) -- D-LO2-1 stage-1 surface); deeper ThenBy chains and non-OrderBy " +
                        "receivers have no fused-composite runtime entry");
                int obIndex = -1;
                for (int si = stmts.Count - 1; si >= 0; --si)
                {
                    if (stmts[si] is AssignStmt cand && cand.Target == recvLoc.Name
                        && cand.Value is CallExpr candCall
                        && candCall.Resolved?.CppName == "il2cpp::il2cpp_linq_order_by_key_i4"
                        && candCall.Args.Count == 2)
                    {
                        obIndex = si;
                        break;
                    }
                }
                if (obIndex < 0)
                    throw new NotSupportedException(
                        $"Enumerable.ThenBy receiver '{recvLoc.Name}' does not name an OrderBy(keySelector) assignment " +
                        "(OrderBy(k1).ThenBy(k2) -- D-LO2-1 stage-1 surface); deeper ThenBy chains and non-OrderBy " +
                        "receivers have no fused-composite runtime entry");
                var obAssign = (AssignStmt)stmts[obIndex];
                var obValue = (CallExpr)obAssign.Value;
                var thenDesc = resolved.CppName == ResolvedMethod.ThenByDescCombineMarker;
                var composite = new CallExpr
                {
                    Function = $"call_{ins.Operand}",
                    ReturnsRef = true,
                    ResultKind = null,
                    Resolved = new ResolvedMethod(
                        thenDesc
                            ? "il2cpp::il2cpp_linq_then_by_key_desc_i4"
                            : "il2cpp::il2cpp_linq_then_by_key_i4",
                        3, false, true, true, new[] { false, false, false }, false,
                        ParamTypeNames: new[] { "a", "", "" },
                        ReturnTypeName: "System.Int32[]"),
                    IsPInvoke = false,
                    SetLastError = false,
                };
                composite.Args.Add(obValue.Args[0]); // the ORIGINAL source array
                composite.Args.Add(obValue.Args[1]); // the PRIMARY key selector
                composite.Args.Add(args[1]);         // the SECONDARY key selector
                // Replace the order_by assignment (remove from its old spot --
                // before the secondary delegate -- and append at the current
                // end so every referenced delegate is already defined).
                stmts.RemoveAt(obIndex);
                stmts.Add(new AssignStmt
                {
                    Target = recvLoc.Name,
                    Value = composite,
                    SourceOffset = ins.Offset,
                    SourceOpName = OpName(ins.OpCode),
                });
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
            var pinvokeFreeTemps = new List<(string Name, bool Utf16)>();
            if (call.IsPInvoke && call.StringArgIndices.Count > 0)
            {
                for (var i = 0; i < call.Args.Count; i++)
                {
                    if (!call.StringArgIndices.Contains(i)) continue;
                    var mtemp = $"t{temps.Count}";
                    temps.Add(mtemp);
                    // wf2 (winforms1): CharSet.Unicode -> UTF-16 marshal
                    // (il2cpp_pinvoke_string_to_utf16_alloc / utf16_free).
                    var isUtf16 = call.Resolved?.ParamTypeNames != null
                        && i < call.Resolved.ParamTypeNames.Length
                        && call.Resolved.ParamTypeNames[i] == "u16:System.String";
                    stmts.Add(new AssignStmt
                    {
                        Target = mtemp,
                        Value = new MarshalUtf8Expr { Arg = call.Args[i], Utf16 = isUtf16 },
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode)
                    });
                    call.Args[i] = new LocExpr { Name = mtemp };
                    pinvokeFreeTemps.Add((mtemp, isUtf16));
                }
                call.StringArgIndices.Clear(); // args are pre-marshalled now
            }
            // P-G3 R-E (GENERIC-SHARE-DESIGN.md §5.2): a SHARED generic
            // method's byref T params are Il2CppObject** slots (the shared
            // object ABI). A VALUE argument (int32 local) must be BOXED into a
            // bridge temp whose ADDRESS is passed, then UNBOXED back into the
            // local after the call:
            //   tN = il2cpp_box_int32(x);   call(fn, &tN);  x = il2cpp_unbox_int32(tN);
            // A REFERENCE slot (&string local) passes through untouched (the
            // slot already IS an Il2CppObject*, reinterpreted as
            // Il2CppObject**). The bridge temp is declared Il2CppObject*
            // (ExprKind of the box call -> "ref") and GC-rooted.
            var byrefBridgeSuffix = new List<AssignStmt>();
            var sharedByref = md != null ? SharedByrefVarSlots(md, (int)ins.Operand) : null;
            if (sharedByref != null)
            {
                var argOffset = resolved is { IsInstance: true } ? 1 : 0; // this precedes explicit params
                for (var bi = 0; bi < sharedByref.Length && bi + argOffset < call.Args.Count; bi++)
                {
                    if (!sharedByref[bi]) continue;
                    var bArgIdx = bi + argOffset;
                    if (call.Args[bArgIdx] is not LocAddrExpr laB || LocWidth(laB.Name, ctx, tempWidths) == "r")
                    {
                        // P-G4 ②: a field ADDRESS argument (ref obj.field) to a
                        // SHARED byref slot. A REFERENCE-typed field's address
                        // is &f == Il2CppObject** -> the callee's Il2CppObject**
                        // slot matches directly (zero-copy, correct). A VALUE
                        // slot field's address is &f == int32_t*/... -> an ABI
                        // mismatch that cannot be bridged (the box-to-temp-then-
                        // write-back bridge is local-only). Loud-reject the value
                        // slot shape here (the fold catch turns it into a stub +
                        // warning); the complete field bridge is future work
                        // (P-G5).
                        if (SharedByrefFieldArgSlotIsValue(md, types, call.Args[bArgIdx]))
                            throw new NotSupportedException("shared byref arg = ref field ADDRESS over a VALUE slot (ref obj.field); 请先经局部 ref 中转（P-G5 字段桥）");
                        continue; // reference slot: &slot is Il2CppObject**-compatible (zero-copy)
                    }
                    var btemp = $"t{temps.Count}";
                    temps.Add(btemp);
                    tempWidths[btemp] = "r";
                    // D5 (surface 4): the box/unbox bridge is TYPE-AWARE --
                    // a VALUE slot's box/unbox helper is keyed on the shared
                    // MVAR param's CONCRETE type (the MethodSpec genArg:
                    // int32 -> il2cpp_box_int32, double -> icall_box_double,
                    // ...). The old int32-only bridge read only the low 32
                    // bits of a double/int64 slot.
                    var (boxFn, boxDeref, unboxFn, unboxKind) =
                        SharedByrefConcreteBridge(md!, (int)ins.Operand, bi)
                        ?? ("il2cpp::il2cpp_box_int32", "deref:i", "il2cpp::il2cpp_unbox_int32", "i");
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
                    boxCall.Args.Add(laB);
                    stmts.Add(new AssignStmt
                    {
                        Target = btemp,
                        Value = boxCall,
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode),
                    });
                    call.Args[bArgIdx] = new LocAddrExpr { Name = btemp };
                    var unboxCall = new CallExpr
                    {
                        Function = unboxFn,
                        ReturnsRef = false,
                        ResultKind = unboxKind,
                        Resolved = new ResolvedMethod(unboxFn, 1, false, true, false,
                            new[] { true }, false),
                    };
                    unboxCall.Args.Add(new LocExpr { Name = btemp });
                    byrefBridgeSuffix.Add(new AssignStmt
                    {
                        Target = laB.Name,
                        Value = unboxCall,
                        SourceOffset = ins.Offset,
                        SourceOpName = OpName(ins.OpCode),
                    });
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
                    objTypes[rt2.Name] = resolved.ReturnTypeName;
                // D5 (surface 4): a SHARED method-level generic method's MVAR
                // return (PickUser2::Pick<int> -- the callee emits the boxed
                // object slot) records the CONCRETE T on the result temp. The
                // callvirt arm already does this via SharedReturnConcreteType;
                // a STATIC shared method arrives through the `call` path, so it
                // is recorded here too -- the consumer (Console.WriteLine /
                // Int32::ToString) then unboxes by the tracked type.
                else if (md != null && stack.Peek() is LocExpr rtc
                    && SharedReturnConcreteType(md, (int)ins.Operand) is string callSharedRet)
                    objTypes[rtc.Name] = callSharedRet;
                foreach (var mt in pinvokeFreeTemps)
                    stmts.Add(new CallStmt { Function = mt.Utf16 ? "pinvoke_utf16_free" : "pinvoke_utf8_free", Args = { new LocExpr { Name = mt.Name } }, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
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
                    stmts.Add(new CallStmt { Function = mt.Utf16 ? "pinvoke_utf16_free" : "pinvoke_utf8_free", Args = { new LocExpr { Name = mt.Name } }, SourceOffset = ins.Offset, SourceOpName = OpName(ins.OpCode) });
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
            Dictionary<string, List<Expr>> exceptionMessages)
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

            // M3 Task 11: value-type newobj -- a struct instance is
            // initialized IN PLACE (no GC allocation; the value lives in the
            // declaring scope). The newobj folds to: {T} __t; {T}__ctor(&__t, args);
            // M5 wiring: the BCL 8-byte value types (System.DateTime /
            // System.TimeSpan) are runtime structs (Il2CppDateTime /
            // Il2CppTimeSpan) -- the same in-place path, with the ctor
            // resolved to the runtime icall (ResultIsReceiver).
            var isValueTypeNewobj = (types?.FirstOrDefault(t => t.FullName == typeFullName && t.IsValueType) != null)
                || typeFullName is "System.DateTime" or "System.TimeSpan" or "System.Decimal"
                // P14 dtoffset: System.DateTimeOffset is a BCL value type
                // (Il2CppDateTimeOffset -- the same in-place path).
                || typeFullName is "System.DateTimeOffset"
                // P17 biginteger: System.Numerics.BigInteger is a BCL value
                // type (Il2CppBigInteger -- the same in-place path).
                || typeFullName is "System.Numerics.BigInteger"
                // wf2 (winforms1): System.IntPtr/UIntPtr are BCL VALUE types.
                || typeFullName is "System.IntPtr" or "System.UIntPtr";
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
            // (ArgumentException/InvalidOperationException/... -- no runtime
            // class descriptor exists; the typed throw carries the carrier).
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
                && !(types?.Any(t => t.FullName == typeFullName) ?? false);
            // ilistException: a System.Exception built with a message
            // (`new Exception("a")`) and USED as a value (added to a list,
            // .Message read) needs a REAL object identity -- the message
            // side table keys by pointer, and a null model has no identity
            // to key on. Allocate really (BclAllocExpr routes System.Exception
            // to a plain-object allocation); the throw-site carrier path
            // above is untouched. All other BCL exceptions keep the null
            // model (throw-only paths).
            var isMessageCarrierException = typeFullName == "System.Exception" && (resolved?.ParamCount ?? 0) >= 1;
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
            // Only the .ctor(string) message and .ctor(string,string)
            // paramName+message shapes are captured; any other shape leaves the
            // throw with the default message (the emitter's fallback).
            if (isBclException)
            {
                exceptionMessages[name] = new List<Expr>(args);
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

        // G2 (audit recurse-recheck f14b): whether the call is
        // System.Object::ToString() -- Roslyn emits ToString on an `object`
        // typed local as a MemberRef on the System.Object TypeRef (BCL members
        // never carry a MethodDefinition token). Read the param count from the
        // signature header: only the 0-param (the sole .NET Object.ToString
        // signature). On a hit, a callvirt with an unknown receiver type falls
        // back to icall_object_to_string instead of the vtable.
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
                var td = types.FirstOrDefault(t => t.FullName == name);
                if (td != null) return !td.IsValueType;
            }
            return name is not ("System.Byte" or "System.SByte" or "System.Boolean"
                or "System.Int16" or "System.UInt16" or "System.Char"
                or "System.Int32" or "System.UInt32" or "System.Single"
                or "System.Int64" or "System.UInt64" or "System.Double"
                or "System.IntPtr" or "System.UIntPtr"
                or "System.DateTime" or "System.TimeSpan" or "System.Decimal" or "System.Guid");
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
                    var defType = sig.ReadByte();
                    return (defType != 0x11, null, "?"); // CLASS inst -> ref; VALUETYPE inst -> value (width unknown)
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
        {
            var (_, _, name) = ElementAccessInfo(md, token, types);
            return name == "System.Decimal";
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

        // Type full name -> TypeCode value: the net48 constant table, a mirror
        // of the runtime's icall_object_get_type_code (typecheck.cpp 181-215)
        // -- Int32=9 Boolean=3 Char=4 Double=14 String=18, else 1
        // (TypeCode.Object). Matches the runtime table entry-for-entry
        // (Single=13 and other net48 values are outside the runtime's M4 scope;
        // both paths fall back to 1 -- keeping consistency).
        static int TypeCodeOfTypeName(string fullName) => fullName switch
        {
            "System.Int32" => 9,
            "System.Boolean" => 3,
            "System.Char" => 4,
            "System.Double" => 14,
            "System.String" => 18,
            // decimal-domain T3: net48 TypeCode.Decimal == 15 (mirrors the
            // typecheck.cpp/typeext.cpp table entries -- keep all three in sync).
            "System.Decimal" => 15,
            _ => 1,
        };

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
                || fullName.StartsWith("System.Comparison`", StringComparison.Ordinal))
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
                throw new NotSupportedException($"TypeSpec signature byte 0x{header:X2} not GenericInst(0x15) in M3");
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
            if (sig.ReadByte() == 0x13)
            {
                _ = sig.ReadCompressedInteger(); // VAR index (single instantiation: T -> int)
                return "System.Int32";
            }
            return "System.Runtime.CompilerServices.TaskAwaiter`1";
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
                        var parts = hint.Split('_');
                        if (vIdx < parts.Length)
                            return SuffixToManagedTypeName(parts[vIdx]);
                    }
                    return "System.Int32"; // M3 single-instantiation default
                }
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
                _ = sig.ReadCompressedInteger();
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
            return TypeSpecName(md, hs); // remaining legacy shapes unchanged
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
                throw new NotSupportedException($"TypeSpec signature byte 0x{header:X2} not GenericInst(0x15) in M3 (newobj ctor-type resolution)");
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
            return defName + "_" + string.Join("_", args);
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
                0x1D => "array",
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
            s.ReadCompressedInteger(); // VAR index byte follows 0x13
            return MethodRefResolver.InstantiationSuffixHint ?? "int";
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
            if (typeByte is 0x0E or 0x12 or 0x1C) return true;
            if (typeByte == 0x15 && md != null) return IsGenericInstRefCarrier(md, ins);
            return false;
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
            var s = string.Join("_", name.Split('.', '+', '`', '<', '>', ',', '[', ']'));
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
            CallExpr c => c.ResultKind ?? "i",
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
            LocExpr l => LocWidth(l.Name, ctx, tempWidths) is not ("l" or "d" or "r"),
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
                or ILOpCode.Stfld or ILOpCode.Stsfld
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
        return ret is 0x0A or 0x0B ? "l" : ret is 0x0C or 0x0D ? "d" : ret is 0x17 or 0x18 ? "ptr" : ret is 0x1B ? "p" : null;
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
            HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)callHandle).Parent is { Kind: HandleKind.TypeSpecification } par
                && !GenericInstAllInt(md, (TypeSpecificationHandle)par)
                ? GenericInstDefName(md, (TypeSpecificationHandle)par) is { } gdn
                    && GenericUsageAnalyzer.SharedGenericDefs?.Contains(gdn) == true
                : false,
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

    // P-G3: whether a closed generic-instance TypeSpec's arguments are ALL the
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
        var (_, _, byrefVar) = MethodSignatureCoder.DecodeVarFlags(md, dm.Signature);
        return byrefVar;
    }

    // D5: the box/unbox bridge pair for a SHARED method-level generic method's
    // byref MVAR param, keyed on the MethodSpec's CONCRETE generic argument
    // (the value slot's managed type: int32 -> il2cpp_box_int32 /
    // il2cpp_unbox_int32, double -> icall_box_double / icall_unbox_double, ...).
    // The old bridge hardcoded int32; a double slot would box/unbox only its
    // low 32 bits. Returns the legacy int32 pair for undecodable concrete types
    // (conservative -- the pre-D5 default) and null for non-shared / non-byref
    // shapes (caller keeps the zero-copy pass-through).
    static (string BoxFn, string BoxDeref, string UnboxFn, string UnboxKind)? SharedByrefConcreteBridge(
        MetadataReader md, int token, int paramIndex)
    {
        var h = MetadataTokens.Handle(token);
        if (h.Kind != HandleKind.MethodSpecification) return null;
        var ms = md.GetMethodSpecification((MethodSpecificationHandle)h);
        if (ms.Method.Kind != HandleKind.MethodDefinition) return null;
        var dm = md.GetMethodDefinition((MethodDefinitionHandle)ms.Method);
        if (GenericUsageAnalyzer.SharedGenericDefs?.Contains(md.GetFullName(dm.GetDeclaringType())) != true) return null;
        var mvIdx = ByrefParamMvarIndex(md, dm, paramIndex);
        if (mvIdx < 0) return null;
        return MethodSpecGenArgFullName(md, ms.Signature, mvIdx) switch
        {
            "System.Double" => ("il2cpp::runtime::icall_box_double", "deref:d", "il2cpp::runtime::icall_unbox_double", "d"),
            "System.Int64" => ("il2cpp::runtime::icall_box_long", "deref:l", "il2cpp::runtime::icall_unbox_long", "l"),
            "System.Boolean" => ("il2cpp::runtime::icall_box_bool", "deref:b", "il2cpp::runtime::icall_unbox_bool", "b"),
            "System.Char" => ("il2cpp::runtime::icall_box_char", "deref:c", "il2cpp::runtime::icall_unbox_char", "c"),
            "System.Byte" => ("il2cpp::runtime::icall_box_byte", "deref:u1", "il2cpp::runtime::icall_unbox_byte", "b"),
            "System.SByte" => ("il2cpp::runtime::icall_box_sbyte", "deref:i", "il2cpp::runtime::icall_unbox_sbyte", "b"),
            "System.Int16" => ("il2cpp::runtime::icall_box_short", "deref:u2", "il2cpp::runtime::icall_unbox_short", "b"),
            "System.UInt16" => ("il2cpp::runtime::icall_box_ushort", "deref:u2", "il2cpp::runtime::icall_unbox_ushort", "c"),
            "System.UInt32" => ("il2cpp::runtime::icall_box_uint", "deref:ui", "il2cpp::runtime::icall_unbox_uint", "i"),
            "System.UInt64" => ("il2cpp::runtime::icall_box_ulong", "deref:ul", "il2cpp::runtime::icall_unbox_ulong", "l"),
            "System.Single" => ("il2cpp::runtime::icall_box_float", "deref:d", "il2cpp::runtime::icall_unbox_float", "d"),
            // Int32 / undecodable concrete type -> the legacy int32 pair.
            _ => ("il2cpp::il2cpp_box_int32", "deref:i", "il2cpp::il2cpp_unbox_int32", "i"),
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

    // P-G4 ②: whether a shared-byref ARGUMENT that is a FIELD ADDRESS (ldflda
    // -> FieldAddrExpr / ldsflda -> StaticFieldAddrExpr) targets a VALUE slot.
    // A REFERENCE-typed field's address is &f == Il2CppObject** -- the shared
    // callee's Il2CppObject** byref slot matches it directly (zero-copy pass-
    // through is CORRECT). A VALUE slot field's address is &f == int32_t*/...
    // -- ABI-mismatched, and no field-write-back bridge exists yet. The audit
    // ruling (P-G4-AUDIT-2.md §11.2): only the value-slot shape is loudly
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
    // "宁可特例化").
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
    // copy so the ② field-address helper can classify field slots outside
    // Fold's scope.
    static string CppFieldName(string name)
    {
        var s = string.Join("_", name.Split('.', '+', '`', '<', '>', ',', '[', ']'));
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

    // D5: the CONCRETE generic-argument type of a SHARED method-level generic
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

    // Read the concrete generic-argument FULL NAME at genIndex from a MethodSpec
    // GENERICINST (0x0A) blob, or null when the argument is open/unboxable.
    // The same byte table as SharedReturnConcreteType's MemberRef TypeSpec-arg
    // decode (primitives/string/object/class/value-type).
    static string? MethodSpecGenArgFullName(MetadataReader md, in BlobHandle genSigHandle, int genIndex)
    {
        var gsig = md.GetBlobReader(genSigHandle);
        if (gsig.ReadByte() != 0x0A) return null; // not GENERICINST
        var argc = gsig.ReadCompressedInteger();
        if ((uint)genIndex >= (uint)argc) return null;
        byte b = 0;
        for (var i = 0; i <= genIndex; i++)
            b = MethodSignatureCoder.SkipCustomModifiers(ref gsig, gsig.ReadByte());
        if (b is 0x11 or 0x12)
        {
            // Re-read the concrete argument's TypeDefOrRef full name.
            var gs2 = md.GetBlobReader(genSigHandle);
            if (gs2.ReadByte() != 0x0A) return null;
            var argc2 = gs2.ReadCompressedInteger();
            for (var i = 0; i < genIndex; i++)
                _ = MethodSignatureCoder.SkipCustomModifiers(ref gs2, gs2.ReadByte());
            var th = gs2.ReadTypeHandle();
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
        MarshalUtf8Expr m => new MarshalUtf8Expr { Arg = ReplaceExpr(m.Arg, temp, value), Utf16 = m.Utf16 },
        _ => e,
    };

    //------------------------------------------------------------------------------
    // Tail-call optimization (recursion-audit §6a): self-recursive tail calls
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
    // re-enters the body (audit §6a condition 4: the frame slot addresses are
    // unchanged across the loop reuse, a single push stays valid).
    //
    // Semantic-equivalence proof (audit §6a): the self-recursive tail call
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













