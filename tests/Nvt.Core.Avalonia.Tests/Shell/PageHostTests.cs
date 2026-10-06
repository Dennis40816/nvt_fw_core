// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.Shell;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Shell;

/// <summary>Characterizes the frozen synchronous page-host admission and materialization contracts.</summary>
public sealed partial class PageHostTests
{
    private static readonly string[] MaterializationOrder = ["Build", "DataContext", "ContentTemplate", "Content"];

    /// <summary>Both admission values retain template and host context identities without building.</summary>
    /// <param name="shouldLoad">Whether the caller admits content.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnsureContentUsesOnlyLazyAdmission(bool shouldLoad)
    {
        var context = new object();
        var hostContext = new object();
        var template = new RecordingTemplate(static _ => throw new InvalidOperationException("Unexpected build."));
        var host = new ContentControl { ContentTemplate = template, DataContext = hostContext };

        PageHost.EnsureContent(host, shouldLoad, context);

        Assert.Same(shouldLoad ? context : null, host.Content);
        Assert.Same(template, host.ContentTemplate);
        Assert.Same(hostContext, host.DataContext);
        Assert.Equal(0, template.BuildCount);
        Assert.Equal(0, template.MatchCount);
    }

    /// <summary>Both admission values preserve an existing object or control and its context.</summary>
    /// <param name="shouldLoad">Whether the caller admits replacement content.</param>
    /// <param name="controlContent">Whether the existing content is a control.</param>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void EnsureContentRetainsExistingContent(bool shouldLoad, bool controlContent)
    {
        var existingContext = new object();
        object existing = controlContent ? new Control { DataContext = existingContext } : new object();
        var template = new RecordingTemplate(static _ => throw new InvalidOperationException("Unexpected build."));
        var host = new ContentControl { Content = existing, ContentTemplate = template };

        PageHost.EnsureContent(host, shouldLoad, new object());

        Assert.Same(existing, host.Content);
        Assert.Same(template, host.ContentTemplate);
        Assert.Equal(0, template.BuildCount);
        if (existing is Control control)
        {
            Assert.Same(existingContext, control.DataContext);
        }
    }

