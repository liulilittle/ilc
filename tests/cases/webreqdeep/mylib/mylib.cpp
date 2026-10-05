// mylib.cpp -- native library for the webreqdeep case: a loopback HTTP
// server + WebClient-download probes. Exported with C linkage.
//
// The server is implemented on Winsock with DYNAMIC loading (LoadLibrary /
// GetProcAddress), so this single source builds in ALL three configurations
// without link-time library flags:
//   * net48 oracle:  run-tests.ps1 `g++ -shared` (no -lws2_32) -> mylib.dll
//   * transpiled:    cmake static target (mylib/CMakeLists.txt links ws2_32)
//   * compilers:     MinGW g++ AND MSVC (plain Win32 API, no GNU extensions)
//
// Server contract (must satisfy BOTH clients):
//   * runtime client (http.cpp): HTTP/1.0 GET, parses Content-Length.
//   * net48 WebClient:            decodes the body with Encoding.Default
//     (GBK on this machine) IGNORING charset=utf-8 -- so the body must be
//     pure ASCII for the two decoders to agree byte-for-byte (the UTF-8 vs
//     GBK divergence is recorded in webreqdeep/GAPS.md).
// The response is fixed ASCII text with Connection: close; one connection
// per request (both clients reconnect for the second download).
#if defined(_WIN32)
    #ifndef _WIN32_WINNT
        #define _WIN32_WINNT 0x0600
    #endif
    #include <winsock2.h>
    #include <ws2tcpip.h>
    #include <windows.h>
#else
    #include <cstring>
#endif

#include <cstdint>
#include <cstdio>
#include <cstring>

