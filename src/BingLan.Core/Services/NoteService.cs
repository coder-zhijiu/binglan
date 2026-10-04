using BingLan.Core.Models;

namespace BingLan.Core.Services;

public static class NoteService
{
    public const string DefaultTitle = "便签";

    public static NoteWidgetState CreateDefaultWidget() => new();

    public static void CommitTitle(NoteWidgetState note, string? title)
    {
        ArgumentNullException.ThrowIfNull(note);
        note.Title = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title.Trim();
    }

    public static void CommitContent(NoteWidgetState note, string? content)
    {
        ArgumentNullException.ThrowIfNull(note);
        note.Content = NormalizeLineEndings(content ?? string.Empty);
    }

    public static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n');

    public static string ToEditorText(string content) =>
        content.Replace("\n", Environment.NewLine);
}
