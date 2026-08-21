# 转译产物（生成 C++）质量审计报告

- 审计日期：2026-08-03
- 审计性质：只读审计（未修改任何源代码、未 git commit）
- 审计域：转译产物（transpiled output）—— 生成 C++ 代码的质量
- 审计对象：`E:\il2cpp\build\audit_gen_<case>\` 下 11 个用例的生成产物
  （string / arrays / objects / virtual / interface / boxing / generics /
   delegates / exceptions / statics / loop）
- 转译命令：`il2cppc.exe <case>\out\<case>.exe <outdir>`（net48 用例产物为 .exe，
  PEReader 不区分扩展名），全部 EXIT=0
- 审计基线：docs/REQUIREMENTS.md 铁律 2e（生成代码性能 >= 官方 JIT）、2j（属性折叠）、
  风格规范 §3（注释）
- 辅助验证：g++ (clang 17, MinGW) -O2 反汇编级验证（-S），构建 Release（-O3，对齐
  scripts/run-tests.ps1:58/65 的测试构建）

---

## 1. 审计方法

1. 转译 11 个代表性用例到独立目录 `build/audit_gen_<case>`，逐一通读全部生成 .cpp/.h。
2. 对热循环用例（loop / arrays）用 `g++ -O2 -S` 出汇编，检查生成代码在 -O3 测试构建
   （run-tests.ps1 用 Release）下是否有明显低效。
3. 审查运行时被生成代码调用的辅助函数（array/null_check/interface_dispatch 等）的
   内联性，判断每次操作的真实调用开销。
4. 评估：表达式质量、函数调用开销（含铁律 2j 属性折叠实现状态）、常量化、
   内存安全（GC 根 wrap/unwrap、悬垂风险）、代码体积与注释质量。

---

## 2. 用例评分总表

| 用例 | 生成代码评分 | 一句话结论 | 关键证据 |
|---|---|---|---|
| string | 4 / 5 | 折叠干净、注释齐全；字符串字面量每次调用重新分配 | 见 §3.1 |
| loop | 4 / 5 | 算术 IR 极干净，-O3 整循环折叠为常量 | 见 §3.2 |
| arrays | 3 / 5 | 结构正确但每次元素访问都是函数调用，热循环无法提升/向量化 | 见 §3.3 |
| objects | 3 / 5 | 引用链表达式内 null_check 重复 3 次（CSE 缺失），17 处 null_check | 见 §3.4 |
| virtual | 4 / 5 | vtable 分派正确；无按 newobj 具体类型去虚化 | 见 §3.5 |
| interface | 4 / 5 | 接口槽映射表正确；每次调用线性搜索 + 两次函数调用 | 见 §3.6 |
| boxing | 3 / 5 | join 点冗余 temp；`(nullptr == nullptr)`、`is_instance(nullptr,…)` 未折叠 | 见 §3.7 |
| generics | 4 / 5 | 泛型实例化直接调用（非 vtable）；Get/Set 简单访问器未折叠（2j 缺口） | 见 §3.8 |
| delegates | 4 / 5 | 委托调用干净；新建委托上 null_check 冗余 + t0→loc0 拷贝 | 见 §3.9 |
| exceptions | 4 / 5 | try/catch/finally 语义正确；`new Exception` 产生死分配位 + ctor_nop | 见 §3.10 |
| statics | 3 / 5 | cctor 守卫逻辑正确但每次静态字段访问都发 NOINLINE 调用，且 .cctor 内部重复守卫 | 见 §3.11 |

平均 ≈ 3.7。全部用例构建通过（`cmake --build` EXIT=0）、`g++ -Wall -Wextra` 零警告
（quality gate 达成）、运行输出与 net48 参考逐字节一致（已抽查 loop/string）。

---

## 3. 逐用例证据

### 3.1 string（4/5）

`audit_gen_string/src/Cases_Str.cpp:19-25`：7 条 IL → 7 条语句，每个字符串字面量
完全内联到 icall 参数中：

```cpp
il2cpp::runtime::icall_console_write_line_s((Il2CppObject*)il2cpp::il2cpp_string_new_from_cs("hello")); // IL call @0x0005
```

- 优点：表达式完全折叠、无多余 temp、注释含 IL offset 溯源（@0x0005）。
- 缺点：**每个字符串字面量在每次调用时都重新 `il2cpp_string_new_from_cs` 分配**
  （CodeGenerator.cs:646 决定）。.NET 字面量是 interned 的，`ReferenceEquals` 语义与
  GC 分配成本均偏离；高频方法内字符串字面量是纯开销。这是语义/性能双重缺口
  （字面量 interning 未实现）。

### 3.2 loop（4/5）

`audit_gen_loop/src/Cases_Loop.cpp:33`：`ble.s` 折叠为 `if((loc4 > 100LL) == 0LL) goto L_7;`
（Not(Clt) 表示，语义正确但 `== 0LL` 是「取反比较」的冗余形式，可直接发 `<=`）。

反汇编验证（`g++ -O2 -S`，loop_audit.s:21-51）：整段 `sum 1..100`、while、do-while、
continue/break 循环被 clang **全部常量折叠/展开** —— 最终只剩 9 条 `movl $K, %ecx; call
icall_console_write_*`。证明生成代码的标量算术 IR 极其优化友好（-O3 下整循环变常量，
此处**超过** JIT 的常量传播水平）。

### 3.3 arrays（3/5）

`audit_gen_arrays/src/Cases_Arrays.cpp:54/87` 每次元素读写：

```cpp
(*(int32_t*)il2cpp::il2cpp_array_get_elem_ptr((Il2CppArray*)(loc0), (int32_t)(loc4)) = ...); // stelem
```

反汇编（arrays_audit.s:258-275）热循环 `.LBB0_37`：

```asm
.LBB0_37:                                   # Inner Loop Header
        movq    40(%rsp), %rcx
        callq   il2cpp_array_get_length      # 每轮都重取 length（函数调用！）
        cmpl    %eax, %edi
        jge     .LBB0_40
        movq    40(%rsp), %rcx
        movl    %edi, %edx
        callq   il2cpp_array_get_elem_ptr   # 每元素一次函数调用（内部再做边界检查）
        addl    (%rax), %esi
        incl    %edi
        jmp     .LBB0_37
