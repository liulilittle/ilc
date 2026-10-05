# icall 缺口清单（runtime 已实现 vs 转译器已接线）

> 审计时间：2026-08-03（只读审计，未改任何源码）
> 方法：`src/runtime/include/internal/*.h` 全量提取函数声明（含 inline 定义，双命名 `icall_*`/`il2cpp_*`）→ 与 `src/il2cppc/**/*.cs` 词边界引用集合求差 → 逐项人工验证上下文（排除头文件 include 名 / CMake 目标 / 生成 inline / loud 拒绝消息 / 注释噪音）。
> 用途：BCL 接线总攻施工单。接线代理按域批量消费，接线后回填"已接线"标记。

## 统计总览

| 集合 | 数量 |
|---|---|
| runtime 头文件声明函数（50 头，去重） | 438 |
| 转译器已引用（token 匹配） | 264 |
| **正向缺口：runtime 有、转译器未接线** | **174** |
| 反向候选：转译器 token、runtime 头无 | 46 |
| **真反向缺口（P0 链接隐患）** | **0** |

正向缺口按优先级分布：P1 高 = 78 · P2 中 = 55 · P3 低（内部/诊断） = 41
（P3 细分为：runtime 内部 API 24、诊断/断言 7、throw 特化 10——多数**不需要**转译器接线）

---

## P0 反向缺口：0 个（无链接失败隐患）

46 个反向候选逐项验证后全部为噪音，**当前转译器发射的所有函数名在 runtime 头均有定义**：

| 候选类别 | 数量 | 说明 |
|---|---|---|
| 头文件 include 名（`il2cpp_math` 等） | 28 | CodeGenerator 的 `#include "il2cpp_math.h"` 字符串 token，非函数 |
| CodeGenerator 生成 inline helper | 2 | `il2cpp_double_bits`、`il2cpp_console_write_line_l`（生成代码自带定义） |
| 生成代码导出符号 | 2 | `il2cpp_entry_main`、`il2cpp_runtime_init`（entry.cpp 由转译器生成） |
| CMake 目标名 | 2 | `il2cpp_gen`、`il2cpp_gen_exe` |
| loud 拒绝消息文本 | 2 | `icall_console_is_input_redirected`、`icall_directory_delete`（仅出现在 NotSupportedException 消息，不发射） |
| 截断 token / 注释 | 10 | `icall_math_`、`il2cpp_unbox_`、`il2cpp_type_from_name` 等 |

> 注意：`il2cpp_stringbuilder_capacity`（MethodRefResolver.cs:743）在 runtime 头**不存在**，但 fcap cast 在 CodeGenerator.cs:827/1806 坍缩为 `->capacity` 字段读取，从不发射为函数调用——非链接隐患，仅占位名。

---

## 正向缺口（174）——按域分组

### P1 高：常用 BCL API 直接映射（接线优先）

#### System.Math（il2cpp_math.h，7）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| icall_math_max_i | Math.Max(int,int) | P1 |
| icall_math_max_l | Math.Max(long,long) | P1 |
| icall_math_max_d | Math.Max(double,double) | P1 |
| icall_math_min_i | Math.Min(int,int) | P1 |
| icall_math_min_l | Math.Min(long,long) | P1 |
| icall_math_min_d | Math.Min(double,double) | P1 |
| icall_math_clamp_i | Math.Clamp(int,int,int)（net48 无此 API，.NET Core 契约） | P2 |

#### System.String（il2cpp_string_ext2.h / il2cpp_concat.h，4）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| icall_string_concat_3 | string.Concat(a,b,c)（3 段快速路径） | P1 |
| icall_string_concat_4 | string.Concat(a,b,c,d)（4 段快速路径） | P1 |
| il2cpp_string_builder_join | string.Join(char, string[])（单字符分隔符） | P1 |
| il2cpp_string_repeat | string 重复 N 次（Concat 优化） | P2 |

#### System.StringBuilder（il2cpp_stringbuilder.h，0 缺口——11 个已全部接线 ✓）

