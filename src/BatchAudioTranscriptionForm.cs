namespace MadNegar;

internal sealed class BatchAudioTranscriptionForm : Form
{
    private readonly Func<IReadOnlyList<string>, BatchAudioOutputOptions, CancellationToken,
        IProgress<BatchAudioProgress>, Task<IReadOnlyList<BatchAudioFileResult>>> _processor;
    private readonly string _themeMode;
    private readonly bool _subtitlesSupported;
    private readonly ListView _files = new();
    private readonly TextBox _outputFolder = new();
    private readonly CheckBox _txtCheck = new();
    private readonly CheckBox _srtCheck = new();
    private readonly CheckBox _vttCheck = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _addButton = new();
    private readonly Button _removeButton = new();
    private readonly Button _clearButton = new();
    private readonly Button _browseButton = new();
    private readonly Button _retryFailedButton = new();
    private readonly Button _startButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _closeButton = new();
    private CancellationTokenSource? _cancellation;
    private readonly Dictionary<string, BatchAudioFileOutputState> _outputStates = new(StringComparer.OrdinalIgnoreCase);
    private bool _running;

    public BatchAudioTranscriptionForm(
        string themeMode,
        bool subtitlesSupported,
        Func<IReadOnlyList<string>, BatchAudioOutputOptions, CancellationToken,
            IProgress<BatchAudioProgress>, Task<IReadOnlyList<BatchAudioFileResult>>> processor)
    {
        _themeMode = themeMode;
        _subtitlesSupported = subtitlesSupported;
        _processor = processor;
        Text = "MadNegar — Batch Audio Transcription";
        ClientSize = new Size(900, 590);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;

        var title = new Label
        {
            Text = "Batch audio transcription",
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            Location = new Point(16, 14),
            Size = new Size(420, 30)
        };
        Controls.Add(title);
        var description = new Label
        {
            Text = "Files run sequentially and locally. The source audio is never changed. Existing output files are not overwritten.",
            Location = new Point(18, 47),
            Size = new Size(850, 25),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Tag = "muted"
        };
        Controls.Add(description);

        _files.Location = new Point(18, 80);
        _files.Size = new Size(850, 300);
        _files.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _files.View = View.Details;
        _files.FullRowSelect = true;
        _files.GridLines = true;
        _files.MultiSelect = true;
        _files.HideSelection = false;
        _files.SelectedIndexChanged += (_, _) => RefreshButtons();
        _files.Columns.Add("Audio file", 300);
        _files.Columns.Add("Status", 125);
        _files.Columns.Add("Details", 405);
        Controls.Add(_files);

        ConfigureButton(_addButton, "Add files…", 18, 388, 98);
        _addButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _addButton.Click += (_, _) => AddFiles();
        Controls.Add(_addButton);
        ConfigureButton(_removeButton, "Remove", 122, 388, 82);
        _removeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _removeButton.Click += (_, _) => RemoveSelectedFiles();
        Controls.Add(_removeButton);
        ConfigureButton(_clearButton, "Clear list", 210, 388, 82);
        _clearButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _clearButton.Click += (_, _) => _files.Items.Clear();
        Controls.Add(_clearButton);

        var folderLabel = new Label { Text = "Output folder", Location = new Point(18, 430), Size = new Size(92, 24) };
        folderLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        Controls.Add(folderLabel);
        _outputFolder.Location = new Point(112, 426);
        _outputFolder.Size = new Size(650, 28);
        _outputFolder.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _outputFolder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MadNegar Transcripts");
        Controls.Add(_outputFolder);
        ConfigureButton(_browseButton, "Browse…", 770, 424, 98);
        _browseButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _browseButton.Click += (_, _) => BrowseOutputFolder();
        Controls.Add(_browseButton);

        _txtCheck.Text = "Export TXT";
        _txtCheck.Checked = true;
        _txtCheck.Location = new Point(18, 462);
        _txtCheck.Size = new Size(110, 25);
        _txtCheck.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        Controls.Add(_txtCheck);
        _srtCheck.Text = "Export SRT";
        _srtCheck.Checked = false;
        _srtCheck.Enabled = subtitlesSupported;
        _srtCheck.Location = new Point(132, 462);
        _srtCheck.Size = new Size(105, 25);
        _srtCheck.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        Controls.Add(_srtCheck);
        _vttCheck.Text = "Export VTT";
        _vttCheck.Checked = false;
        _vttCheck.Enabled = subtitlesSupported;
        _vttCheck.Location = new Point(240, 462);
        _vttCheck.Size = new Size(105, 25);
        _vttCheck.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        Controls.Add(_vttCheck);

        var formatNote = new Label
        {
            Text = subtitlesSupported ? "SRT/VTT require a Whisper.cpp model and real timestamps." : "SRT/VTT disabled: select a supported Whisper.cpp final model to export timestamped subtitles.",
            Location = new Point(355, 462),
            Size = new Size(513, 25),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Tag = "muted"
        };
        Controls.Add(formatNote);

        _progress.Location = new Point(18, 495);
        _progress.Size = new Size(850, 12);
        _progress.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _progress.Minimum = 0;
        _progress.Maximum = 100;
        Controls.Add(_progress);

        _status.Location = new Point(18, 511);
        _status.Size = new Size(850, 24);
        _status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _status.Tag = "muted";
        _status.Text = "Add one or more supported audio files to begin.";
        Controls.Add(_status);

        ConfigureButton(_retryFailedButton, "Retry failed", 400, 545, 112);
        _retryFailedButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _retryFailedButton.Enabled = false;
        _retryFailedButton.Click += async (_, _) => await RetryFailedAsync();
        Controls.Add(_retryFailedButton);

        ConfigureButton(_startButton, "Start batch", 520, 545, 112, primary: true);
        _startButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _startButton.Click += async (_, _) => await StartBatchAsync();
        Controls.Add(_startButton);
        ConfigureButton(_cancelButton, "Cancel batch", 640, 545, 112);
        _cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _cancelButton.Enabled = false;
        _cancelButton.Click += (_, _) => RequestCancellation();
        Controls.Add(_cancelButton);
        ConfigureButton(_closeButton, "Close", 760, 545, 108);
        _closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _closeButton.Click += (_, _) => Close();
        Controls.Add(_closeButton);

        CancelButton = _closeButton;
        FormClosing += (_, e) =>
        {
            if (!_running) return;
            e.Cancel = true;
            RequestCancellation();
        };
        FormClosed += (_, _) => _cancellation?.Dispose();
        ThemeManager.Apply(this, themeMode);
    }

