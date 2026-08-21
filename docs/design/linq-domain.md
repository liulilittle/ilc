# System.Linq 域设计（LINQ AOT 转译形态）

> 状态：阶段 1 设计定稿 | 目标对齐：.NET Framework 4.8 | 为后续实现铺路
> 本域不做迭代器状态机、不做延迟执行；用"委托回调 + 数组特化"的 icall 形态落地（见 §1.3）。
> 全部结论基于对当前代码库的实测（文件:行号证据），非假设。

---

## 1. AOT 形态分析

### 1.1 背景：LINQ 在 AOT 下的三个特殊性

1. **泛型静态方法**：`Enumerable.Where<T>(IEnumerable<T>, Func<T,bool>)` 是泛型方法。AOT 下没有 JIT 泛型实例化，必须按"具体类型实例"落成具体代码——即 IL 层面的泛型实参（`genArgs[0]` = 元素类型）决定生成哪份调用。
2. **委托回调**：`Func<T,bool>` 谓词。本项目委托已实现：`Il2CppDelegate { obj, methodPtr, target }`（[READ:src/runtime/include/internal/il2cpp_delegate.h:31-36]），转译器对 `callvirt Delegate.Invoke` 直接发射通过 `methodPtr` 的函数指针调用（[READ:src/il2cppc/Emit/CodeGenerator.cs:1035]：`((RetType(*)(...))d->methodPtr)(args)`）。即：**委托调用在生成代码里就是一条间接 C 调用**，无虚表、无接口调度、无装箱。
3. **迭代器**：.NET LINQ 靠 IEnumerable<T> + 状态机枚举器。AOT 下若走接口调度（方案 1），需要给数组/List 实现 IEnumerable<T> 接口并引入枚举器对象族——本 runtime 目前**没有任何 IEnumerable/IEnumerator 实现**（接口调度机制 `il2cpp_interface_dispatch` 存在，[READ:CodeGenerator.cs:1139]，但没有枚举器类型可用）。

调用链 `Where(pred).Select(sel).ToList()` 的落法在 §1.3 定稿。

### 1.2 三个方案对比

| 维度 | 方案 1：runtime 通用对象迭代 | 方案 2：转译器展开专用函数 | 方案 3：委托回调 icall（推荐） |
|---|---|---|---|
| 机制 | 数组实现 IEnumerable<T>，运行时经 `il2cpp_interface_dispatch` 逐元素调度枚举器 | 转译器在**每个调用点**内联生成专用循环（谓词直接内联进生成代码） | 转译器把 LINQ 静态调用解析为 runtime icall：源数组 + 委托对象作参数；runtime 按元素类型特化入口，经 `methodPtr` 直调回调 |
| 每元素成本 | 接口虚调度 + 枚举器 MoveNext 调用 + 可能的装箱 | 无（谓词可内联） | 一次间接函数指针调用（无虚表/无接口调度/无装箱） |
| 生成量 | 低（一个通用实现） | **高**：每个调用点 × 类型组合一份代码，链式还要折叠分析；代码膨胀 + 编译时间不可控 | 中：每（操作 × 元素类型）一个 icall 入口；转译器只做签名映射，调用点零展开 |
| 实现量 | 高：枚举器类型族 + 数组的 IEnumerable<T> 接口实现 + 装箱语义 | 高：transpiler 内做数据流分析 | 低：纯 C++ 循环 + 既有委托直调机制 |
| GC 安全 | 枚举器对象需做 GC 根 | 生成代码管 | 委托对象作为 icall 参数被调用栈持有 → 根安全；结果数组由 resolver 的 ReturnsRef 跟踪（先例 [READ:MethodRefResolver.cs:875] GetInvocationList） |
| 性能 | 差（每元素 2 次间接跳转 + 状态机） | 最好 | 好（每元素 1 次间接调用，接近方案 2） |

