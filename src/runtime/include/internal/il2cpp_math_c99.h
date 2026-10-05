#pragma once
//------------------------------------------------------------------------------
// il2cpp_math_c99.h -- GCC/libstdc++ C99-math <cmath> compatibility shim.
//
// WHY THIS FILE EXISTS (audit 2026-08-08, M6 batch 23):
//
// libstdc++ (GCC) exposes the C99/C++11 math additions in namespace std only
// under feature macros. Two INDEPENDENT gaps affect real GCC 8.1:
//
//   (A) C99/C++11 additions (acosh asinh atanh cbrt ilogb log2 nan remainder
//       scalbn trunc round + their -f/-l variants) are gated by
//       _GLIBCXX11_USE_C99_MATH. On MinGW-w64 GCC 8.1 the configure-time C99
//       check fails against the classic MSVCRT (lacks cbrt/log2/acosh/...),
//       so the macro is left UNDEFINED and <cmath> declares NONE of these
//       names in std.
//   (B) The -f float variants of the C89/C++98 core functions (sqrtf sinf
//       cosf fabsf powf ceilf floorf fmodf ...) were absent from namespace
//       std in ALL libstdc++ releases before GCC 14 (PR79700, fixed r14-341)
//       -- this is NOT controlled by the C99 feature macro at all.
//
// The runtime math icalls (mathf.cpp / mathext.cpp / math.cpp) therefore FAIL
// TO COMPILE on real GCC 8.1, even though they build cleanly on clang+libc++
// / MSVC (which always provide the std:: names). The C global names (::sqrtf,
// ::acosh, ...) from <math.h> are present on EVERY toolchain (MinGW-w64 ships
// a full C99 math.h), so the standard-compliant names are re-exported into
// namespace std under the affected libstdc++ builds only.
//
// This shim is INERT (compiles to nothing) on:
//   * clang/libc++   -- __GLIBCXX__ undefined, std:: names always present
//   * MSVC/UCRT      -- __GLIBCXX__ undefined
//   * GCC 14+        -- __GLIBCXX__ >= 20230511 (the PR79700 fix date), names
//                       present natively; no re-declaration is emitted
// It activates ONLY on the affected libstdc++ builds (GCC 8.1 MinGW and any
// pre-GCC-14 libstdc++), where the names are absent.
//------------------------------------------------------------------------------

#include <cmath>
#include <math.h>

#if defined(__GLIBCXX__)
// (A) C99/C++11 additions -- only when the feature macro is undefined (the
// MinGW-w64 GCC 8.1 case). Where the macro IS defined these already exist in
// std, so re-exporting them would be a redeclaration error -- hence the
// separate guard.
#if !defined(_GLIBCXX11_USE_C99_MATH)
namespace std {
using ::acosh; using ::asinh; using ::atanh; using ::cbrt;
using ::ilogb; using ::log2; using ::nan; using ::remainder;
using ::round; using ::scalbn; using ::trunc;
using ::acoshf; using ::asinhf; using ::atanhf; using ::cbrtf;
using ::ilogbf; using ::log2f; using ::nanf; using ::remainderf;
using ::roundf; using ::scalbnf; using ::truncf;
} // namespace std
#endif

// (B) -f float variants of the C89/C++98 core -- absent from std in every
// libstdc++ before GCC 14 (PR79700). __GLIBCXX__ is a release-date macro
// (GCC 8.1 == 20180414, GCC 13.1 == 20230408, GCC 14.1 == 20240418); the fix
// landed 2023-05-11, so anything older needs the re-export.
#if defined(__GLIBCXX__) && __GLIBCXX__ < 20230511
namespace std {
using ::sqrtf; using ::fabsf; using ::sinf; using ::cosf; using ::tanf;
using ::asinf; using ::acosf; using ::atanf; using ::atan2f; using ::expf;
using ::logf; using ::log10f; using ::powf; using ::ceilf; using ::floorf;
using ::fmodf; using ::modff; using ::coshf; using ::sinhf; using ::tanhf;
} // namespace std
#endif
#endif // defined(__GLIBCXX__)
