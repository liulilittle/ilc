# clang-mingw-w64.cmake -- Clang 23 (windows-gnu) + MSYS2 MinGW-w64 GCC sysroot
#
# 用途: 让本仓库 tests/cases/<case>/gen 与 src/runtime 用
#       D:\Program Files\mingw\llvm\bin\clang++.exe 以 x86_64-w64-windows-gnu
#       目标编译; libstdc++/mingw 头库与 winpthread 取自 MSYS2 MinGW-w64。
#
# 背景(实测 2026-09-13): 该独立 LLVM 的 MinGW toolchain 不启用 GCC 探测,
# --gcc-toolchain / --gcc-install-dir 均被标 unused, 头/库不会自动查找;
# 且 clang 的 MinGW 驱动不像 GCC 驱动那样自动链接 winpthread。故此处显式注入。
#
# 可覆盖环境变量:
#   IL2CPP_CLANGXX      默认 D:/Program Files/mingw/llvm/bin/clang++.exe
#   IL2CPP_MINGW_ROOT   默认 D:/Program Files/mingw/msys2/mingw64

set(CMAKE_SYSTEM_NAME Windows)

if(DEFINED ENV{IL2CPP_CLANGXX})
    set(CMAKE_CXX_COMPILER "$ENV{IL2CPP_CLANGXX}")
else()
    set(CMAKE_CXX_COMPILER "D:/Program Files/mingw/llvm/bin/clang++.exe")
endif()
set(CMAKE_CXX_COMPILER_TARGET x86_64-w64-windows-gnu)

# 无 GCC 自动探测 -> CMake 编译器探测不能做链接测试, 只编译静态库
set(CMAKE_TRY_COMPILE_TARGET_TYPE STATIC_LIBRARY)

# ccache 复用 (存在即启用; 4.13.6 实测见于 msys2 mingw64\bin)
find_program(CCACHE_PROGRAM ccache)
if(CCACHE_PROGRAM)
    set(CMAKE_CXX_COMPILER_LAUNCHER "${CCACHE_PROGRAM}")
endif()

if(DEFINED ENV{IL2CPP_MINGW_ROOT})
    set(_mingw_root "$ENV{IL2CPP_MINGW_ROOT}")
else()
    set(_mingw_root "D:/Program Files/mingw/msys2/mingw64")
endif()
if(NOT EXISTS "${_mingw_root}/include/c++")
    message(FATAL_ERROR "clang-mingw-w64.cmake: ${_mingw_root} 下无 include/c++ (检查 IL2CPP_MINGW_ROOT)")
endif()

# 取版本号最大的 libstdc++ 头目录 (如 include/c++/16.1.0)
file(GLOB _cxx_candidates LIST_DIRECTORIES true "${_mingw_root}/include/c++/*")
set(_cxx_dirs "")
foreach(_d IN LISTS _cxx_candidates)
    if(IS_DIRECTORY "${_d}" AND "${_d}" MATCHES "/[0-9]+\\.[0-9]+(\\.[0-9]+)?$")
        list(APPEND _cxx_dirs "${_d}")
    endif()
endforeach()
list(SORT _cxx_dirs COMPARE NATURAL ORDER DESCENDING)
list(GET _cxx_dirs 0 _cxx_inc)

# 取版本号最大的 GCC 库目录 (如 lib/gcc/x86_64-w64-mingw32/16.1.0)
file(GLOB _gcc_candidates LIST_DIRECTORIES true "${_mingw_root}/lib/gcc/x86_64-w64-mingw32/*")
set(_gcc_dirs "")
foreach(_d IN LISTS _gcc_candidates)
    if(IS_DIRECTORY "${_d}" AND "${_d}" MATCHES "/[0-9]+\\.[0-9]+(\\.[0-9]+)?$")
        list(APPEND _gcc_dirs "${_d}")
    endif()
endforeach()
list(SORT _gcc_dirs COMPARE NATURAL ORDER DESCENDING)
list(GET _gcc_dirs 0 _gcc_lib)

set(CMAKE_CXX_FLAGS_INIT "-nostdinc++ -isystem \"${_cxx_inc}\" -isystem \"${_cxx_inc}/x86_64-w64-mingw32\" -isystem \"${_cxx_inc}/backward\" -isystem \"${_mingw_root}/x86_64-w64-mingw32/include\" -isystem \"${_mingw_root}/include\"")
set(CMAKE_EXE_LINKER_FLAGS_INIT "-L \"${_mingw_root}/lib\" -L \"${_gcc_lib}\" -L \"${_mingw_root}/x86_64-w64-mingw32/lib\"")

# clang MinGW 驱动不自动链接 winpthread (GCC 驱动会); pthread_mutex_* 需显式
link_libraries(winpthread)
