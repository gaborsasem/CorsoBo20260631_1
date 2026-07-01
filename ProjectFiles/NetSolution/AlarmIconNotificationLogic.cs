#region Using directives
using System;
using CoreBase = FTOptix.CoreBase;
using FTOptix.HMIProject;
using UAManagedCore;
using System.Linq;
using UAManagedCore.Logging;
using FTOptix.NetLogic;
using FTOptix.Core;
using FTOptix.SQLiteStore;
using FTOptix.Store;
using FTOptix.EventLogger;
using FTOptix.Modbus;
using FTOptix.CommunicationDriver;
using FTOptix.OmronFins;
using FTOptix.OmronEthernetIP;
using FTOptix.S7TiaProfinet;
using FTOptix.DataLogger;
using FTOptix.Recipe;
using FTOptix.ODBCStore;
using FTOptix.RecipeX;
using FTOptix.OPCUAServer;
#endregion

public class AlarmIconNotificationLogic : BaseNetLogic
{
	public override void Start()
	{
		var context = LogicObject.Context;
		affinityId = context.AssignAffinityId();

		RegisterObserverOnLocalizedAlarmsContainer(context);
		RegisterObserverOnSessionLocaleIdChanged(context);
		RegisterObserverOnLocalizedAlarmsObject(context);
	}

	public override void Stop()
	{
		if (alarmEventRegistration != null)
			alarmEventRegistration.Dispose();
		if (alarmEventRegistration2 != null)
			alarmEventRegistration2.Dispose();

		alarmEventRegistration = null;
		alarmEventRegistration2 = null;
		alarmsNotificationObserver = null;
		retainedAlarmsObjectObserver = null;
	}

	/// <summary>
	/// Registers an observer for localized alarms in the retained alarms object.
	/// </summary>
	/// <param name="context"> The context in which the observer is registered.</param>
	public void RegisterObserverOnLocalizedAlarmsObject(IContext context)
	{
		var retainedAlarms = context.GetNode(FTOptix.Alarm.Objects.RetainedAlarms);

		retainedAlarmsObjectObserver = new RetainedAlarmsObjectObserver((ctx) => RegisterObserverOnLocalizedAlarmsContainer(ctx));

		// observe ReferenceAdded of localized alarm containers
		alarmEventRegistration2 = retainedAlarms.RegisterEventObserver(
			retainedAlarmsObjectObserver, EventType.ForwardReferenceAdded, affinityId);
	}

	/// <summary>
	/// Registers an observer for localized alarms in the context.
	/// </summary>
	/// <param name="context">The context in which the alarm is being registered.</param>
	/// <returns>
	/// No return value.
	/// </returns>
	/// <remarks>
	/// The method first disposes of any existing alarm event registration, then creates a new observer
	/// and registers it to monitor both forward reference additions and removals of alarms.
	/// </remarks>
	public void RegisterObserverOnLocalizedAlarmsContainer(IContext context)
	{
		var retainedAlarms = context.GetNode(FTOptix.Alarm.Objects.RetainedAlarms);
		var localizedAlarmsVariable = retainedAlarms.GetVariable("LocalizedAlarms");
		var localizedAlarmsContainer = context.GetNode((NodeId)localizedAlarmsVariable.GetValue());

		if (alarmEventRegistration != null)
		{
			alarmEventRegistration.Dispose();
			alarmEventRegistration = null;
		}

		alarmsNotificationObserver = new AlarmsNotificationObserver(LogicObject);
		alarmsNotificationObserver.Initialize();

		alarmEventRegistration = localizedAlarmsContainer.RegisterEventObserver(
			alarmsNotificationObserver,
			EventType.ForwardReferenceAdded | EventType.ForwardReferenceRemoved, affinityId);
	}