    private void ConfigureButton(Button button, string text, int x, int y, int width, bool primary = false)
    {
        button.Text = text;
        button.Location = new Point(x, y);
        button.Size = new Size(width, 32);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.Cursor = Cursors.Hand;
        button.Tag = primary ? "primary" : "secondary";
    }

    private void AddFiles()
    {
        if (_running) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Add audio files to the batch",
            Filter = "Supported audio (*.wav;*.mp3;*.aif;*.aiff)|*.wav;*.mp3;*.aif;*.aiff|All files (*.*)|*.*",
            FilterIndex = 1,
            Multiselect = true,
            CheckFileExists = true,
            DereferenceLinks = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var known = _files.Items.Cast<ListViewItem>()
            .Select(item => item.Tag as string)
            .Where(path => path is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        foreach (var selectedPath in dialog.FileNames)
        {
            var path = Path.GetFullPath(selectedPath);
            if (!BatchTranscriptionSupport.IsSupportedAudioPath(path) || !File.Exists(path) || !known.Add(path))
            {
                skipped++;
                continue;
            }
            var item = new ListViewItem(Path.GetFileName(path));
            item.SubItems.Add("Pending");
            item.SubItems.Add(path);
            item.Tag = path;
            _files.Items.Add(item);
        }
        _status.Text = skipped == 0
            ? $"Queued {_files.Items.Count} file(s)."
            : $"Queued {_files.Items.Count} file(s); skipped {skipped} duplicate, unsupported or missing selection(s).";
        RefreshButtons();
    }

    private void RemoveSelectedFiles()
    {
        if (_running) return;
        foreach (ListViewItem item in _files.SelectedItems.Cast<ListViewItem>().ToArray()) _files.Items.Remove(item);
        RefreshButtons();
    }

    private void BrowseOutputFolder()
    {
        if (_running) return;
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where batch transcripts and subtitles should be saved.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_outputFolder.Text) ? _outputFolder.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _outputFolder.Text = dialog.SelectedPath;
    }

