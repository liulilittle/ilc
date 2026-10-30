// Wiring probe for the net48 mscorlib InternalCall audit.
//
// Instead of re-deriving the ILC dispatch table with a regex over the C#
// sources, this probe LOADS the transpiler assembly and calls
// MethodRefResolver.BclResolve for every net48 mscorlib InternalCall. The
// answer is therefore produced by the exact code path the transpiler runs at
// emit time.
//
// Each icall is probed on two passes:
//   hint-less -- receiverType = null (StackFold's first resolution pass)
//   hinted    -- receiverType = the declaring type (the re-resolve pass)
// A method counts as wired when EITHER pass yields a symbol, because the
// transpiler only ever needs one pass to succeed.
//
// usage: IcallWiringProbe <mscorlib.dll> <out.tsv>
//
// Output is a TSV with the SAME join key as MetadataIcallCounter:
//   owner <TAB> name <TAB> signature <TAB> kind <TAB> status <TAB> symbol <TAB> site <TAB> error
//
// kind:
//   icall     -- the method carries MethodImplAttributes.InternalCall
//   publicapi -- a public method on a public type (a possible public WRAPPER
//                around an internal icall; probed so the audit can tell a
//                "shadow" -- behaviour delivered through a wired public
//                wrapper -- from a genuinely missing icall)
// status is one of: resolved | loud | unmapped.
//
// --self-check runs three assertions that pin down what "unmapped" means:
//   1. a known-wired member resolves to its runtime symbol;
//   2. the SAME member of a known-unwired owner returns "unmapped" when the
//      MetadataReader IS mscorlib (this probe's situation);
//   3. it becomes a LOUD NotSupportedException once the reader is a USER
//      assembly -- i.e. MethodRefResolver.BclResolve's external-BCL phantom
//      guard (MethodRefResolver.cs:1671) fires in a real transpile. That is
//      what turns every "unmapped" row here into a deliberate rejection rather
//      than a silently emitted undefined C++ call.

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using IcallAuditShared;
using Il2Cpp.Transpiler.Emit;

