// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Text;
using System.Text.Json;
using Nvt.Core.Persistence;
using Xunit;

namespace Nvt.Core.Tests.Persistence;

/// <summary>Ports NFC's codec scenarios and characterizes its unchanged stream and JSON behavior.</summary>
public sealed class LocalJsonDocumentTests
{
    /// <summary>Customizing one options copy leaves later copies and the codec's defaults unchanged.</summary>
    [Fact]
    public async Task OptionsCopiesKeepCustomizationLocal()
    {
        JsonSerializerOptions first = LocalJsonDocument.CreateOptions();
        JsonSerializerOptions second = LocalJsonDocument.CreateOptions();
        Assert.NotSame(first, second);
        Assert.False(first.IsReadOnly);
        Assert.False(second.IsReadOnly);
        first.WriteIndented = false;
        first.PropertyNameCaseInsensitive = true;
        first.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        first.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never;
        first.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        Assert.True(second.WriteIndented);
        Assert.False(second.PropertyNameCaseInsensitive);
        Assert.Null(second.PropertyNamingPolicy);
        Assert.Equal(System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull, second.DefaultIgnoreCondition);
        Assert.Empty(second.Converters);
        Assert.Equal("{}", JsonSerializer.Serialize(new NullableDocument(null), second));
        Assert.Contains("reportJson", JsonSerializer.Serialize(new NullableDocument(null), first), StringComparison.Ordinal);
        using var stream = new MemoryStream("{\"reportJson\":\"ignored\"}"u8.ToArray());
        NullableDocument? loaded = await LocalJsonDocument.DeserializeAsync<NullableDocument>(
            stream, TestContext.Current.CancellationToken);
        Assert.NotNull(loaded);
        Assert.Null(loaded.ReportJson);
        JsonSerializerOptions later = LocalJsonDocument.CreateOptions();
        Assert.False(later.IsReadOnly);
        Assert.True(later.WriteIndented);
        Assert.Empty(later.Converters);
        Assert.Null(typeof(LocalJsonDocument).GetField("Options"));
    }

    // Codec-only portion of ShellPreferenceFileStoreRoundTripsAndInvalidValuesFallBack.
    /// <summary>Preference-shaped JSON retains the original property names, values, and defaults.</summary>
    [Fact]
    public async Task RoundTripsPreferenceDocument()
    {
        var expected = new PreferenceDocument(1, new PreferenceValues(
            "Dark", "Strict", "Traditional Chinese", true, false));
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(expected, LocalJsonDocument.CreateOptions());
        using (JsonDocument document = JsonDocument.Parse(bytes))
        {
            JsonElement root = document.RootElement;
            Assert.Equal(1, root.GetProperty("SchemaVersion").GetInt32());
            Assert.Equal(2, root.EnumerateObject().Count());
            JsonElement entry = root.GetProperty("Preferences");
            Assert.Equal("Dark", entry.GetProperty("Theme").GetString());
            Assert.Equal("Strict", entry.GetProperty("Strictness").GetString());
            Assert.Equal("Traditional Chinese", entry.GetProperty("Language").GetString());
            Assert.True(entry.GetProperty("IsReducedMotionEnabled").GetBoolean());
            Assert.False(entry.GetProperty("ExpandInputDetailsByDefault").GetBoolean());
            Assert.Equal(5, entry.EnumerateObject().Count());
        }

        using var stream = new MemoryStream(bytes);
        Assert.Equal(expected, await LocalJsonDocument.DeserializeAsync<PreferenceDocument>(
            stream, TestContext.Current.CancellationToken));
    }

    // Codec-only portion of ShellPreferenceFileStoreLoadsBomDocuments.
    /// <summary>The five BOM encodings accepted by NFC deserialize the same preference document.</summary>
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf32be")]
    public async Task LoadsBomDocuments(string encodingName)
    {
        const string json = """
            {
              "SchemaVersion": 1,
              "Preferences": {
                "Theme": "Dark",
                "Strictness": "Strict",
                "Language": "Traditional Chinese",
                "IsReducedMotionEnabled": true
              }
            }
            """;
        Encoding encoding = GetBomEncoding(encodingName);
        using var stream = new MemoryStream([.. encoding.GetPreamble(), .. encoding.GetBytes(json)]);

        Assert.Equal(new PreferenceDocument(1, new PreferenceValues(
            "Dark", "Strict", "Traditional Chinese", true, false)),
            await LocalJsonDocument.DeserializeAsync<PreferenceDocument>(
                stream, TestContext.Current.CancellationToken));
        Assert.True(stream.CanRead);
    }

