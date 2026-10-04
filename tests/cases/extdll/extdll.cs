using System;
using System.Runtime.InteropServices;
namespace Cases { public static class Extdll {
    // P-D4: an OUT-OF-MANIFEST library (extdll is built as a SHARED .dll that
    // the transpiled exe does NOT link). All calls resolve at RUNTIME through
    // il2cpp_pinvoke_resolve (LoadLibrary+GetProcAddress); the net48 oracle
    // does the same via the loader. EntryPoint aliases (name != method name)
    // exercise the import-name path on a SELF-BUILT library (clipops only
    // covered the windows.h system-DLL branch).
    [DllImport("extdll", EntryPoint = "extdll_add")]
    public static extern int add(int a, int b);
    [DllImport("extdll", EntryPoint = "extdll_triple")]
    public static extern int triple(int x);
    [DllImport("extdll", EntryPoint = "extdll_string_len", CharSet = CharSet.Ansi)]
    public static extern int string_len(string s);
    // Failure paths -- caught managed exceptions (net48 semantics).
    [DllImport("nosuchlib_d4")]
    public static extern int missing_lib(int x);
    [DllImport("extdll", EntryPoint = "no_such_entry_d4")]
    public static extern int missing_entry(int x);
    public static void Main() {
        Console.WriteLine("add=" + add(20, 22));      // 42
        Console.WriteLine("triple=" + triple(7));     // 21
        Console.WriteLine("string_len=" + string_len("hello")); // 5
        Console.WriteLine("add=" + add(1, 2));        // 3 (resolve cache)
        try {
            int v = missing_lib(1);
            Console.WriteLine("missing_lib NO_THROW " + v);
        }
        catch (DllNotFoundException) {
            Console.WriteLine("missing_lib DllNotFoundException");
        }
        catch (Exception ex) {
            Console.WriteLine("missing_lib OTHER " + ex.GetType().FullName);
        }
        try {
            int v = missing_entry(1);
            Console.WriteLine("missing_entry NO_THROW " + v);
        }
        catch (EntryPointNotFoundException) {
            Console.WriteLine("missing_entry EntryPointNotFoundException");
        }
        catch (Exception ex) {
            Console.WriteLine("missing_entry OTHER " + ex.GetType().FullName);
        }
        Console.WriteLine("string_len=" + string_len("abc")); // 3 (still alive)
    }
}}
