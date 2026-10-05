using System;
namespace Cases { public static class IfCase {
    // ---- comparison matrix: < <= > >= == != over int boundaries ----
    static bool Lt(int a, int b) { if (a < b) return true; return false; }
    static bool Le(int a, int b) { if (a <= b) return true; return false; }
    static bool Gt(int a, int b) { if (a > b) return true; return false; }
    static bool Ge(int a, int b) { if (a >= b) return true; return false; }
    static bool Eq(int a, int b) { if (a == b) return true; return false; }
    static bool Ne(int a, int b) { if (a != b) return true; return false; }

    // ---- else-if chain: sign classification ----
    static int Sign(int x) {
        if (x < 0) return -1;
        else if (x == 0) return 0;
        else if (x > 0) return 1;
        else return 99;
    }

    // ---- three-way comparison: == / < / > disjoint branches ----
    static int Cmp(int a, int b) {
        if (a == b) return 0;
        if (a < b) return -1;
        if (a > b) return 1;
        return 7;
    }

    // ---- range membership via && (both sides must hold) ----
    static bool InRange(int lo, int hi, int v) {
        if (v >= lo && v <= hi) return true;
        return false;
    }

    // ---- range exclusion via || (either side suffices) ----
    static bool OutRange(int lo, int hi, int v) {
        if (v < lo || v > hi) return true;
        return false;
    }

    // ---- multi-term && / || chains ----
    static bool AllPos(int a, int b, int c) {
        return a > 0 && b > 0 && c > 0;
    }
    static bool AnyPos(int a, int b, int c) {
        return a > 0 || b > 0 || c > 0;
    }

    // ---- mixed precedence: (a && b) || (c && d) ----
    static bool BothPair(int a, int b, int c, int d) {
        return (a > 0 && b > 0) || (c > 0 && d > 0);
    }

    // ---- negation of a compound condition ----
    static bool NotBothPos(int a, int b) {
        return !(a > 0 && b > 0);
    }
    static bool Neither(int a, int b) {
        return !(a > 0) && !(b > 0);
    }
    static bool OneOf(int a, int b) {
        return (a > 0) != (b > 0);
    }

    // ---- nested if/else diamond: all four outcome cells ----
    static int Nested(int a, int b) {
        if (a > 0) {
            if (b > 0) return 1;
            else return 2;
        } else {
            if (b > 0) return 3;
            else return 4;
        }
    }

    // ---- three-deep nesting ----
    static int Deep(int a, int b, int c) {
        if (a > 0) {
            if (b > 0) {
                if (c > 0) return 1;
                else return 2;
            } else return 3;
        } else return 4;
    }

    // ---- conditional expression (ternary) ----
    static int Tern(int a, int b) {
        return a > b ? a - b : b - a;
    }
    static int TernChain(bool a, bool b, bool c) {
        return a ? 1 : (b ? 2 : (c ? 3 : 4));
    }
    static bool TernCond(bool a, bool b) {
        return a ? b : false;
    }

    // ---- branch takes an arithmetic-condition result ----
    static int ArithCond(int a, int b, int c) {
        if (a + b > c) return 1;
        return 0;
    }

    // ---- bool method call in condition position ----
    static bool Called(bool c) { return c; }
    static int IfCall(bool c) {
        if (Called(c)) return 5;
        return 6;
    }

    // ---- short-circuit && / || with observable side effects ----
    static bool flag = false;
    static bool SetFlag() { flag = true; return true; }
    static bool ClearFlag() { flag = false; return false; }
    // ClearFlag() is false -> SetFlag() must NOT run (flag stays false).
    static bool AndLazyLeft() { flag = false; bool r = ClearFlag() && SetFlag(); return r == false && flag == false; }
    // SetFlag() is true -> ClearFlag() must run (flag ends false).
    static bool AndRunRight() { flag = false; bool r = SetFlag() && ClearFlag(); return r == false && flag == false; }
    // SetFlag() is true -> ClearFlag() must NOT run (flag stays true).
    static bool OrLazyLeft() { flag = false; bool r = SetFlag() || ClearFlag(); return r == true && flag == true; }
    // ClearFlag() is false -> SetFlag() must run (flag ends true).
    static bool OrRunRight() { flag = false; bool r = ClearFlag() || SetFlag(); return r == true && flag == true; }
    // (ClearFlag() && SetFlag()) || true: && short-circuits, || then folds true.
    static bool LazyMixed() { flag = false; bool r = ClearFlag() && SetFlag() || true; return r == true && flag == false; }

    // ---- condition with assignment only in the taken branch ----
    static bool CondAssign(bool c) {
        bool r = false;
        if (c) r = true;
        return r;
    }
    static int CondInc(int x) {
        int r = 0;
        if (x > 0) r = x + 1;
        return r;
    }

    // ---- early return vs fall-through ----
    static int Early(int x) {
        if (x == 1) return 10;
        if (x == 2) return 20;
        return 30;
    }

