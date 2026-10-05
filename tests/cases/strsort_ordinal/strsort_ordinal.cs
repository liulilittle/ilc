using System;

// strsort_ordinal -- G4-adjacent probe: StringComparer.Ordinal /
// OrdinalIgnoreCase and the generic Array.Sort<T>(T[], IComparer<T>)
// overload. Expected red: the transpiler has no StringComparer static
// getter mapping (it emits the generated-name fallback
// System_StringComparer_get_Ordinal_<hash>, an undefined symbol at C++
// build) and the generic IComparer overload is not wired (the runtime has
// il2cpp_array_sort_icomparer but the emitter's generic Sort overload
// selection routes elsewhere). Deterministic: no time/random/paths.
class Prog
{
    static void Main()
    {
        string[] xs = new string[] { "b", "a", "c" };
        Array.Sort(xs, StringComparer.Ordinal);
        for (int i = 0; i < xs.Length; i++)
            Console.WriteLine("o=" + xs[i]);

        string[] ys = new string[] { "B", "a" };
        Array.Sort<string>(ys, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ys.Length; i++)
            Console.WriteLine("ic=" + ys[i]);

        Console.WriteLine("done");
    }
}
