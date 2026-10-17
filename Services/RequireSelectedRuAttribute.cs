using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RefineryContractAPI.Services;

/// <summary>
/// Multi-RU: data baru milik satu RU. Super Admin / Head Office yang sedang memilih "Semua RU"
/// harus memilih RU dulu di header sebelum menambah data, supaya data tidak diam-diam masuk RU IV.
/// User RU biasa tidak terpengaruh (data selalu masuk RU-nya sendiri).
/// Respons membawa header "X-Ru-Required" supaya tampilan bisa langsung menawarkan pemilihan RU.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class RequireSelectedRuAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var currentRu = context.HttpContext.RequestServices.GetRequiredService<ICurrentRu>();
        if (!currentRu.IsCrossRu || currentRu.Filter != null) return;

        context.HttpContext.Response.Headers["X-Ru-Required"] = "1";
        context.Result = new BadRequestObjectResult(new
        {
            code = "RU_REQUIRED",
            message = "Pilih RU terlebih dahulu di bagian atas halaman sebelum menambah data. " +
                      "Saat \"Semua RU\" dipilih, sistem tidak tahu data baru ini milik RU mana.",
        });
    }
}
