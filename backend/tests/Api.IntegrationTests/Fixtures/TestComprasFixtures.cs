using Millet.Almacen.Infrastructure.Seed;
using Millet.Compartido.Infrastructure.Seed;

namespace Millet.Api.IntegrationTests.Fixtures;

/// <summary>
/// Constantes + helpers compartidos para los integration tests del
/// módulo Compras (PR-A2 Lote B). Reemplaza la <c>SucursalIdFija =
/// 00000003-0001-*</c> deprecada y los <c>Guid.CreateVersion7()</c>
/// aleatorios de depto/almacén que dejaron de pasar la validación
/// cross-table introducida por Lote A.
///
/// <para>
/// <b>Single source of truth:</b> las constantes <c>SucursalMid</c>,
/// <c>DeptoCompras</c>, <c>AlmacenMidGeneral</c> son alias de los
/// GUIDs deterministas que el seed productivo ya expone
/// (<see cref="AlmacenSeedHostedService.AlmMidGeneralId"/>,
/// <see cref="CatalogosTestSeedHostedService.DepartamentoComprasId"/>,
/// etc.). Si el seed productivo cambia un GUID, este helper migra
/// automáticamente.
/// </para>
///
/// <para>
/// <b>Duplicado intencional:</b> existe una copia idéntica en
/// <c>Compras.IntegrationTests/Fixtures/TestComprasFixtures.cs</c>.
/// Los dos proyectos de test no se referencian entre sí (es
/// anti-patrón en .NET), y crear un <c>Compras.TestSupport.csproj</c>
/// nuevo para 50 líneas es overengineering. La duplicación es chica y
/// el archivo cambia raramente; cuando lo haga, sincronizar los dos.
/// </para>
///
/// <para>
/// <b>Sucursal MID</b>: la sucursal canónica del seed
/// (<c>CatalogosTestSeedHostedService.TestSucursales[0]</c>) con
/// clave "MID" y nombre "Planta México Centro". El depto canónico
/// para tests es <c>COMPRAS</c> y el almacén destino es el general de
/// MID (<c>ALM-MID-G</c>). La asignación N:M
/// <c>(MID, COMPRAS, Activo)</c> la sembró PR-A1 en
/// <see cref="CatalogosTestSeedHostedService.SeedAsignacionesSucursalDepartamentoAsync"/>.
/// </para>
/// </summary>
public static class TestComprasFixtures
{
    // ─── GUIDs canónicos del seed productivo ────────────────────────

    /// <summary>
    /// Sucursal canónica de tests: MID (Planta México Centro).
    /// Mirror del primer item de
    /// <c>CatalogosTestSeedHostedService.TestSucursales</c>.
    /// </summary>
    public static readonly Guid SucursalMid =
        Guid.Parse("00000005-0003-0000-0000-000000000001");

    /// <summary>Sucursal MTY — útil para tests negativos de coherencia.</summary>
    public static readonly Guid SucursalMty =
        Guid.Parse("00000005-0003-0000-0000-000000000002");

    /// <summary>Sucursal QRO — útil para tests negativos de coherencia.</summary>
    public static readonly Guid SucursalQro =
        Guid.Parse("00000005-0003-0000-0000-000000000003");

    /// <summary>
    /// Departamento canónico de tests: COMPRAS. Alias del GUID público
    /// expuesto por el seed.
    /// </summary>
    public static readonly Guid DeptoCompras =
        CatalogosTestSeedHostedService.DepartamentoComprasId;

    /// <summary>
    /// Almacén destino canónico de tests: ALM-MID-G (General MID).
    /// Alias del GUID público expuesto por
    /// <see cref="AlmacenSeedHostedService"/>.
    /// </summary>
    public static readonly Guid AlmacenMidGeneral =
        AlmacenSeedHostedService.AlmMidGeneralId;

    /// <summary>
    /// Almacén alternativo en MID (ALM-MID-MP, Materia Prima). Útil
    /// cuando un test necesita un segundo almacén distinto en la
    /// misma sucursal.
    /// </summary>
    public static readonly Guid AlmacenMidMp =
        AlmacenSeedHostedService.AlmMidMpId;

    /// <summary>
    /// Almacén general de MTY. Útil para tests negativos de
    /// <c>RQ_ALMACEN_NO_PERTENECE_A_SUCURSAL</c> (RQ con sucursal=MID
    /// + almacén=MTY → 422).
    /// </summary>
    public static readonly Guid AlmacenMtyGeneral =
        AlmacenSeedHostedService.AlmMtyGeneralId;

    /// <summary>
    /// Código alfanumérico de la sucursal MID (entra al folio:
    /// <c>MID2026-NNNNNN</c>). Validado contra el regex
    /// <c>^[A-Z]{2,4}$</c> del backend.
    /// </summary>
    public const string SucursalCodigoMid = "MID";

    /// <summary>
    /// Año de folio default para tests. Coordinado con
    /// <c>ComprasTestSeedHostedService</c> que adelanta
    /// <c>folio_secuencias</c> a <c>siguiente=10001</c> para
    /// <c>(empresa-bootstrap, MID, 2026)</c>.
    /// </summary>
    public const short FolioAnioDefault = 2026;

    // ─── Helper para body del POST /api/v1/compras/requisiciones ────

    /// <summary>
    /// Construye un body válido para
    /// <c>POST /api/v1/compras/requisiciones</c> usando las
    /// constantes canónicas de este fixture. Cada parámetro es
    /// opcional y se override-able para tests específicos.
    /// (PR3: la RQ manual ya no captura almacén — el body no lo lleva.)
    /// </summary>
    public static CrearRequisicionRequestBody BuildCrearRqValidBody(
        Guid? sucursalId = null,
        string? sucursalCodigo = null,
        short? folioAnio = null,
        Guid? departamentoId = null,
        int clasificacion = 2,           // MateriaPrima
        int prioridad = 1,               // Normal
        DateTimeOffset? fechaSolicitud = null,
        DateOnly? fechaEntregaDeseada = null,
        Guid? proveedorSugeridoId = null,
        string? descripcion = "Test integration",
        Guid? requisitanteId = null) => new(
            SucursalId: sucursalId ?? SucursalMid,
            SucursalCodigo: sucursalCodigo ?? SucursalCodigoMid,
            FolioAnio: folioAnio ?? FolioAnioDefault,
            DepartamentoId: departamentoId ?? DeptoCompras,
            Clasificacion: clasificacion,
            Prioridad: prioridad,
            FechaSolicitud: fechaSolicitud ?? DateTimeOffset.UtcNow,
            FechaEntregaDeseada: fechaEntregaDeseada,
            ProveedorSugeridoId: proveedorSugeridoId,
            Descripcion: descripcion,
            RequisitanteId: requisitanteId);
}

/// <summary>
/// Body canónico del POST /api/v1/compras/requisiciones — mismo shape
/// del <c>CrearRequisicionCommand</c> backend (sin EmpresaId ni
/// CreadorId, que el handler resuelve del JWT).
/// </summary>
public sealed record CrearRequisicionRequestBody(
    Guid SucursalId,
    string SucursalCodigo,
    short FolioAnio,
    Guid DepartamentoId,
    int Clasificacion,
    int Prioridad,
    DateTimeOffset FechaSolicitud,
    DateOnly? FechaEntregaDeseada,
    Guid? ProveedorSugeridoId,
    string? Descripcion,
    Guid? RequisitanteId);
