using Microsoft.Extensions.Logging;
using Millet.Almacen.Domain.Ports;

namespace Millet.Almacen.Infrastructure.Stubs;

// ============================================================================
// Stubs NoOp de los puertos cross-module declarados en 01-diseno §6.1.
// Todos vivirán hasta que el módulo adapter real esté disponible — Compras
// (OC + RQ), DatosMaestros (Artículo + Proveedor), Administración
// (Sucursal + Empleado + TipoCambio), Contabilidad (Concepto), Finanzas
// (Periodo). Cada stub lleva su propio PLATFORM-TODO buscable con
// `rg "PLATFORM-TODO" backend/src/Almacen`.
//
// Los stubs son cara-de-juguete: devuelven fixture mínimo coherente o
// `null` para no bloquear el smoke del módulo. Los handlers reales
// validarán contra el adapter productivo en la fase del puerto
// correspondiente (F1+).
// ============================================================================

/// <summary>
/// PLATFORM-TODO(&lt;ComprasOcReadAdapter&gt;): adapter real consultando
/// <c>compras.ordenes_compra</c> en <c>ComprasDbContext</c>. Reemplaza este
/// stub cuando se cablee el adapter real (F2-PR2 / fase de Compras).
/// </summary>
public sealed class NoOpComprasOcReadPort : IComprasOcReadPort
{
    private readonly ILogger<NoOpComprasOcReadPort> _logger;

    public NoOpComprasOcReadPort(ILogger<NoOpComprasOcReadPort> logger)
    {
        _logger = logger;
    }

    public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpComprasOcReadPort] oc={OcId} → null (stub F0-PR1)", ocId);
        return Task.FromResult<OcLectura?>(null);
    }

    public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
        IReadOnlyCollection<Guid> ocIds, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpComprasOcReadPort] folios({Count}) → vacío (stub F0-PR1)", ocIds.Count);
        return Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
}

/// <summary>
/// PLATFORM-TODO(&lt;ComprasRqReadAdapter&gt;): adapter real consultando
/// <c>compras.requisiciones</c> en <c>ComprasDbContext</c>. Reemplaza este
/// stub cuando se cablee el adapter real (F4-PR1 / fase de Compras).
/// </summary>
public sealed class NoOpComprasRequisicionReadPort : IComprasRequisicionReadPort
{
    private readonly ILogger<NoOpComprasRequisicionReadPort> _logger;

    public NoOpComprasRequisicionReadPort(ILogger<NoOpComprasRequisicionReadPort> logger)
    {
        _logger = logger;
    }

    public Task<RequisicionLectura?> ObtenerAsync(Guid rqId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpComprasRequisicionReadPort] rq={RqId} → null (stub F0-PR1)", rqId);
        return Task.FromResult<RequisicionLectura?>(null);
    }

    public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
        IReadOnlyCollection<Guid> rqIds, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpComprasRequisicionReadPort] folios({Count}) → vacío (stub F0-PR1)", rqIds.Count);
        return Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
}

/// <summary>
/// PLATFORM-TODO(&lt;ComprasPedidoVivoReadAdapter&gt;): adapter real agregando
/// "vivo de origen sistema" por (articulo, almacén) desde <c>ComprasDbContext</c>
/// (ADR-0047 PR5.B). Reemplazado en <c>Program.cs</c> por
/// <c>Compras.Infrastructure.PublicAdapters.ComprasPedidoVivoReadAdapter</c>. El
/// stub devuelve vacío (interino correcto: sin RQ de sistema aún, vivo = 0).
/// </summary>
public sealed class NoOpComprasPedidoVivoReadPort : IComprasPedidoVivoReadPort
{
    private readonly ILogger<NoOpComprasPedidoVivoReadPort> _logger;

    public NoOpComprasPedidoVivoReadPort(ILogger<NoOpComprasPedidoVivoReadPort> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyDictionary<PedidoVivoClave, decimal>> ObtenerVivoDeSistemaAsync(
        IReadOnlyCollection<PedidoVivoClave> pares, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpComprasPedidoVivoReadPort] pares({Count}) → vacío (stub PR5.B)", pares.Count);
        return Task.FromResult<IReadOnlyDictionary<PedidoVivoClave, decimal>>(
            new Dictionary<PedidoVivoClave, decimal>());
    }
}

