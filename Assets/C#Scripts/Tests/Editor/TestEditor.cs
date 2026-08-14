using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Test))]
public class TestEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();

        if (GUILayout.Button("执行测试"))
        {
            Test test = (Test)target;
            test.ExecuteTest();
        }
    }
}
