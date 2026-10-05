using System;

public enum MyColor { Red, Green }

namespace AuditFix
{
    public static class BoxEnum
    {
        private static object _slot;

        public static void Main()
        {
            // Enum box (timecase 施工单 2 follow-up): both a USER enum
            // TypeDef (MyColor) and a BCL enum TypeRef (System.DayOfWeek)
            // must box onto the int64 carrier (icall_box_long).
            _slot = MyColor.Green;
            _slot = DayOfWeek.Monday;
            Console.WriteLine(_slot == null ? 1 : 0); // 0
        }
    }
}
