namespace Il2Cpp.Transpiler.Ir;

using Il2Cpp.Transpiler.Emit;

public abstract class Stmt
{
    // IL source (recorded at StackFold fold time, for the emitter to generate
    // English IL provenance comments). Settable (not init-only): the inliner
    // re-stamps inlined statements with the call site's offset so the EH
    // walk's try-region membership survives optimization (InlinePass.
    // StampSource, exdeep "expected catch").
    public int SourceOffset { get; set; } = -1;
    public string SourceOpName { get; set; } = "";
}

public sealed class LabelStmt : Stmt { public required string Name { get; init; } }
public sealed class AssignStmt : Stmt { public required string Target { get; init; } public required Expr Value { get; init; } }
public sealed class ExprStmt : Stmt { public required Expr Value { get; init; } }
public sealed class BranchStmt : Stmt
{
    public required string TargetLabel { get; init; }
    public Expr? Condition { get; init; } // null = unconditional
}
public sealed class RetStmt : Stmt { public Expr? Value { get; init; } }
// M5 BCL wiring: Resolved = the fold-time resolution (receiver-type-aware
// resolution cannot be reproduced at emit time -- the emitter must use the
// fold's answer verbatim, including the CppName and the arg-cast codes).
public sealed class CallStmt : Stmt
{
    public required string Function { get; init; }
    public List<Expr> Args { get; } = new();
    public ResolvedMethod? Resolved { get; set; }
}

//------------------------------------------------------------------------------
// FieldAssignStmt -- WRITE of an INSTANCE field (stfld): Object->FieldCppName = Value.
//   Same shape as FieldExpr (see Expr.cs) with an additional Value expression.
//   IsRefField: true when the target field holds a GC reference -- the emitted
//   value must be cast to the concrete struct pointer type (the IR carries
//   references as Il2CppObject*).
//   Emitted as `(<obj>)-><FieldCppName> = <value>;` with a receiver null-check.
//------------------------------------------------------------------------------
public sealed class FieldAssignStmt : Stmt
{
    public required Expr Object { get; init; }
    public required string FieldCppName { get; init; }
    public required string OwnerFullName { get; init; } // e.g. "Calc.Point" -> struct Calc_Point
    public bool IsRefField { get; init; }
    public bool IsValueTypeObject { get; init; } // true: receiver is a value-type value (no null check)
    public required string RefTargetFullName { get; init; } // full type name of the ref field's type
    public required Expr Value { get; init; }
}

//------------------------------------------------------------------------------
// IrNode -- Stmt half of the total clone/serialize dispatch (see Expr.cs).
//------------------------------------------------------------------------------
public static partial class IrNode
{
    //--------------------------------------------------------------------------
    // Serialization.
    //--------------------------------------------------------------------------
    public static string SerializeStmt(Stmt s) => s switch
    {
        LabelStmt l => $"Label({l.Name})",
        AssignStmt a => $"{a.Target} = {SerializeExpr(a.Value)}",
        ExprStmt es => $"{SerializeExpr(es.Value)};",
        BranchStmt b => b.Condition == null
            ? $"goto {b.TargetLabel};"
            : $"if ({SerializeExpr(b.Condition)}) goto {b.TargetLabel};",
        RetStmt r => r.Value == null ? "return;" : $"return {SerializeExpr(r.Value)};",
        CallStmt c => SerializeCall("CallStmt", c.Function, false, null, c.Resolved,
            System.Array.Empty<int>(), false, c.Args),
        FieldAssignStmt f => $"FieldAssign({SerializeExpr(f.Object)}, {f.OwnerFullName}.{f.FieldCppName}, {SerializeExpr(f.Value)}){(f.IsRefField ? "@ref" : "")}{(f.IsValueTypeObject ? "{vt}" : "")}",
        _ => throw new NotSupportedException($"IR statement {s.GetType().Name} has no SerializeStmt arm (IR-node completeness audit)"),
    };

    public static string SerializeStmts(IReadOnlyList<Stmt> stmts)
    {
        var lines = new List<string>(stmts.Count);
        foreach (var s in stmts) lines.Add(SerializeStmt(s));
        return string.Join(System.Environment.NewLine, lines);
    }

    //--------------------------------------------------------------------------
    // Deep clone.
    //--------------------------------------------------------------------------
    public static Stmt CloneStmt(Stmt s) => s switch
    {
        LabelStmt l => new LabelStmt { Name = l.Name },
        AssignStmt a => new AssignStmt { Target = a.Target, Value = CloneExpr(a.Value) },
        ExprStmt es => new ExprStmt { Value = CloneExpr(es.Value) },
        BranchStmt b => new BranchStmt
        {
            TargetLabel = b.TargetLabel,
            Condition = b.Condition == null ? null : CloneExpr(b.Condition),
        },
        RetStmt r => new RetStmt { Value = r.Value == null ? null : CloneExpr(r.Value) },
        CallStmt c => CloneCallStmt(c),
        FieldAssignStmt f => new FieldAssignStmt
        {
            Object = CloneExpr(f.Object),
            FieldCppName = f.FieldCppName,
            OwnerFullName = f.OwnerFullName,
            IsRefField = f.IsRefField,
            IsValueTypeObject = f.IsValueTypeObject,
            RefTargetFullName = f.RefTargetFullName,
            Value = CloneExpr(f.Value),
        },
        _ => throw new NotSupportedException($"IR statement {s.GetType().Name} has no CloneStmt arm (IR-node completeness audit)"),
    };

    public static List<Stmt> CloneStmts(IReadOnlyList<Stmt> stmts)
    {
        var result = new List<Stmt>(stmts.Count);
        foreach (var s in stmts) result.Add(CloneStmt(s));
        return result;
    }

    private static CallStmt CloneCallStmt(CallStmt c)
    {
        var nc = new CallStmt { Function = c.Function, Resolved = c.Resolved };
        foreach (var a in c.Args) nc.Args.Add(CloneExpr(a));
        return nc;
    }
}

