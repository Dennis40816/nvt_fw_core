// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Nvt.Core.Avalonia.Dialogs;

/// <summary>A modal warning prompt with selectable content and an OK button.</summary>
public sealed partial class WarningDialog : Window
{
    private SelectableTextBlock? _titleText;
    private SelectableTextBlock? _messageText;

    /// <summary>Initializes a new instance of the <see cref="WarningDialog"/> class for XAML loading.</summary>
    public WarningDialog()
    {
        InitializeComponent();
    }

    /// <summary>Initializes a new instance of the <see cref="WarningDialog"/> class with supplied content.</summary>
    /// <param name="title">The selectable title text.</param>
    /// <param name="message">The selectable message text.</param>
    public WarningDialog(string title, string message) : this()
    {
        if (_titleText is not null) _titleText.Text = title;
        if (_messageText is not null) _messageText.Text = message;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _titleText = this.FindControl<SelectableTextBlock>("TitleText");
        _messageText = this.FindControl<SelectableTextBlock>("MessageText");
    }

    private void OkButtonClick(object? sender, RoutedEventArgs e) => Close();
}