    /// <summary>Empty strings, whitespace, control characters, and boxed values are existing non-null content.</summary>
    /// <param name="existing">A synthetic edge value that both helpers must retain by identity.</param>
    [AvaloniaTheory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\0")]
    [InlineData("\u200b")]
    [InlineData("測試🙂")]
    [InlineData(false)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void HelpersRetainNonNullEdgeValues(object existing)
    {
        var template = new RecordingTemplate(static _ => throw new InvalidOperationException("Unexpected build."));
        var host = new ObservedHost { Content = existing, ContentTemplate = template };
        host.Changed = _ => throw new InvalidOperationException("Unexpected assignment.");

        PageHost.EnsureContent(host, shouldLoad: true, new object());
        PageHost.MaterializeContent(host, new object());

        Assert.Same(existing, host.Content);
        Assert.Same(template, host.ContentTemplate);
        Assert.Equal(0, template.BuildCount);
    }

    /// <summary>A control admitted lazily retains its own context rather than receiving the host's context.</summary>
    [AvaloniaFact]
    public void EnsureContentDoesNotAssignControlDataContext()
    {
        var contentContext = new object();
        var hostContext = new object();
        var content = new Control { DataContext = contentContext };
        var host = new ContentControl { DataContext = hostContext };

        PageHost.EnsureContent(host, shouldLoad: true, content);

        Assert.Same(content, host.Content);
        Assert.Same(contentContext, content.DataContext);
        Assert.Same(hostContext, host.DataContext);
    }

    /// <summary>Runtime null content remains admissible and a later non-null value can still load.</summary>
    [AvaloniaFact]
    public void EnsureContentAcceptsNullContentWithoutSealingTheHost()
    {
        var host = new ContentControl();
        PageHost.EnsureContent(host, shouldLoad: true, null!);
        Assert.Null(host.Content);

        var content = new object();
        PageHost.EnsureContent(host, shouldLoad: true, content);
        Assert.Same(content, host.Content);
    }

    /// <summary>Missing and null-producing templates fall back to the same data object without clearing the template.</summary>
    /// <param name="hasTemplate">Whether a null-producing template is installed.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaterializeContentPreservesFallbackIdentities(bool hasTemplate)
    {
        var context = new object();
        var hostContext = new object();
        var template = hasTemplate ? new RecordingTemplate(value =>
        {
            Assert.Same(context, value);
            return null;
        }) : null;
        var host = new ContentControl { ContentTemplate = template, DataContext = hostContext };

        PageHost.MaterializeContent(host, context);
        PageHost.MaterializeContent(host, new object());

        Assert.Same(context, host.Content);
        Assert.Same(template, host.ContentTemplate);
        Assert.Same(hostContext, host.DataContext);
        Assert.Equal(hasTemplate ? 1 : 0, template?.BuildCount ?? 0);
        Assert.Equal(0, template?.MatchCount ?? 0);
    }

    /// <summary>Existing objects and controls bypass template building and every assignment.</summary>
    /// <param name="controlContent">Whether the existing content is a control.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaterializeContentRetainsExistingContent(bool controlContent)
    {
        var existingContext = new object();
        var hostContext = new object();
        object existing = controlContent ? new Control { DataContext = existingContext } : new object();
        var template = new RecordingTemplate(static _ => throw new InvalidOperationException("Unexpected build."));
        var host = new ObservedHost { Content = existing, ContentTemplate = template, DataContext = hostContext };
        host.Changed = _ => throw new InvalidOperationException("Unexpected assignment.");

        PageHost.MaterializeContent(host, new object());

        Assert.Same(existing, host.Content);
        Assert.Same(template, host.ContentTemplate);
        Assert.Same(hostContext, host.DataContext);
        Assert.Equal(0, template.BuildCount);
        Assert.Equal(0, template.MatchCount);
        if (existing is Control control)
        {
            Assert.Same(existingContext, control.DataContext);
        }
    }

    /// <summary>A successful build receives the exact context before template clearing and content publication.</summary>
    [AvaloniaFact]
    public void MaterializeContentBuildsOnceAndAssignsInFrozenOrder()
    {
        var context = new object();
        var hostContext = new object();
        var previousContext = new object();
        var events = new List<string>();
        var content = new ObservedControl { DataContext = previousContext };
        var host = new ObservedHost { DataContext = hostContext };
        var template = new RecordingTemplate(value =>
        {
            events.Add("Build");
            Assert.Same(context, value);
            Assert.Null(host.Content);
            Assert.Same(previousContext, content.DataContext);
            return content;
        });
        host.ContentTemplate = template;
        content.Changed = args =>
        {
            if (args.Property == Control.DataContextProperty)
            {
                events.Add("DataContext");
                Assert.Same(context, content.DataContext);
                Assert.Same(template, host.ContentTemplate);
                Assert.Null(host.Content);
            }
        };
        host.Changed = args =>
        {
            if (args.Property == ContentControl.ContentTemplateProperty)
            {
                events.Add("ContentTemplate");
                Assert.Same(context, content.DataContext);
                Assert.Null(host.ContentTemplate);
                Assert.Null(host.Content);
            }
            else if (args.Property == ContentControl.ContentProperty)
            {
                events.Add("Content");
                Assert.Same(context, content.DataContext);
                Assert.Null(host.ContentTemplate);
                Assert.Same(content, host.Content);
            }
        };

        PageHost.MaterializeContent(host, context);
        PageHost.MaterializeContent(host, new object());
        PageHost.EnsureContent(host, shouldLoad: true, new object());

        Assert.Equal(MaterializationOrder, events);
        Assert.Equal(1, template.BuildCount);
        Assert.Equal(0, template.MatchCount);
        Assert.Same(content, host.Content);
        Assert.Same(context, content.DataContext);
        Assert.Same(hostContext, host.DataContext);
    }

    /// <summary>Direct materialization builds the template even when its Match predicate rejects the context.</summary>
    [AvaloniaFact]
    public void MaterializeContentDoesNotConsultTemplateMatch()
    {
        var content = new Control();
        var context = new object();
        var template = new RecordingTemplate(_ => content, matches: false);
        var host = new ContentControl { ContentTemplate = template };

        PageHost.MaterializeContent(host, context);

        Assert.Same(content, host.Content);
        Assert.Same(context, content.DataContext);
        Assert.Null(host.ContentTemplate);
        Assert.Equal(1, template.BuildCount);
        Assert.Equal(0, template.MatchCount);
    }

    /// <summary>Lazy-first and warmup-first calls retain whichever content was admitted first.</summary>
    /// <param name="lazyFirst">Whether lazy admission precedes materialization.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LazyFirstAndWarmupFirstRetainTheirFirstContent(bool lazyFirst)
    {
        var context = new object();
        var previousContext = new object();
        var content = new Control { DataContext = previousContext };
        var template = new RecordingTemplate(_ => content);
        var host = new ContentControl { ContentTemplate = template };

        if (lazyFirst)
        {
            PageHost.EnsureContent(host, shouldLoad: true, context);
            PageHost.MaterializeContent(host, context);
        }
        else
        {
            PageHost.MaterializeContent(host, context);
            PageHost.EnsureContent(host, shouldLoad: true, context);
        }

        Assert.Same(lazyFirst ? context : content, host.Content);
        Assert.Same(lazyFirst ? template : null, host.ContentTemplate);
        Assert.Same(lazyFirst ? previousContext : context, content.DataContext);
        Assert.Equal(lazyFirst ? 0 : 1, template.BuildCount);
    }

    /// <summary>A false lazy admission leaves the host available for later materialization.</summary>
    [AvaloniaFact]
    public void DeniedLazyAdmissionStillAllowsWarmup()
    {
        var denied = new object();
        var context = new object();
        var content = new Control();
        var template = new RecordingTemplate(value =>
        {
            Assert.Same(context, value);
            return content;
        });
        var host = new ContentControl { ContentTemplate = template };
        PageHost.EnsureContent(host, shouldLoad: false, denied);
        Assert.Null(host.Content);
        Assert.Equal(0, template.BuildCount);

        PageHost.MaterializeContent(host, context);

        Assert.Same(content, host.Content);
        Assert.Same(context, content.DataContext);
        Assert.Null(host.ContentTemplate);
        Assert.Equal(1, template.BuildCount);
    }

    /// <summary>A null context with a null fallback leaves content null and permits the next build.</summary>
    /// <param name="hasTemplate">Whether a null-producing template is installed.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaterializeContentNullFallbackRemainsRetryable(bool hasTemplate)
    {
        var context = new object();
        var template = hasTemplate ? new RecordingTemplate(static _ => null) : null;
        var host = new ContentControl { ContentTemplate = template };

        PageHost.MaterializeContent(host, null!);
        Assert.Null(host.Content);
        Assert.Same(template, host.ContentTemplate);
        Assert.Equal(hasTemplate ? 1 : 0, template?.BuildCount ?? 0);
        PageHost.MaterializeContent(host, context);

        Assert.Same(context, host.Content);
        Assert.Same(template, host.ContentTemplate);
        Assert.Equal(hasTemplate ? 2 : 0, template?.BuildCount ?? 0);
    }

    /// <summary>A built control accepts a runtime null context and retains it after publication.</summary>
    [AvaloniaFact]
    public void MaterializeContentAssignsNullDataContextToBuiltControl()
    {
        var content = new Control { DataContext = new object() };
        var host = new ContentControl { DataContext = new object() };
        var template = new RecordingTemplate(value =>
        {
            Assert.Null(value);
            return content;
        });
        host.ContentTemplate = template;

        PageHost.MaterializeContent(host, null!);

        Assert.Same(content, host.Content);
        Assert.Null(content.DataContext);
        Assert.Null(host.ContentTemplate);
        Assert.Equal(1, template.BuildCount);
    }

    private sealed class RecordingTemplate(Func<object?, Control?> build, bool matches = true) : IDataTemplate
    {
        internal int BuildCount { get; private set; }

        internal int MatchCount { get; private set; }

        public Control? Build(object? param)
        {
            BuildCount++;
            return build(param);
        }

        public bool Match(object? data)
        {
            MatchCount++;
            return matches;
        }
    }

    private sealed class ObservedHost : ContentControl
    {
        internal Action<AvaloniaPropertyChangedEventArgs>? Changed { get; set; }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            Changed?.Invoke(change);
        }
    }

    private sealed class ObservedControl : Control
    {
        internal Action<AvaloniaPropertyChangedEventArgs>? Changed { get; set; }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            Changed?.Invoke(change);
        }
    }
}
