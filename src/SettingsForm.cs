using System.Net.Http.Headers;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MadNegar;

internal sealed class SettingsForm : Form
{
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly string _rootDirectory;
    private readonly string _modelsDirectory;
    private readonly AppSettings _settings;
    private readonly ListView _modelsList = new();
    private readonly ThemedComboBox _providerCombo = new();
    private readonly ThemedComboBox _downloadSourceCombo = new();
    private readonly ThemedComboBox _liveModelCombo = new();
    private readonly ThemedComboBox _finalModelCombo = new();
    private readonly Button _openProviderPageButton = new();
    private readonly ThemeToggleButton _themeToggleButton = new();
    private readonly Button _downloadButton = new();
    private readonly Button _cancelDownloadButton = new();
    private readonly Button _refreshDevicesButton = new();
    private readonly Button _startMicTestButton = new();
    private readonly ThemedComboBox _microphoneCombo = new();
    private readonly Label _transcriptionNoteLabel = new();
    private readonly ThemedTrackBar _microphoneBoost = new();
    private readonly ThemedComboBox _audioProfileCombo = new();
    private bool _applyingAudioProfile;
    private readonly Label _microphoneBoostValue = new();
    private readonly ThemedCheckBox _autoLevelCheckBox = new();
    private readonly ThemedCheckBox _reduceRumbleCheckBox = new();
    private readonly ThemedCheckBox _liveTranscriptionCheckBox = new();
    private readonly ThemedCheckBox _fastLivePreviewCheckBox = new();
    private readonly ThemedCheckBox _globalHotkeyCheckBox = new();
    private readonly ThemedCheckBox _directTypingAutoInsertCheckBox = new();
    private readonly ThemedCheckBox _voiceTypingSoundFeedbackCheckBox = new();
    private readonly ThemedComboBox _autoLanguageStrategyCombo = new();
    private readonly ThemedComboBox _transcriptionPreferenceCombo = new();
    private readonly ThemedComboBox _autoMixedPersianModelCombo = new();
    private readonly ThemedComboBox _autoMixedEnglishModelCombo = new();
    private readonly Label _dualModelModeNote = new();
    private readonly Panel _autoLanguageCard = new();
    private readonly Panel _technicalTermsCard = new();
    private readonly Label _autoLanguageModeNote = new();
    private readonly Label _autoLanguageDescription = new();
    private readonly Label _dualModelPersianLabel = new();
    private readonly Label _dualModelEnglishLabel = new();
    private readonly Label _technicalTermsNote = new();
    private readonly ThemedCheckBox _preserveEnglishTechnicalTermsInAutoCheckBox = new();
    private readonly ThemedCheckBox _persianTextNormalizeYehCheckBox = new();
    private readonly ThemedCheckBox _persianTextNormalizeKafCheckBox = new();
    private readonly ThemedCheckBox _persianTextNormalizeHalfSpacesCheckBox = new();
    private readonly ThemedCheckBox _persianTextNormalizePunctuationCheckBox = new();
    private readonly ThemedCheckBox _applyPersianTextDirectTypingCheckBox = new();
    private readonly Label _globalHotkeyShortcutLabel = new();
    private readonly Button _changeGlobalHotkeyButton = new();
    private readonly Button _resetGlobalHotkeyButton = new();
    private readonly Label _globalHotkeyCaptureNote = new();
    private bool _capturingGlobalHotkey;
    private readonly ThemedProgressBar _downloadProgress = new();
    private readonly Label _downloadProgressLabel = new();
    private readonly ThemedProgressBar _microphoneLevel = new();
    private readonly Label _microphoneStatus = new();
    private readonly Panel _titleBar = new();
    private readonly Button _audioNavButton = new();
    private readonly Button _downloadsNavButton = new();
    private readonly Button _transcriptionNavButton = new();
    private readonly Button _persianTextNavButton = new();
    private readonly Button _dictionaryNavButton = new();
    private readonly Button _historyNavButton = new();
    private readonly Button _diagnosticsNavButton = new();
    private readonly Panel _audioPage = new();
    private readonly Panel _downloadsPage = new();
    private readonly Panel _transcriptionPage = new();
    private readonly Panel _persianTextPage = new();
    private readonly Panel _dictionaryPage = new();
    private readonly Panel _historyPage = new();
    private readonly Panel _diagnosticsPage = new();
    private readonly TextBox _diagnosticsReportBox = new();
    private readonly Button _refreshDiagnosticsButton = new();
    private readonly Button _copyDiagnosticsButton = new();
    private readonly ListView _dictionaryList = new();
    private readonly TextBox _dictionaryPhraseInput = new();
    private readonly TextBox _dictionaryReplacementInput = new();
    private readonly ThemedComboBox _dictionaryLanguageCombo = new();
    private readonly ThemedCheckBox _dictionaryApplyToDirectTypingCheckBox = new();
    private readonly Button _dictionaryAddUpdateButton = new();
    private readonly Button _dictionaryRemoveButton = new();
    private readonly Button _dictionaryImportButton = new();
    private readonly Button _dictionaryExportButton = new();
    private readonly ThemedCheckBox _historyEnabledCheckBox = new();
    private readonly Button _deleteAllHistoryButton = new();
    private bool _loadingDictionaryEntries;
    private readonly Button _closeWindowButton = new();
    private readonly ToolTip _titleToolTip = new();
    private readonly Label _selectedModelStatus = new();
    private CancellationTokenSource? _downloadCts;
    private WasapiCapture? _testWaveIn;
    private MMDevice? _testDevice;
    private bool _isDownloading;
    private bool _isTestingMicrophone;
    private bool _isClosing;
    private bool _unsupportedAudioFormatReported;

    private string ModelPath(ModelInfo model) => Path.Combine(_modelsDirectory, model.FileName);
    private string ProviderModelsDirectory => Path.Combine(_rootDirectory, "ProviderModels");
    private ProviderModelInfo? SelectedDownloadItem => _modelsList.SelectedItems.Count == 0
        ? null : _modelsList.SelectedItems[0].Tag as ProviderModelInfo;

    private string DownloadDirectory(ProviderModelInfo item) =>
        Path.Combine(ProviderModelsDirectory, SafePathPart(item.Provider), SafePathPart(item.Id));

    private string ProviderModelDirectory(ProviderModelInfo item)
    {
        var storage = DownloadDirectory(item);
        if (!RequiresKaldiFolder(item) || HasKaldiFolderStructure(storage)) return storage;
        if (Directory.Exists(storage))
        {
            try
            {
                foreach (var amFolder in Directory.GetDirectories(storage, "am", SearchOption.AllDirectories))
                {
                    var candidate = Path.GetDirectoryName(amFolder);
                    if (!string.IsNullOrWhiteSpace(candidate) && HasKaldiFolderStructure(candidate)) return candidate;
                }
            }
            catch { }
        }
        return storage;
    }

    private string CompletionMarker(ProviderModelInfo item) =>
        Path.Combine(DownloadDirectory(item), ModelValidation.ProviderCompletionMarkerName);

    private bool IsDownloaded(ProviderModelInfo item) => item.IsWhisper
        ? ModelValidation.IsWhisperModelReady(ModelPath(item.WhisperModel!))
        : IsProviderModelInstalled(item);

    private bool IsProviderModelInstalled(ProviderModelInfo item)
    {
        var directory = DownloadDirectory(item);
        if (!Directory.Exists(directory)) return false;
        if (RequiresKaldiFolder(item)) return HasKaldiFolderStructure(ProviderModelDirectory(item));
        if (item.Provider == "faster-whisper") return ModelValidation.IsFasterWhisperModelReady(directory);
        return File.Exists(CompletionMarker(item)) && !ModelValidation.HasPartialArtifacts(directory);
    }

    private static bool RequiresKaldiFolder(ProviderModelInfo item) => item.Provider is "Vosk" or "Diba-STT";
    private static bool HasKaldiFolderStructure(string directory) => ModelValidation.HasKaldiModelStructure(directory);
    private static bool HasVoskFolderStructure(string directory) => HasKaldiFolderStructure(directory);

