#region Using directives

using FTOptix.Core;
using FTOptix.CoreBase;
using FTOptix.DataLogger;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.UI;
using System;
using System.Linq;
using UAManagedCore;
using FTOptix.Store;
using FTOptix.SQLiteStore;
using FTOptix.WebUI;
using System.Threading;
using FTOptix.Recipe;
using FTOptix.ODBCStore;
using FTOptix.RecipeX;
using FTOptix.OPCUAServer;

#endregion

public class AdvancedTrendLogic : BaseNetLogic
{
    /// <summary>
    /// Initializes the advanced trend logic by importing pens from the configured store.
    /// </summary>
    public override void Start()
    {
        ImportPensFromStore();
    }

    /// <summary>
    /// Stops the advanced trend logic. No cleanup required.
    /// </summary>
    public override void Stop()
    {
        // Nothing to do here
    }

    /// <summary>
    /// Imports pens from a Store to the trend.
    /// Retrieves the list of logged variables from the Store and adds them as pens to the trend.
    /// </summary>
    [ExportMethod]
    public void ImportPensFromStore()
    {
        // Insert code to be executed by the method
        var myTrend = Owner.Get<FTOptix.UI.Trend>("TrendPanel/MainTrend");
        if (myTrend == null)
        {
            Log.Error("AdvancedTrendLogic", "Cannot get to the main trend, if the widget was tampered, make sure to restore a working path");
            return;
        }
        // Check if a Store was configured as source
        var sourceLogger = (FTOptix.DataLogger.DataLogger)LogicObject.Context.ResolvePath(myTrend.Get("Model"), myTrend.GetVariable("Model/DynamicLink").Value).ResolvedNode ?? null;
        if (sourceLogger == null)
        {
            Log.Error("AdvancedTrendLogic", "Cannot find a valid logger, make sure you configured the Alias at the widget root");
            return;
        }
        // Get the list of the logged variables
        var loggerVariables = sourceLogger.VariablesToLog.OfType<FTOptix.DataLogger.VariableToLog>();
        if (loggerVariables.ToList().Count < 1)
        {
            Log.Error("AdvancedTrendLogic", "Cannot find any variable in " + sourceLogger.BrowseName);
            return;
        }
        // Remove all existing pens from trend
        myTrend.Pens.Clear();
        
        foreach (var (srcVar, newPen) in
        // Add the new pens to the trend
        from VariableToLog loggerVariable in loggerVariables
        let newPen = InformationModel.Make<FTOptix.UI.TrendPen>(loggerVariable.BrowseName)
            select (loggerVariable, newPen))
            {
                newPen.Color = new Color(0xFF, (byte)randomNumber.Next(256), (byte)randomNumber.Next(256), (byte)randomNumber.Next(256));
                var dynamicLinkTarget = sourceLogger.GetVariable("VariablesToLog/" + srcVar.BrowseName + "/LastValue");
                newPen.SetDynamicLink(dynamicLinkTarget, DynamicLinkMode.ReadWrite);
                myTrend.Pens.Add(newPen);
            }

        Log.Debug("AdvancedTrendLogic", "Pens were added successfully");
    }

    /// <summary>
    /// Random number generator used for creating random pen colors.
    /// </summary>
    private static readonly Random randomNumber = new Random();
}
