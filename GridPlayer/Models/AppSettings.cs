namespace GridPlayer.Models
{
    public class AppSettings
    {
        public int Rows { get; set; } = 2;
        public int Columns { get; set; } = 2;
        public string VideoPath { get; set; } = string.Empty;
        public bool IsSwapEnabled { get; set; } = false;
        public string SelectedGrid1 { get; set; } = "2x2";
        public string SelectedGrid2 { get; set; } = "3x3";
        public int SelectedDelay { get; set; } = 10;
        public bool IsSloMoEnabled { get; set; } = false;
    }
}
