// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.Windows.Input;

namespace Nvt.Core.Avalonia.LogConsole;

// The view owns this adapter. Its enabled state and notifications have the controller's single lifetime source.
internal sealed class ConsoleClearSearchCommand(ConsoleController? controller) : ICommand
{
    public bool CanExecute(object? parameter) => controller?.ResetFiltersCommand.CanExecute(null) == true;
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) controller!.SetSearchText(string.Empty);
    }
    public event EventHandler? CanExecuteChanged
    {
        add { if (controller is not null) controller.ResetFiltersCommand.CanExecuteChanged += value; }
        remove { if (controller is not null) controller.ResetFiltersCommand.CanExecuteChanged -= value; }
    }
}
