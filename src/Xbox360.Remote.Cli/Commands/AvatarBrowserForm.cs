using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using XeCli.Localization;
using Xbox360.Remote;
using Xbox360.Remote.Cli.Avatar;
using Color = System.Drawing.Color;
using Timer = System.Windows.Forms.Timer;

namespace Xbox360.Remote.Cli.Commands;

internal sealed class AvatarBrowserForm : Form {
    private AvatarLibraryIndex _index;
    private readonly AvatarBrowseCommand.Settings _settings;
    private readonly AvatarCommandHelpers.AvatarResolvedPaths _paths;
    private Dictionary<uint, AvatarTitleSummary> _titlesById;
    private List<AvatarTitleSummary> _allTitles;
    private Dictionary<uint, List<AvatarItemRecord>> _itemsByTitle;
    private Dictionary<string, AvatarItemRecord> _itemsByContentId;
    private readonly HashSet<string> _selectedContentIds;
    private HashSet<string> _allTags;
    private readonly string _identityLabel;
    private readonly string _sourceLabel;
    private readonly bool _showSensitiveDetails;
    private readonly string _modeLabel;
    private bool _refreshingTitles;
    private bool _refreshingItems;
    private readonly Func<CancellationToken, Task<AvatarLibraryIndex>>? _catalogLoader;
    private bool _catalogLoadStarted;
    private bool _catalogLoaded;
    private string? _catalogLoadError;

    private readonly TextBox _titleSearchBox = new TextBox();
    private readonly ListView _titleList = new ListView();
    private readonly TextBox _itemSearchBox = new TextBox();
    private readonly ComboBox _tagFilterBox = new ComboBox();
    private readonly ListView _itemList = new ListView();
    private readonly Label _summaryLabel = new Label();
    private readonly Label _headerSelectionLabel = new Label();
    private readonly Label _selectionLabel = new Label();
    private readonly Label _footerLabel = new Label();
    private readonly Button _selectAllButton = new Button();
    private readonly Button _clearButton = new Button();
    private readonly Button _installButton = new Button();
    private readonly Button _closeButton = new Button();
    private bool _suppressFilterRefreshes;

    public AvatarBrowserForm(
        AvatarLibraryIndex index,
        AvatarCommandHelpers.AvatarResolvedPaths paths,
        AvatarBrowseCommand.Settings settings,
        AvatarCommandHelpers.AvatarResolvedOwnership? ownership,
        IReadOnlyCollection<AvatarItemRecord> initialSelection) : this(
            paths,
            settings,
            ownership,
            initialSelection,
            null) {
        ApplyCatalog(index);
        ApplyInitialFilters();
    }

    public AvatarBrowserForm(
        AvatarCommandHelpers.AvatarResolvedPaths paths,
        AvatarBrowseCommand.Settings settings,
        AvatarCommandHelpers.AvatarResolvedOwnership? ownership,
        IReadOnlyCollection<AvatarItemRecord> initialSelection,
        Func<CancellationToken, Task<AvatarLibraryIndex>>? catalogLoader) {
        _settings = settings;
        _paths = paths;
        _selectedContentIds = new HashSet<string>(initialSelection.Select(item => item.ContentId), StringComparer.OrdinalIgnoreCase);
        _titlesById = new Dictionary<uint, AvatarTitleSummary>();
        _allTitles = new List<AvatarTitleSummary>();
        _itemsByTitle = new Dictionary<uint, List<AvatarItemRecord>>();
        _itemsByContentId = new Dictionary<string, AvatarItemRecord>(StringComparer.OrdinalIgnoreCase);
        _allTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _showSensitiveDetails = settings.ShowSensitive;
        _identityLabel = BuildIdentityLabel(ownership, _showSensitiveDetails);
        _sourceLabel = BuildSourceLabel(paths, _showSensitiveDetails);
        _modeLabel = paths.RemoteMode ? "Hosted avatar library" : "Local avatar collection";
        _catalogLoader = catalogLoader;
        _index = CreateEmptyIndex();

        InitializeUi();
        SetLoadingState();
        Shown += async (_, _) => await LoadCatalogAsync();
        WinFormsLocalizer.Apply(this);
    }

