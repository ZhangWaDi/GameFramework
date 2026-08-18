using System;
using System.Collections.Generic;
using GameFramework.PoolSystem;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework.UI.Virtualization
{
    /// <summary>
    /// 固定 Item 尺寸的单主轴循环滚动列表基类。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ScrollRect))]
    public abstract class LoopScrollViewBase<TItem, TData> : MonoBehaviour, ICanvasElement where TItem : LoopItemBase where TData : LoopItemDataBase
    {
        #region 外部调用

        public IReadOnlyList<TData> DataList => dataList;
        public IReadOnlyDictionary<int, TItem> VisibleItems => visibleItems;

        /// <summary>
        /// 替换数据列表并刷新当前可见 Item。
        /// </summary>
        public void RefreshDataList(IReadOnlyList<TData> items, bool resetPosition = false)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items), "循环滚动列表数据不能为空。");
            }
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null)
                {
                    throw new ArgumentException($"循环滚动列表第 {i} 条数据为空。", nameof(items));
                }
            }

            EnsureInitialized();
            RegisterScrollListener();
            RecycleAllVisibleItems();
            if (!ReferenceEquals(items, dataList))
            {
                dataList.Clear();
                for (int i = 0; i < items.Count; i++)
                {
                    dataList.Add(items[i]);
                }
            }
            for (int i = 0; i < dataList.Count; i++)
            {
                dataList[i].Index = i;
            }

            RecalculateLayout(resetPosition);
            RequestPostLayoutRefresh();
        }

        /// <summary>
        /// 刷新全部可见 Item，不重新创建对象。
        /// </summary>
        public void RefreshVisibleItems()
        {
            EnsureInitialized();
            if (layoutDirty)
            {
                RegisterLayoutRefresh();
                return;
            }
            UpdateVisibleItems(true);
        }

        /// <summary>
        /// 刷新指定索引的 Item；Item 不可见时返回 false。
        /// </summary>
        public bool RefreshItem(int index)
        {
            ValidateDataIndex(index);
            if (!visibleItems.TryGetValue(index, out TItem item))
            {
                return false;
            }
            item.RefreshByData(dataList[index]);
            return true;
        }

        /// <summary>
        /// 将指定数据项所在行或列滚动到视口主轴起点。
        /// </summary>
        public void ScrollToIndex(int index)
        {
            ValidateDataIndex(index);
            EnsureInitialized();
            int lineIndex = index / crossAxisCount;
            rebuilding = true;
            try
            {
                SetScrollOffset(MainAxisPaddingStart + lineIndex * MainAxisStep);
            }
            finally
            {
                rebuilding = false;
            }
            InvalidateVisibleRange();
            if (layoutDirty)
            {
                RegisterLayoutRefresh();
                return;
            }
            UpdateVisibleItems(false);
        }

        /// <summary>
        /// 尝试获取指定索引的可见 Item。
        /// </summary>
        public bool TryGetVisibleItem(int index, out TItem item)
        {
            return visibleItems.TryGetValue(index, out item);
        }

        /// <summary>
        /// 清除数据列表并刷新当前可见 Item。
        /// </summary>
        public void ClearDataList(bool resetPosition = false)
        {
            RefreshDataList(Array.Empty<TData>(), resetPosition);
        }

        #endregion

        #region 内部实现

        [SerializeField] private GameObject itemPrefab;
        [SerializeField] private RectOffset padding = new();
        [SerializeField] private Vector2 spacing;
        [SerializeField, Min(0)] private int bufferLines = 1;

        private readonly List<TData> dataList = new();
        private readonly Dictionary<int, TItem> visibleItems = new();
        private readonly Dictionary<GameObject, TItem> itemComponents = new(ReferenceEqualityComparer<GameObject>.Instance);
        private readonly Dictionary<GameObject, RectTransform> itemRectTransforms = new(ReferenceEqualityComparer<GameObject>.Instance);
        private readonly List<int> recycleIndices = new();
        private GameObjPool itemPool;
        private ScrollRect scrollRect;
        private RectTransform viewport;
        private RectTransform content;
        private Vector2 itemSize;
        private int crossAxisCount = 1;
        private int mainLineCount;
        private int previousFirstLine = -1;
        private int previousLastLineExclusive = -1;
        private bool initialized;
        private bool listenerRegistered;
        private bool rebuilding;
        private bool layoutDirty;
        private bool registerLayoutOnNextFrame;
        private bool hasLayoutSnapshot;
        private Vector2 previousLayoutViewportSize;
        private Vector2 previousLayoutContentSize;
        private Vector2 observedViewportSize;

        private const float LayoutSizeTolerance = 0.01f;

        protected RectOffset Padding => padding ??= new();
        protected Vector2 Spacing => spacing;
        protected Vector2 ItemSize => itemSize;
        protected RectTransform Viewport => viewport;
        protected RectTransform Content => content;
        protected int ItemCount => dataList.Count;
        protected int CrossAxisCount => crossAxisCount;
        protected abstract float MainAxisStep { get; }
        protected abstract float MainAxisPaddingStart { get; }

        protected abstract void ConfigureScrollRect(ScrollRect targetScrollRect);
        protected abstract void CalculateLayout();
        protected abstract Vector2 CalculateItemPosition(int index, RectTransform item);
        protected abstract float GetScrollOffset();
        protected abstract void SetScrollOffset(float offset);
        protected abstract float GetViewportMainAxisSize();

        protected void SetLayoutMetrics(int targetCrossAxisCount, int targetMainLineCount, Vector2 contentSize)
        {
            crossAxisCount = Mathf.Max(1, targetCrossAxisCount);
            mainLineCount = Mathf.Max(0, targetMainLineCount);
            content.sizeDelta = contentSize;
        }

        private void Awake()
        {
            TryInitialize();
        }

        private void OnEnable()
        {
            if (!TryInitialize())
            {
                return;
            }
            RegisterScrollListener();
            RequestPostLayoutRefresh();
        }

        private void OnDisable()
        {
            RemoveScrollListener();
            CanvasUpdateRegistry.UnRegisterCanvasElementForRebuild(this);
            registerLayoutOnNextFrame = false;
        }

        private void OnDestroy()
        {
            RemoveScrollListener();
            CanvasUpdateRegistry.UnRegisterCanvasElementForRebuild(this);
            itemPool?.Dispose();
            visibleItems.Clear();
            itemComponents.Clear();
            itemRectTransforms.Clear();
        }

        private void LateUpdate()
        {
            if (!initialized)
            {
                return;
            }
            if (registerLayoutOnNextFrame)
            {
                registerLayoutOnNextFrame = false;
                RegisterLayoutRefresh();
                return;
            }
            if (!layoutDirty && !AreLayoutSizesEqual(observedViewportSize, viewport.rect.size))
            {
                RequestPostLayoutRefresh();
            }
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!initialized || rebuilding)
            {
                return;
            }
            RequestPostLayoutRefresh();
        }

        public void Rebuild(CanvasUpdate executing)
        {
            if (executing != CanvasUpdate.PostLayout || !layoutDirty || !initialized || !isActiveAndEnabled)
            {
                return;
            }

            Vector2 currentViewportSize = viewport.rect.size;
            RecalculateLayout(false);
            UpdateVisibleItems(true);
            Vector2 currentContentSize = content.rect.size;
            observedViewportSize = currentViewportSize;

            bool layoutStable = hasLayoutSnapshot && AreLayoutSizesEqual(previousLayoutViewportSize, currentViewportSize) && AreLayoutSizesEqual(previousLayoutContentSize, currentContentSize);
            previousLayoutViewportSize = currentViewportSize;
            previousLayoutContentSize = currentContentSize;
            hasLayoutSnapshot = true;
            if (layoutStable)
            {
                layoutDirty = false;
                hasLayoutSnapshot = false;
                return;
            }

            registerLayoutOnNextFrame = true;
        }

        public void LayoutComplete()
        {
        }

        public void GraphicUpdateComplete()
        {
        }

        public bool IsDestroyed()
        {
            return this == null;
        }

        private bool TryInitialize()
        {
            if (initialized)
            {
                return true;
            }

            scrollRect = GetComponent<ScrollRect>();
            if (scrollRect == null)
            {
                return LogInitializationError("缺少 ScrollRect 组件。");
            }
            if (itemPrefab == null)
            {
                return LogInitializationError("未设置 Item 预制体。");
            }
            if (scrollRect.content == null)
            {
                return LogInitializationError("ScrollRect 未设置 Content。");
            }
            if (itemPrefab.transform is not RectTransform prefabRect)
            {
                return LogInitializationError("Item 预制体根节点必须包含 RectTransform。");
            }
            if (itemPrefab.GetComponent<TItem>() == null)
            {
                return LogInitializationError($"Item 预制体根节点必须包含 {typeof(TItem).Name} 组件。");
            }

            itemSize = prefabRect.rect.size;
            if (itemSize.x <= 0f || itemSize.y <= 0f)
            {
                return LogInitializationError("Item 预制体的宽高必须大于零。");
            }
            if (itemSize.x + spacing.x <= 0f || itemSize.y + spacing.y <= 0f)
            {
                return LogInitializationError("Item 尺寸与间距之和必须大于零。");
            }

            content = scrollRect.content;
            viewport = scrollRect.viewport != null ? scrollRect.viewport : scrollRect.transform as RectTransform;
            if (viewport == null)
            {
                return LogInitializationError("无法获取 ScrollRect 的 Viewport。");
            }

            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            ConfigureScrollRect(scrollRect);
            itemPool = new GameObjPool(itemPrefab);
            initialized = true;
            RecalculateLayout(true);
            observedViewportSize = viewport.rect.size;
            return true;
        }

        private void EnsureInitialized()
        {
            if (!TryInitialize())
            {
                throw new InvalidOperationException("循环滚动列表初始化失败，请检查组件配置。");
            }
        }

        private bool LogInitializationError(string message)
        {
            Debug.LogError($"{GetType().Name} 初始化失败：{message}", this);
            return false;
        }

        private void RegisterScrollListener()
        {
            if (listenerRegistered)
            {
                return;
            }
            scrollRect.onValueChanged.AddListener(OnScrollValueChanged);
            listenerRegistered = true;
        }

        private void RemoveScrollListener()
        {
            if (!listenerRegistered || scrollRect == null)
            {
                return;
            }
            scrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);
            listenerRegistered = false;
        }

        private void RecalculateLayout(bool resetPosition)
        {
            rebuilding = true;
            try
            {
                float currentOffset = resetPosition ? 0f : GetScrollOffset();
                CalculateLayout();
                SetScrollOffset(currentOffset);
                InvalidateVisibleRange();
            }
            finally
            {
                rebuilding = false;
            }
        }

        private void OnScrollValueChanged(Vector2 _)
        {
            if (rebuilding || layoutDirty)
            {
                return;
            }
            UpdateVisibleItems(false);
        }

        private void RequestPostLayoutRefresh()
        {
            layoutDirty = true;
            hasLayoutSnapshot = false;
            RegisterLayoutRefresh();
        }

        private void RegisterLayoutRefresh()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }
            if (CanvasUpdateRegistry.IsRebuildingLayout())
            {
                registerLayoutOnNextFrame = true;
                return;
            }

            RectTransform scrollRectTransform = scrollRect.transform as RectTransform;
            LayoutRebuilder.MarkLayoutForRebuild(scrollRectTransform);
            CanvasUpdateRegistry.TryRegisterCanvasElementForLayoutRebuild(this);
        }

        private void UpdateVisibleItems(bool forceRefresh)
        {
            if (!initialized)
            {
                return;
            }
            if (dataList.Count == 0 || mainLineCount == 0)
            {
                RecycleAllVisibleItems();
                InvalidateVisibleRange();
                return;
            }

            float scrollOffset = Mathf.Max(0f, GetScrollOffset());
            float firstOffset = scrollOffset - MainAxisPaddingStart;
            float lastOffset = scrollOffset + GetViewportMainAxisSize() - MainAxisPaddingStart;
            int firstLine = Mathf.FloorToInt(firstOffset / MainAxisStep) - bufferLines;
            int lastLineExclusive = Mathf.CeilToInt(lastOffset / MainAxisStep) + bufferLines;
            firstLine = Mathf.Clamp(firstLine, 0, mainLineCount);
            lastLineExclusive = Mathf.Clamp(lastLineExclusive, firstLine, mainLineCount);
            if (!forceRefresh && firstLine == previousFirstLine && lastLineExclusive == previousLastLineExclusive)
            {
                return;
            }

            previousFirstLine = firstLine;
            previousLastLineExclusive = lastLineExclusive;
            int firstIndex = firstLine * crossAxisCount;
            int endIndexExclusive = Mathf.Min(dataList.Count, lastLineExclusive * crossAxisCount);
            RecycleItemsOutsideRange(firstIndex, endIndexExclusive);
            for (int index = firstIndex; index < endIndexExclusive; index++)
            {
                ShowItem(index, forceRefresh);
            }
        }

        private void ShowItem(int index, bool forceRefresh)
        {
            if (visibleItems.TryGetValue(index, out TItem item))
            {
                RectTransform visibleItemRect = itemRectTransforms[item.gameObject];
                visibleItemRect.anchoredPosition = CalculateItemPosition(index, visibleItemRect);
                if (forceRefresh)
                {
                    item.RefreshByData(dataList[index]);
                }
                return;
            }

            GameObject itemObject = itemPool.Get();
            if (itemObject.transform.parent != content)
            {
                itemObject.transform.SetParent(content, false);
            }
            if (!itemComponents.TryGetValue(itemObject, out item) || item == null)
            {
                item = itemObject.GetComponent<TItem>();
                if (item == null)
                {
                    itemPool.Release(itemObject);
                    throw new InvalidOperationException($"对象池创建的 Item 根节点缺少 {typeof(TItem).Name} 组件。");
                }
                itemComponents[itemObject] = item;
            }
            if (!itemRectTransforms.TryGetValue(itemObject, out RectTransform itemRect) || itemRect == null)
            {
                itemRect = itemObject.transform as RectTransform;
                if (itemRect == null)
                {
                    itemPool.Release(itemObject);
                    throw new InvalidOperationException("对象池创建的 Item 根节点缺少 RectTransform 组件。");
                }
                itemRect.anchorMin = new Vector2(0f, 1f);
                itemRect.anchorMax = new Vector2(0f, 1f);
                itemRectTransforms[itemObject] = itemRect;
            }

            itemRect.localScale = Vector3.one;
            itemRect.localRotation = Quaternion.identity;
            itemRect.anchoredPosition = CalculateItemPosition(index, itemRect);
            visibleItems.Add(index, item);
            item.RefreshByData(dataList[index]);
        }

        private void RecycleItemsOutsideRange(int firstIndex, int endIndexExclusive)
        {
            recycleIndices.Clear();
            foreach (KeyValuePair<int, TItem> pair in visibleItems)
            {
                if (pair.Key < firstIndex || pair.Key >= endIndexExclusive)
                {
                    recycleIndices.Add(pair.Key);
                }
            }
            for (int i = 0; i < recycleIndices.Count; i++)
            {
                RecycleVisibleItem(recycleIndices[i]);
            }
        }

        private void RecycleAllVisibleItems()
        {
            foreach (TItem item in visibleItems.Values)
            {
                if (item == null)
                {
                    continue;
                }
                itemPool.Release(item.gameObject);
            }
            visibleItems.Clear();
        }

        private void RecycleVisibleItem(int index)
        {
            TItem item = visibleItems[index];
            visibleItems.Remove(index);
            if (item == null)
            {
                return;
            }
            itemPool.Release(item.gameObject);
        }

        private void ValidateDataIndex(int index)
        {
            if (index < 0 || index >= dataList.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"数据索引必须在 0 到 {dataList.Count - 1} 之间。");
            }
        }

        private void InvalidateVisibleRange()
        {
            previousFirstLine = -1;
            previousLastLineExclusive = -1;
        }

        private static bool AreLayoutSizesEqual(Vector2 left, Vector2 right)
        {
            return Mathf.Abs(left.x - right.x) <= LayoutSizeTolerance && Mathf.Abs(left.y - right.y) <= LayoutSizeTolerance;
        }

        #endregion
    }
}
