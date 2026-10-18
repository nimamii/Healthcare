namespace Healthcare.Modules.Identity.Domain;

/// <summary>
/// Roles defined by the master spec (§15). These are the minimum required.
/// Authorization is always enforced server-side; a role name arriving from
/// a client is never trusted — it is only ever resolved from the token
/// this module issued.
/// </summary>
public static class Roles
{
    public const string Customer = "Customer";
    public const string Doctor = "Doctor";
    public const string Pharmacist = "Pharmacist";
    public const string PharmacyAdmin = "PharmacyAdmin";
    public const string Supplier = "Supplier";
    public const string SupplierStaff = "SupplierStaff";
    public const string PlatformAdmin = "PlatformAdmin";

    /// <summary>
    /// A user may hold more than one role (a doctor who also buys equipment,
    /// for example). Kept in one place so the seed data and the validators
    /// cannot drift apart.
    /// </summary>
    public static readonly IReadOnlyCollection<string> All =
    [
        Customer, Doctor, Pharmacist, PharmacyAdmin, Supplier, SupplierStaff, PlatformAdmin,
    ];
}
