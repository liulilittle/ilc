using System;
using System.Net;

namespace Cases { public static class SocketCase {
    // IPAddress layer probes -- wired today (ipaddr.h parse layer,
    // MethodMap.Net.cs:94-126). Same bool-only shape as netcase: the icall
    // layer never throws, so every probe is a caught-flag, never exception
    // message text (net-domain.md e2e assertion convention).
    static bool RoundTrips(string s) {
        try { return IPAddress.Parse(s).ToString() == s; }
        catch (FormatException) { return false; }
    }
    static bool IsFormatException(string s) {
        try { IPAddress.Parse(s); return false; }
        catch (FormatException) { return true; }
    }
    public static void Main() {
        // --- loopback-relevant dotted quads: Parse -> ToString round-trip ---
        Console.WriteLine(RoundTrips("127.0.0.1"));
        Console.WriteLine(RoundTrips("0.0.0.0"));
        Console.WriteLine(RoundTrips("127.0.0.2"));
        Console.WriteLine(RoundTrips("255.255.255.255"));
        // --- malformed: net48 throws FormatException (caught -> bool) ---
        Console.WriteLine(IsFormatException("127.0.0.256")); // octet > 255
        Console.WriteLine(IsFormatException("1.2.3"));       // three parts
        Console.WriteLine(IsFormatException("1.2.3.4.5"));   // five parts
        Console.WriteLine(IsFormatException(""));            // empty
        Console.WriteLine(IsFormatException("127.0.0.1 "));  // trailing space
        Console.WriteLine(IsFormatException("abc"));         // non-numeric
        // ===== SOCKET CONTRACT (handle-gap, LOUD) -- see GAPS.md =====
        // System.Net.Sockets.Socket is a hard handle-gap: MethodMap.Net.cs:128-159
        // throws NotSupportedException at TRANSPILE time for .ctor/Connect/Send/
        // Receive/Close (socket.h is a flat opaque-handle API with no class
        // descriptor; the icall objectification of net-domain.md sections 2.3 +
        // 4.2 -- icall_socket_ctor/connect_host/close + the
        // System_Net_Sockets_Socket_class descriptor { obj; int32_t fd; } -- is
        // runtime work). CodeGenerator.cs:309 translates ALL static methods, so
        // any `new Socket(...)` in this assembly kills the whole case. The
        // probes below are therefore pinned HERE behind SOCKET_WIRED: the net48
        // build passes no define today -> the IL is socket-free and this case
        // runs; when the icall layer lands, build with
        // `-p:DefineConstants=SOCKET_WIRED`, re-run net48 (expected.txt grows by
        // 4 lines), re-transpile, re-diff.
#if SOCKET_WIRED
        Console.WriteLine(TryCreateClose());      // create -> close round-trip
        Console.WriteLine(TryConnectRefused());   // connect to a port with no
                                                  // listener -> SocketException
#endif
    }
#if SOCKET_WIRED
    static bool TryCreateClose() {
        try {
            var s = new System.Net.Sockets.Socket(
                AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            s.Close();
            return true;
        } catch (Exception) { return false; }
    }
    static bool TryConnectRefused() {
        try {
            var s = new System.Net.Sockets.Socket(
                AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            s.Connect("127.0.0.1", 1); // local refused: no listener on port 1
            s.Close();
            return false;
        } catch (System.Net.Sockets.SocketException) { return true; }
    }
#endif
}}