extern "C" {

// ASCII body (28 bytes): net48 (GBK decode) and the runtime (UTF-8 decode)
// both produce the identical string for pure-ASCII bytes.
static const char kBody[] = "Hello, ILC! Download OK 2026";
static const size_t kBodyLen = sizeof(kBody) - 1;

#if defined(_WIN32)

// --- dynamic Winsock resolution (no link-time ws2_32 dependency) -----------
typedef int  (__stdcall *WSAStartup_t)(WORD, LPWSADATA);
typedef int  (__stdcall *WSACleanup_t)(void);
typedef SOCKET (__stdcall *Socket_t)(int, int, int);
typedef int  (__stdcall *Bind_t)(SOCKET, const struct sockaddr*, int);
typedef int  (__stdcall *Listen_t)(SOCKET, int);
typedef int  (__stdcall *GetSockName_t)(SOCKET, struct sockaddr*, int*);
typedef SOCKET (__stdcall *Accept_t)(SOCKET, struct sockaddr*, int*);
typedef int  (__stdcall *Recv_t)(SOCKET, char*, int, int);
typedef int  (__stdcall *Send_t)(SOCKET, const char*, int, int);
typedef int  (__stdcall *Closesocket_t)(SOCKET);
typedef u_short (__stdcall *Ntohs_t)(u_short);
typedef u_long (__stdcall *Htonl_t)(u_long);

static HMODULE g_ws2 = NULL;
static WSAStartup_t   f_WSAStartup;
static WSACleanup_t   f_WSACleanup;
static Socket_t       f_socket;
static Bind_t         f_bind;
static Listen_t       f_listen;
static GetSockName_t  f_getsockname;
static Accept_t       f_accept;
static Recv_t         f_recv;
static Send_t         f_send;
static Closesocket_t  f_closesocket;
static Ntohs_t        f_ntohs;
static Htonl_t        f_htonl;

static bool ResolveWinsock(void)
{
    if (g_ws2 != NULL)
    {
        return true;
    }
    HMODULE h = ::LoadLibraryA("ws2_32.dll");
    if (h == NULL)
    {
        return false;
    }
    g_ws2 = h;
    f_WSAStartup  = (WSAStartup_t)::GetProcAddress(h, "WSAStartup");
    f_WSACleanup  = (WSACleanup_t)::GetProcAddress(h, "WSACleanup");
    f_socket      = (Socket_t)::GetProcAddress(h, "socket");
    f_bind        = (Bind_t)::GetProcAddress(h, "bind");
    f_listen      = (Listen_t)::GetProcAddress(h, "listen");
    f_getsockname = (GetSockName_t)::GetProcAddress(h, "getsockname");
    f_accept      = (Accept_t)::GetProcAddress(h, "accept");
    f_recv        = (Recv_t)::GetProcAddress(h, "recv");
    f_send        = (Send_t)::GetProcAddress(h, "send");
    f_closesocket = (Closesocket_t)::GetProcAddress(h, "closesocket");
    f_ntohs       = (Ntohs_t)::GetProcAddress(h, "ntohs");
    f_htonl       = (Htonl_t)::GetProcAddress(h, "htonl");
    return f_WSAStartup != NULL && f_socket != NULL && f_bind != NULL
        && f_listen != NULL && f_accept != NULL && f_recv != NULL
        && f_send != NULL && f_closesocket != NULL;
}

static SOCKET g_listen = INVALID_SOCKET;
static HANDLE g_thread = NULL;
static volatile LONG g_served = 0;
static int32_t g_port = 0;

static DWORD WINAPI AcceptLoop(LPVOID)
{
    for (;;)
    {
        SOCKET c = f_accept(g_listen, NULL, NULL);
        if (c == INVALID_SOCKET)
        {
            break; // listener closed by stop_server
        }
        // Count on ACCEPT (before serving), so by the time a client has
        // finished downloading, the counter already reflects its connection
        // (no accept-thread vs client race on the assertion).
        InterlockedIncrement(&g_served);
        char buf[4096];
        size_t got = 0;
        while (got < sizeof(buf))
        {
            int n = f_recv(c, buf + got, (int)(sizeof(buf) - got), 0);
            if (n <= 0)
            {
                break;
            }
            got += (size_t)n;
            if (got >= 4 && std::memcmp(buf + got - 4, "\r\n\r\n", 4) == 0)
            {
                break;
            }
        }
        char head[256];
        int hl = std::snprintf(head, sizeof(head),
            "HTTP/1.0 200 OK\r\n"
            "Content-Type: text/plain; charset=utf-8\r\n"
            "Content-Length: %zu\r\n"
            "Connection: close\r\n"
            "\r\n", kBodyLen);
        f_send(c, head, hl, 0);
        f_send(c, kBody, (int)kBodyLen, 0);
        f_closesocket(c);
    }
    return 0;
}

// Binds 127.0.0.1:0 (ephemeral), starts the accept thread, returns the port.
int32_t start_server(void)
{
    if (!ResolveWinsock())
    {
        return 0;
    }
    WSADATA wsa;
    if (f_WSAStartup(MAKEWORD(2, 2), &wsa) != 0)
    {
        return 0;
    }
    SOCKET s = f_socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (s == INVALID_SOCKET)
    {
        return 0;
    }
    struct sockaddr_in addr;
    std::memset(&addr, 0, sizeof(addr));
    addr.sin_family = AF_INET;
    addr.sin_addr.s_addr = f_htonl(INADDR_LOOPBACK);
    addr.sin_port = 0;
    if (f_bind(s, (struct sockaddr*)&addr, sizeof(addr)) == SOCKET_ERROR)
    {
        f_closesocket(s);
        return 0;
    }
    if (f_listen(s, 8) == SOCKET_ERROR)
    {
        f_closesocket(s);
        return 0;
    }
    struct sockaddr_in got;
    int len = sizeof(got);
    std::memset(&got, 0, sizeof(got));
    if (f_getsockname(s, (struct sockaddr*)&got, &len) == SOCKET_ERROR)
    {
        f_closesocket(s);
        return 0;
    }
    g_port = (int32_t)f_ntohs(got.sin_port);
    g_listen = s;
    g_thread = ::CreateThread(NULL, 0, AcceptLoop, NULL, 0, NULL);
    return g_port;
}

void stop_server(void)
{
    if (g_listen != INVALID_SOCKET)
    {
        f_closesocket(g_listen);
        g_listen = INVALID_SOCKET;
    }
    if (g_thread != NULL)
    {
        ::WaitForSingleObject(g_thread, 5000);
        ::CloseHandle(g_thread);
        g_thread = NULL;
    }
    if (g_ws2 != NULL && f_WSACleanup != NULL)
    {
        f_WSACleanup();
    }
}

int32_t server_body_len(void)
{
    return (int32_t)kBodyLen;
}

int32_t server_served(void)
{
    return (int32_t)InterlockedCompareExchange(&g_served, 0, 0);
}

#else // POSIX stub -- the dual-compiler (mingw+msvc) surface is Windows-only.

int32_t start_server(void) { return 0; }
void stop_server(void) {}
int32_t server_body_len(void) { return (int32_t)kBodyLen; }
int32_t server_served(void) { return 0; }

#endif

} // extern "C"