#### 数值 TryParse（il2cpp_parse.h，6）
| icall | .NET 签名 | 优先级 | 接线注意 |
|---|---|---|---|
| icall_try_parse_int32 | int.TryParse(string, out int) | P1 | out 指针 → 需要 M 阶段 out 支持或先拒绝 |
| icall_try_parse_int64 | long.TryParse(string, out long) | P1 | 同上 |
| icall_try_parse_double | double.TryParse(string, out double) | P1 | 同上 |
| icall_try_parse_bool | bool.TryParse(string, out bool) | P1 | 同上 |
| icall_try_parse_byte | byte.TryParse(string, out byte) | P2 | 同上 |
| icall_try_parse_int32_base | Convert.ToInt32(string, int fromBase) | P2 | 同上 |

#### System.Int32（il2cpp_format.h，1）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| icall_int32_to_string_hex | int.ToString("X")（十六进制） | P1 |

#### System.Diagnostics.Stopwatch（stopwatch.h，5）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| il2cpp_stopwatch_get_frequency | Stopwatch.Frequency (static readonly long) | P1 |
| il2cpp_stopwatch_get_timestamp | Stopwatch.GetTimestamp() (static long) | P1 |
| il2cpp_stopwatch_get_elapsed_ticks | Stopwatch.ElapsedTicks（start/end 原始差值） | P1 |
| il2cpp_stopwatch_get_elapsed_milliseconds | Stopwatch.ElapsedMilliseconds | P1 |
| il2cpp_stopwatch_get_elapsed_microseconds | Stopwatch.ElapsedMicroseconds | P2 |

#### System.Collections.Queue/Stack（stackqueue.h，14）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| il2cpp_queue_class | Queue 类描述符（object_new 用） | P1 |
| il2cpp_queue_create | new Queue() | P1 |
| il2cpp_queue_enqueue | Queue.Enqueue(object) | P1 |
| il2cpp_queue_dequeue | Queue.Dequeue() | P1 |
| il2cpp_queue_peek | Queue.Peek() | P1 |
| il2cpp_queue_get_count | Queue.Count | P1 |
| il2cpp_queue_clear | Queue.Clear() | P2 |
| il2cpp_stack_class | Stack 类描述符 | P1 |
| il2cpp_stack_create | new Stack() | P1 |
| il2cpp_stack_push | Stack.Push(object) | P1 |
| il2cpp_stack_pop | Stack.Pop() | P1 |
| il2cpp_stack_peek | Stack.Peek() | P1 |
| il2cpp_stack_get_count | Stack.Count | P1 |
| il2cpp_stack_clear | Stack.Clear() | P2 |

#### System.IO 流（streamio.h，14）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| il2cpp_stream_file_open | FileStream 打开（path, mode） | P1 |
| il2cpp_stream_memory_create | new MemoryStream() | P1 |
| il2cpp_stream_read | Stream.Read(byte[], off, len) | P1 |
| il2cpp_stream_write | Stream.Write(byte[], off, len) | P1 |
| il2cpp_stream_read_line | StreamReader.ReadLine() | P1 |
| il2cpp_stream_write_line | StreamWriter.WriteLine(string) | P1 |
| il2cpp_stream_close | Stream.Close/Dispose | P1 |
| il2cpp_stream_get_length | Stream.Length | P1 |
| il2cpp_stream_get_position | Stream.Position | P1 |
| il2cpp_stream_set_position | Stream.Position set | P1 |
| il2cpp_stream_get_can_read | Stream.CanRead | P2 |
| il2cpp_stream_get_can_write | Stream.CanWrite | P2 |
| il2cpp_stream_flush | Stream.Flush | P2 |
| il2cpp_stream_memory_to_array | MemoryStream.ToArray() | P2 |

#### System.Text.Encoding（il2cpp_encoding.h，5）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| il2cpp_encoding_get_bytes | Encoding.GetBytes(string)（size-query 模式） | P1 |
| il2cpp_encoding_get_chars | Encoding.GetChars(byte[]) | P1 |
| il2cpp_encoding_utf8_convert | UTF8Encoding 转码（原始缓冲） | P2 |
| il2cpp_encoding_ascii_convert | ASCIIEncoding 转码 | P2 |
| il2cpp_encoding_unicode_convert | UnicodeEncoding 转码 | P2 |

