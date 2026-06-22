namespace AntiDupl.NET.WinForms
{
    public enum AutoSelectSide
    {
        DontCare = 0,
        First = 1,   // Select the first image in pair
        Second = 2,  // Select the second image in pair
    }

    public class AutoSelectCriteria
    {
        // Which side to select based on each criterion
        public AutoSelectSide TimeSide = AutoSelectSide.DontCare;      // Older/Newer
        public AutoSelectSide SizeSide = AutoSelectSide.DontCare;      // Smaller/Larger (bytes)
        public AutoSelectSide QualitySide = AutoSelectSide.DontCare;   // Worse/Better
        public AutoSelectSide ResolutionSide = AutoSelectSide.DontCare; // Lower/Higher
        public AutoSelectSide PoolSide = AutoSelectSide.DontCare;      // Pool1/Pool2

        public bool IncludeDefects = false;

        // Predefined criteria for quick selection
        public static AutoSelectCriteria Older => new AutoSelectCriteria { TimeSide = AutoSelectSide.First };
        public static AutoSelectCriteria Newer => new AutoSelectCriteria { TimeSide = AutoSelectSide.Second };
        public static AutoSelectCriteria SmallerFile => new AutoSelectCriteria { SizeSide = AutoSelectSide.First };
        public static AutoSelectCriteria LargerFile => new AutoSelectCriteria { SizeSide = AutoSelectSide.Second };
        public static AutoSelectCriteria LowerResolution => new AutoSelectCriteria { ResolutionSide = AutoSelectSide.First };
        public static AutoSelectCriteria HigherResolution => new AutoSelectCriteria { ResolutionSide = AutoSelectSide.Second };
        public static AutoSelectCriteria WorseQuality => new AutoSelectCriteria { QualitySide = AutoSelectSide.First };
        public static AutoSelectCriteria BetterQuality => new AutoSelectCriteria { QualitySide = AutoSelectSide.Second };
        public static AutoSelectCriteria FromPool1 => new AutoSelectCriteria { PoolSide = AutoSelectSide.First };
        public static AutoSelectCriteria FromPool2 => new AutoSelectCriteria { PoolSide = AutoSelectSide.Second };

        public bool HasCriteria =>
            TimeSide != AutoSelectSide.DontCare ||
            SizeSide != AutoSelectSide.DontCare ||
            QualitySide != AutoSelectSide.DontCare ||
            ResolutionSide != AutoSelectSide.DontCare ||
            PoolSide != AutoSelectSide.DontCare;
    }
}
