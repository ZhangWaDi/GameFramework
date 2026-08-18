using System;
using System.Collections.Generic;
using System.Reflection;
using GameFramework.UI.Virtualization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class LoopScrollViewTests
{
    private readonly List<GameObject> createdObjects = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null)
            {
                Object.DestroyImmediate(createdObjects[i]);
            }
        }
        createdObjects.Clear();
    }

    [Test]
    public void VerticalList_RecyclesItemsOutsideViewport()
    {
        TestVerticalLoopScrollView view = CreateView<TestVerticalLoopScrollView>(out RectTransform content);
        List<VirtualLoopItemData> dataList = CreateDataList(100);

        view.RefreshDataList(dataList);
        CompletePostLayout(view);

        Assert.AreEqual(250f, content.sizeDelta.x, 0.001f);
        Assert.AreEqual(5000f, content.sizeDelta.y, 0.001f);
        Assert.AreEqual(6, view.VisibleItems.Count);
        Assert.AreEqual(6, content.childCount);
        Assert.IsTrue(view.TryGetVisibleItem(0, out _));

        view.ScrollToIndex(20);

        Assert.AreEqual(1000f, content.anchoredPosition.y, 0.001f);
        Assert.AreEqual(6, view.VisibleItems.Count);
        Assert.AreEqual(6, content.childCount);
        Assert.IsFalse(view.TryGetVisibleItem(0, out _));
        Assert.IsTrue(view.TryGetVisibleItem(20, out VirtualLoopItem item));
        Assert.AreEqual(20, item.Data.Index);
    }

    [Test]
    public void HorizontalList_RecyclesItemsOutsideViewport()
    {
        TestHorizontalLoopScrollView view = CreateView<TestHorizontalLoopScrollView>(out RectTransform content);
        List<VirtualLoopItemData> dataList = CreateDataList(100);

        view.RefreshDataList(dataList);
        view.ScrollToIndex(20);
        CompletePostLayout(view);

        Assert.AreEqual(5000f, content.sizeDelta.x, 0.001f);
        Assert.AreEqual(250f, content.sizeDelta.y, 0.001f);
        Assert.AreEqual(-1000f, content.anchoredPosition.x, 0.001f);
        Assert.AreEqual(6, view.VisibleItems.Count);
        Assert.AreEqual(6, content.childCount);
        Assert.IsFalse(view.TryGetVisibleItem(0, out _));
        Assert.IsTrue(view.TryGetVisibleItem(20, out VirtualLoopItem item));
        Assert.AreEqual(20, item.Data.Index);
    }

    [Test]
    public void RefreshDataList_CopiesDataAndAssignsIndices()
    {
        TestVerticalLoopScrollView view = CreateView<TestVerticalLoopScrollView>(out _);
        List<VirtualLoopItemData> source = CreateDataList(20);

        view.RefreshDataList(source);
        source.Clear();

        Assert.AreEqual(20, view.DataList.Count);
        for (int i = 0; i < view.DataList.Count; i++)
        {
            Assert.AreEqual(i, view.DataList[i].Index);
        }
    }

    [Test]
    public void RefreshItem_OnlyRefreshesVisibleTarget()
    {
        TestVerticalLoopScrollView view = CreateView<TestVerticalLoopScrollView>(out _);
        view.RefreshDataList(CreateDataList(20));
        CompletePostLayout(view);
        Assert.IsTrue(view.TryGetVisibleItem(0, out VirtualLoopItem item));
        int previousRefreshCount = item.RefreshCount;

        bool refreshed = view.RefreshItem(0);

        Assert.IsTrue(refreshed);
        Assert.AreEqual(previousRefreshCount + 1, item.RefreshCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.RefreshItem(20));
    }

    [Test]
    public void ClearDataList_RecyclesAllVisibleItemsAndResetsContent()
    {
        TestVerticalLoopScrollView view = CreateView<TestVerticalLoopScrollView>(out RectTransform content);
        view.RefreshDataList(CreateDataList(100));
        CompletePostLayout(view);

        view.ClearDataList();

        Assert.AreEqual(0, view.DataList.Count);
        Assert.AreEqual(0, view.VisibleItems.Count);
        Assert.AreEqual(0, CountActiveChildren(content));
        Assert.AreEqual(250f, content.sizeDelta.y, 0.001f);
        Assert.AreEqual(Vector2.zero, content.anchoredPosition);
    }

    [Test]
    public void RefreshDataList_CreatesItemsAfterViewportPostLayout()
    {
        TestVerticalLoopScrollView view = CreateView<TestVerticalLoopScrollView>(out _);
        RectTransform viewport = view.GetComponent<ScrollRect>().viewport;
        viewport.sizeDelta = new Vector2(250f, 0f);

        view.RefreshDataList(CreateDataList(100));

        Assert.AreEqual(0, view.VisibleItems.Count);

        viewport.sizeDelta = new Vector2(250f, 250f);
        CompletePostLayout(view);

        Assert.AreEqual(6, view.VisibleItems.Count);
    }

    private T CreateView<T>(out RectTransform content) where T : Component
    {
        GameObject root = CreateObject("LoopScrollView", typeof(RectTransform), typeof(ScrollRect));
        root.SetActive(false);
        GameObject viewportObject = CreateObject("Viewport", typeof(RectTransform));
        GameObject contentObject = CreateObject("Content", typeof(RectTransform));
        GameObject prefab = CreateObject("ItemPrefab", typeof(RectTransform), typeof(VirtualLoopItem));
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        content = contentObject.GetComponent<RectTransform>();
        RectTransform prefabRect = prefab.GetComponent<RectTransform>();
        viewport.SetParent(root.transform, false);
        content.SetParent(viewport, false);
        viewport.sizeDelta = new Vector2(250f, 250f);
        prefabRect.sizeDelta = new Vector2(100f, 100f);
        prefab.SetActive(false);

        ScrollRect scrollRect = root.GetComponent<ScrollRect>();
        scrollRect.viewport = viewport;
        scrollRect.content = content;
        T view = root.AddComponent<T>();
        SetPrivateField(view, "itemPrefab", prefab);
        SetPrivateField(view, "bufferLines", 0);
        root.SetActive(true);
        return view;
    }

    private List<VirtualLoopItemData> CreateDataList(int count)
    {
        List<VirtualLoopItemData> dataList = new(count);
        for (int i = 0; i < count; i++)
        {
            dataList.Add(new VirtualLoopItemData());
        }
        return dataList;
    }

    private GameObject CreateObject(string objectName, params Type[] components)
    {
        GameObject createdObject = new(objectName, components);
        createdObjects.Add(createdObject);
        return createdObject;
    }

    private static void SetPrivateField<T>(T target, string fieldName, object value)
    {
        Type type = target.GetType();
        FieldInfo field = null;
        while (type != null && field == null)
        {
            field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            type = type.BaseType;
        }
        Assert.IsNotNull(field);
        field.SetValue(target, value);
    }

    private static int CountActiveChildren(RectTransform parent)
    {
        int count = 0;
        for (int i = 0; i < parent.childCount; i++)
        {
            if (parent.GetChild(i).gameObject.activeSelf)
            {
                count++;
            }
        }
        return count;
    }

    private static void CompletePostLayout<TItem, TData>(LoopScrollViewBase<TItem, TData> view) where TItem : LoopItemBase where TData : LoopItemDataBase
    {
        view.Rebuild(CanvasUpdate.PostLayout);
    }
}

public sealed class VirtualLoopItemData : LoopItemDataBase
{
}

public sealed class VirtualLoopItem : LoopItemBase
{
    public int RefreshCount { get; private set; }

    protected override void OnRefreshByData(LoopItemDataBase data)
    {
        RefreshCount++;
    }
}

public sealed class TestVerticalLoopScrollView : LoopScrollViewVertical<VirtualLoopItem, VirtualLoopItemData>
{
}

public sealed class TestHorizontalLoopScrollView : LoopScrollViewHorizontal<VirtualLoopItem, VirtualLoopItemData>
{
}
