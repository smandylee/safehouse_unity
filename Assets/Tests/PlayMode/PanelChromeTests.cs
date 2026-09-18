using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Safehouse.UI.Chrome;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Safehouse.Tests
{
    /// <summary>
    /// Guards the custom-drawn chrome against the bug that kept the GEAR screen's boxes visibly
    /// hanging out past their panel's right and bottom edges: FacetedPanel drew its frame to
    /// contentRect (the box left over after padding) instead of to the element's own box, so the
    /// frame was drawn two paddings too small while the children it framed stayed inside the
    /// padding. Layout was correct throughout - only the drawing disagreed with it - so nothing
    /// that checked element rects would have caught it. These check the drawn geometry instead.
    /// </summary>
    public sealed class PanelChromeTests
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
        public IEnumerator TheDrawnFrameCoversTheWholePanelIncludingItsPadding()
        {
            var panel = new FacetedPanel();
            panel.style.width = 400;
            panel.style.height = 300;
            panel.style.paddingLeft = 14;
            panel.style.paddingRight = 14;
            panel.style.paddingTop = 14;
            panel.style.paddingBottom = 14;

            var root = CreatePanel();
            root.Add(panel);
            yield return null;

            // Stated as "frame minus content box == the padding", both read back as resolved
            // values: the panel scales the design to fit the window, so the authored 400x300 and
            // 14px padding all arrive scaled, and hard-coding them would just pin the scale factor.
            // The gap being zero instead of the padding is precisely the bug this guards - that is
            // what drawing the frame to the content box produces.
            var horizontalPadding = panel.resolvedStyle.paddingLeft + panel.resolvedStyle.paddingRight;
            var verticalPadding = panel.resolvedStyle.paddingTop + panel.resolvedStyle.paddingBottom;
            Assert.Greater(horizontalPadding, 0f, "the panel under test is supposed to have padding");

            Assert.AreEqual(horizontalPadding, panel.FrameRect.width - panel.contentRect.width, 0.5f,
                "the frame must span the panel's full width, padding included");
            Assert.AreEqual(verticalPadding, panel.FrameRect.height - panel.contentRect.height, 0.5f,
                "the frame must span the panel's full height, padding included");
            Assert.AreEqual(Vector2.zero, panel.FrameRect.position,
                "the frame starts at the panel's own top-left corner");
        }

        [UnityTest]
        public IEnumerator AChildStaysInsideTheDrawnFrame()
        {
            // The symptom as reported: with padding on the panel, a child that fills the content
            // box must still sit within the frame drawn around the panel, on every side.
            var panel = new FacetedPanel();
            panel.style.width = 400;
            panel.style.height = 300;
            panel.style.paddingLeft = 14;
            panel.style.paddingRight = 14;
            panel.style.paddingTop = 14;
            panel.style.paddingBottom = 14;

            var child = new VisualElement { style = { flexGrow = 1 } };
            panel.Add(child);

            var root = CreatePanel();
            root.Add(panel);
            yield return null;

            var frame = panel.FrameRect;
            var childInPanelSpace = panel.WorldToLocal(child.worldBound);

            Assert.GreaterOrEqual(childInPanelSpace.xMin, frame.xMin - 0.5f, "child crosses the left edge");
            Assert.GreaterOrEqual(childInPanelSpace.yMin, frame.yMin - 0.5f, "child crosses the top edge");
            Assert.LessOrEqual(childInPanelSpace.xMax, frame.xMax + 0.5f, "child crosses the right edge");
            Assert.LessOrEqual(childInPanelSpace.yMax, frame.yMax + 0.5f, "child crosses the bottom edge");
        }

        [UnityTest]
        public IEnumerator TheGearScreensOwnPanelsFrameTheirContents()
        {
            var controller = CreateGearScreen();
            yield return null;

            var root = controller.GetComponent<UIDocument>().rootVisualElement;
            var offenders = new List<string>();

            foreach (var panel in root.Query<FacetedPanel>().ToList())
            {
                var frame = panel.FrameRect;
                foreach (var child in panel.Children())
                {
                    var box = panel.WorldToLocal(child.worldBound);
                    if (box.xMax > frame.xMax + 0.5f || box.yMax > frame.yMax + 0.5f
                        || box.xMin < frame.xMin - 0.5f || box.yMin < frame.yMin - 0.5f)
                    {
                        offenders.Add($"{child.name} {box} outside frame {frame}");
                    }
                }
            }

            CollectionAssert.IsEmpty(offenders,
                "these sit outside the panel frame drawn around them:\n" + string.Join("\n", offenders));
        }

        private VisualElement CreatePanel()
        {
#if UNITY_EDITOR
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                "Assets/_Project/UI/GearPanelSettings.asset");
            Assert.IsNotNull(panelSettings, "Run Safehouse > Build Gear Scene once first.");
            _host = new GameObject("ChromeTestHost");
            var document = _host.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            return document.rootVisualElement;
#else
            Assert.Ignore("Loads assets via AssetDatabase, so this only runs as an Editor PlayMode test.");
            return null;
#endif
        }

        private Safehouse.UI.GearScreenController CreateGearScreen()
        {
#if UNITY_EDITOR
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                "Assets/_Project/UI/GearPanelSettings.asset");
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/_Project/UI/GearScreen.uxml");
            Assert.IsNotNull(panelSettings, "Run Safehouse > Build Gear Scene once first.");
            Assert.IsNotNull(uxml, "Run Safehouse > Build Gear Scene once first.");

            _host = new GameObject("GearScreen");
            var document = _host.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = uxml;
            return _host.AddComponent<Safehouse.UI.GearScreenController>();
#else
            Assert.Ignore("Loads assets via AssetDatabase, so this only runs as an Editor PlayMode test.");
            return null;
#endif
        }
    }
}
