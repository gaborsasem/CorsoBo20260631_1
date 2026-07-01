#region Using directives

using FTOptix.Core;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.UI;
using System;
using UAManagedCore;
using FTOptix.DataLogger;
using FTOptix.Store;
using FTOptix.SQLiteStore;
using FTOptix.WebUI;
using FTOptix.Recipe;
using FTOptix.ODBCStore;
using FTOptix.RecipeX;
using FTOptix.OPCUAServer;
using OpcUa = UAManagedCore.OpcUa;

#endregion

public class TrendPensLogic : BaseNetLogic
{
    /// <summary>
    /// Initializes the trend logic and starts the startup process.
    /// If the main trend cannot be retrieved, logs an error and returns.
    /// If the trend exists, checks if it has any pens. If not, schedules a delayed task to run the startup logic after 1 second.
    /// Otherwise, directly calls the startup logic.
    /// </summary>
    public override void Start()
    {
        myTrend = Owner.Owner.Owner.Owner.Owner.Get<FTOptix.UI.Trend>("TrendPanel/MainTrend");
        if (myTrend == null)
        {
            Log.Error("TrendRangesLogic", "Cannot get to the main trend, if the widget was tampered, make sure to restore a working path");
            return;
        }
        if (myTrend.Pens.Count == 0)
        {
            // Handle the case where the graph is being generated
            StartupTask = new DelayedTask(StartupLogic, 1000, LogicObject);
            StartupTask.Start();
        }
        else
        {
            // Handle the case where the trend is already in there
            StartupLogic();
        }
    }

    /// <summary>
    /// Stops the current operation and disposes of related resources.
    /// </summary>
    /// <remarks>
    /// This method disposes of the <see cref="referencesEventRegistration"/> resource, sets <see cref="referencesObserver"/> to null, and disposes of <see cref="StartupTask"/>.
    /// </remarks>
    public override void Stop()
    {
        // Stop the pens observer
        referencesEventRegistration?.Dispose();
        referencesObserver = null;
        // Stop the delayed task (if any)
        StartupTask?.Dispose();
    }

    /// <summary>
    /// Handles the startup logic for plotting pens. Checks if there are any pens to plot and, if so, sets up an observer to track changes in the pens. Also registers an event observer for changes in the pens.
    /// </summary>
    /// <remarks>
    /// Checks if there are any pens in the trend. If there are none, logs a debug message and returns immediately. If pens exist, creates a ReferencesObserver to monitor changes and registers an event observer for forward reference additions and removals.
    /// </remarks>
    private void StartupLogic()
    {
        // Runtime creation of the different pens data
        if (myTrend.Pens.Count == 0)
        {
            Log.Debug("TrendPensLogic", "No pens to plot");
            return;
        }
        referencesObserver = new ReferencesObserver(myTrend.Pens, LogicObject.Owner.Get<FTOptix.UI.Item>("ScrollView1/Container"));

        referencesEventRegistration = myTrend.Get("Pens").RegisterEventObserver(
            referencesObserver, EventType.ForwardReferenceAdded | EventType.ForwardReferenceRemoved);
    }

    /// <summary>
    /// Creates a new pen with random color and thickness, adds it to a trend, and associates it with a variable.
    /// </summary>
    /// <remarks>
    /// The pen is initialized with a random color (RGB values between 0 and 255) and a fixed thickness of 3.
    /// The pen is added to the trend, and a dynamic link is established between the pen and the variable.
    /// </remarks>
    [ExportMethod]
    public void AddPen()
    {
        // Add a new pen at runtime
        var pen = InformationModel.MakeVariable<FTOptix.UI.TrendPen>("Pen" + count, OpcUa.DataTypes.Float);
        var variable = InformationModel.MakeVariable("Variable" + count, OpcUa.DataTypes.Float);
        pen.Color = new Color(255, (byte)randomNumber.Next(0, 255), (byte)randomNumber.Next(0, 255), (byte)randomNumber.Next(0, 255));
        pen.Thickness = 3;
        myTrend.Pens.Add(pen);
        Project.Current.Get("Model/RuntimeAdded").Add(variable);
        pen.SetDynamicLink(variable);
        count++;
    }

