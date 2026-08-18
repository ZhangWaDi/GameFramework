using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GameFramework.UI.Editor
{
    [CustomEditor(typeof(UIElementBuilder))]
    public sealed class UIElementBuilderEditor : UnityEditor.Editor
    {
        private const float Spacing = 2f;

        private SerializedProperty bindingsProperty;
        private SerializedProperty legacyGameObjectsProperty;
        private SerializedProperty legacyNamesProperty;
        private SerializedProperty legacyTypesProperty;
        private SerializedProperty legacyAuthoritiesProperty;
        private ReorderableList bindingList;

        private void OnEnable()
        {
            bindingsProperty = serializedObject.FindProperty("bindings");
            legacyGameObjectsProperty = serializedObject.FindProperty("gameObjectList");
            legacyNamesProperty = serializedObject.FindProperty("itemNameList");
            legacyTypesProperty = serializedObject.FindProperty("itemTypeList");
            legacyAuthoritiesProperty = serializedObject.FindProperty("itemAuthorityTypeList");
            bindingList = new ReorderableList(serializedObject, bindingsProperty, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "UI 元素绑定"),
                drawElementCallback = DrawBinding,
                elementHeight = EditorGUIUtility.singleLineHeight * 3f + Spacing * 4f,
                onAddCallback = AddBinding
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawLegacyMigration();
            bindingList.DoLayoutList();
            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed) PrefabUtility.RecordPrefabInstancePropertyModifications((UIElementBuilder)target);

            UIElementBuilder builder = (UIElementBuilder)target;
            List<string> errors = UIElementCodeGenerator.Validate(builder);
            for (int i = 0; i < errors.Count; i++) EditorGUILayout.HelpBox(errors[i], MessageType.Error);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(errors.Count > 0))
                {
                    if (GUILayout.Button("生成 Binding 文件")) GenerateBindingFile(builder);
                }

                using (new EditorGUI.DisabledScope(errors.Count > 0 || UIElementCodeGenerator.GetClickCallbackCount(builder) == 0))
                {
                    if (GUILayout.Button("生成点击回调模板")) UIClickCallbackTemplateWindow.Open(builder);
                }

                using (new EditorGUI.DisabledScope(builder.BindingCount == 0))
                {
                    if (GUILayout.Button("全部清除") && EditorUtility.DisplayDialog("清除 UI 绑定", "确定清除全部 UI 元素绑定吗？", "清除", "取消")) ClearBindings();
                }
            }

            EditorGUILayout.HelpBox("生成文件与面板脚本位于同一目录。面板类必须声明为 partial；BasePanel 会在自身 Awake 时自动调用生成的 OnBindUI()，不依赖 UIViewMgr。", MessageType.Info);
        }

        private static void GenerateBindingFile(UIElementBuilder builder)
        {
            try
            {
                string assetPath = UIElementCodeGenerator.GenerateBindingFile(builder);
                MonoScript generatedScript = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
                EditorGUIUtility.PingObject(generatedScript);
                Debug.Log($"已生成 UI 绑定文件：{assetPath}", generatedScript);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("生成 UI 绑定失败", exception.Message, "确定");
            }
        }

        private void DrawBinding(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty bindingProperty = bindingsProperty.GetArrayElementAtIndex(index);
            SerializedProperty fieldNameProperty = bindingProperty.FindPropertyRelative("fieldName");
            SerializedProperty targetProperty = bindingProperty.FindPropertyRelative("target");
            SerializedProperty clickProperty = bindingProperty.FindPropertyRelative("generateClickHandler");
            float lineHeight = EditorGUIUtility.singleLineHeight;

            rect.y += Spacing;
            Rect fieldRect = new(rect.x, rect.y, rect.width, lineHeight);
            EditorGUI.PropertyField(fieldRect, fieldNameProperty, new GUIContent("字段名"));

            Object currentTarget = targetProperty.objectReferenceValue;
            GameObject currentSource = GetSource(currentTarget);
            Rect sourceRect = new(rect.x, rect.y + lineHeight + Spacing, rect.width, lineHeight);
            GameObject newSource = (GameObject)EditorGUI.ObjectField(sourceRect, "层级对象", currentSource, typeof(GameObject), true);
            if (newSource != currentSource)
            {
                targetProperty.objectReferenceValue = newSource == null ? null : GetDefaultTarget(newSource);
                clickProperty.boolValue = targetProperty.objectReferenceValue is Button;
                if (newSource != null && string.IsNullOrWhiteSpace(fieldNameProperty.stringValue)) fieldNameProperty.stringValue = CSharpIdentifierUtility.Sanitize(newSource.name);
                currentTarget = targetProperty.objectReferenceValue;
                currentSource = newSource;
            }

            Rect targetRect = new(rect.x, rect.y + (lineHeight + Spacing) * 2f, rect.width * 0.72f, lineHeight);
            DrawTargetPopup(targetRect, currentSource, targetProperty);
            Rect clickRect = new(targetRect.xMax + Spacing, targetRect.y, rect.xMax - targetRect.xMax - Spacing, lineHeight);
            using (new EditorGUI.DisabledScope(targetProperty.objectReferenceValue is not Button))
            {
                if (targetProperty.objectReferenceValue is not Button) clickProperty.boolValue = false;
                clickProperty.boolValue = EditorGUI.ToggleLeft(clickRect, "生成点击方法", clickProperty.boolValue);
            }
        }

        private static void DrawTargetPopup(Rect rect, GameObject source, SerializedProperty targetProperty)
        {
            if (source == null)
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUI.Popup(rect, "绑定目标", 0, new[] { "请先选择层级对象" });
                return;
            }

            List<Object> candidates = GetCandidates(source);
            string[] labels = GetCandidateLabels(candidates);
            int selectedIndex = Mathf.Max(0, candidates.FindIndex(candidate => candidate == targetProperty.objectReferenceValue));
            int newIndex = EditorGUI.Popup(rect, "绑定目标", selectedIndex, labels);
            targetProperty.objectReferenceValue = candidates[newIndex];
        }

        private void AddBinding(ReorderableList list)
        {
            int index = bindingsProperty.arraySize;
            bindingsProperty.InsertArrayElementAtIndex(index);
            SerializedProperty bindingProperty = bindingsProperty.GetArrayElementAtIndex(index);
            bindingProperty.FindPropertyRelative("fieldName").stringValue = string.Empty;
            bindingProperty.FindPropertyRelative("target").objectReferenceValue = null;
            bindingProperty.FindPropertyRelative("generateClickHandler").boolValue = false;
            list.index = index;
        }

        private void ClearBindings()
        {
            serializedObject.Update();
            bindingsProperty.arraySize = 0;
            serializedObject.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications((UIElementBuilder)target);
        }

        private void DrawLegacyMigration()
        {
            int legacyCount = Mathf.Max(legacyGameObjectsProperty.arraySize, legacyNamesProperty.arraySize, legacyTypesProperty.arraySize, legacyAuthoritiesProperty.arraySize);
            if (legacyCount == 0) return;

            EditorGUILayout.HelpBox($"检测到 {legacyCount} 条 NGF 旧版并行列表数据。转换成功后会清空旧数据；成员访问权限统一改为 private。", MessageType.Warning);
            if (GUILayout.Button("转换旧版绑定数据")) ConvertLegacyBindings();
            EditorGUILayout.Space();
        }

        private void ConvertLegacyBindings()
        {
            List<LegacyBinding> convertedBindings = new();
            List<string> errors = new();
            if (legacyGameObjectsProperty.arraySize != legacyNamesProperty.arraySize || legacyGameObjectsProperty.arraySize != legacyTypesProperty.arraySize || legacyGameObjectsProperty.arraySize != legacyAuthoritiesProperty.arraySize)
            {
                EditorUtility.DisplayDialog("旧版数据无法转换", $"旧版并行列表长度不一致：GameObject={legacyGameObjectsProperty.arraySize}，字段名={legacyNamesProperty.arraySize}，类型={legacyTypesProperty.arraySize}，权限={legacyAuthoritiesProperty.arraySize}。", "确定");
                return;
            }

            UIElementBuilder builder = (UIElementBuilder)target;
            HashSet<string> fieldNames = new(StringComparer.Ordinal);
            for (int i = 0; i < builder.Bindings.Count; i++)
            {
                UIElementBinding binding = builder.Bindings[i];
                if (binding != null && !string.IsNullOrWhiteSpace(binding.FieldName)) fieldNames.Add(binding.FieldName);
            }

            for (int i = 0; i < legacyGameObjectsProperty.arraySize; i++)
            {
                GameObject source = (GameObject)legacyGameObjectsProperty.GetArrayElementAtIndex(i).objectReferenceValue;
                string fieldName = legacyNamesProperty.GetArrayElementAtIndex(i).stringValue;
                string typeName = legacyTypesProperty.GetArrayElementAtIndex(i).stringValue;
                if (source == null)
                {
                    errors.Add($"第 {i} 条旧绑定没有 GameObject。");
                    continue;
                }

                if (!source.transform.IsChildOf(builder.transform))
                {
                    errors.Add($"第 {i} 条旧绑定“{source.name}”不属于 {builder.name} 的层级。");
                    continue;
                }

                Object resolvedTarget = ResolveLegacyTarget(source, typeName);
                if (resolvedTarget == null)
                {
                    errors.Add($"第 {i} 条旧绑定无法在 {source.name} 上找到组件类型“{typeName}”。");
                    continue;
                }

                string resolvedFieldName = string.IsNullOrWhiteSpace(fieldName) ? CSharpIdentifierUtility.Sanitize(source.name) : CSharpIdentifierUtility.Sanitize(fieldName);
                if (!fieldNames.Add(resolvedFieldName))
                {
                    errors.Add($"第 {i} 条旧绑定转换后的字段名“{resolvedFieldName}”重复。");
                    continue;
                }

                convertedBindings.Add(new LegacyBinding(resolvedFieldName, resolvedTarget));
            }

            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("旧版数据无法转换", string.Join(Environment.NewLine, errors), "确定");
                return;
            }

            Undo.RecordObject(target, "Convert Legacy UI Bindings");
            for (int i = 0; i < convertedBindings.Count; i++)
            {
                int newIndex = bindingsProperty.arraySize;
                bindingsProperty.InsertArrayElementAtIndex(newIndex);
                SerializedProperty bindingProperty = bindingsProperty.GetArrayElementAtIndex(newIndex);
                bindingProperty.FindPropertyRelative("fieldName").stringValue = convertedBindings[i].FieldName;
                bindingProperty.FindPropertyRelative("target").objectReferenceValue = convertedBindings[i].Target;
                bindingProperty.FindPropertyRelative("generateClickHandler").boolValue = convertedBindings[i].Target is Button;
            }

            legacyGameObjectsProperty.arraySize = 0;
            legacyNamesProperty.arraySize = 0;
            legacyTypesProperty.arraySize = 0;
            legacyAuthoritiesProperty.arraySize = 0;
            serializedObject.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications((UIElementBuilder)target);
        }

        private static GameObject GetSource(Object target)
        {
            return target switch
            {
                GameObject gameObject => gameObject,
                Component component => component.gameObject,
                _ => null
            };
        }

        private static List<Object> GetCandidates(GameObject source)
        {
            List<Object> candidates = new() { source };
            Component[] components = source.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null) candidates.Add(components[i]);
            }

            return candidates;
        }

        private static string[] GetCandidateLabels(IReadOnlyList<Object> candidates)
        {
            string[] labels = new string[candidates.Count];
            Dictionary<string, int> typeCounts = new(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                string typeName = candidates[i] is GameObject ? nameof(GameObject) : candidates[i].GetType().Name;
                typeCounts.TryGetValue(typeName, out int count);
                count++;
                typeCounts[typeName] = count;
                labels[i] = count == 1 ? typeName : $"{typeName} #{count}";
            }

            return labels;
        }

        private static Object GetDefaultTarget(GameObject source)
        {
            string typeName = source.name.Length >= 3 ? source.name[^3..] switch
            {
                "Btn" => "Button",
                "Dpd" => "Dropdown",
                "Ipf" => "InputField",
                "Img" => "Image",
                "Rtf" => "RectTransform",
                "Rmg" => "RawImage",
                "Sdr" => "Slider",
                "Tmp" => "TMP_Text",
                "Tsf" => "Transform",
                "Tog" => "Toggle",
                "Txt" => "Text",
                "Hlg" => "BakedHorizontalLayout",
                "Vlg" => "BakedVerticalLayout",
                "Clg" => "BakedCircleLayout",
                "Flg" => "BakedFanLayout",
                "Hsv" => "LoopScrollViewHorizontal",
                "Vsv" => "LoopScrollViewVertical",
                _ => "GameObject"
            } : "GameObject";
            return ResolveLegacyTarget(source, typeName) ?? source;
        }

        private static Object ResolveLegacyTarget(GameObject source, string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName) || typeName == nameof(GameObject)) return source;
            Component[] components = source.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null) continue;
                for (Type type = component.GetType(); type != null && type != typeof(Object); type = type.BaseType)
                {
                    if (type.Name == typeName || type.FullName == typeName) return component;
                }
            }

            return null;
        }

        private readonly struct LegacyBinding
        {
            public LegacyBinding(string fieldName, Object target)
            {
                FieldName = fieldName;
                Target = target;
            }

            public string FieldName { get; }
            public Object Target { get; }
        }
    }

    public sealed class UIClickCallbackTemplateWindow : EditorWindow
    {
        private string generatedCode;
        private Vector2 scrollPosition;

        public static void Open(UIElementBuilder builder)
        {
            UIClickCallbackTemplateWindow window = GetWindow<UIClickCallbackTemplateWindow>("UI 点击回调模板");
            window.generatedCode = UIElementCodeGenerator.GenerateClickCallbackTemplate(builder);
            window.minSize = new Vector2(520f, 360f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("复制以下代码并粘贴到面板业务 partial class 内部，然后填写点击逻辑。", MessageType.Info);
            if (GUILayout.Button("复制全部回调")) EditorGUIUtility.systemCopyBuffer = generatedCode;
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            generatedCode = EditorGUILayout.TextArea(generatedCode, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
    }

}
