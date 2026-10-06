// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;
using System.Text.Json;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Frozen envelope serialization contracts using synthetic data.</summary>
public sealed class RuntimeQueryProtocolTests
{
    /// <summary>Request fields retain their order, camel case and explicit null arguments.</summary>
    [Fact]
    public void CompactRequestWithNullArgumentsHasExactBytes()
    {
        var request = new RuntimeQueryRequest("synthetic-v1", "probe", null);
        AssertBytes("{\"version\":\"synthetic-v1\",\"command\":\"probe\",\"args\":null}", request, pretty: false);
    }

    /// <summary>Dictionary key casing, insertion order and Unicode escaping remain unchanged.</summary>
    [Fact]
    public void CompactRequestWithArgumentsHasExactBytes()
    {
        var request = new RuntimeQueryRequest("synthetic-v1", "probe", new Dictionary<string, string>
        {
            ["CaseKey"] = "測試",
            ["empty"] = string.Empty
        });
        AssertBytes("""{"version":"synthetic-v1","command":"probe","args":{"CaseKey":"\u6E2C\u8A66","empty":""}}""",
            request, pretty: false);
    }

    /// <summary>Indented requests use two spaces and the serializer's platform line endings.</summary>
    [Fact]
    public void PrettyRequestHasExactBytes()
    {
        var request = new RuntimeQueryRequest("synthetic-v1", "probe", null);
        AssertBytes("{\n  \"version\": \"synthetic-v1\",\n  \"command\": \"probe\",\n  \"args\": null\n}",
            request, pretty: true);
    }

    /// <summary>Success data uses camel case and retains nested and envelope nulls.</summary>
    [Fact]
    public void CompactSuccessHasExactBytes()
    {
        var response = RuntimeQueryResponseEnvelope.Success(new { DisplayName = "測試", OptionalValue = (string?)null });
        AssertBytes("""{"ok":true,"data":{"displayName":"\u6E2C\u8A66","optionalValue":null},"error":null}""",
            response, pretty: false);
    }

    /// <summary>Successful null data retains both null properties.</summary>
    [Fact]
    public void NullSuccessHasExactBytes()
    {
        AssertBytes("{\"ok\":true,\"data\":null,\"error\":null}",
            RuntimeQueryResponseEnvelope.Success(null), pretty: false);
    }

    /// <summary>Indented success output retains property order and nulls.</summary>
    [Fact]
    public void PrettySuccessHasExactBytes()
    {
        var response = RuntimeQueryResponseEnvelope.Success(new { Value = 7, Note = (string?)null });
        AssertBytes("{\n  \"ok\": true,\n  \"data\": {\n    \"value\": 7,\n    \"note\": null\n  },\n  \"error\": null\n}",
            response, pretty: true);
    }

    /// <summary>Failure output retains data null and the ordered code/message fields.</summary>
    [Fact]
    public void CompactFailureHasExactBytes()
    {
        AssertBytes("{\"ok\":false,\"data\":null,\"error\":{\"code\":\"SYNTHETIC_ERROR\",\"message\":\"synthetic failure\"}}",
            RuntimeQueryResponseEnvelope.Failure("SYNTHETIC_ERROR", "synthetic failure"), pretty: false);
    }

    /// <summary>Indented failure output retains the same envelope contract.</summary>
    [Fact]
    public void PrettyFailureHasExactBytes()
    {
        AssertBytes("{\n  \"ok\": false,\n  \"data\": null,\n  \"error\": {\n    \"code\": \"SYNTHETIC_ERROR\",\n    \"message\": \"synthetic failure\"\n  }\n}",
            RuntimeQueryResponseEnvelope.Failure("SYNTHETIC_ERROR", "synthetic failure"), pretty: true);
    }

    /// <summary>Deserialization keeps the frozen case-sensitive property matching behavior.</summary>
    [Fact]
    public void PascalCaseRequestPropertiesAreNotMatched()
    {
        var request = JsonSerializer.Deserialize<RuntimeQueryRequest>(
            "{\"Version\":\"synthetic-v1\",\"Command\":\"probe\",\"Args\":null}", RuntimeQueryProtocol.CompactJsonOptions);
        Assert.NotNull(request);
        Assert.Null(request.Version);
        Assert.Null(request.Command);
        Assert.Null(request.Args);
    }

    private static void AssertBytes<T>(string expected, T value, bool pretty)
    {
        var options = pretty ? RuntimeQueryProtocol.PrettyJsonOptions : RuntimeQueryProtocol.CompactJsonOptions;
        if (pretty)
        {
            expected = expected.Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        }

        Assert.Equal(Encoding.UTF8.GetBytes(expected), JsonSerializer.SerializeToUtf8Bytes(value, options));
        Assert.Equal(expected, JsonSerializer.Serialize(value, options));
    }
}
