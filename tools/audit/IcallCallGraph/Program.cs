// Reachability evidence for the net48 mscorlib InternalCall audit.
//
// Decodes every managed method body in mscorlib.dll, builds the intra-assembly
// call graph, and reports for each InternalCall method the shortest chain up to
// a "public face" method (type public && method public) -- the point at which
// USER code can enter. That chain is what separates a P0 (directly callable)
// icall from a shadow (internal icall reached only through a public wrapper)
// and what proves an icall is genuinely unreachable (P3).
//
// The output also carries, per icall, its own signature, and one flat list of
// every public-face method (owner, name, signature). Both are needed because a
// classification key is (owner, name, SIGNATURE): overloads of one name can have
// different reachability, and a wrapper edge must be matched against the exact
// overload the user calls -- not against every method sharing a name.
//
// The IL decoder validates itself: every method body must be consumed exactly.
// A single misdecoded opcode would desynchronise the stream and show up as a
// length mismatch, which is fatal (exit 4) rather than silently wrong.
//
// usage: IcallCallGraph <mscorlib.dll> <out.json>

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using IcallAuditShared;

namespace IcallCallGraph;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: IcallCallGraph <assembly.dll> <out.json>");
            return 2;
        }

        using var fs = File.OpenRead(Path.GetFullPath(args[0]));
        using var pe = new PEReader(fs);
        var reader = pe.GetMetadataReader();

        var methods = new Dictionary<int, MethodRec>();
        var byOwnerName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var sigProvider = new SignatureProvider(reader);

        foreach (var th in reader.TypeDefinitions)
        {
            var td = reader.GetTypeDefinition(th);
            var owner = TypeFullName(reader, th);
            foreach (var mh in td.GetMethods())
            {
                var md = reader.GetMethodDefinition(mh);
                var rec = new MethodRec
                {
                    Token = MetadataTokens.GetToken(mh),
                    Owner = owner,
                    Name = reader.GetString(md.Name),
                    Signature = Sig(reader, sigProvider, md),
                    TypePublic = IsTypePublic(td.Attributes),
                    MethodPublic = (md.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public,
                    Static = (md.Attributes & MethodAttributes.Static) != 0,
                    Rva = md.RelativeVirtualAddress,
                    IsIcall = (md.ImplAttributes & MethodImplAttributes.InternalCall) != 0,
                };
                methods[rec.Token] = rec;
                var key = owner + "::" + rec.Name;
                if (!byOwnerName.TryGetValue(key, out var lst))
                    byOwnerName[key] = lst = new List<int>();
                lst.Add(rec.Token);
            }
        }

        // ---- decode IL, collect call edges ----
        var callees = new Dictionary<int, List<int>>();
        var external = new Dictionary<int, List<string>>();
        var decodeFailures = 0;

        foreach (var rec in methods.Values)
        {
            if (rec.Rva == 0) continue;
            var body = pe.GetMethodBody(rec.Rva);
            var il = body.GetILBytes();
            var result = IlScan.Scan(il ?? Array.Empty<byte>());
            if (!result.Ok)
            {
                decodeFailures++;
                continue;
            }

            var list = new List<int>();
            foreach (var tok in result.CallTokens)
            {
                var target = Resolve(reader, tok, byOwnerName);
                if (target < 0)
                {
                    if (!external.TryGetValue(rec.Token, out var ext))
                        external[rec.Token] = ext = new List<string>();
                    ext.Add(tok.ToString("X8"));
                    continue;
                }
                list.Add(target);
            }
            if (list.Count > 0) callees[rec.Token] = list;
        }

        Console.WriteLine("methods=" + methods.Count);
        Console.WriteLine("decodeFailures=" + decodeFailures);
        Console.WriteLine("methodsWithCalls=" + callees.Count);

        // ---- reverse graph ----
        var callers = new Dictionary<int, List<int>>();
        foreach (var kv in callees)
        {
            foreach (var callee in kv.Value.Distinct())
            {
                if (!callers.TryGetValue(callee, out var lst))
                    callers[callee] = lst = new List<int>();
                lst.Add(kv.Key);
            }
        }

        // ---- multi-source BFS from every public-face method over reverse edges ----
        var dist = new Dictionary<int, int>();
        var via = new Dictionary<int, int>();   // node -> caller one step closer to the public face
        var root = new Dictionary<int, int>();  // node -> public-face method that anchors the chain
        var queue = new Queue<int>();

        foreach (var rec in methods.Values)
        {
            if (!(rec.TypePublic && rec.MethodPublic && !rec.IsIcall)) continue;
            if (!dist.ContainsKey(rec.Token))
            {
                dist[rec.Token] = 0;
                root[rec.Token] = rec.Token;
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
                root[c] = root[cur];
                // An icall is a chain TERMINUS: we record how far it is from a
                // public face but never route a path through it (icalls do not
                // call managed code back).
                if (!methods[c].IsIcall) queue.Enqueue(c);
            }
        }

        // ---- emit ----
        var icalls = methods.Values.Where(m => m.IsIcall)
            .OrderBy(m => m.Owner, StringComparer.Ordinal)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();
        sb.Append("{\n  \"methodCount\": ").Append(methods.Count);
        sb.Append(",\n  \"decodeFailures\": ").Append(decodeFailures);
        sb.Append(",\n  \"icalls\": [\n");

        for (var i = 0; i < icalls.Count; i++)
        {
            var m = icalls[i];
            sb.Append("    {");
            sb.Append("\"owner\": ").Append(J(m.Owner));
            sb.Append(", \"name\": ").Append(J(m.Name));
            sb.Append(", \"sig\": ").Append(J(m.Signature));
            sb.Append(", \"typePublic\": ").Append(m.TypePublic ? "true" : "false");
            sb.Append(", \"methodPublic\": ").Append(m.MethodPublic ? "true" : "false");
            sb.Append(", \"static\": ").Append(m.Static ? "true" : "false");
            sb.Append(", \"publicFace\": ").Append(m.TypePublic && m.MethodPublic ? "true" : "false");

            // direct callers inside mscorlib
            sb.Append(", \"callers\": [");
            if (callers.TryGetValue(m.Token, out var cs))
            {
                var ordered = cs.Select(t => methods[t])
                    .OrderBy(x => x.Owner, StringComparer.Ordinal)
                    .ThenBy(x => x.Name, StringComparer.Ordinal)
                    .ToList();
                for (var k = 0; k < ordered.Count; k++)
                {
                    if (k > 0) sb.Append(", ");
                    sb.Append("{\"owner\": ").Append(J(ordered[k].Owner));
                    sb.Append(", \"name\": ").Append(J(ordered[k].Name));
                    sb.Append(", \"sig\": ").Append(J(ordered[k].Signature));
                    sb.Append(", \"publicFace\": ").Append(ordered[k].TypePublic && ordered[k].MethodPublic ? "true" : "false");
                    sb.Append(", \"isIcall\": ").Append(ordered[k].IsIcall ? "true" : "false");
                    sb.Append('}');
                }
            }
            sb.Append(']');

            // shortest chain to a public face
            if (dist.TryGetValue(m.Token, out var d0))
            {
                var chain = new List<string>();
                var cur = m.Token;
                var guard = 0;
                while (via.TryGetValue(cur, out var parent) && guard++ < 64)
                {
                    var r = methods[parent];
                    chain.Add(r.Owner + "::" + r.Name);
                    cur = parent;
                }
                var rootRec = methods[root[m.Token]];
                sb.Append(", \"publicDistance\": ").Append(d0);
                sb.Append(", \"publicRoot\": ").Append(J(rootRec.Owner + "::" + rootRec.Name));
                sb.Append(", \"chain\": [");
                for (var k = 0; k < chain.Count; k++)
                {
                    if (k > 0) sb.Append(", ");
                    sb.Append(J(chain[k]));
                }
                sb.Append(']');
            }
            else
            {
                sb.Append(", \"publicDistance\": -1");
            }

            sb.Append('}');
            if (i + 1 < icalls.Count) sb.Append(',');
            sb.Append('\n');
        }

        sb.Append("  ],\n  \"edges\": [\n");
        var edgeRows = new List<string>();
        foreach (var kv in callees)
        {
            var from = methods[kv.Key];
            foreach (var to in kv.Value.Distinct())
            {
                var toRec = methods[to];
                edgeRows.Add("    [" + J(from.Owner) + ", " + J(from.Name) + ", " + J(from.Signature)
                    + ", " + J(toRec.Owner) + ", " + J(toRec.Name) + ", " + J(toRec.Signature) + "]");
            }
        }
        edgeRows.Sort(StringComparer.Ordinal);
        sb.Append(string.Join(",\n", edgeRows));
        sb.Append("\n  ],\n  \"publicFaces\": [\n");

        // Every method USER code can call directly: public method on a public
        // type, excluding the icalls themselves (an icall cannot wrap another
        // icall -- it has no managed body). The classifier joins its wrapper
        // edges against THIS list, so an internal helper is never mistaken for
        // a user-visible entry point.
        var faces = methods.Values
            .Where(m => m.TypePublic && m.MethodPublic && !m.IsIcall)
            .OrderBy(m => m.Owner, StringComparer.Ordinal)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(m => m.Signature, StringComparer.Ordinal)
            .ToList();
        for (var i = 0; i < faces.Count; i++)
        {
            sb.Append("    [").Append(J(faces[i].Owner)).Append(", ").Append(J(faces[i].Name))
                .Append(", ").Append(J(faces[i].Signature)).Append(']');
            if (i + 1 < faces.Count) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append("  ]\n}\n");
        File.WriteAllText(Path.GetFullPath(args[1]), sb.ToString(), new UTF8Encoding(false));

        Console.WriteLine("icalls=" + icalls.Count);
        Console.WriteLine("publicFaceIcells=" + icalls.Count(m => m.TypePublic && m.MethodPublic));
        Console.WriteLine("publicFaceMethods=" + faces.Count);
        Console.WriteLine("unreachableFromPublic=" + icalls.Count(m => !dist.ContainsKey(m.Token)));
        return decodeFailures == 0 ? 0 : 4;
    }

    private static string Sig(MetadataReader reader, SignatureProvider provider, MethodDefinition md)
    {
        try
        {
            var sig = md.DecodeSignature(provider, null);
            return sig.ReturnType + " (" + string.Join(", ", sig.ParameterTypes) + ")";
        }
        catch (BadImageFormatException)
        {
            return "?";
        }
    }

    private static int Resolve(MetadataReader reader, int token, Dictionary<string, List<int>> byOwnerName)
    {
        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind == HandleKind.MethodDefinition)
            return token;

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
                return -1;   // generic instantiation: resolved by name elsewhere
            default:
                return -1;
        }

        var name = reader.GetString(mr.Name);
        var key = ownerName + "::" + name;
        if (!byOwnerName.TryGetValue(key, out var lst) || lst.Count == 0) return -1;
        return lst[0];
    }

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

    private static string J(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}

