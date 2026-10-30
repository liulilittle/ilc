// Shared ECMA-335 signature renderer for the icall audit tools.
//
// Both the metadata counter and the wiring probe must print a method signature
// in exactly the same shape, because the audit joins their outputs on
// (declaring type, method name, signature). Keeping one renderer guarantees the
// join keys cannot drift.

using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace IcallAuditShared;

public sealed class SignatureProvider : ISignatureTypeProvider<string, object?>
{
    private readonly MetadataReader _reader;

    public SignatureProvider(MetadataReader reader) => _reader = reader;

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "System.Boolean",
        PrimitiveTypeCode.Byte => "System.Byte",
        PrimitiveTypeCode.SByte => "System.SByte",
        PrimitiveTypeCode.Char => "System.Char",
        PrimitiveTypeCode.Int16 => "System.Int16",
        PrimitiveTypeCode.UInt16 => "System.UInt16",
        PrimitiveTypeCode.Int32 => "System.Int32",
        PrimitiveTypeCode.UInt32 => "System.UInt32",
        PrimitiveTypeCode.Int64 => "System.Int64",
        PrimitiveTypeCode.UInt64 => "System.UInt64",
        PrimitiveTypeCode.Single => "System.Single",
        PrimitiveTypeCode.Double => "System.Double",
        PrimitiveTypeCode.IntPtr => "System.IntPtr",
        PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
        PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.String => "System.String",
        PrimitiveTypeCode.TypedReference => "System.TypedReference",
        PrimitiveTypeCode.Void => "System.Void",
        _ => typeCode.ToString(),
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        => TypeName(reader, handle);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var tr = reader.GetTypeReference(handle);
        var name = reader.GetString(tr.Name);
        var scope = tr.ResolutionScope;
        if (scope.Kind == HandleKind.TypeReference)
        {
            var outer = GetTypeFromReference(reader, (TypeReferenceHandle)scope, rawTypeKind);
            return outer + "+" + name;
        }
        var ns = reader.GetString(tr.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetArrayType(string elementType, ArrayShape shape)
        => elementType + "[" + new string(',', shape.Rank - 1) + "]";

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetPinnedType(string elementType) => elementType;

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        => genericType + "<" + string.Join(", ", typeArguments) + ">";

    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

    public string GetFunctionPointerType(MethodSignature<string> signature)
        => "fnptr " + signature.ReturnType + " (" + string.Join(", ", signature.ParameterTypes) + ")";

    private static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var def = reader.GetTypeDefinition(handle);
        var name = reader.GetString(def.Name);
        var declaring = def.GetDeclaringType();
        if (!declaring.IsNil)
            return TypeName(reader, declaring) + "+" + name;
        var ns = reader.GetString(def.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}
