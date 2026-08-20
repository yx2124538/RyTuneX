using CommunityToolkit.WinUI.Controls;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RyTuneX.Controls;
using RyTuneX.Models;
using RyTuneX.Services;

namespace RyTuneX.Helpers;

public static class IntelligentCardEnhancer
{
    private static readonly HashSet<int> HookedToggleHashCodes = new();

    // Scans a Page for all ToggleSwitches and SettingsCards, attaching intelligent badges,
    // risk indicators, score weight chips, per-item rollback buttons, and technical detail flyouts
    public static void EnhancePage(Page page)
    {
        if (page == null) return;

        try
        {
            var toggleSwitches = GetAllToggleSwitches(page);
            _ = LogHelper.Log($"[IntelligentCardEnhancer] Enhancing page '{page.GetType().Name}' with {toggleSwitches.Count} toggles.");

            foreach (var toggle in toggleSwitches)
            {
                var tagName = (toggle.Tag as string) ?? toggle.Name;
                if (!string.IsNullOrEmpty(tagName))
                {
                    try
                    {
                        EnhanceToggleControl(page, toggle, tagName);
                    }
                    catch (Exception itemEx)
                    {
                        _ = LogHelper.LogError($"[IntelligentCardEnhancer] Error enhancing toggle '{tagName}': {itemEx.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _ = LogHelper.LogError($"[IntelligentCardEnhancer] Error enhancing page {page.GetType().Name}: {ex.Message}");
        }
    }

    private static void EnhanceToggleControl(Page page, ToggleSwitch toggle, string tag)
    {
        // Hook up live Toggled event for real-time backup, score, & UI update
        if (HookedToggleHashCodes.Add(toggle.GetHashCode()))
        {
            toggle.Toggled += async (s, e) =>
            {
                try
                {
                    // Save pre-apply snapshot for per-item rollback if user turned it ON
                    if (toggle.IsOn)
                    {
                        var itemModel = IntelligentOptimizationEngine.GetItemByTag(tag);
                        var details = itemModel?.TechnicalDetails ?? GetTechnicalDetailsForTag(tag);
                        ItemRollbackService.SavePreApplyBackup(tag, false, details);
                    }

                    // Refresh this card's controls
                    EnhanceToggleControl(page, toggle, tag);

                    // Refresh score header on page if present
                    var scoreHeader = FindVisualChildren<IntelligentScoreHeaderControl>(page).FirstOrDefault();
                    if (scoreHeader != null)
                    {
                        await scoreHeader.RefreshScoreAsync();
                    }
                }
                catch (Exception ex)
                {
                    _ = LogHelper.LogError($"[IntelligentCardEnhancer] Error handling toggle event for {tag}: {ex.Message}");
                }
            };
        }

        // Query backup info and catalog metadata
        var (hasBackup, preState, backupDt, details) = ItemRollbackService.GetBackupInfo(tag);
        var catalogItem = IntelligentOptimizationEngine.GetItemByTag(tag);

        var settingsCard = FindParent<SettingsCard>(toggle);

        if (settingsCard != null)
        {
            EnhanceSettingsCard(page, settingsCard, toggle, tag, hasBackup, backupDt, details, catalogItem);
        }
        else
        {
            EnhanceStandaloneToggle(page, toggle, tag, hasBackup, backupDt, details, catalogItem);
        }
    }

    private static void EnhanceSettingsCard(Page page, SettingsCard settingsCard, ToggleSwitch toggle, string tag, bool hasBackup, DateTime? backupDt, string? details, OptimizationItemModel? catalogItem)
    {
        StackPanel? actionPanel;

        if (settingsCard.Content is StackPanel existingStack && existingStack.Name == "IntelligentCardStack")
        {
            actionPanel = existingStack;
        }
        else
        {
            var originalContent = settingsCard.Content;

            actionPanel = new StackPanel
            {
                Name = "IntelligentCardStack",
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };

            settingsCard.Content = null;

            if (originalContent is UIElement originalElem)
            {
                actionPanel.Children.Add(originalElem);
            }

            settingsCard.Content = actionPanel;
        }

        var existingBar = actionPanel.Children.OfType<Border>().FirstOrDefault(b => (string?)b.Tag == "IntelligentBar");
        if (existingBar != null)
        {
            actionPanel.Children.Remove(existingBar);
        }

        var intelligentBar = CreateIntelligentBar(page, toggle, tag, hasBackup, backupDt, details, catalogItem);
        actionPanel.Children.Insert(0, intelligentBar);
    }

    private static void EnhanceStandaloneToggle(Page page, ToggleSwitch toggle, string tag, bool hasBackup, DateTime? backupDt, string? details, OptimizationItemModel? catalogItem)
    {
        var parentPanel = VisualTreeHelper.GetParent(toggle) as Panel;
        if (parentPanel == null) return;

        if (parentPanel is StackPanel sp)
        {
            var existingBar = sp.Children.OfType<Border>().FirstOrDefault(b => (string?)b.Tag == "IntelligentBar");
            if (existingBar != null) sp.Children.Remove(existingBar);

            var intelligentBar = CreateIntelligentBar(page, toggle, tag, hasBackup, backupDt, details, catalogItem);
            int idx = sp.Children.IndexOf(toggle);
            sp.Children.Insert(Math.Max(0, idx), intelligentBar);
        }
    }

    private static Border CreateIntelligentBar(Page page, ToggleSwitch toggle, string tag, bool hasBackup, DateTime? backupDt, string? details, OptimizationItemModel? catalogItem)
    {
        var risk = catalogItem?.Risk ?? RiskLevel.Safe;
        var scoreWeight = catalogItem?.ScoreWeight ?? 5;
        var isOptimal = toggle.IsOn;

        var intelligentBar = new Border
        {
            Tag = "IntelligentBar",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var barStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Risk Level / Recommendation Badge
        var (riskLabel, riskBg, riskFg) = GetRiskBadgeInfo(risk, catalogItem?.IsRecommended ?? false, isOptimal);
        var riskBadge = new Border
        {
            Background = riskBg,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        riskBadge.Child = new TextBlock
        {
            Text = riskLabel,
            FontSize = 10,
            Foreground = riskFg,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        barStack.Children.Add(riskBadge);

        // Score Weight Chip (+X pts) Only displayed for unapplied items to indicate potential gain
        if (!isOptimal)
        {
            var scoreChip = new Border
            {
                Background = GetResourceBrush("SubtleFillColorSecondaryBrush", new SolidColorBrush(Windows.UI.Color.FromArgb(30, 128, 128, 128))),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 2, 5, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            scoreChip.Child = new TextBlock
            {
                Text = string.Format("Intelligent_PointsGain".GetLocalized(), scoreWeight),
                FontSize = 10,
                Foreground = GetResourceBrush("TextFillColorSecondaryBrush", new SolidColorBrush(Colors.Gray)),
                FontWeight = Microsoft.UI.Text.FontWeights.Medium
            };
            barStack.Children.Add(scoreChip);
        }

        // Per-Item Rollback Button
        var rollbackBtn = new Button
        {
            IsEnabled = hasBackup,
            Style = GetResourceStyle("DefaultButtonStyle"),
            Padding = new Thickness(6, 3, 6, 3),
            VerticalAlignment = VerticalAlignment.Center
        };

        var (_, preState, _, _) = ItemRollbackService.GetBackupInfo(tag);
        ToolTipService.SetToolTip(rollbackBtn, hasBackup
            ? string.Format("Intelligent_Flyout_PreApplyBackup".GetLocalized(), $"{backupDt:g}", preState ? "ON" : "OFF")
            : "Intelligent_Flyout_NoBackup".GetLocalized());

        var rollbackStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        rollbackStack.Children.Add(new FontIcon { Glyph = "\uE7A7", FontSize = 11 });
        rollbackStack.Children.Add(new TextBlock { Text = "Intelligent_Btn_Rollback".GetLocalized(), FontSize = 11 });
        rollbackBtn.Content = rollbackStack;

        rollbackBtn.Click += async (s, e) =>
        {
            rollbackBtn.IsEnabled = false;
            _ = LogHelper.Log($"[IntelligentCardEnhancer] User clicked per-item rollback for tag '{tag}'");

            bool success = await ItemRollbackService.RollbackItemAsync(tag);
            if (success)
            {
                var (_, newPreState, _, _) = ItemRollbackService.GetBackupInfo(tag);
                toggle.IsOn = newPreState;

                var scoreHeader = FindVisualChildren<IntelligentScoreHeaderControl>(page).FirstOrDefault();
                if (scoreHeader != null)
                {
                    await scoreHeader.RefreshScoreAsync();
                }

                EnhanceToggleControl(page, toggle, tag);
            }
        };

        barStack.Children.Add(rollbackBtn);

        // Details / Info Flyout Button
        var detailsBtn = new Button
        {
            Style = GetResourceStyle("DefaultButtonStyle"),
            Padding = new Thickness(6, 3, 6, 3),
            VerticalAlignment = VerticalAlignment.Center
        };

        var detailsStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        detailsStack.Children.Add(new FontIcon { Glyph = "\uE946", FontSize = 11 });
        detailsBtn.Content = detailsStack;
        ToolTipService.SetToolTip(detailsBtn, "Intelligent_Flyout_TechnicalDetails".GetLocalized());

        var flyout = new Flyout();
        var flyoutStack = new StackPanel { Width = 300, Spacing = 8 };

        var titleBlock = new TextBlock
        {
            Text = catalogItem?.Title ?? tag,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        };
        flyoutStack.Children.Add(titleBlock);

        var riskTextBlock = new TextBlock
        {
            Text = $"Category: {catalogItem?.CategoryDisplay ?? "Optimization"} • Risk: {risk}",
            FontSize = 11,
            Foreground = GetResourceBrush("TextFillColorSecondaryBrush", new SolidColorBrush(Colors.Gray))
        };
        flyoutStack.Children.Add(riskTextBlock);

        if (!string.IsNullOrEmpty(catalogItem?.Description))
        {
            var descBlock = new TextBlock
            {
                Text = catalogItem.Description,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            flyoutStack.Children.Add(descBlock);
        }

        if (!string.IsNullOrEmpty(catalogItem?.ImpactDescription))
        {
            var impactBorder = new Border
            {
                Background = GetResourceBrush("SubtleFillColorSecondaryBrush", new SolidColorBrush(Windows.UI.Color.FromArgb(20, 128, 128, 128))),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4)
            };
            impactBorder.Child = new TextBlock
            {
                Text = $"Impact: {catalogItem.ImpactDescription}",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            flyoutStack.Children.Add(impactBorder);
        }

        var techDetails = details ?? catalogItem?.TechnicalDetails ?? GetTechnicalDetailsForTag(tag);
        var techBlock = new TextBlock
        {
            Text = $"Technical Key / Action:\n{techDetails}",
            FontSize = 10,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetResourceBrush("TextFillColorSecondaryBrush", new SolidColorBrush(Colors.Gray))
        };
        flyoutStack.Children.Add(techBlock);

        var backupInfoBlock = new TextBlock
        {
            Text = string.Format("Intelligent_Flyout_Status".GetLocalized(), isOptimal ? "Intelligent_Badge_Optimal".GetLocalized() : (toggle.IsOn ? "ON" : "OFF")),
            FontSize = 10,
            Foreground = isOptimal ? new SolidColorBrush(Colors.MediumSeaGreen) : GetResourceBrush("TextFillColorSecondaryBrush", new SolidColorBrush(Colors.Gray))
        };
        flyoutStack.Children.Add(backupInfoBlock);

        flyout.Content = flyoutStack;
        detailsBtn.Flyout = flyout;
        barStack.Children.Add(detailsBtn);

        intelligentBar.Child = barStack;
        return intelligentBar;
    }

    private static (string Label, Brush Background, Brush Foreground) GetRiskBadgeInfo(RiskLevel risk, bool isRecommended, bool isOptimal)
    {
        var white = new SolidColorBrush(Colors.White);

        if (isOptimal)
        {
            return ("Intelligent_Badge_Optimal".GetLocalized(), new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 139, 34)), white);
        }

        if (isRecommended)
        {
            return ("Intelligent_Badge_Recommended".GetLocalized(), new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212)), white);
        }

        return risk switch
        {
            RiskLevel.Safe => ("Intelligent_Badge_Safe".GetLocalized(), new SolidColorBrush(Windows.UI.Color.FromArgb(255, 46, 117, 182)), white),
            RiskLevel.Moderate => ("Intelligent_Badge_Moderate".GetLocalized(), new SolidColorBrush(Windows.UI.Color.FromArgb(255, 216, 119, 0)), white),
            RiskLevel.Advanced => ("Intelligent_Badge_Advanced".GetLocalized(), new SolidColorBrush(Windows.UI.Color.FromArgb(255, 112, 48, 160)), white),
            RiskLevel.Caution => ("Intelligent_Badge_Caution".GetLocalized(), new SolidColorBrush(Windows.UI.Color.FromArgb(255, 180, 40, 40)), white),
            _ => ("Intelligent_Badge_Safe".GetLocalized(), new SolidColorBrush(Windows.UI.Color.FromArgb(255, 46, 117, 182)), white)
        };
    }

    private static Brush GetResourceBrush(string key, Brush fallback)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out var res) && res != null)
            {
                if (res is Brush brush) return brush;
                if (res is Windows.UI.Color color) return new SolidColorBrush(color);
            }
        }
        catch { }
        return fallback;
    }

    private static Style? GetResourceStyle(string key)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out var res) && res is Style style)
            {
                return style;
            }
        }
        catch { }
        return null;
    }

    private static string GetTechnicalDetailsForTag(string tag)
    {
        return tag switch
        {
            "BackgroundApps" => "HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\BackgroundAccessApplications -> GlobalUserDisabled = 1",
            "TelemetryServices" => "Services DiagTrack & dmwappushservice -> Start = Disabled; AllowTelemetry = 0",
            "WindowsAI" => "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI -> DisableAIDataAnalysis = 1",
            "WindowsRecall" => "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsAI -> DisableRecall = 1",
            "CoPilotAI" => "HKCU\\Software\\Policies\\Microsoft\\Windows\\WindowsCopilot -> TurnOffWindowsCopilot = 1",
            "ClassicContextMenu" => "HKCU\\Software\\Classes\\CLSID\\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\\InprocServer32",
            "SystemProfile" => "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile -> SystemResponsiveness = 10",
            "MenuShowDelay" => "HKCU\\Control Panel\\Desktop -> MenuShowDelay = 0",
            "KeyboardLatency" => "HKCU\\Control Panel\\Keyboard -> KeyboardDelay = 0, KeyboardSpeed = 31",
            "MouseAcceleration" => "HKCU\\Control Panel\\Mouse -> MouseSpeed = 0, MouseThreshold1 = 0, MouseThreshold2 = 0",
            _ => $"HKLM\\SOFTWARE\\RyTuneX\\Optimizations\\{tag}"
        };
    }

    public static List<ToggleSwitch> GetAllToggleSwitches(DependencyObject root)
    {
        var list = new List<ToggleSwitch>();
        var visited = new HashSet<DependencyObject>();
        TraverseElementTree(root, list, visited);
        return list.Distinct().ToList();
    }

    private static void TraverseElementTree(DependencyObject element, List<ToggleSwitch> list, HashSet<DependencyObject> visited)
    {
        if (element == null || !visited.Add(element)) return;

        if (element is ToggleSwitch toggle)
        {
            list.Add(toggle);
        }

        try
        {
            if (element is Page page && page.Content != null)
            {
                TraverseElementTree(page.Content, list, visited);
            }
            else if (element is ScrollViewer sv && sv.Content is UIElement svContent)
            {
                TraverseElementTree(svContent, list, visited);
            }
            else if (element is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    TraverseElementTree(child, list, visited);
                }
            }
            else if (element is ContentControl cc && cc.Content is UIElement ccContent)
            {
                TraverseElementTree(ccContent, list, visited);
            }
            else if (element is Border border && border.Child != null)
            {
                TraverseElementTree(border.Child, list, visited);
            }
            else if (element is UserControl uc && uc.Content != null)
            {
                TraverseElementTree(uc.Content, list, visited);
            }
        }
        catch { }

        try
        {
            int count = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                TraverseElementTree(child, list, visited);
            }
        }
        catch { }
    }

    public static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj != null)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T typedChild)
                {
                    yield return typedChild;
                }
                if (child is SettingsCard settingsCard)
                {
                    foreach (var childOfSettingsCard in FindVisualChildren<T>(settingsCard))
                    {
                        yield return childOfSettingsCard;
                    }
                }
                else
                {
                    foreach (var childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }
    }

    private static T? FindParent<T>(DependencyObject element) where T : DependencyObject
    {
        DependencyObject current = element;
        while (current != null)
        {
            if (current is T parent) return parent;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
