#!/bin/bash
# One-command build for an IL2CPP-transpiled application -- POSIX/WSL mirror of
# scripts/build-app.ps1 (P-F friendly-compiler one-click, P-F-AUDIT-1 §5).
#
#   -Input <csproj|exe> : managed program (default: required)
#   -Kind exe|static|shared
#   -Toolchain mingw|wsl|ndk   (msvc is Windows-only; see build-app.ps1)
#   -OutDir <dir>
#   -Clean
#
# Pipeline: [dotnet build] -> il2cppc transpile -> cmake configure -> cmake
# build --target <form> -> artifacts + side native libraries (P/Invoke SHARED
# modules) copied next to the artifact so dlopen finds them at runtime.
# Toolchains are discovered from env only (IL2CPP_MINGW_ROOT, IL2CPP_WSL_DISTRO,
# IL2CPP_NDK_ROOT) -- no machine-specific paths.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(dirname "${SCRIPT_DIR}")"

INPUT=""
KIND="exe"
TOOLCHAIN="mingw"
OUTDIR=""
LIBNAME=""
CONFIG="Release"
CLEAN=0

usage() {
    sed -n '2,22p' "${SCRIPT_DIR}/build-app.sh"
    exit 1
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -Input)      INPUT="$2"; shift 2 ;;
        -Kind)       KIND="$2"; shift 2 ;;
        -Toolchain)  TOOLCHAIN="$2"; shift 2 ;;
        -OutDir)     OUTDIR="$2"; shift 2 ;;
        -LibName)    LIBNAME="$2"; shift 2 ;;
        -Config)     CONFIG="$2"; shift 2 ;;
        -Clean)      CLEAN=1; shift ;;
        *) echo "unknown option: $1"; usage ;;
    esac
done

if [[ -z "${INPUT}" ]]; then
    echo "ERROR: -Input <csproj|exe|dll> is required"
    usage
