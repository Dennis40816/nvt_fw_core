// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.Shell;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Shell;

/// <summary>Characterizes unchanged exception identities and partial assignment states.</summary>
public sealed partial class PageHostTests
{
    /// <summary>False admission never dereferences a null host, while true admission and warmup do.</summary>
    [AvaloniaFact]
    public void NullHostFollowsTheFrozenPredicateOrder()
    {
        PageHost.EnsureContent(null!, shouldLoad: false, null!);

        _ = Assert.Throws<NullReferenceException>(() => PageHost.EnsureContent(null!, shouldLoad: true, new object()));
        _ = Assert.Throws<NullReferenceException>(() => PageHost.MaterializeContent(null!, new object()));
    }

    /// <summary>A build failure escapes by identity before assignments, and a later call can retry.</summary>
    [AvaloniaFact]
    public void BuildFailurePropagatesUnchangedAndAllowsRetry()
    {
        var failure = new InvalidOperationException("Synthetic build failure.");
        var hostContext = new object();
        var context = new object();
        var content = new Control { DataContext = new object() };
        object previousContext = content.DataContext;
        var template = new RecordingTemplate(value =>
        {
            Assert.Same(context, value);
            throw failure;
        });
        var host = new ObservedHost { ContentTemplate = template, DataContext = hostContext };
        host.Changed = _ => throw new InvalidOperationException("Unexpected host assignment.");

        Exception actual = Assert.Throws<InvalidOperationException>(() => PageHost.MaterializeContent(host, context));

        Assert.Same(failure, actual);
        Assert.Equal("Synthetic build failure.", actual.Message);
        Assert.Same(template, host.ContentTemplate);
        Assert.Null(host.Content);
        Assert.Same(hostContext, host.DataContext);
        Assert.Same(previousContext, content.DataContext);
        Assert.Equal(1, template.BuildCount);
        Assert.Equal(0, template.MatchCount);

        host.Changed = null;
        var retryTemplate = new RecordingTemplate(_ => content);
        host.ContentTemplate = retryTemplate;
        PageHost.MaterializeContent(host, context);
        Assert.Same(content, host.Content);
        Assert.Same(context, content.DataContext);
        Assert.Null(host.ContentTemplate);
        Assert.Equal(1, retryTemplate.BuildCount);
    }

    /// <summary>Each assignment failure preserves the same exception and stops the remaining assignment sequence.</summary>
    /// <param name="failureStage">The assignment that throws: context, template, then content.</param>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AssignmentFailurePreservesPartialStateAndOrder(int failureStage)
    {
        var failure = new InvalidOperationException("Synthetic assignment failure.");
        var events = new List<string>();
        var context = new object();
        var hostContext = new object();
        var content = new ObservedControl { DataContext = new object() };
        var host = new ObservedHost { DataContext = hostContext };
        var template = new RecordingTemplate(_ =>
        {
            events.Add("Build");
            return content;
        });
        host.ContentTemplate = template;
        content.Changed = args =>
        {
            if (args.Property == Control.DataContextProperty)
            {
                events.Add("DataContext");
                if (failureStage == 1)
                {
                    throw failure;
                }
            }
        };
        host.Changed = args =>
        {
            if (args.Property == ContentControl.ContentTemplateProperty)
            {
                events.Add("ContentTemplate");
                if (failureStage == 2)
                {
                    throw failure;
                }
            }
            else if (args.Property == ContentControl.ContentProperty)
            {
                events.Add("Content");
                if (failureStage == 3)
                {
                    throw failure;
                }
            }
        };

        Exception actual = Assert.Throws<InvalidOperationException>(() => PageHost.MaterializeContent(host, context));

        Assert.Same(failure, actual);
        Assert.Equal("Synthetic assignment failure.", actual.Message);
        Assert.Equal(MaterializationOrder.Take(failureStage + 1), events);
        Assert.Same(context, content.DataContext);
        Assert.Same(hostContext, host.DataContext);
        Assert.Same(failureStage == 1 ? template : null, host.ContentTemplate);
        Assert.Same(failureStage == 3 ? content : null, host.Content);
        Assert.Equal(1, template.BuildCount);
    }

    /// <summary>Lazy content assignment does not wrap its failure or restore the already-published content.</summary>
    [AvaloniaFact]
    public void EnsureContentAssignmentFailurePropagatesUnchanged()
    {
        var failure = new InvalidOperationException("Synthetic lazy assignment failure.");
        var context = new object();
        var template = new RecordingTemplate(static _ => throw new InvalidOperationException("Unexpected build."));
        var host = new ObservedHost { ContentTemplate = template };
        host.Changed = args =>
        {
            if (args.Property == ContentControl.ContentProperty)
            {
                throw failure;
            }
        };

        Exception actual = Assert.Throws<InvalidOperationException>(() => PageHost.EnsureContent(host, shouldLoad: true, context));

        Assert.Same(failure, actual);
        Assert.Equal("Synthetic lazy assignment failure.", actual.Message);
        Assert.Same(context, host.Content);
        Assert.Same(template, host.ContentTemplate);
        Assert.Equal(0, template.BuildCount);
    }

    /// <summary>Both fallback paths retain the template when their content assignment throws.</summary>
    /// <param name="hasTemplate">Whether a null-producing template is installed.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallbackAssignmentFailurePropagatesUnchanged(bool hasTemplate)
    {
        var failure = new InvalidOperationException("Synthetic fallback assignment failure.");
        var context = new object();
        var template = hasTemplate ? new RecordingTemplate(static _ => null) : null;
        var host = new ObservedHost { ContentTemplate = template };
        host.Changed = args =>
        {
            if (args.Property == ContentControl.ContentProperty)
            {
                throw failure;
            }
        };

        Exception actual = Assert.Throws<InvalidOperationException>(() => PageHost.MaterializeContent(host, context));

        Assert.Same(failure, actual);
        Assert.Equal("Synthetic fallback assignment failure.", actual.Message);
        Assert.Same(context, host.Content);
        Assert.Same(template, host.ContentTemplate);
        Assert.Equal(hasTemplate ? 1 : 0, template?.BuildCount ?? 0);
    }
}
