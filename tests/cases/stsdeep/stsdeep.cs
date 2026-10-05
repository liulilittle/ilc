using System;
using System.Text;
class P
{
    static void Main()
    {
        string s = "a,b,,c";
        Console.WriteLine("sp0=" + (s.Split(',')[0] == "a"));
        Console.WriteLine("sp=" + (string.Join("|", s.Split(',')) == "a|b||c"));
        Console.WriteLine("spo=" + (string.Join("|", s.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) == "a|b|c"));
        Console.WriteLine("ws=" + (string.Join("|", "x  y".Split(new char[] { ' ' })) == "x||y"));
        Console.WriteLine("wso=" + (string.Join("|", "x  y".Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) == "x|y"));
        Console.WriteLine("tc=" + (s.ToCharArray().Length == 6));
        char[] arr = new char[3];
        s.CopyTo(1, arr, 0, 3);
        Console.WriteLine("ct=" + (new string(arr) == ",b,"));
        Console.WriteLine("sd=" + (new string("hello".ToCharArray(1, 3)) == "ell"));
        StringBuilder sb = new StringBuilder("abcdef");
        Console.WriteLine("sbg=" + (sb[1] == 'b'));
        sb[0] = 'X';
        sb[2] = 'z';
        Console.WriteLine("sbs=" + (sb.ToString() == "Xbzdef"));
        Console.WriteLine("sbl=" + (sb.Length == 6));
        Console.WriteLine("sbmx=" + (sb.MaxCapacity >= 16));
        TimeSpan ts = new TimeSpan(0, 0, 0, 1, 250);
        Console.WriteLine("tsc=" + (ts.TotalMilliseconds == 1250.0));
        Console.WriteLine("tss=" + (ts.ToString() == "00:00:01.2500000"));
        Console.WriteLine("tadd=" + (ts.Add(TimeSpan.FromMilliseconds(750)).TotalSeconds == 2.0));
        Console.WriteLine("tsub=" + (ts.Subtract(TimeSpan.FromMilliseconds(250)).TotalMilliseconds == 1000.0));
        Console.WriteLine("tneg=" + ((-ts).TotalMilliseconds == -1250.0));
        Console.WriteLine("tsgn=" + (ts > TimeSpan.Zero));
        Console.WriteLine("tlt=" + (ts < TimeSpan.FromMinutes(1)));
        Console.WriteLine("tge=" + (ts >= TimeSpan.FromMilliseconds(1250.0)));
        Console.WriteLine("tne=" + (ts != TimeSpan.FromMilliseconds(1)));
        Console.WriteLine("tph=" + (ts.Hours == 0 && ts.Minutes == 0 && ts.Seconds == 1 && ts.Milliseconds == 250));
        TimeSpan fg = new TimeSpan(1, 2, 3, 4, 5);
        Console.WriteLine("tfg=" + (fg.Days == 1 && fg.Hours == 2 && fg.Minutes == 3 && fg.Seconds == 4 && fg.Milliseconds == 5));
        Console.WriteLine("tdiff=" + (fg.Subtract(ts).Ticks > 0));
        Console.WriteLine("tdur=" + (ts.Duration().TotalMilliseconds == 1250.0));
        Console.WriteLine("tt=" + (ts.Ticks == 12500000L));
        Console.WriteLine("tfromd=" + (TimeSpan.FromDays(1.5).TotalHours == 36.0));
        Console.WriteLine("tfromh=" + (TimeSpan.FromHours(0.5).TotalMinutes == 30.0));
        Console.WriteLine("tfromm=" + (TimeSpan.FromMinutes(0.5).TotalSeconds == 30.0));
        Console.WriteLine("tfroms=" + (TimeSpan.FromSeconds(0.5).TotalMilliseconds == 500.0));
        Console.WriteLine("tcst=" + (TimeSpan.Parse("00:00:01.2500000").Ticks == 12500000L));
        TimeSpan pr;
        Console.WriteLine("trp=" + (TimeSpan.TryParse("6", out pr) && pr.Days == 6));
        Console.WriteLine("trp2=" + (TimeSpan.TryParse("6.12:14:45", out pr) && pr.Days == 6 && pr.Hours == 12 && pr.Minutes == 14 && pr.Seconds == 45));
        Console.WriteLine("trp3=" + (TimeSpan.TryParse("12:34", out pr) && pr.Hours == 12 && pr.Minutes == 34));
        Console.WriteLine("trp4=" + (TimeSpan.TryParse("-0:10:0", out pr) && pr.Ticks == -6000000000L));
        Console.WriteLine("trp5=" + (TimeSpan.TryParse(" 1:00:00 ", out pr) && pr.Ticks == 36000000000L));
        Console.WriteLine("trp6=" + (TimeSpan.TryParse("6:12:14.5", out pr) && pr.Milliseconds == 500));
        Console.WriteLine("trp7=" + (TimeSpan.TryParse("10675200", out pr) == false));
        Console.WriteLine("trp8=" + (TimeSpan.TryParse("6:12:14:45", out pr) && pr.Days == 6 && pr.Hours == 12 && pr.Minutes == 14 && pr.Seconds == 45));
        Console.WriteLine("trp9=" + (TimeSpan.TryParse("", out pr) == false));
        Console.WriteLine("trpA=" + (TimeSpan.TryParse(null, out pr) == false));
        Console.WriteLine("trpB=" + (TimeSpan.Parse("00:00:01.2500000").Ticks == 12500000L));
        Console.WriteLine("tcz=" + (TimeSpan.Zero.Ticks == 0L));
        Console.WriteLine("tmx=" + (TimeSpan.MaxValue > TimeSpan.MinValue));
        Console.WriteLine("thp=" + (new TimeSpan(864000000000L).Days == 1));
    }
}
