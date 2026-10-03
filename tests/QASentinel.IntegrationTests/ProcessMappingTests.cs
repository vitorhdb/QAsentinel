using QASentinel.Agent;
using QASentinel.Automation.Models;

namespace QASentinel.IntegrationTests;

public sealed class ProcessMappingTests
{
    [Fact]
    public void ProcessDescriptor_MapsToIpcDtoWithoutLosingNativeHandle()
    {
        var descriptor = new ProcessDescriptor(14920, "Argus", "Argus CX", "C:\\Argus\\Argus.exe", "x64", new nint(0x19281));

        var dto = descriptor.ToDto();

        Assert.Equal(descriptor.ProcessId, dto.ProcessId);
        Assert.Equal(descriptor.ExecutablePath, dto.ExecutablePath);
        Assert.Equal(0x19281, dto.MainWindowHandle);
        Assert.Equal("x64", dto.Architecture);
    }
}
