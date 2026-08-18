using UnityEngine;
using UnityEngine.UI;

namespace GameFramework.UI.Virtualization
{
    public abstract class LoopScrollViewHorizontal<TItem, TData> : LoopScrollViewBase<TItem, TData> where TItem : LoopItemBase where TData : LoopItemDataBase
    {
        #region 内部实现

        protected override float MainAxisStep => ItemSize.x + Spacing.x;
        protected override float MainAxisPaddingStart => Padding.left;

        protected override void ConfigureScrollRect(ScrollRect targetScrollRect)
        {
            targetScrollRect.horizontal = true;
            targetScrollRect.vertical = false;
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

            float availableHeight = Mathf.Max(0f, viewportHeight - Padding.top - Padding.bottom);
            int rowCount = Mathf.Max(1, Mathf.FloorToInt((availableHeight + Spacing.y) / (ItemSize.y + Spacing.y)));
            int columnCount = Mathf.CeilToInt((float)ItemCount / rowCount);
            float itemsWidth = columnCount * ItemSize.x + Mathf.Max(0, columnCount - 1) * Spacing.x;
            float itemsHeight = rowCount * ItemSize.y + Mathf.Max(0, rowCount - 1) * Spacing.y;
            float contentWidth = Mathf.Max(viewportWidth, Padding.left + Padding.right + itemsWidth);
            float contentHeight = Mathf.Max(viewportHeight, Padding.top + Padding.bottom + itemsHeight);
            SetLayoutMetrics(rowCount, columnCount, new Vector2(contentWidth, contentHeight));
        }

        protected override Vector2 CalculateItemPosition(int index, RectTransform item)
        {
            int column = index / CrossAxisCount;
            int row = index % CrossAxisCount;
            float x = Padding.left + column * (ItemSize.x + Spacing.x) + ItemSize.x * item.pivot.x;
            float y = -Padding.top - row * (ItemSize.y + Spacing.y) - ItemSize.y * (1f - item.pivot.y);
            return new Vector2(x, y);
        }

        protected override float GetScrollOffset()
        {
            return -Content.anchoredPosition.x;
        }

        protected override void SetScrollOffset(float offset)
        {
            float maxOffset = Mathf.Max(0f, Content.rect.width - Viewport.rect.width);
            Content.anchoredPosition = new Vector2(-Mathf.Clamp(offset, 0f, maxOffset), 0f);
        }

        protected override float GetViewportMainAxisSize()
        {
            return Viewport.rect.width;
        }

        #endregion
    }

    public sealed class LoopScrollViewHorizontal : LoopScrollViewHorizontal<LoopItemBase, LoopItemDataBase>
    {
    }
}
