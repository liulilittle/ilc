namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Net -- System.Net minimal domain (socket.h / ipaddr.h / http.h).
// Owners: System.Net.IPAddress / System.Net.Sockets.Socket / System.Net.WebClient
//
// RUNTIME SURFACE REALITY (verified 2026-08-05 against src/runtime):
//   * socket.h/socket.cpp -- flat OPAQUE-HANDLE API (il2cpp_socket*):
//     create / connect(host,port) / send / receive / get_*_endpoint / close.
//     NO object-typed icalls, NO class descriptor, NO handle table.
//   * ipaddr.h/ipaddr.cpp -- PARSE-LAYER only: il2cpp_ipaddress_parse
//     (Il2CppString*, uint8_t out4[4]) -> 1/0, il2cpp_ipaddress_to_string
//     (const uint8_t bytes4[4]) -> Il2CppString*, get_address_bytes, dns.
//     The planned icall layer (net-domain.md section 4.3, icall_ip_*) is NOT
//     implemented -- the parse layer NEVER throws (failure = return 0).
//   * http.h/http.cpp -- flat engine only: il2cpp_http_get(const char*)
//     -> il2cpp_http_response struct { ok, status_code, body_len, body,
//     error } + il2cpp_http_free_body. NO managed-string wrapper, NO
//     icall_web_* layer (net-domain.md section 4.4 is not implemented).
//
// WIRED SURFACE (this file):
//   System.Net.IPAddress.Parse(string) -> il2cpp_ipaddress_parse
//     VALUE-SLOT MODEL (the Guid precedent, MethodMap.Guid.cs): the 4
//     address bytes live in a uint8_t[4] slot -- the "ip4" result kind
//     (CodeGenerator ExprKind + slot declaration + write-through assignment,
//     the SAME machinery as "guid"; ipaddr.h has no struct wrapper). The
//     icall WRITES through the output buffer, so the assignment statement
//     form is the only valid emission: `ip = Parse(s)` becomes
//     il2cpp_ipaddress_parse((Il2CppString*)(s), loc0).
//     DIVERGENCE (documented): net48 Parse throws FormatException on invalid
//     input; the parse layer returns 0 and leaves the slot untouched
//     (reads back as 0.0.0.0). The throwing icall layer (net-domain.md
//     section 4.3 icall_ip_parse) is runtime work, out of this file's
//     domain -- the transpiler-only mapping cannot compose a conditional
//     throw into the write-through call form.
//   System.Net.IPAddress.ToString() -> il2cpp_ipaddress_to_string
//     instance; the receiver is the uint8_t[4] slot itself -- a byte-slot
//     param carrying the "g" cast code (rendered (uint8_t*)(...); the Guid
//     contract, MethodMapTests.Guid.cs:18-35).
//
// HANDLE-GAP DECISION (Socket) -- icall OBJECTIFICATION, per net-domain.md
// sections 2.3 + 4.2: socket.h's ACTUAL form is a flat opaque-handle API.
// A managed Socket needs (a) an OBJECT to hold the handle -- no class
// descriptor exists, so `new Socket(...)` would emit
// il2cpp_object_new(&System_Net_Sockets_Socket_class) and fail at link, and
// (b) OBJECT-TYPED entry points -- no cast can convert the managed receiver
// (Il2CppObject*) to il2cpp_socket* (opaque struct; the StreamIO handle-gap
// precedent, MethodMap.StreamIO.cs:32-53, documents the identical wall). A
// handle table would only move the gap (still no descriptor, no object
// field to key on). Closure = the runtime's netsocket icall layer:
//   il2cpp_netsocket.h icall_socket_ctor / connect_host / send / receive /
//   close (Il2CppObject* self carrying an int32_t fd slot) + the
//   System_Net_Sockets_Socket_class descriptor ({ obj; int32_t fd; }).
// All five requested members are therefore LOUD rejections below, each
// spelling out the exact runtime work that closes the gap.
//
// WebClient -- WIRED (web-b23 runtime closure): the runtime added the
// WebClient OBJECT layer (il2cpp_http.h) -- the System_Net_WebClient_class
// descriptor ({ obj } = 8 bytes, no fields) plus the single-call icalls:
//   .ctor()                      -> icall_web_client_ctor (no-op, instance)
//   DownloadString(string)       -> icall_web_client_download_string
//     (Il2CppObject* self, Il2CppString* url) -> Il2CppString* -- the whole
//     HTTP GET closure (connect -> request -> 200-check -> UTF-8 decode ->
//     managed string -> free_body), throwing WebException on failure.
// The emitter emits ONE call expression per mapping (the runtime closure
// makes the multi-step composition unnecessary). new WebClient() allocates
// through the BclAllocExpr fallback il2cpp_object_new(&System_Net_WebClient_class).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapNet(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type)
    {
        switch (owner)
        {
            //------------------------------------------------------------------
            // System.Net.IPAddress (ipaddr.h parse layer)
            //------------------------------------------------------------------
            case "System.Net.IPAddress":
                return TryMapIpAddress(name, ps, count);
            //------------------------------------------------------------------
            // System.Net.Sockets.Socket (socket.h -- handle gap, LOUD)
            //------------------------------------------------------------------
            case "System.Net.Sockets.Socket":
                return TryMapSocket(name, ps, count);
            //------------------------------------------------------------------
            // System.Net.WebClient (http.h engine -- wrapper gap, LOUD)
            //------------------------------------------------------------------
            case "System.Net.WebClient":
                return TryMapWebClient(name, ps, count);
            default:
                throw new NotSupportedException($"{owner} is not a System.Net owner wired by MethodMap.Net.cs (IPAddress / Socket / WebClient only)");
        }
    }

    private static ResolvedMethod? TryMapIpAddress(string name, byte[] ps, int count)
    {
        switch (name)
        {
            case "Parse":
                // static IPAddress Parse(string): the icall WRITES the four
                // address bytes through its output buffer -- the "ip4" value
                // slot (uint8_t[4] local). Same write-through contract as
                // Guid.Parse (MethodMap.Guid.cs:47-56): the result must be
                // assigned to a value slot, never consumed in expression
                // position (the CodeGenerator "ip4" guard rejects that).
                // Failure (0 return) leaves the slot untouched -- the net48
                // FormatException needs the runtime icall layer
                // (icall_ip_parse, net-domain.md section 4.3).
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_ipaddress_parse", 1, true, false, false, new[] { "s" }, "ip4",
                        returnTypeName: "System.Net.IPAddress");
                return null;
            case "ToString":
                // instance string ToString(): the receiver is the ip4 value
                // slot -- a byte-slot param, so it carries the "g" cast code
                // (rendered `(uint8_t*)(...)`; the Guid contract,
                // MethodMapTests.Guid.cs:18-35 -- "g" survives both the
                // plain-array value and the ldloca address shape).
                if (ps.Length == 0)
                    return I("il2cpp::runtime::il2cpp_ipaddress_to_string", 0, true, true, true, new[] { "g" });
                return null;
            default:
                throw new NotSupportedException(
                    "System.Net.IPAddress member not wired: the ipaddr.h parse layer covers " +
                    "Parse(string)/ToString() only (GetAddressBytes/get_AddressFamily/Equals/" +
                    "GetHashCode stay LOUD; the icall_ip_* object layer of net-domain.md section 4.3 is runtime work)");
        }
    }

    private static ResolvedMethod? TryMapSocket(string name, byte[] ps, int count)
    {
        // Socket object layer (il2cpp_netsocket.h/cpp): the managed Socket is
        // a runtime-registered class ({ obj; int32_t fd; }, fd at slot 1,
        // -1 = closed) backed by the GLOBAL System_Net_Sockets_Socket_class.
        switch (name)
        {
            case ".ctor":
                if (ps.Length == 3)
                    return I("il2cpp::runtime::icall_socket_ctor", 3, false, false, true,
                        new[] { "", "i", "i", "i" });
                return null;
            case "Connect":
                if (ps.Length == 2 && ps[0] == 0x0E && ps[1] == 0x08)
                    return I("il2cpp::runtime::icall_socket_connect_host", 2, false, false, true,
                        new[] { "", "s", "i" });
                return null;
            case "Send":
                if (ps.Length == 1 && ps[0] == 0x1D)
                    return I("il2cpp::runtime::icall_socket_send", 1, true, false, true,
                        new[] { "", "a" });
                if (ps.Length == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_socket_send_offset", 3, true, false, true,
                        new[] { "", "a", "i", "i" });
                return null;
            case "Receive":
                if (ps.Length == 1 && ps[0] == 0x1D)
                    return I("il2cpp::runtime::icall_socket_receive", 1, true, false, true,
                        new[] { "", "a" });
                if (ps.Length == 3 && ps[0] == 0x1D && ps[1] == 0x08 && ps[2] == 0x08)
                    return I("il2cpp::runtime::icall_socket_receive_offset", 3, true, false, true,
                        new[] { "", "a", "i", "i" });
                return null;
            case "Close":
                if (ps.Length == 0)
                    return I("il2cpp::runtime::icall_socket_close", 0, false, false, true);
                return null;
            default: return null;
        }
    }

    private static ResolvedMethod? TryMapWebClient(string name, byte[] ps, int count)
    {
        switch (name)
        {
            case ".ctor":
                // WebClient is a stateless object (System_Net_WebClient_class
                // descriptor = { obj }, http.cpp). The parameterless ctor is a
                // no-op that consumes the receiver (icall_web_client_ctor,
                // instance -- the emitter passes the new object as arg[0]).
                if (count == 0)
                    return I("il2cpp::runtime::icall_web_client_ctor", 0, false, false, true);
                return null;
            case "DownloadString":
                // instance string DownloadString(string url): the runtime
                // performs the whole HTTP GET closure (connect -> request ->
                // 200-check -> UTF-8 decode -> managed string -> free_body),
                // throwing WebException on non-200/network failure. The url
                // param is Il2CppString* (the "s" cast); the receiver is the
                // stateless object ("" passthrough).
                if (ps.Length == 1 && ps[0] == 0x0E)
                    return I("il2cpp::runtime::icall_web_client_download_string", 1, true, true, true,
                        new[] { "", "s" });
                return null;
            default: return null;
        }
    }
}
