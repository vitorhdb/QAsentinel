# Implementation changelog

## Sprint Técnica 01

### Criado

- solução .NET 10 com 12 projetos de produção e 2 de teste;
- WPF shell, Design System e navegação;
- Agent, protocolo Named Pipes e handshake;
- processo/sessão/UIA3/Inspector/highlight;
- click, foco, input e screenshots;
- SQLite/EF Core com migration inicial;
- Test Playground;
- testes unitários e integração headed;
- documentação operacional e arquitetural.

### Alterado

- o escopo amplo do System Design foi reduzido ao technical spike e MVP Inspector;
- módulos futuros aparecem apenas como “Em desenvolvimento”;
- ações reaquiriam componentes por LocatorSet em vez de reusar referências UIA.

### Testado

- restore e build completos sem warnings;
- serialização IPC e correlação;
- ordenação/deduplicação de locators;
- mapeamento de erros;
- modelo de domínio e process mapping;
- attach real ao Playground, descoberta do TextBox, input via UIA3 e screenshot.
- criação, movimentação e ocultação do overlay click-through em runtime.

### Problemas encontrados

- a máquina não possuía SDK; foi instalado SDK 10.0.401 isolado;
- FlaUI exige TFM Windows para selecionar seus assets modernos;
- aplicações elevadas continuam sujeitas a UIPI;
- componentes Delphi reais ainda não foram medidos.

### Pendências

- executar a matriz de validação no Argus/Delphi real;
- registrar gaps de TButton, TEdit, TComboBox, TCheckBox e TDBGrid;
- implementar fallback UIA2/Win32 somente para gaps comprovados;
- melhorar seleção de janelas em processos com splash/múltiplas top-level.

### Próximo passo recomendado

Executar a aplicação Delphi real com o mesmo nível de privilégio, coletar uma matriz de exposição por componente e transformar somente as falhas reproduzíveis em adapters/fallbacks.

## Validação Delphi 01

### Criado

- sonda de compatibilidade UIA3 somente leitura, com limites e exportação JSON/Markdown;
- enumeração Win32 de janelas top-level por PID;
- heurística testável de seleção da janela principal;
- matriz de compatibilidade em `docs/delphi-compatibility.md`.

### Alterado

- attach e descoberta de processos não confiam mais exclusivamente em `Process.MainWindowHandle`;
- AutomationId numérico idêntico ao HWND deixou de ser tratado como locator persistível;
- `AutomationElementInfo` passou a incluir `FrameworkId` e `HelpText`.

### Testado

- Playground: attach, scan, input e screenshot;
- Argus real: seleção da `TFAdministrativo` e leitura de 137 elementos UIA3 na coleta final;
- exposição de `TBitBtn`, `TCheckBox`, `TTreeView`, `TPageControl`, `TListView` e `TDBGrid`;
- seleção da janela Delphi coberta por teste unitário.

### Problemas encontrados

- `Process.MainWindowHandle` apontava para a `TApplication` oculta, com bounds vazios;
- 63 AutomationIds observados eram apenas o HWND em decimal;
- `TDBGrid` expôs somente `Pane + LegacyIAccessible`, sem linhas, células, `Grid` ou `Table`;
- `TEdit` e `TComboBox` não estavam presentes na tela coletada.

### Pendências

- validar click real de `TBitBtn` e toggle de `TCheckBox` em cenário de homologação controlado;
- coletar uma tela com `TEdit` e `TComboBox`;
- definir o caso de uso do `TDBGrid` antes de escolher UIA2, MSAA, Win32 ou adapter específico.

### Próximo passo recomendado

Executar uma validação interativa controlada em uma tela sem efeito destrutivo: `TBitBtn` com `Invoke`, `TCheckBox` com `Toggle`, `TEdit` com `Value`/teclado e screenshot em monitor com coordenadas negativas.

## Calibração Delphi 02

### Criado

- comando de calibração controlada com relatório JSON e evidências de aplicação/elemento;
- abstração Win32 para estado, restauração, minimização e ativação da janela;
- restauração automática do estado minimizado original;
- etapa opcional pós-navegação para calibrar um componente sem invocá-lo.

### Alterado

- locator por HWND agora valida também Name, ClassName e ControlType;
- locator `NativeWindowHandle=0` deixou de ser criado;
- click em `TreeItem` sem Invoke usa SelectionItem para trazer o alvo à viewport antes do click físico;
- `IsOffscreen` passou a ser combinado com bounds/interseção da janela;
- screenshots simultâneas não sobrescrevem arquivos com o mesmo nome.

