using MediatR;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.UnitTests.TestDoubles;

/// <summary>Reloj fijo para tests deterministas.</summary>
public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; } = now;
}

/// <summary>Contexto de empresa fijo. <see cref="Bypass"/> es no-op.</summary>
public sealed class FakeEmpresaContext(Guid? current, bool isBypassed = false) : ICurrentEmpresaContext
{
    private sealed class NoopScope : IDisposable { public void Dispose() { } }

    public Guid? Current { get; } = current;
    public bool IsBypassed { get; } = isBypassed;
    public IDisposable Bypass() => new NoopScope();
}

/// <summary>Usuario fijo para auditoría/emisor.</summary>
public sealed class FakeUserContext(Guid? userId, string? userName = "test") : ICurrentUserContext
{
    public Guid? UserId { get; } = userId;
    public string? UserName { get; } = userName;
}

/// <summary>Candado de período configurable.</summary>
public sealed class FakePeriodoContablePort(bool abierto = true) : IPeriodoContablePort
{
    public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken) =>
        Task.FromResult(abierto);
}

/// <summary>Catálogos SAT configurable (todo válido por default).</summary>
public sealed class FakeCatalogosSatReadPort(bool todoValido = true) : ICatalogosSatReadPort
{
    public Task<bool> ExisteFormaPagoAsync(string claveSat, CancellationToken cancellationToken) => Task.FromResult(todoValido);
    public Task<bool> ExisteUsoCfdiAsync(string claveSat, CancellationToken cancellationToken) => Task.FromResult(todoValido);
    public Task<bool> ExisteRegimenFiscalAsync(string codigo, CancellationToken cancellationToken) => Task.FromResult(todoValido);
    public Task<bool> ExisteMonedaAsync(string codigo, CancellationToken cancellationToken) => Task.FromResult(todoValido);
}

/// <summary>Stub del PAC que devuelve un timbre fake (camino feliz por default). Captura la última emisión para asserts.</summary>
public sealed class FakeFiscalApiClient(string uuid = "11111111-1111-1111-1111-111111111111") : ICfdiTimbradoPort
{
    public CfdiEmision? UltimaEmision { get; private set; }

    public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken cancellationToken)
    {
        UltimaEmision = emision;
        return Task.FromResult(new TimbradoResultado(
            TimbradoEstado.Timbrado, uuid, "SELLO_CFDI", "SELLO_SAT", "00000000000000000000",
            new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.Zero), "SAT970701NN3",
            $"<cfdi serie=\"{emision.Serie}\" folio=\"{emision.Folio}\" uuid=\"{uuid}\" fake=\"true\" />", null, null));
    }

    public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud solicitud, CancellationToken cancellationToken) =>
        Task.FromResult(new CancelacionResultado(CancelacionEstado.Aceptada, "Cancelado", null));

    public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud solicitud, CancellationToken cancellationToken) =>
        Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
}

/// <summary>Repo de CFDI en memoria para tests.</summary>
public sealed class FakeCfdiRepositorioPort : ICfdiRepositorioPort
{
    public Guid? UltimoGuardadoId { get; private set; }

    public Task<Guid> GuardarAsync(CfdiArchivoNuevo archivo, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        UltimoGuardadoId = id;
        return Task.FromResult(id);
    }

    public Task<CfdiArchivoLeido?> ObtenerAsync(Guid cfdiArchivoId, CancellationToken cancellationToken) =>
        Task.FromResult<CfdiArchivoLeido?>(null);

    public Task<CfdiArchivoLeido?> ObtenerPorUuidAsync(string uuid, CancellationToken cancellationToken) =>
        Task.FromResult<CfdiArchivoLeido?>(null);
}

/// <summary>
/// <see cref="ISender"/> mínimo: responde sólo a <see cref="ReservarFolioCommand"/>
/// con una respuesta canned (la reserva real usa raw SQL que el provider InMemory
/// no soporta, por eso se mockea). El resto lanza.
/// </summary>
public sealed class FakeSender(ReservarFolioResponse reservaResponse) : ISender
{
    public int VecesReservoFolio { get; private set; }

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is ReservarFolioCommand)
        {
            VecesReservoFolio++;
            return Task.FromResult((TResponse)(object)reservaResponse);
        }

        throw new NotImplementedException($"FakeSender no maneja {request.GetType().Name}.");
    }

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
        throw new NotImplementedException();

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}