fi
if [[ "${INPUT}" != /* && ! -e "${INPUT}" ]]; then
    INPUT="$(pwd)/${INPUT}"
fi
if [[ ! -e "${INPUT}" ]]; then
    echo "ERROR: input not found: ${INPUT}"
    exit 1
fi

# ---------------------------------------------------------------------------
# 1. Managed assembly to transpile
# ---------------------------------------------------------------------------
INPUT_DIR="$(dirname "${INPUT}")"
INPUT_BASE="$(basename "${INPUT}" .csproj)"
INPUT_BASE="$(basename "${INPUT_BASE}" .exe)"
INPUT_BASE="$(basename "${INPUT_BASE}" .dll)"
if [[ -z "${LIBNAME}" ]]; then LIBNAME="${INPUT_BASE}"; fi

STAGING=""
TRANSPILE_INPUT=""
if [[ "${INPUT}" == *.csproj ]]; then
    STAGING="${INPUT_DIR}/out_app"
    mkdir -p "${STAGING}"
    dotnet build "${INPUT}" -c "${CONFIG}" -o "${STAGING}"
    if [[ -f "${STAGING}/${INPUT_BASE}.dll" ]]; then
        TRANSPILE_INPUT="${STAGING}/${INPUT_BASE}.dll"
    else
        TRANSPILE_INPUT="${STAGING}/${INPUT_BASE}.exe"
    fi
    if [[ ! -f "${TRANSPILE_INPUT}" ]]; then
        echo "ERROR: no managed assembly produced under ${STAGING}"
        exit 1
    fi
else
    TRANSPILE_INPUT="${INPUT}"
fi

# ---------------------------------------------------------------------------
# 2. Transpile
# ---------------------------------------------------------------------------
GEN="${INPUT_DIR}/gen_app"
if [[ "${CLEAN}" -eq 1 ]]; then rm -rf "${GEN}"; fi
mkdir -p "${GEN}"

IL2CPPC_DLL="${ROOT}/src/il2cppc/bin/${CONFIG}/net8.0/il2cppc.dll"
if [[ ! -f "${IL2CPPC_DLL}" ]]; then
    dotnet build "${ROOT}/src/il2cppc/il2cppc.csproj" -c "${CONFIG}"
fi
dotnet exec "${IL2CPPC_DLL}" "${TRANSPILE_INPUT}" "${GEN}"

# ---------------------------------------------------------------------------
# 3. Configure + build
# ---------------------------------------------------------------------------
BUILD="${GEN}/build_${TOOLCHAIN}"
if [[ "${CLEAN}" -eq 1 ]]; then rm -rf "${BUILD}"; fi
mkdir -p "${BUILD}"

case "${KIND}" in
    exe)    TARGET="il2cpp_gen_exe" ;;
    static) TARGET="il2cpp_gen" ;;
    shared) TARGET="il2cpp_gen_shared" ;;
    *) echo "ERROR: -Kind ${KIND} invalid (exe|static|shared)"; exit 1 ;;
esac

CMAKE_ARGS=( -S "${GEN}" -B "${BUILD}" "-DCMAKE_BUILD_TYPE=${CONFIG}" )
if [[ -n "${LIBNAME}" ]]; then CMAKE_ARGS+=( "-DIL2CPP_LIB_NAME=${LIBNAME}" ); fi

BUILT_IN_WSL=0
case "${TOOLCHAIN}" in
    mingw)
        # cross MinGW on a POSIX host: the toolchain file finds the compiler
        # from IL2CPP_MINGW_ROOT / MINGW_ROOT / PATH
        if [[ -n "${IL2CPP_MINGW_ROOT:-}" ]]; then
            export PATH="${IL2CPP_MINGW_ROOT}/bin:${PATH}"
        fi
        CMAKE_ARGS+=( -G "Unix Makefiles" "-DCMAKE_TOOLCHAIN_FILE=${ROOT}/cmake/mingw-w64.cmake" )
        ;;
    wsl|linux)
        # native Linux (if already inside WSL/linux, cmake runs directly;
        # a Windows caller reaches this script inside WSL anyway)
        CMAKE_ARGS+=( -G "Unix Makefiles" )
        if [[ -n "${IL2CPP_WSL_DISTRO:-}" ]]; then export DISTRO="${IL2CPP_WSL_DISTRO}"; fi
        ;;
    ndk)
        NDK="${IL2CPP_NDK_ROOT:-${NDK_ROOT:-}}"
        if [[ -z "${NDK}" ]]; then
            echo "ERROR: ndk toolchain needs IL2CPP_NDK_ROOT (or NDK_ROOT) env; no machine paths are hardcoded"
            exit 1
        fi
        CMAKE_ARGS+=( -G "Unix Makefiles" "-DCMAKE_TOOLCHAIN_FILE=${ROOT}/cmake/android-ndk.cmake" "-DCMAKE_ANDROID_NDK=${NDK}" )
        ;;
    *) echo "ERROR: -Toolchain ${TOOLCHAIN} invalid (mingw|wsl|ndk)"; exit 1 ;;
esac

cmake "${CMAKE_ARGS[@]}"
cmake --build "${BUILD}" --target "${TARGET}"

# ---------------------------------------------------------------------------
# 4. Side native libraries (P/Invoke SHARED modules) next to the artifact
# ---------------------------------------------------------------------------
deploy_native_libs() {
    local dest="$1"
    mkdir -p "${dest}"
    while IFS= read -r -d '' lib; do
        cp -f "${lib}" "${dest}/" 2>/dev/null || true
    done < <(find "${BUILD}" \( -name '*.so' -o -name '*.dll' \) -print0 2>/dev/null)
}

# ---------------------------------------------------------------------------
# 5. Artifacts -> OutDir
# ---------------------------------------------------------------------------
if [[ -z "${OUTDIR}" ]]; then OUTDIR="${GEN}/dist_${TOOLCHAIN}"; fi
mkdir -p "${OUTDIR}"

copy_headers() {
    local dest="$1"
    mkdir -p "${dest}"
    [[ -d "${GEN}/include" ]] && cp -r "${GEN}/include/." "${dest}/"
    [[ -d "${ROOT}/src/runtime/include" ]] && cp -r "${ROOT}/src/runtime/include/." "${dest}/"
}

find_artifact() {
    local needles=("$@")
    for pat in "${needles[@]}"; do
        local hit
        hit=$(find "${BUILD}" -name "${pat}" 2>/dev/null | head -n 1 || true)
        if [[ -n "${hit}" ]]; then printf '%s' "${hit}"; return 0; fi
    done
    return 1
}

if [[ "${KIND}" == "exe" ]]; then
    EXE=$(find_artifact "il2cpp_gen_exe")
    if [[ -z "${EXE}" ]]; then echo "ERROR: exe artifact not found under ${BUILD}"; exit 1; fi
    cp "${EXE}" "${OUTDIR}/il2cpp_gen_exe"
    deploy_native_libs "${OUTDIR}"
elif [[ "${KIND}" == "static" ]]; then
    LIBDIR="${OUTDIR}/lib"
    mkdir -p "${LIBDIR}"
    SLIB=$(find_artifact "lib${LIBNAME}.a" "${LIBNAME}.a" "libil2cpp_gen.a") ||
        { echo "ERROR: static artifact not found under ${BUILD}"; exit 1; }
    cp "${SLIB}" "${LIBDIR}/"
    RTLIB=$(find_artifact "libil2cpp_runtime.a") && cp "${RTLIB}" "${LIBDIR}/"
    copy_headers "${OUTDIR}/include"
else
    SHLIB=$(find_artifact "lib${LIBNAME}.so" "${LIBNAME}.so" "libil2cpp_gen.so" "lib${LIBNAME}.dll" "${LIBNAME}.dll") ||
        { echo "ERROR: shared artifact not found under ${BUILD}"; exit 1; }
    cp "${SHLIB}" "${OUTDIR}/"
    LIBDIR="${OUTDIR}/lib"
    mkdir -p "${LIBDIR}"
    RTLIB=$(find_artifact "libil2cpp_runtime.a") && cp "${RTLIB}" "${LIBDIR}/"
    copy_headers "${OUTDIR}/include"
    deploy_native_libs "${OUTDIR}"
fi

echo ""
echo "build-app OK: -Input ${INPUT} -Kind ${KIND} -Toolchain ${TOOLCHAIN}"
echo "  transpiled project : ${GEN}"
echo "  build dir          : ${BUILD}"
echo "  artifacts out      : ${OUTDIR}"
find "${OUTDIR}" -type f | while read -r f; do echo "    ${f}"; done