### Testado

- foco, highlight e screenshot do item `Grupos Operacionais`;
- navegação para `Grupo 01 - Teste - (301)` e retorno visual para `Horário de Funcionamento`;
- resolução, foco e highlight do `TBitBtn` `Colunas` sem invocar o botão;
- aplicação inicialmente minimizada restaurada somente durante a calibração;
- screenshot da aplicação com `1382x736` e screenshot do elemento `Colunas`.

### Problemas encontrados

- janelas minimizadas produzem bounds UIA inválidos para evidência;
- um HWND reciclado podia resolver outro botão da mesma classe;
- SelectionItem e LegacyIAccessible isolados não dispararam o evento de navegação VCL;
- `SetForegroundWindow` pode retornar falso, embora `AutomationElement.Focus()` funcione.

### Pendências

- calibrar `TCheckBox` apenas em uma ação cujo efeito possa ser revertido com segurança;
- localizar uma tela de homologação com `TEdit`/`TComboBox` e dados descartáveis antes de testar input;
- não invocar `Colunas`, `Alterar Horário`, `Gravar` ou outros comandos de negócio sem cenário explícito.

### Próximo passo recomendado

Expor a sonda/calibração na UI do Inspector como “Diagnóstico do componente”, mantendo ações de negócio separadas e explícitas.

## M2 — Automation 01

### Criado

- entidades `TestCase`, `TestStep`, `TestExecution` e `StepExecution`;
- Runner com Click, Focus, Input, WaitForElement e assertions Exists/Visible/Enabled;
- smart wait cancelável, retry com backoff e classificação `Failed` versus `Error`;
- operações IPC assíncronas `execution.start`, `execution.status` e `execution.cancel`;
- migration `M2Automation` e repositórios de casos/resultados;
- telas de Testes e Execução e integração “Adicionar ao teste” no Inspector;
- Architecture Review e documentação operacional do M2.

### Alterado

- o Agent passou a hospedar o Runner, preservando o Desktop sem acesso direto a UI Automation;
- a visibilidade usada pelo Runner combina `IsOffscreen` com interseção física, conforme a calibração Delphi;
- o timeout a frio do teste de overlay foi ampliado para eliminar flutuação de bootstrap WPF.

### Testado

- 23 testes unitários;
- 6 testes de integração headed;
- Runner real pelo Named Pipe contra o Test Playground com Input + AssertVisible;
- round-trip de definição e resultado no SQLite;
- startup do Desktop, aplicação automática da migration e handshake com o Agent;
- build completo com zero warnings e zero errors.

### Problemas encontrados

- o SDK 10.0.401 não estava mais instalado e foi restaurado pelo pacote oficial;
- a API nativa de controle visual não estava disponível nesta sessão, impedindo uma inspeção automatizada por screenshot das novas telas;
- progresso em andamento ainda reside somente na memória do Agent.

### Pendências

- validação visual manual das telas M2 em diferentes DPI/tamanhos;
- editor para reordenar/remover passos e ajustar timeout/retry;
- assertions de valor, seleção, janela e grid;
- persistência incremental de progresso e recuperação após crash.

### Próximo passo recomendado

Executar um caso controlado no Argus usando somente navegação e assertions não destrutivas; em seguida, ampliar assertions a partir dos padrões realmente expostos pelos componentes Delphi.

## M2 — Validação Argus real

### Criado

- comando `--runner-probe` para executar um cenário estritamente read-only contra um PID e locators informados;
- relatório JSON por passo em `artifacts/runner-probes`.

### Alterado

- nenhuma ação de negócio do Argus foi invocada; a sonda usa somente espera e assertions.

### Testado

- attach UIA3 no Argus PID `12840` e janela modal `Dados Dominio`;
- elemento `OK` localizado por Name, ClassName e ControlType;
- `WaitForElement`, `AssertExists`, `AssertVisible` e `AssertEnabled` aprovados na primeira tentativa;
- execução concluída como `Passed` em 270 ms.

### Problemas encontrados

- a sessão de controle visual externo não expôs aplicações nativas, portanto a confirmação foi feita pelo próprio Agent/UIA3 e pelo relatório estruturado, sem screenshot automatizado da UI.

### Pendências

- executar um caso de navegação controlada no Argus somente quando houver estado de homologação explicitamente reversível.

### Próximo passo recomendado

Iniciar o Recorder M3 reutilizando locators e Runner já validados.