/// <summary>
/// PLATFORM-TODO(&lt;UsuarioServicioReadAdapter&gt;): adapter real en
/// <c>Identidad.Infrastructure.PublicAdapters</c> que resuelve el usuario de servicio
/// del reorden desde <c>identidad.usuario_servicio</c> (ADR-0047 PR5.C). Reemplazado
/// en <c>Program.cs</c>. El stub devuelve null (sin SP resuelto → el path de sistema
/// no puede crear la RQ, falla ruidoso aguas arriba).
/// </summary>
public sealed class NoOpUsuarioServicioReadPort : IUsuarioServicioReadPort
{
    private readonly ILogger<NoOpUsuarioServicioReadPort> _logger;

    public NoOpUsuarioServicioReadPort(ILogger<NoOpUsuarioServicioReadPort> logger)
    {
        _logger = logger;
    }

    public Task<UsuarioServicioLectura?> ObtenerReordenAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpUsuarioServicioReadPort] reorden → null (stub PR5.C)");
        return Task.FromResult<UsuarioServicioLectura?>(null);
    }
}

/// <summary>
/// PLATFORM-TODO(&lt;ComprasCrearRqSistemaAdapter&gt;): adapter real en
/// <c>Compras.Infrastructure.PublicAdapters</c> que crea la RQ de sistema (ADR-0047
/// PR5.C). Reemplazado en <c>Program.cs</c>. A diferencia de los NoOp de lectura, este
/// es de ESCRITURA crítica: falla ruidoso si se resuelve sin el adapter real (evita
/// no-ops silenciosos de una creación).
/// </summary>
public sealed class NoOpComprasCrearRqSistemaPort : IComprasCrearRqSistemaPort
{
    public Task<Guid> CrearBorradorSistemaAsync(
        CrearRqSistemaSolicitud solicitud, CancellationToken cancellationToken)
        => throw new InvalidOperationException(
            "IComprasCrearRqSistemaPort no tiene adapter real cableado (stub NoOp). " +
            "Wirea ComprasCrearRqSistemaAdapter en Program.cs (ADR-0047 PR5.C).");
}

/// <summary>
/// PLATFORM-TODO(&lt;ArticuloReadAdapter&gt;): adapter real consultando
/// <c>compartido.articulos</c> (catálogo cross-empresa de DatosMaestros).
/// Reemplaza cuando entre F1-PR3 (seed de catálogos para Almacén).
/// </summary>
public sealed class NoOpArticuloReadPort : IArticuloReadPort
{
    private readonly ILogger<NoOpArticuloReadPort> _logger;

    public NoOpArticuloReadPort(ILogger<NoOpArticuloReadPort> logger)
    {
        _logger = logger;
    }

    public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpArticuloReadPort] articulo={ArticuloId} → null (stub F0-PR1)", articuloId);
        return Task.FromResult<ArticuloLectura?>(null);
    }

    public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
        IReadOnlyCollection<Guid> articuloIds, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpArticuloReadPort] porIds({Count}) → vacío (stub F0-PR1)", articuloIds.Count);
        return Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(
            new Dictionary<Guid, ArticuloLectura>());
    }
}

/// <summary>
/// PLATFORM-TODO(&lt;ProveedorReadAdapter&gt;): adapter real consultando
/// <c>compartido.proveedores</c>. Reemplaza cuando entre F6-PR1
/// (devolución a proveedor).
/// </summary>
public sealed class NoOpProveedorReadPort : IProveedorReadPort
{
    private readonly ILogger<NoOpProveedorReadPort> _logger;

    public NoOpProveedorReadPort(ILogger<NoOpProveedorReadPort> logger)
    {
        _logger = logger;
    }

    public Task<ProveedorLectura?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpProveedorReadPort] proveedor={ProveedorId} → null (stub F0-PR1)", proveedorId);
        return Task.FromResult<ProveedorLectura?>(null);
    }

    public Task<IReadOnlyDictionary<Guid, ProveedorLectura>> ObtenerPorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpProveedorReadPort] porIds({Count}) → vacío (stub F0-PR1)", proveedorIds.Count);
        return Task.FromResult<IReadOnlyDictionary<Guid, ProveedorLectura>>(
            new Dictionary<Guid, ProveedorLectura>());
    }
}

