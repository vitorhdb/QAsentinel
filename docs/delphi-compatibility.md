# Compatibilidade Delphi VCL

## Escopo da coleta

Coletas executadas em 30/09/2026 e 02/10/2026 contra `C:\Argus\Argus.exe`, com UIA3 e enumeração Win32 de janelas top-level. O relatório bruto permanece em `artifacts/compatibility`, ignorado pelo Git, porque nomes de elementos podem conter dados do ambiente de homologação.

## Resultado

O `Process.MainWindowHandle` apontava para a janela oculta `TApplication`, sem bounds e sem descendentes UIA. A seleção passou a enumerar todas as janelas top-level do PID e priorizar a janela visível, com bounds válidos e que não seja `TApplication`. A janela correta encontrada foi `TFAdministrativo`.

Na coleta final, UIA3 expôs 137 elementos. A contagem variou ligeiramente durante a sessão por causa do conteúdo dinâmico da tela:

| Sinal | Resultado | Leitura |
| --- | ---: | --- |
| Elementos UIA | 137 | A árvore principal é navegável. |
| AutomationId preenchido | 66 (48,2%) | 64 valores eram o HWND em decimal e, portanto, efêmeros. |
| HWND nativo | 66 (48,2%) | Bom fallback dentro da sessão atual. |
| Elementos com algum pattern | 137 (100%) | Muitos possuem apenas `LegacyIAccessible`. |
| Sem pattern semântico além de legado/janela | 56 (40,9%) | Exigem cautela antes de interação. |
| Classes Delphi `T*` | 66 (48,2%) | ClassName é um sinal útil, mas não identifica sozinho uma instância. |

## Matriz observada

| Componente | Exposição UIA3 | Patterns relevantes | Decisão atual |
| --- | --- | --- | --- |
| `TBitBtn` | `Button`, Name presente em botões textuais, HWND | `Invoke` | Suportado por UIA3; validar click em cenário controlado. |
| `TCheckBox` | `CheckBox`, HWND | `Toggle`, `Invoke` | Suportado por UIA3. |
| `TTreeView` / itens | `Tree` e `TreeItem`, Name nos itens | `Selection`, `SelectionItem`, alguns `Invoke` | Bom candidato a locator `Name + Parent`; ainda falta modelar o parent estrutural. |
| `TPageControl` / tabs | `Tab` e `TabItem`, Name nas tabs | `Selection`, `SelectionItem` | Suportado por UIA3. |
| `TListView` | `List`, HWND | `Selection` | Parcial; itens não foram caracterizados nesta tela. |
| `TDBGrid` | Apenas `Pane`, ClassName, HWND e bounds | `LegacyIAccessible` | Gap confirmado: linhas/células não aparecem como `Grid`/`Table`. Não criar adapter antes de um cenário de leitura definido. |
| `TEdit` / `TMaskEdit` | `Edit`, HWND e bounds | `Value`, `LegacyIAccessible` | Exposição suficiente para `Fill`; escrita no Argus continua bloqueada até existir um dado descartável explícito. |
| `TComboBox` | Não presente nas telas coletadas | — | Pendente de coleta. |

## Decisões derivadas

- Win32 fica responsável por descobrir o HWND top-level correto; UIA3 continua responsável pela árvore e interação.
- AutomationId numérico idêntico ao HWND continua visível para diagnóstico, mas não entra no `LocatorSet` como identificador estável.
- HWND é válido para reaquisição durante a sessão, não para persistência entre execuções.
- `TDBGrid` não receberá um adapter genérico nesta etapa. Primeiro serão registrados objetivo de leitura, estrutura MSAA disponível e mensagens Win32 suportadas pelo componente real.
- Coordenadas negativas foram observadas porque a janela estava em um monitor à esquerda do monitor primário. Highlight e screenshot devem continuar usando coordenadas da tela virtual.

## Calibração interativa controlada

A calibração foi executada sem gravação de dados e sem digitação no Argus:

1. o Argus estava minimizado; foi restaurado temporariamente e devolvido ao estado minimizado ao final;
2. o item `Grupos Operacionais` foi resolvido, focado e destacado;
3. o item-filho `Grupo 01 - Teste - (301)` foi invocado e abriu a tela do grupo;
4. nessa tela, o `TBitBtn` `Colunas` foi resolvido novamente, recebeu foco e highlight, sem executar seu `Invoke`;
5. a navegação voltou visualmente para `Horário de Funcionamento`;
6. screenshots da aplicação (`1382x736`) e dos elementos foram verificadas visualmente.

Gaps encontrados e corrigidos durante a calibração:

- bounds de janela minimizada (`160x28` e coordenadas próximas de `-32000`) não podem ser usados para highlight/screenshot;
- HWND reciclado precisava ser validado também por Name e ControlType;
- elementos virtuais recebiam indevidamente `NativeWindowHandle=0` como locator de alta prioridade;
- `IsOffscreen` do provider VCL não é suficiente para decidir se um clique físico é seguro;
- `SelectionItem.Select()` sozinho muda a seleção, mas não dispara necessariamente o evento VCL. O fallback agora seleciona, confirma a viewport e executa o clique físico.

Evidência estruturada final: `artifacts/calibration/argus/20260930_204317_Argus_calibration.json`.

## Validação do Recorder em homologação

Em 02/10/2026, a instância `ARGUS HOMOLOGAÇÃO - DB-01` foi usada para um fluxo sem alteração de dados:

1. `Horário de Funcionamento` foi estabelecido como baseline;
2. o Recorder iniciou com hook filtrado pelo PID `9676`;
3. um click físico navegou para `Grupos Operacionais`;
4. outro click físico retornou para `Horário de Funcionamento`;
5. o snapshot conteve exatamente dois `Click`, ambos `TreeItem` resolvidos por Name + Parent + ControlType;
6. screenshots confirmaram visualmente a navegação e a restauração.

A mesma coleta encontrou um `TEdit` (`HelpText=Buscar Log`) e dois `TMaskEdit`, todos com `ValuePattern`. Nenhum valor foi alterado: o `Fill` foi validado somente no Test Playground até existir um campo de homologação com conteúdo explicitamente descartável.

Evidência: `artifacts/recorder-probes/argus-homologacao/20261002_115713_Argus_recorder_probe.json`.

## Como repetir

```powershell
$env:DOTNET_ROOT = "C:\Users\vitor\.dotnet-qa-sentinel"
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
src\QASentinel.Agent\bin\Debug\net10.0-windows\QASentinel.Agent.exe `
  --compatibility-probe <PID> `
  --output artifacts\compatibility `
  --timeout-seconds 45 `
  --max-depth 16 `
  --max-elements 5000
```

O comando gera JSON para análise automatizada e Markdown para leitura humana. A sonda não clica, não digita e não altera o aplicativo alvo.

Exemplo da calibração controlada executada:

```powershell
src\QASentinel.Agent\bin\Debug\net10.0-windows\QASentinel.Agent.exe `
  --calibrate-process <PID> `
  --target-name "Grupo 01 - Teste - (301)" `
  --invoke-navigation `
  --post-navigation-target-name "Colunas" `
  --post-navigation-target-class "TBitBtn" `
  --restore-name "Horário de Funcionamento" `
  --output artifacts\calibration\argus
```

Exemplo da sonda reversível do Recorder:

```powershell
src\QASentinel.Agent\bin\Debug\net10.0-windows\QASentinel.Agent.exe `
  --recorder-probe <PID> `
  --target-name "Grupos Operacionais" `
  --restore-name "Horário de Funcionamento" `
  --output artifacts\recorder-probes\argus-homologacao `
  --timeout-seconds 90
```
