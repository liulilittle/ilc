#pragma once
//------------------------------------------------------------------------------
// il2cpp_datatable.h -- System.Data minimal in-memory surface (batch 152f,
// ADO stage 1): DataTable / DataColumn / DataRow object model.
//
//   Il2CppDataTable { columns: string[] (column names), rows: object[][],
//                     colCount, rowCount }
//     A plain column-name list plus a row store of object[] value arrays
//     (every element is a boxed managed value; the collector scans both
//     arrays -- the descriptors' refFieldBitmap covers columns/rows).
//   Il2CppDataColumn { name: string } -- returned by Columns.Add (the
//     caller usually ignores it; the name is readable back via ColumnName).
//   Il2CppDataRow { table: ref, rowIndex: int32 } -- returned by Rows.Add /
//     Rows[index]; get_Item reads table.rows[rowIndex][col].
//
// COLUMN TYPES (DataTable.Compute aggregate typing): net48 stores a DataType
// per DataColumn and the aggregate RESULT TYPE follows it -- an int column's
// SUM is Int64, its AVG is Int32 with INTEGER division, a decimal column's
// aggregates stay Decimal (MEASURED, see datatable.cpp's Compute comment).
// The model keeps one full type-name string per column in `colTypes`
// ("" = no declared type, the historical untyped column, whose values net48
// coerces to System.String).
//
// GC safety: all reference stores go through il2cpp_write_barrier; rows are
// appended by GROWING the rows array (one new array + pure copies -- no
// allocation between birth and publication, the listi4 discipline).
//------------------------------------------------------------------------------

#include "il2cpp_object.h"
#include "il2cpp_metadata.h" // Il2CppClass descriptors
#include "il2cpp_array.h"    // Il2CppArray
#include "il2cpp_string.h"   // Il2CppString (the column name)

#include <cstdint>

