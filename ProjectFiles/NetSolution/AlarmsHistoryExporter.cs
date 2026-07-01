#region Using directives
using System;
using System.IO;
using System.Text;
using FTOptix.CoreBase;
using FTOptix.HMIProject;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.NetLogic;
using FTOptix.EventLogger;
using FTOptix.OPCUAServer;
using FTOptix.UI;
using FTOptix.Store;
using FTOptix.SQLiteStore;
using FTOptix.Core;
using FTOptix.Modbus;
using FTOptix.CommunicationDriver;
using FTOptix.OmronFins;
using FTOptix.OmronEthernetIP;
using FTOptix.S7TiaProfinet;
using FTOptix.DataLogger;
using FTOptix.Recipe;
using FTOptix.ODBCStore;
using FTOptix.RecipeX;
#endregion

public class AlarmsHistoryExporter : BaseNetLogic
{
    /// <summary>
    /// This method exports alarm history data to a CSV file.
    /// It validates time slice, retrieves CSV file path, delimiter, wraps fields, table object, stores object, and query.
    /// Executes SQL query on the stored object, writes the header and content to the CSV file.
    /// If an error occurs during execution, it logs the exception message.
    /// </summary>
    [ExportMethod]
    public void Export()
    {
        try
        {
            ValidateTimeSlice();

            var csvPath = GetCSVFilePath();
            if (string.IsNullOrEmpty(csvPath))
                throw new Exception("No CSV file chosen, please fill the CSVPath variable");

            char? fieldDelimiter = GetFieldDelimiter();
            bool wrapFields = GetWrapFields();
            var tableObject = GetTable();
            var storeObject = GetStoreObject(tableObject);
            var selectQuery = GetQuery();

            storeObject.Query(selectQuery, out string[] header, out object[,] resultSet);

            if (header == null || resultSet == null)
                throw new Exception("Unable to execute SQL query, malformed result");

            var rowCount = resultSet.GetLength(0);
            var columnCount = resultSet.GetLength(1);

            using (var csvWriter = new CSVFileWriter(csvPath) { FieldDelimiter = fieldDelimiter.Value, WrapFields = wrapFields })
            {
                csvWriter.WriteLine(header);
                WriteTableContent(resultSet, rowCount, columnCount, csvWriter);
            }

            Log.Info("AlarmsHistoryExporter", "The alarms history has been succesfully exported to " + csvPath);
        }
        catch (Exception ex)
        {
            Log.Error("AlarmsHistoryExporter", "Unable to export data alarms history: " + ex.Message);
        }
    }

    /// <summary>
    /// Writes table content from an array into a CSV file writer.
    /// Each row in the array represents one line in the CSV file.
    /// The method iterates over each cell in the specified rows and columns,
    /// converting non-null cells to strings before writing them to the CSV file.
    /// If a cell contains null, it writes "NULL" instead.
    /// </summary>
    /// <param name="resultSet">Array containing rows of data to write.</param>
    /// <param name="rowCount">Number of rows to process.</param>
    /// <param name="columnCount">Number of columns per row.</param>
    /// <param name="csvWriter">CSV file writer object to which the data will be written.</param>
    private void WriteTableContent(object[,] resultSet, int rowCount, int columnCount, CSVFileWriter csvWriter)
    {
        for (var r = 0; r < rowCount; ++r)
        {
            var currentRow = new string[columnCount];

            for (var c = 0; c < columnCount; ++c)
                currentRow[c] = resultSet[r, c]?.ToString() ?? "NULL";

            csvWriter.WriteLine(currentRow);
        }
    }

    /// <summary>
    /// This method retrieves a table from the information model based on the provided variable reference.
    /// If the variable is missing or invalid, it throws an exception indicating that the table was not found.
    /// It also checks whether the retrieved node represents a valid table with a non-empty ID before returning it.
    /// </summary>
    /// <returns>The table object corresponding to the given variable reference.</returns>
    /// <remarks>
    /// Results in <c>table</c> containing the data associated with the specified table variable.
    /// </remarks>
    /// <param name="variableName">The name of the variable holding the table reference.</param>
    /// <param name="informationModel">The information model used for querying nodes.</param>
    private Table GetTable()
    {
        var alarmEventLoggerVariable = LogicObject.GetVariable("Table");
        if (alarmEventLoggerVariable == null)
            throw new Exception("Table variable not found");

        NodeId tableNodeId = alarmEventLoggerVariable.Value;
        if (tableNodeId == null || tableNodeId == NodeId.Empty)
            throw new Exception("Table variable is empty");

        var tableNode = InformationModel.Get(tableNodeId) as Table;
        if (tableNode == null)
            throw new Exception("The specified table node is not an instance of Store Table type");

        return tableNode;
    }

