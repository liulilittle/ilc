using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using System.Reflection.Metadata;
using Xunit;

// P-F friendly-compiler build forms: the generated CMake project must emit
// three targets sharing the translated sources -- il2cpp_gen (STATIC user
// program library), il2cpp_gen_shared (SHARED loadable C-API library) and
// il2cpp_gen_exe (self-test executable; host.cpp ONLY in the exe). The static
// form must be installable/distributable (install(TARGETS) + IL2CPP_LIB_NAME
// naming) and the runtime must be position-independent so a Linux .so can link
// the static runtime (P-F-AUDIT-1 双审定案 §1/§2/§3).
public class BuildFormTests
{
    private static string FixturePath(string name = "calc")
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

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "il2cppc", "il2cppc.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("cannot locate the repo root from " + AppContext.BaseDirectory);
    }

    private static string GenerateCmake(string fixture = "calc")
    {
        var types = MetadataLoader.LoadAssembly(FixturePath(fixture));
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        try
        {
            CodeGenerator.Generate(types, dir, FixturePath(fixture));
            return File.ReadAllText(Path.Combine(dir, "CMakeLists.txt"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Generate_CmakeEnablesGlobalPositionIndependentCode()
    {
        var cmake = GenerateCmake();
        // P-F-FIX D2: STATIC P/Invoke modules (mylib, add_subdirectory'd into
        // the generated project) must ALSO be PIC -- a Linux .so that folds a
        // non-PIC static lib dies with R_X86_64_32 relocs at high offsets. The
        // project-wide flag covers every target including the submodules.
        Assert.Contains("set(CMAKE_POSITION_INDEPENDENT_CODE ON)", cmake);
    }

    [Fact]
    public void Generate_CmakeSharedInstallCarriesImportArchive()
    {
        var cmake = GenerateCmake();
        // P-F-FIX M1: the Windows import library (lib<name>.dll.a / <name>.lib)
        // must install alongside the DLL so a host can link the C-API.
        Assert.Contains("install(TARGETS il2cpp_gen_shared ARCHIVE DESTINATION lib", cmake);
    }

    [Fact]
    public void Generate_CmakeEmitsStaticSharedExeThreeTargets()
    {
        var cmake = GenerateCmake();
        Assert.Contains("add_library(il2cpp_gen STATIC", cmake);
        Assert.Contains("add_library(il2cpp_gen_shared SHARED", cmake);
        Assert.Contains("add_executable(il2cpp_gen_exe", cmake);
    }

    [Fact]
    public void Generate_CmakeSharedTargetSharesTranslatedSourcesAndEntry()
    {
        var cmake = GenerateCmake();
        // one shared source set (user translations + entry.cpp) feeds BOTH the
        // STATIC and the SHARED user-program libraries; host.cpp never enters it
        var srcSet = cmake.Substring(cmake.IndexOf("set(IL2CPP_GEN_SOURCES", StringComparison.Ordinal));
        srcSet = srcSet.Substring(0, srcSet.IndexOf(")", StringComparison.Ordinal));
        Assert.Contains("src/entry.cpp", srcSet);
        Assert.Contains("Calc_Program.cpp", srcSet);
        Assert.DoesNotContain("src/host.cpp", srcSet);
        Assert.Contains("add_library(il2cpp_gen STATIC ${IL2CPP_GEN_SOURCES})", cmake);
        Assert.Contains("add_library(il2cpp_gen_shared SHARED ${IL2CPP_GEN_SOURCES})", cmake);
    }

    [Fact]
    public void Generate_CmakeExeTargetOnlyHostCpp()
    {
        var cmake = GenerateCmake();
        var exeBlock = cmake.Substring(cmake.IndexOf("add_executable(il2cpp_gen_exe", StringComparison.Ordinal));
        Assert.Contains("src/host.cpp", exeBlock);
        Assert.DoesNotContain("src/entry.cpp", exeBlock);
        Assert.DoesNotContain(".cpp\"", exeBlock); // no user src files in the exe
    }

    [Fact]
    public void Generate_CmakeLinksSharedTargetToRuntime()
    {
        var cmake = GenerateCmake();
        // the SHARED user-program library folds the static runtime in -- the
        // PIC property (runtime CMakeLists) is what makes the .so link succeed
        var sharedBlock = cmake.Substring(cmake.IndexOf("add_library(il2cpp_gen_shared SHARED", StringComparison.Ordinal));
        Assert.Contains("target_link_libraries(il2cpp_gen_shared PUBLIC il2cpp_runtime", sharedBlock);
    }

    [Fact]
    public void Generate_CmakeEmitsInstallRules()
    {
        var cmake = GenerateCmake();
        Assert.Contains("install(TARGETS il2cpp_gen ARCHIVE DESTINATION lib)", cmake);
        Assert.Contains("install(TARGETS il2cpp_gen_shared", cmake);
        Assert.Contains("install(DIRECTORY include/ DESTINATION include)", cmake);
    }

    [Fact]
    public void Generate_CmakeSupportsLibNameOverride()
    {
        var cmake = GenerateCmake();
        Assert.Contains("if(IL2CPP_LIB_NAME)", cmake);
        Assert.Contains("set_target_properties(il2cpp_gen PROPERTIES OUTPUT_NAME \"${IL2CPP_LIB_NAME}\")", cmake);
        Assert.Contains("set_target_properties(il2cpp_gen_shared PROPERTIES OUTPUT_NAME \"${IL2CPP_LIB_NAME}\")", cmake);
    }

    [Fact]
    public void Generate_CmakeWindowsExportAllSymbolsOnShared()
    {
        var cmake = GenerateCmake();
        // WIN32 insurance: auto-export the DLL symbols on MSVC/MinGW; the
        // property must be guarded by if(WIN32) so POSIX builds are unaffected
        Assert.Contains("if(WIN32)", cmake);
        Assert.Contains("set_target_properties(il2cpp_gen_shared PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON)", cmake);
    }

    [Fact]
    public void Runtime_CmakeHasPositionIndependentCode()
    {
        var runtimeCmake = File.ReadAllText(Path.Combine(RepoRoot(), "src", "runtime", "CMakeLists.txt"));
        // P-F 双审定案 §1: the static runtime must build PIC so a Linux .so
        // target can fold it without non-PIC relocations
        Assert.Contains("POSITION_INDEPENDENT_CODE ON", runtimeCmake);
    }

    [Fact]
    public void Runtime_CmakeInstallsArchiveAndHeaders()
    {
        var runtimeCmake = File.ReadAllText(Path.Combine(RepoRoot(), "src", "runtime", "CMakeLists.txt"));
        Assert.Contains("install(TARGETS il2cpp_runtime ARCHIVE DESTINATION lib)", runtimeCmake);
        Assert.Contains("install(DIRECTORY include/ DESTINATION include)", runtimeCmake);
    }
}
