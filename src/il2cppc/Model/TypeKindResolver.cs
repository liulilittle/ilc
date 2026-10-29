using System.Reflection.Metadata;
using Il2Cpp.Transpiler.Ir;

namespace Il2Cpp.Transpiler.Model;

public static class TypeKindResolver
{
    public static (FieldTypeKind Kind, string FullName, string CppKind) Resolve(
        byte elementType,
        int varIndex = -1,
        string[]? genericArgs = null,
        bool isStaticField = false)
    {
        switch (elementType)
        {
            case 0x0E: return (FieldTypeKind.Reference, "System.String", "Il2CppObject*");
            case 0x1C: return (FieldTypeKind.Reference, "System.Object", "Il2CppObject*");
            case 0x12: return (FieldTypeKind.Reference, "", "Il2CppObject*");
            case 0x15: return (FieldTypeKind.Reference, "", "Il2CppObject*");
            case 0x1D: case 0x14: return (FieldTypeKind.Reference, "System.Array", "Il2CppObject*");
            case 0x13:
                if (genericArgs != null && varIndex >= 0 && varIndex < genericArgs.Length && !string.IsNullOrEmpty(genericArgs[varIndex]))
                    return ArgKind(genericArgs[varIndex]);
                return (FieldTypeKind.Int32, "", "int32_t");
            case 0x1E: return (FieldTypeKind.Reference, "System.Object", "Il2CppObject*");
            case 0x0A: return (FieldTypeKind.Int64, "System.Int64", "int64_t");
            case 0x0B: return (FieldTypeKind.UInt64, "System.UInt64", "int64_t");
            // probe-sfieldfloat: the static-field arm keeps the REAL floating
            // width too -- int32_t storage truncated every store/load of a
            // `static double/float` (the same defect MetadataLoader.
            // DecodeExtendedStaticType carried). No caller passes
            // isStaticField today; the table is kept consistent so a future
            // static-field caller cannot reintroduce the truncation.
            case 0x0C: return (FieldTypeKind.Float, "System.Single", "float");
            case 0x0D: return (FieldTypeKind.Double, "System.Double", "double");
            case 0x18: case 0x19: return isStaticField ? (FieldTypeKind.Int32, "System.IntPtr", "int32_t") : (FieldTypeKind.NativeInt, "System.IntPtr", "intptr_t");
            case 0x11:
                if (varIndex >= 0 && genericArgs != null && varIndex < genericArgs.Length)
                {
                    var ga = genericArgs[varIndex];
                    if (string.IsNullOrEmpty(ga)) return (FieldTypeKind.Int32, "", "int32_t");
                    return ArgKind(ga);
                }
                return (FieldTypeKind.Int32, "", "int32_t");
            case 0x02: case 0x03: case 0x04: case 0x05: case 0x06: case 0x07: case 0x08: case 0x09: return (FieldTypeKind.Int32, "", "int32_t");
            case 0x0F: return isStaticField ? (FieldTypeKind.Int32, "System.IntPtr", "int32_t") : (FieldTypeKind.NativeInt, "System.IntPtr", "intptr_t");
            default: return (FieldTypeKind.Reference, "System.Object", "Il2CppObject*");
        }
    }

    public static (FieldTypeKind Kind, string FullName, string CppKind) ResolveFromFullName(string fullName) => ArgKind(fullName);

    //--------------------------------------------------------------------------
    // The BCL runtime VALUE structs whose INSTANCE field (a closure display
    // class member, a nested-type field) must keep the REAL runtime value
    // shape -- never an int32 slot.
    //
    // The int32 fallback silently truncated the value: a captured DateTime
    // became `int32_t d;` while the IL value is the 16-byte
    // il2cpp::runtime::Il2CppDateTime -- the store (->d = t1) failed to
    // compile and the read silently took 8 bytes out of a 4-byte slot.
    //
    // The sizes/alignments are the runtime structs' own (il2cpp_time.h /
    // decimal.h / il2cpp_dtoffset.h / guid.h): Guid 16/4 (alignas(4)),
    // DateTime 16/8, TimeSpan 8/8, Decimal 16/8, DateTimeOffset 16/8.
    //--------------------------------------------------------------------------
    public static bool TryGetValueStructFieldKind(string fullName, out FieldTypeKind kind)
    {
        switch (fullName)
        {
            case "System.Guid": kind = FieldTypeKind.Guid; return true;
            case "System.DateTime": kind = FieldTypeKind.DateTime; return true;
            case "System.TimeSpan": kind = FieldTypeKind.TimeSpan; return true;
            case "System.Decimal": kind = FieldTypeKind.Decimal; return true;
            case "System.DateTimeOffset": kind = FieldTypeKind.DateTimeOffset; return true;
            default: kind = FieldTypeKind.Int32; return false;
        }
    }