    public static void Main() {
        Console.WriteLine(Lt(-5, 0));
        Console.WriteLine(Lt(0, 0));
        Console.WriteLine(Lt(3, 2));
        Console.WriteLine(Le(-5, 0));
        Console.WriteLine(Le(0, 0));
        Console.WriteLine(Le(3, 2));
        Console.WriteLine(Gt(-5, 0));
        Console.WriteLine(Gt(0, 0));
        Console.WriteLine(Gt(3, 2));
        Console.WriteLine(Ge(-5, 0));
        Console.WriteLine(Ge(0, 0));
        Console.WriteLine(Ge(3, 2));
        Console.WriteLine(Eq(-1, -1));
        Console.WriteLine(Eq(0, 0));
        Console.WriteLine(Eq(1, 2));
        Console.WriteLine(Ne(-1, -1));
        Console.WriteLine(Ne(0, 0));
        Console.WriteLine(Ne(1, 2));
        Console.WriteLine(Eq(int.MaxValue, int.MaxValue));
        Console.WriteLine(Ne(int.MaxValue, int.MinValue));
        Console.WriteLine(Sign(-100));
        Console.WriteLine(Sign(-1));
        Console.WriteLine(Sign(0));
        Console.WriteLine(Sign(1));
        Console.WriteLine(Sign(100));
        Console.WriteLine(Cmp(-3, 3));
        Console.WriteLine(Cmp(0, 0));
        Console.WriteLine(Cmp(3, -3));
        Console.WriteLine(InRange(0, 10, -1));
        Console.WriteLine(InRange(0, 10, 0));
        Console.WriteLine(InRange(0, 10, 5));
        Console.WriteLine(InRange(0, 10, 10));
        Console.WriteLine(InRange(0, 10, 11));
        Console.WriteLine(OutRange(0, 10, -1));
        Console.WriteLine(OutRange(0, 10, 0));
        Console.WriteLine(OutRange(0, 10, 5));
        Console.WriteLine(OutRange(0, 10, 10));
        Console.WriteLine(OutRange(0, 10, 11));
        Console.WriteLine(AllPos(1, 2, 3));
        Console.WriteLine(AllPos(1, -2, 3));
        Console.WriteLine(AllPos(1, 2, -3));
        Console.WriteLine(AllPos(-1, -2, -3));
        Console.WriteLine(AnyPos(1, 2, 3));
        Console.WriteLine(AnyPos(1, -2, -3));
        Console.WriteLine(AnyPos(-1, 2, -3));
        Console.WriteLine(AnyPos(-1, -2, -3));
        Console.WriteLine(BothPair(1, 1, 0, 0));
        Console.WriteLine(BothPair(1, -1, 1, 1));
        Console.WriteLine(BothPair(1, -1, 1, -1));
        Console.WriteLine(BothPair(-1, -1, -1, -1));
        Console.WriteLine(NotBothPos(1, 1));
        Console.WriteLine(NotBothPos(1, -1));
        Console.WriteLine(NotBothPos(-1, -1));
        Console.WriteLine(Neither(-1, -1));
        Console.WriteLine(Neither(-1, 1));
        Console.WriteLine(Neither(1, -1));
        Console.WriteLine(OneOf(-1, 1));
        Console.WriteLine(OneOf(1, -1));
        Console.WriteLine(OneOf(-1, -1));
        Console.WriteLine(OneOf(1, 1));
        Console.WriteLine(Nested(1, 1));
        Console.WriteLine(Nested(1, -1));
        Console.WriteLine(Nested(-1, 1));
        Console.WriteLine(Nested(-1, -1));
        Console.WriteLine(Nested(0, 0));
        Console.WriteLine(Deep(1, 1, 1));
        Console.WriteLine(Deep(1, 1, -1));
        Console.WriteLine(Deep(1, -1, 1));
        Console.WriteLine(Deep(1, -1, -1));
        Console.WriteLine(Deep(-1, 1, 1));
        Console.WriteLine(Deep(-1, 1, -1));
        Console.WriteLine(Deep(-1, -1, 1));
        Console.WriteLine(Deep(-1, -1, -1));
        Console.WriteLine(Tern(7, 3));
        Console.WriteLine(Tern(3, 7));
        Console.WriteLine(Tern(5, 5));
        Console.WriteLine(TernChain(true, false, false));
        Console.WriteLine(TernChain(false, true, false));
        Console.WriteLine(TernChain(false, false, true));
        Console.WriteLine(TernChain(false, false, false));
        Console.WriteLine(TernCond(true, true));
        Console.WriteLine(TernCond(true, false));
        Console.WriteLine(TernCond(false, true));
        Console.WriteLine(TernCond(false, false));
        Console.WriteLine(ArithCond(2, 3, 4));
        Console.WriteLine(ArithCond(2, 3, 6));
        Console.WriteLine(IfCall(true));
        Console.WriteLine(IfCall(false));
        Console.WriteLine(AndLazyLeft());
        Console.WriteLine(AndRunRight());
        Console.WriteLine(OrLazyLeft());
        Console.WriteLine(OrRunRight());
        Console.WriteLine(LazyMixed());
        Console.WriteLine(CondAssign(true));
        Console.WriteLine(CondAssign(false));
        Console.WriteLine(CondInc(5));
        Console.WriteLine(CondInc(-5));
        Console.WriteLine(CondInc(0));
        Console.WriteLine(Early(1));
        Console.WriteLine(Early(2));
        Console.WriteLine(Early(3));
    }
}}
