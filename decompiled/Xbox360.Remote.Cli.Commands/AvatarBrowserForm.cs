using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Xbox360.Remote.Cli.Avatar;
using Color = System.Drawing.Color;
using Panel = System.Windows.Forms.Panel;

namespace Xbox360.Remote.Cli.Commands;

internal sealed class AvatarBrowserForm : Form
{
	private readonly AvatarLibraryIndex _index;

	private readonly AvatarBrowseCommand.Settings _settings;

	private readonly AvatarCommandHelpers.AvatarResolvedPaths _paths;

	private readonly Dictionary<uint, AvatarTitleSummary> _titlesById;

	private readonly List<AvatarTitleSummary> _allTitles;

	private readonly Dictionary<uint, List<AvatarItemRecord>> _itemsByTitle;

	private readonly Dictionary<string, AvatarItemRecord> _itemsByContentId;

	private readonly HashSet<string> _selectedContentIds;

	private readonly HashSet<string> _allTags;

	private readonly string _currentUserLabel;

	private readonly string _modeLabel;

	private bool _refreshingTitles;

	private bool _refreshingItems;

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

	public IReadOnlyList<AvatarItemRecord> SelectedItems
	{
		get
		{
			AvatarItemRecord value;
			return (from contentId in _selectedContentIds
				select (!_itemsByContentId.TryGetValue(contentId, out value)) ? null : value into item
				where item != null
				select (item)).OrderBy<AvatarItemRecord, string>((AvatarItemRecord item) => item.TitleName, StringComparer.OrdinalIgnoreCase).ThenBy<AvatarItemRecord, string>((AvatarItemRecord item) => AvatarCommandHelpers.ResolveItemDisplayName(item), StringComparer.OrdinalIgnoreCase).ThenBy<AvatarItemRecord, string>((AvatarItemRecord item) => item.ContentId, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}
	}

	public AvatarBrowserForm(AvatarLibraryIndex index, AvatarCommandHelpers.AvatarResolvedPaths paths, AvatarBrowseCommand.Settings settings, AvatarCommandHelpers.AvatarResolvedOwnership? ownership, IReadOnlyCollection<AvatarItemRecord> initialSelection)
	{
		_index = index;
		_settings = settings;
		_paths = paths;
		_titlesById = index.Titles.ToDictionary((AvatarTitleSummary title) => title.TitleId);
		_allTitles = index.Titles.ToList();
		_itemsByTitle = (from item in index.Items
			group item by item.TitleId).ToDictionary((IGrouping<uint, AvatarItemRecord> group) => group.Key, (IGrouping<uint, AvatarItemRecord> group) => group.OrderBy<AvatarItemRecord, string>((AvatarItemRecord item) => AvatarCommandHelpers.ResolveItemDisplayName(item), StringComparer.OrdinalIgnoreCase).ToList());
		_itemsByContentId = index.Items.ToDictionary<AvatarItemRecord, string>((AvatarItemRecord item) => item.ContentId, StringComparer.OrdinalIgnoreCase);
		_selectedContentIds = new HashSet<string>(initialSelection.Select((AvatarItemRecord item) => item.ContentId), StringComparer.OrdinalIgnoreCase);
		_allTags = index.Items.SelectMany((AvatarItemRecord item) => item.Tags).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		_currentUserLabel = ((ownership == null) ? "Current user: unavailable" : ("Current user: " + (ownership.Gamertag ?? "unknown") + " | XUID: " + ownership.XuidText));
		_modeLabel = (paths.RemoteMode ? "Hosted avatar library" : "Local avatar collection");
		InitializeUi();
		ApplyInitialFilters();
	}

	private void InitializeUi()
	{
		Text = "XeCLI Avatar Browser";
		base.StartPosition = FormStartPosition.CenterScreen;
		MinimumSize = new Size(1260, 820);
		base.Size = new Size(1480, 920);
		BackColor = Color.FromArgb(11, 23, 11);
		ForeColor = Color.WhiteSmoke;
		Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			Padding = new Padding(12)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		Panel control = BuildHeaderPanel();
		TableLayoutPanel control2 = BuildSplitContainer();
		Panel control3 = BuildActionsPanel();
		tableLayoutPanel.Controls.Add(control, 0, 0);
		tableLayoutPanel.Controls.Add(control2, 0, 1);
		tableLayoutPanel.Controls.Add(control3, 0, 2);
		base.Controls.Add(tableLayoutPanel);
	}

	private Panel BuildHeaderPanel()
	{
		Panel obj = new Panel
		{
			Dock = DockStyle.Top,
			Height = 86,
			BackColor = Color.FromArgb(15, 30, 15),
			Padding = new Padding(16, 12, 16, 12)
		};
		Label value = new Label
		{
			Text = "Avatar Browser",
			AutoSize = true,
			Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold, GraphicsUnit.Point),
			ForeColor = Color.FromArgb(170, 255, 140),
			Location = new Point(0, 0)
		};
		_summaryLabel.AutoSize = true;
		_summaryLabel.Text = $"{_modeLabel} | {AvatarLibraryService.ListTitles(_index).Count} titles | {_index.Items.Count} items";
		_summaryLabel.ForeColor = Color.Gainsboro;
		_summaryLabel.Location = new Point(2, 40);
		_headerSelectionLabel.AutoSize = true;
		_headerSelectionLabel.ForeColor = Color.FromArgb(194, 255, 154);
		_headerSelectionLabel.Location = new Point(2, 58);
		Label value2 = new Label
		{
			AutoSize = true,
			Text = _currentUserLabel,
			ForeColor = Color.Gold,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right),
			Location = new Point(760, 10)
		};
		Label value3 = new Label
		{
			AutoSize = true,
			Text = (_paths.RemoteMode ? _paths.EffectiveManifestUrl : (_paths.EffectiveLibraryRoot ?? "local corpus")),
			ForeColor = Color.LightSkyBlue,
			Anchor = (AnchorStyles.Top | AnchorStyles.Right),
			Location = new Point(760, 34)
		};
		obj.Controls.Add(value);
		obj.Controls.Add(_summaryLabel);
		obj.Controls.Add(_headerSelectionLabel);
		obj.Controls.Add(value2);
		obj.Controls.Add(value3);
		return obj;
	}

	private TableLayoutPanel BuildSplitContainer()
	{
		TableLayoutPanel obj = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			BackColor = Color.FromArgb(20, 32, 20),
			ColumnStyles = 
			{
				new ColumnStyle(SizeType.Percent, 38f),
				new ColumnStyle(SizeType.Percent, 62f)
			},
			RowStyles = 
			{
				new RowStyle(SizeType.Percent, 100f)
			},
			Padding = new Padding(0)
		};
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(8),
			BackColor = Color.FromArgb(20, 32, 20)
		};
		Panel panel2 = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(8),
			BackColor = Color.FromArgb(20, 32, 20)
		};
		panel.Controls.Add(BuildTitlePane());
		panel2.Controls.Add(BuildItemPane());
		obj.Controls.Add(panel, 0, 0);
		obj.Controls.Add(panel2, 1, 0);
		return obj;
	}

	private Control BuildTitlePane()
	{
		TableLayoutPanel obj = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 4,
			BackColor = Color.FromArgb(20, 32, 20),
			RowStyles = 
			{
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.Percent, 100f)
			}
		};
		Label control = new Label
		{
			Text = "Game / title filter",
			AutoSize = true,
			ForeColor = Color.Gainsboro,
			Dock = DockStyle.Top
		};
		_titleSearchBox.Dock = DockStyle.Top;
		_titleSearchBox.BorderStyle = BorderStyle.FixedSingle;
		_titleSearchBox.BackColor = Color.FromArgb(28, 44, 28);
		_titleSearchBox.ForeColor = Color.WhiteSmoke;
		_titleSearchBox.TextChanged += delegate
		{
			RefreshTitles();
		};
		Label control2 = new Label
		{
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
		_titleList.SelectedIndexChanged += delegate
		{
			RefreshItems();
		};
		_titleList.DoubleClick += delegate
		{
			RefreshItems();
		};
		obj.Controls.Add(control, 0, 0);
		obj.Controls.Add(_titleSearchBox, 0, 1);
		obj.Controls.Add(control2, 0, 2);
		obj.Controls.Add(_titleList, 0, 3);
		return obj;
	}

	private Control BuildItemPane()
	{
		TableLayoutPanel obj = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 6,
			BackColor = Color.FromArgb(20, 32, 20),
			RowStyles = 
			{
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.AutoSize),
				new RowStyle(SizeType.Percent, 100f),
				new RowStyle(SizeType.AutoSize)
			}
		};
		Label control = new Label
		{
			Text = "Item search",
			AutoSize = true,
			ForeColor = Color.Gainsboro,
			Dock = DockStyle.Top
		};
		_itemSearchBox.Dock = DockStyle.Top;
		_itemSearchBox.BorderStyle = BorderStyle.FixedSingle;
		_itemSearchBox.BackColor = Color.FromArgb(28, 44, 28);
		_itemSearchBox.ForeColor = Color.WhiteSmoke;
		_itemSearchBox.TextChanged += delegate
		{
			RefreshItems();
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			WrapContents = false,
			BackColor = Color.FromArgb(20, 32, 20),
			Padding = new Padding(0, 8, 0, 8)
		};
		Label value = new Label
		{
			Text = "Tag",
			AutoSize = true,
			ForeColor = Color.Gainsboro,
			Margin = new Padding(0, 7, 8, 0)
		};
		_tagFilterBox.DropDownStyle = ComboBoxStyle.DropDownList;
		_tagFilterBox.Width = 180;
		_tagFilterBox.BackColor = Color.FromArgb(28, 44, 28);
		_tagFilterBox.ForeColor = Color.WhiteSmoke;
		_tagFilterBox.SelectedIndexChanged += delegate
		{
			RefreshItems();
		};
		flowLayoutPanel.Controls.Add(value);
		flowLayoutPanel.Controls.Add(_tagFilterBox);
		Label control2 = new Label
		{
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
		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			AutoSize = true,
			WrapContents = false,
			BackColor = Color.FromArgb(20, 32, 20),
			Padding = new Padding(0, 8, 0, 0)
		};
		_footerLabel.AutoSize = true;
		_footerLabel.ForeColor = Color.Gainsboro;
		_footerLabel.Text = "Select one title on the left, then check the items to install.";
		flowLayoutPanel2.Controls.Add(_footerLabel);
		obj.Controls.Add(control, 0, 0);
		obj.Controls.Add(_itemSearchBox, 0, 1);
		obj.Controls.Add(flowLayoutPanel, 0, 2);
		obj.Controls.Add(control2, 0, 3);
		obj.Controls.Add(_itemList, 0, 4);
		obj.Controls.Add(flowLayoutPanel2, 0, 5);
		return obj;
	}

	private Panel BuildActionsPanel()
	{
		Panel obj = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = 56,
			BackColor = Color.FromArgb(15, 30, 15),
			Padding = new Padding(0, 8, 0, 0)
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Right,
			AutoSize = true,
			WrapContents = false,
			BackColor = Color.FromArgb(15, 30, 15)
		};
		ConfigureButton(_selectAllButton, "Select All", SelectAllVisible);
		ConfigureButton(_clearButton, "Clear", ClearVisible);
		ConfigureButton(_installButton, "Install Selected", InstallSelected);
		ConfigureButton(_closeButton, "Close", delegate
		{
			base.DialogResult = DialogResult.Cancel;
			Close();
		});
		_installButton.Enabled = _selectedContentIds.Count > 0;
		_installButton.BackColor = Color.FromArgb(60, 120, 60);
		_installButton.ForeColor = Color.WhiteSmoke;
		_installButton.FlatAppearance.BorderColor = Color.FromArgb(120, 200, 120);
		flowLayoutPanel.Controls.Add(_selectAllButton);
		flowLayoutPanel.Controls.Add(_clearButton);
		flowLayoutPanel.Controls.Add(_installButton);
		flowLayoutPanel.Controls.Add(_closeButton);
		obj.Controls.Add(flowLayoutPanel);
		return obj;
	}

	private static void ConfigureButton(Button button, string text, EventHandler handler)
	{
		button.Text = text;
		button.AutoSize = true;
		button.Height = 32;
		button.Margin = new Padding(6, 0, 0, 0);
		button.FlatStyle = FlatStyle.Flat;
		button.FlatAppearance.BorderSize = 1;
		button.BackColor = Color.FromArgb(30, 52, 30);
		button.ForeColor = Color.WhiteSmoke;
		button.Click += handler;
	}

	private void ApplyInitialFilters()
	{
		_titleSearchBox.Text = ((!string.IsNullOrWhiteSpace(_settings.Game)) ? _settings.Game : string.Empty);
		_itemSearchBox.Text = ((!string.IsNullOrWhiteSpace(_settings.Search)) ? _settings.Search : string.Empty);
		List<string> list = new List<string>();
		list.Add("All Tags");
		list.AddRange(_allTags.OrderBy<string, string>((string tag) => tag, StringComparer.OrdinalIgnoreCase));
		_tagFilterBox.BeginUpdate();
		_tagFilterBox.Items.Clear();
		foreach (string item in list)
		{
			_tagFilterBox.Items.Add(item);
		}
		_tagFilterBox.EndUpdate();
		if (!string.IsNullOrWhiteSpace(_settings.Tag))
		{
			int num = _tagFilterBox.Items.IndexOf(_settings.Tag);
			_tagFilterBox.SelectedIndex = ((num >= 0) ? num : 0);
		}
		else
		{
			_tagFilterBox.SelectedIndex = 0;
		}
		RefreshTitles();
		if (!string.IsNullOrWhiteSpace(_settings.TitleId) && SaveHelpers.TryParseTitleId(_settings.TitleId, out var titleId) && _titlesById.TryGetValue(titleId, out AvatarTitleSummary value))
		{
			SelectTitle(value);
		}
		else if (_titleList.Items.Count > 0)
		{
			_titleList.Items[0].Selected = true;
		}
	}

	private void RefreshTitles()
	{
		if (_refreshingTitles)
		{
			return;
		}
		try
		{
			_refreshingTitles = true;
			uint? currentTitleId = GetSelectedTitleId();
			string text = _titleSearchBox.Text?.Trim() ?? string.Empty;
			IEnumerable<AvatarTitleSummary> enumerable = AvatarCommandHelpers.FilterTitles(_allTitles, text, int.MaxValue);
			if (SaveHelpers.TryParseTitleId(text, out var parsedTitleId))
			{
				enumerable = enumerable.Where((AvatarTitleSummary title) => title.TitleId == parsedTitleId);
			}
			_titleList.BeginUpdate();
			_titleList.Items.Clear();
			foreach (AvatarTitleSummary item in enumerable)
			{
				ListViewItem listViewItem = new ListViewItem(item.TitleName)
				{
					Tag = item
				};
				listViewItem.SubItems.Add($"0x{item.TitleId:X8}");
				listViewItem.SubItems.Add(item.ItemCount.ToString(CultureInfo.InvariantCulture));
				listViewItem.SubItems.Add(FtpHelpers.FormatBytes(item.TotalBytes));
				listViewItem.SubItems.Add((item.Publishers.Count == 0) ? "-" : string.Join(", ", item.Publishers.Take(2)));
				_titleList.Items.Add(listViewItem);
			}
			_titleList.EndUpdate();
			if (_titleList.Items.Count == 0)
			{
				_itemList.BeginUpdate();
				_itemList.Items.Clear();
				_itemList.EndUpdate();
				_selectionLabel.Text = "Selected: 0";
				return;
			}
			ListViewItem listViewItem2 = null;
			if (currentTitleId.HasValue)
			{
				listViewItem2 = _titleList.Items.Cast<ListViewItem>().FirstOrDefault((ListViewItem item) => ((AvatarTitleSummary)item.Tag).TitleId == currentTitleId.Value);
			}
			if (listViewItem2 == null)
			{
				listViewItem2 = _titleList.Items[0];
			}
			if (!listViewItem2.Selected)
			{
				listViewItem2.Selected = true;
			}
			listViewItem2.Focused = true;
			listViewItem2.EnsureVisible();
			RefreshItems();
		}
		finally
		{
			_refreshingTitles = false;
		}
	}

	private void RefreshItems()
	{
		if (_refreshingItems)
		{
			return;
		}
		try
		{
			_refreshingItems = true;
			AvatarTitleSummary selectedTitle = GetSelectedTitle();
			if (selectedTitle == null)
			{
				_itemList.BeginUpdate();
				_itemList.Items.Clear();
				_itemList.EndUpdate();
				_footerLabel.Text = "Select a title to view its avatar items.";
				_selectionLabel.Text = $"Selected: {_selectedContentIds.Count}";
				_installButton.Enabled = _selectedContentIds.Count > 0;
				return;
			}
			string search = _itemSearchBox.Text?.Trim() ?? string.Empty;
			string selectedTag = _tagFilterBox.SelectedItem as string;
			bool num = !string.IsNullOrWhiteSpace(selectedTag) && !string.Equals(selectedTag, "All Tags", StringComparison.OrdinalIgnoreCase);
			IEnumerable<AvatarItemRecord> enumerable2;
			if (!_itemsByTitle.TryGetValue(selectedTitle.TitleId, out List<AvatarItemRecord> value))
			{
				IEnumerable<AvatarItemRecord> enumerable = Array.Empty<AvatarItemRecord>();
				enumerable2 = enumerable;
			}
			else
			{
				IEnumerable<AvatarItemRecord> enumerable = value;
				enumerable2 = enumerable;
			}
			IEnumerable<AvatarItemRecord> source = enumerable2;
			if (!string.IsNullOrWhiteSpace(search))
			{
				source = source.Where((AvatarItemRecord item) => AvatarCommandHelpers.ResolveItemDisplayName(item).Contains(search, StringComparison.OrdinalIgnoreCase) || item.ContentId.Contains(search, StringComparison.OrdinalIgnoreCase) || item.TitleName.Contains(search, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(item.GameName) && item.GameName.Contains(search, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrWhiteSpace(item.Publisher) && item.Publisher.Contains(search, StringComparison.OrdinalIgnoreCase)));
			}
			if (num)
			{
				source = source.Where((AvatarItemRecord item) => item.Tags.Any((string tag) => string.Equals(tag, selectedTag, StringComparison.OrdinalIgnoreCase)));
			}
			List<AvatarItemRecord> list = source.ToList();
			_itemList.BeginUpdate();
			_itemList.ItemChecked -= ItemListOnItemChecked;
			_itemList.Items.Clear();
			foreach (AvatarItemRecord item in list)
			{
				ListViewItem listViewItem = new ListViewItem(AvatarCommandHelpers.ResolveItemDisplayName(item))
				{
					Tag = item,
					Checked = _selectedContentIds.Contains(item.ContentId)
				};
				listViewItem.SubItems.Add(item.ContentId);
				listViewItem.SubItems.Add(AvatarCommandHelpers.DescribeLayout(item));
				listViewItem.SubItems.Add(FtpHelpers.FormatBytes(item.SizeBytes));
				listViewItem.SubItems.Add(item.Publisher ?? "-");
				_itemList.Items.Add(listViewItem);
			}
			_itemList.ItemChecked += ItemListOnItemChecked;
			_itemList.EndUpdate();
			_footerLabel.Text = $"{selectedTitle.TitleName} | {list.Count} visible item(s)";
			_selectionLabel.Text = $"Selected: {_selectedContentIds.Count}";
			_installButton.Enabled = _selectedContentIds.Count > 0;
		}
		finally
		{
			_refreshingItems = false;
		}
	}

	private void SelectAllVisible(object? sender, EventArgs e)
	{
		foreach (ListViewItem item in _itemList.Items)
		{
			if (!item.Checked)
			{
				item.Checked = true;
			}
		}
		RefreshSelectionLabel();
	}

	private void ClearVisible(object? sender, EventArgs e)
	{
		foreach (ListViewItem item in _itemList.Items)
		{
			if (item.Checked)
			{
				item.Checked = false;
			}
		}
		RefreshSelectionLabel();
	}

	private void InstallSelected(object? sender, EventArgs e)
	{
		if (_selectedContentIds.Count == 0)
		{
			MessageBox.Show(this, "Select at least one avatar item first.", "XeCLI Avatar Browser", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		base.DialogResult = DialogResult.OK;
		Close();
	}

	private void ItemListOnItemChecked(object? sender, ItemCheckedEventArgs e)
	{
		if (!_refreshingItems && e.Item.Tag is AvatarItemRecord avatarItemRecord)
		{
			if (e.Item.Checked)
			{
				_selectedContentIds.Add(avatarItemRecord.ContentId);
			}
			else
			{
				_selectedContentIds.Remove(avatarItemRecord.ContentId);
			}
			RefreshSelectionLabel();
		}
	}

	private void RefreshSelectionLabel()
	{
		_selectionLabel.Text = $"Selected: {_selectedContentIds.Count}";
		_headerSelectionLabel.Text = $"Selected: {_selectedContentIds.Count}";
		_installButton.Enabled = _selectedContentIds.Count > 0;
	}

	private AvatarTitleSummary? GetSelectedTitle()
	{
		if (_titleList.SelectedItems.Count == 0)
		{
			return null;
		}
		return _titleList.SelectedItems[0].Tag as AvatarTitleSummary;
	}

	private uint? GetSelectedTitleId()
	{
		return GetSelectedTitle()?.TitleId;
	}

	private void SelectTitle(AvatarTitleSummary title)
	{
		foreach (ListViewItem item in _titleList.Items)
		{
			if (item.Tag is AvatarTitleSummary avatarTitleSummary && avatarTitleSummary.TitleId == title.TitleId)
			{
				item.Selected = true;
				item.Focused = true;
				item.EnsureVisible();
				break;
			}
		}
		RefreshItems();
	}
}
