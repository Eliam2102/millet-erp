using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Application.Revaluacion;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.UnitTests.Revaluacion;

/// <summary>
/// CXP-12 · criterios de la ficha: CA10.8 (cierre de enero con saldos en USD, diferencia no realizada y reversa el 1 de febrero),
/// CXP-12-a (1,000 USD a 18.00 con TC de cierre 18.50 = pérdida de $500 y su reversa) y CXP-12-b (factura pagada antes del cierre
/// no se revalúa). Datos ficticios; el TC sale de un simulador, nunca de Banxico.
/// </summary>
public sealed class RevaluacionCierreTests : IDisposable
{
    private static readonly DateOnly CierreEnero = new(2026, 1, 31);
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _sucursal = Guid.NewGuid();
    private readonly Guid _proveedor = Guid.NewGuid();
    private readonly Reloj _reloj = new(new(2026, 1, 31, 20, 0, 0, TimeSpan.Zero));
    private readonly Publicador _eventos = new();
    private readonly Tc _tc = new();
    private readonly CuentasPorPagarDbContext _db;

    public RevaluacionCierreTests()
    {
        _tc.Valores[("USD", CierreEnero)] = 18.50m;
        _db = new(new DbContextOptionsBuilder<CuentasPorPagarDbContext>().UseInMemoryDatabase($"cxp-revaluacion-{Guid.NewGuid():N}").Options,
            new EmpresaContexto(_empresa), new PeriodoAbiertoStub(), _reloj);
    }

    public void Dispose() => _db.Dispose();

    private EjecutarRevaluacionCierreHandler Ejecutar() => new(_db, new EmpresaContexto(_empresa), _reloj, _tc, _eventos);
    private PublicarReversasRevaluacionHandler Reversar() => new(_db, _reloj, _eventos);

    private Domain.FacturaProveedor.FacturaProveedor Factura(decimal total, string moneda, decimal? tc, string folio = "FA-100", Guid? sucursal = null)
    {
        var ahora = new DateTimeOffset(2026, 1, 10, 15, 0, 0, TimeSpan.Zero);
        var f = Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(_empresa, null, null, _proveedor, sucursal ?? _sucursal, folio, null, ahora, ahora,
            DateOnly.FromDateTime(ahora.UtcDateTime).AddDays(30), moneda, tc, total, 0, 0, 0, total, "Prueba", ahora);
        _db.FacturasProveedor.Add(f);
        return f;
    }

    [Fact]
    public async Task M317_la_vista_previa_calcula_por_proveedor_y_sucursal_sin_registrar_ni_publicar()
    {
        Factura(1000m, "USD", 18.00m);
        Factura(200m, "USD", 19.00m, "FA-101");
        await _db.SaveChangesAsync();

        var r = await new VistaPreviaRevaluacionCierreHandler(_db, _tc).Handle(new(2026, 1), default);

        r.YaEjecutada.Should().BeFalse();
        r.Detalles.Should().HaveCount(2);
        r.TotalPerdida.Should().Be(500m);
        r.TotalUtilidad.Should().Be(100m);
        // Una sola póliza por proveedor y sucursal con el neto (500 − 100 = 400 de pérdida).
        var poliza = r.Polizas.Should().ContainSingle().Subject;
        (poliza.ProveedorId, poliza.SucursalId, poliza.Perdida, poliza.Utilidad).Should().Be((_proveedor, _sucursal, 400m, 0m));
        // No crea la revaluación ni publica eventos: el mes sigue disponible para ejecutarse.
        (await _db.RevaluacionesCierre.CountAsync()).Should().Be(0);
        _eventos.Eventos.Should().BeEmpty();
        var ejecucion = await Ejecutar().Handle(new(2026, 1), default);
        ejecucion.TotalPerdida.Should().Be(500m);
    }

