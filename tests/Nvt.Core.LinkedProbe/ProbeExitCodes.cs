// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.LinkedProbe;

internal static class ProbeExitCodes
{
    internal const int Success = 0;
    internal const int LifetimeCaptureFailed = 24;
    internal const int HandshakeTimedOut = 25;
    internal const int InvalidStartGate = 26;
    internal const int InvalidInput = 64;
    internal const int DuplicateMode = 70;
}
