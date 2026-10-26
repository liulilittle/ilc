using System;
using System.Net;
namespace Cases { public static class LoudRest1IpBytes {
    public static void Main() {
        // IPAddress.GetAddressBytes() -- the instance member returning the
        // address's raw 4 bytes (previously a LOUD rejection -- "the ipaddr.h
        // parse layer covers Parse(string)/ToString() only").
        var ip = IPAddress.Parse("192.168.1.1");
        byte[] b = ip.GetAddressBytes();
        Console.WriteLine(string.Join(",", b));
        Console.WriteLine(ip.ToString());
    }
}}