**结论：方案 3 + 数组特化**。理由：
- **正确性路径最短**：方案 3 复用已实证的委托直调（CodeGenerator.cs:1035），无需新增枚举器类型族（方案 1 的巨额实现量），也无需转译器数据流展开（方案 2 的生成量风险）。
- **性能达标**：每元素成本 = 一次间接调用；谓词函数体是 AOT 生成的静态函数，C++ 编译器可跨函数看（LTO 前也因 icall 边界收不到），但对比方案 1 的接口双跳已足够，且与"性能 >= 官方 JIT"路线不冲突（JIT 的 LINQ 也是委托 invoke 走虚拟分派，我们是裸函数指针，反而更快）。
- **AOT 泛型实例化天然契合**：泛型实参在转译期已知 → 元素类型映射为固定签名入口（§4 类型表）→ 这就是"AOT 泛型实例化 = 具体类型调用"的落点。
- **方案 2 留作阶段 2 优化**：热链 `Where().Select()` 连续物化可做转译器折叠（生成数组→数组直通），作为性能路线图条目，不阻塞阶段 1 落地。

### 1.3 方案 3 的落法细节

**特化维度 = 元素类型族**：`i4 / i8 / d / b / ref`（映射表见 §4.2；u4→i4、f4→d 归并，符合既有 cast code 约定 [READ:memory.md:13 的 "i"/"l"/"d"/"c"/"b" 体系]）。

**⚠ 阶段 1 源数组可达族只有 i4 与 ref**：生成代码的 `newarr` 只接受 `System.Int32 → (4, false)` 与引用类型 → (8, true)，其余（byte/double/int64/char/用户 struct）全部 loud 拒绝（[READ:CodeGenerator.cs:1873-1880]，audit C4）。即**用户代码能构造的数组只有 int32[] 与 ref 元素数组**（string[]/object[]/用户类[]）；i8/d/b 族入口保留为"运行时自建结果数组"与未来 newarr 扩展的出口（§4.2 表注）。

**源特化 = 数组**（阶段 1 只接受 `Il2CppArray*`）：
- 数组自描述（`length / elemSize / isRefElem / data[]`，[READ:il2cpp_array.h:42-49]）→ 无回调操作（count/to_array）可单入口类型无关实现；
- 有回调操作按元素类型族分入口，循环体 `il2cpp_array_get_elem_ptr` 边界检查 + 步长寻址（[READ:il2cpp_array.h:75-82]），回调 `((Sig)del->methodPtr)(elem)`；
- **结果数组由 runtime 自建**（`il2cpp_array_new`），不受 newarr 限制：如 `select_i4_ref` 产出 ref 数组、`where_i4` 产出 int32[]——源受限、结果不限。

**物化语义（阶段 1 设计决策，net48 偏差声明）**：
- `Where`/`Select`/`OrderBy` 返回**物化数组**（急切执行），`Where(p).Select(s)` 链 = 数组→数组的 icall 序列，无中间 IEnumerable 分配。
- 可观测差异仅出现在：谓词/选择器有副作用、或链式调用之间源集合被修改——阶段 1 明确接受并记录（与 .NET 延迟语义不同）。延迟状态机与 Join/GroupBy 一并留阶段 2。

**空序列语义（net48 基准，全操作适用）**：
| 操作 | 空序列结果 |
|---|---|
| Count / ToArray / Sum(i4) / Any() | 0 / 空数组 / 0 / false |
| Any(pred) / All(pred) | false / true（空集全称量化） |
| First / Min / Max | InvalidOperationException |
| FirstOrDefault | default(T)：i4/i8/d → 0，ref → nullptr |

**异常契约**：null 源 / null 回调 → `il2cpp_throw_argument_null_exception`（[READ:il2cpp_exception_ext.h:51]）；空序列 First/Min/Max → `il2cpp_throw_invalid_operation_exception`（[READ:il2cpp_exception_ext.h:60]）；不支持的组合（非数组源、valuetype 元素、捕获 lambda 回调）→ 转译期 `NotSupportedException`（loud，模式同 [READ:MethodRefResolver.cs:829]）。

---

## 2. 委托支撑现状（衔接）

