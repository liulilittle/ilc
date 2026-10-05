//------------------------------------------------------------------------------
// COM stage 17 batch 1 fixture -- a minimal "real WinForms-shaped" contract
// mirror. This is NOT System.Windows.Forms.dll (the transpiler loads user
// assemblies; the winforms1 fixture already mirrors WfControl etc. with real
// P/Invoke). Here we mirror ONLY the IDropTarget contract shape: the
// DragDropEffects enum + a minimal DragEventArgs + the 4-method IDropTarget
// interface with DragEventArgs REF params and void returns -- the FIRST
// r:-param interface-dispatch coverage (all existing interface e2e cases are
// int-signed).
//
// Consumption:
//   * tests/runtime/test_com.cpp section 19 (headless, every platform):
//     il2cpp_runtime_init() registers these types, then
//     il2cpp_interface_dispatch is called slot by slot on objects created via
//     il2cpp_object_new -- asserting the method ran + arg side effects +
//     dual-implementer isolation + CCW/GCHandle target recovery (the batch-3
//     bridge base).
//   * standalone e2e (run-tests.ps1 auto-GLOBs tests/cases/com17type/):
//     Cases.Com17type.Main exercises the generated callvirt path (dual
//     implementers keep devirt S3 from collapsing it to a direct call).
//
// Constraints (audit): no windows, no threads, no P/Invoke, no static-field
// initializers -- il2cpp_runtime_init() runs the Main-type cctor, so it must
// stay side-effect free; instance observer fields only (no static roots).
//
// Regenerate the committed generated output (tests/cases/com17type/gen_v1/):
//   dotnet build tests/cases/com17type/com17type.csproj -c Release -o tests/cases/com17type/out
//   dotnet run --project src/il2cppc -- tests/cases/com17type/out/com17type.exe tests/cases/com17type/gen_v1
//------------------------------------------------------------------------------
using System;

public enum DragDropEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
}

// Minimal DragEventArgs -- X/Y/Effect + the IDataObject Data reference (batch
// 3: the typed-RCW direction fills Data with a managed IDataObject wrapper
// over the native IDataObject* the OLE drag-drop delivers). Effect is stored
// as its int32 underlying value: the M2 transpiler rejects VALUE-TYPE
// instance fields (enum-typed fields are 0x11 ELEMENT_TYPE_VALUETYPE,
// MetadataLoader DecodeFieldSignature throws "value-type instance fields not
// supported in M2"), so the enum flows through the ctor param and the (int)
// cast only -- the generated struct member is the int32 slot the test asserts
// against. Data is an INTERFACE-typed reference field: interfaces have no
// generated C++ struct, so the transpiler emits it as a plain Il2CppObject*
// GC reference slot (TypeEmitter ReferenceCppType interface branch).
public class DragEventArgs
{
    public IDataObject Data;
    public int X;
    public int Y;
    public int Effect;

    public DragEventArgs(IDataObject data, int x, int y, DragDropEffects effect)
    {
        Data = data;
        X = x;
        Y = y;
        Effect = (int)effect;
    }
}

// The real WinForms IDropTarget contract shape (4 methods, DragEventArgs REF
// param, void returns -- the batch-3 OnDragEnter(DragEventArgs) bridge shape).
public interface IDropTarget
{
    void OnDragEnter(DragEventArgs e);
    void OnDragOver(DragEventArgs e);
    void OnDragLeave(DragEventArgs e);
    void OnDragDrop(DragEventArgs e);
}

// First implementer -- implicit interface implementation (public methods, the
// only SigKey-compatible form). Writes instance observer fields.
public class WfDropSource : IDropTarget
{
    public int EnterCount;
    public int OverCount;
    public int LeaveCount;
    public int DropCount;
    public int LastX;
    public int LastY;
    public int LastEffect;

    public void OnDragEnter(DragEventArgs e)
    {
        EnterCount++;
        LastX = e.X;
        LastY = e.Y;
        LastEffect = (int)e.Effect;
    }

    public void OnDragOver(DragEventArgs e)
    {
        OverCount++;
    }

    public void OnDragLeave(DragEventArgs e)
    {
        LeaveCount++;
    }

    public void OnDragDrop(DragEventArgs e)
    {
        DropCount++;
        LastX = e.X;
        LastY = e.Y;
        LastEffect = (int)e.Effect;
    }
}

// Second implementer -- distinct counters, proving dual-implementer isolation
// (each object's dispatch lands on ITS OWN methods) and keeping the e2e
// callvirt on the runtime dispatch path (2 implementers -> no devirt S3).
public class WfDropSource2 : IDropTarget
{
    public int EnterCount;
    public int OverCount;
    public int LeaveCount;
    public int DropCount;
    public int LastX;
    public int LastY;
    public int LastEffect;

    public void OnDragEnter(DragEventArgs e)
    {
        EnterCount++;
        LastX = e.X;
        LastY = e.Y;
        LastEffect = (int)e.Effect;
    }

    public void OnDragOver(DragEventArgs e)
    {
        OverCount++;
    }

    public void OnDragLeave(DragEventArgs e)
    {
        LeaveCount++;
    }

    public void OnDragDrop(DragEventArgs e)
    {
        DropCount++;
        LastX = e.X;
        LastY = e.Y;
        LastEffect = (int)e.Effect;
    }
}

// NOTE: no derived type here (tests/cases/interface already proves the
// audit-H7 inherited-interface dispatch for field-less implementers). The
// M2 transpiler lays out ONLY a type's own instance fields (EmitClassStruct
// / LayoutInstanceFields), so a field-carrying base (WfDropSource) cannot be
// inherited without its observer fields falling out of the derived struct's
// 8-byte grid -- a WfDropSourceSub : WfDropSource {} would dispatch the
// inherited OnDragEnter that writes OOB. Batch-1 keeps every implementer a
// direct System.Object base with its own fields.

