namespace Il2Cpp.Transpiler.Ir;

using Il2Cpp.Transpiler.Emit;

// DivUn: unsigned int32 division (div.un). StackFold does not map div.un
// today (NotSupported), so no emitted code contains it; Normalize folds
// constant-divisor DivUn to a logical shift (ShrUn) at IR level.
public enum BinaryOp { Add, Sub, Mul, Div, Rem, And, Or, Xor, Shl, Shr, ShrUn, DivUn, Ceq, Cgt, Clt, CgtUn, CltUn }
public enum UnaryOp { Neg, Not }

public abstract class Expr { }
// IsInt64: the constant was loaded by ldc.i8 (an int64-typed value). In-range
// int64 constants are bit-identical to int32, so a STANDALONE constant folds
// to the int32 model -- but ANY operation combining it with another operand
// would be int64-typed in .NET (int32 + int64 -> int64), which the int32
// IR/ABI cannot represent (silent wrap). The marker lets the fold reject such
// mixed int64 arithmetic loudly; conv.i4 (explicit narrowing) clears it.
public sealed class IntConstExpr : Expr { public required long Value { get; init; } public bool IsInt64 { get; init; } }
// ldc.i8 constant OUTSIDE the int32 range (M5 wiring; audit C3 allowed only
// in-range int64 constants). Holds the full 64-bit value; emits as an int64_t
// C++ literal ("LL"). The emitter's TryFoldInt32 does NOT fold this node, so
// any arithmetic combining it stays int64 in C++ (correct .NET widening).
public sealed class Int64ConstExpr : Expr { public required long Value { get; init; } }
// ldc.r8 constant (M5 wiring): the raw double from the IL operand. Emits as a
// C++ double literal; NaN bit patterns emit il2cpp_double_from_bits(<raw>)
// (bit-exact -- the C NAN macro is a float QNaN, not .NET's 0xFFF8...0000
// canonical NaN) and +/-Infinity emit INFINITY/-INFINITY (the generated header
// includes <cmath>). TryFoldInt32 does not fold this node.
public sealed class DoubleConstExpr : Expr { public required double Value { get; init; } }
public sealed class StringConstExpr : Expr
{
    public required string Value { get; init; }
    // p-refl2-gapmatrix (B08): for a typeof-chain TYPE VALUE, the metadata
    // TOKEN the value was folded from (the GetTypeFromHandle fold). Lets a
    // downstream GetMethod/GetField record the member's declaring type -- the
    // simplified member model represents a member VALUE as its name string,
    // which loses the declaring type (net48's GetMethod on a base type returns
    // the BASE declaration). Null for a plain string constant.
    public int? TypeToken { get; init; }
}
public sealed class NullExpr : Expr
{
    // ldnull: null managed reference (emitted as nullptr). TypeName records
    // the TRACKED managed type of a folded null (the Encoding singleton
    // getters fold to null with "System.Text.UTF8Encoding" etc.) so a
    // callvirt on the null receiver resolves with the receiver hint
    // (GetBytes/GetString page dispatch). The emitter ignores it.
    public string? TypeName { get; init; }
}
public sealed class LocExpr : Expr { public required string Name { get; init; } }

//------------------------------------------------------------------------------
// ExceptionEntryExpr -- the synthetic value the fold pushes at a CATCH
// handler start (.NET semantics: the handler stack begins with the exception
// object). The M3 carrier (DotNetException) has no managed Exception object;
// the value is rendered as the exception's SHORT type name interned as a
// managed string -- GetType()/get_Name on it fold to identities, so
// `catch (Exception e) { e.GetType().Name }` prints the exception type name.
//   Emitted as: il2cpp::il2cpp_string_intern_literal(<short type name of
//               __il2cpp_ex>) -- only in scope inside the generated catch.
//------------------------------------------------------------------------------
public sealed class ExceptionEntryExpr : Expr { }

