namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Legacy -- LOUD rejections for the net48 audit §3/§6 legacy
// families (icall-inventory-net48.md rows 786-824 / 980: "旧技术, 建议 LOUD
// 拒绝" / "低频/legacy").
//
//   * System.Runtime.Remoting.* -- the 6 public-face RemotingServices gaps
//     (CallDefaultCtor / AllocateInitializedObject / nSetRemoteActivation-
//     Configured / Unwrap / AlwaysUnwrap / CheckCast + the RealProxy/Context/
//     Message/StackBuilderSink icalls). The runtime has NO remoting
//     infrastructure (no transparent proxies, no ObjRef, no channels, no
//     context-bound objects), so EVERY member of the family is rejected as a
//     family -- a member-level list would just misname the same wall.
//   * System.AppDomain -- the runtime has no AppDomain object; the AOT
//     process IS the default domain (CreateDomain/Unload/evidence/probing
//     are all meaningless here).
//   * System.ArgIterator -- varargs reflection (__arglist) is not in the M3
//     model.
//   * System.Threading.ReaderWriterLock -- legacy low-frequency lock with no
//     runtime icall (Monitor / the Interlocked atomics cover the need).
//
// The BclResolve wrapper prefixes every message with "{owner}::{name}: " so
// the rejection self-identifies. Without these cases the members silently
// fell to the generated-name path (undefined C++ symbol at link).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapLegacy(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            case "System.Runtime.Remoting.RemotingServices":
            case "System.Runtime.Remoting.Proxies.RealProxy":
            case "System.Runtime.Remoting.Contexts.Context":
            case "System.Runtime.Remoting.Messaging.Message":
            case "System.Runtime.Remoting.Messaging.StackBuilderSink":
            case "System.Runtime.Remoting.Channels.ChannelServices":
                throw new NotSupportedException(
                    $"{name} is part of System.Runtime.Remoting -- a legacy technology rejected per the net48 audit §3 (6 public-face gaps, '建议 LOUD 拒绝'): the runtime has no remoting infrastructure (no transparent proxies / ObjRef / channels / context-bound objects), so the member cannot be wired");
            case "System.AppDomain":
                throw new NotSupportedException(
                    $"{name} is part of System.AppDomain -- a legacy/low-frequency domain (net48 audit §3) with no runtime AppDomain object: the AOT process IS the default domain and CreateDomain/Unload/evidence probing are meaningless here");
            case "System.ArgIterator":
                throw new NotSupportedException(
                    $"{name} is part of System.ArgIterator -- a legacy varargs-reflection primitive (net48 audit §3) with no runtime support: __arglist is not in the M3 model");
            case "System.Threading.ReaderWriterLock":
                throw new NotSupportedException(
                    $"{name} is part of System.Threading.ReaderWriterLock -- a legacy low-frequency lock (net48 audit §3) with no runtime icall: use the Monitor domain (monitor.h) or the Interlocked atomics (il2cpp_interlocked.h) instead");
            default:
                return null;
        }
    }
}
