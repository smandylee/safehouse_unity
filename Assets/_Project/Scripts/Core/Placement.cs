using System.Collections.Generic;

namespace Safehouse.Core
{
    /// <summary>Where an item ended up, and which way round it sits.</summary>
    public readonly struct Placement
    {
        public Placement(int x, int y, int rotation)
        {
            X = x;
            Y = y;
            Rotation = rotation;
        }

        public int X { get; }
        public int Y { get; }
        public int Rotation { get; }

        public override string ToString() => $"{X},{Y} r{Rotation}";
    }

    /// <summary>
    /// Pure placement rules. Nothing here touches disk, a screen, or a random generator, so the
    /// same grid and the same item always give the same answer.
    /// </summary>
    public static class PlacementRules
    {
        public static void Footprint(ItemDefinition item, int rotation, out int width, out int height)
        {
            if (rotation != 0 && rotation != 90)
            {
                throw new ValidationException("Rotation must be 0 or 90 degrees.");
            }

            width = rotation == 90 ? item.Height : item.Width;
            height = rotation == 90 ? item.Width : item.Height;
        }

        /// <summary>
        /// Why this item cannot go at x,y - or null when it can. Returns the reason rather than
        /// throwing, because callers show it to the player as-is.
        /// </summary>
        public static string PlacementError(IStashGrid grid,
            IReadOnlyDictionary<string, ItemDefinition> catalog, string itemId, int x, int y,
            int rotation = 0, string ignoreInstanceId = null)
        {
            if (!catalog.TryGetValue(itemId, out var item))
            {
                return $"Unknown item type: {itemId}. Restore its item database entry.";
            }

            int width, height;
            try
            {
                Footprint(item, rotation, out width, out height);
            }
            catch (ValidationException error)
            {
                return error.Message;
            }

            if (x < 0 || y < 0 || x + width > grid.StashWidth || y + height > grid.StashHeight)
            {
                return "Item would extend outside the stash.";
            }

            foreach (var other in grid.Stash)
            {
                // The item being moved must not collide with where it currently sits.
                if (other.InstanceId == ignoreInstanceId)
                {
                    continue;
                }

                if (!catalog.TryGetValue(other.ItemId, out var otherItem))
                {
                    return $"Unknown item already in stash: {other.ItemId}.";
                }

                Footprint(otherItem, other.Rotation, out var otherWidth, out var otherHeight);
                if (x < other.X + otherWidth && x + width > other.X
                    && y < other.Y + otherHeight && y + height > other.Y)
                {
                    return "That space is occupied.";
                }
            }

            return null;
        }

        public static bool CanPlace(IStashGrid grid, IReadOnlyDictionary<string, ItemDefinition> catalog,
            string itemId, int x, int y, int rotation = 0, string ignoreInstanceId = null) =>
            PlacementError(grid, catalog, itemId, x, y, rotation, ignoreInstanceId) == null;

        /// <summary>
        /// A copy of <paramref name="grid"/> with one item moved (and possibly turned). The item keeps
        /// its position in the list, so draw order - and therefore <see cref="ItemAt"/> - is stable.
        /// Throws <see cref="ValidationException"/> with the same reason <see cref="PlacementError"/>
        /// gives; the original grid is never modified, so a refused move leaves nothing half-done.
        /// </summary>
        public static StashGrid Move(StashGrid grid, IReadOnlyDictionary<string, ItemDefinition> catalog,
            string instanceId, int x, int y, int rotation)
        {
            var index = IndexOf(grid, instanceId);
            var current = grid.Stash[index];
            var error = PlacementError(grid, catalog, current.ItemId, x, y, rotation, instanceId);
            if (error != null)
            {
                throw new ValidationException(error);
            }

            var moved = new List<ItemInstance>(grid.Stash);
            moved[index] = current.MovedTo(x, y, rotation);
            return grid.With(moved);
        }

        /// <summary>
        /// Takes an item out of <paramref name="from"/> and puts it into <paramref name="to"/> at x,y -
        /// stash to backpack, rig to stash, and so on. Both grids come back as copies; if the item
        /// does not fit, this throws and neither grid has changed, so an item can never be lost or
        /// duplicated by a refused transfer. When both arguments are the same grid it is a plain move.
        /// </summary>
        public static (StashGrid From, StashGrid To) Transfer(StashGrid from, StashGrid to,
            IReadOnlyDictionary<string, ItemDefinition> catalog, string instanceId, int x, int y, int rotation)
        {
            if (ReferenceEquals(from, to))
            {
                var same = Move(from, catalog, instanceId, x, y, rotation);
                return (same, same);
            }

            var index = IndexOf(from, instanceId);
            var current = from.Stash[index];
            var error = PlacementError(to, catalog, current.ItemId, x, y, rotation);
            if (error != null)
            {
                throw new ValidationException(error);
            }

            var remaining = new List<ItemInstance>(from.Stash);
            remaining.RemoveAt(index);
            var added = new List<ItemInstance>(to.Stash) { current.MovedTo(x, y, rotation) };
            return (from.With(remaining), to.With(added));
        }

        private static int IndexOf(StashGrid grid, string instanceId)
        {
            for (var i = 0; i < grid.Stash.Count; i++)
            {
                if (grid.Stash[i].InstanceId == instanceId)
                {
                    return i;
                }
            }

            throw new ValidationException($"No item with instance ID {instanceId} in the stash.");
        }

