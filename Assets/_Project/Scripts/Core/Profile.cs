using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// One character, exactly as saved. Immutable: a change builds a new profile (see <see cref="With"/>),
    /// so a change that fails to save can never leave a half-changed one behind. The C# side of the Python
    /// models.Profile, plus the rig and backpack grids, which the Python save has no concept of (schema 7).
    ///
    /// The constructor checks every field on its own; whether the items exist in the catalog and fit their
    /// grids needs the catalog, and is <see cref="ProfileRules.ValidateAgainstCatalog"/>.
    /// </summary>
    public sealed class Profile
    {
        public const int SchemaVersion = 7;

        public const int DefaultMoney = 500_000;
        public const int DefaultStashWidth = 10;
        public const int DefaultStashHeight = 20;

        // Fixed for now. The data says a rig's or backpack's "capacity" is its cell count (LV-119 = 24,
        // 6Sh118 = 48), so these should eventually follow what is worn; until then every character has these.
        public const int DefaultRigWidth = 6;
        public const int DefaultRigHeight = 4;
        public const int DefaultBackpackWidth = 6;
        public const int DefaultBackpackHeight = 8;

        public Profile(string profileId, string displayName, int money,
            StashGrid stash, StashGrid rig, StashGrid backpack,
            IEnumerable<TraderState> traders, string status, Loadout loadout,
            IReadOnlyDictionary<string, int> bodyParts,
            IReadOnlyDictionary<string, IReadOnlyList<string>> conditions,
            IReadOnlyDictionary<string, string> bio,
            IReadOnlyDictionary<string, int> abilities)
        {
            ProfileId = Validate.Identifier(profileId, "profile_id", instance: true);
            DisplayName = Validate.Text(displayName, "Character name", CharacterSheet.MaxNameLength).Trim();
            if (DisplayName.Length == 0)
            {
                throw new ValidationException("Character name cannot be empty.");
            }

            Money = Validate.Integer(money, "Money");
            Stash = stash ?? throw new ValidationException("A profile needs a stash.");
            Rig = rig ?? throw new ValidationException("A profile needs a rig grid.");
            Backpack = backpack ?? throw new ValidationException("A profile needs a backpack grid.");

            Traders = (traders ?? Enumerable.Empty<TraderState>()).ToList();
            if (Traders.Count > CharacterSheet.MaxTraderStates)
            {
                throw new ValidationException($"At most {CharacterSheet.MaxTraderStates} trader states are kept.");
            }

            if (Traders.Select(state => state.TraderId).Distinct().Count() != Traders.Count)
            {
                throw new ValidationException("A trader appears more than once.");
            }

            if (!CharacterSheet.Statuses.Contains(status))
            {
                throw new ValidationException($"Status must be one of {string.Join(", ", CharacterSheet.Statuses)}.");
            }

            Status = status;
            Loadout = loadout ?? Loadout.Empty;
            BodyParts = CheckBodyParts(bodyParts);
            Conditions = CheckConditions(conditions);
            Bio = CheckBio(bio);
            Abilities = CheckAbilities(abilities);
        }

        public string ProfileId { get; }
        public string DisplayName { get; }
        public int Money { get; }
        public StashGrid Stash { get; }
        public StashGrid Rig { get; }
        public StashGrid Backpack { get; }
        public IReadOnlyList<TraderState> Traders { get; }
        public string Status { get; }
        public Loadout Loadout { get; }
        public IReadOnlyDictionary<string, int> BodyParts { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Conditions { get; }
        public IReadOnlyDictionary<string, string> Bio { get; }
        public IReadOnlyDictionary<string, int> Abilities { get; }

        /// <summary>Total hit points: derived from the body parts, never stored.</summary>
        public int Health => BodyParts.Values.Sum();

        /// <summary>A brand-new healthy character with nothing on, in nothing.</summary>
        public static Profile CreateNew(string profileId, string displayName, int money = DefaultMoney,
            int stashWidth = DefaultStashWidth, int stashHeight = DefaultStashHeight) =>
            new Profile(profileId, displayName, money,
                new StashGrid(stashWidth, stashHeight),
                new StashGrid(DefaultRigWidth, DefaultRigHeight),
                new StashGrid(DefaultBackpackWidth, DefaultBackpackHeight),
                null, CharacterSheet.Active, Loadout.Empty,
                CharacterSheet.BodyPartMaxHealth.ToDictionary(pair => pair.Key, pair => pair.Value),
                new Dictionary<string, IReadOnlyList<string>>(),
                CharacterSheet.BioFields.ToDictionary(field => field, field => ""),
                CharacterSheet.Abilities.ToDictionary(ability => ability, ability => CharacterSheet.AbilityBase));

        /// <summary>A copy with some parts replaced; anything left out is kept.</summary>
        public Profile With(StashGrid stash = null, StashGrid rig = null, StashGrid backpack = null,
            Loadout loadout = null, int? money = null, string status = null) =>
            new Profile(ProfileId, DisplayName, money ?? Money,
                stash ?? Stash, rig ?? Rig, backpack ?? Backpack,
                Traders, status ?? Status, loadout ?? Loadout,
                BodyParts, Conditions, Bio, Abilities);

        private static IReadOnlyDictionary<string, int> CheckBodyParts(IReadOnlyDictionary<string, int> parts)
        {
            RequireExactKeys(parts?.Keys, CharacterSheet.BodyParts, "Body parts");
            foreach (var pair in CharacterSheet.BodyPartMaxHealth)
            {
                Validate.Integer(parts[pair.Key], $"Body part {pair.Key}", 0, pair.Value);
            }

            return CharacterSheet.BodyParts.ToDictionary(name => name, name => parts[name]);
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> CheckConditions(
            IReadOnlyDictionary<string, IReadOnlyList<string>> conditions)
        {
            var result = new Dictionary<string, IReadOnlyList<string>>();
            foreach (var part in CharacterSheet.BodyParts)
            {
                if (conditions == null || !conditions.TryGetValue(part, out var list))
                {
                    continue;
                }

                if (list == null || list.Count == 0 || list.Distinct().Count() != list.Count
                    || list.Any(name => !CharacterSheet.ConditionNames.Contains(name)))
                {
                    throw new ValidationException(
                        $"Conditions for {part} must be a non-empty list of distinct: {string.Join(", ", CharacterSheet.ConditionNames)}.");
                }

                result[part] = list.ToList();
            }

            if (conditions != null && conditions.Keys.Any(key => !result.ContainsKey(key)))
            {
                throw new ValidationException("Conditions name a body part that does not exist.");
            }

            return result;
        }

        private static IReadOnlyDictionary<string, string> CheckBio(IReadOnlyDictionary<string, string> bio)
        {
            RequireExactKeys(bio?.Keys, CharacterSheet.BioFields, "Bio");
            foreach (var field in CharacterSheet.BioFields)
            {
                Validate.Text(bio[field], $"Bio {field}", CharacterSheet.MaxBioLength, allowEmpty: true);
            }

            return CharacterSheet.BioFields.ToDictionary(field => field, field => bio[field]);
        }

        private static IReadOnlyDictionary<string, int> CheckAbilities(IReadOnlyDictionary<string, int> abilities)
        {
            RequireExactKeys(abilities?.Keys, CharacterSheet.Abilities, "Abilities");
            var spent = 0;
            foreach (var name in CharacterSheet.Abilities)
            {
                spent += CharacterSheet.AbilityCost(abilities[name]); // throws on a score that is not a tier
            }

            if (spent > CharacterSheet.AbilityBonusPool)
            {
                throw new ValidationException(
                    $"Abilities spend {spent} bonus points; at most {CharacterSheet.AbilityBonusPool} are available.");
            }

            return CharacterSheet.Abilities.ToDictionary(name => name, name => abilities[name]);
        }

        private static void RequireExactKeys(IEnumerable<string> found, IEnumerable<string> expected, string label)
        {
            if (found == null || !new HashSet<string>(found).SetEquals(expected))
            {
                throw new ValidationException($"{label} must have exactly: {string.Join(", ", expected)}.");
            }
        }
    }
}