    //--------------------------------------------------------------------------
    // System.Nullable`1<T> identity + underlying-T extraction. The nested
    // closure-field decoder (TypeEmitter.DecodeNestedFieldSignature) sees a
    // GENERICINST (0x15) with this definition name and expands it into the
    // underlying T value slot plus a hasValue(bool) slot.
    //--------------------------------------------------------------------------
    public static bool IsNullableFullName(string fullName)
        => fullName.StartsWith("System.Nullable`1<", StringComparison.Ordinal)
           && fullName.EndsWith(">", StringComparison.Ordinal);

    public static string? NullableUnderlyingName(string fullName)
    {
        const string prefix = "System.Nullable`1<";
        if (!IsNullableFullName(fullName)) return null;
        return fullName.Substring(prefix.Length, fullName.Length - prefix.Length - 1);
    }

    // The emitted C++ member type and the native (size, alignment) of a
    // value-struct field kind.
    public static (string CppType, int SizeBytes, int AlignBytes) ValueStructFieldShape(FieldTypeKind kind) => kind switch
    {
        FieldTypeKind.Guid => ("il2cpp::Il2CppGuid", 16, 4),
        FieldTypeKind.DateTime => ("il2cpp::runtime::Il2CppDateTime", 16, 8),
        FieldTypeKind.TimeSpan => ("il2cpp::runtime::Il2CppTimeSpan", 8, 8),
        FieldTypeKind.Decimal => ("il2cpp::runtime::Il2CppDecimal", 16, 8),
        FieldTypeKind.DateTimeOffset => ("il2cpp::runtime::Il2CppDateTimeOffset", 16, 8),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a value-struct field kind"),
    };

    // Whether the kind is one of the runtime value-struct field kinds (the
    // member holds a value struct, never a GC reference and never an int32).
    public static bool IsValueStructFieldKind(FieldTypeKind kind)
        => kind is FieldTypeKind.Guid or FieldTypeKind.DateTime or FieldTypeKind.TimeSpan
            or FieldTypeKind.Decimal or FieldTypeKind.DateTimeOffset;

    // The expression kind of a value-struct field read -- the same carrier
    // names the value-typed LOCALS use ("dt"/"ts"/"dec"/"dto"/"guid"), so a
    // field read and a local read bridge identically.
    public static string? ValueStructExprKind(FieldTypeKind kind) => kind switch
    {
        FieldTypeKind.Guid => "guid",
        FieldTypeKind.DateTime => "dt",
        FieldTypeKind.TimeSpan => "ts",
        FieldTypeKind.Decimal => "dec",
        FieldTypeKind.DateTimeOffset => "dto",
        _ => null,
    };

    private static (FieldTypeKind Kind, string FullName, string CppKind) ArgKind(string fullName) => fullName switch
    {
        "System.Int64" => (FieldTypeKind.Int64, fullName, "int64_t"),
        "System.UInt64" => (FieldTypeKind.UInt64, fullName, "int64_t"),
        "System.Single" => (FieldTypeKind.Float, fullName, "float"),
        "System.Double" => (FieldTypeKind.Double, fullName, "double"),
        "System.IntPtr" or "System.UIntPtr" => (FieldTypeKind.NativeInt, fullName, "intptr_t"),
        // B3: System.Guid keeps the 16-byte value shape (guid.h Il2CppGuid),
        // never a GC reference / int32 slot.
        "System.Guid" => (FieldTypeKind.Guid, fullName, "il2cpp::Il2CppGuid"),
        "System.Boolean" or "System.Byte" or "System.SByte" or "System.Char" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" => (FieldTypeKind.Int32, fullName, "int32_t"),
        _ when fullName is "System.String" or "System.Object" => (FieldTypeKind.Reference, fullName, "Il2CppObject*"),
        _ when fullName.EndsWith("[]", StringComparison.Ordinal) => (FieldTypeKind.Reference, "System.Array", "Il2CppObject*"),
        _ => (FieldTypeKind.Reference, fullName, "Il2CppObject*"),
    };

