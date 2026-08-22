cmake_minimum_required(VERSION 3.10)
project(il2cpp_gen CXX)
add_subdirectory(../../src/runtime ${CMAKE_BINARY_DIR}/runtime)
add_library(il2cpp_gen STATIC
    src/Cases_AsyncCase.cpp
    src/_PrivateImplementationDetails_.cpp
    src/Point.cpp
    src/_AF_d__14.cpp
    src/_B_d__1.cpp
    src/_Big_d__10.cpp
    src/_C_d__2.cpp
    src/_D_d__3.cpp
    src/_F_d__5.cpp
    src/_FF_d__13.cpp
    src/_H_d__7.cpp
    src/_L_d__9.cpp
    src/_OF_d__12.cpp
    src/_PF_d__15.cpp
    src/_S_d__4.cpp
    src/_T_d__8.cpp
    src/_UF_d__11.cpp
    src/__StaticArrayInitTypeSize_12.cpp
    src/entry.cpp
)
target_include_directories(il2cpp_gen PUBLIC include/internal)
target_link_libraries(il2cpp_gen PUBLIC il2cpp_runtime)
target_compile_features(il2cpp_gen PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(il2cpp_gen PRIVATE /W4 /EHsc /bigobj)
else()
  target_compile_options(il2cpp_gen PRIVATE -Wall -Wextra -fexceptions)
endif()
add_executable(il2cpp_gen_exe
    src/host.cpp
)
target_include_directories(il2cpp_gen_exe PRIVATE include/internal)
target_link_libraries(il2cpp_gen_exe PRIVATE il2cpp_gen)
target_compile_features(il2cpp_gen_exe PRIVATE cxx_std_17)
if(MSVC)
  target_compile_options(il2cpp_gen_exe PRIVATE /W4 /EHsc /bigobj)
else()
  target_compile_options(il2cpp_gen_exe PRIVATE -Wall -Wextra -fexceptions)
endif()