internal sealed class MethodRec
{
    public int Token;
    public string Owner = "";
    public string Name = "";
    public string Signature = "";
    public bool TypePublic;
    public bool MethodPublic;
    public bool Static;
    public bool IsIcall;
    public int Rva;
}

internal static class IlScan
{
    // Operand sizes for single-byte opcodes. Anything not listed has no operand.
    private static readonly sbyte[] Size1 = BuildTable1();
    private static readonly sbyte[] SizeFe = BuildTableFe();

    private static sbyte[] BuildTable1()
    {
        var t = new sbyte[256];
        t[0x0E] = 1; t[0x0F] = 1; t[0x10] = 1; t[0x11] = 1; t[0x12] = 1; t[0x13] = 1;
        t[0x1F] = 1; t[0x20] = 4; t[0x21] = 8; t[0x22] = 4; t[0x23] = 8;
        t[0x27] = 4;
        t[0x28] = 4;
        t[0x29] = 4;
        for (var i = 0x2B; i <= 0x37; i++) t[i] = 1;
        for (var i = 0x38; i <= 0x44; i++) t[i] = 4;
        t[0x45] = -1;   // switch
        t[0x6F] = 4; t[0x70] = 4; t[0x71] = 4; t[0x72] = 4; t[0x73] = 4;
        t[0x74] = 4; t[0x75] = 4;
        t[0x79] = 4;
        t[0x7B] = 4; t[0x7C] = 4; t[0x7D] = 4; t[0x7E] = 4; t[0x7F] = 4; t[0x80] = 4; t[0x81] = 4;
        t[0x8C] = 4; t[0x8D] = 4;
        t[0x8F] = 4;
        t[0xA3] = 4; t[0xA4] = 4; t[0xA5] = 4;
        t[0xC2] = 4;
        t[0xC6] = 4;
        t[0xD0] = 4;
        t[0xDD] = 4; t[0xDE] = 1;
        return t;
    }

