namespace BingLan.App;

internal sealed class SingleInstanceGate : IDisposable
{
    private readonly Mutex _mutex;
    private bool _ownsMutex = true;

    private SingleInstanceGate(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static bool TryAcquire(string name, out SingleInstanceGate? gate)
    {
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            gate = null;
            return false;
        }

        gate = new SingleInstanceGate(mutex);
        return true;
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _ownsMutex = false;
            _mutex.ReleaseMutex();
        }
        _mutex.Dispose();
    }
}
