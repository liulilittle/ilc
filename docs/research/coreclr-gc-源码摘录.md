# coreclr GC 源码摘录（移植参考）

- 来源：dotnet/runtime main 分支 src/coreclr/gc/（MIT License）
- 用途：关键函数/结构摘录，行号对应原文件；完整源码已本地持久化（`E:\il2cpp\reference\coreclr\src\coreclr\gc\`，63 文件，2026-08-05 补拉；原 temp 缓存已废弃）
- 摘录原则：只录与移植点直接相关的函数/结构，行号对应原文件

---

## 1. 代与段模型（gc.h）

```cpp
// gc.h:109-133
enum gc_generation_num
{
    // small object heap includes generations [0-2], which are "generations" in the general sense.
    soh_gen0 = 0,
    soh_gen1 = 1,
    soh_gen2 = 2,
    max_generation = soh_gen2,

    // large object heap, technically not a generation, but it is convenient to represent it as such
    loh_generation = 3,

    // pinned heap, a separate generation for the same reasons as loh
    poh_generation = 4,

    uoh_start_generation = loh_generation,

    // number of ephemeral generations
    ephemeral_generation_count = max_generation,
    total_generation_count = poh_generation + 1,
    uoh_generation_count = total_generation_count - uoh_start_generation
};
```

```cpp
// gcinterface.h:201-225 —— 每线程分配上下文（分配快路径核心）
struct gc_alloc_context
{
    uint8_t*       alloc_ptr;
    uint8_t*       alloc_limit;
    int64_t        alloc_bytes;      // Number of bytes allocated on SOH by this context
    int64_t        alloc_bytes_uoh;  // Number of bytes allocated not on SOH by this context
    void*          gc_reserved_1;
    void*          gc_reserved_2;
    int            alloc_count;
    void init() { ... }
};
```

---

## 2. 卡表常量与换算（gcpriv.h）

```cpp
// gcpriv.h:6765-6795
#define GC_PAGE_SIZE 0x1000
#define card_word_width ((size_t)32)

// The value of card_size is determined empirically according to the average size of an object
// In the code we also rely on the assumption that one card_table entry (uint32_t) covers an entire os page
#if defined (HOST_64BIT)
#define card_size ((size_t)(2*GC_PAGE_SIZE/card_word_width))   // 64位: 256 字节/卡
#else
#define card_size ((size_t)(GC_PAGE_SIZE/card_word_width))     // 32位: 128 字节/卡
#endif // HOST_64BIT

inline size_t card_word (size_t card)   { return card / card_word_width; }   // 卡 -> word 索引
inline unsigned card_bit (size_t card)  { return (unsigned)(card % card_word_width); }  // 卡 -> word 内位
inline size_t gcard_of (uint8_t* object){ return (size_t)(object) / card_size; }  // 地址 -> 卡
```

---

## 3. 卡清除 = 老化（card_table.cpp）

```cpp
// card_table.cpp:1296-1346 —— 区间清卡（不是全表清）
//Clear the cards [start_card, end_card[
void gc_heap::clear_cards (size_t start_card, size_t end_card)
{
    if (start_card < end_card)
    {
        size_t start_word = card_word (start_card);
        size_t end_word = card_word (end_card);
        if (start_word < end_word)
        {
            unsigned bits = card_bit (start_card);
            card_table [start_word] &= lowbits (~0, bits);
            for (size_t i = start_word+1; i < end_word; i++)
                card_table [i] = 0;
            bits = card_bit (end_card);
            if (bits != 0)
                card_table [end_word] &= highbits (~0, bits);
        }
        else
        {
            card_table [start_word] &= (lowbits (~0, card_bit (start_card)) |
                                        highbits (~0, card_bit (end_card)));
        }
    }
}

