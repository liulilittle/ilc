cmake_minimum_required(VERSION 3.10)
project(il2cpp_gen CXX)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
add_subdirectory(../../../../src/runtime ${CMAKE_BINARY_DIR}/runtime)
# P-F: translated sources shared by the static + shared user-program libraries
set(IL2CPP_GEN_SOURCES
    src/Cases_ZP1.cpp
    src/entry.cpp
)
add_library(il2cpp_gen STATIC ${IL2CPP_GEN_SOURCES})
target_include_directories(il2cpp_gen PUBLIC include/internal)
target_link_libraries(il2cpp_gen PUBLIC il2cpp_runtime)
target_compile_features(il2cpp_gen PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(il2cpp_gen PRIVATE /W4 /EHsc /bigobj)
else()
  target_compile_options(il2cpp_gen PRIVATE -Wall -Wextra -fexceptions)
endif()
add_library(il2cpp_gen_shared SHARED ${IL2CPP_GEN_SOURCES})
target_include_directories(il2cpp_gen_shared PUBLIC include/internal)
target_link_libraries(il2cpp_gen_shared PUBLIC il2cpp_runtime)
target_compile_features(il2cpp_gen_shared PUBLIC cxx_std_17)
if(MSVC)
  target_compile_options(il2cpp_gen_shared PRIVATE /W4 /EHsc /bigobj)
else()
  target_compile_options(il2cpp_gen_shared PRIVATE -Wall -Wextra -fexceptions)
endif()
if(WIN32)
  # loadable C-API library: alongside entry.cpp's IL2CPP_EXPORT, auto-export so
  # MSVC/MinGW DLL consumers find the symbols without a manual .def file
  set_target_properties(il2cpp_gen_shared PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON)
endif()
if(IL2CPP_LIB_NAME)
  # -DIL2CPP_LIB_NAME=<assembly> renames the distributable libraries
  # (lib<name>.a / <name>.lib / <name>.dll / lib<name>.so)
  set_target_properties(il2cpp_gen PROPERTIES OUTPUT_NAME "${IL2CPP_LIB_NAME}")
  set_target_properties(il2cpp_gen_shared PROPERTIES OUTPUT_NAME "${IL2CPP_LIB_NAME}")
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
# P-F section 3: distributable package -- cmake --install <build> gives
# lib<name>.a + libil2cpp_runtime.a + include/ (consumers: -L -l<name> -lil2cpp_runtime)
install(TARGETS il2cpp_gen ARCHIVE DESTINATION lib)
install(TARGETS il2cpp_gen_shared ARCHIVE DESTINATION lib LIBRARY DESTINATION lib RUNTIME DESTINATION bin)
install(DIRECTORY include/ DESTINATION include)
