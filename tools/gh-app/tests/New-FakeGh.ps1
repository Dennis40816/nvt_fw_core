# Copyright (c) 2026 Dennis Liu. All rights reserved.
#Requires -Version 7.4
function New-FakeGh {
    param([string]$Directory)
    $null = New-Item -ItemType Directory -Force -Path $Directory
    $source = @'
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Web.Script.Serialization;

static class FakeGh {
    static int Main(string[] args) {
        try { return Run(args); }
        catch (Exception) { Console.Error.Write("HTTP 597 offline fixture failure"); return 1; }
    }
    static int Run(string[] args) {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(false);
        var json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        string token = Environment.GetEnvironmentVariable("GH_TOKEN") ?? "";
        string input = Console.In.ReadToEnd();
        var environment = new Dictionary<string, object>();
        foreach (string name in new[] { "GH_TOKEN", "GITHUB_TOKEN", "GH_ENTERPRISE_TOKEN", "GH_DEBUG", "GH_HOST", "GH_REPO" }) {
            string value = Environment.GetEnvironmentVariable(name);
            environment[name] = name.Contains("TOKEN") && value != null ? "[redacted]" : value;
        }
        var call = new Dictionary<string, object> {
            { "Arguments", args }, { "Input", input.Replace(token.Length == 0 ? "__ABSENT_TOKEN__" : token, "[redacted]") },
            { "AppToken", token == String.Concat("ghs_", "FAKE") }, { "Environment", environment }
        };
        File.AppendAllText(Environment.GetEnvironmentVariable("NVT_GHAPP_CALLS"), json.Serialize(call) + "\n");
        string statePath = Environment.GetEnvironmentVariable("NVT_GHAPP_RESPONSES");
        string indexPath = Environment.GetEnvironmentVariable("NVT_GHAPP_INDEX");
        int index = File.Exists(indexPath) ? Int32.Parse(File.ReadAllText(indexPath)) : 0;
        File.WriteAllText(indexPath, (index + 1).ToString());
        var state = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(statePath));
        var steps = (System.Collections.IList)state["Steps"];
        if (index >= steps.Count) { Console.Error.Write("HTTP 599 unexpected offline call"); return 1; }
        var step = (Dictionary<string, object>)steps[index];
        var expected = (System.Collections.IList)step["Arguments"];
        bool valid = args.Length == expected.Count && (bool)step["App"] == (token.Length != 0);
        for (int i = 0; valid && i < args.Length; i++) { valid = args[i] == (string)expected[i]; }
        if (!valid) { Console.Error.Write("HTTP 598 offline argument or identity mismatch"); return 1; }
        Console.Out.Write(((string)step["Output"]).Replace("__TOKEN__", token));
        Console.Error.Write(((string)step["Error"]).Replace("__TOKEN__", token));
        return Convert.ToInt32(step["ExitCode"]);
    }
}
'@
    $sourcePath = Join-Path $Directory 'FakeGh.cs'
    $executable = Join-Path $Directory 'gh.exe'
    [IO.File]::WriteAllText($sourcePath, $source)
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $output = & $compiler /nologo /target:exe /reference:System.Web.Extensions.dll "/out:$executable" $sourcePath 2>&1
    if ($LASTEXITCODE -ne 0) { throw "The offline gh executable did not compile: $output" }
    if (-not [IO.File]::Exists($executable)) { throw 'The offline gh executable is missing.' }
}
