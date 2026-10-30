using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.UnitTests.TestSupport;

namespace Millet.Almacen.UnitTests.P1;

/// <summary>
/// REQ-PERM (86e3xd4uz), criterio 4: el vale urgente en Conkal (sucursal 101 del servidor de pruebas)
/// registra la salida y la deja pendiente de regularizar con plazo de 48 h hábiles.
/// </summary>
public sealed class ValeUrgenteConkalTests
{
    [Fact]
    public async Task Vale_urgente_en_Conkal_queda_pendiente_de_regularizar()
    {
        await using var f = new P1Fixture();
        var conkal = Guid.NewGuid();
        typeof(Domain.Catalogo.Almacen).GetProperty(nameof(Domain.Catalogo.Almacen.SucursalId))!
            .SetValue(await f.Db.Almacenes.SingleAsync(), conkal);
        await f.Db.SaveChangesAsync();
        var h = new RegistrarSalidaPorValeHandler(f.Db, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true),
            new P1Fixture.Calendario(), new P1Fixture.Centros(), new P7Support.Conversion(), P7Support.Apartados(f.Db));

        await h.Handle(new(P1Fixture.Fecha, "vale.pdf", null, null, [new(f.ArticuloId, null, 1, Guid.NewGuid(), null, null, null, f.BinId)]), default);

        var vale = await f.Db.Movimientos.SingleAsync();
        vale.PendienteRegularizacion.Should().BeTrue();
        vale.FechaLimiteRegularizacion.Should().NotBeNull();
        f.Events.Items.OfType<SalidaRequisicionRegistradaIntegrationEvent>().Single().SucursalId.Should().Be(conkal);
    }
}
