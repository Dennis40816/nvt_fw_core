// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Globalization;
using System.IO.Pipes;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Nvt.Core.TestProbe;

Console.OutputEncoding = new UTF8Encoding(false);
return await Probe.RunAsync(args);

namespace Nvt.Core.TestProbe
{
    internal static class Probe
    {
        internal static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);
        internal static readonly TimeSpan ReadyLinger = TimeSpan.FromMilliseconds(200);
        internal const int HandshakeLimitMilliseconds = 5_000;
        internal const int HandshakePollMilliseconds = 10;

        internal static async Task<int> RunAsync(string[] args)
        {
            try
            {
                var inputs = new ProbeInputs(args);
                string mode = inputs.Required("mode");
                if (mode is not ("ambient-pipe" or "contained-isolation" or "arguments-environment" or
                    "ready" or "ready-wrong-identity" or "ready-partial" or "invalid-utf8" or "oversized" or
                    "ready-tree-root" or "silent-wait" or "exit" or "tree-grandchild" or "tree-root-exit" or
                    "tree-root-wait" or "orphan-chain-root" or "orphan-chain-exit" or "detached-descendant-root" or
                    "hold-lock" or "dual-output-exit" or "dual-output-wait"))
                {
                    throw new ProbeInputException("Unknown mode.");
                }

                if (mode == "arguments-environment")
                {
                    await File.WriteAllLinesAsync(inputs.Required("marker"),
                        [inputs.Required("text"), Environment.CurrentDirectory, .. inputs.Payload]);
                    return 0;
                }
                if (mode == "tree-grandchild")
                {
                    await WriteProcessIdAsync(inputs.Required("tree-marker"));
                    await Task.Delay(Wait);
                    return 0;
                }
                if (mode is "ambient-pipe" or "contained-isolation")
                {
                    RequireWindows();
                    if (mode == "ambient-pipe")
                    {
                        string marker = inputs.Required("marker");
                        string handle = inputs.Required("ambient-handle");
                        await File.WriteAllTextAsync(marker, "started");
                        await TryWritePipeAsync(handle, "leaked");
                    }
                    else
                    {
                        string payload = inputs.Required("payload");
                        await TryWritePipeAsync(inputs.Optional("allowed-handle"), payload);
                        await TryWritePipeAsync(inputs.Optional("cross-handle"), "cross:" + payload);
                    }
                    return 0;
                }

                using var job = LifetimeJob.Join();
                if (mode is "ready" or "ready-wrong-identity" or "ready-partial" or "invalid-utf8" or
                    "oversized" or "ready-tree-root")
                {
                    return await ReadyModes.RunAsync(mode, inputs);
                }
                if (mode == "exit")
                {
                    return inputs.Integer("exit-code", 0);
                }
                if (mode == "silent-wait")
                {
                    await Task.Delay(Wait);
                    return 0;
                }
                if (mode == "hold-lock")
                {
                    return await HoldLockAsync(inputs);
                }
                if (mode is "dual-output-exit" or "dual-output-wait")
                {
                    return await DualOutputAsync(mode == "dual-output-wait", inputs);
                }
                return await RunTreeAsync(mode, inputs);
            }
            catch (ProbeInputException error)
            {
                Console.Error.WriteLine(error.Message);
                return 64;
            }
            catch (JobJoinException error)
            {
                Console.Error.WriteLine(error.Message);
                return 24;
            }
        }

