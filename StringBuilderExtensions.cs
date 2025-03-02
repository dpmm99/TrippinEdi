using System.Text;

namespace TrippinEdi;

public static class StringBuilderExtensions
{
    public static bool StartsWith(this StringBuilder sb, string value)
    {
        if (sb.Length < value.Length)
            return false;

        // Compare characters one by one
        for (int i = 0; i < value.Length; i++)
        {
            if (sb[i] != value[i])
                return false;
        }

        return true;
    }

    public static bool EndsWith(this StringBuilder sb, string value)
    {
        if (sb.Length < value.Length)
            return false;

        // Compare characters one by one
        var startIndex = sb.Length - value.Length;
        for (int i = 0; i < value.Length; i++)
        {
            if (sb[startIndex + i] != value[i])
                return false;
        }

        return true;
    }
}
