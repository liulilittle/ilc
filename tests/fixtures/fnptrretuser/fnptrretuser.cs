// fnptrretuser.cs -- unit-test fixture for the P-D3 user-method fnptr RETURN
// LOUD lock. A USER method returning a `delegate*` (ELEMENT_TYPE_FNPTR 0x1B)
// is rejected loudly by ReturnTypeCpp (no 0x1B arm -> NotSupportedException).
// P-D3 keeps this LOUD (red-test lock); wiring is a recorded follow-up.
//
// NOTE: lives in tests/fixtures (unit-only; never an e2e case -- its
// transpile is EXPECTED to fail).
using System;
namespace Cases { public static class FnPtrRetUser {
    static int Inc(int x) => x + 1;
    static unsafe delegate*<int, int> GetInc() => &Inc;
    public static unsafe void Main() {
        Console.WriteLine(GetInc()(41));
    }
}}