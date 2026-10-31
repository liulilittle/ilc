cmake_minimum_required(VERSION 3.10)
project(suffixprobe CXX)
add_library(suffixprobe SHARED suffixprobe.cpp)
# MinGW prefixes shared libraries with "lib" by default (libsuffixprobe.dll); the
# [DllImport("suffixprobe.dll")] loader searches "suffixprobe.dll" -- pin the
# output name so the deployed .dll matches the module name on every generator.
set_target_properties(suffixprobe PROPERTIES PREFIX "" OUTPUT_NAME suffixprobe)
target_compile_features(suffixprobe PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(suffixprobe PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(suffixprobe PRIVATE -Wall -Wextra)
endif()
