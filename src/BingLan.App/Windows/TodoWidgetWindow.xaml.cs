using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BingLan.Core.Models;
using BingLan.Core.Services;

namespace BingLan.App.Windows;

public partial class TodoWidgetWindow : WidgetWindowBase
{
    private readonly ObservableCollection<TodoItemState> _items;
    private string? _textBeforeEdit;

    public TodoWidgetWindow(TodoWidgetState state)
    {
        State = state;
        _items = new ObservableCollection<TodoItemState>(state.Items.OrderBy(x => x.Order));
        InitializeComponent();
        DataContext = state;
        ApplyAppearance(state.Appearance);
        TodoList.ItemsSource = _items;
        TodoList.ItemContainerGenerator.StatusChanged += (_, _) => ApplyCompletedVisibility();
        ApplyPlacement(state.Placement, state.IsLocked);
        WidgetCornerRadius = state.CornerRadius;
        RefreshSummary();
    }

    public TodoWidgetState State { get; }

    protected override System.Windows.Controls.TextBox? TitleEditorBox => TitleEditor;

    protected override void CommitTitleEdit()
    {
        State.Title = string.IsNullOrWhiteSpace(TitleEditor.Text) ? "待办便签" : TitleEditor.Text.Trim();
        TitleEditor.Text = State.Title;
        NotifyStateChanged();
    }

    protected override void OnLockChanged(bool locked)
    {
        State.IsLocked = locked;
    }

    public void CaptureState()
    {
        CopyPlacementTo(State.Placement);
        State.CornerRadius = WidgetCornerRadius;
        State.Items = _items.OrderBy(x => x.Order).ToList();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var item = TodoService.Add(State);
        _items.Add(item);
        CaptureState();
        RefreshSummary();
        NotifyStateChanged();

        Dispatcher.BeginInvoke(() =>
        {
            if (TodoList.ItemContainerGenerator.ContainerFromItem(item) is not ContentPresenter presenter)
            {
                return;
            }
            var editor = FindVisualChild<System.Windows.Controls.TextBox>(presenter);
            editor?.Focus();
        }, DispatcherPriority.Input);
    }

    private void Completion_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }
        ApplyCompletedVisibility();
        RefreshSummary();
        CaptureState();
        NotifyStateChanged();
    }

    private void TodoText_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _textBeforeEdit = (sender as System.Windows.Controls.TextBox)?.Text;
    }

    private void TodoText_LostFocus(object sender, RoutedEventArgs e)
    {
        RefreshSummary();
        CaptureState();
        NotifyStateChanged();
    }

    private void TodoText_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && sender is System.Windows.Controls.TextBox box && _textBeforeEdit is not null)
        {
            box.Text = _textBeforeEdit;
        }

        if (e.Key is Key.Enter or Key.Escape)
        {
            // Clearing keyboard focus does not raise LostFocus, so save here.
            CaptureState();
            NotifyStateChanged();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        CaptureState();
        if (TodoService.RemoveCompleted(State) == 0)
        {
            return;
        }

        _items.Clear();
        foreach (var item in State.Items)
        {
            _items.Add(item);
        }
        RefreshSummary();
        NotifyStateChanged();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(sender, -1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(sender, 1);

    private void Move(object sender, int offset)
    {
        if ((sender as FrameworkElement)?.DataContext is not TodoItemState item)
        {
            return;
        }
        CaptureState();
        if (!TodoService.Move(State, item.Id, offset))
        {
            return;
        }
        ReloadItems();
        NotifyStateChanged();
    }

    private void DeleteTodo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TodoItemState item)
        {
            return;
        }
        _items.Remove(item);
        TodoService.Remove(State, item.Id);
        CaptureState();
        RefreshSummary();
        NotifyStateChanged();
    }

    private void ToggleCompletedVisibility_Click(object sender, RoutedEventArgs e)
    {
        State.HideCompleted = !State.HideCompleted;
        ApplyCompletedVisibility();
        RefreshSummary();
        NotifyStateChanged();
    }

    private void ReloadItems()
    {
        _items.Clear();
        foreach (var item in State.Items.OrderBy(x => x.Order))
        {
            _items.Add(item);
        }
        RefreshSummary();
    }

    // Completed rows are hidden in place rather than filtered out of the list, so the other
    // rows keep their controls and stay reachable for screen readers and other assistive tools.
    private void ApplyCompletedVisibility()
    {
        foreach (var item in _items)
        {
            if (TodoList.ItemContainerGenerator.ContainerFromItem(item) is UIElement row)
            {
                row.Visibility = State.HideCompleted && item.IsCompleted
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }
    }

    // Blank rows are places to type, not tasks, so they are left out of the count.
    private void RefreshSummary()
    {
        var written = _items.Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
        var completed = written.Count(x => x.IsCompleted);
        SummaryText.Text = written.Count == 0 ? "写下第一条待办" : $"{completed}/{written.Count} 已完成";
        ClearCompletedButton.IsEnabled = _items.Any(x => x.IsCompleted);
        CompletedVisibilityButton.Content = State.HideCompleted ? "显示已完成" : "隐藏已完成";
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
            {
                return result;
            }
            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }
        return null;
    }
}
