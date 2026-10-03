# QA Sentinel

QA Sentinel é uma plataforma incremental de automação de QA para aplicações Windows, com foco inicial em Delphi VCL. O estado atual entrega dois processos isolados (`Desktop` e `Agent`), descoberta e attach de aplicações, Hover Inspector UIA3, ações, screenshots, Runner persistente do M2 e o primeiro incremento funcional do Recorder M3.

O objetivo imediato continua sendo automação confiável. O M3 grava cliques e preenchimentos semânticos filtrados pelo processo alvo, sem instalar hook de teclado; API, relatórios e IA permanecem fora deste corte.

## Arquitetura

```text
QASentinel.Desktop (WPF/MVVM)
        │
        │ Named Pipes · contratos versionados
        ▼
QASentinel.Agent
        │
        ├── Runner (smart wait, retry e assertions)
        ├── Recorder (hook Win32, normalização e locators)
        ├── FlaUI / UIA3
        ├── Win32 (processos, cursor, highlight)
        └── Screenshot
        │
        ▼
Aplicação Windows / Delphi
```

Consulte [docs/architecture.md](docs/architecture.md), [docs/m2-architecture-review.md](docs/m2-architecture-review.md), [docs/m3-architecture-review.md](docs/m3-architecture-review.md), [docs/test-runner.md](docs/test-runner.md), [docs/recorder.md](docs/recorder.md), [docs/automation-engine.md](docs/automation-engine.md), [docs/inspector.md](docs/inspector.md) e [docs/delphi-compatibility.md](docs/delphi-compatibility.md) para os limites e decisões técnicas.

## Pré-requisitos

- Windows 10, Windows 11 ou Windows Server com sessão gráfica interativa;
- .NET 10 SDK;
- privilégios no mesmo nível de integridade da aplicação testada.

O `global.json` fixa o SDK `10.0.401`. Valide a instalação com `dotnet --info`.

## Compilar e testar

```powershell
dotnet tool restore
dotnet restore QASentinel.sln
dotnet build QASentinel.sln --no-restore
dotnet test QASentinel.sln --no-build --no-restore
```

Os testes de integração usam uma sessão gráfica real: validam o handshake Named Pipe, iniciam o Test Playground, anexam via UIA3, executam um teste assíncrono pelo Agent, digitam em um TextBox, avaliam uma assertion, capturam imagem, exercitam o overlay e validam a captura/reprodução de um clique pelo hook do Recorder.

Para uma coleta UIA3 somente leitura de uma aplicação real, use o modo `--compatibility-probe` do Agent. O comando e os limites estão documentados em [docs/delphi-compatibility.md](docs/delphi-compatibility.md). Relatórios brutos devem ficar em `artifacts/compatibility`, pois podem conter textos exibidos pela aplicação alvo.

O modo `--calibrate-process` valida round-trip de locators, foco, highlight, screenshots e, opcionalmente, uma navegação controlada com retorno. Ele restaura temporariamente janelas minimizadas e devolve o aplicativo ao estado anterior. Evidências ficam em `artifacts/calibration`.

## Executar

```powershell
dotnet run --project src/QASentinel.Desktop/QASentinel.Desktop.csproj
```

O Desktop inicia o Agent automaticamente. No Dashboard, use **Iniciar Test Playground** e siga o fluxo descrito em [docs/getting-started.md](docs/getting-started.md).

Logs estruturados ficam em `%LOCALAPPDATA%\QASentinel\logs`. O banco SQLite fica em `%LOCALAPPDATA%\QASentinel\qasentinel.db`. Screenshots ficam, por padrão, em `%USERPROFILE%\Pictures\QA Sentinel\Screenshots`.

## Estado atual

Implementado:

- shell WPF, navegação e Design System;
- projetos e aplicações persistidos em SQLite/EF Core com migration;
- Agent isolado e handshake Named Pipes;
- descoberta de processos com PID, título, caminho e arquitetura;
- attach, seleção Win32 da janela top-level visível e sessão UIA3;
- Hover Inspector, propriedades, patterns e overlay click-through;
- click, foco, input e screenshot de aplicação/elemento;
- Test Playground e testes automatizados;
- sonda de compatibilidade UIA3 com exportação JSON/Markdown, validada no Argus Delphi real;
- TestCase/TestStep, Runner no Agent, retry, smart waits e assertions elementares;
- operações assíncronas `execution.start/status/cancel` pelo Named Pipe;
- casos e resultados persistidos no SQLite por migrations;
- telas de Testes e Execução integradas ao Inspector;
- Recorder M3 de cliques com hook Win32 filtrado por PID, alvo acionável UIA3 e IPC start/status/stop;
- captura `Fill` por comparação do valor UIA inicial/final, sem registrar teclas e ignorando campos sensíveis;
- geração de caso persistente a partir de Click/Fill e replay pelo Runner do M2;
- sonda read-only do Runner validada no Argus real.

Ainda não implementado: double-click, drag, scroll, múltiplos campos navegados somente por Tab, UIA2/Win32 como fallback de interação, Tree Inspector, assertions de valor/grid/janela, grids Delphi especializados, vídeo, API Monitor, relatórios e IA.
