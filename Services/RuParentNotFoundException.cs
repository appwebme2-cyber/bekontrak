using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RefineryContractAPI.Services;

/// <summary>
/// Multi-RU: data turunan (tagihan, amandemen, progress, RAB, dokumen, dll.) menunjuk kontrak atau
/// draft yang tidak ada di RU pengguna. Dilempar AppDbContext sebelum data disimpan.
/// </summary>
public class RuParentNotFoundException : InvalidOperationException
{
    public RuParentNotFoundException(string parent)
        : base($"{parent} tidak ditemukan di RU Anda.") { }
}

/// <summary>Ubah RuParentNotFoundException yang tidak ditangkap controller menjadi 404 dengan pesan jelas.</summary>
public class RuParentNotFoundFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not RuParentNotFoundException ex) return;
        context.Result = new NotFoundObjectResult(new { message = ex.Message });
        context.ExceptionHandled = true;
    }
}