    /// <summary>
    /// This method retrieves the store object associated with the given table node's owner.
    /// <example>
    /// For example:
    /// <code>
    /// Store store = GetStoreObject(tableNode);
    /// </code>
    /// results in <c>store</c>'s being set to the store object corresponding to the owner of the specified table node.
    /// </example>
    /// </summary>
    /// <param name="tableNode">The table node whose owner's store will be retrieved.</param>
    /// <returns>
    /// The store object linked to the owner of the provided table node.
    /// </returns>
    /// <remarks>
    /// Assumes that the 'Owner' property on the 'TableNode' class has an 'Owner' property which itself is of type 'Store'.
    /// </remarks>
    private Store GetStoreObject(Table tableNode)
    {
        return tableNode.Owner.Owner as Store;
    }

    /// <summary>
    /// Retrieves the CSV file path from the logic object and returns it as a Uri resource.
    /// If the CSVPath variable is not found, an exception is thrown.
    /// </summary>
    /// <returns>A Uri representing the location of the CSV file.</returns>
    /// <remarks>
    /// The method retrieves the CSV file path using the GetVariable method from the LogicObject class,
    /// checks for its presence by comparing it with null, and throws an exception if the variable is missing.
    /// It then constructs a Uri resource from the CSVPath's value and returns it.
    /// </remarks>
    private string GetCSVFilePath()
    {
        var csvPathVariable = LogicObject.GetVariable("CSVPath");
        if (csvPathVariable == null)
            throw new Exception("CSVPath variable not found");

        return new ResourceUri(csvPathVariable.Value).Uri;
    }

    /// <summary>
    /// This method retrieves the field delimiter from the logic object's variable "FieldDelimiter".
    /// If the variable is not found or its value is invalid, it throws an exception.
    /// It checks for a non-empty string, ensures the length is exactly one, validates that the character can be parsed as a valid char, and returns the delimiter.
    /// </summary>
    /// <returns>The field delimiter as a char type.</returns>
    private char GetFieldDelimiter()
    {
        var separatorVariable = LogicObject.GetVariable("FieldDelimiter");
        if (separatorVariable == null)
            throw new Exception("FieldDelimiter variable not found");

        string separator = separatorVariable.Value;
        if (separator == String.Empty)
            throw new Exception("FieldDelimiter variable is empty");

        if (separator.Length != 1)
            throw new Exception("Wrong FieldDelimiter configuration. Please insert a single character");

        if (!char.TryParse(separator, out char result))
            throw new Exception("Wrong FieldDelimiter configuration. Please insert a char");

        return result;
    }

    /// <summary>
    /// This method retrieves the value associated with the "WrapFields" variable from the LogicObject and returns it as a boolean.
    /// If the "WrapFields" variable is not found, an exception will be thrown.
    /// </summary>
    /// <returns>A boolean value representing the state of the "WrapFields" variable.</returns>
    private bool GetWrapFields()
    {
        var wrapFieldsVariable = LogicObject.GetVariable("WrapFields");
        if (wrapFieldsVariable == null)
            throw new Exception("WrapFields variable not found");

        return wrapFieldsVariable.Value;
    }

    /// <summary>
    /// This method retrieves the query from a logic object's variable named 'Query'.
    /// If the query variable is not found, an exception is thrown with the message "Query variable not found".
    /// If the retrieved query is null or empty, another exception is thrown with the message "Query variable is empty or not valid".
    /// The method returns the validated query as a string.
    /// </summary>
    /// <returns>The validated query as a string.</returns>
    private string GetQuery()
    {
        var queryVariable = LogicObject.GetVariable("Query");
        if (queryVariable == null)
            throw new Exception("Query variable not found");

        string query = queryVariable.Value;
        if (String.IsNullOrEmpty(query))
            throw new Exception("Query variable is empty or not valid");

        return query;
    }

    /// <summary>
    /// Validates the time slice by checking for null variables and ensuring the start date is before the end date.
    /// If either variable is null or both are after each other, an exception is thrown.
    /// </summary>
    /// <remarks>
    /// Throws an exception if:
    /// - Either "From" or "To" variable is null or empty.
    /// - The "To" date is after the "From" date.
    /// </remarks>
    private void ValidateTimeSlice()
    {
        var fromVariable = LogicObject.GetVariable("From");
        if (fromVariable == null || fromVariable.Value == null)
            throw new Exception("From variable is empty or missing");
        var toVariable = LogicObject.GetVariable("To");
        if (toVariable == null || toVariable.Value == null)
            throw new Exception("To variable is empty or missing");

        DateTime fromValue = fromVariable.Value;
        DateTime toValue = toVariable.Value;

        if (toValue < fromValue)
            throw new Exception("Not a valid time slice. The date entered in the \"From\" property is later than the date entered in the \"To\"");
    }

