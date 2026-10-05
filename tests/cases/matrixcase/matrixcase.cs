//------------------------------------------------------------------------------
// matrixcase.cs -- multidimensional-array PROBE (tests/ cases dir).
//
// Purpose: locate EVERY exact rejection point of multidim arrays in the
// transpiler, one probe at a time. This file is a PROBE, not a passing
// case -- it FAILS to transpile by design. Results are recorded in
// docs/audit/multidim-probe.md (construction order for multidim support).
//
//   Probe A: Control1D -- 1D arrays must still work (control, PASS).
//   Probe B: ProbeMultidimNew -- `int[,] m = new int[2,3]` (C# lowers this
//            to newobj of the int32[,] ctor, NOT newarr; Roslyn ELIDES the
//            local when the array stays on the eval stack -> the newobj
//            wall fires directly).
//   Probe C: ProbeMultidimIndex -- `m[1,2]` read+write (Get/Set calls;
//            unreachable -- Probe B's newobj wall fires first in metadata
//            order).
//   Probe D: ProbeMultidimParam -- multidim as PARAMETER (load-time M2
//            signature-decode wall).
//   Probe E: ProbeMultidimLocal -- FORCED multidim local (a loop forces
//            ldloc/stloc; Roslyn elides the local otherwise -> the
//            MethodBodyReader local wall).
//
// Run protocol: enable ONE multidim probe method per run (metadata order
// matters -- the FIRST failing method kills the transpile), rebuild (VERIFY
// the build succeeded -- a silent compile error leaves a stale exe that
// re-tests the previous probe), run the current il2cppc.dll, record
// file:line:message.
//
// Verified wall map (2026-08-06 02:1x, il2cppc.dll 2026-08-05 23:59:28):
//   B: StackFold.cs:2170   (GenericInstantiationName, TypeSpec 0x14 != 0x15)
//   D: MethodSignatureCoder.cs:103 (EncodeType, M2 param type 0x14)
//   E: MethodBodyReader.cs:193     (LocalTypeKind, local type 0x14/0x19)
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class MatrixCase {
    public static void Main() {
        Control1D();
    }

    // --- Probe A: control -- 1D arrays must keep working ---
    static void Control1D() {
        int[] a = new int[3];
        for (int i = 0; i < 3; i++) a[i] = (i + 1) * 10;
        Console.WriteLine("1d ok");
        Console.WriteLine(a.Length);  // 3
        Console.WriteLine(a[2]);      // 30
    }

    // --- Probe B: multidim allocation `int[,] m = new int[2,3]` ---
    //static void ProbeMultidimNew() {
    //    int[,] m = new int[2, 3];
    //    Console.WriteLine(m.Length);  // 6
    //}

    // --- Probe C: multidim indexer read+write `m[1,2]` ---
    //static void ProbeMultidimIndex() {
    //    int[,] m = new int[2, 3];
    //    m[0, 0] = 1; m[1, 2] = 7;
    //    Console.WriteLine(m[1, 2]);   // 7
    //}

    // --- Probe D: multidim as PARAMETER -- reaches the Get/Set index path
    //     (no newobj in this body; the array arrives pre-built) ---
    //static void ProbeMultidimParam(int[,] m) {
    //    Console.WriteLine(m[1, 2]);   // 7
    //}

    // --- Probe E: FORCED multidim local (loop forces ldloc/stloc; Roslyn
    //     elides the local when the array stays on the eval stack) ---
    //static void ProbeMultidimLocal() {
    //    int[,] m = new int[2, 3];
    //    for (int i = 0; i < 2; i++) m[i, 0] = i;
    //    Console.WriteLine(m[1, 0]);   // 1
    //}
}}
