// mylib.cpp -- native library for the pinvokeexc case (GN-17 probe A): the
// native entry throws a C++ std::exception so the probe verifies the managed
// boundary converts it to System.Runtime.InteropServices.SEHException (net48
// semantics -- the process stays alive and the managed catch runs).
// Exported with C linkage so the transpiled import declaration binds.

#include <cstdint>
#include <stdexcept>

extern "C" {

void native_throw_cpp(void)
{
    throw std::runtime_error("native std::runtime_error");
}

}