```

- **`il2cpp_array_get_length` 没有被提升出循环**：转译器明明在循环前算了
  `t14 = il2cpp_array_get_length(loc1)`（Cases_Arrays.cpp:91），但因该函数是**非内联
  函数调用**，编译器无法证明它不改变 `arr->length`，于是每次迭代重算（Arrays.cpp:91
  的提升被 C++ 编译器否决）。.NET JIT 把 Length 当字段读、天然提升。
- 每次元素访问 = 一次真实函数调用（内部还有边界检查 + 冷 abort 分支）。JIT 是内联
  边界比较 + `movl [rax+idx*4], esi`。热循环每次迭代 2 次调用（get_length + get_elem_ptr）。
- 常量下标噪声：`(int32_t)(0LL)`/`(int32_t)(4LL)`（Cases_Arrays.cpp:58-59）。

### 3.4 objects（3/5）

`audit_gen_objects/src/Cases_Objects.cpp:49-50` 引用链表达式内同一接收者被
`il2cpp_null_check` **重复 3 次**：

```cpp
((Point*)il2cpp_null_check((Il2CppObject*)((Il2CppObject*)(((PointRef*)il2cpp_null_check((Il2CppObject*)(loc1)))->ref1))))->X
```

- 同表达式 3 次 `il2cpp_null_check` + 2 层冗余 cast；`loc1` 刚由 newobj 产生
  （:42-44）必非空，全部守卫都是死检查。全文件 17 处 null_check（objects 用例最高）。
- 转译器没有做「已知非空」集合（newobj 之后 / 同表达式内已查）→ 无 CSE 无消除。
- 写入正确：引用字段存储先发 `il2cpp_write_barrier` 再赋值（:47-48, :56-57），符合分代 GC 契约。
- GC 根：帧注册了 8 个 ref 槽，其中 t0/t1/t2/t3/t4 多个是「tX = newobj; locY = tX」纯拷贝
  （:36-38, :42-44），temp 未与 loc 合并 → 根表膨胀 + 每方法多 2-3 个冗余拷贝。

### 3.5 virtual（4/5）

`audit_gen_virtual/src/Cases_Virtual.cpp:43`：

```cpp
((int32_t(*)(Il2CppObject*))il2cpp_null_check((Il2CppObject*)(t0))->klass->vtable[0])(t0);
```

- vtable 槽分派正确；`Shape.cpp:10-13` vtable 表与 `Square.cpp:10-13` override 槽位正确。
- **无去虚化**：`new Square(4)` 后 `.Area()`/`.Sides()`（virtual.cs:37-39）接收者是
  编译期确定的 Square，仍走 `vtable[N]` 间接调用。JIT 对此类 sealed/已知类型调用会
  直接绑定。同表达式 `il2cpp_null_check(t2)` 在连续两条 vtable 调用中重复（:47-49）。
- `Square_Area` 反汇编（square_audit.s:26-30）实证**两个完全相同的 null_check 调用**：
  ```asm
  movq  %rcx, %rsi
  callq il2cpp_null_check      # 第 1 次
  movl  8(%rax), %edi
  movq  %rsi, %rcx
  callq il2cpp_null_check      # 第 2 次（同一 arg0，编译器无法消除不透明调用）
  imull 8(%rax), %edi
  ```
  同表达式 `arg0->W * arg0->W`（Square.cpp:52）本应 `mov eax,[rcx+8]; imul eax,eax`，
  实际 2 次函数调用 + 2 次内存读。JIT 侧最多 1 次判空 + 1 次 load。

### 3.6 interface（4/5）

`audit_gen_interface/src/Cases_Interface.cpp:44`：

```cpp
((int32_t(*)(Il2CppObject*, int32_t))*il2cpp_interface_dispatch(il2cpp_null_check((Il2CppObject*)(t0)), &ICalc_class, 0))(t0, 21LL);
```

- `Doubler.cpp:18-25` 接口槽→vtable 槽映射表正确。
- **每次接口调用 = null_check 调用 + interface_dispatch 线性搜索调用**
  （metadata.cpp:129-147 逐接口遍历），再间接调用。JIT 对已知接收者直接去虚化。
- `ICalc.cpp:16-17` 描述符 `vtable = nullptr` 但 `vtableSize = 2` —— 接口类型
  元数据自相矛盾（接口无 vtable，vtableSize 应为 0；当前无功能影响但属脏数据）。

### 3.7 boxing（3/5）

`audit_gen_boxing/src/Cases_Boxing.cpp:63-72` join 点冗余 temp：

```cpp
t10 = t6;                  // brfalse 前的 spill
if(loc0 == 0LL) goto L_109;
t11 = (...vtable[0])(loc0);
t12 = t10;                 // 真臂
t13 = t11;
goto L_110;
L_109:
t12 = t10;                 // 假臂 —— t10 与 t12 是同一值的两代拷贝
t13 = 0LL;
```

- 三元 `d == null ? 0 : d.Legs()` 的三臂归一化正确，但 `t10`/`t12` 两代冗余拷贝
  可合并（t12 可直接引用 t6）。同时 `:65` 的 `il2cpp_null_check` 在 `loc0 != 0` 已
  分支保证下是死检查（同一表达式的分支守卫已隐含非空）。
- **常量化缺口**：`:76` `icall_console_write_line_i((nullptr == nullptr));`
  （`n == null ? 1 : 0`，boxing.cs:26，n 已知 null）—— 空局部常量未传播，`nullptr ==
  nullptr` 恒真未折叠为 1；`:77` `il2cpp_is_instance(nullptr, &Animal_class)`（boxing.cs:27）
  同样未折叠为 0。转译器对 int32 算术常量折叠（CodeGenerator.cs:1133 TryFoldInt32）但不做
  **局部值常量传播**。
- `:45` `il2cpp_box_int32(nullptr, 5LL)` —— klass 参数恒传 nullptr（运行时惰性解析到
  Int32 盒类），参数位形同虚设。

### 3.8 generics（4/5）

`audit_gen_generics/src/Cases_Generics.cpp:35`：`Box_1_int_Set_…(t0, 42LL)` —— 泛型实例
方法调用被绑定为**直接调用**（正确，无继承覆盖面）；`Box_1_int.cpp:10-20` 描述符
`vtableSize 0`、`refFieldBitmap 0b00u` 正确。

- **简单访问器未折叠（铁律 2j 未落地）**：`Box_1_int_Get`（Box_1_int.cpp:31-34）是
  `return ((Box_1_int*)null_check(arg0))->item;` 单字段读取，`Set` 单字段存储，却在每个
  调用点发完整函数调用 + null_check（Cases_Generics.cpp:36/41/43 调用 3 次 Get）。
  JIT 会内联这类 getter。这正是 docs/architecture/属性折叠设计.md 规划、尚未实现的
  AccessorAnalyzer 预扫描 pass 的适用场景。

### 3.9 delegates（4/5）

`audit_gen_delegates/src/Cases_Delegates.cpp:56`：

```cpp
il2cpp::runtime::icall_console_write_line_i(((int32_t(*)(int32_t))((Il2CppDelegate*)il2cpp_null_check((Il2CppObject*)(loc0)))->methodPtr)(21LL));
```

- 委托调用 = null_check 调用 + 函数指针间接调用，正确且紧凑。两处可优化：
  ① `loc0`/`t2` 刚从 `il2cpp_delegate_new` 产生必非空，null_check 冗余；
  ② `t0 = delegate_new; loc0 = t0;`（:54-55）temp 拷贝可合并。

### 3.10 exceptions（4/5）

`audit_gen_exceptions/src/Cases_Exceptions.cpp:38-45`：

```cpp
try {
    t3 = nullptr;                                  // newobj —— 未分配，占位
    il2cpp::runtime::il2cpp_object_ctor_nop(t3);   // 死调用（t3 为 null）
    il2cpp::il2cpp_throw_new("System.Exception", "exception thrown");
} catch (il2cpp::DotNetException& __il2cpp_ex) {
    if (!il2cpp::il2cpp_exception_matches("System.Exception", __il2cpp_ex.type_name())) throw;
    ...
}
```

- `throw new Exception("boom")` 识别为异常载体、直接走 `il2cpp_throw_new`（不真分配
  Exception 对象，正确）；但 `newobj` 仍发 `t3 = nullptr` + `ctor_nop(t3)` 死代码
  （Exception 的构造函数是空体），t3 还被注册为 GC 根（:25）。
- try/catch 匹配、finally 复制到 catch 再 rethrow（:88-93, :119-124）语义正确。
  **已知未覆盖路径**：try 内 `return` 穿过 finally（本用例无此形态，未触发，需专项
  用例验证；正确性归转译器域审计）。

### 3.11 statics（3/5）

`audit_gen_statics/src/Cases_Statics.cpp:35-37` 读改写静态字段：

```cpp
ensure_cctor_Cases_Statics();                      // IL ldsfld
ensure_cctor_Cases_Statics();                      // IL stsfld —— 与前一条紧邻重复！
Cases_Statics_sf_counter = (Cases_Statics_sf_counter + 2LL);
```

- 连续两条 `ensure_cctor`（ldsfld 与 stsfld 各发一条）只相隔一行，第二次恒为 no-op；
  `:45-47` 同模式重复。`.cctor` 内部（:61,63）也在每个 stsfld 前发 `ensure_cctor` ——
  cctor 在守卫内执行、标志恒为 true，全是死调用。
- **守卫本身是 `IL2CPP_NOINLINE` 函数调用**（:12）：循环内每次静态字段访问 = 一次
  非内联调用 + 布尔测试。JIT 把 cctor-done 检查内联并提升出循环。`ensure_cctor` 包装
  可内联（.cctor 体保持 NOINLINE 即可保留帧记录），循环可整体只检查一次。

---

## 4. 横切发现（全部用例）

### 4.1 每次 int32 常量都带 LL 后缀 → 全部常量算术被抬到 int64

CodeGenerator.cs:640 `return i.Value.ToString(...) + "LL";` —— **所有 int32 常量发成
`NLL`**。后果：
- `loc0 = 0LL`、`return 0LL`（Shape.cpp:42）、`(loc4 + 1LL)`（Cases_Loop.cpp:31）——
  `int32_t + long long` 在 C++ 侧提升为 int64 运算后窄化回 int32。
- -O3 下编译器会窄化回去（square_audit.s 实证 `imull` 仍是 32 位），但 -O0/-O1 下
  每处常量参与运算都是 64 位指令；且这是全局代码噪声。建议 int32 常量发裸字面量
  （或 `i32` 后缀），仅 int64/uint64 用 LL（项目目前无 int64 支持，C3 审计项）。
- 副作用：`if((loc4 > 100LL) == 0LL)` 的 `== 0LL` 取反比较（Cases_Loop.cpp:33）可直接
  折叠为 `<=`；`nullptr == nullptr`（Cases_Boxing.cpp:76）应折叠为 1。

### 4.2 运行时热辅助全部非内联 → 无法内联也无法提升

以下被生成代码高频调用的辅助函数全是 .cpp 内普通函数定义（非 inline）：
- `il2cpp_array_get_elem_ptr`（array.cpp:115）/ `il2cpp_array_get_length`（array.cpp:134）
- `il2cpp_null_check`（object.cpp:53）
- `il2cpp_interface_dispatch`（metadata.cpp:116，且是线性搜索）
- `il2cpp_checked_div`（异常域）、`il2cpp_is_instance`、`il2cpp_write_barrier`

反汇编实证（§3.3/§3.5）：编译器既不能内联它们，也不能跨调用做 CSE / 循环提升 /
别名分析（无法证明 `get_length` 不写 `arr->length`）。这是**热循环性能与 JIT 差距的
头号来源**。把 `get_elem_ptr`/`get_length`/`null_check` 改为头文件内联（边界检查与
冷 abort 分支保留，内联后 abort 路径不进热路径），循环即可变成寄存器索引寻址、
length 可提升、可向量化。

### 4.3 内存安全（根 wrap/unwrap）总评

- **GC 根正确**：每个含引用局部的方法序言都注册 `__frameRefs[]` + `il2cpp_frame_push`
  （Cases_Objects.cpp:28-34 等），返回前 pop（arrays_audit.s:284 实证）。temp/loc 全部
  根化，无悬垂。写屏障在引用字段存储前正确发射。
- **已知缺口（与转译器域审计 C1 一致）**：帧根只覆盖引用局部/temp，**不含引用参数
  （含 this）**（§3.4 帧只含 t*/loc*）。生成代码忠实实现了这一（有缺陷的）根策略。
- 无栈上悬垂：所有字符串/temp 要么根化要么立即消费；`new Exception` 不分配（无泄漏）。
- 冗余根：`tX = newobj; locY = tX` 的 tX 纯拷贝被根化（§3.4/§3.9），根表偏大。

### 4.4 代码体积与注释质量

| 指标 | 观测 |
|---|---|
| 每方法体行数 | 1-30 行（Main 类 20-30 行，其余 1-8 行），无巨型方法 |
| 注释占比 | 22%-48%（string 46%、interface 47%、objects 43%、statics 38%、loop 28%） |
| 注释质量 | 高：函数头含 IL 签名/RVA/token/Semantics（Cases_Str.cpp:12-16），语句级 IL provenance（`// IL stloc.0 @0x000B`），全英文，行尾注释右对齐 ~100 列 |
| 冗余膨胀 | 表达式内重复 null_check + 冗余 cast 链使单行可超 200 字符（Cases_Objects.cpp:49）；LL 后缀噪声 |

