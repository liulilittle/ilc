using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Il;

namespace Il2Cpp.Transpiler.Ir;

//------------------------------------------------------------------------------
// Normalize -- algebraic-identity / constant-normalization pass (P0-1) plus
// null-comparison folding (P0-3), modeled on coreclr's fgMorphSmpOp
// (docs/research/coreclr-jit-optimization.md §2.3).
//
// Runs between StackFold and EmitBody (CodeGenerator.Generate). It rewrites
// every expression bottom-up. After the pass, branch conditions that became
// constants feed the EXISTING emission-time dead-branch/block machinery
// (CodeGenerator.EmitBody reachability + EmitStmt constant branches) -- the
// constant-propagation chain folds into branch pinning + dead-block removal.
//
// Equivalence argument (every rule, .NET int32 unchecked-wrap semantics):
// all rules hold modulo 2^32. Rules that REMOVE an evaluation (x*0, x&0,
// x-x, x==x, ...) are restricted to pure arithmetic subtrees
// (IsPureArithmetic: LocExpr/IntConstExpr/NullExpr and nested arithmetic
// WITHOUT Div/Rem) -- a FieldExpr read can throw NullReferenceException and
// a Div/Rem by zero throws DivideByZeroException, so removing their
// evaluation would change observable behavior. (IR calls are already
// materialized into temps by StackFold, so CallExprs never appear nested.)
//
// Rules:
//   x+0 -> x, 0+x -> x, x-0 -> x, 0-x -> -x, x*1 -> x, 1*x -> x,
//   x*0 -> 0 (pure), x&0 -> 0 (pure), x|0 -> x, x^0 -> x, x&(-1) -> x,
//   x<<0 -> x, x>>0 -> x, x-x -> 0 (pure), -(-x) -> x, ~~x -> x,
//   (x+c1)+c2 -> x+(c1+c2) (same commutative op, int32 wrap),
//   x==x -> 1, x>x -> 0, x<x -> 0 (pure; unsigned variants too),
//   null==null -> 1, obj==null -> 0 for straight-line known-non-null
//   allocations (newobj/ldstr/newarr/box) -- invalidated at labels (join
//   points) and at exception-handler starts (non-sequential entry).
//   Constant-right canonicalization for Add/Mul/And/Or/Xor enables the
//   same-op merge; both-constant operands fold via CodeGenerator.TryFoldInt32
//   (single wrap-semantics implementation, DRY).
//   Post-pass constant strength reduction (P0-2), see StrengthReduceStmts:
//   x*2^k -> x<<k, x/2^k (signed) -> (x+((x>>31)&(2^k-1)))>>k, x/2^k (uint)
//   -> x>>k; shift-count chains merge ((x<<1)<<2 -> x<<3). Replaces the
//   il2cpp_checked_div call with plain C++ shifts (safe subset only).
//------------------------------------------------------------------------------
public static class Normalize
{
    public static List<Stmt> FoldStmts(List<Stmt> stmts, IReadOnlyList<IlExceptionRegion>? regions = null)
    {
        var handlerStarts = regions?.Select(r => r.HandlerOffset).ToHashSet() ?? new HashSet<int>();
        // knownNonNull[name]: the local/temp holds a non-null allocation on
        // every straight-line path since its definition (until the next
        // assignment, label, or handler start).
        var knownNonNull = new Dictionary<string, bool>();
        var result = new List<Stmt>(stmts.Count);
        foreach (var s in stmts)
        {
            if (s is LabelStmt || handlerStarts.Contains(s.SourceOffset))
                knownNonNull.Clear();

            Stmt rewritten = s switch
            {
                AssignStmt a => new AssignStmt
                {
                    Target = a.Target,
                    Value = Rewrite(a.Value, knownNonNull),
                    SourceOffset = a.SourceOffset,
                    SourceOpName = a.SourceOpName,
                },
                ExprStmt e => new ExprStmt { Value = Rewrite(e.Value, knownNonNull), SourceOffset = e.SourceOffset, SourceOpName = e.SourceOpName },
                RetStmt r => new RetStmt { Value = r.Value == null ? null : Rewrite(r.Value, knownNonNull), SourceOffset = r.SourceOffset, SourceOpName = r.SourceOpName },
                BranchStmt b => new BranchStmt
                {
                    TargetLabel = b.TargetLabel,
                    Condition = b.Condition == null ? null : Rewrite(b.Condition, knownNonNull),
                    SourceOffset = b.SourceOffset,
                    SourceOpName = b.SourceOpName,
                },
                CallStmt c => RewriteCallStmt(c, knownNonNull),
                FieldAssignStmt f => new FieldAssignStmt
                {
                    Object = Rewrite(f.Object, knownNonNull),
                    FieldCppName = f.FieldCppName,
                    OwnerFullName = f.OwnerFullName,
                    IsRefField = f.IsRefField,
                    IsValueTypeObject = f.IsValueTypeObject,
                    RefTargetFullName = f.RefTargetFullName,
                    Value = Rewrite(f.Value, knownNonNull),
                    SourceOffset = f.SourceOffset,
                    SourceOpName = f.SourceOpName,
                },
                _ => s, // LabelStmt
            };
            result.Add(rewritten);

            // update the known-non-null set from THIS statement's assignment
            // (targets with a '.' are static fields -- not locals/temps).
            if (rewritten is AssignStmt ra && !ra.Target.Contains('.'))
            {
                if (IsNonNullAlloc(ra.Value, knownNonNull)) knownNonNull[ra.Target] = true;
                else knownNonNull.Remove(ra.Target);
            }
            // A CALL may write ANY local through a ref/out argument: the
            // known-non-null set must not survive a call, or a later null
            // comparison folds against a stale non-null fact (interlockedobj
            // e2e, batch 149 -- ReferenceEquals(e, null) after
            // Interlocked.Exchange(ref e, null) folded to 0). CallStmt and
            // assignments/expressions whose value is a CallExpr/VirtualCallExpr
            // (the transpiler materializes calls into temps, so the call
            // itself always sits in one of these shapes).
            if (rewritten is CallStmt
                || (rewritten is AssignStmt { Value: CallExpr or VirtualCallExpr })
                || (rewritten is ExprStmt { Value: CallExpr or VirtualCallExpr }))
            {
                knownNonNull.Clear();
            }
        }
        // P0-2: constant strength reduction, AFTER the algebraic fold so the
        // fold's same-op merge runs on Mul operands first (see header).
        return StrengthReduceStmts(result);
    }