	/// <summary>
	/// Registers an observer for changes to the session locale ID.
	/// </summary>
	/// <param name="context">The context in which the observer is registered.</param>
	public void RegisterObserverOnSessionLocaleIdChanged(IContext context)
	{
		var currentSessionLocaleIdVariable = context.Sessions.CurrentSessionInfo.SessionObject.Children["ActualLocaleId"];

		localeIdChangedObserver = new CallbackVariableChangeObserver((IUAVariable variable, UAValue newValue, UAValue oldValue, ElementAccess a, ulong aa) =>
		{
			RegisterObserverOnLocalizedAlarmsContainer(context);
		});

		localeIdRegistration = currentSessionLocaleIdVariable.RegisterEventObserver(
			localeIdChangedObserver, EventType.VariableValueChanged, affinityId);
	}

	private class RetainedAlarmsObjectObserver : IReferenceObserver
	{
		/// <summary>
		/// This method initializes a retained alarms object observer with a callback action.
		/// The callback action is used to handle alarm events.
		/// </summary>
		/// <param name="action">The action to be executed when alarms are triggered.</param>
		/// <returns>
		/// A RetainedAlarmsObjectObserver instance, which is used to observe and trigger alarms.
		/// </returns>
		public RetainedAlarmsObjectObserver(Action<IContext> action)
		{
			registrationCallback = action;
		}

