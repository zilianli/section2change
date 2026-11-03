using System.Collections.Concurrent;
using InterchangeMod.Systems;

namespace InterchangeMod.Generation.Tests;

public class LatestNodeSolveTests
{
    private static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<LatestNodeSolve<int, int>.Completion> Complete(LatestNodeSolve<int, int> solve)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            if (solve.TryTakeCompletion(out var completion)) return completion;
            await Task.Delay(1, timeout.Token);
        }
    }

    // Removing equality coalescing would restart/cancel a long search on every button click.
    [Fact]
    public async Task EqualActiveRequestsShareOneUncancelledWorkerAndRevision()
    {
        var entered = Signal();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        using var solve = new LatestNodeSolve<int, int>((input, token) =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult(true);
            release.Wait(token);
            return input * 10;
        });
        long revision = solve.Request(7);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (int i = 0; i < 100; i++) Assert.Equal(revision, solve.Request(7));
            Assert.False(solve.TryTakeCompletion(out _));
            Assert.Equal(1, calls);
        }
        finally { release.Set(); }
        var completed = await Complete(solve);
        Assert.Equal(70, completed.Result);
        Assert.Equal(revision, completed.Revision);
        Assert.Null(completed.Error);
    }

    // Publishing the first result or starting each queued request would apply stale geometry or overlap workers.
    [Fact]
    public async Task SupersededWorkerIsDrainedBeforeOnlyTheLatestQueuedInputRuns()
    {
        var entered = Signal();
        using var release = new ManualResetEventSlim();
        var calls = new ConcurrentQueue<int>();
        int active = 0, peak = 0;
        using var solve = new LatestNodeSolve<int, int>((input, token) =>
        {
            int current = Interlocked.Increment(ref active);
            Interlocked.Exchange(ref peak, Math.Max(peak, current));
            calls.Enqueue(input);
            try
            {
                if (input == 1)
                {
                    entered.TrySetResult(true);
                    release.Wait(); // Deliberately emulate a solver that has not yet observed cancellation.
                }
                return input * 10;
            }
            finally { Interlocked.Decrement(ref active); }
        });
        solve.Request(1);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            solve.Request(2);
            long latest = solve.Request(3);
            Assert.Equal(latest, solve.Request(3));
            Assert.False(solve.TryTakeCompletion(out _));
            Assert.Equal(new[] { 1 }, calls.ToArray());
        }
        finally { release.Set(); }
        var completed = await Complete(solve);
        Assert.Equal(3, completed.Input);
        Assert.Equal(30, completed.Result);
        Assert.Equal(new[] { 1, 3 }, calls.ToArray());
        Assert.Equal(1, peak);
        Assert.False(solve.IsBusy);
    }

    // Omitting epoch invalidation would let a completed old selection survive cancel/tool exit.
    [Fact]
    public async Task InvalidateSignalsCancellationAndRejectsOldResultEvenForEqualNextInput()
    {
        var entered = Signal(); var exited = Signal();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        CancellationToken oldToken = default;
        using var solve = new LatestNodeSolve<int, int>((input, token) =>
        {
            int call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                oldToken = token;
                entered.TrySetResult(true);
                release.Wait();
                exited.TrySetResult(true);
            }
            return call;
        });
        long oldRevision = solve.Request(4);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            solve.Request(99); // This queued replacement must also be removed by invalidate.
            solve.Invalidate();
            Assert.True(oldToken.IsCancellationRequested);
            Assert.False(solve.TryTakeCompletion(out _));
            Assert.True(solve.Request(4) > oldRevision);
        }
        finally { release.Set(); }
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var completed = await Complete(solve);
        Assert.Equal(2, completed.Result);
        Assert.Equal(2, calls);
    }

    // Swallowing worker exceptions would leave the panel permanently saying it is solving.
    [Fact]
    public async Task CurrentWorkerExceptionIsReturnedAndDoesNotPreventAnotherRequest()
    {
        var failure = new InvalidOperationException("search failed");
        using var solve = new LatestNodeSolve<int, int>((input, token) => input == 1 ? throw failure : 20);
        solve.Request(1);
        Assert.Same(failure, (await Complete(solve)).Error);
        solve.Request(2);
        var completed = await Complete(solve);
        Assert.Null(completed.Error);
        Assert.Equal(20, completed.Result);
    }

    // Dispose must cancel without blocking the game main thread on an uncooperative worker.
    [Fact]
    public async Task DisposeInvalidatesPendingWorkWithoutWaitingForTheWorker()
    {
        var entered = Signal(); var exited = Signal();
        using var release = new ManualResetEventSlim();
        CancellationToken workerToken = default;
        var solve = new LatestNodeSolve<int, int>((input, token) =>
        {
            workerToken = token;
            entered.TrySetResult(true);
            release.Wait();
            exited.TrySetResult(true);
            return input;
        });
        solve.Request(1);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            solve.Request(2);
            solve.Dispose();
            Assert.True(workerToken.IsCancellationRequested);
            Assert.False(solve.IsBusy);
            Assert.Throws<ObjectDisposedException>(() => solve.Request(3));
        }
        finally { release.Set(); solve.Dispose(); }
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(solve.TryTakeCompletion(out _));
    }
}
