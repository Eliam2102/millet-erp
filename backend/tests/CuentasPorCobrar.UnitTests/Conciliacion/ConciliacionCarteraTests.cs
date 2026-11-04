using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Conciliacion;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Conciliacion;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.CuentasPorCobrar.Infrastructure.Workers;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.Conciliacion;

/// <summary>C7.7: conciliación diaria de cartera contra Facturación.</summary>
public sealed class ConciliacionCarteraTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateOnly Dia = new(2026, 11, 2);
    private static readonly DateTimeOffset Timbrado = new(2026, 11, 2, 16, 0, 0, TimeSpan.Zero);

    private static FacturaFacturacion Factura(Guid id, decimal total = 1_160m, bool cancelada = false) =>
        new(Empresa, id, "FV-100", "UUID-FV", "VGL860910IU4", "Vidrios del Golfo", total, cancelada);

    private static AplicacionFacturacion Aplicacion(TipoAplicacionFacturacion tipo, Guid origen, Guid factura,
        decimal importe = 200m, bool cancelada = false) =>
        new(Empresa, tipo, origen, "NC-7", "UUID-NC", factura, "VGL860910IU4", "Vidrios del Golfo", importe, cancelada);

    private static FacturaCartera EnCartera(Guid facturaVentaId, decimal total = 1_160m) =>
        FacturaCartera.Crear(Empresa, facturaVentaId, null, "VGL860910IU4", "Vidrios del Golfo", "UUID-FV", "FV-100",
            total, "MXN", "PPD", Timbrado, Timbrado.AddDays(30));

    private static IReadOnlyList<DiferenciaCalculada> Comparar(ComprobantesDelDia dia, params FacturaCartera[] cartera) =>
        ComparadorConciliacion.Comparar(dia,
            cartera.Select(f => new FacturaEnCartera(f.Id, f.FacturaVentaId, f.Total, f.Estado)).ToList(), []);

    [Fact]
    public void C77a_factura_timbrada_sin_evento_en_cartera_es_diferencia()
    {
        var facturaId = Guid.NewGuid();

        var diferencias = Comparar(new([Factura(facturaId)], []));

        var d = diferencias.Should().ContainSingle().Subject;
        d.Tipo.Should().Be(TipoDiferenciaConciliacion.FacturaNoEnCartera);
        d.ComprobanteId.Should().Be(facturaId);
        d.ImporteFacturacion.Should().Be(1_160m);
        d.ImporteCartera.Should().BeNull();
        d.CausaProbable.Should().Contain("evento de factura timbrada");
    }

    [Fact]
    public void C77c_nc_timbrada_sin_movimiento_en_cartera_es_diferencia()
    {
        var factura = EnCartera(Guid.NewGuid());
        var nc = Guid.NewGuid();

        var diferencias = Comparar(new([], [Aplicacion(TipoAplicacionFacturacion.NotaCredito, nc, factura.FacturaVentaId)]), factura);

        var d = diferencias.Should().ContainSingle().Subject;
        d.Tipo.Should().Be(TipoDiferenciaConciliacion.NotaCreditoNoReflejada);
        d.ComprobanteId.Should().Be(nc);
        d.TipoComprobante.Should().Be("Nota de crédito");
        d.CausaProbable.Should().Contain("No se procesó el evento de la nota de crédito");
    }

    [Fact]
    public void Todo_reflejado_no_da_diferencias()
    {
        var factura = EnCartera(Guid.NewGuid());
        var nc = Guid.NewGuid();
        var rep = Guid.NewGuid();
        var dia = new ComprobantesDelDia([Factura(factura.FacturaVentaId)],
        [
            Aplicacion(TipoAplicacionFacturacion.NotaCredito, nc, factura.FacturaVentaId, 160m),
            Aplicacion(TipoAplicacionFacturacion.ReciboPago, rep, factura.FacturaVentaId, 500m),
        ]);
        MovimientoEnCartera[] movimientos =
        [
            new(factura.Id, TipoMovimientoCartera.NotaCredito, nc, 160m, false),
            new(factura.Id, TipoMovimientoCartera.Pago, rep, 500m, false),
        ];

        ComparadorConciliacion.Comparar(dia, [new(factura.Id, factura.FacturaVentaId, factura.Total, factura.Estado)], movimientos)
            .Should().BeEmpty();
    }

    [Fact]
    public void Rep_y_cobro_sin_movimiento_son_pago_no_reflejado()
    {
        var factura = EnCartera(Guid.NewGuid());
        var dia = new ComprobantesDelDia([],
        [
            Aplicacion(TipoAplicacionFacturacion.ReciboPago, Guid.NewGuid(), factura.FacturaVentaId),
            Aplicacion(TipoAplicacionFacturacion.CobroMostrador, Guid.NewGuid(), factura.FacturaVentaId),
        ]);

        Comparar(dia, factura).Select(d => (d.Tipo, d.TipoComprobante)).Should().Equal(
            (TipoDiferenciaConciliacion.PagoNoReflejado, "REP"),
            (TipoDiferenciaConciliacion.PagoNoReflejado, "Cobro de mostrador"));
    }

    [Fact]
    public void Cancelada_en_facturacion_y_vigente_en_cartera_es_cancelacion_pendiente()
    {
        var factura = EnCartera(Guid.NewGuid());
        var rep = Guid.NewGuid();
        var dia = new ComprobantesDelDia([Factura(factura.FacturaVentaId, cancelada: true)],
            [Aplicacion(TipoAplicacionFacturacion.ReciboPago, rep, factura.FacturaVentaId, 500m, cancelada: true)]);

        var diferencias = ComparadorConciliacion.Comparar(dia,
            [new(factura.Id, factura.FacturaVentaId, factura.Total, factura.Estado)],
            [new(factura.Id, TipoMovimientoCartera.Pago, rep, 500m, Revertido: false)]);

        diferencias.Select(d => d.Tipo).Should().Equal(
            TipoDiferenciaConciliacion.CancelacionPendiente, TipoDiferenciaConciliacion.CancelacionPendiente);
    }

    [Fact]
    public void Cancelacion_ya_reflejada_o_nunca_proyectada_no_es_diferencia()
    {
        var factura = EnCartera(Guid.NewGuid());
        factura.Cancelar();
        var rep = Guid.NewGuid();
        var dia = new ComprobantesDelDia(
            [Factura(factura.FacturaVentaId, cancelada: true), Factura(Guid.NewGuid(), cancelada: true)],
            [Aplicacion(TipoAplicacionFacturacion.ReciboPago, rep, factura.FacturaVentaId, 500m, cancelada: true)]);

        ComparadorConciliacion.Comparar(dia,
                [new(factura.Id, factura.FacturaVentaId, factura.Total, factura.Estado)],
                [new(factura.Id, TipoMovimientoCartera.Pago, rep, 500m, Revertido: true)])
            .Should().BeEmpty();
    }

    [Fact]
    public void Importe_distinto_y_cancelado_solo_en_cartera()
    {
        var conOtroTotal = EnCartera(Guid.NewGuid(), total: 1_000m);
        var canceladaEnCartera = EnCartera(Guid.NewGuid());
        canceladaEnCartera.Cancelar();

        var diferencias = Comparar(
            new([Factura(conOtroTotal.FacturaVentaId), Factura(canceladaEnCartera.FacturaVentaId)], []),
            conOtroTotal, canceladaEnCartera);

        diferencias.Select(d => (d.Tipo, d.ImporteCartera)).Should().Equal(
            (TipoDiferenciaConciliacion.ImporteDistinto, 1_000m),
            (TipoDiferenciaConciliacion.CanceladoSoloEnCartera, 1_160m));
    }

    [Fact]
    public async Task C77b_dia_sin_diferencias_deja_registro_de_la_corrida()
    {
        await using var db = Db();
        var factura = EnCartera(Guid.NewGuid());
        db.FacturasCartera.Add(factura);
        await db.SaveChangesAsync();
        var puerto = new PuertoFijo(new([Factura(factura.FacturaVentaId)], []));

        var r = await Handler(db, puerto, empresa: Empresa)
            .Handle(new ConciliarCarteraCommand(Dia, OrigenConciliacion.Demanda), CancellationToken.None);

        var corrida = r.Should().ContainSingle().Subject;
        corrida.TotalDiferencias.Should().Be(0);
        corrida.ComprobantesRevisados.Should().Be(1);
        corrida.Origen.Should().Be("Demanda");
        corrida.EjecutadaPor.Should().Be(Usuario.Id);
        puerto.DiaConsultado.Should().Be(Dia);
        var guardada = await db.ConciliacionesCartera.Include(c => c.Diferencias).SingleAsync();
        guardada.Fecha.Should().Be(Dia);
        guardada.Diferencias.Should().BeEmpty();
    }

    [Fact]
    public async Task Corrida_con_diferencias_las_guarda_y_el_detalle_las_lista_por_cliente()
    {
        await using var db = Db();
        var puerto = new PuertoFijo(new([Factura(Guid.NewGuid())], []));

        var r = await Handler(db, puerto, empresa: Empresa)
            .Handle(new ConciliarCarteraCommand(Dia, OrigenConciliacion.Demanda), CancellationToken.None);

        r.Single().TotalDiferencias.Should().Be(1);
        var detalle = await new ConciliacionDetalleHandler(db).Handle(new(r.Single().Id), CancellationToken.None);
        detalle.Reporte.Titulo.Should().Be("Conciliación de cartera del 02/11/2026");
        var fila = detalle.Reporte.Filas.Should().ContainSingle().Subject;
        fila["tipo"].Should().Be("Factura no está en cartera");
        fila["cliente"].Should().Be("Vidrios del Golfo");
        fila["folio"].Should().Be("FV-100");
    }

    [Fact]
    public async Task Cada_corrida_queda_registrada_sin_sobrescribir_la_anterior()
    {
        await using var db = Db();
        var handler = Handler(db, new PuertoFijo(new([], [])), empresa: Empresa);

        await handler.Handle(new ConciliarCarteraCommand(Dia, OrigenConciliacion.Demanda), CancellationToken.None);
        await handler.Handle(new ConciliarCarteraCommand(Dia, OrigenConciliacion.Demanda), CancellationToken.None);

        (await new ListarConciliacionesHandler(db).Handle(new(null, null), CancellationToken.None)).Should().HaveCount(2);
    }

    [Fact]
    public async Task La_lista_pone_primero_la_corrida_ejecutada_mas_reciente_no_el_dia_mas_reciente()
    {
        await using var db = Db();
        var temprano = Timbrado.AddHours(1);
        db.ConciliacionesCartera.AddRange(
            ConciliacionCartera.Registrar(Empresa, Dia, OrigenConciliacion.Programada, null, temprano, 0, []),
            ConciliacionCartera.Registrar(Empresa, Dia.AddDays(-20), OrigenConciliacion.Demanda, Usuario.Id, temprano.AddHours(3), 0, []),
            ConciliacionCartera.Registrar(Empresa, Dia.AddDays(-5), OrigenConciliacion.Demanda, Usuario.Id, temprano.AddHours(2), 0, []));
        await db.SaveChangesAsync();

        var lista = await new ListarConciliacionesHandler(db).Handle(new(null, null), CancellationToken.None);

        lista.Select(c => c.Fecha).Should().Equal(Dia.AddDays(-20), Dia.AddDays(-5), Dia);
    }

    [Fact]
    public async Task Programada_en_bypass_concilia_cada_empresa_y_registra_sin_usuario()
    {
        await using var db = Db();
        var otra = Guid.NewGuid();
        db.FacturasCartera.Add(EnCartera(Guid.NewGuid()));
        await db.SaveChangesAsync();
        var puerto = new PuertoFijo(new([Factura(Guid.NewGuid()) with { EmpresaId = otra }], []));

        var r = await Handler(db, puerto, empresa: null)
            .Handle(new ConciliarCarteraCommand(Dia, OrigenConciliacion.Programada), CancellationToken.None);

        r.Should().HaveCount(2);
        r.Should().OnlyContain(c => c.EjecutadaPor == null && c.Origen == "Programada");
        r.Sum(c => c.TotalDiferencias).Should().Be(1);
    }

    [Theory]
    [InlineData("2026-11-03T11:59:00Z", null)]          // 05:59 en Mérida: todavía no toca
    [InlineData("2026-11-03T12:00:00Z", "2026-11-02")]  // 06:00 en Mérida: concilia ayer
    [InlineData("2026-11-03T03:00:00Z", "2026-11-01")]  // 21:00 del día 2 en Mérida (ya es 3 en UTC): su ayer es el 1
    public void El_worker_concilia_el_dia_anterior_desde_la_hora_local(string utc, string? esperado)
    {
        ConciliacionCarteraWorker.DiaPendiente(DateTimeOffset.Parse(utc), horaLocal: 6)
            .Should().Be(esperado is null ? null : DateOnly.Parse(esperado));
    }

    [Fact]
    public void El_dia_contable_es_el_de_Merida_no_el_de_UTC()
    {
        DiaContableCxc.De(new DateTimeOffset(2026, 11, 3, 3, 0, 0, TimeSpan.Zero)).Should().Be(new DateOnly(2026, 11, 2));
    }

    [Fact]
    public void No_se_concilia_un_dia_que_no_ha_llegado()
    {
        var validator = new ConciliarCarteraValidator(new Reloj(new DateTimeOffset(2026, 11, 3, 3, 0, 0, TimeSpan.Zero)));

        validator.Validate(new ConciliarCarteraCommand(new DateOnly(2026, 11, 3), OrigenConciliacion.Demanda)).IsValid.Should().BeFalse();
        validator.Validate(new ConciliarCarteraCommand(new DateOnly(2026, 11, 2), OrigenConciliacion.Demanda)).IsValid.Should().BeTrue();
    }

    private static CuentasPorCobrarDbContext Db() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>().UseInMemoryDatabase($"cxc_conciliacion_{Guid.NewGuid()}").Options,
            new EmpresaContexto(null));

    private static ConciliarCarteraHandler Handler(CuentasPorCobrarDbContext db, IConciliacionFacturacionReadPort puerto, Guid? empresa) =>
        new(db, puerto, new EmpresaContexto(empresa), Usuario, new Reloj(Timbrado.AddDays(1)));

    private static readonly UsuarioFijo Usuario = new(Guid.NewGuid());

    private sealed class PuertoFijo(ComprobantesDelDia dia) : IConciliacionFacturacionReadPort
    {
        public DateOnly? DiaConsultado { get; private set; }

        public Task<ComprobantesDelDia> ConsultarDiaAsync(DateOnly fecha, CancellationToken cancellationToken)
        {
            DiaConsultado = fecha;
            return Task.FromResult(dia);
        }
    }

    private sealed class Reloj(DateTimeOffset ahora) : IClock
    {
        public DateTimeOffset UtcNow => ahora;
    }

    private sealed class UsuarioFijo(Guid id) : ICurrentUserContext
    {
        public Guid Id => id;
        public Guid? UserId => id;
        public string? UserName => "cxc";
    }

    private sealed class EmpresaContexto(Guid? actual) : ICurrentEmpresaContext
    {
        public Guid? Current => actual;
        public bool IsBypassed => actual is null;
        public IDisposable Bypass() => new Nada();
        private sealed class Nada : IDisposable { public void Dispose() { } }
    }
}