    //--------------------------------------------------------------------------
    // Constant strength reduction (P0-2), modeled on coreclr's fgMorphSmpOp
    // mul/div lowering: x*2^k -> x<<k ; x/2^k (signed int32) ->
    // (x + ((x>>31)&(2^k-1))) >> k ; x/2^k (uint, DivUn) -> x>>k (ShrUn).
    //
    // Runs AFTER the algebraic fold so a merged product (x*2)*4 -> x*8 folds
    // to a single x<<3 (the fold's same-op merge must see Mul operands; a
    // strength pass inside Rewrite would pre-shift the inner operand and
    // break (x*2)*3 -> x*6).
    //
    // SAFE SUBSET (never wrong, never changes observable behavior):
    //  - only POSITIVE power-of-2 constants; divisor 1 stays a division
    //    (x/1 keeps the checked_div path; int.MinValue/-1 wrap excluded by
    //    the negative-divisor rule), 0 is not a power of 2 (DivideByZero
    //    preserved), int64/double operands (Kind "l"/"d", IsInt64 constants)
    //    are never touched (the int32 formula is wrong for 64-bit).
    //  - Div/DivUn duplicate the dividend in the corrected form, so they fold
    //    only when the dividend is pure arithmetic (IsPureArithmetic: no
    //    Div/Rem/FieldExpr) -- an impure dividend's exception could otherwise
    //    fire twice. Mul->Shl does not duplicate, so it has no purity gate.
    //
    // Equivalence (signed x/2^k -> corrected shift), for every int32 x:
    //   x >= 0: x>>31 = 0, result = x>>k = floor(x/2^k) = x/2^k (truncation).
    //   x <  0: x>>31 = -1, correction (2^k-1) added; floor((x+2^k-1)/2^k) =
    //           ceil(x/2^k) = x/2^k (truncation). The addition cannot wrap:
    //           it runs only for x<0, and x + (2^k-1) >= -2^31 + 2^k - 1.
    //   Shift counts mask to 5 bits like .NET (the emitter masks too).
    //--------------------------------------------------------------------------
    private static List<Stmt> StrengthReduceStmts(List<Stmt> stmts)
    {
        var result = new List<Stmt>(stmts.Count);
        foreach (var s in stmts)
        {
            result.Add(s switch
            {
                AssignStmt a => new AssignStmt
                {
                    Target = a.Target,
                    Value = StrengthReduce(a.Value),
                    SourceOffset = a.SourceOffset,
                    SourceOpName = a.SourceOpName,
                },
                ExprStmt e => new ExprStmt { Value = StrengthReduce(e.Value), SourceOffset = e.SourceOffset, SourceOpName = e.SourceOpName },
                RetStmt r => new RetStmt { Value = r.Value == null ? null : StrengthReduce(r.Value), SourceOffset = r.SourceOffset, SourceOpName = r.SourceOpName },
                BranchStmt b => new BranchStmt
                {
                    TargetLabel = b.TargetLabel,
                    Condition = b.Condition == null ? null : StrengthReduce(b.Condition),
                    SourceOffset = b.SourceOffset,
                    SourceOpName = b.SourceOpName,
                },
                CallStmt c => StrengthReduceCallStmt(c),
                FieldAssignStmt f => new FieldAssignStmt
                {
                    Object = StrengthReduce(f.Object),
                    FieldCppName = f.FieldCppName,
                    OwnerFullName = f.OwnerFullName,
                    IsRefField = f.IsRefField,
                    IsValueTypeObject = f.IsValueTypeObject,
                    RefTargetFullName = f.RefTargetFullName,
                    Value = StrengthReduce(f.Value),
                    SourceOffset = f.SourceOffset,
                    SourceOpName = f.SourceOpName,
                },
                _ => s, // LabelStmt
            });
        }
        return result;
    }

