namespace GridVids.Models
{
    public class AppSettings
    {
        public int Rows { get; set; } = 2;
        public int Columns { get; set; } = 2;
        public string VideoPath { get; set; } = string.Empty;
        public bool IsSwapEnabled { get; set; } = false;
        public bool IsSingleVidEnabled { get; set; } = false;
        public bool IsRandomStartEnabled { get; set; } = true;
        public string SelectedGrid1 { get; set; } = "2x2";
        public string SelectedGrid2 { get; set; } = "3x3";
        public int SelectedDelay { get; set; } = 10;
    }
}
