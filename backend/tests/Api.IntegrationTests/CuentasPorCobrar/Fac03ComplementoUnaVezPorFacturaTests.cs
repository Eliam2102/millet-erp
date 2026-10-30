using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using Millet.CuentasPorCobrar.Application.EventListeners;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.CuentasPorCobrar;

/// <summary>
/// 86e3qddce · FAC-03 criterio 3 contra PostgreSQL: el mismo complemento con otro EventId
/// no vuelve a descontar la factura de 10,000 (cartera sigue en 6,000 y parcialidad 1) y el
/// índice único (complemento + factura + tipo) rechaza el duplicado aunque se salte el listener.
/// </summary>
public sealed class Fac03ComplementoUnaVezPorFacturaTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");

    [Fact]
    public async Task Mismo_complemento_con_otro_EventId_deja_la_cartera_en_6000_y_un_solo_movimiento()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var factura = await NuevaFactura(db);
        var reppId = Guid.NewGuid();
        try
        {
            ReciboPagoTimbradoCommand Evento() => new(Guid.NewGuid(), new ReciboPagoTimbradoPayload(
                Empresa, DateTimeOffset.UtcNow, reppId, Guid.NewGuid().ToString(), 4_000m, 0m,
                [new ReppFacturaPagadaPayload(factura.FacturaVentaId, 4_000m, 1, "MXN", 6_000m)]));
            await sender.Send(Evento());
            await sender.Send(Evento());

            var f = await db.FacturasCartera.AsNoTracking().SingleAsync(x => x.Id == factura.Id);
            f.SaldoPendiente.Should().Be(6_000m);
            (await db.MovimientosCartera.CountAsync(m => m.FacturaCarteraId == factura.Id)).Should().Be(1);
        }
        finally { await Limpiar(db, factura.Id); }
    }

    [Fact]
    public async Task Indice_unico_rechaza_dos_movimientos_del_mismo_complemento_y_factura()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        var factura = await NuevaFactura(db);
        var reppId = Guid.NewGuid();
        try
        {
            MovimientoCartera Mov() => new(Empresa, factura.Id, TipoMovimientoCartera.Pago, reppId, 4_000m, DateTimeOffset.UtcNow);
            db.MovimientosCartera.Add(Mov());
            await db.SaveChangesAsync();
            db.MovimientosCartera.Add(Mov());
            await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        }
        finally { db.ChangeTracker.Clear(); await Limpiar(db, factura.Id); }
    }

    private static async Task<FacturaCartera> NuevaFactura(CuentasPorCobrarDbContext db)
    {
        var f = FacturaCartera.Crear(Empresa, Guid.NewGuid(), Guid.NewGuid(), "AAA010101AAA", "Cliente ficticio FAC-03",
            Guid.NewGuid().ToString(), $"F03-{Guid.NewGuid():N}", 10_000m, "MXN", "PPD",
            DateTimeOffset.UtcNow.AddDays(-5), DateTimeOffset.UtcNow.AddDays(25));
        db.FacturasCartera.Add(f);
        await db.SaveChangesAsync();
        return f;
    }

    private static async Task Limpiar(CuentasPorCobrarDbContext db, Guid facturaId)
    {
        await db.MovimientosCartera.Where(m => m.FacturaCarteraId == facturaId).ExecuteDeleteAsync();
        await db.FacturasCartera.Where(f => f.Id == facturaId).ExecuteDeleteAsync();
    }
}
