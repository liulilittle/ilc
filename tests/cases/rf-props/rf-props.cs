//------------------------------------------------------------------------------
// rf-props.cs -- RF-N2 e2e probe: PropertyInfo.GetValue/SetValue through the
// property table's getterName/setterName accessors (the net48 oracle, byte-
// for-byte). The receiver is the PROPERTY-NAME string (the simplified member
// model -- as il2cpp_typeinfo_get_property returns); the runtime joins the
// declaring type from the TARGET object's klass and dispatches the getter/
// setter accessor method through the same invoker path as MethodInfo.Invoke
// (RF-1's 'this' injection precedent -- the C3-2 event accessor shape).
//
// The accessors are COMPUTED (a real method body -- `+ 0` / `+ ""` on a
// backing field) so the transpiler emits an actual accessor function with a
// fnPtr. A plain auto-property accessor is FOLDED to a field access and its
// method-table entry carries fnPtr=nullptr (runtime-unreachable, the same
// documented boundary as MethodInfo.Invoke on a folded accessor -- RF-1's
// probe used real method bodies for the same reason).
//
//   GetNum()    : int32 property read  (getter get_Num)
//   SetNum(int) : int32 property write (setter set_Num)
//   GetName()   : string property read (getter get_Name)
//   SetName     : string property write (setter set_Name)
//   StrBase     : a BASE-class property -- the declaring type join must walk
//                 the own + base chain (the klass join + base-chain property
//                 lookup, matching net48 inherited-property semantics).
//
// Every probe prints a bool against the net48 oracle value.
//------------------------------------------------------------------------------
using System;
using System.Reflection;

namespace Cases
{
    public class PropHostBase
    {
        private int _num;
        private string _name;

        // Computed accessors (non-foldable bodies -- see header). The body is
        // a CALL to a private helper (csc folds a plain `_num + 0` away at the
        // IL level, leaving the simple ldfld/stfld shape); the helper is a
        // normal method, never get_/set_-named, so AccessorAnalyzer never
        // folds it, and the accessor's call body does not match the fold
        // shape either -- both stay real emitted functions.
        public int Num
        {
            get { return GetNum(); }
            set { SetNum(value); }
        }
        private int GetNum() { return _num; }
        private void SetNum(int v) { _num = v; }

        public string Name
        {
            get { return GetName(); }
            set { SetName(value); }
        }
        private string GetName() { return _name; }
        private void SetName(string v) { _name = v; }
    }

    public class PropHost : PropHostBase
    {
    }

    public static class RfProps
    {
        public static void Main()
        {
            var h = new PropHost();
            h.Num = 10;
            h.Name = "hi";

            // instance, int32 property READ -- getter get_Num() dispatched on
            // the target instance (the invoker 'this' slot; net48: 10).
            Console.WriteLine("get_num=" + ((int)typeof(PropHost).GetProperty("Num").GetValue(h) == 10));
            // instance, int32 property WRITE -- setter set_Num(int) dispatched
            // on the target (the write must land on the real target).
            typeof(PropHost).GetProperty("Num").SetValue(h, 7);
            Console.WriteLine("set_num_state=" + (h.Num == 7));
            Console.WriteLine("get_num_after_set=" + ((int)typeof(PropHost).GetProperty("Num").GetValue(h) == 7));
            // instance, string property READ (reference slot).
            Console.WriteLine("get_name=" + ((string)typeof(PropHost).GetProperty("Name").GetValue(h) == "hi"));
            // instance, string property WRITE (reference slot write).
            typeof(PropHost).GetProperty("Name").SetValue(h, "yo");
            Console.WriteLine("set_name_state=" + (h.Name == "yo"));
            // null target -> TargetException (net48 instance-property
            // doctrine: PropertyInfo.GetValue(null) on an instance property
            // throws TargetException, NOT NullReferenceException -- the
            // typed catch distinguishes them byte-for-byte).
            PropertyInfo num = typeof(PropHost).GetProperty("Num");
            bool caught = false;
            try { num.GetValue(null); }
            catch (TargetException) { caught = true; }
            Console.WriteLine("null_target_exc=" + caught);
        }
    }
}
