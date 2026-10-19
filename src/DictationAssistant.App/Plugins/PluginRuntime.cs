using System.Diagnostics;
using DictationAssistant.Plugin.Abstractions;

namespace DictationAssistant.App.Plugins;

public sealed record PluginLogEntry(DateTimeOffset Timestamp, string PluginId, PluginLogLevel Level, string Message);

/// <summary>Owns initialization, serialized calls, cooperative cancellation and final cleanup.</summary>
public sealed class PluginRuntime
{
    private const int MaximumLogEntries = 200;
    private const int MaximumPendingCalls = 256;
    private readonly object _gate = new();
    private readonly Func<PluginEntry, IPluginContext> _contextFactory;
    private readonly CancellationTokenSource _lifetime;
    private readonly Dictionary<PluginEntry, RuntimeState> _states = [];
    private readonly Queue<PluginLogEntry> _logs = [];
    private readonly AsyncLocal<InvocationFrame?> _currentFrame = new();
    private Task? _loadTask;
    private Task? _initializeTask;
    private Task? _shutdownTask;
    private Task? _observerTask;
    private PluginWordListSnapshot? _pendingSnapshot;
    private long _latestRevision = long.MinValue;
    private bool _stopping;
    private int _activeCalls;
    private TaskCompletionSource _drained = CompletedSource();