    private async Task StartBatchAsync(IReadOnlyList<string>? requestedPaths = null, bool retryExistingOutputs = false)
    {
        if (_running) return;
        var paths = requestedPaths?.ToArray() ?? _files.Items.Cast<ListViewItem>().Select(item => item.Tag as string)
            .Where(path => !string.IsNullOrWhiteSpace(path)).Cast<string>().ToArray();
        if (paths.Length == 0)
        {
            MessageBox.Show(this, "Add at least one supported audio file.", "Batch transcription", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!_txtCheck.Checked && !_srtCheck.Checked && !_vttCheck.Checked)
        {
            MessageBox.Show(this, "Choose at least one output format.", "Batch transcription", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if ((_srtCheck.Checked || _vttCheck.Checked) && !_subtitlesSupported)
        {
            MessageBox.Show(this, "SRT/VTT require a supported Whisper.cpp model.", "Batch transcription", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(_outputFolder.Text))
        {
            MessageBox.Show(this, "Choose an output folder.", "Batch transcription", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        BatchAudioOutputOptions options;
        try
        {
            var folder = Path.GetFullPath(_outputFolder.Text.Trim());
            Directory.CreateDirectory(folder);
            IReadOnlyDictionary<string, BatchAudioFileOutputState>? priorStates = retryExistingOutputs
                ? new Dictionary<string, BatchAudioFileOutputState>(_outputStates, StringComparer.OrdinalIgnoreCase)
                : null;
            options = new BatchAudioOutputOptions(folder, _txtCheck.Checked, _srtCheck.Checked, _vttCheck.Checked, priorStates);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The output folder could not be created. Choose a writable local folder.\n\n" + ex.Message,
                "Batch transcription", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        SetRunning(true);
        _progress.Minimum = 0;
        _progress.Maximum = paths.Length;
        _progress.Value = 0;
        var selectedRunPaths = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (ListViewItem item in _files.Items)
        {
            var path = item.Tag as string;
            if (path is not null && selectedRunPaths.Contains(path))
            {
                item.SubItems[1].Text = "Pending";
                item.SubItems[2].Text = path;
            }
        }
        _status.Text = $"Starting batch for {paths.Length} file(s)…";
        var progress = new Progress<BatchAudioProgress>(UpdateProgress);
        try
        {
            var results = await _processor(paths, options, _cancellation.Token, progress);
            var succeeded = results.Count(result => result.Status.Equals("Success", StringComparison.OrdinalIgnoreCase));
            var partial = results.Count(result => result.Status.Equals("Partial", StringComparison.OrdinalIgnoreCase));
            var failed = results.Count(result => result.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase));
            var noSpeech = results.Count(result => result.Status.Equals("No speech", StringComparison.OrdinalIgnoreCase));
            var cancelled = results.Count(result => result.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase));
            _status.Text = _cancellation.IsCancellationRequested
                ? $"Cancelled · {succeeded} complete, {partial} partial, {failed} failed, {noSpeech} no speech, {cancelled} cancelled."
                : $"Batch complete · {succeeded} complete, {partial} partial, {failed} failed, {noSpeech} no speech.";
            foreach (var result in results)
            {
                UpdateOneResult(result);
                if (result.OutputStem is not null && result.CompletedFormats is not null)
                    _outputStates[result.InputPath] = new BatchAudioFileOutputState(
                        options.OutputDirectory, result.OutputStem,
                        new HashSet<string>(result.CompletedFormats, StringComparer.OrdinalIgnoreCase));
            }
            RefreshButtons();
            _progress.Value = Math.Min(_progress.Maximum, Math.Max(_progress.Value, results.Count));
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Batch cancelled. The current output was either not published or had already completed; check the per-file status.";
        }
        catch (Exception ex)
        {
            _status.Text = "Batch stopped unexpectedly: " + ex.Message;
        }
        finally
        {
            SetRunning(false);
            _cancellation?.Dispose();
            _cancellation = null;
            RefreshButtons();
        }
    }

    private async Task RetryFailedAsync()
    {
        if (_running) return;
        var retryPaths = _files.Items.Cast<ListViewItem>()
            .Where(item => item.SubItems.Count > 1 && item.SubItems[1].Text is "Failed" or "Partial" or "No speech" or "Cancelled")
            .Select(item => item.Tag as string)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
        if (retryPaths.Length == 0) return;
        await StartBatchAsync(retryPaths, retryExistingOutputs: true);
    }

    private void UpdateProgress(BatchAudioProgress update)
    {
        if (IsDisposed || !IsHandleCreated) return;
        var item = FindItem(update.InputPath);
        if (item is not null)
        {
            item.SubItems[1].Text = update.Status;
            item.SubItems[2].Text = update.Details;
        }
        _status.Text = $"File {Math.Min(update.Index + 1, update.Total)} of {update.Total} · {Path.GetFileName(update.InputPath)} · {update.Status}";
        if (update.Status is "Success" or "Partial" or "Failed" or "No speech" or "Cancelled")
            _progress.Value = Math.Min(_progress.Maximum, Math.Max(_progress.Value, update.Index + 1));
    }

    private void UpdateOneResult(BatchAudioFileResult result)
    {
        var item = FindItem(result.InputPath);
        if (item is null) return;
        item.SubItems[1].Text = result.Status;
        item.SubItems[2].Text = result.Details;
    }

    private ListViewItem? FindItem(string path) => _files.Items.Cast<ListViewItem>()
        .FirstOrDefault(item => string.Equals(item.Tag as string, path, StringComparison.OrdinalIgnoreCase));

    private void RequestCancellation()
    {
        if (!_running) return;
        try { _cancellation?.Cancel(); } catch { }
        _cancelButton.Enabled = false;
        _status.Text = "Cancellation requested. The current engine may need a short time to stop…";
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _files.Enabled = !running;
        CancelButton = running ? _cancelButton : _closeButton;
        _addButton.Enabled = !running;
        _removeButton.Enabled = !running;
        _clearButton.Enabled = !running;
        _browseButton.Enabled = !running;
        _outputFolder.Enabled = !running;
        _txtCheck.Enabled = !running;
        _srtCheck.Enabled = !running && _subtitlesSupported;
        _vttCheck.Enabled = !running && _subtitlesSupported;
        _retryFailedButton.Enabled = !running && _files.Items.Cast<ListViewItem>()
            .Any(item => item.SubItems.Count > 1 && item.SubItems[1].Text is "Failed" or "Partial" or "No speech" or "Cancelled");
        _startButton.Enabled = !running;
        _cancelButton.Enabled = running;
        _closeButton.Enabled = !running;
    }

    private void RefreshButtons()
    {
        if (_running) return;
        _removeButton.Enabled = _files.SelectedItems.Count > 0;
        _clearButton.Enabled = _files.Items.Count > 0;
        _startButton.Enabled = _files.Items.Count > 0;
        _retryFailedButton.Enabled = _files.Items.Cast<ListViewItem>()
            .Any(item => item.SubItems.Count > 1 && item.SubItems[1].Text is "Failed" or "Partial" or "No speech" or "Cancelled");
    }
}
