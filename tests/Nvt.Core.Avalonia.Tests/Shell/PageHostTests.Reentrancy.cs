// Copyright (c) 2026 Dennis Liu. All rights reserved.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Nvt.Core.Avalonia.Shell;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.Shell;

/// <summary>Uses synchronous callback gates to preserve reentrant page-host behavior.</summary>
public sealed partial class PageHostTests
{
    /// <summary>A lazy assignment callback observes published content and cannot replace or build it through either helper.</summary>
    [AvaloniaFact]
    public void LazyAssignmentReentryRetainsPublishedContent()
    {
        var context = new object();
        var replacement = new object();
        var template = new RecordingTemplate(static _ => throw new InvalidOperationException("Unexpected build."));
        var host = new ObservedHost { ContentTemplate = template };
        int notifications = 0;
        host.Changed = args =>
        {
            if (args.Property == ContentControl.ContentProperty)
            {
                notifications++;
                Assert.Same(context, host.Content);
                Assert.Same(template, host.ContentTemplate);
                PageHost.EnsureContent(host, shouldLoad: true, replacement);
                PageHost.MaterializeContent(host, replacement);
            }
        };

        PageHost.EnsureContent(host, shouldLoad: true, context);

        Assert.Same(context, host.Content);
        Assert.Same(template, host.ContentTemplate);
        Assert.Equal(1, notifications);
        Assert.Equal(0, template.BuildCount);
    }

    /// <summary>A successful outer build keeps its final assignment when Build reenters lazy admission.</summary>
    /// <param name="nullBuild">Whether the outer build returns null and uses lazy fallback instead.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildReentryPreservesSuccessfulAndFallbackAssignmentRules(bool nullBuild)
    {
        var context = new object();
        var reentrantContent = new object();
        var previousContext = new object();
        var content = new Control { DataContext = previousContext };
        var host = new ContentControl();
        var template = new RecordingTemplate(_ =>
        {
            PageHost.EnsureContent(host, shouldLoad: true, reentrantContent);
            Assert.Same(reentrantContent, host.Content);
            return nullBuild ? null : content;
        });
        host.ContentTemplate = template;

        PageHost.MaterializeContent(host, context);

        Assert.Same(nullBuild ? reentrantContent : content, host.Content);
        Assert.Same(nullBuild ? template : null, host.ContentTemplate);
        Assert.Same(nullBuild ? previousContext : context, content.DataContext);
        Assert.Equal(1, template.BuildCount);
    }

    /// <summary>A gated nested materialization can build twice while content is still null; the outer build publishes last.</summary>
    [AvaloniaFact]
    public void BuildReentryCanMaterializeAgainBeforeOuterPublication()
    {
        var outerContext = new object();
        var innerContext = new object();
        var outerContent = new Control();
        var innerContent = new Control();
        var host = new ContentControl();
        bool entered = false;
        var template = new RecordingTemplate(value =>
        {
            if (!entered)
            {
                entered = true;
                Assert.Same(outerContext, value);
                PageHost.MaterializeContent(host, innerContext);
                Assert.Same(innerContent, host.Content);
                Assert.Same(innerContext, innerContent.DataContext);
                Assert.Null(host.ContentTemplate);
                return outerContent;
            }

            Assert.Same(innerContext, value);
            Assert.Null(host.Content);
            return innerContent;
        });
        host.ContentTemplate = template;

        PageHost.MaterializeContent(host, outerContext);

        Assert.Same(outerContent, host.Content);
        Assert.Same(outerContext, outerContent.DataContext);
        Assert.Same(innerContext, innerContent.DataContext);
        Assert.Null(host.ContentTemplate);
        Assert.Equal(2, template.BuildCount);
        Assert.Equal(0, template.MatchCount);
    }