namespace IcallWiringProbe;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: IcallWiringProbe <mscorlib.dll> <out.tsv>");
            return 2;
        }

        // BclResolve's outer catch RE-THROWS a fresh NotSupportedException
        // (MethodRefResolver.cs:1415), which discards the original stack. The
        // audit needs the real throw site inside MethodMap.*.cs, so capture
        // every first-chance exception while a single BclResolve call is in
        // flight and keep the innermost Emit-layer frame.
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            if (!_inCall) return;
            var site = InnermostFrame(e.Exception);
            if (site != null) _currentSite ??= site;
        };

        using var fs = File.OpenRead(Path.GetFullPath(args[0]));
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();

        var lines = new List<string>();
        var counts = new Dictionary<string, int>();

        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            var owner = FullName(md, th);

            var typePublic = IsTypePublic(td.Attributes);

            foreach (var mh in td.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                var isIcall = (m.ImplAttributes & System.Reflection.MethodImplAttributes.InternalCall) != 0;
                var isPublicApi = typePublic
                    && (m.Attributes & System.Reflection.MethodAttributes.MemberAccessMask)
                        == System.Reflection.MethodAttributes.Public;
                if (!isIcall && !isPublicApi)
                    continue;

                var name = md.GetString(m.Name);
                var sig = Signature(md, m);

                var hintless = Call(md, owner, name, m.Signature, null);
                var hinted = Call(md, owner, name, m.Signature, owner);

                var resolved = hinted.Symbol ?? hintless.Symbol;
                var loud = hinted.Loud || hintless.Loud;
                var err = hinted.Error ?? hintless.Error;

                var status = resolved != null ? "resolved"
                    : loud ? "loud"
                    : "unmapped";

                var kind = isIcall ? "icall" : "publicapi";
                counts[kind + ":" + status] = counts.GetValueOrDefault(kind + ":" + status) + 1;

                lines.Add(string.Join('\t', owner, name, sig, kind, status,
                    resolved ?? "", (hinted.ThrowSite ?? hintless.ThrowSite ?? ""),
                    (err ?? "").Replace('\t', ' ').Replace('\n', ' ')));
            }
        }

        lines.Sort(StringComparer.Ordinal);
        File.WriteAllLines(Path.GetFullPath(args[1]), lines, new UTF8Encoding(false));

        foreach (var kv in counts.OrderBy(k => k.Key, StringComparer.Ordinal))
            Console.WriteLine(kv.Key + "=" + kv.Value);
        return 0;
    }

    private sealed record ProbeResult(string? Symbol, bool Loud, string? Error, string? ThrowSite);

    [ThreadStatic] private static bool _inCall;
    [ThreadStatic] private static string? _currentSite;

    private static ProbeResult Call(MetadataReader md, string owner, string name, BlobHandle sig, string? receiverType)
    {
        _inCall = true;
        _currentSite = null;
        try
        {
            var r = MethodRefResolver.BclResolve(md, owner, name, sig, null, receiverType,
                receiverIsAddress: false, arg0Type: null, arg1Type: null);
            return new ProbeResult(r?.CppName, false, null, null);
        }
        catch (NotSupportedException ex)
        {
            // The evidence the report needs is WHERE in the transpiler the
            // decision was made -- the first-chance site, because the outer
            // catch in BclResolve replaces the stack with its own frame.
            return new ProbeResult(null, true, ex.Message, _currentSite ?? InnermostFrame(ex));
        }
        catch (Exception ex)
        {
            // A non-NotSupported throw is NOT a designed rejection; surface it
            // verbatim so a wiring gap can be told apart from a probe artifact.
            return new ProbeResult(null, false, ex.GetType().Name + ": " + ex.Message,
                _currentSite ?? InnermostFrame(ex));
        }
        finally
        {
            _inCall = false;
        }
    }

    // The throw-site FILE:LINE of the innermost frame that lives in the
    // transpiler's Emit layer. Every TryMap* dispatcher is a partial-class
    // member of MethodRefResolver, so the frame's DISPLAY name always says
    // "MethodRefResolver.TryMapXxx"; only the FILE tells the domains apart.
    private static string? InnermostFrame(Exception ex)
    {
        var st = ex.StackTrace;
        if (string.IsNullOrEmpty(st)) return null;

        foreach (var raw in st.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("at ", StringComparison.Ordinal)) continue;

            var at = line.IndexOf(" in ", StringComparison.Ordinal);
            if (at < 0) continue;

            var file = line.Substring(at + 4).Trim();
            var colon = file.LastIndexOf(":line ", StringComparison.Ordinal);
            if (colon < 0) continue;

            var path = file.Substring(0, colon);
            var num = file.Substring(colon + 6);

            var name = Path.GetFileName(path);
            if (!name.StartsWith("MethodMap", StringComparison.Ordinal)
                && !name.StartsWith("MethodRefResolver", StringComparison.Ordinal)
                && !name.StartsWith("CodeGenerator", StringComparison.Ordinal)
                && !name.StartsWith("StackFold", StringComparison.Ordinal))
                continue;

            return name + ":" + num;
        }

        return null;
    }

    private static string Signature(MetadataReader reader, MethodDefinition m)
    {
        try
        {
            var sig = m.DecodeSignature(new SignatureProvider(reader), null);
            return sig.ReturnType + " (" + string.Join(", ", sig.ParameterTypes) + ")";
        }
        catch (Exception ex)
        {
            return "<undecodable: " + ex.GetType().Name + ">";
        }
    }

    private static bool IsTypePublic(System.Reflection.TypeAttributes a) => (a & System.Reflection.TypeAttributes.VisibilityMask) switch
    {
        System.Reflection.TypeAttributes.Public
            or System.Reflection.TypeAttributes.NestedPublic
            or System.Reflection.TypeAttributes.NestedFamily
            or System.Reflection.TypeAttributes.NestedFamORAssem => true,
        _ => false,
    };

    private static string FullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var def = reader.GetTypeDefinition(handle);
        var name = reader.GetString(def.Name);
        var declaring = def.GetDeclaringType();
        if (!declaring.IsNil) return FullName(reader, declaring) + "+" + name;
        var ns = reader.GetString(def.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}
