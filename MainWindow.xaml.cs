using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MonkeModCleaner
{
    public sealed partial class MainWindow : Window
    {
        private static readonly FontFamily _font = new("Segoe UI Variable Display");
        private static readonly HttpClient _http = new();
        private readonly List<(string Ts, string Msg)> _consoleLogs = new();

        private bool _isDarkMode = true;
        private bool _isRunning;
        private string _installMode = "Standard";
        private int _autoClearLimit;
        private CancellationTokenSource? _cts;

        private ProgressBar? _progressBar;
        private TextBlock? _statusText;
        private Button? _startButton;
        private RadioButton? _standardRadio;
        private RadioButton? _fullRadio;
        private RadioButton? _bepinexRadio;

        private SolidColorBrush _cardBg = null!;
        private SolidColorBrush _cardBorder = null!;
        private SolidColorBrush _textPri = null!;
        private SolidColorBrush _textSec = null!;

        private static readonly string DataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonkeModCleaner");
        private static readonly string SettingsFile = Path.Combine(DataFolder, "settings.json");

        private sealed class SettingsData
        {
            public bool IsDarkMode { get; set; } = true;
            public int AutoClearLimit { get; set; }
            public string InstallMode { get; set; } = "Standard";
        }

        private static readonly (string Label, int Count)[] AutoClearOptions =
        {
            ("Never", 0), ("50 entries", 50), ("100 entries", 100),
            ("250 entries", 250), ("500 entries", 500), ("1000 entries", 1000),
        };

        private static readonly string[] KnownBepInExFolders = { "BepInEx" };

        private static readonly string[] KnownBepInExRootFiles = {
            "winhttp.dll", "doorstop_config.ini", ".doorstop_version",
            "changelog.txt", "BepInEx.cfg", "libdoorstop.so", "run_bepinex.sh"
        };

        static MainWindow()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("MonkeModCleaner/1.0");
        }

        public MainWindow()
        {
            InitializeComponent();
            AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 620));
            AppWindow.SetIcon("MonkeModCleaner.ico");
            SystemBackdrop = new DesktopAcrylicBackdrop();

            if (AppWindow.Presenter is OverlappedPresenter p)
            {
                p.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
                p.PreferredMinimumWidth = 900;
                p.PreferredMinimumHeight = 620;
            }

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            LoadSettings();
            UpdateBrushes();
            ApplyTheme();

            AppNavView.SelectedItem = AppNavView.MenuItems[0];
            ShowInstallPage();

            AppWindow.Closing += async (_, e) =>
            {
                if (!_isRunning) return;
                e.Cancel = true;
                var dialog = new ContentDialog
                {
                    Title = "Task In Progress",
                    Content = "Closing the app during tasks can corrupt your Gorilla Tag install.\n\nWould you still like to close the app?",
                    PrimaryButtonText = "Close Anyway",
                    CloseButtonText = "Cancel",
                    XamlRoot = RootGrid.XamlRoot
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    _cts?.Cancel();
                    _isRunning = false;
                    Application.Current.Exit();
                }
            };
        }

        private void TitleCloseBtn_Click(object s, RoutedEventArgs e) => Close();
        private void TitleMinBtn_Click(object s, RoutedEventArgs e)
        {
            if (AppWindow.Presenter is OverlappedPresenter p) p.Minimize();
        }
        private void TitleMaxBtn_Click(object s, RoutedEventArgs e)
        {
            if (AppWindow.Presenter is OverlappedPresenter p)
            {
                if (p.State == OverlappedPresenterState.Maximized) p.Restore();
                else p.Maximize();
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                var s = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(SettingsFile));
                if (s == null) return;
                _isDarkMode = s.IsDarkMode;
                _autoClearLimit = s.AutoClearLimit;
                _installMode = s.InstallMode;
            }
            catch { }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(DataFolder);
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new SettingsData
                {
                    IsDarkMode = _isDarkMode,
                    AutoClearLimit = _autoClearLimit,
                    InstallMode = _installMode
                }));
            }
            catch { }
        }

        private void UpdateBrushes()
        {
            if (_isDarkMode)
            {
                _cardBg = new SolidColorBrush(ColorHelper.FromArgb(32, 255, 255, 255));
                _cardBorder = new SolidColorBrush(ColorHelper.FromArgb(65, 255, 255, 255));
                _textPri = new SolidColorBrush(Colors.White);
                _textSec = new SolidColorBrush(ColorHelper.FromArgb(180, 255, 255, 255));
            }
            else
            {
                _cardBg = new SolidColorBrush(ColorHelper.FromArgb(160, 255, 255, 255));
                _cardBorder = new SolidColorBrush(ColorHelper.FromArgb(90, 0, 0, 0));
                _textPri = new SolidColorBrush(Colors.Black);
                _textSec = new SolidColorBrush(ColorHelper.FromArgb(160, 0, 0, 0));
            }
        }

        private static void SetBrush(ResourceDictionary res, string key, Windows.UI.Color c)
        {
            if (res.TryGetValue(key, out var v) && v is SolidColorBrush b) b.Color = c;
            else res[key] = new SolidColorBrush(c);
        }

        private void ApplyTheme()
        {
            var theme = _isDarkMode ? ElementTheme.Dark : ElementTheme.Light;
            RootGrid.RequestedTheme = theme;
            AppNavView.RequestedTheme = theme;

            UpdateBrushes();

            RootGrid.Background = new SolidColorBrush(
                _isDarkMode ? ColorHelper.FromArgb(160, 0, 0, 0) : ColorHelper.FromArgb(100, 245, 245, 250));

            var pane = _isDarkMode ? ColorHelper.FromArgb(252, 16, 16, 16) : ColorHelper.FromArgb(252, 222, 222, 230);
            var paneDefault = _isDarkMode ? ColorHelper.FromArgb(250, 16, 16, 16) : ColorHelper.FromArgb(250, 222, 222, 230);

            AppTitleBar.Background = new SolidColorBrush(pane);

            var res = AppNavView.Resources;
            SetBrush(res, "NavigationViewDefaultPaneBackground", paneDefault);
            SetBrush(res, "NavigationViewExpandedPaneBackground", pane);
            SetBrush(res, "NavigationViewTopPaneBackground", pane);

            byte b0 = _isDarkMode ? (byte)255 : (byte)0;
            SetBrush(res, "NavigationViewItemBackgroundPointerOver", ColorHelper.FromArgb(_isDarkMode ? (byte)18 : (byte)28, b0, b0, b0));
            SetBrush(res, "NavigationViewItemBackgroundPressed", ColorHelper.FromArgb(_isDarkMode ? (byte)8 : (byte)42, b0, b0, b0));
            SetBrush(res, "NavigationViewItemBackgroundSelected", ColorHelper.FromArgb(_isDarkMode ? (byte)22 : (byte)18, b0, b0, b0));
            SetBrush(res, "NavigationViewItemBackgroundSelectedPointerOver", ColorHelper.FromArgb(_isDarkMode ? (byte)28 : (byte)32, b0, b0, b0));

            AppTitleText.Foreground = _textPri;
            AppSubtitleText.Foreground = _textSec;
            PageTitleText.Foreground = _textPri;
            PageSubtitleText.Foreground = _textSec;

            if (AppNavView.SelectedItem is NavigationViewItem sel)
                NavigateTo(sel.Tag?.ToString() ?? "");
        }

        private async Task TransitionToPage(Action buildPage)
        {
            var eIn = new CubicEase { EasingMode = EasingMode.EaseIn };
            var eOut = new CubicEase { EasingMode = EasingMode.EaseOut };

            var sbOut = new Storyboard();
            Anim(sbOut, PageContentHost, "Opacity", 1, 0, 80, eIn);
            Anim(sbOut, PageContentTransform, "Y", 0, 8, 80, eIn);
            Anim(sbOut, PageTitleText, "Opacity", 1, 0, 60, eIn);
            Anim(sbOut, PageSubtitleText, "Opacity", 1, 0, 60, eIn);
            sbOut.Begin();

            await Task.Delay(90);
            buildPage();

            PageContentTransform.Y = -10;
            PageTitleTransform.Y = -6;
            PageSubtitleTransform.Y = -4;

            var sbIn = new Storyboard();
            Anim(sbIn, PageTitleText, "Opacity", 0, 1, 250, eOut);
            Anim(sbIn, PageTitleTransform, "Y", -6, 0, 250, eOut);
            Anim(sbIn, PageSubtitleText, "Opacity", 0, 1, 200, eOut, 50);
            Anim(sbIn, PageSubtitleTransform, "Y", -4, 0, 200, eOut, 50);
            Anim(sbIn, PageContentHost, "Opacity", 0, 1, 280, eOut, 80);
            Anim(sbIn, PageContentTransform, "Y", -10, 0, 280, eOut, 80);
            sbIn.Begin();
        }

        private static void Anim(Storyboard sb, DependencyObject target, string prop,
            double from, double to, int ms, EasingFunctionBase ease, int delay = 0)
        {
            var a = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(ms),
                EasingFunction = ease
            };
            if (delay > 0) a.BeginTime = TimeSpan.FromMilliseconds(delay);
            Storyboard.SetTarget(a, target);
            Storyboard.SetTargetProperty(a, prop);
            sb.Children.Add(a);
        }

        private void AnimateCardsIn()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            int delay = 0;
            foreach (var child in PageContentHost.Children)
            {
                if (child is not Border card) continue;
                card.Opacity = 0;
                card.RenderTransform = new TranslateTransform { Y = 12 };
                var sb = new Storyboard();
                Anim(sb, card, "Opacity", 0, 1, 300, ease, delay);
                Anim(sb, card.RenderTransform, "Y", 12, 0, 300, ease, delay);
                sb.Begin();
                delay += 60;
            }
        }

        private void NavigateTo(string tag)
        {
            switch (tag)
            {
                case "Install": ShowInstallPage(); break;
                case "Console": ShowConsolePage(); break;
                case "Settings": ShowSettingsPage(); break;
            }
            AnimateCardsIn();
        }

        private void AppNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer?.Tag?.ToString() is string tag)
                _ = TransitionToPage(() => NavigateTo(tag));
        }

        private void ShowInstallPage()
        {
            PageTitleText.Text = "Install";
            PageSubtitleText.Text = "Clean up your Gorilla Tag mods and get a fresh BepInEx setup.";
            PageContentHost.Children.Clear();

            var modeStack = MakeStack();
            modeStack.Children.Add(MakeTitle("Install Mode"));
            modeStack.Children.Add(MakeText("Choose how you want to reset your modded Gorilla Tag install."));

            modeStack.Children.Add(MakeRadioRow("Standard", "Standard Reset",
                "Removes BepInEx and known mod files, verifies game files through Steam, then downloads and installs the latest BepInEx. Faster, preserves base game download."));

            modeStack.Children.Add(MakeRadioRow("Full", "Full Reset",
                "Completely deletes the entire Gorilla Tag folder and redownloads everything through Steam, then downloads and installs the latest BepInEx from scratch. Slower, but guarantees a 100% clean install — removes even files from niche mod menus."));

            modeStack.Children.Add(MakeRadioRow("BepInEx", "BepInEx Reset",
                "Removes any existing BepInEx installation and all associated files, then downloads and installs the latest BepInEx release fresh. Use this if your game files are fine and you just need a clean BepInEx setup for modding."));

            PageContentHost.Children.Add(WrapCard(modeStack));

            var actionStack = MakeStack();
            _statusText = new TextBlock
            {
                Text = "Ready",
                Opacity = 0.7,
                Foreground = _textPri,
                FontFamily = _font,
                FontSize = 13
            };
            actionStack.Children.Add(_statusText);

            _progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Height = 4,
                CornerRadius = new CornerRadius(2)
            };
            actionStack.Children.Add(_progressBar);

            _startButton = MakeButton("Start");
            _startButton.Click += StartButton_Click;
            actionStack.Children.Add(_startButton);
            PageContentHost.Children.Add(WrapCard(actionStack));
        }

        private Grid MakeRadioRow(string mode, string label, string tooltip)
        {
            var grid = new Grid { Height = 32 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var radio = new RadioButton
            {
                GroupName = "Mode",
                IsChecked = _installMode == mode,
                MinWidth = 0,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            radio.Checked += (_, _) => { _installMode = mode; SaveSettings(); };

            if (mode == "Standard") _standardRadio = radio;
            else if (mode == "Full") _fullRadio = radio;
            else if (mode == "BepInEx") _bepinexRadio = radio;

            Grid.SetColumn(radio, 0);
            grid.Children.Add(radio);

            var text = new TextBlock
            {
                Text = label,
                FontFamily = _font,
                Foreground = _textPri,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            text.PointerPressed += (_, _) => radio.IsChecked = true;
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var info = new FontIcon
            {
                Glyph = "\uE946",
                FontSize = 14,
                Foreground = _textSec,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            ToolTipService.SetToolTip(info, new ToolTip { Content = tooltip, MaxWidth = 350 });
            Grid.SetColumn(info, 2);
            grid.Children.Add(info);

            return grid;
        }

        private async void StartButton_Click(object s, RoutedEventArgs e)
        {
            if (_isRunning) return;

            string? steamPath = FindSteamPath();
            string? gamePath = steamPath != null ? FindGorillaTag(steamPath) : null;

            if (_installMode == "BepInEx" && gamePath != null &&
                Directory.Exists(Path.Combine(gamePath, "BepInEx")))
            {
                var confirm = new ContentDialog
                {
                    Title = "Are you sure?",
                    Content = "This will completely remove your current BepInEx installation and all associated files, including plugins, configs, and patchers, then install the latest version fresh.",
                    PrimaryButtonText = "Continue",
                    CloseButtonText = "Cancel",
                    XamlRoot = RootGrid.XamlRoot
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
            }

            _isRunning = true;
            _cts = new CancellationTokenSource();
            SetControlsEnabled(false);

            try
            {
                await RunCleanProcess(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Cancelled.", 0);
                AddLog("Operation cancelled by user.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}", _progressBar!.Value);
                AddLog($"Error: {ex.Message}");
            }
            finally
            {
                _isRunning = false;
                SetControlsEnabled(true);
            }
        }

        private void SetControlsEnabled(bool enabled)
        {
            if (_startButton != null) _startButton.IsEnabled = enabled;
            if (_standardRadio != null) _standardRadio.IsEnabled = enabled;
            if (_fullRadio != null) _fullRadio.IsEnabled = enabled;
            if (_bepinexRadio != null) _bepinexRadio.IsEnabled = enabled;
        }

        private void RemoveBepInExFiles(string gamePath)
        {
            AddLog("Scanning for BepInEx folders...");
            foreach (var folder in KnownBepInExFolders)
            {
                string fp = Path.Combine(gamePath, folder);
                if (!Directory.Exists(fp)) continue;
                AddLog($"Deleting folder: {folder}");
                try { Directory.Delete(fp, true); }
                catch (Exception ex) { AddLog($"Failed to delete {folder}: {ex.Message}"); }
            }

            AddLog("Scanning for BepInEx root files...");
            foreach (var file in KnownBepInExRootFiles)
            {
                string fp = Path.Combine(gamePath, file);
                if (!File.Exists(fp)) continue;
                AddLog($"Deleting file: {file}");
                try { File.Delete(fp); }
                catch (Exception ex) { AddLog($"Failed to delete {file}: {ex.Message}"); }
            }

            foreach (var file in Directory.GetFiles(gamePath, "*.log"))
            {
                string name = Path.GetFileName(file);
                AddLog($"Deleting log file: {name}");
                try { File.Delete(file); } catch { }
            }
        }

        private async Task RunCleanProcess(CancellationToken ct)
        {
            AddLog($"Starting {_installMode} Reset mode...");

            UpdateStatus("Locating Steam installation...", 0);
            AddLog("Searching Windows registry for Steam path...");
            string? steamPath = FindSteamPath();
            if (steamPath == null)
            {
                AddLog("Steam installation not found in registry.");
                UpdateStatus("Steam installation not found.", 0);
                return;
            }
            AddLog($"Steam found at: {steamPath}");
            ct.ThrowIfCancellationRequested();

            UpdateStatus("Searching for Gorilla Tag...", 10);
            AddLog("Scanning Steam library folders for Gorilla Tag (AppID 1533390)...");
            string? gamePath = FindGorillaTag(steamPath);
            if (gamePath == null)
            {
                AddLog("Gorilla Tag not found in any Steam library.");
                UpdateStatus("Gorilla Tag not found in any Steam library.", 10);
                return;
            }
            AddLog($"Gorilla Tag found at: {gamePath}");
            ct.ThrowIfCancellationRequested();

            if (_installMode == "BepInEx")
            {
                UpdateStatus("Removing existing BepInEx files...", 15);
                RemoveBepInExFiles(gamePath);
                ct.ThrowIfCancellationRequested();

                await DownloadAndInstallBepInEx(gamePath, 35, 70, ct);

                AddLog("BepInEx Reset complete.");
                UpdateStatus("Done! Launch Gorilla Tag to finish generating required BepInEx files, then your game is ready to mod.", 100);
                return;
            }

            if (_installMode == "Full")
            {
                UpdateStatus("Deleting entire game folder...", 20);
                AddLog($"Deleting: {gamePath}");
                try { Directory.Delete(gamePath, true); }
                catch (Exception ex) { AddLog($"Warning: {ex.Message}"); }
                AddLog("Game folder deleted.");
            }
            else
            {
                UpdateStatus("Removing BepInEx and known mod files...", 20);
                RemoveBepInExFiles(gamePath);
            }
            ct.ThrowIfCancellationRequested();

            UpdateStatus("Verifying game files through Steam...", 35);
            AddLog("Launching Steam game file verification (AppID 1533390)...");
            Process.Start(new ProcessStartInfo
            {
                FileName = "steam://validate/1533390",
                UseShellExecute = true
            });
            AddLog("Steam verification triggered. Waiting for user confirmation...");

            var waitDialog = new ContentDialog
            {
                Title = "Waiting for Steam",
                Content = "Steam is verifying or redownloading your Gorilla Tag files.\n\nClick Continue once Steam has finished.",
                PrimaryButtonText = "Continue",
                XamlRoot = RootGrid.XamlRoot
            };
            await waitDialog.ShowAsync();
            AddLog("User confirmed Steam verification complete.");
            ct.ThrowIfCancellationRequested();

            if (!Directory.Exists(gamePath))
                Directory.CreateDirectory(gamePath);

            await DownloadAndInstallBepInEx(gamePath, 55, 80, ct);

            AddLog($"{_installMode} Reset complete.");
            UpdateStatus("Done! Launch Gorilla Tag to finish generating required BepInEx files, then your game is ready to mod.", 100);
        }

        private async Task DownloadAndInstallBepInEx(string gamePath, double dlProgress, double installProgress, CancellationToken ct)
        {
            UpdateStatus("Downloading latest BepInEx from GitHub...", dlProgress);
            AddLog("Fetching latest BepInEx release info from GitHub API...");
            string zipPath = Path.Combine(Path.GetTempPath(), "BepInEx_Latest.zip");
            await DownloadLatestBepInEx(zipPath, ct);
            AddLog("BepInEx downloaded successfully.");
            ct.ThrowIfCancellationRequested();

            UpdateStatus("Installing BepInEx...", installProgress);
            AddLog($"Extracting BepInEx to: {gamePath}");
            ZipFile.ExtractToDirectory(zipPath, gamePath, true);

            string changelogPath = Path.Combine(gamePath, "changelog.txt");
            if (File.Exists(changelogPath))
            {
                try { File.Delete(changelogPath); AddLog("Removed BepInEx changelog.txt from game folder."); }
                catch { }
            }

            string pluginsPath = Path.Combine(gamePath, "BepInEx", "plugins");
            if (!Directory.Exists(pluginsPath))
            {
                Directory.CreateDirectory(pluginsPath);
                AddLog("Created BepInEx plugins folder.");
            }

            try { File.Delete(zipPath); } catch { }
            AddLog("Temporary zip file cleaned up.");
        }

        private void UpdateStatus(string text, double progress)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_statusText != null) _statusText.Text = text;
                if (_progressBar != null) _progressBar.Value = progress;
            });
        }

        private void AddLog(string msg)
        {
            if (_autoClearLimit > 0 && _consoleLogs.Count >= _autoClearLimit)
                _consoleLogs.Clear();

            _consoleLogs.Add((DateTime.Now.ToString("HH:mm:ss"), msg));

            if (AppNavView.SelectedItem is NavigationViewItem { Tag: "Console" })
                DispatcherQueue.TryEnqueue(() => ShowConsolePage());
        }

        private static string? FindSteamPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                return (key?.GetValue("SteamPath") as string)?.Replace('/', '\\');
            }
            catch { return null; }
        }

        private static string? FindGorillaTag(string steamPath)
        {
            var libraryPaths = new List<string> { Path.Combine(steamPath, "steamapps") };

            string libFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libFile))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(libFile), "\"path\"\\s+\"(.+?)\""))
                    libraryPaths.Add(Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps"));
            }

            foreach (var lib in libraryPaths)
            {
                if (!File.Exists(Path.Combine(lib, "appmanifest_1533390.acf"))) continue;
                string gp = Path.Combine(lib, "common", "Gorilla Tag");
                if (Directory.Exists(gp)) return gp;
            }
            return null;
        }

        private static async Task DownloadLatestBepInEx(string destPath, CancellationToken ct)
        {
            var json = await _http.GetStringAsync("https://api.github.com/repos/BepInEx/BepInEx/releases/latest", ct);
            string? url = null;
            foreach (var asset in JsonDocument.Parse(json).RootElement.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                if (name.Contains("win") && name.Contains("x64") && name.EndsWith(".zip"))
                {
                    url = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
            if (url == null) throw new Exception("Could not find BepInEx Windows x64 download on GitHub.");

            var bytes = await _http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(destPath, bytes, ct);
        }

        private void ShowConsolePage()
        {
            PageTitleText.Text = "Console";
            PageSubtitleText.Text = "Live log of MonkeModCleaner activity.";
            PageContentHost.Children.Clear();

            var logStack = MakeStack();
            if (_consoleLogs.Count == 0)
                logStack.Children.Add(MakeText("No activity yet. Run an install to see logs here."));
            else
                foreach (var (ts, msg) in _consoleLogs)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    row.Children.Add(new TextBlock
                    {
                        Text = $"[{ts}]",
                        Opacity = 0.32,
                        Foreground = _textPri,
                        FontFamily = _font,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    row.Children.Add(new TextBlock
                    {
                        Text = msg,
                        Opacity = 0.85,
                        Foreground = _textPri,
                        FontFamily = _font,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    });
                    logStack.Children.Add(row);
                }
            PageContentHost.Children.Add(WrapCard(logStack));

            var ctrlStack = MakeStack();
            ctrlStack.Children.Add(MakeText(
                $"{_consoleLogs.Count} log {(_consoleLogs.Count == 1 ? "entry" : "entries")} stored in memory."));
            var clearBtn = MakeButton("Clear Logs");
            clearBtn.Click += (_, _) => { _consoleLogs.Clear(); ShowConsolePage(); AnimateCardsIn(); };
            ctrlStack.Children.Add(clearBtn);
            PageContentHost.Children.Add(WrapCard(ctrlStack));
        }

        private void ShowSettingsPage()
        {
            PageTitleText.Text = "Settings";
            PageSubtitleText.Text = "Customize your MonkeModCleaner experience.";
            PageContentHost.Children.Clear();

            var appearStack = MakeStack();
            appearStack.Children.Add(MakeTitle("Appearance"));
            var themeToggle = new ToggleSwitch
            {
                Header = "Light Mode",
                IsOn = !_isDarkMode,
                Foreground = _textPri,
                FontFamily = _font
            };
            themeToggle.Toggled += (_, _) => { _isDarkMode = !themeToggle.IsOn; ApplyTheme(); SaveSettings(); };
            appearStack.Children.Add(themeToggle);
            PageContentHost.Children.Add(WrapCard(appearStack));

            var consStack = MakeStack();
            consStack.Children.Add(MakeTitle("Console"));
            consStack.Children.Add(MakeText(
                "Automatically clear log entries when the count exceeds the limit. \"Never\" keeps all entries until the app is closed."));

            string curClearLbl = AutoClearOptions.FirstOrDefault(x => x.Count == _autoClearLimit).Label ?? "Never";
            var clearBtn = MakeDropDown(curClearLbl, 150);
            clearBtn.Flyout = BuildFlyout(AutoClearOptions.Select(x => x.Label).ToArray(), sel =>
            {
                _autoClearLimit = AutoClearOptions.First(x => x.Label == sel).Count;
                clearBtn.Content = sel; SaveSettings();
            });
            var clearRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            clearRow.Children.Add(new TextBlock
            {
                Text = "Auto-clear after",
                Foreground = _textPri,
                FontFamily = _font,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.85
            });
            clearRow.Children.Add(clearBtn);
            consStack.Children.Add(clearRow);
            PageContentHost.Children.Add(WrapCard(consStack));
        }

        private Border WrapCard(UIElement content) => new()
        {
            Background = _cardBg,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24),
            BorderBrush = _cardBorder,
            BorderThickness = new Thickness(1.5),
            Margin = new Thickness(0, 0, 0, 12),
            Child = content
        };

        private StackPanel MakeStack() => new() { Spacing = 12 };

        private TextBlock MakeTitle(string t) => new()
        {
            Text = t,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _textPri,
            FontFamily = _font
        };

        private TextBlock MakeText(string t) => new()
        { Text = t, Opacity = 0.8, Foreground = _textPri, FontFamily = _font, TextWrapping = TextWrapping.Wrap };

        private Button MakeButton(string label) => new()
        {
            Content = label,
            Background = _cardBg,
            Foreground = _textPri,
            BorderBrush = _cardBorder,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            FontFamily = _font,
            Padding = new Thickness(16, 8, 16, 8)
        };

        private DropDownButton MakeDropDown(string label, double width) => new()
        {
            Content = label,
            Width = width,
            CornerRadius = new CornerRadius(10),
            Background = _cardBg,
            BorderThickness = new Thickness(1.5),
            BorderBrush = _cardBorder,
            Foreground = _textPri,
            FontFamily = _font,
            VerticalAlignment = VerticalAlignment.Center
        };

        private MenuFlyout BuildFlyout(string[] options, Action<string> onSelect)
        {
            var flyout = new MenuFlyout();
            foreach (var opt in options)
            {
                var item = new MenuFlyoutItem { Text = opt, FontFamily = _font };
                item.Click += (_, _) => onSelect(opt);
                flyout.Items.Add(item);
            }
            return flyout;
        }
    }
}