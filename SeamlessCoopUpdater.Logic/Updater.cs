using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Win32;
using SeamlessCoopUpdater.Models;

namespace SeamlessCoopUpdater.Logic;

public class Updater
{
    private readonly string _gameInstallationFolder;
    private readonly string? _currentInstalledVersion;
    private readonly string _modFolder;
    private string? _currentBackupFolderPath;
    private LatestRelease? _latestRelease;

    private const string GameRootFolderName = "ELDEN RING";
    private const string ModFolderName = "SeamlessCoop";
    private const string IniFileName = "ersc_settings.ini";
    private const string LibrariesFileRelativePath = "steamapps/libraryfolders.vdf";
    private const string GetLatestReleaseApiUrl = "/repos/LukeYui/EldenRingSeamlessCoopRelease/releases/latest";

    public Updater()
    {
        _gameInstallationFolder = Path.Combine(GetRootInstallationFolder(), "Game");
        _modFolder = Path.Combine(_gameInstallationFolder, ModFolderName);
        _currentInstalledVersion = GetCurrentInstalledVersion();
    }
    
    public string? GetCurrentInstalledVersion()
    {
        string modFolder = Path.Combine(_gameInstallationFolder, ModFolderName);
        string versionFilePath = Path.Combine(modFolder, "version.txt");

        if (!File.Exists(versionFilePath))
            return null;

        return File.ReadAllText(versionFilePath);
    }

    private void CreateVersionFile()
    {
        if (_latestRelease != null)
        {
            string versionFilePath = Path.Combine(_modFolder, "version.txt");
            File.WriteAllText(versionFilePath, _latestRelease?.tag_name);
        }
    }

    public void CleanOldBackupFolders()
    {
        string[] backupFolders =
            Directory.GetDirectories(_gameInstallationFolder, $"{ModFolderName}_*", SearchOption.TopDirectoryOnly);

        foreach (string backupFolder in backupFolders)
        {
            if (backupFolder != _currentBackupFolderPath)
                Directory.Delete(backupFolder, true);
        }

        Console.WriteLine("Anciens backups supprimés.");
    }

    public string DownloadFile(string browserDownloadUrl, string version)
    {
        string downloadsDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string downloadedFilePath = Path.Combine(downloadsDirectory, version + ".zip");

        if (File.Exists(downloadedFilePath))
            File.Delete(downloadedFilePath);

        using (HttpClient client = new HttpClient())
        {
            using (HttpResponseMessage response = client.GetAsync(browserDownloadUrl).Result)
            {
                using (Stream contentStream = response.Content.ReadAsStream())
                {
                    using (FileStream fileStream = new FileStream(downloadedFilePath, FileMode.Create, FileAccess.Write,
                               FileShare.None))
                    {
                        contentStream.CopyTo(fileStream);
                    }
                }
            }
        }

        return downloadedFilePath;
    }

    public void ReplaceIniFileValues()
    {
        if (string.IsNullOrEmpty(_currentBackupFolderPath))
            throw new Exception("Backup du mod introuvable.");
        
        string iniFilePath = Path.Combine(_modFolder, IniFileName);
        string backupIniFilePath = Path.Combine(_currentBackupFolderPath, IniFileName);

        if (!File.Exists(iniFilePath) || !File.Exists(backupIniFilePath))
            throw new FileNotFoundException();

        string[] backupIniFileLines = File.ReadAllLines(backupIniFilePath);
        Dictionary<string, string> backupIniFileValues = backupIniFileLines
            .Where(x => x.Contains("=") && !x.StartsWith(";"))
            .ToDictionary(x => x.Split('=')[0].Trim(), x => x.Split('=')[1].Trim());

        string[] iniFileLines = File.ReadAllLines(iniFilePath);
        Dictionary<string, string> iniFileValues = iniFileLines
            .Where(x => x.Contains("=") && !x.StartsWith(";"))
            .ToDictionary(x => x.Split('=')[0].Trim(), x => x.Split('=')[1].Trim());

        List<string> newIniFileLines = new List<string>();
        foreach (string line in iniFileLines)
        {
            string newLine = line;

            if (line.Contains("="))
            {
                string key = line.Split('=')[0].Trim();
                string value = line.Split('=')[1].Trim();
                if (backupIniFileValues.ContainsKey(key) && backupIniFileValues[key] != value)
                    newLine = $"{line.Substring(0, line.IndexOf('=') + 1)} {backupIniFileValues[key]}";
            }

            newIniFileLines.Add(newLine);
        }

        File.WriteAllLines(iniFilePath, newIniFileLines);

        if (iniFileValues.Count > backupIniFileValues.Count)
            Console.WriteLine("Le nouveau fichier ini contient de nouvelles entrées. Allez y faire un tour !");
    }

    public void CopyFiles(string newVersionModFolder, string installationFolder)
    {
        string modFolder = Path.Combine(installationFolder, ModFolderName);

        if (Directory.Exists(modFolder))
            Directory.Delete(modFolder, true);

        CopyDirectory(newVersionModFolder, installationFolder, true, true);
        
        Console.WriteLine("Fichiers du mod copiés");
    }

