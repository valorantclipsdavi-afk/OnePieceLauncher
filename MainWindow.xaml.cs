using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace OnePieceLauncherWPF
{
    public partial class MainWindow : Window
    {
        // ==========================================
        // CONFIGURAÇÕES DO JOGO - MUDE AQUI!
        // ==========================================
        private const string GitHubUser = "valorantclipsdavi-afk"; 
        private const string GitHubRepo = "OnePieceGame";
        private const string GitHubLauncherRepo = "OnePieceLauncher";
        private const string GameExecutableName = "OnePieceGame.exe";
        // ==========================================

        private static readonly Version CurrentLauncherVersion = new Version("1.0.0-0626-1"); // Start fresh with 1.0.0 for WPF

        private string githubApiUrl => $"https://api.github.com/repos/{GitHubUser}/{GitHubRepo}/releases/latest";
        private string githubLauncherApiUrl => $"https://api.github.com/repos/{GitHubUser}/{GitHubLauncherRepo}/releases/latest";

        private string gameFolderPath;
        private string versionFilePath;
        private string zipFilePath;
        private string gameExecutablePath;
        private string usernameFilePath;
        private string genderFilePath;

        public MainWindow()
        {
            InitializeComponent();
            lblVersion.Text = $"v{CurrentLauncherVersion.ToString(3)}";
            SetupPaths();
            _ = CheckForUpdatesAsync();
        }

        private void SetupPaths()
        {
            // O Launcher ficará na pasta raiz junto com "Game"
            string baseFolder = AppDomain.CurrentDomain.BaseDirectory;
            gameFolderPath = Path.Combine(baseFolder, "Game");
            versionFilePath = Path.Combine(baseFolder, "versao.txt");
            zipFilePath = Path.Combine(baseFolder, "update.zip");
            gameExecutablePath = Path.Combine(gameFolderPath, GameExecutableName);
            usernameFilePath = Path.Combine(baseFolder, "username.txt");
            genderFilePath = Path.Combine(baseFolder, "gender.txt");

            if (File.Exists(usernameFilePath))
            {
                txtUsername.Text = File.ReadAllText(usernameFilePath).Trim();
            }
            if (File.Exists(genderFilePath))
            {
                string gender = File.ReadAllText(genderFilePath).Trim().ToLower();
                if (gender == "female") rbFemale.IsChecked = true;
                else rbMale.IsChecked = true;
            }

            // Tenta deletar atualizador antigo se existir
            string oldBatch = Path.Combine(baseFolder, "update_launcher.bat");
            if (File.Exists(oldBatch))
            {
                try { File.Delete(oldBatch); } catch { }
            }
        }

        // --- Window Chrome Controls ---
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        // --- Update Logic ---
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OnePieceLauncher", "2.0"));

                    // 1. Verificar se há atualização para o Launcher
                    try
                    {
                        HttpResponseMessage launcherResponse = await client.GetAsync(githubLauncherApiUrl);
                        if (launcherResponse.IsSuccessStatusCode)
                        {
                            string launcherJson = await launcherResponse.Content.ReadAsStringAsync();
                            using (JsonDocument launcherDoc = JsonDocument.Parse(launcherJson))
                            {
                                JsonElement launcherRoot = launcherDoc.RootElement;
                                string latestLauncherTag = launcherRoot.GetProperty("tag_name").GetString() ?? "";
                                
                                string cleanTag = latestLauncherTag.StartsWith("v", StringComparison.OrdinalIgnoreCase) 
                                    ? latestLauncherTag.Substring(1) 
                                    : latestLauncherTag;

                                if (Version.TryParse(cleanTag, out Version? latestLauncherVersion) && latestLauncherVersion > CurrentLauncherVersion)
                                {
                                    var assets = launcherRoot.GetProperty("assets");
                                    if (assets.GetArrayLength() > 0)
                                    {
                                        string launcherDownloadUrl = assets[0].GetProperty("browser_download_url").GetString() ?? "";
                                        if (!string.IsNullOrEmpty(launcherDownloadUrl))
                                        {
                                            await UpdateLauncherAsync(launcherDownloadUrl, latestLauncherVersion.ToString(3), client);
                                            return; 
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Erro ao verificar atualização do launcher: " + ex.Message);
                    }

                    // 2. Verificar se há atualização para o Jogo
                    HttpResponseMessage response = await client.GetAsync(githubApiUrl);
                    if (!response.IsSuccessStatusCode)
                    {
                        lblStatus.Text = "Erro ao acessar internet. Modo offline.";
                        EnablePlayButton();
                        return;
                    }

                    string json = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        JsonElement root = doc.RootElement;
                        string latestVersion = root.GetProperty("tag_name").GetString() ?? "";

                        string localVersion = "v0.0";
                        if (File.Exists(versionFilePath))
                        {
                            localVersion = File.ReadAllText(versionFilePath).Trim();
                        }

                        if (latestVersion != localVersion)
                        {
                            var assets = root.GetProperty("assets");
                            if (assets.GetArrayLength() > 0)
                            {
                                string gameDownloadUrl = assets[0].GetProperty("browser_download_url").GetString() ?? "";
                                if (!string.IsNullOrEmpty(gameDownloadUrl))
                                {
                                    await DownloadAndExtractAsync(gameDownloadUrl, latestVersion, client);
                                }
                                else
                                {
                                    lblStatus.Text = "Arquivo do jogo não encontrado na release.";
                                    EnablePlayButton();
                                }
                            }
                            else
                            {
                                lblStatus.Text = "Nenhum arquivo encontrado na release.";
                                EnablePlayButton();
                            }
                        }
                        else
                        {
                            lblStatus.Text = "O jogo está atualizado!";
                            progressBar.Value = 100;
                            EnablePlayButton();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Erro: " + ex.Message;
                EnablePlayButton();
            }
        }

        private async Task UpdateLauncherAsync(string url, string newVersion, HttpClient client)
        {
            try
            {
                lblStatus.Text = $"Atualizando Launcher ({newVersion})...";

                string baseFolder = AppDomain.CurrentDomain.BaseDirectory;
                string launcherZipPath = Path.Combine(baseFolder, "launcher_update.zip");
                string launcherTempPath = Path.Combine(baseFolder, "launcher_temp");

                using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long? totalBytes = response.Content.Headers.ContentLength;

                    using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                    using (FileStream fileStream = new FileStream(launcherZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        byte[] buffer = new byte[8192];
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes.HasValue)
                            {
                                int percentage = (int)((double)totalRead / totalBytes.Value * 100);
                                Dispatcher.Invoke(() => progressBar.Value = percentage);
                            }
                        }
                    }
                }

                lblStatus.Text = "Extraindo Launcher...";
                await Task.Run(() =>
                {
                    SafeDeleteDirectory(launcherTempPath);
                    Directory.CreateDirectory(launcherTempPath);
                    ZipFile.ExtractToDirectory(launcherZipPath, launcherTempPath);
                });

                string batchPath = Path.Combine(baseFolder, "update_launcher.bat");
                string exeName = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);

                string batchContent = $@"@echo off
:wait
taskkill /f /im ""{exeName}"" > nul 2>&1
timeout /t 1 /nobreak > nul
xcopy ""launcher_temp"" ""."" /y /e /s /i /q > nul
if errorlevel 1 goto wait
rmdir /s /q ""launcher_temp"" > nul
if exist ""launcher_update.zip"" del /f /q ""launcher_update.zip"" > nul
start """" ""{exeName}""
del ""%~f0""
";
                File.WriteAllText(batchPath, batchContent);

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = batchPath,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = baseFolder
                };
                Process.Start(startInfo);

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Erro ao atualizar launcher: " + ex.Message;
                EnablePlayButton();
            }
        }

        private async Task DownloadAndExtractAsync(string url, string newVersion, HttpClient client)
        {
            try
            {
                lblStatus.Text = $"Baixando atualização ({newVersion})...";

                using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;

                    if (File.Exists(zipFilePath))
                    {
                        File.SetAttributes(zipFilePath, FileAttributes.Normal);
                    }

                    using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                    using (FileStream fileStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        byte[] buffer = new byte[8192];
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes.HasValue)
                            {
                                int percentage = (int)((double)totalRead / totalBytes.Value * 100);
                                Dispatcher.Invoke(() => progressBar.Value = percentage);
                            }
                        }
                    }
                }

                lblStatus.Text = "Extraindo arquivos...";
                await Task.Run(() =>
                {
                    SafeDeleteDirectory(gameFolderPath);
                    if (!Directory.Exists(gameFolderPath))
                    {
                        Directory.CreateDirectory(gameFolderPath);
                    }
                    
                    using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            string destinationPath = Path.GetFullPath(Path.Combine(gameFolderPath, entry.FullName));
                            if (destinationPath.StartsWith(gameFolderPath, StringComparison.Ordinal))
                            {
                                if (string.IsNullOrEmpty(entry.Name))
                                {
                                    Directory.CreateDirectory(destinationPath);
                                }
                                else
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                                    if (File.Exists(destinationPath))
                                    {
                                        try { File.Delete(destinationPath); }
                                        catch 
                                        { 
                                            try { File.Move(destinationPath, destinationPath + ".old" + Guid.NewGuid().ToString("N").Substring(0, 4)); } catch { }
                                        }
                                    }
                                    entry.ExtractToFile(destinationPath, true);
                                }
                            }
                        }
                    }
                });

                File.Delete(zipFilePath);
                File.WriteAllText(versionFilePath, newVersion);

                lblStatus.Text = "Atualização concluída!";
                EnablePlayButton();
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Erro ao atualizar: " + ex.Message;
                EnablePlayButton();
            }
        }

        private void EnablePlayButton()
        {
            Dispatcher.Invoke(() => btnPlay.IsEnabled = true);
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            if (string.IsNullOrEmpty(username))
            {
                username = "Pirata_" + new Random().Next(1000, 9999);
                txtUsername.Text = username;
            }
            File.WriteAllText(usernameFilePath, username);

            string gender = rbFemale.IsChecked == true ? "female" : "male";
            File.WriteAllText(genderFilePath, gender);

            string args = $"-username \"{username}\" -gender {gender}";

            if (File.Exists(gameExecutablePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = gameExecutablePath,
                    Arguments = args,
                    WorkingDirectory = gameFolderPath,
                    UseShellExecute = true
                });
                Application.Current.Shutdown();
            }
            else
            {
                string[] possiblePaths = Directory.GetFiles(gameFolderPath, GameExecutableName, SearchOption.AllDirectories);
                if (possiblePaths.Length > 0)
                {
                    string actualExePath = possiblePaths[0];
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = actualExePath,
                        Arguments = args,
                        WorkingDirectory = Path.GetDirectoryName(actualExePath),
                        UseShellExecute = true
                    });
                    Application.Current.Shutdown();
                }
                else
                {
                    MessageBox.Show("Arquivo do jogo não encontrado em:\n" + gameExecutablePath, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SafeDeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;

            try
            {
                string[] processesToKill = {
                    Path.GetFileNameWithoutExtension(GameExecutableName),
                    "UnityCrashHandler64",
                    "UnityCrashHandler32"
                };

                foreach (var procName in processesToKill)
                {
                    foreach (var process in Process.GetProcessesByName(procName))
                    {
                        try 
                        { 
                            process.Kill(); 
                            process.WaitForExit(2000); 
                        } 
                        catch { }
                    }
                }
            }
            catch { }

            try
            {
                var directory = new DirectoryInfo(path);
                foreach (var file in directory.GetFiles("*", SearchOption.AllDirectories))
                {
                    file.Attributes = FileAttributes.Normal;
                }
            }
            catch { }

            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                System.Threading.Thread.Sleep(1000);
                try { Directory.Delete(path, true); } 
                catch 
                {
                    try 
                    {
                        string tempName = path + "_old_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        Directory.Move(path, tempName);
                    }
                    catch { }
                }
            }
        }
    }
}
