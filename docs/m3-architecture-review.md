# Architecture Review — M3 Recorder

## 1. Objetivo do primeiro incremento

O M3 transforma interações manuais observadas no aplicativo alvo em passos semânticos reaproveitáveis pelo Runner do M2. O primeiro corte registra cliques do processo anexado; o segundo captura `Fill` comparando o valor inicial/final de um campo UIA, sem observar teclas.

## 2. Arquitetura

```text
Desktop Recorder UI
        |
        | Named Pipe
        v
Agent Recorder Coordinator
        |
        +-- IPointerInputSource -> hook Win32 filtrado pelo PID
        +-- IAutomationEngine -> elemento sob o ponto
        +-- Event normalizer -> RecordedStep
        |
        v
TestCase/TestStep -> M2 Runner
```

Será criado `QASentinel.Recorder` porque o Recorder passou a ser escopo ativo e possui ciclo de vida, modelos e regras de normalização próprios. O projeto depende das abstrações de automação, mas não de WPF, Named Pipes ou P/Invoke. O hook concreto permanece em `QASentinel.Automation.Win32`.

## 3. Decisões mantidas

- o Desktop não instala hooks nem acessa UI Automation;
- todo evento é filtrado pelo PID da sessão antes de sair da camada Win32;
- coordenadas são usadas apenas para descobrir o elemento no momento da gravação;
- o passo persistido utiliza `LocatorSet`, não coordenadas como estratégia principal;
- filhos puramente visuais são promovidos ao primeiro ancestral com pattern acionável;
- a reprodução reutiliza o Runner do M2; não haverá um segundo motor de replay;
- start/status/stop serão operações IPC curtas, sem manter a conexão bloqueada.

## 4. Segurança e privacidade

O Recorder mantém somente o hook do botão esquerdo. Captura textual global não foi adicionada. O `Fill` semântico exige:

- filtro obrigatório pelo processo e sessão ativa;
- detecção de campos de senha;
- política para mascarar ou ignorar dados sensíveis;
- `ValuePattern` gravável e alteração efetiva do valor;
- limite local de 4 KiB por valor.

Campos com `IsPassword` não têm seu valor lido. Metadados associados a senha, segredo, token, PIN, documentos e cartão também são bloqueados antes da leitura. Essa estratégia evita transformar o Agent em um keylogger acidental.

## 5. Riscos

- telas que mudam imediatamente após o mouse-up podem remover o elemento antes da resolução;
- owner-drawn controls podem resolver apenas o container;
- hooks globais exigem message loop dedicado e remoção determinística;
- aplicações elevadas continuam sujeitas a UIPI;
- cliques repetidos e double-click precisarão de normalização própria em incremento posterior.

## 6. Escopo implementado agora

1. `QASentinel.Recorder` e contratos de gravação.
2. Hook Win32 de mouse filtrado por PID.
3. Normalização mouse down/up em um passo `Click`.
4. Resolução automática do elemento e preservação dos locators.
5. IPC start/status/stop.
6. Tela Recorder com passos capturados e geração de caso persistente.
7. Replay pelo Runner existente.
8. Testes unitários do normalizador e integração no Test Playground.
9. `Fill` semântico para campos `Edit`, convertido em `Input` ao salvar o caso.
10. Política de campos sensíveis e PasswordBox de teste.
11. Sonda reversível do Recorder no Argus de homologação.

Durante o teste integrado, o hit-test do botão WPF retornou inicialmente seu `TextBlock` interno. A resolução foi corrigida no adapter UIA3 com uma operação específica para alvo acionável; o Hover Inspector continua retornando o elemento exato sob o cursor.

## 7. Fora deste corte

Hook de teclado não está planejado para o fluxo normal. Double-click, drag, scroll, múltiplos fills navegados exclusivamente por Tab, menus especiais e gravação de assertions permanecem para próximos incrementos.
