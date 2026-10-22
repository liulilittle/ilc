using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Il2Cpp.Transpiler.Model;

//------------------------------------------------------------------------------
// ValueTupleSupport -- System.ValueTuple`1..8 support for C# 7 tuple syntax.
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
//   * the element M3 names and their C++ field types;
//   * the generated struct + member helpers (file scope, inline);
//   * the generated member names the fold resolves to.
//
// ELEMENT KINDS: 'i' int32 slot, 'l' int64 slot, 'd'/'f' double slot,
// 'r' managed reference (string / object / array / user class).
// A user VALUE-type element is carried as a reference-shaped slot (documented
// deviation: its helper text/hash uses the object dispatch).
//------------------------------------------------------------------------------
namespace Il2Cpp.Transpiler.Emit;

internal static class ValueTupleSupport
{
    internal const string DefPrefix = "System.ValueTuple`";

    internal static bool IsValueTupleName(string? fullName)
        => fullName != null && fullName.StartsWith(DefPrefix, StringComparison.Ordinal);

    // The arity encoded in "System.ValueTuple`N[...]"; -1 when not a tuple.
    internal static int Arity(string fullName)
    {
        if (!IsValueTupleName(fullName)) return -1;
        var i = DefPrefix.Length;
        var digits = 0;
        while (i + digits < fullName.Length && char.IsAsciiDigit(fullName[i + digits])) digits++;
        return digits == 0 || !int.TryParse(fullName.AsSpan(i, digits), out var n) ? -1 : n;
    }

