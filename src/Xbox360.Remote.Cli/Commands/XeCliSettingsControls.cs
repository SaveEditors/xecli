using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Xbox360.Remote.Cli.Commands;

internal sealed class XeCliSettingsSelect : Control {
    private const int ArrowAreaWidth = 34;
    private const int OptionHeight = 32;
    private const int MaximumVisibleOptions = 10;

    private XeCliSettingsTheme theme = XeCliTerminalForm.GetSettingsTheme("cyan");
    private ToolStripDropDown? dropDown;
    private readonly List<ToolStripDropDown> retiredDropDowns = [];
    private int selectedIndex = -1;
    private bool hot;

    internal XeCliSettingsSelect() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.UserPaint, true);
        Dock = DockStyle.Fill;
        Margin = new Padding(0, 5, 0, 5);
        MinimumSize = new Size(120, 26);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ComboBox;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal List<string> Items { get; } = [];

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowThemeSwatches { get; set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<string, XeCliSettingsTheme>? ThemeResolver { get; set; }

    internal event EventHandler? SelectedIndexChanged;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int SelectedIndex {
        get => selectedIndex;
        set {
            int normalized = value >= 0 && value < Items.Count ? value : -1;
            if (selectedIndex == normalized)
                return;
            selectedIndex = normalized;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string? SelectedItem {
        get => selectedIndex >= 0 && selectedIndex < Items.Count ? Items[selectedIndex] : null;
        set {
            int index = value == null
                ? -1
                : Items.FindIndex(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
            SelectedIndex = index;
        }
    }

    internal void ApplyTheme(XeCliSettingsTheme nextTheme) {
        theme = nextTheme;
        BackColor = theme.FieldBackground;
        ForeColor = theme.PrimaryText;
        Invalidate();
    }

    internal void OpenDropDown() {
        if (Items.Count == 0 || dropDown != null)
            return;

        int columnCount = Math.Max(1, (int)Math.Ceiling(Items.Count / (double)MaximumVisibleOptions));
        int popupWidth = Math.Max(Width, Math.Min(720, columnCount * 220));
        int visibleOptions = Math.Min(Items.Count, MaximumVisibleOptions);
        int popupHeight = visibleOptions * OptionHeight + 8;
        int optionWidth = Math.Max(160, (popupWidth - 8) / columnCount);

        FlowLayoutPanel optionList = new() {
            AutoScroll = false,
            BackColor = theme.CardBackground,
            FlowDirection = FlowDirection.TopDown,
            Margin = Padding.Empty,
            Padding = new Padding(3),
            Size = new Size(popupWidth - 2, popupHeight - 2),
            WrapContents = columnCount > 1
        };
        for (int index = 0; index < Items.Count; index++) {
            SelectOption option = new(this, index) {
                Margin = Padding.Empty,
                Size = new Size(optionWidth, OptionHeight)
            };
            optionList.Controls.Add(option);
        }

        ToolStripControlHost host = new(optionList) {
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Size = optionList.Size
        };
        ToolStripDropDown popup = new() {
            AutoClose = true,
            AutoSize = false,
            BackColor = theme.Border,
            DropShadowEnabled = true,
            Margin = Padding.Empty,
            Padding = new Padding(1),
            Size = new Size(popupWidth, popupHeight)
        };
        popup.Items.Add(host);
        popup.Closed += (_, _) => {
            if (ReferenceEquals(dropDown, popup))
                dropDown = null;
            retiredDropDowns.Add(popup);
            if (IsHandleCreated && !IsDisposed && !Disposing) {
                BeginInvoke(new MethodInvoker(() => DisposeRetiredDropDown(popup)));
            }
            Focus();
            Invalidate();
        };
        dropDown = popup;
        popup.Show(this, new Point(0, Height), ToolStripDropDownDirection.BelowRight);
        Invalidate();
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            ToolStripDropDown? popup = dropDown;
            dropDown = null;
            popup?.Dispose();
            foreach (ToolStripDropDown retired in retiredDropDowns)
                retired.Dispose();
            retiredDropDowns.Clear();
        }
        base.Dispose(disposing);
    }

    protected override void OnMouseEnter(EventArgs e) {
        hot = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e) {
        hot = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) {
            Focus();
            OpenDropDown();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e) {
        if (e.KeyCode is Keys.Enter or Keys.Space or Keys.F4 || (e.Alt && e.KeyCode == Keys.Down)) {
            OpenDropDown();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Down && Items.Count > 0) {
            SelectedIndex = Math.Min(Items.Count - 1, Math.Max(0, SelectedIndex + 1));
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Up && Items.Count > 0) {
            SelectedIndex = Math.Max(0, SelectedIndex < 0 ? 0 : SelectedIndex - 1);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e) {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 2 || bounds.Height <= 2)
            return;
        bounds.Width--;
        bounds.Height--;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color field = hot || Focused || dropDown != null ? Mix(theme.FieldBackground, theme.Accent, 0.07f) : theme.FieldBackground;
        Color border = Focused || dropDown != null ? theme.Accent : theme.Border;
        using GraphicsPath path = CreateRoundedRectangle(bounds, 5);
        using (SolidBrush brush = new(field))
            e.Graphics.FillPath(brush, path);
        using (Pen pen = new(border, Focused || dropDown != null ? 1.4f : 1f) { Alignment = PenAlignment.Inset })
            e.Graphics.DrawPath(pen, path);

        Rectangle arrowArea = new(bounds.Right - ArrowAreaWidth + 1, bounds.Top + 1, ArrowAreaWidth - 1, bounds.Height - 1);
        using (SolidBrush brush = new(Mix(theme.FieldBackground, theme.Accent, hot ? 0.18f : 0.11f)))
            e.Graphics.FillRectangle(brush, arrowArea);
        using (Pen divider = new(theme.Border))
            e.Graphics.DrawLine(divider, arrowArea.Left, bounds.Top + 4, arrowArea.Left, bounds.Bottom - 4);

        int centerX = arrowArea.Left + arrowArea.Width / 2;
        int centerY = arrowArea.Top + arrowArea.Height / 2;
        using (Pen arrow = new(theme.Accent, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) {
            e.Graphics.DrawLine(arrow, centerX - 4, centerY - 2, centerX, centerY + 2);
            e.Graphics.DrawLine(arrow, centerX, centerY + 2, centerX + 4, centerY - 2);
        }

        int textLeft = 12;
        string? selected = SelectedItem;
        if (ShowThemeSwatches && selected != null && ThemeResolver != null) {
            XeCliSettingsTheme itemTheme = ThemeResolver(selected);
            Rectangle swatch = new(11, Math.Max(5, (Height - 10) / 2), 16, 10);
            using (SolidBrush brush = new(itemTheme.Accent))
                e.Graphics.FillRectangle(brush, swatch);
            using (Pen pen = new(itemTheme.Border))
                e.Graphics.DrawRectangle(pen, swatch);
            textLeft = 36;
        }

        Rectangle textBounds = new(textLeft, 1, Math.Max(1, arrowArea.Left - textLeft - 8), Height - 2);
        TextRenderer.DrawText(
            e.Graphics,
            selected ?? "Select...",
            Font,
            textBounds,
            selected == null ? theme.SecondaryText : theme.PrimaryText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private void SelectOptionAt(int index) {
        SelectedIndex = index;
        dropDown?.Close(ToolStripDropDownCloseReason.ItemClicked);
    }

    private void DisposeRetiredDropDown(ToolStripDropDown popup) {
        retiredDropDowns.Remove(popup);
        popup.Dispose();
    }

    private XeCliSettingsTheme GetTheme() => theme;

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius) {
        int diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        GraphicsPath path = new();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Mix(Color from, Color to, float amount) {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * amount),
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private sealed class SelectOption : Control {
        private readonly XeCliSettingsSelect owner;
        private readonly int index;
        private bool hot;

        internal SelectOption(XeCliSettingsSelect owner, int index) {
            this.owner = owner;
            this.index = index;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.ListItem;
            AccessibleName = owner.Items[index];
        }

        protected override void OnMouseEnter(EventArgs e) {
            hot = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e) {
            hot = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
                owner.SelectOptionAt(index);
        }

        protected override void OnPaint(PaintEventArgs e) {
            bool selected = owner.SelectedIndex == index;
            XeCliSettingsTheme palette = owner.GetTheme();
            Color background = selected
                ? Mix(palette.CardBackground, palette.Accent, 0.17f)
                : hot ? Mix(palette.CardBackground, palette.Accent, 0.09f) : palette.CardBackground;
            e.Graphics.Clear(background);
            if (selected) {
                using SolidBrush rail = new(palette.Accent);
                e.Graphics.FillRectangle(rail, 0, 4, 3, Math.Max(1, Height - 8));
            }

            int textLeft = 12;
            string item = owner.Items[index];
            if (owner.ShowThemeSwatches && owner.ThemeResolver != null) {
                XeCliSettingsTheme itemTheme = owner.ThemeResolver(item);
                Rectangle swatch = new(11, Math.Max(5, (Height - 10) / 2), 16, 10);
                using (SolidBrush brush = new(itemTheme.Accent))
                    e.Graphics.FillRectangle(brush, swatch);
                using (Pen pen = new(itemTheme.Border))
                    e.Graphics.DrawRectangle(pen, swatch);
                textLeft = 36;
            }
            Rectangle textBounds = new(textLeft, 0, Math.Max(1, Width - textLeft - 32), Height);
            TextRenderer.DrawText(
                e.Graphics,
                item,
                owner.Font,
                textBounds,
                selected ? palette.PrimaryText : palette.SecondaryText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            if (selected) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int x = Width - 19;
                int y = Height / 2;
                using Pen check = new(palette.Accent, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                e.Graphics.DrawLine(check, x - 4, y, x - 1, y + 3);
                e.Graphics.DrawLine(check, x - 1, y + 3, x + 5, y - 4);
            }
        }
    }
}

internal sealed class XeCliSettingsCheckBox : CheckBox {
    private XeCliSettingsTheme theme = XeCliTerminalForm.GetSettingsTheme("cyan");
    private bool hot;

    internal XeCliSettingsCheckBox() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        Appearance = Appearance.Normal;
        AutoCheck = true;
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
    }

    internal void ApplyTheme(XeCliSettingsTheme nextTheme) {
        theme = nextTheme;
        BackColor = theme.ShellBackground;
        ForeColor = theme.PrimaryText;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) {
        hot = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e) {
        hot = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnGotFocus(EventArgs e) {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e) {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int boxSize = 15;
        Rectangle box = new(1, Math.Max(1, (Height - boxSize) / 2), boxSize, boxSize);
        Color border = Focused || hot ? theme.Accent : theme.Border;
        using (SolidBrush field = new(Checked ? theme.Accent : theme.FieldBackground))
            e.Graphics.FillRectangle(field, box);
        using (Pen pen = new(border))
            e.Graphics.DrawRectangle(pen, box);

        if (Checked) {
            Color checkColor = GetContrastText(theme.Accent);
            int centerY = box.Top + box.Height / 2;
            using Pen check = new(checkColor, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawLine(check, box.Left + 3, centerY, box.Left + 6, centerY + 3);
            e.Graphics.DrawLine(check, box.Left + 6, centerY + 3, box.Right - 3, box.Top + 4);
        }

        Rectangle textBounds = new(25, 0, Math.Max(1, Width - 25), Height);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            textBounds,
            Enabled ? theme.PrimaryText : theme.SecondaryText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues) {
            Rectangle focus = Rectangle.Inflate(textBounds, -1, -4);
            ControlPaint.DrawFocusRectangle(e.Graphics, focus, theme.Accent, BackColor);
        }
    }

    private static Color GetContrastText(Color background) {
        double brightness = background.R * 0.299 + background.G * 0.587 + background.B * 0.114;
        return brightness >= 150 ? Color.FromArgb(8, 18, 22) : Color.White;
    }
}

internal sealed class XeCliSettingsNumeric : Control {
    private const int StepAreaWidth = 62;

    private readonly TextBox editor = new() {
        BorderStyle = BorderStyle.None,
        Multiline = false,
        TextAlign = HorizontalAlignment.Right
    };
    private XeCliSettingsTheme theme = XeCliTerminalForm.GetSettingsTheme("cyan");
    private decimal minimum;
    private decimal maximum = 100m;
    private decimal value;
    private decimal increment = 1m;
    private bool thousandsSeparator;
    private int hotStep;

    internal XeCliSettingsNumeric() {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.ContainerControl |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable |
            ControlStyles.UserPaint, true);
        MinimumSize = new Size(100, 26);
        Size = new Size(150, 28);
        Margin = new Padding(0, 5, 0, 5);
        Cursor = Cursors.IBeam;
        AccessibleRole = AccessibleRole.SpinButton;
        Controls.Add(editor);
        editor.KeyDown += EditorKeyDown;
        editor.Leave += (_, _) => CommitEditorText();
        editor.TextChanged += (_, _) => Invalidate();
        UpdateEditorText();
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal decimal Minimum {
        get => minimum;
        set {
            minimum = value;
            if (maximum < minimum)
                maximum = minimum;
            Value = this.value;
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal decimal Maximum {
        get => maximum;
        set {
            maximum = Math.Max(minimum, value);
            Value = this.value;
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal decimal Value {
        get => value;
        set {
            decimal normalized = Math.Clamp(value, minimum, maximum);
            if (this.value == normalized && editor.TextLength > 0)
                return;
            this.value = normalized;
            UpdateEditorText();
            ValueChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal decimal Increment {
        get => increment;
        set => increment = Math.Max(1m, value);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ThousandsSeparator {
        get => thousandsSeparator;
        set {
            thousandsSeparator = value;
            UpdateEditorText();
        }
    }

    internal event EventHandler? ValueChanged;

    internal void ApplyTheme(XeCliSettingsTheme nextTheme) {
        theme = nextTheme;
        BackColor = theme.FieldBackground;
        ForeColor = theme.PrimaryText;
        editor.BackColor = theme.FieldBackground;
        editor.ForeColor = theme.PrimaryText;
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e) {
        base.OnFontChanged(e);
        editor.Font = Font;
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs levent) {
        base.OnLayout(levent);
        int editorHeight = Math.Min(Height - 8, Math.Max(Font.Height + 2, 18));
        editor.Bounds = new Rectangle(8, Math.Max(3, (Height - editorHeight) / 2), Math.Max(1, Width - StepAreaWidth - 16), editorHeight);
    }

    protected override void OnEnter(EventArgs e) {
        base.OnEnter(e);
        if (!editor.Focused)
            editor.Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e) {
        int nextHotStep = 0;
        if (e.X >= Width - StepAreaWidth)
            nextHotStep = e.X < Width - StepAreaWidth / 2 ? -1 : 1;
        if (nextHotStep != hotStep) {
            hotStep = nextHotStep;
            Cursor = hotStep == 0 ? Cursors.IBeam : Cursors.Hand;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) {
        hotStep = 0;
        Cursor = Cursors.IBeam;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && e.X >= Width - StepAreaWidth) {
            Step(e.X < Width - StepAreaWidth / 2 ? -1 : 1);
            editor.Focus();
        }
    }

    protected override void OnPaint(PaintEventArgs e) {
        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 2 || bounds.Height <= 2)
            return;
        bounds.Width--;
        bounds.Height--;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        Color border = editor.Focused ? theme.Accent : theme.Border;
        using GraphicsPath path = CreateRoundedRectangle(bounds, 5);
        using (SolidBrush field = new(theme.FieldBackground))
            e.Graphics.FillPath(field, path);
        using (Pen pen = new(border, editor.Focused ? 1.4f : 1f) { Alignment = PenAlignment.Inset })
            e.Graphics.DrawPath(pen, path);

        Rectangle stepArea = new(bounds.Right - StepAreaWidth + 1, bounds.Top + 1, StepAreaWidth - 1, bounds.Height - 1);
        Rectangle decrementArea = new(stepArea.Left, stepArea.Top, stepArea.Width / 2, stepArea.Height);
        Rectangle incrementArea = new(decrementArea.Right, stepArea.Top, stepArea.Right - decrementArea.Right + 1, stepArea.Height);
        using (SolidBrush baseBrush = new(Mix(theme.FieldBackground, theme.Accent, 0.11f)))
            e.Graphics.FillRectangle(baseBrush, stepArea);
        if (hotStep != 0) {
            using SolidBrush hover = new(Mix(theme.FieldBackground, theme.Accent, 0.22f));
            e.Graphics.FillRectangle(hover, hotStep > 0 ? incrementArea : decrementArea);
        }
        using (Pen divider = new(theme.Border)) {
            e.Graphics.DrawLine(divider, stepArea.Left, bounds.Top + 4, stepArea.Left, bounds.Bottom - 4);
            e.Graphics.DrawLine(divider, decrementArea.Right, stepArea.Top + 4, decrementArea.Right, stepArea.Bottom - 4);
        }
        DrawStepGlyph(e.Graphics, decrementArea, add: false);
        DrawStepGlyph(e.Graphics, incrementArea, add: true);
    }

    private void EditorKeyDown(object? sender, KeyEventArgs e) {
        if (e.KeyCode == Keys.Up) {
            Step(1);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Down) {
            Step(-1);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Enter) {
            CommitEditorText();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void Step(int direction) {
        CommitEditorText();
        Value += direction * increment;
        editor.SelectAll();
    }

    private void CommitEditorText() {
        string candidate = editor.Text.Trim();
        NumberStyles styles = NumberStyles.Integer | NumberStyles.AllowThousands;
        if (decimal.TryParse(candidate, styles, CultureInfo.CurrentCulture, out decimal parsed) ||
            decimal.TryParse(candidate, styles, CultureInfo.InvariantCulture, out parsed)) {
            Value = parsed;
        }
        else {
            UpdateEditorText();
        }
    }

    private void UpdateEditorText() {
        string formatted = value.ToString(thousandsSeparator ? "N0" : "0", CultureInfo.CurrentCulture);
        if (!string.Equals(editor.Text, formatted, StringComparison.Ordinal))
            editor.Text = formatted;
    }

    private void DrawStepGlyph(Graphics graphics, Rectangle area, bool add) {
        int centerX = area.Left + area.Width / 2;
        int centerY = area.Top + area.Height / 2;
        using Pen pen = new(theme.Accent, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(pen, centerX - 4, centerY, centerX + 4, centerY);
        if (add)
            graphics.DrawLine(pen, centerX, centerY - 4, centerX, centerY + 4);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius) {
        int diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        GraphicsPath path = new();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Mix(Color from, Color to, float amount) {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * amount),
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }
}