注释质量对齐风格规范 §3，无形式化注释。

---

## 5. 铁律 2e 差距分析（生成代码性能 vs 官方 JIT）

### 已达标 / 反超的场景

- **标量算术 / 无堆访问的循环**：转译器折叠出极干净的 int32 IR，-O3 下 clang 可把
  整段循环常量折叠（loop 用例：`sum 1..100` → `movl $5050, %ecx`，loop_audit.s:21）。
  此场景生成代码 ≥ JIT。
- **正确性前提**：全部用例运行输出与 net48 参考逐字节一致；-Wall -Wextra 零警告
  （quality gate 达成）；构建零错误。

### 明显落后 JIT 的场景（差距量化）

| 场景 | JIT 行为 | 当前生成代码 | 差距来源 |
|---|---|---|---|
| 数组热循环 | 内联边界检查 + 索引寻址，Length 提升，可向量化 | 每元素 1 次 `get_elem_ptr` 调用 + 每轮 1 次 `get_length` 调用（无法提升） | 4.2 非内联辅助 |
| 字段/引用链表达式 | 1 次判空 + 1 次 load | 同表达式重复 null_check（2-3 次调用） | 4.1 无 CSE / 已知非空集合 |
| 虚方法调用 | 已知类型去虚化 / sealed 直接绑定 | 恒走 vtable 间接调用 | 无去虚化 pass |
| 接口调用 | 去虚化或缓存槽 | null_check + 线性搜索 + 间接调用 | 无去虚化；dispatch 线性搜索 |
| 简单 getter/setter | 内联为字段访问 | NOINLINE 全函数调用 + null_check（2j 未落地） | 属性折叠未实现 |
| 静态字段访问（循环内） | cctor 检查内联 + 循环提升 | 每次访问 NOINLINE 守卫调用 | ensure_cctor 不可内联 + 冗余 |
| 字符串字面量 | interned 复用 | 每次调用重新分配 | 无字面量 interning |

