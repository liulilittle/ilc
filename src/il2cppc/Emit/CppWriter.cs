using System.Text;

namespace Il2Cpp.Transpiler.Emit;

public sealed class CppWriter
{
    private const int AlignColumn = 100; // end-of-line comment right-align column (spec 4.4.2 style rule)
    private readonly StringBuilder _sb = new();
    private int _indent;

    public void Line(string text = "")
    {
        if (text.Length > 0) _sb.Append(' ', _indent * 4);
        _sb.AppendLine(text);
    }

    // Writes a line verbatim (no indentation): used when the method body already
    // carries its own indentation, to avoid double-indentation breaking the alignment
    // of end-of-line comments.
    public void RawLine(string text = "") => _sb.AppendLine(text);

    // Code + end-of-line comment: the comment right-aligns to column 100 (openppp2
    // style, spec 4.4.2)
    public void LineAligned(string code, string comment = "")
    {
        _sb.Append(' ', _indent * 4);
        if (comment.Length == 0)
        {
            _sb.AppendLine(code);
            return;
        }
        // The length check must subtract the indentation, otherwise code at columns
        // 95-97 plus the indent overflows the align column (M3 fix)
        var padded = code.Length < AlignColumn - 2 - _indent * 4
            ? code.PadRight(AlignColumn - 2 - _indent * 4)
            : code + " ";
        _sb.AppendLine(padded + "// " + comment);
    }

    public void Section(string title) // section divider
    {
        _sb.AppendLine("//------------------------------------------------------------------------------");
        if (title.Length > 0) _sb.AppendLine("// " + title);
        _sb.AppendLine("//------------------------------------------------------------------------------");
    }

    public void Open() => _indent++;
    public void Close() => _indent--;
    public override string ToString() => _sb.ToString();
}
