# r77 no-matching-function (nmf) analysis

- Total: 4160
- Unique: 1349

## Top 10

### System_IntPtr_op_Explicit_b87f01d65c5fb32c -- 69x

- E:\il2cpp\out\mscorlib_gen_r77\src\System_ArgIterator.cpp:292
  ```
      t1 = System_IntPtr_op_Explicit_b87f01d65c5fb32c(t0);                                          // IL call @0x0006 -- store call result into t1
  ```
- E:\il2cpp\out\mscorlib_gen_r77\src\System_Diagnostics_Tracing_DataCollector.cpp:249
  ```
      t0 = System_IntPtr_op_Explicit_b87f01d65c5fb32c(loc1);                                        // IL call @0x0070 -- store call result into t0
  ```

### System_Tuple_8_int_get_Rest_b3ab99604a10375a -- 56x

- E:\il2cpp\out\mscorlib_gen_r77\src\System_TupleExtensions.cpp:559
  ```
      t1 = System_Tuple_8_int_get_Rest_b3ab99604a10375a(t0);                                        // IL callvirt @0x00DE -- store evaluated value into t1
  ```
- E:\il2cpp\out\mscorlib_gen_r77\src\System_TupleExtensions.cpp:605
  ```
      t1 = System_Tuple_8_int_get_Rest_b3ab99604a10375a(t0);                                        // IL callvirt @0x00DE -- store evaluated value into t1
  ```

### System_Threading_Volatile_Read_c71f9240faef1dff -- 49x

- E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2__GetEnumerator_d__34_int_int.cpp:164
  ```
      il2cpp::il2cpp_write_barrier(reinterpret_cast<void**>(&((reinterpret_cast<System_Collections_Concurrent_ConcurrentDictionary_2__GetEnumerator_d__34_int_int*>(il2cpp_null_check(static_cast<Il2CppObject*>(arg0)))))->_current_5__4), static_cast<Il2CppObject*>(System_Threading_Volatile_Read_c71f9240faef1dff(t0))); // IL stfld @0x0054 -- write barrier before reference-field store
  ```