        /// <summary>
        /// Mark occupied cells once, so a bulk check scales with the number of cells rather than
        /// with every pair of items.
        /// </summary>
        private static bool[,] Occupancy(IStashGrid grid,
            IReadOnlyDictionary<string, ItemDefinition> catalog)
        {
            var cells = new bool[grid.StashHeight, grid.StashWidth];
            foreach (var other in grid.Stash)
            {
                if (!catalog.TryGetValue(other.ItemId, out var item))
                {
                    throw new ValidationException($"Unknown item already in stash: {other.ItemId}.");
                }

                Footprint(item, other.Rotation, out var width, out var height);
                // Clamp rather than trust: this also runs on saves that have not been validated yet.
                var lastRow = System.Math.Min(other.Y + height, grid.StashHeight);
                var lastColumn = System.Math.Min(other.X + width, grid.StashWidth);
                for (var row = System.Math.Max(other.Y, 0); row < lastRow; row++)
                {
                    for (var column = System.Math.Max(other.X, 0); column < lastColumn; column++)
                    {
                        cells[row, column] = true;
                    }
                }
            }

            return cells;
        }

        /// <summary>
        /// The first free spot, scanning each row left to right. Tries the item's own orientation
        /// before the rotated one, so items keep the shape the player expects when space allows.
        /// </summary>
        public static Placement? FirstFit(IStashGrid grid,
            IReadOnlyDictionary<string, ItemDefinition> catalog, string itemId)
        {
            if (!catalog.TryGetValue(itemId, out var item))
            {
                throw new ValidationException($"Unknown item: {itemId}.");
            }

            var cells = Occupancy(grid, catalog);
            var rotations = item.Width == item.Height ? new[] { 0 } : new[] { 0, 90 };
            foreach (var rotation in rotations)
            {
                Footprint(item, rotation, out var width, out var height);
                for (var y = 0; y + height <= grid.StashHeight; y++)
                {
                    for (var x = 0; x + width <= grid.StashWidth; x++)
                    {
                        if (IsClear(cells, x, y, width, height))
                        {
                            return new Placement(x, y, rotation);
                        }
                    }
                }
            }

            return null;
        }

        private static bool IsClear(bool[,] cells, int x, int y, int width, int height)
        {
            for (var row = y; row < y + height; row++)
            {
                for (var column = x; column < x + width; column++)
                {
                    if (cells[row, column])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Which item covers this cell. Walks the stash backwards so the most recently added item
        /// wins, matching what the player sees drawn on top.
        /// </summary>
        public static ItemInstance ItemAt(IStashGrid grid,
            IReadOnlyDictionary<string, ItemDefinition> catalog, int x, int y)
        {
            for (var index = grid.Stash.Count - 1; index >= 0; index--)
            {
                var instance = grid.Stash[index];
                if (!catalog.TryGetValue(instance.ItemId, out var item))
                {
                    continue;
                }

                Footprint(item, instance.Rotation, out var width, out var height);
                if (instance.X <= x && x < instance.X + width
                    && instance.Y <= y && y < instance.Y + height)
                {
                    return instance;
                }
            }

            return null;
        }

        /// <summary>
        /// Placement rules for a whole grid at once. Ids in <paramref name="takenIds"/> - worn items,
        /// for example - already count as used, and this adds the grid's own ids to that set so a
        /// caller can check a character's stash and hideout against one another.
        /// </summary>
        public static void ValidateGrid(IStashGrid grid,
            IReadOnlyDictionary<string, ItemDefinition> catalog, ISet<string> takenIds = null)
        {
            var seen = takenIds ?? new HashSet<string>();
            var cells = new bool[grid.StashHeight, grid.StashWidth];
            foreach (var instance in grid.Stash)
            {
                if (!seen.Add(instance.InstanceId))
                {
                    throw new ValidationException($"Duplicate instance ID: {instance.InstanceId}.");
                }

                var label = $"{instance.ItemId} [{Shorten(instance.InstanceId)}]";
                if (!catalog.TryGetValue(instance.ItemId, out var item))
                {
                    throw new ValidationException(
                        $"{label}: Unknown item type: {instance.ItemId}. Restore its item database entry.");
                }

                Footprint(item, instance.Rotation, out var width, out var height);
                if (instance.X + width > grid.StashWidth || instance.Y + height > grid.StashHeight)
                {
                    throw new ValidationException($"{label}: Item would extend outside the stash.");
                }

                if (!IsClear(cells, instance.X, instance.Y, width, height))
                {
                    throw new ValidationException($"{label}: That space is occupied.");
                }

                for (var row = instance.Y; row < instance.Y + height; row++)
                {
                    for (var column = instance.X; column < instance.X + width; column++)
                    {
                        cells[row, column] = true;
                    }
                }
            }
        }

        /// <summary>Cells used, total weight and total value of everything in the grid.</summary>
        public static void StashTotals(IStashGrid grid,
            IReadOnlyDictionary<string, ItemDefinition> catalog,
            out int cells, out double weight, out long value)
        {
            cells = 0;
            weight = 0;
            value = 0;
            foreach (var instance in grid.Stash)
            {
                if (!catalog.TryGetValue(instance.ItemId, out var item))
                {
                    throw new ValidationException($"Unknown item in stash: {instance.ItemId}.");
                }

                cells += item.Width * item.Height;
                weight += item.Weight;
                value += item.BaseValue;
            }
        }

        private static string Shorten(string instanceId) =>
            instanceId.Length <= 8 ? instanceId : instanceId.Substring(0, 8);
    }
}
