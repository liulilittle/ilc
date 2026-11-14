// zz-loud-sentinel.cs -- W11 F1-b controlled active LOUD contract sentinel.
//
// This case MUST throw an uncaught managed NotSupportedException at RUNTIME.
// It is the fixed, always-active protection object of scripts/loud-gate.ps1:
// the gate runs scripts/difftest.ps1 on this case and requires BOTH a non-zero
// exit AND the documented "Unhandled managed exception" evidence in the
// runtime stderr (src/runtime/src/exception.cpp:213). If the throw is ever
// "fixed" into a silent return (contract lost), difftest exits 0 and the gate
// fails, so the CI chain (scripts/ci.ps1) blocks. It is registered in
// scripts/loud-contracts.txt (the controlled set) and in
// scripts/sweep-exclude.txt with a "LOUD contract" note so the byte-exact
// sweep never counts it as a FAIL (it cannot be byte-exact by construction).
using System;

namespace Cases
{
    public static class ZzLoudSentinel
    {
        public static void Main()
        {
            throw new NotSupportedException("w11-loud-sentinel: LOUD contract held (must reject)");
        }
    }
}
