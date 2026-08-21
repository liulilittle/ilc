using System;

namespace MainArgs
{
    // Entry-argument fixture: Main(string[] args) -- the C# entry-point
    // signature. The generated entry must construct the managed string[]
    // from the host argv (il2cpp_array_new + il2cpp_string_new_from_utf8)
    // and pass it through; a zero-arg call is an arity mismatch (recurse
    // work order). Emits argc and arg0 so a probe run can verify the
    // argv round-trip end to end.
    public static class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("argc=" + args.Length);
            if (args.Length >= 1) Console.WriteLine("arg0=" + args[0]);
        }
    }
}