    private static string SafePathPart(string value) => string.Concat(value.Select(ch =>
        char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_'));

    public SettingsForm(string rootDirectory, AppSettings settings)
    {
        _rootDirectory = rootDirectory;
        _modelsDirectory = Path.Combine(rootDirectory, "Models");
        _settings = settings;

        Text = "MadNegar Settings";
        KeyPreview = true;
        KeyDown += HandleGlobalHotkeyCaptureKeyDown;
        ClientSize = new Size(620, 700);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(247, 249, 252);
        ForeColor = Color.FromArgb(30, 41, 59);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;

        Directory.CreateDirectory(_modelsDirectory);
        BuildUi();
        ThemeManager.Apply(this, _settings.ThemeMode);
        PopulateProviders();
        RefreshDownloadRows();
        PopulateTranscriptionModelChoices();
        RefreshMicrophoneDevices();
        FormClosing += (_, e) =>
        {
            if (_isDownloading)
            {
                _downloadProgressLabel.Text = "Cancelling/finishing the download. Close Settings after the download stops.";
                try { _downloadCts?.Cancel(); } catch { }
                e.Cancel = true;
                return;
            }
            _isClosing = true;
            StopMicrophoneTest();
        };
        FormClosed += (_, _) => _titleToolTip.Dispose();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MadNegar", "1.0.1"));
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        return client;
    }

    private void BuildUi()
    {
        _titleBar.Location = new Point(0, 0);
        _titleBar.Size = new Size(ClientSize.Width, 47);
        _titleBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _titleBar.Tag = "titlebar";
        _titleBar.Paint += (_, e) =>
        {
            using var pen = new Pen(ThemeManager.ActivePalette.Border);
            e.Graphics.DrawLine(pen, 0, _titleBar.Height - 1, _titleBar.Width, _titleBar.Height - 1);
        };

        var title = new Label
        {
            Text = "Settings",
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            Location = new Point(16, 7),
            Size = new Size(76, 30)
        };
        title.MouseDown += DragWindow;
        _titleBar.Controls.Add(title);

        _themeToggleButton.Tag = "theme-toggle";
        _themeToggleButton.Location = new Point(96, 6);
        _themeToggleButton.Size = new Size(34, 34);
        _themeToggleButton.Font = new Font("Segoe UI Symbol", 15F, FontStyle.Regular);
        _themeToggleButton.Text = string.Empty;
        _themeToggleButton.Padding = Padding.Empty;
        _themeToggleButton.TextAlign = ContentAlignment.MiddleCenter;
        _themeToggleButton.FlatStyle = FlatStyle.Flat;
        _themeToggleButton.FlatAppearance.BorderSize = 0;
        _themeToggleButton.Cursor = Cursors.Hand;
        _themeToggleButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _themeToggleButton.AccessibleName = "Switch theme";
        _themeToggleButton.Click += (_, _) => ToggleThemeMode();
        _titleBar.Controls.Add(_themeToggleButton);
        RefreshThemeToggleButton();

        _closeWindowButton.Text = "×";
        _closeWindowButton.Tag = "window-close";
        _closeWindowButton.FlatStyle = FlatStyle.Flat;
        _closeWindowButton.FlatAppearance.BorderSize = 0;
        _closeWindowButton.Font = new Font("Segoe UI Symbol", 15F, FontStyle.Regular);
        _closeWindowButton.Location = new Point(580, 0);
        _closeWindowButton.Size = new Size(40, 34);
        _closeWindowButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _closeWindowButton.Cursor = Cursors.Hand;
        _closeWindowButton.Click += (_, _) => Close();
        _titleToolTip.SetToolTip(_closeWindowButton, "Close Settings");
        _titleBar.Controls.Add(_closeWindowButton);
        _titleBar.MouseDown += DragWindow;
        Controls.Add(_titleBar);

        // Use custom themed navigation and pages instead of the native TabControl.
        // The native WinForms tab renderer can leave a bright client border in dark mode.
        _audioNavButton.Text = "Audio";
        _audioNavButton.Location = new Point(14, 57);
        _audioNavButton.Size = new Size(52, 32);
        _audioNavButton.Tag = "nav-active";
        _audioNavButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _audioNavButton.Click += (_, _) => SetSettingsPage("Audio");
        Controls.Add(_audioNavButton);

        _downloadsNavButton.Text = "Downloads";
        _downloadsNavButton.Location = new Point(70, 57);
        _downloadsNavButton.Size = new Size(75, 32);
        _downloadsNavButton.Tag = "nav";
        _downloadsNavButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _downloadsNavButton.Click += (_, _) => SetSettingsPage("Downloads");
        Controls.Add(_downloadsNavButton);

        _transcriptionNavButton.Text = "Transcription";
        _transcriptionNavButton.Location = new Point(149, 57);
        _transcriptionNavButton.Size = new Size(89, 32);
        _transcriptionNavButton.Tag = "nav";
        _transcriptionNavButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _transcriptionNavButton.Click += (_, _) => SetSettingsPage("Transcription");
        Controls.Add(_transcriptionNavButton);

        _persianTextNavButton.Text = "Persian Text";
        _persianTextNavButton.Location = new Point(242, 57);
        _persianTextNavButton.Size = new Size(82, 32);
        _persianTextNavButton.Tag = "nav";
        _persianTextNavButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _persianTextNavButton.Click += (_, _) => SetSettingsPage("Persian Text");
        Controls.Add(_persianTextNavButton);

        _dictionaryNavButton.Text = "Dictionary";
        _dictionaryNavButton.Location = new Point(328, 57);
        _dictionaryNavButton.Size = new Size(75, 32);
        _dictionaryNavButton.Tag = "nav";
        _dictionaryNavButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _dictionaryNavButton.Click += (_, _) => SetSettingsPage("Dictionary");
        Controls.Add(_dictionaryNavButton);

        _historyNavButton.Text = "History";
        _historyNavButton.Location = new Point(407, 57);
        _historyNavButton.Size = new Size(65, 32);
        _historyNavButton.Tag = "nav";
        _historyNavButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _historyNavButton.Click += (_, _) => SetSettingsPage("History");
        Controls.Add(_historyNavButton);

        _diagnosticsNavButton.Text = "Diagnostics";
        _diagnosticsNavButton.Location = new Point(476, 57);
        _diagnosticsNavButton.Size = new Size(90, 32);
        _diagnosticsNavButton.Tag = "nav";
        _diagnosticsNavButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _diagnosticsNavButton.Click += (_, _) => SetSettingsPage("Diagnostics");
        Controls.Add(_diagnosticsNavButton);

        ConfigureSettingsPage(_audioPage, visible: true);
        ConfigureSettingsPage(_downloadsPage, visible: false);
        ConfigureSettingsPage(_transcriptionPage, visible: false);
        _transcriptionPage.AutoScroll = true;
        ConfigureSettingsPage(_persianTextPage, visible: false);
        ConfigureSettingsPage(_dictionaryPage, visible: false);
        _dictionaryPage.AutoScroll = true;
        ConfigureSettingsPage(_historyPage, visible: false);
        _historyPage.AutoScroll = true;
        ConfigureSettingsPage(_diagnosticsPage, visible: false);
        _diagnosticsPage.AutoScroll = true;
        Controls.Add(_audioPage);
        Controls.Add(_downloadsPage);
        Controls.Add(_transcriptionPage);
        Controls.Add(_persianTextPage);
        Controls.Add(_dictionaryPage);
        Controls.Add(_historyPage);
        Controls.Add(_diagnosticsPage);
        BuildAudioTab(_audioPage);
        BuildDownloadsTab(_downloadsPage);
        BuildTranscriptionTab(_transcriptionPage);
        BuildPersianTextTab(_persianTextPage);
        BuildDictionaryTab(_dictionaryPage);
        BuildHistoryTab(_historyPage);
        BuildDiagnosticsTab(_diagnosticsPage);

        var doneButton = new Button
        {
            Text = "Done",
            Location = new Point(498, 650),
            Size = new Size(106, 32)
        };
        StyleButton(doneButton, primary: true);
        doneButton.Click += (_, _) => Close();
        Controls.Add(doneButton);
        AcceptButton = doneButton;
    }

    private void ToggleThemeMode()
    {
        var isCurrentlyDark = ThemeManager.IsDark(_settings.ThemeMode);
        _settings.ThemeMode = isCurrentlyDark ? "Light" : "Dark";
        try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
        ThemeManager.Apply(this, _settings.ThemeMode);
        RefreshThemeToggleButton();
        _audioPage.Invalidate(true);
        _downloadsPage.Invalidate(true);
        _transcriptionPage.Invalidate(true);
        _persianTextPage.Invalidate(true);
        _dictionaryPage.Invalidate(true);
        _historyPage.Invalidate(true);
        _diagnosticsPage.Invalidate(true);
        Invalidate(true);
    }

    private void RefreshThemeToggleButton()
    {
        var isDark = ThemeManager.IsDark(_settings.ThemeMode);
        _themeToggleButton.IsDarkMode = isDark;
        _titleToolTip.SetToolTip(_themeToggleButton, isDark ? "Switch to light mode" : "Switch to dark mode");
        ThemeManager.ApplyControl(_themeToggleButton, _settings.ThemeMode);
    }

    private static void ConfigureSettingsPage(Panel page, bool visible)
    {
        page.Location = new Point(12, 98);
        page.Size = new Size(596, 548);
        page.Visible = visible;
        page.BorderStyle = BorderStyle.None;
        page.Tag = "settings-page";
        page.Padding = new Padding(0);
        page.Paint += (_, e) =>
        {
            var palette = ThemeManager.ActivePalette;
            using var pen = new Pen(palette.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, page.Width - 1), Math.Max(0, page.Height - 1));
        };
    }

    private void SetSettingsPage(string page)
    {
        _audioPage.Visible = page == "Audio";
        _downloadsPage.Visible = page == "Downloads";
        _transcriptionPage.Visible = page == "Transcription";
        _persianTextPage.Visible = page == "Persian Text";
        _dictionaryPage.Visible = page == "Dictionary";
        _historyPage.Visible = page == "History";
        _diagnosticsPage.Visible = page == "Diagnostics";
        _audioNavButton.Tag = page == "Audio" ? "nav-active" : "nav";
        _downloadsNavButton.Tag = page == "Downloads" ? "nav-active" : "nav";
        _transcriptionNavButton.Tag = page == "Transcription" ? "nav-active" : "nav";
        _persianTextNavButton.Tag = page == "Persian Text" ? "nav-active" : "nav";
        _dictionaryNavButton.Tag = page == "Dictionary" ? "nav-active" : "nav";
        _historyNavButton.Tag = page == "History" ? "nav-active" : "nav";
        _diagnosticsNavButton.Tag = page == "Diagnostics" ? "nav-active" : "nav";
        if (page == "Diagnostics") RefreshDiagnosticsReport();
        ThemeManager.Apply(this, _settings.ThemeMode);
        _audioPage.Invalidate(true);
        _downloadsPage.Invalidate(true);
        _transcriptionPage.Invalidate(true);
        _persianTextPage.Invalidate(true);
        _dictionaryPage.Invalidate(true);
        _historyPage.Invalidate(true);
        _diagnosticsPage.Invalidate(true);
    }

    private void BuildHistoryTab(Control tab)
    {
        tab.Controls.Add(MakeLabel("Local transcript history", 18, 18, 500, 26));

        _historyEnabledCheckBox.Text = "Save final transcripts in local history (off by default)";
        _historyEnabledCheckBox.AutoSize = false;
        _historyEnabledCheckBox.Location = new Point(18, 58);
        _historyEnabledCheckBox.Size = new Size(540, 28);
        _historyEnabledCheckBox.Checked = _settings.TranscriptHistoryEnabled;
        _historyEnabledCheckBox.CheckedChanged += (_, _) =>
        {
            _settings.TranscriptHistoryEnabled = _historyEnabledCheckBox.Checked;
            try { AppSettingsStore.Save(_rootDirectory, _settings); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the history preference.\n\n" + ex.Message, "History settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        tab.Controls.Add(_historyEnabledCheckBox);

        var privacyNote = new Label
        {
            Text = "Only final transcript text is saved, after local text cleanup and dictionary rules. Audio is never saved in history. New entries stop being recorded when this option is off; existing entries remain until you delete them.",
            Location = new Point(18, 96),
            Size = new Size(540, 70),
            Tag = "muted"
        };
        tab.Controls.Add(privacyNote);

        var locationNote = new Label
        {
            Text = "Local file: " + TranscriptHistoryStore.GetPath(_rootDirectory),
            Location = new Point(18, 174),
            Size = new Size(540, 48),
            Tag = "muted"
        };
        tab.Controls.Add(locationNote);

        _deleteAllHistoryButton.Text = "Delete all history…";
        StyleButton(_deleteAllHistoryButton, primary: false);
        _deleteAllHistoryButton.Location = new Point(18, 238);
        _deleteAllHistoryButton.Size = new Size(160, 34);
        _deleteAllHistoryButton.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Permanently delete all MadNegar transcript history? This does not delete exported TXT files.",
                    "Delete transcript history", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                TranscriptHistoryStore.Clear(_rootDirectory);
                MessageBox.Show(this, "Local transcript history was deleted.", "History", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not delete transcript history.\n\n" + ex.Message, "History", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        tab.Controls.Add(_deleteAllHistoryButton);

        var note = new Label
        {
            Text = "History is opt-in and independent from TXT Export. The app stores no audio and does not send transcript text to a cloud service. Keep the history option off on shared Windows accounts.",
            Location = new Point(18, 292),
            Size = new Size(540, 58),
            Tag = "muted"
        };
        tab.Controls.Add(note);
    }

    private void BuildDiagnosticsTab(Control tab)
    {
        tab.Controls.Add(MakeLabel("Local runtime diagnostics", 18, 16, 520, 25));
        var privacyNote = new Label
        {
            Text = "This report is generated in memory when requested. It includes device/format, model/runtime, audio levels, timing and the last error. Transcript text and audio are never included or written to a diagnostics log.",
            Location = new Point(18, 45),
            Size = new Size(552, 48),
            Tag = "muted"
        };
        tab.Controls.Add(privacyNote);

        _diagnosticsReportBox.Location = new Point(18, 98);
        _diagnosticsReportBox.Size = new Size(552, 378);
        _diagnosticsReportBox.Multiline = true;
        _diagnosticsReportBox.ReadOnly = true;
        _diagnosticsReportBox.ScrollBars = ScrollBars.Vertical;
        _diagnosticsReportBox.WordWrap = false;
        _diagnosticsReportBox.Font = new Font("Consolas", 9F, FontStyle.Regular);
        _diagnosticsReportBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        tab.Controls.Add(_diagnosticsReportBox);

        _refreshDiagnosticsButton.Text = "Refresh";
        _refreshDiagnosticsButton.Location = new Point(18, 487);
        _refreshDiagnosticsButton.Size = new Size(100, 32);
        StyleButton(_refreshDiagnosticsButton, primary: false);
        _refreshDiagnosticsButton.Click += (_, _) => RefreshDiagnosticsReport();
        tab.Controls.Add(_refreshDiagnosticsButton);

        _copyDiagnosticsButton.Text = "Copy report";
        _copyDiagnosticsButton.Location = new Point(126, 487);
        _copyDiagnosticsButton.Size = new Size(120, 32);
        StyleButton(_copyDiagnosticsButton, primary: true);
        _copyDiagnosticsButton.Click += (_, _) =>
        {
            RefreshDiagnosticsReport();
            try { Clipboard.SetText(_diagnosticsReportBox.Text); }
            catch (Exception ex) { MessageBox.Show(this, "Could not copy diagnostics. " + ex.Message, "Diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        tab.Controls.Add(_copyDiagnosticsButton);
        RefreshDiagnosticsReport();
    }

    private void RefreshDiagnosticsReport()
    {
        var selectedModel = !string.IsNullOrWhiteSpace(_settings.SelectedModelKey) ? _settings.SelectedModelKey : _settings.SelectedModelId;
        var language = _settings.SpokenLanguage switch { "fa" => "Persian (fa)", "en" => "English (en)", _ => "Auto" };
        _diagnosticsReportBox.Text = DiagnosticsState.CreateReport(
            _rootDirectory,
            _settings.SelectedMicrophoneName ?? "Windows default microphone",
            selectedModel ?? "Not selected",
            language,
            AutoLanguageText.NormalizeStrategy(_settings.AutoLanguageStrategy),
            _settings.TranscriptHistoryEnabled);
    }

    private void BuildAudioTab(Control tab)
    {
        tab.Controls.Add(MakeLabel("Input microphone", 16, 16, 180, 20));

        _microphoneCombo.Location = new Point(16, 40);
        _microphoneCombo.Size = new Size(405, 28);
        _microphoneCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _microphoneCombo.DropDownWidth = 460;
        _microphoneCombo.SelectedIndexChanged += MicrophoneComboSelectedIndexChanged;
        tab.Controls.Add(_microphoneCombo);

        _refreshDevicesButton.Text = "Refresh";
        StyleButton(_refreshDevicesButton, primary: false);
        _refreshDevicesButton.Location = new Point(430, 39);
        _refreshDevicesButton.Size = new Size(124, 30);
        _refreshDevicesButton.Click += (_, _) => RefreshMicrophoneDevices();
        tab.Controls.Add(_refreshDevicesButton);

        _microphoneStatus.Location = new Point(18, 80);
        _microphoneStatus.Size = new Size(535, 36);
        _microphoneStatus.Text = "Choose a microphone, then start the test.";
        _microphoneStatus.ForeColor = Color.FromArgb(71, 85, 105);
        tab.Controls.Add(_microphoneStatus);

        tab.Controls.Add(MakeLabel("Live input level (includes software boost)", 18, 125, 520, 20));
        _microphoneLevel.Location = new Point(17, 151);
        _microphoneLevel.Size = new Size(537, 22);
        _microphoneLevel.Minimum = 0;
        _microphoneLevel.Maximum = 100;
        _microphoneLevel.Value = 0;
        tab.Controls.Add(_microphoneLevel);

        var instruction = new Label
        {
            Text = "Press Start Mic Test and say: 1, 2, 3 — 1, 2, 3. WASAPI is used to access the same modern Windows microphone endpoints as most communication apps.",
            Location = new Point(18, 183),
            Size = new Size(535, 43),
            ForeColor = Color.FromArgb(71, 85, 105)
        };
        instruction.Tag = "muted";
        tab.Controls.Add(instruction);

        _startMicTestButton.Text = "▶  Start Mic Test";
        StyleButton(_startMicTestButton, primary: true);
        _startMicTestButton.Location = new Point(17, 229);
        _startMicTestButton.Size = new Size(235, 36);
        _startMicTestButton.Click += (_, _) =>
        {
            if (_isTestingMicrophone || _testWaveIn is not null)
                StopMicrophoneTest();
            else
                StartMicrophoneTest();
        };
        tab.Controls.Add(_startMicTestButton);

        tab.Controls.Add(MakeLabel("Audio profile", 18, 270, 250, 20));
        _audioProfileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _audioProfileCombo.Location = new Point(16, 291);
        _audioProfileCombo.Size = new Size(535, 28);
        _audioProfileCombo.Items.AddRange(["Custom", "Quiet Room", "Noisy Room"]);
        _audioProfileCombo.SelectedIndex = AudioProfilePolicy.Normalize(_settings.AudioProfile) switch
        {
            AudioProfilePolicy.QuietRoom => 1,
            AudioProfilePolicy.NoisyRoom => 2,
            _ => 0
        };
        _audioProfileCombo.SelectedIndexChanged += (_, _) => ApplySelectedAudioProfile();
        tab.Controls.Add(_audioProfileCombo);

        var profileNote = new Label
        {
            Text = "Presets are starting points, not AI noise cancellation. Custom keeps your manual Boost, Auto-level and rumble-filter settings.",
            Location = new Point(18, 321),
            Size = new Size(535, 31),
            ForeColor = Color.FromArgb(100, 116, 139),
            Tag = "muted"
        };
        tab.Controls.Add(profileNote);

        tab.Controls.Add(MakeLabel("Software microphone boost", 18, 352, 300, 20));
        _microphoneBoost.Minimum = 0;
        _microphoneBoost.Maximum = 30;
        _microphoneBoost.TickFrequency = 5;
        _microphoneBoost.SmallChange = 1;
        _microphoneBoost.LargeChange = 5;
        _microphoneBoost.Value = Math.Clamp(_settings.MicrophoneBoostDb, 0, 30);
        _microphoneBoost.Location = new Point(12, 374);
        _microphoneBoost.Size = new Size(430, 45);
        _microphoneBoost.ValueChanged += (_, _) => SaveAudioProcessingSettings();
        tab.Controls.Add(_microphoneBoost);

        _microphoneBoostValue.Location = new Point(447, 382);
        _microphoneBoostValue.Size = new Size(105, 24);
        _microphoneBoostValue.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _microphoneBoostValue.ForeColor = Color.FromArgb(37, 99, 235);
        tab.Controls.Add(_microphoneBoostValue);

        _autoLevelCheckBox.Text = "Auto-level quiet speech (recommended)";
        _autoLevelCheckBox.AutoSize = true;
        _autoLevelCheckBox.Location = new Point(18, 425);
        _autoLevelCheckBox.Checked = _settings.AutoLevelQuietSpeech;
        _autoLevelCheckBox.CheckedChanged += (_, _) => SaveAudioProcessingSettings();
        tab.Controls.Add(_autoLevelCheckBox);

        _reduceRumbleCheckBox.Text = "Reduce low-frequency rumble (80 Hz high-pass)";
        _reduceRumbleCheckBox.AutoSize = true;
        _reduceRumbleCheckBox.Location = new Point(18, 453);
        _reduceRumbleCheckBox.Checked = _settings.ReduceLowFrequencyRumble;
        _reduceRumbleCheckBox.CheckedChanged += (_, _) => SaveAudioProcessingSettings();
        tab.Controls.Add(_reduceRumbleCheckBox);

        var note = new Label
        {
            Text = "Boost and auto-level are applied before recognition. High boost can also amplify background noise; the high-pass filter reduces low rumble but is not AI noise cancellation.",
            Location = new Point(18, 487),
            Size = new Size(535, 48),
            ForeColor = Color.FromArgb(100, 116, 139),
            Tag = "muted"
        };
        tab.Controls.Add(note);

        UpdateMicrophoneBoostLabel();
    }

    private void BuildTranscriptionTab(Panel tab)
    {
        tab.SuspendLayout();
        tab.Controls.Clear();
        tab.AutoScroll = true;
        tab.AutoScrollMinSize = Size.Empty;

        // Card 1 — keep model selection compact and distinguish temporary preview from final output.
        var modelCard = CreateTranscriptionCard(tab, 12, 270,
            "Speech models",
            "Live Preview stays automatic when Speak Language is Auto. Dual-model Auto compares the selected Persian- and English-role candidates.");

        _liveTranscriptionCheckBox.Text = "Show live transcription while recording (experimental)";
        _liveTranscriptionCheckBox.AutoSize = false;
        _liveTranscriptionCheckBox.Location = new Point(18, 76);
        _liveTranscriptionCheckBox.Size = new Size(510, 24);
        _liveTranscriptionCheckBox.Checked = _settings.LiveTranscriptionEnabled;
        _liveTranscriptionCheckBox.CheckedChanged += (_, _) =>
        {
            _settings.LiveTranscriptionEnabled = _liveTranscriptionCheckBox.Checked;
            try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
        };
        modelCard.Controls.Add(_liveTranscriptionCheckBox);

        _fastLivePreviewCheckBox.Text = "Automatically choose a fast compatible model for Live Preview";
        _fastLivePreviewCheckBox.AutoSize = false;
        _fastLivePreviewCheckBox.Location = new Point(18, 102);
        _fastLivePreviewCheckBox.Size = new Size(510, 24);
        _fastLivePreviewCheckBox.Checked = _settings.FastLivePreviewEnabled;
        _fastLivePreviewCheckBox.CheckedChanged += (_, _) =>
        {
            _settings.FastLivePreviewEnabled = _fastLivePreviewCheckBox.Checked;
            _liveModelCombo.Enabled = !_fastLivePreviewCheckBox.Checked && _liveModelCombo.Items.Count > 1;
            try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
        };
        modelCard.Controls.Add(_fastLivePreviewCheckBox);

        modelCard.Controls.Add(MakeLabel("Live Preview", 18, 139, 126, 24));
        _liveModelCombo.AccessibleName = "Live Preview model";
        _liveModelCombo.AccessibleDescription = "Uncheck automatic fast model selection to choose a specific downloaded model. Vosk and Diba preview only in their own model language; multilingual Whisper/faster-whisper can use Auto.";
        _liveModelCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _liveModelCombo.Location = new Point(151, 134);
        _liveModelCombo.Size = new Size(377, 30);
        _liveModelCombo.DropDownWidth = 510;
        _liveModelCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_liveModelCombo.SelectedItem is ModelSelectionOption option)
            {
                _settings.LivePreviewModelKey = option.Key;
                try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
            }
        };
        modelCard.Controls.Add(_liveModelCombo);

        modelCard.Controls.Add(MakeLabel("Final transcription", 18, 178, 126, 24));
        _finalModelCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _finalModelCombo.Location = new Point(151, 173);
        _finalModelCombo.Size = new Size(377, 30);
        _finalModelCombo.DropDownWidth = 510;
        _finalModelCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_finalModelCombo.SelectedItem is SpeechModelChoice choice)
            {
                _settings.SelectedModelKey = choice.Key;
                _settings.SelectedModelId = choice.Id;
                try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
            }
        };
        modelCard.Controls.Add(_finalModelCombo);

        _transcriptionNoteLabel.Location = new Point(18, 211);
        _transcriptionNoteLabel.Size = new Size(510, 48);
        _transcriptionNoteLabel.Tag = "muted";
        _transcriptionNoteLabel.AutoEllipsis = true;
        modelCard.Controls.Add(_transcriptionNoteLabel);

        // Card 2 — voice typing controls grouped in one horizontal shortcut row.
        var typingCard = CreateTranscriptionCard(tab, 294, 252,
            "Voice typing",
            "Use a global shortcut to start/stop recording without leaving Word, a browser, or a Rename field.");

        _globalHotkeyCheckBox.Text = "Enable global voice typing shortcut";
        _globalHotkeyCheckBox.AutoSize = false;
        _globalHotkeyCheckBox.Location = new Point(18, 74);
        _globalHotkeyCheckBox.Size = new Size(510, 24);
        _globalHotkeyCheckBox.Checked = _settings.GlobalHotkeyEnabled;
        _globalHotkeyCheckBox.CheckedChanged += (_, _) =>
        {
            _settings.GlobalHotkeyEnabled = _globalHotkeyCheckBox.Checked;
            if (!_settings.GlobalHotkeyEnabled && _capturingGlobalHotkey) CancelGlobalHotkeyCapture();
            UpdateGlobalHotkeyControlsEnabled();
            try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
        };
        typingCard.Controls.Add(_globalHotkeyCheckBox);

        typingCard.Controls.Add(MakeLabel("Shortcut", 18, 106, 82, 25));
        _globalHotkeyShortcutLabel.Text = GlobalHotkeySupport.NormalizeShortcut(_settings.GlobalHotkeyShortcut);
        _globalHotkeyShortcutLabel.Location = new Point(101, 106);
        _globalHotkeyShortcutLabel.Size = new Size(148, 27);
        _globalHotkeyShortcutLabel.TextAlign = ContentAlignment.MiddleLeft;
        typingCard.Controls.Add(_globalHotkeyShortcutLabel);

        _changeGlobalHotkeyButton.Text = "Change…";
        _changeGlobalHotkeyButton.Location = new Point(267, 104);
        _changeGlobalHotkeyButton.Size = new Size(92, 30);
        _changeGlobalHotkeyButton.Click += (_, _) =>
        {
            if (_capturingGlobalHotkey) CancelGlobalHotkeyCapture();
            else StartGlobalHotkeyCapture();
        };
        typingCard.Controls.Add(_changeGlobalHotkeyButton);

        _resetGlobalHotkeyButton.Text = "Default";
        _resetGlobalHotkeyButton.Location = new Point(366, 104);
        _resetGlobalHotkeyButton.Size = new Size(82, 30);
        _resetGlobalHotkeyButton.Click += (_, _) =>
        {
            _capturingGlobalHotkey = false;
            _settings.GlobalHotkeyShortcut = GlobalHotkeySupport.DefaultShortcut;
            _globalHotkeyShortcutLabel.Text = _settings.GlobalHotkeyShortcut;
            _changeGlobalHotkeyButton.Text = "Change…";
            _globalHotkeyCaptureNote.Text = GlobalHotkeyCaptureInstructions;
            UpdateGlobalHotkeyControlsEnabled();
            try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
        };
        typingCard.Controls.Add(_resetGlobalHotkeyButton);

        _globalHotkeyCaptureNote.Text = GlobalHotkeyCaptureInstructions;
        _globalHotkeyCaptureNote.Location = new Point(101, 134);
        _globalHotkeyCaptureNote.Size = new Size(427, 25);
        _globalHotkeyCaptureNote.ForeColor = Color.FromArgb(100, 116, 139);
        _globalHotkeyCaptureNote.Tag = "muted";
        typingCard.Controls.Add(_globalHotkeyCaptureNote);
        UpdateGlobalHotkeyControlsEnabled();

        _directTypingAutoInsertCheckBox.Text = "Automatically insert final text into the active field";
        _directTypingAutoInsertCheckBox.AutoSize = false;
        _directTypingAutoInsertCheckBox.Location = new Point(18, 165);
        _directTypingAutoInsertCheckBox.Size = new Size(510, 24);
        _directTypingAutoInsertCheckBox.Checked = _settings.DirectTypingAutoInsert;
        _directTypingAutoInsertCheckBox.CheckedChanged += (_, _) =>
        {
            _settings.DirectTypingAutoInsert = _directTypingAutoInsertCheckBox.Checked;
            try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
        };
        typingCard.Controls.Add(_directTypingAutoInsertCheckBox);

        _voiceTypingSoundFeedbackCheckBox.Text = "Play a short sound when recording starts/stops (no Windows toast)";
        _voiceTypingSoundFeedbackCheckBox.AutoSize = false;
        _voiceTypingSoundFeedbackCheckBox.Location = new Point(18, 192);
        _voiceTypingSoundFeedbackCheckBox.Size = new Size(510, 24);
        _voiceTypingSoundFeedbackCheckBox.Checked = _settings.VoiceTypingSoundFeedback;
        _voiceTypingSoundFeedbackCheckBox.CheckedChanged += (_, _) =>
        {
            _settings.VoiceTypingSoundFeedback = _voiceTypingSoundFeedbackCheckBox.Checked;
            try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
        };
        typingCard.Controls.Add(_voiceTypingSoundFeedbackCheckBox);

        var directTypingNote = new Label
        {
            Text = "Live preview is never typed. Only final text is inserted. Normal Paste replaces any selected text. Turn off Auto Insert for copy only; nothing is inserted automatically. MadNegar never presses Enter; it will not press Enter for you.",
            Location = new Point(18, 219),
            Size = new Size(510, 25),
            ForeColor = Color.FromArgb(100, 116, 139),
            Tag = "muted"
        };
        typingCard.Controls.Add(directTypingNote);

        // Card 3 — automatic recognition policy, with the independent transcript-language preference.
        _autoLanguageCard.Controls.Clear();
        _autoLanguageCard.Location = new Point(10, 558);
        _autoLanguageCard.Size = new Size(550, 230);
        _autoLanguageCard.Tag = "settings-card";
        _autoLanguageCard.Paint += (_, e) =>
        {
            using var pen = new Pen(ThemeManager.ActivePalette.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, _autoLanguageCard.Width - 1), Math.Max(0, _autoLanguageCard.Height - 1));
        };
        _autoLanguageCard.Controls.Add(MakeLabel("Automatic language", 18, 14, 510, 25));
        _autoLanguageDescription.Text = "Choose the normal single-model path for speed, or start from the Persian model and preserve recognized English technical terms for mixed speech.";
        _autoLanguageDescription.Location = new Point(18, 42);
        _autoLanguageDescription.Size = new Size(510, 34);
        _autoLanguageDescription.ForeColor = Color.FromArgb(100, 116, 139);
        _autoLanguageDescription.Tag = "muted";
        _autoLanguageCard.Controls.Add(_autoLanguageDescription);

        _autoLanguageCard.Controls.Add(MakeLabel("Transcription Language Preference / زبان اصلی ترنسکریپت", 18, 81, 510, 20));
        _transcriptionPreferenceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _transcriptionPreferenceCombo.Location = new Point(18, 102);
        _transcriptionPreferenceCombo.Size = new Size(510, 30);
        _transcriptionPreferenceCombo.DropDownWidth = 510;
        _transcriptionPreferenceCombo.Items.AddRange([
            "فارسی‌اول · Persian-first",
            "English-first · انگلیسی‌اول"
        ]);
        _transcriptionPreferenceCombo.AccessibleName = "Transcription Language Preference / زبان اصلی ترنسکریپت";
        _transcriptionPreferenceCombo.AccessibleDescription = "Only biases uncertain candidate selection. It must not suppress Persian script or turn a mixed utterance into a single language.";
        _transcriptionPreferenceCombo.SelectedIndex =
            AutoLanguageText.NormalizeTranscriptionLanguagePreference(_settings.TranscriptionLanguagePreference) ==
            AutoLanguageText.EnglishFirstPreference ? 1 : 0;
        _transcriptionPreferenceCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_transcriptionPreferenceCombo.SelectedIndex < 0) return;
            _settings.TranscriptionLanguagePreference = _transcriptionPreferenceCombo.SelectedIndex == 1
                ? AutoLanguageText.EnglishFirstPreference
                : AutoLanguageText.PersianFirstPreference;
            try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
        };
        _autoLanguageCard.Controls.Add(_transcriptionPreferenceCombo);

        _autoLanguageCard.Controls.Add(MakeLabel("Auto mode", 18, 143, 95, 25));
        _autoLanguageStrategyCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _autoLanguageStrategyCombo.Location = new Point(116, 139);
        _autoLanguageStrategyCombo.Size = new Size(412, 30);
        _autoLanguageStrategyCombo.DropDownWidth = 500;
        _autoLanguageStrategyCombo.Items.AddRange([
            "Standard Auto · one model, faster",
            "Persian-first mixed Auto · two models, recommended"
        ]);
        var savedStrategy = AutoLanguageText.NormalizeForSettings(_settings.AutoLanguageStrategy);
        _autoLanguageStrategyCombo.SelectedIndex = savedStrategy == AutoLanguageText.DualModelMixed ? 1 : 0;
        _autoLanguageStrategyCombo.SelectedIndexChanged += (_, _) =>
        {
            _settings.AutoLanguageStrategy = _autoLanguageStrategyCombo.SelectedIndex == 1
                ? AutoLanguageText.DualModelMixed
                : AutoLanguageText.Balanced;
            UpdateDualModelModeControls();
            try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
        };
        _autoLanguageCard.Controls.Add(_autoLanguageStrategyCombo);

        _autoLanguageModeNote.Location = new Point(18, 179);
        _autoLanguageModeNote.Size = new Size(510, 42);
        _autoLanguageModeNote.ForeColor = Color.FromArgb(100, 116, 139);
        _autoLanguageModeNote.Tag = "muted";
        _autoLanguageCard.Controls.Add(_autoLanguageModeNote);

        _dualModelPersianLabel.Text = "Persian base model";
        _dualModelPersianLabel.Location = new Point(18, 228);
        _dualModelPersianLabel.Size = new Size(510, 20);
        _dualModelPersianLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _autoLanguageCard.Controls.Add(_dualModelPersianLabel);

        _autoMixedPersianModelCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _autoMixedPersianModelCombo.Location = new Point(18, 249);
        _autoMixedPersianModelCombo.Size = new Size(510, 30);
        _autoMixedPersianModelCombo.DropDownWidth = 510;
        _autoMixedPersianModelCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_autoMixedPersianModelCombo.SelectedItem is DualModelChoiceOption option && !string.IsNullOrWhiteSpace(option.Key))
            {
                _settings.AutoMixedPersianModelKey = option.Key;
                _settings.AutoMixedPersianModelId = option.Key[(option.Key.IndexOf("::", StringComparison.Ordinal) + 2)..];
                try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
            }
        };
        _autoLanguageCard.Controls.Add(_autoMixedPersianModelCombo);

        _dualModelEnglishLabel.Text = "English term-comparison model";
        _dualModelEnglishLabel.Location = new Point(18, 287);
        _dualModelEnglishLabel.Size = new Size(510, 20);
        _dualModelEnglishLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _autoLanguageCard.Controls.Add(_dualModelEnglishLabel);

        _autoMixedEnglishModelCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _autoMixedEnglishModelCombo.Location = new Point(18, 308);
        _autoMixedEnglishModelCombo.Size = new Size(510, 30);
        _autoMixedEnglishModelCombo.DropDownWidth = 510;
        _autoMixedEnglishModelCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_autoMixedEnglishModelCombo.SelectedItem is DualModelChoiceOption option && !string.IsNullOrWhiteSpace(option.Key))
            {
                _settings.AutoMixedEnglishModelKey = option.Key;
                _settings.AutoMixedEnglishModelId = option.Key[(option.Key.IndexOf("::", StringComparison.Ordinal) + 2)..];
                try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
            }
        };
        _autoLanguageCard.Controls.Add(_autoMixedEnglishModelCombo);

        _dualModelModeNote.Location = new Point(18, 345);
        _dualModelModeNote.Size = new Size(510, 54);
        _dualModelModeNote.ForeColor = Color.FromArgb(100, 116, 139);
        _dualModelModeNote.Tag = "muted";
        _autoLanguageCard.Controls.Add(_dualModelModeNote);
        tab.Controls.Add(_autoLanguageCard);

        // Card 4 — preserve known spoken-English names in Latin script; dictionary handles user terms.
        _technicalTermsCard.Controls.Clear();
        _technicalTermsCard.Location = new Point(10, _autoLanguageCard.Bottom + 12);
        _technicalTermsCard.Size = new Size(550, 118);
        _technicalTermsCard.Tag = "settings-card";
        _technicalTermsCard.Paint += (_, e) =>
        {
            using var pen = new Pen(ThemeManager.ActivePalette.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, _technicalTermsCard.Width - 1), Math.Max(0, _technicalTermsCard.Height - 1));
        };
        _technicalTermsCard.Controls.Add(MakeLabel("English terms in mixed speech", 18, 14, 510, 25));

        _preserveEnglishTechnicalTermsInAutoCheckBox.Text = "Keep recognized English technical terms in Latin script";
        _preserveEnglishTechnicalTermsInAutoCheckBox.AutoSize = false;
        _preserveEnglishTechnicalTermsInAutoCheckBox.Location = new Point(18, 43);
        _preserveEnglishTechnicalTermsInAutoCheckBox.Size = new Size(510, 24);
        _preserveEnglishTechnicalTermsInAutoCheckBox.Checked = _settings.PreserveEnglishTechnicalTermsInAuto;
        _preserveEnglishTechnicalTermsInAutoCheckBox.CheckedChanged += (_, _) =>
        {
            _settings.PreserveEnglishTechnicalTermsInAuto = _preserveEnglishTechnicalTermsInAutoCheckBox.Checked;
            try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
        };
        _technicalTermsCard.Controls.Add(_preserveEnglishTechnicalTermsInAutoCheckBox);

        _technicalTermsNote.Text = "Auto is prompted to keep Persian in Persian script and English in Latin; recognizable Finglish gets a Persian retry. Common terms can be restored here; add custom terms in Dictionary. Results are best-effort.";
        _technicalTermsNote.Location = new Point(18, 70);
        _technicalTermsNote.Size = new Size(510, 39);
        _technicalTermsNote.ForeColor = Color.FromArgb(100, 116, 139);
        _technicalTermsNote.Tag = "muted";
        _technicalTermsCard.Controls.Add(_technicalTermsNote);
        tab.Controls.Add(_technicalTermsCard);

        PopulateTranscriptionModelChoices();
        PopulateDualMixedModelChoices();
        UpdateDualModelModeControls();
        _liveModelCombo.Enabled = !_settings.FastLivePreviewEnabled && _liveModelCombo.Items.Count > 1;
        tab.ResumeLayout(true);
    }

    private Panel CreateTranscriptionCard(Control parent, int top, int height, string title, string subtitle)
    {
        var card = new Panel
        {
            Location = new Point(10, top),
            Size = new Size(550, height),
            Tag = "settings-card",
            BorderStyle = BorderStyle.None
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(ThemeManager.ActivePalette.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, card.Width - 1), Math.Max(0, card.Height - 1));
        };
        var titleLabel = MakeLabel(title, 18, 13, 510, 25);
        titleLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        card.Controls.Add(titleLabel);
        var subtitleLabel = new Label
        {
            Text = subtitle,
            Location = new Point(18, 41),
            Size = new Size(510, 31),
            ForeColor = Color.FromArgb(100, 116, 139),
            Tag = "muted"
        };
        card.Controls.Add(subtitleLabel);
        parent.Controls.Add(card);
        return card;
    }

    private bool IsDualModelChoiceDownloaded(SpeechModelChoice choice) => choice.WhisperModel is not null
        ? ModelValidation.IsWhisperModelReady(ModelPath(choice.WhisperModel))
        : choice.ProviderModel is not null && IsDownloaded(choice.ProviderModel) && choice.IsSupportedForFinal;

    private IReadOnlyList<SpeechModelChoice> GetDownloadedDualModelChoices()
    {
        var whisperChoices = ModelCatalog.Models.Select(model => new SpeechModelChoice(model, null));
        var providerChoices = ProviderModelCatalog.All
            .Where(item => !item.IsWhisper) // Whisper.cpp models are represented by ModelCatalog.Models above.
            .Select(item => new SpeechModelChoice(null, item));
        return whisperChoices.Concat(providerChoices)
            .Where(IsDualModelChoiceDownloaded)
            .Where(choice => choice.IsSupportedForFinal)
            .OrderBy(choice => choice.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void PopulateDualMixedModelChoices()
    {
        var downloaded = GetDownloadedDualModelChoices();
        var persianChoices = downloaded.Where(choice => DualModelSelectionPolicy.SupportsLanguageRole(choice, "fa"))
            .Select(choice => new DualModelChoiceOption(choice.Key, $"{choice.DisplayName} · {choice.Provider} · {choice.Languages} · {choice.ApproxSize}"))
            .ToList();
        var englishChoices = downloaded.Where(choice => DualModelSelectionPolicy.SupportsLanguageRole(choice, "en"))
            .Select(choice => new DualModelChoiceOption(choice.Key, $"{choice.DisplayName} · {choice.Provider} · {choice.Languages} · {choice.ApproxSize}"))
            .ToList();
        PopulateDualModelCombo(_autoMixedPersianModelCombo, persianChoices,
            DualModelSelectionPolicy.LegacyOrSavedKey(_settings.AutoMixedPersianModelKey, _settings.AutoMixedPersianModelId), "Persian");
        PopulateDualModelCombo(_autoMixedEnglishModelCombo, englishChoices,
            DualModelSelectionPolicy.LegacyOrSavedKey(_settings.AutoMixedEnglishModelKey, _settings.AutoMixedEnglishModelId), "English");
        _dualModelModeNote.Text = $"Ready choices — Persian base: {persianChoices.Count}; English comparison: {englishChoices.Count}. " +
            "The language preference is only a tie-breaker when candidate detections are uncertain. Mixed-script output and confident language detections take priority; the app does not intentionally translate the whole sentence. Audio stays local.";
    }

    private static void PopulateDualModelCombo(ThemedComboBox combo, IReadOnlyList<DualModelChoiceOption> choices, string? savedKey, string role)
    {
        combo.BeginUpdate();
        combo.Items.Clear();
        if (choices.Count == 0)
        {
            var action = role.Equals("Persian", StringComparison.OrdinalIgnoreCase)
                ? "Download/repair a complete multilingual Whisper.cpp/faster-whisper model or a Persian Vosk/Diba model in Settings → Downloads."
                : "Download/repair a complete English-capable Whisper.cpp/faster-whisper model or English Vosk model in Settings → Downloads.";
            combo.Items.Add($"No complete downloaded {role} models — see the note below");
            combo.SelectedIndex = 0;
            combo.Enabled = false;
            combo.AccessibleDescription = action;
        }
        else
        {
            combo.Items.Add(new DualModelChoiceOption("", $"Choose a downloaded {role} model…"));
            foreach (var choice in choices) combo.Items.Add(choice);
            var choiceIndex = choices.ToList().FindIndex(choice => choice.Key.Equals(savedKey, StringComparison.OrdinalIgnoreCase));
            combo.SelectedIndex = choiceIndex >= 0 ? choiceIndex + 1 : 0;
            combo.Enabled = true;
        }
        combo.EndUpdate();
    }

    private void UpdateDualModelModeControls()
    {
        var enabled = AutoLanguageText.NormalizeForSettings(_settings.AutoLanguageStrategy) == AutoLanguageText.DualModelMixed;
        _autoMixedPersianModelCombo.Visible = enabled;
        _autoMixedEnglishModelCombo.Visible = enabled;
        _dualModelPersianLabel.Visible = enabled;
        _dualModelEnglishLabel.Visible = enabled;
        _dualModelModeNote.Visible = enabled;
        _autoMixedPersianModelCombo.Enabled = enabled && _autoMixedPersianModelCombo.Items.Count > 0 && _autoMixedPersianModelCombo.Items[0] is DualModelChoiceOption;
        _autoMixedEnglishModelCombo.Enabled = enabled && _autoMixedEnglishModelCombo.Items.Count > 0 && _autoMixedEnglishModelCombo.Items[0] is DualModelChoiceOption;
        _dualModelModeNote.ForeColor = enabled ? ThemeManager.ActivePalette.MutedText : Color.FromArgb(100, 116, 139);
        _autoLanguageModeNote.Text = enabled
            ? "Live Preview stays on Auto. Final text compares both language-role models; confident detections and mixed-script candidates outrank the preference."
            : "Single-model Auto stays automatic. The preference never locks mixed speech; in dual-model mode it only breaks uncertain candidate ties.";
        _autoLanguageCard.Height = enabled ? 412 : 230;
        _technicalTermsCard.Top = _autoLanguageCard.Bottom + 12;
        _transcriptionPage.AutoScrollMinSize = new Size(0, _technicalTermsCard.Bottom + 12);
        _transcriptionPage.PerformLayout();
        _autoLanguageCard.Invalidate(true);
        _technicalTermsCard.Invalidate(true);
    }

    private void BuildPersianTextTab(Control tab)
    {
        var heading = new Label
        {
            Text = "Persian text cleanup",
            Location = new Point(18, 18),
            Size = new Size(535, 26),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };
        tab.Controls.Add(heading);

        var intro = new Label
        {
            Text = "These local, rule-based fixes run on the final transcript only. Live preview is left untouched. Undo fixes in the main window restores the original transcript exactly.",
            Location = new Point(18, 48),
            Size = new Size(535, 48),
            ForeColor = Color.FromArgb(100, 116, 139),
            Tag = "muted"
        };
        tab.Controls.Add(intro);

        ConfigurePersianTextCheckBox(_persianTextNormalizeYehCheckBox,
            "Normalize Arabic Yeh (ي) to Persian Yeh (ی)", 18, 112,
            () => _settings.PersianTextNormalizeYeh,
            value => _settings.PersianTextNormalizeYeh = value);
        ConfigurePersianTextCheckBox(_persianTextNormalizeKafCheckBox,
            "Normalize Arabic Kaf (ك) to Persian Kaf (ک)", 18, 148,
            () => _settings.PersianTextNormalizeKaf,
            value => _settings.PersianTextNormalizeKaf = value);
        ConfigurePersianTextCheckBox(_persianTextNormalizeHalfSpacesCheckBox,
            "Apply common half-space rules (می‌رود، کتاب‌ها)", 18, 184,
            () => _settings.PersianTextNormalizeHalfSpaces,
            value => _settings.PersianTextNormalizeHalfSpaces = value);
        ConfigurePersianTextCheckBox(_persianTextNormalizePunctuationCheckBox,
            "Fix spacing around Persian punctuation (، ؛ ؟)", 18, 220,
            () => _settings.PersianTextNormalizePunctuation,
            value => _settings.PersianTextNormalizePunctuation = value);
        ConfigurePersianTextCheckBox(_applyPersianTextDirectTypingCheckBox,
            "Also apply these fixes to global-shortcut direct typing", 18, 266,
            () => _settings.ApplyPersianTextNormalizationToDirectTyping,
            value => _settings.ApplyPersianTextNormalizationToDirectTyping = value);

        var caution = new Label
        {
            Text = "Undo affects the transcript inside MadNegar only; it cannot retract text already pasted into another application. URLs, e-mail addresses and inline code are protected from these rules. Disable any rule that changes words in an unwanted way.",
            Location = new Point(18, 316),
            Size = new Size(535, 60),
            ForeColor = Color.FromArgb(100, 116, 139),
            Tag = "muted"
        };
        tab.Controls.Add(caution);
    }

    private void BuildDictionaryTab(Control tab)
    {
        var heading = new Label
        {
            Text = "Local dictionary",
            Location = new Point(18, 16),
            Size = new Size(535, 26),
            Font = new Font("Segoe UI", 12F, FontStyle.Bold)
        };
        tab.Controls.Add(heading);

        var intro = new Label
        {
            Text = "Correct recurring names and technical terms locally. Rules run once on the final transcript, never on Live Preview. URLs, email addresses and code are protected.",
            Location = new Point(18, 46),
            Size = new Size(535, 42),
            ForeColor = Color.FromArgb(100, 116, 139),
            Tag = "muted"
        };
        tab.Controls.Add(intro);

        tab.Controls.Add(MakeLabel("Recognized phrase", 18, 92, 252, 20));
        _dictionaryPhraseInput.Location = new Point(18, 114);
        _dictionaryPhraseInput.Size = new Size(252, 27);
        _dictionaryPhraseInput.MaxLength = LocalDictionaryText.MaximumPhraseLength;
        ThemeManager.ApplyControl(_dictionaryPhraseInput, _settings.ThemeMode);
        _dictionaryPhraseInput.TextChanged += (_, _) => UpdateDictionaryActionLabel();
        tab.Controls.Add(_dictionaryPhraseInput);

        tab.Controls.Add(MakeLabel("Replace with", 282, 92, 270, 20));
        _dictionaryReplacementInput.Location = new Point(282, 114);
        _dictionaryReplacementInput.Size = new Size(270, 27);
        _dictionaryReplacementInput.MaxLength = LocalDictionaryText.MaximumReplacementLength;
        ThemeManager.ApplyControl(_dictionaryReplacementInput, _settings.ThemeMode);
        tab.Controls.Add(_dictionaryReplacementInput);

        tab.Controls.Add(MakeLabel("Apply when spoken language is", 18, 150, 252, 20));
        _dictionaryLanguageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _dictionaryLanguageCombo.Location = new Point(18, 172);
        _dictionaryLanguageCombo.Size = new Size(180, 28);
        _dictionaryLanguageCombo.Items.AddRange(["Any / Auto / mixed", "Persian only", "English only"]);
        _dictionaryLanguageCombo.SelectedIndex = 0;
        _dictionaryLanguageCombo.SelectedIndexChanged += (_, _) => UpdateDictionaryActionLabel();
        tab.Controls.Add(_dictionaryLanguageCombo);

        _dictionaryAddUpdateButton.Text = "Add rule";
        _dictionaryAddUpdateButton.Location = new Point(212, 171);
        _dictionaryAddUpdateButton.Size = new Size(104, 30);
        StyleButton(_dictionaryAddUpdateButton, primary: true);
        _dictionaryAddUpdateButton.Click += (_, _) => AddOrUpdateDictionaryEntry();
        tab.Controls.Add(_dictionaryAddUpdateButton);

        _dictionaryRemoveButton.Text = "Remove";
        _dictionaryRemoveButton.Location = new Point(322, 171);
        _dictionaryRemoveButton.Size = new Size(84, 30);
        StyleButton(_dictionaryRemoveButton, primary: false);
        _dictionaryRemoveButton.Click += (_, _) => RemoveDictionaryEntry();
        tab.Controls.Add(_dictionaryRemoveButton);

        _dictionaryImportButton.Text = "Import…";
        _dictionaryImportButton.Location = new Point(412, 171);
        _dictionaryImportButton.Size = new Size(66, 30);
        StyleButton(_dictionaryImportButton, primary: false);
        _dictionaryImportButton.Click += (_, _) => ImportDictionary();
        tab.Controls.Add(_dictionaryImportButton);

        _dictionaryExportButton.Text = "Export…";
        _dictionaryExportButton.Location = new Point(484, 171);
        _dictionaryExportButton.Size = new Size(68, 30);
        StyleButton(_dictionaryExportButton, primary: false);
        _dictionaryExportButton.Click += (_, _) => ExportDictionary();
        tab.Controls.Add(_dictionaryExportButton);

        _dictionaryList.View = View.Details;
        _dictionaryList.CheckBoxes = true;
        _dictionaryList.FullRowSelect = true;
        _dictionaryList.MultiSelect = false;
        _dictionaryList.HideSelection = false;
        _dictionaryList.Location = new Point(18, 210);
        _dictionaryList.Size = new Size(534, 245);
        _dictionaryList.Columns.Add("Recognized phrase", 180);
        _dictionaryList.Columns.Add("Replacement", 220);
        _dictionaryList.Columns.Add("Scope", 98);
        _dictionaryList.Tag = "list";
        _dictionaryList.SelectedIndexChanged += (_, _) => LoadSelectedDictionaryEntry();
        _dictionaryList.ItemChecked += (_, e) =>
        {
            if (_loadingDictionaryEntries || e.Item.Tag is not int index ||
                index < 0 || index >= _settings.LocalDictionaryEntries.Count) return;
            _settings.LocalDictionaryEntries[index].Enabled = e.Item.Checked;
            SaveDictionarySettings();
        };
        tab.Controls.Add(_dictionaryList);

        _dictionaryApplyToDirectTypingCheckBox.Text = "Also apply dictionary rules to global-shortcut direct typing";
        _dictionaryApplyToDirectTypingCheckBox.AutoSize = false;
        _dictionaryApplyToDirectTypingCheckBox.Location = new Point(18, 462);
        _dictionaryApplyToDirectTypingCheckBox.Size = new Size(534, 25);
        _dictionaryApplyToDirectTypingCheckBox.Checked = _settings.ApplyLocalDictionaryToDirectTyping;
        _dictionaryApplyToDirectTypingCheckBox.CheckedChanged += (_, _) =>
        {
            _settings.ApplyLocalDictionaryToDirectTyping = _dictionaryApplyToDirectTypingCheckBox.Checked;
            SaveDictionarySettings();
        };
        tab.Controls.Add(_dictionaryApplyToDirectTypingCheckBox);

        var privacyNote = new Label
        {
            Text = "Stored in local settings.json. Import accepts JSON only (up to 1 MB). In Auto language mode, only rules scoped to Any / Auto / mixed are used; use that scope for technical terms in mixed Persian-English speech.",
            Location = new Point(18, 490),
            Size = new Size(534, 42),
            ForeColor = Color.FromArgb(100, 116, 139),
            Tag = "muted"
        };
        tab.Controls.Add(privacyNote);

        RefreshDictionaryList();
    }

    private int SelectedDictionaryIndex => _dictionaryList.SelectedItems.Count == 0
        ? -1 : _dictionaryList.SelectedItems[0].Tag is int index ? index : -1;

    private void RefreshDictionaryList(string? selectPhrase = null)
    {
        _loadingDictionaryEntries = true;
        try
        {
            _dictionaryList.BeginUpdate();
            _dictionaryList.Items.Clear();
            for (var i = 0; i < _settings.LocalDictionaryEntries.Count; i++)
            {
                var entry = _settings.LocalDictionaryEntries[i];
                var item = new ListViewItem(entry.Phrase) { Checked = entry.Enabled, Tag = i };
                item.SubItems.Add(entry.Replacement);
                item.SubItems.Add(entry.Language switch
                {
                    LocalDictionaryText.PersianLanguage => "Persian",
                    LocalDictionaryText.EnglishLanguage => "English",
                    _ => "Any / Auto"
                });
                _dictionaryList.Items.Add(item);
                if (!string.IsNullOrWhiteSpace(selectPhrase) &&
                    string.Equals(entry.Phrase, selectPhrase, StringComparison.OrdinalIgnoreCase))
                    item.Selected = true;
            }
        }
        finally
        {
            _dictionaryList.EndUpdate();
            _loadingDictionaryEntries = false;
        }
        LoadSelectedDictionaryEntry();
    }

    private void LoadSelectedDictionaryEntry()
    {
        var index = SelectedDictionaryIndex;
        if (index < 0 || index >= _settings.LocalDictionaryEntries.Count)
        {
            _dictionaryPhraseInput.Clear();
            _dictionaryReplacementInput.Clear();
            _dictionaryLanguageCombo.SelectedIndex = 0;
            _dictionaryAddUpdateButton.Text = "Add rule";
            _dictionaryRemoveButton.Enabled = false;
            return;
        }

        var entry = _settings.LocalDictionaryEntries[index];
        _dictionaryPhraseInput.Text = entry.Phrase;
        _dictionaryReplacementInput.Text = entry.Replacement;
        _dictionaryLanguageCombo.SelectedIndex = entry.Language switch
        {
            LocalDictionaryText.PersianLanguage => 1,
            LocalDictionaryText.EnglishLanguage => 2,
            _ => 0
        };
        _dictionaryAddUpdateButton.Text = "Update";
        _dictionaryRemoveButton.Enabled = true;
        UpdateDictionaryActionLabel();
    }

    private void UpdateDictionaryActionLabel()
    {
        var index = SelectedDictionaryIndex;
        if (index < 0 || index >= _settings.LocalDictionaryEntries.Count)
        {
            _dictionaryAddUpdateButton.Text = "Add rule";
            return;
        }

        var selected = _settings.LocalDictionaryEntries[index];
        var language = _dictionaryLanguageCombo.SelectedIndex switch
        {
            1 => LocalDictionaryText.PersianLanguage,
            2 => LocalDictionaryText.EnglishLanguage,
            _ => LocalDictionaryText.AnyLanguage
        };
        _dictionaryAddUpdateButton.Text =
            string.Equals(selected.Phrase, _dictionaryPhraseInput.Text.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(selected.Language, language, StringComparison.OrdinalIgnoreCase)
                ? "Update" : "Add rule";
    }

    private void AddOrUpdateDictionaryEntry()
    {
        var language = _dictionaryLanguageCombo.SelectedIndex switch
        {
            1 => LocalDictionaryText.PersianLanguage,
            2 => LocalDictionaryText.EnglishLanguage,
            _ => LocalDictionaryText.AnyLanguage
        };
        if (!LocalDictionaryText.TryValidateEntry(_dictionaryPhraseInput.Text,
                _dictionaryReplacementInput.Text, language, out var newEntry, out var error))
        {
            MessageBox.Show(this, error, "Dictionary rule", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selected = SelectedDictionaryIndex;
        if (selected >= 0 && selected < _settings.LocalDictionaryEntries.Count)
        {
            var currentlySelected = _settings.LocalDictionaryEntries[selected];
            // Editing the phrase/scope creates a new rule; changing only the replacement updates the selected rule.
            if (!string.Equals(currentlySelected.Phrase, newEntry.Phrase, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(currentlySelected.Language, newEntry.Language, StringComparison.OrdinalIgnoreCase))
                selected = -1;
        }
        var duplicate = _settings.LocalDictionaryEntries.FindIndex(entry =>
            string.Equals(entry.Phrase, newEntry.Phrase, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Language, newEntry.Language, StringComparison.OrdinalIgnoreCase));
        if (duplicate >= 0 && duplicate != selected)
        {
            MessageBox.Show(this, "A rule for this phrase and scope already exists. Select it to update the replacement.",
                "Dictionary rule already exists", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (selected >= 0 && selected < _settings.LocalDictionaryEntries.Count)
        {
            newEntry.Enabled = _settings.LocalDictionaryEntries[selected].Enabled;
            _settings.LocalDictionaryEntries[selected] = newEntry;
        }
        else
        {
            if (_settings.LocalDictionaryEntries.Count >= LocalDictionaryText.MaximumEntries)
            {
                MessageBox.Show(this, $"The dictionary limit is {LocalDictionaryText.MaximumEntries} rules.",
                    "Dictionary is full", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _settings.LocalDictionaryEntries.Add(newEntry);
        }

        _settings.LocalDictionaryEntries = LocalDictionaryText.NormalizeEntries(_settings.LocalDictionaryEntries);
        SaveDictionarySettings();
        RefreshDictionaryList(newEntry.Phrase);
        _dictionaryPhraseInput.Focus();
        _dictionaryPhraseInput.SelectAll();
    }

    private void RemoveDictionaryEntry()
    {
        var index = SelectedDictionaryIndex;
        if (index < 0 || index >= _settings.LocalDictionaryEntries.Count) return;
        _settings.LocalDictionaryEntries.RemoveAt(index);
        SaveDictionarySettings();
        RefreshDictionaryList();
        _dictionaryPhraseInput.Clear();
        _dictionaryReplacementInput.Clear();
        _dictionaryPhraseInput.Focus();
    }

    private void ImportDictionary()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import local dictionary JSON",
            Filter = "MadNegar dictionary (*.json)|*.json|JSON files (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length > LocalDictionaryText.MaximumImportBytes)
                throw new InvalidDataException("Import is larger than 1 MB.");
            var imported = LocalDictionaryText.DeserializeImport(File.ReadAllText(dialog.FileName));
            var answer = MessageBox.Show(this,
                $"Replace the current dictionary with {imported.Count} imported rule(s)? Disabled rules are preserved in the file.",
                "Replace local dictionary", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            _settings.LocalDictionaryEntries = LocalDictionaryText.NormalizeEntries(imported);
            SaveDictionarySettings();
            RefreshDictionaryList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not import the dictionary. " + ex.Message,
                "Dictionary import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportDictionary()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export local dictionary JSON",
            Filter = "MadNegar dictionary (*.json)|*.json",
            DefaultExt = "json",
            AddExtension = true,
            FileName = "MadNegar-Dictionary.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var json = JsonSerializer.Serialize(LocalDictionaryText.NormalizeEntries(_settings.LocalDictionaryEntries),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dialog.FileName, json, new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not export the dictionary. " + ex.Message,
                "Dictionary export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveDictionarySettings()
    {
        try { AppSettingsStore.Save(_rootDirectory, _settings); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The dictionary could not be saved to local settings. " + ex.Message,
                "Dictionary save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ConfigurePersianTextCheckBox(ThemedCheckBox checkBox, string label, int x, int y,
        Func<bool> readValue, Action<bool> writeValue)
    {
        checkBox.Text = label;
        checkBox.AutoSize = false;
        checkBox.Location = new Point(x, y);
        checkBox.Size = new Size(535, 25);
        checkBox.Checked = readValue();
        checkBox.CheckedChanged += (_, _) =>
        {
            writeValue(checkBox.Checked);
            try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
        };
        _persianTextPage.Controls.Add(checkBox);
    }

    private const string GlobalHotkeyCaptureInstructions = "Click Change, then press your desired combination (for example Ctrl+Shift+J). Use at least one modifier and one key. Windows-reserved combinations may not be available.";

    private void UpdateGlobalHotkeyControlsEnabled()
    {
        var enabled = _settings.GlobalHotkeyEnabled;
        _globalHotkeyShortcutLabel.Enabled = enabled && !_capturingGlobalHotkey;
        _changeGlobalHotkeyButton.Enabled = enabled;
        _resetGlobalHotkeyButton.Enabled = enabled;
    }

    private void StartGlobalHotkeyCapture()
    {
        if (!_settings.GlobalHotkeyEnabled) return;
        _capturingGlobalHotkey = true;
        _globalHotkeyShortcutLabel.Text = "Press keys…";
        _globalHotkeyShortcutLabel.Enabled = true;
        _globalHotkeyCaptureNote.Text = "Press the complete shortcut now. Press Esc to cancel.";
        _changeGlobalHotkeyButton.Text = "Cancel";
        Focus();
    }

    private void CancelGlobalHotkeyCapture()
    {
        _capturingGlobalHotkey = false;
        _globalHotkeyShortcutLabel.Text = GlobalHotkeySupport.NormalizeShortcut(_settings.GlobalHotkeyShortcut);
        _globalHotkeyCaptureNote.Text = GlobalHotkeyCaptureInstructions;
        _changeGlobalHotkeyButton.Text = "Change…";
        UpdateGlobalHotkeyControlsEnabled();
    }

    private void HandleGlobalHotkeyCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_capturingGlobalHotkey) return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        if (e.KeyCode == Keys.Escape)
        {
            CancelGlobalHotkeyCapture();
            return;
        }

        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            _globalHotkeyShortcutLabel.Text = "Modifier…";
            _globalHotkeyCaptureNote.Text = "Keep the modifier key(s) held and press the main key.";
            return;
        }

        if (!GlobalHotkeySupport.TryCreateShortcut(e.KeyCode, e.Control, e.Alt, e.Shift,
                GlobalHotkeySupport.IsWindowsKeyDown(), out var shortcut, out var error))
        {
            _globalHotkeyCaptureNote.Text = error;
            return;
        }

        _capturingGlobalHotkey = false;
        _settings.GlobalHotkeyShortcut = shortcut;
        _globalHotkeyShortcutLabel.Text = shortcut;
        _globalHotkeyCaptureNote.Text = GlobalHotkeyCaptureInstructions;
        _changeGlobalHotkeyButton.Text = "Change…";
        UpdateGlobalHotkeyControlsEnabled();
        try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
    }

    private void PopulateTranscriptionModelChoices()
    {
        if (_liveModelCombo.IsDisposed || _finalModelCombo.IsDisposed) return;
        var whisperModels = ModelCatalog.Models.Where(m => ModelValidation.IsWhisperModelReady(ModelPath(m))).ToList();
        var providers = ProviderModelCatalog.All.Where(m => !m.IsWhisper && IsDownloaded(m))
            .Where(m => m.Provider is "Vosk" or "Diba-STT" or "faster-whisper")
            .ToList();
        var choices = whisperModels.Select(m => new SpeechModelChoice(m, null))
            .Concat(providers.Select(m => new SpeechModelChoice(null, m)))
            .Where(choice => choice.IsSupportedForFinal)
            .ToList();

        var savedFinalKey = !string.IsNullOrWhiteSpace(_settings.SelectedModelKey) ? _settings.SelectedModelKey : _settings.SelectedModelId;
        var previousFinal = !string.IsNullOrWhiteSpace(savedFinalKey)
            ? savedFinalKey
            : (_finalModelCombo.SelectedItem as SpeechModelChoice)?.Key;
        _finalModelCombo.BeginUpdate();
        _finalModelCombo.Items.Clear();
        foreach (var choice in choices) _finalModelCombo.Items.Add(choice);
        var finalIndex = choices.FindIndex(m => m.Key.Equals(previousFinal, StringComparison.OrdinalIgnoreCase) || m.Id.Equals(previousFinal, StringComparison.OrdinalIgnoreCase));
        if (finalIndex >= 0)
        {
            _finalModelCombo.SelectedIndex = finalIndex;
            _transcriptionNoteLabel.Text = "Live Preview lists complete models with a runnable preview engine. In Auto, multilingual Whisper/faster-whisper can switch languages; Vosk/Diba are listed too but preview only in their own language. Uncheck automatic fast selection to choose a model manually. faster-whisper preview is experimental and can be slower because it reloads through the local file adapter. Nemotron is download-only.";
        }
        else if (!string.IsNullOrWhiteSpace(savedFinalKey))
        {
            _finalModelCombo.SelectedIndex = -1;
            _transcriptionNoteLabel.Text = $"Saved final model '{savedFinalKey}' is not available in this version or is missing/incomplete. Re-download it or explicitly choose a supported model; MadNegar will not switch silently. Auto live preview needs multilingual Whisper.";
        }
        else if (choices.Count > 0)
        {
            var defaultIndex = choices.FindIndex(m => m.Id == "base");
            _finalModelCombo.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 0;
        }
        else
        {
            _finalModelCombo.SelectedIndex = -1;
            _transcriptionNoteLabel.Text = "No complete local models are available yet. Download a model in Settings → Downloads. Nemotron is currently download-only.";
        }
        _finalModelCombo.EndUpdate();

        var liveChoices = choices.Where(choice => choice.IsSupportedForLive)
            .Where(IsLivePreviewCompatibleForConfiguredLanguage)
            .ToList();
        var liveOptions = new List<ModelSelectionOption>
        {
            new("follow-final", "Use final model when live-capable; otherwise use fastest compatible model")
        };
        liveOptions.AddRange(liveChoices.Select(choice => new ModelSelectionOption(
            // Keep old Whisper model IDs readable from settings.json; provider-backed models use a namespaced key.
            choice.IsWhisper ? choice.Id : choice.Key,
            $"{choice.DisplayName} · {choice.Provider} · {choice.Languages}")));
        var previousLive = _settings.LivePreviewModelKey;
        _liveModelCombo.BeginUpdate();
        _liveModelCombo.Items.Clear();
        foreach (var option in liveOptions) _liveModelCombo.Items.Add(option);
        var liveIndex = liveOptions.FindIndex(option => option.Key.Equals(previousLive, StringComparison.OrdinalIgnoreCase));
        _liveModelCombo.SelectedIndex = liveIndex >= 0 ? liveIndex : 0;
        _liveModelCombo.EndUpdate();
        _liveModelCombo.Enabled = !_settings.FastLivePreviewEnabled && liveChoices.Count > 0;
    }

    private bool IsLivePreviewCompatibleForConfiguredLanguage(SpeechModelChoice choice)
    {
        // Manual Live Preview may use language-specific Vosk/Diba models even while the final
        // transcription language is Auto. They run in the model's native language only; they do
        // not detect/switch languages. Multilingual Whisper/faster-whisper are needed for true
        // automatic language switching in the preview stream.
        if (string.Equals(_settings.SpokenLanguage, "fa", StringComparison.OrdinalIgnoreCase))
            return !choice.EnglishOnly && choice.Languages.Contains("Persian", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(_settings.SpokenLanguage, "en", StringComparison.OrdinalIgnoreCase))
            return choice.Languages.Contains("English", StringComparison.OrdinalIgnoreCase);
        if (choice.IsWhisper) return !choice.EnglishOnly;
        return choice.IsSupportedForLive && (choice.IsFasterWhisper || choice.UsesVoskRuntime);
    }

    private void ApplySelectedAudioProfile()
    {
        if (_applyingAudioProfile || _audioProfileCombo.SelectedIndex < 0) return;
        var profile = _audioProfileCombo.SelectedIndex switch
        {
            1 => AudioProfilePolicy.QuietRoom,
            2 => AudioProfilePolicy.NoisyRoom,
            _ => AudioProfilePolicy.Custom
        };

        if (profile == AudioProfilePolicy.Custom)
        {
            _settings.AudioProfile = AudioProfilePolicy.Custom;
            try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
            return;
        }

        _applyingAudioProfile = true;
        try
        {
            _settings.AudioProfile = AudioProfilePolicy.Normalize(profile);
            if (AudioProfilePolicy.TryGetPreset(profile, out var preset))
            {
                _settings.MicrophoneBoostDb = preset.MicrophoneBoostDb;
                _settings.AutoLevelQuietSpeech = preset.AutoLevelQuietSpeech;
                _settings.ReduceLowFrequencyRumble = preset.ReduceLowFrequencyRumble;
            }
            _microphoneBoost.Value = Math.Clamp(_settings.MicrophoneBoostDb, _microphoneBoost.Minimum, _microphoneBoost.Maximum);
            _autoLevelCheckBox.Checked = _settings.AutoLevelQuietSpeech;
            _reduceRumbleCheckBox.Checked = _settings.ReduceLowFrequencyRumble;
            UpdateMicrophoneBoostLabel();
            AppSettingsStore.Save(_rootDirectory, _settings);
        }
        catch { }
        finally { _applyingAudioProfile = false; }
    }

    private void SaveAudioProcessingSettings()
    {
        if (_applyingAudioProfile) return;
        _settings.MicrophoneBoostDb = _microphoneBoost.Value;
        _settings.AutoLevelQuietSpeech = _autoLevelCheckBox.Checked;
        _settings.ReduceLowFrequencyRumble = _reduceRumbleCheckBox.Checked;
        _settings.AudioProfile = AudioProfilePolicy.Custom;
        if (_audioProfileCombo.SelectedIndex != 0)
        {
            _applyingAudioProfile = true;
            try { _audioProfileCombo.SelectedIndex = 0; }
            finally { _applyingAudioProfile = false; }
        }
        UpdateMicrophoneBoostLabel();
        try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
    }

    private void UpdateMicrophoneBoostLabel()
    {
        if (_microphoneBoostValue.IsDisposed) return;
        _microphoneBoostValue.Text = $"+{_microphoneBoost.Value} dB";
    }

    private void BuildDownloadsTab(Control tab)
    {
        var description = new Label
        {
            Text = "Browse speech models by provider. Select a provider to see its models and download links.",
            Location = new Point(14, 8),
            Size = new Size(560, 23),
            ForeColor = Color.FromArgb(71, 85, 105)
        };
        description.Tag = "muted";
        tab.Controls.Add(description);

        var providerLabel = MakeLabel("Provider", 14, 35, 70, 25);
        tab.Controls.Add(providerLabel);
        _providerCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _providerCombo.Location = new Point(82, 32);
        _providerCombo.Size = new Size(315, 30);
        _providerCombo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _providerCombo.SelectedIndexChanged += (_, _) => { RefreshDownloadRows(); RefreshSourceOptions(); };
        tab.Controls.Add(_providerCombo);

        _openProviderPageButton.Text = "Open model page ↗";
        StyleButton(_openProviderPageButton, primary: false);
        _openProviderPageButton.Location = new Point(405, 32);
        _openProviderPageButton.Size = new Size(155, 30);
        _openProviderPageButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _openProviderPageButton.Click += (_, _) => OpenSelectedProviderPage();
        tab.Controls.Add(_openProviderPageButton);

        var sourceLabel = MakeLabel("Source", 14, 67, 70, 25);
        tab.Controls.Add(sourceLabel);
        _downloadSourceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _downloadSourceCombo.Location = new Point(82, 64);
        _downloadSourceCombo.Size = new Size(315, 30);
        _downloadSourceCombo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        tab.Controls.Add(_downloadSourceCombo);

        _modelsList.Location = new Point(12, 100);
        _modelsList.Size = new Size(546, 260);
        _modelsList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _modelsList.View = View.Details;
        _modelsList.FullRowSelect = true;
        _modelsList.GridLines = false;
        _modelsList.HideSelection = false;
        _modelsList.MultiSelect = false;
        _modelsList.Columns.Add("Model / package", 205);
        _modelsList.Columns.Add("Approx. size", 105);
        _modelsList.Columns.Add("Language", 110);
        _modelsList.Columns.Add("Status", 120);
        _modelsList.SelectedIndexChanged += (_, _) => { RefreshSourceOptions(); UpdateDownloadControls(); };
        tab.Controls.Add(_modelsList);

        _selectedModelStatus.Location = new Point(14, 365);
        _selectedModelStatus.Size = new Size(540, 34);
        _selectedModelStatus.ForeColor = Color.FromArgb(71, 85, 105);
        _selectedModelStatus.AutoEllipsis = true;
        tab.Controls.Add(_selectedModelStatus);

        _downloadButton.Text = "Download Selected";
        StyleButton(_downloadButton, primary: true);
        _downloadButton.Location = new Point(12, 402);
        _downloadButton.Size = new Size(220, 32);
        _downloadButton.Click += async (_, _) => await DownloadSelectedModelAsync();
        tab.Controls.Add(_downloadButton);

        _cancelDownloadButton.Text = "Cancel Download";
        StyleButton(_cancelDownloadButton, primary: false);
        _cancelDownloadButton.Location = new Point(242, 402);
        _cancelDownloadButton.Size = new Size(135, 32);
        _cancelDownloadButton.Enabled = false;
        _cancelDownloadButton.Click += (_, _) => _downloadCts?.Cancel();
        tab.Controls.Add(_cancelDownloadButton);

        _downloadProgress.Location = new Point(13, 443);
        _downloadProgress.Size = new Size(545, 15);
        _downloadProgress.Minimum = 0;
        _downloadProgress.Maximum = 100;
        tab.Controls.Add(_downloadProgress);

        _downloadProgressLabel.Location = new Point(14, 461);
        _downloadProgressLabel.Size = new Size(544, 25);
        _downloadProgressLabel.Text = "Select a model to see its download status.";
        _downloadProgressLabel.ForeColor = Color.FromArgb(100, 116, 139);
        _downloadProgressLabel.AutoEllipsis = true;
        tab.Controls.Add(_downloadProgressLabel);

        var integrationNote = new Label
        {
            Text = "Whisper.cpp, Vosk and Diba-STT are available for final transcription and supported live previews. faster-whisper is available for final transcription; Nemotron is still download-only.",
            Location = new Point(14, 492),
            Size = new Size(540, 42),
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(100, 116, 139)
        };
        integrationNote.Tag = "muted";
        tab.Controls.Add(integrationNote);
    }

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero); // WM_NCLBUTTONDOWN / HTCAPTION
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(ThemeManager.ActivePalette.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, ClientSize.Width - 1), Math.Max(0, ClientSize.Height - 1));
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    private static Label MakeLabel(string text, int x, int y, int width, int height) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(width, height),
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        ForeColor = Color.FromArgb(51, 65, 85)
    };

    private static void StyleButton(Button button, bool primary)
    {
        button.Tag = primary ? "primary" : "secondary";
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        button.BackColor = primary ? Color.FromArgb(37, 99, 235) : Color.White;
        button.ForeColor = primary ? Color.White : Color.FromArgb(51, 65, 85);
        button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private void RefreshMicrophoneDevices()
    {
        var savedId = _settings.SelectedMicrophoneId;
        var savedName = _settings.SelectedMicrophoneName;
        var devices = new List<MicrophoneChoice>();
        string? defaultCommunicationId = null;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            try
            {
                using var defaultEndpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                defaultCommunicationId = defaultEndpoint.ID;
            }
            catch
            {
                try
                {
                    using var defaultEndpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                    defaultCommunicationId = defaultEndpoint.ID;
                }
                catch { }
            }

            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (MMDevice endpoint in endpoints)
            {
                try { devices.Add(new MicrophoneChoice(endpoint.ID, endpoint.FriendlyName)); }
                finally { endpoint.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            _microphoneStatus.Text = "Could not enumerate Windows audio inputs: " + ex.Message;
            _microphoneStatus.ForeColor = Color.FromArgb(180, 83, 9);
        }

        _microphoneCombo.SelectedIndexChanged -= MicrophoneComboSelectedIndexChanged;
        _microphoneCombo.BeginUpdate();
        _microphoneCombo.Items.Clear();
        foreach (var device in devices) _microphoneCombo.Items.Add(device);
        _microphoneCombo.EndUpdate();
        _microphoneCombo.SelectedIndexChanged += MicrophoneComboSelectedIndexChanged;

        var savedIndex = devices.FindIndex(d => string.Equals(d.Id, savedId, StringComparison.OrdinalIgnoreCase));
        if (savedIndex < 0)
            savedIndex = devices.FindIndex(d => string.Equals(d.Name, savedName, StringComparison.OrdinalIgnoreCase));
        if (savedIndex < 0 && !string.IsNullOrWhiteSpace(defaultCommunicationId))
            savedIndex = devices.FindIndex(d => string.Equals(d.Id, defaultCommunicationId, StringComparison.OrdinalIgnoreCase));

        if (devices.Count > 0)
        {
            _microphoneCombo.SelectedIndex = savedIndex >= 0 ? savedIndex : 0;
            _microphoneStatus.Text = $"{devices.Count} active Windows audio input(s) found. Select one and start the test.";
            _microphoneStatus.ForeColor = Color.FromArgb(22, 101, 52);
        }
        else
        {
            _microphoneStatus.Text = "No active microphone endpoints found. Check Windows Sound settings and press Refresh.";
            _microphoneStatus.ForeColor = Color.FromArgb(180, 83, 9);
            _microphoneLevel.Value = 0;
        }
        RefreshAudioControls();
        SaveSelectedMicrophone();
    }

    private void MicrophoneComboSelectedIndexChanged(object? sender, EventArgs e) => SaveSelectedMicrophone();

    private void SaveSelectedMicrophone()
    {
        if (_microphoneCombo.SelectedItem is MicrophoneChoice choice)
        {
            _settings.SelectedMicrophoneId = choice.Id;
            _settings.SelectedMicrophoneName = choice.Name;
            try { AppSettingsStore.Save(_rootDirectory, _settings); } catch { }
        }
        RefreshAudioControls();
    }

    private void StartMicrophoneTest()
    {
        if (_microphoneCombo.SelectedItem is not MicrophoneChoice choice)
        {
            _microphoneStatus.Text = "No microphone selected. Connect or select an input device first.";
            _microphoneStatus.ForeColor = Color.FromArgb(180, 83, 9);
            return;
        }
        if (_testWaveIn is not null) return;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _testDevice = enumerator.GetDevice(choice.Id);
            var capture = new WasapiCapture(_testDevice, true, 50);
            capture.DataAvailable += OnMicrophoneTestDataAvailable;
            capture.RecordingStopped += OnMicrophoneTestStopped;
            _testWaveIn = capture;
            _isTestingMicrophone = true;
            _microphoneLevel.Value = 0;
            _unsupportedAudioFormatReported = false;
            _microphoneStatus.Text = $"Connected to {choice.Name}. Listening — say “1, 2, 3”.";
            _microphoneStatus.ForeColor = Color.FromArgb(22, 101, 52);
            capture.StartRecording();
        }
        catch (Exception ex)
        {
            _isTestingMicrophone = false;
            var failed = _testWaveIn;
            _testWaveIn = null;
            try { failed?.Dispose(); } catch { }
            try { _testDevice?.Dispose(); } catch { }
            _testDevice = null;
            _microphoneStatus.Text = "Microphone test could not start. Check Windows microphone permissions and device availability.";
            _microphoneStatus.ForeColor = Color.FromArgb(180, 83, 9);
            MessageBox.Show(this, ex.Message, "Microphone test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshAudioControls();
    }

    private void OnMicrophoneTestDataAvailable(object? sender, WaveInEventArgs e)
    {
        var capture = sender as WasapiCapture;
        if (!_isTestingMicrophone || capture is null || e.BytesRecorded <= 0) return;
        if (!AudioProcessor.TryMeasurePeak(e.Buffer, e.BytesRecorded, capture.WaveFormat, _settings.MicrophoneBoostDb, out var peak))
        {
            if (_unsupportedAudioFormatReported) return;
            _unsupportedAudioFormatReported = true;
            if (!IsDisposed && IsHandleCreated)
            {
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (!_isTestingMicrophone || IsDisposed) return;
                        _microphoneLevel.Value = 0;
                        _microphoneStatus.Text = $"Unsupported audio format for the level meter: {capture.WaveFormat}. Try another microphone format in Windows Sound settings.";
                        _microphoneStatus.ForeColor = Color.FromArgb(180, 83, 9);
                    }));
                }
                catch (InvalidOperationException) { }
            }
            return;
        }
        var percent = (int)Math.Clamp(peak * 100d, 0, 100);
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (!_isTestingMicrophone || IsDisposed) return;
                _microphoneLevel.Value = percent;
                if (percent >= 3)
                {
                    _microphoneStatus.Text = $"Microphone connected · boosted input level {percent}%.";
                    _microphoneStatus.ForeColor = percent >= 95
                        ? Color.FromArgb(180, 83, 9)
                        : Color.FromArgb(22, 101, 52);
                }
                else
                {
                    _microphoneStatus.Text = "Device opened but input is still very low. Try another endpoint or raise Windows microphone level.";
                    _microphoneStatus.ForeColor = Color.FromArgb(180, 83, 9);
                }
            }));
        }
        catch (InvalidOperationException) { }
    }

    private void StopMicrophoneTest()
    {
        _isTestingMicrophone = false;
        _microphoneLevel.Value = 0;
        var capture = _testWaveIn;
        if (capture is null)
        {
            RefreshAudioControls();
            return;
        }
        try { capture.StopRecording(); }
        catch
        {
            try { capture.Dispose(); } catch { }
            if (ReferenceEquals(_testWaveIn, capture)) _testWaveIn = null;
        }
        _microphoneStatus.Text = "Microphone test stopped.";
        _microphoneStatus.ForeColor = Color.FromArgb(71, 85, 105);
        RefreshAudioControls();
    }

    private void OnMicrophoneTestStopped(object? sender, StoppedEventArgs e)
    {
        if (sender is WasapiCapture capture)
        {
            capture.DataAvailable -= OnMicrophoneTestDataAvailable;
            capture.RecordingStopped -= OnMicrophoneTestStopped;
            try { capture.Dispose(); } catch { }
        }
        if (ReferenceEquals(_testWaveIn, sender)) _testWaveIn = null;
        try { _testDevice?.Dispose(); } catch { }
        _testDevice = null;
        if (_isClosing || IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (!_isClosing && !IsDisposed) RefreshAudioControls();
                if (e.Exception is not null && !_isClosing && !IsDisposed)
                {
                    _microphoneStatus.Text = "Microphone test stopped with an error: " + e.Exception.Message;
                    _microphoneStatus.ForeColor = Color.FromArgb(180, 83, 9);
                }
            }));
        }
        catch (InvalidOperationException) { }
    }

    private void RefreshAudioControls()
    {
        var hasDevice = _microphoneCombo.SelectedItem is MicrophoneChoice;
        _microphoneCombo.Enabled = !_isTestingMicrophone && _testWaveIn is null && hasDevice;
        _refreshDevicesButton.Enabled = !_isTestingMicrophone && _testWaveIn is null;
        _startMicTestButton.Text = _isTestingMicrophone ? "■  Stop Mic Test" : "▶  Start Mic Test";
        _startMicTestButton.Tag = _isTestingMicrophone ? "danger" : "primary";
        _startMicTestButton.Enabled = _isTestingMicrophone || (_testWaveIn is null && hasDevice);
        ThemeManager.ApplyControl(_startMicTestButton, _settings.ThemeMode);
    }

    private void PopulateProviders()
    {
        _providerCombo.BeginUpdate();
        _providerCombo.Items.Clear();
        foreach (var provider in ProviderModelCatalog.Providers) _providerCombo.Items.Add(provider);
        _providerCombo.EndUpdate();
        if (_providerCombo.Items.Count > 0) _providerCombo.SelectedIndex = 0;
    }

    private void RefreshDownloadRows()
    {
        var keepId = SelectedDownloadItem?.Id;
        var provider = _providerCombo.SelectedItem as string;
        var models = ProviderModelCatalog.All.Where(m => string.Equals(m.Provider, provider, StringComparison.Ordinal)).ToList();
        _modelsList.BeginUpdate();
        _modelsList.Items.Clear();
        foreach (var model in models)
        {
            var downloaded = IsDownloaded(model);
            var storagePath = model.IsWhisper ? ModelPath(model.WhisperModel!) : DownloadDirectory(model);
            var hasLocalArtifacts = model.IsWhisper
                ? File.Exists(storagePath) || File.Exists(storagePath + ".partial")
                : Directory.Exists(storagePath) || File.Exists(storagePath + ".zip.partial");
            var status = downloaded
                ? model.IsWhisper ? "Downloaded — runtime setup on first use"
                : model.Provider is "faster-whisper" or "Oncilla / Nemotron (Persian)" ? "Downloaded — runtime setup needed"
                : "Ready"
                : hasLocalArtifacts ? "Incomplete" : "Available to download";
            var item = new ListViewItem(model.DisplayName);
            item.SubItems.Add(model.ApproxSize);
            item.SubItems.Add(model.Languages);
            item.SubItems.Add(status);
            item.Tag = model;
            _modelsList.Items.Add(item);
            if (string.Equals(model.Id, keepId, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
        }
        _modelsList.EndUpdate();
        if (_modelsList.SelectedItems.Count == 0 && _modelsList.Items.Count > 0)
            _modelsList.Items[0].Selected = true;
        RefreshSourceOptions();
        UpdateDownloadControls();
        PopulateTranscriptionModelChoices();
        PopulateDualMixedModelChoices();
        UpdateDualModelModeControls();
    }

    private void UpdateDownloadControls()
    {
        var item = SelectedDownloadItem;
        var downloaded = item is not null && IsDownloaded(item);
        _downloadButton.Enabled = !_isDownloading && item is not null && !downloaded;
        _cancelDownloadButton.Enabled = _isDownloading;
        _modelsList.Enabled = !_isDownloading;
        _providerCombo.Enabled = !_isDownloading;
        _downloadSourceCombo.Enabled = !_isDownloading;
        _openProviderPageButton.Enabled = !_isDownloading && item is not null;
        if (item is null)
        {
            _selectedModelStatus.Text = "Select a model.";
            return;
        }

        var note = item.Description ?? string.Empty;
        if (downloaded)
        {
            _selectedModelStatus.Text = item.IsWhisper
                ? $"{item.DisplayName} is downloaded. The local whisper.cpp runtime is installed on first use. {note}"
                : item.Provider is "Vosk" or "Diba-STT"
                    ? $"{item.DisplayName} is ready for offline final transcription through the local Kaldi/Vosk decoder. {note}"
                    : item.Provider == "faster-whisper"
                        ? $"{item.DisplayName} is downloaded. Its private Python runtime is installed automatically on first use. {note}"
                        : $"{item.DisplayName} is downloaded, but transcription is not supported until its runtime adapter is implemented. {note}";
        }
        else
        {
            _selectedModelStatus.Text = $"{item.DisplayName} · {item.ApproxSize} · {item.Languages} · {item.License}. {note}";
        }
    }

    private void OpenSelectedProviderPage()
    {
        var item = SelectedDownloadItem;
        if (item is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(item.SourceUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open the provider page.\n\n" + ex.Message,
                "MadNegar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task DownloadSelectedModelAsync()
    {
        var item = SelectedDownloadItem;
        if (item is null || IsDownloaded(item)) return;

        using var cts = new CancellationTokenSource();
        _downloadCts = cts;
        _isDownloading = true;
        _downloadProgress.Style = ProgressBarStyle.Blocks;
        _downloadProgress.Value = 0;
        _downloadProgressLabel.Text = $"Preparing download: {item.DisplayName}";
        UpdateDownloadControls();
        try
        {
            if (item.DownloadKind == ProviderDownloadKind.WhisperBinary)
            {
                var model = item.WhisperModel ?? throw new InvalidOperationException("Whisper model metadata is missing.");
                var destination = ModelPath(model);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var partialPath = destination + ".partial";
                TryDelete(ModelValidation.WhisperCompletionMarker(destination));
                var progress = new Progress<DownloadProgress>(p => ShowDownloadProgress(item.DisplayName, p));
                await DownloadFileWithFallbackAsync(GetDirectDownloadUrls(item), partialPath, progress, cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                if (!ModelValidation.IsWhisperModelFileValid(partialPath))
                    throw new InvalidDataException($"The downloaded file for {item.DisplayName} is not a valid Whisper.cpp ggml model (it may be truncated or an HTTP error page). Choose another source and retry.");
                File.Move(partialPath, destination, overwrite: true);
                ModelValidation.WriteWhisperCompletionMarker(destination);
                _downloadProgressLabel.Text = $"Download complete and validated: {item.DisplayName} ({item.ApproxSize}).";
            }
            else if (item.DownloadKind == ProviderDownloadKind.ZipArchive)
            {
                await DownloadAndExtractZipAsync(item, cts.Token);
                _downloadProgressLabel.Text = $"Download and extraction complete: {item.DisplayName}.";
            }
            else if (item.DownloadKind == ProviderDownloadKind.HuggingFaceSnapshot)
            {
                await DownloadHuggingFaceFilesAsync(item, cts.Token);
                _downloadProgressLabel.Text = $"Download complete: {item.DisplayName}.";
            }

            _downloadProgress.Style = ProgressBarStyle.Blocks;
            _downloadProgress.MarqueeAnimationSpeed = 0;
            _downloadProgress.Value = 100;
        }
        catch (OperationCanceledException)
        {
            _downloadProgressLabel.Text = "Download cancelled. Completed files are kept so the download can be resumed.";
        }
        catch (Exception ex)
        {
            _downloadProgressLabel.Text = "Download failed. Choose another Source and retry.";
            MessageBox.Show(this,
                "The selected provider files could not be downloaded. Try Source → Automatic first; if needed, choose the official source or HF mirror.\n\n" + ex.Message,
                "MadNegar · Download details", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _downloadCts = null;
            _isDownloading = false;
            if (!_downloadProgressLabel.Text.StartsWith("Download complete:", StringComparison.Ordinal) &&
                !_downloadProgressLabel.Text.StartsWith("Download complete and validated:", StringComparison.Ordinal) &&
                !_downloadProgressLabel.Text.StartsWith("Download and extraction complete:", StringComparison.Ordinal))
            {
                _downloadProgress.Style = ProgressBarStyle.Blocks;
                _downloadProgress.MarqueeAnimationSpeed = 0;
                _downloadProgress.Value = 0;
            }
            RefreshDownloadRows();
            UpdateDownloadControls();
        }
    }

    private async Task DownloadAndExtractZipAsync(ProviderModelInfo item, CancellationToken token)
    {
        var destination = DownloadDirectory(item);
        var parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        var archivePath = destination + ".zip.partial";
        var stagingPath = destination + ".extracting";
        var installPath = destination + ".installing";
        var backupPath = destination + ".previous";
        var installCompleted = false;
        try
        {
            // Recover an old valid install if the previous process stopped between renames.
            if (!Directory.Exists(destination) && Directory.Exists(backupPath))
                Directory.Move(backupPath, destination);
            if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true);
            if (Directory.Exists(installPath)) Directory.Delete(installPath, recursive: true);
            var progress = new Progress<DownloadProgress>(p => ShowDownloadProgress(item.DisplayName, p));
            await DownloadFileWithFallbackAsync(GetDirectDownloadUrls(item), archivePath, progress, token);
            token.ThrowIfCancellationRequested();
            SafeZipExtractor.ExtractToDirectory(archivePath, stagingPath);
            token.ThrowIfCancellationRequested();

            var innerDirectories = Directory.GetDirectories(stagingPath);
            var rootFiles = Directory.GetFiles(stagingPath);
            if (innerDirectories.Length == 1 && rootFiles.Length == 0)
                Directory.Move(innerDirectories[0], installPath);
            else
                Directory.Move(stagingPath, installPath);

            if (!ModelValidation.HasKaldiModelStructure(installPath))
                throw new InvalidDataException($"The archive for {item.DisplayName} does not contain a complete Kaldi/Vosk model. Expected am/*.mdl, conf files and graph/*.fst.");
            ModelValidation.WriteProviderCompletionMarker(installPath);
            token.ThrowIfCancellationRequested();

            if (Directory.Exists(backupPath)) Directory.Delete(backupPath, recursive: true);
            var movedOldModel = false;
            if (Directory.Exists(destination))
            {
                Directory.Move(destination, backupPath);
                movedOldModel = true;
            }
            try
            {
                Directory.Move(installPath, destination);
            }
            catch
            {
                if (movedOldModel && !Directory.Exists(destination) && Directory.Exists(backupPath))
                    Directory.Move(backupPath, destination);
                throw;
            }
            if (Directory.Exists(backupPath)) Directory.Delete(backupPath, recursive: true);
            installCompleted = true;
        }
        finally
        {
            // Keep the completed archive after cancellation/extraction/validation failure so a
            // retry can diagnose/reuse it; remove it only after the validated model is installed.
            if (installCompleted) TryDelete(archivePath);
            try { if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, recursive: true); } catch { }
            try { if (Directory.Exists(installPath)) Directory.Delete(installPath, recursive: true); } catch { }
            // If replacement did not complete, restore the previously valid install where possible.
            try
            {
                if (!Directory.Exists(destination) && Directory.Exists(backupPath)) Directory.Move(backupPath, destination);
            }
            catch { }
        }
    }

    private async Task DownloadHuggingFaceFilesAsync(ProviderModelInfo item, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(item.HuggingFaceRepo))
            throw new InvalidOperationException("Hugging Face repository metadata is missing.");

        var repo = item.HuggingFaceRepo;
        var files = await GetHuggingFaceFilesWithFallbackAsync(item, token);
        var directory = DownloadDirectory(item);
        Directory.CreateDirectory(directory);
        // An interrupted refresh must never inherit an older marker and appear complete.
        TryDelete(CompletionMarker(item));
        var totalBytes = files.Sum(f => Math.Max(0, f.Size));
        long alreadyCompleted = 0;
        foreach (var file in files)
        {
            var relative = item.HuggingFaceFolderPrefix is { Length: > 0 } prefix
                ? file.Path[prefix.Length..]
                : file.Path;
            relative = relative.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(directory, relative));
            var basePath = Path.GetFullPath(directory + Path.DirectorySeparatorChar);
            if (!target.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The provider repository returned an unsafe file path.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target) && file.Size > 0 && new FileInfo(target).Length == file.Size)
            {
                alreadyCompleted += file.Size;
                continue;
            }

            var partial = target + ".partial";
            var encodedPath = string.Join("/", file.Path.Split('/').Select(Uri.EscapeDataString));
            var hosts = GetHuggingFaceHosts();
            // Some proxies/mirrors return HTTP 400 for the download=true query. Retry the canonical
            // resolve URL without the query before moving to the next host.
            var urls = hosts.SelectMany(host => new[]
            {
                $"https://{host}/{repo}/resolve/main/{encodedPath}?download=true",
                $"https://{host}/{repo}/resolve/main/{encodedPath}"
            }).ToArray();
            long fileReceived = 0;
            var currentFileBase = alreadyCompleted;
            var progress = new Progress<DownloadProgress>(p =>
            {
                fileReceived = p.ReceivedBytes;
                var current = currentFileBase + fileReceived;
                ShowDownloadProgress(item.DisplayName, new DownloadProgress(current,
                    totalBytes > 0 ? totalBytes : null));
            });
            await DownloadFileWithFallbackAsync(urls, partial, progress, token, file.Size > 0 ? file.Size : null);
            token.ThrowIfCancellationRequested();
            File.Move(partial, target, overwrite: true);
            alreadyCompleted += file.Size > 0 ? file.Size : fileReceived;
        }
        token.ThrowIfCancellationRequested();
        if (ModelValidation.HasPartialArtifacts(directory))
            throw new InvalidDataException($"The {item.DisplayName} download has leftover partial files. Retry the download before using this model.");
        if (item.Provider == "faster-whisper" && !ModelValidation.IsFasterWhisperModelReady(directory))
            throw new InvalidDataException($"The downloaded {item.DisplayName} is incomplete. Required files are model.bin, config.json and tokenizer/vocabulary assets; choose another source or retry.");
        if (RequiresKaldiFolder(item) && !ModelValidation.HasKaldiModelStructure(ProviderModelDirectory(item)))
            throw new InvalidDataException($"The downloaded {item.DisplayName} does not contain a complete Kaldi/Vosk model (am/*.mdl, conf files and graph/*.fst are required). Choose another source and retry.");
        if (item.Provider == "Oncilla / Nemotron (Persian)" &&
            !Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly).Any(ModelValidation.HasNonEmptyFile))
            throw new InvalidDataException($"The repository listing for {item.DisplayName} did not produce any usable top-level files.");
        ModelValidation.WriteProviderCompletionMarker(directory);
    }

    private string SelectedSourceId => (_downloadSourceCombo.SelectedItem as DownloadSourceChoice)?.Id ?? "auto";

    private void RefreshSourceOptions()
    {
        if (_downloadSourceCombo.IsDisposed) return;
        var oldId = SelectedSourceId;
        var model = SelectedDownloadItem;
        var options = new List<DownloadSourceChoice>();
        if (model is null)
        {
            options.Add(new DownloadSourceChoice("auto", "Automatic (official → mirror)"));
            options.Add(new DownloadSourceChoice("official", "Official source"));
            options.Add(new DownloadSourceChoice("mirror", "Mirror source"));
        }
        else if (model.DownloadKind == ProviderDownloadKind.HuggingFaceSnapshot || model.DownloadKind == ProviderDownloadKind.WhisperBinary)
        {
            options.Add(new DownloadSourceChoice("auto", "Automatic (official → HF mirror)"));
            options.Add(new DownloadSourceChoice("official", "Hugging Face (official)"));
            options.Add(new DownloadSourceChoice("mirror", "Hugging Face mirror"));
        }
        else
        {
            options.Add(new DownloadSourceChoice("auto", "Automatic (official → mirror)"));
            options.Add(new DownloadSourceChoice("official", model.Provider == "Vosk" ? "Vosk official source" : "Official source"));
            if (!string.IsNullOrWhiteSpace(model.AlternativeDownloadUrl))
                options.Add(new DownloadSourceChoice("mirror", "Community mirror"));
        }

        _downloadSourceCombo.BeginUpdate();
        _downloadSourceCombo.Items.Clear();
        foreach (var option in options) _downloadSourceCombo.Items.Add(option);
        var found = options.FindIndex(o => o.Id == oldId);
        _downloadSourceCombo.SelectedIndex = found >= 0 ? found : 0;
        _downloadSourceCombo.EndUpdate();
    }

    private string[] GetHuggingFaceHosts()
    {
        return SelectedSourceId switch
        {
            "official" => ["huggingface.co"],
            "mirror" => ["hf-mirror.com"],
            _ => ["huggingface.co", "hf-mirror.com"]
        };
    }

    private string[] GetDirectDownloadUrls(ProviderModelInfo item)
    {
        var official = item.DownloadUrl;
        string? alternative = item.AlternativeDownloadUrl;
        if (item.DownloadKind == ProviderDownloadKind.WhisperBinary)
        {
            alternative = official.Replace("https://huggingface.co/", "https://hf-mirror.com/", StringComparison.OrdinalIgnoreCase);
        }
        else if (item.DownloadKind == ProviderDownloadKind.HuggingFaceSnapshot)
        {
            var repo = item.HuggingFaceRepo ?? throw new InvalidOperationException("Hugging Face repository metadata is missing.");
            official = $"https://huggingface.co/{repo}";
            alternative = $"https://hf-mirror.com/{repo}";
        }

        return SelectedSourceId switch
        {
            "official" => [official],
            "mirror" when !string.IsNullOrWhiteSpace(alternative) => [alternative],
            "mirror" => throw new InvalidOperationException("No alternate source is listed for this model."),
            _ => new[] { official }.Concat(string.IsNullOrWhiteSpace(alternative) ? Array.Empty<string>() : new[] { alternative }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private async Task<List<HuggingFaceFile>> GetHuggingFaceFilesWithFallbackAsync(ProviderModelInfo item, CancellationToken token)
    {
        var repo = item.HuggingFaceRepo ?? throw new InvalidOperationException("Hugging Face repository metadata is missing.");
        var failures = new List<string>();
        var prefix = item.HuggingFaceFolderPrefix?.Trim('/');

        // Diba snapshots can contain large nested folders, so request the selected folder first.
        var scopes = new List<(string Path, bool IsVersionFolder)>();
        if (!string.IsNullOrWhiteSpace(prefix))
            scopes.Add((string.Join("/", prefix.Split('/').Select(Uri.EscapeDataString)), true));
        scopes.Add((string.Empty, false));

        // Avoid unsupported page-size parameters. `expand=true` may reduce the API page size,
        // so the ordinary recursive listing is attempted first and Link: rel=next is followed.
        var queries = new[]
        {
            "?recursive=true",
            "?recursive=true&expand=true",
            "?expand=true",
            string.Empty
        };

        foreach (var host in GetHuggingFaceHosts())
        {
            foreach (var scope in scopes)
            {
                foreach (var query in queries)
                {
                    token.ThrowIfCancellationRequested();
                    var scopePart = string.IsNullOrEmpty(scope.Path) ? string.Empty : "/" + scope.Path;
                    var apiUrl = $"https://{host}/api/models/{repo}/tree/main{scopePart}{query}";
                    try
                    {
                        var files = await GetHuggingFaceTreeFilesAsync(
                            apiUrl, host, scope, prefix, item, token);
                        if (files.Count > 0) return files;
                        failures.Add($"{host}: no matching model files found at {scopePart} using {query}");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { failures.Add($"{host} {scopePart}{query}: {TrimMessage(ex.Message)}"); }
                }
            }

            // Fallback to the stable model-metadata endpoint. It returns a repository sibling list
            // and avoids tree-listing query combinations that some proxies/mirrors reject.
            try
            {
                var metadataUrl = $"https://{host}/api/models/{repo}";
                using var request = new HttpRequestMessage(HttpMethod.Get, metadataUrl);
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(token);
                    failures.Add($"{host} metadata API HTTP {(int)response.StatusCode}: {TrimMessage(body)}");
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(token);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                if (!json.RootElement.TryGetProperty("siblings", out var siblings) || siblings.ValueKind != JsonValueKind.Array)
                {
                    failures.Add($"{host}: model metadata did not contain a file list.");
                    continue;
                }

                var files = new List<HuggingFaceFile>();
                foreach (var sibling in siblings.EnumerateArray())
                {
                    if (!sibling.TryGetProperty("rfilename", out var nameElement)) continue;
                    var path = nameElement.GetString();
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (!string.IsNullOrWhiteSpace(prefix) && !path.StartsWith(prefix + "/", StringComparison.Ordinal)) continue;
                    if (item.HuggingFaceRootModelFilesOnly)
                    {
                        if (path.Contains('/')) continue;
                        if (!IsModelRuntimeFile(path)) continue;
                        if (path.EndsWith(".nemo", StringComparison.OrdinalIgnoreCase)) continue;
                        if (path.StartsWith("README", StringComparison.OrdinalIgnoreCase) || path is "LICENSE" or ".gitattributes") continue;
                    }

                    long size = 0;
                    if (sibling.TryGetProperty("size", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.Number)
                        size = sizeElement.GetInt64();
                    else if (sibling.TryGetProperty("lfs", out var lfs) && lfs.ValueKind == JsonValueKind.Object &&
                             lfs.TryGetProperty("size", out var lfsSize) && lfsSize.ValueKind == JsonValueKind.Number)
                        size = lfsSize.GetInt64();
                    files.Add(new HuggingFaceFile(path, size));
                }
                if (files.Count > 0)
                    return files.GroupBy(f => f.Path, StringComparer.Ordinal).Select(g => g.First()).ToList();
                failures.Add($"{host}: metadata response had no matching files for the selected model.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { failures.Add($"{host} metadata API: {TrimMessage(ex.Message)}"); }
        }

        throw new InvalidOperationException(
            "Could not list model files from the selected source(s). In Settings → Downloads, choose another Source (Automatic, Hugging Face official, or HF mirror) and retry. " +
            string.Join(" | ", failures.Take(8)));
    }

    private async Task<List<HuggingFaceFile>> GetHuggingFaceTreeFilesAsync(
        string firstPageUrl,
        string host,
        (string Path, bool IsVersionFolder) scope,
        string? prefix,
        ProviderModelInfo item,
        CancellationToken token)
    {
        var files = new List<HuggingFaceFile>();
        var visitedPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? pageUrl = firstPageUrl;
        const int MaxPages = 100;

        for (var page = 0; page < MaxPages && !string.IsNullOrWhiteSpace(pageUrl); page++)
        {
            token.ThrowIfCancellationRequested();
            if (!visitedPages.Add(pageUrl))
                throw new InvalidDataException("Hugging Face returned a repeated pagination URL; stopping to avoid an endless loop.");

            using var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(token);
                throw new HttpRequestException(
                    $"{host} HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) for tree listing {pageUrl}: {TrimMessage(body)}",
                    null, response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            if (json.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"{host} returned an unexpected tree-list response format for {pageUrl}.");

            foreach (var entry in json.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("path", out var pathElement)) continue;
                var path = pathElement.GetString();
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (entry.TryGetProperty("type", out var typeElement))
                {
                    var typeText = typeElement.GetString();
                    if (typeText is not null && typeText.Equals("directory", StringComparison.OrdinalIgnoreCase)) continue;
                    if (typeText is not null && typeText.Equals("folder", StringComparison.OrdinalIgnoreCase)) continue;
                }

                if (scope.IsVersionFolder && !string.IsNullOrWhiteSpace(prefix) &&
                    !path.StartsWith(prefix + "/", StringComparison.Ordinal) &&
                    !path.Equals(prefix, StringComparison.Ordinal))
                    path = prefix + "/" + path.TrimStart('/');

                if (!string.IsNullOrWhiteSpace(prefix) && !path.StartsWith(prefix + "/", StringComparison.Ordinal)) continue;
                if (item.HuggingFaceRootModelFilesOnly)
                {
                    if (path.Contains('/')) continue;
                    if (!IsModelRuntimeFile(path)) continue;
                    if (path.EndsWith(".nemo", StringComparison.OrdinalIgnoreCase)) continue;
                    if (path.StartsWith("README", StringComparison.OrdinalIgnoreCase) || path is "LICENSE" or ".gitattributes") continue;
                }

                long size = 0;
                if (entry.TryGetProperty("size", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.Number)
                    size = sizeElement.GetInt64();
                else if (entry.TryGetProperty("lfs", out var lfs) && lfs.ValueKind == JsonValueKind.Object &&
                         lfs.TryGetProperty("size", out var lfsSize) && lfsSize.ValueKind == JsonValueKind.Number)
                    size = lfsSize.GetInt64();
                files.Add(new HuggingFaceFile(path, size));
            }

            pageUrl = GetNextHuggingFacePageUrl(response, pageUrl);
            if (page == MaxPages - 1 && !string.IsNullOrWhiteSpace(pageUrl))
                throw new InvalidDataException($"The Hugging Face tree listing exceeded the safety limit of {MaxPages} pages.");
        }

        return files.GroupBy(f => f.Path, StringComparer.Ordinal).Select(g => g.First()).ToList();
    }

    private static string? GetNextHuggingFacePageUrl(HttpResponseMessage response, string currentUrl)
    {
        if (!response.Headers.TryGetValues("Link", out var linkHeaders)) return null;
        var currentUri = new Uri(currentUrl, UriKind.Absolute);
        foreach (var link in string.Join(",", linkHeaders).Split(','))
        {
            var start = link.IndexOf('<');
            var end = start < 0 ? -1 : link.IndexOf('>', start + 1);
            var parametersText = end >= 0 ? link[(end + 1)..] : link;
            var isNext = parametersText.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(parameter => parameter.Trim())
                .Where(parameter => parameter.StartsWith("rel=", StringComparison.OrdinalIgnoreCase))
                .Select(parameter => parameter[4..].Trim().Trim('\"', '\''))
                .Any(relation => relation.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(token => token.Equals("next", StringComparison.OrdinalIgnoreCase)));
            if (!isNext) continue;

            if (start < 0 || end <= start + 1)
                throw new InvalidDataException("Hugging Face returned a malformed Link header for the next page.");

            var next = link[(start + 1)..end].Trim();
            if (!Uri.TryCreate(currentUri, next, out var nextUri))
                throw new InvalidDataException("Hugging Face returned an invalid URL for the next page.");
            if (!string.Equals(nextUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(nextUri.Host, currentUri.Host, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The Hugging Face pagination link changed scheme or host; refusing to leave the selected download source.");
            return nextUri.AbsoluteUri;
        }
        return null;
    }

    private async Task DownloadFileWithFallbackAsync(
        IEnumerable<string> urls,
        string destination,
        IProgress<DownloadProgress> progress,
        CancellationToken token,
        long? expectedSize = null)
    {
        var candidates = urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var errors = new List<string>();
        foreach (var url in candidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await DownloadFileAsync(url, destination, progress, token, expectedSize);
                TryDelete(destination + ".resume.json");
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add($"{new Uri(url).Host}: {TrimMessage(ex.Message)}"); }
        }
        throw new InvalidOperationException("All selected download sources failed: " + string.Join(" | ", errors));
    }

    private static string TrimMessage(string value)
    {
        var clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= 240 ? clean : clean[..240] + "…";
    }

    private sealed record DownloadSourceChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private static bool IsModelRuntimeFile(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return extension is ".bin" or ".json" or ".safetensors" or ".model" or ".txt" or ".vocab" or ".merges" or ".tiktoken"
            || name is "tokenizer" or "vocabulary";
    }

    private sealed record HuggingFaceFile(string Path, long Size);

    private sealed record DownloadResumeMetadata(string Url, string? ETag, string? LastModified);

    private async Task DownloadFileAsync(
        string url,
        string destination,
        IProgress<DownloadProgress> progress,
        CancellationToken token,
        long? expectedSize = null) =>
        await DownloadFileAsync(url, destination, progress, token, expectedSize, allowResume: true);

    private async Task DownloadFileAsync(
        string url,
        string destination,
        IProgress<DownloadProgress> progress,
        CancellationToken token,
        long? expectedSize,
        bool allowResume)
    {
        var metadataPath = destination + ".resume.json";
        var metadata = ReadResumeMetadata(metadataPath);
        var existingLength = File.Exists(destination) ? new FileInfo(destination).Length : 0L;
        var canResume = allowResume && existingLength > 0 && metadata is not null &&
                        string.Equals(metadata.Url, url, StringComparison.Ordinal) &&
                        (!string.IsNullOrWhiteSpace(metadata.ETag) || !string.IsNullOrWhiteSpace(metadata.LastModified));
        if (!canResume)
        {
            TryDelete(destination);
            TryDelete(metadataPath);
            existingLength = 0;
            metadata = null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("*/*");
        if (canResume)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
            if (!string.IsNullOrWhiteSpace(metadata!.ETag) && EntityTagHeaderValue.TryParse(metadata.ETag, out var etag))
                request.Headers.IfRange = new RangeConditionHeaderValue(etag);
            else if (DateTimeOffset.TryParse(metadata.LastModified, out var modified))
                request.Headers.IfRange = new RangeConditionHeaderValue(modified);
        }

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable && canResume)
        {
            response.Dispose();
            TryDelete(destination);
            TryDelete(metadataPath);
            await DownloadFileAsync(url, destination, progress, token, expectedSize, allowResume: false);
            return;
        }
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(token);
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) at {url}. {TrimMessage(body)}",
                null, response.StatusCode);
        }

        var responseEtag = response.Headers.ETag?.ToString();
        var savedEtag = metadata?.ETag;
        var validatorChanged = canResume &&
            ((!string.IsNullOrWhiteSpace(savedEtag) && !string.IsNullOrWhiteSpace(responseEtag) &&
              !string.Equals(savedEtag, responseEtag, StringComparison.Ordinal)) ||
             (!string.IsNullOrWhiteSpace(metadata?.LastModified) && response.Content.Headers.LastModified is { } currentModified &&
              DateTimeOffset.TryParse(metadata.LastModified, out var savedModified) && currentModified != savedModified));
        var append = canResume && !validatorChanged &&
                     response.StatusCode == System.Net.HttpStatusCode.PartialContent &&
                     response.Content.Headers.ContentRange?.From == existingLength;
        if (canResume && (validatorChanged || (response.StatusCode == System.Net.HttpStatusCode.PartialContent && !append)))
        {
            response.Dispose();
            TryDelete(destination);
            TryDelete(metadataPath);
            await DownloadFileAsync(url, destination, progress, token, expectedSize, allowResume: false);
            return;
        }
        if (!append) existingLength = 0;

        var responseValidator = response.Headers.ETag?.ToString();
        var responseModified = response.Content.Headers.LastModified?.ToString("O");
        var resumeMetadata = new DownloadResumeMetadata(url, responseValidator, responseModified);
        WriteResumeMetadata(metadataPath, resumeMetadata);

        var responseLength = response.Content.Headers.ContentLength;
        var total = append
            ? response.Content.Headers.ContentRange?.Length ?? (responseLength.HasValue ? existingLength + responseLength.Value : null)
            : responseLength;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(destination, append ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, bufferSize: 128 * 1024, useAsync: true);
        var buffer = new byte[128 * 1024];
        long received = existingLength;
        var receivedThisResponse = 0L;
        var lastReport = DateTime.UtcNow;
        progress.Report(new DownloadProgress(received, total));
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            received += read;
            receivedThisResponse += read;
            if ((DateTime.UtcNow - lastReport).TotalMilliseconds >= 120 || (total.HasValue && received >= total.Value))
            {
                progress.Report(new DownloadProgress(received, total));
                lastReport = DateTime.UtcNow;
            }
        }
        await output.FlushAsync(token);

        if (responseLength.HasValue && receivedThisResponse != responseLength.Value)
            throw new IOException($"Incomplete download from {new Uri(url).Host}: HTTP declared {responseLength.Value} bytes for this response but only {receivedThisResponse} arrived. Retry to resume the download.");
        if (total.HasValue && received != total.Value)
            throw new IOException($"Incomplete download from {new Uri(url).Host}: expected {total.Value} total bytes but received {received}. Retry to resume the download.");
        if (expectedSize.HasValue && received != expectedSize.Value)
            throw new IOException($"Model file size mismatch for {Path.GetFileName(destination)}: repository metadata says {expectedSize.Value} bytes but {received} bytes were downloaded.");

        progress.Report(new DownloadProgress(received, total ?? expectedSize));
    }

    private static DownloadResumeMetadata? ReadResumeMetadata(string metadataPath)
    {
        try
        {
            return File.Exists(metadataPath)
                ? JsonSerializer.Deserialize<DownloadResumeMetadata>(File.ReadAllText(metadataPath))
                : null;
        }
        catch { return null; }
    }

    private static void WriteResumeMetadata(string metadataPath, DownloadResumeMetadata metadata)
    {
        var temporaryPath = metadataPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(metadata));
        File.Move(temporaryPath, metadataPath, overwrite: true);
    }

    private void ShowDownloadProgress(string modelId, DownloadProgress progress)
    {
        if (IsDisposed || _isClosing) return;
        if (progress.TotalBytes is > 0)
        {
            if (_downloadProgress.Style != ProgressBarStyle.Blocks)
            {
                _downloadProgress.Style = ProgressBarStyle.Blocks;
                _downloadProgress.MarqueeAnimationSpeed = 0;
            }
            var percent = (int)Math.Clamp(progress.ReceivedBytes * 100d / progress.TotalBytes.Value, 0, 100);
            _downloadProgress.Value = percent;
            _downloadProgressLabel.Text = $"Downloading {modelId}: {percent}% · {FormatBytes(progress.ReceivedBytes)} / {FormatBytes(progress.TotalBytes.Value)}";
        }
        else
        {
            _downloadProgress.Style = ProgressBarStyle.Marquee;
            _downloadProgress.MarqueeAnimationSpeed = 24;
            _downloadProgressLabel.Text = $"Downloading {modelId}: {FormatBytes(progress.ReceivedBytes)} received";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.#} {units[unit]}";
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed record MicrophoneChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record DownloadProgress(long ReceivedBytes, long? TotalBytes);
}
