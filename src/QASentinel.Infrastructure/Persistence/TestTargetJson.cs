using System.Text.Json;
using QASentinel.Domain.Testing;

namespace QASentinel.Infrastructure.Persistence;

internal static class TestTargetJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(TestTarget target) => JsonSerializer.Serialize(target, Options);

    public static TestTarget Deserialize(string json) =>
        JsonSerializer.Deserialize<TestTarget>(json, Options)
        ?? throw new InvalidOperationException("O alvo persistido do passo é inválido.");
}
