using System.Text.RegularExpressions;

namespace Admission.Api.Services;

public static partial class StudentIdValidator
{
    [GeneratedRegex("^[0-9]{17}$", RegexOptions.CultureInvariant)]
    private static partial Regex StudentIdPattern();

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && StudentIdPattern().IsMatch(value);
}
