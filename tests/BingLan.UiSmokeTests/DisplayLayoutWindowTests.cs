using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BingLan.App.Services;
using BingLan.App.Windows;
using BingLan.Core.Dock;
using BingLan.Core.Models;

/// <summary>
/// A card moved while the arrangement of monitors is changing keeps the position it had
/// on the earlier arrangement, and goes back there once that arrangement returns.
/// </summary>
internal static class DisplayLayoutWindowTests
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    internal static void Run(Action<Window> show, Action pump)
    {
        var settled = typeof(WindowScreenRecovery).GetField("_settledLayout", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("找不到显示器排列状态");
        var state = new TodoWidgetState
        {
            Placement = new WindowPlacement { Left = 240, Top = 220, Width = 300, Height = 320 }
        };
        var window = new TodoWidgetWindow(state);
        var raised = 0;
        void OnChanged() => raised++;
        WindowScreenRecovery.LayoutChanged += OnChanged;
        show(window);
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            WindowScreenRecovery.SettleLayout();
            window.CaptureState();
            GetWindowRect(handle, out var home);
            var layout = WindowScreenRecovery.CurrentLayout();
            if (!DisplayLayoutRules.TryRecall(state.Placement, layout, out var remembered)
                || remembered != new PixelRect(home.Left, home.Top, home.Right, home.Bottom))
            {
                throw new InvalidOperationException("当前排列下应记住卡片位置");
            }

            // Windows moves the card while the new arrangement has not been handled yet.
            settled.SetValue(null, "earlier-arrangement");
            SetWindowPos(handle, 0, home.Left + 300, home.Top + 120, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
            pump();
            window.CaptureState();
            window.CaptureState();
            if (!DisplayLayoutRules.TryRecall(state.Placement, layout, out remembered)
                || remembered.Left != home.Left || remembered.Top != home.Top)
            {
                throw new InvalidOperationException("排列切换过程中被挪动的位置不应覆盖记住的位置");
            }
            if (raised != 1)
            {
                throw new InvalidOperationException($"未处理的排列应只通知一次，实际 {raised} 次");
            }

            if (!window.RestoreDisplayLayout())
            {
                throw new InvalidOperationException("回到记住的排列时卡片应移回原位");
            }
            pump();
            GetWindowRect(handle, out var restored);
            if (restored.Left != home.Left || restored.Top != home.Top
                || restored.Right != home.Right || restored.Bottom != home.Bottom)
            {
                throw new InvalidOperationException(
                    $"卡片应回到 {home.Left},{home.Top}，实际 {restored.Left},{restored.Top}");
            }
        }
        finally
        {
            settled.SetValue(null, null);
            WindowScreenRecovery.LayoutChanged -= OnChanged;
            window.Close();
        }
    }
}
