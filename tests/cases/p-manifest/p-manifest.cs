using System;

//------------------------------------------------------------------------------
// p-manifest -- nb10 Windows application manifest e2e probe.
//
// The sidecar p-manifest.exe.manifest (asInvoker) sits next to the built exe.
// il2cppc resolves it WITHOUT any flag (the same external-manifest convention
// Windows honors), emits app.manifest + app.rc, and the generated CMake project
// links the .rc into il2cpp_gen_exe as RT_MANIFEST resource id 1.
//
// This probe pins the program OUTPUT (byte-exact vs net48); the embedded
// resource itself is verified by scripts/verify-manifest.ps1 (a PE resource
// reader), because stdout cannot observe it.
//------------------------------------------------------------------------------
namespace Cases
{
    public static class PManifest
    {
        public static void Main()
        {
            Console.WriteLine("[manifest.run]=ok");
            Console.WriteLine("[manifest.sum]=" + (1 + 2 + 3));
        }
    }
}
