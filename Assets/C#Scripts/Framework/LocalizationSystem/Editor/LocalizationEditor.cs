using System;
using System.Collections.Generic;
using GameFramework.LocalizationSystem.Generated;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework.LocalizationSystem.Editor
{
    /// <summary>
    /// 提供 LocalizedComponent 的配置检查和运行时预览入口。
    /// </summary>
    [CustomEditor(typeof(LocalizedComponent))]
    public sealed class LocalizedComponentEditor : UnityEditor.Editor
    {
        private SerializedProperty targetTypeProperty;
        private SerializedProperty localizationKeyProperty;
        private SerializedProperty legacyTextProperty;
        private SerializedProperty textMeshProProperty;
        private SerializedProperty targetImageProperty;
        private string validatedKey;
        private LocalizedTargetType validatedTargetType;
        private string keyValidationMessage;

        private void OnEnable()
        {
            targetTypeProperty = serializedObject.FindProperty("targetType");
            localizationKeyProperty = serializedObject.FindProperty("localizationKey");
            legacyTextProperty = serializedObject.FindProperty("legacyText");
            textMeshProProperty = serializedObject.FindProperty("textMeshPro");
            targetImageProperty = serializedObject.FindProperty("targetImage");
            EditorApplication.projectChanged += InvalidateKeyValidation;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= InvalidateKeyValidation;
        }

        private void InvalidateKeyValidation()
        {
            validatedKey = null;
            Repaint();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(targetTypeProperty);
            EditorGUILayout.PropertyField(localizationKeyProperty);
            EditorGUILayout.PropertyField(GetCurrentTargetProperty());
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            if (GUILayout.Button("自动绑定当前对象上的目标组件"))
            {
                BindCurrentTarget();
            }

            if (Application.isPlaying && GUILayout.Button("刷新本地化内容"))
            {
                ((LocalizedComponent)target).RefreshLocalization();
            }

            DrawConfigurationWarnings();
        }

        private SerializedProperty GetCurrentTargetProperty()
        {
            return (LocalizedTargetType)targetTypeProperty.enumValueIndex switch
            {
                LocalizedTargetType.Text => legacyTextProperty,
                LocalizedTargetType.TextMeshPro => textMeshProProperty,
                LocalizedTargetType.Image => targetImageProperty,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        private void BindCurrentTarget()
        {
            LocalizedComponent localizedComponent = (LocalizedComponent)target;
            SerializedProperty currentTargetProperty = GetCurrentTargetProperty();
            Component targetComponent = localizedComponent.TargetType switch
            {
                LocalizedTargetType.Text => localizedComponent.GetComponent<Text>(),
                LocalizedTargetType.TextMeshPro => localizedComponent.GetComponent<TMP_Text>(),
                LocalizedTargetType.Image => localizedComponent.GetComponent<Image>(),
                _ => null
            };

            Undo.RecordObject(localizedComponent, "绑定本地化目标组件");
            currentTargetProperty.objectReferenceValue = targetComponent;
            serializedObject.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(localizedComponent);
            EditorUtility.SetDirty(localizedComponent);
        }

        private void DrawConfigurationWarnings()
        {
            if (string.IsNullOrWhiteSpace(localizationKeyProperty.stringValue))
            {
                EditorGUILayout.HelpBox("尚未配置本地化 Key。显示文本不会被自动用作 Key。", MessageType.Warning);
                return;
            }

            if (GetCurrentTargetProperty().objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("当前对象上没有与目标类型对应的组件。", MessageType.Error);
            }

            LocalizedTargetType targetType = (LocalizedTargetType)targetTypeProperty.enumValueIndex;
            string localizationKey = localizationKeyProperty.stringValue;
            if (!string.Equals(validatedKey, localizationKey, StringComparison.Ordinal) || validatedTargetType != targetType)
            {
                validatedKey = localizationKey;
                validatedTargetType = targetType;
                keyValidationMessage = ValidateKeyInLanguagePackages(localizationKey, targetType);
            }

            if (!string.IsNullOrEmpty(keyValidationMessage))
            {
                EditorGUILayout.HelpBox(keyValidationMessage, MessageType.Warning);
            }
        }

        private static string ValidateKeyInLanguagePackages(string localizationKey, LocalizedTargetType targetType)
        {
            string sectionId = targetType == LocalizedTargetType.Image ? LocalizationMgr.ResourcePathSectionId : LocalizationMgr.TextSectionId;
            string[] assetGuids = AssetDatabase.FindAssets("t:LocalizationDataSO");
            if (assetGuids.Length == 0)
            {
                return "项目中未找到 LocalizationDataSO，暂时无法校验 Key。";
            }

            List<string> missingLanguages = new();
            foreach (string assetGuid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                LocalizationDataSO languagePackage = AssetDatabase.LoadAssetAtPath<LocalizationDataSO>(assetPath);
                if (languagePackage == null || !ContainsKey(languagePackage, sectionId, localizationKey))
                {
                    string languageId = languagePackage == null || string.IsNullOrWhiteSpace(languagePackage.Language) ? assetPath : languagePackage.Language;
                    missingLanguages.Add(languageId);
                }
            }

            return missingLanguages.Count == 0 ? string.Empty : $"以下语言包不存在 Key“{localizationKey}”：{string.Join("、", missingLanguages)}。";
        }

        private static bool ContainsKey(LocalizationDataSO languagePackage, string sectionId, string localizationKey)
        {
            foreach (LocalizationTableSection section in languagePackage.Sections)
            {
                if (section == null || !string.Equals(section.TableId, sectionId, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (LocalizationDataEntry entry in section.Entries)
                {
                    if (entry != null && string.Equals(entry.Key, localizationKey, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            return false;
        }
    }

    /// <summary>
    /// 提供 Text、TMP_Text 和 Image 的右键本地化组件快捷入口。
    /// </summary>
    public static class LocalizationEditor
    {
        private const string TextMenuPath = "CONTEXT/Text/Add Localized Component";
        private const string TextMeshProMenuPath = "CONTEXT/TMP_Text/Add Localized Component";
        private const string ImageMenuPath = "CONTEXT/Image/Add Localized Component";

        [MenuItem(TextMenuPath)]
        private static void AddToText(MenuCommand menuCommand)
        {
            AddLocalizedComponent(menuCommand.context as Text, LocalizedTargetType.Text);
        }

        [MenuItem(TextMenuPath, true)]
        private static bool ValidateAddToText(MenuCommand menuCommand)
        {
            return CanAddLocalizedComponent(menuCommand.context as Text);
        }

        [MenuItem(TextMeshProMenuPath)]
        private static void AddToTextMeshPro(MenuCommand menuCommand)
        {
            AddLocalizedComponent(menuCommand.context as TMP_Text, LocalizedTargetType.TextMeshPro);
        }

        [MenuItem(TextMeshProMenuPath, true)]
        private static bool ValidateAddToTextMeshPro(MenuCommand menuCommand)
        {
            return CanAddLocalizedComponent(menuCommand.context as TMP_Text);
        }

        [MenuItem(ImageMenuPath)]
        private static void AddToImage(MenuCommand menuCommand)
        {
            AddLocalizedComponent(menuCommand.context as Image, LocalizedTargetType.Image);
        }

        [MenuItem(ImageMenuPath, true)]
        private static bool ValidateAddToImage(MenuCommand menuCommand)
        {
            return CanAddLocalizedComponent(menuCommand.context as Image);
        }

        private static bool CanAddLocalizedComponent(Component targetComponent)
        {
            return targetComponent != null && targetComponent.GetComponent<LocalizedComponent>() == null;
        }

        /// <summary>
        /// 添加或配置 LocalizedComponent 到目标组件上。
        /// </summary>
        /// <param name="targetComponent">要添加 LocalizedComponent 的目标组件。</param>
        /// <param name="targetType">目标组件的类型。</param>
        private static void AddLocalizedComponent(Component targetComponent, LocalizedTargetType targetType)
        {
            if (targetComponent == null)
            {
                Logger.Instance.LogError("未找到本地化目标组件，无法添加 LocalizedComponent。");
                return;
            }

            LocalizedComponent existingComponent = targetComponent.GetComponent<LocalizedComponent>();
            if (existingComponent != null)
            {
                Selection.activeObject = existingComponent;
                return;
            }

            LocalizedComponent localizedComponent = Undo.AddComponent<LocalizedComponent>(targetComponent.gameObject);
            Undo.RecordObject(localizedComponent, "配置本地化组件");
            SerializedObject serializedComponent = new(localizedComponent);
            serializedComponent.FindProperty("targetType").enumValueIndex = (int)targetType;
            serializedComponent.FindProperty(GetTargetPropertyName(targetType)).objectReferenceValue = targetComponent;
            serializedComponent.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(localizedComponent);
            EditorUtility.SetDirty(localizedComponent);

            if (targetComponent.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(targetComponent.gameObject.scene);
            }

            Selection.activeObject = localizedComponent;
        }

        private static string GetTargetPropertyName(LocalizedTargetType targetType)
        {
            return targetType switch
            {
                LocalizedTargetType.Text => "legacyText",
                LocalizedTargetType.TextMeshPro => "textMeshPro",
                LocalizedTargetType.Image => "targetImage",
                _ => throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "未知的本地化目标类型。")
            };
        }
    }
}