		/// <summary>
		/// This method handles the addition of a reference to a node, checking if the target node's browse name matches the current session's locale.
		/// If it does, it triggers the registration callback for the node.
		/// </summary>
		/// <param name="sourceNode">The source node in the reference.</param>
		/// <param name="targetNode">The target node being referenced.</param>
		/// <param name="referenceTypeId">The type of reference being added.</param>
		/// <param name="senderId">The sender identifier for the reference.</param>
		/// <remarks>
		/// The method sets the locale ID to "en-US" if no locale is set in the target node's context.
		/// </remarks>
		public void OnReferenceAdded(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
		{
			string localeId = targetNode.Context.Sessions.CurrentSessionHandler.ActualLocaleId;
			if (String.IsNullOrEmpty(localeId))
				localeId = "en-US";

			if (targetNode.BrowseName == localeId)
				registrationCallback(targetNode.Context);
		}

		/// <summary>
		/// This method is called when a reference is removed. It takes three parameters: the source node, the target node, the type of reference, and the sender ID. The method does not return any value.
		/// </summary>
		/// <param name="sourceNode">The source node in the reference.</param>
		/// <param name="targetNode">The target node in the reference.</param>
		/// <param name="referenceTypeId">The type of reference being removed.</param>
		/// <param name="senderId">The ID of the sender of the reference.</param>
		public void OnReferenceRemoved(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
		{
		}

		private Action<IContext> registrationCallback;
	}

	private class AlarmsNotificationObserver : IReferenceObserver
	{
		/// <summary>
		/// This method initializes the <see cref="logicNode"/> property with the provided <see cref="IUANode" />.
		/// </summary>
		/// <param name="logicNode">The IUANode instance to be assigned to the logicNode property.</param>
		/// <remarks>
		/// The method is used to set up the logic node for further processing or interaction.
		/// </remarks>
		public AlarmsNotificationObserver(IUANode logicNode)
		{
			this.logicNode = logicNode;
		}

		/// <summary>
		/// This method initializes the alarm-related variables by retrieving the number of retained alarms and the last alarm node from the logic node.
		/// It also retrieves the localized alarms container and processes its children to set the retained alarms count and last alarm node.
		/// </summary>
		/// <remarks>
		/// - Retrieves the "AlarmCount" and "LastAlarm" variables from the logic node.
		/// - Retrieves the "RetainedAlarms" node and its "LocalizedAlarms" variable.
		/// - If the localized alarms container is found, it sets the retained alarms count to the number of children and the last alarm node to the last child's node.
		/// - If no localized alarms container is found, it sets both values to default (0 and empty node).
		/// </remarks>
		public void Initialize()
		{
			retainedAlarmsCount = logicNode.GetVariable("AlarmCount");
			lastAlarm = logicNode.GetVariable("LastAlarm");

			IContext context = logicNode.Context;
			var retainedAlarms = context.GetNode(FTOptix.Alarm.Objects.RetainedAlarms);
			var localizedAlarmsVariable = retainedAlarms.GetVariable("LocalizedAlarms");
			var localizedAlarmsNodeId = (NodeId)localizedAlarmsVariable.Value;
			IUANode localizedAlarmsContainer = null;
			if (localizedAlarmsNodeId != null && !localizedAlarmsNodeId.IsEmpty)
				localizedAlarmsContainer = context.GetNode(localizedAlarmsNodeId);

			if (localizedAlarmsContainer == null)
			{
				retainedAlarmsCount.Value = 0;
				lastAlarm.Value = NodeId.Empty;
				return;
			}

			var children = localizedAlarmsContainer.Children.ToArray();
			retainedAlarmsCount.Value = children.Length;
			if (children.Any())
				lastAlarm.Value = children.Last()?.NodeId ?? NodeId.Empty;
			else
				lastAlarm.Value = NodeId.Empty;
		}

		/// <summary>
		/// This method increments the retained alarms count and records the target node ID in the last alarm.
		/// </summary>
		/// <param name="sourceNode">The source node being referenced.</param>
		/// <param name="targetNode">The target node being referenced.</param>
		/// <param name="referenceTypeId">The type of reference being added.</param>
		/// <param name="senderId">The sender identifier.</param>
		/// <returns>
		/// No return value is returned.
		/// </returns>
		public void OnReferenceAdded(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
		{
			++retainedAlarmsCount.Value;

			lastAlarm.Value = targetNode.NodeId;
		}

		/// <summary>
		/// This method handles the removal of a reference and updates the retained alarms count and last alarm node.
		/// </summary>
		/// <param name="sourceNode">The source node being removed.</param>
		/// <param name="targetNode">The target node to which the reference is being removed.</param>
		/// <param name="referenceTypeId">The type of reference being removed.</param>
		/// <param name="senderId">The sender identifier for the reference.</param>
		/// <returns>
		/// No return value (void).
		/// </returns>
		public void OnReferenceRemoved(IUANode sourceNode, IUANode targetNode, NodeId referenceTypeId, ulong senderId)
		{
			IContext context = logicNode.Context;
			var retainedAlarms = context.GetNode(FTOptix.Alarm.Objects.RetainedAlarms);
			var localizedAlarmsVariable = retainedAlarms.GetVariable("LocalizedAlarms");
			var localizedAlarmsNodeId = (NodeId)localizedAlarmsVariable.Value;
			IUANode localizedAlarmsContainer = null;
			if (localizedAlarmsNodeId != null && !localizedAlarmsNodeId.IsEmpty)
				localizedAlarmsContainer = context.GetNode(localizedAlarmsNodeId);

			if (localizedAlarmsContainer == null)
			{
				retainedAlarmsCount.Value = 0;
				lastAlarm.Value = NodeId.Empty;
				return;
			}

			var children = localizedAlarmsContainer.Children.ToArray();
			retainedAlarmsCount.Value = children.Length;
			if (children.Any())
				lastAlarm.Value = children.Last()?.NodeId ?? NodeId.Empty;
			else
				lastAlarm.Value = NodeId.Empty;
		}

		private IUAVariable retainedAlarmsCount;
		private IUAVariable lastAlarm;
		private IUANode logicNode;
	}

	private uint affinityId = 0;
	private AlarmsNotificationObserver alarmsNotificationObserver;
	private RetainedAlarmsObjectObserver retainedAlarmsObjectObserver;
	private IEventRegistration alarmEventRegistration;
	private IEventRegistration alarmEventRegistration2;
	private IEventRegistration localeIdRegistration;
	private IEventObserver localeIdChangedObserver;
}
