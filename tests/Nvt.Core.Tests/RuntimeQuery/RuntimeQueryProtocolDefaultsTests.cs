// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nvt.Core.RuntimeQuery;
using Xunit;

namespace Nvt.Core.Tests.RuntimeQuery;

/// <summary>Checks immutable shared wire defaults and independent mutable option copies.</summary>
public sealed class RuntimeQueryProtocolDefaultsTests
{
    /// <summary>A fresh assembly exposes frozen defaults before either serializer has used them.</summary>
    [Fact]
    public void SharedOptionsAreReadOnlyBeforeFirstSerialization()
    {
        var context = new AssemblyLoadContext("RuntimeQuery defaults", isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(typeof(RuntimeQueryProtocol).Assembly.Location);
            var protocol = assembly.GetType(typeof(RuntimeQueryProtocol).FullName!, throwOnError: true)!;
            foreach (var name in new[] { nameof(RuntimeQueryProtocol.CompactJsonOptions), nameof(RuntimeQueryProtocol.PrettyJsonOptions) })
            {
                var options = Assert.IsType<JsonSerializerOptions>(protocol.GetProperty(name)!.GetValue(null));
                Assert.True(options.IsReadOnly);
                Assert.Throws<InvalidOperationException>(() => options.WriteIndented = !options.WriteIndented);
                Assert.Throws<InvalidOperationException>(() => options.Converters.Add(new JsonStringEnumConverter()));
            }
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>Factory copies preserve literal JSON bytes and can change independently of other copies and shared defaults.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FactoryCopiesAreMutableIndependentAndKeepExactDefaults(bool pretty)
    {
        var shared = pretty ? RuntimeQueryProtocol.PrettyJsonOptions : RuntimeQueryProtocol.CompactJsonOptions;
        var first = pretty ? RuntimeQueryProtocol.CreatePrettyJsonOptions() : RuntimeQueryProtocol.CreateCompactJsonOptions();
        var second = pretty ? RuntimeQueryProtocol.CreatePrettyJsonOptions() : RuntimeQueryProtocol.CreateCompactJsonOptions();
        Assert.NotSame(shared, first);
        Assert.NotSame(first, second);
        Assert.False(first.IsReadOnly);
        Assert.False(second.IsReadOnly);
        Assert.Same(JsonNamingPolicy.CamelCase, first.PropertyNamingPolicy);
        Assert.Null(first.DictionaryKeyPolicy);
        Assert.Null(first.Encoder);
        Assert.False(first.PropertyNameCaseInsensitive);
        Assert.Equal(JsonIgnoreCondition.Never, first.DefaultIgnoreCondition);
        Assert.Equal(JsonNumberHandling.Strict, first.NumberHandling);
        Assert.Equal(JsonCommentHandling.Disallow, first.ReadCommentHandling);
        Assert.False(first.AllowTrailingCommas);
        Assert.Equal(pretty, first.WriteIndented);
        Assert.Equal(0, first.MaxDepth);
        var value = new { DisplayName = "測試", OptionalValue = (string?)null };
        var expected = pretty
            ? "{\n  \"displayName\": \"\\u6E2C\\u8A66\",\n  \"optionalValue\": null\n}".Replace("\n", Environment.NewLine, StringComparison.Ordinal)
            : "{\"displayName\":\"\\u6E2C\\u8A66\",\"optionalValue\":null}";
        // Customize before first use, since serialization itself freezes each copy.
        first.PropertyNameCaseInsensitive = true;
        first.Converters.Add(new JsonStringEnumConverter());
        first.WriteIndented = !pretty;
        Assert.False(second.PropertyNameCaseInsensitive);
        Assert.Empty(second.Converters);
        Assert.Empty(shared.Converters);
        Assert.Equal(pretty, shared.WriteIndented);
        Assert.Equal(expected, JsonSerializer.Serialize(value, second));
        Assert.Equal(expected, JsonSerializer.Serialize(value, shared));
    }
}