//------------------------------------------------------------------------------
// LocAddrExpr -- address of a local (ldloca / ldarga): &Name. Used by
// value-type construction (this = &p) and byref passing.
//------------------------------------------------------------------------------
public sealed class LocAddrExpr : Expr { public required string Name { get; init; } }
public sealed class StaticFieldExpr : Expr { public required string FieldName { get; init; } }
//------------------------------------------------------------------------------
// StaticFieldAddrExpr -- address of a static field (ldsflda, ECMA 0x7F):
// &<storage>. The ldflda counterpart for static fields. `"A:" + order` (order
// a static int) compiles to ldsflda + call instance Int32::ToString() -- the
// byref receiver is the field ADDRESS; the resolver's receiverIsAddress
// routing picks the "deref:*" arm (a LOAD of the field value), never the
// "unbox:*" arm (which would pointer-cast the int32 value).
//------------------------------------------------------------------------------
public sealed class StaticFieldAddrExpr : Expr { public required string FieldName { get; init; } }
public sealed class BinaryExpr : Expr
{
    public required BinaryOp Op { get; init; }
    public required Expr Left { get; init; }
    public required Expr Right { get; init; }
    // M5 wiring: the operand kind when either side is a double ("d") or an
    // int64 constant ("l") -- the C++ operator resolves by operand type, so
    // the emitter only needs the kind for temp typing and reachability folds.
    public string? Kind { get; init; }
}
public sealed class UnaryExpr : Expr { public required UnaryOp Op { get; init; } public required Expr Operand { get; init; } }
public sealed class CallExpr : Expr
{
    public required string Function { get; init; }
    public List<Expr> Args { get; } = new();
    // true: the call returns a managed reference (Il2CppObject*) -- determines
    // the declared C++ type of the temporary that receives the result.
    public bool ReturnsRef { get; init; }
    // M5 wiring: the C++ result kind for VALUE-typed results when they are
    // NOT int32 ("l" int64_t / "d" double). Null = int32 (or ref per
    // ReturnsRef). Set by the fold for synthesized icall calls (conv folds);
    // the emitter's ExprKind uses it to type the receiving temp.
    public string? ResultKind { get; set; }
    // M5 BCL wiring: the fold-time resolution (receiver-type-aware; the
    // emitter must use this verbatim -- it cannot reproduce the receiver-type
    // context). Null for user-method calls (resolved by token at emit time).
    public ResolvedMethod? Resolved { get; set; }
    // M4 Task 3: P/Invoke string-argument marshalling -- the indices of the
    // args that are managed strings (marshalled to UTF-8 at the call site).
    public bool IsPInvoke { get; init; }
    public List<int> StringArgIndices { get; init; } = new();
    // M4 Task 4: [DllImport(SetLastError=true)] -- capture the OS error after.
    public bool SetLastError { get; init; }
    // p-refl2-gapmatrix (B08): the member-VALUE provenance for a folded
    // typeof-chain GetMethod/GetField call -- (declaring type token, member
    // name, isField). The fold attaches it so a downstream member flag
    // predicate (IsAbstract/IsVirtual/IsGenericMethod/IsInitOnly/IsLiteral)
    // can resolve the EXACT declaration from the metadata (the name-string
    // member VALUE alone cannot -- net48's GetMethod on a base type returns
    // the base declaration, not the derived override). Null otherwise.
    public (int TypeToken, string MemberName, bool IsField)? MemberProvenance { get; set; }
}

