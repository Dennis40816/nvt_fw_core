// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;

namespace Nvt.Core.Avalonia.LogConsole;

// A view owns this wiring through its events; the controller receives only a view-free lifetime token.
internal sealed class ConsoleControllerAttachment
{
    private readonly Control _view;
    private readonly StyledProperty<ConsoleController?> _property;
    // UI thread only. Controller identity and its attachment token are replaced and released together.
    private Connection? _connection;

    internal ConsoleControllerAttachment(Control view, StyledProperty<ConsoleController?> property)
    {
        _view = view;
        _property = property;
        view.AttachedToVisualTree += Attached;
        view.DetachedFromVisualTree += Detached;
        view.PropertyChanged += Changed;
    }

    private void Attached(object? sender, VisualTreeAttachmentEventArgs args) => Connect();
    private void Detached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _connection?.Attachment.Dispose();
        _connection = null;
    }
    private void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == _property && TopLevel.GetTopLevel(_view) is not null) Connect();
    }
    private void Connect()
    {
        var controller = _view.GetValue(_property);
        if (ReferenceEquals(_connection?.Controller, controller)) return;
        _connection?.Attachment.Dispose();
        _connection = null;
        var attachment = controller?.AttachView();
        try
        {
            if (attachment is not null)
            {
                _connection = new(controller!, attachment);
                attachment = null;
            }
        }
        finally { attachment?.Dispose(); }
    }
    private sealed record Connection(ConsoleController Controller, IDisposable Attachment);
}
