//------------------------------------------------------------------------------
// test_http.cpp -- runtime HTTP client domain unit tests (net T3 simplified).
//
// No external network is touched: every network case runs against a local
// fixture server on the 127.0.0.1 loopback, built from raw OS sockets in a
// background thread (the runtime socket domain deliberately exposes no
// listen/accept; socket.h document block). The kernel connect backlog
// guarantees the client's connect completes as soon as listen() runs, so
// the accept thread can never deadlock the test. The fixture always sends
// its response and closes, so the client can never hang on a missing EOF
// (recv returns 0 at close).
//
// Coverage:
//   1. URL/scheme errors (no network): null/empty URL, malformed URL,
//      https rejection, other scheme rejection.
//   2. POST null body -> "null request body".
//   3. Loopback probe + connection-refused: bind an ephemeral listener,
//      release it, then GET -- the connect MUST be refused and surface as
//      "connection failed". If the freed port was reclaimed the probe
//      reports SKIP (same rule as test_socket.cpp).
//   4. Fixture cases (gated on the probe):
//      a. GET with Content-Length -> status 200, exact fixed body.
//      b. GET with no Content-Length -> body read to EOF, exact.
//      c. GET with no path -> request line "GET / HTTP/1.0".
//      d. POST with body -> server echoes it back; request line + Host
//         + Content-Length verified on the server side.
//      e. POST with empty body -> valid, "echo:" back.
//      f. chunked response -> rejected with the stable chunked error.
//      g. 404 response -> !ok, status_code 404, stable "HTTP status 404".
//   5. WebClient object layer (net-domain.md section 4.4, no external net):
//      a. System_Net_WebClient_class descriptor + lazy registration +
//         no-op ctor.
//      b. download(null) -> ArgumentNullException.
//      c. download(malformed URL) -> FormatException.
//      d. download(https://) -> NotSupportedException (no TLS domain).
//      d2. webcase oracle shapes (no network): "http://" / "http://[::1" /
//          "://host" -> FormatException (shape "other"); scheme-less
//          "not a url" -> WebException (net48 GetUri file fallback).
//      e. loopback 200 -> managed body string (both the instance-form
//         icall_web_client_download_string and the stateless alias
//         icall_webclient_download_string; each gets its OWN single-shot
//         fixture).
//      f. loopback 404 -> WebException with the net48 base chain
//         (System.Net.WebException -> System.InvalidOperationException ->
//         System.Exception).
//      g. loopback chunked -> NotSupportedException (loud).
//
// Like the other runtime tests: plain executable, Expect() harness, exit
// non-zero on failure; SKIP lines exit 0 (loopback unavailable is not a
// failure).
//------------------------------------------------------------------------------

#include "http.h"
#include "socket.h"
#include "il2cpp_http.h"      // WebClient icall layer (section 4.4)
#include "il2cpp_exception.h" // DotNetException / il2cpp_exception_matches
#include "il2cpp_runtime.h"   // Il2CppClass / il2cpp_class_by_name

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <thread>

#if defined(_WIN32)
    #if !defined(_WIN32_WINNT)
        #define _WIN32_WINNT 0x0600
    #endif
    #define WIN32_LEAN_AND_MEAN
    #define NOMINMAX
    #include <winsock2.h>
    #include <ws2tcpip.h>
    #if defined(_MSC_VER) || (defined(__MINGW32__) && !defined(__clang__))
        #pragma comment(lib, "ws2_32.lib")
    #endif
#else
    #include <sys/socket.h>
    #include <netinet/in.h>
    #include <arpa/inet.h>
    #include <unistd.h>
#endif

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

