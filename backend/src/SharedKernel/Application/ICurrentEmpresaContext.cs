namespace Millet.SharedKernel.Application;

/// <summary>
/// Empresa actual del request (multi-tenant). La implementación de producción
/// la resuelve desde el claim current_empresa_id del JWT del API (ver
/// ADR-0011). En Phase 1 hay una implementación placeholder que retorna
/// null hasta el flujo de auth real (PR 4+).
///
/// Soporta <see cref="Bypass"/> para escenarios donde no hay request: tests
/// unitarios que prueban entidades aisladas, migraciones que crean data
/// global, jobs de seed. El uso de Bypass está restringido por convención
/// (un test escanea código y falla si aparece fuera de tests/, Migrations/,
/// o Seed/).
/// </summary>
public interface ICurrentEmpresaContext
{
    /// <summary>EmpresaId actual; null si no hay request o no hay auth.</summary>
    Guid? Current { get; }

    /// <summary>True si actualmente estamos dentro de un Bypass scope.</summary>
    bool IsBypassed { get; }

    /// <summary>
    /// Activa un bypass del filtro de empresa. Debe usarse SOLO en tests/,
    /// Migrations/, o código de seed. El interceptor de empresa no exige
    /// Current ni valida cross-empresa mientras el bypass está activo.
    /// Disposable: usar en bloque using.
    /// </summary>
    IDisposable Bypass();
}
