// Probe v2: decimal literals in statement/operand position (void/bool sigs --
// decimal RETURN types hit "return type 17 is not supported by M3", itself a gap).
public static class Probe
{
    // operand position: ldc.i4.6 + newobj .ctor(int32) feeds op_Equality
    public static bool C(decimal x) => x == 6m;

    // local literal + literal-vs-literal compare (5-arg ctor shape)
    public static bool L()
    {
        decimal d = 0.1m;
        return d == 0.1m;
    }

    // negative fractional literal (sign=1)
    public static bool N()
    {
        decimal n = -0.5m;
        return n < 0m;
    }

    // >int32 literal -> ldc.i8 + .ctor(int64)
    public static bool H()
    {
        decimal h = 9999999999999m;
        return h > 0m;
    }
}
