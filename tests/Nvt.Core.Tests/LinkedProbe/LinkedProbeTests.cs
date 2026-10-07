// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using Nvt.Core.Launcher.Contracts;
using Xunit;

namespace Nvt.Core.Tests.LinkedProbe;

/// <summary>Verifies the Core-linked child contract independently of future mode lanes.</summary>
public sealed class LinkedProbeTests
{
    private static readonly string[] DefaultNames =
    [
        "CORE_TEST_APP_READY_HANDLE",
        "CORE_TEST_APP_EXPECTED_VERSION",
        "CORE_TEST_LAUNCHER_READY_HANDLE",
        "CORE_TEST_LAUNCHER_EXPECTED_READY",
        "CORE_TEST_BOOTSTRAP_ADMISSION_HANDLE",
        "CORE_TEST_BOOTSTRAP_START_CONTEXT",
        "CORE_TEST_BOOTSTRAP_START_HANDLE",
        "CORE_TEST_LIFETIME_CONTEXT",
        "CORE_TEST_LIFETIME_HANDLE",
        "CORE_TEST_LIFETIME_JOB",
        "CORE_TEST_LIFETIME_STATE_PATH",
        "CORE_TEST_LIFETIME_KIND",
        "CORE_TEST_BOOTSTRAP_IDENTITY",
    ];