    // Codec-only portion of LoadLargeHistoryAvoidsWholeFileTextAllocation.
    /// <summary>A large payload keeps NFC's allocation bound for UTF-8 and legacy UTF-16.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeDocumentAvoidsWholeFileTextAllocation(bool useLegacyUtf16Encoding)
    {
        const int reportCharacterCount = 4 * 1024 * 1024;
        string reportJson = $"\"{new string('A', reportCharacterCount - 2)}\"";
        var expected = new PayloadDocument(reportJson);
        string json = JsonSerializer.Serialize(expected, LocalJsonDocument.CreateOptions());
        Encoding encoding = useLegacyUtf16Encoding ? Encoding.Unicode : new UTF8Encoding(false);
        using var stream = new MemoryStream([.. encoding.GetPreamble(), .. encoding.GetBytes(json)]);
        _ = await LocalJsonDocument.DeserializeAsync<PayloadDocument>(
            stream, TestContext.Current.CancellationToken);
        stream.Position = 0;

        long before = GC.GetAllocatedBytesForCurrentThread();
        PayloadDocument? loaded = await LocalJsonDocument.DeserializeAsync<PayloadDocument>(
            stream, TestContext.Current.CancellationToken);
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(expected, loaded);
        Assert.InRange(allocatedBytes, 0, reportCharacterCount * 3L);
    }

    /// <summary>Serialization pins indentation, escaping, property casing, and omission of null properties.</summary>
    [Fact]
    public void SerializationPreservesExactUtf8Bytes()
    {
        var value = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Name"] = "<測試>",
            ["Absent"] = null,
            ["Items"] = new string?[] { "value", null },
            ["Enabled"] = false,
        };
        const string expected = "{\n  \"Name\": \"\\u003C\\u6E2C\\u8A66\\u003E\",\n  \"Absent\": null,\n  \"Items\": [\n    \"value\",\n    null\n  ],\n  \"Enabled\": false\n}";

