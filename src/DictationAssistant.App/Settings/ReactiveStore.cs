namespace DictationAssistant.App.Settings;

public class ReactiveStoreChangedEventArgs<T>(T oldValue, T newValue) : EventArgs
{
    public T OldValue { get; } = oldValue;
    public T NewValue { get; } = newValue;
}

/// <summary>
/// A minimal reactive store for an immutable state <typeparamref name="T"/>.
/// Supports atomic updates, change events, and sub-value observation.
/// </summary>
public class ReactiveStore<T> where T : notnull
{
    public T Value { get; private set; }

    public event EventHandler<ReactiveStoreChangedEventArgs<T>>? Changed;

    public ReactiveStore(T initialValue)
    {
        Value = initialValue;
    }

    public void Update(Func<T, T> mutator)
    {
        var oldValue = Value;
        Value = mutator(Value);
        Changed?.Invoke(this, new ReactiveStoreChangedEventArgs<T>(oldValue, Value));
    }

    /// <summary>
    /// Observe a sub-value. Callback fires only when the selected value changes.
    /// </summary>
    public IDisposable Observe<TSelected>(Func<T, TSelected> selector, Action<TSelected> callback)
    {
        var observer = new Observer<TSelected>(this, selector, callback);
        Changed += observer.OnChanged;
        return observer;
    }

    private sealed class Observer<TSelected>(
        ReactiveStore<T> store,
        Func<T, TSelected> selector,
        Action<TSelected> callback) : IDisposable
    {
        public void Dispose() => store.Changed -= OnChanged;

        internal void OnChanged(object? sender, ReactiveStoreChangedEventArgs<T> e)
        {
            var oldSelected = selector(e.OldValue);
            var newSelected = selector(e.NewValue);
            if (!EqualityComparer<TSelected>.Default.Equals(oldSelected, newSelected))
            {
                callback(newSelected);
            }
        }
    }
}
