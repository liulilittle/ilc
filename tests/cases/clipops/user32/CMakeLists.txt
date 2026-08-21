# user32/CMakeLists.txt -- system-DLL shim for the clipops P/Invoke case.
# Same pattern as menuevt/mdievt: forwards to the system user32 import library
# (RegisterClassExW / CreateWindowExW / PostMessageW / PeekMessageW /
# TranslateMessage / DispatchMessageW / DefWindowProcW / OpenClipboard /
# EmptyClipboard / SetClipboardData / GetClipboardData / CloseClipboard /
# IsClipboardFormatAvailable / CountClipboardFormats / EnumClipboardFormats /
# GetClipboardFormatNameW / RegisterClipboardFormatW /
# GetClipboardSequenceNumber).
cmake_minimum_required(VERSION 3.10)
project(user32 C)
add_library(user32 INTERFACE)
target_link_libraries(user32 INTERFACE "user32")
target_compile_features(user32 INTERFACE cxx_std_17)
