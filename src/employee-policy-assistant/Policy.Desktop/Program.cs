// SPDX-License-Identifier: Apache-2.0
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Policy.Core;

namespace Policy.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<PolicyApplication>().UsePlatformDetect();
}
public sealed class PolicyApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var credentials = Environment.GetEnvironmentVariable("POLICY_CREDENTIALS") ?? "credentials";
            var work = Environment.GetEnvironmentVariable("POLICY_WORK") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MunariumPolicy");
            var endpoint = Environment.GetEnvironmentVariable("MUNARIUM_REST_URL") ?? "http://127.0.0.1:18082";
            var vm = new PolicyViewModel(new PolicySession(endpoint, work), credentials);
            desktop.MainWindow = new PolicyWindow(vm);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
