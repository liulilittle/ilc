using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;
using Il2Cpp.Transpiler.Opt;

namespace Il2Cpp.Transpiler.Emit;

// T3 string pool (design docs/architecture/string-pooling-design.md §2.3):
// Generate's authoritative per-run pool + emission statistics. Program.cs
// consumes the SAME pool the emitter used (D1 single writer -- the pool
// built by Generate's in-loop fold->collect; ScanAssembly is a separate
// diagnostic API and never feeds emission).
public sealed record GenerateResult(StringPool Pool, int Folded, int Methods);

public static class CodeGenerator
{
    // M1 generator main flow: types come from MetadataLoader; assemblyPath is used to read method bodies (PEReader).
    // Output: {outDir}/CMakeLists.txt, src/<Type>.cpp (one file per type), include/internal/generated.h + <Type>.h.
    // T3 string pool: Generate builds the assembly-level literal pool in its
    // own method loop (fold -> collect, design §2.2 order) and the emitter
    // resolves every ldstr to g_strPool[id] from THE SAME pool -- index and
    // table row can never diverge (D1/D3).
    public static GenerateResult Generate(List<TypeDef> types, string outDir, string assemblyPath = "", SequencePointMap? symMap = null,
        CliOptions? options = null)
    {
        // xUnit parallelizes test classes: concurrent Generate calls on the
        // shared static pool field would interleave ids. The lock serializes
        // them (CLI is single-threaded -- no runtime cost). The inner
        // GenerateCore body was refactored wholesale to avoid re-indenting
        // ~400 lines (C7 surgical scope).
        lock (_poolLock)
        {
            _pool = new StringPool();
            var result = GenerateCore(types, outDir, assemblyPath, symMap, options);
            return result;
        }
    }

    // T3 string pool: the emit-side pool + lock (see Generate). Only touched
    // under _poolLock -- single authoritative writer per Generate call.
    private static StringPool _pool = null!;
    private static readonly object _poolLock = new();

    // CG6 (delegate2/delegops): the set of method tokens whose address was
    // taken as a delegate target (ldftn / delegate-new) during body emission.
    // entry.cpp's il2cpp_runtime_init emits one il2cpp_delegate_register_method
    // call per token so Delegate.Method.Name resolves (the runtime name
    // registry is otherwise empty -> InvalidOperationException). Single-writer
    // under _poolLock; cleared at the top of every GenerateCore.
    private static readonly HashSet<int> _delegateMethodTokens = new();

    //--------------------------------------------------------------------------
    // P-D4 (mixed loading): per-library static/dynamic routing.
    //
    // A [DllImport] library is STATIC (direct extern "C" call, linked into the
    // exe) when the case directory carries a library dir whose CMakeLists does
    // NOT build it SHARED (mylib STATIC; system DLLs via INTERFACE import-lib
    // targets like kernel32). It is DYNAMIC (resolved at runtime through
    // il2cpp_pinvoke_resolve) when the case's library dir is absent
    // (out-of-manifest) or builds the library SHARED (e2e extdll). Dynamic
    // libraries must NOT emit an extern "C" declaration (the symbol would be
    // an undefined link reference -- the very failure dynamic resolution was
    // meant to avoid) and must NOT be target_link_libraries'd.
    //
    // The manifest decision is made ONCE per Generate (under _poolLock, so
    // parallel xUnit Generate calls never race): the case directory is derived
    // from the assembly path exactly as EmitCmake derives it.
    //--------------------------------------------------------------------------
    private static HashSet<string> _dynamicPinvokeModules = new(StringComparer.OrdinalIgnoreCase);
    // (moduleBase, entry) -> the native C++ signature ({"Ret","Params"}) of a
    // DYNAMIC P/Invoke, keyed for the call-site resolve cast. Built alongside
    // the dynamic-module set; only dynamic entries are stored (static calls
    // keep the extern "C" decl and need no cast).
    private static readonly Dictionary<(string Module, string Entry), (string Ret, string Params)> _pinvokeDynamicSig =
        new();

    // The discriminated decision + signature for one P/Invoke module.
    private static bool PinvokeModuleIsDynamic(string module)
        => _dynamicPinvokeModules.Contains(module);

    // Whether this resolved call is a DYNAMIC P/Invoke (out-of-manifest lib).
    private static bool TryPinvokeDynamicSig(string module, string entry, out string retCpp, out string paramsCpp)
    {
        // A bare module key (the resolver may yield the raw name; normalize
        // the .dll suffix as the manifest keys do).
        var baseKey = module.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? module.Substring(0, module.Length - 4) : module;
        if (_pinvokeDynamicSig.TryGetValue((baseKey, entry), out var sig))
        {
            retCpp = sig.Ret;
            paramsCpp = sig.Params;
            return true;
        }
        retCpp = "";
        paramsCpp = "";
        return false;
    }

    private static GenerateResult GenerateCore(List<TypeDef> types, string outDir, string assemblyPath, SequencePointMap? symMap,
        CliOptions? options)
    {
        _delegateMethodTokens.Clear();
        if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
            throw new ArgumentException($"invalid or missing assemblyPath: '{assemblyPath}'");

        Directory.CreateDirectory(Path.Combine(outDir, "src"));
        Directory.CreateDirectory(Path.Combine(outDir, "include", "internal"));

        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        ComputePinvokeManifest(types, md, assemblyPath, outDir);

        var generatedHeader = new CppWriter();
        generatedHeader.Line("#pragma once");
        generatedHeader.Line("// generated by il2cppc - do not edit");
        generatedHeader.Line("#include \"il2cpp_runtime.h\"");
        // M5 BCL wiring: the runtime icall extension headers -- the generated
        // code calls il2cpp::runtime::icall_* from the M5 runtime domains
        // (console/math/math_ext/io/time/random/boxing/exception/array_ext/
        // format/conv/convert/concat/hashtable/gc_api/delegate_ext/typecheck/
        // string_ext/stringbuilder/environment). <cmath> supplies NAN/
        // INFINITY for double-literal emission; <cstring> for the
        // bit-conversion helpers below.
        generatedHeader.Line("#include \"il2cpp_console.h\"");
        generatedHeader.Line("#include \"il2cpp_math.h\"");
        generatedHeader.Line("#include \"il2cpp_math_ext.h\"");
        // net9/10 E2E icallcase: MathF domain header -- MethodMap.MathF.cs maps
        // MathF.Sqrt/Pow/... to icall_mathf_*_f whose declarations live here;
        // without the include the generated code fails to compile.
        generatedHeader.Line("#include \"il2cpp_mathf.h\"");
        // net9/10 E2E icallcase: the internal-helper stub declarations
        // (icall_threadobj_spin_wait, icall_thread_pool_*, icall_log_* ...).
        generatedHeader.Line("#include \"stubs.h\"");
        generatedHeader.Line("#include \"il2cpp_io.h\"");
        generatedHeader.Line("#include \"il2cpp_time.h\"");
        generatedHeader.Line("#include \"guid.h\""); // System.Guid value slots (uint8_t[16])
        generatedHeader.Line("#include \"il2cpp_random.h\"");
        generatedHeader.Line("#include \"il2cpp_boxing_ext.h\"");
        generatedHeader.Line("#include \"il2cpp_boxing_ext2.h\"");
        generatedHeader.Line("#include \"il2cpp_exception_ext.h\"");
        generatedHeader.Line("#include \"il2cpp_array_ext.h\"");
        generatedHeader.Line("#include \"il2cpp_array_ext2.h\"");
        generatedHeader.Line("#include \"il2cpp_format.h\"");
        generatedHeader.Line("#include \"il2cpp_conv.h\"");
        generatedHeader.Line("#include \"il2cpp_convert.h\"");
        // M5 wiring: Base64 domain (il2cpp_convert_to/from_base64_string --
        // Convert.ToBase64String/FromBase64String, MethodMap.Base64.cs).
        generatedHeader.Line("#include \"il2cpp_convertbase64.h\"");
        // Windows types: forward-declare Windows SDK types referenced by
        // generated code (SYSTEM_INFO, MEMORYSTATUSEX, WIN32_FIND_DATA, etc.).
        // Conditional include keeps non-Windows builds clean.
        generatedHeader.Line("#ifdef _WIN32");
        generatedHeader.Line("#include <windows.h>");
        // Win32 SDK headers that <windows.h> does NOT pull in: the GUI import
        // families (comdlg32 -> commdlg.h: GetOpenFileNameW/ChooseColorW/
        // ChooseFontW; comctl32 -> commctrl.h: InitCommonControlsEx/
        // ImageList_*) need their own headers so the call sites see the SDK
        // prototypes. The transpiler no longer restates system-DLL imports
        // (isWindowsDll below suppresses the declaration), so these prototypes
        // ARE the call targets -- without the include the call is undeclared.
        // Conditional on the assembly actually importing the module (no cost
        // for the non-GUI cases). wingdi.h (gdi32) rides <windows.h> already.
        static bool HasWin32Import(List<TypeDef> ts, string name)
            => ts.Any(t => t.Methods.Any(m => m.IsPInvoke && m.PinvokeModule != null
                && (m.PinvokeModule.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || m.PinvokeModule.Equals(name + ".dll", StringComparison.OrdinalIgnoreCase))));
        if (HasWin32Import(types, "comdlg32")) generatedHeader.Line("#include <commdlg.h>");
        if (HasWin32Import(types, "comctl32")) generatedHeader.Line("#include <commctrl.h>");
        generatedHeader.Line("#endif");
        // M7 wiring: numeric Parse/TryParse domain -- Int32/Int64/Double/
        // Byte/Bool TryParse icalls (icall_try_parse_int32/int64/double/bool/
        // byte + _base, MethodMap.ValueType.cs) live in il2cpp_parse.h; the
        // M6 include-table rebuild dropped it, so TryParse emitting cases
        // (numchain/parsecase) failed to compile against the namespace.
        generatedHeader.Line("#include \"il2cpp_parse.h\"");
        // M5.5/M6 wiring: reflection domains -- reflect_base.h declares the
        // Type.GetType / Activator.CreateInstance icalls (MethodMap.Reflect.cs)
        // and typeinfo.h the typeinfo registry + member-query icalls
        // (il2cpp_typeinfo_get_method/field/property, MethodMap.Type.cs).
        // reflectcase/refinvoke build failures were missing includes ("no member
        // named 'il2cpp_type_get_type'/'il2cpp_typeinfo_get_method'").
        generatedHeader.Line("#include \"reflect_base.h\"");
        generatedHeader.Line("#include \"typeinfo.h\"");
        // M5.5 reflection: the unified-signature invoker -- MethodInfo.Invoke
        // (icall_method_invoke, MethodMap.MethodInfo) dispatches through
        // il2cpp_invoker_invoke(fnPtr, signature, argbuf, argCount, ret);
        // invoker.h declares the argbuf slot union + the function. Without
        // the include the generated invoker call cannot compile.
        generatedHeader.Line("#include \"invoker.h\"");
        // M6 wiring: decimal domain -- the arithmetic/comparison icalls
        // (icall_decimal_*) and the decimal_format custom format + ToString
        // path (MethodMap.Decimal.cs). decimalcase blocking D was a missing
        // include in the M6 include-table rebuild.
        generatedHeader.Line("#include \"decimal.h\"");
        generatedHeader.Line("#include \"decimal_format.h\"");
        generatedHeader.Line("#include \"il2cpp_concat.h\"");
        generatedHeader.Line("#include \"il2cpp_hashtable.h\"");
        generatedHeader.Line("#include \"il2cpp_gc_api.h\"");
        generatedHeader.Line("#include \"il2cpp_delegate_ext.h\"");
        generatedHeader.Line("#include \"il2cpp_typecheck.h\"");
        generatedHeader.Line("#include \"il2cpp_string_ext.h\"");
        generatedHeader.Line("#include \"il2cpp_stringbuilder.h\"");
        // D-CHA-2: char static domain -- char.IsDigit/IsLetter/IsWhiteSpace/
        // ToUpper/ToLower/... (icall_char_*, MethodMap.Char.cs) live in
        // il2cpp_char.h; without the include the generated C++ cannot see the
        // declarations (charops static-surface cmake failure otherwise).
        generatedHeader.Line("#include \"il2cpp_char.h\"");
        generatedHeader.Line("#include \"il2cpp_environment.h\"");
        // M5 wiring: the second-wave runtime extension headers (stringext2
        // statics / format_ext custom formatters / time_ext DateTime format /
        // value_semantics hashing / thread / gc_ext weak-ref + finalizer /
        // string intern / marshal + pinvoke extensions).
        generatedHeader.Line("#include \"il2cpp_string_ext2.h\"");
        generatedHeader.Line("#include \"il2cpp_format_ext.h\"");
        // D-CUL-1: NumberFormatInfo.CurrencySymbol (get_CurrencySymbol, batch
        // 118) maps to il2cpp::runtime::il2cpp_number_format_currency_symbol
        // in numberformat.h -- without the include the generated C++ cannot
        // see the declaration (culturefmt e2e cmake failure otherwise).
        generatedHeader.Line("#include \"numberformat.h\"");
        generatedHeader.Line("#include \"il2cpp_time_ext.h\"");
        // M6 wiring: TimeZone simplification domain -- il2cpp_timezone_get_
        // utc_offset / is_daylight_saving (MethodMap.TimeZone.cs) live in
        // timezone.h (namespace il2cpp::runtime). timezonecase was a matrix6
        // regression: M5 had this include in the working tree, the M6
        // include-table rebuild dropped it.
        generatedHeader.Line("#include \"timezone.h\"");
        // P13 wiring: GregorianCalendar domain -- the calendar icalls
        // (icall_calendar_*, MethodMap.Calendar.cs) live in il2cpp_calendar.h
        // (namespace il2cpp::runtime). Without the include the generated C++
        // cannot see the declarations (calendarcase e2e).
        generatedHeader.Line("#include \"il2cpp_calendar.h\"");
        // P13 wiring: DateTimeOffset domain -- the dtoffset icalls
        // (icall_dtoffset_*, MethodMap.Dtoffset.cs) live in il2cpp_dtoffset.h
        // (namespace il2cpp::runtime). Without the include the generated C++
        // cannot see the declarations (dtoffsetcase e2e).
        generatedHeader.Line("#include \"il2cpp_dtoffset.h\"");
        // P15 wiring: wait-handle domain -- the Semaphore/Mutex/EventWaitHandle
        // icalls (icall_waithandle_*, MethodMap.WaitHandle.cs) live in
        // il2cpp_waithandle.h (namespace il2cpp::runtime). Without the include
        // the generated C++ cannot see the declarations (waithandlecase e2e).
        generatedHeader.Line("#include \"il2cpp_waithandle.h\"");
        // P17 wiring: BigInteger domain -- the biginteger icalls
        // (icall_biginteger_*, MethodMap.BigInteger.cs) live in
        // il2cpp_biginteger.h (namespace il2cpp::runtime). Without the include
        // the generated C++ cannot see the declarations (bigintcase e2e).
        generatedHeader.Line("#include \"il2cpp_biginteger.h\"");
        generatedHeader.Line("#include \"il2cpp_value_semantics.h\"");
        generatedHeader.Line("#include \"il2cpp_thread.h\"");
        // M6 wiring: threadpool domain -- the QueueUserWorkItem/SetMaxThreads
        // icalls (il2cpp_threadpool_queue_work_item_delegate /
        // il2cpp_threadpool_set_max_threads, MethodMap.ThreadPool.cs) live in
        // threadpool.h; without the include the generated C++ cannot see the
        // declarations (threadpoolcase e2e).
        generatedHeader.Line("#include \"threadpool.h\"");
        // M6 wiring: threadobj domain -- the Thread OBJECT icalls
        // (icall_threadobj_ctor/start/join/is_alive/managed_thread_id/priority/
        // current_thread, MethodMap.Thread.cs) live in threadobj.h; without
        // the include the generated C++ cannot see the declarations
        // (threadcase e2e).
        generatedHeader.Line("#include \"threadobj.h\"");
        // batch 158 (timercase): the Timer managed surface -- the
        // System.Threading.Timer descriptor (System_Threading_Timer_class)
        // and the icall_timer_ctor/change/dispose declarations live in
        // timer.h; without the include the generated C++ cannot see them.
        generatedHeader.Line("#include \"timer.h\"");
        generatedHeader.Line("#include \"il2cpp_gc_ext.h\"");
        // M6 wiring: GCHandle domain -- the handle-table icalls
        // (icall_gchandle_alloc/free/get_target/addr_of_pinned, MethodMap.
        // GCHandle.cs) live in il2cpp_gchandle.h. Missing include -> the
        // generated C++ cannot see the declarations (cmake_build failure).
        generatedHeader.Line("#include \"il2cpp_gchandle.h\"");
        generatedHeader.Line("#include \"il2cpp_string_intern.h\"");
        generatedHeader.Line("#include \"il2cpp_marshal_ext.h\"");
        generatedHeader.Line("#include \"il2cpp_marshal_ext2.h\"");
        generatedHeader.Line("#include \"il2cpp_pinvoke_ext.h\"");
        // P/Invoke native-ABI bridge (Win32 handle/pointer/wide-string/struct
        // marshalling): the internal carriers (intptr_t / char16_t* / managed
        // mirror structs) are adapted to whatever the NATIVE declaration names
        // at the call target -- windows.h's SDK prototype on Windows, the
        // transpiler's own extern "C" otherwise.
        generatedHeader.Line("#include \"il2cpp_pinvoke_bridge.h\"");
        // M5 wiring: regex domain (il2cpp_regex_is_match/match/replace/split
        // -- static Regex overloads, MethodMap.Regex.cs).
        generatedHeader.Line("#include \"regex.h\"");
        // M5 wiring: List<int> domain -- the int32 AOT expansion allocates
        // through il2cpp_list_i4_class (BclAllocExpr, MethodMap.List.cs).
        generatedHeader.Line("#include \"listi4.h\"");
        // ilistException: List<Exception> (CLASS instantiation) allocates
        // through il2cpp_list_obj_class (listobj.h, namespace il2cpp) --
        // without the include the generated C++ cannot see the declarations.
        generatedHeader.Line("#include \"listobj.h\"");
        // M6 wiring: ArrayList / Stack+Queue object domains -- the member
        // icalls (il2cpp_arraylist_* / il2cpp_stack_* / il2cpp_queue_*) and
        // the *_class() descriptor accessors live in arraylist.h / stackqueue.h
        // (namespace il2cpp). listops/stackq D-AL-1/D-STK-1 were matrix6
        // build failures: the include table never listed them.
        generatedHeader.Line("#include \"arraylist.h\"");
        generatedHeader.Line("#include \"stackqueue.h\"");
        // dictsl-b23 (dictcase SortedList wiring): the non-generic SortedList
        // domain -- the member icalls (il2cpp_sortedlist_*) and the GLOBAL
        // System_Collections_SortedList_class descriptor (newobj fallback)
        // live in sortedlist.h (namespace il2cpp / il2cpp::runtime). hash2
        // wired set_Item but never added this include -- the SortedList e2e
        // surface has never compiled for that reason (dictcase blocker).
        generatedHeader.Line("#include \"sortedlist.h\"");
        // M7/M8 wiring: LINQ domain -- the System.Linq array-family icalls
        // (il2cpp_linq_count/count_i4/any/any_i4/all_i4/sum_i4/min_i4/max_i4,
        // MethodMap.Linq.cs) live in linq.h; the filtering/mapping operators
        // (il2cpp_linq_where_i4/select_i4_i4/to_array/to_list/first_i4/
        // first_or_default_i4) in linq_map.h; the ordering operators
        // (il2cpp_linq_order_by_i4/ref/string) in linq_sort.h (namespace
        // il2cpp). linqdeep was the first LINQ e2e to reach the cmake build --
        // missing include -> ~20 "no member named 'il2cpp_linq_*' in namespace
        // 'il2cpp'" compile errors (D-LINQ-2).
        generatedHeader.Line("#include \"linq.h\"");
        generatedHeader.Line("#include \"linq_map.h\"");
        generatedHeader.Line("#include \"linq_sort.h\"");
        // M6 wiring: the remaining wired-domain runtime headers -- every
        // il2cpp::runtime::* symbol the MethodMap domains can emit must be
        // declared in generated.h or the generated C++ fails to compile
        // (arrayextra/arraysort/binaryio/bitconverter/buffer/debugext/
        // dictsi/enumext/interlocked/intptr/monitor/string_ext3/streamio/
        // stopwatch/timeext2/typeext/versionuri + regex_match).
        generatedHeader.Line("#include \"arrayextra.h\"");
        generatedHeader.Line("#include \"arraysort.h\"");
        generatedHeader.Line("#include \"attribute.h\"");
        generatedHeader.Line("#include \"binaryio.h\"");
        generatedHeader.Line("#include \"bitconverter.h\"");
        generatedHeader.Line("#include \"buffer.h\"");
        generatedHeader.Line("#include \"debugext.h\"");
        generatedHeader.Line("#include \"dictsi.h\"");
        // rtdict-b23 (DICT-GC): the Dictionary<string,int> GC-OBJECT wrapper --
        // the System_Collections_Generic_Dictionary_2_string_int_class GLOBAL
        // descriptor + the icall_dict_si_* wrappers (dictsi_obj.h). Missing
        // include -> il2cpp_object_new(&System_Collections_Generic_Dictionary_2_
        // string_int_class) + the icall_dict_si_* calls cannot compile.
        generatedHeader.Line("#include \"dictsi_obj.h\"");
        // Phase-2 absorption: the Dictionary<string,string> /
        // Dictionary<Uri,string> expansions ride the shared generic table
        // (dictgen.h) through the icall_dict_gen_*_o twins -- the dictss /
        // uristr shims are deleted, no dictss_obj.h include.
        // Phase-1 (dictgen): the shared generic Dictionary`2 storage + kind
        // registry (dictgen.h) -- the wrapper/enumerator/KVP descriptors +
        // the single icall_dict_gen_* family.
        generatedHeader.Line("#include \"dictgen.h\"");
        // G4: the Queue<int> boxing/unboxing wrappers (queueint_obj.h).
        generatedHeader.Line("#include \"queueint_obj.h\"");
        // T2 W4: the Stack<int/long/double> boxing/unboxing wrappers.
        generatedHeader.Line("#include \"stackint_obj.h\"");
        generatedHeader.Line("#include \"sortedlistint_obj.h\"");
        generatedHeader.Line("#include \"sortedset_obj.h\"");
        generatedHeader.Line("#include \"genericcomparer_obj.h\"");
        generatedHeader.Line("#include \"enumext.h\"");
        generatedHeader.Line("#include \"il2cpp_interlocked.h\"");
        generatedHeader.Line("#include \"il2cpp_string_ext3.h\"");
        generatedHeader.Line("#include \"il2cpp_typeext.h\"");
        generatedHeader.Line("#include \"intptr.h\"");
        generatedHeader.Line("#include \"monitor.h\"");
        generatedHeader.Line("#include \"regex_match.h\"");
        generatedHeader.Line("#include \"stopwatch.h\"");
        generatedHeader.Line("#include \"streamio.h\"");
        // M6 wiring: StreamIO OBJECT domain -- the object-state stream icalls
        // (icall_stream_file_ctor / icall_stream_read_bytes / ...) and the 4
        // System_IO_*_class descriptors are declared in il2cpp_streamio.h.
        // Missing include -> il2cpp_object_new(&System_IO_FileStream_class)
        // fails to compile (undeclared identifier).
        generatedHeader.Line("#include \"il2cpp_streamio.h\"");
        // P18 wiring: DeflateStream domain -- the deflate icalls
        // (icall_deflate_stream_*, MethodMap.Deflate.cs) live in
        // il2cpp_deflate.h (namespace il2cpp::runtime). Without the include
        // the generated C++ cannot see the declarations (deflatecase e2e).
        generatedHeader.Line("#include \"il2cpp_deflate.h\"");
        // P19 wiring: Expression-tree domain -- the exprtree icalls
        // (icall_expr_*, MethodMap.ExprTree.cs) live in il2cpp_exprtree.h
        // (namespace il2cpp::runtime). Without the include the generated C++
        // cannot see the declarations (exprtreecase e2e).
        generatedHeader.Line("#include \"il2cpp_exprtree.h\"");
        // M6 wiring: XML domain -- XmlDocument.LoadXml/OuterXml icalls
        // (il2cpp_xml_load_xml / il2cpp_xml_outer_xml, MethodMap.Xml.cs) live
        // in xml_doc.h (namespace il2cpp::runtime), which includes xml_ast.h
        // for the XmlNode handle type. Missing include -> the generated
        // il2cpp_xml_load_xml call + "node" cast cannot compile (xmlcase).
        generatedHeader.Line("#include \"xml_doc.h\"");
        // D-XMLW-1 (xmlwrite): the XmlWriter minimal write surface -- the
        // il2cpp_xml_writer_* / il2cpp_xml_writer_settings_* icalls
        // (MethodMap.Xml.cs TryMapXmlWriter) are declared in xml_write.h
        // (namespace il2cpp::runtime). Missing include -> ~20 "no member
        // named 'il2cpp_xml_writer_*'" compile errors (the xmlwrite gate).
        generatedHeader.Line("#include \"xml_write.h\"");
        generatedHeader.Line("#include \"timeext2.h\"");
        generatedHeader.Line("#include \"versionuri.h\"");
        // M6 wiring: StringIO domain -- the StringReader/StringWriter object
        // icalls (icall_stringreader_* / icall_stringwriter_*, MethodMap.StringIO.cs)
        // and their System_IO_StringReader_class / System_IO_StringWriter_class
        // descriptors are declared in stringrw.h.
        generatedHeader.Line("#include \"stringrw.h\"");
        // M6 wiring: encoding domain -- the allocating GetBytes/GetString
        // icalls (il2cpp_encoding_*_get_bytes/get_string, MethodMap.Encoding.cs)
        // need the extension header declared (the raw-buffer entries were never
        // wired, so the include was never needed before).
        generatedHeader.Line("#include \"il2cpp_encoding.h\"");
        // M6 wiring: crypto domain -- the one-shot hash_bytes ARRAY icalls
        // (il2cpp_md5/sha1/sha256_hash_bytes, MethodMap.Hash.cs) are declared
        // in the hash headers; sha1.h lives in namespace il2cpp, md5/sha256
        // in il2cpp::runtime -- all three headers are self-contained.
        generatedHeader.Line("#include \"md5.h\"");
        generatedHeader.Line("#include \"sha1.h\"");
        generatedHeader.Line("#include \"sha256.h\"");
        // M6 wiring: crypto OBJECT domain -- MD5/SHAx.Create() newobj + the
        // allocating create icalls (icall_md5/sha1/sha256_create, MethodMap.
        // Crypto.cs) and the System_Security_Cryptography_*_class descriptors
        // live in crypto_obj.h (namespace il2cpp::runtime). hashcase/hashchain
        // /cryptocase were matrix6 regressions: M5 had this include in the
        // working tree, the M6 include-table rebuild dropped it.
        generatedHeader.Line("#include \"crypto_obj.h\"");
        // M6 wiring: Process/Path domain -- the PID handle-gap icalls
        // (il2cpp_process_start/wait_for_exit/get_exit_code/read_stdout_line/
        // get_current_process_id, MethodMap.Process.cs) plus the Path
        // extension icalls are declared in pathproc.h.
        generatedHeader.Line("#include \"pathproc.h\"");
        // M6 wiring: WebClient domain -- the System_Net_WebClient_class GLOBAL
        // descriptor + icall_web_client_ctor/download_string (il2cpp_http.h).
        // Missing include -> "undeclared identifier System_Net_WebClient_class"
        // (webcase -- BclAllocExpr already routes newobj to the descriptor).
        generatedHeader.Line("#include \"il2cpp_http.h\"");
        // M7 CG-FIX (netcase cmake FAIL): the System.Net.IPAddress parse layer
        // (il2cpp_ipaddress_parse / il2cpp_ipaddress_to_string, MethodMap.Net.cs)
        // is declared in ipaddr.h (namespace il2cpp::runtime). Without the
        // include the generated C++ reports "no member named
        // 'il2cpp_ipaddress_parse' in namespace 'il2cpp::runtime'".
        generatedHeader.Line("#include \"ipaddr.h\"");
        generatedHeader.Line("#include <cmath>");
        generatedHeader.Line("#include <cstring>");
        // ILOP-2 P8: localloc -- alloca, emitted INLINE at the call site
        // (the .NET stack-frame lifetime). MSVC declares _alloca in
        // <malloc.h>; GCC/Clang provide __builtin_alloca (no header needed --
        // <alloca.h> is glibc-only, absent on MinGW-w64). The wrapper hides
        // the platform split; <cstddef> provides size_t.
        generatedHeader.Line("#if defined(_MSC_VER)");
        generatedHeader.Line("#include <malloc.h>");
        generatedHeader.Line("#endif");
        generatedHeader.Line("inline void* il2cpp_localloc(size_t n) {");
        generatedHeader.Line("#if defined(_MSC_VER)");
        generatedHeader.Line("    return _alloca(n);");
        generatedHeader.Line("#else");
        generatedHeader.Line("    return __builtin_alloca(n);");
        generatedHeader.Line("#endif");
        generatedHeader.Line("}");
        // M5 wiring: bit-conversion helpers for BitConverter.DoubleToInt64Bits
        // / Int64BitsToDouble (no runtime icall exists; memcpy is the portable
        // type-pun -- identical on every supported compiler).
        generatedHeader.Line("inline int64_t il2cpp_double_bits(double v) { int64_t r; std::memcpy(&r, &v, sizeof(r)); return r; }");
        generatedHeader.Line("inline double il2cpp_double_from_bits(int64_t v) { double r; std::memcpy(&r, &v, sizeof(r)); return r; }");
        // M5 wiring: Console.WriteLine(long) -- the runtime has no int64
        // console icall; format through icall_int64_to_string then the string
        // printer (identical .NET rendering).
        generatedHeader.Line("IL2CPP_NOINLINE inline void il2cpp_console_write_line_l(int64_t v)");
        generatedHeader.Line("{");
        generatedHeader.Line("    il2cpp::runtime::icall_console_write_line_s(reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::runtime::icall_int64_to_string(v)));");
        generatedHeader.Line("}");
        // Generated code lives in the global namespace and refers to the
        // runtime's object-model types (Il2CppObject / Il2CppClass) and the
        // gc/registry API unqualified; a single using-directive is the
        // generated-code contract for the whole translation unit.
        generatedHeader.Line("using namespace il2cpp;");
        generatedHeader.Line();
        // fnptr domain b23 (calli): unmanaged calling-convention keywords for
        // function-pointer casts. On x64/ARM64 (Windows and POSIX) all four
        // conventions unify to the platform default, so the keywords are
        // empty; only 32-bit Windows distinguishes __cdecl/__stdcall/__thiscall/
        // __fastcall. MSVC x86 accepts all four; the empty fallback keeps the
        // generated code portable (POSIX compilers reject __stdcall etc.).
        generatedHeader.Line("#if defined(_WIN32) && !defined(_WIN64)");
        generatedHeader.Line("#define IL2CPP_CALLCONV_CDECL __cdecl");
        generatedHeader.Line("#define IL2CPP_CALLCONV_STDCALL __stdcall");
        generatedHeader.Line("#define IL2CPP_CALLCONV_THISCALL __thiscall");
        generatedHeader.Line("#define IL2CPP_CALLCONV_FASTCALL __fastcall");
        generatedHeader.Line("#else");
        generatedHeader.Line("#define IL2CPP_CALLCONV_CDECL");
        generatedHeader.Line("#define IL2CPP_CALLCONV_STDCALL");
        generatedHeader.Line("#define IL2CPP_CALLCONV_THISCALL");
        generatedHeader.Line("#define IL2CPP_CALLCONV_FASTCALL");
        generatedHeader.Line("#endif");
        generatedHeader.Line();

        // R77 generic multi-instantiation scan (R2b): collect every generic-inst
        // local across all method bodies, and synthesize a CLONE TypeDef per
        // unique (defName, suffix) whose methods/fields are re-owned so the
        // emitter can generate the concrete struct + methods. MUST run BEFORE
        // VTablePlanner.PlanAll (the clone's vtable/interface maps need
        // planning exactly like a definition). Identity: the clone shares the
        // definition's FullName (C++ symbols/hashes key on the definition
        // name -- A3 constraints C3/C4); TypeDef.InstantiationKey (FullName +
        // "|" + suffix) is the planner dedup key.
        var genericInstTypes = new Dictionary<string, TypeDef>(StringComparer.Ordinal);
        // P-G2 active share set: every generic definition whose usage analysis
        // accepted plain-object sharing collapses ALL its closed
        // instantiations onto one "object" clone (see
        // GenericUsageAnalyzer.CollapseInstantiation). Conservative phase:
        // only fully object-compatible templates share; anything directed
        // stays on today's per-instance path.
        var shareSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var st in types)
        {
            // P-G3: a non-generic class whose METHODS are generic (SwapRef<T>)
            // is a method-level sharing candidate (its definition IS the object
            // template -- no clone, arity 0). Non-generic classes never share.
            var hasGenericMethod = st.Methods.Any(m => m.GenericArity > 0);
            if ((st.GenericArity <= 0 && !hasGenericMethod) || st.IsInterface || st.IsValueType) continue;
            // P-G2 inheritance guard: when another type derives from THIS
            // generic definition (VirtStr : VirtBase<string>), sharing it as
            // "object" would rewrite the derived vtable/BaseType slots to the
            // object clone while the base C++ layout/signatures stay per-suffix
            // -- a layout mismatch that faulted at runtime (genericvirtstr
            // 0xC0000005). Derived inheritance needs the exact per-instance
            // base, so a shared base is refused.
            var isBaseOf = types.Any(t => !ReferenceEquals(t, st) && !t.IsInterface
                && t.BaseTypeName?.StartsWith(st.FullName + "<", StringComparison.Ordinal) == true);
            if (isBaseOf) continue;
            var mask = GenericUsageAnalyzer.GetShareMask(
                md, st,
                m => { try { return MethodBodyReader.Read(pe, md, st, m); } catch { return null; } });
            if (mask is { } mm && mm.All(x => x)) shareSet.Add(st.FullName);
        }
        GenericUsageAnalyzer.SharedGenericDefs = shareSet;
        // R2b scan source helper: synthesize a clone for one (defFullName,
        // suffix) pair from the definition in this assembly (same helper the
        // local-kind scan uses; also fed by the newobj/TypeSpec and model
        // string scans below -- newobj-only instantiations have no local kind).
        void EnsureClone(string gDef, string gSuffix)
        {
            var def = types.FirstOrDefault(x => x.FullName == gDef && x.InstantiationSuffix == null);
            if (def == null) return;
            // R2b: the "int" instantiation IS the definition itself under the
            // M3 single-instantiation convention (TypeCppName default "_int");
            // a clone would emit a second same-named type/method set (allSrc
            // overwrite/duplicate definition). Keep the definition. P-G2: the
            // default instantiation is "int" PER parameter for arity>1 defs
            // ("Pair`2" default == "int_int"), so the guard must compare
            // against the arity-wide int suffix, not a bare "int".
            var defaultSuffix = string.Join("_", Enumerable.Repeat("int", def.GenericArity));
            if (gSuffix == defaultSuffix) return;
            // R2b scope: reference CLASS instantiations only. Interface/struct
            // instantiations keep the legacy path (R3 / P2 concerns: interface
            // dispatch maps and value-type slots need their own closure).
            if (def.IsInterface || def.IsValueType) return;
            // P-G2: a shared definition collapses every closed instantiation
            // onto the single "object" clone (the share mask guarantees the
            // body only uses object-compatible operations). The clone suffix is
            // "object" repeated per arity so it matches the call side
            // (GenericUsageAnalyzer.CollapseInstantiation rewrites EACH
            // argument to "object").
            if (GenericUsageAnalyzer.SharedGenericDefs?.Contains(gDef) == true)
            {
                gSuffix = string.Join("_", Enumerable.Repeat("object", def.GenericArity));
            }
            var gKey = gDef + "|" + gSuffix;
            if (genericInstTypes.ContainsKey(gKey)) return;
            var clone = new TypeDef
            {
                Name = def.Name,
                Namespace = def.Namespace,
                GenericArity = def.GenericArity,
                InstantiationSuffix = gSuffix,
                IsInterface = false,
                IsValueType = false,
                IsSealed = def.IsSealed,
                IsAbstract = false,
                IsNested = def.IsNested,
                DeclaringTypeName = def.DeclaringTypeName,
                BeforeFieldInit = def.BeforeFieldInit,
                BaseTypeName = def.BaseTypeName,
            };
            // R2b P2: a VAR field becomes the concrete reference storage in a
            // reference instantiation (Il2CppObject* -- the uniform managed
            // reference representation).
            bool RefVar(FieldDef f) => f.IsGenericVar && gSuffix != "int";
            foreach (var f in def.StaticFields)
                clone.StaticFields.Add(new FieldDef
                {
                    Name = f.Name, Owner = clone, IsStatic = true,
                    IsLiteral = f.IsLiteral, ConstantValue = f.ConstantValue,
                    TypeKind = RefVar(f) ? FieldTypeKind.Reference : f.TypeKind,
                    FieldTypeName = RefVar(f) ? "System.Object" : f.FieldTypeName,
                    IsGenericVar = f.IsGenericVar,
                });
            foreach (var f in def.InstanceFields)
                clone.InstanceFields.Add(new FieldDef
                {
                    Name = f.Name, Owner = clone, IsStatic = false,
                    IsLiteral = f.IsLiteral, ConstantValue = f.ConstantValue,
                    TypeKind = RefVar(f) ? FieldTypeKind.Reference : f.TypeKind,
                    FieldTypeName = RefVar(f) ? "System.Object" : f.FieldTypeName,
                    IsGenericVar = f.IsGenericVar,
                });
            foreach (var m in def.Methods)
                clone.Methods.Add(new MethodDef
                {
                    Name = m.Name, Owner = clone, IsStatic = m.IsStatic,
                    IsPublic = m.IsPublic, ParamCount = m.ParamCount,
                    ParamTypes = m.ParamTypes, ReturnType = m.ReturnType,
                    ReturnTypeName = m.ReturnTypeName, IsVirtual = m.IsVirtual,
                    IsOverride = m.IsOverride, IsAbstract = m.IsAbstract,
                    IsFinal = m.IsFinal, IsPInvoke = m.IsPInvoke,
                    PinvokeModule = m.PinvokeModule, PinvokeEntryName = m.PinvokeEntryName,
                    PinvokeFlags = m.PinvokeFlags, PinvokeSetLastError = m.PinvokeSetLastError,
                    GenericArity = m.GenericArity,
                    ParamIsVar = m.ParamIsVar, ReturnIsVar = m.ReturnIsVar,
                    ParamIsByrefVar = m.ParamIsByrefVar,
                });
            foreach (var p in def.Properties)
                clone.Properties.Add(new PropertyDef
                {
                    Name = p.Name, Owner = clone, TypeName = p.TypeName,
                    GetterName = p.GetterName, SetterName = p.SetterName,
                    IndexParamCount = p.IndexParamCount,
                });
            foreach (var iname in def.InterfaceNames)
                clone.InterfaceNames.Add(iname);
            genericInstTypes[gKey] = clone;
            types.Add(clone);
        }
        // Snapshot: EnsureClone appends to `types` during the scan.
        foreach (var ct in types.ToList())
        {
            if (ct.IsInterface || IsDelegateTypeDef(ct)) continue;
            foreach (var cm in ct.Methods)
            {
                if (cm.IsPInvoke || cm.IsAbstract) continue;
                MethodBodyInfo? scanInfo;
                try { scanInfo = MethodBodyReader.Read(pe, md, ct, cm); }
                catch { continue; }
                if (scanInfo == null) continue;
                foreach (var lk in scanInfo.LocalKinds)
                {
                    if (lk == null || !lk.StartsWith("g:", StringComparison.Ordinal)) continue;
                    var gParts = lk[2..].Split(':');
                    if (gParts.Length < 2) continue;
                    EnsureClone(gParts[0], gParts[1]);
                }
                // R2b scan source 2: newobj-only instantiations (e.g.
                // `new Box<string>()` with no local). The newobj operand is a
                // METHOD token (0x73), NOT a TypeSpec -- the constructed type
                // lives in the MemberRef's PARENT (a TypeSpec for a generic
                // instantiation). A3/A9 anchor: MethodDef (same-assembly
                // generic ctor) and MethodSpec (generic-method token) have no
                // useful TypeSpec here.
                try
                {
                    foreach (var ins in IlDecoder.DecodeMethod(scanInfo.IlBytes, md))
                    {
                        if (ins.OpCode != System.Reflection.Metadata.ILOpCode.Newobj) continue;
                        var h = MetadataTokens.Handle((int)ins.Operand);
                        if (h.Kind != HandleKind.MemberReference) continue;
                        var mr2 = md.GetMemberReference((MemberReferenceHandle)h);
                        if (mr2.Parent.Kind != HandleKind.TypeSpecification) continue;
                        var inst = MethodBodyReader.TryClassGenericInstanceKind(
                            md, (TypeSpecificationHandle)mr2.Parent);
                        if (inst == null) continue;
                        var ip = inst[2..].Split(':');
                        if (ip.Length >= 2) EnsureClone(ip[0], ip[1]);
                    }
                }
                catch { }
            }
        }
        // R2b scan source 3: model strings -- a generic-inst BASE type
        // (VirtStr : VirtBase<string> decodes BaseTypeName "VirtBase`1<System.String>")
        // and generic-inst FIELD types carry the angle-bracket canonical name;
        // parse and clone the definitions they instantiate.
        void ScanModelName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return;
            var lt = name.IndexOf('<');
            if (lt < 0 || !name.EndsWith(">", StringComparison.Ordinal)) return;
            var defName = name[..lt];
            if (!defName.Contains('`')) return;
            var inner = name[(lt + 1)..^1];
            var suffix = inner switch
            {
                "System.String" => "string",
                "System.Object" => "object",
                "System.Int32" => "int",
                "System.Int64" => "long",
                "System.Double" => "double",
                "System.Single" => "float",
                "System.Boolean" => "bool",
                "System.Char" => "char",
                "System.Byte" => "byte",
                _ => null,
            };
            if (suffix != null) EnsureClone(defName, suffix);
        }
        foreach (var ct in types.ToList())
        {
            if (ct.InstantiationSuffix != null) continue;
            ScanModelName(ct.BaseTypeName);
            foreach (var f in ct.InstanceFields) ScanModelName(f.FieldTypeName);
            foreach (var f in ct.StaticFields) ScanModelName(f.FieldTypeName);
        }

        // R2b clone linking: the clones were copied BEFORE the loader's
        // second-pass resolution, so their BaseType/InterfaceTypes are unset.
        // Idempotent guard -- only fills what is still null/empty, existing
        // links untouched. Resolution = the shared ResolveInstantiationOwner
        // (suffix-exact clone -> exact FullName -> F1 argument-aware branch ->
        // generic-arity prefix): single implementation (C6), identical to the
        // callvirt/devirt owner resolution.
        {
            foreach (var t in types)
            {
                if (t.BaseType == null && t.BaseTypeName != "System.Object"
                    && ResolveInstantiationOwner(types, t.BaseTypeName) is { } bt)
                    t.BaseType = bt;
                if (t.InterfaceTypes.Count == 0 && t.InterfaceNames.Count > 0)
                {
                    foreach (var iname in t.InterfaceNames)
                        if (ResolveInstantiationOwner(types, iname) is { } it && !t.InterfaceTypes.Contains(it))
                            t.InterfaceTypes.Add(it);
                }
            }
        }

        // Task 7: virtual-slot planning pass -- computes every type's vtable
        // (base-first slots, override replacement) BEFORE any emission so the
        // descriptor/vtable/body generation all see the final slot layout.
        VTablePlanner.PlanAll(types);

        // Rule 2j: property-folding pre-scan -- simple accessors (get/set that
        // directly return/store a same-type field) are folded into field
        // accesses at every call site and their FUNCTIONS are not emitted.
        var accessors = AccessorAnalyzer.Scan(pe, md, types);
        // OPT-2 (escape-analysis wiring, AllocPromoPass.cs header ordering
        // contract): fold every translatable method once into an inline
        // candidate table. Each emitted method then runs InlinePass (small
        // pure ctor/getter expansion) BEFORE AllocPromoPass -- the inlined
        // ctor/getter bodies expose the field write/read on the local object,
        // which the promotion pass then replaces with scalars -- and DCE
        // cleans the introduced scalar stores. Virtual methods and ref
        // returns are never candidates (InlinePass.IsInlineable guards).
        var inlineCandidates = new Dictionary<string, InlineCandidate>();
        foreach (var ct in types)
        {
            if (ct.IsInterface) continue;
            foreach (var cm in ct.Methods)
            {
                if (cm.IsPInvoke || cm.IsAbstract) continue;
                if (IsDelegateTypeDef(ct)) continue;
                MethodBodyInfo? candInfo;
                try { candInfo = MethodBodyReader.Read(pe, md, ct, cm); }
                catch (Exception) { Console.Error.WriteLine($"warning: inline candidate read failed for {ct.FullName}::{cm.Name} -> skipped"); continue; }
                if (candInfo == null) continue;
                if (accessors.ContainsKey(candInfo.MethodToken)) continue;
                var candInsns = IlDecoder.DecodeMethod(candInfo.IlBytes, md);
                List<Stmt> candStmts;
                try
                {
                    candStmts = StackFold.Fold(candInsns, new MethodContext
                    {
                        ArgCount = candInfo.ArgCount,
                        LocNames = candInfo.LocNames,
                        LocalKinds = candInfo.LocalKinds,
                        ReturnsVoid = candInfo.ReturnsVoid,
                        ExceptionRegions = candInfo.ExceptionRegions,
                        MethodFullName = $"{ct.FullName}.{cm.Name}",
                        CctorOwnerFullName = cm.Name == ".cctor" ? TypeEmitter.ReflectionFullName(ct) : null,
                    }, md, types, accessors, pe);
                }
                catch (Exception)
                {
                    Console.Error.WriteLine($"warning: inline fold failed for {ct.FullName}::{cm.Name} -> skipped");
                    continue;
                }
                var candFn = $"call_{candInfo.MethodToken}";
                inlineCandidates[candFn] = new InlineCandidate
                {
                    Function = candFn,
                    ParamCount = candInfo.ArgCount,
                    ReturnsVoid = candInfo.ReturnsVoid,
                    ReturnsRef = !candInfo.ReturnsVoid && candInfo.ReturnType != SignatureTypeCode.Int32,
                    HasExceptionRegions = candInfo.ExceptionRegions.Count > 0,
                    IsVirtual = cm.IsVirtual,
                    Body = candStmts,
                    // D1 gate: struct-typed locals (the inline temps would
                    // misdeclare -- see InlinePass.IsInlineable).
                    LocalKinds = candInfo.LocalKinds,
                };
            }
        }
        // M5.5 reflection (refl2 S2/G1): the folded-accessor guard is REMOVED.
        // A type whose simple accessors were folded has no emitted get_X/set_X
        // function symbols, but EmitTypeInfo now receives the type's folded
        // method-name set (below) and emits fnPtr=nullptr for those methods --
        // the table stays intact, no undefined-symbol link error. Only the
        // per-type folded METHOD names are passed (foldedAccessorNames), NOT
        // a whole-type exclusion.
        var foldedAccessorNames = accessors.Values
            .GroupBy(a => a.OwnerFullName)
            .ToDictionary(g => g.Key,
                g => g.Select(a => a.MethodName).ToHashSet(),
                StringComparer.Ordinal);
        // M5.5 reflection (refl2 S1/G2): the return-encodability guard is
        // REMOVED -- MethodDef.ReturnTypeName (MethodSignatureCoder.EncodeReturn,
        // D1) carries the full encoded name for 0x11 VALUETYPE / 0x12 CLASS /
        // 0x15 GENERICINST (and SZARRAY/BYREF/PTR/ARRAY/FNPTR) returns, so
        // TypeEmitter.EncodeReturnType names EVERY return and no method-table
        // emission LOUDs.
        // refl2 S3 (D4): mode B reflection scope. CliOptions is now consumed by
        // the generator: --reflection-list <manifest> + granularity members
        // limits TypeInfo table emission to the manifest's Kept types (the
        // FilterReflectionManifest result, plus the base/interface CLOSURE --
        // a kept type's table references &{Base}_typeinfo / &{Iface}_typeinfo,
        // which must also emit or the link fails). granularity all (mode A) and
        // no-manifest (default) emit every type (null scope).
        var reflectedNames = ReflectionScopeFromOptions(options, types);
        // Residual whole-type exclusion: a type whose TypeCppName is STILL not
        // a valid C++ identifier (Sanitize handles '=' -- G3 -- but any other
        // unforseen metadata character would leave an uncompilable table/
        // extern name). Conservative safety net, never the '=' case.
        var noTypeInfoOwners = types
            .Where(t => !IsValidCppIdentifier(TypeEmitter.TypeCppName(t)))
            .Select(t => t.FullName)
            .ToHashSet();

        var allSrc = new List<string>();

        // T3 string pool counters: methods/folded must match ScanAssembly's
        // accounting (both count only emitted bodies -- interface/PInvoke/
        // abstract/folded-accessor skips above are shared).
        var foldedCount = 0;
        var methodCount = 0;

        // r78 fix: two-pass forward declaration strategy.
        // Pass 0: collect field-referenced types NOT in the types list (BCL internal types
        // like System_Runtime_Serialization_SafeSerializationManager, MdaHelper, TlsContents, etc.)
        // and emit a forward declaration for each. Without this, fields/methods referencing these
        // types generate "unknown type name" compile errors.
        var typesInList = new HashSet<string>(types.Select(t => t.FullName), StringComparer.Ordinal);
        var fwdDeclNames = new HashSet<string>(StringComparer.Ordinal);
        // P/Invoke dedupe: extern "C" forbids overloads, so the same native
        // entry name must be emitted at most once even when several managed
        // overloads share it (e.g. DuplicateHandle x2 with different signatures).
        var emittedPinvokeDecls = new HashSet<string>(StringComparer.Ordinal);
        // Win32 SDK typedef collision set for BCL forward declarations.
        var Win32CollidingForwardNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "COORD", "SMALL_RECT", "LUID", "SID_AND_ATTRIBUTES",
            "SECURITY_ATTRIBUTES", "WIN32_FILE_ATTRIBUTE_DATA",
            "IDispatch", "Record",
        };
        // Pure-icall wrappers: the same method metadata can be visited twice --
        // emit each wrapper once (same-name same-signature duplicates).
        var emittedPureWrappers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ct in types)
        {
            foreach (var f in ct.InstanceFields)
                if (!string.IsNullOrEmpty(f.FieldTypeName) && !f.FieldTypeName.Contains(':'))
                    fwdDeclNames.Add(f.FieldTypeName);
            foreach (var f in ct.StaticFields)
                if (!string.IsNullOrEmpty(f.FieldTypeName) && !f.FieldTypeName.Contains(':'))
                    fwdDeclNames.Add(f.FieldTypeName);
            // r78 fix: also scan method return types and parameter types for
            // referenced BCL types not in the list.
            foreach (var m in ct.Methods)
            {
                if (!string.IsNullOrEmpty(m.ReturnTypeName) && !m.ReturnTypeName.Contains(':'))
                    fwdDeclNames.Add(m.ReturnTypeName);
                foreach (var p in m.ParamTypes.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var paramType = p.Trim();
                    if (!string.IsNullOrEmpty(paramType) && !paramType.Contains(':'))
                        fwdDeclNames.Add(paramType);
                }
            }
            // r78 fix: scan base type and interface names — types like
            // System_Reflection_MethodBase are base types of loaded types
            // but are not themselves in the types list.
            if (!string.IsNullOrEmpty(ct.BaseTypeName) && !ct.BaseTypeName.Contains(':'))
                fwdDeclNames.Add(ct.BaseTypeName);
            foreach (var iface in ct.InterfaceNames)
                if (!string.IsNullOrEmpty(iface) && !iface.Contains(':'))
                    fwdDeclNames.Add(iface);
        }
        foreach (var name in fwdDeclNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            var fwdSan = TypeEmitter.Sanitize(name);
            // r79 fix: strip trailing underscore from generic instantiations
            // (Sanitize splits on '<' and '>' and joins with '_', so
            // "List`1<int>" -> "List_1_int_"; the struct definition uses
            // TypeCppName which emits "List_1_int" -- the forward declaration
            // must match or the linker sees two different type names).
            if (fwdSan.EndsWith("_"))
                fwdSan = fwdSan[..^1];
            // Win32 SDK collision set (verified typedef intersection):
            // windows.h already defines these; a duplicate `struct X;`
            // conflicts with the SDK typedef. LUID_AND_ATTRIBUTES has zero
            // code references -- drop it. The rest resolve to the SDK types
            // on Windows (blittable mirrors) and keep the BCL forward
            // declaration elsewhere.
            if (fwdSan == "LUID_AND_ATTRIBUTES") continue;
            if (Win32CollidingForwardNames.Contains(fwdSan))
            {
                generatedHeader.Line($"#ifndef _WIN32");
                generatedHeader.Line($"struct {fwdSan}; // r78: forward-declared BCL type");
                generatedHeader.Line($"#endif");
                continue;
            }
            generatedHeader.Line($"struct {fwdSan}; // r78: forward-declared BCL type");
        }

        // M2 type emission: struct layout definitions all live in generated.h (cross-type visible);
        // Il2CppClass descriptor definitions go in each type's own .cpp (single definition, ODR safe);
        // generated.h emits extern declarations only, for entry.cpp registration. Interfaces have no
        // instance struct, but their descriptors (vtable slot layout + registration) are referenced by the implementing types' map tables.
        foreach (var t in types)
        {
            if (t.IsInterface)
            {
                generatedHeader.Line($"struct {TypeEmitter.TypeCppName(t)};");  // r77 fix: forward declare interface
                generatedHeader.Line($"extern Il2CppClass {TypeEmitter.TypeCppName(t)}_class;");
                continue;
            }
            if (t.IsValueType)
            {
                // M3 Task 11: value types have a struct (plain C++ value) but
                // NO class descriptor (they are not GC classes).
                if (t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor"))
                    generatedHeader.Line(TypeEmitter.EmitClassStruct(t, types));
                else
                    // Fieldless/ctorless value types (compiler-generated async
                    // awaiters): an empty struct still needs a DEFINITION --
                    // fwd-decl + by-value use without one is "incomplete
                    // type" at every use site. No descriptor (M3 shape kept).
                    generatedHeader.Line($"struct {TypeEmitter.TypeCppName(t)} {{}};");
                // P25b (user struct box): user value types DO get a runtime
                // descriptor ({T}_class, defined in the .cpp, registered by
                // entry.cpp). The box icall stamps boxed instances with it
                // (identity + isinst object/ValueType via the ValueType base
                // chain) and il2cpp_unbox_user_struct validates against it.
                // Reference-free MVP: structs with GC reference fields are
                // rejected at box/unbox sites, so the descriptor's
                // refFieldBitmap stays 0 (the collector never scans these
                // boxes). Interface types / delegates / fieldless empty structs
                // keep the M3 shape (no descriptor).
                if (t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor"))
                    generatedHeader.Line($"extern Il2CppClass {TypeEmitter.TypeCppName(t)}_class;");
                continue;
            }
            if (!(t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor"))) continue;
            // CG6 (delegate2/delegatecb link FAIL): a custom delegate TypeDef
            // (IntFn/Cb) has NO descriptor (the runtime stamps every delegate
            // with its single System.Delegate class) -- neither the class
            // struct nor the extern exists for it.
            if (IsDelegateTypeDef(t)) continue;
            generatedHeader.Line(TypeEmitter.EmitClassStruct(t, types));
            generatedHeader.Line($"extern Il2CppClass {TypeEmitter.TypeCppName(t)}_class;");
        }

        // G6 skip-stub: a fold-rejected body would otherwise leave `entry.cpp`
        // and the typeinfo table referencing a symbol that is never defined
        // ("use of undeclared identifier" -- caudit2/dtaudit2). Emit a typed
        // stub that throws a managed NotSupportedException at run time: the
        // gap stays LOUD, the rest of the case stays usable.
        void EmitNotTranspiledStub(CppWriter w, TypeDef t, MethodDef m,
                                   MethodBodyInfo info, string ret)
        {
            var cppMethodName = MethodCppName(t, m);
            var paramNames = Enumerable.Range(0, info.ArgCount).Select(i => $"arg{i}").ToArray();
            var paramTypes = ParamTypesCpp(m, info);
            var body = "    il2cpp_throw_new(\"System.NotSupportedException,System.Exception\", " +
                       $"\"this method was not transpiled (M5 mapping gap): {t.FullName}::{m.Name} -- see the transpile log\");\n";
            if (ret != "void")
                body += "    return {}; // unreachable (the throw above never returns)\n";
            w.Line("//------------------------------------------------------------------------------");
            w.Line($"// NOT TRANSPILED: {t.FullName}::{m.Name} -- the fold rejected the body;");
            w.Line("// this stub keeps entry.cpp and the typeinfo table linkable and reports the gap");
            w.Line("// as a managed NotSupportedException at run time.");
            w.Line("//------------------------------------------------------------------------------");
            w.Line(MethodEmitter.EmitMethod(cppMethodName, paramNames, ret, body, paramTypes));
            w.Line();
            generatedHeader.Line($"{ret} {cppMethodName}({ParamListTyped(paramNames, paramTypes)});");
        }

        foreach (var t in types)
        {
            var cppName = TypeEmitter.TypeCppName(t);
            var typeEmittedMethod = false;
            // Link-gate fix (arrayops): methodless types (e.g. compiler-generated
            // <PrivateImplementationDetails>.__StaticArrayInitTypeSize=N blobs) emit
            // NO TU today, but entry.cpp still registers their &{T}_typeinfo
            // (ShouldEmitTypeInfo passes) -> undefined-symbol link error. A TU that
            // carries only the TypeInfo table restores the table/registration pair.
            var typeEmittedTypeInfo = false;
            var w = new CppWriter();
            w.Section("generated by il2cppc");
            w.Line("// source assembly: " + Path.GetFileName(assemblyPath));
            w.Line("// DO NOT EDIT");
            w.Line();
            // Method declarations all go in generated.h: cross-method/cross-type forward references (Main emitted before Add can still see Add's declaration).
            w.Line("#include \"generated.h\"");
            w.Line();

            // Static-constructor guard (Task 6, beforefieldinit lazy semantics): the type's first use
            // (static-field access / instantiation / static call) runs .cctor first. Guard = file-scope
            // bool + one call (M2 simple single-threaded guard; M3 double-checked-lock threading); types
            // without .cctor generate nothing (zero overhead). audit #5: the done flag has EXTERNAL
            // linkage -- the inline check (`if (done == false) ensure_cctor_X();`) appears in other
            // types' .cpp (cross-TU static-field access); file-static would fail to compile (use of undeclared identifier); generated.h emits extern.
            var cctor = t.Methods.FirstOrDefault(m => m.Name == ".cctor");
            if (cctor != null && cctor.IsStatic)
            {
                w.Line($"// Static-constructor guard for {t.FullName} (beforefieldinit lazy semantics).");
                w.Line($"// First call runs .cctor exactly once; later calls are no-ops.");
                w.Line($"// The done flag has EXTERNAL linkage: the inline done-check emitted at");
                w.Line($"// static-field access sites in OTHER translation units reads it directly.");
                w.Line($"bool {cppName}_cctor_done = false;");
                w.Line($"static bool {cppName}_cctor_running = false;");
                w.Line($"static const char* {cppName}_cctor_exc_type = nullptr;");
                w.Line($"static const char* {cppName}_cctor_exc_msg = nullptr;");
                // msvc C4702 (cctorchain): when the .cctor body itself ends in
                // a throw (an [[noreturn]] il2cpp_throw_new call, visible to
                // MSVC's same-TU flow analysis), the post-catch bookkeeping
                // here is provably unreachable. It is CORRECT code (needed
                // whenever the .cctor CAN return), so suppress the diagnostic
                // for this generated template only -- the structure is fully
                // generator-controlled.
                w.Line("#if defined(_MSC_VER)");
                w.Line("#pragma warning(push)");
                w.Line("#pragma warning(disable:4702)");
                w.Line("#endif");
                w.Line($"IL2CPP_NOINLINE void ensure_cctor_{cppName}(void) {{");
                w.Open();
                w.Line($"if ({cppName}_cctor_done) return;");
                w.Line($"if ({cppName}_cctor_running) return;");
                w.Line($"if ({cppName}_cctor_exc_type != nullptr) throw il2cpp::DotNetException({cppName}_cctor_exc_type, {cppName}_cctor_exc_msg);");
                w.Line($"{cppName}_cctor_running = true;");
                w.Line($"try {{");
                w.Open();
                w.Line($"{MethodCppName(t, cctor)}();");
                w.Close();
                w.Line($"}} catch (il2cpp::DotNetException& __il2cpp_cctor_ex) {{");
                w.Open();
                w.Line($"{cppName}_cctor_exc_type = __il2cpp_cctor_ex.type_name();");
                w.Line($"{cppName}_cctor_exc_msg = __il2cpp_cctor_ex.message_text();");
                w.Line($"{cppName}_cctor_running = false;");
                w.Line($"throw; // rethrow the cached cctor failure");
                w.Close();
                w.Line($"}} catch (...) {{");
                w.Open();
                w.Line($"{cppName}_cctor_running = false;");
                w.Line($"throw;");
                w.Close();
                w.Line($"}}");
                w.Line($"{cppName}_cctor_running = false;");
                w.Line($"{cppName}_cctor_done = true;");
                w.Close();
                w.Line("}");
                w.Line("#if defined(_MSC_VER)");
                w.Line("#pragma warning(pop)");
                w.Line("#endif");
                w.Line();
                generatedHeader.Line($"extern bool {cppName}_cctor_done;");
                generatedHeader.Line($"void ensure_cctor_{cppName}(void);");
            }

            // Task 7: virtual table (must precede descriptor emission -- the
            // descriptor references the table). Each slot's C++ name is the
            // effective method at that slot (base-inherited or overridden).
            // delechain: custom delegate TypeDefs emit NO vtable -- their
            // methods are runtime-provided and the fold routes construction/
            // Invoke through the runtime delegate machinery (methodPtr).
            if (t.VirtualMethods.Count > 0 && !t.IsInterface && !IsDelegateTypeDef(t))
            {
                // ValueType.GetHashCode vtable slots: the BCL method has no
                // body and the slot-name synthesis bypasses the MethodMap, so
                // the slot would reference an undefined generated function
                // (link error in every TU with a value-type vtable). Route
                // the slot at the existing OfPtr icall (declared in stubs.h,
                // in the generated include chain). It returns the 0
                // placeholder -- these slots belong to dead
                // StaticArrayInitType vtables in practice.
                var slotNames = t.VirtualMethods.Select(m =>
                    m.Owner.MatchesFullName("System.ValueType") && m.Name == "GetHashCode"
                        ? "il2cpp::runtime::icall_value_type_get_hash_code_of_ptr"
                        : MethodCppName(m.Owner, m)).ToList();
                w.Line(TypeEmitter.EmitVTable(t, slotNames));
                generatedHeader.Line($"extern void* {cppName}_vtable[];");
            }

            // Task 8: interface dispatch maps (concrete types only -- an
            // interface type has no maps, only the slot layout).
            string? ifaceMapName = null;
            var ifaceMapCount = 0;
            if (!t.IsInterface && t.InterfaceSlotMaps.Count > 0)
            {
                ifaceMapName = $"{cppName}_interfaces";
                ifaceMapCount = t.InterfaceSlotMaps.Count;
                w.Line(TypeEmitter.EmitInterfaceMaps(t));
                generatedHeader.Line($"extern InterfaceMapEntry {cppName}_interfaces[];");
            }

            // Type descriptor definitions (single definition point, ODR safe; generated.h has extern declarations only).
            // M3 Task 11: value types have NO descriptor (not GC classes).
            // delechain: custom delegate TypeDefs have NO descriptor either --
            // delegate objects are runtime Il2CppDelegate instances, never
            // il2cpp_object_new(&..._class) allocations.
            // P25b (user struct box): user value types DO emit a descriptor --
            // the box icall needs {T}_class (instanceSize = sizeof(payload),
            // baseClass = &System_ValueType_class) to stamp boxed instances so
            // isinst/castclass and il2cpp_unbox_user_struct's ValueType-chain
            // check resolve. The condition mirrors the generated.h extern gate
            // (field-bearing or .ctor-bearing structs only).
            if ((!t.IsValueType && !IsDelegateTypeDef(t) && (t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor")
                || t.VirtualMethods.Count > 0 || t.IsInterface))
                || (t.IsValueType && !IsDelegateTypeDef(t) && (t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor"))))
            {
                w.Line(TypeEmitter.EmitClassDescriptor(t,
                    t.VirtualMethods.Count > 0 && !t.IsInterface ? $"{cppName}_vtable" : null,
                    null, t.VirtualMethods.Count,
                    ifaceMapName, ifaceMapCount));
            }
            w.Line(TypeEmitter.EmitStaticStorage(t, md));
            // M5.5 reflection (R-REFL-01): emit the per-type TypeInfo metadata
            // table (typeinfo.h) alongside the descriptor/static storage. Mode
            // A = ALL types (including interfaces -- an implementing type's
            // interface array references &{Interface}_typeinfo). refl2 S3:
            // mode B (manifest + members granularity) limits the scope to the
            // manifest's Kept types + base/interface closure. refl2 S2: the
            // type's folded-accessor method names go to EmitTypeInfo so those
            // methods get fnPtr=nullptr (no undefined-symbol link error).
            if (ShouldEmitTypeInfo(t, reflectedNames, noTypeInfoOwners))
            {
                var typeInfoBlock = TypeEmitter.EmitTypeInfo(t, reflectedNames, md, foldedAccessorNames.GetValueOrDefault(t.FullName));
                if (typeInfoBlock.Length > 0)
                {
                    w.Line(typeInfoBlock);
                    typeEmittedTypeInfo = true;
                }
            }
            w.Line();
            foreach (var m in t.Methods.Where(m => m.IsStatic || m.Name == ".ctor" || IsUserInstanceMethod(m)))
            {
                // Interface methods have no body (RVA=0, abstract contract) -- emit declarations only,
                // no implementations. Interface types are never instantiated; their vtable slots are
                // carried by the implementing types' maps. delechain: custom delegate types behave the
                // same -- .ctor/Invoke/BeginInvoke/EndInvoke are "runtime managed" MethodDefs (no IL
                // body, RVA=0), provided by the runtime; the fold routes construction/Invoke through the runtime delegate machinery.
                if (t.IsInterface || IsDelegateTypeDef(t)) continue;
                // M4 Task 1: P/Invoke -- no IL body; emit a native import
                // declaration (the symbol lives in the linked native library).
                if (m.IsPInvoke)
                {
                    // Dedupe: same native entry emitted once (first overload wins).
                    if (!emittedPinvokeDecls.Add(m.PinvokeEntryName ?? m.Name))
                        continue;
                    // Mapped-QCall decl suppression: a QCall the MethodMap
                    // covers routes to an icall at every call site (the
                    // resolver short-circuit), so its naked extern decl is
                    // dead -- and deadly when it collides with a system
                    // header (e.g. GetCommandLine). Unmapped QCalls keep
                    // the legacy decl. Wrapped: map LOUD must not kill decls.
                    if ((m.PinvokeModule ?? "").Equals("QCall", StringComparison.Ordinal))
                    {
                        try
                        {
                            var qHandle = FindMethodHandle(md, m);
                            if (qHandle.HasValue)
                            {
                                var qMi = md.GetMethodDefinition(qHandle.Value);
                                var qOwner = md.GetFullName(qMi.GetDeclaringType());
                                var qmap = MethodRefResolver.BclResolve(md, qOwner, m.Name, qMi.Signature, null, null, false, null, null);
                                if (qmap != null) continue;
                            }
                        }
                        catch { }
                    }
                    // P-D4 P1: an ordinal EntryPoint ("#N") can never compile
                    // ('#' is not a C++ identifier) and plat_dl_sym resolves
                    // by name -- LOUD-reject at the declaration side (the
                    // resolver guards the called path; this guards even a
                    // declared-but-never-called entry). Full ordinal support
                    // is deferred to P3.
                    var piEntry = m.PinvokeEntryName ?? m.Name;
                    if (!string.IsNullOrEmpty(piEntry) && piEntry.StartsWith("#", StringComparison.Ordinal))
                        throw new NotSupportedException(
                            $"P/Invoke EntryPoint='{piEntry}' (ordinal entry) is not supported: '#' is not a valid native symbol name in this transpiler (P-D4)");
                    // P-D4 (mixed loading): a DYNAMIC library (out-of-manifest
                    // or SHARED) gets NO extern "C" declaration -- the symbol
                    // would be an undefined link reference, defeating the
                    // runtime resolution every call site emits. Only the
                    // static-link libraries keep the declaration (zero-overhead
                    // direct calls, the established ABI).
                    var pinvokeDynamic = PinvokeModuleIsDynamic(m.PinvokeModule ?? "");
                    if (pinvokeDynamic)
                    {
                        w.Line($"// P/Invoke (P-D4): dynamic import from '{m.PinvokeModule}' -- resolved at RUNTIME via il2cpp_pinvoke_resolve (no extern \"C\" declaration: the library is not linked into the exe).");
                        generatedHeader.Line($"// P/Invoke (P-D4): dynamic import from '{m.PinvokeModule}' -- resolved at RUNTIME via il2cpp_pinvoke_resolve (no extern \"C\" declaration: the library is not linked into the exe).");
                        w.Line();
                        continue;
                    }
                    var piRet = PinvokeReturnTypeCpp(md, m);
                    var piParams = PinvokeParamList(md, m);
                    var piDecl = $"extern \"C\" {piRet} {m.PinvokeEntryName}({piParams});";
                    w.Line($"// P/Invoke (M4 Task 1): native import from '{m.PinvokeModule}' ([DllImport]).");
                    // Guard: Windows API functions are declared in windows.h -- don't redeclare
                    // on Windows to avoid "functions that differ only in their return type" overload
                    // conflicts (generated int32_t vs Windows SDK BOOL/LPVOID/etc).
                    var pinvokeMod = m.PinvokeModule ?? "";
                    // Any Windows system library (kernel32/user32/advapi32/shell32/
                    // ole32/oleaut32/crypt32/secur32/bcrypt/ntdll/combase/mscoree/clr/
                    // api-ms-win-*) is declared in windows.h -- don't redeclare on
                    // Windows to avoid overload conflicts. QCall is not a DLL.
                    var pinvokeBase = pinvokeMod.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        ? pinvokeMod.Substring(0, pinvokeMod.Length - 4) : pinvokeMod;
                    var isWindowsDll = pinvokeMod.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.StartsWith("api-ms-win-", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("kernel32", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("user32", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("gdi32", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("comdlg32", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("comctl32", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("advapi32", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("shell32", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("ole32", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("oleaut32", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("crypt32", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("secur32", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("bcrypt", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("ntdll", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("combase", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("mscoree", StringComparison.OrdinalIgnoreCase)
                        || pinvokeBase.Equals("clr", StringComparison.OrdinalIgnoreCase);
                    if (isWindowsDll)
                    {
                        w.Line($"#ifdef _WIN32");
                        w.Line($"// '{m.PinvokeEntryName}' declared in windows.h -- not redeclared to avoid overload conflicts");
                        w.Line($"#else");
                        w.Line(piDecl);
                        w.Line($"#endif");
                        generatedHeader.Line($"#ifdef _WIN32");
                        generatedHeader.Line($"// '{m.PinvokeEntryName}' declared in windows.h -- not redeclared to avoid overload conflicts");
                        generatedHeader.Line($"#else");
                        generatedHeader.Line(piDecl);
                        generatedHeader.Line($"#endif");
                    }
                    else
                    {
                        w.Line(piDecl);
                        generatedHeader.Line(piDecl);
                    }
                    w.Line();
                    continue;
                }
                // delechain: ABSTRACT methods have no IL body (RVA=0) by
                // contract -- custom delegate TypeDefs declare .ctor/Invoke/
                // BeginInvoke/EndInvoke as abstract runtime-provided methods
                // (Roslyn shape); the fold routes delegate construction and
                // Invoke through the runtime delegate machinery, so nothing
                // is emitted here. Reading them would crash (no body).
                if (m.IsAbstract) continue;
                // r78 fix: pure icall methods have RVA=0 (no IL body). If MethodRefResolver
                // can map the method to a runtime icall, emit a wrapper that calls it directly.
                // Previously these were silently skipped by the MethodBodyReader.Read try/catch.
                if (!m.IsPInvoke)
                {
                    var pureHandle = FindMethodHandle(md, m);
                    if (pureHandle.HasValue)
                    {
                        var pMi = md.GetMethodDefinition(pureHandle.Value);
                        if (pMi.RelativeVirtualAddress == 0)
                        {
                            var pureOwner = md.GetFullName(pMi.GetDeclaringType());
                            var pureName = md.GetString(pMi.Name);
                            var (purePc, pureRet, purePt, pureRetName) = MethodSignatureCoder.Decode(md, pMi.Signature);
                            ResolvedMethod? pureRes = null;
                            try { pureRes = MethodRefResolver.BclResolve(md, pureOwner, pureName, pMi.Signature, null, null, false, null, null); }
                            catch { pureRes = null; }
                            if (pureRes != null)
                            {
                                // Visit-level dedupe: same method emitted once.
                                if (!emittedPureWrappers.Add(MethodCppName(t, m)))
                                    continue;
                                // Strip the signature-codec kind prefix (r:/V:/T:/P:) --
                                // TypeCppName cannot parse it and would leak "r:Foo"
                                // into the header ("unknown type name 'r'").
                                var pureRetStripped = pureRetName;
                                if (pureRetStripped != null)
                                {
                                    foreach (var pfx in new[] { "r:", "V:", "T:", "P:" })
                                        if (pureRetStripped.StartsWith(pfx, StringComparison.Ordinal))
                                        { pureRetStripped = pureRetStripped.Substring(pfx.Length); break; }
                                }
                                var pureRetCpp = !string.IsNullOrEmpty(pureRetStripped)
                                    // SZARRAY return ("u1[]" etc.): a managed
                                    // array is Il2CppArray*, never a Sanitize
                                    // short name ("unknown type name 'u1__'").
                                    ? (pureRetStripped.EndsWith("[]", StringComparison.Ordinal)
                                        ? "Il2CppArray*" : TypeCppName(pureRetStripped))
                                    : (pureRet == SignatureTypeCode.Void ? "void" : ReturnTypeCpp(pureRet));
                                var pureParams = new List<string>();
                                var pureIdx = 0;
                                if ((pMi.Attributes & System.Reflection.MethodAttributes.Static) == 0)
                                    pureParams.Add("Il2CppObject* arg" + (pureIdx++));
                                foreach (var pch in SplitTopLevel(purePt).Where(s => s.Length > 0).Take(purePc))
                                {
                                    var pk = pch.Trim();
                                    // By-ref value-type params (B:V:T, e.g. FCall
                                    // decimal slots) carry ADDRESSES -- declare
                                    // the pointed-to C++ type, never int32_t
                                    // (pointer truncation on x64).
                                    if (pk.StartsWith("B:V:", StringComparison.Ordinal))
                                    {
                                        pureParams.Add(TypeCppName(pk[4..]) + "* arg" + (pureIdx++));
                                        continue;
                                    }
                                    // Value-type params (V:T): the full struct type
                                    // (e.g. V:System.Decimal -> Il2CppDecimal).
                                    if (pk.StartsWith("V:", StringComparison.Ordinal))
                                    {
                                        pureParams.Add(TypeCppName(pk[2..]) + " arg" + (pureIdx++));
                                        continue;
                                    }
                                    var isRef = pk.StartsWith("r:", StringComparison.Ordinal) || pk == "System.Object" || pk == "System.String";
                                    pureParams.Add((isRef ? "Il2CppObject*" : "int32_t") + " arg" + (pureIdx++));
                                }
                                var pureSigLine = $"{MethodCppName(t, m)}({string.Join(", ", pureParams)})";
                                w.Line($"IL2CPP_NOINLINE {pureRetCpp} {pureSigLine}");
                                w.Line("{");
                                w.Open();
                                var pureArgs = new List<string>();
                                if ((pMi.Attributes & System.Reflection.MethodAttributes.Static) == 0)
                                    pureArgs.Add("arg0");
                                for (var ai = 0; ai < purePc; ai++) pureArgs.Add("arg" + (ai + ((pMi.Attributes & System.Reflection.MethodAttributes.Static) == 0 ? 1 : 0)));
                                if (pureRet == SignatureTypeCode.Void)
                                    w.Line($"{pureRes.CppName}({string.Join(", ", pureArgs)});");
                                else
                                    w.Line($"return {pureRes.CppName}({string.Join(", ", pureArgs)});");
                                w.Close();
                                w.Line("}");
                                w.Line();
                                generatedHeader.Line($"extern \"C\" {pureRetCpp} {pureSigLine};");
                                continue;
                            }
                        }
                    }
                }
                MethodBodyInfo bodyInfo;
                try { bodyInfo = MethodBodyReader.Read(pe, md, t, m); }
                catch (Exception) { Console.Error.WriteLine($"warning: method body read failed for {t.FullName}::{m.Name} -> skipped (no stub: no body info for the signature)"); continue; }
                if (accessors.ContainsKey(bodyInfo.MethodToken)) continue;
                var insns = IlDecoder.DecodeMethod(bodyInfo.IlBytes, md);
                var managedRetStruct = ManagedReturnStructName(md, m);
                // VERIFY-FIX (2.7): returnType is computed BEFORE the Guid skip --
                // the stub needs the method's real return type, and the patch's
                // original order referenced the yet-undeclared local (CS0841).
                var returnType = managedRetStruct != null ? TypeCppName(managedRetStruct) : ReturnTypeCpp(bodyInfo.ReturnType);
                if (managedRetStruct == "System.Guid")
                {
                    Console.Error.WriteLine($"warning: {t.FullName}::{m.Name} returns Guid -> skipped");
                    EmitNotTranspiledStub(w, t, m, bodyInfo, returnType);
                    continue;
                }
                // R2b P2: a VAR return in a reference instantiation renders as
                // the managed reference (Il2CppObject*).
                // D5 (arity-0 def): a SHARED method-level generic definition's
                // MVAR return ALSO renders as Il2CppObject* -- the definition
                // body IS the shared object template and its MVAR return slot
                // is the boxed T object. Per-instance arity-0 defs (genlist
                // Id<T>) keep the int32 default.
                var defShared = GenericUsageAnalyzer.SharedGenericDefs?.Contains(t.FullName) == true;
                var methodLevelSharedDef = defShared && t.InstantiationSuffix == null
                    && t.GenericArity == 0 && t.Methods.Any(mm => mm.GenericArity > 0);
                if (managedRetStruct == null && m.ReturnIsVar
                    && (t.InstantiationSuffix != null || methodLevelSharedDef))
                    returnType = "Il2CppObject*";
                _curInstantiationSuffix = t.InstantiationSuffix;
                _curReturnIsVar = m.ReturnIsVar;
                // P-G4 F1: the member check MUST gate BOTH faces. The old shape
                // only applied SharedGenericDefs to the arity-0 clause, so ANY
                // non-shared def's reference clone (suffix != null, != "int" --
                // VirtBase`1_string, PairCon`2_string_string) set _curMethodShare
                // true and wrongly took the shared object ABI for byref-VAR/
                // stobj-VAR bodies. The audit's four-state proof: the only
                // behavior difference is non-shared reference clone true->false
                // (nothing in the current pool has that shape). The shared faces
                // are (a) the arity-0 generic-METHOD definition (Swap -- no clone
                // exists, the definition body IS the object template) and (b) the
                // shared "object" clone of an arity>0 def. The all-int definition
                // of a shared def stays per-instance (its suffix is null AND its
                // methods are not generic -- the arity-0 clause below excludes
                // it). The object clone's suffix is never the "int" default.
                _curMethodShare = defShared && (
                      t.InstantiationSuffix != null && t.InstantiationSuffix != "int"
                   || (t.InstantiationSuffix == null && t.GenericArity == 0
                       && t.Methods.Any(mm => mm.GenericArity > 0)));
                _curStructReturnCpp = managedRetStruct != null ? returnType : null;
                List<Stmt> stmts;
                // R2b P2: the resolution hint for VAR (0x13) TypeSpec args in
                // this body's member references (List`1<T> -> List`1_<suffix>).
                // Save/restore around the fold so the hint cannot leak into
                // later bodies (R1 review item).
                var prevVarHint = MethodRefResolver.InstantiationSuffixHint;
                MethodRefResolver.InstantiationSuffixHint = t.InstantiationSuffix;
                try { stmts = StackFold.Fold(insns, new MethodContext { ArgCount = bodyInfo.ArgCount, LocNames = bodyInfo.LocNames, LocalKinds = bodyInfo.LocalKinds, ReturnsVoid = bodyInfo.ReturnsVoid, ExceptionRegions = bodyInfo.ExceptionRegions, MethodFullName = $"{t.FullName}.{m.Name}", CctorOwnerFullName = m.Name == ".cctor" ? TypeEmitter.ReflectionFullName(t) : null }, md, types, accessors, pe); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"warning: method fold failed -> skipped ({t.FullName}::{m.Name}: {ex.Message})");
                    EmitNotTranspiledStub(w, t, m, bodyInfo, returnType);
                    continue;
                }
                finally { MethodRefResolver.InstantiationSuffixHint = prevVarHint; }
                // P0-1/P0-3: algebraic identities + null-comparison folding
                // (constant propagation already happened inside StackFold.Fold;
                // branch conditions that became constants feed the existing
                // emission-time dead-branch/block elimination).
                // DCE-1 (hard principle 4 -- no dead code in generated code):
                // dead-store elimination + pure-value copy propagation MUST
                // run BEFORE Normalize: propagated constants feed the algebraic
                // fold, which turns branch-chain constants into constant
                // conditions the emission-time reachability removes.
                stmts = DcePass.Run(stmts).Stmts;
                // OPT-2 (escape-analysis ordering contract, opt2-b23-report §6
                // P2: the original DcePass.Run above is KEPT -- it cleans the
                // folded IR before inlining so stale temps do not pollute the
                // expansion): inline small pure callees first (the inlined
                // ctor/getter bodies turn `new Item(i); ctor(it); it.Get()`
                // into direct field write/read on the local), promote the
                // now-non-escaping allocation to scalar temps, then DCE the
                // introduced scalar stores/copies. Runs BEFORE Normalize so the
                // folded scalar arithmetic feeds the algebraic fold.
                stmts = InlinePass.Inline(stmts, fn => inlineCandidates.TryGetValue(fn, out var c) ? c : null).Stmts;
                stmts = AllocPromoPass.Run(stmts).Stmts;
                stmts = DcePass.Run(stmts).Stmts;
                // mathdeep: a DOUBLE's `v == v`/`v != v` is NaN-aware (.NET
                // IsNaN = `v != v`, true for NaN), but Normalize folds the
                // int32 self-identity `Ceq(x,x) -> 1` -- WRONG for doubles
                // (NaN != NaN is true). Normalize.cs is read-only, so shield
                // double self-comparisons HERE (before Normalize) by rewriting
                // `Ceq(x,x)` (x a double slot) into a synthetic call the
                // emitter renders as the raw double comparison.
                stmts = ProtectDoubleSelfCompare(stmts, m, bodyInfo);
                stmts = Normalize.FoldStmts(stmts, bodyInfo.ExceptionRegions);
                // T3 string pool (design §2.2 order constraint): fold constant
                // concats THEN collect into Generate's authoritative pool --
                // the folded product ("ab") dedups against an existing "ab"
                // literal, and the emitter below resolves every ldstr from
                // THIS pool (index == table row, D1 single writer).
                var (pooled, foldedN) = StringPoolPass.FoldConcatConstants(stmts);
                stmts = pooled;
                foldedCount += foldedN;
                StringPoolPass.Collect(_pool, stmts);
                // Temp-slot reuse (stackframe-audit P1-1): merge temps whose
                // live spans do not overlap onto one slot name (pure rename,
                // post-Fold pre-EmitBody -- the emitter declares one C++
                // variable and one GC root per slot). See TempSlotPass.cs.
                // probe3 (closurecase): the pass's metadata-free kind analysis
                // cannot see reference-typed static fields -- supply the
                // metadata-backed predicate so a `<>c.<>9__4_0` delegate-cache
                // read is a "ref" def and never shares an int32 slot.
                stmts = TempSlotPass.Rename(stmts,
                    fieldName => StaticFieldExprIsReference(md, types, fieldName)).Stmts;
                // ipaudit4: liveness-based root clearing (linear methods only
                // -- exception regions keep the conservative all-or-nothing
                // rooting; the pass itself is a no-op elsewhere).
                if (bodyInfo.ExceptionRegions.Count == 0)
                {
                    stmts = RootClearPass.Run(stmts,
                        fieldName => StaticFieldExprIsReference(md, types, fieldName)).Stmts;
                }
                methodCount++;
                typeEmittedMethod = true;
                // caudit2-fix: publish the per-arg address map to the emitter
                // (FieldAccess value-type receiver judge). Rebuilt per method.
                _curArgAddress = ArgAddressMap(m, bodyInfo);
                // A5 (myIList): the deferred `call_<token>` ctor calls inside
                // generic DEFINITION bodies resolve at EMIT time through the
                // open parent TypeSpec -- without the body owner's hint they
                // decode to the "int" default (i4 arms) while the fold-time
                // calls took the closed arms. Mirror the fold scope above so
                // emission sees the same closed context (save/restore: the
                // hint must never leak across bodies).
                var prevEmitHint = MethodRefResolver.InstantiationSuffixHint;
                MethodRefResolver.InstantiationSuffixHint = t.InstantiationSuffix;
                string body;
                List<string> refLocals;
                RootSlotPass.Analysis rootAnalysis;
                List<string> structFieldRoots;
                try
                {
                    (body, refLocals, rootAnalysis, structFieldRoots) = EmitBody(md, types, bodyInfo, stmts, symMap, m.ParamTypes);
                }
                finally
                {
                    MethodRefResolver.InstantiationSuffixHint = prevEmitHint;
                }
                // Audit C1: reference-typed parameters (including 'this') are
                // GC frame roots while the method is on the call stack -- an
                // object reachable ONLY through a parameter (no local/field
                // references it) would be collected and the parameter would
                // dangle. Parameters are C++ function arguments; their stack
                // slots are stable for the method's duration, so &argN is a
                // valid root address.
                var paramRoots = new List<string>();
                if (!m.IsStatic && !m.Owner.IsValueType) paramRoots.Add("arg0"); // this
                if (m.ParamTypes.Length > 0)
                {
                    var parts = SplitTopLevel(m.ParamTypes).ToArray();
                    for (var i = 0; i < parts.Length; i++)
                        if (parts[i].StartsWith("r:", StringComparison.Ordinal)
                            || (m.ParamIsVar is { } piv && i < piv.Length && piv[i]
                                && m.Owner.InstantiationSuffix != null))
                            paramRoots.Add(m.IsStatic ? $"arg{i}" : $"arg{i + 1}");
                }
                refLocals = refLocals.Concat(paramRoots).ToList();
        var cppMethodName = MethodCppName(t, m);
        // F2: managedRetStruct/returnType computed before Fold (StructReturnCpp
        // seeding); returnType here reuses that computation for the declaration.
        var paramNames = Enumerable.Range(0, bodyInfo.ArgCount).Select(i => $"arg{i}").ToArray();
        var paramTypes = ParamTypesCpp(m, bodyInfo);
        // Silence unused this/parameter warnings (-Wall -Wextra quality gate): if an instance method
        // body never references arg0 (empty ctor), emit `(void)arg0;` to mark it used -- a semantic
        // no-op that silences the compiler. Extended to any unreferenced parameter (e.g. an empty setter's value).
        for (var ai = 0; ai < bodyInfo.ArgCount; ai++)
        {
            if (!body.Contains($"arg{ai}"))
            {
                body = $"\n    static_cast<void>(arg{ai}); // parameter arg{ai} is unused by this method body (silences -Wunused-parameter)\n" + body;
            }
        }
        // Task 10: managed frame roots -- the method's reference locals are GC
        // roots while the method is on the call stack. The prologue registers
        // them (one slot per ref local); every 'return' unregisters them.
        // M2: single-threaded, no exception unwinding (M3's exception system handles unwinding-path
        // removes); multiple rets converging at a goto each remove (idempotent; a repeated remove is a no-op).
        // GC-Root-1 (user top-priority perf): only reference slots whose value
        // is LIVE across a reachable allocation point are registered -- a slot
        // never live across an allocation is a pure local temporary whose
        // address must not be taken (address escape kills register
        // allocation). This subsumes the P0-2 leaf elision: a body that can
        // never allocate has no allocation points, so its root set is empty.
        // Methods with exception regions keep the whole-method conservative
        // model -- a catch/finally handler can be entered from ANY statement
        // of its try range, so the flat IR cannot prove handler liveness.
        var frameRoots = bodyInfo.ExceptionRegions.Count > 0
            ? refLocals.Concat(structFieldRoots).ToList()
            : refLocals.Where(r => rootAnalysis.All.Contains(r)).Concat(structFieldRoots).ToList();        if (frameRoots.Count > 0)
            body = WrapFrameRoots(body, frameRoots, cppMethodName);
                EmitFunctionHeader(w, t, m, bodyInfo, stmts, assemblyPath);
                w.Line(MethodEmitter.EmitMethod(cppMethodName, paramNames, returnType, body, paramTypes));
                w.Line();
                generatedHeader.Line($"{returnType} {cppMethodName}({ParamListTyped(paramNames, paramTypes)});");
                // M7 genlist: a generic method whose body is a PURE PASS-THROUGH
                // (`T Id<T>(T x) => x` -- the folded IR moves args/locals only,
                // no arithmetic/calls/fields on the generic value) gets a SECOND
                // string-typed variant `{name}_string` -- the resolver routes a
                // 0x0E (string) MethodSpec there (MethodRefResolver
                // MethodSpec-on-MethodDefinition case). The variant's generic
                // (MVAR) params/return become Il2CppObject*; the SAME folded
                // body compiles for both (a pass-through is type-agnostic).
                // Gate: only type-agnostic bodies are expanded -- a numeric
                // generic (`Add<T>`) would emit an uncompilable Il2CppObject*
                // body and break unrelated cases.
                if (m.IsGenericMethod && IsPurePassThrough(stmts))
                {
                    var shape = GenericMvarShape(md, bodyInfo.MethodToken, m.ParamCount);
                    // ParamMvar covers EXPLICIT params only; paramTypes leads
                    // with hidden slots (instance `this`). Index with the
                    // leading offset and bounds-guard (a static/instance skew
                    // overran the tail -- IndexOutOfRange on smoke input).
                    var lead = paramTypes.Length - shape.ParamMvar.Length;
                    var strParamTypes = paramTypes.Select((pt, i) =>
                    {
                        var j = i - lead;
                        return j >= 0 && j < shape.ParamMvar.Length && shape.ParamMvar[j] ? "Il2CppObject*" : pt;
                    }).ToArray();
                    var strReturnType = shape.ReturnMvar ? "Il2CppObject*" : returnType;
                    w.Line(MethodEmitter.EmitMethod(cppMethodName + "_string", paramNames, strReturnType, body, strParamTypes));
                    w.Line();
                    generatedHeader.Line($"{strReturnType} {cppMethodName}_string({ParamListTyped(paramNames, strParamTypes)});");
                }
            }

            // A type owns a TU when it emitted a method OR a TypeInfo table
            // (table-only TUs carry methodless <PrivateImplementationDetails>
            // blob types so entry.cpp registration always has its definition).
            if (typeEmittedMethod || typeEmittedTypeInfo)
            {
                File.WriteAllText(Path.Combine(outDir, "src", cppName + ".cpp"), w.ToString());
                File.WriteAllText(Path.Combine(outDir, "include", "internal", cppName + ".h"),
                    $"#pragma once\n#include \"generated.h\"\n");
                allSrc.Add(cppName);
            }
        }

        generatedHeader.Line();
        generatedHeader.Line("// static field storage declarations (definitions live in each type's .cpp)");
        foreach (var t in types)
            foreach (var f in t.StaticFields.Where(f => f.IsStatic && !f.IsLiteral))
                generatedHeader.Line($"extern {TypeEmitter.StaticFieldCppType(f, md)} {TypeEmitter.FieldCppName(t, f.Name)};");
        generatedHeader.Line();
        // Runtime type descriptor externs: entry.cpp's registration table references each type's static tables in its .cpp.
        foreach (var t in types)
            if (t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor"))
            {
                // CG6 (delegate2/delegatecb link FAIL): a custom delegate
                // TypeDef (IntFn/Cb) has NO descriptor -- the runtime stamps
                // every delegate with its single System.Delegate class. The
                // extern (and the entry.cpp class_register it serves) must be
                // skipped or the linker reports "undefined symbol: {T}_class".
                if (IsDelegateTypeDef(t)) continue;
                generatedHeader.Line($"extern Il2CppClass {TypeEmitter.TypeCppName(t)}_class;");
            }
        // M5.5 reflection (R-REFL-01): TypeInfo table externs -- entry.cpp
        // calls il2cpp_typeinfo_register(&{T}_typeinfo) from a DIFFERENT TU,
        // so the non-const namespace-global table needs the cross-TU extern
        // declaration (mirrors the Il2CppClass extern above; NON-const to
        // match the emitting side -- TypeEmitter.EmitTypeInfo linkage bug was
        // static const -> link failure). Skipped for types outside the mode B
        // scope or with a still-invalid C++ identifier (no table emitted).
        foreach (var t in types)
            if (ShouldEmitTypeInfo(t, reflectedNames, noTypeInfoOwners))
                generatedHeader.Line($"extern il2cpp::Il2CppTypeInfo {TypeEmitter.TypeCppName(t)}_typeinfo;");
        generatedHeader.Line();
        // M6 recurse work order: a Main(string[] args) entry receives the
        // host argv -- the entry declaration carries argc/argv so the
        // managed string[] can be constructed and passed (arity match; a
        // zero-arg call against the Main(Il2CppObject*) declaration is a
        // C++ compile error).
        var headerMain = types.FirstOrDefault(t => t.Methods.Any(m => m.Name == "Main"))?.Methods.FirstOrDefault(m => m.Name == "Main");
        var headerMainTakesArgs = headerMain is { ParamCount: 1 } && headerMain.ParamTypes == "r:System.String[]";
        generatedHeader.Line(headerMainTakesArgs
            ? "IL2CPP_EXPORT int32_t il2cpp_entry_main(int32_t argc, char** argv);"
            : "IL2CPP_EXPORT int32_t il2cpp_entry_main(void);");
        // T3 string pool (design §2.3): the static table data file -- emitted
        // by Program.cs only when the pool is non-empty; include conditionally
        // (an empty pool writes NO file -- unconditional include would break
        // every string-free program's generated.h).
        if (_pool.LiteralCount > 0)
            generatedHeader.Line("#include \"string-pool.h\"");
        // RES-1 (iron rule 18): the resource pool data file -- emitted by
        // Program.cs only when resources exist; include conditionally (an
        // assembly with no resources writes NO file).
        if (ResourceCount(assemblyPath) > 0)
            generatedHeader.Line("#include \"resource-pool.h\"");
        // D5 (valuetuple-local design): flush every tuple struct used by any
        // method body as a FILE-SCOPE definition -- generated.h is included
        // by every .cpp, so declaration arms (D3) and P5 locals in ANY
        // function see one shared type.
        foreach (var def in TupleDefsNeeded)
        {
            var bar = def.IndexOf('|');
            generatedHeader.Line($"struct {def[..bar]} {{ {def[(bar + 1)..]}; }};");
        }
        // P-D2 #1: flush every delegate multicast helper as a FILE-SCOPE inline
        // definition -- generated.h is included by every .cpp, so any Invoke
        // call site in ANY method body sees one shared helper (deduped by
        // signature hash; inline = no ODR across translation units).
        foreach (var def in DelegateHelperDefs)
            generatedHeader.Line(def);
        // P-D2 #2: flush every GfpS instance-binding module (see GfpModuleDefs).
        // The modules use std::fprintf/std::abort (overflow / invalid-slot loud
        // abort) -- <cstdio>/<cstdlib> are not guaranteed transitively, emit them
        // explicitly before the definitions.
        if (GfpModuleDefs.Count > 0)
        {
            generatedHeader.Line("#include <cstdio>");
            generatedHeader.Line("#include <cstdlib>");
        }
        foreach (var def in GfpModuleDefs)
            generatedHeader.Line(def);
        // Guid.Empty static-field reads: System.Guid has no TypeDef storage,
        // so ldsfld emits the shared file-scope all-zero 16-byte slot (the
        // Guid value representation; "g"-cast receivers take its address).
        // NOT const: the Guid slot ABI passes mutable uint8_t* (a const
        // declaration would make reinterpret_cast<uint8_t*> ill-formed).
        if (_guidEmptyNeeded)
        {
            generatedHeader.Line("static uint8_t il2cpp_guid_empty_bytes[16] = { 0 };");
        }
        File.WriteAllText(Path.Combine(outDir, "include", "internal", "generated.h"), generatedHeader.ToString());

        EmitMain(outDir, types, pe, md, _pool, noTypeInfoOwners, reflectedNames, ResourceCount(assemblyPath));
        EmitCmake(outDir, allSrc, assemblyPath, types);
        return new GenerateResult(_pool, foldedCount, methodCount);
    }

    // IR->C++ statement emission. body is emitted one indent level deep (end-of-line comments align
    // to column 100) and written verbatim into the function body by MethodEmitter. Returns
    // (body, refLocals, rootAnalysis): refLocals = the reference-typed local names this method declares as
    // Il2CppObject* (declaration-only; the frame-root set comes from rootAnalysis); rootAnalysis = GC-Root-1
    // slot-liveness analysis over this body (which reference slots are live across an allocation point).
    //--------------------------------------------------------------------------
    // perf2 str_concat: fold the net48 `"v" + i` IL shape -- an Int32::ToString
    // call whose result is immediately consumed by String.Concat(string,string):
    //
    //     t0 = icall_int32_to_string(X);       // the number's digits string
    //     Y  = icall_string_concat_2(A, t0);   // A + number
    //
    // into
    //
    //     t0 = icall_string_concat_int(A, X);  // ONE managed allocation
    //     Y  = t0;                             // pointer copy
    //
    // (the runtime icall_string_concat_int formats the int32 digits inline into
    // the same scratch as A's payload -- the intermediate number string and its
    // allocation are eliminated; see ConcatWithInt32 in stringext2.cpp). Runs
    // FIRST in EmitBody so every downstream analysis (reachability / declared /
    // locKind / RootSlotPass) sees the folded IR. Folds ONLY the exact shape:
    // t0 single-use, the pair adjacent, concat_2 arity, t0 the concat's LAST
    // arg (icall_string_concat_int appends the number AFTER the string).
    //--------------------------------------------------------------------------
    private static void FoldConcatInt32(List<Stmt> stmts)
    {
        for (var i = 0; i + 1 < stmts.Count; i++)
        {
            if (stmts[i] is not AssignStmt toStr || toStr.Target.Contains('.')) continue;
            if (toStr.Value is not CallExpr toStrCall) continue;
            if (toStrCall.Resolved?.CppName != "il2cpp::runtime::icall_int32_to_string") continue;
            if (toStrCall.Args.Count != 1) continue;
            var temp = toStr.Target;
            if (stmts[i + 1] is not AssignStmt concat || concat.Target == temp) continue;
            if (concat.Value is not CallExpr concatCall) continue;
            if (concatCall.Resolved?.CppName != "il2cpp::runtime::icall_string_concat_2") continue;
            if (concatCall.Args.Count != 2) continue;
            if (concatCall.Args[1] is not LocExpr tempUse || tempUse.Name != temp) continue;
            // temp must be single-assigned (only the ToString stmt) and
            // single-read (only the concat's last arg).
            var assignUses = 0;
            var readUses = 0;
            for (var k = 0; k < stmts.Count && assignUses <= 1 && readUses <= 1; k++)
            {
                if (stmts[k] is AssignStmt a && a.Target == temp) assignUses++;
                readUses += CountLocalRefs(stmts[k], temp);
            }
            if (assignUses != 1 || readUses != 1) continue;
            var toStrCast = toStrCall.Resolved.ParamTypeNames is { Length: > 0 } tc && tc[0] is { Length: > 0 } tcv
                ? tcv : "deref:i";
            var folded = new CallExpr
            {
                Function = concatCall.Function,
                ReturnsRef = true,
                Resolved = new ResolvedMethod("il2cpp::runtime::icall_string_concat_int", 2, false, true, true,
                    new[] { true, false }, false, ParamTypeNames: new[] { "", toStrCast })
            };
            folded.Args.Add(concatCall.Args[0]);
            folded.Args.Add(toStrCall.Args[0]);
            stmts[i] = new AssignStmt { Target = temp, Value = folded, SourceOffset = toStr.SourceOffset, SourceOpName = toStr.SourceOpName };
            stmts[i + 1] = new AssignStmt { Target = concat.Target, Value = new LocExpr { Name = temp }, SourceOffset = concat.SourceOffset, SourceOpName = concat.SourceOpName };
        }
    }

    // Count the LocExpr/LocAddrExpr read-references to `name` in one statement
    // (AssignStmt targets are NOT reads -- the fold's single-read guard counts
    // the concat's arg reference only).
    private static int CountLocalRefs(Stmt s, string name)
    {
        var count = 0;
        void Walk(Expr e)
        {
            switch (e)
            {
                case LocExpr l when l.Name == name: count++; break;
                case LocAddrExpr la when la.Name == name: count++; break;
                case BinaryExpr b: Walk(b.Left); Walk(b.Right); break;
                case UnaryExpr u: Walk(u.Operand); break;
                case CallExpr c: foreach (var a in c.Args) Walk(a); break;
                case VirtualCallExpr v: foreach (var a in v.Args) Walk(a); break;
                case DelegateInvokeExpr d: foreach (var a in d.Args) Walk(a); break;
                case DelegateNewExpr dn: Walk(dn.Target); break;
                case MarshalUtf8Expr m: Walk(m.Arg); break;
                case FieldExpr f: Walk(f.Object); break;
                case FieldAddrExpr fa: Walk(fa.Object); break;
            }
        }
        switch (s)
        {
            case AssignStmt a: Walk(a.Value); break;
            case CallStmt cs: foreach (var a in cs.Args) Walk(a); break;
            case ExprStmt es: Walk(es.Value); break;
            case RetStmt r when r.Value != null: Walk(r.Value); break;
            case BranchStmt b when b.Condition != null: Walk(b.Condition); break;
            case FieldAssignStmt f: Walk(f.Object); Walk(f.Value); break;
        }
        return count;
    }

    private static (string Body, List<string> RefLocals, RootSlotPass.Analysis RootAnalysis, List<string> StructFieldRoots) EmitBody(MetadataReader md, IReadOnlyList<TypeDef> types, MethodBodyInfo bodyInfo, List<Stmt> stmts, SequencePointMap? symMap, string paramTypes = "")
    {
        _curReturnType = bodyInfo.ReturnType;
        // perf2 str_concat: fold the net48 `"v" + i` IL shape
        //   t0 = icall_int32_to_string(X); Y = icall_string_concat_2(A, t0)
        // (Int32::ToString immediately feeding a 2-arg string Concat) into
        //   t0 = icall_string_concat_int(A, X); Y = t0
        // -- ONE managed allocation instead of TWO (the intermediate number
        // string is eliminated; ConcatWithInt32 in stringext2.cpp formats the
        // digits inline into the same scratch as the 'A' payload). Conditions:
        // t0 is a single-use temp, the concat's LAST arg is t0, and the pair is
        // adjacent. The rewrite runs BEFORE every downstream analysis (reach-
        // ability / declared / locKind / RootSlotPass) so they all see the
        // folded IR (same statement count and SourceOffset -- EH membership
        // and statement indices are preserved).
        FoldConcatInt32(stmts);

        var w = new CppWriter();
        w.Open(); // one indent level inside the function body

        // SYMLN-1: the currently-active C# #line state inside this method body.
        // The C++ #line directive is PERSISTENT -- it applies to every
        // following line until changed, so a directive is re-emitted only when
        // the statement's target (line, document) differs from the active one.
        var symLine = -1;
        string? symDoc = null;

        // Reachability analysis: after constant-condition branches (e.g. `(5LL > 3LL) == 0LL`) and
        // dead labels are eliminated, dead-block statements are not emitted -- otherwise MSVC /W4
        // C4702 (unreachable code) and clang -Wunused-label. Rules: unreachable after goto/ret/
        // constant-true branch; labels still referenced by branches reset to reachable; labels with no branch references (dead labels) are neither emitted nor reset (their block is only reached by fall-through, and a dead fall-through means a dead block).
        var branchTargets = stmts.OfType<BranchStmt>()
            .Where(b => b.Condition == null || !TryFoldInt32(b.Condition, out var cv) || cv != 0)
            .Select(b => b.TargetLabel)
            .ToHashSet();
        var endReachableState = true;
        bool[] ComputeReachability(HashSet<string> targets)
        {
            var arr = new bool[stmts.Count];
            var live = true;
            // C++-level fall-through, tracked SEPARATELY from `live`: the
            // throw/rethrow terminator rule below exists to suppress dead IL
            // (C4702) and must NOT feed the C4716 decision. il2cpp_throw_new is
            // deliberately NOT declared [[noreturn]], so from the C++ compiler's
            // point of view a `throw` statement DOES fall through to the closing
            // brace -- a throw-only non-void method therefore still needs the
            // trailing zero-return (C4716TrailingReturnTests). `ret` (emits
            // `return ...;`) and unconditional/constant-true branches (emit
            // `goto ...;`) really do terminate at the C++ level, so they clear
            // this flag too. This flag deliberately reproduces the pre-existing
            // semantics: only labels reset it (no region-context reset) -- a
            // spurious trailing return on a `try { return x; } finally {...}`
            // body would itself be unreachable code (C4702).
            var liveCpp = true;
            // EH region context of one statement, as a stable integer key:
            // 1_000_000+handlerRegion if the offset is inside a handler range,
            // else 2_000_000+tryRegion if inside a try range, else 0. A change
            // of context means control moved to a different EH region, which is
            // entered by the exception dispatcher / by falling out of the
            // previous region -- NEVER by falling through the region's own
            // terminator. `live` therefore restarts there.
            //
            // Without this, a `throw` at the end of a try body poisons every
            // following statement up to the next label: exdeep's `try { throw }
            // catch { }` (empty handler -> no statement to reset on) dropped the
            // whole rest of Main (f19/f20 probes vanished from the output while
            // the build still succeeded). The lock-finally cleanup copies that
            // C4702 suppression targets live INSIDE the same try region as their
            // throw, so they stay dead.
            int RegionContext(int off)
            {
                var h = -1;
                var t = -1;
                for (var r = 0; r < bodyInfo.ExceptionRegions.Count; r++)
                {
                    var rg = bodyInfo.ExceptionRegions[r];
                    if (off >= rg.HandlerOffset && off < rg.HandlerOffset + rg.HandlerLength) h = r;
                    else if (off >= rg.TryOffset && off < rg.TryOffset + rg.TryLength) t = r;
                }
                if (h >= 0) return 1_000_000 + h;
                if (t >= 0) return 2_000_000 + t;
                return 0;
            }
            var prevCtx = -3; // no previous statement
            for (var i = 0; i < stmts.Count; i++)
            {
                var ctx = RegionContext(stmts[i].SourceOffset);
                if (ctx != prevCtx) live = true; // new EH region: reachable again
                prevCtx = ctx;
                arr[i] = live;
                switch (stmts[i])
                {
                    case LabelStmt l when !targets.Contains(l.Name):
                        break; // dead label: not emitted, reachability not reset
                    case LabelStmt:
                        live = true;
                        liveCpp = true;
                        break;
                    case RetStmt:
                        live = false;
                        liveCpp = false;
                        break;
                    // IL throw / rethrow is a TERMINATOR (ECMA-355 III.3.3x:
                    // no fall-through after it). Statements the fold kept
                    // after a throw (the lock-finally cleanup copies) would
                    // otherwise be emitted as C++ code following the
                    // [[noreturn]] il2cpp_throw_new call -> MSVC C4702
                    // (lockcase/exceptionchain2). A label still resets
                    // reachability, so any branch target stays emitted.
                    // NOTE: liveCpp is intentionally NOT cleared here (see above).
                    case CallStmt th when th.Function.StartsWith("throw_", StringComparison.Ordinal)
                        || th.Function == "rethrow":
                        live = false;
                        break;
                    case BranchStmt b:
                        if (b.Condition == null) { live = false; liveCpp = false; } // unconditional goto: fall-through is dead
                        else if (TryFoldInt32(b.Condition, out var cv) && cv != 0) { live = false; liveCpp = false; } // constant true: always taken
                        break;                                          // constant false/non-constant: fall-through continues
                }
            }
            endReachableState = liveCpp;
            return arr;
        }
        var reachable = ComputeReachability(branchTargets);
        // Second pass (msvc C4102 / gcc -Wunused-label, boolops L_240): a label
        // can be targeted ONLY by a branch that sits in an UNREACHABLE block --
        // that branch is never emitted, so the label it targets is never
        // emitted either. Recompute the target set from reachable branches
        // only and re-run reachability (a dropped label stops resetting
        // reachability, so its dead-fall-through block is dropped too --
        // semantics preserved: no emitted goto could reach it).
        {
            var emittedTargets = new HashSet<string>();
            for (var i = 0; i < stmts.Count; i++)
            {
                if (!reachable[i] || stmts[i] is not BranchStmt b2) continue;
                if (b2.Condition != null && TryFoldInt32(b2.Condition, out var cv2) && cv2 == 0) continue; // constant-false: no goto emitted
                emittedTargets.Add(b2.TargetLabel);
            }
            if (emittedTargets.Count != branchTargets.Count)
            {
                branchTargets = emittedTargets;
                reachable = ComputeReachability(branchTargets);
            }
        }
        // C4716 (MULTI-D batch find, delegate2 msvc): after the last statement,
        // endReachableState holds the C++-LEVEL FALL-THROUGH state of the
        // function's closing brace (liveCpp -- `throw` does NOT terminate it,
        // because il2cpp_throw_new is not [[noreturn]]). A non-void method whose
        // body never falls into an IL 'ret' (it ends on a throw/rethrow path)
        // leaves the C++ end-of-function reachable WITHOUT a return statement ->
        // MSVC C4716. The trailing-return injection below keys off this flag.
        var endReachable = endReachableState;

        // Locals/temps (not parameters, not static fields) are declared once at the function top (assignments inside dead blocks are not counted).
        // A local is declared when it appears ANYWHERE in a reachable statement's
        // expression tree: as an AssignStmt target, as a plain read (LocExpr --
        // `t = (loc8 == 123)` reads the TryParse result slot), or as an ADDRESS
        // (LocAddrExpr -- an out/byref slot passed to Int32.TryParse /
        // Math.DivRem). Address-taking and reads can be nested arbitrarily deep
        // (a LocAddrExpr inside a CallExpr argument of a CallStmt, or inside a
        // BranchStmt.Condition). The pre-fix scan only checked TOP-LEVEL CallStmt
        // args and the direct AssignStmt.Value shape, missing TryParse's out-slot
        // inside WriteLine (numchain loc7-22 / loc25-26) and Math.DivRem's
        // result slots (locN) -- the emitted C++ referenced undeclared locals
        // and the cmake build failed.
        var paramSet = Enumerable.Range(0, bodyInfo.ArgCount).Select(i => $"arg{i}").ToHashSet();
        var declared = new List<string>();
        void Declare(string name)
        {
            if (name.Contains('.') || paramSet.Contains(name) || declared.Contains(name)) return;
            declared.Add(name);
        }
        void CollectLocals(Expr e)
        {
            switch (e)
            {
                case LocExpr l: Declare(l.Name); break;
                case LocAddrExpr l: Declare(l.Name); break;
                case BinaryExpr b: CollectLocals(b.Left); CollectLocals(b.Right); break;
                case UnaryExpr u: CollectLocals(u.Operand); break;
                case CallExpr c: foreach (var a in c.Args) CollectLocals(a); break;
                case VirtualCallExpr v: foreach (var a in v.Args) CollectLocals(a); break;
                case DelegateInvokeExpr d: foreach (var a in d.Args) CollectLocals(a); break;
                case DelegateNewExpr dn: CollectLocals(dn.Target); break;
                case MarshalUtf8Expr m: CollectLocals(m.Arg); break;
                case FieldExpr f: CollectLocals(f.Object); break;
                case FieldAddrExpr fa: CollectLocals(fa.Object); break;
                // Leaves (constants, null, static fields, newobj, value-type
                // new, funcptr, exception entry) carry no local references.
            }
        }
        for (var i = 0; i < stmts.Count; i++)
        {
            if (!reachable[i]) continue;
            switch (stmts[i])
            {
                case AssignStmt a:
                    Declare(a.Target);
                    CollectLocals(a.Value);
                    break;
                case CallStmt vc:
                    foreach (var a in vc.Args) CollectLocals(a);
                    break;
                case ExprStmt es:
                    CollectLocals(es.Value);
                    break;
                case RetStmt r when r.Value != null:
                    CollectLocals(r.Value);
                    break;
                case BranchStmt b when b.Condition != null:
                    CollectLocals(b.Condition);
                    break;
                case FieldAssignStmt f:
                    CollectLocals(f.Object);
                    CollectLocals(f.Value);
                    break;
            }
        }
        // Type inference (M2: int32_t / const char* (string literals) / Il2CppObject* (managed
        // references)). Propagative: the Value assigned to a loc may itself be a LocExpr (
        // loc0 = t0`) -- trace to the base expression (StringConstExpr = string; NewObjExpr /
        // ReturnsRef CallExpr / reference FieldExpr = reference; rest = value). Cyclic references (a=b; b=a) cannot arise from stack folding (each value is evaluated before push).
        var locKind = new Dictionary<string, string>(); // name -> "ref" | "val" | <type>
        // M3 Task 11 FIRST: value-type locals initialized by ldloca + ctor
        // call get the ctor's declaring type as their kind -- this must
        // precede the ExprKind propagation so copies (loc1 = loc0) resolve.
        // wf2 (winforms1): a struct with NO explicit ctor (`WNDCLASSEX wc =
        // new WNDCLASSEX();`) compiles to ldloca + initobj (Roslyn emits no
        // .ctor call for the implicit default ctor), so the initobj_<token>
        // call must ALSO type the local -- otherwise `wc.cbSize = 80;` emits
        // against an int32_t slot ("member reference base type 'int32_t'").
        for (var i = 0; i < stmts.Count; i++)
        {
            if (!reachable[i]) continue;
            if (stmts[i] is not CallStmt vc) continue;
            if (vc.Args.Count > 0 && vc.Args[0] is LocAddrExpr laCtor)
            {
                if (vc.Function.StartsWith("call_", StringComparison.Ordinal))
                {
                    var tok = int.Parse(vc.Function[5..]);
                    // wf2 (winforms1): a value-type local is typed ONLY by a
                    // REAL value-type ctor call on its address. The old scan
                    // matched ANY call with a LocAddrExpr first arg -- the
                    // P/Invoke `GetMessageW(out MSG msg)` (args[0] = &msg)
                    // was mis-read as a ctor of MSG and typed `msg` as the
                    // DECLARING CLASS (Cases_Winforms1) instead of the MSG
                    // struct ("member reference base type ... is not a
                    // structure or union"). Only a `.ctor` whose declaring
                    // type is a value type qualifies.
                    if (IsValueTypeCtorToken(md, tok, types))
                        locKind[laCtor.Name] = ValueTypeCtorOwnerCpp(md, tok, types);
                }
                else if (vc.Function.StartsWith("initobj_", StringComparison.Ordinal)
                         && int.TryParse(vc.Function["initobj_".Length..], out var ioTok))
                {
                    // R2b P2 (default(T) in a generic body): an initobj on a
                    // VAR/MVAR TypeSpec must NOT materialize the canonical
                    // "System.Int32" name -- TypeCppName sanitizes it to the
                    // UNDEFINED C++ type `System_Int32`. The VAR channel
                    // decides the slot exactly like every other VAR face:
                    // closed reference clone -> ref; definition/int -> value.
                    if (IsVarOrMvarTypeSpec(md, ioTok))
                        locKind[laCtor.Name] = _curInstantiationSuffix is not (null or "int") ? "ref" : "val";
                    else
                        locKind[laCtor.Name] = TypeCppName(TypeTokenFullName(md, ioTok));
                }
            }
        }
        // net9/10 E2E icallcase (decimal Parse defect): an AssignStmt whose
        // value is a decimal icall (ResultKind "dec") writes its 16-byte
        // Il2CppDecimal through the TARGET slot -- `icall_decimal_parse((&t10), s)`.
        // The fold temp t10 defaulted to int32_t -> 4-byte slot for a
        // 16-byte write. Type every such target as the decimal struct.
        // STANDALONE pass: the ctor/initobj scan above early-continues on
        // non-CallStmt, so AssignStmt targets never reach it.
        for (var i = 0; i < stmts.Count; i++)
        {
            if (!reachable[i]) continue;
            if (stmts[i] is AssignStmt aDec
                && aDec.Value is CallExpr cDec && cDec.Resolved?.ResultKind is "dec" or "dec2")
            {
                locKind[aDec.Target] = "il2cpp::Il2CppDecimal";
            }
        }
        // fnptr domain b23: a `delegate*` parameter is a raw function pointer
        // (void* slot). Seed the local-kind analysis so a copy into a local
        // (`delegate* p = arg;`) keeps the pointer width -- the calli null
        // guard compares it to nullptr, and the cast needs the full pointer.
        // (paramTypes is the MethodSignatureCoder comma-joined encoding; for
        // instance methods arg0 = this, so param i maps to arg{i+1}.)
        // M7 CG-FIX (boxing/interface/props/virtual/delecb2 cmake FAIL): the
        // inlined base-ctor chain renames the callee's `this` (arg0) to a
        // fresh inline temp (`inl0 = arg0;`), and the temp's type propagates
        // from ExprKind(LocExpr arg0). Parameters were never seeded, so the
        // reference `this` degraded to "val" -> int32_t -> the generated C++
        // assigned Il2CppObject* to an int32_t local. Seed every reference
        // parameter (this for instance methods + every "r:"-encoded explicit
        // param) as "ref"; fnptr params stay "p".
        var parts = paramTypes.Length == 0 ? Array.Empty<string>() : SplitTopLevel(paramTypes).ToArray();
        var isStaticMethod = bodyInfo.ArgCount == parts.Length;
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("F:", StringComparison.Ordinal))
                locKind[isStaticMethod ? $"arg{i}" : $"arg{i + 1}"] = "p";
            else if (parts[i].StartsWith("r:", StringComparison.Ordinal))
                locKind[isStaticMethod ? $"arg{i}" : $"arg{i + 1}"] = "ref";
        }
        // Instance method `this` (arg0) is always a managed reference -- the
        // inline pass rewrites it into fresh temps (`inl0 = arg0`) and copies
        // must keep the reference width. Value-type owners pass `this` by
        // address ({T}*) -- NOT a GC ref -- so only seed when the owner is a
        // reference type (the "val" default would also be wrong for a
        // value-type `this`; value-type methods declare their own arg0 in
        // ParamTypesCpp, so the seed only matters for reference owners).
        if (!isStaticMethod && !MethodOwnerIsValueType(md, bodyInfo.MethodToken, types))
            locKind["arg0"] = "ref";
        // M7 CG-FIX (lockcase/props cmake FAIL): static-field READ kinds. A
        // reference-typed static field (`static readonly object gate`) read into
        // a local (`loc4 = LockCase_sf_gate`) must declare the local Il2CppObject*
        // -- the plain ExprKind(StaticFieldExpr) arm classifies everything as
        // "val" (int32_t), so the monitor icall receiver degraded to int32.
        // Pre-seed: any assign whose value is a reference static field read
        // propagates "ref" to the target (run BEFORE the generic loop so the
        // generic arm's LocExpr lookups see the correct kind). The generic
        // loop below MUST NOT overwrite these -- ExprKind has no metadata
        // access, so a StaticFieldExpr falls to its default "val" arm.
        for (var i = 0; i < stmts.Count; i++)
        {
            if (!reachable[i] || stmts[i] is not AssignStmt asf) continue;
            if (asf.Value is StaticFieldExpr sf && StaticFieldExprIsReference(md, types, sf.FieldName))
                locKind[asf.Target] = "ref";
        }
        // R7-C4244 (winforms1): a static IntPtr/UIntPtr read (storage
        // intptr_t/uintptr_t) assigned to a temp/local must keep the full
        // pointer width -- the ExprKind default "val" declared int32_t and the
        // assignment warned C4244 (x16; the slot then fed ShowWindow's native
        // arg). Seed "ptr" (declared intptr_t) like the ref seed above.
        for (var i = 0; i < stmts.Count; i++)
        {
            if (!reachable[i] || stmts[i] is not AssignStmt asp) continue;
            if (asp.Value is not StaticFieldExpr sfp) continue;
            var dotP = sfp.FieldName.LastIndexOf('.');
            if (dotP <= 0) continue;
            var ownerP = sfp.FieldName[..dotP];
            var fieldP = sfp.FieldName[(dotP + 1)..];
            var ownerShortP = ownerP[(ownerP.LastIndexOf('+') + 1)..];
            var ownerDefP = types.FirstOrDefault(x => x.FullName == ownerShortP || TypeEmitter.ReflectionFullName(x) == ownerP);
            var fieldDefP = ownerDefP?.StaticFields.FirstOrDefault(ff => ff.Name == fieldP);
            if (fieldDefP != null && fieldDefP.FieldTypeName is "System.IntPtr" or "System.UIntPtr")
                locKind[asp.Target] = "ptr";
        }
        for (var i = 0; i < stmts.Count; i++)
        {
            if (!reachable[i] || stmts[i] is not AssignStmt a) continue;
            if (a.Value is StaticFieldExpr) continue; // already classified above
            var kind = ExprKind(a.Value, locKind);
            if (kind != null) locKind[a.Target] = kind;
        }
        // M5 wiring: IL-declared int64/double locals are authoritative -- a
        // copy (`long b = a;`) must propagate the 64-bit kind even when the
        // fold only saw an int32-typed constant source.
        for (var i = 0; i < bodyInfo.LocalKinds.Length; i++)
            if (bodyInfo.LocalKinds[i] is "l" or "d")
                locKind[bodyInfo.LocNames[i]] = bodyInfo.LocalKinds[i];
        var refLocals = new List<string>();
        // batch 156c (asynccase): REFERENCE FIELDS of VALUETYPE locals (the
            // async state machine's builder/awaiter slots) -- collected by the
            // "v:" declaration arm as `local.field` root-slot expressions. Merged
            // into frameRoots UNCONDITIONALLY (whole-method, bypassing the
            // RootSlotPass liveness filter -- interior struct fields are
            // address-taken and the liveness analysis tracks local NAMES only).
            var structFieldRoots = new List<string>();
            foreach (var d in declared)
            {
                // boxdeep/boxdeep2 (-Wunused-but-set-variable): a compiler TEMP
                // (tN/inlN) whose only store is an IL `stloc` of an
                // intentionally-unused C# local (the `try { int x = (int)od; }`
                // InvalidCastException probe -- the unbox's side effect is the
                // point, the value is never read). The transpile is faithful;
                // the attribute only acknowledges the unused binding (genuine
                // emitter bugs on USED temps still warn).
                var mu = (d.Length > 1 && d[0] == 't' && char.IsDigit(d[1]))
                    || (d.StartsWith("inl", StringComparison.Ordinal) && d.Length > 3 && char.IsDigit(d[3]))
                    ? "[[maybe_unused]] " : "";
                // G5 (boxstr): the same acknowledgment for EVERY declaration arm
                // below (int64_t/double/... kind-specific arms previously emitted
                // the bare declaration -> -Wunused-but-set-variable on an
                // intentionally-unread IL temp).
                string MuPrefix(string name) =>
                    (name.Length > 1 && name[0] == 't' && char.IsDigit(name[1]))
                    || (name.StartsWith("inl", StringComparison.Ordinal) && name.Length > 3 && char.IsDigit(name[3]))
                        ? "[[maybe_unused]] " : "";
                var kind = locKind.TryGetValue(d, out var k) ? k : "val";
            // M3 Task 11: a local initialized by a value-type ctor
            // (ldloca + call .ctor) is declared as the ctor's declaring type.
            string? vtType = null;
            for (var i = 0; i < stmts.Count; i++)
            {
                if (!reachable[i]) continue;
                if (stmts[i] is CallStmt vc && vc.Function.StartsWith("call_", StringComparison.Ordinal)
                    && vc.Args.Count > 0 && vc.Args[0] is LocAddrExpr la && la.Name == d)
                {
                    var tok = int.Parse(vc.Function[5..]);
                    // wf2 (winforms1): mirror of the locKind scan -- only a REAL
                    // value-type ctor call types the local (the P/Invoke
                    // GetMessageW(out MSG msg) would otherwise type `msg` as the
                    // DECLARING CLASS, Cases_Winforms1).
                    if (IsValueTypeCtorToken(md, tok, types))
                    {
                        vtType = ValueTypeCtorOwnerCpp(md, tok, types);
                        break;
                    }
                }
            }
            if (vtType != null)
            {
                w.Line($"{MuPrefix(d)}{vtType} {d};");
                continue;
            }
            // M5 wiring: the IL local signature is authoritative for
            // int64/double slots -- a local the IL declares as int64 must be
            // declared int64_t even when the fold only saw an int32-typed
            // constant (conv.i8 identity: `long v = x;`).
            var ilKind = d.StartsWith("loc", StringComparison.Ordinal)
                && int.TryParse(d.AsSpan(3), out var di) && di < bodyInfo.LocalKinds.Length
                ? bodyInfo.LocalKinds[di]
                : null;
            if (ilKind == "l" || ilKind == "d")
            {
                w.Line($"{(ilKind == "l" ? "int64_t" : "double")} {d};");
                continue;
            }
            // batch 156c (asynccase): an IL-declared REFERENCE local ("r" --
            // TaskAwaiter`1<T> in the async state machine: the awaiter IS the
            // task object) is authoritative: declare Il2CppObject* + frame
            // root. Same IL-authoritative doctrine as the int64/double
            // branch -- the awaiter value (a task ref) must not degrade to
            // an int32 slot.
            // batch 175 (linqgroup): the IL "r" declaration is authoritative
            // UNLESS the IR locKind analysis PROVES the slot only ever stores
            // plain values. net48 Roslyn REUSES an object-typed temp slot
            // (0x12 class) to hold int32/bool values in the LINQ aggregate
            // chain (`loc15 = (il2cpp_linq_sum_i4(...) == 45LL)` -- the IL
            // local signature says class, but every store is bool/int32). The
            // IL signature here is a slot-reuse artifact: when the local has
            // stores and NONE of them is a reference-typed expression ("ref"),
            // the declaration must follow the IR (int32_t), not the IL
            // signature -- otherwise the generated C++ declares
            // `Il2CppObject* loc15` while assigning bool/int32 to it (20
            // "incompatible integer to pointer conversion" cmake errors).
            // The asynccase awaiter still rides the branch: its stores
            // include icall_task_get_awaiter (ReturnsRef -> "ref"), so a
            // reference store exists even when a later struct-field read
            // (`loc2 = arg0->__u__1`) classifies the LAST assignment "val".
            // A local with NO stores at all keeps the IL-authoritative
            // Il2CppObject* declaration (no IR evidence to override it).
            if (ilKind == "r")
            {
                var hasRefStore = false;
                var hasAnyStore = false;
                for (var si = 0; si < stmts.Count; si++)
                {
                    if (!reachable[si] || stmts[si] is not AssignStmt rs || rs.Target != d) continue;
                    hasAnyStore = true;
                    // FIX-175b (monitordeep regression, introduced by f9a96fa):
                    // the pre-seed pass (reference static fields, ref params)
                    // already recorded locKind[rs.Target] == "ref" -- that is
                    // AUTHORITATIVE for a reference store even when
                    // ExprKind(rs.Value) falls to the default "val" arm
                    // (StaticFieldExpr has no metadata access there and only
                    // System.Decimal is special-cased). Without this, an
                    // object local (IL 0x1C) seeded "ref" by a reference
                    // static-field assignment (loc0 = MonitorDeep_sf_gateA)
                    // was declared int32_t -> pointer/integer conversion
                    // errors at the lock/monitor call sites.
                    if (locKind.TryGetValue(rs.Target, out var targetKind) && targetKind == "ref")
                    {
                        hasRefStore = true;
                        break;
                    }
                    if (ExprKind(rs.Value, locKind) == "ref") { hasRefStore = true; break; }
                }
                if (hasRefStore || !hasAnyStore)
                {
                    refLocals.Add(d);
                    w.Line($"Il2CppObject* {d} = nullptr;");
                }
                else
                {
                    w.Line($"{MuPrefix(d)}int32_t {d};");
                }
                continue;
            }
            // wf2 (winforms1): a VALUETYPE local whose IL signature carries a
            // struct full name ("v:MSG" -- MethodBodyReader.LocalTypeKind)
            // declares as the generated struct type (`MSG msg;`). The
            // P/Invoke GetMessageW(out MSG) writes through &msg -- an
            // int32_t slot would overrun.
            // D3 (valuetuple-local design): a GENERICINST System.ValueTuple`N
            // with flat primitive args arrives as "tuple:<codes>" (D2) and
            // declares as the canonical tuple struct. The struct DEFINITION
            // is emitted file-scope in generated.h (D5) so every function
            // sees one type.
            if (ilKind != null && ilKind.StartsWith("tuple:", StringComparison.Ordinal))
            {
                var codes = ilKind["tuple:".Length..].Split(',');
                w.Line($"{TupleStructCppName(codes)} {d} {{ }};");
                continue;
            }
            // R77 generic multi-instantiation: a generic-inst local encodes
            // the definition name + the instantiation suffix
            // ("g:System.RuntimeType+ListBuilder`1:object"). The local must
            // be declared as the instantiated type (ListBuilder_1_object),
            // not the default (ListBuilder_1_int).
            if (ilKind != null && ilKind.StartsWith("g:", StringComparison.Ordinal))
            {
                var gParts = ilKind[2..].Split(':');
                if (gParts.Length >= 2)
                {
                    var defName = gParts[0];
                    var suffix = gParts[1];
                    var genericTypeDef = types.FirstOrDefault(t => t.FullName == defName);
                    if (genericTypeDef != null)
                    {
                        // R2b P2: a CLASS instantiation is a managed reference
                        // (the newobj result is an object pointer) -- declare
                        // Il2CppObject* + frame root. The concrete
                        // {base}_{suffix} VALUE declaration is only for
                        // VALUE-type instantiations (ListBuilder`1:object,
                        // the shape this encoding was introduced for).
                        if (!genericTypeDef.IsValueType)
                        {
                            refLocals.Add(d);
                            w.Line($"Il2CppObject* {d} = nullptr;");
                            locKind[d] = "ref";
                            continue;
                        }
                        // Build the instantiated C++ name: {base}_{suffix}
                        var baseName = TypeEmitter.Sanitize(defName);
                        w.Line($"{MuPrefix(d)}{baseName}_{suffix} {d};");
                        continue;
                    }
                }
                // Fallback: if we can't resolve, declare as int32_t
                w.Line($"{MuPrefix(d)}int32_t {d};");
                continue;
            }
            // R2b P2: a bare VAR local (0x13/0x1E -> "v" from
            // MethodBodyReader.LocalTypeKind) inside a reference
            // instantiation is the managed reference -- same ref-carrier
            // doctrine as the "r" locals above.
            if (ilKind == "v" && _curInstantiationSuffix != null && _curInstantiationSuffix != "int")
            {
                refLocals.Add(d);
                w.Line($"Il2CppObject* {d} = nullptr;");
                continue;
            }
            if (ilKind != null && ilKind.StartsWith("v:", StringComparison.Ordinal))
            {
                // P25d (dictp25): a System.Collections.Generic.KeyValuePair`2
                // IL-declared local (the `foreach (KeyValuePair<,> kvp in
                // <IDictionary or Dictionary>)` iteration variable) is a REF
                // CARRIER in this transpiler: the runtime gives every pair as
                // the Il2CppDictKvpSI holder OBJECT (get_Key/get_Value are
                // separate icalls -- there is no 16-byte pair struct). The
                // generic TypeCppName arm would emit `System_
                // Collections_Generic_KeyValuePair_2 loc;` (unknown type name
                // -> cmake build FAIL); the "r" doctrine applies (declare +
                // frame root) exactly like the asynccase awaiter slot.
                if (ilKind[2..] == "System.Collections.Generic.KeyValuePair`2"
                    || ilKind.StartsWith("System.Collections.Generic.KeyValuePair`2<", StringComparison.Ordinal))
                {
                    refLocals.Add(d);
                    w.Line($"Il2CppObject* {d} = nullptr;");
                    continue;
                }
                // P25f (dhprobe): the net6+ interpolation handler
                // (System.Runtime.CompilerServices.DefaultInterpolatedStringHandler
                // -- a VALUETYPE local the C# compiler address-takes). The
                // runtime models the handler as a StringBuilder OBJECT in the
                // slot (icall_interp_handler_ctor writes it; AppendLiteral/
                // AppendFormatted/ToStringAndClear deref:r it). Same ref
                // carrier + frame root as the KeyValuePair`2 branch above.
                if (ilKind == "v:System.Runtime.CompilerServices.DefaultInterpolatedStringHandler")
                {
                    refLocals.Add(d);
                    w.Line($"Il2CppObject* {d} = nullptr;");
                    continue;
                }
                // guidcase (M7R2): a System.Guid IL-declared local is the
                // 16-byte VALUE SLOT (uint8_t[16]) -- the guid.h icalls write
                // through raw byte pointers (the "g" cast; there is NO
                // System_Guid struct -- System.Guid is a BCL type with no
                // TypeDef). The generic TypeCppName arm would emit
                // `System_Guid loc;` (unknown type name -> cmake build FAIL).
                if (ilKind[2..] == "System.Guid")
                {
                    w.Line($"uint8_t {d}[16];");
                    // Register the slot kind so the Guid value-copy / Empty
                    // memcpy paths recognize assignments to this local (the
                    // declaration alone does not seed locKind).
                    locKind[d] = "guid";
                    continue;
                }
                // D5 (gccase LatencyMode): a BCL ENUM-return value local
                // (System.Runtime.GCLatencyMode, ...) declares as int32 -- the
                // enum carrier. The generic TypeCppName fallback would emit the
                // MANGLED fullname (`System_Runtime_GCLatencyMode`), which is
                // never defined in generated.cpp (BCL enums have no TypeDef/SFS
                // here) -> unknown type name at the cmake build step.
                if (ilKind.Length > 2 && IsEnumReceiverType(ilKind[2..], types))
                {
                    w.Line($"{MuPrefix(d)}int32_t {d};");
                    continue;
                }
                // batch 156c (asynccase): a VALUETYPE local whose struct has
                // REFERENCE fields (the async state machine <F>d__1 -- the
                // builder/awaiter Il2CppObject* slots) must be ZERO-
                // INITIALIZED (`T d = {};` -- a bare `T d;` leaves the ref
                // fields as garbage) AND register those FIELD ADDRESSES as
                // GC frame roots: the collector scans the frame root list,
                // not the struct value inline, so a collection inside
                // MoveNext (Task.FromResult allocation) would otherwise sweep
                // the builder/awaiter the struct still references -- and a
                // collection BEFORE the first field store would scan the
                // garbage slot contents (the same AV class as unrooted ref
                // locals, CodeGenerator 1234-1242). The entry is
                // `local.field` -- WrapFrameRoots emits
                // `static_cast<void*>(&loc.field)` (a valid Il2CppObject**
                // root slot). These are UNCONDITIONAL whole-method roots (the
                // struct is address-taken via ldloca; RootSlotPass liveness
                // cannot track interior fields).
                var vtTypeDef = types.FirstOrDefault(t => t.FullName == ilKind[2..]);
                if (vtTypeDef != null && vtTypeDef.InstanceFields.Any(f => f.TypeKind == FieldTypeKind.Reference))
                {
                    // Nested structs (async state machines): declare with the
                    // PREFIXED TypeCppName (Cases_AsyncCase__B_d__1), not the
                    // short-name string overload (_B_d__1) -- the struct itself
                    // emits prefixed since the r78 nested-collision fix.
                    w.Line($"{TypeEmitter.TypeCppName(vtTypeDef)} {d} = {{ }};");
                    foreach (var rf in vtTypeDef.InstanceFields)
                        if (rf.TypeKind == FieldTypeKind.Reference)
                            structFieldRoots.Add($"{d}.{TypeEmitter.Sanitize(rf.Name)}");
                }
                else
                {
                    w.Line($"{MuPrefix(d)}{(vtTypeDef != null ? TypeEmitter.TypeCppName(vtTypeDef) : TypeCppName(ilKind[2..]))} {d};");
                }
                continue;
            }
            if (kind == "cstr")
            {
                // Audit H5: P/Invoke marshal temp -- the il2cpp_pinvoke_ext.h
                // contract is char* (alloc AND free both take char*; the
                // native const char* param accepts it implicitly). NOT a GC
                // root; the native buffer is freed after the call.
                w.Line($"{MuPrefix(d)}char* {d};");
                continue;
            }
            if (kind == "cstr16")
            {
                // wf2 (winforms1): CharSet.Unicode P/Invoke marshal temp --
                // char16_t* (il2cpp_pinvoke_string_to_utf16_alloc /
                // pinvoke_utf16_free pair; the native const char16_t* param
                // accepts it implicitly). NOT a GC root.
                w.Line($"{MuPrefix(d)}char16_t* {d};");
                continue;
            }
            if (kind == "ref")
            {
                refLocals.Add(d); // Task 10 frame-root candidate
                // M5 wiring: zero-initialize every GC-rooted reference local
                // -- WrapFrameRoots registers the slots at function ENTRY, so
                // an allocation that triggers a collection BEFORE the first
                // store would scan the garbage slot contents (known
                // ScanObject(0x9) AV class). nullptr is a safe scan.
                w.Line($"Il2CppObject* {d} = nullptr;");
                continue;
            }
            // M3 Task 11: a value-type temp (kind = the type name, e.g.
            // "Point") is declared as that C++ type; everything else falls
            // through to the scalar declarations below.
            // M5 wiring: "l" -> int64_t, "d" -> double (icall results and
            // int64/double constants held in temps).
            if (kind == "l")
            {
                w.Line($"{MuPrefix(d)}int64_t {d};");
                continue;
            }
            if (kind == "d")
            {
                w.Line($"{MuPrefix(d)}double {d};");
                continue;
            }
            // fnptr domain b23: a local holding a raw function pointer
            // (ldftn result or a `delegate*` param copy) is declared void*
            // -- the full pointer width the calli null-guard/cast need.
            if (kind == "p")
            {
                w.Line($"void* {d} = nullptr;");
                continue;
            }
            // M4.5: P/Invoke IntPtr result temps -> full-width pointer (an
            // int32_t temp truncates the x64 native return; the consumer
            // casts onward with the "p" arg-cast).
            if (kind == "ptr")
            {
                w.Line($"{MuPrefix(d)}intptr_t {d};");
                continue;
            }
            // M5 wiring: DateTime/TimeSpan icall results -> the runtime value
            // struct types (il2cpp_time.h -- included by generated.h).
            if (kind == "dt")
            {
                w.Line($"{MuPrefix(d)}il2cpp::runtime::Il2CppDateTime {d};");
                continue;
            }
            if (kind == "ts")
            {
                w.Line($"{MuPrefix(d)}il2cpp::runtime::Il2CppTimeSpan {d};");
                continue;
            }
            // P13 dtoffset: DateTimeOffset icall results -> the 16-byte runtime
            // value struct (il2cpp_dtoffset.h -- included by generated.h).
            if (kind == "dto")
            {
                w.Line($"{MuPrefix(d)}il2cpp::runtime::Il2CppDateTimeOffset {d};");
                continue;
            }
            // P17 biginteger: BigInteger icall results -> the 8-byte runtime
            // value struct (il2cpp_biginteger.h -- included by generated.h).
            if (kind == "bi")
            {
                w.Line($"{MuPrefix(d)}il2cpp::runtime::Il2CppBigInteger {d};");
                continue;
            }
            // decimal-domain T3 (+T4b "dec2" out-last): decimal icall results
            // -> the 16-byte runtime value struct (decimal.h -- included by
            // generated.h).
            if (kind == "dec" || kind == "dec2")
            {
                w.Line($"{MuPrefix(d)}il2cpp::runtime::Il2CppDecimal {d};");
                continue;
            }
            // Struct-marshal wiring: registered-struct icall results
            // (PtrToStructure<T> via struct_*_load) declare the native
            // value struct (il2cpp_pinvoke_struct.h -- in generated.h chain).
            if (kind == "rect")
            {
                w.Line($"{MuPrefix(d)}il2cpp::Il2CppPinvokeRect {d};");
                continue;
            }
            if (kind == "point")
            {
                w.Line($"{MuPrefix(d)}il2cpp::Il2CppPinvokePoint {d};");
                continue;
            }
            if (kind == "msg")
            {
                w.Line($"{MuPrefix(d)}il2cpp::Il2CppPinvokeMsg {d};");
                continue;
            }
            // Guid value slots: guid.h operates on RAW 16 bytes -- the
            // helpers take uint8_t[16] pointers (no struct wrapper exists),
            // so the slot is a plain uint8_t[16] array. C++ arrays are not
            // assignable, so value copies emit memcpy (see the AssignStmt
            // emitter); ldloca addresses are cast to the byte pointer at
            // the call sites (see EmitIcallCallArgs).
            if (kind == "guid")
            {
                w.Line($"uint8_t {d}[16];");
                continue;
            }
            // M7 CG-FIX (netcase): the IPAddress.Parse "ip4" value slot -- the
            // four address bytes as a plain uint8_t[4] array (ipaddr.h takes
            // uint8_t out4[4], same write-through contract as the Guid slot).
            if (kind == "ip4")
            {
                w.Line($"uint8_t {d}[4];");
                continue;
            }
            if (kind != "ref" && kind != "val" && kind.Length > 0)
                w.Line($"{mu}{kind} {d};");
            else
                w.Line(kind == "ref" ? $"{mu}Il2CppObject* {d};" : $"{mu}int32_t {d};");
        }
        if (declared.Count > 0) w.Line();

        // P0-1 (hotpath-audit-2): constant-length newarr tracking. A local
        // whose single assignment is a fresh `new T[N]` (directly or through
        // pure reference copies) is PROVABLY non-null with length N -- element
        // access on it can elide the null check and compare the index against
        // the constant N. Single-assignment guard: a target assigned more than
        // once could hold a different (possibly null) value at the access
        // site, so it is never tracked. The map is published to
        // _curNewArrLengths for the ldelem/stelem emission (TryEmitFastArrayPtr).
        // D-ARR-1 (arrresize, 2026-08-09): an ADDRESS-TAKEN guard on top of the
        // single-assignment gate. `Array.Resize(ref e, N)` passes the local's
        // address (`&e` -> LocAddrExpr) to icall_array_resize_i4, which WRITES
        // a new array reference through the slot -- a byref replacement that is
        // NOT an AssignStmt, so the single-assignment gate cannot see it. The
        // stale constant length then feeds the fast path: `new int[0]` folded
        // to `(0 >= 0 && 0 < 0LL)` -- always false -- so a legal post-resize
        // `e[0]`/`e[2]` read called il2cpp_array_fail (IndexOutOfRange).
        // Any local whose address is taken anywhere in the body is therefore
        // EXCLUDED from the constant-length proof (a read-only byref usage
        // only loses the fast path -- the checked helper keeps semantics).
        var addrTaken = new HashSet<string>();
        void CollectAddrTaken(Expr e)
        {
            switch (e)
            {
                case LocAddrExpr la: addrTaken.Add(la.Name); break;
                case BinaryExpr b: CollectAddrTaken(b.Left); CollectAddrTaken(b.Right); break;
                case UnaryExpr u: CollectAddrTaken(u.Operand); break;
                case CallExpr c: foreach (var a in c.Args) CollectAddrTaken(a); break;
                case VirtualCallExpr v: foreach (var a in v.Args) CollectAddrTaken(a); break;
                case DelegateInvokeExpr d: foreach (var a in d.Args) CollectAddrTaken(a); break;
                case DelegateNewExpr dn: CollectAddrTaken(dn.Target); break;
                case MarshalUtf8Expr m: CollectAddrTaken(m.Arg); break;
                case FieldExpr f: CollectAddrTaken(f.Object); break;
                case FieldAddrExpr fa: CollectAddrTaken(fa.Object); break;
            }
        }
        for (var i = 0; i < stmts.Count; i++)
        {
            if (!reachable[i]) continue;
            switch (stmts[i])
            {
                case AssignStmt a: CollectAddrTaken(a.Value); break;
                case CallStmt vc: foreach (var a in vc.Args) CollectAddrTaken(a); break;
                case ExprStmt es: CollectAddrTaken(es.Value); break;
                case RetStmt r when r.Value != null: CollectAddrTaken(r.Value); break;
                case BranchStmt b when b.Condition != null: CollectAddrTaken(b.Condition); break;
                case FieldAssignStmt f: CollectAddrTaken(f.Object); CollectAddrTaken(f.Value); break;
            }
        }
        var assignCount = new Dictionary<string, int>();
        for (var i = 0; i < stmts.Count; i++)
        {
            if (!reachable[i] || stmts[i] is not AssignStmt ac) continue;
            assignCount.TryGetValue(ac.Target, out var cnt);
            assignCount[ac.Target] = cnt + 1;
        }
        var newArrLengths = new Dictionary<string, long>();
        // P0-1 extension (arr_write gap): the raw fast paths emit the LITERAL
        // allocation element size instead of a `->elemSize` field read. The
        // C++ optimizer cannot hoist that field load out of an element loop
        // (the stores go through `->data`, which may alias the header -- per
        // the llvm-objdump of the ArrWrite inner loop), costing a memory load
        // + imul per element; the transpiler KNOWS the allocation element
        // size at the newarr token (ArrayElementType is the single source of
        // the newarr argument AND this literal), so the literal is
        // semantically identical to the runtime field on every path --
        // including mismatched-opcode (invalid) IL, where the checked helper
        // also uses the ALLOCATION size. Mirrors newArrLengths propagation
        // (pure reference copies carry the same allocation).
        var newArrElemSizes = new Dictionary<string, int>();
        var progressed = true;
        while (progressed)
        {
            progressed = false;
            for (var i = 0; i < stmts.Count; i++)
            {
                if (!reachable[i] || stmts[i] is not AssignStmt aa) continue;
                if (assignCount[aa.Target] != 1 || addrTaken.Contains(aa.Target) || newArrLengths.ContainsKey(aa.Target)) continue;
                long? len = aa.Value switch
                {
                    CallExpr nc when nc.Function.StartsWith("newarr_", StringComparison.Ordinal)
                                    && nc.Args.Count == 1 => ConstantValue(nc.Args[0]),
                    LocExpr lc when newArrLengths.TryGetValue(lc.Name, out var pv) => pv,
                    _ => null,
                };
                int? elemSize = aa.Value switch
                {
                    CallExpr nc when nc.Function.StartsWith("newarr_", StringComparison.Ordinal)
                                    && nc.Args.Count == 1 && int.TryParse(nc.Function[7..], out var arrTok)
                        => ArrayElementType(md, arrTok, types).ElemSize,
                    LocExpr lc when newArrElemSizes.TryGetValue(lc.Name, out var pv) => pv,
                    _ => null,
                };
                if (len is { } lv && elemSize is { } ev)
                {
                    newArrLengths[aa.Target] = lv;
                    newArrElemSizes[aa.Target] = ev;
                    progressed = true;
                }
            }
        }
        _curNewArrLengths = newArrLengths;
        _curNewArrElemSizes = newArrElemSizes;
        _curLocKinds = locKind;

        // perf2 str_chars: known-length STRING LITERAL locals. A local whose
        // single assignment is a StringConstExpr (ldstr -> g_strPool[id]) holds
        // a literal whose UTF-16 unit count the transpiler knows exactly
        // (.Value.Length). Pure LocExpr copies carry the same literal. This map
        // feeds ComputeLoopSafeAccesses so a get_Chars(s, i) access can be
        // certified in-bounds against the KNOWN literal length (same soundness
        // gate as newArrLengths: single-assignment only -- a target assigned
        // more than once could hold a different (shorter/null) string at the
        // access site). Published to _curStrLiteralLengths for the get_Chars
        // emission (TryEmitLoopProvenStringChars).
        var strLiteralLengths = new Dictionary<string, long>();
        var strProgressed = true;
        while (strProgressed)
        {
            strProgressed = false;
            for (var i = 0; i < stmts.Count; i++)
            {
                if (!reachable[i] || stmts[i] is not AssignStmt sa) continue;
                if (assignCount[sa.Target] != 1 || addrTaken.Contains(sa.Target) || strLiteralLengths.ContainsKey(sa.Target)) continue;
                long? slen = sa.Value switch
                {
                    StringConstExpr sc => sc.Value.Length,
                    LocExpr lc when strLiteralLengths.TryGetValue(lc.Name, out var pv) => pv,
                    _ => null,
                };
                if (slen is { } sv)
                {
                    strLiteralLengths[sa.Target] = sv;
                    strProgressed = true;
                }
            }
        }
        _curStrLiteralLengths = strLiteralLengths;

        // P0-1 loop-pattern extension: prove 0 <= iv < len for ldelem/stelem
        // accesses inside condition-last for-loops
        // (`for (iv = C; iv < B; iv++)` compiled to init; goto check; body(iv);
        // iv = iv + d; check: if (iv < B) goto body). The header check gates
        // every body entry and iv only increments, so at every access BEFORE
        // the increment iv is in [0, B). When B <= len (or B is ldlen of the
        // SAME fresh array) the access is provably in-bounds: the bounds
        // compare is dead (ComputeLoopSafeAccesses publishes the certified
        // (statement, array, index) triples for ldelem/stelem emission).
        // perf2 str_chars: the same proof also certifies get_Chars(s, i) string
        // accesses against a tracked string LITERAL local's known length.
        _curLoopSafeAccesses = ComputeLoopSafeAccesses(stmts, newArrLengths, strLiteralLengths);

        // GC-Root-1: slot-liveness analysis -- which slot names are live across
        // a reachable allocation point (RootSlotPass). Replaces the P0-2
        // whole-body pureLeaf flag: a body that can never allocate has no
        // allocation points, so its root set is empty (same elision); a body
        // that CAN allocate roots only the slots actually live across one.
        var rootAnalysis = RootSlotPass.Compute(stmts, reachable, StmtMayGc);

        // M3 Task 6: exception regions -- emit try/catch/finally wrappers by
        // IL offset membership. A statement belongs to the try block when its
        // SourceOffset is inside [TryOffset, TryOffset+TryLength); to the
        // handler when inside [HandlerOffset, HandlerOffset+HandlerLength).
        // M3 scope: leave-to-outside is NOT supported (a goto leaving a try
        // block is illegal C++) -- the exception cases in scope fall through
        // the try block naturally.
        //
        // M6 EH-nesting fix (exdeep gen/src/Cases_Exdeep.cpp 6 syntax errors):
        // the walk keeps a LIFO block stack instead of single activeTry/
        // activeHandler slots. The single-slot version broke on two nested
        // shapes:
        //  - f1/f2/f3 (nested catch): a statement in the INNER handler AND
        //    the outer try opened the outer `try {` AFTER the inner catch
        //    was already open (inside the catch body) -> bare try, unbalanced
        //    braces ("expected catch").
        //  - f5/f6/f9/f20 (finally + outer catch): the statement after a
        //    finally's catch-all landed in the OUTER handler, and the walk
        //    opened the outer catch WITHOUT closing the catch-all first ->
        //    a `catch` inside a catch body after its `throw;` ("expected
        //    expression" + cascading "expected '}'").
        // The stack pops innermost-first (LIFO), so a try nested inside a
        // handler closes before the handler, and the finally catch-all closes
        // before any outer handler opens. The try block of a region opens
        // when its try range is entered and closes with the region's dance
        // (catch-open / finally duplication / empty-handler catch).
        var regions = bodyInfo.ExceptionRegions;
        // Open blocks, innermost last: ('T', R) = region R's try block open
        // (attributed to the innermost region of its try-range chain; sibling
        // catches with identical ranges share ONE try block); ('H', R) = R's
        // catch clause open.
        var blocks = new List<(char Kind, int Region)>();
        // CG6 pairing-guard support: regions whose try block was OPENED during
        // this body walk (pushed on `blocks` at least once). The guard must
        // distinguish "try never opened (all DCE'd) -- needs an empty try"
        // from "try opened and already closed by the POP dance -- the catch
        // pairs with the real (closed) try". Only the former needs the guard.
        var openedTrys = new HashSet<int>();
        // Finally regions: their handler statements are DUPLICATED (normal
        // path after the try + exceptional path inside the catch-all, then
        // rethrow). The handler statements themselves are skipped in the main
        // flow (they are emitted at the two duplication points).
        var finallyRegions = regions.Where(r => r.Kind == ExceptionRegionKind.Finally).ToList();
        var finallyHandlerStmts = new Dictionary<int, List<Stmt>>();
        foreach (var fr in finallyRegions)
        {
            finallyHandlerStmts[fr.HandlerOffset] = stmts
                .Where(s => s.SourceOffset >= fr.HandlerOffset
                         && s.SourceOffset < fr.HandlerOffset + fr.HandlerLength)
                .ToList();
        }

        // M6 sibling-catch dispatch fix (exdeep f18): catch clauses sharing
        // ONE try range (same TryOffset/TryLength) must dispatch through a
        // SINGLE C++ catch + if/else-if chain. C++ semantics (verified on the
        // mingw toolchain with a standalone probe): a bare `throw;` inside a
        // handler resumes the search AFTER the current try-block's handler
        // list -- remaining same-try clauses are never considered (GCC warns
        // "-Wexceptions: will be caught by earlier handler"), so N sibling
        // `catch (il2cpp::DotNetException&)` clauses with guard-rethrow can
        // only ever reach the FIRST clause. catchChain[region] = the full
        // sibling list in dispatch order for regions that share their try
        // range with >= 2 catch clauses; openChains tracks which chains have
        // their single catch currently open (sibling bodies open/close on the
        // block stack WITHOUT closing the chain).
        var catchChain = new Dictionary<int, List<int>>();
        for (var i = 0; i < regions.Count; i++)
        {
            if (regions[i].Kind != ExceptionRegionKind.Catch || catchChain.ContainsKey(i)) continue;
            var siblings = new List<int>();
            for (var j = 0; j < regions.Count; j++)
            {
                if (regions[j].Kind == ExceptionRegionKind.Catch
                    && regions[j].TryOffset == regions[i].TryOffset
                    && regions[j].TryLength == regions[i].TryLength)
                    siblings.Add(j);
            }
            if (siblings.Count < 2) continue;
            foreach (var s in siblings) catchChain[s] = siblings;
        }
        var openChains = new HashSet<int>();

        // The chain of try regions containing an offset, outermost first.
        // Regions with identical (TryOffset, TryLength) are SIBLING catches of
        // one try block -- dedupe keeping the LAST (the block is attributed to
        // it; the walk's pop dance uses the attributed region, matching the
        // pre-M6 last-match-wins behavior).
        List<int> TryChain(int off)
        {
            var chain = new List<int>();
            for (var r = 0; r < regions.Count; r++)
            {
                if (off >= regions[r].TryOffset && off < regions[r].TryOffset + regions[r].TryLength)
                    chain.Add(r);
            }
            if (chain.Count <= 1) return chain;
            // Stable sort by try-range length DESC: the LONGEST range is the
            // outermost region (the decoder already re-sorts outer-first, so
            // this is a defensive re-establishment of the nesting order).
            chain = chain.OrderByDescending(r => regions[r].TryLength).ToList();
            var deduped = new List<int>();
            for (var i = 0; i < chain.Count; i++)
            {
                var dup = i + 1 < chain.Count
                    && regions[chain[i + 1]].TryOffset == regions[chain[i]].TryOffset
                    && regions[chain[i + 1]].TryLength == regions[chain[i]].TryLength;
                if (!dup) deduped.Add(chain[i]);
            }
            return deduped;
        }

        // Pop one open try block with its region's closing dance. May push an
        // open catch clause (the region's own catch, or the finally catch-all)
        // which the caller's LIFO loop then closes if the statement is not in
        // that handler.
        void CloseTryBlock(int r, int h)
        {
            var closedRegion = regions[r];
            if (closedRegion.Kind == ExceptionRegionKind.Finally
                && finallyHandlerStmts.TryGetValue(closedRegion.HandlerOffset, out var finStmts))
            {
                // Finally duplication (normal path): emit the finally code
                // AFTER the try block's statements (runs when the try
                // completes normally) -- still inside the try block, before
                // its closing brace.
                foreach (var fs in finStmts)
                    EmitStmt(w, fs, md, types, branchTargets, locKind);
            }
            if (catchChain.TryGetValue(r, out var cchain))
            {
                // Chain try (the block is attributed to the LAST sibling by
                // the dedup above): close the try into the single catch + the
                // if/else-if dispatch skeleton.
                w.Close();
                w.Line("}");
                if (h >= 0 && cchain.Contains(h))
                {
                    // `h` is the first sibling whose body receives statements
                    // (siblings before it have empty bodies, closed inline by
                    // the next guard's leading `}`).
                    OpenChainCatch(h);
                    blocks.Add(('H', h));
                }
                else
                {
                    // No sibling body receives a statement: emit the whole
                    // skeleton (all empty clause bodies) and close it.
                    OpenChainCatch(cchain[^1]);
                    CloseChainCatch(cchain[^1]);
                }
                return;
            }
            if (h == r)
            {
                // The statement lands in this region's handler: close the try
                // block and open the catch clause (nested inside any outer
                // try still open on the stack).
                w.Close();
                w.Line("}"); // close the try block before its catch
                OpenCatch(w, md, closedRegion);
                blocks.Add(('H', r));
            }
            else if (closedRegion.Kind == ExceptionRegionKind.Finally)
            {
                // Exceptional path for finally: catch-all, duplicate the
                // finally code, then rethrow the original exception.
                w.Close();
                w.Line("}"); // close the try block before its catch-all
                OpenCatch(w, md, closedRegion);
                if (finallyHandlerStmts.TryGetValue(closedRegion.HandlerOffset, out var fin2))
                {
                    // M7 CG-FIX (lockcase cmake FAIL: redefinition of label):
                    // the finally handler statements are emitted TWICE -- the
                    // normal path (in the try body) and this exceptional path
                    // (in the catch-all). A label inside the handler (lockcase's
                    // `L_54:`, the lockTaken guard target) would therefore be
                    // emitted twice in one C++ function scope -> "redefinition
                    // of label". Rename every label (and its branch targets)
                    // in THIS exceptional copy so the two copies never collide.
                    // The renamed label names are ALSO registered in
                    // branchTargets -- EmitStmt drops a label that is not a
                    // branch target ("dead label"), and the renamed label
                    // would otherwise be suppressed while the renamed branch
                    // still jumps to it ("use of undeclared label").
                    foreach (var fs in RenameFinallyLabels(fin2, "_exc", branchTargets))
                        EmitStmt(w, fs, md, types, branchTargets, locKind);
                }
                w.Line("throw; // rethrow the original exception (finally semantics)");
                blocks.Add(('H', r));
            }
            else if (closedRegion.Kind == ExceptionRegionKind.Catch
                     && !stmts.Any(s => s.SourceOffset >= closedRegion.HandlerOffset
                                     && s.SourceOffset < closedRegion.HandlerOffset + closedRegion.HandlerLength))
            {
                // Empty catch handler (e.g. `catch { }`): no IR statements
                // land in the handler, so the wrapper never opened -- a
                // bare `try { }` is a C++ syntax error. Emit the empty
                // catch clause (the type check still dispatches correctly
                // in a multi-catch chain).
                w.Close();
                w.Line("}"); // close the try block
                OpenCatch(w, md, closedRegion);
                w.Close();
                w.Line("} // end catch handler (empty handler)");
            }
            else
            {
                w.Close();
                w.Line("} // end try block");
            }
        }

        // Chain guard line for catch clause `s`: `if (<matches>) {` for the
        // first clause, `} else if (<matches>) {` for later ones (the leading
        // `}` closes the previous clause body); a catch-all clause (token 0,
        // .NET requires it LAST) becomes the final `} else {`.
        string ChainGuardLine(int s, bool first)
        {
            var reg = regions[s];
            if (reg.CatchTypeToken == 0)
                return first ? "if (true) {" : "} else {";
            var cond = $"il2cpp::il2cpp_exception_matches(\"{TypeTokenFullName(md, reg.CatchTypeToken)}\", __il2cpp_ex.type_name())";
            return first ? $"if ({cond}) {{" : $"}} else if ({cond}) {{";
        }

        // Open the single catch + the dispatch skeleton up to `arriving`
        // (the sibling whose body is about to receive statements). Siblings
        // before it have EMPTY bodies -- they stay open with no statements,
        // closed by the next guard's leading `}`.
        void OpenChainCatch(int arriving)
        {
            var chain = catchChain[arriving];
            w.Line("catch (il2cpp::DotNetException& __il2cpp_ex) {");
            w.Open();
            // msvc C4101: a chain whose clauses are ALL catch-alls has no
            // `__il2cpp_ex.type_name()` guard -- mark the variable used
            // (a typed clause's guard already uses it).
            if (!chain.Any(s => regions[s].CatchTypeToken != 0))
                w.Line("(void)__il2cpp_ex; // catch-all chain: the exception value is never inspected");
            var first = true;
            foreach (var s in chain)
            {
                if (!first) w.Close(); // the previous (empty) clause body ends
                w.Line(ChainGuardLine(s, first));
                w.Open();
                if (s == arriving) break;
                first = false;
            }
            openChains.Add(chain[0]);
        }

        // The statement arriving in the NEXT sibling's body: close the
        // previous body and open the next clause's guard.
        void ContinueChainCatch(int next)
        {
            w.Close(); // the previous clause body ends
            w.Line(ChainGuardLine(next, first: false));
            w.Open();
        }

        // Close the dispatch chain: the open clause body, then every
        // remaining (empty) clause as else-if, then the no-match rethrow tail
        // (a catch-all LAST clause replaces the tail -- it always matches),
        // then the single catch clause.
        void CloseChainCatch(int lastOpen)
        {
            var chain = catchChain[lastOpen];
            for (var i = chain.IndexOf(lastOpen) + 1; i < chain.Count; i++)
            {
                // Remaining siblings have empty bodies (a statement in one of
                // them would have arrived before any later statement).
                w.Close();
                w.Line(ChainGuardLine(chain[i], first: false));
                w.Open();
            }
            if (regions[chain[^1]].CatchTypeToken == 0)
            {
                // The chain ends in a catch-all: its body WAS the final else
                // (always matches) -- just close it, no rethrow tail.
                w.Close();
                w.Line("}");
            }
            else
            {
                // No clause matched: rethrow so the exception continues
                // unwinding to the enclosing handlers (.NET: the catch set
                // is closed). The leading `}` closes the last clause body.
                w.Close();
                w.Line("} else {");
                w.Open();
                w.Line("throw; // no clause matched: rethrow (unwinding continues)");
                w.Close();
                w.Line("}");
            }
            w.Close();
            w.Line("} // end catch handler");
            openChains.Remove(chain[0]);
        }

        for (var i = 0; i < stmts.Count; i++)
        {
            var s = stmts[i];
            // P0-1 loop-pattern extension: the finally-dance EmitStmt calls
            // (handler duplication) run before this statement's own emission and
            // must see NO loop proof (a handler can run with the induction
            // variable at any value -- an exception can interrupt the loop).
            _curStmtIndex = -1;
            var isEmittedLabel = s is LabelStmt && branchTargets.Contains(((LabelStmt)s).Name);
            if (!reachable[i] && !isEmittedLabel) continue; // dead-block statements are not emitted (C4702)

            // Skip finally-handler statements (they are emitted duplicated).
            if (finallyRegions.Any(fr => s.SourceOffset >= fr.HandlerOffset
                                       && s.SourceOffset < fr.HandlerOffset + fr.HandlerLength))
            {
                continue;
            }

            var off = s.SourceOffset;
            var chain = TryChain(off);
            // Innermost region whose handler range contains the statement.
            var h = -1;
            for (var r = 0; r < regions.Count; r++)
            {
                if (off >= regions[r].HandlerOffset && off < regions[r].HandlerOffset + regions[r].HandlerLength)
                    h = r;
            }

            // POP phase -- close open blocks the statement no longer needs,
            // innermost first. A catch clause closes when the statement is
            // not in its handler; a try block closes when the statement is
            // not in its try range (the dance may open the region's catch --
            // including the finally catch-all -- which the loop then closes
            // if the statement is not in that handler).
            while (blocks.Count > 0)
            {
                var (kind, r) = blocks[^1];
                if (kind == 'H')
                {
                    if (r == h) break;
                    if (catchChain.TryGetValue(r, out var cchain))
                    {
                        blocks.RemoveAt(blocks.Count - 1);
                        if (h >= 0 && cchain.Contains(h))
                        {
                            // The next statement opens a LATER sibling's
                            // body: the chain continues -- the previous
                            // body's close brace goes on the next guard line.
                            // BREAK, not continue: the statement is inside the
                            // chain's catch, which is nested inside every
                            // enclosing block still on the stack -- popping
                            // further would close the enclosing handler/try
                            // too early (FMT-2: chain-in-catch mangles the
                            // outer catch and the dispatch tail).
                            break;
                        }
                        // The chain ends here (no remaining sibling receives
                        // a statement): close it with the dispatch tail.
                        CloseChainCatch(r);
                        continue;
                    }
                    w.Close(); // end the catch block body
                    w.Line("} // end catch handler");
                    blocks.RemoveAt(blocks.Count - 1);
                }
                else
                {
                    if (chain.Contains(r)) break;
                    blocks.RemoveAt(blocks.Count - 1);
                    CloseTryBlock(r, h);
                }
            }
            // PUSH phase -- open try blocks for chain regions not yet open
            // (outermost first; a try nested inside an open handler stays
            // inside it), then the statement's handler clause if any.
            foreach (var r in chain)
            {
                if (blocks.Any(b => b.Kind == 'T' && b.Region == r)) continue;
                w.Line("try {");
                w.Open();
                blocks.Add(('T', r));
                openedTrys.Add(r);
            }
            if (h >= 0 && !blocks.Any(b => b.Kind == 'H' && b.Region == h))
            {
                // CG6 pairing guard (delegate2 cmake FAIL: `catch` without a
                // matching `try` -- Cases_Delegate2.cpp:361): every statement
                // of a region's try range may be eliminated (dead-block skip,
                // or a discarded-value statement such as a delegate-invoke
                // whose result the IL pops -- StackFold's pop produces no
                // statement), so the try block above never opened while the
                // handler below opens a catch. A bare `catch` is a C++
                // syntax error. Emit an EMPTY try block so the handler pairs
                // correctly; an empty try can never fire, which is exactly
                // the DCE'd semantics. Chain regions share ONE try block
                // (attributed to the LAST sibling -- the dedup in TryChain),
                // so the guard checks whether ANY chain member's try was ever
                // opened (openedTrys, not the current stack -- the POP dance
                // legitimately closes the try before later chain members
                // arrive, and those must NOT re-open an empty try).
                var tryOpen = catchChain.TryGetValue(h, out var hchain0)
                    ? hchain0.Any(r => openedTrys.Contains(r))
                    : openedTrys.Contains(h);
                if (!tryOpen)
                {
                    w.Line("try { } // CG6: empty try block -- its IL range emitted no statements");
                }
                if (catchChain.TryGetValue(h, out var hchain))
                {
                    if (openChains.Contains(hchain[0]))
                        ContinueChainCatch(h);
                    else
                        OpenChainCatch(h);
                }
                else
                {
                    OpenCatch(w, md, regions[h]);
                }
                blocks.Add(('H', h));
            }

            _curStmtIndex = i;
            // P5 ValueTuple-return: detect CallStmt(TupleReturnFields) +
            // AssignStmt(DiscardedValueExpr) pair and merge them into a single
            // operation. The StackFold splits call+store, but TupleReturnFields
            // needs the call to write through out-pointer args into the target.
            if (s is CallStmt csTuple && csTuple.Resolved?.TupleReturnFields is { Length: > 0 } trfTuple
                && i + 1 < stmts.Count && stmts[i + 1] is AssignStmt asTuple
                && asTuple.Value is StackFold.DiscardedValueExpr)
            {
                // 1. Declare a stack-allocated tuple local.
                // net9/10 E2E icallcase fix: (a) member separator must be ';'
                // (`double Item1, double Item2;` repeats the TYPE -- a syntax
                // error); (b) the same method can hit this path twice (Math.SinCos
                // + MathF.SinCos both map TupleReturnFields ["d","d"]) -- a second
                // plain struct definition would be a duplicate-symbol error, so
                // the definition is idempotent via a TU-wide macro guard.
                var tupleTypeFields = string.Join("; ", trfTuple.Select((ft, ti) =>
                    $"{ExprTypeCpp(ft)} Item{ti + 1}"));
                var tupleTypeName = TupleStructCppName(trfTuple);
                var tupleLocal = $"__tuple_{asTuple.Target}";
                // D5: record for FILE-SCOPE emission into generated.h (the old
                // inline #ifndef block was function-local -- a second method
                // using the same tuple type saw an undefined type).
                TupleDefsNeeded.Add($"{tupleTypeName}|{tupleTypeFields}");
                w.LineAligned($"{tupleTypeName} {tupleLocal} {{ }};",
                    $"IL {At(asTuple.SourceOpName, asTuple.SourceOffset)} -- P5: allocate ValueTuple local");
                // 2. Generate the call args + out-pointer args.
                var tupleArgs = EmitIcallCallArgs(csTuple.Resolved, csTuple.Args, md, types) ?? "";
                var fieldAddrs = new List<string>();
                for (int ti = 0; ti < trfTuple.Length; ti++)
                    fieldAddrs.Add($"(&({tupleLocal}).Item{ti + 1})");
                var allTArgs = tupleArgs.Length > 0
                    ? $"{tupleArgs}, {string.Join(", ", fieldAddrs)}"
                    : string.Join(", ", fieldAddrs);
                // 3. Emit the call.
                w.LineAligned($"{csTuple.Resolved.CppName}({allTArgs});",
                    $"IL {At(s.SourceOpName, s.SourceOffset)} -- P5: ValueTuple-return call ({trfTuple.Length}-field tuple)");
                // 4. Store the tuple in the target.
                w.LineAligned($"{EmitTarget(asTuple.Target)} = {tupleLocal};",
                    $"IL {At(asTuple.SourceOpName, asTuple.SourceOffset)} -- P5: store ValueTuple into {EmitTarget(asTuple.Target)}");
                i++; // skip the AssignStmt
                continue;
            }
            // SYMLN-1: map this statement's IL offset to its C# line and (re)
            // emit the persistent #line directive when the target changed. The
            // directive is a preprocessor instruction (column 0, RawLine) --
            // it makes compiler diagnostics, debugger breakpoints and
            // exception stack traces point at the C# source. A statement with
            // no PDB sequence point (offset unmapped / no PDB) keeps the
            // active directive -- still correct C# attribution, and the
            // initial "no directive" state means the PDB-less input emits none.
            if (symMap != null && s.SourceOffset >= 0)
            {
                var hit = symMap.Lookup(bodyInfo.MethodToken, s.SourceOffset);
                if (hit.HasValue && (hit.Value.Line != symLine || hit.Value.DocumentPath != symDoc))
                {
                    symLine = hit.Value.Line;
                    symDoc = hit.Value.DocumentPath;
                    w.RawLine($"#line {symLine} \"{EscapeDocumentPath(symDoc)}\"");
                }
            }
            EmitStmt(w, s, md, types, branchTargets, locKind);
        }
        // Close any open region blocks at the end of the body (LIFO; the try
        // dances cover the tail try/finally case).
        while (blocks.Count > 0)
        {
            var (kind, r) = blocks[^1];
            if (kind == 'H')
            {
                if (catchChain.TryGetValue(r, out var cchain))
                    CloseChainCatch(r);
                else
                {
                    w.Close();
                    w.Line("} // end catch handler");
                }
                blocks.RemoveAt(blocks.Count - 1);
            }
            else
            {
                blocks.RemoveAt(blocks.Count - 1);
                // No statement is in a handler at the body end -- the dance's
                // catch-open branch can never fire here (h = -1).
                CloseTryBlock(r, -1);
            }
        }
        // C4716 trailing return (MULTI-D batch find, delegate2 msvc): a non-void
        // method whose emitted body ends on a THROW path (IL ends with
        // throw/rethrow -- no 'ret' falls through) emits a C++ function whose
        // closing brace is reachable without a return statement.
        // il2cpp_throw_new is NOT declared [[noreturn]] (a plain void signature
        // keeps the generated `catch` dispatch an ordinary C++ call), so MSVC's
        // end-of-function flow check reports C4716 ("must return a value") and
        // the msvc half of the build fails. Append the return type's zero value
        // after the last emitted statement -- dead at runtime (throw_new always
        // unwinds) but it satisfies the compiler's flow analysis. Void methods
        // and methods whose end is provably unreachable (a 'ret' or an
        // unconditional branch terminates the fall-through) need no value.
        if (endReachable && bodyInfo.ReturnType != SignatureTypeCode.Void)
            w.Line(TrailingReturn(bodyInfo.ReturnType, _curStructReturnCpp));
        _curNewArrLengths = new Dictionary<string, long>();
        _curNewArrElemSizes = new Dictionary<string, int>();
        _curStmtIndex = -1;
        _curLoopSafeAccesses = new();
        _curStrLiteralLengths = new();
        _curLocKinds = new();
        _curReturnType = default;
        return (w.ToString(), refLocals, rootAnalysis, structFieldRoots);
    }

    //--------------------------------------------------------------------------
    // OpenCatch -- emit the catch wrapper opening for one region.
    //
    //   catch (il2cpp::DotNetException& __il2cpp_ex) {
    //       if (!il2cpp::il2cpp_exception_matches("<CaughtType>", __il2cpp_ex.type_name()))
    //           throw;   // non-matching type: rethrow (unwinding continues)
    //       <handler statements>
    //   }
    // The caught type comes from the region's CatchTypeToken (0 = catch-all,
    // matches everything -- the runtime's matcher returns true for the
    // "System.Exception" catch-all; a catch-all handler (filter/finally
    // without type) is emitted WITHOUT the type check.
    //
    // SINGLE-clause catches only. Try blocks with MULTIPLE sibling catch
    // clauses go through OpenChainCatch/ContinueChainCatch/CloseChainCatch
    // (single catch + if/else-if dispatch): C++ rethrow semantics skip the
    // remaining same-try clauses, so per-clause `catch` + guard-rethrow can
    // only ever reach the first clause (exdeep f18 root cause).
    //--------------------------------------------------------------------------
    private static void OpenCatch(CppWriter w, MetadataReader md, IlExceptionRegion region)
    {
        // The closing '}' of the PREVIOUS block (try or handler) is written
        // by the closer -- OpenCatch writes only the catch clause itself.
        w.Line("catch (il2cpp::DotNetException& __il2cpp_ex) {");
        w.Open();
        if (region.Kind == ExceptionRegionKind.Finally)
        {
            // finally region: the catch-all runs the finally code then
            // RETHROWS (the original exception continues unwinding -- .NET
            // finally semantics: cleanup code runs, the exception propagates).
            // The catch variable is NEVER inspected here -> explicitly mark it
            // used (msvc C4101 "unreferenced local variable" on every finally).
            w.Line("(void)__il2cpp_ex; // finally: the exception is rethrown, never inspected");
            w.Line("// finally block: cleanup code runs, then the original exception is rethrown");
        }
        else if (region.CatchTypeToken != 0)
        {
            var typeName = TypeTokenFullName(md, region.CatchTypeToken);
            w.Line($"if (!il2cpp::il2cpp_exception_matches(\"{typeName}\", __il2cpp_ex.type_name())) throw; // non-matching type: rethrow");
        }
        else
        {
            // catch-all: no type test, and the handler body cannot reference
            // the exception (no variable binding) -> mark it used (C4101).
            w.Line("(void)__il2cpp_ex; // catch-all: the exception value is never inspected");
            w.Line("// catch-all handler: matches every managed exception");
        }
    }

    //--------------------------------------------------------------------------
    // EscapeDocumentPath -- SYMLN-1: the document path string that goes inside
    // `#line N "..."`. The Portable PDB stores UTF-8 absolute paths. For the
    // directive: forward slashes are accepted by MSVC/GCC/clang on every
    // platform (backslashes are literal in an s-char-sequence but a leading
    // `\u`/`\x` in the raw bytes could be misread by some hosts), and a double
    // quote would terminate the string -- both are normalized away.
    //--------------------------------------------------------------------------
    private static string EscapeDocumentPath(string path)
    {
        var p = path.Replace('\\', '/');
        p = p.Replace("\"", "'");
        return p;
    }

    // P5 helper: map ValueTupleReturnFields type codes to C++ type names.
    private static string ExprTypeCpp(string code) => code switch
    {
        "d" => "double",
        "f" => "float",
        "l" => "int64_t",
        "i" => "int32_t",
        _ => throw new NotSupportedException($"Unknown ValueTuple field type code '{code}'")
    };

    // D1 (valuetuple-local design): the CANONICAL C++ struct name for a
    // ValueTuple with the given field type codes. Single source of truth --
    // the P5 emission site, the local-declaration arm, and the read-back
    // paths must all derive the SAME name or the emitted struct and its
    // users diverge (the defect that kept Math.SinCos uncompilable).
    internal static string TupleStructCppName(IReadOnlyList<string> fieldCodes)
        => $"System_ValueTuple_{fieldCodes.Count}_{string.Join("_", fieldCodes.Select(ExprTypeCpp))}";

    // D5 (valuetuple-local design): tuple struct DEFINITIONS collected during
    // body emission and flushed FILE-SCOPE into generated.h before it is
    // written. The old inline-in-function definition was function-local
    // scope -- a second method using the same tuple type saw an undefined
    // type (the macro guard only deduped TEXT, not C++ visibility).
    private static readonly SortedSet<string> TupleDefsNeeded = new(StringComparer.Ordinal);
    // P-D2 #1: delegate multicast helper DEFINITIONS collected during body
    // emission and flushed FILE-SCOPE into generated.h (inline -- shared by
    // every translation unit through the generated.h include, deduped by
    // signature hash, no ODR). One helper per distinct non-4-shape delegate
    // Invoke signature: flattens the invocation list through the exported
    // icall_delegate_get_invocation_list and calls every leaf's methodPtr with
    // the per-leaf static/instance dispatch, returning the LAST result.
    private static readonly SortedSet<string> DelegateHelperDefs = new(StringComparer.Ordinal);
    // P-D2 #2: GetFunctionPointerForDelegate instance-binding module
    // DEFINITIONS collected during resolution and flushed FILE-SCOPE into
    // generated.h (inline -- see DelegateGfpModuleDef). One module per distinct
    // delegate Invoke signature: a shared strong-GC-root slot array, one unique
    // thunk per slot, and the GfpS dispatcher (static single-cast -> raw
    // methodPtr; instance-bound -> (methodPtr, target) identity slot).
    internal static readonly SortedSet<string> GfpModuleDefs = new(StringComparer.Ordinal);
    // P-D2 #2: register a GfpS module (called by the resolver when a
    // GetFunctionPointerForDelegate call site resolves its delegate signature).
    internal static void RegisterDelegateGfpModule(string retCpp, string[] paramCpp, string hash)
    {
        GfpModuleDefs.Add(DelegateGfpModuleDef(retCpp, paramCpp, hash));
    }
    // Set when any method body reads System.Guid.Empty (the file-scope
    // il2cpp_guid_empty_bytes definition is flushed with the tuple defs).
    private static bool _guidEmptyNeeded;

    // F2 (decimal-domain): the CURRENT method's VALUETYPE-return C++ type
    // (null otherwise) -- set per-method in GenerateCore before body emission,
    // consumed by the C4716 trailing-return path which runs in a scope that
    // does not capture the per-method locals. Static = single-threaded tool.
    private static SignatureTypeCode _curReturnType = default;

    private static bool ReturnTypeIsRef(SignatureTypeCode code) => code switch
    {
        SignatureTypeCode.Void => false,
        SignatureTypeCode.Int32 or SignatureTypeCode.Int64 or SignatureTypeCode.UInt32 or SignatureTypeCode.UInt64
            or SignatureTypeCode.Boolean or SignatureTypeCode.Char
            or SignatureTypeCode.Int16 or SignatureTypeCode.UInt16 or SignatureTypeCode.SByte or SignatureTypeCode.Byte
            or SignatureTypeCode.IntPtr or SignatureTypeCode.UIntPtr or SignatureTypeCode.Single or SignatureTypeCode.Double
            or SignatureTypeCode.GenericTypeParameter or SignatureTypeCode.GenericMethodParameter => false,
        _ => true,
    };

    private static string? _curStructReturnCpp;

    // R2b P2 (closed generic clone context): the instantiation suffix of the
    // type whose body is currently being emitted (null for definitions), and
    // whether the current method's return slot is a VAR. Reference
    // instantiations render VAR slots as Il2CppObject*.
    private static string? _curInstantiationSuffix;
    private static bool _curReturnIsVar;
    // P-G3 (method-level sharing): whether the type whose method is currently
    // emitted is a SHARED generic implementation -- either the arity-0 generic
    // definition itself (Swap -- the definition IS the object template) or the
    // shared "object" clone of an arity>0 def (Pair_2_object_object). The
    // shared ABI renders byref VAR/MVAR slots as Il2CppObject** and stobj on a
    // VAR/MVAR slot as a double-pointer write.
    private static bool _curMethodShare;
    // caudit2-fix: per-argument "is an address" map for the CURRENT method body
    // (arg0 = value-type this pointer, B:V:/B:r: by-ref, P: raw pointer).
    // FieldAccess consults it instead of the old name-prefix heuristic.
    private static bool[] _curArgAddress = System.Array.Empty<bool>();

    private static void EmitStmt(CppWriter w, Stmt s, MetadataReader md, IReadOnlyList<TypeDef> types, HashSet<string> branchTargets, Dictionary<string, string> locKind)
    {
        switch (s)
        {
            case LabelStmt l:
                if (!branchTargets.Contains(l.Name)) break; // dead label: no references after constant-condition elimination, not emitted (-Wunused-label)
                // The label self-terminates with a null statement (`L_N: ;`).
                // C++ requires a label to be followed by a statement; a label
                // directly before a block-closing brace (lock's lockTaken guard
                // at the end of a try block) is C2059 in MSVC / syntax error in
                // clang -- the null statement is valid in every position.
                w.LineAligned($"{l.Name}: ;", $"IL branch target at offset 0x{l.SourceOffset:X4}");
                break;
            case AssignStmt a:
                // Guid value-slot wiring: NewGuid/Parse icalls WRITE through
                // their output-buffer parameter (void return -- guid.h's
                // helpers take raw uint8_t[16] pointers), so the assignment
                // becomes a statement call with the target slot as the
                // output buffer (`t = fn(...)` is not compilable C++: the
                // icall takes the buffer, not the value).
                if (a.Value is CallExpr gcal && gcal.Resolved is { ResultKind: "guid" or "dec" or "dec2" or "ip4" or "bi" } gres)
                {
                    if (a.Target.Contains('.'))
                        throw new NotSupportedException("Guid static-field store not wired: TypeEmitter static storage is int32_t and a 16-byte Guid write would overrun it");
                    var garg = EmitIcallCallArgs(gres, gcal.Args, md, types) ?? "";
                    if (gres.ResultKind == "dec")
                    {
                        // decimal-domain T3: decimal-producing icalls take the
                        // RESULT SLOT as their FIRST parameter (MethodMap.
                        // Decimal.cs contract -- pointer ABI like Guid, result
                        // slot prepended): `t = op_Addition(a, b)` becomes
                        // `icall_decimal_add((&t), a, b)`. The result slot is a
                        // 16-byte Il2CppDecimal VALUE local -- it does NOT
                        // decay to the icall's Il2CppDecimal* param, so the
                        // slot must be ADDRESSED (&t) like the value args
                        // (Guid's uint8_t[16] arrays decay; decimal structs do
                        // not -- blocker E). The target is a local/temp name
                        // (never a static field -- that throws above), so the
                        // plain `(&{name})` form is always correct here.
                        w.LineAligned($"{gres.CppName}((&{EmitTarget(a.Target)}){(garg.Length > 0 ? ", " + garg : "")});",
                            $"IL {At(a.SourceOpName, a.SourceOffset)} -- write decimal value into (&{EmitTarget(a.Target)}) (16-byte value slot)");
                    }
                    else if (gres.ResultKind == "dec2")
                    {
                        // T4b: decimal-producing icalls with the result slot
                        // LAST (icall_binary_read_decimal(self, out) -- the
                        // "dec" prepend contract would put it first, wrong
                        // order here): `t = ReadDecimal()` becomes
                        // `icall_binary_read_decimal(self, (&t))`.
                        w.LineAligned(garg.Length > 0
                                ? $"{gres.CppName}({garg}, (&{EmitTarget(a.Target)}));"
                                : $"{gres.CppName}((&{EmitTarget(a.Target)}));",
                            $"IL {At(a.SourceOpName, a.SourceOffset)} -- write decimal value into (&{EmitTarget(a.Target)}) (out-last slot)");
                    }
                    else if (gres.ResultKind == "bi")
                    {
                        // P17 biginteger: BigInteger-producing icalls take the
                        // RESULT SLOT as their FIRST parameter (the decimal
                        // contract -- the 8-byte Il2CppBigInteger VALUE local
                        // does not decay to the icall's Il2CppBigInteger*
                        // param, so the slot is ADDRESSED): `t = a + b`
                        // becomes `icall_biginteger_add((&t), a, b)`.
                        w.LineAligned($"{gres.CppName}((&{EmitTarget(a.Target)}){(garg.Length > 0 ? ", " + garg : "")});",
                            $"IL {At(a.SourceOpName, a.SourceOffset)} -- write BigInteger value into (&{EmitTarget(a.Target)}) (8-byte value slot)");
                    }
                    else
                    {
                        w.LineAligned(garg.Length > 0
                                ? $"{gres.CppName}({garg}, {EmitTarget(a.Target)});"
                                : $"{gres.CppName}({EmitTarget(a.Target)});",
                            $"IL {At(a.SourceOpName, a.SourceOffset)} -- write Guid value into {EmitTarget(a.Target)} (16-byte value slot)");
                    }
                    break;
                }
                // P5 ValueTuple-return wiring: the icall writes through
                // out-pointer args (&target.Item1, &target.Item2, ...), exactly
                // like the guid/dec/ip4/bi pattern. The CallExpr's Resolved
                // field carries TupleReturnFields (the field type codes). The
                // target local IS the ValueTuple struct.
                if (a.Value is CallExpr tcal && tcal.Resolved is { TupleReturnFields: { Length: > 0 } trf })
                {
                    var targs = EmitIcallCallArgs(tcal.Resolved, tcal.Args, md, types) ?? "";
                    var fieldAddrs = new List<string>();
                    for (int ti = 0; ti < trf.Length; ti++)
                        fieldAddrs.Add($"(&(({EmitTarget(a.Target)}).Item{ti + 1}))");
                    var allTArgs = targs.Length > 0
                        ? $"{targs}, {string.Join(", ", fieldAddrs)}"
                        : string.Join(", ", fieldAddrs);
                    w.LineAligned($"{tcal.Resolved.CppName}({allTArgs});",
                        $"IL {At(a.SourceOpName, a.SourceOffset)} -- ValueTuple-return call ({trf.Length}-field tuple)");
                    break;
                }
                // Guid value copies: the slot is uint8_t[16] -- C++ arrays are
                // NOT assignable, so a value copy emits a 16-byte memcpy
                // (exact .NET Guid copy semantics).
                if (a.Value is LocExpr srcLoc && locKind.TryGetValue(a.Target, out var tk) && tk == "guid"
                    && locKind.TryGetValue(srcLoc.Name, out var sk) && sk == "guid")
                {
                    if (a.Target.Contains('.'))
                        throw new NotSupportedException("Guid static-field copy not wired: TypeEmitter static storage is int32_t");
                    w.LineAligned($"std::memcpy({EmitTarget(a.Target)}, {srcLoc.Name}, 16);",
                        $"IL {At(a.SourceOpName, a.SourceOffset)} -- copy Guid value (16-byte value slot)");
                    break;
                }
                // Guid.Empty read into a Guid slot: the shared all-zero
                // constant copied with the same 16-byte memcpy discipline.
                if (a.Value is StaticFieldExpr gEmpty && gEmpty.FieldName == "System.Guid.Empty"
                    && locKind.TryGetValue(a.Target, out var gek) && gek == "guid")
                {
                    if (a.Target.Contains('.'))
                        throw new NotSupportedException("Guid.Empty store to a static field not wired: TypeEmitter static storage is int32_t");
                    _guidEmptyNeeded = true;
                    w.LineAligned($"std::memcpy({EmitTarget(a.Target)}, il2cpp_guid_empty_bytes, 16);",
                        $"IL {At(a.SourceOpName, a.SourceOffset)} -- Guid.Empty into {EmitTarget(a.Target)} (16-byte zero copy)");
                    break;
                }
                var assignCode = $"{EmitTarget(a.Target)} = {EmitExpr(a.Value, md, types)};";
                if (a.Value is CallExpr)
                    w.LineAligned(assignCode, $"IL {At(a.SourceOpName, a.SourceOffset)} -- store call result into {EmitTarget(a.Target)}");
                else if (a.Target.Contains('.'))
                    w.LineAligned(assignCode, $"IL {At(a.SourceOpName, a.SourceOffset)} -- store value into static field {EmitTarget(a.Target)}");
                else if (assignCode == $"{EmitTarget(a.Target)} = {EmitTarget(a.Target)};")
                    w.LineAligned($"/* value already in {EmitTarget(a.Target)} -- merge/store no-op (P26 -Wself-assign guard) */",
                        $"IL {At(a.SourceOpName, a.SourceOffset)} -- self-assign suppressed");
                else
                    w.LineAligned(assignCode, $"IL {At(a.SourceOpName, a.SourceOffset)} -- store evaluated value into {EmitTarget(a.Target)}");
                break;
            case FieldAssignStmt fa:
                // Instance field write: null-check the receiver (.NET semantics: dereferencing null
                // throws NullReferenceException) -> write barrier (M3 Task 3: reference-field writes
                // mark the card table via il2cpp_write_barrier) -> direct member write.
                // GCC-REGRESSION (gccompact): the stored value must be cast to the SAME C++ type
                // the field is DECLARED with -- both sides now derive from the single authority
                // TypeEmitter.ReferenceCppType(field type, allTypes). Previously the assignment
                // used TypeCppName(...)+"*" (Node*) while the declaration used the reference-
                // type table (Il2CppObject* for a nested user type whose model key is the short
                // name) -- incompatible pointer types on every nested-class reference store.
                // M3 Task 11: value-type receivers are plain values (no null check, no barrier).
                var faValueCpp = TypeEmitter.ReferenceCppType(fa.RefTargetFullName, types);
                // wf5 (winforms1 phase-4): a reference field whose type is a
                // custom DELEGATE has no generated struct -- the value stays
                // Il2CppObject* (identity), so the store cast must not try a
                // <DelegateName>* reinterpret (unknown type name in C++).
                if (IsCustomDelegateType(fa.RefTargetFullName, types)) faValueCpp = "Il2CppObject*";
                // COM stage 17 batch 3 (com17type): an INTERFACE-typed
                // reference field (DragEventArgs.Data : IDataObject) has no
                // generated struct either -- the store cast stays Il2CppObject*
                // (the interface descriptor is extern-only, never a C++ type).
                if (IsInterfaceType(fa.RefTargetFullName, types)) faValueCpp = "Il2CppObject*";
                // r77-cannot-init (Top1, instance field stfld): when the field
                // is Il2CppObject* but the assigned value is a val-kind int32,
                // static_cast<Il2CppObject*>(int32) is ill-formed in C++. Use
                // reinterpret_cast through intptr_t to express the slot-reuse
                // semantics (IL stack slot holds an int32 that is logically
                // an Il2CppObject* in the ref-typed field).
                var faValIsVal = fa.IsRefField && ExprKind(fa.Value, _curLocKinds) == "val";
                var faValue = !faValIsVal
                    ? (fa.IsRefField
                        ? (faValueCpp == "Il2CppObject*"
                            ? $"static_cast<Il2CppObject*>({EmitExpr(fa.Value, md, types)})"
                            : NullSafePtrCast(faValueCpp, EmitExpr(fa.Value, md, types)))
                        : EmitExpr(fa.Value, md, types))
                    : (faValueCpp == "Il2CppObject*"
                        ? $"reinterpret_cast<Il2CppObject*>({EmitExpr(fa.Value, md, types)})"
                        : NullSafePtrCast(faValueCpp, EmitExpr(fa.Value, md, types)));
                var faTarget = FieldAccess(fa.Object, fa.OwnerFullName, fa.FieldCppName, md, types, fa.IsValueTypeObject);
                if (fa.IsRefField && !fa.IsValueTypeObject)
                {
                    // Reference field: write barrier first (record old-generation writes -> card table), then assign.
                    // M7 CG-FIX (gcstress/objects/propcase cmake FAIL): the slot
                    // address is `&(<Node*->Next>)` -- a `Node**` (address of a
                    // Node* field). static_cast from `Node**` to `void**` is NOT
                    // allowed (unrelated pointer types); reinterpret_cast is the
                    // exact re-interpretation contract (the runtime barrier reads
                    // the slot through void**). The value cast below stays
                    // reinterpret_cast: the source may be an integer constant (e.g. a token);
                    // static_cast cannot convert an integer to a pointer, but reinterpret_cast can.
                    var barrierVal = faValIsVal
                        ? $"reinterpret_cast<Il2CppObject*>({EmitExpr(fa.Value, md, types)})"
                        : $"reinterpret_cast<Il2CppObject*>({EmitExpr(fa.Value, md, types)})";
                    w.LineAligned($"il2cpp::il2cpp_write_barrier(reinterpret_cast<void**>(&{faTarget}), {barrierVal});",
                        $"IL {At(fa.SourceOpName, fa.SourceOffset)} -- write barrier before reference-field store");
                }
                var faCode = $"{faTarget} = {faValue};";
                w.LineAligned(faCode,
                    $"IL {At(fa.SourceOpName, fa.SourceOffset)} -- {(fa.IsValueTypeObject ? "store into value-type field" : "null-checked store into instance field")} {fa.OwnerFullName}.{fa.FieldCppName}");
                break;
            case RetStmt r:
                if (r.Value == null)
                {
                    w.LineAligned("return;",
                        $"IL {At(r.SourceOpName, r.SourceOffset)} -- exit method with no value (void)");
                }
                else
                {
                    var retExpr = EmitExpr(r.Value, md, types);
                    var retKind = TypeKindResolver.ResolveExprKind(r.Value, _curLocKinds);
                    // ifcase: a STATIC FIELD return whose signature is a value
                    // type must not ride the ExprKind heuristic -- that
                    // returns "ref" for every non-Decimal/DateTime static
                    // field, so `return flag;` (int32 storage) entered the
                    // ref->int narrow bridge and emitted the ill-formed
                    // reinterpret_cast<intptr_t>(int32_t). The authoritative
                    // metadata decode (StaticFieldExprIsReference, the same
                    // predicate the field READ declaration path uses) decides
                    // the kind here. Local override only: ResolveExprKind's
                    // public classification is untouched.
                    if (r.Value is StaticFieldExpr sfe)
                        retKind = StaticFieldExprIsReference(md, types, sfe.FieldName) ? "ref" : "val";
                    // Return type bridge: method signature return type differs from expression kind.
                    // Case 1: returns Il2CppObject* (ref) but expression is int/val → reinterpret_cast through intptr_t.
                    // Case 2: returns int32_t but expression is ref/ptr → static_cast<int32_t> narrow (lossy but preserves slot-reuse semantics).
                    // MSVC C2440 guard (msvc P/Invoke family): a "ptr"-kind expression is ALREADY the intptr_t
                    // carrier (IntPtr ValueTypeNewExpr / ResultKind=="ptr" call, incl. the
                    // il2cpp_pinvoke_result<intptr_t> bridge). Wrapping it in
                    // reinterpret_cast<intptr_t> is a SAME-TYPE reinterpret -- MSVC rejects it
                    // (C2440) while g++/clang tolerate (a fake-green). "ref" is a real pointer
                    // (Il2CppObject*/concrete*), where the reinterpret is required.
                    string IntPtrBridge(string targetCpp, string expr) =>
                        retKind == "ptr"
                            ? $"static_cast<{targetCpp}>({expr})"
                            : $"static_cast<{targetCpp}>(reinterpret_cast<intptr_t>({expr}))";
                    // R2b P2: a VAR return in a reference instantiation is
                    // declared Il2CppObject* at EmitBody entry -- a reference
                    // expression is a plain upcast (the generic bridges below
                    // would reinterpret through int32_t).
                    // D5: a SHARED method-level generic definition's MVAR
                    // return (arity-0 def, InstantiationSuffix == null) is the
                    // same boxed-object slot -- the upcast applies to it too.
                    if (_curReturnIsVar && (_curInstantiationSuffix != null || _curMethodShare) && retKind is "ref" or "ptr")
                        retExpr = $"static_cast<Il2CppObject*>({retExpr})";
                    else if ((_curReturnType == SignatureTypeCode.Int32 || _curReturnType == SignatureTypeCode.Int64) && retKind is "ref" or "ptr")
                        retExpr = IntPtrBridge(ReturnTypeCpp(_curReturnType), retExpr);
                    else if ((_curReturnType == SignatureTypeCode.IntPtr || _curReturnType == SignatureTypeCode.UIntPtr) && retKind is "val" or "i")
                        retExpr = $"static_cast<{ReturnTypeCpp(_curReturnType)}>({retExpr})";
                    else if (ReturnTypeIsRef(_curReturnType) && retKind is "val" or "i" or "l" or "d" or "f" or "b" or "c")
                    {
                        // Enum val types (System_TypeCode etc.) must be boxed to Il2CppObject* --
                        // reinterpret_cast through intptr_t is UB (enum is not a pointer).
                        retExpr = $"il2cpp::il2cpp_box_int32(nullptr, static_cast<int32_t>({retExpr}))";
                    }
                    else if (!ReturnTypeIsRef(_curReturnType) && retKind is "ref" or "ptr")
                        retExpr = IntPtrBridge(ReturnTypeCpp(_curReturnType), retExpr);
                    // P-G2 (paircase Make): a SZArray return (T1[]) is declared
                    // Il2CppArray* by ReturnTypeCpp, but the returned value is a
                    // newarr result held as Il2CppObject* -- bridge the object
                    // slot to the array signature so the body links.
                    else if ((byte)_curReturnType == 0x1D && retKind is "ref" or "r")
                        retExpr = $"reinterpret_cast<Il2CppArray*>({retExpr})";
                    w.LineAligned($"return {retExpr};",
                        $"IL {At(r.SourceOpName, r.SourceOffset)} -- exit method returning the computed value");
                }
                break;
            case BranchStmt b:
                // When the condition is a compile-time constant (compiler-resident stack constant,
                // e.g. `(5LL > 3LL) == 0LL`), hard-code the branch: emitting `if(<constant>)` would trigger MSVC /W4 C4127, and a constant condition is already determinate control flow.
                if (b.Condition != null && TryFoldInt32(b.Condition, out var condConst))
                {
                    if (condConst != 0)
                        w.LineAligned($"goto {b.TargetLabel};",
                            $"IL {At(b.SourceOpName, b.SourceOffset)} -- constant-true condition, unconditional jump to {b.TargetLabel}");
                    else
                        w.LineAligned("",
                            $"IL {At(b.SourceOpName, b.SourceOffset)} -- constant-false condition, branch eliminated");
                }
                else
                {
                    w.LineAligned(b.Condition == null
                            ? $"goto {b.TargetLabel};"
                            : $"if({EmitCondition(b.Condition, md, types)}) goto {b.TargetLabel};",
                        $"IL {At(b.SourceOpName, b.SourceOffset)} -- {(b.Condition == null ? "unconditional jump" : "jump when condition evaluates to true")} to {b.TargetLabel}");
                }
                break;
            case CallStmt c:
                if (c.Function.StartsWith("ensure_cctor_", StringComparison.Ordinal))
                {
                    // Cctor guard optimization (audit #5): the done-flag test is
                    // INLINED at the access point -- the hot path is one load +
                    // test + predicted-not-taken branch instead of a NOINLINE
                    // call. The cold path still runs the NOINLINE
                    // ensure_cctor_* (which sets the flag then runs the .cctor
                    // body -- frame discipline preserved).
                    w.LineAligned($"if ({c.Function["ensure_cctor_".Length..]}_cctor_done == false) {c.Function}();",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- run the static-constructor guard when not yet initialized");
                    break;
                }
                if (c.Function == "capture_last_error")
                {
                    // M4 Task 4: capture the OS error code after a SetLastError=true call.
                    w.LineAligned("il2cpp::il2cpp_pinvoke_capture_last_error();",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- capture OS error code (SetLastError=true)");
                    break;
                }
                if (c.Function == "pinvoke_utf8_free")
                {
                    // Audit H5: release a P/Invoke string marshal buffer after the call.
                    w.LineAligned($"il2cpp::il2cpp_pinvoke_utf8_free({EmitExpr(c.Args[0], md, types)});",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- release the marshalled UTF-8 buffer");
                    break;
                }
                if (c.Function == "pinvoke_utf16_free")
                {
                    // wf2 (winforms1): release a UTF-16 marshal buffer (CharSet.Unicode).
                    w.LineAligned($"il2cpp::il2cpp_pinvoke_utf16_free({EmitExpr(c.Args[0], md, types)});",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- release the marshalled UTF-16 buffer");
                    break;
                }
                if (c.Function.StartsWith("vctor_", StringComparison.Ordinal)
                    && int.TryParse(c.Function[6..], out var vctok))
                {
                    // Value-type ctor: {T}__ctor_hash(tN, args...) -- this is
                    // the value itself (the ctor takes it by value; field
                    // access uses (&this)->f inside).
                    var resolvedVc = MethodRefResolver.Resolve(md, vctok, types);
                    var fnName = resolvedVc.CppName;
                    var thisCode = EmitExpr(c.Args[0], md, types);
                    // decimal-domain T3 (blocker E): decimal ctor icalls take
                    // Il2CppDecimal* -- the value slot must be ADDRESSED (&tN)
                    // or the struct value cannot convert to the pointer param
                    // (newobj decimal literal shape:
                    // icall_decimal_ctor_5((&tN), ...)). DateTime/TimeSpan
                    // (ResultIsReceiver) take the value and are unaffected.
                    // P17 biginteger: the BigInteger ctor icalls take
                    // Il2CppBigInteger* -- the same address-taking.
                    // A2 (dtdefect001): the DateTimeOffset ctor icalls take
                    // Il2CppDateTimeOffset* (the out-param result slot) --
                    // the same address-taking, or the struct value cannot
                    // convert to the pointer param.
                    if (resolvedVc.CppName.StartsWith("il2cpp::runtime::icall_decimal", StringComparison.Ordinal)
                        || resolvedVc.CppName.StartsWith("il2cpp::runtime::icall_biginteger", StringComparison.Ordinal)
                        || resolvedVc.CppName.StartsWith("il2cpp::runtime::icall_dtoffset", StringComparison.Ordinal))
                        thisCode = $"(&{thisCode})";
                    // drop-cast fix (icallwide): the old inline walk ignored
                    // ParamTypeNames -- a trailing "drop" (DateTime
                    // ctor(y,m,d,h,mi,s,kind) drops the DateTimeKind) leaked
                    // into the emitted call ("expected 6, have 7"). Walk the
                    // casts exactly like EmitIcallCallArgs does: index ai in
                    // the casts array aligns with c.Args[ai] (slot 0 is the
                    // receiver); "drop" skips the argument.
                    var argCode = new List<string>();
                    for (var ai = 1; ai < c.Args.Count; ai++)
                    {
                        var castV = resolvedVc.ParamTypeNames != null && ai < resolvedVc.ParamTypeNames.Length
                            ? resolvedVc.ParamTypeNames[ai]
                            : null;
                        if (castV == "drop") continue;
                        argCode.Add(EmitExpr(c.Args[ai], md, types));
                    }
                    var argList = argCode.Count > 0 ? ", " + string.Join(", ", argCode) : "";
                    if (resolvedVc.ResultIsReceiver)
                    {
                        // M5 wiring: BCL value-type ctors (DateTime/TimeSpan)
                        // -- the runtime icall RETURNS the initialized value;
                        // the receiver slot is the assignment target.
                        w.LineAligned($"{thisCode} = {fnName}({argList.TrimStart(',', ' ')});",
                            $"IL {At(c.SourceOpName, c.SourceOffset)} -- initialize value-type instance (BCL runtime icall)");
                    }
                    else
                    {
                        w.LineAligned($"{fnName}({thisCode}{argList});",
                            $"IL {At(c.SourceOpName, c.SourceOffset)} -- initialize value-type instance (newobj)");
                    }
                    break;
                }
                if (c.Function.StartsWith("dinvoke_", StringComparison.Ordinal)
                    && int.TryParse(c.Function[8..], out var ditok))
                {
                    // void delegate Invoke: reuse the expression emitter.
                    var dinvoke = new DelegateInvokeExpr { MethodToken = ditok, ReturnsRef = false };
                    dinvoke.Args.AddRange(c.Args);
                    w.LineAligned($"{EmitExpr(dinvoke, md, types)};",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- invoke delegate (void)");
                    break;
                }
                if (c.Function.StartsWith("calli_", StringComparison.Ordinal)
                    && long.TryParse(c.Function[6..], out var calliTok))
                {
                    // void calli (fnptr domain b23): reuse the expression
                    // emitter (the null-pointer guard throws NRE on null).
                    var calliExpr = new CallExpr { Function = c.Function, ReturnsRef = false };
                    calliExpr.Args.AddRange(c.Args);
                    w.LineAligned($"{EmitTask9Call(calliExpr, md, types)};",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- indirect call through function pointer (calli, void)");
                    break;
                }
                if (c.Function.StartsWith("throw_", StringComparison.Ordinal))
                {
                    // Audit H8: typed throw -- StackFold captured the exception
                    // type from the newobj at the throw site; the emitted
                    // carrier carries the type + its .NET base chain so a
                    // catch of the derived type AND of a base type both match
                    // (il2cpp_exception_matches walks the chain).
                    // excext (m7m): StackFold now forwards the BCL-exception
                    // ctor message/paramName args -- the carrier carries the
                    // REAL message instead of the hardcoded "exception thrown".
                    var thrownType = c.Function[6..];
                    var chain = ExceptionChain(thrownType, types);
                    w.LineAligned(EmitThrowCall(thrownType, chain, c.Args, md, types),
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- throw a managed {thrownType} exception");
                    break;
                }
                if (c.Function == "rethrow")
                {
                    // M3 rethrow (`throw;` inside a catch): the statement lives
                    // inside the generated catch (il2cpp::DotNetException&
                    // __il2cpp_ex) block, so bare C++ `throw;` rethrows the
                    // CURRENT exception preserving its type -- exactly .NET
                    // rethrow semantics (exdeep f6/f7/f21 rely on this).
                    w.LineAligned("throw;",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- rethrow the current exception (bare throw;)");
                    break;
                }
                if (c.Function.StartsWith("vcall_", StringComparison.Ordinal)
                    && int.TryParse(c.Function[6..], out var vtok))
                {
                    // void virtual call (callvirt with no return): build a temp VirtualCallExpr to reuse the emission path.
                    var vcall = new VirtualCallExpr { MethodToken = vtok, ReturnsRef = false };
                    vcall.Args.AddRange(c.Args);
                    w.LineAligned($"{EmitVirtualCall(vcall, md, types)};",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- invoke virtual method via vtable slot (void)");
                    break;
                }
                if (c.Function.StartsWith("stelem_", StringComparison.Ordinal))
                {
                    // stelem statement (write an array element): build a temp CallExpr to reuse expression emission.
                    var stelem = new CallExpr { Function = c.Function, ReturnsRef = false };
                    stelem.Args.AddRange(c.Args);
                    w.LineAligned($"{EmitTask9Call(stelem, md, types)};",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- store value into array element");
                    break;
                }
                // ILOP-2 P2: stind_{op} -- pointer store by opcode width
                // (Stind_i1/i2/i4/i8/i/u1/u2/u4/r4/r8/ref). Args=[addr, value].
                // D-RND2-1 (randomdeep2 GAPS): `arr[i]++` folds to
                // ldelema -> ldind -> add -> stind; the statement emission
                // DROPPED the trailing ';' (every other statement arm appends
                // it), so the increment's C++ line
                //   (*reinterpret_cast<int32_t*>(t11) = ...)
                // had no terminator -- "expected ';' after expression" build
                // abort. The whole pointer-store family (stind/stobj/cpobj/
                // cpblk/initblk) shared the same missing-terminator root.
                if (c.Function.StartsWith("stind_", StringComparison.Ordinal))
                {
                    var stindSuffix = c.Function[(c.Function.LastIndexOf('_') + 1)..];
                    var stindAddr = EmitExpr(c.Args[0], md, types);
                    var stindVal = EmitExpr(c.Args[1], md, types);
                    w.LineAligned((stindSuffix switch
                    {
                        "i1" => $"(*reinterpret_cast<int8_t*>({stindAddr}) = static_cast<int8_t>({stindVal}))",
                        "u1" => $"(*reinterpret_cast<uint8_t*>({stindAddr}) = static_cast<uint8_t>({stindVal}))",
                        "i2" => $"(*reinterpret_cast<int16_t*>({stindAddr}) = static_cast<int16_t>({stindVal}))",
                        "u2" => $"(*reinterpret_cast<uint16_t*>({stindAddr}) = static_cast<uint16_t>({stindVal}))",
                        "i4" or "u4" => $"(*reinterpret_cast<int32_t*>({stindAddr}) = static_cast<int32_t>({stindVal}))",
                        "i8" => $"(*reinterpret_cast<int64_t*>({stindAddr}) = static_cast<int64_t>({stindVal}))",
                        "i" => $"(*reinterpret_cast<intptr_t*>({stindAddr}) = static_cast<intptr_t>({stindVal}))",
                        "r4" => $"(*reinterpret_cast<float*>({stindAddr}) = static_cast<float>({stindVal}))",
                        "r8" => $"(*reinterpret_cast<double*>({stindAddr}) = static_cast<double>({stindVal}))",
                        "ref" => $"(*reinterpret_cast<Il2CppObject**>({stindAddr}) = static_cast<Il2CppObject*>({stindVal}))",
                        _ => throw new NotSupportedException($"stind opcode suffix {stindSuffix} not supported in emission"),
                    }) + ";", $"IL {At(c.SourceOpName, c.SourceOffset)} -- store value through pointer (stind.{stindSuffix})");
                    break;
                }
                // ILOP-2 P4: stobj_{token} -- store a value type by TYPE TOKEN.
                if (c.Function.StartsWith("stobj_", StringComparison.Ordinal)
                    && int.TryParse(c.Function["stobj_".Length..], out var stobjTok))
                {
                    var stobjName = TypeTokenFullName(md, stobjTok);
                    var stobjAddr = EmitExpr(c.Args[0], md, types);
                    var stobjVal = EmitExpr(c.Args[1], md, types);
                    // P-G3 R-B: a VAR/MVAR TypeSpec stobj in a SHARED method is
                    // a byref object slot -- the shared ABI reads/writes an
                    // Il2CppObject* THROUGH the address (Il2CppObject**), the
                    // same depth ldobj uses (7871). A single-pointer write on
                    // the shared arg (declared Il2CppObject**) would assign
                    // the object value INTO the pointer slot ("no match for
                    // 'operator='", operand types Il2CppObject vs
                    // Il2CppObject*).
                    if (IsVarOrMvarTypeSpec(md, stobjTok) && _curMethodShare)
                    {
                        w.LineAligned($"(*reinterpret_cast<Il2CppObject**>({stobjAddr}) = static_cast<Il2CppObject*>({stobjVal}));",
                            $"IL {At(c.SourceOpName, c.SourceOffset)} -- store the boxed object into the byref T slot (shared object ABI)");
                        break;
                    }
                    w.LineAligned($"(*reinterpret_cast<{TypeCppName(stobjName)}*>({stobjAddr}) = {stobjVal});",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- store value-type value through pointer");
                    break;
                }
                // ILOP-2 P5: cpobj_{token} -- copy a value type (dest <- src).
                if (c.Function.StartsWith("cpobj_", StringComparison.Ordinal)
                    && int.TryParse(c.Function["cpobj_".Length..], out var cpobjTok))
                {
                    var cpobjName = TypeTokenFullName(md, cpobjTok);
                    var cpobjDest = EmitExpr(c.Args[0], md, types);
                    var cpobjSrc = EmitExpr(c.Args[1], md, types);
                    w.LineAligned($"(*reinterpret_cast<{TypeCppName(cpobjName)}*>({cpobjDest}) = *reinterpret_cast<{TypeCppName(cpobjName)}*>({cpobjSrc}));",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- copy value-type value (dest <- src)");
                    break;
                }
                // ILOP-2 P4: initobj_{token} -- zero-initialize a value type at
                // the address (int/ref -> *addr = 0 / nullptr; structs ->
                // zero-fill). TypeEmitter only emits struct layouts for the
                // Int32/Reference slot model, so a plain scalar zero is the
                // faithful zero-init for every reachable value type here.
                if (c.Function.StartsWith("initobj_", StringComparison.Ordinal)
                    && int.TryParse(c.Function["initobj_".Length..], out var initobjTok))
                {
                    var initobjAddr = EmitExpr(c.Args[0], md, types);
                    // batch 156c (asynccase): initobj of a GENERIC-INSTANTIATION
                    // type (TaskAwaiter`1<int32> -- the async state machine's
                    // awaiter-field clear). The awaiter is a REFERENCE slot
                    // (Il2CppObject*), so the faithful zero-init is a null
                    // store; the generic TypeSpec has no sizeof(TypeCppName)
                    // struct (the TypeTokenFullName decode below would throw
                    // on the TypeSpecification token kind).
                    if (initobjTok >= 0 && MetadataTokens.Handle(initobjTok).Kind == HandleKind.TypeSpecification)
                    {
                        // R2b P2 (default(T) in a generic body): a VAR/MVAR
                        // slot is the int32 VALUE in the definition/int
                        // closure -- an 8-byte Il2CppObject** null store on
                        // it overruns the slot. Closed reference clones keep
                        // the reference-slot null store (and every non-VAR
                        // TypeSpec -- the awaiter GenericInst -- is a ref).
                        if (IsVarOrMvarTypeSpec(md, initobjTok)
                            && _curInstantiationSuffix is null or "int")
                        {
                            w.LineAligned($"(*reinterpret_cast<int32_t*>({initobjAddr})) = 0;",
                                $"IL {At(c.SourceOpName, c.SourceOffset)} -- zero-initialize VAR slot (default(T) in a generic body)");
                            break;
                        }
                        w.LineAligned($"(*reinterpret_cast<Il2CppObject**>({initobjAddr})) = nullptr;",
                            $"IL {At(c.SourceOpName, c.SourceOffset)} -- zero-initialize reference slot (generic value type)");
                        break;
                    }
                    w.LineAligned($"std::memset({initobjAddr}, 0, sizeof({TypeCppName(TypeTokenFullName(md, initobjTok))}));",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- zero-initialize value type at address");
                    break;
                }
                // ILOP-2 P8: cpblk/initblk -- memory block copy / fill.
                // cpblk: memcpy(dest, src, count); initblk: memset(dest, value, count).
                if (c.Function == "cpblk")
                {
                    w.LineAligned($"std::memcpy({EmitExpr(c.Args[0], md, types)}, {EmitExpr(c.Args[1], md, types)}, static_cast<size_t>({EmitExpr(c.Args[2], md, types)}));",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- copy memory block (cpblk)");
                    break;
                }
                if (c.Function == "initblk")
                {
                    w.LineAligned($"std::memset({EmitExpr(c.Args[0], md, types)}, static_cast<int>({EmitExpr(c.Args[1], md, types)}), static_cast<size_t>({EmitExpr(c.Args[2], md, types)}));",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- fill memory block (initblk)");
                    break;
                }
                // ILOP-2 P11: jmp_{token} -- tail jump: transfer control to the
                // target method, returning its value as this method's value
                // (ECMA jmp is EXACTLY a tail call; self-recursion already
                // folds to the TailCall loop, any other target renders here).
                if (c.Function.StartsWith("jmp_", StringComparison.Ordinal)
                    && int.TryParse(c.Function["jmp_".Length..], out var jmpTok))
                {
                    var jmpResolved = MethodRefResolver.Resolve(md, jmpTok, types);
                    var jmpArgs = string.Join(", ", c.Args.Select(a => EmitExpr(a, md, types)));
                    w.LineAligned($"return {jmpResolved.CppName}({jmpArgs});",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- tail jump to {jmpResolved.CppName}");
                    break;
                }
                // M5 wiring: the fold-time resolution (c.Resolved) carries the
                // icall arg-cast codes; user-method calls resolve by token.
                // "fcap" collapses the call to a field read (no function call).
                var resolvedStmt = c.Resolved ?? ResolveByToken(c.Function, md, types);
                var callArgsStmt = resolvedStmt != null
                    ? EmitIcallCallArgs(resolvedStmt, c.Args, md, types)
                    : string.Join(", ", c.Args.Select(a => EmitExpr(a, md, types)));
                var fnStmt = resolvedStmt?.CppName ?? EmitFunctionName(c.Function, md, types);
                // M7 CG-FIX (boxdeep2 cmake FAIL): Object::ToString on a boxed
                // uint / Type receiver -- MethodMap.Object.cs's ToString
                // receiver-type list (read-only) omits "System.UInt32" and
                // "System.Type", so the resolver falls to the mangled
                // generated-name path (System_Object_ToString_<hash>, an
                // undefined symbol). The runtime icall_object_to_string
                // (valuesemantics.cpp) handles EVERY receiver: strings return
                // identity, boxed UInt32 prints the full 32-bit pattern,
                // a GetType() result IS the type-name string (identity ->
                // "System.UInt32"). Route the mangled fallback there.
                if (resolvedStmt?.CppName.StartsWith("System_Object_ToString_", StringComparison.Ordinal) == true
                    && c.Args.Count > 0)
                {
                    w.LineAligned($"static_cast<Il2CppObject*>(il2cpp::runtime::icall_object_to_string({EmitExpr(c.Args[0], md, types)}));",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- Object::ToString via the runtime dispatch (boxed value / Type)");
                    break;
                }
                // M7 CG-FIX (excext cmake FAIL): non-generic BCL delegate ctor
                // (System.Action -- StackFold.IsDelegateType matches generic
                // `System.Action\`` but not the parameterless non-generic
                // `System.Action`, so its newobj+ctor fell to the generic alloc
                // path -> System_Action__ctor_<hash>, an undefined symbol). The
                // delegate ctor takes (this=the discarded placeholder, target,
                // native-int method pointer); rewrite to the runtime
                // construction `il2cpp::il2cpp_delegate_new(&method, target)`
                // assigned into the receiver slot.
                // CG8 (findidx D-findidx-2): the same gap for generic BCL
                // delegates NOT listed by StackFold.IsDelegateType (read-only
                // Ir/StackFold.cs matches Func`/Action`/ThreadStart/WaitCallback
                // but NOT `System.Predicate\``) -- `newobj System.Predicate<int>`
                // fell to the generic alloc path -> the mangled
                // System_Predicate_1_int__ctor_<hash> symbol. The predicate ctor
                // is structurally identical (this, target, method pointer), so
                // the same interception applies.
                // cg8-b23 (icomparedeep): the same gap for `System.Comparison\``
                // -- the delegate form of Array.Sort<T>(T[], Comparison<T>)
                // (D-ICMP-3). The Comparison<T> ctor is structurally identical
                // to Predicate/Converter (this, target, method pointer), and the
                // runtime delegate-sort entries (arraysort.h
                // il2cpp_array_sort_int32_comparison / _ref_comparison) consume
                // the SAME single-cast delegate ABI -- so the newobj placeholder
                // + ctor rewrite extend identically.
                var isBclDelegateCtor = resolvedStmt != null
                    && (resolvedStmt.CppName.StartsWith("System_Action__ctor_", StringComparison.Ordinal)
                        || (resolvedStmt.CppName.StartsWith("System_Predicate_", StringComparison.Ordinal)
                            && resolvedStmt.CppName.Contains("__ctor_", StringComparison.Ordinal))
                        // wir9-b23 (arrfuncs unlock): the Converter<TInput,
                        // TOutput> ctor used by Array.ConvertAll<int,int> --
                        // same single-cast static delegate ABI as Predicate.
                        || (resolvedStmt.CppName.StartsWith("System_Converter_", StringComparison.Ordinal)
                            && resolvedStmt.CppName.Contains("__ctor_", StringComparison.Ordinal))
                        // cg8-b23 (icomparedeep): the System.Comparison`1<T>
                        // ctor used by Array.Sort<T>(T[], Comparison<T>) --
                        // same single-cast static delegate ABI as Predicate.
                        || (resolvedStmt.CppName.StartsWith("System_Comparison_", StringComparison.Ordinal)
                            && resolvedStmt.CppName.Contains("__ctor_", StringComparison.Ordinal)));
                if (isBclDelegateCtor && c.Args.Count >= 3)
                {
                    var recvCode = EmitExpr(c.Args[0], md, types);
                    var targetCode = EmitExpr(c.Args[1], md, types);
                    var methodPtr = EmitExpr(c.Args[2], md, types);
                    // BUG-2 (batch 177b): the type ID for THIS path is "0"
                    // (untracked) -- the delegate type is not readily derivable
                    // from the mangled ctor name (System.Action / Predicate`1 /
                    // Converter`2 / Comparison`1), and these forms are not
                    // combined in practice; the runtime conservatively skips
                    // the Combine type check for them.
                    // Delegate->object uses reinterpret_cast: Il2CppDelegate embeds
                    // Il2CppObject as its first member (composition, never
                    // inheritance -- same rule as Il2CppArray/String); static_cast
                    // is ill-formed here.
                    w.LineAligned($"{recvCode} = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_delegate_new({methodPtr}, {targetCode}, 0));",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- construct BCL delegate (il2cpp_delegate_new)");
                    break;
                }
                if (resolvedStmt is { ResultIsReceiver: true })
                {
                    // M5 wiring: BCL value-type ctor via ldloca + call
                    // (DateTime/TimeSpan): the runtime icall RETURNS the
                    // initialized value; the receiver is the ldloca ADDRESS
                    // of the value-type local -- store the result through it.
                    // (The newobj shape goes through the vctor_ path instead.)
                    // xmlcase (FIX-7): XmlDocument.LoadXml uses the same
                    // ResultIsReceiver shape, but its NON-receiver args carry
                    // cast codes ("s" -- the XPath string must reach the icall
                    // as Il2CppString*, not the IR's Il2CppObject*). The raw
                    // EmitExpr path below dropped those casts. EmitIcallCallArgs
                    // applies the codes to args[1..]; the receiver arg (arg0,
                    // the result slot) is deliberately excluded (the icall
                    // RETURNS the value rather than taking the slot).
                    // The receiver itself is either an ldloca ADDRESS (a
                    // value-type slot: (*(&loc)) = value) or a plain reference
                    // local (handle-gap XmlDocument: loc = value -- no deref;
                    // the old unconditional deref broke the nulled handle).
                    var recvCode = EmitExpr(c.Args[0], md, types);
                    var isAddr = c.Args[0] is LocAddrExpr or StaticFieldAddrExpr
                        || recvCode.StartsWith("(&", StringComparison.Ordinal);
                    var recvTarget = isAddr ? $"(*({recvCode}))" : recvCode;
                    var ctorArgs2 = new List<string>();
                    for (var ai = 1; ai < c.Args.Count; ai++)
                    {
                        var ptnCast = resolvedStmt.ParamTypeNames is { Length: > 0 } ptn2 && ai < ptn2.Length ? ptn2[ai] : null;
                        // drop-cast fix (icallwide): a "drop" slot must skip the
                        // argument ENTIRELY (the DateTime ctor(y,m,d,h,mi,s,kind)
                        // kind) -- the old walk only skipped the CAST and still
                        // added the argument ("expected 6, have 7").
                        if (ptnCast == "drop") continue;
                        var acode = EmitExpr(c.Args[ai], md, types);
                        if (ptnCast is { Length: > 0 } pcast)
                        {
                            acode = EmitIcallCallArgs(new ResolvedMethod(resolvedStmt.CppName, resolvedStmt.ParamCount,
                                    resolvedStmt.IsConsoleWriteLine, resolvedStmt.ReturnsValue, resolvedStmt.ReturnsRef,
                                    resolvedStmt.ParamIsRef, resolvedStmt.IsInstance, resolvedStmt.IsPInvoke,
                                    new[] { pcast }, resolvedStmt.SetLastError, resolvedStmt.ResultKind,
                                    resolvedStmt.PrependCountArg, resolvedStmt.PrependZeroArg,
                                    resolvedStmt.AppendZeroArg, resolvedStmt.ReturnTypeName, resolvedStmt.ResultIsReceiver),
                                new List<Expr> { c.Args[ai] }, md, types)!;
                        }
                        ctorArgs2.Add(acode);
                    }
                    // xmlcase (FIX-7): the icall result lands in a reference
                    // local (Il2CppObject* handle-gap XmlDocument) -- the
                    // runtime LoadXml returns il2cpp::XmlNode*, an unrelated
                    // pointer type, so the handle needs the exact reinterpret
                    // (the reference-typed call-result contract elsewhere).
                    // FIX XML-RC (xmlreader2/xmlcase/xmldeep2/xpathdeep cmake
                    // FAIL): the pre-fix literal here was static_cast<...>,
                    // which is ILL-FORMED for an unrelated pointer return
                    // (XmlNode* -> Il2CppObject*); this ResultIsReceiver
                    // reference-local arm is the general handle-gap bridge,
                    // so the cast must be reinterpret_cast (the same rule the
                    // comment above already states).
                    var callResult = c.Args[0] is LocExpr && recvTarget != null
                        ? $"reinterpret_cast<Il2CppObject*>({fnStmt}({string.Join(", ", ctorArgs2)}))"
                        : $"{fnStmt}({string.Join(", ", ctorArgs2)})";
                    w.LineAligned($"{recvTarget} = {callResult};",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- initialize value-type instance (ldloca ctor)");
                    break;
                }
                if (resolvedStmt?.ParamTypeNames is { Length: > 0 } fc && fc[0] == "fcap")
                {
                    w.LineAligned($"{callArgsStmt};",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- read StringBuilder.Capacity (runtime struct field)");
                }
                // M7 CG-FIX (encodingcase cmake FAIL): `new string(...)` is a
                // CREATE-STYLE ctor -- the runtime discards `this_` (the
                // nullptr newobj placeholder) and RETURNS the new string. The
                // fold splits newobj into alloc + ctor CallStmt and pushes the
                // alloc temp; the ctor's RETURN must land back in that slot or
                // the pushed temp stays nullptr (empty-string runtime path).
                // Emit `recv = (Il2CppObject*)fn(recv, args...)` -- the
                // receiver slot becomes the created string.
                else if (resolvedStmt is { ReturnsRef: true }
                         && resolvedStmt.CppName.StartsWith("il2cpp::runtime::icall_string_ctor", StringComparison.Ordinal)
                         && c.Args.Count > 0)
                {
                    var recvCode = EmitExpr(c.Args[0], md, types);
                    w.LineAligned($"{recvCode} = static_cast<Il2CppObject*>({fnStmt}({callArgsStmt}));",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- create-style string ctor (return replaces the placeholder)");
                }
                else if (IsMessageCarrierCppName(fnStmt))
                {
                    // M7 CG-FIX (excext): the resolver's get_Message CppName
                    // is missing the closing paren (see IsMessageCarrierCppName);
                    // emit the balanced nested call directly. The receiver is
                    // dropped (the "drop" cast), so the call takes NO args.
                    w.LineAligned($"{fnStmt});",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- read the exception message carrier (get_Message)");
                }
                else if (resolvedStmt?.ResultKind == "ip4")
                {
                    // M7 CG-FIX (netcase): IPAddress.Parse(s) in STATEMENT
                    // position (result discarded -- `IPAddress.Parse(s);` in
                    // IsFormatException). The runtime ipaddr.h signature is
                    // il2cpp_ipaddress_parse(Il2CppString*, uint8_t out4[4]) --
                    // it WRITES the four bytes through an output buffer, so a
                    // 1-arg call fails to compile ("too few arguments"). Allocate
                    // a throwaway uint8_t[4] slot in a scope block for the
                    // discarded result. (The net48 FormatException on invalid
                    // input is a DOCUMENTED runtime divergence: the parse layer
                    // returns 0 and never throws -- the case's IsFormatException
                    // checks can only be satisfied by the icall layer.)
                    w.LineAligned($"{{ uint8_t __ip4[4]; {fnStmt}({callArgsStmt}, __ip4); }}",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- IPAddress.Parse (statement position, result discarded)");
                }
                else if (resolvedStmt?.CppName == "il2cpp::runtime::icall_exception_ctor_message"
                    && c.Args.Count > 1)
                {
                    // ilistException: System.Exception::.ctor(string) double
                    // emission -- the base ctor nop initializes the receiver
                    // FIRST (EHBaseCtorTests pins nop(arg0)), then the message
                    // capture into the runtime side table. A single combined
                    // call would swallow the nop.
                    var recvCode = EmitExpr(c.Args[0], md, types);
                    w.LineAligned($"il2cpp::runtime::il2cpp_object_ctor_nop({recvCode});",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- initialize exception instance (base ctor nop)");
                    w.LineAligned($"{fnStmt}({callArgsStmt});",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- capture the exception message into the side table)");
                }
                else
                {
                    w.LineAligned($"{fnStmt}({callArgsStmt});",
                        $"IL {At(c.SourceOpName, c.SourceOffset)} -- invoke {DescribeCall(fnStmt)}");
                }
                break;
            default:
                throw new NotSupportedException($"IR statement {s.GetType().Name} is not supported by M1 emission");
        }
    }

    private static string EmitExpr(Expr e, MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        switch (e)
        {
            case IntConstExpr i:
                return i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "LL";
            case Int64ConstExpr l:
                // G4 (bitopcase): long.MinValue cannot be written as a single
                // C++ literal -- `-9223372036854775808LL` is parsed as ULL
                // (the magnitude exceeds LLONG_MAX), so unary minus wraps the
                // unsigned value and the constant becomes +2^63 instead of
                // -2^63. Emit the well-formed two-literal form (equal to
                // LLONG_MIN on every target).
                return l.Value == long.MinValue
                    ? "(-9223372036854775807LL - 1LL)"
                    : l.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "LL";
            case DoubleConstExpr d2:
                // M5 wiring: double literal emission. NaN must preserve the
                // EXACT .NET bit pattern: the C `NAN` macro is a float QNaN
                // (0x7FC00000 -> widens to 0x7FF8000000000000) while .NET's
                // canonical double.NaN is 0xFFF8000000000000 (sign bit set),
                // and ldc.r4 NaN payloads widen differently still -- so NaN
                // emits il2cpp_double_from_bits(<raw int64 bits>) (defined in
                // generated.h), which round-trips the IL constant bit-exactly.
                // ±Infinity have no payload, so the <cmath> macros are faithful
                // (the sign is preserved by the unary minus).
                // Integral values get an explicit ".0" so C++ keeps double
                // semantics (and "-0" preserves the negative-zero sign).
                if (double.IsNaN(d2.Value)) return $"il2cpp_double_from_bits(static_cast<int64_t>({BitConverter.DoubleToInt64Bits(d2.Value)}LL))";
                if (double.IsPositiveInfinity(d2.Value)) return "INFINITY";
                if (double.IsNegativeInfinity(d2.Value)) return "-INFINITY";
                var txt = d2.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                if (!txt.Contains('.') && !txt.Contains('e') && !txt.Contains('E'))
                    txt += ".0";
                return txt;
            case StringConstExpr s:
                // T3 string pool (design §2.3): ldstr -> single load from the
                // static table g_strPool[id] (built by Generate's in-loop
                // fold->collect pass). Table entries ARE intern-pool instances
                // (fill uses intern_literal), so .NET interned identity
                // semantics are preserved and no cast is needed (g_strPool is
                // Il2CppObject* -- the IR's uniform reference shape).
                return $"il2cpp::g_strPool[{_pool!.IdOf(s.Value)}]";
            case NullExpr:
                return "nullptr";
            case MarshalUtf8Expr m:
                // wf2 (winforms1): a NULL managed string marshals as a NULL
                // native pointer (.NET DllImport semantics) -- GetModuleHandleW
                // (null) must receive nullptr (an empty buffer returns 0). The
                // runtime utf16/utf8 allocs turn null into an EMPTY buffer, so
                // the emitter bypasses them for a statically-null argument. The
                // temp is still declared char16_t*/char* (cstr16/cstr kind) and
                // the paired free is a null-safe no-op.
                if (m.Arg is NullExpr) return "nullptr";
                // Audit H5: marshal a managed string into its own heap buffer.
                // wf2 (winforms1): CharSet.Unicode P/Invokes use the UTF-16
                // alloc (the native W-API reads char16_t*).
                return m.Utf16
                    ? $"il2cpp::il2cpp_pinvoke_string_to_utf16_alloc({NullSafePtrCast("Il2CppString*", EmitExpr(m.Arg, md, types))})"
                    : $"il2cpp::il2cpp_pinvoke_string_to_utf8_alloc({NullSafePtrCast("Il2CppString*", EmitExpr(m.Arg, md, types))})";
            case LocExpr l:
                return l.Name;
            case LocAddrExpr la:
                // ldloca: address of a local (value-type this / byref).
                return $"(&{la.Name})";
            case StaticFieldExpr f:
                // System.Guid.Empty: no TypeDef storage exists -- the read is
                // the shared all-zero 16-byte slot (the Guid value form).
                if (f.FieldName == "System.Guid.Empty")
                {
                    _guidEmptyNeeded = true;
                    return "il2cpp_guid_empty_bytes";
                }
                return FieldStorageName(f.FieldName);
            case StaticFieldAddrExpr sa:
                // ldsflda (ECMA 0x7F): address of a static field -- &<storage>.
                // The deref:* receiver cast then loads the field VALUE.
                // System.Guid.Empty: no TypeDef storage -- the shared all-zero
                // 16-byte slot (the array name decays to the uint8_t* the
                // "g"-cast receivers expect).
                if (sa.FieldName == "System.Guid.Empty")
                {
                    _guidEmptyNeeded = true;
                    return "il2cpp_guid_empty_bytes";
                }
                return $"(&{FieldStorageName(sa.FieldName)})";
            case BinaryExpr b:
                // M5 wiring: double arithmetic (either side a double literal) --
                // both operands are promoted so C++ resolves the double
                // operator (the IR carries no type on BinaryExpr). Must run
                // BEFORE TryFoldInt32 (double constants never fold there) and
                // before the checked_div/Rem paths (those are int32-only).
                // M6 (numchain): a double LOCAL divided by an int (`da / b`)
                // must stay a double division too -- ExprIsDouble covers
                // double-typed locals/call results, not just literals.
                if (ExprIsDouble(b.Left) || ExprIsDouble(b.Right))
                {
                    // D-DBL-1: double/float `%` (IL rem on R8) has NO C++
                    // operator -- `%` is integer-only, so double % double is
                    // a compile error (the numchain runtime-operand case
                    // never constant-folds: only BOTH-constant doubles fold
                    // at the C# compile time). Route to the runtime fmod
                    // icall (std::fmod == the CLR double-% contract,
                    // net48-verified: sign follows the dividend, x%0 -> NaN,
                    // inf%y -> NaN, x%inf -> x). Runs before TryFoldInt32
                    // and the checked_div/Rem paths (those are int32-only).
                    if (b.Op == BinaryOp.Rem)
                        return $"il2cpp::runtime::icall_math_fmod_d(static_cast<double>({EmitExpr(b.Left, md, types)}), static_cast<double>({EmitExpr(b.Right, md, types)}))";
                    var dsym = b.Op switch
                    {
                        BinaryOp.Ceq => "==", BinaryOp.Cgt => ">", BinaryOp.Clt => "<",
                        BinaryOp.CgtUn => ">", BinaryOp.CltUn => "<",
                        _ => OpSymbol(b.Op),
                    };
                    return $"(static_cast<double>({EmitExpr(b.Left, md, types)}) {dsym} static_cast<double>({EmitExpr(b.Right, md, types)}))";
                }
                // G5 (bitopcase): an int64 constant subtree folds with
                // unchecked wrap. Emitting the two-literal form for the
                // folded long.MinValue (the Int64ConstExpr convention) keeps
                // the constant well-formed.
                if ((ExprIsInt64(b.Left) || ExprIsInt64(b.Right))
                    && TryFoldInt64(b, out var folded64))
                {
                    return folded64 == long.MinValue
                        ? "(-9223372036854775807LL - 1LL)"
                        : folded64.ToString(System.Globalization.CultureInfo.InvariantCulture) + "LL";
                }
                if (TryFoldInt32(b, out var folded))
                    return folded.ToString(System.Globalization.CultureInfo.InvariantCulture) + "LL";
                // M3 Task 6: checked division -- .NET's integer division by
                // zero throws DivideByZeroException (C++ would crash). The
                // runtime helper checks and throws; the expression is
                // substituted for the raw '/'.
                // P1 (hotpath-audit-2): a CONSTANT divisor's checked branches
                // (b == 0 / INT_MIN / -1) are statically dead for safe
                // divisors -- emit a plain C++ division so the optimizer can
                // strength-reduce it (mul-shift) instead of a call + 2 dead
                // branches. Dangerous divisors keep the checked helper.
                if (b.Op == BinaryOp.Div)
                {
                    var cdiv = ConstantValue(b.Right);
                    // int64 operands must NOT truncate to int32 (bitopcase
                    // G5): the div width follows the operand width.
                    var is64 = ExprIsInt64(b.Left) || ExprIsInt64(b.Right);
                    if (cdiv is { } dv && dv != 0 && dv != -1 && dv != int.MinValue && dv != long.MinValue)
                    {
                        var dl = EmitExpr(b.Left, md, types);
                        var dr = EmitExpr(b.Right, md, types);
                        return is64
                            ? $"(({dl}) / ({dr}))"
                            : $"(static_cast<int32_t>({dl}) / static_cast<int32_t>({dr}))";
                    }
                    return is64
                        ? $"(({EmitExpr(b.Left, md, types)}) / ({EmitExpr(b.Right, md, types)}))"
                        : $"il2cpp::il2cpp_checked_div({EmitExpr(b.Left, md, types)}, {EmitExpr(b.Right, md, types)})";
                }
                if (b.Op == BinaryOp.Rem)
                    return (ExprIsInt64(b.Left) || ExprIsInt64(b.Right))
                        ? $"(({EmitExpr(b.Left, md, types)}) % ({EmitExpr(b.Right, md, types)}))"
                        : $"il2cpp::il2cpp_checked_rem({EmitExpr(b.Left, md, types)}, {EmitExpr(b.Right, md, types)})";
                // null reference comparison (Task 9): C# `x == null` / `x != null` (Ceq/Cgt_un on
                // references) -> emit the equivalent pointer-vs-nullptr comparison. NullExpr is always a reference type.
                //   ceq(ref, null)      -> (ref == nullptr)      [x == null]
                //   cgt.un(ref, null)   -> (ref != nullptr)      [x != null, C# compiled form]
                if (b.Right is NullExpr)
                {
                    return b.Op switch
                    {
                        BinaryOp.Ceq => $"({EmitExpr(b.Left, md, types)} == nullptr)",
                        BinaryOp.Cgt or BinaryOp.CgtUn => $"({EmitExpr(b.Left, md, types)} != nullptr)",
                        BinaryOp.Clt or BinaryOp.CltUn => $"({EmitExpr(b.Left, md, types)} == nullptr)",
                        _ => throw new NotSupportedException($"null comparison op {b.Op} not supported in M2"),
                    };
                }
                if (b.Left is NullExpr)
                {
                    return b.Op switch
                    {
                        BinaryOp.Ceq => $"({EmitExpr(b.Right, md, types)} == nullptr)",
                        BinaryOp.Cgt or BinaryOp.CgtUn => $"({EmitExpr(b.Right, md, types)} != nullptr)",
                        BinaryOp.Clt or BinaryOp.CltUn => $"({EmitExpr(b.Right, md, types)} == nullptr)",
                        _ => throw new NotSupportedException($"null comparison op {b.Op} not supported in M2"),
                    };
                }
                // Audit H1: Cgt_un/Clt_un compare as UNSIGNED. uint values are
                // stored in int32_t slots; signed '>'/'<' would make every
                // high-bit-set uint comparison wrong. Constant operands are
                // folded above with (uint) semantics; the non-constant path
                // must cast both sides to uint32_t before comparing. For
                // int64/ulong operands the cast must be uint64_t (bitopcase
                // G3: a uint32_t cast truncated 2^32 and gave the wrong side).
                if (b.Op is BinaryOp.CgtUn or BinaryOp.CltUn)
                {
                    var sym = b.Op == BinaryOp.CgtUn ? ">" : "<";
                    var is64 = ExprIsInt64(b.Left) || ExprIsInt64(b.Right);
                    return is64
                        ? $"(static_cast<uint64_t>({EmitExpr(b.Left, md, types)}) {sym} static_cast<uint64_t>({EmitExpr(b.Right, md, types)}))"
                        : $"(static_cast<uint32_t>({EmitExpr(b.Left, md, types)}) {sym} static_cast<uint32_t>({EmitExpr(b.Right, md, types)}))";
                }
                // shr.un (logical right shift): `uint x >> n` -- the shift is
                // LOGICAL regardless of the operand's signedness (.NET shr.un
                // semantics: zero-fill). Casting through uint32_t guarantees
                // the logical shift even for negative int32 values; the result
                // cast back to int32_t keeps the value int32-typed for
                // downstream signed operations (bit pattern preserved). For
                // int64/ulong operands the full uint64_t width must be kept
                // and the count masked to 6 bits (bitopcase G2).
                if (b.Op == BinaryOp.ShrUn)
                    return ExprIsInt64(b.Left)
                        ? $"(static_cast<int64_t>(static_cast<uint64_t>({EmitExpr(b.Left, md, types)}) >> ({EmitExpr(b.Right, md, types)} & 63)))"
                        : $"(static_cast<int32_t>(static_cast<uint32_t>({EmitExpr(b.Left, md, types)}) >> ({EmitExpr(b.Right, md, types)} & 31)))";
                // Shl/Shr count masking: .NET masks the shift count to 5 bits
                // for int32 operands, 6 bits for int64/ulong (ECMA-335
                // III.3.57/58); the constant-fold path already masks
                // (TryFoldInt32), the runtime path must too -- an unmasked
                // count >= 32 is UB in C++ and diverges from .NET (bitopcase
                // G1: 1LL<<33 became 1LL<<1 with the 5-bit mask).
                if (b.Op is BinaryOp.Shl or BinaryOp.Shr)
                {
                    var sym = b.Op == BinaryOp.Shl ? "<<" : ">>";
                    var mask = ExprIsInt64(b.Left) ? 63 : 31;
                    return $"({EmitExpr(b.Left, md, types)} {sym} ({EmitExpr(b.Right, md, types)} & {mask}))";
                }
                // D-I64D-1: UNCHECKED int64/ulong multiplication (IL mul, the
                // C# unchecked context) must WRAP mod 2^64 -- a raw C++ `a * b`
                // on int64_t operands is signed-overflow UB when the product
                // exceeds int64 range (clang -O2 folds INT64_MIN * 2
                // differently than the two's-complement wrap; int64deep
                // o4_min_mul2). Route through the runtime wrap helper
                // (conv.cpp il2cpp_unchecked_mul_64, unsigned intermediate --
                // defined modulo 2^64, then the two's-complement cast on every
                // targeted platform). The checked antonym already folds to
                // il2cpp_checked_mul_64 (StackFold mul.ovf, exception.cpp).
                // Runs AFTER the double branch (double mul stays IEEE-754 `*`).
                if (b.Op == BinaryOp.Mul && (ExprIsInt64(b.Left) || ExprIsInt64(b.Right)))
                    return $"il2cpp::il2cpp_unchecked_mul_64({EmitExpr(b.Left, md, types)}, {EmitExpr(b.Right, md, types)})";
                // P26 (bcldeep): a NEGATIVE integer literal compared to a
                // numeric call/temp -- e.g. `(uint)x == uint.MaxValue`, where
                // Roslyn lowers uint.MaxValue to ldc.i4 0xFFFFFFFF carried as
                // the IntConstExpr -1 and printed "-1LL". The C++ result
                // `uint32_t == int64_t` promotes to int64 (4294967295 == -1)
                // and is FALSE, while .NET compares the 32-bit bit patterns.
                // A negative INTEGER literal operand can never co-exist with a
                // reference (object == -1 does not compile) or a double
                // (doubles leave via the branch above), so EQUALITY/
                // INEQUALITY against it is provably integer-only: compare
                // through the unsigned cast (int32 -> uint32_t, int64 ->
                // uint64_t -- bit-pattern equality, the C# ldc.i4 semantics).
                if (b.Op is BinaryOp.Ceq
                    && IsNegativeIntConst(b.Left) != IsNegativeIntConst(b.Right))
                {
                    var lit = IsNegativeIntConst(b.Left) ? b.Left : b.Right;
                    var val = IsNegativeIntConst(b.Left) ? b.Right : b.Left;
                    var sym = "==";
                    var wide = ExprIsInt64(val)
                        || (lit is IntConstExpr { IsInt64: true });
                    return wide
                        ? $"(static_cast<uint64_t>({EmitExpr(val, md, types)}) {sym} static_cast<uint64_t>({EmitExpr(lit, md, types)}))"
                        : $"(static_cast<uint32_t>({EmitExpr(val, md, types)}) {sym} static_cast<uint32_t>({EmitExpr(lit, md, types)}))";
                }
                // cannot-init Top1 fix: cmp/gt/lt/ge/le between Il2CppObject* and int32.
                // The fallback path above unconditionally concatenates left op right,
                // which yields ill-formed C++ when one operand is a pointer and the
                // other is an integer (e.g. `obj == count` -> `Il2CppObject* == int32_t`).
                // Cast both sides to intptr_t so the comparison is integer-typed.
                if (b.Op is BinaryOp.Ceq or BinaryOp.Cgt or BinaryOp.Clt or BinaryOp.CgtUn or BinaryOp.CltUn)
                {
                    // FIX PTR-CMP (eventdeep cmake FAIL): a C++ POINTER operand
                    // cannot be converted to intptr_t by static_cast -- that is
                    // ill-formed between unrelated types ("static_cast from
                    // 'Il2CppObject *' to 'intptr_t' is not allowed"); the
                    // pointer -> integer bridge must be reinterpret_cast. The
                    // INTEGER operands (including the intptr_t CARRIERS --
                    // ResultKind "ptr" call results and System.IntPtr
                    // ValueTypeNewExpr both emit intptr_t, NOT a C++ pointer)
                    // keep static_cast. The old blanket static_cast worked only
                    // for the integer operands and broke on every real pointer.
                    bool IsPtrExpr(Expr e) => e switch
                    {
                        Ir.LocExpr le => _curLocKinds.TryGetValue(le.Name, out var k)
                            && (k.Contains("*", StringComparison.Ordinal) || k.Contains("Il2CppObject", StringComparison.Ordinal)),
                        // A static field's reference-ness is decoded from the
                        // metadata signature (the ExprKind heuristic maps every
                        // non-Decimal/DateTime static field to "ref"); the
                        // storage is int32_t unless the signature is a
                        // reference type (StaticFieldExprIsReference).
                        Ir.StaticFieldExpr sf => StaticFieldExprIsReference(md, types, sf.FieldName),
                        // Resolver "ref" = a C++ pointer expression. "ptr" is
                        // the intptr_t carrier's kind -- an integer.
                        _ => TypeKindResolver.ResolveExprKind(e, _curLocKinds) is "ref"
                    };
                    bool IsIntExpr(Expr e) => e switch
                    {
                        Ir.LocExpr le => _curLocKinds.TryGetValue(le.Name, out var k)
                            && (k == "val" || k == "i" || k == "int32_t" || k.Contains("int32", StringComparison.Ordinal)),
                        Ir.IntConstExpr => true,
                        _ => TypeKindResolver.ResolveExprKind(e, _curLocKinds) is "val" or "i"
                    };
                    // A cast from the nullptr literal to an integer type is
                    // ill-formed in C++ (NullSafePtrCast precedent); the
                    // zero-test shape (brfalse/brtrue folding emits
                    // Ceq(expr, 0)) renders as a direct null comparison.
                    static bool IsZeroConst(Expr e) => e switch
                    {
                        Ir.IntConstExpr i => i.Value == 0,
                        Ir.Int64ConstExpr l => l.Value == 0,
                        _ => false,
                    };
                    string PtrAsIntPtr(Expr e)
                    {
                        var code = EmitExpr(e, md, types);
                        return code == "nullptr" ? "0" : $"reinterpret_cast<intptr_t>({code})";
                    }
                    var leftIsPtr = IsPtrExpr(b.Left);
                    var rightIsPtr = IsPtrExpr(b.Right);
                    var leftIsInt = IsIntExpr(b.Left);
                    var rightIsInt = IsIntExpr(b.Right);
                    if (b.Op == BinaryOp.Ceq)
                    {
                        if (leftIsPtr && IsZeroConst(b.Right))
                            return $"({EmitExpr(b.Left, md, types)} == nullptr)";
                        if (rightIsPtr && IsZeroConst(b.Left))
                            return $"({EmitExpr(b.Right, md, types)} == nullptr)";
                    }
                    if (leftIsPtr && rightIsInt)
                        return $"({PtrAsIntPtr(b.Left)} {OpSymbol(b.Op)} static_cast<intptr_t>({EmitExpr(b.Right, md, types)}))";
                    if (rightIsPtr && leftIsInt)
                        return $"(static_cast<intptr_t>({EmitExpr(b.Left, md, types)}) {OpSymbol(b.Op)} {PtrAsIntPtr(b.Right)})";
                }
                if (b.Op is BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul
                    && (ExprIsInt64(b.Left) || ExprIsInt64(b.Right)))
                {
                    // net48 int64 arithmetic wraps (unchecked); C++ signed
                    // overflow is UB. Compute in uint64 and cast back.
                    var sym = b.Op switch
                    {
                        BinaryOp.Add => "+",
                        BinaryOp.Sub => "-",
                        _ => "*",
                    };
                    return $"(static_cast<int64_t>(static_cast<uint64_t>({EmitExpr(b.Left, md, types)}) {sym} static_cast<uint64_t>({EmitExpr(b.Right, md, types)})))";
                }
                return $"({EmitExpr(b.Left, md, types)} {OpSymbol(b.Op)} {EmitExpr(b.Right, md, types)})";
            case UnaryExpr u:
                if (TryFoldInt32(u, out var uFolded))
                    return uFolded.ToString(System.Globalization.CultureInfo.InvariantCulture) + "LL";
                return $"({(u.Op == UnaryOp.Neg ? "-" : "~")}{EmitExpr(u.Operand, md, types)})";
            case CallExpr c:
                return EmitTask9Call(c, md, types);
            case VirtualCallExpr v:
                return EmitVirtualCall(v, md, types);
            case FieldExpr f:
                // Instance field read: null-check the receiver, then dereference by the concrete struct type.
                // A (Il2CppObject*) expression may come from an object/base reference; cast to the declared concrete type here.
                // Reference field values are cast back to Il2CppObject* uniformly (the IR's uniform reference
                // representation -- temps/parameters/returns are all Il2CppObject*; cross-type field read/write aligns via casts).
                // M3 Task 11: value-type receivers are plain values ('.' access, no null check).
                var fval = $"{FieldAccess(f.Object, f.OwnerFullName, f.FieldCppName, md, types, f.IsValueTypeObject)}";
                return f.IsRefField ? $"reinterpret_cast<Il2CppObject*>({fval})" : fval;
            case FieldAddrExpr fa:
                // Instance field address (ldflda): &obj->field. Null-check the receiver first.
                // batch 156c (asynccase): VALUE-TYPE receivers (the async state
                // machine's builder/awaiter fields) skip the null check -- a
                // struct address cannot be static_cast to Il2CppObject*.
                return $"(&{FieldAccess(fa.Object, fa.OwnerFullName, fa.FieldCppName, md, types, fa.IsValueTypeObject)})";
            case NewObjExpr n:
                // newobj allocation expression: zero-initialized object (the ctor call was already split into its own CallStmt by StackFold).
                // M5 wiring: BCL classes allocate through their RUNTIME class
                // descriptors (il2cpp_<type>_class()) -- the transpiler emits
                // no descriptor for them.
                return BclAllocExpr(n.TypeFullName)
                    ?? $"il2cpp_object_new(&{TypeCppName(n.TypeFullName)}_class)";
            case ValueTypeNewExpr vn:
                // Value-type newobj: declare the value in place (the ctor
                // statement initializes it -- see the vctor_ emission).
                return $"{TypeCppName(vn.TypeFullName)} {{ }}";
            case ExceptionEntryExpr:
                // M5 wiring: the synthetic catch-handler entry -- the
                // exception's SHORT type name interned as a managed string
                // (e.GetType().Name). __il2cpp_ex is in scope ONLY inside
                // the generated catch block (where the entry is consumed).
                // The runtime type_name() is a chain "Type,Base1,Base2,...":
                // the short name is the last '.' segment of the FIRST
                // comma-separated entry, null-terminated (a bare pointer
                // into the chain would run to the chain's end; the pre-M5
                // strrchr('.')+1 grabbed "Exception" from the trailing base
                // entry instead). The std::string temporary lives through
                // the full expression, so c_str() is valid for the intern.
                return "il2cpp::il2cpp_string_intern_literal(" +
                    "[](const char* n) -> std::string { const char* c = std::strchr(n, ','); " +
                    "std::string seg = c != nullptr ? std::string(n, static_cast<size_t>(c - n)) : std::string(n); " +
                    "auto dot = seg.rfind('.'); " +
                    "return dot == std::string::npos ? seg : seg.substr(dot + 1); " +
                    "}(__il2cpp_ex.type_name()).c_str())";
            case FuncPtrExpr fp:
                // ldftn: the address of the target method's generated function.
                // fnptr domain b23: emitted as a void* (the model's pointer
                // slot) so a `delegate*` parameter receives it directly; the
                // calli site casts it to the target function-pointer
                // signature, the delegate path re-casts via il2cpp_delegate_new.
                // CG6: record the method as a delegate-created target so
                // entry.cpp registers its SHORT name (Delegate.Method.Name).
                _delegateMethodTokens.Add(fp.MethodToken);
                return $"reinterpret_cast<void*>(&{MethodCppNameFromToken(fp.MethodToken, md, types)})";
            case DelegateNewExpr dn:
                // delegate construction: wrap the method pointer. #42: the
                // 2-arg shape carries the bound-instance target -- loc0 for an
                // instance-method binding (the instance ABI needs `this`), an
                // explicit nullptr for a static binding (uniform shape; the
                // runtime signature is (void*, Il2CppObject* = nullptr)).
                // CG6: record the delegate-created method for the entry.cpp
                // name-registry registration.
                _delegateMethodTokens.Add(dn.MethodToken);
                // BUG-2 (batch 177b): pass the delegate-type ID (dn.TypeId --
                // FNV-1a of the full type name; "0" when the creation path
                // could not name the type). The runtime stores it on the
                // delegate and compares at Combine time (net48 ArgumentException
                // on a heterogeneous combine).
                return $"reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_delegate_new(reinterpret_cast<void*>(&{MethodCppNameFromToken(dn.MethodToken, md, types)}), {EmitExpr(dn.Target, md, types)}, {dn.TypeId}))";
            case DelegateInvokeExpr di:
                // delegate Invoke. int32-returning delegates with <=4 int32
                // args route through icall_multicast_invoke_i4: the runtime
                // flattens the composition tree and calls every leaf in order
                // (multicast semantics). Raw methodPtr dispatch below would
                // read a container's `first` field -- Il2CppMulticastDelegate
                // { obj, first, second } puts `first` at the same offset as
                // Il2CppDelegate::methodPtr -- and call a data pointer as
                // code (0xC0000005). Other signatures (ref-arg / non-int32
                // returns) keep the direct methodPtr call: single-cast only,
                // multicast of those is a documented gap (il2cpp_delegate.h).
                {
                    var receiver = di.Args.Count > 0 ? di.Args[0] : throw new NotSupportedException("delegate Invoke without receiver");
                    var args = di.Args.Skip(1).Select(a => EmitExpr(a, md, types)).ToList();
                    var receiverCode = EmitExpr(receiver, md, types);
                    var (retCpp, paramCpp) = DelegateSignature(md, di.MethodToken);
                    // BUG-1 (batch 172): the i4 route now ALSO accepts a
                    // VOID-returning delegate whose params are <=4 all-int32
                    // (Action<int>, Action<int,int>, custom void M(int), ...).
                    // Before, those decoded to retCpp "void" (only the ZERO-arg
                    // void shape was remapped to the int32 ABI by batch 168) and
                    // fell through to the raw methodPtr dispatch below -- which
                    // reads a multicast container's `first` field as code ->
                    // 0xC0000005. A void-N-int-arg delegate keeps the int32 ABI
                    // on the i4 multicast path; the void return is discarded by
                    // the caller's void context (the i4 icall's result is never
                    // consumed).
                    if ((retCpp == "int32_t" || retCpp == "void") && paramCpp.Length <= 4
                        && paramCpp.All(p => p == "int32_t"))
                    {
                        var callArgs = string.Join(", ", args);
                        if (args.Count < 4)
                            callArgs = string.Join(", ", args.Concat(Enumerable.Repeat("0", 4 - args.Count)));
                        return $"il2cpp::runtime::icall_multicast_invoke_i4(({receiverCode}){(callArgs.Length > 0 ? ", " + callArgs : "")})";
                    }
                    // BUG-1 (batch 172): the ONE-managed-ref-arg void ABI
                    // (`void Handler(string)` -- Action<string>,
                    // Action<object>, custom 1-ref void delegates). Routes
                    // through the new icall_multicast_invoke_r_void (the
                    // runtime flattens the composition tree and calls every
                    // leaf in order; the raw methodPtr path below would read a
                    // container's `first` field as code -> 0xC0000005 for
                    // multicast chains).
                    if (retCpp == "void" && paramCpp.Length == 1
                        && paramCpp[0] == "Il2CppObject*")
                    {
                        return $"il2cpp::runtime::icall_multicast_invoke_r_void(({receiverCode}), {args[0]})";
                    }
                     // batch 162 (eventcase): the EVENT-HANDLER ABI -- void
                     // return with exactly TWO managed-reference args
                     // (`void Handler(object sender, EventArgs e)` --
                     // EventHandler, System.Timers.Timer's ElapsedEventHandler,
                     // WinForms events). Routes through
                     // icall_multicast_invoke_rr_void (the runtime flattens the
                     // composition tree and calls every leaf in order; the raw
                     // methodPtr path below would read a container's `first`
                     // field as code -> 0xC0000005 for multicast chains).
                     if (retCpp == "void" && paramCpp.Length == 2
                         && paramCpp.All(p => p == "Il2CppObject*"))
                     {
                         var callArgs2 = string.Join(", ", args);
                         return $"il2cpp::runtime::icall_multicast_invoke_rr_void(({receiverCode}){(callArgs2.Length > 0 ? ", " + callArgs2 : "")})";
                     }

                     // rlaudit4: ref-returning ZERO-arg delegates (Func<string>,
                     // Func<object>, custom ref-return no-arg): multicast must
                     // flatten (raw methodPtr dispatch would read a container's
                     // `first` field as code -> 0xC0000005); net48 keeps the
                     // LAST leaf's result.
                     if (retCpp == "Il2CppObject*" && paramCpp.Length == 0)
                     {
                         return $"il2cpp::runtime::icall_multicast_invoke_r(({receiverCode}))";
                     }

                     // P-D2 #1: ANY OTHER signature routes through the
                     // per-signature MULTICAST helper (il2cpp_delegate_multicast_*,
                     // emitted file-scope into generated.h by the DelegateHelperDefs
                     // flush). This replaces the raw methodPtr dispatch below
                     // (single-cast only -- multicast read a container's `first`
                     // field as code -> 0xC0000005) AND fixes the silent int32
                     // truncation/ABI-tearing the old 2-class decode produced for
                     // int64/double/float returns & params. The helper flattens the
                     // invocation list through the exported
                     // icall_delegate_get_invocation_list and calls every leaf with
                     // the per-leaf static/instance dispatch, returning the LAST
                     // result (.NET multicast semantics -- the same per-leaf rule
                     // the 4 runtime fast-path icalls use).
                     var helperName = DelegateMulticastHelperName(retCpp, paramCpp);
                     return $"{helperName}(({receiverCode}){(args.Count > 0 ? ", " + string.Join(", ", args) : "")})";
                }
            default:
                throw new NotSupportedException($"IR expression {e.GetType().Name} is not supported by M1 emission");
        }
    }

    //--------------------------------------------------------------------------
    // ExprKind -- base kind of an expression for temp/local typing.
    //   "ref" : managed reference slot (Il2CppObject* -- includes strings,
    //           which are managed objects in the M3 model)
    //   "val" : 32-bit value slot (int32_t)
    //   null  : kind not determinable from this expression alone
    // LocExpr follows the recorded kind of its source assignment (the
    // propagation table built by EmitBody), giving `loc0 = t0` (t0 a newobj
    // result) a reference kind even though the assignment's own Value is a
    // plain local reference.
    //--------------------------------------------------------------------------
    private static string? ExprKind(Expr e, Dictionary<string, string> locKind) => e switch
    {
        StringConstExpr => "ref",
        NullExpr => "ref",
        // Address snapshots (ldloca spilled to a branch-merge temp, e.g. the
        // constrained-call receiver): a typed address (int32_t*/T**) does
        // not fit any value slot -- int32_t truncates on x64 AND rejects
        // the assignment (pointer->integer is ill-formed C++). void* holds
        // every data address; uses reinterpret back explicitly (the
        // deref:*/addr:* cast discipline).
        LocAddrExpr => "p",
        // fnptr domain b23: ldftn yields a raw function pointer (void* slot).
        FuncPtrExpr => "p",
        // M5 wiring: the catch-handler entry value is the exception's SHORT
        // type name as an interned managed string (see ExceptionEntryExpr).
        ExceptionEntryExpr => "ref",
        NewObjExpr => "ref",
        DelegateNewExpr => "ref",
        MarshalUtf8Expr m => m.Utf16 ? "cstr16" : "cstr", // const char16_t* / const char* (P/Invoke marshal buffer, audit H5)
        ValueTypeNewExpr v => TypeCppName(v.TypeFullName), // value-type temp: the C++ type name
        // M5 wiring: int64/double constants and kinds propagate to the temp/
        // local declarations (int64_t / double).
        Int64ConstExpr => "l",
        DoubleConstExpr => "d",
        BinaryExpr b when b.Kind is "l" or "d" => b.Kind,
        // M5 wiring: DateTime/TimeSpan icall results are 8-byte runtime value
        // structs -- the temp is declared with the runtime C++ type name.
        // Guid icall results are 16-byte raw value slots (uint8_t[16]; the
        // icalls WRITE through the output buffer -- the "guid" kind carries
        // the slot shape to the declaration site).
        CallExpr c when c.ResultKind is "l" or "d" or "dt" or "ts" or "guid" or "dec" or "dec2" or "ptr" or "ip4" or "dto" or "bi" or "rect" or "point" or "msg" => c.ResultKind,
        // P-D3 P0: a call whose result is a raw function pointer (fnptr
        // DllImport return) carries ResultKind "p" (void* slot) -- the same
        // carrier FuncPtrExpr/LocAddrExpr use. Without this arm the result
        // temp collapsed to int32_t and truncated the x64 address.
        CallExpr c when c.ResultKind == "p" => "p",
        // P25b (user struct box): the unbox of a user value type carries
        // "us:{CppName}" (StackFold.UnboxUserStructKind) -- strip the prefix to
        // the C++ struct type so the result temp is declared `{T} t;` and the
        // unboxed value is copied in as a real struct member-by-member.
        CallExpr c when c.ResultKind?.StartsWith("us:", StringComparison.Ordinal) == true => c.ResultKind["us:".Length..],
        CallExpr c when c.ReturnsRef => "ref",
        VirtualCallExpr v when v.ReturnsRef => "ref",
        // P2-HARDEN #3: a delegate Invoke returning long/double/float carries
        // ResultKind ("l"/"d"/"float") -- the full-width P-D2 #1 helper result
        // lands on a COMPILER temp (branch-merge join slots etc.); without
        // this arm the temp fell through to "val" and the int64_t/double/float
        // helper result was truncated into an int32_t slot.
        DelegateInvokeExpr d when d.ResultKind is "l" or "d" or "float" => d.ResultKind,
        DelegateInvokeExpr d when d.ReturnsRef => "ref",
        FieldExpr f when f.IsRefField => "ref",
        LocExpr l when locKind.TryGetValue(l.Name, out var k) => k,
        // dec3 §5 Bug1: a decimal static field load (ldsfld System.Decimal::One)
        // must declare the local as Il2CppDecimal, not int32_t -- the BCL
        // sf_Zero/One descriptors are 16-byte runtime structs, and casting
        // them to int32_t truncates (decimalcase 9 build errors).
        StaticFieldExpr f when f.FieldName.StartsWith("System.Decimal.", StringComparison.Ordinal) => "dec",
        _ => "val",
    };

    // M7 genlist: a generic method body is PURE PASS-THROUGH when the folded
    // IR only moves args/locals (no arithmetic/calls/fields on the generic
    // value) -- `T Id<T>(T x) => x` folds to `return arg0`. Such a body is
    // type-agnostic: emitting it with Il2CppObject* param/return types (the
    // string variant) is safe. A numeric generic (`Add<T>(T a, T b) => a+b`)
    // folds to `arg0 + arg1` -- NOT pass-through; its string variant would be
    // uncompilable C++ (Il2CppObject* arithmetic), so the gate rejects it.
    private static bool IsPurePassThrough(List<Stmt> stmts)
    {
        foreach (var s in stmts)
        {
            switch (s)
            {
                case RetStmt r:
                    if (r.Value is not (LocExpr or null)) return false;
                    break;
                case AssignStmt a:
                    if (a.Value is not LocExpr) return false;
                    break;
                case LabelStmt:
                    break;
                default:
                    return false; // call/field/branch/expr on the generic value
            }
        }
        return true;
    }

    // M7 genlist: decode the method definition's RAW signature to find which
    // explicit parameters are generic method parameters (MVAR, 0x1E) and
    // whether the return is an MVAR -- the string variant types exactly those
    // slots as Il2CppObject* (every other slot keeps its concrete type). paramCount
    // is the EXPLICIT parameter count (instance methods' `this` is slot arg0,
    // never a generic parameter).
    private static (bool[] ParamMvar, bool ReturnMvar) GenericMvarShape(MetadataReader md, int methodToken, int paramCount)
    {
        var handle = (MethodDefinitionHandle)MetadataTokens.Handle(methodToken);
        var sig = md.GetBlobReader(md.GetMethodDefinition(handle).Signature);
        var hdr = sig.ReadSignatureHeader();
        if (hdr.IsGeneric) _ = sig.ReadCompressedInteger(); // GenericParameterCount
        var declaredCount = sig.ReadCompressedInteger();
        var ret = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        bool returnMvar = ret == 0x1E;
        if (ret is 0x11 or 0x12 or 0x40) _ = sig.ReadTypeHandle();
        else if (ret is 0x1D) _ = ReadSigParam(md, ref sig);
        else if (ret is 0x13 or 0x1E) _ = sig.ReadCompressedInteger(); // VAR/MVAR return index
        var paramMvar = new bool[paramCount];
        for (var i = 0; i < declaredCount && i < paramMvar.Length; i++)
            paramMvar[i] = ReadSigParam(md, ref sig) == 0x1E;
        return (paramMvar, returnMvar);
    }

    private static byte ReadSigParam(MetadataReader md, ref BlobReader sig)
    {
        var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        switch (b)
        {
            case 0x11 or 0x12 or 0x40: _ = sig.ReadTypeHandle(); break;
            case 0x1D: _ = ReadSigParam(md, ref sig); break;
            case 0x13 or 0x1E: _ = sig.ReadCompressedInteger(); break; // VAR/MVAR index
            case 0x15:
                sig.ReadByte();
                _ = sig.ReadTypeHandle();
                var n = sig.ReadCompressedInteger();
                for (var i = 0; i < n; i++) _ = ReadSigParam(md, ref sig);
                break;
        }
        return b;
    }

    // M5 wiring: TypeSpec → the generic DEFINITION's full name
    // ("System.Func`2<int,int>" -> "System.Func`2") -- used to classify
    // generic-instantiation casts (delegate instantiations are identities).
    private static string GenericTypeSpecDefinitionName(MetadataReader md, TypeSpecificationHandle handle)
    {
        var spec = md.GetTypeSpecification(handle);
        var sig = md.GetBlobReader(spec.Signature);
        var header = sig.ReadByte();
        if (header is 0x13 or 0x1E)
        {
            // VAR/MVAR TypeSpec: generic parameter -- read the param index and
            // return a placeholder (the generic definition is the param itself).
            _ = sig.ReadCompressedInteger();
            return "T";
        }
        if (header != 0x15)
            throw new NotSupportedException($"TypeSpec signature byte 0x{header:X2} not GenericInst(0x15) in M5");
        // GenericInst definition preamble: the ELEMENT_TYPE_CLASS(0x12)/VALUETYPE(0x11) marker
        // + TypeDefOrRef coded index (Roslyn TypeSpec blob verified: 15 12 <coded>...).
        // Skipping it makes ReadTypeHandle misread 0x12 as a coded index -> out-of-range/invalid-handle crash.
        sig.ReadByte();
        var defHandle = sig.ReadTypeHandle();
        return defHandle.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
            HandleKind.TypeSpecification => GenericTypeSpecDefinitionName(md, (TypeSpecificationHandle)defHandle),
            _ => throw new NotSupportedException($"generic definition kind {defHandle.Kind} in M5"),
        };
    }

    // M7 typecheckcase: SZARRAY (0x1D) TypeSpec target -> the runtime array
    // class descriptor for the isinst/castclass conformance test. The runtime
    // registers array classes under the "[<element full name>" name convention
    // (array.cpp g_int32ArrayClass "[System.Int32" / g_objectArrayClass
    // "[System.Object"); reference-element arrays (string[]/object[]/user
    // class[]) all share the single g_objectArrayClass. An element whose
    // array class is NOT registered (e.g. double[] -- only int32/object
    // arrays exist in stage 1) resolves via il2cpp_class_by_name to nullptr
    // and is_instance returns false -- .NET semantics (element types differ).
    private static string SzArrayArrayClassCpp(MetadataReader md, TypeSpecificationHandle handle)
    {
        var spec = md.GetTypeSpecification(handle);
        var sig = md.GetBlobReader(spec.Signature);
        var header = sig.ReadByte();
        if (header != 0x1D)
            throw new NotSupportedException($"TypeSpec signature byte 0x{header:X2} not SZARRAY(0x1D) in M7");
        var elementByte = sig.ReadByte();
        string elementFullName;
        switch (elementByte)
        {
            // The ECMA-335 element-type byte -> full type name. Only the
            // int32/object pair has a registered array class; every other
            // element maps to its full name so il2cpp_class_by_name answers
            // nullptr (isinst false) -- the correct conformance result.
            case 0x08: elementFullName = "System.Int32"; break; // int32 (registered)
            case 0x02: elementFullName = "System.Boolean"; break;
            case 0x03: elementFullName = "System.Char"; break;
            case 0x04: elementFullName = "System.SByte"; break;
            case 0x05: elementFullName = "System.Byte"; break;
            case 0x06: elementFullName = "System.Int16"; break;
            case 0x07: elementFullName = "System.UInt16"; break;
            case 0x09: elementFullName = "System.UInt32"; break;
            case 0x0A: elementFullName = "System.Int64"; break;
            case 0x0B: elementFullName = "System.UInt64"; break;
            case 0x0C: elementFullName = "System.Single"; break;
            case 0x0D: elementFullName = "System.Double"; break;
            case 0x0E: elementFullName = "System.String"; break; // ref -> object array class
            case 0x1C: elementFullName = "System.Object"; break; // ref -> object array class
            // CLASS/VALUETYPE element: the TypeDefOrRef coded index follows.
            case 0x12:
            case 0x11:
            {
                var elemHandle = sig.ReadTypeHandle();
                elementFullName = elemHandle.Kind switch
                {
                    HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)elemHandle),
                    HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)elemHandle),
                    HandleKind.TypeSpecification => GenericTypeSpecDefinitionName(md, (TypeSpecificationHandle)elemHandle),
                    _ => throw new NotSupportedException($"SZARRAY element kind {elemHandle.Kind} in M7"),
                };
                break;
            }
            // VAR/MVAR (0x13/0x1E): generic parameter type variables -- M6 model
            // generates only T->int32/uint32, but the runtime array class lookup
            // uses the object-array bucket (same as CLASS/VALUETYPE).
            case 0x13:
            case 0x1E:
                elementFullName = "System.Object";
                break;
            default:
                throw new NotSupportedException($"SZARRAY element type byte 0x{elementByte:X2} not supported in M7");
        }
        // Reference elements share the single object-array class ("[System.Object");
        // int32 is the only registered primitive array class. Any other element
        // yields an unregistered name -> il2cpp_class_by_name nullptr -> isinst false.
        string arrayName = elementByte is 0x0E or 0x1C or 0x12 ? "[System.Object" : $"[{elementFullName}";
        return $"il2cpp::il2cpp_class_by_name(\"{arrayName}\")";
    }
    private static string? BclAllocExpr(string typeFullName)
    {
        // WeakReference<T> (unbounded instantiations) folds onto the
        // non-generic descriptor (same object model: header + int32 handle).
        if (typeFullName.StartsWith("System.WeakReference`1", StringComparison.Ordinal))
            return "il2cpp_object_new(&System_WeakReference_class)";
        return typeFullName switch
        {
        // M5 wiring: BCL classes with a runtime descriptor -- the generated
        // code allocates through the descriptor (the C++ name is the
        // namespace-qualified runtime accessor).
        // System.Object: TypeCppName maps it to "Il2CppObject", so the NewObj
        // fallback would emit il2cpp_object_new(&Il2CppObject_class) -- an
        // undefined symbol (the runtime descriptor is System_Object_class,
        // extern in il2cpp_metadata.h). gccase/valuesem `new object()` probes
        // hit this.
        "System.Object" => "il2cpp_object_new(&System_Object_class)",
        // ilistException: `new Exception("a")` -- BCL exceptions have no
        // generated class descriptor; allocate as a plain object (the
        // message rides the runtime side table keyed by object identity,
        // not by klass, so GetType()/Message never consult the klass).
        // Without the entry the fallback emits an undeclared
        // &System_Exception_class symbol.
        "System.Exception" => "il2cpp_object_new(&System_Object_class)",
        // M7 CG-FIX (encodingcase cmake FAIL): `new string(...)` -- the string
        // ctors are CREATE-STYLE (string.cpp discards `this_` and returns the
        // new string, so the newobj placeholder is a DISCARDED allocation; the
        // ctor call emitter captures the returned string into the receiver
        // slot). The placeholder must still compile -- a plain
        // il2cpp_object_new(&Il2CppObject_class) is an undefined symbol, and a
        // real String allocation is pure waste. nullptr is the correct
        // placeholder: it never leaks and is ignored by every string ctor.
        "System.String" => "nullptr",
        "System.Text.StringBuilder" => "il2cpp_object_new(il2cpp::il2cpp_stringbuilder_class())",
        "System.Collections.Hashtable" => "il2cpp_object_new(il2cpp::il2cpp_hashtable_class())",
        // List<int> -- the int32 AOT expansion allocates through the listi4
        // runtime descriptor (MethodMap.List.cs routes the members).
        "System.Collections.Generic.List`1_int" => "il2cpp_object_new(il2cpp::il2cpp_list_i4_class())",
        // ilistException: List<Exception> (CLASS instantiation -- any
        // class-typed T folds to the "_class" suffix) allocates through the
        // listobj reference domain (MethodMap.List.cs obj arms route the
        // members); without the entry the fallback emits an undefined
        // &System_Collections_Generic_List_1_class symbol.
        // listrefcase: the same reference domain owns EVERY reference-element
        // instantiation -- STRING ("_string"), OBJECT ("_object") and SZARRAY
        // ("_array") share the Il2CppObject* slot ABI and must allocate the
        // runtime listobj descriptor (MethodMap.List.cs objReceiver).
        "System.Collections.Generic.List`1_class" or
        "System.Collections.Generic.List`1_string" or
        "System.Collections.Generic.List`1_object" or
        "System.Collections.Generic.List`1_array" or
        "System.Collections.Generic.List`1_long" => "il2cpp_object_new(il2cpp::il2cpp_list_obj_class())",
        "System.Collections.Generic.List`1_double" => "il2cpp_object_new(il2cpp::il2cpp_list_obj_class())",
        "System.Collections.Generic.List`1_float" or
        "System.Collections.Generic.List`1_bool" or
        "System.Collections.Generic.List`1_char" or
        "System.Collections.Generic.List`1_sbyte" or
        "System.Collections.Generic.List`1_byte" or
        "System.Collections.Generic.List`1_short" or
        "System.Collections.Generic.List`1_ushort" or
        "System.Collections.Generic.List`1_uint" => "il2cpp_object_new(il2cpp::il2cpp_list_obj_class())",
        // G4: a NESTED GenericInst element (List<List<int>>) is a managed
        // reference; its instance name carries the fully nested suffix
        // ("...List`1_System.Collections.Generic.List`1_int"), so the entry is
        // a prefix rule rather than an exact key.
        var nestedListAlloc when nestedListAlloc.StartsWith("System.Collections.Generic.List`1_System.Collections.Generic.List", StringComparison.Ordinal)
            => "il2cpp_object_new(il2cpp::il2cpp_list_obj_class())",
        // rtdict-b23 (DICT-GC): Dictionary<string,int> allocates through the
        // runtime GLOBAL wrapper descriptor (dictsi_obj.h) -- the same
        // contract as the listi4/random descriptors: without this entry the
        // newobj fallback would emit the SAME
        // &System_Collections_Generic_Dictionary_2_string_int_class symbol,
        // but as a runtime-owned GLOBAL it must go through the lazy accessor
        // (constraint #12 registration) rather than a generated descriptor.
        "System.Collections.Generic.Dictionary`2_string_int" => "il2cpp_object_new(il2cpp::il2cpp_dict_si_object_class())",
        // Phase-1 (dictgen): Dictionary<int,string> allocates through the
        // shared generic wrapper descriptor (dictgen.h) -- one class for
        // every future kind combination (kinds ride the icalls as trailing
        // literals, not as distinct classes).
        "System.Collections.Generic.Dictionary`2_int_string" => "il2cpp_object_new(il2cpp::il2cpp_dict_gen_object_class())",
        // G4: the string->string expansion rides the shared generic wrapper
        // descriptor (dictgen.h -- Phase-2 absorption of the hashtable arms).
        "System.Collections.Generic.Dictionary`2_string_string" => "il2cpp_object_new(il2cpp::il2cpp_dict_gen_object_class())",
        "System.Collections.Generic.Dictionary`2_string_System.Collections.Generic.List`1_int" => "il2cpp_object_new(il2cpp::il2cpp_dict_gen_object_class())",
        // T4: Dictionary<Uri,string> (CLASS-argument-NOT-LAST) rides the same
        // shared descriptor; the Uri key normalizes per call inside the
        // icall_dict_gen_*_o twins (Phase-2 absorption of the forwarders).
        "System.Collections.Generic.Dictionary`2_System.Uri_string" => "il2cpp_object_new(il2cpp::il2cpp_dict_gen_object_class())",
        // G4: HashSet<int> reuses the same hashtable descriptor (boxed-int
        // members; see hashset.h).
        "System.Collections.Generic.HashSet`1_int" => "il2cpp_object_new(il2cpp::il2cpp_hashtable_class())",
        // T2 W3: long/double/uint/object/class reuse the same hashtable descriptor.
        "System.Collections.Generic.HashSet`1_long" or
        "System.Collections.Generic.HashSet`1_double" or
        "System.Collections.Generic.HashSet`1_uint" or
        "System.Collections.Generic.HashSet`1_object" or
        "System.Collections.Generic.HashSet`1_class" => "il2cpp_object_new(il2cpp::il2cpp_hashtable_class())",
        "System.Random" => "il2cpp_object_new(il2cpp::il2cpp_random_class())",
        // batch 158 (timercase): System.Threading.Timer allocates through the
        // runtime-registered descriptor (timer.cpp -- the managed object is a
        // header + ONE raw il2cpp_timer* handle slot).
        "System.Threading.Timer" => "il2cpp_object_new(&System_Threading_Timer_class)",
        // batch 161 (timerscase): System.Timers.Timer allocates through the
        // runtime-registered descriptor (timer.cpp -- header + handle +
        // elapsed delegate + autoReset + interval slots).
        "System.Timers.Timer" => "il2cpp_object_new(&System_Timers_Timer_class)",
        // socketcase: System.Net.Sockets.Socket allocates through the
        // runtime-registered descriptor (il2cpp_netsocket.cpp -- header +
        // int32 fd at slot 1, -1 = closed).
        "System.Net.Sockets.Socket" => "il2cpp_object_new(&System_Net_Sockets_Socket_class)",
        // FIX-3 (collections2/stackq): ArrayList / Stack / Queue object domains
        // allocate through their lazy *_class() descriptor accessors
        // (arraylist.h / stackqueue.h, namespace il2cpp) -- without the
        // whitelist the fallback emitted il2cpp_object_new(&ArrayList_class),
        // an undefined symbol (TypeCppName has no runtime descriptor).
        "System.Collections.ArrayList" => "il2cpp_object_new(il2cpp::il2cpp_arraylist_class())",
        "System.Collections.Stack" => "il2cpp_object_new(il2cpp::il2cpp_stack_class())",
        "System.Collections.Queue" => "il2cpp_object_new(il2cpp::il2cpp_queue_class())",
        // G4: the generic expansions ride the same descriptors.
        "System.Collections.Generic.Stack`1_string" => "il2cpp_object_new(il2cpp::il2cpp_stack_class())",
        // T2 W1: the reference-element instantiations (object/class) reuse the
        // non-generic stack descriptor (Il2CppObject* slots, stackqueue.h).
        "System.Collections.Generic.Stack`1_object" or
        "System.Collections.Generic.Stack`1_class" => "il2cpp_object_new(il2cpp::il2cpp_stack_class())",
        // T2 W4: the typed value-element instantiations ride the same stack descriptor.
        "System.Collections.Generic.Stack`1_int" or
        "System.Collections.Generic.Stack`1_long" or
        "System.Collections.Generic.Stack`1_double" => "il2cpp_object_new(il2cpp::il2cpp_stack_class())",
        "System.Collections.Generic.Queue`1_int" => "il2cpp_object_new(il2cpp::il2cpp_queue_class())",
        // T2 W1: the reference-element instantiations (string/object/class)
        // reuse the non-generic queue descriptor (Il2CppObject* slots).
        "System.Collections.Generic.Queue`1_string" or
        "System.Collections.Generic.Queue`1_object" or
        "System.Collections.Generic.Queue`1_class" => "il2cpp_object_new(il2cpp::il2cpp_queue_class())",
        // T2 W4: the typed value-element instantiations ride the same queue descriptor.
        "System.Collections.Generic.Queue`1_long" or
        "System.Collections.Generic.Queue`1_double" => "il2cpp_object_new(il2cpp::il2cpp_queue_class())",
        // URI-FIX (diffuri001): a dotted class-element instantiation (Queue<Uri>
        // -> "Queue`1_System.Uri") rides the non-generic queue descriptor; the
        // remainder is namespace-qualified (contains '.'), unlike the short
        // primitive suffixes.
        var queueDottedAlloc when queueDottedAlloc.StartsWith("System.Collections.Generic.Queue`1_", StringComparison.Ordinal) && queueDottedAlloc.Substring("System.Collections.Generic.Queue`1_".Length).Contains('.')
            => "il2cpp_object_new(il2cpp::il2cpp_queue_class())",
        // A5/B1/B6 (myIList/baudit2/ilistException): a dotted user-class
        // List instantiation ("List`1_System.Exception" /
        // "List`1_Cases.Baudit2.P") rides the shared listobj descriptor
        // (the DottedRefArg kind rule -- the queueDottedAlloc precedent
        // above). BCL struct full names are excluded by the rule and keep
        // the fallback (LOUD undefined symbol at link, never silent).
        var listDottedAlloc when listDottedAlloc.StartsWith("System.Collections.Generic.List`1_", StringComparison.Ordinal) && MethodRefResolver.DottedRefArg(listDottedAlloc) != null
            => "il2cpp_object_new(il2cpp::il2cpp_list_obj_class())",
        // dictsl-b23 (dictcase SortedList wiring): System.Collections.SortedList
        // allocates through the lazy *_class() descriptor accessor (sortedlist.h
        // -- same FIX-3 contract as Stack/Queue above); without the entry the
        // newobj fallback would emit il2cpp_object_new(&System_Collections_
        // SortedList_class), which is the same descriptor but allocated WITHOUT
        // the lazy registration (isinst identity divergence).
        "System.Collections.SortedList" => "il2cpp_object_new(il2cpp::il2cpp_sortedlist_class())",
        "System.Collections.Generic.SortedList`2_string_int" => "il2cpp_object_new(il2cpp::il2cpp_sortedlist_class())",
        "System.Collections.Generic.SortedDictionary`2_string_int" => "il2cpp_object_new(il2cpp::il2cpp_sortedlist_class())",
        "System.Collections.Generic.SortedSet`1_int" => "il2cpp_object_new(il2cpp::il2cpp_sortedlist_class())",
        // T2 W2: string/long/double/object/class ride the same sortedlist descriptor.
        "System.Collections.Generic.SortedSet`1_string" or
        "System.Collections.Generic.SortedSet`1_long" or
        "System.Collections.Generic.SortedSet`1_double" or
        "System.Collections.Generic.SortedSet`1_object" or
        "System.Collections.Generic.SortedSet`1_class" => "il2cpp_object_new(il2cpp::il2cpp_sortedlist_class())",
        // ADO stage 1 (batch 152f): System.Data.DataTable / DataColumn /
        // DataRow allocate through the lazy *_class() descriptor accessors
        // (il2cpp_datatable.h -- the FIX-3 contract).
        "System.Data.DataTable" => "il2cpp_object_new(il2cpp::runtime::il2cpp_datatable_class())",
        "System.Data.DataColumn" => "il2cpp_object_new(il2cpp::runtime::il2cpp_datacolumn_class())",
        "System.Data.DataRow" => "il2cpp_object_new(il2cpp::runtime::il2cpp_datarow_class())",
        // ADO stage 2 (batch 153d): DataSet.
        "System.Data.DataSet" => "il2cpp_object_new(il2cpp::runtime::il2cpp_dataset_class())",
        // FIX-3 (webcase): System.Net.WebClient uses the GLOBAL class descriptor
        // (il2cpp_http.h -- same contract as System_Version_class).
        "System.Net.WebClient" => "il2cpp_object_new(&System_Net_WebClient_class)",
        // FIX-3 (xmlcase): HANDLE-GAP MODE (MethodMap.Xml.cs contract) -- the
        // runtime xml domain has NO System_Xml_XmlDocument descriptor, so the
        // document IS its root node: newobj folds to nullptr (no allocation);
        // LoadXml then lands the root handle in the receiver local.
        "System.Xml.XmlDocument" => "nullptr",
        // D-XMLW-1 (xmlwrite): HANDLE-GAP MODE -- XmlWriterSettings has no
        // class descriptor either; newobj must allocate the raw settings
        // struct through the xml_write.h factory (the fallback emitted
        // il2cpp_object_new(&System_Xml_XmlWriterSettings_class) -- an
        // undefined symbol -> cmake_build FAIL). The raw struct pointer is
        // reinterpreted to Il2CppObject* (the handle-gap carrier shape).
        "System.Xml.XmlWriterSettings" => "static_cast<Il2CppObject*>(il2cpp::runtime::il2cpp_xml_writer_settings_new())",
        // M7 CG-FIX (excext cmake FAIL): non-generic BCL delegates
        // (System.Action, ...) -- StackFold.IsDelegateType (read-only Ir/
        // StackFold.cs) matches generic `System.Action\`` / `System.Func\``
        // but NOT the parameterless non-generic `System.Action`, so its newobj
        // fell through to the generic alloc path (il2cpp_object_new
        // (&System_Action_class) -- an undefined symbol). The delegate ctor
        // CallStmt is intercepted in EmitCallStmt and rewritten to
        // il2cpp::il2cpp_delegate_new(&method, target); the newobj placeholder
        // is a DISCARDED allocation (the delegate is created by the ctor), so
        // nullptr is the correct zero-cost placeholder.
        "System.Action" => "nullptr",
        // CG8 (findidx D-findidx-2): generic BCL delegates NOT matched by
        // StackFold.IsDelegateType (read-only Ir/StackFold.cs lists Func`/
        // Action`/ThreadStart/WaitCallback but NOT `System.Predicate\``), so
        // `newobj System.Predicate<int>` fell to the generic alloc path
        // (il2cpp_object_new(&System_Predicate_1_int_class) -- an undefined
        // symbol). Same contract as System.Action: the ctor CallStmt is
        // intercepted in EmitCallStmt and rewritten to
        // il2cpp::il2cpp_delegate_new; the newobj placeholder is a DISCARDED
        // allocation (the delegate is created by the ctor) -- nullptr is the
        // correct zero-cost placeholder.
        "System.Predicate`1_int" => "nullptr",
        // wir9-b23 (arrfuncs unlock): the CONVERTER delegate used by
        // Array.ConvertAll<int,int> -- System.Converter`2<int,int> -- same
        // CG8 contract as Predicate`1: the ctor CallStmt is intercepted in
        // EmitCallStmt (the System_Converter_ match below) and rewritten to
        // il2cpp::il2cpp_delegate_new; the newobj placeholder is a DISCARDED
        // allocation -- nullptr is the correct zero-cost placeholder.
        "System.Converter`2_int_int" => "nullptr",
        // cg8-b23 (icomparedeep): the COMPARISON delegate used by
        // Array.Sort<T>(T[], Comparison<T>) -- System.Comparison`1<int> /
        // `1<string> / `1<object> / `1<class> -- same CG8 contract as
        // Predicate`1: the ctor CallStmt is intercepted in EmitCallStmt (the
        // System_Comparison_ match below) and rewritten to
        // il2cpp::il2cpp_delegate_new; the newobj placeholder is a DISCARDED
        // allocation -- nullptr is the correct zero-cost placeholder. The
        // delegate lands in the runtime delegate-sort entries (arraysort.h
        // il2cpp_array_sort_int32_comparison / _ref_comparison).
        "System.Comparison`1_int" => "nullptr",
        "System.Comparison`1_string" => "nullptr",
        "System.Comparison`1_object" => "nullptr",
        "System.Comparison`1_class" => "nullptr",
        // net9/10 E2E icallcase (CG8 matrix report): Converter`2 — the ctor
        // interception already covers every instantiation ("System_Converter_"
        // prefix); only the BclAllocExpr placeholder keys were missing for
        // NON-int,int instantiations, so Array.ConvertAll<string,int>(arr,
        // int.Parse) hit the generated-name path. Same nullptr placeholder:
        // the delegate lands in the runtime icall_array_convert_all_* slot.
        "System.Converter`2_string_int" => "nullptr",
        "System.Converter`2_string_string" => "nullptr",
        _ => null,
        };
    }

    //--------------------------------------------------------------------------
    // EmitVirtualCall -- callvirt dispatch through the receiver's vtable.
    //
    //   slot = the virtual slot of MethodToken on its DECLARING type
    //          (VTablePlanner assigned slots; the slot layout is inherited by
    //          derived types, so the declaring type's slot == receiver type's
    //          slot when the receiver is a derived type).
    //   receiver = Args[0] (this), null-checked.
    //   emitted: ((void(*)(<receiver, args>))il2cpp_null_check((Il2CppObject*)
    //            (<receiver>))->klass->vtable[slot])(<args...>)
    //--------------------------------------------------------------------------
    private static string EmitVirtualCall(VirtualCallExpr v, MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        // M6 BCL-virtual devirtualization: callvirt targets declared on a BCL
        // base type that is NOT in the translated set (System.IO.TextReader /
        // TextWriter -- StringReader.ReadLine etc. compile as TextReader.ReadLine
        // memberrefs) resolve through MethodRefResolver to their domain icall
        // (the StringIO domain maps the virtual base members to the same
        // object icalls). Translated owners keep the vtable dispatch path.
        if (!OwnerIsTranslated(md, v.MethodToken, types))
        {
            // P18 deflate: pass the tracked receiver type so a DeflateStream
            // receiver routes the inherited Stream Write/Read/Close to the
            // deflate icalls (the resolver's Stream case dispatches on it).
            var mappedVc = MethodRefResolver.Resolve(md, v.MethodToken, types,
                v.ReceiverTypeName, v.Args.Count > 0 && v.Args[0] is LocAddrExpr, v.ReceiverTypeName);
            // M7 CG-FIX (boxdeep2 cmake FAIL): Object::ToString on a boxed
            // uint / Type receiver -- MethodMap.Object.cs's ToString list
            // (read-only) omits "System.UInt32"/"System.Type", so the resolver
            // returned the mangled generated-name fallback
            // (System_Object_ToString_<hash>, an undefined symbol). Route to
            // the runtime icall_object_to_string dispatch (handles strings as
            // identity + every boxed primitive by value).
            if (mappedVc?.CppName.StartsWith("System_Object_ToString_", StringComparison.Ordinal) == true
                && v.Args.Count > 0)
            {
                // M6 enum box klass (D-ENM-3, enumdeep2/3 ts_red): a BOXED
                // ENUM receiver -- the enum box now carries the enum's OWN
                // name-bearing klass (icall_box_enum), so the ToString must
                // dispatch through il2cpp_enum_boxed_to_string (enumext.h) --
                // it reads the boxed klass, resolves the registered enum
                // metadata by klass NAME and prints the MEMBER name ("Red",
                // net48-identical). icall_object_to_string would fall through
                // to the klass-name fallback and print "Cases.Color".
                if (v.ReceiverTypeName != null && IsEnumReceiverType(v.ReceiverTypeName, types))
                    return $"reinterpret_cast<Il2CppObject*>(il2cpp::runtime::il2cpp_enum_boxed_to_string(static_cast<Il2CppObject*>({EmitExpr(v.Args[0], md, types)})))";
                return $"reinterpret_cast<Il2CppObject*>(il2cpp::runtime::icall_object_to_string({EmitExpr(v.Args[0], md, types)}))";
            }
            // M7 CG-FIX (excext cmake FAIL): a callvirt to System.Action::Invoke
            // -- StackFold.IsDelegateType (read-only Ir/StackFold.cs) matches
            // generic `System.Action\``/`System.Func\`` but NOT the
            // parameterless non-generic `System.Action`, so the invoke fell to
            // the mangled generated-name path (System_Action_Invoke_<hash>, an
            // undefined symbol). Emit the runtime delegate invoke: void-return
            // (the System.Action lambda body) dispatches through the leaf
            // methodPtr -- single-cast Action, receiver is the delegate object.
            if (mappedVc?.CppName.StartsWith("System_Action_Invoke_", StringComparison.Ordinal) == true
                && v.Args.Count > 0)
            {
                var d0 = EmitExpr(v.Args[0], md, types);
                var d = $"(reinterpret_cast<il2cpp::Il2CppDelegate*>(il2cpp_null_check(static_cast<Il2CppObject*>({d0}))))";
                // void (void): ((void(*)(void))d->methodPtr)()
                var invokeArgs = string.Join(", ", v.Args.Skip(1).Select(a => EmitExpr(a, md, types)));
                return $"((reinterpret_cast<void(*)(Il2CppObject*)>({d}->methodPtr))({d}->target))";
            }
            if (mappedVc != null)
            {
                var callArgsVc = EmitIcallCallArgs(mappedVc, v.Args, md, types) ?? "";
                var callCodeVc = $"{mappedVc.CppName}({callArgsVc})";
                return mappedVc.ReturnsRef
                    ? $"reinterpret_cast<Il2CppObject*>({callCodeVc})"
                    : callCodeVc;
            }
        }
        var (owner, sig) = VirtualTarget(md, v.MethodToken, types);
        var retCpp = VirtualReturnTypeCpp(md, v.MethodToken);
        var paramCpp = VirtualParamTypesCpp(md, v.MethodToken); // excludes this; this is always Il2CppObject*
        // P-G3 shared-callee ABI: a SHARED generic definition (Pair`2) exposes
        // EVERY generic slot as Il2CppObject* (the D3 object clone), regardless
        // of the per-argument M3 verdict. The vtable C++ signature must match
        // the clone's declared shape (CallSiteVarIsRef marks only reference
        // instantiation slots -- Pair<int,string> would otherwise keep the raw
        // int32 slot and the fnPtr cast would never link).
        var sharedCallee = owner.GenericArity > 0
            && GenericUsageAnalyzer.SharedGenericDefs?.Contains(owner.FullName) == true
            && !SharedCallInstanceAllInt(md, v.MethodToken);
        if (sharedCallee)
        {
            for (var i = 0; i < paramCpp.Length; i++) paramCpp[i] = "Il2CppObject*";
            // P-G5 D6: keep a concrete SZArray return DECLARED Il2CppArray*.
            // The caller-side bridges (:5283/:5302) reinterpret the array
            // pointer to the caller's Il2CppObject* result temp EXACTLY on
            // `retCpp == "Il2CppArray*"`; overriding it to Il2CppObject*
            // (the generic-slot rule below) emits the raw array pointer into
            // the object temp -> C++ "cannot convert Il2CppArray* to
            // Il2CppObject*" (Il2CppArray embeds Il2CppObject obj as a DATA
            // MEMBER, not inheritance -- il2cpp_array.h:44-53). The shared
            // clone's declared return IS Il2CppArray* (ReturnTypeCpp 0x1D),
            // so the vtable/fnPtr cast also must use Il2CppArray*.
            if (retCpp != "void" && retCpp != "Il2CppObject*" && retCpp != "Il2CppArray*") retCpp = "Il2CppObject*";
        }
        // R2b P2 (interface-instantiation ABI): the method signature encodes
        // T as the int32 default; the CALL-SITE TypeSpec is authoritative.
        // When the target's parent instantiates T with a reference arg
        // (IMyList`1<class System.Exception>) every VAR slot is the managed
        // reference (Il2CppObject*) -- the dispatched clone returns/accepts
        // Il2CppObject* (P2 declaration face).
        if (CallSiteVarIsRef(md, v.MethodToken))
        {
            var sigH = MetadataTokens.Handle(v.MethodToken).Kind == HandleKind.MethodDefinition
                ? md.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.Handle(v.MethodToken)).Signature
                : md.GetMemberReference((MemberReferenceHandle)MetadataTokens.Handle(v.MethodToken)).Signature;
            var (retVar2, paramVars2, _) = MethodSignatureCoder.DecodeVarFlags(md, sigH);
            if (retVar2) retCpp = "Il2CppObject*";
            for (var pi = 0; pi < paramCpp.Length && pi < paramVars2.Length; pi++)
                if (paramVars2[pi]) paramCpp[pi] = "Il2CppObject*";
        }
        var receiver = v.Args.Count > 0 ? v.Args[0] : throw new NotSupportedException("callvirt without receiver");
        var args = v.Args.Skip(1).Select((a, pi) =>
        {
            var expr = EmitExpr(a, md, types);
            // P-G3 shared-callee box bridge: a shared clone's generic slots
            // are Il2CppObject*; a VALUE argument (paircase Set(11, str)) must
            // be boxed to a managed object before the call -- a raw int into
            // the object slot is a hard C++ error. Reference args pass through
            // unchanged (they already ARE the object).
            if (sharedCallee && pi < paramCpp.Length && paramCpp[pi] == "Il2CppObject*")
            {
                var k = TypeKindResolver.ResolveExprKind(a, _curLocKinds);
                if (k is "ref" or "r" or "ptr") return expr;
                if (k is "l")
                    return $"reinterpret_cast<Il2CppObject*>(il2cpp::runtime::icall_box_long(static_cast<int64_t>({expr})))";
                return $"il2cpp::il2cpp_box_int32(nullptr, static_cast<int32_t>({expr}))";
            }
            return expr;
        }).ToList();
        var receiverCode = EmitExpr(receiver, md, types);
        var receiverChecked = $"il2cpp_null_check(static_cast<Il2CppObject*>({receiverCode}))";

        // Devirt S1 (devirtualization-plan.md §S1): a callvirt whose receiver's
        // STATIC type (tracked by StackFold objTypes -- newobj/box/GetType/
        // delegate results) is an EFFECTIVELY-SEALED translated class lowers to
        // a DIRECT call. A sealed class cannot be subclassed, so the runtime
        // receiver is exactly that class (or null): the vtable slot resolves to
        // ONE statically-known implementation -- VTablePlanner already placed
        // the effective override in the receiver type's slot table
        // (recvType.VirtualMethods[slot], base slots deep-copied). Pure
        // compile-time lowering, runtime unchanged. The callvirt null-check is
        // PRESERVED (receiverChecked) -- identical semantics to the vtable path
        // below, NOT the unchecked non-virtual callvirt form (2911-2918, which
        // relies on method-body self-checks).
        if (v.ReceiverTypeName != null)
        {
            var direct = TryDevirtualize(v, owner, sig, types, receiverChecked, args, retCpp);
            if (direct != null) return direct;
        }

        string dispatch;
        if (owner.IsInterface)
        {
            // Interface dispatch (Task 8): look up the interface's mapping entry in the receiver's
            // runtime type descriptor interfaces table, slotMap[ifaceSlot] -> vtable slot -> call.
            //   The runtime helper scans interfaces linearly (M2 has few interfaces, linear suffices; M3 hash-optimizes).
            var ifaceSlot = InterfaceSlot(owner, sig);
            dispatch = $"*il2cpp_interface_dispatch({receiverChecked}, &{TypeEmitter.TypeCppName(owner)}_class, {ifaceSlot})";
        }
        else
        {
            var slot = owner.VirtualMethods.FindIndex(m => m != null && VTablePlanner.SigKey(m) == sig);
            if (slot >= 0)
            {
                dispatch = $"{receiverChecked}->klass->vtable[{slot}]";
            }
            else
            {
                // NON-VIRTUAL callvirt (C# emits callvirt for every instance
                // call): direct call -- the receiver is typed, the target is
                // statically known. Resolve the method on the OWNING type,
                // walking the base type chain (the method may be inherited).
                var targetMethod = owner.Methods.FirstOrDefault(m => VTablePlanner.SigKey(m) == sig);
                var targetTypeDef = owner;
                if (targetMethod == null)
                {
                    for (var bt = owner.BaseType; bt != null; bt = bt.BaseType)
                    {
                        targetMethod = bt.Methods.FirstOrDefault(m => VTablePlanner.SigKey(m) == sig);
                        if (targetMethod != null) { targetTypeDef = bt; break; }
                    }
                }
                if (targetMethod == null)
                {
                    var globalMatch = types.FirstOrDefault(t => t.Methods.Any(m => VTablePlanner.SigKey(m) == sig));
                    if (globalMatch != null)
                    {
                        targetMethod = globalMatch.Methods.First(m => VTablePlanner.SigKey(m) == sig);
                        targetTypeDef = globalMatch;
                    }
                }
                if (targetMethod == null)
                {
                    var callArgs3 = string.Join(", ", new[] { receiverCode }.Concat(args));
                    return $"{TypeEmitter.TypeCppName(owner)}_{sig.Split('(')[0]}_{SigHash.Hash(TypeEmitter.ReflectionFullName(owner) + "." + sig.Split('(')[0] + "(" + sig.Split('(')[1])}({callArgs3})";
                }
                var fnName = MethodCppName(targetTypeDef, targetMethod);
                var callArgs2 = string.Join(", ", new[] { receiverCode }.Concat(args));
                // P-G3 D1 (decision A): the shared clone returns the T slot as
                // Il2CppObject* (boxed for value arguments). The return stays
                // BOXED -- the IL's own box:/unbox.any conversions at the
                // consumer side handle the value extraction (a box:i on an
                // already-object value is identity, CodeGenerator box:i arm).
                // Unboxing here would contradict the fold's ReturnsRef=true
                // (the result temp is declared Il2CppObject*) and produce
                // `Il2CppObject* t = il2cpp_unbox_int32(...)` -- ill-formed.
                // SZArray-return bridge (paircase Make): a T1[] return declares
                // Il2CppArray* in the callee (ReturnTypeCpp 0x1D) while the
                // fold labels the caller result temp a managed reference
                // (Il2CppObject* -- SZArray is a reference type) -- reinterpret
                // the array pointer to the uniform object slot (the same
                // bridge EmitTask9Call applies on the `call` path).
                var directResult = $"{fnName}({callArgs2})";
                if (retCpp == "Il2CppArray*")
                    directResult = $"reinterpret_cast<Il2CppObject*>({directResult})";
                return directResult;
            }
        }

        // Call target function pointer: signature = this (Il2CppObject*) + explicit params (real types).
        var fnSig = string.Join(", ", new[] { "Il2CppObject*" }.Concat(paramCpp));
        var fnPtr = $"(({retCpp}(*)({fnSig})){dispatch})";
        var callArgs = string.Join(", ", new[] { receiverCode }.Concat(args));
        // P-G3 D1 (decision A): the shared clone returns the T slot as
        // Il2CppObject* -- the return stays boxed (see the direct-callvirt
        // arm above); the downstream box:/unbox.any conversion extracts the
        // value. Unboxing at this call point would contradict the fold's
        // ReturnsRef=true result-temp declaration (Il2CppObject*).
        // SZArray-return bridge: the callee fnPtr declares Il2CppArray* while
        // the caller's result temp is the managed-reference Il2CppObject* --
        // reinterpret the array pointer to the object slot (paircase Make).
        var fnPtrResult = $"{fnPtr}({callArgs})";
        return retCpp == "Il2CppArray*"
            ? $"reinterpret_cast<Il2CppObject*>({fnPtrResult})"
            : fnPtrResult;
    }

    //--------------------------------------------------------------------------
    // Devirt S1/S3 (devirtualization-plan.md §3.2): callvirt -> direct call on
    // a statically-known receiver. Returns the emitted C++ or null (fall back
    // to the runtime vtable dispatch below).
    //
    // S1 class path gates:
    //   ① the receiver's STATIC type (v.ReceiverTypeName, tracked by StackFold
    //     objTypes) resolves to a translated type -- same arity-prefix rule as
    //     VirtualTarget/OwnerIsTranslated ("Box`1_int" matches "Box`1");
    //   ② that type is effectively sealed -- metadata Sealed OR closed-world
    //     no-subclass (DerivedCount==0), and NOT abstract/interface/value-type;
    //   ③ the callvirt owner is a CLASS (interface dispatch is S3) whose slot
    //     for the signature resolves within the receiver's vtable;
    //   ④ the receiver static type is the owner or a subtype -- a stale
    //     objTypes hint that contradicts the call's declaring type is skipped
    //     (the vtable path stays correct).
    //
    // S3 interface gates (owner.IsInterface): the interface has exactly ONE
    // concrete implementer in the closed world (InterfaceImplementerCount == 1,
    // filled by VTablePlanner) OR the receiver static type is effectively
    // sealed; the implementation resolves via recvType.InterfaceSlotMaps
    // (interface slot -> vtable slot, VTablePlanner.BuildInterfaceMaps),
    // skipping il2cpp_interface_dispatch's runtime linear scan.
    //
    // The resolved implementation is recvType.VirtualMethods[slot]: VTablePlanner
    // deep-copies base slots into every derived type and replaces overridden
    // slots, so the receiver's slot table holds the EXACT function the runtime
    // vtable would dispatch for that slot. The callvirt null-check is preserved
    // (receiverChecked is passed as `this` -- a null receiver throws exactly
    // like the vtable path, NOT the unchecked non-virtual callvirt form).
    //--------------------------------------------------------------------------
    // R2b (M1/M2): resolve an owner/receiver type name to its TypeDef,
    // PREFERRING the suffix-exact closed clone over the shared-FullName
    // definition. Order: suffix-exact instantiation name ("Box`1_string") ->
    // exact FullName (definition / default instance) -> F1 argument-aware
    // angle-bracket name ("VirtBase`1<System.String>") -> legacy prefix
    // fallback. Single implementation (C6): the clone-linking pass and the
    // callvirt/devirt owners share it.
    private static TypeDef? ResolveInstantiationOwner(IReadOnlyList<TypeDef> types, string name)
    {
        var exact = types.FirstOrDefault(t => t.InstantiationSuffix != null
            && t.FullName + "_" + t.InstantiationSuffix == name);
        if (exact != null) return exact;
        var byName = types.FirstOrDefault(t => t.FullName == name);
        if (byName != null) return byName;
        // F1 (R2b P2): a canonical generic-instance name resolves by its
        // ARGUMENT. A BASE type ("VirtBase`1<System.String>") must bind the
        // matching closed CLONE (VirtBase_1_string) -- the definition's
        // default "_int" instance has the wrong ABI (genericvirtstr AV:
        // VirtStr inherited the int vtable slot and dispatch truncated the
        // string reference). A class-typed argument binds the "_class" clone.
        // Only when no clone exists (interfaces have none; the arg is "T")
        // fall back to the definition.
        var lt = name.IndexOf('<');
        if (lt > 0 && name.EndsWith(">", StringComparison.Ordinal))
        {
            var gDef = name[..lt];
            var inner = name[(lt + 1)..^1];
            var suffix = inner switch
            {
                "System.String" => "string",
                "System.Object" => "object",
                "System.Int32" => "int",
                "System.Int64" => "long",
                "System.Double" => "double",
                "System.Single" => "float",
                "System.Boolean" => "bool",
                "System.Char" => "char",
                "System.Byte" => "byte",
                _ => null,
            };
            if (suffix != null)
            {
                var cloneHit = types.FirstOrDefault(t => t.FullName == gDef
                    && t.InstantiationSuffix == suffix);
                if (cloneHit != null) return cloneHit;
            }
            else
            {
                var classHit = types.FirstOrDefault(t => t.FullName == gDef
                    && t.InstantiationSuffix == "class");
                if (classHit != null) return classHit;
            }
            var gDefHit = types.FirstOrDefault(t => t.FullName == gDef);
            if (gDefHit != null) return gDefHit;
        }
        return types.FirstOrDefault(t => t.GenericArity > 0
            && name.StartsWith(t.FullName + "_", StringComparison.Ordinal));
    }

    private static string? TryDevirtualize(VirtualCallExpr v, TypeDef owner, string sig,
        IReadOnlyList<TypeDef> types, string receiverChecked, List<string> args, string retCpp)
    {
        if (v.ReceiverTypeName == null || owner.IsValueType) return null;
        var recvType = ResolveInstantiationOwner(types, v.ReceiverTypeName);
        if (recvType == null || recvType.IsInterface || recvType.IsValueType) return null;

        // P-G5 D6: a devirtualized DIRECT call returning an SZArray declares
        // the callee as Il2CppArray* (ReturnTypeCpp 0x1D) while the caller's
        // result temp is a managed-reference Il2CppObject* (ReturnsRef labels
        // 0x1D a reference) -- reinterpret the array pointer to the object
        // slot, exactly like the non-devirt direct arm (:5283) and fnPtr arm
        // (:5302). Il2CppArray embeds the object header as a data member, so
        // the raw assignment would not compile.
        string SzArrayBridge(string call) => retCpp == "Il2CppArray*"
            ? $"reinterpret_cast<Il2CppObject*>({call})"
            : call;

        // Devirt S3 (devirtualization-plan.md §4 S3): interface dispatch on a
        // receiver whose static type is known lowers to a DIRECT call when the
        // interface has exactly ONE concrete implementer in the closed world
        // (InterfaceImplementerCount, VTablePlanner) -- any object assigned to
        // the interface must be that single type -- OR the receiver static type
        // is effectively sealed (a sealed receiver is exactly that type, so its
        // own interface map resolves). The implementation comes from the
        // receiver's compile-time InterfaceSlotMaps (interface slot -> vtable
        // slot), skipping il2cpp_interface_dispatch's runtime linear scan.
        if (owner.IsInterface)
        {
            if (owner.InterfaceImplementerCount != 1 && !recvType.IsEffectivelySealed) return null;
            if (!recvType.InterfaceSlotMaps.TryGetValue(owner, out var slotMap)) return null;
            var ifaceSlot = InterfaceSlot(owner, sig);
            if (ifaceSlot < 0 || ifaceSlot >= slotMap.Length) return null;
            var typeSlot = slotMap[ifaceSlot];
            if (typeSlot < 0 || typeSlot >= recvType.VirtualMethods.Count) return null;
            var ifaceImpl = recvType.VirtualMethods[typeSlot];
            if (ifaceImpl == null || ifaceImpl.IsAbstract) return null;
            var ifaceFn = MethodCppName(ifaceImpl.Owner, ifaceImpl);
            var ifaceArgs = string.Join(", ", new[] { receiverChecked }.Concat(args));
            return SzArrayBridge($"{ifaceFn}({ifaceArgs})");
        }

        // Gate ④: the receiver must be assignable to the declaring owner.
        if (!ReferenceEquals(recvType, owner) && !IsSubtypeOf(recvType, owner)) return null;
        var slot = owner.VirtualMethods.FindIndex(m => m != null && VTablePlanner.SigKey(m) == sig);
        if (slot < 0 || slot >= recvType.VirtualMethods.Count) return null;
        var impl = recvType.VirtualMethods[slot];
        if (impl == null || impl.IsAbstract) return null;
        // Gate ②: the receiver must be effectively sealed (S1 class path) OR
        // the effective slot method is FINAL (Devirt S2) -- a final method
        // (sealed override) cannot be overridden by any subtype, so the slot
        // resolves statically even when the receiver's static type is NOT
        // effectively sealed (a subclass that re-implements nothing can only
        // inherit the final implementation). Both conditions are
        // method/type-level sealing; the null-check is preserved either way.
        if (!recvType.IsEffectivelySealed && !impl.IsFinal) return null;
        var fnName = MethodCppName(impl.Owner, impl);
        var callArgs = string.Join(", ", new[] { receiverChecked }.Concat(args));
        return SzArrayBridge($"{fnName}({callArgs})");
    }

    private static bool IsSubtypeOf(TypeDef t, TypeDef baseType)
    {
        for (var b = t.BaseType; b != null; b = b.BaseType)
            if (ReferenceEquals(b, baseType)) return true;
        return false;
    }

    // Virtual method explicit-parameter C++ types (excluding this): decoded from the signature (i -> int32_t; r: -> Il2CppObject*).
    // R2b P2 (interface-instantiation ABI): whether a callvirt/interface
    // target's parent TypeSpec instantiates T with a REFERENCE argument
    // (IMyList`1<class System.Exception>) -- the VAR slots of the target then
    // ride as Il2CppObject* at the call site. A VAR argument (0x13)
    // refers to the ENCLOSING clone's T: resolved through
    // _curInstantiationSuffix (same closure as the declaration/body faces).
    // false for MethodDef targets, open definitions, value-type args and
    // primitive args.
    private static bool CallSiteVarIsRef(MetadataReader md, int token)
    {
        var handle = MetadataTokens.Handle(token);
        if (handle.Kind != HandleKind.MemberReference) return false;
        var mr = md.GetMemberReference((MemberReferenceHandle)handle);
        if (mr.Parent.Kind != HandleKind.TypeSpecification) return false;
        var sig = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)mr.Parent).Signature);
        if (sig.ReadByte() != 0x15) return false;
        _ = sig.ReadByte(); // CLASS/VALUETYPE marker
        _ = sig.ReadTypeHandle();
        if (sig.ReadCompressedInteger() != 1) return false;
        var argByte = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
        // R2b P2 closure (call side): B`1<!0> inside clone A<T>'s body -- the
        // ABI follows the clone's closed suffix ("int" -> value slot; every
        // other closed suffix -> managed reference, matching the declaration
        // face `suffix != "int"` predicate used by RefVar/local/return).
        if (argByte == 0x13)
            return _curInstantiationSuffix is not (null or "int");
        return VarArgByteIsRef(ref sig, argByte);
    }

    private static bool VarArgByteIsRef(ref BlobReader sig, byte b)
    {
        switch (b)
        {
            case 0x0E or 0x12 or 0x1C: return true;          // string/class/object
            case 0x1D: return true;                          // SZARRAY
            case 0x15:
            {
                var marker = sig.ReadByte();                 // nested GenericInst
                return marker != 0x11;                       // CLASS inst -> ref; VALUETYPE -> value
            }
default: return false;                           // primitives/VAR(handled by caller)/TypedRef: not a reference
            }
        }

    // P-G3: whether THIS call's closed instance is the ALL-INT M3 default
    // (Box`1<Int32>, the definition itself) -- it does NOT collapse onto the
    // shared object clone (CollapseInstantiation keeps the "int" default
    // per-instance), so its call site uses the raw int32 signature. Only
    // non-all-int instances (Box`1<String>) take the shared object ABI.
    private static bool SharedCallInstanceAllInt(MetadataReader md, int token)
    {
        var h = MetadataTokens.Handle(token);
        if (h.Kind != HandleKind.MemberReference) return false;
        var mr = md.GetMemberReference((MemberReferenceHandle)h);
        if (mr.Parent.Kind != HandleKind.TypeSpecification) return false;
        var ts = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)mr.Parent).Signature);
        if (ts.ReadByte() != 0x15) return false; // not a GenericInst instance
        ts.ReadByte();                    // CLASS/VALUETYPE marker
        ts.ReadTypeHandle();
        var argc = ts.ReadCompressedInteger();
        for (var i = 0; i < argc; i++)
        {
            var b = MethodSignatureCoder.SkipCustomModifiers(ref ts, ts.ReadByte());
            if (b != 0x08) return false;  // only the exact int32 default counts
        }
        return true;
    }

    private static string[] VirtualParamTypesCpp(MetadataReader md, int token)
    {
        string paramTypes;
        var handle = MetadataTokens.Handle(token);
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            var m = md.GetMethodDefinition((MethodDefinitionHandle)handle);
            (_, _, paramTypes, _) = MethodSignatureCoder.Decode(md, m.Signature);
        }
        else
        {
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            (_, _, paramTypes, _) = MethodSignatureCoder.Decode(md, mr.Signature);
        }
        if (paramTypes.Length == 0) return Array.Empty<string>();
        // F1 secondary (decimal-domain): virtual-dispatch params get the same
        // V:-aware typing as ParamTypesCpp -- a decimal value param must be
        // the full struct, not a truncated int32_t slot.
        return SplitTopLevel(paramTypes).Select(p => p switch
        {
            var r when r.StartsWith("r:", StringComparison.Ordinal) => "Il2CppObject*",
            var v when v.StartsWith("V:", StringComparison.Ordinal) => TypeCppName(v[2..]),
            _ => "int32_t",
        }).ToArray();
    }

    // Interface dispatch runtime helper: obj->klass looks up the interface mapping and returns the
    // vtable-slot function pointer. Declared in the runtime (il2cpp_runtime.h); the implementation scans the interfaces table linearly.
    private static int InterfaceSlot(TypeDef iface, string sig)
    {
        for (var i = 0; i < iface.VirtualMethods.Count; i++)
            if (VTablePlanner.SigKey(iface.VirtualMethods[i]) == sig)
                return i;
        throw new NotSupportedException($"interface {iface.FullName}::{sig} slot not found");
    }

    // Whether the callvirt target owner belongs to the transpiled type set (precondition for
    // VirtualTarget). Decodes with the same ownerFullName as VirtualTarget; a generic-instance owner
    // ("Box`1_int") matches the generic definition ("Box`1").
    private static bool OwnerIsTranslated(MetadataReader md, int token, IReadOnlyList<TypeDef> types)
    {
        var handle = MetadataTokens.Handle(token);
        string? ownerFullName;
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            ownerFullName = md.GetFullName(md.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType());
        }
        else if (handle.Kind == HandleKind.MemberReference)
        {
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            ownerFullName = mr.Parent.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)mr.Parent),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)mr.Parent),
                HandleKind.TypeSpecification => GenericInstantiationName(md, (TypeSpecificationHandle)mr.Parent),
                _ => null,
            };
        }
        else
        {
            ownerFullName = null;
        }
        return ownerFullName != null
            && types.Any(t => t.FullName == ownerFullName
                           || (t.GenericArity > 0 && ownerFullName.StartsWith(t.FullName + "_", StringComparison.Ordinal)));
    }

    // callvirt token -> (declaring type TypeDef, signature key).
    private static (TypeDef Owner, string Sig) VirtualTarget(MetadataReader md, int token, IReadOnlyList<TypeDef> types)
    {        var handle = MetadataTokens.Handle(token);
        string ownerFullName;
        string methodName;
        string paramTypes;
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            var m = md.GetMethodDefinition((MethodDefinitionHandle)handle);
            methodName = md.GetString(m.Name);
            ownerFullName = md.GetFullName(m.GetDeclaringType());
            (_, _, paramTypes, _) = MethodSignatureCoder.Decode(md, m.Signature);
        }
        else
        {
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            methodName = md.GetString(mr.Name);
            (_, _, paramTypes, _) = MethodSignatureCoder.Decode(md, mr.Signature);
            ownerFullName = mr.Parent.Kind switch
            {
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)mr.Parent),
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)mr.Parent),
                // M3 generics: callvirt on a generic instantiation carries a
                // TypeSpec parent; the owner resolves to the instantiation
                // (same encoding as the transpiled type name).
                HandleKind.TypeSpecification => GenericInstantiationName(md, (TypeSpecificationHandle)mr.Parent),
                _ => throw new NotSupportedException($"callvirt owner kind {mr.Parent.Kind}"),
            };
        }
        // Generic-instance owner ("Box`1_int") -> match the generic definition
        // ("Box`1"); R2b: prefer the suffix-exact clone (M2).
        var owner = ResolveInstantiationOwner(types, ownerFullName)
            ?? throw new NotSupportedException($"callvirt owner {ownerFullName} not in translated types");
        return (owner, $"{methodName}({paramTypes})");
    }

    // callvirt target method return type -> C++ type (the virtual method signature is resolved from the token).
    private static string VirtualReturnTypeCpp(MetadataReader md, int token)
    {
        var handle = MetadataTokens.Handle(token);
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            var m = md.GetMethodDefinition((MethodDefinitionHandle)handle);
            var (_, retType, _, _) = MethodSignatureCoder.Decode(md, m.Signature);
            return ReturnTypeCpp(retType);
        }
        var mr = md.GetMemberReference((MemberReferenceHandle)handle);
        var (_, retMr, _, _) = MethodSignatureCoder.Decode(md, mr.Signature);
        return ReturnTypeCpp(retMr);
    }

    // M7 CG-FIX (lockcase finally duplication): clone a finally-handler
    // statement list with every label renamed to a fresh unique name (append
    // `suffix`), and rewrite the branch targets that point at those labels.
    // The finally handler is emitted at TWO points (normal path in the try
    // body + exceptional path in the catch-all); a label inside it (the lock
    // statement's lockTaken guard `L_54:`) would otherwise be emitted twice in
    // one function scope -> C++ "redefinition of label". The two copies must
    // use disjoint label names. Only labels DEFINED within the copied list are
    // renamed (a branch to a label outside the finally -- illegal in M3 scope,
    // "leave-to-outside" is rejected -- would stay untouched).
    private static List<Stmt> RenameFinallyLabels(List<Stmt> stmts, string suffix, HashSet<string> branchTargets)
    {
        var labelMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var s in stmts)
            if (s is LabelStmt l && !labelMap.ContainsKey(l.Name))
                labelMap[l.Name] = l.Name + suffix;
        if (labelMap.Count == 0) return stmts;
        foreach (var v in labelMap.Values) branchTargets.Add(v);
        var result = new List<Stmt>(stmts.Count);
        foreach (var s in stmts)
        {
            if (s is LabelStmt l)
            {
                result.Add(new LabelStmt { Name = labelMap[l.Name], SourceOffset = l.SourceOffset, SourceOpName = l.SourceOpName });
            }
            else if (s is BranchStmt b && labelMap.TryGetValue(b.TargetLabel, out var nv))
            {
                result.Add(new BranchStmt { TargetLabel = nv, Condition = b.Condition, SourceOffset = b.SourceOffset, SourceOpName = b.SourceOpName });
            }
            else
            {
                result.Add(s); // reuse the immutable IR node verbatim (no clone needed)
            }
        }
        return result;
    }

    // M7 CG-FIX (excext cmake FAIL): the get_Message carrier CppName from
    // MethodMap.Exception.cs (read-only) is `il2cpp_string_intern_literal(
    // __il2cpp_ex.message_text()` -- MISSING the closing `)` of the
    // string_intern_literal call (the nested message_text() paren is balanced,
    // but the outer wrapper is not). The generic `{fn}({args})` emission then
    // produces `message_text()()` (calling the const char* as a function).
    // This is a resolver-string bug; since the resolver file is read-only, the
    // emitter appends the missing paren and emits the nested call WITHOUT the
    // generic `(...)` wrapper (the receiver is dropped, args are empty).
    private static bool IsMessageCarrierCppName(string cpp)
        => cpp == "il2cpp::il2cpp_string_intern_literal(__il2cpp_ex.message_text()";

    //--------------------------------------------------------------------------
    // WrapFrameRoots -- frame-root protocol for one method body (Task 10).
    //
    //   Prologue:  il2cpp::il2cpp_gc_add_root_range((void**)&loc0, 1);   [per ref local]
    //   Epilogue:  il2cpp::il2cpp_gc_remove_root_range((void**)&loc0);   [before EVERY return]
    //
    //   The frame-root protocol makes the method's reference locals visible to
    //   the collector while the method is on the call stack -- the M2
    //   equivalent of the full frame-record stack (Task 10's frame records
    //   arrive with M3's exception/StackTrace system; the root RANGE API is
    //   the shared contract).
    //
    //   M2 scope: single-threaded, no exception unwinding (the remove calls
    //   are emitted before every 'return' line; the M3 exception system adds
    //   unwinding-path removes). A method with zero ref locals emits nothing
    //   (zero overhead -- the common arithmetic case).
    //--------------------------------------------------------------------------
    private static string WrapFrameRoots(string body, List<string> refLocals, string methodName)
    {
        if (refLocals.Count == 0) return body;
        var lines = body.Split('\n').ToList();
        // Frame-root protocol (M3 Task 9): the prologue builds a frame record (array of ref-local
        // addresses) and pushes it; every return pops it (1:1 pairing). The frame is the GC safepoint
        // = root scan (replacing M2's add/remove root-range protocol -- the API stays compatible).
        var lastDecl = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith("int32_t ", StringComparison.Ordinal)
                || t.StartsWith("Il2CppObject* ", StringComparison.Ordinal)
                || t.StartsWith("const char* ", StringComparison.Ordinal))
                lastDecl = i;
        }
        var prologue = new List<string>
        {
            "",
            "    // Managed frame (M3 Task 9): this method's reference locals are GC roots while it is on the call stack.",
        };
        var refsList = string.Join(", ", refLocals.Select(l => $"static_cast<void*>(&{l})"));
        prologue.Add($"    void* __frameRefs[] = {{ {refsList} }};");
        prologue.Add($"    il2cpp::Il2CppFrame __frame = {{ nullptr, nullptr, nullptr, 0 }};");
        prologue.Add($"    __frame.methodName = \"{methodName}\";");
        prologue.Add("    __frame.refs = static_cast<void**>(__frameRefs);");
        prologue.Add($"    __frame.refCount = {refLocals.Count};");
        prologue.Add("    il2cpp::il2cpp_frame_push(&__frame);");
        // NOTE: no explicit pop -- the frame's RAII destructor pops it when
        // the method's stack storage goes out of scope (normal return AND
        // exception unwinding; M3 Task 10). The destructor's prev != nullptr
        // guard makes double-pop safe if a future explicit pop is added.
        lines.InsertRange(lastDecl + 1, prologue);

        // Epilogue: (removed -- RAII destructor handles the pop on every path)
        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }
        return sb.ToString();
    }

    //--------------------------------------------------------------------------
    // P0-2 (hotpath-audit-2) -- GC-frame elision analysis.
    //
    // The managed frame registers a method's reference locals as GC roots so
    // a collection triggered DURING the method finds them. A method that can
    // never allocate (directly or through any call it makes) can never
    // trigger a collection -- the collector runs only on allocation failure
    // / explicit il2cpp_gc_collect (no background thread) -- so its frame is
    // pure overhead. Failure paths of the checked runtime helpers never
    // allocate managed memory (checked_div/checked_rem throw a C++
    // DotNetException carrier; il2cpp_null_fail aborts; il2cpp_array_fail
    // throws the same carrier), so they cannot trigger a collection either.
    //
    // CONSERVATIVE: only icalls VERIFIED as inline pure field reads in the
    // runtime headers (and the inline array element/length helpers) count as
    // non-allocating; anything unknown keeps the frame (better to keep a
    // useless frame than to drop a needed one).
    //--------------------------------------------------------------------------
    private static readonly System.Collections.Generic.HashSet<string> PureIcallCppNames =
        new(System.StringComparer.Ordinal)
        {
            // Verified INLINE pure field reads (il2cpp_string.h /
            // il2cpp_array.h) -- no allocation possible.
            "il2cpp::il2cpp_string_get_length",
            "il2cpp::il2cpp_array_get_length",
            // Verified pure no-op (il2cpp_runtime.h / runtime.cpp):
            // System.Object's base constructor has no fields or behavior -- the
            // emitted `il2cpp_object_ctor_nop(this)` can never allocate. Making
            // it pure lets every newobj-ctor pair (the object churn pattern)
            // run with NO GC frame -- the ctor body is then fully inlinable.
            "il2cpp::runtime::il2cpp_object_ctor_nop",
        };

    private static bool StmtMayGc(Stmt s) => s switch
    {
        // A label is a position marker -- executing it does nothing, so it can
        // never trigger a collection. (The old whole-body pureLeaf flag never
        // noticed: any label forced the body non-leaf; the per-slot analysis
        // would otherwise root every slot live at a label.)
        LabelStmt => false,
        AssignStmt a => ExprMayGc(a.Value),
        ExprStmt e => ExprMayGc(e.Value),
        BranchStmt b => b.Condition != null && ExprMayGc(b.Condition),
        RetStmt r => r.Value != null && ExprMayGc(r.Value),
        // capture_last_error / pinvoke_utf8_free are native-adjacent no-ops
        // for the GC (no managed allocation); the inline array element/length
        // helpers (stelem_/ldelem_/ldlen_) are pure memory operations; icalls
        // verified as PURE (the PureIcallCppNames whitelist -- inline field
        // reads, the ctor no-op) cannot allocate either. Everything else
        // (ensure_cctor_*, vctor_*, user call_*, unlisted icalls) may allocate.
        CallStmt c => (c.Resolved == null || !PureIcallCppNames.Contains(c.Resolved.CppName))
                      && !c.Function.StartsWith("capture_last_error", StringComparison.Ordinal)
                      && !c.Function.StartsWith("pinvoke_utf8_free", StringComparison.Ordinal)
                      && !c.Function.StartsWith("pinvoke_utf16_free", StringComparison.Ordinal)
                      && !c.Function.StartsWith("stelem_", StringComparison.Ordinal)
                      && !c.Function.StartsWith("ldelem_", StringComparison.Ordinal)
                      && !c.Function.StartsWith("ldlen_", StringComparison.Ordinal)
                      || c.Args.Any(ExprMayGc),
        _ => true, // unknown statement kinds are treated as GC-capable
    };

    private static bool ExprMayGc(Expr e) => e switch
    {
        // newarr_ allocates (il2cpp_array_new); user-method calls (Resolved
        // == null) and non-whitelisted icalls may allocate; the inline array
        // element/length helpers (ldelem_/stelem_/ldlen_) are pure.
        CallExpr c => c.Function.StartsWith("newarr_", StringComparison.Ordinal)
                      || (!c.Function.StartsWith("ldelem_", StringComparison.Ordinal)
                          && !c.Function.StartsWith("stelem_", StringComparison.Ordinal)
                          && !c.Function.StartsWith("ldlen_", StringComparison.Ordinal)
                          && (c.Resolved == null || !PureIcallCppNames.Contains(c.Resolved.CppName)))
                      || c.Args.Any(ExprMayGc),
        // Virtual dispatch / delegates run USER code -- may allocate.
        VirtualCallExpr or NewObjExpr or DelegateNewExpr or DelegateInvokeExpr
            // intern-literal may allocate on a miss; marshal allocates a buffer.
            or MarshalUtf8Expr or ExceptionEntryExpr or StringConstExpr => true,
        BinaryExpr b => ExprMayGc(b.Left) || ExprMayGc(b.Right),
        UnaryExpr u => ExprMayGc(u.Operand),
        FieldExpr f => ExprMayGc(f.Object),
        FieldAddrExpr fa => ExprMayGc(fa.Object),
        // il2cpp_null_check is inline (object.h) and its failure aborts
        // (no allocation); checked_div/rem throw a C++ carrier (no managed
        // allocation); static-field access is a plain variable read (the
        // cctor guard is a separate CallStmt, analyzed on its own).
        IntConstExpr or Int64ConstExpr or DoubleConstExpr or NullExpr or LocExpr
            or LocAddrExpr or StaticFieldExpr or StaticFieldAddrExpr or ValueTypeNewExpr => false,
        _ => true, // unknown expression kinds are treated as GC-capable
    };

    // P0-1/P1 (hotpath-audit-2): the compile-time value of a constant
    // expression (IntConstExpr / Int64ConstExpr); null for anything else.
    private static long? ConstantValue(Expr e) => e switch
    {
        IntConstExpr i => i.Value,
        Int64ConstExpr l => l.Value,
        _ => null,
    };

    // P26 (bcldeep): a negative integer literal operand (ldc.i4 0xFFFFFFFF
    // carried as -1). Only INTEGER literals can be negative (doubles route
    // through the fp comparison branch above, references cannot compare to a
    // number), so this flags exactly the unsigned-narrowing comparisons.
    private static bool IsNegativeIntConst(Expr e) => e switch
    {
        IntConstExpr i => i.Value < 0,
        Int64ConstExpr l => l.Value < 0,
        _ => false,
    };

    // The operand-width test for the int32/int64-ambiguous binary-op paths
    // (Div/Rem/ShrUn/CgtUn/CltUn/Shl/Shr). The IR carries no type on
    // BinaryExpr (b.Kind is never populated -- the M5 "l"/"d" hint only
    // exists on CallExpr.ResultKind and the IL local kinds), yet emitting an
    // int64 operand with int32 casts truncates it (bitopcase G1/G2/G3/G5:
    // int64 shifts got a 5-bit mask, shr.un/div/cgt.un truncated the left
    // operand to uint32_t/int32_t). Width is recovered from the operand
    // nodes: Int64ConstExpr (a constant beyond int32 range), an IsInt64
    // in-range ldc.i8 constant, an int64/uint64 local (via the published
    // _curLocKinds), a 64-bit call result (ResultKind "l"/"ptr"), or a
    // nested 64-bit binary.
    private static bool ExprIsInt64(Expr e) => e switch
    {
        Int64ConstExpr => true,
        IntConstExpr i => i.IsInt64,
        LocExpr l => _curLocKinds.TryGetValue(l.Name, out var k) && k == "l",
        BinaryExpr b => b.Kind == "l" || ExprIsInt64(b.Left) || ExprIsInt64(b.Right),
        UnaryExpr u => ExprIsInt64(u.Operand),
        CallExpr c => c.ResultKind == "l" || c.ResultKind == "ptr",
        _ => false,
    };

    // The double-operand test (mirror of ExprIsInt64): a DoubleConstExpr, a
    // double-typed local (LocalKinds "d"/"f"), a 64-bit call result carrying
    // "d", or a nested double binary. Used by the Div/Rem/compare paths --
    // `double_local / int` must stay a DOUBLE division (numchain `da / b`),
    // not fall through to the int32 checked_div (which truncates).
    private static bool ExprIsDouble(Expr e) => e switch
    {
        DoubleConstExpr => true,
        LocExpr l => _curLocKinds.TryGetValue(l.Name, out var k) && k == "d",
        BinaryExpr b => b.Kind == "d" || ExprIsDouble(b.Left) || ExprIsDouble(b.Right),
        UnaryExpr u => ExprIsDouble(u.Operand),
        CallExpr c => c.ResultKind == "d",
        _ => false,
    };

    //--------------------------------------------------------------------------
    // ProtectDoubleSelfCompare -- mathdeep IsNaN(double)/IsFinite(double).
    //
    // .NET `v == v` for a DOUBLE is NaN-aware: NaN == NaN is FALSE and
    // NaN != NaN is TRUE -- exactly the IsNaN probe. Normalize.cs folds the
    // int32 self-identity `Ceq(x, x) -> 1` (valid only for int32 slots where
    // x == x is always true), which turns IsNaN's `v != v` into a constant 0
    // (mathdeep FAIL@diff 19). Normalize is read-only, so this pass runs in
    // GenerateCore BEFORE Normalize.FoldStmts and rewrites a self-comparison
    // of a double-typed slot into a synthetic call the emitter renders as the
    // raw double comparison -- the fold can no longer fire.
    //
    //   Ceq(x, x)            -> il2cpp_double_eq_self(x)   (v == v)
    //   Ceq(Ceq(x,x), 0)     -> il2cpp_double_ne_self(x)   (v != v)
    //
    // A slot is double-typed when its IL signature is "d"/"f" (param) or its
    // local kind is "d"/"f" (the MethodSignatureCoder/ReadLocalNames
    // encodings). Only exact self-comparisons (both sides the same slot
    // name) are rewritten -- a genuine comparison of two different doubles is
    // left alone.
    //--------------------------------------------------------------------------
    private static List<Stmt> ProtectDoubleSelfCompare(List<Stmt> stmts, MethodDef m, MethodBodyInfo bodyInfo)
    {
        // Build the set of double-typed slot names for this method: parameters
        // (m.ParamTypes is the comma-joined MethodSignatureCoder encoding; for
        // instance methods arg0 = this, so param i maps to arg{i+1}) and
        // IL-declared locals (LocalKinds "d"/"f").
        var doubleSlots = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        if (m.ParamTypes.Length > 0)
        {
            var parts = SplitTopLevel(m.ParamTypes).ToArray();
            for (var i = 0; i < parts.Length; i++)
                if (parts[i] is "d" or "f")
                    doubleSlots.Add(m.IsStatic ? $"arg{i}" : $"arg{i + 1}");
        }
        for (var i = 0; i < bodyInfo.LocalKinds.Length; i++)
            if (bodyInfo.LocalKinds[i] is "d" or "f")
                doubleSlots.Add(bodyInfo.LocNames[i]);
        if (doubleSlots.Count == 0) return stmts;

        Expr Rewrite(Expr e)
        {
            switch (e)
            {
                case BinaryExpr b:
                    // v == v: both sides the same double slot -> synthetic eq.
                    if (b.Op == BinaryOp.Ceq
                        && b.Left is LocExpr bl && b.Right is LocExpr br
                        && bl.Name == br.Name && doubleSlots.Contains(bl.Name))
                        return new CallExpr { Function = "il2cpp_double_eq_self", Args = { new LocExpr { Name = bl.Name } } };
                    // v != v: Ceq(Ceq(x,x), 0) -> synthetic ne. Normalize would
                    // fold the inner Ceq(x,x) -> 1 first (int32 self-identity),
                    // so the whole pattern must be recognized at this node.
                    if (b.Op == BinaryOp.Ceq
                        && b.Left is BinaryExpr inner && inner.Op == BinaryOp.Ceq
                        && inner.Left is LocExpr il && inner.Right is LocExpr ir
                        && il.Name == ir.Name && doubleSlots.Contains(il.Name)
                        && b.Right is IntConstExpr { Value: 0 })
                        return new CallExpr { Function = "il2cpp_double_ne_self", Args = { new LocExpr { Name = il.Name } } };
                    var nl = Rewrite(b.Left);
                    var nr = Rewrite(b.Right);
                    return (ReferenceEquals(nl, b.Left) && ReferenceEquals(nr, b.Right))
                        ? b
                        : new BinaryExpr { Op = b.Op, Left = nl, Right = nr, Kind = b.Kind };
                case UnaryExpr u:
                    var no = Rewrite(u.Operand);
                    return ReferenceEquals(no, u.Operand) ? u : new UnaryExpr { Op = u.Op, Operand = no };
                case CallExpr c:
                    return RewriteCallExpr(c);
                case VirtualCallExpr v:
                {
                    var changed = false;
                    var nv = new VirtualCallExpr { MethodToken = v.MethodToken, ReturnsRef = v.ReturnsRef, ReceiverTypeName = v.ReceiverTypeName };
                    for (var i = 0; i < v.Args.Count; i++)
                    {
                        var na = Rewrite(v.Args[i]);
                        if (!ReferenceEquals(na, v.Args[i])) changed = true;
                        nv.Args.Add(na);
                    }
                    return changed ? nv : v;
                }
                case DelegateInvokeExpr d:
                {
                    var changed = false;
                    var nd = new DelegateInvokeExpr { MethodToken = d.MethodToken, ReturnsRef = d.ReturnsRef, ResultKind = d.ResultKind };
                    for (var i = 0; i < d.Args.Count; i++)
                    {
                        var na = Rewrite(d.Args[i]);
                        if (!ReferenceEquals(na, d.Args[i])) changed = true;
                        nd.Args.Add(na);
                    }
                    return changed ? nd : d;
                }
                case DelegateNewExpr dn:
                {
                    var nt = Rewrite(dn.Target);
                    return ReferenceEquals(nt, dn.Target)
                        ? dn
                        : new DelegateNewExpr { MethodToken = dn.MethodToken, Target = nt, TypeId = dn.TypeId };
                }
                case MarshalUtf8Expr m:
                {
                    var nm = Rewrite(m.Arg);
                    return ReferenceEquals(nm, m.Arg) ? m : new MarshalUtf8Expr { Arg = nm, Utf16 = m.Utf16 };
                }
                case FieldExpr f:
                {
                    var nfo = Rewrite(f.Object);
                    return ReferenceEquals(nfo, f.Object)
                        ? f
                        : new FieldExpr
                        {
                            Object = nfo, FieldCppName = f.FieldCppName, OwnerFullName = f.OwnerFullName,
                            IsRefField = f.IsRefField, IsValueTypeObject = f.IsValueTypeObject,
                        };
                }
                case FieldAddrExpr fa:
                {
                    var nfa = Rewrite(fa.Object);
                    return ReferenceEquals(nfa, fa.Object)
                        ? fa
                        : new FieldAddrExpr { Object = nfa, FieldCppName = fa.FieldCppName, OwnerFullName = fa.OwnerFullName, IsValueTypeObject = fa.IsValueTypeObject };
                }
                default:
                    return e;
            }
        }

        CallExpr RewriteCallExpr(CallExpr c)
        {
            var changed = false;
            var nc = new CallExpr
            {
                Function = c.Function, ReturnsRef = c.ReturnsRef, ResultKind = c.ResultKind,
                Resolved = c.Resolved, IsPInvoke = c.IsPInvoke, SetLastError = c.SetLastError,
            };
            for (var i = 0; i < c.Args.Count; i++)
            {
                var na = Rewrite(c.Args[i]);
                if (!ReferenceEquals(na, c.Args[i])) changed = true;
                nc.Args.Add(na);
            }
            foreach (var si in c.StringArgIndices) nc.StringArgIndices.Add(si);
            return changed ? nc : c;
        }

        var result = new List<Stmt>(stmts.Count);
        foreach (var s in stmts)
        {
            switch (s)
            {
                case AssignStmt a:
                {
                    var nv = Rewrite(a.Value);
                    result.Add(ReferenceEquals(nv, a.Value)
                        ? a
                        : new AssignStmt { Target = a.Target, Value = nv, SourceOffset = a.SourceOffset, SourceOpName = a.SourceOpName });
                    break;
                }
                case ExprStmt es:
                {
                    var nv = Rewrite(es.Value);
                    result.Add(ReferenceEquals(nv, es.Value)
                        ? es
                        : new ExprStmt { Value = nv, SourceOffset = es.SourceOffset, SourceOpName = es.SourceOpName });
                    break;
                }
                case BranchStmt bs:
                {
                    var nc = bs.Condition != null ? Rewrite(bs.Condition) : null;
                    result.Add(ReferenceEquals(nc, bs.Condition)
                        ? bs
                        : new BranchStmt { TargetLabel = bs.TargetLabel, Condition = nc, SourceOffset = bs.SourceOffset, SourceOpName = bs.SourceOpName });
                    break;
                }
                case RetStmt rs:
                {
                    var nv = rs.Value != null ? Rewrite(rs.Value) : null;
                    result.Add(ReferenceEquals(nv, rs.Value)
                        ? rs
                        : new RetStmt { Value = nv, SourceOffset = rs.SourceOffset, SourceOpName = rs.SourceOpName });
                    break;
                }
                case CallStmt cs:
                {
                    var changed = false;
                    var ncs = new CallStmt { Function = cs.Function, Resolved = cs.Resolved, SourceOffset = cs.SourceOffset, SourceOpName = cs.SourceOpName };
                    for (var i = 0; i < cs.Args.Count; i++)
                    {
                        var na = Rewrite(cs.Args[i]);
                        if (!ReferenceEquals(na, cs.Args[i])) changed = true;
                        ncs.Args.Add(na);
                    }
                    result.Add(changed ? ncs : cs);
                    break;
                }
                case FieldAssignStmt fas:
                {
                    var no = Rewrite(fas.Object);
                    var nv = Rewrite(fas.Value);
                    result.Add((ReferenceEquals(no, fas.Object) && ReferenceEquals(nv, fas.Value))
                        ? fas
                        : new FieldAssignStmt
                        {
                            Object = no, FieldCppName = fas.FieldCppName, OwnerFullName = fas.OwnerFullName,
                            IsRefField = fas.IsRefField, IsValueTypeObject = fas.IsValueTypeObject,
                            RefTargetFullName = fas.RefTargetFullName, Value = nv,
                            SourceOffset = fas.SourceOffset, SourceOpName = fas.SourceOpName,
                        });
                    break;
                }
                default:
                    result.Add(s);
                    break;
            }
        }
        return result;
    }

    // Per-method map: local name -> provable constant length of the fresh
    // newarr it holds (built in EmitBody; published for ldelem/stelem
    // emission). The transpiler is single-threaded and sequential, so a
    // static slot is safe; it is reset after every method body.
    private static System.Collections.Generic.Dictionary<string, long> _curNewArrLengths =
        new();
    // P0-1 extension: local -> provable ALLOCATION element size of the fresh
    // newarr it holds (built in the same EmitBody pre-scan as
    // _curNewArrLengths; same single-assignment gate). The raw fast paths
    // emit this LITERAL instead of a `->elemSize` field read (see the
    // pre-scan comment: the C++ optimizer cannot hoist the field load out of
    // an element loop -- the ArrWrite inner loop reloads it per element).
    private static System.Collections.Generic.Dictionary<string, int> _curNewArrElemSizes =
        new();

    // P0-1 loop-pattern extension: the (statement, array local, index local)
    // triples whose ldelem/stelem access is PROVEN in-bounds by the enclosing
    // condition-last for-loop shape (ComputeLoopSafeAccesses), plus the index
    // of the statement currently being emitted (single-threaded sequential
    // emission -- same slot pattern as _curNewArrLengths). A statement index
    // of -1 means "no proof applies" (e.g. the finally-dance emissions).
    private static System.Collections.Generic.HashSet<(int Stmt, string Arr, string Idx)> _curLoopSafeAccesses =
        new();
    private static int _curStmtIndex = -1;
    // perf2 str_chars: local -> known UTF-16 unit count of the string LITERAL
    // it holds (single-assignment StringConstExpr or a pure LocExpr copy of
    // one; built in EmitBody, published for get_Chars direct-read emission --
    // TryEmitLoopProvenStringChars verifies the receiver is a tracked literal
    // local so the loop-proof triple applies to a STRING, not an array).
    private static System.Collections.Generic.Dictionary<string, long> _curStrLiteralLengths =
        new();

    // Per-method map: slot name -> coarse kind ("l" int64_t / "d" double /
    // "i" int32 / "ref" reference / "r" string-object ...). Published by
    // EmitBody (the same locKind the local declarations use) so the binary-op
    // emitter can distinguish int64 operands from int32 -- the IR carries no
    // type on BinaryExpr (b.Kind is never populated), yet the div/shr.un/
    // cgt.un/shift paths MUST NOT truncate a 64-bit operand to 32 bits
    // (bitopcase G1/G2/G3/G5). Reset after every method body.
    private static System.Collections.Generic.Dictionary<string, string> _curLocKinds =
        new();

    // enumparseic/enumdeep: a TryParse out slot declared as a plain int32
    // local carries kind "val" (the default int32 slot) rather than an enum
    // struct C++ type -- its address must be taken directly, never read
    // through ->value__ (which requires an enum struct base).
    private static bool IsInt32Slot(string name) =>
        !_curLocKinds.TryGetValue(name, out var k) || k is "val" or "i" or "int";

    //--------------------------------------------------------------------------
    // P0-1 (hotpath-audit-2): the array-element fast path. When the array is
    // a local PROVABLY holding a fresh constant-length newarr (see the
    // EmitBody pre-scan) and the index is a side-effect-free leaf, emit a raw
    // data-pointer access whose bounds check compares against the CONSTANT
    // newarr length and whose null check is ELIDED (a fresh newarr can never
    // be null). The cold il2cpp_array_fail call keeps out-of-range semantics
    // identical to the checked helper (IndexOutOfRangeException carrier).
    // Returns null when the proof does not apply (fall back to the checked
    // helper -- correctness contract untouched).
    //--------------------------------------------------------------------------
    private static string? TryEmitFastArrayPtr(Expr arr, Expr idx, MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        if (arr is not LocExpr al) return null;
        if (!_curNewArrLengths.TryGetValue(al.Name, out var len)) return null;
        // P0-1 extension: the literal ALLOCATION element size (same value the
        // runtime stores in ->elemSize; see the pre-scan comment) -- the
        // field load is not hoistable out of an element loop by the C++
        // optimizer. Both maps are built together, so a missing entry here
        // means the proof does not apply: fall back conservatively.
        if (!_curNewArrElemSizes.TryGetValue(al.Name, out var elemWidth)) return null;
        // The index expression is emitted MULTIPLE times (bounds check +
        // address + failure call) -- only side-effect-free leaves are safe.
        // batch 159 (bench sort): also accept a LEAF + CONSTANT OFFSET shape
        // (BinaryExpr Add/Sub with one side a leaf LocExpr and the other an
        // int constant) -- the insertion-sort shift loop `a[j+1] = a[j]`
        // indexes with `j + 1`; without this arm the store fell back to the
        // checked helper whose `arr->length` field load is NOT hoistable out
        // of the element loop (the constant-length fast path keeps the
        // compare against the literal newarr length). The offset composes
        // into the bounds compare and the address computation.
        string? idxCode = null;
        long idxOffset = 0;
        switch (idx)
        {
            case LocExpr or IntConstExpr or Int64ConstExpr:
                idxCode = EmitExpr(idx, md, types);
                break;
            case BinaryExpr b when (b.Op == BinaryOp.Add || b.Op == BinaryOp.Sub):
            {
                // leaf [+-] const, or const + leaf (Add only -- Sub is NOT
                // commutative: `99 - i` is not `i + 99`, so a const-left Sub
                // must fall back to the checked helper path).
                Expr leaf;
                long off;
                if (b.Op == BinaryOp.Sub && IsIntConst(b.Right))
                {
                    leaf = b.Left;
                    off = -IntConstValue(b.Right);
                }
                else if (b.Op == BinaryOp.Add && IsIntConst(b.Left))
                {
                    leaf = b.Right;
                    off = IntConstValue(b.Left);
                }
                else if (IsIntConst(b.Right))
                {
                    leaf = b.Left;
                    off = IntConstValue(b.Right);
                }
                else
                {
                    return null;
                }
                if (leaf is not (LocExpr or IntConstExpr or Int64ConstExpr)) return null;
                idxOffset = off;
                idxCode = EmitExpr(leaf, md, types);
                break;
            }
            default:
                return null;
        }
        var arrCode = NullSafePtrCast("Il2CppArray*", EmitExpr(arr, md, types));
        var castIdx = $"static_cast<int32_t>({idxCode})";
        var fullIdx = idxOffset == 0
            ? castIdx
            : $"static_cast<int32_t>({idxCode} + ({idxOffset}LL))";
        return $"(({fullIdx} >= 0 && {fullIdx} < {len}LL) ? {arrCode}->data + static_cast<size_t>({fullIdx}) * static_cast<size_t>({elemWidth}) : (il2cpp::il2cpp_array_fail({arrCode}, {fullIdx}), nullptr))";
    }

    // batch 159 (bench sort): index-offset helpers for the leaf + constant
    // array-index shape (the insertion-sort `a[j+1]` shift loop).
    private static bool IsIntConst(Expr e) => e is IntConstExpr or Int64ConstExpr;

    private static long IntConstValue(Expr e) => e switch
    {
        IntConstExpr ic => ic.Value,
        Int64ConstExpr ic64 => ic64.Value,
        _ => 0,
    };

    //--------------------------------------------------------------------------
    // ComputeLoopSafeAccesses -- P0-1 loop-pattern bounds proof (hotpath-audit-2
    // P0-1 extension). Detects condition-last for-loop shapes in the IR and
    // certifies the ldelem/stelem accesses they contain:
    //
    //     iv = C;               // C >= 0 constant, in the straight-line window
    //                           //   right before the loop (no targeted label)
    //     goto check;           // pre-entry: the FIRST iteration also passes the
    //                           //   guard (the loop body is only entered through
    //                           //   the check label)
    //   body:                   // the ONLY branch into `body` is the backedge
    //     ... a[iv] ...         // certified accesses: after `body`, BEFORE the
    //                           //   increment (the sole other iv assignment)
    //     iv = iv + d;          // d >= 1 constant
    //   check:
    //     if (iv < B) goto body;  // B: constant K > 0, or ldlen(<the same
    //                             //   fresh array local>)
    //
    // Soundness: every path into `body` passes the guard (the pre-entry jumps
    // to `check`, and the backedge IS the guard), so iv < B at body entry; iv
    // is modified only by the increment, which comes AFTER the certified
    // accesses, so iv < B at each access; iv = C + m*d >= 0 (C >= 0, d >= 1).
    // With B <= len(arr) (or B == len of the very array), iv in [0, len(arr)):
    // the bounds compare is statically dead. Any deviation rejects the access
    // and the checked helper stays (correctness contract untouched):
    //   - a goto into `body` from elsewhere (skips the guard);
    //   - a branch into the init window or to `check` from outside the loop
    //     (skips the init -- iv's lower bound would be lost);
    //   - any extra assignment to iv (the count must be exactly 2);
    //   - a non-constant / non-positive bound, or a bound above the length.
    //--------------------------------------------------------------------------
    private static System.Collections.Generic.HashSet<(int Stmt, string Arr, string Idx)> ComputeLoopSafeAccesses(
        List<Stmt> stmts, Dictionary<string, long> newArrLengths,
        Dictionary<string, long>? strLiteralLengths = null)
    {
        var safe = new System.Collections.Generic.HashSet<(int, string, string)>();
        if (stmts.Count == 0) return safe;
        var labelIndex = new Dictionary<string, int>();
        for (var i = 0; i < stmts.Count; i++)
            if (stmts[i] is LabelStmt lb) labelIndex[lb.Name] = i;

        // Candidate loops: one pass per backedge-shaped guard `if (iv < B) goto body`.
        for (var j = 1; j + 1 < stmts.Count; j++)
        {
            if (stmts[j] is not BranchStmt guard || guard.Condition is not BinaryExpr cmp) continue;
            if (cmp.Op != BinaryOp.Clt || cmp.Left is not LocExpr ivE) continue;
            var iv = ivE.Name;
            // Bound: constant K > 0, or ldlen of a fresh array local.
            long? boundLen = null;
            string? boundArr = null;
            switch (cmp.Right)
            {
                case IntConstExpr k when k.Value > 0: boundLen = k.Value; break;
                case CallExpr ld when ld.Function.StartsWith("ldlen_", StringComparison.Ordinal)
                                      && ld.Args.Count == 1 && ld.Args[0] is LocExpr ba:
                    boundArr = ba.Name;
                    break;
                default: continue;
            }
            if (boundLen == null && boundArr == null) continue;
            if (!labelIndex.TryGetValue(guard.TargetLabel, out var b)) continue;
            if (b >= j || stmts[b] is not LabelStmt) continue;
            // The check label directly precedes the guard; the pre-entry jumps
            // to it from right before the body.
            if (stmts[j - 1] is not LabelStmt checkL) continue;
            var checkName = checkL.Name;
            if (b < 1 || stmts[b - 1] is not BranchStmt pre || pre.Condition != null || pre.TargetLabel != checkName)
                continue;
            // Only the backedge may target the body label, and the check label
            // only from the pre-entry (b-1) or body continues (in (b, j)) --
            // anything else could enter the loop without the guard/init.
            var bodyTargets = 0;
            var checkTargetsOk = true;
            for (var t = 0; t < stmts.Count; t++)
            {
                if (stmts[t] is not BranchStmt br) continue;
                if (br.TargetLabel == guard.TargetLabel) bodyTargets++;
                if (br.TargetLabel == checkName && (t < b - 1 || t >= j)) checkTargetsOk = false;
            }
            if (bodyTargets != 1 || !checkTargetsOk) continue;
            // iv: exactly two assignments in the whole method -- the init
            // (constant >= 0, before the loop) and the increment (iv + d, d >= 1)
            // strictly inside the body. No label in the init window may be a
            // branch target (a jump into the window would skip the init).
            var ivAssigns = new List<int>();
            for (var t = 0; t < stmts.Count; t++)
                if (stmts[t] is AssignStmt as2 && as2.Target == iv) ivAssigns.Add(t);
            if (ivAssigns.Count != 2 || ivAssigns[0] >= b || ivAssigns[1] <= b || ivAssigns[1] >= j) continue;
            var initIdx = ivAssigns[0];
            var incIdx = ivAssigns[1];
            if (stmts[initIdx] is not AssignStmt initA || initA.Value is not IntConstExpr c0 || c0.Value < 0) continue;
            var initWindowOk = true;
            for (var t = initIdx + 1; t < b && initWindowOk; t++)
            {
                if (stmts[t] is not LabelStmt wl) continue;
                for (var u = 0; u < stmts.Count; u++)
                    if (stmts[u] is BranchStmt bt && bt.TargetLabel == wl.Name) { initWindowOk = false; break; }
            }
            if (!initWindowOk) continue;
            if (stmts[incIdx] is not AssignStmt incA || incA.Value is not BinaryExpr incB || incB.Op != BinaryOp.Add) continue;
            var incAmt = incB.Left is LocExpr lf && lf.Name == iv ? ConstantValue(incB.Right)
                       : incB.Right is LocExpr lr && lr.Name == iv ? ConstantValue(incB.Left)
                       : null;
            if (incAmt == null || incAmt <= 0) continue;

            // Certify accesses in (b, incIdx) whose index is iv on a tracked
            // array whose length is >= the loop bound, or on a tracked string
            // LITERAL local whose known length is >= the constant loop bound
            // (perf2 str_chars: the string indexer get_Chars(s, i) -- the
            // string is a non-null literal and the proof guarantees i in
            // [0, boundLen) with boundLen <= the literal's length, so the
            // raw UTF-16 read is in-bounds).
            for (var i = b + 1; i < incIdx; i++)
            {
                foreach (var (arrName, idxName) in EnumerateArrayAccesses(stmts[i]))
                {
                    if (idxName != iv) continue;
                    long? knownLen = newArrLengths.TryGetValue(arrName, out var alen)
                        ? alen
                        : strLiteralLengths != null && strLiteralLengths.TryGetValue(arrName, out var slen)
                            ? slen
                            : null;
                    if (knownLen == null) continue;
                    if (boundArr != null ? boundArr == arrName : boundLen <= knownLen)
                        safe.Add((i, arrName, idxName));
                }
            }
        }
        return safe;
    }

    // Enumerate every (arrayLocal, indexLocal) pair of an ldelem/stelem access
    // in a statement -- recursively (an ldelem sits nested in binary exprs,
    // e.g. `sum += a[i] & 0x1F`). Only leaf LocExpr indices are reported (a
    // computed index like `a[i + 1]` is never certified).
    private static IEnumerable<(string Arr, string Idx)> EnumerateArrayAccesses(Stmt s)
    {
        switch (s)
        {
            case AssignStmt a: return EnumerateArrayAccessesIn(a.Value);
            case ExprStmt e: return EnumerateArrayAccessesIn(e.Value);
            case BranchStmt br: return br.Condition == null ? Array.Empty<(string, string)>() : EnumerateArrayAccessesIn(br.Condition);
            case RetStmt r: return r.Value == null ? Array.Empty<(string, string)>() : EnumerateArrayAccessesIn(r.Value);
            case FieldAssignStmt f: return EnumerateArrayAccessesIn(f.Value).Concat(EnumerateArrayAccessesIn(f.Object));
            // stelem is a CallStmt whose ARGS are the raw operands (arr, idx,
            // value) -- the function name carries the opcode (see StackFold's
            // stelem folding); the value arg may still nest an ldelem.
            case CallStmt cs:
                var hits = new List<(string, string)>();
                if ((cs.Function.StartsWith("stelem_", StringComparison.Ordinal)
                     || cs.Function.StartsWith("ldelem_", StringComparison.Ordinal))
                    && cs.Args.Count >= 2 && cs.Args[0] is LocExpr ca && cs.Args[1] is LocExpr ci)
                    hits.Add((ca.Name, ci.Name));
                hits.AddRange(cs.Args.SelectMany(EnumerateArrayAccessesIn));
                return hits;
            default: return Array.Empty<(string, string)>();
        }
    }

    private static IEnumerable<(string Arr, string Idx)> EnumerateArrayAccessesIn(Expr e)
    {
        if (e is CallExpr ce)
        {
            if ((ce.Function.StartsWith("ldelem_", StringComparison.Ordinal)
                 || ce.Function.StartsWith("stelem_", StringComparison.Ordinal))
                && ce.Args.Count >= 2 && ce.Args[0] is LocExpr al && ce.Args[1] is LocExpr il)
            {
                yield return (al.Name, il.Name);
                yield break;
            }
            // perf2 str_chars: the string indexer `s[i]` -- a resolved
            // icall_string_get_chars(s, i) call. Reported as (stringLocal,
            // indexLocal) so the loop-bound proof can certify the access
            // against the string LITERAL local's known length (both leaf
            // LocExprs only -- the raw fast path needs side-effect-free
            // operands).
            if (ce.Resolved?.CppName == "il2cpp::runtime::icall_string_get_chars"
                && ce.Args.Count == 2 && ce.Args[0] is LocExpr sla && ce.Args[1] is LocExpr sil)
            {
                yield return (sla.Name, sil.Name);
                yield break;
            }
            foreach (var a in ce.Args)
                foreach (var hit in EnumerateArrayAccessesIn(a)) yield return hit;
            yield break;
        }
        switch (e)
        {
            case BinaryExpr bx:
                foreach (var hit in EnumerateArrayAccessesIn(bx.Left)) yield return hit;
                foreach (var hit in EnumerateArrayAccessesIn(bx.Right)) yield return hit;
                break;
            case UnaryExpr ux:
                foreach (var hit in EnumerateArrayAccessesIn(ux.Operand)) yield return hit;
                break;
            case MarshalUtf8Expr mu:
                foreach (var hit in EnumerateArrayAccessesIn(mu.Arg)) yield return hit;
                break;
            case VirtualCallExpr vc:
                foreach (var a in vc.Args) foreach (var hit in EnumerateArrayAccessesIn(a)) yield return hit;
                break;
            case DelegateInvokeExpr di:
                foreach (var a in di.Args) foreach (var hit in EnumerateArrayAccessesIn(a)) yield return hit;
                break;
            case FieldExpr fe:
                foreach (var hit in EnumerateArrayAccessesIn(fe.Object)) yield return hit;
                break;
            case FieldAddrExpr fa:
                foreach (var hit in EnumerateArrayAccessesIn(fa.Object)) yield return hit;
                break;
        }
    }

    //--------------------------------------------------------------------------
    // P0-1 loop-pattern extension: raw unchecked access. When the enclosing
    // for-loop proof (ComputeLoopSafeAccesses) certified this exact access --
    // current statement + array local + index local -- the index is in
    // [0, len(arr)) on EVERY path that reaches it: emit the bare data pointer
    // with NO null check and NO bounds compare (both statically dead). Returns
    // null when the proof does not apply (fall back to TryEmitFastArrayPtr,
    // then the checked helper).
    //--------------------------------------------------------------------------
    private static string? TryEmitLoopProvenArrayPtr(Expr arr, Expr idx, MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        if (arr is not LocExpr al || idx is not LocExpr il) return null;
        if (!_curLoopSafeAccesses.Contains((_curStmtIndex, al.Name, il.Name))) return null;
        // P0-1 extension: literal ALLOCATION element size instead of a
        // ->elemSize field read (not hoistable by the C++ optimizer inside an
        // element loop; same value as the runtime field -- see the pre-scan
        // comment). Missing entry = proof does not apply: fall back.
        if (!_curNewArrElemSizes.TryGetValue(al.Name, out var elemWidth)) return null;
        var arrCode = NullSafePtrCast("Il2CppArray*", EmitExpr(arr, md, types));
        var castIdx = $"static_cast<int32_t>({EmitExpr(idx, md, types)})";
        // Parenthesized like the fast-path ternary's success arm: the use site
        // casts the WHOLE expression (`(int32_t*)<ptr>`); without the outer
        // parens the cast would bind to `->data` alone and the `+ offset` would
        // fall outside (pointer arithmetic + non-assignable scalar).
        return $"(({arrCode}->data + static_cast<size_t>({castIdx}) * static_cast<size_t>({elemWidth})))";
    }

    //--------------------------------------------------------------------------
    // perf2 str_chars: the string indexer's raw UTF-16 read. When the enclosing
    // condition-last for-loop proof (ComputeLoopSafeAccesses) certified this
    // exact `s[i]` access -- current statement + string-literal local + index
    // local -- the index is in [0, boundLen) on every path, and the string is a
    // non-null literal of known length >= boundLen: emit the direct char16_t
    // read `((Il2CppString*)s)->chars[i]` with NO icall boundary, NO bounds
    // compare, NO null check (all statically dead under the proof). The
    // receiver must be a tracked string LITERAL local (single-assignment
    // StringConstExpr or copy -- _curStrLiteralLengths membership), which also
    // rules out mistaking an array-local certified triple for a string.
    // Returns null when the proof does not apply (fall back to the checked
    // inline icall -- correctness contract untouched).
    //--------------------------------------------------------------------------
    private static string? TryEmitLoopProvenStringChars(Expr str, Expr idx, MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        if (str is not LocExpr sl || idx is not LocExpr il) return null;
        if (!_curStrLiteralLengths.ContainsKey(sl.Name)) return null;
        if (!_curLoopSafeAccesses.Contains((_curStmtIndex, sl.Name, il.Name))) return null;
        var strPtr = NullSafePtrCast("Il2CppString*", EmitExpr(str, md, types));
        return $"static_cast<int32_t>((({strPtr})->chars)[{EmitExpr(idx, md, types)}])";
    }

    // TypeSpec -> generic instance name (the same encoding as MethodRefResolver/StackFold).
    private static string GenericInstantiationName(MetadataReader md, TypeSpecificationHandle handle)
    {
        var spec = md.GetTypeSpecification(handle);
        var sig = md.GetBlobReader(spec.Signature);
        var header = sig.ReadByte();
        if (header != 0x15)
            throw new NotSupportedException($"TypeSpec signature byte 0x{header:X2} not GenericInst(0x15) in M3");
        // GenericInst definition: ELEMENT_TYPE_CLASS(0x12)/VALUETYPE(0x11)/
        // TypeSpec(0x1B) + TypeDefOrRef coded index (Roslyn references the
        // generic definition via CLASS).
        var defType = sig.ReadByte();
        _ = defType; // consumed; M3 accepts CLASS/VALUETYPE
        EntityHandle defHandle = sig.ReadTypeHandle();
        string defName = defHandle.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)defHandle),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)defHandle),
            HandleKind.TypeSpecification => GenericInstantiationName(md, (TypeSpecificationHandle)defHandle),
            _ => throw new NotSupportedException($"generic definition kind {defHandle.Kind} in M3"),
        };
        var argCount = sig.ReadCompressedInteger();
        var args = new string[argCount];
        for (var i = 0; i < argCount; ++i)
        {
            var b = sig.ReadByte();
            args[i] = ReadM3Arg(md, ref sig, b);
        }
        GenericUsageAnalyzer.CollapseInstantiation(defName, args); // P-G2 shared collapse
        return defName + "_" + string.Join("_", args);
    }

    // The M3 generic-ARGUMENT byte table (0x15 = a NESTED generic
    // instantiation, e.g. IEnumerable`1<KeyValuePair`2<string,int>> --
    // recurse so the outer instance name carries the nested suffix).
    // ECMA-335 II.23.1.16: custom modifiers (0x1F/0x20) are stripped first.
    private static string ReadM3Arg(MetadataReader md, ref BlobReader sig, byte b)
    {
        b = MethodSignatureCoder.SkipCustomModifiers(ref sig, b);
        return b switch
        {
            0x02 => "bool",
            0x03 => "char",
            0x04 => "sbyte",
            0x05 => "byte",
            0x06 => "short",
            0x07 => "ushort",
            0x08 => "int",
            0x09 => "uint",
            0x0A => "long",
            0x0B => "ulong",
            0x0C => "float",
            0x0D => "double",
            0x0E => "string",
             0x11 => MethodSignatureCoder.ResolveM3ArgTypeName(md, ref sig),
             0x12 => MethodSignatureCoder.ResolveM3ArgTypeName(md, ref sig),
            0x1C => "object",
            0x1D => "array",
            0x15 => ReadM3NestedGeneric(md, ref sig),
            // B2/B5 family (MyList<T> definition bodies decoded at emit
            // time, e.g. callvirt owner resolution): an OPEN TypeSpec arg
            // (VAR 0x13 + index) denotes the enclosing closed clone's T --
            // the R2b P2 InstantiationSuffixHint mechanism (StackFold's and
            // MethodRefResolver's arms are the precedent). Outside a body
            // pass the hint is null and the historical "int" holds.
            0x13 => ReadM3VarArgCG(ref sig),
            var other => throw new NotSupportedException($"generic argument byte 0x{other:X2} in M3"),
        };
    }

    // VAR (0x13) generic-argument consumption: the index byte follows.
    private static string ReadM3VarArgCG(ref BlobReader sig)
    {
        sig.ReadCompressedInteger();
        return MethodRefResolver.InstantiationSuffixHint ?? "int";
    }

    private static string ReadM3NestedGeneric(MetadataReader md, ref BlobReader sig)
    {
        _ = sig.ReadByte(); // CLASS(0x12)/VALUETYPE(0x11) marker
        EntityHandle nestedDef = sig.ReadTypeHandle();
        string nestedName = nestedDef.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)nestedDef),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)nestedDef),
            HandleKind.TypeSpecification => GenericInstantiationName(md, (TypeSpecificationHandle)nestedDef),
            _ => throw new NotSupportedException($"nested generic definition kind {nestedDef.Kind} in M3"),
        };
        var nestedCount = sig.ReadCompressedInteger();
        var nestedArgs = new string[nestedCount];
        for (var j = 0; j < nestedCount; ++j)
            nestedArgs[j] = ReadM3Arg(md, ref sig, sig.ReadByte());
        return nestedName + "_" + string.Join("_", nestedArgs);
    }

    // token -> the method's generated C++ name (MethodDef or MemberReference, including the generic-definition-name hash).
    private static string MethodCppNameFromToken(int token, MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        var resolved = MethodRefResolver.Resolve(md, token, types);
        return resolved.CppName;
    }

    // CG6: the SHORT method name of a delegate-target token (the runtime
    // registry value for Delegate.Method.Name). MethodDef/MemberReference only;
    // anything else is not a callable method and yields null (no registration).
    private static string? DelegateMethodShortName(int token, MetadataReader md)
    {
        var handle = MetadataTokens.Handle(token);
        return handle.Kind switch
        {
            HandleKind.MethodDefinition => md.GetString(md.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
            HandleKind.MemberReference => md.GetString(md.GetMemberReference((MemberReferenceHandle)handle).Name),
            _ => null,
        };
    }

        // Delegate Invoke function signature (return + parameter C++ types).
        // BUG-1 (batch 172): the retCpp contract drives the multicast routing
        // below. Every value/void shape that the multicast icalls can express
        // must resolve to one of the routed values -- "int32_t" (i4 ABI, void
        // or value return) or "void" (rr_void/r_void event ABI) -- so no
        // multicast-capable delegate ever falls through to the raw methodPtr
        // dispatch (which reads a multicast container's `first` field as code
        // -> 0xC0000005).
        //
        // P-D2 #1 (delegate signature generalization): the decode is FULL-WIDTH
        // now. The old 2-class classification (Il2CppObject* vs int32_t)
        // silently collapsed int64/double/float params AND returns to int32_t,
        // which (a) routed `Func<long>` / `Func<int,double>` single-cast
        // delegates onto the i4 multicast icall -- the runtime called the leaf
        // through an int32_t ABI, reading EAX (int64 return truncated) or
        // passing a torn int32 slot into an int64/double param -- and (b) left
        // the leading int64/double shapes uncallable as multicast. The C++ type
        // of every param/return now matches the leaf method's REAL generated
        // ABI (ParamTypesCpp / ReturnTypeCpp -- the SAME mapping the #1
        // multicast helper cast table uses, so the helper and the leaf always
        // agree). Value-type / fnptr / pointer / TypeHandle / byref shapes
        // LOUD-reject (the old default->int32_t silently tore them).
        private static (string Ret, string[] Params) DelegateSignature(MetadataReader md, int token)
        {
            // BUG-1: a GENERIC delegate (System.Action<T>, Func<T,U>, ...) has
            // an Invoke MemberRef whose parent is a TypeSpec carrying the
            // instantiation args. The raw signature decodes those params as
            // generic VAR (M3 T->int) -- wrong for Action<string> (the param IS
            // a managed ref), which would silently route the i4 multicast path.
            // Substitute the TypeSpec's generic args before classifying.
            var handle = MetadataTokens.Handle(token);
            if (handle.Kind == HandleKind.MemberReference)
            {
                var mr = md.GetMemberReference((MemberReferenceHandle)handle);
                if (mr.Parent.Kind == HandleKind.TypeSpecification)
                    return DelegateSignatureGeneric(md, (TypeSpecificationHandle)mr.Parent, mr.Signature);
                // P-D2 #1: a NON-typeSpec MemberRef Invoke (BCL non-generic --
                // EventHandler.Invoke(object, EventArgs)) decodes straight from
                // its signature blob (replaces the Resolve-based 2-class path).
                return DelegateSignatureBlob(md, mr.Signature, Array.Empty<byte>());
            }
            if (handle.Kind == HandleKind.MethodDefinition)
            {
                // Custom delegate Invoke MethodDef (Roslyn: MethodImplAttributes
                // Runtime, no IL body -- the transpiler skips emission but must
                // decode its signature for the routing decision below).
                var m = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                return DelegateSignatureBlob(md, m.Signature, Array.Empty<byte>());
            }
            throw new NotSupportedException($"delegate Invoke token kind {handle.Kind} not supported in P-D2 #1 signature decode");
        }

        // P-D2 #1: decode a raw delegate Invoke signature blob into the
        // full-width leaf-ABI C++ types (see DelegateSignature surviving retCpp
        // contract -- the void + 0-param shape keeps the int32 ABI so the i4
        // multicast route stays multicast-capable).
        private static (string Ret, string[] Params) DelegateSignatureBlob(MetadataReader md,
            BlobHandle sigHandle, byte[] genArgs)
        {
            var sig = md.GetBlobReader(sigHandle);
            var header = sig.ReadSignatureHeader();
            if (header.IsGeneric) sig.ReadCompressedInteger(); // Invoke is never generic; kept for alignment
            var paramCount = sig.ReadCompressedInteger();
            var retCpp = ConsumeDelegateSigFull(md, ref sig, genArgs, isReturn: true, out var retIsVoid);
            var paramCpp = new string[paramCount];
            for (var i = 0; i < paramCount; ++i)
                paramCpp[i] = ConsumeDelegateSigFull(md, ref sig, genArgs, isReturn: false, out _);
            // FIX-162b: a void-returning ZERO-ARG delegate stays on the int32
            // ABI (multicast-capable via the i4 icall; the void return is
            // discarded by the caller's void context).
            if (retIsVoid && paramCount == 0) retCpp = "int32_t";
            return (retCpp, paramCpp);
        }

        // BUG-1: a generic delegate's Invoke signature (void Invoke(T) etc.)
        // carries generic VAR/MVAR params that MUST be substituted with the
        // TypeSpec's instantiation args (Action<string> -> the param is a
        // managed ref; Action<int> -> int32). Decodes the Invoke blob with the
        // TypeSpec generic args as the substitution table. P-D2 #1: the decode
        // is FULL-WIDTH (the genArgs bytes map to the leaf-ABI C++ types via
        // ConsumeDelegateSigByte), so `Func<long,int>` / `Func<int,double>`
        // no longer collapse to int32_t.
        private static (string Ret, string[] Params) DelegateSignatureGeneric(MetadataReader md,
            TypeSpecificationHandle parent, BlobHandle sigHandle)
        {
            var genArgs = DelegateTypeSpecGenericArgs(md, parent);
            return DelegateSignatureBlob(md, sigHandle, genArgs);
        }

        // BUG-1: read a generic delegate TypeSpec's instantiation args (the
        // GenericInst blob: 0x15 marker, CLASS/VALUETYPE byte, definition
        // coded index, arg count, then one type per arg). Returns the leading
        // type byte of each arg with full trailing-payload consumption.
        private static byte[] DelegateTypeSpecGenericArgs(MetadataReader md, TypeSpecificationHandle handle)
        {
            var spec = md.GetTypeSpecification(handle);
            var sig = md.GetBlobReader(spec.Signature);
            if (sig.ReadByte() != 0x15)
                throw new NotSupportedException($"delegate TypeSpec signature not GenericInst(0x15) in BUG-1 decode");
            sig.ReadByte(); // CLASS/VALUETYPE marker
            _ = sig.ReadTypeHandle(); // generic definition coded index
            var count = sig.ReadCompressedInteger();
            var args = new byte[count];
            for (var i = 0; i < count; ++i)
                args[i] = ConsumeDelegateTypeByte(md, ref sig);
            return args;
        }

        // Consume one type byte + trailing payload (CLASS/VALUETYPE handle,
        // SZARRAY/PTR/BYREF element, GenericInst args, VAR/MVAR index, multidim
        // array bounds, fnptr inner signature); return the leading byte.
        private static byte ConsumeDelegateTypeByte(MetadataReader md, ref BlobReader sig)
        {
            // ECMA-335 II.23.1.16: strip custom modifiers (0x1F/0x20) first.
            var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
            switch (b)
            {
                case 0x10: return ConsumeDelegateTypeByte(md, ref sig); // BYREF: element follows
                case 0x11 or 0x12 or 0x40: _ = sig.ReadTypeHandle(); break;
                case 0x1D or 0x1F: _ = ConsumeDelegateTypeByte(md, ref sig); break; // SZARRAY element
                case 0x13 or 0x1E: _ = sig.ReadCompressedInteger(); break;          // VAR/MVAR index
                case 0x15:                                                        // GenericInst
                    sig.ReadByte();
                    _ = sig.ReadTypeHandle();
                    var n = sig.ReadCompressedInteger();
                    for (var i = 0; i < n; i++) _ = ConsumeDelegateTypeByte(md, ref sig);
                    break;
                case 0x14:                                                        // multidim ARRAY
                    _ = ConsumeDelegateTypeByte(md, ref sig);
                    _ = sig.ReadCompressedInteger(); // rank
                    var sizes = sig.ReadCompressedInteger();
                    for (var i = 0; i < sizes; i++) _ = sig.ReadCompressedInteger();
                    var lob = sig.ReadCompressedInteger();
                    for (var i = 0; i < lob; i++) _ = sig.ReadCompressedInteger();
                    break;
                case 0x0F: _ = ConsumeDelegateTypeByte(md, ref sig); break;        // PTR pointee
                case 0x1B:                                                        // FNPTR
                    sig.ReadByte(); // calling convention
                    var nf = sig.ReadCompressedInteger();
                    _ = ConsumeDelegateTypeByte(md, ref sig); // return type
                    for (var i = 0; i < nf; i++) _ = ConsumeDelegateTypeByte(md, ref sig);
                    break;
            }
            return b;
        }

        // P-D2 #1: decode ONE delegate Invoke signature type (return or param)
        // with generic VAR/MVAR substitution into the leaf-ABI C++ type the
        // generated method declares (ParamTypesCpp / ReturnTypeCpp -- the SAME
        // mapping the #1 multicast helper cast table must use, otherwise the
        // cast table and the leaf declaration misalign = silent ABI tearing).
        // The type byte and its trailing payload are consumed. Unsupported
        // shapes (value types 0x11, fnptr 0x1B, pointers 0x0F, TypeHandle 0x40,
        // byref 0x10, typedref 0x16) LOUD-reject -- the old default->int32_t
        // silently tore them.
        private static string ConsumeDelegateSigFull(MetadataReader md, ref BlobReader sig,
            byte[] genArgs, bool isReturn, out bool isVoid)
        {
            // ECMA-335 II.23.1.16: strip custom modifiers (0x1F/0x20) first.
            var b = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
            isVoid = false;
            switch (b)
            {
                case 0x01: isVoid = true; return "void";
                case 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09:
                    return "int32_t";                          // bool/char/sbyte/byte/short/ushort/int/uint (int32 slots)
                case 0x0A or 0x0B: return "int64_t";           // long/ulong
                case 0x0C:
                    // FLOAT-ASYMMETRY (P-D2 #1): a float RETURN is float
                    // (ReturnTypeCpp 0x0C -> float) but a float PARAMETER
                    // arrives as double (ParamTypesCpp "f" -> double -- the
                    // leaf's generated parameter declares double). The helper
                    // cast table must use the same width or the leaf ABI tears.
                    return isReturn ? "float" : "double";
                case 0x0D: return "double";
                case 0x0E: return "Il2CppObject*";             // STRING
                case 0x12: _ = sig.ReadTypeHandle(); return "Il2CppObject*"; // CLASS
                case 0x1C: return "Il2CppObject*";             // OBJECT
                case 0x1D or 0x1F: _ = ConsumeDelegateSigFull(md, ref sig, genArgs, isReturn, out _); return "Il2CppObject*"; // SZARRAY
                case 0x13 or 0x1E:                            // VAR/MVAR -> instantiation arg
                {
                    var idx = sig.ReadCompressedInteger();
                    if (idx < genArgs.Length)
                        return ConsumeDelegateSigByte(md, genArgs[idx], isReturn, out isVoid);
                    return "int32_t"; // unresolved VAR -> value (M3 T->int convention)
                }
                case 0x15:                                    // GenericInst
                {
                    // The marker byte (0x11 VALUETYPE / 0x12 CLASS) decides:
                    // a VALUETYPE generic-instantiation is a struct value -- not
                    // expressible in the helper -- LOUD-reject.
                    var marker = sig.ReadByte();
                    _ = sig.ReadTypeHandle();
                    var n = sig.ReadCompressedInteger();
                    for (var i = 0; i < n; i++) _ = ConsumeDelegateSigFull(md, ref sig, genArgs, isReturn, out _);
                    if (marker == 0x11)
                        throw new NotSupportedException("delegate Invoke with a VALUETYPE generic-instantiation parameter/return is not supported: the multicast helper cannot express a struct value");
                    return "Il2CppObject*";
                }
                case 0x14:                                    // multidim ARRAY (ref)
                    _ = ConsumeDelegateSigFull(md, ref sig, genArgs, isReturn, out _);
                    _ = sig.ReadCompressedInteger();
                    var sizes = sig.ReadCompressedInteger();
                    for (var i = 0; i < sizes; i++) _ = sig.ReadCompressedInteger();
                    var lob = sig.ReadCompressedInteger();
                    for (var i = 0; i < lob; i++) _ = sig.ReadCompressedInteger();
                    return "Il2CppObject*";
                case 0x18: return "intptr_t";                  // native int (ELEMENT_TYPE_I)
                case 0x11:                                     // VALUETYPE
                    _ = sig.ReadTypeHandle();
                    throw new NotSupportedException("delegate Invoke with a VALUETYPE (0x11) parameter/return is not supported: the multicast helper cannot express a struct value (the old decode silently collapsed it to int32_t)");
                case 0x40:                                     // TypeHandle
                    _ = sig.ReadTypeHandle();
                    throw new NotSupportedException("delegate Invoke with a TypeHandle (0x40) parameter/return is not supported: the multicast helper cannot express it");
                case 0x0F:                                     // PTR
                    _ = ConsumeDelegateSigFull(md, ref sig, genArgs, isReturn, out _);
                    throw new NotSupportedException("delegate Invoke with an unmanaged-pointer (0x0F) parameter/return is not supported: the multicast helper cannot express it");
                case 0x1B:                                     // FNPTR
                    sig.ReadByte();
                    var nf = sig.ReadCompressedInteger();
                    _ = ConsumeDelegateSigFull(md, ref sig, genArgs, isReturn, out _);
                    for (var i = 0; i < nf; i++) _ = ConsumeDelegateSigFull(md, ref sig, genArgs, isReturn, out _);
                    throw new NotSupportedException("delegate Invoke with a function-pointer (0x1B) parameter/return is not supported: the multicast helper cannot express it");
                case 0x10:                                     // BYREF (ref/out param)
                    _ = ConsumeDelegateSigFull(md, ref sig, genArgs, isReturn, out _);
                    throw new NotSupportedException("delegate Invoke with a byref (0x10) parameter/return is not supported: the multicast helper cannot express the address ABI");
                case 0x16:                                     // TYPEDREF
                    throw new NotSupportedException("delegate Invoke with a TypedReference (0x16) parameter/return is not supported");
                default:
                    throw new NotSupportedException($"delegate Invoke signature type byte 0x{b:X2} not supported in P-D2 #1 decode");
            }
        }

        // P-D2 #1: map a TypeSpec generic-argument LEADING byte (from
        // DelegateTypeSpecGenericArgs) to the leaf-ABI C++ type, mirroring
        // ConsumeDelegateSigFull for the bytes the substitution can carry.
        // (The generic-arg byte carries no trailing payload -- a CLASS/VALUETYPE
        // arg is stored as its bare marker byte.)
        private static string ConsumeDelegateSigByte(MetadataReader md, byte byteCode, bool isReturn,
            out bool isVoid)
        {
            isVoid = false;
            switch (byteCode)
            {
                case 0x01: isVoid = true; return "void";
                case 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 or 0x13 or 0x1E:
                    return "int32_t";
                case 0x0A or 0x0B: return "int64_t";
                case 0x0C: return isReturn ? "float" : "double";
                case 0x0D: return "double";
                case 0x0E or 0x12 or 0x14 or 0x15 or 0x1C or 0x1D or 0x1F: return "Il2CppObject*";
                case 0x18: return "intptr_t";
                case 0x11:
                    throw new NotSupportedException("delegate Invoke with a VALUETYPE generic argument is not supported: the multicast helper cannot express a struct value");
                case 0x40:
                    throw new NotSupportedException("delegate Invoke with a TypeHandle generic argument is not supported: the multicast helper cannot express it");
                case 0x0F:
                    throw new NotSupportedException("delegate Invoke with an unmanaged-pointer generic argument is not supported");
                case 0x1B:
                    throw new NotSupportedException("delegate Invoke with a function-pointer generic argument is not supported");
                case 0x10:
                    throw new NotSupportedException("delegate Invoke with a byref generic argument is not supported");
                case 0x16:
                    throw new NotSupportedException("delegate Invoke with a TypedReference generic argument is not supported");
                default:
                    throw new NotSupportedException($"delegate Invoke generic argument type byte 0x{byteCode:X2} not supported in P-D2 #1 decode");
            }
        }

        // P-D2 #1: register (dedupe) + return the per-signature multicast helper
        // name for a non-4-shape delegate Invoke. The NAME is the FNV-1a hash
        // over the full-width (retCpp, paramCpp) signature -- deterministic
        // across TUs, so every call site AND the generated.h definition agree.
        private static string DelegateMulticastHelperName(string retCpp, string[] paramCpp)
        {
            var hash = SigHash.Hash(retCpp + "(" + string.Join(",", paramCpp) + ")");
            var name = $"il2cpp_delegate_multicast_{hash}";
            DelegateHelperDefs.Add(DelegateMulticastHelperDef(retCpp, paramCpp, name));
            return name;
        }

        // P-D2 #1: build the file-scope inline multicast helper body for ONE
        // distinct delegate Invoke signature (see DelegateHelperDefs). The
        // helper uses ONLY the exported runtime API:
        //   * il2cpp_null_check(list) FIRST -- icall_delegate_get_invocation_list
        //     returns nullptr for null input (delegateext.cpp:311-312), so the
        //     .NET NullReferenceException must be raised before the flatten;
        //   * icall_delegate_get_invocation_list(list) -- the exported flatten
        //     (returns the leaf delegates as a reference array);
        //   * il2cpp_array_get_length / il2cpp_array_get_elem_ptr -- walk the
        //     leaves (the generated-code array access pair).
        // Every leaf is reinterpreted to Il2CppDelegate* and dispatched by
        // binding: target != null -> (ret(*)(Il2CppObject*, params))methodPtr
        // (target, args); target == null -> (ret(*)(params))methodPtr (args) --
        // the SAME per-leaf rule the 4 runtime fast-path icalls use. The LAST
        // leaf's result wins (.NET multicast return-value semantics). Param and
        // return C++ types are the FULL-WIDTH leaf ABI (float RETURN = float,
        // float PARAM = double -- the P-D2 #1 asymmetry).
        private static string DelegateMulticastHelperDef(string retCpp, string[] paramCpp, string name)
        {
            var sb = new System.Text.StringBuilder();
            var paramDecls = paramCpp.Length == 0
                ? "Il2CppObject* list"
                : "Il2CppObject* list, " + string.Join(", ", paramCpp.Select((t, i) => $"{t} p{i}"));
            var leafStaticSig = string.Join(", ", paramCpp);
            var leafThisSig = string.Join(", ", new[] { "Il2CppObject*" }.Concat(paramCpp));
            var callArgs = string.Join(", ", paramCpp.Select((_, i) => $"p{i}"));
            var zero = retCpp == "Il2CppObject*" ? "nullptr" : "0";
            var hasRet = retCpp != "void";
            var head = hasRet
                ? "        __res = "
                : "";
            var instCall = $"reinterpret_cast<{retCpp}(*)({leafThisSig})>(__leaf->methodPtr)(__leaf->target{(callArgs.Length > 0 ? ", " + callArgs : "")})";
            var statCall = $"reinterpret_cast<{retCpp}(*)({leafStaticSig})>(__leaf->methodPtr)({callArgs})";
            sb.AppendLine($"inline {retCpp} {name}({paramDecls})");
            sb.AppendLine("{");
            sb.AppendLine("    Il2CppArray* __dels = reinterpret_cast<Il2CppArray*>(il2cpp::runtime::icall_delegate_get_invocation_list(il2cpp_null_check(list)));");
            if (hasRet) sb.AppendLine($"    {retCpp} __res = {zero};");
            sb.AppendLine("    for (int32_t __i = 0; __i < il2cpp::il2cpp_array_get_length(__dels); ++__i)");
            sb.AppendLine("    {");
            sb.AppendLine("        Il2CppDelegate* __leaf = reinterpret_cast<Il2CppDelegate*>(*reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(__dels, __i)));");
            sb.AppendLine("        if (__leaf->target != nullptr)");
            sb.AppendLine($"            {head}{instCall};");
            sb.AppendLine("        else");
            sb.AppendLine($"            {head}{statCall};");
            sb.AppendLine("    }");
            if (hasRet) sb.AppendLine("    return __res;");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // P-D2 #2: build ONE GetFunctionPointerForDelegate instance-binding
        // module (see GfpModuleDefs) for a distinct delegate Invoke signature.
        // Runtime primitives (all EXPORTED -- no runtime source change):
        //   * il2cpp_null_check                -- NRE on null (delegateext NRE contract)
        //   * icall_delegate_get_invocation_list + il2cpp_array_get_length/
        //     il2cpp_array_get_elem_ptr        -- multicast detection (length > 1)
        //   * Il2CppDelegate { methodPtr, target } -- the per-leaf binding
        //   * il2cpp_gc_add_static_root        -- PERMANENT strong root (the slot
        //     holds the delegate; a native-held thunk pointer stays valid forever)
        // Per-slot THUNK: unique function per slot (the native side stores the
        // thunk ADDRESS, so each slot needs a distinct, stable function). The
        // slot array is a function-local static of an inline function -- ONE
        // shared instance across every translation unit (a delegate asked for its
        // pointer in any method of any TU returns the SAME thunk address; the
        // collector rewrites the root slot under compaction, so the thunk always
        // reads the fresh address).
        private static string DelegateGfpModuleDef(string retCpp, string[] paramCpp, string hash)
        {
            const int kMax = 64;
            var sb = new System.Text.StringBuilder();
            var paramDecls = string.Join(", ", paramCpp.Select((t, i) => $"{t} p{i}"));
            var leafStaticSig = string.Join(", ", paramCpp);
            var leafThisSig = string.Join(", ", new[] { "Il2CppObject*" }.Concat(paramCpp));
            var callArgs = string.Join(", ", paramCpp.Select((_, i) => $"p{i}"));
            var hasRet = retCpp != "void";
            var ret = hasRet
                ? "return "
                : "";
            sb.AppendLine($"constexpr int32_t il2cpp_gfp_kMax_{hash} = {kMax};");
            sb.AppendLine($"inline Il2CppObject** il2cpp_gfp_slots_{hash}()");
            sb.AppendLine("{");
            sb.AppendLine($"    static Il2CppObject* s[il2cpp_gfp_kMax_{hash}] = {{ }};");
            sb.AppendLine("    return s;");
            sb.AppendLine("}");
            for (var slot = 0; slot < kMax; slot++)
            {
                sb.AppendLine($"inline {retCpp} il2cpp_gfp_thunk_{hash}_{slot}({paramDecls})");
                sb.AppendLine("{");
                sb.AppendLine($"    Il2CppDelegate* __leaf = reinterpret_cast<Il2CppDelegate*>(il2cpp_gfp_slots_{hash}()[{slot}]);");
                sb.AppendLine("    if (__leaf->target != nullptr)");
                sb.AppendLine($"        {ret}reinterpret_cast<{retCpp}(*)({leafThisSig})>(__leaf->methodPtr)(__leaf->target{(callArgs.Length > 0 ? ", " + callArgs : "")});");
                sb.AppendLine("    else");
                sb.AppendLine($"        {ret}reinterpret_cast<{retCpp}(*)({leafStaticSig})>(__leaf->methodPtr)({callArgs});");
                sb.AppendLine("}");
            }
            sb.AppendLine($"inline intptr_t il2cpp_gfp_thunk_addr_{hash}(int32_t __i)");
            sb.AppendLine("{");
            sb.AppendLine("    switch (__i)");
            sb.AppendLine("    {");
            for (var slot = 0; slot < kMax; slot++)
                sb.AppendLine($"        case {slot}: return reinterpret_cast<intptr_t>(&il2cpp_gfp_thunk_{hash}_{slot});");
            sb.AppendLine("        default:");
            sb.AppendLine("            std::fprintf(stderr, \"il2cpp_delegate_gfp: invalid thunk slot %d\\n\", static_cast<int>(__i));");
            sb.AppendLine("            std::abort();");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine($"inline intptr_t il2cpp_delegate_gfp_{hash}(Il2CppObject* d)");
            sb.AppendLine("{");
            sb.AppendLine("    d = il2cpp_null_check(d);");
            sb.AppendLine("    Il2CppArray* __inv = reinterpret_cast<Il2CppArray*>(il2cpp::runtime::icall_delegate_get_invocation_list(d));");
            sb.AppendLine("    if (il2cpp::il2cpp_array_get_length(__inv) > 1)");
            sb.AppendLine("    {");
            sb.AppendLine("        il2cpp::il2cpp_throw_new(\"System.InvalidOperationException,System.Exception\", \"A multicast delegate has no single native function pointer.\");");
            sb.AppendLine("    }");
            sb.AppendLine("    Il2CppDelegate* __leaf = reinterpret_cast<Il2CppDelegate*>(*reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(__inv, 0)));");
            sb.AppendLine("    if (__leaf->target == nullptr)");
            sb.AppendLine("        return reinterpret_cast<intptr_t>(__leaf->methodPtr);");
            sb.AppendLine($"    Il2CppObject** __slots = il2cpp_gfp_slots_{hash}();");
            sb.AppendLine($"    for (int32_t __i = 0; __i < il2cpp_gfp_kMax_{hash}; ++__i)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (__slots[__i] != nullptr)");
            sb.AppendLine("        {");
            sb.AppendLine("            Il2CppDelegate* __bound = reinterpret_cast<Il2CppDelegate*>(__slots[__i]);");
            sb.AppendLine("            if (__bound->methodPtr == __leaf->methodPtr && __bound->target == __leaf->target)");
            sb.AppendLine($"                return il2cpp_gfp_thunk_addr_{hash}(__i);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine($"    for (int32_t __i = 0; __i < il2cpp_gfp_kMax_{hash}; ++__i)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (__slots[__i] == nullptr)");
            sb.AppendLine("        {");
            sb.AppendLine("            __slots[__i] = d;");
            sb.AppendLine("            il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&__slots[__i]));");
            sb.AppendLine($"            return il2cpp_gfp_thunk_addr_{hash}(__i);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine($"    std::fprintf(stderr, \"il2cpp_delegate_gfp_{hash}: instance-binding slot pool overflow ({kMax})\\n\");");
            sb.AppendLine("    std::abort();");
            sb.AppendLine("}");
            return sb.ToString();
        }

    // Whether a method token is a REAL value-type ctor (.ctor whose declaring
    // type is a user struct / DateTime / TimeSpan / Decimal). The local-kind
    // scan calls this to avoid mis-typing a by-ref P/Invoke arg
    // (GetMessageW(out MSG msg) has a LocAddrExpr first arg but is NOT a ctor).
    private static bool IsValueTypeCtorToken(MetadataReader md, int token, IReadOnlyList<TypeDef> types)
    {
        var handle = MetadataTokens.Handle(token);
        string ownerFullName;
        string methodName;
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            var m = md.GetMethodDefinition((MethodDefinitionHandle)handle);
            methodName = md.GetString(m.Name);
            ownerFullName = md.GetFullName(m.GetDeclaringType());
        }
        else if (handle.Kind == HandleKind.MemberReference)
        {
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            methodName = md.GetString(mr.Name);
            ownerFullName = mr.Parent.Kind switch
            {
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)mr.Parent),
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)mr.Parent),
                _ => "?",
            };
        }
        else
        {
            return false;
        }
        if (methodName != ".ctor") return false;
        // G7: an IntPtr/UIntPtr ctor-initialized local (`ldloca loc;
        // <value>; call .ctor`) declares the full-width carrier via
        // TypeCppName (intptr_t/uintptr_t). Roslyn encodes these locals
        // as native int/uint, whose coarse IL local kind ("i") would
        // otherwise declare an int32_t slot and truncate the value.
        return ownerFullName is "System.DateTime" or "System.TimeSpan" or "System.Decimal" or "System.Guid"
            or "System.IntPtr" or "System.UIntPtr"
            || types.Any(t => t.FullName == ownerFullName && t.IsValueType);
    }

    // Value-type ctor token -> the declaring type's C++ name ("Calc.Point" -> Point).
    private static string ValueTypeCtorOwnerCpp(MetadataReader md, int token, IReadOnlyList<TypeDef> types)
    {
        var handle = MetadataTokens.Handle(token);
        string ownerFullName;
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            var m = md.GetMethodDefinition((MethodDefinitionHandle)handle);
            ownerFullName = md.GetFullName(m.GetDeclaringType());
        }
        else
        {
            var mr = md.GetMemberReference((MemberReferenceHandle)handle);
            ownerFullName = mr.Parent.Kind switch
            {
                HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)mr.Parent),
                HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)mr.Parent),
                _ => "?",
            };
        }
        var t = types.FirstOrDefault(x => x.FullName == ownerFullName);
        // M5 wiring: unknown owners (BCL value types like System.DateTime) go
        // through the CodeGenerator TypeCppName mapping -- System.DateTime ->
        // il2cpp::runtime::Il2CppDateTime, System.TimeSpan -> Il2CppTimeSpan.
        return t != null ? TypeEmitter.TypeCppName(t) : TypeCppName(ownerFullName);
    }

    // M7 CG-FIX (boxing/interface inline-arg typing): whether the declaring
    // type of a method token is a USER value type (struct). Value-type
    // instance methods pass `this` BY ADDRESS ({T}*) -- not a GC reference --
    // so the EmitBody arg0="ref" seed must skip them (a struct this must stay
    // the value-type pointer). BCL value types (DateTime/TimeSpan/Decimal)
    // are handled by TypeCppName, not the reference seed, so only USER
    // TypeDefs with IsValueType count.
    private static bool MethodOwnerIsValueType(MetadataReader md, int token, IReadOnlyList<TypeDef> types)
    {
        var handle = MetadataTokens.Handle(token);
        if (handle.Kind != HandleKind.MethodDefinition) return false;
        var ownerFullName = md.GetFullName(md.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType());
        return types.Any(t => t.FullName == ownerFullName && t.IsValueType);
    }

    // P/Invoke return type (M4: int32_t; M4.5: IntPtr/UIntPtr returns -> full-width pointer -- an
    // int32_t declaration truncates a 64-bit pointer return on x64 (pinvokeext reverse_static's
    // signature blob verified: the return byte 0x18 = SignatureTypeCode.IntPtr, so the original
    // `extern "C" int32_t reverse_static(const char*);` only took the low 32 address bits).
    // wfm2 (struct returns): a VALUETYPE struct return (V:<FullName>) declares the native
    // struct type (il2cpp_pinvoke_struct.h) -- the native callee returns the struct by value.
    private static string PinvokeReturnTypeCpp(MetadataReader md, MethodDef m)
    {
        if (m.ReturnType is SignatureTypeCode.IntPtr) return "intptr_t";
        if (m.ReturnType is SignatureTypeCode.UIntPtr) return "uintptr_t";
        // P-D3 P0 (fnptr DllImport RETURN): ELEMENT_TYPE_FNPTR (0x1B) return
        // was silently declared int32_t (the x64 address truncated). A fnptr
        // IS a void* (the "p" slot); the C++ compiler reads the full-width
        // pointer and the calli rides the void* value directly.
        if (m.ReturnType is SignatureTypeCode.FunctionPointer) return "void*";
        // P-D1 P0-1 (full-width returns): int64/double/float/uint64 DllImport
        // returns were silently declared int32_t (a double return read EAX,
        // a long return sign-extended the truncated EAX -- both P-D1 audits
        // §4). The result TEMP is already sized by StackFold's
        // UserCallResultKind ("l"/"d"/"ptr"), so only the extern "C"
        // declaration must name the native return width -- the C++ compiler
        // then reads the correctly-typed return register/converts the SDL.
        if (m.ReturnType is SignatureTypeCode.Int64) return "int64_t";
        if (m.ReturnType is SignatureTypeCode.UInt64) return "uint64_t";
        if (m.ReturnType is SignatureTypeCode.Single) return "float";
        if (m.ReturnType is SignatureTypeCode.Double) return "double";
        var structRet = PinvokeReturnStructName(md, m);
        if (structRet != null) return structRet;
        // Small native-int-family remains int32_t (the CLR expands BOOL/short/
        // byte/char P/Invoke slots to 4-byte native ints). A STRING-typed
        // return (native char* -> managed string) still collapses here too --
        // out of P0 scope, recorded as a P2 future item.
        return "int32_t";
    }

    // wfm2 P2: a P/Invoke method whose RETURN is a VALUETYPE struct -- the
    // native callee returns the struct BY VALUE. The MethodDef model stores
    // only SignatureTypeCode.ReturnType (no full name), so the full name is
    // re-decoded from the method signature blob: header + param count + the
    // RETURN byte; a 0x11 (VALUETYPE) return is followed by the TypeDefOrRef.
    private static string? PinvokeReturnStructName(MetadataReader md, MethodDef m)
    {
        var handle = FindMethodHandle(md, m);
        if (handle == null) return null;
        var mi = md.GetMethodDefinition(handle.Value);
        var sig = md.GetBlobReader(mi.Signature);
        var hdr = sig.ReadSignatureHeader();
        if (hdr.IsGeneric) sig.ReadCompressedInteger();
        sig.ReadCompressedInteger(); // param count
        var retByte = sig.ReadByte();
        if (retByte != 0x11) return null; // not a VALUETYPE return
        var owner = sig.ReadTypeHandle();
        var name = owner.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)owner),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)owner),
            _ => null,
        };
        return name == null ? null : PinvokeStructCppType(name);
    }

    // P/Invoke parameter list (M4: int32 and string (-> const char*)).
    // wfm2 (struct params): a VALUETYPE struct param (V:<FullName>) declares the native
    // struct type BY VALUE (blittable register/stack ABI); a BYREF struct param
    // (B:V:<FullName> -- out/ref, e.g. GetWindowRect's out RECT) declares the struct POINTER.
    // wfm1 (winforms1): an UNREGISTERED user struct (WNDCLASSEX/MSG -- no
    // il2cpp_pinvoke_struct entry) passes BY ADDRESS: the managed value struct
    // is emitted with a native-compatible layout (int32_t/intptr_t fields at
    // natural alignment), so the pointer is handed through untouched. The C++
    // type is the sanitized generated struct name (the nested types expand to
    // their short metadata name -- TypeEmitter.ExpandNestedTypes -- which is
    // exactly the token's suffix here).
    private static string PinvokeParamList(MetadataReader md, MethodDef m)
    {
        // wf2 (winforms1): CharSet.Unicode/Auto P/Invokes marshal string args
        // as UTF-16 (the W-API reads char16_t*). m.ParamTypes carries the raw
        // MethodSignatureCoder encoding ("r:System.String" regardless of
        // CharSet) -- the CharSet must be re-decoded from the import attribute
        // (MethodImportAttributes CharSet mask 0x06: 0x02 Ansi / 0x04 Unicode
        // / 0x06 Auto; on Windows Auto = W/Unicode).
        var pinvokeHandle = FindMethodHandle(md, m);
        var utf16PInvoke = false;
        if (pinvokeHandle != null)
        {
            var mi = md.GetMethodDefinition(pinvokeHandle.Value);
            var import = mi.GetImport();
            if (!import.Module.IsNil)
            {
                var charSet = (int)import.Attributes & 0x06;
                utf16PInvoke = charSet is 0x04 or 0x06;
            }
        }
        if (m.ParamTypes.Length == 0) return "void";
        return string.Join(", ", SplitTopLevel(m.ParamTypes).Select(p =>
        {
            if (p.StartsWith("B:V:", StringComparison.Ordinal))
            {
                var inner = PinvokeStructCppType(p[4..]);
                if (inner != null) return inner + "*";
                return TypeEmitter.Sanitize(p[4..]) + "*";
            }
            if (p.StartsWith("V:", StringComparison.Ordinal))
            {
                var inner = PinvokeStructCppType(p[2..]);
                if (inner != null) return inner;
                return TypeEmitter.Sanitize(p[2..]);
            }
            // P-D3 P1 (fnptr DllImport PARAM): ELEMENT_TYPE_FNPTR (0x1B)
            // params cross the P/Invoke boundary at full width -- the fnptr
            // value IS the model's void* "p" slot, passed through directly on
            // x64 (the native callee receives the raw function pointer). The
            // call-site arg bridge (EmitIcallCallArgs + PinvokeArgBridge)
            // converts the void* managed slot to the native void* decl.
            if (p.StartsWith("F:", StringComparison.Ordinal)) return "void*";
            // wf2 (winforms1): CharSet.Unicode P/Invoke string param -> UTF-16
            // (const char16_t*), else UTF-8 (const char*).
            if (p == "r:System.String") return utf16PInvoke ? "const char16_t*" : "const char*";
            // P-D1 P0: the marshalling type matrix is COMPLETE -- every branch
            // is either a real native type or a LOUD rejection (never the old
            // flat int32_t fallback). The CLR expands the narrow scalar
            // family (bool/char/sbyte/byte/short/ushort/typedref) to 4-byte
            // native int slots; native-int stays intptr_t.
            // P-D1 P0-1 (full-width scalars): int64/uint64/double/float params
            // were silently declared int32_t and truncated by the
            // il2cpp_pinvoke_arg bridge (static_cast to the int32_t decl) --
            // both P-D1 audits §4. They now declare their real width; the
            // call-site arg bridge (EmitIcallCallArgs + PinvokeArgBridge)
            // deduces the native type and converts the managed slot.
            return p switch
            {
                "l" => "int64_t",
                "q" => "uint64_t",
                "f" => "float",
                "d" => "double",
                "n" => "intptr_t",
                "i" or "b" or "c" or "i1" or "u1" or "i2" or "u2" or "tr" => "int32_t",
                // P-D1 P0-2 (delegate-typed DllImport params): a delegate (or
                // any other reference-typed / fnptr / pointer / byref) param
            // P-D2 #4 (delegate-typed DllImport params): a DELEGATE param
            // marshals through the per-signature GfpS dispatcher -- the native
            // side receives the intptr_t function pointer (the SAME carrier an
            // explicit GetFunctionPointerForDelegate hands over). The
            // declaration path RESOLVES the delegate Invoke signature and
            // REGISTERS the GfpS module (generated.h flush), so the call site
            // arg bridge wraps the delegate argument in the dispatcher.
            // Non-delegate reference params (arrays/objects/BCL generic
            // delegates absent from the input assembly metadata) keep the
            // P-D1 P0-2 LOUD rejection below.
            var rDel when rDel.StartsWith("r:", StringComparison.Ordinal)
                && MethodRefResolver.DelegateGfpForPinvokeParam(md, rDel[2..]) is { } gfpName
                => "intptr_t",
            // P-D1 P0-2 (delegate-typed DllImport params): a delegate (or any
            // other reference-typed / fnptr / pointer / byref) param is
            // LOUDLY rejected -- the old fallback emitted int32_t for
            // an Il2CppObject* delegate (silent compile + runtime crash;
            // delegatecb.cs:19-27 claims this rejection but no code
            // existed). fnptr (F:) and pointer (P:) params are the P-D3
            // work items; they must not silently downgrade either.
            var r when r.StartsWith("r:", StringComparison.Ordinal)
                || r.StartsWith("F:", StringComparison.Ordinal)
                || r.StartsWith("P:", StringComparison.Ordinal)
                || r.StartsWith("B:", StringComparison.Ordinal) =>
                throw new NotSupportedException(
                    $"P/Invoke parameter type '{r}' on {m.Name} is not supported: reference-typed (delegate/array/object), fnptr, pointer and by-ref DllImport parameters are LOUDLY rejected (no marshalling bridge exists -- P-D3 wires fnptr)"),
                var other => throw new NotSupportedException(
                    $"P/Invoke parameter encoding '{other}' on {m.Name} is not supported"),
            };
        }));
    }

    // wfm2: the native C++ type of a registered managed struct full name
    // (System.Drawing.Point/Rectangle/System.Windows.Forms.Message ->
    // il2cpp::Il2CppPinvokePoint/Rect/Msg; anything else -> null).
    private static string? PinvokeStructCppType(string managedFullName) => managedFullName switch
    {
        "System.Drawing.Point" => "il2cpp::Il2CppPinvokePoint",
        "System.Drawing.Rectangle" => "il2cpp::Il2CppPinvokeRect",
        "System.Windows.Forms.Message" => "il2cpp::Il2CppPinvokeMsg",
        _ => null,
    };

    // RES-1 (iron rule 18): the count of embedded manifest resources in the
    // assembly being transpiled -- drives the conditional resource-pool.h
    // include (generated.h) and the il2cpp_resource_pool_fill() call
    // (il2cpp_runtime_init). Mirrors Program.WriteResourcePoolReport's
    // LoadManifestResources path; load failures are tolerated (0 resources =
    // no include, no fill -- never turn a successful transpile into a failure).
    private static int ResourceCount(string assemblyPath)
    {
        try
        {
            return MetadataLoader.LoadManifestResources(assemblyPath).Count;
        }
        catch
        {
            return 0;
        }
    }

    // MethodDef -> signature decode (M4: look up the method body with the same (owner, name)).
    private static MethodDefinitionHandle? FindMethodHandle(MetadataReader md, MethodDef m)
    {
        foreach (var tdh in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(tdh);
            if (md.GetFullName(tdh) != m.Owner.FullName) continue;
            foreach (var mh in td.GetMethods())
            {
                var mi = md.GetMethodDefinition(mh);
                if (md.GetString(mi.Name) != m.Name) continue;
                var (_, _, paramTypes, _) = MethodSignatureCoder.Decode(md, mi.Signature);
                if (paramTypes == m.ParamTypes) return mh;
            }
        }
        return null;
    }

    // FIX-4 (delechain): a STATIC field whose metadata signature is a
    // reference type (delegate/class/string/object/array -- e.g. the
    // field-backed event `_changed` of type Func<int,int>) must be
    // declared Il2CppObject* storage, NOT int32_t: the generated body
    // assigns the delegate-combine result (`static_cast<Il2CppObject*>`)
    // into the storage slot, which fails to compile against int32_t.
    // The model's FieldDef.TypeKind is only populated for INSTANCE fields
    // (MetadataLoader decodes instance signatures only -- static fields
    // are added without type info), so the reference-ness is re-decoded
    // here from the metadata field signature. TypeEmitter.cs:504 emits
    // the matching int32_t DEFINITION -- the paired reference-typed fix
    // there is a reported TypeEmitter-side item (this batch owns the
    // extern declaration side only).
    // M7 CG-FIX (lockcase/props): IR static-field name ("LockCase.gate") ->
    // reference-ness. Splits owner.fullName + field name and delegates to
    // StaticFieldIsReference (which needs the TypeDef + metadata field sig).
    // A static reference field read into a local must propagate "ref" (see the
    // locKind pre-seed in EmitBody).
    private static bool StaticFieldExprIsReference(MetadataReader md, IReadOnlyList<TypeDef> types, string fieldName)
    {
        var dot = fieldName.LastIndexOf('.');
        if (dot <= 0) return false;
        var ownerFullName = fieldName[..dot];
        var field = fieldName[(dot + 1)..];
        // probe3 (closurecase): the owner may be a NESTED compiler-generated
        // type (`<>c` lambda cache class). MetadataLoader skips nested types
        // from the transpiled `types` list, so the TypeDef lookup below
        // misses and the read degrades to int32_t. Resolve directly in
        // metadata instead. Spelling note: a MemberReference parent renders
        // nested owners as "Outer+Nested" (GetFullName on TypeReference),
        // while GetFullName on the TypeDefinition itself yields the short
        // name -- compare the LAST '+' segment.
        var ownerShort = ownerFullName[(ownerFullName.LastIndexOf('+') + 1)..];
        foreach (var tdh in md.TypeDefinitions)
        {
            if (md.GetFullName(tdh) != ownerShort) continue;
            return StaticFieldIsReference(md, tdh, field);
        }
        // BCL static fields whose RUNTIME storage is an Il2CppObject* carrier:
        // the owner is a TypeRef into a reference assembly (no local field
        // metadata), but the synthesized storage name is externed by the
        // runtime as a managed reference -- arraysort.h:97
        // System_Collections_Comparer_sf_Default, il2cpp_console.h:66
        // System_IO_TextWriter_sf_Null. Reading either into a local must
        // declare the local Il2CppObject* (caudit3 cmake FAIL otherwise).
        if (ownerFullName == "System.Collections.Comparer"      // Default / DefaultInvariant
            || ownerFullName == "System.IO.TextWriter")          // Null
            return true;
        return false;
    }

    private static bool StaticFieldIsReference(MetadataReader md, TypeDefinitionHandle owner, string fieldName)
    {
        var td = md.GetTypeDefinition(owner);
        foreach (var fh in td.GetFields())
        {
            var fd = md.GetFieldDefinition(fh);
            if (md.GetString(fd.Name) != fieldName) continue;
            var sig = md.GetBlobReader(fd.Signature);
            if (sig.ReadByte() != 0x06) return false; // not a FIELD sig
            // ECMA-335 II.23.1.16: strip custom modifiers (volatile ->
            // CMOD_REQD(IsVolatile) 0x1F) before classifying.
            var typeByte = MethodSignatureCoder.SkipCustomModifiers(ref sig, sig.ReadByte());
            // Reference kinds (mirrors MetadataLoader.DecodeFieldSignature
            // reference arms + GenericInst/delegate classes): 0x0E String,
            // 0x1C Object, 0x12 Class, 0x15 GenericInst (Func`N etc.),
            // 0x1D SZARRAY, 0x14 ARRAY. Everything else (int32,
            // value-type, VAR) stays int32_t storage.
            return typeByte is 0x0E or 0x1C or 0x12 or 0x15 or 0x1D or 0x14;
        }
        return false;
    }

    // Branch-condition emission: EmitExpr for BinaryExpr/UnaryExpr already adds parentheses, so the
    // outer if(...) must not wrap them again, or `if((x == 4LL))` triggers clang -Wparentheses-equality (CI quality gate: zero warnings on both compilers).
    private static string EmitCondition(Expr e, MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        if (e is BinaryExpr or UnaryExpr)
        {
            var code = EmitExpr(e, md, types);
            // Only when EmitExpr actually added parentheses (constant folding returns the bare constant `-2147483648LL`, with no parens to strip)
            return code.StartsWith('(') && code.EndsWith(')')
                ? code.Substring(1, code.Length - 2)
                : code;
        }
        return EmitExpr(e, md, types);
    }

    //--------------------------------------------------------------------------
    // FieldAccess -- instance field access expression with .NET null semantics.
    //
    //   obj         : the receiver expression (any Expr producing a GC pointer)
    //   owner       : full type name of the field's declaring type ("Calc.Point")
    //   fieldCppName: the sanitized member name on the generated struct
    //   md/types    : metadata context forwarded to recursive EmitExpr calls
    //
    // Emits:   il2cpp_null_check((Il2CppObject*)(<obj>))-><field>
    //   The null check happens BEFORE the dereference (matching .NET's
    //   NullReferenceException on null receivers) and evaluates the receiver
    //   exactly once (the receiver expression appears only inside the check
    //   argument). The pointer is cast to the CONCRETE generated struct type
    //   so the member access compiles (the IR carries the reference as an
    //   Il2CppObject* slot; the struct's first member is that header, so the
    //   cast is layout-correct).
    //--------------------------------------------------------------------------
    private static string FieldAccess(Expr obj, string owner, string fieldCppName, MetadataReader md, IReadOnlyList<TypeDef> types, bool isValueTypeObject = false)
    {
        var objCode = EmitExpr(obj, md, types);
        if (isValueTypeObject)
        {
            // M3 Task 11: value-type receiver -- no object header, no null
            // check. The value is a plain C++ value; take its address.
            // If the receiver is already an address (LocAddrExpr from ldloca)
            // or a value-type method's this (a {T}* parameter named arg0),
            // dereference it directly; a plain value local needs (&obj)->f.
            // caudit2-fix: address args (this pointer / by-ref / raw pointer)
            // dereference directly; a BY-VALUE struct arg falls through to the
            // (&(arg))-> form (arg1 is a plain C++ value -- `arg1->V` did not compile).
            if (obj is LocAddrExpr || (obj is LocExpr le && IsAddressArg(le.Name)))
                return $"{objCode}->{fieldCppName}";
            return $"(&({objCode}))->{fieldCppName}";
        }
        var ownerCpp = TypeCppName(owner);
        // CAST-1: reinterpret_cast<T> requires a parenthesized operand
        // (`reinterpret_cast<T>il2cpp_null_check(...)` does not compile --
        // delegateinst/boxunbox/delecb2 cmake failures). The null check
        // returns Il2CppObject*; cast it to the concrete struct type. The
        // paren balance must match the old `((T*)il2cpp_null_check(...))`
        // shape -- one open for the cast's operand plus the two outer.
        return $"(({(ownerCpp == "Il2CppObject" ? "static_cast" : "reinterpret_cast")}<{ownerCpp}*>(il2cpp_null_check(static_cast<Il2CppObject*>({objCode})))))->{fieldCppName}";
    }

    // M7 CG-FIX (boxdeep2 cmake FAIL): the class-descriptor expression for
    // isinst/castclass. The runtime exposes GLOBAL descriptors only for
    // Int32/Boolean/Char/Double (metadata.cpp System_*_class) plus Object/
    // String/interfaces; the other box primitives (UInt32/Int64/UInt64/Byte/
    // SByte/Int16/UInt16/Single/Decimal) are file-local g_*BoxClass in
    // boxingext2.cpp, lazily registered on first box. `&System_UInt32_class`
    // is an undefined symbol, so those types resolve through
    // il2cpp_class_by_name (registered by the time any boxed value of that
    // type exists -- isinst compares pointer identity). Every other type uses
    // the mangled global descriptor symbol.
    private static string BoxClassDescriptorCpp(string typeName) => typeName switch
    {
        "System.Int32" => "&System_Int32_class",
        "System.Boolean" => "&System_Boolean_class",
        "System.Char" => "&System_Char_class",
        "System.Double" => "&System_Double_class",
        "System.Object" => "&System_Object_class",
        "System.String" => "&System_String_class",
        "System.UInt32" => "il2cpp::il2cpp_class_by_name(\"System.UInt32\")",
        "System.Int64" => "il2cpp::il2cpp_class_by_name(\"System.Int64\")",
        "System.UInt64" => "il2cpp::il2cpp_class_by_name(\"System.UInt64\")",
        "System.Byte" => "il2cpp::il2cpp_class_by_name(\"System.Byte\")",
        "System.SByte" => "il2cpp::il2cpp_class_by_name(\"System.SByte\")",
        "System.Int16" => "il2cpp::il2cpp_class_by_name(\"System.Int16\")",
        "System.UInt16" => "il2cpp::il2cpp_class_by_name(\"System.UInt16\")",
        "System.Single" => "il2cpp::il2cpp_class_by_name(\"System.Single\")",
        "System.Decimal" => "il2cpp::il2cpp_class_by_name(\"System.Decimal\")",
        // batch 162 (eventcase): BCL delegates (System.EventHandler /
        // EventHandler`1 -- event backing fields, isinst on the Raise
        // receiver) resolve to the runtime System.Delegate descriptor; the
        // multicast class's baseClass walks up to it, so isinst passes for
        // both single-cast and combined delegates.
        "System.EventHandler" or "System.EventHandler`1" => "&System_Delegate_class",
        _ => $"&{TypeEmitter.Sanitize(typeName)}_class",
    };

    // Type full name -> generated struct name ("Calc.Point" -> Calc_Point). System.Object/String have
    // no generated struct in M2 (runtime types); reference slots are uniformly Il2CppObject*.
    // M5 wiring: the BCL 8-byte value types map to the runtime struct types
    // (il2cpp_time.h -- Il2CppDateTime/Il2CppTimeSpan, single int64 field).
    private static string TypeCppName(string fullName) => fullName switch
    {
        "System.Object" => "Il2CppObject",
        "System.String" => "Il2CppObject",
        "System.DateTime" => "il2cpp::runtime::Il2CppDateTime",
        "System.TimeSpan" => "il2cpp::runtime::Il2CppTimeSpan",
        // P13 dtoffset: the 16-byte runtime value struct (il2cpp_dtoffset.h).
        "System.DateTimeOffset" => "il2cpp::runtime::Il2CppDateTimeOffset",
        // P17 biginteger: the 8-byte runtime value struct (il2cpp_biginteger.h).
        "System.Numerics.BigInteger" => "il2cpp::runtime::Il2CppBigInteger",
        // decimal-domain T3: the 16-byte runtime value struct (decimal.h).
        "System.Decimal" => "il2cpp::runtime::Il2CppDecimal",
        // M6 wiring: GCHandle is a single int32 handle value (the M2 value
        // carrier, same slot model as IntPtr -- il2cpp_gchandle.h). Without
        // this entry a GCHandle local falls to the default
        // System_Runtime_InteropServices_GCHandle name (no generated struct)
        // -> C++ compile failure.
        "System.Runtime.InteropServices.GCHandle" => "int32_t",
        // wf2 (winforms1): System.IntPtr/UIntPtr are the pointer CARRIER
        // (intptr_t/uintptr_t slots -- MethodMap.IntPtr, M4.5 "ptr" result kind).
        "System.IntPtr" => "intptr_t",
        "System.UIntPtr" => "uintptr_t",
        // Struct-marshal wiring: registered structs map to the native
        // value structs (PinvokeStructCppType) so struct-typed locals and
        // slots declare a defined type instead of System_Drawing_Rectangle.
        "System.Drawing.Point" => "il2cpp::Il2CppPinvokePoint",
        "System.Drawing.Rectangle" => "il2cpp::Il2CppPinvokeRect",
        "System.Windows.Forms.Message" => "il2cpp::Il2CppPinvokeMsg",
        _ => TypeEmitter.Sanitize(fullName),
    };

    // int32 constant arithmetic folds at generation time (.NET unchecked wrap: take low 32 bits,
    // sign-extend). (1) avoids -Wconstant-conversion / -Woverflow on LL constants like
    // `icall(...((2147483647LL + 1LL)))` reaching int32 params (truncation completes at generation
    // time; C++ sees no implicit narrowing). (2) the folded constant is exactly .NET's int32 result
    // (2147483647 + 1 == -2147483648). Comparisons (Ceq/Cgt/Clt/...) always fold to 0/1 too -- a
    // constant branch condition (the `ldc.i4.5; dup; ldc.i4.3; ble.s` stack pattern) makes EmitStmt
    // hard-code the branch, avoiding `if(<constant>)` (MSVC /W4 C4127).
    // Divide/modulo by zero cannot occur in valid C# (Roslyn errors at compile time); not folded.
    internal static bool TryFoldInt32(Expr e, out long value)
    {
        switch (e)
        {
            case IntConstExpr i:
                // int64-typed constants (ldc.i8, IsInt64 marker) must NOT be
                // folded with int32 semantics: an int64 expression can exceed
                // the int32 range mid-stream (e.g. `x + 1L` near int.MaxValue).
                // Falling through to the C++ emission widens the expression to
                // int64 naturally; folding it as int32 would silently wrap.
                if (i.IsInt64)
                {
                    value = 0;
                    return false;
                }
                value = i.Value;
                return true;
            case BinaryExpr b:
                if (!TryFoldInt32(b.Left, out var l) || !TryFoldInt32(b.Right, out var r))
                {
                    value = 0;
                    return false;
                }
                if (b.Op is BinaryOp.Div or BinaryOp.Rem && r == 0)
                {
                    value = 0;
                    return false;
                }
                // Audit H9: int.MinValue / -1 always throws OverflowException in .NET (the div
                // instruction checks this operand pair in both checked and unchecked contexts; C# long
                // division cannot overflow, and Wrap32 would silently fold to int.MinValue) -- do not
                // fold; keep the division so the emitter routes to il2cpp_checked_div (exception.cpp
                // checks a==INT32_MIN && b==-1, then throws a managed exception carrier). Rem never overflows (int.MinValue % -1 == 0); it stays outside this guard.
                if (b.Op == BinaryOp.Div && l == int.MinValue && r == -1)
                {
                    value = 0;
                    return false;
                }
                value = b.Op switch
                {
                    BinaryOp.Add => Wrap32(l + r),
                    BinaryOp.Sub => Wrap32(l - r),
                    BinaryOp.Mul => Wrap32(l * r),
                    BinaryOp.Div => Wrap32(l / r),
                    BinaryOp.Rem => Wrap32(l % r),
                    BinaryOp.And => l & r,
                    BinaryOp.Or => l | r,
                    BinaryOp.Xor => l ^ r,
                    // Shift counts mask to 0..31 per int32 semantics; right shift is arithmetic (sign-
                    // extending negatives), matching .NET. shr.un is logical (zero-filling) -- shift as uint32, then restore the int32 bit pattern.
                    BinaryOp.Shl => Wrap32(l << (int)(r & 31)),
                    BinaryOp.Shr => Wrap32(l >> (int)(r & 31)),
                    BinaryOp.ShrUn => Wrap32((uint)l >> (int)(r & 31)),
                    BinaryOp.Ceq => l == r ? 1 : 0,
                    BinaryOp.Cgt => l > r ? 1 : 0,
                    BinaryOp.Clt => l < r ? 1 : 0,
                    BinaryOp.CgtUn => (uint)l > (uint)r ? 1 : 0,
                    BinaryOp.CltUn => (uint)l < (uint)r ? 1 : 0,
                    _ => 0,
                };
                return true;
            case UnaryExpr u:
                if (!TryFoldInt32(u.Operand, out var operand))
                {
                    value = 0;
                    return false;
                }
                value = u.Op == UnaryOp.Neg ? Wrap32(-operand) : Wrap32(~operand);
                return true;
            default:
                value = 0;
                return false;
        }
    }

    // Take the low 32 bits + sign-extend (explicit unchecked: equivalent to C# int wrap-around, deterministic across compilers/configs).
    private static long Wrap32(long v) => unchecked((int)(v & 0xFFFFFFFFL));

    // G5 (bitopcase): int64 constant folding with net48 UNCHECKED wrap-around
    // (the C# unchecked context / IL add/sub/mul semantics). Mirrors
    // TryFoldInt32's operator set at 64-bit width. Comparisons are NOT folded
    // here (they stay in the generic emitter, where equality already casts to
    // uint64); div/rem by zero and the long.MinValue / -1 division are refused
    // (net48 raises OverflowException / DivideByZeroException at runtime).
    internal static bool TryFoldInt64(Expr e, out long value)
    {
        switch (e)
        {
            case Int64ConstExpr l:
                value = l.Value;
                return true;
            case IntConstExpr i:
                value = i.Value; // an int32 constant widens into the int64 context
                return true;
            case BinaryExpr b:
                if (!TryFoldInt64(b.Left, out var lv) || !TryFoldInt64(b.Right, out var rv))
                {
                    value = 0;
                    return false;
                }
                if (b.Op is BinaryOp.Div or BinaryOp.Rem && rv == 0)
                {
                    value = 0;
                    return false;
                }
                if (b.Op == BinaryOp.Div && lv == long.MinValue && rv == -1)
                {
                    value = 0;
                    return false;
                }
                if (b.Op is BinaryOp.Ceq or BinaryOp.Cgt or BinaryOp.Clt
                    or BinaryOp.CgtUn or BinaryOp.CltUn)
                {
                    value = 0;
                    return false; // comparisons keep their int32 emission
                }
                value = b.Op switch
                {
                    BinaryOp.Add => unchecked(lv + rv),
                    BinaryOp.Sub => unchecked(lv - rv),
                    BinaryOp.Mul => unchecked(lv * rv),
                    BinaryOp.Div => lv / rv,
                    BinaryOp.Rem => lv % rv,
                    BinaryOp.And => lv & rv,
                    BinaryOp.Or => lv | rv,
                    BinaryOp.Xor => lv ^ rv,
                    // IL shift counts mask to 0..63 at 64-bit width.
                    BinaryOp.Shl => unchecked(lv << (int)(rv & 63)),
                    BinaryOp.Shr => lv >> (int)(rv & 63),
                    BinaryOp.ShrUn => unchecked((long)((ulong)lv >> (int)(rv & 63))),
                    _ => 0,
                };
                return true;
            case UnaryExpr u:
                if (!TryFoldInt64(u.Operand, out var ov))
                {
                    value = 0;
                    return false;
                }
                value = u.Op == UnaryOp.Neg ? unchecked(-ov) : ~ov;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    // Function-header English comments: IL signature + method-body location (RVA/token) + semantics overview (spec: generated code must carry fully-English, hyper-detailed comments).
    private static void EmitFunctionHeader(CppWriter w, TypeDef t, MethodDef m, MethodBodyInfo info,
        List<Stmt> stmts, string assemblyPath)
    {
        w.Line("//------------------------------------------------------------------------------");
        w.Line("// IL method: " + IlMethodSignature(t, m, info));
        w.Line($"// IL offset (RVA): 0x{info.Rva:X8} (assembly: {Path.GetFileName(assemblyPath)}, token: 0x{info.MethodToken:X8})");
        w.Line("// Semantics: " + DescribeMethodSemantics(info, stmts));
        w.Line("//------------------------------------------------------------------------------");
    }

    private static string IlMethodSignature(TypeDef t, MethodDef m, MethodBodyInfo info)
    {
        var ret = info.ReturnType switch
        {
            SignatureTypeCode.Void => "System.Void",
            SignatureTypeCode.Int32 => "System.Int32",
            _ => info.ReturnType.ToString(),
        };
        var pars = string.Join(",", Enumerable.Repeat("System.Int32", info.ArgCount));
        return $"{ret} {t.FullName}::{m.Name}({pars})";
    }

    private static string DescribeMethodSemantics(MethodBodyInfo info, List<Stmt> stmts)
    {
        var ret = stmts.OfType<RetStmt>().LastOrDefault();
        var retDesc = ret == null ? "never returns via an IL 'ret'"
            : ret.Value == null ? "returns void"
            : $"returns {DescribeExprShort(ret.Value)}";
        return $"Transpiled from {info.ArgCount} argument slot(s) and {info.IlBytes.Length} byte(s) of IL; "
             + $"the body below is the stack-folding result ({stmts.Count} IR statement(s)) of the original "
             + $"instruction stream. On exit the method {retDesc}. All operations follow the M1 32-bit signed "
             + $"integer contract.";
    }

    private static string DescribeExprShort(Expr e)
    {
        switch (e)
        {
            case IntConstExpr i: return $"the constant {i.Value}";
            case StringConstExpr s: return $"the string literal \"{s.Value}\"";
            case LocExpr l: return $"the value of {l.Name}";
            case StaticFieldExpr f: return $"the value of static field {FieldStorageName(f.FieldName)}";
            case BinaryExpr b: return $"the result of ({DescribeExprShort(b.Left)} {OpSymbol(b.Op)} {DescribeExprShort(b.Right)})";
            case UnaryExpr u: return $"the result of {(u.Op == UnaryOp.Neg ? "negating" : "bitwise-complementing")} ({DescribeExprShort(u.Operand)})";
            case CallExpr c: return "the result of a function call";
            default: return "a computed value";
        }
    }

    private static string DescribeCall(string fn) =>
        fn.Contains("icall_console_write_line") ? "Console.WriteLine via the runtime icall" : $"the transpiled function {fn}";

    // IL provenance comment fragment: opcode + optional offset (no @0x when there is no source).
    private static string At(string opName, int offset) => offset >= 0 ? $"{opName} @0x{offset:X4}" : opName;

    // The AssignStmt target can be a local/temp (verbatim) or a static-field path (Owner.Field -> the C++ storage name).
    private static string EmitTarget(string target) => target.Contains('.') ? FieldStorageName(target) : target;

    // "{Owner.FullName}.{Field}" -> "{TypeCppName}_sf_{Sanitize(Field)}", consistent with TypeEmitter.EmitStaticStorage.
    private static string FieldStorageName(string fullName)
    {
        var idx = fullName.LastIndexOf('.');
        if (idx < 0) return fullName;
        return $"{TypeEmitter.Sanitize(fullName[..idx])}_sf_{TypeEmitter.Sanitize(fullName[(idx + 1)..])}";
    }

    // CallExpr/CallStmt.Function is a "call_{token}" placeholder -> MethodRefResolver substitutes
    // the real CppName. The box_/unbox_/isinst_/castclass_ prefixes are special-cased in the CallExpr branches of EmitExpr/EmitStmt (below).
    private static string EmitFunctionName(string function, MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        if (function.StartsWith("call_", StringComparison.Ordinal) && int.TryParse(function[5..], out var token))
            return MethodRefResolver.Resolve(md, token, types).CppName;
        return function;
    }

    // Task 9 helper call: the full expression for a box_/unbox_/isinst_/castclass_ prefix.
    //   box_{token}(v)        -> il2cpp::il2cpp_box_int32(nullptr, v)
    //   unbox_{token}(obj)    -> il2cpp::il2cpp_unbox_int32(obj)
    //   isinst_{token}(obj)   -> il2cpp::il2cpp_is_instance(obj, &{Type}_class)
    //   castclass_{token}(obj)-> il2cpp::il2cpp_is_instance(obj, &{Type}_class)
    //     (castclass uses isinst for the check in M2; the InvalidCast throw on a type mismatch is
    //      il2cpp_is_instance's caller (the generated code) owns it -- M2 simplifies to the check itself)
    //
    // BCL enums that can reach a box site in a translated assembly. The
    // transpiler reads only the USER assembly's metadata: a BCL enum
    // (System.DayOfWeek, ...) is a TypeRef whose System.Enum base lives in
    // mscorlib -- invisible here. User-defined enums are TypeDefs (detected
    // via BaseTypeName == "System.Enum" in the box arm, no whitelist). The
    // whitelist grows on demand -- one entry per case that needs it.
    private static readonly System.Collections.Generic.HashSet<string> KnownBclEnumNames =
        new(System.StringComparer.Ordinal)
        {
            "System.DayOfWeek",
            "System.Runtime.GCLatencyMode",
        };
    private static bool IsEnumReceiverType(string typeName, IReadOnlyList<TypeDef> types)
        => typeName == "System.Enum"
           || KnownBclEnumNames.Contains(typeName)
            || types.Any(t => t.MatchesFullName(typeName) && t.BaseTypeName == "System.Enum");

    private static string EmitTask9Call(Expr e, MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        if (e is not CallExpr c) return EmitExpr(e, md, types);
        // calli (0x29, fnptr domain b23): indirect call through a function
        // pointer -- the fold emitted CallExpr{Function="calli_{token}",
        // Args=[fnptr, args...]}. Decode the StandAloneSig signature and
        // render ((retCpp{conv}(*)(params))fnptr)(args) with a null-pointer
        // guard (a null fnptr is a .NET NullReferenceException).
        if (c.Function.StartsWith("calli_", StringComparison.Ordinal)
            && long.TryParse(c.Function[6..], out var calliToken))
        {
            return EmitCalliCall(c, (int)calliToken, md, types);
        }
        // mathdeep: synthetic double self-comparison (ProtectDoubleSelfCompare).
        // `v == v` for a DOUBLE is NaN-aware (false when v is NaN), so the
        // int32 self-identity fold (x==x -> 1) must not apply; the shield
        // rewrote it into this call, which renders the raw double comparison.
        if (c.Function == "il2cpp_double_eq_self" && c.Args.Count == 1)
        {
            var darg = EmitExpr(c.Args[0], md, types);
            return $"((static_cast<double>({darg})) == (static_cast<double>({darg})))";
        }
        if (c.Function == "il2cpp_double_ne_self" && c.Args.Count == 1)
        {
            var darg = EmitExpr(c.Args[0], md, types);
            return $"((static_cast<double>({darg})) != (static_cast<double>({darg})))";
        }
        if (c.Function.StartsWith("box_", StringComparison.Ordinal) && int.TryParse(c.Function[4..], out var boxTok))
        {
            // Audit H3 + M5 wiring: box dispatches on the value type. The M5
            // runtime domains provide the box icalls for the extended
            // primitives; anything else is rejected LOUDLY (silently calling
            // the wrong box helper would stamp the wrong box class).
            // M6 enum box wiring (timecase work order 2 follow-up): a USER enum
            // boxes through icall_box_enum(&{T}_class, value) -- the enum's OWN
            // name-bearing class descriptor (D-ENM-3/D-ENM-5, enumdeep2/3): a
            // long-carrier box would stamp the Int64 box class and the boxed
            // enum would LOSE its enum identity (enumext's klass-name-keyed
            // registry lookup -- FindInfo -- would see "System.Int64"). The
            // 8-byte payload slot is shared with the long box, so every
            // consumer (Console "enum:l" unbox:l cast, MethodMap.Enum
            // "unbox:l" casts) still reads the widened value. User enums are
            // TypeDefs (base System.Enum, detected via BaseTypeName) whose
            // generated {T}_class descriptor carries the enum name; BCL enums
            // (System.DayOfWeek etc.) are TypeRefs whose System.Enum base
            // lives in mscorlib -- outside the user assembly's metadata -- and
            // have NO runtime class descriptor symbol, so they keep the
            // int64-carrier box (the documented BCL-enum whitelist below,
            // extends on demand).
            var boxType = TypeTokenFullName(md, boxTok);
            var boxVal = EmitExpr(c.Args[0], md, types);
            // P-G3/paircase Cmp: `box !0` on a REFERENCE-typed generic
            // parameter (the constrained IComparable callvirt's argument) is
            // identity -- the value is ALREADY the managed object (a string/
            // object clone slot). The M3 default decodes the VAR TypeSpec to
            // "System.Int32" and would box_int32 the string POINTER (a
            // garbage box). Resolve the concrete T through the enclosing
            // clone's suffix (InstantiationSuffixHint) and treat a reference
            // arg as a no-op.
            var boxConcreteVar = MethodRefResolver.SharedCloneVarTypeName(md, boxTok);
            if (boxConcreteVar != null)
            {
                var boxConcreteDef = types.FirstOrDefault(x => x.MatchesFullName(boxConcreteVar));
                var isValueConcrete = boxConcreteDef != null && boxConcreteDef.IsValueType
                    || boxConcreteVar is "System.Int32" or "System.Int64" or "System.UInt64"
                        or "System.Double" or "System.Single" or "System.Boolean" or "System.Char"
                        or "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16"
                        or "System.UInt32" or "System.Decimal";
                if (!isValueConcrete)
                {
                    return boxVal; // box of a reference-typed T is identity
                }
boxType = boxConcreteVar; // value-typed T: box the concrete type
            }
            var boxTypeDef = types.FirstOrDefault(x => x.MatchesFullName(boxType));
            if (boxTypeDef != null && boxTypeDef.BaseTypeName == "System.Enum")
                return $"il2cpp::runtime::icall_box_enum({BoxClassDescriptorCpp(boxType)}, static_cast<int64_t>({boxVal}))";
            if (KnownBclEnumNames.Contains(boxType))
                return $"il2cpp::runtime::icall_box_long(static_cast<int64_t>({boxVal}))";
            return boxType switch
            {
                "System.Int32" => $"il2cpp::il2cpp_box_int32(nullptr, {boxVal})",
                "System.Boolean" => $"il2cpp::runtime::icall_box_bool(static_cast<bool>({boxVal}))",
                "System.Char" => $"il2cpp::runtime::icall_box_char({boxVal})",
                "System.Double" => $"il2cpp::runtime::icall_box_double({boxVal})",
                "System.Int64" => $"il2cpp::runtime::icall_box_long({boxVal})",
                "System.Byte" => $"il2cpp::runtime::icall_box_byte({boxVal})",
                "System.SByte" => $"il2cpp::runtime::icall_box_sbyte({boxVal})",
                "System.Int16" => $"il2cpp::runtime::icall_box_short({boxVal})",
                "System.UInt16" => $"il2cpp::runtime::icall_box_ushort({boxVal})",
                "System.UInt32" => $"il2cpp::runtime::icall_box_uint({boxVal})",
                "System.UInt64" => $"il2cpp::runtime::icall_box_ulong({boxVal})",
                "System.Single" => $"il2cpp::runtime::icall_box_float(static_cast<double>({boxVal}))",
                // decimal-domain T3: the 16-byte value boxes by ADDRESS (the
                // icall copies the slot into a new System.Decimal box object).
                // Runtime symbol lands with T1 (boxingext2 + decimal.h).
                "System.Decimal" => $"il2cpp::runtime::icall_box_decimal(static_cast<il2cpp::runtime::Il2CppDecimal*>(&({boxVal})))",
                // box of System.Object is a no-op (already a reference type)
                "System.Object" => boxVal,
                // BCL pointer-wrapper handles (ModuleHandle, RuntimeTypeHandle,
                // RuntimeMethodHandle, RuntimeFieldHandle, AssemblyHash): box via
                // icall_box_long — no runtime class descriptor, pointer-sized
                // payload (8 bytes). MVP: boxed runtime type is Int64 (documented).
                "System.ModuleHandle" => $"il2cpp::runtime::icall_box_long(static_cast<int64_t>(reinterpret_cast<intptr_t>({boxVal})))",
                "System.RuntimeTypeHandle" => $"il2cpp::runtime::icall_box_long(static_cast<int64_t>(reinterpret_cast<intptr_t>({boxVal})))",
                "System.RuntimeMethodHandle" => $"il2cpp::runtime::icall_box_long(static_cast<int64_t>(reinterpret_cast<intptr_t>({boxVal})))",
                "System.RuntimeFieldHandle" => $"il2cpp::runtime::icall_box_long(static_cast<int64_t>(reinterpret_cast<intptr_t>({boxVal})))",
                "System.Configuration.Assemblies.AssemblyHash" => $"il2cpp::runtime::icall_box_long(static_cast<int64_t>(reinterpret_cast<intptr_t>({boxVal})))",
                // P25b (user struct box): a USER value type (a TypeDef whose
                // base is System.ValueType -- not an enum, not a BCL struct)
                // boxes through icall_box_user_struct with the struct's OWN
                // generated descriptor (&{T}_class), copying the sizeof({T})
                // payload bytes into the box. Reference-free MVP: a struct
                // with GC reference fields is REJECTED loudly here (boxing it
                // would place an object reference at slot 1 (offset 8), and
                // the boxed object's mark scan indexes the bitmap from the
                // object header -- a ref field would be miscounted as a pure
                // header, a GC-safety leak).
                _ when boxTypeDef != null && boxTypeDef.IsValueType =>
                    boxTypeDef.RefFieldCount > 0 && !boxType.StartsWith("System.")
                        ? throw new NotSupportedException($"box of {boxType} not supported -- user structs with GC reference fields are not yet boxable (P25b reference-free MVP)")
                        : $"il2cpp::runtime::icall_box_user_struct(&{TypeEmitter.TypeCppName(boxTypeDef)}_class, &({boxVal}), sizeof({TypeEmitter.TypeCppName(boxTypeDef)}))",
                _ => throw new NotSupportedException($"box of {boxType} not supported -- no runtime box helper (audit H3 + M5)"),
            };
        }
        if (c.Function.StartsWith("unbox_", StringComparison.Ordinal) && int.TryParse(c.Function[6..], out var unboxTok))
        {
            var unboxType = TypeTokenFullName(md, unboxTok);
            var unboxTypeDef = types.FirstOrDefault(x => x.MatchesFullName(unboxType));
            var unboxVal = EmitExpr(c.Args[0], md, types);
            return unboxType switch
            {
                "System.Int32" => $"il2cpp::il2cpp_unbox_int32({unboxVal})",
                "System.Boolean" => $"il2cpp::runtime::icall_unbox_bool({unboxVal})",
                "System.Char" => $"il2cpp::runtime::icall_unbox_char({unboxVal})",
                "System.Double" => $"il2cpp::runtime::icall_unbox_double({unboxVal})",
                "System.Int64" => $"il2cpp::runtime::icall_unbox_long({unboxVal})",
                "System.Byte" => $"il2cpp::runtime::icall_unbox_byte({unboxVal})",
                "System.SByte" => $"il2cpp::runtime::icall_unbox_sbyte({unboxVal})",
                "System.Int16" => $"il2cpp::runtime::icall_unbox_short({unboxVal})",
                "System.UInt16" => $"il2cpp::runtime::icall_unbox_ushort({unboxVal})",
                "System.UInt32" => $"il2cpp::runtime::icall_unbox_uint({unboxVal})",
                "System.UInt64" => $"il2cpp::runtime::icall_unbox_ulong({unboxVal})",
                "System.Single" => $"il2cpp::runtime::icall_unbox_float({unboxVal})",
                // decimal-domain T3: the unbox returns the 16-byte value by
                // value (the "dec"-kind temp assignment). Runtime symbol lands
                // with T1 (boxingext2 + decimal.h).
                "System.Decimal" => $"il2cpp::runtime::icall_unbox_decimal({unboxVal})",
                // P25b (user struct box): a USER value type unboxes through
                // il2cpp_unbox_user_struct -- the runtime validates the box
                // (its klass's base chain reaches System.ValueType; NRE/ICE on
                // failure) and returns the payload ADDRESS; the generated code
                // casts it to {T}* and dereferences to the {T} value. The
                // ResultKind (StackFold) routes the result temp to the {T}
                // C++ struct type so the local holds a real struct copy.
                 _ when unboxTypeDef != null && unboxTypeDef.IsValueType =>
                     $"(*reinterpret_cast<{TypeEmitter.TypeCppName(unboxTypeDef)}*>(il2cpp::runtime::il2cpp_unbox_user_struct({unboxVal})))",
                // unbox of System.Object: degenerate case (object is already a reference)
                "System.Object" => unboxVal,
                // unbox of enum types: enum values are boxed as int32/int64 -- unbox to int32
                var ubt when ubt.StartsWith("System.") => $"il2cpp::il2cpp_unbox_int32({unboxVal})",
                _ => throw new NotSupportedException($"unbox of {unboxType} not supported -- no runtime unbox helper (audit H3 + M5)"),
            };
        }
        if ((c.Function.StartsWith("isinst_", StringComparison.Ordinal) && int.TryParse(c.Function[7..], out var isTok))
            || (c.Function.StartsWith("castclass_", StringComparison.Ordinal) && int.TryParse(c.Function[10..], out isTok)))
        {
            // .NET isinst semantics: returns obj (type matches) or null (no match) -- NOT a bool!
            //   isinst:   match -> obj, no match -> null (as-operator semantics)
            //   castclass:match -> obj, null input -> null (D-CAST-NULL: ECMA castclass on
            //     a null reference is a NO-OP, NOT an InvalidCastException), non-null
            //     mismatch -> InvalidCastException. The mismatch arm is emitted as
            //     `(obj) == nullptr ? nullptr : throw` -- is_instance(null) is false, so
            //     without the null short-circuit a null input falls into the throw arm.
            //     (audit H4: the old implementation returned null, so the caller could not
            //     distinguish "conversion succeeded with a null input" from "conversion
            //     failed". Now the failure path throws a typed managed exception; throw_new
            //     always unwinds, so the comma-expression nullptr branch is unreachable (it
            //     only satisfies the compiler).)
            // M5 wiring: a TypeSpec target (generic instantiation) -- a
            // delegate instantiation (System.Func`N/System.Action`N) cast is
            // an IDENTITY (the receiver is already that delegate); other
            // generic instantiations have no runtime descriptor and are
            // rejected loudly.
            // M5 wiring: a TypeSpec target (generic instantiation) -- a
            // delegate instantiation (System.Func`N/System.Action`N) cast is
            // an IDENTITY (the receiver is already that delegate); other
            // generic instantiations have no runtime descriptor and are
            // rejected loudly. Must run BEFORE TypeTokenFullName (which does
            // not decode TypeSpec tokens).
            if (MetadataTokens.Handle(isTok).Kind == HandleKind.TypeSpecification)
            {
                var tsHandle = (TypeSpecificationHandle)MetadataTokens.Handle(isTok);
                // M7 typecheckcase: a SZARRAY (0x1D) TypeSpec target (`arr is
                // int[]`) is an ARRAY-CLASS conformance test, NOT a generic
                // instantiation. The runtime registers array classes under the
                // "[<element type>" convention (array.cpp); an unregistered
                // element array (double[] here) resolves to nullptr and the
                // isinst returns false -- .NET semantics (element types
                // differ).
                if (md.GetBlobReader(md.GetTypeSpecification(tsHandle).Signature).ReadByte() == 0x1D)
                {
                    var objCodeArr = EmitExpr(c.Args[0], md, types);
                    // nullptr_t fix (boxing/boxdeep/boxdeep2): a NULL literal
                    // operand must NOT enter reinterpret_cast<Il2CppObject*>
                    // ("reinterpret_cast from 'std::nullptr_t'" is ill-formed
                    // C++). isinst(null)/castclass(null) are null (castclass
                    // does not throw on null -- ECMA) -> constant-shortcut.
                    if (objCodeArr == "nullptr") return "nullptr";
                    var klassArr = SzArrayArrayClassCpp(md, tsHandle);
                    if (c.Function.StartsWith("castclass_", StringComparison.Ordinal))
                        return $"(il2cpp::il2cpp_is_instance({objCodeArr}, {klassArr}) ? reinterpret_cast<Il2CppObject*>({objCodeArr}) : (({objCodeArr}) == nullptr ? nullptr : (il2cpp::il2cpp_throw_new(\"System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception\", \"Unable to cast object to the target type.\"), nullptr)))";
                    return $"(il2cpp::il2cpp_is_instance({objCodeArr}, {klassArr}) ? reinterpret_cast<Il2CppObject*>({objCodeArr}) : nullptr)";
                }
                var specName = GenericTypeSpecDefinitionName(md, tsHandle);
                if (!specName.StartsWith("System.Func`", StringComparison.Ordinal)
                    && !specName.StartsWith("System.Action`", StringComparison.Ordinal))
                {
                    throw new NotSupportedException($"isinst/castclass of generic instantiation {specName} not wired in M5");
                }
                var objCode0 = EmitExpr(c.Args[0], md, types);
                // nullptr_t fix: see the SZARRAY arm above (null is the
                // identity-equivalent safe result for delegate isinst/identity).
                if (objCode0 == "nullptr") return "nullptr";
                return $"reinterpret_cast<Il2CppObject*>({objCode0})";
            }
            var typeName = TypeTokenFullName(md, isTok);
            var objCode = EmitExpr(c.Args[0], md, types);
            // nullptr_t fix (boxing/boxdeep/boxdeep2): null never enters the
            // is_instance/identity expressions below (each would wrap the
            // literal in reinterpret_cast<Il2CppObject*>(nullptr) -- ill-formed
            // C++); isinst(null)/castclass(null) are null (no throw on null).
            if (objCode == "nullptr") return "nullptr";
            // xmlenum: HANDLE-GAP identity for the System.Xml xml_doc.h types
            // (MethodMap.Xml.cs handle-gap contract: XmlDocument IS its root
            // node handle, XmlNode/XmlElement/XmlNodeList are raw pointers
            // carried in Il2CppObject* slots). Those types have NO runtime
            // class descriptor, so &System_Xml_XmlNode_class would be an
            // undefined symbol (link FAIL); the only reachable castclass in
            // the wired surface is the foreach element conversion over the
            // XmlNodeList enumerator (object -> XmlNode), where the
            // compile-time tracked type is the sole type information the
            // value has. Identity, same contract as the custom-delegate arm
            // below (D-CAST-NULL preserved: null input -> null).
            if (IsXmlHandleGapType(typeName))
                return $"reinterpret_cast<Il2CppObject*>({objCode})";
            // CG6 (delegate2/delegatecb link FAIL: "undefined symbol:
            // IntFn_class"/"Cb_class"): a custom delegate TypeDef has NO
            // runtime descriptor -- the runtime stamps EVERY delegate with
            // its single System.Delegate class (delegate.cpp g_delegateClass),
            // so {T}_class does not exist. A castclass/isinst to a custom
            // delegate type is therefore an IDENTITY: the value is already a
            // runtime delegate object (the generic System.Func/System.Action
            // identity precedent, above). The delegate type cannot be
            // distinguished at runtime anyway -- the checker has one class.
            if (IsCustomDelegateType(typeName, types))
                return $"reinterpret_cast<Il2CppObject*>({objCode})";
            // M7 CG-FIX (boxdeep2 cmake FAIL): box primitives WITHOUT a GLOBAL
            // descriptor (UInt32/Int64/UInt64/Byte/SByte/Int16/UInt16/Single/
            // Decimal -- boxingext2.cpp's g_*BoxClass are file-local, lazily
            // registered; only Int32/Boolean/Char/Double have the metadata.cpp
            // System_*_class globals) cannot reference &System_UInt32_class --
            // the symbol does not exist. Resolve the descriptor at runtime via
            // il2cpp_class_by_name("System.UInt32"): the box class is
            // registered by the first box icall of that type, and isinst
            // compares pointer identity against the object's stamped klass --
            // exactly the registered class. A type never boxed resolves to
            // nullptr -> isinst false (correct: nothing can be that box).
            var klassExpr = BoxClassDescriptorCpp(typeName);
            if (c.Function.StartsWith("castclass_", StringComparison.Ordinal))
            {
                var chain = "System.InvalidCastException,System.InvalidCastException,System.SystemException,System.Exception";
                // D-CAST-NULL (hashtabledeep): ECMA castclass on a null reference is a
                // NO-OP -- only a NON-NULL type mismatch throws. The null check sits on
                // the mismatch arm (the only path where a null input can arrive, because
                // is_instance(null) is false); on the match path the input is already
                // known non-null (is_instance implies obj != nullptr), so the expression
                // evaluates the input at most twice.
                return $"(il2cpp::il2cpp_is_instance({objCode}, {klassExpr}) ? reinterpret_cast<Il2CppObject*>({objCode}) : (({objCode}) == nullptr ? nullptr : (il2cpp::il2cpp_throw_new(\"{chain}\", \"Unable to cast object to the target type.\"), nullptr)))";
            }
            return $"(il2cpp::il2cpp_is_instance({objCode}, {klassExpr}) ? reinterpret_cast<Il2CppObject*>({objCode}) : nullptr)";
        }
        // M3 arrays: newarr/ldlen/ldelem/stelem helpers.
        if (c.Function.StartsWith("newarr_", StringComparison.Ordinal)
            && int.TryParse(c.Function[7..], out var arrTok))
        {
            // Audit C4: the element type decides the allocation -- reference
            // elements (string[]/object[]/user classes) need elemSize =
            // sizeof(void*) and isRefElement=true so the payload is scanned
            // as GC references and stelem_ref writes fit the stride. A
            // constant elemSize 4 (old behaviour) made every stelem_ref
            // overrun the allocation. Non-int32/non-reference types are
            // rejected loudly (never silently mis-allocated).
            var (elemSize, isRefElem) = ArrayElementType(md, arrTok, types);
            // D-TYPG-1: reference-element newarr passes the ELEMENT class so the
            // runtime stamps the array with an element-aware descriptor (string[]
            // -> "[System.String" -> GetType().Name "String[]"). Value-element
            // arrays keep nullptr (the fixed int32/byte/char/long array classes).
            // ilistException: a BCL element with NO generated descriptor
            // (System.Exception has no _class symbol) must NOT emit
            // &Missing_class (undeclared identifier). Resolve it at runtime
            // via il2cpp_class_by_name -- the M7 box-path precedent just above
            // (known descriptors resolve to the identical pointer; unknown
            // ones answer nullptr -> the shared object array, which compiles
            // and carries object elements correctly). Translated user types
            // keep the direct &Symbol form.
            var elemName = TypeTokenFullName(md, arrTok);
            var elemClassCpp = !isRefElem
                ? "nullptr"
                : types.Any(t => t.MatchesFullName(elemName))
                ? ArrayElementClassCpp(elemName)
                : $"il2cpp::il2cpp_class_by_name(\"{elemName}\")";
            return $"reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_array_new({elemClassCpp}, {elemSize}, {(isRefElem ? "true" : "false")}, static_cast<int32_t>({EmitExpr(c.Args[0], md, types)})))";
        }
        if (c.Function.StartsWith("ldlen_", StringComparison.Ordinal))
        {
            return $"il2cpp::il2cpp_array_get_length({NullSafePtrCast("Il2CppArray*", EmitExpr(c.Args[0], md, types))})";
        }
        if (c.Function.StartsWith("ldelem_", StringComparison.Ordinal))
        {
            // ldelem: *(T*)il2cpp_array_get_elem_ptr(arr, idx) -- width read.
            // Reference elements: *(Il2CppObject**)... (the opcode is encoded
            // in the function name -- audit C4: "ldelem_ref" must be detected).
            // P0-1 (hotpath-audit-2): on a provable constant-length newarr the
            // null check is elided and the bounds check compares against the
            // constant -- TryEmitFastArrayPtr returns the raw access, null
            // falls back to the checked helper.
            // M5 wiring: the element WIDTH comes from the opcode suffix
            // (Ldelem_u1/u2/i1/i2/i4/ref -- byte[] values from the Base64
            // icalls are reachable now): a 4-byte read of a 1-byte element
            // crosses the array end and reads neighbor garbage (probe-verified:
            // BytesEqual on two "SGVsbG8=" decodes compared different OOB
            // bytes at the last element). The ref form stays a pointer read.
            var ldelemSuffix = c.Function[(c.Function.LastIndexOf('_') + 1)..];
            var ldelemPtr = TryEmitLoopProvenArrayPtr(c.Args[0], c.Args[1], md, types)
                ?? TryEmitFastArrayPtr(c.Args[0], c.Args[1], md, types)
                ?? $"il2cpp::il2cpp_array_get_elem_ptr({NullSafePtrCast("Il2CppArray*", EmitExpr(c.Args[0], md, types))}, static_cast<int32_t>({EmitExpr(c.Args[1], md, types)}))";
            return ldelemSuffix switch
            {
                "ref" => $"static_cast<Il2CppObject*>(*reinterpret_cast<Il2CppObject**>({ldelemPtr}))",
                "u1" => $"(*reinterpret_cast<uint8_t*>({ldelemPtr}))",
                "i1" => $"(*reinterpret_cast<int8_t*>({ldelemPtr}))",
                "u2" => $"(*reinterpret_cast<uint16_t*>({ldelemPtr}))",
                "i2" => $"(*reinterpret_cast<int16_t*>({ldelemPtr}))",
                "u4" => $"(*reinterpret_cast<uint32_t*>({ldelemPtr}))",
                // ILOP-2 P9: 8-byte element reads (int64/uint64/double arrays)
                // + float element reads -- the M3 fixed-width set was extended
                // by StackFold (Ldelem_i8/Ldelem_r4/Ldelem_r8...).
                "u8" or "i8" or "i" => $"(*reinterpret_cast<int64_t*>({ldelemPtr}))",
                "r4" => $"static_cast<double>(*reinterpret_cast<float*>({ldelemPtr}))",
                "r8" => $"(*reinterpret_cast<double*>({ldelemPtr}))",
                _ => $"(*reinterpret_cast<int32_t*>({ldelemPtr}))",
            };
        }
        if (c.Function.StartsWith("stelem_", StringComparison.Ordinal))
        {
            // stelem (statement): *(T*)il2cpp_array_get_elem_ptr(arr, idx) = value;
            // Width dispatch like the ldelem handler (byte[] writes from the
            // Base64 icall results are reachable; a 4-byte store would overrun
            // the 1-byte element).
            var stelemSuffix = c.Function[(c.Function.LastIndexOf('_') + 1)..];
            var stelemPtr = TryEmitLoopProvenArrayPtr(c.Args[0], c.Args[1], md, types)
                ?? TryEmitFastArrayPtr(c.Args[0], c.Args[1], md, types)
                ?? $"il2cpp::il2cpp_array_get_elem_ptr(reinterpret_cast<Il2CppArray*>({EmitExpr(c.Args[0], md, types)}), static_cast<int32_t>({EmitExpr(c.Args[1], md, types)}))";
            return stelemSuffix switch
            {
                "ref" => $"(*reinterpret_cast<Il2CppObject**>({stelemPtr}) = static_cast<Il2CppObject*>({EmitExpr(c.Args[2], md, types)}))",
                "u1" or "i1" => $"(*reinterpret_cast<uint8_t*>({stelemPtr}) = static_cast<uint8_t>({EmitExpr(c.Args[2], md, types)}))",
                "u2" or "i2" => $"(*reinterpret_cast<uint16_t*>({stelemPtr}) = static_cast<uint16_t>({EmitExpr(c.Args[2], md, types)}))",
                // ILOP-2 P9: 8-byte element writes (int64/uint64/double arrays)
                // and 4-byte float writes -- the M3 fixed-width ldelem/stelem
                // set was extended by StackFold (Ldelem_i8/Ldelem_r4/...).
                "u8" or "i8" or "i" => $"(*reinterpret_cast<int64_t*>({stelemPtr}) = static_cast<int64_t>({EmitExpr(c.Args[2], md, types)}))",
                "r4" => $"(*reinterpret_cast<float*>({stelemPtr}) = static_cast<float>({EmitExpr(c.Args[2], md, types)}))",
                "r8" => $"(*reinterpret_cast<double*>({stelemPtr}) = static_cast<double>({EmitExpr(c.Args[2], md, types)}))",
                _ => $"(*reinterpret_cast<int32_t*>({stelemPtr}) = static_cast<int32_t>({EmitExpr(c.Args[2], md, types)}))",
            };
        }
        // ILOP-2 P1: ldind_{op} -- pointer dereference by opcode width
        // (Ldind_i1/i2/i4/i8/i/u1/u2/u4/r4/r8/ref). The opcode is the Function
        // suffix; the address is Args[0]. The dereference is a typed load
        // through the address (the IL `ldind.*` loads a value from a managed
        // or native pointer -- StackFold folded it as a marker CallExpr).
        if (c.Function.StartsWith("ldind_", StringComparison.Ordinal))
        {
            var ldindSuffix = c.Function[(c.Function.LastIndexOf('_') + 1)..];
            var ldindAddr = EmitExpr(c.Args[0], md, types);
            return ldindSuffix switch
            {
                "i1" => $"(*reinterpret_cast<int8_t*>({ldindAddr}))",
                "u1" => $"(*reinterpret_cast<uint8_t*>({ldindAddr}))",
                "i2" => $"(*reinterpret_cast<int16_t*>({ldindAddr}))",
                "u2" => $"(*reinterpret_cast<uint16_t*>({ldindAddr}))",
                "i4" or "u4" => $"(*reinterpret_cast<int32_t*>({ldindAddr}))",
                "i8" => $"(*reinterpret_cast<int64_t*>({ldindAddr}))",
                "i" => $"(*reinterpret_cast<intptr_t*>({ldindAddr}))",
                "r4" => $"static_cast<double>(*reinterpret_cast<float*>({ldindAddr}))",
                "r8" => $"(*reinterpret_cast<double*>({ldindAddr}))",
                "ref" => $"static_cast<Il2CppObject*>(*reinterpret_cast<Il2CppObject**>({ldindAddr}))",
                _ => throw new NotSupportedException($"ldind opcode suffix {ldindSuffix} not supported in emission"),
            };
        }
        // ILOP-2 P3/P4: ldobj_{token} -- load a value type by TYPE TOKEN
        // (the width comes from the metadata type). Primitives use the same
        // typed dereference as ldind; user value types use their generated
        // struct name; DateTime/Decimal/Guid use the runtime structs.
        if (c.Function.StartsWith("ldobj_", StringComparison.Ordinal)
            && int.TryParse(c.Function["ldobj_".Length..], out var ldobjTok))
        {
            var ldobjName = TypeTokenFullName(md, ldobjTok);
            var ldobjAddr = EmitExpr(c.Args[0], md, types);
            if (c.ReturnsRef)
                return $"static_cast<Il2CppObject*>(*reinterpret_cast<Il2CppObject**>({ldobjAddr}))";
            return ldobjName switch
            {
                "System.SByte" => $"(*reinterpret_cast<int8_t*>({ldobjAddr}))",
                "System.Byte" or "System.Boolean" => $"(*reinterpret_cast<uint8_t*>({ldobjAddr}))",
                "System.Int16" or "System.Char" => $"(*reinterpret_cast<int16_t*>({ldobjAddr}))",
                "System.UInt16" => $"(*reinterpret_cast<uint16_t*>({ldobjAddr}))",
                "System.Int32" or "System.UInt32" => $"(*reinterpret_cast<int32_t*>({ldobjAddr}))",
                "System.Int64" or "System.UInt64" => $"(*reinterpret_cast<int64_t*>({ldobjAddr}))",
                "System.IntPtr" or "System.UIntPtr" => $"(*reinterpret_cast<intptr_t*>({ldobjAddr}))",
                "System.Single" => $"static_cast<double>(*reinterpret_cast<float*>({ldobjAddr}))",
                "System.Double" => $"(*reinterpret_cast<double*>({ldobjAddr}))",
                "System.Decimal" => $"(*reinterpret_cast<il2cpp::runtime::Il2CppDecimal*>({ldobjAddr}))",
                "System.DateTime" => $"(*reinterpret_cast<il2cpp::runtime::Il2CppDateTime*>({ldobjAddr}))",
                "System.TimeSpan" => $"(*reinterpret_cast<il2cpp::runtime::Il2CppTimeSpan*>({ldobjAddr}))",
                "System.DateTimeOffset" => $"(*reinterpret_cast<il2cpp::runtime::Il2CppDateTimeOffset*>({ldobjAddr}))",
                "System.Numerics.BigInteger" => $"(*reinterpret_cast<il2cpp::runtime::Il2CppBigInteger*>({ldobjAddr}))",
                _ => $"(*reinterpret_cast<{TypeCppName(ldobjName)}*>({ldobjAddr}))",
            };
        }
        // ILOP-2 P6: sizeof_{token} -- the byte size of a value type
        // (sizeof <T> on a non-primitive type; primitives folded to constants
        // at fold time -- only user/BCL value types reach the emitter).
        if (c.Function.StartsWith("sizeof_", StringComparison.Ordinal)
            && int.TryParse(c.Function["sizeof_".Length..], out var sizeofTok))
        {
            var sizeofName = TypeTokenFullName(md, sizeofTok);
            return $"static_cast<int32_t>(sizeof({TypeCppName(sizeofName)}))";
        }
        // ILOP-2 P7: ldelema_{token} -- address of array element [idx]
        // (the type token is descriptive only; the address read is
        // width-independent -- il2cpp_array_get_elem_ptr, same as ldelem).
        if (c.Function.StartsWith("ldelema_", StringComparison.Ordinal))
        {
            var ldelemaPtr = TryEmitLoopProvenArrayPtr(c.Args[0], c.Args[1], md, types)
                ?? TryEmitFastArrayPtr(c.Args[0], c.Args[1], md, types)
                ?? $"il2cpp::il2cpp_array_get_elem_ptr({NullSafePtrCast("Il2CppArray*", EmitExpr(c.Args[0], md, types))}, static_cast<int32_t>({EmitExpr(c.Args[1], md, types)}))";
            return $"reinterpret_cast<intptr_t>({ldelemaPtr})";
        }
        // ILOP-2 P8: il2cpp::il2cpp_localloc -- stack-frame allocation
        // (alloca -- the frame lives until the method returns, exactly the
        // .NET localloc semantics). alloca must be emitted INLINE at the call
        // site; no runtime helper can provide it (it cannot leave the caller's
        // frame). Cross-platform: MSVC uses _alloca (<malloc.h>), GCC/Clang
        // use alloca (<alloca.h>); both are declared in generated.h.
        if (c.Function == "il2cpp::il2cpp_localloc")
        {
            // PTR-SIG: the result feeds pointer locals (stackalloc char* p =
            // ...): reinterpret to intptr_t so the assignment into the
            // pointer-slot local compiles (void* -> integer needs an
            // explicit cast; the slot arithmetic then indexes it directly).
            return $"reinterpret_cast<intptr_t>(il2cpp_localloc(static_cast<size_t>({EmitExpr(c.Args[0], md, types)})))";
        }
        // ILOP-2 P8: cpblk/initblk passthrough -- folded as CallStmt, handled
        // in the statement emitter (memcpy/memset). A value-position CallExpr
        // never carries them (side-effecting statements only).
        if (c.Function is "cpblk" or "initblk")
            throw new NotSupportedException($"ILOP-2 {c.Function} reached expression position: it is a side-effecting statement");
        // D-INT-1: the checked arithmetic family (add.ovf/sub.ovf/mul.ovf +
        // the _un unsigned forms, rem.un) folds to il2cpp::il2cpp_checked_*
        // (StackFold.cs). The int32 path casts operands explicitly (the fold
        // carries no literal width; IntConstExpr renders "...LL"), and uint32
        // rides the int32 slot. i64ck-b23: a 64-bit fold (StackFold sets
        // ResultKind "l", or either operand is a 64-bit expression) routes to
        // the il2cpp_checked_*_64 helpers (exception.cpp) -- the int32 helpers
        // would truncate the operands, so they are emitted verbatim (no casts;
        // int64/uint64 operands are already int64_t slots; the _un_64 forms
        // take/return int64_t bit patterns).
        if (c.Function.StartsWith("il2cpp::il2cpp_checked_", StringComparison.Ordinal))
        {
            var is64 = c.ResultKind == "l" || ExprIsInt64(c.Args[0]) || ExprIsInt64(c.Args[1]);
            if (is64)
                return $"{c.Function}_64({EmitExpr(c.Args[0], md, types)}, {EmitExpr(c.Args[1], md, types)})";
            return $"{c.Function}(static_cast<int32_t>({EmitExpr(c.Args[0], md, types)}), static_cast<int32_t>({EmitExpr(c.Args[1], md, types)}))";
        }
        // ILOP-2 ckfinite + checked arithmetic passthrough: the runtime icall
        // names are fully-qualified (il2cpp::runtime::icall_ckfinite /
        // il2cpp::il2cpp_checked_{add,sub,mul,...}), so the generic call
        // emission below renders them verbatim (no local rewrite needed).
        // Ordinary call: call_{token} -> the real CppName + arguments.
        // (audit H5: P/Invoke string args are pre-marshalled into their own
        // heap buffers by StackFold -- see MarshalUtf8Expr -- and freed after
        // the call; the old in-place wrap made multiple string args alias the
        // runtime's single rotating static buffer.)
        // M5 wiring: the fold-time resolution (c.Resolved) carries the
        // receiver-type-aware answer + the icall arg-cast codes; user-method
        // calls resolve by token as before. "fcap" collapses to a field read.
        var resolved2 = c.Resolved ?? ResolveByToken(c.Function, md, types);
        // perf2 str_chars: the string indexer `s[i]` -- when the loop-bound
        // proof certified this exact access, emit the raw UTF-16 read instead
        // of the icall boundary (no bounds/null check; both statically dead).
        // Must run BEFORE the generic resolved-call emission below.
        if (resolved2?.CppName == "il2cpp::runtime::icall_string_get_chars" && c.Args.Count == 2)
        {
            var strFast = TryEmitLoopProvenStringChars(c.Args[0], c.Args[1], md, types);
            if (strFast != null) return strFast;
        }
        // Guid value-slot guard: NewGuid/Parse write through their output
        // buffer (void icalls -- the assignment statement form is the ONLY
        // valid emission; see the AssignStmt emitter). A "guid"-kind call
        // reaching the EXPRESSION path means the fold's single-use temp
        // inlining consumed it directly (e.g. `return Guid.Parse(s)`) --
        // reject LOUDLY instead of emitting uncompilable C++.
        if (resolved2?.ResultKind is "guid" or "dec" or "dec2" or "ip4" or "bi")
            throw new NotSupportedException($"{(resolved2.ResultKind == "dec" ? "decimal icall" : resolved2.ResultKind == "dec2" ? "decimal icall(dec2)" : resolved2.ResultKind == "ip4" ? "IPAddress.Parse" : resolved2.ResultKind == "bi" ? "BigInteger icall" : "Guid.NewGuid/Parse")} [{resolved2.CppName}] call in expression position: the icall takes the result slot as an explicit parameter, so the result must be assigned to a value slot (the fold inlined the single-use temp into the consumer -- StackFold must keep {resolved2.ResultKind}-kind calls in their assignment)");
        if (resolved2?.ParamTypeNames is { Length: > 0 } fcap && fcap[0] == "fcap")
            return EmitIcallCallArgs(resolved2, c.Args, md, types)!;
        // M7 CG-FIX (boxdeep2 cmake FAIL): Object::ToString on a boxed uint /
        // Type receiver -- the resolver's ToString receiver-type list (MethodMap
        // .Object.cs, read-only) omits "System.UInt32"/"System.Type", so it fell
        // to the mangled generated-name path (System_Object_ToString_<hash> --
        // an undefined symbol). The runtime icall_object_to_string dispatches on
        // the receiver's runtime class and prints every boxed primitive; a
        // GetType() result IS the type-name string, so ToString on it is the
        // identity -> the full name. (boxdeep2 GAP-1/GAP-2 PROBE-1/PROBE-2).
        if (resolved2?.CppName.StartsWith("System_Object_ToString_", StringComparison.Ordinal) == true
            && c.Args.Count > 0)
            return $"static_cast<Il2CppObject*>(il2cpp::runtime::icall_object_to_string({EmitExpr(c.Args[0], md, types)}))";
        var callArgs2 = resolved2 != null
            ? EmitIcallCallArgs(resolved2, c.Args, md, types)
            : string.Join(", ", c.Args.Select(a => EmitExpr(a, md, types)));
        // M7 CG-FIX (excext): the get_Message carrier CppName is unbalanced
        // (see IsMessageCarrierCppName) -- emit the nested call directly with
        // the missing closing paren (no generic `(...)` wrapper; the receiver
        // is dropped and args are empty).
        var cppName2 = resolved2?.CppName ?? EmitFunctionName(c.Function, md, types);
        var callCode2 = IsMessageCarrierCppName(cppName2)
            ? cppName2 + ")"
            : $"{cppName2}({callArgs2})";
        // P-D4 (mixed loading): a DYNAMIC P/Invoke call site routes through
        // the runtime resolver -- the transpiler casts the resolved pointer to
        // the native signature (the SAME types the extern "C" declaration
        // would have named) and calls through it:
        //   ((Ret(*)(Params))il2cpp::il2cpp_pinvoke_resolve("dll","entry"))(args)
        // The args already carry the il2cpp_pinvoke_arg bridge (the cast's
        // parameter types deduce the native conversions). The runtime caches
        // per (dll, entry), so repeated calls are a table lookup. Failure
        // (library/symbol missing) is a managed exception at the call
        // boundary -- DllNotFoundException / EntryPointNotFoundException.
        if (resolved2 is { IsPInvoke: true, PinvokeModule: not null } pm
            && TryPinvokeDynamicSig(pm.PinvokeModule, cppName2, out var dynRet, out var dynParams))
        {
            callCode2 = $"(({dynRet}(*)({dynParams}))il2cpp::il2cpp_pinvoke_resolve(\"{pm.PinvokeModule}\",\"{cppName2}\"))({callArgs2})";
        }
        // P/Invoke "ptr" result bridge (il2cpp_pinvoke_bridge.h): the native
        // return may be an SDK pointer/size type (HWND/HMODULE/LRESULT/...)
        // while the managed carrier is the intptr_t temp. Wrap the call in the
        // converting helper -- it reinterprets a pointer result back to the
        // intptr_t carrier (an IntPtr-typed native return passes through).
        if (resolved2 is { IsPInvoke: true, ResultKind: "ptr" })
            return $"il2cpp::runtime::il2cpp_pinvoke_result<intptr_t>({callCode2})";
        // M5 wiring: the runtime icalls return Il2CppString* where the IR's
        // reference representation is Il2CppObject* (Il2CppString is NOT a
        // derived class -- the two are unrelated structs). Ref-typed call
        // results are always consumed as Il2CppObject*, so the cast is exact.
        // CG6 (delegatecb): icall_delegate_get_fn_ptr returns void* while the
        // "ptr" result temp is declared intptr_t (CodeGenerator.cs:1027) --
        // `intptr_t t = void*` failed to compile. Normalize THIS one void*-
        // returning ptr icall to the intptr_t carrier (every other "ptr"-kind
        // icall already returns intptr_t -- verified marshalext3/runtime/
        // pinvokeext/gchandle). NOT a blanket "ptr" wrap: P/Invoke IntPtr
        // returns (reverse_static) already return intptr_t and a redundant
        // reinterpret changed the emitted text shape.
        if (resolved2?.CppName == "il2cpp::runtime::icall_delegate_get_fn_ptr")
            return $"reinterpret_cast<intptr_t>({callCode2})";
        return resolved2 is { ReturnsRef: true } && c.Function.StartsWith("call_", StringComparison.Ordinal)
            ? $"reinterpret_cast<Il2CppObject*>({callCode2})"
            : callCode2;
    }

    // call_{token} placeholder -> resolved from the token when the fold is missing it (user method).
    private static ResolvedMethod? ResolveByToken(string function, MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        if (function.StartsWith("call_", StringComparison.Ordinal) && int.TryParse(function[5..], out var token))
            return MethodRefResolver.Resolve(md, token, types);
        return null;
    }

    //--------------------------------------------------------------------------
    // EmitCalliCall -- calli (0x29, fnptr domain b23) indirect call emission.
    // The fold produced CallExpr{Function="calli_{token}", Args=[fnptr, ...]}.
    // Decode the StandAloneSig signature (ECMA-335 I.12.3.3.3): the calling
    // convention byte selects the C++ calling-convention keyword, the params
    // build the target function-pointer type, the return type types the call.
    // Emitted as (ECMA-335 III.3.19 + corert CppWriter pattern):
    //     ((retCpp{CONV}(*)(paramCpp...))fnptr)(args...)
    // with a null-pointer guard: a null fnptr is a .NET
    // NullReferenceException (the runtime throws it via il2cpp_throw_new).
    //--------------------------------------------------------------------------
    private static string EmitCalliCall(CallExpr c, int token, MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        var handle = MetadataTokens.Handle(token);
        if (handle.Kind != HandleKind.StandaloneSignature)
            throw new NotSupportedException($"calli token 0x{token:X8} is not a StandAloneSig");
        var (callingConvention, hasThis, paramCount, returnType, paramTypes) =
            MethodSignatureCoder.DecodeStandalone(md, md.GetStandaloneSignature((StandaloneSignatureHandle)handle).Signature);
        // Target function-pointer C++ types: return + params. Params are the
        // encoded types (i->int32_t, r:->Il2CppObject*); a HAS_THIS target
        // prepends the implicit receiver (Il2CppObject*).
        var retCpp = CalliReturnCpp((byte)returnType);
        var paramCpp = paramTypes.Length == 0 ? new List<string>() : SplitTopLevel(paramTypes).Select(p => CalliParamCpp(p)).ToList();
        var fnSig = hasThis ? new[] { "Il2CppObject*" }.Concat(paramCpp).ToList() : paramCpp;
        // Unmanaged calling convention keyword (cross-platform macros defined
        // in the generated header; empty except 32-bit Windows where the
        // conventions differ).
        var convMacro = callingConvention switch
        {
            0x00 => "",                       // DEFAULT (managed fnptr)
            0x01 => "IL2CPP_CALLCONV_CDECL ",
            0x02 => "IL2CPP_CALLCONV_STDCALL ",
            0x03 => "IL2CPP_CALLCONV_THISCALL ",
            0x04 => "IL2CPP_CALLCONV_FASTCALL ",
            _ => throw new NotSupportedException($"calli calling convention 0x{callingConvention:X2} not supported in fnptr-b23"),
        };
        var fnptrCode = EmitExpr(c.Args[0], md, types);
        // The fnptr expression may be a FuncPtrExpr (&Method), a local holding
        // a pointer, or a null literal -- emit once into the guard and cast.
        var callArgs = string.Join(", ", c.Args.Skip(1).Select(a => EmitExpr(a, md, types)));
        var callCode = $"(({retCpp}({convMacro}*)({string.Join(", ", fnSig)})){fnptrCode})({callArgs})";
        // Null-pointer guard -> NRE. The false arm is unreachable (throw_new
        // unwinds); the cast keeps the ternary type-consistent for the C++
        // compiler (same pattern as the castclass comma-expression).
        var nreChain = ExceptionChain("System.NullReferenceException", types);
        return retCpp == "void"
            ? $"({fnptrCode} != nullptr ? {callCode} : (il2cpp::il2cpp_throw_new(\"{nreChain}\", \"NullReferenceException: call through null function pointer\"), (void)0))"
            : $"({fnptrCode} != nullptr ? {callCode} : (il2cpp::il2cpp_throw_new(\"{nreChain}\", \"NullReferenceException: call through null function pointer\"), ({retCpp})0))";
    }

    // calli target RETURN C++ type. The return byte may be void, an
    // int32/int64/double slot, or a managed reference (raw element bytes:
    // 0x01 void, 0x08/0x09 int32, 0x0A/0x0B int64, 0x0C/0x0D double, 0x0E/0x12/
    // 0x1C/0x1D/0x1F reference). int64/double returns widen the temp slot.
    private static string CalliReturnCpp(byte returnByte)
    {
        if (returnByte == 0x01) return "void";
        // int32-width slots (incl. bool/char/short/native-int -- all int32_t
        // in the object model): the default C++ type for a calli result.
        if (returnByte is 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x07 or 0x08 or 0x09 or 0x17) return "int32_t";
        if (returnByte is 0x0A or 0x0B) return "int64_t";
        if (returnByte is 0x0C or 0x0D) return "double";
        if (returnByte is 0x0E or 0x12 or 0x1C or 0x1D or 0x1F) return "Il2CppObject*";
        // P-D3 (§7.3 calli whitelist): 0x18 ELEMENT_TYPE_I (IntPtr/native-int)
        // must carry the FULL pointer width -- the old int32_t collapsed the
        // x64 address. 0x0F (PTR) and 0x1B (FNPTR) returns are raw pointer
        // carriers (void*); a subsequent calli rides them directly (the "p"
        // slot), matching ReturnTypeCpp's 0x0F -> void* rule.
        if (returnByte == 0x18) return "intptr_t";
        if (returnByte is 0x0F or 0x1B) return "void*";
        // Value types (0x11) / TypeHandle (0x40) returns: reject loudly (no
        // runtime value carrier -- the fnptr domain emits
        // int32/int64/double/ref/pointer slots).
        throw new NotSupportedException($"calli return type 0x{returnByte:X2} not supported in fnptr-b23 (only int32/int64/double/ref/void/ptr)");
    }

    // calli target PARAMETER C++ type (encoded type -> C++ type).
    private static string CalliParamCpp(string encoded)
    {
        if (encoded.StartsWith("r:", StringComparison.Ordinal)) return "Il2CppObject*";
        // P-D3 (§7.3 calli whitelist): a fnptr (F:) or pointer (P:) parameter
        // IS a void* at full pointer width -- the call-side fnptr/pointer
        // value feeds the slot directly (no teardown).
        if (encoded.StartsWith("F:", StringComparison.Ordinal)) return "void*";
        if (encoded.StartsWith("P:", StringComparison.Ordinal)) return "void*";
        return encoded switch
        {
            "i" or "b" or "c" or "i1" or "u1" or "i2" or "u2" or "n" => "int32_t",
            "l" or "q" => "int64_t",
            "f" or "d" => "double",
            _ => throw new NotSupportedException($"calli parameter type '{encoded}' not supported in fnptr-b23 (only int32/int64/double/ref/ptr)"),
        };
    }

    //--------------------------------------------------------------------------
    // NullSafePtrCast -- CAST-null (cg4, convertcase cmake_build FAIL): GCC
    // rejects `reinterpret_cast<T*>(nullptr)` ("reinterpret_cast from
    // 'std::nullptr_t' to 'T*' is not allowed"). A null literal is IMPLICITLY
    // convertible to every pointer type, so it needs no cast -- emit it bare.
    // Any non-null operand keeps the explicit reinterpret_cast (the exact-
    // address re-interpretation contract for reference -> concrete pointer
    // casts). EmitExpr(NullExpr) renders exactly "nullptr", so the string
    // equality is the null-literal detector.
    //--------------------------------------------------------------------------
    private static string NullSafePtrCast(string targetPtr, string operand)
        => operand == "nullptr" ? operand : $"reinterpret_cast<{targetPtr}>({operand})";

    //--------------------------------------------------------------------------
    // IcallArgOrder -- CG7 (arrayext2 unlock): the resolver wires the .NET
    // 4-arg Array.FindIndex<T> overload to the runtime delegate icall, but
    // the IL argument order and the icall parameter order differ:
    //   IL   pushes (array, startIndex, count, match)   -- match is LAST
    //   icall_array_find_index(arr, predicate, startIndex, count)
    // EmitIcallCallArgs emits arguments positionally with no reordering
    // facility, so without a mapping the delegate lands in the count slot
    // and is static_cast<int32_t> (a C++ compile error). This table is the
    // minimal order-declaration mechanism: for every reordered icall it maps
    // each ICALL parameter slot to the IL argument index that supplies it.
    // The resolver's ParamTypeNames casts stay indexed by ICALL position
    // (MethodMap.Array.cs writes them in icall order -- the "a","","i","i"
    // table below pairs slot-by-slot with this mapping). Absent = identity.
    //--------------------------------------------------------------------------
    private static readonly Dictionary<string, int[]> IcallArgOrder = new()
    {
        // slot0 (arr) <- args[0] (array); slot1 (predicate) <- args[3] (match);
        // slot2 (startIndex) <- args[1]; slot3 (count) <- args[2].
        ["il2cpp::runtime::icall_array_find_index"] = new[] { 0, 3, 1, 2 },
        // Buffer.MemoryCopy IL order (source, destination, destSize,
        // srcBytes) -> runtime sized order (dst, src, destSize, srcBytes):
        // slot0 (dst) <- args[1]; slot1 (src) <- args[0]; rest identity.
        ["il2cpp::runtime::il2cpp_buffer_memory_copy_sized"] = new[] { 1, 0, 2, 3 },
    };

    //--------------------------------------------------------------------------
    // EmitIcallCallArgs -- M5 BCL wiring: build the argument list of an icall
    // call from the fold-time resolution (ResolvedMethod.ParamTypeNames are
    // C++ cast codes; see MethodRefResolver for the table). Handles:
    //   * prepend markers (String.Concat -> leading count, Hashtable() -> 0)
    //   * "fcap" receiver -> StringBuilder.Capacity direct field read (the
    //     WHOLE call collapses to the field expression -- no function call)
    //   * "deref:*" value-type receivers (ldloca addresses)
    //   * "unbox:*" boxed receivers (il2cpp_unbox_* helpers)
    //   * "drop" args (format strings -- not honored yet)
    // Returns null when the call collapses to a field read (EmitCallStmt
    // emits the field expression instead of a function call).
    //--------------------------------------------------------------------------
    //--------------------------------------------------------------------------
    // PinvokeArgBridge -- P/Invoke native-ABI argument adaptation (see
    // src/runtime/include/internal/il2cpp_pinvoke_bridge.h). The generated
    // code does NOT restate the SDK prototype on Windows (windows.h owns it),
    // so each argument is wrapped in the il2cpp_pinvoke_arg conversion object:
    // its templated conversion operator deduces the NATIVE parameter type at
    // the call target and performs the exact bridge (pointer target ->
    // reinterpret_cast for IntPtr handles / char16_t* strings / mirror struct
    // pointers, arithmetic target -> static_cast). A nullptr literal stays
    // bare (reinterpret_cast from std::nullptr_t is ill-formed -- the
    // NullSafePtrCast precedent; nullptr converts to any pointer implicitly).
    //--------------------------------------------------------------------------
    private static string PinvokeArgBridge(string code)
        => code == "nullptr" ? code : $"il2cpp::runtime::il2cpp_pinvoke_arg({code})";

    private static string? EmitIcallCallArgs(ResolvedMethod r, List<Expr> args, MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        if (r.CppName == "il2cpp::runtime::il2cpp_object_ctor_nop")
        {
            // M6 ctor-carrier fix (exdeep MyException.cpp): the BCL exception
            // ctor no-op takes ONLY this_ -- the runtime signature is
            // il2cpp_object_ctor_nop(Il2CppObject* this_). The resolver's
            // drop-cast list aligns with the MESSAGE args and has no receiver
            // slot, so the generic cast walk would drop the receiver and pass
            // the message instead (or nothing for the parameterless base
            // ctor) -- "too few arguments ... 'this_' was not specified".
            // The receiver is always args[0]; everything else is the message
            // carrier the runtime ignores.
            return args.Count > 0 ? EmitExpr(args[0], md, types) : "";
        }
        if (r.ParamTypeNames is { Length: > 0 } casts && casts[0] == "fcap")
        {
            // M7 CG-FIX (sblong/stringbuilder cmake FAIL): the receiver chain
            // opens `((reinterpret_cast<T*>(il2cpp_null_check(static_cast<
            // Il2CppObject*>(X))))` -- the outer TWO parens wrap the whole
            // member access `->capacity`, so the expression must close BOTH:
            // `((...)->capacity)`. The old emission dropped the final `)` and
            // the C++ compiler reported "expected ')'" at the `->capacity`.
            return $"((reinterpret_cast<il2cpp::Il2CppStringBuilder*>(il2cpp_null_check(static_cast<Il2CppObject*>({EmitExpr(args[0], md, types)}))))->capacity)";
        }
        var parts = new List<string>();
        if (r.PrependCountArg) parts.Add(r.ParamCount.ToString());
        if (r.PrependZeroArg) parts.Add("0");
        // enumdeep (M7R2): il2cpp_enum_to_string(Il2CppClass*, int64_t) --
        // prepend the enum's generated class descriptor (name-bearing, so
        // the runtime registry lookup by name finds the registered metadata).
        if (r.PrependTypeClass != null) parts.Add($"&{TypeEmitter.Sanitize(r.PrependTypeClass)}_class");
        var casts2 = r.ParamTypeNames;
        // CG7 (arrayext2): the IcallArgOrder table maps each ICALL parameter
        // slot to the IL argument that supplies it (null = identity). The
        // cast codes (casts2) are indexed by ICALL slot -- the resolver wires
        // them in icall order (MethodMap.Array.cs ARG-ORDER CONSTRAINT), so
        // slot j reads casts2[j] while the EXPRESSION comes from args[order[j]].
        IcallArgOrder.TryGetValue(r.CppName, out var tableOrder);
        // B4 (dtoprobe): a per-resolution order wins over the table (shared
        // icalls serve several shapes; the table keys CppName globally).
        var argOrder = r.ArgOrder ?? tableOrder;
        var slotCount = argOrder != null ? argOrder.Length : args.Count;
        // M7 CG-FIX (lockcase cmake FAIL): Monitor.Enter(object, ref bool
        // lockTaken) -- the runtime signature is (Il2CppObject*, bool*), but
        // the M1 ABI carries bool in an int32_t slot, so the fold's ldloca
        // address is int32_t* and `il2cpp_monitor_enter(obj, (&lockTaken))`
        // fails to initialize a bool* param. The lockTaken flag is ALWAYS the
        // LAST argument and always a LocAddrExpr address; cast it to bool*
        // (exact-address reinterpret -- the runtime writes *lockTaken = true,
        // the int32 0/1 bit pattern is layout-compatible with bool).
        // D-THR3-2 (2026-08-11): the same bool* flag-cast must apply to the
        // 2-arg Monitor.TryEnter(obj, ref bool) -- its runtime overload
        // il2cpp_monitor_try_enter(Il2CppObject*, bool*) (monitor.h) takes
        // the flag as a bool* too, and a bare `(&loc)` (int32_t*) matches
        // NO try_enter overload (the threadext2 un-park failed the C++
        // build at HEAD). The 1-arg TryEnter(obj) never reaches here: its
        // last slot is the object ref, not a LocAddrExpr.
        // parity-T8: the same flag-cast covers the 3-arg TryEnter(object,
        // timeoutMs, ref bool) -- its runtime overload takes bool* too and
        // the flag is likewise the LAST slot.
        var isMonitorEnter = r.CppName == "il2cpp::runtime::il2cpp_monitor_enter";
        var isMonitorTryEnterFlag = r.CppName == "il2cpp::runtime::il2cpp_monitor_try_enter"
            && (r.ParamCount == 2 || r.ParamCount == 3);
        for (var j = 0; j < slotCount; j++)
        {
            var srcIdx = argOrder != null ? argOrder[j] : j;
            // B4 (dtoprobe): slot literal -- -1 emits nullptr (a slot with
            // no IL source). Placed BEFORE the bounds check (no args entry
            // exists) and before cast handling (no cast applies).
            if (srcIdx == -1) { parts.Add("nullptr"); continue; }
            if (srcIdx >= args.Count)
                throw new NotSupportedException(
                    $"icall '{r.CppName}' arg-order slot {j} references IL argument {srcIdx} but the call carries only {args.Count} argument(s)");
            var code = EmitExpr(args[srcIdx], md, types);
            var cast = casts2 != null && j < casts2.Length ? casts2[j] : null;
            if ((isMonitorEnter || isMonitorTryEnterFlag) && j == slotCount - 1 && args[srcIdx] is LocAddrExpr && cast is "" or null)            {
                parts.Add($"reinterpret_cast<bool*>({code})");
                continue;
            }
            if (cast == null || cast.Length == 0)
            {
                // "" (or no resolver cast) = RAW PASSTHROUGH: the argument
                // expression is already the icall's parameter -- the ldloca
                // ADDRESS of a ref/byref slot (Interlocked's int32_t*/int64_t*
                // location, value-type ctor receivers, Monitor ref bools,
                // ...). NOTE (interlockedcase regression fix, 2026-08-05):
                // commit b59c6d9 blanket-cast EVERY ""-cast LocAddrExpr arg
                // to (uint8_t*) because Guid value slots (uint8_t[16] arrays)
                // have pointer-to-array addresses that do NOT decay -- but
                // Interlocked int32_t*/int64_t* locations and value-type
                // ctor Point* receivers broke under it. The cast is now
                // carried EXPLICITLY by the "g" cast code (MethodMap.Guid.cs)
                // -- "" stays a pure passthrough, exactly like the other
                // no-cast receivers above.
                parts.Add(r.IsPInvoke ? PinvokeArgBridge(code) : code);
                continue;
            }
            if (cast == "drop")
            {
                // cultureprobe (provider routing): the double/decimal custom
                // formatters upgraded their runtime ABI with a trailing
                // IFormatProvider slot; the unit-pinned "drop" cast stays for
                // the mapping SHAPE, but the flagged icalls must PASS the
                // argument through (null/zh-CN/other -> runtime zh-CN path;
                // invariant name -> invariant layout).
                if (r.ProviderCulture) { parts.Add(code); }
                continue;
            }
            var castPart = cast switch
            {
                "i" => $"static_cast<int32_t>({code})",
                "l" => $"static_cast<int64_t>({code})",
                "d" => $"static_cast<double>({code})",
                "c" => $"static_cast<char16_t>({code})",
                "b" => $"static_cast<bool>({code})",
                "q" => $"static_cast<uint64_t>({code})",
                // M4.5: "p" -- a P/Invoke IntPtr carrier reaching a
                // pointer-typed icall param (PtrToStringAnsi's const char*):
                // the cast is EXPLICIT -- an int32_t -> pointer implicit
                // conversion is a C2440 error on MSVC and truncates the x64
                // address (the pinvokeext cmake_build FAIL root cause).
                "p" => NullSafePtrCast("const char*", code),
                // wfm1 (P/Invoke UTF-16): "p16" -- a P/Invoke IntPtr carrier
                // reaching a char16_t* icall param (PtrToStringUni -- the
                // runtime takes a const char16_t* UTF-16 buffer; the "p" cast
                // to const char* would MISREAD the UTF-16 payload as a byte
                // string). Explicit const char16_t* reinterpret.
                "p16" => NullSafePtrCast("const char16_t*", code),
                // CG6 (delegatecb): "n" -- a NATIVE-INT (IntPtr) argument
                // reaching an intptr_t P/Invoke parameter (the 0x18
                // ELEMENT_TYPE_I encoding). The declaration is intptr_t
                // (PinvokeParamList); the carrier is either an int32_t slot
                // (the M-stage IntPtr model) or an intptr_t temp ("ptr"-kind
                // result -- icall_delegate_get_fn_ptr returns the raw
                // function address). The explicit static_cast sign-extends
                // the int32 carrier to pointer width on x64 and is a no-op
                // for an intptr_t carrier. A null literal renders bare
                // (static_cast from std::nullptr_t is a C++ error -- the
                // NullSafePtrCast precedent).
                "n" => code == "nullptr" ? code : $"static_cast<intptr_t>({code})",
                // M5 wiring: "s" -- a STRING-typed runtime param (the format/
                // parse icalls take Il2CppString*, not the IR's Il2CppObject*).
                "s" => NullSafePtrCast("Il2CppString*", code),
                // M5 wiring: "a" -- an ARRAY-typed runtime param (the Base64
                // icall takes Il2CppArray*; Il2CppArray embeds Il2CppObject as
                // its first member, so the cast is the same exact-address
                // reinterpret as "s").
                "a" => NullSafePtrCast("Il2CppArray*", code),
                // DependentHandle out-object: "addr:o" -- a BYREF
                // reference-typed out argument (Object&). The ldloca address
                // of the caller's Il2CppObject* local reinterprets as
                // Il2CppObject** (the runtime writes the referent through it).
                "addr:o" => NullSafePtrCast("Il2CppObject**", code),
                // "g" -- a Guid value-slot byte pointer: the slot is
                // `uint8_t loc[16]` and its ldloca ADDRESS is a
                // pointer-to-array that does NOT decay to uint8_t*, so the
                // guid.h icalls (const uint8_t[16] params) need the explicit
                // byte-pointer cast. Carried by EVERY byte-slot param of the
                // System.Guid mappings (MethodMap.Guid.cs) -- the ONLY domain
                // whose ""-cast addresses the b59c6d9 blanket used to cast
                // (see the "" passthrough above for the regression note).
                "g" => NullSafePtrCast("uint8_t*", code),
                // P/Invoke string params (sprint worklist #28/#29 next-gap):
                // the P/Invoke branch of MethodRefResolver carries the RAW
                // signature encodings as cast codes -- they double as the
                // fold's string-arg detector (StackFold "r:System.String" ->
                // StringArgIndices). The fold pre-marshals every such arg
                // into its OWN cstr temp (il2cpp_pinvoke_string_to_utf8_alloc,
                // freed after the call -- audit H5), and the native
                // declaration takes const char* (PinvokeParamList) -- the
                // temp is already char*, so the arg passes through UNCAST.
                // "s" would be WRONG here (it casts to Il2CppString*).
                "r:System.String" => code,
                // wf2 (winforms1): CharSet.Unicode P/Invoke string arg -- the
                // pre-marshalled char16_t* temp passes through UNCAST (the
                // native declaration is const char16_t*).
                "u16:System.String" => code,
                "deref:i" => $"(*reinterpret_cast<int32_t*>({code}))",
                // binrw3 residual: "deref:u1"/"deref:u2" -- read a 1/2-byte
                // cell through a NATIVE-width pointer (ldelema of byte[]/
                // char[] points at a 1/2-byte element; deref:i over-reads
                // neighbor bytes -- b4=12345). The int32 icalls take the
                // promoted value; byte/char ranges make the digits exact.
                "deref:u1" => $"(*reinterpret_cast<uint8_t*>({code}))",
                "deref:u2" => $"(*reinterpret_cast<uint16_t*>({code}))",
                "deref:l" => $"(*reinterpret_cast<int64_t*>({code}))",
                // strictalias (2026-09-13): "deref:v" -- read the address's
                // slot at its DECLARED type (no type pun). The IntPtr/UIntPtr
                // value carrier is intptr_t on the widened paths (winforms1)
                // but still int32_t on live paths (pinvokedeep2 loc7), so a
                // fixed-width pointer cast is wrong for one of the two: an
                // int32_t* read of an intptr_t slot trips GCC
                // -Wstrict-aliasing (Cases_Winforms1.cpp:13787), and an
                // intptr_t* read of an int32_t slot would over-read. *addr
                // keeps the declared width and lets the icall parameter
                // conversion sign-extend; it also lets the CHECKED
                // il2cpp_intptr_to_int32 see the full pointer value again.
                // nativint regression fix (2026-09-13): the receiver may be an
                // ADDRESS EXPRESSION (ldloca/ldflda -- winforms1 loc303:
                // `(*((&loc303)))`, declared-width deref, no pun) or a VALUE
                // TEMP CARRYING AN ADDRESS as an intptr_t integer (nativint
                // arr[i]: `t4 = reinterpret_cast<intptr_t>(il2cpp_array_get_elem_ptr(...))`
                // -- `*(t4)` is ill-formed, `have 'intptr_t'`). Deref the
                // carrier through its OWN declared type (decltype(t4)* =
                // intptr_t*, the element's declared width) -- still no type
                // pun, because the pointer's pointee type IS the temp's
                // declared type.
                "deref:v" => args[srcIdx] is LocAddrExpr or FieldAddrExpr or StaticFieldAddrExpr
                    ? $"(*({code}))"
                    : $"(*reinterpret_cast<decltype({code})*>({code}))",
                // batch 156c (asynccase): "deref:r" -- read an Il2CppObject*
                // slot through its ADDRESS (the async state machine's
                // TaskAwaiter<T> / AsyncTaskMethodBuilder<T> receivers are
                // Reference slots; `ldloca V_3; call get_IsCompleted()` needs
                // the slot VALUE (the task object), not the address).
                "deref:r" => $"(*reinterpret_cast<Il2CppObject**>({code}))",
                // decimal-domain C44 (decgap): "deref:ui"/"deref:ul" -- read a
                // UNSIGNED slot (uint32 lives in an int32 slot, uint64 in an
                // int64 slot) as the unsigned type. The "deref:i/l" forms
                // yield int32_t/int64_t, and the implicit int->unsigned
                // conversion to a uint32_t/uint64_t icall parameter trips
                // MSVC C4245 (/W4 signed/unsigned mismatch).
                "deref:ui" => $"(*reinterpret_cast<uint32_t*>({code}))",
                "deref:ul" => $"(*reinterpret_cast<uint64_t*>({code}))",
                "deref:d" => $"(*reinterpret_cast<double*>({code}))",
                "deref:b" => $"static_cast<bool>(*reinterpret_cast<int32_t*>({code}))",
                // marshalrw (2026-09-13): "addr:v" -- pass the ADDRESS of a
                // VALUE argument. StructureToPtr<T>'s T value reaches the
                // runtime's value-path store as &value. Address expressions
                // (ldloca -- the value already lives at the address) pass
                // through; value expressions take their address.
                "addr:v" => args[srcIdx] is LocAddrExpr or FieldAddrExpr or StaticFieldAddrExpr
                    ? code
                    : $"&({code})",
                // M7 ref/out wiring: "addr:*" -- a BYREF out/ref ARGUMENT whose
                // slot already has the right C++ type (int32_t/int64_t/double
                // locals). The ldloca address passes through as a TYPED pointer
                // (TryParse-family icalls take int32_t*/int64_t*/double*). The
                // "" passthrough would hit the blanket uint8_t* cast above
                // (Guid-only assumption -- b59c6d9 audit).
                "addr:i" => $"reinterpret_cast<int32_t*>({code})",
                "addr:l" => $"reinterpret_cast<int64_t*>({code})",
                "addr:d" => $"reinterpret_cast<double*>({code})",
                // netcore21/31 Interlocked.CompareExchange out-bool: the bool
                // slot is an int32 carrier (the M-stage bool convention) --
                // the wrapper writes 0/1 through int32_t*.
                "addr:b" => $"reinterpret_cast<int32_t*>({code})",
                // wir9-b23 (arrfuncs unlock): "addr:a" -- a BYREF ARRAY slot
                // (Array.Resize(ref int32[], ...)): the ldloca LocAddrExpr
                // address points at the caller's Il2CppObject* array local
                // (Il2CppObject**), but icall_array_resize_i4 declares
                // Il2CppArray** -- an exact-address reinterpret (same shape as
                // the "addr:i/l/d" typed-address casts; the runtime writes the
                // new array pointer through the slot).
                "addr:a" => NullSafePtrCast("Il2CppArray**", code),
                // batch 149 (Interlocked ref-object Exchange/CompareExchange):
                // a REF OBJECT slot location -- the ldloca address of the
                // object local reinterprets as void** (the runtime pointer
                // atomics take void**/void*). ("p" is taken by the P/Invoke
                // const char* cast, hence "pobj".)
                "pobj" => $"reinterpret_cast<void**>({code})",
                // M5 wiring: DateTime/TimeSpan value-type receivers -- the
                // ldloca address points at the runtime struct; deref to the
                // struct VALUE (the icall takes it by value).
                "deref:dt" => $"(*reinterpret_cast<il2cpp::runtime::Il2CppDateTime*>({code}))",
                "deref:ts" => $"(*reinterpret_cast<il2cpp::runtime::Il2CppTimeSpan*>({code}))",
                // parity-T8: "ts_ticks" -- a TimeSpan VALUE reaching the
                // int64_t ticks param of il2cpp_monitor_wait_timespan.
                // Il2CppTimeSpan is { int64_t ticks } (il2cpp_time.h:49): VALUE
                // expressions read .ticks, ADDRESS expressions (->ticks).
                "ts_ticks" => args[srcIdx] is LocAddrExpr or FieldAddrExpr or StaticFieldAddrExpr
                    ? $"(({code})->ticks)"
                    : $"(({code}).ticks)",
                // P13 dtoffset: DateTimeOffset value-type receivers -- the
                // ldloca address dereferences to the struct VALUE (the icall
                // takes it by value, DateTime/TimeSpan precedent).
                "deref:dto" => $"(*reinterpret_cast<il2cpp::runtime::Il2CppDateTimeOffset*>({code}))",
                // P17 biginteger: BigInteger value-type receivers -- the
                // ldloca address dereferences to the 8-byte struct VALUE.
                "deref:bi" => $"(*reinterpret_cast<il2cpp::runtime::Il2CppBigInteger*>({code}))",
                // decimal-domain T3: 16-byte decimal receivers -- the ldloca
                // address dereferences to the struct VALUE (DateTime/TimeSpan
                // precedent; only reached where the icall takes the value --
                // the decimal icalls take ADDRESSES, so decimal receivers use
                // the uncast "" code instead).
                "deref:dec" => $"(*reinterpret_cast<il2cpp::runtime::Il2CppDecimal*>({code}))",
                // decimal-domain T3 (blocker E): a decimal VALUE argument
                // reaching a const Il2CppDecimal* icall param. A decimal slot
                // is a 16-byte Il2CppDecimal struct -- it does NOT decay to a
                // pointer (unlike Guid's uint8_t[16] array), so a VALUE
                // expression needs an explicit address-of. ADDRESS expressions
                // (ldloca/ldsflda -- LocAddrExpr/StaticFieldAddrExpr) already
                // render `(&x)` and must pass through UNCAST (address-of an
                // address would be a double indirection). The cast code is
                // SHAPE-AWARE on the arg.
                "dec" => args[srcIdx] is LocAddrExpr or StaticFieldAddrExpr ? code : $"(&({code}))",
                // enumdeep (M7R2): an enum-VALUE slot reaching the int64_t
                // value param of il2cpp_enum_to_string. The enum struct is
                // { Il2CppObject _obj; int32_t value__; } -- the value rides
                // in value__ (the IR widened it to int64; int32 -> int64 is
                // the implicit widening the icall param needs). ADDRESS
                // expressions (`(&loc2)` -- Cases_Color*) read value__ through
                // the pointer; VALUE expressions (a Cases_Color local read)
                // read it through `.`. EmitExpr never produces a plain int32
                // here (the receiver is always the enum struct slot).
                // enumparseic/enumdeep: when the slot is a plain int32 local
                // (`Color outVal`/`c` declared int32_t), there is no struct
                // -- dereference the address (VALUE form passes through).
                "enumval" => args[srcIdx] is LocAddrExpr laVal && IsInt32Slot(laVal.Name)
                    ? $"(*({code}))"
                    : args[srcIdx] is LocExpr leVal && IsInt32Slot(leVal.Name)
                    ? $"{code}"
                    : args[srcIdx] is LocAddrExpr or StaticFieldAddrExpr
                    ? $"({code})->value__"
                    : $"({code}).value__",
                // batch 148e (Enum.TryParse out): the out slot's ADDRESS --
                // the enum VALUE rides in value__ (offset 8 of the enum
                // struct), so "addr:l" (a plain reinterpret of the struct
                // base) would write the _obj carrier instead. Deref the
                // value__ field and take its address. When the out slot is
                // a plain int32 local (the transpiler declares `Color outVal`
                // as int32_t and reads it back as int32), there is no
                // struct -- take the slot address directly.
                "enumaddr" => args[srcIdx] is LocAddrExpr laEnum && IsInt32Slot(laEnum.Name)
                    ? $"reinterpret_cast<int64_t*>({code})"
                    : args[srcIdx] is LocAddrExpr or StaticFieldAddrExpr
                    ? $"reinterpret_cast<int64_t*>((&(({code})->value__)))"
                    : args[srcIdx] is LocExpr leEnum && IsInt32Slot(leEnum.Name)
                    ? $"reinterpret_cast<int64_t*>((&({code})))"
                    : $"reinterpret_cast<int64_t*>((&(({code}).value__)))",
                "unbox:i" => $"il2cpp::il2cpp_unbox_int32(static_cast<Il2CppObject*>({code}))",
                "unbox:l" => $"il2cpp::runtime::icall_unbox_long(static_cast<Il2CppObject*>({code}))",
                // cast-3 fix: box:i — when icall expects Il2CppObject* but gets int32, box it
                "box:i" => (TypeKindResolver.ResolveExprKind(args[srcIdx], _curLocKinds) == "ref" || code.Contains("Il2CppObject*", StringComparison.Ordinal))
                    ? code
                    : $"il2cpp::il2cpp_box_int32(nullptr, static_cast<int32_t>({code}))",
                // batch 148 (enum ToString box path): pass the BOXED object
                // through uncast -- the icall reads the klass identity from
                // the box itself (il2cpp_enum_to_string(_format)).
                "boxed" => code,
                // exc001 (Task.Run): "run:i" / "run:r" -- synchronously
                // invoke a ZERO-ARG delegate and use its return value as the
                // icall argument (Task.Run(f) ≡ FromResult(f.Invoke()) in
                // the synchronous-completion model). int32-family results
                // ride the i4 multicast ABI (4 trailing zeros pad the
                // (list,a0..a3) signature -- the DelegateInvokeExpr emission
                // precedent); managed-reference results ride the r ABI (LAST
                // leaf wins -- net48 multicast Invoke semantics). The
                // delegate object is already Il2CppObject* (no cast). Only
                // these two ABIs exist -- long/double/float carriers stay
                // LOUD in the Run arm, never raw-dispatched (a multicast
                // container's `first` field would be called as code).
                "run:i" => $"il2cpp::runtime::icall_multicast_invoke_i4({code}, 0, 0, 0, 0)",
                "run:r" => $"il2cpp::runtime::icall_multicast_invoke_r({code})",
                // decimal-domain C44 (decgap): boxed uint32/uint64 payloads --
                // the unsigned unboxers (icall_unbox_uint/ulong) keep the
                // unsigned type so a uint32_t/uint64_t icall param gets NO
                // implicit int->unsigned conversion (MSVC C4245).
                "unbox:ui" => $"il2cpp::runtime::icall_unbox_uint(static_cast<Il2CppObject*>({code}))",
                "unbox:ul" => $"il2cpp::runtime::icall_unbox_ulong(static_cast<Il2CppObject*>({code}))",
                "unbox:d" => $"il2cpp::runtime::icall_unbox_double(static_cast<Il2CppObject*>({code}))",
                "unbox:b" => $"il2cpp::runtime::icall_unbox_bool(static_cast<Il2CppObject*>({code}))",
                "unbox:c" => $"il2cpp::runtime::icall_unbox_char(static_cast<Il2CppObject*>({code}))",
                // decimal-domain T3: the boxed decimal unboxes to the 16-byte
                // value (by-value return -- the "dec"-kind temp).
                "unbox:dec" => $"il2cpp::runtime::icall_unbox_decimal(static_cast<Il2CppObject*>({code}))",
                // M6 XML wiring: "node" -- an XmlNode* receiver handle
                // (handle-gap mode: the managed local holds the raw tree node
                // pointer; get_OuterXml already declares this cast code in
                // MethodMap.Xml.cs but the switch lacked it -- xmlcase was
                // FAIL@transpile "icall arg cast code 'node' not implemented").
                "node" => NullSafePtrCast("const il2cpp::XmlNode*", code),
                // wfm2 (struct P/Invoke marshalling): "rect"/"point"/"msg" --
                // the registered small-struct cast codes (MethodRefResolver
                // PInvokeCastCode maps V:/B:V: encodings to them). BY-VALUE
                // args take the struct by copy: the managed value slot
                // (int32_t locals laid out as the struct's fields) is
                // reinterpreted to the native struct -- both are blittable
                // (to_native is a size-checked memcpy; by-value struct args
                // are ABI-correct through the register/stack calling
                // convention). BY-REF/out args (LocAddrExpr addresses) pass
                // the struct POINTER through (the managed slot address is the
                // native in/out buffer).
                "rect" => args[srcIdx] is LocAddrExpr or StaticFieldAddrExpr
                    ? $"reinterpret_cast<il2cpp::Il2CppPinvokeRect*>({code})"
                    : $"(*reinterpret_cast<const il2cpp::Il2CppPinvokeRect*>(&({code})))",
                "point" => args[srcIdx] is LocAddrExpr or StaticFieldAddrExpr
                    ? $"reinterpret_cast<il2cpp::Il2CppPinvokePoint*>({code})"
                    : $"(*reinterpret_cast<const il2cpp::Il2CppPinvokePoint*>(&({code})))",
                "msg" => args[srcIdx] is LocAddrExpr or StaticFieldAddrExpr
                    ? $"reinterpret_cast<il2cpp::Il2CppPinvokeMsg*>({code})"
                    : $"(*reinterpret_cast<const il2cpp::Il2CppPinvokeMsg*>(&({code})))",
                // wf2 (winforms1): an UNREGISTERED user struct (WNDCLASSEX/MSG
                // -- no il2cpp_pinvoke_struct entry) passes BY ADDRESS. The
                // managed struct is emitted with a native-compatible layout
                // (int32_t/intptr_t fields at natural alignment, wfm1), so
                // the by-ref/out ADDRESS expression (ldloca -> "(&wc)") is
                // handed to the native callee untouched -- the exact pointer
                // type the native declaration names (PinvokeParamList emits
                // the sanitized struct name + "*"). BY-VALUE (V:) args pass
                // the struct VALUE (the managed slot is a C++ struct value of
                // the same blittable layout -- identity on the register/stack
                // ABI). The prefix match covers ANY user struct name (the
                // registered rect/point/msg codes are matched above).
                var bv when bv.StartsWith("B:V:", StringComparison.Ordinal) => code,
                var vv when vv.StartsWith("V:", StringComparison.Ordinal) => code,
                "tostring:sb" => $"il2cpp::runtime::icall_stringbuilder_to_string(static_cast<Il2CppObject*>({code}))",
                // M6 enum box wiring (MethodMap.Console.cs box-type switch
                // enum arm): a BOXED enum value -- il2cpp_enum_to_string
                // (enumext.h:148) formats by member name when the enum
                // metadata is registered (il2cpp_enum_register) and the class
                // descriptor carries the name; the null descriptor takes the
                // documented Int32 no-metadata path (numeric decimal) -- the
                // same forward ABI as the Enum.ToString mapping
                // (MethodMap.Enum.cs). The value slot is the boxed enum's
                // int64 carrier (unbox:l -- icall_unbox_long); the explicit
                // Il2CppObject* wrap matches the write_line_s param type
                // (Il2CppString* is not derived from Il2CppObject).
                // FIX TIME-RC (timecase/timeops cmake FAIL): the wrap was a
                // static_cast, which is ILL-FORMED between unrelated pointer
                // types (Il2CppString* -> Il2CppObject*); the declared intent
                // ("matches the param type") only needs the type, so the
                // bridge must be reinterpret_cast (same rule as the XmlNode
                // family fix).
                "enum:l" => $"reinterpret_cast<il2cpp::Il2CppObject*>(il2cpp::runtime::il2cpp_enum_to_string(nullptr, il2cpp::runtime::icall_unbox_long(static_cast<Il2CppObject*>({code}))))",
                // timezone domain (MethodMap.TimeZone.cs): "tzsec" -- the
                // TimeZoneInfo.GetUtcOffset Local branch. The receiver
                // expression is the folded null (no identity); the cast
                // REPLACES the argument with the runtime's current-instant
                // offset in SECONDS scaled to TICKS (1 s = 10^7 ticks --
                // il2cpp_time.h) for icall_timespan_from_ticks.
                "tzsec" => $"il2cpp::runtime::il2cpp_timezone_get_utc_offset() * 10000000LL",
                // B:r: / B:V: by-ref interface/value: pass address through
                var br when br.StartsWith("B:r:", StringComparison.Ordinal) || br.StartsWith("B:V:", StringComparison.Ordinal) => $"&({code})",
                // P-D2 #4: a DELEGATE-typed P/Invoke argument -- wrap the
                // delegate object in the per-signature GfpS dispatcher (the
                // declaration is intptr_t, so the dispatcher's intptr_t result
                // feeds the native fn-pointer parameter; the explicit
                // Il2CppObject* cast covers a non-object carrier).
                var rcDel when rcDel.StartsWith("r:", StringComparison.Ordinal)
                    && MethodRefResolver.DelegateGfpForPinvokeParam(md, rcDel[2..]) is { } gfpNameDel
                    => gfpNameDel + "(static_cast<Il2CppObject*>(" + code + "))",
                // r: reference type cast: pass through (already Il2CppObject*)
                var rc when rc.StartsWith("r:", StringComparison.Ordinal) => code,
                // P:i / P:v pinning: pass through (pin handled by runtime)
                var pp when pp.StartsWith("P:", StringComparison.Ordinal) => code,
                // unknown cast code: pass through (log for audit)
                _ => code,
            };
            parts.Add(r.IsPInvoke ? PinvokeArgBridge(castPart) : castPart);
        }
        // M7R3 (hashtable2/hashtablecase): the injected 0 goes AFTER the
        // arguments -- the receiver must occupy the icall's FIRST parameter
        // (icall_hashtable_ctor(Il2CppObject* self, int32_t capacity)).
        if (r.AppendZeroArg) parts.Add("0");
        // decimal-domain C44 (decgap): literal trailing arguments after the
        // IL argument list (decimal.Round 1/2-arg overloads -> the runtime
        // icall_decimal_round(result, d, decimals, mode) needs the missing
        // default decimals/mode = 0 injected as literals).
        if (r.AppendLiterals is { Length: > 0 } literals)
            foreach (var lit in literals) parts.Add(lit);
        return string.Join(", ", parts);
    }

    // Type token (0x02 TypeDef / 0x01 TypeRef) -> full name. TypeSpec
    // decodes by shape: VAR (0x13) -> System.Int32 (M3 single-instantiation
    // convention -- agrees with StackFold.TypeSpecName's VAR arm, so
    // fold/emitter judgments pair on generic parameters). Every other shape
    // keeps the legacy Object no-op (r103 pairing: the fold side still
    // yields the legacy TaskAwaiter name there).
    private static string TypeTokenFullName(MetadataReader md, int token)
    {
        var handle = MetadataTokens.Handle(token);
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)handle),
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => TypeSpecTokenName(md, (TypeSpecificationHandle)handle),
            // MethodDefinition / FieldDefinition etc used as newarr element -- tolerant fallback
            _ => "System.Object",
        };
    }

    private static string TypeSpecTokenName(MetadataReader md, TypeSpecificationHandle handle)
    {
        var sig = md.GetBlobReader(md.GetTypeSpecification(handle).Signature);
        var header = sig.ReadByte();
        if (header == 0x13)
        {
            _ = sig.ReadCompressedInteger();
            return "System.Int32";
        }
        // GenericInst (0x15): no generic-struct box/descriptor surface exists
        // (generic struct boxing work order) -- legacy Object no-op (a box
        // becomes a passthrough, r103 behavior). Imprecise but compilable;
        // the fold side still resolves the precise instance name.
        return "System.Object";
    }

    // R2b P2: whether a type token is a VAR (0x13) / MVAR (0x1E) TypeSpec --
    // a generic parameter occurrence (default(T) in a generic body). The
    // callers route these through the VAR closure channel instead of the
    // canonical "System.Int32" name (which sanitizes to the undefined C++
    // type `System_Int32`).
    private static bool IsVarOrMvarTypeSpec(MetadataReader md, int token)
    {
        var handle = MetadataTokens.Handle(token);
        if (handle.Kind != HandleKind.TypeSpecification) return false;
        var sig = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
        return sig.ReadByte() is 0x13 or 0x1E;
    }

    //--------------------------------------------------------------------------
    // ArrayElementClassCpp -- the element class expression for a reference
    // newarr (D-TYPG-1). The runtime stamps a reference array with an
    // element-aware descriptor name "[<element>" (string[] -> "[System.String"
    // -> GetType().Name "String[]"; net48 parity). Reuses the box-descriptor
    // resolution so the emitted expression is the exact class the runtime
    // registers (System.String -> &System_String_class, user class ->
    // &Cases_Animal2_class, box-only primitives -> class_by_name).
    //--------------------------------------------------------------------------
    private static string ArrayElementClassCpp(string typeName) =>
        BoxClassDescriptorCpp(typeName);

    //--------------------------------------------------------------------------
    // ArrayElementType -- newarr element type → (elemSize, isRefElement).
    //   System.Int32                 -> (4, false)
    //   System.Byte                  -> (1, false)   (net-domain T0; ldelem.u1/
    //                                                 stelem.i1 width dispatch
    //                                                 already present)
    //   reference types (string/object/user class) -> (8, true)
    //   everything else (double, user struct, ...) -> LOUD rejection
    // (audit C4: a wrong element size makes stelem_ref/ldelem_ref read/write
    // past the allocation and the GC scan the payload with the wrong stride.)
    //--------------------------------------------------------------------------
    private static (int ElemSize, bool IsRefElement) ArrayElementType(MetadataReader md, int typeToken, IReadOnlyList<TypeDef> types)
    {
        var typeName = TypeTokenFullName(md, typeToken);
        if (typeName == "System.Int32") return (4, false);
        if (typeName == "System.Byte") return (1, false);
        // M6 (strcomp2/Split C4): a char[] holds 2-byte UTF-16 code units.
        // The runtime Split/params-char[] path (and any user char[] newarr)
        // allocates 2-byte elements; stelem.i2/ldelem.i2 already dispatch the
        // 2-byte width.
        if (typeName == "System.Char") return (2, false);
        // CG6 (arrayops): Int64/UInt64 element arrays (long[]/ulong[] --
        // Array.IndexOf/Sort on long arrays, IndexOf/GetLowerBound probes).
        // 8-byte elements; the ldelem/stelem i8 width dispatch already exists
        // (ILOP-2 P9), so only the ALLOCATION stride was missing.
        if (typeName == "System.Int64" || typeName == "System.UInt64") return (8, false);
        // Batch 155b: System.IntPtr/UIntPtr elements -- native int (8 bytes
        // on the 64-bit target) -- allocate like long[] (non-ref, native-int
        // stride); Ldelem_i/Stelem_i read/write the 8-byte payload.
        if (typeName == "System.IntPtr" || typeName == "System.UIntPtr") return (IntPtr.Size, false);
        if (typeName == "System.String" || typeName == "System.Object") return (IntPtr.Size, true);
        // D-GC-2 (gcstress3): the newarr gate must GENERALIZE reference
        // elements instead of enumerating BCL/user names. A SZARRAY element is
        // a GC reference iff its type is NOT a value type; value-ness is
        // decided per token kind so every reference element passes:
        //   * TypeDef (top-level OR nested user class) -> the metadata base
        //     type (System.ValueType / System.Enum = value -> reject; anything
        //     else is a class). This also closes the nested-type hole: the old
        //     `types` lookup keyed by FullName missed nested TypeDefs
        //     (MetadataLoader skips them; GetFullName returns the SHORT name,
        //     and ExpandNestedTypes only runs in some pipelines).
        //   * TypeRef (BCL/referenced types) -> the wired value types are
        //     rejected loudly; every other referenced type is a reference
        //     element (System.WeakReference[] / StringBuilder[] / ...).
        //   * TypeSpec (generic instantiations) stays rejected by
        //     TypeTokenFullName -- no silent mis-allocation.
        var handle = MetadataTokens.Handle(typeToken);
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var baseTypeName = TypeBaseFullName(md, (TypeDefinitionHandle)handle);
            if (baseTypeName is "System.ValueType" or "System.Enum")
            {
                // Tolerant fallback: unknown value type newarr -- assume 8-byte element
                // (may cause incorrect GC scan stride, but allows generation to continue)
                Console.Error.WriteLine($"warning: newarr of value type {typeName} -- using 8-byte element fallback");
                return (IntPtr.Size, false);
            }
            return (IntPtr.Size, true);
        }
        if (IsKnownBclValueType(typeName))
            throw new NotSupportedException($"newarr of value type {typeName} not supported (audit C4)");
        return (IntPtr.Size, true);
    }

    // Metadata base type full name of a TypeDef (System.Object root). Used by
    // ArrayElementType to classify a TypeDef token as value (System.ValueType /
    // System.Enum) or reference without relying on the loaded `types` model
    // (which skips nested TypeDefs).
    private static string TypeBaseFullName(MetadataReader md, TypeDefinitionHandle h)
    {
        var baseType = md.GetTypeDefinition(h).BaseType;
        if (baseType.IsNil) return "System.Object";
        return baseType.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)baseType),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)baseType),
            HandleKind.TypeSpecification => "System.Object",
            _ => "System.Object",
        };
    }

    // BCL value types wired by the transpiler (primitives + structs the emitter
    // models: TypeCppName / box / unbox / guid.h / decimal.h). A newarr whose
    // element is one of these must NOT fall through to the reference-element
    // path (the GC would scan the payload with an 8-byte ref stride over value
    // slots -- silent mis-allocation). Kept as a closed set: every wired value
    // type is enumerated, so any other referenced type is a reference element.
    private static bool IsKnownBclValueType(string typeName) =>
        typeName is "System.Boolean" or "System.SByte" or "System.Int16" or "System.UInt16"
            or "System.UInt32" or "System.Single" or "System.Double" or "System.IntPtr"
            or "System.UIntPtr" or "System.DateTime" or "System.TimeSpan" or "System.Decimal"
            or "System.Guid";

    private static string OpSymbol(BinaryOp op) => op switch
    {
        BinaryOp.Add => "+", BinaryOp.Sub => "-", BinaryOp.Mul => "*", BinaryOp.Div => "/", BinaryOp.DivUn => "/", BinaryOp.Rem => "%",
        BinaryOp.And => "&", BinaryOp.Or => "|", BinaryOp.Xor => "^", BinaryOp.Shl => "<<", BinaryOp.Shr => ">>", BinaryOp.ShrUn => ">>",
        BinaryOp.Ceq => "==", BinaryOp.Cgt => ">", BinaryOp.Clt => "<", BinaryOp.CgtUn => ">", BinaryOp.CltUn => "<",
        _ => throw new NotSupportedException($"binary operation {op} is not supported by M1 emission"),
    };

    //--------------------------------------------------------------------------
    // ExceptionChain -- the il2cpp_throw_new type-name chain for one throw.
    //   Chain format (exceptionext convention): "{Type},{Type},{Base1},...".
    //   il2cpp_exception_matches walks the segments after the first comma, so
    //   the repeated type makes a catch of the thrown type itself match and
    //   the base segments make base catches match.
    //   User types: base chain walked via the translated type graph. BCL
    //   exceptions: known .NET bases (map); unknown types fall back to
    //   System.Exception as the universal root.
    //--------------------------------------------------------------------------
    private static string ExceptionChain(string typeName, IReadOnlyList<TypeDef> types)
    {
        if (typeName == "System.Exception") return "System.Exception,System.Exception";
        var bases = new List<string>();
        // Nested types (e.g. "Cases.Exops2+CxEx2") carry Namespace="" so
        // TypeDef.FullName is the SHORT name ("CxEx2") -- the old equality
        // lookup missed every nested throw type and the chain collapsed to
        // System.Exception. MatchesFullName accepts both forms. Chain SEGMENTS
        // must use the metadata '+' form (the generated catch matcher passes
        // TypeTokenFullName, e.g. "Cases.Exops2+CxEx"), so a nested base is
        // rendered declaring+name.
        static string MetaName(TypeDef t) =>
            t.IsNested && t.DeclaringTypeName.Length > 0
                ? t.DeclaringTypeName + "+" + t.Name
                : t.FullName;
        var cur = types.FirstOrDefault(t => t.MatchesFullName(typeName));
        // Base-chain walk: the loader's BaseType link is keyed by FullName, so
        // a nested base ("Cases.Exops2+CxEx" vs FullName "CxEx") leaves the
        // link null -- fall back to a MatchesFullName name lookup and keep
        // walking until the terminal BCL base (e.g. System.Exception), whose
        // name is the final segment.
        var guard = 0;
        while (cur != null && cur.BaseTypeName != "System.Object" && guard++ < 64)
        {
            var next = cur.BaseType ?? types.FirstOrDefault(x => x.MatchesFullName(cur.BaseTypeName));
            if (next == null)
            {
                // Terminal base is a BCL type not in the translated set -- its
                // metadata name is the last chain segment.
                bases.Add(cur.BaseTypeName);
                break;
            }
            bases.Add(MetaName(next));
            cur = next;
        }
        if (bases.Count == 0 && BclExceptionBases.TryGetValue(typeName, out var bcl))
            bases.AddRange(bcl.Split(','));
        if (bases.Count == 0) bases.Add("System.Exception");
        return typeName + "," + typeName + "," + string.Join(",", bases);
    }

    //--------------------------------------------------------------------------
    // EmitThrowCall / EmitMessageArg -- the throw-site carrier emission
    // (excext, m7m). StackFold forwards the BCL-exception ctor arguments
    // (message / paramName) captured at the newobj; this emitter turns them
    // into the il2cpp_throw_new / factory call so the managed exception
    // carrier carries the REAL message. Shapes:
    //   ArgumentNullException(string paramName)          -> the runtime factory
    //                                                      composes the
    //                                                      standard text
    //   ArgumentOutOfRangeException(p, m)                -> factory (message
    //                                                      wins, net48)
    //   everything else .ctor(string message)            -> il2cpp_throw_new
    //                                                      with the message
    // A StringConstExpr arg becomes a C literal; a runtime string local is
    // converted with il2cpp_pinvoke_string_to_utf8_alloc (the throw path, so
    // the buffer outlives the unwind); any other shape / no args falls back to
    // the previous "exception thrown" default.
    //--------------------------------------------------------------------------
    private static string EmitThrowCall(string thrownType, string chain, List<Expr> args,
        MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        if (thrownType == "System.ArgumentNullException" && args.Count >= 1)
            return $"il2cpp::il2cpp_throw_argument_null_exception({EmitMessageArg(args[0], md, types)});";
        if (thrownType == "System.ArgumentOutOfRangeException" && args.Count >= 2)
            return $"il2cpp::il2cpp_throw_argument_out_of_range_exception({EmitMessageArg(args[0], md, types)}, {EmitMessageArg(args[1], md, types)});";
        if (args.Count >= 1)
            return $"il2cpp::il2cpp_throw_new(\"{chain}\", {EmitMessageArg(args[^1], md, types)});";
        return $"il2cpp::il2cpp_throw_new(\"{chain}\", \"exception thrown\");";
    }

    private static string EmitMessageArg(Expr arg, MetadataReader md, IReadOnlyList<TypeDef> types)
    {
        return arg is StringConstExpr s
            ? $"\"{EscapeC(s.Value)}\""
            : $"il2cpp::il2cpp_pinvoke_string_to_utf8_alloc({NullSafePtrCast("Il2CppString*", EmitExpr(arg, md, types))})";
    }

    // delechain: whether a type is a custom delegate -- its TypeDef base-class chain reaches
    // System.MulticastDelegate / System.Delegate (delegate types are carried by the runtime: no method bodies, no vtable, no descriptor).
    private static bool IsDelegateTypeDef(TypeDef t)
    {
        for (var cur = t; cur != null; cur = cur.BaseType)
            if (cur.BaseTypeName == "System.MulticastDelegate" || cur.BaseTypeName == "System.Delegate")
                return true;
        return false;
    }

    // CG6: whether a FULL NAME names a custom delegate TypeDef in the
    // translated set (the identity-cast detection for castclass/isinst to a
    // delegate type -- IntFn/Cb have no runtime descriptor).
    private static bool IsCustomDelegateType(string fullName, IReadOnlyList<TypeDef> types)
    {
        // Nested-name match (delegatechain): nested TypeDefs carry the short
        // FullName, metadata queries use the '+' form -- MatchesFullName covers
        // both, same fix as StackFold.IsDelegateType.
        var t = types.FirstOrDefault(x => x.MatchesFullName(fullName));
        if (t != null && IsDelegateTypeDef(t)) return true;
        // batch 162 (eventcase): BCL delegates (System.EventHandler etc.) are
        // runtime Il2CppDelegate objects with no generated struct -- the
        // reference-field store cast must stay Il2CppObject* (the generated
        // C++ name would be an unknown type).
        return fullName == "System.EventHandler" || fullName == "System.EventHandler`1";
    }

    // xmlenum: whether a FULL NAME names one of the System.Xml handle-gap
    // types carried by xml_doc.h (raw pointers in Il2CppObject* slots, no
    // runtime class descriptor -- the identity-cast set for isinst/castclass,
    // the same contract as the custom-delegate arm above).
    private static bool IsXmlHandleGapType(string fullName) => fullName is
        "System.Xml.XmlNode" or "System.Xml.XmlElement" or "System.Xml.XmlDocument" or "System.Xml.XmlNodeList";

    // COM stage 17 batch 3 (com17type): whether a FULL NAME names an
    // INTERFACE TypeDef in the translated set. Interfaces have NO generated
    // C++ struct (only the extern Il2CppClass descriptor -- instanceSize 0,
    // never allocated), so a reference-field store cast to the interface's
    // C++ name would be an unknown type -- the value stays Il2CppObject*.
    private static bool IsInterfaceType(string fullName, IReadOnlyList<TypeDef> types)
    {
        return types.Any(x => x.FullName == fullName && x.IsInterface);
    }

    // Known .NET base chains for BCL exceptions (compile-time knowledge; the
    // runtime does not walk metadata -- the chain is baked into the throw).
    private static readonly Dictionary<string, string> BclExceptionBases = new()
    {
        ["System.ArgumentException"] = "System.SystemException,System.Exception",
        ["System.ArgumentNullException"] = "System.ArgumentException,System.SystemException,System.Exception",
        ["System.ArgumentOutOfRangeException"] = "System.ArgumentException,System.SystemException,System.Exception",
        ["System.InvalidOperationException"] = "System.SystemException,System.Exception",
        ["System.NotSupportedException"] = "System.SystemException,System.Exception",
        ["System.InvalidCastException"] = "System.SystemException,System.Exception",
        ["System.IndexOutOfRangeException"] = "System.SystemException,System.Exception",
        ["System.NullReferenceException"] = "System.SystemException,System.Exception",
        ["System.DivideByZeroException"] = "System.ArithmeticException,System.SystemException,System.Exception",
        ["System.OverflowException"] = "System.ArithmeticException,System.SystemException,System.Exception",
        ["System.FormatException"] = "System.SystemException,System.Exception",
        ["System.KeyNotFoundException"] = "System.SystemException,System.Exception",
        // excext (m7m): the FULL managed name -- the excext case throws
        // `new KeyNotFoundException(...)` under `using System.Collections.Generic`,
        // which resolves to this namespaced full name. Without the entry the
        // chain omits System.SystemException, so catch(SystemException) never
        // matched and the throw escaped unhandled.
        ["System.Collections.Generic.KeyNotFoundException"] = "System.SystemException,System.Exception",
    };

    // C string literal escaping (backslash / quotes / newline, etc.). T3 convergence: the single
    // implementation lives in StringPoolPass.EscapeCppLiteral (used for pool-table row emission);
    // this delegate stays byte-for-byte identical (both implementations verified: same 5 replacements, same order, recipe §3).
    private static string EscapeC(string s) => StringPoolPass.EscapeCppLiteral(s);

    private static string ReturnTypeCpp(SignatureTypeCode code) => code switch
    {
        SignatureTypeCode.Void => "void",
        SignatureTypeCode.Int32 => "int32_t",
        // M5 wiring: bool/char live in int32_t slots; int64/double get their
        // own C++ widths (user methods returning them transpile as such).
        SignatureTypeCode.Boolean => "int32_t",
        SignatureTypeCode.Char => "int32_t",
        SignatureTypeCode.Int64 => "int64_t",
        SignatureTypeCode.Double => "double",
        // wf2 (winforms1): IntPtr/UIntPtr method returns ride the full-width
        // pointer carrier (the same intptr_t/uintptr_t C++ type the P/Invoke
        // return path emits -- M4.5). A WNDPROC-shaped managed method returns
        // the DefWindowProcW/IntPtr.Zero result, which is an intptr_t temp:
        // declaring the function return int32_t would truncate the x64
        // pointer. The M-stage user-method slot model stays int32_t for
        // IntPtr VALUES (locals/params); only the RETURN declaration widens.
        SignatureTypeCode.IntPtr => "intptr_t",
        SignatureTypeCode.UIntPtr => "uintptr_t",
        // M3 generics: generic parameter return (T) -- the default
        // instantiation maps T to int32_t.
        SignatureTypeCode.GenericTypeParameter => "int32_t",
        // genlist A: generic METHOD parameter return (!!T) -- the same
        // default-instantiation rule as the class-level VAR above (the
        // MethodDef declaration transpiles; the MethodSpec call site rejects
        // loudly in the resolver).
        SignatureTypeCode.GenericMethodParameter => "int32_t",
        // Reference return: SignatureTypeCode has no Class member (0x12 is remapped), so judge by the
        // raw byte value (same rules as MethodRefResolver.IsRefType: 0x0E STRING / 0x12 CLASS / 0x1C OBJECT).
        var b when (byte)b is 0x0E or 0x12 or 0x1C or 0x40 => "Il2CppObject*",
        // delecb2 closure (M6): a GENERIC-INSTANCE return (0x15) -- in M6 the
        // only reachable generic instantiations are the delegate types
        // System.Func`N / System.Action`N, which are always reference objects
        // (MakeAdder returns Func<int,int>). ReturnTypeCpp had no 0x15 arm ->
        // LOUD "return type GenericTypeInstance is not supported by M3". The
        // temp is a reference slot (IsRefType in MethodRefResolver.cs carries
        // the matching 0x15 rule, wired in parallel).
        SignatureTypeCode.GenericTypeInstance => "Il2CppObject*",
        // ValueType (0x11): struct return -- tolerant fallback to Il2CppObject* (boxed)
        var b when (byte)b is 0x11 => "Il2CppObject*",
        // Ptr (0x0F) / ByReference (0x10): pointer/byref returns -- pass as void*
        var b when (byte)b is 0x0F or 0x10 => "void*",
        // SZArray (0x1D): single-dimensional array return (e.g., int[]) -- Il2CppArray*
        var b when (byte)b is 0x1D => "Il2CppArray*",
        // Integer primitives (ECMA-335 type codes): I1/U1/I2/U2/I4/U4/I8/U8/R4/R8
        var b when (byte)b is 0x04 => "int8_t",    // SByte
        var b when (byte)b is 0x05 => "uint8_t",   // Byte
        var b when (byte)b is 0x06 => "int16_t",   // Int16
        var b when (byte)b is 0x07 => "uint16_t",  // UInt16
        var b when (byte)b is 0x08 => "int32_t",   // Int32
        var b when (byte)b is 0x09 => "uint32_t",  // UInt32
        var b when (byte)b is 0x0A => "int64_t",   // Int64
        var b when (byte)b is 0x0B => "uint64_t",  // UInt64
        var b when (byte)b is 0x0C => "float",     // Single
        var b when (byte)b is 0x0D => "double",    // Double
        var b when (byte)b is 0x02 => "bool",      // Boolean
        var b when (byte)b is 0x03 => "char16_t",  // Char
        // TypedReference (0x16), ByRef (0x10), Ptr (0x0F) tolerant fallbacks
        var b when (byte)b is 0x16 => "Il2CppObject*",
        var b when (byte)b is 0x10 => "void*",
        var b when (byte)b is 0x0F => "void*",
        _ => throw new NotSupportedException($"return type {code} is not supported by M3"),
    };

    // C4716 trailing-return value (see the injection in EmitBody): the zero
    // value of the C++ return type -- `return 0;` for the numeric/integer
    // widths (int32_t/int64_t/double/intptr_t...), `return nullptr;` for the
    // reference returns (Il2CppObject* -- the same byte-set rule ReturnTypeCpp
    // uses, including GenericTypeInstance).
    private static string TrailingReturn(SignatureTypeCode ret, string? structCpp = null)
    {
        // F2 (decimal-domain): a VALUETYPE struct return zero-initializes the
        // struct (`return Il2CppDecimal{};`) -- `return 0;` would not compile.
        if (structCpp != null) return $"return {structCpp}{{}};";
        var b = (byte)ret;
        return (b is 0x0E or 0x12 or 0x1C or 0x40 || ret == SignatureTypeCode.GenericTypeInstance)
            ? "return nullptr;"
            : "return 0;";
    }

    // F2 (decimal-domain, net9/10 E2E campaign): decode a method's RETURN
    // signature blob; when it is a VALUETYPE (0x11) return the full type
    // name (System.Decimal / user structs) for TypeCppName mapping. Null
    // for every other return shape. Same blob walk as
    // PinvokeReturnStructName (:5142) but name-preserving.
    private static string? ManagedReturnStructName(MetadataReader md, MethodDef m)
    {
        var handle = FindMethodHandle(md, m);
        if (handle == null) return null;
        var mi = md.GetMethodDefinition(handle.Value);
        var sig = md.GetBlobReader(mi.Signature);
        var hdr = sig.ReadSignatureHeader();
        if (hdr.IsGeneric) sig.ReadCompressedInteger();
        sig.ReadCompressedInteger(); // param count
        var retByte = sig.ReadByte();
        if (retByte != 0x11) return null; // not a VALUETYPE return
        var owner = sig.ReadTypeHandle();
        return owner.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)owner),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)owner),
            _ => null,
        };
    }

    // refl2 S3 (D4): does this type get a TypeInfo table/extern/registration?
    // reflectedNames == null = mode A / default (every type); otherwise the
    // type must be in the mode B manifest scope. The residual noTypeInfoOwners
    // (still-invalid C++ identifier) always excludes. Shared by the three
    // typeinfo emission sites (.cpp table, generated.h extern, entry.cpp
    // registration) so they can never diverge (D1 single decision point).
    private static bool ShouldEmitTypeInfo(TypeDef t, IReadOnlyCollection<string>? reflectedNames,
        IReadOnlySet<string> noTypeInfoOwners)
        => !noTypeInfoOwners.Contains(t.FullName)
           && (reflectedNames == null || reflectedNames.Contains(t.FullName));

    // refl2 S3 (D4): the mode B reflection scope -- the generator's consumption
    // of CliOptions.ReflectionList. null = emit every type (granularity all /
    // no manifest: mode A / default). Otherwise = FilterReflectionManifest's
    // Kept result (the manifest types that truly exist in the assembly set)
    // PLUS the base/interface CLOSURE: a kept type's TypeInfo table references
    // &{Base}_typeinfo and &{Iface}_typeinfo, so those types must ALSO emit
    // their tables or the generated C++ fails to link. The closure is a
    // fixpoint over BaseType + InterfaceTypes (all TypeDefs in the loaded set).
    private static IReadOnlyCollection<string>? ReflectionScopeFromOptions(CliOptions? options, List<TypeDef> types)
    {
        if (options == null || !options.ReflectionListProvided
            || options.ReflectionGranularity == ReflectionGranularity.All)
            return null;
        var scope = new HashSet<string>(
            TypeEmitter.FilterReflectionManifest(options.ReflectionList, types).Kept,
            StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var t in types)
            {
                if (!scope.Contains(t.FullName)) continue;
                if (t.BaseType != null && scope.Add(t.BaseType.FullName)) changed = true;
                foreach (var iface in t.InterfaceTypes)
                    if (scope.Add(iface.FullName)) changed = true;
            }
        }
        return scope;
    }

    // M5.5 reflection (R-REFL-01): is the type's C++ name a valid identifier?
    // TypeEmitter.Sanitize now handles '=' (refl2 S3/G3: the compiler-generated
    // array-initializer struct <PrivateImplementationDetails>
    // .__StaticArrayInitTypeSize=3's short name) plus the metadata separators.
    // Any OTHER unforseen character would leave an uncompilable table/extern
    // name -- this residual guard keeps such types excluded (conservative
    // safety net, never the known '=' case).
    private static bool IsValidCppIdentifier(string name)
    {
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_')) return false;
        return name.Skip(1).All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    // M2 instance method set: both .ctor and ordinary instance methods are emitted (virtual methods are M3). Static methods stay M1.
    private static bool IsUserInstanceMethod(MethodDef m) => m.Name != ".ctor" && !m.IsStatic;

    // Parameter C++ type array: for instance methods arg0 = this (Il2CppObject*), then map per the
    // signature encoding ("i" -> int32_t; "r:..." -> Il2CppObject*). For static methods arg0 = the first explicit parameter.
    // Bracket-aware top-level split: generic instantiations embed commas
    // (Dictionary`2<r:String,V:X>) that a naive Split(',') would tear into
    // phantom params ("V:X>>" -> unknown type 'X_').
    internal static IEnumerable<string> SplitTopLevel(string s)
    {
        int depth = 0, start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '<') depth++;
            else if (s[i] == '>') depth--;
            else if (s[i] == ',' && depth == 0)
            {
                yield return s.Substring(start, i - start);
                start = i + 1;
            }
        }
        yield return s.Substring(start);
    }
    // caudit2-fix: which argN slots are ADDRESSES in the generated C++ ABI:
    //   * arg0 of a value-type instance method is `T*` (this by address);
    //   * BYREF value/reference params (B:V: / B:r:) and raw pointers (P:)
    //     are pointers/addresses;
    //   * BY-VALUE struct params (V:) are plain C++ values.
    // The map is indexed by arg slot exactly like the emitted names arg0..argN.
    private static bool[] ArgAddressMap(MethodDef m, MethodBodyInfo info)
    {
        var map = new bool[info.ArgCount];
        var slot = 0;
        if (!m.IsStatic && slot < map.Length)
        {
            map[slot++] = m.Owner.IsValueType; // this: T* for value types only
        }
        if (m.ParamTypes.Length > 0)
        {
            var parts = SplitTopLevel(m.ParamTypes).ToArray();
            for (var pi = 0; pi < parts.Length && slot < map.Length; pi++)
            {
                var pk = parts[pi].Trim();
                // P-G3: a BYREF-wrapped generic parameter (`ref T` / `out T`)
                // of a shared method is the byref object slot (Il2CppObject**)
                // -- an ADDRESS in the C++ ABI (same as B:V:/B:r:/P:).
                if (m.ParamIsByrefVar is { } pibv && pi < pibv.Length && pibv[pi] && _curMethodShare)
                {
                    map[slot++] = true;
                    continue;
                }
                map[slot++] = pk.StartsWith("B:V:", StringComparison.Ordinal)
                    || pk.StartsWith("B:r:", StringComparison.Ordinal)
                    || pk.StartsWith("P:", StringComparison.Ordinal);
            }
        }
        return map;
    }

    // caudit2-fix: true when the arg slot is address-typed in the C++ ABI.
    private static bool IsAddressArg(string name)
    {
        if (!name.StartsWith("arg", StringComparison.Ordinal)) return false;
        if (!int.TryParse(name.AsSpan(3), out var idx)) return false;
        return idx >= 0 && idx < _curArgAddress.Length && _curArgAddress[idx];
    }

    private static string[] ParamTypesCpp(MethodDef m, MethodBodyInfo info)
    {
        var types = new List<string>();
        if (!m.IsStatic)
        {
            // M3 Task 11: value-type methods receive this BY ADDRESS ({T}*):
            // the instance is a plain value; field access dereferences the
            // pointer. Reference types keep Il2CppObject*.
            types.Add(m.Owner.IsValueType ? $"{TypeEmitter.TypeCppName(m.Owner)}*" : "Il2CppObject*");
        }
        if (m.ParamTypes.Length > 0)
        {
            var parts = SplitTopLevel(m.ParamTypes).ToArray();
            for (var i = 0; i < parts.Length; i++)
            {
                // P-G3 (method-level shared ABI): a BYREF-wrapped generic
                // parameter (`ref T` / `out T` -- "B:i") of a SHARED
                // implementation is the byref OBJECT slot: Il2CppObject** (a
                // slot holding the BOXED T object). The shared body's ldobj/
                // stobj on it read/write an Il2CppObject* through the pointer;
                // the call side passes the address of a boxed bridge temp
                // (value T) or the reference slot itself (reference T).
                if (m.ParamIsByrefVar is { } pibv && i < pibv.Length && pibv[i]
                    && _curMethodShare)
                {
                    types.Add("Il2CppObject**");
                    continue;
                }
                // R2b P2: a VAR parameter in a reference instantiation is the
                // managed reference (Il2CppObject*); definitions and int
                // instantiations keep the raw int32 slot.
                if (m.ParamIsVar is { } piv && i < piv.Length && piv[i]
                    && m.Owner.InstantiationSuffix != null)
                {
                    types.Add("Il2CppObject*");
                    continue;
                }
                types.Add(parts[i] switch
                {
                    // M5 wiring: 64-bit/double parameters get their real C++
                    // width -- an int32_t declaration truncates a double
                    // parameter (mathdeep IsNaN/IsFinite took int32_t arg0 and
                    // `v != v` folded to 0). Encodings from MethodSignatureCoder:
                    // "d"/"f" double slots, "l"/"q" int64 slots.
                    "d" or "f" => "double",
                    "l" or "q" => "int64_t",
                    // wf2 (winforms1): a native-int (IntPtr) parameter is the
                    // full pointer width -- int32_t truncates the x64 HWND/
                    // WPARAM/LPARAM the WNDPROC ABI passes.
                    "n" => "intptr_t",
                    var arr when arr.StartsWith("r:System.Array", StringComparison.Ordinal) => "Il2CppArray*",
                    var r when r.StartsWith("r:", StringComparison.Ordinal) => "Il2CppObject*",
                    // fnptr domain b23: a `delegate*` parameter is a raw
                    // function pointer -- typed void* (the pointer-sized slot
                    // the model uses for native pointers; the calli site
                    // casts to the target signature).
                    var f when f.StartsWith("F:", StringComparison.Ordinal) => "void*",
                    // decimal-domain F1 (net9/10 E2E campaign): a VALUETYPE
                    // value param ("V:<FullName>") is the runtime struct --
                    // int32_t truncates the slot that "dec"-cast icall sites
                    // address (&arg0 as const Il2CppDecimal*). TypeCppName
                    // maps System.Decimal -> il2cpp::runtime::Il2CppDecimal
                    // (plus DateTime/TimeSpan/Guid/user structs).
                    // caudit2-fix: a BY-REF value-type param (ref/out struct) is
                    // an ADDRESS -- declare the struct POINTER (the int32_t default
                    // truncated the x64 address and mis-typed the field access).
                    var bv when bv.StartsWith("B:V:", StringComparison.Ordinal) => TypeCppName(bv[4..]) + "*",
                    var v when v.StartsWith("V:", StringComparison.Ordinal) => TypeCppName(v[2..]),
                    _ => "int32_t",
                });
            }
        }
        return types.ToArray();
    }

    private static string ParamList(string[] paramNames) =>
        paramNames.Length == 0 ? "void" : string.Join(", ", paramNames.Select(p => $"int32_t {p}"));

    private static string ParamListTyped(string[] paramNames, string[] paramTypes) =>
        paramNames.Length == 0
            ? "void"
            : string.Join(", ", paramNames.Select((p, i) => $"{paramTypes[i]} {p}"));

    // Method C++ name: {TypeCppName}_{Sanitize(Method)}_{SigHash(full signature hash)}.
    // Hash input = {owner}.{name}({paramTypes}): the parameter-type signature participates (MethodDef.
    // ParamTypes is filled by MetadataLoader's decode), so overloads (same name, different params) get
    // different C++ function names, using the same encoding as MethodRefResolver.Resolve (MemberRef/
    // MethodDef token -> CppName) for consistent naming. Note: the prefix MUST be TypeCppName(t) (generic
    // definition Box`1 -> "Box_1_int"), not Sanitize(t.FullName) ("Box_1") -- the generic-instance call
    // side (MethodRefResolver TypeSpec path) prefixes with GenericInstantiationName (Box_1_int__ctor_...), and a declaration-name fork = the matrix4 generics regression.
    // Definition-side method C++ name. The hash input uses the '+' canonical
    // owner key (ReflectionFullName == FullName for top-level types, zero
    // churn there) so nested types hash EXACTLY like the call side
    // (MethodRefResolver ownerFullName is md.GetFullName, '+'-form). Hashing
    // the short nested FullName instead produced names no call site references
    // (arrayext2: <>c lambda methods undefined).
    private static string MethodCppName(TypeDef t, MethodDef m) =>
        $"{TypeEmitter.TypeCppName(t)}_{TypeEmitter.Sanitize(m.Name)}_{SigHash.Hash(TypeEmitter.ReflectionFullName(t) + "." + m.Name + "(" + m.ParamTypes + ")")}";

    // entry.cpp: il2cpp_entry_main -> runtime_init + type registration + .cctor + Main. When Main is void, return 0 after the call.

    //--------------------------------------------------------------------------
    // BclAttributeClassNames -- the BCL attribute classes the assembly
    // REFERENCES (item d collection): every TypeReference whose full name ends
    // with "Attribute" (the .NET attribute-class naming convention). These are
    // the names typeof folds / Type.GetType probes can produce; net48 resolves
    // them from mscorlib and the transpiled build must too. Deterministic
    // order (sorted) so entry.cpp output is stable across runs.
    //--------------------------------------------------------------------------
    private static List<string> BclAttributeClassNames(MetadataReader md)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var trh in md.TypeReferences)
        {
            var fullName = md.GetFullName(trh);
            if (fullName.EndsWith("Attribute", StringComparison.Ordinal))
                names.Add(fullName);
        }
        return names.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    //--------------------------------------------------------------------------
    // EmitAttributeRegistrations -- D-ATTR-1 item c: construct the custom
    // attribute INSTANCES of every annotated type and register them
    // (il2cpp_attribute_register). Only attributes whose class is a TRANSLATED
    // user type can be constructed (il2cpp_object_new + ctor + field stores);
    // BCL attribute classes have no transpiled class and stay unregistered
    // (documented simplification -- the registry answers only what it holds).
    // The registry key is the annotated type's METADATA full name -- the same
    // literal the typeof fold / get_custom_named query string carries
    // (GetFullName '+'-form for nested types; the attribute-CLASS lookup
    // below matches either form via MatchesFullName).
    // Capacity: the runtime registry is fixed at kMaxAttributeTypes (32);
    // overflow must surface at COMPILE time, not as a runtime register=0.
    //--------------------------------------------------------------------------
    private static void EmitAttributeRegistrations(CppWriter w, MetadataReader md, List<TypeDef> types, StringPool pool)
    {
        const int kMaxAttributeTypes = 32; // must stay in sync with attribute.cpp
        var annotated = MetadataLoader.LoadAllCustomAttributes(md);
        var typeIndex = 0;
        var attrIndex = 0;
        foreach (var (typeFullName, attrs) in annotated)
        {
            var emitAttrs = new List<(CustomAttributeDef Def, TypeDef Cls)>();
            foreach (var ca in attrs)
            {
                // Nested attribute classes (e.g. Cases.AttrDeep+Tagged):
                // the metadata attribute-type name is '+'-form while expanded
                // nested TypeDefs key by SHORT FullName -- match either form
                // or user attributes silently skip registration (attrdeep
                // GetCustomAttribute(s) on nested types answer empty).
                var cls = types.FirstOrDefault(t => t.MatchesFullName(ca.AttributeType));
                if (cls == null)
                    continue; // BCL / non-translated attribute class: not constructible
                emitAttrs.Add((ca, cls));
            }
            if (emitAttrs.Count == 0)
                continue;
            if (typeIndex >= kMaxAttributeTypes)
                throw new NotSupportedException(
                    $"attribute registry capacity: {kMaxAttributeTypes} annotated types is the runtime maximum (attribute.cpp kMaxAttributeTypes); '{typeFullName}' would overflow");
            w.Line($"// D-ATTR-1 item c: custom attributes of '{typeFullName}' (attribute.h registry)");
            var temps = new List<string>();
            foreach (var (ca, cls) in emitAttrs)
            {
                var tName = $"__il2cpp_attr_{attrIndex++}";
                temps.Add(tName);
                w.Line($"Il2CppObject* {tName} = il2cpp_object_new(&{TypeEmitter.TypeCppName(cls)}_class);");
                if (ca.FixedArgs.Count == 0)
                {
                    // Parameterless attribute ctor: the whole body is the base
                    // Attribute::.ctor no-op (System.Attribute::.ctor is mapped
                    // to il2cpp_object_ctor_nop), so the object is fully
                    // constructed by the nop -- exactly what newobj emits.
                    w.Line($"il2cpp::runtime::il2cpp_object_ctor_nop({tName});");
                }
                else
                {
                    var ctorCpp = MethodRefResolver.Resolve(md, ca.ConstructorToken, types).CppName;
                    var argCpp = ca.FixedArgs.Select(a => EmitAttributeArg(a, pool)).ToList();
                    w.Line($"{ctorCpp}({tName}, {string.Join(", ", argCpp)});");
                }
                foreach (var na in ca.NamedArgs)
                    EmitAttributeNamedArgStore(w, tName, cls, na, pool);
            }
            w.Line($"static Il2CppObject* __il2cpp_attr_arr_{typeIndex}[] = {{ {string.Join(", ", temps)} }};");
            // The registry stores the array POINTER, and the attribute OBJECTS
            // are plain heap allocations -- register every element slot as a GC
            // root (the collector scans ONLY registered roots + frames + heap;
            // unrooted startup objects would be collected on the first
            // allocation-driven collection, leaving the registry dangling).
            for (var i = 0; i < temps.Count; i++)
                w.Line($"il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&__il2cpp_attr_arr_{typeIndex}[{i}]));");
            w.Line($"il2cpp::runtime::il2cpp_attribute_register(\"{EscapeC(typeFullName)}\", __il2cpp_attr_arr_{typeIndex}, {temps.Count});");
            ++typeIndex;
        }
    }

    // The boxed .NET constant of a decoded custom-attribute arg -> the C++
    // literal the generated ctor/field-store expects (string -> the interned
    // pool object, integers/bool/char/enum -> int32/int64 literals per the M1
    // 32-bit contract, null -> nullptr). Unsupported shapes are LOUD (never
    // silently mis-typed).
    private static string EmitAttributeArg(object? value, StringPool pool)
    {
        switch (value)
        {
            case null:
                return "nullptr";
            case string s:
                return $"il2cpp::g_strPool[{pool.IdOf(s)}]";
            case bool b:
                return b ? "1" : "0";
            case char c:
                return $"((int32_t){((int)c).ToString(System.Globalization.CultureInfo.InvariantCulture)})";
            case sbyte sb:
                return $"{sb.ToString(System.Globalization.CultureInfo.InvariantCulture)}LL";
            case byte by:
                return $"{by.ToString(System.Globalization.CultureInfo.InvariantCulture)}LL";
            case short sh:
                return $"{sh.ToString(System.Globalization.CultureInfo.InvariantCulture)}LL";
            case ushort ush:
                return $"{ush.ToString(System.Globalization.CultureInfo.InvariantCulture)}LL";
            case int i:
                return $"{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}LL";
            case uint ui:
                return $"{ui.ToString(System.Globalization.CultureInfo.InvariantCulture)}LL";
            case long l:
                return $"{l.ToString(System.Globalization.CultureInfo.InvariantCulture)}LL";
            case ulong ul when ul <= long.MaxValue:
                return $"{ul.ToString(System.Globalization.CultureInfo.InvariantCulture)}LL";
            case CustomAttributeEnumValue e:
                return $"{e.Underlying.ToString(System.Globalization.CultureInfo.InvariantCulture)}LL";
            default:
                throw new NotSupportedException(
                    $"custom-attribute value of type {(value?.GetType().Name ?? "null")} cannot be emitted to C++ (D-ATTR-1 registration scope: string/int32/char/enum constants only)");
        }
    }

    // A named (field/property) argument -> a direct field store on the
    // constructed instance. The name resolves to the attribute class's public
    // instance field or an auto-property's "<X>k__BackingField" (the loader
    // keeps backing fields in the model). A named arg with no matching field is
    // LOUD (the registry object would silently lack the value otherwise).
    private static void EmitAttributeNamedArgStore(CppWriter w, string tName, TypeDef cls, CustomAttributeNamedArg na, StringPool pool)
    {
        var field = cls.InstanceFields.FirstOrDefault(f => f.Name == na.Name)
                 ?? cls.InstanceFields.FirstOrDefault(f => f.Name == $"<{na.Name}>k__BackingField");
        if (field == null)
            throw new NotSupportedException(
                $"custom attribute '{cls.FullName}' named argument '{na.Name}' has no matching instance field in the translated class");
        var memberName = TypeEmitter.Sanitize(field.Name) == "_obj" ? "_obj_field" : TypeEmitter.Sanitize(field.Name);
        w.Line($"{tName}->{memberName} = {EmitAttributeArg(na.Value, pool)};");
    }

    private static void EmitMain(string outDir, List<TypeDef> types, PEReader pe, MetadataReader md, StringPool pool,
        IReadOnlySet<string> noTypeInfoOwners, IReadOnlyCollection<string>? reflectedNames, int resourceCount)
    {
        var mainType = types.FirstOrDefault(t => t.Methods.Any(m => m.Name == "Main"));
        if (mainType == null) return;
        var w = new CppWriter();
        w.Line("#include \"generated.h\"");
        w.Line();
        // D-ATTR-1 item d: BCL attribute class NAME descriptors (attrdeep
        // recorded defect). Type.GetType("System.ObsoleteAttribute") resolves
        // in net48 from mscorlib; the transpiled typeinfo registry holds only
        // user-assembly types and the builtin class registry (il2cpp_class_by_name
        // -- reflect_base.cpp's get_type BCL fallback) has no attribute-class
        // descriptors, so the query returned null: divergence. Emit a
        // lightweight name-only Il2CppClass per REFERENCED BCL attribute class
        // (the "Attribute" name suffix is the .NET convention) and register it
        // at startup so the BCL fallback answers exactly like net48. baseClass
        // = System.Object; no vtable/interfaces -- the descriptors serve ONLY
        // name resolution (class_by_name never touches the other fields; no
        // generated code casts to them).
        var bclAttrNames = BclAttributeClassNames(md);
        foreach (var bclName in bclAttrNames)
        {
            w.Line($"// BCL attribute class name descriptor (D-ATTR-1 item d): Type.GetType(\"{EscapeC(bclName)}\")");
            w.Line($"il2cpp::Il2CppClass {TypeEmitter.Sanitize(bclName)}_class = {{");
            w.Open();
            w.Line($"\"{EscapeC(bclName)}\",");
            w.Line("static_cast<int32_t>(sizeof(Il2CppObject)), 0u, 0,");
            w.Line("&System_Object_class, nullptr, 0, nullptr, 0,");
            w.Close();
            w.Line("};");
        }
        w.Line();
        w.Line("// M4 Task 7: the C API contract -- il2cpp_runtime_init() must");
        w.Line("// leave the runtime fully usable: runtime bootstrap + export");
        w.Line("// registrations + type registrations + Main-type .cctor. This");
        w.Line("// generated implementation is what external hosts call.");
        w.Line("IL2CPP_EXPORT void il2cpp_runtime_init(void) {");
        w.Open();
        w.Line("il2cpp::runtime::runtime_init();");
        // D-ATTR-1 item d: register the BCL attribute class name descriptors so
        // il2cpp_class_by_name (the Type.GetType BCL fallback, reflect_base.cpp)
        // resolves them. Startup-only and idempotent (class_register dedups).
        foreach (var bclName in bclAttrNames)
            w.Line($"il2cpp::il2cpp_class_register(&{TypeEmitter.Sanitize(bclName)}_class);");
        // D-ATTR-1 item c: the attribute registry -- construct every custom
        // attribute INSTANCE (user attribute classes only; a BCL attribute class
        // has no transpiled class, so it cannot be constructed -- those rows stay
        // unregistered, a documented simplification) and register each annotated
        // type's array. Static arrays: the register contract stores the array
        // POINTER, so it must outlive init. The strings interned here enter the
        // SAME pool the pool-fill below materializes (the table size comes from
        // Program.cs after Generate with the FINAL pool), so g_strPool indices
        // are valid -- and this call must precede the LiteralCount>0 fill check.
        EmitAttributeRegistrations(w, md, types, pool);
        // T3 string pool (design §2.3): eager fill BEFORE any generated user
        // code can run -- the Main-type .cctor (ensure_cctor_* below) may
        // ldstr; the table must be filled or ldstr reads uninitialized slots.
        // Entries are the intern-pool instances (the intern pool is a GC
        // root), so the table needs no root of its own.
        if (pool.LiteralCount > 0)
            w.Line("il2cpp::il2cpp_string_pool_fill();");
        // RES-1 (iron rule 18): the embedded-resource pool data file was
        // emitted by Program.cs (resource-pool.h); fill the runtime registry
        // BEFORE any generated user code can query a resource -- the Main-type
        // .cctor (ensure_cctor_* below) may call GetManifestResourceStream.
        if (resourceCount > 0)
            w.Line("il2cpp::il2cpp_resource_pool_fill();");
        // CG6 (delegate2/delegops): the runtime's delegate method-name registry
        // (il2cpp_delegate_ext.h) is EMPTY unless generated startup code
        // registers every delegate-created method -- Delegate.Method.Name
        // (delegate2.cs:34 / delegops) would throw InvalidOperationException at
        // query time (net48 returns the SHORT method name). Register each
        // method whose address was taken as a delegate target (ldftn /
        // delegate-new during body emission), keyed by the generated C++
        // function address. Re-registration is idempotent (last name wins).
        if (_delegateMethodTokens.Count > 0)
        {
            w.Line("// CG6: register delegate-created method SHORT names (Delegate.Method.Name)");
            foreach (var tok in _delegateMethodTokens)
            {
                var shortName = DelegateMethodShortName(tok, md);
                if (shortName == null) continue;
                w.Line($"il2cpp::il2cpp_delegate_register_method(reinterpret_cast<void*>(&{MethodCppNameFromToken(tok, md, types)}), \"{shortName}\");");
            }
        };
        // M4 Task 7: C API exports -- register ONLY the MAIN type's static
        // methods with the EXACT export-table signature
        // int32_t(int32_t,int32_t,int32_t,int32_t) (M4 contract). The
        // function-pointer assignment is well-defined because the generated
        // declaration has the identical signature; a signature mismatch is
        // filtered out here, never cast (casting function pointers to a
        // different signature and calling through them is UB -- iron rule).
        foreach (var m in mainType.Methods.Where(m =>
            m.IsStatic && m.ReturnType == SignatureTypeCode.Int32 &&
            m.ParamCount == 4 && m.ParamTypes == "i,i,i,i"))
        {
            var fn = MethodCppName(mainType, m);
            w.Line($"il2cpp::il2cpp_runtime_register_export(\"{mainType.FullName}\", \"{m.Name}\", &{fn});");
        }
        // M2 type registration: every type with instances registers its runtime descriptor (the GC/type tables see the type).
        // M4 Task 7: a type with C API exports must be registered too --
        // il2cpp_type_from_name resolves exports by descriptor name.
        // Static roots are registered by each type's .cpp registration function? -- M2 simplification:
        // entry registers the type table uniformly; static-field reference roots join the GC root set in Task 10 (frame records).
        foreach (var t in types)
        {
            // P25b (user struct box): user value types are NOT GC classes and
            // their descriptors were never registered before -- the box icall
            // needs {T}_class reachable (class_by_name / isinst / the
            // unbox-ValueType-chain check). Register them like every other
            // descriptor -- the registration key is the descriptor pointer
            // (idempotent), and the descriptor's instanceSize/refFieldBitmap
            // are the PAYLOAD shape (the box object itself is header+payload,
            // allocated via il2cpp_gc_alloc_size).
            if (t.IsValueType)
            {
                if (!(t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor"))) continue;
                w.Line($"il2cpp::il2cpp_class_register(&{TypeEmitter.TypeCppName(t)}_class);");
                continue;
            }
            var hasExport = t.Methods.Any(m => m.IsStatic &&
                m.ReturnType == SignatureTypeCode.Int32 &&
                m.ParamCount == 4 && m.ParamTypes == "i,i,i,i");
            if (!(t.InstanceFields.Count > 0 || t.Methods.Any(m => m.Name == ".ctor")
                  || t.VirtualMethods.Count > 0 || t.IsInterface || hasExport)) continue;
            // CG6 (delegate2/delegatecb link FAIL): custom delegate TypeDefs
            // have no descriptor (IsDelegateTypeDef) -- registering
            // &{T}_class is an undefined symbol.
            if (IsDelegateTypeDef(t)) continue;
            w.Line($"il2cpp::il2cpp_class_register(&{TypeEmitter.TypeCppName(t)}_class);");
        }
        // FixAV (winforms1 user32 AV): register every REFERENCE static field
        // (the Il2CppObject* slots -- delegates, objects, strings, arrays) as a
        // GC root. The runtime's compacting collector (gc.cpp) sweeps unmarked
        // blocks and rewrites only REGISTERED roots after a compaction; an
        // unregistered static delegate field keeps a stale pointer once a
        // collection fires, so icall_delegate_get_fn_ptr can read a garbage
        // methodPtr and USER32's dispatch calls a bad lpfnWndProc address
        // (0xC0000005 in the window-message path). il2cpp_gc_add_static_root is
        // address-dedup and bounded (kMaxStaticRoots), and registering the
        // field's ADDRESS is valid before the .cctor assigns its value.
        foreach (var t in types)
        {
            foreach (var refField in TypeEmitter.ReferenceStaticFieldCppNames(t, md))
                w.Line($"il2cpp::il2cpp_gc_add_static_root(reinterpret_cast<void**>(&{refField}));");
        }
        // M7D user-finalizer wiring (gcext e2e, matrix7 diff item 1): a type
        // with an OWN Finalize override (a C# destructor) registers its
        // Finalize method pointer in the runtime's TYPE-level finalizer
        // registry at startup. The runtime half (il2cpp_gc_register_type_finalizer
        // + the AllocCore auto-arm, il2cpp_gc.h/gc.cpp) then arms every
        // subsequent instance of that type with the finalize method at
        // allocation, so the collector runs the body with two-collection
        // semantics -- without this registration the Finalize method is a
        // dead vtable slot (net48: a type with a finalizer is registered at
        // object construction automatically). Registration is startup-only
        // and idempotent (re-register replaces the callback).
        // A Finalize override implies an instance surface (.ctor/virtual), so
        // the {T}_class descriptor the registry keys on always exists; value
        // types and delegates cannot declare user finalizers.
        foreach (var t in types)
        {
            var fin = t.Methods.FirstOrDefault(m => m.Name == "Finalize");
            if (fin == null || IsDelegateTypeDef(t)) continue;
            w.Line($"il2cpp::il2cpp_gc_register_type_finalizer(&{TypeEmitter.TypeCppName(t)}_class, &{MethodCppName(t, fin)});");
        }
        // M7R2 (enum register wiring -- the REGISTER WIRING REPORT item c
        // call site): every user enum TypeDef publishes its member table
        // (names + values) to the runtime registry BEFORE user code runs.
        // il2cpp_enum_to_string(&{T}_class, value) then finds the member
        // NAME via FindInfo by descriptor name ("Cases.Color" -> the
        // registered table) -- enumdeep/enumdeep2 ToString byte-exactness.
        // Enums are NOT IsValueType (base is System.Enum, not
        // System.ValueType), so the class_register loop above emitted their
        // name-bearing descriptors (InstanceFields value__ > 0); this loop
        // is the OWN enum loop the wiring report requires (il2cpp_runtime_init
        // static init, startup-only, no-sync -- enumext.h contract). Capacity
        // is enforced at compile time (item d).
        var enumTypes = types.Where(t => t.BaseTypeName == "System.Enum").ToList();
        MethodRefResolver.EnsureEnumRegisterCapacity(enumTypes.Count);
        foreach (var t in enumTypes)
            if (MethodRefResolver.TryGetUserEnumRegisterInfo(t, out var einfo))
                w.Line(MethodRefResolver.EmitEnumRegisterBlock(einfo.TypeName, einfo.UnderlyingTypeCode, einfo.Names, einfo.Values));
        // M5.5 reflection (R-REFL-01): register every reflected type's TypeInfo
        // table in the runtime registry BEFORE any Type.GetType/GetMethod/
        // GetField query runs (the registry is static; registration is
        // idempotent -- typeinfo.cpp). Mode A = all types; mode B (refl2 S3) =
        // the manifest-scoped set. The registration must follow the
        // class_register loop (the TypeInfo tables reference the descriptors)
        // and precede the Main-type .cctor ensure below (reflectcase/refinvoke
        // queries run inside Main).
        foreach (var t in types)
            if (ShouldEmitTypeInfo(t, reflectedNames, noTypeInfoOwners))
                w.Line(TypeEmitter.EmitTypeInfoRegistration(t));
        // The Main type's .cctor fires through the lazy guard: static-field accesses/calls inside
        // Main's body already contain ensure calls; if Main is itself a static-method call (entry calls
        // Main) and the Main type has no field access, .cctor would not fire -- .NET allows deferred beforefieldinit, so M2 explicitly has entry call ensure on the Main type (idempotent).
        var mainCctor = mainType.Methods.FirstOrDefault(m => m.Name == ".cctor");
        if (mainCctor != null)
        {
            w.Line($"ensure_cctor_{TypeEmitter.TypeCppName(mainType)}();");
        }
        w.Close();
        w.Line("}");
        w.Line();
        var mainMethod = mainType.Methods.First(m => m.Name == "Main");
        var mainTakesArgs = mainMethod.ParamCount == 1 && mainMethod.ParamTypes == "r:System.String[]";
        w.Line(mainTakesArgs
            ? "IL2CPP_EXPORT int32_t il2cpp_entry_main(int32_t argc, char** argv) {"
            : "IL2CPP_EXPORT int32_t il2cpp_entry_main(void) {");
        w.Open();
        w.Line("il2cpp_runtime_init();");
        // M6 recurse work order: Main(string[] args) -- construct the managed
        // string[] from the host argv (il2cpp_array_new with the reference
        // element contract + il2cpp_string_new_from_utf8 per element) and pass
        // it. Without this the entry calls Main with zero args against a
        // Main(Il2CppObject* arg0) declaration -- an arity mismatch (recurse
        // case used Main() to dodge it).
        // .NET argv semantics: Main(string[] args) EXCLUDES argv[0] (the
        // program name) -- the host forwards the full C argv, so the managed
        // array is built from argv[1..argc-1] (length argc-1; an argc<=1 host
        // invocation yields the empty array, matching .NET's zero args).
        if (mainTakesArgs)
        {
            w.Line("// main-args entry: build the managed string[] from the host argv");
            w.Line("// (argv[0] is the program name -- .NET Main(string[]) excludes it).");
            w.Line("int32_t __il2cpp_argc = argc > 1 ? argc - 1 : 0;");
            w.Line("il2cpp::Il2CppArray* __il2cpp_entry_args = il2cpp::il2cpp_array_new(nullptr, sizeof(void*), true, static_cast<int32_t>(__il2cpp_argc));");
            w.Line("for (int32_t __il2cpp_arg = 0; __il2cpp_arg < __il2cpp_argc; __il2cpp_arg++) {");
            w.Open();
            w.Line("*reinterpret_cast<Il2CppObject**>(il2cpp::il2cpp_array_get_elem_ptr(__il2cpp_entry_args, __il2cpp_arg)) = reinterpret_cast<Il2CppObject*>(il2cpp::il2cpp_string_new_from_utf8(argv[__il2cpp_arg + 1]));");
            w.Close();
            w.Line("}");
        }
        if (MethodBodyReader.Read(pe, md, mainType, mainMethod).ReturnsVoid)
        {
            w.Line(mainTakesArgs
                ? $"{MethodCppName(mainType, mainMethod)}(reinterpret_cast<Il2CppObject*>(__il2cpp_entry_args));"
                : $"{MethodCppName(mainType, mainMethod)}();");
            w.Line("return 0;");
        }
        else
        {
            w.Line(mainTakesArgs
                ? $"return {MethodCppName(mainType, mainMethod)}(reinterpret_cast<Il2CppObject*>(__il2cpp_entry_args));"
                : $"return {MethodCppName(mainType, mainMethod)}();");
        }
        w.Close();
        w.Line("}");
        // M4 Task 7: entry.cpp holds the C-API-exportable entry (no main --
        // the STATIC library form is linked by external hosts); the
        // self-test executable gets a separate host.cpp with main.
        File.WriteAllText(Path.Combine(outDir, "src", "entry.cpp"), w.ToString());

        // host.cpp: the executable's main. This file belongs to the
        // il2cpp_gen_exe target ONLY (a library must not carry a main).
        var h = new CppWriter();
        h.Line("#include \"generated.h\"");
        h.Line();
        // MinGW/MSVC CRT needs main: il2cpp_entry_main is the C-API exported entry; main calls it as the host.
        // M6 recurse work order: a Main(string[] args) entry needs the host
        // argv -- main forwards argc/argv to il2cpp_entry_main.
        h.Line(mainTakesArgs ? "int main(int argc, char** argv) {" : "int main() {");
        h.Open();
        // M3 Task 10: top-level guard -- an unhandled managed exception is
        // reported (with the frame stack) and the process exits non-zero
        // (never silent; user iron rule).
        h.Line("try {");
        h.Open();
        h.Line(mainTakesArgs ? "return il2cpp_entry_main(argc, argv);" : "return il2cpp_entry_main();");
        h.Close();
        h.Line("} catch (const il2cpp::DotNetException& __il2cpp_unhandled) {");
        h.Open();
        h.Line("return il2cpp::il2cpp_report_unhandled_exception(__il2cpp_unhandled.message_text());");
        h.Close();
        h.Line("}");
        h.Close();
        h.Line("}");
        File.WriteAllText(Path.Combine(outDir, "src", "host.cpp"), h.ToString());
    }

    //--------------------------------------------------------------------------
    // ComputePinvokeManifest -- derive the P-D4 static/dynamic P/Invoke
    // manifest ONCE per Generate (called right after the metadata reader is
    // opened; under _poolLock).
    //
    // A [DllImport] library is STATIC when the case directory carries a
    // library dir whose CMakeLists does NOT build it SHARED (mylib STATIC;
    // system DLL INTERFACE import-lib targets like kernel32). It is DYNAMIC
    // (runtime resolve via il2cpp_pinvoke_resolve) when the case's library
    // dir is ABSENT (out-of-manifest, e.g. nosuchlib_d4) or builds the
    // library SHARED (the P-D4 extdll e2e). "QCall" is never a DLL and stays
    // on the legacy static decl path. The dynamic-set keys carry BOTH the raw
    // module name and the ".dll"-stripped base so call-site lookups (raw) and
    // dir-keyed lookups (base) both hit.
    //--------------------------------------------------------------------------
    private static void ComputePinvokeManifest(IReadOnlyList<TypeDef> types, MetadataReader md, string assemblyPath, string outDir)
    {
        _dynamicPinvokeModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _pinvokeDynamicSig.Clear();
        var modules = types
            .SelectMany(t => t.Methods)
            .Where(m => m.IsPInvoke && m.PinvokeModule != null)
            .Select(m => m.PinvokeModule!)
            .Distinct()
            .ToList();
        foreach (var mod in modules)
        {
            if (mod.Equals("QCall", StringComparison.Ordinal)) continue;
            var modBase = mod.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? mod.Substring(0, mod.Length - 4) : mod;
            var libDir = FindPinvokeLibDir(assemblyPath, outDir, modBase);
            var isDynamic = libDir == null; // out-of-manifest
            if (!isDynamic)
            {
                var cmakePath = Path.Combine(libDir!, "CMakeLists.txt");
                if (File.Exists(cmakePath))
                {
                    var text = File.ReadAllText(cmakePath);
                    // add_library(<name> SHARED ...) => the library is BUILT
                    // shared and NOT linked into the exe (dynamic resolution).
                    if (Regex.IsMatch(text,
                        $@"add_library\s*\(\s*{Regex.Escape(modBase)}\s+SHARED\b",
                        RegexOptions.IgnoreCase))
                        isDynamic = true;
                }
            }
            if (!isDynamic) continue;
            _dynamicPinvokeModules.Add(mod);
            _dynamicPinvokeModules.Add(modBase);
            // Record the native signature for each dynamic entry -- the call
            // site casts the resolve pointer with the SAME types the extern
            // "C" declaration would have named (single source of truth).
            foreach (var m in types.SelectMany(t => t.Methods)
                         .Where(m => m.IsPInvoke && string.Equals(m.PinvokeModule, mod, StringComparison.OrdinalIgnoreCase)))
            {
                var entry = m.PinvokeEntryName ?? m.Name;
                if (string.IsNullOrEmpty(entry)) continue;
                var key = (modBase, entry);
                if (!_pinvokeDynamicSig.ContainsKey(key))
                    _pinvokeDynamicSig[key] = (PinvokeReturnTypeCpp(md, m), PinvokeParamList(md, m));
            }
        }
    }

    // Find the native-library directory (a folder holding CMakeLists.txt) for
    // a P/Invoke module. The assembly may sit at several layouts -- e2e
    // staging (tests/cases/<case>/out_stage_<compiler>/), unit-fixture
    // obj/Release/net48/, or a repo-root sibling -- so WALK UP from the
    // assembly's directory to a directory that contains <modBase>/
    // CMakeLists.txt (bounded walk; the first hit on the leaf-most layout).
    // Falls back to outDir/<modBase>; null = OUT-OF-MANIFEST (dynamic).
    private static string? FindPinvokeLibDir(string assemblyPath, string outDir, string modBase)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        for (var depth = 0; dir != null && depth < 16; ++depth)
        {
            var candidate = Path.Combine(dir, modBase);
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "CMakeLists.txt")))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        var outLib = Path.Combine(Path.GetFullPath(outDir), modBase);
        if (Directory.Exists(outLib) && File.Exists(Path.Combine(outLib, "CMakeLists.txt")))
            return outLib;
        return null;
    }

    private static void EmitCmake(string outDir, List<string> srcs, string assemblyPath, IReadOnlyList<TypeDef> types)
    {
        var w = new CppWriter();
        w.Line("cmake_minimum_required(VERSION 3.10)");
        w.Line("project(il2cpp_gen CXX)");
        w.Line($"add_subdirectory({RuntimeRelativePath(outDir, assemblyPath)} ${{CMAKE_BINARY_DIR}}/runtime)");
        // M4 Task 1 / P-D4: P/Invoke native libraries. STATIC modules (mylib
        // STATIC, system DLL INTERFACE targets) are add_subdirectory'd AND
        // linked (target_link_libraries). DYNAMIC modules (out-of-manifest or
        // SHARED builds) are add_subdirectory'd -- the .dll still needs to be
        // BUILT for runtime deployment -- but are NOT linked (the generated
        // call sites resolve them at runtime). A module whose case directory
        // does not exist gets NO add_subdirectory at all (the old emission
        // made cmake configure fail on it).
        var pinvokeModules = types
            .SelectMany(t => t.Methods)
            .Where(m => m.IsPInvoke && m.PinvokeModule != null)
            .Select(m => m.PinvokeModule!)
            .Distinct()
            .ToList();
        var linkedPinvokeModules = new List<string>();
        foreach (var mod in pinvokeModules)
        {
            // The native library lives in the CASE directory (one level above
            // the assembly output dir): tests/cases/<name>/mylib/.
            var modBase = mod.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? mod.Substring(0, mod.Length - 4) : mod;
            var libDir = FindPinvokeLibDir(assemblyPath, outDir, modBase);
            if (libDir == null) continue; // out-of-manifest: build nothing here
            var rel = RelTo(outDir, libDir);
            w.Line($"add_subdirectory({rel} ${{CMAKE_BINARY_DIR}}/{modBase})");
            if (PinvokeModuleIsDynamic(mod)) continue; // SHARED: build only, no link
            linkedPinvokeModules.Add(modBase);
        }
        // M4 Task 7: the PRODUCT form is a static library (il2cpp_gen) that
        // external programs link against; il2cpp_gen_exe remains the
        // self-test/double-run target. Both share the translated sources;
        // entry.cpp (the C-API entry + export registrations) goes into the
        // library, host.cpp (main) into the executable only.
        w.Line("add_library(il2cpp_gen STATIC");
        foreach (var s in srcs) w.Line("    src/" + s + ".cpp");
        w.Line("    src/entry.cpp");
        w.Line(")");
        w.Line("target_include_directories(il2cpp_gen PUBLIC include/internal)");
        w.Line("target_link_libraries(il2cpp_gen PUBLIC il2cpp_runtime" + (linkedPinvokeModules.Count > 0 ? " " + string.Join(" ", linkedPinvokeModules) : "") + ")");
        w.Line("target_compile_features(il2cpp_gen PUBLIC cxx_std_17)");
        w.Line("if(MSVC)");
        w.Line("  target_compile_options(il2cpp_gen PRIVATE /W4 /EHsc /bigobj)");
        w.Line("else()");
        w.Line("  target_compile_options(il2cpp_gen PRIVATE -Wall -Wextra -fexceptions)");
        w.Line("endif()");
        w.Line("add_executable(il2cpp_gen_exe");
        w.Line("    src/host.cpp");
        w.Line(")");
        w.Line("target_include_directories(il2cpp_gen_exe PRIVATE include/internal)");
        w.Line("target_link_libraries(il2cpp_gen_exe PRIVATE il2cpp_gen)");
        w.Line("target_compile_features(il2cpp_gen_exe PRIVATE cxx_std_17)");
        w.Line("if(MSVC)");
        w.Line("  target_compile_options(il2cpp_gen_exe PRIVATE /W4 /EHsc /bigobj)");
        w.Line("else()");
        w.Line("  target_compile_options(il2cpp_gen_exe PRIVATE -Wall -Wextra -fexceptions)");
        w.Line("endif()");
        File.WriteAllText(Path.Combine(outDir, "CMakeLists.txt"), w.ToString());
    }

    // Relative path (same volume); degrades to an absolute path across volumes.
    private static string RelTo(string fromDir, string toDir)
    {
        try
        {
            return Path.GetRelativePath(Path.GetFullPath(fromDir), Path.GetFullPath(toDir)).Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return toDir.Replace('\\', '/');
        }
    }

    // M1 fix: the add_subdirectory src/runtime relative path is computed from outDir's actual location
    // (Task 9 puts gen under tests/cases/<name>/gen, so ../src/runtime cannot be hard-coded). Find the
    // repo root upward from the assembly's directory, then relativize against outDir (absolute across volumes, resolvable by cmake).
    private static string RuntimeRelativePath(string outDir, string assemblyPath)
    {
        var repoRoot = FindRepoRoot(assemblyPath);
        if (repoRoot == null) return "../src/runtime"; // fallback: the old behavior when the repo root is not found
        var runtimeSrc = Path.GetFullPath(Path.Combine(repoRoot, "src", "runtime"));
        string rel;
        try
        {
            rel = Path.GetRelativePath(Path.GetFullPath(outDir), runtimeSrc);
        }
        catch (ArgumentException)
        {
            rel = runtimeSrc; // different volume: fall back to the absolute path
        }
        return rel.Replace('\\', '/');
    }

    private static string? FindRepoRoot(string assemblyPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "src", "runtime"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}












