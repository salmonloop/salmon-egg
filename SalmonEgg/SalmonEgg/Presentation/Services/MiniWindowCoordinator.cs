using System;
using System.Threading.Tasks;
#if WINDOWS
using Microsoft.UI.Windowing;
using Windows.Graphics;
#endif
using SalmonEgg.Presentation.Views.MiniWindow;

namespace SalmonEgg.Presentation.Services;

public sealed class MiniWindowCoordinator : IMiniWindowCoordinator
{
    private MiniChatWindow? _miniWindow;

    public Task OpenMiniWindowAsync()
    {
        var mainWindow = App.MainWindowInstance;
        if (mainWindow?.DispatcherQueue == null)
        {
            return Task.CompletedTask;
        }

        if (_miniWindow != null)
        {
            try
            {
#if WINDOWS
                _miniWindow.AppWindow?.Show();
#endif
                _miniWindow.Activate();
            }
            catch
            {
                // The user may have closed the window via system caption buttons.
                // Treat this as "not open" so the next attempt can recreate it.
                _miniWindow = null;
            }

            if (_miniWindow != null)
            {
                return Task.CompletedTask;
            }
        }

        void CreateAndShow()
        {
            try
            {
                var window = new MiniChatWindow();
                window.Closed += (_, _) =>
                {
                    // Ensure the app can reopen the mini window after a manual close.
                    if (ReferenceEquals(_miniWindow, window))
                    {
                        _miniWindow = null;
                    }
                };

#if WINDOWS
                try
                {
                    var appWindow = window.AppWindow;
                    if (appWindow != null)
                    {
                        // A small always-on-top window meant for monitoring + quick reply.
                        // Use an overlapped presenter so the user can resize freely.
                        appWindow.Resize(new SizeInt32(450, 700));

                        var presenter = OverlappedPresenter.Create();
                        presenter.IsAlwaysOnTop = true;
                        presenter.IsResizable = true;
                        presenter.IsMaximizable = false;
                        presenter.IsMinimizable = false;
                        appWindow.SetPresenter(presenter);
                        appWindow.Show();
                    }
                }
                catch
                {
                    // Fall back to normal overlapped window.
                }
#endif

#if HAS_UNO
                window.SetWindowIcon();
#endif

                window.Activate();
                _miniWindow = window;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to open mini window.", ex);
            }
        }

        if (mainWindow.DispatcherQueue.HasThreadAccess)
        {
            CreateAndShow();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<object?>();
        var enqueued = mainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                CreateAndShow();
                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        if (!enqueued)
        {
            tcs.TrySetException(new InvalidOperationException("Failed to enqueue mini window creation to the UI thread."));
        }

        return tcs.Task;
    }

    public Task ReturnToMainWindowAsync()
    {
        var mainWindow = App.MainWindowInstance;
        if (mainWindow?.DispatcherQueue == null)
        {
            return Task.CompletedTask;
        }

        void CloseMiniWindow()
        {
            try
            {
                try
                {
                    mainWindow.Activate();
                }
                catch
                {
                }

                try
                {
                    _miniWindow?.Close();
                }
                catch
                {
                }

                _miniWindow = null;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to return to main window.", ex);
            }
        }

        if (mainWindow.DispatcherQueue.HasThreadAccess)
        {
            CloseMiniWindow();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<object?>();
        var enqueued = mainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                CloseMiniWindow();
                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        if (!enqueued)
        {
            tcs.TrySetException(new InvalidOperationException("Failed to enqueue mini window close to the UI thread."));
        }

        return tcs.Task;
    }
}
