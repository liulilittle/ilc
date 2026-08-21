namespace Il2Cpp.Transpiler.Emit;

//------------------------------------------------------------------------------
// MethodMap.Marshal -- System.Runtime.InteropServices.Marshal static-method
// mappings (P0 sprint-worklist #28/#29: static/instance classification).
// Owner: System.Runtime.InteropServices.Marshal
//
// ROOT CAUSE (fixed here): the MemberRef fallback name heuristic
// (MethodRefResolver.cs) classifies a member as INSTANCE unless it is
// .ctor/.cctor/get_* -- every Marshal STATIC method (GetLastWin32Error,
// PtrToStringAnsi, ...) was therefore resolved as instance and StackFold
// tried to pop `this` off an empty stack ("resolved X as INSTANCE (pops
// this) but the stack is empty"). Wiring the domain pins IsInstance=false
// for every mapped member.
//
// ICALL SURFACE (declared in the generated include chain -- verified):
//   il2cpp_runtime.h -> il2cpp_pinvoke.h (transitively included in
//   generated.h) declares:
//     il2cpp::il2cpp_pinvoke_get_last_error        -- GetLastWin32Error
//     il2cpp::il2cpp_pinvoke_string_from_utf8      -- PtrToStringAnsi
//     il2cpp::runtime::icall_marshal_alloc_hglobal / free_hglobal /
//       copy_to_unmanaged / copy_from_unmanaged    -- AllocHGlobal /
//       FreeHGlobal / Copy (runtime.cpp, M4 Task 6)
//     il2cpp::runtime::icall_marshal_realloc_hglobal -- ReAllocHGlobal
//       (D-HG-1, 2026-08-12: marshalext.cpp + il2cpp_marshal_ext.h -- the
//       realloc half of the hglobal family, net48 semantics)
//
//   NOT declared in generated.h (il2cpp_marshal_ext2.h is absent from the
//   include list): il2cpp_marshal_set_last_win32_error / size_of /
//   offset_of / string_to_hglobal_ansi / string_to_hglobal_uni. Those map
//   to LOUD throws with precise wiring guidance (1 include line in
//   CodeGenerator's generated.h emission + this mapping) -- wired LOUD,
//   never silently emitted (a missing declaration is a C++ compile error
//   with no transpiler-side message).
//
// POINTER CARRIER (M-stage model): an IntPtr value rides in an intptr_t
// slot (the fold's "ptr" result kind for raw 0x17/0x18 returns -- see
// MethodMap.IntPtr.cs and StackFold UserCallResultKind). The pointer-typed
// icall parameters (PtrToStringAnsi's const char*, FreeHGlobal's void*,
// Copy's dest/src) are reached through EXPLICIT pointer casts carried by
// the arg-cast codes: "p" -> (const char*), "g" -> (uint8_t*) (implicit
// void* onward). The old "" casts passed the carrier straight into a
// pointer parameter (C2440 / invalid conversion on MSVC/MinGW -- the
// marshalcase cmake_build FAIL root cause, closed by the G3 fix). The
// runtime alloc icall returns intptr_t (il2cpp_runtime.h) so the
// "ptr"-kind assignment `intptr_t t = icall(...)` compiles without an
// emitter return cast. Inline chains (a pointer-returning call consumed
// directly by a pointer-taking icall) round-trip at the C++ level.
// Full-width P/Invoke return handling is the M4.5 work item
// (PinvokeReturnTypeCpp in CodeGenerator.cs) -- out of this file's domain.
//
// ABI: all mapped members are STATIC (IsInstance=false). ps bytes: 0x08=i4,
// 0x0E=string, 0x1D=SZARRAY (element byte consumed by RawSignature -- byte[]
// and int[] are indistinguishable in ps; the M4 copy icalls implement the
// int32 stride only, runtime.cpp).
//
// INTPTR FORMS (root cause #29): mscorlib declares IntPtr parameters as the
// RAW NATIVE-INT bytes 0x17 (ELEMENT_TYPE_I) / 0x18 (ELEMENT_TYPE_U) -- the
// real pinvokeext MemberRef blob for PtrToStringAnsi is [00,01,0E,18] (dumped
// from out/pinvokeext.exe). Roslyn can also emit IntPtr as VALUETYPE (0x11 +
// TypeHandle) for user-declared signatures. Every IntPtr param guard must
// accept all three; the original 0x11-only guard returned null on the real
// blob -> fallback name -> INSTANCE classification -> "pops this" (sprint
// worklist #29, StackFold.cs:1454).
//------------------------------------------------------------------------------
public static partial class MethodRefResolver
{
    private static ResolvedMethod? TryMapMarshal(string owner, string name, byte[] ps, int count, byte[]? genArgs,
        string? receiverType, bool receiverIsAddress, string? arg0Type, string? arg1Type, string[]? genArgNames = null)
    {
        // COM interop surface (net48 inventory §4 line 971: "23 个 COM 相关
        // 建议 LOUD 拒绝"). The core RCW surface is now WIRED (com3, com.cpp):
        // GetActiveObject / GetIUnknownForObject / GetObjectForIUnknown /
        // ReleaseComObject (below) + IsComObject (marshal-b23) map to real
        // runtime icalls (il2cpp_com.h, namespace il2cpp::runtime). The
        // REMAINING members (CCW surface, type-library marshaling, WinRT,
        // the *Native/*Internal variants, the not-yet-wired FinalReleaseComObject)
        // still reject LOUD with the COM-domain message -- they MUST NOT fall
        // through to the generated-name path (that silently emits a call to an
        // undefined C++ function). The name set covers the public wrappers AND
        // their internal *Native/*Internal icall variants (the transpiler can
        // resolve either, since BclResolve keys on the MemberRef name the IL
        // emits).
        if (kComMarshalMembers.Contains(name))
        {
            throw new NotSupportedException(
                $"COM interop is not supported in ILC: {name} belongs to the COM interop domain " +
                "(RCW/CCW, IUnknown/IDispatch, type-library marshaling, VARIANT conversion) and ILC has no COM runtime");
        }
        switch (name)
        {
            // int GetLastWin32Error(): reads the SetLastError=true capture
            // slot (il2cpp_pinvoke.h:57 documents exactly this mapping).
            case "GetLastWin32Error":
                if (ps.Length != 0) return null;
                return I("il2cpp::il2cpp_pinvoke_get_last_error", 0, true, false, false);
            // void SetLastWin32Error(int): helper EXISTS in the runtime
            // (il2cpp_marshal_set_last_win32_error, marshalext2.cpp) but
            // il2cpp_marshal_ext2.h is NOT in the generated include chain.
            case "SetLastWin32Error":
                throw new NotSupportedException(
                    "SetLastWin32Error not wired: il2cpp_marshal_set_last_win32_error (il2cpp_marshal_ext2.h) is not declared in the generated include chain -- wiring requires adding '#include \"il2cpp_marshal_ext2.h\"' to CodeGenerator's generated.h emission (out of the MethodMap.Marshal file domain)");
            // string PtrToStringAnsi(IntPtr): the UTF-8 -> managed string
            // decode (il2cpp_pinvoke.h). The IntPtr arg rides in an int32
            // slot ("" cast = raw pass-through) -- see the carrier caveat.
            case "PtrToStringAnsi":
                if (ps.Length != 1 || !IsIntPtrByte(ps[0])) return null;
                // M4.5: "p" -- the IntPtr arg (an int32 slot or an intptr_t
                // P/Invoke-result temp) reaches the const char* param via an
                // explicit cast (EmitIcallCallArgs "p" code); the old "" cast
                // passed an int32_t straight into const char* (C2440 on MSVC).
                return I("il2cpp::il2cpp_pinvoke_string_from_utf8", 1, true, true, false, new[] { "p" });
            // string PtrToStringUni(IntPtr): WF-M1 (2026-08-07) -- the UTF-16
            // decode helper now exists (il2cpp_pinvoke_ext.h:134
            // il2cpp_pinvoke_string_from_utf16, wfm1 report §1). The IntPtr
            // carrier reaches the const char16_t* param via the "p16" cast
            // code (CG-FIX must add it to EmitIcallCallArgs -- the "p" cast
            // is const char* only; "p16" is its char16_t twin).
            case "PtrToStringUni":
                if (ps.Length != 1 || !IsIntPtrByte(ps[0])) return null;
                return I("il2cpp::il2cpp_pinvoke_string_from_utf16", 1, true, true, false, new[] { "p16" });
            // IntPtr AllocHGlobal(int) / AllocHGlobal(IntPtr): native heap
            // allocation (calloc-backed; the IntPtr overload's byte count is
            // the same int32 slot). void* return -- see the carrier caveat.
            case "AllocHGlobal":
                if (ps.Length != 1 || (ps[0] != 0x08 && !IsIntPtrByte(ps[0]))) return null;
                return I("il2cpp::runtime::icall_marshal_alloc_hglobal", 1, true, false, false, new[] { "i" });
            // void FreeHGlobal(IntPtr): release an AllocHGlobal buffer
            // (null is a no-op). void* arg -- see the carrier caveat.
            case "FreeHGlobal":
                if (ps.Length != 1 || !IsIntPtrByte(ps[0])) return null;
                // "g" -- the IntPtr carrier (an intptr_t slot or an int32
                // temp) reaches the void* param via an explicit byte-pointer
                // cast ((uint8_t*) -- C-style, implicit void* onward); the
                // old "" cast passed intptr_t straight into void* (C2440).
                return I("il2cpp::runtime::icall_marshal_free_hglobal", 1, false, false, false, new[] { "g" });
            // IntPtr ReAllocHGlobal(IntPtr pv, IntPtr cb): D-HG-1 (2026-08-12)
            // -- reallocate an hglobal-family buffer (net48 realloc semantics:
            // the old pointer is INVALID, a NEW pointer is returned, and the
            // first min(oldSize, cb) bytes are preserved; realloc failure
            // throws OutOfMemoryException). Both args are IntPtr carriers; the
            // runtime icall takes intptr_t for both (the "n" native-int cast:
            // an int32 carrier sign-extends to pointer width, an intptr_t
            // temp passes through unchanged -- the carrier caveat).
            case "ReAllocHGlobal":
                if (ps.Length != 2 || !IsIntPtrByte(ps[0]) || !IsIntPtrByte(ps[1])) return null;
                return I("il2cpp::runtime::icall_marshal_realloc_hglobal", 2, true, false, false,
                    new[] { "n", "n" }, resultKind: "ptr");
            // Copy: two 4-arg directions dispatch on arg order (array first
            // = managed -> native, IntPtr first = native -> managed). The M4
            // icalls implement the int32 stride (runtime.cpp) -- the array
            // ELEMENT kind is not distinguishable in ps (0x1D for every
            // array), the element-type matrix is a runtime contract note.
            case "Copy":
                if (ps.Length != 4) return null;
                if (ps[0] == 0x1D)
                    return I("il2cpp::runtime::icall_marshal_copy_to_unmanaged", 4, false, false, false,
                        new[] { "a", "i", "g", "i" });
                if (IsIntPtrByte(ps[0]))
                    return I("il2cpp::runtime::icall_marshal_copy_from_unmanaged", 4, false, false, false,
                        new[] { "g", "a", "i", "i" });
                return null;
            // string StringToHGlobalAnsi(string): the ANSI helper still
            // requires il2cpp_marshal_ext2.h in the generated include chain
            // (out of this file's domain) -- LOUD stays.
            case "StringToHGlobalAnsi":
                if (ps.Length != 1 || ps[0] != 0x0E) return null;
                throw new NotSupportedException(
                    "StringToHGlobalAnsi not wired: il2cpp_marshal_string_to_hglobal_ansi (il2cpp_marshal_ext2.h) is not declared in the generated include chain -- wiring requires adding '#include \"il2cpp_marshal_ext2.h\"' to CodeGenerator's generated.h emission (out of the MethodMap.Marshal file domain)");
            // IntPtr StringToHGlobalUni(string): WF-M1 (2026-08-07) -- the
            // hglobal icall form now exists (il2cpp_pinvoke_ext.h:177
            // il2cpp_pinvoke_string_to_utf16_hglobal, wfm1 report §1): returns
            // intptr_t (IntPtr.Zero on null) -- "ptr" result kind, so the
            // result temp is intptr_t with no emitter return cast. The string
            // arg reaches the Il2CppString* param via the "s" cast. The
            // buffer is freed by the managed FreeHGlobal ->
            // icall_marshal_free_hglobal (std::free pairing).
            case "StringToHGlobalUni":
                if (ps.Length != 1 || ps[0] != 0x0E) return null;
                return I("il2cpp::il2cpp_pinvoke_string_to_utf16_hglobal", 1, true, false, false,
                    new[] { "s" }, "ptr");
            // int SizeOf<T>() / SizeOf(Type) / SizeOf(object): D-MAR-1 wires
            // the REGISTERED STRUCT FAMILY (System.Drawing.Rectangle/Point/
            // System.Windows.Forms.Message -- the native layout registry,
            // il2cpp_pinvoke_struct.h). The layout table is the runtime's
            // single authority (marshalext3.cpp icalls):
            //   * SizeOf(Type)      -- the ILC Type VALUE is the type's
            //     FULL-NAME STRING (typeof(X) folds to it -- StackFold's
            //     GetTypeFromHandle fold), passed as the name string to
            //     il2cpp_marshal_size_of_struct_named (kind_by_name lookup).
            //   * SizeOf<T>()       -- the MethodSpec's VALUETYPE arg name is
            //     captured from the signature (genArgNames, MethodRefResolver
            //     GenericArgValueTypeNames) and selects the per-struct icall
            //     (point=8 / rect=16 / msg=48-or-28 -- the pointer-width-
            //     dependent MSG size stays runtime-computed).
            //   * SizeOf(object)    -- the boxed primitive path (marshalext4
            //     icall_marshal_size_of_object, box class name -> net48
            //     TypeCode -> size). A BOXED REGISTERED STRUCT is unreachable
            //     (ILC has no struct boxing -- EmitTask9Call LOUD).
            //   * SizeOf<T>(T)      -- a managed struct VALUE parameter needs
            //     a struct-typed slot (no generated C++ type for the BCL
            //     registered structs) -- LOUD with the extension point.
            case "SizeOf":
                // SizeOf(Type): the IL param is CLASS System.Type (0x12 /
                // 0x40), but the ARGUMENT is the folded typeof(X) name STRING
                // (StackFold's GetTypeFromHandle fold) -- the runtime icall
                // takes the Il2CppString* ("s" cast), resolved through the
                // registry by name.
                if (ps.Length == 1 && ps[0] is 0x12 or 0x40)
                    return I("il2cpp::runtime::il2cpp_marshal_size_of_struct_named", 1, true, false, false, new[] { "s" });
                if (ps.Length == 1 && ps[0] == 0x1C)
                    return I("il2cpp::runtime::icall_marshal_size_of_object", 1, true, false, false, new[] { "" });
                if (ps.Length == 0 && genArgNames is { Length: 1 })
                {
                    var sfx = MarshalStructKindSuffix(genArgNames[0]);
                    if (sfx != null)
                        return I($"il2cpp::runtime::il2cpp_marshal_size_of_struct_{sfx}", 0, true, false, false);
                    throw new NotSupportedException($"SizeOf<{genArgNames[0]}> not wired: only the registered struct family (System.Drawing.Rectangle / System.Drawing.Point / System.Windows.Forms.Message) has a layout-registry SizeOf icall");
                }
                if (ps.Length == 1 && ps[0] == 0x13)
                    throw new NotSupportedException("SizeOf<T>(T structure) not wired: passing a managed struct VALUE requires a struct-typed slot and the registered BCL structs have no generated C++ type (CodeGenerator TypeCppName extension)");
                throw new NotSupportedException($"SizeOf with {ps.Length} parameter(s) not wired: the registered-struct family supports SizeOf<T>() (generic), SizeOf(Type) and SizeOf(object)");
            // int OffsetOf<T>(string) / OffsetOf(Type, string): D-MAR-1 wires
            // the registered struct family. The field name is matched against
            // the net48 FIELD names (Rectangle: X/Y/Width/Height at 0/4/8/12,
            // Point: X/Y at 0/4 -- the layout registry, field_offset by name);
            // an unknown field -> ArgumentException (net48). The MSG field set
            // is the Win32 tagMSG names (hwnd/message/wParam/lParam/time/pt) --
            // net48 System.Windows.Forms.Message substitutes result for
            // time+pt, a documented divergence (SizeOf/OffsetOf on Message are
            // NOT net48-faithful; rect/point match byte-for-byte).
            case "OffsetOf":
                // OffsetOf(Type, string): ps[0] is CLASS System.Type (0x12 /
                // 0x40 -- the folded typeof name STRING is the runtime value),
                // ps[1] the field-name string literal.
                if (ps.Length == 2 && ps[0] is 0x12 or 0x40 && ps[1] == 0x0E)
                    return I("il2cpp::runtime::il2cpp_marshal_offset_of_struct_named", 2, true, false, false, new[] { "s", "s" });
                if (ps.Length == 1 && ps[0] == 0x0E && genArgNames is { Length: 1 })
                {
                    var sfx = MarshalStructKindSuffix(genArgNames[0]);
                    if (sfx != null)
                        return I($"il2cpp::runtime::il2cpp_marshal_offset_of_struct_{sfx}", 1, true, false, false, new[] { "s" });
                    throw new NotSupportedException($"OffsetOf<{genArgNames[0]}> not wired: only the registered struct family has a layout-registry OffsetOf icall");
                }
                throw new NotSupportedException($"OffsetOf with {ps.Length} parameter(s) not wired: the registered-struct family supports OffsetOf<T>(string) and OffsetOf(Type, string)");
            // void PtrToStructure/StructureToPtr: the byte semantics ARE
            // implemented in the runtime (il2cpp_marshal_struct_*_load/store,
            // marshalext3.cpp -- size-checked blittable memcpy +
            // ArgumentNullException, unit-tested in test_marshalstruct.cpp),
            // but the transpiler cannot emit them: PtrToStructure<T> returns a
            // managed struct VALUE whose temp/local type (il2cpp::Il2CppPinvokeRect
            // etc.) is outside CodeGenerator's result-kind whitelist
            // (ExprKind, CodeGenerator.cs:2849) and a `Rectangle` local
            // currently declares the unknown C++ type "System_Drawing_Rectangle"
            // (the local-kind IL signature path, CodeGenerator.cs:1189-1202).
            // BOTH are the CodeGenerator TypeCppName/result-kind extension --
            // a read-only file in this batch. The registered-struct family's
            // reachable e2e surface is SizeOf/OffsetOf (below); the
            // PtrToStructure/StructureToPtr byte round-trip is runtime-pinned.
            case "PtrToStructure":
            case "StructureToPtr":
                throw new NotSupportedException(
                    $"{name} not wired: the byte semantics exist in the runtime (il2cpp_marshal_struct_*_load/store, marshalext3.cpp -- the registered struct family of il2cpp_pinvoke_struct.h), but a PtrToStructure result / ref-T StructureToPtr argument needs a STRUCT-TYPED local or result temp -- CodeGenerator's result-kind whitelist (ExprKind, line 2849) and the value-type local declaration (line 1189-1202) must learn the registered struct C++ types (il2cpp::Il2CppPinvokeRect/Point/Msg). That is the CodeGenerator TypeCppName extension (a read-only file in this batch); the runtime round-trip is unit-tested in test_marshalstruct.cpp");
            // GetFunctionPointerForDelegate/GetDelegateForFunctionPointer:
            // deliberately UNMAPPED (return null) -- the delegatecb work item
            // (#11) pins the MethodSpec-path error "generic instantiation of
            // Marshal.GetFunctionPointerForDelegate has no BCL mapping in M5"
            // @MethodRefResolver.cs:224; mapping it would change that contract.
            // IntPtr GetFunctionPointerForDelegate<T>(T delegate) (delegatecb
            // work item): the runtime single-cast static-method delegate
            // marshals to its raw methodPtr (il2cpp_delegate_ext.h:
            // icall_delegate_get_fn_ptr) -- the pointer the native side calls.
            // The delegate arg rides in an Il2CppObject* slot ("" passthrough);
            // the void* result is the IntPtr carrier ("ptr" result kind ->
            // intptr_t temp). The MethodSpec path (Resolve MethodSpecification)
            // dispatches the generic form here via BclResolve (owner/name);
            // the non-generic MemberRef form maps identically.
            case "GetFunctionPointerForDelegate":
                if (ps.Length != 1 || genArgs == null) return null;
                return I("il2cpp::runtime::icall_delegate_get_fn_ptr", 1, true, false, false,
                    new[] { "" }, "ptr");
            // IntPtr UnsafeAddrOfPinnedArrayElement(Array, int): the address of
            // element 'index' (array data base + index * elemSize) as the IntPtr
            // carrier (icall returns intptr_t -- "ptr" result kind). ps[0] is
            // 0x12 (CLASS System.Array), 0x13 (VAR -- the generic
            // UnsafeAddrOfPinnedArrayElement<T>(T[], int) overload) or 0x1D
            // (SZARRAY); ps[1] is the int32 index. Runtime validates null/range
            // (IndexOutOfRangeException -- a safe superset of net48's
            // undefined behavior). PIN LIMITATION: ILC has no real pinning, the
            // address is only valid until the next collection (documented in
            // il2cpp_marshal_ext.h).
            case "UnsafeAddrOfPinnedArrayElement":
                if (ps.Length != 2 || ps[0] is not (0x12 or 0x13 or 0x1D) || ps[1] != 0x08) return null;
                return I("il2cpp::runtime::icall_marshal_unsafe_addr_of_pinned_array_element",
                    2, true, false, false, new[] { "a", "i" }, "ptr");
            // int GetHRForException(Exception): the exception's HRESULT (net48
            // reads Exception.HResult; 0 for null). ILC exceptions carry no
            // instance _HResult field -- the managed Exception object is the
            // catch handler's interned type-name string -- so the runtime maps
            // the exception TYPE to its net48 default HResult. ps[0] is 0x12
            // (CLASS System.Exception) or 0x1C (object).
            case "GetHRForException":
                if (ps.Length != 1 || ps[0] is not (0x12 or 0x1C)) return null;
                return I("il2cpp::runtime::icall_marshal_get_hr_for_exception", 1, true, false, false,
                    new[] { "" });
            // ---- marshal-b23: VARIANT family + IsComObject (marshalext5.cpp) --
            // These four were REMOVED from kComMarshalMembers (the COM LOUD
            // set) -- they now have real runtime icalls (il2cpp_marshal_ext.h,
            // namespace il2cpp::runtime). IntPtr args ride in the pointer
            // carrier and reach the intptr_t params via the "g" byte-pointer
            // cast (implicit void* onward -- the FreeHGlobal precedent).
            // object GetObjectForNativeVariant(IntPtr)
            case "GetObjectForNativeVariant":
                if (ps.Length != 1 || !IsIntPtrByte(ps[0])) return null;
                return I("il2cpp::runtime::icall_marshal_get_object_for_native_variant", 1, true, true, false,
                    new[] { "g" });
            // void GetNativeVariantForObject(object, IntPtr)
            case "GetNativeVariantForObject":
                if (ps.Length != 2 || ps[0] is not (0x12 or 0x1C) || !IsIntPtrByte(ps[1])) return null;
                return I("il2cpp::runtime::icall_marshal_get_native_variant_for_object", 2, false, false, false,
                    new[] { "", "g" });
            // object[] GetObjectsForNativeVariants(IntPtr, int)
            case "GetObjectsForNativeVariants":
                if (ps.Length != 2 || !IsIntPtrByte(ps[0]) || ps[1] != 0x08) return null;
                return I("il2cpp::runtime::icall_marshal_get_objects_for_native_variants", 2, true, true, false,
                    new[] { "g", "i" });
            // bool IsComObject(object)
            case "IsComObject":
                if (ps.Length != 1 || ps[0] is not (0x12 or 0x1C)) return null;
                return I("il2cpp::runtime::icall_marshal_is_com_object", 1, true, false, false,
                    new[] { "" });
            // ---- com3: Marshal COM RCW surface (com.cpp) -- the 4 members
            // REMOVED from kComMarshalMembers (the COM LOUD set). They now
            // have real runtime icalls (il2cpp_com.h, namespace
            // il2cpp::runtime). Windows = full implementation; POSIX = the
            // documented stub (NotSupportedException / 0, never a crash) --
            // the platform-limitation contract (il2cpp_com.h header comment).
            //   GetActiveObject(string progID): returns the RCW managed object
            //   (Il2CppObject* -- "ref" object result). Null progID ->
            //   ArgumentNullException; unregistered ProgID -> COMException
            //   (REGDB_E_CLASSNOTREG); registered-but-not-running -> COMException
            //   (MK_E_UNAVAILABLE); non-Windows -> NotSupportedException.
            case "GetActiveObject":
                if (ps.Length != 1 || ps[0] != 0x0E) return null;
                return I("il2cpp::runtime::icall_com_get_active_object", 1, true, true, false,
                    new[] { "s" });
            //   GetIUnknownForObject(object o): exposes the object's native
            //   IUnknown* as the IntPtr carrier ("ptr" result kind -- the
            //   icall returns intptr_t, AddRef'd). RCW -> its wrapped
            //   IUnknown*; non-RCW managed object -> a fresh CCW (net48).
            case "GetIUnknownForObject":
                if (ps.Length != 1 || ps[0] is not (0x12 or 0x1C)) return null;
                return I("il2cpp::runtime::icall_com_get_iunknown_for_object", 1, true, false, false,
                    new[] { "" }, "ptr");
            //   IntPtr GetComInterfaceForObject(object o, Type T): exposes the
            //   object's native interface pointer for T (AddRef'd -- the
            //   caller must Release). The managed Type -> IID resolution
            //   happens at TRANSPILE time (the runtime Type object cannot be
            //   resolved to an IID at compile time), so ONLY the two constant
            //   Type args typeof(IUnknown)/typeof(IDispatch) are supported --
            //   the resolver emits the ComGetInterfaceMarker and StackFold
            //   inspects the FOLDED Type argument (a typeof constant
            //   StringConstExpr) and emits the icall with the matching IID
            //   constant; any other Type arg stays LOUD (StackFold throws).
            //   ps[0] = object (0x12/0x1C), ps[1] = Type (0x12/0x1C).
            case "GetComInterfaceForObject":
                if (ps.Length != 2 || ps[0] is not (0x12 or 0x1C) || ps[1] is not (0x12 or 0x1C)) return null;
                return I(ResolvedMethod.ComGetInterfaceMarker, 2, true, false, false,
                    new[] { "", "" }, "ptr");
            //   GetObjectForIUnknown(IntPtr pUnk): wrap a raw IUnknown*
            //   (intptr_t carrier) into an RCW, taking ownership. The IntPtr
            //   carrier reaches the intptr_t param via the "n" cast
            //   (static_cast<intptr_t> -- the delegatecb precedent).
            case "GetObjectForIUnknown":
                if (ps.Length != 1 || !IsIntPtrByte(ps[0])) return null;
                return I("il2cpp::runtime::icall_com_get_object_for_iunknown", 1, true, true, false,
                    new[] { "n" });
            //   int ReleaseComObject(object o): release the RCW's native
            //   reference; returns the remaining runtime reference count
            //   (int32 -- net48 returns int). A non-RCW -> ArgumentException;
            //   null -> ArgumentNullException; a released RCW's later COM
            //   operations -> InvalidComObjectException (safe superset).
            case "ReleaseComObject":
                if (ps.Length != 1 || ps[0] is not (0x12 or 0x1C)) return null;
                return I("il2cpp::runtime::icall_com_release_com_object", 1, true, false, false,
                    new[] { "" });
            // int GetExceptionCode(): x86-only SEH query -- ILC has no SEH in
            // the fully managed context, the runtime always returns 0.
            case "GetExceptionCode":
                if (ps.Length != 0) return null;
                return I("il2cpp::runtime::icall_marshal_get_exception_code", 0, true, false, false);
            // IntPtr GetExceptionPointers(): x86-only SEH query -- the runtime
            // always returns IntPtr.Zero (intptr_t carrier, "ptr" result kind).
            case "GetExceptionPointers":
                if (ps.Length != 0) return null;
                return I("il2cpp::runtime::icall_marshal_get_exception_pointers", 0, true, false, false,
                    resultKind: "ptr");
            // ---- com4: Marshal COM stream surface (stage 12, com.cpp) --
            //   Stream CreateStreamOnHGlobal(IntPtr hGlobal, bool
            //   fDeleteOnRelease): ole32 CreateStreamOnHGlobal wrapped into a
            //   ComStream stream object (Il2CppObject* -- "ref" object result;
            //   NOT an RCW). ps[0] = the IntPtr HGLOBAL carrier (0x11/0x17/0x18
            //   -> the "n" intptr_t cast), ps[1] = the bool fDeleteOnRelease
            //   (0x02 -> the "i" int32 slot -- the bool rides the int32 slot).
            case "CreateStreamOnHGlobal":
                if (ps.Length != 2 || !IsIntPtrByte(ps[0]) || ps[1] != 0x02) return null;
                return I("il2cpp::runtime::icall_com_stream_create_on_hglobal", 2, true, true, false,
                    new[] { "n", "i" });
            //   IntPtr GetHGlobalFromStream(Stream stream): the HGLOBAL behind
            //   the stream's IStream (intptr_t carrier, "ptr" result kind).
            //   ps[0] = CLASS System.IO.Stream (0x12 / 0x1C object).
            case "GetHGlobalFromStream":
                if (ps.Length != 1 || ps[0] is not (0x12 or 0x1C)) return null;
                return I("il2cpp::runtime::icall_com_stream_get_hglobal", 1, true, false, false,
                    new[] { "" }, "ptr");
            // ---- com5: Marshal BSTR bridge (stage 13, com.cpp) -- the public
            // wrappers are WIRED (real runtime icalls); the internal *Native
            // variants stay in kComMarshalMembers (LOUD -- the
            // GetIUnknownForObjectNative precedent). net48 semantics:
            //   IntPtr StringToBSTR(string s): SysAllocStringLen(s->chars,
            //   s->length) -- embedded NUL units preserved. ps[0] = string
            //   (0x0E -> the "s" cast). The BSTR rides in the intptr_t carrier
            //   ("ptr" result kind). null -> IntPtr.Zero (0).
            case "StringToBSTR":
                if (ps.Length != 1 || ps[0] != 0x0E) return null;
                return I("il2cpp::runtime::icall_com_bstr_from_string", 1, true, false, false,
                    new[] { "s" }, "ptr");
            //   string BSTRToString(IntPtr bstr): decode to the FIRST NUL unit
            //   (net48 PtrToStringUni semantics -- embedded NULs truncate).
            //   ps[0] = the IntPtr carrier (0x11/0x17/0x18 -> the "n" intptr_t
            //   cast). "ref" object result. 0 -> null.
            case "BSTRToString":
                if (ps.Length != 1 || !IsIntPtrByte(ps[0])) return null;
                return I("il2cpp::runtime::icall_com_string_from_bstr", 1, true, true, false,
                    new[] { "n" });
            //   void FreeBSTR(IntPtr bstr): SysFreeString (0 -> no-op). ps[0]
            //   = the IntPtr carrier -> the "n" intptr_t cast.
            case "FreeBSTR":
                if (ps.Length != 1 || !IsIntPtrByte(ps[0])) return null;
                return I("il2cpp::runtime::icall_com_bstr_free", 1, false, false, false,
                    new[] { "n" });
            default: return null;
        }
    }

