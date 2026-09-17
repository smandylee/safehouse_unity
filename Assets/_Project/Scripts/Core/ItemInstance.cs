namespace Safehouse.Core
{
    /// <summary>
    /// One owned copy of an item type, with where it sits in a grid. Immutable: moving an item
    /// produces a new instance with the same id, so a half-finished move can never be observed.
    /// </summary>
    public sealed class ItemInstance
    {
        private ItemInstance(string instanceId, string itemId, int x, int y, int rotation)
        {
            InstanceId = instanceId;
            ItemId = itemId;
            X = x;
            Y = y;
            Rotation = rotation;
        }

        public string InstanceId { get; }
        public string ItemId { get; }
        public int X { get; }
        public int Y { get; }

        /// <summary>Either 0 or 90 degrees; anything else is rejected.</summary>
        public int Rotation { get; }

        public static ItemInstance Create(string instanceId, string itemId, int x, int y, int rotation = 0)
        {
            if (rotation != 0 && rotation != 90)
            {
                throw new ValidationException("Rotation must be 0 or 90 degrees.");
            }

            return new ItemInstance(
                Validate.Identifier(instanceId, "instance_id", instance: true),
                Validate.Identifier(itemId, "item_id"),
                Validate.Integer(x, "Item x", 0, CoreLimits.MaxGrid - 1),
                Validate.Integer(y, "Item y", 0, CoreLimits.MaxGrid - 1),
                rotation);
        }

        public ItemInstance MovedTo(int x, int y, int rotation) =>
            Create(InstanceId, ItemId, x, y, rotation);

        public override string ToString() => $"{ItemId} @ {X},{Y} r{Rotation}";
    }
}
