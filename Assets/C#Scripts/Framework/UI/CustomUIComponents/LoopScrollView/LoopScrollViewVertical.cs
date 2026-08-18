using UnityEngine;
using UnityEngine.UI;

namespace GameFramework.UI.Virtualization
{
    public abstract class LoopScrollViewVertical<TItem, TData> : LoopScrollViewBase<TItem, TData> where TItem : LoopItemBase where TData : LoopItemDataBase
    {
        #region 内部实现

        protected override float MainAxisStep => ItemSize.y + Spacing.y;
        protected override float MainAxisPaddingStart => Padding.top;

        protected override void ConfigureScrollRect(ScrollRect targetScrollRect)
        {
            targetScrollRect.horizontal = false;
            targetScrollRect.vertical = true;
        }

        protected override void CalculateLayout()
        {
            float viewportWidth = Viewport.rect.width;
            float viewportHeight = Viewport.rect.height;
            if (ItemCount == 0)
            {
                SetLayoutMetrics(1, 0, new Vector2(viewportWidth, viewportHeight));
                return;
            }

            float availableWidth = Mathf.Max(0f, viewportWidth - Padding.left - Padding.right);
            int columnCount = Mathf.Max(1, Mathf.FloorToInt((availableWidth + Spacing.x) / (ItemSize.x + Spacing.x)));
            int rowCount = Mathf.CeilToInt((float)ItemCount / columnCount);
            float itemsWidth = columnCount * ItemSize.x + Mathf.Max(0, columnCount - 1) * Spacing.x;
            float itemsHeight = rowCount * ItemSize.y + Mathf.Max(0, rowCount - 1) * Spacing.y;
            float contentWidth = Mathf.Max(viewportWidth, Padding.left + Padding.right + itemsWidth);
            float contentHeight = Mathf.Max(viewportHeight, Padding.top + Padding.bottom + itemsHeight);
            SetLayoutMetrics(columnCount, rowCount, new Vector2(contentWidth, contentHeight));
        }

        protected override Vector2 CalculateItemPosition(int index, RectTransform item)
        {
            int row = index / CrossAxisCount;
            int column = index % CrossAxisCount;
            float x = Padding.left + column * (ItemSize.x + Spacing.x) + ItemSize.x * item.pivot.x;
            float y = -Padding.top - row * (ItemSize.y + Spacing.y) - ItemSize.y * (1f - item.pivot.y);
            return new Vector2(x, y);
        }

        protected override float GetScrollOffset()
        {
            return Content.anchoredPosition.y;
        }

        protected override void SetScrollOffset(float offset)
        {
            float maxOffset = Mathf.Max(0f, Content.rect.height - Viewport.rect.height);
            Content.anchoredPosition = new Vector2(0f, Mathf.Clamp(offset, 0f, maxOffset));
        }

        protected override float GetViewportMainAxisSize()
        {
            return Viewport.rect.height;
        }

        #endregion
    }

    public sealed class LoopScrollViewVertical : LoopScrollViewVertical<LoopItemBase, LoopItemDataBase>
    {
    }
}
