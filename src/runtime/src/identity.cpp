//------------------------------------------------------------------------------
// identity.cpp -- System.Security.Principal.WindowsIdentity icalls (see
// identity.h for the contract). _WIN32 is the only implemented platform.
//
// net48 chain recap (measured, p-identity): GetCurrent() resolves the current
// thread token (Win32.OpenThreadToken) and falls back to the process token
// (OpenProcessToken) when the thread has none; the account Name comes from the
// token's user SID via LookupAccountSid; IsAuthenticated excludes the
// anonymous (S-1-5-7) and guest (S-1-5-32-546 membership) tokens.
//
// The AOT surface carries the token HANDLE in an intptr_t value slot (the
// handle-gap design: a WindowsIdentity *value* is its token, the Process=PID
// precedent in pathproc.h). The probe asserts shapes (non-zero / authenticated
// / non-empty name), which are byte-exact across the net48 oracle and this
// runtime on the same machine/user.
//------------------------------------------------------------------------------

#include "identity.h"

#include <cstdint>
#include <string>
#include <vector>

#if defined(_WIN32)
    #define WIN32_LEAN_AND_MEAN
    #include <windows.h>
#endif

namespace il2cpp { namespace runtime {

namespace {

#if defined(_WIN32)

// net48 GetCurrent() requests TokenAccessLevels.MaximumAllowed; TOKEN_MAXIMUM_
// ALLOWED is behind a _WIN32_WINNT gate in some SDKs, so pin the documented
// value (0x02000000, winnt.h TOKEN_ACCESS_MASK) directly.
constexpr DWORD kMaximumAllowed = 0x02000000u;

// The token's user SID, fetched via GetTokenInformation(TokenUser).
// Returns false on failure (or a degenerate zero-length result). The caller's
// vector is resized to the required buffer length; *sidOut receives the SID
// (PSID == void*; the Windows typedef is an opaque handle, never a typed SID).
bool get_token_user_sid(HANDLE token, std::vector<uint8_t>& buf, void** sidOut)
{
    DWORD len = 0;
    if (!GetTokenInformation(token, TokenUser, nullptr, 0, &len))
    {
        if (GetLastError() != ERROR_INSUFFICIENT_BUFFER)
            return false;
        buf.resize(len);
        if (!GetTokenInformation(token, TokenUser, buf.data(), len, &len))
            return false;
    }
    else
    {
        // A zero-required-length token user is degenerate -- treat as absent.
        return false;
    }
    const TOKEN_USER* tu = reinterpret_cast<const TOKEN_USER*>(buf.data());
    *sidOut = tu->User.Sid;
    return true;
}

// Whether the SID matches the well-known SID of the given type.
bool sid_matches_well_known(const void* sid, WELL_KNOWN_SID_TYPE type)
{
    if (sid == nullptr)
        return false;
    uint8_t known[SECURITY_MAX_SID_SIZE];
    DWORD len = SECURITY_MAX_SID_SIZE;
    if (!CreateWellKnownSid(type, nullptr, known, &len))
        return false;
    return EqualSid(const_cast<void*>(sid), reinterpret_cast<void*>(known)) != 0;
}

#endif // _WIN32

} // namespace

intptr_t il2cpp_security_identity_get_current_token()
{
#if defined(_WIN32)
    HANDLE token = nullptr;
    // net48 chain: the thread token first (impersonation token when present),
    // then the process token (the ordinary non-impersonating case).
    if (!OpenThreadToken(GetCurrentThread(), kMaximumAllowed, TRUE, &token))
    {
        if (!OpenProcessToken(GetCurrentProcess(), kMaximumAllowed, &token))
            return 0;
    }
    return reinterpret_cast<intptr_t>(token);
#else
    return 0;
#endif
}

int32_t il2cpp_security_identity_is_authenticated(intptr_t token)
{
    if (token == 0)
        return 0;
#if defined(_WIN32)
    std::vector<uint8_t> userBuf;
    void* userSid = nullptr;
    if (!get_token_user_sid(reinterpret_cast<HANDLE>(token), userBuf, &userSid))
    {
        // A token with no readable user SID cannot be authenticated.
        return 0;
    }
    // net48 IsAuthenticated (~ null auth type) = !IsAnonymous && !IsGuest.
    if (sid_matches_well_known(userSid, WinAnonymousSid))
        return 0;
    if (sid_matches_well_known(userSid, WinBuiltinGuestsSid))
        return 0;
    // Guest membership can also arrive through the token's GROUP list.
    DWORD glen = 0;
    std::vector<uint8_t> groups;
    if (!GetTokenInformation(reinterpret_cast<HANDLE>(token), TokenGroups, nullptr, 0, &glen)
        && GetLastError() == ERROR_INSUFFICIENT_BUFFER)
    {
        groups.resize(glen);
        if (GetTokenInformation(reinterpret_cast<HANDLE>(token), TokenGroups, groups.data(), glen, &glen))
        {
            const TOKEN_GROUPS* tg = reinterpret_cast<const TOKEN_GROUPS*>(groups.data());
            for (DWORD i = 0; i < tg->GroupCount; ++i)
            {
                if (sid_matches_well_known(tg->Groups[i].Sid, WinBuiltinGuestsSid))
                    return 0;
            }
        }
    }
    return 1;
#else
    return 0;
#endif
}

Il2CppString* il2cpp_security_identity_get_name(intptr_t token)
{
    if (token == 0)
        return nullptr;
#if defined(_WIN32)
    std::vector<uint8_t> userBuf;
    void* userSid = nullptr;
    if (!get_token_user_sid(reinterpret_cast<HANDLE>(token), userBuf, &userSid))
        return nullptr;
    // LookupAccountSidW: retry once with the size it reports (a too-long
    // name -- beyond the fixed buffers -- is a documented W7 edge).
    wchar_t account[256];
    wchar_t domain[512];
    DWORD accountLen = 256;
    DWORD domainLen = 512;
    SID_NAME_USE use = SidTypeUnknown;
    if (!LookupAccountSidW(nullptr, userSid, account, &accountLen, domain, &domainLen, &use))
    {
        if (GetLastError() != ERROR_INSUFFICIENT_BUFFER
            || accountLen > 256 || domainLen > 512)
            return nullptr;
        if (!LookupAccountSidW(nullptr, userSid, account, &accountLen, domain, &domainLen, &use))
            return nullptr;
    }
    std::wstring full;
    full.reserve(domainLen + accountLen + 1);
    full.append(domain);
    full.push_back(L'\\');
    full.append(account);
    // MinGW wchar_t is 16-bit like char16_t; reinterpret at the same width.
    return il2cpp_string_new(reinterpret_cast<const char16_t*>(full.c_str()),
        static_cast<int32_t>(full.size()));
#else
    return nullptr;
#endif
}

}} // namespace il2cpp::runtime