    [Fact]
    public async Task M317_la_vista_previa_avisa_si_falta_el_tc_de_cierre_y_si_el_mes_ya_se_reevaluo()
    {
        Factura(1000m, "USD", 18.00m);
        await _db.SaveChangesAsync();
        _tc.Valores.Clear();
        var sinTc = await new VistaPreviaRevaluacionCierreHandler(_db, _tc).Handle(new(2026, 1), default);
        sinTc.Polizas.Should().BeEmpty();
        sinTc.Avisos.Should().ContainSingle().Which.Should().Contain("tipo de cambio");

        _tc.Valores[("USD", CierreEnero)] = 18.50m;
        await Ejecutar().Handle(new(2026, 1), default);
        var ya = await new VistaPreviaRevaluacionCierreHandler(_db, _tc).Handle(new(2026, 1), default);
        ya.YaEjecutada.Should().BeTrue();
        (await _db.RevaluacionesCierre.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CXP12a_mil_dolares_a_18_con_cierre_a_18_50_es_perdida_de_500_y_se_publica_para_la_poliza()
    {
        Factura(1000m, "USD", 18.00m);
        await _db.SaveChangesAsync();

        var r = await Ejecutar().Handle(new(2026, 1), default);

        r.TotalPerdida.Should().Be(500m);
        r.TotalUtilidad.Should().Be(0m);
        r.Documentos.Should().Be(1);
        r.FechaCierre.Should().Be(CierreEnero);
        var e = _eventos.Eventos.Should().ContainSingle().Which.Should().BeOfType<RevaluacionCierreRegistradaIntegrationEvent>().Subject;
        (e.ProveedorId, e.SucursalId, e.Perdida, e.Utilidad, e.FechaCierre).Should().Be((_proveedor, _sucursal, 500m, 0m, CierreEnero));
        var d = (await _db.RevaluacionesCierre.Include(x => x.Detalles).SingleAsync()).Detalles.Single();
        (d.ImporteLibros, d.ImporteCierre, d.Diferencia, d.TcFactura, d.TcCierre).Should().Be((18000m, 18500m, 500m, 18.00m, 18.50m));
    }

    [Fact]
    public async Task CXP12b_factura_pagada_antes_del_cierre_no_se_revalua()
    {
        var pagada = Factura(1000m, "USD", 18.00m, folio: "FA-PAGADA");
        pagada.Autorizar(null, new(2026, 1, 12, 0, 0, 0, TimeSpan.Zero));
        pagada.RegistrarPago(1000m, new(2026, 1, 20, 12, 0, 0, TimeSpan.Zero), "Pago");
        var abierta = Factura(400m, "USD", 18.00m, folio: "FA-ABIERTA");
        await _db.SaveChangesAsync();

        var r = await Ejecutar().Handle(new(2026, 1), default);

        r.Documentos.Should().Be(1);
        r.TotalPerdida.Should().Be(200m);
        (await _db.RevaluacionesCierre.Include(x => x.Detalles).SingleAsync()).Detalles.Select(d => d.FacturaProveedorId)
            .Should().Equal(abierta.Id);
    }

    [Fact]
    public async Task Pago_parcial_antes_del_cierre_revalua_solo_el_saldo()
    {
        var f = Factura(1000m, "USD", 18.00m);
        f.Autorizar(null, new(2026, 1, 12, 0, 0, 0, TimeSpan.Zero));
        f.RegistrarPago(600m, new(2026, 1, 20, 12, 0, 0, TimeSpan.Zero), "Pago parcial");
        await _db.SaveChangesAsync();

        (await Ejecutar().Handle(new(2026, 1), default)).TotalPerdida.Should().Be(200m);
    }

    [Fact]
    public async Task CA108_el_cierre_de_enero_se_revierte_solo_el_1_de_febrero_y_una_sola_vez()
    {
        Factura(1000m, "USD", 18.00m);
        await _db.SaveChangesAsync();
        await Ejecutar().Handle(new(2026, 1), default);
        _eventos.Eventos.Clear();

        (await Reversar().Handle(new(), default)).Should().Be(0, "el 31 de enero todavía no toca la reversa");
        _eventos.Eventos.Should().BeEmpty();

        _reloj.Ahora = new(2026, 2, 1, 15, 0, 0, TimeSpan.Zero);
        (await Reversar().Handle(new(), default)).Should().Be(1);
        var e = _eventos.Eventos.Should().ContainSingle().Which.Should().BeOfType<RevaluacionCierreRevertidaIntegrationEvent>().Subject;
        (e.ProveedorId, e.SucursalId, e.Perdida, e.Utilidad, e.FechaReversa).Should().Be((_proveedor, _sucursal, 500m, 0m, new DateOnly(2026, 2, 1)));
        (await _db.RevaluacionesCierre.SingleAsync()).ReversaPublicadaEn.Should().NotBeNull();

        _eventos.Eventos.Clear();
        (await Reversar().Handle(new(), default)).Should().Be(0, "la reversa no se publica dos veces");
        _eventos.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task Tipo_de_cambio_de_cierre_menor_es_utilidad_no_realizada()
    {
        Factura(1000m, "USD", 18.00m);
        await _db.SaveChangesAsync();
        _tc.Valores[("USD", CierreEnero)] = 17.50m;

        var r = await Ejecutar().Handle(new(2026, 1), default);

        (r.TotalPerdida, r.TotalUtilidad).Should().Be((0m, 500m));
        var e = _eventos.Eventos.Single().Should().BeOfType<RevaluacionCierreRegistradaIntegrationEvent>().Subject;
        (e.Perdida, e.Utilidad).Should().Be((0m, 500m));
    }

    [Fact]
    public async Task Un_proveedor_con_facturas_en_dos_sucursales_genera_un_evento_por_sucursal_y_su_reversa_igual()
    {
        var otra = Guid.NewGuid();
        Factura(1000m, "USD", 18.00m, folio: "A");
        Factura(100m, "USD", 18.70m, folio: "B", sucursal: otra);   // utilidad de 20
        await _db.SaveChangesAsync();

        await Ejecutar().Handle(new(2026, 1), default);

        _eventos.Eventos.Cast<RevaluacionCierreRegistradaIntegrationEvent>().Select(e => (e.SucursalId, e.Perdida, e.Utilidad))
            .Should().BeEquivalentTo([(_sucursal, 500m, 0m), (otra, 0m, 20m)]);
        _eventos.Eventos.Clear();
        _reloj.Ahora = new(2026, 2, 1, 15, 0, 0, TimeSpan.Zero);
        await Reversar().Handle(new(), default);
        _eventos.Eventos.Cast<RevaluacionCierreRevertidaIntegrationEvent>().Select(e => (e.SucursalId, e.Perdida, e.Utilidad))
            .Should().BeEquivalentTo([(_sucursal, 500m, 0m), (otra, 0m, 20m)]);
    }

    [Fact]
    public async Task Las_diferencias_de_un_proveedor_se_netean_en_un_solo_evento_y_pesos_o_sin_tc_no_entran()
    {
        Factura(1000m, "USD", 18.00m, folio: "A");
        Factura(500m, "USD", 18.90m, folio: "B");      // utilidad de 200
        Factura(900m, "MXN", null, folio: "MXN");      // pesos: no se revalúa
        var sinTc = Factura(100m, "USD", null, folio: "SIN-TC");
        await _db.SaveChangesAsync();

        var r = await Ejecutar().Handle(new(2026, 1), default);

        r.Documentos.Should().Be(2);
        r.Omitidas.Should().ContainSingle().Which.FacturaId.Should().Be(sinTc.Id);
        var e = _eventos.Eventos.Should().ContainSingle().Which.Should().BeOfType<RevaluacionCierreRegistradaIntegrationEvent>().Subject;
        (e.Perdida, e.Utilidad).Should().Be((300m, 0m));
        (r.TotalPerdida, r.TotalUtilidad).Should().Be((500m, 200m));
    }

    [Fact]
    public async Task Sin_tipo_de_cambio_de_cierre_no_inventa_uno_ni_deja_nada()
    {
        Factura(1000m, "EUR", 20m);
        await _db.SaveChangesAsync();

        var falla = () => Ejecutar().Handle(new(2026, 1), default);

        (await falla.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("REVALUACION_SIN_TC_CIERRE");
        _eventos.Eventos.Should().BeEmpty();
        (await _db.RevaluacionesCierre.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Un_mes_se_revalua_una_sola_vez_y_no_antes_de_que_cierre()
    {
        Factura(1000m, "USD", 18.00m);
        await _db.SaveChangesAsync();
        await Ejecutar().Handle(new(2026, 1), default);

        var repetida = () => Ejecutar().Handle(new(2026, 1), default);
        (await repetida.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("REVALUACION_YA_EJECUTADA");

        var futuro = () => Ejecutar().Handle(new(2026, 2), default);
        (await futuro.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("REVALUACION_MES_NO_CERRADO");
    }

    [Fact]
    public async Task El_reporte_lista_cada_documento_por_proveedor_con_sus_totales()
    {
        Factura(1000m, "USD", 18.00m, folio: "FA-100");
        await _db.SaveChangesAsync();
        await Ejecutar().Handle(new(2026, 1), default);

        var reporte = await new ReporteRevaluacionCierreHandler(_db, new Proveedores(), _reloj).Handle(new(2026, 1), default);

        var fila = reporte.Filas.Should().ContainSingle().Subject;
        fila["proveedor_nombre"].Should().Be("Vidrios del Norte SA de CV");
        fila["documento"].Should().Be("FA-100");
        fila["diferencia"].Should().Be(500m);
        fila["resultado"].Should().Be("Pérdida");
        reporte.Totales!["perdida"].Should().Be(500m);
        reporte.Totales["neto"].Should().Be(500m);
        var sinCorrer = () => new ReporteRevaluacionCierreHandler(_db, new Proveedores(), _reloj).Handle(new(2026, 3), default);
        (await sinCorrer.Should().ThrowAsync<EntityNotFoundException>()).Which.Code.Should().Be("REVALUACION_NO_ENCONTRADA");
    }

    private sealed class Reloj(DateTimeOffset ahora) : IClock
    {
        public DateTimeOffset Ahora { get; set; } = ahora;
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class EmpresaContexto(Guid id) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = id;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }

    private sealed class Publicador : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
        {
            Eventos.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class Tc : ITipoCambioReadPort
    {
        public Dictionary<(string Moneda, DateOnly Fecha), decimal> Valores { get; } = [];
        public Task<decimal?> ObtenerAsync(string monedaOrigen, string monedaDestino, DateOnly fecha, CancellationToken cancellationToken) =>
            Task.FromResult(Valores.TryGetValue((monedaOrigen, fecha), out var v) ? (decimal?)v : null);
    }

    private sealed class Proveedores : IProveedorReadPort
    {
        public Task<ProveedorDto?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProveedorDto?> ObtenerPorRfcAsync(string rfc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(i => i, _ => "Vidrios del Norte SA de CV"));
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerRfcsPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(i => i, _ => "AAA010101AAA"));
    }
}
