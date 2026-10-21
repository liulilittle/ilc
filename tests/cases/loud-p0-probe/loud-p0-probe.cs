using System;using System.Linq;
class P{
 static void B(){ Console.WriteLine("[pn] byte="+new[]{1,2,3}.Min(x=>(byte)x)); }
 static void C(){ Console.WriteLine("[pn] char="+new[]{"a","c"}.Max(s=>s[0])); }
 static void U(){ Console.WriteLine("[pn] ulong="+new[]{1,2,3}.Max(x=>(ulong)x)); }
 static void GA(){ Console.WriteLine("[pn] gb_ulong_elem="+new[]{1,2,3,4}.GroupBy(x=>x%2, x=>(ulong)x).Count()); }
 static void AG(){ Console.WriteLine("[pn] agg_ulong="+new[]{1,2,3}.Aggregate(10UL,(a,x)=>a+(ulong)x)); }
 static void Run(string n, Action a){ try{ a(); }catch(Exception e){ Console.WriteLine("[pn] "+n+" EX:"+e.Message); } }
 static void Main(){ Run("B",B); Run("C",C); Run("U",U); Run("GA",GA); Run("AG",AG); }
}
