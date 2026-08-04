using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Identidad.Domain;

/// <summary>
/// Sujeto identificable del sistema que NO es una persona, sino una
/// aplicación M2M autenticándose vía Entra ID con el flow
/// <c>client_credentials</c> (App Registration / service principal).
///
/// <para>
/// Se modela como entidad separada de <see cref="Usuario"/> por dos
/// razones: (a) el contrato con Entra es distinto (claim <c>appid</c> en
/// vez de <c>oid</c> + <c>upn</c>), (b) el alcance de auditoría y
/// observabilidad es distinto (un SP NO tiene email, NO tiene preferencias
/// de UI, NO se asigna a múltiples empresas). Para el resto del stack
/// (idempotencia, audit_log, query filters) el handler de auth proyecta
/// al SP como un "usuario" via los claims estándar (<c>sub</c>,
/// <c>current_empresa_id</c>) — ver Api/Auth/EntraServicePrincipalAuthenticationHandler.
/// </para>
///
/// <para>
/// Decisión de empresa única (D-EMPRESA del prompt PR A): un SP siempre
/// actúa contra UNA empresa. ERP es single-tenant (Millet); multi-empresa
/// por SP queda fuera del modelo. Si en el futuro un SP necesita
/// multi-empresa, evaluar agregar <c>UsuarioServicioEmpresa</c> N:N.
/// </para>
///
/// <para>
/// Decisión de permisos directos (D-PERMISOS del prompt PR A): los
/// permisos se asignan directamente al SP via
/// <see cref="UsuarioServicioPermiso"/>, NO via Rol intermedio. Razón
/// pragmática: en fase 1 son ≤3 SPs y sus permisos son específicos
/// y conocidos. Si el catálogo de SPs crece a >10, refactorizar a
/// <c>UsuarioServicioRol</c> + reuso del modelo de <c>RolPermiso</c>.
/// </para>
///
/// Ver <c>docs/integration/01-api-contract.md</c> §3.5 y ADR-0003.
/// </summary>
public sealed class UsuarioServicio : BaseEntity, IAuditable
{
    public string Nombre { get; private set; } = string.Empty;
    public Guid EntraAppId { get; private set; }
    public Guid EntraObjectId { get; private set; }
    public Guid EmpresaId { get; private set; }
    public bool Activo { get; private set; } = true;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? DeactivatedAtUtc { get; private set; }
    public string? Notes { get; private set; }

    private UsuarioServicio() { } // EF Core

    public UsuarioServicio(
        Guid id,
        string nombre,
        Guid entraAppId,
        Guid entraObjectId,
        Guid empresaId,
        DateTimeOffset createdAtUtc,
        string? notes = null) : base(id)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("Nombre es requerido.", nameof(nombre));
        if (entraAppId == Guid.Empty)
            throw new ArgumentException("EntraAppId es requerido.", nameof(entraAppId));
        if (entraObjectId == Guid.Empty)
            throw new ArgumentException("EntraObjectId es requerido.", nameof(entraObjectId));
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId es requerido.", nameof(empresaId));

        Nombre = nombre;
        EntraAppId = entraAppId;
        EntraObjectId = entraObjectId;
        EmpresaId = empresaId;
        CreatedAtUtc = createdAtUtc;
        Notes = notes;
    }

    /// <summary>
    /// Actualiza metadata mutable del SP (Nombre, EntraObjectId, Notes).
    /// EmpresaId y EntraAppId NO son mutables en runtime (son la identidad
    /// del SP); para cambiar requieren registrar un SP nuevo y desactivar
    /// el viejo. Se invoca desde el bootstrap cuando detecta drift entre
    /// config y BD.
    /// </summary>
    public void ActualizarMetadata(string nombre, Guid entraObjectId, string? notes)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("Nombre es requerido.", nameof(nombre));
        if (entraObjectId == Guid.Empty)
            throw new ArgumentException("EntraObjectId es requerido.", nameof(entraObjectId));

        Nombre = nombre;
        EntraObjectId = entraObjectId;
        Notes = notes;
    }

    public void Desactivar(DateTimeOffset whenUtc)
    {
        if (!Activo) return;
        Activo = false;
        DeactivatedAtUtc = whenUtc;
    }

    public void Reactivar()
    {
        if (Activo) return;
        Activo = true;
        DeactivatedAtUtc = null;
    }
}
