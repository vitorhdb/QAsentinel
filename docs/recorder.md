# Recorder — M3

## Objetivo

O Recorder observa cliques e alterações em campos editáveis da aplicação conectada, convertendo-os em passos semânticos que o Runner do M2 pode reproduzir. Ele não cria um segundo motor de execução e não captura teclas.

## Fluxo

```text
Recorder UI -> recorder.start -> Agent
                              -> hook Win32 filtrado por PID
                              -> UIA3 no mouse-down
                              -> alvo acionável/editável
                              -> LocatorSet
Recorder UI <- recorder.status <- RecordedStep[]
Recorder UI -> TestCase SQLite -> Runner M2
```

O par `mouse down/up` vira um `Click` quando ocorre com o botão esquerdo, em até dois segundos e com deslocamento máximo de oito pixels. Ao clicar em um `Edit` gravável, o Recorder lê o valor inicial por UIA e compara com o valor final no próximo click ou no stop; uma alteração vira `Fill`, posteriormente mapeado para `Input`. Eventos fora do processo anexado são descartados ainda na fonte Win32.

## Resolução do alvo

UI Automation pode retornar um filho visual do controle, como o `TextBlock` dentro de um botão WPF. Para gravação, `InspectActionableAtAsync` preserva primeiro `ControlType=Edit`; nos demais casos percorre os ancestrais do mesmo processo e escolhe o primeiro com pattern `Invoke`, `SelectionItem`, `Toggle`, `ExpandCollapse` ou ação Legacy. Se nenhum existir, preserva o elemento original.

O passo mantém os locators disponibilizados pelo elemento — AutomationId, HWND quando válido, Name, ClassName, ControlType, Parent e coordenadas de baixa prioridade. O replay sempre reaquiri o elemento pelo `LocatorSet`.

## Ciclo de vida e IPC

- `recorder.start`: limpa a captura anterior e instala o hook para a sessão informada;
- `recorder.status`: retorna estado e uma cópia dos passos capturados;
- `recorder.stop`: remove o hook, drena a fila e finaliza o snapshot.

Ao iniciar o Recorder, o Agent encerra o Hover Inspector para evitar duas rotinas concorrentes de inspeção. Ativar o Inspector ou anexar outro processo encerra primeiro uma gravação ativa, preservando seu snapshot. O fechamento do Desktop também tenta parar a gravação antes de encerrar o Agent.

## Segurança e limites

- somente botão esquerdo;
- somente o PID da sessão ativa;
- sem hook ou captura de teclado;
- campos UIA `IsPassword` nunca têm seu valor lido;
- metadados de senha, segredo, token, API key, PIN, CVV, CPF/CNPJ, cartão ou conta bancária são bloqueados;
- valores acima de 4 KiB são descartados;
- sem double-click, drag, scroll ou assertions gravadas;
- exige sessão Windows interativa e mesmo nível de integridade do alvo.

Um fluxo que navega por Tab entre vários campos captura apenas o campo inicialmente armado até o próximo click/stop. Observação de mudança de foco sem hook de teclado fica para outro incremento.

## Validação

Os testes headed capturam e reproduzem um click em `btnClear`, além de gravar um `Fill` em `txtName`. Um PasswordBox recebe conteúdo fictício, mas o snapshot mantém somente o click e `GetValueAsync` confirma `IsPassword=True` com valor nulo.

No Argus de homologação, a sonda gravou `Grupos Operacionais -> Horário de Funcionamento`, confirmou dois `TreeItem` semânticos e restaurou a tela inicial. O relatório está em `artifacts/recorder-probes/argus-homologacao/20261002_115713_Argus_recorder_probe.json`.