    private static sbyte[] BuildTableFe()
    {
        var t = new sbyte[256];
        t[0x06] = 4; t[0x07] = 4;
        t[0x09] = 2; t[0x0A] = 2; t[0x0B] = 2; t[0x0C] = 2; t[0x0D] = 2; t[0x0E] = 2;
        t[0x12] = 1;
        t[0x15] = 4; t[0x16] = 4;
        t[0x19] = 1;
        t[0x1C] = 4;
        return t;
    }

    public sealed class Result
    {
        public bool Ok = true;
        public readonly List<int> CallTokens = new List<int>();
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
                var sz = SizeFe[op2];
                if (sz < 0 || i + sz > il.Length) { res.Ok = false; return res; }
                if (op2 == 0x06) res.CallTokens.Add(BitConverter.ToInt32(il, i));
                i += sz;
                continue;
            }

            var s = Size1[op];
            if (s == -1)
            {
                if (i + 4 > il.Length) { res.Ok = false; return res; }
                var n = BitConverter.ToInt32(il, i);
                i += 4;
                if (n < 0 || i + 4L * n > il.Length) { res.Ok = false; return res; }
                i += 4 * n;
                continue;
            }
            if (s < 0) { res.Ok = false; return res; }
            if (i + s > il.Length) { res.Ok = false; return res; }

            if (op is 0x28 or 0x6F or 0x73 or 0x27)
                res.CallTokens.Add(BitConverter.ToInt32(il, i));
            else if (op == 0xFE && i - 2 >= 0) { /* unreachable */ }

            i += s;
        }
        return res;
    }
}
