//------------------------------------------------------------------------------
// dtaudit4.cs -- DateTimeOffset audit probe (G6 audit D10/D11).
//
//   * min_plus14 = new DateTimeOffset(new DateTime(1,1,1), TimeSpan.FromHours(14))
//       net48 throws ArgumentOutOfRangeException (the UTC ticks would fall
//       below DateTime.MinValue). The runtime ctor applies the offset with NO
//       range check (dtoffset.cpp:49-55): expected to succeed and print the
//       local form "01/01/0001 00:00:00 +14:00".
//   * utcnow_offset = DateTimeOffset.UtcNow.Offset
//       net48 prints the machine offset (+08:00 on this host). The runtime has
//       no get_UtcNow mapping (MethodMap.Dtoffset.cs: no case -- default null),
//       so the generated-name fallback is expected to fail the C++ build.
//       The build.log tail is the D11 evidence.
//------------------------------------------------------------------------------
using System;

namespace Cases { public static class Dtaudit4 {
    public static void Main()
    {
        try
        {
            DateTimeOffset d = new DateTimeOffset(new DateTime(1, 1, 1), TimeSpan.FromHours(14));
            Console.WriteLine("min_plus14=ok:" + d.ToString());
        }
        catch (ArgumentOutOfRangeException) { Console.WriteLine("min_plus14=ArgumentOutOfRangeException"); }
        catch (ArgumentException) { Console.WriteLine("min_plus14=ArgumentException"); }
        catch (Exception) { Console.WriteLine("min_plus14=Exception"); }

        Console.WriteLine("utcnow_offset=" + DateTimeOffset.UtcNow.Offset);
    }
}}