void gc_heap::clear_card_for_addresses (uint8_t* start_address, uint8_t* end_address)
{
    size_t   start_card = card_of (align_on_card (start_address));
    size_t   end_card = card_of (align_lower_card (end_address));
    clear_cards (start_card, end_card);
}
```

---

## 4. 卡驱动的老代→短暂代引用发现（mark_phase.cpp）

```cpp
// mark_phase.cpp:3579-3698（节选）—— 遍历老代段，扫置位卡，清无引用的卡
void gc_heap::mark_through_cards_for_segments (card_fn fn, BOOL relocating CARD_MARKING_STEALING_ARG(gc_heap* hpt))
{
    ...
    heap_segment* seg = heap_segment_rw (generation_start_segment (oldest_gen));  // 从最老代开始
    ...
    while (1)
    {
        if (card_of(last_object) > card)
        {
            if (cg_pointers_found == 0)
            {
                uint8_t* last_object_processed = last_object;
                // 该卡范围内没有发现指向 ephemeral 的引用 → 清卡（老化！）
                size_t card_last_obj = card_of (last_object_processed);
                clear_cards(card, card_last_obj);
                ...
            }
            n_eph += cg_pointers_found;
            cg_pointers_found = 0;
            card = card_of (last_object);
        }
        if (card >= end_card)
        {
            foundp = find_card(card_table, card, card_word_end, end_card);  // 找下一个置位卡
            ...
        }
        ...
    }
}
```

---

## 5. mark_phase 主流程（弱引用/finalization 顺序）

```cpp
// mark_phase.cpp:2763 mark_phase（节选）
    // 1. 根
    GCScan::GcScanRoots(...);        // 线程栈根
    GCScan::GcScanHandles(...);      // 句柄表强根+固定根
    // 2. 老代卡扫描（见 §4）……
    // 3. 短弱引用：未晋升目标置 null
    GCScan::GcShortWeakPtrScan (condemned_gen_number, max_generation,&sc);   // :3295
    // 4. 终结器：不可达但有终结器 → 晋升（复活）+ 其子图再标记
    finalize_queue->ScanForFinalization (GCHeap::Promote, condemned_gen_number, __this);  // :3319
    drain_mark_queue();                                                       // :3320
    scan_dependent_handles(condemned_gen_number, &sc, false);                // :3326（CWT）
    // 5. 长弱引用
    GCScan::GcWeakPtrScan (condemned_gen_number, max_generation, &sc);       // :3360
```

---

## 6. finalization 双区队列（finalization.cpp）

```cpp
// finalization.cpp:81-118（注释节选）—— 队列设计要点
// registered 与 unreachable 是同一数组的两个逻辑区段：
// 注册新终结器 = 从 unreachable 端取槽 -> 边界外扩（O(1)，锁只护短临界区）
// GC 时不可达对象从 registered 旋转到 unreachable（此时无其他线程操作，无需锁）
// 终结器线程从 unreachable 端取对象执行 Finalize()

// finalization.cpp:36-69 Initialize —— 数组 = 100 个 Object* 起步，动态增长
const int INITIAL_FINALIZER_ARRAY_SIZE = 100;
m_Array = new (nothrow)(Object*[INITIAL_FINALIZER_ARRAY_SIZE]);
m_EndArray = &m_Array[INITIAL_FINALIZER_ARRAY_SIZE];
for (int i = 0; i < FreeList; i++)      // FreeList 个逻辑区段
    SegQueueLimit (i) = m_Array;
```

---

## 7. 桶化空闲列表（gcpriv.h allocator）

```cpp
// gcpriv.h:763-803 —— 桶结构
#define MAX_SOH_BUCKET_COUNT (13)  // SOH 代最大桶数
#define MAX_BUCKET_COUNT (20)
class alloc_list
{
    uint8_t* head;
    uint8_t* tail;
    size_t damage_count;
    ...
};

// gcpriv.h:835-879 —— 选桶 O(1)：BitScanReverse
class allocator
{
    int first_bucket_bits;
    unsigned int num_buckets;
    ...
    unsigned int first_suitable_bucket (size_t size)
    {
        // sizes taking first_bucket_bits or less are mapped to bucket 0
        // others are mapped to buckets 0, 1, 2 respectively
        size = (size >> first_bucket_bits) | 1;
        DWORD highest_set_bit_index;
        BitScanReverse64(&highest_set_bit_index, size);   // 64位
        return min ((unsigned int)highest_set_bit_index, (num_buckets - 1));
    }
};
```

---

## 8. GCDesc 引用描述（gcdesc.h）

```cpp
// gcdesc.h:64-114 —— 系列：一段连续引用 + 起始偏移
class CGCDescSeries
{
public:
    union
    {
        size_t seriessize;              // 系列长度（字节，见下）
        val_serie_item val_serie[1];    // 值类型数组的 {nptrs, skip} 编码
    };
    size_t startoffset;                 // 系列起点偏移
    size_t GetSeriesSize () { return seriessize; }
    size_t GetSeriesOffset () { return startoffset; }
};

// gcdesc.h:121-263 —— 多个系列的组合；负 NumSeries = 值类型数组重复模式
class CGCDesc
{
public:
    static size_t ComputeSize (size_t NumSeries)        { return sizeof(size_t) + NumSeries*sizeof(CGCDescSeries); }
    static size_t ComputeSizeRepeating (size_t NumSeries) { return sizeof(size_t) + sizeof(CGCDescSeries) + (NumSeries-1)*sizeof(val_serie_item); }
    static void Init (void* mem, size_t NumSeries)      { *((size_t*)mem-1) = NumSeries; }
    static void InitValueClassSeries (void* mem, size_t NumSeries) { *((ptrdiff_t*)mem-1) = -((ptrdiff_t)NumSeries); }
    size_t GetNumSeries () { return *(PTR_size_t(PTR_CGCDesc(this))-1); }
    // 系列存储在以 MethodTable 为终点"向后生长"的内存里
};

