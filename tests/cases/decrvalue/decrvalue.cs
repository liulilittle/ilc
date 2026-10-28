//------------------------------------------------------------------------------
// decrvalue.cs -- decimal PURE-VALUE operands that are NOT materialized into a
// value slot before the icall (e2e lock for the "taking address of rvalue"
// emitter gap, docs/audit/b5-gaps.md).
//
// Every decimal icall takes `const Il2CppDecimal*` (a 16-byte struct cannot
// ride the value channel), so the emitter addresses each operand. An operand
// that is a PRVALUE has no address: `&(f())` is ill-formed C++. The registered
// shape is a decimal comparison whose operand is a decimal-returning delegate
// Invoke (`f() == 2.5m`), the only decimal-producing expression the stack fold
// leaves inline; the same cast serves the arithmetic family, the conversions,
// and the instance members.
//
// Coverage:
//   S1 static-call results (materialized value slots -- the control shape)
//   S2 delegate Invoke operands: == / != / < / > / <= / >= (equal, less,
//      greater, zero, negative) + CompareTo / Equals / ToString / GetHashCode
//   S3 arithmetic on prvalue operands: + - * / % and unary negation
//   S4 conversion of a prvalue (op_Explicit decimal -> int32/int64/double)
//   S5 a closure-captured decimal (ldflda operands -- the address shape)
//   S6 error path: a div-by-zero prvalue operand throws DivideByZeroException
//
// net48 is the byte reference (scripts/difftest.ps1).
//------------------------------------------------------------------------------
using System;
using System.Globalization;

namespace Cases {
    public static class DecRvalue {
        private static decimal Add(decimal x, decimal y) { return x + y; }
        private static decimal Sub(decimal x, decimal y) { return x - y; }

        // ---------- S1: static-call results (materialized slots) ----------
        private static void S1Calls() {
            Console.WriteLine("s1_eq=" + (Add(10m, 3m) == Sub(3m, 10m)));
            Console.WriteLine("s1_lt=" + (Sub(3m, 10m) < Add(10m, 3m)));
            Console.WriteLine("s1_ge=" + (Add(10m, 3m) >= 13m));
            Console.WriteLine("s1_sum=" + (Add(10m, 3m) + Sub(3m, 10m)));
            Console.WriteLine("s1_cmp=" + Add(10m, 3m).CompareTo(Sub(3m, 10m)));
        }

