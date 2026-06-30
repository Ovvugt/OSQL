using OSQL.Core.Storage;

namespace OSQL.Core.Tests;

[TestFixture]
public sealed class BufferPoolTests
{
    private static InMemoryPageDevice DeviceWith(int pages)
    {
        var device = new InMemoryPageDevice();
        for (var i = 0; i < pages; i++)
        {
            var p = device.AllocatePage();
            device.PageBytes(p)[0] = (byte)(i + 1); // tag each page so we can tell them apart
        }

        return device;
    }

    [Test]
    public void Fetch_Miss_ReadsFromDeviceOnce()
    {
        var pool = new BufferPool(capacity: 4);
        var device = DeviceWith(1);

        var frame = pool.Fetch(device, 0);
        pool.Unpin(frame, dirty: false);

        Assert.That(frame.Page[0], Is.EqualTo(1));
        Assert.That(pool.DiskReads, Is.EqualTo(1));
    }

    [Test]
    public void Fetch_Hit_DoesNotTouchTheDeviceAgain()
    {
        var pool = new BufferPool(capacity: 4);
        var device = DeviceWith(1);

        pool.Unpin(pool.Fetch(device, 0), dirty: false);
        pool.Unpin(pool.Fetch(device, 0), dirty: false);

        Assert.That(pool.DiskReads, Is.EqualTo(1)); // second fetch was a cache hit
    }

    [Test]
    public void Fetch_BeyondCapacity_EvictsLeastRecentlyUsed()
    {
        var pool = new BufferPool(capacity: 2);
        var device = DeviceWith(3);

        pool.Unpin(pool.Fetch(device, 0), dirty: false); // load 0
        pool.Unpin(pool.Fetch(device, 1), dirty: false); // load 1
        pool.Unpin(pool.Fetch(device, 2), dirty: false); // load 2 -> evicts 0 (LRU)

        Assert.That(pool.Count, Is.EqualTo(2));

        pool.Unpin(pool.Fetch(device, 0), dirty: false); // 0 was evicted -> another read
        Assert.That(pool.DiskReads, Is.EqualTo(4));
    }

    [Test]
    public void Eviction_OfDirtyPage_WritesItBack()
    {
        var pool = new BufferPool(capacity: 1);
        var device = DeviceWith(2);

        var frame = pool.Fetch(device, 0);
        frame.Page[1] = 99;                 // modify page 0
        pool.Unpin(frame, dirty: true);

        pool.Unpin(pool.Fetch(device, 1), dirty: false); // forces page 0 out

        Assert.That(pool.DiskWrites, Is.EqualTo(1));
        Assert.That(device.PageBytes(0)[1], Is.EqualTo(99)); // the change reached the device
    }

    [Test]
    public void Eviction_OfCleanPage_DoesNotWrite()
    {
        var pool = new BufferPool(capacity: 1);
        var device = DeviceWith(2);

        pool.Unpin(pool.Fetch(device, 0), dirty: false);
        pool.Unpin(pool.Fetch(device, 1), dirty: false); // evicts the clean page 0

        Assert.That(pool.DiskWrites, Is.EqualTo(0));
    }

    [Test]
    public void PinnedPage_IsNotEvicted()
    {
        var pool = new BufferPool(capacity: 1);
        var device = DeviceWith(2);

        var pinned = pool.Fetch(device, 0); // held pinned

        // The only frame is pinned, so there's no room to bring in another page.
        Assert.That(() => pool.Fetch(device, 1), Throws.TypeOf<InvalidOperationException>());

        pool.Unpin(pinned, dirty: false);
    }

    [Test]
    public void Flush_WritesDirtyPagesFsyncsAndDropsFrames()
    {
        var pool = new BufferPool(capacity: 4);
        var device = DeviceWith(1);

        var frame = pool.Fetch(device, 0);
        frame.Page[1] = 7;
        pool.Unpin(frame, dirty: true);

        pool.Flush(device);

        Assert.That(device.PageBytes(0)[1], Is.EqualTo(7));
        Assert.That(device.Flushes, Is.EqualTo(1));
        Assert.That(pool.Count, Is.EqualTo(0));
    }
}
