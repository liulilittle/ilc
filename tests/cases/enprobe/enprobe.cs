using System;
using System.Collections;
using System.Collections.Generic;

namespace Cases { public static class EnProbe {
    class MyEnum : IEnumerator<int> {
        int n, i;
        public MyEnum(int n) { this.n = n; }
        public bool MoveNext() { return ++i <= n; }
        public int Current { get { return i; } }
        int IEnumerator<int>.Current { get { return i; } }
        object IEnumerator.Current { get { return i; } }
        public void Reset() { i = 0; }
        public void Dispose() {}
    }
    class MyEnum2 : IEnumerable<int> {
        int n;
        public MyEnum2(int n) { this.n = n; }
        public IEnumerator<int> GetEnumerator() { return new MyEnum(n); }
        IEnumerator IEnumerable.GetEnumerator() { return new MyEnum(n); }
    }
    public static void Main() {
        int sum = 0;
        foreach (int x in new MyEnum2(4)) sum += x;
        Console.WriteLine("sum=" + sum);
        Console.WriteLine("done");
    }
}}
