# Architecture Review

## 1. Entendimento do projeto

QA Sentinel é uma plataforma Windows de automação e assistência de QA. O M1 provou a cadeia essencial para Delphi; o M2 acrescentou definições persistentes e execução repetível; o primeiro incremento do M3 agora transforma cliques manuais em passos semânticos sem mover automação para o Desktop.

Testes, Execução e gravação de Click/Fill são módulos funcionais. Hook de teclado, API, Reporting e IA permanecem fora deste corte.

## 2. Arquitetura

| Projeto | Responsabilidade | Dependências relevantes |
|---|---|---|
| `QASentinel.Desktop` | WPF, MVVM, navegação e cliente do Agent | Application, Contracts, Infrastructure |
| `QASentinel.Agent` | Host de automação, Runner, Recorder e servidor IPC | Application, UIA, Win32, Inspector, Recorder |
| `QASentinel.Contracts` | DTOs e envelopes do protocolo | nenhuma camada externa |
| `QASentinel.Domain` | projetos, casos, passos e resultados | nenhuma |
| `QASentinel.Application` | Runner, portas, cliente Named Pipe e erros amigáveis | Domain, Contracts |
| `QASentinel.Automation` | modelos e abstrações neutras | Domain |
| `QASentinel.Automation.UIA` | adapter FlaUI/UIA3 | Automation |
| `QASentinel.Automation.Win32` | processos, cursor, overlay e screenshot | Automation |
| `QASentinel.Inspector` | polling, deduplicação e ciclo de highlight | Automation, Win32 |
| `QASentinel.Recorder` | ciclo de gravação, normalização e passos semânticos | Automation |
| `QASentinel.Infrastructure` | SQLite, EF Core, repositories e migrations | Application, Domain |
| `QASentinel.Observability` | configuração Serilog | abstrações de host/logging |
| `QASentinel.TestPlayground` | alvo WPF controlado | WPF |

Dependências externas ficam nos adapters. Objetos FlaUI nunca chegam ao Desktop nem atravessam o IPC.

## 3. Fluxo

1. Desktop inicia o Agent companheiro.
2. Cliente abre o Named Pipe e faz handshake da versão do protocolo.
3. Desktop solicita processos com janela visível.
4. Agent anexa via FlaUI e cria uma `ApplicationSession`.
5. Hover Inspector amostra o cursor a cada 120 ms, consulta UIA3 e filtra pelo PID da sessão.
6. Agent atualiza o overlay e mantém o último `AutomationElementInfo` neutro.
7. Desktop consulta o snapshot a cada 160 ms.
8. Ações enviam `SessionId + LocatorSet`; o Agent reaquiri o elemento antes de agir.
9. O Desktop envia um caso por `execution.start`; o Agent executa o pipeline em background.
10. O Desktop consulta o progresso, apresenta a classificação e persiste o resultado final.
11. No Recorder, o Agent instala um hook de mouse filtrado pelo PID, resolve o alvo semântico via UIA3 e publica passos `Click` ou `Fill` por polling IPC.
12. O Desktop converte os passos gravados em `TestCase` e delega o replay ao mesmo Runner do M2.

## 4. Riscos

- **Delphi VCL:** componentes owner-drawn podem expor apenas um HWND/container.
- **Controles customizados:** AutomationId, Name e patterns podem ser ausentes ou instáveis.
- **Grids:** TDBGrid/DevExpress/TMS exigem análise individual antes de um adapter.
- **UIPI:** Agent sem elevação não automatiza de forma confiável um alvo elevado.
- **UIA síncrono:** chamadas COM podem atrasar; o isolamento do Agent limita o impacto no Desktop.
- **Janelas:** Delphi expõe `TApplication` oculta como `Process.MainWindowHandle`; a seleção precisa considerar todas as top-level do PID.
- **DPI/múltiplos monitores:** overlay usa coordenadas físicas Win32; a validação Delphi deve incluir escalas diferentes e coordenadas negativas.
- **Estado da janela:** UIA pode devolver bounds próximos de `-32000,-32000` para aplicações minimizadas; calibração restaura temporariamente e ações normais rejeitam entrada física nesse estado.
- **Flags VCL inconsistentes:** `IsOffscreen` pode ser `true` para item visível; ações físicas exigem também interseção real dos bounds com a janela.
- **Controles compostos:** o hit-test pode retornar um filho visual (`TextBlock`) em vez do controle acionável; o Recorder promove o alvo ao primeiro ancestral com pattern de ação.
- **Privacidade:** não existe hook de teclado; `Fill` compara valores UIA somente para `Edit` não sensível e nunca lê `IsPassword`.

## 5. Decisões mantidas

- .NET 10, WPF, CommunityToolkit.Mvvm;
- Desktop e Agent separados;
- Named Pipes local;
- UIA3/FlaUI como primeiro backend;
- locators múltiplos e automação semântica;
- SQLite/EF Core e Serilog;
- xUnit e integração headed.

## 6. Melhorias necessárias aplicadas

- protocolo IPC versionado, correlacionado, limitado e restrito ao usuário atual;
- reaquisição do elemento por LocatorSet em toda ação;
- polling com intervalo mínimo, deduplicação e overlay `no-activate/click-through`;
- timeout e `CancellationToken` em operações assíncronas;
- erros técnicos tipados no Agent e mensagens amigáveis no Desktop;
- migration inicial limitada a Projects, Applications e Settings.
- Runner atrás de `ITestAutomationDriver`, hospedado no Agent, com start/status/cancel não bloqueantes;
- migration M2 limitada a casos, passos, execuções e resultados de passo;
- enumeração Win32 de janelas top-level com seleção determinística da janela visível, evitando attach na `TApplication` oculta;
- sonda de compatibilidade limitada por profundidade, quantidade de elementos e timeout.
- calibração controlada com restauração do estado minimizado, round-trip de locators, foco, highlight e evidências;
- validação semântica de HWND contra Name/ClassName/ControlType e fallback de TreeItem via SelectionItem + click físico seguro.
- hook de mouse com message loop dedicado, remoção determinística e filtro de PID antes de publicar eventos;
- resolução específica de alvo acionável/editável, sem alterar o nível de detalhe do Hover Inspector;
- captura de preenchimento por valor final, com bloqueio de senha/token/PIN/documentos e limite de 4 KiB;
- replay do Recorder reutilizando `TestCase`, `TestStep` e Runner existentes.

## 7. Escopo implementado

Estrutura da solução, DI, observabilidade, WPF shell, Design System, Agent, handshake, processos, attach, sessão, UIA3, Hover Inspector, highlight, click, foco, input, screenshot, TestCase/TestStep, Runner, assertions elementares, retry, smart wait, resultados persistentes, Recorder de Click/Fill, Playground, testes e documentação.

## Dependência entre processos

```text
Desktop ── Contracts/IPC ──► Agent ─┬─ Runner ──── Automation ports ──► UIA3 / Win32
   │                            ├─ Recorder ── hook + UIA3
   │                            └─ Inspector orchestration
   └── Application/Domain
```

O processo separado isola o Desktop de falhas e bloqueios da camada de automação. Ele não isola o aplicativo sob teste, que já é um processo externo, nem elimina requisitos de sessão interativa ou elevação.
