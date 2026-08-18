using System;
using GameFramework.PoolSystem;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class GameObjPoolTests
{
    private GameObject prefab;
    private GameObjPool pool;

    [SetUp]
    public void SetUp()
    {
        prefab = new GameObject("PoolPrefab");
        prefab.SetActive(false);
        pool = new GameObjPool(prefab);
    }

    [TearDown]
    public void TearDown()
    {
        pool?.Dispose();
        if (prefab != null)
        {
            Object.DestroyImmediate(prefab);
        }
    }

    [Test]
    public void GetAndRelease_ReusesInstanceAndUpdatesActiveState()
    {
        GameObject item = pool.Get();
        Assert.IsTrue(item.activeSelf);

        pool.Release(item);
        Assert.IsFalse(item.activeSelf);
        GameObject reusedItem = pool.Get();

        Assert.AreSame(item, reusedItem);
        Assert.IsTrue(reusedItem.activeSelf);
    }

    [Test]
    public void Release_RejectsNullForeignAndDuplicateObjects()
    {
        GameObject item = pool.Get();
        GameObject foreignItem = new("ForeignItem");
        try
        {
            Assert.Throws<ArgumentNullException>(() => pool.Release(null));
            Assert.Throws<InvalidOperationException>(() => pool.Release(foreignItem));
            pool.Release(item);
            Assert.Throws<InvalidOperationException>(() => pool.Release(item));
        }
        finally
        {
            Object.DestroyImmediate(foreignItem);
        }
    }

    [Test]
    public void Clear_DestroysOnlyAvailableObjectsAndKeepsPoolUsable()
    {
        GameObject activeItem = pool.Get();
        GameObject availableItem = pool.Get();
        pool.Release(availableItem);

        pool.Clear();

        Assert.IsTrue(availableItem == null);
        Assert.IsFalse(activeItem == null);
        pool.Release(activeItem);
        Assert.AreSame(activeItem, pool.Get());
    }

    [Test]
    public void Dispose_DestroysAllOwnedObjectsAndClosesPool()
    {
        GameObject activeItem = pool.Get();
        GameObject availableItem = pool.Get();
        pool.Release(availableItem);

        pool.Dispose();
        pool.Dispose();

        Assert.IsTrue(activeItem == null);
        Assert.IsTrue(availableItem == null);
        Assert.Throws<ObjectDisposedException>(() => pool.Get());
        Assert.Throws<ObjectDisposedException>(() => pool.Release(activeItem));
        Assert.Throws<ObjectDisposedException>(() => pool.Clear());
    }

    [Test]
    public void Constructor_RejectsNullPrefab()
    {
        Assert.Throws<ArgumentNullException>(() => new GameObjPool(null));
    }
}
