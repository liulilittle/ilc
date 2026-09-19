//------------------------------------------------------------------------------
// datatable.cpp -- System.Data minimal in-memory surface (batch 152f, ADO
// stage 1): DataTable / DataColumn / DataRow object model (see
// il2cpp_datatable.h for the layouts and GC contract).
//
// The column store is a string[] of names; the row store is an object[][]
// (each row is an object[] of boxed values). Rows and columns grow by ONE
// new array + pure pointer copies (no allocation between birth and
// publication, the listi4 discipline) -- rooted re-reads after the growth
// allocation (the GcRoot pattern, stackqueue.cpp).
//------------------------------------------------------------------------------

#include "il2cpp_datatable.h"

#include "il2cpp_boxing_ext.h"
#include "il2cpp_exception_ext.h"
#include "il2cpp_gcroot.h"
#include "il2cpp_runtime.h"
#include "il2cpp_string.h"

#include <cstring>

namespace il2cpp {
namespace runtime {

namespace {

//------------------------------------------------------------------------------
// Class descriptors (GLOBAL scope -- the transpiler emits
// &System_Data_DataTable_class for newobj/isinst, the stackqueue contract).
//   DataTable: columns (slot 1) and rows (slot 2) are GC refs.
//   DataColumn: name (slot 1) is a GC ref.
//   DataRow: table (slot 1) is a GC ref.
//------------------------------------------------------------------------------
Il2CppClass System_Data_DataTable_class = {
    "System.Data.DataTable",                 // name
    static_cast<int32_t>(sizeof(Il2CppDataTable)), // instanceSize
    0b01110u,                                // refFieldBitmap: bits 1+2+4
    3,                                       // refFieldCount
    nullptr,                                 // baseClass
    nullptr,                                 // vtable
    0,                                       // vtableSize
    nullptr,                                 // interfaces
    0,                                       // interfaceCount
};

Il2CppClass System_Data_DataColumn_class = {
    "System.Data.DataColumn",                // name
    static_cast<int32_t>(sizeof(Il2CppDataColumn)), // instanceSize
    0b00010u,                                // refFieldBitmap: bit 1
    1,                                       // refFieldCount
    nullptr,                                 // baseClass
    nullptr,                                 // vtable
    0,                                       // vtableSize
    nullptr,                                 // interfaces
    0,                                       // interfaceCount
};

Il2CppClass System_Data_DataRow_class = {
    "System.Data.DataRow",                   // name
    static_cast<int32_t>(sizeof(Il2CppDataRow)), // instanceSize
    0b01010u,                                // refFieldBitmap: bits 1+3
    2,                                       // refFieldCount
    nullptr,                                 // baseClass
    nullptr,                                 // vtable
    0,                                       // vtableSize
    nullptr,                                 // interfaces
    0,                                       // interfaceCount
};

// ADO stage 2 (batch 153d): DataSet -- the table collection.
Il2CppClass System_Data_DataSet_class = {
    "System.Data.DataSet",                   // name
    static_cast<int32_t>(sizeof(Il2CppDataSet)), // instanceSize
    0b00010u,                                // refFieldBitmap: bit 1
    1,                                       // refFieldCount
    nullptr,                                 // baseClass
    nullptr,                                 // vtable
    0,                                       // vtableSize
    nullptr,                                 // interfaces
    0,                                       // interfaceCount
};

bool g_tableRegistered = false;
bool g_columnRegistered = false;
bool g_rowRegistered = false;
bool g_datasetRegistered = false;

void EnsureTableRegistered()
{
    if (g_tableRegistered) return;
    il2cpp_class_register(&System_Data_DataTable_class);
    g_tableRegistered = true;
}

void EnsureColumnRegistered()
{
    if (g_columnRegistered) return;
    il2cpp_class_register(&System_Data_DataColumn_class);
    g_columnRegistered = true;
}

void EnsureDataSetRegistered()
{
    if (g_datasetRegistered) return;
    il2cpp_class_register(&System_Data_DataSet_class);
    g_datasetRegistered = true;
}

void EnsureRowRegistered()
{
    if (g_rowRegistered) return;
    il2cpp_class_register(&System_Data_DataRow_class);
    g_rowRegistered = true;
}

Il2CppDataTable* AsTable(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppDataTable*>(self);
}

Il2CppDataColumn* AsColumn(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppDataColumn*>(self);
}

Il2CppDataRow* AsRow(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppDataRow*>(self);
}

Il2CppDataSet* AsDataSet(Il2CppObject* self)
{
    return reinterpret_cast<Il2CppDataSet*>(self);
}

void ThrowIfNullReceiver(Il2CppObject* self)
{
    if (self == nullptr)
    {
        il2cpp_throw_null_reference_exception(nullptr);
    }
}

// A reference array of 'count' object slots.
Il2CppArray* AllocRefArray(int32_t count)
{
    return il2cpp_array_new(nullptr, static_cast<int32_t>(sizeof(void*)), true, count);
}

} // namespace

//------------------------------------------------------------------------------
// il2cpp_datatable_class / il2cpp_datacolumn_class / il2cpp_datarow_class --
// the lazy descriptor accessors (newobj/isinst targets).
//------------------------------------------------------------------------------
Il2CppClass* il2cpp_datatable_class()
{
    EnsureTableRegistered();
    return &System_Data_DataTable_class;
}

Il2CppClass* il2cpp_datacolumn_class()
{
    EnsureColumnRegistered();
    return &System_Data_DataColumn_class;
}

Il2CppClass* il2cpp_datarow_class()
{
    EnsureRowRegistered();
    return &System_Data_DataRow_class;
}

Il2CppClass* il2cpp_dataset_class()
{
    EnsureDataSetRegistered();
    return &System_Data_DataSet_class;
}

//------------------------------------------------------------------------------
// icall_datatable_ctor -- DataTable(): an empty column list and row store.
//------------------------------------------------------------------------------
void icall_datatable_ctor(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    EnsureTableRegistered();
    Il2CppDataTable* t = AsTable(self);
    GcRoot<Il2CppDataTable*> root(t, "icall_datatable_ctor");
    Il2CppArray* cols = AllocRefArray(0);
    Il2CppArray* rows = AllocRefArray(0);
    t = root.Get();
    t->columns = cols;
    il2cpp_write_barrier(reinterpret_cast<void**>(&t->columns),
                         reinterpret_cast<Il2CppObject*>(t->columns));
    t->rows = rows;
    il2cpp_write_barrier(reinterpret_cast<void**>(&t->rows),
                         reinterpret_cast<Il2CppObject*>(t->rows));
    t->colCount = 0;
    t->rowCount = 0;
}

//------------------------------------------------------------------------------
// icall_datatable_columns_add -- Columns.Add(string name): append the column
// name and return a DataColumn object (the caller usually discards it).
//------------------------------------------------------------------------------
Il2CppObject* icall_datatable_columns_add(Il2CppObject* self, Il2CppObject* name)
{
    ThrowIfNullReceiver(self);
    EnsureColumnRegistered();
    GcRoot<Il2CppObject*> root(self, "icall_datatable_columns_add");
    Il2CppDataTable* t = AsTable(self);
    const int32_t oldCount = t->colCount;
    Il2CppArray* newCols = AllocRefArray(oldCount + 1);
    Il2CppArray* newRows = AllocRefArray(t->rowCount); // same row count (no-op grow keeps shape)
    self = root.Get();
    t = AsTable(self);
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(newCols->data);
    Il2CppObject* const* src = reinterpret_cast<Il2CppObject* const*>(t->columns->data);
    for (int32_t i = 0; i < oldCount; ++i)
    {
        dst[i] = src[i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
    }
    dst[oldCount] = name;
    il2cpp_write_barrier(reinterpret_cast<void**>(&dst[oldCount]), name);
    t->columns = newCols;
    il2cpp_write_barrier(reinterpret_cast<void**>(&t->columns),
                         reinterpret_cast<Il2CppObject*>(t->columns));
    t->colCount = oldCount + 1;
    // The row store is rebuilt at the same length (a fresh array each Add so
    // the shape stays uniform; no value changes).
    t->rows = newRows;
    il2cpp_write_barrier(reinterpret_cast<void**>(&t->rows),
                         reinterpret_cast<Il2CppObject*>(t->rows));
    // The DataColumn object.
    GcRoot<Il2CppObject*> rootSelf(self, "icall_datatable_columns_add_col");
    Il2CppDataColumn* col = reinterpret_cast<Il2CppDataColumn*>(
        il2cpp_object_new(&System_Data_DataColumn_class));
    rootSelf.Get();
    col->name = reinterpret_cast<Il2CppString*>(name);
    il2cpp_write_barrier(reinterpret_cast<void**>(&col->name),
                         reinterpret_cast<Il2CppObject*>(col->name));
    return reinterpret_cast<Il2CppObject*>(col);
}

//------------------------------------------------------------------------------
// icall_datatable_get_column_count / icall_datatable_get_row_count.
//------------------------------------------------------------------------------
int32_t icall_datatable_get_column_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsTable(self)->colCount;
}

int32_t icall_datatable_get_row_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsTable(self)->rowCount;
}

//------------------------------------------------------------------------------
// icall_datatable_get_column -- Columns[index]: the DataColumn object.
//------------------------------------------------------------------------------
Il2CppObject* icall_datatable_get_column(Il2CppObject* self, int32_t index)
{
    ThrowIfNullReceiver(self);
    EnsureColumnRegistered();
    Il2CppDataTable* t = AsTable(self);
    if (index < 0 || index >= t->colCount)
    {
        il2cpp_throw_argument_out_of_range_exception("index", nullptr);
    }
    GcRoot<Il2CppDataTable*> root(t, "icall_datatable_get_column");
    Il2CppObject* nameObj = reinterpret_cast<Il2CppObject* const*>(t->columns->data)[index];
    t = root.Get();
    Il2CppDataColumn* col = reinterpret_cast<Il2CppDataColumn*>(
        il2cpp_object_new(&System_Data_DataColumn_class));
    col->name = reinterpret_cast<Il2CppString*>(nameObj);
    il2cpp_write_barrier(reinterpret_cast<void**>(&col->name),
                         reinterpret_cast<Il2CppObject*>(col->name));
    return reinterpret_cast<Il2CppObject*>(col);
}

//------------------------------------------------------------------------------
// icall_datatable_rows_add -- Rows.Add(object[] values): append the row (the
// values array is adopted, not copied) and return a DataRow for it.
//------------------------------------------------------------------------------
Il2CppObject* icall_datatable_rows_add(Il2CppObject* self, Il2CppArray* values)
{
    ThrowIfNullReceiver(self);
    if (values == nullptr)
    {
        il2cpp_throw_argument_null_exception("values");
    }
    EnsureRowRegistered();
    GcRoot<Il2CppObject*> root(self, "icall_datatable_rows_add");
    Il2CppDataTable* t = AsTable(self);
    const int32_t oldCount = t->rowCount;
    Il2CppArray* newRows = AllocRefArray(oldCount + 1);
    self = root.Get();
    t = AsTable(self);
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(newRows->data);
    Il2CppObject* const* src = reinterpret_cast<Il2CppObject* const*>(t->rows->data);
    for (int32_t i = 0; i < oldCount; ++i)
    {
        dst[i] = src[i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
    }
    dst[oldCount] = reinterpret_cast<Il2CppObject*>(values);
    il2cpp_write_barrier(reinterpret_cast<void**>(&dst[oldCount]),
                         reinterpret_cast<Il2CppObject*>(values));
    t->rows = newRows;
    il2cpp_write_barrier(reinterpret_cast<void**>(&t->rows),
                         reinterpret_cast<Il2CppObject*>(t->rows));
    t->rowCount = oldCount + 1;
    // The DataRow for the appended row.
    GcRoot<Il2CppObject*> rootSelf(self, "icall_datatable_rows_add_row");
    Il2CppDataRow* row = reinterpret_cast<Il2CppDataRow*>(
        il2cpp_object_new(&System_Data_DataRow_class));
    rootSelf.Get();
    row->table = self;
    il2cpp_write_barrier(reinterpret_cast<void**>(&row->table),
                         reinterpret_cast<Il2CppObject*>(row->table));
    row->rowIndex = oldCount;
    row->values = values;
    il2cpp_write_barrier(reinterpret_cast<void**>(&row->values),
                         reinterpret_cast<Il2CppObject*>(row->values));
    return reinterpret_cast<Il2CppObject*>(row);
}

//------------------------------------------------------------------------------
// icall_datatable_get_row -- Rows[index]: a DataRow wrapper for that row.
//------------------------------------------------------------------------------
Il2CppObject* icall_datatable_get_row(Il2CppObject* self, int32_t index)
{
    ThrowIfNullReceiver(self);
    EnsureRowRegistered();
    Il2CppDataTable* t = AsTable(self);
    if (index < 0 || index >= t->rowCount)
    {
        il2cpp_throw_argument_out_of_range_exception("index", nullptr);
    }
    GcRoot<Il2CppDataTable*> root(t, "icall_datatable_get_row");
    t = root.Get();
    Il2CppDataRow* row = reinterpret_cast<Il2CppDataRow*>(
        il2cpp_object_new(&System_Data_DataRow_class));
    row->table = reinterpret_cast<Il2CppObject*>(t);
    il2cpp_write_barrier(reinterpret_cast<void**>(&row->table),
                         reinterpret_cast<Il2CppObject*>(row->table));
    row->rowIndex = index;
    row->values = reinterpret_cast<Il2CppArray*>(t->rows->data[index]);
    il2cpp_write_barrier(reinterpret_cast<void**>(&row->values),
                         reinterpret_cast<Il2CppObject*>(row->values));
    return reinterpret_cast<Il2CppObject*>(row);
}

//------------------------------------------------------------------------------
// icall_datarow_get_item -- DataRow[indexer]: the boxed value at the column.
// A detached NewRow reads its own values slot (net48 allows pre-fill).
//------------------------------------------------------------------------------
Il2CppObject* icall_datarow_get_item(Il2CppObject* self, int32_t col)
{
    ThrowIfNullReceiver(self);
    Il2CppDataRow* r = AsRow(self);
    if (r->table == nullptr)
    {
        il2cpp_throw_invalid_operation_exception("The row is detached.");
    }
    Il2CppDataTable* t = AsTable(r->table);
    if (col < 0 || col >= t->colCount)
    {
        il2cpp_throw_argument_out_of_range_exception("column", nullptr);
    }
    Il2CppObject* const* values;
    if (r->rowIndex >= 0)
    {
        if (r->rowIndex >= t->rowCount)
        {
            il2cpp_throw_argument_out_of_range_exception("row", nullptr);
        }
        Il2CppObject* const* rows = reinterpret_cast<Il2CppObject* const*>(t->rows->data);
        values = reinterpret_cast<Il2CppObject* const*>(
            reinterpret_cast<Il2CppArray*>(rows[r->rowIndex])->data);
    }
    else
    {
        values = reinterpret_cast<Il2CppObject* const*>(r->values->data);
    }
    return values[col];
}

//------------------------------------------------------------------------------
// icall_datarow_set_item -- DataRow[indexer] = value (detached NewRow
// pre-fills its own values slot; net48 semantics).
//------------------------------------------------------------------------------
void icall_datarow_set_item(Il2CppObject* self, int32_t col, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    Il2CppDataRow* r = AsRow(self);
    if (r->table == nullptr)
    {
        il2cpp_throw_invalid_operation_exception("The row is detached.");
    }
    Il2CppDataTable* t = AsTable(r->table);
    if (col < 0 || col >= t->colCount)
    {
        il2cpp_throw_argument_out_of_range_exception("column", nullptr);
    }
    Il2CppObject** values;
    if (r->rowIndex >= 0)
    {
        if (r->rowIndex >= t->rowCount)
        {
            il2cpp_throw_argument_out_of_range_exception("row", nullptr);
        }
        Il2CppObject** rows = reinterpret_cast<Il2CppObject**>(t->rows->data);
        values = reinterpret_cast<Il2CppObject**>(
            reinterpret_cast<Il2CppArray*>(rows[r->rowIndex])->data);
    }
    else
    {
        values = reinterpret_cast<Il2CppObject**>(r->values->data);
    }
    values[col] = value;
    il2cpp_write_barrier(reinterpret_cast<void**>(&values[col]), value);
}

//------------------------------------------------------------------------------
// ADO stage 2 (batch 153): row removal / clear.
//------------------------------------------------------------------------------

// Remove the row at 'index': shift the remaining rows left (net48
// DataRowCollection.RemoveAt semantics).
void icall_datatable_rows_remove_at(Il2CppObject* self, int32_t index)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> root(self, "icall_datatable_rows_remove_at");
    Il2CppDataTable* t = AsTable(self);
    if (index < 0 || index >= t->rowCount)
    {
        il2cpp_throw_argument_out_of_range_exception("index", nullptr);
    }
    const int32_t oldCount = t->rowCount;
    Il2CppArray* newRows = AllocRefArray(oldCount - 1);
    self = root.Get();
    t = AsTable(self);
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(newRows->data);
    Il2CppObject* const* src = reinterpret_cast<Il2CppObject* const*>(t->rows->data);
    for (int32_t i = 0, j = 0; i < oldCount; ++i)
    {
        if (i == index) continue;
        dst[j++] = src[i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[j - 1]), dst[j - 1]);
    }
    t->rows = newRows;
    il2cpp_write_barrier(reinterpret_cast<void**>(&t->rows),
                         reinterpret_cast<Il2CppObject*>(t->rows));
    t->rowCount = oldCount - 1;
}

