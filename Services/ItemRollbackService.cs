using Microsoft.Win32;
using RyTuneX.Helpers;

namespace RyTuneX.Services;

public static class ItemRollbackService
{
    private const string BackupRegistryBaseKey = @"SOFTWARE\RyTuneX\ItemBackups";

    private static RegistryKey GetBaseKey(bool writable = false)
    {
        var baseHive = RegistryHive.LocalMachine;
        var view = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
            ? RegistryView.Registry64
            : RegistryView.Default;

        using var rootKey = RegistryKey.OpenBaseKey(baseHive, view);
        var subKey = writable
            ? rootKey.CreateSubKey(BackupRegistryBaseKey)
            : rootKey.OpenSubKey(BackupRegistryBaseKey, writable);

        return subKey ?? (writable ? rootKey.CreateSubKey(BackupRegistryBaseKey) : null!);
    }

    // Saves a pre-apply snapshot of an item's state before applying an optimization
    // Only saves if a snapshot does not already exist (to preserve true initial state)
    public static void SavePreApplyBackup(string tag, bool currentState, string technicalDetails)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;

        try
        {
            using var baseKey = GetBaseKey(writable: true);
            if (baseKey == null) return;

            // If backup already exists, preserve the initial snapshot
            var existing = baseKey.OpenSubKey(tag);
            if (existing != null)
            {
                existing.Dispose();
                return;
            }

            using var itemKey = baseKey.CreateSubKey(tag);
            if (itemKey != null)
            {
                itemKey.SetValue("PreApplyState", currentState ? 1 : 0, RegistryValueKind.DWord);
                itemKey.SetValue("BackupTimestamp", DateTime.UtcNow.ToString("o"), RegistryValueKind.String);
                itemKey.SetValue("TechnicalDetails", technicalDetails ?? string.Empty, RegistryValueKind.String);
                _ = LogHelper.Log($"[ItemRollbackService] Created rollback point for '{tag}' (PreApplyState={currentState})");
            }
        }
        catch (Exception ex)
        {
            _ = LogHelper.LogError($"[ItemRollbackService] Failed to save rollback backup for {tag}: {ex.Message}");
        }
    }

    // Checks if a per-item rollback point exists for the given tag
    public static (bool HasBackup, bool PreApplyState, DateTime? BackupDate, string? TechnicalDetails) GetBackupInfo(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return (false, false, null, null);

        try
        {
            using var baseKey = GetBaseKey(writable: false);
            if (baseKey == null) return (false, false, null, null);

            using var itemKey = baseKey?.OpenSubKey(tag);
            if (itemKey != null)
            {
                var stateVal = itemKey.GetValue("PreApplyState");
                var timeVal = itemKey.GetValue("BackupTimestamp") as string;
                var detailsVal = itemKey.GetValue("TechnicalDetails") as string;

                if (stateVal is int stateInt)
                {
                    DateTime? dt = null;
                    if (DateTime.TryParse(timeVal, out var parsedDt))
                    {
                        dt = parsedDt.ToLocalTime();
                    }
                    return (true, stateInt == 1, dt, detailsVal);
                }
            }
        }
        catch (Exception ex)
        {
            _ = LogHelper.LogError($"[ItemRollbackService] Error checking backup info for {tag}: {ex.Message}");
        }

        return (false, false, null, null);
    }

    // Returns all item tags that currently have an active rollback point saved
    public static HashSet<string> GetAvailableRollbackTags()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var baseKey = GetBaseKey(writable: false);
            if (baseKey != null)
            {
                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    result.Add(subKeyName);
                }
            }
        }
        catch (Exception ex)
        {
            _ = LogHelper.LogError($"[ItemRollbackService] Error listing rollback tags: {ex.Message}");
        }
        return result;
    }

    // Performs per-item rollback for a single item back to its saved pre-apply state
    public static async Task<bool> RollbackItemAsync(string tag)
    {
        var (hasBackup, preApplyState, backupDate, details) = GetBackupInfo(tag);
        if (!hasBackup)
        {
            _ = LogHelper.LogWarning($"[ItemRollbackService] No rollback point found for item '{tag}'.");
            return false;
        }

        _ = LogHelper.Log($"[ItemRollbackService] Executing per-item rollback for '{tag}' -> Restoring PreApplyState={preApplyState}");

        try
        {
            // Execute the toggle action back to the pre-apply state
            var fakeToggle = new Microsoft.UI.Xaml.Controls.ToggleSwitch
            {
                Tag = tag,
                IsOn = preApplyState
            };

            await OptimizationOptions.XamlSwitchesAsync(fakeToggle).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);

            // Clear the rollback point since it has been restored
            ClearBackup(tag);

            _ = LogHelper.Log($"[ItemRollbackService] Successfully rolled back item '{tag}'.");
            return true;
        }
        catch (Exception ex)
        {
            _ = LogHelper.LogError($"[ItemRollbackService] Failed to rollback item '{tag}': {ex.Message}");
            return false;
        }
    }

    // Rolls back multiple items sequentially
    public static async Task<int> RollbackMultipleAsync(IEnumerable<string> tags)
    {
        int successCount = 0;
        foreach (var tag in tags)
        {
            if (await RollbackItemAsync(tag).ConfigureAwait(false))
            {
                successCount++;
            }
        }
        return successCount;
    }

    // Removes the rollback backup for a specified item tag
    public static void ClearBackup(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;

        try
        {
            using var baseKey = GetBaseKey(writable: true);
            baseKey?.DeleteSubKeyTree(tag, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            _ = LogHelper.LogError($"[ItemRollbackService] Error clearing backup for {tag}: {ex.Message}");
        }
    }
}
