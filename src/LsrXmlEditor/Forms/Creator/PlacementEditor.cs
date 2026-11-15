using LsrXmlEditor.Core.Creators;
using LsrXmlEditor.Infrastructure;

namespace LsrXmlEditor.Forms.Creator;

internal sealed class PlacementEditor : FlowLayoutPanel
{
    private const decimal Range = 100000;
    private static readonly string[] Labels = ["X", "Y", "Z", "Heading"];

    private readonly NumericUpDown[] _values;
    private readonly CheckBox _attached = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
    private readonly Button _fromSave = new() { Text = "From &save game ▾", AutoSize = true };
    private readonly ContextMenuStrip _saves = new();
    private EntryDraft? _draft;
    private bool _loading;

    public PlacementEditor(IReadOnlyList<SavedPosition> saves)
    {
        AutoSize = true;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        Margin = Padding.Empty;

        var coordinates = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        _values = [.. Labels.Select((label, i) =>
        {
            var box = new NumericUpDown
            {
                Minimum = i == 3 ? 0 : -Range,
                Maximum = i == 3 ? 360 : Range,
                DecimalPlaces = i == 3 ? 2 : 4,
                Width = LogicalToDeviceUnits(i == 3 ? 80 : 100),
                TextAlign = HorizontalAlignment.Right
            };
            box.ValueChanged += (_, _) => Store();
            coordinates.Controls.Add(new Label { Text = label + ":", AutoSize = true, Margin = new Padding(i == 0 ? 0 : 10, 6, 3, 0) });
            coordinates.Controls.Add(box);
            return box;
        })];

        var paste = new Button { Text = "&Paste coordinates", AutoSize = true };
        paste.Click += (_, _) => Paste();
        var reset = new Button { Text = "&Reset", AutoSize = true };
        reset.Click += (_, _) => Put(_draft?.Origin);
        var tip = new ToolTip();
        tip.SetToolTip(paste, "Reads X, Y, Z (and heading) from the clipboard, e.g. \"981.85, -103.02, 74.84, 220\"");
        tip.SetToolTip(_fromSave, "Uses the spot where you saved the game with Los Santos RED");
        tip.SetToolTip(reset, "Back to the position of the template");

        foreach (var save in saves)
        {
            _saves.Items.Add(save.Title, null, (_, _) => Put(save.Placement));
        }

        _fromSave.Enabled = saves.Count > 0;
        _fromSave.Click += (_, _) => _saves.Show(_fromSave, new Point(0, _fromSave.Height));

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        buttons.Controls.AddRange([paste, _fromSave, reset]);

        _attached.CheckedChanged += (_, _) =>
        {
            if (_draft is not null && !_loading)
            {
                _draft.MoveAttached = _attached.Checked;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };

        Controls.AddRange([coordinates, buttons, _attached]);
        Disposed += (_, _) =>
        {
            _saves.Dispose();
            tip.Dispose();
        };
    }

    public event EventHandler? Changed;

    public void Bind(EntryDraft draft)
    {
        _draft = draft;
        _loading = true;
        Put(draft.Placement);
        _attached.Text = $"&Move the {draft.AttachedPoints} spawn, parking and camera points along (keeps the layout around the entrance)";
        _attached.Visible = draft.AttachedPoints > 0;
        _attached.Checked = draft.MoveAttached;
        _loading = false;
    }

    private void Put(Placement? placement)
    {
        if (placement is null)
        {
            return;
        }

        var loading = _loading;
        _loading = true;
        double[] values = [placement.X, placement.Y, placement.Z, placement.Heading];
        for (var i = 0; i < values.Length; i++)
        {
            _values[i].Value = Math.Clamp((decimal)values[i], _values[i].Minimum, _values[i].Maximum);
        }

        _loading = loading;
        Store();
    }

    private void Store()
    {
        if (_draft is null || _loading)
        {
            return;
        }

        _draft.Placement = new Placement((double)_values[0].Value, (double)_values[1].Value, (double)_values[2].Value, (double)_values[3].Value);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Paste()
    {
        var numbers = Clipboard.ContainsText() ? NameRules.Numbers(Clipboard.GetText()).Take(4).ToList() : [];
        if (numbers.Count < 3)
        {
            Dialogs.Info(FindForm(), "The clipboard holds no coordinates.\nCopy something like \"981.85, -103.02, 74.84\" (optionally with a heading as 4th number) and try again.");
            return;
        }

        var heading = numbers.Count > 3 ? numbers[3] : (double)_values[3].Value;
        Put(new Placement(numbers[0], numbers[1], numbers[2], ((heading % 360) + 360) % 360));
    }
}
