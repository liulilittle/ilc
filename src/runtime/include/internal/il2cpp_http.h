#pragma once
//------------------------------------------------------------------------------
// il2cpp_http.h -- WebClient-facing object layer over the flat HTTP engine.
//
// The flat engine (http.h / src/runtime/src/http.cpp) exposes
// il2cpp_http_get(const char*) -> il2cpp_http_response -- a multi-field C
// struct plus a NUL-terminated malloc'd body. The managed WebClient surface
// (net-domain.md section 4.4) needs SINGLE-CALL wrappers the transpiler can
// emit: marshal -> get -> ok-check -> UTF-8 -> managed string -> free_body
// cannot be composed into one call expression at the IL boundary.
//
// Surface (net48 baseline, minimal T3 closure):
//   * System_Net_WebClient_class -- GLOBAL class descriptor ({ obj } only =
//     8 bytes, no fields; WebClient is stateless, net-domain.md section 2.3).
//     The transpiler emits &System_Net_WebClient_class for newobj/
//     isinst/castclass (the BclAllocExpr fallback), so generated code links
//     against this exact mangled global symbol.
//   * il2cpp_web_client_class()  -- lazy descriptor registration (idempotent;
//     the same pattern as il2cpp_version_class() in versionuri.cpp).
//   * icall_web_client_ctor(Il2CppObject* self) -- no-op ctor (stateless).
//   * icall_web_client_download_string(Il2CppObject* self, Il2CppString* url)
//     -- HTTP GET: status 200 -> body as a managed string; every failure
//     throws the net-domain.md section 2.5 exception types.
//   * icall_webclient_download_string(Il2CppString* url) -- stateless
//     convenience alias (task-card form), identical contract.
//
// Error mapping (net-domain.md sections 1.4-8 + 2.5; stable English engine
// messages are the discrimination key):
//   * null url                    -> System.ArgumentNullException
//   * empty / malformed URL       -> System.FormatException
//   * https:// (no TLS domain)    -> System.NotSupportedException (loud)
//   * chunked response            -> System.NotSupportedException (loud)
//   * scheme-less URL (no "://")
//     -- net48 GetUri file fallback, e.g. "not a url"
//                                  -> System.Net.WebException ("file not found";
//                                     webcase oracle line 4)
//   * non-200 status / connect /
//     send / recv / premature EOF -> System.Net.WebException
//       (chain: System.Net.WebException -> System.InvalidOperationException
//       -> System.SystemException -> System.Exception -- net48 inheritance)
//
// All errors are DotNetExceptions (il2cpp_throw_new) carrying stable
// type-name chains; the generated catch matcher (il2cpp_exception_matches)
// implements .NET inheritance semantics. DownloadString decodes the body as
// fixed UTF-8 (net-domain.md section 1.4-3); an embedded NUL truncates the
// managed string (documented minimal text surface -- binary bodies belong to
// DownloadData / il2cpp_http_get).
//
// Cross-platform: the icall is platform-neutral by construction -- the flat
// engine sits on the socket domain (socket.cpp), whose Windows/POSIX branches
// live entirely inside socket.cpp (winsock2 vs. BSD sockets; CMake links
// ws2_32 on Windows only). No platform branch exists in this file.
//
// Thread-safety: no shared mutable state; every call is self-contained over
// the per-call flat engine (the engine itself is per-call).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_string.h"
#include "il2cpp_exception.h"

#include <cstdint>

// System.Net.WebClient runtime descriptor (GLOBAL scope -- the transpiler
// emits &System_Net_WebClient_class for newobj/isinst/castclass; the same
// contract as System_Version_class / System_Uri_class in versionuri.cpp).
extern il2cpp::Il2CppClass System_Net_WebClient_class;

namespace il2cpp { namespace runtime {

// Lazy registration of the System.Net.WebClient descriptor (idempotent --
// il2cpp_class_register dedupes by pointer).
il2cpp::Il2CppClass* il2cpp_web_client_class();

// WebClient ctor: no-op (stateless -- net-domain.md section 2.3).
void icall_web_client_ctor(il2cpp::Il2CppObject* self);

// HTTP GET -> managed body string (see header contract). 'self' is ignored
// (the WebClient object carries no state).
il2cpp::Il2CppString* icall_web_client_download_string(
    il2cpp::Il2CppObject* self, il2cpp::Il2CppString* url);

// Stateless task-card alias of the download icall (same contract).
il2cpp::Il2CppString* icall_webclient_download_string(
    il2cpp::Il2CppString* url);

// WebClient.DownloadString(Uri) (net48): the Uri is marshaled to its string
// form (versionuri.h icall_uri_to_string -- net48 WebClient.GetUri(address)
// returns the Uri's string) and then the string download closure runs.
il2cpp::Il2CppString* icall_web_client_download_string_uri(
    il2cpp::Il2CppObject* self, il2cpp::Il2CppObject* uri);

}} // namespace il2cpp::runtime
