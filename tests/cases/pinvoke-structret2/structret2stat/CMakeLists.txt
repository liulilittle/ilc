cmake_minimum_required(VERSION 3.10)
project(structret2stat CXX)
# STATIC: linked INTO the generated exe -- the transpiler emits the extern "C"
# declaration from the method signature and calls the symbol directly (the
# static-direct P/Invoke path, as opposed to the SHARED/dynamic resolve shape
# of tests/cases/pinvoke-structreturn/structretprobe).
add_library(structret2stat STATIC structret2stat.cpp)
target_compile_features(structret2stat PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(structret2stat PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(structret2stat PRIVATE -Wall -Wextra)
endif()
