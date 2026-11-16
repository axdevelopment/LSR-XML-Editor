using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LsrXmlEditor.Core.Themes;

namespace LsrXmlEditor.Infrastructure;

internal static class ControlStyler
{
    private const int CaptionColorAttribute = 35;
    private const int CaptionTextAttribute = 36;
    private const int BorderColorAttribute = 34;
    private const string HostTypeName = "ElementHost";

    private static readonly ConditionalWeakTable<Control, object> Watched = [];

    public static void Attach(Form form, ThemePalette palette)
    {
        Style(form, palette);
        form.HandleCreated += (_, _) => PaintCaption(form, palette);
        if (form.IsHandleCreated)
        {
            PaintCaption(form, palette);
        }
    }

    public static bool IsGray(Color color) => color.IsSystemColor && color.ToKnownColor() == KnownColor.GrayText;

    private static void Style(Control control, ThemePalette palette)
    {
        Apply(control, palette);
        if (control.GetType().Name == HostTypeName)
        {
            return;
        }

        foreach (Control child in control.Controls)
        {
            Style(child, palette);
        }

        if (!Watched.TryGetValue(control, out _))
        {
            Watched.Add(control, palette);
            control.ControlAdded += (_, e) =>
            {
                if (e.Control is { } added)
                {
                    Style(added, palette);
                }
            };
        }
    }

    private static void Apply(Control control, ThemePalette palette)
    {
        switch (control)
        {
            case Form form:
                form.BackColor = palette.Surface;
                form.ForeColor = palette.Text;
                break;
            case PropertyGrid grid:
                StyleGrid(grid, palette);
                break;
            case DataGridView table:
                StyleTable(table, palette);
                break;
            case ListView or TreeView:
                control.BackColor = palette.Window;
                control.ForeColor = palette.Text;
                if (control is TreeView tree)
                {
                    tree.LineColor = palette.Border;
                }

                break;
            case ListBox { DrawMode: not DrawMode.Normal }:
                break;
            case TextBoxBase box:
                box.BackColor = palette.Input;
                box.ForeColor = palette.Text;
                if (box.BorderStyle == BorderStyle.Fixed3D)
                {
                    box.BorderStyle = BorderStyle.FixedSingle;
                }

                break;
            case ListBox or NumericUpDown:
                control.BackColor = palette.Input;
                control.ForeColor = palette.Text;
                break;
            case ComboBox combo:
                combo.BackColor = palette.Input;
                combo.ForeColor = palette.Text;
                if (combo.DrawMode == DrawMode.Normal)
                {
                    combo.FlatStyle = FlatStyle.Flat;
                }

                break;
            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = palette.Input;
                button.ForeColor = palette.Text;
                button.FlatAppearance.BorderColor = palette.Border;
                button.FlatAppearance.MouseOverBackColor = palette.Hover;
                button.FlatAppearance.MouseDownBackColor = palette.InactiveSelection;
                break;
            case TabControl tabs:
                StyleTabs(tabs, palette);
                break;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                page.BackColor = palette.Surface;
                page.ForeColor = palette.Text;
                break;
            case SplitContainer split:
                split.BackColor = palette.Border;
                split.Panel1.BackColor = palette.Surface;
                split.Panel2.BackColor = palette.Surface;
                break;
            case ToolStrip strip:
                strip.BackColor = palette.Surface;
                strip.ForeColor = palette.Text;
                StyleItems(strip.Items, palette);
                break;
            case LinkLabel link:
                link.LinkColor = palette.Accent;
                link.ActiveLinkColor = palette.Accent;
                break;
            default:
                MapSystemColors(control, palette);
                break;
        }
    }

    private static void MapSystemColors(Control control, ThemePalette palette)
    {
        if (Map(control.BackColor, palette) is { } back)
        {
            control.BackColor = back;
        }

        if (IsGray(control.ForeColor))
        {
            control.ForeColor = palette.Muted;
        }
        else if (control.ForeColor.IsSystemColor)
        {
            control.ForeColor = palette.Text;
        }
    }

    private static Color? Map(Color color, ThemePalette palette) => !color.IsSystemColor ? null : color.ToKnownColor() switch
    {
        KnownColor.Window => palette.Window,
        KnownColor.ControlDark or KnownColor.ControlDarkDark => palette.Border,
        KnownColor.Control or KnownColor.ButtonFace => palette.Surface,
        KnownColor.ControlLight or KnownColor.ControlLightLight => palette.Header,
        KnownColor.Highlight => palette.Selection,
        _ => null
    };

