namespace MadNegar;

internal sealed class TranscriptHistoryForm : Form
{
    private readonly string _rootDirectory;
    private readonly bool _historyEnabled;
    private readonly TextBox _searchBox = new();
    private readonly ListView _entriesList = new();
    private readonly TextBox _transcriptPreview = new();
    private readonly Label _statusLabel = new();
    private readonly Button _useButton = new();
    private readonly Button _copyButton = new();
    private readonly Button _deleteButton = new();
    private readonly Button _clearAllButton = new();
    private readonly Button _closeButton = new();
    private List<TranscriptHistoryEntry> _entries = new();

    public string? SelectedTranscript { get; private set; }

    public TranscriptHistoryForm(string rootDirectory, bool historyEnabled, string themeMode)
    {
        _rootDirectory = rootDirectory;
        _historyEnabled = historyEnabled;
        Text = "MadNegar Transcript History";
        ClientSize = new Size(850, 640);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(700, 520);
        MaximizeBox = true;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;

        var title = new Label
        {
            Text = "Transcript History",
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            Location = new Point(16, 14),
            Size = new Size(360, 30)
        };
        Controls.Add(title);

        var optInNote = new Label
        {
            Text = historyEnabled
                ? "History is ON. Final text is stored locally; audio is never stored here."
                : "History is OFF for new transcripts. Existing entries remain available until deleted.",
            Location = new Point(18, 48),
            Size = new Size(800, 24),
            Tag = "muted"
        };
        Controls.Add(optInNote);

        var searchLabel = new Label { Text = "Search", Location = new Point(18, 82), Size = new Size(55, 24) };
        Controls.Add(searchLabel);
        _searchBox.Location = new Point(76, 78);
        _searchBox.Size = new Size(742, 28);
        _searchBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _searchBox.TextChanged += (_, _) => RefreshEntryList();
        Controls.Add(_searchBox);

        _entriesList.Location = new Point(18, 116);
        _entriesList.Size = new Size(800, 238);
        _entriesList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _entriesList.View = View.Details;
        _entriesList.FullRowSelect = true;
        _entriesList.GridLines = true;
        _entriesList.MultiSelect = false;
        _entriesList.HideSelection = false;
        _entriesList.Columns.Add("Date / time", 138);
        _entriesList.Columns.Add("Language", 78);
        _entriesList.Columns.Add("Model", 160);
        _entriesList.Columns.Add("Transcript preview", 400);
        _entriesList.SelectedIndexChanged += (_, _) => UpdateSelectedPreview();
        Controls.Add(_entriesList);

        var previewLabel = new Label { Text = "Selected transcript", Location = new Point(18, 365), Size = new Size(200, 22) };
        Controls.Add(previewLabel);
        _transcriptPreview.Location = new Point(18, 389);
        _transcriptPreview.Size = new Size(800, 145);
        _transcriptPreview.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _transcriptPreview.Multiline = true;
        _transcriptPreview.ReadOnly = true;
        _transcriptPreview.ScrollBars = ScrollBars.Vertical;
        _transcriptPreview.BorderStyle = BorderStyle.FixedSingle;
        _transcriptPreview.Font = new Font("Segoe UI", 10F);
        Controls.Add(_transcriptPreview);

        _statusLabel.Location = new Point(18, 540);
        _statusLabel.Size = new Size(800, 24);
        _statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _statusLabel.Tag = "muted";
        Controls.Add(_statusLabel);

        ConfigureButton(_useButton, "Use in editor", 18, 575, 124, primary: true);
        _useButton.Click += (_, _) => UseSelectedTranscript();
        Controls.Add(_useButton);
        ConfigureButton(_copyButton, "Copy", 150, 575, 92);
        _copyButton.Click += (_, _) => CopySelectedTranscript();
        Controls.Add(_copyButton);
        ConfigureButton(_deleteButton, "Delete selected", 250, 575, 124);
        _deleteButton.Click += (_, _) => DeleteSelectedTranscript();
        Controls.Add(_deleteButton);
        ConfigureButton(_clearAllButton, "Clear all…", 382, 575, 112);
        _clearAllButton.Click += (_, _) => ClearAllHistory();
        Controls.Add(_clearAllButton);
        ConfigureButton(_closeButton, "Close", 714, 575, 104);
        _closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _closeButton.Click += (_, _) => Close();
        Controls.Add(_closeButton);
        CancelButton = _closeButton;

        ThemeManager.Apply(this, themeMode);
        RefreshEntries();
    }

