# Linux x86_64 (WSL) 交叉编译 Toolchain
#
# 用途: 从 Windows 主机通过 WSL 交叉编译到 Linux x86_64
# 用法: cmake -DCMAKE_TOOLCHAIN_FILE=cmake/linux-x64-toolchain.cmake -B build-linux -G "Unix Makefiles" ..
#
# 前置条件: 已安装 WSL (Ubuntu 发行版)
# 目标: Linux x86_64 (glibc), 产物为 ELF .a 静态库

set(CMAKE_SYSTEM_NAME Linux)
set(CMAKE_SYSTEM_PROCESSOR x86_64)

# ---------------------------------------------------------------------------
# 查找 WSL 可执行文件
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
# 编译器配置 — 使用 WSL 内的 GCC
#   CMAKE_C_COMPILER_LAUNCHER 会在每次编译命令前插入 wsl.exe
#   实际调用形式: wsl.exe gcc <flags> -c file.cpp -o file.o
# ---------------------------------------------------------------------------
set(CMAKE_C_COMPILER gcc)
set(CMAKE_CXX_COMPILER g++)
set(CMAKE_C_COMPILER_LAUNCHER ${WSL_EXECUTABLE})
set(CMAKE_CXX_COMPILER_LAUNCHER ${WSL_EXECUTABLE})

# ---------------------------------------------------------------------------
# 目标根路径 (WSL 内的 Linux 系统根)
# ---------------------------------------------------------------------------
set(CMAKE_FIND_ROOT_PATH
    "/usr"
    "/usr/local"
    "/lib"
    "/lib64"
)

# ---------------------------------------------------------------------------
# 搜索行为控制
# ---------------------------------------------------------------------------
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE ONLY)

# ---------------------------------------------------------------------------
# Linux 特定定义
# ---------------------------------------------------------------------------
set(CMAKE_EXECUTABLE_SUFFIX "")
set(CMAKE_SHARED_LIBRARY_SUFFIX ".so")
set(CMAKE_STATIC_LIBRARY_SUFFIX ".a")
set(CMAKE_IMPORT_LIBRARY_SUFFIX ".a")

# ---------------------------------------------------------------------------
# Linux 链接器标志
# ---------------------------------------------------------------------------
set(CMAKE_EXE_LINKER_FLAGS "-pthread ${CMAKE_EXE_LINKER_FLAGS}")

# ---------------------------------------------------------------------------
# 调试/发布默认标志
# ---------------------------------------------------------------------------
if(NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type" FORCE)
endif()

set(CMAKE_C_FLAGS_RELEASE "-O2 -DNDEBUG" CACHE STRING "" FORCE)
set(CMAKE_CXX_FLAGS_RELEASE "-O2 -DNDEBUG" CACHE STRING "" FORCE)
