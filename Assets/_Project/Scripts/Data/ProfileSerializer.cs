using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Safehouse.Core;

namespace Safehouse.Data
{
    /// <summary>
    /// A profile as JSON, and back. The C# side of the Python Profile.to_dict / from_dict: it owns the JSON
    /// shape, Safehouse.Core owns what is valid. Reads are as strict as the Python ones (a number must be an
    /// integer, not 3.0 or true) and unknown keys are ignored and dropped on the next write, so an older save
    /// with extra keys still opens.
    /// </summary>
    public static class ProfileSerializer
    {
        private const int MaxItems = CoreLimits.MaxGrid * CoreLimits.MaxGrid;

        /// <summary>
        /// Parses save text into a JSON tree. Dates are left as the plain strings they are (Newtonsoft would
        /// otherwise turn a name like "2020-01-01" into a date and write it back in a different format), and
        /// anything after the document is an error.
        /// </summary>
        public static JToken Parse(string text)
        {
            var settings = new JsonSerializerSettings
            {
                DateParseHandling = DateParseHandling.None,
                CheckAdditionalContent = true,
                MaxDepth = 64,
            };
            return JsonConvert.DeserializeObject<JToken>(text, settings)
                ?? throw new JsonSerializationException("The file holds no JSON document.");
        }