    private static CallStmt StrengthReduceCallStmt(CallStmt c)
    {
        var nc = new CallStmt { Function = c.Function, SourceOffset = c.SourceOffset, SourceOpName = c.SourceOpName, Resolved = c.Resolved };
        foreach (var a in c.Args) nc.Args.Add(StrengthReduce(a));
        return nc;
    }

    private static Expr StrengthReduce(Expr e)
    {
        switch (e)
        {
            case BinaryExpr b:
            {
                var l = StrengthReduce(b.Left);
                var r = StrengthReduce(b.Right);
                if (b.Kind is null && r is IntConstExpr rc && !rc.IsInt64
                    && l is not Int64ConstExpr and not DoubleConstExpr)
                {
                    // (x<<a)<<b -> x<<((a+b)&31) -- keeps strength-reduction
                    // chains (and IL shift chains) as single shifts.
                    if (b.Op is BinaryOp.Shl or BinaryOp.Shr or BinaryOp.ShrUn
                        && l is BinaryExpr sl && sl.Op == b.Op && sl.Kind is null
                        && sl.Right is IntConstExpr sc && !sc.IsInt64)
                    {
                        var merged = (sc.Value + rc.Value) & 31;
                        if (merged == 0)
                            return sl.Left; // x<<0 -> x
                        return new BinaryExpr { Op = b.Op, Left = sl.Left, Right = new IntConstExpr { Value = merged } };
                    }
                    if (b.Op == BinaryOp.Mul && rc.Value > 1 && IsPowerOfTwo(rc.Value))
                        return MakeShift(BinaryOp.Shl, l, Log2Exact(rc.Value));
                    if (b.Op == BinaryOp.Div && rc.Value >= 2 && IsPowerOfTwo(rc.Value) && IsPureArithmetic(l))
                    {
                        // x/2^k -> (x + ((x>>31)&(2^k-1))) >> k (see header).
                        var k = Log2Exact(rc.Value);
                        return new BinaryExpr
                        {
                            Op = BinaryOp.Shr,
                            Left = new BinaryExpr
                            {
                                Op = BinaryOp.Add,
                                Left = l,
                                Right = new BinaryExpr
                                {
                                    Op = BinaryOp.And,
                                    Left = new BinaryExpr { Op = BinaryOp.Shr, Left = l, Right = new IntConstExpr { Value = 31 } },
                                    Right = new IntConstExpr { Value = rc.Value - 1 },
                                },
                            },
                            Right = new IntConstExpr { Value = k },
                        };
                    }
                    if (b.Op == BinaryOp.DivUn && rc.Value >= 2 && IsPowerOfTwo(rc.Value) && IsPureArithmetic(l))
                        return MakeShift(BinaryOp.ShrUn, l, Log2Exact(rc.Value));
                }
                return new BinaryExpr { Op = b.Op, Left = l, Right = r, Kind = b.Kind };
            }
            case UnaryExpr u:
                return new UnaryExpr { Op = u.Op, Operand = StrengthReduce(u.Operand) };
            case CallExpr c:
            {
                var nc = new CallExpr { Function = c.Function, ReturnsRef = c.ReturnsRef, IsPInvoke = c.IsPInvoke, SetLastError = c.SetLastError, ResultKind = c.ResultKind, Resolved = c.Resolved };
                foreach (var a in c.Args) nc.Args.Add(StrengthReduce(a));
                foreach (var i in c.StringArgIndices) nc.StringArgIndices.Add(i);
                return nc;
            }
            case VirtualCallExpr v:
            {
                var nv = new VirtualCallExpr { MethodToken = v.MethodToken, ReturnsRef = v.ReturnsRef, ReceiverTypeName = v.ReceiverTypeName };
                foreach (var a in v.Args) nv.Args.Add(StrengthReduce(a));
                return nv;
            }
            case DelegateInvokeExpr d:
            {
                var nd = new DelegateInvokeExpr { MethodToken = d.MethodToken, ReturnsRef = d.ReturnsRef };
                foreach (var a in d.Args) nd.Args.Add(StrengthReduce(a));
                return nd;
            }
            case MarshalUtf8Expr m:
                return new MarshalUtf8Expr { Arg = StrengthReduce(m.Arg), Utf16 = m.Utf16 };
            case FieldExpr f:
                return new FieldExpr
                {
                    Object = StrengthReduce(f.Object),
                    FieldCppName = f.FieldCppName,
                    OwnerFullName = f.OwnerFullName,
                    IsRefField = f.IsRefField,
                    IsValueTypeObject = f.IsValueTypeObject,
                };
            case FieldAddrExpr fa:
                return new FieldAddrExpr
                {
                    Object = StrengthReduce(fa.Object),
                    FieldCppName = fa.FieldCppName,
                    OwnerFullName = fa.OwnerFullName,
                    IsValueTypeObject = fa.IsValueTypeObject,
                };
            default:
                return e;
        }
    }

