using System.Text.Json;
using FdeTutor.Contracts.Serialization;
using FdeTutor.Domain.Policy;

namespace FdeTutor.Domain.Tests;

public sealed class PolicyContractTests
{
    [Fact]
    public void SerializedPolicyContainsSchemaAndNodeIdentity()
    {
        var decision = S083Policy.Evaluate([]);
        using var document = JsonDocument.Parse(ContractJson.Serialize(decision));

        Assert.Equal(
            "1.0.0",
            document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(
            "S083",
            document.RootElement.GetProperty("contentNodeId").GetString());
        Assert.Equal(
            "Orient",
            document.RootElement.GetProperty("state").GetString());
    }
}
