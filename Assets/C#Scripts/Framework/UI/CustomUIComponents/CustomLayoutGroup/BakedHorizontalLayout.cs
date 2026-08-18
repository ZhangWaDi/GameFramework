using System.Collections.Generic;
using UnityEngine;

namespace GameFramework.UI.Layout
{
    /// <summary>
    /// 将直接子节点按水平方向烘焙排列，只修改子节点位置。
    /// </summary>
    public sealed class BakedHorizontalLayout : BakedLinearLayout
    {
        protected override void Arrange(IReadOnlyList<RectTransform> layoutChildren)
        {
            if (layoutChildren.Count == 0) return;

            float totalWidth = GetTotalSpacing(layoutChildren.Count);
            for (int i = 0; i < layoutChildren.Count; i++) totalWidth += layoutChildren[i].rect.width;

            Rect layoutRect = LayoutRectTransform.rect;
            float contentLeft = layoutRect.xMin + Padding.left;
            float contentRight = layoutRect.xMax - Padding.right;
            float contentBottom = layoutRect.yMin + Padding.bottom;
            float contentTop = layoutRect.yMax - Padding.top;
            float horizontalAlignment = GetHorizontalAlignment(ChildAlignment);
            float verticalAlignment = GetVerticalAlignment(ChildAlignment);
            float cursor = contentLeft + (contentRight - contentLeft - totalWidth) * horizontalAlignment;

            for (int i = 0; i < layoutChildren.Count; i++)
            {
                RectTransform child = layoutChildren[i];
                float childWidth = child.rect.width;
                float childHeight = child.rect.height;
                float x = cursor + childWidth * child.pivot.x;
                float childBottom = contentBottom + (contentTop - contentBottom - childHeight) * verticalAlignment;
                float y = childBottom + childHeight * child.pivot.y;
                SetChildLocalPosition(child, new Vector2(x, y));
                cursor += childWidth;
                if (i < layoutChildren.Count - 1) cursor += GetSpacingAfter(i);
            }
        }
    }
}
