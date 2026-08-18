using System;
using System.Collections.Generic;
using GameFramework.EventSystem;
using GameFramework.RedDotSystem;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class RedDotMgrTests
{
    private enum TestRedDotType
    {
        Root,
        SecondRoot,
        Item
    }

    private enum TestRedDotEvent
    {
        RefreshAll,
        RefreshInstance
    }

    private readonly List<GameObject> createdObjects = new();

    [SetUp]
    public void SetUp()
    {
        RedDotMgr.Clear();
        EventCenter.Clear();
        RedDotMgr.Instance.OnInit();
    }

    [TearDown]
    public void TearDown()
    {
        RedDotMgr.Clear();
        EventCenter.Clear();

        foreach (GameObject createdObject in createdObjects)
        {
            if (createdObject != null)
            {
                Object.DestroyImmediate(createdObject);
            }
        }

        createdObjects.Clear();
    }

    [Test]
    public void MultipleInstances_AggregateIntoSameRootWithoutDuplicateCount()
    {
        TestRedDotRule rootRule = new();
        TestRedDotRule itemRule = new(_ => new[] { RedDotNodeKey.Singleton(TestRedDotType.Root) });
        RegisterRules(rootRule, itemRule);

        RedDotNodeKey rootKey = RedDotNodeKey.Singleton(TestRedDotType.Root);
        RedDotNodeKey itemOneKey = RedDotNodeKey.Create(TestRedDotType.Item, 1);
        RedDotNodeKey itemTwoKey = RedDotNodeKey.Create(TestRedDotType.Item, 2);
        GameObject rootObject = CreateObject("RootRedDot");
        GameObject itemOneObject = CreateObject("ItemOneRedDot");
        GameObject itemTwoObject = CreateObject("ItemTwoRedDot");

        RedDotMgr.Instance.BindRedDot(rootKey, rootObject);
        RedDotMgr.Instance.BindRedDot(itemOneKey, itemOneObject);
        RedDotMgr.Instance.BindRedDot(itemTwoKey, itemTwoObject);

        Assert.IsFalse(rootObject.activeSelf);
        Assert.IsFalse(itemOneObject.activeSelf);
        Assert.IsFalse(itemTwoObject.activeSelf);

        itemRule.SetValue(1, 1);
        RedDotMgr.Instance.Refresh(itemOneKey);
        Assert.IsTrue(rootObject.activeSelf);
        Assert.IsTrue(itemOneObject.activeSelf);
        Assert.AreEqual(1, RedDotMgr.Instance.GetCount(rootKey));

        RedDotMgr.Instance.Refresh(itemOneKey);
        Assert.AreEqual(1, RedDotMgr.Instance.GetCount(rootKey));

        itemRule.SetValue(2, 1);
        RedDotMgr.Instance.Refresh(itemTwoKey);
        Assert.AreEqual(2, RedDotMgr.Instance.GetCount(rootKey));

        itemRule.SetValue(1, 0);
        RedDotMgr.Instance.Refresh(itemOneKey);
        Assert.IsTrue(rootObject.activeSelf);
        Assert.AreEqual(1, RedDotMgr.Instance.GetCount(rootKey));

        itemRule.SetValue(2, 0);
        RedDotMgr.Instance.Refresh(itemTwoKey);
        Assert.IsFalse(rootObject.activeSelf);
        Assert.AreEqual(0, RedDotMgr.Instance.GetCount(rootKey));
    }

    [Test]
    public void OneInstance_CanNotifyMultipleParents()
    {
        TestRedDotRule rootRule = new();
        TestRedDotRule secondRootRule = new();
        TestRedDotRule itemRule = new(_ => new[]
        {
            RedDotNodeKey.Singleton(TestRedDotType.Root),
            RedDotNodeKey.Singleton(TestRedDotType.SecondRoot)
        });

        RedDotMgr.Instance.RegisterRedDot(TestRedDotType.Root, rootRule);
        RedDotMgr.Instance.RegisterRedDot(TestRedDotType.SecondRoot, secondRootRule);
        RedDotMgr.Instance.RegisterRedDot(TestRedDotType.Item, itemRule);

        RedDotNodeKey itemKey = RedDotNodeKey.Create(TestRedDotType.Item, 10001);
        itemRule.SetValue(10001, 1);
        RedDotMgr.Instance.Refresh(itemKey);

        Assert.IsTrue(RedDotMgr.Instance.IsActive(RedDotNodeKey.Singleton(TestRedDotType.Root)));
        Assert.IsTrue(RedDotMgr.Instance.IsActive(RedDotNodeKey.Singleton(TestRedDotType.SecondRoot)));
    }

    [Test]
    public void SameInstance_CanBindMultipleGameObjects()
    {
        TestRedDotRule rootRule = new();
        RedDotMgr.Instance.RegisterRedDot(TestRedDotType.Root, rootRule);
        RedDotNodeKey rootKey = RedDotNodeKey.Singleton(TestRedDotType.Root);
        GameObject firstObject = CreateObject("FirstRootRedDot");
        GameObject secondObject = CreateObject("SecondRootRedDot");

        RedDotMgr.Instance.BindRedDot(rootKey, firstObject);
        RedDotMgr.Instance.BindRedDot(rootKey, secondObject);
        RedDotMgr.Instance.SetActive(rootKey, true);

        Assert.IsTrue(firstObject.activeSelf);
        Assert.IsTrue(secondObject.activeSelf);
    }

    [Test]
    public void SynchronizeInstances_RemovesMissingInstanceContribution()
    {
        TestRedDotRule rootRule = new();
        TestRedDotRule itemRule = new(_ => new[] { RedDotNodeKey.Singleton(TestRedDotType.Root) });
        RegisterRules(rootRule, itemRule);
        itemRule.SetValue(1, 1);
        itemRule.SetValue(2, 1);

        RedDotMgr.Instance.SynchronizeInstances(TestRedDotType.Item, new int[] { 1, 2 });
        Assert.AreEqual(2, RedDotMgr.Instance.GetCount(RedDotNodeKey.Singleton(TestRedDotType.Root)));

        RedDotMgr.Instance.SynchronizeInstances(TestRedDotType.Item, new int[] { 2 });
        Assert.AreEqual(1, RedDotMgr.Instance.GetCount(RedDotNodeKey.Singleton(TestRedDotType.Root)));
        Assert.IsFalse(RedDotMgr.Instance.TryGetCount(RedDotNodeKey.Create(TestRedDotType.Item, 1), out _));
    }

    [Test]
    public void RegisteredEvents_RefreshInstancesAndAreRemovedOnRelease()
    {
        TestRedDotRule rootRule = new();
        EventDrivenTestRedDot itemRule = new(_ => new[] { RedDotNodeKey.Singleton(TestRedDotType.Root) });
        RegisterRules(rootRule, itemRule);
        itemRule.SetValue(10, 1);
        itemRule.SetValue(20, 1);

        RedDotMgr.Instance.RegisterNode(RedDotNodeKey.Create(TestRedDotType.Item, 10));
        RedDotMgr.Instance.RegisterNode(RedDotNodeKey.Create(TestRedDotType.Item, 20));
        itemRule.SetValue(10, 0);
        EventCenter.Instance.EventTrigger<int>(TestRedDotEvent.RefreshInstance, 10);

        Assert.AreEqual(1, RedDotMgr.Instance.GetCount(RedDotNodeKey.Singleton(TestRedDotType.Root)));

        itemRule.SetValue(20, 0);
        EventCenter.Instance.EventTrigger(TestRedDotEvent.RefreshAll);
        Assert.AreEqual(0, RedDotMgr.Instance.GetCount(RedDotNodeKey.Singleton(TestRedDotType.Root)));
        Assert.AreEqual(2, EventCenter.Instance.EventCount);

        RedDotMgr.Clear();
        Assert.AreEqual(0, EventCenter.Instance.EventCount);
    }

    private void RegisterRules(BaseRedDot rootRule, BaseRedDot itemRule)
    {
        RedDotMgr.Instance.RegisterRedDot(TestRedDotType.Root, rootRule);
        RedDotMgr.Instance.RegisterRedDot(TestRedDotType.Item, itemRule);
    }

    private GameObject CreateObject(string objectName)
    {
        GameObject createdObject = new(objectName);
        createdObjects.Add(createdObject);
        return createdObject;
    }

    private class TestRedDotRule : BaseRedDot
    {
        private readonly Dictionary<int, int> values = new();
        private readonly Func<RedDotNodeKey, IReadOnlyList<RedDotNodeKey>> parentResolver;

        internal TestRedDotRule(Func<RedDotNodeKey, IReadOnlyList<RedDotNodeKey>> targetParentResolver = null)
        {
            parentResolver = targetParentResolver;
        }

        internal void SetValue(int instanceId, int value)
        {
            values[instanceId] = value;
        }

        protected override int OnCheck(RedDotNodeKey nodeKey)
        {
            return nodeKey.HasInstanceId && values.TryGetValue(nodeKey.InstanceId, out int value) ? value : 0;
        }

        protected override IReadOnlyList<RedDotNodeKey> GetParentKeys(RedDotNodeKey nodeKey)
        {
            return parentResolver?.Invoke(nodeKey) ?? Array.Empty<RedDotNodeKey>();
        }
    }

    private sealed class EventDrivenTestRedDot : TestRedDotRule
    {
        internal EventDrivenTestRedDot(Func<RedDotNodeKey, IReadOnlyList<RedDotNodeKey>> targetParentResolver) : base(targetParentResolver)
        {
        }

        protected override void OnRegisterEvents()
        {
            ListenRefreshAll(TestRedDotEvent.RefreshAll);
            ListenRefreshInstance<int>(TestRedDotEvent.RefreshInstance, instanceId => instanceId);
        }
    }
}
