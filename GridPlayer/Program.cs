using Avalonia;
using System;

namespace GridPlayer;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (System.Linq.Enumerable.Contains(args, "--setup"))
        {
            Console.WriteLine("Setting up project binaries...");
            // Run setup synchronously or in a blocking Task
            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    // Use CurrentDirectory (the project folder) if we're in setup mode
                    string baseDir = Environment.CurrentDirectory;
                    Console.WriteLine($"Starting download into {baseDir}...");
                    var f = GridPlayer.Services.BinaryDownloader.EnsureFfprobeAsync(null, baseDir);
                    var m = GridPlayer.Services.BinaryDownloader.EnsureMpvAsync(null, baseDir);
                    await System.Threading.Tasks.Task.WhenAll(f, m);
                    Console.WriteLine("Binaries ready.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Setup failed: {ex.Message}");
                    Environment.Exit(1);
                }
            }).Wait();
            return;
        }

        // For regular startup, check for binaries quietly
        try
        {
            // Ensure binaries exist in the execution directory
            GridPlayer.Services.BinaryDownloader.EnsureFfprobeAsync().GetAwaiter().GetResult();
            GridPlayer.Services.BinaryDownloader.EnsureMpvAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // If it fails, we'll try to start anyway, but the app might fail later if path isn't set
            Console.Error.WriteLine($"Quiet setup failed error: {ex.Message}");
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
