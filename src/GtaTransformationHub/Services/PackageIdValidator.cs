using System.Text.RegularExpressions;

namespace GtaTransformationHub.Services;

public static partial class PackageIdValidator
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && IdPattern().IsMatch(value);
}
