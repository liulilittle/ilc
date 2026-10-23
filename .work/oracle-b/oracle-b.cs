using System;
using System.Collections.Generic;
namespace Cases
{
    public class Box<T> { public T V; public class Inner { } }
    public struct Val { public int X; }
    public class Plain { }
    public static class P
    {
        static void W(string t, object v) { Console.WriteLine("[" + t + "]=[" + (v == null ? "<null>" : v.ToString()) + "]"); }
        public static void Main()
        {
            W("boxint.full", typeof(Box<int>).FullName);
            W("boxint.name", typeof(Box<int>).Name);
            W("boxdef.full", typeof(Box<>).FullName);
            W("boxplain.full", typeof(Box<Plain>).FullName);
            W("boxval.full", typeof(Box<Val>).FullName);
            W("boxstr.full", typeof(Box<string>).FullName);
            W("boxintarr.full", typeof(Box<int[]>).FullName);
            W("boxint.inner.full", typeof(Box<int>.Inner).FullName);
            W("boxdef.inner.full", typeof(Box<>.Inner).FullName);
            W("boxint.inner.name", typeof(Box<int>.Inner).Name);
            W("nested.plain.full", typeof(Outer.Plain2).FullName);
            W("nested.plain.name", typeof(Outer.Plain2).Name);
            W("arr.plain.full", typeof(Plain[]).FullName);
            W("arr.plain.name", typeof(Plain[]).Name);
            W("arr.boxint.full", typeof(Box<int>[]).FullName);
            W("arr.boxint.name", typeof(Box<int>[]).Name);
            W("arr.arr.full", typeof(int[][]).FullName);
            W("arr.md.full", typeof(int[,,]).FullName);
            W("arr.md.name", typeof(int[,,]).Name);
            W("arr.jag2.full", typeof(int[][][]).FullName);
            W("arr.str.full", typeof(string[]).FullName);
            W("arr.obj.full", typeof(object[]).FullName);
            W("arr.en.full", typeof(DayOfWeek[]).FullName);
            W("arr.null.full", typeof(int?[]).FullName);
            W("list.list.full", typeof(List<List<int>>).FullName);
            W("dict.full", typeof(Dictionary<string, int>).FullName);
            W("byref.full", typeof(int).MakeByRefType().FullName);
            W("ptr.full", typeof(int).MakePointerType().FullName);
            W("asm", typeof(Plain).Assembly.FullName);
            W("asmname", typeof(Plain).Assembly.GetName().Name);
            W("aqn", typeof(Plain).AssemblyQualifiedName);
            W("aqn.int", typeof(int).AssemblyQualifiedName);
            W("boxint.aqn", typeof(Box<int>).AssemblyQualifiedName);
            W("arr.aqn", typeof(Plain[]).AssemblyQualifiedName);
            W("ns", typeof(Box<int>).Namespace);
            W("ns.nested", typeof(Outer.Plain2).Namespace);
            W("ns.arr", typeof(Plain[]).Namespace);
            W("ns.intarr", typeof(int[]).Namespace);
            W("ns.generic", typeof(List<int>).Namespace);
            W("decl.nested", typeof(Outer.Plain2).DeclaringType.Name);
            W("decl.boxinner", typeof(Box<int>.Inner).DeclaringType.Name);
            W("decl.int", typeof(int).DeclaringType == null ? "<null>" : "x");
            W("rank.nested", typeof(Box<int>.Inner).IsNested);
            W("rank.nestedpub", typeof(Box<int>.Inner).IsNestedPublic);
            W("nested.vis", (int)(typeof(Outer.Plain2).Attributes & System.Reflection.TypeAttributes.VisibilityMask));
        }
    }
    public class Outer { public class Plain2 { } }
}