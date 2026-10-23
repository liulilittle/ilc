using System;
using System.Linq;
namespace Cases {
public static class ZP3 {
  public static bool PB(byte x) { return x > 1; }
  public static bool PS(short x) { return x > 1; }
  public static bool PC(char x) { return x > 1; }
  public static bool PD(bool x) { return x; }
  public static bool PU(uint x) { return x > 1; }
  public static int KB(byte x) { return x; }
  public static int KS(short x) { return x; }
  public static int KC(char x) { return x; }
  public static int KU(uint x) { return (int)x; }
  // narrow Where/Select/First/Last
  public static void A1() { byte[] a={1,2}; Console.WriteLine(a.Where(PB).ToArray().Length); }
  public static void A2() { short[] a={1,2}; Console.WriteLine(a.Where(PS).ToArray().Length); }
  public static void A3() { char[] a={'a','b'}; Console.WriteLine(a.Where(PC).ToArray().Length); }
  public static void A4() { bool[] a={true,false}; Console.WriteLine(a.Where(PD).ToArray().Length); }
  public static void A5() { uint[] a={1,2}; Console.WriteLine(a.Where(PU).ToArray().Length); }
  // narrow OrderBy
  public static void B1() { byte[] a={2,1}; Console.WriteLine(a.OrderBy(KB).ToArray().Length); }
  public static void B2() { short[] a={2,1}; Console.WriteLine(a.OrderBy(KS).ToArray().Length); }
  public static void B3() { char[] a={'b','a'}; Console.WriteLine(a.OrderBy(KC).ToArray().Length); }
  public static void B4() { uint[] a={2,1}; Console.WriteLine(a.OrderBy(KU).ToArray().Length); }
  // narrow GroupBy
  public static void C1() { byte[] a={1,2}; Console.WriteLine(a.GroupBy(KB).Count()); }
  public static void C2() { uint[] a={1,2}; Console.WriteLine(a.GroupBy(KU).Count()); }
  // narrow Take/Skip/Distinct/Reverse/Concat/Except/Contains/SequenceEqual/ElementAt/DefaultIfEmpty
  public static void D1() { byte[] a={1,2}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void D2() { byte[] a={1,2}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void D3() { byte[] a={1,2,2}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void D4() { byte[] a={1,2}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void D5() { byte[] a={1,2}; Console.WriteLine(a.Concat(new byte[]{3}).ToArray().Length); }
  public static void D6() { byte[] a={1,2}; Console.WriteLine(a.Except(new byte[]{2}).ToArray().Length); }
  public static void D7() { byte[] a={1,2}; Console.WriteLine(a.Contains((byte)2)); }
  public static void D8() { byte[] a={1,2}; Console.WriteLine(a.SequenceEqual(new byte[]{1,2})); }
  public static void D9() { byte[] a={1,2}; Console.WriteLine(a.ElementAt(1)); }
  public static void D10() { byte[] a={1,2}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void Main() { A1(); }
}}