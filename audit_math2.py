import re, pathlib
def hdr_names_strict(path):
    txt = pathlib.Path(path).read_text(encoding='utf-8', errors='ignore')
    # only lines that start with type decl
    names=[]
    for line in txt.splitlines():
        m = re.match(r'^\s*(int32_t|int64_t|uint32_t|uint64_t|double|float|void)\s+(icall_\w+)\s*\(', line)
        if m:
            names.append(m.group(2))
    return sorted(set(names))

def def_names_strict(path):
    txt = pathlib.Path(path).read_text(encoding='utf-8', errors='ignore')
    names=[]
    for line in txt.splitlines():
        m = re.match(r'^\s*(int32_t|int64_t|uint32_t|uint64_t|double|float|void)\s+(icall_\w+)\s*\(', line)
        if m:
            names.append(m.group(2))
    return sorted(set(names))

hdr_math = hdr_names_strict(r"E:\il2cpp\src\runtime\include\internal\il2cpp_math.h")
hdr_ext = hdr_names_strict(r"E:\il2cpp\src\runtime\include\internal\il2cpp_math_ext.h")
hdr_mathf = hdr_names_strict(r"E:\il2cpp\src\runtime\include\internal\il2cpp_mathf.h")
def_math = def_names_strict(r"E:\il2cpp\src\runtime\src\math.cpp")
def_ext = def_names_strict(r"E:\il2cpp\src\runtime\src\mathext.cpp")
def_mathf = def_names_strict(r"E:\il2cpp\src\runtime\src\mathf.cpp")

print("HDR strict:")
print("math.h", len(hdr_math), hdr_math)
print("ext", len(hdr_ext), hdr_ext)
print("mathf", len(hdr_mathf), hdr_mathf)
print("\nDEF strict:")
print("math.cpp", len(def_math), def_math)
print("mathext.cpp", len(def_ext), def_ext)
print("mathf.cpp", len(def_mathf), def_mathf)

runtime_double = set(hdr_math + hdr_ext)
runtime_float = set(hdr_mathf)
all_runtime = runtime_double | runtime_float
all_defs = set(def_math + def_ext + def_mathf)
print("\nRuntime double", len(runtime_double), "float", len(runtime_float), "total", len(all_runtime))
print("Defs total", len(all_defs))
print("HDR not DEF", sorted(all_runtime - all_defs))
print("DEF not HDR", sorted(all_defs - all_runtime))

# Maps
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
 "icall_math_bigmul",
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

print("\nMAP double", len(map_double), map_double)
print("MAP float", len(map_float), map_float)

# Gaps
print("\n=== GAPS ===")
print("double runtime not in map", sorted(set(runtime_double) - set(map_double)))
print("float runtime not in map", sorted(set(runtime_float) - set(map_float)))
print("map double not in runtime", sorted(set(map_double) - set(runtime_double)))
print("map float not in runtime", sorted(set(map_float) - set(runtime_float)))

# Classify gaps
gaps = sorted(set(runtime_double) - set(map_double))
print("\nClassification of double gaps:")
for g in gaps:
    # check where it comes from
    src = []
    if g in hdr_math: src.append("math.h")
    if g in hdr_ext: src.append("math_ext.h")
    print(f"  {g} in {src}")

# Check CodeGenerator for fmod
import pathlib as pl
cg = pl.Path(r"E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs").read_text(encoding='utf-8', errors='ignore')
print("\nCodeGenerator fmod/math references:")
for line in cg.splitlines():
    if "icall_math" in line or "fmod" in line.lower():
        print(line.strip())

# Check div helpers usage
print("\n--- div/mod helpers search in CodeGen ---")
found=False
for line in cg.splitlines():
    if "icall_math_div" in line or "icall_math_mod" in line:
        print(line.strip()); found=True
if not found:
    print("No div/mod mapping in CodeGenerator.cs")

# Check header include anomaly
print("\n=== Header/Impl placement ===")
print("bigmul hdr_math?", "icall_math_bigmul" in hdr_math, "hdr_ext?", "icall_math_bigmul" in hdr_ext)
print("bigmul def_math?", "icall_math_bigmul" in def_math, "def_ext?", "icall_math_bigmul" in def_ext)
print("round_d thrice? hdr_math?", "icall_math_round_d" in hdr_math, "def_math?", "icall_math_round_d" in def_math, "def_ext?", "icall_math_round_d" in def_ext)
# round_d appears in math.cpp and also forward-declared in math_ext.h inclusion? Actually math_ext includes math.h
print("math_ext.h includes math.h?", "il2cpp_math.h" in pathlib.Path(r"E:\il2cpp\src\runtime\src\mathext.cpp").read_text(encoding='utf-8', errors='ignore'))
print("math.h declares round_d", "icall_math_round_d" in hdr_math)
print("mathext.cpp defines round_digits etc - does it include math.h?", pathlib.Path(r"E:\il2cpp\src\runtime\src\mathext.cpp").read_text(encoding='utf-8', errors='ignore')[:500])
