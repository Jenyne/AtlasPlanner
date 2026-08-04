using AtlasPlanner.Gui.ViewModels;
using AtlasPlanner.Gui.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AtlasPlanner.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Tree parsing and art loading are slow enough to be visible, so the window is shown
            // first and populated once it is up.
            desktop.MainWindow.Opened += (_, _) => _ = viewModel.InitialiseAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
