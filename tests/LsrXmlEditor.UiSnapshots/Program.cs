using System.Drawing.Imaging;
using System.Globalization;
using LsrXmlEditor.Controls;
using LsrXmlEditor.Core.Storage;
using LsrXmlEditor.Core.Xml;
using LsrXmlEditor.Forms;
using LsrXmlEditor.Infrastructure;
using LsrXmlEditor.PropertyModel;
using LsrXmlEditor.Services;

namespace LsrXmlEditor.UiSnapshots;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length < 2 || !Directory.Exists(args[0]))
        {
            Console.WriteLine("Usage: LsrXmlEditor.UiSnapshots <xml folder copy> <output folder>");
            return 2;
        }

        CultureInfo.DefaultThreadCurrentCulture = Format.Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Format.Culture;
        CultureInfo.CurrentCulture = Format.Culture;
        CultureInfo.CurrentUICulture = Format.Culture;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);

        var output = Directory.CreateDirectory(args[1]).FullName;
        var workspace = new Workspace(AppPaths.Default);
        var form = new MainForm(workspace, args[0])
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000),
            ShowInTaskbar = false
        };

        var exitCode = 1;
        form.Shown += async (_, _) =>
        {
            try
            {
                exitCode = await RunAsync(form, workspace, output);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                form.Close();
            }
        };

        Application.Run(form);
        return exitCode;
    }

    private static async Task<int> RunAsync(MainForm form, Workspace workspace, string output)
    {
        while (!workspace.IsOpen || workspace.Files.Count == 0 || form.Tree.Nodes.Count == 0)
        {
            await Task.Delay(50);
        }

        await Settle(form);
        Capture(form, output, "01-overview.png");

        form.Tree.Select(t => t is GroupTarget { Group: "Police & Dispatch" });
        await Settle(form);
        Capture(form, output, "02-section.png");

        var agencies = Path.Combine(workspace.Folder, "Agencies.xml");
        await form.NavigateAsync(agencies, null, 0, null);
        await Settle(form);

        var entry = (XmlNodeObject)form.Editor.Grid.SelectedObject!;
        var spawnLimit = (XmlFieldDescriptor)entry.GetProperties()["SpawnLimit"]!;
        var before = spawnLimit.Element.Value;
        var target = before == "42" ? 43L : 42L;
        spawnLimit.SetValue(entry, target);
        form.Editor.Grid.Refresh();
        form.Editor.RevealField("SpawnLimit");
        await Settle(form);
        Capture(form, output, "03-editor-changed.png");

        await form.SaveCurrentAsync();
        await Settle(form);
        Capture(form, output, "04-editor-saved.png");
        var status = form.StatusText;
        var backup = workspace.Backups!.List().FirstOrDefault(b => b.RelativePath == "Agencies.xml");
        var backupValue = backup is null ? null : XmlConfigDocument.Load(backup.BackupPath).Categories.Single().Entries.First().Element("SpawnLimit")!.Value;

        await form.NavigateAsync(Path.Combine(workspace.Folder, "Settings.xml"), null, 8, null);
        await Settle(form);
        Capture(form, output, "05-settings.png");

        var locations = workspace.FindBase("Locations")!;
        var gasStations = locations.Stats!.Categories.First(c => c.Name == "GasStations");
        await form.NavigateAsync(locations.FullPath, gasStations.Path, 2, null);
        await Settle(form);
        Capture(form, output, "06-locations.png");

        await form.NavigateAsync(Path.Combine(workspace.Folder, "Gangs.xml"), null, 0, "ID");
        await Settle(form);
        var gang = (XmlNodeObject)form.Editor.Grid.SelectedObject!;
        var gangId = (XmlFieldDescriptor)gang.GetProperties()["ID"]!;
        var originalId = gangId.Element.Value;
        gangId.SetValue(gang, "SHOULD_NOT_WRITE");
        var lockHeld = gangId.IsLocked && gangId.Element.Value == originalId;
        Capture(form, output, "07-locked.png");
        gangId.Unlock();
        await Settle(form);
        var unlocked = !gangId.IsLocked;
        Console.WriteLine($"Gang ID {originalId}: locked={lockHeld}, reference={gangId.GameReference}, unlockable={unlocked}");

        await form.NavigateAsync(agencies, null, 0, null);
        await Settle(form);
        form.List.SelectedIndices.Clear();
        foreach (var index in new[] { 1, 2, 3 })
        {
            form.List.SelectedIndices.Add(index);
        }

        await Settle(form);
        var multi = form.Editor.Grid.SelectedObject as MultiNodeObject;
        var multiOk = multi is not null && multi.Elements.Count == 3;
        if (multi is not null)
        {
            var multiId = (MultiFieldDescriptor)multi.GetProperties()["ID"]!;
            var multiLimit = (MultiFieldDescriptor)multi.GetProperties()["SpawnLimit"]!;
            multiOk &= multiId.GetValue(multi) is null;
            multiLimit.SetValue(multi, 5L);
            multiOk &= multi.Elements.All(e => e.Element("SpawnLimit")!.Value == "5") && multiLimit.GetValue(multi) is 5L;
            form.Editor.Grid.Refresh();
            await Settle(form);
            Capture(form, output, "08-multi-select.png");
            await form.SaveCurrentAsync();
            await Settle(form);
            Console.WriteLine($"Multi edit: {multi.Elements.Count} entries, ID mixed={multiId.IsMixed}, SpawnLimit={multiLimit.GetValue(multi)}, status: {form.StatusText}");
        }

        await form.NavigateAsync(agencies, null, 2, "SpawnLimit");
        await Settle(form);
        var revealed = form.Editor.Grid.SelectedGridItem?.PropertyDescriptor is XmlFieldDescriptor { Segment: "SpawnLimit" } field
            && form.Editor.Entry is { } shown
            && ReferenceEquals(field.Element.Parent, shown)
            && shown.ElementsBeforeSelf(shown.Name).Count() == 2;
        Console.WriteLine($"Jump to field: {revealed}");

        CaptureDialog(new BackupsForm(workspace), output, "09-backups.png");
        CaptureDialog(new ChangesForm(workspace), output, "10-changes.png");
        CaptureDialog(new SearchForm(workspace, _ => { }), output, "11-search.png");

        var saved = XmlConfigDocument.Load(agencies).Categories.Single().Entries.First().Element("SpawnLimit")!.Value;

        Console.WriteLine($"SpawnLimit before: {before}, on disk now: {saved}, in backup: {backupValue}");
        Console.WriteLine($"Status: {status}");
        Console.WriteLine($"Multi edit ok: {multiOk}");
        return saved == target.ToString() && backupValue == before && lockHeld && unlocked && multiOk && revealed ? 0 : 1;
    }

    private static async Task Settle(MainForm form)
    {
        await form.Navigation;
        for (var i = 0; i < 5; i++)
        {
            Application.DoEvents();
            await Task.Delay(80);
        }
    }

    private static void CaptureDialog(Form dialog, string output, string name)
    {
        using (dialog)
        {
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-20000, -20000);
            dialog.Show();
            Application.DoEvents();
            Capture(dialog, output, name);
            dialog.Close();
        }
    }

    private static void Capture(Form form, string output, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        var path = Path.Combine(output, name);
        bitmap.Save(path, ImageFormat.Png);
        Console.WriteLine($"Saved {path}");
    }
}
