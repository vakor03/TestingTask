using System.Text;

namespace TestingTask.CLI;

public static class SecurityUtils
{
    public static string SanitizeText(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c == '\t' || (c >= ' ' && !char.IsSurrogate(c)))
                sb.Append(c);
        }

        string result = sb.ToString().Trim();

        if (result.Length > 0 && (result[0] == '=' || result[0] == '+' || result[0] == '-' || result[0] == '@'))
            result = "'" + result;

        if (result.Length > 256)
            result = result[..256];

        return result;
    }

    public static int ClampNonNegative(int value) =>
        value < 0 ? 0 : value;

    public static decimal ClampNonNegative(decimal value) =>
        value < 0 ? 0 : value;
}