// Remove the row a DataRow wraps (net48 DataRowCollection.Remove(DataRow)).
void icall_datatable_rows_remove(Il2CppObject* self, Il2CppObject* row)
{
    ThrowIfNullReceiver(self);
    if (row == nullptr)
    {
        il2cpp_throw_argument_null_exception("row");
    }
    Il2CppDataRow* r = AsRow(row);
    if (r->table != self)
    {
        il2cpp_throw_invalid_operation_exception("The row does not belong to this table.");
    }
    icall_datatable_rows_remove_at(self, r->rowIndex);
}

// Clear all rows (net48 DataTable.Clear keeps the schema -- the minimal
// surface keeps the columns).
void icall_datatable_clear(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> root(self, "icall_datatable_clear");
    Il2CppDataTable* t = AsTable(self);
    Il2CppArray* newRows = AllocRefArray(0);
    self = root.Get();
    t = AsTable(self);
    t->rows = newRows;
    il2cpp_write_barrier(reinterpret_cast<void**>(&t->rows),
                         reinterpret_cast<Il2CppObject*>(t->rows));
    t->rowCount = 0;
}

//------------------------------------------------------------------------------
// ADO stage 2 (batch 153b): NewRow / Add(DataRow) / ItemArray.
//------------------------------------------------------------------------------

