using System.Windows;
using EverydaySlideshow.Core;
using EverydaySlideshow.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace EverydaySlideshow;

public sealed class CustomMoodDialog : Window
{
    private readonly TextBox _nameBox;
    private readonly TextBox _secondsBox;
    private readonly TextBox _opacityBox;
    private readonly CheckBox _muteBox;
    private readonly CheckBox _includeVideosBox;
    private readonly CheckBox _familySafeBox;
    private readonly ComboBox _verticalBiasBox;

    public CustomMoodDialog(CustomMoodProfile draft, AppLanguage language = AppLanguage.English)
    {
        Title = LocalizedText.Translate(language, "CustomMoodDialogTitle");
        Width = 500;
        Height = 520;
        MinWidth = 460;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _nameBox = new TextBox { Text = draft.Name, Margin = new Thickness(0, 4, 0, 12) };
        _secondsBox = new TextBox { Text = draft.SlideSeconds.ToString(), Margin = new Thickness(0, 4, 0, 12) };
        _opacityBox = new TextBox { Text = draft.Opacity.ToString("0.00"), Margin = new Thickness(0, 4, 0, 12) };
        _muteBox = new CheckBox { Content = LocalizedText.Translate(language, "MuteVideoDialog"), IsChecked = draft.MuteVideo };
        _includeVideosBox = new CheckBox { Content = LocalizedText.Translate(language, "IncludeVideos"), IsChecked = draft.IncludeVideos };
        _familySafeBox = new CheckBox { Content = LocalizedText.Translate(language, "MoodSafe"), IsChecked = draft.FamilySafeMode };
        _verticalBiasBox = new ComboBox
        {
            ItemsSource = new[]
            {
                new BiasChoice(LocalizedText.Translate(language, "NoPreference"), VerticalPhotoBias.Normal),
                new BiasChoice(LocalizedText.Translate(language, "MorePortraits"), VerticalPhotoBias.Prefer),
                new BiasChoice(LocalizedText.Translate(language, "FewerPortraits"), VerticalPhotoBias.Avoid)
            },
            DisplayMemberPath = nameof(BiasChoice.Label),
            SelectedValuePath = nameof(BiasChoice.Value),
            SelectedValue = draft.VerticalBias,
            Margin = new Thickness(0, 4, 0, 12)
        };

        var okButton = new Button
        {
            Content = LocalizedText.Translate(language, "Save"),
            MinWidth = 96,
            IsDefault = true,
            Margin = new Thickness(8, 0, 0, 0)
        };
        okButton.Click += (_, _) =>
        {
            if (!int.TryParse(_secondsBox.Text, out var seconds))
            {
                System.Windows.MessageBox.Show(this, LocalizedText.Translate(language, "SecondsNumericMessage"), LocalizedText.Translate(language, "CustomMoodDialogTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!double.TryParse(_opacityBox.Text, out var opacity))
            {
                System.Windows.MessageBox.Show(this, LocalizedText.Translate(language, "BrightnessNumericMessage"), LocalizedText.Translate(language, "CustomMoodDialogTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Result = new CustomMoodProfile
            {
                Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? LocalizedText.Translate(language, "CustomMoodFallbackName") : _nameBox.Text.Trim(),
                SlideSeconds = PlaybackSpeedCatalog.Normalize(seconds),
                Opacity = Math.Clamp(opacity, 0.35, 1.0),
                MuteVideo = _muteBox.IsChecked == true,
                IncludeVideos = _includeVideosBox.IsChecked == true,
                FamilySafeMode = _familySafeBox.IsChecked == true,
                VerticalBias = _verticalBiasBox.SelectedValue is VerticalPhotoBias bias ? bias : VerticalPhotoBias.Normal
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
        panel.Children.Add(new TextBlock { Text = LocalizedText.Translate(language, "SwitchSeconds"), FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_secondsBox);
        panel.Children.Add(new TextBlock { Text = LocalizedText.Translate(language, "Brightness"), FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_opacityBox);
        panel.Children.Add(new TextBlock { Text = LocalizedText.Translate(language, "PortraitPhotos"), FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_verticalBiasBox);
        panel.Children.Add(_muteBox);
        panel.Children.Add(_includeVideosBox);
        panel.Children.Add(_familySafeBox);
        panel.Children.Add(buttons);
        Content = panel;
    }

    public CustomMoodProfile? Result { get; private set; }

    private sealed record BiasChoice(string Label, VerticalPhotoBias Value)
    {
        public override string ToString() => Label;
    }
}
