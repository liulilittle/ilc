// exprobe.cs — 异常族（throw 重抛 / catch 过滤 / finally 确定性）net48 实证。
// 关键：throw; 保留原异常与堆栈 / throw ex; 重置堆栈；catch-when 过滤先于 handler 求值、
// 过滤为假不进入 handler、过滤内抛异常传播过滤异常；finally 全路径确定性。
using System;
using System.Runtime.CompilerServices;

class ExProbe
{
    static void P(string s) => Console.WriteLine(s);
    static void Pb(string label, bool v) => P(label + " = " + (v ? 1 : 0));

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void InnerThrow() { throw new InvalidOperationException("orig"); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void RethrowOrig() { try { InnerThrow(); } catch (Exception e) { throw; } }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void RethrowNew() { try { InnerThrow(); } catch (Exception e) { throw e; } }

    static bool Filter(Exception e) { P("  filter: " + e.Message); return e.Message == "boom"; }
    static bool FilterFalse(Exception e) { P("  filter(false)"); return false; }

    static void Main()
    {
        // ---- catch 类型匹配：精确类型优先于基类 ----
        P("-- 多重 catch 顺序 --");
        try { throw new InvalidOperationException("x"); }
        catch (InvalidOperationException) { P("catch: InvalidOperationException"); }
        catch (Exception) { P("catch: Exception"); }
        try { throw new Exception("y"); }
        catch (InvalidOperationException) { P("catch: InvalidOperationException"); }
        catch (Exception) { P("catch: Exception"); }

        // ---- throw; vs throw ex;：堆栈轨迹 ----
        P("-- rethrow --");
        try { RethrowOrig(); }
        catch (Exception e)
        {
            Pb("throw; 保留原始堆栈（含 InnerThrow）", e.StackTrace.Contains("InnerThrow"));
            Pb("throw; 消息保留", e.Message == "orig");
        }
        try { RethrowNew(); }
        catch (Exception e)
        {
            Pb("throw ex; 堆栈被重置（不含 InnerThrow）", !e.StackTrace.Contains("InnerThrow"));
            Pb("throw ex; 消息保留", e.Message == "orig");
        }

        // ---- catch 内抛新异常：InnerException 链 ----
        P("-- 抛新异常包装 --");
        try
        {
            try { InnerThrow(); }
            catch (Exception e) { throw new ArgumentException("wrapped", e); }
        }
        catch (Exception e)
        {
            P("外层: " + e.GetType().Name);
            P("InnerException: " + (e.InnerException != null ? e.InnerException.GetType().Name : "<null>"));
        }

        // ---- catch-when 过滤：真 → handler；假 → 不进入（向外传播）----
        P("-- catch 过滤 --");
        try
        {
            P("  try");
            throw new Exception("boom");
        }
        catch (Exception e) when (Filter(e)) { P("  handler: caught"); }
        finally { P("  finally"); }
        try
        {
            try { throw new Exception("nope"); }
            catch (Exception e) when (FilterFalse(e)) { P("  inner-handler"); }
        }
        catch (Exception e) { P("outer-handler: " + e.Message); }

        // ---- 过滤内抛异常：传播过滤的异常（原异常被替换）----
        P("-- 过滤抛异常 --");
        try
        {
            try { throw new Exception("original"); }
            catch (Exception e) when (ThrowingFilter(e)) { P("  inner-handler"); }
        }
        catch (Exception e) { P("outer: " + e.GetType().Name + " / " + e.Message); }

        // ---- finally 确定性：正常 / 异常 / return 全执行 ----
        P("-- finally 全路径 --");
        TryFinally(0);    // 正常
        try { TryFinally(1); } catch (Exception e) { P("  caught: " + e.GetType().Name); }
        P("finally+return 返回值: " + FinallyReturn());

        // ---- 嵌套 try/catch/finally 顺序：try → catch → finally ----
        P("-- try/catch/finally 顺序 --");
        try
        {
            P("  try");
            throw new Exception("z");
        }
        catch (Exception) { P("  catch"); }
        finally { P("  finally"); }
    }

    static bool ThrowingFilter(Exception e)
    {
        P("  filter throws");
        throw new ArgumentNullException("filter-arg");
    }

    static void TryFinally(int mode)
    {
        try
        {
            if (mode == 1) throw new Exception("boom");
            P("  try: ok");
        }
        finally { P("  finally: ran"); }
    }

    static int FinallyReturn()
    {
        try { return 42; }
        finally { P("  finally: ran before return"); }
    }
}