        // ---------- S2: delegate Invoke operands (PRVALUES) ----------
        private static void S2DelegateOperands() {
            Func<decimal> p = () => 10m;
            Func<decimal> q = () => 3m;
            Func<decimal> ten = () => 10m;
            Func<decimal> z = () => 0m;
            Func<decimal> neg = () => -1m;

            Console.WriteLine("s2_eq=" + (p() == q()));
            Console.WriteLine("s2_eqSelf=" + (p() == ten()));
            Console.WriteLine("s2_ne=" + (p() != q()));
            Console.WriteLine("s2_neSelf=" + (p() != ten()));
            Console.WriteLine("s2_lt=" + (p() < q()));
            Console.WriteLine("s2_ltRev=" + (q() < p()));
            Console.WriteLine("s2_gt=" + (p() > q()));
            Console.WriteLine("s2_gtRev=" + (q() > p()));
            Console.WriteLine("s2_le=" + (p() <= ten()));
            Console.WriteLine("s2_leLt=" + (q() <= p()));
            Console.WriteLine("s2_ge=" + (p() >= ten()));
            Console.WriteLine("s2_geGt=" + (p() >= q()));
            // zero / negative boundaries
            Console.WriteLine("s2_zero=" + (z() == 0m));
            Console.WriteLine("s2_zeroNe=" + (z() != 0.00m));
            Console.WriteLine("s2_negLt=" + (neg() < z()));
            Console.WriteLine("s2_negEq=" + (neg() == -1m));
            Console.WriteLine("s2_negGt=" + (z() > neg()));
            // the constant on the LEFT, the prvalue on the RIGHT
            Console.WriteLine("s2_constLeft=" + (13m == Add(10m, 3m)));
            Console.WriteLine("s2_constLeftLt=" + (0m < neg()));
            // instance members on prvalue operands
            Console.WriteLine("s2_cmp=" + p().CompareTo(q()));
            Console.WriteLine("s2_cmpEq=" + p().CompareTo(ten()));
            Console.WriteLine("s2_cmpNeg=" + neg().CompareTo(z()));
            Console.WriteLine("s2_equals=" + p().Equals(ten()));
            Console.WriteLine("s2_equalsNe=" + p().Equals(q()));
            Console.WriteLine("s2_hashEq=" + (p().GetHashCode() == ten().GetHashCode()));
            Console.WriteLine("s2_hashNe=" + (p().GetHashCode() == q().GetHashCode()));
            Console.WriteLine("s2_str=" + p().ToString());
            Console.WriteLine("s2_strInv=" + p().ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("s2_strNeg=" + neg().ToString());
            Console.WriteLine("s2_decCmp=" + decimal.Compare(p(), q()));
            Console.WriteLine("s2_decCmpEq=" + decimal.Compare(p(), ten()));
        }

        // ---------- S3: arithmetic on prvalue operands ----------
        private static void S3Arithmetic() {
            Func<decimal> p = () => 10m;
            Func<decimal> q = () => 4m;
            Console.WriteLine("s3_add=" + (p() + q()));
            Console.WriteLine("s3_sub=" + (p() - q()));
            Console.WriteLine("s3_mul=" + (p() * q()));
            Console.WriteLine("s3_div=" + (p() / q()));
            Console.WriteLine("s3_rem=" + (p() % q()));
            Console.WriteLine("s3_neg=" + (-p()));
            Console.WriteLine("s3_mulConst=" + (p() * 2m));
            Console.WriteLine("s3_chain=" + (p() + q() * q()));
            Console.WriteLine("s3_named=" + decimal.Divide(p(), q()) + "/" + decimal.Multiply(p(), q()));
            Console.WriteLine("s3_addEq=" + ((p() + q()) == 14m));
            Console.WriteLine("s3_addLt=" + ((p() + q()) < p() * q()));
        }

        // ---------- S4: conversion of a prvalue ----------
        private static void S4Convert() {
            Func<decimal> p = () => 10.75m;
            Func<decimal> neg = () => -1.5m;
            Console.WriteLine("s4_i32=" + (int)p());
            Console.WriteLine("s4_i64=" + (long)p());
            Console.WriteLine("s4_i32trunc=" + (int)neg());
            Console.WriteLine("s4_dbl=" + ((double)p() == 10.75));
            Console.WriteLine("s4_i32cmp=" + ((int)p() == 10));
        }

        // ---------- S5: closure-captured decimal (ldflda operands) ----------
        private static void S5Captured() {
            decimal d = 5.5m;
            Func<bool> cmpSame = () => d.CompareTo(5.5m) == 0;
            Func<bool> cmpLess = () => d.CompareTo(6m) < 0;
            Func<bool> eq = () => d.Equals(5.5m);
            Func<bool> eqNe = () => d.Equals(4m);
            Func<bool> lt = () => d < 6m;
            Func<bool> sumEq = () => d + 1m == 6.5m;
            Console.WriteLine("s5_cmpSame=" + cmpSame());
            Console.WriteLine("s5_cmpLess=" + cmpLess());
            Console.WriteLine("s5_eq=" + eq());
            Console.WriteLine("s5_eqNe=" + eqNe());
            Console.WriteLine("s5_lt=" + lt());
            Console.WriteLine("s5_sumEq=" + sumEq());
        }

        // ---------- S6: error path (prvalue operand, div by zero) ----------
        private static void S6ErrorPath() {
            Func<decimal> p = () => 1m;
            Func<decimal> z = () => 0m;
            try {
                decimal bad = p() / z();
                Console.WriteLine("s6_divzero=False val=" + bad);
            } catch (DivideByZeroException) {
                Console.WriteLine("s6_divzero=True");
            }
        }

        // ---------- S7: local operands (the untouched control shape) ----------
        private static void S7Locals() {
            decimal a = 2.5m;
            decimal b = 2.50m;
            decimal n = -2.5m;
            decimal z = 0m;
            Console.WriteLine("s7_eq=" + (a == b));
            Console.WriteLine("s7_ne=" + (a != b));
            Console.WriteLine("s7_lt=" + (n < z));
            Console.WriteLine("s7_ge=" + (a >= b));
            Console.WriteLine("s7_add=" + (a + b));
        }

        public static void Main() {
            S1Calls();
            S2DelegateOperands();
            S3Arithmetic();
            S4Convert();
            S5Captured();
            S6ErrorPath();
            S7Locals();
        }
    }
}