        Assert.Equal(Encoding.UTF8.GetBytes(expected.Replace("\n", Environment.NewLine, StringComparison.Ordinal)),
            JsonSerializer.SerializeToUtf8Bytes(value, LocalJsonDocument.CreateOptions()));
        Assert.Equal($"{{{Environment.NewLine}  \"ReportJson\": \"payload\"{Environment.NewLine}}}", JsonSerializer.Serialize(
            new PayloadDocument("payload"), LocalJsonDocument.CreateOptions()));
        Assert.Equal("{}", JsonSerializer.Serialize(new NullableDocument(null), LocalJsonDocument.CreateOptions()));
    }

    /// <summary>Unicode, scalar, and short JSON inputs work without a byte order mark.</summary>
    [Theory]
    [InlineData("\"測試😀\"", "測試😀")]
    [InlineData("null", null)]
    [InlineData("\"\"", "")]
    public async Task ReadsUtf8WithoutBom(string json, string? expected)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        Assert.Equal(expected, await LocalJsonDocument.DeserializeAsync<string>(
            stream, TestContext.Current.CancellationToken));
        Assert.True(stream.CanRead);
    }

    /// <summary>Malformed, empty, and truncated JSON throw rather than supplying host defaults.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{not valid json")]
    [InlineData("[] {}")]
    public async Task RejectsInvalidJson(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        _ = await Assert.ThrowsAsync<JsonException>(async () =>
            await LocalJsonDocument.DeserializeAsync<JsonElement>(
                stream, TestContext.Current.CancellationToken));
        Assert.True(stream.CanRead);
    }

    /// <summary>Property matching stays case-sensitive and unknown properties remain tolerated.</summary>
    [Fact]
    public async Task PreservesDefaultPropertyMatching()
    {
        using var stream = new MemoryStream("""{"reportJson":"ignored","Unknown":1}"""u8.ToArray());

        NullableDocument? loaded = await LocalJsonDocument.DeserializeAsync<NullableDocument>(
            stream, TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Null(loaded.ReportJson);
    }

    /// <summary>Pre-cancelled reads preserve cancellation and leave the caller's stream open.</summary>
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    public async Task ObservesCancellation(string encodingName)
    {
        Encoding encoding = GetBomEncoding(encodingName);
        using var stream = new MemoryStream([.. encoding.GetPreamble(), .. encoding.GetBytes("{}")]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await LocalJsonDocument.DeserializeAsync<JsonElement>(stream, cancellation.Token));
        Assert.True(stream.CanRead);
    }

    /// <summary>The source resets an incoming UTF-8 stream's position to the absolute start.</summary>
    [Fact]
    public async Task ResetsNonzeroPosition()
    {
        using var stream = new MemoryStream("\"start\" \"later\""u8.ToArray());
        stream.Position = 8;

        _ = await Assert.ThrowsAsync<JsonException>(async () =>
            await LocalJsonDocument.DeserializeAsync<string>(stream, TestContext.Current.CancellationToken));
    }

    /// <summary>Encoding detection still fills its prefix when a seekable stream returns one byte at a time.</summary>
    [Theory]
    [InlineData("utf16le")]
    [InlineData("utf32le")]
    public async Task HandlesPartialPrefixReads(string encodingName)
    {
        Encoding encoding = GetBomEncoding(encodingName);
        using var stream = new OneByteAtATimeStream(
            [.. encoding.GetPreamble(), .. encoding.GetBytes("\"測試😀\"")]);

        Assert.Equal("測試😀", await LocalJsonDocument.DeserializeAsync<string>(
            stream, TestContext.Current.CancellationToken));
    }

    /// <summary>Non-seekable input keeps the source's unsupported-stream failure.</summary>
    [Fact]
    public async Task RequiresSeekableStream()
    {
        using var stream = new NonSeekableStream("{}"u8.ToArray());

        _ = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await LocalJsonDocument.DeserializeAsync<JsonElement>(stream, TestContext.Current.CancellationToken));
        Assert.True(stream.CanRead);
    }

    /// <summary>GetPath keeps ordinary Path.Combine semantics, including relative parent components.</summary>
    [Fact]
    public void CombinesHostSuppliedPath()
    {
        Assert.Equal(Path.Combine("state", "preferences.json"),
            LocalJsonDocument.GetPath("state", "preferences.json"));
        Assert.Equal(Path.Combine("state", "..", "preferences.json"),
            LocalJsonDocument.GetPath("state", Path.Combine("..", "preferences.json")));
    }

    /// <summary>Both path arguments retain the source's null, empty, and whitespace validation.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void RejectsInvalidPathArguments(string? value)
    {
        ArgumentException directoryException = Assert.ThrowsAny<ArgumentException>(
            () => LocalJsonDocument.GetPath(value!, "file.json"));
        ArgumentException fileException = Assert.ThrowsAny<ArgumentException>(
            () => LocalJsonDocument.GetPath("state", value!));

        Assert.Equal("localStateDirectory", directoryException.ParamName);
        Assert.Equal("fileName", fileException.ParamName);
    }

    private static Encoding GetBomEncoding(string name) => name switch
    {
        "utf8" => new UTF8Encoding(true),
        "utf16le" => Encoding.Unicode,
        "utf16be" => Encoding.BigEndianUnicode,
        "utf32le" => new UTF32Encoding(false, true),
        "utf32be" => new UTF32Encoding(true, true),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private sealed record PreferenceDocument(int SchemaVersion, PreferenceValues Preferences);

    private sealed record PreferenceValues(
        string Theme,
        string Strictness,
        string Language,
        bool IsReducedMotionEnabled,
        bool ExpandInputDetailsByDefault);

    private sealed record PayloadDocument(string ReportJson);

    private sealed record NullableDocument(string? ReportJson);

    private sealed class OneByteAtATimeStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 1)]);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
