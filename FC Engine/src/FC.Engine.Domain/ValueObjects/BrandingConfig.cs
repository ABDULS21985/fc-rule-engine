namespace FC.Engine.Domain.ValueObjects;

public class BrandingConfig
{
    public string? LogoUrl { get; set; }
    public string? LogoSmallUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? AccentColor { get; set; }
    public string? DangerColor { get; set; }
    public string? SuccessColor { get; set; }
    public string? WarningColor { get; set; }
    public string? BackgroundColor { get; set; }
    public string? SidebarColor { get; set; }
    public string? FontHeading { get; set; }
    public string? FontBody { get; set; }
    public string? CompanyName { get; set; }
    public string? SupportEmail { get; set; }
    public string? SupportPhone { get; set; }
    public string? CopyrightText { get; set; }
    public string? WatermarkText { get; set; }
    public string? LoginBackgroundUrl { get; set; }
    public string? LoginTagline { get; set; }
    public string? TrustBadge1 { get; set; }
    public string? TrustBadge2 { get; set; }
    public string? TrustBadge3 { get; set; }
    public string? CustomCss { get; set; }
    public string? EmailHeaderColor { get; set; }
    public string? EmailBodyBackground { get; set; }
    public string? ThemePresetName { get; set; }

    public static BrandingConfig WithDefaults(BrandingConfig? custom = null)
    {
        return new BrandingConfig
        {
            LogoUrl = custom?.LogoUrl ?? "/images/cbn-logo.svg",
            LogoSmallUrl = custom?.LogoSmallUrl ?? "/images/cbn-logo.svg",
            FaviconUrl = custom?.FaviconUrl ?? "/favicon.svg",
            PrimaryColor = custom?.PrimaryColor ?? "#006B3F",
            SecondaryColor = custom?.SecondaryColor ?? "#C8A415",
            AccentColor = custom?.AccentColor ?? "#1A73E8",
            DangerColor = custom?.DangerColor ?? "#DC3545",
            SuccessColor = custom?.SuccessColor ?? "#28A745",
            WarningColor = custom?.WarningColor ?? "#FFC107",
            BackgroundColor = custom?.BackgroundColor ?? "#F8F9FA",
            SidebarColor = custom?.SidebarColor ?? "#1A1F2B",
            FontHeading = custom?.FontHeading ?? "Plus Jakarta Sans",
            FontBody = custom?.FontBody ?? "Inter",
            CompanyName = custom?.CompanyName ?? "RegOS",
            SupportEmail = custom?.SupportEmail,
            SupportPhone = custom?.SupportPhone,
            CopyrightText = custom?.CopyrightText ?? $"(c) {DateTime.UtcNow.Year} RegOS. All rights reserved.",
            WatermarkText = custom?.WatermarkText ?? "CONFIDENTIAL",
            LoginBackgroundUrl = custom?.LoginBackgroundUrl,
            LoginTagline = custom?.LoginTagline ?? "Financial Returns Collection & Analysis System",
            TrustBadge1 = custom?.TrustBadge1 ?? "Role-Based Access",
            TrustBadge2 = custom?.TrustBadge2 ?? "Audit Logging",
            TrustBadge3 = custom?.TrustBadge3 ?? "Encrypted Sessions",
            CustomCss = custom?.CustomCss,
            EmailHeaderColor = custom?.EmailHeaderColor ?? "#006B3F",
            EmailBodyBackground = custom?.EmailBodyBackground ?? "#F8F9FA",
            ThemePresetName = custom?.ThemePresetName ?? "Financial Green",
        };
    }
}
