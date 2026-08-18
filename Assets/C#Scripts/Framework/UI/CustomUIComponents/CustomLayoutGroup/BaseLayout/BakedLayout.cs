using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework.UI.Layout
{
    /// <summary>
    /// 仅在显式调用时计算并写入子节点布局，不参与 uGUI 的自动布局重建。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public abstract class BakedLayout : MonoBehaviour
    {
        [SerializeField] private bool includeInactiveChildren;
        [SerializeField] private bool respectLayoutIgnorer = true;

        private readonly List<RectTransform> children = new();
        private RectTransform layoutRectTransform;

        protected RectTransform LayoutRectTransform
        {
            get
            {
                if (layoutRectTransform == null) layoutRectTransform = transform as RectTransform;
                return layoutRectTransform;
            }
        }

        /// <summary>
        /// 重新计算布局并将结果一次性写入直接子节点。
        /// </summary>
        [ContextMenu("Bake Layout")]
        public void ApplyLayout()
        {
            RebuildChildren();
            Arrange(children);
        }

        /// <summary>
        /// 为旧调用方保留的显式刷新入口。
        /// </summary>
        public void RefreshLayout()
        {
            ApplyLayout();
        }

        protected abstract void Arrange(IReadOnlyList<RectTransform> layoutChildren);

        protected static void SetChildLocalPosition(RectTransform child, Vector2 position)
        {
            Vector3 currentPosition = child.localPosition;
            Vector3 targetPosition = new(position.x, position.y, currentPosition.z);
            if ((currentPosition - targetPosition).sqrMagnitude <= 0.000001f) return;
            child.localPosition = targetPosition;
        }

        protected static void SetChildLocalRotation(RectTransform child, float angle)
        {
            if (Mathf.Abs(Mathf.DeltaAngle(child.localEulerAngles.z, angle)) <= 0.001f) return;
            child.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        protected static void SetChildFacingPoint(RectTransform child, Vector2 childPosition, Vector2 targetPoint, float rotationOffset)
        {
            Vector2 direction = targetPoint - childPosition;
            if (direction.sqrMagnitude <= 0.000001f) return;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f + rotationOffset;
            SetChildLocalRotation(child, angle);
        }

        private void RebuildChildren()
        {
            children.Clear();
            Transform layoutTransform = transform;
            for (int i = 0; i < layoutTransform.childCount; i++)
            {
                if (layoutTransform.GetChild(i) is not RectTransform child) continue;
                if (!includeInactiveChildren && !child.gameObject.activeSelf) continue;
                if (respectLayoutIgnorer)
                {
                    ILayoutIgnorer layoutIgnorer = child.GetComponent<ILayoutIgnorer>();
                    if (layoutIgnorer != null && layoutIgnorer.ignoreLayout) continue;
                }
                children.Add(child);
            }
        }
    }
}