//------------------------------------------------------------------------------
// Minimal raw-socket helpers (OS API). The runtime domain exposes no
// listen/accept, so the fixture server side is raw sockets -- exactly the
// pattern used by test_socket.cpp.
//------------------------------------------------------------------------------
#if defined(_WIN32)
using RawSock = SOCKET;
constexpr RawSock kRawInvalid = INVALID_SOCKET;
RawSock RawCreate() { return ::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP); }
int RawClose(RawSock s) { return ::closesocket(s); }
int RawSend(RawSock s, const char* bytes, int count)
{
    return ::send(s, bytes, count, 0);
}
int RawRecv(RawSock s, char* buf, int count)
{
    return ::recv(s, buf, count, 0);
}
#else
using RawSock = int;
constexpr RawSock kRawInvalid = -1;
RawSock RawCreate() { return ::socket(AF_INET, SOCK_STREAM, 0); }
int RawClose(RawSock s) { return ::close(s); }
int RawSend(RawSock s, const char* bytes, int count)
{
    return static_cast<int>(::send(s, bytes, count, 0));
}
int RawRecv(RawSock s, char* buf, int count)
{
    return static_cast<int>(::recv(s, buf, count, 0));
}
#endif

bool RawBindEphemeral(RawSock s, uint16_t* portOut)
{
    sockaddr_in addr;
    std::memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    addr.sin_port = 0;
#if defined(_WIN32)
    const int len = static_cast<int>(sizeof(addr));
#else
    const socklen_t len = sizeof(addr);
#endif
    if (::bind(s, reinterpret_cast<const sockaddr*>(&addr), len) != 0)
    {
        return false;
    }
    sockaddr_in got;
    std::memset(&got, 0, sizeof(got));
#if defined(_WIN32)
    int gotLen = static_cast<int>(sizeof(got));
#else
    socklen_t gotLen = sizeof(got);
#endif
    if (::getsockname(s, reinterpret_cast<sockaddr*>(&got), &gotLen) != 0)
    {
        return false;
    }
    *portOut = ntohs(got.sin_port);
    return true;
}

//------------------------------------------------------------------------------
// Local HTTP fixture server. Accepts one client, reads the full request
// (header block + POST body), then answers according to the mode and
// closes. The request head (and POST body, when present) is captured so
// the main thread can assert what the client actually sent -- the raw
// writes happen in this thread alone and are read only after join().
//------------------------------------------------------------------------------
enum class Mode {
    Fixed200, // Content-Length framed body
    EofBody,  // no Content-Length; body read to EOF (Connection: close)
    EchoPost, // 200 "echo:" + request body (Content-Length framed)
    Chunked,  // Transfer-Encoding: chunked (client must reject)
    NotFound  // HTTP/1.0 404
};

