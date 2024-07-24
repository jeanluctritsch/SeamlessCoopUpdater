using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Win32;

namespace SeamlessCoopUpdater;

class Program
{
    private const string GameRootFolderName = "ELDEN RING";
    private const string ModFolderName = "SeamlessCoop";
    private const string IniFileName = "ersc_settings.ini";
    private const string LibrariesFileRelativePath = "steamapps/libraryfolders.vdf";
    private const string GetLatestReleaseApiUrl = "/repos/LukeYui/EldenRingSeamlessCoopRelease/releases/latest";

    static async Task Main()
    {
        try
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                Console.WriteLine("Ce programme ne fonctionne que sous Windows.");
                Environment.Exit(-1);
            }

            string installationFolder = GetRootInstallationFolder();
            Console.WriteLine($"Dossier d'installation trouvé : {installationFolder}");

            string gameInstallationFolder = Path.Combine(installationFolder, "Game");

            LatestRelease latestRelease = await CheckForUpdates();
            string? currentInstalledVersion = GetCurrentInstalledVersion(gameInstallationFolder);

            string? readLine;

            if (currentInstalledVersion == latestRelease.tag_name)
            {
                Console.WriteLine("Le mod est déjà à jour.");
                Console.WriteLine("Voulez-vous forcer la mise à jour ? ([o]/n)");
                readLine = Console.ReadLine();
                if (!String.IsNullOrEmpty(readLine) && readLine.ToUpper() != "O" && readLine.ToUpper() != "Y")
                {
                    Environment.Exit(0);
                }
            }

            Console.WriteLine(
                $"Dernière version du mod trouvée : {latestRelease.tag_name}. Voulez-vous l'installer ? ([O]/N)");

            readLine = Console.ReadLine();
            if (!String.IsNullOrEmpty(readLine) && readLine.ToUpper() != "O" && readLine.ToUpper() != "Y")
            {
                Environment.Exit(0);
            }

            string downloadedFilePath =
                DownloadFile(latestRelease.assets[0].browser_download_url, latestRelease.tag_name);

            string newVersionModFolder = GetNewVersionModFolder(downloadedFilePath);
            Console.WriteLine($"Dossier de la nouvelle version du mod trouvé : {newVersionModFolder}");

            string? backupFolderPath = CreateModBackup(gameInstallationFolder);
            Console.WriteLine("Backup du mod créé.");

            string modFolder = CopyFiles(newVersionModFolder, gameInstallationFolder);
            Console.WriteLine("Nouvelle version du mod installée.");

            CreateVersionFile(modFolder, latestRelease.tag_name);

            if (!string.IsNullOrEmpty(backupFolderPath))
                ReplaceIniFileValues(modFolder, backupFolderPath);

            Console.WriteLine("Mise à jour terminée.");

            CleanOldBackupFolders(backupFolderPath);

            Console.WriteLine("Appuyez sur une touche pour quitter.");
            Console.ReadKey();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Console.WriteLine("Appuyez sur une touche pour quitter.");
            Console.ReadKey();
        }
    }

    private static string? GetCurrentInstalledVersion(string gameInstallationFolder)
    {
        string modFolder = Path.Combine(gameInstallationFolder, ModFolderName);
        string versionFilePath = Path.Combine(modFolder, "version.txt");

        if (!File.Exists(versionFilePath))
            return null;

        return File.ReadAllText(versionFilePath);
    }

    private static void CreateVersionFile(string modFolder, string latestReleaseTagName)
    {
        string versionFilePath = Path.Combine(modFolder, "version.txt");
        File.WriteAllText(versionFilePath, latestReleaseTagName);
    }

    private static void CleanOldBackupFolders(string? newBackupFolderPath)
    {
        string installationFolder = GetRootInstallationFolder();
        string gameInstallationFolder = Path.Combine(installationFolder, "Game");
        string[] backupFolders =
            Directory.GetDirectories(gameInstallationFolder, $"{ModFolderName}_*", SearchOption.TopDirectoryOnly);

        foreach (string backupFolder in backupFolders)
        {
            if (backupFolder != newBackupFolderPath)
                Directory.Delete(backupFolder, true);
        }
    }

    private static string DownloadFile(string browserDownloadUrl, string version)
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

    private static void ReplaceIniFileValues(string modInstallationFolder, string backupFolderPath)
    {
        string iniFilePath = Path.Combine(modInstallationFolder, IniFileName);
        string backupIniFilePath = Path.Combine(backupFolderPath, IniFileName);

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

    private static string CopyFiles(string newVersionModFolder, string installationFolder)
    {
        string modFolder = Path.Combine(installationFolder, ModFolderName);

        if (Directory.Exists(modFolder))
            Directory.Delete(modFolder, true);

        CopyDirectory(newVersionModFolder, installationFolder, true, true);

        return modFolder;
    }

    private static string? CreateModBackup(string installationFolder)
    {
        string modFolder = Path.Combine(installationFolder, ModFolderName);
        if (!Directory.Exists(modFolder))
            return null;

        string now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string backupFolder = Path.Combine(installationFolder, $"{ModFolderName}_{now}");
        Directory.Move(modFolder, backupFolder);

        return backupFolder;
    }


    private static string GetNewVersionModFolder(string downloadedFilePath)
    {
        string downloadsDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string extractedFolder = Path.Combine(downloadsDirectory, downloadedFilePath.Replace(".zip", ""));
        if (!Directory.Exists(extractedFolder))
        {
            ZipFile.ExtractToDirectory(downloadedFilePath, extractedFolder);
        }

        return extractedFolder;
    }

    private static string GetRootInstallationFolder()
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

        return installationFolder;
    }

    private static string GetSteamInstallationFolderPath()
    {
        string? path64 = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Valve\\Steam")
            .GetValue("InstallPath", string.Empty) as string;

        if (!string.IsNullOrEmpty(path64))
            return path64;

        string? path32 =
            Registry.LocalMachine.OpenSubKey("SOFTWARE\\Valve\\Steam").GetValue("InstallPath", string.Empty) as string;

        if (!string.IsNullOrEmpty(path32))
            return path32;

        throw new Exception("Steam n'est pas installé.");
    }

    static void CopyDirectory(string sourceDir, string destinationDir, bool recursive, bool copyContentsOnly = false)
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

    static async Task<LatestRelease> CheckForUpdates()
    {
        HttpClient client = new HttpClient();
        client.BaseAddress = new Uri("https://api.github.com");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Host = "api.github.com";
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SeamlessCoopUpdater", "1.0"));
        HttpResponseMessage response = await client.GetAsync(GetLatestReleaseApiUrl);
        string responseContent = await response.Content.ReadAsStringAsync();
        LatestRelease? info = JsonSerializer.Deserialize<LatestRelease>(responseContent);

        if (info == null)
        {
            throw new Exception("Impossible de récupérer les informations du mod auprès de GitHub.");
        }

        return info;
    }
}