//------------------------------------------------------------------------------
// MarshalUtf8Expr -- P/Invoke string marshalling (audit H5): a managed
// string marshalled into its OWN heap-allocated UTF-8 buffer. The value is a
// const char* (the temp that holds it is declared as such). Each P/Invoke
// string argument gets its own temp BEFORE the call (independent buffers --
// the runtime's rotating static buffer would alias multiple arguments); the
// generated call site frees each buffer after the call
// (il2cpp_pinvoke_utf8_free).
//   Emitted as: il2cpp::il2cpp_pinvoke_string_to_utf8_alloc((Il2CppString*)(arg))
//------------------------------------------------------------------------------
public sealed class MarshalUtf8Expr : Expr
{
    public required Expr Arg { get; init; }
    // wf2 (winforms1): CharSet.Unicode P/Invokes marshal string args as
    // UTF-16 (il2cpp_pinvoke_string_to_utf16_alloc / pinvoke_utf16_free) --
    // the Win32 W-entry functions read char16_t* buffers; a UTF-8 buffer
    // would be misread (class name corruption, window creation failure).
    public bool Utf16 { get; init; }
}

//------------------------------------------------------------------------------
// VirtualCallExpr -- callvirt dispatch (Task 7).
//   MethodToken : the virtual method token (MethodDef/MemberReference) --
//                 resolved at emit time to its slot on the DECLARING type's
//                 vtable, then dispatched through the RECEIVER's runtime type
//                 descriptor: ((void(*)(...))obj->klass->vtable[slot])(obj,..)
//   Args[0]     : the receiver (this)
//   The receiver null-check is folded into the dispatch (Task 9's full
//   null-check system; M2 the vtable read on null crashes as the runtime
//   diagnostic -- a generated il2cpp_null_check wraps the receiver).
//------------------------------------------------------------------------------
public sealed class VirtualCallExpr : Expr
{
    public required int MethodToken { get; init; }
    public required bool ReturnsRef { get; init; }
    // tuple1 (Tuple): a callvirt whose resolved C++ result is a generated tuple
    // struct ("vt:<closedName>") -- the result temp must declare as that struct,
    // not int32_t (the arity-8 get_Rest chain). Optional.
    public string? ResultKind { get; init; }
    // Devirt S1 (G2): the receiver's STATIC type as tracked by StackFold's
    // objTypes (newobj/box/GetType/delegate results propagated through stores).
    // Carried to the emitter so EmitVirtualCall can devirtualize a call on an
    // effectively-sealed receiver to a direct call (the type is the FULL name
    // -- e.g. "Cases.Square"; a generic instantiation like "Box`1_int" matches
    // the generic definition via the arity-prefix rule). Optional: null when
    // no tracked type exists (plain locals/params) -- those keep vtable
    // dispatch. IR passes pass it through verbatim (clone sites).
    public string? ReceiverTypeName { get; init; }
    public List<Expr> Args { get; } = new();
}

//------------------------------------------------------------------------------
// NewObjExpr -- object allocation ONLY (the constructor call is folded by
// StackFold into a separate CallStmt; see StackFold.EmitNewObj).
//   TypeFullName : the type being allocated (e.g. "Calc.Point") -- selects
//                  the generated struct (Calc_Point) and its class descriptor
//                  (Calc_Point_class) at emit time.
//   Emitted as   : il2cpp_object_new(&<T>_class)  (returns Il2CppObject*)
//------------------------------------------------------------------------------
public sealed class NewObjExpr : Expr
{
    public required string TypeFullName { get; init; }
}

//------------------------------------------------------------------------------
// ValueTypeNewExpr -- value-type newobj (M3 Task 11): a struct instance is
// initialized IN PLACE (no allocation -- value types are plain C++ values).
//   Emitted as: {T} __t; {T}__ctor_hash(&__t, args...);  (the ctor call is a
//   separate statement; this expression carries the declaration + ctor token)
//------------------------------------------------------------------------------
public sealed class ValueTypeNewExpr : Expr
{
    public required string TypeFullName { get; init; }
    public required int CtorToken { get; init; }
}

//------------------------------------------------------------------------------
// FuncPtrExpr -- ldftn: the address of a method (delegate creation input).
//   MethodToken : the method whose address is taken (MethodDef/MemberReference).
//   Emitted as  : &<MethodCppName>  (the method's generated function address)
//------------------------------------------------------------------------------
public sealed class FuncPtrExpr : Expr
{
    public required int MethodToken { get; init; }
}

