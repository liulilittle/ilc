//------------------------------------------------------------------------------
// dtaudit1.cs -- DateTime Kind audit probe (G6 static audit D1/D2 verification).
//
// Prints the Kind value (the .NET int encoding) of three DateTimes:
//   * SpecifyKind(new DateTime(2020,1,1), Local).Kind -- net48: 2 (Local)
//   * Parse("2020-01-01").Kind                        -- net48: 0 (Unspecified)
//   * DateTime.Now.Kind                               -- net48: 2 (Local)
//
// The runtime is a single-zone UTC host: get_Kind is a constant 1 and
// SpecifyKind is the identity on ticks (time.cpp:483-493), so the runtime is
// expected to print 1 / 1 / 1. The byte-exact diff against the net48 oracle
// is the D1/D2 defect evidence (this case FAILing the diff IS the finding).
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Dtaudit1 {
    public static void Main()
    {
        Console.WriteLine("spec_local_kind=" + (int)DateTime.SpecifyKind(new DateTime(2020, 1, 1), DateTimeKind.Local).Kind);
        Console.WriteLine("parse_kind=" + (int)DateTime.Parse("2020-01-01").Kind);
        Console.WriteLine("now_kind=" + (int)DateTime.Now.Kind);
    }
}}
