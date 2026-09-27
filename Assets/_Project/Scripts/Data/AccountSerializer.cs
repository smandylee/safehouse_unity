using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Safehouse.Core;

namespace Safehouse.Data
{
    /// <summary>
    /// An account as JSON, and back. The C# side of the Python account.json serialization. Reads are strict:
    /// a number must be an integer, and unknown keys are ignored and dropped on the next write.
    /// </summary>
    public static class AccountSerializer
    {
        /// <summary>Parses account text into a JSON tree using the same strict rules as profile saves.</summary>
        public static JToken Parse(string text) => ProfileSerializer.Parse(text);

        /// <summary>The bytes to write: 2-space indent, LF, no BOM, one trailing newline.</summary>
        public static byte[] Serialize(Account account) =>
            new UTF8Encoding(false).GetBytes(ToJson(account).ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");

        public static JObject ToJson(Account account)
        {
            return new JObject
            {
                ["schema_version"] = Account.SchemaVersion,
                ["character_order"] = new JArray(account.CharacterOrder),
                ["last_fuel_tick"] = account.LastFuelTick,
                ["hideout"] = new JObject
                {
                    ["fuel"] = account.Hideout.Fuel,
                    ["facilities"] = new JArray(account.Hideout.Facilities.Select(facility => new JObject
                    {
                        ["facility_id"] = facility.FacilityId,
                        ["level"] = facility.Level,
                    })),
                },
                ["production"] = new JObject
                {
                    ["jobs"] = new JArray(account.ProductionJobs.Select(job => new JObject
                    {
                        ["recipe_id"] = job.RecipeId,
                        ["start_time"] = job.StartTime,
                        ["character_id"] = job.CharacterId,
                        ["gpu_count"] = job.GpuCount,
                    })),
                },
            };
        }

        public static Account FromJson(JToken document)
        {
            var root = Obj(document, "Account");
            if (root["schema_version"] == null || root["schema_version"].Type != JTokenType.Integer
                || root["schema_version"].Value<long>() != Account.SchemaVersion)
            {
                throw new ValidationException($"Unsupported account schema: {root["schema_version"]}.");
            }

            var hideout = Obj(root["hideout"], "hideout");
            var facilities = Arr(hideout["facilities"], "facilities").Select(ReadFacility);
            var fuelToken = hideout["fuel"];
            var fuel = fuelToken != null && (fuelToken.Type == JTokenType.Float || fuelToken.Type == JTokenType.Integer)
                ? fuelToken.Value<double>()
                : Hideout.DefaultFuel;
            var characterOrder = Arr(root["character_order"], "character_order").Select(token => Str(token, "character_id"));

            var lastFuelTickToken = root["last_fuel_tick"];
            var lastFuelTick = lastFuelTickToken != null &&
                (lastFuelTickToken.Type == JTokenType.Float || lastFuelTickToken.Type == JTokenType.Integer)
                ? lastFuelTickToken.Value<double>()
                : 0.0;

            var production = root["production"];
            var jobs = production != null && production.Type == JTokenType.Object
                ? Arr(production["jobs"], "jobs").Select(ReadJob).ToList()
                : new List<ProductionJob>();

            return new Account(new Hideout(facilities, fuel), characterOrder, lastFuelTick, jobs);
        }

        private static Facility ReadFacility(JToken token)
        {
            var facility = Obj(token, "Each facility");
            return new Facility(Str(facility["facility_id"], "facility_id"),
                Int(facility["level"], "level"));
        }

        private static ProductionJob ReadJob(JToken token)
        {
            var job = Obj(token, "Each production job");
            var gpuToken = job["gpu_count"];
            return new ProductionJob(
                Str(job["recipe_id"], "recipe_id"),
                Double(job["start_time"], "start_time"),
                Str(job["character_id"], "character_id"),
                gpuToken != null && gpuToken.Type == JTokenType.Integer ? gpuToken.Value<int>() : 0);
        }

        private static JObject Obj(JToken token, string label) =>
            token as JObject ?? throw new ValidationException($"{label} must be an object.");

        private static JArray Arr(JToken token, string label) =>
            token as JArray ?? throw new ValidationException($"{label} must be a list.");

        private static string Str(JToken token, string label) =>
            token != null && token.Type == JTokenType.String
                ? token.Value<string>()
                : throw new ValidationException($"{label} must be text.");

        private static int Int(JToken token, string label)
        {
            if (token == null || token.Type != JTokenType.Integer)
            {
                throw new ValidationException($"{label} must be an integer.");
            }

            try
            {
                return token.Value<int>();
            }
            catch (System.OverflowException)
            
            {
                throw new ValidationException($"{label} is out of range.");
            }
        }

        private static double Double(JToken token, string label)
        {
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
            {
                throw new ValidationException($"{label} must be a number.");
            }

            return token.Value<double>();
        }
    }
}
