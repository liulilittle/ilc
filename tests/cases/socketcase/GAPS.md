# GAPS.md — socketcase unwired API surface

Case status: **net48 reference run GREEN (10/10 lines, expected.txt oracle) — transpile BLOCKED.**

## Gap 1: MethodMap.Net.cs is not dispatched (NEW — blocks IPAddress AND Socket)

Verified 2026-08-05:

```
[BASH:dotnet run --project src/il2cppc -- tests/cases/socketcase/out/socketcase.exe tests/cases/socketcase/gen]
il2cppc error: call at offset 0x0001: resolved System_Net_IPAddress_Parse_5679e48349e89d74 as
INSTANCE (pops this) but the stack is empty -- likely a wrong static/instance classification
   at Il2Cpp.Transpiler.Ir.StackFold.<Fold>g__EmitCall|0_8 ... StackFold.cs:line 1522
exit=2
```

Root cause: `TryMapNet` (MethodMap.Net.cs:69) has **zero call sites** — grep over
`src/il2cppc/Emit/*.cs` finds only the definition, never a dispatch
(`[READ:MethodRefResolver.cs:437-565]` BCL domain dispatch switch has no
`case "System.Net.IPAddress"`). `IPAddress.Parse` therefore falls through to the
generic metadata path, where the static method is misclassified as INSTANCE
(e2e-sprint-worklist #28/#29 family, but at StackFold.cs:1522, not :1369) and the
fold aborts.

Closure (out of socketcase scope — 只新建 socketcase/**): add
`case "System.Net.IPAddress": return TryMapNet(owner, name, ps, count, genArgs,
receiverType, receiverIsAddress, arg0Type, arg1Type);` to
MethodRefResolver.cs's BCL domain dispatch, then re-transpile this case. The
`I(..., inst:false, ...)` static classification in MethodMap.Net.cs:109/118 is
already correct.

Note: netcase.cs:9 claims "Transpiler wiring is PENDING" — this dispatch absence
is exactly that pending state; both netcase and socketcase stay transpile-blocked
until the dispatch line lands.

## Gap 2: Socket handle-gap (pre-existing, LOUD by design)

`System.Net.Sockets.Socket` is a hard handle-gap: MethodMap.Net.cs:128-159 throws
NotSupportedException at transpile time for `.ctor`/`Connect`/`Send`/`Receive`/
`Close` — socket.h is a flat opaque-handle API (`il2cpp_socket*`) with no class
descriptor, so `new Socket(...)` would emit
`il2cpp_object_new(&System_Net_Sockets_Socket_class)` (undefined symbol at link)
and the managed receiver cannot be cast to `il2cpp_socket*`. Closure is the
icall objectification of net-domain.md sections 2.3 + 4.2:
`il2cpp_netsocket.h icall_socket_ctor/connect_host/send/receive/close` +
`System_Net_Sockets_Socket_class` descriptor `{ obj; int32_t fd; }`.

The socket probes (create-close round-trip + connect-to-local-refused →
SocketException) are pinned in socketcase.cs behind `SOCKET_WIRED` — today's
net48 build passes no define, so the IL stays socket-free (CodeGenerator.cs:309
translates ALL static methods; a reachable `new Socket(...)` would kill the
whole case at transpile). When the icall layer lands:

1. build with `-p:DefineConstants=SOCKET_WIRED`
2. re-run net48 → expected.txt grows by 4 lines
3. re-transpile / re-build / re-diff (run-tests.ps1 -Case socketcase)