**结论**：差距全部集中在「堆访问 + 非内联辅助调用」路径，可用第 6 节优化机会收敛；
纯标量路径已达标。差距可在不改变语义的前提下通过转译器 emit-time 变换 +
运行时辅助内联化消除。

---

## 6. Top 优化机会（按预期收益排序）

### #1 运行时热辅助内联化（数组 + null_check + checked_div）

- **位置**：src/runtime/src/array.cpp:115/134、object.cpp:53（+异常域 checked_div）
- **现状**：非内联函数；每次数组元素访问 = 1 次调用；循环内 `get_length` 无法被 C++
  编译器提升（4.2 反汇编实证 arrays_audit.s:261）。
- **建议**：把 `il2cpp_array_get_elem_ptr` / `il2cpp_array_get_length` /
  `il2cpp_null_check` 改为**头文件内联函数**（边界检查保留，abort 路径冷；或增加
  发射侧 `inline` 直接展开为 `arr->data + idx*elemSize` + 独立内联边界检查宏）。
- **预期收益量级**：数组热循环 2-5x（每元素去 1 次调用；length 可提升、可向量化）；
  是所有堆访问路径的乘法因子，第一优先。

### #2 去虚化（devirtualization）：newobj 已知类型 → 直接调用

- **位置**：转译器 emit-time（VTablePlanner / StackFold.EmitVirtualCall）
- **现状**：`new Square(4)` 后 `.Area()` 恒走 `vtable[N]`（Cases_Virtual.cpp:61-66）；
  `new Doubler()` 后接口调用恒走线性搜索（Cases_Interface.cpp:44，metadata.cpp:129）。
