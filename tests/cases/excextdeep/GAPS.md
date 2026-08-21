# GAPS.md — excextdeep unwired / diverging surface (e2e batch C63, 2026-08-12)

Case status: **PASS 24/24 byte-exact** (net48 oracle vs ILC transpiled run,
LF-join diff; mingw + msvc both PASS). The active surface drives the MESSAGE
CHAIN (a helper throws -> a wrapper reads + rethrows -> the outer catch reads
the SAME text), the type-name identity (GetType().Name on a catch variable),
the throw-factory messages (ArgumentNullException / ArgumentOutOfRangeException
/ ArgumentException), base-class catching and runtime-fault carriers
(DivideByZero / Overflow). No product code modified; no commit made.

## Recorded, NOT probed — the Exception OBJECT data surface is M3/Task-10 scope

The catch-variable surface here is the DotNetException CARRIER
(il2cpp_exception.h: typeName + message) exposed through get_Message
(__il2cpp_ex.message_text()) and GetType().Name (ExceptionEntryExpr). The
following Exception members have NO MethodMap.Exception.cs entry -> the
resolver falls to the generated-name path -> undefined C++ symbol -> cmake
build fails. Each is documented for the exception-object milestone:

- **InnerException** — get_InnerException unmapped; a message-chain probe
  `ex.InnerException` would break the build.
- **StackTrace / StackTrace property** — unmapped (no carrier has a stack).
- **Data (IDictionary)** — unmapped.
- **HelpLink / Source / TargetSite** — unmapped.
- **ToString() on the exception OBJECT** — falls through to the vtable path
  (loud downstream failure, MethodMap.Exception.cs ToString -> null).

## Culture-robust message comparison

The ArgumentNullException(paramName) factory composes the zh-CN host text
(值不能为 null。\n参数名: param, exceptionext.cpp). The case compares the caught
message with bool probes (ex.Message != null / ex.Message.Length > 0 /
GetType().Name) instead of printing the composed text raw, so the diff stays
byte-stable on any host culture (the wide-char codepage-fragility convention
of consoledeep/consolestream).
