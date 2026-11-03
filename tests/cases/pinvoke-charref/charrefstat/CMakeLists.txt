cmake_minimum_required(VERSION 3.10)
project(charrefstat CXX)
# STATIC: linked INTO the generated exe -- the transpiler emits the extern "C"
# declaration from its own parameter matrix and calls it directly (the static
# call shape, as opposed to the SHARED/dynamic resolve shape of charrefprobe).
add_library(charrefstat STATIC charrefstat.cpp)
target_compile_features(charrefstat PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(charrefstat PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(charrefstat PRIVATE -Wall -Wextra)
endif()
