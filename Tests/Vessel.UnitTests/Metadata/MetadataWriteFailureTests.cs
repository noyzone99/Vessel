using ZoneTree.AbstractFileStream;

namespace Vessel.Metadata;

public sealed class MetadataWriteFailureTests : IDisposable
{
    private const int WalHeaderSize = sizeof(long) + 2 * sizeof(int);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"VesselMetadataFailure-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialWalAppendBlocksQueuedMutationsAndRecoversOnReopen(bool delete)
    {
        var timeout = TimeSpan.FromSeconds(10);
        var token = TestContext.Current.CancellationToken;
        var committed = CreateMetadata("existing", "committed");
        var replacement = committed with { Hash = "replacement" };
        var queued = CreateMetadata("queued", "queued");
        using var releaseFailure = new ManualResetEventSlim();
        var provider = new FaultingFileStreamProvider(releaseFailure);

        using (var repository = new MetadataRepository(_root, useCompression: false, fileStreamProvider: provider))
        {
            repository.Upsert(committed);
            repository.Upsert(CreateMetadata("untouched", "untouched"));
            var wal = Assert.IsType<FaultingWalStream>(provider.Wal);
            var committedLength = wal.Length;
            wal.FailAfterHeader = true;
            var faulting = Task.Run(() => Record.Exception(() =>
            {
                if (delete) repository.Delete(committed.Key);
                else repository.Upsert(replacement);
            }), token);

            Task<Exception?>? queuedMutation = null;
            try
            {
                await wal.FailureReached.Task.WaitAsync(timeout, token);
                Assert.Equal(committedLength + WalHeaderSize, wal.Length);

                var queuedThread = new TaskCompletionSource<Thread>(TaskCreationOptions.RunContinuationsAsynchronously);
                queuedMutation = Task.Factory.StartNew(() =>
                {
                    queuedThread.SetResult(Thread.CurrentThread);
                    return Record.Exception(() => repository.Upsert(queued));
                }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                var thread = await queuedThread.Task.WaitAsync(timeout, token);
                Assert.True(SpinWait.SpinUntil(() =>
                    queuedMutation.IsCompleted || (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, timeout));
                Assert.False(queuedMutation.IsCompleted);
            }
            finally
            {
                releaseFailure.Set();
                await faulting.WaitAsync(timeout, CancellationToken.None);
                if (queuedMutation is not null) await queuedMutation.WaitAsync(timeout, CancellationToken.None);
            }

            Assert.Same(wal.InjectedFailure, await faulting);
            Assert.NotNull(queuedMutation);
            Assert.Same(wal.InjectedFailure, Assert.IsType<IOException>(await queuedMutation).InnerException);
            Assert.Throws<IOException>(() => repository.Get(committed.Key));
            Assert.Throws<IOException>(() => repository.Delete("untouched"));
            Assert.Throws<IOException>(() => repository.IterateKeys("").ToArray());
            Assert.True(repository.IsHashReferenced(replacement.Hash));
            Assert.Equal(committedLength + WalHeaderSize, wal.Length);
        }

        using (var reopened = new MetadataRepository(_root, useCompression: false))
        {
            Assert.Equal(committed, reopened.Get(committed.Key));
            Assert.NotNull(reopened.Get("untouched"));
            Assert.Null(reopened.Get(queued.Key));
            Assert.True(reopened.IsHashReferenced(committed.Hash));
            Assert.False(reopened.IsHashReferenced(replacement.Hash));
            reopened.Upsert(queued);
        }

        using var reopenedAgain = new MetadataRepository(_root, useCompression: false);
        Assert.Equal(committed, reopenedAgain.Get(committed.Key));
        Assert.Equal(queued, reopenedAgain.Get(queued.Key));
    }

    private static ObjectMetadata CreateMetadata(string key, string hash)
        => new(key, hash, 0, CompressionType.None, hash, DateTimeOffset.UnixEpoch);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FaultingFileStreamProvider(ManualResetEventSlim releaseFailure) : IFileStreamProvider
    {
        private readonly LocalFileStreamProvider _local = new();

        public FaultingWalStream? Wal { get; private set; }

        public IFileStream CreateFileStream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
        {
            var stream = _local.CreateFileStream(path, mode, access, share, bufferSize, options);
            if (Path.GetFileName(Path.GetDirectoryName(path)) != "seg" || !Path.GetFileName(path).Contains(".wal.", StringComparison.Ordinal))
                return stream;

            return Wal = new FaultingWalStream(stream, releaseFailure);
        }

        public bool FileExists(string path) => _local.FileExists(path);
        public bool DirectoryExists(string path) => _local.DirectoryExists(path);
        public void CreateDirectory(string path) => _local.CreateDirectory(path);
        public void DeleteFile(string path) => _local.DeleteFile(path);
        public void DeleteDirectory(string path, bool recursive) => _local.DeleteDirectory(path, recursive);
        public string ReadAllText(string path) => _local.ReadAllText(path);
        public byte[] ReadAllBytes(string path) => _local.ReadAllBytes(path);
        public void Replace(string source, string destination, string backup) => _local.Replace(source, destination, backup);
        public DurableFileWriter GetDurableFileWriter() => _local.GetDurableFileWriter();
        public IReadOnlyList<string> GetDirectories(string path) => _local.GetDirectories(path);
        public string CombinePaths(string first, string second) => _local.CombinePaths(first, second);
    }

    private sealed class FaultingWalStream(IFileStream stream, ManualResetEventSlim releaseFailure) : Stream, IFileStream
    {
        private int _headerBytesLeft = WalHeaderSize;

        public bool FailAfterHeader { get; set; }
        public IOException InjectedFailure { get; } = new("Simulated I/O failure after a WAL record header.");
        public TaskCompletionSource FailureReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string FilePath => stream.FilePath;
        public override long Length => stream.Length;
        public override long Position { get => stream.Position; set => stream.Position = value; }
        public override bool CanRead => stream.CanRead;
        public override bool CanSeek => stream.CanSeek;
        public override bool CanWrite => stream.CanWrite;
        public override void Flush() => stream.Flush();
        public void Flush(bool flushToDisk) => stream.Flush(flushToDisk);
        public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => stream.Read(buffer);
        public int ReadFaster(byte[] buffer, int offset, int count) => stream.ReadFaster(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);
        public override void SetLength(long value) => stream.SetLength(value);
        public Stream ToStream() => this;

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (!FailAfterHeader)
            {
                stream.Write(buffer);
                return;
            }

            var count = Math.Min(_headerBytesLeft, buffer.Length);
            stream.Write(buffer[..count]);
            _headerBytesLeft -= count;
            if (count == buffer.Length) return;

            FailAfterHeader = false;
            stream.Flush();
            FailureReached.SetResult();
            if (!releaseFailure.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("The WAL fault was not released.");
            throw InjectedFailure;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) stream.Dispose();
            base.Dispose(disposing);
        }
    }
}
