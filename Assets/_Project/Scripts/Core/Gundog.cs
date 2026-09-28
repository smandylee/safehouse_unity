using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// Gundog Revised character-sheet vocabulary. Stored on the character for the later board game.
    /// Expeditions and the automatic fight do not read it.
    /// </summary>
    public static class Gundog
    {
        public const int AbilityMin = 1;
        public const int AbilityMax = 10;
        public const int AbilityDefault = 5;
        public const int CareerLines = 5;
        public const int MaxLineLength = 40;
        public const int MaxReward = 999;
        public const int MaxMovement = 99;
        public const int MaxDurability = 999;

        public static readonly IReadOnlyList<GundogStat> Abilities = new[]
        {
            new GundogStat("muscle", "근력"),
            new GundogStat("finesse", "재주"),
            new GundogStat("agility", "민첩"),
            new GundogStat("sense", "감각"),
            new GundogStat("perception", "지각"),
            new GundogStat("charm", "매력"),
            new GundogStat("build", "체격"),
            new GundogStat("humanity", "인간"),
        };

        /// <summary>Skill families, in the same order as each class's modifier row.</summary>
        public static readonly IReadOnlyList<GundogStat> Skills = new[]
        {
            new GundogStat("shooting", "사격"),
            new GundogStat("melee", "격투"),
            new GundogStat("transport", "운송"),
            new GundogStat("perception", "지각"),
            new GundogStat("negotiation", "교섭"),
            new GundogStat("education", "교양"),
            new GundogStat("technique", "기술"),
        };

        public static readonly IReadOnlyList<GundogClass> Classes = new[]
        {
            Class("assault", "어설트", "ASSAULT",
                new[] { 30, 20, 15, 10, 10, 10, 10 }, new[] { 20, 15, 15, 10, 10, 10, 10 },
                Art("assault-1", "건 액션"), Art("assault-2", "퀵 드로우"),
                Art("assault-3", "컴뱃 센스"), Art("assault-4", "컴뱃 무브")),
            Class("operator", "오퍼레이터", "OPERATOR",
                new[] { 10, 10, 10, 10, 15, 30, 20 }, new[] { 10, 10, 10, 10, 10, 20, 20 },
                Art("operator-1", "어드바이스"), Art("operator-2", "워킹 메뉴얼"),
                Art("operator-3", "일렉트로닉스"), Art("operator-4", "컴퓨터 브레인")),
            Class("commander", "커맨더", "COMMANDER",
                new[] { 25, 10, 10, 10, 20, 20, 10 }, new[] { 20, 10, 10, 10, 15, 15, 10 },
                Art("commander-1", "아웃 제너럴"), Art("commander-2", "어택 포메이션"),
                Art("commander-3", "커버 포메이션"), Art("commander-4", "킬링 익스프레션")),
            Class("scout", "스카우트", "SCOUT",
                new[] { 10, 10, 25, 20, 10, 15, 15 }, new[] { 10, 10, 20, 20, 10, 10, 10 },
                Art("scout-1", "어큐트 센스"), Art("scout-2", "아크로바틱 피트"),
                Art("scout-3", "서바이벌 스킬"), Art("scout-4", "식스 센스")),
            Class("sniper", "스나이퍼", "SNIPER",
                new[] { 30, 10, 20, 15, 10, 10, 10 }, new[] { 20, 10, 15, 15, 10, 10, 10 },
                Art("sniper-1", "샤프 슈터"), Art("sniper-2", "시리어스 운즈"),
                Art("sniper-3", "스톤 콜드"), Art("sniper-4", "트랜퀼리티")),
            Class("medic", "메딕", "MEDIC",
                new[] { 10, 10, 10, 10, 20, 15, 30 }, new[] { 10, 10, 10, 10, 15, 15, 20 },
                Art("medic-1", "아스클레피오스"), Art("medic-2", "애널라이즈"),
                Art("medic-3", "카운터 플롯"), Art("medic-4", "섬즈 업")),
            Class("mechanic", "메카닉", "MECHANIC",
                new[] { 10, 10, 10, 20, 10, 15, 30 }, new[] { 10, 10, 10, 15, 10, 15, 20 },
                Art("mechanic-1", "앤티도트"), Art("mechanic-2", "커스터마이즈"),
                Art("mechanic-3", "이베이시브 액션"), Art("mechanic-4", "스턴트")),
            Class("grappler", "그래플러", "GRAPPLER",
                new[] { 10, 30, 20, 15, 10, 10, 10 }, new[] { 10, 20, 15, 15, 10, 10, 10 },
                Art("grappler-1", "알카난"), Art("grappler-2", "거합술"),
                Art("grappler-3", "스러스트 앤 패리"), Art("grappler-4", "녹 다운")),
        };

        public static GundogClass FindClass(string classId) =>
            Classes.FirstOrDefault(entry => entry.ClassId == classId);

        public static GundogArt FindArt(string artId)
        {
            foreach (var entry in Classes)
            {
                var art = entry.Arts.FirstOrDefault(candidate => candidate.ArtId == artId);
                if (art != null)
                {
                    return art;
                }
            }

            return null;
        }

        private static GundogClass Class(string id, string name, string english, int[] main, int[] sub,
            params GundogArt[] arts) =>
            new GundogClass(id, name, english, main, sub, arts);

        private static GundogArt Art(string id, string name) => new GundogArt(id, name);
    }

    public sealed class GundogStat
    {
        public GundogStat(string statId, string name)
        {
            StatId = statId;
            Name = name;
        }

        public string StatId { get; }
        public string Name { get; }
    }

    public sealed class GundogArt
    {
        public GundogArt(string artId, string name)
        {
            ArtId = artId;
            Name = name;
        }

        public string ArtId { get; }
        public string Name { get; }
    }

    public sealed class GundogClass
    {
        public GundogClass(string classId, string name, string english, IReadOnlyList<int> mainModifiers,
            IReadOnlyList<int> subModifiers, IReadOnlyList<GundogArt> arts)
        {
            if (mainModifiers == null || mainModifiers.Count != Gundog.Skills.Count
                || subModifiers == null || subModifiers.Count != Gundog.Skills.Count)
            {
                throw new ValidationException("A class needs one modifier per skill family.");
            }

            ClassId = classId;
            Name = name;
            English = english;
            MainModifiers = mainModifiers.ToList();
            SubModifiers = subModifiers.ToList();
            Arts = arts.ToList();
        }

        public string ClassId { get; }
        public string Name { get; }
        public string English { get; }
        public IReadOnlyList<int> MainModifiers { get; }
        public IReadOnlyList<int> SubModifiers { get; }
        public IReadOnlyList<GundogArt> Arts { get; }
    }

    /// <summary>
    /// The Gundog sheet stored on a character. Class skill numbers are the main-class modifier plus the
    /// sub-class modifier. Older saves have a blank sheet: no class, every ability at 5.
    /// </summary>
    public sealed class GundogSheet
    {
        public GundogSheet(IReadOnlyDictionary<string, int> abilities, string mainClass, string subClass,
            IEnumerable<string> arts, IReadOnlyList<string> career, int rewardPoints, int movement, int durability,
            string rank, string language, string occupation, string era)
        {
            Abilities = CheckAbilities(abilities);
            MainClass = mainClass ?? "";
            SubClass = subClass ?? "";
            var chosen = (arts ?? Enumerable.Empty<string>()).ToList();
            if (chosen.Distinct().Count() != chosen.Count)
            {
                throw new ValidationException("A class art is selected twice.");
            }

            var hasClass = MainClass.Length > 0 || SubClass.Length > 0;
            if (!hasClass)
            {
                if (MainClass.Length != 0 || SubClass.Length != 0 || chosen.Count > 0)
                {
                    throw new ValidationException("Choose both a main class and a sub class.");
                }
            }
            else
            {
                var main = Gundog.FindClass(MainClass);
                var sub = Gundog.FindClass(SubClass);
                if (main == null || sub == null)
                {
                    throw new ValidationException("Choose both a main class and a sub class.");
                }

                foreach (var artId in chosen)
                {
                    if (main.Arts.All(art => art.ArtId != artId) && sub.Arts.All(art => art.ArtId != artId))
                    {
                        throw new ValidationException("That class art does not belong to the chosen classes.");
                    }
                }
            }

            Arts = chosen;
            if (career == null || career.Count != Gundog.CareerLines)
            {
                throw new ValidationException($"Career needs exactly {Gundog.CareerLines} lines.");
            }

            Career = career.Select(line => Validate.Text(line ?? "", "Career", Gundog.MaxLineLength, allowEmpty: true)).ToList();
            RewardPoints = Validate.Integer(rewardPoints, "Reward points", 0, Gundog.MaxReward);
            Movement = Validate.Integer(movement, "Movement", 0, Gundog.MaxMovement);
            Durability = Validate.Integer(durability, "Durability", 0, Gundog.MaxDurability);
            Rank = Validate.Text(rank ?? "", "Rank", Gundog.MaxLineLength, allowEmpty: true);
            Language = Validate.Text(language ?? "", "Language", Gundog.MaxLineLength, allowEmpty: true);
            Occupation = Validate.Text(occupation ?? "", "Occupation", Gundog.MaxLineLength, allowEmpty: true);
            Era = Validate.Text(era ?? "", "Era", Gundog.MaxLineLength, allowEmpty: true);
        }

        public IReadOnlyDictionary<string, int> Abilities { get; }
        public string MainClass { get; }
        public string SubClass { get; }
        public IReadOnlyList<string> Arts { get; }
        public IReadOnlyList<string> Career { get; }
        public int RewardPoints { get; }
        public int Movement { get; }
        public int Durability { get; }
        public string Rank { get; }
        public string Language { get; }
        public string Occupation { get; }
        public string Era { get; }

        public static GundogSheet Blank { get; } = new GundogSheet(
            Gundog.Abilities.ToDictionary(stat => stat.StatId, stat => Gundog.AbilityDefault),
            "", "", null, Enumerable.Repeat("", Gundog.CareerLines).ToList(), 0, 0, 0, "", "", "", "");

        /// <summary>Main-class modifier plus sub-class modifier, per skill family. Zero when the sheet has no class.</summary>
        public IReadOnlyDictionary<string, int> SkillModifiers()
        {
            var result = Gundog.Skills.ToDictionary(skill => skill.StatId, skill => 0);
            if (MainClass.Length == 0)
            {
                return result;
            }

            var main = Gundog.FindClass(MainClass);
            var sub = Gundog.FindClass(SubClass);
            for (var index = 0; index < Gundog.Skills.Count; index++)
            {
                result[Gundog.Skills[index].StatId] = main.MainModifiers[index] + sub.SubModifiers[index];
            }

            return result;
        }

        private static IReadOnlyDictionary<string, int> CheckAbilities(IReadOnlyDictionary<string, int> abilities)
        {
            var expected = Gundog.Abilities.Select(stat => stat.StatId);
            if (abilities == null || !new HashSet<string>(abilities.Keys).SetEquals(expected))
            {
                throw new ValidationException("Gundog abilities must be: " + string.Join(", ", expected) + ".");
            }

            return Gundog.Abilities.ToDictionary(stat => stat.StatId,
                stat => Validate.Integer(abilities[stat.StatId], stat.Name, Gundog.AbilityMin, Gundog.AbilityMax));
        }
    }
}
