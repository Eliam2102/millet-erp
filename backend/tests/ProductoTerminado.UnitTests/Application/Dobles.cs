using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Escaneos;
using Millet.ProductoTerminado.Application.Ports;
using Millet.Administracion.Application.Abstractions;
using Millet.ProductoTerminado.Application.Salidas;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.UnitTests.Application;

/// <summary>
/// Reglas de salida del 9-nov en las pruebas que no tratan de ellas: antes de emitir, otra persona autoriza la excepción sin factura
/// (Administración) y la parcialidad (Crédito); lo que no hace falta autorizar se ignora. Las reglas mismas están en ReglasSalidaTests.
/// </summary>
internal sealed class EmisorAutorizado(ProductoTerminadoDbContext db, IPedidosPtReadPort pedidos, IFacturacionSalidaPort facturacion, EmitirHojaSalidaHandler emitir, IClock reloj)
{
    public static readonly Guid AprobadorId = Guid.Parse("00000000-0000-0000-0000-0000000000dd");

    public async Task<HojaSalidaDto> Handle(EmitirHojaSalidaCommand c, CancellationToken ct)
    {
        var aprobar = new AutorizarHojaSalidaHandler(db, new PtAcceso(new UsuarioFijo(AprobadorId), new PermisosTodos(), new SucursalesTodas()), pedidos, facturacion,
            new EmpresaFija(EntornoPt.Empresa), reloj);
        foreach (var nivel in new[] { "administracion", "credito" })
        {
            try { await aprobar.Handle(new AutorizarHojaSalidaCommand(c.Id, nivel, "Autorizada en la prueba", null), ct); }
            catch (BusinessRuleException) { }
            catch (EntityNotFoundException) { }
        }
        return await emitir.Handle(c, ct);
    }
}

internal sealed class UsuarioFijo(Guid id) : ICurrentUserContext
{
    public Guid? UserId => id;
    public string? UserName => null;
}

internal sealed class PermisosTodos : ICurrentUserPermissions
{
    public ValueTask<bool> TieneAsync(string permiso, CancellationToken ct = default) => ValueTask.FromResult(true);
}

internal sealed class SucursalesTodas : IUsuarioSucursalReadPort
{
    public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid usuarioId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([EntornoPt.Sucursal]);
    public Task<bool> EstaAsociadoAsync(Guid usuarioId, Guid sucursalId, CancellationToken ct) => Task.FromResult(true);
}

/// <summary>Sin festivos; la sucursal de prueba en Mérida salvo que se indique otra zona.</summary>
internal sealed class CalendarioFalso : IPtCalendarioSucursalesPort
{
    public Dictionary<Guid, SucursalCalendarioPt> Sucursales { get; } = new() { [EntornoPt.Sucursal] = new("CIR", "America/Merida") };
    public Task<IReadOnlyDictionary<Guid, SucursalCalendarioPt>> SucursalesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, SucursalCalendarioPt>>(Sucursales.Where(s => ids.Contains(s.Key)).ToDictionary());
    public Task<IReadOnlySet<DateOnly>> FestivosAsync(CancellationToken ct) => Task.FromResult<IReadOnlySet<DateOnly>>(new HashSet<DateOnly>());
}

internal sealed class FakePedidosPort : IPedidosPtReadPort
{
    public Dictionary<string, PedidoPtOrigen> Pedidos { get; } = new();
    public Task<PedidoPtOrigen?> ObtenerPorNumeroAsync(Guid empresaId, string numeroPedido, CancellationToken ct) =>
        Task.FromResult(Pedidos.GetValueOrDefault(numeroPedido));
    public Dictionary<Guid, Guid> Facturas { get; } = new();
    public Task<Guid?> ObtenerPedidoDeFacturaAsync(Guid empresaId, Guid comprobanteId, CancellationToken ct) =>
        Task.FromResult(Facturas.TryGetValue(comprobanteId, out var p) ? (Guid?)p : null);
    public Dictionary<Guid, PedidoMonitorOrigen> Monitor { get; } = new();
    public Task<IReadOnlyDictionary<Guid, PedidoMonitorOrigen>> ObtenerParaMonitorAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, PedidoMonitorOrigen>>(Monitor.Where(k => ids.Contains(k.Key)).ToDictionary(k => k.Key, k => k.Value));
}

