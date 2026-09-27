namespace Safehouse.Core
{
    /// <summary>
    /// Bounds for the hideout and its facilities. The C# side of the Python config.py limits that belong to
    /// the hideout module.
    /// </summary>
    public static class HideoutLimits
    {
        public const int MaxFacilityLevel = 10;
        public const int MaxRoomLevel = 10;
        public const int MaxPopulation = 10;
        public const int MaxFuelPerHour = 1_000;
        public const int MaxFuel = 100_000;
    }
}
