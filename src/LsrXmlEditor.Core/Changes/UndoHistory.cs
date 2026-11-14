using System.Xml.Linq;

namespace LsrXmlEditor.Core.Changes;

public sealed class UndoHistory
{
    public const int DefaultCapacity = 10;

    private readonly int _capacity;
    private readonly LinkedList<Step> _undo = new();
    private readonly Stack<Step> _redo = new();
    private readonly Dictionary<XDocument, string> _documents = new(ReferenceEqualityComparer.Instance);
    private Step? _open;
    private Step? _replaying;

    public UndoHistory(int capacity = DefaultCapacity) => _capacity = capacity;

    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoTarget => _undo.Last?.Value.Path;

    public string? RedoTarget => _redo.TryPeek(out var step) ? step.Path : null;

    public void Track(XDocument document, string path)
    {
        if (_documents.TryAdd(document, path))
        {
            document.Changing += OnChanging;
            document.Changed += OnChanged;
        }
    }

    public void Forget(XDocument document)
    {
        if (!_documents.Remove(document))
        {
            return;
        }

        document.Changing -= OnChanging;
        document.Changed -= OnChanged;
        for (var node = _undo.First; node is not null;)
        {
            var next = node.Next;
            if (ReferenceEquals(node.Value.Document, document))
            {
                _undo.Remove(node);
            }

            node = next;
        }

        var remaining = _redo.Where(s => !ReferenceEquals(s.Document, document)).Reverse().ToList();
        _redo.Clear();
        remaining.ForEach(_redo.Push);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear() => Retain([]);

    public void Retain(IEnumerable<XDocument> live)
    {
        var keep = live.ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var document in _documents.Keys.Where(d => !keep.Contains(d)).ToList())
        {
            Forget(document);
        }
    }

    public void Close() => _open = null;

    public XDocument? Undo() => Replay(_undo.Last?.Value, fromUndo: true);

    public XDocument? Redo() => Replay(_redo.TryPeek(out var step) ? step : null, fromUndo: false);

    private XDocument? Replay(Step? step, bool fromUndo)
    {
        if (step is null)
        {
            return null;
        }

        _open = null;
        if (fromUndo)
        {
            _undo.RemoveLast();
        }
        else
        {
            _redo.Pop();
        }

        var inverse = new Step(step.Document, step.Path);
        _replaying = inverse;
        try
        {
            for (var i = step.Operations.Count - 1; i >= 0; i--)
            {
                step.Operations[i].Revert();
            }
        }
        finally
        {
            _replaying = null;
        }

        if (fromUndo)
        {
            _redo.Push(inverse);
        }
        else
        {
            Append(inverse);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return step.Document;
    }

    private void OnChanging(object? sender, XObjectChangeEventArgs e)
    {
        if (sender is not XObject target || target.Document is not { } document)
        {
            return;
        }

        Operation? operation = e.ObjectChange switch
        {
            XObjectChange.Remove when target is XNode node && node.Parent is { } parent => new Removal(node, parent, IndexOf(node)),
            XObjectChange.Remove when target is XAttribute attribute && attribute.Parent is { } owner => new AttributeRemoval(attribute, owner),
            XObjectChange.Value when target is XText text => new TextValue(text, text.Value),
            XObjectChange.Value when target is XAttribute attribute => new AttributeValue(attribute, attribute.Value),
            XObjectChange.Name when target is XElement element => new Rename(element, element.Name),
            _ => null
        };

        if (operation is not null)
        {
            Record(document, operation);
        }
    }

    private void OnChanged(object? sender, XObjectChangeEventArgs e)
    {
        if (e.ObjectChange == XObjectChange.Add && sender is XObject target && target.Document is { } document)
        {
            Record(document, new Addition(target));
        }
    }

    private void Record(XDocument document, Operation operation)
    {
        if (_replaying is { } replay)
        {
            replay.Operations.Add(operation);
            return;
        }

        if (_open is null || !ReferenceEquals(_open.Document, document))
        {
            _open = new Step(document, _documents.GetValueOrDefault(document, string.Empty));
            Append(_open);
            _redo.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        _open.Operations.Add(operation);
    }

    private void Append(Step step)
    {
        _undo.AddLast(step);
        while (_undo.Count > _capacity)
        {
            _undo.RemoveFirst();
        }
    }

    private static int IndexOf(XNode node)
    {
        var index = 0;
        for (var previous = node.PreviousNode; previous is not null; previous = previous.PreviousNode)
        {
            index++;
        }

        return index;
    }

    private sealed class Step(XDocument document, string path)
    {
        public XDocument Document { get; } = document;

        public string Path { get; } = path;

        public List<Operation> Operations { get; } = [];
    }

    private abstract class Operation
    {
        public abstract void Revert();
    }

    private sealed class Addition(XObject target) : Operation
    {
        public override void Revert()
        {
            switch (target)
            {
                case XNode node when node.Parent is not null || node.Document is not null:
                    node.Remove();
                    break;
                case XAttribute attribute when attribute.Parent is not null:
                    attribute.Remove();
                    break;
            }
        }
    }

    private sealed class Removal(XNode node, XContainer parent, int index) : Operation
    {
        public override void Revert()
        {
            if (index == 0)
            {
                parent.AddFirst(node);
            }
            else if (parent.Nodes().ElementAtOrDefault(index - 1) is { } previous)
            {
                previous.AddAfterSelf(node);
            }
            else
            {
                parent.Add(node);
            }
        }
    }

    private sealed class AttributeRemoval(XAttribute attribute, XElement owner) : Operation
    {
        public override void Revert() => owner.Add(attribute);
    }

    private sealed class TextValue(XText text, string value) : Operation
    {
        public override void Revert() => text.Value = value;
    }

    private sealed class AttributeValue(XAttribute attribute, string value) : Operation
    {
        public override void Revert() => attribute.Value = value;
    }

    private sealed class Rename(XElement element, XName name) : Operation
    {
        public override void Revert() => element.Name = name;
    }
}
