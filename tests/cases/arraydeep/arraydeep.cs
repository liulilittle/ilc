using System;
namespace Cases { public static class ArrayDeep {
    // Array deep chain e2e: newarr byte (byte[] allocation, net-domain T0
    // fix) + element-wise stelem.u1/ldelem.u1 + out-of-bounds
    // exception-ization (IndexOutOfRangeException catchable, not abort --
    // runtime il2cpp_array_fail + transpiler constant-length fast path
    // BOTH carry the same semantics) + negative index + Array.Copy partial
    // copy (icall_array_copy) + foreach sum + int[]/string[] mixed
    // (value vs reference element arrays). All bool probes (per e2e
    // convention); net48 is the reference baseline; expected.txt frozen
    // from the net48 reference run (all True).
    public static void Main() {
        // 1. newarr byte: byte[4] allocation (net-domain T0 fix: elemSize=1).
        byte[] b = new byte[4];
        Console.WriteLine(b.Length == 4);
        // 2. element-wise assignment / read (stelem.u1 / ldelem.u1 widths).
        b[0] = 10; b[1] = 20; b[2] = 30; b[3] = 40;
        Console.WriteLine(b[0] == 10 && b[1] == 20 && b[2] == 30 && b[3] == 40);
        // 3. out-of-bounds READ caught as IndexOutOfRangeException (constant
        //    length fast path -> il2cpp_array_fail -> managed exception).
        bool caught = false;
        try { int x = b[4]; }
        catch (IndexOutOfRangeException) { caught = true; }
        Console.WriteLine(caught);
        // 4. out-of-bounds WRITE caught as IndexOutOfRangeException.
        caught = false;
        try { b[4] = 99; }
        catch (IndexOutOfRangeException) { caught = true; }
        Console.WriteLine(caught && b[3] == 40);
        // 5. negative index caught as IndexOutOfRangeException.
        caught = false;
        try { int x = b[-1]; }
        catch (IndexOutOfRangeException) { caught = true; }
        Console.WriteLine(caught);
        // 6. checked-helper path (length from a runtime call, no constant
        //    fast path): runtime il2cpp_array_get_elem_ptr bounds check still
        //    throws catchable IndexOutOfRangeException (same il2cpp_array_fail
        //    carrier as the fast path).
        int n = b.Length;
        int[] dyn = new int[n];
        dyn[0] = 1; dyn[1] = 2; dyn[2] = 3;
        caught = false;
        try { int y = dyn[n]; }
        catch (IndexOutOfRangeException) { caught = true; }
        Console.WriteLine(caught);
        // 7. Array.Copy partial copy (5-arg overload -> icall_array_copy).
        byte[] dst = new byte[2];
        Array.Copy(b, 1, dst, 0, 2);
        Console.WriteLine(dst[0] == 20 && dst[1] == 30);
        // 8. foreach sum over byte[].
        int sum = 0;
        foreach (byte x in b) sum += x;
        Console.WriteLine(sum == 100);
        // 9. int[] value-element array.
        int[] nums = new int[3];
        nums[0] = 7; nums[1] = 8; nums[2] = 9;
        Console.WriteLine(nums[0] + nums[1] + nums[2] == 24);
        // 10. string[] reference-element array (stelem_ref / ldelem_ref).
        string[] names = new string[2];
        names[0] = "ab"; names[1] = "cd";
        Console.WriteLine(names[0] == "ab" && names[1] == "cd" && names[0].Length == 2);
    }
}}
