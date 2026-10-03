using System.Text.Json;
using QASentinel.Contracts;

namespace QASentinel.UnitTests;

public sealed class IpcSerializationTests
{
    [Fact]
    public void RequestEnvelope_RoundTripsWithoutLosingCorrelationOrPayload()
    {
        var requestId = Guid.NewGuid();
        var envelope = new IpcRequestEnvelope(
            AgentProtocol.Version,
            requestId,
            AgentOperations.Attach,
            ContractJson.ToElement(new AttachApplicationRequest(14920, 4500)));

        var json = JsonSerializer.Serialize(envelope, ContractJson.Options);
        var roundTrip = JsonSerializer.Deserialize<IpcRequestEnvelope>(json, ContractJson.Options);
        var payload = roundTrip!.Payload.Deserialize<AttachApplicationRequest>(ContractJson.Options);

        Assert.Equal(requestId, roundTrip.RequestId);
        Assert.Equal(AgentOperations.Attach, roundTrip.Operation);
        Assert.Equal(14920, payload!.ProcessId);
        Assert.Equal(4500, payload.TimeoutMilliseconds);
    }

    [Fact]
    public void ElementContract_SerializesLocatorEnumsAsReadableStrings()
    {
        var dto = new LocatorSetDto([new ElementLocatorDto(LocatorKindDto.AutomationId, "btnSave", 100)]);

        var json = JsonSerializer.Serialize(dto, ContractJson.Options);

        Assert.Contains("automationId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"kind\":0", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutionRequest_RoundTripsWithoutDomainEntities()
    {
        var request = new StartTestExecutionRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Preencher e validar",
            [
                new ExecutableTestStepDto(
                    Guid.NewGuid(),
                    1,
                    TestActionDto.Input,
                    new LocatorSetDto(
                    [
                        new ElementLocatorDto(LocatorKindDto.AutomationId, "txtDescription", 100),
                        new ElementLocatorDto(LocatorKindDto.Name, "Descrição", 85)
                    ]),
                    "QA Sentinel",
                    5000,
                    3)
            ]);

        var json = JsonSerializer.Serialize(request, ContractJson.Options);
        var roundTrip = JsonSerializer.Deserialize<StartTestExecutionRequest>(json, ContractJson.Options);

        Assert.NotNull(roundTrip);
        Assert.Equal(TestActionDto.Input, roundTrip.Steps[0].Action);
        Assert.Equal("txtDescription", roundTrip.Steps[0].Target.Locators[0].Value);
        Assert.Equal(3, roundTrip.Steps[0].MaxAttempts);
    }

    [Fact]
    public void RecorderState_RoundTripsWithAutomaticLocators()
    {
        var state = new RecorderStateResponse(
            true,
            Guid.NewGuid(),
            [
                new RecordedStepDto(
                    Guid.NewGuid(),
                    1,
                    RecordedActionDto.Fill,
                    "Descrição",
                    "TEdit",
                    "Edit",
                    new LocatorSetDto(
                    [
                        new ElementLocatorDto(LocatorKindDto.AutomationId, "txtDescription", 100),
                        new ElementLocatorDto(LocatorKindDto.ClassName, "TEdit", 70)
                    ]),
                    "Descrição QA",
                    DateTimeOffset.UtcNow)
            ],
            "Gravando");

        var json = JsonSerializer.Serialize(state, ContractJson.Options);
        var roundTrip = JsonSerializer.Deserialize<RecorderStateResponse>(json, ContractJson.Options);

        Assert.NotNull(roundTrip);
        Assert.Equal(RecordedActionDto.Fill, roundTrip.Steps[0].Action);
        Assert.Equal("Descrição", roundTrip.Steps[0].DisplayName);
        Assert.Equal("Descrição QA", roundTrip.Steps[0].Input);
        Assert.Equal(2, roundTrip.Steps[0].Target.Locators.Count);
    }
}
