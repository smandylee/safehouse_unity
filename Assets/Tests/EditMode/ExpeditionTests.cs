using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Safehouse.Core;
using Safehouse.Data;

namespace Safehouse.Tests
{
    /// <summary>
    /// The two expedition kinds from the Python build: a real-time simulated trip, and a direct node map.
    /// Neither one reads skills or classes.
    /// </summary>
    public sealed class ExpeditionTests
    {
        [Test]
        public void PythonRandomMatchesThePythonSequence()
        {
            var random = PythonRandom.Seed(1);
            Assert.AreEqual(0.13436424411240122, random.Random(), 1e-12);
            Assert.AreEqual(0.8474337369372327, random.Random(), 1e-12);
            Assert.AreEqual(0.763774618976614, random.Random(), 1e-12);
            Assert.AreEqual(0.2550690257394217, random.Random(), 1e-12);
            CollectionAssert.AreEqual(new[] { 2, 2, 2, 3, 2, 1 }, Enumerable.Range(0, 6).Select(_ => random.RandInt(1, 3)).ToArray());
            CollectionAssert.AreEqual(new[] { 12, 62, 3, 49 }, Enumerable.Range(0, 4).Select(_ => random.RandRange(100)).ToArray());
            CollectionAssert.AreEqual(new[] { "b", "c", "a", "b" },
                Enumerable.Range(0, 4).Select(_ => random.Choices(new[] { "a", "b", "c" }, new[] { 1d, 2d, 3d })).ToArray());
            CollectionAssert.AreEqual(new[] { "y", "x", "y" },
                Enumerable.Range(0, 3).Select(_ => random.Choices(new[] { "x", "y" }, new[] { 1.5, 2.5 })).ToArray());

            var again = PythonRandom.Seed(1);
            var state = again.GetState();
            var first = again.RandRange(1000);
            again.SetState(state.Version, state.Words, state.Index, state.GaussNext);
            Assert.AreEqual(first, again.RandRange(1000));
        }

        [Test]
        public void ZoneTimeMatchesThePythonClock()
        {
            Assert.AreEqual(1800, ExpeditionRules.ZoneSeconds("outer", false));
            Assert.AreEqual(2700, ExpeditionRules.ZoneSeconds("outer", true));
            Assert.AreEqual(2700, ExpeditionRules.ZoneSeconds("center", false));
            Assert.AreEqual(4050, ExpeditionRules.ZoneSeconds("center", true));
            Assert.AreEqual(3600, ExpeditionRules.ZoneSeconds("deep", false));
            Assert.AreEqual(5400, ExpeditionRules.ZoneSeconds("deep", true));
        }

        [Test]
        public void ADirectExpeditionIsNeverDue()
        {
            var route = new Route("n0", new Dictionary<string, RouteNode>
            {
                ["n0"] = new RouteNode("n0", "start", 0, 0.5, new[] { "n1" }, false, null, null),
                ["n1"] = new RouteNode("n1", "exit", 1, 0.5, new string[0], false, null, null),
            });
            var random = PythonRandom.Seed(1);
            var direct = ExpeditionRules.Create("customs", false, new[] { ProfileId() }, 1_000, ExpeditionModes.Direct, route, random);
            Assert.IsFalse(ExpeditionRules.Due(direct, 1_000 + 10_000_000));

            var simulated = ExpeditionRules.Create("customs", false, new[] { ProfileId() }, 1_000, ExpeditionModes.Simulation, null, PythonRandom.Seed(1));
            Assert.IsFalse(ExpeditionRules.Due(simulated, 1_000 + 1799));
            Assert.IsTrue(ExpeditionRules.Due(simulated, 1_000 + 1800));
        }

        [Test]
        public void TheSameSeedBuildsTheSameForwardRoute()
        {
            var catalog = CatalogLoader.Load();
            var maps = MapLoader.LoadMaps(catalog);
            var gear = GearLoader.Load(catalog);
            var combat = CombatLoader.Load();
            var rules = RaidLoader.Load();
            var zone = maps["customs"].Zone("outer", false);

            var first = RaidMaps.Generate(zone, false, rules, gear, combat, PythonRandom.Seed(4));
            var second = RaidMaps.Generate(zone, false, rules, gear, combat, PythonRandom.Seed(4));
            CollectionAssert.AreEqual(first.Nodes.Keys.OrderBy(id => id).ToList(), second.Nodes.Keys.OrderBy(id => id).ToList());
            Assert.AreEqual("start", first.Nodes[first.StartNodeId].Kind);
            Assert.AreEqual(1, first.Nodes.Values.Count(node => node.Kind == "exit"));
            foreach (var node in first.Nodes.Values)
            {
                foreach (var neighbor in node.Neighbors)
                {
                    Assert.Greater(first.Nodes[neighbor].X, node.X);
                }
            }
        }

