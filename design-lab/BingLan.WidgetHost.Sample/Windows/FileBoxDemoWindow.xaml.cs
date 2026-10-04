using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BingLan.WidgetHost.Sample.Interop;
using BingLan.WidgetHost.Sample.Models;
using BingLan.WidgetHost.Wpf;

namespace BingLan.WidgetHost.Sample.Windows;

public partial class FileBoxDemoWindow
{
    private static readonly Brush NormalBorder = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF));
    private static readonly Brush DropBorder = new SolidColorBrush(Color.FromArgb(0xE8, 0x6E, 0x94, 0xD8));
    private readonly ObservableCollection<FileTile> _items = [];
    private double _expandedHeight = 444;
    private bool _isCollapsed;

    public FileBoxDemoWindow()
    {
        InitializeComponent();
        NormalBorder.Freeze();
        DropBorder.Freeze();

        AddSample("1_现代农学院网...", ".docx");
        AddSample("8期摘要.doc...", ".docx");
        AddSample("8期摘要_一致性...", ".docx");
        AddSample("8期摘要_英文校...", ".docx");
        AddSample("2026小兴安岭...", ".docx");
        AddSample("晋北土壤利用与碳...", ".docx");
        AddSample("退还金额格式.x...", ".xlsx");
        AddSample("委托业务专题可行...", ".docx");
        AddSample("专家咨询费模板...", ".xlsx");
        AddSample("查看信件.pdf", ".pdf");
        AddSample("2026-07-...", ".pdf");

        FileList.ItemsSource = _items;
    }

    private void AddSample(string name, string extension) =>
        _items.Add(new FileTile(
            name,
            extension,
            ShellIconProvider.Get(extension)));

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
            MinHeight = 280;
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

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        var canDrop = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = canDrop ? DragDropEffects.Link : DragDropEffects.None;
        Surface.BorderBrush = canDrop ? DropBorder : NormalBorder;
        Surface.BorderThickness = canDrop ? new Thickness(2) : new Thickness(1.2);
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e) => ResetDropVisual();

    private void Window_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            {
                return;
            }

            foreach (var path in paths.Where(File.Exists).Concat(paths.Where(Directory.Exists)))
            {
                if (_items.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var name = Directory.Exists(path)
                    ? new DirectoryInfo(path).Name
                    : System.IO.Path.GetFileName(path);
                var extension = Directory.Exists(path)
                    ? string.Empty
                    : System.IO.Path.GetExtension(path);
                _items.Add(new FileTile(
                    name,
                    extension,
                    ShellIconProvider.Get(path, useFileAttributes: false),
                    path));
            }
        }
        finally
        {
            ResetDropVisual();
        }
    }

    private void FileTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: FileTile tile })
        {
            Open(tile);
            e.Handled = true;
        }
    }

    private void OpenFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FileTile tile })
        {
            Open(tile);
        }
    }

    private void RemoveFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FileTile tile })
        {
            _items.Remove(tile);
        }
    }

    private static void Open(FileTile tile)
    {
        if (tile.Path is null || (!File.Exists(tile.Path) && !Directory.Exists(tile.Path)))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(tile.Path)
        {
            UseShellExecute = true
        });
    }

    private void ResetDropVisual()
    {
        Surface.BorderBrush = NormalBorder;
        Surface.BorderThickness = new Thickness(1.2);
    }
}
