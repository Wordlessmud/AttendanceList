namespace AttendanceList.Services;

internal sealed class AsyncInitializationGate<T> where T : class
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private T? _value;

    public async Task<T> GetAsync(Func<Task<T>> initialise)
    {
        var existing = Volatile.Read(ref _value);
        if (existing is not null)
        {
            return existing;
        }

        await _lock.WaitAsync();
        try
        {
            existing = Volatile.Read(ref _value);
            if (existing is not null)
            {
                return existing;
            }

            // Do not publish the value until the asynchronous initializer has
            // finished. Concurrent callers remain behind the gate.
            var created = await initialise();
            Volatile.Write(ref _value, created);
            return created;
        }
        finally
        {
            _lock.Release();
        }
    }
}
