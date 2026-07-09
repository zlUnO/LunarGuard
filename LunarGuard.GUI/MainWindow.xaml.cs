using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LunarGuard.Core;
using LunarGuard.Core.Obfuscation;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Brush = System.Windows.Media.Brush;
using MessageBox = System.Windows.MessageBox;
using WF = System.Windows.Forms;

namespace LunarGuard.GUI;

public partial class MainWindow : Window
{
    private string? _selectedFilePath;
    private bool _isProcessing;
    private string? _selectedDirPath;

    public MainWindow()
    {
        InitializeComponent();

        TabHome.Checked += Tab_Checked;
        TabFaq.Checked += Tab_Checked;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.GetPosition(this).Y < 50)
            DragMove();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (HomePanel is null) return;

        var isHome = TabHome.IsChecked == true;
        HomePanel.Visibility = isHome ? Visibility.Visible : Visibility.Collapsed;
        FaqPanel.Visibility = isHome ? Visibility.Collapsed : Visibility.Visible;
        PageTitle.Text = isHome ? "Главная" : "FAQ — настройки обфускации";
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Lua files (*.lua)|*.lua|All files (*.*)|*.*",
            Title = "Выберите .lua файл для обработки"
        };
        if (dialog.ShowDialog() == true)
            SelectFile(dialog.FileName);
    }

    private void BrowseDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WF.FolderBrowserDialog
        {
            Description = "Выберите папку с .lua файлами"
        };
        if (dialog.ShowDialog() == WF.DialogResult.OK)
        {
            _selectedDirPath = dialog.SelectedPath;
            _selectedFilePath = null;
            var files = Directory.GetFiles(_selectedDirPath, "*.lua");
            FilePathText.Text = $"Папка: {Path.GetFileName(_selectedDirPath)}";
            FileSizeText.Text = $"найдено {files.Length} .lua файлов";
            FilePathText.Foreground = (Brush)new BrushConverter().ConvertFrom("#E2F0FD")!;

            SettingsPanel.IsEnabled = true;
            RunBtn.IsEnabled = true;
        }
    }

    private void SelectFile(string path)
    {
        _selectedFilePath = path;
        _selectedDirPath = null;
        var name = Path.GetFileName(path);
        var info = new FileInfo(path);
        var size = info.Length switch
        {
            < 1024 => $"{info.Length} Б",
            < 1024 * 1024 => $"{info.Length / 1024.0:F1} КБ",
            _ => $"{info.Length / (1024.0 * 1024.0):F1} МБ"
        };
        FilePathText.Text = name;
        FileSizeText.Text = $"{size} | {Path.GetDirectoryName(Path.GetFullPath(path))}";
        FilePathText.Foreground = (Brush)new BrushConverter().ConvertFrom("#E2F0FD")!;

        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var outName = $"{Path.GetFileNameWithoutExtension(path)}.obfuscated.lua";
        OutputPathText.Text = Path.Combine(dir, outName);
        OutputPathText.Foreground = (Brush)new BrushConverter().ConvertFrom("#E2F0FD")!;

        SettingsPanel.IsEnabled = true;
        RunBtn.IsEnabled = true;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;
        if (_selectedFilePath is null && _selectedDirPath is null) return;

        _isProcessing = true;
        RunBtn.IsEnabled = false;
        BrowseBtn.IsEnabled = false;

        var options = new ObfuscationOptions
        {
            RenameVariables = ChkRename.IsChecked == true,
            RenamePrefix = TxtRenamePrefix.Text.Trim(),
            EncryptStrings = ChkEncrypt.IsChecked == true,
            StringKey = TxtStringKey.Text.Trim(),
            EncodeNumbers = ChkEncodeNumbers.IsChecked == true,
            InjectDeadCode = ChkDeadCode.IsChecked == true,
            DeadCodeBlocks = int.TryParse(TxtDeadCodeBlocks.Text, out var d) ? d : 5,
            ObfuscateControlFlow = ChkControlFlow.IsChecked == true,
            SplitExpressions = ChkSplitExpr.IsChecked == true,
            AntiDebug = ChkAntiDebug.IsChecked == true,
            Virtualize = ChkVirtualize.IsChecked == true,
            OptimizeAst = ChkOptimize.IsChecked == true,
            SplitStrings = ChkSplitStrings.IsChecked == true,
            OpaquePredicates = ChkOpaque.IsChecked == true,
            AntiTamper = ChkAntiTamper.IsChecked == true,
        };

        if (_selectedDirPath != null)
        {
            await ProcessDirectory(_selectedDirPath, options);
        }
        else if (_selectedFilePath != null)
        {
            await ProcessSingleFile(_selectedFilePath, options);
        }

        _isProcessing = false;
        RunBtn.IsEnabled = _selectedFilePath is not null || _selectedDirPath is not null;
        BrowseBtn.IsEnabled = true;

        await Task.Delay(1500);
        HideProgress();
    }

    private async Task ProcessSingleFile(string filePath, ObfuscationOptions options)
    {
        ShowProgress($"Обработка {Path.GetFileName(filePath)}...", 0);

        try
        {
            var source = await File.ReadAllTextAsync(filePath);

            var result = await Task.Run(() =>
            {
                var processor = new LunarGuardProcessor();
                return processor.Process(source, options);
            });

            var dir = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
            var outName = $"{Path.GetFileNameWithoutExtension(filePath)}.obfuscated.lua";
            var outputPath = Path.Combine(dir, outName);

            await File.WriteAllTextAsync(outputPath, result);

            ShowProgress("Готово!", 100);

            var msg = $"Файл успешно обработан!\n\nСохранено:\n{outputPath}";
            System.Windows.MessageBox.Show(msg, "LunarGuard", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowProgress($"Ошибка: {ex.Message}", 0);
            System.Windows.MessageBox.Show($"Ошибка обработки:\n{ex.Message}", "LunarGuard",
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ProcessDirectory(string dirPath, ObfuscationOptions options)
    {
        var files = Directory.GetFiles(dirPath, "*.lua");
        var success = 0;
        var fail = 0;

        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            var pct = (i * 100) / files.Length;
            ShowProgress($"[{i + 1}/{files.Length}] {Path.GetFileName(file)}...", pct);

            try
            {
                var source = await File.ReadAllTextAsync(file);
                var result = await Task.Run(() =>
                {
                    var processor = new LunarGuardProcessor();
                    return processor.Process(source, options);
                });

                var outName = $"{Path.GetFileNameWithoutExtension(file)}.obfuscated.lua";
                var outputPath = Path.Combine(dirPath, outName);
                await File.WriteAllTextAsync(outputPath, result);
                success++;
            }
            catch
            {
                fail++;
            }
        }

        ShowProgress($"Готово: {success} OK, {fail} ошибок", 100);
        var msg = $"Обработано файлов: {success}\nОшибок: {fail}";
        System.Windows.MessageBox.Show(msg, "LunarGuard", MessageBoxButton.OK,
            fail > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private object? _updateBtnOriginal;

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        _updateBtnOriginal ??= UpdateBtn.Content;
        UpdateBtn.IsEnabled = false;

        try
        {
            UpdateBtn.Content = "Хеширование...";
            var localHash = ComputeLocalHash();

            UpdateBtn.Content = "Загрузка с GitHub...";
            (var remoteHash, var latestVersion, var downloadUrl) = await FetchLatestReleaseAsync();

            if (remoteHash.Length > 0 && ConstantTimeEquals(localHash, remoteHash))
            {
                System.Windows.MessageBox.Show("Установлена последняя версия LunarGuard.",
                                "Обновлений нет", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var msg = remoteHash.Length == 0
                    ? $"Доступна новая версия: {latestVersion}\n\nХотите перейти на страницу загрузки?"
                    : $"Доступна новая версия: {latestVersion}\n\nХеш отличается от текущей.\nХотите перейти на страницу загрузки?";
                var result = System.Windows.MessageBox.Show(msg, "Обновление найдено",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes && Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
                    && uri.Scheme is "https" or "http" && IsTrustedHost(uri.Host))
                    Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
            }
        }
        catch (HttpRequestException)
        {
            System.Windows.MessageBox.Show("Не удалось проверить обновления.\nПроверьте подключение к интернету.",
                            "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Ошибка проверки обновлений:\n{ex.Message}",
                            "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            UpdateBtn.IsEnabled = true;
            UpdateBtn.Content = _updateBtnOriginal;
        }
    }

    private static string ComputeLocalHash()
    {
        var path = Environment.ProcessPath!;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLower();
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static async Task<(string Hash, string Version, string DownloadUrl)> FetchLatestReleaseAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LunarGuard/2.0.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        var json = await client.GetStringAsync(
            "https://api.github.com/repos/zlUnO/LunarGuard/releases/latest");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tagName = root.GetProperty("tag_name").GetString()
            ?? throw new InvalidOperationException("Не удалось прочитать информацию о версии.");
        var version = tagName.TrimStart('v');

        var downloadUrl = "https://github.com/zlUnO/LunarGuard/releases";
        if (root.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
        {
            var url = assets[0].GetProperty("browser_download_url").GetString();
            if (!string.IsNullOrEmpty(url))
                downloadUrl = url;
        }

        return await DownloadAndHashAsync(client, downloadUrl, version);
    }

    private static async Task<(string Hash, string Version, string DownloadUrl)> DownloadAndHashAsync(
        HttpClient client, string downloadUrl, string version)
    {
        const long maxDownloadSize = 100L * 1024 * 1024;

        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength > maxDownloadSize)
            throw new InvalidOperationException(
                $"Release file too large ({contentLength:N0} bytes). Maximum allowed: 100 MiB.");

        var tempDir = Path.Combine(Path.GetTempPath(), "LunarGuard");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var fs = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096))
            {
                await response.Content.CopyToAsync(fs);
            }

            await using var stream = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLower();
            return (hash, version, downloadUrl);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    private static bool IsTrustedHost(string host)
    {
        return host is "github.com" or "api.github.com" or "raw.githubusercontent.com"
            or "objects.githubusercontent.com" or "github.githubassets.com";
    }

    private void ShowProgress(string status, int percent)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressStatusText.Text = status;
            ProgressPercentText.Text = $"{percent}%";

            var parent = ProgressFill.Parent as FrameworkElement;
            var parentWidth = parent?.ActualWidth ?? 400;
            ProgressFill.Width = parentWidth * percent / 100;
        });
    }

    private void HideProgress()
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ProgressFill.Width = 0;
        });
    }
}