        /// <summary>The bytes to write: 2-space indent, LF, no BOM, one trailing newline (as the Python build writes).</summary>
        public static byte[] Serialize(Profile profile) =>
            new UTF8Encoding(false).GetBytes(ToJson(profile).ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");

        public static JObject ToJson(Profile profile)
        {
            return new JObject
            {
                ["schema_version"] = Profile.SchemaVersion,
                ["profile_id"] = profile.ProfileId,
                ["display_name"] = profile.DisplayName,
                ["money"] = profile.Money,
                ["stash_width"] = profile.Stash.StashWidth,
                ["stash_height"] = profile.Stash.StashHeight,
                ["stash"] = Items(profile.Stash),
                ["traders"] = new JArray(profile.Traders.Select(state => new JObject
                {
                    ["trader_id"] = state.TraderId,
                    ["spent"] = state.Spent,
                    ["standing"] = state.Standing,
                    ["purchases"] = new JArray(state.Purchases.Select(purchase => new JObject
                    {
                        ["offer_id"] = purchase.OfferId,
                        ["window"] = purchase.Window,
                        ["count"] = purchase.Count,
                    })),
                })),
                ["status"] = profile.Status,
                ["loadout"] = new JArray(profile.Loadout.Items.Select(worn => new JObject
                {
                    ["slot"] = worn.Slot,
                    ["instance_id"] = worn.InstanceId,
                    ["item_id"] = worn.ItemId,
                })),
                ["body_parts"] = new JObject(CharacterSheet.BodyParts.Select(part =>
                    new JProperty(part, profile.BodyParts[part]))),
                ["conditions"] = new JObject(CharacterSheet.BodyParts.Where(profile.Conditions.ContainsKey).Select(part =>
                    new JProperty(part, new JArray(profile.Conditions[part])))),
                ["bio"] = new JObject(CharacterSheet.BioFields.Select(field => new JProperty(field, profile.Bio[field]))),
                ["abilities"] = new JObject(CharacterSheet.Abilities.Select(name =>
                    new JProperty(name, profile.Abilities[name]))),
                ["carried"] = new JObject
                {
                    ["rig"] = CarriedGrid(profile.Rig),
                    ["backpack"] = CarriedGrid(profile.Backpack),
                },
                ["personal_room"] = new JObject
                {
                    ["level"] = profile.Room.Level,
                },
            };
        }

        private static JObject CarriedGrid(StashGrid grid) => new JObject
        {
            ["width"] = grid.StashWidth,
            ["height"] = grid.StashHeight,
            ["items"] = Items(grid),
        };

        private static JArray Items(StashGrid grid) => new JArray(grid.Stash.Select(item => new JObject
        {
            ["instance_id"] = item.InstanceId,
            ["item_id"] = item.ItemId,
            ["x"] = item.X,
            ["y"] = item.Y,
            ["rotation"] = item.Rotation,
        }));

        /// <summary>
        /// Reads a current-schema (8) profile. Older documents go through <see cref="ProfileMigrations.Upgrade"/>
        /// first; anything else - including a newer schema - is refused rather than guessed at.
        /// </summary>
        public static Profile FromJson(JToken document)
        {
            var root = Obj(document, "Profile");
            if (root["schema_version"] == null || root["schema_version"].Type != JTokenType.Integer
                || root["schema_version"].Value<long>() != Profile.SchemaVersion)
            {
                throw new ValidationException($"Unsupported save schema: {root["schema_version"]}.");
            }

            var stash = ReadGrid(root["stash_width"], root["stash_height"], root["stash"], "stash");
            var carried = Obj(root["carried"], "Carried items");
            var rigDocument = Obj(carried["rig"], "Rig");
            var backpackDocument = Obj(carried["backpack"], "Backpack");

            return new Profile(
                Str(root["profile_id"], "profile_id"),
                Str(root["display_name"], "display_name"),
                Int(root["money"], "money"),
                stash,
                ReadGrid(rigDocument["width"], rigDocument["height"], rigDocument["items"], "rig"),
                ReadGrid(backpackDocument["width"], backpackDocument["height"], backpackDocument["items"], "backpack"),
                Arr(root["traders"], "traders").Select(ReadTrader).ToList(),
                Str(root["status"], "status"),
                ReadLoadout(root["loadout"]),
                Obj(root["body_parts"], "body_parts").Properties().ToDictionary(p => p.Name, p => Int(p.Value, p.Name)),
                ReadConditions(root["conditions"]),
                Obj(root["bio"], "bio").Properties().ToDictionary(p => p.Name, p => Str(p.Value, p.Name)),
                Obj(root["abilities"], "abilities").Properties().ToDictionary(p => p.Name, p => Int(p.Value, p.Name)),
                ReadRoom(root["personal_room"]));
        }

        private static PersonalRoom ReadRoom(JToken token) =>
            token == null || token.Type == JTokenType.Null
                ? PersonalRoom.Default
                : new PersonalRoom(Int(Obj(token, "personal_room")["level"], "personal_room level"));

        private static StashGrid ReadGrid(JToken width, JToken height, JToken items, string label)
        {
            var list = Arr(items, label);
            if (list.Count > MaxItems)
            {
                throw new ValidationException($"The {label} holds too many items.");
            }

            return new StashGrid(Int(width, label + " width"), Int(height, label + " height"),
                list.Select(token => ReadItem(token, label)));
        }

        private static ItemInstance ReadItem(JToken token, string label)
        {
            var item = Obj(token, label + " item");
            return ItemInstance.Create(
                Str(item["instance_id"], "instance_id"), Str(item["item_id"], "item_id"),
                Int(item["x"], "x"), Int(item["y"], "y"), Int(item["rotation"], "rotation"));
        }

        private static Loadout ReadLoadout(JToken token)
        {
            var list = Arr(token, "loadout");
            if (list.Count > LoadoutSlots.All.Count)
            {
                throw new ValidationException("Loadout must be a list with at most one item per slot.");
            }

            return new Loadout(list.Select(entry =>
            {
                var worn = Obj(entry, "Each loadout entry");
                return EquippedItem.Create(Str(worn["slot"], "slot"), Str(worn["instance_id"], "instance_id"),
                    Str(worn["item_id"], "item_id"));
            }));
        }

        private static TraderState ReadTrader(JToken token)
        {
            var state = Obj(token, "Each trader state");
            return new TraderState(Str(state["trader_id"], "trader_id"), Long(state["spent"], "spent"),
                Int(state["standing"], "standing"),
                Arr(state["purchases"], "purchases").Select(entry =>
                {
                    var purchase = Obj(entry, "Each purchase");
                    return new OfferPurchase(Str(purchase["offer_id"], "offer_id"),
                        Long(purchase["window"], "window"), Int(purchase["count"], "count"));
                }));
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadConditions(JToken token) =>
            Obj(token, "conditions").Properties().ToDictionary(
                property => property.Name,
                property => (IReadOnlyList<string>)Arr(property.Value, property.Name + " conditions")
                    .Select(value => Str(value, "condition")).ToList());

        // ---- strict readers: the JSON type must be the right one, not merely convertible ----

        private static JObject Obj(JToken token, string label) =>
            token as JObject ?? throw new ValidationException($"{label} must be an object.");

        private static JArray Arr(JToken token, string label) =>
            token as JArray ?? throw new ValidationException($"{label} must be a list.");

        private static string Str(JToken token, string label) =>
            token != null && token.Type == JTokenType.String
                ? token.Value<string>()
                : throw new ValidationException($"{label} must be text.");

        private static long Long(JToken token, string label)
        {
            if (token == null || token.Type != JTokenType.Integer)
            {
                throw new ValidationException($"{label} must be an integer.");
            }

            try
            {
                return token.Value<long>();
            }
            catch (OverflowException)
            {
                throw new ValidationException($"{label} is out of range.");
            }
        }

        private static int Int(JToken token, string label)
        {
            var value = Long(token, label);
            if (value < int.MinValue || value > int.MaxValue)
            {
                throw new ValidationException($"{label} is out of range.");
            }

            return (int)value;
        }
    }
}
