using System.Collections;
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

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }
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
