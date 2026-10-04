using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using BingLan.Core.Themes;

namespace BingLan.App.Windows;

/// <summary>
/// Lets the user bind each dock slot an imported theme could not resolve on its own to a
/// local app, or skip it. The coordinator (via <c>pinBoundApp</c>) remains the only thing
/// that changes dock state; this window only collects the user's choice per slot.
/// </summary>
public partial class ThemeBindingWizardWindow : Window
{
    private readonly Func<ThemeAppBinding, string, int, BindOutcome> _pinBoundApp;

    public ThemeBindingWizardWindow(
        IReadOnlyList<ThemeMissingSlot> missingSlots,
        Func<ThemeAppBinding, string, int, BindOutcome> pinBoundApp)
    {
        InitializeComponent();
        _pinBoundApp = pinBoundApp;
        Rows = new ObservableCollection<ThemeBindingRow>(
            missingSlots.Select(slot => new ThemeBindingRow(slot)));
        SlotList.ItemsSource = Rows;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    public ObservableCollection<ThemeBindingRow> Rows { get; }

    private void ChooseApp_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ThemeBindingRow row)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"为“{row.SlotName}”选择应用",
            Filter = "应用程序 (*.exe;*.lnk)|*.exe;*.lnk",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            DereferenceLinks = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        // A failed binding keeps the slot open so another app can be chosen or it can be skipped.
        var outcome = _pinBoundApp(row.Slot.Binding, dialog.FileName, row.Slot.OriginalIndex);
        row.Status = outcome.Message;
        if (outcome.Succeeded)
        {
            row.MarkResolved();
        }
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ThemeBindingRow row)
        {
            return;
        }

        row.Status = "已跳过，可稍后在 Dock 页手动添加";
        row.MarkResolved();
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

/// <summary>One row of the binding wizard's list.</summary>
public sealed class ThemeBindingRow(ThemeMissingSlot slot) : INotifyPropertyChanged
{
    private string _status = "尚未绑定";
    private bool _isPending = true;

    public ThemeMissingSlot Slot { get; } = slot;
    public string SlotName => AppSlotCatalog.GetName(Slot.Binding.Slot);
    public string BindingName => Slot.Binding.DisplayName.Length > 0 ? Slot.Binding.DisplayName : SlotName;
    public string ChooseAppAutomationName => $"为{SlotName}选择应用";
    public string SkipAutomationName => $"跳过{SlotName}绑定";

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public bool IsPending
    {
        get => _isPending;
        private set
        {
            _isPending = value;
            OnPropertyChanged();
        }
    }

    public void MarkResolved() => IsPending = false;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
