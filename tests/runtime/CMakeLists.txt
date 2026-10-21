#------------------------------------------------------------------------------
# tests/runtime -- C++ unit tests for the runtime static library.
#
# Builds plain executables (no test framework dependency): every assertion is
# a runtime check that prints FAIL and exits non-zero on the first violated
# invariant, so the CI pipeline observes the result via the exit code.
#
# The runtime library is consumed through the same add_subdirectory path the
# generated code uses, exercising the exact PUBLIC include/compile contract
# that generated translation units link against.
#
# M4 parallel extension: GLOB automatically discovers test_*.cpp (each domain
# agent only adds its own test files, no need to edit this file; cmake configure
# must be re-run after adding files).
#
# COM stage 17 batch 1: test_com additionally links the committed com17type fixture
# output (tests/cases/com17type/gen_v1, WinForms-mirror IDropTarget interface + two
# implementers WfDropSource/WfDropSource2) -- test_com section 19 uses
# il2cpp_interface_dispatch to headlessly verify r: parameter interface dispatch.
# GLOB is unchanged; only test_com links generated code (other test_*.exe are
# unaffected). See the header comment of tests/cases/com17type/com17type.cs for the
# regeneration flow.
#------------------------------------------------------------------------------
cmake_minimum_required(VERSION 3.10)
project(il2cpp_runtime_tests CXX)

# CTest registration. Every test_*.cpp executable below is added to CTest, so
# `ctest` in this build directory runs the same suites the sweep scripts run by
# hand. NOTE: a test's own main() decides whether it has anything to check --
# e.g. test_timezone_posix is POSIX-only and returns 0 immediately on Windows,
# so on Windows it is a no-op, NOT coverage. On POSIX it exits 77 when its
# tzdata is unusable, which SKIP_RETURN_CODE turns into a ctest "Skipped".
enable_testing()

# monitor.cpp (il2cpp_runtime) uses pthread_mutex_* directly on POSIX --
# Linux/WSL linking needs -pthread. On Windows/MinGW the Threads module resolves
# to an empty/system thread target with no side effects.
find_package(Threads REQUIRED)

add_subdirectory(../../src/runtime ${CMAKE_BINARY_DIR}/runtime)

# P0 ReadLine probe: expose the streamio identity-seek counter to the test
# binaries (g_streamioIdentitySeeks / g_streamioForceSeekFailure). Test-only --
# the runtime CMakeLists used by generated projects does not define it, so
# production builds compile the counter away.
if(TARGET il2cpp_runtime)
    target_compile_definitions(il2cpp_runtime PRIVATE IL2CPP_STREAMIO_FSEEK_PROBE)
endif()

file(GLOB TEST_SOURCES CONFIGURE_DEPENDS test_*.cpp)
foreach(TEST_SRC ${TEST_SOURCES})
    get_filename_component(TEST_NAME ${TEST_SRC} NAME_WE)
    add_executable(${TEST_NAME} ${TEST_SRC})
    target_include_directories(${TEST_NAME} PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/../../src/runtime/include/internal
    )
    target_link_libraries(${TEST_NAME} PRIVATE il2cpp_runtime Threads::Threads)
    target_compile_features(${TEST_NAME} PRIVATE cxx_std_17)
    # The test TU needs the same probe macro as the runtime TU, otherwise the
    # section that asserts the seek count is compiled out of the test itself.
    target_compile_definitions(${TEST_NAME} PRIVATE IL2CPP_STREAMIO_FSEEK_PROBE)
    if(NOT MSVC)
        # POSIX dlopen/dlsym needs -ldl (WSL/GCC baseline)
        target_link_libraries(${TEST_NAME} PRIVATE ${CMAKE_DL_LIBS})
    endif()
    if(MSVC)
        # /utf-8: sources are UTF-8 without BOM (including non-ASCII comments); MSVC defaults to code page 936 and garbles them, swallowing later declarations
        target_compile_options(${TEST_NAME} PRIVATE /W4 /EHsc /bigobj /utf-8)
    else()
        # -Wall -Wextra: CI quality gate, zero warnings on both compilers (style spec §7); PUBLIC propagates to linked generated code
        target_compile_options(${TEST_NAME} PRIVATE -Wall -Wextra -fexceptions)
    endif()
    add_test(NAME ${TEST_NAME} COMMAND ${TEST_NAME})
    # Several suites print multi-KB failure dumps; keep ctest's log readable
    # and the timeout generous (test_com links a large generated fixture).
    set_tests_properties(${TEST_NAME} PROPERTIES TIMEOUT 300)
    # test_timezone_posix exits 77 when its tzdata is unusable (every zone
    # skipped). Declaring that code for THAT test only makes ctest report
    # "Skipped" instead of a pass, so a machine without zoneinfo cannot show a
    # false green. Scoped to the one test so no other suite's exit code is
    # reinterpreted.
    if(TEST_NAME STREQUAL "test_timezone_posix")
        set_tests_properties(${TEST_NAME} PROPERTIES SKIP_RETURN_CODE 77)
    endif()
endforeach()


# COM stage 17 batch 1/2/3: link the committed com17type transpiled output
# (gen_v3 -- batch 3 adds the IDataObject Data field + the 4-arg DragEventArgs
# ctor) into test_com ONLY (section 19 dispatches the WinForms-mirror
# IDropTarget headlessly; section 20 drives the IDataObject view; section 21
# drives il2cpp_object_construct + the typed-RCW direction). The generated
# entry.cpp provides il2cpp_runtime_init(); its il2cpp_entry_main is unused by
# the test (test_com has its own main).
if(TARGET test_com)
    set(COM17TYPE_GEN ${CMAKE_CURRENT_SOURCE_DIR}/../cases/com17type/gen_v10)
    target_sources(test_com PRIVATE
        ${COM17TYPE_GEN}/src/DragDropEffects.cpp
        ${COM17TYPE_GEN}/src/DragEventArgs.cpp
        ${COM17TYPE_GEN}/src/IAutoScale.cpp
        ${COM17TYPE_GEN}/src/IDataObject.cpp
        ${COM17TYPE_GEN}/src/IDropTarget.cpp
        ${COM17TYPE_GEN}/src/WfAutoScale.cpp
        ${COM17TYPE_GEN}/src/WfDataObject.cpp
        ${COM17TYPE_GEN}/src/WfDataObject2.cpp
        ${COM17TYPE_GEN}/src/WfDropSource.cpp
        ${COM17TYPE_GEN}/src/WfDropSource2.cpp
        ${COM17TYPE_GEN}/src/Cases_Com17type.cpp
        ${COM17TYPE_GEN}/src/entry.cpp
    )
    target_include_directories(test_com PRIVATE
        ${COM17TYPE_GEN}/include/internal
    )
endif()
