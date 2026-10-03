using System.Globalization;
using System.Text;
using QASentinel.Automation.Models;

namespace QASentinel.Recorder;

public sealed class RecorderFieldPolicy(int maximumCapturedLength = 4_096)
{
    private static readonly string[] CompactSensitiveTerms =
    [
        "password",
        "passwd",
        "senha",
        "secret",
        "token",
        "apikey",
        "accesskey",
        "chavedeacesso",
        "creditcard",
        "cartaodecredito",
        "bankaccount",
        "contabancaria",
        "socialsecurity"
    ];

    private static readonly HashSet<string> SensitiveTokens = new(StringComparer.Ordinal)
    {
        "pwd",
        "pin",
        "cvv",
        "cvc",
        "cpf",
        "cnpj",
        "ssn"
    };

    public bool CanTrack(AutomationElementInfo element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return string.Equals(element.ControlType, "Edit", StringComparison.OrdinalIgnoreCase) &&
               !IsSensitiveMetadata(element);
    }

    public bool IsSensitiveMetadata(AutomationElementInfo element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var tokens = NormalizeTokens(string.Join(
            ' ',
            element.Name,
            element.AutomationId,
            element.HelpText,
            element.ClassName));
        var compact = string.Concat(tokens);
        return tokens.Any(SensitiveTokens.Contains) ||
               CompactSensitiveTerms.Any(term => compact.Contains(term, StringComparison.Ordinal));
    }

    public bool CanStore(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length <= maximumCapturedLength;
    }

    private static string[] NormalizeTokens(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character)
                ? char.ToLowerInvariant(character)
                : ' ');
        }

        return builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