    #region CSVFileWriter
    private class CSVFileWriter : IDisposable
    {
        public char FieldDelimiter { get; set; } = ',';

        public char QuoteChar { get; set; } = '"';

        public bool WrapFields { get; set; } = false;

        /// <summary>
        /// Initializes a new instance of the CSVFileWriter class with the specified file path.
        /// </summary>
        /// <param name="filePath">The path of the file where the data will be written.</param>
        /// <remarks>
        /// The constructor creates a new StreamWriter object using the provided file path,
        /// opening it for writing (false flag means append mode).
        /// UTF-8 encoding is used by default.
        /// </remarks>
        /// <returns>A StreamWriter instance ready for writing CSV data.</returns>
        public CSVFileWriter(string filePath)
        {
            streamWriter = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// Initializes a new instance of the CSVFileWriter class with the specified file path and encoding.
        /// </summary>
        /// <param name="filePath">The file path where the output will be written.</param>
        /// <param name="encoding">The character encoding used for writing the data.</param>
        /// <remarks>
        /// The constructor creates a new StreamWriter object that writes to the specified file path using the provided encoding.
        /// </remarks>
        public CSVFileWriter(string filePath, System.Text.Encoding encoding)
        {
            streamWriter = new StreamWriter(filePath, false, encoding);
        }

        /// <summary>
        /// Initializes a new instance of the CSVFileWriter class with the specified StreamWriter.
        /// <example>
        /// For example:
        /// <code>
        /// var writer = new CSVFileWriter(new StringWriter());
        /// </code>
        /// results in <c>writer</c> being an initialized instance of CSVFileWriter using the given StringWriter.
        /// </example>
        /// </summary>
        /// <param name="streamWriter">A StreamWriter used for writing CSV data.</param>
        /// <returns>
        /// A CSVFileWriter instance configured with the provided StreamWriter.
        /// </returns>
        /// <remarks>
        /// The constructor initializes the underlying stream writer for CSV file operations.
        /// </remarks>
        public CSVFileWriter(StreamWriter streamWriter)
        {
            this.streamWriter = streamWriter;
        }

        /// <summary>
        /// Writes an array of strings to the specified stream with optional field wrapping and delimiter handling.
        /// <example>
        /// For example:
        /// <code>
        /// string[] data = { "Name", "John Doe" };
        /// WriteData(data, stream, true, "\t", "\n");
        /// </code>
        /// results in writing the data to the stream with each field separated by a tab character and line breaks added as necessary.
        /// </example>
        /// </summary>
        /// <param name="fields">An array containing the string fields to be written.</param>
        public void WriteLine(string[] fields)
        {
            var stringBuilder = new StringBuilder();

            for (var i = 0; i < fields.Length; ++i)
            {
                if (WrapFields)
                    stringBuilder.AppendFormat("{0}{1}{0}", QuoteChar, EscapeField(fields[i]));
                else
                    stringBuilder.AppendFormat("{0}", fields[i]);

                if (i != fields.Length - 1)
                    stringBuilder.Append(FieldDelimiter);
            }

            streamWriter.WriteLine(stringBuilder.ToString());
            streamWriter.Flush();
        }

        /// <summary>
        /// This method escapes a given field by replacing the quotes with double quotes.
        /// </summary>
        /// <param name="field">The field to be escaped.</param>
        /// <returns>The escaped field as a string.</returns>
        private string EscapeField(string field)
        {
            var quoteCharString = QuoteChar.ToString();
            return field.Replace(quoteCharString, quoteCharString + quoteCharString);
        }

        private StreamWriter streamWriter;

        #region IDisposable Support
        private bool disposed = false;
        /// <summary>
        /// This protected virtual method handles the disposal of resources when either disposing or non-disposing state is active.
        /// If disposing is true, it disposes of the stream writer resource; otherwise, it does nothing.
        /// It marks the object as disposed once all resources are properly cleaned up.
        /// </summary>
        /// <param name="disposing">
        /// True if the method is called during the disposing phase; false if called during the non-disposing phase.
        /// </param>
        /// <remarks>
        /// The object is marked as disposed after both phases complete.
        /// </remarks>
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
                streamWriter.Dispose();

            disposed = true;
        }

        /// <summary>
        /// The Dispose method is called by the garbage collector when an object is no longer needed but must be cleaned up before it can be reclaimed.
        /// <example>
        /// For example:
        /// <code>
        /// myObject.Dispose();
        /// </code>
        /// will ensure that all resources associated with the object are properly released.
        /// </example>
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }
    #endregion
}
