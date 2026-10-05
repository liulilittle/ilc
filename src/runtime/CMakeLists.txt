cmake_minimum_required(VERSION 3.10)
project(il2cpp_runtime CXX)
# monitor.cpp uses pthread_mutex_* directly on POSIX -- Linux/WSL linking needs -pthread.
# On Windows/MinGW the Threads module resolves to an empty interface target with no side effects.
find_package(Threads REQUIRED)
# M4 parallel extension: GLOB automatically picks up new src/*.cpp modules
# (each domain agent only adds its own source files, no need to edit this
# file; note that cmake configure must be re-run after adding files).
file(GLOB RUNTIME_SOURCES CONFIGURE_DEPENDS src/*.cpp)
add_library(il2cpp_runtime STATIC ${RUNTIME_SOURCES})
# P-F 双审定案 §1: the static runtime must build as position-independent code so a
# SHARED user-program library (il2cpp_gen_shared) can fold it without non-PIC
# relocations on Linux .so links. Harmless for static/exe consumers.
set_target_properties(il2cpp_runtime PROPERTIES POSITION_INDEPENDENT_CODE ON)
target_include_directories(il2cpp_runtime PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include/internal
)
target_compile_features(il2cpp_runtime PUBLIC cxx_std_17)
if(NOT MSVC)
    # POSIX dlopen/dlsym (pinvoke.cpp) -- PUBLIC propagates to the link target
    target_link_libraries(il2cpp_runtime PUBLIC ${CMAKE_DL_LIBS})
endif()
# pthread linking (monitor.cpp etc.) -- PUBLIC propagates to the link target
# (tests/generated code pick it up automatically)
target_link_libraries(il2cpp_runtime PUBLIC Threads::Threads)
if(WIN32)
    # socket.cpp uses Winsock2 (socket/connect/send/recv/WSAStartup) -- Windows
    # linking needs ws2_32 (recognized by both MinGW and MSVC); POSIX has no
    # such dependency (the socket API lives in libc)
    target_link_libraries(il2cpp_runtime PUBLIC ws2_32)
endif()
if(MSVC)
    # /utf-8: sources are UTF-8 without BOM; MSVC defaults to code page 936,
    # which garbles them and swallows later declarations
    target_compile_options(il2cpp_runtime PUBLIC /W4 /EHsc /bigobj /utf-8)
else()
    # -Wall -Wextra: CI quality gate, zero warnings on both compilers (style
    # spec §7); PUBLIC propagates to linked generated code
    target_compile_options(il2cpp_runtime PUBLIC -Wall -Wextra -fexceptions -fno-rtti)
endif()

# P-F §3: distributable static package -- install(TARGETS) + full runtime
# headers so an external consumer gets libil2cpp_runtime.a + include/ without
# re-adding the runtime as a subdirectory (build-app -Kind static ships this).
install(TARGETS il2cpp_runtime ARCHIVE DESTINATION lib)
install(DIRECTORY include/ DESTINATION include)


