using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LsrXmlEditor.Core.Xml;

public sealed record XmlFileFormat(Encoding Encoding, string? Declaration, string NewLine, string Trailing)
{
    private const int ProbeSize = 4096;
    private const int WriteBufferSize = 1 << 16;

    public static XmlFileFormat Detect(Stream stream)
    {
        var start = stream.Position;
        Span<byte> head = stackalloc byte[ProbeSize];
        var read = stream.ReadAtLeast(head, ProbeSize, throwOnEndOfStream: false);
        head = head[..read];

        var (encoding, bomLength) = DetectEncoding(head);
        var text = encoding.GetString(head[bomLength..]);
        var declaration = ReadDeclaration(text.TrimStart());
        var lf = text.IndexOf('\n');
        var newLine = lf > 0 && text[lf - 1] == '\r' ? "\r\n" : lf >= 0 ? "\n" : "\r\n";

        var trailing = ReadTrailing(stream, encoding);
        stream.Position = start;
        return new XmlFileFormat(encoding, declaration, newLine, trailing);
    }

    public void Write(XDocument document, string path)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = Encoding,
            OmitXmlDeclaration = true,
            Indent = false,
            NewLineHandling = NewLineHandling.Replace,
            NewLineChars = NewLine,
            CheckCharacters = false
        };

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, WriteBufferSize);
        using (var writer = XmlWriter.Create(stream, settings))
        {
            if (Declaration is not null)
            {
                writer.WriteRaw(Declaration);
            }

            foreach (var node in document.Nodes())
            {
                node.WriteTo(writer);
            }
        }

        if (Trailing.Length > 0 && document.LastNode is not XText)
        {
            stream.Write(Encoding.GetBytes(Trailing));
        }
    }

    private static string? ReadDeclaration(string text)
    {
        if (!text.StartsWith("<?xml", StringComparison.Ordinal))
        {
            return null;
        }

        var end = text.IndexOf("?>", StringComparison.Ordinal);
        return end < 0 ? null : text[..(end + 2)];
    }

    private static (Encoding Encoding, int BomLength) DetectEncoding(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return (new UTF8Encoding(true), 3);
        }

        if (head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return (new UnicodeEncoding(false, true), 2);
        }

        if (head.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return (new UnicodeEncoding(true, true), 2);
        }

        return (new UTF8Encoding(false), 0);
    }

    private static string ReadTrailing(Stream stream, Encoding encoding)
    {
        const int tailSize = 64;
        if (!stream.CanSeek || stream.Length == 0)
        {
            return string.Empty;
        }

        stream.Position = Math.Max(0, stream.Length - tailSize);
        Span<byte> tail = stackalloc byte[tailSize];
        var read = stream.ReadAtLeast(tail, tailSize, throwOnEndOfStream: false);
        var text = encoding.GetString(tail[..read]);
        var end = text.LastIndexOf('>');
        return end < 0 ? string.Empty : text[(end + 1)..];
    }
}
