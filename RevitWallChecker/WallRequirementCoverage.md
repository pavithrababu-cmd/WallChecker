# Wall-only manager requirement coverage

This package is scoped only to Wall elements.

| Wall requirement area | Implemented |
|---|---|
| Wall parameters | Yes |
| Project/client-required parameters | Yes, via JSON configuration |
| Wall type validity | Yes |
| Wall type/company naming standard | Yes, via JSON wildcard patterns |
| Duplicate/overlapping wall geometry | Yes |
| Wall-end connectivity | Yes, using nearby-wall geometry + Revit wall-join permission |
| Base/top levels and offsets | Yes, discovered dynamically from current model |
| Worksets | Yes; approved workset list is configurable |
| Naming standards | Yes, through wall-type naming patterns |
| COBie/asset information | Yes, through configurable parameter names |
| Materials | Yes, every compound wall layer is checked for a resolvable material |
| Coordinates | Yes; finite geometry always checked, optional coordinate boundary |
| Wall-related Revit warnings | Yes; only warnings whose failing element IDs include Walls |
| Company/project standards | Engine is configurable; no client-specific values are invented |

## Important scope decision

The checker does not hardcode values such as:

- Generic - 8"
- L1
- L2
- Architecture
- a particular workset name
- a particular COBie parameter list

Those vary by project. The active Revit model supplies the model facts, and `Config/WallCheckerSettings.json` supplies optional company/client rules.

## Warning logic

A general Revit warning is not automatically treated as a wall issue.

The checker reads each Revit warning, gets its failing element IDs, and creates a wall issue only when one of those IDs is an actual Wall.

## Connectivity logic

A wall is not marked as unconnected merely because both ends are open. Standalone walls can be valid.

The check reports the actionable case where a wall end is geometrically close to another wall but wall joining is explicitly disallowed.

## Final validation

Because this environment does not contain the Autodesk Revit 2025 API assemblies, the source package cannot be compiled against Revit here. Build it on the development laptop where Revit 2025 is installed. The project is configured for the Revit 2025 API paths already used by the uploaded project.
