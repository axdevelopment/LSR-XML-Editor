using LsrXmlEditor.Core.Storage;
using LsrXmlEditor.Core.Themes;
using LsrXmlEditor.Infrastructure;

namespace LsrXmlEditor.Forms;

public sealed class OptionsForm : Form
{
    private static readonly ThemePalette ClassicPreview = new(
        ThemeCatalog.LightId, "Light", false, ThemeCatalog.DefaultFont,
        Color.White, Color.FromArgb(240, 240, 240), Color.White, Color.Black, Color.FromArgb(96, 96, 96), Color.FromArgb(200, 200, 200),
        Color.FromArgb(0, 120, 215), Color.FromArgb(0, 120, 215), Color.White, Color.FromArgb(217, 217, 217), Color.FromArgb(229, 243, 255),
        Color.FromArgb(240, 240, 240), Color.Firebrick);

    private static readonly string[] Rows = ["AMBIENT_GANG_LOST", "AMBIENT_GANG_BALLAS", "AMBIENT_GANG_FAMILY"];
    private static readonly string[] Names = ["The Lost MC", "Ballas", "The Families"];

    private readonly AppSettings _settings;
    private readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 12 };
    private readonly ComboBox _size = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Panel _preview = new() { Margin = new Padding(0, 4, 0, 8) };
    private Font? _previewFont;
    private Font? _previewBold;

    public OptionsForm(AppSettings settings)
    {
        _settings = settings;
        Font = UiTheme.Font;
        Text = "Options";
        Icon = StockImages.AppIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _theme.Items.AddRange([.. ThemeCatalog.Choices.Select(c => c.Name)]);
        _theme.SelectedIndex = Math.Max(0, ThemeCatalog.Choices.ToList().FindIndex(c => string.Equals(c.Id, settings.Theme, StringComparison.OrdinalIgnoreCase)));
        _size.Items.AddRange([.. UiTheme.TextSizes.Select(s => s == AppSettings.DefaultTextSize ? $"{s} % (default)" : $"{s} %")]);
        _size.SelectedIndex = Math.Max(0, UiTheme.TextSizes.ToList().IndexOf(settings.TextSize));
        _theme.Width = _size.Width = LogicalToDeviceUnits(UiTheme.Grow(240));
        _preview.Size = new Size(LogicalToDeviceUnits(UiTheme.Grow(480)), LogicalToDeviceUnits(UiTheme.Grow(176)));
        _preview.Paint += (_, e) => PaintPreview(e.Graphics);
        _theme.SelectedIndexChanged += (_, _) => UpdatePreviewFont();
        _size.SelectedIndexChanged += (_, _) => UpdatePreviewFont();
        UpdatePreviewFont();

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(16, 14, 16, 8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var heading = new Label { Text = "Appearance", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) };
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 2);
        layout.Controls.Add(Caption("&Theme:"), 0, 1);
        layout.Controls.Add(_theme, 1, 1);
        layout.Controls.Add(Caption("Text &size:"), 0, 2);
        layout.Controls.Add(_size, 1, 2);
        var previewCaption = new Label { Text = "Preview", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 12, 0, 0) };
        layout.Controls.Add(previewCaption, 0, 3);
        layout.SetColumnSpan(previewCaption, 2);
        layout.Controls.Add(_preview, 0, 4);
        layout.SetColumnSpan(_preview, 2);
        var note = new Label
        {
            Text = "Apply switches the look right away – open files, unsaved changes and undo stay as they are.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(_preview.Width, 0)
        };
        layout.Controls.Add(note, 0, 5);
        layout.SetColumnSpan(note, 2);

        var ok = new Button { Text = "&Apply", AutoSize = true, MinimumSize = new Size(LogicalToDeviceUnits(80), 0) };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, MinimumSize = new Size(LogicalToDeviceUnits(80), 0) };
        ok.Click += (_, _) => Accept();
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 12) };
        buttons.Controls.AddRange([cancel, ok]);

        var outer = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        outer.Controls.Add(layout, 0, 0);
        outer.Controls.Add(buttons, 0, 1);
        Controls.Add(outer);
        AcceptButton = ok;
        CancelButton = cancel;
        Disposed += (_, _) =>
        {
            heading.Font.Dispose();
            _previewFont?.Dispose();
            _previewBold?.Dispose();
        };
        UiTheme.Attach(this);
    }

    public bool Changed { get; private set; }

    private string SelectedTheme => ThemeCatalog.Choices[Math.Max(0, _theme.SelectedIndex)].Id;

    private int SelectedSize => UiTheme.TextSizes[Math.Max(0, _size.SelectedIndex)];

    private ThemePalette PreviewPalette => UiTheme.Resolve(SelectedTheme) ?? ClassicPreview;

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 12, 4) };

    private void Accept()
    {
        Changed = !string.Equals(SelectedTheme, _settings.Theme, StringComparison.OrdinalIgnoreCase) || SelectedSize != _settings.TextSize;
        _settings.Theme = SelectedTheme;
        _settings.TextSize = SelectedSize;
        DialogResult = DialogResult.OK;
    }

    private void UpdatePreviewFont()
    {
        _previewFont?.Dispose();
        _previewBold?.Dispose();
        var name = UiTheme.IsInstalled(PreviewPalette.FontName) ? PreviewPalette.FontName : ThemeCatalog.DefaultFont;
        _previewFont = new Font(name, UiTheme.BaseFontSize * SelectedSize / 100f);
        _previewBold = new Font(_previewFont, FontStyle.Bold);
        _preview.Invalidate();
    }

    private void PaintPreview(Graphics graphics)
    {
        if (_previewFont is null || _previewBold is null)
        {
            return;
        }

        var palette = PreviewPalette;
        var width = _preview.ClientSize.Width;
        var height = _preview.ClientSize.Height;
        var padding = LogicalToDeviceUnits(10);
        var line = _previewFont.Height + LogicalToDeviceUnits(6);
        var column = width / 2;
        graphics.Clear(palette.Window);

        Fill(graphics, palette.Surface, new Rectangle(0, 0, width, line + padding / 2));
        TextRenderer.DrawText(graphics, "Gangs.xml", _previewBold, new Point(padding, padding / 2), palette.Text);
        TextRenderer.DrawText(graphics, "19 items", _previewFont, new Rectangle(0, padding / 2, width - padding, line), palette.Muted, TextFormatFlags.Right);

        var y = line + padding / 2;
        Fill(graphics, palette.Header, new Rectangle(0, y, width, line));
        TextRenderer.DrawText(graphics, "Name", _previewFont, new Point(padding, y + 3), palette.Text);
        TextRenderer.DrawText(graphics, "Description", _previewFont, new Point(column, y + 3), palette.Text);
        y += line;

        for (var i = 0; i < Rows.Length && y + line <= height; i++)
        {
            var (back, fore) = i switch
            {
                1 => (palette.Selection, palette.SelectionText),
                2 => (palette.Hover, palette.Text),
                _ => (palette.Window, palette.Text)
            };
            Fill(graphics, back, new Rectangle(0, y, width, line));
            TextRenderer.DrawText(graphics, Rows[i], _previewFont, new Point(padding, y + 3), fore);
            TextRenderer.DrawText(graphics, Names[i], _previewFont, new Point(column, y + 3), fore);
            y += line;
        }

        if (y + line <= height)
        {
            TextRenderer.DrawText(graphics, "Locked · comes from GTA V itself", _previewFont, new Point(padding, y + 3), palette.Muted);
        }

        using var border = new Pen(palette.Border);
        graphics.DrawRectangle(border, 0, 0, width - 1, height - 1);
    }

    private static void Fill(Graphics graphics, Color color, Rectangle bounds)
    {
        using var brush = new SolidBrush(color);
        graphics.FillRectangle(brush, bounds);
    }
}
