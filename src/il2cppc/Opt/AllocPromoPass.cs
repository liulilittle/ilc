using Il2Cpp.Transpiler.Ir;

namespace Il2Cpp.Transpiler.Opt;

//------------------------------------------------------------------------------
// AllocPromoPass -- allocation promotion / scalar replacement of aggregates
// for PURE-LOCAL objects (the object-bench gap: `Item it = new Item(i);
// sum += it.Get()` allocates a GC object every iteration while the net48 JIT
// proves the object never escapes, inlines the ctor/getter and eliminates
// the allocation entirely -- ~6 ns/iteration vs the ILC side's allocation +
// two NOINLINE calls).
//
// RUNTIME CONSTRAINT (feasibility, reported to the user): the runtime's
// il2cpp_object_new (object.cpp) delegates straight to il2cpp_gc_alloc --
// there is NO stack-allocation form (a stack object would be untracked by
// the mark-sweep collector and its address would dangle once the collector
// runs). So "stack allocation" is NOT available; the equivalent win is
// IR-level scalar replacement: a non-escaping object whose fields are all
// VALUE fields is replaced by one int32 temp per field (the field model is
// Int32-or-Reference only), the allocation statement disappears, and the C++
// optimizer sees a pure scalar loop.
//
//   Promotion rule (conservative, per object):
//     * the object is created exactly ONCE in the method:
//         N = new T()   (AssignStmt, Value = NewObjExpr)
//       and N is never reassigned; every alias of N is a pure reference
//       copy (`M = N`, `M = N` again, chains) -- multi-assignment kills it.
//     * NO use of N (or any alias) escapes the method or an allocation:
//         - not address-taken (LocAddrExpr)
//         - not an argument of ANY call except the known no-op ctor_nop
//           (il2cpp_object_ctor_nop -- an empty runtime function, see
//           runtime.cpp `(void)this_;`); the no-op call is dropped with the
//           object
//         - not stored into another object's field, not returned, not used
//           in a branch condition or comparison
//         - every FIELD access must be on a plain `alias->F` shape, the
//           field must be a VALUE field (IsRefField == false -- a reference
//           field would need its own GC root, which the scalar model does
//           not provide) and the receiver must not be a value-type object
//     * field WRITE values must not reference the object itself (no
//       self-cycle `N.v = N`).
//
//   Transformation (per promoted object with field set {F1..Fk}):
//     * the allocation statement becomes `N_prm_F1 = 0; ...; N_prm_Fk = 0;`
//       (zero-init preserves .NET default-field semantics for every read
//       path; the int32 kind matches the Int32-only value-field model)
//     * every `alias->F = V` becomes `N_prm_F = V`
//     * every read of `alias->F` becomes `N_prm_F`
//     * alias copies and the ctor_nop call disappear.
//
//   ORDERING (wiring contract, reported to the user): run AFTER InlinePass
//   (the ctor/getter calls that would otherwise observe/escape the object
//   must already be expanded into direct field accesses) and BEFORE
//   DcePass.Run (the pass introduces scalar stores the DCE can clean).
//   The transformed temps are plain LocExpr names -- the emitter's existing
//   declaration scan and ExprKind inference type them (int32 value fields ->
//   int32_t), and RootSlotPass naturally drops the object's now-unused ref
//   slots (the frame shrinks to the promoted scalars).
//------------------------------------------------------------------------------
public static class AllocPromoPass
{
    // The empty base-ctor runtime call (newobj of a user type with no
    // explicit ctor folds to `il2cpp_object_ctor_nop`). Dropped together
    // with its promoted receiver -- it is a pure no-op (runtime.cpp).
    public const string CtorNopCppName = "il2cpp::runtime::il2cpp_object_ctor_nop";
    public const int MaxPromotedObjectsPerMethod = 64;

    public static (List<Stmt> Stmts, int Promoted) Run(List<Stmt> stmts)
    {
        var allocTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in stmts)
            if (s is AssignStmt a && a.Value is NewObjExpr && !a.Target.Contains('.'))
                allocTargets.Add(a.Target);

