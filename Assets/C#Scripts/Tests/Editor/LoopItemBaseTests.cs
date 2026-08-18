using System;
using GameFramework.UI.Virtualization;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class LoopItemBaseTests
{
    private GameObject itemObject;

    [TearDown]
    public void TearDown()
    {
        if (itemObject != null) Object.DestroyImmediate(itemObject);
    }

    [Test]
    public void RefreshByData_SavesDataAndCallsRefreshHook()
    {
        itemObject = new GameObject("LoopItem");
        TestLoopItem item = itemObject.AddComponent<TestLoopItem>();
        TestLoopItemData data = new() { Index = 7 };

        item.RefreshByData(data);

        Assert.AreSame(data, item.Data);
        Assert.AreEqual(7, item.RefreshedIndex);
        Assert.AreEqual(1, item.RefreshCount);
    }

    [Test]
    public void RefreshByData_RejectsNullData()
    {
        itemObject = new GameObject("LoopItem");
        TestLoopItem item = itemObject.AddComponent<TestLoopItem>();

        Assert.Throws<ArgumentNullException>(() => item.RefreshByData(null));
        Assert.IsNull(item.Data);
    }

}

public sealed class TestLoopItemData : LoopItemDataBase
{
}

public sealed class TestLoopItem : LoopItemBase
{
    public int RefreshedIndex { get; private set; } = -1;
    public int RefreshCount { get; private set; }

    protected override void OnRefreshByData(LoopItemDataBase data)
    {
        RefreshedIndex = data.Index;
        RefreshCount++;
    }
}
