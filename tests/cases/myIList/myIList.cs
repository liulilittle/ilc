using System;
using System.Collections.Generic;
namespace Cases {
    public interface IMyList<T> { T Get(int i); void Set(int i, T v); int Count { get; } }
    public class MyList<T> : IMyList<T> {
        private List<T> inner = new List<T>();
        public T Get(int i) => inner[i];
        public void Set(int i, T v) => inner[i] = v;
        public int Count => inner.Count;
        public void Add(T v) => inner.Add(v);
    }
    public static class MyIList {
        public static void Main() {
            var list = new MyList<Exception>();
            list.Add(new Exception("a"));
            list.Add(new Exception("b"));
            IMyList<Exception> ilist = list;
            var ex = ilist.Get(0);
            Console.WriteLine(ex.Message);
            Console.WriteLine(ilist.Count);
            Console.WriteLine("PASS");
        }
    }
}
