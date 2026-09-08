#!/usr/bin/env python3
import re, pathlib, sys

# Headers
def hdr_names(path):
    txt = pathlib.Path(path).read_text(encoding='utf-8', errors='ignore')
    # extract decls: lines with icall_ and ending with );
    names = re.findall(r'\b(icall_\w+)\s*\(', txt)
    # filter to declaration context (before ; ) - but findall already captures impl too, but headers only have decls so fine
    # dedup
    uniq = sorted(set(names))
    return uniq

def def_names(path):
    txt = pathlib.Path(path).read_text(encoding='utf-8', errors='ignore')
    names = re.findall(r'\b(icall_\w+)\s*\(', txt)
    uniq = sorted(set(names))
    return uniq

hdr_math = hdr_names(r"E:\il2cpp\src\runtime\include\internal\il2cpp_math.h")
hdr_ext = hdr_names(r"E:\il2cpp\src\runtime\include\internal\il2cpp_math_ext.h")
hdr_mathf = hdr_names(r"E:\il2cpp\src\runtime\include\internal\il2cpp_mathf.h")
def_math = def_names(r"E:\il2cpp\src\runtime\src\math.cpp")
def_ext = def_names(r"E:\il2cpp\src\runtime\src\mathext.cpp")
def_mathf = def_names(r"E:\il2cpp\src\runtime\src\mathf.cpp")

print("=== HDR ===")
print("math.h", len(hdr_math), hdr_math)
print("ext", len(hdr_ext), hdr_ext)
print("mathf", len(hdr_mathf), hdr_mathf)

print("\n=== DEF ===")
print("math.cpp", len(def_math), def_math)
print("mathext.cpp", len(def_ext), def_ext)
print("mathf.cpp", len(def_mathf), def_mathf)

# Manual map expansion
# MethodMap.Math.cs
math_cs_icalls = [
 "icall_math_abs_i","icall_math_abs_l","icall_math_abs_d",
 "icall_math_max_i","icall_math_max_l","icall_math_max_d",
 "icall_math_min_i","icall_math_min_l","icall_math_min_d",
 "icall_math_sign_i","icall_math_sign_l","icall_math_sign_d",
 "icall_math_sqrt_d","icall_math_pow_d","icall_math_floor_d","icall_math_ceiling_d",
 "icall_math_round_d","icall_math_round_digits_d","icall_math_round_mode_d","icall_math_round_digits_mode_d",
 "icall_math_truncate_d","icall_math_sin_d","icall_math_cos_d","icall_math_tan_d",
 "icall_math_asin_d","icall_math_acos_d","icall_math_atan_d","icall_math_atan2_d",
 "icall_math_sincos","icall_math_exp_d","icall_math_log_d","icall_math_log_base_d","icall_math_log10_d",
 "icall_math_cosh_d","icall_math_sinh_d","icall_math_tanh_d","icall_math_ieee_remainder_d",
 "icall_math_bigmul",
 "icall_double_is_nan","icall_double_is_positive_infinity","icall_double_is_negative_infinity","icall_double_is_infinity",
]

mathextra_icalls = [
 "icall_math_clamp_i","icall_math_clamp_l","icall_math_clamp_d",
 "icall_math_acosh_d","icall_math_asinh_d","icall_math_atanh_d","icall_math_cbrt_d","icall_math_log2_d","icall_math_fma_d",
 "icall_math_max_magnitude_d","icall_math_min_magnitude_d","icall_math_ilogb_d","icall_math_scaleb_d","icall_math_copy_sign_d",
 "icall_math_bit_decrement_d","icall_math_bit_increment_d","icall_math_reciprocal_estimate_d","icall_math_reciprocal_sqrt_estimate_d",
 "icall_math_divrem_i","icall_math_divrem_l",
 # Note: icall_math_bigmul already above, duplicated intentionally
 "icall_math_bigmul",
 # dormant but present: modf/sincos are LOUD not mapped, so NOT in map list
]

