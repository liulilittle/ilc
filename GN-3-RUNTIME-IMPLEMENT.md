# GN-3-RUNTIME-IMPLEMENT — 多维数组 runtime 端(commit②)

目标:runtime 实现 `il2cpp_array_new_multi_{v|ref}{2|3}d` 分配 + `il2cpp_array_get/set_i4_2d/3d` 访问,使 p-multidimarg 探针 byte-exact(True/2/7),与转译端①(4414d93f)共同达成 GN-3。

## 方案(A 裁决:最小多维支持)

### 头布局(src/runtime/include/internal/il2cpp_array.h)
- `IL2CPP_MAX_ARRAY_RANK = 8` 编译期上限(显式字段,memory-safety 铁律:仅显式 struct 字段、无隐式尾块)。
- `Il2CppArray` 头新增 `int32_t dims[IL2CPP_MAX_ARRAY_RANK]` 显式字段(rank + 每维 length;lower bound 恒 0)。
- `length` 保持总元素数:单维 = 元素数;多维 = 每维 size 乘积。**GC 扫描(gc.cpp:619-631)经 `arr->data`/`arr->length`/`isRefElem` 字段访问**,不硬编码 `offsetof(data)`,故头加字段后单维路径与 GC 扫描行为零改动;`il2cpp_array_get_elem_ptr`(inline)对多维数组的 flat stelem(0..product-1)直接可用(InitializeArray 折叠已按 flat 发射)。
- 单维分配 `il2cpp_array_new` 补 `dims[0] = length`(rank-aware GetLength/GetUpperBound 对齐)。

### 分配(src/runtime/src/array.cpp)
- `il2cpp_array_new_multi(elemClass, elemSize, isRefElement, rank, dims)`:校验 rank(1..8,超出 abort)、dims>=0、乘积不溢出(int32);data 行主序平铺单连续区;写 rank/dims;length = product。GC 类仍走 `ArrayClassForElement`(ref)/`g_int32ArrayClass`(value)。
- rank 固定包装:`_v2d/_v3d/_ref2d/_ref3d`(转译端 CodeGenerator.cs:8435 发射的确切名字,`{v|ref}` 标签 = 元素后缀推导的 isRef,`<N>d` = rank)。

### 访问(src/runtime/src/array.cpp)
- `MultidimFlatOffset(arr, rank, idx)`:null → NullReferenceException(经 COLD `il2cpp_array_fail`);逐维 `idx[d]∈[0,dims[d])` 越界 → IndexOutOfRangeException(net48 对齐);flat = Σ idx[d] * (其后各维乘积) 行主序。
- `get/set_i4_2d/3d`:flat 偏移经 `arr->elemSize` 步长读写 int32。

### 秩感知 icall(src/runtime/src/arrayext.cpp)
- `RequireDimensionInRange(arr, dim)` 取代单维 `RequireDimension`:`0 <= dim < arr->rank` 校验。
- `icall_array_get_length/get_upper_bound/get_lower_bound` 改为读 `dims[dim]` / `dims[dim]-1` / 0(单维 dims[0]==length 保持旧行为,既有测试 167-191 全绿)。

## TDD 红→绿

### 红(实现前,runtime 无 multidim 符号)
转译端①已发射 `il2cpp::il2cpp_array_new_multi_v2d/v3d`、`get/set_i4_2d/3d`;runtime 无对应符号 → 探针链接失败(undefined reference)。

### 绿(实现后)
```
il2cpp_gen_exe.exe 运行:
  exit=0
  True
  2
  7
expected.txt(经 net48 oracle 定稿):
  True
  2
  7
  → byte-exact PASS
```

## C0 与回归
- `dotnet build src/il2cppc -c Debug`:0 警告 0 错误。
- runtime 全量 cmake(MSVC/ninja Release)构建:0 错误;新增代码 0 警告(仅既有 timeext.cpp C4996/test_arraysort C4100 非新域)。
- runtime 测试套件 185/185 通过(test_arrayext/test_arraysort/test_arraybounds/test_array/test_array_find/test_array_extra/test_arraylist 等全绿)。
- dotnet unit 2395/2395 通过。

## 影响范围
- src/runtime/include/internal/il2cpp_array.h(头:dims 字段 + 4 声明)
- src/runtime/src/array.cpp(分配 + 访问 + flat 偏移)
- src/runtime/src/arrayext.cpp(GetLength/GetUpperBound/GetLowerBound 秩感知)
- 单维路径/GC 扫描零改动;引用类型数组(e.g. gen-arrayarg 路径)未触碰。

## 证据链
- 探针 byte-exact:[BASH: gn3-multidim9/build_rt2/il2cpp_gen_exe.exe → True/2/7, exit 0]
- C0 转译端:[BASH: dotnet build src/il2cppc -c Debug → 0 警 0 错]
- C0 runtime:[BASH: cmake --build build_rt → 0 错;array*.exe 185/185 exit 0]
