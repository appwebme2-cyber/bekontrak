namespace RefineryContractAPI.Services;

/// <summary>
/// Endpoint POST yang tidak menyimpan data (mis. ekstraksi AI yang hanya mengembalikan usulan)
/// boleh dipanggil akun hanya-baca (Guest, Head Office). Dibaca oleh ReadOnlyRoleFilter.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class AllowReadOnlyRolesAttribute : Attribute { }
