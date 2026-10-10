# Android NDK toolchain (P-F one-click entry -Toolchain ndk skeleton, P-F-AUDIT-1 sections 2/6/8).
#
# This file is the NDK build entry for the cross-platform friendly-compiler: it reuses
# the NDK-provided android.toolchain.cmake, targeting ABI arm64-v8a / minSdk 21 to
# generate static/shared library targets. Link resolution under Bionic
# (Threads::Threads / CMAKE_DL_LIBS / ws2_32 routing) is a follow-up verification
# item -- ws2_32 lives inside if(WIN32) in src/runtime, so B/NDK never links it wrongly.
#
# The NDK root is discovered from **environment variables** (P-F-FIX D3: -DANDROID_NDK
# direct pass-through is forbidden -- cmake 3.22's ABI try_compile sub-build does not
# inject non-CMAKE_-prefixed cache variables, so passing -D directly causes a FATAL in
# the sub-build; env vars propagate along the parent process and are naturally usable):
#   Prefer IL2CPP_NDK_ROOT, then NDK_ROOT.
# Also write CMAKE_ANDROID_NDK into the cache (CMAKE_ prefix, inherited by try_compile
# sub-builds), dual-written with ANDROID_NDK so android.toolchain.cmake parses on both
# old and new versions.
#
# Usage (via scripts/build-app.ps1 -Toolchain ndk one-click, or directly):
#   $env:IL2CPP_NDK_ROOT="<ndk root dir>"
#   cmake -S <gen> -B <build> -G Ninja \
#         -DCMAKE_BUILD_TYPE=Release \
#         -DCMAKE_TOOLCHAIN_FILE=cmake/android-ndk.cmake \
#         -DCMAKE_ANDROID_NDK=$env:IL2CPP_NDK_ROOT ..

set(CMAKE_SYSTEM_NAME Android)

# ABI / API defaults: overridable via -DANDROID_ABI / -DANDROID_PLATFORM.
# G5 (CROSSPLATFORM-DECISION batch B): drop FORCE and use a plain CACHE entry so
# non-default ABIs like -DANDROID_ABI=armeabi-v7a / x86_64 can truly override (FORCE
# would pin the -D value back to arm64-v8a, see RED evidence).
set(ANDROID_ABI "arm64-v8a" CACHE STRING "Android ABI")
set(ANDROID_PLATFORM "android-21" CACHE STRING "Android API level" FORCE)
# Shared libraries default to c++_shared; static-library scenarios can set ANDROID_STL=c++_static.
set(ANDROID_STL "c++_shared" CACHE STRING "Android STL" FORCE)

# ---------------------------------------------------------------------------
# NDK root discovery: env only (IL2CPP_NDK_ROOT / NDK_ROOT); a -D pass-through is
# only accepted as a fallback in the CMAKE_ANDROID_NDK (CMAKE_-prefixed) form.
# ---------------------------------------------------------------------------
set(_PF_NDK_ROOT "")
if(DEFINED ENV{IL2CPP_NDK_ROOT})
    set(_PF_NDK_ROOT "$ENV{IL2CPP_NDK_ROOT}")
elseif(DEFINED ENV{NDK_ROOT})
    set(_PF_NDK_ROOT "$ENV{NDK_ROOT}")
elseif(DEFINED CMAKE_ANDROID_NDK)
    set(_PF_NDK_ROOT "${CMAKE_ANDROID_NDK}")
else()
    message(FATAL_ERROR
        "android-ndk.cmake needs the IL2CPP_NDK_ROOT (or NDK_ROOT) env var; "
        "no machine paths are hardcoded")
endif()

if(NOT EXISTS "${_PF_NDK_ROOT}/build/cmake/android.toolchain.cmake")
    message(FATAL_ERROR "android-ndk.cmake: invalid NDK root '${_PF_NDK_ROOT}' "
        "(android.toolchain.cmake not found under build/cmake/)")
endif()

message(STATUS "Android NDK: ${_PF_NDK_ROOT} (ABI=${ANDROID_ABI}, platform=${ANDROID_PLATFORM})")

# Dual-write ANDROID_NDK (the traditional android.toolchain.cmake entry) and
# CMAKE_ANDROID_NDK (CMAKE_ prefix, inheritable by ABI try_compile sub-builds; P-F-FIX D3).
set(ANDROID_NDK "${_PF_NDK_ROOT}" CACHE PATH "Android NDK root" FORCE)
set(CMAKE_ANDROID_NDK "${_PF_NDK_ROOT}" CACHE PATH "Android NDK root" FORCE)

if(WIN32)
    file(TO_CMAKE_PATH "${ANDROID_NDK}" ANDROID_NDK_CMAKE)
else()
    set(ANDROID_NDK_CMAKE "${ANDROID_NDK}")
endif()
include("${ANDROID_NDK_CMAKE}/build/cmake/android.toolchain.cmake")