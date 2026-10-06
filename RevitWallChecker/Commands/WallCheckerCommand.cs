using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitWallChecker.UI;

namespace RevitWallChecker.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class WallCheckerCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;

            if (uiDoc == null || uiDoc.Document == null)
            {
                message = "No active Revit document was found.";
                return Result.Failed;
            }

            // Reuse the existing Wall Quality Checker window
            // instead of creating a new window every time.
            MainWindow.ShowWindow(
                uiDoc.Document,
                uiDoc);

            return Result.Succeeded;
        }
    }
}