# r78 Generation Fix Report

## Summary

r78 generation (mscorlib transpilation) failed with 0 cpp files generated. Multiple fixes were applied to handle mscorlib metadata, but the working tree remains in an inconsistent state that prevents successful compilation.

## Root Cause Analysis

The r78 generation fails during assembly loading with errors like:
- `field type byte 0x1F not supported in M2`
- `field type byte 0x02 not supported in M2`
- `field type byte 0x15 (generic instantiation) not supported in M2`
- `property type byte 0x19 not supported in refl3`

These are NOT exceptions being swallowed in `CodeGenerator.Generate`. The exceptions occur in `MetadataLoader.LoadAssembly` BEFORE `CodeGenerator.Generate` is called, so the entire generation aborts.

## Fixes Applied

1. **MetadataLoader.cs**: Added handling for custom modifiers (0x1F/0x20), basic value-type instance fields (0x02-0x0D), generic instantiation instance fields (0x15), array instance fields (0x14/0x1D), exotic field shapes (0x0F/0x10/0x1B/0x16/0x1E), and exotic ReadTypeName shapes.

2. **MethodSignatureCoder.cs**: Added handling for IntPtr/UIntPtr property types (0x18/0x19) and exotic property types (0x0F/0x10/0x1B/0x16).

3. **CodeGenerator.cs**: Added try-catch around `MethodBodyReader.Read` in both the inlineCandidates scan loop and the main emission loop to prevent a single bad method from aborting the entire generation.

4. **TypeEmitter.cs**: Added handling for basic value-type nested fields.

## Current State

Build fails with errors in `TypeKindResolver.cs` which references `StackFold._curClosureArgs` and `FieldTypeKind.Double/Float/Int64/UInt64` - symbols that exist in the working tree's `FieldDef.cs` (modified to include them) but the file `TypeKindResolver.cs` is untracked (not in git).

## Files Modified

| File | Change |
|------|--------|
| src/il2cppc/Emit/CodeGenerator.cs | try-catch around MethodBodyReader.Read |
| src/il2cppc/MetadataLoader.cs | Handle mscorlib exotic field/property types |
| src/il2cppc/Emit/MethodSignatureCoder.cs | Handle mscorlib exotic property types |
| src/il2cppc/Emit/TypeEmitter.cs | Handle mscorlib exotic nested field types |

## Result

**Generation could not be completed.** The working tree has inconsistencies between tracked and untracked files that prevent compilation. r78 src/cpp file count: **0**.

## Recommendation

1. Either commit TypeKindResolver.cs and ensure FieldDef.cs enum matches, OR
2. Delete TypeKindResolver.cs and revert FieldDef.cs to HEAD, OR
3. Use `git checkout HEAD -- .` to reset all tracked files and remove untracked source files before attempting r78 again.