#### System.Threading.Interlocked（il2cpp_interlocked.h，12）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| il2cpp_interlocked_increment / _64 | Interlocked.Increment(ref int/long) | P1 |
| il2cpp_interlocked_decrement / _64 | Interlocked.Decrement(ref int/long) | P1 |
| il2cpp_interlocked_add / _64 | Interlocked.Add(ref int/long, int/long) | P1 |
| il2cpp_interlocked_exchange / _64 / _pointer | Interlocked.Exchange(ref T) | P1 |
| il2cpp_interlocked_compare_exchange / _64 / _pointer | Interlocked.CompareExchange(ref T) | P1 |

#### System.IntPtr/UIntPtr（intptr.h，6）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| il2cpp_intptr_add | IntPtr.Add(IntPtr, int) | P1 |
| il2cpp_intptr_subtract | IntPtr.Subtract(IntPtr, int) | P1 |
| il2cpp_intptr_to_int64 | IntPtr.ToInt64() | P1 |
| il2cpp_intptr_to_int32 | IntPtr.ToInt32() | P1 |
| il2cpp_intptr_op_equality | IntPtr.op_Equality(IntPtr, IntPtr) | P1 |
| il2cpp_intptr_compare | IntPtr.CompareTo(IntPtr) | P2 |

#### System.Globalization.CultureInfo（cultureinfo.h，6）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| il2cpp_culture_get_current | CultureInfo.CurrentCulture | P1 |
| il2cpp_culture_get_invariant | CultureInfo.InvariantCulture | P1 |
| il2cpp_culture_get_name | CultureInfo.Name | P1 |
| il2cpp_culture_get_number_decimal_separator | NumberFormatInfo.NumberDecimalSeparator | P1 |
| il2cpp_culture_get_number_group_separator | NumberFormatInfo.NumberGroupSeparator | P2 |
| il2cpp_culture_get_number_negative_sign | NumberFormatInfo.NegativeSign | P2 |

#### System.Array（il2cpp_array_ext2.h，1）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| icall_array_find_index_int32 | Array.FindIndex(int[], int, int, int)（无谓词版） | P1 |

#### System.Type / 类型检查（il2cpp_typecheck.h / il2cpp_typeext.h，8）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| icall_type_is_instance | is 运算符运行时判定 | P1 |
| icall_type_cast | as 运算符运行时转换（失败 null） | P1 |
| icall_type_is_subclass_of | Type.IsSubclassOf(Type) | P2 |
| icall_type_equals | Type == Type（引用/等价比较） | P2 |
| il2cpp_type_get_typecode | Type.GetTypeCode(Type) | P2 |
| il2cpp_type_get_full_name | Type.FullName | P2 |
| il2cpp_type_get_type_from_handle | Type.GetTypeFromHandle(RuntimeTypeHandle) | P2 |
| il2cpp_type_is_assignable_from | Type.IsAssignableFrom(Type) | P2 |

### P2 中：扩展域 / 较少用 API

#### GC 扩展（il2cpp_gc_ext.h / il2cpp_gc.h 的 icall 面，4）
| icall | .NET 签名 | 优先级 | 接线注意 |
|---|---|---|---|
| icall_gc_register_finalizer | 对象 finalizer 注册 | P2 | 需要 delegate 回调 |
| icall_gc_finalize_trigger | GC 终结器触发 | P2 | 内部 |
| icall_gc_weak_ref_add / _remove | WeakReference 支持（slot 注册） | P2 | 需要对象模型支持 |

#### Delegate 扩展（il2cpp_delegate_ext.h，3）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| icall_delegate_get_fn_ptr | Delegate.GetFunctionPointer() | P2 |
| icall_delegate_to_callback | Delegate 转 C 回调（P/Invoke 场景） | P2 |
| icall_multicast_invoke_i4 | MulticastDelegate.Invoke(int,int,int,int) 快速路径 | P3 |

