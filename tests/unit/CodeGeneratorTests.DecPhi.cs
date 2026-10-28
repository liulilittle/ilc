using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Xunit;

namespace Il2Cpp.Tests
{
    // impl-decphi (docs/audit/b5-gaps.md 11.5): the branch-merge phi slot for a
    // DECIMAL stack value. Roslyn duplicates the int->decimal conversion INTO
    // each arm of `acc + (g == Guid.Empty ? 1m : 100m)`, so the merged
    // accumulator copy is a decimal stack value at the join; the slot-type
    // seed channel (EmitBody parameter seeds -> ExprKind(LocExpr) propagation)
    // must declare the join temp Il2CppDecimal, not int32_t. Before the fix
    // the generated body declared `int32_t t1;` and
    // `icall_decimal_add((&t5), (&(t3)), (&(t4)))` received an int32_t* --
    // a C++ compile error (a C0 build failure, NOT a wrong-value risk).
    public class CodeGeneratorDecPhiTests
    {
        private static string GenerateDecPhi()
        {
            var types = MetadataLoader.LoadAssembly(FixturePath("decphi"));
            var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
            try
            {
                CodeGenerator.Generate(types, dir, FixturePath("decphi"));
                return File.ReadAllText(Path.Combine(dir, "src", "DecPhi_Program.cpp"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static string FixturePath(string name)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "out", name + ".dll");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException(
                $"fixture {name}.dll not found; run scripts/build-fixtures.ps1 first (expects repo root/tests/fixtures/out/{name}.dll)");
        }

        [Fact]
        public void Generate_DecimalBranchJoin_SlotDeclaresIl2CppDecimal()
        {
            var cpp = GenerateDecPhi();

            // The Pick body: the join slots carrying the decimal accumulator
            // (arg1 copy t1 + the merged t3) must be the 16-byte struct.
            // The pre-fix shape declared `int32_t t1;` / `int32_t t3;` and
            // `icall_decimal_add` took an int32_t* (the cmake RED).
            var pick = ExtractMethodBody(cpp, "IL2CPP_NOINLINE il2cpp::runtime::Il2CppDecimal DecPhi_Program_Pick_");
            Assert.Contains("il2cpp::runtime::Il2CppDecimal t1;", pick);
            Assert.Contains("il2cpp::runtime::Il2CppDecimal t3;", pick);
            Assert.Contains("icall_decimal_add", pick);
            Assert.DoesNotContain("int32_t t1;", pick);
            Assert.DoesNotContain("int32_t t3;", pick);
        }

        [Fact]
        public void Generate_DecimalBranchJoin_ReceiverAndArgSlotsStayCorrect()
        {
            var cpp = GenerateDecPhi();

            // The Guid parameter and the int32 comparison result keep their
            // own slot shapes (no over-widening of the whole body).
            var pick = ExtractMethodBody(cpp, "IL2CPP_NOINLINE il2cpp::runtime::Il2CppDecimal DecPhi_Program_Pick_");
            Assert.Contains("il2cpp::Il2CppGuid arg0", pick);
            Assert.Contains("int32_t t0;", pick);
            Assert.Contains("il2cpp::runtime::Il2CppDecimal arg1", pick);
        }

        private static string ExtractMethodBody(string cpp, string fnSignaturePrefix)
        {
            var start = cpp.IndexOf(fnSignaturePrefix, StringComparison.Ordinal);
            Assert.True(start >= 0, $"function signature {fnSignaturePrefix} not found");
            var open = cpp.IndexOf('{', start);
            var depth = 0;
            for (var i = open; i < cpp.Length; i++)
            {
                if (cpp[i] == '{') depth++;
                else if (cpp[i] == '}')
                {
                    depth--;
                    if (depth == 0) return cpp[start..(i + 1)];
                }
            }
            return cpp[start..];
        }
    }
}
