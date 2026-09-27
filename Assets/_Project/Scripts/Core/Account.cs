using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// Account-level data: the shared hideout and the order characters appear in. Immutable like a profile.
    /// The C# side of the Python account.json model.
    /// </summary>
    public sealed class Account
    {
        public const int SchemaVersion = 1;
        public const int MaxCharacterOrder = CharacterSheet.MaxCharacters;

        public Account(Hideout hideout, IEnumerable<string> characterOrder, double lastFuelTick = 0.0,
            IEnumerable<ProductionJob> productionJobs = null)
        {
            Hideout = hideout ?? throw new ValidationException("An account needs a hideout.");
            CharacterOrder = (characterOrder ?? Enumerable.Empty<string>()).ToList();
            LastFuelTick = lastFuelTick;
            ProductionJobs = (productionJobs ?? Enumerable.Empty<ProductionJob>()).ToList();
            if (CharacterOrder.Count > MaxCharacterOrder)
            {
                throw new ValidationException($"At most {MaxCharacterOrder} characters can be ordered.");
            }

            foreach (var id in CharacterOrder)
            {
                Validate.Identifier(id, "character_id", instance: true);
            }

            if (CharacterOrder.Distinct().Count() != CharacterOrder.Count)
            {
                throw new ValidationException("A character appears more than once in the order.");
            }
        }

        public Hideout Hideout { get; }
        public IReadOnlyList<string> CharacterOrder { get; }

        /// <summary>Seconds since Unix epoch when fuel was last consumed. 0 means it has never been ticked.</summary>
        public double LastFuelTick { get; }

        /// <summary>Running hideout production jobs. Empty when none are active.</summary>
        public IReadOnlyList<ProductionJob> ProductionJobs { get; }

        public static Account CreateNew() => new Account(Hideout.CreateNew(), null);

        public Account With(Hideout hideout = null, IEnumerable<string> characterOrder = null,
            double? lastFuelTick = null, IEnumerable<ProductionJob> productionJobs = null) =>
            new Account(hideout ?? Hideout, characterOrder ?? CharacterOrder, lastFuelTick ?? LastFuelTick,
                productionJobs ?? ProductionJobs);

        public int Population => CharacterOrder.Count;
    }
}
