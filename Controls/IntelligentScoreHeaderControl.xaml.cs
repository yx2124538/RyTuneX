using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RyTuneX.Contracts.Services;
using RyTuneX.Helpers;
using RyTuneX.Models;
using RyTuneX.Services;
using RyTuneX.Views;

namespace RyTuneX.Controls;

public sealed partial class IntelligentScoreHeaderControl : UserControl
{
    public static readonly DependencyProperty TargetCategoryProperty =
        DependencyProperty.Register(
            nameof(TargetCategory),
            typeof(string),
            typeof(IntelligentScoreHeaderControl),
            new PropertyMetadata("All", OnTargetCategoryChanged));

    public string TargetCategory
    {
        get => (string)GetValue(TargetCategoryProperty);
        set => SetValue(TargetCategoryProperty, value);
    }

    private static void OnTargetCategoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is IntelligentScoreHeaderControl control)
        {
            _ = control.RefreshScoreAsync();
        }
    }

    private List<OptimizationItemModel> _allScannedItems = new();
    private Page? _parentPage;
    private string _selectedMode = "Recommended"; // "Recommended", "AllSafe", "Advanced"

    public IntelligentScoreHeaderControl()
    {
        InitializeComponent();
        Loaded += IntelligentScoreHeaderControl_Loaded;
    }

    private void IntelligentScoreHeaderControl_Loaded(object sender, RoutedEventArgs e)
    {
        _parentPage = FindParentPage(this);
        if (PreviewDialog != null && PreviewDialog.XamlRoot == null && this.XamlRoot != null)
        {
            PreviewDialog.XamlRoot = this.XamlRoot;
        }

        // Auto-detect page category if not explicitly set
        if (TargetCategory == "All" && _parentPage != null)
        {
            var pageName = _parentPage.GetType().Name;
            if (pageName.Contains("OptimizeSystem", StringComparison.OrdinalIgnoreCase))
            {
                TargetCategory = "Performance";
            }
            else if (pageName.Contains("Privacy", StringComparison.OrdinalIgnoreCase))
            {
                TargetCategory = "PrivacyAndTelemetry";
            }
            else if (pageName.Contains("Features", StringComparison.OrdinalIgnoreCase))
            {
                TargetCategory = "FeaturesAndUsability";
            }
        }

        LocalizeStaticControls();
        UpdateModeButtonsUI();
        _ = RefreshScoreAsync();
    }

    private void LocalizeStaticControls()
    {
        if (ScoreLabelText != null) ScoreLabelText.Text = "Intelligent_ScoreTitle".GetLocalized();
        if (HealthGradeText != null) HealthGradeText.Text = "Intelligent_HealthGrade_Scanning".GetLocalized();

        if (ModeRecText != null) ModeRecText.Text = "Intelligent_Mode_Rec".GetLocalized();
        if (ModeSafeText != null) ModeSafeText.Text = "Intelligent_Mode_Safe".GetLocalized();
        if (ModeAdvText != null) ModeAdvText.Text = "Intelligent_Mode_Adv".GetLocalized();

        if (ModeRecommendedBtn != null) ToolTipService.SetToolTip(ModeRecommendedBtn, "Intelligent_Mode_Rec_Tooltip".GetLocalized());
        if (ModeAllSafeBtn != null) ToolTipService.SetToolTip(ModeAllSafeBtn, "Intelligent_Mode_Safe_Tooltip".GetLocalized());
        if (ModeAdvancedBtn != null) ToolTipService.SetToolTip(ModeAdvancedBtn, "Intelligent_Mode_Adv_Tooltip".GetLocalized());

        if (PreviewBtnText != null) PreviewBtnText.Text = "Intelligent_Btn_Preview".GetLocalized();
        if (RollbackBtnText != null) RollbackBtnText.Text = "Intelligent_Btn_Rollback".GetLocalized();

        if (TuneByDomainHeaderText != null) TuneByDomainHeaderText.Text = "Intelligent_TuneByDomain".GetLocalized();
        if (HomeNavPerfText != null) HomeNavPerfText.Text = "Intelligent_Domain_PerfTitle".GetLocalized();
        if (HomeNavPrivText != null) HomeNavPrivText.Text = "Intelligent_Domain_PrivTitle".GetLocalized();
        if (HomeNavFeatText != null) HomeNavFeatText.Text = "Intelligent_Domain_FeatTitle".GetLocalized();

        if (PreviewDialogDescText != null) PreviewDialogDescText.Text = "Intelligent_Preview_Desc".GetLocalized();
    }

    public async Task RefreshScoreAsync()
    {
        try
        {
            MainProgressBar.Visibility = Visibility.Visible;
            MainProgressBar.IsIndeterminate = true;

            _allScannedItems = await IntelligentOptimizationEngine.ScanAsync();
            var score = IntelligentOptimizationEngine.Analyse(_allScannedItems);
            IntelligentOptimizationEngine.Recommend(_allScannedItems);

            UpdateUI(score);

            MainProgressBar.IsIndeterminate = false;
            MainProgressBar.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _ = LogHelper.LogError($"[IntelligentScoreHeader] Error refreshing score: {ex.Message}");
            MainProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateUI(IntelligentScoreModel score)
    {
        var category = TargetCategory;
        SystemSummaryText.Text = score.SystemSummary;

        if (category == "Performance")
        {
            DomainIcon.Glyph = "\uE9D9";
            DomainTitleText.Text = "Intelligent_Domain_PerfTitle".GetLocalized();
            ScoreLabelText.Text = "Intelligent_ScoreTitle".GetLocalized();
            ScoreText.Text = $"{score.PerformanceScore}%";
            ScoreRing.Value = score.PerformanceScore;
            HealthGradeText.Text = GetGrade(score.PerformanceScore);

            var catItems = _allScannedItems.Where(i => i.Category == OptimizationCategory.Performance).ToList();
            int active = catItems.Count(i => i.IsApplied);
            int total = catItems.Count;
            int recCount = catItems.Count(i => i.IsRecommended && !i.IsApplied);
            int rollCount = catItems.Count(i => i.RollbackAvailable);

            ActiveCountText.Text = string.Format("Intelligent_ActiveCount".GetLocalized(), active, total);
            StatusMessageText.Text = "Intelligent_Domain_PerfDesc".GetLocalized();

            CategoryMiniScoresGrid.Visibility = Visibility.Collapsed;
            DomainSubMetricsPanel.Visibility = Visibility.Visible;
            PageActionPanel.Visibility = Visibility.Visible;
            HomeNavigationPanel.Visibility = Visibility.Collapsed;

            DomainMetric1Text.Text = string.Format("Intelligent_RecommendedCount".GetLocalized(), recCount);
            DomainMetric2Text.Text = string.Format("Intelligent_RollbacksCount".GetLocalized(), rollCount);
            DomainMetric3Text.Text = "Intelligent_Metric_LowLatency".GetLocalized();

            CalculateGain(catItems);
        }
        else if (category == "PrivacyAndTelemetry")
        {
            DomainIcon.Glyph = "\uE7B3";
            DomainTitleText.Text = "Intelligent_Domain_PrivTitle".GetLocalized();
            ScoreLabelText.Text = "Intelligent_ScoreTitle".GetLocalized();
            ScoreText.Text = $"{score.PrivacyScore}%";
            ScoreRing.Value = score.PrivacyScore;
            HealthGradeText.Text = GetGrade(score.PrivacyScore);

            var catItems = _allScannedItems.Where(i => i.Category == OptimizationCategory.PrivacyAndTelemetry).ToList();
            int active = catItems.Count(i => i.IsApplied);
            int total = catItems.Count;
            int recCount = catItems.Count(i => i.IsRecommended && !i.IsApplied);
            int rollCount = catItems.Count(i => i.RollbackAvailable);

            ActiveCountText.Text = string.Format("Intelligent_ActiveCount".GetLocalized(), active, total);
            StatusMessageText.Text = "Intelligent_Domain_PrivDesc".GetLocalized();

            CategoryMiniScoresGrid.Visibility = Visibility.Collapsed;
            DomainSubMetricsPanel.Visibility = Visibility.Visible;
            PageActionPanel.Visibility = Visibility.Visible;
            HomeNavigationPanel.Visibility = Visibility.Collapsed;

            DomainMetric1Text.Text = string.Format("Intelligent_RecommendedCount".GetLocalized(), recCount);
            DomainMetric2Text.Text = string.Format("Intelligent_RollbacksCount".GetLocalized(), rollCount);
            DomainMetric3Text.Text = "Intelligent_Metric_DataShield".GetLocalized();

            CalculateGain(catItems);
        }
        else if (category == "FeaturesAndUsability")
        {
            DomainIcon.Glyph = "\uE74C";
            DomainTitleText.Text = "Intelligent_Domain_FeatTitle".GetLocalized();
            ScoreLabelText.Text = "Intelligent_ScoreTitle".GetLocalized();
            ScoreText.Text = $"{score.FeaturesScore}%";
            ScoreRing.Value = score.FeaturesScore;
            HealthGradeText.Text = GetGrade(score.FeaturesScore);

            var catItems = _allScannedItems.Where(i => i.Category == OptimizationCategory.FeaturesAndUsability).ToList();
            int active = catItems.Count(i => i.IsApplied);
            int total = catItems.Count;
            int recCount = catItems.Count(i => i.IsRecommended && !i.IsApplied);
            int rollCount = catItems.Count(i => i.RollbackAvailable);

            ActiveCountText.Text = string.Format("Intelligent_ActiveCount".GetLocalized(), active, total);
            StatusMessageText.Text = "Intelligent_Domain_FeatDesc".GetLocalized();

            CategoryMiniScoresGrid.Visibility = Visibility.Collapsed;
            DomainSubMetricsPanel.Visibility = Visibility.Visible;
            PageActionPanel.Visibility = Visibility.Visible;
            HomeNavigationPanel.Visibility = Visibility.Collapsed;

            DomainMetric1Text.Text = string.Format("Intelligent_RecommendedCount".GetLocalized(), recCount);
            DomainMetric2Text.Text = string.Format("Intelligent_RollbacksCount".GetLocalized(), rollCount);
            DomainMetric3Text.Text = "Intelligent_Metric_Productivity".GetLocalized();

            CalculateGain(catItems);
        }
        else
        {
            DomainIcon.Glyph = "\uE9F5";
            DomainTitleText.Text = "Intelligent_Domain_GlobalTitle".GetLocalized();
            ScoreLabelText.Text = "Intelligent_ScoreTitle".GetLocalized();
            ScoreText.Text = $"{score.OverallScore}%";
            ScoreRing.Value = score.OverallScore;
            HealthGradeText.Text = score.HealthGrade;

            int active = _allScannedItems.Count(i => i.IsApplied);
            int total = _allScannedItems.Count;

            ActiveCountText.Text = string.Format("Intelligent_OptimizationsActive".GetLocalized(), active, total);
            StatusMessageText.Text = "Intelligent_Domain_GlobalDesc".GetLocalized();

            CategoryMiniScoresGrid.Visibility = Visibility.Visible;
            DomainSubMetricsPanel.Visibility = Visibility.Collapsed;
            PageActionPanel.Visibility = Visibility.Collapsed;
            HomeNavigationPanel.Visibility = Visibility.Visible;

            if (score.PotentialScoreGain > 0)
            {
                PotentialGainBorder.Visibility = Visibility.Visible;
                PotentialGainText.Text = string.Format("Intelligent_PotentialGain".GetLocalized(), score.PotentialScoreGain);
            }
            else
            {
                PotentialGainBorder.Visibility = Visibility.Collapsed;
            }

            PerfScoreText.Text = $"{"Intelligent_Category_Performance".GetLocalized()}: {score.PerformanceScore}%";
            PerfScoreProgress.Value = score.PerformanceScore;

            PrivScoreText.Text = $"{"Intelligent_Category_Privacy".GetLocalized()}: {score.PrivacyScore}%";
            PrivScoreProgress.Value = score.PrivacyScore;

            FeatScoreText.Text = $"{"Intelligent_Category_Usability".GetLocalized()}: {score.FeaturesScore}%";
            FeatScoreProgress.Value = score.FeaturesScore;
        }

        UpdateApplyButtonText();
    }

    private void CalculateGain(List<OptimizationItemModel> catItems)
    {
        var unapplied = catItems.Where(i => !i.IsApplied && (i.Risk == RiskLevel.Safe || i.IsRecommended));
        int gainPoints = unapplied.Sum(i => i.ScoreWeight);
        int totalWeight = catItems.Sum(i => i.ScoreWeight);
        int gainPct = totalWeight > 0 ? (int)Math.Round((double)gainPoints / totalWeight * 100) : 0;

        if (gainPct > 0)
        {
            PotentialGainBorder.Visibility = Visibility.Visible;
            PotentialGainText.Text = string.Format("Intelligent_PotentialGain".GetLocalized(), gainPct);
        }
        else
        {
            PotentialGainBorder.Visibility = Visibility.Collapsed;
        }
    }

    private static string GetGrade(int score) => score switch
    {
        >= 90 => "Intelligent_HealthGrade_Excellent".GetLocalized(),
        >= 75 => "Intelligent_HealthGrade_Good".GetLocalized(),
        >= 60 => "Intelligent_HealthGrade_Fair".GetLocalized(),
        >= 40 => "Intelligent_HealthGrade_NeedsTuning".GetLocalized(),
        _ => "Intelligent_HealthGrade_Unoptimized".GetLocalized()
    };

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton clickedBtn && clickedBtn.Tag is string mode)
        {
            _selectedMode = mode;
            UpdateModeButtonsUI();
            UpdateApplyButtonText();
        }
    }

    private void UpdateModeButtonsUI()
    {
        if (ModeRecommendedBtn != null) ModeRecommendedBtn.IsChecked = _selectedMode == "Recommended";
        if (ModeAllSafeBtn != null) ModeAllSafeBtn.IsChecked = _selectedMode == "AllSafe";
        if (ModeAdvancedBtn != null) ModeAdvancedBtn.IsChecked = _selectedMode == "Advanced";
    }

    private void UpdateApplyButtonText()
    {
        if (MainApplyBtnText == null) return;

        var candidates = GetCandidateItemsForMode(_selectedMode);
        int count = candidates.Count;

        string modeLabel = _selectedMode switch
        {
            "AllSafe" => "Intelligent_Mode_Safe".GetLocalized(),
            "Advanced" => "Intelligent_Mode_Adv".GetLocalized(),
            _ => "Intelligent_Mode_Rec".GetLocalized()
        };

        MainApplyBtnText.Text = count > 0
            ? string.Format("Intelligent_Apply_Format".GetLocalized(), modeLabel, count)
            : string.Format("Intelligent_Apply_Active".GetLocalized(), modeLabel);
    }

    private List<OptimizationItemModel> GetCandidateItemsForMode(string mode)
    {
        var pageTags = GetParentPageToggleTags();
        var domainFiltered = _allScannedItems
            .Where(i => pageTags.Count == 0 || pageTags.Contains(i.Tag))
            .ToList();

        return mode switch
        {
            "AllSafe" => domainFiltered.Where(i => !i.IsApplied && i.Risk == RiskLevel.Safe).ToList(),
            "Advanced" => domainFiltered.Where(i => !i.IsApplied && (i.Risk == RiskLevel.Safe || i.Risk == RiskLevel.Moderate || i.Risk == RiskLevel.Advanced)).ToList(),
            _ => domainFiltered.Where(i => !i.IsApplied && (i.IsRecommended || i.Risk == RiskLevel.Safe)).ToList()
        };
    }

    private async void ApplySelectedMode_Click(object sender, RoutedEventArgs e)
    {
        var itemsToApply = GetCandidateItemsForMode(_selectedMode);

        if (itemsToApply.Count == 0)
        {
            string modeName = _selectedMode switch
            {
                "AllSafe" => "Intelligent_Mode_Safe".GetLocalized(),
                "Advanced" => "Intelligent_Mode_Adv".GetLocalized(),
                _ => "Intelligent_Mode_Rec".GetLocalized()
            };
            StatusMessageText.Text = string.Format("Intelligent_Status_AllActive".GetLocalized(), modeName);
            return;
        }

        await ExecuteApplyAsync(itemsToApply);
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        var pageTags = GetParentPageToggleTags();
        var allUnappliedOnPage = _allScannedItems
            .Where(i => !i.IsApplied && (pageTags.Count == 0 || pageTags.Contains(i.Tag)))
            .ToList();

        if (allUnappliedOnPage.Count == 0)
        {
            StatusMessageText.Text = "Intelligent_Preview_Empty".GetLocalized();
            return;
        }

        // Pre-select items based on currently toggled mode
        var modeCandidates = new HashSet<string>(GetCandidateItemsForMode(_selectedMode).Select(i => i.Tag), StringComparer.OrdinalIgnoreCase);
        foreach (var item in allUnappliedOnPage)
        {
            item.IsSelectedForApply = modeCandidates.Contains(item.Tag);
        }

        if (PreviewDialog.XamlRoot == null && this.XamlRoot != null)
        {
            PreviewDialog.XamlRoot = this.XamlRoot;
        }

        string modeName = _selectedMode switch
        {
            "AllSafe" => "Intelligent_Mode_Safe".GetLocalized(),
            "Advanced" => "Intelligent_Mode_Adv".GetLocalized(),
            _ => "Intelligent_Mode_Rec".GetLocalized()
        };
        PreviewDialog.Title = string.Format("Intelligent_Preview_Title".GetLocalized(), modeName);
        PreviewDialog.PrimaryButtonText = "Intelligent_Preview_ApplyBtn".GetLocalized();
        PreviewDialog.CloseButtonText = "Intelligent_Preview_CancelBtn".GetLocalized();

        PreviewListView.ItemsSource = allUnappliedOnPage;
        var res = await PreviewDialog.ShowAsync();
        if (res == ContentDialogResult.Primary)
        {
            var selectedToApply = allUnappliedOnPage.Where(i => i.IsSelectedForApply).ToList();
            if (selectedToApply.Count > 0)
            {
                await ExecuteApplyAsync(selectedToApply);
            }
        }
    }

    private async void RollbackPageItems_Click(object sender, RoutedEventArgs e)
    {
        var pageTags = GetParentPageToggleTags();
        var itemsToRollback = _allScannedItems
            .Where(i => i.RollbackAvailable && (pageTags.Count == 0 || pageTags.Contains(i.Tag)))
            .ToList();

        if (itemsToRollback.Count == 0)
        {
            StatusMessageText.Text = "Intelligent_Status_NoRollbacks".GetLocalized();
            return;
        }

        MainProgressBar.Visibility = Visibility.Visible;
        StatusMessageText.Text = string.Format("Intelligent_Status_RollingBack".GetLocalized(), itemsToRollback.Count);

        foreach (var item in itemsToRollback)
        {
            await IntelligentOptimizationEngine.RollbackItemAsync(item);
        }

        await RefreshScoreAsync();
        if (_parentPage != null)
        {
            IntelligentCardEnhancer.EnhancePage(_parentPage);
        }

        StatusMessageText.Text = string.Format("Intelligent_Status_RollbackDone".GetLocalized(), itemsToRollback.Count);
    }

    private async Task ExecuteApplyAsync(List<OptimizationItemModel> items)
    {
        try
        {
            MainProgressBar.Visibility = Visibility.Visible;
            MainProgressBar.IsIndeterminate = false;

            var applyProgress = new Progress<(int Current, int Total, string Status)>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    MainProgressBar.Value = ((double)p.Current / p.Total) * 100;
                    StatusMessageText.Text = string.Format("Intelligent_Status_Applying".GetLocalized(), p.Current, p.Total, p.Status);
                });
            });

            await IntelligentOptimizationEngine.ApplyAsync(items, applyProgress);

            StatusMessageText.Text = "Intelligent_Status_Verifying".GetLocalized();
            await IntelligentOptimizationEngine.VerifyAsync(items);

            await RefreshScoreAsync();

            if (_parentPage != null)
            {
                IntelligentCardEnhancer.EnhancePage(_parentPage);
            }

            StatusMessageText.Text = string.Format("Intelligent_Status_Success".GetLocalized(), items.Count);
        }
        catch (Exception ex)
        {
            _ = LogHelper.LogError($"[IntelligentScoreHeader] Apply error: {ex.Message}");
            StatusMessageText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            MainProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void NavigateToPerformance_Click(object sender, RoutedEventArgs e) =>
        App.GetService<INavigationService>().NavigateTo(typeof(OptimizeSystemPage).FullName!);

    private void NavigateToPrivacy_Click(object sender, RoutedEventArgs e) =>
        App.GetService<INavigationService>().NavigateTo(typeof(PrivacyPage).FullName!);

    private void NavigateToFeatures_Click(object sender, RoutedEventArgs e) =>
        App.GetService<INavigationService>().NavigateTo(typeof(FeaturesPage).FullName!);

    private HashSet<string> GetParentPageToggleTags()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_parentPage == null) return set;

        foreach (var toggle in IntelligentCardEnhancer.FindVisualChildren<ToggleSwitch>(_parentPage))
        {
            if (toggle.Tag is string tag && !string.IsNullOrEmpty(tag))
            {
                set.Add(tag);
            }
        }
        return set;
    }

    private static Page? FindParentPage(DependencyObject element)
    {
        DependencyObject current = element;
        while (current != null)
        {
            if (current is Page page) return page;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
