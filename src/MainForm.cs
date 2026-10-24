using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vosk;

namespace MadNegar;

internal sealed class MainForm : Form
{
    // whisper.cpp's CLI defaults to English when -l is omitted. The Auto path must pass -l auto
    // explicitly; this neutral prompt only reinforces script conventions, not one spoken language.
    private const string MixedScriptAutoPrompt = "متن فارسی را با خط فارسی بنویس؛ English words را با حروف لاتین نگه دار. Transcribe exactly as spoken. Do not translate or omit either language.";
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MadNegar");
    private string ModelsDirectory => Path.Combine(_root, "Models");
    private string RuntimeDirectory => Path.Combine(_root, "Runtime");
    private string TempDirectory => Path.Combine(_root, "Temp");

    private readonly ThemedComboBox _modelCombo = new();
    private readonly ThemedComboBox _mainDualPersianModelCombo = new();
    private readonly ThemedComboBox _mainDualEnglishModelCombo = new();
    private readonly Label _modelSelectionLabel = new();
    private readonly Label _modelStatus = new();
    private readonly Button _settingsButton = new();
    private bool _populatingMainDualModelChoices;
    private readonly ThemeToggleButton _themeToggleButton = new();
    private readonly ThemedComboBox _languageCombo = new();
    private readonly ThemedComboBox _transcriptionPreferenceCombo = new();
    private readonly Label _transcriptionPreferenceLabel = new();
    private bool _syncingTranscriptionPreference;
    private readonly Button _startButton = new();
    private readonly Label _statusLabel = new();
    private readonly ThemedProgressBar _progress = new();
    private readonly Label _progressLabel = new();
    private readonly TextBox _transcriptBox = new();
    private readonly Button _copyButton = new();
    private readonly Button _exportButton = new();
    private readonly Button _historyButton = new();
    private readonly Button _fileTranscriptionButton = new();
    private readonly Button _batchTranscriptionButton = new();
    private readonly Button _retryInsertButton = new();
    private readonly Button _undoTextFixButton = new();
    private readonly Button _clearButton = new();
    private readonly Button _cancelDirectTypingButton = new();
    private readonly Panel _titleBar = new();
    private readonly Label _windowTitleLabel = new();
    private readonly WindowActionButton _minimizeWindowButton = new();
    private readonly WindowActionButton _trayWindowButton = new();
    private readonly WindowActionButton _closeWindowButton = new();
    private readonly NotifyIcon _notifyIcon = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolTip _windowToolTip = new();
    private readonly System.Windows.Forms.Timer _liveTranscriptionTimer = new() { Interval = 1800 };
    private readonly object _liveAudioLock = new();
    private readonly MemoryStream _liveAudioBuffer = new();

    private AppSettings _settings = new();
    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _fileTranscriptionCts;
    private WasapiCapture? _waveIn;
    private MMDevice? _captureDevice;
    private WaveFileWriter? _waveWriter;
    private string? _recordingPath;
    private WaveFormat? _captureWaveFormat;
    private Task? _liveTranscriptionTask;
    private CancellationTokenSource? _liveTranscriptionCts;
    private string _liveTranscriptText = string.Empty;
    private Vosk.Model? _liveVoskModel;
    private string? _liveVoskModelPath;
    private bool _isBusy;
    private bool _isRecording;
    private bool _isStoppingRecording;
    private bool _isTranscribing;
    private bool _isFileTranscribing;
    private bool _isClosing;
    // State for guarded programmatic transcript updates and one-step raw-text Undo.
    private bool _settingTranscriptProgrammatically;
    private string? _rawTranscriptBeforeNormalization;
    private string? _normalizedTranscriptForUndo;
    private bool _hotkeyRegistered;
    private IntPtr _registeredHotkeyHandle;
    private bool _isHiddenToTray;
    private bool _directTypingSession;
    private bool _directTypingCancelled;
    private CancellationTokenSource? _directTypingCancellation;
    private CancellationTokenSource? _retryInsertCancellation;
    private DirectTypingTarget? _directTypingTarget;
    private DirectTypingTarget? _pendingDirectTypingTarget;
    private string? _pendingDirectTypingText;
    private string? _lastWhisperDetectedLanguage;
    private string? _lastImportedAudioPath;
    private string? _lastImportedAudioModelKey;
    private string? _lastImportedAudioLanguage;
    private string? _lastImportedAudioDetectedLanguage;

