cmake_minimum_required(VERSION 3.10)
project(mylib CXX)
# P-D5 (merge regression fix): P-D3's fnptrret/fnptrparam unit-only fixtures
# DllImport("mylib") and their tests assert the STATIC direct-call shape
# (extern "C" full-width decls). P-D4's per-library routing walks up for a
# physical mylib dir; without one the fixtures were mis-routed as DYNAMIC
# (extern "C" suppressed) and the P-D3 assertions broke. This STATIC manifest
# restores the intended static-link semantics -- same convention every e2e
# case uses (mylib STATIC). Never built by build-fixtures.ps1 (unit-only).
add_library(mylib STATIC mylib.cpp)
target_compile_features(mylib PUBLIC cxx_std_17)
