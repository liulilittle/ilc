# MinGW-w64 交叉编译 Toolchain
#
# 用途: 从 Linux 或 Windows 主机交叉编译到 Windows x86_64
# 用法: cmake -DCMAKE_TOOLCHAIN_FILE=cmake/mingw-w64.cmake -B build-mingw ..
#
# 自动检测优先级:
#   1. 环境变量 MINGW_ROOT (显式指定 mingw-w64 安装根)
#   2. 常见安装路径扫描 (Linux: /usr, /usr/x86_64-w64-mingw32; Windows: MSYS2, ming-w64)
#   3. PATH 中查找 x86_64-w64-mingw32-gcc
#
# 支持主机: Linux (Debian/Ubuntu/Fedora/Arch), Windows (MSYS2/MinGW-w64/Native)

set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR x86_64)

# ---------------------------------------------------------------------------
# 编译器自动检测
# ---------------------------------------------------------------------------
if(NOT DEFINED CMAKE_C_COMPILER)
    # 优先使用环境变量 MINGW_ROOT
    if(DEFINED ENV{MINGW_ROOT})
        set(_MINGW_PREFIX "$ENV{MINGW_ROOT}/bin/x86_64-w64-mingw32-")
    elseif(DEFINED ENV{MINGW64_ROOT})
        set(_MINGW_PREFIX "$ENV{MINGW64_ROOT}/bin/x86_64-w64-mingw32-")
    elseif(DEFINED ENV{MSYSTEM_PREFIX})
        # MSYS2
        set(_MINGW_PREFIX "$ENV{MSYSTEM_PREFIX}/bin/x86_64-w64-mingw32-")
    else()
        # 默认前缀 — 允许 PATH 中的 x86_64-w64-mingw32-gcc 被找到
        set(_MINGW_PREFIX "x86_64-w64-mingw32-")
    endif()

    find_program(_MINGW_C_COMPILER
        NAMES ${_MINGW_PREFIX}gcc
              ${_MINGW_PREFIX}clang
              x86_64-w64-mingw32-gcc
              x86_64-w64-mingw32-clang
              clang
        PATHS
            "$ENV{MINGW_ROOT}/bin"
            "$ENV{MINGW64_ROOT}/bin"
            "/usr/bin"
            "/usr/x86_64-w64-mingw32/bin"
            "/mingw64/bin"
            "C:/msys64/mingw64/bin"
            "C:/Program Files/ming-w64/bin"
            "C:/mingw64/bin"
        DOC "MinGW-w64 C compiler"
    )

    find_program(_MINGW_CXX_COMPILER
        NAMES ${_MINGW_PREFIX}g++
              ${_MINGW_PREFIX}clang++
              x86_64-w64-mingw32-g++
              x86_64-w64-mingw32-clang++
              clang++
        PATHS
            "$ENV{MINGW_ROOT}/bin"
            "$ENV{MINGW64_ROOT}/bin"
            "/usr/bin"
            "/usr/x86_64-w64-mingw32/bin"
            "/mingw64/bin"
            "C:/msys64/mingw64/bin"
            "C:/Program Files/ming-w64/bin"
            "C:/mingw64/bin"
        DOC "MinGW-w64 C++ compiler"
    )

    if(_MINGW_C_COMPILER)
        set(CMAKE_C_COMPILER "${_MINGW_C_COMPILER}")
    else()
        message(FATAL_ERROR "MinGW-w64 C compiler not found. Set MINGW_ROOT or ensure x86_64-w64-mingw32-gcc is in PATH.")
    endif()

    if(_MINGW_CXX_COMPILER)
        set(CMAKE_CXX_COMPILER "${_MINGW_CXX_COMPILER}")
    else()
        message(FATAL_ERROR "MinGW-w64 C++ compiler not found. Set MINGW_ROOT or ensure x86_64-w64-mingw32-g++ is in PATH.")
    endif()
endif()

message(STATUS "MinGW-w64 C compiler: ${CMAKE_C_COMPILER}")
message(STATUS "MinGW-w64 C++ compiler: ${CMAKE_CXX_COMPILER}")

# ---------------------------------------------------------------------------
# 目标根路径 (用于查找库和头文件)
# ---------------------------------------------------------------------------
if(DEFINED ENV{MINGW_ROOT})
    set(CMAKE_FIND_ROOT_PATH "$ENV{MINGW_ROOT}/x86_64-w64-mingw32")
elseif(DEFINED ENV{MINGW64_ROOT})
    set(CMAKE_FIND_ROOT_PATH "$ENV{MINGW64_ROOT}/x86_64-w64-mingw32")
elseif(DEFINED ENV{MSYSTEM_PREFIX})
    set(CMAKE_FIND_ROOT_PATH "$ENV{MSYSTEM_PREFIX}/x86_64-w64-mingw32")
else()
    # 常见默认路径 — 按探测优先级排列
    set(CMAKE_FIND_ROOT_PATH
        "C:/Program Files/ming-w64/x86_64-w64-mingw32"
        "C:/msys64/mingw64/x86_64-w64-mingw32"
        "/usr/x86_64-w64-mingw32"
        "/mingw64/x86_64-w64-mingw32"
    )
endif()

# ---------------------------------------------------------------------------
# 搜索行为控制
#   PROGRAM: 在主机上查找可执行程序 (如 cmake, pkg-config)
#   LIBRARY: 只在目标路径中查找库 (交叉编译的 .a/.dll.a)
#   INCLUDE: 只在目标路径中查找头文件
#   PACKAGE: 只在目标路径中查找包
# ---------------------------------------------------------------------------
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_PACKAGE ONLY)

# ---------------------------------------------------------------------------
# Windows 特定定义
# ---------------------------------------------------------------------------
set(CMAKE_EXECUTABLE_SUFFIX ".exe")
set(CMAKE_SHARED_LIBRARY_SUFFIX ".dll")
set(CMAKE_STATIC_LIBRARY_SUFFIX ".a")
set(CMAKE_IMPORT_LIBRARY_SUFFIX ".dll.a")

# ---------------------------------------------------------------------------
# 静态链接偏好 (MinGW-w64 默认静态链接运行时, 避免依赖 libgcc_s_seh-1.dll 等)
# ---------------------------------------------------------------------------
set(CMAKE_EXE_LINKER_FLAGS "-static-libgcc -static-libstdc++ ${CMAKE_EXE_LINKER_FLAGS}")

# ---------------------------------------------------------------------------
# 调试/发布默认标志
# ---------------------------------------------------------------------------
if(NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type" FORCE)
endif()

set(CMAKE_C_FLAGS_RELEASE "-O2 -DNDEBUG" CACHE STRING "" FORCE)
set(CMAKE_CXX_FLAGS_RELEASE "-O2 -DNDEBUG" CACHE STRING "" FORCE)
