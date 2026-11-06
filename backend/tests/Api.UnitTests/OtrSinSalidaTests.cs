using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Infrastructure;
using Millet.SharedKernel.Infrastructure.Persistence.Interceptors;

namespace Millet.Api.UnitTests;
public sealed class OtrSinSalidaTests
{
    [Fact]
    public async Task Booking_embarque_e_hitos_se_guardan_en_el_expediente_sin_crear_salidas_hacia_AW()
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
        e.CambiarCompromiso("planeado", hoy, actor, null); await db.SaveChangesAsync();
        e.OmitirHito("planeado", "DEMO omitir planeado", actor); await db.SaveChangesAsync();
        var c = e.Contenedor(o.ContenedorId!.Value);
        e.GuardarContenedor(c.Id, OtrEnvio.Datos(c) with { Booking = "DEMO-BOOK" }); await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var guardado = await db.Expedientes.Include(x => x.Contenedores).Include(x => x.Hitos).SingleAsync();
        Assert.Equal("DEMO-BOOK", guardado.Contenedor(c.Id).Booking);
        Assert.True(guardado.Hitos.Single(h => h.Clave == "planeado").Omitido);
        Assert.Empty(await db.OtrSalidas.ToArrayAsync());
    }
}
