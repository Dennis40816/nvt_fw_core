// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Reflection;
using System.Text;
using Nvt.Core.LinkedProbe;

Console.OutputEncoding = new UTF8Encoding(false);
return await Probe.RunAsync(args, CancellationToken.None);

namespace Nvt.Core.LinkedProbe
{
    internal static class Probe
    {
        internal static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
        {
            try
            {
                var inputs = new ProbeInputs(args);
                string mode = inputs.Required("mode");
                var modes = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
                foreach (Type type in typeof(Probe).Assembly.GetTypes())
                {
                    foreach (MethodInfo method in type.GetMethods(
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        var attribute = method.GetCustomAttribute<LinkedProbeModeAttribute>();
                        if (attribute is not null && !modes.TryAdd(attribute.Name, method))
                        {
                            Console.Error.WriteLine("Duplicate mode: " + attribute.Name);
                            return ProbeExitCodes.DuplicateMode;
                        }
                    }
                }
                if (!modes.TryGetValue(mode, out MethodInfo? selected))
                {
                    throw new ProbeInputException("Unknown mode: " + mode);
                }
                if (mode != "linked-self-check" && !OperatingSystem.IsWindows())
                {
                    throw new ProbeInputException("This mode requires Windows.");
                }

                var context = new ProbeContext(inputs,
                    ProtocolNames.Create(inputs.Optional("protocol-prefix") ?? ProtocolNames.DefaultPrefix), cancellationToken);
                var run = selected.CreateDelegate<Func<ProbeContext, Task<int>>>();
                return await run(context);
            }
            catch (ProbeInputException error)
            {
                Console.Error.WriteLine(error.Message);
                return ProbeExitCodes.InvalidInput;
            }
        }
    }
}