    // Builds op(x, count), merging through an existing same-op shift chain:
    // MakeShift(Shl, (x<<1), 2) -> x<<3.
    private static Expr MakeShift(BinaryOp op, Expr left, long count)
    {
        if (left is BinaryExpr sb && sb.Op == op && sb.Kind is null
            && sb.Right is IntConstExpr sc && !sc.IsInt64
            && sb.Left is not Int64ConstExpr and not DoubleConstExpr)
            return MakeShift(op, sb.Left, (sc.Value + count) & 31);
        return new BinaryExpr { Op = op, Left = left, Right = new IntConstExpr { Value = count & 31 } };
    }

    private static bool IsPowerOfTwo(long v) => v > 0 && (v & (v - 1)) == 0;

    private static int Log2Exact(long v)
    {
        int k = 0;
        while (v > 1) { v >>= 1; k++; }
        return k;
    }

    private static CallStmt RewriteCallStmt(CallStmt c, Dictionary<string, bool> knownNonNull)
    {
        // M5 wiring: preserve the fold-time resolution (receiver-type-aware;
        // the emitter cannot reproduce it from the token alone).
        var nc = new CallStmt { Function = c.Function, SourceOffset = c.SourceOffset, SourceOpName = c.SourceOpName, Resolved = c.Resolved };
        foreach (var a in c.Args) nc.Args.Add(Rewrite(a, knownNonNull));
        return nc;
    }

