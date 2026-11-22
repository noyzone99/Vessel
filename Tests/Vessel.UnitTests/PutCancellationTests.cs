using System.Reflection;
using System.Security.Cryptography;

namespace Vessel.Tests;

public sealed class PutCancellationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"VesselPutCancellation-{Guid.NewGuid():N}");

    [Fact]
    public async Task CancellingWhileWaitingForBlobLockPreservesPreviousObject()
    {
        const string key = "existing";
        var testToken = TestContext.Current.CancellationToken;
        byte[] previousBytes = [1, 2, 3];
        using (var storage = new VesselStorage(_root))
        {
            using var previous = new MemoryStream(previousBytes);
            var metadata = await storage.PutAsync(key, previous, cancellationToken: testToken);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(testToken);

            var emptyHash = Convert.ToHexStringLower(SHA256.HashData(ReadOnlySpan<byte>.Empty));
            var blobLock = GetStripe(storage, "_blobLocks", emptyHash);
            var keyLock = GetStripe(storage, "_keyLocks", key);
            var result = new TaskCompletionSource<Task<ObjectMetadata>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writer = new Thread(() => result.SetResult(storage.PutAsync(key, Stream.Null, overwrite: true,
                cancellationToken: cancellation.Token))) { IsBackground = true };

            bool blocked;
            lock (blobLock)
            {
                writer.Start();
                // Staging an uncompressed empty stream never waits for a payload write.
                // The writer holds the key stripe while waiting to enter the blob stripe.
                blocked = SpinWait.SpinUntil(() =>
                    (writer.ThreadState & ThreadState.WaitSleepJoin) != 0 && IsHeldByAnotherThread(keyLock),
                    TimeSpan.FromSeconds(10));
                cancellation.Cancel();
            }

            var put = await result.Task.WaitAsync(testToken);
            Assert.True(blocked, "The writer did not reach the blob lock.");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => put);
            Assert.True(put.IsCanceled);
            Assert.Equal(metadata, storage.GetMetadata(key));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "tmp")));
            Assert.Single(Directory.EnumerateFiles(Path.Combine(_root, "blobs"), "*.blob", SearchOption.AllDirectories));
        }

        using var reopened = new VesselStorage(_root);
        using var reader = reopened.GetStream(key);
        using var content = new MemoryStream();
        await reader.CopyToAsync(content, testToken);
        Assert.Equal(previousBytes, content.ToArray());
    }

    private static object GetStripe(VesselStorage storage, string field, string value)
    {
        var stripes = (object[])typeof(VesselStorage).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(storage)!;
        return stripes[(uint)string.GetHashCode(value, StringComparison.Ordinal) % (uint)stripes.Length];
    }

    private static bool IsHeldByAnotherThread(object gate)
    {
        if (!Monitor.TryEnter(gate)) return true;
        Monitor.Exit(gate);
        return false;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