    private sealed class ReferencesObserver : IReferenceObserver
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReferencesObserver"/> class.
        /// </summary>
        /// <param name="pensNode">The collection of trend pens to initialize.</param>
        /// <param name="uiContainer">The container item to hold the UI elements.</param>
        /// <remarks>
        /// Iterates through each trend pen in the collection and calls <see cref="CreatePenUI"/> to initialize each one.
        /// </remarks>
        public ReferencesObserver(UAManagedCore.PlaceholderChildNodeCollection<FTOptix.UI.TrendPen> pensNode, Item uiContainer)
        {
            this.uiContainer = uiContainer;
            foreach (var trendPen in pensNode)
            {
                // Create pens that are defined in the DataLogger
                CreatePenUI(trendPen, false);
            }
        }

        /// <summary>
        /// Adds a reference between two nodes, using the specified reference type and sender ID.
        /// </summary>
        /// <param name="sourceNode">The source node in the reference.</param>
        /// <param name="targetNode">The target node in the reference.</param>
        /// <param name="referenceTypeId">The type of reference being added.</param>
        /// <param name="senderId">The ID of the sender.</param>
        public void OnReferenceAdded(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
        {
            // If a new pen is added at runtime to the trend, we need to add a line
            CreatePenUI(targetNode, true);
        }

        /// <summary>
        /// Removes a UI threshold associated with the target node.
        /// </summary>
        /// <param name="sourceNode">The source node in the reference relationship.</param>
        /// <param name="targetNode">The target node for which the UI threshold is being removed.</param>
        /// <param name="referenceTypeId">The type of reference being removed.</param>
        /// <param name="senderId">The identifier of the sender entity.</param>
        public void OnReferenceRemoved(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
        {
            // Remove the line if the pen is removed
            var uiThreshold = uiContainer.Get(targetNode.BrowseName);
            if (uiThreshold != null)
                uiThreshold.Delete();
        }

        /// <summary>
        /// Adds a pen UI element to the UI container based on the provided node and whether it was created at runtime.
        /// </summary>
        /// <param name="penNode">The node representing the pen to be added.</param>
        /// <param name="runtimeCreated">A boolean indicating whether the pen was created at runtime.</param>
        /// <remarks>
        /// The method logs the addition of the pen and initializes the UI with the node's ID and runtime creation status.
        /// </remarks>
        private void CreatePenUI(IUANode penNode, bool runtimeCreated = false)
        {
            // Add pens to the UI container
            Log.Debug("TrendPensLogic", "Adding " + penNode.BrowseName);
            var penUI = InformationModel.MakeObject<AdvancedTrendPenUI>(penNode.BrowseName);
            penUI.GetVariable("Pen").Value = penNode.NodeId;
            // Check if the pen was added at runtime (it is not in the DataLogger)
            penUI.GetVariable("RuntimeCreated").Value = runtimeCreated;
            uiContainer.Add(penUI);
        }

        private readonly Item uiContainer;
    }

    /// <summary>
    /// The main trend object for the logic.
    /// </summary>
    private Trend myTrend;

    /// <summary>
    /// Counter for creating unique pen and variable names.
    /// </summary>
    private int count = 0;

    /// <summary>
    /// Random number generator for creating random pen colors.
    /// </summary>
    private readonly Random randomNumber = new Random();

    /// <summary>
    /// Observer for monitoring changes to trend pens.
    /// </summary>
    private ReferencesObserver referencesObserver;

    /// <summary>
    /// Event registration for pen reference changes.
    /// </summary>
    private IEventRegistration referencesEventRegistration;

    /// <summary>
    /// Delayed task for startup logic execution.
    /// </summary>
    private DelayedTask StartupTask;
}
