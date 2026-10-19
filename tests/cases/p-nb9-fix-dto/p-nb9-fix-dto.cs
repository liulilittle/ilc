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
//   [addyears]=2021-02-28      2020-02-29 + 1 year (month-end truncation)
//   [addmonths]=2020-02-29     2020-01-31 + 1 month
//   [addmonths_neg]=2020-02-29 2020-03-31 - 1 month
//   [to_plus14]=ArgumentOutOfRangeException
//   [to_minus14]=ArgumentOutOfRangeException
//   [sub_local]=ArgumentOutOfRangeException
//   [add_local]=ArgumentOutOfRangeException
//   [addyears_range]=ArgumentOutOfRangeException
//   [addmonths_range]=ArgumentOutOfRangeException
//
// The boundary checks are written as inline try/catch blocks: the DTO result
// of an expression-lambda body is a separate pre-existing local-typing gap,
// unrelated to this batch.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PNb9FixDto
    {
        public static void Main()
        {
            var leap = new DateTimeOffset(2020, 2, 29, 12, 0, 0, TimeSpan.Zero);
            var leapPlus = leap.AddYears(1);
            Console.WriteLine("[addyears]=" + leapPlus.ToString("yyyy-MM-dd"));

            var jan31 = new DateTimeOffset(2020, 1, 31, 12, 0, 0, TimeSpan.Zero);
            var janPlus = jan31.AddMonths(1);
            Console.WriteLine("[addmonths]=" + janPlus.ToString("yyyy-MM-dd"));

            var mar31 = new DateTimeOffset(2020, 3, 31, 12, 0, 0, TimeSpan.Zero);
            var marMinus = mar31.AddMonths(-1);
            Console.WriteLine("[addmonths_neg]=" + marMinus.ToString("yyyy-MM-dd"));

            var max = new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero);
            var min = new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var negOffset = new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.FromHours(-14));
            var posOffset = new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.FromHours(14));

            try
            {
                var bad = max.ToOffset(TimeSpan.FromHours(14));
                Console.WriteLine("[to_plus14]=ok:" + bad.Ticks);
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("[to_plus14]=ArgumentOutOfRangeException");
            }

            try
            {
                var bad = min.ToOffset(TimeSpan.FromHours(-14));
                Console.WriteLine("[to_minus14]=ok:" + bad.Ticks);
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("[to_minus14]=ArgumentOutOfRangeException");
            }

            try
            {
                var bad = negOffset.Subtract(TimeSpan.FromHours(2));
                Console.WriteLine("[sub_local]=ok:" + bad.Ticks);
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("[sub_local]=ArgumentOutOfRangeException");
            }

            try
            {
                var bad = posOffset.Add(TimeSpan.FromHours(2));
                Console.WriteLine("[add_local]=ok:" + bad.Ticks);
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("[add_local]=ArgumentOutOfRangeException");
            }

            try
            {
                var bad = max.AddYears(1);
                Console.WriteLine("[addyears_range]=ok:" + bad.Ticks);
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("[addyears_range]=ArgumentOutOfRangeException");
            }

            try
            {
                var bad = max.AddMonths(1);
                Console.WriteLine("[addmonths_range]=ok:" + bad.Ticks);
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("[addmonths_range]=ArgumentOutOfRangeException");
            }
        }
    }
}