mathf_icalls = [
 "icall_mathf_sqrt_f","icall_mathf_abs_f","icall_mathf_sin_f","icall_mathf_cos_f","icall_mathf_tan_f",
 "icall_mathf_asin_f","icall_mathf_acos_f","icall_mathf_atan_f","icall_mathf_exp_f","icall_mathf_log_f","icall_mathf_log2_f","icall_mathf_log10_f",
 "icall_mathf_ceiling_f","icall_mathf_floor_f","icall_mathf_truncate_f","icall_mathf_cosh_f","icall_mathf_sinh_f","icall_mathf_tanh_f",
 "icall_mathf_acosh_f","icall_mathf_asinh_f","icall_mathf_atanh_f","icall_mathf_cbrt_f",
 "icall_mathf_atan2_f","icall_mathf_pow_f","icall_mathf_ieee_remainder_f","icall_mathf_log_base_f",
 "icall_mathf_max_f","icall_mathf_min_f","icall_mathf_max_magnitude_f","icall_mathf_min_magnitude_f",
 "icall_mathf_fma_f","icall_mathf_round_f","icall_mathf_round_digits_f","icall_mathf_scalb_f","icall_mathf_sign_i","icall_mathf_ilogb_i",
 "icall_mathf_sincos_f","icall_mathf_modf_f",
]

map_double = sorted(set(math_cs_icalls + mathextra_icalls))
map_float = sorted(set(mathf_icalls))

runtime_double_set = set(hdr_math + hdr_ext)
runtime_float_set = set(hdr_mathf)
# unified runtime
all_runtime = runtime_double_set | runtime_float_set
# defs unified
all_defs = set(def_math + def_ext + def_mathf)

print("\n=== COUNTS ===")
print(f"runtime headers total {len(all_runtime)}: double {len(runtime_double_set)} float {len(runtime_float_set)}")
print(f"defs total {len(all_defs)}")
print(f"map double {len(map_double)}")
print(f"map float {len(map_float)}")
print(f"map total {len(set(map_double+map_float))}")

# Check header vs def
hdr_not_def = sorted(all_runtime - all_defs)
def_not_hdr = sorted(all_defs - all_runtime)
print("\n=== HDR not DEF (gap header->impl) ===", hdr_not_def)
print("=== DEF not HDR (impl without header) ===", def_not_hdr)

# Check runtime vs map
# For double: which runtime icalls not in map?
double_not_map = sorted(runtime_double_set - set(map_double))
float_not_map = sorted(runtime_float_set - set(map_float))
print("\n=== Runtime HDR double not in MAP (GAP) ===", double_not_map)
print("=== Runtime HDR float not in MAP (GAP) ===", float_not_map)

# Check map vs runtime (err)
map_not_runtime_double = sorted(set(map_double) - runtime_double_set)
map_not_runtime_float = sorted(set(map_float) - runtime_float_set)
print("\n=== MAP double not in runtime HDR (ERR) ===", map_not_runtime_double)
print("=== MAP float not in runtime HDR (ERR) ===", map_not_runtime_float)

# Also check abandoned: headers that exist but not reachable due to MethodMap LOUD vs gap
# Need to classify double_not_map
# Among double_not_map, which are intentional non-mapped?
# icall_math_fmod_d: lowered via CodeGenerator BinaryOp.Rem, not MethodMap
# icall_math_max_d/min_d are mapped via stem but we included them, so they are in map_double; however hdr also has them, so they are NOT gaps now
# icall_math_div_u etc are internal helpers for DivModInt unsigned forms, not System.Math public API
# Let's list all hdrs and check

print("\n=== Detailed classification ===")
for n in sorted(runtime_double_set):
    in_map = n in set(map_double)
    in_def = n in all_defs
    print(f"{n:40} hdr->def:{in_def} hdr->map:{in_map}")

print("\n=== Float detailed ===")
for n in sorted(runtime_float_set):
    in_map = n in set(map_float)
    in_def = n in all_defs
    print(f"{n:40} hdr->def:{in_def} hdr->map:{in_map}")

# Also check for any mapping that references THROW instead of icall (LOUD gaps)
loud = ["ModF/SplitFractionDouble (Math.ModF)","SinCos ValueTuple (Math.SinCos tuple) extra already counted? BigMul wide forms"]
print("\nLOUD (NotSupported) in MethodMap.MathExtra: ModF, SplitFractionDouble, SinCos tuple, BigMul(long,long,out long) wide forms")

# Check header location anomaly: bigmul
print("\n=== BigMul location ===")
print("hdr_math contains bigmul:", "icall_math_bigmul" in hdr_math)
print("hdr_ext contains bigmul:", "icall_math_bigmul" in hdr_ext)
print("def_math contains bigmul:", "icall_math_bigmul" in def_math)
print("def_ext contains bigmul:", "icall_math_bigmul" in def_ext)

# Count gaps
gap_double = len(double_not_map)
gap_float = len(float_not_map)
err_double = len(map_not_runtime_double)
err_float = len(map_not_runtime_float)
print(f"\n=== SUMMARY gap/err ===")
print(f"double gap={gap_double} err={err_double} float gap={gap_float} err={err_float} total gap={gap_double+gap_float} err={err_double+err_float}")
