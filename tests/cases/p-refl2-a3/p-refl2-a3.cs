using System;
using System.Collections.Generic;
namespace Cases
{
    public static class PRefl2A3
    {
        static void P(string t, object v) { Console.WriteLine("[" + t + "]=[" + (v == null ? "<null>" : v.ToString()) + "]"); }
        public static void Main()
        {
            P("n.sz.name", typeof(int[]).Name);
            P("n.sz.full", typeof(int[]).FullName);
            P("n.sz.elem", typeof(int[]).GetElementType().FullName);
            P("n.md.name", typeof(int[,]).Name);
            P("n.md.full", typeof(int[,]).FullName);
            P("n.md.elem", typeof(int[,]).GetElementType().FullName);
            P("n.jag.name", typeof(int[][]).Name);
            P("n.jag.full", typeof(int[][]).FullName);
            P("n.null.name", typeof(int?).Name);
            P("n.null.full", typeof(int?).FullName);
            P("n.def.name", typeof(List<>).Name);
            P("n.def.full", typeof(List<>).FullName);
            P("n.inst.name", typeof(List<int>).Name);
            P("n.inst.full", typeof(List<int>).FullName);
            P("n.dict.full", typeof(Dictionary<string,int>).FullName);
            P("n.strarr.name", typeof(string[]).Name);
            P("n.objarr.name", typeof(object[]).Name);
            P("n.enumarr.name", typeof(DayOfWeek[]).Name);
        }
    }
}