internal sealed class FakeSucursalPort : IPtSucursalPorClaveAwPort
{
    public Dictionary<string, Guid> Claves { get; } = new();
    public Task<Guid?> ResolverAsync(string claveAw, CancellationToken ct) =>
        Task.FromResult(Claves.TryGetValue(claveAw, out var id) ? (Guid?)id : null);
}

internal sealed class CapturaEventos : IIntegrationEventPublisher
{
    public List<object> Eventos { get; } = [];
    public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
    {
        Eventos.Add(integrationEvent);
        return Task.CompletedTask;
    }
}

internal sealed class RelojFijo(DateTimeOffset ahora) : IClock
{
    public DateTimeOffset UtcNow => ahora;
}

internal sealed class EmpresaFija(Guid id) : ICurrentEmpresaContext
{
    public Guid? Current => id;
    public bool IsBypassed => false;
    public IDisposable Bypass() => throw new NotSupportedException();
}

/// <summary>Entorno de prueba: DbContext InMemory + fakes + handler listo.</summary>
internal sealed class EntornoPt : IDisposable
{
    public static readonly Guid Empresa = Guid.Parse("00000000-0000-0000-0000-0000000000aa");
    public static readonly Guid Sucursal = Guid.Parse("00000000-0000-0000-0000-0000000000bb");
    public ProductoTerminadoDbContext Db { get; }
    public FakePedidosPort Pedidos { get; } = new();
    public FakeSucursalPort Sucursales { get; } = new();
    public CapturaEventos Eventos { get; } = new();
    public FakeSaldoPort Saldos { get; } = new();
    public LiberacionPedido Liberacion { get; }
    public RegistrarEscaneoPiezaHandler Handler { get; }

    public EntornoPt()
    {
        Db = new ProductoTerminadoDbContext(
            new DbContextOptionsBuilder<ProductoTerminadoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaFija(Empresa));
        Sucursales.Claves["01"] = Sucursal;
        var reloj = new RelojFijo(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        Liberacion = new LiberacionPedido(Db, Saldos, Eventos, reloj);
        Handler = new RegistrarEscaneoPiezaHandler(Db, new EscaneoProcesador(Db, Pedidos, Sucursales, Eventos, reloj, Liberacion), reloj);
    }

    public void AgregarPedido(string numero, int posicion, decimal cantidad, string estado = "Abierto", string? condicionPago = null) =>
        Pedidos.Pedidos[numero] = new PedidoPtOrigen(Guid.NewGuid(), numero, Sucursal, estado, condicionPago, 1,
            [new LineaPtOrigen(posicion, null, "Vidrio", cantidad, null)]);

    public Task<EscaneoResultadoDto> Escanear(string etiqueta, string pedido = "P1", int? pos = 1, string? pieza = "1", DateTime? at = null, string? clave = "01") =>
        Handler.Handle(new RegistrarEscaneoPiezaCommand(Empresa, "Demo", etiqueta, pedido, pos, null, pieza, 2821, null,
            at ?? new DateTime(2026, 10, 28, 8, 0, 0, DateTimeKind.Unspecified), clave), CancellationToken.None);

    public void Dispose() => Db.Dispose();
}

/// <summary>Saldo por pedido de prueba; sin dato = no se conoce (no cubierto), como el adaptador Real.</summary>
internal sealed class FakeSaldoPort : ISaldoPedidoReadPort
{
    public Dictionary<string, decimal> Saldos { get; } = new();
    public Task<decimal?> ObtenerAsync(Guid empresaId, string numeroPedido, CancellationToken ct) =>
        Task.FromResult(Saldos.TryGetValue(numeroPedido, out var s) ? (decimal?)s : null);
}
