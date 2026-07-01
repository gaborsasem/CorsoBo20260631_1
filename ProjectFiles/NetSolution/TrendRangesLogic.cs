#region Using directives

using FTOptix.Core;
using FTOptix.CoreBase;
using FTOptix.DataLogger;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.Store;
using FTOptix.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UAManagedCore;
using FTOptix.SQLiteStore;
using FTOptix.WebUI;
using System.Collections;
using FTOptix.Recipe;
using FTOptix.ODBCStore;
using FTOptix.RecipeX;
using FTOptix.OPCUAServer;

#endregion

public class TrendRangesLogic : BaseNetLogic
{
    /// <summary>
    /// Initializes and sets up the reference observer for time ranges and pens in a trend.
    /// Retrieves the trend object, checks for existence, and initializes the references observer
    /// to monitor changes in time ranges and pens. If no pens are found, skips further processing.
    /// </summary>
    /// <remarks>
    /// Assumes access to an owner object and information model for retrieving
    /// trend, logger, store, and references observer.
    /// </remarks>
    public override void Start()
    {
        // Insert code to be executed when the user-defined logic is started
        var trend = Owner.Owner.Owner.Owner.Owner.Get<FTOptix.UI.Trend>("TrendPanel/MainTrend");
        if (trend == null)
        {
            Log.Error("TrendRangesLogic", "Cannot get to the main trend, if the widget was tampered, make sure to restore a working path");
            return;
        }
        var pens = trend.Get("Pens");
        if (pens.Children.Count == 0)
        {
            Log.Debug("TrendRangesLogic", "No pens to render, skipping...");
            return;
        }
        var logger = InformationModel.Get<FTOptix.DataLogger.DataLogger>(Owner.Owner.Owner.Owner.Owner.GetVariable("Logger").Value);
        var store = InformationModel.Get<FTOptix.Store.Store>(logger.Store);
        var rangesNode = trend.Get("TimeRanges");
        bool localTime = trend.ReferenceTimeZone == ReferenceTimeZone.Local;
        referencesObserver = new ReferencesObserver(rangesNode, pens, LogicObject.Owner.Get<Item>("Scroll/Container"), store, logger, localTime);

        referencesEventRegistration = rangesNode.RegisterEventObserver(
            referencesObserver, EventType.ForwardReferenceAdded | EventType.ForwardReferenceRemoved);
    }