**已实现（M3 Task 8）**：
- 结构：`Il2CppDelegate { Il2CppObject obj; void* methodPtr; Il2CppObject* target; }`（[READ:il2cpp_delegate.h:31-36]）。
- 创建：`il2cpp_delegate_new(void* methodPtr)`（[READ:il2cpp_delegate.h:40]）；转译器发射 `il2cpp_delegate_new((void*)(&<方法>))`（[READ:CodeGenerator.cs:1024]，ldftn 取生成的静态函数地址）。
- 调用：`DelegateInvokeExpr` → `((RetType(*)(...))d->methodPtr)(args)` + null 检查（[READ:CodeGenerator.cs:1026-1035]）。
- 泛型委托实例化 cast 是 identity（[READ:CodeGenerator.cs:1082,1692-1698]）——`Func<int,bool>` 实例就是同一对象，无包装。

**硬限制（必须写进 LINQ 契约）**：
1. **仅单播静态方法委托**：instance-bound（`target != null`）委托是 M3.5+（[READ:il2cpp_delegate.h:19-21]）；`DelegateInvokeExpr` 发射时**不传 target**（CodeGenerator.cs:1028-1035 只传显式参数）——带 target 的委托调用会丢掉 `this`。
2. **multicast invoke 仅 i4×4 ABI**：`icall_multicast_invoke_i4` 的叶子签名写死 `int32_t(*)(int32_t,int32_t,int32_t,int32_t)`（[READ:il2cpp_delegate_ext.h:121]）——LINQ 回调不能用 multicast。
3. **回调提取先例**：`icall_delegate_get_fn_ptr` 对 null → NRE、multicast/instance-bound → InvalidOperationException 的 loud 拒绝（[READ:il2cpp_delegate_ext.h:128-138]）——LINQ 回调校验沿用同一策略。

**LINQ 回调契约（本域定义）**：
- 回调以**委托对象**（`Il2CppObject*`）传入 icall，运行时读 `methodPtr` 并 cast 为固定签名调用。传委托对象而非裸指针的好处：调用栈持有 → GC 根安全；未来 instance-bound 委托落地后无需改 ABI。
- **捕获 lambda 限制**：C# 捕获 lambda 编译为闭包类实例方法 → `target != null` → 阶段 1 **不支持**（runtime 侧 loud 拒绝，同 icall_delegate_get_fn_ptr 策略）。非捕获 lambda / 静态方法委托 → 支持。
- 签名 cast 由 runtime 侧做（每入口固定），与转译器 `DelegateSignature` 的 per-签名 cast（CodeGenerator.cs:1031）不冲突。

---

## 3. API 裁剪清单

### 阶段 1（本设计 3 任务卡覆盖）
| 成员 | 任务卡 | 入口族 | 备注 |
|---|---|---|---|
| Count | T1 | `il2cpp_linq_count`（类型无关） | 空 → 0 |
| Any / Any(pred) / All(pred) | T1 | `_any`（无关）+ `_any_<t>` / `_all_<t>` | 空：Any=false, All=true |
| Sum | T1 | `_sum_i4`（阶段 1 实测族） | 空 → 0；溢出 unchecked（net48 默认） |
| Min / Max | T1 | `_min_i4/_max_i4` | 空 → InvalidOperationException；ref 版需比较器 → 阶段 2 |
| Where | T2 | `_where_i4/_where_ref` | 谓词 `bool(*)(T)`；返回物化数组 |
| Select | T2 | `_select_<in>_<out>` | 选择器 `Out(*)(T)`；**二维族**（可跨类型，如 int→string）；源侧限 i4/ref |
| First / FirstOrDefault | T2 | `_first_i4/_first_ref` 等 | 空：First → IOE；FirstOrDefault → default |
| ToArray | T2 | `_to_array`（类型无关克隆） | 元素自描述 → 单入口 |
| ToList | T2（依赖） | `_to_list` | **依赖 List\<T\> runtime 域（当前不存在）**，见下 |
| OrderBy（比较器版） | T3 | `_orderby_i4/_orderby_ref` | 比较器 `int32_t(*)(T,T)`；**稳定排序**（net48 OrderBy 稳定） |

> 注：`<t>` 族按 §4.2 表——**阶段 1 实测仅 i4 与 ref**（newarr 只放行 int32[] 与引用类型[]，[READ:CodeGenerator.cs:1873-1880]）；i8/d/b 入口保留，随 newarr 扩展启用。

