#pragma once
//------------------------------------------------------------------------------
// appdomain.h -- System.AppDomain minimal object closure runtime support
// (adm1 batch).
//
// The AOT process IS the default AppDomain (single-domain model; CreateDomain
// / Unload / the security CAS surface are LOUD rejections). The net48
// observable public surface of the default domain is reproduced from the
// process itself:
//
//   get_CurrentDomain      -- a lazily allocated process-wide singleton
//                             (Il2CppAppDomainObject, zero-state carrier)
//   get_Id                 -- 1 (net48 default-domain id)
//   get_FriendlyName       -- the exe's file base name (incl. ".exe")
//   get_BaseDirectory      -- the exe directory + trailing separator (NOT
//                             AppContext.BaseDirectory -- net48 measures the
//                             exe dir, AppContext measures the CWD)
//   get_RelativeSearchPath -- null (default host)
//   get_DomainManager      -- null (default host; W7: host-injected manager)
//   get_SetupInformation   -- an AppDomainSetup carrier (ApplicationBase ==
//                             BaseDirectory; ConfigurationFile == exe dir +
//                             exe name + ".Config")
//   GetData/SetData        -- a process-level key -> object table (single
//                             domain => domain isolation is unobservable, so
//                             the table is Do/process-local; APPBASE and
//                             APP_CONFIG_FILE derive from the exe path)
//   CreateInstance/CreateInstanceAndUnwrap -- il2cpp_class_by_name + object
//                             allocation for REGISTERED types (bad assembly
//                             -> FileNotFoundException; null args throw)
//   IsFullyTrusted/IsHomogenous/IsDefaultAppDomain -- true (default host)
//   add_/remove_UnhandledException -- a process-level delegate chain
//                             (raise is W7: the crash-termination hook is not
//                             implemented)
//
// ExecuteAssembly / PermissionSet / Evidence / ApplicationTrust /
// GetAssemblies / ToString stay LOUD (runtime NotSupportedException) -- no
// runtime assembly loader / CAS object model exists.
//
// OBJECT MODEL -- three zero-state/one-field carriers (the Stopwatch
// carrier precedent, stopwatch.h):
//   * System_AppDomain_class:    GLOBAL descriptor; the singleton is a header
//                                only (Il2CppAppDomainObject) -- every getter
//                                arm derives from the process; GetType/isinst/
//                                castclass resolve solely through the
//                                descriptor name.
//   * System_AppDomainSetup_class: GLOBAL descriptor; zero-state carrier.
//   * System_Runtime_Remoting_ObjectHandle_class: GLOBAL descriptor + ONE
//                                reference field wrapping a CreateInstance
//                                result (Unwrap reads it back).
// Generated code references &System_AppDomain_class for isinst/castclass and
// &System_AppDomainSetup_class / &System_Runtime_Remoting_ObjectHandle_class
// for the SetupInformation / CreateInstance results (the global mangled-symbol
// contract in il2cpp_metadata.h). appdomain.cpp registers all three lazily.
//------------------------------------------------------------------------------

#include <cstdint>

#include "il2cpp_object.h"   // Il2CppObject (managed object header)
#include "il2cpp_metadata.h" // Il2CppClass (descriptor type; System_Object_class)

// System.AppDomain runtime descriptor (GLOBAL scope -- the transpiler emits
// &System_AppDomain_class for isinst/castclass; defined in appdomain.cpp,
// registered lazily through il2cpp_appdomain_class()).
extern il2cpp::Il2CppClass System_AppDomain_class;

// System.AppDomainSetup runtime descriptor (GLOBAL scope -- the
// SetupInformation carrier).
extern il2cpp::Il2CppClass System_AppDomainSetup_class;

// System.Runtime.Remoting.ObjectHandle runtime descriptor (GLOBAL scope --
// the CreateInstance carrier).
extern il2cpp::Il2CppClass System_Runtime_Remoting_ObjectHandle_class;

namespace il2cpp {

// Managed System.AppDomain: zero-state carrier (the process IS the default
// domain; every getter arm derives from the process). NO reference fields
// (refFieldBitmap 0).
struct Il2CppAppDomainObject
{
    Il2CppObject obj; // object header (klass pointer, offset 0)
};

// Managed System.AppDomainSetup: zero-state carrier (ApplicationBase /
// ConfigurationFile derive from the process exe path). It is a single
// process-wide instance; net48 returns a fresh object per get, but the
// carrier exposes no writable state on the probed surface, so identity is
// unobservable.
struct Il2CppAppDomainSetupObject
{
    Il2CppObject obj; // object header (klass pointer, offset 0)
};

// Managed System.Runtime.Remoting.ObjectHandle: object header + ONE wrapped
// reference (slot 1 -- refFieldBitmap bit 1 set). Unwrap reads it back.
struct Il2CppObjectHandleObject
{
    Il2CppObject obj;    // object header (klass pointer, offset 0)
    Il2CppObject* value; // the wrapped object (GC reference, slot 1)
};

// The System.AppDomain descriptor (lazily registered on first use; also
// registers the AppDomainSetup + ObjectHandle descriptors -- they share the
// one lazy registration).
Il2CppClass* il2cpp_appdomain_class();

// The process-wide AppDomain singleton (net48 default domain). Lazily
// allocated through System_AppDomain_class, GC-rooted, returned on every
// call (the SAME pointer -- refeq across get_CurrentDomain calls).
Il2CppObject* il2cpp_appdomain_singleton();

// The process-wide AppDomainSetup carrier (SetupInformation result).
Il2CppObject* il2cpp_appdomain_setup_singleton();

} // namespace il2cpp

