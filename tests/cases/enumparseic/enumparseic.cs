using System;

namespace Cases
{
    public static class EnumParseIc
    {
        enum Color { Red, Green, Blue = 7, Alpha }

        public static void Main()
        {
            // ignoreCase: true -- ASCII case-insensitive NAME matching
            Console.WriteLine(Enum.Parse(typeof(Color), "red", true));
            Console.WriteLine(Enum.Parse(typeof(Color), "GREEN", true));
            Console.WriteLine(Enum.Parse(typeof(Color), "bLuE", true));
            Console.WriteLine(Enum.Parse(typeof(Color), "ALPHA", true));
            // ASCII whitespace is trimmed before the (case-insensitive) match
            Console.WriteLine(Enum.Parse(typeof(Color), "  alpha  ", true));
            // ignoreCase: false -- exact case still required (net48)
            try
            {
                Enum.Parse(typeof(Color), "red", false);
                Console.WriteLine("cs-parse: no-throw");
            }
            catch (ArgumentException)
            {
                Console.WriteLine("cs-parse: throw");
            }
            // the numeric path is unaffected by ignoreCase
            Console.WriteLine(Enum.Parse(typeof(Color), "7", true));
            Console.WriteLine(Enum.Parse(typeof(Color), " 3 ", false));
            // unknown name -> ArgumentException regardless of ignoreCase
            try
            {
                Enum.Parse(typeof(Color), "xyz", true);
                Console.WriteLine("unknown: no-throw");
            }
            catch (ArgumentException)
            {
                Console.WriteLine("unknown: throw");
            }
            // ToString(string format): D/G/F/X, case-insensitive specifier
            Color c = Color.Blue; // 7
            Console.WriteLine(c.ToString("D"));
            Console.WriteLine(c.ToString("d"));
            Console.WriteLine(c.ToString("G"));
            Console.WriteLine(c.ToString("g"));
            Console.WriteLine(c.ToString("F"));
            Console.WriteLine(c.ToString("f"));
            Console.WriteLine(c.ToString("X"));
            Console.WriteLine(c.ToString("x"));
            // null format -> ArgumentNullException (net48)
            try
            {
                c.ToString((string)null);
                Console.WriteLine("fmt-null: no-throw");
            }
            catch (ArgumentNullException)
            {
                Console.WriteLine("fmt-null: throw");
            }
            // unknown specifier -> FormatException (net48)
            try
            {
                c.ToString("Z");
                Console.WriteLine("fmt-z: no-throw");
            }
            catch (FormatException)
            {
                Console.WriteLine("fmt-z: throw");
            }
            try
            {
                c.ToString("DD");
                Console.WriteLine("fmt-dd: no-throw");
            }
            catch (FormatException)
            {
                Console.WriteLine("fmt-dd: throw");
            }
            // Enum.IsDefined: string names (case-sensitive) + numeric values
            Console.WriteLine(Enum.IsDefined(typeof(Color), "Red"));
            Console.WriteLine(Enum.IsDefined(typeof(Color), "red"));
            Console.WriteLine(Enum.IsDefined(typeof(Color), "  Blue  "));
            Console.WriteLine(Enum.IsDefined(typeof(Color), 7));
            Console.WriteLine(Enum.IsDefined(typeof(Color), 5));
            Console.WriteLine(Enum.IsDefined(typeof(Color), c));
            Console.WriteLine(Enum.IsDefined(typeof(Color), "Xyz"));
            // Enum.Format(Type, object, string): the static D/G/F/X form;
            // the value must be the enum or its underlying type (net48
            // rejects a member-NAME string with ArgumentException).
            Console.WriteLine(Enum.Format(typeof(Color), 7, "D"));
            Console.WriteLine(Enum.Format(typeof(Color), Color.Alpha, "G"));
            try
            {
                Enum.Format(typeof(Color), "blue", "G");
                Console.WriteLine("fmt-str: no-throw");
            }
            catch (ArgumentException)
            {
                Console.WriteLine("fmt-str: throw");
            }
            Console.WriteLine(Enum.Format(typeof(Color), 7, "X"));
            Console.WriteLine(Enum.Format(typeof(Color), Color.Green, "F"));
            Console.WriteLine(Enum.Format(typeof(Color), Color.Red, "d"));
            // Enum.GetValues: member values in declaration order
            int[] vals = (int[])Enum.GetValues(typeof(Color));
            for (int i = 0; i < vals.Length; i++)
                Console.WriteLine(vals[i]);
            // Enum.ToObject: the boxed enum with the enum's own klass
            Console.WriteLine(Enum.ToObject(typeof(Color), 7));
            Console.WriteLine(Enum.ToObject(typeof(Color), 1));
            // Enum.CompareTo: width-aware payload comparison
            Console.WriteLine(c.CompareTo(Color.Green)); // 7 vs 1 -> 1
            Console.WriteLine(c.CompareTo(Color.Blue));  // 7 vs 7 -> 0
            Console.WriteLine(c.CompareTo(Color.Alpha)); // 7 vs 8 -> -1
            Console.WriteLine(c.CompareTo(null));        // 1 (net48)
            // Enum.TryParse: name (case-sensitive) + numeric, out written only on success
            Color outVal;
            Console.WriteLine(Enum.TryParse<Color>("Blue", out outVal) && outVal == Color.Blue);
            Console.WriteLine(Enum.TryParse<Color>("  Alpha  ", out outVal) && outVal == Color.Alpha);
            Console.WriteLine(Enum.TryParse<Color>("green", out outVal) && outVal == Color.Green);
            Console.WriteLine(Enum.TryParse<Color>("99", out outVal) && outVal == Color.Alpha);
            Console.WriteLine(Enum.TryParse<Color>("xyz", out outVal));
            // ignoreCase overload
            Console.WriteLine(Enum.TryParse<Color>("GREEN", true, out outVal) && outVal == Color.Green);
            Console.WriteLine(Enum.TryParse<Color>("blue", false, out outVal) && outVal == Color.Blue);
        }
    }
}
