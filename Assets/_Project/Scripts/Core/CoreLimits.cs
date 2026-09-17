namespace Safehouse.Core
{
    /// <summary>
    /// Bounds shared by validation and placement. The C# side of the Python config.py, kept in its
    /// own type so nothing has to reach into a save or a data file just to learn a limit.
    /// </summary>
    public static class CoreLimits
    {
        /// <summary>Widest or tallest a grid - or a single item - may be, in cells.</summary>
        public const int MaxGrid = 50;

        public const int MaxMoney = 2_000_000_000;
        public const int MaxItemNameLength = 120;
        public const int MaxCategoryLength = 40;
        public const int MaxTagLength = 40;
        public const int MaxTags = 50;
        public const int MaxDescriptionLength = 1000;
        public const int MaxIconPathLength = 260;

        /// <summary>Heaviest a single item may be, in kilograms.</summary>
        public const double MaxItemWeight = 10_000;
    }
}
