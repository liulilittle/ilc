using System;
using System.Collections.Generic;

namespace Cases
{
    public static class GenList
    {
        static T Id<T>(T x) => x;

        public static void Main()
        {
            var list = new List<int>();
            list.Add(1);
            list.Add(2);
            Console.WriteLine(list.Count == 2);
            list.Add(3);
            Console.WriteLine(list.Count == 3);
            Console.WriteLine(list[0] == 1);
            Console.WriteLine(list[2] == 3);
            Console.WriteLine(Id<int>(42));
            Console.WriteLine(Id<string>("ok"));
        }
    }
}
