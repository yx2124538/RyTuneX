using Microsoft.UI.Xaml;
using RyTuneX.Views;
using Windows.ApplicationModel;
using Windows.Services.Store;
using Windows.Storage;
using WinRT.Interop;

namespace RyTuneX.Helpers;

public static class ReviewPromptHelper
{
    private static int _isShowingPrompt = 0;

    // Opens the official Microsoft Store in-app Rate and Review dialog
    // Only shows once after an optimization completion, and never again once closed
    public static async Task ShowReviewPopupAsync(XamlRoot? xamlRoot = null, bool forceShow = false)
    {
        if (Interlocked.CompareExchange(ref _isShowingPrompt, 1, 0) != 0)
        {
            _ = LogHelper.Log("[ReviewPromptHelper] Review prompt is already in progress. Skipping.");
            return;
        }

        try
        {
            var settings = ApplicationData.Current.LocalSettings.Values;

            // If not manually triggered (e.g. from Settings), check if it has already been shown once
            if (!forceShow)
            {
                if (settings.TryGetValue("HasShownReviewPrompt", out var shown) && shown is true)
                {
                    _ = LogHelper.Log("[ReviewPromptHelper] Review prompt has already been shown once. Skipping.");
                    return;
                }
            }

            _ = LogHelper.Log("[ReviewPromptHelper] Invoking Microsoft Store Rate & Review prompt...");

            // Mark as shown immediately so it is never shown again automatically once closed
            settings["HasShownReviewPrompt"] = true;
            settings["ReviewPrompt_LastDate"] = DateTime.UtcNow.ToString("o");

            bool apiInvoked = false;

            try
            {
                var storeContext = StoreContext.GetDefault();
                var hwnd = WindowNative.GetWindowHandle(App.MainWindow);

                if (hwnd != IntPtr.Zero)
                {
                    InitializeWithWindow.Initialize(storeContext, hwnd);
                }

                var result = await storeContext.RequestRateAndReviewAppAsync();
                _ = LogHelper.Log($"[ReviewPromptHelper] StoreContext.RequestRateAndReviewAppAsync result: {result.Status}");

                if (result.Status == StoreRateAndReviewStatus.Succeeded)
                {
                    apiInvoked = true;
                    settings["HasReviewedApp"] = true;
                }
                else if (result.Status == StoreRateAndReviewStatus.CanceledByUser)
                {
                    apiInvoked = true;
                }
            }
            catch (Exception ex)
            {
                _ = LogHelper.LogWarning($"[ReviewPromptHelper] StoreContext RequestRateAndReviewAppAsync error: {ex.Message}");
            }

            // Fallback for sideload/dev test environments if StoreContext did not open in-app
            if (!apiInvoked && forceShow)
            {
                try
                {
                    string storeUri = "ms-windows-store://review/?ProductId=9PDH8M7HF2SQ";
                    if (RuntimeHelper.IsMSIX)
                    {
                        try
                        {
                            var pfn = Package.Current.Id.FamilyName;
                            if (!string.IsNullOrEmpty(pfn))
                            {
                                storeUri = $"ms-windows-store://review/?PFN={pfn}";
                            }
                        }
                        catch { }
                    }

                    _ = LogHelper.Log($"[ReviewPromptHelper] Launching fallback Store URI: {storeUri}");
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(storeUri));
                }
                catch (Exception ex)
                {
                    _ = LogHelper.LogError($"[ReviewPromptHelper] Fallback Store URI launch failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _ = LogHelper.LogError($"[ReviewPromptHelper] Error displaying review prompt: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isShowingPrompt, 0);
        }
    }

    // Notifies the review system that an optimization operation has completed
    // Triggers the Microsoft Store in-app review prompt once
    public static void NotifyOptimizationCompleted(XamlRoot? xamlRoot = null)
    {
        try
        {
            var settings = ApplicationData.Current.LocalSettings.Values;

            // Check if already shown before scheduling any task
            if (settings.TryGetValue("HasShownReviewPrompt", out var shown) && shown is true)
            {
                return;
            }

            int count = 0;
            if (settings.TryGetValue("ReviewPrompt_OptimizationCount", out var countObj) && countObj is int c)
            {
                count = c;
            }
            count++;
            settings["ReviewPrompt_OptimizationCount"] = count;

            _ = LogHelper.Log($"[ReviewPromptHelper] Optimization completed (count: {count}). Dispatching review prompt...");

            var dispatcher = App.MainWindow.DispatcherQueue ?? ShellPage.Current?.DispatcherQueue;

            void Action()
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(800);
                    if (dispatcher != null)
                    {
                        dispatcher.TryEnqueue(async () =>
                        {
                            await ShowReviewPopupAsync(xamlRoot);
                        });
                    }
                    else
                    {
                        await ShowReviewPopupAsync(xamlRoot);
                    }
                });
            }

            if (dispatcher?.HasThreadAccess == true)
            {
                Action();
            }
            else if (dispatcher != null)
            {
                dispatcher.TryEnqueue(Action);
            }
            else
            {
                Action();
            }
        }
        catch (Exception ex)
        {
            _ = LogHelper.LogWarning($"[ReviewPromptHelper] Failed to notify optimization completion: {ex.Message}");
        }
    }
}
