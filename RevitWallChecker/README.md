# Revit Wall Checker - Wall-only Quality Tool

Target: Autodesk Revit 2025 / .NET 8 / x64

## What this version checks

1. Wall parameter integrity
2. Configured required project parameters
3. Wall type validity
4. Configurable wall type naming standard
5. Exact/near-exact duplicate wall geometry
6. Potential wall-end connectivity problems where a nearby wall exists and joining is disallowed
7. Base/top levels, offsets and unconnected heights
8. Worksets and optional approved workset list
9. Configured COBie/asset parameters
10. Material layer validity
11. Geometry/coordinate validity and optional coordinate boundary
12. Revit warnings that actually reference Wall elements

The tool deliberately does NOT turn unknown company standards into fake failures.

## Important configuration

Edit:

Config\WallCheckerSettings.json

The following lists are intentionally empty until the actual client/company rules are known:

- RequiredWallParameters
- CobieParameters
- ApprovedWorksetNames
- ApprovedWallTypeNamePatterns

The engine is ready for those rules, but it cannot invent the client's standards.

## Build

Open the project in Visual Studio 2022 and build:

RevitWallChecker.csproj

The project references:

C:\Program Files\Autodesk\Revit 2025\RevitAPI.dll
C:\Program Files\Autodesk\Revit 2025\RevitAPIUI.dll

If Revit is installed elsewhere, update the two HintPath values.

## Install

1. Close Revit.
2. Build Release.
3. Run `Install-RevitWallChecker.ps1` from PowerShell.
4. Start Revit 2025.
5. Open Add-Ins -> Revit Wall Checker -> Wall Checker.

The installer also unblocks the generated files to reduce the Windows "file is blocked" problem.

## Result interpretation

A failed wall means at least one concrete rule produced an issue for that wall.

A Revit warning is shown only when the warning's failing element IDs contain an actual Wall element. General Revit warnings are not counted as wall warnings.

The tool does not assume that a wall with no nearby wall is automatically "unconnected"; standalone walls can be legitimate. It flags the more actionable case where a wall end is close to another wall but wall joining is explicitly disallowed.

## Output

Use `EXPORT CSV` after a run to create a report containing:

Element ID, Check, Status, Issue, Actual, Expected, Wall Type, Level

Use `SELECT WALL IN REVIT` after selecting a result row to select and zoom to that wall.