namespace il2cpp {
namespace runtime {

// Managed layouts (see stackqueue.h conventions: slot 0 = object header).
struct Il2CppDataTable
{
    Il2CppObject _obj;      // slot 0: object header
    Il2CppArray* columns;   // slot 1: string[] of column names
    Il2CppArray* rows;      // slot 2: object[][] of row value arrays
    int32_t      colCount;  // slot 3 low half
    int32_t      rowCount;  // slot 3 high half
    Il2CppString* name;     // slot 4: DataTable.TableName (ADO stage 3)
    Il2CppArray* colTypes;  // slot 5: string[] of DataType full names ("" = untyped)
};

struct Il2CppDataColumn
{
    Il2CppObject _obj;      // slot 0
    Il2CppString* name;     // slot 1: the column name
    Il2CppString* dataType; // slot 2: DataType full name ("System.Int32", "" = untyped)
};

struct Il2CppDataRow
{
    Il2CppObject   _obj;     // slot 0
    Il2CppObject*  table;    // slot 1: the owning DataTable
    int32_t        rowIndex; // slot 2 low half
    Il2CppArray*   values;   // slot 3: the row's value array (detached
                             // NewRow pre-fill; attached rows alias the
                             // entry in the table's rows array)
};

// System.Data.DataTable
void   icall_datatable_ctor(Il2CppObject* self);
Il2CppObject* icall_datatable_columns_add(Il2CppObject* self, Il2CppObject* name);
// Columns.Add(string, Type): append a TYPED column. 'typeName' is the DataType's
// full managed name ("System.Int32"/"System.Double"/"System.Decimal"/...), the
// same string the Type VALUE carries (the simplified Type model: a Type VALUE
// IS its full-name string). A null/absent typeName behaves like the untyped
// overload (net48's Add(name) makes a String column; "" is that marker here).
Il2CppObject* icall_datatable_columns_add_typed(Il2CppObject* self, Il2CppObject* name,
                                                Il2CppObject* typeName);
// Columns.Add(DataColumn): adopt a DataColumn created by the DataColumn ctor.
Il2CppObject* icall_datatable_columns_add_column(Il2CppObject* self, Il2CppObject* column);
int32_t icall_datatable_get_column_count(Il2CppObject* self);
Il2CppObject* icall_datatable_get_column(Il2CppObject* self, int32_t index);
Il2CppObject* icall_datatable_rows_add(Il2CppObject* self, Il2CppArray* values);
int32_t icall_datatable_get_row_count(Il2CppObject* self);
Il2CppObject* icall_datatable_get_row(Il2CppObject* self, int32_t index);

// System.Data.DataColumn -- the ctor (name[, type]) and the DataType property.
void icall_datacolumn_ctor(Il2CppObject* self, Il2CppObject* name);
void icall_datacolumn_ctor_typed(Il2CppObject* self, Il2CppObject* name, Il2CppObject* typeName);
Il2CppObject* icall_datacolumn_get_data_type(Il2CppObject* self);
void icall_datacolumn_set_data_type(Il2CppObject* self, Il2CppObject* typeName);

// ADO stage 2 (batch 153): row removal / clear. rows_remove_at shifts the
// remaining rows left (net48 DataRowCollection.RemoveAt); rows_remove drops
// the row a DataRow wraps; clear empties both columns and rows (net48
// DataTable.Clear keeps the schema -- the minimal surface keeps columns).
void icall_datatable_rows_remove_at(Il2CppObject* self, int32_t index);
void icall_datatable_rows_remove(Il2CppObject* self, Il2CppObject* row);
void icall_datatable_clear(Il2CppObject* self);

// ADO stage 2 (batch 153b): NewRow / Add(DataRow) / ItemArray. new_row
// returns a detached empty row (rowIndex -1); rows_add_row mounts a
// previously created row at the end; get/set_item_array copies the row
// values to/from an object[] (net48 DataRow.ItemArray).
Il2CppObject* icall_datatable_new_row(Il2CppObject* self);
void icall_datatable_rows_add_row(Il2CppObject* self, Il2CppObject* row);
Il2CppObject* icall_datarow_get_item_array(Il2CppObject* self);
void icall_datarow_set_item_array(Il2CppObject* self, Il2CppObject* values);

// ADO stage 2 (batch 153c): DataTable.Select(filter) -- the minimal filter
// grammar: one "column op value" predicate, op in = <> > < >= <=; value is
// a quoted string ('Alice') or a number. Returns a DataRow[] of matches.
Il2CppObject* icall_datatable_select(Il2CppObject* self, Il2CppObject* filter);

// ADO stage 2 (batch 153d): DataSet -- a table collection (object[] of
// DataTable, grows by one-array-pointer-copy like the DataTable rows).
struct Il2CppDataSet
{
    Il2CppObject _obj;      // slot 0: object header
    Il2CppArray* tables;    // slot 1: DataTable[]
    int32_t      count;     // slot 2 low half
};

void   icall_dataset_ctor(Il2CppObject* self);
void   icall_dataset_tables_add(Il2CppObject* self, Il2CppObject* table);
int32_t icall_dataset_get_table_count(Il2CppObject* self);
Il2CppObject* icall_dataset_get_table(Il2CppObject* self, int32_t index);

// ADO stage 3 (batch 154): DataTable.TableName get/set + DataRow.Table.
Il2CppObject* icall_datatable_get_name(Il2CppObject* self);
void icall_datatable_set_name(Il2CppObject* self, Il2CppObject* value);
Il2CppObject* icall_datarow_get_table(Il2CppObject* self);

// ADO stage 3 (batch 154e): DataTable.Compute(expression, filter) -- the
// aggregate family SUM/COUNT/AVG/MIN/MAX over a column, with the optional
// filter using the Select AND grammar. The RESULT TYPE follows the column's
// declared DataType, exactly like net48 (MEASURED -- one process per probe):
//
//   column     MIN / MAX     SUM       AVG                    COUNT
//   ------------------------------------------------------------------
//   int        Int32         Int64     Int32 (INTEGER division) Int32
//   double     Double        Double    Double                  Int32
//   decimal    Decimal       Decimal   Decimal                 Int32
//
// An empty (or all-DBNull) set yields DBNull for MIN/MAX/SUM/AVG and Int32 0
// for COUNT. DBNull cells are SKIPPED by every aggregate. An untyped column
// behaves like net48's String column: MIN/MAX answer the string, SUM/AVG throw
// System.Data.DataException, COUNT counts rows.
Il2CppObject* icall_datatable_compute(Il2CppObject* self, Il2CppObject* expression, Il2CppObject* filter);

// ADO stage 3 (batch 154f): DataTable.Select(filter, sort) -- the single
// sort column form "col [ASC|DESC]" (ASC default) applied to the Select
// matches; returns the DataRow[] in sorted order.
Il2CppObject* icall_datatable_select_sort(Il2CppObject* self, Il2CppObject* filter, Il2CppObject* sort);

// ADO stage 3 (batch 154g): DataTable.Clone() -- a new table with the same
// column schema and no rows.
Il2CppObject* icall_datatable_clone(Il2CppObject* self);

// System.Data.DataRow
Il2CppObject* icall_datarow_get_item(Il2CppObject* self, int32_t col);
void icall_datarow_set_item(Il2CppObject* self, int32_t col, Il2CppObject* value);

// System.Data.DataColumn
Il2CppObject* icall_datacolumn_get_name(Il2CppObject* self);

// Lazy descriptor accessors (newobj/isinst targets for the transpiler).
Il2CppClass* il2cpp_datatable_class();
Il2CppClass* il2cpp_datacolumn_class();
Il2CppClass* il2cpp_datarow_class();
Il2CppClass* il2cpp_dataset_class();

//------------------------------------------------------------------------------
// System.DBNull -- the ADO sentinel value.
//
// net48 exposes ONE process-wide instance (`DBNull.Value`), a reference type
// whose only state is the type itself, so this runtime keeps a single
// descriptor + a single shared instance. The transpiler reads the BCL static
// field `DBNull.Value` as a call to il2cpp_datatable_dbnull_value() (the
// simplified-Type style: there is no BCL static storage to reference), so the
// descriptor itself stays file-local in datatable.cpp. Aggregates skip DBNull
// cells and answer DBNull for an empty/all-DBNull set, so identity here must
// be STABLE.
//------------------------------------------------------------------------------
// The single shared System.DBNull instance (registered as a GC root; never
// collected). Non-null after the first call.
Il2CppObject* il2cpp_datatable_dbnull_value();
bool il2cpp_datatable_is_dbnull(const Il2CppObject* obj);

} // namespace runtime
} // namespace il2cpp