    // Element M3 names of a CLOSED instance name (the suffix after
    // "System.ValueTuple`N_"). Arity-driven; the supported element set
    // (primitives / string / object / array tokens / dotted user full names)
    // splits unambiguously.
    internal static string[] ElementNames(string fullName)
    {
        var arity = Arity(fullName);
        if (arity <= 0) return Array.Empty<string>();
        var rest = fullName[(DefPrefix.Length + arity.ToString(System.Globalization.CultureInfo.InvariantCulture).Length)..];
        if (rest.StartsWith("_", StringComparison.Ordinal)) rest = rest[1..];
        if (rest.Length == 0) return Array.Empty<string>();
        var parts = rest.Split('_');
        if (parts.Length == arity) return parts;
        if (parts.Length > arity)
        {
            // A dotted/underscored element name: re-join greedily from the
            // right (the trailing elements of a tuple are the simple ones).
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

    // Element M3 name -> slot kind character.
    internal static char KindChar(string elementName) => elementName switch
    {
        "bool" or "char" or "sbyte" or "byte" or "short" or "ushort" or "int" or "uint" => 'i',
        "long" or "ulong" => 'l',
        "double" or "float" => 'd',
        _ => 'r',
    };

    // Element M3 name -> the C++ field type inside the generated struct.
    internal static string FieldCppType(string elementName) => KindChar(elementName) switch
    {
        'i' => "int32_t",
        'l' => "int64_t",
        'd' => "double",
        _ => "Il2CppObject*",
    };

    internal static bool IsRefElement(string elementName) => KindChar(elementName) == 'r';

    // The canonical C++ struct name -- MUST equal
    // TypeEmitter.Sanitize(closedInstanceName), which is exactly what
    // TypeEmitter.TypeCppName / CodeGenerator.TypeCppName produce for the same
    // closed instance name.
    internal static string StructName(string fullName) => TypeEmitter.Sanitize(fullName);

    internal static string ToStringFn(string fullName) => StructName(fullName) + "_ToString";
    internal static string GetHashCodeFn(string fullName) => StructName(fullName) + "_GetHashCode";
    internal static string EqualsFn(string fullName) => StructName(fullName) + "_Equals";

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
            return DefPrefix + arity.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "_" + string.Join("_", args);
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
        if (IsValueTupleName(fullName)) _needed.Add(fullName);
    }

    internal static void Clear() => _needed.Clear();

    internal static IEnumerable<string> Needed => _needed;

    // Emit the struct + member helpers for one instantiation.
    internal static string EmitDefinitions(string fullName)
    {
        var elems = ElementNames(fullName);
        if (elems.Length == 0) return "";
        var name = StructName(fullName);
        var w = new CppWriter();
        w.Line($"// System.ValueTuple`{elems.Length} instantiation (C# tuple syntax).");
        w.Line($"struct {name} {{");
        w.Open();
        for (var i = 0; i < elems.Length; i++)
            w.Line($"{FieldCppType(elems[i])} Item{i + 1};");
        w.Close();
        w.Line("};");
        w.Line();
        EmitCtor(w, name, elems);
        EmitToString(w, name, elems);
        EmitGetHashCode(w, name, elems);
        EmitEquals(w, name, elems);
        return w.ToString();
    }

    private static void EmitCtor(CppWriter w, string name, string[] elems)
    {
        // The IL ctor takes the elements in order; the transpiler emits a
        // free function taking the receiver ADDRESS (the value-type method
        // ABI) plus the elements. Field order == ctor argument order.
        var args = string.Join(", ", elems.Select((e, i) => $"{FieldCppType(e)} arg{i}"));
        w.Line($"inline void {name}_ctor({name}* self{(args.Length > 0 ? ", " + args : "")})");
        w.Line("{");
        w.Open();
        for (var i = 0; i < elems.Length; i++)
            w.Line($"self->Item{i + 1} = arg{i};");
        w.Close();
        w.Line("}");
        w.Line();
    }

    private static void EmitToString(CppWriter w, string name, string[] elems)
    {
        // net48 text: "(" + elem1 + ", " + elem2 + ")".
        w.Line($"inline Il2CppObject* {name}_ToString(const {name}& self)");
        w.Line("{");
        w.Open();
        w.Line("Il2CppObject* acc = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_intern_literal(\"(\"));");
        for (var i = 0; i < elems.Length; i++)
        {
            if (i > 0)
                w.Line("acc = il2cpp::runtime::icall_string_concat_2(acc, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_intern_literal(\", \")));");
            w.Line($"acc = il2cpp::runtime::icall_string_concat_2(acc, reinterpret_cast<Il2CppObject*>({ElementToString(elems[i], i)}));");
        }
        w.Line("acc = il2cpp::runtime::icall_string_concat_2(acc, reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_intern_literal(\")\")));");
        w.Line("return acc;");
        w.Close();
        w.Line("}");
        w.Line();
    }

    private static string ElementBits(string elementName, int index)
    {
        // The helper takes the element's RAW 64-BIT SLOT BITS: an integer
        // value widens, a double needs its BIT PATTERN (a plain
        // static_cast<int64_t>(double) truncates the value).
        var item = $"self.Item{index + 1}";
        return KindChar(elementName) switch
        {
            'd' => $"il2cpp::runtime::il2cpp_tuple_double_bits({item})",
            'r' => "static_cast<int64_t>(0)",
            _ => $"static_cast<int64_t>({item})",
        };
    }

    private static string ElementToString(string elementName, int index)
    {
        var kind = KindChar(elementName);
        var value = ElementBits(elementName, index);
        var obj = kind == 'r' ? $"self.Item{index + 1}" : "nullptr";
        return $"il2cpp::runtime::il2cpp_tuple_element_to_string('{kind}', {value}, {obj})";
    }

    private static string ElementHash(string elementName, int index)
    {
        var kind = KindChar(elementName);
        var value = ElementBits(elementName, index);
        var obj = kind == 'r' ? $"self.Item{index + 1}" : "nullptr";
        return $"il2cpp::runtime::il2cpp_tuple_element_hash('{kind}', {value}, {obj})";
    }

    private static void EmitGetHashCode(CppWriter w, string name, string[] elems)
    {
        w.Line($"inline int32_t {name}_GetHashCode(const {name}& self)");
        w.Line("{");
        w.Open();
        w.Line("int32_t h = 0;");
        for (var i = 0; i < elems.Length; i++)
        {
            w.Line($"h = il2cpp::runtime::il2cpp_tuple_combine_hash(h, {ElementHash(elems[i], i)});");
        }
        w.Line("return h;");
        w.Close();
        w.Line("}");
        w.Line();
    }

    private static void EmitEquals(CppWriter w, string name, string[] elems)
    {
        // The strongly-typed Equals(ValueTuple`N) overload (csc picks it for
        // tuple-to-tuple comparisons). Reference elements compare through
        // icall_object_equals (string content / boxed value / identity).
        w.Line($"inline int32_t {name}_Equals(const {name}& self, const {name}& other)");
        w.Line("{");
        w.Open();
        var conds = new List<string>();
        for (var i = 0; i < elems.Length; i++)
        {
            conds.Add(IsRefElement(elems[i])
                ? $"il2cpp::runtime::icall_object_equals(self.Item{i + 1}, other.Item{i + 1})"
                : $"self.Item{i + 1} == other.Item{i + 1}");
        }
        w.Line($"return {(conds.Count == 0 ? "1" : string.Join(" && ", conds))} ? 1 : 0;");
        w.Close();
        w.Line("}");
        w.Line();
    }
}
