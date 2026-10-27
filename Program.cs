/*
 * CrimsonX - A GUI VPN client that fetches, tests and load-balances multiple xray configs suited for your network.
 * Copyright (C) 2026 RichTiTAN
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using Avalonia;
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;

namespace CrimsonX;

class Program
{
    private static System.Threading.Mutex? _mutex;

    private static bool _softwareRender;

    private const string ActivationPipe = "CrimsonX-Activate";

    [STAThread]
    public static int Main(string[] args)
    {
        bool portableRequested = args.Any(a => string.Equals(a, "--portable", StringComparison.OrdinalIgnoreCase));
        if (!portableRequested)
        {
            try { portableRequested = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt")); }
            catch { }
        }
        if (portableRequested) CrimsonX.Services.AppPaths.Portable = true;

        if (args.Any(a => string.Equals(a, "--doctor", StringComparison.OrdinalIgnoreCase)))
            return CrimsonX.Services.DiagnosticsService.Run(args);

        if (args.Any(a => string.Equals(a, "--software-render", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(a, "--software", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.SetEnvironmentVariable("LIBGL_ALWAYS_SOFTWARE", "1");
            Environment.SetEnvironmentVariable("GALLIUM_DRIVER", "llvmpipe");
            _softwareRender = true;
        }

        const string appName = "CrimsonX_SingleInstanceMutex";
        _mutex = new System.Threading.Mutex(true, appName, out bool createdNew);
        if (!createdNew)
        {
            RequestActivation();
            return 0;
        }

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            CrimsonX.Services.SimpleLogger.Log(e.ExceptionObject?.ToString() ?? "Unknown");
        TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                CrimsonX.Services.BackgroundTask.Report("unobserved task", e.Exception);
                e.SetObserved();
            };
        try
        {
            CrimsonX.Services.SimpleLogger.Log($"[Startup] process {Environment.ProcessId} starting (log: {CrimsonX.Services.AppPaths.LogDir})");
            StartActivationListener();
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
            CrimsonX.Services.SimpleLogger.Log("[Shutdown] the desktop lifetime ended normally.");
        }
        finally
        {
            CrimsonX.Services.SimpleLogger.Log($"[Shutdown] exiting (process {Environment.ProcessId}).");
            CrimsonX.Services.SimpleLogger.Shutdown();
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        return 0;
    }

    private static void StartActivationListener()
    {
        var thread = new System.Threading.Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        ActivationPipe, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.None);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    reader.ReadToEnd();
                    Avalonia.Threading.Dispatcher.UIThread.Post(ActivateMainWindow);
                }
                catch (Exception ex)
                {
                    CrimsonX.Services.SimpleLogger.Log("[Activation] listener stopped: " + ex.Message);
                    return;
                }
            }
        })
        { IsBackground = true, Name = "CrimsonX activation listener" };
        thread.Start();
    }

    private static void ActivateMainWindow()
    {
        try
        {
            var window = MainWindow.Instance;
            if (window == null) return;
            window.Show();
            if (window.WindowState == Avalonia.Controls.WindowState.Minimized)
                window.WindowState = Avalonia.Controls.WindowState.Normal;
            window.Activate();
        }
        catch (Exception ex)
        {
            CrimsonX.Services.SimpleLogger.Log(ex);
        }
    }

    private static void RequestActivation()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", ActivationPipe, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client);
            writer.Write("show");
            writer.Flush();
        }
        catch
        {
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
