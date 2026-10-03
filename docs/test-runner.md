# Test Runner — M2

## Fluxo

O Desktop envia uma definição imutável ao Agent e recebe o `ExecutionId` imediatamente.

```text
execution.start -> Running
execution.status -> progresso
execution.cancel -> solicitação de cancelamento
execution.status -> Passed | Failed | Error | Cancelled
```

O Runner vive em `QASentinel.Application` e depende apenas de `ITestAutomationDriver`. O adapter do Agent converte os alvos neutros em `LocatorSet` e chama `IAutomationEngine`; nenhuma referência FlaUI chega ao Runner ou ao Desktop.

## Ações disponíveis

- `Click`
- `Focus`
- `Input`
- `WaitForElement`
- `AssertExists`
- `AssertVisible`
- `AssertEnabled`

As ações interativas esperam o elemento existir e estar habilitado antes de agir. `AssertVisible` considera a interseção física entre elemento e janela, além de `IsOffscreen`, devido ao comportamento observado no Delphi VCL.

## Retry e smart wait

Cada passo possui timeout e `MaxAttempts`. O smart wait consulta o elemento periodicamente com `CancellationToken`; o retry de interação usa backoff de 200 ms, 500 ms, 1 s e 2 s. Não há `Thread.Sleep` no pipeline do Runner.

Falhas técnicas transitórias podem ser repetidas. Assertions não são repetidas como ações: sua condição é observada até o timeout.

## Classificação do resultado

- `Passed`: ação concluída ou condição satisfeita.
- `Failed`: o produto não atingiu uma assertion dentro do timeout.
- `Error`: a automação não localizou/interagiu ou encontrou falha técnica.
- `Cancelled`: cancelamento solicitado ou limite global encerrado.
- `Skipped`: passo posterior não executado após falha ou erro.

Essa separação evita reportar defeito do produto quando o problema pertence ao motor de automação.

## Persistência

A migration `M2Automation` adiciona:

- `TestCases`
- `TestSteps`
- `TestExecutions`
- `StepExecutions`

Os locators do alvo são um value object do Domain e são serializados em JSON somente no adapter de persistência. Definições e resultados continuam fortemente tipados fora do SQLite.

## Limites atuais

- progresso em andamento existe somente na memória do Agent;
- o resultado final é persistido pelo Desktop;
- execução distribuída, retomada após crash e fila ficam para fases posteriores;
- assertions de valor, seleção, janela e grid ainda não fazem parte deste corte;
- passos produzidos pelo Recorder M3 são `Click` e `Input` (originado de `Fill`).

## Sonda read-only em aplicação real

O Agent possui um modo de diagnóstico que executa apenas `WaitForElement`, `AssertExists`, `AssertVisible` e `AssertEnabled`, sem click ou input:

```powershell
src\QASentinel.Agent\bin\Debug\net10.0-windows\QASentinel.Agent.exe `
  --runner-probe 12840 `
  --target-name OK `
  --target-class Button `
  --target-control-type Button `
  --output artifacts\runner-probes\argus
```

O PID e os locators devem ser substituídos pelos valores da sessão. O relatório JSON registra o resultado e cada tentativa; textos da aplicação podem conter dados sensíveis e não devem ser versionados sem revisão.