// gcdesc.h:51-60 —— 值类型数组的重复单元
struct val_serie_item
{
    HALF_SIZE_T nptrs;   // 每元素引用数
    HALF_SIZE_T skip;    // 每元素跳过字节
};
```

---

## 9. 压缩决策（plan_phase.cpp）

```cpp
// plan_phase.cpp:7829 —— 空间足够则不压缩
bool gc_heap::decide_on_compaction_space()
{
    ... // 检查各代 free space 与阈值 min_reclaim_fragmentation_threshold
}

// plan_phase.cpp:1488 —— 碎片阈值（按堆数折算）
size_t gc_heap::min_reclaim_fragmentation_threshold (uint32_t num_heaps)
{
    ... // 默认 ~40-60% 存活率以下才考虑压缩等
}

// plan_phase.cpp:7884 —— 全 GC 是否值得压缩
bool gc_heap::is_full_compacting_gc_productive()
```

---

## 10. 压缩/重定位（relocate_compact.cpp）

```cpp
// relocate_compact.cpp:1427 relocate_phase —— 先改引用，后搬内存
//   - 根更新: Ref_UpdatePointers / Ref_UpdatePinnedPointers（gcscan.cpp:173-174）
//   - 存活对象引用字段: walk_relocation 按 brick 遍历 plug 更新
// relocate_compact.cpp:2024 compact_phase
//   - compact_plug (1755): 逐 plug gcmemcopy 顺序搬移
//   - pinned plug 跳过: pin_by_gc (465) / convert_to_pinned_plug (plan_phase.cpp:2459)
```

---

## 11. 段释放与延迟释放（regions_segments.cpp）

```cpp
// regions_segments.cpp:604-621 —— 空段返还 OS
void gc_heap::release_segment (heap_segment* sg)
{
    ...
    reduce_committed_bytes (sg, ...);
    virtual_free (sg, reserved_size, sg);   // VirtualFree / munmap
}

// regions_segments.cpp:1484 delay_free_segments —— 延迟释放（防抖动）
void gc_heap::delay_free_segments()
```

---

## 12. GCConfig 配置表（gcconfig.h:78-151 节选）

```cpp
INT_CONFIG   (SegmentSize,               "GCSegmentSize",             NULL,  0,     "Specifies the managed heap segment size")
INT_CONFIG   (Gen0Size,                  "GCgen0size",                NULL,  0,     "Specifies the smallest gen0 budget")
INT_CONFIG   (CompactRatio,              "GCCompactRatio",            NULL,  0,     "Specifies the ratio compacting GCs vs sweeping")
INT_CONFIG   (LOHCompactionMode,         "GCLOHCompact",              NULL,  0,     "Specifies the LOH compaction mode")
INT_CONFIG   (LOHThreshold,              "GCLOHThreshold",            "System.GC.LOHThreshold", LARGE_OBJECT_SIZE, "Specifies the size that will make objects go on LOH")
INT_CONFIG   (GCHeapHardLimit,           "GCHeapHardLimit",           "System.GC.HeapHardLimit", 0, "Specifies a hard limit for the GC heap")
INT_CONFIG   (GCWriteBarrier,            "GCWriteBarrier",            NULL,  0,     "Specifies whether GC should use more precise but slower write barrier")
INT_CONFIG   (ConserveMemory,            "GCConserveMemory",          "System.GC.ConserveMemory", 0, "Specifies how hard GC should try to conserve memory - values 0-9")
INT_CONFIG   (LatencyMode,               "GCLatencyMode",             NULL,  -1,    "Specifies the GC latency mode - batch, interactive or low latency")
// 全部声明见 gcconfig.h:78-151；运行时 Get* / Set* / 环境变量注入见 gcconfig.cpp:82-99
```

---

## 13. 句柄表根类型（gcscan.cpp 编排）

```cpp
// gcscan.cpp:162-178 —— 句柄表扫描两阶段分派
void GCScan::GcScanHandles (promote_func* fn, int condemned, int max_gen, ScanContext* sc)
{
    if (sc->promotion)   // mark 期
    {
        Ref_TracePinningRoots(condemned, max_gen, sc, fn);   // PINNED 先扫
        Ref_TraceNormalRoots(condemned, max_gen, sc, fn);    // STRONG
    }
    else                 // relocate 期
    {
        Ref_UpdatePointers(condemned, max_gen, sc, fn);
        Ref_UpdatePinnedPointers(condemned, max_gen, sc, fn);
        Ref_ScanDependentHandlesForRelocation(condemned, max_gen, sc, fn);
        Ref_ScanWeakInteriorPointersForRelocation(condemned, max_gen, sc, fn);
    }
}
// 句柄代龄: Ref_AgeHandles (gcscan.cpp:246) / Ref_RejuvenateHandles (gcscan.cpp:239)
```
