using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Safehouse.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Safehouse.Tests
{
    /// <summary>
    /// Builds the real GearScreen.uxml + GearScreenController in a bare GameObject (no scene file
    /// needed) and drives it the way a player would, minus the pointer: SelectItem() stands in for
    /// a click on a stash cell, since simulating a real pointer event is far more machinery for the
    /// same coverage. `Safehouse > Build Gear Scene` must have run at least once so the PanelSettings
    /// asset these tests load exists.
    /// </summary>
    public sealed class GearScreenControllerTests
    {
        private GameObject _host;

        private string _iconRoot;

        [SetUp]
        public void SetUp()
        {
            // Most tests are about layout and rules, not art: point icons at a folder that does not exist
            // so they behave the same whether or not the developer has downloaded the real icons.
            _iconRoot = Path.Combine(Path.GetTempPath(), "safehouse-test-icons-" + System.Guid.NewGuid().ToString("N"));
            IconLibrary.DefaultFolder = _iconRoot;
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }

            IconLibrary.DefaultFolder = null;
            if (Directory.Exists(_iconRoot))
            {
                Directory.Delete(_iconRoot, recursive: true);
            }
        }

        /// <summary>Puts synthetic text-free art for this item where the slot cards will look for it.</summary>
        private void GiveArt(string itemId, int width, int height)
        {
            var folder = Path.Combine(_iconRoot, "art");
            Directory.CreateDirectory(folder);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            File.WriteAllBytes(Path.Combine(folder, itemId + ".png"), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        /// <summary>Puts a synthetic icon for this item where the screen will look for it.</summary>
        private void GiveIcon(string itemId, int width = 4, int height = 4)
        {
            var folder = Path.Combine(_iconRoot, IconLibrary.Size.ToString());
            Directory.CreateDirectory(folder);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            File.WriteAllBytes(Path.Combine(folder, itemId + ".png"), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        [UnityTest]
        public IEnumerator TheStashGridShowsOnePlacedCellPerItem()
        {
            var controller = CreateGearScreen();
            yield return null; // UIDocument builds its visual tree on the next update, not this frame.

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var grid = root.Q<VisualElement>("stash-grid");

            Assert.Greater(grid.childCount, 0, "expected at least one stash cell");
            foreach (var cell in grid.Children())
            {
                StringAssert.StartsWith("cell-", cell.name);
            }
        }

        [UnityTest]
        public IEnumerator SelectingAStashCellFillsInTheDetailPanel()
        {
            var controller = CreateGearScreen();
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var grid = root.Q<VisualElement>("stash-grid");
            var firstCell = grid[0];
            var instanceId = firstCell.name.Substring("cell-".Length);

            controller.SelectItem(instanceId);

            Assert.IsTrue(firstCell.ClassListContains("cell-selected"));
            Assert.IsNotEmpty(root.Q<Label>("label-selected-name").text);
            Assert.IsNotEmpty(root.Q<Label>("label-selected-value").text);
            StringAssert.DoesNotContain("—", root.Q<Label>("label-selected-value").text); // no leftover "—"
        }

        [UnityTest]
        public IEnumerator SelectingADifferentCellClearsTheSelectionOnTheFirst()
        {
            var controller = CreateGearScreen();
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var grid = root.Q<VisualElement>("stash-grid");
            Assume.That(grid.childCount, Is.GreaterThan(1));

            var first = grid[0];
            var second = grid[1];
            controller.SelectItem(first.name.Substring("cell-".Length));
            controller.SelectItem(second.name.Substring("cell-".Length));

            Assert.IsFalse(first.ClassListContains("cell-selected"));
            Assert.IsTrue(second.ClassListContains("cell-selected"));
        }

        [UnityTest]
        public IEnumerator MovingAnItemToAFreeSpotRepositionsItsCell()
        {
            var controller = CreateGearScreen();
            yield return null;

            var grid = controller.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>("stash-grid");
            var cell = grid[0];
            var instanceId = cell.name.Substring("cell-".Length);
            var width = cell.style.width.value.value;
            var height = cell.style.height.value.value;

            // The sample stash is 10 x 12; find the first spot the item can be moved to.
            var moved = false;
            for (var y = 0; y < 12 && !moved; y++)
            {
                for (var x = 0; x < 10 && !moved; x++)
                {
                    if ((x != 0 || y != 0) && controller.TryMoveItem(instanceId, x, y, 0) == null)
                    {
                        moved = true;
                        Assert.AreEqual(x * 48 + 2, cell.style.left.value.value, 0.01f);
                        Assert.AreEqual(y * 48 + 2, cell.style.top.value.value, 0.01f);
                    }
                }
            }

            Assert.IsTrue(moved, "expected at least one free spot in the sample stash");
            Assert.AreEqual(width, cell.style.width.value.value, 0.01f);
            Assert.AreEqual(height, cell.style.height.value.value, 0.01f);
        }

        [UnityTest]
        public IEnumerator MovingOntoAnotherItemIsRefusedAndLeavesTheCellWhereItWas()
        {
            var controller = CreateGearScreen();
            yield return null;

            var grid = controller.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>("stash-grid");
            Assume.That(grid.childCount, Is.GreaterThan(1));
            var moving = grid[1];
            var beforeLeft = moving.style.left.value.value;
            var beforeTop = moving.style.top.value.value;

            // grid[0] was placed first by FirstFit, so it sits at 0,0.
            var error = controller.TryMoveItem(moving.name.Substring("cell-".Length), 0, 0, 0);

            Assert.IsNotNull(error);
            Assert.AreEqual(beforeLeft, moving.style.left.value.value, 0.01f);
            Assert.AreEqual(beforeTop, moving.style.top.value.value, 0.01f);
        }

        [UnityTest]
        public IEnumerator TurningAnItemSwapsItsCellWidthAndHeight()
        {
            var controller = CreateGearScreen();
            yield return null;

            var grid = controller.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>("stash-grid");
            var cell = grid[0]; // the AK-12, which is not square
            var instanceId = cell.name.Substring("cell-".Length);
            var width = cell.style.width.value.value;
            var height = cell.style.height.value.value;
            Assume.That(width, Is.Not.EqualTo(height));

            // Turn it into whichever spot it fits in turned; the pixel size must swap either way.
            var turned = false;
            for (var y = 0; y < 12 && !turned; y++)
            {
                for (var x = 0; x < 10 && !turned; x++)
                {
                    turned = controller.TryMoveItem(instanceId, x, y, 90) == null;
                }
            }

            Assert.IsTrue(turned, "expected the turned AK-12 to fit somewhere");
            // Turning swaps the footprint, so the cell's pixel width and height swap too.
            Assert.AreEqual(height, cell.style.width.value.value, 0.01f);
            Assert.AreEqual(width, cell.style.height.value.value, 0.01f);
        }

        [UnityTest]
        public IEnumerator DraggingACellWithRealPointerEventsShowsACopyUnderThePointer()
        {
            var controller = CreateGearScreen();
            yield return null;
            yield return null; // let layout run so worldBound is real

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var cell = root.Q<VisualElement>("stash-grid")[0];
            var startLeft = cell.style.left.value.value;
            var start = cell.worldBound.center;

            Send(cell, PointerDownEvent.GetPooled(MakeTouch(TouchPhase.Began, start)));
            Send(cell, PointerMoveEvent.GetPooled(MakeTouch(TouchPhase.Moved, start + new Vector2(60, 0))));

            Assert.IsTrue(cell.ClassListContains("cell-dragging"),
                "pointer-move events never reached the drag handler (a Button's Clickable may be swallowing them)");
            Assert.IsNotNull(root.Q(className: "cell-proxy"), "a copy of the cell should follow the pointer");
            Assert.AreEqual(startLeft, cell.style.left.value.value, "the original stays put until the drop");

            Send(cell, PointerUpEvent.GetPooled(MakeTouch(TouchPhase.Ended, start + new Vector2(60, 0))));
            Assert.IsFalse(cell.ClassListContains("cell-dragging"), "dropping should end the drag");
            Assert.IsNull(root.Q(className: "cell-proxy"), "the copy should be gone after the drop");
        }

        [UnityTest]
        public IEnumerator DraggingAStashItemOntoTheRigMovesItThere()
        {
            var controller = CreateGearScreen();
            yield return null;
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var rig = root.Q<VisualElement>("rig-grid");
            var cell = SmallStashCell(root);
            var instanceId = cell.name.Substring("cell-".Length);
            var press = cell.worldBound.center;
            var grab = press - cell.worldBound.min;

            // Aim so the item's top-left corner lands in the rig's bottom-right cell (5,3), which is
            // empty in the sample loadout; the few extra pixels stay well inside that cell.
            var topLeft = rig.worldBound.min + new Vector2(5 * 48 + 2 + cell.resolvedStyle.marginLeft + 5,
                3 * 48 + 2 + cell.resolvedStyle.marginTop + 5);
            var over = topLeft + grab;

            Send(cell, PointerDownEvent.GetPooled(MakeTouch(TouchPhase.Began, press)));
            Send(cell, PointerMoveEvent.GetPooled(MakeTouch(TouchPhase.Moved, over)));

            var ghost = root.Q(className: "cell-ghost");
            Assert.IsNotNull(ghost, "hovering a grid should show where the item would land");
            Assert.AreSame(rig, ghost.parent);
            Assert.IsFalse(ghost.ClassListContains("cell-ghost-bad"), "(5,3) in the rig is free");

            Send(cell, PointerUpEvent.GetPooled(MakeTouch(TouchPhase.Ended, over)));

            Assert.AreEqual("rig", controller.GridOf(instanceId));
            Assert.AreSame(rig, cell.parent);
            Assert.AreEqual(5 * 48 + 2, cell.style.left.value.value, 0.01f);
            Assert.AreEqual(3 * 48 + 2, cell.style.top.value.value, 0.01f);
        }

        [UnityTest]
        public IEnumerator TransferringToAnotherGridUpdatesTheCarriedCounter()
        {
            var controller = CreateGearScreen();
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var cell = SmallStashCell(root);
            var instanceId = cell.name.Substring("cell-".Length);
            var before = root.Q<Label>("label-carried").text;

            Assert.IsNull(controller.TryTransferItem(instanceId, "backpack", 5, 7, 0));

            Assert.AreEqual("backpack", controller.GridOf(instanceId));
            Assert.AreNotEqual(before, root.Q<Label>("label-carried").text);
            Assert.AreSame(root.Q<VisualElement>("backpack-grid"), cell.parent);
        }

        [UnityTest]
        public IEnumerator ARefusedTransferLeavesTheItemInItsOwnGrid()
        {
            var controller = CreateGearScreen();
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var cell = SmallStashCell(root);
            var instanceId = cell.name.Substring("cell-".Length);

            // (0,0) in the rig holds a magazine.
            Assert.IsNotNull(controller.TryTransferItem(instanceId, "rig", 0, 0, 0));
            Assert.IsNotNull(controller.TryTransferItem(instanceId, "rig", 6, 0, 0), "off the edge");
            Assert.IsNotNull(controller.TryTransferItem(instanceId, "nowhere", 0, 0, 0));

            Assert.AreEqual("stash", controller.GridOf(instanceId));
            Assert.AreSame(root.Q<VisualElement>("stash-grid"), cell.parent);
        }

        private const string Rifle = "kalashnikov-ak-12-545x39-assault-rifle";
        private const string OtherRifle = "colt-m4a1-556x45-assault-rifle";
        private const string WornHelmet = "rys-t-bulletproof-helmet-black";
        private const string SpareHelmet = "altyn-bulletproof-helmet-olive-drab";
        private const string FittingAmmo = "545x39mm-bp-gs";

        [UnityTest]
        public IEnumerator TheLoadoutSlotsShowWhatTheCharacterWears()
        {
            var controller = CreateGearScreen();
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;

            Assert.AreEqual(Rifle, controller.EquippedItemIn("primary"));
            Assert.AreEqual(WornHelmet, controller.EquippedItemIn("helmet"));
            Assert.AreNotEqual("Empty", root.Q<Label>("slot-helmet-name").text);
            Assert.AreEqual("545x39", root.Q<Label>("slot-primary-stat").text);
            Assert.AreEqual("24", root.Q<Label>("slot-rig-stat").text, "the LV-119 carries 24 cells");
            Assert.AreEqual("48", root.Q<Label>("slot-backpack-stat").text);
            Assert.AreEqual("3 USES", root.Q<Label>("slot-meds-stat").text);
        }

        [UnityTest]
        public IEnumerator EquippingFromTheStashSwapsTheOldPieceBackIn()
        {
            var controller = CreateGearScreen();
            yield return null;

            var spare = controller.FirstInstanceOf(SpareHelmet);
            Assume.That(spare, Is.Not.Null, "the sample stash should hold a spare helmet");
            var oldInstance = controller.EquippedInstanceIn("helmet");

            Assert.IsNull(controller.TryEquipItem(spare, "helmet"));

            Assert.AreEqual(SpareHelmet, controller.EquippedItemIn("helmet"));
            Assert.AreEqual("stash", controller.GridOf(oldInstance), "the old helmet goes back to the stash");
            Assert.IsNull(controller.GridOf(spare), "the new one is worn, not in a grid");
            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            StringAssert.Contains("Altyn", root.Q<Label>("slot-helmet-name").text);
        }

        [UnityTest]
        public IEnumerator AnItemOnlyGoesInItsOwnSlot()
        {
            var controller = CreateGearScreen();
            yield return null;

            var spare = controller.FirstInstanceOf(SpareHelmet);
            Assume.That(spare, Is.Not.Null);

            StringAssert.Contains("helmet", controller.TryEquipItem(spare, "armor"));
            Assert.AreEqual("stash", controller.GridOf(spare));
            Assert.AreEqual(WornHelmet, controller.EquippedItemIn("helmet"));
        }

        [UnityTest]
        public IEnumerator EquippingARifleOfAnotherCaliberUnloadsTheWornAmmo()
        {
            var controller = CreateGearScreen();
            yield return null;

            var other = controller.FirstInstanceOf(OtherRifle);
            Assume.That(other, Is.Not.Null, "the sample stash should hold a rifle of another caliber");
            var ammo = controller.EquippedInstanceIn("ammo");
            Assume.That(controller.EquippedItemIn("ammo"), Is.EqualTo(FittingAmmo));

            Assert.IsNull(controller.TryEquipItem(other, "primary"));

            Assert.IsNull(controller.EquippedItemIn("ammo"));
            Assert.AreEqual("stash", controller.GridOf(ammo));
        }

        [UnityTest]
        public IEnumerator AmmoOfTheWrongCaliberCannotBeLoaded()
        {
            var controller = CreateGearScreen();
            yield return null;

            var other = controller.FirstInstanceOf(OtherRifle);
            Assume.That(other, Is.Not.Null);
            Assert.IsNull(controller.TryEquipItem(other, "primary")); // now an M4 with no ammo

            var fitting = controller.FirstInstanceOf(FittingAmmo);
            Assume.That(fitting, Is.Not.Null);

            StringAssert.Contains("does not fit", controller.TryEquipItem(fitting, "ammo"));
        }

        [UnityTest]
        public IEnumerator TakingGearOffPutsItInTheChosenGrid()
        {
            var controller = CreateGearScreen();
            yield return null;

            var helmet = controller.EquippedInstanceIn("helmet");

            Assert.IsNull(controller.TryUnequipItem(helmet, "backpack", 4, 6, 0));

            Assert.IsNull(controller.EquippedItemIn("helmet"));
            Assert.AreEqual("backpack", controller.GridOf(helmet));
        }

        [UnityTest]
        public IEnumerator DraggingAStashItemOntoItsSlotWearsIt()
        {
            var controller = CreateGearScreen();
            yield return null;
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var spare = controller.FirstInstanceOf(SpareHelmet);
            Assume.That(spare, Is.Not.Null);
            var cell = root.Q<VisualElement>("cell-" + spare);
            var helmetCard = root.Q<VisualElement>("slot-helmet");
            var press = cell.worldBound.center;
            var over = helmetCard.worldBound.center;

            Send(cell, PointerDownEvent.GetPooled(MakeTouch(TouchPhase.Began, press)));
            Send(cell, PointerMoveEvent.GetPooled(MakeTouch(TouchPhase.Moved, over)));
            Assert.IsTrue(helmetCard.ClassListContains("slot-drop-ok"), "a helmet may go in the helmet slot");

            Send(cell, PointerUpEvent.GetPooled(MakeTouch(TouchPhase.Ended, over)));

            Assert.AreEqual(SpareHelmet, controller.EquippedItemIn("helmet"));
            Assert.IsFalse(helmetCard.ClassListContains("slot-drop-ok"), "the highlight should clear on drop");
        }

        [UnityTest]
        public IEnumerator HoveringTheWrongSlotShowsItAsRefused()
        {
            var controller = CreateGearScreen();
            yield return null;
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var spare = controller.FirstInstanceOf(SpareHelmet);
            Assume.That(spare, Is.Not.Null);
            var cell = root.Q<VisualElement>("cell-" + spare);
            var armorCard = root.Q<VisualElement>("slot-armor");
            var press = cell.worldBound.center;
            var over = armorCard.worldBound.center;

            Send(cell, PointerDownEvent.GetPooled(MakeTouch(TouchPhase.Began, press)));
            Send(cell, PointerMoveEvent.GetPooled(MakeTouch(TouchPhase.Moved, over)));
            Assert.IsTrue(armorCard.ClassListContains("slot-drop-bad"));

            Send(cell, PointerUpEvent.GetPooled(MakeTouch(TouchPhase.Ended, over)));
            Assert.AreEqual(WornHelmet, controller.EquippedItemIn("helmet"), "a refused drop changes nothing");
        }

        [UnityTest]
        public IEnumerator AnItemWithAnIconShowsItsArtInsteadOfTheMonogram()
        {
            GiveIcon("graphics-card");
            var controller = CreateGearScreen();
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var cell = root.Q<VisualElement>("cell-" + controller.FirstInstanceOf("graphics-card"));

            Assert.IsNotNull(cell.Q<Image>(), "the cell should hold the item's art");
            Assert.IsNull(cell.Q<Label>(), "the placeholder monogram and name should be gone");
        }

        [UnityTest]
        public IEnumerator AnItemWithoutAnIconKeepsItsPlaceholder()
        {
            GiveIcon("graphics-card"); // some other item has art; this one does not
            var controller = CreateGearScreen();
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var cell = root.Q<VisualElement>("cell-" + controller.FirstInstanceOf("toolset"));

            Assert.IsNull(cell.Q<Image>());
            Assert.IsNotNull(cell.Q<Label>());
        }

        [UnityTest]
        public IEnumerator ATurnedItemsArtIsSpunAQuarterTurn()
        {
            GiveIcon("graphics-card");
            var controller = CreateGearScreen();
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var id = controller.FirstInstanceOf("graphics-card"); // 2x1
            var upright = root.Q<VisualElement>("cell-" + id).Q<Image>();
            Assert.AreEqual(0f, upright.style.rotate.value.angle.value, 0.01f);

            var turned = false;
            for (var y = 0; y < 12 && !turned; y++)
            {
                for (var x = 0; x < 10 && !turned; x++)
                {
                    turned = controller.TryMoveItem(id, x, y, 90) == null;
                }
            }

            Assume.That(turned, "the turned card should fit somewhere");
            controller.SelectItem(id);
            // A move only repositions the existing cell, so redraw by re-equipping nothing: the rotation
            // shows on the next full redraw, which a swap of worn gear triggers.
            var spare = controller.FirstInstanceOf("altyn-bulletproof-helmet-olive-drab");
            Assume.That(spare, Is.Not.Null);
            Assert.IsNull(controller.TryEquipItem(spare, "helmet"));

            var image = root.Q<VisualElement>("cell-" + id).Q<Image>();
            Assert.AreEqual(90f, image.style.rotate.value.angle.value, 0.01f);
        }

        [UnityTest]
        public IEnumerator AWornItemWithArtFillsItsSlotCardWithJustTheArt()
        {
            GiveIcon("rys-t-bulletproof-helmet-black");
            var controller = CreateGearScreen();
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var card = root.Q<VisualElement>("slot-helmet");

            Assert.IsTrue(card.ClassListContains("slot-art"), "the card should switch to art-only");
            var art = card.Q(className: "slot-art-image");
            Assert.IsNotNull(art);
            Assert.IsNotNull(art.Q<Image>().image);
            Assert.AreNotEqual(ScaleMode.ScaleAndCrop, art.Q<Image>().scaleMode, "art is never cropped");
            Assert.AreSame(card, art.parent);

            // A slot whose item has no art keeps its text layout.
            Assert.IsFalse(root.Q<VisualElement>("slot-armor").ClassListContains("slot-art"));
            Assert.IsNull(root.Q<VisualElement>("slot-armor").Q(className: "slot-art-image"));
        }

        [UnityTest]
        public IEnumerator ASlotCardKeepsItsSizeAndTheIconIsStretchedToFillIt()
        {
            // A 4x3 rig icon (192x144) is far from the 96x108 card's shape; the card must not change to suit
            // it, the icon is stretched to the card instead.
            GiveIcon("spiritus-systems-lv-119-plate-carrier-black-division-v1", 192, 144);
            var controller = CreateGearScreen();
            for (var frame = 0; frame < 4; frame++)
            {
                yield return null; // let layout run
            }

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var card = root.Q<VisualElement>("slot-rig");

            Assert.AreEqual(108f, card.resolvedStyle.height, 1f, "the card is not reshaped");
            Assert.AreEqual(ScaleMode.StretchToFill, card.Q(className: "slot-art-image").Q<Image>().scaleMode);
        }

        [UnityTest]
        public IEnumerator TextFreeArtIsFittedWholeAndTheCardIsNotReshaped()
        {
            // A wide rig render (4:3) in the 96x108 card: fitted at its own proportions, not stretched.
            GiveArt("spiritus-systems-lv-119-plate-carrier-black-division-v1", 192, 144);
            var controller = CreateGearScreen();
            for (var frame = 0; frame < 4; frame++)
            {
                yield return null;
            }

            var card = controller.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>("slot-rig");

            Assert.AreEqual(ScaleMode.ScaleToFit, card.Q(className: "slot-art-image").Q<Image>().scaleMode);
            Assert.AreEqual(108f, card.resolvedStyle.height, 1f);
        }

        [UnityTest]
        public IEnumerator TextFreeArtIsUsedInPreferenceToTheStashIcon()
        {
            GiveIcon("rys-t-bulletproof-helmet-black", 96, 96);
            GiveArt("rys-t-bulletproof-helmet-black", 10, 10);
            var controller = CreateGearScreen();
            yield return null;

            var card = controller.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>("slot-helmet");

            Assert.AreEqual(10, card.Q(className: "slot-art-image").Q<Image>().image.width,
                "the text-free art, not the 96px inventory icon with its name in the corner");
        }

        [UnityTest]
        public IEnumerator AnItemWithoutTextFreeArtFallsBackToTheStashIconStretched()
        {
            GiveIcon("rys-t-bulletproof-helmet-black", 96, 96);
            var controller = CreateGearScreen();
            yield return null;

            var card = controller.GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>("slot-helmet");

            Assert.AreEqual(96, card.Q(className: "slot-art-image").Q<Image>().image.width);
            Assert.AreEqual(ScaleMode.StretchToFill, card.Q(className: "slot-art-image").Q<Image>().scaleMode);
        }

        [UnityTest]
        public IEnumerator TheArtOnASlotCardFollowsSelectionAndSwaps()
        {
            GiveIcon("rys-t-bulletproof-helmet-black");
            GiveIcon("altyn-bulletproof-helmet-olive-drab");
            var controller = CreateGearScreen();
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var card = root.Q<VisualElement>("slot-helmet");
            controller.SelectItem(controller.EquippedInstanceIn("helmet"));
            Assert.IsTrue(card.Q(className: "slot-art-image").ClassListContains("cell-selected"));

            var spare = controller.FirstInstanceOf("altyn-bulletproof-helmet-olive-drab");
            Assume.That(spare, Is.Not.Null);
            Assert.IsNull(controller.TryEquipItem(spare, "helmet"));

            Assert.AreEqual(1, card.Query(className: "slot-art-image").ToList().Count, "one art element, not a pile");
        }

        [UnityTest]
        public IEnumerator TheDetailPanelSwatchShowsTheArtToo()
        {
            GiveIcon("rys-t-bulletproof-helmet-black");
            var controller = CreateGearScreen();
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            controller.SelectItem(controller.EquippedInstanceIn("helmet"));

            Assert.AreEqual("", root.Q<Label>("label-selected-icon").text);
            Assert.IsNotNull(root.Q<VisualElement>("swatch-selected").resolvedStyle.backgroundImage.texture);
        }

        /// <summary>A 1x1 item's cell in the stash: 48px pitch minus the 2px gap is 46.</summary>
        private static VisualElement SmallStashCell(VisualElement root)
        {
            var small = root.Q<VisualElement>("stash-grid").Children().FirstOrDefault(candidate =>
                Mathf.Approximately(candidate.style.width.value.value, 46f)
                && Mathf.Approximately(candidate.style.height.value.value, 46f));
            Assume.That(small, Is.Not.Null, "the sample stash should hold at least one 1x1 item");
            return small;
        }

        private static Touch MakeTouch(TouchPhase phase, Vector2 position) =>
            new Touch { fingerId = 1, phase = phase, position = position, pressure = 1f };

        private static void Send(VisualElement target, EventBase evt)
        {
            evt.target = target;
            target.SendEvent(evt);
            evt.Dispose();
        }

        private GearScreenController CreateGearScreen()
        {
#if UNITY_EDITOR
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                "Assets/_Project/UI/GearPanelSettings.asset");
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/_Project/UI/GearScreen.uxml");
            Assert.IsNotNull(panelSettings, "Run Safehouse > Build Gear Scene once before running this test.");
            Assert.IsNotNull(uxml, "Run Safehouse > Build Gear Scene once before running this test.");

            _host = new GameObject("GearScreen");
            var document = _host.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = uxml;
            return _host.AddComponent<GearScreenController>();
#else
            Assert.Ignore("Loads assets via AssetDatabase, so this only runs as an Editor PlayMode test.");
            return null;
#endif
        }
    }
}
