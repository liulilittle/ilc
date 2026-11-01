cmake_minimum_required(VERSION 3.10)
project(byrefstat CXX)
# STATIC: linked INTO the generated exe -- the transpiler emits the extern "C"
# declaration from its own parameter matrix and calls it directly (the static
# call shape, as opposed to the SHARED/dynamic resolve shape of byrefprobe).
add_library(byrefstat STATIC byrefstat.cpp)
target_compile_features(byrefstat PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(byrefstat PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(byrefstat PRIVATE -Wall -Wextra)
endif()