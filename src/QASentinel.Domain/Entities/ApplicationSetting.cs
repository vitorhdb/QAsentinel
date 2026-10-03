namespace QASentinel.Domain.Entities;

public sealed class ApplicationSetting
{
    private ApplicationSetting()
    {
    }

    public ApplicationSetting(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key.Trim();
        Value = value;
    }

    public string Key { get; private set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
