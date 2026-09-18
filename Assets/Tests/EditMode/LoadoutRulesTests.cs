using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Safehouse.Core;

namespace Safehouse.Tests
{
    /// <summary>Equipping and taking off gear: the rules ported from Python session.equip / unequip.</summary>
    public sealed class LoadoutRulesTests
    {
        private static readonly Dictionary<string, ItemDefinition> Catalog = new Dictionary<string, ItemDefinition>
        {
            ["ak"] = ItemDefinition.Create("ak", "AK", "Weapons", 4, 1, 1000, "Rare", 3.0),
            ["m4"] = ItemDefinition.Create("m4", "M4", "Weapons", 4, 1, 1000, "Rare", 3.0),
            ["ak-round"] = ItemDefinition.Create("ak-round", "AK round", "Ammo", 1, 1, 10, "Common", 0.01),
            ["m4-round"] = ItemDefinition.Create("m4-round", "M4 round", "Ammo", 1, 1, 10, "Common", 0.01),
            ["helm-a"] = ItemDefinition.Create("helm-a", "Helmet A", "Gear", 2, 2, 500, "Rare", 1.0),
            ["helm-b"] = ItemDefinition.Create("helm-b", "Helmet B", "Gear", 2, 2, 500, "Rare", 1.0),
            ["junk"] = ItemDefinition.Create("junk", "Junk", "Loot", 1, 1, 5, "Common", 0.1),
        };

        private static readonly GearData Gear = new GearData(
            new[] { new WeaponStats("ak", "545", 36, 100), new WeaponStats("m4", "556", 48, 119) },
            new[] { new AmmoStats("ak-round", "545", 50), new AmmoStats("m4-round", "556", 45) },
            new[] { new EquipmentStats("helm-a", "helmet", 5, 0), new EquipmentStats("helm-b", "helmet", 3, 0) },
            null);

        private static int _next;
        private static string NewId() => (++_next).ToString("x32");

        private static ItemInstance At(string itemId, int x, int y) => ItemInstance.Create(NewId(), itemId, x, y);

        private static EquippedItem Worn(string slot, string itemId) => EquippedItem.Create(slot, NewId(), itemId);

        private static StashGrid Stash(int width, int height, params ItemInstance[] items) =>
            new StashGrid(width, height, items);

        [Test]
        public void SlotOfFollowsTheGearTables()
        {
            Assert.AreEqual("primary", Gear.SlotOf("ak"));
            Assert.AreEqual("ammo", Gear.SlotOf("ak-round"));
            Assert.AreEqual("helmet", Gear.SlotOf("helm-a"));
            Assert.IsNull(Gear.SlotOf("junk"));
        }

        [Test]
        public void EquippingMovesTheItemOutOfTheStashIntoItsSlot()
        {
            var helmet = At("helm-a", 0, 0);
            var stash = Stash(6, 6, helmet, At("junk", 3, 3));

            var result = LoadoutRules.Equip(Gear, Catalog, Loadout.Empty, stash, stash, helmet.InstanceId);

            Assert.AreEqual(helmet.InstanceId, result.Loadout.Get("helmet").InstanceId);
            Assert.AreEqual(1, result.Stash.Stash.Count);
            Assert.AreEqual(2, stash.Stash.Count, "the original stash must not change");
            Assert.IsNull(Loadout.Empty.Get("helmet"), "the original loadout must not change");
        }

        [Test]
        public void ReplacingWornGearReturnsTheOldPieceToTheStashKeepingItsInstanceId()
        {
            var old = Worn("helmet", "helm-a");
            var loadout = new Loadout(new[] { old });
            var next = At("helm-b", 0, 0);
            var stash = Stash(6, 6, next);

            var result = LoadoutRules.Equip(Gear, Catalog, loadout, stash, stash, next.InstanceId);

            Assert.AreEqual("helm-b", result.Loadout.Get("helmet").ItemId);
            var back = result.Stash.Stash.Single();
            Assert.AreEqual(old.InstanceId, back.InstanceId);
            Assert.AreEqual("helm-a", back.ItemId);
        }

        [Test]
        public void AnItemThatIsNotGearCannotBeEquipped()
        {
            var junk = At("junk", 0, 0);
            var stash = Stash(4, 4, junk);

            var error = Assert.Throws<ValidationException>(
                () => LoadoutRules.Equip(Gear, Catalog, Loadout.Empty, stash, stash, junk.InstanceId));

            StringAssert.Contains("cannot be equipped", error.Message);
        }

        [Test]
        public void AmmoMustMatchTheWeaponInHand()
        {
            var round = At("m4-round", 0, 0);
            var stash = Stash(4, 4, round);

            var noWeapon = Assert.Throws<ValidationException>(
                () => LoadoutRules.Equip(Gear, Catalog, Loadout.Empty, stash, stash, round.InstanceId));
            StringAssert.Contains("does not fit", noWeapon.Message);

            var withAk = new Loadout(new[] { Worn("primary", "ak") });
            Assert.Throws<ValidationException>(
                () => LoadoutRules.Equip(Gear, Catalog, withAk, stash, stash, round.InstanceId));

            var withM4 = new Loadout(new[] { Worn("primary", "m4") });
            Assert.DoesNotThrow(() => LoadoutRules.Equip(Gear, Catalog, withM4, stash, stash, round.InstanceId));
        }

