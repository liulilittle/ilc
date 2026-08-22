using System;
using System.Reflection;
using System.Collections.Generic;
namespace Cases { public static class DelBug2 {
    public static void Main() {
        MyClass mc1 = new MyClass();
        MyClass mc2 = new MyClass();
        Action<MyClass> m1 = mc1.InstanceM;
        Action<MyClass> m2 = mc1.InstanceM;
        Action<MyClass> m3 = mc2.InstanceM;
        Console.WriteLine("TARGET1==TARGET2=" + object.ReferenceEquals(m1.Target, m2.Target));
        Console.WriteLine("TARGET1==TARGET3=" + object.ReferenceEquals(m1.Target, m3.Target));
        Console.WriteLine("METHOD1==METHOD3=" + (m1.Method == m3.Method));
        Console.WriteLine("METHOD1.HASH=" + m1.Method.GetHashCode());
        Console.WriteLine("METHOD3.HASH=" + m3.Method.GetHashCode());
        Console.WriteLine("MC1.HASH=" + mc1.GetHashCode());
        Console.WriteLine("MC2.HASH=" + mc2.GetHashCode());
        MyClass mc1b = new MyClass();
        Action<MyClass> m4 = mc1b.InstanceM;
        Console.WriteLine("M4-HASH=" + m4.GetHashCode());
        Console.WriteLine("M1==M4=" + (m1.GetHashCode() == m4.GetHashCode()));
        Action<int> a1 = x => Console.Write("");
        Action<int> a2 = y => Console.Write("");
        Console.WriteLine("A1-METHOD==A2-METHOD=" + (a1.Method == a2.Method));
        Console.WriteLine("A1-TARGET-NULL=" + (a1.Target == null));
    }
    public class MyClass {
        public int fld = 42;
        public void InstanceM(MyClass c) { Console.Write(""); }
    }
}}
