using System.Collections.Generic;
using UnityEngine;

namespace GameFramework.UI.Layout
{
    /// <summary>
    /// 将直接子节点均匀烘焙到圆周上，零度位于圆形顶部。
    /// </summary>
    public sealed class BakedCircleLayout : BakedRadialLayout
    {
        protected override void Arrange(IReadOnlyList<RectTransform> layoutChildren)
        {
            if (layoutChildren.Count == 0) return;
            float spacingAngle = 360f / layoutChildren.Count;
            for (int i = 0; i < layoutChildren.Count; i++) PlaceChild(layoutChildren[i], i * spacingAngle);
        }
    }
}