void ServeOne(RawSock listener, Mode mode, std::string* requestHeadOut)
{
    const RawSock peer = ::accept(listener, nullptr, nullptr);
    if (peer == kRawInvalid)
    {
        return;
    }
    // Test-side watchdog: a stuck fixture must not hang CI. (The runtime
    // client has no timeout API; the fixture always answers and closes,
    // so the client side cannot hang either.)
    const int timeoutMs = 30000;
    ::setsockopt(peer, SOL_SOCKET, SO_RCVTIMEO,
                 reinterpret_cast<const char*>(&timeoutMs),
                 sizeof(timeoutMs));

    // Read the header block (up to "\r\n\r\n").
    std::string acc;
    char buf[4096];
    size_t headEnd = std::string::npos;
    while (headEnd == std::string::npos)
    {
        const int n = RawRecv(peer, buf, static_cast<int>(sizeof(buf)));
        if (n <= 0)
        {
            break;
        }
        acc.append(buf, static_cast<size_t>(n));
        headEnd = acc.find("\r\n\r\n");
    }

    std::string head;
    std::string body;
    if (headEnd != std::string::npos)
    {
        head = acc.substr(0, headEnd);
        std::string leftover = acc.substr(headEnd + 4);
        // POST: read Content-Length more bytes.
        const size_t contentPos = head.find("Content-Length:");
        if (head.compare(0, 4, "POST") == 0
            && contentPos != std::string::npos)
        {
            const long long need
                = std::strtoll(head.c_str() + contentPos + 15, nullptr, 10);
            while (static_cast<long long>(leftover.size()) < need)
            {
                const int n = RawRecv(peer, buf, static_cast<int>(sizeof(buf)));
                if (n <= 0)
                {
                    break;
                }
                leftover.append(buf, static_cast<size_t>(n));
            }
            body = leftover.substr(
                0, static_cast<size_t>(need));
        }
    }
    if (requestHeadOut != nullptr)
    {
        *requestHeadOut = head;
    }

    const char* response = nullptr;
    std::string dynamic;
    switch (mode)
    {
    case Mode::Fixed200:
        // Content-Length declares only "Hello, HTTP!" (12 bytes); the
        // trailing "EXTRA" bytes must NOT be consumed by a correct client
        // (this is what makes the test sensitive to CL framing vs. the
        // read-to-EOF path -- a broken client would read all 18 bytes).
        response = "HTTP/1.0 200 OK\r\nContent-Type: text/plain\r\n"
                   "Content-Length: 12\r\n\r\nHello, HTTP!EXTRA";
        break;
    case Mode::EofBody:
        response = "HTTP/1.0 200 OK\r\nConnection: close\r\n\r\nEOF-body";
        break;
    case Mode::EchoPost:
    {
        dynamic = "HTTP/1.0 200 OK\r\nContent-Length: ";
        dynamic += std::to_string(5 + body.size());
        dynamic += "\r\n\r\necho:";
        dynamic += body;
        response = dynamic.c_str();
        break;
    }
    case Mode::Chunked:
        response = "HTTP/1.0 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n"
                   "5\r\nhello\r\n0\r\n\r\n";
        break;
    case Mode::NotFound:
        response = "HTTP/1.0 404 Not Found\r\nContent-Length: 4\r\n\r\nnope";
        break;
    }
    if (response != nullptr)
    {
        RawSend(peer, response, static_cast<int>(std::strlen(response)));
    }
    RawClose(peer);
}

struct Fixture {
    RawSock listener = kRawInvalid;
    uint16_t port = 0;
    std::thread thread;
    std::string requestHead;
    bool ok = false;
};

bool StartFixture(Mode mode, Fixture* f)
{
    f->listener = RawCreate();
    if (f->listener == kRawInvalid)
    {
        return false;
    }
    if (!RawBindEphemeral(f->listener, &f->port))
    {
        RawClose(f->listener);
        f->listener = kRawInvalid;
        return false;
    }
    if (::listen(f->listener, 1) != 0)
    {
        RawClose(f->listener);
        f->listener = kRawInvalid;
        return false;
    }
    f->thread = std::thread(ServeOne, f->listener, mode, &f->requestHead);
    f->ok = true;
    return true;
}

void StopFixture(Fixture* f)
{
    if (f->thread.joinable())
    {
        f->thread.join();
    }
    if (f->listener != kRawInvalid)
    {
        RawClose(f->listener);
        f->listener = kRawInvalid;
    }
    f->ok = false;
}

// Assert the common shape of a successful response, free its body.
void ExpectOkResponse(const il2cpp::runtime::il2cpp_http_response& resp,
                      int32_t status, const char* body, const char* what)
{
    Expect(resp.ok, what);
    Expect(resp.error == nullptr, what);
    Expect(resp.status_code == status, what);
    if (resp.ok && resp.error == nullptr)
    {
        Expect(resp.body != nullptr, what);
        Expect(resp.body_len == static_cast<int32_t>(std::strlen(body)), what);
        Expect(resp.body != nullptr
                   && std::strcmp(resp.body, body) == 0,
               what);
    }
}

