using System.Diagnostics;
using Microsoft.Win32;
using Shared;

namespace Web.Services;

public class StarCraftService
{
    private readonly MyStarcraftBot _starcraftBot;

    public StarCraftService(MyStarcraftBot starcraftBot)
    {
        _starcraftBot = starcraftBot;
    }

    private Process? _chaosLauncherProcess;
    private static readonly string _starcraftBasePath = Path.Combine(
        Directory.GetCurrentDirectory(),
        "..",
        "Starcraft"
    );

    public List<string> GetMapOptions()
    {
        var mapsDirectory = Path.Combine(_starcraftBasePath, "Maps");

        if (!Directory.Exists(mapsDirectory))
        {
            Console.WriteLine($"Warning: Maps directory not found at {mapsDirectory}");
            return new List<string>();
        }

        var mapFiles = Directory
            .GetFiles(mapsDirectory, "*.sc?", SearchOption.TopDirectoryOnly)
            .Select(path => $"maps/{Path.GetFileName(path)}")
            .OrderBy(name => name)
            .ToList();

        return mapFiles;
    }

    public void StartStarCraft(GamePreferences gamePreferences)
    {
        BwapiConfigService.ConfigureBwapiIni(gamePreferences);

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(
                _starcraftBasePath,
                "BWAPI",
                "Chaoslauncher",
                "Chaoslauncher - MultiInstance.exe"
            ),
            WorkingDirectory = Path.Combine(_starcraftBasePath, "BWAPI", "Chaoslauncher"),
            UseShellExecute = false,
            Verb = "",
            CreateNoWindow = false,
        };

        _chaosLauncherProcess = Process.Start(startInfo);
    }

    private void CloseStarCraftWindow()
    {
        Console.WriteLine("Looking for StarCraft process...");
        try
        {
            // Use taskkill command which can force-terminate processes
            var processInfo = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = "/F /IM StarCraft.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            var process = Process.Start(processInfo);
            if (process != null)
            {
                process.WaitForExit();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();

                Console.WriteLine($"taskkill output: {output}");
                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine($"taskkill error: {error}");
                }

                process.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error using taskkill: {ex.Message}");
        }
    }

    public void StopChaosLauncher()
    {
        Console.WriteLine("Stopping chaoslauncherprocess");
        if (_chaosLauncherProcess != null && !_chaosLauncherProcess.HasExited)
        {
            _chaosLauncherProcess.Kill();
            _chaosLauncherProcess.WaitForExit();
            _chaosLauncherProcess?.Dispose();
            _chaosLauncherProcess = null;
            Console.WriteLine("Chaoslauncher process stopped.");
        }
    }

    public void StopAndReset()
    {
        // Console.WriteLine("Disposing StarCraftService...");
        // CloseStarCraftWindow();
        // StopChaosLauncher();
    }
}
