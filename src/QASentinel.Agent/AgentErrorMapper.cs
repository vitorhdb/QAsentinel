using System.Text.Json;
using QASentinel.Automation.Exceptions;
using QASentinel.Contracts;

namespace QASentinel.Agent;

internal static class AgentErrorMapper
{
    internal static AgentErrorDto Map(Exception exception) => exception switch
    {
        ApplicationNotFoundException => new("application_not_found", exception.Message),
        ApplicationAttachException => new("application_attach", exception.Message, GetDetail(exception)),
        WindowNotFoundException => new("window_not_found", exception.Message),
        ElementNotFoundException => new("element_not_found", exception.Message),
        ElementNotInteractableException => new("element_not_interactable", exception.Message, GetDetail(exception)),
        AutomationTimeoutException => new("timeout", exception.Message, GetDetail(exception)),
        AutomationBackendException => new("automation_backend", exception.Message, GetDetail(exception)),
        SessionNotFoundException => new("session_not_found", exception.Message),
        ExecutionNotFoundException => new("execution_not_found", exception.Message),
        OperationCanceledException => new("cancelled", "A operação foi cancelada."),
        JsonException => new("invalid_payload", "A requisição contém dados inválidos."),
        InvalidOperationException => new("invalid_operation", exception.Message),
        _ => new("internal_error", "O Agent encontrou uma falha inesperada.", exception.GetType().Name)
    };

    private static string? GetDetail(Exception exception) => exception.InnerException?.Message;
}