    private static void StyleItems(ToolStripItemCollection items, ThemePalette palette)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is ToolStripControlHost host)
            {
                host.BackColor = palette.Input;
                host.ForeColor = palette.Text;
            }

            if (item is ToolStripDropDownItem { HasDropDownItems: true } dropDown)
            {
                StyleItems(dropDown.DropDownItems, palette);
            }
        }
    }

    private static void StyleGrid(PropertyGrid grid, ThemePalette palette)
    {
        grid.BackColor = palette.Surface;
        grid.ViewBackColor = palette.Window;
        grid.ViewForeColor = palette.Text;
        grid.ViewBorderColor = palette.Border;
        grid.LineColor = palette.Header;
        grid.CategoryForeColor = palette.Text;
        grid.CategorySplitterColor = palette.Border;
        grid.HelpBackColor = palette.Surface;
        grid.HelpForeColor = palette.Text;
        grid.HelpBorderColor = palette.Border;
        grid.CommandsBackColor = palette.Surface;
        grid.CommandsForeColor = palette.Text;
        grid.CommandsBorderColor = palette.Border;
        grid.SelectedItemWithFocusBackColor = palette.Selection;
        grid.SelectedItemWithFocusForeColor = palette.SelectionText;
        grid.DisabledItemForeColor = palette.Muted;
    }

    private static void StyleTable(DataGridView table, ThemePalette palette)
    {
        table.EnableHeadersVisualStyles = false;
        table.BackgroundColor = palette.Window;
        table.GridColor = palette.Border;
        table.ForeColor = palette.Text;
        table.DefaultCellStyle.BackColor = palette.Window;
        table.DefaultCellStyle.ForeColor = palette.Text;
        table.DefaultCellStyle.SelectionBackColor = palette.Selection;
        table.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
        table.ColumnHeadersDefaultCellStyle.BackColor = palette.Header;
        table.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
        table.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Header;
        table.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
        table.RowHeadersDefaultCellStyle.BackColor = palette.Header;
        table.RowHeadersDefaultCellStyle.ForeColor = palette.Text;
        table.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
    }

    private static void StyleTabs(TabControl tabs, ThemePalette palette)
    {
        if (tabs.DrawMode == TabDrawMode.OwnerDrawFixed)
        {
            return;
        }

        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        _ = new TabPainter(tabs, palette);
        tabs.DrawItem += (_, e) =>
        {
            var selected = e.Index == tabs.SelectedIndex;
            using var brush = new SolidBrush(selected ? palette.Window : palette.Surface);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, selected ? palette.Text : palette.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
    }

    private sealed class TabPainter : NativeWindow
    {
        private const int PaintMessage = 0x000F;

        private readonly TabControl _tabs;
        private readonly ThemePalette _palette;

        public TabPainter(TabControl tabs, ThemePalette palette)
        {
            _tabs = tabs;
            _palette = palette;
            if (tabs.IsHandleCreated)
            {
                AssignHandle(tabs.Handle);
            }

            tabs.HandleCreated += (_, _) => AssignHandle(tabs.Handle);
            tabs.HandleDestroyed += (_, _) => ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == PaintMessage && _tabs.TabCount > 0)
            {
                PaintGaps();
            }
        }

        private void PaintGaps()
        {
            using var region = new Region(_tabs.ClientRectangle);
            region.Exclude(_tabs.DisplayRectangle);
            for (var i = 0; i < _tabs.TabCount; i++)
            {
                region.Exclude(Rectangle.Inflate(_tabs.GetTabRect(i), -2, -2));
            }

            using var graphics = _tabs.CreateGraphics();
            using var brush = new SolidBrush(_palette.Surface);
            graphics.FillRegion(brush, region);
            if (_tabs.SelectedIndex >= 0)
            {
                var tab = Rectangle.Inflate(_tabs.GetTabRect(_tabs.SelectedIndex), -2, -2);
                using var accent = new SolidBrush(_palette.Accent);
                graphics.FillRectangle(accent, tab.X, tab.Bottom - 2, tab.Width, 2);
            }
        }
    }

    private static void PaintCaption(Form form, ThemePalette palette)
    {
        SetAttribute(form.Handle, CaptionColorAttribute, palette.Surface);
        SetAttribute(form.Handle, CaptionTextAttribute, palette.Text);
        SetAttribute(form.Handle, BorderColorAttribute, palette.Border);
    }

    private static void SetAttribute(IntPtr handle, int attribute, Color color)
    {
        var value = color.R | color.G << 8 | color.B << 16;
        _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
