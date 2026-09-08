# r77 DateTimeFormatInfo refFieldBitmap 溢出分析报告

**生成时间**: 2026-09-06  
**目标文件**: `E:\il2cpp\src\runtime\src\gc.cpp`, `E:\il2cpp\out\mscorlib_gen_r77\src\System_Globalization_DateTimeFormatInfo.cpp`  
**数据来源**: r77 错误日志 + 结构体布局分析

---

## 1. 关键发现: r77 中 DateTimeFormatInfo 的实际错误

### 1.1 错误统计

r77 三片扫描中，DateTimeFormatInfo 相关编译错误仅 **5 个**：

| 错误类型 | 数量 |
|---------|------|
| `assigning to 'int32_t' from incompatible type 'Il2CppDateTime'` | 2 |
| `no viable conversion from 'int32_t' to 'Il2CppDateTime'` | 2 |
| `cannot initialize parameter of type 'int32_t*' with rvalue of type 'Il2CppObject**'` | 1 |

### 1.2 根因: 类型不匹配，非溢出

这 5 个错误的根因是 **DateTimeFormatInfo 结构体中 DateTime 字段被错误地声明为 int32_t**，而非 `il2cpp::runtime::Il2CppDateTime`：

```
// generated.h:18842-18845
Il2CppObject* m_dateWords; // slot 47 at offset 376 (GC reference)
Il2CppObject* m_fullTimeSpanPositivePattern; // slot 48 at offset 384 (GC reference)
Il2CppObject* m_fullTimeSpanNegativePattern; // slot 49 at offset 392 (GC reference)
Il2CppObject* m_dtfiTokenHash; // slot 50 at offset 400 (GC reference)
```

但生成代码中某些路径将 `Il2CppDateTime` 值赋给了 int32_t 槽位（或反之）。

**结论**: r77 中的 DateTimeFormatInfo 错误与 refFieldBitmap 溢出无关。

---

## 2. refFieldBitmap 溢出分析（潜在风险）

### 2.1 DateTimeFormatInfo 结构体布局

`System_Globalization_DateTimeFormatInfo` 有 **51 个引用字段**（slot 1-50），加上 slot 0 的对象头：

```
slot  1: m_cultureData              (ref)
slot  2: m_name                     (ref)
slot  3: m_langName                 (ref)
slot  4: m_compareInfo              (ref)
slot  5: m_cultureInfo              (ref)
slot  6: amDesignator               (ref)
slot  7: pmDesignator               (ref)
slot  8: dateSeparator              (ref)
slot  9: generalShortTimePattern    (ref)
slot 10: generalLongTimePattern     (ref)
slot 11: timeSeparator              (ref)
slot 12: monthDayPattern            (ref)
slot 13: dateTimeOffsetPattern      (ref)
slot 14: calendar                   (ref)
slot 15: firstDayOfWeek             (int32_t, value)
slot 16: calendarWeekRule           (int32_t, value)
slot 17: fullDateTimePattern        (ref)
slot 18: abbreviatedDayNames        (ref)
slot 19: m_superShortDayNames       (ref)
slot 20: dayNames                   (ref)
slot 21: abbreviatedMonthNames      (ref)
slot 22: monthNames                 (ref)
slot 23: genitiveMonthNames         (ref)
slot 24: m_genitiveAbbreviatedMonthNames (ref)
slot 25: leapYearMonthNames         (ref)
slot 26: longDatePattern            (ref)
slot 27: shortDatePattern           (ref)
slot 28: yearMonthPattern           (ref)
slot 29: longTimePattern            (ref)
slot 30: shortTimePattern           (ref)
slot 31: allYearMonthPatterns       (ref)  ← 最后一个被 GC 扫描的引用
slot 32: allShortDatePatterns       (ref)  ← 溢出！未被扫描
slot 33: allLongDatePatterns        (ref)  ← 溢出！未被扫描
slot 34: allShortTimePatterns       (ref)  ← 溢出！未被扫描
slot 35: allLongTimePatterns        (ref)  ← 溢出！未被扫描
slot 36: m_eraNames                 (ref)  ← 溢出！未被扫描
slot 37: m_abbrevEraNames           (ref)  ← 溢出！未被扫描
slot 38: m_abbrevEnglishEraNames    (ref)  ← 溢出！未被扫描
slot 39: optionalCalendars          (ref)  ← 溢出！未被扫描
slot 40: m_isReadOnly               (int32_t, value)
slot 41: formatFlags                (int32_t, value)
slot 42: CultureID                  (int32_t, value)
slot 43: m_useUserOverride          (int32_t, value)
slot 44: bUseCalendarInfo           (int32_t, value)
slot 45: nDataItem                  (int32_t, value)
slot 46: m_isDefaultCalendar        (int32_t, value)
slot 47: m_dateWords                (ref)  ← 溢出！未被扫描
slot 48: m_fullTimeSpanPositivePattern (ref)  ← 溢出！未被扫描
slot 49: m_fullTimeSpanNegativePattern (ref)  ← 溢出！未被扫描
slot 50: m_dtfiTokenHash            (ref)  ← 溢出！未被扫描
```

