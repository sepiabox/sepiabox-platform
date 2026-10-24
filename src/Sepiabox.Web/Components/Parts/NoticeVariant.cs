namespace Sepiabox.Web.Components.Parts;

/// <summary>The variants of <see cref="Notice"/>.</summary>
public static class NoticeVariant
{
    public const string Success = "success";
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";

    /// <summary>Error for a resource key that names an error (<c>Person_Error_NameTaken</c>), success otherwise.</summary>
    public static string ForKey(string? key) =>
        key is not null && key.Contains("_Error_", StringComparison.Ordinal) ? Error : Success;
}
