// SPDX-License-Identifier: Apache-2.0
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Policy.Core;
using Policy.Desktop;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Policy.Tests.TestApplication))]
namespace Policy.Tests;

public static class TestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PolicyApplication>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

[Trait("Kind", "headless")]
public sealed class DesktopTests
{
    [AvaloniaFact]
    public async Task DesktopInputStreamingSourcesExportAndIdentitySwitch()
    {
        var vm = new PolicyViewModel(Acceptance.App("desktop-" + Guid.NewGuid().ToString("N")), Acceptance.Credentials);
        var window = new PolicyWindow(vm) { Height = 1080 };
        window.Show();
        try
        {
            window.IdentityBox.SelectedItem = "employee";
            await Idle(vm);
            Assert.Equal("policy-employee", vm.Session.Identity!.Uid);
            window.QuestionBox.Focus(); window.KeyTextInput("What is the equipment request allowance and approval procedure?");
            Assert.Contains("equipment", window.QuestionBox.Text);
            window.AskButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Idle(vm);
            Assert.Equal("complete", vm.Session.Answer!.Status);
            Assert.Contains(Acceptance.ReadScenario("equipment").Required[0], window.AnswerText.Text);
            Assert.Contains(vm.Stages, s => s.StartsWith("expansion"));
            window.SourcesBox.SelectedIndex = 0;
            Assert.Contains("SHA-256:", window.SourceText.Text);
            var path = Path.Combine(Acceptance.Work, "desktop-export.txt");
            await vm.ExportAsync(path); Assert.Contains(Acceptance.ReadScenario("equipment").Required[0], File.ReadAllText(path));
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var bitmap = window.CaptureRenderedFrame())
            {
                Assert.NotNull(bitmap); bitmap.Save(Path.Combine(Acceptance.Work, "application.png"));
            }
            window.IdentityBox.SelectedItem = "hr"; await Idle(vm);
            Assert.Equal("policy-hr", vm.Session.Identity!.Uid);
            Assert.Empty(vm.Sources); Assert.Equal("", window.AnswerText.Text); Assert.Equal("", window.SourceText.Text);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void MissingGrantsShowActionableStatus()
    {
        var vm = new PolicyViewModel(Acceptance.App("no-grants"), "/absent-grants");
        var window = new PolicyWindow(vm); window.Show();
        try { Assert.Empty(vm.Identities); Assert.Contains("bootstrap", window.StatusText.Text); }
        finally { window.Close(); }
    }
    private static async Task Idle(PolicyViewModel vm)
    {
        var limit = DateTime.UtcNow.AddMinutes(3);
        while (vm.Busy && DateTime.UtcNow < limit) await Task.Delay(25);
        Assert.False(vm.Busy, "Desktop operation timed out.");
    }
}

[Trait("Kind", "preview")]
public sealed class PreviewTests
{
    [AvaloniaFact]
    public void RenderInvoiceTerminalAndActualPacket()
    {
        var path = Environment.GetEnvironmentVariable("INVOICE_PACKET_PATH") ?? Directory.GetFiles("/invoice/fixture", "case-001.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal).First();
        var packet = File.ReadAllText(path);
        var lines = string.Join("\n", packet.Split('\n').Take(27));
        var window = new Window
        {
            Width = 1200, Height = 880, Background = Brush.Parse("#101827"),
            Content = new StackPanel { Margin = new Thickness(32), Spacing = 18, Children =
            {
                new TextBlock { Text = "Munarium · Invoice exception batch", Foreground = Brushes.White, FontSize = 28 },
                new TextBlock { Text = "Terminal and exported review packet · synthetic fixture", Foreground = Brush.Parse("#99ACC9"), FontSize = 16 },
                new TextBlock { Text = "$ sh src/invoice-exception/local.sh test\n$ cat " + Path.GetFileName(path), Foreground = Brush.Parse("#75E6B5"), FontFamily = new FontFamily("monospace"), FontSize = 17 },
                new TextBlock { Text = lines, Foreground = Brush.Parse("#E5EDF9"), FontFamily = new FontFamily("monospace"), FontSize = 15, TextWrapping = TextWrapping.Wrap }
            } }
        };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var bitmap = window.CaptureRenderedFrame(); Assert.NotNull(bitmap);
            bitmap.Save(Path.Combine(Acceptance.Work, "invoice-application.png"));
        }
        finally { window.Close(); }
    }
}
