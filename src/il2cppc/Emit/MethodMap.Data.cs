//------------------------------------------------------------------------------
// MethodMap.Data -- System.Data minimal surface (batch 152f, ADO stage 1):
// DataTable / DataColumn / DataRow.
//
//   DataTable()
//     .Columns.Add(string)          -> icall_datatable_columns_add (DataColumn)
//     .Columns.Count                -> icall_datatable_get_column_count
//     .Columns[int]                 -> icall_datatable_get_column (DataColumn)
//     .Rows.Add(object[])           -> icall_datatable_rows_add (DataRow)
//     .Rows.Count                   -> icall_datatable_get_row_count
//     .Rows[int]                    -> icall_datatable_get_row (DataRow)
//   DataRow[int] get / set          -> icall_datarow_get_item / set_item
//   DataColumn.ColumnName           -> icall_datacolumn_get_name
//
// The descriptors are emitted as &System_Data_DataTable_class etc. (the
// runtime registers them; newobj routes through il2cpp_datatable_class()).
//------------------------------------------------------------------------------

namespace Il2Cpp.Transpiler.Emit
{
    public static partial class MethodRefResolver
    {
        private static ResolvedMethod? TryMapData(string owner, string name, byte[] ps, int count,
            byte[]? genArgs, string? receiverType, bool receiverIsAddress,
            string? arg0Type, string? arg1Type)
        {
            switch (owner)
            {
                case "System.Data.DataTable":
                    switch (name)
                    {
                        case ".ctor":
                            if (count == 0)
                                return I("il2cpp::runtime::icall_datatable_ctor", 0, false, false, true);
                            return null;
                        case "get_Columns":
                            // Columns is a property returning a DataColumnCollection
                            // -- the minimal surface folds it to the table itself
                            // (Columns.Count/Add[int]/Add(string) route through
                            // the DataTable icalls with the receiver uncast).
                            return new ResolvedMethod("::identity::", 0, false, true, true,
                                new[] { false }, true,
                                ReturnTypeName: "System.Data.DataColumnCollection");
                        case "get_Rows":
                            // Rows folds to the table itself (same pattern).
                            return new ResolvedMethod("::identity::", 0, false, true, true,
                                new[] { false }, true,
                                ReturnTypeName: "System.Data.DataRowCollection");
                        case "Add":
                            // Columns.Add(string) / Rows.Add(object[]) -- the
                            // receiver is the folded table; the argument passes
                            // through uncast (string or object[] "a").
                            if (count == 1 && ps[0] == 0x0E)
                                return I("il2cpp::runtime::icall_datatable_columns_add", 1, true, true, true,
                                    new[] { "", "" }, returnTypeName: "System.Data.DataColumn");
                            if (count == 1 && ps[0] == 0x1D)
                                return I("il2cpp::runtime::icall_datatable_rows_add", 1, true, true, true,
                                    new[] { "", "a" }, returnTypeName: "System.Data.DataRow");
                            return null;
                        case "get_Count":
                            // Columns.Count / Rows.Count -- the receiver is the
                            // folded table; the DataColumnCollection/DataRowCollection
                            // pseudo-owner resolves here via the receiver type.
                            return new ResolvedMethod(
                                receiverType == "System.Data.DataRowCollection"
                                    ? "il2cpp::runtime::icall_datatable_get_row_count"
                                    : "il2cpp::runtime::icall_datatable_get_column_count",
                                0, false, true, false, new[] { false }, true);
                        case "get_Item":
                            // Columns[int] / Rows[int] -- the receiver is the
                            // folded table.
                            return new ResolvedMethod(
                                receiverType == "System.Data.DataRowCollection"
                                    ? "il2cpp::runtime::icall_datatable_get_row"
                                    : "il2cpp::runtime::icall_datatable_get_column",
                                1, true, true, true, new[] { false, false }, true,
                                ParamTypeNames: new[] { "", "i" },
                                ReturnTypeName: receiverType == "System.Data.DataRowCollection"
                                    ? "System.Data.DataRow" : "System.Data.DataColumn");
                        case "Clear":
                            // DataTable.Clear() -- ADO stage 2.
                            if (count == 0)
                                return I("il2cpp::runtime::icall_datatable_clear", 0, false, false, true);
                            return null;
                        case "NewRow":
                            // DataTable.NewRow() -- ADO stage 2.
                            if (count == 0)
                                return I("il2cpp::runtime::icall_datatable_new_row", 0, true, true, true,
                                    returnTypeName: "System.Data.DataRow");
                            return null;
                        case "get_TableName":
                            // ADO stage 3.
                            return I("il2cpp::runtime::icall_datatable_get_name", 0, true, true, true,
                                returnTypeName: "System.String");
                        case "set_TableName":
                            // ADO stage 3.
                            if (count == 1)
                                return I("il2cpp::runtime::icall_datatable_set_name", 1, false, false, true,
                                    new[] { "", "" });
                            return null;
                        case "Select":
                            // DataTable.Select(string) -- ADO stage 2; returns
                            // a DataRow[].
                            if (count == 1)
                                return I("il2cpp::runtime::icall_datatable_select", 1, true, true, true,
                                    new[] { "", "" }, returnTypeName: "System.Data.DataRow[]");
                            // DataTable.Select(string, string) -- ADO stage 3;
                            // the sort expression (single column ASC/DESC).
                            if (count == 2)
                                return I("il2cpp::runtime::icall_datatable_select_sort", 2, true, true, true,
                                    new[] { "", "", "" }, returnTypeName: "System.Data.DataRow[]");
                            return null;
                        case "Compute":
                            // DataTable.Compute(string, string) -- ADO stage 3;
                            // returns a boxed aggregate (object).
                            if (count == 2)
                                return I("il2cpp::runtime::icall_datatable_compute", 2, true, true, true,
                                    new[] { "", "", "" });
                            return null;
                        case "Clone":
                            // DataTable.Clone() -- ADO stage 3.
                            if (count == 0)
                                return I("il2cpp::runtime::icall_datatable_clone", 0, true, true, true,
                                    returnTypeName: "System.Data.DataTable");
                            return null;
                        default:
                            return null;
                    }
                case "System.Data.DataRow":
                    switch (name)
                    {
                        case "get_Item":
                            return I("il2cpp::runtime::icall_datarow_get_item", 1, true, true, true,
                                new[] { "", "i" });
                        case "set_Item":
                            return I("il2cpp::runtime::icall_datarow_set_item", 2, false, false, true,
                                new[] { "", "i", "" });
                        case "get_ItemArray":
                            return I("il2cpp::runtime::icall_datarow_get_item_array", 0, true, true, true);
                        case "set_ItemArray":
                            return I("il2cpp::runtime::icall_datarow_set_item_array", 1, false, false, true,
                                new[] { "", "" });
                        case "get_Table":
                            // ADO stage 3: DataRow.Table.
                            return I("il2cpp::runtime::icall_datarow_get_table", 0, true, true, true,
                                returnTypeName: "System.Data.DataTable");
                        default:
                            return null;
                    }
                case "System.Data.DataSet":
                    switch (name)
                    {
                        case ".ctor":
                            if (count == 0)
                                return I("il2cpp::runtime::icall_dataset_ctor", 0, false, false, true);
                            return null;
                        case "get_Tables":
                            // Tables folds to the DataSet itself (the same
                            // identity pattern as Columns/Rows on DataTable).
                            return new ResolvedMethod("::identity::", 0, false, true, true,
                                new[] { false }, true,
                                ReturnTypeName: "System.Data.DataTableCollection");
                        default:
                            return null;
                    }
                case "System.Data.DataTableCollection":
                    switch (name)
                    {
                        case "Add":
                            // Tables.Add(DataTable) -- ADO stage 2.
                            if (count == 1 && receiverType == "System.Data.DataTableCollection")
                                return I("il2cpp::runtime::icall_dataset_tables_add", 1, false, false, true,
                                    new[] { "", "" });
                            return null;
                        case "get_Count":
                            if (receiverType == null) return null;
                            if (receiverType == "System.Data.DataTableCollection")
                                return I("il2cpp::runtime::icall_dataset_get_table_count", 0, true, false, true);
                            return null;
                        case "get_Item":
                            if (receiverType == "System.Data.DataTableCollection")
                                return I("il2cpp::runtime::icall_dataset_get_table", 1, true, true, true,
                                    new[] { "", "i" }, returnTypeName: "System.Data.DataTable");
                            return null;
                        default:
                            return null;
                    }
                case "System.Data.DataColumn":
                    switch (name)
                    {
                        case ".ctor":
                            // DataColumn() / DataColumn(string) / DataColumn(string, Type).
                            if (count == 0)
                                return I("il2cpp::runtime::icall_datacolumn_ctor", 0, false, false, true,
                                    new[] { "" });
                            if (count == 1 && ps[0] == 0x0E)
                                return I("il2cpp::runtime::icall_datacolumn_ctor", 1, false, false, true,
                                    new[] { "", "" });
                            if (count == 2 && ps[0] == 0x0E)
                                // The Type VALUE is its full-name string in this
                                // runtime (the simplified Type model), so the
                                // second argument rides the reference slot.
                                return I("il2cpp::runtime::icall_datacolumn_ctor_typed", 2, false, false, true,
                                    new[] { "", "", "" });
                            return null;
                        case "get_ColumnName":
                            return I("il2cpp::runtime::icall_datacolumn_get_name", 0, true, true, true);
                        case "get_DataType":
                            return I("il2cpp::runtime::icall_datacolumn_get_data_type", 0, true, true, true);
                        case "set_DataType":
                            if (count == 1)
                                return I("il2cpp::runtime::icall_datacolumn_set_data_type", 1, false, false, true,
                                    new[] { "", "" });
                            return null;
                        default:
                            return null;
                    }
                case "System.Data.DataColumnCollection":
                case "System.Data.DataRowCollection":
                case "System.Data.InternalDataCollectionBase":
                    // The Columns/Rows property folds to the table itself
                    // (::identity::); the collection members forward to the
                    // table icalls (the receiver is the folded table).
                    // InternalDataCollectionBase is the shared base declaring
                    // get_Count; the folded receiver carries the concrete
                    // collection type in receiverType. DataTableCollection
                    // also lands here through the shared base (DataSet.Tables).
                    switch (name)
                    {
                        case "Add":
                            // Columns.Add(string[, Type]) / Rows.Add(object[]) /
                            // Rows.Add(DataRow). The receiver is the folded
                            // table; the arguments pass through uncast.
                            //
                            // The two-argument Columns.Add(string, Type) is the
                            // TYPED-column constructor: the Type VALUE is its
                            // full-name string in this runtime (the simplified
                            // Type model), so it rides the same reference slot
                            // and the icall stores it as the column's DataType.
                            if (count == 2 && ps[0] == 0x0E)
                                return I("il2cpp::runtime::icall_datatable_columns_add_typed", 2, true, true, true,
                                    new[] { "", "", "" }, returnTypeName: "System.Data.DataColumn");
                            if (count == 1 && ps[0] == 0x0E)
                                return I("il2cpp::runtime::icall_datatable_columns_add", 1, true, true, true,
                                    new[] { "", "" }, returnTypeName: "System.Data.DataColumn");
                            // Columns.Add(DataColumn) -- adopt a constructed
                            // column. The receiver type MUST be the column
                            // collection: the hint-less first resolve pass
                            // carries receiverType == null and must DEFER (the
                            // receiver-aware re-resolve picks this arm or the
                            // Rows.Add(DataRow) one below).
                            if (count == 1 && ps[0] == 0x12
                                && receiverType == "System.Data.DataColumnCollection")
                                return I("il2cpp::runtime::icall_datatable_columns_add_column", 1, true, true, true,
                                    new[] { "", "" }, returnTypeName: "System.Data.DataColumn");
                            if (count == 1 && ps[0] == 0x1D)
                                return I("il2cpp::runtime::icall_datatable_rows_add", 1, true, true, true,
                                    new[] { "", "a" }, returnTypeName: "System.Data.DataRow");
                            // Rows.Add(DataRow) -- mount a NewRow -- ADO
                            // stage 2.
                            if (count == 1 && ps[0] == 0x12 && receiverType == "System.Data.DataRowCollection")
                                return I("il2cpp::runtime::icall_datatable_rows_add_row", 1, false, false, true,
                                    new[] { "", "" });
                            return null;
                        case "get_Count":
                            // The concrete collection type arrives in the
                            // receiver-aware re-resolve (defer without it --
                            // the owner may be the InternalDataCollectionBase
                            // base, which cannot choose). DataTableCollection
                            // (DataSet.Tables) lands here through the base.
                            if (receiverType == null) return null;
                            if (receiverType == "System.Data.DataTableCollection")
                                return I("il2cpp::runtime::icall_dataset_get_table_count", 0, true, false, true);
                            return new ResolvedMethod(
                                receiverType == "System.Data.DataRowCollection"
                                    ? "il2cpp::runtime::icall_datatable_get_row_count"
                                    : "il2cpp::runtime::icall_datatable_get_column_count",
                                0, false, true, false, new[] { false }, true);
                        case "RemoveAt":
                            // Rows.RemoveAt(int) -- ADO stage 2.
                            if (count == 1 && receiverType == "System.Data.DataRowCollection")
                                return I("il2cpp::runtime::icall_datatable_rows_remove_at", 1, false, false, true,
                                    new[] { "", "i" });
                            return null;
                        case "Remove":
                            // Rows.Remove(DataRow) -- ADO stage 2.
                            if (count == 1 && receiverType == "System.Data.DataRowCollection")
                                return I("il2cpp::runtime::icall_datatable_rows_remove", 1, false, false, true,
                                    new[] { "", "" });
                            return null;
                        case "Clear":
                            // DataTable.Clear() / Rows.Clear() -- ADO stage 2.
                            if (count == 0)
                                return I("il2cpp::runtime::icall_datatable_clear", 0, false, false, true);
                            return null;
                        case "get_Item":
                            return new ResolvedMethod(
                                owner == "System.Data.DataRowCollection"
                                    ? "il2cpp::runtime::icall_datatable_get_row"
                                    : "il2cpp::runtime::icall_datatable_get_column",
                                1, true, true, true, new[] { false, false }, true,
                                ParamTypeNames: new[] { "", "i" },
                                ReturnTypeName: owner == "System.Data.DataRowCollection"
                                    ? "System.Data.DataRow" : "System.Data.DataColumn");
                        default:
                            return null;
                    }
                default:
                    return null;
            }
        }
    }
}
