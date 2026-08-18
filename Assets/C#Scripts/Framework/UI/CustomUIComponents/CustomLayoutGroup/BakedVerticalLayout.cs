using System.Collections.Generic;
using UnityEngine;

namespace GameFramework.UI.Layout
{
    /// <summary>
    /// 将直接子节点按竖直方向烘焙排列，只修改子节点位置。
    /// </summary>
    public sealed class BakedVerticalLayout : BakedLinearLayout
    {
        protected override void Arrange(IReadOnlyList<RectTransform> layoutChildren)
        {
            if (layoutChildren.Count == 0) return;

            float totalHeight = GetTotalSpacing(layoutChildren.Count);
            for (int i = 0; i < layoutChildren.Count; i++) totalHeight += layoutChildren[i].rect.height;

            Rect layoutRect = LayoutRectTransform.rect;
            float contentLeft = layoutRect.xMin + Padding.left;
            float contentRight = layoutRect.xMax - Padding.right;
            float contentBottom = layoutRect.yMin + Padding.bottom;
            float contentTop = layoutRect.yMax - Padding.top;
            float horizontalAlignment = GetHorizontalAlignment(ChildAlignment);
            float verticalAlignment = GetVerticalAlignment(ChildAlignment);
            float groupBottom = contentBottom + (contentTop - contentBottom - totalHeight) * verticalAlignment;
            float cursor = groupBottom + totalHeight;

            for (int i = 0; i < layoutChildren.Count; i++)
            {
                RectTransform child = layoutChildren[i];
                float childWidth = child.rect.width;
                float childHeight = child.rect.height;
                float childLeft = contentLeft + (contentRight - contentLeft - childWidth) * horizontalAlignment;
                float x = childLeft + childWidth * child.pivot.x;
                float y = cursor - childHeight * (1f - child.pivot.y);
                SetChildLocalPosition(child, new Vector2(x, y));
                cursor -= childHeight;
                if (i < layoutChildren.Count - 1) cursor -= GetSpacingAfter(i);
            }
        }
    }
}
