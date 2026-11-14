namespace LsrXmlEditor.Infrastructure;

public static class SplitterExtensions
{
    public static void SetDistance(this SplitContainer split, int distance)
    {
        var length = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        var max = length - split.Panel2MinSize - split.SplitterWidth;
        if (max >= split.Panel1MinSize)
        {
            split.SplitterDistance = Math.Clamp(distance, split.Panel1MinSize, max);
        }
    }
}
