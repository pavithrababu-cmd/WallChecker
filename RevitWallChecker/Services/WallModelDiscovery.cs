using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitWallChecker.Services
{
    public class WallModelDiscovery
    {
        private readonly Document _doc;

        public WallModelDiscovery(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public List<Wall> GetWalls()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .ToList();
        }

        public List<WallType> GetUsedWallTypes()
        {
            return GetWalls()
                .Select(w => _doc.GetElement(w.GetTypeId()) as WallType)
                .Where(t => t != null)
                .GroupBy(t => t.Id.IntegerValue)
                .Select(g => g.First())
                .OrderBy(t => t.Name)
                .ToList();
        }

        public List<Level> GetLevels()
        {
            return new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
        }

        public List<string> GetWallWorksets()
        {
            if (!_doc.IsWorkshared)
                return new List<string>();

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Wall wall in GetWalls())
            {
                try
                {
                    Workset workset = _doc.GetWorksetTable().GetWorkset(wall.WorksetId);
                    if (workset != null && !string.IsNullOrWhiteSpace(workset.Name))
                        names.Add(workset.Name);
                }
                catch
                {
                }
            }

            return names.OrderBy(x => x).ToList();
        }

        public List<string> GetWallMaterials()
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Wall wall in GetWalls())
            {
                WallType type = _doc.GetElement(wall.GetTypeId()) as WallType;
                CompoundStructure structure = type?.GetCompoundStructure();
                if (structure == null)
                    continue;

                foreach (CompoundStructureLayer layer in structure.GetLayers())
                {
                    if (layer.MaterialId == ElementId.InvalidElementId)
                        continue;

                    Material material = _doc.GetElement(layer.MaterialId) as Material;
                    if (material != null && !string.IsNullOrWhiteSpace(material.Name))
                        names.Add(material.Name);
                }
            }

            return names.OrderBy(x => x).ToList();
        }

        public List<string> GetWallParameters()
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Wall wall in GetWalls())
            {
                foreach (Parameter parameter in wall.Parameters)
                {
                    string name = parameter?.Definition?.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name);
                }
            }

            return names.OrderBy(x => x).ToList();
        }

        public List<FailureMessage> GetWallWarnings()
        {
            HashSet<int> wallIds = new HashSet<int>(
                GetWalls().Select(w => w.Id.IntegerValue));

            List<FailureMessage> result = new List<FailureMessage>();

            foreach (FailureMessage warning in _doc.GetWarnings())
            {
                ICollection<ElementId> ids = warning.GetFailingElements();
                if (ids == null)
                    continue;

                if (ids.Any(id => wallIds.Contains(id.IntegerValue)))
                    result.Add(warning);
            }

            return result;
        }

        public int GetWallCount() => GetWalls().Count;
        public int GetWallTypeCount() => GetUsedWallTypes().Count;
        public int GetLevelCount() => GetLevels().Count;
        public int GetWallWorksetCount() => GetWallWorksets().Count;
        public int GetWallMaterialCount() => GetWallMaterials().Count;
        public int GetWallWarningCount() => GetWallWarnings().Count;
    }
}
