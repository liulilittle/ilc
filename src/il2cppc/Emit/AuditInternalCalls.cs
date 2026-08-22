using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Il2Cpp.Transpiler.Emit;

// InternalCall coverage auditor: enumerates every [MethodImpl(InternalCall)]
// method in a reference/core assembly and checks whether the transpiler's
// BclResolve maps it to a runtime icall. Methods with no mapping would fall
// through to a generated-name call site and fail to link -- these are the
// gaps the ILC compiler must close for .NET 4.8 / .NET Core 8-10+.
//
// Owner classification (P4 refinement): gaps whose OWNER is CLR-internal
// infrastructure (System.StubHelpers, Microsoft.Runtime, System.Runtime*Handle,
// System.Signature, System.TypedReference, System.ArgIterator, System.Mda,
// System.Variant, System.Reflection.*, System.Runtime.CompilerServices.*) are
// almost always FALSE-POSITIVE gaps -- the ILC runtime reimplements the BCL
// natively and user code never calls those internal FCall helpers. Only gaps in
// USER-FACING types (System.String, System.Math, System.Array, System.Environment,
// System.GC, System.Delegate, System.Buffer, System.Convert, System.Collections.*,
// System.Text.*, System.IO.*, ...) are real, reachable public-API gaps.
public static partial class MethodRefResolver
{
    // CLR-internal infrastructure owners whose InternalCall methods are NOT
    // reachable user-code surface (false-positive gaps).
    private static readonly HashSet<string> s_internalOwners = new HashSet<string>
    {
        "System.StubHelpers", "Microsoft.Runtime", "System.Mda", "System.Variant",
        "System.Signature", "System.TypedReference", "System.ArgIterator",
        "System.RuntimeTypeHandle", "System.RuntimeMethodHandle", "System.RuntimeFieldHandle",
        "System.ModuleHandle", "System.RuntimeType", "System.RuntimeFieldInfo",
        "System.RuntimeMethodInfo", "System.RuntimeConstructorInfo", "System.RuntimePropertyInfo",
        "System.RuntimeEventInfo", "System.RuntimeParameterInfo", "System.RuntimeAssembly",
        "System.RuntimeModule", "System.Reflection.Emit", "System.Runtime.InteropServices.WindowsRuntime",
    };

    private static bool IsInternalOwner(string owner)
    {
        if (s_internalOwners.Contains(owner)) return true;
        // namespace-prefix heuristics for internal infrastructure
        return owner.StartsWith("System.Reflection.", StringComparison.Ordinal)
            || owner.StartsWith("System.Runtime.CompilerServices.", StringComparison.Ordinal)
            || owner.StartsWith("System.Runtime.InteropServices.", StringComparison.Ordinal)
            || owner.StartsWith("System.StubHelpers.", StringComparison.Ordinal)
            || owner.StartsWith("Microsoft.Runtime.", StringComparison.Ordinal)
            // NET48 legacy families (the MethodMap.Legacy.cs design decision:
            // family-level LOUD rejection -- no remoting infrastructure exists
            // in any AOT target, and the APIs are DELETED in modern .NET).
            // Classified as internal so the audit reports them as
            // internalErr/internalGap (documented-unreachable) rather than
            // user-facing gaps.
            || owner.StartsWith("System.Runtime.Remoting.", StringComparison.Ordinal);
    }

