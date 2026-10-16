using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.ComercioExterior.Infrastructure.PublicAdapters;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Api.UnitTests;
public sealed class CeEmbarqueSucursalTests
{
    [Fact]
    public async Task Puerto_consumido_por_facturacion_no_exige_permisos_ni_alcance_de_ce_y_bloquea_incoterm_faltante()
    {
        var empresa = new FakeEmpresaContext(Guid.NewGuid());
        using var db = new ComercioExteriorDbContext(new DbContextOptionsBuilder<ComercioExteriorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var actor = Guid.NewGuid(); var sucursal = Guid.NewGuid(); var cliente = Guid.NewGuid();
        var e = new Expediente(Guid.NewGuid(), empresa.Current!.Value, "CE-EXP-DEMO", TipoExpediente.Exportacion,
            sucursal, "DEMO", null, Guid.NewGuid(), "USD", actor, ["DEMO cliente"]);
        e.AgregarParticipante(Guid.NewGuid(), "DEMO cliente", TipoParticipante.Cliente, null, PapelParticipante.Captura, null, cliente);
        var contenedor = Guid.NewGuid(); e.GuardarContenedor(contenedor, new("DEMO-C", null, null, "DEMO-BL", null, null, null, null));
        var pedido = new PedidoEmbarcable(Guid.NewGuid(), "DEMO-PED", cliente, "DEMO cliente", sucursal, true,
            "Importado", 1, [new(1, null, "DEMO vidrio", 60, "H87")], null, null);
        e.AsignarPartida(contenedor, pedido, 1, 60, actor); db.Expedientes.Add(e); await db.SaveChangesAsync();
        var adapter = new EmbarqueExportacionReadAdapter(db, new Pedidos(pedido), new Catalogos());
        var lectura = await adapter.ObtenerAsync(pedido.Id, CancellationToken.None);
        Assert.True(lectura.Ligado);
        Assert.False(lectura.ListoParaFacturar);
        Assert.Equal("CE_INCOTERM_REQUERIDO", lectura.CodigoBloqueo);
        e.Cancelar("DEMO cancelación"); await db.SaveChangesAsync();
        Assert.False((await adapter.ObtenerAsync(pedido.Id, CancellationToken.None)).Ligado);
    }
    [Fact]
    public async Task Pedido_no_ligado_no_exige_acceso_a_una_sucursal_ce()
    {
        using var db = new ComercioExteriorDbContext(new DbContextOptionsBuilder<ComercioExteriorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(Guid.NewGuid()));
        var adapter = new EmbarqueExportacionReadAdapter(db, null!, null!);
        Assert.False((await adapter.ObtenerAsync(Guid.NewGuid(), CancellationToken.None)).Ligado);
    }
    private sealed class Pedidos(PedidoEmbarcable pedido) : IPedidosEmbarcablesReadPort
    {
        public Task<IReadOnlyList<PedidoEmbarcable>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<PedidoEmbarcable>>([pedido]);
        public Task<IReadOnlyList<PedidoEmbarcable>> ListarAsync(Guid cliente, Guid sucursal, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Catalogos : ICatalogosCeReadPort
    {
        public Task<IReadOnlyDictionary<Guid, string>> IncotermsAsync(IEnumerable<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<IReadOnlyList<Guid>> AdjuntosVigentesAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<IReadOnlyList<ReferenciaCe>> ReferenciasAsync(string tipo, string q, CancellationToken ct) => throw new NotSupportedException();
        public Task<Guid> IncotermDdpAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task ValidarAsync(Guid incoterm, string moneda, Guid sucursal, Guid responsable, CancellationToken ct) => throw new NotSupportedException();
        public Task ValidarResponsableAsync(Guid sucursal, Guid responsable, CancellationToken ct) => throw new NotSupportedException();
        public Task ValidarParticipanteAsync(Guid? proveedor, Guid? cliente, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<RequisitoDocumento>> RequisitosAsync(TipoExpediente tipo, CancellationToken ct) => throw new NotSupportedException();
        public Task ValidarAdjuntoAsync(Guid expediente, Guid tipo, Guid adjunto, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> NombresAsync(IEnumerable<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> SucursalesAsync(IEnumerable<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
    }
}
