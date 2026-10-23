using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Il2Cpp.Transpiler.Model;

//------------------------------------------------------------------------------
// ValueTupleSupport -- System.ValueTuple`0..8 support for C# 7 tuple syntax.
//
// A C# tuple `(int, string)` IS the ordinary BCL value type
// System.ValueTuple`2<Int32,String>: csc emits a normal `newobj` of its
// .ctor, `ldfld` of its Item1/Item2 fields, and calls to its Equals /
// GetHashCode / ToString overrides. The transpiled assembly references the
// type through a TypeRef (mscorlib), so there is NO TypeDef for it and every
// reference side named a type that was never defined (cmake: "'System_
// ValueTuple_2_int_string_class' was not declared").
//
// This module is the single source of truth for:
//   * the canonical CLOSED INSTANCE NAME of an instantiation -- the same
//     spelling MethodRefResolver/CodeGenerator.GenericInstantiationName
//     produce ("System.ValueTuple`2_int_string"), so the local, the ctor, the
//     field owner and the struct definition can never diverge;
//   * the ELEMENT TABLE of each instantiation (recorded from the metadata
//     signature, never re-derived from the flattened name -- the flattened
//     name is ambiguous when an element is itself a generic instantiation);
//   * each element's STRUCTURAL SHAPE (primitive slot / BCL value struct /
//     nested ValueTuple / user value type / managed reference) and the
//     resulting C++ field type;
//   * the generated struct + member helpers (file scope, inline);
//   * the generated member names the fold resolves to.
//
// net48 SEMANTICS (probe-verified against csc/net48, see
// .opencode/tmp/vtup-notes.md):
//   * arity 1..7 hash: left fold of
//         Combine(h, e) = unchecked((rol5(h) + h) ^ e)
//     seeded with the FIRST element's hash (System.Numerics.Hashing.
//     HashHelpers.Combine -- IL-verified in mscorlib's
//     ValueTuple::CombineHashCodes).
//   * arity 8 (ValueTuple`8<T1..T7,TRest>): with L = TRest's flattened
//     length, the hash is
//         fold(Item_L, Item_{L+1}, ..., Item7, TRest.GetHashCode())
//     and equals TRest.GetHashCode() when L >= 8 (the last 8 slots win).
//   * ToString: "(e1, e2, ...)", flattened across TRest through the
//     ITupleInternal.ToStringEnd contract; a null reference element renders
//     as the EMPTY string ("(1, )").
//------------------------------------------------------------------------------
namespace Il2Cpp.Transpiler.Emit;

// The STRUCTURAL shape of one tuple element -- decided from the element's
// metadata type, never from its flattened name.
internal enum TupleElemShape
{
    Int32,      // int/uint/short/ushort/byte/sbyte (int32 slot)
    Int64,      // long/ulong
    Double,     // double
    Single,     // float (stored as double, formatted/hashed as float)
    Bool,       // bool (int32 slot)
    Char,       // char (int32 slot)
    Decimal,    // System.Decimal (16-byte Il2CppDecimal)
    DateTime,   // System.DateTime (Il2CppDateTime)
    TimeSpan,   // System.TimeSpan (Il2CppTimeSpan)
    Guid,       // System.Guid (16-byte Il2CppGuid)
    Nested,     // a nested System.ValueTuple`N -- carried BY VALUE
    UserStruct, // a translated user value type -- carried BY VALUE
    Ref,        // string/object/array/class/Nullable<T> -- Il2CppObject*
}

internal readonly struct TupleElem
{
    public TupleElem(TupleElemShape shape, string cpp, string token)
    {
        Shape = shape;
        Cpp = cpp;
        Token = token;
    }

    public TupleElemShape Shape { get; }
    // The C++ field type inside the generated struct.
    public string Cpp { get; }
    // The closed instance name (Nested) / full type name -- the registry key
    // of a nested tuple, otherwise informational.
    public string Token { get; }
}

internal static class ValueTupleSupport
{
    internal const string DefPrefix = "System.ValueTuple`";
    internal const string ZeroName = "System.ValueTuple";

    internal static bool IsValueTupleName(string? fullName)
        => fullName != null
           && (fullName == ZeroName || fullName.StartsWith(DefPrefix, StringComparison.Ordinal));

