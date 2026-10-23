//------------------------------------------------------------------------------
// nullable2.cs -- Nullable<T> over the required T set (single topic per method).
// Each T gets its own static method: ctor / HasValue / Value / ToString /
// GetValueOrDefault on BOTH the has-value and the no-value path.
// Reference-type T is NOT tested: `Nullable<T>` requires T : struct, so a
// `string?` nullable does not exist in C# 7.3 / net48.
//------------------------------------------------------------------------------

using System;

struct Pt
{
    public int X;
    public Pt(int x) { X = x; }
    public override string ToString() { return "Pt(" + X + ")"; }
}

class Nullable2
{
    static void PInt()
    {
        int? n = 5;
        Console.WriteLine("int has: " + n.HasValue);
        Console.WriteLine("int value: " + (n.Value == 5));
        Console.WriteLine("int str: " + n.ToString());
        Console.WriteLine("int gvod: " + (n.GetValueOrDefault() == 5));
        int? z = null;
        Console.WriteLine("int z has: " + z.HasValue);
        Console.WriteLine("int z str len: " + z.ToString().Length);
        Console.WriteLine("int z gvod: " + z.GetValueOrDefault());
        Console.WriteLine("int z gvod arg: " + z.GetValueOrDefault(42));
        try { int v = z.Value; Console.WriteLine("int z no throw: " + v); }
        catch (InvalidOperationException e) { Console.WriteLine("int z throws: " + e.GetType().FullName); }
    }

    static void PLong()
    {
        long? n = 9000000000L;
        Console.WriteLine("long has: " + n.HasValue);
        Console.WriteLine("long value: " + (n.Value == 9000000000L));
        Console.WriteLine("long str: " + n.ToString());
        Console.WriteLine("long gvod: " + (n.GetValueOrDefault() == 9000000000L));
        long? z = null;
        Console.WriteLine("long z has: " + z.HasValue);
        Console.WriteLine("long z str len: " + z.ToString().Length);
        Console.WriteLine("long z gvod: " + z.GetValueOrDefault());
        try { long v = z.Value; Console.WriteLine("long z no throw: " + v); }
        catch (InvalidOperationException e) { Console.WriteLine("long z throws: " + e.GetType().FullName); }
    }

    static void PDouble()
    {
        double? n = 1.5;
        Console.WriteLine("double has: " + n.HasValue);
        Console.WriteLine("double value: " + (n.Value == 1.5));
        Console.WriteLine("double str: " + n.ToString());
        Console.WriteLine("double gvod: " + (n.GetValueOrDefault() == 1.5));
        double? z = null;
        Console.WriteLine("double z has: " + z.HasValue);
        Console.WriteLine("double z str len: " + z.ToString().Length);
        Console.WriteLine("double z gvod: " + z.GetValueOrDefault());
        try { double v = z.Value; Console.WriteLine("double z no throw: " + v); }
        catch (InvalidOperationException e) { Console.WriteLine("double z throws: " + e.GetType().FullName); }
    }

    static void PBool()
    {
        bool? n = true;
        Console.WriteLine("bool has: " + n.HasValue);
        Console.WriteLine("bool value: " + (n.Value == true));
        Console.WriteLine("bool str: " + n.ToString());
        Console.WriteLine("bool gvod: " + (n.GetValueOrDefault() == true));
        bool? z = null;
        Console.WriteLine("bool z has: " + z.HasValue);
        Console.WriteLine("bool z str len: " + z.ToString().Length);
        Console.WriteLine("bool z gvod: " + z.GetValueOrDefault());
        try { bool v = z.Value; Console.WriteLine("bool z no throw: " + v); }
        catch (InvalidOperationException e) { Console.WriteLine("bool z throws: " + e.GetType().FullName); }
    }

    static void PChar()
    {
        char? n = 'A';
        Console.WriteLine("char has: " + n.HasValue);
        Console.WriteLine("char value: " + (n.Value == 'A'));
        Console.WriteLine("char str: " + n.ToString());
        Console.WriteLine("char gvod: " + (n.GetValueOrDefault() == 'A'));
        char? z = null;
        Console.WriteLine("char z has: " + z.HasValue);
        Console.WriteLine("char z str len: " + z.ToString().Length);
        Console.WriteLine("char z gvod: " + (z.GetValueOrDefault() == '\0'));
        try { char v = z.Value; Console.WriteLine("char z no throw: " + v); }
        catch (InvalidOperationException e) { Console.WriteLine("char z throws: " + e.GetType().FullName); }
    }

