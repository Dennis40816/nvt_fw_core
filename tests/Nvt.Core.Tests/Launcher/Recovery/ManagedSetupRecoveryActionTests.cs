// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Nvt.Core.Launcher.Recovery;
using Xunit;

namespace Nvt.Core.Tests.Launcher.Recovery;

/// <summary>The recovery action values are part of the frozen public contract and the zero value never deletes.</summary>
public sealed class ManagedSetupRecoveryActionTests
{
    /// <summary>The numeric values are pinned, so a reorder is a reviewed change and never an accident.</summary>
    [Theory]
    [InlineData(ManagedSetupRecoveryAction.ConvergeReady, 0)]
    [InlineData(ManagedSetupRecoveryAction.RemoveIncompleteInstallation, 1)]
    public void ActionValuesAreFrozen(ManagedSetupRecoveryAction action, int value) =>
        Assert.Equal(value, (int)action);

    /// <summary>An unset or zeroed action is the harmless one and is never the deletion.</summary>
    [Fact]
    public void DefaultActionIsNotTheDeleteAction()
    {
        ManagedSetupRecoveryAction unset = default;

        Assert.Equal(ManagedSetupRecoveryAction.ConvergeReady, unset);
        Assert.NotEqual(ManagedSetupRecoveryAction.RemoveIncompleteInstallation, unset);
    }

    /// <summary>Only the two documented actions exist, in the pinned order.</summary>
    [Fact]
    public void OnlyTheTwoDocumentedActionsExist()
    {
        ManagedSetupRecoveryAction[] expected =
            [ManagedSetupRecoveryAction.ConvergeReady, ManagedSetupRecoveryAction.RemoveIncompleteInstallation];

        Assert.Equal(expected, Enum.GetValues<ManagedSetupRecoveryAction>());
    }
}
