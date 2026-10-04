using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BingLan.WidgetHost.Sample.Models;
using BingLan.WidgetHost.Wpf;

namespace BingLan.WidgetHost.Sample.Windows;

public partial class TodoDemoWindow
{
    private readonly ObservableCollection<TodoItem> _items =
    [
        new("开始今天的第一件事"),
        new("整理桌面文件"),
        new("检查本周安排")
    ];
    private readonly ICollectionView _view;
    private double _expandedHeight = 420;
    private bool _isCollapsed;

    public TodoDemoWindow()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = FilterTodo;
        TodoList.ItemsSource = _view;
        UpdateSummary();
    }

    private bool FilterTodo(object value) =>
        !HideCompletedMenuItem.IsChecked ||
        value is not TodoItem { IsCompleted: true };

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var item = new TodoItem(string.Empty);
        _items.Add(item);
        _view.Refresh();
        UpdateSummary();
        Dispatcher.BeginInvoke(
            () =>
            {
                var editor = FindVisualChildren<TextBox>(TodoList).LastOrDefault();
                editor?.Focus();
            },
            DispatcherPriority.Input);
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is null)
        {
            return;
        }
        MoreButton.ContextMenu.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        MoreButton.ContextMenu.IsOpen = true;
    }

    private void LockMenuItem_Click(object sender, RoutedEventArgs e) =>
        IsLayoutLocked = LockMenuItem.IsChecked;

    private void HideCompletedMenuItem_Click(object sender, RoutedEventArgs e) =>
        _view.Refresh();

    private void PoggetBackdropMenuItem_Click(object sender, RoutedEventArgs e)
    {
        BackdropPreference = WidgetBackdropPreference.PoggetLike;
        PoggetBackdropMenuItem.IsChecked = true;
        AutoBackdropMenuItem.IsChecked = false;
        SolidBackdropMenuItem.IsChecked = false;
    }

    private void AutoBackdropMenuItem_Click(object sender, RoutedEventArgs e)
    {
        BackdropPreference = WidgetBackdropPreference.Auto;
        PoggetBackdropMenuItem.IsChecked = false;
        AutoBackdropMenuItem.IsChecked = true;
        SolidBackdropMenuItem.IsChecked = false;
    }

    private void SolidBackdropMenuItem_Click(object sender, RoutedEventArgs e)
    {
        BackdropPreference = WidgetBackdropPreference.Solid;
        PoggetBackdropMenuItem.IsChecked = false;
        AutoBackdropMenuItem.IsChecked = false;
        SolidBackdropMenuItem.IsChecked = true;
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e) => Close();

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCollapsed)
        {
            MinHeight = 260;
            Height = Math.Max(MinHeight, _expandedHeight);
            CollapseGlyph.Text = "\uE70E";
        }
        else
        {
            _expandedHeight = Height;
            MinHeight = 70;
            Height = 70;
            CollapseGlyph.Text = "\uE70D";
        }
        _isCollapsed = !_isCollapsed;
    }

    private void CompletionChanged(object sender, RoutedEventArgs e)
    {
        if (HideCompletedMenuItem.IsChecked)
        {
            _view.Refresh();
        }
        UpdateSummary();
    }

    private void MoveUpMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TodoItem item })
        {
            return;
        }
        var index = _items.IndexOf(item);
        if (index > 0)
        {
            _items.Move(index, index - 1);
        }
    }

    private void MoveDownMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TodoItem item })
        {
            return;
        }
        var index = _items.IndexOf(item);
        if (index >= 0 && index < _items.Count - 1)
        {
            _items.Move(index, index + 1);
        }
    }

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: TodoItem item })
        {
            _items.Remove(item);
            UpdateSummary();
        }
    }

    private void TitleEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void TodoText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void UpdateSummary()
    {
        var completed = _items.Count(item => item.IsCompleted);
        SummaryText.Text = $"{completed}/{_items.Count} 已完成";
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T result)
            {
                yield return result;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
