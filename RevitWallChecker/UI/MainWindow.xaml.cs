using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitWallChecker.Config;
using RevitWallChecker.Models;
using RevitWallChecker.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace RevitWallChecker.UI
{
    public partial class MainWindow : Window
    {
        private static MainWindow _instance;

        private Document _doc;
        private UIDocument _uiDoc;
        private WallModelDiscovery _discovery;
        private readonly WallCheckerSettings _settings;
        private readonly List<WallCheckResult> _results = new List<WallCheckResult>();
        private WallJoinRepairHandler _joinRepairHandler;
        private ExternalEvent _joinRepairEvent;

        private int _selectedElementId = -1;

        public MainWindow(Document doc, UIDocument uiDoc)
        {
            InitializeComponent();

            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _discovery = new WallModelDiscovery(_doc);

            string configPath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                "Config",
                "WallCheckerSettings.json");

            _settings = WallCheckerSettings.Load(configPath);
            StatusText.Text = "Ready - wall-only checks are configured from the current model.";


            _joinRepairHandler = new WallJoinRepairHandler();
            _joinRepairEvent = ExternalEvent.Create(_joinRepairHandler);

            _joinRepairHandler.OnCompleted = summary =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    StatusText.Text = "Wall join repair completed.";

                    MessageBox.Show(
                                    summary,
                                    "Wall Join Repair Results",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);

                    // Re-run the existing checks after the repair.
                    RunCheckButton_Click(this, new RoutedEventArgs());
                }));

            };

        }

        public static void ShowWindow(Document doc, UIDocument uiDoc)
        {
            if (doc == null || uiDoc == null)
                return;

            if (_instance != null)
            {
                try
                {
                    if (_instance.IsLoaded)
                    {
                        _instance.UpdateDocument(doc, uiDoc);

                        if (_instance.WindowState == WindowState.Minimized)
                            _instance.WindowState = WindowState.Normal;

                        _instance.Show();
                        _instance.Activate();

                        _instance.Topmost = true;
                        _instance.Topmost = false;

                        return;
                    }
                }
                catch
                {
                    _instance = null;
                }
            }

            _instance = new MainWindow(doc, uiDoc);

            _instance.Closed += (sender, args) =>
            {
                _instance = null;
            };

            _instance.Show();
            _instance.Activate();
        }

        public void UpdateDocument(Document doc, UIDocument uiDoc)
        {
            if (doc == null || uiDoc == null)
                return;

            _doc = doc;
            _uiDoc = uiDoc;
            _discovery = new WallModelDiscovery(_doc);

            // Do not clear _results here. Existing scan results must remain visible.
            StatusText.Text = "Existing wall quality results restored.";
        }

        private void RunCheckButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RunCheckButton.IsEnabled = false;
                StatusText.Text = "Reading walls and running checks...";
                _results.Clear();

                IssueFilterComboBox.Items.Clear();
                IssueFilterComboBox.Items.Add("ALL ISSUES");
                IssueFilterComboBox.SelectedIndex = 0;

                List<Wall> walls = _discovery.GetWalls();
                ModelWallsText.Text = walls.Count.ToString();
                UpdateModelSummary();

                if (walls.Count == 0)
                {
                    ResultsGrid.ItemsSource = null;
                    UpdateIssueBreakdown();
                    StatusText.Text = "No walls were found in the active model.";
                    return;
                }

                // 1. Parameters and parameter integrity
                CheckRequiredParameters(walls);
                CheckConfiguredRequiredParameters(walls);

                // 2. Wall type / naming standard
                CheckWallTypes(walls);
                CheckConfiguredWallTypeStandard(walls);

                // 3. Duplicate / exact overlapping walls
                CheckDuplicateWalls(walls);

                // 4. Wall-end connectivity
                CheckUnconnectedWalls(walls);

                // 5. Levels / constraints / offsets
                CheckLevels(walls);

                // 6. Worksets
                CheckWorksets(walls);

                // 7. COBie / asset parameters, only when configured
                CheckCobieData(walls);

                // 8. Materials
                CheckMaterials(walls);

                // 9. Coordinates
                CheckCoordinates(walls);

                // 10. Revit warnings that actually reference walls
                CheckWarnings();

                ResultsGrid.ItemsSource = null;
                ResultsGrid.ItemsSource = _results
                    .OrderBy(r => r.ElementId)
                    .ThenBy(r => r.CheckName)
                    .ToList();

                PopulateFilters();
                UpdateIssueBreakdown();

                StatusText.Text =
                    $"Wall quality check completed. {walls.Count} walls analysed and {_results.Count} issues found.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.ToString(),
                    "Wall Quality Checker",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                StatusText.Text = "Check failed.";
            }
            finally
            {
                RunCheckButton.IsEnabled = true;
            }
        }


        private void UpdateModelSummary()
        {
            try
            {
                if (_doc == null)
                {
                    StatusText.Text = "No active Revit document.";
                    return;
                }

                if (_discovery == null)
                {
                    _discovery = new WallModelDiscovery(_doc);
                }

                ModelWallsText.Text =
                    _discovery.GetWallCount().ToString();

                ModelWallTypesText.Text =
                    _discovery.GetWallTypeCount().ToString();

                ModelLevelsText.Text =
                    _discovery.GetLevelCount().ToString();

                ModelWorksetsText.Text = _doc.IsWorkshared
                    ? _discovery.GetWallWorksetCount().ToString()
                    : "N/A";

                ModelMaterialsText.Text =
                    _discovery.GetWallMaterialCount().ToString();
            }
            catch (Exception ex)
            {
                StatusText.Text =
                    "Summary update failed: " + ex.Message;

                throw;
            }
        }


        private void FixAllIssuesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (_joinRepairHandler == null || _joinRepairEvent == null)
            {
                MessageBox.Show(
                    "The wall repair handler is not initialized. " +
                    "Check the constructor and ExternalEvent setup.",
                    "Wall Quality Checker",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (_results == null || _results.Count == 0)
            {
                MessageBox.Show(
                    "No scan results found. Run the wall quality check first.",
                    "Wall Quality Checker",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var requests = new List<WallJoinRepairRequest>();
            var seen = new HashSet<string>();

            foreach (WallCheckResult result in _results)
            {
                if (result == null ||
                    !string.Equals(
                        result.CheckName,
                        "Unconnected Walls",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string actualValue = result.ActualValue ?? string.Empty;

                var nearbyMatch =
                    System.Text.RegularExpressions.Regex.Match(
                        actualValue,
                        @"Nearby Wall\s+(\d+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                var endMatch =
                    System.Text.RegularExpressions.Regex.Match(
                        actualValue,
                        @"WallEnd\s*=\s*(\d+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (!nearbyMatch.Success || !endMatch.Success)
                    continue;

                if (!int.TryParse(
                        nearbyMatch.Groups[1].Value,
                        out int nearbyWallId) ||
                    !int.TryParse(
                        endMatch.Groups[1].Value,
                        out int wallEnd))
                {
                    continue;
                }

                // Revit wall ends are numbered 0 and 1.
                if (wallEnd != 0 && wallEnd != 1)
                    continue;

                if (result.ElementId == nearbyWallId)
                    continue;

                // Avoid submitting the same wall end more than once.
                string key = $"{result.ElementId}:{wallEnd}";

                if (!seen.Add(key))
                    continue;

                requests.Add(new WallJoinRepairRequest
                {
                    WallId = result.ElementId,
                    NearbyWallId = nearbyWallId,
                    WallEnd = wallEnd
                });
            }

            if (requests.Count == 0)
            {
                MessageBox.Show(
                    "No valid wall-join repair requests were found.\n\n" +
                    "Run the check again and verify that ActualValue " +
                    "contains 'Nearby Wall <id>; WallEnd=<0 or 1>'.",
                    "Wall Quality Checker",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            MessageBoxResult answer = MessageBox.Show(
                $"The checker prepared {requests.Count} unique wall-end " +
                "repair requests.\n\n" +
                "Revit will revalidate each request before attempting " +
                "to allow the wall join. Some requests may be skipped.\n\n" +
                "Save a backup of your model before proceeding.\n\n" +
                "Do you want to continue?",
                "Confirm Wall Join Repair",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return;

            _joinRepairHandler.Requests = requests;

            try
            {
                _joinRepairEvent.Raise();
                StatusText.Text =
                    $"Submitted {requests.Count} wall-join repair requests.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Could not start wall repair.";

                MessageBox.Show(
                    "Could not start the repair operation:\n" + ex.Message,
                    "Wall Quality Checker",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void UpdateIssueBreakdown()
        {
            if (IssueBreakdownPanel == null)
                return;

            IssueBreakdownPanel.Children.Clear();

            if (_results == null || _results.Count == 0)
            {
                TextBlock noIssuesText = new TextBlock
                {
                    Text = "No issues detected.",
                    FontSize = 14,
                    Margin = new Thickness(0, 4, 0, 4)
                };

                IssueBreakdownPanel.Children.Add(noIssuesText);
                return;
            }

            // ============================================================
            // GROUP ALL ISSUES BY CHECK NAME
            // ============================================================

            List<IGrouping<string, WallCheckResult>> checkGroups =
                _results
                    .Where(r => !string.IsNullOrWhiteSpace(r.CheckName))
                    .GroupBy(
                        r => r.CheckName.Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => GetBreakdownOrder(g.Key))
                    .ThenBy(g => g.Key)
                    .ToList();


            // ============================================================
            // DISPLAY ONLY THE OVERALL COUNT FOR EACH CATEGORY
            // ============================================================

            foreach (IGrouping<string, WallCheckResult> checkGroup
                     in checkGroups)
            {
                AddBreakdownRow(
                    checkGroup.Key,
                    checkGroup.Count(),
                    true);
            }


            // ============================================================
            // SEPARATOR
            // ============================================================

            Border separator = new Border
            {
                Height = 1,
                Background = System.Windows.Media.Brushes.LightGray,
                Margin = new Thickness(0, 8, 0, 8)
            };

            IssueBreakdownPanel.Children.Add(separator);


            // ============================================================
            // TOTAL ISSUES
            // ============================================================

            AddBreakdownRow(
                "TOTAL ISSUES",
                _results.Count,
                true);

        }

        private int GetBreakdownOrder(string checkName)
        {
            if (string.Equals(
                checkName,
                "Warnings",
                StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(
                checkName,
                "Unconnected Walls",
                StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (string.Equals(
                checkName,
                "Duplicate Walls",
                StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (string.Equals(
                checkName,
                "Materials",
                StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }

            return 100;
        }

        private void AddBreakdownRow(
            string label,
            int count,
            bool isMainCategory)
        {
            System.Windows.Controls.Grid row =
                new System.Windows.Controls.Grid
                {
                    Margin = isMainCategory
                        ? new Thickness(0, 4, 0, 4)
                        : new Thickness(20, 2, 0, 2)
                };

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });

            row.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(70)
                });

            TextBlock labelText = new TextBlock
            {
                Text = isMainCategory
                    ? label
                    : "└─ " + label,
                FontSize = isMainCategory ? 14 : 13,
                FontWeight = isMainCategory
                    ? FontWeights.SemiBold
                    : FontWeights.Normal
            };

            System.Windows.Controls.Grid.SetColumn(labelText, 0);

            TextBlock countText = new TextBlock
            {
                Text = count.ToString(),
                FontSize = isMainCategory ? 14 : 13,
                FontWeight = isMainCategory
                    ? FontWeights.SemiBold
                    : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            System.Windows.Controls.Grid.SetColumn(countText, 1);

            row.Children.Add(labelText);
            row.Children.Add(countText);

            IssueBreakdownPanel.Children.Add(row);
        }

        // ============================================================
        // 1. PARAMETERS
        // ============================================================

        private void CheckRequiredParameters(List<Wall> walls)
        {
            foreach (Wall wall in walls)
            {
                Parameter baseConstraint = wall.get_Parameter(
                    BuiltInParameter.WALL_BASE_CONSTRAINT);

                if (baseConstraint == null ||
                    baseConstraint.StorageType != StorageType.ElementId ||
                    _doc.GetElement(baseConstraint.AsElementId()) as Level == null)
                {
                    AddResult(
                        wall,
                        "Parameters",
                        "FAILED",
                        "Wall Base Constraint is missing or does not reference a valid Level.",
                        GetParameterValue(baseConstraint),
                        "Valid Level");
                }

                Parameter topConstraint = wall.get_Parameter(
                    BuiltInParameter.WALL_HEIGHT_TYPE);

                if (topConstraint == null)
                {
                    AddResult(
                        wall,
                        "Parameters",
                        "FAILED",
                        "Wall Top Constraint parameter is missing.",
                        "Missing",
                        "Valid Top Constraint");
                }

                Parameter baseOffset = wall.get_Parameter(
                    BuiltInParameter.WALL_BASE_OFFSET);

                ValidateDoubleParameter(
                    wall,
                    baseOffset,
                    "Base Offset",
                    BuiltInParameter.WALL_BASE_OFFSET);

                Parameter topOffset = wall.get_Parameter(
                    BuiltInParameter.WALL_TOP_OFFSET);

                ValidateDoubleParameter(
                    wall,
                    topOffset,
                    "Top Offset",
                    BuiltInParameter.WALL_TOP_OFFSET);

                Parameter locationLine = wall.get_Parameter(
                    BuiltInParameter.WALL_KEY_REF_PARAM);

                if (locationLine == null ||
                    locationLine.StorageType != StorageType.Integer)
                {
                    AddResult(
                        wall,
                        "Parameters",
                        "FAILED",
                        "Wall Location Line parameter is missing or has an invalid storage type.",
                        locationLine?.StorageType.ToString() ?? "Missing",
                        "Integer");
                }

                if (topConstraint != null &&
                    topConstraint.StorageType == StorageType.ElementId &&
                    topConstraint.AsElementId() == ElementId.InvalidElementId)
                {
                    Parameter unconnectedHeight = wall.get_Parameter(
                        BuiltInParameter.WALL_USER_HEIGHT_PARAM);

                    if (unconnectedHeight == null ||
                        unconnectedHeight.StorageType != StorageType.Double ||
                        unconnectedHeight.AsDouble() <= 0)
                    {
                        AddResult(
                            wall,
                            "Parameters",
                            "FAILED",
                            "Unconnected wall must have a positive Unconnected Height.",
                            GetParameterValue(unconnectedHeight),
                            "Greater Than Zero");
                    }
                }

                WallType wallType = _doc.GetElement(wall.GetTypeId()) as WallType;
                if (wallType == null || string.IsNullOrWhiteSpace(wallType.Name))
                {
                    AddResult(
                        wall,
                        "Parameters",
                        "FAILED",
                        "Wall Type could not be resolved or has no valid name.",
                        wallType?.Name ?? "Missing",
                        "Named Wall Type");
                }

                LocationCurve location = wall.Location as LocationCurve;
                if (location == null || location.Curve == null)
                {
                    AddResult(
                        wall,
                        "Parameters",
                        "FAILED",
                        "Wall does not have a valid Location Curve.",
                        "Missing",
                        "Valid Location Curve");
                }
            }
        }

        private void ValidateDoubleParameter(
            Wall wall,
            Parameter parameter,
            string label,
            BuiltInParameter builtInParameter)
        {
            if (parameter == null)
            {
                AddResult(
                    wall,
                    "Parameters",
                    "FAILED",
                    $"{label} parameter is missing.",
                    "Missing",
                    "Valid Number");
                return;
            }

            if (parameter.StorageType != StorageType.Double)
            {
                AddResult(
                    wall,
                    "Parameters",
                    "FAILED",
                    $"{label} has an invalid storage type.",
                    parameter.StorageType.ToString(),
                    "Double");
                return;
            }

            double value = parameter.AsDouble();
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                AddResult(
                    wall,
                    "Parameters",
                    "FAILED",
                    $"{label} contains an invalid numeric value.",
                    value.ToString(),
                    "Finite Number");
            }
        }

        private void CheckConfiguredRequiredParameters(List<Wall> walls)
        {
            foreach (string requiredName in _settings.RequiredWallParameters ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(requiredName))
                    continue;

                foreach (Wall wall in walls)
                {
                    Parameter parameter = wall.LookupParameter(requiredName);

                    if (parameter == null)
                    {
                        AddResult(
                            wall,
                            "Parameters",
                            "FAILED",
                            $"Required project parameter '{requiredName}' is missing.",
                            "Missing",
                            "Parameter Required");
                        continue;
                    }

                    if (IsParameterEmpty(parameter))
                    {
                        AddResult(
                            wall,
                            "Parameters",
                            "FAILED",
                            $"Required project parameter '{requiredName}' is empty.",
                            "<Empty>",
                            "Value Required");
                    }
                }
            }
        }

        // ============================================================
        // 2. WALL TYPES / NAMING STANDARD
        // ============================================================

        private void CheckWallTypes(List<Wall> walls)
        {
            foreach (Wall wall in walls)
            {
                WallType type = _doc.GetElement(wall.GetTypeId()) as WallType;

                if (type == null)
                {
                    AddResult(
                        wall,
                        "Wall Types",
                        "FAILED",
                        "Wall type cannot be resolved.",
                        "Missing",
                        "Valid Wall Type");
                }
                else if (string.IsNullOrWhiteSpace(type.Name))
                {
                    AddResult(
                        wall,
                        "Wall Types",
                        "FAILED",
                        "Wall type has no valid name.",
                        "<Empty>",
                        "Named Wall Type");
                }
            }
        }

        private void CheckConfiguredWallTypeStandard(List<Wall> walls)
        {
            if (!_settings.CheckWallTypeNameAgainstApprovedPatterns ||
                _settings.ApprovedWallTypeNamePatterns == null ||
                _settings.ApprovedWallTypeNamePatterns.Count == 0)
                return;

            foreach (Wall wall in walls)
            {
                WallType type = _doc.GetElement(wall.GetTypeId()) as WallType;
                string name = type?.Name ?? "";

                bool approved = _settings.ApprovedWallTypeNamePatterns.Any(
                    pattern => WildcardMatch(name, pattern));

                if (!approved)
                {
                    AddResult(
                        wall,
                        "Naming",
                        "FAILED",
                        "Wall Type name does not match the configured project/company naming standard.",
                        name,
                        string.Join(" OR ", _settings.ApprovedWallTypeNamePatterns));
                }
            }
        }

        // ============================================================
        // 3. DUPLICATE / EXACT OVERLAPPING WALLS
        // ============================================================

        private void CheckDuplicateWalls(List<Wall> walls)
        {
            double tolerance = Math.Max(0.0001, _settings.DuplicateToleranceFeet);

            for (int i = 0; i < walls.Count; i++)
            {
                Wall first = walls[i];
                LocationCurve firstLocation = first.Location as LocationCurve;
                Curve firstCurve = firstLocation?.Curve;

                if (firstCurve == null)
                    continue;

                for (int j = i + 1; j < walls.Count; j++)
                {
                    Wall second = walls[j];
                    LocationCurve secondLocation = second.Location as LocationCurve;
                    Curve secondCurve = secondLocation?.Curve;

                    if (secondCurve == null)
                        continue;

                    if (Math.Abs(firstCurve.Length - secondCurve.Length) > tolerance)
                        continue;

                    XYZ firstStart = firstCurve.GetEndPoint(0);
                    XYZ firstEnd = firstCurve.GetEndPoint(1);
                    XYZ secondStart = secondCurve.GetEndPoint(0);
                    XYZ secondEnd = secondCurve.GetEndPoint(1);

                    bool sameDirection =
                        firstStart.DistanceTo(secondStart) <= tolerance &&
                        firstEnd.DistanceTo(secondEnd) <= tolerance;

                    bool oppositeDirection =
                        firstStart.DistanceTo(secondEnd) <= tolerance &&
                        firstEnd.DistanceTo(secondStart) <= tolerance;

                    if (!sameDirection && !oppositeDirection)
                        continue;

                    // Same centerline alone is not enough to call two walls duplicates.
                    // We also require the same base/top constraints.
                    if (!SameConstraint(first, second, BuiltInParameter.WALL_BASE_CONSTRAINT))
                        continue;

                    if (!SameConstraint(first, second, BuiltInParameter.WALL_HEIGHT_TYPE))
                        continue;

                    AddResult(
                        first,
                        "Duplicate Walls",
                        "FAILED",
                        $"Exact/near-exact duplicate wall geometry detected. Matching wall Element ID: {second.Id.IntegerValue}.",
                        second.Id.IntegerValue.ToString(),
                        "No duplicate wall");

                    AddResult(
                        second,
                        "Duplicate Walls",
                        "FAILED",
                        $"Exact/near-exact duplicate wall geometry detected. Matching wall Element ID: {first.Id.IntegerValue}.",
                        first.Id.IntegerValue.ToString(),
                        "No duplicate wall");
                }
            }
        }

        private bool SameConstraint(Wall first, Wall second, BuiltInParameter parameter)
        {
            Parameter p1 = first.get_Parameter(parameter);
            Parameter p2 = second.get_Parameter(parameter);

            if (p1 == null || p2 == null)
                return false;

            if (p1.StorageType != p2.StorageType)
                return false;

            switch (p1.StorageType)
            {
                case StorageType.ElementId:
                    return p1.AsElementId() == p2.AsElementId();
                case StorageType.Integer:
                    return p1.AsInteger() == p2.AsInteger();
                case StorageType.Double:
                    return Math.Abs(p1.AsDouble() - p2.AsDouble()) < 0.0001;
                case StorageType.String:
                    return string.Equals(p1.AsString(), p2.AsString(), StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        // ============================================================
        // 4. WALL-END CONNECTIVITY
        // ============================================================

        //private void CheckUnconnectedWalls(List<Wall> walls)
        //{
        //    if (!_settings.RequireAtLeastOneWallEndConnectedWhenNearbyWallExists)
        //        return;

        //    double tolerance = Math.Max(0.001, _settings.ConnectionToleranceFeet);

        //    for (int i = 0; i < walls.Count; i++)
        //    {
        //        Wall wall = walls[i];
        //        LocationCurve location = wall.Location as LocationCurve;
        //        Curve curve = location?.Curve;

        //        if (curve == null)
        //            continue;

        //        for (int end = 0; end <= 1; end++)
        //        {
        //            XYZ point = curve.GetEndPoint(end);
        //            Wall nearbyWall = FindNearbyWall(point, wall.Id, walls, tolerance);

        //            if (nearbyWall == null)
        //                continue;

        //            bool joinAllowed = true;

        //            try
        //            {
        //                joinAllowed = WallUtils.IsWallJoinAllowedAtEnd(wall, end);
        //            }
        //            catch
        //            {
        //            }

        //            if (!joinAllowed)
        //            {
        //                AddResult(
        //                    wall,
        //                    "Unconnected Walls",
        //                    "FAILED",
        //                    $"Wall end {end + 1} is within the configured connection tolerance " +
        //                    $"of Wall {nearbyWall.Id.IntegerValue}, but joining is disallowed.",
        //                    $"Nearby Wall {nearbyWall.Id.IntegerValue}; WallEnd={end}",
        //                    "Join allowed when a wall connection is required");
        //            }
        //        }
        //    }
        //}



        private void CheckUnconnectedWalls(List<Wall> walls)
        {
            if (!_settings.RequireAtLeastOneWallEndConnectedWhenNearbyWallExists)
                return;

            double tolerance = Math.Max(
                0.001,
                _settings.ConnectionToleranceFeet);

            foreach (Wall wall in walls)
            {
                LocationCurve location = wall.Location as LocationCurve;
                Curve curve = location?.Curve;

                if (curve == null)
                    continue;

                for (int end = 0; end <= 1; end++)
                {
                    XYZ endpoint = curve.GetEndPoint(end);

                    Wall nearbyWall = FindNearbyWall(
                        endpoint,
                        wall.Id,
                        walls,
                        tolerance);

                    if (nearbyWall == null)
                        continue;

                    bool joinAllowed;

                    try
                    {
                        joinAllowed = WallUtils.IsWallJoinAllowedAtEnd(
                            wall,
                            end);
                    }
                    catch (Exception)
                    {
                        AddResult(
                            wall,
                            "Unconnected Walls",
                            "NEEDS REVIEW",
                            $"Could not determine join permission at wall end {end + 1}.",
                            $"Nearby Wall {nearbyWall.Id.IntegerValue}; WallEnd={end}",
                            "Join permission should be checked");

                        continue;
                    }

                    // Join permission alone does not prove physical connectivity.
                    if (!joinAllowed)
                    {
                        AddResult(
                            wall,
                            "Unconnected Walls",
                            "NEEDS REVIEW",
                            $"Wall end {end + 1} is within the configured " +
                            $"tolerance of Wall {nearbyWall.Id.IntegerValue}, " +
                            "but joining is disallowed. Physical disconnection " +
                            "has not been verified.",
                            $"Nearby Wall {nearbyWall.Id.IntegerValue}; " +
                            $"WallEnd={end}; JoinAllowed=false",
                            "Review whether these walls should connect");
                    }
                }
            }
        }


        private Wall FindNearbyWall(
            XYZ point,
            ElementId sourceId,
            List<Wall> walls,
            double tolerance)
        {
            foreach (Wall other in walls)
            {
                if (other.Id == sourceId)
                    continue;

                LocationCurve otherLocation = other.Location as LocationCurve;
                Curve otherCurve = otherLocation?.Curve;

                if (otherCurve == null)
                    continue;

                try
                {
                    IntersectionResult result = otherCurve.Project(point);
                    if (result != null && result.Distance <= tolerance)
                        return other;
                }
                catch
                {
                }
            }

            return null;
        }

        // ============================================================
        // 5. LEVELS / CONSTRAINTS
        // ============================================================

        private void CheckLevels(List<Wall> walls)
        {
            foreach (Wall wall in walls)
            {
                Parameter baseParameter = wall.get_Parameter(
                    BuiltInParameter.WALL_BASE_CONSTRAINT);

                Parameter topParameter = wall.get_Parameter(
                    BuiltInParameter.WALL_HEIGHT_TYPE);

                Parameter baseOffsetParameter = wall.get_Parameter(
                    BuiltInParameter.WALL_BASE_OFFSET);

                Parameter topOffsetParameter = wall.get_Parameter(
                    BuiltInParameter.WALL_TOP_OFFSET);

                Level baseLevel = GetLevelFromParameter(baseParameter);
                double baseOffset = GetDouble(baseOffsetParameter);

                if (baseLevel == null)
                {
                    AddResult(
                        wall,
                        "Levels",
                        "FAILED",
                        "Wall Base Constraint does not reference a valid Level.",
                        GetParameterValue(baseParameter),
                        "Valid Level");
                    continue;
                }

                if (topParameter == null ||
                    topParameter.StorageType != StorageType.ElementId)
                {
                    AddResult(
                        wall,
                        "Levels",
                        "FAILED",
                        "Wall Top Constraint cannot be read.",
                        GetParameterValue(topParameter),
                        "Valid Level or Unconnected");
                    continue;
                }

                ElementId topId = topParameter.AsElementId();

                if (topId == ElementId.InvalidElementId)
                {
                    Parameter height = wall.get_Parameter(
                        BuiltInParameter.WALL_USER_HEIGHT_PARAM);

                    if (height == null ||
                        height.StorageType != StorageType.Double ||
                        height.AsDouble() <= 0)
                    {
                        AddResult(
                            wall,
                            "Levels",
                            "FAILED",
                            "Unconnected wall has no positive unconnected height.",
                            GetParameterValue(height),
                            "Greater Than Zero");
                    }

                    continue;
                }

                Level topLevel = _doc.GetElement(topId) as Level;

                if (topLevel == null)
                {
                    AddResult(
                        wall,
                        "Levels",
                        "FAILED",
                        "Top Constraint does not reference a valid Level.",
                        topId.IntegerValue.ToString(),
                        "Valid Level");
                    continue;
                }

                double topOffset = GetDouble(topOffsetParameter);
                double bottomElevation = baseLevel.Elevation + baseOffset;
                double topElevation = topLevel.Elevation + topOffset;

                if (topElevation <= bottomElevation)
                {
                    AddResult(
                        wall,
                        "Levels",
                        "FAILED",
                        "Wall top elevation is not above its base elevation.",
                        $"Base={FormatFeet(bottomElevation)}, Top={FormatFeet(topElevation)}",
                        "Top elevation greater than base elevation");
                }
            }
        }

        private Level GetLevelFromParameter(Parameter parameter)
        {
            if (parameter == null ||
                parameter.StorageType != StorageType.ElementId)
                return null;

            return _doc.GetElement(parameter.AsElementId()) as Level;
        }

        // ============================================================
        // 6. WORKSETS
        // ============================================================

        private void CheckWorksets(List<Wall> walls)
        {
            if (!_doc.IsWorkshared)
                return;

            foreach (Wall wall in walls)
            {
                try
                {
                    Workset workset = _doc.GetWorksetTable().GetWorkset(wall.WorksetId);

                    if (workset == null || string.IsNullOrWhiteSpace(workset.Name))
                    {
                        AddResult(
                            wall,
                            "Worksets",
                            "FAILED",
                            "Wall workset could not be resolved or has no valid name.",
                            "Unknown",
                            "Valid named workset");
                        continue;
                    }

                    if (_settings.CheckWorksetAgainstApprovedNames &&
                        _settings.ApprovedWorksetNames != null &&
                        _settings.ApprovedWorksetNames.Count > 0 &&
                        !_settings.ApprovedWorksetNames.Any(
                            x => string.Equals(x, workset.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        AddResult(
                            wall,
                            "Worksets",
                            "FAILED",
                            "Wall is assigned to a workset outside the configured approved workset list.",
                            workset.Name,
                            string.Join(" OR ", _settings.ApprovedWorksetNames));
                    }
                }
                catch
                {
                    AddResult(
                        wall,
                        "Worksets",
                        "FAILED",
                        "Unable to read wall workset.",
                        "Unknown",
                        "Valid workset");
                }
            }
        }

        // ============================================================
        // 7. COBIE / ASSET DATA
        // ============================================================

        private void CheckCobieData(List<Wall> walls)
        {
            if (_settings.CobieParameters == null ||
                _settings.CobieParameters.Count == 0)
                return;

            foreach (Wall wall in walls)
            {
                foreach (string parameterName in _settings.CobieParameters)
                {
                    if (string.IsNullOrWhiteSpace(parameterName))
                        continue;

                    Parameter parameter = wall.LookupParameter(parameterName);

                    if (parameter == null)
                    {
                        AddResult(
                            wall,
                            "COBie",
                            "FAILED",
                            $"Required COBie/asset parameter '{parameterName}' is missing.",
                            "Missing",
                            "Parameter Required");
                    }
                    else if (IsParameterEmpty(parameter))
                    {
                        AddResult(
                            wall,
                            "COBie",
                            "FAILED",
                            $"Required COBie/asset parameter '{parameterName}' is empty.",
                            "<Empty>",
                            "Value Required");
                    }
                }
            }
        }

        // ============================================================
        // 8. MATERIALS
        // ============================================================

        private void CheckMaterials(List<Wall> walls)
        {
            foreach (Wall wall in walls)
            {
                WallType wallType = _doc.GetElement(wall.GetTypeId()) as WallType;
                CompoundStructure structure = wallType?.GetCompoundStructure();

                if (structure == null)
                {
                    AddResult(
                        wall,
                        "Materials",
                        "FAILED",
                        "Wall Type has no compound structure/material layer definition.",
                        "No Compound Structure",
                        "Valid Material Layers");
                    continue;
                }

                IList<CompoundStructureLayer> layers = structure.GetLayers();

                if (layers == null || layers.Count == 0)
                {
                    AddResult(
                        wall,
                        "Materials",
                        "FAILED",
                        "Wall Type contains no material layers.",
                        "0 layers",
                        "At least one material layer");
                    continue;
                }

                for (int i = 0; i < layers.Count; i++)
                {
                    ElementId materialId = layers[i].MaterialId;

                    if (materialId == ElementId.InvalidElementId)
                    {
                        AddResult(
                            wall,
                            "Materials",
                            "FAILED",
                            $"Wall material layer {i + 1} has no material assigned.",
                            "No Material",
                            "Material Required");
                    }
                    else if (_doc.GetElement(materialId) as Material == null)
                    {
                        AddResult(
                            wall,
                            "Materials",
                            "FAILED",
                            $"Wall material layer {i + 1} references a material that cannot be resolved.",
                            materialId.IntegerValue.ToString(),
                            "Valid Material");
                    }
                }
            }
        }

        // ============================================================
        // 9. COORDINATES / GEOMETRY
        // ============================================================

        private void CheckCoordinates(List<Wall> walls)
        {
            foreach (Wall wall in walls)
            {
                LocationCurve location = wall.Location as LocationCurve;
                Curve curve = location?.Curve;

                if (curve == null)
                    continue;

                XYZ start = curve.GetEndPoint(0);
                XYZ end = curve.GetEndPoint(1);

                if (!IsValidPoint(start) || !IsValidPoint(end))
                {
                    AddResult(
                        wall,
                        "Coordinates",
                        "FAILED",
                        "Wall geometry contains an invalid coordinate.",
                        "NaN/Infinity",
                        "Finite coordinates");
                    continue;
                }

                if (curve.Length <= 0.0001)
                {
                    AddResult(
                        wall,
                        "Coordinates",
                        "FAILED",
                        "Wall has zero or near-zero geometric length.",
                        curve.Length.ToString("0.######"),
                        "Length greater than zero");
                }

                if (_settings.CoordinateLimitFeet > 0)
                {
                    double limit = _settings.CoordinateLimitFeet;

                    if (Math.Abs(start.X) > limit ||
                        Math.Abs(start.Y) > limit ||
                        Math.Abs(start.Z) > limit ||
                        Math.Abs(end.X) > limit ||
                        Math.Abs(end.Y) > limit ||
                        Math.Abs(end.Z) > limit)
                    {
                        AddResult(
                            wall,
                            "Coordinates",
                            "FAILED",
                            "Wall is outside the configured coordinate boundary.",
                            $"Start={FormatPoint(start)}; End={FormatPoint(end)}",
                            $"All coordinates within ±{limit:0.###} ft");
                    }
                }
            }
        }

        private bool IsValidPoint(XYZ point)
        {
            return point != null &&
                   !double.IsNaN(point.X) &&
                   !double.IsNaN(point.Y) &&
                   !double.IsNaN(point.Z) &&
                   !double.IsInfinity(point.X) &&
                   !double.IsInfinity(point.Y) &&
                   !double.IsInfinity(point.Z);
        }

        // ============================================================
        // 10. REVIT WARNINGS - WALLS ONLY
        // ============================================================

        //private void CheckWarnings(List<Wall> walls)
        //{
        //    HashSet<int> wallIds = new HashSet<int>(
        //        walls.Select(w => w.Id.IntegerValue));

        //    foreach (FailureMessage warning in _doc.GetWarnings())
        //    {
        //        ICollection<ElementId> failingIds = warning.GetFailingElements();

        //        if (failingIds == null)
        //            continue;

        //        string description = warning.GetDescriptionText();

        //        foreach (ElementId id in failingIds)
        //        {
        //            if (!wallIds.Contains(id.IntegerValue))
        //                continue;

        //            Wall wall = _doc.GetElement(id) as Wall;
        //            if (wall == null)
        //                continue;

        //            AddResult(
        //                wall,
        //                "Warnings",
        //                "FAILED",
        //                string.IsNullOrWhiteSpace(description)
        //                    ? "Revit warning references this wall."
        //                    : description,
        //                "Revit Warning",
        //                "No wall-related warning");
        //        }
        //    }
        //}

        private void CheckWarnings()
        {
            // Get all walls in the current model
            List<Wall> walls = new FilteredElementCollector(_doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .ToList();

            HashSet<int> wallIds = new HashSet<int>(
                walls.Select(w => w.Id.IntegerValue));

            // Get actual Revit warnings
            foreach (FailureMessage warning in _doc.GetWarnings())
            {
                ICollection<ElementId> failingIds =
                    warning.GetFailingElements();

                if (failingIds == null || failingIds.Count == 0)
                    continue;

                // Find the first wall affected by this warning
                Wall affectedWall = null;

                foreach (ElementId id in failingIds)
                {
                    if (!wallIds.Contains(id.IntegerValue))
                        continue;

                    affectedWall = _doc.GetElement(id) as Wall;

                    if (affectedWall != null)
                        break;
                }

                // Ignore warnings that do not involve walls
                if (affectedWall == null)
                    continue;

                string description = warning.GetDescriptionText();

                AddResult(
                    affectedWall,
                    "Warnings",
                    "FAILED",
                    string.IsNullOrWhiteSpace(description)
                        ? "Revit warning references this wall."
                        : description,
                    "Revit Warning",
                    "No wall-related warning");
            }
        }
        // ============================================================
        // HELPERS
        // ============================================================

        private void AddResult(
            Wall wall,
            string checkName,
            string status,
            string issue,
            string actual,
            string expected)
        {
            WallType wallType = _doc.GetElement(wall.GetTypeId()) as WallType;

            _results.Add(new WallCheckResult
            {
                ElementId = wall.Id.IntegerValue,
                CheckName = checkName,
                Status = status,
                Issue = issue ?? "",
                ActualValue = actual ?? "",
                ExpectedValue = expected ?? "",
                WallType = wallType?.Name ?? "Unknown",
                Level = GetLevelName(wall)
            });
        }

        private string GetLevelName(Wall wall)
        {
            Parameter parameter = wall.get_Parameter(
                BuiltInParameter.WALL_BASE_CONSTRAINT);

            Level level = GetLevelFromParameter(parameter);
            return level?.Name ?? "Unknown";
        }

        private string GetParameterValue(Parameter parameter)
        {
            try
            {
                if (parameter == null)
                    return "Missing";

                if (parameter.StorageType == StorageType.String)
                    return parameter.AsString() ?? "";

                return parameter.AsValueString()
                       ?? parameter.AsString()
                       ?? "";
            }
            catch
            {
                return "";
            }
        }

        private bool IsParameterEmpty(Parameter parameter)
        {
            if (parameter == null)
                return true;

            try
            {
                switch (parameter.StorageType)
                {
                    case StorageType.String:
                        return string.IsNullOrWhiteSpace(parameter.AsString());

                    case StorageType.ElementId:
                        return parameter.AsElementId() == ElementId.InvalidElementId;

                    default:
                        string value = parameter.AsValueString() ?? parameter.AsString();
                        return string.IsNullOrWhiteSpace(value);
                }
            }
            catch
            {
                return true;
            }
        }

        private double GetDouble(Parameter parameter)
        {
            if (parameter == null || parameter.StorageType != StorageType.Double)
                return 0;

            double value = parameter.AsDouble();
            return double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;
        }

        private string FormatFeet(double feet)
        {
            return feet.ToString("0.###") + " ft";
        }

        private string FormatPoint(XYZ point)
        {
            return $"({point.X:0.###}, {point.Y:0.###}, {point.Z:0.###}) ft";
        }

        private bool WildcardMatch(string value, string pattern)
        {
            if (value == null || pattern == null)
                return false;

            string regex = "^" +
                           Regex.Escape(pattern)
                               .Replace("\\*", ".*")
                               .Replace("\\?", ".") +
                           "$";

            return Regex.IsMatch(
                value,
                regex,
                RegexOptions.IgnoreCase);
        }

        // ============================================================
        // EXPORT TO EXCEL (.XLSX)
        // ============================================================

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get exactly what is currently displayed after filtering
                List<WallCheckResult> exportResults =
                    GetFilteredResultsForExport();

                if (exportResults.Count == 0)
                {
                    MessageBox.Show(
                        "There are no issues matching the selected filters.",
                        "Wall Quality Checker",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                SaveFileDialog saveDialog = new SaveFileDialog
                {
                    Title = "Export Wall Quality Report",
                    Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                    DefaultExt = ".xlsx",
                    AddExtension = true,
                    FileName =
                        $"Wall_Quality_Report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };

                if (saveDialog.ShowDialog() != true)
                    return;

                string filePath = saveDialog.FileName;

                CreateExcelFile(
                    filePath,
                    exportResults);

                string selectedCheck =
                    CheckFilterComboBox.SelectedItem as string
                    ?? "ALL CHECKS";

                string selectedIssue =
                    IssueFilterComboBox.SelectedItem as string
                    ?? "ALL ISSUES";

                MessageBox.Show(
                    $"Excel report exported successfully.\n\n" +
                    $"Check: {selectedCheck}\n" +
                    $"Issue: {selectedIssue}\n" +
                    $"Issues exported: {exportResults.Count}\n\n" +
                    $"{filePath}",
                    "Wall Quality Checker",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to export Excel report.\n\n" +
                    $"{ex.Message}\n\n" +
                    $"{ex.StackTrace}",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        private List<WallCheckResult> GetFilteredResultsForExport()
        {
            string selectedCheck =
                CheckFilterComboBox.SelectedItem as string;

            string selectedIssue =
                IssueFilterComboBox.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(selectedCheck))
                selectedCheck = "ALL CHECKS";

            if (string.IsNullOrWhiteSpace(selectedIssue))
                selectedIssue = "ALL ISSUES";

            IEnumerable<WallCheckResult> filteredResults = _results;

            // ==========================================
            // FILTER BY CHECK / CATEGORY
            // ==========================================

            if (!string.Equals(
                selectedCheck,
                "ALL CHECKS",
                StringComparison.OrdinalIgnoreCase))
            {
                filteredResults = filteredResults.Where(r =>
                    string.Equals(
                        r.CheckName?.Trim(),
                        selectedCheck.Trim(),
                        StringComparison.OrdinalIgnoreCase));
            }

            // ==========================================
            // FILTER BY ISSUE
            // ==========================================

            if (!string.Equals(
                selectedIssue,
                "ALL ISSUES",
                StringComparison.OrdinalIgnoreCase))
            {
                filteredResults = filteredResults.Where(r =>
                    string.Equals(
                        r.Issue?.Trim(),
                        selectedIssue.Trim(),
                        StringComparison.OrdinalIgnoreCase));
            }

            return filteredResults
                .OrderBy(r => r.ElementId)
                .ThenBy(r => r.CheckName)
                .ToList();
        }

        private void CreateExcelFile(
    string filePath,
    List<WallCheckResult> exportResults)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);

            using (FileStream fileStream = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite))
            using (ZipArchive archive = new ZipArchive(
                fileStream,
                ZipArchiveMode.Create))
            {
                CreateContentTypes(archive);
                CreateRootRelationships(archive);
                CreateWorkbook(archive);
                CreateWorkbookRelationships(archive);
                CreateStyles(archive);

                CreateSummarySheet(
                    archive,
                    exportResults);

                CreateIssuesSheet(
                    archive,
                    exportResults);
            }
        }

        private void CreateContentTypes(ZipArchive archive)
        {
            XNamespace ns =
                "http://schemas.openxmlformats.org/package/2006/content-types";

            XDocument document = new XDocument(
                new XElement(ns + "Types",
                    new XElement(ns + "Default",
                        new XAttribute("Extension", "rels"),
                        new XAttribute("ContentType",
                            "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(ns + "Default",
                        new XAttribute("Extension", "xml"),
                        new XAttribute("ContentType", "application/xml")),
                    new XElement(ns + "Override",
                        new XAttribute("PartName", "/xl/workbook.xml"),
                        new XAttribute("ContentType",
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                    new XElement(ns + "Override",
                        new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                        new XAttribute("ContentType",
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                    new XElement(ns + "Override",
                        new XAttribute("PartName", "/xl/worksheets/sheet2.xml"),
                        new XAttribute("ContentType",
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                    new XElement(ns + "Override",
                        new XAttribute("PartName", "/xl/styles.xml"),
                        new XAttribute("ContentType",
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"))
                ));

            WriteXml(archive, "[Content_Types].xml", document);
        }

        private void CreateRootRelationships(ZipArchive archive)
        {
            XNamespace ns =
                "http://schemas.openxmlformats.org/package/2006/relationships";

            XDocument document = new XDocument(
                new XElement(ns + "Relationships",
                    new XElement(ns + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type",
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                        new XAttribute("Target", "xl/workbook.xml"))
                ));

            WriteXml(archive, "_rels/.rels", document);
        }

        private void CreateWorkbook(ZipArchive archive)
        {
            XNamespace ns =
                "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rel =
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

            XDocument document = new XDocument(
                new XElement(ns + "workbook",
                    new XAttribute(XNamespace.Xmlns + "r", rel),
                    new XElement(ns + "sheets",
                        new XElement(ns + "sheet",
                            new XAttribute("name", "Summary"),
                            new XAttribute("sheetId", "1"),
                            new XAttribute(rel + "id", "rId1")),
                        new XElement(ns + "sheet",
                            new XAttribute("name", "All Errors"),
                            new XAttribute("sheetId", "2"),
                            new XAttribute(rel + "id", "rId2"))
                    )
                ));

            WriteXml(archive, "xl/workbook.xml", document);
        }

        private void CreateWorkbookRelationships(ZipArchive archive)
        {
            XNamespace ns =
                "http://schemas.openxmlformats.org/package/2006/relationships";

            XDocument document = new XDocument(
                new XElement(ns + "Relationships",
                    new XElement(ns + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type",
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                        new XAttribute("Target", "worksheets/sheet1.xml")),
                    new XElement(ns + "Relationship",
                        new XAttribute("Id", "rId2"),
                        new XAttribute("Type",
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                        new XAttribute("Target", "worksheets/sheet2.xml")),
                    new XElement(ns + "Relationship",
                        new XAttribute("Id", "rId3"),
                        new XAttribute("Type",
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                        new XAttribute("Target", "styles.xml"))
                ));

            WriteXml(archive, "xl/_rels/workbook.xml.rels", document);
        }

        private void CreateStyles(ZipArchive archive)
        {
            XNamespace ns =
                "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            XDocument document = new XDocument(
                new XElement(ns + "styleSheet",
                    new XElement(ns + "fonts",
                        new XAttribute("count", "2"),
                        new XElement(ns + "font",
                            new XElement(ns + "sz", new XAttribute("val", "11")),
                            new XElement(ns + "name", new XAttribute("val", "Calibri"))),
                        new XElement(ns + "font",
                            new XElement(ns + "b"),
                            new XElement(ns + "sz", new XAttribute("val", "11")),
                            new XElement(ns + "name", new XAttribute("val", "Calibri")))),
                    new XElement(ns + "fills",
                        new XAttribute("count", "2"),
                        new XElement(ns + "fill",
                            new XElement(ns + "patternFill", new XAttribute("patternType", "none"))),
                        new XElement(ns + "fill",
                            new XElement(ns + "patternFill", new XAttribute("patternType", "gray125")))),
                    new XElement(ns + "borders",
                        new XAttribute("count", "1"),
                        new XElement(ns + "border",
                            new XElement(ns + "left"),
                            new XElement(ns + "right"),
                            new XElement(ns + "top"),
                            new XElement(ns + "bottom"),
                            new XElement(ns + "diagonal"))),
                    new XElement(ns + "cellStyleXfs",
                        new XAttribute("count", "1"),
                        new XElement(ns + "xf",
                            new XAttribute("numFmtId", "0"),
                            new XAttribute("fontId", "0"),
                            new XAttribute("fillId", "0"),
                            new XAttribute("borderId", "0"))),
                    new XElement(ns + "cellXfs",
                        new XAttribute("count", "2"),
                        new XElement(ns + "xf",
                            new XAttribute("numFmtId", "0"),
                            new XAttribute("fontId", "0"),
                            new XAttribute("fillId", "0"),
                            new XAttribute("borderId", "0")),
                        new XElement(ns + "xf",
                            new XAttribute("numFmtId", "0"),
                            new XAttribute("fontId", "1"),
                            new XAttribute("fillId", "0"),
                            new XAttribute("borderId", "0"))),
                    new XElement(ns + "cellStyles",
                        new XAttribute("count", "1"),
                        new XElement(ns + "cellStyle",
                            new XAttribute("name", "Normal"),
                            new XAttribute("xfId", "0")))
                ));

            WriteXml(archive, "xl/styles.xml", document);
        }

        private void CreateSummarySheet(
             ZipArchive archive,
             List<WallCheckResult> exportResults)
        {
            XNamespace ns =
                "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            string selectedCheck =
                CheckFilterComboBox.SelectedItem as string
                ?? "ALL CHECKS";

            string selectedIssue =
                IssueFilterComboBox.SelectedItem as string
                ?? "ALL ISSUES";

            int warningCount = exportResults.Count(r =>
                string.Equals(
                    r.CheckName,
                    "Revit Warning",
                    StringComparison.OrdinalIgnoreCase)
                ||
                string.Equals(
                    r.CheckName,
                    "Warnings",
                    StringComparison.OrdinalIgnoreCase));

            int affectedWalls = exportResults
                .Select(r => r.ElementId)
                .Distinct()
                .Count();

            XElement sheetData = new XElement(ns + "sheetData");

            AddRow(
                sheetData,
                1,
                "WALL QUALITY CHECKER");

            AddRow(
                sheetData,
                3,
                "Check Filter",
                selectedCheck);

            AddRow(
                sheetData,
                4,
                "Issue Filter",
                selectedIssue);

            AddRow(
                sheetData,
                6,
                "Affected Walls",
                affectedWalls.ToString());

            AddRow(
                sheetData,
                7,
                "Issues Exported",
                exportResults.Count.ToString());

            AddRow(
                sheetData,
                8,
                "Wall Warnings",
                warningCount.ToString());

            XDocument document = new XDocument(
                new XElement(
                    ns + "worksheet",

                    new XElement(
                        ns + "sheetViews",

                        new XElement(
                            ns + "sheetView",
                            new XAttribute(
                                "workbookViewId",
                                "0"))),

                    sheetData));

            WriteXml(
                archive,
                "xl/worksheets/sheet1.xml",
                document);
        }

        private void CreateIssuesSheet(
                            ZipArchive archive,
                            List<WallCheckResult> exportResults)
        {
            XNamespace ns =
                "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            XElement sheetData = new XElement(ns + "sheetData");

            AddRow(sheetData, 1,
                "Element ID", "Check", "Status", "Issue",
                "Actual", "Expected", "Wall Type", "Level");

            int rowNumber = 2;

            // IMPORTANT: export the complete scan results, not the currently filtered DataGrid.
            // _results contains every failed wall check from the current model scan.
            foreach (WallCheckResult item in exportResults)
            {
                AddRow(sheetData, rowNumber,
                    item.ElementId.ToString(),
                    item.CheckName ?? "",
                    item.Status ?? "",
                    item.Issue ?? "",
                    item.ActualValue ?? "",
                    item.ExpectedValue ?? "",
                    item.WallType ?? "",
                    item.Level ?? "");

                rowNumber++;
            }

            int lastRow = Math.Max(1, rowNumber - 1);

            XElement columns = new XElement(ns + "cols",
                new XElement(ns + "col",
                    new XAttribute("min", "1"), new XAttribute("max", "1"),
                    new XAttribute("width", "15"), new XAttribute("customWidth", "1")),
                new XElement(ns + "col",
                    new XAttribute("min", "2"), new XAttribute("max", "3"),
                    new XAttribute("width", "18"), new XAttribute("customWidth", "1")),
                new XElement(ns + "col",
                    new XAttribute("min", "4"), new XAttribute("max", "4"),
                    new XAttribute("width", "60"), new XAttribute("customWidth", "1")),
                new XElement(ns + "col",
                    new XAttribute("min", "5"), new XAttribute("max", "6"),
                    new XAttribute("width", "30"), new XAttribute("customWidth", "1")),
                new XElement(ns + "col",
                    new XAttribute("min", "7"), new XAttribute("max", "8"),
                    new XAttribute("width", "25"), new XAttribute("customWidth", "1"))
            );

            XElement sheetViews = new XElement(
                ns + "sheetViews",
                new XElement(ns + "sheetView",
                    new XAttribute("workbookViewId", "0"),
                    new XElement(ns + "pane",
                        new XAttribute("ySplit", "1"),
                        new XAttribute("topLeftCell", "A2"),
                        new XAttribute("activePane", "bottomLeft"),
                        new XAttribute("state", "frozen"))));

            XElement autoFilter = new XElement(
                ns + "autoFilter",
                new XAttribute("ref", $"A1:H{lastRow}"));

            XDocument document = new XDocument(
                new XElement(ns + "worksheet",
                    sheetViews,
                    columns,
                    sheetData,
                    autoFilter));

            WriteXml(archive, "xl/worksheets/sheet2.xml", document);
        }

        private void AddRow(
            XElement sheetData,
            int rowNumber,
            params string[] values)
        {
            XNamespace ns =
                "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            XElement row = new XElement(ns + "row",
                new XAttribute("r", rowNumber));

            for (int i = 0; i < values.Length; i++)
            {
                string cellReference =
                    GetExcelColumnName(i + 1) + rowNumber;

                row.Add(new XElement(ns + "c",
                    new XAttribute("r", cellReference),
                    new XAttribute("t", "inlineStr"),
                    new XElement(ns + "is",
                        new XElement(ns + "t", values[i] ?? ""))));
            }

            sheetData.Add(row);
        }

        private string GetExcelColumnName(int columnNumber)
        {
            string columnName = "";

            while (columnNumber > 0)
            {
                int modulo = (columnNumber - 1) % 26;
                columnName =
                    Convert.ToChar('A' + modulo) + columnName;
                columnNumber =
                    (columnNumber - modulo) / 26 - 1;
            }

            return columnName;
        }

        private void WriteXml(
            ZipArchive archive,
            string path,
            XDocument document)
        {
            ZipArchiveEntry entry = archive.CreateEntry(path);

            using Stream stream = entry.Open();
            document.Save(stream);
        }

        // ============================================================
        // SELECT WALL IN REVIT
        // ============================================================

        private void ResultsGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            WallCheckResult result = ResultsGrid.SelectedItem as WallCheckResult;
            _selectedElementId = result?.ElementId ?? -1;
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get the currently selected row directly from the DataGrid
                WallCheckResult selectedResult =
                    ResultsGrid.SelectedItem as WallCheckResult;

                if (selectedResult == null)
                {
                    MessageBox.Show(
                        "Select a result row first.",
                        "Wall Quality Checker",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                int elementIdValue = selectedResult.ElementId;

                ElementId elementId = new ElementId(elementIdValue);

                Element element = _doc.GetElement(elementId);

                if (element == null)
                {
                    MessageBox.Show(
                        $"Wall with Element ID {elementIdValue} could not be found in the active Revit model.",
                        "Wall Quality Checker",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                // Select the wall in Revit
                _uiDoc.Selection.SetElementIds(
                    new List<ElementId>
                    {
                elementId
                    });

                // Zoom/show the selected wall
                _uiDoc.ShowElements(elementId);

                StatusText.Text =
                    $"Wall {elementIdValue} selected in Revit.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not select the wall in Revit.\n\n{ex.Message}",
                    "Selection Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ============================================================
        // POPULATE ISSUE FILTER
        // ============================================================

        private void PopulateIssueFilter()
        {
            if (IssueFilterComboBox == null)
                return;

            // Clear previous filter values
            IssueFilterComboBox.Items.Clear();

            // First option = show everything
            IssueFilterComboBox.Items.Add("ALL ISSUES");

            // Get unique check categories from the current results
            List<string> issueTypes = _results
                .Where(r => !string.IsNullOrWhiteSpace(r.CheckName))
                .Select(r => r.CheckName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            foreach (string issueType in issueTypes)
            {
                IssueFilterComboBox.Items.Add(issueType);
            }

            // Select ALL ISSUES by default
            IssueFilterComboBox.SelectedIndex = 0;
        }

        // ============================================================
        // FILTER BY CHECK + ISSUE
        // ============================================================

        private void FilterComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (CheckFilterComboBox == null ||
                IssueFilterComboBox == null ||
                ResultsGrid == null)
                return;


            string selectedCheck =
                CheckFilterComboBox.SelectedItem as string;

            string selectedIssue =
                IssueFilterComboBox.SelectedItem as string;


            // Default values
            if (string.IsNullOrWhiteSpace(selectedCheck))
                selectedCheck = "ALL CHECKS";

            if (string.IsNullOrWhiteSpace(selectedIssue))
                selectedIssue = "ALL ISSUES";


            IEnumerable<WallCheckResult> filteredResults =
                _results;


            // --------------------------------------------------------
            // FILTER BY CHECK
            // --------------------------------------------------------

            if (selectedCheck != "ALL CHECKS")
            {
                filteredResults = filteredResults.Where(r =>
                    string.Equals(
                        r.CheckName?.Trim(),
                        selectedCheck.Trim(),
                        StringComparison.OrdinalIgnoreCase));
            }


            // --------------------------------------------------------
            // FILTER BY ISSUE
            // --------------------------------------------------------

            if (selectedIssue != "ALL ISSUES")
            {
                filteredResults = filteredResults.Where(r =>
                    string.Equals(
                        r.Issue?.Trim(),
                        selectedIssue.Trim(),
                        StringComparison.OrdinalIgnoreCase));
            }


            // --------------------------------------------------------
            // DISPLAY RESULTS
            // --------------------------------------------------------

            ResultsGrid.ItemsSource = filteredResults
                .OrderBy(r => r.ElementId)
                .ThenBy(r => r.CheckName)
                .ToList();
        }

        // ============================================================
        // POPULATE BOTH FILTERS
        // ============================================================

        private void PopulateFilters()
        {
            if (CheckFilterComboBox == null ||
                IssueFilterComboBox == null)
                return;


            // --------------------------------------------------------
            // CHECK FILTER
            // --------------------------------------------------------

            CheckFilterComboBox.Items.Clear();

            CheckFilterComboBox.Items.Add("ALL CHECKS");

            List<string> checkTypes = _results
                .Where(r => !string.IsNullOrWhiteSpace(r.CheckName))
                .Select(r => r.CheckName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            foreach (string check in checkTypes)
            {
                CheckFilterComboBox.Items.Add(check);
            }


            // --------------------------------------------------------
            // ISSUE FILTER
            // --------------------------------------------------------

            IssueFilterComboBox.Items.Clear();

            IssueFilterComboBox.Items.Add("ALL ISSUES");

            List<string> issues = _results
                .Where(r => !string.IsNullOrWhiteSpace(r.Issue))
                .Select(r => r.Issue.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            foreach (string issue in issues)
            {
                IssueFilterComboBox.Items.Add(issue);
            }


            // Default selections
            CheckFilterComboBox.SelectedIndex = 0;
            IssueFilterComboBox.SelectedIndex = 0;
        }
    }
}
