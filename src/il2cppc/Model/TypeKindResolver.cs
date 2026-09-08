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
            case 0x0C: return isStaticField ? (FieldTypeKind.Int32, "System.Single", "int32_t") : (FieldTypeKind.Float, "System.Single", "float");
            case 0x0D: return isStaticField ? (FieldTypeKind.Int32, "System.Double", "int32_t") : (FieldTypeKind.Double, "System.Double", "double");
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

    private static (FieldTypeKind Kind, string FullName, string CppKind) ArgKind(string fullName) => fullName switch
    {
        "System.Int64" => (FieldTypeKind.Int64, fullName, "int64_t"),
        "System.UInt64" => (FieldTypeKind.UInt64, fullName, "int64_t"),
        "System.Single" => (FieldTypeKind.Float, fullName, "float"),
        "System.Double" => (FieldTypeKind.Double, fullName, "double"),
        "System.IntPtr" or "System.UIntPtr" => (FieldTypeKind.NativeInt, fullName, "intptr_t"),
        "System.Boolean" or "System.Byte" or "System.SByte" or "System.Char" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" => (FieldTypeKind.Int32, fullName, "int32_t"),
        _ when fullName is "System.String" or "System.Object" => (FieldTypeKind.Reference, fullName, "Il2CppObject*"),
        _ when fullName.EndsWith("[]", StringComparison.Ordinal) => (FieldTypeKind.Reference, "System.Array", "Il2CppObject*"),
        _ => (FieldTypeKind.Reference, fullName, "Il2CppObject*"),
    };

    public static string? ResolveExprKind(Ir.Expr e, IReadOnlyDictionary<string, string> locKind)
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
            Ir.DelegateInvokeExpr d when d.ReturnsRef => "ref",
            Ir.FieldExpr f when f.IsRefField => "ref",
            Ir.LocExpr l when locKind.TryGetValue(l.Name, out var k) => k,
            _ => "val",
        };
    }

    private static string SimpleCppName(string fullName) => string.Join("_", fullName.Split('.', '+', '`', '<', '>', ',', '[', ']', '=', '!', '*', '&', ':'));
}
