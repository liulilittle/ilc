#include "generated.h"

int main() {
    try {
        return il2cpp_entry_main();
    } catch (const il2cpp::DotNetException& __il2cpp_unhandled) {
        return il2cpp::il2cpp_report_unhandled_exception(__il2cpp_unhandled.message_text());
    } catch (const std::exception& __il2cpp_cxx) {
        std::fprintf(stderr, "Unhandled native C++ exception: %s\n", __il2cpp_cxx.what());
        return 1;
    } catch (...) {
        std::fprintf(stderr, "Unhandled native exception (unknown type).\n");
        return 1;
    }
}
