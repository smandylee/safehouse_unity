using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// The rule data for one hideout facility level: what it needs, what it costs, and what it does.
    /// The C# side of the data in <c>hideout.json</c>.
    /// </summary>
    public sealed class FacilityLevelDefinition
    {
        public FacilityLevelDefinition(int level,
            IReadOnlyList<FacilityRequirement> requirements,
            FacilityCost cost,
            IReadOnlyDictionary<string, object> effects)
        {
            Level = Validate.Integer(level, "Facility level", 0, HideoutLimits.MaxFacilityLevel);
            Requirements = requirements ?? new List<FacilityRequirement>();
            Cost = cost ?? new FacilityCost(0);
            Effects = effects ?? new Dictionary<string, object>();
        }

        public int Level { get; }
        public IReadOnlyList<FacilityRequirement> Requirements { get; }
        public FacilityCost Cost { get; }
        public IReadOnlyDictionary<string, object> Effects { get; }

        public bool TryGet<T>(string key, out T value)
        {
            value = default;
            if (!Effects.TryGetValue(key, out var raw) || raw == null)
            {
                return false;
            }

            if (raw is T typed)
            {
                value = typed;
                return true;
            }

            return false;
        }
    }

    /// <summary>Another facility and level required before this upgrade.</summary>
    public sealed class FacilityRequirement
    {
        public FacilityRequirement(string facilityId, int level)
        {
            FacilityId = Validate.Identifier(facilityId, "facility_id");
            Level = Validate.Integer(level, "Required facility level", 1, HideoutLimits.MaxFacilityLevel);
        }

        public string FacilityId { get; }
        public int Level { get; }
    }

    /// <summary>The price to upgrade a facility to a level.</summary>
    public sealed class FacilityCost
    {
        public FacilityCost(int money, IReadOnlyList<FacilityItemCost> items = null)
        {
            Money = Validate.Integer(money, "Upgrade cost", 0, CoreLimits.MaxMoney);
            Items = items ?? new List<FacilityItemCost>();
        }

        public int Money { get; }
        public IReadOnlyList<FacilityItemCost> Items { get; }
    }

    /// <summary>An item cost for a facility upgrade.</summary>
    public sealed class FacilityItemCost
    {
        public FacilityItemCost(string itemId, int count)
        {
            ItemId = Validate.Identifier(itemId, "item_id");
            Count = Validate.Integer(count, "Item cost count", 1, 10000);
        }

        public string ItemId { get; }
        public int Count { get; }
    }

    /// <summary>The rule data for one facility across all its levels.</summary>
    public sealed class FacilityDefinition
    {
        public FacilityDefinition(string facilityId, string name, string description, int maxLevel, bool decorative,
            IReadOnlyDictionary<int, FacilityLevelDefinition> levels)
        {
            FacilityId = Validate.Identifier(facilityId, "facility_id");
            Name = Validate.Text(name, "Facility name", CoreLimits.MaxItemNameLength);
            Description = Validate.Text(description, "Facility description", CoreLimits.MaxDescriptionLength, allowEmpty: true);
            MaxLevel = Validate.Integer(maxLevel, "Max level", 1, HideoutLimits.MaxFacilityLevel);
            Decorative = decorative;
            Levels = levels ?? new Dictionary<int, FacilityLevelDefinition>();

            if (Levels.Count == 0 || Levels.Keys.Min() != 1 || Levels.Keys.Max() != MaxLevel
                || Enumerable.Range(1, MaxLevel).Any(level => !Levels.ContainsKey(level)))
            {
                throw new ValidationException($"Facility '{facilityId}' must define levels 1 to {MaxLevel}.");
            }
        }

        public string FacilityId { get; }
        public string Name { get; }
        public string Description { get; }
        public int MaxLevel { get; }
        public bool Decorative { get; }
        public IReadOnlyDictionary<int, FacilityLevelDefinition> Levels { get; }

        public FacilityLevelDefinition Level(int level)
        {
            if (!Levels.TryGetValue(level, out var definition))
            {
                throw new ValidationException($"Facility '{FacilityId}' has no level {level}.");
            }

            return definition;
        }
    }

    /// <summary>All hideout rule data loaded from <c>hideout.json</c>.</summary>
    public sealed class HideoutDefinition
    {
        public HideoutDefinition(IReadOnlyDictionary<string, FacilityDefinition> facilities)
        {
            Facilities = facilities ?? new Dictionary<string, FacilityDefinition>();
        }

        public IReadOnlyDictionary<string, FacilityDefinition> Facilities { get; }

        public FacilityDefinition Facility(string facilityId) =>
            Facilities.TryGetValue(facilityId, out var definition)
                ? definition
                : throw new ValidationException($"Facility '{facilityId}' is not defined.");

        public bool HasFacility(string facilityId) => Facilities.ContainsKey(facilityId);

        public FacilityDefinition PersonalRoom =>
            Facilities.TryGetValue("personal_room", out var room)
                ? room
                : throw new ValidationException("Personal room is not defined in hideout data.");
    }
}