    public static void AuditInternalCalls(string assemblyPath, TextWriter output)
    {
        using var fs = File.OpenRead(assemblyPath);
        using var pe = new PEReader(fs);
        if (!pe.HasMetadata)
        {
            output.WriteLine($"NO-METADATA\t{assemblyPath}");
            return;
        }
        var mr = pe.GetMetadataReader();
        int total = 0, mapped = 0, receiverMapped = 0, gap = 0, error = 0, internalGap = 0, internalErr = 0, mappedGeneric = 0;
        foreach (var tdH in mr.TypeDefinitions)
        {
            var td = mr.GetTypeDefinition(tdH);
            var owner = mr.GetFullName(tdH);
            if (owner == "<Module>" || owner.Length == 0) continue;
            foreach (var mh in td.GetMethods())
            {
                var m = mr.GetMethodDefinition(mh);
                // P4 refinement: only PUBLIC InternalCall methods are reachable
                // user-code surface. Internal/private FCall helpers are CLR-internal
                // and never called by user programs, so they are false-positive gaps.
                if ((m.Attributes & System.Reflection.MethodAttributes.Public) == 0)
                    continue;
                var impl = m.ImplAttributes;
                if ((impl & System.Reflection.MethodImplAttributes.InternalCall) == 0)
                    continue;
                var name = mr.GetString(m.Name);
#if DEBUG_SIG
                if (name is "GetElementSize" or "InternalGetValue" or "GetRawArrayData" or "GetThreadDeserializationTracker" or "GetMemoryInfo" or "DoubleToNumber" or "NumberToDouble" or "RhBulkMoveWithWriteBarrier" or ".ctor")
                {
                    var (sigCount, sigRet, sigPs) = RawSignature(mr, m.Signature);
                    output.WriteLine($"DBG\t{owner}.{name}\tcount={sigCount}\tret=0x{sigRet:X2}\tps={BitConverter.ToString(sigPs)}");
                }
#endif
#if DEBUG_SIG
                if (name.Contains("CompareExchange") || name.Contains("Exchange"))
                    output.WriteLine($"DBG0\t{owner}.{name}\tpublic={((m.Attributes & System.Reflection.MethodAttributes.Public) != 0)}\timpl=0x{(int)m.ImplAttributes:X4}");
#endif
                total++;
                var internalOwner = IsInternalOwner(owner);
                try
                {
                    var r1 = BclResolve(mr, owner, name, m.Signature, null, null, false, null, null);
                    if (r1 != null)
                    {
                        mapped++;
                        output.WriteLine($"OK\t{owner}.{name}\t{r1.CppName}");
                        continue;
                    }
                    var r2 = BclResolve(mr, owner, name, m.Signature, null, owner, false, null, null);
                    if (r2 != null)
                    {
                        receiverMapped++;
                        output.WriteLine($"RECV\t{owner}.{name}\t{r2.CppName}");
                        continue;
                    }
                    // Generic-method probe: the audit passes genArgs=null,
                    // but generic InternalCalls (Interlocked.Exchange<T> --
                    // MVAR 0x1E in the signature) resolve ONLY with a
                    // concrete generic argument. Probe the common encodings;
                    // any hit proves the wiring exists (the transpiler
                    // supplies the REAL genArgs at each call site).
                    var (_, probeRet, probePs) = RawSignature(mr, m.Signature);
#if DEBUG_SIG
                    if (owner.Contains("Interlocked"))
                        output.WriteLine($"DBG2\t{owner}.{name}\tprobeRet=0x{probeRet:X2}\tps={BitConverter.ToString(probePs)}");
#endif
                    if (probeRet is 0x13 or 0x1E || probePs.Any(p => p is 0x13 or 0x1E))
                    {
                        byte[][] probes = { new byte[] { 0x08 }, new byte[] { 0x0A }, new byte[] { 0x0C }, new byte[] { 0x1C }, new byte[] { 0x0E } };
                        ResolvedMethod? genericHit = null;
                        foreach (var probe in probes)
                        {
                            genericHit = BclResolve(mr, owner, name, m.Signature, probe, null, false, null, null)
                                ?? BclResolve(mr, owner, name, m.Signature, probe, owner, false, null, null);
                            if (genericHit != null) break;
                        }
                        if (genericHit != null)
                        {
                            mappedGeneric++;
                            output.WriteLine($"GEN\t{owner}.{name}\t{genericHit.CppName}");
                            continue;
                        }
                    }
                    if (internalOwner) { internalGap++; output.WriteLine($"IGAP\t{owner}.{name}"); }
                    else
                    {
#if DEBUG_SIG
                        var (_, gRet, gPs) = RawSignature(mr, m.Signature);
                        output.WriteLine($"DBG3\t{owner}.{name}\tret=0x{gRet:X2}\tps={BitConverter.ToString(gPs)}");
#endif
                        gap++; output.WriteLine($"GAP\t{owner}.{name}");
                    }
                }
                catch (Exception ex)
                {
                    if (internalOwner) { internalErr++; output.WriteLine($"IERR\t{owner}.{name}\t{ex.GetType().Name}:{ex.Message}"); }
                    else { error++; output.WriteLine($"ERR\t{owner}.{name}\t{ex.GetType().Name}:{ex.Message}"); }
                }
            }
        }
        // realGap = user-facing gaps (reachable public-API surface the ILC must close)
        output.WriteLine($"SUMMARY\ttotal={total}\tmapped={mapped}\treceiverMapped={receiverMapped}\t" +
            $"mappedGeneric={mappedGeneric}\trealGap={gap}\trealErr={error}\tinternalGap={internalGap}\tinternalErr={internalErr}\t{assemblyPath}");
    }
}
