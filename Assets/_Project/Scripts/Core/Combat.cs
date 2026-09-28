using System;
using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// How a fight during an expedition is decided. Gear numbers come from the game; every tunable
    /// number lives in combat.json. A fight never reads character skills or classes.
    /// </summary>
    public sealed class CombatNumbers
    {
        public CombatNumbers(
            IReadOnlyDictionary<string, int> weapon,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> engagement,
            IReadOnlyDictionary<string, int> armor,
            IReadOnlyDictionary<string, (int Low, int High)> encounters,
            int nightPercent, int bossDamagePercent, int bossChancePercent,
            IReadOnlyDictionary<string, int> scav, string scavName,
            int partyExtraPercent, int swingLow, int swingHigh,
            IReadOnlyDictionary<string, int> unarmed, int medsUseBelowPercent,
            int shotsLandPercent, int enemySkillPercent,
            IReadOnlyDictionary<string, int> practicalRate, int maxSeconds,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> hitLocation,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> injuryChances,
            IReadOnlyDictionary<string, int> untreatedBleedDamage, int fractureFirepowerPenaltyPercent)
        {
            Weapon = weapon;
            Engagement = engagement;
            Armor = armor;
            Encounters = encounters;
            NightPercent = nightPercent;
            BossDamagePercent = bossDamagePercent;
            BossChancePercent = bossChancePercent;
            Scav = scav;
            ScavName = scavName;
            PartyExtraPercent = partyExtraPercent;
            SwingLow = swingLow;
            SwingHigh = swingHigh;
            Unarmed = unarmed;
            MedsUseBelowPercent = medsUseBelowPercent;
            ShotsLandPercent = shotsLandPercent;
            EnemySkillPercent = enemySkillPercent;
            PracticalRate = practicalRate;
            MaxSeconds = maxSeconds;
            HitLocation = hitLocation;
            InjuryChances = injuryChances;
            UntreatedBleedDamage = untreatedBleedDamage;
            FractureFirepowerPenaltyPercent = fractureFirepowerPenaltyPercent;
        }

        public IReadOnlyDictionary<string, int> Weapon { get; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Engagement { get; }
        public IReadOnlyDictionary<string, int> Armor { get; }
        public IReadOnlyDictionary<string, (int Low, int High)> Encounters { get; }
        public int NightPercent { get; }
        public int BossDamagePercent { get; }
        public int BossChancePercent { get; }
        public IReadOnlyDictionary<string, int> Scav { get; }
        public string ScavName { get; }
        public int PartyExtraPercent { get; }
        public int SwingLow { get; }
        public int SwingHigh { get; }
        public IReadOnlyDictionary<string, int> Unarmed { get; }
        public int MedsUseBelowPercent { get; }
        public int ShotsLandPercent { get; }
        public int EnemySkillPercent { get; }
        public IReadOnlyDictionary<string, int> PracticalRate { get; }
        public int MaxSeconds { get; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> HitLocation { get; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> InjuryChances { get; }
        public IReadOnlyDictionary<string, int> UntreatedBleedDamage { get; }
        public int FractureFirepowerPenaltyPercent { get; }
    }

    public sealed class Enemy
    {
        public Enemy(string name, int health, int armorClass, int penetration, int damage, int fireRate, bool boss)
        {
            Name = name;
            Health = health;
            ArmorClass = armorClass;
            Penetration = penetration;
            Damage = damage;
            FireRate = fireRate;
            Boss = boss;
        }

        public string Name { get; }
        public int Health { get; }
        public int ArmorClass { get; }
        public int Penetration { get; }
        public int Damage { get; }
        public int FireRate { get; }
        public bool Boss { get; }

        public static Enemy FromMob(MobStats mob, bool boss = true) =>
            new Enemy(mob.Name, mob.Health, mob.ArmorClass, mob.Penetration, mob.Damage, mob.FireRate, boss);

        public static Enemy Scav(CombatNumbers numbers) =>
            new Enemy(numbers.ScavName, numbers.Scav["health"], numbers.Scav["armor_class"],
                numbers.Scav["penetration"], numbers.Scav["damage"], numbers.Scav["fire_rate"], false);
    }

    /// <summary>One character as they stand in a fight. Head or thorax at 0 downs them even if other parts remain.</summary>
    public sealed class Fighter
    {
        public Fighter(string characterId, string name, IReadOnlyDictionary<string, int> bodyParts,
            IReadOnlyDictionary<string, int> maxBodyParts, IReadOnlyDictionary<string, string[]> conditions,
            WeaponStats weapon, AmmoStats ammo, IReadOnlyDictionary<string, int> armorClasses, int healPool,
            bool stopsLightBleed, bool stopsHeavyBleed, bool treatsFracture)
        {
            CharacterId = characterId;
            Name = name;
            BodyParts = new Dictionary<string, int>(bodyParts);
            MaxBodyParts = new Dictionary<string, int>(maxBodyParts);
            Conditions = new Dictionary<string, string[]>();
            foreach (var pair in conditions)
            {
                Conditions[pair.Key] = (string[])pair.Value.Clone();
            }

            Weapon = weapon;
            Ammo = ammo;
            ArmorClasses = new Dictionary<string, int>(armorClasses);
            HealPool = healPool;
            StopsLightBleed = stopsLightBleed;
            StopsHeavyBleed = stopsHeavyBleed;
            TreatsFracture = treatsFracture;
        }

        public string CharacterId { get; }
        public string Name { get; }
        public Dictionary<string, int> BodyParts { get; }
        public Dictionary<string, int> MaxBodyParts { get; }
        public Dictionary<string, string[]> Conditions { get; }
        public WeaponStats Weapon { get; }
        public AmmoStats Ammo { get; }
        public Dictionary<string, int> ArmorClasses { get; }
        public int HealPool { get; }
        public bool StopsLightBleed { get; }
        public bool StopsHeavyBleed { get; }
        public bool TreatsFracture { get; }

        public int Health => BodyParts.Values.Sum();
        public int MaxHealth => MaxBodyParts.Values.Sum();
        public bool Standing => Health > 0 && BodyParts["head"] > 0 && BodyParts["thorax"] > 0;
        public bool Fractured => Conditions.Values.Any(list => list.Contains("fracture"));

        public Fighter Copy() => new Fighter(CharacterId, Name, BodyParts, MaxBodyParts, Conditions,
            Weapon, Ammo, ArmorClasses, HealPool, StopsLightBleed, StopsHeavyBleed, TreatsFracture);
    }

    public sealed class EncounterResult
    {
        public EncounterResult(string enemy, bool won, int seconds, Dictionary<string, int> damage,
            string[] downed, Dictionary<string, int> healed, Dictionary<string, string> hitPart,
            Dictionary<string, string[]> newConditions, Dictionary<string, string[]> treatedConditions)
        {
            Enemy = enemy;
            Won = won;
            Seconds = seconds;
            Damage = damage;
            Downed = downed;
            Healed = healed;
            HitPart = hitPart;
            NewConditions = newConditions;
            TreatedConditions = treatedConditions;
        }

        public string Enemy { get; }
        public bool Won { get; }
        public int Seconds { get; }
        public Dictionary<string, int> Damage { get; }
        public string[] Downed { get; }
        public Dictionary<string, int> Healed { get; }
        public Dictionary<string, string> HitPart { get; }
        public Dictionary<string, string[]> NewConditions { get; }
        public Dictionary<string, string[]> TreatedConditions { get; }

        public string Summary()
        {
            var hurt = string.Join(", ", Damage.Where(pair => pair.Value > 0)
                .Select(pair => $"{pair.Key} -{pair.Value} ({(HitPart.TryGetValue(pair.Key, out var part) ? part : "body")})"));
            if (hurt.Length == 0)
            {
                hurt = "no damage";
            }

            var ending = Won ? "beaten" : "broke off";
            var notes = NewConditions.Where(pair => pair.Value.Length > 0)
                .Select(pair => $"{pair.Key} {string.Join(", ", pair.Value)}").ToList();
            var note = notes.Count > 0 ? "; " + string.Join(", ", notes) : "";
            return $"{Enemy} {ending} in {Seconds}s ({hurt}{note})";
        }
    }

    public static class CombatRules
    {
        public const int Percent = 100;

        public static int PenetrationPercent(int penetration, int armorClass, CombatNumbers numbers)
        {
            var needed = armorClass * numbers.Armor["points_per_class"];
            if (penetration >= needed)
            {
                return Percent;
            }

            var floor = numbers.Armor["min_penetration_percent"];
            var through = floor + (Percent - floor) * (double)penetration / needed;
            return (int)Math.Round(through + (Percent - through) * numbers.Armor["blunt_percent"] / Percent,
                MidpointRounding.ToEven);
        }

        public static int PracticalShotsPerMinute(WeaponStats weapon, CombatNumbers numbers)
        {
            var floor = numbers.PracticalRate.TryGetValue(weapon.WeaponClass, out var rate) ? rate : 0;
            return Math.Max(weapon.FireRate, floor);
        }

        public static int WeaponPercent(WeaponStats weapon, CombatNumbers numbers)
        {
            if (weapon == null)
            {
                return Percent;
            }

            var row = numbers.Weapon;
            var rate = Clamp(Percent * (double)PracticalShotsPerMinute(weapon, numbers) / row["fire_rate_reference"],
                row["fire_rate_min_percent"], row["fire_rate_max_percent"]);
            var recoil = Clamp(Percent * (double)row["recoil_reference"] / Math.Max(1, weapon.Recoil),
                row["recoil_min_percent"], row["recoil_max_percent"]);
            var ergonomics = Clamp(Percent * (double)Math.Max(1, weapon.Ergonomics) / row["ergonomics_reference"],
                row["ergonomics_min_percent"], row["ergonomics_max_percent"]);
            return (int)Math.Round(rate * recoil * ergonomics / (Percent * Percent), MidpointRounding.ToEven);
        }

        public static int EngagementPercent(WeaponStats weapon, string engagementRange, CombatNumbers numbers)
        {
            if (weapon == null)
            {
                return Percent;
            }

            return numbers.Engagement[engagementRange][weapon.WeaponClass];
        }

        public static double Firepower(Fighter fighter, string engagementRange, int targetArmor, CombatNumbers numbers)
        {
            var landing = numbers.ShotsLandPercent / (double)Percent;
            double firepower;
            if (fighter.Ammo == null || fighter.Weapon == null)
            {
                var row = numbers.Unarmed;
                var through = PenetrationPercent(row["penetration"], targetArmor, numbers);
                firepower = row["damage"] * row["fire_rate"] / 60.0 * through / Percent * landing;
            }
            else
            {
                var shotsPerSecond = PracticalShotsPerMinute(fighter.Weapon, numbers) / 60.0;
                var through = PenetrationPercent(fighter.Ammo.Penetration, targetArmor, numbers);
                var handling = WeaponPercent(fighter.Weapon, numbers) * EngagementPercent(fighter.Weapon, engagementRange, numbers);
                firepower = fighter.Ammo.ShotDamage * shotsPerSecond * through / Percent
                    * handling / (Percent * (double)Percent) * landing;
            }

            if (fighter.Fractured)
            {
                firepower *= (Percent - numbers.FractureFirepowerPenaltyPercent) / (double)Percent;
            }

            return firepower;
        }

        public static double EnemyFirepower(Enemy enemy, int armorClass, CombatNumbers numbers)
        {
            var damage = enemy.Damage != 0 ? enemy.Damage : numbers.Unarmed["damage"];
            var penetration = enemy.Penetration != 0 ? enemy.Penetration : numbers.Unarmed["penetration"];
            var through = PenetrationPercent(penetration, armorClass, numbers);
            var firing = (enemy.FireRate != 0 ? enemy.FireRate : numbers.Unarmed["fire_rate"]) / 60.0;
            var landing = numbers.ShotsLandPercent * (double)numbers.EnemySkillPercent / (Percent * Percent);
            var firepower = damage * firing * through / Percent * landing;
            return enemy.Boss ? firepower * numbers.BossDamagePercent / Percent : firepower;
        }

        public static double PartyFirepower(IReadOnlyList<Fighter> party, Enemy enemy, string engagementRange, CombatNumbers numbers)
        {
            var standing = party.Where(fighter => fighter.Standing).ToList();
            var share = numbers.PartyExtraPercent / (double)Percent;
            var total = 0.0;
            for (var index = 0; index < standing.Count; index++)
            {
                total += Firepower(standing[index], engagementRange, enemy.ArmorClass, numbers) * (index == 0 ? 1 : share);
            }

            return total;
        }

        public static List<Enemy> RollEncounters(Zone zone, bool night, GearData gear, PythonRandom random, CombatNumbers numbers)
        {
            var span = numbers.Encounters[zone.ZoneId];
            var count = random.RandInt(span.Low, span.High);
            if (night)
            {
                count = (int)Math.Round(count * (double)numbers.NightPercent / Percent, MidpointRounding.ToEven);
            }

            var enemies = new List<Enemy>();
            for (var i = 0; i < count; i++)
            {
                enemies.Add(Enemy.Scav(numbers));
            }

            foreach (var boss in zone.NightBosses(night))
            {
                var chance = (int)Math.Round(boss.ChancePerMille * (double)numbers.BossChancePercent / Percent,
                    MidpointRounding.ToEven);
                if (random.RandRange(1000) >= chance || !gear.Mobs.ContainsKey(boss.MobId))
                {
                    continue;
                }

                enemies.Add(Enemy.FromMob(gear.Mobs[boss.MobId]));
                foreach (var escort in boss.Escorts)
                {
                    if (!gear.Mobs.ContainsKey(escort.MobId))
                    {
                        continue;
                    }

                    for (var i = 0; i < escort.Count; i++)
                    {
                        enemies.Add(Enemy.FromMob(gear.Mobs[escort.MobId]));
                    }
                }
            }

            return enemies;
        }

        public static (Dictionary<string, Fighter> Fighters, List<string> Log) ApplyZoneEnd(
            IReadOnlyDictionary<string, Fighter> fighters, CombatNumbers numbers)
        {
            var updated = new Dictionary<string, Fighter>();
            var log = new List<string>();
            foreach (var pair in fighters)
            {
                var fighter = pair.Value;
                if (!fighter.Standing)
                {
                    updated[pair.Key] = fighter;
                    continue;
                }

                var treated = Treat(fighter).Fighter;
                var bodyParts = new Dictionary<string, int>(treated.BodyParts);
                foreach (var conditions in treated.Conditions)
                {
                    string bleed = null;
                    if (conditions.Value.Contains("heavy_bleed"))
                    {
                        bleed = "heavy_bleed";
                    }
                    else if (conditions.Value.Contains("light_bleed"))
                    {
                        bleed = "light_bleed";
                    }

                    if (bleed == null)
                    {
                        continue;
                    }

                    var lost = Math.Min(bodyParts[conditions.Key], numbers.UntreatedBleedDamage[bleed]);
                    if (lost > 0)
                    {
                        bodyParts[conditions.Key] -= lost;
                        log.Add($"{fighter.Name} loses {lost} more to an untreated {bleed.Replace('_', ' ')} ({conditions.Key}).");
                    }
                }

                var result = WithParts(treated, bodyParts, treated.Conditions, treated.HealPool);
                if (!result.Standing)
                {
                    log.Add($"{fighter.Name} goes down from blood loss.");
                }

                updated[pair.Key] = result;
            }

            return (updated, log);
        }

        public static (List<Fighter> Party, EncounterResult Result) ResolveEncounter(
            IReadOnlyList<Fighter> party, Enemy enemy, string engagementRange, PythonRandom random, CombatNumbers numbers)
        {
            var standing = party.Where(fighter => fighter.Standing).ToList();
            if (standing.Count == 0)
            {
                return (party.ToList(), new EncounterResult(enemy.Name, false, 0, new Dictionary<string, int>(),
                    new string[0], new Dictionary<string, int>(), new Dictionary<string, string>(),
                    new Dictionary<string, string[]>(), new Dictionary<string, string[]>()));
            }

            var ours = PartyFirepower(party, enemy, engagementRange, numbers);
            var needed = ours > 0 ? enemy.Health / ours : (double)numbers.MaxSeconds;
            var won = needed <= numbers.MaxSeconds;
            var seconds = Math.Min(needed, numbers.MaxSeconds);
            var target = random.Choice(standing);
            var weights = numbers.HitLocation[engagementRange];
            var parts = CharacterSheet.BodyParts.ToList();
            var part = random.Choices(parts, parts.Select(name => (double)weights[name]).ToList());
            var armorClass = target.ArmorClasses.TryGetValue(part, out var worn) ? worn : 0;
            var incoming = EnemyFirepower(enemy, armorClass, numbers) * seconds;
            incoming = Math.Round(incoming * random.RandInt(numbers.SwingLow, numbers.SwingHigh) / Percent, MidpointRounding.ToEven);
            var partHealth = target.BodyParts[part];
            var hurt = Math.Max(0, partHealth - (int)incoming);
            var dealt = partHealth - hurt;
            var bodyParts = new Dictionary<string, int>(target.BodyParts) { [part] = hurt };
            var conditions = CopyConditions(target.Conditions);
            var partConditions = new HashSet<string>(conditions.TryGetValue(part, out var existing) ? existing : new string[0]);
            var gained = new List<string>();
            if (dealt > 0)
            {
                var injury = numbers.InjuryChances;
                // The heavy roll always happens. The light roll happens only when heavy did not land,
                // because Python chains them with elif. Fracture is its own roll, and only on a limb.
                if (random.RandRange(Percent) < injury["heavy_bleed_chance"][part] && !partConditions.Contains("heavy_bleed"))
                {
                    partConditions.Remove("light_bleed");
                    partConditions.Add("heavy_bleed");
                    gained.Add("heavy_bleed");
                }
                else if (random.RandRange(Percent) < injury["light_bleed_chance"][part]
                    && !partConditions.Contains("light_bleed") && !partConditions.Contains("heavy_bleed"))
                {
                    partConditions.Add("light_bleed");
                    gained.Add("light_bleed");
                }

                var limbs = new HashSet<string> { "left_arm", "right_arm", "left_leg", "right_leg" };
                if (limbs.Contains(part) && !partConditions.Contains("fracture")
                    && random.RandRange(Percent) < injury["fracture_chance"][part])
                {
                    partConditions.Add("fracture");
                    gained.Add("fracture");
                }
            }

            if (partConditions.Count > 0)
            {
                conditions[part] = partConditions.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            }
            else
            {
                conditions.Remove(part);
            }

            var wounded = WithParts(target, bodyParts, conditions, target.HealPool);
            var healed = new Dictionary<string, int>();
            if (wounded.Standing && wounded.HealPool > 0
                && wounded.Health * Percent < wounded.MaxHealth * numbers.MedsUseBelowPercent)
            {
                var deficit = wounded.MaxBodyParts[part] - wounded.BodyParts[part];
                var given = Math.Min(wounded.HealPool, deficit);
                if (given > 0)
                {
                    var healedParts = new Dictionary<string, int>(wounded.BodyParts) { [part] = wounded.BodyParts[part] + given };
                    wounded = WithParts(wounded, healedParts, wounded.Conditions, wounded.HealPool - given);
                    healed[wounded.Name] = given;
                }
            }

            var treated = Treat(wounded);
            wounded = treated.Fighter;
            var after = party.Select(fighter => fighter.CharacterId == target.CharacterId ? wounded : fighter).ToList();
            var newConditions = gained.Count > 0
                ? new Dictionary<string, string[]> { [target.Name] = gained.Select(condition => $"{part}:{condition}").ToArray() }
                : new Dictionary<string, string[]>();
            var treatedConditions = treated.Treated.Length > 0
                ? new Dictionary<string, string[]> { [target.Name] = treated.Treated }
                : new Dictionary<string, string[]>();
            var result = new EncounterResult(enemy.Name, won, Math.Max(1, (int)Math.Round(seconds, MidpointRounding.ToEven)),
                new Dictionary<string, int> { [target.Name] = dealt },
                wounded.Standing ? new string[0] : new[] { target.Name },
                healed, new Dictionary<string, string> { [target.Name] = part }, newConditions, treatedConditions);
            return (after, result);
        }

        private static (Fighter Fighter, string[] Treated) Treat(Fighter fighter)
        {
            var cures = new HashSet<string>();
            if (fighter.StopsLightBleed) { cures.Add("light_bleed"); }
            if (fighter.StopsHeavyBleed) { cures.Add("heavy_bleed"); }
            if (fighter.TreatsFracture) { cures.Add("fracture"); }
            if (cures.Count == 0 || fighter.Conditions.Count == 0)
            {
                return (fighter, new string[0]);
            }

            var conditions = new Dictionary<string, string[]>();
            var treated = new List<string>();
            foreach (var pair in fighter.Conditions)
            {
                var remaining = pair.Value.Where(condition => !cures.Contains(condition)).ToArray();
                treated.AddRange(pair.Value.Where(cures.Contains).Select(condition => $"{pair.Key}:{condition}"));
                if (remaining.Length > 0)
                {
                    conditions[pair.Key] = remaining;
                }
            }

            return (WithParts(fighter, fighter.BodyParts, conditions, fighter.HealPool), treated.ToArray());
        }

        private static Fighter WithParts(Fighter fighter, IReadOnlyDictionary<string, int> bodyParts,
            IReadOnlyDictionary<string, string[]> conditions, int healPool) =>
            new Fighter(fighter.CharacterId, fighter.Name, bodyParts, fighter.MaxBodyParts, conditions,
                fighter.Weapon, fighter.Ammo, fighter.ArmorClasses, healPool,
                fighter.StopsLightBleed, fighter.StopsHeavyBleed, fighter.TreatsFracture);

        private static Dictionary<string, string[]> CopyConditions(Dictionary<string, string[]> conditions)
        {
            var copy = new Dictionary<string, string[]>();
            foreach (var pair in conditions)
            {
                copy[pair.Key] = (string[])pair.Value.Clone();
            }

            return copy;
        }

        private static double Clamp(double value, int low, int high) => Math.Max(low, Math.Min(high, value));
    }
}
