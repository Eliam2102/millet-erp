using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Cancelaciones;
using Millet.Facturacion.Application.Cancelaciones.AutorizarCancelacion;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cancelaciones;

/// <summary>
/// H-U31: cuando el SAT responde 205 (aún no refleja el CFDI) la cancelación no se rechaza: la factura sigue vigente, la misma
/// solicitud queda pendiente de reintento con un aviso y fecha sugerida, y el reintento la reenvía sin crear otra.
/// </summary>
public sealed class CancelacionSat205Tests
{
    private static readonly DateTimeOffset Timbrado = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ahora = Timbrado.AddMinutes(10);
    private static readonly Guid Ingresos = Guid.NewGuid();
    private static readonly Guid Contador = Guid.NewGuid();

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(empresaId));

    /// <summary>PAC con respuestas en fila.</summary>
    private sealed class FakeFiscalEnFila(params CancelacionResultado[] respuestas) : ICfdiTimbradoPort
    {
        private readonly Queue<CancelacionResultado> _fila = new(respuestas);
        private readonly FakeFiscalApiClient _interno = new();
        public int Cancelaciones { get; private set; }
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) => _interno.TimbrarAsync(emision, ct);
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct)
        {
            Cancelaciones++;
            return Task.FromResult(_fila.Dequeue());
        }
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) => _interno.ConsultarEstatusAsync(s, ct);
    }

    private static readonly CancelacionResultado Sat205 =
        new(CancelacionEstado.Error, null, "Cancellation failed — 205 - UUID no existe");

    private static async Task<FacturaVenta> SembrarFacturaAsync(FacturacionDbContext db, Guid empresaId)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(empresaId, "AW-1", Guid.NewGuid(), Guid.NewGuid(),
            "Cliente", (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Emisor", "601", "76120"), "PUE", "03", "MXN", null, 2026, 11,
            (short)1, ComportamientoFiscal.MostradorInmediato, pedido.Id, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Timbrado, null, null);
        pedido.MarcarFacturado(fv.Id);
        db.PedidosFacturables.Add(pedido);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return fv;
    }

    private static AutorizarCancelacionHandler Handler(FacturacionDbContext db, ICfdiTimbradoPort fiscal, DateTimeOffset ahora) =>
        new(db, fiscal, new FakeIntegrationEventPublisher(), new FakeUserContext(Contador, "Contador"), new FakeClock(ahora),
            Options.Create(new CancelacionSatOpciones { ReintentoMinutos = 60 }));

    private static async Task<(FacturacionDbContext Db, FacturaVenta Factura)> SolicitadaAsync()
    {
        var empresa = Guid.NewGuid();
        var db = NewDb(empresa);
        var fv = await SembrarFacturaAsync(db, empresa);
        await new SolicitarCancelacionHandler(db, new FakeEmpresaContext(empresa), new FakeUserContext(Ingresos, "Ingresos"), new FakeClock(Ahora))
            .Handle(new SolicitarCancelacionCommand(fv.Id, "02", null), CancellationToken.None);
        return (db, fv);
    }

    [Fact]
    public async Task El_205_deja_la_factura_vigente_y_la_solicitud_pendiente_de_reintento_con_fecha_sugerida()
    {
        var (db, fv) = await SolicitadaAsync();
        using var _ = db;

        var r = await Handler(db, new FakeFiscalEnFila(Sat205), Ahora).Handle(new AutorizarCancelacionCommand(fv.Id), CancellationToken.None);

        r.EstadoComprobante.Should().Be(nameof(EstadoTimbrado.Timbrado));
        r.EstadoSolicitud.Should().Be(nameof(EstadoSolicitudCancelacion.Solicitada));
        r.MensajeError.Should().StartWith(CancelacionSat205.PrefijoAviso)
            .And.Contain("reintenta en unos minutos")
            // Primer envío: timbrado (18:00 UTC = 12:00 en CDMX) + 60 min.
            .And.Contain("Reintento sugerido: 11/11/2026 13:00");
        (await db.SolicitudesCancelacion.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task El_reintento_reenvia_la_misma_solicitud_y_si_el_SAT_acepta_la_factura_queda_cancelada()
    {
        var (db, fv) = await SolicitadaAsync();
        using var _ = db;
        var fiscal = new FakeFiscalEnFila(Sat205, Sat205, new CancelacionResultado(CancelacionEstado.Aceptada, "Cancelado", null));
        await Handler(db, fiscal, Ahora).Handle(new AutorizarCancelacionCommand(fv.Id), CancellationToken.None);

        // Segundo 205: la fecha sugerida corre desde el reintento.
        var otraVez = await Handler(db, fiscal, Ahora.AddHours(2)).Handle(new ReintentarCancelacionCommand(fv.Id), CancellationToken.None);
        otraVez.EstadoSolicitud.Should().Be(nameof(EstadoSolicitudCancelacion.Solicitada));
        otraVez.MensajeError.Should().Contain("Reintento sugerido: 11/11/2026 15:10");

        var final = await Handler(db, fiscal, Ahora.AddHours(4)).Handle(new ReintentarCancelacionCommand(fv.Id), CancellationToken.None);
        final.EstadoComprobante.Should().Be(nameof(EstadoTimbrado.Cancelado));
        final.EstadoSolicitud.Should().Be(nameof(EstadoSolicitudCancelacion.Aceptada));
        final.MensajeError.Should().BeNull();
        fiscal.Cancelaciones.Should().Be(3);
        (await db.SolicitudesCancelacion.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Otro_error_del_SAT_sigue_rechazando_y_no_admite_reintento()
    {
        var (db, fv) = await SolicitadaAsync();
        using var _ = db;
        var r = await Handler(db, new FakeFiscalEnFila(new CancelacionResultado(CancelacionEstado.Error, null, "Falló — 300 - Usuario no válido")), Ahora)
            .Handle(new AutorizarCancelacionCommand(fv.Id), CancellationToken.None);
        r.EstadoSolicitud.Should().Be(nameof(EstadoSolicitudCancelacion.Rechazada));

        var act = () => Handler(db, new FakeFiscalEnFila(), Ahora).Handle(new ReintentarCancelacionCommand(fv.Id), CancellationToken.None);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CANCELACION_SIN_REINTENTO");
    }

    [Theory]
    [InlineData("205 - UUID no existe", true)]
    [InlineData("Error 1205", false)]
    [InlineData("2050", false)]
    public void Reconoce_solo_el_codigo_205(string mensaje, bool esperado) =>
        CancelacionSat205.EsNoReflejadoAun(new CancelacionResultado(CancelacionEstado.Error, null, mensaje)).Should().Be(esperado);
}
