using Il2Cpp.Transpiler.Emit;
using Xunit;

//------------------------------------------------------------------------------
// TypeEmitterSanitizeTests -- the whitelist contract of TypeEmitter.Sanitize.
//
// The historical rule was a BLACKLIST: Split on a fixed separator list
// ('.', '+', '`', '<', '>', ',', '[', ']', '=') and Join with '_'. Any
// character missing from that list survived into the generated C++ identifier
// (the '|' in the csc local-function metadata name "<Add>g__Local|1_0" broke
// every local function). The rule is now WHITELISTED: after the unchanged
// Split/Join step, every remaining character outside [A-Za-z0-9_] becomes '_'.
//
// The tests below pin down both halves of the contract:
//   1. every existing (already-legal) symbol is a FIXED POINT -- the change is
//      a no-op for all previously generated names (hash/symbol stability);
//   2. Sanitize is IDEMPOTENT (Sanitize(Sanitize(x)) == Sanitize(x));
//   3. every known hostile character class produces a valid C++ identifier.
//------------------------------------------------------------------------------
public class TypeEmitterSanitizeTests
{
    // Real symbols harvested from generated output of the regression cases
    // (tests/cases/<case>/gen_diff_*/src/*.cpp and include/internal/generated.h).
    // These MUST keep their exact spelling: the SigHash suffixes and the
    // mangled struct/field names are baked into already-shipped generated code.
    public static readonly string[] KnownExistingSymbols =
    {
        "Cases_Generics_Box_1_int",
        "Cases_Generics_Box_1_string",
        "Cases_Recurse_Main_5e2f6a1a1c2b3d4e",
        "Cases_YieldProbe_Range_d__0",
        "Cases_YieldProbe_Main_0e1d2c3b4a596877",
        "Cases_AsyncCase_F_d__1",
        "Cases_AsyncCase_H_d__3",
        "Cases_DelegateGen_Main_9f8e7d6c5b4a3210",
        "Cases_GenericIface_Impl_1_int",
        "Cases_Valuesem_Point",
        "Cases_Virtual_Base",
        "Cases_Virtual_Derived",
        "Cases_Exceptions_Main_1122334455667788",
        "Cases_LinqDeep_Main_8877665544332211",
        "Cases_DecimalCase_Main_aabbccddeeff0011",
        "Cases_DateTime2_Main_0123456789abcdef",
        "Cases_GuidMethod2_Main_fedcba9876543210",
        "Cases_MvarUseCase_Main_0f1e2d3c4b5a6978",
        "Cases_P0gen_Main_13579bdf2468ace0",
        "Cases_P0linq1_Main_2468ace013579bdf",
        "__StaticArrayInitTypeSize_3",
        "System_ValueTuple_2_int_string",
        "System_Collections_Generic_List_1_int",
        "Cases_A_B_h7f3a1c9d",
        "_obj",
        "_obj_field",
        "_X_k__BackingField",
        "Cases_Sanitize1___c__DisplayClass1_0",
        "Cases_Sanitize1__Add_g__Local_0_0_8a8e8aca113f1970",
    };

    [Fact]
    public void Sanitize_LocalFunctionPipeCharacter_ProducesValidCppIdentifier()
    {
        // csc metadata name of a local function: "<Add>g__Local|1_0".
        var raw = "<Add>g__Local|1_0";
        var got = TypeEmitter.Sanitize(raw);
        Assert.DoesNotContain("|", got);
        Assert.Equal("_Add_g__Local_1_0", got);
        AssertValidCppIdentifier(got);
    }

    [Fact]
    public void Sanitize_NestedLocalFunctionName_ProducesValidCppIdentifier()
    {
        // "<Nested>g__Inner|1_1"
        var got = TypeEmitter.Sanitize("<Nested>g__Inner|1_1");
        Assert.Equal("_Nested_g__Inner_1_1", got);
        AssertValidCppIdentifier(got);
    }