    public PluginRuntime(PluginCatalog catalog, Func<PluginEntry, IPluginContext> contextFactory,
        CancellationToken lifetimeToken)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
    }

    public PluginCatalog Catalog { get; }
    public IReadOnlyList<PluginLogEntry> Logs { get { lock (_gate) return Array.AsReadOnly(_logs.ToArray()); } }

    public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        Task task;
        lock (_gate)
        {
            ThrowIfStopping();
            task = _loadTask ??= Task.Run(Catalog.Load);
        }
        return task.WaitAsync(cancellationToken);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Task task;
        lock (_gate)
        {
            ThrowIfStopping();
            _loadTask ??= Task.Run(Catalog.Load);
            task = _initializeTask ??= Task.Run(InitializeAllCoreAsync);
        }
        return task.WaitAsync(cancellationToken);
    }

    private async Task InitializeAllCoreAsync()
    {
        await _loadTask!.ConfigureAwait(false);
        foreach (var entry in Catalog.Entries.Where(entry => entry.IsLoaded))
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            try { await EnsureInitializedAsync(entry).ConfigureAwait(false); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { throw; }
            catch (Exception) { /* Each failed plugin already has a visible diagnostic. */ }
        }
    }

    public IPluginContext GetContext(PluginEntry entry)
    {
        lock (_gate)
        {
            ThrowIfStopping();
            return GetState(entry).Context.Value;
        }
    }

    /// <summary>Carry only plugin ownership across dispatchers. Expired leases never keep a completed call reentrant.</summary>
    internal Func<Task> CaptureInvocation(Func<Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var captured = _currentFrame.Value;
        return () =>
        {
            var previous = _currentFrame.Value;
            _currentFrame.Value = captured;
            try { return callback(); }
            finally { _currentFrame.Value = previous; }
        };
    }

    private RuntimeState GetState(PluginEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            if (_states.TryGetValue(entry, out var existing)) return existing;
            if (entry.Instance is null || !Catalog.Entries.Contains(entry))
                throw new InvalidOperationException("插件尚未加载或不属于当前运行时。");
            var state = new RuntimeState(new Lazy<IPluginContext>(() => _contextFactory(entry),
                LazyThreadSafetyMode.ExecutionAndPublication));
            _states.Add(entry, state);
            return state;
        }
    }

    private Task EnsureInitializedAsync(PluginEntry entry, bool waitForTurn = true)
    {
        // Check before awaiting a shared task: initialization must never wait on itself.
        for (var frame = _currentFrame.Value; frame is not null; frame = frame.Parent)
            if (frame.Lease.IsActive && ReferenceEquals(frame.Entry, entry) && frame.IsInitialization)
                throw new InvalidOperationException($"插件 {entry.DisplayName} 不能在初始化期间递归调用自身。");
        var state = GetState(entry);
        lock (_gate)
        {
            // Independent initialization chains can otherwise wait on each other (A -> B and B -> A).
            // A new nested dependency is safe: its task inherits this chain and recursion is detected above.
            if ((!waitForTurn || CurrentActiveFrame() is not null) && state.InitializeTask is { IsCompleted: false })
                throw new InvalidOperationException($"插件 {entry.DisplayName} 正在初始化，此操作不能等待其他初始化，请稍后重试。");
            return state.InitializeTask ??= Task.Run(() => InitializeEntryCoreAsync(entry, state));
        }
    }

    private async Task InitializeEntryCoreAsync(PluginEntry entry, RuntimeState state)
    {
        await state.Gate.WaitAsync(_lifetime.Token).ConfigureAwait(false);
        var previous = _currentFrame.Value;
        var current = new InvocationFrame(entry, "initialize", previous, true, new InvocationLease());
        _currentFrame.Value = current;
        try
        {
            _lifetime.Token.ThrowIfCancellationRequested();
            if (entry.Error is not null) throw new InvalidOperationException(entry.Error);
            if (entry.Lifecycle is { } lifecycle)
                await lifecycle.InitializeAsync(state.Context.Value, _lifetime.Token).ConfigureAwait(false);
            PluginCatalog.FreezeCommands(entry);
            entry.IsInitialized = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            entry.Error = $"初始化失败：{ex.Message}";
            entry.Commands = [];
            Log(entry, PluginLogLevel.Error, entry.Error);
            throw;
        }
        finally
        {
            await current.Lease.CloseAsync().ConfigureAwait(false);
            _currentFrame.Value = previous;
            state.Gate.Release();
        }
    }

    public Task InvokeAsync(PluginEntry entry, string operation,
        Func<CancellationToken, Task> callback, CancellationToken cancellationToken = default, bool waitForTurn = true)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return InvokeAsync(entry, operation, async token =>
        {
            await callback(token).ConfigureAwait(false);
            return true;
        }, cancellationToken, waitForTurn);
    }

    /// <summary>Set waitForTurn false for real-time callbacks that must fail rather than queue behind a modal command.</summary>
    public Task<T> InvokeAsync<T>(PluginEntry entry, string operation,
        Func<CancellationToken, Task<T>> callback, CancellationToken cancellationToken = default, bool waitForTurn = true)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(callback);
        if (operation.Length > 200) throw new ArgumentException("插件操作名称不能超过 200 字符。", nameof(operation));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfStopping();
            if (_activeCalls >= MaximumPendingCalls)
                throw new InvalidOperationException("插件操作队列已满，请等待当前操作完成。");
            if (_activeCalls++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _loadTask ??= Task.Run(Catalog.Load);
        }
        // One awaited operation, with instance admission controlled below. Never run synchronous plugin work on UI.
        return Task.Run(async () =>
        {
            try { return await InvokeCoreAsync(entry, operation, callback, cancellationToken, waitForTurn).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Log(entry, PluginLogLevel.Error, $"{operation}：{ex.Message}");
                throw;
            }
            finally
            {
                lock (_gate) if (--_activeCalls == 0) _drained.TrySetResult();
            }
        });
    }

    private async Task<T> InvokeCoreAsync<T>(PluginEntry entry, string operation,
        Func<CancellationToken, Task<T>> callback, CancellationToken cancellationToken, bool waitForTurn)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        token.ThrowIfCancellationRequested();
        await _loadTask!.ConfigureAwait(false);
        InvocationFrame? reentrantOwner = null;
        for (var frame = _currentFrame.Value; frame is not null; frame = frame.Parent)
        {
            if (!frame.Lease.IsActive || !ReferenceEquals(frame.Entry, entry)) continue;
            if (frame.IsInitialization)
                throw new InvalidOperationException($"插件 {entry.DisplayName} 不能在初始化期间递归调用自身。");
            if (string.Equals(frame.Operation, operation, StringComparison.Ordinal))
                throw new InvalidOperationException($"插件 {entry.DisplayName} 的操作 {operation} 发生递归调用。");
            reentrantOwner ??= frame;
        }
        await EnsureInitializedAsync(entry, waitForTurn).WaitAsync(token).ConfigureAwait(false);
        if (!entry.IsLoaded) throw new InvalidOperationException(entry.Error ?? "插件未加载。");
        var state = GetState(entry);
        // Sibling Task.WhenAll calls must not bypass instance serialization. A detached child whose
        // parent already finished becomes an ordinary call and takes the instance gate instead.
        var reentrant = reentrantOwner?.Lease.TryEnterNested() == true;
        if (!reentrant)
        {
            if (!waitForTurn || CurrentActiveFrame() is not null)
            {
                // Avoid cross-plugin deadlocks and real-time audio waiting behind a modal plugin command.
                if (!await state.Gate.WaitAsync(0, token).ConfigureAwait(false))
                    throw new InvalidOperationException($"插件 {entry.DisplayName} 正忙，此操作不能等待其他插件，请稍后重试。");
            }
            else await state.Gate.WaitAsync(token).ConfigureAwait(false);
        }
        var previous = _currentFrame.Value;
        var current = new InvocationFrame(entry, operation, previous, false, new InvocationLease());
        _currentFrame.Value = current;
        try
        {
            token.ThrowIfCancellationRequested();
            return await callback(token).ConfigureAwait(false);
        }
        finally
        {
            await current.Lease.CloseAsync().ConfigureAwait(false);
            _currentFrame.Value = previous;
            if (reentrant) reentrantOwner!.Lease.ExitNested();
            else state.Gate.Release();
        }
    }

    /// <summary>Keep one pending snapshot and one worker, even while a slow observer is running.</summary>
    public void NotifyWordListChanged(PluginWordListSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (_stopping || _lifetime.IsCancellationRequested) return;
            if (snapshot.Revision < _latestRevision) return;
            _latestRevision = snapshot.Revision;
            _pendingSnapshot = snapshot;
            if (_observerTask is null || _observerTask.IsCompleted)
                _observerTask = Task.Run(ObserveWordListCoreAsync);
        }
    }

    private async Task ObserveWordListCoreAsync()
    {
        try
        {
            await InitializeAsync(_lifetime.Token).ConfigureAwait(false);
            while (true)
            {
                PluginWordListSnapshot? snapshot;
                lock (_gate)
                {
                    snapshot = _pendingSnapshot;
                    _pendingSnapshot = null;
                    if (snapshot is null || _stopping)
                    {
                        return;
                    }
                }
                foreach (var entry in Catalog.Entries.Where(entry => entry.IsLoaded && entry.WordListObserver is not null))
                {
                    try
                    {
                        await InvokeAsync(entry, "word-list-changed", token =>
                            entry.WordListObserver!.OnWordListChangedAsync(snapshot, token), _lifetime.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
                    catch (Exception) { /* InvokeAsync already records the plugin failure. */ }
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.WriteLine($"[PluginRuntime] Observer dispatch failed: {ex}");
            lock (_gate) _pendingSnapshot = null;
        }
        finally
        {
            lock (_gate)
            {
                // A new notification may arrive just before a failed worker exits.
                _observerTask = null;
                if (!_stopping && !_lifetime.IsCancellationRequested && _pendingSnapshot is not null)
                    _observerTask = Task.Run(ObserveWordListCoreAsync);
            }
        }
    }

    public void Log(PluginEntry entry, PluginLogLevel level, string message)
    {
        ArgumentNullException.ThrowIfNull(entry);
        message ??= string.Empty;
        if (message.Length > 4000) message = message[..4000];
        var item = new PluginLogEntry(DateTimeOffset.UtcNow, entry.Manifest?.Id ?? entry.DisplayName, level, message);
        if (level == PluginLogLevel.Error) entry.LastError = message;
        lock (_gate)
        {
            if (_logs.Count == MaximumLogEntries) _logs.Dequeue();
            _logs.Enqueue(item);
        }
        Trace.WriteLine($"[Plugin:{item.PluginId}] {level}: {message}");
    }

    /// <summary>Cancel callers, await actual completion, then release instances. Cancelling the wait does not abandon cleanup.</summary>
    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        Task task;
        lock (_gate)
        {
            if (_shutdownTask is null)
            {
                _stopping = true;
                _pendingSnapshot = null;
                _shutdownTask = Task.Run(ShutdownCoreAsync);
            }
            task = _shutdownTask;
        }
        return task.WaitAsync(cancellationToken);
    }

    private async Task ShutdownCoreAsync()
    {
        try { _lifetime.Cancel(); }
        catch (Exception ex) { Trace.WriteLine($"[PluginRuntime] Cancellation callback failed: {ex}"); }
        Task? load;
        Task? initialize;
        Task? observer;
        Task drained;
        lock (_gate)
        {
            load = _loadTask;
            initialize = _initializeTask;
            observer = _observerTask;
            drained = _drained.Task;
        }
        foreach (var task in new[] { load, initialize, observer, drained })
        {
            if (task is null) continue;
            try { await task.ConfigureAwait(false); }
            catch (Exception ex) { Trace.WriteLine($"[PluginRuntime] Work finished during shutdown: {ex.Message}"); }
        }
        KeyValuePair<PluginEntry, RuntimeState>[] states;
        lock (_gate) states = _states.ToArray();
        foreach (var (_, state) in states)
        {
            if (state.InitializeTask is null) continue;
            try { await state.InitializeTask.ConfigureAwait(false); }
            catch (Exception) { /* Initialization diagnostics have already been recorded. */ }
        }
        // Include instances whose constructor/command validation failed, so their resources are not leaked.
        foreach (var entry in Catalog.Entries.Reverse().Where(entry => entry.Instance is not null))
        {
            if (entry.Lifecycle is { } lifecycle)
            {
                try { await lifecycle.ShutdownAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { Log(entry, PluginLogLevel.Error, $"关闭失败：{ex.Message}"); }
            }
            try
            {
                if (entry.Instance is IAsyncDisposable asynchronous) await asynchronous.DisposeAsync().ConfigureAwait(false);
                else if (entry.Instance is IDisposable disposable) disposable.Dispose();
            }
            catch (Exception ex) { Log(entry, PluginLogLevel.Error, $"释放失败：{ex.Message}"); }
            entry.IsInitialized = false;
        }
        foreach (var (_, state) in states) state.Gate.Dispose();
        _lifetime.Dispose();
    }

    private void ThrowIfStopping()
    {
        if (_stopping) throw new InvalidOperationException("插件运行时正在关闭，不能开始新的操作。");
        _lifetime.Token.ThrowIfCancellationRequested();
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private InvocationFrame? CurrentActiveFrame()
    {
        for (var frame = _currentFrame.Value; frame is not null; frame = frame.Parent)
            if (frame.Lease.IsActive) return frame;
        return null;
    }

    private sealed class RuntimeState(Lazy<IPluginContext> context)
    {
        public Lazy<IPluginContext> Context { get; } = context;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Task? InitializeTask { get; set; }
    }

    // The AsyncLocal value and links are immutable. Only the referenced ownership lease expires.
    private sealed record InvocationFrame(PluginEntry Entry, string Operation, InvocationFrame? Parent,
        bool IsInitialization, InvocationLease Lease);

    private sealed class InvocationLease
    {
        private readonly object _gate = new();
        private volatile bool _active = true;
        private bool _nested;
        private TaskCompletionSource _drained = CompletedSource();
        public bool IsActive => _active;

        public bool TryEnterNested()
        {
            lock (_gate)
            {
                if (!_active) return false;
                if (_nested)
                    throw new InvalidOperationException("同一插件不能并发重入；请依次 await 宿主操作。");
                _nested = true;
                _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return true;
            }
        }

        public void ExitNested()
        {
            lock (_gate)
            {
                _nested = false;
                _drained.TrySetResult();
            }
        }

        public Task CloseAsync()
        {
            lock (_gate)
            {
                _active = false;
                return _drained.Task;
            }
        }
    }
}
