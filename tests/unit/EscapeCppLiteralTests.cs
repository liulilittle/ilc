using Il2Cpp.Transpiler.Opt;
using Xunit;

//------------------------------------------------------------------------------
// EscapeCppLiteral regression set (A7 draft 2026-09-13, adopted by main).
//
// Background: winforms1.cs:5675 passes "All\0*.*\0" to
// Marshal.StringToHGlobalUni; the pool emitter must NOT write raw NUL bytes
// into generated C++ (GCC/clang -Wnull-character; gen2 2026-08-18 artifact).
// Contract (src/il2cppc/Opt/StringPoolPass.cs:185-257): classic escapes for
// \ " \n \r \t; EVERY other char outside U+0020..U+007E is emitted as its
// UTF-8 bytes in 3-digit octal escapes; a raw decimal digit after an octal
// run forces an adjacent-literal split ("\001" "42") to silence msvc C4125.
//------------------------------------------------------------------------------
public class EscapeCppLiteralTests
{
    [Fact]
    public void NulIsOctalEscaped_Winforms1Filter()
        => Assert.Equal("All\\000*.*\\000", StringPoolPass.EscapeCppLiteral("All\0*.*\0"));

    [Fact]
    public void C0ControlsAndDelAreOctalEscaped()
        => Assert.Equal("\\001\\007\\033\\177",
                        StringPoolPass.EscapeCppLiteral("\u0001\u0007\u001b\u007f"));

    [Fact]
    public void DigitAfterOctalGetsAdjacentLiteralSplit_C4125()
        => Assert.Equal("a\\001\" \"42", StringPoolPass.EscapeCppLiteral("a\u000142"));

    [Fact]
    public void ClassicEscapesStayClassic()
        => Assert.Equal("\\\\\\\"\\n\\r\\t", StringPoolPass.EscapeCppLiteral("\\\"\n\r\t"));

    [Fact]
    public void NonAsciiIsUtf8Octal_CharsetProof()
        => Assert.Equal("\\302\\244", StringPoolPass.EscapeCppLiteral("\u00a4"));

    [Fact]
    public void SurrogatePairIsOneCodePoint()
        => Assert.Equal("\\360\\237\\230\\200", StringPoolPass.EscapeCppLiteral("\uD83D\uDE00"));

    [Fact]
    public void EscapedOutputContainsNoRawNul()
        => Assert.Equal(-1, StringPoolPass.EscapeCppLiteral("All\0*.*\0").IndexOf('\0'));
}
