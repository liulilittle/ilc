// swprobe.cs — 控制流族（switch 表 + leave/endfinally）net48 实证。
// 关键：switch 越界/负值 → default；long switch = 比较链非跳表；leave 退出 try 必执行 finally；
// 嵌套 finally LIFO；return/break/continue 各退出路径的 finally 确定性。
using System;

class SwProbe
{
    static void P(string s) => Console.WriteLine(s);
    static void Pi(string label, int v) => P(label + " = " + v);

    static int SwInt(int x)
    {
        // 连续整数 → IL switch（跳转表）
        switch (x)
        {
            case 0: return 100;
            case 1: return 101;
            case 2: return 102;
            default: return -1;
        }
    }

    static void Main()
    {
        Pi("switch(0)", SwInt(0));
        Pi("switch(1)", SwInt(1));
        Pi("switch(2)", SwInt(2));
        Pi("switch(3) [default]", SwInt(3));
        Pi("switch(-1) [default]", SwInt(-1));
        Pi("switch(100) [default]", SwInt(100));

        // switch 稀疏 + 负 case：不连续区间走比较链（il2cpp 转译器需注意）
        Pi("sparse(5)", Sparse(5));
        Pi("sparse(-3)", Sparse(-3));
        Pi("sparse(7) [default]", Sparse(7));

        // long switch（C# 7 起合法；IL 为比较链非 switch 指令）
        Pi("longSwitch(5L)", LongSwitch(5L));
        Pi("longSwitch(99L) [default]", LongSwitch(99L));

        // goto case（同一跳表）
        Pi("gotoCase(1)", GotoCase(1));

        // ---- leave/finally：return 退出 try → finally 仍执行 ----
        Pi("retThroughFinally(1)", RetThroughFinally(1));
        P("-- retThroughFinally(2) 异常路径 --");
        try { Pi("retThroughFinally(2)", RetThroughFinally(2)); }
        catch (Exception e) { P("caught: " + e.GetType().Name); }

        // ---- finally 在异常传播前执行（try/finally 无 catch）----
        P("-- try/finally 异常传播顺序 --");
        try { ThrowInTryFinally(); }
        catch (Exception e) { P("caught: " + e.GetType().Name); }

        // ---- 嵌套 finally：LIFO（异常被外层 catch 接住，程序正常退出）----
        P("-- 嵌套 finally LIFO --");
        try
        {
            try { throw new Exception("inner"); }
            finally { P("finally: inner"); }
        }
        catch (Exception e) { P("caught: " + e.GetType().Name); }
        finally { P("finally: outer"); }
        P("-- end --");

        // ---- break/continue 在循环内 try 的 finally ----
        P("-- 循环内 try/finally break --");
        for (int i = 0; i < 3; i++)
        {
            try
            {
                if (i == 1) break;
                P("iter " + i);
            }
            finally { P("finally iter " + i); }
        }
        P("-- 循环内 try/finally continue --");
        for (int i = 0; i < 3; i++)
        {
            try
            {
                if (i == 1) continue;
                P("iter " + i);
            }
            finally { P("finally iter " + i); }
        }
        P("-- done --");
    }

    static int Sparse(int x)
    {
        switch (x)
        {
            case 5: return 50;
            case -3: return -30;
            default: return -1;
        }
    }

    static int LongSwitch(long x)
    {
        switch (x)
        {
            case 5L: return 50;
            case 6L: return 60;
            default: return -1;
        }
    }

    static int GotoCase(int x)
    {
        switch (x)
        {
            case 1: goto case 2;
            case 2: return 20;
            default: return -1;
        }
    }

    static int RetThroughFinally(int mode)
    {
        try
        {
            if (mode == 2) throw new Exception("boom");
            P("try: mode=" + mode);
            return mode * 10;      // leave：finally 先执行，返回值为 10
        }
        finally
        {
            P("finally: 执行于 return/异常之前");
        }
    }

    static void ThrowInTryFinally()
    {
        try { P("try: 抛异常"); throw new InvalidOperationException("x"); }
        finally { P("finally: 异常传播前"); }
    }
}
