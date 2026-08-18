using System.Collections.Generic;
using GameFramework.UI.Layout;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GameFramework.UI.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(BakedLayout), true)]
    public sealed class BakedLayoutEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("布局只在点击按钮或由业务代码显式调用时更新，不会自动参与 Canvas Layout Rebuild。", MessageType.Info);
            if (GUILayout.Button("Bake Layout")) BakeSelectedLayouts();
        }

        private void BakeSelectedLayouts()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                BakedLayout layout = (BakedLayout)targets[i];
                List<Object> undoTargets = GetUndoTargets(layout);
                Undo.RecordObjects(undoTargets.ToArray(), $"Bake {layout.GetType().Name}");
                layout.ApplyLayout();
                for (int j = 0; j < undoTargets.Count; j++)
                {
                    Object changedObject = undoTargets[j];
                    EditorUtility.SetDirty(changedObject);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(changedObject);
                }
                if (layout.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);
            }
            SceneView.RepaintAll();
        }

        private static List<Object> GetUndoTargets(BakedLayout layout)
        {
            List<Object> undoTargets = new() { layout };
            Transform layoutTransform = layout.transform;
            for (int i = 0; i < layoutTransform.childCount; i++)
            {
                if (layoutTransform.GetChild(i) is RectTransform child) undoTargets.Add(child);
            }
            return undoTargets;
        }
    }
}
