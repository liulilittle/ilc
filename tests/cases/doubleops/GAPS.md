# GAPS.md -- doubleops recorded findings (e2e batch C54, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 86-line byte-exact).

## D-DBL-1 -- double `%` (IL rem on R8) NOT wired: emits C++ `%` (compile error)

- **Phenomenon (mingw build failure)**: a runtime double modulo probe
  `ten % four` transpiles to a C++ expression `locA % locB` with `double`
  operands -- `CodeGenerator.cs` `OpSymbol` maps `BinaryOp.Rem` to `"%"`,
  which is an INVALID operation for C++ doubles (only integer `%` is legal).
  `mingw32-g++` fails with "invalid operands to binary expression ('double'
  and 'double')".
- **Why numchain's `10.0 % 4.0` passes**: both operands are constants, so the
  C# compiler folds the expression at compile time and the `rem` opcode never
  reaches the emitter. Any RUNTIME double `%` (local/local) hits the bad `%`.
- **Disposition**: the e2e drops the runtime double-modulo probes entirely
  (a compile-time-constant `%` would exercise nothing). The IEEE remainder
  surface stays with `Math.IEEERemainder` (wired, mathext precedent).
- **Suggested fix (not applied)**: lower `BinaryOp.Rem` on `double`/`float`
  operands to `fmod(a, b)` (or a runtime icall) in the opcode emitter.

## D-DBL-2 -- Math.Round(2.675, 2) is 2.68 on net48 (differs from .NET Core)

- **Phenomenon**: `Math.Round(2.675, 2) == 2.68` is TRUE on the net48 oracle
  (and the ILC runtime reproduces it). .NET Core/5+ returns 2.67 for the same
  call (the double 2.675 is 2.6749999...). The ILC runtime's
  `icall_math_round_digits_d` matches the net48 (Framework) algorithm.
- **Disposition**: the oracle is net48, so the probe stays; maintainers must
  not "correct" it to 2.67.