namespace il2cpp { namespace runtime {

// AppDomain.CurrentDomain (static) -- the singleton.
Il2CppObject* icall_appdomain_get_current_domain();

// AppDomain.Id (instance) -- 1 (net48 default-domain id).
int32_t icall_appdomain_get_id_public(Il2CppObject* self);

// AppDomain.FriendlyName (instance) -- the exe's file base name (with .exe).
Il2CppObject* icall_appdomain_get_friendly_name(Il2CppObject* self);

// AppDomain.BaseDirectory (instance) -- the exe directory + trailing
// separator. (net48 measures the EXE dir here -- different from
// AppContext.BaseDirectory which measures the CWD.)
Il2CppObject* icall_appdomain_get_base_directory(Il2CppObject* self);

// AppDomain.RelativeSearchPath (instance) -- null (default host).
Il2CppObject* icall_appdomain_get_relative_search_path(Il2CppObject* self);

// AppDomain.DomainManager (instance) -- null (default host; W7: the
// host-injected manager scenario is not implemented).
Il2CppObject* icall_appdomain_get_domain_manager(Il2CppObject* self);

// AppDomain.SetupInformation (instance) -- the AppDomainSetup carrier.
Il2CppObject* icall_appdomain_get_setup_information(Il2CppObject* self);

// AppDomain.GetData(string) (instance) -- process-level key->value table;
// APPBASE / APP_CONFIG_FILE derive from the exe path when never SetData'd.
// null name -> ArgumentNullException.
Il2CppObject* icall_appdomain_get_data(Il2CppObject* self, Il2CppObject* name);

// AppDomain.SetData(string, object) (instance) -- store/overwrite in the
// process-level table. null name -> ArgumentNullException.
void icall_appdomain_set_data(Il2CppObject* self, Il2CppObject* name,
                              Il2CppObject* value);

// AppDomain.CreateInstance(string assemblyName, string typeName) (instance)
// -- allocate a REGISTERED-type instance and wrap it in an ObjectHandle.
// Unknown assembly/type -> FileNotFoundException; null args ->
// ArgumentNullException. (Registered-type domain only -- the type must have
// been reached by the program so its descriptor exists.)
Il2CppObject* icall_appdomain_create_instance(Il2CppObject* self,
                                              Il2CppObject* assemblyName,
                                              Il2CppObject* typeName);

// AppDomain.CreateInstanceAndUnwrap(string, string) (instance) -- as above,
// returning the created object directly (no ObjectHandle wrapper).
Il2CppObject* icall_appdomain_create_instance_and_unwrap(Il2CppObject* self,
                                                         Il2CppObject* assemblyName,
                                                         Il2CppObject* typeName);

// AppDomain.IsFullyTrusted (instance) -- true (default host).
int32_t icall_appdomain_is_fully_trusted(Il2CppObject* self);

// AppDomain.IsHomogenous (instance) -- true (default host).
int32_t icall_appdomain_is_homogenous(Il2CppObject* self);

// AppDomain.IsDefaultAppDomain (instance) -- true (the AOT process IS the
// default domain).
int32_t icall_appdomain_is_default_app_domain(Il2CppObject* self);

// AppDomain.add_UnhandledException (instance) -- combine the handler into the
// process-level event chain (raise is W7, not implemented).
void icall_appdomain_add_unhandled_exception(Il2CppObject* self,
                                             Il2CppObject* handler);

// AppDomain.remove_UnhandledException (instance) -- remove from the chain.
void icall_appdomain_remove_unhandled_exception(Il2CppObject* self,
                                                Il2CppObject* handler);

// AppDomainSetup.ApplicationBase (instance) -- exe dir + trailing separator.
Il2CppObject* icall_appdomain_setup_get_application_base(Il2CppObject* self);

// AppDomainSetup.ConfigurationFile (instance) -- exe dir + exe name +
// ".Config".
Il2CppObject* icall_appdomain_setup_get_configuration_file(Il2CppObject* self);

// ObjectHandle.Unwrap() (instance) -- the wrapped object.
Il2CppObject* icall_objecthandle_unwrap(Il2CppObject* self);

//------------------------------------------------------------------------------
// LOUD strains (single-source difftest mapping): these members have no
// runtime implementation (no assembly loader / CAS object model), so they are
// mapped to icalls that throw NotSupportedException AT RUNTIME. The mapping
// keeps the single-source probe compilable+linkable (before adm1 they were
// transpile-aborts) -- the p-adb oracle asserts the loud throw.
//------------------------------------------------------------------------------

// ExecuteAssembly(string) (instance) -- LOUD: no runtime assembly loader.
int32_t icall_appdomain_execute_assembly(Il2CppObject* self, Il2CppObject* assemblyFile);

// PermissionSet (instance) -- LOUD: no CAS object model.
Il2CppObject* icall_appdomain_get_permission_set(Il2CppObject* self);

// Evidence (instance) -- LOUD: no CAS object model.
Il2CppObject* icall_appdomain_get_evidence(Il2CppObject* self);

// ApplicationTrust (instance) -- LOUD: no CAS object model.
Il2CppObject* icall_appdomain_get_application_trust(Il2CppObject* self);

// GetAssemblies() (instance) -- LOUD: AOT single-program assembly model.
Il2CppObject* icall_appdomain_get_assemblies(Il2CppObject* self);

// ToString() (instance) -- LOUD: the localized (culture-dependent) name text
// is W7 (same trailing Exception.ToString culture boundary).
Il2CppObject* icall_appdomain_to_string(Il2CppObject* self);

}} // namespace il2cpp::runtime