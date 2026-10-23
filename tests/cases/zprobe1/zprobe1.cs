using System;
using System.Collections.Generic;
using System.Linq;
namespace Cases {
public static class ZP1 {
  public static bool P(int x) { return x > 0; }
  public static bool PL(long x) { return x > 0; }
  public static int S(int x) { return x; }
  public static int SL(long x) { return (int)x; }
  public static void M1() { long[] a = {1,2}; var r = a.Where(PL).ToArray(); Console.WriteLine(r.Length); }
  public static void M2() { long[] a = {1,2}; var r = a.Take(1).ToArray(); Console.WriteLine(r.Length); }
  public static void M3() { long[] a = {1,2}; var r = a.Distinct().ToArray(); Console.WriteLine(r.Length); }
  public static void M4() { long[] a = {1,2}; var r = a.OrderBy(SL).ToArray(); Console.WriteLine(r.Length); }
  public static void Main() { M1(); }
}}