**ToList 依赖项（任务卡前置）**：`List<T>` runtime 域不存在（grep `List`1` 于 src/il2cppc/Emit 零命中）。参考 `Il2CppStack/Il2CppQueue` 的"托管对象 + 数组倍增 + count"模式（[READ:stackqueue.h:12-24,43-53]：倍增 min 4、满则加倍）建最小 List<T> 域；`il2cpp_linq_to_list` 在其上实现。若 List<T> 域未排期，ToList 降级为 NotSupported（loud）。

### 阶段 2（明确不做，仅 Join/GroupBy 为硬目标，其余顺延）
Join、GroupBy（硬目标，需哈希 + 分组物化）；顺延：Distinct、Union、Except、Intersect、SelectMany、Aggregate、Cast、OfType、Take、Skip、Reverse、Last、LastOrDefault、ElementAt、SequenceEqual、Contains、ToDictionary、OrderByDescending、ThenBy、Single、ref 类型 Min/Max、延迟 IEnumerable 语义。

---

## 4. C API 草案

### 4.1 约定
- 函数放 `namespace il2cpp`（与 `il2cpp_stack_*` / `il2cpp_array_*` 同域约定，[READ:stackqueue.h:61-64]）；MethodRefResolver 引用为 `il2cpp::il2cpp_linq_*`。
- 命名：`il2cpp_linq_<op>[_<in>[_<out>]]`；回调一律 `Il2CppObject*`（委托对象），运行时经 methodPtr 直调。
- 回调签名：谓词 `bool(*)(T)`；选择器 `Out(*)(T)`；比较器 `int32_t(*)(T,T)`（负/零/正，net48 Comparer 约定）。

### 4.2 元素类型 → ABI 映射（原始签名字节 → 族，依据 [READ:memory.md:11]）
| 族 | raw byte | 回调元素 C 类型 | 存储 | 阶段 1 源可达 |
|---|---|---|---|---|
| i4 | 0x08(i4) 0x09(u4) | int32_t | elemSize=4 | ✅（newarr 支持，[READ:CodeGenerator.cs:1873]） |
| ref | 0x0E(string) 0x1C(object) 0x12(class) 0x1D(array) | Il2CppObject* | elemSize=sizeof(void*), isRefElem=true | ✅（newarr 支持，[READ:CodeGenerator.cs:1874]） |
| i8 | 0x0A(i8) 0x0B(u8) | int64_t | elemSize=8 | ⚠ 需 newarr 扩展（当前 loud 拒绝） |
| d | 0x0C(f4) 0x0D(f8) | double | elemSize=8（f4 由转译器升宽） | ⚠ 需 newarr 扩展 |
| b | 0x02 | bool | elemSize=1 | ⚠ 需 newarr 扩展 |
| （拒绝） | 0x03(char) 0x11(valuetype) 等 | — | — | 转译期 NotSupported |

> 表注：**阶段 1 只实现并测试 i4 / ref 两族的入口**（源数组只能由 newarr 产生，而 newarr 只放行 int32 与引用类型）。i8/d/b 族签名**保留在 API 草案中**（runtime 内部无自描述障碍：elemSize/isRefElem 是数组字段），作为 newarr 扩展（含 char→i4 归并）的前置配套，届时补入口与测试即可。

### 4.3 函数签名（T1 基础族）
```cpp
int32_t       il2cpp_linq_count(Il2CppArray* src);                        // 类型无关；空 → 0
bool          il2cpp_linq_any(Il2CppArray* src);                          // 类型无关；空 → false
bool          il2cpp_linq_any_i4(Il2CppArray* src, Il2CppObject* pred);   // + _ref；空 → false
bool          il2cpp_linq_all_i4(Il2CppArray* src, Il2CppObject* pred);   // + _ref；空 → true
int32_t       il2cpp_linq_sum_i4(Il2CppArray* src);                       // 空 → 0（net48：int32 求和返回 int32）
int32_t       il2cpp_linq_min_i4(Il2CppArray* src);                       // + _ref？不：ref Min/Max 需比较器 → 阶段 2
int32_t       il2cpp_linq_max_i4(Il2CppArray* src);                       // 空 → IOE
```
> 族扩展：i8/d/b 入口（`_sum_i8(int64_t)/_sum_d(double)` 等）随 newarr 扩展启用（§4.2 表注）。
### 4.4 函数签名（T2 过滤映射）
```cpp
Il2CppArray*  il2cpp_linq_where_i4(Il2CppArray* src, Il2CppObject* pred); // + _ref；谓词 bool(*)(T)
Il2CppArray*  il2cpp_linq_select_i4_i4(Il2CppArray* src, Il2CppObject* sel);
Il2CppArray*  il2cpp_linq_select_i4_ref(Il2CppArray* src, Il2CppObject* sel);  // int→string 等跨类型
Il2CppArray*  il2cpp_linq_select_ref_ref(Il2CppArray* src, Il2CppObject* sel);
int32_t       il2cpp_linq_first_i4(Il2CppArray* src);                     // + _ref；空 → IOE
int32_t       il2cpp_linq_first_or_default_i4(Il2CppArray* src);          // 空 → 0
Il2CppObject* il2cpp_linq_first_or_default_ref(Il2CppArray* src);         // 空 → nullptr
Il2CppArray*  il2cpp_linq_to_array(Il2CppArray* src);                     // 克隆（元素自描述，单入口）
Il2CppObject* il2cpp_linq_to_list(Il2CppArray* src);                      // 依赖 List<T> 域（§3）
```
> 注：select 是二维族 `_<in>_<out>`——源侧限于 i4/ref（阶段 1），出侧不限（runtime 自建结果数组）。
### 4.5 函数签名（T3 排序）
```cpp
Il2CppArray*  il2cpp_linq_orderby_i4(Il2CppArray* src, Il2CppObject* cmp); // + _ref；i8/d/b 随 newarr 扩展
// 稳定排序；返回排序后的新数组（不改源）；比较器 int32_t(*)(T,T)
```

**实现要点**：T3 稳定排序可用归并/插入混合（小数组插入，n>32 归并）；先例 `icall_array_sort`（int32 升序原地，[READ:arrayext.cpp:234-236]）和 `icall_array_sort_strings`（序数，[READ:arrayext2.cpp:263-266]）只覆盖无比较器场景，OrderBy 是新的比较器 + 稳定 + 拷贝语义，不共享实现。

---

## 5. 任务卡（3 张）

通用：runtime 文件用 CMake GLOB 自动发现（src/*.cpp、tests/runtime/test_*.cpp 均无需改 CMakeLists，[READ:process.md:5-7 历史确认]）；测试用 Expect + g_failures + 非零退出码约定（test_math.cpp 风格）；头文件带 doc 块（域约定）。

### T1 数组基础族（count / any / all / sum / min / max）
- **文件域**：`src/runtime/include/internal/linq.h`（+ 可选 `linq_basic.h` 拆分）、`src/runtime/src/linq.cpp`、`tests/runtime/test_linq.cpp`
- **测试要点**：
  - 空/单/多元素数组的 count/any/all/sum/min/max 全组合；空集真值表（Any=false、All=true）
  - sum 溢出 unchecked（int32_max + 1 → 负）；负值求和
  - min/max 空 → InvalidOperationException（throw 路径断言）
  - null 源 → ArgumentNullException；null 谓词 → ArgumentNullException
  - any/all 谓词短路（副作用计数器断言只调用到首次假/真）
  - 回调经真实 `il2cpp_delegate_new` 包装的静态函数指针（非裸传）——契约验证
  - 数值边界：int32 极值（INT32_MIN/MAX）；ref 族（string[]）any/all 谓词走 `_ref` 入口
  - i8/d/b 族入口暂不实现（newarr 未扩展，§4.2）——不写空壳，避免死代码

### T2 过滤映射（where / select / first / firstordefault / toarray / tolist*）
- **文件域**：`src/runtime/include/internal/linq_map.h`、`src/runtime/src/linq_map.cpp`、`tests/runtime/test_linq_map.cpp`
- **测试要点**：
  - where：全真/全假/空数组 → 结果长度正确；谓词 null → ArgumentNullException
  - select：同类型 + 跨类型（i4→ref：int→string 经既有 string 域）+ ref→ref；结果 isRefElem/elemSize 正确
  - 链式：`where(pred).select(sel)` 数组→数组物化，断言中间无枚举器（结果即数组）
  - first：命中/空 → IOE；firstordefault：空 → 0/null 且不抛
  - toarray：克隆与原数组等长同值；**修改结果不影响源**（深拷贝断言）
  - tolist：依赖 List<T> 域；若域未排期则该入口 NotSupported（转译期 loud）
  - ref 元素数组（string[]）上 where/first 的 GC 存活（gcstress 可选）

### T3 排序（OrderBy 比较器）
- **文件域**：`src/runtime/include/internal/linq_sort.h`、`src/runtime/src/linq_sort.cpp`、`tests/runtime/test_linq_sort.cpp`
- **测试要点**：
  - **稳定性**：带序号键的对构造（键相等项顺序不变）——net48 OrderBy 稳定，必须断言
  - 比较器方向：正向/逆向比较器（negate 语义）；比较器返回值仅看符号（±1/±100 等价）
  - 空/单元素/已排序/逆序/重复键数组
  - 源数组不被修改（排序新数组）；null 比较器 → ArgumentNullException
  - ref 元素（string[]）序数比较器排序；null 元素传给比较器（net48 允许，比较器自定序）

---

## 6. 转译器接线注意（现状 + 改动点）

**现状（grep 实证）**：`System.Linq` 在 src/il2cppc/Emit 零接线（grep `System\.Linq|Enumerable` 仅命中转译器自身 C# 的 using）。`BclResolveCore` 按 owner 全名 switch（[READ:MethodRefResolver.cs:216-258]），无 Enumerable 分支——LINQ 是绿地。

**改动点（阶段 1 接线）**：
1. `BclResolveCore` 增 `case "System.Linq.Enumerable": return LinqDispatch(name, ps, genArgs, arg0Type);`——`genArgs` 已解析（泛型实参字节数组），`arg0Type` 已有栈值类型跟踪（[READ:MethodRefResolver.cs:82-93]），签名解析走既有 RawSignature。
2. **元素类型派发**：`genArgs[0]` → §4.2 表 → 具体入口（`il2cpp::il2cpp_linq_where_i4` 等）。这就是"AOT 泛型实例化 → 具体类型调用"的转译器落点。
3. **源类型校验**：`arg0Type` 必须以 `[]` 结尾（数组源）——非数组（List<T> 等）→ `NotSupportedException`（loud，模式同 Directory.Delete [READ:MethodRefResolver.cs:829]）。List<T> 源随 List<T> 域落地。
4. **结果跟踪**：数组结果 icall 必须 `ReturnsRef=true`（`I(..., true, ...)`，先例 GetInvocationList [READ:MethodRefResolver.cs:875]）——否则生成代码把 `Il2CppArray*` 当 i4 处理。
5. **回调参数直传**：委托是栈值，作为普通参数传入（先例 delegate_combine 传对象参数 [READ:MethodRefResolver.cs:871]）；运行时自行校验（null → ANE；`target != null` → IOE，§2 契约）。
6. **委托 Invoke 现状引用**：`callvirt Func<T,bool>.Invoke` 已被 StackFold/CodeGenerator 折叠为 methodPtr 直调（[READ:CodeGenerator.cs:1026-1035]）——**用户代码里手动 Invoke 委托的场景已可用**；LINQ 只是把同一形态搬进 runtime。捕获 lambda（target != null）在生成代码层面同样未支持（M3.5+），阶段 1 不展开。
7. **可选阶段 2 优化**：`where().select()` 连续物化链在 StackFold 做折叠（数组→数组直通，少一次中间数组分配）；本期不做，避免改动 StackFold（该文件有并行代理在途，[READ:discover.md:6]）。

---

## 附：设计决策摘要
1. 方案 3（委托回调 icall）+ 数组特化，方案 2 留阶段 2 折叠优化
2. 阶段 1 急切物化（Where/Select/OrderBy 返回数组），延迟语义随阶段 2
3. 回调 ABI = 委托对象（Il2CppObject*），运行时 methodPtr 直调；拒绝捕获 lambda（instance-bound）
4. 元素类型族 i4/i8/d/b/ref，u4→i4、f4→d 归并；**阶段 1 实测仅 i4/ref（newarr 只放行 int32[] 与引用类型[]），i8/d/b 随 newarr 扩展启用**
5. ToList 依赖 List<T> 域（stackqueue.h 倍增模式），未排期则 loud NotSupported
