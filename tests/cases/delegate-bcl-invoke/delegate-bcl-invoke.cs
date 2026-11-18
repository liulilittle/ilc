using System;
using System.ComponentModel;
using System.Reflection;

// Every BCL delegate family the transpiler must recognise without a name list:
// the ctor signature decides construction, the receiver type decides Invoke.
// Covers: a System.dll delegate (PropertyChangedEventHandler), a mscorlib
// reflection callback (MemberFilter), a generic delegate (Func<int,int>), and
// the cross-method shape that regressed corelibp0 (constructed in one method,
// invoked in another).
//
// Delegate.Combine/Remove are exercised through the Delegate-typed result
// without a castclass back to Action: castclass to a BCL delegate type is a
// separate, pre-existing gap (it emits an undeclared System_Action_class).
public static class Program
{
    static void OnPc(object s, PropertyChangedEventArgs e) { }
    static bool OnMember(MemberInfo m, object f) { return true; }
    static int OnInt(int x) { return x + 1; }
    static void OnA() { }

    static int InvokeElsewhere(Func<int, int> f, int v) { return f(v); }
    static void InvokeActionElsewhere(Action a) { a(); }

    public static int Main()
    {
        PropertyChangedEventHandler pce = new PropertyChangedEventHandler(OnPc);
        pce(null, null);

        MemberFilter mf = new MemberFilter(OnMember);
        bool m = mf(null, null);

        Func<int, int> f = new Func<int, int>(OnInt);
        int r = InvokeElsewhere(f, 41);

        Action a1 = new Action(OnA);
        Action a2 = new Action(OnA);
        Delegate comb = Delegate.Combine(a1, a2);
        Delegate rem = Delegate.Remove(comb, a1);
        InvokeActionElsewhere(a1);

        Console.WriteLine("pce=" + (pce != null) + " mf=" + m + " r=" + r + " comb=" + (comb != null) + " rem=" + (rem != null));
        return 0;
    }
}