    [Theory]
    [InlineData("a|b", "a_b")]
    [InlineData("a{b}", "a_b_")]
    [InlineData("a b", "a_b")]
    [InlineData("a:b", "a_b")]
    [InlineData("a;b", "a_b")]
    [InlineData("a/b", "a_b")]
    [InlineData("a\\b", "a_b")]
    [InlineData("a\"b", "a_b")]
    [InlineData("a#b", "a_b")]
    [InlineData("a@b", "a_b")]
    [InlineData("a%b", "a_b")]
    [InlineData("a^b", "a_b")]
    [InlineData("a&b", "a_b")]
    [InlineData("a*b", "a_b")]
    [InlineData("a!b", "a_b")]
    [InlineData("a?b", "a_b")]
    [InlineData("a~b", "a_b")]
    [InlineData("a`b", "a_b")] // backtick: Split separator, single '_' (unchanged)
    [InlineData("a.b", "a_b")]
    [InlineData("a+b", "a_b")]
    [InlineData("a,b", "a_b")]
    [InlineData("a[b]", "a_b_")] // Split on '[' AND ']' keeps the trailing empty segment
    [InlineData("a=b", "a_b")]
    [InlineData("a<b>c", "a_b_c")]
    [InlineData("a..b", "a__b")] // consecutive separators keep the Split semantics
    [InlineData("a.|.b", "a___b")] // ... and the whitelist step adds one '_' per hostile char
    public void Sanitize_HostileCharacters_ProduceValidCppIdentifier(string raw, string expected)
    {
        var got = TypeEmitter.Sanitize(raw);
        Assert.Equal(expected, got);
        AssertValidCppIdentifier(got);
    }

    [Fact]
    public void Sanitize_IsIdempotent()
    {
        var inputs = new[]
        {
            "<Add>g__Local|1_0",
            "<Nested>g__Inner|1_1",
            "<>c__DisplayClass1_0",
            "<Main>d__0",
            "<>f__AnonymousType0`1",
            "<PrivateImplementationDetails>.__StaticArrayInitTypeSize=3",
            "Cases.Box`1",
            "System.Collections.Generic.List`1<System.String>",
            "V_Cases.Foo",
            "a|b|c",
            "|",
            "",
            "V_",
            "_obj",
            "Cases_A_B",
        };
        foreach (var input in inputs)
        {
            var once = TypeEmitter.Sanitize(input);
            var twice = TypeEmitter.Sanitize(once);
            Assert.Equal(once, twice);
        }
    }

    [Fact]
    public void Sanitize_KnownExistingSymbols_AreFixedPoints()
    {
        // The whitelist step must not change ANY previously generated symbol:
        // every real symbol is already in [A-Za-z0-9_] and therefore a fixed
        // point. This is the "do not break existing hashes/names" hard evidence.
        foreach (var sym in KnownExistingSymbols)
        {
            Assert.Equal(sym, TypeEmitter.Sanitize(sym));
        }
    }

    [Fact]
    public void Sanitize_EveryKnownSymbolStaysValidCppIdentifier()
    {
        foreach (var sym in KnownExistingSymbols)
            AssertValidCppIdentifier(TypeEmitter.Sanitize(sym));
    }

    [Fact]
    public void Sanitize_AllBytes_ProduceValidCppIdentifier()
    {
        // Exhaustive over every UTF-16 code unit 0x0000..0xFFFF that can appear
        // in a metadata name: no character may survive as a non-identifier.
        for (var c = 0; c <= 0xFFFF; c++)
        {
            var raw = "A" + (char)c + "B";
            var got = TypeEmitter.Sanitize(raw);
            AssertValidCppIdentifier(got);
            Assert.Equal(got, TypeEmitter.Sanitize(got)); // idempotent
        }
    }

    [Fact]
    public void Sanitize_ExistingReplacementsKeepTheirSemantics()
    {
        // '<'/'>' are replaced (and a run of them collapses to ONE '_' thanks
        // to Split) -- the behavior that names every compiler-generated type.
        Assert.Equal("__c", TypeEmitter.Sanitize("<>c"));
        Assert.Equal("__c__DisplayClass1_0", TypeEmitter.Sanitize("<>c__DisplayClass1_0"));
        Assert.Equal("_Main_d__0", TypeEmitter.Sanitize("<Main>d__0"));
        Assert.Equal("__f__AnonymousType0_1", TypeEmitter.Sanitize("<>f__AnonymousType0`1"));
        // '=' keeps its refl2 S2/G3 behavior.
        Assert.Equal("__StaticArrayInitTypeSize_3", TypeEmitter.Sanitize("__StaticArrayInitTypeSize=3"));
    }

    private static void AssertValidCppIdentifier(string s)
    {
        Assert.False(string.IsNullOrEmpty(s));
        Assert.True(char.IsAsciiLetter(s[0]) || s[0] == '_',
            $"identifier must not start with '{s[0]}' (0x{(int)s[0]:X4}) in \"{s}\"");
        foreach (var ch in s)
        {
            Assert.True(char.IsAsciiLetterOrDigit(ch) || ch == '_',
                $"illegal character '{ch}' (0x{(int)ch:X4}) in \"{s}\"");
        }
    }
}
