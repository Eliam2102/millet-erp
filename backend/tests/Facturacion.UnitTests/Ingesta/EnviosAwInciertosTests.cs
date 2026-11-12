using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Ingesta.Queries;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Ingesta;

/// <summary>M-233: un envío sin acuse cuya versión el ERP ya aplicó es «Incierto»; el resto, «EnCurso» con su excepción.</summary>
public sealed class EnviosAwInciertosTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    [Fact]
    public async Task Envio_interrumpido_aparece_como_incierto_y_el_otro_en_curso_con_su_excepcion()
    {
        await using var db = new FacturacionDbContext(
            new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(Empresa));
        var ahora = DateTimeOffset.UtcNow;
        db.IngestaControles.Add(IngestaControl.Crear(Empresa, OrigenPedido.Aw, "100", "h", 3, EstadoIngesta.Importado, null, ahora));
        db.ExcepcionesImportacion.Add(ExcepcionImportacion.Crear(Empresa, OrigenPedido.Aw, "200", MotivoExcepcion.TotalesNoCuadran, "no cuadra"));
        await db.SaveChangesAsync();
        var reader = new FakeAwSolicitudesReader(pendientes:
        [
            new SolicitudAw(Guid.NewGuid(), "100", OperacionAw.Alta, 3, ahora.AddMinutes(-5)),
            new SolicitudAw(Guid.NewGuid(), "200", OperacionAw.Alta, 1, ahora.AddMinutes(-1)),
        ]);

        var r = await new EnviosAwInciertosHandler(db, reader).Handle(new EnviosAwInciertosQuery(), CancellationToken.None);

        r.Select(i => (i.NumeroPedido, i.Estado)).Should().Equal(("100", "Incierto"), ("200", "EnCurso"));
        r[1].Detalle.Should().Be("no cuadra");
    }
}
