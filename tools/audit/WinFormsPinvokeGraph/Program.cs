// WinFormsPinvokeGraph -- reachability + shape evidence for the net48
// System.Windows.Forms / System.Drawing P/Invoke audit.
//
// usage: WinFormsPinvokeGraph <outDir> <rootType>=<method[,method]>::<...> <assembly.dll>...
//
// Emits into <outDir>:
//   pinvokes.tsv     every P/Invoke method (assembly, module, entry point,
//                    owner, name, charset, lastError, exactSpelling, ret,
//                    param types, param shape flags)
//   reachable.tsv    P/Invokes reachable by forward BFS from the roots, with
//                    the shortest chain back to a root
//   comimports.tsv   every ComImport type (assembly, namespace, name)
//   resources.tsv    every manifest resource (assembly, name, size)
//   roots.tsv        the resolved root methods
//
// The IL scanner validates itself (every body consumed exactly, a
// desynchronised stream is fatal) exactly like tools/audit/IcallCallGraph.

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace WinFormsPinvokeGraph;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: WinFormsPinvokeGraph <outDir> <rootSpec> <assembly.dll>...");
            Console.Error.WriteLine("  rootSpec: 'OwnerSuffix|Method1,Method2;OwnerSuffix2|Method3'");
            return 2;
        }

        var outDir = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(outDir);
        var roots = ParseRoots(args[1]);
        var asmPaths = args.Skip(2).ToList();

        var methods = new Dictionary<int, MethodRec>();
        var byOwnerName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var tokenIds = new Dictionary<(int Asm, int Token), int>();
        var asmIndex = 0;
        var pinvokes = new List<PinvokeRec>();
        var comimports = new List<string>();
        var resources = new List<string>();
        var attrCensus = new Dictionary<string, int>(StringComparer.Ordinal);
        var enumNames = new HashSet<string>(StringComparer.Ordinal);
        var comTypeNames = new HashSet<string>(StringComparer.Ordinal);
        var delegateNames = new HashSet<string>(StringComparer.Ordinal);
        var structNames = new HashSet<string>(StringComparer.Ordinal);

        // Pre-pass: enum + [ComImport] type names decide how a P/Invoke
        // signature is classified, so they must be complete before any import
        // is decoded (an enum-typed param is an int32 slot, not a struct).
        foreach (var path in asmPaths)
        {
            using var fs0 = File.OpenRead(path);
            using var pe0 = new PEReader(fs0);
            var r0 = pe0.GetMetadataReader();
            foreach (var th0 in r0.TypeDefinitions)
            {
                var td0 = r0.GetTypeDefinition(th0);
                var full0 = TypeFullName(r0, th0);
                if (IsEnum(r0, td0)) enumNames.Add(full0);
                if (IsComImport(r0, td0)) comTypeNames.Add(full0);
                if (IsDelegate(r0, td0)) delegateNames.Add(full0);
                if (IsValueTypeDecl(r0, td0)) structNames.Add(full0);
            }
        }

        foreach (var path in asmPaths)
        {
            var asmName = Path.GetFileNameWithoutExtension(path);
            var asmOrd = asmIndex++;
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            var reader = pe.GetMetadataReader();

            foreach (var th in reader.TypeDefinitions)
            {
                var td = reader.GetTypeDefinition(th);
                var owner = TypeFullName(reader, th);
                foreach (var ch in td.GetCustomAttributes())
                {
                    var nm = AttrTypeName(reader, reader.GetCustomAttribute(ch));
                    if (!nm.StartsWith("System.Runtime.InteropServices.", StringComparison.Ordinal)
                        && !nm.StartsWith("System.ComponentModel.", StringComparison.Ordinal)) continue;
                    var censusKey = asmName + "\t" + nm;
                    attrCensus[censusKey] = attrCensus.GetValueOrDefault(censusKey) + 1;
                }
                if (IsComImport(reader, td))
                {
                    comimports.Add(asmName + "\t" + NamespaceOf(reader, td) + "\t" + owner);
                }
                foreach (var mh in td.GetMethods())
                {
                    var md = reader.GetMethodDefinition(mh);
                    var localToken = MetadataTokens.GetToken(mh);
                    var globalId = methods.Count + 1;
                    var rec = new MethodRec
                    {
                        Token = globalId,
                        LocalToken = localToken,
                        AsmOrdinal = asmOrd,
                        Owner = owner,
                        Name = reader.GetString(md.Name),
                        Assembly = asmName,
                        TypePublic = IsTypePublic(td.Attributes),
                        MethodPublic = (md.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public,
                        Static = (md.Attributes & MethodAttributes.Static) != 0,
                        Rva = md.RelativeVirtualAddress,
                        IsIcall = (md.ImplAttributes & MethodImplAttributes.InternalCall) != 0,
                    };
                    var imp = md.GetImport();
                    rec.IsPinvoke = (md.Attributes & MethodAttributes.PinvokeImpl) != 0 && !imp.Module.IsNil;
                    methods[rec.Token] = rec;
                    tokenIds[(asmOrd, localToken)] = globalId;
                    try
                    {
                        var sig = md.DecodeSignature(new TypeNameProvider(reader), null);
                        MethodParamCounts[rec.Token] = sig.ParameterTypes.Length;
                    }
                    catch
                    {
                        MethodParamCounts[rec.Token] = -1;
                    }
                    var key = owner + "::" + rec.Name;
                    if (!byOwnerName.TryGetValue(key, out var lst)) byOwnerName[key] = lst = new List<int>();
                    lst.Add(rec.Token);

                    if (rec.IsPinvoke)
                    {
                        var info = DecodeImport(reader, md, imp, enumNames, comTypeNames, delegateNames, structNames);
                        pinvokes.Add(new PinvokeRec
                        {
                            Assembly = asmName,
                            Module = info.Module,
                            EntryPoint = info.EntryPoint,
                            Owner = owner,
                            Name = rec.Name,
                            CharSet = info.CharSet,
                            SetLastError = info.SetLastError,
                            ExactSpelling = info.ExactSpelling,
                            Return = info.Return,
                            Params = string.Join(";", info.Params),
                            ParamCount = info.Params.Length,
                            ParamSummary = info.ParamSummary,
                        });
                    }
                }
            }

            foreach (var rh in reader.ManifestResources)
            {
                var r = reader.GetManifestResource(rh);
                resources.Add(asmName + "\t" + reader.GetString(r.Name) + "\t" + (r.Implementation.IsNil ? "embedded" : "linked"));
            }
        }

        // ---- decode IL, collect call edges ----
        var callees = new Dictionary<int, List<int>>();
        var decodeFailures = 0;
        var callEdges = 0;
        var asmOrdinal = 0;

        foreach (var path in asmPaths)
        {
            var ord = asmOrdinal++;
            using var fs = File.OpenRead(path);
            using var pe = new PEReader(fs);
            var reader = pe.GetMetadataReader();
            foreach (var th in reader.TypeDefinitions)
            {
                var td = reader.GetTypeDefinition(th);
                foreach (var mh in td.GetMethods())
                {
                    var md = reader.GetMethodDefinition(mh);
                    if (md.RelativeVirtualAddress == 0) continue;
                    if (!tokenIds.TryGetValue((ord, MetadataTokens.GetToken(mh)), out var token)) continue;
                    MethodBodyBlock body;
                    try { body = pe.GetMethodBody(md.RelativeVirtualAddress); }
                    catch { decodeFailures++; continue; }
                    var il = body.GetILBytes();
                    var result = IlScan.Scan(il ?? Array.Empty<byte>());
                    if (!result.Ok) { decodeFailures++; continue; }
                    var list = new List<int>();
                    foreach (var tok in result.CallTokens)
                    {
                        var target = Resolve(reader, tok, byOwnerName, tokenIds, ord);
                        if (target < 0) continue;
                        list.Add(target);
                    }
                    if (list.Count > 0) { callees[token] = list; callEdges += list.Count; }
                }
            }
        }

        // ---- resolve roots ----
        var rootRows = new List<string>();
        var queue = new Queue<int>();
        var dist = new Dictionary<int, int>();
        var via = new Dictionary<int, int>();
        var rootOf = new Dictionary<int, int>();
        foreach (var kv in roots)
        {
            foreach (var rec in methods.Values)
            {
                if (!OwnerMatches(rec.Owner, kv.Key)) continue;
                if (!kv.Value.Contains(rec.Name)) continue;
                rootRows.Add(rec.Assembly + "\t" + rec.Owner + "::" + rec.Name + "\t" + rec.LocalToken.ToString("X8") + "\t" + (rec.Rva != 0 ? "il" : "nobody"));
                if (rec.Rva == 0) continue;
                if (dist.ContainsKey(rec.Token)) continue;
                dist[rec.Token] = 0;
                rootOf[rec.Token] = rec.Token;
                queue.Enqueue(rec.Token);
            }
        }

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            var d = dist[cur];
            if (!callees.TryGetValue(cur, out var cs)) continue;
            foreach (var c in cs)
            {
                if (dist.ContainsKey(c)) continue;
                dist[c] = d + 1;
                via[c] = cur;
                rootOf[c] = rootOf[cur];
                queue.Enqueue(c);
            }
        }

        // ---- emit ----
        var sb = new StringBuilder();
        sb.AppendLine("assembly\tmodule\tentrypoint\towner\tmethod\tcharset\tlastError\texactSpelling\tret\tparamCount\tparams\tflag");
        foreach (var p in pinvokes.OrderBy(p => p.Module, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(p => p.EntryPoint, StringComparer.Ordinal))
            sb.AppendLine(string.Join("\t", p.Assembly, p.Module, p.EntryPoint, p.Owner, p.Name,
                p.CharSet, p.SetLastError ? "1" : "0", p.ExactSpelling ? "1" : "0",
                p.Return, p.ParamCount.ToString(), p.Params, p.ParamSummary));
        File.WriteAllText(Path.Combine(outDir, "pinvokes.tsv"), sb.ToString(), new UTF8Encoding(false));

        sb.Clear();
        sb.AppendLine("module\tentrypoint\towner\tmethod\tdistance\troot\tchain");
        var reachable = new List<PinvokeRec>();
        foreach (var p in pinvokes)
        {
            // map the P/Invoke method to its MethodDef token through owner/name
            var key = p.Owner + "::" + p.Name;
            if (!byOwnerName.TryGetValue(key, out var toks)) continue;
            var best = -1;
            var chainRoot = "";
            var chain = "";
            foreach (var t in toks)
            {
                if (!dist.TryGetValue(t, out var d)) continue;
                if (best != -1 && d >= best) continue;
                best = d;
                var steps = new List<string>();
                var cur = t;
                var guard = 0;
                while (via.TryGetValue(cur, out var parent) && guard++ < 64)
                {
                    steps.Add(methods[parent].Owner + "::" + methods[parent].Name);
                    cur = parent;
                }
                chainRoot = methods[rootOf[t]].Owner + "::" + methods[rootOf[t]].Name;
                chain = string.Join(" <- ", steps);
            }
            if (best < 0) continue;
            reachable.Add(p);
            sb.AppendLine(string.Join("\t", p.Module, p.EntryPoint, p.Owner, p.Name, best.ToString(), chainRoot, chain));
        }
        File.WriteAllText(Path.Combine(outDir, "reachable.tsv"), sb.ToString(), new UTF8Encoding(false));

        File.WriteAllLines(Path.Combine(outDir, "delegates.tsv"), delegateNames.OrderBy(x => x, StringComparer.Ordinal), new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(outDir, "comimports.tsv"), comimports.OrderBy(x => x, StringComparer.Ordinal), new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(outDir, "resources.tsv"), resources.OrderBy(x => x, StringComparer.Ordinal), new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(outDir, "roots.tsv"), rootRows.OrderBy(x => x, StringComparer.Ordinal), new UTF8Encoding(false));

        // Attribute-name census: proves HOW many types carry each COM-related
        // attribute and that the ComImport detector is looking at real names
        // (a silently wrong detector would report comimports=0).
        sb.Clear();
        sb.AppendLine("assembly\tattribute\ttypes");
        foreach (var kv in attrCensus.OrderBy(k => k.Key, StringComparer.Ordinal))
            sb.AppendLine(kv.Key + "\t" + kv.Value);
        File.WriteAllText(Path.Combine(outDir, "attributes.tsv"), sb.ToString(), new UTF8Encoding(false));

        sb.Clear();
        sb.AppendLine("caller\tcallee");
        foreach (var kv in callees.OrderBy(k => k.Key))
        {
            var from = methods[kv.Key];
            foreach (var to in kv.Value.Distinct())
                sb.AppendLine(from.Owner + "::" + from.Name + "\t" + methods[to].Owner + "::" + methods[to].Name);
        }
        File.WriteAllText(Path.Combine(outDir, "edges.tsv"), sb.ToString(), new UTF8Encoding(false));

        Console.WriteLine("assemblies=" + asmPaths.Count);
        Console.WriteLine("methods=" + methods.Count);
        Console.WriteLine("pinvokes=" + pinvokes.Count);
        Console.WriteLine("decodeFailures=" + decodeFailures);
        Console.WriteLine("callEdges=" + callEdges);
        Console.WriteLine("roots=" + rootRows.Count);
        Console.WriteLine("reachedMethods=" + dist.Count);
        Console.WriteLine("reachablePinvokes=" + reachable.Count);
        Console.WriteLine("comimports=" + comimports.Count);
        Console.WriteLine("resources=" + resources.Count);
        // InternalCall census: the audit's "no hidden native coupling" claim
        // rests on this being 0 for both assemblies.
        Console.WriteLine("internalcalls=" + methods.Values.Count(m => m.IsIcall));
        foreach (var g in methods.Values.Where(m => m.IsIcall).GroupBy(m => m.Assembly).OrderBy(g => g.Key))
            Console.WriteLine("  internalcall " + g.Key + "=" + g.Count());
        foreach (var m in methods.Values.Where(m => m.IsIcall).OrderBy(m => m.Assembly).ThenBy(m => m.Owner))
            Console.WriteLine("  icall " + m.Assembly + " " + m.Owner + "::" + m.Name + " rva=" + m.Rva);
        return decodeFailures == 0 ? 0 : 4;
    }

    private static Dictionary<string, HashSet<string>> ParseRoots(string spec)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var group in spec.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = group.Split('|');
            if (parts.Length != 2) continue;
            map[parts[0]] = new HashSet<string>(parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        }
        return map;
    }

    private static bool OwnerMatches(string owner, string suffix) =>
        owner.Equals(suffix, StringComparison.Ordinal) || owner.EndsWith("." + suffix, StringComparison.Ordinal);

    // [ComImport] is a PSEUDO-CUSTOM attribute: the C#/IL compiler stores it in
    // the TypeDef flags (TypeAttributes.Import, 0x00001000) and emits NO
    // CustomAttribute row. Scanning custom-attribute rows therefore always
    // reports zero ComImport types (the first version of this checker did
    // exactly that -- comimports=0 on an assembly with 198 of them).
    private static bool IsEnum(MetadataReader reader, TypeDefinition td)
    {
        var bt = td.BaseType;
        if (bt.IsNil) return false;
        var nm = bt.Kind switch
        {
            HandleKind.TypeReference => TypeRefFullName(reader, (TypeReferenceHandle)bt),
            HandleKind.TypeDefinition => TypeFullName(reader, (TypeDefinitionHandle)bt),
            _ => "",
        };
        return nm is "System.Enum" or "System.ValueType" && nm == "System.Enum";
    }

    private static bool IsDelegate(MetadataReader reader, TypeDefinition td)
    {
        if (td.BaseType.IsNil) return false;
        var nm = td.BaseType.Kind switch
        {
            HandleKind.TypeReference => TypeRefFullName(reader, (TypeReferenceHandle)td.BaseType),
            HandleKind.TypeDefinition => TypeFullName(reader, (TypeDefinitionHandle)td.BaseType),
            _ => "",
        };
        return nm == "System.MulticastDelegate";
    }

    private static bool IsComImport(MetadataReader reader, TypeDefinition td) =>
        (td.Attributes & TypeAttributes.Import) != 0;

    private static string AttrTypeName(MetadataReader reader, CustomAttribute ca)
    {
        switch (ca.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var mr = reader.GetMemberReference((MemberReferenceHandle)ca.Constructor);
                if (mr.Parent.Kind == HandleKind.TypeReference)
                    return TypeRefFullName(reader, (TypeReferenceHandle)mr.Parent);
                if (mr.Parent.Kind == HandleKind.TypeDefinition)
                    return TypeFullName(reader, (TypeDefinitionHandle)mr.Parent);
                return "";
            case HandleKind.MethodDefinition:
                var md2 = reader.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor);
                return TypeFullName(reader, md2.GetDeclaringType());
            default:
                return "";
        }
    }

    private static string NamespaceOf(MetadataReader reader, TypeDefinition td)
    {
        var ns = reader.GetString(td.Namespace);
        if (!string.IsNullOrEmpty(ns)) return ns;
        var decl = td.GetDeclaringType();
        return decl.IsNil ? "" : NamespaceOf(reader, reader.GetTypeDefinition(decl));
    }

    private static ImportInfo DecodeImport(MetadataReader reader, MethodDefinition md, MethodImport imp, HashSet<string> enumNames, HashSet<string> comTypeNames, HashSet<string> delegateNames,
        HashSet<string> structNames)
    {
        var attrs = (int)imp.Attributes;
        var charSet = (attrs & 0x06) switch
        {
            0x02 => "Ansi",
            0x04 => "Unicode",
            0x06 => "Auto",
            _ => "None",
        };
        var sig = md.DecodeSignature(new TypeNameProvider(reader), null);
        var notes = new List<string>();
        foreach (var p in sig.ParameterTypes)
        {
            if (p.EndsWith("[]", StringComparison.Ordinal)) notes.Add("array");
            else if (p.EndsWith("&", StringComparison.Ordinal))
            {
                // BYREF: the emitter passes the managed slot ADDRESS. For a
                // struct that is the native pointer the callee expects (works
                // today, registered or not -- CodeGenerator.cs:9045-9051);
                // for a scalar/class/enum there is no bridge at all.
                var inner = p[..^1];
                if (enumNames.Contains(inner)) notes.Add("byref-enum:" + inner);
                else if (structNames.Contains(inner)) notes.Add("byref-vt:" + inner);
                else if (IsScalarLike(inner)) notes.Add("byref-scalar:" + inner);
                else notes.Add("byref-ref:" + inner);
            }
            else if (p == "System.String") notes.Add("string");
            else if (p == "System.IntPtr" || p == "System.UIntPtr") notes.Add("intptr");
            else if (p.Contains('*')) notes.Add("ptr");
            else if (p.StartsWith("fnptr", StringComparison.Ordinal)) notes.Add("fnptr");
            else if (enumNames.Contains(p)) notes.Add("enum:" + p);
            else if (p == "System.Text.StringBuilder") notes.Add("sb");
            else if (p == "System.Runtime.InteropServices.HandleRef") notes.Add("handleref");
            else if (p == "System.Object") notes.Add("object");
            else if (comTypeNames.Contains(p) || IsComInterface(p)) notes.Add("com:" + p);
            else if (delegateNames.Contains(p)) notes.Add("delegate:" + p);
            else if (IsStructLike(p)) notes.Add("vt:" + p);
        }
        if (enumNames.Contains(sig.ReturnType)) notes.Add("ret-enum:" + sig.ReturnType);
        else if (IsStructLike(sig.ReturnType)) notes.Add("ret-vt:" + sig.ReturnType);
        return new ImportInfo
        {
            Module = reader.GetString(reader.GetModuleReference(imp.Module).Name),
            EntryPoint = reader.GetString(imp.Name),
            CharSet = charSet,
            SetLastError = (attrs & 0x40) != 0,
            ExactSpelling = (attrs & 0x01) != 0,
            Return = sig.ReturnType,
            Params = sig.ParameterTypes.ToArray(),
            ParamSummary = string.Join(",", notes.Distinct()),
        };
    }

    private static readonly HashSet<string> ScalarNames = new(StringComparer.Ordinal)
    {
        "System.Int32", "System.UInt32", "System.Int16", "System.UInt16", "System.Int64",
        "System.UInt64", "System.Single", "System.Double", "System.Boolean", "System.Char",
        "System.Byte", "System.SByte", "System.IntPtr", "System.UIntPtr",
        // ISignatureTypeProvider.GetPrimitiveType renders CLR keywords, so the
        // by-ref inner name of a primitive arrives as "int"/"float"/... -- both
        // spellings must classify as scalar.
        "int", "uint", "short", "ushort", "long", "ulong", "float", "double",
        "bool", "char", "byte", "sbyte", "System.Int32", "System.Single",
    };

    private static bool IsScalarLike(string t) => ScalarNames.Contains(t);

    private static bool IsValueTypeDecl(MetadataReader reader, TypeDefinition td)
    {
        if (td.BaseType.IsNil) return false;
        var nm = td.BaseType.Kind switch
        {
            HandleKind.TypeReference => TypeRefFullName(reader, (TypeReferenceHandle)td.BaseType),
            HandleKind.TypeDefinition => TypeFullName(reader, (TypeDefinitionHandle)td.BaseType),
            _ => "",
        };
        return nm == "System.ValueType";
    }

    private static bool IsComInterface(string t) =>
        t.StartsWith("System.Runtime.InteropServices.ComTypes.", StringComparison.Ordinal);

    private static bool IsStructLike(string t) =>
        t switch
        {
            "System.String" or "System.Object" or "System.IntPtr" or "System.UIntPtr" => false,
            "void" or "bool" or "char" or "sbyte" or "byte" or "short" or "ushort" or "int" or "uint"
                or "long" or "ulong" or "float" or "double" or "string" => false,
            _ => !t.EndsWith("[]", StringComparison.Ordinal) && !t.EndsWith("&", StringComparison.Ordinal)
                 && !t.Contains('*') && !t.StartsWith("fnptr", StringComparison.Ordinal),
        };

    private static int Resolve(MetadataReader reader, int token, Dictionary<string, List<int>> byOwnerName,
        Dictionary<(int Asm, int Token), int> tokenIds, int asmOrd)
    {
        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind == HandleKind.MethodDefinition)
            return tokenIds.TryGetValue((asmOrd, token), out var gid) ? gid : -1;
        if (handle.Kind != HandleKind.MemberReference) return -1;
        var mr = reader.GetMemberReference((MemberReferenceHandle)handle);
        string ownerName;
        switch (mr.Parent.Kind)
        {
            case HandleKind.TypeReference:
                ownerName = TypeRefFullName(reader, (TypeReferenceHandle)mr.Parent);
                break;
            case HandleKind.TypeDefinition:
                ownerName = TypeFullName(reader, (TypeDefinitionHandle)mr.Parent);
                break;
            case HandleKind.TypeSpecification:
                var ts = reader.GetTypeSpecification((TypeSpecificationHandle)mr.Parent);
                ownerName = ts.DecodeSignature(new TypeNameProvider(reader), null);
                if (ownerName.Contains('<')) ownerName = ownerName[..ownerName.IndexOf('<')];
                break;
            default:
                return -1;
        }

        var name = reader.GetString(mr.Name);
        var key = ownerName + "::" + name;
        if (!byOwnerName.TryGetValue(key, out var lst) || lst.Count == 0) return -1;
        if (lst.Count == 1) return lst[0];

        // Disambiguate overloads by the MemberRef parameter count when possible.
        var expected = -1;
        try { expected = mr.DecodeMethodSignature(new TypeNameProvider(reader), null).ParameterTypes.Length; }
        catch { expected = -1; }
        if (expected >= 0)
        {
            foreach (var t in lst)
            {
                var pc = MethodParamCounts.GetValueOrDefault(t, -1);
                if (pc == expected) return t;
            }
        }
        return lst[0];
    }

    public static readonly Dictionary<int, int> MethodParamCounts = new();

    private static bool IsTypePublic(TypeAttributes a) => (a & TypeAttributes.VisibilityMask) switch
    {
        TypeAttributes.Public or TypeAttributes.NestedPublic or TypeAttributes.NestedFamily
            or TypeAttributes.NestedFamORAssem => true,
        _ => false,
    };

    private static string TypeFullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var def = reader.GetTypeDefinition(handle);
        var name = reader.GetString(def.Name);
        var declaring = def.GetDeclaringType();
        if (!declaring.IsNil) return TypeFullName(reader, declaring) + "+" + name;
        var ns = reader.GetString(def.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string TypeRefFullName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var tr = reader.GetTypeReference(handle);
        var name = reader.GetString(tr.Name);
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
            return TypeRefFullName(reader, (TypeReferenceHandle)tr.ResolutionScope) + "+" + name;
        var ns = reader.GetString(tr.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}

internal sealed class ImportInfo
{
    public string Module = "";
    public string EntryPoint = "";
    public string CharSet = "None";
    public bool SetLastError;
    public bool ExactSpelling;
    public string Return = "void";
    public string[] Params = Array.Empty<string>();
    public string ParamSummary = "";
}

internal sealed class PinvokeRec
{
    public string Assembly = "";
    public string Module = "";
    public string EntryPoint = "";
    public string Owner = "";
    public string Name = "";
    public string CharSet = "None";
    public bool SetLastError;
    public bool ExactSpelling;
    public string Return = "void";
    public int ParamCount;
    public string Params = "";
    public string ParamSummary = "";
}

internal sealed class MethodRec
{
    public int Token;
    public int LocalToken;
    public int AsmOrdinal;
    public string Owner = "";
    public string Name = "";
    public string Assembly = "";
    public bool TypePublic;
    public bool MethodPublic;
    public bool Static;
    public bool IsIcall;
    public bool IsPinvoke;
    public int Rva;
}

internal sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
{
    private readonly MetadataReader _reader;
    public TypeNameProvider(MetadataReader reader) => _reader = reader;

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.SByte => "sbyte",
        PrimitiveTypeCode.Byte => "byte",
        PrimitiveTypeCode.Int16 => "short",
        PrimitiveTypeCode.UInt16 => "ushort",
        PrimitiveTypeCode.Int32 => "int",
        PrimitiveTypeCode.UInt32 => "uint",
        PrimitiveTypeCode.Int64 => "long",
        PrimitiveTypeCode.UInt64 => "ulong",
        PrimitiveTypeCode.Single => "float",
        PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.String => "System.String",
        PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.IntPtr => "System.IntPtr",
        PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
        PrimitiveTypeCode.TypedReference => "typedref",
        _ => typeCode.ToString(),
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var def = reader.GetTypeDefinition(handle);
        var name = reader.GetString(def.Name);
        var decl = def.GetDeclaringType();
        if (!decl.IsNil) return GetTypeFromDefinition(reader, decl, rawTypeKind) + "+" + name;
        var ns = reader.GetString(def.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var tr = reader.GetTypeReference(handle);
        var name = reader.GetString(tr.Name);
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
            return GetTypeFromReference(reader, (TypeReferenceHandle)tr.ResolutionScope, rawTypeKind) + "+" + name;
        var ns = reader.GetString(tr.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        => genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetFunctionPointerType(MethodSignature<string> signature)
        => "fnptr " + signature.ReturnType + "(" + string.Join(",", signature.ParameterTypes) + ")";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
}

internal static class IlScan
{
    // Operand widths + call-edge opcodes come from the GENERATED table
    // (IlOpcodeTable.cs), which tools/audit/gen_il_opcode_table.py derives
    // from the CLR master opcode definition. A hand-maintained table here
    // desynchronises the IL stream on any operand-width gap or typo and then
    // fabricates call edges out of unrelated operand bytes (the first version
    // of this scanner did exactly that: every opcode missing from the hand
    // table defaulted to width 0, so the scanner walked into operand bytes,
    // and Control::WaitForWaitHandle appeared to call
    // System.Drawing.ToolboxBitmapAttribute::GetOriginalBitmap).

    public sealed class Result
    {
        public bool Ok = true;
        public readonly List<int> CallTokens = new();
    }

    public static Result Scan(byte[] il)
    {
        var res = new Result();
        var i = 0;
        while (i < il.Length)
        {
            var op = il[i++];
            if (op == 0xFE)
            {
                if (i >= il.Length) { res.Ok = false; return res; }
                var op2 = il[i++];
                var sz2 = IlOpcodeTable.SizeFe[op2];
                if (sz2 == -1 || i + sz2 > il.Length) { res.Ok = false; return res; }
                if (IlOpcodeTable.CallFe[op2]) res.CallTokens.Add(BitConverter.ToInt32(il, i));
                i += sz2;
                continue;
            }

            var s = IlOpcodeTable.Size1[op];
            if (s == -1)
            {
                // InlineSwitch: int32 count then count*int32 deltas.
                if (i + 4 > il.Length) { res.Ok = false; return res; }
                var n = BitConverter.ToInt32(il, i);
                i += 4;
                if (n < 0 || i + 4L * n > il.Length) { res.Ok = false; return res; }
                i += 4 * n;
                continue;
            }
            if (i + s > il.Length) { res.Ok = false; return res; }
            if (IlOpcodeTable.Call1[op]) res.CallTokens.Add(BitConverter.ToInt32(il, i));
            i += s;
        }
        res.Ok = i == il.Length;
        return res;
    }
}
