using System;

//------------------------------------------------------------------------------
// p-nb9-fix-dto -- nb9 fix batch: DateTimeOffset.AddYears/AddMonths and the
// LOCAL-CLOCK range validation on the arithmetic surface.
//
// Before this fix:
//   * AddYears/AddMonths were not wired at all (late C0 phantom symbol).
//   * Add/Subtract(TimeSpan)/AddTicks/ToOffset/operators validated only the
//     UTC ticks, so a value whose LOCAL clock left [0001-01-01, 9999-12-31]
//     came back instead of throwing ArgumentOutOfRangeException.
//
// net48 oracle (host .NET Framework 4.x):
//   [addyears]= ... (month-end truncation: 2020-02-29 +1y -> 2021-02-28)
//   [addmonths]= ... (2020-01-31 +1mo -> 2020-02-29)
//   [to_plus14]=ArgumentOutOfRangeException
//   [to_minus14]=ArgumentOutOfRangeException
//   [sub_local]=ArgumentOutOfRangeException
//   [add_local]=ArgumentOutOfRangeException
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9FixDto
    {
        static string Name(Action a)
        {
            try
            {
                a();
                return "ok";
            }
            catch (Exception ex)
            {
                return ex.GetType().Name;
            }
        }

        public static void Main()
        {
            var leap = new DateTimeOffset(2020, 2, 29, 12, 0, 0, TimeSpan.Zero);
            Console.WriteLine("[addyears]=" + leap.AddYears(1).ToString("yyyy-MM-dd"));
            Console.WriteLine("[addmonths]=" + new DateTimeOffset(2020, 1, 31, 12, 0, 0, TimeSpan.Zero)
                .AddMonths(1).ToString("yyyy-MM-dd"));
            Console.WriteLine("[addmonths_neg]=" + new DateTimeOffset(2020, 3, 31, 12, 0, 0, TimeSpan.Zero)
                .AddMonths(-1).ToString("yyyy-MM-dd"));

            var max = new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero);
            var min = new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero);

            Console.WriteLine("[to_plus14]=" + Name(() => max.ToOffset(TimeSpan.FromHours(14))));
            Console.WriteLine("[to_minus14]=" + Name(() => min.ToOffset(TimeSpan.FromHours(-14))));

            var negOffset = new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.FromHours(-14));
            Console.WriteLine("[sub_local]=" + Name(() => negOffset.Subtract(TimeSpan.FromHours(2))));

            var posOffset = new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.FromHours(14));
            Console.WriteLine("[add_local]=" + Name(() => posOffset.Add(TimeSpan.FromHours(2))));

            Console.WriteLine("[addyears_range]=" + Name(() => max.AddYears(1)));
            Console.WriteLine("[addmonths_range]=" + Name(() => max.AddMonths(1)));
        }
    }
}
