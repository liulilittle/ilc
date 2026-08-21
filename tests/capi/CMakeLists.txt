cmake_minimum_required(VERSION 3.10)
project(capi_host CXX)

# The transpiled static library + runtime (built from the generated project).
# This CMake file is DRIVEN by the harness (run-capi.ps1), which passes the
# generated project's source root as CAPILLIB_DIR.
if(NOT DEFINED CAPILLIB_DIR)
    message(FATAL_ERROR "CAPILLIB_DIR must point at the generated il2cpp_gen project root")
endif()

add_executable(capi_host main.cpp)
target_include_directories(capi_host PRIVATE
    ${CAPILLIB_DIR}/include/internal
    ${CMAKE_CURRENT_SOURCE_DIR}/../../src/runtime/include/internal
    ${CMAKE_CURRENT_SOURCE_DIR}/../../src/runtime/include
)
# Link the generated static library and the runtime it depends on.
target_link_libraries(capi_host PRIVATE il2cpp_gen)
target_compile_features(capi_host PRIVATE cxx_std_17)
if(MSVC)
  target_compile_options(capi_host PRIVATE /W4 /EHsc /bigobj)
else()
  target_compile_options(capi_host PRIVATE -Wall -Wextra -fexceptions)
endif()

# The generated project defines il2cpp_gen; add it as a subdirectory so the
# host links the library target directly.
add_subdirectory(${CAPILLIB_DIR} ${CMAKE_BINARY_DIR}/gen)