        [Test]
        public void SwitchingToAWeaponOfAnotherCaliberUnloadsTheAmmo()
        {
            var loadout = new Loadout(new[] { Worn("primary", "ak"), Worn("ammo", "ak-round") });
            var m4 = At("m4", 0, 0);
            var stash = Stash(6, 6, m4);

            var result = LoadoutRules.Equip(Gear, Catalog, loadout, stash, stash, m4.InstanceId);

            Assert.IsNull(result.Loadout.Get("ammo"));
            Assert.AreEqual(new[] { "ak", "ak-round" }, result.Stash.Stash.Select(i => i.ItemId).OrderBy(i => i).ToArray());
        }

        [Test]
        public void ARefusedEquipChangesNothingWhenThereIsNoRoomForWhatComesOff()
        {
            var loadout = new Loadout(new[] { Worn("helmet", "helm-a") });
            var next = At("helm-b", 0, 0);

            // The new helmet comes out of a pack, and the stash it must swap into is already full.
            var full = Stash(2, 2, At("helm-a", 0, 0));
            var source = Stash(4, 4, next);
            var error = Assert.Throws<ValidationException>(
                () => LoadoutRules.Equip(Gear, Catalog, loadout, source, full, next.InstanceId));

            StringAssert.Contains("No space", error.Message);
            Assert.AreEqual(1, source.Stash.Count);
            Assert.AreEqual("helm-a", loadout.Get("helmet").ItemId);
        }

        [Test]
        public void ASwapFromTheStashItselfFitsBecauseTheNewPieceLeavesFirst()
        {
            var loadout = new Loadout(new[] { Worn("helmet", "helm-a") });
            var next = At("helm-b", 0, 0);
            var tight = Stash(2, 2, next); // exactly one helmet's worth of space

            Assert.DoesNotThrow(() => LoadoutRules.Equip(Gear, Catalog, loadout, tight, tight, next.InstanceId));
        }

        [Test]
        public void EquippingFromAnotherGridReturnsTheOldPieceToTheStashNotToThatGrid()
        {
            var old = Worn("helmet", "helm-a");
            var loadout = new Loadout(new[] { old });
            var next = At("helm-b", 0, 0);
            var pack = Stash(4, 4, next);
            var stash = Stash(6, 6);

            var result = LoadoutRules.Equip(Gear, Catalog, loadout, pack, stash, next.InstanceId);

            Assert.AreEqual(0, result.Source.Stash.Count);
            Assert.AreEqual(old.InstanceId, result.Stash.Stash.Single().InstanceId);
        }

        [Test]
        public void UnequippingPlacesTheItemWhereAskedWithItsOwnInstanceId()
        {
            var helmet = Worn("helmet", "helm-a");
            var loadout = new Loadout(new[] { helmet });
            var stash = Stash(6, 6);

            var (after, target) = LoadoutRules.Unequip(Catalog, loadout, "helmet", stash, 3, 2);

            Assert.IsNull(after.Get("helmet"));
            var placed = target.Stash.Single();
            Assert.AreEqual((helmet.InstanceId, 3, 2), (placed.InstanceId, placed.X, placed.Y));
        }

        [Test]
        public void UnequippingIntoOccupiedOrMissingSpaceIsRefused()
        {
            var loadout = new Loadout(new[] { Worn("helmet", "helm-a") });
            var stash = Stash(6, 6, At("junk", 3, 2));

            Assert.Throws<ValidationException>(() => LoadoutRules.Unequip(Catalog, loadout, "helmet", stash, 3, 2));
            Assert.Throws<ValidationException>(() => LoadoutRules.Unequip(Catalog, loadout, "rig", stash, 0, 0));
        }

        [Test]
        public void TakingOffTheWeaponAlsoUnloadsItsAmmo()
        {
            var loadout = new Loadout(new[] { Worn("primary", "ak"), Worn("ammo", "ak-round") });
            var stash = Stash(6, 6);

            var (after, target) = LoadoutRules.Unequip(Catalog, loadout, "primary", stash, 0, 0);

            Assert.AreEqual(0, after.Items.Count);
            Assert.AreEqual(new[] { "ak", "ak-round" }, target.Stash.Select(i => i.ItemId).OrderBy(i => i).ToArray());
        }

        [Test]
        public void ALoadoutCannotUseOneSlotTwice()
        {
            Assert.Throws<ValidationException>(
                () => new Loadout(new[] { Worn("helmet", "helm-a"), Worn("helmet", "helm-b") }));
        }

        [Test]
        public void AnItemInTwoGearTablesIsRejected()
        {
            Assert.Throws<ValidationException>(() => new GearData(
                new[] { new WeaponStats("ak", "545", 1, 1) }, new[] { new AmmoStats("ak", "545", 1) }, null, null));
        }
    }
}