//------------------------------------------------------------------------------
// COM stage 17 batch 2 -- the typed-RCW foundation. A SIMPLIFIED IDataObject
// contract: the real OLE interface's FORMATETC/STGMEDIUM pointers are carried
// as int32 discriminators (GetData(fmt, medium) / SetData(fmt, value) /
// QueryGetData(fmt) -> int), so the CCW's IDataObject view can bridge the
// native oleidl slots to MANAGED interface methods by name. INTERFACE SLOT
// ORDER IS ALPHABETICAL, not declarative (the planner sorts virtual methods):
// GetData=0 / QueryGetData=1 / SetData=2 -- exactly the mismatch the
// name-resolution dispatch exists to bridge (native slot 3 GetData / 5
// QueryGetData / 7 SetData map onto those alphabetical slots).
//------------------------------------------------------------------------------
public interface IDataObject
{
    void GetData(int fmt, int medium);
    int QueryGetData(int fmt);
    void SetData(int fmt, int value);
}

// First IDataObject implementer -- implicit public implementation (the only
// SigKey-compatible form) writing instance observer fields (GetDataCount /
// SetDataCount / QueryCount / LastFmt / LastMedium / LastValue / QueryResult).
public class WfDataObject : IDataObject
{
    public int GetDataCount;
    public int SetDataCount;
    public int QueryCount;
    public int LastFmt;
    public int LastMedium;
    public int LastValue;
    public int QueryResult;

    public void GetData(int fmt, int medium)
    {
        GetDataCount++;
        LastFmt = fmt;
        LastMedium = medium;
    }

    public int QueryGetData(int fmt)
    {
        QueryCount++;
        LastFmt = fmt;
        return QueryResult;
    }

    public void SetData(int fmt, int value)
    {
        SetDataCount++;
        LastFmt = fmt;
        LastValue = value;
    }
}

// Second implementer -- distinct counters, proving dual-implementer isolation
// through the IDataObject view (each CCW dispatches into ITS OWN object's
// methods) and keeping any e2e callvirt on the runtime dispatch path.
public class WfDataObject2 : IDataObject
{
    public int GetDataCount;
    public int SetDataCount;
    public int QueryCount;
    public int LastFmt;
    public int LastMedium;
    public int LastValue;
    public int QueryResult;

    public void GetData(int fmt, int medium)
    {
        GetDataCount++;
        LastFmt = fmt;
        LastMedium = medium;
    }

    public int QueryGetData(int fmt)
    {
        QueryCount++;
        LastFmt = fmt;
        return QueryResult;
    }

    public void SetData(int fmt, int value)
    {
        SetDataCount++;
        LastFmt = fmt;
        LastValue = value;
    }
}

// Stage 19: the typed-automation interface -- double (VT_R8) and string
// (VT_BSTR) parameters + a double return, the IDispatch::Invoke typed-argument
// surface beyond the int32 (VT_I4) shape. Scale(factor, label) is the void
// two-arg case; Rate(factor) is the double-return one-arg case. The observer
// stores the factor's INTEGER part (the M2 field decoder supports int32
// instance fields; the double flows through the automation channel itself).
public interface IAutoScale
{
    void Scale(double factor, string label);
    double Rate(double factor);
    int ScaleCountProp { get; set; }
    int Adjust(bool on, uint amount);
    bool IsEnabled();
    int GetCount();
    string GetLabel();
    void SetBig(long value);
    long GetBig();
}

public class WfAutoScale : IAutoScale
{
    public int ScaleCount;
    public int LastFactorInt;
    public string LastLabel = "";
    public int RateResultInt;
    public void Scale(double factor, string label)
    {
        ScaleCount++;
        LastFactorInt = (int)factor;
        LastLabel = label;
    }
    public double Rate(double factor)
    {
        return RateResultInt * factor;
    }
    public int ScaleCountProp
    {
        get { return ScaleCount; }
        set { ScaleCount = value; }
    }
    public int Adjust(bool on, uint amount)
    {
        return (on ? 1 : 0) + (int)amount;
    }
    public bool IsEnabled()
    {
        return ScaleCount > 0;
    }
    public int GetCount()
    {
        return ScaleCount;
    }
    public string GetLabel()
    {
        return LastLabel;
    }
    public int BigLow;
    public void SetBig(long value)
    {
        BigLow = (int)value;
    }
    public long GetBig()
    {
        return 5000000000L;
    }
}

namespace Cases
{
    public static class Com17type
    {
        public static void Main()
        {
            // Standalone e2e path (test_com section 19 drives dispatch
            // directly and never calls Main). Exercises the generated
            // callvirt -> il2cpp_interface_dispatch path; two implementers
            // keep the call on the runtime dispatch (no devirt S3 collapse).
            WfDropSource src = new WfDropSource();
            IDropTarget target = src;
            DragEventArgs e = new DragEventArgs(null, 0x1122, 0x3344, DragDropEffects.Copy);
            target.OnDragEnter(e);
            target.OnDragOver(e);
            target.OnDragLeave(e);
            target.OnDragDrop(new DragEventArgs(null, 0x5566, 0x7788, DragDropEffects.Move));
            Console.WriteLine("a=" + src.EnterCount + "," + src.OverCount + "," + src.LeaveCount + "," + src.DropCount);
            Console.WriteLine("last=" + src.LastX + "," + src.LastY + "," + src.LastEffect);

            WfDropSource2 src2 = new WfDropSource2();
            IDropTarget target2 = src2;
            target2.OnDragEnter(e);
            target2.OnDragDrop(e);
            Console.WriteLine("b=" + src2.EnterCount + "," + src2.DropCount + ",a=" + src.EnterCount);
        }
    }
}
