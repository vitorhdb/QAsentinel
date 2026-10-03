using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using QASentinel.Contracts;

namespace QASentinel.Desktop.State;

public sealed class TestDraftState : ObservableObject
{
    public ObservableCollection<DraftTestStep> Steps { get; } = [];

    public void Add(TestActionDto action, AutomationElementInfoDto element, string? input)
    {
        ArgumentNullException.ThrowIfNull(element);
        Steps.Add(new DraftTestStep(
            Guid.NewGuid(),
            Steps.Count + 1,
            action,
            element.Locators,
            action == TestActionDto.Input ? input ?? string.Empty : null,
            5_000,
            action is TestActionDto.Click or TestActionDto.Focus or TestActionDto.Input ? 3 : 1,
            GetDisplayTarget(element)));
    }

    public void Clear() => Steps.Clear();

    private static string GetDisplayTarget(AutomationElementInfoDto element) =>
        FirstNonEmpty(element.AutomationId, element.Name, element.ClassName, element.ControlType) ?? "Elemento sem nome";

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed record DraftTestStep(
    Guid StepId,
    int Sequence,
    TestActionDto Action,
    LocatorSetDto Target,
    string? Input,
    int TimeoutMilliseconds,
    int MaxAttempts,
    string DisplayTarget);