/// <summary>
/// Stub por defecto del módulo para <see cref="ICxpDocumentosReadPort"/>.
/// El adapter real (<c>CxpDocumentosReadAdapter</c>, hosteado en
/// CuentasPorPagar.Infrastructure.PublicAdapters) se wirea en
/// <c>Program.cs</c> desde el día 1 — este NoOp solo cubre hosts que
/// registren Almacén sin CxP (tests de módulo aislado).
/// </summary>
public sealed class NoOpCxpDocumentosReadPort : ICxpDocumentosReadPort
{
    private static readonly IReadOnlyDictionary<Guid, string> Empty =
        new Dictionary<Guid, string>();

    private readonly ILogger<NoOpCxpDocumentosReadPort> _logger;

    public NoOpCxpDocumentosReadPort(ILogger<NoOpCxpDocumentosReadPort> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosFacturaAsync(
        IReadOnlyCollection<Guid> facturaIds, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpCxpDocumentosReadPort] foliosFactura({Count}) → vacío", facturaIds.Count);
        return Task.FromResult(Empty);
    }

    public Task<IReadOnlyDictionary<Guid, string>> ObtenerUuidsFiscalesCfdiAsync(
        IReadOnlyCollection<Guid> cfdiRecibidoIds, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpCxpDocumentosReadPort] uuidsCfdi({Count}) → vacío", cfdiRecibidoIds.Count);
        return Task.FromResult(Empty);
    }

    public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosNotaCreditoAsync(
        IReadOnlyCollection<Guid> notaCreditoIds, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpCxpDocumentosReadPort] foliosNc({Count}) → vacío", notaCreditoIds.Count);
        return Task.FromResult(Empty);
    }
}

/// <summary>
/// PLATFORM-TODO(&lt;SucursalReadAdapter&gt;): adapter real consultando
/// <c>compartido.sucursales</c> (módulo Administración). Reemplaza cuando
/// entre F1-PR1 (re-localización del catálogo de almacenes con la
/// jerarquía Sucursal → Almacén → Sub-almacén).
/// </summary>
public sealed class NoOpSucursalReadPort : ISucursalReadPort
{
    private readonly ILogger<NoOpSucursalReadPort> _logger;

    public NoOpSucursalReadPort(ILogger<NoOpSucursalReadPort> logger)
    {
        _logger = logger;
    }

    public Task<SucursalLectura?> ObtenerAsync(Guid sucursalId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpSucursalReadPort] sucursal={SucursalId} → null (stub F0-PR1)", sucursalId);
        return Task.FromResult<SucursalLectura?>(null);
    }
}

// NoOpEmpleadoReadPort retirado en ADM-PR2: el adapter real vive en
// Compartido.Infrastructure.PublicAdapters.EmpleadoReadAdapter y se
// registra en Program.cs (doc 10-catalogo-puestos-empleados).

/// <summary>
/// PLATFORM-TODO(&lt;TipoCambioReadAdapter&gt;): adapter real consultando
/// <c>catalogos.tipos_cambio</c>. Reemplaza cuando entre F2-PR2
/// (recepción con multimoneda real). Por ahora devuelve <c>1.0</c> para
/// MXN y <c>null</c> para cualquier otra moneda — fuerza al handler a
/// rechazar movimientos en moneda extranjera hasta que el adapter exista.
/// </summary>
public sealed class NoOpTipoCambioReadPort : ITipoCambioReadPort
{
    private readonly ILogger<NoOpTipoCambioReadPort> _logger;

    public NoOpTipoCambioReadPort(ILogger<NoOpTipoCambioReadPort> logger)
    {
        _logger = logger;
    }

    public Task<decimal?> ObtenerTipoCambioMxnAsync(
        string monedaOrigen,
        DateOnly fecha,
        CancellationToken cancellationToken)
    {
        if (string.Equals(monedaOrigen, "MXN", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<decimal?>(1.0m);
        }

        _logger.LogDebug(
            "[NoOpTipoCambioReadPort] moneda={Moneda} fecha={Fecha} → null (stub F0-PR1)",
            monedaOrigen, fecha);
        return Task.FromResult<decimal?>(null);
    }
}

/// <summary>
/// PLATFORM-TODO(&lt;ConceptoContableReadAdapter&gt;): adapter real
/// cuando exista el módulo Contabilidad. Reemplaza este stub al cablear
/// la integración con la póliza generada por
/// <c>EntradaInventarioValoradaEvent</c>.
/// </summary>
public sealed class NoOpConceptoContableReadPort : IConceptoContableReadPort
{
    private readonly ILogger<NoOpConceptoContableReadPort> _logger;

