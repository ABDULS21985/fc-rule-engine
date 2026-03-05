namespace FC.Engine.Domain.Security;

public static class PermissionCatalog
{
    public const string TemplateRead = "template.read";
    public const string TemplateEdit = "template.edit";
    public const string TemplatePublish = "template.publish";

    public const string SubmissionRead = "submission.read";
    public const string SubmissionCreate = "submission.create";
    public const string SubmissionEdit = "submission.edit";
    public const string SubmissionValidate = "submission.validate";
    public const string SubmissionSubmit = "submission.submit";
    public const string SubmissionReview = "submission.review";
    public const string SubmissionApprove = "submission.approve";
    public const string SubmissionReject = "submission.reject";
    public const string SubmissionExport = "submission.export";
    public const string SubmissionDelete = "submission.delete";

    public const string UserRead = "user.read";
    public const string UserCreate = "user.create";
    public const string UserEdit = "user.edit";
    public const string UserDeactivate = "user.deactivate";
    public const string UserRoleAssign = "user.role.assign";

    public const string BillingRead = "billing.read";
    public const string BillingManage = "billing.manage";

    public const string ReportRead = "report.read";
    public const string ReportCreate = "report.create";
    public const string ReportSchedule = "report.schedule";

    public const string SettingsRead = "settings.read";
    public const string SettingsEdit = "settings.edit";
    public const string SettingsBranding = "settings.branding";

    public const string AuditRead = "audit.read";

    public const string CalendarRead = "calendar.read";
    public const string CalendarManage = "calendar.manage";

    public const string NotificationManage = "notification.manage";
    public const string AdminPlatform = "admin.platform";

    public static readonly IReadOnlyList<string> All = new[]
    {
        TemplateRead, TemplateEdit, TemplatePublish,
        SubmissionRead, SubmissionCreate, SubmissionEdit, SubmissionValidate, SubmissionSubmit,
        SubmissionReview, SubmissionApprove, SubmissionReject, SubmissionExport, SubmissionDelete,
        UserRead, UserCreate, UserEdit, UserDeactivate, UserRoleAssign,
        BillingRead, BillingManage,
        ReportRead, ReportCreate, ReportSchedule,
        SettingsRead, SettingsEdit, SettingsBranding,
        AuditRead,
        CalendarRead, CalendarManage,
        NotificationManage,
        AdminPlatform
    };

    private static readonly IReadOnlyList<string> AdminWithoutPlatform = All
        .Where(x => !string.Equals(x, AdminPlatform, StringComparison.OrdinalIgnoreCase))
        .ToArray();

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultRolePermissions =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Admin"] = AdminWithoutPlatform,
            ["Maker"] = new[]
            {
                TemplateRead,
                SubmissionRead, SubmissionCreate, SubmissionEdit, SubmissionValidate, SubmissionSubmit,
                ReportRead,
                CalendarRead
            },
            ["Checker"] = new[]
            {
                TemplateRead,
                SubmissionRead, SubmissionReview, SubmissionReject,
                ReportRead
            },
            ["Approver"] = new[]
            {
                TemplateRead,
                SubmissionRead, SubmissionApprove, SubmissionReject,
                ReportRead
            },
            ["Viewer"] = new[]
            {
                TemplateRead,
                SubmissionRead,
                ReportRead,
                CalendarRead
            },
            ["PlatformAdmin"] = All
        };
}
