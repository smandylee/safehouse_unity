using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Safehouse.Core;

namespace Safehouse.Data
{
    /// <summary>
    /// Turns items.json into item definitions. The C# side of the Python catalog.py: it owns the
    /// JSON shape, while the rules in Safehouse.Core only ever see validated objects.
    /// </summary>
    public static class CatalogLoader
    {
        public static Dictionary<string, ItemDefinition> Parse(JObject document)
        {
            if (!(document["items"] is JArray rows))
            {
                throw new GameDataException("Item database must contain an items list.");
            }

            if (rows.Count == 0)
            {
                throw new GameDataException("Item database is empty.");
            }

            var catalog = new Dictionary<string, ItemDefinition>(rows.Count);
            foreach (var row in rows)
            {
                var item = ReadItem(row);
                if (catalog.ContainsKey(item.ItemId))
                {
                    throw new GameDataException($"Duplicate item_id: {item.ItemId}.");
                }

                catalog[item.ItemId] = item;
            }

            return catalog;
        }

        public static Dictionary<string, ItemDefinition> Load() =>
            Parse(GameDataLoader.Load(GameDataFile.Items));

        private static ItemDefinition ReadItem(JToken row)
        {
            if (!(row is JObject item))
            {
                throw new GameDataException("Each item definition must be an object.");
            }

            try
            {
                return ItemDefinition.Create(
                    item.Value<string>("item_id"),
                    item.Value<string>("name"),
                    item.Value<string>("category"),
                    item.Value<int>("width"),
                    item.Value<int>("height"),
                    item.Value<int>("base_value"),
                    item.Value<string>("rarity"),
                    item.Value<double?>("weight") ?? -1,
                    item["tags"]?.ToObject<string[]>(),
                    item.Value<string>("description") ?? "",
                    item.Value<string>("icon_path") ?? "");
            }
            catch (ValidationException error)
            {
                // Name the offending row; "Item width must be..." alone is useless in a 4,800-row file.
                var id = item.Value<string>("item_id") ?? "(no item_id)";
                throw new GameDataException($"{id}: {error.Message}", error);
            }
        }
    }
}
