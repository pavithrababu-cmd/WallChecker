namespace RevitWallChecker.Models
{
    public class WallCheckResult
    {
        public int ElementId { get; set; }
        public string CheckName { get; set; }
        public string Status { get; set; }
        public string Issue { get; set; }
        public string ActualValue { get; set; }
        public string ExpectedValue { get; set; }
        public string WallType { get; set; }
        public string Level { get; set; }
    }
}