#### Marshal（il2cpp_marshal_ext.h / il2cpp_runtime.h marshal 组，11）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| icall_marshal_alloc_hglobal | Marshal.AllocHGlobal(int) | P2 |
| icall_marshal_free_hglobal | Marshal.FreeHGlobal(IntPtr) | P2 |
| icall_marshal_alloc_struct | Marshal.AllocHGlobal(struct 尺寸) | P2 |
| icall_marshal_free_struct | Marshal.FreeHGlobal(struct 指针) | P2 |
| icall_marshal_size_of_struct | Marshal.SizeOf(struct)（字段表版） | P2 |
| icall_marshal_copy_to_unmanaged | Marshal.Copy(byte[], int, IntPtr, int) | P2 |
| icall_marshal_copy_from_unmanaged | Marshal.Copy(IntPtr, byte[], int, int) | P2 |
| icall_marshal_struct_to_int32_array | struct → int[]（字段访问桥） | P3 |
| icall_marshal_int32_array_to_struct | int[] → struct | P3 |
| icall_marshal_read_int32_field | struct 字段读取（按索引） | P3 |
| icall_marshal_write_int32_field | struct 字段写入 | P3 |

#### P/Invoke 扩展（il2cpp_pinvoke.h / il2cpp_pinvoke_ext.h，7）
| icall | .NET 签名 | 优先级 | 接线注意 |
|---|---|---|---|
| il2cpp_pinvoke_resolve | DllImport 动态解析 | P1 | **已查：转译器零引用**（全 il2cppc 词边界验证）——若转译器只发静态绑定则此函数为 runtime 自用面，接线前先确认 P/Invoke 发射路径 |
| il2cpp_pinvoke_string_to_utf8 / _from_utf8 | Ansi 字符串封送 | P2 | 转译器用 `_alloc` 变体（已接线），原生版供 runtime 自用 |
| il2cpp_pinvoke_get_last_error | Marshal.GetLastWin32Error() | P2 | |
| il2cpp_pinvoke_clear_last_error | SetLastError 清除 | P2 | |
| il2cpp_pinvoke_register_callback | 回调注册（JS/宿主场景） | P3 | |
| il2cpp_pinvoke_find_callback | 回调查找 | P3 | |

#### Object 装箱/等值（il2cpp_boxing_ext.h，2）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| icall_boxed_equals | object.Equals（装箱值比较，ext2 版已接线） | P2 |
| icall_unbox_int32 | 拆箱 int32（转译器用 `il2cpp_unbox_int32`——il2cpp_runtime.h 版已接线） | P3（重复面） |

#### 异常 throw 特化（il2cpp_exception_ext.h + il2cpp_string_ext.h，12）
| icall | .NET 签名 | 优先级 | 接线注意 |
|---|---|---|---|
| il2cpp_throw_overflow_exception | throw OverflowException | P1 | CodeGenerator 目前统一 `il2cpp_throw_new(chain)`——特化面可提精度 |
| il2cpp_throw_argument_exception | throw ArgumentException | P1 | 同上 |
| il2cpp_throw_argument_null_exception | throw ArgumentNullException | P1 | 同上 |
| il2cpp_throw_argument_out_of_range_exception | throw ArgumentOutOfRangeException | P1 | 同上 |
| il2cpp_throw_null_reference_exception | throw NullReferenceException | P1 | 同上 |
| il2cpp_throw_index_out_of_range_exception | throw IndexOutOfRangeException | P1 | 同上 |
| il2cpp_throw_divide_by_zero_exception | throw DivideByZeroException | P2 | 同上 |
| il2cpp_throw_format_exception | throw FormatException | P2 | 同上 |
| il2cpp_throw_invalid_operation_exception | throw InvalidOperationException | P2 | 同上 |
| il2cpp_throw_key_not_found_exception | throw KeyNotFoundException | P2 | 同上 |
| il2cpp_throw_not_supported_exception | throw NotSupportedException | P2 | 同上 |
| il2cpp_throw_argument_out_of_range | throw ArgumentOutOfRangeException（string_ext.h 旧面） | P3 | 与上面重复，确认旧面废弃后删 |

#### System.Threading（il2cpp_thread.h，2）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| icall_thread_get_priority | Thread.Priority | P2 |
| icall_thread_sleep_zero | Thread.Sleep(0)（让出） | P2 |

#### Attribute（attribute.h，2）
| icall | .NET 签名 | 优先级 |
|---|---|---|
| il2cpp_attribute_register | Attribute 注册（元数据注入） | P3 |
| il2cpp_attribute_get_custom | Attribute.GetCustomAttribute | P3 |

### P3 低：runtime 内部 API / 诊断（通常**不需要**转译器接线）