    /// <summary>
    /// Releases any resources associated with the event registration and sets the observer to null.
    /// </summary>
    /// <remarks>
    /// Ensures that the event registration is properly disposed of and that the observer is no longer referenced.
    /// </remarks>
    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
        referencesEventRegistration?.Dispose();
        referencesObserver = null;
    }

    private sealed class ReferencesObserver : IReferenceObserver
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReferencesObserver"/> class.
        /// Sets up the observer with the provided nodes, container, store, logger, and time settings,
        /// then processes all child nodes of the ranges node to create UI elements for each range.
        /// </summary>
        /// <param name="rangesNode">The node containing the ranges to be processed.</param>
        /// <param name="pens">The node containing the pens to be processed.</param>
        /// <param name="uiContainer">The container for the UI elements.</param>
        /// <param name="store">The store to hold data.</param>
        /// <param name="logger">The logger to log events.</param>
        /// <param name="localTime">A boolean indicating whether to use local time.</param>
        public ReferencesObserver(IUANode rangesNode, IUANode pens, Item uiContainer, Store store, DataLogger logger, bool localTime)
        {
            this.uiContainer = uiContainer;
            this.store = store;
            this.pens = pens;
            this.logger = logger;
            this.localTime = localTime;
            rangesNode.Children.ToList().ForEach(CreateRangeUI);
        }

        /// <summary>
        /// Adds a reference between two nodes and creates a range UI for the target node.
        /// </summary>
        /// <param name="sourceNode">The source node in the reference.</param>
        /// <param name="targetNode">The target node in the reference.</param>
        /// <param name="referenceTypeId">The type of reference being added.</param>
        /// <param name="senderId">The ID of the sender node.</param>
        public void OnReferenceAdded(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
        {
            CreateRangeUI(targetNode);
        }

        /// <summary>
        /// Removes a UI range associated with a target node.
        /// </summary>
        /// <param name="sourceNode">The source node in the reference.</param>
        /// <param name="targetNode">The target node from which the UI range is removed.</param>
        /// <param name="referenceTypeId">The type of reference being removed.</param>
        /// <param name="senderId">The ID of the sender object.</param>
        public void OnReferenceRemoved(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
        {
            var uiRange = uiContainer.Get(targetNode.NodeId.Id.ToString());
            uiRange?.Delete();
        }

        /// <summary>
        /// Creates a time range UI based on the provided IUANode.
        /// Calculates the time span, retrieves statistics for each column,
        /// and creates a UI object to display the trend range.
        /// </summary>
        /// <param name="rangeNode">The IUANode representing the range to process.</param>
        private void CreateRangeUI(IUANode rangeNode)
        {
            Log.Debug("TrendRangesLogic", "Adding " + rangeNode.BrowseName);
            // Extract data from all the ranges
            TimeRange range = (TimeRange)(rangeNode as IUAVariable).Value.Value;
            var trendTimeRange = InformationModel.MakeObject<TrendTimeRange>("TimeRange");
            trendTimeRange.StartTime = range.StartTime;
            trendTimeRange.EndTime = range.EndTime;
            var timeSpan = range.EndTime - range.StartTime;
            trendTimeRange.TimeSpan = timeSpan.TotalMilliseconds;
            var pensAndColumns = GetPenAndColumns();
            // Get name from the DataBase table, either if defined by the user (if defined) or the logger name
            var tableName = string.IsNullOrEmpty(logger.TableName) ? logger.BrowseName : logger.TableName;
            // Populate range data for each pen
            pensAndColumns.ForEach(p =>
            {
                var rangeStatistics = InformationModel.MakeObject<RangeStatistics>(p.column);
                var stats = GetFromStore(p.column, range.StartTime, range.EndTime, tableName, localTime);
                if (stats == null)
                    return;
                rangeStatistics.Avg = stats.Value.Avg;
                rangeStatistics.Min = stats.Value.Min;
                rangeStatistics.Max = stats.Value.Max;
                rangeStatistics.Pen = p.pen.NodeId;
                trendTimeRange.Get("Statistics").Add(rangeStatistics);
            });

            var rangeUI = InformationModel.MakeObject<AdvancedTrendRangeUI>(rangeNode.NodeId.Id.ToString());
            rangeUI.Add(trendTimeRange);
            rangeUI.GetVariable("Range").Value = trendTimeRange.NodeId;
            uiContainer.Add(rangeUI);
        }

        private struct Statistics
        {
            public double Avg;
            public double Min;
            public double Max;
        }

        private struct PenColumn
        {
            public TrendPen pen;
            public string column;
        }

        /// <summary>
        /// Retrieves the pen and column information from the trend object.
        /// Iterates through the children of the pens node, looking for TrendPen objects and their associated DynamicLink objects.
        /// For each DynamicLink, checks if the pointed variable is "LastValue",
        /// and if so, creates a PenColumn object with the pen and column information.
        /// </summary>
        /// <returns>
        /// A list of PenColumn objects containing the pen and column information.
        /// </returns>
        private List<PenColumn> GetPenAndColumns()
        {
            var penColumns = new List<PenColumn>();
            pens.Children.OfType<FTOptix.UI.TrendPen>().ToList().ForEach((pen) =>
            {
                pen.Children.OfType<FTOptix.CoreBase.DynamicLink>().ToList().ForEach(dynamicLink =>
                {
                    var pointedVar = dynamicLink.Refs.GetVariable(FTOptix.Core.ReferenceTypes.Resolves);
                    if (pointedVar?.BrowseName == "LastValue")
                    {
                        PenColumn penColumn = new()
                        {
                            pen = pen,
                            column = pointedVar.Owner.BrowseName
                        };
                        Console.WriteLine(pointedVar.Owner.BrowseName);
                        penColumns.Add(penColumn);
                    }
                });
            });
            return penColumns;
        }

        /// <summary>
        /// Retrieves statistics from the store for a given column and time range.
        /// Constructs a SQL query to calculate the average, maximum, and minimum values for the specified column
        /// within the given time range. If the query fails, attempts to switch between "Timestamp" and "LocalTimestamp" columns.
        /// </summary>
        /// <param name="column">The column name to retrieve statistics for.</param>
        /// <param name="start">The start time of the range.</param>
        /// <param name="end">The end time of the range.</param>
        /// <param name="tableName">The name of the table to query.</param>
        /// <param name="isLocalTime">Indicates whether to use local time.</param>
        /// <returns>
        /// A Statistics object containing the average, maximum, and minimum values for the specified column,
        /// or null if the query fails.
        /// </returns>
        private Statistics? GetFromStore(string column, DateTime start, DateTime end, string tableName, bool isLocalTime = true)
        {
            // We get the column name from the current setting of the DataLogger
            // but if the project was made with a very old version of FT Optix
            // a fallback is used (no LocalTimestamp was logged previously) 
            string localTimestampColumnName = "LocalTimestamp";
            string timestampColumnName = "Timestamp";
            string timeStampColumn = isLocalTime ? localTimestampColumnName : timestampColumnName;
            string[] header;
            object[,] output;

            try
            {
                string query = $"SELECT AVG(\"{column}\"), MAX(\"{column}\"), MIN(\"{column}\") FROM \"{tableName}\" WHERE \"{timeStampColumn}\" BETWEEN \"{start.ToString("o", CultureInfo.InvariantCulture)}\" AND \"{end.ToString("o", CultureInfo.InvariantCulture)}\"";
                store.Query(query, out header, out output);
            }
            catch
            {
                try
                {
                    if (timeStampColumn == localTimestampColumnName)
                    {
                        timeStampColumn = timestampColumnName;
                    }
                    else
                    {
                        timeStampColumn = localTimestampColumnName;
                    }
                    string query = $"SELECT AVG(\"{column}\"), MAX(\"{column}\"), MIN(\"{column}\") FROM \"{tableName}\" WHERE \"{timeStampColumn}\" BETWEEN \"{start.ToString("o", CultureInfo.InvariantCulture)}\" AND \"{end.ToString("o", CultureInfo.InvariantCulture)}\"";
                    store.Query(query, out header, out output);
                }
                catch
                {
                    Log.Error("TrendRangesLogic.GetFromStore", "Cannot determine Timestamp/LocalTimestamp column from store");
                    return null;
                }
            }

            if (output.Length == 3 &&
                output[0, 0] != null &&
                output[0, 1] != null &&
                output[0, 2] != null)
            {
                Statistics statistics = new()
                {
                    Avg = Convert.ToDouble(output[0, 0], CultureInfo.InvariantCulture),
                    Max = Convert.ToDouble(output[0, 1], CultureInfo.InvariantCulture),
                    Min = Convert.ToDouble(output[0, 2], CultureInfo.InvariantCulture)
                };
                return statistics;
            }
            return null;
        }

        private readonly Item uiContainer;
        private readonly Store store;
        private readonly IUANode pens;
        private readonly DataLogger logger;
        private readonly bool localTime;
    }

    private ReferencesObserver referencesObserver;
    private IEventRegistration referencesEventRegistration;
}
