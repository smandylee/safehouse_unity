using Safehouse.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Safehouse.Editor
{
    /// <summary>
    /// Builds the PanelSettings asset and demo scene for the GEAR screen. A tool rather than a
    /// checked-in scene authored by hand: Unity serializes .unity/.asset files as YAML with GUID
    /// references that are easy to get subtly wrong by hand, so letting the Editor generate and
    /// save them itself is the reliable way to produce one. Safe to re-run - it overwrites both.
    /// </summary>
    public static class GearSceneBuilder
    {
        private const string UiFolder = "Assets/_Project/UI";
        private const string PanelSettingsPath = UiFolder + "/GearPanelSettings.asset";
        private const string UxmlPath = UiFolder + "/GearScreen.uxml";
        private const string ScenePath = "Assets/_Project/Scenes/Gear.unity";

        [MenuItem("Safehouse/Build Gear Scene")]
        public static void Build()
        {
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
            }

            // Fixed pixel scale: every size in GearScreen.uxml is authored in exact pixels to match
            // the artboard mock-up, so scaling with the screen would throw that off. No
            // themeStyleSheet - every visual property this screen needs is set explicitly in
            // Theme.uss and the custom Chrome controls, so Unity's default control skin is unused.
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.referenceResolution = new Vector2Int(1440, 900);
            EditorUtility.SetDirty(panelSettings);

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (uxml == null)
            {
                Debug.LogError($"GearSceneBuilder: no VisualTreeAsset at {UxmlPath}. Import the " +
                                "project (or wait for it to finish) before building the scene.");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // UI Toolkit's Screen Space - Overlay panel (our PanelSettings mode) draws straight to
            // the screen and needs no Camera to do it. But an empty scene has none at all, and the
            // Game view always shows its "No cameras rendering" placeholder over the top when that's
            // true - so this one exists purely to keep that placeholder from covering the UI. It
            // renders nothing (culling mask "Nothing") and its clear colour never actually shows,
            // since the UI fills the screen.
            var camera = new GameObject("UI Camera (renders nothing, see comment)").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.043f, 0.051f, 0.047f);
            camera.cullingMask = 0;

            var host = new GameObject("GearScreen");
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = uxml;
            host.AddComponent<GearScreenController>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"GearSceneBuilder: wrote {ScenePath} and {PanelSettingsPath}.");
        }

        /// <summary>
        /// Dev-only visual check: open Gear.unity, maximise the Game view so the whole Unity window
        /// is the rendered screen (nothing else to crop out), and start Play. Meant to be driven from
        /// outside the Editor (batchmode is graphics-less and can't render this) so a screenshot tool
        /// can grab a real frame - the Unity equivalent of tools/capture_demo.py on the Python side.
        /// </summary>
        [MenuItem("Safehouse/Open Gear Scene And Play (dev)")]
        public static void OpenAndPlay()
        {
            EditorSceneManager.OpenScene(ScenePath);

            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var gameView = EditorWindow.GetWindow(gameViewType);
            gameView.Show();
            gameView.maximized = true;
            gameView.Focus();

            EditorApplication.isPlaying = true;
        }
    }
}
