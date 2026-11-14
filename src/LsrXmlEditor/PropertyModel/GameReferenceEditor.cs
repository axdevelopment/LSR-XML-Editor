using System.ComponentModel;
using System.Drawing.Design;
using System.Globalization;
using LsrXmlEditor.Infrastructure;

namespace LsrXmlEditor.PropertyModel;

public sealed class GameReferenceEditor : UITypeEditor
{
    private static readonly Lazy<Image> LockImage = new(() => StockImages.Small(StockIconId.Lock));

    public static GameReferenceEditor Instance { get; } = new();

    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;

    public override bool GetPaintValueSupported(ITypeDescriptorContext? context) => true;

    public override void PaintValue(PaintValueEventArgs e)
    {
        var image = LockImage.Value;
        var size = Math.Min(e.Bounds.Height, e.Bounds.Width);
        var x = e.Bounds.X + (e.Bounds.Width - size) / 2;
        var y = e.Bounds.Y + (e.Bounds.Height - size) / 2;
        e.Graphics.DrawImage(image, new Rectangle(x, y, size, size));
    }

    public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
    {
        if (context?.PropertyDescriptor is not ILockableField descriptor || !descriptor.IsLocked)
        {
            return value;
        }

        var message =
            $"\"{descriptor.DisplayName}\" contains a {descriptor.GameReference}:\n\n    {descriptor.LockedValue}\n\n"
            + "Names like this come from GTA V itself. If it is changed to something the game does not know, "
            + "LSR can no longer spawn, animate or find it.\n\nUnlock this field for editing?";

        var answer = MessageBox.Show(Form.ActiveForm, message, Dialogs.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes)
        {
            descriptor.Unlock();
        }

        return value;
    }
}

public sealed class LockedValueConverter : TypeConverter
{
    public static LockedValueConverter Instance { get; } = new();

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => false;

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        value?.ToString() ?? string.Empty;
}
