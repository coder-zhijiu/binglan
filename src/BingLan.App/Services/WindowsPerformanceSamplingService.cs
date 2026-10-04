using System.Net.NetworkInformation;
using BingLan.App.Interop;
using BingLan.Core.Models;
using BingLan.Core.Services;
using Timer = System.Threading.Timer;

namespace BingLan.App.Services;

// 全应用共享的单一性能采样服务：所有组件只订阅 Sampled 事件，不各自建计时器。
// 采样在 Timer 的线程池回调里完成，宿主负责把快照封送回 UI 线程。
public sealed class WindowsPerformanceSamplingService : IPerformanceSamplingService
{
    private readonly object _gate = new();
    private Timer? _timer;
    private PerformanceSamplingMode _mode = PerformanceSamplingMode.Normal;
    private bool _hasCpuBaseline;
    private ulong _lastIdleTicks;
    private ulong _lastKernelTicks;
    private ulong _lastUserTicks;
    private long _lastSentBytes;
    private long _lastReceivedBytes;
    private DateTimeOffset _lastNetworkSampleAt;

    // Listing adapters costs ~15 ms on a machine with VPN and virtual adapters, reading
    // their counters well under 1 ms, so the list is kept until the network changes (and
    // at most a minute, in case a change goes unreported).
    private static readonly TimeSpan AdapterListLifetime = TimeSpan.FromMinutes(1);
    private NetworkInterface[] _adapters = [];
    private DateTimeOffset _adaptersListedAt;
    private volatile bool _adaptersStale = true;

    public event Action<PerformanceSnapshot>? Sampled;

    public bool IsRunning { get; private set; }

    public PerformanceSamplingMode Mode => _mode;

    public TimeSpan CurrentInterval => PerformanceSamplingRules.ResolveInterval(_mode);

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
            CaptureCpuBaseline();
            CaptureNetworkBaseline();
            _timer = new Timer(OnTick, null, CurrentInterval, CurrentInterval);
            IsRunning = true;
        }
    }

    public void SetMode(PerformanceSamplingMode mode)
    {
        lock (_gate)
        {
            _mode = mode;
            _timer?.Change(CurrentInterval, CurrentInterval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
            _timer?.Dispose();
            _timer = null;
            IsRunning = false;
        }
    }

    public void Dispose() => Stop();

    private void OnTick(object? state)
    {
        PerformanceSnapshot snapshot;
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }
            try
            {
                snapshot = Capture();
            }
            catch (Exception exception) when (exception is NetworkInformationException
                or InvalidOperationException)
            {
                // 单次采样失败不终止共享计时器，下一周期继续。
                return;
            }
        }
        Sampled?.Invoke(snapshot);
    }

    private PerformanceSnapshot Capture()
    {
        var now = DateTimeOffset.Now;
        var cpu = CaptureCpuPercent();
        var memory = CaptureMemoryPercent();
        var (upload, download) = CaptureNetworkRates(now);
        return new PerformanceSnapshot(cpu, memory, upload, download, now);
    }

    private void CaptureCpuBaseline()
    {
        if (PerformanceNativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            _lastIdleTicks = idle.ToUInt64();
            _lastKernelTicks = kernel.ToUInt64();
            _lastUserTicks = user.ToUInt64();
            _hasCpuBaseline = true;
        }
        else
        {
            _hasCpuBaseline = false;
        }
    }

    private double CaptureCpuPercent()
    {
        if (!PerformanceNativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0d;
        }
        if (!_hasCpuBaseline)
        {
            CaptureCpuBaseline();
            return 0d;
        }

        var idleTicks = idle.ToUInt64();
        var kernelTicks = kernel.ToUInt64();
        var userTicks = user.ToUInt64();
        var idleDelta = idleTicks - _lastIdleTicks;
        var totalDelta = (kernelTicks - _lastKernelTicks) + (userTicks - _lastUserTicks);
        _lastIdleTicks = idleTicks;
        _lastKernelTicks = kernelTicks;
        _lastUserTicks = userTicks;
        if (totalDelta == 0)
        {
            return 0d;
        }

        // kernel 时间包含 idle 时间，CPU 占用 = 1 - idle / (kernel + user)。
        return PerformanceSamplingRules.ClampPercent(
            (1d - (double)idleDelta / totalDelta) * 100d);
    }

    private static double CaptureMemoryPercent()
    {
        var status = PerformanceNativeMethods.MemoryStatusEx.Create();
        if (!PerformanceNativeMethods.GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
        {
            return 0d;
        }
        return PerformanceSamplingRules.ClampPercent(status.MemoryLoad);
    }

    private void CaptureNetworkBaseline()
    {
        var (sent, received) = SumNetworkCounters();
        _lastSentBytes = sent;
        _lastReceivedBytes = received;
        _lastNetworkSampleAt = DateTimeOffset.Now;
    }

    private (double Upload, double Download) CaptureNetworkRates(DateTimeOffset now)
    {
        var (sent, received) = SumNetworkCounters();
        var elapsed = now - _lastNetworkSampleAt;
        var upload = PerformanceSamplingRules.ComputeThroughputBytesPerSecond(
            _lastSentBytes, sent, elapsed);
        var download = PerformanceSamplingRules.ComputeThroughputBytesPerSecond(
            _lastReceivedBytes, received, elapsed);
        _lastSentBytes = sent;
        _lastReceivedBytes = received;
        _lastNetworkSampleAt = now;
        return (upload, download);
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => _adaptersStale = true;

    private (long Sent, long Received) SumNetworkCounters()
    {
        var now = DateTimeOffset.UtcNow;
        if (_adaptersStale || now - _adaptersListedAt >= AdapterListLifetime)
        {
            _adaptersStale = false;
            _adaptersListedAt = now;
            _adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                    && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToArray();
        }

        long sent = 0;
        long received = 0;
        foreach (var adapter in _adapters)
        {
            try
            {
                var statistics = adapter.GetIPv4Statistics();
                sent += statistics.BytesSent;
                received += statistics.BytesReceived;
            }
            catch (NetworkInformationException)
            {
                // 单个适配器读取失败时跳过，不影响其余适配器。
            }
        }
        return (sent, received);
    }
}
