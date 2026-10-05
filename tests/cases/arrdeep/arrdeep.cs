//------------------------------------------------------------------------------
// arrdeep.cs -- end-to-end reference case for ARRAY-METHOD DEPTH (net48
// oracle). Extends arraydeep (byte[]/int[]/string[] element ops + bounds
// catches) to the System.Array METHOD surface and index-arithmetic loops.
//
// Coverage (active, all wired):
//   * Length / GetLength(0) / GetUpperBound(0) on int[] / byte[] / string[]
//   * empty-array GetUpperBound == -1 (net48: the largest valid index)
//   * GetLength(1) on a 1-D array -> IndexOutOfRangeException (both runtimes)
//   * out-of-bounds read / write / negative index -> IndexOutOfRangeException
//   * runtime-length fast-path catch (length from a runtime call)
//   * "2-D-style" NESTED-LOOP initialization over a flat 1-D array with
//     row*cols + col index arithmetic, plus element-wise verification
//   * Array.Copy partial (5-arg -> icall_array_copy)
//   * foreach accumulation
//   * a second array whose length is computed from GetLength(0) (chained)
//
// Every line prints "label=True/False". net48 is the reference baseline.
//
// MULTIDIM ACCEPTANCE SURFACE (NOT wired -- do not uncomment until fixed):
//   * int[,] m = new int[2,3]  -- multidim newarr/local signature is a
//     transpile gap: "multidim-array/function-pointer local ... not
//     supported (complex trailing signature)" @MethodBodyReader.cs:193.
//   * m.GetLowerBound(0)       -- no case in MethodMap.Array.cs -> the
//     generated-name fallback System_Array_GetLowerBound_* is an undeclared
//     symbol (cmake_build failure). The runtime icall surface also documents
//     GetLength/GetUpperBound as "one-dimensional scope" (il2cpp_array_ext.h).
//   * m.Rank                   -- get_Rank not mapped (same fallback path).
//   * m[i,j] element access    -- depends on the multidim storage model.
//   The 1-D method surface below pins everything that IS reachable; the
//   multidim shape is the array-milestone acceptance surface.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class ArrDeep {
    public static void Main() {
        // --- Length / GetLength / GetUpperBound (int[]) ---
        int[] a = new int[4];
        Console.WriteLine("len_4=" + (a.Length == 4));
        Console.WriteLine("gllen_0=" + (a.GetLength(0) == 4));
        Console.WriteLine("ub_0=" + (a.GetUpperBound(0) == 3));
        a[0] = 10; a[1] = 20; a[2] = 30; a[3] = 40;
        Console.WriteLine("sum4=" + (a[0] + a[1] + a[2] + a[3] == 100));
        // --- empty array: GetUpperBound == -1 (net48) ---
        int[] empty = new int[0];
        Console.WriteLine("empty_len=" + (empty.Length == 0));
        Console.WriteLine("empty_ub=" + (empty.GetUpperBound(0) == -1));
        // --- GetLength(1) on 1-D -> IndexOutOfRangeException (both runtimes) ---
        bool caught = false;
        try { int x = a.GetLength(1); } catch (IndexOutOfRangeException) { caught = true; }
        Console.WriteLine("dim1_throws=" + caught);
        caught = false;
        try { int y = a.GetUpperBound(2); } catch (IndexOutOfRangeException) { caught = true; }
        Console.WriteLine("dim2_throws=" + caught);
        // --- out-of-bounds read / write / negative index ---
        caught = false;
        try { int x = a[4]; } catch (IndexOutOfRangeException) { caught = true; }
        Console.WriteLine("oob_read=" + caught);
        caught = false;
        try { a[4] = 99; } catch (IndexOutOfRangeException) { caught = true; }
        Console.WriteLine("oob_write=" + (caught && a[3] == 40));
        caught = false;
        try { int x = a[-1]; } catch (IndexOutOfRangeException) { caught = true; }
        Console.WriteLine("neg_read=" + caught);
        caught = false;
        try { a[-2] = 5; } catch (IndexOutOfRangeException) { caught = true; }
        Console.WriteLine("neg_write=" + caught);
        // --- runtime-length path (length from a method call, no constant fast path) ---
        int n = a.Length;
        int[] dyn = new int[n];
        dyn[0] = 1; dyn[1] = 2; dyn[2] = 3;
        caught = false;
        try { int y = dyn[n]; } catch (IndexOutOfRangeException) { caught = true; }
        Console.WriteLine("dyn_oob=" + caught);
        // --- 2-D-style nested-loop init over a flat 1-D array ---
        int rows = 3, cols = 4;
        int[] grid = new int[rows * cols];
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                grid[i * cols + j] = i * 10 + j;
        Console.WriteLine("grid_len=" + (grid.Length == 12));
        Console.WriteLine("grid_ub=" + (grid.GetUpperBound(0) == 11));
        Console.WriteLine("grid_00=" + (grid[0 * 4 + 0] == 0));
        Console.WriteLine("grid_12=" + (grid[1 * 4 + 2] == 12));
        Console.WriteLine("grid_23=" + (grid[2 * 4 + 3] == 23));
        int gridSum = 0;
        foreach (int v in grid) gridSum += v;
        Console.WriteLine("grid_sum=" + (gridSum == 138));               // 0..23 with row*10 stride
        int diag = 0;
        for (int i = 0; i < rows; i++) diag += grid[i * cols + i];       // 0 + 11 + 22
        Console.WriteLine("grid_diag=" + (diag == 33));
        // --- chained: second array sized from GetLength(0) ---
        int[] b = new int[a.GetLength(0)];
        b[0] = 7; b[1] = 8; b[2] = 9; b[3] = 10;
        Console.WriteLine("chain_len=" + (b.Length == a.Length));
        Console.WriteLine("chain_sum=" + (b[0] + b[3] == 17));
        // --- Array.Copy partial (5-arg -> icall_array_copy) ---
        int[] dst = new int[2];
        Array.Copy(a, 1, dst, 0, 2);
        Console.WriteLine("copy_ok=" + (dst[0] == 20 && dst[1] == 30));
        // --- byte[] method surface (net-domain T0 element width) ---
        byte[] by = new byte[3];
        by[0] = 1; by[1] = 2; by[2] = 3;
        Console.WriteLine("byte_len=" + (by.Length == 3));
        Console.WriteLine("byte_gll=" + (by.GetLength(0) == 3));
        Console.WriteLine("byte_ub=" + (by.GetUpperBound(0) == 2));
        caught = false;
        try { int x = by[3]; } catch (IndexOutOfRangeException) { caught = true; }
        Console.WriteLine("byte_oob=" + caught);
        // --- string[] method surface (reference-element array) ---
        string[] names = new string[2];
        names[0] = "ab"; names[1] = "cd";
        Console.WriteLine("str_len=" + (names.Length == 2));
        Console.WriteLine("str_gll=" + (names.GetLength(0) == 2));
        Console.WriteLine("str_ub=" + (names.GetUpperBound(0) == 1));
        Console.WriteLine("str_el=" + (names[0] == "ab" && names[1] == "cd"));
    }
}}
