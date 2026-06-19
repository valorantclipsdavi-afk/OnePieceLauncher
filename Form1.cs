using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace OnePieceLauncher
{
    public partial class Form1 : Form
    {
        // ==========================================
        // CONFIGURAÇÕES DO JOGO - MUDE AQUI!
        // ==========================================
        private const string GitHubUser = "valorantclipsdavi-afk"; 
        private const string GitHubRepo = "OnePieceGame";
        private const string GitHubLauncherRepo = "OnePieceLauncher";
        private const string GameExecutableName = "OnePieceGame.exe";
        
        // ==========================================

        private static readonly Version CurrentLauncherVersion = new Version("0.0.23");

        private Label lblStatus;
        private ProgressBar progressBar;
        private Button btnPlay;
        private string githubApiUrl => $"https://api.github.com/repos/{GitHubUser}/{GitHubRepo}/releases/latest";
        private string githubLauncherApiUrl => $"https://api.github.com/repos/{GitHubUser}/{GitHubLauncherRepo}/releases/latest";

        private string gameFolderPath;
        private string versionFilePath;
        private string zipFilePath;
        private string gameExecutablePath;
        private string usernameFilePath;
        
        private TextBox txtUsername;

        public Form1()
        {
            InitializeUI();
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

            // Tenta deletar atualizador antigo se existir
            string oldBatch = Path.Combine(baseFolder, "update_launcher.bat");
            if (File.Exists(oldBatch))
            {
                try { File.Delete(oldBatch); } catch { }
            }
        }

        private void InitializeUI()
        {
            this.Text = $"One Piece - Atualizador (v{CurrentLauncherVersion.ToString(3)})";
            this.Size = new Size(400, 200);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            lblStatus = new Label
            {
                Text = "Verificando atualizações...",
                Location = new Point(20, 20),
                AutoSize = true,
                Font = new Font("Arial", 10, FontStyle.Regular)
            };

            progressBar = new ProgressBar
            {
                Location = new Point(20, 60),
                Size = new Size(340, 25),
                Style = ProgressBarStyle.Continuous
            };

            Label lblUser = new Label
            {
                Text = "Nome:",
                Location = new Point(20, 118),
                AutoSize = true,
                Font = new Font("Arial", 10, FontStyle.Regular)
            };

            txtUsername = new TextBox
            {
                Location = new Point(70, 115),
                Size = new Size(130, 25),
                Font = new Font("Arial", 10, FontStyle.Regular)
            };

            if (File.Exists(usernameFilePath))
            {
                txtUsername.Text = File.ReadAllText(usernameFilePath).Trim();
            }

            btnPlay = new Button
            {
                Text = "Jogar",
                Location = new Point(210, 110),
                Size = new Size(100, 35),
                Enabled = false,
                Font = new Font("Arial", 10, FontStyle.Bold)
            };
            btnPlay.Click += BtnPlay_Click;

            this.Controls.Add(lblStatus);
            this.Controls.Add(progressBar);
            this.Controls.Add(lblUser);
            this.Controls.Add(txtUsername);
            this.Controls.Add(btnPlay);
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // GitHub requer User-Agent
                    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OnePieceLauncher", "1.0"));

                    // 1. Verificar se há atualização para o Launcher em seu repositório dedicado
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
                                            return; // Retorna para reiniciar o Launcher
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Falha ao checar atualização do launcher não deve impedir o jogo de rodar
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

                // Baixar atualização do launcher
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
                                progressBar.Invoke((MethodInvoker)(() => progressBar.Value = percentage));
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

                // Criar script batch para substituir arquivos após fechar
                string batchPath = Path.Combine(baseFolder, "update_launcher.bat");
                string exeName = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);

                string batchContent = $@"@echo off
timeout /t 2 /nobreak > nul
xcopy ""launcher_temp"" ""."" /y /e /s /i /q > nul
rmdir /s /q ""launcher_temp"" > nul
if exist ""launcher_update.zip"" del /f /q ""launcher_update.zip"" > nul
start """" ""{exeName}""
del ""%~f0""
";
                File.WriteAllText(batchPath, batchContent);

                // Executar o script em background de forma silenciosa
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = batchPath,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = baseFolder
                };
                Process.Start(startInfo);

                Application.Exit();
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
                                progressBar.Invoke((MethodInvoker)(() => progressBar.Value = percentage));
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
                    ZipFile.ExtractToDirectory(zipFilePath, gameFolderPath, overwriteFiles: true);
                });

                // Deleta o ZIP
                File.Delete(zipFilePath);

                // Atualiza a versão
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
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)EnablePlayButton);
                return;
            }
            btnPlay.Enabled = true;
        }

        private void BtnPlay_Click(object sender, EventArgs e)
        {
            string username = txtUsername.Text.Trim();
            if (string.IsNullOrEmpty(username))
            {
                username = "Pirata_" + new Random().Next(1000, 9999);
                txtUsername.Text = username;
            }
            File.WriteAllText(usernameFilePath, username);

            string args = $"-username \"{username}\"";

            if (File.Exists(gameExecutablePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = gameExecutablePath,
                    Arguments = args,
                    WorkingDirectory = gameFolderPath,
                    UseShellExecute = true
                });
                Application.Exit();
            }
            else
            {
                // Tenta encontrar o executável nas subpastas caso o zip tenha vindo com uma pasta raiz
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
                    Application.Exit();
                }
                else
                {
                    MessageBox.Show("Arquivo do jogo não encontrado em:\n" + gameExecutablePath, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void SafeDeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;

            // 1. Tenta fechar processos do jogo e do Unity Crash Handler que travam as DLLs
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

            // 2. Remove atributos de Somente Leitura (Read-Only) de arquivos e subpastas recursivamente
            try
            {
                var directory = new DirectoryInfo(path);
                foreach (var file in directory.GetFiles("*", SearchOption.AllDirectories))
                {
                    file.Attributes = FileAttributes.Normal;
                }
            }
            catch { }

            // 3. Tenta deletar a pasta recursivamente
            try
            {
                Directory.Delete(path, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Se falhar devido a trava de arquivo, espera um momento e tenta de novo
                System.Threading.Thread.Sleep(1000);
                try { Directory.Delete(path, true); } 
                catch 
                {
                    // Falha silenciosa final: renomeia para sair do caminho do extrator
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
