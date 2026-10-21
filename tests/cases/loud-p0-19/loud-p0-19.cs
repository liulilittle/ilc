// LOUD P0-19: System.Math decimal overload dispatch (net48 oracle).
//
// Before the fix Math.Abs/Max/Min/Sign/Floor/Ceiling/Round/Truncate on a
// decimal fell through the byte-only Math mapping to the double/long icalls
// (abs_d(<Il2CppDecimal*>) -> C2664). Every value is assigned to a local
// first (decimal icalls take the result slot and are assignment-only in the
// emitter), then printed with InvariantCulture for a stable "G" oracle line.
using System;
using System.Globalization;

class P
{
    private static string F(decimal d) => d.ToString(CultureInfo.InvariantCulture);

    private static void Main()
    {
        decimal neg = -12.75m;
        decimal pos = 3.5m;
        decimal zero = 0m;
        decimal negZero = -0.0m;

        decimal absNeg = Math.Abs(neg);
        decimal absPos = Math.Abs(pos);
        decimal absZero = Math.Abs(negZero);
        Console.WriteLine("[p0-19] abs=" + F(absNeg) + "," + F(absPos) + "," + F(absZero));

        decimal max = Math.Max(neg, pos);
        decimal min = Math.Min(neg, pos);
        // Tie = numerically equal but different SCALE: the returned operand's
        // scale is observable in the printed value.
        decimal oneA = 1.0m;
        decimal oneB = 1.00m;
        decimal maxTie = Math.Max(oneA, oneB);
        decimal maxTieRev = Math.Max(oneB, oneA);
        decimal minTie = Math.Min(oneA, oneB);
        decimal minTieRev = Math.Min(oneB, oneA);
        Console.WriteLine("[p0-19] maxmin=" + F(max) + "," + F(min) + ",tie=" + F(maxTie) + ","
            + F(maxTieRev) + "," + F(minTie) + "," + F(minTieRev));

        int signNeg = Math.Sign(neg);
        int signPos = Math.Sign(pos);
        int signZero = Math.Sign(zero);
        int signNegZero = Math.Sign(negZero);
        Console.WriteLine("[p0-19] sign=" + signNeg + "," + signPos + "," + signZero + "," + signNegZero);

        decimal floorNeg = Math.Floor(neg);
        decimal floorPos = Math.Floor(pos);
        decimal ceilNeg = Math.Ceiling(neg);
        decimal ceilPos = Math.Ceiling(pos);
        Console.WriteLine("[p0-19] floorceil=" + F(floorNeg) + "," + F(floorPos) + "," + F(ceilNeg) + "," + F(ceilPos));

        decimal r1 = Math.Round(2.5m);
        decimal r2 = Math.Round(3.5m);
        decimal r3 = Math.Round(-2.5m);
        decimal r4 = Math.Round(1.235m, 2);
        decimal r5 = Math.Round(1.245m, 2);
        Console.WriteLine("[p0-19] round=" + F(r1) + "," + F(r2) + "," + F(r3) + "," + F(r4) + "," + F(r5));

        decimal t1 = Math.Truncate(neg);
        decimal t2 = Math.Truncate(pos);
        Console.WriteLine("[p0-19] trunc=" + F(t1) + "," + F(t2));

        // No-misfire: the primitive overloads keep their original icalls.
        int ai = Math.Abs(-7);
        long al = Math.Abs(-9000000000L);
        double ad = Math.Abs(-2.5);
        float af = Math.Abs(-3.25f);
        double ash = Math.Abs((short)-5);
        double ab = Math.Abs((byte)5);
        int mi = Math.Max(3, 9);
        int ni = Math.Min(3, 9);
        double md = Math.Max(2.5, 7.25);
        int sd = Math.Sign(-0.5);
        Console.WriteLine("[p0-19] prim=" + ai + "," + al + "," + ad.ToString(CultureInfo.InvariantCulture)
            + "," + ((double)af).ToString(CultureInfo.InvariantCulture) + ","
            + ash.ToString(CultureInfo.InvariantCulture) + "," + ab.ToString(CultureInfo.InvariantCulture) + ","
            + mi + "," + ni + "," + md.ToString(CultureInfo.InvariantCulture) + "," + sd);

        // Untracked-parameter path: a decimal passed through ldarg must still
        // dispatch on the decoded signature (V:System.Decimal).
        ViaArg(neg, 2.5m);
    }

    private static void ViaArg(decimal d, decimal m)
    {
        decimal a = Math.Abs(d);
        decimal r = Math.Round(m);
        Console.WriteLine("[p0-19] via-arg=" + F(a) + "," + F(r));
    }
}
