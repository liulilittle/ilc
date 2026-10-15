#pragma once
//------------------------------------------------------------------------------
// identity.h -- System.Security.Principal.WindowsIdentity icall contract
// (see identity.cpp for the implementation).
//
// net48 semantics measured on this Windows host (probe p-identity, oracle
// p-identity/expected.txt): WindowsIdentity.GetCurrent() succeeds on a plain
// console process -- Token is a nonzero handle, IsAuthenticated is true, and
// Name is the "<domain>\<account>" account name (LOOKUP of the token's user
// SID). The managed net48 chain (measured via metadata IL) is:
//
//   GetCurrent() -> GetCurrentInternal -> GetCurrentToken -> the audited
//   internal call Win32.OpenThreadToken(TokenAccessLevels, WinSecurityContext,
//   out SafeAccessTokenHandle&) with a fallback to the P/Invoke
//   Win32Native.OpenProcessToken(GetCurrentProcess(), ...) when the thread has
//   no token (ERROR_NO_TOKEN).
//
// The AOT runtime substitutes the PUBLIC surface at the operand boundary (the
// user probe calls WindowsIdentity.GetCurrent(), never the internal Win32
// icall). The "WindowsIdentity value" is the token HANDLE carried in an
// intptr_t value slot (the Process=PID handle-gap precedent, pathproc.h):
//
//   get_current_token       -> intptr_t token (thread token, else process
//                              token; 0 on failure)
//   is_authenticated        -> bool: true unless the token's user SID is the
//                              anonymous (S-1-5-7) or builtin-guest
//                              (S-1-5-32-546) account (net48 IsAuthenticated =
//                              !IsAnonymous && !IsGuest with a null auth type)
//   get_name                -> managed "<domain>\<account>" (LookupAccountSidW)
//
// _WIN32 is the ONLY platform with a real implementation; other targets return
// the documented null/zero/false surface (W7: the Windows token family is a
// Win32-only API).
//------------------------------------------------------------------------------

#include <cstdint>

#include "il2cpp_object.h"
#include "il2cpp_string.h"

namespace il2cpp { namespace runtime {

// WindowsIdentity.GetCurrent() -- the current thread/process token handle
// (intptr_t value slot, the identity's handle-gap carrier). 0 on failure.
intptr_t il2cpp_security_identity_get_current_token();

// WindowsIdentity.get_IsAuthenticated -- false for the anonymous/guest SIDs,
// true otherwise (and trivially true for any nonzero authenticated token).
int32_t il2cpp_security_identity_is_authenticated(intptr_t token);

// WindowsIdentity.get_Name -- the "<domain>\<account>" account name of the
// token's user SID (LookupAccountSidW), as a managed string; null on failure.
Il2CppString* il2cpp_security_identity_get_name(intptr_t token);

}} // namespace il2cpp::runtime