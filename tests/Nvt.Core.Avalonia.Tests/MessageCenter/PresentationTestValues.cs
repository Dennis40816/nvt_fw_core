// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using Nvt.Core.Avalonia.MessageCenter;
using Nvt.Core.MessageCenter;
using Xunit;

namespace Nvt.Core.Avalonia.Tests.MessageCenter;

internal static class PresentationTestValues
{
    internal static SyntheticText A { get; } = new("A");
    internal static SyntheticText B { get; } = new("B");
    internal const string ActivityTrace = "changed:ActivityItems|changed:HasActivityItems|changed:HasNoActivityItems|changed:SessionActivitySummary";
    internal const string PaneTrace = "changing:IsSystemInformationSelected|changing:IsRunReportsSelected|changed:IsSystemInformationSelected|changed:IsRunReportsSelected";
    internal const string ProgressTrace = "changing:IsRefreshInProgress|changing:SystemStatusAnnouncement|changing:RefreshActionLabel|changed:IsRefreshInProgress|changed:SystemStatusAnnouncement|changed:RefreshActionLabel";
    internal const string DiagnosticTrace = "changed:ActiveBadgeCount|changed:HasActiveDiagnostics|changed:HasNoActiveDiagnostics|changed:MessageCenterAccessibleName|changed:SystemStatusAnnouncement|" + ActivityTrace;
    internal const string HostDiagnosticTrace = "changed:HostCurrent|changed:ActiveBadgeCount|changed:HasActiveDiagnostics|changed:HasNoActiveDiagnostics|changed:HostRows|changed:HostBlocker|changed:HostAvailability|changed:HostAvailabilityText|changed:HostCatalog|changed:HostEnvironment|changed:MessageCenterAccessibleName|changed:SystemStatusAnnouncement|" + ActivityTrace;
    internal const string LanguageTrace = "changed:Text|changed:MessageCenterAccessibleName|changed:SystemStatusAnnouncement|changed:RefreshActionLabel|changed:ActivityItems|changed:SessionActivitySummary|changed:DebugActivityActionLabel";
    internal const string FailureResetTrace = "changing:HasExportFailure|changed:HasExportFailure|changing:ExportStatus|changed:ExportStatus";
    internal static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static void SetGeneration(MessageCenterViewModel viewModel, long generation)
    {
        var session = (MessageCenterSession)typeof(MessageCenterViewModel)
            .GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewModel)!;
        typeof(MessageCenterSession).GetProperty(nameof(MessageCenterSession.ExportContextGeneration))!
            .SetValue(session, generation);
    }

    internal static IRelayCommand ModalCommand(MessageCenterViewModel viewModel, int operation) => operation switch
    {
        0 => viewModel.OpenCommand,
        1 => viewModel.CloseCommand,
        2 => viewModel.IsSystemInformationSelected ? viewModel.ShowRunReportsCommand : viewModel.ShowSystemInformationCommand,
        3 => viewModel.OpenRunReportsCommand,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    internal sealed record SyntheticText(string Name) : IMessageCenterText
    {
        public string ShowDebugActivityLabel => $"{Name}:show-debug";
        public string HideDebugActivityLabel => $"{Name}:hide-debug";
        public string RefreshDiagnosticsLabel => $"{Name}:refresh";
        public string RefreshingDiagnosticsLabel => $"{Name}:refreshing";
        public string DiagnosticsExportedLabel => $"{Name}:exported";
        public string DiagnosticsExportFailedLabel => $"{Name}:export-failed";
        public string FormatSessionActivitySummary(int count) => $"{Name}:activity:{count}";
        public string FormatMessageCenterAccessibleName(int count) => $"{Name}:name:{count}";
        public string FormatSystemDiagnosticAnnouncement(int count) => $"{Name}:diagnostics:{count}";
    }

    internal sealed class PassiveProvider(Func<SyntheticText> text)
        : IMessageCenterProvider
    {
        private sealed record Counts(int Diagnostics, int Activity);
        // Counts and instrumentation are test/UI-thread-only. Counts are replaced together.
        private Counts _counts = new(0, 0);
        internal List<MessageCenterActivity> Entries { get; } = [];
        internal List<long> Projections { get; } = [];
        internal int Captures { get; private set; }
        public int ActiveDiagnosticCount => _counts.Diagnostics;
        public int ActivityCount => _counts.Activity;
        internal void SetCounts(int diagnostics, int activity) => _counts = new(diagnostics, activity);
        internal void Add(long sequence, MessageActivityImportance importance, MessageActivitySeverity severity)
        {
            Entries.Add(new(sequence, importance, severity, () =>
            {
                Projections.Add(sequence);
                string prefix = text().Name;
                return new($"{prefix}:time:{sequence}", $"{prefix}:title:{sequence}",
                    $"{prefix}:detail:{sequence}", $"{prefix}:category:{sequence}", $"{prefix}:status:{sequence}", severity);
            }));
        }
        public IReadOnlyList<MessageCenterActivity> CaptureActivity()
        {
            Captures++;
            return [.. Entries];
        }
    }

    internal sealed class Fixture
    {
        // Current text and all logs belong to the test's serialized caller/UI thread.
        internal SyntheticText Text { get; set; } = A;
        internal List<string> Trace { get; } = [];
        internal PassiveProvider Provider { get; }
        internal Facade ViewModel { get; }

        internal Fixture(
            Func<Facade, CancellationToken, Task>? refresh = null,
            Func<string, CancellationToken, Task>? export = null,
            Action? close = null,
            Action<MessageCenterInteraction>? interaction = null,
            bool productProgressNotifications = false)
        {
            Provider = new PassiveProvider(() => Text);
            Facade? viewModel = null;
            viewModel = new Facade(Provider, () => Text,
                token => refresh is null ? Task.CompletedTask : refresh(viewModel!, token),
                export ?? (static (_, _) => Task.CompletedTask),
                () => { Trace.Add("close-report"); close?.Invoke(); },
                value => { Trace.Add($"interaction:{value}"); interaction?.Invoke(value); },
                productProgressNotifications);
            ViewModel = viewModel;
            ViewModel.PropertyChanging += (_, args) => Trace.Add($"changing:{args.PropertyName}");
            ViewModel.PropertyChanged += (_, args) => Trace.Add($"changed:{args.PropertyName}");
        }

        internal void AssertTrace(string expected) => Assert.Equal(expected, string.Join('|', Trace));
    }

    internal sealed class Facade(
        IMessageCenterProvider provider,
        Func<IMessageCenterText> text,
        Func<CancellationToken, Task> refresh,
        Func<string, CancellationToken, Task> export,
        Action close,
        Action<MessageCenterInteraction> interaction,
        bool productProgressNotifications)
        : MessageCenterViewModel(provider, text, refresh, export, close, interaction)
    {
        private enum NotificationBatch { Language, Diagnostics }
        private sealed record NotificationScope(NotificationBatch Batch);
        // Temporary notification context is UI-thread-only, nested synchronously, and never crosses await.
        private NotificationScope? _notificationScope;

        internal void NotifyHostLanguageChanged() => InNotificationScope(NotificationBatch.Language, ApplyLanguageChanged);
        internal void NotifyDiagnosticsOutsideScope() => NotifyDiagnosticsChanged();
        internal void NotifyHostDiagnosticsChanged() => InNotificationScope(NotificationBatch.Diagnostics, NotifyDiagnosticsChanged);

        private void InNotificationScope(NotificationBatch batch, Action notify)
        {
            NotificationScope? previous = _notificationScope;
            _notificationScope = new(batch);
            try { notify(); }
            finally { _notificationScope = previous; }
        }

        internal void SetProgress(bool value) => IsRefreshInProgress = value;
        internal void SetStatus(string value) => ExportStatus = value;
        internal void SetFailure(bool value) => HasExportFailure = value;

        internal async Task RefreshWithProgressAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
        {
            Observe(() => IsRefreshInProgress = true);
            try
            {
                await work(cancellationToken);
                Observe(() => { HasExportFailure = false; ExportStatus = string.Empty; });
                NotifyDiagnosticsChanged();
            }
            finally { Observe(() => IsRefreshInProgress = false); }
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (_notificationScope?.Batch == NotificationBatch.Diagnostics && e.PropertyName == nameof(HasNoActiveDiagnostics))
            {
                try { base.OnPropertyChanged(e); }
                finally
                {
                    // These product properties are separate isolated sites after HasNoActiveDiagnostics in NFC.
                    // A fault still aborts the remaining subscribers within the current site.
                    Observe(() => OnPropertyChanged("HostRows"));
                    Observe(() => OnPropertyChanged("HostBlocker"));
                    Observe(() => OnPropertyChanged("HostAvailability"));
                    Observe(() => OnPropertyChanged("HostAvailabilityText"));
                    Observe(() => OnPropertyChanged("HostCatalog"));
                    Observe(() => OnPropertyChanged("HostEnvironment"));
                }
                return;
            }
            if (_notificationScope?.Batch == NotificationBatch.Diagnostics && e.PropertyName == nameof(ActiveBadgeCount))
            {
                // NFC raises Current in its own isolated site before ActiveBadgeCount.
                Observe(() => OnPropertyChanged("HostCurrent"));
            }
            base.OnPropertyChanged(e);
            if (_notificationScope?.Batch == NotificationBatch.Language)
            {
                if (e.PropertyName == nameof(Text))
                {
                    OnPropertyChanged("HostRows");
                    OnPropertyChanged("HostCatalog");
                    OnPropertyChanged("HostEnvironment");
                }
                if (e.PropertyName == nameof(SystemStatusAnnouncement)) { OnPropertyChanged("HostAvailabilityText"); }
            }
            if (productProgressNotifications && e.PropertyName == nameof(IsRefreshInProgress))
            {
                OnPropertyChanged("HostAvailability");
                OnPropertyChanged("HostAvailabilityText");
            }
        }

        protected override void OnPropertyChanging(PropertyChangingEventArgs e)
        {
            base.OnPropertyChanging(e);
            if (productProgressNotifications && e.PropertyName == nameof(IsRefreshInProgress))
            {
                OnPropertyChanging("HostAvailability");
                OnPropertyChanging("HostAvailabilityText");
            }
        }

        private static void Observe(Action action)
        {
            try { action(); }
            catch (Exception) { /* Matches isolation of committed host progress/reset and diagnostic sites. */ }
        }
    }
}



