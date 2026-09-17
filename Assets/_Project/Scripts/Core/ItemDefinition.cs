using System;
using System.Collections.Generic;
using System.Linq;

namespace Safehouse.Core
{
    /// <summary>
    /// An item type: what it is and how big it is. Immutable and shared - every stashed copy of a
    /// rifle points at the same definition, and only the copy carries position and rotation.
    /// </summary>
    public sealed class ItemDefinition
    {
        private ItemDefinition(string itemId, string name, string category, int width, int height,
            int baseValue, string rarity, double weight, IReadOnlyList<string> tags,
            string description, string iconPath)
        {
            ItemId = itemId;
            Name = name;
            Category = category;
            Width = width;
            Height = height;
            BaseValue = baseValue;
            Rarity = rarity;
            Weight = weight;
            Tags = tags;
            Description = description;
            IconPath = iconPath;
        }

        public string ItemId { get; }
        public string Name { get; }
        public string Category { get; }
        public int Width { get; }
        public int Height { get; }
        public int BaseValue { get; }
        public string Rarity { get; }
        public double Weight { get; }
        public IReadOnlyList<string> Tags { get; }

        /// <summary>Reserved metadata: the bundled data ships these empty, for copyright reasons.</summary>
        public string Description { get; }

        public string IconPath { get; }

        public static ItemDefinition Create(string itemId, string name, string category,
            int width, int height, int baseValue, string rarity, double weight,
            IEnumerable<string> tags = null, string description = "", string iconPath = "")
        {
            var tagList = tags == null ? Array.Empty<string>() : tags.ToArray();
            if (tagList.Length > CoreLimits.MaxTags)
            {
                throw new ValidationException(
                    $"Item tags must be a list with at most {CoreLimits.MaxTags} entries.");
            }

            foreach (var tag in tagList)
            {
                Validate.Text(tag, "Tag", CoreLimits.MaxTagLength);
            }

            return new ItemDefinition(
                Validate.Identifier(itemId, "item_id"),
                Validate.Text(name, "Item name", CoreLimits.MaxItemNameLength),
                Validate.Text(category, "Category", CoreLimits.MaxCategoryLength),
                Validate.Integer(width, "Item width", 1, CoreLimits.MaxGrid),
                Validate.Integer(height, "Item height", 1, CoreLimits.MaxGrid),
                Validate.Integer(baseValue, "Base value"),
                Validate.Text(rarity, "Rarity", CoreLimits.MaxCategoryLength),
                Validate.Weight(weight, "Item weight"),
                tagList,
                Validate.Text(description ?? "", "Description", CoreLimits.MaxDescriptionLength, allowEmpty: true),
                Validate.Text(iconPath ?? "", "Icon path", CoreLimits.MaxIconPathLength, allowEmpty: true));
        }

        public override string ToString() => $"{Name} ({Width}x{Height})";
    }
}
