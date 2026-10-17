using System.Reflection.Metadata;

namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Module -- REFL6B (item B3): System.Reflection.Module folds.
// Owners: System.Type / the MemberInfo-derived owners (get_Module) +
// System.Reflection.Module (get_Name).
//
// The simplified Module model: a Module VALUE is the MODULE-NAME STRING (the
// single-module assembly's manifest module name -- md.GetModuleDefinition()
// .Name, e.g. "p-refl6x.exe"). net48 Module.Name returns exactly that string
// (oracle-pinned [mod.t]/[mod.f] = "p-refl6x.exe"), so:
//   * get_Module (on a Type VALUE or a member-name VALUE receiver) folds to
//     the module-name literal -- the result is receiver-independent (one
//     module per assembly) like the get_Assembly fold (MethodMap.Type.cs
//     TryMapTypeAssembly). ReturnTypeName "System.Reflection.Module" keeps the
//     fold-linkage (a later Module::get_Name resolves on this domain).
//   * System.Reflection.Module::get_Name is the IDENTITY fold (the Module
//     VALUE IS the name string) -- the CultureInfo.get_Name / MemberInfo.
//     get_Name string-receiver precedent (MethodMap.CultureInfo.cs /
//     MethodMap.Object.cs).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapModule(MetadataReader md, string owner, string name, int count)
    {
        bool isReflectReceiver = owner is "System.Type" or "System.Reflection.Assembly"
            or "System.Reflection.MemberInfo" or "System.Reflection.FieldInfo"
            or "System.Reflection.PropertyInfo" or "System.Reflection.MethodInfo"
            or "System.Reflection.MethodBase" or "System.Reflection.ConstructorInfo"
            or "System.Reflection.EventInfo" or "System.Reflection.Module";
        if (!isReflectReceiver) return null;
        switch (name)
        {
            case "get_Module":
                if (count == 0)
                {
                    string moduleName;
                    try
                    {
                        moduleName = md.GetString(md.GetModuleDefinition().Name);
                    }
                    catch
                    {
                        moduleName = "";
                    }
                    // The fold pushes the module-name string as the Module VALUE.
                    return new ResolvedMethod(ResolvedMethod.LiteralMarker + moduleName, 0, false, true, true,
                        Array.Empty<bool>(), true, ReturnTypeName: "System.Reflection.Module");
                }
                throw new NotSupportedException(
                    $"{owner}::get_Module with {count} parameter(s) not wired in refl6b -- the () property folds to the module-name literal");
            case "get_Name":
                // Module::get_Name -- the Module VALUE is the module name, so
                // the getter is the identity fold (receiver == result).
                if (owner == "System.Reflection.Module" && count == 0)
                    return new ResolvedMethod(ResolvedMethod.IdentityMarker, 0, false, true, true,
                        Array.Empty<bool>(), true);
                return null;
            default:
                return null;
        }
    }
}