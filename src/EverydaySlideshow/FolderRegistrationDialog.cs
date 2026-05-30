using System.IO;
using System.Windows;
using EverydaySlideshow.Core;
using EverydaySlideshow.Services;
using EverydaySlideshow.ViewModels;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace EverydaySlideshow;

public sealed class FolderRegistrationDialog : Window
{
    private readonly string _path;
    private readonly TextBox _nameBox;
    private readonly CheckBox _includeSubfoldersBox;
    private readonly CheckBox _privateBox;

    public FolderRegistrationDialog(string path, AppLanguage language = AppLanguage.English)
    {
        _path = path;
        Title = LocalizedText.Translate(language, "FolderDialogTitle");
        Width = 520;
        Height = 330;
        MinWidth = 480;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var folderName = new DirectoryInfo(path).Name;
        _nameBox = new TextBox { Text = folderName, Margin = new Thickness(0, 4, 0, 14) };
        _includeSubfoldersBox = new CheckBox
        {
            Content = LocalizedText.Translate(language, "IncludeSubfoldersDialog"),
            IsChecked = true
        };
        _privateBox = new CheckBox
        {
            Content = LocalizedText.Translate(language, "AvoidInSafeMode")
        };

        var pathBlock = new TextBlock
        {
            Text = path,
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DimGray,
            Margin = new Thickness(0, 4, 0, 14)
        };

        var okButton = new Button
        {
            Content = LocalizedText.Translate(language, "Register"),
            MinWidth = 96,
            IsDefault = true,
            Margin = new Thickness(8, 0, 0, 0)
        };
        okButton.Click += (_, _) =>
        {
            Result = new FolderRegistrationResult(
                _path,
                string.IsNullOrWhiteSpace(_nameBox.Text) ? folderName : _nameBox.Text,
                _includeSubfoldersBox.IsChecked == true,
                _privateBox.IsChecked == true);
            DialogResult = true;
        };

        var cancelButton = new Button
        {
            Content = LocalizedText.Translate(language, "Cancel"),
            MinWidth = 96,
            IsCancel = true
        };

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);

        var panel = new StackPanel
        {
            Margin = new Thickness(24)
        };
        panel.Children.Add(new TextBlock { Text = LocalizedText.Translate(language, "DisplayName"), FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_nameBox);
        panel.Children.Add(new TextBlock { Text = LocalizedText.Translate(language, "Location"), FontWeight = FontWeights.SemiBold });
        panel.Children.Add(pathBlock);
        panel.Children.Add(_includeSubfoldersBox);
        panel.Children.Add(_privateBox);
        panel.Children.Add(buttons);

        Content = panel;
    }

    public FolderRegistrationResult? Result { get; private set; }
}
