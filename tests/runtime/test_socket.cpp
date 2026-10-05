//------------------------------------------------------------------------------
// test_socket.cpp -- runtime socket domain unit tests.
//
// No external network is touched: everything stays on the 127.0.0.1
// loopback interface, and the loopback tests are gated behind an
// availability probe so a machine without a working loopback never turns
// this suite red (flakiness rule).
//
// Coverage:
//   1. create/close lifecycle: create returns a handle, close releases it,
//      close(nullptr) is a no-op.
//   2. Pure error paths (no network needed): null handle / null buffer /
//      null host / empty host / negative and zero counts / send and
//      receive on an unconnected socket / endpoint queries on an
//      unconnected socket / short or null endpoint buffers.
//   3. Loopback probe + connection-refused: bind an ephemeral listener on
//      127.0.0.1, learn its port, release it, then connect -- the connect
//      MUST be refused (ECONNREFUSED / WSAECONNREFUSED). If the freed port
//      was reclaimed by another process (the connect unexpectedly
//      succeeds) the probe reports SKIP instead of FAIL; if the raw
//      listener cannot be created/bound the loopback tests are skipped.
//      A refused connect proves the loopback TCP path is alive, which is
//      the precondition for the round-trip below.
//   4. Loopback round-trip (only after the probe proved loopback alive):
//      a raw OS server socket listens on an ephemeral port and answers
//      "ping" with "pong" on a background thread, while the client uses
//      the runtime API end to end: create -> connect("127.0.0.1") ->
//      send 4 bytes -> receive "pong" -> receive 0 (EOF after the server
//      close) -> get_remote_endpoint matches 127.0.0.1:<server port> ->
//      get_local_endpoint is on 127.0.0.1 -> close. The server side uses
//      raw OS sockets because the simplified runtime domain deliberately
//      exposes no listen/accept; the kernel connect backlog guarantees
//      the client's connect completes as soon as listen() runs, so the
//      accept thread can never deadlock the test.
//
// Like the other runtime tests: a plain executable with a minimal
// assertion harness -- any failed Expect() prints FAIL and bumps the
// counter; the process exits non-zero on any failure so CI observes a red
// signal. SKIP lines exit 0 (loopback unavailable is not a failure).
//------------------------------------------------------------------------------

#include "socket.h"

#include <cstdint>
#include <cstdio>
#include <cstring>
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
// Minimal raw-socket server helpers (OS API). The runtime domain exposes no
// listen/accept, so the server side of the round-trip is raw sockets.
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

// Bind a listener to 127.0.0.1 on an OS-assigned ephemeral port and report
// the port. Port 0 lets the OS pick a free port, so consecutive test runs
// never collide (deterministic on busy machines).
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

// Server side of the round-trip: accept one client, read exactly 4 bytes,
// answer "pong", then close the peer (its FIN surfaces as receive() == 0
// on the client side).
void EchoPong(RawSock listener)
{
    const RawSock peer = ::accept(listener, nullptr, nullptr);
    if (peer == kRawInvalid)
    {
        return;
    }
    char bytes[4];
    int n = 0;
    while (n < 4)
    {
        const int got = RawRecv(peer, bytes + n, 4 - n);
        if (got <= 0)
        {
            break;
        }
        n += got;
    }
    if (n == 4)
    {
        RawSend(peer, "pong", 4);
    }
    RawClose(peer);
}

} // namespace

