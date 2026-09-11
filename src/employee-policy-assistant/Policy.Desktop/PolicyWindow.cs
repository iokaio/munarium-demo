// SPDX-License-Identifier: Apache-2.0
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Ioka.Munarium.Client;
using Policy.Core;

namespace Policy.Desktop;

public sealed class PolicyWindow : Window
{
    public ComboBox IdentityBox { get; } = new() { MinWidth = 170, PlaceholderText = "Identity" };
    public ComboBox ModelBox { get; } = new() { MinWidth = 280, PlaceholderText = "Model" };
    public TextBox QuestionBox { get; } = new() { Watermark = "Ask about equipment, training, leave, or regional policy…", AcceptsReturn = true, MinHeight = 72, MaxLength = 2000, TextWrapping = TextWrapping.Wrap };
    public Button AskButton { get; } = new() { Content = "Ask policy question" };
    public ListBox SourcesBox { get; } = new() { MinHeight = 100 };
    public TextBox SourceText { get; } = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 130 };
    public TextBox AnswerText { get; } = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 160 };
    public TextBlock StatusText { get; } = new() { TextWrapping = TextWrapping.Wrap };
    public PolicyWindow(PolicyViewModel vm)
    {
        Title = "Munarium · Employee policy assistant"; Width = 1050; Height = 850; MinWidth = 720; MinHeight = 600;
        var follow = new CheckBox { Content = "Include previous question as follow-up context" };
        var cancel = new Button { Content = "Disconnect stream" };
        var reconcile = new Button { Content = "Inspect interrupted turn" };
        var refresh = new Button { Content = "Reload renewed grant" };
        var export = new Button { Content = "Export answer and evidence" };
        IdentityBox.ItemsSource = vm.Identities; ModelBox.ItemsSource = vm.Models;
        IdentityBox.SelectionChanged += async (_, _) =>
        {
            if (IdentityBox.SelectedItem is string identity) { await vm.SelectAsync(identity); ModelBox.SelectedIndex = 0; SourceText.Text = ""; QuestionBox.Text = ""; follow.IsChecked = false; }
        };
        AskButton.Click += async (_, _) => await vm.AskAsync(QuestionBox.Text ?? "", ModelBox.SelectedItem as ModelChoice, follow.IsChecked == true);
        cancel.Click += (_, _) => vm.Cancel();
        reconcile.Click += async (_, _) => await vm.ReconcileAsync();
        refresh.Click += async (_, _) => await vm.RefreshAsync();
        export.Click += async (_, _) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export policy answer", SuggestedFileName = "policy-answer.txt" });
            if (file?.TryGetLocalPath() is string path) await vm.ExportAsync(path);
        };
        SourcesBox.ItemsSource = vm.Sources;
        SourcesBox.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<TurnHit>((hit, _) => new TextBlock { Text = hit?.SourcePath, TextWrapping = TextWrapping.Wrap });
        SourcesBox.SelectionChanged += (_, _) => SourceText.Text = SourcesBox.SelectedItem is TurnHit hit ? $"{Evidence.Label(hit)}\n{hit.SourcePath}\nSHA-256: {hit.SourceContentHash}\n\n{hit.Text}" : "";
        vm.PropertyChanged += (_, _) =>
        {
            StatusText.Text = vm.Status; AnswerText.Text = vm.Answer;
            IdentityBox.IsEnabled = ModelBox.IsEnabled = AskButton.IsEnabled = refresh.IsEnabled = reconcile.IsEnabled = !vm.Busy;
            export.IsEnabled = !vm.Busy && vm.Session.Answer?.Status == "complete";
            cancel.IsEnabled = vm.Busy;
        };
        StatusText.Text = vm.Status;
        Content = new ScrollViewer { Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Employee policy assistant", FontSize = 26, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "Fictional workplace policies · answers are drafts for review", Foreground = Brushes.Gray },
                new WrapPanel { Orientation = Orientation.Horizontal, Children = { IdentityBox, ModelBox, refresh } },
                QuestionBox, follow,
                new WrapPanel { Children = { AskButton, cancel, reconcile, export } },
                StatusText,
                new ItemsControl { ItemsSource = vm.Stages },
                new TextBlock { Text = "Answer", FontWeight = FontWeight.Bold }, AnswerText,
                new TextBlock { Text = "Sources · select a document to inspect the returned excerpt", FontWeight = FontWeight.Bold }, SourcesBox, SourceText
            }
        } };
        Closed += async (_, _) => await vm.Session.DisposeAsync();
    }
}
