# Architecture Review — M2 Automation

## 1. Entendimento do marco

O M2 transforma a fundação de inspeção do M1 em automação repetível. O corte inclui casos e passos de teste, execução pelo Agent, assertions elementares, retry, smart waits, persistência SQLite e resultados que distinguem falha do produto de falha da automação.

Recorder, vídeo, relatório HTML, UIA2, visão computacional e self-healing permanecem fora deste marco.

## 2. Arquitetura mantida

- `QASentinel.Desktop` edita casos, inicia execuções e apresenta resultados.
- `QASentinel.Application` contém o pipeline do Runner e as portas de persistência e automação.
- `QASentinel.Domain` contém o modelo de teste e execução, sem dependências de WPF, FlaUI, IPC ou EF Core.
- `QASentinel.Agent` hospeda o Runner e é o único processo autorizado a usar o motor de automação Windows.
- `QASentinel.Contracts` transporta DTOs explícitos pelo Named Pipe.
- `QASentinel.Infrastructure` persiste definições e resultados no SQLite.

Fluxo do M2:

```text
Desktop -> Named Pipe -> Agent -> Runner -> UIA3 -> aplicação alvo
   |                                      |
   +------------ SQLite <--- resultado ---+
```

## 3. Decisão relevante

O Runner será hospedado no Agent, não no Desktop. Colocá-lo no Desktop faria a orquestração depender de chamadas de automação locais e enfraqueceria o isolamento entre apresentação e Windows Automation. O Desktop enviará uma definição imutável do teste, receberá um identificador imediatamente e consultará o progresso por IPC. Isso também evita manter uma chamada Named Pipe aberta durante toda a execução.

O pipeline será implementado em `QASentinel.Application`, atrás de `ITestAutomationDriver`. O adapter concreto ficará no Agent e encapsulará `IAutomationEngine`. Não será criado um projeto `QASentinel.Runner` ainda: o volume atual não justifica outro assembly e a extração futura continua simples.

## 4. Riscos e mitigação

- **Seletores Delphi instáveis:** cada passo preserva múltiplos locators ordenados por score; HWND não será a única estratégia.
- **Atrasos de UI:** cada passo possui timeout e usa polling cancelável; não haverá `Sleep` fixo no pipeline.
- **Falha do produto versus automação:** assertion não satisfeita produz `Failed`; incapacidade técnica de localizar ou interagir produz `Error`.
- **Execuções longas no IPC:** `execution.start` retorna imediatamente; status e cancelamento são operações separadas.
- **Reexecução destrutiva:** o M2 não cria ações destrutivas nem executa automaticamente em produção.
- **Persistência parcial:** a definição e o resultado final serão transacionais; progresso em memória poderá ser perdido se o Agent for encerrado, limitação documentada para este marco local.

## 5. Escopo implementado agora

1. Modelo `TestCase`, `TestStep`, `TestExecution` e `StepExecution`.
2. Runner com Click, Focus, Input, WaitForElement, AssertExists, AssertVisible e AssertEnabled.
3. Retry com backoff e smart wait cancelável.
4. Operações IPC de iniciar, consultar e cancelar execução.
5. Repositórios e migration SQLite para casos e resultados.
6. Telas mínimas de Testes e Execução, integradas ao Inspector para reutilizar o elemento atual.
7. Testes unitários do pipeline, retry, assertions, serialização e persistência; integração pelo Agent/Test Playground.

## 6. Critério de saída do incremento

Um elemento capturado pelo Inspector poderá compor um caso persistido. Com o Test Playground anexado, o Agent executará seus passos, o Desktop acompanhará o estado e o SQLite armazenará o resultado e os detalhes de cada passo.
