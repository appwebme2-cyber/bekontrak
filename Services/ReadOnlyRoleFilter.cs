using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RefineryContractAPI.Services;

/// <summary>
/// Menolak permintaan tulis (POST/PUT/PATCH/DELETE) dari akun yang rolenya tidak punya hak tulis
/// sama sekali di matriks hak akses (mis. Head Office, Guest, External), supaya pembatasan
/// "hanya melihat" berlaku di API, bukan cuma di tampilan.
/// </summary>
public class ReadOnlyRoleFilter : IAsyncActionFilter
{
    // Controller yang boleh dipanggil semua user yang sudah login (data milik user itu sendiri).
    // Endpoint admin di dalamnya tetap dijaga [Authorize(Roles = "admin")].
    private static readonly HashSet<string> ExemptControllers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Auth", "Favorites", "Profile",
        "Chat", // chatbot hanya membaca data; POST dipakai untuk mengirim pertanyaan
    };

    private readonly RolePermissionService _permissions;

    public ReadOnlyRoleFilter(RolePermissionService permissions) => _permissions = permissions;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var method = http.Request.Method;
        var isWrite = HttpMethods.IsPost(method) || HttpMethods.IsPut(method) ||
                      HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

        // Hanya request yang sudah terautentikasi; endpoint publik (login, register) dibiarkan.
        if (!isWrite || http.User.Identity?.IsAuthenticated != true)
        {
            await next();
            return;
        }

        var controller = context.RouteData.Values["controller"]?.ToString();
        if (controller != null && ExemptControllers.Contains(controller))
        {
            await next();
            return;
        }

        var role = http.User.FindFirstValue(ClaimTypes.Role);
        if (!await _permissions.CanWriteAsync(role))
        {
            context.Result = new ObjectResult(new { message = "Akun Anda hanya memiliki akses lihat (read-only) dan tidak dapat mengubah data." })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        await next();
    }
}
