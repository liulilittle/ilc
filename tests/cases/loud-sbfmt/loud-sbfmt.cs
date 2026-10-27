// LOUD-sbfmt (impl-sb): the net6+/net9 StringBuilder format arms that were
// LOUD whitelist rejections before this wiring:
//   A  AppendLine(ref AppendInterpolatedStringHandler) + the provider variant
//   B  AppendFormat(string[, provider], params ReadOnlySpan<object?>) -- the
//      C# 13 span-params lowering (inline-array expansion -> argsArray kernel)
//   C  AppendFormat<TArg...>(provider, CompositeFormat, TArg...) + the params
//      object[] form (the pre-parsed CompositeFormat rides the format string)
//   D  Append(ReadOnlySpan<char>) + Append(ref handler) (a no-op chain link)
// All values are ints/strings: the bind model drops the IFormatProvider (the
// documented net6+ deviation), so the expected text must be culture-neutral.
using System;
using System.Globalization;
using System.Text;

class P
{
    static void Main()
    {
        var sb = new StringBuilder();

        // A: the nested interpolation handler (bind + deref appends + newline).
        sb.AppendLine($"a={1}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"b={2}");

        // B: the C# 13 span-params lowering (inline array -> object[] -> the
        // argsArray composite kernel).
        sb.AppendFormat("{0}-{1}-{2}-{3}", 1, 2, 3, 4);
        sb.AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}", 5, 6, 7, 8, 9);
        sb.AppendLine();

        // C: the CompositeFormat overloads (the generic TArg family and the
        // params object[] form).
        var cf = CompositeFormat.Parse("[{0}+{1}]");
        sb.AppendFormat(CultureInfo.InvariantCulture, cf, 10, 11);
        sb.AppendLine();
        sb.AppendFormat(CultureInfo.InvariantCulture, cf, new object[] { 12, 13 });
        sb.AppendLine();

        // D: the span append (string-backed carrier) + the handler append
        // (the outer call is a no-op chain link).
        sb.Append("tail".AsSpan());
        sb.Append($"|z={3}");
        sb.AppendLine();

        // E (P3-1②): the char[]-backed span carrier (ReadOnlySpan<char>::
        // op_Implicit(char[])) -- the span kernel dispatches on the backing
        // object class and appends the ARRAY's element units (not the string
        // path). The direct cast is the identity-folded lowering; a sliced
        // AsSpan(string,int,int) would be a different (uncovered) shape.
        char[] chars = { 'x', '|', 'y' };
        sb.Append((ReadOnlySpan<char>)chars);
        sb.AppendLine();

        // F (P3-1②): the three-TArg CompositeFormat form
        // (AppendFormat<TArg0,TArg1,TArg2> -- the kernel family's top arity;
        // each closed int slot boxes through the composite engine).
        var cf3 = CompositeFormat.Parse("<{0}-{1}-{2}>");
        sb.AppendFormat(CultureInfo.InvariantCulture, cf3, 14, 15, 16);
        sb.AppendLine();

        Console.WriteLine(sb.ToString());
    }
}
