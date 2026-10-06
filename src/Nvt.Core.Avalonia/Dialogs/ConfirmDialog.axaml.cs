// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Path = Avalonia.Controls.Shapes.Path;

namespace Nvt.Core.Avalonia.Dialogs;

/// <summary>A modal confirmation prompt that returns true for Confirm and false for Cancel.</summary>
public sealed partial class ConfirmDialog : Window
{
    private SelectableTextBlock? _titleText;
    private SelectableTextBlock? _messageText;
    private Button? _confirmButton;
    private Button? _cancelButton;
    private SelectableTextBlock? _confirmButtonText;
    private SelectableTextBlock? _cancelButtonText;
    private Path? _cancelIcon;

    /// <summary>Initializes a new instance of the <see cref="ConfirmDialog"/> class for XAML loading.</summary>
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>Initializes a new instance of the <see cref="ConfirmDialog"/> class with supplied content.</summary>
    /// <param name="title">The selectable title text.</param>
    /// <param name="message">The selectable message text.</param>
    /// <param name="confirmText">The affirmative button text.</param>
    /// <param name="cancelText">The cancel button text.</param>
    /// <param name="emphasizeCancel">Whether to add the danger class and display the cancel icon.</param>
    /// <param name="confirmTip">The affirmative button tooltip, or null for no tooltip.</param>
    /// <param name="cancelTip">The cancel button tooltip, or null for no tooltip.</param>
    public ConfirmDialog(string title, string message, string confirmText, string cancelText,
        bool emphasizeCancel = false, string? confirmTip = null, string? cancelTip = null) : this()
    {
        if (_titleText is not null) _titleText.Text = title;
        if (_messageText is not null) _messageText.Text = message;
        if (_confirmButtonText is not null)
        {
            _confirmButtonText.Text = confirmText;
        }
        else if (_confirmButton is not null)
        {
            _confirmButton.Content = confirmText;
        }

        if (_confirmButton is not null) ToolTip.SetTip(_confirmButton, confirmTip);
        if (_cancelButton is not null)
        {
            ToolTip.SetTip(_cancelButton, cancelTip);
            if (_cancelButtonText is not null)
            {
                _cancelButtonText.Text = cancelText;
            }
            else
            {
                _cancelButton.Content = cancelText;
            }

            if (emphasizeCancel)
            {
                _cancelButton.Classes.Remove("actionNeutral");
                _cancelButton.Classes.Add("actionDanger");
                if (_cancelIcon is not null) _cancelIcon.IsVisible = true;
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _titleText = this.FindControl<SelectableTextBlock>("TitleText");
        _messageText = this.FindControl<SelectableTextBlock>("MessageText");
        _confirmButton = this.FindControl<Button>("ConfirmButton");
        _cancelButton = this.FindControl<Button>("CancelButton");
        _confirmButtonText = this.FindControl<SelectableTextBlock>("ConfirmButtonText");
        _cancelButtonText = this.FindControl<SelectableTextBlock>("CancelButtonText");
        _cancelIcon = this.FindControl<Path>("CancelIcon");
    }

    private void ConfirmButtonClick(object? sender, RoutedEventArgs e) => Close(true);

    private void CancelButtonClick(object? sender, RoutedEventArgs e) => Close(false);
}
