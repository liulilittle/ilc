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
    if(NOT MSVC)
        # POSIX dlopen/dlsym needs -ldl (WSL/GCC baseline)
        target_link_libraries(${TEST_NAME} PRIVATE ${CMAKE_DL_LIBS})
    endif()
    if(MSVC)
        # /utf-8: sources are UTF-8 without BOM (including non-ASCII comments); MSVC defaults to code page 936 and garbles them, swallowing later declarations
        target_compile_options(${TEST_NAME} PRIVATE /W4 /EHsc /bigobj /utf-8)
    else()
        # -Wall -Wextra: CI quality gate, zero warnings on both compilers (style spec §7), PUBLIC propagates to linked generated code
        target_compile_options(${TEST_NAME} PRIVATE -Wall -Wextra -fexceptions)
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