    public void CreateModBackup()
    {
        if (!Directory.Exists(_modFolder))
            throw new Exception("Dossier du mod introuvable.");

        string now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _currentBackupFolderPath = Path.Combine(_gameInstallationFolder, $"{ModFolderName}_{now}");
        Directory.Move(_modFolder, _currentBackupFolderPath);
        
        Console.WriteLine("Backup du mod créé.");
    }


    public static string GetNewVersionModFolder(string downloadedFilePath)
    {
        string downloadsDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string extractedFolder = Path.Combine(downloadsDirectory, downloadedFilePath.Replace(".zip", ""));
        if (!Directory.Exists(extractedFolder))
        {
            ZipFile.ExtractToDirectory(downloadedFilePath, extractedFolder);
        }

        Console.WriteLine($"Dossier de la nouvelle version du mod trouvé : {extractedFolder}");

        return extractedFolder;
    }

    private string GetRootInstallationFolder()
    {
        List<string> candidates = new List<string>();
        string steamInstallationFolderPath = GetSteamInstallationFolderPath();
        string steamLibrariesFilePath = Path.Combine(steamInstallationFolderPath, LibrariesFileRelativePath);
        string installationFolder = string.Empty;

        if (!File.Exists(steamLibrariesFilePath))
        {
            Console.WriteLine("Fichier de bibliothèques Steam introuvable.");
            Environment.Exit(-1);
        }

        string[] steamLibraries = File.ReadAllLines(steamLibrariesFilePath)
            .Where(line => line.Contains("\"path\""))
            .Select(line => line.Replace("\"path\"", "").Replace("\\\\", "\\").Replace("\"", "").Trim())
            .ToArray();


        foreach (string steamLibrary in steamLibraries)
        {
            EnumerationOptions options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 8
            };

            candidates.AddRange(Directory.GetDirectories(steamLibrary, GameRootFolderName, options));
        }

        if (candidates.Count == 0)
        {
            Environment.Exit(-1);
        }
        else if (candidates.Count == 1)
        {
            installationFolder = candidates[0];
        }
        else
        {
            Console.WriteLine(
                $"Plusieurs dossiers {GameRootFolderName} ont été trouvés. Lequel voulez-vous mettre à jour ?");
            for (int i = 0; i < candidates.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {candidates[i]}");
            }

            if (!int.TryParse(Console.ReadLine(), out int choice) || choice < 1 || choice > candidates.Count)
            {
                Console.WriteLine("Choix invalide.");
                Environment.Exit(-1);
            }

            installationFolder = candidates[choice - 1];
        }
        
        Console.WriteLine($"Dossier d'installation trouvé : {installationFolder}");

        return installationFolder;
    }

    private string GetSteamInstallationFolderPath()
    {
        Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Valve\\Steam");
        string? path64 = Registry.LocalMachine.GetValue("InstallPath") as string;

        if (!string.IsNullOrEmpty(path64))
            return path64;

        string? path32 =
            Registry.LocalMachine.OpenSubKey("SOFTWARE\\Valve\\Steam").GetValue("InstallPath", string.Empty) as string;

        if (!string.IsNullOrEmpty(path32))
            return path32;

        throw new Exception("Steam n'est pas installé.");
    }

    public static void CopyDirectory(string sourceDir, string destinationDir, bool recursive, bool copyContentsOnly = false)
    {
        // Get information about the source directory
        DirectoryInfo dir = new DirectoryInfo(sourceDir);

        // Check if the source directory exists
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

        // Cache directories before we start copying
        DirectoryInfo[] dirs = dir.GetDirectories();

        // Create the destination directory
        if (!Directory.Exists(destinationDir) && !copyContentsOnly)
            Directory.CreateDirectory(destinationDir);

        // Get the files in the source directory and copy to the destination directory
        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        // If recursive and copying subdirectories, recursively call this method
        if (recursive)
        {
            foreach (DirectoryInfo subDir in dirs)
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir, true);
            }
        }
    }

    public async Task<UpdatesStatus> CheckForUpdates()
    {
        HttpClient client = new HttpClient();
        client.BaseAddress = new Uri("https://api.github.com");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Host = "api.github.com";
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SeamlessCoopUpdater", "1.0"));
        HttpResponseMessage response = await client.GetAsync(GetLatestReleaseApiUrl);
        string responseContent = await response.Content.ReadAsStringAsync();
        _latestRelease = JsonSerializer.Deserialize<LatestRelease>(responseContent);

        if (_latestRelease == null)
        {
            return UpdatesStatus.Error;
        }
        
        if (_currentInstalledVersion == _latestRelease.tag_name)
            return UpdatesStatus.UpToDate;

        return UpdatesStatus.NewVersionAvailable;
    }

    public void Update()
    {
        if (_latestRelease == null)
            throw new Exception("Aucune version trouvée sur GitHub.");
        
        string downloadedFilePath =
            DownloadFile(_latestRelease.assets[0].browser_download_url, _latestRelease.tag_name);

        string newVersionModFolder = GetNewVersionModFolder(downloadedFilePath);

        CreateModBackup();

        CopyFiles(newVersionModFolder, _gameInstallationFolder);

        CreateVersionFile();

        ReplaceIniFileValues();

        CleanOldBackupFolders();
        
        Console.WriteLine("Mise à jour terminée.");
    }
}