    public MainForm()
    {
        Text = "MadNegar";
        ClientSize = new Size(420, 695);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(247, 249, 252);
        ForeColor = Color.FromArgb(30, 41, 59);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;

        Directory.CreateDirectory(ModelsDirectory);
        Directory.CreateDirectory(RuntimeDirectory);
        Directory.CreateDirectory(TempDirectory);
        _settings = AppSettingsStore.Load(_root);

        BuildUi();
        ConfigureTrayIcon();
        RefreshDownloadedModels();
        _languageCombo.SelectedIndex = _settings.SpokenLanguage switch
        {
            "fa" => 1,
            "en" => 2,
            _ => 0
        };
        RefreshTranscriptionPreferenceControl();
        PopulateMainDualModelChoices();
        UpdateModelSelectorMode();
        UpdateTranscriptTextDirection();
        ThemeManager.Apply(this, _settings.ThemeMode);
        ApplyTrayMenuTheme();
        RefreshControls();
        _liveTranscriptionTimer.Tick += (_, _) =>
        {
            if (!_settings.LiveTranscriptionEnabled || !_isRecording || _isStoppingRecording) return;
            if (_liveTranscriptionTask is { IsCompleted: false })
            {
                DiagnosticsState.RecordLivePreviewSkipped();
                return;
            }
            _liveTranscriptionTask = ProcessLiveTranscriptionTickAsync();
        };
        FormClosing += HandleFormClosing;
        FormClosed += (_, _) =>
        {
            _liveTranscriptionTimer.Stop();
            _liveTranscriptionTimer.Dispose();
            lock (_liveAudioLock) _liveAudioBuffer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _trayMenu.Dispose();
            _windowToolTip.Dispose();
        };
        if (IsHandleCreated) RefreshGlobalHotkeyRegistration(showError: false);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RefreshGlobalHotkeyRegistration(showError: false);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_hotkeyRegistered)
        {
            GlobalHotkeySupport.Unregister(_registeredHotkeyHandle);
            _hotkeyRegistered = false;
            _registeredHotkeyHandle = IntPtr.Zero;
        }
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == GlobalHotkeySupport.WmHotkey && m.WParam.ToInt32() == GlobalHotkeySupport.MessageId)
        {
            HandleGlobalHotkeyPressed();
            return;
        }
        base.WndProc(ref m);
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
        BuildTitleBar();

        var subtitle = new Label
        {
            Text = "Offline voice typing · Persian + English",
            Location = new Point(20, 45),
            Size = new Size(238, 20),
            Tag = "muted"
        };
        Controls.Add(subtitle);

        _settingsButton.Text = "⚙  Settings";
        StyleButton(_settingsButton, primary: false);
        _settingsButton.Location = new Point(300, 41);
        _settingsButton.Size = new Size(104, 31);
        _settingsButton.Click += (_, _) => OpenSettings();
        Controls.Add(_settingsButton);

        _themeToggleButton.Tag = "theme-toggle";
        _themeToggleButton.Location = new Point(264, 41);
        _themeToggleButton.Size = new Size(30, 31);
        _themeToggleButton.Font = new Font("Segoe UI Symbol", 13F, FontStyle.Regular);
        _themeToggleButton.Text = string.Empty;
        _themeToggleButton.Padding = Padding.Empty;
        _themeToggleButton.TextAlign = ContentAlignment.MiddleCenter;
        _themeToggleButton.Click += (_, _) => ToggleThemeMode();
        _themeToggleButton.AccessibleName = "Switch theme";
        _windowToolTip.SetToolTip(_themeToggleButton, "Switch theme");
        Controls.Add(_themeToggleButton);
        RefreshThemeToggleButton();

        var separator = new Panel
        {
            Location = new Point(18, 76),
            Size = new Size(384, 1),
            BackColor = Color.FromArgb(226, 232, 240),
            Tag = "separator"
        };
        Controls.Add(separator);

        _modelSelectionLabel.Text = "Downloaded speech model";
        _modelSelectionLabel.Location = new Point(18, 87);
        _modelSelectionLabel.Size = new Size(360, 18);
        Controls.Add(_modelSelectionLabel);

        _modelCombo.Location = new Point(16, 107);
        _modelCombo.Size = new Size(388, 29);
        _modelCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _modelCombo.DropDownWidth = 390;
        _modelCombo.SelectedIndexChanged += ModelComboSelectedIndexChanged;
        Controls.Add(_modelCombo);

        _mainDualPersianModelCombo.Location = new Point(16, 107);
        _mainDualPersianModelCombo.Size = new Size(190, 29);
        _mainDualPersianModelCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _mainDualPersianModelCombo.DropDownWidth = 360;
        _mainDualPersianModelCombo.AccessibleName = "Persian base model";
        _windowToolTip.SetToolTip(_mainDualPersianModelCombo, "Persian base model used by Dual-model mixed Auto. This is synchronized with Settings → Transcription.");
        _mainDualPersianModelCombo.SelectedIndexChanged += (_, _) => MainDualModelChoiceChanged("fa");
        _mainDualPersianModelCombo.Visible = false;
        Controls.Add(_mainDualPersianModelCombo);

        _mainDualEnglishModelCombo.Location = new Point(212, 107);
        _mainDualEnglishModelCombo.Size = new Size(192, 29);
        _mainDualEnglishModelCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _mainDualEnglishModelCombo.DropDownWidth = 360;
        _mainDualEnglishModelCombo.AccessibleName = "English comparison model";
        _windowToolTip.SetToolTip(_mainDualEnglishModelCombo, "English comparison model used by Dual-model mixed Auto. This is synchronized with Settings → Transcription.");
        _mainDualEnglishModelCombo.SelectedIndexChanged += (_, _) => MainDualModelChoiceChanged("en");
        _mainDualEnglishModelCombo.Visible = false;
        Controls.Add(_mainDualEnglishModelCombo);

        _modelStatus.Location = new Point(18, 139);
        _modelStatus.Size = new Size(384, 28);
        _modelStatus.Tag = "muted";
        Controls.Add(_modelStatus);

        Controls.Add(MakeLabel("Spoken language", 18, 174, 350, 18));
        _languageCombo.Location = new Point(16, 195);
        _languageCombo.Size = new Size(388, 29);
        _languageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageCombo.Items.AddRange(["Auto (Persian/English)", "Persian (native script)", "English"]);
        _languageCombo.SelectedIndexChanged += (_, _) =>
        {
            _settings.SpokenLanguage = _languageCombo.SelectedIndex switch { 1 => "fa", 2 => "en", _ => "auto" };
            UpdateTranscriptTextDirection();
            PopulateMainDualModelChoices();
            UpdateModelSelectorMode();
            try { AppSettingsStore.Save(_root, _settings); } catch { }
            RefreshControls();
        };
        Controls.Add(_languageCombo);

        _transcriptionPreferenceLabel.Text = "Transcription Language Preference / زبان اصلی ترنسکریپت";
        _transcriptionPreferenceLabel.Location = new Point(18, 228);
        _transcriptionPreferenceLabel.Size = new Size(386, 18);
        _transcriptionPreferenceLabel.AccessibleName = "Transcription Language Preference / زبان اصلی ترنسکریپت";
        Controls.Add(_transcriptionPreferenceLabel);

        _transcriptionPreferenceCombo.Location = new Point(16, 248);
        _transcriptionPreferenceCombo.Size = new Size(388, 29);
        _transcriptionPreferenceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _transcriptionPreferenceCombo.Items.AddRange([
            "فارسی‌اول · Persian-first",
            "English-first · انگلیسی‌اول"
        ]);
        _transcriptionPreferenceCombo.AccessibleName = "Transcription Language Preference / زبان اصلی ترنسکریپت";
        _transcriptionPreferenceCombo.AccessibleDescription = "Only biases ambiguous recognition choices. Persian must remain in Persian script and genuine English words in Latin script in either preference mode.";
        _transcriptionPreferenceCombo.SelectedIndexChanged += TranscriptionPreferenceComboSelectedIndexChanged;
        _windowToolTip.SetToolTip(_transcriptionPreferenceCombo,
            "Recognition preference only; does not change Speak Language or translate/lock mixed speech.");
        Controls.Add(_transcriptionPreferenceCombo);

        _startButton.Text = "▶  Start Recording";
        StyleButton(_startButton, primary: true);
        _startButton.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _startButton.Location = new Point(16, 280);
        _startButton.Size = new Size(194, 52);
        _startButton.Click += async (_, _) =>
        {
            if (_isRecording && !_isStoppingRecording) StopRecording();
            else if (!_isBusy && !_isTranscribing && !_isStoppingRecording) await StartRecordingClickedAsync();
        };
        Controls.Add(_startButton);

        _fileTranscriptionButton.Text = "Audio File…";
        StyleButton(_fileTranscriptionButton, primary: false);
        _fileTranscriptionButton.Location = new Point(218, 280);
        _fileTranscriptionButton.Size = new Size(90, 52);
        _fileTranscriptionButton.Click += async (_, _) => await ChooseAudioFileAsync();
        _windowToolTip.SetToolTip(_fileTranscriptionButton, "Transcribe a local WAV, MP3 or AIFF file without modifying the source file.");
        Controls.Add(_fileTranscriptionButton);

        _batchTranscriptionButton.Text = "Batch…";
        StyleButton(_batchTranscriptionButton, primary: false);
        _batchTranscriptionButton.Location = new Point(314, 280);
        _batchTranscriptionButton.Size = new Size(90, 52);
        _batchTranscriptionButton.Click += (_, _) => OpenBatchTranscription();
        _windowToolTip.SetToolTip(_batchTranscriptionButton, "Transcribe several local audio files sequentially.");
        Controls.Add(_batchTranscriptionButton);

        _statusLabel.Location = new Point(18, 342);
        _statusLabel.Size = new Size(384, 22);
        _statusLabel.Text = "Ready";
        _statusLabel.Tag = "muted";
        Controls.Add(_statusLabel);

        _progress.Location = new Point(17, 368);
        _progress.Size = new Size(386, 12);
        _progress.Minimum = 0;
        _progress.Maximum = 100;
        _progress.Style = ProgressBarStyle.Blocks;
        Controls.Add(_progress);

        _progressLabel.Location = new Point(18, 385);
        _progressLabel.Size = new Size(384, 30);
        _progressLabel.Text = "Select a downloaded model to begin.";
        _progressLabel.Tag = "muted";
        Controls.Add(_progressLabel);

        Controls.Add(MakeLabel("Transcript", 18, 420, 350, 18));
        _transcriptBox.Location = new Point(16, 442);
        _transcriptBox.Size = new Size(388, 120);
        _transcriptBox.Multiline = true;
        _transcriptBox.AcceptsReturn = true;
        _transcriptBox.AcceptsTab = true;
        _transcriptBox.ScrollBars = ScrollBars.Vertical;
        _transcriptBox.BorderStyle = BorderStyle.FixedSingle;
        _transcriptBox.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
        _transcriptBox.TextChanged += (_, _) =>
        {
            if (!_settingTranscriptProgrammatically)
                ClearImportedAudioSubtitleContext();
            // Editing or clearing the displayed transcript invalidates a pending retry so stale text cannot be inserted.
            if (_pendingDirectTypingText is not null &&
                !string.Equals(_transcriptBox.Text, _pendingDirectTypingText, StringComparison.Ordinal))
            {
                _pendingDirectTypingTarget = null;
                _pendingDirectTypingText = null;
            }
            if (!_settingTranscriptProgrammatically && _rawTranscriptBeforeNormalization is not null &&
                !string.Equals(_transcriptBox.Text, _normalizedTranscriptForUndo, StringComparison.Ordinal))
            {
                ClearPersianUndoSnapshot();
            }
            UpdateTranscriptTextDirection(_transcriptBox.Text);
            RefreshControls();
        };
        Controls.Add(_transcriptBox);

        _copyButton.Text = "Copy";
        StyleButton(_copyButton, primary: true);
        _copyButton.Location = new Point(16, 581);
        _copyButton.Size = new Size(72, 34);
        _copyButton.Click += (_, _) => CopyTranscript();
        _windowToolTip.SetToolTip(_copyButton, "Copy the current transcript.");
        Controls.Add(_copyButton);

        _undoTextFixButton.Text = "Undo";
        StyleButton(_undoTextFixButton, primary: false);
        _undoTextFixButton.Location = new Point(94, 581);
        _undoTextFixButton.Size = new Size(72, 34);
        _undoTextFixButton.Enabled = false;
        _undoTextFixButton.Click += (_, _) => UndoPersianTextFixes();
        _windowToolTip.SetToolTip(_undoTextFixButton, "Restore the exact unmodified transcript inside MadNegar.");
        Controls.Add(_undoTextFixButton);

        _retryInsertButton.Text = "Retry";
        StyleButton(_retryInsertButton, primary: false);
        _retryInsertButton.Location = new Point(172, 581);
        _retryInsertButton.Size = new Size(72, 34);
        _retryInsertButton.Enabled = false;
        _windowToolTip.SetToolTip(_retryInsertButton, "Retry only a safe, failed direct insertion.");
        _retryInsertButton.Click += async (_, _) => await RetryDirectTypingInsertAsync();
        Controls.Add(_retryInsertButton);

        _exportButton.Text = "Export";
        StyleButton(_exportButton, primary: false);
        _exportButton.Location = new Point(250, 581);
        _exportButton.Size = new Size(72, 34);
        _exportButton.Click += (_, _) => ExportTranscript();
        _windowToolTip.SetToolTip(_exportButton, "Save the current transcript to a UTF-8 TXT file.");
        Controls.Add(_exportButton);

        _clearButton.Text = "Clear";
        StyleButton(_clearButton, primary: false);
        _clearButton.Location = new Point(328, 581);
        _clearButton.Size = new Size(72, 34);
        _clearButton.Click += (_, _) => ClearTranscript();
        Controls.Add(_clearButton);

        _cancelDirectTypingButton.Text = "Cancel Voice Typing";
        StyleButton(_cancelDirectTypingButton, primary: false);
        _cancelDirectTypingButton.Location = new Point(16, 622);
        _cancelDirectTypingButton.Size = new Size(388, 27);
        _cancelDirectTypingButton.Visible = false;
        _cancelDirectTypingButton.Click += (_, _) => { if (_isFileTranscribing) CancelFileTranscription(); else CancelDirectTyping(); };
        Controls.Add(_cancelDirectTypingButton);

        var footer = new Label
        {
            Text = "Audio is processed locally on this PC.",
            Location = new Point(18, 654),
            Size = new Size(278, 18),
            Tag = "muted",
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(footer);

        _historyButton.Text = "History";
        StyleButton(_historyButton, primary: false);
        _historyButton.Location = new Point(306, 648);
        _historyButton.Size = new Size(94, 26);
        _historyButton.Click += (_, _) => OpenTranscriptHistory();
        _windowToolTip.SetToolTip(_historyButton, "Browse saved transcripts. Saving new history is off by default.");
        Controls.Add(_historyButton);
    }


    private void BuildTitleBar()
    {
        _titleBar.Location = new Point(0, 0);
        _titleBar.Size = new Size(ClientSize.Width, 34);
        _titleBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _titleBar.Tag = "titlebar";
        _titleBar.Paint += (_, e) =>
        {
            var palette = ThemeManager.ActivePalette;
            using var pen = new Pen(palette.Border);
            e.Graphics.DrawLine(pen, 0, _titleBar.Height - 1, _titleBar.Width, _titleBar.Height - 1);
        };

        _windowTitleLabel.Text = "MadNegar";
        _windowTitleLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _windowTitleLabel.Location = new Point(13, 0);
        _windowTitleLabel.Size = new Size(180, 34);
        _windowTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
        _windowTitleLabel.Tag = "titlebar-text";
        _windowTitleLabel.MouseDown += DragWindow;
        _titleBar.Controls.Add(_windowTitleLabel);

        // Put the notification-area action before ordinary minimize, with a clear tray/download glyph.
        ConfigureWindowButton(_trayWindowButton, "window-tray", "Minimize to notification area", 306);
        _trayWindowButton.Click += (_, _) => HideToTray();
        ConfigureWindowButton(_minimizeWindowButton, "window-minimize", "Minimize to taskbar", 342);
        _minimizeWindowButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        ConfigureWindowButton(_closeWindowButton, "window-close", "Exit MadNegar", 378);
        _closeWindowButton.Click += (_, _) =>
        {
            Close();
        };

        _titleBar.Controls.Add(_minimizeWindowButton);
        _titleBar.Controls.Add(_trayWindowButton);
        _titleBar.Controls.Add(_closeWindowButton);
        _titleBar.MouseDown += DragWindow;
        Controls.Add(_titleBar);
        _titleBar.BringToFront();
    }

    private void ConfigureWindowButton(WindowActionButton button, string tag, string tooltip, int x)
    {
        button.Text = string.Empty;
        button.Tag = tag;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Font = new Font("Segoe UI Symbol", tag == "window-close" ? 15F : 11F, FontStyle.Regular);
        button.Location = new Point(x, 0);
        button.Size = new Size(36, 33);
        button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        button.Cursor = Cursors.Hand;
        button.AccessibleName = tooltip;
        _windowToolTip.SetToolTip(button, tooltip);
    }

    private void ConfigureTrayIcon()
    {
        _trayMenu.Items.Add("Open MadNegar", null, (_, _) => RestoreFromTray());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Exit MadNegar", null, (_, _) =>
        {
            Close();
        });
        try { _notifyIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application; }
        catch { _notifyIcon.Icon = SystemIcons.Application; }
        _notifyIcon.Text = "MadNegar - Voice typing";
        _notifyIcon.ContextMenuStrip = _trayMenu;
        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
        _notifyIcon.Visible = false;
        ApplyTrayMenuTheme();
    }

    private void ApplyTrayMenuTheme()
    {
        var palette = ThemeManager.Current(_settings.ThemeMode);
        _trayMenu.BackColor = palette.Surface;
        _trayMenu.ForeColor = palette.Text;
        foreach (ToolStripItem item in _trayMenu.Items)
        {
            item.BackColor = palette.Surface;
            item.ForeColor = palette.Text;
        }
    }

    private void HideToTray()
    {
        if (_isClosing) return;
        _isHiddenToTray = true;
        _notifyIcon.Visible = true;
        ShowInTaskbar = false;
        Hide();
    }

    private void RestoreFromTray()
    {
        if (_isClosing || IsDisposed) return;
        _isHiddenToTray = false;
        _notifyIcon.Visible = _settings.GlobalHotkeyEnabled && _hotkeyRegistered;
        ShowInTaskbar = true;
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
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

    private SpeechModelChoice? SelectedModel => _modelCombo.SelectedItem as SpeechModelChoice;
    private string ModelPath(ModelInfo model) => Path.Combine(ModelsDirectory, model.FileName);
    private string ModelPath(SpeechModelChoice model) => model.WhisperModel is not null
        ? ModelPath(model.WhisperModel)
        : ProviderModelDirectory(model.ProviderModel!);
    private string ProviderModelsDirectory => Path.Combine(_root, "ProviderModels");
    private string ProviderModelStorageDirectory(ProviderModelInfo model) => Path.Combine(ProviderModelsDirectory, SafePathPart(model.Provider), SafePathPart(model.Id));
    private string ProviderModelDirectory(ProviderModelInfo model)
    {
        var storage = ProviderModelStorageDirectory(model);
        if (!RequiresKaldiFolder(model) || HasKaldiFolderStructure(storage)) return storage;
        // Older Vosk archives and some provider snapshots can keep the model one level deeper.
        if (Directory.Exists(storage))
        {
            try
            {
                foreach (var amFolder in Directory.GetDirectories(storage, "am", SearchOption.AllDirectories))
                {
                    var candidate = Path.GetDirectoryName(amFolder);
                    if (!string.IsNullOrWhiteSpace(candidate) && HasKaldiFolderStructure(candidate))
                        return candidate;
                }
            }
            catch { }
        }
        return storage;
    }
    private SpeechModelChoice ResolveDualModelChoice(string modelKey, string legacyId, string role)
    {
        var allChoices = ModelCatalog.Models.Select(item => new SpeechModelChoice(item, null))
            .Concat(ProviderModelCatalog.All.Where(item => !item.IsWhisper).Select(item => new SpeechModelChoice(null, item)))
            .Where(choice => choice.IsSupportedForFinal);
        var choice = allChoices.FirstOrDefault(item => item.Key.Equals(modelKey, StringComparison.OrdinalIgnoreCase));
        if (choice is null)
        {
            var legacyKey = $"faster-whisper::{legacyId}";
            choice = allChoices.FirstOrDefault(item => item.Key.Equals(legacyKey, StringComparison.OrdinalIgnoreCase));
        }
        if (choice is null)
            throw new InvalidOperationException($"The saved {role} model '{modelKey}' is not supported by the current transcription runtimes. Open Settings → Transcription and select a downloaded model that supports {role}.");
        if (!DualModelSelectionPolicy.SupportsLanguageRole(choice, role))
            throw new InvalidOperationException($"The selected {role} role cannot use {choice.Provider} / {choice.Id} ({choice.Languages}). Choose a downloaded model compatible with {role}.");
        if (!IsModelDownloaded(choice))
            throw new InvalidOperationException($"The selected {role} model {choice.Provider} / {choice.Id} is missing or incomplete. Open Settings → Downloads, complete its download, then reselect it.");
        return choice;
    }

    private async Task<string> TranscribeDualModelsAcrossProvidersAsync(
        SpeechModelChoice persianChoice, SpeechModelChoice englishChoice, string audioPath,
        IProgress<string> runtimeProgress, CancellationToken cancellationToken)
    {
        var workDirectory = Path.Combine(TempDirectory, $"dual-role-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);
        Vosk.Model? persianVoskModel = null;
        Vosk.Model? englishVoskModel = null;
        IReadOnlyList<AutoLanguageTranscription.WaveSegment> segments = Array.Empty<AutoLanguageTranscription.WaveSegment>();
        try
        {
            if ((persianChoice.IsWhisper || englishChoice.IsWhisper) && FindWhisperCli() is null)
                throw new FileNotFoundException("The selected dual-model roles include a Whisper.cpp model, but whisper-cli.exe is not installed. Install/repair the local Whisper.cpp runtime or select another downloaded model.");

            if (persianChoice.UsesVoskRuntime)
            {
                runtimeProgress.Report($"Loading Persian-role model: {persianChoice.Provider} / {persianChoice.Id}…");
                Vosk.Vosk.SetLogLevel(-1);
                persianVoskModel = await Task.Run(() => new Vosk.Model(ModelPath(persianChoice)), cancellationToken);
            }
            if (englishChoice.UsesVoskRuntime)
            {
                runtimeProgress.Report($"Loading English-role model: {englishChoice.Provider} / {englishChoice.Id}…");
                Vosk.Vosk.SetLogLevel(-1);
                englishVoskModel = await Task.Run(() => new Vosk.Model(ModelPath(englishChoice)), cancellationToken);
            }

            // Non-faster-whisper providers are compared on short overlapping segments. The chosen
            // engines remain local; providers with process-based runtimes may run slower per segment.
            segments = AutoLanguageTranscription.SplitWaveFile(
                audioPath, workDirectory, cancellationToken,
                chunkSeconds: 3, overlapMilliseconds: 450, minimumDurationSeconds: 3);
            var merged = string.Empty;
            for (var index = 0; index < segments.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var segmentPath = segments[index].Path;
                runtimeProgress.Report($"Comparing Persian and English model outputs · segment {index + 1}/{segments.Count}…");
                _lastWhisperDetectedLanguage = null;
                var faText = await TranscribeDualCandidateAsync(
                    persianChoice, "fa", segmentPath, Path.Combine(workDirectory, $"fa-{index + 1:000}"),
                    persianVoskModel, runtimeProgress, cancellationToken);
                var faDetectedLanguage = _lastWhisperDetectedLanguage;
                cancellationToken.ThrowIfCancellationRequested();
                _lastWhisperDetectedLanguage = null;
                var enText = await TranscribeDualCandidateAsync(
                    englishChoice, "en", segmentPath, Path.Combine(workDirectory, $"en-{index + 1:000}"),
                    englishVoskModel, runtimeProgress, cancellationToken);
                var enDetectedLanguage = _lastWhisperDetectedLanguage;
                var selected = DualModelSelectionPolicy.ChooseCandidate(
                    faText, enText, faDetectedLanguage, enDetectedLanguage,
                    _settings.TranscriptionLanguagePreference);
                if (string.IsNullOrWhiteSpace(selected)) continue;
                merged = MergeTranscriptSegments(merged, selected);
            }

            var result = merged.Trim();
            if (_settings.PreserveEnglishTechnicalTermsInAuto)
                result = AutoLanguageText.RestoreEnglishTechnicalTerms(result);
            return result;
        }
        finally
        {
            try { persianVoskModel?.Dispose(); } catch { }
            try { englishVoskModel?.Dispose(); } catch { }
            foreach (var segment in segments)
            {
                if (!segment.IsTemporary) continue;
                try { if (File.Exists(segment.Path)) File.Delete(segment.Path); } catch { }
            }
            try { if (Directory.Exists(workDirectory)) Directory.Delete(workDirectory, recursive: true); } catch { }
        }
    }

    private async Task<string> TranscribeDualCandidateAsync(
        SpeechModelChoice choice, string language, string audioPath, string outputBase,
        Vosk.Model? reusableVoskModel, IProgress<string> runtimeProgress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (choice.IsWhisper)
        {
            var cli = FindWhisperCli() ?? throw new FileNotFoundException("whisper-cli.exe is missing for the selected Whisper.cpp model.");
            // Do not force a multilingual model to the role's language: this caused the Persian
            // model to turn English phrases into Persian and the English model to translate Persian.
            // Let the audio drive detection; only a genuinely English-only model must be forced to en.
            var decodeLanguage = choice.EnglishOnly ? "en" : null;
            _lastWhisperDetectedLanguage = null;
            return await RunWhisperAsync(cli, choice.WhisperModel!, audioPath, outputBase, decodeLanguage,
                cancellationToken, includeLanguageMetadata: !choice.EnglishOnly);
        }
        if (choice.UsesVoskRuntime)
        {
            var voskModel = reusableVoskModel ?? throw new InvalidOperationException($"The {language} role model {choice.Id} was not initialized.");
            return await Task.Run(() => RunVoskBatch(voskModel, audioPath, cancellationToken), cancellationToken);
        }
        if (choice.IsFasterWhisper)
        {
            // Faster-whisper models in this catalog are multilingual. Do not force a language
            // based only on the model's UI role; otherwise mixed speech is translated wholesale.
            return await FasterWhisperRuntime.TranscribeAsync(
                _root, ModelPath(choice), audioPath, "auto", AutoLanguageText.Balanced,
                runtimeProgress, cancellationToken, preserveEnglishTechnicalTerms: false,
                transcriptionLanguagePreference: _settings.TranscriptionLanguagePreference);
        }
        throw new NotSupportedException($"The selected model {choice.Provider} / {choice.Id} does not have a transcription runtime.");
    }

    private string ProviderModelMarker(ProviderModelInfo model) => Path.Combine(ProviderModelStorageDirectory(model), ".madnegar-download-complete");
    private static bool RequiresKaldiFolder(ProviderModelInfo model) => model.Provider is "Vosk" or "Diba-STT";
    private static bool HasKaldiFolderStructure(string directory) => ModelValidation.HasKaldiModelStructure(directory);
    private static bool HasVoskFolderStructure(string directory) => HasKaldiFolderStructure(directory);
    private static string SafePathPart(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_'));
    private bool IsModelDownloaded(SpeechModelChoice model) => model.WhisperModel is not null
        ? ModelValidation.IsWhisperModelReady(ModelPath(model.WhisperModel))
        : IsProviderModelInstalled(model.ProviderModel!);

    private bool IsProviderModelInstalled(ProviderModelInfo model)
    {
        var storage = ProviderModelStorageDirectory(model);
        if (!Directory.Exists(storage)) return false;
        if (RequiresKaldiFolder(model)) return HasKaldiFolderStructure(ProviderModelDirectory(model));
        if (model.Provider == "faster-whisper") return ModelValidation.IsFasterWhisperModelReady(storage);
        return File.Exists(ProviderModelMarker(model)) && !ModelValidation.HasPartialArtifacts(storage);
    }

    private void ToggleThemeMode()
    {
        var isCurrentlyDark = ThemeManager.IsDark(_settings.ThemeMode);
        _settings.ThemeMode = isCurrentlyDark ? "Light" : "Dark";
        try { AppSettingsStore.Save(_root, _settings); } catch { }
        ThemeManager.Apply(this, _settings.ThemeMode);
        ApplyTrayMenuTheme();
        RefreshThemeToggleButton();
        RefreshControls();
        Invalidate(true);
    }

    private void RefreshThemeToggleButton()
    {
        var isDark = ThemeManager.IsDark(_settings.ThemeMode);
        _themeToggleButton.IsDarkMode = isDark;
        _windowToolTip.SetToolTip(_themeToggleButton, isDark ? "Switch to light mode" : "Switch to dark mode");
        ThemeManager.ApplyControl(_themeToggleButton, _settings.ThemeMode);
    }

    private void OpenSettings()
    {
        if (_isBusy || _isRecording || _isTranscribing) return;
        UnregisterGlobalHotkey();
        try
        {
            using var dialog = new SettingsForm(_root, _settings);
            dialog.ShowDialog(this);
        }
        finally
        {
            _settings = AppSettingsStore.Load(_root);
            RefreshTranscriptionPreferenceControl();
            ThemeManager.Apply(this, _settings.ThemeMode);
            ApplyTrayMenuTheme();
            RefreshThemeToggleButton();
            RefreshDownloadedModels();
            PopulateMainDualModelChoices();
            UpdateModelSelectorMode();
            RefreshControls();
            RefreshGlobalHotkeyRegistration(showError: true);
        }
    }

    private void TranscriptionPreferenceComboSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_syncingTranscriptionPreference || _transcriptionPreferenceCombo.SelectedIndex < 0) return;
        _settings.TranscriptionLanguagePreference = _transcriptionPreferenceCombo.SelectedIndex == 1
            ? AutoLanguageText.EnglishFirstPreference
            : AutoLanguageText.PersianFirstPreference;
        try { AppSettingsStore.Save(_root, _settings); } catch { }
    }

    private void RefreshTranscriptionPreferenceControl()
    {
        _syncingTranscriptionPreference = true;
        try
        {
            _transcriptionPreferenceCombo.SelectedIndex =
                AutoLanguageText.NormalizeTranscriptionLanguagePreference(_settings.TranscriptionLanguagePreference) ==
                AutoLanguageText.EnglishFirstPreference ? 1 : 0;
        }
        finally { _syncingTranscriptionPreference = false; }
    }

    private void RefreshDownloadedModels()
    {
        var savedKey = !string.IsNullOrWhiteSpace(_settings.SelectedModelKey) ? _settings.SelectedModelKey : _settings.SelectedModelId;
        var previousKey = !string.IsNullOrWhiteSpace(savedKey) ? savedKey : SelectedModel?.Key;
        var downloaded = ModelCatalog.Models.Where(m => ModelValidation.IsWhisperModelReady(ModelPath(m)))
            .Select(m => new SpeechModelChoice(m, null))
            .Concat(ProviderModelCatalog.All.Where(m => !m.IsWhisper && IsProviderModelInstalled(m))
                .Select(m => new SpeechModelChoice(null, m)))
            .Where(m => m.IsSupportedForFinal)
            .OrderBy(m => m.IsSupportedForFinal ? 0 : 1)
            .ThenBy(m => m.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _modelCombo.SelectedIndexChanged -= ModelComboSelectedIndexChanged;
        _modelCombo.BeginUpdate();
        _modelCombo.Items.Clear();
        foreach (var model in downloaded) _modelCombo.Items.Add(model);
        _modelCombo.EndUpdate();
        _modelCombo.SelectedIndexChanged += ModelComboSelectedIndexChanged;

        var preferredIndex = downloaded.FindIndex(m =>
            string.Equals(m.Key, previousKey, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.Id, previousKey, StringComparison.OrdinalIgnoreCase));
        if (downloaded.Count > 0)
        {
            if (preferredIndex >= 0)
            {
                _modelCombo.SelectedIndex = preferredIndex;
            }
            else if (!string.IsNullOrWhiteSpace(savedKey))
            {
                // A persisted choice must not be silently replaced by a different model.
                _modelCombo.SelectedIndex = -1;
                _progressLabel.Text = $"Saved model '{savedKey}' is missing or incomplete. Re-download it or explicitly choose another model.";
            }
            else
            {
                preferredIndex = downloaded.FindIndex(m => m.IsWhisper && m.Id == "base");
                _modelCombo.SelectedIndex = preferredIndex >= 0 ? preferredIndex : 0;
            }
            UpdateModelStatus();
        }
        else
        {
            _modelCombo.SelectedIndex = -1;
            UpdateModelStatus();
            _progressLabel.Text = string.IsNullOrWhiteSpace(savedKey)
                ? "Download a model in Settings before recording."
                : $"Saved model '{savedKey}' is missing or incomplete. Open Settings → Downloads to re-download it.";
        }
        PopulateMainDualModelChoices();
        UpdateModelSelectorMode();
    }

    private bool IsDualModelAutoActive => _languageCombo.SelectedIndex == 0 &&
        AutoLanguageText.NormalizeForSettings(_settings.AutoLanguageStrategy) == AutoLanguageText.DualModelMixed;

    private IReadOnlyList<SpeechModelChoice> GetReadyDualModelChoices() =>
        ModelCatalog.Models.Select(model => new SpeechModelChoice(model, null))
            .Concat(ProviderModelCatalog.All.Where(item => !item.IsWhisper)
                .Select(item => new SpeechModelChoice(null, item)))
            .Where(choice => choice.IsSupportedForFinal && IsModelDownloaded(choice))
            .OrderBy(choice => choice.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void PopulateMainDualModelChoices()
    {
        if (_mainDualPersianModelCombo.IsDisposed || _mainDualEnglishModelCombo.IsDisposed) return;
        var ready = GetReadyDualModelChoices();
        var persian = ready.Where(choice => DualModelSelectionPolicy.SupportsLanguageRole(choice, "fa"))
            .Select(choice => new DualModelChoiceOption(choice.Key, $"FA · {choice.DisplayName} · {choice.Provider} · {choice.Languages}"))
            .ToList();
        var english = ready.Where(choice => DualModelSelectionPolicy.SupportsLanguageRole(choice, "en"))
            .Select(choice => new DualModelChoiceOption(choice.Key, $"EN · {choice.DisplayName} · {choice.Provider} · {choice.Languages}"))
            .ToList();

        _populatingMainDualModelChoices = true;
        try
        {
            PopulateMainDualModelCombo(_mainDualPersianModelCombo, persian,
                DualModelSelectionPolicy.LegacyOrSavedKey(_settings.AutoMixedPersianModelKey, _settings.AutoMixedPersianModelId), "FA");
            PopulateMainDualModelCombo(_mainDualEnglishModelCombo, english,
                DualModelSelectionPolicy.LegacyOrSavedKey(_settings.AutoMixedEnglishModelKey, _settings.AutoMixedEnglishModelId), "EN");
        }
        finally { _populatingMainDualModelChoices = false; }
    }

    private static void PopulateMainDualModelCombo(ThemedComboBox combo, IReadOnlyList<DualModelChoiceOption> choices, string savedKey, string role)
    {
        combo.BeginUpdate();
        combo.Items.Clear();
        combo.Items.Add(new DualModelChoiceOption(string.Empty,
            choices.Count == 0 ? $"{role} · No ready models — Settings → Downloads" : $"{role} · Choose model…"));
        foreach (var choice in choices) combo.Items.Add(choice);
        var selected = choices.ToList().FindIndex(choice => choice.Key.Equals(savedKey, StringComparison.OrdinalIgnoreCase));
        combo.SelectedIndex = selected >= 0 ? selected + 1 : 0;
        combo.Enabled = choices.Count > 0;
        combo.EndUpdate();
    }

    private void MainDualModelChoiceChanged(string role)
    {
        if (_populatingMainDualModelChoices) return;
        var combo = role.Equals("fa", StringComparison.OrdinalIgnoreCase)
            ? _mainDualPersianModelCombo : _mainDualEnglishModelCombo;
        if (combo.SelectedItem is DualModelChoiceOption option && !string.IsNullOrWhiteSpace(option.Key))
        {
            var separator = option.Key.IndexOf("::", StringComparison.Ordinal);
            if (role.Equals("fa", StringComparison.OrdinalIgnoreCase))
            {
                _settings.AutoMixedPersianModelKey = option.Key;
                _settings.AutoMixedPersianModelId = separator >= 0 ? option.Key[(separator + 2)..] : option.Key;
            }
            else
            {
                _settings.AutoMixedEnglishModelKey = option.Key;
                _settings.AutoMixedEnglishModelId = separator >= 0 ? option.Key[(separator + 2)..] : option.Key;
            }
            try { AppSettingsStore.Save(_root, _settings); } catch { }
        }
        UpdateModelStatus();
        RefreshControls();
    }

    private (SpeechModelChoice Persian, SpeechModelChoice English)? TryGetSelectedDualModelPair()
    {
        try
        {
            var persianKey = (_mainDualPersianModelCombo.SelectedItem as DualModelChoiceOption)?.Key;
            if (string.IsNullOrWhiteSpace(persianKey))
                persianKey = DualModelSelectionPolicy.LegacyOrSavedKey(_settings.AutoMixedPersianModelKey, _settings.AutoMixedPersianModelId);
            var englishKey = (_mainDualEnglishModelCombo.SelectedItem as DualModelChoiceOption)?.Key;
            if (string.IsNullOrWhiteSpace(englishKey))
                englishKey = DualModelSelectionPolicy.LegacyOrSavedKey(_settings.AutoMixedEnglishModelKey, _settings.AutoMixedEnglishModelId);
            var persian = ResolveDualModelChoice(persianKey, _settings.AutoMixedPersianModelId, "Persian");
            var english = ResolveDualModelChoice(englishKey, _settings.AutoMixedEnglishModelId, "English");
            if (string.Equals(persian.Key, english.Key, StringComparison.OrdinalIgnoreCase)) return null;
            return (persian, english);
        }
        catch { return null; }
    }

    private SpeechModelChoice? ActiveTranscriptionModel
    {
        get
        {
            if (!IsDualModelAutoActive) return SelectedModel;
            return TryGetSelectedDualModelPair()?.Persian;
        }
    }

    private bool HasUsableCurrentModelSelection()
    {
        if (IsDualModelAutoActive) return TryGetSelectedDualModelPair() is not null;
        return SelectedModel is { IsSupportedForFinal: true } model && IsModelDownloaded(model);
    }

    private IReadOnlyList<SpeechModelChoice> CurrentModelsToPrepare()
    {
        if (IsDualModelAutoActive && TryGetSelectedDualModelPair() is { } pair)
            return [pair.Persian, pair.English];
        return ActiveTranscriptionModel is { } model ? [model] : Array.Empty<SpeechModelChoice>();
    }

    private void UpdateModelSelectorMode()
    {
        var dual = IsDualModelAutoActive;
        _modelSelectionLabel.Text = dual ? "Dual-model Auto · choose Persian + English models" : "Downloaded speech model";
        _modelCombo.Visible = !dual;
        _mainDualPersianModelCombo.Visible = dual;
        _mainDualEnglishModelCombo.Visible = dual;
        UpdateModelStatus();
    }

    private void ModelComboSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (SelectedModel is { } model)
        {
            _settings.SelectedModelId = model.Id;
            _settings.SelectedModelKey = model.Key;
            try { AppSettingsStore.Save(_root, _settings); } catch { }
        }
        UpdateModelStatus();
        RefreshControls();
    }

    private void UpdateModelStatus()
    {
        if (IsDualModelAutoActive)
        {
            var pair = TryGetSelectedDualModelPair();
            if (pair is { } models)
            {
                _modelStatus.Text = $"Synced with Settings · FA: {models.Persian.DisplayName} ({models.Persian.Provider}) · EN: {models.English.DisplayName} ({models.English.Provider})";
                _modelStatus.ForeColor = ThemeManager.IsDark(_settings.ThemeMode)
                    ? Color.FromArgb(134, 239, 172) : Color.FromArgb(22, 101, 52);
            }
            else
            {
                _modelStatus.Text = "Choose two different, complete models: one for Persian and one for English.";
                _modelStatus.ForeColor = ThemeManager.IsDark(_settings.ThemeMode)
                    ? Color.FromArgb(253, 230, 138) : Color.FromArgb(180, 83, 9);
            }
            return;
        }

        var model = SelectedModel;
        if (model is null)
        {
            var savedKey = !string.IsNullOrWhiteSpace(_settings.SelectedModelKey) ? _settings.SelectedModelKey : _settings.SelectedModelId;
            _modelStatus.Text = string.IsNullOrWhiteSpace(savedKey)
                ? "No complete models found. Open Settings → Downloads to get started."
                : $"Saved model '{savedKey}' is missing or incomplete. Re-download it or choose another model.";
            _modelStatus.ForeColor = ThemeManager.IsDark(_settings.ThemeMode)
                ? Color.FromArgb(253, 230, 138)
                : Color.FromArgb(180, 83, 9);
            return;
        }

        if (!model.IsSupportedForFinal)
        {
            _modelStatus.Text = $"Downloaded · {model.Provider} · runtime not available yet";
            _modelStatus.ForeColor = ThemeManager.IsDark(_settings.ThemeMode) ? Color.FromArgb(253, 230, 138) : Color.FromArgb(180, 83, 9);
            return;
        }

        _modelStatus.Text = model.IsVosk
            ? $"Downloaded · Vosk · {model.Languages} · offline final transcription"
            : model.IsDiba
                ? $"Downloaded · Diba-STT · Persian · local Kaldi/Vosk engine"
                : model.IsFasterWhisper
                    ? $"Downloaded · faster-whisper · multilingual · CPU int8 runtime"
                    : model.EnglishOnly
                        ? $"Downloaded · {model.ApproxSize} · English only"
                        : $"Downloaded · {model.ApproxSize} · Persian + English";
        _modelStatus.ForeColor = ThemeManager.IsDark(_settings.ThemeMode)
            ? Color.FromArgb(134, 239, 172)
            : Color.FromArgb(22, 101, 52);
    }

    private void RefreshControls()
    {
        var model = ActiveTranscriptionModel;
        var hasModel = model is not null && model.IsSupportedForFinal && IsModelDownloaded(model) && HasUsableCurrentModelSelection();
        var modelSelectorsEnabled = !_isBusy && !_isRecording && !_isTranscribing;
        _modelCombo.Enabled = !IsDualModelAutoActive && modelSelectorsEnabled && _modelCombo.Items.Count > 0;
        _mainDualPersianModelCombo.Enabled = IsDualModelAutoActive && modelSelectorsEnabled && _mainDualPersianModelCombo.Items.Count > 1;
        _mainDualEnglishModelCombo.Enabled = IsDualModelAutoActive && modelSelectorsEnabled && _mainDualEnglishModelCombo.Items.Count > 1;
        _settingsButton.Enabled = !_isBusy && !_isRecording && !_isTranscribing;
        _languageCombo.Enabled = !_isBusy && !_isRecording && !_isTranscribing;
        _copyButton.Enabled = _transcriptBox.TextLength > 0;
        _exportButton.Enabled = _transcriptBox.TextLength > 0 && !_isRecording && !_isStoppingRecording && !_isTranscribing && !_isBusy;
        _historyButton.Enabled = !_isRecording && !_isStoppingRecording && !_isTranscribing && !_isBusy;
        _fileTranscriptionButton.Enabled = hasModel && !_isRecording && !_isStoppingRecording && !_isTranscribing && !_isBusy && !_directTypingSession;
        _batchTranscriptionButton.Enabled = hasModel && !_isRecording && !_isStoppingRecording && !_isTranscribing && !_isBusy && !_directTypingSession;
        _undoTextFixButton.Enabled = _rawTranscriptBeforeNormalization is not null
            && string.Equals(_normalizedTranscriptForUndo, _transcriptBox.Text, StringComparison.Ordinal)
            && !_isRecording && !_isStoppingRecording && !_isTranscribing && !_isBusy;
        // Do not clear only the visible textbox while live text is still being merged.
        // Finalization is also busy, so the completed transcript cannot reappear after Clear.
        _clearButton.Enabled = _transcriptBox.TextLength > 0 && !_isRecording && !_isStoppingRecording && !_isTranscribing && !_isBusy;
        _retryInsertButton.Enabled = _pendingDirectTypingTarget is not null
            && !string.IsNullOrWhiteSpace(_pendingDirectTypingText)
            && string.Equals(_pendingDirectTypingText, _transcriptBox.Text, StringComparison.Ordinal)
            && !_isRecording && !_isStoppingRecording && !_isTranscribing && !_isBusy && !_directTypingSession;
        _cancelDirectTypingButton.Visible = _directTypingSession || _isFileTranscribing;
        _cancelDirectTypingButton.Text = _isFileTranscribing ? "Cancel File Transcription" : "Cancel Voice Typing";
        _cancelDirectTypingButton.Enabled = (_directTypingSession && !_directTypingCancelled || _isFileTranscribing) && !_isClosing;
        UpdateRecordButtonState(hasModel);
    }

    private void UpdateRecordButtonState(bool? modelAvailable = null)
    {
        var model = ActiveTranscriptionModel;
        var hasModel = modelAvailable ?? (model is not null && model.IsSupportedForFinal && IsModelDownloaded(model));
        if (_isStoppingRecording)
        {
            _startButton.Text = "…  Stopping recording";
            _startButton.Tag = "secondary";
            _startButton.Enabled = false;
        }
        else if (_isRecording)
        {
            _startButton.Text = "■  Stop & Transcribe";
            _startButton.Tag = "danger";
            _startButton.Enabled = true;
        }
        else if (_isBusy)
        {
            _startButton.Text = "…  Preparing microphone";
            _startButton.Tag = "secondary";
            _startButton.Enabled = false;
        }
        else if (_isTranscribing)
        {
            _startButton.Text = "…  Transcribing";
            _startButton.Tag = "secondary";
            _startButton.Enabled = false;
        }
        else
        {
            _startButton.Text = "▶  Start Recording";
            _startButton.Tag = "primary";
            _startButton.Enabled = hasModel;
        }
        ThemeManager.ApplyControl(_startButton, _settings.ThemeMode);
    }

    private async Task StartRecordingClickedAsync(bool fromGlobalHotkey = false)
    {
        var model = ActiveTranscriptionModel;
        if (model is null || !IsModelDownloaded(model) || !HasUsableCurrentModelSelection())
        {
            var detail = IsDualModelAutoActive
                ? "Choose two different, complete models in the Persian and English selectors. You can also choose them in Settings → Transcription."
                : "Open Settings → Downloads and download a speech model first.";
            ReportRecordingStartProblem("Model required", detail, fromGlobalHotkey);
            return;
        }
        if (!model.IsSupportedForFinal)
        {
            ReportRecordingStartProblem("Engine not available yet",
                $"{model.Provider} / {model.Id} is downloaded, but its runtime is not available in this version. Choose Whisper.cpp, Vosk, Diba-STT, or faster-whisper for transcription.", fromGlobalHotkey);
            return;
        }
        if (model.EnglishOnly && _languageCombo.SelectedIndex != 2)
        {
            ReportRecordingStartProblem("English-only model",
                "This model is English-only. Set Spoken language to English, or choose a multilingual/Persian model.", fromGlobalHotkey);
            return;
        }
        if (_languageCombo.SelectedIndex == 2 &&
            model.Languages.Contains("Persian", StringComparison.OrdinalIgnoreCase) &&
            !model.Languages.Contains("English", StringComparison.OrdinalIgnoreCase))
        {
            ReportRecordingStartProblem("Persian-only model",
                $"{model.Provider} / {model.Id} supports Persian only. Select Auto or Persian, or choose a multilingual model for English speech.", fromGlobalHotkey);
            return;
        }

        using var cts = new CancellationTokenSource();
        _operationCts = cts;
        _isBusy = true;
        _progressLabel.Text = "Checking local speech engine…";
        SetProgressMode(ProgressBarStyle.Blocks, 0);
        RefreshControls();
        try
        {
            var modelsToPrepare = CurrentModelsToPrepare();
            if (modelsToPrepare.Count == 0)
                throw new InvalidOperationException("No valid transcription model pair is selected. Choose the Persian and English models in the main window or Settings → Transcription.");

            var whisperRuntimeReady = FindWhisperCli() is not null;
            var fasterRuntimeReady = FasterWhisperRuntime.IsReady(_root);
            foreach (var candidate in modelsToPrepare)
            {
                cts.Token.ThrowIfCancellationRequested();
                if (!IsModelDownloaded(candidate) || !candidate.IsSupportedForFinal)
                    throw new InvalidOperationException($"The selected model {candidate.Provider} / {candidate.Id} is missing, incomplete, or has no transcription runtime. Re-download it or choose another model.");

                if (candidate.IsWhisper)
                {
                    if (!whisperRuntimeReady)
                    {
                        if (fromGlobalHotkey)
                        {
                            ReportRecordingStartProblem("Whisper runtime setup required",
                                "Start one recording from the MadNegar window first so the local Whisper runtime can be installed. The global shortcut will not start a background download.", true);
                            return;
                        }
                        _progressLabel.Text = "Setting up the Whisper.cpp engine (one-time download)…";
                        await DownloadAndInstallRuntimeAsync(cts.Token);
                        whisperRuntimeReady = FindWhisperCli() is not null;
                        if (!whisperRuntimeReady)
                            throw new FileNotFoundException("The Whisper.cpp runtime setup completed without finding whisper-cli.exe. Repair the local runtime and try again.");
                    }
                }
                else if (candidate.UsesVoskRuntime)
                {
                    var modelDirectory = ModelPath(candidate);
                    if (!HasKaldiFolderStructure(modelDirectory))
                        throw new DirectoryNotFoundException($"The downloaded {candidate.Provider} model folder is incomplete or has an unexpected layout. Re-download {candidate.Id} from Settings → Downloads.");
                }
                else if (candidate.IsFasterWhisper)
                {
                    var modelDirectory = ModelPath(candidate);
                    if (!ModelValidation.IsFasterWhisperModelReady(modelDirectory))
                        throw new FileNotFoundException($"The faster-whisper model {candidate.Id} is incomplete. MadNegar needs non-empty model.bin, config.json and tokenizer/vocabulary files, with no partial downloads. Re-download it from Settings → Downloads.");
                    if (!fasterRuntimeReady)
                    {
                        if (fromGlobalHotkey)
                        {
                            ReportRecordingStartProblem("Runtime setup required",
                                "Start recording once from the MadNegar window to approve and complete the faster-whisper runtime setup. The global shortcut will not start a background download.", true);
                            return;
                        }
                        var setupChoice = MessageBox.Show(this,
                            "The first use of faster-whisper needs a private Python 3.11 runtime and the faster-whisper package (downloaded once). These are used locally; your audio is not uploaded. Continue with setup?",
                            "Set up faster-whisper", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        if (setupChoice != DialogResult.Yes) return;
                        var setupProgress = new Progress<string>(message =>
                        {
                            if (!_isClosing && !IsDisposed) _progressLabel.Text = message;
                        });
                        await FasterWhisperRuntime.EnsureReadyAsync(_root, setupProgress, cts.Token);
                        fasterRuntimeReady = FasterWhisperRuntime.IsReady(_root);
                        if (!fasterRuntimeReady)
                            throw new InvalidOperationException("The faster-whisper runtime setup did not complete. Check the setup diagnostics and retry.");
                    }
                }
            }
            cts.Token.ThrowIfCancellationRequested();
            _progressLabel.Text = "Speech engine ready.";
            if (_directTypingSession && _settings.VoiceTypingSoundFeedback)
            {
                // Play the start tone before opening the microphone stream so the tone itself
                // cannot be captured as the first samples of the user's recording.
                VoiceTypingSoundFeedback.PlayStart();
                await Task.Delay(350, cts.Token);
            }
            StartCapture();
        }
        catch (OperationCanceledException)
        {
            _progressLabel.Text = "Setup cancelled.";
        }
        catch (Exception ex)
        {
            _progressLabel.Text = "Could not prepare the speech engine.";
            var detail = "MadNegar could not set up the local speech engine. Check your internet connection and try again.\n\n" + ex.Message;
            if (fromGlobalHotkey) ShowHotkeyNotification("Voice typing could not start", detail);
            else MessageBox.Show(this, detail, "Speech engine setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _operationCts = null;
            _isBusy = false;
            RefreshControls();
        }
    }

    private void StartCapture()
    {
        try
        {
            ClearImportedAudioSubtitleContext();
            Directory.CreateDirectory(TempDirectory);
            _recordingPath = Path.Combine(TempDirectory, $"recording-{Guid.NewGuid():N}.wav");

            using var enumerator = new MMDeviceEnumerator();
            MMDevice device;
            if (!string.IsNullOrWhiteSpace(_settings.SelectedMicrophoneId))
            {
                try { device = enumerator.GetDevice(_settings.SelectedMicrophoneId); }
                catch { device = GetDefaultCaptureDevice(enumerator); }
            }
            else
            {
                device = GetDefaultCaptureDevice(enumerator);
            }

            try
            {
                var capture = new WasapiCapture(device, true, 50);
                _captureDevice = device;
                _waveIn = capture;
                _captureWaveFormat = capture.WaveFormat;
                DiagnosticsState.RecordingStarted(
                    device.FriendlyName,
                    capture.WaveFormat.SampleRate,
                    capture.WaveFormat.Channels,
                    capture.WaveFormat.BitsPerSample,
                    capture.WaveFormat.Encoding.ToString());
                _waveWriter = new WaveFileWriter(_recordingPath, capture.WaveFormat);
                lock (_liveAudioLock)
                {
                    _liveAudioBuffer.Position = 0;
                    _liveAudioBuffer.SetLength(0);
                }
                capture.DataAvailable += (_, e) =>
                {
                    if (e.BytesRecorded > 0)
                    {
                        if (AudioProcessor.TryMeasurePeakAndRms(e.Buffer, e.BytesRecorded, capture.WaveFormat,
                            _settings.MicrophoneBoostDb, out var diagnosticPeak, out var diagnosticRms))
                            DiagnosticsState.UpdateAudioMeter(diagnosticPeak, diagnosticRms);
                        else
                            DiagnosticsState.SetAudioMeterUnsupported(capture.WaveFormat.ToString());
                    }
                    try { _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded); }
                    catch { /* The stopped/error path reports capture failures where possible. */ }
                    if (_settings.LiveTranscriptionEnabled && e.BytesRecorded > 0)
                    {
                        try
                        {
                            lock (_liveAudioLock)
                            {
                                _liveAudioBuffer.Position = _liveAudioBuffer.Length;
                                _liveAudioBuffer.Write(e.Buffer, 0, e.BytesRecorded);
                                TrimLiveAudioBufferUnsafe(capture.WaveFormat);
                            }
                        }
                        catch { /* The final full recording remains authoritative if a preview chunk fails. */ }
                    }
                };
                capture.RecordingStopped += OnRecordingStopped;
                capture.StartRecording();
            }
            catch
            {
                device.Dispose();
                throw;
            }

            _isRecording = true;
            _liveTranscriptText = string.Empty;
            _statusLabel.Text = _settings.LiveTranscriptionEnabled
                ? "Recording · live transcription enabled"
                : "Recording… Speak now.";
            _progressLabel.Text = _settings.LiveTranscriptionEnabled
                ? (_settings.FastLivePreviewEnabled
                    ? "Fast live preview enabled. It uses the smallest downloaded multilingual model; final transcription uses your selected model."
                    : "Live preview enabled. Final transcription runs when you stop.")
                : $"Recording from {_settings.SelectedMicrophoneName ?? "Windows default microphone"}…";
            SetProgressMode(ProgressBarStyle.Blocks, 0);
            if (_settings.LiveTranscriptionEnabled) _liveTranscriptionTimer.Start();
            else _liveTranscriptionTimer.Stop();
            RefreshControls();
            if (_directTypingSession)
                ShowHotkeyNotification("Listening", "Speak now. Press the selected shortcut again to stop and transcribe; keep the destination field focused.");
        }
        catch (Exception ex)
        {
            DiagnosticsState.RecordError("Start microphone", ex.Message);
            try { _waveWriter?.Dispose(); } catch { }
            _waveWriter = null;
            try { _waveIn?.Dispose(); } catch { }
            _waveIn = null;
            try { _captureDevice?.Dispose(); } catch { }
            _captureDevice = null;
            TryDelete(_recordingPath);
            _recordingPath = null;
            _isRecording = false;
            _isStoppingRecording = false;
            RefreshControls();
            var detail = "Could not start microphone recording. Open Settings → Audio, select the microphone, and run the test.\n\n" + ex.Message;
            if (_directTypingSession)
            {
                ShowHotkeyNotification("Microphone unavailable", detail);
                FinishDirectTypingSession();
            }
            else MessageBox.Show(this, detail, "Microphone unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static MMDevice GetDefaultCaptureDevice(MMDeviceEnumerator enumerator)
    {
        try { return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); }
        catch { return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console); }
    }

    private void StopRecording()
    {
        if (!_isRecording || _isStoppingRecording || _waveIn is null) return;
        _isStoppingRecording = true;
        DiagnosticsState.SetStage("Finalizing recording");
        try { _liveTranscriptionCts?.Cancel(); } catch { }
        _statusLabel.Text = "Finishing recording…";
        _progressLabel.Text = "Preparing audio for transcription…";
        RefreshControls();
        if (_directTypingSession)
            ShowHotkeyNotification("Recording stopped", "Final transcription is running. Keep the same destination field focused for direct insertion.");
        try { _waveIn.StopRecording(); }
        catch (Exception ex)
        {
            DiagnosticsState.RecordError("Stop recording", ex.Message);
            _isStoppingRecording = false;
            RefreshControls();
            if (_directTypingSession) ShowHotkeyNotification("Could not stop recording", ex.Message);
            else MessageBox.Show(this, ex.Message, "Could not stop recording", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        var completedPath = _recordingPath;
        try { _waveWriter?.Dispose(); } catch { }
        _waveWriter = null;
        try { _waveIn?.Dispose(); } catch { }
        _waveIn = null;
        try { _captureDevice?.Dispose(); } catch { }
        _captureDevice = null;

        if (_isClosing)
        {
            TryDelete(completedPath);
            return;
        }
        if (IsDisposed || !IsHandleCreated)
        {
            TryDelete(completedPath);
            return;
        }

        BeginInvoke(new Action(async () =>
        {
            _liveTranscriptionTimer.Stop();
            _isRecording = false;
            _isStoppingRecording = false;
            // RecordingStopped is raised after capture and file writer cleanup above, so give
            // immediate stop feedback before waiting for any outstanding live-preview work.
            if (e.Exception is null && _directTypingSession && _settings.VoiceTypingSoundFeedback)
                VoiceTypingSoundFeedback.PlayStop();
            if (_liveTranscriptionTask is { } liveTask)
            {
                try { await liveTask; } catch { /* Live preview is optional; final transcription still proceeds. */ }
            }
            DisposeLiveVoskModel();
            if (_directTypingSession && _directTypingCancelled)
            {
                TryDelete(completedPath);
                _recordingPath = null;
                _isTranscribing = false;
                _statusLabel.Text = "Voice typing cancelled.";
                _progressLabel.Text = "The recording was discarded; nothing was inserted.";
                ShowHotkeyNotification("Voice typing cancelled", "The recording was discarded; nothing was inserted.");
                FinishDirectTypingSession();
                RefreshControls();
                return;
            }
            if (e.Exception is not null)
            {
                DiagnosticsState.RecordError("Audio capture", e.Exception.Message);
                _statusLabel.Text = "Recording stopped with an error.";
                if (_directTypingSession)
                {
                    ShowHotkeyNotification("Recording error", e.Exception.Message);
                    FinishDirectTypingSession();
                }
                else MessageBox.Show(this, e.Exception.Message, "Recording error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                TryDelete(completedPath);
                _recordingPath = null;
                RefreshControls();
                return;
            }
            if (string.IsNullOrWhiteSpace(completedPath) || !File.Exists(completedPath))
            {
                _statusLabel.Text = "No audio was recorded.";
                if (_directTypingSession)
                {
                    ShowHotkeyNotification("No audio recorded", "Nothing was inserted. Try the shortcut again and check the selected microphone.");
                    FinishDirectTypingSession();
                }
                _recordingPath = null;
                RefreshControls();
                return;
            }
            _isTranscribing = true;
            DiagnosticsState.SetStage("Preparing transcription");
            RefreshControls();
            if (_directTypingSession)
                ShowHotkeyNotification("Transcribing", "The final transcript is being prepared locally. It has not been inserted yet.");
            await TranscribeAsync(completedPath);
        }));
    }

    private void TrimLiveAudioBufferUnsafe(WaveFormat format)
    {
        // Keep the live preview buffer bounded if a large model is slower than real time.
        // The full recording is stored separately and is always used for the final transcript.
        var maximumBytes = Math.Max(format.BlockAlign, (long)format.AverageBytesPerSecond * 20L);
        // Trim in two-second batches instead of copying a large MemoryStream on every audio callback.
        var trimThresholdBytes = maximumBytes + (long)format.AverageBytesPerSecond * 2L;
        if (_liveAudioBuffer.Length <= trimThresholdBytes) return;

        var allBytes = _liveAudioBuffer.ToArray();
        var keepBytes = (int)Math.Min(allBytes.LongLength, maximumBytes);
        keepBytes -= keepBytes % Math.Max(1, format.BlockAlign);
        var start = allBytes.Length - keepBytes;
        start -= start % Math.Max(1, format.BlockAlign);
        keepBytes = allBytes.Length - start;

        _liveAudioBuffer.Position = 0;
        _liveAudioBuffer.SetLength(0);
        _liveAudioBuffer.Write(allBytes, start, keepBytes);
    }

    private async Task ProcessLiveTranscriptionTickAsync()
    {
        if (!_settings.LiveTranscriptionEnabled || !_isRecording || _isStoppingRecording || _isClosing) return;
        var previewWatch = Stopwatch.StartNew();
        var previewAttempted = false;
        var selectedModel = ActiveTranscriptionModel;
        var model = selectedModel is null ? null : ResolveLivePreviewModel(selectedModel);
        var format = _captureWaveFormat;
        var cli = model?.IsWhisper == true ? FindWhisperCli() : null;
        if (model?.IsFasterWhisper == true && !FasterWhisperRuntime.IsReady(_root))
        {
            if (_isRecording && !_isStoppingRecording)
                _progressLabel.Text = "faster-whisper Live Preview needs its local runtime. Stop recording and run one final faster-whisper transcription, then try again.";
            return;
        }
        if (model is null || selectedModel is null || format is null ||
            !model.IsSupportedForLive || (model.IsWhisper && cli is null))
        {
            if (_settings.LiveTranscriptionEnabled && _isRecording && !_isStoppingRecording)
            {
                if (model is null)
                {
                    var requestedLanguage = _languageCombo.SelectedIndex switch { 1 => "Persian", 2 => "English", _ => "Auto (Persian/English)" };
                    _progressLabel.Text = $"No compatible live preview model is downloaded for {requestedLanguage}. Download a compatible model or disable live preview.";
                }
                else if (model.IsWhisper && cli is null)
                {
                    _progressLabel.Text = "Whisper.cpp runtime is not installed for live preview. Run a final Whisper.cpp transcription once to install it, or choose a compatible Vosk/Diba model with an explicit language.";
                }
                else if (model.IsFasterWhisper && !FasterWhisperRuntime.IsReady(_root))
                {
                    _progressLabel.Text = "faster-whisper runtime is not ready for live preview. Run one final faster-whisper transcription first, then try again.";
                }
            }
            return;
        }

        byte[] audioBytes;
        lock (_liveAudioLock)
        {
            // Do not feed clips that are too short for a useful speech hypothesis.
            // Shorter threshold lets the first preview start earlier than the old 1.3-second wait.
            var minimumBytes = Math.Max(format.BlockAlign, (long)format.AverageBytesPerSecond * 900L / 1000L);
            if (_liveAudioBuffer.Length < minimumBytes) return;

            audioBytes = _liveAudioBuffer.ToArray();
            previewAttempted = true;
            // Retain a little overlap so words spanning the chunk boundary are less likely to be lost.
            var overlapBytes = (int)Math.Min(audioBytes.LongLength,
                (long)format.AverageBytesPerSecond * 350L / 1000L);
            overlapBytes -= overlapBytes % Math.Max(1, format.BlockAlign);
            _liveAudioBuffer.Position = 0;
            _liveAudioBuffer.SetLength(0);
            if (overlapBytes > 0)
                _liveAudioBuffer.Write(audioBytes, audioBytes.Length - overlapBytes, overlapBytes);
        }

        var id = Guid.NewGuid().ToString("N");
        var chunkPath = Path.Combine(TempDirectory, $"live-chunk-{id}.wav");
        var processedPath = Path.Combine(TempDirectory, $"live-ready-{id}.wav");
        var outputBase = Path.Combine(TempDirectory, $"live-transcript-{id}");
        var outputText = outputBase + ".txt";
        using var liveCts = new CancellationTokenSource();
        _liveTranscriptionCts = liveCts;
        var liveToken = liveCts.Token;
        try
        {
            Directory.CreateDirectory(TempDirectory);
            using (var writer = new WaveFileWriter(chunkPath, format))
                writer.Write(audioBytes, 0, audioBytes.Length);

            await Task.Run(() => AudioProcessor.PrepareForWhisper(
                chunkPath,
                processedPath,
                _settings.MicrophoneBoostDb,
                _settings.AutoLevelQuietSpeech,
                _settings.ReduceLowFrequencyRumble,
                liveToken), liveToken);

            liveToken.ThrowIfCancellationRequested();
            var spokenLanguage = _languageCombo.SelectedIndex switch { 1 => "fa", 2 => "en", _ => "auto" };
        // Auto remains decoder-driven in preview; the transcription preference must not force a
        // whole temporary chunk into one language and damage ordinary code-switching.
        var language = AutoLanguageText.ResolveLivePreviewLanguage(spokenLanguage, _settings.AutoLanguageStrategy);
        var isAutomaticLanguagePreview = spokenLanguage == "auto";
            string segment;
            if (model.IsWhisper)
            {
                // Whisper.cpp uses greedy decoding for temporary captions; the final pass can refine it.
                segment = await RunWhisperAsync(
                    cli!, model.WhisperModel!, processedPath, outputBase, language, liveToken, fastPreview: true);
                if (isAutomaticLanguagePreview &&
                    AutoLanguageText.ShouldTryPersianPreferredPass(segment))
                {
                    // Avoid a second decode on ordinary captions; retry only recognizable all-Latin Finglish.
                    var preferredPersian = await RunWhisperAsync(
                        cli!, model.WhisperModel!, processedPath, outputBase + "-fa-preferred", "fa", liveToken);
                    if (AutoLanguageText.IsPersianCandidate(preferredPersian, segment))
                        segment = _settings.PreserveEnglishTechnicalTermsInAuto
                            ? AutoLanguageText.RestoreEnglishTechnicalTerms(preferredPersian)
                            : preferredPersian;
                }
            }
            else if (model.UsesVoskRuntime)
            {
                // Load Vosk/Kaldi once per recording, then reuse the model and create a lightweight
                // recognizer for each short chunk. This supports both downloaded Vosk and Diba models.
                var voskModel = await Task.Run(() => GetOrCreateLiveVoskModel(ModelPath(model)), liveToken);
                segment = await Task.Run(() => RunVoskBatch(voskModel, processedPath), liveToken);
            }
            else if (model.IsFasterWhisper)
            {
                // Experimental file-based preview: call the local adapter on this bounded chunk.
                // It can be slower than Whisper.cpp/Vosk; the timer skips ticks while a chunk is busy.
                var previewLanguage = language ?? "auto";
                var previewStatus = new Progress<string>(message =>
                {
                    if (_isRecording && !_isStoppingRecording && !_isClosing && !IsDisposed)
                        _progressLabel.Text = "Live Preview · faster-whisper · " + message;
                });
                segment = await FasterWhisperRuntime.TranscribeAsync(
                    _root, ModelPath(model), processedPath, previewLanguage,
                    AutoLanguageText.Balanced, previewStatus, liveToken,
                    preserveEnglishTechnicalTerms: _settings.PreserveEnglishTechnicalTermsInAuto,
                    transcriptionLanguagePreference: _settings.TranscriptionLanguagePreference);
            }
            else
            {
                return;
            }

            segment = segment.Trim();
            if (isAutomaticLanguagePreview && _settings.PreserveEnglishTechnicalTermsInAuto)
                segment = AutoLanguageText.RestoreEnglishTechnicalTerms(segment);
            if (string.IsNullOrWhiteSpace(segment) || _isClosing || IsDisposed) return;

            _liveTranscriptText = MergeTranscriptSegments(_liveTranscriptText, segment);
            _transcriptBox.Text = _liveTranscriptText;
            UpdateTranscriptTextDirection(_liveTranscriptText);
            _transcriptBox.SelectionStart = _transcriptBox.TextLength;
            _transcriptBox.ScrollToCaret();
            _statusLabel.Text = "Recording · live transcription active";
            var autoUsesMonolingualPreview = spokenLanguage == "auto" && model.UsesVoskRuntime;
            _progressLabel.Text = autoUsesMonolingualPreview
                ? $"Live preview · {model.DisplayName} ({model.Provider}, {model.Languages}) · this preview is language-specific; final transcription still uses Auto."
                : string.Equals(model.Key, selectedModel.Key, StringComparison.OrdinalIgnoreCase)
                    ? $"Live preview · {model.DisplayName} ({model.Provider}); final text is refined after you stop."
                    : $"Live preview · {model.DisplayName} ({model.Provider}) · final uses {selectedModel.DisplayName}.";
        }
        catch (OperationCanceledException)
        {
            // Stop/cancel is allowed to interrupt an in-flight preview; the full recording is transcribed next.
        }
        catch (Exception)
        {
            // Don't interrupt capture or show a dialog for an optional preview failure.
            if (!_isClosing && !IsDisposed && _isRecording && !_isStoppingRecording)
                _progressLabel.Text = "Live preview is catching up; final transcription will run when you stop.";
        }
        finally
        {
            if (previewAttempted) DiagnosticsState.RecordLivePreviewCompleted(previewWatch.Elapsed);
            if (ReferenceEquals(_liveTranscriptionCts, liveCts)) _liveTranscriptionCts = null;
            TryDelete(chunkPath);
            TryDelete(processedPath);
            TryDelete(outputText);
            if (_isClosing) DisposeLiveVoskModel();
        }
    }

    private bool IsLiveModelCompatibleWithLanguage(SpeechModelChoice model)
    {
        // Keep the selector and Settings in sync. Auto can list language-specific Vosk/Diba for
        // users who explicitly want that model's preview; their preview is native-language-only.
        if (_languageCombo.SelectedIndex == 0)
        {
            if (model.IsWhisper) return !model.EnglishOnly;
            return model.IsSupportedForLive && (model.IsFasterWhisper || model.UsesVoskRuntime);
        }
        if (_languageCombo.SelectedIndex == 1)
            return !model.EnglishOnly && model.Languages.Contains("Persian", StringComparison.OrdinalIgnoreCase);
        if (_languageCombo.SelectedIndex == 2)
            return model.Languages.Contains("English", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private SpeechModelChoice? ResolveLivePreviewModel(SpeechModelChoice selectedFinalModel)
    {
        var downloadedWhisper = ModelCatalog.Models
            .Where(model => ModelValidation.IsWhisperModelReady(ModelPath(model)))
            .Select(model => new SpeechModelChoice(model, null))
            .Where(IsLiveModelCompatibleWithLanguage)
            .ToList();
        var downloadedProviderModels = ProviderModelCatalog.All
            .Where(model => !model.IsWhisper && (model.Provider is "Vosk" or "Diba-STT" or "faster-whisper") && IsProviderModelInstalled(model))
            .Select(model => new SpeechModelChoice(null, model))
            .Where(IsLiveModelCompatibleWithLanguage)
            .ToList();
        var liveChoices = downloadedWhisper.Concat(downloadedProviderModels)
            .Where(model => model.IsSupportedForLive && IsModelDownloaded(model)).ToList();
        if (liveChoices.Count == 0) return null;

        if (_settings.FastLivePreviewEnabled)
        {
            // Prefer the smallest downloaded compatible multilingual Whisper model. An .en
            // model can only be chosen when Spoken language is explicitly English.
            var fastestWhisper = downloadedWhisper
                .OrderBy(choice => GetLivePreviewModelPriority(choice.WhisperModel!))
                .FirstOrDefault();
            if (fastestWhisper is not null) return fastestWhisper;
            if (selectedFinalModel.IsSupportedForLive && IsModelDownloaded(selectedFinalModel) &&
                IsLiveModelCompatibleWithLanguage(selectedFinalModel)) return selectedFinalModel;
            return liveChoices.OrderBy(GetLivePreviewModelPriority).FirstOrDefault();
        }

        var requestedKey = _settings.LivePreviewModelKey;
        if (!string.IsNullOrWhiteSpace(requestedKey) && !requestedKey.Equals("follow-final", StringComparison.OrdinalIgnoreCase))
        {
            var requested = liveChoices.FirstOrDefault(choice =>
                choice.Key.Equals(requestedKey, StringComparison.OrdinalIgnoreCase) ||
                choice.Id.Equals(requestedKey, StringComparison.OrdinalIgnoreCase));
            if (requested is not null) return requested;
        }

        if (selectedFinalModel.IsSupportedForLive && IsModelDownloaded(selectedFinalModel) &&
            IsLiveModelCompatibleWithLanguage(selectedFinalModel))
            return selectedFinalModel;
        return liveChoices.OrderBy(GetLivePreviewModelPriority).FirstOrDefault();
    }

    private static int GetLivePreviewModelPriority(SpeechModelChoice choice)
    {
        if (choice.WhisperModel is { } whisper) return GetLivePreviewModelPriority(whisper);
        var id = choice.Id.ToLowerInvariant();
        if (choice.IsDiba) return id.Contains("small", StringComparison.Ordinal) ? 0 : 40;
        if (choice.IsVosk) return id.Contains("small", StringComparison.Ordinal) ? 5 : 45;
        return 100;
    }

    private static int GetLivePreviewModelPriority(ModelInfo model)
    {
        var id = model.Id.ToLowerInvariant();
        var familyPriority = id.StartsWith("tiny", StringComparison.Ordinal) ? 0
            : id.StartsWith("base", StringComparison.Ordinal) ? 1
            : id.StartsWith("small", StringComparison.Ordinal) ? 2
            : id.Contains("turbo", StringComparison.Ordinal) ? 3
            : id.StartsWith("medium", StringComparison.Ordinal) ? 4
            : 5;
        var quantizationPriority = id.Contains("q5", StringComparison.Ordinal) ? 0
            : id.Contains("q8", StringComparison.Ordinal) ? 1
            : 2;
        return familyPriority * 10 + quantizationPriority;
    }

    private Vosk.Model GetOrCreateLiveVoskModel(string modelDirectory)
    {
        if (_liveVoskModel is not null && string.Equals(_liveVoskModelPath, modelDirectory, StringComparison.OrdinalIgnoreCase))
            return _liveVoskModel;

        DisposeLiveVoskModel();
        Vosk.Vosk.SetLogLevel(-1);
        var loadedModel = new Vosk.Model(modelDirectory);
        _liveVoskModel = loadedModel;
        _liveVoskModelPath = modelDirectory;
        return loadedModel;
    }

    private void DisposeLiveVoskModel()
    {
        try { _liveVoskModel?.Dispose(); } catch { }
        _liveVoskModel = null;
        _liveVoskModelPath = null;
    }

    private static string MergeTranscriptSegments(string accumulated, string segment)
    {
        static string Normalize(string value) => string.Concat(value.Where(char.IsLetterOrDigit))
            .ToLowerInvariant();

        var left = System.Text.RegularExpressions.Regex.Split(accumulated.Trim(), "\\s+")
            .Where(part => part.Length > 0).ToArray();
        var right = System.Text.RegularExpressions.Regex.Split(segment.Trim(), "\\s+")
            .Where(part => part.Length > 0).ToArray();
        if (left.Length == 0) return string.Join(" ", right);
        if (right.Length == 0) return string.Join(" ", left);

        var maxOverlap = Math.Min(12, Math.Min(left.Length, right.Length));
        for (var overlap = maxOverlap; overlap >= 1; overlap--)
        {
            var same = true;
            for (var i = 0; i < overlap; i++)
            {
                var oldToken = Normalize(left[left.Length - overlap + i]);
                var newToken = Normalize(right[i]);
                if (oldToken.Length == 0 || !string.Equals(oldToken, newToken, StringComparison.Ordinal))
                {
                    same = false;
                    break;
                }
            }
            if (same)
                return string.Join(" ", left.Concat(right.Skip(overlap)));
        }

        return string.Join(" ", left) + " " + string.Join(" ", right);
    }

    private async Task<string?> TranscribeAsync(string audioPath, bool deleteSourceAudio = true, bool isImportedFile = false,
        CancellationToken cancellationToken = default, bool suppressUserErrors = false)
    {
        var operationWatch = Stopwatch.StartNew();
        var wasDirectTyping = _directTypingSession;
        var operationToken = wasDirectTyping ? (_directTypingCancellation?.Token ?? cancellationToken) : cancellationToken;
        var model = ActiveTranscriptionModel;
        if (model is null || !model.IsSupportedForFinal || !HasUsableCurrentModelSelection())
        {
            _isTranscribing = false;
            if (wasDirectTyping) FinishDirectTypingSession();
            if (deleteSourceAudio) TryDelete(audioPath);
            RefreshControls();
            return null;
        }

        var languageSetting = _languageCombo.SelectedIndex switch { 1 => "fa", 2 => "en", _ => "auto" };
        var runtimeStatus = model.IsWhisper
            ? (FindWhisperCli() is null ? "whisper-cli runtime missing" : "whisper-cli found")
            : model.IsFasterWhisper
                ? (Directory.Exists(Path.Combine(RuntimeDirectory, "faster-whisper")) ? "faster-whisper runtime folder present; adapter checks readiness at use" : "faster-whisper runtime setup may be needed")
                : model.UsesVoskRuntime ? "Vosk .NET runtime path" : "Runtime unsupported";
        var autoStrategy = AutoLanguageText.NormalizeStrategy(_settings.AutoLanguageStrategy);
        var dualModelAutoRequested = string.Equals(languageSetting, "auto", StringComparison.OrdinalIgnoreCase) &&
            autoStrategy == AutoLanguageText.DualModelMixed;
        DiagnosticsState.SetTranscriptionContext(
            dualModelAutoRequested ? "Dual-model mixed Auto" : model.Provider,
            dualModelAutoRequested
                ? $"fa:{DualModelSelectionPolicy.LegacyOrSavedKey(_settings.AutoMixedPersianModelKey, _settings.AutoMixedPersianModelId)} + en:{DualModelSelectionPolicy.LegacyOrSavedKey(_settings.AutoMixedEnglishModelKey, _settings.AutoMixedEnglishModelId)}"
                : model.Id,
            dualModelAutoRequested ? "Per-role model paths are resolved locally from Settings" : ModelPath(model),
            dualModelAutoRequested ? "Two local, role-compatible models; short-segment comparison" : runtimeStatus,
            languageSetting, autoStrategy);
        var outputBase = Path.Combine(TempDirectory, $"transcript-{Guid.NewGuid():N}");
        var outputTxt = outputBase + ".txt";
        var processedAudioPath = Path.Combine(TempDirectory, $"audio-ready-{Guid.NewGuid():N}.wav");
        var fallbackOutputBase = outputBase + "-fa";
        var fallbackOutputTxt = fallbackOutputBase + ".txt";
        try
        {
            DiagnosticsState.SetStage("Preparing audio locally");
            operationToken.ThrowIfCancellationRequested();
            _statusLabel.Text = isImportedFile ? "Preparing audio file…" : "Preparing microphone audio…";
            _progressLabel.Text = isImportedFile
                ? "Converting the selected local file to the speech engine format. The source file will be kept."
                : "Applying microphone gain and audio cleanup locally…";
            SetProgressMode(ProgressBarStyle.Marquee, 0);
            await Task.Run(() => AudioProcessor.PrepareForWhisper(
                audioPath,
                processedAudioPath,
                isImportedFile ? 0 : _settings.MicrophoneBoostDb,
                isImportedFile ? false : _settings.AutoLevelQuietSpeech,
                isImportedFile ? false : _settings.ReduceLowFrequencyRumble,
                operationToken), operationToken);
            operationToken.ThrowIfCancellationRequested();

            DiagnosticsState.SetStage("Transcribing locally");
            _statusLabel.Text = "Transcribing on this PC…";
            _progressLabel.Text = string.Equals(languageSetting, "auto", StringComparison.OrdinalIgnoreCase) &&
                AutoLanguageText.NormalizeStrategy(_settings.AutoLanguageStrategy) == AutoLanguageText.DualModelMixed
                ? $"Using dual-model Auto: fa/{DualModelSelectionPolicy.LegacyOrSavedKey(_settings.AutoMixedPersianModelKey, _settings.AutoMixedPersianModelId)} + en/{DualModelSelectionPolicy.LegacyOrSavedKey(_settings.AutoMixedEnglishModelKey, _settings.AutoMixedEnglishModelId)}. Audio stays local."
                : $"Using {model.Provider} / {model.Id}. Audio is not uploaded.";

            string transcript;
            var isAutoLanguage = _languageCombo.SelectedIndex == 0;
            var language = _languageCombo.SelectedIndex switch { 1 => "fa", 2 => "en", _ => null };
            var strategyForThisTranscription = AutoLanguageText.NormalizeStrategy(_settings.AutoLanguageStrategy);
            var useDualModelMixedAuto = isAutoLanguage && strategyForThisTranscription == AutoLanguageText.DualModelMixed;
            if (!useDualModelMixedAuto && isAutoLanguage && model.UsesVoskRuntime)
                throw new InvalidOperationException($"Auto language detection cannot switch languages with the selected {model.Provider} model ({model.Id}). This model is language-specific. Choose Persian/English explicitly or select a multilingual Whisper/faster-whisper model.");
            if (!useDualModelMixedAuto && isAutoLanguage && model.IsWhisper && model.WhisperModel!.EnglishOnly)
                throw new InvalidOperationException($"The selected Whisper model ({model.Id}) is English-only and cannot be used with Spoken language = Auto. Select English explicitly or choose a multilingual model.");

            var runtimeProgress = new Progress<string>(message =>
            {
                if (!_isClosing && !IsDisposed) _progressLabel.Text = message;
            });
            if (useDualModelMixedAuto)
            {
                var persianChoice = ResolveDualModelChoice(
                    DualModelSelectionPolicy.LegacyOrSavedKey(_settings.AutoMixedPersianModelKey, _settings.AutoMixedPersianModelId),
                    _settings.AutoMixedPersianModelId, "Persian");
                var englishChoice = ResolveDualModelChoice(
                    DualModelSelectionPolicy.LegacyOrSavedKey(_settings.AutoMixedEnglishModelKey, _settings.AutoMixedEnglishModelId),
                    _settings.AutoMixedEnglishModelId, "English");
                if (string.Equals(persianChoice.Key, englishChoice.Key, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Dual-model Auto requires two different models. Choose a Persian-role model and a different English-role model in Settings → Transcription.");

                if (persianChoice.IsFasterWhisper && englishChoice.IsFasterWhisper)
                {
                    // The Python adapter keeps both CTranslate2 models loaded once for the whole job.
                    transcript = await FasterWhisperRuntime.TranscribeAsync(
                        _root, ModelPath(persianChoice), processedAudioPath, "auto",
                        strategyForThisTranscription, runtimeProgress, operationToken,
                        englishModelDirectory: ModelPath(englishChoice),
                        preserveEnglishTechnicalTerms: _settings.PreserveEnglishTechnicalTermsInAuto,
                        transcriptionLanguagePreference: _settings.TranscriptionLanguagePreference);
                }
                else
                {
                    transcript = await TranscribeDualModelsAcrossProvidersAsync(
                        persianChoice, englishChoice, processedAudioPath, runtimeProgress, operationToken);
                }
            }
            else if (model.IsWhisper)
            {
                var whisperModel = model.WhisperModel!;
                var cli = FindWhisperCli();
                if (cli is null) throw new FileNotFoundException("whisper-cli.exe was not found in the local runtime folder.");
                transcript = isAutoLanguage && !whisperModel.EnglishOnly
                    ? await TranscribeWhisperAutoAsync(cli, whisperModel, processedAudioPath, outputBase, fallbackOutputBase, operationToken)
                    : await RunWhisperAsync(cli, whisperModel, processedAudioPath, outputBase, language, operationToken);
            }
            else if (model.UsesVoskRuntime)
            {
                transcript = await Task.Run(() => RunVoskBatch(ModelPath(model), processedAudioPath, operationToken), operationToken);
            }
            else if (model.IsFasterWhisper)
            {
                transcript = await FasterWhisperRuntime.TranscribeAsync(
                    _root, ModelPath(model), processedAudioPath, language ?? "auto",
                    _settings.AutoLanguageStrategy, runtimeProgress, operationToken,
                    preserveEnglishTechnicalTerms: _settings.PreserveEnglishTechnicalTermsInAuto,
                    transcriptionLanguagePreference: _settings.TranscriptionLanguagePreference);
            }
            else
            {
                throw new NotSupportedException($"The {model.Provider} runtime is not available in this version.");
            }

            if (wasDirectTyping && _directTypingCancelled)
            {
                _statusLabel.Text = "Voice typing cancelled.";
                _progressLabel.Text = "Local transcription may have finished, but its result was discarded and was not inserted.";
                ShowHotkeyNotification("Voice typing cancelled", "The result was discarded and was not inserted. The local engine may have finished its current operation.");
                return null;
            }

            transcript = transcript.Trim();
            if (isAutoLanguage && _settings.PreserveEnglishTechnicalTermsInAuto)
                transcript = AutoLanguageText.RestoreEnglishTechnicalTerms(transcript);
            if (string.IsNullOrWhiteSpace(transcript))
            {
                DiagnosticsState.SetStage("No speech detected");
                _statusLabel.Text = "No speech detected.";
                _progressLabel.Text = isImportedFile
                    ? "No speech was recognized in this file. Check that it contains audible speech and is not damaged."
                    : "Try increasing Microphone Boost or choosing a different microphone.";
                if (wasDirectTyping)
                    ShowHotkeyNotification("No speech detected", "Nothing was inserted. Check the microphone and try again.");
                return null;
            }
            else
            {
                if (isImportedFile && File.Exists(audioPath))
                {
                    _lastImportedAudioPath = audioPath;
                    _lastImportedAudioModelKey = model.Key;
                    _lastImportedAudioLanguage = languageSetting;
                    _lastImportedAudioDetectedLanguage = model.IsWhisper &&
                        AutoLanguageText.NormalizeStrategy(_settings.AutoLanguageStrategy) is not (AutoLanguageText.MixedSegments or AutoLanguageText.DualModelMixed)
                            ? _lastWhisperDetectedLanguage : null;
                }
                transcript = SetFinalTranscriptText(transcript, wasDirectTyping);
                DiagnosticsState.SetStage("Transcription completed");
                _statusLabel.Text = "Transcription complete.";
                var preferenceSummary = AutoLanguageText.IsPersianFirstPreference(_settings.TranscriptionLanguagePreference)
                    ? "فارسی‌اول" : "English-first";
                _progressLabel.Text = string.Equals(languageSetting, "auto", StringComparison.OrdinalIgnoreCase) &&
                    AutoLanguageText.NormalizeStrategy(_settings.AutoLanguageStrategy) == AutoLanguageText.DualModelMixed
                    ? $"Done · dual Auto ({_settings.AutoMixedPersianModelId} + {_settings.AutoMixedEnglishModelId}) · {preferenceSummary}"
                    : $"Done · {model.Provider}/{model.Id} · {languageSetting} · {preferenceSummary}";
                if (_settings.TranscriptHistoryEnabled)
                {
                    try
                    {
                        TranscriptHistoryStore.Add(_root, transcript,
                            _languageCombo.SelectedIndex switch { 1 => "fa", 2 => "en", _ => "auto" },
                            dualModelAutoRequested
                                ? $"faster-whisper dual-model Auto / fa:{_settings.AutoMixedPersianModelId} + en:{_settings.AutoMixedEnglishModelId}"
                                : $"{model.Provider} / {model.Id}");
                    }
                    catch (Exception historyError)
                    {
                        _progressLabel.Text = $"Transcript is ready, but history was not saved: {historyError.Message}";
                    }
                }

                if (wasDirectTyping)
                {
                    if (_directTypingCancelled)
                    {
                        ShowHotkeyNotification("Voice typing cancelled", "The transcript was kept in MadNegar and was not inserted.");
                    }
                    else if (!_settings.DirectTypingAutoInsert)
                    {
                        var copied = await CopyFinalTranscriptToClipboardAsync(transcript);
                        _pendingDirectTypingTarget = null;
                        _pendingDirectTypingText = null;
                        if (copied.Success)
                            ShowHotkeyNotification("Copied to clipboard", "The final transcript is in the clipboard and remains in MadNegar. Nothing was inserted into any application.");
                        else
                            ShowHotkeyNotification("Copy failed", copied.Message + " The transcript remains in MadNegar; use Copy Text to try again.");
                    }
                    else if (_directTypingTarget is { } target)
                    {
                        _statusLabel.Text = "Inserting transcript…";
                        _progressLabel.Text = "Rechecking the original destination before Paste…";
                        ShowHotkeyNotification("Inserting", "Sending the final transcript to the original field. A selected range is replaced by normal Paste; Enter is never pressed.");
                        var insertion = await GlobalHotkeySupport.TryPasteTextPreservingClipboardAsync(
                            transcript, target, _directTypingCancellation?.Token ?? CancellationToken.None);
                        if (_directTypingCancelled)
                        {
                            _pendingDirectTypingTarget = null;
                            _pendingDirectTypingText = null;
                            ShowHotkeyNotification("Voice typing cancelled", insertion.Message + " Check the destination if the paste had already begun.");
                        }
                        else if (insertion.Success)
                        {
                            _pendingDirectTypingTarget = null;
                            _pendingDirectTypingText = null;
                            ShowHotkeyNotification("Voice typing complete", insertion.Message + " MadNegar will not press Enter or confirm a rename.");
                        }
                        else
                        {
                            if (insertion.RetrySafe)
                            {
                                _pendingDirectTypingTarget = target;
                                _pendingDirectTypingText = transcript;
                            }
                            else
                            {
                                _pendingDirectTypingTarget = null;
                                _pendingDirectTypingText = null;
                            }
                            ShowHotkeyNotification("Insert failed", insertion.Message + (insertion.RetrySafe
                                ? " The transcript is safe to retry with Retry Insert, or copy it manually."
                                : " Automatic retry is disabled because part of Paste may have reached the destination; review the target before trying again."));
                        }
                    }
                    else
                    {
                        ShowHotkeyNotification("Insert failed", "No original destination was captured. The transcript remains in MadNegar; use Copy Text.");
                    }
                }
            }
            return transcript;
        }
        catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
        {
            DiagnosticsState.SetStage("Transcription cancelled");
            _statusLabel.Text = isImportedFile ? "File transcription cancelled." : "Transcription cancelled.";
            _progressLabel.Text = isImportedFile
                ? "The original audio file was kept. No new transcript was exported."
                : "The current result was cancelled.";
            if (isImportedFile) ClearImportedAudioSubtitleContext();
            if (wasDirectTyping) ShowHotkeyNotification("Voice typing cancelled", "The operation was cancelled. The destination was not updated with a late result.");
            return null;
        }
        catch (Exception ex)
        {
            if (isImportedFile) ClearImportedAudioSubtitleContext();
            DiagnosticsState.RecordError("Final transcription", ex.Message);
            if (wasDirectTyping && _directTypingCancelled)
            {
                _statusLabel.Text = "Voice typing cancelled.";
                _progressLabel.Text = "The result was discarded; nothing new was inserted.";
                ShowHotkeyNotification("Voice typing cancelled", "The current engine operation ended without inserting its result.");
            }
            else
            {
                _statusLabel.Text = "Transcription failed.";
                _progressLabel.Text = "Please try again.";
                if (wasDirectTyping) ShowHotkeyNotification("Transcription failed", ex.Message + " The recording was not inserted into another app.");
                else if (suppressUserErrors)
                    _progressLabel.Text = ex.Message;
                else MessageBox.Show(this,
                    (isImportedFile ? "MadNegar could not transcribe the selected audio file." : "MadNegar could not transcribe this recording.") + "\n\n" + ex.Message,
                    isImportedFile ? "File transcription error" : "Transcription error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return null;
        }
        finally
        {
            operationWatch.Stop();
            DiagnosticsState.RecordOperationDuration(operationWatch.Elapsed);
            if (wasDirectTyping) FinishDirectTypingSession();
            if (deleteSourceAudio) TryDelete(audioPath);
            TryDelete(processedAudioPath);
            TryDelete(outputTxt);
            TryDelete(fallbackOutputTxt);
            TryDelete(outputBase + ".json");
            TryDelete(fallbackOutputBase + ".json");
            _recordingPath = null;
            _isTranscribing = false;
            SetProgressMode(ProgressBarStyle.Blocks, 0);
            RefreshControls();
        }
    }

    private async Task ChooseAudioFileAsync()
    {
        if (_isBusy || _isRecording || _isStoppingRecording || _isTranscribing || _directTypingSession) return;
        var model = SelectedModel;
        if (model is null || !model.IsSupportedForFinal || !IsModelDownloaded(model))
        {
            MessageBox.Show(this, "Download and select a supported speech model in Settings → Downloads before transcribing a file.",
                "Speech model required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Choose an audio file to transcribe locally",
            Filter = "Supported audio (*.wav;*.mp3;*.aif;*.aiff)|*.wav;*.mp3;*.aif;*.aiff|WAV files (*.wav)|*.wav|MP3 files (*.mp3)|*.mp3|AIFF files (*.aif;*.aiff)|*.aif;*.aiff|All files (*.*)|*.*",
            FilterIndex = 1,
            CheckFileExists = true,
            Multiselect = false,
            DereferenceLinks = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!SubtitleExportSupport.IsSupportedAudioPath(dialog.FileName))
        {
            MessageBox.Show(this, "This first file-transcription version supports WAV, MP3 and AIFF files. The source file has not been changed.",
                "Unsupported audio format", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _fileTranscriptionCts?.Dispose();
        _fileTranscriptionCts = new CancellationTokenSource();
        _isFileTranscribing = true;
        _isTranscribing = true;
        ClearImportedAudioSubtitleContext();
        _statusLabel.Text = "Transcribing selected audio file…";
        _progressLabel.Text = "Local processing only. The original audio file will be kept.";
        SetProgressMode(ProgressBarStyle.Marquee, 0);
        RefreshControls();
        try
        {
            await TranscribeAsync(dialog.FileName, deleteSourceAudio: false, isImportedFile: true, cancellationToken: _fileTranscriptionCts.Token);
        }
        finally
        {
            _isFileTranscribing = false;
            _isTranscribing = false;
            _fileTranscriptionCts?.Dispose();
            _fileTranscriptionCts = null;
            RefreshControls();
        }
    }

    private void OpenBatchTranscription()
    {
        if (_isBusy || _isRecording || _isStoppingRecording || _isTranscribing || _directTypingSession) return;
        if (_pendingDirectTypingTarget is not null)
        {
            MessageBox.Show(this, "A previous direct-typing insertion is still awaiting review. Retry it or copy the text before starting a batch.",
                "Pending voice typing", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var model = SelectedModel;
        if (model is null || !model.IsSupportedForFinal || !IsModelDownloaded(model))
        {
            MessageBox.Show(this, "Download and select a supported speech model in Settings → Downloads before batch transcription.",
                "Speech model required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new BatchAudioTranscriptionForm(
            _settings.ThemeMode,
            model.IsWhisper,
            ProcessBatchAudioAsync);
        dialog.ShowDialog(this);
    }

    private async Task<IReadOnlyList<BatchAudioFileResult>> ProcessBatchAudioAsync(
        IReadOnlyList<string> inputPaths,
        BatchAudioOutputOptions options,
        CancellationToken cancellationToken,
        IProgress<BatchAudioProgress> progress)
    {
        var results = new List<BatchAudioFileResult>(inputPaths.Count);
        var currentIndex = -1;
        string? currentPath = null;
        var currentOutputNotes = new List<string>();
        string? currentOutputStem = null;
        var currentCompletedFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var originalTranscript = _transcriptBox.Text;
        var originalRaw = _rawTranscriptBeforeNormalization;
        var originalNormalized = _normalizedTranscriptForUndo;
        var selectedModel = SelectedModel;
        var outputExtensions = BatchTranscriptionSupport.GetSelectedExtensions(options);
        if (selectedModel is null || !selectedModel.IsSupportedForFinal || !IsModelDownloaded(selectedModel))
            throw new InvalidOperationException("The selected final-transcription model is no longer ready. Re-select a downloaded model and try again.");
        if ((options.ExportSrt || options.ExportVtt) && !selectedModel.IsWhisper)
            throw new InvalidOperationException("SRT/VTT require a supported Whisper.cpp final model with timestamped output.");

        _fileTranscriptionCts?.Dispose();
        _fileTranscriptionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var batchToken = _fileTranscriptionCts.Token;
        _isFileTranscribing = true;
        try
        {
            Directory.CreateDirectory(options.OutputDirectory);
            for (var index = 0; index < inputPaths.Count; index++)
            {
                batchToken.ThrowIfCancellationRequested();
                currentIndex = index;
                currentPath = Path.GetFullPath(inputPaths[index]);
                currentOutputNotes = new List<string>();
                currentOutputStem = null;
                currentCompletedFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var inputPath = currentPath;
                if (!BatchTranscriptionSupport.IsSupportedAudioPath(inputPath) || !File.Exists(inputPath))
                {
                    var invalid = new BatchAudioFileResult(inputPath, "Failed", "The input file is missing or has an unsupported extension.");
                    results.Add(invalid);
                    progress.Report(new BatchAudioProgress(index, inputPaths.Count, inputPath, invalid.Status, invalid.Details));
                    continue;
                }

                if (options.PriorOutputStates is not null && options.PriorOutputStates.TryGetValue(inputPath, out var priorState) &&
                    string.Equals(Path.GetFullPath(priorState.OutputDirectory), Path.GetFullPath(options.OutputDirectory), StringComparison.OrdinalIgnoreCase))
                {
                    currentOutputStem = priorState.OutputStem;
                    currentCompletedFormats = new HashSet<string>(priorState.CompletedFormats, StringComparer.OrdinalIgnoreCase);
                    foreach (var completedFormat in currentCompletedFormats.ToArray())
                    {
                        if (!File.Exists(Path.Combine(options.OutputDirectory, currentOutputStem + completedFormat)))
                            currentCompletedFormats.Remove(completedFormat);
                    }
                }

                // A retry of a partial file only produces missing outputs. Do not retranscribe
                // or create a suffixed duplicate when the currently selected formats are already present.
                var priorStemForSkip = currentOutputStem;
                if (priorStemForSkip is not null && outputExtensions.Count > 0 && outputExtensions.All(extension =>
                    currentCompletedFormats.Contains(extension) && File.Exists(Path.Combine(options.OutputDirectory, priorStemForSkip + extension))))
                {
                    var alreadyDone = new BatchAudioFileResult(inputPath, "Success", "Selected output formats already exist; skipped to avoid duplicates.",
                        currentOutputStem, new HashSet<string>(currentCompletedFormats, StringComparer.OrdinalIgnoreCase));
                    results.Add(alreadyDone);
                    progress.Report(new BatchAudioProgress(index, inputPaths.Count, inputPath, alreadyDone.Status, alreadyDone.Details));
                    continue;
                }

                progress.Report(new BatchAudioProgress(index, inputPaths.Count, inputPath, "Processing", "Transcribing locally; source file is kept."));
                ClearImportedAudioSubtitleContext();
                _isTranscribing = true;
                var transcript = await TranscribeAsync(inputPath, deleteSourceAudio: false, isImportedFile: true,
                    cancellationToken: batchToken, suppressUserErrors: true);
                batchToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(transcript))
                {
                    var detail = _statusLabel.Text.Equals("No speech detected.", StringComparison.OrdinalIgnoreCase)
                        ? _progressLabel.Text
                        : string.IsNullOrWhiteSpace(_progressLabel.Text) ? "Transcription failed. See Diagnostics for the latest engine error." : _progressLabel.Text;
                    var failedStatus = _statusLabel.Text.Equals("No speech detected.", StringComparison.OrdinalIgnoreCase) ? "No speech" : "Failed";
                    var failed = new BatchAudioFileResult(inputPath, failedStatus, detail, currentOutputStem,
                        new HashSet<string>(currentCompletedFormats, StringComparer.OrdinalIgnoreCase));
                    results.Add(failed);
                    progress.Report(new BatchAudioProgress(index, inputPaths.Count, inputPath, failed.Status, failed.Details));
                    continue;
                }

                if (currentOutputStem is null)
                {
                    try { currentOutputStem = BatchTranscriptionSupport.GetUniqueOutputStem(options.OutputDirectory, inputPath, outputExtensions); }
                    catch (Exception ex)
                    {
                        var allocationFailure = new BatchAudioFileResult(inputPath, "Failed", "Could not allocate safe output names: " + ex.Message);
                        results.Add(allocationFailure);
                        progress.Report(new BatchAudioProgress(index, inputPaths.Count, inputPath, allocationFailure.Status, allocationFailure.Details));
                        continue;
                    }
                }
                var stem = currentOutputStem ?? throw new InvalidOperationException("Could not allocate an output name for this file.");
                var outputNotes = currentOutputNotes;
                var successes = 0;
                var failures = 0;
                if (options.ExportTxt)
                {
                    var txtPath = Path.Combine(options.OutputDirectory, stem + ".txt");
                    if (currentCompletedFormats.Contains(".txt") && File.Exists(txtPath))
                    {
                        outputNotes.Add("TXT already saved; skipped");
                        successes++;
                    }
                    else
                    {
                        try
                        {
                            await BatchTranscriptionSupport.WriteTextAtomicallyAsync(txtPath, transcript, batchToken);
                            currentCompletedFormats.Add(".txt");
                            outputNotes.Add("TXT saved");
                            successes++;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { outputNotes.Add("TXT failed: " + ex.Message); failures++; }
                    }
                }

                var subtitleLanguage = _languageCombo.SelectedIndex switch { 1 => "fa", 2 => "en", _ => "auto" };
                var detectedLanguage = _lastImportedAudioDetectedLanguage;
                foreach (var format in new[] { "srt", "vtt" })
                {
                    if (format == "srt" && !options.ExportSrt || format == "vtt" && !options.ExportVtt) continue;
                    var extension = "." + format;
                    var subtitlePath = Path.Combine(options.OutputDirectory, stem + extension);
                    if (currentCompletedFormats.Contains(extension) && File.Exists(subtitlePath))
                    {
                        outputNotes.Add(format.ToUpperInvariant() + " already saved; skipped");
                        successes++;
                        continue;
                    }
                    try
                    {
                        await GenerateBatchSubtitleAsync(inputPath, subtitlePath, format, selectedModel,
                            subtitleLanguage, detectedLanguage, batchToken);
                        currentCompletedFormats.Add(extension);
                        outputNotes.Add(format.ToUpperInvariant() + " saved");
                        successes++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { outputNotes.Add(format.ToUpperInvariant() + " failed: " + ex.Message); failures++; }
                }

                var status = failures == 0 ? "Success" : successes > 0 ? "Partial" : "Failed";
                var result = new BatchAudioFileResult(inputPath, status, string.Join(" · ", outputNotes), stem,
                    new HashSet<string>(currentCompletedFormats, StringComparer.OrdinalIgnoreCase));
                results.Add(result);
                progress.Report(new BatchAudioProgress(index, inputPaths.Count, inputPath, result.Status, result.Details));
            }
        }
        catch (OperationCanceledException)
        {
            var nextIndex = results.Count;
            if (currentPath is not null && currentIndex >= results.Count)
            {
                var detail = currentOutputNotes.Count == 0
                    ? "Cancelled before any output was completed for this file."
                    : "Cancelled during this file; completed outputs may already exist: " + string.Join(" · ", currentOutputNotes);
                var cancelledCurrent = new BatchAudioFileResult(currentPath, "Cancelled", detail, currentOutputStem,
                    new HashSet<string>(currentCompletedFormats, StringComparer.OrdinalIgnoreCase));
                results.Add(cancelledCurrent);
                progress.Report(new BatchAudioProgress(currentIndex, inputPaths.Count, currentPath, cancelledCurrent.Status, cancelledCurrent.Details));
                nextIndex = currentIndex + 1;
            }
            for (var index = nextIndex; index < inputPaths.Count; index++)
            {
                var path = Path.GetFullPath(inputPaths[index]);
                var cancelled = new BatchAudioFileResult(path, "Cancelled", "Not processed because batch cancellation was requested.");
                results.Add(cancelled);
                progress.Report(new BatchAudioProgress(index, inputPaths.Count, path, cancelled.Status, cancelled.Details));
            }
        }
        finally
        {
            _isFileTranscribing = false;
            _isTranscribing = false;
            _fileTranscriptionCts?.Dispose();
            _fileTranscriptionCts = null;
            ClearImportedAudioSubtitleContext();
            _settingTranscriptProgrammatically = true;
            try { _transcriptBox.Text = originalTranscript; }
            finally { _settingTranscriptProgrammatically = false; }
            _rawTranscriptBeforeNormalization = originalRaw;
            _normalizedTranscriptForUndo = originalNormalized;
            _undoTextFixButton.Enabled = originalRaw is not null && string.Equals(originalTranscript, originalNormalized, StringComparison.Ordinal);
            _statusLabel.Text = "Batch processing finished.";
            _progressLabel.Text = "Review per-file results in the batch window. Existing outputs are never overwritten.";
            SetProgressMode(ProgressBarStyle.Blocks, 0);
            RefreshControls();
        }
        return results;
    }

    private async Task<string> GenerateBatchSubtitleAsync(
        string sourcePath,
        string destinationPath,
        string format,
        SpeechModelChoice selectedModel,
        string languageSetting,
        string? detectedLanguage,
        CancellationToken cancellationToken)
    {
        if (!selectedModel.IsWhisper || selectedModel.WhisperModel is not { } whisperModel)
            throw new NotSupportedException("Timestamped subtitles require the Whisper.cpp final-transcription model.");
        if (!SubtitleExportSupport.IsSupportedSubtitleFormat(format))
            throw new ArgumentException("Choose SRT or VTT.", nameof(format));
        var cli = FindWhisperCli() ?? throw new FileNotFoundException("The local whisper-cli runtime was not found. No subtitle file was written.");
        var outputBase = Path.Combine(TempDirectory, $"batch-subtitle-{Guid.NewGuid():N}");
        var processedAudioPath = Path.Combine(TempDirectory, $"batch-subtitle-audio-{Guid.NewGuid():N}.wav");
        var temporaryDestination = destinationPath + $".{Guid.NewGuid():N}.part";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => AudioProcessor.PrepareForWhisper(sourcePath, processedAudioPath, 0, false, false, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var language = languageSetting switch
            {
                "fa" => "fa",
                "en" => "en",
                _ => detectedLanguage is "fa" or "en" ? detectedLanguage : null
            };
            await RunWhisperAsync(cli, whisperModel, processedAudioPath, outputBase, language,
                cancellationToken: cancellationToken, includeLanguageMetadata: false, subtitleFormat: format);
            var generatedPath = outputBase + "." + format;
            if (!File.Exists(generatedPath))
                throw new InvalidDataException($"Whisper.cpp did not create {format.ToUpperInvariant()}; verify the installed runtime supports -o{format}.");
            var content = await File.ReadAllTextAsync(generatedPath, cancellationToken);
            if (!SubtitleExportSupport.IsValidSubtitle(format, content, out var reason))
                throw new InvalidDataException("Whisper.cpp output failed subtitle validation: " + reason);
            var processed = SubtitleExportSupport.ApplyCueTextProcessing(content, format,
                _settings.PersianTextNormalizeYeh, _settings.PersianTextNormalizeKaf,
                _settings.PersianTextNormalizeHalfSpaces, _settings.PersianTextNormalizePunctuation,
                _settings.LocalDictionaryEntries, languageSetting);
            if (!SubtitleExportSupport.IsValidSubtitle(format, processed, out reason))
                throw new InvalidDataException("Text cleanup produced invalid subtitle structure: " + reason);
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(temporaryDestination, processed,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryDestination, destinationPath, overwrite: false);
            return $"Saved validated {format.ToUpperInvariant()}";
        }
        finally
        {
            TryDelete(temporaryDestination);
            TryDelete(processedAudioPath);
            TryDelete(outputBase + ".txt");
            TryDelete(outputBase + ".json");
            TryDelete(outputBase + ".srt");
            TryDelete(outputBase + ".vtt");
        }
    }

    private void CancelFileTranscription()
    {
        if (!_isFileTranscribing) return;
        try { _fileTranscriptionCts?.Cancel(); } catch { }
        _statusLabel.Text = "Cancelling file transcription…";
        _progressLabel.Text = "Stopping the local engine and keeping the original audio file.";
        _cancelDirectTypingButton.Enabled = false;
    }

    private void ClearImportedAudioSubtitleContext()
    {
        _lastImportedAudioPath = null;
        _lastImportedAudioModelKey = null;
        _lastImportedAudioLanguage = null;
        _lastImportedAudioDetectedLanguage = null;
    }

    private bool CanExportImportedSubtitles()
    {
        var model = SelectedModel;
        return !IsDualModelAutoActive && _lastImportedAudioPath is { } path && File.Exists(path) &&
            model is { IsWhisper: true } && string.Equals(model.Key, _lastImportedAudioModelKey, StringComparison.Ordinal);
    }

    private async Task ExportImportedSubtitlesAsync(string destinationPath, string format)
    {
        if (!CanExportImportedSubtitles() || _lastImportedAudioPath is not { } sourcePath || SelectedModel?.WhisperModel is not { } model)
        {
            MessageBox.Show(this, "Timestamped SRT/VTT export is available for an imported audio file transcribed with the same selected Whisper.cpp model. Choose the same model and import the source audio again if needed.",
                "Subtitle export unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!SubtitleExportSupport.IsSupportedSubtitleFormat(format)) return;

        var cli = FindWhisperCli();
        if (cli is null)
        {
            MessageBox.Show(this, "The local whisper-cli runtime was not found. No subtitle file was written.",
                "Whisper runtime missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var outputBase = Path.Combine(TempDirectory, $"subtitle-{Guid.NewGuid():N}");
        var processedAudioPath = Path.Combine(TempDirectory, $"subtitle-audio-{Guid.NewGuid():N}.wav");
        var temporaryDestinationPath = destinationPath + $".{Guid.NewGuid():N}.part";
        _fileTranscriptionCts?.Dispose();
        _fileTranscriptionCts = new CancellationTokenSource();
        var subtitleToken = _fileTranscriptionCts.Token;
        _isFileTranscribing = true;
        _isTranscribing = true;
        _statusLabel.Text = "Generating timestamped subtitles…";
        _progressLabel.Text = "Running Whisper.cpp locally for real timestamps. Persian cleanup and dictionary rules apply to cue text; manual transcript-box edits are not mapped to timestamps.";
        SetProgressMode(ProgressBarStyle.Marquee, 0);
        RefreshControls();
        try
        {
            await Task.Run(() => AudioProcessor.PrepareForWhisper(sourcePath, processedAudioPath, 0, false, false, subtitleToken), subtitleToken);
            subtitleToken.ThrowIfCancellationRequested();
            var language = _lastImportedAudioLanguage switch
            {
                "fa" => "fa",
                "en" => "en",
                _ => _lastImportedAudioDetectedLanguage is "fa" or "en" ? _lastImportedAudioDetectedLanguage : null
            };
            await RunWhisperAsync(cli, model, processedAudioPath, outputBase, language,
                cancellationToken: subtitleToken, includeLanguageMetadata: false, subtitleFormat: format);
            var generatedPath = outputBase + "." + format;
            if (!File.Exists(generatedPath))
                throw new InvalidDataException($"Whisper.cpp completed but did not create a {format.ToUpperInvariant()} subtitle file. Check that the installed whisper-cli build supports -o{format}.");
            var generatedContent = await File.ReadAllTextAsync(generatedPath, subtitleToken);
            if (!SubtitleExportSupport.IsValidSubtitle(format, generatedContent, out var reason))
                throw new InvalidDataException("The speech engine did not produce a valid timestamped subtitle file. " + reason);
            var subtitleLanguage = _lastImportedAudioLanguage switch { "fa" => "fa", "en" => "en", _ => "auto" };
            generatedContent = SubtitleExportSupport.ApplyCueTextProcessing(
                generatedContent, format,
                _settings.PersianTextNormalizeYeh,
                _settings.PersianTextNormalizeKaf,
                _settings.PersianTextNormalizeHalfSpaces,
                _settings.PersianTextNormalizePunctuation,
                _settings.LocalDictionaryEntries,
                subtitleLanguage);
            if (!SubtitleExportSupport.IsValidSubtitle(format, generatedContent, out reason))
                throw new InvalidDataException("Subtitle text processing produced an invalid cue structure. " + reason);
            subtitleToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(temporaryDestinationPath, generatedContent,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), subtitleToken);
            subtitleToken.ThrowIfCancellationRequested();
            // Publish only a complete, validated file. A cancellation must not truncate an existing destination.
            File.Move(temporaryDestinationPath, destinationPath, overwrite: true);
            _statusLabel.Text = "Subtitle export complete.";
            _progressLabel.Text = $"Saved timestamped {format.ToUpperInvariant()} subtitles to {Path.GetFileName(destinationPath)}.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Subtitle export cancelled.";
            _progressLabel.Text = "The source audio and current transcript were kept.";
        }
        catch (Exception ex)
        {
            DiagnosticsState.RecordError("Subtitle export", ex.Message);
            _statusLabel.Text = "Subtitle export failed.";
            _progressLabel.Text = "No unvalidated subtitle output was accepted.";
            MessageBox.Show(this, "MadNegar could not create a validated subtitle file. The audio remains local and unchanged.\n\n" + ex.Message,
                "Subtitle export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            TryDelete(temporaryDestinationPath);
            TryDelete(processedAudioPath);
            TryDelete(outputBase + ".txt");
            TryDelete(outputBase + ".json");
            TryDelete(outputBase + ".srt");
            TryDelete(outputBase + ".vtt");
            _isFileTranscribing = false;
            _isTranscribing = false;
            _fileTranscriptionCts?.Dispose();
            _fileTranscriptionCts = null;
            SetProgressMode(ProgressBarStyle.Blocks, 0);
            RefreshControls();
        }
    }

    private static string RunVoskBatch(string modelDirectory, string wavPath, CancellationToken cancellationToken = default)
    {
        Vosk.Vosk.SetLogLevel(-1);
        using var model = new Vosk.Model(modelDirectory);
        return RunVoskBatch(model, wavPath, cancellationToken);
    }

    private static string RunVoskBatch(Vosk.Model model, string wavPath, CancellationToken cancellationToken = default)
    {
        using var source = new WaveFileReader(wavPath);
        if (source.WaveFormat.SampleRate != 16000 || source.WaveFormat.Channels != 1 || source.WaveFormat.BitsPerSample != 16)
            throw new InvalidOperationException("The audio preparation stage did not produce the 16 kHz mono PCM audio required by Vosk.");

        using var recognizer = new VoskRecognizer(model, source.WaveFormat.SampleRate);
        recognizer.SetMaxAlternatives(0);
        var buffer = new byte[8192];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            recognizer.AcceptWaveform(buffer, read);
        }

        var resultJson = recognizer.FinalResult();
        using var document = JsonDocument.Parse(resultJson);
        return document.RootElement.TryGetProperty("text", out var text) ? text.GetString() ?? string.Empty : string.Empty;
    }

    private async Task<string> TranscribeWhisperAutoAsync(
        string cli, ModelInfo model, string audioPath, string outputBase, string fallbackOutputBase, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var strategy = AutoLanguageText.NormalizeStrategy(_settings.AutoLanguageStrategy);
        if (strategy == AutoLanguageText.MixedSegments)
        {
            var segmented = await TranscribeWhisperMixedSegmentsAsync(cli, model, audioPath, outputBase, cancellationToken);
            if (!string.IsNullOrWhiteSpace(segmented))
            {
                if (LooksLikeRomanizedPersian(segmented))
                {
                    _progressLabel.Text = "Checking Persian script after segment-level Auto…";
                    var persian = await RunWhisperAsync(cli, model, audioPath, fallbackOutputBase, "fa", cancellationToken);
                    if (AutoLanguageText.IsPersianCandidate(persian, segmented)) return persian;
                }
                return segmented;
            }
            _progressLabel.Text = "Segmented Auto produced no text; retrying the full recording…";
            return await RunWhisperAsync(cli, model, audioPath, outputBase, null, cancellationToken);
        }

        _progressLabel.Text = strategy switch
        {
            AutoLanguageText.PreferPersian => "Auto language: checking whether Persian-script recovery is needed…",
            AutoLanguageText.PreferEnglish => "Auto language: checking whether English-script recovery is needed…",
            _ => "Auto language: transcribing locally…"
        };
        var automatic = await RunWhisperAsync(cli, model, audioPath, outputBase, null, cancellationToken, includeLanguageMetadata: true);
        var detectedLanguage = _lastWhisperDetectedLanguage;

        if (AutoLanguageText.ShouldTryPersianPreferredPass(automatic, detectedLanguage))
        {
            _progressLabel.Text = "Auto: checking for Persian-script recovery…";
            var preferredPersian = await RunWhisperAsync(cli, model, audioPath, fallbackOutputBase, "fa", cancellationToken);
            if (AutoLanguageText.IsPersianCandidate(preferredPersian, automatic))
                return _settings.PreserveEnglishTechnicalTermsInAuto
                    ? AutoLanguageText.RestoreEnglishTechnicalTerms(preferredPersian)
                    : preferredPersian;
        }

        if (strategy == AutoLanguageText.PreferEnglish && AutoLanguageText.ShouldTryEnglishPreferredPass(automatic))
        {
            _progressLabel.Text = "Auto language: trying an English-preferred pass…";
            var englishCandidate = await RunWhisperAsync(cli, model, audioPath, fallbackOutputBase, "en", cancellationToken);
            if (AutoLanguageText.IsEnglishCandidate(englishCandidate, automatic)) return englishCandidate;
        }
        else if (strategy == AutoLanguageText.Balanced &&
            AutoLanguageText.ShouldRetryEnglishForScriptMismatch(automatic, detectedLanguage))
        {
            _progressLabel.Text = "Auto language: detected Persian but got Latin-only text; validating one English pass…";
            var englishCandidate = await RunWhisperAsync(cli, model, audioPath, fallbackOutputBase, "en", cancellationToken, includeLanguageMetadata: true);
            if (AutoLanguageText.IsEnglishCandidate(englishCandidate, automatic) &&
                string.Equals(_lastWhisperDetectedLanguage, "en", StringComparison.OrdinalIgnoreCase)) return englishCandidate;
        }
        else if (strategy == AutoLanguageText.Balanced &&
            AutoLanguageText.ShouldRetryPersianForScriptMismatch(automatic, detectedLanguage))
        {
            _progressLabel.Text = "Auto language: detected English but got Persian-script text; validating one Persian pass…";
            var persianCandidate = await RunWhisperAsync(cli, model, audioPath, fallbackOutputBase, "fa", cancellationToken, includeLanguageMetadata: true);
            if (AutoLanguageText.IsPersianCandidate(persianCandidate, automatic) &&
                string.Equals(_lastWhisperDetectedLanguage, "fa", StringComparison.OrdinalIgnoreCase)) return persianCandidate;
        }
        else if (strategy == AutoLanguageText.Balanced && LooksLikeRomanizedPersian(automatic))
        {
            _progressLabel.Text = "Auto language: checking Persian-script output…";
            var persianCandidate = await RunWhisperAsync(cli, model, audioPath, fallbackOutputBase, "fa", cancellationToken);
            if (AutoLanguageText.IsPersianCandidate(persianCandidate, automatic)) return persianCandidate;
        }

        return automatic;
    }

    private async Task<string> TranscribeWhisperMixedSegmentsAsync(
        string cli, ModelInfo model, string audioPath, string outputBase, CancellationToken cancellationToken = default)
    {
        var segmentDirectory = outputBase + "-segments";
        Directory.CreateDirectory(segmentDirectory);
        IReadOnlyList<AutoLanguageTranscription.WaveSegment> segments = Array.Empty<AutoLanguageTranscription.WaveSegment>();
        var transcripts = new List<string>();
        try
        {
            segments = AutoLanguageTranscription.SplitWaveFile(audioPath, segmentDirectory);
            if (segments.Count == 1 && !segments[0].IsTemporary)
                return await TranscribeWhisperAutoSegmentWithRecoveryAsync(cli, model, audioPath, outputBase, cancellationToken);

            for (var index = 0; index < segments.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _progressLabel.Text = $"Mixed-language Auto · segment {index + 1}/{segments.Count}…";
                var segmentOutputBase = Path.Combine(segmentDirectory, $"output-{index + 1:000}");
                var piece = await TranscribeWhisperAutoSegmentWithRecoveryAsync(
                    cli, model, segments[index].Path, segmentOutputBase, cancellationToken);
                transcripts.Add(piece);
            }

            return AutoLanguageText.MergeSegmentTranscripts(transcripts);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _progressLabel.Text = "Segmented Auto was unavailable; retrying single-pass Auto…";
            return await RunWhisperAsync(cli, model, audioPath, outputBase, null, cancellationToken);
        }
        finally
        {
            foreach (var segment in segments)
            {
                if (!segment.IsTemporary) continue;
                TryDelete(segment.Path);
            }
            try
            {
                if (Directory.Exists(segmentDirectory))
                {
                    foreach (var file in Directory.GetFiles(segmentDirectory)) TryDelete(file);
                    Directory.Delete(segmentDirectory, recursive: true);
                }
            }
            catch { }
        }
    }


    private async Task<string> TranscribeWhisperAutoSegmentWithRecoveryAsync(
        string cli, ModelInfo model, string audioPath, string outputBase, CancellationToken cancellationToken = default)
    {
        // Use the same bounded detector/script recovery for both short Mixed-mode clips and
        // longer individual segments. Mixed-script phrases are explicitly protected by the
        // script-mismatch predicates; recognizable Finglish has its own one-pass recovery.
        var original = await RunWhisperAsync(cli, model, audioPath, outputBase, null, cancellationToken, includeLanguageMetadata: true);
        var detectedLanguage = _lastWhisperDetectedLanguage;

        if (AutoLanguageText.ShouldTryPersianPreferredPass(original, detectedLanguage))
        {
            var preferredPersian = await RunWhisperAsync(cli, model, audioPath, outputBase + "-fa-preferred", "fa", cancellationToken);
            if (AutoLanguageText.IsPersianCandidate(preferredPersian, original))
                return _settings.PreserveEnglishTechnicalTermsInAuto
                    ? AutoLanguageText.RestoreEnglishTechnicalTerms(preferredPersian)
                    : preferredPersian;
        }

        if (AutoLanguageText.ShouldRetryEnglishForScriptMismatch(original, detectedLanguage))
        {
            var candidate = await RunWhisperAsync(cli, model, audioPath, outputBase + "-en", "en", cancellationToken, includeLanguageMetadata: true);
            if (AutoLanguageText.IsEnglishCandidate(candidate, original) &&
                string.Equals(_lastWhisperDetectedLanguage, "en", StringComparison.OrdinalIgnoreCase)) return candidate;
        }
        else if (AutoLanguageText.ShouldRetryPersianForScriptMismatch(original, detectedLanguage))
        {
            var candidate = await RunWhisperAsync(cli, model, audioPath, outputBase + "-fa", "fa", cancellationToken, includeLanguageMetadata: true);
            if (AutoLanguageText.IsPersianCandidate(candidate, original) &&
                string.Equals(_lastWhisperDetectedLanguage, "fa", StringComparison.OrdinalIgnoreCase)) return candidate;
        }
        else if (LooksLikeRomanizedPersian(original))
        {
            var candidate = await RunWhisperAsync(cli, model, audioPath, outputBase + "-fa", "fa", cancellationToken, includeLanguageMetadata: true);
            if (AutoLanguageText.IsPersianCandidate(candidate, original) &&
                (string.IsNullOrEmpty(_lastWhisperDetectedLanguage) ||
                 string.Equals(_lastWhisperDetectedLanguage, "fa", StringComparison.OrdinalIgnoreCase))) return candidate;
        }

        return original;
    }

    private async Task<string> RunWhisperAsync(
        string cli, ModelInfo model, string audioPath, string outputBase, string? language,
        CancellationToken cancellationToken = default, bool fastPreview = false, bool includeLanguageMetadata = false, string? subtitleFormat = null)
    {
        var outputTxt = outputBase + ".txt";
        var outputJson = outputBase + ".json";
        var outputSrt = outputBase + ".srt";
        var outputVtt = outputBase + ".vtt";
        if (subtitleFormat is not null && !SubtitleExportSupport.IsSupportedSubtitleFormat(subtitleFormat))
            throw new ArgumentException("Subtitle format must be srt or vtt.", nameof(subtitleFormat));
        TryDelete(outputTxt);
        TryDelete(outputJson);
        if (subtitleFormat is null or "srt") TryDelete(outputSrt);
        if (subtitleFormat is null or "vtt") TryDelete(outputVtt);
        _lastWhisperDetectedLanguage = null;
        var startInfo = new ProcessStartInfo
        {
            FileName = cli,
            WorkingDirectory = Path.GetDirectoryName(cli) ?? RuntimeDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add(ModelPath(model));
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(audioPath);
        startInfo.ArgumentList.Add("-otxt");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add(outputBase);
        if (includeLanguageMetadata) startInfo.ArgumentList.Add("-oj");
        if (string.Equals(subtitleFormat, "srt", StringComparison.OrdinalIgnoreCase)) startInfo.ArgumentList.Add("-osrt");
        if (string.Equals(subtitleFormat, "vtt", StringComparison.OrdinalIgnoreCase)) startInfo.ArgumentList.Add("-ovtt");
        startInfo.ArgumentList.Add("-nt");
        startInfo.ArgumentList.Add("-np");
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(Math.Clamp(Environment.ProcessorCount - 1, 1, 8).ToString());
        if (fastPreview)
        {
            // Greedy decoding avoids beam search/best-of retries for temporary partial captions.
            startInfo.ArgumentList.Add("-bs");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-bo");
            startInfo.ArgumentList.Add("1");
        }
        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add(string.IsNullOrWhiteSpace(language) ? "auto" : language);
        if (string.IsNullOrWhiteSpace(language))
        {
            startInfo.ArgumentList.Add("--prompt");
            startInfo.ArgumentList.Add(MixedScriptAutoPrompt);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("The speech engine process could not be started.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(); } catch { }
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { }
            throw;
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException($"Speech engine exited with code {process.ExitCode}.\n{TrimForDialog(details)}");
        }
        var transcript = File.Exists(outputTxt) ? await File.ReadAllTextAsync(outputTxt) : stdout;
        if (includeLanguageMetadata && File.Exists(outputJson))
        {
            _lastWhisperDetectedLanguage = TryReadWhisperDetectedLanguage(outputJson);
            TryDelete(outputJson);
        }
        return transcript.Trim();
    }

    private static string? TryReadWhisperDetectedLanguage(string jsonPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (document.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("language", out var languageElement))
            {
                var value = languageElement.GetString()?.Trim().ToLowerInvariant().Trim('[', ']', ' ', '\t');
                if (value is "fa" or "en" or "persian" or "english")
                    return value is "persian" ? "fa" : value is "english" ? "en" : value;
            }
        }
        catch { /* JSON output is an optional hint; the transcript itself remains usable. */ }
        return null;
    }

    private static bool ContainsPersianScript(string text) => AutoLanguageText.ContainsPersianScript(text);

    private static bool LooksLikeRomanizedPersian(string text) => AutoLanguageText.LooksLikeRomanizedPersian(text);

    private static string TrimForDialog(string value)
    {
        var text = value.Trim();
        return text.Length <= 1600 ? text : text[..1600] + "…";
    }

    private async Task<string> DownloadAndInstallRuntimeAsync(CancellationToken token)
    {
        Directory.CreateDirectory(RuntimeDirectory);
        var existing = FindWhisperCli();
        if (existing is not null) return existing;

        var zipPath = Path.Combine(TempDirectory, "whisper-runtime.zip");
        var urls = await GetRuntimeDownloadUrlsAsync(token);
        Exception? lastError = null;

        foreach (var url in urls)
        {
            TryDelete(zipPath);
            try
            {
                var progress = new Progress<DownloadProgress>(p => ShowDownloadProgress("Downloading speech engine", p));
                await DownloadFileAsync(url, zipPath, progress, token);
                token.ThrowIfCancellationRequested();
                _progressLabel.Text = "Extracting speech engine…";
                SafeZipExtractor.ExtractToDirectory(zipPath, RuntimeDirectory);
                var cli = FindWhisperCli();
                if (cli is not null) return cli;
                throw new InvalidOperationException("The downloaded Windows package did not contain whisper-cli.exe.");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { lastError = ex; }
            finally { TryDelete(zipPath); }
        }

        throw new InvalidOperationException(
            "MadNegar could not download a compatible Windows x64 speech engine. Open the whisper.cpp releases page to check availability.",
            lastError);
    }

    private static async Task<List<string>> GetRuntimeDownloadUrlsAsync(CancellationToken token)
    {
        var urls = new List<string>();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/repos/ggml-org/whisper.cpp/releases/latest");
            request.Headers.UserAgent.ParseAdd("MadNegar/1.0.1");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using var jsonStream = await response.Content.ReadAsStreamAsync(token);
            using var json = await JsonDocument.ParseAsync(jsonStream, cancellationToken: token);
            if (json.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                var candidates = assets.EnumerateArray()
                    .Where(a => a.TryGetProperty("name", out _) && a.TryGetProperty("browser_download_url", out _))
                    .Select(a => new
                    {
                        Name = a.GetProperty("name").GetString() ?? "",
                        Url = a.GetProperty("browser_download_url").GetString() ?? ""
                    })
                    .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                && a.Name.Contains("whisper-bin", StringComparison.OrdinalIgnoreCase)
                                && a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase)
                                && !a.Name.Contains("cuda", StringComparison.OrdinalIgnoreCase)
                                && !a.Name.Contains("vulkan", StringComparison.OrdinalIgnoreCase)
                                && !a.Name.Contains("blas", StringComparison.OrdinalIgnoreCase)
                                && !a.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase)
                                && !a.Name.Contains("win32", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var best = candidates.FirstOrDefault(a =>
                    a.Name.Equals("whisper-bin-x64.zip", StringComparison.OrdinalIgnoreCase))
                    ?? candidates.FirstOrDefault();
                if (best is not null && Uri.TryCreate(best.Url, UriKind.Absolute, out _)) urls.Add(best.Url);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* Direct release URLs below are fallbacks for API outages or rate limits. */ }

        const string directLatest = "https://github.com/ggml-org/whisper.cpp/releases/download/v1.9.4/whisper-bin-x64.zip";
        const string mirrorFallback = "https://sourceforge.net/projects/whisper-cpp.mirror/files/v1.9.1/whisper-bin-x64.zip/download";
        if (!urls.Contains(directLatest, StringComparer.OrdinalIgnoreCase)) urls.Add(directLatest);
        if (!urls.Contains(mirrorFallback, StringComparer.OrdinalIgnoreCase)) urls.Add(mirrorFallback);
        return urls;
    }

    private string? FindWhisperCli()
    {
        if (!Directory.Exists(RuntimeDirectory)) return null;
        try
        {
            return Directory.EnumerateFiles(RuntimeDirectory, "whisper-cli.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private async Task DownloadFileAsync(
        string url,
        string destination,
        IProgress<DownloadProgress> progress,
        CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 1024 * 128, useAsync: true);
        var buffer = new byte[1024 * 128];
        long received = 0;
        var lastReport = DateTime.UtcNow;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            received += read;
            if ((DateTime.UtcNow - lastReport).TotalMilliseconds >= 120 || (total.HasValue && received >= total.Value))
            {
                progress.Report(new DownloadProgress(received, total));
                lastReport = DateTime.UtcNow;
            }
        }
        await output.FlushAsync(token);
        progress.Report(new DownloadProgress(received, total));
    }

    private void ShowDownloadProgress(string prefix, DownloadProgress progress)
    {
        if (IsDisposed || _isClosing) return;
        if (progress.TotalBytes is > 0)
        {
            if (_progress.Style != ProgressBarStyle.Blocks)
            {
                _progress.Style = ProgressBarStyle.Blocks;
                _progress.MarqueeAnimationSpeed = 0;
            }
            var percent = (int)Math.Clamp(progress.ReceivedBytes * 100d / progress.TotalBytes.Value, 0, 100);
            _progress.Value = percent;
            _progressLabel.Text = $"{prefix}: {percent}% · {FormatBytes(progress.ReceivedBytes)} / {FormatBytes(progress.TotalBytes.Value)}";
        }
        else
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 24;
            _progressLabel.Text = $"{prefix}: {FormatBytes(progress.ReceivedBytes)} received";
        }
    }

    private void SetProgressMode(ProgressBarStyle style, int value)
    {
        _progress.Style = style;
        _progress.MarqueeAnimationSpeed = style == ProgressBarStyle.Marquee ? 24 : 0;
        if (style == ProgressBarStyle.Blocks)
            _progress.Value = Math.Clamp(value, _progress.Minimum, _progress.Maximum);
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

    private async void HandleGlobalHotkeyPressed()
    {
        if (_isClosing || !_settings.GlobalHotkeyEnabled) return;
        // The modal batch window may be processing while each individual TranscribeAsync
        // temporarily clears _isTranscribing. Never let the global hotkey start a second job.
        if (_isFileTranscribing) return;

        if (_directTypingSession)
        {
            if (_isRecording && !_isStoppingRecording) StopRecording();
            else if (_isStoppingRecording || _isTranscribing)
                ShowHotkeyNotification("Voice typing", "The current recording is already being finalized. Please wait for transcription to finish.");
            return;
        }

        if (_isRecording || _isStoppingRecording || _isBusy || _isTranscribing)
        {
            ShowHotkeyNotification("Voice typing is busy", "Finish the current recording or transcription before starting direct typing.");
            return;
        }

        DirectTypingTarget? target = null;
        if (_settings.DirectTypingAutoInsert)
        {
            if (!GlobalHotkeySupport.TryCaptureForegroundTarget(Handle, out target, out var targetProblem) || target is null)
            {
                ShowHotkeyNotification("Select a destination field", targetProblem);
                return;
            }
        }

        var selectedModel = SelectedModel;
        if (selectedModel is { IsWhisper: true } && FindWhisperCli() is null)
        {
            ShowHotkeyNotification("Whisper runtime setup required", "Start one recording from the MadNegar window first so the local Whisper runtime can be installed. The global shortcut will not start a background download.");
            return;
        }
        if (selectedModel is { IsFasterWhisper: true } && !FasterWhisperRuntime.IsReady(_root))
        {
            ShowHotkeyNotification("Runtime setup required", "Start one recording from the MadNegar window first to approve and complete the faster-whisper runtime setup.");
            return;
        }

        _pendingDirectTypingTarget = null;
        _pendingDirectTypingText = null;
        _directTypingCancellation?.Dispose();
        _directTypingCancellation = new CancellationTokenSource();
        _directTypingCancelled = false;
        _directTypingTarget = target;
        _directTypingSession = true;
        RefreshControls();
        try
        {
            await StartRecordingClickedAsync(fromGlobalHotkey: true);
            if (!_isRecording && !_isBusy && !_isTranscribing) FinishDirectTypingSession();
        }
        catch (Exception ex)
        {
            ShowHotkeyNotification("Voice typing could not start", ex.Message);
            FinishDirectTypingSession();
        }
    }

    private void ReportRecordingStartProblem(string title, string message, bool fromGlobalHotkey)
    {
        if (fromGlobalHotkey) ShowHotkeyNotification(title, message);
        else MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowHotkeyNotification(string title, string message)
    {
        // Global voice typing deliberately uses no Windows toast or tray-balloon.
        // The main-window status remains available for diagnostics when the user opens it.
        _statusLabel.Text = title;
        _progressLabel.Text = message.Length > 190 ? message[..190] + "…" : message;
    }

    private void FinishDirectTypingSession()
    {
        _directTypingSession = false;
        _directTypingTarget = null;
        _directTypingCancelled = false;
        try { _directTypingCancellation?.Dispose(); } catch { }
        _directTypingCancellation = null;
        if (!_isClosing && !IsDisposed && IsHandleCreated) RefreshControls();
    }

    private void CancelDirectTyping()
    {
        if (!_directTypingSession || _directTypingCancelled) return;
        _directTypingCancelled = true;
        try { _directTypingCancellation?.Cancel(); } catch { }
        try { _operationCts?.Cancel(); } catch { }

        if (_isRecording && !_isStoppingRecording)
        {
            ShowHotkeyNotification("Cancelling voice typing", "Stopping the microphone and discarding this recording. No transcript will be inserted.");
            StopRecording();
            RefreshControls();
            return;
        }
        if (_isStoppingRecording)
        {
            ShowHotkeyNotification("Cancelling voice typing", "The microphone is stopping. The recording will be discarded.");
            RefreshControls();
            return;
        }
        if (_isTranscribing || _isBusy)
        {
            ShowHotkeyNotification("Cancellation requested", "The current local engine operation may finish, but MadNegar will discard its result and will not insert it.");
            RefreshControls();
            return;
        }

        FinishDirectTypingSession();
        _statusLabel.Text = "Voice typing cancelled.";
        _progressLabel.Text = "No transcript was inserted.";
        RefreshControls();
    }

    private async Task<DirectTypingResult> CopyFinalTranscriptToClipboardAsync(string text)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText)
                    && string.Equals(Clipboard.GetText(TextDataFormat.UnicodeText), text, StringComparison.Ordinal))
                    return new DirectTypingResult(true, "The complete final transcript was copied as Unicode text.");
                lastError = new InvalidOperationException("The clipboard did not retain the exact final transcript.");
            }
            catch (Exception ex) { lastError = ex; }
            if (attempt < 2) await Task.Delay(100);
        }
        return new DirectTypingResult(false, $"The transcript could not be copied after three attempts ({lastError?.Message ?? "unknown clipboard error"}).");
    }

    private async Task RetryDirectTypingInsertAsync()
    {
        var target = _pendingDirectTypingTarget;
        var text = _pendingDirectTypingText;
        if (target is null || string.IsNullOrWhiteSpace(text)
            || _isRecording || _isStoppingRecording || _isTranscribing || _isBusy || _directTypingSession)
            return;

        _retryInsertButton.Enabled = false;
        try { _retryInsertCancellation?.Cancel(); } catch { }
        _retryInsertCancellation?.Dispose();
        using var retryCts = new CancellationTokenSource();
        _retryInsertCancellation = retryCts;
        var retryToken = retryCts.Token;
        _statusLabel.Text = "Preparing retry…";
        _progressLabel.Text = "Trying to reactivate the original destination, then verifying its exact focused control.";
        ShowHotkeyNotification("Inserting", "Retry was requested. MadNegar will reactivate the original destination only if the same control can be verified.");
        try
        {
            if (!await GlobalHotkeySupport.TryReactivateCapturedTargetAsync(target, retryToken))
            {
                if (!_isClosing)
                    ShowHotkeyNotification("Insert failed", "The original destination or exact text field could not be safely reactivated. The transcript remains in MadNegar; use Copy Text and paste manually.");
                return;
            }
            if (retryToken.IsCancellationRequested || _isClosing) return;

            var result = await GlobalHotkeySupport.TryPasteTextPreservingClipboardAsync(text, target, retryToken);
            if (result.Success)
            {
                _pendingDirectTypingTarget = null;
                _pendingDirectTypingText = null;
                ShowHotkeyNotification("Voice typing complete", result.Message + " Enter was not pressed.");
            }
            else
            {
                if (!result.RetrySafe)
                {
                    _pendingDirectTypingTarget = null;
                    _pendingDirectTypingText = null;
                }
                ShowHotkeyNotification("Insert failed", result.Message + (result.RetrySafe
                    ? " You can try Retry Insert again or use Copy Text."
                    : " Automatic retry has been disabled because partial insertion may have occurred; review the destination first."));
            }
        }
        catch (OperationCanceledException)
        {
            if (!_isClosing) ShowHotkeyNotification("Retry cancelled", "The retry was cancelled before a new paste could be sent.");
        }
        catch (Exception ex)
        {
            if (!_isClosing) ShowHotkeyNotification("Insert failed", $"Retry failed ({ex.Message}). The transcript remains in MadNegar; use Copy Text.");
        }
        finally
        {
            if (ReferenceEquals(_retryInsertCancellation, retryCts)) _retryInsertCancellation = null;
            RefreshControls();
        }
    }

    private void UnregisterGlobalHotkey()
    {
        if (!_hotkeyRegistered) return;
        GlobalHotkeySupport.Unregister(_registeredHotkeyHandle);
        _hotkeyRegistered = false;
        _registeredHotkeyHandle = IntPtr.Zero;
    }

    private void RefreshGlobalHotkeyRegistration(bool showError)
    {
        if (!IsHandleCreated || _isClosing) return;
        UnregisterGlobalHotkey();
        if (!_settings.GlobalHotkeyEnabled)
        {
            _notifyIcon.Text = "MadNegar - Voice typing";
            _notifyIcon.Visible = _isHiddenToTray;
            return;
        }

        if (GlobalHotkeySupport.TryRegister(Handle, _settings.GlobalHotkeyShortcut, out var errorCode))
        {
            _hotkeyRegistered = true;
            _registeredHotkeyHandle = Handle;
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "MadNegar - Global voice typing enabled";
            return;
        }

        _settings.GlobalHotkeyEnabled = false;
        try { AppSettingsStore.Save(_root, _settings); } catch { }
        var detail = $"MadNegar could not register {_settings.GlobalHotkeyShortcut}. Windows error {errorCode}: {new System.ComponentModel.Win32Exception(errorCode).Message}. The shortcut was switched off. Choose another shortcut in Settings → Transcription.";
        if (showError)
        {
            _notifyIcon.Visible = _isHiddenToTray;
            MessageBox.Show(this, detail, "Global shortcut unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            // A saved shortcut may become unavailable if another application claims it while MadNegar is closed.
            _notifyIcon.Visible = true;
            ShowHotkeyNotification("Global shortcut unavailable", detail);
        }
        _notifyIcon.Text = "MadNegar - Voice typing";
    }

    private void UpdateTranscriptTextDirection(string? text = null)
    {
        if (_transcriptBox.IsDisposed) return;
        var value = text ?? _transcriptBox.Text;
        var spokenLanguage = _languageCombo.SelectedIndex switch { 1 => "fa", 2 => "en", _ => "auto" };
        var persianLetters = AutoLanguageText.CountPersianLetters(value);
        var latinLetters = AutoLanguageText.CountLatinLetters(value);
        var rightToLeft = spokenLanguage == "fa" ||
            (spokenLanguage == "auto" && persianLetters >= 2);
        // Auto follows the script actually returned; transcript-language preference never sets UI direction.
        if (spokenLanguage == "auto" && persianLetters == 0 && latinLetters >= 4) rightToLeft = false;
        _transcriptBox.RightToLeft = rightToLeft ? RightToLeft.Yes : RightToLeft.No;
        _transcriptBox.TextAlign = rightToLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    private void ClearTranscript()
    {
        if (_isRecording || _isStoppingRecording || _isTranscribing || _isBusy) return;
        _pendingDirectTypingTarget = null;
        _pendingDirectTypingText = null;
        ClearPersianUndoSnapshot();
        _liveTranscriptText = string.Empty;
        _transcriptBox.Clear();
        UpdateTranscriptTextDirection();
        RefreshControls();
    }

    private string SetFinalTranscriptText(string rawTranscript, bool wasDirectTyping)
    {
        var shouldNormalize = !wasDirectTyping || _settings.ApplyPersianTextNormalizationToDirectTyping;
        var finalText = shouldNormalize
            ? PersianTextNormalizer.Normalize(rawTranscript,
                _settings.PersianTextNormalizeYeh,
                _settings.PersianTextNormalizeKaf,
                _settings.PersianTextNormalizeHalfSpaces,
                _settings.PersianTextNormalizePunctuation)
            : rawTranscript;
        if (!wasDirectTyping || _settings.ApplyLocalDictionaryToDirectTyping)
        {
            var spokenLanguage = _languageCombo.SelectedIndex switch { 1 => "fa", 2 => "en", _ => "auto" };
            finalText = LocalDictionaryText.Apply(finalText, _settings.LocalDictionaryEntries, spokenLanguage);
        }

        if (!string.Equals(finalText, rawTranscript, StringComparison.Ordinal))
        {
            _rawTranscriptBeforeNormalization = rawTranscript;
            _normalizedTranscriptForUndo = finalText;
        }
        else
        {
            ClearPersianUndoSnapshot();
        }

        _settingTranscriptProgrammatically = true;
        try
        {
            _transcriptBox.Text = finalText;
            UpdateTranscriptTextDirection(finalText);
            _transcriptBox.SelectionStart = _transcriptBox.TextLength;
            _transcriptBox.ScrollToCaret();
        }
        finally
        {
            _settingTranscriptProgrammatically = false;
        }
        RefreshControls();
        return finalText;
    }

    private void UndoPersianTextFixes()
    {
        if (_rawTranscriptBeforeNormalization is not { } raw ||
            !string.Equals(_normalizedTranscriptForUndo, _transcriptBox.Text, StringComparison.Ordinal))
            return;

        _pendingDirectTypingTarget = null;
        _pendingDirectTypingText = null;
        ClearPersianUndoSnapshot();
        _settingTranscriptProgrammatically = true;
        try
        {
            _transcriptBox.Text = raw;
            UpdateTranscriptTextDirection(raw);
            _transcriptBox.SelectionStart = _transcriptBox.TextLength;
            _transcriptBox.ScrollToCaret();
        }
        finally
        {
            _settingTranscriptProgrammatically = false;
        }
        _statusLabel.Text = "Original transcript restored.";
        _progressLabel.Text = "Persian text fixes were undone inside MadNegar.";
        RefreshControls();
    }

    private void ClearPersianUndoSnapshot()
    {
        _rawTranscriptBeforeNormalization = null;
        _normalizedTranscriptForUndo = null;
        if (!_undoTextFixButton.IsDisposed) _undoTextFixButton.Enabled = false;
    }

    private async void ExportTranscript()
    {
        var text = _transcriptBox.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        var canExportSubtitles = CanExportImportedSubtitles();
        using var dialog = new SaveFileDialog
        {
            Title = "Export transcript",
            Filter = canExportSubtitles
                ? "Text files (*.txt)|*.txt|SubRip subtitles (*.srt)|*.srt|WebVTT subtitles (*.vtt)|*.vtt"
                : "Text files (*.txt)|*.txt",
            FilterIndex = 1,
            AddExtension = false,
            OverwritePrompt = false,
            FileName = $"MadNegar-{DateTime.Now:yyyyMMdd-HHmmss}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var destinationPath = dialog.FileName;
        var extension = Path.GetExtension(destinationPath).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
        {
            extension = dialog.FilterIndex switch { 2 => ".srt", 3 => ".vtt", _ => ".txt" };
            destinationPath += extension;
        }
        if (extension is not (".txt" or ".srt" or ".vtt"))
        {
            MessageBox.Show(this, "Choose a TXT, SRT or VTT extension. No file was written.", "Unsupported export format", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (File.Exists(destinationPath) && MessageBox.Show(this,
            $"{Path.GetFileName(destinationPath)} already exists. Replace it?", "Confirm overwrite", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        try
        {
            if (extension is ".srt" or ".vtt")
            {
                await ExportImportedSubtitlesAsync(destinationPath, extension[1..]);
                return;
            }
            TranscriptTextExport.WriteUtf8(destinationPath, text);
            _statusLabel.Text = "Transcript exported to TXT.";
            _progressLabel.Text = $"Saved UTF-8 text to {Path.GetFileName(destinationPath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The transcript could not be exported. Check the destination folder and write permissions.\n\n" + ex.Message,
                "Export transcript", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenTranscriptHistory()
    {
        using var dialog = new TranscriptHistoryForm(_root, _settings.TranscriptHistoryEnabled, _settings.ThemeMode);
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedTranscript)) return;
        LoadTranscriptFromHistory(dialog.SelectedTranscript);
    }

    private void LoadTranscriptFromHistory(string text)
    {
        _pendingDirectTypingTarget = null;
        _pendingDirectTypingText = null;
        ClearPersianUndoSnapshot();
        _settingTranscriptProgrammatically = true;
        try
        {
            _transcriptBox.Text = text;
            _transcriptBox.SelectionStart = _transcriptBox.TextLength;
            _transcriptBox.ScrollToCaret();
        }
        finally { _settingTranscriptProgrammatically = false; }
        _statusLabel.Text = "Transcript loaded from local history.";
        _progressLabel.Text = "Review the text, copy it, or export it to TXT.";
        RefreshControls();
    }

    private void CopyTranscript()
    {
        if (string.IsNullOrEmpty(_transcriptBox.Text)) return;
        try
        {
            Clipboard.SetText(_transcriptBox.Text);
            _statusLabel.Text = "Text copied to clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not copy text to the clipboard.\n\n" + ex.Message,
                "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        _isClosing = true;
        UnregisterGlobalHotkey();
        _directTypingCancelled = true;
        try { _directTypingCancellation?.Cancel(); } catch { }
        try { _retryInsertCancellation?.Cancel(); } catch { }
        _pendingDirectTypingTarget = null;
        _pendingDirectTypingText = null;
        _liveTranscriptionTimer.Stop();
        try { _liveTranscriptionCts?.Cancel(); } catch { }
        _notifyIcon.Visible = false;
        try { _operationCts?.Cancel(); } catch { }
        try { _waveIn?.StopRecording(); } catch { }
        // The capture/device are released by the asynchronous RecordingStopped callback.
    }

    private sealed record DownloadProgress(long ReceivedBytes, long? TotalBytes);
}
