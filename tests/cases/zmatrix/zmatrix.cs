using System;
using System.Collections.Generic;
using System.Linq;
namespace Cases {
public static class ZMatrix {
  public static bool P_i4(int x) { return x > 0; }
  public static int S_i4(int x) { return x + 1; }
  public static int K_i4(int x) { return x % 2; }
  public static int A_i4(int x, int y) { return x + y; }
  public static bool PI_i4(int x, int i) { return i >= 0; }
  public static int SI_i4(int x, int i) { return x; }
  public static int Z_i4(int x, int y) { return x; }
  public static bool P_i8(long x) { return x > 0; }
  public static long S_i8(long x) { return x + 1L; }
  public static int K_i8(long x) { return (int)(x % 2); }
  public static long A_i8(long x, long y) { return x + y; }
  public static bool PI_i8(long x, int i) { return i >= 0; }
  public static long SI_i8(long x, int i) { return x; }
  public static long Z_i8(long x, long y) { return x; }
  public static bool P_f(float x) { return x > 0f; }
  public static float S_f(float x) { return x + 1f; }
  public static int K_f(float x) { return (int)x; }
  public static float A_f(float x, float y) { return x + y; }
  public static bool PI_f(float x, int i) { return i >= 0; }
  public static float SI_f(float x, int i) { return x; }
  public static float Z_f(float x, float y) { return x; }
  public static bool P_d(double x) { return x > 0.0; }
  public static double S_d(double x) { return x + 1.0; }
  public static int K_d(double x) { return (int)x; }
  public static double A_d(double x, double y) { return x + y; }
  public static bool PI_d(double x, int i) { return i >= 0; }
  public static double SI_d(double x, int i) { return x; }
  public static double Z_d(double x, double y) { return x; }
  public static bool P_by(byte x) { return x > 0; }
  public static byte S_by(byte x) { return (byte)(x + 1); }
  public static int K_by(byte x) { return x % 2; }
  public static byte A_by(byte x, byte y) { return (byte)(x + y); }
  public static bool PI_by(byte x, int i) { return i >= 0; }
  public static byte SI_by(byte x, int i) { return x; }
  public static byte Z_by(byte x, byte y) { return x; }
  public static bool P_sb(sbyte x) { return x > 0; }
  public static sbyte S_sb(sbyte x) { return (sbyte)(x + 1); }
  public static int K_sb(sbyte x) { return x % 2; }
  public static sbyte A_sb(sbyte x, sbyte y) { return (sbyte)(x + y); }
  public static bool PI_sb(sbyte x, int i) { return i >= 0; }
  public static sbyte SI_sb(sbyte x, int i) { return x; }
  public static sbyte Z_sb(sbyte x, sbyte y) { return x; }
  public static bool P_sh(short x) { return x > 0; }
  public static short S_sh(short x) { return (short)(x + 1); }
  public static int K_sh(short x) { return x % 2; }
  public static short A_sh(short x, short y) { return (short)(x + y); }
  public static bool PI_sh(short x, int i) { return i >= 0; }
  public static short SI_sh(short x, int i) { return x; }
  public static short Z_sh(short x, short y) { return x; }
  public static bool P_us(ushort x) { return x > 0; }
  public static ushort S_us(ushort x) { return (ushort)(x + 1); }
  public static int K_us(ushort x) { return x % 2; }
  public static ushort A_us(ushort x, ushort y) { return (ushort)(x + y); }
  public static bool PI_us(ushort x, int i) { return i >= 0; }
  public static ushort SI_us(ushort x, int i) { return x; }
  public static ushort Z_us(ushort x, ushort y) { return x; }
  public static bool P_ui(uint x) { return x > 0; }
  public static uint S_ui(uint x) { return x + 1u; }
  public static int K_ui(uint x) { return (int)(x % 2); }
  public static uint A_ui(uint x, uint y) { return x + y; }
  public static bool PI_ui(uint x, int i) { return i >= 0; }
  public static uint SI_ui(uint x, int i) { return x; }
  public static uint Z_ui(uint x, uint y) { return x; }
  public static bool P_ul(ulong x) { return x > 0; }
  public static ulong S_ul(ulong x) { return x + 1UL; }
  public static int K_ul(ulong x) { return (int)(x % 2); }
  public static ulong A_ul(ulong x, ulong y) { return x + y; }
  public static bool PI_ul(ulong x, int i) { return i >= 0; }
  public static ulong SI_ul(ulong x, int i) { return x; }
  public static ulong Z_ul(ulong x, ulong y) { return x; }
  public static bool P_bo(bool x) { return x; }
  public static bool S_bo(bool x) { return !x; }
  public static int K_bo(bool x) { return x ? 1 : 0; }
  public static bool A_bo(bool x, bool y) { return x || y; }
  public static bool PI_bo(bool x, int i) { return i >= 0; }
  public static bool SI_bo(bool x, int i) { return x; }
  public static bool Z_bo(bool x, bool y) { return x; }
  public static bool P_ch(char x) { return x > 'a'; }
  public static char S_ch(char x) { return (char)(x + 1); }
  public static int K_ch(char x) { return (int)x; }
  public static char A_ch(char x, char y) { return (char)(x + 1); }
  public static bool PI_ch(char x, int i) { return i >= 0; }
  public static char SI_ch(char x, int i) { return x; }
  public static char Z_ch(char x, char y) { return x; }
  public static bool P_dec(decimal x) { return x > 0m; }
  public static decimal S_dec(decimal x) { return x + 1m; }
  public static int K_dec(decimal x) { return (int)x; }
  public static decimal A_dec(decimal x, decimal y) { return x + y; }
  public static bool PI_dec(decimal x, int i) { return i >= 0; }
  public static decimal SI_dec(decimal x, int i) { return x; }
  public static decimal Z_dec(decimal x, decimal y) { return x; }
  public static bool P_dt(DateTime x) { return x.Year > 0; }
  public static DateTime S_dt(DateTime x) { return x.AddDays(1); }
  public static int K_dt(DateTime x) { return x.Year; }
  public static DateTime A_dt(DateTime x, DateTime y) { return x.AddDays(y.Day); }
  public static bool PI_dt(DateTime x, int i) { return i >= 0; }
  public static DateTime SI_dt(DateTime x, int i) { return x; }
  public static DateTime Z_dt(DateTime x, DateTime y) { return x; }
  public static bool P_ts(TimeSpan x) { return x.TotalHours > 0; }
  public static TimeSpan S_ts(TimeSpan x) { return x.Add(TimeSpan.FromMinutes(1)); }
  public static int K_ts(TimeSpan x) { return (int)x.TotalHours; }
  public static TimeSpan A_ts(TimeSpan x, TimeSpan y) { return x.Add(y); }
  public static bool PI_ts(TimeSpan x, int i) { return i >= 0; }
  public static TimeSpan SI_ts(TimeSpan x, int i) { return x; }
  public static TimeSpan Z_ts(TimeSpan x, TimeSpan y) { return x; }
  public static bool P_gd(Guid x) { return x != Guid.Empty; }
  public static Guid S_gd(Guid x) { return x; }
  public static int K_gd(Guid x) { return x.GetHashCode(); }
  public static Guid A_gd(Guid x, Guid y) { return x; }
  public static bool PI_gd(Guid x, int i) { return i >= 0; }
  public static Guid SI_gd(Guid x, int i) { return x; }
  public static Guid Z_gd(Guid x, Guid y) { return x; }
  public static bool P_sr(string x) { return x.Length > 0; }
  public static string S_sr(string x) { return x + "!"; }
  public static int K_sr(string x) { return x.Length; }
  public static string A_sr(string x, string y) { return x + y; }
  public static bool PI_sr(string x, int i) { return i >= 0; }
  public static string SI_sr(string x, int i) { return x; }
  public static string Z_sr(string x, string y) { return x; }
  public static void M_i4_Where() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Where(P_i4).ToArray().Length); }
  public static void M_i4_WhereIdx() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Where(PI_i4).ToArray().Length); }
  public static void M_i4_Select() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Select(S_i4).ToArray().Length); }
  public static void M_i4_SelectIdx() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Select(SI_i4).ToArray().Length); }
  public static void M_i4_OrderBy() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.OrderBy(K_i4).ToArray().Length); }
  public static void M_i4_OrderByDesc() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.OrderByDescending(K_i4).ToArray().Length); }
  public static void M_i4_ThenBy() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.OrderBy(K_i4).ThenBy(K_i4).ToArray().Length); }
  public static void M_i4_GroupBy() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.GroupBy(K_i4).Count()); }
  public static void M_i4_ToList() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.ToList().Count); }
  public static void M_i4_ToArray() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.ToArray().Length); }
  public static void M_i4_First() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_i4_FirstOrDef() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_i4_Last() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_i4_LastOrDef() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_i4_Any() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Any()); }
  public static void M_i4_All() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.All(P_i4)); }
  public static void M_i4_Count() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Count()); }
  public static void M_i4_CountPred() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Count(P_i4)); }
  public static void M_i4_Zip() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Zip(a, Z_i4).ToArray().Length); }
  public static void M_i4_Take() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_i4_Skip() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_i4_Distinct() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_i4_Reverse() { int[] a = new int[]{1,2,3}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_i4_Concat() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_i4_Union() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_i4_Intersect() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_i4_Except() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_i4_Contains() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_i4_SequenceEq() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_i4_Single() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_i4_SingleOrDef() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_i4_ElementAt() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_i4_DefaultIfEmpty() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_i4_TakeWhile() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.TakeWhile(P_i4).ToArray().Length); }
  public static void M_i4_SkipWhile() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.SkipWhile(P_i4).ToArray().Length); }
  public static void M_i4_LongCount() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.LongCount()); }
  public static void M_i4_Aggregate() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Aggregate(A_i4).GetHashCode()); }
  public static void M_i4_AggSeed() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Aggregate(a[0], A_i4).GetHashCode()); }
  public static void M_i4_Sum() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Sum().GetHashCode()); }
  public static void M_i4_SumSel() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Sum(S_i4).GetHashCode()); }
  public static void M_i4_Average() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Average().GetHashCode()); }
  public static void M_i4_AverageSel() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Average(S_i4).GetHashCode()); }
  public static void M_i4_Min() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_i4_MinSel() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Min(S_i4).GetHashCode()); }
  public static void M_i4_Max() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_i4_MaxSel() { int[] a = new int[]{1,2,3}; Console.WriteLine(a.Max(S_i4).GetHashCode()); }
  public static void M_i8_Where() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Where(P_i8).ToArray().Length); }
  public static void M_i8_WhereIdx() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Where(PI_i8).ToArray().Length); }
  public static void M_i8_Select() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Select(S_i8).ToArray().Length); }
  public static void M_i8_SelectIdx() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Select(SI_i8).ToArray().Length); }
  public static void M_i8_OrderBy() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.OrderBy(K_i8).ToArray().Length); }
  public static void M_i8_OrderByDesc() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.OrderByDescending(K_i8).ToArray().Length); }
  public static void M_i8_ThenBy() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.OrderBy(K_i8).ThenBy(K_i8).ToArray().Length); }
  public static void M_i8_GroupBy() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.GroupBy(K_i8).Count()); }
  public static void M_i8_ToList() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.ToList().Count); }
  public static void M_i8_ToArray() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.ToArray().Length); }
  public static void M_i8_First() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_i8_FirstOrDef() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_i8_Last() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_i8_LastOrDef() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_i8_Any() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Any()); }
  public static void M_i8_All() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.All(P_i8)); }
  public static void M_i8_Count() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Count()); }
  public static void M_i8_CountPred() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Count(P_i8)); }
  public static void M_i8_Zip() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Zip(a, Z_i8).ToArray().Length); }
  public static void M_i8_Take() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_i8_Skip() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_i8_Distinct() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_i8_Reverse() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_i8_Concat() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_i8_Union() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_i8_Intersect() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_i8_Except() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_i8_Contains() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_i8_SequenceEq() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_i8_Single() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_i8_SingleOrDef() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_i8_ElementAt() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_i8_DefaultIfEmpty() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_i8_TakeWhile() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.TakeWhile(P_i8).ToArray().Length); }
  public static void M_i8_SkipWhile() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.SkipWhile(P_i8).ToArray().Length); }
  public static void M_i8_LongCount() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.LongCount()); }
  public static void M_i8_Aggregate() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Aggregate(A_i8).GetHashCode()); }
  public static void M_i8_AggSeed() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Aggregate(a[0], A_i8).GetHashCode()); }
  public static void M_i8_Sum() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Sum().GetHashCode()); }
  public static void M_i8_SumSel() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Sum(S_i8).GetHashCode()); }
  public static void M_i8_Average() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Average().GetHashCode()); }
  public static void M_i8_AverageSel() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Average(S_i8).GetHashCode()); }
  public static void M_i8_Min() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_i8_MinSel() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Min(S_i8).GetHashCode()); }
  public static void M_i8_Max() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_i8_MaxSel() { long[] a = new long[]{1L,2L,3L}; Console.WriteLine(a.Max(S_i8).GetHashCode()); }
  public static void M_f_Where() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Where(P_f).ToArray().Length); }
  public static void M_f_WhereIdx() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Where(PI_f).ToArray().Length); }
  public static void M_f_Select() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Select(S_f).ToArray().Length); }
  public static void M_f_SelectIdx() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Select(SI_f).ToArray().Length); }
  public static void M_f_OrderBy() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.OrderBy(K_f).ToArray().Length); }
  public static void M_f_OrderByDesc() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.OrderByDescending(K_f).ToArray().Length); }
  public static void M_f_ThenBy() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.OrderBy(K_f).ThenBy(K_f).ToArray().Length); }
  public static void M_f_GroupBy() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.GroupBy(K_f).Count()); }
  public static void M_f_ToList() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.ToList().Count); }
  public static void M_f_ToArray() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.ToArray().Length); }
  public static void M_f_First() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_f_FirstOrDef() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_f_Last() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_f_LastOrDef() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_f_Any() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Any()); }
  public static void M_f_All() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.All(P_f)); }
  public static void M_f_Count() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Count()); }
  public static void M_f_CountPred() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Count(P_f)); }
  public static void M_f_Zip() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Zip(a, Z_f).ToArray().Length); }
  public static void M_f_Take() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_f_Skip() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_f_Distinct() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_f_Reverse() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_f_Concat() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_f_Union() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_f_Intersect() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_f_Except() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_f_Contains() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_f_SequenceEq() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_f_Single() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_f_SingleOrDef() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_f_ElementAt() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_f_DefaultIfEmpty() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_f_TakeWhile() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.TakeWhile(P_f).ToArray().Length); }
  public static void M_f_SkipWhile() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.SkipWhile(P_f).ToArray().Length); }
  public static void M_f_LongCount() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.LongCount()); }
  public static void M_f_Aggregate() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Aggregate(A_f).GetHashCode()); }
  public static void M_f_AggSeed() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Aggregate(a[0], A_f).GetHashCode()); }
  public static void M_f_Sum() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Sum().GetHashCode()); }
  public static void M_f_SumSel() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Sum(S_f).GetHashCode()); }
  public static void M_f_Average() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Average().GetHashCode()); }
  public static void M_f_AverageSel() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Average(S_f).GetHashCode()); }
  public static void M_f_Min() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_f_MinSel() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Min(S_f).GetHashCode()); }
  public static void M_f_Max() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_f_MaxSel() { float[] a = new float[]{1.5f,2.5f}; Console.WriteLine(a.Max(S_f).GetHashCode()); }
  public static void M_d_Where() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Where(P_d).ToArray().Length); }
  public static void M_d_WhereIdx() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Where(PI_d).ToArray().Length); }
  public static void M_d_Select() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Select(S_d).ToArray().Length); }
  public static void M_d_SelectIdx() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Select(SI_d).ToArray().Length); }
  public static void M_d_OrderBy() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.OrderBy(K_d).ToArray().Length); }
  public static void M_d_OrderByDesc() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.OrderByDescending(K_d).ToArray().Length); }
  public static void M_d_ThenBy() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.OrderBy(K_d).ThenBy(K_d).ToArray().Length); }
  public static void M_d_GroupBy() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.GroupBy(K_d).Count()); }
  public static void M_d_ToList() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.ToList().Count); }
  public static void M_d_ToArray() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.ToArray().Length); }
  public static void M_d_First() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_d_FirstOrDef() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_d_Last() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_d_LastOrDef() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_d_Any() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Any()); }
  public static void M_d_All() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.All(P_d)); }
  public static void M_d_Count() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Count()); }
  public static void M_d_CountPred() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Count(P_d)); }
  public static void M_d_Zip() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Zip(a, Z_d).ToArray().Length); }
  public static void M_d_Take() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_d_Skip() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_d_Distinct() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_d_Reverse() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_d_Concat() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_d_Union() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_d_Intersect() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_d_Except() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_d_Contains() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_d_SequenceEq() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_d_Single() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_d_SingleOrDef() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_d_ElementAt() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_d_DefaultIfEmpty() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_d_TakeWhile() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.TakeWhile(P_d).ToArray().Length); }
  public static void M_d_SkipWhile() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.SkipWhile(P_d).ToArray().Length); }
  public static void M_d_LongCount() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.LongCount()); }
  public static void M_d_Aggregate() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Aggregate(A_d).GetHashCode()); }
  public static void M_d_AggSeed() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Aggregate(a[0], A_d).GetHashCode()); }
  public static void M_d_Sum() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Sum().GetHashCode()); }
  public static void M_d_SumSel() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Sum(S_d).GetHashCode()); }
  public static void M_d_Average() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Average().GetHashCode()); }
  public static void M_d_AverageSel() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Average(S_d).GetHashCode()); }
  public static void M_d_Min() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_d_MinSel() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Min(S_d).GetHashCode()); }
  public static void M_d_Max() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_d_MaxSel() { double[] a = new double[]{1.5,2.5}; Console.WriteLine(a.Max(S_d).GetHashCode()); }
  public static void M_by_Where() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Where(P_by).ToArray().Length); }
  public static void M_by_WhereIdx() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Where(PI_by).ToArray().Length); }
  public static void M_by_Select() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Select(S_by).ToArray().Length); }
  public static void M_by_SelectIdx() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Select(SI_by).ToArray().Length); }
  public static void M_by_OrderBy() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.OrderBy(K_by).ToArray().Length); }
  public static void M_by_OrderByDesc() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.OrderByDescending(K_by).ToArray().Length); }
  public static void M_by_ThenBy() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.OrderBy(K_by).ThenBy(K_by).ToArray().Length); }
  public static void M_by_GroupBy() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.GroupBy(K_by).Count()); }
  public static void M_by_ToList() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.ToList().Count); }
  public static void M_by_ToArray() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.ToArray().Length); }
  public static void M_by_First() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_by_FirstOrDef() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_by_Last() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_by_LastOrDef() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_by_Any() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Any()); }
  public static void M_by_All() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.All(P_by)); }
  public static void M_by_Count() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Count()); }
  public static void M_by_CountPred() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Count(P_by)); }
  public static void M_by_Zip() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Zip(a, Z_by).ToArray().Length); }
  public static void M_by_Take() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_by_Skip() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_by_Distinct() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_by_Reverse() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_by_Concat() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_by_Union() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_by_Intersect() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_by_Except() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_by_Contains() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_by_SequenceEq() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_by_Single() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_by_SingleOrDef() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_by_ElementAt() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_by_DefaultIfEmpty() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_by_TakeWhile() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.TakeWhile(P_by).ToArray().Length); }
  public static void M_by_SkipWhile() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.SkipWhile(P_by).ToArray().Length); }
  public static void M_by_LongCount() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.LongCount()); }
  public static void M_by_Aggregate() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Aggregate(A_by).GetHashCode()); }
  public static void M_by_AggSeed() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Aggregate(a[0], A_by).GetHashCode()); }
  public static void M_by_Min() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_by_Max() { byte[] a = new byte[]{1,2,3}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_sb_Where() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Where(P_sb).ToArray().Length); }
  public static void M_sb_WhereIdx() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Where(PI_sb).ToArray().Length); }
  public static void M_sb_Select() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Select(S_sb).ToArray().Length); }
  public static void M_sb_SelectIdx() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Select(SI_sb).ToArray().Length); }
  public static void M_sb_OrderBy() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.OrderBy(K_sb).ToArray().Length); }
  public static void M_sb_OrderByDesc() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.OrderByDescending(K_sb).ToArray().Length); }
  public static void M_sb_ThenBy() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.OrderBy(K_sb).ThenBy(K_sb).ToArray().Length); }
  public static void M_sb_GroupBy() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.GroupBy(K_sb).Count()); }
  public static void M_sb_ToList() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.ToList().Count); }
  public static void M_sb_ToArray() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.ToArray().Length); }
  public static void M_sb_First() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_sb_FirstOrDef() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_sb_Last() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_sb_LastOrDef() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_sb_Any() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Any()); }
  public static void M_sb_All() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.All(P_sb)); }
  public static void M_sb_Count() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Count()); }
  public static void M_sb_CountPred() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Count(P_sb)); }
  public static void M_sb_Zip() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Zip(a, Z_sb).ToArray().Length); }
  public static void M_sb_Take() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_sb_Skip() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_sb_Distinct() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_sb_Reverse() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_sb_Concat() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_sb_Union() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_sb_Intersect() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_sb_Except() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_sb_Contains() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_sb_SequenceEq() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_sb_Single() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_sb_SingleOrDef() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_sb_ElementAt() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_sb_DefaultIfEmpty() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_sb_TakeWhile() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.TakeWhile(P_sb).ToArray().Length); }
  public static void M_sb_SkipWhile() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.SkipWhile(P_sb).ToArray().Length); }
  public static void M_sb_LongCount() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.LongCount()); }
  public static void M_sb_Aggregate() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Aggregate(A_sb).GetHashCode()); }
  public static void M_sb_AggSeed() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Aggregate(a[0], A_sb).GetHashCode()); }
  public static void M_sb_Min() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_sb_Max() { sbyte[] a = new sbyte[]{1,2,3}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_sh_Where() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Where(P_sh).ToArray().Length); }
  public static void M_sh_WhereIdx() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Where(PI_sh).ToArray().Length); }
  public static void M_sh_Select() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Select(S_sh).ToArray().Length); }
  public static void M_sh_SelectIdx() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Select(SI_sh).ToArray().Length); }
  public static void M_sh_OrderBy() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.OrderBy(K_sh).ToArray().Length); }
  public static void M_sh_OrderByDesc() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.OrderByDescending(K_sh).ToArray().Length); }
  public static void M_sh_ThenBy() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.OrderBy(K_sh).ThenBy(K_sh).ToArray().Length); }
  public static void M_sh_GroupBy() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.GroupBy(K_sh).Count()); }
  public static void M_sh_ToList() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.ToList().Count); }
  public static void M_sh_ToArray() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.ToArray().Length); }
  public static void M_sh_First() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_sh_FirstOrDef() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_sh_Last() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_sh_LastOrDef() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_sh_Any() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Any()); }
  public static void M_sh_All() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.All(P_sh)); }
  public static void M_sh_Count() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Count()); }
  public static void M_sh_CountPred() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Count(P_sh)); }
  public static void M_sh_Zip() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Zip(a, Z_sh).ToArray().Length); }
  public static void M_sh_Take() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_sh_Skip() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_sh_Distinct() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_sh_Reverse() { short[] a = new short[]{1,2,3}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_sh_Concat() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_sh_Union() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_sh_Intersect() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_sh_Except() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_sh_Contains() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_sh_SequenceEq() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_sh_Single() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_sh_SingleOrDef() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_sh_ElementAt() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_sh_DefaultIfEmpty() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_sh_TakeWhile() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.TakeWhile(P_sh).ToArray().Length); }
  public static void M_sh_SkipWhile() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.SkipWhile(P_sh).ToArray().Length); }
  public static void M_sh_LongCount() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.LongCount()); }
  public static void M_sh_Aggregate() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Aggregate(A_sh).GetHashCode()); }
  public static void M_sh_AggSeed() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Aggregate(a[0], A_sh).GetHashCode()); }
  public static void M_sh_Min() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_sh_Max() { short[] a = new short[]{1,2,3}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_us_Where() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Where(P_us).ToArray().Length); }
  public static void M_us_WhereIdx() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Where(PI_us).ToArray().Length); }
  public static void M_us_Select() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Select(S_us).ToArray().Length); }
  public static void M_us_SelectIdx() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Select(SI_us).ToArray().Length); }
  public static void M_us_OrderBy() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.OrderBy(K_us).ToArray().Length); }
  public static void M_us_OrderByDesc() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.OrderByDescending(K_us).ToArray().Length); }
  public static void M_us_ThenBy() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.OrderBy(K_us).ThenBy(K_us).ToArray().Length); }
  public static void M_us_GroupBy() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.GroupBy(K_us).Count()); }
  public static void M_us_ToList() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.ToList().Count); }
  public static void M_us_ToArray() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.ToArray().Length); }
  public static void M_us_First() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_us_FirstOrDef() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_us_Last() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_us_LastOrDef() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_us_Any() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Any()); }
  public static void M_us_All() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.All(P_us)); }
  public static void M_us_Count() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Count()); }
  public static void M_us_CountPred() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Count(P_us)); }
  public static void M_us_Zip() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Zip(a, Z_us).ToArray().Length); }
  public static void M_us_Take() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_us_Skip() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_us_Distinct() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_us_Reverse() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_us_Concat() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_us_Union() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_us_Intersect() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_us_Except() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_us_Contains() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_us_SequenceEq() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_us_Single() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_us_SingleOrDef() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_us_ElementAt() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_us_DefaultIfEmpty() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_us_TakeWhile() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.TakeWhile(P_us).ToArray().Length); }
  public static void M_us_SkipWhile() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.SkipWhile(P_us).ToArray().Length); }
  public static void M_us_LongCount() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.LongCount()); }
  public static void M_us_Aggregate() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Aggregate(A_us).GetHashCode()); }
  public static void M_us_AggSeed() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Aggregate(a[0], A_us).GetHashCode()); }
  public static void M_us_Min() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_us_Max() { ushort[] a = new ushort[]{1,2,3}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_ui_Where() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Where(P_ui).ToArray().Length); }
  public static void M_ui_WhereIdx() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Where(PI_ui).ToArray().Length); }
  public static void M_ui_Select() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Select(S_ui).ToArray().Length); }
  public static void M_ui_SelectIdx() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Select(SI_ui).ToArray().Length); }
  public static void M_ui_OrderBy() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.OrderBy(K_ui).ToArray().Length); }
  public static void M_ui_OrderByDesc() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.OrderByDescending(K_ui).ToArray().Length); }
  public static void M_ui_ThenBy() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.OrderBy(K_ui).ThenBy(K_ui).ToArray().Length); }
  public static void M_ui_GroupBy() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.GroupBy(K_ui).Count()); }
  public static void M_ui_ToList() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.ToList().Count); }
  public static void M_ui_ToArray() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.ToArray().Length); }
  public static void M_ui_First() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_ui_FirstOrDef() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_ui_Last() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_ui_LastOrDef() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_ui_Any() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Any()); }
  public static void M_ui_All() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.All(P_ui)); }
  public static void M_ui_Count() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Count()); }
  public static void M_ui_CountPred() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Count(P_ui)); }
  public static void M_ui_Zip() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Zip(a, Z_ui).ToArray().Length); }
  public static void M_ui_Take() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_ui_Skip() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_ui_Distinct() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_ui_Reverse() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_ui_Concat() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_ui_Union() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_ui_Intersect() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_ui_Except() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_ui_Contains() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_ui_SequenceEq() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_ui_Single() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_ui_SingleOrDef() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_ui_ElementAt() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_ui_DefaultIfEmpty() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_ui_TakeWhile() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.TakeWhile(P_ui).ToArray().Length); }
  public static void M_ui_SkipWhile() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.SkipWhile(P_ui).ToArray().Length); }
  public static void M_ui_LongCount() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.LongCount()); }
  public static void M_ui_Aggregate() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Aggregate(A_ui).GetHashCode()); }
  public static void M_ui_AggSeed() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Aggregate(a[0], A_ui).GetHashCode()); }
  public static void M_ui_Min() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_ui_Max() { uint[] a = new uint[]{1u,2u,3u}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_ul_Where() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Where(P_ul).ToArray().Length); }
  public static void M_ul_WhereIdx() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Where(PI_ul).ToArray().Length); }
  public static void M_ul_Select() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Select(S_ul).ToArray().Length); }
  public static void M_ul_SelectIdx() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Select(SI_ul).ToArray().Length); }
  public static void M_ul_OrderBy() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.OrderBy(K_ul).ToArray().Length); }
  public static void M_ul_OrderByDesc() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.OrderByDescending(K_ul).ToArray().Length); }
  public static void M_ul_ThenBy() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.OrderBy(K_ul).ThenBy(K_ul).ToArray().Length); }
  public static void M_ul_GroupBy() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.GroupBy(K_ul).Count()); }
  public static void M_ul_ToList() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.ToList().Count); }
  public static void M_ul_ToArray() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.ToArray().Length); }
  public static void M_ul_First() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_ul_FirstOrDef() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_ul_Last() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_ul_LastOrDef() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_ul_Any() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Any()); }
  public static void M_ul_All() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.All(P_ul)); }
  public static void M_ul_Count() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Count()); }
  public static void M_ul_CountPred() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Count(P_ul)); }
  public static void M_ul_Zip() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Zip(a, Z_ul).ToArray().Length); }
  public static void M_ul_Take() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_ul_Skip() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_ul_Distinct() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_ul_Reverse() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_ul_Concat() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_ul_Union() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_ul_Intersect() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_ul_Except() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_ul_Contains() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_ul_SequenceEq() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_ul_Single() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_ul_SingleOrDef() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_ul_ElementAt() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_ul_DefaultIfEmpty() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_ul_TakeWhile() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.TakeWhile(P_ul).ToArray().Length); }
  public static void M_ul_SkipWhile() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.SkipWhile(P_ul).ToArray().Length); }
  public static void M_ul_LongCount() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.LongCount()); }
  public static void M_ul_Aggregate() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Aggregate(A_ul).GetHashCode()); }
  public static void M_ul_AggSeed() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Aggregate(a[0], A_ul).GetHashCode()); }
  public static void M_ul_Min() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_ul_Max() { ulong[] a = new ulong[]{1UL,2UL,3UL}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_bo_Where() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Where(P_bo).ToArray().Length); }
  public static void M_bo_WhereIdx() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Where(PI_bo).ToArray().Length); }
  public static void M_bo_Select() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Select(S_bo).ToArray().Length); }
  public static void M_bo_SelectIdx() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Select(SI_bo).ToArray().Length); }
  public static void M_bo_OrderBy() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.OrderBy(K_bo).ToArray().Length); }
  public static void M_bo_OrderByDesc() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.OrderByDescending(K_bo).ToArray().Length); }
  public static void M_bo_ThenBy() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.OrderBy(K_bo).ThenBy(K_bo).ToArray().Length); }
  public static void M_bo_GroupBy() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.GroupBy(K_bo).Count()); }
  public static void M_bo_ToList() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.ToList().Count); }
  public static void M_bo_ToArray() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.ToArray().Length); }
  public static void M_bo_First() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_bo_FirstOrDef() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_bo_Last() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_bo_LastOrDef() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_bo_Any() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Any()); }
  public static void M_bo_All() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.All(P_bo)); }
  public static void M_bo_Count() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Count()); }
  public static void M_bo_CountPred() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Count(P_bo)); }
  public static void M_bo_Zip() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Zip(a, Z_bo).ToArray().Length); }
  public static void M_bo_Take() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_bo_Skip() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_bo_Distinct() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_bo_Reverse() { bool[] a = new bool[]{true,false}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_bo_Concat() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_bo_Union() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_bo_Intersect() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_bo_Except() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_bo_Contains() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_bo_SequenceEq() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_bo_Single() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_bo_SingleOrDef() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_bo_ElementAt() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_bo_DefaultIfEmpty() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_bo_TakeWhile() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.TakeWhile(P_bo).ToArray().Length); }
  public static void M_bo_SkipWhile() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.SkipWhile(P_bo).ToArray().Length); }
  public static void M_bo_LongCount() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.LongCount()); }
  public static void M_bo_Aggregate() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Aggregate(A_bo).GetHashCode()); }
  public static void M_bo_AggSeed() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Aggregate(a[0], A_bo).GetHashCode()); }
  public static void M_bo_Min() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_bo_Max() { bool[] a = new bool[]{true,false}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_ch_Where() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Where(P_ch).ToArray().Length); }
  public static void M_ch_WhereIdx() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Where(PI_ch).ToArray().Length); }
  public static void M_ch_Select() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Select(S_ch).ToArray().Length); }
  public static void M_ch_SelectIdx() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Select(SI_ch).ToArray().Length); }
  public static void M_ch_OrderBy() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.OrderBy(K_ch).ToArray().Length); }
  public static void M_ch_OrderByDesc() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.OrderByDescending(K_ch).ToArray().Length); }
  public static void M_ch_ThenBy() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.OrderBy(K_ch).ThenBy(K_ch).ToArray().Length); }
  public static void M_ch_GroupBy() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.GroupBy(K_ch).Count()); }
  public static void M_ch_ToList() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.ToList().Count); }
  public static void M_ch_ToArray() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.ToArray().Length); }
  public static void M_ch_First() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_ch_FirstOrDef() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_ch_Last() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_ch_LastOrDef() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_ch_Any() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Any()); }
  public static void M_ch_All() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.All(P_ch)); }
  public static void M_ch_Count() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Count()); }
  public static void M_ch_CountPred() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Count(P_ch)); }
  public static void M_ch_Zip() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Zip(a, Z_ch).ToArray().Length); }
  public static void M_ch_Take() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_ch_Skip() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_ch_Distinct() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_ch_Reverse() { char[] a = new char[]{'a','b'}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_ch_Concat() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_ch_Union() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_ch_Intersect() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_ch_Except() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_ch_Contains() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_ch_SequenceEq() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_ch_Single() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_ch_SingleOrDef() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_ch_ElementAt() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_ch_DefaultIfEmpty() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_ch_TakeWhile() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.TakeWhile(P_ch).ToArray().Length); }
  public static void M_ch_SkipWhile() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.SkipWhile(P_ch).ToArray().Length); }
  public static void M_ch_LongCount() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.LongCount()); }
  public static void M_ch_Aggregate() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Aggregate(A_ch).GetHashCode()); }
  public static void M_ch_AggSeed() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Aggregate(a[0], A_ch).GetHashCode()); }
  public static void M_ch_Min() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_ch_Max() { char[] a = new char[]{'a','b'}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_dec_Where() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Where(P_dec).ToArray().Length); }
  public static void M_dec_WhereIdx() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Where(PI_dec).ToArray().Length); }
  public static void M_dec_Select() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Select(S_dec).ToArray().Length); }
  public static void M_dec_SelectIdx() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Select(SI_dec).ToArray().Length); }
  public static void M_dec_OrderBy() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.OrderBy(K_dec).ToArray().Length); }
  public static void M_dec_OrderByDesc() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.OrderByDescending(K_dec).ToArray().Length); }
  public static void M_dec_ThenBy() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.OrderBy(K_dec).ThenBy(K_dec).ToArray().Length); }
  public static void M_dec_GroupBy() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.GroupBy(K_dec).Count()); }
  public static void M_dec_ToList() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.ToList().Count); }
  public static void M_dec_ToArray() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.ToArray().Length); }
  public static void M_dec_First() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_dec_FirstOrDef() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_dec_Last() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_dec_LastOrDef() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_dec_Any() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Any()); }
  public static void M_dec_All() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.All(P_dec)); }
  public static void M_dec_Count() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Count()); }
  public static void M_dec_CountPred() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Count(P_dec)); }
  public static void M_dec_Zip() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Zip(a, Z_dec).ToArray().Length); }
  public static void M_dec_Take() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_dec_Skip() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_dec_Distinct() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_dec_Reverse() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_dec_Concat() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_dec_Union() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_dec_Intersect() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_dec_Except() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_dec_Contains() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_dec_SequenceEq() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_dec_Single() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_dec_SingleOrDef() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_dec_ElementAt() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_dec_DefaultIfEmpty() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_dec_TakeWhile() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.TakeWhile(P_dec).ToArray().Length); }
  public static void M_dec_SkipWhile() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.SkipWhile(P_dec).ToArray().Length); }
  public static void M_dec_LongCount() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.LongCount()); }
  public static void M_dec_Aggregate() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Aggregate(A_dec).GetHashCode()); }
  public static void M_dec_AggSeed() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Aggregate(a[0], A_dec).GetHashCode()); }
  public static void M_dec_Sum() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Sum().GetHashCode()); }
  public static void M_dec_SumSel() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Sum(S_dec).GetHashCode()); }
  public static void M_dec_Average() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Average().GetHashCode()); }
  public static void M_dec_AverageSel() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Average(S_dec).GetHashCode()); }
  public static void M_dec_Min() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_dec_MinSel() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Min(S_dec).GetHashCode()); }
  public static void M_dec_Max() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_dec_MaxSel() { decimal[] a = new decimal[]{1m,2m,3m}; Console.WriteLine(a.Max(S_dec).GetHashCode()); }
  public static void M_dt_Where() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Where(P_dt).ToArray().Length); }
  public static void M_dt_WhereIdx() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Where(PI_dt).ToArray().Length); }
  public static void M_dt_Select() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Select(S_dt).ToArray().Length); }
  public static void M_dt_SelectIdx() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Select(SI_dt).ToArray().Length); }
  public static void M_dt_OrderBy() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.OrderBy(K_dt).ToArray().Length); }
  public static void M_dt_OrderByDesc() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.OrderByDescending(K_dt).ToArray().Length); }
  public static void M_dt_ThenBy() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.OrderBy(K_dt).ThenBy(K_dt).ToArray().Length); }
  public static void M_dt_GroupBy() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.GroupBy(K_dt).Count()); }
  public static void M_dt_ToList() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.ToList().Count); }
  public static void M_dt_ToArray() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.ToArray().Length); }
  public static void M_dt_First() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_dt_FirstOrDef() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_dt_Last() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_dt_LastOrDef() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_dt_Any() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Any()); }
  public static void M_dt_All() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.All(P_dt)); }
  public static void M_dt_Count() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Count()); }
  public static void M_dt_CountPred() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Count(P_dt)); }
  public static void M_dt_Zip() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Zip(a, Z_dt).ToArray().Length); }
  public static void M_dt_Take() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_dt_Skip() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_dt_Distinct() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_dt_Reverse() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_dt_Concat() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_dt_Union() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_dt_Intersect() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_dt_Except() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_dt_Contains() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_dt_SequenceEq() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_dt_Single() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_dt_SingleOrDef() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_dt_ElementAt() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_dt_DefaultIfEmpty() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_dt_TakeWhile() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.TakeWhile(P_dt).ToArray().Length); }
  public static void M_dt_SkipWhile() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.SkipWhile(P_dt).ToArray().Length); }
  public static void M_dt_LongCount() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.LongCount()); }
  public static void M_dt_Aggregate() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Aggregate(A_dt).GetHashCode()); }
  public static void M_dt_AggSeed() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Aggregate(a[0], A_dt).GetHashCode()); }
  public static void M_dt_Min() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_dt_Max() { DateTime[] a = new DateTime[]{new DateTime(2020,1,2), new DateTime(2019,3,4)}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_ts_Where() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Where(P_ts).ToArray().Length); }
  public static void M_ts_WhereIdx() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Where(PI_ts).ToArray().Length); }
  public static void M_ts_Select() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Select(S_ts).ToArray().Length); }
  public static void M_ts_SelectIdx() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Select(SI_ts).ToArray().Length); }
  public static void M_ts_OrderBy() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.OrderBy(K_ts).ToArray().Length); }
  public static void M_ts_OrderByDesc() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.OrderByDescending(K_ts).ToArray().Length); }
  public static void M_ts_ThenBy() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.OrderBy(K_ts).ThenBy(K_ts).ToArray().Length); }
  public static void M_ts_GroupBy() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.GroupBy(K_ts).Count()); }
  public static void M_ts_ToList() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.ToList().Count); }
  public static void M_ts_ToArray() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.ToArray().Length); }
  public static void M_ts_First() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_ts_FirstOrDef() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_ts_Last() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_ts_LastOrDef() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_ts_Any() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Any()); }
  public static void M_ts_All() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.All(P_ts)); }
  public static void M_ts_Count() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Count()); }
  public static void M_ts_CountPred() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Count(P_ts)); }
  public static void M_ts_Zip() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Zip(a, Z_ts).ToArray().Length); }
  public static void M_ts_Take() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_ts_Skip() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_ts_Distinct() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_ts_Reverse() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_ts_Concat() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_ts_Union() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_ts_Intersect() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_ts_Except() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_ts_Contains() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_ts_SequenceEq() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_ts_Single() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_ts_SingleOrDef() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_ts_ElementAt() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_ts_DefaultIfEmpty() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_ts_TakeWhile() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.TakeWhile(P_ts).ToArray().Length); }
  public static void M_ts_SkipWhile() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.SkipWhile(P_ts).ToArray().Length); }
  public static void M_ts_LongCount() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.LongCount()); }
  public static void M_ts_Aggregate() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Aggregate(A_ts).GetHashCode()); }
  public static void M_ts_AggSeed() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Aggregate(a[0], A_ts).GetHashCode()); }
  public static void M_ts_Min() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_ts_Max() { TimeSpan[] a = new TimeSpan[]{TimeSpan.FromHours(1), TimeSpan.FromHours(2)}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_gd_Where() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Where(P_gd).ToArray().Length); }
  public static void M_gd_WhereIdx() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Where(PI_gd).ToArray().Length); }
  public static void M_gd_Select() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Select(S_gd).ToArray().Length); }
  public static void M_gd_SelectIdx() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Select(SI_gd).ToArray().Length); }
  public static void M_gd_OrderBy() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.OrderBy(K_gd).ToArray().Length); }
  public static void M_gd_OrderByDesc() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.OrderByDescending(K_gd).ToArray().Length); }
  public static void M_gd_ThenBy() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.OrderBy(K_gd).ThenBy(K_gd).ToArray().Length); }
  public static void M_gd_GroupBy() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.GroupBy(K_gd).Count()); }
  public static void M_gd_ToList() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.ToList().Count); }
  public static void M_gd_ToArray() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.ToArray().Length); }
  public static void M_gd_First() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_gd_FirstOrDef() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_gd_Last() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_gd_LastOrDef() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_gd_Any() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Any()); }
  public static void M_gd_All() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.All(P_gd)); }
  public static void M_gd_Count() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Count()); }
  public static void M_gd_CountPred() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Count(P_gd)); }
  public static void M_gd_Zip() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Zip(a, Z_gd).ToArray().Length); }
  public static void M_gd_Take() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_gd_Skip() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_gd_Distinct() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_gd_Reverse() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_gd_Concat() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_gd_Union() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_gd_Intersect() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_gd_Except() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_gd_Contains() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_gd_SequenceEq() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_gd_Single() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_gd_SingleOrDef() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_gd_ElementAt() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_gd_DefaultIfEmpty() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_gd_TakeWhile() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.TakeWhile(P_gd).ToArray().Length); }
  public static void M_gd_SkipWhile() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.SkipWhile(P_gd).ToArray().Length); }
  public static void M_gd_LongCount() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.LongCount()); }
  public static void M_gd_Aggregate() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Aggregate(A_gd).GetHashCode()); }
  public static void M_gd_AggSeed() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Aggregate(a[0], A_gd).GetHashCode()); }
  public static void M_gd_Min() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_gd_Max() { Guid[] a = new Guid[]{Guid.Empty, Guid.Empty}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void M_sr_Where() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Where(P_sr).ToArray().Length); }
  public static void M_sr_WhereIdx() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Where(PI_sr).ToArray().Length); }
  public static void M_sr_Select() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Select(S_sr).ToArray().Length); }
  public static void M_sr_SelectIdx() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Select(SI_sr).ToArray().Length); }
  public static void M_sr_OrderBy() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.OrderBy(K_sr).ToArray().Length); }
  public static void M_sr_OrderByDesc() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.OrderByDescending(K_sr).ToArray().Length); }
  public static void M_sr_ThenBy() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.OrderBy(K_sr).ThenBy(K_sr).ToArray().Length); }
  public static void M_sr_GroupBy() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.GroupBy(K_sr).Count()); }
  public static void M_sr_ToList() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.ToList().Count); }
  public static void M_sr_ToArray() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.ToArray().Length); }
  public static void M_sr_First() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.First().GetHashCode()); }
  public static void M_sr_FirstOrDef() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.FirstOrDefault().GetHashCode()); }
  public static void M_sr_Last() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Last().GetHashCode()); }
  public static void M_sr_LastOrDef() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.LastOrDefault().GetHashCode()); }
  public static void M_sr_Any() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Any()); }
  public static void M_sr_All() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.All(P_sr)); }
  public static void M_sr_Count() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Count()); }
  public static void M_sr_CountPred() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Count(P_sr)); }
  public static void M_sr_Zip() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Zip(a, Z_sr).ToArray().Length); }
  public static void M_sr_Take() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Take(1).ToArray().Length); }
  public static void M_sr_Skip() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Skip(1).ToArray().Length); }
  public static void M_sr_Distinct() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Distinct().ToArray().Length); }
  public static void M_sr_Reverse() { string[] a = new string[]{"b","A"}; Console.WriteLine(Enumerable.Reverse(a).ToArray().Length); }
  public static void M_sr_Concat() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Concat(a).ToArray().Length); }
  public static void M_sr_Union() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Union(a).ToArray().Length); }
  public static void M_sr_Intersect() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Intersect(a).ToArray().Length); }
  public static void M_sr_Except() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Except(a).ToArray().Length); }
  public static void M_sr_Contains() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Contains(a[0])); }
  public static void M_sr_SequenceEq() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.SequenceEqual(a)); }
  public static void M_sr_Single() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Single().GetHashCode()); }
  public static void M_sr_SingleOrDef() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.SingleOrDefault().GetHashCode()); }
  public static void M_sr_ElementAt() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.ElementAt(0).GetHashCode()); }
  public static void M_sr_DefaultIfEmpty() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.DefaultIfEmpty().ToArray().Length); }
  public static void M_sr_TakeWhile() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.TakeWhile(P_sr).ToArray().Length); }
  public static void M_sr_SkipWhile() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.SkipWhile(P_sr).ToArray().Length); }
  public static void M_sr_LongCount() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.LongCount()); }
  public static void M_sr_Aggregate() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Aggregate(A_sr).GetHashCode()); }
  public static void M_sr_AggSeed() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Aggregate(a[0], A_sr).GetHashCode()); }
  public static void M_sr_Min() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Min().GetHashCode()); }
  public static void M_sr_Max() { string[] a = new string[]{"b","A"}; Console.WriteLine(a.Max().GetHashCode()); }
  public static void Main() { M_i4_Where(); }
}}
