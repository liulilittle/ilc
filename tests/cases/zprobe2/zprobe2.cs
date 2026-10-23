using System;
using System.Linq;
namespace Cases {
public static class ZP2 {
  public static bool P(int x) { return x > 0; }
  public static void M1() { int[] a = {1,2}; var r = a.Union(new int[]{2,3}).ToArray(); Console.WriteLine(r.Length); }
  public static void M2() { int[] a = {1,2}; var r = a.Intersect(new int[]{2,3}).ToArray(); Console.WriteLine(r.Length); }
  public static void M3() { int[] a = {1,2}; var r = a.Single(); Console.WriteLine(r); }
  public static void M4() { int[] a = {1,2}; var r = a.Single(P); Console.WriteLine(r); }
  public static void M5() { int[] a = {1,2}; var r = a.ElementAt(1); Console.WriteLine(r); }
  public static void M6() { int[] a = {1,2}; var r = a.DefaultIfEmpty().ToArray(); Console.WriteLine(r.Length); }
  public static void M7() { int[] a = {1,2}; var r = a.TakeWhile(P).ToArray(); Console.WriteLine(r.Length); }
  public static void Main() { M1(); }
}}