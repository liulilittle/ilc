# clang-mingw-w64.cmake -- Clang 23 (windows-gnu) + MSYS2 MinGW-w64 GCC sysroot
#
# Purpose: let this repo's tests/cases/<case>/gen and src/runtime be compiled with
#          D:\Program Files\mingw\llvm\bin\clang++.exe targeting
#          x86_64-w64-windows-gnu; libstdc++/mingw headers+libs and winpthread come
#          from MSYS2 MinGW-w64.
#
# Background (measured 2026-09-13): this standalone LLVM's MinGW toolchain does not
# enable GCC probing; --gcc-toolchain / --gcc-install-dir are both marked unused and
# headers/libs are not auto-discovered; moreover clang's MinGW driver does not
# auto-link winpthread the way the GCC driver does. So we inject these explicitly here.
#
# Overridable environment variables:
#   IL2CPP_CLANGXX      default D:/Program Files/mingw/llvm/bin/clang++.exe
#   IL2CPP_MINGW_ROOT   default D:/Program Files/mingw/msys2/mingw64

set(CMAKE_SYSTEM_NAME Windows)

if(DEFINED ENV{IL2CPP_CLANGXX})
    set(CMAKE_CXX_COMPILER "$ENV{IL2CPP_CLANGXX}")
else()
    set(CMAKE_CXX_COMPILER "D:/Program Files/mingw/llvm/bin/clang++.exe")
endif()
set(CMAKE_CXX_COMPILER_TARGET x86_64-w64-windows-gnu)

# No GCC auto-probing -> CMake compiler probing cannot do link tests, use static libraries only
set(CMAKE_TRY_COMPILE_TARGET_TYPE STATIC_LIBRARY)

# ccache reuse (enabled if present; 4.13.6 observed under msys2 mingw64\bin)
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
    message(FATAL_ERROR "clang-mingw-w64.cmake: no include/c++ under ${_mingw_root} (check IL2CPP_MINGW_ROOT)")
endif()

# Take the libstdc++ header dir with the highest version (e.g. include/c++/16.1.0)
file(GLOB _cxx_candidates LIST_DIRECTORIES true "${_mingw_root}/include/c++/*")
set(_cxx_dirs "")
foreach(_d IN LISTS _cxx_candidates)
    if(IS_DIRECTORY "${_d}" AND "${_d}" MATCHES "/[0-9]+\\.[0-9]+(\\.[0-9]+)?$")
        list(APPEND _cxx_dirs "${_d}")
    endif()
endforeach()
list(SORT _cxx_dirs COMPARE NATURAL ORDER DESCENDING)
list(GET _cxx_dirs 0 _cxx_inc)

# Take the GCC library dir with the highest version (e.g. lib/gcc/x86_64-w64-mingw32/16.1.0)
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

# The clang MinGW driver does not auto-link winpthread (the GCC driver does); pthread_mutex_* needs it explicit
link_libraries(winpthread)
