using System;

//------------------------------------------------------------------------------
// convert-narrow -- Convert.ToXxx(double) over the narrow integer targets.
//
// net48 rounds the double half-to-even then range-checks the target width
// (Convert.ToByte(3.7) == 4; Convert.ToByte(1000.0) throws OverflowException;
// Convert.ToUInt32(-1.0) throws OverflowException). A float source rides the
// double slot.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class ConvertNarrow
    {
        public static void Main()
        {
            double d = 3.7;
            Console.WriteLine("[byte] " + Convert.ToByte(d));
            Console.WriteLine("[sbyte] " + Convert.ToSByte(d));
            Console.WriteLine("[int16] " + Convert.ToInt16(d));
            Console.WriteLine("[uint16] " + Convert.ToUInt16(d));
            Console.WriteLine("[uint32] " + Convert.ToUInt32(d));
            Console.WriteLine("[uint64] " + Convert.ToUInt64(d));
            Console.WriteLine("[fbyte] " + Convert.ToByte(2.5f));

            try { Console.WriteLine(Convert.ToByte(1000.0)); }
            catch (Exception e) { Console.WriteLine("[ovf] " + e.GetType().Name); }
            try { Console.WriteLine(Convert.ToUInt32(-1.0)); }
            catch (Exception e) { Console.WriteLine("[neg] " + e.GetType().Name); }
        }
    }
}