    private void ConfigureButton(Button button, string text, int x, int y, int width, bool primary = false)
    {
        button.Text = text;
        button.Location = new Point(x, y);
        button.Size = new Size(width, 32);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.Cursor = Cursors.Hand;
        button.Tag = primary ? "primary" : "secondary";
        ThemeManager.ApplyControl(button, "Light");
    }

    private TranscriptHistoryEntry? SelectedEntry => _entriesList.SelectedItems.Count == 0
        ? null : _entriesList.SelectedItems[0].Tag as TranscriptHistoryEntry;

    private void RefreshEntries()
    {
        try
        {
            _entries = TranscriptHistoryStore.Load(_rootDirectory);
            _statusLabel.Text = _historyEnabled
                ? $"Local history · {_entries.Count} saved transcript(s)"
                : $"History recording is OFF · {_entries.Count} previous transcript(s) remain stored";
        }
        catch (Exception ex)
        {
            _entries = new List<TranscriptHistoryEntry>();
            _statusLabel.Text = "History could not be read. The file was not overwritten: " + ex.Message;
        }
        RefreshEntryList();
    }

    private void RefreshEntryList()
    {
        var query = _searchBox.Text.Trim();
        var previousId = SelectedEntry?.Id;
        _entriesList.BeginUpdate();
        try
        {
            _entriesList.Items.Clear();
            foreach (var entry in _entries)
            {
                if (query.Length > 0 && !entry.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !entry.Language.Contains(query, StringComparison.OrdinalIgnoreCase)
                    && !entry.Model.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;

                var preview = entry.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (preview.Length > 180) preview = preview[..177] + "…";
                var item = new ListViewItem(entry.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                item.SubItems.Add(entry.Language switch { "fa" => "Persian", "en" => "English", _ => "Auto" });
                item.SubItems.Add(entry.Model);
                item.SubItems.Add(preview);
                item.Tag = entry;
                _entriesList.Items.Add(item);
                if (previousId is not null && string.Equals(previousId, entry.Id, StringComparison.Ordinal)) item.Selected = true;
            }
        }
        finally { _entriesList.EndUpdate(); }
        if (_entriesList.SelectedItems.Count == 0 && _entriesList.Items.Count > 0) _entriesList.Items[0].Selected = true;
        UpdateSelectedPreview();
    }

    private void UpdateSelectedPreview()
    {
        var entry = SelectedEntry;
        _transcriptPreview.Text = entry?.Text ?? string.Empty;
        _useButton.Enabled = entry is not null;
        _copyButton.Enabled = entry is not null;
        _deleteButton.Enabled = entry is not null;
        _clearAllButton.Enabled = _entries.Count > 0;
    }

    private void UseSelectedTranscript()
    {
        if (SelectedEntry is not { } entry) return;
        SelectedTranscript = entry.Text;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CopySelectedTranscript()
    {
        if (SelectedEntry is not { } entry) return;
        try
        {
            Clipboard.SetText(entry.Text, TextDataFormat.UnicodeText);
            _statusLabel.Text = "Selected transcript copied to clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not copy the transcript.\n\n" + ex.Message, "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DeleteSelectedTranscript()
    {
        if (SelectedEntry is not { } entry) return;
        if (MessageBox.Show(this, "Delete this saved transcript? This cannot be undone.", "Delete transcript",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            TranscriptHistoryStore.Delete(_rootDirectory, entry.Id);
            RefreshEntries();
            _statusLabel.Text = "Transcript deleted.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not delete the transcript.\n\n" + ex.Message, "History", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ClearAllHistory()
    {
        if (_entries.Count == 0) return;
        if (MessageBox.Show(this, "Delete every transcript in MadNegar's local history? This does not delete exported TXT files.",
                "Clear transcript history", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            TranscriptHistoryStore.Clear(_rootDirectory);
            RefreshEntries();
            _statusLabel.Text = "All saved transcript history was deleted.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not clear history.\n\n" + ex.Message, "History", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
