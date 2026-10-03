using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QASentinel.Automation.Abstractions;
using QASentinel.Automation.Models;

namespace QASentinel.Agent;

public static class CompatibilityProbeCommand
{
    private const string CommandArgument = "--compatibility-probe";

    public static async Task<int?> TryRunAsync(
        string[] arguments,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var commandIndex = Array.FindIndex(
            arguments,
            argument => string.Equals(argument, CommandArgument, StringComparison.OrdinalIgnoreCase));
        if (commandIndex < 0)
        {
            return null;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("CompatibilityProbe");
        try
        {
            var processId = ReadRequiredInt(arguments, commandIndex + 1, CommandArgument, 1, int.MaxValue);
            var outputDirectory = ReadOption(arguments, "--output")
                ?? Path.Combine(Environment.CurrentDirectory, "artifacts", "compatibility");
            var timeoutSeconds = ReadOptionalInt(arguments, "--timeout-seconds", 30, 5, 120);
            var maxDepth = ReadOptionalInt(arguments, "--max-depth", 12, 0, 64);
            var maxElements = ReadOptionalInt(arguments, "--max-elements", 2_000, 1, 10_000);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var engine = services.GetRequiredService<IAutomationEngine>();
            var session = await engine.AttachAsync(
                processId,
                TimeSpan.FromSeconds(Math.Min(timeoutSeconds, 15)),
                timeout.Token).ConfigureAwait(false);
            var scan = await engine.ScanCompatibilityAsync(
                session.Id,
                new CompatibilityScanOptions(maxDepth, maxElements),
                timeout.Token).ConfigureAwait(false);
            var files = await CompatibilityReportWriter.WriteAsync(scan, outputDirectory, timeout.Token)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Operation=CompatibilityProbe ProcessId={ProcessId} Elements={ElementCount} Json={JsonPath} " +
                "Markdown={MarkdownPath} Result=Success",
                processId,
                scan.Elements.Count,
                files.JsonPath,
                files.MarkdownPath);
            return 0;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogError(exception, "Operation=CompatibilityProbe Result=Timeout");
            return 2;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Operation=CompatibilityProbe Result=Failure");
            return 1;
        }
    }

    private static int ReadRequiredInt(
        string[] arguments,
        int index,
        string option,
        int minimum,
        int maximum)
    {
        if (index >= arguments.Length ||
            !int.TryParse(arguments[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value < minimum ||
            value > maximum)
        {
            throw new ArgumentException($"Informe um valor entre {minimum} e {maximum} após {option}.");
        }

        return value;
    }

    private static int ReadOptionalInt(
        string[] arguments,
        string option,
        int defaultValue,
        int minimum,
        int maximum)
    {
        var optionIndex = Array.FindIndex(
            arguments,
            argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));
        return optionIndex < 0
            ? defaultValue
            : ReadRequiredInt(arguments, optionIndex + 1, option, minimum, maximum);
    }

    private static string? ReadOption(string[] arguments, string option)
    {
        var optionIndex = Array.FindIndex(
            arguments,
            argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));
        if (optionIndex < 0)
        {
            return null;
        }

        if (optionIndex + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[optionIndex + 1]))
        {
            throw new ArgumentException($"Informe um valor após {option}.");
        }

        return arguments[optionIndex + 1];
    }
}

