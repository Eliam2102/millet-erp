using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Application.Escaneos;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.ProductoTerminado.UnitTests.Application;

internal sealed class FakePedidosPort : IPedidosPtReadPort
{
    public Dictionary<string, PedidoPtOrigen> Pedidos { get; } = new();
    public Task<PedidoPtOrigen?> ObtenerPorNumeroAsync(Guid empresaId, string numeroPedido, CancellationToken ct) =>
        Task.FromResult(Pedidos.GetValueOrDefault(numeroPedido));
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
    public RegistrarEscaneoPiezaHandler Handler { get; }

    public EntornoPt()
    {
        Db = new ProductoTerminadoDbContext(
            new DbContextOptionsBuilder<ProductoTerminadoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaFija(Empresa));
        Sucursales.Claves["01"] = Sucursal;
        var reloj = new RelojFijo(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        Handler = new RegistrarEscaneoPiezaHandler(Db, new EscaneoProcesador(Db, Pedidos, Sucursales, Eventos, reloj), reloj);
    }

    public void AgregarPedido(string numero, int posicion, decimal cantidad, string estado = "Abierto") =>
        Pedidos.Pedidos[numero] = new PedidoPtOrigen(Guid.NewGuid(), numero, Sucursal, estado, null, 1,
            [new LineaPtOrigen(posicion, null, "Vidrio", cantidad, null)]);

    public Task<EscaneoResultadoDto> Escanear(string etiqueta, string pedido = "P1", int? pos = 1, string? pieza = "1", DateTime? at = null, string? clave = "01") =>
        Handler.Handle(new RegistrarEscaneoPiezaCommand(Empresa, "Demo", etiqueta, pedido, pos, null, pieza, 2821, null,
            at ?? new DateTime(2026, 10, 28, 8, 0, 0, DateTimeKind.Unspecified), clave), CancellationToken.None);

    public void Dispose() => Db.Dispose();
}
