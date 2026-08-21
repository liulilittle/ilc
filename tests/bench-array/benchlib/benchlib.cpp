//------------------------------------------------------------------------------
// benchlib.cpp -- native timing source for the benchmark suite.
//
// The M-stage transpiler subset cannot reach System.Diagnostics.Stopwatch /
// System.DateTime / Environment.TickCount (no icall wiring in
// MethodRefResolver), so the benchmark harness obtains its clock through
// P/Invoke (M4, fully supported):
//
//   int now_us()  -- monotonic steady-clock microseconds since an arbitrary
//                    epoch, wrapped to [0, 2^31). 31 bits cover ~35 minutes,
//                    far beyond any benchmark; elapsed = now_us() - start
//                    (int32 subtraction, no wrap for such short windows).
//
// The SAME binary serves both benchmark runs:
//   * net48 reference run: built as a shared DLL (g++ -shared) next to
//     bench.exe -- the CLR loads benchlib.dll via [DllImport("benchlib")].
//   * transpiled run: built as the STATIC target "benchlib" (this directory
//     is add_subdirectory'd by the generated project, the mylib pattern) and
//     linked into il2cpp_gen_exe.
//------------------------------------------------------------------------------
#include <chrono>

extern "C" int now_us()
{
    const long long us = std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count();
    return static_cast<int>(us & 0x7FFFFFFFLL);
}
