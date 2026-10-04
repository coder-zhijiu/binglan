using System.Windows;
using System.Windows.Automation;

namespace BingLan.App.Windows;

/// <summary>Asks for one line of text, with a heading and a short explanation.</summary>
public partial class TextPromptDialog : Window
{
    public TextPromptDialog(string heading, string description, string value)
    {
        InitializeComponent();
        Title = heading;
        HeadingText.Text = heading;
        DescriptionText.Text = description;
        ValueEditor.Text = value;
        AutomationProperties.SetName(ValueEditor, heading);
        Loaded += (_, _) =>
        {
            ValueEditor.SelectAll();
            ValueEditor.Focus();
        };
    }

    public string Value => ValueEditor.Text;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
