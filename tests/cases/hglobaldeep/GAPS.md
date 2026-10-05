# GAPS — hglobaldeep (e2e batch C66)

## D-HG-1: Marshal.ReAllocHGlobal 未接线（generic 生成名路径，非 LOUD）

- 事实（代码审查）：MethodMap.Marshal.cs 无 "ReAllocHGlobal" case（Select-String
  0 命中）；src/runtime 无 realloc icall。使用即落入 generic generated-name 路径
  → cmake 构建期未定义 extern（非 NotSupportedException LOUD 拒绝）。
- 后果：net48 的 Alloc→ReAlloc→Copy 增长路径在 ILC 不可达；本用例覆盖
  Alloc/Copy 组合（部分拷贝/偏移/覆写/多缓冲/字符串互操作/压力循环）。
- 状态：未修（本批只记录，不修产品代码）。

## D-HG-2: Marshal.Copy 非 int[] 载荷用 int32 步长（元素宽度分歧）

- 事实：icall_marshal_copy_to/from_unmanaged（runtime.cpp:158-179）恒以
  `arr->data + start*4, memcpy(len*4)` 拷贝——byte[]/short[]/long[]/double[] 等
  会被按 4 字节/元素拷贝，与 net48 的元素宽度拷贝分歧。用例只用 int[]。

## D-HG-3: AllocHGlobal 零初始化契约分歧（oracle 实证 2026-08-12）

- 事实：runtime 用 GlobalAlloc(GMEM_FIXED|GMEM_ZEROINIT)（全零），net48
  AllocHGlobal 用 LocalAlloc(LMEM_FIXED)（不保证清零，未写槽位为垃圾）。
  probe "写 2/8 槽读 8 槽" 在 net48 oracle 打印 False → 已移除；用例只回读所写槽位。
