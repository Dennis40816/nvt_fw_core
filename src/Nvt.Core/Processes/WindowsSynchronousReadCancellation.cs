// Copyright (c) 2026 Dennis Liu. All rights reserved.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Nvt.Core.Processes;

/// <summary>Interrupts only reads on the dedicated thread that creates and owns this scope.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsSynchronousReadCancellation : IDisposable
{
    private const uint ThreadTerminate = 0x0001; // CancelSynchronousIo requires THREAD_TERMINATE access.
    private readonly SafeFileHandle _thread;
    private readonly int _managedThreadId = Environment.CurrentManagedThreadId;

    internal WindowsSynchronousReadCancellation()
    {
        _thread = OpenThread(ThreadTerminate, 0, GetCurrentThreadId());
        if (_thread.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            _thread.Dispose();
            throw new Win32Exception(error);
        }
    }

    internal int Read(TextReader reader, char[] buffer, int count, CancellationToken stopToken)
    {
        if (Environment.CurrentManagedThreadId != _managedThreadId)
        {
            throw new InvalidOperationException("The synchronous read must stay on its dedicated owning thread.");
        }

        stopToken.ThrowIfCancellationRequested();
        var pending = new PendingRead(_thread, _managedThreadId);
        CancellationTokenRegistration registration = stopToken.UnsafeRegister(
            static state => ((PendingRead)state!).Cancel(), pending);
        try
        {
            // Registration may run inline if stop won the race before registration.
            stopToken.ThrowIfCancellationRequested();
            return reader.Read(buffer, 0, count);
        }
        finally
        {
            pending.Complete();
            // Join the callback before any subsequent read, other I/O, or handle close on this thread.
            registration.Dispose();
        }
    }

    /// <summary>Closes the handle to the dedicated owning thread.</summary>
    public void Dispose()
    {
        _thread.Dispose();
    }

    private sealed class PendingRead(SafeFileHandle thread, int managedThreadId)
    {
        private int _completed;

        internal void Complete()
        {
            Volatile.Write(ref _completed, 1);
        }

        internal void Cancel()
        {
            if (Environment.CurrentManagedThreadId == managedThreadId)
            {
                // An inline registration callback precedes Read; the following token check stops it.
                return;
            }

            while (Volatile.Read(ref _completed) == 0)
            {
                // ERROR_NOT_FOUND may mean Read has not entered the kernel yet. Retry until this read
                // finishes, covering cancellation immediately before/after the blocking system call.
                _ = CancelSynchronousIo(thread);
                Thread.Sleep(1);
            }
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeFileHandle OpenThread(uint desiredAccess, int inheritHandle, uint threadId);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int CancelSynchronousIo(SafeFileHandle thread);
}
