using QASentinel.Application.Agents;

namespace QASentinel.UnitTests;

public sealed class ErrorMappingTests
{
    [Theory]
    [InlineData("application_not_found", "não está mais em execução")]
    [InlineData("window_not_found", "janela principal")]
    [InlineData("element_not_found", "não foi encontrado")]
    [InlineData("timeout", "limite configurado")]
    public void AgentErrors_AreMappedToActionableMessages(string code, string expectedFragment)
    {
        var exception = new AgentOperationException(code, "technical");

        var message = AgentErrorMessageMapper.ToFriendlyMessage(exception);

        Assert.Contains(expectedFragment, message, StringComparison.CurrentCultureIgnoreCase);
        Assert.DoesNotContain("technical", message, StringComparison.Ordinal);
    }
}
