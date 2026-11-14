using System.Text;

namespace LsrXmlEditor.Core.Text;

public static class DisplayNames
{
    public static string Humanize(string name)
    {
        if (name.Length < 2)
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_')
            {
                builder.Append(' ');
                continue;
            }

            if (i > 0 && char.IsUpper(c) && builder.Length > 0 && builder[^1] != ' ')
            {
                var previous = name[i - 1];
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                {
                    builder.Append(' ');
                }
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
