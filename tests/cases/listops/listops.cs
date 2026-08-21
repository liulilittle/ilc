//------------------------------------------------------------------------------
// listops.cs -- end-to-end reference case for System.Collections.ArrayList
// object-model depth (net48 oracle). No prior e2e case covers the arraylist.h
// domain (collchain exercises ArrayList via a Remove that is LOUD-unwired;
// this case stays on the fully wired surface).
//
// Coverage (active, all wired -- MethodMap.ArrayList.cs + arraylist.cpp):
//   * parameterless ctor + get_Count
//   * Add (boxed value elements) and indexed get_Item reads (unboxed back)
//   * set_Item overwrite (index + boxed value)
//   * RemoveAt(index) with the index from IndexOf(value)
//   * IndexOf present/missing (returns -1)
//   * Contains present/missing
//   * growth beyond the initial capacity (Add beyond the first resize)
//   * Clear resets the list
//   * reference (string) elements: Add / get_Item / set_Item / Contains
//   * interleaved Add/IndexOf/RemoveAt + accumulation loop over get_Item
//
// Every line prints "label=True/False". net48 is the reference baseline.
//
// ACCEPTANCE SURFACE (NOT wired -- do not uncomment until fixed):
//   * Remove(object)        -- LOUD: runtime has remove_at(index) only
//     (MethodMap.ArrayList.cs:58); the wired shape is RemoveAt(IndexOf(v)).
//   * capacity ctors        -- LOUD: parameterless create icall only.
//   * ToArray               -- runtime icall exists but unmapped
//     (generated-name fallback).
//   * storing the Add result -- Add returns void in the runtime (the managed
//     int return is dropped); `int n = list.Add(x)` fails loud at C++.
//------------------------------------------------------------------------------
using System;
using System.Collections;

namespace Cases { public static class ListOps {
    public static void Main() {
        // --- basics ---
        ArrayList a = new ArrayList();
        Console.WriteLine("init_cnt=" + (a.Count == 0));
        a.Add(10);
        a.Add(20);
        a.Add(30);
        Console.WriteLine("cnt3=" + (a.Count == 3));
        Console.WriteLine("item0=" + ((int)a[0] == 10));
        Console.WriteLine("item2=" + ((int)a[2] == 30));
        // --- set_Item overwrite ---
        a[1] = 25;
        Console.WriteLine("set_item=" + ((int)a[1] == 25));
        Console.WriteLine("cnt_after_set=" + (a.Count == 3));
        // --- RemoveAt + IndexOf ---
        a.RemoveAt(1);
        Console.WriteLine("cnt2=" + (a.Count == 2));
        Console.WriteLine("removed=" + ((int)a[1] == 30));
        Console.WriteLine("indexof=" + (a.IndexOf(30) == 1));
        Console.WriteLine("indexof_missing=" + (a.IndexOf(99) == -1));
        Console.WriteLine("contains=" + (a.Contains(10)));
        Console.WriteLine("contains_neg=" + (a.Contains(99) == false));
        // --- growth beyond initial capacity ---
        a.Add(40);
        a.Add(50);
        Console.WriteLine("cnt4=" + (a.Count == 4));
        Console.WriteLine("grown=" + ((int)a[3] == 50));
        Console.WriteLine("grown_head=" + ((int)a[0] == 10));
        // --- Clear ---
        a.Clear();
        Console.WriteLine("clear=" + (a.Count == 0));
        // --- reference (string) elements ---
        ArrayList strs = new ArrayList();
        strs.Add("x");
        strs.Add("y");
        Console.WriteLine("str_cnt=" + (strs.Count == 2));
        Console.WriteLine("str_item=" + ((string)strs[1] == "y"));
        strs[0] = "z";
        Console.WriteLine("str_set=" + ((string)strs[0] == "z"));
        Console.WriteLine("str_contains=" + (strs.Contains("z")));
        Console.WriteLine("str_indexof=" + (strs.IndexOf("y") == 1));
        // --- interleaved Add/IndexOf/RemoveAt + accumulation ---
        ArrayList b = new ArrayList();
        for (int i = 0; i < 5; i++) b.Add(i * 10);
        int idx = b.IndexOf(30);
        b.RemoveAt(idx);
        Console.WriteLine("inter_cnt=" + (b.Count == 4));
        Console.WriteLine("inter_idx=" + (b.IndexOf(40) == 3));
        Console.WriteLine("inter_removed=" + ((int)b[2] == 20));
        int sum = 0;
        for (int i = 0; i < b.Count; i++) sum += (int)b[i];
        Console.WriteLine("sum=" + (sum == 70));
    }
}}
