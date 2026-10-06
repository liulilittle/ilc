//------------------------------------------------------------------------------
// rf-invoke-instance.cs -- RF-1 e2e probe: MethodInfo.Invoke 'this' injection
// (net48 oracle, byte-for-byte). The committed R3 probe (typeinfo.cpp:671-674)
// exercises a FREE function (no 'this'); this case is the missing INSTANCE
// path -- the argbuf MUST inject the target instance as slot 0 ('this') for
// instance methods, matching the transpiler's emitted ABI (instance methods
// arg0 = this). Every probe is a field read/write on the target instance so
// a missing/invalid 'this' produces a wrong value or a crash (the pre-RF-1
// silent defect).
//
//   AddTo(int)     : instance field read  + explicit int arg
//   Mul2(int,int)  : instance field read  + TWO explicit args (argbuf shift)
//   Set(int)       : instance field WRITE (state persists on the target)
//   Read()         : instance 0-explicit-arg method (the hardest this-slot
//                    case: signature "I" -> invoker must still inject 'this')
//   StatAdd(int,int): STATIC free function (no 'this') -- regression probe:
//                    net48 ignores the obj argument for static methods, so a
//                    non-null receiver keeps the free-function path intact.
//
// Byte-stability: every probe prints a bool against the net48 oracle value.
//------------------------------------------------------------------------------
using System;

namespace Cases {
    public class InvokeHost {
        public int fld;
        public InvokeHost(int v) { fld = v; }
        public int Read() { return fld; }
        public int AddTo(int d) { return fld + d; }
        public int Mul2(int a, int b) { return a * b + fld; }
        public void Set(int v) { fld = v; }
        public static int StatAdd(int a, int b) { return a + b; }
    }

    public static class RfInvokeInstance {
        public static void Main() {
            var h = new InvokeHost(10);
            // instance, 1 explicit arg, field read (net48: 10 + 5 == 15).
            Console.WriteLine("addto=" + ((int)typeof(InvokeHost).GetMethod("AddTo").Invoke(h, new object[] { 5 }) == 15));
            // instance, 2 explicit args, field read (net48: 3*4 + 10 == 22).
            Console.WriteLine("mul2=" + ((int)typeof(InvokeHost).GetMethod("Mul2").Invoke(h, new object[] { 3, 4 }) == 22));
            // instance, field WRITE: the write must land on the real target.
            typeof(InvokeHost).GetMethod("Set").Invoke(h, new object[] { 7 });
            Console.WriteLine("set_state=" + (h.fld == 7));
            // instance, ZERO explicit args (signature "I" -> 'this' only).
            Console.WriteLine("read0=" + ((int)typeof(InvokeHost).GetMethod("Read").Invoke(h, new object[0]) == 7));
            // STATIC free function with a non-null receiver: net48 ignores the
            // receiver for static methods; the free-function path must NOT get
            // a 'this' slot (the R3 no-this regression guard, e2e shape).
            Console.WriteLine("statadd=" + ((int)typeof(InvokeHost).GetMethod("StatAdd").Invoke(h, new object[] { 2, 3 }) == 5));
        }
    }
}