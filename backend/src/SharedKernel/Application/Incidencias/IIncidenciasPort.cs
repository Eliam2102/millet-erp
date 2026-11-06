namespace Millet.SharedKernel.Application.Incidencias;

/// <summary>
/// Solicitud para abrir una incidencia desde la rama de falla de un paso (U-4). <paramref name="Paso"/> y
/// <paramref name="PasoReanudacion"/> son claves de <see cref="PasosIncidencia"/>; <paramref name="DetalleClave"/>
/// distingue varias del mismo paso y documento (p. ej. la línea).
/// </summary>
public sealed record AbrirIncidenciaSolicitud(
    string Modulo,
    string TipoDocumento,
    Guid DocumentoId,
    string DocumentoEtiqueta,
    string Paso,
    string Motivo,
    Guid ResponsableUsuarioId,
    Guid? SucursalId = null,
    string? CodigoMotivo = null,
    string? Detalle = null,
    string? DetalleClave = null,
    DateOnly? FechaCompromiso = null,
    string? PasoReanudacion = null,
    string? RutaReanudacion = null);

/// <summary>Resultado de abrir: el id y si ya existía una pendiente igual (no se duplica).</summary>
public sealed record IncidenciaAbierta(Guid Id, long Numero, bool YaExistia);

/// <summary>
/// Puerto común de incidencias (U-4 · 86e3yzemw) para cualquier módulo. La implementación vive en Compartido
/// (<c>compartido.incidencias</c>) y guarda en su propia transacción: se llama <b>después</b> de guardar el
/// rechazo del módulo, o en un bloque aparte cuando el rechazo es una excepción que revierte la operación.
/// El actor es el usuario actual; sin usuario (workers, eventos) queda como «Sistema».
/// </summary>
public interface IIncidenciasPort
{
    /// <summary>Abre la incidencia; si ya hay una pendiente con el mismo paso, documento y detalle, devuelve esa.</summary>
    Task<IncidenciaAbierta> AbrirAsync(AbrirIncidenciaSolicitud solicitud, CancellationToken cancellationToken);

    /// <summary>
    /// Resuelve las pendientes del paso y documento (todas, o solo la de <paramref name="detalleClave"/>) cuando
    /// el flujo se retomó. Devuelve cuántas resolvió.
    /// </summary>
    Task<int> ResolverPorDocumentoAsync(string paso, Guid documentoId, string? detalleClave, string resolucion, CancellationToken cancellationToken);

    /// <summary>¿Hay incidencias pendientes del paso (o de cualquier paso si es nulo) para el documento?</summary>
    Task<bool> HayPendientesAsync(Guid documentoId, string? paso, CancellationToken cancellationToken);
}

/// <summary>Paso conocido: clave estable, nombre para mostrar y módulo.</summary>
public sealed record PasoIncidencia(string Clave, string Nombre, string Modulo);

/// <summary>
/// Catálogo de pasos con incidencia (claves estables que guardan las incidencias). Una clave no listada
/// se acepta igual (se muestra la clave); este catálogo solo da nombres legibles y alimenta los filtros.
/// </summary>
public static class PasosIncidencia
{
    // Oleada 1 · rama de falla.
    public const string AlmRecepcionDiferenciaOc = "alm.recepcion.diferencia-oc";
    public const string AdmProveedorDocumentoObservado = "adm.proveedor.documento-observado";
    public const string AdmProveedorCuentaBancaria = "adm.proveedor.cuenta-bancaria";
    public const string AlmUbicacionesAsignacion = "alm.ubicaciones.asignacion";
    public const string AlmValeRectificar = "alm.vale.rectificar";
    public const string AlmSalidaAcuse = "alm.salida.acuse";
    public const string AlmCostoNetoFactura = "alm.costo.neto-factura";

    // Pasos donde se retoma.
    public const string AlmRecepcionRegistrar = "alm.recepcion.registrar";
    public const string AdmProveedorExpediente = "adm.proveedor.expediente";
    public const string AdmProveedorCuentas = "adm.proveedor.cuentas";
    public const string AlmUbicacionesAsignar = "alm.ubicaciones.asignar";
    public const string AlmValeRegularizar = "alm.vale.regularizar";
    public const string AlmSalidaDevolucionInterna = "alm.salida.devolucion-interna";
    public const string CxpFacturaRevision = "cxp.factura.revision";

    public static IReadOnlyList<PasoIncidencia> Todos { get; } =
    [
        new(AlmRecepcionDiferenciaOc, "Diferencia de recepción contra la orden de compra", "almacen"),
        new(AdmProveedorDocumentoObservado, "Documento del proveedor observado", "datos_maestros"),
        new(AdmProveedorCuentaBancaria, "Aclarar cambio de cuenta bancaria del proveedor", "datos_maestros"),
        new(AlmUbicacionesAsignacion, "Relación artículo-ubicación", "almacen"),
        new(AlmValeRectificar, "Rectificar vale y su requisición", "almacen"),
        new(AlmSalidaAcuse, "Aclarar firma, conteo o acuse de surtido", "almacen"),
        new(AlmCostoNetoFactura, "Diferencia del neto de factura contra la entrada", "cuentas_por_pagar"),
        new(AlmRecepcionRegistrar, "Registrar la recepción", "almacen"),
        new(AdmProveedorExpediente, "Expediente del proveedor", "datos_maestros"),
        new(AdmProveedorCuentas, "Cuentas bancarias del proveedor", "datos_maestros"),
        new(AlmUbicacionesAsignar, "Asignar artículo a ubicación", "almacen"),
        new(AlmValeRegularizar, "Regularizar el vale con su requisición", "almacen"),
        new(AlmSalidaDevolucionInterna, "Devolución interna de la salida", "almacen"),
        new(CxpFacturaRevision, "Revisión de la factura del proveedor", "cuentas_por_pagar"),
    ];

    private static readonly Dictionary<string, PasoIncidencia> PorClave = Todos.ToDictionary(p => p.Clave);

    public static string Nombre(string? clave) =>
        clave is null ? string.Empty : PorClave.TryGetValue(clave, out var p) ? p.Nombre : clave;
}