    private static Expr Rewrite(Expr e, Dictionary<string, bool> knownNonNull)
    {
        switch (e)
        {
            case BinaryExpr b:
            {
                var l = Rewrite(b.Left, knownNonNull);
                var r = Rewrite(b.Right, knownNonNull);
                var op = b.Op;

                // 1) constant-right canonicalization (commutative ops only --
                //    both-constant nodes are left alone so the const fold wins).
                if (IsCommutative(op) && l is IntConstExpr && r is not IntConstExpr)
                    (l, r) = (r, l);

                // 2) both-constant fold (int32 wrap semantics, single impl).
                // M5 wiring: carry the int64/double operand kind (the emitter
                // types temps from it).
                var bb = new BinaryExpr { Op = op, Left = l, Right = r, Kind = b.Kind };
                if (CodeGenerator.TryFoldInt32(bb, out var cv))
                    return new IntConstExpr { Value = cv };

                // 3) neutral-element / absorbing identities.
                if (r is IntConstExpr ri)
                {
                    switch (op)
                    {
                        case BinaryOp.Add when ri.Value == 0: return l;                      // x+0
                        case BinaryOp.Sub when ri.Value == 0: return l;                      // x-0
                        case BinaryOp.Mul when ri.Value == 1: return l;                      // x*1
                        case BinaryOp.Mul when ri.Value == 0 && IsPureArithmetic(l):
                            return new IntConstExpr { Value = 0 };                           // x*0 (pure)
                        case BinaryOp.And when ri.Value == 0 && IsPureArithmetic(l):
                            return new IntConstExpr { Value = 0 };                           // x&0 (pure)
                        case BinaryOp.And when ri.Value == -1: return l;                     // x&(-1)
                        case BinaryOp.Or when ri.Value == 0: return l;                       // x|0
                        case BinaryOp.Xor when ri.Value == 0: return l;                      // x^0
                        case BinaryOp.Shl or BinaryOp.Shr when ri.Value == 0: return l;      // x<<0, x>>0
                    }
                }
                // left-constant rule (Sub is not commutative -- no flip): 0-x -> -x.
                if (op == BinaryOp.Sub && l is IntConstExpr li && li.Value == 0)
                    return SimplifyUnary(UnaryOp.Neg, r);

                // 4) self-identities -- only for pure subtrees (removing both
                //    evaluations must not remove a potential exception).
                if (ExprEquals(l, r) && IsPureArithmetic(l))
                {
                    if (op == BinaryOp.Sub)
                        return new IntConstExpr { Value = 0 };                               // x-x
                    if (op is BinaryOp.Ceq or BinaryOp.Cgt or BinaryOp.Clt or BinaryOp.CgtUn or BinaryOp.CltUn)
                        return new IntConstExpr { Value = op == BinaryOp.Ceq ? 1 : 0 };      // x==x->1, x>x->0 ...
                }

                // 5) null comparisons: null==null -> 1; a known-non-null object
                //    compared with null -> the constant result.
                var lNull = l is NullExpr;
                var rNull = r is NullExpr;
                if (lNull || rNull)
                {
                    if (lNull && rNull)
                        return new IntConstExpr { Value = op == BinaryOp.Ceq ? 1 : 0 };
                    var other = lNull ? r : l;
                    if (IsNonNullAlloc(other, knownNonNull))
                        return new IntConstExpr { Value = op is BinaryOp.Cgt or BinaryOp.CgtUn ? 1 : 0 };
                }

                // 6) same-op constant merging: (x+c1) op c2 -> x op (c1 op c2).
                if (IsCommutative(op) && r is IntConstExpr rc
                    && l is BinaryExpr lb && lb.Op == op && lb.Right is IntConstExpr lc)
                {
                    if (CodeGenerator.TryFoldInt32(
                        new BinaryExpr { Op = op, Left = new IntConstExpr { Value = lc.Value }, Right = new IntConstExpr { Value = rc.Value } },
                        out var mv))
                        return new BinaryExpr { Op = op, Left = lb.Left, Right = new IntConstExpr { Value = mv } };
                }
                return bb;
            }
            case UnaryExpr u:
                return SimplifyUnary(u.Op, Rewrite(u.Operand, knownNonNull));
            case CallExpr c:
            {
                // M5 wiring: preserve the fold-time resolution (receiver-type-
                // aware icall answers) and the value result kind.
                var nc = new CallExpr { Function = c.Function, ReturnsRef = c.ReturnsRef, IsPInvoke = c.IsPInvoke, SetLastError = c.SetLastError, ResultKind = c.ResultKind, Resolved = c.Resolved };
                foreach (var a in c.Args) nc.Args.Add(Rewrite(a, knownNonNull));
                foreach (var i in c.StringArgIndices) nc.StringArgIndices.Add(i);
                return nc;
            }
            case VirtualCallExpr v:
            {
                var nv = new VirtualCallExpr { MethodToken = v.MethodToken, ReturnsRef = v.ReturnsRef, ReceiverTypeName = v.ReceiverTypeName };
                foreach (var a in v.Args) nv.Args.Add(Rewrite(a, knownNonNull));
                return nv;
            }
            case DelegateInvokeExpr d:
            {
                var nd = new DelegateInvokeExpr { MethodToken = d.MethodToken, ReturnsRef = d.ReturnsRef };
                foreach (var a in d.Args) nd.Args.Add(Rewrite(a, knownNonNull));
                return nd;
            }
            case MarshalUtf8Expr m:
                return new MarshalUtf8Expr { Arg = Rewrite(m.Arg, knownNonNull), Utf16 = m.Utf16 };
            case FieldExpr f:
                return new FieldExpr
                {
                    Object = Rewrite(f.Object, knownNonNull),
                    FieldCppName = f.FieldCppName,
                    OwnerFullName = f.OwnerFullName,
                    IsRefField = f.IsRefField,
                    IsValueTypeObject = f.IsValueTypeObject,
                };
            case FieldAddrExpr fa:
                return new FieldAddrExpr
                {
                    Object = Rewrite(fa.Object, knownNonNull),
                    FieldCppName = fa.FieldCppName,
                    OwnerFullName = fa.OwnerFullName,
                    IsValueTypeObject = fa.IsValueTypeObject,
                };
            default:
                return e; // IntConstExpr / StringConstExpr / NullExpr / LocExpr / LocAddrExpr / StaticFieldExpr / ...
        }
    }