- **建议**：接收者为「本次 newobj 且类型 final（封闭世界无子类）」时把 callvirt/
  interface call 绑定为直接函数调用；接口调用同理。同时消除 interface_dispatch
  线性搜索。
- **预期收益量级**：虚/接口调用密集代码 1.5-3x；每次调用省 vtable/线性搜索 + 使
  后续常量传播/访问器折叠能进一步生效（联动收益）。

### #3 简单访问器折叠（铁律 2j 落地）

- **位置**：转译器（docs/architecture/属性折叠设计.md 已规划：AccessorAnalyzer
  预扫描 + StackFold.EmitCall 折叠 + 跳过 accessor 函数发射）
- **现状**：未实现（转译器全仓无 AccessorAnalyzer，仅 TypeDef.cs:53 一处注释提及）；
  `Box_1_int_Get`/`Set` 等单字段访问器在调用点发完整 NOINLINE 调用 + null_check
  （Box_1_int.cpp:31-46，Cases_Generics.cpp:36/41/43）。
- **建议**：按属性折叠设计.md 落地；非虚 + 同类内 + 简单 get/set 形态 → 调用点
  直接展开为字段访问。
- **预期收益量级**：属性/访问器密集代码 1.5-4x；是禁内联下唯一合法 accessor 消除。

