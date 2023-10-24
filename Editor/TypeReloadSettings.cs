using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyTypeReload.Editor
{
    [FilePath("ProjectSettings/TypeReloadSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class TypeReloadSettings : ScriptableSingleton<TypeReloadSettings>
    {
        [SerializeField]
        private List<string> m_AssemblyWhiteList = new()
        {
            "Assembly-CSharp",
        };

        public List<string> AssemblyWhiteList => m_AssemblyWhiteList;

        private void EnsureEditable() => hideFlags &= ~HideFlags.NotEditable;

        private void OnEnable() => EnsureEditable();

        private void OnDisable() => Save();

        public void Save()
        {
            EnsureEditable();
            Save(true);
        }

        public SerializedObject AsSerializedObject()
        {
            EnsureEditable();
            return new SerializedObject(this);
        }

        public const string PathInProjectSettings = "Project/Easy Type Reload";

        public static void OpenInProjectSettings() => SettingsService.OpenProjectSettings(PathInProjectSettings);
    }
}
