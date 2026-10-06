using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RevitWallChecker.Config
{
    public class WallCheckerSettings
    {
        public List<string> RequiredWallParameters { get; set; } = new List<string>();
        public List<string> CobieParameters { get; set; } = new List<string>();
        public List<string> ApprovedWorksetNames { get; set; } = new List<string>();
        public List<string> ApprovedWallTypeNamePatterns { get; set; } = new List<string>();

        // Revit internal units are feet.
        public double DuplicateToleranceFeet { get; set; } = 0.01;
        public double ConnectionToleranceFeet { get; set; } = 0.25;

        // Optional. 0 disables an absolute coordinate boundary check.
        public double CoordinateLimitFeet { get; set; } = 0;

        public bool RequireAtLeastOneWallEndConnectedWhenNearbyWallExists { get; set; } = true;
        public bool CheckWallTypeNameAgainstApprovedPatterns { get; set; } = false;
        public bool CheckWorksetAgainstApprovedNames { get; set; } = false;

        public static WallCheckerSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return new WallCheckerSettings();

                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<WallCheckerSettings>(
                           json,
                           new JsonSerializerOptions
                           {
                               PropertyNameCaseInsensitive = true
                           })
                       ?? new WallCheckerSettings();
            }
            catch
            {
                // Never stop the Revit command because a configuration file is invalid.
                return new WallCheckerSettings();
            }
        }
    }
}
