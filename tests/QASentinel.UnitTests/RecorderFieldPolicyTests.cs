using QASentinel.Automation.Models;
using QASentinel.Recorder;

namespace QASentinel.UnitTests;

public sealed class RecorderFieldPolicyTests
{
    private readonly RecorderFieldPolicy _policy = new();

    [Theory]
    [InlineData("Senha", "txtPassword")]
    [InlineData("Token de acesso", "txtToken")]
    [InlineData("CPF", "txtDocument")]
    [InlineData("PIN", "txtPin")]
    public void SensitiveMetadataIsNeverTracked(string name, string automationId)
    {
        var element = CreateElement(name, automationId, "Edit");

        Assert.True(_policy.IsSensitiveMetadata(element));
        Assert.False(_policy.CanTrack(element));
    }

    [Fact]
    public void OrdinaryEditableFieldCanBeTracked()
    {
        var element = CreateElement("Descrição", "txtDescription", "Edit");

        Assert.False(_policy.IsSensitiveMetadata(element));
        Assert.True(_policy.CanTrack(element));
    }

    [Fact]
    public void ShortSensitiveTokenDoesNotMatchPartOfAnUnrelatedWord()
    {
        var element = CreateElement("Campaign", "txtCampaign", "Edit");

        Assert.False(_policy.IsSensitiveMetadata(element));
    }

    [Fact]
    public void ExcessivelyLargeValueIsRejected()
    {
        Assert.False(_policy.CanStore(new string('x', 4_097)));
    }

    private static AutomationElementInfo CreateElement(string name, string automationId, string controlType) =>
        new(
            name,
            automationId,
            "TextBox",
            controlType,
            "WPF",
            null,
            nint.Zero,
            true,
            false,
            false,
            new BoundingRectangle(10, 10, 200, 30),
            new LocatorSet([new ElementLocator(LocatorKind.AutomationId, automationId)]),
            ["Value"]);
}
