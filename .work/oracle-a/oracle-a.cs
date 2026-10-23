using System;
using System.Collections.Generic;
using System.Reflection;
namespace Cases
{
    public abstract class AbBase { public abstract void AM(); public virtual void VM() {} }
    public sealed class SeCls : AbBase { public override void AM() {} }
    public class PlainCls { public int F; public readonly int RO = 1; public const int C = 2; public static int S; }
    public struct St { public int X; }
    public enum En { A }
    public interface II { void M(); }
    public static class P
    {
        static void W(string t, object v) { Console.WriteLine("[" + t + "]=[" + (v == null ? "<null>" : v.ToString()) + "]"); }
        static void E(string t, Action a) { try { a(); } catch (Exception e) { W(t + ".err", e.GetType().Name); } }
        public static void Main()
        {
            Type[] ts = { typeof(int), typeof(string), typeof(object), typeof(AbBase), typeof(SeCls), typeof(PlainCls), typeof(St), typeof(En), typeof(II), typeof(int[]), typeof(int[,]), typeof(Exception), typeof(ValueType), typeof(Enum), typeof(Delegate), typeof(Array), typeof(List<int>), typeof(List<>) };
            foreach (var t in ts)
            {
                string n = t.Name;
                W("abs." + n, t.IsAbstract);
                W("seal." + n, t.IsSealed);
                W("pub." + n, t.IsPublic);
                W("nest." + n, t.IsNested);
                W("iface." + n, t.IsInterface);
                W("gen." + n, t.IsGenericType);
                W("gendef." + n, t.IsGenericTypeDefinition);
                E("ctorgen." + n, () => W("ctorgenv." + n, t.IsConstructedGenericType));
                W("vis." + n, (int)(t.Attributes & TypeAttributes.VisibilityMask));
            }
            E("rank.int", () => W("rank.int.v", typeof(int).GetArrayRank()));
            E("rank.arr", () => W("rank.arr.v", typeof(int[]).GetArrayRank()));
            E("rank.md", () => W("rank.md.v", typeof(int[,]).GetArrayRank()));
            E("rank.md3", () => W("rank.md3.v", typeof(int[,,]).GetArrayRank()));
            E("rank.jag", () => W("rank.jag.v", typeof(int[][]).GetArrayRank()));
            W("mk.arr.name", typeof(int).MakeArrayType().Name);
            W("mk.arr.full", typeof(int).MakeArrayType().FullName);
            W("mk.arr2.name", typeof(int).MakeArrayType(2).Name);
            W("mk.arr2.full", typeof(int).MakeArrayType(2).FullName);
            W("mk.br.name", typeof(int).MakeByRefType().Name);
            W("mk.br.full", typeof(int).MakeByRefType().FullName);
            W("mk.ptr.name", typeof(int).MakePointerType().Name);
            W("mk.ptr.full", typeof(int).MakePointerType().FullName);
            W("mk.strarr.name", typeof(string).MakeArrayType().Name);
            W("mk.listarr.name", typeof(List<int>).MakeArrayType().Name);
            var m = typeof(SeCls).GetMethod("AM");
            W("m.virt", m.IsVirtual);
            W("m.abs", m.IsAbstract);
            W("m.gen", m.IsGenericMethod);
            W("m.pub", m.IsPublic);
            W("m.static", m.IsStatic);
            W("m.final", m.IsFinal);
            W("m.hide", m.IsHideBySig);
            W("m.special", m.IsSpecialName);
            var m2 = typeof(AbBase).GetMethod("AM");
            W("m2.virt", m2.IsVirtual);
            W("m2.abs", m2.IsAbstract);
            var f = typeof(PlainCls).GetField("RO");
            W("f.initonly", f.IsInitOnly);
            W("f.literal", f.IsLiteral);
            W("f.static", f.IsStatic);
            var fc = typeof(PlainCls).GetField("C");
            W("fc.initonly", fc.IsInitOnly);
            W("fc.literal", fc.IsLiteral);
            W("fc.static", fc.IsStatic);
            var fs = typeof(PlainCls).GetField("S");
            W("fs.initonly", fs.IsInitOnly);
            W("fs.literal", fs.IsLiteral);
            W("fs.static", fs.IsStatic);
            W("f.mtoken", f.MetadataToken);
            W("m.mtoken", m.MetadataToken);
            W("t.mtoken.int", typeof(int).MetadataToken);
            W("t.mtoken.plain", typeof(PlainCls).MetadataToken);
            W("eq.obj.int", typeof(int).Equals(typeof(int)));
            W("eq.obj.str", typeof(int).Equals("x"));
            W("eq.obj.null", typeof(int).Equals(null));
            W("gt.sysint", Type.GetType("System.Int32") == null ? "<null>" : Type.GetType("System.Int32").FullName);
            W("gt.cases", Type.GetType("Cases.PlainCls") == null ? "<null>" : Type.GetType("Cases.PlainCls").FullName);
            W("gt.bogus.throw", "no");
            E("gt.bogus.true", () => W("gt.bogus.true.v", Type.GetType("No.Such", true)));
            W("gt.case", Type.GetType("cases.plaincls", false, true) == null ? "<null>" : "hit");
            W("gt.case.nocase", Type.GetType("cases.plaincls", false, false) == null ? "<null>" : "hit");
            var asm = typeof(int).Assembly;
            W("asm.full", asm.FullName);
            W("asm.name", asm.GetName().Name);
            W("asm.ver", asm.GetName().Version.ToString());
            W("asm.gt", asm.GetType("Cases.PlainCls") == null ? "<null>" : "hit");
            W("asm.types.count", asm.GetTypes().Length > 0);
            W("asm.exec", Assembly.GetExecutingAssembly().GetName().Name);
            W("asm.entry", Assembly.GetEntryAssembly() == null ? "<null>" : Assembly.GetEntryAssembly().GetName().Name);
            W("asm.eq", asm == typeof(string).Assembly);
            W("h.th", Type.GetTypeFromHandle(typeof(int).TypeHandle).Name);
            W("h.mh", typeof(PlainCls).GetMethod("ToString").MethodHandle.Value == IntPtr.Zero);
            W("h.fh", typeof(PlainCls).GetField("F").FieldHandle.Value == IntPtr.Zero);
            W("act.t", Activator.CreateInstance(typeof(PlainCls)) == null ? "<null>" : "hit");
            W("act.targs", Activator.CreateInstance(typeof(PlainCls), new object[0]) == null ? "<null>" : "hit");
            W("act.tnonpub", Activator.CreateInstance(typeof(PlainCls), false) == null ? "<null>" : "hit");
            W("act.tnonpub2", Activator.CreateInstance(typeof(PlainCls), true) == null ? "<null>" : "hit");
            W("act.int", Activator.CreateInstance(typeof(int)));
            W("act.st", Activator.CreateInstance(typeof(St)) == null ? "<null>" : "hit");
        }
    }
}