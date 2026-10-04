using System.Windows.Media;

namespace BingLan.WidgetHost.Sample.Models;

public sealed record FileTile(
    string Name,
    string Extension,
    ImageSource? Icon,
    string? Path = null);
