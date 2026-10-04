cmake_minimum_required(VERSION 3.10)
project(extdll CXX)
add_library(extdll SHARED extdll.cpp)
# MinGW prefixes shared libraries with "lib" by default (libextdll.dll); the
# [DllImport("extdll")] loader searches "extdll.dll" -- pin the output name so
# the deployed .dll matches the module name on every generator.
set_target_properties(extdll PROPERTIES PREFIX "" OUTPUT_NAME extdll)
target_compile_features(extdll PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(extdll PRIVATE /W4 /EHsc /utf-8)
else()
  target_compile_options(extdll PRIVATE -Wall -Wextra)
endif()