//------------------------------------------------------------------------------
// loud-linq-die-listvt.cs -- LOUD contract case (transpile-time NotSupported).
//
// DefaultIfEmpty(source, defaultValue) over a List<T> whose element is a
// BY-VALUE VALUE STRUCT (System.DateTime / System.TimeSpan / System.Guid).
//
// Why this must stay LOUD rather than "work": a List<T> value element is BOXED
// in the listobj storage, and the LINQ list surface identifies the element
// family with a static IL2CPP_LIST_KIND_* code. Only System.Decimal has one
// (IL2CPP_LIST_KIND_DEC = 12); the other value structs have no list kind, so
// they fall into the REFERENCE tag and the caller's `value` (an Il2CppDateTime
// by value) is emitted into an Il2CppObject* parameter slot:
//
//   error: cannot convert 'il2cpp::runtime::Il2CppDateTime' to
//          'il2cpp::Il2CppObject*'   (MEASURED, audit-fix3-d2b)
//
// The transpiler now rejects it up front with a self-describing message naming
// the missing list storage kind. The ARRAY surface IS wired
// (il2cpp_linq_default_if_empty_family_dt / _ts / _guid / _dtoffset), so the
// same shape over a DateTime[] is a real case (audit-fix3-d2b / -d2h).
//
// guards: docs/audit/b5-gaps.md (a list value-struct element has no storage
// kind). Registered as the residual of the impl-loudvalstruct seat.
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cases
{
    public static class LoudLinqDieListVt
    {
        public static void Main()
        {
            DateTime dt = new DateTime(2021, 6, 7, 8, 9, 10);
            List<DateTime> el = new DateTime[0].ToList();
            DateTime[] rl = el.DefaultIfEmpty(dt).ToArray();
            Console.WriteLine("dt_list_len=" + rl.Length);
            Console.WriteLine("dt_list_0=" + rl[0].Year + "-" + rl[0].Month + "-" + rl[0].Day);
        }
    }
}