    // The arity encoded in "System.ValueTuple`N[...]"; 0 for the non-generic
    // System.ValueTuple; -1 when not a tuple.
    internal static int Arity(string fullName)
    {
        if (fullName == ZeroName) return 0;
        if (!IsValueTupleName(fullName)) return -1;
        var i = DefPrefix.Length;
        var digits = 0;
        while (i + digits < fullName.Length && char.IsAsciiDigit(fullName[i + digits])) digits++;
        return digits == 0 || !int.TryParse(fullName.AsSpan(i, digits), out var n) ? -1 : n;
    }

    //--------------------------------------------------------------------------
    // ELEMENT TABLE REGISTRY.
    //
    // "System.ValueTuple`N_e1_e2_..." cannot be split back into its elements
    // unambiguously (an element name may itself contain '_'), so every decode
    // site records the ELEMENT ARRAY it already had in hand. ElementNames then
    // answers from the registry; the greedy fallback only serves a name the
    // registry has never seen (in which case nothing can compare against it).
    //--------------------------------------------------------------------------
    private static readonly Dictionary<string, string[]> _elements = new(StringComparer.Ordinal);

    internal static void RecordElements(string defName, IReadOnlyList<string> elems)
    {
        var closed = elems.Count == 0 ? defName : defName + "_" + string.Join("_", elems);
        if (_elements.ContainsKey(closed)) return;
        _elements[closed] = elems.ToArray();
        // A nested tuple element is required transitively (its struct must be
        // DEFINED before the outer struct can hold it by value).
        foreach (var e in elems)
            if (IsValueTupleName(e)) Require(e);
    }

    // Element names of a CLOSED instance name. Registry first; greedy split
    // (trailing elements are the simple ones) as the historical fallback.
    internal static string[] ElementNames(string fullName)
    {
        if (_elements.TryGetValue(fullName, out var known)) return known;
        var arity = Arity(fullName);
        if (arity <= 0) return Array.Empty<string>();
        var rest = fullName[(DefPrefix.Length + arity.ToString(System.Globalization.CultureInfo.InvariantCulture).Length)..];
        if (rest.StartsWith("_", StringComparison.Ordinal)) rest = rest[1..];
        if (rest.Length == 0) return Array.Empty<string>();
        var parts = rest.Split('_');
        if (parts.Length == arity) return parts;
        if (parts.Length > arity)
        {
            var result = new string[arity];
            var end = parts.Length;
            for (var k = arity - 1; k >= 0; k--)
            {
                if (k == 0) { result[0] = string.Join("_", parts.Take(end)); break; }
                result[k] = parts[end - 1];
                end--;
            }
            return result;
        }
        return parts;
    }

    // The flattened element count of a ValueTuple closed name: ValueTuple`8
    // appends its TRest's elements (recursively).
    internal static int FlatLength(string fullName)
    {
        var arity = Arity(fullName);
        if (arity < 8) return arity < 0 ? 0 : arity;
        var elems = ElementNames(fullName);
        return 7 + (elems.Length == 8 ? FlatLength(elems[7]) : 0);
    }

    //--------------------------------------------------------------------------
    // Element token (as produced by the M3 generic-argument decoders) ->
    // structural shape + C++ field type.
    //--------------------------------------------------------------------------
    internal static TupleElem ResolveElem(string token, IReadOnlyList<TypeDef>? types)
    {
        switch (token)
        {
            case "bool" or "System.Boolean": return new(TupleElemShape.Bool, "int32_t", token);
            case "char" or "System.Char": return new(TupleElemShape.Char, "int32_t", token);
            case "sbyte" or "byte" or "short" or "ushort" or "int" or "uint"
                or "System.SByte" or "System.Byte" or "System.Int16" or "System.UInt16"
                or "System.Int32" or "System.UInt32":
                return new(TupleElemShape.Int32, "int32_t", token);
            case "long" or "ulong" or "System.Int64" or "System.UInt64":
                return new(TupleElemShape.Int64, "int64_t", token);
            case "double" or "System.Double": return new(TupleElemShape.Double, "double", token);
            case "float" or "System.Single": return new(TupleElemShape.Single, "double", token);
            case "System.Decimal": return new(TupleElemShape.Decimal, "il2cpp::runtime::Il2CppDecimal", token);
            case "System.DateTime": return new(TupleElemShape.DateTime, "il2cpp::runtime::Il2CppDateTime", token);
            case "System.TimeSpan": return new(TupleElemShape.TimeSpan, "il2cpp::runtime::Il2CppTimeSpan", token);
            case "System.Guid": return new(TupleElemShape.Guid, "il2cpp::Il2CppGuid", token);
        }
        if (IsValueTupleName(token))
            return new(TupleElemShape.Nested, StructName(token), token);
        // Nullable<T> is modelled as a managed reference slot (null = no value,
        // boxed T = value), the transpiler-wide GN-2 contract.
        if (token.StartsWith("System.Nullable`1", StringComparison.Ordinal))
            return new(TupleElemShape.Ref, "Il2CppObject*", token);
        if (types != null)
        {
            var def = types.FirstOrDefault(t => t.MatchesFullName(token));
            if (def != null && def.IsValueType)
                return new(TupleElemShape.UserStruct, TypeEmitter.TypeCppName(def), token);
        }
        return new(TupleElemShape.Ref, "Il2CppObject*", token);
    }

