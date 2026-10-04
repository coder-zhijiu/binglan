using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BingLan.App.Windows;
using BingLan.Core.Models;

/// <summary>Drives a card drag without a mouse to check snapping and the cost of each step.</summary>
internal static class SnapMoveTests
{
    private const int GwlStyle = -16;
    private const long WsMaximizeBox = 0x00010000L;

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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    internal static void Run(Action<Window> show, Action pump)
    {
        var anchor = new TodoWidgetWindow(new TodoWidgetState
        {
            Placement = new WindowPlacement { Left = 200, Top = 200, Width = 300, Height = 320 }
        });
        var moving = new TodoWidgetWindow(new TodoWidgetState
        {
            Placement = new WindowPlacement { Left = 700, Top = 300, Width = 300, Height = 320 }
        });
        show(anchor);
        show(moving);
        try
        {
            var anchorHandle = new WindowInteropHelper(anchor).Handle;
            var movingHandle = new WindowInteropHelper(moving).Handle;
            if (((long)GetWindowLongPtr(movingHandle, GwlStyle) & WsMaximizeBox) != 0)
            {
                throw new InvalidOperationException("卡片窗口不应带最大化能力，否则拖到屏幕边缘会被系统吸附成半屏");
            }

            GetWindowRect(anchorHandle, out var anchorRect);
            GetWindowRect(movingHandle, out var start);
            var scale = System.Windows.Media.VisualTreeHelper.GetDpi(moving).DpiScaleX;
            var gap = (int)Math.Round(16 * scale);

            // Drag so the card would land a few pixels past the standard gap beside the anchor.
            const int cursorX = 900;
            const int cursorY = 500;
            if (!moving.BeginDrag(cursorX, cursorY))
            {
                throw new InvalidOperationException("卡片没有开始拖动");
            }
            var targetLeft = anchorRect.Right + gap + 4;
            var targetTop = anchorRect.Top + 3;
            moving.DragTo(cursorX + targetLeft - start.Left, cursorY + targetTop - start.Top);
            pump();
            GetWindowRect(movingHandle, out var snapped);
            if (snapped.Left != anchorRect.Right + gap || snapped.Top != anchorRect.Top)
            {
                throw new InvalidOperationException(
                    $"拖动时没有吸附到相邻卡片：期望 {anchorRect.Right + gap},{anchorRect.Top}，实际 {snapped.Left},{snapped.Top}");
            }
            if (snapped.Right - snapped.Left != start.Right - start.Left)
            {
                throw new InvalidOperationException("吸附不应改变卡片尺寸");
            }

            // Far from the anchor the card follows the pointer exactly.
            moving.DragTo(cursorX + targetLeft - start.Left + 400, cursorY + targetTop - start.Top + 260);
            pump();
            GetWindowRect(movingHandle, out var free);
            if (free.Left != targetLeft + 400 || free.Top != targetTop + 260)
            {
                throw new InvalidOperationException(
                    $"远离其他卡片时不应吸附：期望 {targetLeft + 400},{targetTop + 260}，实际 {free.Left},{free.Top}");
            }

            // Cost of each drag step, including the render it causes.
            const int steps = 240;
            var slow = 0;
            var total = 0d;
            for (var step = 0; step < steps; step++)
            {
                var offset = step % 60;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                moving.DragTo(cursorX + targetLeft - start.Left + offset, cursorY + targetTop - start.Top + offset / 3);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    () => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                watch.Stop();
                total += watch.Elapsed.TotalMilliseconds;
                if (watch.Elapsed.TotalMilliseconds > 16)
                {
                    slow++;
                }
            }
            moving.EndDrag();
            pump();
            Console.WriteLine($"拖动每步含渲染平均 {total / steps:0.00} ms，超过 16 ms 的步数 {slow}/{steps}");
        }
        finally
        {
            anchor.CanClose = true;
            moving.CanClose = true;
            anchor.Close();
            moving.Close();
            pump();
        }
    }
}
