//------------------------------------------------------------------------------
// p-identity.cs -- System.Security.Principal.WindowsIdentity net48 probe
// (GN-20 MSCORLIB legacy, last icall realGap: Win32.OpenThreadToken). Purpose:
// measure what net48 actually RETURNS on a plain Windows console process so the
// ILC side can be judged byte-exact-feasible vs W7.
//
// net48 chain (measured via metadata IL): WindowsIdentity.GetCurrent() ->
// GetCurrentInternal -> GetCurrentToken -> GetCurrentThreadToken -> the audited
// internal call Win32.OpenThreadToken(TokenAccessLevels, WinSecurityContext,
// out SafeAccessTokenHandle&) with a fallback to the P/Invoke
// Microsoft.Win32.Win32Native.OpenProcessToken(GetCurrentProcess(), ...) when
// the thread has no token (ERROR_NO_TOKEN). The public WindowsIdentity surface
// is what USER code can actually reach -- GetCurrent() / get_Token /
// get_IsAuthenticated / get_Name -- so THIS probe pins those; the OpenThreadToken
// icall is the inner layer underneath GetCurrentThreadToken.
//
// OUTPUT CONTRACT (byte-exact target, net48 oracle = expected): every line is a
// host-invariant shape decision, no absolute paths / handles / clock:
//   token nonzero twice (two independent GetCurrent calls), IsAuthenticated,
//   Name non-empty, Name longer than a degenerate edge. The raw HANDLE VALUE and
//   the raw NAME string differ per process/user and are asserted by shape only.
//------------------------------------------------------------------------------
using System;
using System.Security.Principal;

namespace Cases
{
    public static class PIdentity
    {
        public static void Main()
        {
            WindowsIdentity id = WindowsIdentity.GetCurrent();
            Console.WriteLine("[tok.nonzero] " + (id.Token.ToInt64() != 0));
            Console.WriteLine("[auth] " + id.IsAuthenticated);
            Console.WriteLine("[name.nonempty] " + (!string.IsNullOrEmpty(id.Name)));
            Console.WriteLine("[name.len.gt] " + (id.Name != null && id.Name.Length > 3));

            WindowsIdentity id2 = WindowsIdentity.GetCurrent();
            Console.WriteLine("[tok2.nonzero] " + (id2.Token.ToInt64() != 0));
            Console.WriteLine("[auth2] " + id2.IsAuthenticated);
        }
    }
}