    public static string? ResolveExprKind(Ir.Expr e, IReadOnlyDictionary<string, string> locKind)
    {
        return ResolveExprKindCore(e, locKind) ?? "val";
    }

    // B4-1 (D9): the strict variant -- the SAME classification, but an
    // expression the cases below do not identify stays "unknown" instead of the
    // historical "val" fallback. ResolveExprKind keeps "val" (many call sites
    // rely on "not ref" meaning "int32-ish"), but IsValueSlotArg must never
    // treat an unrecognized shape as a PROVEN value slot, so it reads this one.
    public static string? ResolveExprKindStrict(Ir.Expr e, IReadOnlyDictionary<string, string> locKind)
    {
        return ResolveExprKindCore(e, locKind) ?? "unknown";
    }

    private static string? ResolveExprKindCore(Ir.Expr e, IReadOnlyDictionary<string, string> locKind)
    {
        return e switch
        {
            Ir.StringConstExpr => "ref",
            Ir.NullExpr => "ref",
            Ir.FuncPtrExpr => "p",
            Ir.ExceptionEntryExpr => "ref",
            Ir.NewObjExpr => "ref",
            Ir.DelegateNewExpr => "ref",
            Ir.MarshalUtf8Expr m => m.Utf16 ? "cstr16" : "cstr",
            Ir.ValueTypeNewExpr v => v.TypeFullName switch
            {
                "System.Boolean" or "System.Byte" or "System.SByte" or "System.Char" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" => "val",
                "System.Int64" or "System.UInt64" => "l",
                "System.Single" or "System.Double" => "d",
                "System.IntPtr" or "System.UIntPtr" => "ptr",
                "System.DateTime" => "dt",
                "System.TimeSpan" => "ts",
                "System.Decimal" => "dec",
                "System.DateTimeOffset" => "dto",
                "System.Guid" => "guid",
                _ => SimpleCppName(v.TypeFullName),
            },
            Ir.Int64ConstExpr => "l",
            Ir.DoubleConstExpr => "d",
            Ir.BinaryExpr b when b.Kind is "l" or "d" => b.Kind,
            Ir.CallExpr c when c.ResultKind is "l" or "d" or "dt" or "ts" or "guid" or "dec" or "ptr" or "ip4" or "dto" or "bi" => c.ResultKind,
            Ir.CallExpr c when c.ReturnsRef => "ref",
            Ir.VirtualCallExpr v when v.ReturnsRef => "ref",
            Ir.DelegateInvokeExpr d when d.ResultKind is "l" or "d" or "float" => d.ResultKind,
            Ir.DelegateInvokeExpr d when d.ReturnsRef => "ref",
            Ir.FieldExpr f when f.IsRefField => "ref",
            Ir.StaticFieldExpr sf => StaticFieldExprKind(sf.FieldName),
            Ir.LocExpr l when locKind.TryGetValue(l.Name, out var k) => k,
            _ => null,
        };

        static string StaticFieldExprKind(string fieldName)
        {
            // Reference-type static fields (CultureInfo.InvariantCulture etc.) must
            // classify as "ref" so the return-type bridge does not emit an illegal
            // static_cast<intptr_t>(...). Value-type static fields (Decimal.Zero etc.)
            // fall through to "val". Heuristic: known value-type prefixes -> "val",
            // everything else -> "ref" (the overwhelming majority of static fields
            // are reference-type singletons).
            if (fieldName.StartsWith("System.Decimal.", StringComparison.Ordinal)) return "val";
            if (fieldName.StartsWith("System.DateTime.", StringComparison.Ordinal)) return "val";
            // System.Guid.Empty is the shared 16-byte zero slot -- a "guid"
            // value (Il2CppGuid), not a reference.
            if (fieldName == "System.Guid.Empty") return "guid";
            return "ref";
        }
    }

    private static string SimpleCppName(string fullName) => string.Join("_", fullName.Split('.', '+', '`', '<', '>', ',', '[', ']', '=', '!', '*', '&', ':'));
}