    /// <summary>DataContext notification can reenter the same built control; the outer call does not restore its context.</summary>
    [AvaloniaFact]
    public void DataContextReentryRetainsTheNestedContextChange()
    {
        var outerContext = new object();
        var innerContext = new object();
        var content = new ObservedControl();
        var template = new RecordingTemplate(_ => content);
        var host = new ContentControl { ContentTemplate = template };
        int contextNotifications = 0;
        content.Changed = args =>
        {
            if (args.Property == Control.DataContextProperty)
            {
                contextNotifications++;
                Assert.Null(host.Content);
                Assert.Same(template, host.ContentTemplate);
                if (ReferenceEquals(content.DataContext, outerContext))
                {
                    PageHost.MaterializeContent(host, innerContext);
                    Assert.Same(content, host.Content);
                    Assert.Same(innerContext, content.DataContext);
                    Assert.Null(host.ContentTemplate);
                }
            }
        };

        PageHost.MaterializeContent(host, outerContext);

        Assert.Equal(2, contextNotifications);
        Assert.Equal(2, template.BuildCount);
        Assert.Same(content, host.Content);
        Assert.Same(innerContext, content.DataContext);
        Assert.Null(host.ContentTemplate);
    }

    /// <summary>Template-clear notification runs before outer publication and can synchronously admit interim content.</summary>
    [AvaloniaFact]
    public void TemplateClearReentryRunsBeforeOuterContentAssignment()
    {
        var context = new object();
        var interimContent = new object();
        var content = new Control();
        var template = new RecordingTemplate(_ => content);
        var host = new ObservedHost { ContentTemplate = template };
        var published = new List<object?>();
        host.Changed = args =>
        {
            if (args.Property == ContentControl.ContentTemplateProperty)
            {
                Assert.Same(context, content.DataContext);
                Assert.Null(host.ContentTemplate);
                Assert.Null(host.Content);
                PageHost.EnsureContent(host, shouldLoad: true, interimContent);
                PageHost.MaterializeContent(host, new object());
                Assert.Same(interimContent, host.Content);
            }
            else if (args.Property == ContentControl.ContentProperty)
            {
                published.Add(host.Content);
            }
        };

        PageHost.MaterializeContent(host, context);

        Assert.Collection(published,
            value => Assert.Same(interimContent, value),
            value => Assert.Same(content, value));
        Assert.Same(content, host.Content);
        Assert.Same(context, content.DataContext);
        Assert.Null(host.ContentTemplate);
        Assert.Equal(1, template.BuildCount);
    }

    /// <summary>A content-publication callback sees the assigned context and cleared template before reentering.</summary>
    [AvaloniaFact]
    public void MaterializedContentReentrySeesCompletedAssignments()
    {
        var context = new object();
        var content = new Control();
        var template = new RecordingTemplate(_ => content);
        var host = new ObservedHost { ContentTemplate = template };
        int notifications = 0;
        host.Changed = args =>
        {
            if (args.Property == ContentControl.ContentProperty)
            {
                notifications++;
                Assert.Same(content, host.Content);
                Assert.Same(context, content.DataContext);
                Assert.Null(host.ContentTemplate);
                PageHost.MaterializeContent(host, new object());
                PageHost.EnsureContent(host, shouldLoad: true, new object());
            }
        };

        PageHost.MaterializeContent(host, context);

        Assert.Equal(1, notifications);
        Assert.Equal(1, template.BuildCount);
        Assert.Same(content, host.Content);
        Assert.Same(context, content.DataContext);
        Assert.Null(host.ContentTemplate);
    }

    /// <summary>A build failure does not roll back host mutations performed by a reentrant callback.</summary>
    [AvaloniaFact]
    public void BuildFailureRetainsReentrantHostChanges()
    {
        var failure = new InvalidOperationException("Synthetic reentrant build failure.");
        var reentrantContent = new object();
        var replacementTemplate = new RecordingTemplate(static _ => null);
        var host = new ContentControl();
        var template = new RecordingTemplate(_ =>
        {
            PageHost.EnsureContent(host, shouldLoad: true, reentrantContent);
            host.ContentTemplate = replacementTemplate;
            throw failure;
        });
        host.ContentTemplate = template;

        Exception actual = Assert.Throws<InvalidOperationException>(() => PageHost.MaterializeContent(host, new object()));

        Assert.Same(failure, actual);
        Assert.Equal("Synthetic reentrant build failure.", actual.Message);
        Assert.Same(reentrantContent, host.Content);
        Assert.Same(replacementTemplate, host.ContentTemplate);
        Assert.Equal(1, template.BuildCount);
        Assert.Equal(0, replacementTemplate.BuildCount);
    }
}
