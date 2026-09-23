//------------------------------------------------------------------------------
// raudit3.cs -- REFLECTION AUDIT PROBE 3 (G6 static audit, suspicious #7/#9):
// GetField visibility filtering + Type.GetType assembly-qualified name.
//
// Probes:
//   * typeof(D).GetField("hidden") == null -> net48: True (private field is
//     not visible under the default BindingFlags.Public | Instance | Static).
//     Static audit (typeinfo.cpp FindMember): the scan matches by name only
//     -- no visibility/static filtering -> suspected False (private hit).
//   * typeof(D).GetField("shown") != null -> both sides expected True.
//   * Type.GetType("System.Int32, mscorlib").FullName -> net48: "System.Int32"
//     (canonical name; the assembly qualifier is stripped by the parser).
//     Static audit (reflect_base.cpp:248-255): the runtime returns the INPUT
//     string itself -> suspected "System.Int32, mscorlib" if FullName emits;
//     the descriptor-ABI of get_FullName may instead fail the C++ build.
//
// Every line prints "label=<value>", byte-stable across console encodings.
//------------------------------------------------------------------------------
using System;

namespace Cases {
class D { private int hidden; public int shown; }

public static class Raudit3 {
    public static void Main()
    {
        Console.WriteLine("gf_hidden_null=" + (typeof(D).GetField("hidden") == null));
        Console.WriteLine("gf_shown_nonnull=" + (typeof(D).GetField("shown") != null));
        Console.WriteLine("gt_fqn=" + Type.GetType("System.Int32, mscorlib").FullName);
    }
}}
