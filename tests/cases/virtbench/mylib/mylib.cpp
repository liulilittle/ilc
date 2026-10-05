// mylib.cpp -- steady-clock microsecond counter for the virtbench timing
// (the same pattern as tests/bench/benchlib: net48 loads it as mylib.dll via
// [DllImport("mylib")]; the transpiled build links the same source as a
// static library through mylib/CMakeLists.txt).
#include <chrono>
#include <cstdint>

extern "C" {

// Steady-clock microseconds, wrapped into the int32 window ([0, 2^31) -- a
// little over 35 minutes, far beyond any benchmark here).
int32_t now_us()
{
    return static_cast<int32_t>(
        std::chrono::duration_cast<std::chrono::microseconds>(
            std::chrono::steady_clock::now().time_since_epoch()).count());
}

} // extern "C"
