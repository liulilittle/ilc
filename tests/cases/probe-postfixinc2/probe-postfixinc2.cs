//------------------------------------------------------------------------------
// probe-postfixinc2.cs -- RED/GREEN probe: postfix increment evaluation order,
// static-field focused (the impl-postfixinc RED distribution: sfield/sfieldl
// only), with the local / instance-field / property shapes carried along as
// non-regression controls.
//
// net48 semantics of `expr(i++)`: the value of the expression is the value of
// i BEFORE the increment. Any consumer of the postfix expression itself must
// receive the ORIGINAL value; only LATER reads observe the increment.
//
// Static-field shapes (the defect surface):
//   y = SF++            [..-assign]
//   Foo(SF++)           [..-arg]
//   arr[SF++] = v       [..-arr]
//   SF++ + 0            [..-add0]
// int and long, static FIELD and static PROPERTY (getter/setter).
// Local / instance-field / instance-property shapes are regression controls.
// i++ + i++ is deliberately NOT covered (unsequenced, undefined in C#).
//------------------------------------------------------------------------------
using System;

namespace Cases {
public static class PostInc2 {
    public class Box {
        public int IField;
        public long LField;
        public int Prop { get; set; }
    }

    public static int Take(int x) { return x; }
    public static long TakeL(long x) { return x; }

    public static int[] Arr = new int[8];
    public static long[] LArr = new long[8];

    // static field surface
    public static int SF;
    public static long SL;
    // 64-bit static field whose old value does NOT fit int32: the snapshot
    // temp the fold materializes for the postfix expression must keep the
    // full width (an int32_t temp truncates the preserved old value).
    public static long BIG;

    // static property surface (getter/setter must keep the old-value contract)
    private static int _sp;
    public static int SP { get { return _sp; } set { _sp = value; } }
    private static long _spl;
    public static long SPL { get { return _spl; } set { _spl = value; } }

    public static void Main() {
        var b = new Box();

        // ---- regression controls: int local ----
        int i = 5; int y = i++;
        Console.WriteLine("[int-assign] y={0} i={1}", y, i);
        i = 5; int a = Take(i++);
        Console.WriteLine("[int-arg] a={0} i={1}", a, i);
        i = 2; Arr[i++] = 77;
        Console.WriteLine("[int-arr] arr2={0} i={1}", Arr[2], i);
        i = 9; int ia = i++ + 0;
        Console.WriteLine("[int-add0] a={0} i={1}", ia, i);

        // ---- regression controls: long local ----
        long l = 5; long yl = l++;
        Console.WriteLine("[long-assign] y={0} l={1}", yl, l);
        l = 5; long al = TakeL(l++);
        Console.WriteLine("[long-arg] a={0} l={1}", al, l);

        // ---- regression controls: instance field / property ----
        b.IField = 5; int yi = b.IField++;
        Console.WriteLine("[ifield-assign] y={0} f={1}", yi, b.IField);
        b.IField = 5; int ai = Take(b.IField++);
        Console.WriteLine("[ifield-arg] a={0} f={1}", ai, b.IField);
        b.Prop = 5; int yp = b.Prop++;
        Console.WriteLine("[prop-assign] y={0} f={1}", yp, b.Prop);
        b.Prop = 2; Arr[b.Prop++] = 55;
        Console.WriteLine("[prop-arr] arr2={0} f={1}", Arr[2], b.Prop);

        // ---- static field int (defect surface) ----
        SF = 5; int yf = SF++;
        Console.WriteLine("[sfield-assign] y={0} f={1}", yf, SF);
        SF = 5; int af = Take(SF++);
        Console.WriteLine("[sfield-arg] a={0} f={1}", af, SF);
        SF = 2; Arr[SF++] = 66;
        Console.WriteLine("[sfield-arr] arr2={0} f={1}", Arr[2], SF);
        SF = 9; int sf0 = SF++ + 0;
        Console.WriteLine("[sfield-add0] a={0} f={1}", sf0, SF);
        SF = 9; string sfs = $"{SF++}";
        Console.WriteLine("[sfield-intp] s={0} f={1}", sfs, SF);

        // ---- static field long (defect surface) ----
        SL = 5; long ysl = SL++;
        Console.WriteLine("[sfieldl-assign] y={0} f={1}", ysl, SL);
        SL = 5; long asl = TakeL(SL++);
        Console.WriteLine("[sfieldl-arg] a={0} f={1}", asl, SL);
        SL = 2; LArr[SL++] = 88L;
        Console.WriteLine("[sfieldl-arr] arr2={0} f={1}", LArr[2], SL);
        SL = 9; long sl0 = SL++ + 0;
        Console.WriteLine("[sfieldl-add0] a={0} f={1}", sl0, SL);

        // ---- static field long beyond int32 (snapshot width) ----
        BIG = 5000000000L; long ybig = BIG++;
        Console.WriteLine("[sfieldl-big-assign] y={0} f={1}", ybig, BIG);
        BIG = 5000000000L; long abig = TakeL(BIG++);
        Console.WriteLine("[sfieldl-big-arg] a={0} f={1}", abig, BIG);

        // ---- static property int/long (control: call-materialized) ----
        SP = 5; int ysp = SP++;
        Console.WriteLine("[sprop-assign] y={0} f={1}", ysp, SP);
        SP = 5; int asp = Take(SP++);
        Console.WriteLine("[sprop-arg] a={0} f={1}", asp, SP);
        SP = 2; Arr[SP++] = 44;
        Console.WriteLine("[sprop-arr] arr2={0} f={1}", Arr[2], SP);
        SP = 9; int sp0 = SP++ + 0;
        Console.WriteLine("[sprop-add0] a={0} f={1}", sp0, SP);
        SPL = 5; long yspl = SPL++;
        Console.WriteLine("[spropl-assign] y={0} f={1}", yspl, SPL);
        SPL = 5; long aspl = TakeL(SPL++);
        Console.WriteLine("[spropl-arg] a={0} f={1}", aspl, SPL);
    }
}
}