- E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2__GetEnumerator_d__34_int_int.cpp:165
  ```
      ((reinterpret_cast<System_Collections_Concurrent_ConcurrentDictionary_2__GetEnumerator_d__34_int_int*>(il2cpp_null_check(static_cast<Il2CppObject*>(arg0)))))->_current_5__4 = static_cast<Il2CppObject*>(System_Threading_Volatile_Read_c71f9240faef1dff(t0)); // IL stfld @0x0054 -- null-checked store into instance field System.Collections.Concurrent.ConcurrentDictionary`2+<GetEnumerator>d__34_int_int._current_5__4
  ```

### System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int_get_Length_aa95fb2d151867d3 -- 49x

- E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Cryptography_X509Certificates_AlgorithmIdentifierAsn.cpp:275
  ```
      t3 = System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int_get_Length_aa95fb2d151867d3((&loc3)); // IL call @0x005A -- store call result into t3
  ```
- E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Cryptography_X509Certificates_AlgorithmIdentifierAsn.cpp:325
  ```
      if(System_Security_Cryptography_X509Certificates_ReadOnlySpan_1_int_get_Length_aa95fb2d151867d3((&loc0)) == 2LL) goto L_39; // IL beq.s @0x0023 -- jump when condition evaluates to true to L_39
  ```

### System_ArraySegment_1_int_get_Offset_8be8b539a8995ba2 -- 42x

- E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Cryptography_X509Certificates_IncrementalHash.cpp:263
  ```
      t1 = System_ArraySegment_1_int_get_Offset_8be8b539a8995ba2((&loc0));                          // IL call @0x0017 -- store call result into t1
  ```
- E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_int.cpp:273
  ```
      t1 = System_ArraySegment_1_int_get_Offset_8be8b539a8995ba2((&loc0));                          // IL call @0x0021 -- store call result into t1
  ```

### System_ArraySegment_1_int_get_Array_aaceee58dbd184e0 -- 42x

- E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Cryptography_X509Certificates_IncrementalHash.cpp:262
  ```
      t0 = reinterpret_cast<Il2CppObject*>(System_ArraySegment_1_int_get_Array_aaceee58dbd184e0((&loc0))); // IL call @0x0010 -- store call result into t0
  ```
- E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_int.cpp:271
  ```
      t0 = reinterpret_cast<Il2CppObject*>(System_ArraySegment_1_int_get_Array_aaceee58dbd184e0((&loc0))); // IL call @0x0013 -- store call result into t0
  ```

### System_Threading_Volatile_Write_b1c41837d78588c7 -- 40x

- E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2_int_int.cpp:824
  ```
          System_Threading_Volatile_Write_b1c41837d78588c7(reinterpret_cast<System_Collections_Concurrent_ConcurrentDictionary_2_Node_int_int*>(il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>(reinterpret_cast<Il2CppObject*>(((reinterpret_cast<System_Collections_Concurrent_ConcurrentDictionary_2_Tables_int_int*>(il2cpp_null_check(static_cast<Il2CppObject*>(loc0)))))->m_buckets)), static_cast<int32_t>(loc2))), reinterpret_cast<Il2CppObject*>(((reinterpret_cast<System_Collections_Concurrent_ConcurrentDictionary_2_Node_int_int*>(il2cpp_null_check(static_cast<Il2CppObject*>(loc7)))))->m_next)); // IL call @0x00BD -- invoke the transpiled function System_Threading_Volatile_Write_b1c41837d78588c7
  ```
- E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Concurrent_ConcurrentDictionary_2_int_int.cpp:1449
  ```
          System_Threading_Volatile_Write_b1c41837d78588c7(t2, t3);                                 // IL call @0x0139 -- invoke the transpiled function System_Threading_Volatile_Write_b1c41837d78588c7
  ```

### System_ArraySegment_1_int_get_Count_1a39335e3a4abf36 -- 39x

- E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Cryptography_X509Certificates_IncrementalHash.cpp:264
  ```
      t2 = System_ArraySegment_1_int_get_Count_1a39335e3a4abf36((&loc0));                           // IL call @0x001E -- store call result into t2
  ```
- E:\il2cpp\out\mscorlib_gen_r77\src\System_Security_Cryptography_X509Certificates_ReadOnlyMemory_1_int.cpp:197
  ```
      return (System_ArraySegment_1_int_get_Count_1a39335e3a4abf36((&loc0)) == 0LL);                // IL ret @0x0011 -- exit method returning the computed value
  ```

### System_Runtime_InteropServices_SafeBuffer_AcquirePointer_0d7dbe4b8cbf2e0e -- 39x

- E:\il2cpp\out\mscorlib_gen_r77\src\System_IO_UnmanagedMemoryAccessor.cpp:281
  ```
          System_Runtime_InteropServices_SafeBuffer_AcquirePointer_0d7dbe4b8cbf2e0e(arg1, (&loc0)); // IL callvirt @0x0095 -- invoke virtual method via vtable slot (void)
  ```
- E:\il2cpp\out\mscorlib_gen_r77\src\System_IO_UnmanagedMemoryAccessor.cpp:430
  ```
          System_Runtime_InteropServices_SafeBuffer_AcquirePointer_0d7dbe4b8cbf2e0e(reinterpret_cast<Il2CppObject*>(((reinterpret_cast<System_IO_UnmanagedMemoryAccessor*>(il2cpp_null_check(static_cast<Il2CppObject*>(arg0)))))->_buffer), (&loc2)); // IL callvirt @0x001A -- invoke virtual method via vtable slot (void)
  ```

### System_Runtime_CompilerServices_ConditionalWeakTable_2_int_Add_4c5943bc1de1c7d1 -- 37x

- E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Generic_Dictionary_2_int_int.cpp:448
  ```
      System_Runtime_CompilerServices_ConditionalWeakTable_2_int_Add_4c5943bc1de1c7d1(t0, arg0, arg1); // IL callvirt @0x000D -- invoke virtual method via vtable slot (void)
  ```
- E:\il2cpp\out\mscorlib_gen_r77\src\System_Collections_Generic_Dictionary_2_int_object.cpp:448
  ```
      System_Runtime_CompilerServices_ConditionalWeakTable_2_int_Add_4c5943bc1de1c7d1(t0, arg0, arg1); // IL callvirt @0x000D -- invoke virtual method via vtable slot (void)
  ```

## Patterns
1. Generic instantiation mismatch: VTable generic arg chain broken
2. Volatile read/write address type: ldloca cast to Il2CppObject** instead of T&
3. byref params: int* passed where int& expected