/// <summary>Cliente fiscal configurable (null = no existe en el master).</summary>
public sealed class FakeClientesReadPort(
    ClienteFiscalLectura? cliente = null,
    IReadOnlyList<ClienteBusquedaItem>? busqueda = null) : IClientesReadPort
{
    public Task<ClienteFiscalLectura?> ObtenerAsync(Guid clienteId, CancellationToken cancellationToken) => Task.FromResult(cliente);
    public Task<ClienteFiscalLectura?> ResolverPorReferenciaAsync(string referenciaExterna, CancellationToken cancellationToken) => Task.FromResult(cliente);
    public Task<IReadOnlyList<ClienteBusquedaItem>> BuscarAsync(string? rfc, string? razonSocial, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(busqueda ?? (IReadOnlyList<ClienteBusquedaItem>)[]);
}

/// <summary>Producto fiscal configurable (null por default).</summary>
public sealed class FakeProductosReadPort(
    ProductoFiscalLectura? producto = null,
    IReadOnlyList<ProductoAwBusquedaItem>? busqueda = null) : IProductosReadPort
{
    public Task<ProductoFiscalLectura?> ObtenerAsync(Guid productoId, CancellationToken cancellationToken) => Task.FromResult(producto);
    public Task<ProductoFiscalLectura?> ResolverPorReferenciaAsync(string referenciaExterna, CancellationToken cancellationToken) => Task.FromResult(producto);
    public Task<IReadOnlyList<ProductoAwBusquedaItem>> BuscarAsync(string? referencia, string? descripcion, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(busqueda ?? (IReadOnlyList<ProductoAwBusquedaItem>)[]);
}

/// <summary>
/// Catálogo de canales de venta configurable (FAC-ING-PR2): todo id existe y
/// está activo por default; <c>nombres</c> permite fijar el nombre por id
/// (default "Canal N" para asserts de display).
/// </summary>
public sealed class FakeCanalesVentaReadPort(
    bool todoActivo = true,
    IReadOnlyDictionary<short, string>? nombres = null) : ICanalesVentaReadPort
{
    public Task<bool> ExisteActivoAsync(short canalVentaId, CancellationToken cancellationToken) =>
        Task.FromResult(todoActivo);

    public Task<string?> ObtenerNombreAsync(short canalVentaId, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(
            nombres is not null && nombres.TryGetValue(canalVentaId, out var nombre)
                ? nombre
                : $"Canal {canalVentaId}");

    public Task<IReadOnlyList<CanalVentaLectura>> ListarActivosAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CanalVentaLectura>>(
            (nombres ?? new Dictionary<short, string>())
                .OrderBy(kv => kv.Key)
                .Select(kv => new CanalVentaLectura(kv.Key, kv.Value))
                .ToList());
}

/// <summary>
/// Catálogo de sucursales configurable (CAJAS-PR1): por default todo id es
/// activo; <c>noActivas</c> fija los ids que el port reporta como inválidos.
/// </summary>
public sealed class FakeSucursalesReadPort(
    IReadOnlyCollection<Guid>? noActivas = null,
    string? zonaHoraria = "America/Merida") : ISucursalesReadPort
{
    public Task<IReadOnlyList<Guid>> FiltrarNoActivasAsync(
        IReadOnlyCollection<Guid> sucursalIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(
            noActivas is null ? [] : sucursalIds.Intersect(noActivas).ToList());

    public Task<string?> ObtenerZonaHorariaAsync(Guid sucursalId, CancellationToken cancellationToken) =>
        Task.FromResult(zonaHoraria);
}

/// <summary>Datos fiscales del emisor configurables (null = empresa inexistente).</summary>
public sealed class FakeEmpresaFiscalReadPort(
    EmpresaFiscalLectura? emisor = null,
    Guid? sucursalUnica = null) : IEmpresaFiscalReadPort
{
    public Task<EmpresaFiscalLectura?> ObtenerAsync(Guid empresaId, CancellationToken cancellationToken) => Task.FromResult(emisor);
    public Task<Guid?> ObtenerSucursalUnicaActivaAsync(CancellationToken cancellationToken) => Task.FromResult(sucursalUnica);
}

/// <summary>Reader A+W con datos canned (cola + datos del pedido). Con
/// <paramref name="lectura"/> explícita se simulan las salidas sin datos
/// (config-fixable vs datos inválidos, FAC-ING-PR3).</summary>
public sealed class FakeAwSolicitudesReader(
    DatosPedidoAw? datos = null, IReadOnlyList<SolicitudAw>? pendientes = null, LecturaPedidoAw? lectura = null) : IAwSolicitudesReader
{
    public Task<IReadOnlyList<SolicitudAw>> LeerPendientesAsync(int max, CancellationToken cancellationToken) =>
        Task.FromResult(pendientes ?? (IReadOnlyList<SolicitudAw>)[]);

    public Task<LecturaPedidoAw> LeerDatosPedidoAsync(string numeroPedido, CancellationToken cancellationToken) =>
        Task.FromResult(lectura ?? (datos is null
            ? LecturaPedidoAw.DatosInvalidos(Millet.Facturacion.Domain.Ingesta.MotivoExcepcion.Otro, "sin datos (fake)")
            : LecturaPedidoAw.Ok(datos)));
}

/// <summary>Write-back A+W que captura el último resultado escrito.</summary>
public sealed class FakeAwWriteBackPort : IAwWriteBackPort
{
    public AwWriteBack? Ultimo { get; private set; }

    public Task EscribirResultadoAsync(AwWriteBack writeBack, CancellationToken cancellationToken)
    {
        Ultimo = writeBack;
        return Task.CompletedTask;
    }
}

/// <summary>Auto-provisión de master configurable (null = no provisiona → excepción).</summary>
public sealed class FakeMasterProvisioningPort(ClienteFiscalLectura? cliente = null, ProductoFiscalLectura? producto = null) : IMasterProvisioningPort
{
    public Task<ClienteFiscalLectura?> EnsureClienteDesdeAwAsync(string clienteRef, CancellationToken cancellationToken) => Task.FromResult(cliente);
    public Task<ProductoFiscalLectura?> EnsureArticuloDesdeAwAsync(string articuloRef, CancellationToken cancellationToken) => Task.FromResult(producto);
}

/// <summary>Publisher de eventos de integración que captura lo publicado (F10-PR1).</summary>
public sealed class FakeIntegrationEventPublisher : IIntegrationEventPublisher
{
    public List<object> Publicados { get; } = [];

    public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
    {
        Publicados.Add(integrationEvent);
        return Task.CompletedTask;
    }
}

/// <summary>Puerto de asientos contables NoOp para tests (F10-PR1).</summary>
public sealed class FakeContabilidadAsientoPort : IContabilidadAsientoPort
{
    public int Veces { get; private set; }

    public Task RegistrarAsientoAsync(AsientoContableSolicitud solicitud, CancellationToken cancellationToken)
    {
        Veces++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Evaluador de alcance de Cajas configurable (CAJAS-PR2). Default = alcance
/// Total (comportamiento pre-Capa A) y bucket "Sin asignar" sobre cero cajas
/// activas (todo documento es sin-asignar).
/// </summary>
public sealed class FakeAlcanceCajaEvaluator(
    Millet.Facturacion.Application.Cajas.Alcance.AlcanceCajas? alcance = null,
    IReadOnlyList<Millet.Facturacion.Application.Cajas.Alcance.CombinacionAlcance>? combinacionesCajasActivas = null)
    : Millet.Facturacion.Application.Cajas.Alcance.IAlcanceCajaEvaluator
{
    public Task<Millet.Facturacion.Application.Cajas.Alcance.AlcanceCajas> ResolverAsync(CancellationToken cancellationToken) =>
        Task.FromResult(alcance ?? Millet.Facturacion.Application.Cajas.Alcance.AlcanceCajas.Total());

    public Task<Millet.Facturacion.Application.Cajas.Alcance.AlcanceSinAsignar> ResolverSinAsignarAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new Millet.Facturacion.Application.Cajas.Alcance.AlcanceSinAsignar(combinacionesCajasActivas ?? []));
}

/// <summary>Permisos efectivos configurables (CAJAS-PR2 / ICurrentUserPermissions).</summary>
public sealed class FakeCurrentUserPermissions(params string[] permisos) : ICurrentUserPermissions
{
    public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(permisos.Contains(permiso, StringComparer.Ordinal));
}