    internal static TupleElem[] ResolveElems(string fullName, IReadOnlyList<TypeDef>? types)
    {
        var names = ElementNames(fullName);
        var result = new TupleElem[names.Length];
        for (var i = 0; i < names.Length; i++) result[i] = ResolveElem(names[i], types);
        return result;
    }

    // The canonical C++ struct name -- MUST equal
    // TypeEmitter.Sanitize(closedInstanceName), which is exactly what
    // TypeEmitter.TypeCppName / CodeGenerator.TypeCppName produce for the same
    // closed instance name.
    internal static string StructName(string fullName) => TypeEmitter.Sanitize(fullName);

    internal static string ToStringFn(string fullName) => StructName(fullName) + "_ToString";
    // "ToStringEnd()": the ITupleInternal contract -- the text from the FIRST
    // rest element through the closing paren (leading ", ", trailing ')').
    internal static string ToStringInnerFn(string fullName) => StructName(fullName) + "_ToStringInner";
    internal static string GetHashCodeFn(string fullName) => StructName(fullName) + "_GetHashCode";
    internal static string EqualsFn(string fullName) => StructName(fullName) + "_Equals";
    internal static string NewFn(string fullName) => StructName(fullName) + "_New";
    internal static string CtorFn(string fullName) => StructName(fullName) + "_ctor";

    // The field name of element i (1-based): Item1..Item7 and, for arity 8,
    // "Rest" for the TRest slot (the metadata field name csc emits, and the
    // name ldfld resolves to).
    internal static string FieldName(int arity, int index1)
        => arity == 8 && index1 == 8 ? "Rest" : "Item" + index1.ToString(System.Globalization.CultureInfo.InvariantCulture);

