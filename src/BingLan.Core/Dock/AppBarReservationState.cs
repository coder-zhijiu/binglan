namespace BingLan.Core.Dock;

public enum AppBarMessage
{
    Register,
    Unregister
}

public sealed class AppBarReservationState
{
    public bool IsRegistered { get; private set; }

    public IReadOnlyList<AppBarMessage> Apply(bool reserveWorkArea)
    {
        if (reserveWorkArea && !IsRegistered)
        {
            IsRegistered = true;
            return [AppBarMessage.Register];
        }

        if (!reserveWorkArea && IsRegistered)
        {
            IsRegistered = false;
            return [AppBarMessage.Unregister];
        }

        return [];
    }

    public IReadOnlyList<AppBarMessage> Release()
    {
        if (!IsRegistered)
        {
            return [];
        }

        IsRegistered = false;
        return [AppBarMessage.Unregister];
    }

    public void ResetRegistration() => IsRegistered = false;

    public void MarkRegisterFailed() => IsRegistered = false;
}