        [Test]
        public void AbilitiesDoNotChangeASimulatedZone()
        {
            var world = LoadWorld();
            var calm = Profile.CreateNew(ProfileId(), "Calm");
            var gifted = calm.With(abilities: CharacterSheet.Abilities.ToDictionary(
                name => name, name => name == "strength" ? 10 : name == "looks" ? 3 : CharacterSheet.AbilityBase));
            var first = Resolve(world, calm, 9);
            var second = Resolve(world, gifted, 9);
            CollectionAssert.AreEqual(first.Expedition.Log, second.Expedition.Log);
            CollectionAssert.AreEqual(first.Expedition.Loot.SelectMany(pair => pair.Value).ToList(),
                second.Expedition.Loot.SelectMany(pair => pair.Value).ToList());
        }

        [Test]
        public void ExtractReturnsAStandingCharacterAndKeepsADownedOne()
        {
            var folder = Path.Combine(Path.GetTempPath(), "safehouse-expedition-" + Path.GetRandomFileName());
            Directory.CreateDirectory(folder);
            try
            {
                var catalog = CatalogLoader.Load();
                var profiles = new ProfileRepository(folder, catalog);
                var accounts = new AccountRepository(folder);
                accounts.Save(Account.CreateNew());
                var character = profiles.Create("Raider");
                var session = Session(folder, catalog, profiles, accounts);
                var launched = session.Launch("factory", false, new[] { character }, 5_000, ExpeditionModes.Simulation);
                Assert.AreEqual(CharacterSheet.OnExpedition, profiles.Load(character.ProfileId).Status);

                var due = launched.ZoneStartedAt + ExpeditionRules.ZoneSeconds(launched.ZoneId, false);
                var resolved = session.Advance(launched, due);
                if (resolved.Status == ExpeditionStatus.AwaitingChoice)
                {
                    var extracted = session.ChooseExtract(resolved);
                    Assert.AreEqual("extracted", extracted.Outcome);
                    var after = profiles.Load(character.ProfileId);
                    Assert.AreEqual(after.Status == CharacterSheet.Downed ? CharacterSheet.Downed : CharacterSheet.Active, after.Status);
                    if (after.Status == CharacterSheet.Active)
                    {
                        Assert.IsNull(session.FindFor(character.ProfileId));
                    }
                }
                else
                {
                    Assert.AreEqual("wiped", resolved.Outcome);
                    Assert.AreEqual(CharacterSheet.Downed, profiles.Load(character.ProfileId).Status);
                }
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private static (Expedition Expedition, Dictionary<string, Fighter> Fighters) Resolve(LoadedWorld world, Profile profile, int seed)
        {
            var expedition = ExpeditionRules.Create(world.Map.MapId, false, new[] { profile.ProfileId }, 100,
                ExpeditionModes.Simulation, null, PythonRandom.Seed(seed));
            expedition = expedition.With(zoneStartedAt: 0);
            var fighters = new Dictionary<string, Fighter>
            {
                [profile.ProfileId] = ExpeditionRules.BuildFighter(profile, world.Gear),
            };
            var limits = new Dictionary<string, int> { [profile.ProfileId] = ExpeditionRules.CarryLimit(profile, world.Gear) };
            return ExpeditionRules.ResolveZone(expedition, world.Map, world.Catalog, world.Tables, world.Gear, world.Combat,
                fighters, limits);
        }

        private static LoadedWorld LoadWorld()
        {
            var catalog = CatalogLoader.Load();
            return new LoadedWorld
            {
                Catalog = catalog,
                Map = MapLoader.LoadMaps(catalog)["customs"],
                Gear = GearLoader.Load(catalog),
                Combat = CombatLoader.Load(),
                Tables = new LootTables(catalog, MapLoader.LoadContainers(catalog)),
            };
        }

        private static ExpeditionSession Session(string folder, Dictionary<string, ItemDefinition> catalog,
            ProfileRepository profiles, AccountRepository accounts)
        {
            return new ExpeditionSession(new ExpeditionRepository(folder), profiles, accounts,
                MapLoader.LoadMaps(catalog), MapLoader.LoadContainers(catalog), GearLoader.Load(catalog),
                CombatLoader.Load(), RaidLoader.Load(), catalog);
        }

        private static int _next = 1;
        private static string ProfileId() => (_next++).ToString("x32");

        private sealed class LoadedWorld
        {
            public Dictionary<string, ItemDefinition> Catalog;
            public MapDefinition Map;
            public GearData Gear;
            public CombatNumbers Combat;
            public LootTables Tables;
        }
    }
}
