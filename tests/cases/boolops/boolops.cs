//------------------------------------------------------------------------------
// boolops.cs -- BOOLEAN OPERATIONS DEEP e2e reference case (net48 oracle,
// byte-for-byte). Created in e2e batch C81, AFTER the bool domain closure.
//
// Goes deeper than ifcase (C17: the comparison matrix, &&/||/ternary basics,
// short-circuit side effects) on the WIRED boolean surface (IL branches:
// brtrue/brfalse + the branch fold). THIS case drives the LOWER-LEVEL
// boolean plumbing that later cases assume:
//
//   1. && / || TRUTH TABLES -- all 4 rows of each op as PLAIN bool
//      expressions (not via branches); the operands are bool locals fed from
//      comparisons, so the fold sees bool->bool, not int->branch.
//   2. SHORT-CIRCUIT EXCEPTION AVOIDANCE -- `false && (100 / 0 > 5)` must
//      NOT evaluate the division (a naive both-sides emit would throw
//      DivideByZero). The probe's result is a bool, the guard is the
//      operand order, not a catch.
//   3. SHORT-CIRCUIT ORDERING with method-call side effects on BOTH sides
//      (the call order is observable through a static counter).
//   4. TERNARY VALUE-TYPE MATRIX -- ?: returning int / string / double /
//      bool (value ternary, no boxed-object arms), used as an expression
//      operand (x + (c ? a : b)) and as a return value.
//   5. NESTED TERNARY -- 2-deep and 3-deep ?: chains with mixed condition
//      operators (!=, <, ==), evaluated to constants and to locals.
//   6. NEGATION -- ! on bool locals / comparison results, !! double
//      negation, ! on a ternary result.
//   7. BOOL AS DATA -- bool static field, bool array element, bool method
//      parameter and return value (the 0/1 bool ABI through fields/arrays/
//      calls).
//   8. PRECEDENCE -- a && b || c && d (&& binds tighter), a || b && c,
//      (a || b) && c with explicit parens (all 8 truth rows).
//
// Byte-stability: every line prints "label=True/False" or a fixed int;
// net48 is the byte reference. No exception objects, no boxing of the
// ternary arms, no unwired members.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class BoolOps {
    // ---- side-effect counters for short-circuit ordering ----
    static int gCount = 0;
    static bool Touch(string tag)
    {
        gCount++;
        return tag == "on";
    }

    // ---- bool as method param/return (0/1 ABI) ----
    static bool And2(bool a, bool b) { return a && b; }
    static bool Or2(bool a, bool b) { return a || b; }
    static bool Not2(bool a) { return !a; }
    static bool Pick(bool c, bool t, bool f) { return c ? t : f; }

    // ---- bool as field data ----
    static bool sFlag = true;

    public static void Main()
    {
        // ============ 1. && / || truth tables (plain bool operands) ============
        bool t = true, f = false;
        Console.WriteLine("and_tt=" + (t && t));  // True
        Console.WriteLine("and_tf=" + (t && f));  // False
        Console.WriteLine("and_ft=" + (f && t));  // False
        Console.WriteLine("and_ff=" + (f && f));  // False
        Console.WriteLine("or_tt=" + (t || t));   // True
        Console.WriteLine("or_tf=" + (t || f));   // True
        Console.WriteLine("or_ft=" + (f || t));   // True
        Console.WriteLine("or_ff=" + (f || f));   // False

        // operands fed from comparisons (bool->bool fold)
        int a = 3, b = 5;
        Console.WriteLine("cmp_and=" + ((a > b) && (a < b)));   // False
        Console.WriteLine("cmp_or=" + ((a > b) || (a < b)));    // True
        Console.WriteLine("cmp_and2=" + ((a < b) && (b > a)));  // True

        // ============ 2. short-circuit exception avoidance ============
        // The GUARD operand is on the LEFT in every probe: `false && (100/0)`
        // and `true || (100/0)` must never evaluate the division (a naive
        // both-sides emit would throw DivideByZero). Written via a nonzero
        // guard local so csc does not constant-fold it away.
        int zero = 0;
        Console.WriteLine("sc_div_and=" + (f && (100 / zero > 5)));   // False (no throw)
        Console.WriteLine("sc_div_or=" + (t || (100 / zero > 5)));    // True  (no throw)
        Console.WriteLine("sc_div_and2=" + (f && (100 / zero > 5) && t)); // False (chain short-circuits at the first false)
        Console.WriteLine("sc_div_or2=" + (t || (100 / zero > 5) || f));  // True  (chain short-circuits at the first true)

        // ============ 3. short-circuit ordering (side-effect order) ============
        // Touch("on") returns true, Touch("off") returns false.
        gCount = 0;
        bool r1 = Touch("off") && Touch("on");   // && short-circuits: only "off"
        Console.WriteLine("sc_order_and=" + (r1 == false && gCount == 1));
        gCount = 0;
        bool r2 = Touch("on") || Touch("off");   // || short-circuits: only "on"
        Console.WriteLine("sc_order_or=" + (r2 == true && gCount == 1));
        gCount = 0;
        bool r3 = Touch("on") && Touch("off");   // both sides run
        Console.WriteLine("sc_order_and2=" + (r3 == false && gCount == 2));
        gCount = 0;
        bool r4 = Touch("off") || Touch("on");   // both sides run
        Console.WriteLine("sc_order_or2=" + (r4 == true && gCount == 2));

        // ============ 4. ternary value-type matrix ============
        Console.WriteLine("tern_int=" + (a < b ? 7 : 9));           // 7
        Console.WriteLine("tern_int2=" + (a > b ? 7 : 9));          // 9
        Console.WriteLine("tern_str=" + (a < b ? "lo" : "hi"));     // lo
        Console.WriteLine("tern_str2=" + (a > b ? "lo" : "hi"));    // hi
        Console.WriteLine("tern_dbl=" + (a < b ? 1.5 : 2.5));       // 1.5
        Console.WriteLine("tern_bool=" + (a < b ? true : false));   // True
        Console.WriteLine("tern_bool2=" + (a > b ? true : false));  // False
        // ternary as an expression operand
        int c = 10;
        Console.WriteLine("tern_expr=" + (c + (a < b ? 1 : 2)));    // 11
        Console.WriteLine("tern_expr2=" + (c + (a > b ? 1 : 2)));   // 12
        Console.WriteLine("tern_mul=" + ((a < b ? 3 : 4) * 5));     // 15

        // ============ 5. nested ternary ============
        Console.WriteLine("nest_222=" + (a < b ? (a == 3 ? 11 : 12) : 13));      // 11
        Console.WriteLine("nest_223=" + (a < b ? (a != 3 ? 11 : 12) : 13));      // 12
        Console.WriteLine("nest_else=" + (a > b ? 13 : (a == 3 ? 14 : 15)));     // 14
        Console.WriteLine("nest_3deep=" + (a < b ? 1 : a > b ? 2 : a == 3 ? 3 : 4)); // 1
        Console.WriteLine("nest_3deep2=" + (a > b ? 1 : a == b ? 2 : a == 3 ? 3 : 4)); // 3

        // ============ 6. negation ============
        Console.WriteLine("not_loc=" + (!t));                       // False
        Console.WriteLine("not_cmp=" + (!(a < b)));                 // False
        Console.WriteLine("not_cmp2=" + (!(a > b)));                // True
        Console.WriteLine("not_not=" + (!!t));                      // True
        Console.WriteLine("not_not2=" + (!!f));                     // False
        Console.WriteLine("not_tern=" + (!(a < b ? true : false))); // False
        Console.WriteLine("not_mix=" + (!(a > b) && !(a == b)));    // True

        // ============ 7. bool as data (field/param/return) ============
        Console.WriteLine("field=" + (sFlag == true));              // True
        sFlag = !sFlag;
        Console.WriteLine("field2=" + (sFlag == false));            // True
        // bool flags stored in an int[] (0/1 -- the bool ABI in a supported
        // newarr element type; bool[] itself is not a wired newarr element,
        // see GAPS D-BOOL-1).
        int[] flags = { 1, 0, 1, 0 };
        Console.WriteLine("arr0=" + (flags[0] == 1));               // True
        Console.WriteLine("arr1=" + (flags[1] == 0));               // True
        Console.WriteLine("arr_cmp=" + ((flags[0] == 1) && (flags[1] == 0))); // True
        Console.WriteLine("arr_sum=" + ((flags[0] == 1) || (flags[1] == 1) || (flags[2] == 1))); // True
        Console.WriteLine("arr_neg=" + (flags[3] == 0));            // True
        Console.WriteLine("param_and=" + And2(true, false));        // False
        Console.WriteLine("param_and2=" + And2(true, true));        // True
        Console.WriteLine("param_or=" + Or2(false, true));          // True
        Console.WriteLine("param_not=" + Not2(false));              // True
        Console.WriteLine("param_pick=" + Pick(true, true, false)); // True
        Console.WriteLine("param_pick2=" + Pick(false, true, false)); // False

        // ============ 8. precedence ============
        // a && b || c && d   (&& binds tighter than ||)
        Console.WriteLine("prec_1=" + (t && f || t && t));   // True
        Console.WriteLine("prec_2=" + (f && t || t && t));   // True
        Console.WriteLine("prec_3=" + (t && f || f && t));   // False
        Console.WriteLine("prec_4=" + (f && t || f && t));   // False
        // a || b && c
        Console.WriteLine("prec5=" + (t || t && f));         // True
        Console.WriteLine("prec6=" + (f || t && f));         // False
        Console.WriteLine("prec7=" + (f || t && t));         // True
        // (a || b) && c  -- parens change the grouping
        Console.WriteLine("prec8=" + ((t || f) && f));       // False
        Console.WriteLine("prec9=" + ((f || t) && t));       // True
        Console.WriteLine("prec10=" + ((f || f) && t));      // False
        // mixed negation + precedence
        Console.WriteLine("prec11=" + (!(t && f) && (f || t))); // True
        Console.WriteLine("prec12=" + (!(t && t) || !(f && f))); // True
    }
}}
