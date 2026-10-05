#!/bin/bash
# MinGW-w64 交叉编译自动化脚本 — Linux/Unix 主机使用
#
# 用法:
#   ./build-mingw.sh [OPTIONS]
#
# 选项:
#   -b, --build-dir DIR    构建目录 (默认: build-mingw)
#   -t, --build-type TYPE  构建类型 (Debug/Release/RelWithDebInfo/MinSizeRel, 默认: Release)
#   -j, --jobs N           并行任务数 (默认: nproc)
#   -m, --mingw-root PATH  显式指定 MinGW 根目录
#   -g, --generator GEN    CMake 生成器 (默认: Unix Makefiles 或 Ninja)
#   -c, --clean            清理后重新构建
#   --no-tests             不运行测试
#   -h, --help             显示帮助

set -euo pipefail

# ---------------------------------------------------------------------------
# 颜色输出
# ---------------------------------------------------------------------------
RED='\033[0;31m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
NC='\033[0m'

info()  { echo -e "${CYAN}[INFO]${NC} $*"; }
ok()    { echo -e "${GREEN}[OK]${NC} $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC} $*"; }
fatal() { echo -e "${RED}[FAIL]${NC} $*"; exit 1; }

# ---------------------------------------------------------------------------
# 默认参数
# ---------------------------------------------------------------------------
BUILD_DIR="build-mingw"
BUILD_TYPE="Release"
JOBS=$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4)
MINGW_ROOT=""
GENERATOR=""
CLEAN=0
RUN_TESTS=1

# ---------------------------------------------------------------------------
# 解析参数
# ---------------------------------------------------------------------------
while [[ $# -gt 0 ]]; do
    case $1 in
        -b|--build-dir)   BUILD_DIR="$2"; shift 2 ;;
        -t|--build-type)  BUILD_TYPE="$2"; shift 2 ;;
        -j|--jobs)        JOBS="$2"; shift 2 ;;
        -m|--mingw-root)  MINGW_ROOT="$2"; shift 2 ;;
        -g|--generator)   GENERATOR="$2"; shift 2 ;;
        -c|--clean)       CLEAN=1; shift ;;
        --no-tests)       RUN_TESTS=0; shift ;;
        -h|--help)
            head -15 "$0" | grep -E "^#" | sed 's/^# \?//'
            exit 0
            ;;
        *) warn "未知参数: $1"; shift ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR"

# ---------------------------------------------------------------------------
# 1. 检测 MinGW-w64
# ---------------------------------------------------------------------------
echo ""
echo "=== MinGW-w64 交叉编译构建 ==="
echo "Project: $PROJECT_ROOT"

if [[ -n "$MINGW_ROOT" ]]; then
    info "使用显式 MinGW 根目录: $MINGW_ROOT"
    export MINGW_ROOT
fi

# 查找 C 编译器
find_mingw_compiler() {
    local names=("x86_64-w64-mingw32-gcc" "x86_64-w64-mingw32-clang" "clang")
    local paths=(
        "${MINGW_ROOT}/bin"
        "${MINGW64_ROOT}/bin"
        "/usr/bin"
        "/usr/x86_64-w64-mingw32/bin"
        "/mingw64/bin"
        "/usr/lib/ccache"
    )

    for name in "${names[@]}"; do
        # 先查 PATH
        if command -v "$name" &>/dev/null; then
            echo "$(command -v "$name")"
            return 0
        fi
        # 扫描路径
        for p in "${paths[@]}"; do
            if [[ -x "$p/$name" ]]; then
                echo "$p/$name"
                return 0
            fi
        done
    done
    return 1
}

CC="$(find_mingw_compiler)" || fatal "未找到 MinGW-w64 交叉编译器 (x86_64-w64-mingw32-gcc)"
CXX="${CC/gcc/g++}"
if [[ ! -x "$CXX" ]]; then
    CXX="${CC/clang/clang++}"
fi

info "C 编译器: $CC"
"$CC" --version | head -1
info "C++ 编译器: $CXX"

# ---------------------------------------------------------------------------
# 2. 检测 CMake
# ---------------------------------------------------------------------------
command -v cmake &>/dev/null || fatal "未找到 cmake"
info "CMake: $(cmake --version | head -1)"