    public IReadOnlyList<AvatarItemRecord> SelectedItems {
        get {
            return _selectedContentIds
                .Select(contentId => _itemsByContentId.TryGetValue(contentId, out AvatarItemRecord? item) ? item : null)
                .Where(item => item != null)
                .Select(item => item!)
                .OrderBy(item => item.TitleName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => AvatarCommandHelpers.ResolveItemDisplayName(item), StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ContentId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private static AvatarLibraryIndex CreateEmptyIndex() {
        return new AvatarLibraryIndex(
            new AvatarCollectionFingerprint(string.Empty, 0, 0, 0),
            DateTimeOffset.UtcNow,
            Array.Empty<AvatarItemRecord>());
    }

    private void SetLoadingState() {
        _summaryLabel.Text = $"{LocalizedText.Translate(_modeLabel)} | loading catalog...";
        _footerLabel.Text = LocalizedText.Translate("Loading avatar catalog...");
        _titleList.Items.Clear();
        _itemList.Items.Clear();
    }

    private void RefreshHeaderSummary() {
        _summaryLabel.Text = $"{LocalizedText.Translate(_modeLabel)} | {AvatarLibraryService.ListTitles(_index).Count} {LocalizedText.Translate("titles")} | {_index.Items.Count} {LocalizedText.Translate("items")}";
    }

    private void ApplyCatalog(AvatarLibraryIndex index) {
        _index = index;
        _titlesById = index.Titles.ToDictionary(title => title.TitleId);
        _allTitles = index.Titles.ToList();
        _itemsByTitle = index.Items
            .GroupBy(item => item.TitleId)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => AvatarCommandHelpers.ResolveItemDisplayName(item), StringComparer.OrdinalIgnoreCase).ToList());
        _itemsByContentId = index.Items.ToDictionary(item => item.ContentId, StringComparer.OrdinalIgnoreCase);
        _allTags = index.Items.SelectMany(item => item.Tags).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _catalogLoaded = true;
        _catalogLoadError = null;
        RefreshHeaderSummary();
    }

    private void ApplySettingsSelection() {
        if (_selectedContentIds.Count > 0) {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_settings.ContentId)) {
            uint? titleId = null;
            if (!string.IsNullOrWhiteSpace(_settings.TitleId) && SaveHelpers.TryParseTitleId(_settings.TitleId, out uint parsedContentTitleId)) {
                titleId = parsedContentTitleId;
            }

            AvatarItemRecord? item = AvatarLibraryService.FindByContentId(_index, _settings.ContentId, titleId);
            if (item != null) {
                _selectedContentIds.Add(item.ContentId);
                return;
            }
        }

        if (_settings.All && !string.IsNullOrWhiteSpace(_settings.TitleId) && SaveHelpers.TryParseTitleId(_settings.TitleId, out uint parsedTitleId)) {
            foreach (AvatarItemRecord item in AvatarLibraryService.SearchItems(_index, new AvatarItemQuery(TitleId: parsedTitleId, Limit: int.MaxValue))) {
                _selectedContentIds.Add(item.ContentId);
            }
        }
    }

    private async Task LoadCatalogAsync() {
        if (_catalogLoadStarted || _catalogLoader == null) {
            if (_catalogLoader == null && !_catalogLoaded) {
                SetLoadingState();
            }
            return;
        }

        _catalogLoadStarted = true;
        SetLoadingState();
        try {
            AvatarLibraryIndex index = await _catalogLoader(CancellationToken.None);
            if (IsDisposed) {
                return;
            }

            ApplyCatalog(index);
            ApplySettingsSelection();
            ApplyInitialFilters();
        }
        catch (Exception ex) {
            if (IsDisposed) {
                return;
            }

            _catalogLoadError = ex.Message;
            _footerLabel.Text = $"Avatar catalog load failed: {ex.Message}";
        }
    }

