namespace ViggiSlideshow.Core;

/// <summary>
/// Orders names the way Explorer does: runs of digits compare by value, so "IMG_2" comes
/// before "IMG_10". Everything else compares case-insensitively.
/// </summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int startX = i, startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;

                ReadOnlySpan<char> a = x.AsSpan(startX, i - startX).TrimStart('0');
                ReadOnlySpan<char> b = y.AsSpan(startY, j - startY).TrimStart('0');
                if (a.Length != b.Length)
                    return a.Length.CompareTo(b.Length);
                int digits = a.CompareTo(b, StringComparison.Ordinal);
                if (digits != 0)
                    return digits;
            }
            else
            {
                int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (c != 0)
                    return c;
                i++;
                j++;
            }
        }

        int remaining = (x.Length - i).CompareTo(y.Length - j);
        return remaining != 0 ? remaining : string.CompareOrdinal(x, y);
    }
}
