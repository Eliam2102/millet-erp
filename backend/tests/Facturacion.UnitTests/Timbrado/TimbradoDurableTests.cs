using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoBonificacion;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Exceptions;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Xunit;

namespace Millet.Facturacion.UnitTests.Timbrado;

/// <summary>
/// M-173 (86e3zr6wk): la solicitud de timbrado queda guardada antes de llamar al PAC. Datos ficticios.
/// </summary>
public sealed class TimbradoDurableTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 10, 18, 0, 0, TimeSpan.Zero);
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly string _base = Guid.NewGuid().ToString();

    private FacturacionDbContext Db() => new(new DbContextOptionsBuilder<FacturacionDbContext>()
        .UseInMemoryDatabase(_base).Options, new FakeEmpresaContext(_empresa));

    private FacturaVenta FacturaTimbrada(FacturacionDbContext db)
    {
        var f = FacturaVenta.CrearBorrador(_empresa, "MER-A15229", 15229, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "76120"),
            "PUE", "03", "MXN", null, 2026, 11, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "30171500", "Vidrio", "H87", 1m, 10000m, 0m, "02", 0.16m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(f);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return f;
    }

    private EmitirNotaCreditoBonificacionHandler Bonificacion(FacturacionDbContext db, ICfdiTimbradoPort pac) =>
        new(db, new FakeSender(new ReservarFolioResponse("NC-000231", 231, "")),
            new FakePeriodoContablePort(), pac, new FakeCfdiRepositorioPort(), new FakeIntegrationEventPublisher(),
            new FakeEmpresaContext(_empresa), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora),
            ReceptorFiscalTestFactory.Crear(db), new FakeCurrentUserPermissions(), new FakeSaldoFacturaReadPort());

    [Fact]
    public async Task NC_de_bonificacion_con_caida_del_PAC_queda_en_proceso_con_su_folio()
    {
        using var db = Db();
        var factura = FacturaTimbrada(db);

        var act = () => Bonificacion(db, new FakeFiscalCaida())
            .Handle(new EmitirNotaCreditoBonificacionCommand(factura.Id, 1160m, 0.16m, "Descuento"), default);

        await act.Should().ThrowAsync<HttpRequestException>();
        using var otra = Db();
        var nc = await otra.NotasCredito.SingleAsync();
        (nc.Folio, nc.Estado).Should().Be(("NC-000231", EstadoTimbrado.TimbradoEnProceso));
    }

    [Fact]
    public async Task Rechazo_del_adaptador_antes_de_enviar_deja_la_NC_fallida_y_corregible_no_en_proceso()
    {
        using var db = Db();
        var factura = FacturaTimbrada(db);

        var act = () => Bonificacion(db, new FakeFiscalSinConfiguracion())
            .Handle(new EmitirNotaCreditoBonificacionCommand(factura.Id, 1160m, 0.16m, "Descuento"), default);

        await act.Should().ThrowAsync<ConfiguracionPacNoDisponibleException>();
        using var otra = Db();
        var nc = await otra.NotasCredito.SingleAsync();
        (nc.Folio, nc.Estado, nc.TimbradoErrorCodigo).Should().Be(("NC-000231", EstadoTimbrado.TimbradoFallido, "CONFIG_PAC_NO_DISPONIBLE"));
        (await otra.BitacorasIntentoTimbrado.CountAsync(b => b.ComprobanteId == nc.Id)).Should().Be(1);
    }

    private sealed class FakeFiscalSinConfiguracion : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) =>
            throw new ConfiguracionPacNoDisponibleException(emision.EmpresaId);
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) => throw new NotSupportedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) => throw new NotSupportedException();
    }
}