//------------------------------------------------------------------------------
// DelegateInvokeExpr -- callvirt <DelegateType>::Invoke (delegate dispatch).
//   Args[0] : the delegate object; Args[1..] : the delegate's parameters.
//   int32-returning int32-arg signatures (<=4 args) emit
//   icall_multicast_invoke_i4 (the runtime flattens multicast containers and
//   calls every leaf -- raw methodPtr dispatch would read a container's
//   `first` field, same offset as Il2CppDelegate::methodPtr, and call a data
//   pointer as code); other signatures keep
//   ((RetType(*)(...))((Il2CppDelegate*)d)->methodPtr)(args...) (single-cast
//   only).
//------------------------------------------------------------------------------
public sealed class DelegateInvokeExpr : Expr
{
    public required int MethodToken { get; init; }
    public required bool ReturnsRef { get; init; }
    // P2-HARDEN #3: the delegate Invoke RETURN width. The P-D2 #1 multicast
    // helper / ref-ABI icalls return the full-width leaf type (int64_t /
    // double / float); a result landing on a COMPILER temp (branch-merge join
    // slots etc.) must declare that width -- without it, ExprKind/TempSlotPass
    // classify the temp "val" and truncate the widened helper result to
    // int32_t. "l" / "d" / "float" are set by the fold; null = int32-family.
    public string? ResultKind { get; init; }
    public List<Expr> Args { get; } = new();
}

//------------------------------------------------------------------------------
// DelegateNewExpr -- delegate construction (newobj System.Func`N with a
// method pointer): il2cpp_delegate_new(&<MethodCppName>, <Target>, <TypeId>).
// Target is the ctor's non-ldftn argument -- the bound instance for an
// instance-method binding (instance ABI) or nullptr for a static binding
// (the runtime default; the shape stays uniform, #42).
// TypeId (batch 177b, BUG-2): the delegate-type ID as a uint64_t C++ literal
// (FNV-1a of the delegate's full type name -- SigHash.UInt64Literal). The
// runtime stores it on the delegate and compares at Combine time, raising
// ArgumentException on a heterogeneous combine (net48). "0" (no stamp) means
// the creation path could not name the type -> the runtime conservatively
// skips the check for delegates created through it.
//------------------------------------------------------------------------------
public sealed class DelegateNewExpr : Expr
{
    public required int MethodToken { get; init; }
    public required Expr Target { get; init; }
    public string TypeId { get; init; } = "0";
}


//------------------------------------------------------------------------------
// FieldExpr -- read of an INSTANCE field: (Object->FieldCppName).
//   Object       : the object expression whose field is read (a GC pointer).
//   FieldCppName : the C++ member name on the generated struct (Sanitize(field)).
//   FieldTypeName: full ECMA type name of the field's declaring type, used to
//                  pick the correct generated struct type for the pointer
//                  dereference (e.g. Calc_Point* obj; obj->X).
// Emitted as `(<obj>)-><FieldCppName>`; the receiver is null-checked at emit
// time (il2cpp_null_check) because instance dereference on null is a
// NullReferenceException in .NET.
//------------------------------------------------------------------------------
public sealed class FieldExpr : Expr
{
    public required Expr Object { get; init; }
    public required string FieldCppName { get; init; }
    public required string OwnerFullName { get; init; } // e.g. "Calc.Point" -> struct Calc_Point
    public bool IsRefField { get; init; } // true: the field holds a GC reference
    public bool IsValueTypeObject { get; init; } // true: the receiver is a value-type value (no null check, '.' access)
}

