using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RefineryContractAPI.Services;

public static class SuperAdmin
{
    /// <summary>Super Admin = admin tanpa RU (token lama admin tanpa klaim id_ru juga Super Admin).</summary>
    public static bool IsSuperAdmin(this ClaimsPrincipal user) =>
        user.IsInRole("admin") && string.IsNullOrEmpty(user.FindFirst("id_ru")?.Value);
}

/// <summary>
/// Multi-RU: pengaturan yang berlaku untuk semua RU (konfigurasi sistem, hak akses role, SLA,
/// kredit AI) hanya boleh diubah Super Admin. Admin RU tetap bisa membacanya.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SuperAdminOnlyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) return; // [Authorize] yang menangani 401
        if (!user.IsSuperAdmin())
        {
            context.Result = new ObjectResult(new
            {
                message = "Pengaturan ini berlaku untuk semua RU dan hanya dapat diubah oleh Super Admin."
            }) { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