    private void InitializeUi() {
        Text = "XeCLI Avatar Browser";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1260, 820);
        Size = new Size(1480, 920);
        BackColor = Color.FromArgb(11, 23, 11);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        TableLayoutPanel root = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Panel header = BuildHeaderPanel();
        TableLayoutPanel split = BuildSplitContainer();
        Panel actions = BuildActionsPanel();

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(split, 0, 1);
        root.Controls.Add(actions, 0, 2);
        Controls.Add(root);
    }

    private Panel BuildHeaderPanel() {
        Panel header = new Panel {
            Dock = DockStyle.Top,
            Height = 86,
            BackColor = Color.FromArgb(15, 30, 15),
            Padding = new Padding(16, 12, 16, 12)
        };

        Label title = new Label {
            Text = "Avatar Browser",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(170, 255, 140),
            Location = new Point(0, 0)
        };

        _summaryLabel.AutoSize = true;
        _summaryLabel.Text = $"{LocalizedText.Translate(_modeLabel)} | {AvatarLibraryService.ListTitles(_index).Count} {LocalizedText.Translate("titles")} | {_index.Items.Count} {LocalizedText.Translate("items")}";
        _summaryLabel.ForeColor = Color.Gainsboro;
        _summaryLabel.Location = new Point(2, 40);

        _headerSelectionLabel.AutoSize = true;
        _headerSelectionLabel.ForeColor = Color.FromArgb(194, 255, 154);
        _headerSelectionLabel.Location = new Point(2, 58);

        Label user = new Label {
            Text = _identityLabel,
            ForeColor = Color.Gold,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleRight,
            Size = new Size(460, 20),
            Location = new Point(730, 10)
        };

        Label source = new Label {
            Text = _sourceLabel,
            ForeColor = Color.LightSkyBlue,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleRight,
            Size = new Size(460, 20),
            Location = new Point(730, 34)
        };

        header.Controls.Add(title);
        header.Controls.Add(_summaryLabel);
        header.Controls.Add(_headerSelectionLabel);
        header.Controls.Add(user);
        header.Controls.Add(source);
        return header;
    }

    private static string BuildIdentityLabel(AvatarCommandHelpers.AvatarResolvedOwnership? ownership, bool showSensitiveDetails) {
        if (ownership == null) {
            return "Current user: unavailable";
        }

        if (showSensitiveDetails) {
            return $"Current user: {ownership.Gamertag ?? "unknown"} | XUID: {ownership.XuidText}";
        }

        return "Current user: signed in";
    }

    private static string BuildSourceLabel(AvatarCommandHelpers.AvatarResolvedPaths paths, bool showSensitiveDetails) {
        if (showSensitiveDetails) {
            return paths.RemoteMode
                ? $"Source: {paths.EffectiveManifestUrl}"
                : $"Source: {paths.EffectiveLibraryRoot ?? "local library"}";
        }

        return paths.RemoteMode ? "Source: remote library" : "Source: local library";
    }

    private TableLayoutPanel BuildSplitContainer() {
        TableLayoutPanel split = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(20, 32, 20)
        };

        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        split.Padding = new Padding(0);

        Panel left = new Panel {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(20, 32, 20)
        };
        Panel right = new Panel {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(20, 32, 20)
        };

        left.Controls.Add(BuildTitlePane());
        right.Controls.Add(BuildItemPane());
        split.Controls.Add(left, 0, 0);
        split.Controls.Add(right, 1, 0);
        return split;
    }

    private Control BuildTitlePane() {
        TableLayoutPanel pane = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.FromArgb(20, 32, 20)
        };
        pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pane.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Label searchLabel = new Label {
            Text = "Game / title filter",
            AutoSize = true,
            ForeColor = Color.Gainsboro,
            Dock = DockStyle.Top
        };

        _titleSearchBox.Dock = DockStyle.Top;
        _titleSearchBox.BorderStyle = BorderStyle.FixedSingle;
        _titleSearchBox.BackColor = Color.FromArgb(28, 44, 28);
        _titleSearchBox.ForeColor = Color.WhiteSmoke;
        _titleSearchBox.TextChanged += (_, _) => RefreshTitles();

        Label listLabel = new Label {
            Text = "Titles",
            AutoSize = true,
            ForeColor = Color.Gainsboro,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 10, 0, 4)
        };

        _titleList.Dock = DockStyle.Fill;
        _titleList.View = View.Details;
        _titleList.FullRowSelect = true;
        _titleList.GridLines = false;
        _titleList.MultiSelect = false;
        _titleList.HideSelection = false;
        _titleList.CheckBoxes = false;
        _titleList.BackColor = Color.FromArgb(15, 25, 15);
        _titleList.ForeColor = Color.WhiteSmoke;
        _titleList.BorderStyle = BorderStyle.FixedSingle;
        _titleList.Columns.Add("Game", 260);
        _titleList.Columns.Add("Title ID", 105);
        _titleList.Columns.Add("Items", 70);
        _titleList.Columns.Add("Size", 90);
        _titleList.Columns.Add("Publisher", 150);
        _titleList.SelectedIndexChanged += (_, _) => RefreshItems();
        _titleList.DoubleClick += (_, _) => RefreshItems();

        pane.Controls.Add(searchLabel, 0, 0);
        pane.Controls.Add(_titleSearchBox, 0, 1);
        pane.Controls.Add(listLabel, 0, 2);
        pane.Controls.Add(_titleList, 0, 3);
        return pane;
    }

    private Control BuildItemPane() {
        TableLayoutPanel pane = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Color.FromArgb(20, 32, 20)
        };
        pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pane.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label searchLabel = new Label {
            Text = "Item search",
            AutoSize = true,
            ForeColor = Color.Gainsboro,
            Dock = DockStyle.Top
        };

        _itemSearchBox.Dock = DockStyle.Top;
        _itemSearchBox.BorderStyle = BorderStyle.FixedSingle;
        _itemSearchBox.BackColor = Color.FromArgb(28, 44, 28);
        _itemSearchBox.ForeColor = Color.WhiteSmoke;
        _itemSearchBox.TextChanged += (_, _) => RefreshItems();

        FlowLayoutPanel filterRow = new FlowLayoutPanel {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.FromArgb(20, 32, 20),
            Padding = new Padding(0, 8, 0, 8)
        };

        Label tagLabel = new Label {
            Text = "Tag",
            AutoSize = true,
            ForeColor = Color.Gainsboro,
            Margin = new Padding(0, 7, 8, 0)
        };

        _tagFilterBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _tagFilterBox.Width = 180;
        _tagFilterBox.BackColor = Color.FromArgb(28, 44, 28);
        _tagFilterBox.ForeColor = Color.WhiteSmoke;
        _tagFilterBox.SelectedIndexChanged += (_, _) => RefreshItems();

        filterRow.Controls.Add(tagLabel);
        filterRow.Controls.Add(_tagFilterBox);

        Label listLabel = new Label {
            Text = "Avatar items",
            AutoSize = true,
            ForeColor = Color.Gainsboro,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 4)
        };

        _itemList.Dock = DockStyle.Fill;
        _itemList.View = View.Details;
        _itemList.FullRowSelect = true;
        _itemList.GridLines = false;
        _itemList.CheckBoxes = true;
        _itemList.MultiSelect = true;
        _itemList.HideSelection = false;
        _itemList.BackColor = Color.FromArgb(15, 25, 15);
        _itemList.ForeColor = Color.WhiteSmoke;
        _itemList.BorderStyle = BorderStyle.FixedSingle;
        _itemList.Columns.Add("Item", 360);
        _itemList.Columns.Add("Content ID", 310);
        _itemList.Columns.Add("Layout", 95);
        _itemList.Columns.Add("Size", 100);
        _itemList.Columns.Add("Publisher", 220);
        _itemList.ItemChecked += ItemListOnItemChecked;

        FlowLayoutPanel statusRow = new FlowLayoutPanel {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.FromArgb(20, 32, 20),
            Padding = new Padding(0, 8, 0, 0)
        };

        _footerLabel.AutoSize = true;
        _footerLabel.ForeColor = Color.Gainsboro;
        _footerLabel.Text = "Select one title on the left, then check the items to install.";
        statusRow.Controls.Add(_footerLabel);

        pane.Controls.Add(searchLabel, 0, 0);
        pane.Controls.Add(_itemSearchBox, 0, 1);
        pane.Controls.Add(filterRow, 0, 2);
        pane.Controls.Add(listLabel, 0, 3);
        pane.Controls.Add(_itemList, 0, 4);
        pane.Controls.Add(statusRow, 0, 5);
        return pane;
    }

    private Panel BuildActionsPanel() {
        Panel actions = new Panel {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = Color.FromArgb(15, 30, 15),
            Padding = new Padding(0, 8, 0, 0)
        };

        FlowLayoutPanel buttons = new FlowLayoutPanel {
            Dock = DockStyle.Right,
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.FromArgb(15, 30, 15)
        };

        ConfigureButton(_selectAllButton, "Select All", SelectAllVisible);
        ConfigureButton(_clearButton, "Clear", ClearVisible);
        ConfigureButton(_installButton, "Install Selected", InstallSelected);
        ConfigureButton(_closeButton, "Close", (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        _installButton.Enabled = _selectedContentIds.Count > 0;
        _installButton.BackColor = Color.FromArgb(60, 120, 60);
        _installButton.ForeColor = Color.WhiteSmoke;
        _installButton.FlatAppearance.BorderColor = Color.FromArgb(120, 200, 120);

        buttons.Controls.Add(_selectAllButton);
        buttons.Controls.Add(_clearButton);
        buttons.Controls.Add(_installButton);
        buttons.Controls.Add(_closeButton);
        actions.Controls.Add(buttons);
        return actions;
    }

    private static void ConfigureButton(Button button, string text, EventHandler handler) {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 32;
        button.Margin = new Padding(6, 0, 0, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = Color.FromArgb(30, 52, 30);
        button.ForeColor = Color.WhiteSmoke;
        button.UseVisualStyleBackColor = false;
        button.EnabledChanged += (_, _) => {
            button.ForeColor = button.Enabled ? Color.WhiteSmoke : Color.FromArgb(220, 223, 220);
            button.BackColor = button.Enabled ? Color.FromArgb(30, 52, 30) : Color.FromArgb(18, 28, 18);
        };
        button.Click += handler;
    }

    private void ApplyInitialFilters() {
        _suppressFilterRefreshes = true;
        try {
            _titleSearchBox.Text = !string.IsNullOrWhiteSpace(_settings.Game) ? _settings.Game : string.Empty;
            _itemSearchBox.Text = !string.IsNullOrWhiteSpace(_settings.Search) ? _settings.Search : string.Empty;

            List<string> tags = new List<string> { LocalizedText.Translate("All Tags") };
            tags.AddRange(_allTags.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase));
            _tagFilterBox.BeginUpdate();
            _tagFilterBox.Items.Clear();
            foreach (string tag in tags)
                _tagFilterBox.Items.Add(tag);
            _tagFilterBox.EndUpdate();
            if (!string.IsNullOrWhiteSpace(_settings.Tag)) {
                int tagIndex = _tagFilterBox.Items.IndexOf(_settings.Tag);
                _tagFilterBox.SelectedIndex = tagIndex >= 0 ? tagIndex : 0;
            }
            else {
                _tagFilterBox.SelectedIndex = 0;
            }
        }
        finally {
            _suppressFilterRefreshes = false;
        }
        RefreshTitles();

        if (!string.IsNullOrWhiteSpace(_settings.TitleId) &&
            SaveHelpers.TryParseTitleId(_settings.TitleId, out uint parsedTitleId) &&
            _titlesById.TryGetValue(parsedTitleId, out AvatarTitleSummary? match)) {
            SelectTitle(match);
        }
        else if (_titleList.Items.Count > 0) {
            _titleList.Items[0].Selected = true;
        }
    }

    private void RefreshTitles() {
        if (_suppressFilterRefreshes || _refreshingTitles)
            return;

        if (!_catalogLoaded) {
            SetLoadingState();
            return;
        }

        try {
            _refreshingTitles = true;
            uint? currentTitleId = GetSelectedTitleId();
            string filter = _titleSearchBox.Text?.Trim() ?? string.Empty;
            IEnumerable<AvatarTitleSummary> filtered = AvatarCommandHelpers.FilterTitles(_allTitles, filter, int.MaxValue);
            if (SaveHelpers.TryParseTitleId(filter, out uint parsedTitleId))
                filtered = filtered.Where(title => title.TitleId == parsedTitleId);

            _titleList.BeginUpdate();
            _titleList.Items.Clear();
            foreach (AvatarTitleSummary title in filtered) {
                ListViewItem row = new ListViewItem(title.TitleName) {
                    Tag = title
                };
                row.SubItems.Add($"0x{title.TitleId:X8}");
                row.SubItems.Add(title.ItemCount.ToString(CultureInfo.InvariantCulture));
                row.SubItems.Add(FtpHelpers.FormatBytes(title.TotalBytes));
                row.SubItems.Add(title.Publishers.Count == 0 ? "-" : string.Join(", ", title.Publishers.Take(2)));
                _titleList.Items.Add(row);
            }
            _titleList.EndUpdate();

            if (_titleList.Items.Count == 0) {
                _itemList.BeginUpdate();
                _itemList.Items.Clear();
                _itemList.EndUpdate();
                _footerLabel.Text = LocalizedText.Translate("No titles match this filter.");
                RefreshSelectionLabel();
                return;
            }

            ListViewItem? selectedRow = null;
            if (currentTitleId.HasValue) {
                selectedRow = _titleList.Items.Cast<ListViewItem>()
                    .FirstOrDefault(item => ((AvatarTitleSummary) item.Tag!).TitleId == currentTitleId.Value);
            }

            if (selectedRow == null)
                selectedRow = _titleList.Items[0];

            if (!selectedRow.Selected)
                selectedRow.Selected = true;

            selectedRow.Focused = true;
            selectedRow.EnsureVisible();
            RefreshItems();
        }
        finally {
            _refreshingTitles = false;
        }
    }

    private void RefreshItems() {
        if (_suppressFilterRefreshes || _refreshingItems)
            return;

        if (!_catalogLoaded) {
            SetLoadingState();
            return;
        }

        try {
            _refreshingItems = true;
            AvatarTitleSummary? selectedTitle = GetSelectedTitle();
            if (selectedTitle == null) {
                _itemList.BeginUpdate();
                _itemList.Items.Clear();
                _itemList.EndUpdate();
                _footerLabel.Text = LocalizedText.Translate("Select a title to view its avatar items.");
                RefreshSelectionLabel();
                return;
            }

            string search = _itemSearchBox.Text?.Trim() ?? string.Empty;
            string? selectedTag = _tagFilterBox.SelectedItem as string;
            bool useTag = !string.IsNullOrWhiteSpace(selectedTag) &&
                          !string.Equals(selectedTag, LocalizedText.Translate("All Tags"), StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(selectedTag, "All Tags", StringComparison.OrdinalIgnoreCase);

            IEnumerable<AvatarItemRecord> items = _itemsByTitle.TryGetValue(selectedTitle.TitleId, out List<AvatarItemRecord>? titleItems)
                ? titleItems
                : Array.Empty<AvatarItemRecord>();

            if (!string.IsNullOrWhiteSpace(search)) {
                items = items.Where(item =>
                    AvatarCommandHelpers.ResolveItemDisplayName(item).Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    item.ContentId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    item.TitleName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(item.GameName) && item.GameName.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(item.Publisher) && item.Publisher.Contains(search, StringComparison.OrdinalIgnoreCase)));
            }

            if (useTag) {
                items = items.Where(item => item.Tags.Any(tag => string.Equals(tag, selectedTag, StringComparison.OrdinalIgnoreCase)));
            }

            List<AvatarItemRecord> filtered = items.ToList();
            _itemList.BeginUpdate();
            _itemList.ItemChecked -= ItemListOnItemChecked;
            _itemList.Items.Clear();
            foreach (AvatarItemRecord item in filtered) {
                ListViewItem row = new ListViewItem(AvatarCommandHelpers.ResolveItemDisplayName(item)) {
                    Tag = item,
                    Checked = _selectedContentIds.Contains(item.ContentId)
                };
                row.SubItems.Add(item.ContentId);
                row.SubItems.Add(AvatarCommandHelpers.DescribeLayout(item));
                row.SubItems.Add(FtpHelpers.FormatBytes(item.SizeBytes));
                row.SubItems.Add(item.Publisher ?? "-");
                _itemList.Items.Add(row);
            }
            _itemList.ItemChecked += ItemListOnItemChecked;
            _itemList.EndUpdate();

            _footerLabel.Text = $"{selectedTitle.TitleName} | {BuildVisibleItemsText(filtered.Count)}";
            _selectionLabel.Text = BuildSelectedText(_selectedContentIds.Count);
            _installButton.Enabled = _selectedContentIds.Count > 0;
        }
        finally {
            _refreshingItems = false;
        }
    }

    private void SelectAllVisible(object? sender, EventArgs e) {
        foreach (ListViewItem item in _itemList.Items) {
            if (!item.Checked)
                item.Checked = true;
        }
        RefreshSelectionLabel();
    }

    private void ClearVisible(object? sender, EventArgs e) {
        foreach (ListViewItem item in _itemList.Items) {
            if (item.Checked)
                item.Checked = false;
        }
        RefreshSelectionLabel();
    }

    private void InstallSelected(object? sender, EventArgs e) {
        if (_selectedContentIds.Count == 0) {
            MessageBox.Show(this, LocalizedText.Translate("Select at least one avatar item first."), LocalizedText.Translate("XeCLI Avatar Browser"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void ItemListOnItemChecked(object? sender, ItemCheckedEventArgs e) {
        if (_refreshingItems || e.Item.Tag is not AvatarItemRecord item)
            return;

        if (e.Item.Checked)
            _selectedContentIds.Add(item.ContentId);
        else
            _selectedContentIds.Remove(item.ContentId);

        RefreshSelectionLabel();
    }

    private void RefreshSelectionLabel() {
        _selectionLabel.Text = BuildSelectedText(_selectedContentIds.Count);
        _headerSelectionLabel.Text = BuildSelectedText(_selectedContentIds.Count);
        _installButton.Enabled = _selectedContentIds.Count > 0;
    }

    private static string BuildSelectedText(int count) {
        return LocalizedText.Translate("Selected") + ": " + count.ToString(CultureInfo.InvariantCulture);
    }

    private static string BuildVisibleItemsText(int count) {
        return count.ToString(CultureInfo.InvariantCulture) + " " + LocalizedText.Translate("items visible");
    }

    private AvatarTitleSummary? GetSelectedTitle() {
        if (_titleList.SelectedItems.Count == 0)
            return null;
        return _titleList.SelectedItems[0].Tag as AvatarTitleSummary;
    }

    private uint? GetSelectedTitleId() {
        return GetSelectedTitle()?.TitleId;
    }

    private void SelectTitle(AvatarTitleSummary title) {
        foreach (ListViewItem item in _titleList.Items) {
            if (item.Tag is AvatarTitleSummary summary && summary.TitleId == title.TitleId) {
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
                break;
            }
        }
        RefreshItems();
    }
}