//------------------------------------------------------------------------------
// WebClient icall-layer helpers (net-domain.md section 4.4):
//   * Cs / CsEquals -- create an ASCII managed string and compare a managed
//     string against an ASCII literal (the same layout-read pattern as
//     test_binaryio.cpp / test_arrayext2.cpp).
//   * CatchIt -- run a callable and snapshot a thrown DotNetException type
//     chain (the chain is read at catch time; il2cpp_exception_matches
//     applies .NET catch-conformance semantics on it).
//------------------------------------------------------------------------------
il2cpp::Il2CppString* Cs(const char* ascii)
{
    return reinterpret_cast<il2cpp::Il2CppString*>(
        il2cpp::il2cpp_string_new_from_cs(ascii));
}

bool CsEquals(const il2cpp::Il2CppString* s, const char* ascii)
{
    if (s == nullptr)
    {
        return false;
    }
    const size_t n = std::strlen(ascii);
    if (static_cast<size_t>(s->length) != n)
    {
        return false;
    }
    for (size_t i = 0; i < n; ++i)
    {
        if (s->chars[i] != static_cast<char16_t>(
                              static_cast<unsigned char>(ascii[i])))
        {
            return false;
        }
    }
    return true;
}

struct CaughtInfo
{
    bool caught;
    char type[256];
};

template <typename ThrowFn>
CaughtInfo CatchIt(ThrowFn fn)
{
    CaughtInfo info = { false, "" };
    try
    {
        fn();
    }
    catch (const il2cpp::DotNetException& e)
    {
        info.caught = true;
#if defined(_MSC_VER)
        ::strncpy_s(info.type, sizeof(info.type), e.type_name(), sizeof(info.type) - 1);
#else
        std::strncpy(info.type, e.type_name(), sizeof(info.type) - 1);
#endif
        info.type[sizeof(info.type) - 1] = '\0';
    }
    return info;
}

// The caught chain conforms to 'managedType' (exact or base-chain match).
bool IsType(const char* thrownChain, const char* managedType)
{
    return il2cpp::il2cpp_exception_matches(managedType, thrownChain);
}

} // namespace