// Create a detached empty row (net48 DataTable.NewRow). The row carries
// the table back-reference but no slot yet (rowIndex -1).
Il2CppObject* icall_datatable_new_row(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> root(self, "icall_datatable_new_row");
    const int32_t cols = AsTable(self)->colCount;
    Il2CppObject* rowObj = il2cpp_object_new(&System_Data_DataRow_class);
    self = root.Get();
    Il2CppDataRow* row = AsRow(rowObj);
    row->table = self;
    il2cpp_write_barrier(reinterpret_cast<void**>(&row->table),
                         reinterpret_cast<Il2CppObject*>(row->table));
    row->rowIndex = -1;
    row->values = AllocRefArray(cols);
    return rowObj;
}

// Mount a previously created row at the end of the rows collection (net48
// DataRowCollection.Add(DataRow)). A row already belonging to the table
// rejects the mount. The row's own values array becomes the collection
// entry (the NewRow pre-fill travels with it).
void icall_datatable_rows_add_row(Il2CppObject* self, Il2CppObject* row)
{
    ThrowIfNullReceiver(self);
    if (row == nullptr)
    {
        il2cpp_throw_argument_null_exception("row");
    }
    Il2CppDataRow* r = AsRow(row);
    if (r->table == self && r->rowIndex >= 0)
    {
        il2cpp_throw_invalid_operation_exception("The row already belongs to this table.");
    }
    if (r->table != nullptr && r->table != self)
    {
        il2cpp_throw_invalid_operation_exception("The row belongs to another table.");
    }
    GcRoot<Il2CppObject*> root(self, "icall_datatable_rows_add_row");
    Il2CppDataTable* t = AsTable(self);
    const int32_t newCount = t->rowCount + 1;
    Il2CppArray* newRows = AllocRefArray(newCount);
    self = root.Get();
    t = AsTable(self);
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(newRows->data);
    Il2CppObject* const* src = reinterpret_cast<Il2CppObject* const*>(t->rows->data);
    for (int32_t i = 0; i < t->rowCount; ++i)
    {
        dst[i] = src[i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
    }
    dst[t->rowCount] = reinterpret_cast<Il2CppObject*>(AsRow(row)->values);
    il2cpp_write_barrier(reinterpret_cast<void**>(&dst[t->rowCount]),
                         reinterpret_cast<Il2CppObject*>(dst[t->rowCount]));
    t->rows = newRows;
    il2cpp_write_barrier(reinterpret_cast<void**>(&t->rows),
                         reinterpret_cast<Il2CppObject*>(t->rows));
    t->rowCount = newCount;
    r = AsRow(row);
    r->rowIndex = newCount - 1;
}

// Copy the row values out as a fresh object[] (net48 DataRow.ItemArray).
Il2CppObject* icall_datarow_get_item_array(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> root(self, "icall_datarow_get_item_array");
    Il2CppDataRow* r = AsRow(self);
    if (r->table == nullptr)
    {
        il2cpp_throw_invalid_operation_exception("The row is detached.");
    }
    const int32_t cols = AsTable(r->table)->colCount;
    Il2CppObject* const* srcValues;
    if (r->rowIndex >= 0)
    {
        if (r->rowIndex >= AsTable(r->table)->rowCount)
        {
            il2cpp_throw_invalid_operation_exception("The row is not attached to a table.");
        }
        Il2CppObject* const* rows = reinterpret_cast<Il2CppObject* const*>(
            AsTable(r->table)->rows->data);
        srcValues = reinterpret_cast<Il2CppObject* const*>(
            reinterpret_cast<Il2CppArray*>(rows[r->rowIndex])->data);
    }
    else
    {
        srcValues = reinterpret_cast<Il2CppObject* const*>(r->values->data);
    }
    Il2CppArray* out = AllocRefArray(cols);
    self = root.Get();
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(out->data);
    for (int32_t i = 0; i < cols; ++i)
    {
        dst[i] = srcValues[i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
    }
    return reinterpret_cast<Il2CppObject*>(out);
}

// Replace all row values from an object[] (net48 DataRow.ItemArray setter).
void icall_datarow_set_item_array(Il2CppObject* self, Il2CppObject* values)
{
    ThrowIfNullReceiver(self);
    if (values == nullptr)
    {
        il2cpp_throw_argument_null_exception("values");
    }
    GcRoot<Il2CppObject*> root(self, "icall_datarow_set_item_array");
    const Il2CppArray* src = reinterpret_cast<const Il2CppArray*>(values);
    Il2CppObject** dstValues;
    if (AsRow(self)->rowIndex >= 0)
    {
        if (src->length != AsTable(AsRow(self)->table)->colCount)
        {
            il2cpp_throw_argument_exception("The item array length does not match the column count.");
        }
        Il2CppObject** rows = reinterpret_cast<Il2CppObject**>(
            AsTable(AsRow(self)->table)->rows->data);
        dstValues = reinterpret_cast<Il2CppObject**>(
            reinterpret_cast<Il2CppArray*>(rows[AsRow(self)->rowIndex])->data);
    }
    else
    {
        if (src->length != AsTable(AsRow(self)->table)->colCount)
        {
            il2cpp_throw_argument_exception("The item array length does not match the column count.");
        }
        dstValues = reinterpret_cast<Il2CppObject**>(AsRow(self)->values->data);
    }
    const int32_t cols = src->length;
    for (int32_t i = 0; i < cols; ++i)
    {
        Il2CppObject* v = reinterpret_cast<Il2CppObject* const*>(src->data)[i];
        self = root.Get();
        dstValues[i] = v;
        il2cpp_write_barrier(reinterpret_cast<void**>(&dstValues[i]), dstValues[i]);
    }
}

//------------------------------------------------------------------------------
// ADO stage 2 (batch 153c): DataTable.Select(filter).
//------------------------------------------------------------------------------
// The grammar: "col op value [AND col op value]" -- op in {=, <>, >, <, >=,
// <=}; value is a quoted string ('Alice') or a number. Numeric columns
// compare numerically; string columns compare ordinally (UTF-16
// character-wise). The AND join is case-insensitive and must sit outside
// quotes. Rows where a null column value is tested never match.
namespace {
struct SelectCond
{
    int32_t colIdx;
    char16_t op0;
    char16_t op1;
    int32_t opLen;
    int32_t valStart;
    int32_t valEnd;
    bool quoted;
    double rhs;

    // Parse "column op value" from f[segStart..segEnd). Returns false on an
    // unsupported shape (missing op, unknown column, bad number).
    bool Parse(const char16_t* f, int32_t segStart, int32_t segEnd, const Il2CppDataTable* t)
    {
        int32_t opPos = -1;
        opLen = 0;
        op0 = 0;
        op1 = 0;
        for (int32_t i = segStart; i < segEnd; ++i)
        {
            const char16_t ch = f[i];
            if (ch == u'=' || ch == u'<' || ch == u'>')
            {
                opPos = i;
                op0 = ch;
                opLen = 1;
                if (i + 1 < segEnd)
                {
                    const char16_t nxt = f[i + 1];
                    if (nxt == u'=')
                    {
                        opLen = 2;
                        op1 = nxt;
                    }
                    else if (op0 == u'<' && nxt == u'>')
                    {
                        opLen = 2;
                        op1 = nxt;
                    }
                }
                break;
            }
        }
        if (opPos < 0) return false;
        int32_t colStart = segStart;
        int32_t colEnd = opPos;
        while (colStart < colEnd && (f[colStart] == u' ' || f[colStart] == u'\t')) ++colStart;
        while (colEnd > colStart && (f[colEnd - 1] == u' ' || f[colEnd - 1] == u'\t')) --colEnd;
        valStart = opPos + opLen;
        valEnd = segEnd;
        while (valStart < valEnd && (f[valStart] == u' ' || f[valStart] == u'\t')) ++valStart;
        while (valEnd > valStart && (f[valEnd - 1] == u' ' || f[valEnd - 1] == u'\t')) --valEnd;
        quoted = valStart < valEnd && f[valStart] == u'\'' && f[valEnd - 1] == u'\'';
        if (quoted)
        {
            ++valStart;
            --valEnd;
        }
        colIdx = -1;
        if (t->colCount > 0)
        {
            const Il2CppObject* const* cols = reinterpret_cast<const Il2CppObject* const*>(t->columns->data);
            for (int32_t i = 0; i < t->colCount; ++i)
            {
                const Il2CppString* cn = reinterpret_cast<const Il2CppString*>(cols[i]);
                if (cn->length == colEnd - colStart
                    && memcmp(cn->chars, f + colStart,
                              static_cast<size_t>(colEnd - colStart) * sizeof(char16_t)) == 0)
                {
                    colIdx = i;
                    break;
                }
            }
        }
        if (colIdx < 0) return false;
        rhs = 0.0;
        if (!quoted)
        {
            double fracScale = 0.1;
            bool inFrac = false;
            bool neg = false;
            int32_t vi = valStart;
            if (vi < valEnd && f[vi] == u'-')
            {
                neg = true;
                ++vi;
            }
            for (; vi < valEnd; ++vi)
            {
                const char16_t c = f[vi];
                if (c >= u'0' && c <= u'9')
                {
                    if (!inFrac)
                    {
                        rhs = rhs * 10.0 + static_cast<double>(c - u'0');
                    }
                    else
                    {
                        rhs += static_cast<double>(c - u'0') * fracScale;
                        fracScale *= 0.1;
                    }
                }
                else if (c == u'.' && !inFrac)
                {
                    inFrac = true;
                }
                else
                {
                    return false;
                }
            }
            if (neg) rhs = -rhs;
        }
        return true;
    }

    bool Matches(const Il2CppObject* cell, const char16_t* f) const
    {
        if (cell == nullptr) return false;
        if (quoted)
        {
            if (cell->klass != &System_String_class) return false;
            const Il2CppString* cs = reinterpret_cast<const Il2CppString*>(cell);
            const int cmp = cs->length == valEnd - valStart
                ? memcmp(cs->chars, f + valStart,
                         static_cast<size_t>(valEnd - valStart) * sizeof(char16_t))
                : -1;
            return (opLen == 1 && op0 == u'=' && cmp == 0)
                || (opLen == 2 && op0 == u'<' && op1 == u'>' && cmp != 0);
        }
        double lhs;
        if (cell->klass == &System_Int32_class)
        {
            lhs = static_cast<double>(icall_unbox_int32(const_cast<Il2CppObject*>(cell)));
        }
        else if (cell->klass == &System_Double_class)
        {
            lhs = icall_unbox_double(const_cast<Il2CppObject*>(cell));
        }
        else
        {
            lhs = 0.0;
        }
        if (opLen == 1 && op0 == u'=') return lhs == rhs;
        if (opLen == 2 && op0 == u'<' && op1 == u'>') return lhs != rhs;
        if (opLen == 1 && op0 == u'>') return lhs > rhs;
        if (opLen == 1 && op0 == u'<') return lhs < rhs;
        if (opLen == 2 && op0 == u'>' && op1 == u'=') return lhs >= rhs;
        if (opLen == 2 && op0 == u'<' && op1 == u'=') return lhs <= rhs;
        return false;
    }
};

// Find the AND separator outside single quotes; returns the index of the
// 'A' or -1. Case-insensitive; requires space/tab word boundaries.
int32_t FindAndSeparator(const char16_t* f, int32_t flen)
{
    bool inQuote = false;
    for (int32_t i = 0; i + 2 < flen; ++i)
    {
        const char16_t c = f[i];
        if (c == u'\'')
        {
            inQuote = !inQuote;
            continue;
        }
        if (inQuote) continue;
        if ((c == u'a' || c == u'A')
            && (f[i + 1] == u'n' || f[i + 1] == u'N')
            && (f[i + 2] == u'd' || f[i + 2] == u'D'))
        {
            const bool beforeOk = i == 0 || f[i - 1] == u' ' || f[i - 1] == u'\t';
            const bool afterOk = i + 3 >= flen || f[i + 3] == u' ' || f[i + 3] == u'\t';
            if (beforeOk && afterOk) return i;
        }
    }
    return -1;
}
} // namespace

Il2CppObject* icall_datatable_select(Il2CppObject* self, Il2CppObject* filter)
{
    ThrowIfNullReceiver(self);
    if (filter == nullptr)
    {
        il2cpp_throw_argument_null_exception("filter");
    }
    const Il2CppString* fs = reinterpret_cast<const Il2CppString*>(filter);
    const char16_t* f = fs->chars;
    const int32_t flen = fs->length;
    GcRoot<Il2CppObject*> root(self, "icall_datatable_select");
    const Il2CppDataTable* t = AsTable(self);
    // Split on AND (outside quotes): the first condition is [0, andPos),
    // the optional second is [andPos + 3, flen).
    const int32_t andPos = FindAndSeparator(f, flen);
    const int32_t seg1End = andPos < 0 ? flen : andPos;
    SelectCond c1{};
    const bool hasC1 = flen > 0; // an empty filter matches every row (net48)
    if (hasC1 && !c1.Parse(f, 0, seg1End, t))
    {
        il2cpp_throw_argument_exception("Unsupported filter expression.");
    }
    SelectCond c2{};
    const bool hasC2 = andPos >= 0;
    if (hasC2 && !c2.Parse(f, andPos + 3, flen, t))
    {
        il2cpp_throw_argument_exception("Unsupported filter expression.");
    }
    self = root.Get();
    t = AsTable(self);
    // Collect the matching row indices (plain ints -- no GC hazard).
    const int32_t rowCount = t->rowCount;
    Il2CppObject* const* rows = reinterpret_cast<Il2CppObject* const*>(t->rows->data);
    Il2CppArray* out = AllocRefArray(rowCount);
    int32_t matchCount = 0;
    for (int32_t ri = 0; ri < rowCount; ++ri)
    {
        const Il2CppObject* const* values = reinterpret_cast<const Il2CppObject* const*>(
            reinterpret_cast<const Il2CppArray*>(rows[ri])->data);
        bool match = !hasC1 || c1.Matches(values[c1.colIdx], f);
        if (match && hasC2)
        {
            match = c2.Matches(values[c2.colIdx], f);
        }
        if (match)
        {
            reinterpret_cast<Il2CppObject**>(out->data)[matchCount] =
                reinterpret_cast<Il2CppObject*>(rows[ri]);
            ++matchCount;
        }
    }
    // Rebuild as DataRow wrappers bound to the TABLE row (rowIndex = the
    // table slot, so DataRow[int] routes to the same values the wrapper
    // carries).
    GcRoot<Il2CppArray*> rootOut(out, "icall_datatable_select_out");
    Il2CppArray* wrappers = AllocRefArray(matchCount);
    for (int32_t i = 0; i < matchCount; ++i)
    {
        Il2CppObject* const* srcRows = reinterpret_cast<Il2CppObject* const*>(rootOut.Get()->data);
        // Find the table slot of the matched value array (the wrappers must
        // bind rowIndex to the live table row).
        int32_t slot = -1;
        const Il2CppObject* const* trows = reinterpret_cast<const Il2CppObject* const*>(
            AsTable(self)->rows->data);
        Il2CppObject* matched = srcRows[i];
        for (int32_t s = 0; s < AsTable(self)->rowCount; ++s)
        {
            if (trows[s] == matched)
            {
                slot = s;
                break;
            }
        }
        Il2CppDataRow* w = reinterpret_cast<Il2CppDataRow*>(
            il2cpp_object_new(&System_Data_DataRow_class));
        w->table = self;
        il2cpp_write_barrier(reinterpret_cast<void**>(&w->table),
                             reinterpret_cast<Il2CppObject*>(w->table));
        w->rowIndex = slot;
        w->values = reinterpret_cast<Il2CppArray*>(matched);
        il2cpp_write_barrier(reinterpret_cast<void**>(&w->values),
                             reinterpret_cast<Il2CppObject*>(w->values));
        reinterpret_cast<Il2CppObject**>(wrappers->data)[i] =
            reinterpret_cast<Il2CppObject*>(w);
        il2cpp_write_barrier(reinterpret_cast<void**>(&reinterpret_cast<Il2CppObject**>(wrappers->data)[i]),
            reinterpret_cast<Il2CppObject*>(w));
    }
    return reinterpret_cast<Il2CppObject*>(wrappers);
}

//------------------------------------------------------------------------------
// ADO stage 3 (batch 154f): DataTable.Select(filter, sort).
//------------------------------------------------------------------------------
// The single sort column form "col [ASC|DESC]" (ASC default) applied to the
// Select matches: the DataRow wrappers from icall_datatable_select are
// insertion-sorted in place by the sort column's cell values (numeric
// before string, UTF-16 ordinal for strings, nulls last). The sort loop
// allocates nothing, so no collection can move the wrapper array.
namespace {
bool CellNumberValue(const Il2CppObject* cell, double& out)
{
    if (cell == nullptr) return false;
    if (cell->klass == &System_Int32_class)
    {
        out = static_cast<double>(icall_unbox_int32(const_cast<Il2CppObject*>(cell)));
        return true;
    }
    if (cell->klass == &System_Double_class)
    {
        out = icall_unbox_double(const_cast<Il2CppObject*>(cell));
        return true;
    }
    return false;
}

int32_t CompareSortCells(const Il2CppObject* a, const Il2CppObject* b)
{
    double na = 0.0;
    double nb = 0.0;
    const bool aNum = CellNumberValue(a, na);
    const bool bNum = CellNumberValue(b, nb);
    if (aNum && bNum)
    {
        return na < nb ? -1 : (na > nb ? 1 : 0);
    }
    if (aNum != bNum)
    {
        return aNum ? -1 : 1; // numeric before string
    }
    if (a == nullptr && b == nullptr) return 0;
    if (a == nullptr) return -1;
    if (b == nullptr) return 1;
    const Il2CppString* sa = reinterpret_cast<const Il2CppString*>(a);
    const Il2CppString* sb = reinterpret_cast<const Il2CppString*>(b);
    const int32_t len = sa->length < sb->length ? sa->length : sb->length;
    for (int32_t i = 0; i < len; ++i)
    {
        if (sa->chars[i] != sb->chars[i])
        {
            return sa->chars[i] < sb->chars[i] ? -1 : 1;
        }
    }
    return sa->length < sb->length ? -1 : (sa->length > sb->length ? 1 : 0);
}
} // namespace

Il2CppObject* icall_datatable_select_sort(Il2CppObject* self, Il2CppObject* filter, Il2CppObject* sort)
{
    ThrowIfNullReceiver(self);
    if (sort == nullptr)
    {
        il2cpp_throw_argument_null_exception("sort");
    }
    GcRoot<Il2CppObject*> root(self, "icall_datatable_select_sort");
    // The matches are DataRow wrappers bound to the table rows.
    Il2CppObject* matches = icall_datatable_select(self, filter);
    const Il2CppString* ss = reinterpret_cast<const Il2CppString*>(sort);
    const char16_t* s = ss->chars;
    int32_t slen = ss->length;
    // Parse "col [ASC|DESC]": trim, then check the DESC/ASC suffix.
    bool desc = false;
    int32_t colEnd = slen;
    while (colEnd > 0 && (s[colEnd - 1] == u' ' || s[colEnd - 1] == u'\t')) --colEnd;
    if (colEnd >= 5 && (s[colEnd - 5] == u' ' || s[colEnd - 5] == u'\t')
        && (s[colEnd - 4] | 0x20) == u'd' && (s[colEnd - 3] | 0x20) == u'e'
        && (s[colEnd - 2] | 0x20) == u's' && (s[colEnd - 1] | 0x20) == u'c')
    {
        desc = true;
        colEnd -= 5;
    }
    else if (colEnd >= 4 && (s[colEnd - 4] == u' ' || s[colEnd - 4] == u'\t')
        && (s[colEnd - 3] | 0x20) == u'a' && (s[colEnd - 2] | 0x20) == u's'
        && (s[colEnd - 1] | 0x20) == u'c')
    {
        desc = false;
        colEnd -= 4;
    }
    while (colEnd > 0 && (s[colEnd - 1] == u' ' || s[colEnd - 1] == u'\t')) --colEnd;
    int32_t colStart = 0;
    while (colStart < colEnd && (s[colStart] == u' ' || s[colStart] == u'\t')) ++colStart;
    const Il2CppDataTable* t = AsTable(self);
    int32_t sortColIdx = -1;
    if (t->colCount > 0)
    {
        const Il2CppObject* const* cols = reinterpret_cast<const Il2CppObject* const*>(t->columns->data);
        for (int32_t i = 0; i < t->colCount; ++i)
        {
            const Il2CppString* cn = reinterpret_cast<const Il2CppString*>(cols[i]);
            if (cn->length == colEnd - colStart
                && memcmp(cn->chars, s + colStart,
                          static_cast<size_t>(colEnd - colStart) * sizeof(char16_t)) == 0)
            {
                sortColIdx = i;
                break;
            }
        }
    }
    if (sortColIdx < 0)
    {
        il2cpp_throw_argument_exception("The sort expression references an unknown column.");
    }
    self = root.Get();
    // Insertion sort the wrapper array by the sort column (no allocation).
    Il2CppArray* arr = reinterpret_cast<Il2CppArray*>(matches);
    Il2CppObject** elems = reinterpret_cast<Il2CppObject**>(arr->data);
    const int32_t n = arr->length;
    for (int32_t i = 1; i < n; ++i)
    {
        Il2CppObject* key = elems[i];
        int32_t j = i - 1;
        while (j >= 0)
        {
            const Il2CppDataRow* ra = reinterpret_cast<const Il2CppDataRow*>(elems[j]);
            const Il2CppDataRow* rb = reinterpret_cast<const Il2CppDataRow*>(key);
            int32_t cmp = CompareSortCells(
                reinterpret_cast<Il2CppObject**>(ra->values->data)[sortColIdx],
                reinterpret_cast<Il2CppObject**>(rb->values->data)[sortColIdx]);
            if (desc) cmp = -cmp;
            if (cmp <= 0) break;
            elems[j + 1] = elems[j];
            --j;
        }
        elems[j + 1] = key;
    }
    return matches;
}

//------------------------------------------------------------------------------
// ADO stage 3 (batch 154e): DataTable.Compute(expression, filter).
//------------------------------------------------------------------------------
// The aggregate family over a numeric column: SUM/COUNT/AVG/MIN/MAX. The
// expression shape is "FUNC(column)"; the optional filter reuses the Select
// AND grammar (SelectCond). COUNT returns a boxed int; the numeric
// aggregates return a boxed double (the net48 decimal result is
// approximated -- documented deviation).
Il2CppObject* icall_datatable_compute(Il2CppObject* self, Il2CppObject* expression, Il2CppObject* filter)
{
    ThrowIfNullReceiver(self);
    if (expression == nullptr)
    {
        il2cpp_throw_argument_null_exception("expression");
    }
    const Il2CppString* es = reinterpret_cast<const Il2CppString*>(expression);
    const char16_t* f = es->chars;
    const int32_t flen = es->length;
    // Parse "FUNC(column)" -- the open paren terminates the function name.
    int32_t parenPos = -1;
    for (int32_t i = 0; i < flen; ++i)
    {
        if (f[i] == u'(')
        {
            parenPos = i;
            break;
        }
    }
    if (parenPos < 0 || flen < 1 || f[flen - 1] != u')')
    {
        il2cpp_throw_argument_exception("Unsupported compute expression.");
    }
    // Function name (case-insensitive).
    int32_t fnStart = 0;
    while (fnStart < parenPos && (f[fnStart] == u' ' || f[fnStart] == u'\t')) ++fnStart;
    int32_t fnEnd = parenPos;
    while (fnEnd > fnStart && (f[fnEnd - 1] == u' ' || f[fnEnd - 1] == u'\t')) --fnEnd;
    // Column name inside the parens.
    int32_t colStart = parenPos + 1;
    int32_t colEnd = flen - 1;
    while (colStart < colEnd && (f[colStart] == u' ' || f[colStart] == u'\t')) ++colStart;
    while (colEnd > colStart && (f[colEnd - 1] == u' ' || f[colEnd - 1] == u'\t')) --colEnd;
    enum AggKind { kSum, kCount, kAvg, kMin, kMax } kind = kSum;
    const int32_t fnLen = fnEnd - fnStart;
    if (fnLen == 3 && (f[fnStart] | 0x20) == u's' && (f[fnStart + 1] | 0x20) == u'u' && (f[fnStart + 2] | 0x20) == u'm')
    {
        kind = kSum;
    }
    else if (fnLen == 5 && (f[fnStart] | 0x20) == u'c' && (f[fnStart + 1] | 0x20) == u'o'
             && (f[fnStart + 2] | 0x20) == u'u' && (f[fnStart + 3] | 0x20) == u'n'
             && (f[fnStart + 4] | 0x20) == u't')
    {
        kind = kCount;
    }
    else if (fnLen == 3 && (f[fnStart] | 0x20) == u'a' && (f[fnStart + 1] | 0x20) == u'v'
             && (f[fnStart + 2] | 0x20) == u'g')
    {
        kind = kAvg;
    }
    else if (fnLen == 3 && (f[fnStart] | 0x20) == u'm' && (f[fnStart + 1] | 0x20) == u'i'
             && (f[fnStart + 2] | 0x20) == u'n')
    {
        kind = kMin;
    }
    else if (fnLen == 3 && (f[fnStart] | 0x20) == u'm' && (f[fnStart + 1] | 0x20) == u'a'
             && (f[fnStart + 2] | 0x20) == u'x')
    {
        kind = kMax;
    }
    else
    {
        il2cpp_throw_argument_exception("Unsupported aggregate function.");
    }
    GcRoot<Il2CppObject*> root(self, "icall_datatable_compute");
    const Il2CppDataTable* t = AsTable(self);
    // Resolve the column index by name.
    int32_t colIdx = -1;
    if (t->colCount > 0)
    {
        const Il2CppObject* const* cols = reinterpret_cast<const Il2CppObject* const*>(t->columns->data);
        for (int32_t i = 0; i < t->colCount; ++i)
        {
            const Il2CppString* cn = reinterpret_cast<const Il2CppString*>(cols[i]);
            if (cn->length == colEnd - colStart
                && memcmp(cn->chars, f + colStart,
                          static_cast<size_t>(colEnd - colStart) * sizeof(char16_t)) == 0)
            {
                colIdx = i;
                break;
            }
        }
    }
    if (colIdx < 0)
    {
        il2cpp_throw_argument_exception("The compute expression references an unknown column.");
    }
    // Parse the optional filter (the Select AND grammar). The filter chars
    // pointer stays valid for the row loop (no allocation happens between
    // the parse and the loop, so no collection can move the filter string).
    SelectCond cond{};
    bool hasCond = false;
    const char16_t* ff = nullptr;
    if (filter != nullptr)
    {
        const Il2CppString* ffs = reinterpret_cast<const Il2CppString*>(filter);
        ff = ffs->chars;
        const int32_t fflen = ffs->length;
        if (fflen > 0)
        {
            const int32_t andPos = FindAndSeparator(ff, fflen);
            if (!cond.Parse(ff, 0, andPos < 0 ? fflen : andPos, t))
            {
                il2cpp_throw_argument_exception("Unsupported filter expression.");
            }
            hasCond = true;
        }
    }
    self = root.Get();
    t = AsTable(self);
    const int32_t rowCount = t->rowCount;
    const Il2CppObject* const* rows = reinterpret_cast<const Il2CppObject* const*>(t->rows->data);
    double acc = 0.0;
    double accMin = 0.0;
    double accMax = 0.0;
    int32_t count = 0;
    bool firstValue = true;
    for (int32_t ri = 0; ri < rowCount; ++ri)
    {
        const Il2CppObject* const* values = reinterpret_cast<const Il2CppObject* const*>(
            reinterpret_cast<const Il2CppArray*>(rows[ri])->data);
        if (hasCond && !cond.Matches(values[cond.colIdx], ff))
        {
            continue;
        }
        if (kind == kCount)
        {
            ++count;
            continue;
        }
        const Il2CppObject* cell = values[colIdx];
        if (cell == nullptr)
        {
            continue;
        }
        double v;
        if (cell->klass == &System_Int32_class)
        {
            v = static_cast<double>(icall_unbox_int32(const_cast<Il2CppObject*>(cell)));
        }
        else if (cell->klass == &System_Double_class)
        {
            v = icall_unbox_double(const_cast<Il2CppObject*>(cell));
        }
        else
        {
            continue;
        }
        if (firstValue)
        {
            accMin = v;
            accMax = v;
            firstValue = false;
        }
        else
        {
            if (v < accMin) accMin = v;
            if (v > accMax) accMax = v;
        }
        acc += v;
        ++count;
    }
    if (kind == kCount)
    {
        return reinterpret_cast<Il2CppObject*>(il2cpp_box_int32(nullptr, count));
    }
    double result = 0.0;
    switch (kind)
    {
        case kSum: result = acc; break;
        case kAvg: result = count > 0 ? acc / static_cast<double>(count) : 0.0; break;
        case kMin: result = accMin; break;
        case kMax: result = accMax; break;
        default: break;
    }
    return reinterpret_cast<Il2CppObject*>(icall_box_double(result));
}
//------------------------------------------------------------------------------
// ADO stage 2 (batch 153d): DataSet -- the table collection surface.
//------------------------------------------------------------------------------
void icall_dataset_ctor(Il2CppObject* self)
{
    EnsureDataSetRegistered();
    Il2CppDataSet* ds = reinterpret_cast<Il2CppDataSet*>(self);
    GcRoot<Il2CppDataSet*> root(ds, "icall_dataset_ctor");
    Il2CppArray* t = AllocRefArray(0);
    ds = root.Get();
    ds->tables = t;
    il2cpp_write_barrier(reinterpret_cast<void**>(&ds->tables),
                         reinterpret_cast<Il2CppObject*>(ds->tables));
    ds->count = 0;
}

// DataSet.Tables.Add(DataTable): append the table.
void icall_dataset_tables_add(Il2CppObject* self, Il2CppObject* table)
{
    ThrowIfNullReceiver(self);
    if (table == nullptr)
    {
        il2cpp_throw_argument_null_exception("table");
    }
    GcRoot<Il2CppObject*> root(self, "icall_dataset_tables_add");
    Il2CppDataSet* ds = AsDataSet(self);
    const int32_t newCount = ds->count + 1;
    Il2CppArray* newTables = AllocRefArray(newCount);
    self = root.Get();
    ds = AsDataSet(self);
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(newTables->data);
    Il2CppObject* const* src = reinterpret_cast<Il2CppObject* const*>(ds->tables->data);
    for (int32_t i = 0; i < ds->count; ++i)
    {
        dst[i] = src[i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
    }
    dst[ds->count] = table;
    il2cpp_write_barrier(reinterpret_cast<void**>(&dst[ds->count]), dst[ds->count]);
    ds->tables = newTables;
    il2cpp_write_barrier(reinterpret_cast<void**>(&ds->tables),
                         reinterpret_cast<Il2CppObject*>(ds->tables));
    ds->count = newCount;
}

int32_t icall_dataset_get_table_count(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsDataSet(self)->count;
}

Il2CppObject* icall_dataset_get_table(Il2CppObject* self, int32_t index)
{
    ThrowIfNullReceiver(self);
    Il2CppDataSet* ds = AsDataSet(self);
    if (index < 0 || index >= ds->count)
    {
        il2cpp_throw_argument_out_of_range_exception("index", nullptr);
    }
    Il2CppObject* const* tables = reinterpret_cast<Il2CppObject* const*>(ds->tables->data);
    return tables[index];
}

//------------------------------------------------------------------------------
// ADO stage 3 (batch 154g): DataTable.Clone().
//------------------------------------------------------------------------------
// A new table with the same column schema and no rows (net48 Clone copies
// the schema; the minimal surface copies the column names).
Il2CppObject* icall_datatable_clone(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    GcRoot<Il2CppObject*> root(self, "icall_datatable_clone");
    const Il2CppDataTable* t = AsTable(self);
    const int32_t cols = t->colCount;
    Il2CppObject* newObj = il2cpp_object_new(il2cpp_datatable_class());
    GcRoot<Il2CppObject*> rootNew(newObj, "icall_datatable_clone_new");
    Il2CppArray* newCols = AllocRefArray(cols);
    newObj = rootNew.Get();
    Il2CppDataTable* nt = AsTable(newObj);
    Il2CppObject* const* srcCols = reinterpret_cast<Il2CppObject* const*>(t->columns->data);
    Il2CppObject** dst = reinterpret_cast<Il2CppObject**>(newCols->data);
    for (int32_t i = 0; i < cols; ++i)
    {
        dst[i] = srcCols[i];
        il2cpp_write_barrier(reinterpret_cast<void**>(&dst[i]), dst[i]);
    }
    nt->columns = newCols;
    il2cpp_write_barrier(reinterpret_cast<void**>(&nt->columns),
                         reinterpret_cast<Il2CppObject*>(nt->columns));
    nt->colCount = cols;
    Il2CppArray* emptyRows = AllocRefArray(0);
    newObj = rootNew.Get();
    nt = AsTable(newObj);
    nt->rows = emptyRows;
    il2cpp_write_barrier(reinterpret_cast<void**>(&nt->rows),
                         reinterpret_cast<Il2CppObject*>(nt->rows));
    nt->rowCount = 0;
    return newObj;
}

//------------------------------------------------------------------------------
// ADO stage 3 (batch 154): DataTable.TableName get/set + DataRow.Table.
//------------------------------------------------------------------------------
Il2CppObject* icall_datatable_get_name(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(AsTable(self)->name);
}

void icall_datatable_set_name(Il2CppObject* self, Il2CppObject* value)
{
    ThrowIfNullReceiver(self);
    if (value != nullptr && value->klass != &System_String_class)
    {
        il2cpp_throw_argument_exception("TableName must be a string.");
    }
    AsTable(self)->name = reinterpret_cast<Il2CppString*>(value);
    il2cpp_write_barrier(reinterpret_cast<void**>(&AsTable(self)->name),
                         value);
}

Il2CppObject* icall_datarow_get_table(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return AsRow(self)->table;
}

//------------------------------------------------------------------------------
// icall_datacolumn_get_name -- DataColumn.ColumnName.
//------------------------------------------------------------------------------
Il2CppObject* icall_datacolumn_get_name(Il2CppObject* self)
{
    ThrowIfNullReceiver(self);
    return reinterpret_cast<Il2CppObject*>(AsColumn(self)->name);
}

} // namespace runtime
} // namespace il2cpp
