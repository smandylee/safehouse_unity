using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Safehouse.Core;

namespace Safehouse.Data
{
    /// <summary>
    /// One file per expedition, under expeditions/. A completed expedition is not "the character's current
    /// trip": once it is done, a downed member is read from their status.
    /// </summary>
    public sealed class ExpeditionRepository
    {
        private static readonly Regex IdPattern = new Regex("^[0-9a-f]{32}$", RegexOptions.Compiled);
        private readonly string _folder;

        public ExpeditionRepository(string dataFolder)
        {
            _folder = Path.Combine(dataFolder, "expeditions");
            DataFolder = dataFolder;
        }

        public string DataFolder { get; }

        public string PathFor(string expeditionId)
        {
            if (expeditionId == null || !IdPattern.IsMatch(expeditionId))
            {
                throw new StorageException($"Invalid expedition id: '{expeditionId}'.");
            }

            return Path.Combine(_folder, expeditionId + ".json");
        }

        public IReadOnlyList<string> ListIds()
        {
            if (!Directory.Exists(_folder))
            {
                return new string[0];
            }

            return Directory.GetFiles(_folder, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(id => IdPattern.IsMatch(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
        }

        public Expedition Load(string expeditionId)
        {
            var path = PathFor(expeditionId);
            try
            {
                var text = File.ReadAllText(path);
                return ExpeditionSerializer.FromJson(JObject.Parse(text));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                                          || error is Newtonsoft.Json.JsonException || error is ValidationException)
            {
                throw new StorageException($"Cannot load expedition {Path.GetFileName(path)}: {error.Message}\nThe file was left unchanged.", error);
            }
        }

        public void Save(Expedition expedition)
        {
            ExpeditionSerializer.FromJson(ExpeditionSerializer.ToJson(expedition));
            Directory.CreateDirectory(_folder);
            try
            {
                AtomicFile.Write(PathFor(expedition.ExpeditionId), ExpeditionSerializer.Serialize(expedition));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                throw new StorageException($"Could not save the expedition: {error.Message}\nThe requested change was not applied.", error);
            }
        }

        public Expedition FindFor(string profileId)
        {
            foreach (var expeditionId in ListIds())
            {
                var expedition = Load(expeditionId);
                if (expedition.Party.Contains(profileId) && expedition.Status != ExpeditionStatus.Completed)
                {
                    return expedition;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Launch, catch up and resolve an expedition. The expedition file and every changed character are
    /// written together: if a later write fails, the files already written are put back.
    /// </summary>
    public sealed class ExpeditionSession
    {
        private readonly ExpeditionRepository _expeditions;
        private readonly ProfileRepository _profiles;
        private readonly AccountRepository _accounts;
        private readonly IReadOnlyDictionary<string, MapDefinition> _maps;
        private readonly ContainerRules _containers;
        private readonly GearData _gear;
        private readonly CombatNumbers _combat;
        private readonly RaidRules _raids;
        private readonly IReadOnlyDictionary<string, ItemDefinition> _catalog;

        public ExpeditionSession(ExpeditionRepository expeditions, ProfileRepository profiles, AccountRepository accounts,
            IReadOnlyDictionary<string, MapDefinition> maps, ContainerRules containers, GearData gear,
            CombatNumbers combat, RaidRules raids, IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            _expeditions = expeditions;
            _profiles = profiles;
            _accounts = accounts;
            _maps = maps;
            _containers = containers;
            _gear = gear;
            _combat = combat;
            _raids = raids;
            _catalog = catalog;
        }

        public Expedition FindFor(string profileId) => _expeditions.FindFor(profileId);

        public Expedition Launch(string mapId, bool night, IReadOnlyList<Profile> party, double now, string mode)
        {
            if (!_maps.ContainsKey(mapId))
            {
                throw new ValidationException($"Unknown map: {mapId}.");
            }

            foreach (var profile in party)
            {
                if (profile.Status != CharacterSheet.Active)
                {
                    throw new ValidationException($"{profile.DisplayName} is not available for an expedition.");
                }
            }

            var map = _maps[mapId];
            var random = PythonRandom.Create();
            Route route = null;
            string zoneId = null;
            string locationId = null;
            if (mode == ExpeditionModes.Direct)
            {
                if (map.Locations != null)
                {
                    var entry = map.Locations.Get(map.Locations.EntryLocationId);
                    zoneId = entry.ZoneId;
                    locationId = entry.LocationId;
                }
                else
                {
                    zoneId = MapZones.Ids[0];
                }

                route = RaidMaps.Generate(map.Zone(zoneId, night), night, _raids, _gear, _combat, random);
            }

            var expedition = ExpeditionRules.Create(mapId, night, party.Select(profile => profile.ProfileId).ToList(),
                now, mode, route, random, zoneId, locationId);
            var updated = party.ToDictionary(profile => profile.ProfileId,
                profile => profile.With(status: CharacterSheet.OnExpedition));
            Commit(expedition, updated, null);
            return expedition;
        }

        public Expedition Advance(Expedition expedition, double now)
        {
            if (!ExpeditionRules.Due(expedition, now))
            {
                return expedition;
            }

            var current = LoadParty(expedition);
            var fighters = Fighters(current);
            var limits = Limits(current);
            var resolved = ExpeditionRules.ResolveZone(expedition, _maps[expedition.MapId], _catalog,
                new LootTables(_catalog, _containers), _gear, _combat, fighters, limits);
            Commit(resolved.Expedition, ExpeditionRules.SyncProfiles(current, resolved.Fighters), null);
            return resolved.Expedition;
        }

        public Expedition Move(Expedition expedition, string nodeId)
        {
            var current = LoadParty(expedition);
            var resolved = ExpeditionRules.Move(expedition, nodeId, _maps[expedition.MapId], _combat, _raids, Fighters(current));
            Commit(resolved.Expedition, ExpeditionRules.SyncProfiles(current, resolved.Fighters), null);
            return resolved.Expedition;
        }

        public Expedition Choose(Expedition expedition, int choiceIndex)
        {
            var current = LoadParty(expedition);
            var resolved = ExpeditionRules.Choose(expedition, choiceIndex, _maps[expedition.MapId], _catalog,
                new LootTables(_catalog, _containers), _raids, _gear, _combat, Fighters(current), Limits(current));
            var changed = ExpeditionRules.SyncProfiles(current, resolved.Fighters);
            var done = resolved.Expedition;
            if (done.Loot.Count > 0)
            {
                var baseProfiles = done.Loot.Keys.ToDictionary(id => id, id => changed.ContainsKey(id) ? changed[id] : current[id]);
                var delivery = Deliver(done.Loot, baseProfiles);
                foreach (var pair in delivery.Profiles)
                {
                    changed[pair.Key] = pair.Value;
                }

                var discarded = done.Discarded.ToList();
                foreach (var items in delivery.Leftover.Values)
                {
                    discarded.AddRange(items);
                }

                done = done.With(loot: new Dictionary<string, List<string>>(), discarded: discarded);
            }

            Commit(done, changed, null);
            return done;
        }

        public Expedition LeaveZone(Expedition expedition, string action)
        {
            var current = LoadParty(expedition);
            var resolved = ExpeditionRules.LeaveZone(expedition, action, _maps[expedition.MapId], _raids, _gear, _combat,
                Fighters(current));
            Commit(resolved.Expedition, ExpeditionRules.SyncProfiles(current, resolved.Fighters), null);
            return resolved.Expedition.Status == ExpeditionStatus.AwaitingChoice
                ? ChooseExtract(resolved.Expedition)
                : resolved.Expedition;
        }

        public Expedition ChooseContinue(Expedition expedition, double now)
        {
            var advanced = ExpeditionRules.Continue(expedition, now);
            _expeditions.Save(advanced);
            return advanced;
        }

        public Expedition ChooseExtract(Expedition expedition)
        {
            var done = ExpeditionRules.Extract(expedition);
            var current = LoadParty(expedition);
            var deliverable = new Dictionary<string, List<string>>();
            foreach (var pair in done.Loot)
            {
                if (current[pair.Key].Status != CharacterSheet.Downed)
                {
                    deliverable[pair.Key] = pair.Value;
                }
            }

            var delivery = Deliver(deliverable, current);
            var changed = new Dictionary<string, Profile>();
            foreach (var profileId in expedition.Party)
            {
                if (current[profileId].Status == CharacterSheet.Downed)
                {
                    continue;
                }

                var profile = delivery.Profiles.ContainsKey(profileId) ? delivery.Profiles[profileId] : current[profileId];
                changed[profileId] = profile.With(status: CharacterSheet.Active);
            }

            var kept = new Dictionary<string, List<string>>();
            foreach (var pair in done.Loot)
            {
                if (!deliverable.ContainsKey(pair.Key))
                {
                    kept[pair.Key] = pair.Value.ToList();
                }
            }

            foreach (var pair in delivery.Leftover)
            {
                kept[pair.Key] = pair.Value;
            }

            done = done.With(loot: kept);
            Commit(done, changed, null);
            return done;
        }

        private Dictionary<string, Profile> LoadParty(Expedition expedition) =>
            expedition.Party.ToDictionary(id => id, id => _profiles.Load(id));

        private Dictionary<string, Fighter> Fighters(Dictionary<string, Profile> party) =>
            party.ToDictionary(pair => pair.Key, pair => ExpeditionRules.BuildFighter(pair.Value, _gear));

        private Dictionary<string, int> Limits(Dictionary<string, Profile> party) =>
            party.ToDictionary(pair => pair.Key, pair => ExpeditionRules.CarryLimit(pair.Value, _gear));

        /// <summary>
        /// Puts finds into each character's own stash. This build has no separate shared hideout stash
        /// (the personal room is the character stash), so anything that does not fit stays on the expedition.
        /// </summary>
        private (Dictionary<string, Profile> Profiles, Dictionary<string, List<string>> Leftover) Deliver(
            IReadOnlyDictionary<string, List<string>> loot, IReadOnlyDictionary<string, Profile> profiles)
        {
            var changed = new Dictionary<string, Profile>();
            var leftover = new Dictionary<string, List<string>>();
            foreach (var pair in loot)
            {
                var profile = profiles[pair.Key];
                var stash = profile.Stash;
                var kept = new List<string>();
                foreach (var itemId in pair.Value)
                {
                    var spot = PlacementRules.FirstFit(stash, _catalog, itemId);
                    if (spot == null)
                    {
                        kept.Add(itemId);
                        continue;
                    }

                    var instance = ItemInstance.Create(Guid.NewGuid().ToString("N"), itemId, spot.Value.X, spot.Value.Y, spot.Value.Rotation);
                    stash = stash.With(stash.Stash.Concat(new[] { instance }));
                }

                if (kept.Count > 0)
                {
                    leftover[pair.Key] = kept;
                }

                if (stash != profile.Stash)
                {
                    changed[pair.Key] = profile.With(stash: stash);
                }
            }

            return (changed, leftover);
        }

        private void Commit(Expedition expedition, Dictionary<string, Profile> profiles, Account account)
        {
            var snapshots = new List<(string Path, byte[] Bytes)>();
            snapshots.Add(( _expeditions.PathFor(expedition.ExpeditionId), ReadIfExists(_expeditions.PathFor(expedition.ExpeditionId)) ));
            foreach (var profileId in profiles.Keys)
            {
                var path = _profiles.PathFor(profileId);
                snapshots.Add((path, ReadIfExists(path)));
            }

            if (account != null)
            {
                var path = Path.Combine(_accounts.DataFolder, "account.json");
                snapshots.Add((path, ReadIfExists(path)));
            }

            try
            {
                _expeditions.Save(expedition);
                foreach (var profile in profiles.Values)
                {
                    _profiles.Save(profile);
                }

                if (account != null)
                {
                    _accounts.Save(account);
                }
            }
            catch (Exception error)
            {
                for (var i = snapshots.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        if (snapshots[i].Bytes == null)
                        {
                            if (File.Exists(snapshots[i].Path))
                            {
                                File.Delete(snapshots[i].Path);
                            }
                        }
                        else
                        {
                            AtomicFile.Write(snapshots[i].Path, snapshots[i].Bytes);
                        }
                    }
                    catch (IOException)
                    {
                        // The error below still says the save failed.
                    }
                }

                throw new StorageException($"Could not save the expedition: {error.Message}\nNothing was changed.", error);
            }
        }

        private static byte[] ReadIfExists(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
