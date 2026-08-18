using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework.LocalizationSystem.Editor
{
    /// <summary>
    /// 在编辑器提交 Text 或 TMP_Text 文本修改后，同步同对象 LocalizedComponent 的 Key。
    /// </summary>
    [InitializeOnLoad]
    internal static class LocalizedTextKeySynchronizer
    {
        private const string LegacyTextPropertyPath = "m_Text";
        private const string TextMeshProPropertyPath = "m_text";
        private static readonly Dictionary<int, PendingKeySync> PendingSyncs = new();
        private static bool isFlushScheduled;

        static LocalizedTextKeySynchronizer()
        {
            Undo.postprocessModifications += OnPostprocessModifications;
            AssemblyReloadEvents.beforeAssemblyReload += ClearPendingSyncs;
        }

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (Application.isPlaying) return modifications;

            for (int i = 0; i < modifications.Length; i++)
            {
                PropertyModification modification = modifications[i].currentValue;
                if (!TryGetLocalizedComponent(modification, out LocalizedComponent localizedComponent)) continue;
                PendingSyncs[localizedComponent.GetInstanceID()] = new PendingKeySync(localizedComponent, modification.value ?? string.Empty);
            }

            if (PendingSyncs.Count > 0 && !isFlushScheduled)
            {
                isFlushScheduled = true;
                EditorApplication.delayCall += FlushPendingSyncs;
            }

            return modifications;
        }

        private static bool TryGetLocalizedComponent(PropertyModification modification, out LocalizedComponent localizedComponent)
        {
            localizedComponent = null;
            if (modification == null) return false;

            Component textComponent;
            LocalizedTargetType expectedTargetType;
            if (modification.target is Text legacyText && string.Equals(modification.propertyPath, LegacyTextPropertyPath, StringComparison.Ordinal))
            {
                textComponent = legacyText;
                expectedTargetType = LocalizedTargetType.Text;
            }
            else if (modification.target is TMP_Text textMeshPro && string.Equals(modification.propertyPath, TextMeshProPropertyPath, StringComparison.Ordinal))
            {
                textComponent = textMeshPro;
                expectedTargetType = LocalizedTargetType.TextMeshPro;
            }
            else
            {
                return false;
            }

            localizedComponent = textComponent.GetComponent<LocalizedComponent>();
            return localizedComponent != null && localizedComponent.TargetType == expectedTargetType;
        }

        private static void FlushPendingSyncs()
        {
            isFlushScheduled = false;
            List<PendingKeySync> pendingSyncs = new(PendingSyncs.Values);
            PendingSyncs.Clear();

            for (int i = 0; i < pendingSyncs.Count; i++)
            {
                PendingKeySync pendingSync = pendingSyncs[i];
                LocalizedComponent localizedComponent = pendingSync.LocalizedComponent;
                if (localizedComponent == null || string.Equals(localizedComponent.LocalizationKey, pendingSync.Key, StringComparison.Ordinal)) continue;

                Undo.RecordObject(localizedComponent, "同步本地化 Key");
                SerializedObject serializedComponent = new(localizedComponent);
                serializedComponent.FindProperty("localizationKey").stringValue = pendingSync.Key;
                serializedComponent.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(localizedComponent);
                EditorUtility.SetDirty(localizedComponent);
                if (localizedComponent.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(localizedComponent.gameObject.scene);
            }
        }

        private static void ClearPendingSyncs()
        {
            PendingSyncs.Clear();
            isFlushScheduled = false;
        }

        private readonly struct PendingKeySync
        {
            public PendingKeySync(LocalizedComponent localizedComponent, string key)
            {
                LocalizedComponent = localizedComponent;
                Key = key;
            }

            public LocalizedComponent LocalizedComponent { get; }
            public string Key { get; }
        }
    }
}
