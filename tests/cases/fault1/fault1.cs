//------------------------------------------------------------------------------
// fault1 -- CIL `fault` EH clause (0x04), b5-gaps section 48.
//
// C# cannot produce a fault clause (csc lowers try/finally to `finally`), but
// a fault region is REAL, statically-analyzable ECMA-335 IL that C++/CLI and
// hand-written IL emit. net48 runs it; the transpiler must too (the old reader
// LOUD-rejected the whole method).
//
// Measured net48 semantics (oracle = ilasm-built exe, verified by running it):
//   * NORMAL path (NormProc): try body runs, the fault handler is SKIPPED
//     (`np-before` then `np-after`, no `np-fault`).
//   * EXCEPTIONAL path (ThrowProc): try body runs, the fault handler RUNS
//     (`tp-before` then `tp-fault`), then the ORIGINAL exception keeps
//     unwinding to the caller's catch (`main-caught`). This is exactly a
//     finally minus its normal-path copy.
//
// The csproj builds an ordinary net48 Program.cs placeholder, then an
// AfterBuild target overwrites the emitted exe with `fault1.il` assembled by
// the .NET Framework ilasm. Both the net48 oracle and the transpiler read the
// SAME ilasm-built exe, so the fault clause is present on both sides.
//------------------------------------------------------------------------------
using System;

class Program
{
    static void Main()
    {
        Console.WriteLine("placeholder (replaced by ilasm output)");
    }
}