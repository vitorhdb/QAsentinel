# Getting started

## 1. Preparar

Abra PowerShell na raiz e valide o SDK:

```powershell
dotnet --version
```

O resultado deve começar com `10.`. Depois:

```powershell
dotnet tool restore
dotnet restore QASentinel.sln
dotnet build QASentinel.sln
```

## 2. Executar o cenário do MVP

1. Execute `dotnet run --project src/QASentinel.Desktop/QASentinel.Desktop.csproj`.
2. No Dashboard, clique em **Iniciar Test Playground**.
3. Abra **Inspector** na sidebar.
4. Clique em **Atualizar processos**.
5. Selecione `QASentinel.TestPlayground` e clique em **Conectar**.
6. Clique em **Ativar Inspector**.
7. Mova o cursor sobre o botão Salvar ou o campo Nome no Playground.
8. Confirme Name, AutomationId, ClassName, ControlType, HWND, bounds e patterns.
9. Com o campo Nome selecionado, informe um texto e clique em **Digitar texto**.
10. Com o botão Salvar selecionado, clique em **Clicar**.
11. Feche a MessageBox do Playground.
12. Clique em **Screenshot** ou **Screenshot da aplicação**.

## 3. Criar e executar um caso M2

1. Crie um projeto em **Projetos**, se ainda não houver um.
2. No **Inspector**, selecione um elemento, escolha a ação e clique em **Adicionar ao teste**.
3. Repita para compor o fluxo. Para `Input`, o texto informado no Inspector vira a entrada do passo.
4. Abra **Testes**, selecione o projeto, nomeie o caso e clique em **Salvar caso**.
5. Mantenha o Test Playground ou a aplicação alvo conectado.
6. Abra **Execução**, selecione o caso e clique em **Executar**.
7. Acompanhe status, tentativas e duração por passo.
8. Confirme o resultado em **Execuções recentes**. `Failed` significa assertion não atendida; `Error` significa falha técnica da automação.

## 4. Gravar e reproduzir Click/Fill no M3

1. Crie ou selecione um projeto e conecte a aplicação alvo pelo **Inspector**.
2. Abra **Gravador**; a sessão permanece conectada ao sair do Inspector.
3. Selecione o projeto, informe o nome do caso e clique em **Iniciar**.
4. Clique nos controles desejados e preencha campos `Edit` não sensíveis dentro da aplicação alvo.
5. Volte ao QA Sentinel e clique em **Parar**.
6. Confirme os elementos, classes e tipos na lista de passos.
7. Clique em **Salvar e abrir Execução**.
8. Selecione o caso gravado e execute-o pelo Runner.

O hook ignora cliques fora do PID conectado. O `Fill` é produzido pela diferença entre valor inicial e final do campo: nenhuma tecla é capturada. PasswordBox e metadados de senha, token, PIN, CPF/CNPJ ou cartão são ignorados.

## 5. Diagnóstico

- Se o processo não aparecer, confirme que possui uma janela principal visível.
- Se o alvo estiver elevado, execute o QA Sentinel no mesmo nível de privilégio.
- Se o componente não tiver propriedades úteis, registre ClassName, ControlType, HWND, bounds e patterns antes de decidir por UIA2/Win32.
- Consulte `%LOCALAPPDATA%\QASentinel\logs\Agent-*.log` e `Desktop-*.log`.

## 6. Testes

```powershell
dotnet test QASentinel.sln
```

`Uia3PlaygroundTests`, o overlay e o Runner Named Pipe abrem janelas reais. Eles requerem uma sessão Windows interativa e não foram projetados para agentes headless.
