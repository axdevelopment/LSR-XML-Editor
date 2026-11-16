using LsrXmlEditor.Core.Themes;

namespace LsrXmlEditor.Infrastructure;

internal sealed class PaletteRenderer(ThemePalette palette) : ToolStripProfessionalRenderer(new PaletteColorTable(palette))
{
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled || ControlStyler.IsGray(e.Item.ForeColor) ? palette.Muted : palette.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == false ? palette.Muted : palette.Text;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown)
        {
            using var pen = new Pen(palette.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown)
        {
            base.OnRenderToolStripBackground(e);
            return;
        }

        using var brush = new SolidBrush(palette.Surface);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    private sealed class PaletteColorTable(ThemePalette palette) : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => palette.Surface;
        public override Color ToolStripGradientMiddle => palette.Surface;
        public override Color ToolStripGradientEnd => palette.Surface;
        public override Color ToolStripBorder => palette.Border;
        public override Color ToolStripDropDownBackground => palette.Input;
        public override Color ToolStripContentPanelGradientBegin => palette.Surface;
        public override Color ToolStripContentPanelGradientEnd => palette.Surface;
        public override Color ToolStripPanelGradientBegin => palette.Surface;
        public override Color ToolStripPanelGradientEnd => palette.Surface;
        public override Color MenuStripGradientBegin => palette.Surface;
        public override Color MenuStripGradientEnd => palette.Surface;
        public override Color StatusStripGradientBegin => palette.Surface;
        public override Color StatusStripGradientEnd => palette.Surface;
        public override Color MenuBorder => palette.Border;
        public override Color MenuItemBorder => palette.Border;
        public override Color MenuItemSelected => palette.Hover;
        public override Color MenuItemSelectedGradientBegin => palette.Hover;
        public override Color MenuItemSelectedGradientEnd => palette.Hover;
        public override Color MenuItemPressedGradientBegin => palette.Input;
        public override Color MenuItemPressedGradientMiddle => palette.Input;
        public override Color MenuItemPressedGradientEnd => palette.Input;
        public override Color ImageMarginGradientBegin => palette.Input;
        public override Color ImageMarginGradientMiddle => palette.Input;
        public override Color ImageMarginGradientEnd => palette.Input;
        public override Color ButtonSelectedHighlight => palette.Hover;
        public override Color ButtonSelectedHighlightBorder => palette.Border;
        public override Color ButtonSelectedBorder => palette.Border;
        public override Color ButtonSelectedGradientBegin => palette.Hover;
        public override Color ButtonSelectedGradientMiddle => palette.Hover;
        public override Color ButtonSelectedGradientEnd => palette.Hover;
        public override Color ButtonPressedHighlight => palette.InactiveSelection;
        public override Color ButtonPressedBorder => palette.Accent;
        public override Color ButtonPressedGradientBegin => palette.InactiveSelection;
        public override Color ButtonPressedGradientMiddle => palette.InactiveSelection;
        public override Color ButtonPressedGradientEnd => palette.InactiveSelection;
        public override Color ButtonCheckedHighlight => palette.InactiveSelection;
        public override Color ButtonCheckedHighlightBorder => palette.Accent;
        public override Color ButtonCheckedGradientBegin => palette.InactiveSelection;
        public override Color ButtonCheckedGradientMiddle => palette.InactiveSelection;
        public override Color ButtonCheckedGradientEnd => palette.InactiveSelection;
        public override Color CheckBackground => palette.InactiveSelection;
        public override Color CheckSelectedBackground => palette.Hover;
        public override Color CheckPressedBackground => palette.Hover;
        public override Color SeparatorDark => palette.Border;
        public override Color SeparatorLight => palette.Surface;
        public override Color GripDark => palette.Border;
        public override Color GripLight => palette.Surface;
        public override Color OverflowButtonGradientBegin => palette.Surface;
        public override Color OverflowButtonGradientMiddle => palette.Surface;
        public override Color OverflowButtonGradientEnd => palette.Surface;
        public override Color RaftingContainerGradientBegin => palette.Surface;
        public override Color RaftingContainerGradientEnd => palette.Surface;
    }
}