## M3 — Recorder 01

### Criado

- projeto `QASentinel.Recorder` com fonte de eventos abstrata, normalizador e ciclo de gravação;
- hook `WH_MOUSE_LL` em thread dedicada, filtrado pelo PID da sessão;
- contratos e operações IPC `recorder.start`, `recorder.status` e `recorder.stop`;
- tela Gravador com passos ao vivo, projeto, nome e geração de caso persistente;
- documentação arquitetural e operacional do Recorder.

### Alterado

- `IAutomationEngine` ganhou inspeção de alvo acionável sem mudar o comportamento detalhado do Hover Inspector;
- o hit-test promove filhos visuais ao primeiro ancestral com pattern de ação;
- o caso gerado utiliza `TestCase/TestStep` e o replay continua no Runner do M2;
- o início da gravação encerra o Hover Inspector para evitar inspeções concorrentes.
- novo attach ou ativação do Inspector encerra deterministicamente um hook ativo da sessão anterior.

### Testado

- restore e build completos, com zero warnings e zero erros;
- 27 testes unitários e 7 testes de integração aprovados;
- normalização unitária de click válido, movimento acima do limite e alvo ausente;
- serialização dos novos contratos IPC;
- hook real capturando clique físico no `btnClear` do Test Playground;
- resolução do `Button` acionável em vez do `TextBlock` interno;
- replay do passo gravado pelo UIA3, confirmado pelo estado `Formulário limpo`.

### Problemas encontrados

- o primeiro hit-test retornou o `TextBlock` visual dentro do botão WPF;
- `SetForegroundWindow` pode retornar falso sob restrições do Windows, então o teste também aplica foco UIA ao alvo;
- a API de controle visual externa continua sem expor aplicações nativas nesta sessão.

### Pendências

- captura de teclado com proteção para password fields e dados sensíveis;
- double-click, drag, scroll e gravação de assertions;
- validação manual da tela Recorder em diferentes DPI e tamanhos.

### Próximo passo recomendado

Validar a gravação de cliques em um fluxo de navegação reversível do Argus e, depois, implementar `FILL` sem registrar teclas brutas ou conteúdo sensível.

## M3 — Recorder 02

### Criado

- sonda `--recorder-probe` com baseline, navegação, retorno e relatório JSON;
- `PhysicalClickAsync` encapsulado no adapter UIA3 para validar gesto/hook real sem alterar a estratégia normal do Runner;
- leitura neutra `AutomationValueInfo` com proteção `IsPassword`;
- política local para campos sensíveis e limite de 4 KiB;
- PasswordBox no Test Playground.

### Alterado

- o Recorder passou a gerar `Fill` comparando o valor inicial/final de um `Edit`, sem hook de teclado;
- `Fill` atravessa o IPC com o valor e vira `TestActionType.Input` ao salvar o caso;
- alvos `Edit` são preservados antes da promoção para ancestrais acionáveis;
- a tela Gravador passou a exibir Click/Fill e o valor não sensível capturado.

### Testado

- restore e build completos com zero warnings e zero erros;
- 34 testes unitários e 8 testes de integração aprovados;
- Argus homologação: `Horário de Funcionamento -> Grupos Operacionais -> Horário de Funcionamento`;
- dois clicks físicos capturados como `TreeItem`, com restauração visual comprovada por screenshot;
- `Fill` em `txtName`, persistência no snapshot e replay pelo mesmo LocatorSet;
- PasswordBox preenchido com dado fictício sem conteúdo no snapshot;
- `GetValueAsync` retornando `IsPassword=True` e `Value=null`;
- bloqueio unitário para senha, token, PIN, CPF e valor acima do limite.

### Problemas encontrados

- `SelectionItem.Select()` podia navegar sem produzir gesto observável pelo hook;
- a promoção de ancestrais inicialmente convertia um TextBox no `TabItem` pai;
- foco WPF recém-aberto precisava de estabilização curta antes do click físico.
- testes headed precisavam ser serializados e aguardar o provider UIA após `WaitForInputIdle`.

### Pendências

- fills em múltiplos campos percorridos exclusivamente por Tab;
- double-click, drag, scroll e assertions gravadas;
- validar `Fill` em Delphi somente quando houver um campo com dado descartável autorizado.

### Próximo passo recomendado

Adicionar observação de mudança de foco UIA para consolidar múltiplos `Fill` sem hook de teclado e criar edição/reordenação dos passos antes de ampliar os gestos do Recorder.