        internal static void RequireWindows()
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new ProbeInputException("This mode requires Windows handles or a Job.");
            }
        }

        internal static Task WriteProcessIdAsync(string path) =>
            File.WriteAllTextAsync(path, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

        internal static async Task<bool> HandshakeAsync(Func<bool> complete)
        {
            long deadline = Environment.TickCount64 + HandshakeLimitMilliseconds;
            while (!complete() && Environment.TickCount64 < deadline)
            {
                await Task.Delay(HandshakePollMilliseconds);
            }
            return complete();
        }

        private static async Task TryWritePipeAsync(string? value, string text)
        {
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long raw))
            {
                return;
            }
            try
            {
                using var handle = new SafePipeHandle(new IntPtr(raw), ownsHandle: false);
                await using var pipe = new AnonymousPipeClientStream(PipeDirection.Out, handle);
                await pipe.WriteAsync(Encoding.UTF8.GetBytes(text));
                await pipe.FlushAsync();
            }
            catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // The parent observes whether an inherited pipe can receive bytes.
            }
        }

        private static async Task<int> RunTreeAsync(string mode, ProbeInputs inputs)
        {
            if (!OperatingSystem.IsWindows())
            {
                RequireWindows();
                return 64;
            }
            string marker = inputs.Required("tree-marker");
            int exitCode = mode == "tree-root-exit" ? inputs.Integer("exit-code", 0) : 0;
            string? stdout = inputs.Optional("stdout-text");
            if (mode is "tree-root-exit" or "orphan-chain-exit" && stdout is not null)
            {
                Console.Out.WriteLine(stdout);
                Console.Out.Flush();
            }
            bool chain = mode is "orphan-chain-root" or "orphan-chain-exit";
            using var child = WindowsProcess.StartDescendant(
                chain ? "tree-root-exit" : "tree-grandchild", marker,
                holdStandardPipes: mode != "detached-descendant-root");
            if (chain)
            {
                await File.WriteAllTextAsync(marker + ".middle", child.Id.ToString(CultureInfo.InvariantCulture));
            }
            if (!await HandshakeAsync(() => File.Exists(marker) && (!chain || child.HasExited)))
            {
                return 25;
            }
            if (chain)
            {
                await File.WriteAllTextAsync(marker + ".ready", "ready");
            }
            if (mode is "tree-root-wait" or "orphan-chain-root")
            {
                await Task.Delay(Wait);
            }
            return exitCode;
        }

        private static async Task<int> HoldLockAsync(ProbeInputs inputs)
        {
            string path = inputs.Required("lock-path");
            string ready = inputs.Required("lock-ready");
            Console.Out.WriteLine("STARTED");
            Console.Out.Flush();
            FileStream file;
            try
            {
                file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
            await using (file)
            {
                await File.WriteAllTextAsync(ready, "ready");
                Console.Out.WriteLine("LOCK_HELD");
                Console.Out.Flush();
                await Task.Delay(Wait);
            }
            return 0;
        }

        // With the default counts, each stream exceeds a pipe buffer. A parent that drains one stream at a time stalls.
        private static async Task<int> DualOutputAsync(bool wait, ProbeInputs inputs)
        {
            var output = RepeatedText.Read(inputs, "out", wait ? "O" : "A", wait ? "OUT-PARTIAL-END" : "OUT-END");
            var error = RepeatedText.Read(inputs, "err", wait ? "E" : "B", wait ? "ERR-PARTIAL-END" : "ERR-END");
            int milliseconds = wait ? inputs.Integer("wait-ms", 30_000, minimum: 1) : 0;
            output.WriteTo(Console.Out);
            error.WriteTo(Console.Error);
            if (wait)
            {
                await Task.Delay(milliseconds);
            }
            return 0;
        }
    }

    internal readonly record struct RepeatedText(char Character, int Count, string Suffix)
    {
        internal static RepeatedText Read(ProbeInputs inputs, string stream, string character, string suffix)
        {
            string repeated = inputs.Optional(stream + "-char") ?? character;
            if (repeated.Length != 1 || !Ascii.IsValid(repeated))
            {
                throw new ProbeInputException("Invalid input: " + stream + "-char");
            }
            string end = inputs.Optional(stream + "-suffix") ?? suffix;
            if (!Ascii.IsValid(end))
            {
                throw new ProbeInputException("Invalid input: " + stream + "-suffix");
            }
            return new RepeatedText(repeated[0], inputs.Integer(stream + "-count", 131_072, minimum: 0), end);
        }

        // Writes in chunks, so any count works without one large string.
        internal void WriteTo(TextWriter writer)
        {
            string chunk = new(Character, Math.Min(Count, 4096));
            for (int left = Count; left > 0; left -= chunk.Length)
            {
                writer.Write(chunk.AsSpan(0, Math.Min(left, chunk.Length)));
            }
            writer.Write(Suffix);
            writer.Flush();
        }
    }
}
