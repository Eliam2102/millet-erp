using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Infrastructure;
using Millet.SharedKernel.Infrastructure.Persistence.Interceptors;

namespace Millet.Api.UnitTests;
public sealed class OtrSalidaTests
{
    [Fact]
    public async Task Hito_booking_y_cancelaciones_usan_contador_propio_por_envio_y_tipo()
    {
        var empresa = Guid.NewGuid(); var actor = Guid.NewGuid(); var cliente = Guid.NewGuid(); var sucursal = Guid.NewGuid();
        var hoy = new DateOnly(2026, 10, 16);
        var metadata = new MetadataSaveChangesInterceptor(new FakeClock(new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)), new FakeUserContext(actor), new AuditOriginContext());
        using var db = new ComercioExteriorDbContext(new DbContextOptionsBuilder<ComercioExteriorDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(metadata).Options, new FakeEmpresaContext(empresa));
        var e = new Expediente(Guid.NewGuid(), empresa, "CE-EXP-DEMO", TipoExpediente.Exportacion, sucursal, "DEMO", null, Guid.NewGuid(), "USD", actor, ["DEMO cliente"]);
        e.AgregarParticipante(Guid.NewGuid(), "DEMO cliente", TipoParticipante.Cliente, null, PapelParticipante.Captura, null, cliente); e.InicializarHitos(hoy);
        var o = new OtrEnvio(empresa, "DEMO-OTR");
        o.Aplicar(1, new(cliente, sucursal, "DEMO cliente", "DEMO entrega", null, new("DEMO-C", null, null, "DEMO-BL", null, hoy, 100, 1), [new("DEMO-PED", 1, 10, "H87")]));
        o.Vincular(e); e.GuardarContenedor(o.ContenedorId!.Value, o.Conciliar(null, new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)));
        db.Expedientes.Add(e); db.OtrEnvios.Add(o); await db.SaveChangesAsync();
        var inicial = await db.OtrSalidas.SingleAsync(); Assert.Equal(1, inicial.VersionErp); Assert.Equal("hito", inicial.Tipo);
        Assert.Equal(EstadoOtrSalida.Pendiente, inicial.Estado);
        e.CambiarCompromiso("planeado", hoy, actor, null); await db.SaveChangesAsync(); Assert.Single(await db.OtrSalidas.ToArrayAsync());
        e.OmitirHito("planeado", "DEMO omitir planeado", actor); await db.SaveChangesAsync();
        Assert.Contains(await db.OtrSalidas.ToArrayAsync(), s => s.Tipo == "hito" && s.VersionErp == 2);
        var c = e.Contenedor(o.ContenedorId.Value); e.GuardarContenedor(c.Id, OtrEnvio.Datos(c) with { Booking = "DEMO-BOOK" }); await db.SaveChangesAsync();
        Assert.Equal(1, (await db.OtrSalidas.SingleAsync(s => s.Tipo == "booking")).VersionErp);
        e.GuardarContenedor(c.Id, OtrEnvio.Datos(c) with { Booking = null }); await db.SaveChangesAsync();
        Assert.Equal(1, (await db.OtrSalidas.SingleAsync(s => s.Tipo == "booking-cancelado")).VersionErp);
        e.CancelarContenedor(c.Id, "DEMO cancelación contenedor", false); await db.SaveChangesAsync();
        Assert.Equal(1, (await db.OtrSalidas.SingleAsync(s => s.Tipo == "contenedor-cancelado")).VersionErp);
        db.ChangeTracker.Clear();
        e = await db.Expedientes.Include(x => x.Hitos).SingleAsync();
        db.OtrSalidas.Add(o.CrearSalida("hito", 40, "{}", []));
        e.OmitirHito("booking-confirmado", "DEMO segundo hito", actor); await db.SaveChangesAsync();
        Assert.Equal(41, await db.OtrSalidas.Where(s => s.Tipo == "hito").MaxAsync(s => s.VersionErp));
        var cantidad = await db.OtrSalidas.CountAsync(); await db.SaveChangesAsync(); Assert.Equal(cantidad, await db.OtrSalidas.CountAsync());
    }
}
