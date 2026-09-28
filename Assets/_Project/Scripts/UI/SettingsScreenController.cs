using System;
using System.Diagnostics;
using Safehouse.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Safehouse.UI
{
    /// <summary>
    /// The SETTINGS screen: shows where the game keeps its data and exposes useful shortcuts.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SettingsScreenController : MonoBehaviour
    {
        private VisualElement _root;
        private Label _labelDataFolder;
        private Button _buttonOpenFolder;
        private Label _labelStatus;

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;

            _labelDataFolder = _root.Q<Label>("label-data-folder");
            _buttonOpenFolder = _root.Q<Button>("button-open-folder");
            _labelStatus = _root.Q<Label>("label-status");

            _root.Q<Button>("navtab-character").clicked += () => ScreenNavigator.Go("character");
            _root.Q<Button>("navtab-gear").clicked += () => ScreenNavigator.Go("gear");
            _root.Q<Button>("navtab-traders").clicked += () => ScreenNavigator.Go("traders");
            _root.Q<Button>("navtab-hideout").clicked += () => ScreenNavigator.Go("hideout");
            _root.Q<Button>("navtab-scavenge").clicked += () => ScreenNavigator.Go("scavenge");
            _buttonOpenFolder.clicked += OpenDataFolder;

            ScreenNavigator.Register("settings", Show, Hide);
            Hide();
        }

        private void OnDisable()
        {
            ScreenNavigator.Unregister("settings");
        }

        public void Show()
        {
            _root.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Hide()
        {
            _root.style.display = DisplayStyle.None;
        }

        private void Refresh()
        {
            var path = ProfileRepository.DefaultFolder;
            _labelDataFolder.text = path;
            _labelStatus.text = "";
        }

        private void OpenDataFolder()
        {
            var path = ProfileRepository.DefaultFolder;
            try
            {
                if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false });
                }
                else
                {
                    Application.OpenURL($"file://{path}");
                }

                _labelStatus.text = "Opened the data folder.";
            }
            catch (Exception error)
            {
                _labelStatus.text = $"Could not open folder: {error.Message}";
                _labelStatus.EnableInClassList("text-danger", true);
            }
        }
    }
}
