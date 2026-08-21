#pragma once
//------------------------------------------------------------------------------
// http.h -- simplified HTTP client (GET/POST) over the socket domain.
//
// This is the M-stage flat C surface for net-domain task T3 (docs/design/
// net-domain.md section 2.4): URL parse + request build + three-stage
// response parse (status line / headers / body). The full object-based
// System.Net.WebClient/WebRequest surface (il2cpp_nethttp.h icall_web_*)
// is a later stage; this module is the engine underneath it.
//
// Semantic notes (net48 baseline, simplified where noted):
//   * scheme must be "http". "https" (and any other scheme) is rejected
//     with a stable error -- there is no TLS domain.
//   * Request line uses HTTP/1.0 (simplified; the fixture servers used by
//     the tests are 1.0-style). A "Host" header is always sent.
//   * Response body framing follows the design doc rules:
//       rule 1: Content-Length N            -> read exactly N bytes
//       rule 2: no Content-Length           -> read until the peer closes
//       rule 3: Transfer-Encoding: chunked  -> rejected (loud, stable
//               error); chunk decoding is not implemented.
//   * A status line other than "200" is reported as a failure: ok == false
//     with a stable "HTTP status <code>" error; status_code is still
//     filled so the caller can branch on it. (Full T3 later distinguishes
//     3xx passthrough / 4xx-5xx exceptions; this card only does the 200
//     check per the task card.)
//   * Bodies are delivered as a NUL-terminated malloc'd buffer; body_len
//     carries the exact byte count so binary payloads with embedded NULs
//     are preserved. Free with il2cpp_http_free_body.
//
// Result conventions (M-stage runtime: failures are return values, never
// C++ throws -- aligned with socket.h/streamio.h):
//   * ok == true         -> status_code == 200; body/body_len valid.
//   * ok == false        -> error points at a STABLE English message
//                           (never freed); body may still be filled (e.g.
//                           a non-200 response body) -- free it anyway if
//                           non-null.
//   * null/empty url     -> "null or empty URL"
//   * malformed url      -> "malformed URL"
//   * https url          -> "https is not supported" (no TLS domain)
//   * null POST body     -> "null request body"
//   * connect failure    -> "connection failed"
//   * send failure       -> "send failed"
//   * recv failure       -> "receive failed"
//   * premature EOF on a
//     Content-Length body -> "connection closed before body completed"
//   * chunked response   -> "chunked transfer encoding is not supported"
//   * non-200 status     -> "HTTP status <code>" (thread-local static
//                           buffer; valid until the next call on the same
//                           thread)
//   * bad status/header  -> "malformed HTTP response"
//   * malloc failure     -> "out of memory"
//------------------------------------------------------------------------------

#include <cstdint>

namespace il2cpp { namespace runtime {

struct il2cpp_http_response {
    bool ok;             // false = transport/protocol failure or status != 200
    int32_t status_code; // parsed HTTP status code (200 expected); always filled
    int32_t body_len;    // exact body byte count (0 for an empty body)
    char* body;          // malloc'd NUL-terminated body; nullptr when body_len == 0;
                         // free with il2cpp_http_free_body
    const char* error;   // stable English message when !ok; nullptr when ok
};

// HTTP GET. url form: http://host[:port]/path (path defaults to "/").
il2cpp_http_response il2cpp_http_get(const char* url);

// HTTP POST with a request body (null body is rejected; empty body is
// valid). Content-Type defaults to application/x-www-form-urlencoded.
il2cpp_http_response il2cpp_http_post(const char* url, const char* body);

// Release the body buffer returned in il2cpp_http_response. No-op on
// nullptr.
void il2cpp_http_free_body(char* body);

}} // namespace il2cpp::runtime
