using System.Windows;

namespace LiveTranscriber;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings settings;
        Assistant assistant;

        try
        {
            settings = Settings.Load();
            assistant = new Assistant(settings, ApiKeyStore.Load(settings.DataFolder) ?? settings.EnvironmentApiKey ?? string.Empty);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Live Transcriber", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var window = new MainWindow(assistant, settings);
        MainWindow = window;
        window.Show();
        assistant.Start();
    }
}