    //--------------------------------------------------------------------------
    // COM interop member names (LOUD rejection set, see TryMapMarshal top).
    // Both the public wrappers and the internal *Native/*Internal icall
    // variants are listed so any MemberRef name the IL emits is caught.
    //--------------------------------------------------------------------------
    private static readonly HashSet<string> kComMarshalMembers = new(StringComparer.Ordinal)
    {
        // Public COM surface
        "AddRef",
        "AreComObjectsAvailableForCleanup",
        "BindToMoniker",
        "ChangeWrapperHandleStrength",
        "CleanupUnusedObjectsInCurrentContext",
        "CreateAggregatedObject",
        "CreateWrapperOfType",
        "FinalReleaseComObject",
        "GetComInterfaceForObjectInContext",
        "GetIDispatchForObject",
        "GetIDispatchForObjectInContext",
        "GetIUnknownForObjectInContext",
        "GetITypeInfoForType",
        "GetLoadedTypeForGUID",
        "GetMethodInfoForComSlot",
        "GetRawIUnknownForComObjectNoAddRef",
        "GetStartComSlot",
        "GetEndComSlot",
        "GetTypeInfoForType",
        "GetTypeLibGuid",
        "GetTypeLibGuidForAssembly",
        "GetTypeLibLcid",
        "GetTypeLibVersion",
        "GetTypedObjectForIUnknown",
        "InitWrapper",
        "IsTypeVisibleFromCom",
        "Release",
        // Internal *Native/*Internal icall variants (public wrapper承繼)
        "FCallGenerateGuidForType",
        "FCallGetTypeInfoGuid",
        "FCallGetTypeLibGuid",
        "FCallGetTypeLibGuidForAssembly",
        "GetComInterfaceForObjectNative",
        "GetComInterfaceForObjectInContextNative",
        "GetIDispatchForObjectNative",
        "GetIUnknownForObjectNative",
        "GetTypeLibVersionForAssembly",
        "InitializeManagedWinRTFactoryObject",
        "InitializeWrapperForWinRT",
        "InternalCreateWrapperOfType",
        "InternalFinalReleaseComObject",
        "InternalGetComSlotForMethodInfo",
        "InternalReleaseComObject",
        "InternalSwitchCCW",
        "InternalWrapIUnknownWithComObject",
        "StringToBSTRNative",
        "BSTRToStringNative",
        "FreeBSTRNative",
        "_GetTypeLibVersionForAssembly",
    };