    /// <summary>Checks that the renamed apphost reports the same Core identity as the parent.</summary>
    [Fact]
    public async Task CopiedAndRenamedSelfCheckReportsCoreAssemblyIdentity()
    {
        await using var workspace = new LinkedProbeWorkspace();
        workspace.CopyAndRename();
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(workspace.Executable)!, "Nvt.Core.dll")));
        string[] lines = await SelfCheckAsync(workspace, "identity.txt");
        Assembly core = typeof(LauncherProtocolNames).Assembly;
        Assert.Equal(15, lines.Length);
        Assert.Equal(core.GetName().Name, lines[0]);
        Assert.Equal(core.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, lines[1]);
    }

    /// <summary>Checks the exact R05 synthetic names in protocol record order.</summary>
    [Fact]
    public async Task DefaultPrefixReportsExactSyntheticProtocolNames()
    {
        await using var workspace = new LinkedProbeWorkspace();
        string[] lines = await SelfCheckAsync(workspace, "default.txt");
        Assert.Equal(15, lines.Length);
        Assert.Equal(DefaultNames, lines[2..]);
    }

    /// <summary>Checks that a second prefix replaces the prefix of all 13 protocol names.</summary>
    [Fact]
    public async Task SecondPrefixChangesEveryProtocolName()
    {
        await using var workspace = new LinkedProbeWorkspace();
        string[] original = await SelfCheckAsync(workspace, "default.txt");
        string[] changed = await SelfCheckAsync(workspace, "second.txt", ["--protocol-prefix", "CORE_SECOND_"]);
        Assert.Equal(15, changed.Length);
        Assert.Equal(original[..2], changed[..2]);
        Assert.Equal(NamesWithPrefix("CORE_SECOND_"), changed[2..]);
        for (int index = 2; index < changed.Length; index++)
        {
            Assert.NotEqual(original[index], changed[index]);
        }
    }

    /// <summary>Checks environment inputs and argument precedence for every self-check input.</summary>
    [Fact]
    public async Task ArgumentsOverrideEnvironmentInputs()
    {
        await using var workspace = new LinkedProbeWorkspace();
        string environmentMarker = workspace.PathFor("environment.txt");
        var environmentProcess = workspace.Start([], info =>
        {
            info.Environment["CORE_LINKED_PROBE_MODE"] = "linked-self-check";
            info.Environment["CORE_LINKED_PROBE_MARKER"] = environmentMarker;
            info.Environment["CORE_LINKED_PROBE_PROTOCOL_PREFIX"] = "CORE_ENV_";
        });
        await LinkedProbeWorkspace.ExitAsync(environmentProcess, 0);
        string[] environmentLines = await File.ReadAllLinesAsync(environmentMarker, TestContext.Current.CancellationToken);
        Assert.Equal(NamesWithPrefix("CORE_ENV_"), environmentLines[2..]);
        Assert.Empty(await LinkedProbeWorkspace.ReadAsync(environmentProcess.StandardOutput));
        Assert.Empty(await LinkedProbeWorkspace.ReadAsync(environmentProcess.StandardError));

        string marker = workspace.PathFor("argument.txt");
        string unused = workspace.PathFor("unused.txt");
        var process = workspace.Start(
            ["--mode", "linked-self-check", "--marker", marker, "--protocol-prefix", "CORE_ARGUMENT_"], info =>
            {
                info.Environment["CORE_LINKED_PROBE_MODE"] = "unknown";
                info.Environment["CORE_LINKED_PROBE_MARKER"] = unused;
                info.Environment["CORE_LINKED_PROBE_PROTOCOL_PREFIX"] = "CORE_ENV_";
            });
        await LinkedProbeWorkspace.ExitAsync(process, 0);
        string[] lines = await File.ReadAllLinesAsync(marker, TestContext.Current.CancellationToken);
        Assert.Equal(NamesWithPrefix("CORE_ARGUMENT_"), lines[2..]);
        Assert.False(File.Exists(unused));
        Assert.Empty(await LinkedProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.Empty(await LinkedProbeWorkspace.ReadAsync(process.StandardError));
    }

    /// <summary>Checks missing values and unknown modes produce exactly one usage diagnostic.</summary>
    /// <param name="arguments">The invalid child arguments.</param>
    /// <param name="message">The exact expected diagnostic.</param>
    [Theory]
    [InlineData(new string[] { }, "Missing input: mode")]
    [InlineData(new[] { "--mode" }, "Missing input: mode")]
    [InlineData(new[] { "--mode", "unknown" }, "Unknown mode: unknown")]
    [InlineData(new[] { "--mode", "linked-self-check" }, "Missing input: marker")]
    [InlineData(new[] { "--mode", "linked-self-check", "--marker" }, "Missing input: marker")]
    public async Task InvalidInputsReturnUsageCodeAndOneErrorLine(string[] arguments, string message)
    {
        await using var workspace = new LinkedProbeWorkspace();
        var process = workspace.Start(arguments);
        await LinkedProbeWorkspace.ExitAsync(process, 64);
        Assert.Empty(await LinkedProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.Equal(message + Environment.NewLine, await LinkedProbeWorkspace.ReadAsync(process.StandardError));
        Assert.Empty(Directory.GetFiles(workspace.Root));
    }

    /// <summary>Checks inherited probe inputs are removed in any casing, so a parent variable cannot select a mode.</summary>
    [Fact]
    public void InheritedProbeVariablesAreRemovedInAnyCasing()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["core_linked_probe_mode"] = "unknown",
            ["CORE_LINKED_PROBE_MARKER"] = "marker",
            ["Core_Test_App_Ready_Handle"] = "1",
            ["UNRELATED"] = "kept",
        };
        LinkedProbeWorkspace.RemoveProbeVariables(environment);
        Assert.Equal(["UNRELATED"], environment.Keys);
    }

    /// <summary>Checks the discovered modes have unique names and the required dispatch signature.</summary>
    [Fact]
    public async Task ModeTableContainsSelfCheckWithoutDuplicateNames()
    {
        await using var workspace = new LinkedProbeWorkspace();
        var modes = workspace.ChildAssembly.GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetCustomAttributesData()
                .Where(attribute => attribute.AttributeType.FullName == "Nvt.Core.LinkedProbe.LinkedProbeModeAttribute")
                .Select(attribute => (Method: method, Name: (string)attribute.ConstructorArguments[0].Value!)))
            .ToArray();
        string[] names = modes.Select(mode => mode.Name).ToArray();
        Assert.Contains("linked-self-check", names);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(modes, mode =>
        {
            Assert.True(mode.Method.IsStatic);
            Assert.False(mode.Method.ContainsGenericParameters);
            Assert.Equal("RunAsync", mode.Method.Name);
            Assert.Equal(typeof(Task<int>), mode.Method.ReturnType);
            ParameterInfo parameter = Assert.Single(mode.Method.GetParameters());
            Assert.Equal("Nvt.Core.LinkedProbe.ProbeContext", parameter.ParameterType.FullName);
        });
    }

    /// <summary>Checks recognized inputs leave all remaining payload arguments in their original order.</summary>
    [Fact]
    public async Task InputParserPreservesPayloadOrderAndEmptyArguments()
    {
        await using var workspace = new LinkedProbeWorkspace();
        Type type = workspace.ChildAssembly.GetType("Nvt.Core.LinkedProbe.ProbeInputs", throwOnError: true)!;
        ConstructorInfo constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, [typeof(string[])], modifiers: null)!;
        string[] payload = ["first value", "", "--unknown", "value", "quote\"inside", "trailing\\"];
        string[] arguments =
        [
            payload[0], "--mode", "linked-self-check", payload[1], "--marker", "first.txt",
            "--state-path", "state.json", "--boundary", "delete:state.json", "--pause", "before",
            .. payload[2..], "--marker", "second.txt",
        ];
        object inputs = constructor.Invoke([arguments]);
        Assert.Equal(payload, (string[])type.GetProperty("Payload", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inputs)!);
        MethodInfo required = type.GetMethod("Required", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal("second.txt", required.Invoke(inputs, ["marker"]));
        Assert.Equal("state.json", required.Invoke(inputs, ["state-path"]));
        Assert.Equal("delete:state.json", required.Invoke(inputs, ["boundary"]));
        Assert.Equal("before", required.Invoke(inputs, ["pause"]));
    }

    private static string[] NamesWithPrefix(string prefix) =>
        DefaultNames.Select(name => prefix + name["CORE_TEST_".Length..]).ToArray();

    private static async Task<string[]> SelfCheckAsync(LinkedProbeWorkspace workspace, string file, string[]? extra = null)
    {
        string marker = workspace.PathFor(file);
        var process = workspace.Start(["--mode", "linked-self-check", "--marker", marker, .. extra ?? []]);
        await LinkedProbeWorkspace.ExitAsync(process, 0);
        Assert.Empty(await LinkedProbeWorkspace.ReadAsync(process.StandardOutput));
        Assert.Empty(await LinkedProbeWorkspace.ReadAsync(process.StandardError));
        return await File.ReadAllLinesAsync(marker, TestContext.Current.CancellationToken);
    }
}
