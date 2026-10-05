#!/bin/bash
# Linux x86_64 build via WSL — Windows host calls this script to build for Linux
#
# Usage (from Windows PowerShell/CMD):
#   wsl -d Ubuntu2404 -e bash /mnt/e/il2cpp/build-linux.sh
#
# Or from WSL:
#   ./build-linux.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BUILD_DIR="${SCRIPT_DIR}/build-linux"
INSTALL_DIR="${SCRIPT_DIR}/install-linux"

# ---------------------------------------------------------------------------
# 1. 配置
# ---------------------------------------------------------------------------
echo "=== Linux x86_64 Build via WSL ==="
echo "Project: ${SCRIPT_DIR}"
echo "Build:   ${BUILD_DIR}"

cmake -S "${SCRIPT_DIR}/src/runtime" \
      -B "${BUILD_DIR}" \
      -G "Unix Makefiles" \
      -DCMAKE_BUILD_TYPE=Release \
      -DCMAKE_INSTALL_PREFIX="${INSTALL_DIR}" \
      2>&1

# ---------------------------------------------------------------------------
# 2. 构建
# ---------------------------------------------------------------------------
echo ""
echo "--- Build ---"

cmake --build "${BUILD_DIR}" --config Release --parallel "$(nproc)" --verbose 2>&1

# ---------------------------------------------------------------------------
# 3. 验证产物
# ---------------------------------------------------------------------------
echo ""
echo "--- Artifact Verification ---"

LIB="${BUILD_DIR}/libil2cpp_runtime.a"
if [[ -f "${LIB}" ]]; then
    SIZE=$(stat -c%s "${LIB}" 2>/dev/null || stat -f%z "${LIB}" 2>/dev/null)
    echo "OK: libil2cpp_runtime.a (${SIZE} bytes)"
    file "${LIB}"
else
    echo "FAIL: libil2cpp_runtime.a not found"
    exit 1
fi

# ---------------------------------------------------------------------------
# 4. 汇总
# ---------------------------------------------------------------------------
echo ""
echo "=== Build Complete ==="
echo "Build dir: ${BUILD_DIR}"
echo "Library:   ${LIB}"