int main()
{
    using namespace il2cpp::runtime;

    // --- 1. create/close lifecycle ---
    il2cpp_socket* sock = il2cpp_socket_create();
    Expect(sock != nullptr, "create returns a handle");
    if (sock != nullptr)
    {
        il2cpp_socket_close(sock);
    }
    il2cpp_socket_close(nullptr); // must be a no-op

    // --- 2. error paths (no network; valid even when create failed) ---
    il2cpp_socket* e = il2cpp_socket_create();
    uint8_t buf[16] = { 0 };
    char ip[16];
    uint16_t port = 0;
    Expect(il2cpp_socket_send(nullptr, buf, 4) == -1,
           "send on a null handle fails");
    Expect(il2cpp_socket_receive(nullptr, buf, 4) == -1,
           "receive on a null handle fails");
    Expect(il2cpp_socket_send(e, nullptr, 4) == -1,
           "send with a null buffer fails");
    Expect(il2cpp_socket_receive(e, nullptr, 4) == -1,
           "receive with a null buffer fails");
    if (e != nullptr) // zero-count needs a live handle (null returns -1)
    {
        Expect(il2cpp_socket_send(e, buf, 0) == 0,
               "zero-length send returns 0");
        Expect(il2cpp_socket_receive(e, buf, 0) == 0,
               "zero-length receive returns 0");
    }
    Expect(il2cpp_socket_send(e, buf, -1) == -1,
           "negative send count fails");
    Expect(il2cpp_socket_receive(e, buf, -1) == -1,
           "negative receive count fails");
    Expect(il2cpp_socket_send(e, buf, 4) == -1,
           "send on an unconnected socket fails");
    Expect(il2cpp_socket_receive(e, buf, 4) == -1,
           "receive on an unconnected socket fails");
    Expect(!il2cpp_socket_connect(nullptr, "127.0.0.1", 80),
           "connect on a null handle fails");
    Expect(!il2cpp_socket_connect(e, nullptr, 80),
           "connect with a null host fails");
    Expect(!il2cpp_socket_connect(e, "", 80),
           "connect with an empty host fails");
    Expect(!il2cpp_socket_get_remote_endpoint(e, ip, sizeof(ip), &port),
           "remote endpoint on an unconnected socket fails");
    // Platform semantics: getsockname on an unconnected socket FAILS on
    // Windows (the assertion below) but SUCCEEDS on POSIX, returning the
    // unbound INET address 0.0.0.0:0 -- .NET's Socket.LocalEndPoint mirrors
    // the OS in both cases. Accept the platform's behavior.
#ifdef _WIN32
    Expect(!il2cpp_socket_get_local_endpoint(e, ip, sizeof(ip), &port),
           "local endpoint on an unconnected socket fails");
#else
    {
        char posixIp[16];
        uint16_t posixPort = 0;
        const bool posixOk = il2cpp_socket_get_local_endpoint(
            e, posixIp, sizeof(posixIp), &posixPort);
        Expect(!posixOk
                   || (std::strcmp(posixIp, "0.0.0.0") == 0 && posixPort == 0),
               "POSIX unconnected local endpoint is 0.0.0.0:0 (or fails on kernels that refuse it)");
    }
#endif
    Expect(!il2cpp_socket_get_remote_endpoint(e, ip, 15, &port),
           "endpoint with a short buffer fails");
    Expect(!il2cpp_socket_get_remote_endpoint(e, nullptr, 16, &port),
           "endpoint with a null buffer fails");
    Expect(!il2cpp_socket_get_remote_endpoint(e, ip, 16, nullptr),
           "endpoint with a null port output fails");
    if (e != nullptr)
    {
        il2cpp_socket_close(e);
    }

    // --- 3. loopback probe + connection-refused ---
    il2cpp_socket* probe = il2cpp_socket_create();
    if (probe == nullptr)
    {
        std::printf("SKIP: loopback tests (socket create failed)\n");
    }
    bool loopbackAlive = false;
    if (probe != nullptr)
    {
        const RawSock dead = RawCreate();
        uint16_t deadPort = 0;
        if (dead != kRawInvalid && RawBindEphemeral(dead, &deadPort))
        {
            RawClose(dead); // release the port; a connect to it must now fail
            loopbackAlive = !il2cpp_socket_connect(probe, "127.0.0.1",
                                                   deadPort);
            if (!loopbackAlive)
            {
                std::printf("SKIP: loopback refused-check "
                            "(ephemeral port reclaimed)\n");
            }
        }
        else
        {
            if (dead != kRawInvalid)
            {
                RawClose(dead);
            }
            std::printf("SKIP: loopback tests "
                        "(cannot bind an ephemeral listener)\n");
        }
        il2cpp_socket_close(probe);
    }

    if (loopbackAlive)
    {
        il2cpp_socket* probe2 = il2cpp_socket_create();
        Expect(probe2 != nullptr, "second probe socket created");
        if (probe2 != nullptr)
        {
            Expect(!il2cpp_socket_connect(probe2, "localhost", 9),
                   "connect to a dead port is refused (host-name form)");
            il2cpp_socket_close(probe2);
        }
    }

    // --- 4. loopback round-trip (probe proved 127.0.0.1 reachable) ---
    if (loopbackAlive)
    {
        const RawSock listener = RawCreate();
        uint16_t serverPort = 0;
        Expect(listener != kRawInvalid, "raw listener socket created");
        Expect(RawBindEphemeral(listener, &serverPort),
               "raw listener bound to an ephemeral port");
        if (listener != kRawInvalid && serverPort != 0)
        {
            Expect(::listen(listener, 1) == 0, "raw listener listening");
            std::thread server(EchoPong, listener);
            il2cpp_socket* client = il2cpp_socket_create();
            Expect(client != nullptr, "client socket created");
            if (client != nullptr)
            {
                Expect(il2cpp_socket_connect(client, "127.0.0.1", serverPort),
                       "client connects to the loopback listener");
                char remoteIp[16];
                uint16_t remotePort = 0;
                Expect(il2cpp_socket_get_remote_endpoint(
                           client, remoteIp, sizeof(remoteIp), &remotePort)
                       && std::strcmp(remoteIp, "127.0.0.1") == 0
                       && remotePort == serverPort,
                       "remote endpoint is 127.0.0.1:<server port>");
                char localIp[16];
                uint16_t localPort = 0;
                Expect(il2cpp_socket_get_local_endpoint(
                           client, localIp, sizeof(localIp), &localPort)
                       && std::strcmp(localIp, "127.0.0.1") == 0
                       && localPort != 0,
                       "local endpoint is 127.0.0.1:<ephemeral port>");
                Expect(il2cpp_socket_send(client, "ping", 4) == 4,
                       "client sends 4 bytes");
                uint8_t pong[4] = { 0 };
                int n = 0;
                while (n < 4)
                {
                    const int got = il2cpp_socket_receive(client,
                                                          pong + n, 4 - n);
                    if (got <= 0)
                    {
                        break;
                    }
                    n += got;
                }
                Expect(n == 4 && std::memcmp(pong, "pong", 4) == 0,
                       "client receives 'pong'");
                Expect(il2cpp_socket_receive(client, pong, 4) == 0,
                       "client observes EOF after the server close");
                il2cpp_socket_close(client);
            }
            server.join();
            RawClose(listener);
        }
        else
        {
            if (listener != kRawInvalid)
            {
                RawClose(listener);
            }
        }
    }

    // --- Report ---
    if (g_failures != 0)
    {
        std::printf("%d socket assertion(s) failed\n", g_failures);
        return 1;
    }
    std::printf("all runtime socket tests passed\n");
    return 0;
}