    public NoOpConceptoContableReadPort(ILogger<NoOpConceptoContableReadPort> logger)
    {
        _logger = logger;
    }

    public Task<ConceptoContableLectura?> ObtenerAsync(
        string codigoConcepto,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "[NoOpConceptoContableReadPort] concepto={Codigo} → null (stub F0-PR1)",
            codigoConcepto);
        return Task.FromResult<ConceptoContableLectura?>(null);
    }
}

/// <summary>
/// PLATFORM-TODO(&lt;PeriodoContableReadAdapter&gt;): adapter real cuando
/// exista el módulo Finanzas con calendario fiscal central. Reemplaza
/// este stub al cablear el wiring real.
///
/// <para>
/// Comportamiento del stub: siempre retorna <c>true</c> (periodo abierto).
/// Esto preserva la operación durante el desarrollo y permite que
/// <c>almacen.periodos_cerrados</c> (F8-PR2) sea la fuente de verdad
/// local hasta que Finanzas exista.
/// </para>
/// </summary>
public sealed class NoOpPeriodoContableReadPort : IPeriodoContableReadPort
{
    private readonly ILogger<NoOpPeriodoContableReadPort> _logger;

    public NoOpPeriodoContableReadPort(ILogger<NoOpPeriodoContableReadPort> logger)
    {
        _logger = logger;
    }

    public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "[NoOpPeriodoContableReadPort] {Anio}/{Mes:00} → abierto (stub F0-PR1)",
            año, mes);
        return Task.FromResult(true);
    }
}

/// <summary>
/// PLATFORM-TODO(&lt;UsuarioReadAdapter&gt;): adapter real consultando
/// <c>identidad.usuarios</c> en <c>IdentidadDbContext</c>. El adapter real vive
/// en <c>Identidad.Infrastructure.PublicAdapters</c> (el owner, porque Almacén
/// no puede referenciar Identidad sin cerrar ciclo) y se cablea en
/// <c>Program.cs</c>. Reemplaza este stub para resolver el nombre de la persona
/// destinataria de una salida (ADR-0042).
/// </summary>
public sealed class NoOpUsuarioReadPort : IUsuarioReadPort
{
    private readonly ILogger<NoOpUsuarioReadPort> _logger;

    public NoOpUsuarioReadPort(ILogger<NoOpUsuarioReadPort> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(
        IReadOnlyCollection<Guid> usuarioIds, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpUsuarioReadPort] nombres({Count}) → vacío (stub F0-PR1)", usuarioIds.Count);
        return Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
}

/// <summary>
/// PLATFORM-TODO(&lt;AlmacenCentroCostoReadAdapter&gt;): adapter real que delega
/// en <c>CentrosCosto.Application.PublicPorts.IDim3ReadPort</c>. Almacén no
/// referencia CentrosCosto (cerraría ciclo vía Compartido), así que el adapter
/// real vive en <c>Compras.Infrastructure.PublicAdapters</c> (Fase E PR5) y se
/// cablea en <c>Program.cs</c>. Reemplaza este stub para resolver el nombre del
/// CC-Máquina de una línea de salida (ADR-0050 §3).
/// </summary>
public sealed class NoOpCentroCostoReadPort : ICentroCostoReadPort
{
    private readonly ILogger<NoOpCentroCostoReadPort> _logger;

    public NoOpCentroCostoReadPort(ILogger<NoOpCentroCostoReadPort> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyDictionary<Guid, Dim3Lectura>> ObtenerAsync(
        IReadOnlyCollection<Guid> dim3Ids, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpCentroCostoReadPort] dim3({Count}) → vacío (stub PR5)", dim3Ids.Count);
        return Task.FromResult<IReadOnlyDictionary<Guid, Dim3Lectura>>(new Dictionary<Guid, Dim3Lectura>());
    }
}
