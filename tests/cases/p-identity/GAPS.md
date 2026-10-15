# GAPS.md -- p-identity recorded findings (GN-20 legacy WindowsIdentity family)

Case status: **PASS, byte-exact** (mingw, net48 oracle 6-record byte-exact; oracle
sha == runtime sha `151b1f243dac683ed67aa1cdbb5f869252008f3c84e0807b04af01d64e34924f`,
110 bytes, diff=0). `expected.txt` is the exact net48 oracle.

Scope: the net48 public surface of `System.Security.Principal.WindowsIdentity`
reachable from user code on a plain Windows console process -- `GetCurrent()`,
`get_Token`, `get_IsAuthenticated`, `get_Name`. This was the audit's last
realGap (`audit-internalcalls mscorlib` realGap 1 -> 0 this batch) because the
managed GetCurrent() chain's inner step is the internal call
`Win32.OpenThreadToken` (docs/audit/icall-inventory-net48.md row 492).

## FIXED this batch (byte-exact public surface)

`src/runtime/src/identity.cpp` + `identity.h` implement the token-handle
"handle-gap" runtime (the Process=PID precedent, pathproc.h/methodmap.Process.cs):

- `GetCurrent()` -> `il2cpp_security_identity_get_current_token()`: the current
  thread token (`OpenThreadToken(GetCurrentThread(), MAXIMUM_ALLOWED, TRUE)`)
  with the net48 fallback to the process token (`OpenProcessToken(GetCurrentProcess())`)
  when the thread has none. Returns the token HANDLE in an **intptr_t value
  slot** -- a WindowsIdentity *value* is its token (no object model, no
  SafeHandle). 0 on failure.
- `get_Token` -> identity marker (the identity IS its token: `IntPtr.Token`
  folds to the receiver; the probe asserts shape via the mapped
  `IntPtr.ToInt64()`).
- `get_IsAuthenticated` -> `il2cpp_security_identity_is_authenticated(token)`:
  true unless the token's user SID is the anonymous (S-1-5-7) or guest
  (S-1-5-32-546 user or group membership) account -- the net48
  `!IsAnonymous && !IsGuest` rule for a null auth type.
- `get_Name` -> `il2cpp_security_identity_get_name(token)`: the token user SID
  -> `LookupAccountSidW` -> the managed "<domain>\<account>" string.
- `MethodMap.WindowsIdentity.cs` maps the public surface; the virtual
  base-owner twist (`System.Security.Claims.ClaimsIdentity` declares the
  callvirt for `get_IsAuthenticated`/`get_Name`) is forwarded to the map when
  the tracked receiver is a WindowsIdentity. `CodeGenerator` includes
  `identity.h`.

## W7 -- the audited ICALL itself + the un-mapped identity surface (honest)

1. **`System.Security.Principal.Win32.OpenThreadToken`** (the audit row): its
   net48 ABI is `int32 OpenThreadToken(TokenAccessLevels, WinSecurityContext,
   out SafeAccessTokenHandle&)`. The **out `SafeAccessTokenHandle` object cannot
   be constructed in the ILC model** (safehandle.h documents "no SafeHandle
   object model, cannot be constructed end-to-end"), and user code can never
   reach this internal method (the AOT substitutes the PUBLIC WindowsIdentity
   surface at the operand boundary). Closure = net48-ABI-shaped stub
   (`stubs.h`/`stubs.cpp`: `int32_t (int32_t, int32_t, Il2CppObject**)` -> 0,
   out slot untouched) + `MethodMap.Emit.cs` arm. The audit reports it **OK**
   (realGap 1 -> 0) but the direct-ICALL semantics are W7 (the public surface
   is the reachable, byte-exact channel). The old 0-arg `Il2CppObject*` stub
   (MethodMap.GC.cs dead case) was removed/moved.
2. **WindowsIdentity members outside the probe** (`Token`-consuming ctors,
   `IsGuest/IsSystem/IsAnonymous`, `AuthenticationType`, `User`, `Groups`,
   `Impersonate()`, `RunImpersonated`, claim-sourcing members): no runtime
   model; they fall to the generated-name path -> LOUD "not wired in M5"
   (probe never touches them; un-probed surface is a W7 boundary, not a
   fabricated exit).
3. **Non-Windows targets**: the token family is a Win32-only API; identity.cpp
   returns the documented 0 / null / false surface off-_WIN32.

## Empirical net48 facts pinned (oracle/expected.txt + metadata IL dump)

- `WindowsIdentity.GetCurrent()` succeeds on a plain console process: Token is
  a NONZERO handle, IsAuthenticated True, Name non-empty (length > 3).
- Managed chain (mscorlib IL decoded via metadata): GetCurrent() ->
  GetCurrentInternal -> GetCurrentToken -> GetCurrentThreadToken ->
  **Win32.OpenThreadToken** ICALL; thread failure falls back to
  GetCurrentProcessToken -> the P/Invoke `Win32Native.OpenProcessToken`.
- The raw handle value and the raw name string differ per process/user --
  asserted by shape only (the byte-exact contract is the shape-set, matching
  the p-appcontext record discipline).