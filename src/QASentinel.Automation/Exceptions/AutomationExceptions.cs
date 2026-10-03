namespace QASentinel.Automation.Exceptions;

public abstract class AutomationException : Exception
{
    protected AutomationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class ApplicationNotFoundException(int processId)
    : AutomationException($"O processo {processId} não foi encontrado.");

public sealed class ApplicationAttachException(int processId, Exception? innerException = null)
    : AutomationException($"Não foi possível conectar ao processo {processId}.", innerException);

public sealed class WindowNotFoundException(int processId)
    : AutomationException($"Nenhuma janela principal foi encontrada para o processo {processId}.");

public sealed class ElementNotFoundException()
    : AutomationException("O componente não foi encontrado com os identificadores disponíveis.");

public sealed class ElementNotInteractableException(string action, Exception? innerException = null)
    : AutomationException($"O componente não está disponível para a ação '{action}'.", innerException);

public sealed class AutomationTimeoutException(string operation, TimeSpan timeout, Exception? innerException = null)
    : AutomationException($"A operação '{operation}' excedeu o limite de {timeout.TotalSeconds:0.#} segundos.", innerException);

public sealed class AutomationBackendException(string backend, Exception? innerException = null)
    : AutomationException($"O backend de automação {backend} falhou.", innerException);

public sealed class SessionNotFoundException(Guid sessionId)
    : AutomationException($"A sessão de automação '{sessionId}' não existe ou expirou.");
