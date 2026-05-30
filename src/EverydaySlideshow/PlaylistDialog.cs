using System.Windows;
using EverydaySlideshow.Core;
using EverydaySlideshow.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ScrollViewer = System.Windows.Controls.ScrollViewer;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace EverydaySlideshow;

public sealed class PlaylistDialog : Window
{
    private readonly TextBox _nameBox;
    private readonly List<CheckBox> _folderChecks = [];

    public PlaylistDialog(IReadOnlyList<FolderProfile> folders, AppLanguage language = AppLanguage.English)
    {
        Title = LocalizedText.Translate(language, "PlaylistDialogTitle");
        Width = 540;
        Height = 520;
        MinWidth = 500;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _nameBox = new TextBox
        {
            Text = LocalizedText.Translate(language, "PlaylistDialogDefaultName"),
            Margin = new Thickness(0, 4, 0, 14)
        };

        var folderPanel = new StackPanel();
        foreach (var folder in folders)
        {
            var check = new CheckBox
            {
                Content = $"{folder.Name}  ({folder.Path})",
                Tag = folder.Id,
                IsChecked = true,
                Margin = new Thickness(0, 4, 0, 4)
            };
            _folderChecks.Add(check);
            folderPanel.Children.Add(check);
        }

        var okButton = new Button
        {
            Content = LocalizedText.Translate(language, "Create"),
            MinWidth = 96,
            IsDefault = true,
            Margin = new Thickness(8, 0, 0, 0)
        };
        okButton.Click += (_, _) =>
        {
            var ids = _folderChecks
                .Where(check => check.IsChecked == true && check.Tag is string)
                .Select(check => (string)check.Tag)
                .ToList();
            if (ids.Count == 0)
            {
                System.Windows.MessageBox.Show(this, LocalizedText.Translate(language, "ChooseOneFolder"), LocalizedText.Translate(language, "Playlists"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Result = new FolderPlaylist
            {
                Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? LocalizedText.Translate(language, "Playlists") : _nameBox.Text.Trim(),
                FolderIds = ids
            };
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
            Margin = new Thickness(0, 18, 0, 0)
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(okButton);

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = LocalizedText.Translate(language, "Name"), FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_nameBox);
        panel.Children.Add(new TextBlock { Text = LocalizedText.Translate(language, "IncludedFolders"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(new ScrollViewer
        {
            Content = folderPanel,
            Height = 260,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
        });
        panel.Children.Add(buttons);
        Content = panel;
    }

    public FolderPlaylist? Result { get; private set; }
}