### #4 冗余 null_check 消除（已知非空集合 + 表达式内 CSE）

- **位置**：转译器 emit-time（FieldExpr / VirtualCallExpr / 委托调用发射处）
- **现状**：55 处 null_check（objects 17、interface 9、virtual 8）；同表达式重复
  （Cases_Objects.cpp:49 三连、Square.cpp:52 两连），且多数接收者刚 newobj
  （Cases_Delegates.cpp:56）、或已被分支守卫保证非空（Cases_Boxing.cpp:64-65）。
  反汇编实证两个相同调用并存（square_audit.s:26-30）。
- **建议**：EmitBody 维护「已知非空」集合：`il2cpp_object_new`/`delegate_new` 之后、
  同表达式已检查过的接收者，跳过后续 null_check；跨表达式对同一 temp 做 CSE。
- **预期收益量级**：对象/委托密集代码 1.2-2x（消守卫调用 + 减小帧根表）；
  同时大幅减代码体积。

### #5 静态 cctor 守卫优化（内联 + 去重 + 循环提升）

- **位置**：转译器 cctor 守卫发射（statics 域）；运行时 ensure 包装
- **现状**：`ensure_cctor_*` 是 NOINLINE 函数（Cases_Statics.cpp:12），循环内每次
  静态字段访问都调用；`.cctor` 内部与连续 ldsfld/stsfld 间有重复守卫（:35-37, :61-64）。
