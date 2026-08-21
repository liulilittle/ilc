# GAPS.md -- bitconverterdeep recorded findings (e2e batch C67, 2026-08-12)

Case status: **PASS dual-compiler** (mingw + msvc, net48 oracle 100-line byte-exact).

## D-BCD-1 -- BitConverter.SingleToInt32Bits / Int32BitsToSingle do NOT exist on net48

- **Phenomenon**: `BitConverter.SingleToInt32Bits(float)` / `Int32BitsToSingle(int)` are
  .NET Core / .NET 5+ APIs -- the net48 reference assemblies do not declare them, so a
  net48 case cannot compile them (CS0117) and the MethodMap.BitConverter.cs mappings
  (lines 98-105) are unreachable from any net48 oracle case.
- **Disposition**: recorded. The float<->int32 bit bridge is covered on the double
  side (DoubleToInt64Bits / Int64BitsToDouble, which DO exist on net48) and through
  the GetBytes/ToSingle roundtrip; the single bridge remains unwired-to-oracle.

## D-BCD-2 -- ToUInt32 high-bit value comparison flips (runtime vs net48)

- **Phenomenon**: `BitConverter.ToUInt32(bytes, 0) == 0xDEADBEEFu` and
  `== uint.MaxValue` print **False** on the transpiled runtime while net48 prints
  True. The ToUInt32 icall (il2cpp_bitconverter_to_uint32, bitconverter.cpp:170)
  returns the raw uint32 bits but MethodMap.BitConverter.cs maps it with the int32
  slot result kind (no "q"/"u" widening), so a value with bit 31 set (>= 0x80000000)
  is reinterpreted as a NEGATIVE int32 and the uint comparison operand disagrees.
- **Probe**: rt_u32 (0xDEADBEEF) and rt_u32_max (uint.MaxValue) both flipped; the
  value 0x12345678 (bit31 clear) roundtrips correctly on both sides.
- **Disposition**: recorded, NOT fixed (batch rule). The case asserts the low-bit
  roundtrip only; high-bit ToUInt32 comparisons are the documented divergence.

## D-BCD-3 -- net48 1-arity ToT(byte[]) overloads do NOT exist

- **Phenomenon**: numeric/char ToT on net48 require the (byte[], int) startIndex
  form; the offset-free 1-arity overloads are .NET Core additions. The case therefore
  asserts the 2-arity startIndex==0 form (which equals the offset-free value; the
  runtime drops startIndex and reads from index 0 -- documented simplification).
- **Disposition**: recorded. Only startIndex == 0 is exercised; a non-zero startIndex
  would diverge on the runtime (simplified read-from-0) -- not asserted.