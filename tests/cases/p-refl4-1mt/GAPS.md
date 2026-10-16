# GAPS.md — p-refl4-1mt (refl4 item 1)

Case status: PASS byte-exact (net48 oracle vs ILC transpiled run, sha-equal).

## Fixed: MemberInfo.get_MemberType + MemberInfo.get_DeclaringType (incl. C5)

- **Phenomenon**: `MemberInfo.get_MemberType` and `get_DeclaringType` were
  phantom (REFL-EXPLORE4 A1/A2): the receiver is the member-NAME string (the
  simplified member model -- a MemberInfo VALUE is its name string), and no
  MethodMap arm / runtime data source existed. A single gap call dropped the
  whole containing method to a runtime throwing stub.
- **Fix**:
  - Runtime: `il2cpp_memberinfo_get_member_type(memberName)` -- classifies the
    member-VALUE string by scanning the typeinfo member tables (methods first,
    then properties, then events, then fields -- an event name collides with
    its compiler-generated backing field `EvtD`, so the EVENT table wins over
    the FIELD table for the net48 event answer). Returns the net48 MemberTypes
    numeric (Method=8 Field=4 Property=16 Constructor=1 Event=2 TypeInfo=32
    NestedType=128): the `.ctor` disambiguated value `"<FullName>::.ctor"` ->
    1; a registered typeinfo name -> 32, nested (`+`) -> 128; unregistered BCL
    type names -> 32 (defensive).
  - Runtime: `il2cpp_memberinfo_get_declaring_type(memberName)` -- returns the
    declaring type's full-name string (a Type VALUE) by registry scan of the
    DEEPEST type declaring the member (own-table membership; overrides
    re-declare the name in the derived table so most-derived wins; inherited
    members stay under their declaring type's table so the base wins), or the
    OUTER type of a nested type name, or the parsed type of a ctor value, or
    null for a top-level TYPE receiver (net48 `typeof(X).DeclaringType`).
  - Transpiler: `System.Reflection.MemberInfo` owner arms for both properties
    (any member/Type VALUE string receiver) in `MethodMap.Object.cs`.
- **net48 semantics pinned by the oracle**:
  - MemberTypes values: `mt.method=8 mt.field=4 mt.prop=16 mt.event=2
    mt.ctor=1 mt.nested=128 mt.typeinfo=32`.
  - GetMethods()/GetFields()/GetProperties()/GetEvents()/GetConstructors()
    ARRAY ELEMENTS (the C5 gap) are member-NAME strings; MemberType on the
    element classifies by kind (c5.method=8 c5.field=4 c5.prop=16 c5.propinh
    =16 c5.event=2 c5.ctor=1).
  - DeclaringType: own -> itself, inherited -> base, override -> most derived,
    nested type -> outer, non-nested type receiver -> null, ctor -> the type.
- **RED**: runtime exit 1, all 26 records missing (phantom per-record).
- **GREEN**: 26/26 records byte-exact, firstDiff=-1, sha-equal.

## Known simplification (W7)

- A member name that exists in BOTH a field table and an event table (an
  auto-event's backing field shares the event name) answers Event (2) -- the
  events table wins (net48 GetEvent/GetEvents answer Event; the backing field
  name is invisible to those receivers). A net48 `GetField("EvtD")` on such a
  backing field cannot be told apart and answers Event too.
- The backend `value__`/backing-field names (`<PropD>k__BackingField`) are NOT
  probed (they classify Field=4 if queried -- the model enumerates them in
  GetFields like net48).