int main()
{
    using namespace il2cpp::runtime;

    // --- 0. lifecycle (also triggers the one-time winsock startup so the
    // raw fixture sockets below work on Windows) ---
    il2cpp_socket* probeSock = il2cpp_socket_create();
    Expect(probeSock != nullptr, "runtime socket create");
    if (probeSock != nullptr)
    {
        il2cpp_socket_close(probeSock);
    }

    // --- 1. URL / scheme errors (no network) ---
    {
        il2cpp_http_response r = il2cpp_http_get(nullptr);
        Expect(!r.ok && r.error != nullptr
                   && std::strcmp(r.error, "null or empty URL") == 0,
               "null URL -> null or empty URL");
        r = il2cpp_http_get("");
        Expect(!r.ok && r.error != nullptr
                   && std::strcmp(r.error, "null or empty URL") == 0,
               "empty URL -> null or empty URL");
        r = il2cpp_http_get("http://");
        Expect(!r.ok && r.error != nullptr
                   && std::strcmp(r.error, "malformed URL") == 0,
               "host-less URL -> malformed URL");
        r = il2cpp_http_get("ftp://host/path");
        Expect(!r.ok && r.error != nullptr
                   && std::strcmp(r.error, "malformed URL") == 0,
               "unknown scheme -> malformed URL");
        r = il2cpp_http_get("http://host:99999/x");
        Expect(!r.ok && r.error != nullptr
                   && std::strcmp(r.error, "malformed URL") == 0,
               "out-of-range port -> malformed URL");
        r = il2cpp_http_get("http://host:abc/x");
        Expect(!r.ok && r.error != nullptr
                   && std::strcmp(r.error, "malformed URL") == 0,
               "non-numeric port -> malformed URL");
    }
    {
        const il2cpp_http_response r = il2cpp_http_get("https://host/");
        Expect(!r.ok && r.error != nullptr
                   && std::strcmp(r.error, "https is not supported") == 0,
               "https URL -> https is not supported");
    }
    {
        const il2cpp_http_response r
            = il2cpp_http_post("http://host/", nullptr);
        Expect(!r.ok && r.error != nullptr
                   && std::strcmp(r.error, "null request body") == 0,
               "null POST body -> null request body");
    }

    // --- 2. loopback probe + connection-refused ---
    bool loopbackAlive = false;
    {
        const RawSock dead = RawCreate();
        uint16_t deadPort = 0;
        if (dead != kRawInvalid && RawBindEphemeral(dead, &deadPort))
        {
            RawClose(dead); // release; a connect to it must now be refused
            char url[64];
            std::snprintf(url, sizeof(url), "http://127.0.0.1:%u/x",
                          static_cast<unsigned>(deadPort));
            const il2cpp_http_response r = il2cpp_http_get(url);
            Expect(!r.ok && r.error != nullptr
                       && std::strcmp(r.error, "connection failed") == 0,
                   "refused connect -> connection failed");
            il2cpp_http_free_body(r.body);
            loopbackAlive = true;
        }
        else
        {
            if (dead != kRawInvalid)
            {
                RawClose(dead);
            }
            std::printf("SKIP: loopback tests (cannot bind an ephemeral "
                        "listener)\n");
        }
    }

    // --- 3. fixture cases (gated on the loopback probe) ---
    if (loopbackAlive)
    {
        // a. GET with Content-Length framing.
        {
            Fixture f;
            if (StartFixture(Mode::Fixed200, &f))
            {
                char url[64];
                std::snprintf(url, sizeof(url), "http://127.0.0.1:%u/hello",
                              static_cast<unsigned>(f.port));
                const il2cpp_http_response r = il2cpp_http_get(url);
                ExpectOkResponse(r, 200, "Hello, HTTP!",
                                 "GET with Content-Length");
                il2cpp_http_free_body(r.body);
                StopFixture(&f);
                Expect(f.requestHead.find("GET /hello HTTP/1.0") == 0,
                       "GET request line");
                Expect(f.requestHead.find("Host: 127.0.0.1:") !=
                           std::string::npos,
                       "Host header present");
            }
            else
            {
                StopFixture(&f);
                std::printf("SKIP: Fixed200 fixture could not start\n");
            }
        }
        // b. GET with no Content-Length -> body read to EOF.
        {
            Fixture f;
            if (StartFixture(Mode::EofBody, &f))
            {
                char url[64];
                std::snprintf(url, sizeof(url),
                              "http://127.0.0.1:%u/hello",
                              static_cast<unsigned>(f.port));
                const il2cpp_http_response r = il2cpp_http_get(url);
                ExpectOkResponse(r, 200, "EOF-body",
                                 "GET without Content-Length reads to EOF");
                il2cpp_http_free_body(r.body);
                StopFixture(&f);
            }
            else
            {
                StopFixture(&f);
                std::printf("SKIP: EofBody fixture could not start\n");
            }
        }
        // c. GET with no path -> request line "GET / HTTP/1.0".
        {
            Fixture f;
            if (StartFixture(Mode::Fixed200, &f))
            {
                char url[64];
                std::snprintf(url, sizeof(url), "http://127.0.0.1:%u",
                              static_cast<unsigned>(f.port));
                const il2cpp_http_response r = il2cpp_http_get(url);
                ExpectOkResponse(r, 200, "Hello, HTTP!",
                                 "GET with default path");
                il2cpp_http_free_body(r.body);
                StopFixture(&f);
                Expect(f.requestHead.find("GET / HTTP/1.0") == 0,
                       "default path request line");
            }
            else
            {
                StopFixture(&f);
                std::printf("SKIP: default-path fixture could not start\n");
            }
        }
        // d. POST with body -> server echoes it back.
        {
            Fixture f;
            if (StartFixture(Mode::EchoPost, &f))
            {
                char url[64];
                std::snprintf(url, sizeof(url),
                              "http://127.0.0.1:%u/submit",
                              static_cast<unsigned>(f.port));
                const il2cpp_http_response r
                    = il2cpp_http_post(url, "hello");
                ExpectOkResponse(r, 200, "echo:hello",
                                 "POST body echoed back");
                il2cpp_http_free_body(r.body);
                StopFixture(&f);
                Expect(f.requestHead.find("POST /submit HTTP/1.0") == 0,
                       "POST request line");
                Expect(f.requestHead.find("Content-Length: 5") !=
                           std::string::npos,
                       "POST Content-Length correct");
            }
            else
            {
                StopFixture(&f);
                std::printf("SKIP: EchoPost fixture could not start\n");
            }
        }
        // e. POST with empty body.
        {
            Fixture f;
            if (StartFixture(Mode::EchoPost, &f))
            {
                char url[64];
                std::snprintf(url, sizeof(url),
                              "http://127.0.0.1:%u/submit",
                              static_cast<unsigned>(f.port));
                const il2cpp_http_response r
                    = il2cpp_http_post(url, "");
                ExpectOkResponse(r, 200, "echo:",
                                 "POST with empty body");
                il2cpp_http_free_body(r.body);
                StopFixture(&f);
            }
            else
            {
                StopFixture(&f);
                std::printf("SKIP: empty-body fixture could not start\n");
            }
        }
        // f. chunked response -> loud rejection.
        {
            Fixture f;
            if (StartFixture(Mode::Chunked, &f))
            {
                char url[64];
                std::snprintf(url, sizeof(url),
                              "http://127.0.0.1:%u/hello",
                              static_cast<unsigned>(f.port));
                const il2cpp_http_response r = il2cpp_http_get(url);
                Expect(!r.ok && r.error != nullptr
                           && std::strcmp(
                                  r.error,
                                  "chunked transfer encoding is not "
                                  "supported")
                                  == 0,
                       "chunked response rejected");
                il2cpp_http_free_body(r.body);
                StopFixture(&f);
            }
            else
            {
                StopFixture(&f);
                std::printf("SKIP: chunked fixture could not start\n");
            }
        }
        // g. 404 -> !ok with the parsed status code.
        {
            Fixture f;
            if (StartFixture(Mode::NotFound, &f))
            {
                char url[64];
                std::snprintf(url, sizeof(url),
                              "http://127.0.0.1:%u/missing",
                              static_cast<unsigned>(f.port));
                const il2cpp_http_response r = il2cpp_http_get(url);
                Expect(!r.ok, "404 is a failure");
                Expect(r.status_code == 404, "404 status code parsed");
                Expect(r.error != nullptr
                           && std::strcmp(r.error, "HTTP status 404") == 0,
                       "404 stable error message");
                Expect(r.body != nullptr
                           && std::strcmp(r.body, "nope") == 0,
                       "404 body still delivered");
                il2cpp_http_free_body(r.body);
                StopFixture(&f);
            }
            else
            {
                StopFixture(&f);
                std::printf("SKIP: 404 fixture could not start\n");
            }
        }
    }

    // --- 4. WebClient object layer (net-domain.md section 4.4) ---
    // 4a. descriptor + ctor (no network).
    {
        il2cpp::Il2CppClass* wc = il2cpp::runtime::il2cpp_web_client_class();
        Expect(wc != nullptr, "web client descriptor registered");
        if (wc != nullptr)
        {
            Expect(wc->name != nullptr
                       && std::strcmp(wc->name, "System.Net.WebClient") == 0,
                   "web client descriptor name");
            Expect(wc->instanceSize
                       == static_cast<int32_t>(sizeof(il2cpp::Il2CppObject)),
                   "web client instanceSize == header only (stateless)");
            Expect(il2cpp::il2cpp_class_by_name("System.Net.WebClient") != nullptr,
                   "web client registered in the type table");
        }
        Expect(std::strcmp(System_Net_WebClient_class.name,
                           "System.Net.WebClient") == 0,
               "System_Net_WebClient_class global name");
        il2cpp::runtime::icall_web_client_ctor(nullptr); // no-op, must not crash
    }
    // 4b. null url -> ArgumentNullException (no network).
    {
        const CaughtInfo info = CatchIt([] {
            il2cpp::runtime::icall_web_client_download_string(nullptr, nullptr);
        });
        Expect(info.caught, "download(null url) throws DotNetException");
        Expect(info.caught && IsType(info.type, "System.ArgumentNullException"),
               "download(null url) -> ArgumentNullException");
        Expect(info.caught && !IsType(info.type, "System.Net.WebException"),
               "download(null url) is not a WebException");
    }
    // 4c. malformed URL -> FormatException (no network).
    {
        const CaughtInfo info = CatchIt([] {
            il2cpp::runtime::icall_web_client_download_string(nullptr, Cs("http://"));
        });
        Expect(info.caught, "download(malformed URL) throws DotNetException");
        Expect(info.caught && IsType(info.type, "System.FormatException"),
               "download(malformed URL) -> FormatException (net-domain 1.4-8)");
        Expect(info.caught && !IsType(info.type, "System.Net.WebException"),
               "download(malformed URL) is not a WebException");
    }
    // 4d. https -> NotSupportedException (loud, no TLS domain).
    {
        const CaughtInfo info = CatchIt([] {
            il2cpp::runtime::icall_web_client_download_string(nullptr,
                                                              Cs("https://host/"));
        });
        Expect(info.caught, "download(https) throws DotNetException");
        Expect(info.caught && IsType(info.type, "System.NotSupportedException"),
               "download(https) -> NotSupportedException");
    }
    // 4d2. webcase oracle shapes (tests/cases/webcase/expected.txt, no network):
    //   "http://" / "http://[::1" / "://host" -> "other" (FormatException;
    //   net48 GetUri file-fallback would give NotSupportedException -- the
    //   same "other" shape), "not a url" -> "web_exception" (net48 file
    //   fallback on a SCHEME-LESS string, webcase oracle line 4).
    {
        const CaughtInfo httpEmpty = CatchIt([] {
            il2cpp::runtime::icall_web_client_download_string(nullptr, Cs("http://"));
        });
        Expect(httpEmpty.caught && IsType(httpEmpty.type, "System.FormatException"),
               "webcase 'http://' -> FormatException (shape 'other')");
        Expect(httpEmpty.caught && !IsType(httpEmpty.type, "System.Net.WebException"),
               "webcase 'http://' is not a WebException");

        const CaughtInfo badIpv6 = CatchIt([] {
            il2cpp::runtime::icall_web_client_download_string(nullptr,
                                                              Cs("http://[::1"));
        });
        Expect(badIpv6.caught && IsType(badIpv6.type, "System.FormatException"),
               "webcase 'http://[::1' -> FormatException (shape 'other')");
        Expect(badIpv6.caught && !IsType(badIpv6.type, "System.Net.WebException"),
               "webcase 'http://[::1' is not a WebException");

        const CaughtInfo schemeOnly = CatchIt([] {
            il2cpp::runtime::icall_web_client_download_string(nullptr, Cs("://host"));
        });
        Expect(schemeOnly.caught && IsType(schemeOnly.type, "System.FormatException"),
               "webcase '://host' -> FormatException (shape 'other')");
        Expect(schemeOnly.caught && !IsType(schemeOnly.type, "System.Net.WebException"),
               "webcase '://host' is not a WebException");

        const CaughtInfo noScheme = CatchIt([] {
            il2cpp::runtime::icall_web_client_download_string(nullptr, Cs("not a url"));
        });
        Expect(noScheme.caught, "webcase 'not a url' throws DotNetException");
        Expect(noScheme.caught && IsType(noScheme.type, "System.Net.WebException"),
               "webcase 'not a url' -> WebException (net48 scheme-less file fallback, oracle line 4)");
        Expect(noScheme.caught && !IsType(noScheme.type, "System.FormatException"),
               "webcase 'not a url' is not a FormatException");
    }
    // 4e-4g. loopback-gated download cases. Each entry point gets its OWN
    // fixture -- the fixture thread serves exactly ONE client (the same
    // single-shot contract as the section-3 fixtures), so a second request
    // to the same port would block forever in recv waiting for a response
    // nobody sends.
    if (loopbackAlive)
    {
        // 4e. GET 200 -> managed body string (instance-form entry point).
        {
            Fixture f;
            if (StartFixture(Mode::Fixed200, &f))
            {
                char url[64];
                std::snprintf(url, sizeof(url), "http://127.0.0.1:%u/hello",
                              static_cast<unsigned>(f.port));
                il2cpp::Il2CppString* body =
                    il2cpp::runtime::icall_web_client_download_string(nullptr,
                                                                      Cs(url));
                Expect(CsEquals(body, "Hello, HTTP!"),
                       "icall_web_client_download_string 200 -> body string");
                StopFixture(&f);
            }
            else
            {
                StopFixture(&f);
                std::printf("SKIP: download-string fixture could not start\n");
            }
        }
        // 4e2. GET 200 -> managed body string (stateless alias entry point).
        {
            Fixture f;
            if (StartFixture(Mode::Fixed200, &f))
            {
                char url[64];
                std::snprintf(url, sizeof(url), "http://127.0.0.1:%u/hello",
                              static_cast<unsigned>(f.port));
                il2cpp::Il2CppString* aliasBody =
                    il2cpp::runtime::icall_webclient_download_string(Cs(url));
                Expect(CsEquals(aliasBody, "Hello, HTTP!"),
                       "icall_webclient_download_string 200 -> body string");
                StopFixture(&f);
            }
            else
            {
                StopFixture(&f);
                std::printf("SKIP: download-string alias fixture could not "
                            "start\n");
            }
        }
        // 4f. 404 -> WebException shape (net48 base chain).
        {
            Fixture f;
            if (StartFixture(Mode::NotFound, &f))
            {
                char url[64];
                std::snprintf(url, sizeof(url), "http://127.0.0.1:%u/missing",
                              static_cast<unsigned>(f.port));
                const CaughtInfo info = CatchIt([&] {
                    il2cpp::runtime::icall_web_client_download_string(nullptr,
                                                                      Cs(url));
                });
                Expect(info.caught, "download(404) throws DotNetException");
                Expect(info.caught && IsType(info.type, "System.Net.WebException"),
                       "download(404) -> WebException");
                Expect(info.caught && IsType(info.type,
                                             "System.InvalidOperationException"),
                       "download(404) WebException matches InvalidOperationException (net48 base)");
                Expect(info.caught && IsType(info.type, "System.Exception"),
                       "download(404) WebException matches System.Exception");
                Expect(info.caught && !IsType(info.type, "System.FormatException"),
                       "download(404) is not a FormatException");
                StopFixture(&f);
            }
            else
            {
                StopFixture(&f);
                std::printf("SKIP: 404 download fixture could not start\n");
            }
        }
        // 4g. chunked response -> NotSupportedException (loud).
        {
            Fixture f;
            if (StartFixture(Mode::Chunked, &f))
            {
                char url[64];
                std::snprintf(url, sizeof(url), "http://127.0.0.1:%u/hello",
                              static_cast<unsigned>(f.port));
                const CaughtInfo info = CatchIt([&] {
                    il2cpp::runtime::icall_web_client_download_string(nullptr,
                                                                      Cs(url));
                });
                Expect(info.caught && IsType(info.type, "System.NotSupportedException"),
                       "download(chunked) -> NotSupportedException");
                StopFixture(&f);
            }
            else
            {
                StopFixture(&f);
                std::printf("SKIP: chunked download fixture could not start\n");
            }
        }
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d http assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime http tests passed\n");
    return 0;
}