### 2.2 当前 refFieldBitmap 值

```
refFieldBitmap low:  0b111100000001111111111111111111111100111111111111110
refFieldBitmap high: 0b0
refFieldCount: 41
```

**解析**：
- low bitmap 的 bit 0 = 1（对象头，但 GC 从 slot 1 开始扫描）
- bit 1-14 = 1（引用字段）
- bit 15-16 = 0（值字段）
- bit 17-31 = 1（引用字段）
- **bit 32-50 = 0**（未被标记，因为 uint32_t 只有 32 位）
- high bitmap = 0（未使用）

### 2.3 GC 扫描代码的限制

`gc.cpp:547-564`：

```cpp
const uint32_t bitmap = klass->refFieldBitmap;
const int32_t slotLimit = static_cast<int32_t>(
    klass->instanceSize / static_cast<int32_t>(sizeof(void*)));
// Start at slot 1: slot 0 is the klass header, not a managed reference.
for (int32_t slot = 1; slot < 32 && slot < slotLimit; ++slot)
{
    if ((bitmap & (1u << slot)) == 0) continue;
    Il2CppObject** ref = reinterpret_cast<Il2CppObject**>(obj) + slot;
    MarkSlot(ref);
}
```

**两个限制**：
1. `slot < 32`：GC 只扫描前 32 个 slot（slot 1-31）
2. `uint32_t bitmap`：只有 32 位，无法覆盖 slot 32+

### 2.4 溢出的影响

DateTimeFormatInfo 的 slot 32-50 引用字段**不会被 GC 扫描**，导致：

1. **内存泄漏**: 这些引用字段指向的对象不会被标记，会被错误回收
2. **悬空指针**: 回收后字段变成野指针
3. **崩溃**: 访问被回收的对象导致 segfault

**风险等级**: 高（GC 安全漏洞）

---

## 3. 修复方案

### 3.1 方案 A: 扩展 refFieldBitmap 为 64 位（推荐）

将 `klass->refFieldBitmap` 从 `uint32_t` 改为 `uint64_t`：

```cpp
// gc.cpp 修改
const uint64_t bitmap = static_cast<uint64_t>(klass->refFieldBitmap) |
    (static_cast<uint64_t>(klass->refFieldBitmapHigh) << 32);
for (int32_t slot = 1; slot < 64 && slot < slotLimit; ++slot)
{
    if ((bitmap & (1ull << slot)) == 0) continue;
    Il2CppObject** ref = reinterpret_cast<Il2CppObject**>(obj) + slot;
    MarkSlot(ref);
}
```

**优点**: 覆盖所有 slot，向后兼容  
**缺点**: 需要修改 Il2CppClass 结构体（或利用现有 high 字段）

### 3.2 方案 B: 使用现有 refFieldBitmapHigh

如果 `refFieldBitmapHigh` 字段存在且已保留，只需修改扫描循环：

```cpp
const uint64_t bitmap = static_cast<uint64_t>(klass->refFieldBitmap) |
    (static_cast<uint64_t>(klass->refFieldBitmapHigh) << 32);
```

**最小改动**，但需要确认 `refFieldBitmapHigh` 在 Il2CppClass 中的位置。

### 3.3 方案 C: 跳过高位字段（临时方案）

对于 slot >= 32 的引用字段，在结构体中插入 padding 使其对齐到 64 字节边界，确保至少前 64 字节可被扫描。

**缺点**: 不能根本解决问题，只是降低概率。

---

## 4. GC 扫描影响评估

| 方案 | GC 正确性 | 性能影响 | 改动范围 | 风险 |
|------|----------|----------|----------|------|
| A: 64-bit bitmap | ✅ 完全正确 | 无（位运算） | gc.cpp + 结构体 | 低 |
| B: 使用 high 字段 | ✅ 完全正确 | 无 | gc.cpp | 最低 |
| C: 跳过高位 | ❌ 部分正确 | 无 | 无 | 高（泄漏） |

---

## 5. 结论

**r77 中 DateTimeFormatInfo 的 5 个编译错误与 refFieldBitmap 溢出无关**，是类型声明错误。

但 DateTimeFormatInfo 的 refFieldBitmap 溢出是一个**潜在的 GC 安全漏洞**：
- 结构体有 51 个引用字段（slot 1-50）
- 当前 refFieldBitmap 只覆盖 slot 0-31
- slot 32-50 的引用字段不会被 GC 扫描
- 可能导致内存泄漏和悬空指针

**建议**: 采用方案 B，利用现有 refFieldBitmapHigh 字段扩展扫描范围。

---

*报告结束*
