using System.Collections;
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
