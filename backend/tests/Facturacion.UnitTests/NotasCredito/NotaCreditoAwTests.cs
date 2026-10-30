using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Ingesta.ProcesarNotaCreditoAw;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.NotasCredito.AutorizarNotaCredito;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.NotasCredito;

/// <summary>86e3wjt5r: nota de crédito de A+W → «Por autorizar» → autorizar y timbrar (criterios 1 a 5 de la ficha).</summary>
public sealed class NotaCreditoAwTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakePuente : IAwNotasCreditoPort
    {
        public List<AcuseNotaCreditoAw> Acuses { get; } = [];
        public Task<IReadOnlyList<SolicitudNotaCreditoAw>> LeerNotasPendientesAsync(int max, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SolicitudNotaCreditoAw>>([]);
        public Task EscribirAcuseAsync(AcuseNotaCreditoAw acuse, CancellationToken cancellationToken)
        {
            Acuses.Add(acuse);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSaldo(decimal? saldo) : ISaldoFacturaReadPort
    {
        public Task<decimal?> ObtenerSaldoAsync(Guid facturaVentaId, CancellationToken cancellationToken) => Task.FromResult(saldo);
    }

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(empresaId));

    private static DatosFiscalesReceptor Receptor() => new("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);

    /// <summary>Pedido A+W «AW-NC» (versión 3) con su factura timbrada de $5,800 (5,000 + IVA).</summary>
    private static async Task<(FacturaVenta Factura, PedidoFacturable Pedido)> SembrarFacturaAwAsync(
        FacturacionDbContext db, Guid empresaId, EstadoTimbrado estado = EstadoTimbrado.Timbrado, string numeroPedido = "AW-NC")
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(empresaId, numeroPedido, Guid.NewGuid(), Guid.NewGuid(), "Cliente",
            (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 3, "15");
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-000001", 1, pedido.SucursalId, null, null, Receptor(),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "03", "MXN", null, 2026, 10,
            (short)1, ComportamientoFiscal.MostradorInmediato, pedido.Id, null, null, false);
        fv.AgregarLinea(null, "01010101", "Producto", "H87", 5m, 1000m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        if (estado == EstadoTimbrado.Timbrado)
        {
            fv.MarcarTimbradoEnProceso();
            fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Ahora, null, null);
        }
        db.PedidosFacturables.Add(pedido);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return (fv, pedido);
    }

    private static SolicitudNotaCreditoAw Sol(
        decimal importe = 1500m, long version = 1, string numero = "NC-100", string pedido = "AW-NC",
        string motivo = "Bonificacion", string? folio = null) =>
        new(Guid.NewGuid(), numero, version, pedido, folio, "CLI-1", motivo, importe, "Descuento", Ahora);

    private static ProcesarNotaCreditoAwHandler Ingesta(
        FacturacionDbContext db, FakePuente puente, Guid? clienteId, decimal? saldo = null) =>
        new(db, puente,
            new FakeClientesReadPort(clienteId is null ? null
                : new ClienteFiscalLectura(clienteId.Value, "AAA010101AAA", "Cliente", "601", "97000", "G03", "01", "PUE", "MXN", false)),
            new FakeSaldo(saldo), new FakeSender(new ReservarFolioResponse("NC-000001", 1, "")), new FakeClock(Ahora));

    private static async Task<(ProcesarNotaCreditoAwHandler H, FacturacionDbContext Db, FakePuente Puente, Guid Empresa, FacturaVenta F)> EscenarioAsync(
        decimal? saldo = null, EstadoTimbrado estadoFactura = EstadoTimbrado.Timbrado, bool clienteDelPedido = true)
    {
        var empresa = Guid.NewGuid();
        var db = NewDb(empresa);
        var (f, p) = await SembrarFacturaAwAsync(db, empresa, estadoFactura);
        var puente = new FakePuente();
        return (Ingesta(db, puente, clienteDelPedido ? p.ClienteId : Guid.NewGuid(), saldo), db, puente, empresa, f);
    }

    // ---- Criterio 1: llega → «Por autorizar», ligada a la factura, sin timbrar ----

    [Fact]
    public async Task Nota_de_1500_sobre_factura_timbrada_queda_por_autorizar_ligada_y_sin_timbrar()
    {
        var (h, db, puente, empresa, factura) = await EscenarioAsync();

        var r = await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, Sol()), CancellationToken.None);

        r.Resultado.Should().Be(ResultadoSolicitudAw.Aplicada);
        var nc = await db.NotasCredito.Include(n => n.Relaciones).SingleAsync();
        nc.Estado.Should().Be(EstadoTimbrado.PorAutorizar);
        nc.Uuid.Should().BeNull();
        nc.Total.Should().Be(1500m);
        nc.Motivo.Should().Be(MotivoNotaCredito.Bonificacion);
        nc.FacturaRelacionadaId.Should().Be(factura.Id);
        nc.Relaciones.Should().ContainSingle(x => x.TipoRelacion == "01" && x.UuidRelacionado == factura.Uuid);
        nc.AwNumeroNota.Should().Be("NC-100");
        nc.AwNumeroPedido.Should().Be("AW-NC");
        nc.AwVersionPedido.Should().Be(3);
        puente.Acuses.Should().ContainSingle(a => a.Resultado == ResultadoSolicitudAw.Aplicada && a.ErpNotaCreditoId == nc.Id && a.Estado == "PorAutorizar");
    }

    [Fact]
    public async Task Devolucion_usa_relacion_03_y_marca_el_subtotal_que_afecta_inventario()
    {
        var (h, db, _, empresa, _) = await EscenarioAsync();

        await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, Sol(motivo: "Devolucion", importe: 1160m)), CancellationToken.None);

        var nc = await db.NotasCredito.Include(n => n.Relaciones).SingleAsync();
        nc.Motivo.Should().Be(MotivoNotaCredito.Devolucion);
        nc.Relaciones.Should().ContainSingle(x => x.TipoRelacion == "03");
        nc.ImporteAfectaInventario.Should().Be(1000m);
    }

    // ---- Criterio 2: dos veces → una sola nota ----

    [Fact]
    public async Task La_misma_nota_llegando_dos_veces_crea_una_sola()
    {
        var (h, db, puente, empresa, _) = await EscenarioAsync();
        var sol = Sol();

        await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, sol), CancellationToken.None);
        var segunda = await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, sol), CancellationToken.None);

        (await db.NotasCredito.CountAsync()).Should().Be(1);
        segunda.Resultado.Should().Be(ResultadoSolicitudAw.Aplicada);
        segunda.NotaCreditoId.Should().Be((await db.NotasCredito.SingleAsync()).Id);
        puente.Acuses.Should().HaveCount(2);
    }

    [Fact]
    public async Task Version_nueva_de_una_nota_ya_recibida_se_rechaza_sin_duplicar()
    {
        var (h, db, _, empresa, _) = await EscenarioAsync();
        await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, Sol()), CancellationToken.None);

        var r = await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, Sol(version: 2)), CancellationToken.None);

        r.Resultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        (await db.NotasCredito.CountAsync()).Should().Be(1);
    }

    // ---- Criterio 3: factura inexistente → excepción con motivo; no se crea ----

    [Theory]
    [InlineData("AW-OTRO", EstadoTimbrado.Timbrado)]   // pedido sin factura
    [InlineData("AW-NC", EstadoTimbrado.Borrador)]     // factura aún sin timbrar
    public async Task Factura_de_origen_inexistente_abre_excepcion_y_no_crea_nota(string pedido, EstadoTimbrado estado)
    {
        var (h, db, puente, empresa, _) = await EscenarioAsync(estadoFactura: estado);

        var r = await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, Sol(pedido: pedido)), CancellationToken.None);

        r.Resultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        (await db.NotasCredito.CountAsync()).Should().Be(0);
        var ex = await db.ExcepcionesImportacion.SingleAsync();
        ex.Motivo.Should().Be(MotivoExcepcion.NcFacturaNoExiste);
        ex.Resuelto.Should().BeFalse();
        puente.Acuses.Single().Resultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        puente.Acuses.Single().Motivo.Should().Contain("NC-100");
    }

    [Fact]
    public async Task Factura_indicada_por_folio_se_usa_aunque_el_pedido_no_coincida()
    {
        var (h, db, _, empresa, _) = await EscenarioAsync();

        await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, Sol(pedido: "AW-OTRO", folio: "F-000001")), CancellationToken.None);

        (await db.NotasCredito.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Cliente_distinto_al_de_la_factura_abre_excepcion()
    {
        var (h, db, _, empresa, _) = await EscenarioAsync(clienteDelPedido: false);

        var r = await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, Sol()), CancellationToken.None);

        r.Resultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        (await db.NotasCredito.CountAsync()).Should().Be(0);
        (await db.ExcepcionesImportacion.SingleAsync()).Motivo.Should().Be(MotivoExcepcion.NcClienteDistinto);
    }

    // ---- Criterio 5: importe mayor que el saldo ----

    [Fact]
    public async Task Nota_de_9000_sobre_factura_con_saldo_5000_es_excepcion_importe_mayor_que_el_saldo()
    {
        var (h, db, puente, empresa, _) = await EscenarioAsync(saldo: 5000m);

        var r = await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, Sol(importe: 9000m)), CancellationToken.None);

        r.Resultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        (await db.NotasCredito.CountAsync()).Should().Be(0);
        (await db.ExcepcionesImportacion.SingleAsync()).Motivo.Should().Be(MotivoExcepcion.NcImporteExcedeSaldo);
        puente.Acuses.Single().Motivo.Should().Contain("mayor que el saldo");
    }

    [Fact]
    public async Task Las_notas_por_autorizar_reservan_saldo()
    {
        var (h, db, _, empresa, _) = await EscenarioAsync(saldo: 2000m);
        await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, Sol(importe: 1500m, numero: "NC-1")), CancellationToken.None);

        var r = await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, Sol(importe: 1000m, numero: "NC-2")), CancellationToken.None);

        r.Resultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        (await db.NotasCredito.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Version_corregida_de_una_nota_rechazada_entra_y_cierra_la_excepcion()
    {
        var (h, db, _, empresa, _) = await EscenarioAsync(saldo: 5000m);
        await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, Sol(importe: 9000m)), CancellationToken.None);

        var r = await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, Sol(importe: 4000m, version: 2)), CancellationToken.None);

        r.Resultado.Should().Be(ResultadoSolicitudAw.Aplicada);
        (await db.ExcepcionesImportacion.SingleAsync()).Resuelto.Should().BeTrue();
    }

    // ---- Criterio 4: autorizada → se timbra; evento con folio y pedido para la fila de retorno ----

    private static async Task<(AutorizarNotaCreditoHandler H, FacturacionDbContext Db, FakeIntegrationEventPublisher Eventos, Guid NcId)> NotaPorAutorizarAsync(
        decimal? saldo = null, bool periodoAbierto = true)
    {
        var (h, db, _, empresa, _) = await EscenarioAsync();
        var r = await h.Handle(new ProcesarNotaCreditoAwCommand(empresa, Sol()), CancellationToken.None);
        var eventos = new FakeIntegrationEventPublisher();
        var autorizar = new AutorizarNotaCreditoHandler(db, new FakePeriodoContablePort(periodoAbierto), new FakeSaldo(saldo),
            new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(), eventos, new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));
        return (autorizar, db, eventos, r.NotaCreditoId!.Value);
    }

    [Fact]
    public async Task Autorizar_timbra_y_publica_el_evento_con_pedido_version_y_folio()
    {
        var (autorizar, db, eventos, ncId) = await NotaPorAutorizarAsync();

        var resp = await autorizar.Handle(new AutorizarNotaCreditoCommand(ncId), CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        resp.Uuid.Should().NotBeNullOrEmpty();
        var nc = await db.NotasCredito.SingleAsync();
        nc.AutorizadaPorId.Should().NotBeNull();
        nc.AutorizadaEn.Should().Be(Ahora);
        var evento = eventos.Publicados.OfType<NotaCreditoTimbradaIntegrationEvent>().Single();
        evento.Total.Should().Be(1500m);          // CxC baja el saldo de la factura con este total
        evento.FacturaRelacionadaId.Should().Be(nc.FacturaRelacionadaId);
        evento.NumeroPedidoAw.Should().Be("AW-NC");
        evento.VersionPedidoAw.Should().Be(3);
        evento.Folio.Should().Be("NC-000001");
        evento.Uuid.Should().Be(nc.Uuid);
    }

    [Fact]
    public async Task Autorizar_dos_veces_o_una_nota_que_no_esta_por_autorizar_falla()
    {
        var (autorizar, _, _, ncId) = await NotaPorAutorizarAsync();
        await autorizar.Handle(new AutorizarNotaCreditoCommand(ncId), CancellationToken.None);

        var act = () => autorizar.Handle(new AutorizarNotaCreditoCommand(ncId), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "NC_NO_POR_AUTORIZAR");
    }

    [Fact]
    public async Task Autorizar_con_el_saldo_ya_menor_que_la_nota_falla_y_no_timbra()
    {
        var (autorizar, db, eventos, ncId) = await NotaPorAutorizarAsync(saldo: 1000m);

        var act = () => autorizar.Handle(new AutorizarNotaCreditoCommand(ncId), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "NC_IMPORTE_MAYOR_SALDO");
        (await db.NotasCredito.SingleAsync()).Estado.Should().Be(EstadoTimbrado.PorAutorizar);
        eventos.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task Autorizar_con_periodo_cerrado_falla()
    {
        var (autorizar, _, _, ncId) = await NotaPorAutorizarAsync(periodoAbierto: false);

        var act = () => autorizar.Handle(new AutorizarNotaCreditoCommand(ncId), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PERIODO_CERRADO");
    }

    [Fact]
    public async Task Autorizar_nota_inexistente_lanza_NotFound()
    {
        var (autorizar, _, _, _) = await NotaPorAutorizarAsync();

        var act = () => autorizar.Handle(new AutorizarNotaCreditoCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "NOTA_CREDITO_NO_ENCONTRADA");
    }

    // ---- Las notas del ERP (sin origen A+W) no generan fila de retorno ----

    [Fact]
    public void El_evento_de_una_nota_del_ERP_no_lleva_pedido_de_A_W()
    {
        var nc = NotaCredito.CrearBonificacion(Guid.NewGuid(), "NC-1", 1, Guid.NewGuid(), null, null, Receptor(),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "03", "MXN", null, 2026, 10,
            canalVentaId: 1, facturaRelacionadaId: Guid.NewGuid(), montoTotal: 116m, tasaIva: 0.16m, descripcion: null);
        nc.MarcarTimbradoEnProceso();
        nc.MarcarTimbrado("22222222-2222-2222-2222-222222222222", null, null, null, Ahora, null, null);

        var evento = EventosContablesFacturacion.NotaCreditoTimbrada(nc, Ahora);

        evento.NumeroPedidoAw.Should().BeNull();
        evento.VersionPedidoAw.Should().BeNull();
        evento.Folio.Should().Be("NC-1");
    }
}
