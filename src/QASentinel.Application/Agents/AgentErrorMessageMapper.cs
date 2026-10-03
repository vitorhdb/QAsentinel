namespace QASentinel.Application.Agents;

public static class AgentErrorMessageMapper
{
    public static string ToFriendlyMessage(Exception exception) => exception switch
    {
        AgentOperationException { Code: "application_not_found" } => "A aplicação selecionada não está mais em execução.",
        AgentOperationException { Code: "window_not_found" } => "A aplicação não possui uma janela principal disponível.",
        AgentOperationException { Code: "element_not_found" } => "O componente não foi encontrado novamente. Inspecione-o e tente outra vez.",
        AgentOperationException { Code: "element_not_interactable" } => "O componente está visível, mas não aceitou a ação solicitada.",
        AgentOperationException { Code: "timeout" } => "A operação demorou além do limite configurado.",
        TimeoutException => "O Agent não respondeu dentro do tempo esperado.",
        IOException => "A conexão local com o Agent foi interrompida.",
        _ => "Não foi possível concluir a operação. Consulte os logs para os detalhes técnicos."
    };
}
