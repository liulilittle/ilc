# ole32/CMakeLists.txt -- system-DLL shim for the winforms1 P/Invoke case
# (Phase 46: OLE drag-drop surface -- OleInitialize / RegisterDragDrop /
# RevokeDragDrop live in ole32). Same INTERFACE pattern as comctl32.
cmake_minimum_required(VERSION 3.10)
project(ole32 C)
add_library(ole32 INTERFACE)
if(MSVC)
  target_link_libraries(ole32 INTERFACE "ole32.lib")
else()
  target_link_libraries(ole32 INTERFACE "-lole32")
endif()
target_compile_features(ole32 INTERFACE cxx_std_17)
