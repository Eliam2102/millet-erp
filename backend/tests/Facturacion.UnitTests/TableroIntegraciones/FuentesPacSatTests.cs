using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.PublicAdapters;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.Facturacion.UnitTests.TableroIntegraciones;

/// <summary>
/// I13.6 · timbrado con el PAC y cancelaciones ante el SAT en el tablero de integraciones.
/// Datos ficticios.
/// </summary>
public sealed class FuentesPacSatTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);
    private static int _folio;

    [Fact]
    public async Task Timbrado_cuenta_en_proceso_fallidos_de_24_h_y_timbrados()
    {
        using var db = NewDb();
        var enProceso = Anticipo();
        enProceso.MarcarTimbradoEnProceso();
        var fallido = Anticipo();
        fallido.MarcarTimbradoEnProceso();
        fallido.MarcarTimbradoFallido("PAC_RECHAZO", "RFC del receptor no válido");
        var fallidoViejo = Anticipo();
        fallidoViejo.MarcarTimbradoEnProceso();
        fallidoViejo.MarcarTimbradoFallido("PAC_RECHAZO", "viejo");
        var timbrado = Anticipo();
        timbrado.MarcarTimbradoEnProceso();
        timbrado.MarcarTimbrado(Guid.NewGuid().ToString().ToUpperInvariant(), null, null, null, Ahora.AddHours(-1), null, null);
        db.AddRange(enProceso, fallido, fallidoViejo, timbrado);
        Tocado(db, enProceso, Ahora.AddMinutes(-20));
        Tocado(db, fallido, Ahora.AddHours(-1));
        Tocado(db, fallidoViejo, Ahora.AddDays(-3));
        Tocado(db, timbrado, Ahora.AddHours(-1));
        await db.SaveChangesAsync();

        var r = await new TimbradoPacFuenteTablero(db).LeerAsync(Ahora, 10, default);

        (r.Pendientes, r.EnError, r.OkUltimas24h).Should().Be((1, 1, 1), "el fallido de hace 3 días ya no abre alerta");
        r.PendienteMasAntiguo.Should().Be(Ahora.AddMinutes(-20));
        r.UltimoExito.Should().Be(Ahora.AddHours(-1));
        r.Mensajes.Should().HaveCount(3);
        r.Mensajes[0].Should().Match<MensajeIntegracion>(m =>
            m.Estado == EstadoMensajeIntegracion.Error && m.UltimoError == "PAC_RECHAZO: RFC del receptor no válido");
    }

    [Fact]
    public async Task Cancelaciones_solo_marcan_detenida_la_que_no_sale_al_pac_y_el_rechazo_es_error()
    {
        using var db = NewDb();
        var sinSalir = Solicitud(Ahora.AddMinutes(-30));
        var esperandoReceptor = Solicitud(Ahora.AddDays(-2));
        esperandoReceptor.MarcarEnProceso("EnProceso");
        var rechazada = Solicitud(Ahora.AddDays(-1));
        rechazada.MarcarEnProceso("EnProceso");
        rechazada.MarcarRechazada("El receptor rechazó la cancelación", Ahora.AddHours(-2));
        var aceptada = Solicitud(Ahora.AddDays(-1));
        aceptada.MarcarEnProceso("EnProceso");
        aceptada.MarcarAceptada("Cancelado", Ahora.AddHours(-3));
        db.SolicitudesCancelacion.AddRange(sinSalir, esperandoReceptor, rechazada, aceptada);
        await db.SaveChangesAsync();

        var r = await new CancelacionSatFuenteTablero(db).LeerAsync(Ahora, 10, default);

        (r.Pendientes, r.EnError, r.OkUltimas24h).Should().Be((2, 1, 1));
        r.PendienteMasAntiguo.Should().Be(Ahora.AddMinutes(-30), "la espera del receptor no es una falla");
        r.UltimoExito.Should().Be(Ahora.AddHours(-3));
        r.Mensajes[0].Should().Match<MensajeIntegracion>(m =>
            m.Estado == EstadoMensajeIntegracion.Error && m.UltimoError == "El receptor rechazó la cancelación");
    }

    private static FacturaAnticipo Anticipo()
    {
        var n = Interlocked.Increment(ref _folio);
        return FacturaAnticipo.CrearBorrador(Empresa, $"FA-{n}", n, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("BBB010101BBB", "CLIENTE PRUEBA", "601", "64000", "G03", "MEX", false),
            new DatosFiscalesEmisor("AAA010101AAA", "EMPRESA PRUEBA", "601", "97000"), "03", "MXN", null, 2026, 11,
            TipoAnticipo.ClientesMxp, null, Guid.NewGuid(), 1000m, 0.16m);
    }

    private static SolicitudCancelacion Solicitud(DateTimeOffset solicitadaEn)
    {
        var s = SolicitudCancelacion.Crear(Empresa, Guid.NewGuid(), "02", null, solicitadaEn);
        s.Autorizar(Guid.NewGuid(), "Contador", solicitadaEn);
        return s;
    }

    /// <summary>En memoria no corre el interceptor de metadatos: se fija la hora de la última modificación.</summary>
    private static void Tocado(FacturacionDbContext db, Comprobante c, DateTimeOffset cuando) =>
        db.Entry(c).Property(x => x.UpdatedAt).CurrentValue = cuando;

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(Empresa));
}