        var promoted = 0;
        var current = stmts;
        foreach (var root in allocTargets)
        {
            if (!TryPromote(current, root, allocTargets, out var rewritten))
                continue;
            current = rewritten;
            promoted++;
            if (promoted >= MaxPromotedObjectsPerMethod) break;
        }
        return (promoted == 0 ? stmts : current, promoted);
    }

    //==========================================================================
    // Per-object analysis + rewrite.
    //==========================================================================
    private static bool TryPromote(List<Stmt> stmts, string root,
        HashSet<string> allocTargets, out List<Stmt> rewritten)
    {
        rewritten = stmts;
        var alias = new HashSet<string>(StringComparer.Ordinal) { root };
        var fields = new HashSet<string>(StringComparer.Ordinal);
        var allocPos = -1;
        var valid = true;

        for (var i = 0; i < stmts.Count && valid; i++)
        {
            var s = stmts[i];
            // Use-before-alloc guard: a use of the object (or any alias) at a
            // statement BEFORE its allocation is flow-unsafe for the scalar
            // model -- the allocation is replaced by zero-init stores AT the
            // alloc position, so an earlier field WRITE would be silently
            // clobbered and an earlier field READ would read an uninitialized
            // scalar (the .NET semantics for both is an NRE on the null
            // object). The allocation statement itself is exempt.
            if (allocPos == -1 && !(s is AssignStmt { } rootAlloc
                    && rootAlloc.Target == root && rootAlloc.Value is NewObjExpr)
                && StmtReferencesAlias(s, alias))
            {
                valid = false;
                continue;
            }
            switch (s)
            {
                case AssignStmt a:
                    if (a.Target == root)
                    {
                        // Allocation site: allowed once, and only with a NewObjExpr.
                        if (allocPos != -1 || a.Value is not NewObjExpr) valid = false;
                        else allocPos = i;
                        continue;
                    }
                    if (alias.Contains(a.Target))
                    {
                        // Alias reassigned (a copy target written twice) -- the value
                        // changes, promotion cannot model it.
                        valid = false;
                        continue;
                    }
                    if (a.Value is LocExpr l && alias.Contains(l.Name))
                    {
                        // Pure reference copy: M = alias. A static-field target
                        // ("Owner.Field", stsfld) is NOT a local copy -- it stores
                        // the object into shared static storage where the scalar
                        // model has no slot (and dropping it would lose the store).
                        // Reject it as an escape, never alias it.
                        if (allocTargets.Contains(a.Target)) valid = false; // aliased to another allocation
                        else if (a.Target.Contains('.')) valid = false;      // stsfld of the object: static escape
                        else alias.Add(a.Target);
                        continue;
                    }
                    if (!ValidateExpr(a.Value, alias, fields)) valid = false;
                    continue;
                case CallStmt c:
                    if (IsCtorNopOnAlias(c, alias)) continue; // no-op on the object: dropped
                    if (c.Args.Any(x => ContainsAlias(x, alias))) valid = false;
                    else
                        foreach (var x in c.Args)
                            if (!ValidateExpr(x, alias, fields)) { valid = false; break; }
                    continue;
                case FieldAssignStmt f:
                    if (IsAliasObject(f.Object, alias, out _))
                    {
                        if (f.IsRefField || f.IsValueTypeObject) valid = false; // ref field needs a root / value-type receiver
                        else
                        {
                            fields.Add(f.FieldCppName);
                            // A field WRITE whose value references the object (self-cycle)
                            // or any alias escapes the scalar model.
                            if (ContainsAlias(f.Value, alias)) valid = false;
                            else if (!ValidateExpr(f.Value, alias, fields)) valid = false;
                        }
                        continue;
                    }
                    if (ContainsAlias(f.Object, alias) || ContainsAlias(f.Value, alias)) valid = false;
                    else if (!ValidateExpr(f.Object, alias, fields) || !ValidateExpr(f.Value, alias, fields)) valid = false;
                    continue;
                case RetStmt r when r.Value != null:
                    if (!ValidateExpr(r.Value, alias, fields)) valid = false;
                    continue;
                case BranchStmt b when b.Condition != null:
                    if (!ValidateExpr(b.Condition, alias, fields)) valid = false;
                    continue;
                case ExprStmt es:
                    if (!ValidateExpr(es.Value, alias, fields)) valid = false;
                    continue;
            }
        }
        if (!valid || allocPos == -1) return false;

        // Rebuild: replace the allocation with zero-init stores, drop the alias
        // copies and the ctor_nop call, and rewrite every field access.
        var result = new List<Stmt>(stmts.Count);
        var touched = false;
        for (var i = 0; i < stmts.Count; i++)
        {
            var s = stmts[i];
            switch (s)
            {
                case AssignStmt a when a.Target == root && a.Value is NewObjExpr:
                    touched = true; // the allocation disappears (zero fields -> replaced by nothing)
                    foreach (var f in fields.OrderBy(f => f, StringComparer.Ordinal))
                        result.Add(new AssignStmt
                        {
                            Target = PromotedName(root, f),
                            Value = new IntConstExpr { Value = 0 },
                        });
                    continue;
                case AssignStmt a when a.Value is LocExpr l && alias.Contains(l.Name) && alias.Contains(a.Target):
                    touched = true;
                    continue; // alias copy: dropped
                case CallStmt c when IsCtorNopOnAlias(c, alias):
                    touched = true;
                    continue; // no-op ctor call on the promoted object: dropped
                case FieldAssignStmt f when IsAliasObject(f.Object, alias, out _):
                    result.Add(new AssignStmt
                    {
                        Target = PromotedName(root, f.FieldCppName),
                        Value = RewriteExpr(f.Value, alias, root),
                        SourceOffset = f.SourceOffset,
                        SourceOpName = f.SourceOpName,
                    });
                    touched = true;
                    continue;
                default:
                    result.Add(RewriteStmt(s, alias, root));
                    continue;
            }
        }
        if (!touched) return false;
        rewritten = result;
        return true;
    }

    private static string PromotedName(string root, string field) => $"{root}_prm_{field}";

    //--------------------------------------------------------------------------
    // Expression validation: rejects every use of an alias that the scalar
    // model cannot represent. FieldExpr.FieldCppName of alias receivers is
    // collected into `fields`.
    //--------------------------------------------------------------------------
    private static bool ValidateExpr(Expr e, HashSet<string> alias, HashSet<string> fields)
    {
        switch (e)
        {
            case LocExpr l:
                return !alias.Contains(l.Name); // a bare alias reference outside the allowed shapes
            case LocAddrExpr l:
                return !alias.Contains(l.Name); // address of the object: aliasing undecidable
            case FieldExpr f:
                if (IsAliasObject(f.Object, alias, out _))
                {
                    if (f.IsRefField || f.IsValueTypeObject) return false;
                    fields.Add(f.FieldCppName);
                    return true; // field READ on the promoted object
                }
                if (ContainsAlias(f.Object, alias)) return false;
                return ValidateExpr(f.Object, alias, fields);
            case FieldAddrExpr f:
                // &obj->F: the field address can escape the object; rejected.
                if (ContainsAlias(f.Object, alias)) return false;
                return ValidateExpr(f.Object, alias, fields);
            case CallExpr c:
                foreach (var a in c.Args)
                    if (ContainsAlias(a, alias) || !ValidateExpr(a, alias, fields)) return false;
                return true;
            case VirtualCallExpr v:
                foreach (var a in v.Args)
                    if (ContainsAlias(a, alias) || !ValidateExpr(a, alias, fields)) return false;
                return true;
            case DelegateInvokeExpr d:
                foreach (var a in d.Args)
                    if (ContainsAlias(a, alias) || !ValidateExpr(a, alias, fields)) return false;
                return true;
            case DelegateNewExpr dn:
                if (ContainsAlias(dn.Target, alias)) return false;
                return ValidateExpr(dn.Target, alias, fields);
            case MarshalUtf8Expr m:
                if (ContainsAlias(m.Arg, alias)) return false;
                return ValidateExpr(m.Arg, alias, fields);
            case BinaryExpr b:
                return ValidateExpr(b.Left, alias, fields) && ValidateExpr(b.Right, alias, fields);
            case UnaryExpr u:
                return ValidateExpr(u.Operand, alias, fields);
            default:
                return true; // constants / new-object / null / static-field atoms
        }
    }

    private static bool IsAliasObject(Expr e, HashSet<string> alias, out string name)
    {
        if (e is LocExpr l && alias.Contains(l.Name))
        {
            name = l.Name;
            return true;
        }
        name = "";
        return false;
    }

    private static bool ContainsAlias(Expr e, HashSet<string> alias) => e switch
    {
        LocExpr l => alias.Contains(l.Name),
        LocAddrExpr l => alias.Contains(l.Name),
        BinaryExpr b => ContainsAlias(b.Left, alias) || ContainsAlias(b.Right, alias),
        UnaryExpr u => ContainsAlias(u.Operand, alias),
        CallExpr c => c.Args.Any(a => ContainsAlias(a, alias)),
        VirtualCallExpr v => v.Args.Any(a => ContainsAlias(a, alias)),
        DelegateInvokeExpr d => d.Args.Any(a => ContainsAlias(a, alias)),
        DelegateNewExpr dn => ContainsAlias(dn.Target, alias),
        MarshalUtf8Expr m => ContainsAlias(m.Arg, alias),
        FieldExpr f => ContainsAlias(f.Object, alias),
        FieldAddrExpr f => ContainsAlias(f.Object, alias),
        _ => false,
    };

    // Statement-level alias reference test (the use-before-alloc guard).
    private static bool StmtReferencesAlias(Stmt s, HashSet<string> alias) => s switch
    {
        AssignStmt a => alias.Contains(a.Target) || ContainsAlias(a.Value, alias),
        ExprStmt es => ContainsAlias(es.Value, alias),
        RetStmt r => r.Value != null && ContainsAlias(r.Value, alias),
        BranchStmt b => b.Condition != null && ContainsAlias(b.Condition, alias),
        CallStmt c => c.Args.Any(x => ContainsAlias(x, alias)),
        FieldAssignStmt f => ContainsAlias(f.Object, alias) || ContainsAlias(f.Value, alias),
        _ => false,
    };

    private static bool IsCtorNopOnAlias(CallStmt c, HashSet<string> alias)
    {
        if (c.Function != CtorNopCppName && c.Resolved?.CppName != CtorNopCppName) return false;
        if (c.Args.Count != 1) return false;
        return c.Args[0] is LocExpr l && alias.Contains(l.Name);
    }

    //--------------------------------------------------------------------------
    // Rewrite: field reads on the promoted object -> scalar temp reads.
    //--------------------------------------------------------------------------
    private static Stmt RewriteStmt(Stmt s, HashSet<string> alias, string root) => s switch
    {
        AssignStmt a => new AssignStmt
        {
            Target = a.Target,
            Value = RewriteExpr(a.Value, alias, root),
            SourceOffset = a.SourceOffset,
            SourceOpName = a.SourceOpName,
        },
        ExprStmt es => new ExprStmt
        {
            Value = RewriteExpr(es.Value, alias, root),
            SourceOffset = es.SourceOffset,
            SourceOpName = es.SourceOpName,
        },
        RetStmt r => new RetStmt
        {
            Value = r.Value == null ? null : RewriteExpr(r.Value, alias, root),
            SourceOffset = r.SourceOffset,
            SourceOpName = r.SourceOpName,
        },
        BranchStmt b => new BranchStmt
        {
            TargetLabel = b.TargetLabel,
            Condition = b.Condition == null ? null : RewriteExpr(b.Condition, alias, root),
            SourceOffset = b.SourceOffset,
            SourceOpName = b.SourceOpName,
        },
        CallStmt c => RewriteCall(c, alias, root),
        FieldAssignStmt f => new FieldAssignStmt
        {
            Object = RewriteExpr(f.Object, alias, root),
            FieldCppName = f.FieldCppName,
            OwnerFullName = f.OwnerFullName,
            IsRefField = f.IsRefField,
            IsValueTypeObject = f.IsValueTypeObject,
            RefTargetFullName = f.RefTargetFullName,
            Value = RewriteExpr(f.Value, alias, root),
            SourceOffset = f.SourceOffset,
            SourceOpName = f.SourceOpName,
        },
        _ => s,
    };

    private static CallStmt RewriteCall(CallStmt c, HashSet<string> alias, string root)
    {
        var nc = new CallStmt { Function = c.Function, Resolved = c.Resolved, SourceOffset = c.SourceOffset, SourceOpName = c.SourceOpName };
        foreach (var a in c.Args) nc.Args.Add(RewriteExpr(a, alias, root));
        return nc;
    }

    private static Expr RewriteExpr(Expr e, HashSet<string> alias, string root)
    {
        switch (e)
        {
            case FieldExpr f when IsAliasObject(f.Object, alias, out _):
                return new LocExpr { Name = PromotedName(root, f.FieldCppName) };
            case BinaryExpr b:
                return new BinaryExpr
                {
                    Op = b.Op,
                    Left = RewriteExpr(b.Left, alias, root),
                    Right = RewriteExpr(b.Right, alias, root),
                    Kind = b.Kind,
                };
            case UnaryExpr u:
                return new UnaryExpr { Op = u.Op, Operand = RewriteExpr(u.Operand, alias, root) };
            case CallExpr c:
                var nc = new CallExpr
                {
                    Function = c.Function,
                    ReturnsRef = c.ReturnsRef,
                    ResultKind = c.ResultKind,
                    Resolved = c.Resolved,
                    IsPInvoke = c.IsPInvoke,
                    SetLastError = c.SetLastError,
                };
                foreach (var a in c.Args) nc.Args.Add(RewriteExpr(a, alias, root));
                foreach (var i in c.StringArgIndices) nc.StringArgIndices.Add(i);
                return nc;
            case VirtualCallExpr v:
                var nv = new VirtualCallExpr { MethodToken = v.MethodToken, ReturnsRef = v.ReturnsRef, ReceiverTypeName = v.ReceiverTypeName };
                foreach (var a in v.Args) nv.Args.Add(RewriteExpr(a, alias, root));
                return nv;
            case DelegateInvokeExpr d:
                var nd = new DelegateInvokeExpr { MethodToken = d.MethodToken, ReturnsRef = d.ReturnsRef };
                foreach (var a in d.Args) nd.Args.Add(RewriteExpr(a, alias, root));
                return nd;
            case DelegateNewExpr dn:
                return new DelegateNewExpr { MethodToken = dn.MethodToken, Target = RewriteExpr(dn.Target, alias, root), TypeId = dn.TypeId };
            case MarshalUtf8Expr m:
                return new MarshalUtf8Expr { Arg = RewriteExpr(m.Arg, alias, root), Utf16 = m.Utf16 };
            case FieldExpr f:
                return new FieldExpr
                {
                    Object = RewriteExpr(f.Object, alias, root),
                    FieldCppName = f.FieldCppName,
                    OwnerFullName = f.OwnerFullName,
                    IsRefField = f.IsRefField,
                    IsValueTypeObject = f.IsValueTypeObject,
                };
            case FieldAddrExpr f:
                return new FieldAddrExpr
                {
                    Object = RewriteExpr(f.Object, alias, root),
                    FieldCppName = f.FieldCppName,
                    OwnerFullName = f.OwnerFullName,
                    IsValueTypeObject = f.IsValueTypeObject,
                };
            default:
                return e;
        }
    }
}
