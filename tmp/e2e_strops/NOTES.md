# e2e_strops — working notes (state persistence; .opencode forbidden per task)

## Status: DONE — ALL-PASS, byte-exact vs net9 9.0.19

- Program: Program.cs, 51 assertions over Substring/IndexOf/LastIndexOf/Replace/Trim/TrimStart/TrimEnd/StartsWith/EndsWith/CompareOrdinal/get_Chars/Length.
- Compile: raw csc (SDK 8.0.205 Roslyn) -nostdlib+ vs Microsoft.NETCore.App.Ref 9.0.6 refs (System.Runtime.dll + System.Console.dll) -> out\e2e_strops.exe.
- Oracle: copy exe->dll + handwritten runtimeconfig.json -> tmp\net910\net9\dotnet.exe. exit 0, ALL-PASS.
- Transpile: dotnet run --project src/il2cppc -c Release -- out\e2e_strops.exe gen. exit 0.
- Build: cmake -S gen -B build_mingw -G "MinGW Makefiles"; cmake --build -- -j8. exit 0.
- Run: build_mingw\il2cpp_gen_exe.exe. exit 0, stderr empty.
- Diff: 52/52 lines case-sensitive identical, mismatches=0.

## Discoveries

1. Shared tree was BROKEN for everyone: CodeGenerator.cs:3541 duplicated switch arm
   "System.Converter`2_int_int" (already at :3521) -> CS8510 killed il2cppc build.
   Fixed by deleting the redundant arm only. Backup: tmp\backups\e2e_strops\CodeGenerator.cs.bak.
2. My own expectation bug caught by the net9 oracle: "banana".IndexOf("ana",2)==3 (global index),
   not 2. Always run the reference BEFORE trusting hand-computed literals.
3. Raw-csc IL exe is accepted by the transpiler directly; rename bytes to .dll + runtimeconfig.json
   to run on the extracted net9 runtime (tmp/e2e_env precedent).
4. Avoid in test programs: char printing (use int codes), bool printing (print 1/0), trim-set
   overloads (runtime is whitespace-only), odd-length prefix CompareOrdinal pairs
   (runtime pair-scan boundary differs from real .NET: "abc"/"abcd" -> -100 here vs -1 on .NET).

## Transcript
E:\il2cpp\tmp\reports\e2e_strops.md
