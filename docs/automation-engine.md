# Automation Engine

## Contrato

`IAutomationEngine` oferece attach, inspeção exata por ponto, inspeção de alvo acionável, varredura diagnóstica, resolução por locators, click, foco e input. `IScreenshotService`, `IProcessService`, `ITopLevelWindowService`, `IElementHighlighter` e `IComponentAdapter` separam capacidades com dependências e ciclos de vida diferentes.

## Sessão

O attach cria `ApplicationSession` com SessionId, PID, executável, nome do processo, janela principal, horário e backend. A janela principal é escolhida entre as top-level do PID, priorizando uma janela visível, com bounds e diferente da `TApplication` oculta. Somente o Agent mantém `FlaUI.Application`, `UIA3Automation` e `AutomationElement`.

## Resolução

A ordem atual é:

1. HWND, validado contra PID e propriedades conhecidas;
2. AutomationId;
3. Name + ClassName;
4. Name;
5. ClassName.

Coordenadas são registradas para diagnóstico, mas não são usadas como estratégia principal. Antes de click/input, o Agent resolve novamente o alvo. Isso evita depender da validade de objetos COM antigos.

Em controles Win32/Delphi, o provider UIA pode publicar o HWND decimal como AutomationId. Quando os valores são iguais, o AutomationId permanece nas propriedades para diagnóstico, mas é omitido do `LocatorSet`: ele não é estável entre execuções e duplicaria o locator de HWND.

HWNDs são novamente validados contra `Name`, `ClassName` e `ControlType`. Se o Windows reciclar um handle para outro controle, o resolver descarta esse resultado e continua pelas propriedades semânticas. Elementos sem HWND não recebem um locator `NativeWindowHandle=0`.

Para o Recorder, `InspectActionableAtAsync` promove um filho puramente visual ao primeiro ancestral do mesmo processo que exponha `Invoke`, `SelectionItem`, `Toggle`, `ExpandCollapse` ou ação Legacy. `InspectAtAsync` continua retornando o elemento exato para não reduzir a precisão diagnóstica do Hover Inspector.

Campos `ControlType=Edit` são preservados antes dessa promoção. `GetValueAsync` consulta `ValuePattern`, mas verifica `IsPassword` antes de ler o conteúdo e retorna valor nulo para campos protegidos. `PhysicalClickAsync` existe para sondas/testes de gesto real; o Runner normal continua priorizando os patterns semânticos.

## Interação

- Click prefere `InvokePattern`. Para itens selecionáveis sem Invoke, usa `SelectionItem.Select()` para trazê-los à viewport e só então executa click físico com bounds validados. `LegacyIAccessible.DoDefaultAction()` é o fallback semântico seguinte.
- Input prefere `ValuePattern`; usa foco + teclado quando Value não está disponível.
- Foco usa o método semântico do AutomationElement.
- janelas minimizadas e elementos disabled geram `ElementNotInteractableException`;
- input/click físicos exigem bounds válidos dentro da janela. `IsOffscreen` isolado não é confiável em VCL.

## Screenshots

O serviço suporta tela virtual inteira, bounds da janela e bounds do elemento. O padrão de nome é `yyyyMMdd_HHmmss_component.png`.

Quando duas capturas ocorrem no mesmo segundo, um sufixo incremental evita sobrescrita.

## Extensão futura

UIA2 e interação Win32 deverão implementar as mesmas portas. `IComponentAdapter` é a fundação para adapters Delphi, mas nenhum TButton/TEdit/TDBGrid específico foi antecipado sem evidência da aplicação real.

A primeira coleta real confirmou `Invoke` em `TBitBtn`, `Toggle` em `TCheckBox` e ausência de `Grid`/`Table` em `TDBGrid`. Consulte [delphi-compatibility.md](delphi-compatibility.md).

## Integração com o Runner

O Runner não referencia FlaUI. `AgentTestAutomationDriver` converte `TestTarget` em `LocatorSet`, reusa o mesmo `IAutomationEngine` do Inspector e transforma exceptions técnicas em códigos classificáveis. Consulte [test-runner.md](test-runner.md).