    // IntPtr param byte detection (root cause #29): mscorlib emits native-int
    // params as 0x17/0x18 (ELEMENT_TYPE_I/U); Roslyn user signatures use 0x11
    // (VALUETYPE + TypeHandle, consumed by RawSignature). All three mean the
    // int32-slot pointer carrier in the M-stage model.
    private static bool IsIntPtrByte(byte b) => b is 0x11 or 0x17 or 0x18;

    // D-MAR-1: the registered struct family's per-struct icall suffix (the
    // pinvokestruct registry keys, il2cpp_pinvoke_struct.h). EXTENSION POINT
    // for a NEW registered struct: add the registry entry in pinvokestruct.cpp
    // (+ the net48 field-name table), the per-struct SizeOf/OffsetOf/load/
    // store icalls in marshalext3.cpp (+ il2cpp_marshal_ext.h declarations),
    // and ONE line here. The generic forms route through genArgNames (the
    // MethodSpec VALUETYPE arg full name -- MethodRefResolver
    // GenericArgValueTypeNames).
    private static string? MarshalStructKindSuffix(string managedName) => managedName switch
    {
        "System.Drawing.Point" => "point",
        "System.Drawing.Rectangle" => "rect",
        "System.Windows.Forms.Message" => "msg",
        _ => null,
    };
}
