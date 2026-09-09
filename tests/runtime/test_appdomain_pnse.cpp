//------------------------------------------------------------------------------
// test_appdomain_pnse.cpp -- AppDomain QCall PNSE stub tests (stubs.cpp).
//
// Coverage (4 PNSE stubs):
//   1. DisableFusionUpdatesFromADManager throws NotSupported
//   2. GetAppDomainManagerType throws NotSupported
//   3. SetAppDomainManagerType throws NotSupported
//   4. SetCanonicalConditionalAptcaList throws NotSupported
//
// Plain executable + minimal assertion harness (see test_gchandle.cpp).
//------------------------------------------------------------------------------

#include "../src/runtime/include/internal/stubs.h"
#include "il2cpp_exception.h"

#include <cstdio>

namespace {

int g_failures = 0;

void Expect(bool condition, const char* what)
{
    if (!condition)
    {
        std::printf("FAIL: %s\n", what);
        ++g_failures;
    }
}

template <typename Fn>
bool ThrowsNotSupported(Fn fn)
{
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        return il2cpp::il2cpp_exception_matches("System.NotSupportedException", e.type_name());
    }
    return false;
}

} // namespace

int main()
{
    // 1. DisableFusionUpdatesFromADManager
    Expect(ThrowsNotSupported([] {
            il2cpp::runtime::icall_appdomain_disable_fusion_updates_from_ad_manager(0);
        }),
           "DisableFusionUpdatesFromADManager throws NotSupported");

    // 2. GetAppDomainManagerType
    Expect(ThrowsNotSupported([] {
            il2cpp::runtime::icall_appdomain_get_app_domain_manager_type(0, nullptr, nullptr);
        }),
           "GetAppDomainManagerType throws NotSupported");

    // 3. SetAppDomainManagerType
    Expect(ThrowsNotSupported([] {
            il2cpp::runtime::icall_appdomain_set_app_domain_manager_type(0, nullptr, nullptr);
        }),
           "SetAppDomainManagerType throws NotSupported");

    // 4. SetCanonicalConditionalAptcaList
    Expect(ThrowsNotSupported([] {
            il2cpp::runtime::icall_appdomain_set_canonical_conditional_aptca_list(0, nullptr);
        }),
           "SetCanonicalConditionalAptcaList throws NotSupported");

    if (g_failures == 0)
    {
        std::printf("PASS: all 4 AppDomain PNSE stubs throw NotSupported\n");
        return 0;
    }
    std::printf("%d failure(s)\n", g_failures);
    return 1;
}
