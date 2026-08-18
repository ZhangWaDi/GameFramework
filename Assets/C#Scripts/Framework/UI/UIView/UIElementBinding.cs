using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameFramework.UI
{
    /// <summary>
    /// 描述一个可由 <see cref="UIElementBuilder"/> 查询的 UI 对象绑定。
    /// </summary>
    [Serializable]
    public sealed class UIElementBinding
    {
        [SerializeField] private string fieldName;
        [SerializeField] private Object target;
        [SerializeField] private bool generateClickHandler;

        public string FieldName => fieldName;
        public Object Target => target;
        public bool GenerateClickHandler => generateClickHandler;
    }
}
