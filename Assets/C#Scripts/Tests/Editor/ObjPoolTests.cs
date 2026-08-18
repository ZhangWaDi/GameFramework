using System;
using System.Collections.Generic;
using GameFramework.PoolSystem;
using NUnit.Framework;

public sealed class ObjPoolTests
{
    [Test]
    public void GetAndRelease_ReusesSameInstance()
    {
        int createCount = 0;
        ObjPool<TestPoolObject> pool = new(() => new TestPoolObject(++createCount));
        TestPoolObject item = pool.Get();

        pool.Release(item);
        TestPoolObject reusedItem = pool.Get();

        Assert.AreSame(item, reusedItem);
        Assert.AreEqual(1, createCount);
        pool.Dispose();
    }

    [Test]
    public void Release_RejectsNullForeignAndDuplicateObjects()
    {
        ObjPool<TestPoolObject> pool = new(() => new TestPoolObject(1));
        TestPoolObject item = pool.Get();
        TestPoolObject foreignItem = new(2);

        Assert.Throws<ArgumentNullException>(() => pool.Release(null));
        Assert.Throws<InvalidOperationException>(() => pool.Release(foreignItem));
        pool.Release(item);
        Assert.Throws<InvalidOperationException>(() => pool.Release(item));
        pool.Dispose();
    }

    [Test]
    public void Clear_DestroysOnlyAvailableObjectsAndKeepsPoolUsable()
    {
        List<TestPoolObject> destroyedItems = new();
        ObjPool<TestPoolObject> pool = new(() => new TestPoolObject(1), item => destroyedItems.Add(item));
        TestPoolObject activeItem = pool.Get();
        TestPoolObject availableItem = pool.Get();
        pool.Release(availableItem);

        pool.Clear();

        Assert.AreEqual(1, destroyedItems.Count);
        Assert.AreSame(availableItem, destroyedItems[0]);
        pool.Release(activeItem);
        Assert.AreSame(activeItem, pool.Get());
        pool.Dispose();
    }

    [Test]
    public void Dispose_DestroysAllOwnedObjectsAndClosesPool()
    {
        List<TestPoolObject> destroyedItems = new();
        ObjPool<TestPoolObject> pool = new(() => new TestPoolObject(destroyedItems.Count), item => destroyedItems.Add(item));
        TestPoolObject activeItem = pool.Get();
        TestPoolObject availableItem = pool.Get();
        pool.Release(availableItem);

        pool.Dispose();
        pool.Dispose();

        Assert.AreEqual(2, destroyedItems.Count);
        Assert.Contains(activeItem, destroyedItems);
        Assert.Contains(availableItem, destroyedItems);
        Assert.Throws<ObjectDisposedException>(() => pool.Get());
        Assert.Throws<ObjectDisposedException>(() => pool.Release(activeItem));
        Assert.Throws<ObjectDisposedException>(() => pool.Clear());
    }

    [Test]
    public void Get_UsesReferenceIdentityAndRejectsInvalidFactoryResults()
    {
        ObjPool<TestPoolObject> pool = new(() => new TestPoolObject(1));
        Assert.AreNotSame(pool.Get(), pool.Get());
        pool.Dispose();

        Assert.Throws<ArgumentNullException>(() => new ObjPool<TestPoolObject>(null));
        ObjPool<TestPoolObject> nullPool = new(() => null);
        Assert.Throws<InvalidOperationException>(() => nullPool.Get());
        nullPool.Dispose();
    }

    public sealed class TestPoolObject
    {
        public TestPoolObject(int id)
        {
            Id = id;
        }

        public int Id { get; }

        public override bool Equals(object obj)
        {
            return obj is TestPoolObject;
        }

        public override int GetHashCode()
        {
            return 1;
        }
    }
}
