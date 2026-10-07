// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.LinkedProbe;

internal sealed class ProbeContext(
    ProbeInputs inputs, LauncherProtocolNames protocolNames, CancellationToken cancellationToken)
{
    internal ProbeInputs Inputs { get; } = inputs;

    internal IReadOnlyList<string> PayloadArguments => Inputs.Payload;

    internal LauncherProtocolNames ProtocolNames { get; } = protocolNames;

    internal CancellationToken CancellationToken { get; } = cancellationToken;
}