    //--------------------------------------------------------------------------
    // M3 generic-argument decode (the SAME table MethodRefResolver.ReadM3Arg
    // uses) -- produces the element name inside a closed instance name.
    //--------------------------------------------------------------------------
    private static string ReadM3ArgName(MetadataReader md, ref BlobReader sig)
    {
        var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        switch (b)
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
            case 0x11:
            case 0x12:
                return MethodSignatureCoder.ResolveM3ArgTypeName(md, ref sig);
            case 0x1D:
                return "[" + ReadM3ArgName(md, ref sig) + "]";
            case 0x14:
            {
                var elem = ReadM3ArgName(md, ref sig);
                var rank = sig.ReadCompressedInteger();
                var sizes = sig.ReadCompressedInteger();
                for (var i = 0; i < sizes; i++) _ = sig.ReadCompressedInteger();
                var lo = sig.ReadCompressedInteger();
                for (var i = 0; i < lo; i++) _ = sig.ReadCompressedInteger();
                return "[" + elem + rank + "]";
            }
            case 0x15:
            {
                // Nested generic instantiation: definition + args.
                sig.ReadByte();
                var def = sig.ReadTypeHandle();
                var defName = def.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)def),
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)def),
                    _ => "?",
                };
                var n = sig.ReadCompressedInteger();
                var args = new string[n];
                for (var i = 0; i < n; i++) args[i] = ReadM3ArgName(md, ref sig);
                RecordElements(defName, args);
                return defName + "_" + string.Join("_", args);
            }
            case 0x13:
            case 0x1E:
                _ = sig.ReadCompressedInteger();
                return MethodRefResolver.InstantiationSuffixHint ?? "int";
            default:
                return "object";
        }
    }

    //--------------------------------------------------------------------------
    // Decode a System.ValueTuple`N TypeSpec into its canonical closed instance
    // name ("System.ValueTuple`2_int_string"), or null when the TypeSpec is
    // not a ValueTuple instantiation.
    //--------------------------------------------------------------------------
    internal static string? ClosedNameFromSpec(MetadataReader md, TypeSpecificationHandle handle)
    {
        var sig = md.GetBlobReader(md.GetTypeSpecification(handle).Signature);
        if (sig.ReadByte() != 0x15) return null;
        sig.ReadByte(); // CLASS/VALUETYPE marker
        var defHandle = sig.ReadTypeHandle();
        var defName = defHandle.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
            _ => null,
        };
        if (!IsValueTupleName(defName)) return null;
        var argc = sig.ReadCompressedInteger();
        var args = new string[argc];
        for (var i = 0; i < argc; i++) args[i] = ReadM3ArgName(md, ref sig);
        RecordElements(defName!, args);
        return defName + "_" + string.Join("_", args);
    }

    //--------------------------------------------------------------------------
    // Decode a METHOD SIGNATURE's return type when it is a ValueTuple
    // instantiation -> the canonical closed instance name; null otherwise.
    // A tuple return is a GENERICINST (0x15) return, which IsRefType marks as
    // a managed reference -- so the call-site temp would be Il2CppObject* and
    // `return <struct>;` would not compile. The caller overrides ReturnsRef and
    // sets ResultKind "vt:<name>".
    //--------------------------------------------------------------------------
    internal static string? ReturnTupleName(MetadataReader md, BlobHandle sigHandle)
    {
        try
        {
            var sig = md.GetBlobReader(sigHandle);
            var hdr = sig.ReadSignatureHeader();
            if (hdr.IsGeneric) sig.ReadCompressedInteger();
            sig.ReadCompressedInteger(); // param count
            var retByte = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
            if (retByte != 0x15) return null;
            sig.ReadByte(); // CLASS/VALUETYPE marker
            var defHandle = sig.ReadTypeHandle();
            var defName = defHandle.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
                _ => null,
            };
            if (!IsValueTupleName(defName)) return null;
            var argc = sig.ReadCompressedInteger();
            var args = new string[argc];
            for (var i = 0; i < argc; i++) args[i] = ReadM3ArgName(md, ref sig);
            RecordElements(defName!, args);
            return defName + "_" + string.Join("_", args);
        }
        catch { return null; }
    }

    //--------------------------------------------------------------------------
    // ValueTuple.Create<T1..TN> -- the closed instance name from the
    // MethodSpec's generic-argument blob.
    //--------------------------------------------------------------------------
    internal static string? ClosedNameFromArgBytes(MetadataReader md, BlobHandle specSig, int arity)
    {
        try
        {
            var sig = md.GetBlobReader(specSig);
            if (sig.ReadByte() != 0x0A) return null;
            if (sig.ReadCompressedInteger() != arity) return null;
            var args = new string[arity];
            for (var i = 0; i < arity; i++) args[i] = ReadM3ArgName(md, ref sig);
            var defName = DefPrefix + arity.ToString(System.Globalization.CultureInfo.InvariantCulture);
            RecordElements(defName, args);
            return defName + "_" + string.Join("_", args);
        }
        catch { return null; }
    }

    //--------------------------------------------------------------------------
    // Per-instantiation definitions collected during body emission and flushed
    // file-scope into generated.h (the TupleDefsNeeded pattern).
    //--------------------------------------------------------------------------
    private static readonly SortedSet<string> _needed = new(StringComparer.Ordinal);

    internal static void Require(string fullName)
    {
        if (IsValueTupleName(fullName) && fullName != ZeroName) _needed.Add(fullName);
        else if (fullName == ZeroName) _needed.Add(ZeroName);
    }

    internal static void Clear()
    {
        _needed.Clear();
        // The element registry is per-assembly (two Generate calls in one
        // process must not share stale tables), while unit tests call the
        // decoders directly -- keeping it across a Clear would only ever
        // re-add identical entries, so clearing is the safe side.
        _elements.Clear();
    }

    internal static IEnumerable<string> Needed => _needed;

    // Emit the struct + member helpers for one instantiation.
    internal static string EmitDefinitions(string fullName, IReadOnlyList<TypeDef>? types = null)
    {
        var name = StructName(fullName);
        var w = new CppWriter();
        if (fullName == ZeroName)
        {
            w.Line("// System.ValueTuple (the 0-tuple, C# `ValueTuple.Create()`).");
            w.Line($"struct {name} {{");
            w.Close();
            w.Line("};");
            w.Line();
            w.Line($"inline void {CtorFn(fullName)}({name}* self) {{ (void)self; }}");
            w.Line($"inline Il2CppObject* {ToStringFn(fullName)}(const {name}& self)");
            w.Line("{");
            w.Open();
            w.Line("(void)self;");
            w.Line("return reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_intern_literal(\"()\"));");
            w.Close();
            w.Line("}");
            w.Line($"inline Il2CppObject* {ToStringInnerFn(fullName)}(const {name}& self)");
            w.Line("{");
            w.Open();
            w.Line("(void)self;");
            w.Line("return reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_intern_literal(\")\"));");
            w.Close();
            w.Line("}");
            w.Line($"inline int32_t {GetHashCodeFn(fullName)}(const {name}& self) {{ (void)self; return 0; }}");
            w.Line($"inline int32_t {EqualsFn(fullName)}(const {name}& self, const {name}& other) {{ (void)self; (void)other; return 1; }}");
            w.Line($"inline {name} {NewFn(fullName)}() {{ {name} r {{ }}; return r; }}");
            w.Line();
            return w.ToString();
        }

        var elems = ResolveElems(fullName, types);
        if (elems.Length == 0) return "";
        var arity = elems.Length;

        w.Line($"// System.ValueTuple`{arity} instantiation (C# tuple syntax).");
        w.Line($"struct {name} {{");
        w.Open();
        for (var i = 0; i < arity; i++)
            w.Line($"{elems[i].Cpp} {FieldName(arity, i + 1)};");
        w.Close();
        w.Line("};");
        w.Line();
        EmitCtor(w, name, elems, arity);
        EmitNew(w, name, elems, arity);
        EmitToString(w, name, elems, arity);
        EmitToStringInner(w, name, elems, arity);
        EmitGetHashCode(w, name, elems, arity);
        EmitEquals(w, name, elems, arity);
        return w.ToString();
    }

    private static void EmitCtor(CppWriter w, string name, TupleElem[] elems, int arity)
    {
        // The IL ctor takes the elements in order; the transpiler emits a
        // free function taking the receiver ADDRESS (the value-type method
        // ABI) plus the elements. Field order == ctor argument order.
        var args = string.Join(", ", elems.Select((e, i) => $"{e.Cpp} arg{i}"));
        w.Line($"inline void {name}_ctor({name}* self{(args.Length > 0 ? ", " + args : "")})");
        w.Line("{");
        w.Open();
        for (var i = 0; i < arity; i++)
            w.Line($"self->{FieldName(arity, i + 1)} = arg{i};");
        w.Close();
        w.Line("}");
        w.Line();
    }

    private static void EmitNew(CppWriter w, string name, TupleElem[] elems, int arity)
    {
        // ValueTuple.Create<T..>(...) returns the tuple BY VALUE. (The old
        // route reused the ctor helper, which takes the receiver address --
        // the first ELEMENT was then bound to `self` and addressed.)
        var args = string.Join(", ", elems.Select((e, i) => $"{e.Cpp} arg{i}"));
        w.Line($"inline {name} {name}_New({args})");
        w.Line("{");
        w.Open();
        w.Line($"{name} r {{ }};");
        for (var i = 0; i < arity; i++)
            w.Line($"r.{FieldName(arity, i + 1)} = arg{i};");
        w.Line("return r;");
        w.Close();
        w.Line("}");
        w.Line();
    }

    //--------------------------------------------------------------------------
    // Element text / hash / equality expressions.
    //--------------------------------------------------------------------------
    private static string Field(int arity, int index0) => $"self.{FieldName(arity, index0 + 1)}";

    private static string ElementToString(TupleElem e, string field)
        => e.Shape switch
        {
            TupleElemShape.Int32 => $"il2cpp::runtime::icall_int32_to_string({field})",
            TupleElemShape.Int64 => $"il2cpp::runtime::icall_int64_to_string({field})",
            TupleElemShape.Double => $"il2cpp::runtime::icall_double_to_string({field})",
            TupleElemShape.Single => $"il2cpp::runtime::icall_single_to_string(static_cast<double>({field}))",
            TupleElemShape.Bool => $"il2cpp::runtime::icall_bool_to_string({field} != 0)",
            TupleElemShape.Char => $"il2cpp::runtime::icall_char_to_string({field})",
            TupleElemShape.Decimal => $"il2cpp::runtime::icall_decimal_to_string(&{field})",
            TupleElemShape.DateTime => $"reinterpret_cast<Il2CppObject*>(il2cpp::runtime::icall_datetime_to_string({field}))",
            TupleElemShape.TimeSpan => $"reinterpret_cast<Il2CppObject*>(il2cpp::runtime::icall_timespan_to_string({field}))",
            TupleElemShape.Guid => $"reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_guid_to_string({field}.b))",
            TupleElemShape.Nested => $"reinterpret_cast<Il2CppObject*>({StructName(e.Token)}_ToString({field}))",
            TupleElemShape.UserStruct => $"reinterpret_cast<Il2CppObject*>(il2cpp::runtime::il2cpp_user_struct_to_string({UserClass(e)}, const_cast<void*>(static_cast<const void*>(&{field}))))",
            _ => $"reinterpret_cast<Il2CppObject*>(il2cpp::runtime::il2cpp_tuple_ref_to_string({field}))",
        };

    private static string UserClass(TupleElem e) => $"&{e.Cpp}_class";

    private static string ElementHash(TupleElem e, string field)
        => e.Shape switch
        {
            TupleElemShape.Int32 => $"il2cpp::runtime::icall_int32_get_hash_code({field})",
            TupleElemShape.Int64 => $"il2cpp::runtime::icall_int64_get_hash_code({field})",
            TupleElemShape.Double => $"il2cpp::runtime::icall_double_get_hash_code({field})",
            TupleElemShape.Single => $"il2cpp::runtime::il2cpp_tuple_single_hash({field})",
            // Boolean.GetHashCode: true -> 1, false -> 0.
            TupleElemShape.Bool => $"static_cast<int32_t>({field} != 0 ? 1 : 0)",
            // Char.GetHashCode == the UTF-16 code unit (net48).
            TupleElemShape.Char => field,
            TupleElemShape.Decimal => $"il2cpp::runtime::icall_decimal_get_hash_code(&{field})",
            TupleElemShape.DateTime => $"il2cpp::runtime::icall_datetime_get_hash_code({field})",
            TupleElemShape.TimeSpan => $"il2cpp::runtime::icall_timespan_get_hash_code({field})",
            TupleElemShape.Guid => $"il2cpp::il2cpp_guid_get_hash_code({field}.b)",
            TupleElemShape.Nested => $"{StructName(e.Token)}_GetHashCode({field})",
            TupleElemShape.UserStruct => $"il2cpp::runtime::il2cpp_tuple_user_struct_hash({UserClass(e)}, &{field})",
            _ => $"il2cpp::runtime::il2cpp_tuple_ref_hash({field})",
        };

    private static string ElementEquals(TupleElem e, string selfField, string otherField)
        => e.Shape switch
        {
            TupleElemShape.Single => $"(static_cast<float>({selfField}) == static_cast<float>({otherField}))",
            TupleElemShape.Decimal => $"il2cpp::runtime::icall_decimal_equals(&{selfField}, &{otherField})",
            TupleElemShape.DateTime => $"il2cpp::runtime::icall_datetime_equals({selfField}, {otherField})",
            TupleElemShape.TimeSpan => $"(il2cpp::runtime::icall_timespan_get_ticks({selfField}) == il2cpp::runtime::icall_timespan_get_ticks({otherField}))",
            TupleElemShape.Guid => $"il2cpp::il2cpp_guid_equals({selfField}.b, {otherField}.b)",
            TupleElemShape.Nested => $"{StructName(e.Token)}_Equals({selfField}, {otherField}) == 1",
            TupleElemShape.UserStruct => $"il2cpp::runtime::il2cpp_tuple_user_struct_equals({UserClass(e)}, &{selfField}, &{otherField})",
            TupleElemShape.Ref => $"il2cpp::runtime::icall_object_equals({selfField}, {otherField})",
            _ => $"({selfField} == {otherField})",
        };

    private static void EmitToString(CppWriter w, string name, TupleElem[] elems, int arity)
    {
        var inner = ToStringInnerFn(name.Length > 0 ? name[..0] : name); // placeholder, unused
        _ = inner;
        w.Line($"inline Il2CppObject* {name}_ToString(const {name}& self)");
        w.Line("{");
        w.Open();
        w.Line("Il2CppObject* acc = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_intern_literal(\"(\"));");
        for (var i = 0; i < arity; i++)
        {
            if (i > 0)
                w.Line("acc = il2cpp::runtime::icall_string_concat_2(acc, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_intern_literal(\", \")));");
            w.Line($"acc = il2cpp::runtime::icall_string_concat_2(acc, reinterpret_cast<Il2CppObject*>({ElementToString(elems[i], Field(arity, i))}));");
        }
        w.Line("acc = il2cpp::runtime::icall_string_concat_2(acc, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_intern_literal(\")\")));");
        w.Line("return acc;");
        w.Close();
        w.Line("}");
        w.Line();
    }

    // ITupleInternal.ToStringEnd(): the text from the FIRST element of the rest
    // segment through the closing paren -- leading ", ", trailing ')'.
    private static void EmitToStringInner(CppWriter w, string name, TupleElem[] elems, int arity)
    {
        w.Line($"inline Il2CppObject* {name}_ToStringInner(const {name}& self)");
        w.Line("{");
        w.Open();
        w.Line("Il2CppObject* acc = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_intern_literal(\", \"));");
        for (var i = 0; i < arity; i++)
        {
            if (i > 0)
                w.Line("acc = il2cpp::runtime::icall_string_concat_2(acc, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_intern_literal(\", \")));");
            w.Line($"acc = il2cpp::runtime::icall_string_concat_2(acc, reinterpret_cast<Il2CppObject*>({ElementToString(elems[i], Field(arity, i))}));");
        }
        w.Line("acc = il2cpp::runtime::icall_string_concat_2(acc, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_intern_literal(\")\")));");
        w.Line("return acc;");
        w.Close();
        w.Line("}");
        w.Line();
    }

    private static void EmitGetHashCode(CppWriter w, string name, TupleElem[] elems, int arity)
    {
        w.Line($"inline int32_t {name}_GetHashCode(const {name}& self)");
        w.Line("{");
        w.Open();
        if (arity == 8)
        {
            // net48 ValueTuple`8.GetHashCode: with L = TRest's flattened
            // length, only the LAST 8 slots take part --
            //   L >= 8 -> TRest.GetHashCode()
            //   else   -> fold(Item_L, ..., Item7, TRest.GetHashCode())
            var restToken = elems[7].Token;
            var l = IsValueTupleName(restToken) ? FlatLength(restToken) : 1;
            if (l >= 8)
            {
                w.Line($"return {ElementHash(elems[7], Field(arity, 7))};");
            }
            else
            {
                var start = l < 1 ? 1 : l;
                w.Line($"int32_t h = {ElementHash(elems[start - 1], Field(arity, start - 1))};");
                for (var i = start; i < 7; i++)
                    w.Line($"h = il2cpp::runtime::il2cpp_tuple_combine_hash(h, {ElementHash(elems[i], Field(arity, i))});");
                w.Line($"return il2cpp::runtime::il2cpp_tuple_combine_hash(h, {ElementHash(elems[7], Field(arity, 7))});");
            }
        }
        else
        {
            w.Line($"int32_t h = {ElementHash(elems[0], Field(arity, 0))};");
            for (var i = 1; i < arity; i++)
                w.Line($"h = il2cpp::runtime::il2cpp_tuple_combine_hash(h, {ElementHash(elems[i], Field(arity, i))});");
            w.Line("return h;");
        }
        w.Close();
        w.Line("}");
        w.Line();
    }

    private static void EmitEquals(CppWriter w, string name, TupleElem[] elems, int arity)
    {
        // The strongly-typed Equals(ValueTuple`N) overload (csc picks it for
        // tuple-to-tuple comparisons). Reference elements compare through
        // icall_object_equals (string content / boxed value / identity).
        w.Line($"inline int32_t {name}_Equals(const {name}& self, const {name}& other)");
        w.Line("{");
        w.Open();
        var conds = new List<string>();
        for (var i = 0; i < arity; i++)
            conds.Add(ElementEquals(elems[i], Field(arity, i), $"other.{FieldName(arity, i + 1)}"));
        w.Line($"return {(conds.Count == 0 ? "1" : string.Join(" && ", conds))} ? 1 : 0;");
        w.Close();
        w.Line("}");
        w.Line();
    }
}
