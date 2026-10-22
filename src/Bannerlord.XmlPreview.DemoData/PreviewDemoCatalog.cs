namespace Bannerlord.XmlPreview.DemoData;

public static class PreviewDemoCatalog
{
    public const string FrameBorderColor = "#B7985A";

    public static IReadOnlyDictionary<string, string> MockBindings { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["LandManagementString"] = "Land Management",
            ["ActionText"] = "Resume",
            ["IsDisabled"] = "false",
            ["IsPositiveBehaviored"] = "false",
            ["NavigationButtonsEnabled"] = "true",
            ["GameTipTitle"] = "Game Tips",
            ["CurrentTip"] = "You can reset your perks at the tournament master...for the right price.",
            ["LedgerString"] = "Ledger",
            ["LandPlotsString"] = "Land Plots",
            ["OwnedLandPlotsString"] = "Owned:",
            ["OwnedPlotsString"] = "0",
            ["AvailableLandPlotsString"] = "Available:",
            ["AvaliblePlotsString"] = "10",
            ["BuyString"] = "Buy",
            ["SellString"] = "Sell",
            ["UndevLandPlotsString"] = "Undeveloped Land Plots",
            ["OwnedUndevelopedPlotsString"] = "0",
            ["AvalibleUndevelopedPlotsString"] = "20",
            ["SlavesTitle"] = "Slaves",
            ["CapacityText"] = "Capacity",
            ["SlaveCapacityString"] = "0 / 0",
            ["ManageString"] = "Manage",
            ["SlaveDeclineTitleString"] = "Slave Decline:",
            ["SlaveDeclineString"] = "5%",
            ["RevoltRiskTitleString"] = "Revolt Risk:",
            ["RevoltRiskString"] = "0%",
            ["LandUsageTitleString"] = "Land Usage:",
            ["LandUsageString"] = "0%",
            ["BuySlavesTitleString"] = "Buy Market:",
            ["BuySlavesString"] = "Off",
            ["BuySlavesButton"] = "ButtonBrush1",
            ["ProductionDetailsString"] = "Production Details",
            ["StockpileTitle"] = "Stockpile",
            ["StockpileCapacityString"] = "0 / 500",
            ["SellToMarketTitleString"] = "Sell to Market:",
            ["SellToMarketString"] = "Off",
            ["SellToMarketButton"] = "ButtonBrush2",
            ["CurrentProjectTitle"] = "Current Project",
            ["CurrentProjectString"] = "No Current Project",
            ["CancelString"] = "Cancel",
            ["ProgressTitle"] = "Progress:",
            ["CurrentProjecProgressString"] = "0/0",
            ["ProjectQueueString"] = "Project Queue",
            ["UpgradesTitle"] = "Upgrades",
            ["LandClearanceString"] = "Land Clearance",
            ["LandClearanceDescription"] = "Clear land for farming",
            ["PatrolsDescription"] = "Improve estate security",
            ["StorehouseDescription"] = "Increase storage capacity",
            ["UpgradeCostString"] = "2,000",
            ["UpgradeBuildTimeString"] = "7 days",
            ["BuySlavesKnobOffset"] = "4",
            ["BuySlavesTextOffset"] = "20",
            ["SellToMarketKnobOffset"] = "4",
            ["SellToMarketTextOffset"] = "20",
            ["CurrentProjectProgressWidth"] = "0",
            ["UpgradePatrolsString"] = "Increase Patrols",
            ["UpgradeStorehouseString"] = "Expand Storehouse",
            ["UpgradeString"] = "      Upgrade      ",
            ["CloseString"] = "Close",
            ["SettlementImageID"] = "gui_bg_village_empire_t",
            ["CurrentProjectIconSting"] = "ae_storehouse"
        };

    public static IReadOnlyDictionary<string, PreviewGradientBrushFallback> PanelBrushes { get; } =
        new Dictionary<string, PreviewGradientBrushFallback>(StringComparer.OrdinalIgnoreCase)
        {
            ["Encyclopedia.Frame"] = new("#2A1F13", "#B7985A"),
            ["ButtonBrush1"] = new("#425530", "#A5C47A"),
            ["ButtonBrush2"] = new("#5A332C", "#D68A79")
        };

    public static IReadOnlyDictionary<string, string> TextBrushColors { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Recruitment.Popup.Title.Text"] = "#E6D2A4",
            ["OverlayPopup.ButtonText"] = "#F4EFE4"
        };

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> BrushMetricFallbacks { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Encyclopedia.Frame"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Width"] = "1500",
                ["Height"] = "1000"
            }
        };

    public static IReadOnlyList<EscapeMenuPreviewItem> EscapeMenuItems { get; } =
    [
        new("Return to the Game", false, true),
        new("Campaign Options", false, false),
        new("Options", false, false),
        new("Save", false, false),
        new("Save As", false, false),
        new("Load", false, false),
        new("Save And Exit", false, false),
        new("Exit to Main Menu", false, false)
    ];
}

public sealed record PreviewGradientBrushFallback(string FillHex, string StrokeHex);

public sealed record EscapeMenuPreviewItem(string ActionText, bool IsDisabled, bool IsPositiveBehaviored);