# ---------------------------------------------------------------------------
# 3. 检测生成器
# ---------------------------------------------------------------------------
if [[ -z "$GENERATOR" ]]; then
    if command -v ninja &>/dev/null; then
        GENERATOR="Ninja"
    else
        GENERATOR="Unix Makefiles"
    fi
fi
info "生成器: $GENERATOR"
info "并行任务: $JOBS"

# ---------------------------------------------------------------------------
# 4. 清理 (可选)
# ---------------------------------------------------------------------------
BUILD_FULL_PATH="$PROJECT_ROOT/$BUILD_DIR"
if [[ $CLEAN -eq 1 && -d "$BUILD_FULL_PATH" ]]; then
    info "清理构建目录"
    rm -rf "$BUILD_FULL_PATH"
fi

# ---------------------------------------------------------------------------
# 5. CMake 配置
# ---------------------------------------------------------------------------
echo ""
echo "--- CMake 配置 ---"

TOOLCHAIN_FILE="$PROJECT_ROOT/cmake/mingw-w64.cmake"
[[ -f "$TOOLCHAIN_FILE" ]] || fatal "Toolchain 文件不存在: $TOOLCHAIN_FILE"
info "Toolchain: $TOOLCHAIN_FILE"

# 检测根目录 CMakeLists.txt
if [[ -f "$PROJECT_ROOT/CMakeLists.txt" ]]; then
    SOURCE_DIR="$PROJECT_ROOT"
elif [[ -f "$PROJECT_ROOT/src/runtime/CMakeLists.txt" ]]; then
    SOURCE_DIR="$PROJECT_ROOT/src/runtime"
    info "根目录无 CMakeLists.txt, 使用 src/runtime"
else
    fatal "未找到 CMakeLists.txt"
fi

CMAKE_ARGS=(
    -S "$SOURCE_DIR"
    -B "$BUILD_FULL_PATH"
    -G "$GENERATOR"
    -DCMAKE_BUILD_TYPE="$BUILD_TYPE"
    -DCMAKE_TOOLCHAIN_FILE="$TOOLCHAIN_FILE"
    -DCMAKE_C_COMPILER="$CC"
    -DCMAKE_CXX_COMPILER="$CXX"
)

info "cmake ${CMAKE_ARGS[*]}"
cmake "${CMAKE_ARGS[@]}"
ok "CMake 配置完成"

# ---------------------------------------------------------------------------
# 6. CMake 构建
# ---------------------------------------------------------------------------
echo ""
echo "--- 构建 ($BUILD_TYPE) ---"

cmake --build "$BUILD_FULL_PATH" --config "$BUILD_TYPE" --parallel "$JOBS" --verbose
ok "构建完成 (0 errors)"

# ---------------------------------------------------------------------------
# 7. PE 格式验证
# ---------------------------------------------------------------------------
echo ""
echo "--- PE 格式验证 ---"

shopt -s globstar nullglob
PE_FILES=("$BUILD_FULL_PATH"/**/*.{exe,dll})
LIB_FILES=("$BUILD_FULL_PATH"/**/*.a)

if [[ ${#PE_FILES[@]} -eq 0 ]]; then
    warn "未找到 PE 文件"
else
    info "找到 ${#PE_FILES[@]} 个 PE 文件, ${#LIB_FILES[@]} 个静态库"
    for pe in "${PE_FILES[@]}"; do
        if [[ -f "$pe" ]]; then
            MAGIC=$(xxd -l 2 -p "$pe" 2>/dev/null || echo "0000")
            if [[ "$MAGIC" == "4d5a" ]]; then
                ok "PE: $(basename "$pe") ($(stat -c%s "$pe" 2>/dev/null || stat -f%z "$pe" 2>/dev/null) bytes)"
            else
                warn "PE: $(basename "$pe") 缺少 MZ 头 (magic: $MAGIC)"
            fi
        fi
    done
fi

# ---------------------------------------------------------------------------
# 8. 汇总
# ---------------------------------------------------------------------------
echo ""
echo "=== 构建结果 ==="
echo "构建目录: $BUILD_FULL_PATH"
echo "构建类型: $BUILD_TYPE"
echo "编译器  : $CC"

if [[ ${#LIB_FILES[@]} -gt 0 ]]; then
    echo ""
    echo "静态库:"
    for lib in "${LIB_FILES[@]}"; do
        echo "  $(basename "$lib")"
    done
fi

echo ""
echo "=== MinGW 交叉编译构建成功 ==="
