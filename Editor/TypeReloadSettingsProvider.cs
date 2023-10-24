using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace EasyTypeReload.Editor
{
    internal sealed class TypeReloadSettingsProvider : SettingsProvider
    {
        private SerializedObject m_SerializedObject;
        private SerializedProperty m_AssemblyWhiteList;
        private ReorderableList m_AssemblyWhiteListGUI;

        public TypeReloadSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null)
            : base(path, scopes, keywords) { }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            base.OnActivate(searchContext, rootElement);

            TypeReloadSettings.instance.Save();
            m_SerializedObject = TypeReloadSettings.instance.AsSerializedObject();
            m_AssemblyWhiteList = m_SerializedObject.FindProperty("m_AssemblyWhiteList");
        }

        public override void OnGUI(string searchContext)
        {
            m_SerializedObject.Update();
            EditorGUI.BeginChangeCheck();

            GUILayout.BeginHorizontal();
            GUILayout.Space(10);
            GUILayout.BeginVertical();
            GUILayout.Space(15);

            DrawAssemblyWhiteListField();

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                m_SerializedObject.ApplyModifiedProperties();
                TypeReloadSettings.instance.Save();
            }
        }

        private void DrawAssemblyWhiteListField()
        {
            m_AssemblyWhiteListGUI ??= new ReorderableList(m_SerializedObject, m_AssemblyWhiteList, false, true, true, false)
            {
                multiSelect = false,
                elementHeight = EditorGUIUtility.singleLineHeight,
                drawHeaderCallback = (Rect rect) => EditorGUI.LabelField(rect, "Assembly White List"),
                drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
                {
                    const float removeButtonWidth = 20;

                    GUIStyle buttonStyle = ReorderableList.defaultBehaviours.preButton;
                    float buttonHeightDelta = rect.height - buttonStyle.lineHeight;
                    Rect buttonRect = new Rect(rect.x, rect.y + buttonHeightDelta / 2, removeButtonWidth, rect.height - buttonHeightDelta);
                    GUIContent buttonIcon = EditorGUIUtility.TrIconContent("Toolbar Minus", "Remove from the list");

                    if (GUI.Button(buttonRect, buttonIcon, buttonStyle))
                    {
                        m_AssemblyWhiteList.DeleteArrayElementAtIndex(index);
                        return;
                    }

                    // I dont known why, but sometimes this will happen.
                    if (index >= m_AssemblyWhiteList.arraySize)
                    {
                        return;
                    }

                    var prop = m_AssemblyWhiteList.GetArrayElementAtIndex(index);
                    rect.xMin += removeButtonWidth + 2;
                    EditorGUI.LabelField(rect, new GUIContent(prop.stringValue, EditorGUIUtility.FindTexture("Assembly Icon")));
                },
                onSelectCallback = (ReorderableList list) =>
                {
                    int index = list.selectedIndices[0];
                    SerializedProperty prop = list.serializedProperty.GetArrayElementAtIndex(index);
                    string path = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(prop.stringValue);

                    if (path is not null)
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(path);
                        EditorGUIUtility.PingObject(asset);
                    }
                },
                onAddDropdownCallback = (Rect buttonRect, ReorderableList list) =>
                {
                    IEnumerable<string> assemblies = (
                        from assembly in CompilationPipeline.GetAssemblies()
                        orderby assembly.name ascending
                        select assembly.name
                    );

                    var menu = new GenericMenu();
                    var whitelist = new HashSet<string>(TypeReloadSettings.instance.AssemblyWhiteList);

                    foreach (var assemblyName in assemblies)
                    {
                        string name = assemblyName;
                        bool selected = whitelist.Contains(name);
                        menu.AddItem(new GUIContent(name), selected, selected ? null : () =>
                        {
                            var array = list.serializedProperty;
                            array.InsertArrayElementAtIndex(array.arraySize);

                            var prop = array.GetArrayElementAtIndex(array.arraySize - 1);
                            prop.stringValue = name;

                            array.serializedObject.ApplyModifiedProperties();
                            GUI.changed = true;
                        });
                    }

                    menu.DropDown(buttonRect);
                }
            };

            // 自己获取的 rect 比 ReorderableList 获取的 rect 宽度稍窄一点
            Rect rect = EditorGUILayout.GetControlRect(false, m_AssemblyWhiteListGUI.GetHeight());
            m_AssemblyWhiteListGUI.DoList(EditorGUI.IndentedRect(rect));
        }

        [SettingsProvider]
        public static SettingsProvider CreateProjectSettingsProvider()
        {
            return new TypeReloadSettingsProvider(TypeReloadSettings.PathInProjectSettings, SettingsScope.Project);
        }
    }
}
