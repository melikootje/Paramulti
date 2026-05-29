using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace ParalivesMultiplayer.Installer
{
    public static class Installer
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
        private static string? _repoDir;
        private static string? _gameDir;

        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine("║       ParalivesMultiplayer — Windows Installer           ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            try
            {
                _repoDir = DetectRepoDir();
                if (string.IsNullOrEmpty(_repoDir))
                {
                    _repoDir = AskRepoDir();
                }
                if (string.IsNullOrEmpty(_repoDir) || !Directory.Exists(_repoDir))
                {
                    Error("Repository directory not found.");
                    return 1;
                }

                _gameDir = DetectGameDir();
                if (string.IsNullOrEmpty(_gameDir))
                {
                    _gameDir = AskGameDir();
                }
                if (string.IsNullOrEmpty(_gameDir) || !File.Exists(Path.Combine(_gameDir, "Paralives.exe")))
                {
                    Error("Paralives.exe not found in selected directory.");
                    return 1;
                }

                await EnsureDotnetSdk();
                if (!await BuildMod())
                    return 1;

                await InstallBepInEx();
                DeployDlls();
                WriteConfig();

                Console.WriteLine();
                Success("Installation complete!");
                Console.WriteLine($"  Game directory : {_gameDir}");
                Console.WriteLine($"  Plugins folder : {_gameDir}\\BepInEx\\plugins");
                Console.WriteLine();
                Console.WriteLine("  Launch Paralives, then:");
                Console.WriteLine("    F5 = Host   |   F6 = Connect   |   F7 = Disconnect");
                Console.WriteLine();
                return 0;
            }
            catch (Exception ex)
            {
                Error($"Fatal: {ex.Message}");
                return 1;
            }
        }

        // ── Detection ────────────────────────────────────────

        private static string? DetectRepoDir()
        {
            var cur = Directory.GetCurrentDirectory();
            if (File.Exists(Path.Combine(cur, "masterprompt.md")) ||
                Directory.Exists(Path.Combine(cur, "src", "ParalivesMultiplayer")))
                return cur;
            return null;
        }

        private static string? DetectGameDir()
        {
            var steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "..", "..", "..");
            if (Directory.Exists(steam))
            {
                var candidates = Directory.GetDirectories(steam, "steamapps", SearchOption.TopDirectoryOnly);
                foreach (var s in candidates)
                {
                    var p = Path.Combine(Path.GetDirectoryName(s)!, "common", "Paralives");
                    if (File.Exists(Path.Combine(p, "Paralives.exe")))
                        return p;
                }
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var pf = Path.Combine(programFiles, "Paralives");
            if (File.Exists(Path.Combine(pf, "Paralives.exe")))
                return pf;

            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var steamLocal = Path.Combine(local, "Steam", "steamapps", "common", "Paralives");
            if (File.Exists(Path.Combine(steamLocal, "Paralives.exe")))
                return steamLocal;

            return null;
        }

        private static string? AskRepoDir()
        {
            Console.WriteLine("This installer needs the Paramulti source repository.");
            Console.Write("  Path to Paramulti folder [or press Enter to cancel]: ");
            var line = Console.ReadLine();
            return string.IsNullOrWhiteSpace(line) ? null : line!.Trim('"', ' ');
        }

        private static string? AskGameDir()
        {
            Console.WriteLine("We could not auto-detect your Paralives game folder.");
            Console.Write("  Path to folder containing Paralives.exe: ");
            var line = Console.ReadLine();
            return string.IsNullOrWhiteSpace(line) ? null : line!.Trim('"', ' ');
        }

        // ── .NET SDK ─────────────────────────────────────────

        private static async Task EnsureDotnetSdk()
        {
            Info("Checking for .NET SDK…");
            var dotnet = FindDotnet();
            if (dotnet != null)
            {
                Info($"Found dotnet at {dotnet}");
                return;
            }

            Warn(".NET SDK not found — downloading installer…");
            await InstallDotnetSdk();
        }

        private static string? FindDotnet()
        {
            try
            {
                var psi = new ProcessStartInfo("dotnet", "--version")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi)!;
                p.WaitForExit(10000);
                if (p.ExitCode == 0)
                {
                    var path = Path.GetDirectoryName(p.MainModule!.FileName!);
                    if (path != null) return Path.Combine(path, "dotnet.exe");
                    return null;
                }
            }
            catch { }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var candidate = Path.Combine(programFiles, "dotnet", "dotnet.exe");
            if (File.Exists(candidate))
                return candidate;

            return null;
        }

        private static async Task InstallDotnetSdk()
        {
            var installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "dotnet");

            Info($"Installing .NET SDK to {installDir} …");

            var bootstrapUrl = "https://dot.net/v1/dotnet-dev-win-x64.exe";
            var bootstrapPath = Path.Combine(Path.GetTempPath(), "dotnet-install.exe");

            await DownloadFile(bootstrapUrl, bootstrapPath);

            var psi = new ProcessStartInfo(bootstrapPath)
            {
                Arguments = $"/install-dir \"{installDir}\" /no-path",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi)!;
            _ = Task.Run(async () =>
            {
                while (!proc.StandardOutput.EndOfStream)
                    Console.Write(await proc.StandardOutput.ReadLineAsync() + "\n");
            });
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                Error(".NET SDK installer exited with code " + proc.ExitCode);
                throw new InvalidOperationException("Failed to install .NET SDK");
            }

            File.Delete(bootstrapPath);
            Info(".NET SDK installed successfully.");
        }

        // ── Build ────────────────────────────────────────────

        private static async Task<bool> BuildMod()
        {
            Info("Building ParalivesMultiplayer…");
            var dotnet = FindDotnet() ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "dotnet", "dotnet.exe");

            var csproj = Path.Combine(_repoDir!, "src", "ParalivesMultiplayer", "ParalivesMultiplayer.csproj");

            var psi = new ProcessStartInfo(dotnet, $"build \"{csproj}\" -c Release --verbosity minimal")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _repoDir
            };

            using var proc = Process.Start(psi)!;
            _ = Task.Run(async () =>
            {
                while (!proc.StandardOutput.EndOfStream)
                    Console.Write(await proc.StandardOutput.ReadLineAsync() + "\n");
            });
            _ = Task.Run(async () =>
            {
                while (!proc.StandardError.EndOfStream)
                    Console.Write(await proc.StandardError.ReadLineAsync() + "\n");
            });
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                Error("Build failed — see output above.");
                return false;
            }

            Success("Build succeeded.");
            return true;
        }

        // ── BepInEx ──────────────────────────────────────────

        private static async Task InstallBepInEx()
        {
            var bepDir = Path.Combine(_gameDir!, "BepInEx");
            var pluginsDir = Path.Combine(bepDir, "plugins");

            if (Directory.Exists(pluginsDir) &&
                File.Exists(Path.Combine(bepDir, "core", "BepInEx.dll")))
            {
                Info("BepInEx already installed — skipping.");
                return;
            }

            var version = "5.4.23.5";
            var fileName = $"BepInEx_win_x64_{version}.zip";
            var url = $"https://github.com/BepInEx/BepInEx/releases/download/v{version}/{fileName}";
            var zipPath = Path.Combine(Path.GetTempPath(), fileName);

            Info($"Downloading BepInEx {version} (win_x64)…");
            await DownloadFile(url, zipPath);

            Info($"Extracting BepInEx to {_gameDir}…");
            await ExtractZip(zipPath, _gameDir!);
            File.Delete(zipPath);

            var coreDll = Path.Combine(bepDir, "core", "BepInEx.dll");
            if (!File.Exists(coreDll))
            {
                Warn($"BepInEx core DLL not found after extraction. Contents of {_gameDir}:");
                if (Directory.Exists(_gameDir))
                {
                    foreach (var d in Directory.GetDirectories(_gameDir!))
                        Console.WriteLine($"  [DIR] {Path.GetFileName(d)}");
                    foreach (var f in Directory.GetFiles(_gameDir!))
                        Console.WriteLine($"  [FILE] {Path.GetFileName(f)}");
                }
                throw new InvalidOperationException("BepInEx installation failed - extraction incomplete");
            }

            Success("BepInEx installed.");
        }

        // ── Deploy ───────────────────────────────────────────

        private static void DeployDlls()
        {
            var pluginsDir = Path.Combine(_gameDir!, "BepInEx", "plugins");
            var buildBase = Path.Combine(_repoDir!, "src", "ParalivesMultiplayer", "bin", "Release");

            string? FindFile(string searchPattern)
            {
                foreach (var dir in Directory.EnumerateDirectories(buildBase))
                {
                    var found = Directory.GetFiles(dir, searchPattern);
                    if (found.Length > 0) return found[0];
                }
                return null;
            }

            var mainDll = FindFile("ParalivesMultiplayer.dll");
            var protoDll = FindFile("ParalivesMultiplayer.Protocol.dll");

            if (mainDll == null)
            {
                Error("Built DLL not found — build may have failed.");
                throw new InvalidOperationException("Missing ParalivesMultiplayer.dll");
            }

            // Backup existing
            var targetMain = Path.Combine(pluginsDir, "ParalivesMultiplayer.dll");
            if (File.Exists(targetMain))
            {
                var bak = $"{targetMain}.bak.{DateTime.Now:yyyyMMddHHmmss}";
                Info($"Backing up existing plugin to {bak}");
                File.Copy(targetMain, bak);
            }

            File.Copy(mainDll, targetMain, true);
            Info($"Deployed ParalivesMultiplayer.dll");

            if (protoDll != null)
            {
                File.Copy(protoDll, Path.Combine(pluginsDir, "ParalivesMultiplayer.Protocol.dll"), true);
                Info("Deployed ParalivesMultiplayer.Protocol.dll");
            }

            var mainPdb = FindFile("ParalivesMultiplayer.pdb");
            if (mainPdb != null)
            {
                File.Copy(mainPdb, Path.Combine(pluginsDir, "ParalivesMultiplayer.pdb"), true);
            }
        }

        private static void WriteConfig()
        {
            var cfgPath = Path.Combine(_gameDir!, "BepInEx", "config", "com.paralives.multiplayer.cfg");
            Directory.CreateDirectory(Path.GetDirectoryName(cfgPath)!);

            if (File.Exists(cfgPath))
            {
                Info("Config file already exists — skipping.");
                return;
            }

            var cfg = @"[Network]
ListenPort=7890
ConnectAddress=127.0.0.1

[Harmony]
EnablePatches=True

[BuildSync]
DryRunMode=False
RealApplyMode=False

[Debug]
VerboseLogging=False
";
            File.WriteAllText(cfgPath, cfg);
            Info("Created default config at BepInEx/config/com.paralives.multiplayer.cfg");
        }

        // ── Helpers ──────────────────────────────────────────

        private static async Task DownloadFile(string url, string destPath)
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            using var stream = await resp.Content.ReadAsStreamAsync();
            using var file = File.Create(destPath);
            await stream.CopyToAsync(file);
        }

        private static async Task ExtractZip(string zipPath, string destDir)
        {
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -Command \"Expand-Archive -Path '{zipPath}' -DestinationPath '{destDir}' -Force\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            var errorOutput = "";
            _ = Task.Run(async () =>
            {
                while (!p.StandardError.EndOfStream)
                    errorOutput += await p.StandardError.ReadLineAsync() + "\n";
            });
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"Failed to extract {zipPath}: {errorOutput}");
        }

        // ── Console helpers ──────────────────────────────────

        private static void Info(string m) => Console.WriteLine($"[INFO]  {m}");
        private static void Warn(string m) => Console.WriteLine($"[WARN]  {m}");
        private static void Error(string m) => Console.Error.WriteLine($"[ERROR] {m}");
        private static void Success(string m) => Console.WriteLine($"[OK]    {m}");
    }
}
