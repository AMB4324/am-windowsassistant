using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace AMWindowsAssistant;

public partial class MainWindow : Window
{
    private const string WindowsDownloadUrl = "https://www.microsoft.com/software-download/windows11";
    private const string UpdateRepositoryFileName = "update-repository.txt";
    private string? _selectedIsoPath;
    private string? _setupPath;
    private string _updateRepository = string.Empty;
    private bool _isBusy;
    private bool _isCheckingForUpdates;
    private FrameworkElement _activePage;

    public MainWindow()
    {
        InitializeComponent();
        _activePage = HomePage;
        AppVersionText.Text = $"Version {GetCurrentVersion()}";
        LoadUpdateRepository();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.15))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        LoaderRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, spin);

        await Task.Delay(1400);

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(360))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (_, _) => StartupOverlay.Visibility = Visibility.Collapsed;
        StartupOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void HomeNav_Click(object sender, RoutedEventArgs e) => NavigateTo(HomePage, HomeNavButton);

    private void TermsNav_Click(object sender, RoutedEventArgs e) => NavigateTo(TermsPage, TermsNavButton);

    private void PrivacyNav_Click(object sender, RoutedEventArgs e) => NavigateTo(PrivacyPage, PrivacyNavButton);

    private void AboutNav_Click(object sender, RoutedEventArgs e) => NavigateTo(AboutPage, AboutNavButton);

    private void SaveUpdateRepository_Click(object sender, RoutedEventArgs e)
    {
        var repository = UpdateRepositoryTextBox.Text.Trim();
        if (!UpdateService.IsValidRepository(repository))
        {
            UpdateConfigStatusText.Text = "Enter a valid GitHub repository in owner/repository format.";
            UpdateConfigStatusText.Foreground = Brushes.DarkRed;
            return;
        }

        try
        {
            var configPath = GetUpdateRepositoryPath();
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            File.WriteAllText(configPath, repository);
            _updateRepository = repository;
            UpdateConfigStatusText.Text = $"Update source saved: {repository}";
            UpdateConfigStatusText.Foreground = new SolidColorBrush(Color.FromRgb(34, 128, 91));
        }
        catch (IOException ex)
        {
            ShowUpdateConfigurationError(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowUpdateConfigurationError(ex);
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_isCheckingForUpdates)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_updateRepository))
        {
            UpdateConfigStatusText.Text = "Add your GitHub owner/repository here, then save it to enable updates.";
            UpdateConfigStatusText.Foreground = new SolidColorBrush(Color.FromRgb(184, 112, 28));
            NavigateTo(AboutPage, AboutNavButton);
            return;
        }

        _isCheckingForUpdates = true;
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = "  Checking...";

        try
        {
            var update = await UpdateService.CheckAsync(_updateRepository, GetCurrentVersion());
            if (update is null)
            {
                UpdateConfigStatusText.Text = $"You’re up to date (version {GetCurrentVersion()}).";
                UpdateConfigStatusText.Foreground = new SolidColorBrush(Color.FromRgb(34, 128, 91));
                MessageBox.Show(this, $"You’re using the latest available version ({GetCurrentVersion()}).",
                    "No update available", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var releaseNotes = string.IsNullOrWhiteSpace(update.ReleaseNotes)
                ? "No release notes were provided."
                : update.ReleaseNotes.Trim();
            if (releaseNotes.Length > 900)
            {
                releaseNotes = releaseNotes[..900] + "…";
            }

            var installUpdate = MessageBox.Show(
                this,
                $"Version {update.Version} is available (you have {GetCurrentVersion()}).\n\n{releaseNotes}\n\nThe update will download from {_updateRepository} and replace this app after it closes. Continue?",
                "Update available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (installUpdate != MessageBoxResult.Yes)
            {
                return;
            }

            CheckUpdatesButton.Content = "  Downloading...";
            UpdateConfigStatusText.Text = $"Downloading version {update.Version}...";
            var updateDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AMWindowsAssistant", "Updates");
            Directory.CreateDirectory(updateDirectory);
            var downloadPath = Path.Combine(updateDirectory, $"AMWindowsAssistant-{Guid.NewGuid():N}.exe");

            try
            {
                await UpdateService.DownloadAsync(update, downloadPath);
                CheckUpdatesButton.Content = "  Preparing...";
                await StartUpdaterAsync(downloadPath);
                Application.Current.Shutdown();
            }
            catch
            {
                if (File.Exists(downloadPath))
                {
                    File.Delete(downloadPath);
                }

                throw;
            }
        }
        catch (HttpRequestException ex)
        {
            ShowUpdateError("Could not contact GitHub.", ex);
        }
        catch (TaskCanceledException ex)
        {
            ShowUpdateError("The update request timed out.", ex);
        }
        catch (JsonException ex)
        {
            ShowUpdateError("GitHub returned an unexpected release response.", ex);
        }
        catch (IOException ex)
        {
            ShowUpdateError("The update file could not be saved or prepared.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowUpdateError("Windows denied access while preparing the update.", ex);
        }
        catch (Win32Exception ex)
        {
            ShowUpdateError("Windows could not start the update helper.", ex);
        }
        catch (InvalidOperationException ex)
        {
            ShowUpdateError("The update could not be installed.", ex);
        }
        finally
        {
            _isCheckingForUpdates = false;
            CheckUpdatesButton.IsEnabled = true;
            CheckUpdatesButton.Content = "  Check for updates";
        }
    }

    private void LoadUpdateRepository()
    {
        var configPath = GetUpdateRepositoryPath();
        if (!File.Exists(configPath))
        {
            return;
        }

        try
        {
            var repository = File.ReadAllText(configPath).Trim();
            if (UpdateService.IsValidRepository(repository))
            {
                _updateRepository = repository;
                UpdateRepositoryTextBox.Text = repository;
                UpdateConfigStatusText.Text = $"Update source saved: {repository}";
            }
            else
            {
                UpdateConfigStatusText.Text = "Saved update source is invalid. Enter owner/repository and save again.";
                UpdateConfigStatusText.Foreground = Brushes.DarkRed;
            }
        }
        catch (IOException ex)
        {
            UpdateConfigStatusText.Text = "The saved update source could not be read.";
            UpdateConfigStatusText.Foreground = Brushes.DarkRed;
            MessageBox.Show(this, $"The saved update source could not be read.\n\n{ex.Message}",
                "Update configuration error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (UnauthorizedAccessException ex)
        {
            UpdateConfigStatusText.Text = "The saved update source could not be read.";
            UpdateConfigStatusText.Foreground = Brushes.DarkRed;
            MessageBox.Show(this, $"The saved update source could not be read.\n\n{ex.Message}",
                "Update configuration error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string GetUpdateRepositoryPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AMWindowsAssistant",
            UpdateRepositoryFileName);

    private static Version GetCurrentVersion()
    {
        var version = typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0);
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    private Task StartUpdaterAsync(string downloadPath)
    {
        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable) || !File.Exists(currentExecutable))
        {
            throw new InvalidOperationException("Windows could not locate the running app executable.");
        }

        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $download = '{{EscapePowerShellString(downloadPath)}}'
            $target = '{{EscapePowerShellString(currentExecutable)}}'
            $parentPid = {{Environment.ProcessId}}
            $directory = [System.IO.Path]::GetDirectoryName($target)
            $token = [Guid]::NewGuid().ToString('N')
            $staged = Join-Path $directory ('.amwa-update-' + $token + '.exe')
            $backup = Join-Path $directory ('.amwa-backup-' + $token + '.exe')

            try {
                $parent = Get-Process -Id $parentPid -ErrorAction SilentlyContinue
                if ($null -ne $parent -and -not $parent.WaitForExit(120000)) {
                    throw 'The app did not close in time to complete the update.'
                }
                if (-not (Test-Path -LiteralPath $download -PathType Leaf) -or
                    -not (Test-Path -LiteralPath $target -PathType Leaf)) {
                    throw 'The downloaded update or current app file could not be found.'
                }

                Copy-Item -LiteralPath $download -Destination $staged
                [System.IO.File]::Replace($staged, $target, $backup, $true)
                try {
                    Start-Process -FilePath $target | Out-Null
                } catch {
                    if (Test-Path -LiteralPath $backup -PathType Leaf) {
                        [System.IO.File]::Replace($backup, $target, $null, $true)
                    }
                    throw
                }

                if (Test-Path -LiteralPath $backup -PathType Leaf) {
                    try {
                        Remove-Item -LiteralPath $backup -Force -ErrorAction Stop
                    } catch {
                        Add-Type -AssemblyName PresentationFramework
                        [System.Windows.MessageBox]::Show(
                            "The update was installed, but the previous-version backup could not be removed: $backup`n$($_.Exception.Message)",
                            'Update installed') | Out-Null
                    }
                }
            } catch {
                Add-Type -AssemblyName PresentationFramework
                [System.Windows.MessageBox]::Show(
                    "The update could not be installed. Your current app should remain available.`n`n$($_.Exception.Message)",
                    'Update failed') | Out-Null
            } finally {
                foreach ($temporaryFile in @($staged, $download)) {
                    if (Test-Path -LiteralPath $temporaryFile -PathType Leaf) {
                        try {
                            Remove-Item -LiteralPath $temporaryFile -Force -ErrorAction Stop
                        } catch {
                            Add-Type -AssemblyName PresentationFramework
                            [System.Windows.MessageBox]::Show(
                                "A temporary update file could not be removed: $temporaryFile`n$($_.Exception.Message)",
                                'Update cleanup warning') | Out-Null
                        }
                    }
                }
            }
            """;

        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encodedCommand}",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var updater = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows could not start the update helper.");
        return Task.CompletedTask;
    }

    private void ShowUpdateConfigurationError(Exception exception)
    {
        UpdateConfigStatusText.Text = "The update repository could not be saved.";
        UpdateConfigStatusText.Foreground = Brushes.DarkRed;
        MessageBox.Show(this, $"The update repository could not be saved.\n\n{exception.Message}",
            "Could not save update source", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ShowUpdateError(string message, Exception exception)
    {
        UpdateConfigStatusText.Text = message;
        UpdateConfigStatusText.Foreground = Brushes.DarkRed;
        MessageBox.Show(this, $"{message}\n\n{exception.Message}", "Update check failed",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void NavigateTo(FrameworkElement page, Button navButton)
    {
        if (ReferenceEquals(page, _activePage))
        {
            return;
        }

        _activePage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        page.Opacity = 0;

        var translate = new TranslateTransform(0, 12);
        page.RenderTransform = translate;

        var duration = TimeSpan.FromMilliseconds(240);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        page.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = easing
        });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, duration)
        {
            EasingFunction = easing
        });

        foreach (var button in new[] { HomeNavButton, TermsNavButton, PrivacyNavButton, AboutNavButton })
        {
            button.Background = Brushes.Transparent;
            button.Foreground = new SolidColorBrush(Color.FromRgb(170, 184, 204));
        }

        navButton.Background = new SolidColorBrush(Color.FromRgb(27, 111, 229));
        navButton.Foreground = Brushes.White;
        _activePage = page;
    }

    private void SelectIso_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a Windows 11 ISO",
            Filter = "Windows disc image (*.iso)|*.iso",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _selectedIsoPath = dialog.FileName;
        _setupPath = null;
        SelectedIsoText.Text = _selectedIsoPath;
        MountIsoButton.IsEnabled = !_isBusy;
        StartSetupButton.IsEnabled = false;
        StatusText.Text = "ISO selected. Mount it to continue.";
    }

    private async void MountIso_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIsoPath is null || !File.Exists(_selectedIsoPath))
        {
            MessageBox.Show(this, "Choose an ISO file that exists on this PC.", "ISO not found",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        StatusText.Text = "Mounting the ISO. Windows may ask for administrator permission.";

        try
        {
            _setupPath = await Task.Run(() => MountIsoAndFindSetup(_selectedIsoPath));
            StartSetupButton.IsEnabled = true;
            StatusText.Text = "The ISO is mounted and Windows Setup is ready.";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            StatusText.Text = "Mount cancelled.";
            MessageBox.Show(this, "Mounting was cancelled. Approve the Windows permission prompt to continue.",
                "Mount cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Win32Exception ex)
        {
            _setupPath = null;
            StatusText.Text = "Could not mount the ISO.";
            MessageBox.Show(this, $"Windows could not start the ISO mounting operation.\n\n{ex.Message}",
                "Mount failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (InvalidOperationException ex)
        {
            _setupPath = null;
            StatusText.Text = "Could not mount the ISO.";
            MessageBox.Show(this, ex.Message, "Mount failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (IOException ex)
        {
            _setupPath = null;
            StatusText.Text = "Could not find Windows Setup in the mounted ISO.";
            MessageBox.Show(this, ex.Message, "Windows Setup not found",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (TimeoutException ex)
        {
            _setupPath = null;
            StatusText.Text = "Mounting took too long.";
            MessageBox.Show(this, ex.Message, "Mount timed out", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void StartSetup_Click(object sender, RoutedEventArgs e)
    {
        if (_setupPath is null || !File.Exists(_setupPath))
        {
            StartSetupButton.IsEnabled = false;
            StatusText.Text = "Mount an ISO to find its setup program.";
            MessageBox.Show(this, "Mount a Windows 11 ISO before starting setup.", "Setup not ready",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Windows Setup will start from:\n{_setupPath}\n\nCompatibility mode attempts to bypass some Windows 11 hardware checks. Setup may still fail, and device compatibility or future updates are not guaranteed. Back up your files first; Setup may restart your PC.\n\nContinue?",
            "Start Windows Setup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _setupPath,
                Arguments = "/product server",
                UseShellExecute = true
            });
            StatusText.Text = "Windows Setup has started in compatibility mode. Follow its instructions to continue.";
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show(this, $"Windows could not start Setup.\n\n{ex.Message}", "Setup could not start",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, $"Windows could not start Setup.\n\n{ex.Message}", "Setup could not start",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = WindowsDownloadUrl,
                UseShellExecute = true
            });
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show(this, $"The Microsoft download page could not be opened.\n\n{ex.Message}",
                "Could not open page", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, $"The Microsoft download page could not be opened.\n\n{ex.Message}",
                "Could not open page", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string MountIsoAndFindSetup(string isoPath)
    {
        var resultPath = Path.Combine(Path.GetTempPath(), $"amwa-{Guid.NewGuid():N}.txt");
        var quotedIsoPath = EscapePowerShellString(isoPath);
        var quotedResultPath = EscapePowerShellString(resultPath);
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            try {
                $image = Mount-DiskImage -ImagePath '{{quotedIsoPath}}' -PassThru -ErrorAction Stop
                $volume = Get-Volume -DiskImage $image |
                    Where-Object { $_.DriveLetter } | Select-Object -First 1
                if ($null -eq $volume) { exit 2 }
                [System.IO.File]::WriteAllText('{{quotedResultPath}}', [string]$volume.DriveLetter)
                exit 0
            } catch {
                exit 1
            }
            """;

        try
        {
            var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encodedCommand}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows could not start the ISO mounting operation.");

            if (!process.WaitForExit(300_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Windows did not finish mounting the ISO within five minutes.");
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Windows could not mount this ISO (mount operation returned code {process.ExitCode}).");
            }

            if (!File.Exists(resultPath))
            {
                throw new IOException("Windows mounted the ISO but did not report its drive letter.");
            }

            var driveLetter = File.ReadAllText(resultPath).Trim();
            if (driveLetter.Length != 1 || !char.IsLetter(driveLetter[0]))
            {
                throw new IOException("Windows mounted the ISO but returned an invalid drive letter.");
            }

            var setupPath = $"{driveLetter}:\\setup.exe";
            if (!File.Exists(setupPath))
            {
                throw new IOException(
                    "The selected ISO mounted, but it does not contain setup.exe at its root. Choose a Windows 11 installation ISO.");
            }

            return setupPath;
        }
        finally
        {
            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }
        }
    }

    private static string EscapePowerShellString(string value) => value.Replace("'", "''");

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        SelectIsoButton.IsEnabled = !isBusy;
        MountIsoButton.IsEnabled = !isBusy && _selectedIsoPath is not null;
        MountIsoButton.Content = isBusy ? "Mounting..." : "Mount ISO";
        MountProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        StartSetupButton.IsEnabled = !isBusy && _setupPath is not null;
    }
}
