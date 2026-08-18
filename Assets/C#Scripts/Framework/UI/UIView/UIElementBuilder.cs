using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameFramework.UI
{
    /// <summary>
    /// 保存 UI 字段名到具体 GameObject 或 Component 的稳定绑定关系。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIElementBuilder : MonoBehaviour
    {
        [SerializeField] private List<UIElementBinding> bindings = new();

        // 用于把 NGF 旧预制体的四个并行列表一次性转换为新 Binding 数据。
        [SerializeField, HideInInspector] private List<GameObject> gameObjectList = new();
        [SerializeField, HideInInspector] private List<string> itemNameList = new();
        [SerializeField, HideInInspector] private List<string> itemTypeList = new();
        [SerializeField, HideInInspector] private List<int> itemAuthorityTypeList = new();

        private Dictionary<string, Object> bindingLookup;

        public IReadOnlyList<UIElementBinding> Bindings => bindings;
        public int BindingCount => bindings.Count;

        /// <summary>
        /// 获取指定字段名和类型的绑定对象；绑定缺失或类型不匹配时抛出明确异常。
        /// </summary>
        public T Get<T>(string fieldName) where T : Object
        {
            if (string.IsNullOrWhiteSpace(fieldName)) throw new ArgumentException("UI 绑定字段名不能为空。", nameof(fieldName));

            EnsureBindingLookup();
            if (!bindingLookup.TryGetValue(fieldName, out Object target)) throw new KeyNotFoundException($"{name} 的 UIElementBuilder 中不存在字段“{fieldName}”的绑定。");
            if (target == null) throw new MissingReferenceException($"{name} 的 UI 绑定“{fieldName}”引用的对象已经失效。");
            if (target is T typedTarget) return typedTarget;
            throw new InvalidCastException($"{name} 的 UI 绑定“{fieldName}”实际类型为“{target.GetType().FullName}”，不能作为“{typeof(T).FullName}”返回。");
        }

        /// <summary>
        /// 尝试获取指定字段名和类型的绑定对象。
        /// </summary>
        public bool TryGet<T>(string fieldName, out T target) where T : Object
        {
            target = null;
            if (string.IsNullOrWhiteSpace(fieldName)) return false;

            EnsureBindingLookup();
            if (!bindingLookup.TryGetValue(fieldName, out Object boundTarget) || boundTarget == null || boundTarget is not T typedTarget) return false;
            target = typedTarget;
            return true;
        }

        private void OnValidate()
        {
            bindingLookup = null;
        }

        private void EnsureBindingLookup()
        {
            if (bindingLookup != null) return;

            Dictionary<string, Object> lookup = new(StringComparer.Ordinal);
            for (int i = 0; i < bindings.Count; i++)
            {
                UIElementBinding binding = bindings[i];
                if (binding == null) throw new InvalidOperationException($"{name} 的 UIElementBuilder 第 {i} 条绑定为空。");
                if (string.IsNullOrWhiteSpace(binding.FieldName)) throw new InvalidOperationException($"{name} 的 UIElementBuilder 第 {i} 条绑定没有字段名。");
                if (binding.Target == null) throw new MissingReferenceException($"{name} 的 UI 绑定“{binding.FieldName}”没有有效目标对象。");
                if (!lookup.TryAdd(binding.FieldName, binding.Target)) throw new InvalidOperationException($"{name} 的 UIElementBuilder 存在重复字段名“{binding.FieldName}”。");
            }

            bindingLookup = lookup;
        }
    }
}
