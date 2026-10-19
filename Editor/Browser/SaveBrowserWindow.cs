using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace SaveSystem.EditorTools
{
    /** <summary>A window to look through saves on disk: each section's version and data, edit a section, delete, open the folder.</summary> */
    public sealed class SaveBrowserWindow : EditorWindow
    {
        #region Private Fields

        private const string FolderPreference = "SaveSystem.Browser.Folder";
        private const float ListWidth = 220f;

        private string _folder;
        private SaveBrowserModel _model;
        private IReadOnlyList<string> _entries = new List<string>();
        private BrowsedSave _save;
        private string _sectionKey;
        private string _sectionText;
        private string _message;
        private string _confirmDelete;
        private Vector2 _listScroll;
        private Vector2 _sectionScroll;

        #endregion

        #region MonoBehaviour Callbacks

        private void OnEnable()
        {
            titleContent = new GUIContent("Save Browser");
            SetFolder(EditorPrefs.GetString(FolderPreference, UnitySave.SavesFolder));
        }

        private void OnGUI()
        {
            DrawToolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawEntries();
                DrawSave();
            }
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Info);
        }

        #endregion

        #region Public API

        /** <summary>Opens the window.</summary> */
        [MenuItem("Tools/Save System/Save Browser")]
        public static void Open() => GetWindow<SaveBrowserWindow>();

        #endregion

        #region Helpers

        private void SetFolder(string folder)
        {
            _folder = folder;
            EditorPrefs.SetString(FolderPreference, folder);
            _model = new SaveBrowserModel(new FileSaveStorage(folder));
            Refresh();
        }

        private void Refresh()
        {
            _entries = _model.Entries();
            _save = _save != null && _entries.Contains(_save.Entry) ? _model.Open(_save.Entry) : null;
            SelectSection(_sectionKey);
            _confirmDelete = null;
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var folder = EditorGUILayout.DelayedTextField(_folder, EditorStyles.toolbarTextField);
                if (folder != _folder) SetFolder(folder);
                if (GUILayout.Button("Saves", EditorStyles.toolbarButton, GUILayout.Width(50))) SetFolder(UnitySave.SavesFolder);
                if (GUILayout.Button("Settings", EditorStyles.toolbarButton, GUILayout.Width(60))) SetFolder(UnitySave.SettingsFolder);
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60))) Refresh();
                using (new EditorGUI.DisabledScope(!Directory.Exists(_folder)))
                    if (GUILayout.Button("Open Folder", EditorStyles.toolbarButton, GUILayout.Width(80))) EditorUtility.RevealInFinder(_folder);
            }
        }

        private void DrawEntries()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(_listScroll, GUILayout.Width(ListWidth)))
            {
                _listScroll = scroll.scrollPosition;
                if (_entries.Count == 0) EditorGUILayout.LabelField("No saves here.", EditorStyles.miniLabel);
                foreach (var entry in _entries)
                {
                    var selected = _save != null && _save.Entry == entry;
                    if (GUILayout.Toggle(selected, entry, EditorStyles.toolbarButton) && !selected)
                    {
                        _save = _model.Open(entry);
                        _confirmDelete = null;
                        _message = null;
                        SelectSection(_save.Document?.Sections.Keys.FirstOrDefault());
                    }
                }
            }
        }

        private void DrawSave()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (_save == null)
                {
                    EditorGUILayout.LabelField("Pick a save.", EditorStyles.miniLabel);
                    return;
                }

                EditorGUILayout.LabelField(_save.Entry, EditorStyles.boldLabel);
                if (_save.Transforms.Count > 0) EditorGUILayout.LabelField("Transforms: " + string.Join(", ", _save.Transforms));
                if (_save.UsedBackup) EditorGUILayout.HelpBox("Read from the backup: " + _save.Problem, MessageType.Warning);
                else if (_save.Document == null) EditorGUILayout.HelpBox("Can't read: " + _save.Problem, MessageType.Error);
                if (_save.Document?.Meta != null) EditorGUILayout.LabelField("Meta: " + _save.Document.Meta.ToString(Formatting.None), EditorStyles.wordWrappedMiniLabel);

                if (_save.Document != null) DrawSections();
                DrawDelete();
            }
        }

        private void DrawSections()
        {
            using (new EditorGUILayout.HorizontalScope())
                foreach (var pair in _save.Document.Sections)
                    if (GUILayout.Toggle(_sectionKey == pair.Key, $"{pair.Key} v{pair.Value.Version}", EditorStyles.miniButton) && _sectionKey != pair.Key)
                        SelectSection(pair.Key);

            if (_sectionText == null) return;
            using (var scroll = new EditorGUILayout.ScrollViewScope(_sectionScroll))
            {
                _sectionScroll = scroll.scrollPosition;
                _sectionText = EditorGUILayout.TextArea(_sectionText, GUILayout.ExpandHeight(true));
            }
            if (GUILayout.Button("Write Section (keeps the old file as the backup)"))
            {
                var result = _model.WriteSection(_save, _sectionKey, _sectionText);
                _message = result.Succeeded ? $"Wrote {_sectionKey}." : result.Problem;
                Refresh();
            }
        }

        private void DrawDelete()
        {
            if (_confirmDelete != _save.Entry)
            {
                if (GUILayout.Button("Delete")) _confirmDelete = _save.Entry;
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"Really delete {_save.Entry}?"))
                {
                    _message = _model.Delete(_save.Entry) ?? $"Deleted {_save.Entry}.";
                    _save = null;
                    Refresh();
                }
                if (GUILayout.Button("Keep it")) _confirmDelete = null;
            }
        }

        private void SelectSection(string key)
        {
            _sectionKey = key;
            _sectionText = _save?.Document == null || key == null ? null : SaveBrowserModel.SectionText(_save.Document, key);
        }

        #endregion
    }
}