    static void PDecimal()
    {
        decimal? n = 1.5m;
        Console.WriteLine("dec has: " + n.HasValue);
        Console.WriteLine("dec value: " + (n.Value == 1.5m));
        Console.WriteLine("dec str: " + n.ToString());
        Console.WriteLine("dec gvod: " + (n.GetValueOrDefault() == 1.5m));
        decimal? z = null;
        Console.WriteLine("dec z has: " + z.HasValue);
        Console.WriteLine("dec z str len: " + z.ToString().Length);
        Console.WriteLine("dec z gvod: " + (z.GetValueOrDefault() == 0m));
        try { decimal v = z.Value; Console.WriteLine("dec z no throw: " + v); }
        catch (InvalidOperationException e) { Console.WriteLine("dec z throws: " + e.GetType().FullName); }
    }

    static void PDateTime()
    {
        DateTime? n = new DateTime(2020, 1, 2, 3, 4, 5);
        Console.WriteLine("dt has: " + n.HasValue);
        Console.WriteLine("dt value: " + (n.Value == new DateTime(2020, 1, 2, 3, 4, 5)));
        Console.WriteLine("dt str: " + n.ToString());
        Console.WriteLine("dt gvod: " + (n.GetValueOrDefault() == new DateTime(2020, 1, 2, 3, 4, 5)));
        DateTime? z = null;
        Console.WriteLine("dt z has: " + z.HasValue);
        Console.WriteLine("dt z str len: " + z.ToString().Length);
        Console.WriteLine("dt z gvod: " + (z.GetValueOrDefault() == default(DateTime)));
        try { DateTime v = z.Value; Console.WriteLine("dt z no throw: " + v); }
        catch (InvalidOperationException e) { Console.WriteLine("dt z throws: " + e.GetType().FullName); }
    }

    static void PGuid()
    {
        Guid g = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        Guid? n = g;
        Console.WriteLine("guid has: " + n.HasValue);
        Console.WriteLine("guid value: " + (n.Value == g));
        Console.WriteLine("guid str: " + n.ToString());
        Console.WriteLine("guid gvod: " + (n.GetValueOrDefault() == g));
        Guid? z = null;
        Console.WriteLine("guid z has: " + z.HasValue);
        Console.WriteLine("guid z str len: " + z.ToString().Length);
        Console.WriteLine("guid z gvod: " + (z.GetValueOrDefault() == default(Guid)));
        try { Guid v = z.Value; Console.WriteLine("guid z no throw: " + v); }
        catch (InvalidOperationException e) { Console.WriteLine("guid z throws: " + e.GetType().FullName); }
    }

    static void PStruct()
    {
        Pt val = new Pt(7);
        Pt? n = val;
        Console.WriteLine("pt has: " + n.HasValue);
        Console.WriteLine("pt str: " + n.ToString());
        Console.WriteLine("pt gvod str: " + n.GetValueOrDefault().ToString());
        Console.WriteLine("pt value str: " + n.Value.ToString());
        Pt? z = null;
        Console.WriteLine("pt z has: " + z.HasValue);
        Console.WriteLine("pt z str len: " + z.ToString().Length);
        Console.WriteLine("pt z gvod str: " + z.GetValueOrDefault().ToString());
        try { Pt v = z.Value; Console.WriteLine("pt z no throw: " + v.ToString()); }
        catch (InvalidOperationException e) { Console.WriteLine("pt z throws: " + e.GetType().FullName); }
    }

    static void PTimeSpan()
    {
        TimeSpan? n = new TimeSpan(1, 2, 3);
        Console.WriteLine("ts has: " + n.HasValue);
        Console.WriteLine("ts value: " + (n.Value == new TimeSpan(1, 2, 3)));
        Console.WriteLine("ts str: " + n.ToString());
        Console.WriteLine("ts gvod: " + (n.GetValueOrDefault() == new TimeSpan(1, 2, 3)));
        TimeSpan? z = null;
        Console.WriteLine("ts z has: " + z.HasValue);
        Console.WriteLine("ts z str len: " + z.ToString().Length);
        Console.WriteLine("ts z gvod: " + (z.GetValueOrDefault() == default(TimeSpan)));
        try { TimeSpan v = z.Value; Console.WriteLine("ts z no throw: " + v); }
        catch (InvalidOperationException e) { Console.WriteLine("ts z throws: " + e.GetType().FullName); }
    }

    static void PEqHash()
    {
        int? a = 5;
        int? b = 5;
        int? c = 6;
        int? z = null;
        Console.WriteLine("eq same: " + a.Equals((object)b));
        Console.WriteLine("eq diff: " + a.Equals((object)c));
        Console.WriteLine("eq null: " + z.Equals((object)null));
        Console.WriteLine("eq val vs null: " + a.Equals((object)null));
        Console.WriteLine("hash val: " + a.GetHashCode());
        Console.WriteLine("hash null: " + z.GetHashCode());
    }

    static void Main()
    {
        PInt();
        PLong();
        PDouble();
        PBool();
        PChar();
        PDecimal();
        PDateTime();
        PGuid();
        PTimeSpan();
        PStruct();
        PEqHash();
    }
}
