using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Anticipos.Queries;
using Millet.Facturacion.Application.CartaPorte.Queries;
using Millet.Facturacion.Application.Facturas.Queries;
using Millet.Facturacion.Application.Ingesta.Queries;
using Millet.Facturacion.Application.NotasCredito.Queries;
using Millet.Facturacion.Application.Repp.Queries;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using DominioCp = Millet.Facturacion.Domain.CartaPorte;

namespace Millet.Facturacion.UnitTests.Lectura;

/// <summary>
/// 86e401dz2 — las bandejas de Facturación paginan en el servidor: con 3 filas,
/// <c>offset 1 / limit 1</c> devuelve una fila y el total real (3). La búsqueda
/// <c>q</c> usa ILIKE, que InMemory no soporta: la cubre el CI de integración.
/// </summary>
public sealed class BandejasPaginacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DatosFiscalesEmisor Emisor = new("BBB010101BBB", "Millet", "601", "76120");

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static DatosFiscalesReceptor Receptor() => new("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);

    [Fact]
    public async Task Facturas()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        for (var i = 1; i <= 3; i++)
            db.FacturasVenta.Add(FacturaVenta.CrearBorrador(empresaId, $"F-{i}", i, Guid.NewGuid(), null, null, Receptor(),
                Emisor, "PPD", "99", "MXN", null, 2026, 5, (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false));
        await db.SaveChangesAsync();

        var r = await new BandejaFacturasHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new BandejaFacturasQuery(null, 1, 1), CancellationToken.None);

        r.Items.Should().ContainSingle().Which.Folio.Should().Be("F-2");
        r.Total.Should().Be(3);
    }

    [Fact]
    public async Task FacturasAnticipo()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        for (var i = 1; i <= 3; i++)
            db.FacturasAnticipo.Add(FacturaAnticipo.CrearBorrador(empresaId, $"FANT-{i}", i, Guid.NewGuid(), null, null, Receptor(),
                Emisor, "03", "MXN", null, 2026, 5, TipoAnticipo.ClientesMxp, null, Guid.CreateVersion7(), 1000m, 0.16m, canalVentaId: 2));
        await db.SaveChangesAsync();

        var r = await new BandejaFacturasAnticipoHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new BandejaFacturasAnticipoQuery(null, 1, 1), CancellationToken.None);

        r.Items.Should().ContainSingle();
        r.Total.Should().Be(3);
    }

    [Fact]
    public async Task Repp()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        for (var i = 1; i <= 3; i++)
            db.RecibosPago.Add(ReciboPago.CrearBorrador(empresaId, $"P-{i}", i, Guid.NewGuid(), null, null, Receptor(),
                Emisor, 2026, 5, Ahora, "MXN"));
        await db.SaveChangesAsync();

        var r = await new BandejaReppHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new BandejaReppQuery(null, 1, 1), CancellationToken.None);

        r.Items.Should().ContainSingle();
        r.Total.Should().Be(3);
    }

    [Fact]
    public async Task NotasCredito()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        for (var i = 1; i <= 3; i++)
            db.NotasCredito.Add(NotaCredito.CrearBonificacion(empresaId, $"NC-{i}", i, Guid.NewGuid(), null, null, Receptor(),
                Emisor, "03", "MXN", null, 2026, 5, canalVentaId: 2, facturaRelacionadaId: Guid.NewGuid(),
                montoTotal: 116m, tasaIva: 0.16m, descripcion: null));
        await db.SaveChangesAsync();

        var r = await new BandejaNotasCreditoHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new BandejaNotasCreditoQuery(null, 1, 1), CancellationToken.None);

        r.Items.Should().ContainSingle();
        r.Total.Should().Be(3);
    }

    [Fact]
    public async Task CartaPorte()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var v = DominioCp.Vehiculo.Crear(empresaId, "ABC-123", "VL", 2022);
        var o = DominioCp.Operador.Crear(empresaId, "OPER010101AAA", "Juan", "LIC-1");
        db.Vehiculos.Add(v);
        db.Operadores.Add(o);
        for (var i = 1; i <= 3; i++)
            db.CartasPorte.Add(DominioCp.CartaPorte.CrearBorrador(empresaId, TipoComprobante.Traslado, $"CP-{i}", i, Guid.NewGuid(),
                null, null, Receptor(), Emisor, "MXN", 2026, 5, "Conkal", "Cancún", 300m,
                v.Id, o.Id, null, null, Ahora, Ahora.AddHours(4)));
        await db.SaveChangesAsync();

        var r = await new BandejaCartaPorteHandler(db)
            .Handle(new BandejaCartaPorteQuery(null, 1, 1), CancellationToken.None);

        r.Items.Should().ContainSingle();
        r.Total.Should().Be(3);
    }

    [Fact]
    public async Task ExcepcionesImportacion()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        for (var i = 1; i <= 3; i++)
            db.ExcepcionesImportacion.Add(ExcepcionImportacion.Crear(
                empresaId, OrigenPedido.PlantaPintura, $"FPIN-{i}", MotivoExcepcion.ClienteNoExiste, "no existe en master"));
        await db.SaveChangesAsync();

        var r = await new BandejaExcepcionesImportacionHandler(db)
            .Handle(new BandejaExcepcionesImportacionQuery(true, 1, 1), CancellationToken.None);

        r.Items.Should().ContainSingle();
        r.Total.Should().Be(3);
    }
}