//------------------------------------------------------------------------------
// FieldAddrExpr -- address of an INSTANCE field (ldflda): &(Object->FieldCppName).
//   Emitted as `(&(obj)->FieldCppName)`. M2 scope: used by callers that need a
//   byref (e.g. argument passing); the M2 emitter supports it where the
//   consuming instruction is understood. Producing an address into a GC object
//   is safe ONLY for the duration of the statement sequence (no GC point
//   between), matching the M2 non-moving collector.
//------------------------------------------------------------------------------
public sealed class FieldAddrExpr : Expr
{
    public required Expr Object { get; init; }
    public required string FieldCppName { get; init; }
    public required string OwnerFullName { get; init; }
    // batch 156c (asynccase): true when the receiver is a value-type VALUE
    // (the async state machine's builder/awaiter fields -- ldflda on a
    // struct local). The emitter skips the null-check reference cast (a
    // struct address cannot static_cast to Il2CppObject*).
    public bool IsValueTypeObject { get; init; }
}

//------------------------------------------------------------------------------
// IrNode -- canonical IR clone/serialize dispatch (IR-node completeness audit,
// batch 23). Every consumer pass (DCE / TempSlot / Inline / AllocPromo /
// RootSlot / CodeGenerator) hand-rolls its own node switch; a NEW node type
// added to the IR can be silently missed by one of them. This is the SINGLE
// TOTAL switch over every Stmt/Expr subtype (the Stmt half lives in Stmt.cs) --
// IrNodeCoverageTests mirrors the inventory via reflection, so a subtype added
// WITHOUT a dispatch arm fails the suite loudly instead of silently producing
// wrong pass output.
//
//   CloneExpr / CloneStmt  : DEEP structural clone -- every child node is
//                            re-created and every arg list is copied to a new
//                            list (never a shared reference). ResolvedMethod
//                            references are shared (the codebase's pass
//                            convention: all rewriters copy Resolved by
//                            reference).
//   SerializeExpr/SerializeStmt : deterministic compact structural rendering
//                            (for tests and debug output). Provenance
//                            (SourceOffset / SourceOpName) is intentionally
//                            excluded -- it is emission metadata, not IR
//                            structure.
//
// DiscardedValueExpr (StackFold's fold-only pop marker, defined in StackFold.
// cs) is covered too: the switch must stay total for every pass that scans
// pre-emission IR, even though the marker never reaches the emitter.
//------------------------------------------------------------------------------
public static partial class IrNode
{
    //--------------------------------------------------------------------------
    // Serialization.
    //--------------------------------------------------------------------------
    public static string SerializeExpr(Expr e) => e switch
    {
        IntConstExpr i => i.IsInt64 ? $"Int({i.Value}, i64)" : $"Int({i.Value})",
        Int64ConstExpr l => $"Int64({l.Value})",
        DoubleConstExpr d => $"Double({d.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)})",
        StringConstExpr s => $"Str({Quote(s.Value)})",
        NullExpr n => n.TypeName == null ? "Null" : $"Null({n.TypeName})",
        LocExpr l => $"Loc({l.Name})",
        ExceptionEntryExpr => "ExceptionEntry",
        LocAddrExpr la => $"LocAddr({la.Name})",
        StaticFieldExpr f => $"StaticField({f.FieldName})",
        StaticFieldAddrExpr fa => $"StaticFieldAddr({fa.FieldName})",
        BinaryExpr b => $"({SerializeExpr(b.Left)} {OpSymbol(b.Op)} {SerializeExpr(b.Right)}){(b.Kind is null ? "" : $"[{b.Kind}]")}",
        UnaryExpr u => $"({(u.Op == UnaryOp.Neg ? "-" : "~")}{SerializeExpr(u.Operand)})",
        CallExpr c => SerializeCall("Call", c.Function, c.ReturnsRef, c.ResultKind, c.Resolved, c.StringArgIndices, c.SetLastError, c.Args),
        VirtualCallExpr v => $"VirtualCall({v.MethodToken}, {Args(v.Args)}){(v.ReturnsRef ? "@ref" : "")}",
        DelegateInvokeExpr d => $"DelegateInvoke({d.MethodToken}, {Args(d.Args)}){(d.ReturnsRef ? "@ref" : "")}{(d.ResultKind != null ? $" :{d.ResultKind}" : "")}",
        DelegateNewExpr dn => $"DelegateNew({dn.MethodToken}, {SerializeExpr(dn.Target)})",
        MarshalUtf8Expr m => $"MarshalUtf8({SerializeExpr(m.Arg)}){(m.Utf16 ? "@u16" : "")}",
        FieldExpr f => $"Field({SerializeExpr(f.Object)}, {f.OwnerFullName}.{f.FieldCppName}){(f.IsRefField ? "@ref" : "")}{(f.IsValueTypeObject ? "{vt}" : "")}",
        FieldAddrExpr fa => $"FieldAddr({SerializeExpr(fa.Object)}, {fa.OwnerFullName}.{fa.FieldCppName}){(fa.IsValueTypeObject ? "{vt}" : "")}",
        NewObjExpr n => $"New({n.TypeFullName})",
        ValueTypeNewExpr vn => $"ValueTypeNew({vn.TypeFullName}, {vn.CtorToken})",
        FuncPtrExpr fp => $"FuncPtr({fp.MethodToken})",
        StackFold.DiscardedValueExpr dv => $"DiscardedValue({dv.CallToken}@{dv.Offset})",
        _ => throw new NotSupportedException($"IR expression {e.GetType().Name} has no SerializeExpr arm (IR-node completeness audit)"),
    };

