// Copyright (c) 2026 Dennis Liu. All rights reserved.

namespace Nvt.Core.Launcher.Setup;

internal static class SetupObserverExceptions
{
    internal static bool IsObservedAndIgnored(Exception exception) => exception is not null;
}
