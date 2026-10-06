using Autodesk.Revit.UI;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace RevitWallChecker
{
    [Guid("B703865E-39F2-4031-888D-08A4BD02D304")]
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // ============================================================
                // CREATE RIBBON PANEL
                // ============================================================
                RibbonPanel panel =
                    application.CreateRibbonPanel("Revit Wall Checker");

                // Get the DLL location
                string assemblyPath =
                    Assembly.GetExecutingAssembly().Location;

                // ============================================================
                // CREATE WALL CHECKER BUTTON
                // ============================================================
                PushButtonData buttonData = new PushButtonData(
                    "WallQualityChecker",
                    "Wall\nChecker",
                    assemblyPath,
                    "RevitWallChecker.Commands.WallCheckerCommand");

                PushButton button =
                    panel.AddItem(buttonData) as PushButton;

                if (button != null)
                {
                    // ========================================================
                    // BUTTON TOOLTIP
                    // ========================================================
                    button.ToolTip =
                        "Run the wall-only quality checker.";

                    button.LongDescription =
                        "Checks walls in the active Revit model for parameter integrity, " +
                        "wall types, duplicate geometry, connectivity, levels, worksets, " +
                        "materials, coordinates and wall-related Revit warnings.";

                    // ========================================================
                    // LOAD RIBBON ICONS
                    // ========================================================

                    // Small icon - 16 x 16
                    BitmapImage smallIcon =
                        LoadIcon("WallQualityChecker_16.png");

                    // Large icon - 32 x 32
                    BitmapImage largeIcon =
                        LoadIcon("WallQualityChecker_32.png");

                    // ========================================================
                    // ASSIGN ICONS
                    // ========================================================
                    if (smallIcon != null)
                    {
                        button.Image = smallIcon;
                    }

                    if (largeIcon != null)
                    {
                        button.LargeImage = largeIcon;
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show(
                    "Revit Wall Checker",
                    "Failed to create the Wall Checker ribbon button.\n\n" +
                    ex.Message);

                return Result.Failed;
            }
        }


        // ================================================================
        // LOAD ICON FROM ASSETS FOLDER
        // ================================================================
        private BitmapImage LoadIcon(string fileName)
        {
            try
            {
                // Get folder containing RevitWallChecker.dll
                string assemblyFolder =
                    Path.GetDirectoryName(
                        Assembly.GetExecutingAssembly().Location);

                // Build path:
                // DLL folder\Assets\icon.png
                string iconPath =
                    Path.Combine(
                        assemblyFolder,
                        "Assets",
                        fileName);

                // Check whether icon exists
                if (!File.Exists(iconPath))
                {
                    TaskDialog.Show(
                        "Wall Checker Icon",
                        "Icon not found:\n\n" + iconPath);

                    return null;
                }

                // Create BitmapImage
                BitmapImage image =
                    new BitmapImage();

                image.BeginInit();

                image.UriSource =
                    new Uri(
                        iconPath,
                        UriKind.Absolute);

                // Load the image completely into memory
                image.CacheOption =
                    BitmapCacheOption.OnLoad;

                image.EndInit();

                // Make it safe for Revit ribbon usage
                image.Freeze();

                return image;
            }
            catch (Exception ex)
            {
                TaskDialog.Show(
                    "Wall Checker Icon",
                    "Unable to load icon:\n\n" +
                    fileName +
                    "\n\n" +
                    ex.Message);

                return null;
            }
        }


        // ================================================================
        // SHUTDOWN
        // ================================================================
        public Result OnShutdown(
            UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}