    private static string SerializeCall(string name, string fn, bool returnsRef, string? resultKind,
        ResolvedMethod? resolved, System.Collections.Generic.IReadOnlyList<int> stringArgs, bool setLastError,
        System.Collections.Generic.IReadOnlyList<Expr> args)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(name).Append('(').Append(fn);
        foreach (var a in args) sb.Append(", ").Append(SerializeExpr(a));
        sb.Append(')');
        if (returnsRef) sb.Append("@ref");
        if (resultKind != null) sb.Append('[').Append(resultKind).Append(']');
        if (resolved?.CppName != null) sb.Append('!').Append(resolved.CppName);
        if (stringArgs.Count > 0)
        {
            sb.Append("#str(");
            for (var i = 0; i < stringArgs.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(stringArgs[i]);
            }
            sb.Append(')');
        }
        if (setLastError) sb.Append("&le");
        return sb.ToString();
    }

    private static string Args(System.Collections.Generic.IReadOnlyList<Expr> args)
    {
        var parts = new List<string>(args.Count);
        foreach (var a in args) parts.Add(SerializeExpr(a));
        return string.Join(", ", parts);
    }

    private static string OpSymbol(BinaryOp op) => op switch
    {
        BinaryOp.Add => "+", BinaryOp.Sub => "-", BinaryOp.Mul => "*",
        BinaryOp.Div => "/", BinaryOp.Rem => "%", BinaryOp.And => "&",
        BinaryOp.Or => "|", BinaryOp.Xor => "^", BinaryOp.Shl => "<<",
        BinaryOp.Shr => ">>", BinaryOp.ShrUn => ">>u", BinaryOp.DivUn => "/u",
        BinaryOp.Ceq => "==", BinaryOp.Cgt => ">", BinaryOp.Clt => "<",
        BinaryOp.CgtUn => ">u", BinaryOp.CltUn => "<u",
        _ => throw new NotSupportedException($"binary op {op} has no SerializeExpr symbol"),
    };

    private static string Quote(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(ch); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    //--------------------------------------------------------------------------
    // Deep clone.
    //--------------------------------------------------------------------------
    public static Expr CloneExpr(Expr e) => e switch
    {
        IntConstExpr i => new IntConstExpr { Value = i.Value, IsInt64 = i.IsInt64 },
        Int64ConstExpr l => new Int64ConstExpr { Value = l.Value },
        DoubleConstExpr d => new DoubleConstExpr { Value = d.Value },
        StringConstExpr s => new StringConstExpr { Value = s.Value },
        NullExpr n => new NullExpr { TypeName = n.TypeName },
        LocExpr l => new LocExpr { Name = l.Name },
        ExceptionEntryExpr => new ExceptionEntryExpr(),
        LocAddrExpr la => new LocAddrExpr { Name = la.Name },
        StaticFieldExpr f => new StaticFieldExpr { FieldName = f.FieldName },
        StaticFieldAddrExpr fa => new StaticFieldAddrExpr { FieldName = fa.FieldName },
        BinaryExpr b => new BinaryExpr { Op = b.Op, Left = CloneExpr(b.Left), Right = CloneExpr(b.Right), Kind = b.Kind },
        UnaryExpr u => new UnaryExpr { Op = u.Op, Operand = CloneExpr(u.Operand) },
        CallExpr c => CloneCall(c),
        VirtualCallExpr v => CloneVirtual(v),
        DelegateInvokeExpr d => CloneDelegate(d),
        DelegateNewExpr dn => new DelegateNewExpr { MethodToken = dn.MethodToken, Target = CloneExpr(dn.Target), TypeId = dn.TypeId },
        MarshalUtf8Expr m => new MarshalUtf8Expr { Arg = CloneExpr(m.Arg), Utf16 = m.Utf16 },
        FieldExpr f => new FieldExpr
        {
            Object = CloneExpr(f.Object),
            FieldCppName = f.FieldCppName,
            OwnerFullName = f.OwnerFullName,
            IsRefField = f.IsRefField,
            IsValueTypeObject = f.IsValueTypeObject,
        },
        FieldAddrExpr fa => new FieldAddrExpr
        {
            Object = CloneExpr(fa.Object),
            FieldCppName = fa.FieldCppName,
            OwnerFullName = fa.OwnerFullName,
            IsValueTypeObject = fa.IsValueTypeObject,
        },
        NewObjExpr n => new NewObjExpr { TypeFullName = n.TypeFullName },
        ValueTypeNewExpr vn => new ValueTypeNewExpr { TypeFullName = vn.TypeFullName, CtorToken = vn.CtorToken },
        FuncPtrExpr fp => new FuncPtrExpr { MethodToken = fp.MethodToken },
        StackFold.DiscardedValueExpr dv => new StackFold.DiscardedValueExpr { CallToken = dv.CallToken, Offset = dv.Offset },
        _ => throw new NotSupportedException($"IR expression {e.GetType().Name} has no CloneExpr arm (IR-node completeness audit)"),
    };

    private static CallExpr CloneCall(CallExpr c)
    {
        var nc = new CallExpr
        {
            Function = c.Function,
            ReturnsRef = c.ReturnsRef,
            ResultKind = c.ResultKind,
            Resolved = c.Resolved,
            IsPInvoke = c.IsPInvoke,
            SetLastError = c.SetLastError,
        };
        foreach (var a in c.Args) nc.Args.Add(CloneExpr(a));
        foreach (var i in c.StringArgIndices) nc.StringArgIndices.Add(i);
        return nc;
    }

    private static VirtualCallExpr CloneVirtual(VirtualCallExpr v)
    {
        var nv = new VirtualCallExpr { MethodToken = v.MethodToken, ReturnsRef = v.ReturnsRef, ResultKind = v.ResultKind, ReceiverTypeName = v.ReceiverTypeName };
        foreach (var a in v.Args) nv.Args.Add(CloneExpr(a));
        return nv;
    }

    private static DelegateInvokeExpr CloneDelegate(DelegateInvokeExpr d)
    {
        var nd = new DelegateInvokeExpr { MethodToken = d.MethodToken, ReturnsRef = d.ReturnsRef, ResultKind = d.ResultKind };
        foreach (var a in d.Args) nd.Args.Add(CloneExpr(a));
        return nd;
    }
}