public static class CompatibilityReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<CompatibilityReportFiles> WriteAsync(
        AutomationCompatibilityScan scan,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var timestamp = scan.CapturedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var processName = SanitizeFileName(scan.Session.ProcessName);
        var baseName = $"{timestamp}_{processName}_uia3";
        var jsonPath = Path.GetFullPath(Path.Combine(outputDirectory, $"{baseName}.json"));
        var markdownPath = Path.GetFullPath(Path.Combine(outputDirectory, $"{baseName}.md"));

        var json = JsonSerializer.Serialize(ToSerializableReport(scan), JsonOptions);
        await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(markdownPath, BuildMarkdown(scan), Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
        return new CompatibilityReportFiles(jsonPath, markdownPath);
    }

    private static object ToSerializableReport(AutomationCompatibilityScan scan) => new
    {
        session = new
        {
            sessionId = scan.Session.Id,
            scan.Session.ProcessId,
            scan.Session.ExecutablePath,
            scan.Session.ProcessName,
            mainWindowHandle = scan.Session.MainWindowHandle.ToInt64(),
            scan.Session.WindowTitle,
            scan.Session.StartedAt,
            scan.Session.Backend
        },
        scan.CapturedAt,
        scan.ElementLimitReached,
        scan.Warnings,
        elements = scan.Elements.Select(item => new
        {
            item.Sequence,
            item.Depth,
            item.ParentSequence,
            item.SiblingIndex,
            element = new
            {
                item.Element.Name,
                item.Element.AutomationId,
                item.Element.ClassName,
                item.Element.ControlType,
                item.Element.FrameworkId,
                item.Element.HelpText,
                nativeWindowHandle = item.Element.NativeWindowHandle.ToInt64(),
                item.Element.IsEnabled,
                item.Element.IsOffscreen,
                item.Element.IsFocused,
                item.Element.Bounds,
                locators = item.Element.Locators.Locators,
                item.Element.SupportedPatterns
            }
        })
    };

    private static string BuildMarkdown(AutomationCompatibilityScan scan)
    {
        var elements = scan.Elements;
        var withAutomationId = elements.Count(item => !string.IsNullOrWhiteSpace(item.Element.AutomationId));
        var withHandle = elements.Count(item => item.Element.NativeWindowHandle != nint.Zero);
        var withPatterns = elements.Count(item => item.Element.SupportedPatterns.Count > 0);
        var delphiClasses = elements.Count(item => item.Element.ClassName?.StartsWith('T') == true);
        var markdown = new StringBuilder();

        markdown.AppendLine("# Relatório de compatibilidade UIA3");
        markdown.AppendLine();
        markdown.AppendLine($"- Aplicação: `{Escape(scan.Session.ProcessName)}`");
        markdown.AppendLine($"- Executável: `{Escape(scan.Session.ExecutablePath ?? "indisponível")}`");
        markdown.AppendLine($"- PID: `{scan.Session.ProcessId}`");
        markdown.AppendLine($"- Janela principal: `0x{scan.Session.MainWindowHandle.ToInt64():X}`");
        markdown.AppendLine($"- Capturado em: `{scan.CapturedAt:O}`");
        markdown.AppendLine($"- Elementos: `{elements.Count}`");
        markdown.AppendLine($"- Limite atingido: `{scan.ElementLimitReached}`");
        markdown.AppendLine();
        markdown.AppendLine("## Cobertura observada");
        markdown.AppendLine();
        markdown.AppendLine("| Sinal | Quantidade | Cobertura |");
        markdown.AppendLine("| --- | ---: | ---: |");
        AppendCoverage(markdown, "AutomationId", withAutomationId, elements.Count);
        AppendCoverage(markdown, "HWND nativo", withHandle, elements.Count);
        AppendCoverage(markdown, "Patterns UIA", withPatterns, elements.Count);
        AppendCoverage(markdown, "Classes Delphi (`T*`)", delphiClasses, elements.Count);
        markdown.AppendLine();
        AppendGroupedSection(markdown, "Tipos de controle", elements, item => item.Element.ControlType);
        AppendGroupedSection(markdown, "Classes", elements, item => item.Element.ClassName);

        markdown.AppendLine("## Elementos");
        markdown.AppendLine();
        markdown.AppendLine("| # | Nível | Name | AutomationId | ClassName | ControlType | Framework | HWND | Patterns | Bounds |");
        markdown.AppendLine("| ---: | ---: | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var item in elements)
        {
            var element = item.Element;
            markdown.Append("| ").Append(item.Sequence)
                .Append(" | ").Append(item.Depth)
                .Append(" | ").Append(Escape(element.Name))
                .Append(" | ").Append(Escape(element.AutomationId))
                .Append(" | ").Append(Escape(element.ClassName))
                .Append(" | ").Append(Escape(element.ControlType))
                .Append(" | ").Append(Escape(element.FrameworkId))
                .Append(" | ").Append(element.NativeWindowHandle == nint.Zero ? string.Empty : $"0x{element.NativeWindowHandle.ToInt64():X}")
                .Append(" | ").Append(Escape(string.Join(", ", element.SupportedPatterns)))
                .Append(" | ").Append(FormattableString.Invariant($"{element.Bounds.X:0},{element.Bounds.Y:0} {element.Bounds.Width:0}x{element.Bounds.Height:0}"))
                .AppendLine(" |");
        }

        if (scan.Warnings.Count > 0)
        {
            markdown.AppendLine();
            markdown.AppendLine("## Avisos de coleta");
            markdown.AppendLine();
            foreach (var warning in scan.Warnings)
            {
                markdown.Append("- ").AppendLine(Escape(warning));
            }
        }

        return markdown.ToString();
    }

    private static void AppendCoverage(StringBuilder markdown, string label, int count, int total)
    {
        var percentage = total == 0 ? 0 : (double)count / total;
        markdown.Append("| ").Append(label).Append(" | ").Append(count).Append(" | ")
            .Append(percentage.ToString("P1", CultureInfo.GetCultureInfo("pt-BR"))).AppendLine(" |");
    }

    private static void AppendGroupedSection(
        StringBuilder markdown,
        string title,
        IReadOnlyList<AutomationTreeElementInfo> elements,
        Func<AutomationTreeElementInfo, string?> selector)
    {
        markdown.Append("## ").AppendLine(title);
        markdown.AppendLine();
        markdown.AppendLine("| Valor | Quantidade |");
        markdown.AppendLine("| --- | ---: |");
        foreach (var group in elements
                     .GroupBy(item => string.IsNullOrWhiteSpace(selector(item)) ? "(vazio)" : selector(item)!)
                     .OrderByDescending(group => group.Count())
                     .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                     .Take(50))
        {
            markdown.Append("| ").Append(Escape(group.Key)).Append(" | ").Append(group.Count()).AppendLine(" |");
        }

        markdown.AppendLine();
    }

    private static string Escape(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "application" : sanitized;
    }
}

public sealed record CompatibilityReportFiles(string JsonPath, string MarkdownPath);