- **建议**：守卫包装改为可内联（.cctor 体保持 NOINLINE 保留帧记录）；循环内静态字段
  访问把 done 检查提升出循环（.NET JIT 同款）；`.cctor` 体内不发守卫。
- **预期收益量级**：静态字段密集循环 1.5-3x。

### 其他机会（低优先 / 清理级）

- **局部值常量传播**：`nullptr == nullptr`（Cases_Boxing.cpp:76）、`is_instance(nullptr,…)`
  （:77）应折叠为 1/0；null 局部传播即可覆盖。
- **int32 常量 LL 后缀清理**（CodeGenerator.cs:640）：int32 常量发裸字面量，避免全局
  int64 提升（-O0/-O1 有效，-O3 被编译器修复）；`== 0LL` 取反比较折叠为 `<=`。
- **temp 合并**：`tX = newobj; locY = tX`（Cases_Objects.cpp:36-38 等）与 join 点两代
  temp（Cases_Boxing.cpp:63-72 的 t10/t12）合并，减帧根表。
- **`throw new X` 死代码**：`t3 = nullptr; il2cpp_object_ctor_nop(t3);`（Cases_Exceptions.cpp:39-40）
  可直接发 `il2cpp_throw_new`。
- **ICalc.cpp:16-17** 接口描述符 vtableSize=2 与 vtable=nullptr 矛盾，应置 0。

---

## 7. 附：审计过程证据索引

- 转译：11 用例 `il2cppc.exe` 全部 EXIT=0（`build/audit_gen_<case>/`）
- 构建验证：`cmake -S build/audit_gen_loop -B build/audit_gen_loop/build -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release` EXIT=0 零警告
- 运行验证：`il2cpp_gen_exe.exe` 输出 5050/10987654321/345/24 与 `loop.exe` net48 参考逐字节一致
- 反汇编：`g++ -O2 -S` 生成 loop_audit.s / arrays_audit.s / square_audit.s（工作区 `C:\Users\windo\AppData\Local\Temp\opencode\`）
- 属性折叠实现状态：grep `AccessorAnalyzer|accessor|属性` 于 src/il2cppc → 零实现命中
