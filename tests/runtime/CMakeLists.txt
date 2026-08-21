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
# M4 并行扩展：GLOB 自动发现 test_*.cpp（各域代理只加自己的测试文件，无需
# 修改本文件；新增文件后需重新运行 cmake configure）。
#
# COM stage 17 batch 1：test_com 额外链入 com17type fixture 的提交生成物
# （tests/cases/com17type/gen_v1，WinForms-mirror IDropTarget 接口 + 双实现者
# WfDropSource/WfDropSource2）——test_com section 19 用 il2cpp_interface_dispatch
# 无头验证 r: 参数接口派发。不改 GLOB；只有 test_com 链生成代码（其他 test_*.exe
# 不受影响）。生成物重生成流程见 tests/cases/com17type/com17type.cs 头注释。
#------------------------------------------------------------------------------
cmake_minimum_required(VERSION 3.10)
project(il2cpp_runtime_tests CXX)

# monitor.cpp (il2cpp_runtime) 在 POSIX 下直接使用 pthread_mutex_*——
# Linux/WSL 链接需 -pthread。Windows/MinGW 下 Threads 模块自动解析为
# 空/系统线程，无副作用。
find_package(Threads REQUIRED)

add_subdirectory(../../src/runtime ${CMAKE_BINARY_DIR}/runtime)

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
        # POSIX dlopen/dlsym 需要 -ldl（WSL/GCC 基线）
        target_link_libraries(${TEST_NAME} PRIVATE ${CMAKE_DL_LIBS})
    endif()
    if(MSVC)
        # /utf-8: 源文件为 UTF-8 无 BOM（含中文注释），MSVC 默认按代码页 936 解析会乱码并吞掉后续声明
        target_compile_options(${TEST_NAME} PRIVATE /W4 /EHsc /bigobj /utf-8)
    else()
        # -Wall -Wextra: CI 质量门双编译器零警告（风格规范 §7），PUBLIC 传播到链接方生成代码
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