| 域 | 函数 | 说明 |
|---|---|---|
| il2cpp_gc.h（14） | il2cpp_gc_init / _alloc / _alloc_size / _collect / _get_allocated_bytes / _get_generation / _get_total_memory / _add_static_root / _remove_static_root / _add_weak_ref / _remove_weak_ref / _register_finalizer / _run_finalizers / _is_card_marked | 运行时基础设施，由 entry.cpp / 其他 .cpp 自调用。转译器接的是 gc_api.h 的 icall_gc_*（已接线 7 个）。**勿接线**（gc_api 面已覆盖 BCL 映射）。il2cpp_gc_collect 与 icall_gc_collect 重复面，确认后者为 BCL 入口 |
| il2cpp_allocator.h（6） | il2cpp_allocator / _set / _alloc_fast / _free_fast / _alloc_stats / _alloc_bucket_hit_rate | 分配器插件表（C++ 内部）。**勿接线** |
| il2cpp_frame.h（4） | il2cpp_frame_pop / _top / _balance | **例外**：CodeGenerator.cs:1293 已在 prologue 生成 `il2cpp_frame_push(&__frame)`，但 frame_pop 未接线 → 帧配对断裂（1:1 违规，泄漏/GC 扫描隐患）。frame_pop 应 **P1** 接线！frame_top/balance 为诊断，勿接线 |
| debugext.h（4） | il2cpp_debug_assert / _fail / _write_line / il2cpp_trace_write | 诊断面，宿主可接可不接 |
| il2cpp_string.h（4） | il2cpp_string_class / _new / _new_from_cs / _new_from_utf8 | runtime 自用创建面；转译器用 il2cpp_string_new（CodeGenerator 生成路径）——注意：il2cpp_string_class 无引用但 object_new 依赖 class 描述符，若 CodeGenerator 的 string 构造走 class 则需接 |
| il2cpp_runtime.h（6） | il2cpp_class_by_name / _class_is_ref_field | 宿主/运行时内部 |
| attribute.h（2） | 见上 | 反射里程碑 |
| il2cpp_conv.h（9） | icall_conv_bool_i4 / _d_i4 / _d_i8 / _i4_u4 / _i8_i4 / _i8_u8 / _ovf_i8_i4 / _u4_i4 / _u8_i8 | 转换溢出/无符号变体——CodeGenerator 用 conv.ovf 直接发射或 icall_conv_*（已接线 4 个），这 9 个是补充面。**接线前先确认 CodeGenerator conv 路径**，多数可 P3 |
| il2cpp_metadata.h / il2cpp_object.h / il2cpp_typecheck.h 基础面 | — | 无 icall 声明（内部） |

---

## 施工建议（按批次）

1. **批次 1（P1 纯增量，无新机制）**：Math Max/Min/Clamp、string.Concat 3/4、Join(char)、int.ToString("X")、Stopwatch 5、Interlocked 12、IntPtr 6、CultureInfo 6、throw 特化 4-6 个——全部是现有 I() 模式可直接加
2. **批次 2（P1 需对象模型）**：Queue/Stack 14（hashtable 模式照搬）、Stream 14（il2cpp_stream_t 句柄模型）、Encoding 5（size-query 双调模式）
3. **批次 3（P1 需 out 参数）**：TryParse 6——卡 M 阶段 ref/out，先 loud 拒绝保持现状或实现 out
4. **批次 4（P2）**：Delegate 3、GC finalizer/weakref 4、Marshal 11、Type 8、Array.FindIndex
5. **独立修复（P1 帧配对）**：`il2cpp_frame_pop`——CodeGenerator.cs:1293 生成 push 无 pop，帧不配对

## 已知但未接线（loud 拒绝消息，供接线代理参考）

| API | 现状 |
|---|---|
| Console.IsInputRedirected | 拒绝：runtime 无 icall |
| Directory.Delete | 拒绝：runtime 无 icall（只有 il2cpp_file_* 面） |
| DateTime.TotalMinutes/TotalHours | runtime 无 icall（timecase 不可达） |
| DayOfWeek 枚举名打印 | runtime 无支持 |
| StringBuilder.Capacity | 字段读取路径（fcap），无需函数 |
