using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BingLan.WidgetHost.Sample.Models;

public sealed class TodoItem : INotifyPropertyChanged
{
    private string _text;
    private bool _isCompleted;

    public TodoItem(string text)
    {
        _text = text;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }
            _text = value;
            OnPropertyChanged();
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted == value)
            {
                return;
            }
            _isCompleted = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
