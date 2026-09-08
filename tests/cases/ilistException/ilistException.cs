using System;
using System.Collections.Generic;
namespace Cases { public static class IListException {
    public static void Main() {
        var list = new List<Exception>();
        list.Add(new Exception("a"));
        list.Add(new Exception("b"));
        IList<Exception> ilist = list;
        var ex = ilist[0];
        Console.WriteLine(ex.Message);
        Console.WriteLine(ilist.Count);
        int c=0; foreach(var e in ilist) c++;
        Console.WriteLine(c);
        var arr = new Exception[2];
        arr[0] = new Exception("x");
        Console.WriteLine(arr[0].Message);
        Console.WriteLine("PASS");
    }
}}
