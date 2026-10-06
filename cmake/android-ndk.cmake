# Android NDK toolchain (P-F 一键入口 -Toolchain ndk 骨架，P-F-AUDIT-1 §2/§6/§8)。
#
# 此文件是跨平台 friendly-compiler 的 NDK 构建入口：它复用 NDK 自带的
# android.toolchain.cmake，面向 ABI arm64-v8a / minSdk 21 生成静态库/动态库
# 目标。Bionic 下的链接解析（Threads::Threads / CMAKE_DL_LIBS / ws2_32 分流）
# 属后续验证项——ws2_32 在 src/runtime 的 if(WIN32) 内，B/NDK 不会误链。
#
# NDK 根目录从**环境变量**发现（P-F-FIX D3：禁止 -DANDROID_NDK 直连——cmake
# 3.22 的 ABI try_compile 子构建不注入非 CMAKE_ 前缀缓存变量，直接传 -D
# 会在子构建内 FATAL；env 沿父进程传递天然可用）：
#   优先 IL2CPP_NDK_ROOT，其次 NDK_ROOT。
# 同时在缓存里写入 CMAKE_ANDROID_NDK（CMAKE_ 前缀，try_compile 子构建继承），
# 与 ANDROID_NDK 双写以保证 android.toolchain.cmake 在新旧版本上都能解析。
#
# 用法（经 scripts/build-app.ps1 -Toolchain ndk 一键，或直接）：
#   $env:IL2CPP_NDK_ROOT="<ndk 根目录>"
#   cmake -S <gen> -B <build> -G Ninja \
#         -DCMAKE_BUILD_TYPE=Release \
#         -DCMAKE_TOOLCHAIN_FILE=cmake/android-ndk.cmake \
#         -DCMAKE_ANDROID_NDK=$env:IL2CPP_NDK_ROOT ..

set(CMAKE_SYSTEM_NAME Android)

# ABI / API 默认值：可经 -DANDROID_ABI / -DANDROID_PLATFORM 覆盖。
# G5（CROSSPLATFORM-DECISION 批 B）：去 FORCE 改普通 CACHE，使
# -DANDROID_ABI=armeabi-v7a / x86_64 等非默认 ABI 真正可覆盖（FORCE 会把
# -D 值重新钉回 arm64-v8a，见 RED 证据）。
set(ANDROID_ABI "arm64-v8a" CACHE STRING "Android ABI")
set(ANDROID_PLATFORM "android-21" CACHE STRING "Android API level" FORCE)
# 共享库默认使用 c++_shared；静态库场景可改 ANDROID_STL=c++_static。
set(ANDROID_STL "c++_shared" CACHE STRING "Android STL" FORCE)

# ---------------------------------------------------------------------------
# NDK 根目录发现：仅 env（IL2CPP_NDK_ROOT / NDK_ROOT）；-D 传入仅在
# CMAKE_ANDROID_NDK 形态（CMAKE_ 前缀）下作为兜底接受。
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

# 双写 ANDROID_NDK（android.toolchain.cmake 传统入口）与 CMAKE_ANDROID_NDK
# （CMAKE_ 前缀，ABI try_compile 子构建可继承；P-F-FIX D3）。
set(ANDROID_NDK "${_PF_NDK_ROOT}" CACHE PATH "Android NDK root" FORCE)
set(CMAKE_ANDROID_NDK "${_PF_NDK_ROOT}" CACHE PATH "Android NDK root" FORCE)

if(WIN32)
    file(TO_CMAKE_PATH "${ANDROID_NDK}" ANDROID_NDK_CMAKE)
else()
    set(ANDROID_NDK_CMAKE "${ANDROID_NDK}")
endif()
include("${ANDROID_NDK_CMAKE}/build/cmake/android.toolchain.cmake")