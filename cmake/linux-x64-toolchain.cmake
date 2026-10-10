# Linux x86_64 (WSL) cross-compilation Toolchain
#
# Purpose: cross-compile from a Windows host to Linux x86_64 via WSL
# Usage: cmake -DCMAKE_TOOLCHAIN_FILE=cmake/linux-x64-toolchain.cmake -B build-linux -G "Unix Makefiles" ..
#
# Prerequisite: WSL installed (Ubuntu distribution)
# Target: Linux x86_64 (glibc), artifact is an ELF .a static library

set(CMAKE_SYSTEM_NAME Linux)
set(CMAKE_SYSTEM_PROCESSOR x86_64)

# ---------------------------------------------------------------------------
# Find the WSL executable
# ---------------------------------------------------------------------------
find_program(WSL_EXECUTABLE
    NAMES wsl.exe wsl
    PATHS
        "C:/Windows/System32"
        "C:/Windows/SysWOW64"
    DOC "WSL executable"
)

if(NOT WSL_EXECUTABLE)
    message(FATAL_ERROR "WSL not found. Install WSL first: wsl --install")
endif()

message(STATUS "WSL executable: ${WSL_EXECUTABLE}")

# ---------------------------------------------------------------------------
# Compiler configuration -- use GCC inside WSL
#   CMAKE_C_COMPILER_LAUNCHER inserts wsl.exe before every compile command
#   Actual invocation form: wsl.exe gcc <flags> -c file.cpp -o file.o
# ---------------------------------------------------------------------------
set(CMAKE_C_COMPILER gcc)
set(CMAKE_CXX_COMPILER g++)
set(CMAKE_C_COMPILER_LAUNCHER ${WSL_EXECUTABLE})
set(CMAKE_CXX_COMPILER_LAUNCHER ${WSL_EXECUTABLE})

# ---------------------------------------------------------------------------
# Target root path (the Linux system root inside WSL)
# ---------------------------------------------------------------------------
set(CMAKE_FIND_ROOT_PATH
    "/usr"
    "/usr/local"
    "/lib"
    "/lib64"
)

# ---------------------------------------------------------------------------
# Search behavior control
# ---------------------------------------------------------------------------
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE ONLY)

# ---------------------------------------------------------------------------
# Linux-specific definitions
# ---------------------------------------------------------------------------
set(CMAKE_EXECUTABLE_SUFFIX "")
set(CMAKE_SHARED_LIBRARY_SUFFIX ".so")
set(CMAKE_STATIC_LIBRARY_SUFFIX ".a")
set(CMAKE_IMPORT_LIBRARY_SUFFIX ".a")

# ---------------------------------------------------------------------------
# Linux linker flags
# ---------------------------------------------------------------------------
set(CMAKE_EXE_LINKER_FLAGS "-pthread ${CMAKE_EXE_LINKER_FLAGS}")

# ---------------------------------------------------------------------------
# Debug/Release default flags
# ---------------------------------------------------------------------------
if(NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type" FORCE)
endif()

set(CMAKE_C_FLAGS_RELEASE "-O2 -DNDEBUG" CACHE STRING "" FORCE)
set(CMAKE_CXX_FLAGS_RELEASE "-O2 -DNDEBUG" CACHE STRING "" FORCE)
