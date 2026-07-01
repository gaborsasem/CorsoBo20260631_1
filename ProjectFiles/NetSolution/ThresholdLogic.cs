#region Using directives

using FTOptix.Core;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.UI;
using System;
using System.Linq;
using UAManagedCore;
using FTOptix.DataLogger;
using FTOptix.Store;
using FTOptix.SQLiteStore;
using FTOptix.WebUI;
using FTOptix.Recipe;
using FTOptix.ODBCStore;
using FTOptix.RecipeX;
using FTOptix.OPCUAServer;

#endregion

public class ThresholdLogic : BaseNetLogic
{
    /// <summary>
    /// Sets up event observers for reference changes in the Thresholds object.
    /// Registers an observer to track additions and removals of forward references
    /// in the "Accordion1/Content/Container" item.
    /// </summary>
    /// <remarks>
    /// Initializes the <see cref="thresholds"/> property with an instance of Thresholds.
    /// Creates a new <see cref="ReferencesObserver"/> and registers it
    /// with the <see cref="thresholds"/> object, listening for forward reference
    /// events in the specified container.
    /// </remarks>
    public override void Start()
    {
        // Insert code to be executed when the user-defined logic is started
        thresholds = LogicObject.GetAlias("Thresholds");
        referencesObserver = new ReferencesObserver(thresholds, LogicObject.Owner.Get<FTOptix.UI.Item>("Accordion1/Content/Container"));

        referencesEventRegistration = thresholds.RegisterEventObserver(
            referencesObserver, EventType.ForwardReferenceAdded | EventType.ForwardReferenceRemoved);
    }

    /// <summary>
    /// Stops the operation and disposes of the event registration if it exists.
    /// </summary>
    /// <remarks>
    /// Releases any resources associated with the event registration and sets the observer to <c>null</c>.
    /// </remarks>
    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
        referencesEventRegistration?.Dispose();
        referencesObserver = null;
    }

    /// <summary>
    /// Creates a new threshold object with a random color and thickness,
    /// and adds it to the thresholds collection.
    /// </summary>
    /// <remarks>
    /// The threshold is created with a unique identifier based on the current count,
    /// and has a thickness of 1 pixel. The color is randomly generated with RGB values.
    /// </remarks>
    [ExportMethod]
    public void AddThreshold()
    {
        var threshold = InformationModel.MakeObject<FTOptix.UI.TrendThreshold>("Threshold" + count++);
        threshold.Color = new Color(255, (byte)randomNumber.Next(0, 255), (byte)randomNumber.Next(0, 255), (byte)randomNumber.Next(0, 255));
        threshold.Thickness = 1;
        thresholds.Add(threshold);
    }

    private sealed class ReferencesObserver : IReferenceObserver
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReferencesObserver"/> class.
        /// </summary>
        /// <param name="thresholdsNode">The node containing threshold definitions.</param>
        /// <param name="uiContainer">The UI container to which threshold UI elements are added.</param>
        public ReferencesObserver(IUANode thresholdsNode, Item uiContainer)
        {
            this.uiContainer = uiContainer;
            thresholdsNode.Children.ToList().ForEach(CreateThresholdUI);
        }

        /// <summary>
        /// Adds a reference between two nodes and creates a threshold UI for the target node.
        /// </summary>
        /// <param name="sourceNode">The source node in the reference.</param>
        /// <param name="targetNode">The target node in the reference.</param>
        /// <param name="referenceTypeId">The type of reference being added.</param>
        /// <param name="senderId">The identifier of the sender node.</param>
        public void OnReferenceAdded(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
        {
            CreateThresholdUI(targetNode);
        }

        /// <summary>
        /// Removes a UI threshold for a given node when a reference is removed.
        /// </summary>
        /// <param name="sourceNode">The source node in the reference.</param>
        /// <param name="targetNode">The target node being referenced.</param>
        /// <param name="referenceTypeId">The type of reference being removed.</param>
        /// <param name="senderId">The sender identifier for the reference.</param>
        public void OnReferenceRemoved(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
        {
            var uiThreshold = uiContainer.Get(targetNode.BrowseName);
            uiThreshold?.Delete();
        }

        /// <summary>
        /// Creates an AdvancedTrendThresholdUI object and adds it to the UI container based on the provided IUANode.
        /// </summary>
        /// <param name="thresholdNode">The IUANode representing the threshold to be configured.</param>
        private void CreateThresholdUI(IUANode thresholdNode)
        {
            Log.Debug("ThresholdLogic", "Adding: " + thresholdNode.BrowseName);
            var thresholdUI = InformationModel.MakeObject<AdvancedTrendThresholdUI>(thresholdNode.BrowseName);
            thresholdUI.GetVariable("Threshold").Value = thresholdNode.NodeId;
            uiContainer.Add(thresholdUI);
        }

        private readonly Item uiContainer;
    }

    /// <summary>
    /// Counter for creating unique threshold identifiers.
    /// </summary>
    private int count = 0;

    /// <summary>
    /// The thresholds node containing threshold definitions.
    /// </summary>
    private IUANode thresholds;

    /// <summary>
    /// Random number generator for creating random threshold colors.
    /// </summary>
    private readonly Random randomNumber = new Random();

    /// <summary>
    /// Observer for monitoring changes to threshold references.
    /// </summary>
    private ReferencesObserver referencesObserver;

    /// <summary>
    /// Event registration for threshold reference changes.
    /// </summary>
    private IEventRegistration referencesEventRegistration;
}
