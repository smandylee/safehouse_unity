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
        private const string TradersUxmlPath = UiFolder + "/TradersScreen.uxml";
        private const string HideoutUxmlPath = UiFolder + "/HideoutScreen.uxml";
        private const string SettingsUxmlPath = UiFolder + "/SettingsScreen.uxml";
        private const string ScavengeUxmlPath = UiFolder + "/ScavengeScreen.uxml";
        private const string CharacterUxmlPath = UiFolder + "/CharacterScreen.uxml";
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

            // GearScreen.uxml is authored in exact pixels against a 1440x900 artboard, so the panel
            // scales that whole design uniformly to whatever the window actually is. The two
            // alternatives are both wrong here and both were tried: ConstantPixelSize renders it at
            // a literal 1440x900 and simply cuts off whatever doesn't fit in a smaller window, and
            // leaving gear-root to stretch (no fixed size at all) squashes the design out of
            // proportion and makes fixed-height cards overlap their own contents.
            //
            // Expand, not Shrink: both names describe what happens to the PANEL AREA measured in
            // reference units, not to the design drawn inside it. Shrink picks the larger scale, so
            // the panel area ends up smaller than 1440x900 on one axis and that axis gets cut off -
            // measured here, it cropped the bottom bar off a 1280x820 window. Expand picks the
            // smaller scale, so the panel area covers the whole window and the design stays fully
            // on screen, letterboxed on whichever axis the aspect ratio doesn't match.
            //
            // No themeStyleSheet - every visual property this screen needs is set explicitly in
            // Theme.uss and the custom Chrome controls, so Unity's default control skin is unused.
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1440, 900);
            panelSettings.screenMatchMode = PanelScreenMatchMode.Expand;
            EditorUtility.SetDirty(panelSettings);

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (uxml == null)
            {
                Debug.LogError($"GearSceneBuilder: no VisualTreeAsset at {UxmlPath}. Import the " +
                                "project (or wait for it to finish) before building the scene.");
                return;
            }

            var tradersUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(TradersUxmlPath);
            if (tradersUxml == null)
            {
                Debug.LogError($"GearSceneBuilder: no VisualTreeAsset at {TradersUxmlPath}.");
                return;
            }

            var hideoutUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HideoutUxmlPath);
            if (hideoutUxml == null)
            {
                Debug.LogError($"GearSceneBuilder: no VisualTreeAsset at {HideoutUxmlPath}.");
                return;
            }

            var settingsUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SettingsUxmlPath);
            if (settingsUxml == null)
            {
                Debug.LogError($"GearSceneBuilder: no VisualTreeAsset at {SettingsUxmlPath}.");
                return;
            }

            var scavengeUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ScavengeUxmlPath);
            if (scavengeUxml == null)
            {
                Debug.LogError($"GearSceneBuilder: no VisualTreeAsset at {ScavengeUxmlPath}.");
                return;
            }

            var characterUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CharacterUxmlPath);
            if (characterUxml == null)
            {
                Debug.LogError($"GearSceneBuilder: no VisualTreeAsset at {CharacterUxmlPath}.");
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

            // The TRADERS and HIDEOUT screens are additional documents on the same panel. They start hidden;
            // the top bar's tabs (ScreenNavigator) switch between them.
            var tradersHost = new GameObject("TradersScreen");
            var tradersDocument = tradersHost.AddComponent<UIDocument>();
            tradersDocument.panelSettings = panelSettings;
            tradersDocument.visualTreeAsset = tradersUxml;
            tradersHost.AddComponent<TradersScreenController>();

            var hideoutHost = new GameObject("HideoutScreen");
            var hideoutDocument = hideoutHost.AddComponent<UIDocument>();
            hideoutDocument.panelSettings = panelSettings;
            hideoutDocument.visualTreeAsset = hideoutUxml;
            hideoutHost.AddComponent<HideoutScreenController>();

            var settingsHost = new GameObject("SettingsScreen");
            var settingsDocument = settingsHost.AddComponent<UIDocument>();
            settingsDocument.panelSettings = panelSettings;
            settingsDocument.visualTreeAsset = settingsUxml;
            settingsHost.AddComponent<SettingsScreenController>();

            var scavengeHost = new GameObject("ScavengeScreen");
            var scavengeDocument = scavengeHost.AddComponent<UIDocument>();
            scavengeDocument.panelSettings = panelSettings;
            scavengeDocument.visualTreeAsset = scavengeUxml;
            scavengeHost.AddComponent<ScavengeScreenController>();

            var characterHost = new GameObject("CharacterScreen");
            var characterDocument = characterHost.AddComponent<UIDocument>();
            characterDocument.panelSettings = panelSettings;
            characterDocument.visualTreeAsset = characterUxml;
            characterHost.AddComponent<CharacterScreenController>();

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
