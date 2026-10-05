//------------------------------------------------------------------------------
// propcase.cs -- simple-property folding e2e (iron rule 2j): auto properties,
// manual get/set properties, read-only (getter-only) properties and static
// properties must fold into plain field accesses, and every access must
// behave exactly like the reference (.NET Framework 4.8).
//
// Coverage:
//   1. auto property               {get;set;}  (CompilerGenerated backing field)
//   2. manual simple get/set property (explicit backing field)
//   3. read-only property          (getter-only, field-backed)
//   4. static auto + static manual properties
//   5. value propagation:  a.B = 5; b = a.B;  ->  b == 5
//   6. read-modify-write + cross-property propagation
//   7. property chain: a.Node.Child.Leaf.Value (3 ref-type auto properties)
//   8. properties mixed with a plain field in one expression
//   9. loop accumulation through a folded property
//   10. swap through properties; instance-to-static propagation
//
// All outputs are bool checks, so the reference output is fully deterministic
// and hand-verifiable: every line is True when the fold behaved correctly.
//------------------------------------------------------------------------------
using System;

public class PropLeaf
{
    public int Value { get; set; }        // auto property (foldable)
}

public class PropChild
{
    public PropLeaf Leaf { get; set; }    // ref-type auto property (foldable)
}

public class PropNode
{
    public PropChild Child { get; set; }  // ref-type auto property (foldable)
}

public class PropCase
{
    // 1. auto property
    public int Auto { get; set; }
    // 2. manual simple get/set property
    private int _manual;
    public int Manual { get { return _manual; } set { _manual = value; } }
    // 3. read-only property (getter-only, field-backed -- foldable)
    private int _ro;
    public int ReadOnly { get { return _ro; } }
    // 4a. static auto property
    public static int StaticAuto { get; set; }
    // 4b. static manual property
    private static int _stat;
    public static int StaticManual { get { return _stat; } set { _stat = value; } }
    // 5. ref-type auto property (chain link)
    public PropNode Node { get; set; }
    // 6. plain field (mixed arithmetic)
    public int Field;

    public PropCase(int ro) { _ro = ro; }
}

namespace Cases { public static class PropCaseMain {
    public static void Main() {
        PropCase a = new PropCase(5);

        // 5. value propagation: a.B = 5; b = a.B; expect b == 5
        a.Auto = 5;
        int b = a.Auto;
        Console.WriteLine(b == 5);

        // 2. manual get/set
        a.Manual = 7;
        int m = a.Manual;
        Console.WriteLine(m == 7);

        // 3. read-only getter (seeded via ctor)
        int r = a.ReadOnly;
        Console.WriteLine(r == 5);

        // 4a. static auto property
        PropCase.StaticAuto = 9;
        int sa = PropCase.StaticAuto;
        Console.WriteLine(sa == 9);

        // 4b. static manual property
        PropCase.StaticManual = 11;
        int sm = PropCase.StaticManual;
        Console.WriteLine(sm == 11);

        // 6a. read-modify-write through folded props
        a.Auto = a.Auto + 1;
        Console.WriteLine(a.Auto == 6);

        // 6b. cross-property propagation
        a.Manual = a.Auto;
        Console.WriteLine(a.Manual == 6);

        // 7. property chain: a.Node.Child.Leaf.Value
        a.Node = new PropNode();
        a.Node.Child = new PropChild();
        a.Node.Child.Leaf = new PropLeaf();
        a.Node.Child.Leaf.Value = 13;
        Console.WriteLine(a.Node.Child.Leaf.Value == 13);
        a.Node.Child.Leaf.Value = a.Node.Child.Leaf.Value * 2;
        Console.WriteLine(a.Node.Child.Leaf.Value == 26);

        // 8. properties mixed with a plain field in one expression
        a.Field = 3;
        int mixed = a.Auto + a.Manual + a.ReadOnly + a.Field + a.Node.Child.Leaf.Value;
        Console.WriteLine(mixed == 6 + 6 + 5 + 3 + 26);

        // 4c. static properties mixed
        int smix = PropCase.StaticAuto + PropCase.StaticManual;
        Console.WriteLine(smix == 20);

        // 9. loop accumulation through a folded property
        PropCase acc = new PropCase(0);
        int sum = 0;
        for (int i = 1; i <= 10; i++) { acc.Auto = acc.Auto + i; sum = sum + i; }
        Console.WriteLine(acc.Auto == sum);

        // 10a. swap through properties
        a.Auto = 100;
        a.Manual = 200;
        int t1 = a.Auto;
        a.Auto = a.Manual;
        a.Manual = t1;
        Console.WriteLine(a.Auto == 200);
        Console.WriteLine(a.Manual == 100);

        // 10b. instance-to-static propagation
        PropCase.StaticManual = a.Auto;
        Console.WriteLine(PropCase.StaticManual == 200);
    }
}}
