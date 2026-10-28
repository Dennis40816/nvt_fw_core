// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Contracts;

namespace Nvt.Core.Launcher.Integration;

// Guard: the owning experience calls this session under its _generationSync lock, which protects _state.
internal sealed class VersionDiscoverySession
{
    private SessionState _state = new(0, false);

    internal long BeginCheck()
    {
        _state = _state with { Generation = _state.Generation + 1 };
        return _state.Generation;
    }

    internal bool TryPublishAutomaticPrompt(
        long generation, ManagedAppVersion currentVersion, VerifiedUpdateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (generation != _state.Generation || _state.PromptPublished ||
            candidate.Version <= currentVersion)
        {
            return false;
        }
        _state = _state with { PromptPublished = true };
        return true;
    }

    private sealed record SessionState(long Generation, bool PromptPublished);
}