    private static Expr SimplifyUnary(UnaryOp op, Expr operand)
    {
        var u = new UnaryExpr { Op = op, Operand = operand };
        if (CodeGenerator.TryFoldInt32(u, out var v))
            return new IntConstExpr { Value = v };
        // -(-x) -> x ; ~(~x) -> x (holds modulo 2^32 for every int32 x).
        if (operand is UnaryExpr inner && inner.Op == op)
            return inner.Operand;
        return u;
    }

    private static bool IsCommutative(BinaryOp op) =>
        op is BinaryOp.Add or BinaryOp.Mul or BinaryOp.And or BinaryOp.Or or BinaryOp.Xor;

    // Pure arithmetic: no evaluation can throw (no Div/Rem, no field reads).
    private static bool IsPureArithmetic(Expr e) => e switch
    {
        IntConstExpr or LocExpr or NullExpr => true,
        UnaryExpr u => IsPureArithmetic(u.Operand),
        BinaryExpr b => b.Op is not BinaryOp.Div and not BinaryOp.Rem
            && IsPureArithmetic(b.Left) && IsPureArithmetic(b.Right),
        _ => false,
    };

    // Structural equality (unknown expr kinds fall back to reference equality
    // -- dup produces shared instances, so same-instance means same value).
    private static bool ExprEquals(Expr a, Expr b) => a switch
    {
        IntConstExpr ia => b is IntConstExpr ib && ia.Value == ib.Value,
        LocExpr la => b is LocExpr lb && la.Name == lb.Name,
        NullExpr => b is NullExpr,
        StringConstExpr sa => b is StringConstExpr sb && sa.Value == sb.Value,
        BinaryExpr ba => b is BinaryExpr bb && ba.Op == bb.Op
            && ExprEquals(ba.Left, bb.Left) && ExprEquals(ba.Right, bb.Right),
        UnaryExpr ua => b is UnaryExpr ub && ua.Op == ub.Op && ExprEquals(ua.Operand, ub.Operand),
        _ => ReferenceEquals(a, b),
    };

    // Allocations in this runtime never return null: newobj (object_new),
    // ldstr (string_new), newarr (array_new), box (box_int32).
    private static bool IsNonNullAlloc(Expr e, Dictionary<string, bool> knownNonNull) => e switch
    {
        NewObjExpr => true,
        StringConstExpr => true,
        CallExpr c => c.Function.StartsWith("newarr_", StringComparison.Ordinal)
            || c.Function.StartsWith("box_", StringComparison.Ordinal),
        LocExpr l => knownNonNull.TryGetValue(l.Name, out var v) && v,
        _ => false,
    };
}
