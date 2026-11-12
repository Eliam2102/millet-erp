using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Liquidacion;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Application.Salidas;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Workers;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.UnitTests.Application;

/// <summary>Reglas de la hoja de salida confirmadas por Millet (Guadalupe Polanco, 9-nov-2026).</summary>
public sealed class ReglasSalidaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero); // jueves 12:00 en Ciudad de México
    private static readonly Guid Preparador = Guid.Parse("00000000-0000-0000-0000-0000000000cc");

    private sealed class Facturacion : IFacturacionSalidaPort
    {
        public List<FacturaPedidoSalida> Facturas { get; } = [];
        public Task<IReadOnlyList<FacturaPedidoSalida>> FacturasDePedidosAsync(IReadOnlyList<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FacturaPedidoSalida>>([.. Facturas.Where(f => ids.Contains(f.PedidoFacturableId))]);

        public Task<bool> CartaPorteVigenteAsync(Guid cartaPorteId, CancellationToken ct) => Task.FromResult(CartaPorteVigente);
        public bool CartaPorteVigente { get; set; }
        public List<(Guid FacturaId, IReadOnlyList<(int Posicion, int Piezas)> Piezas)> NotasDevolucion { get; } = [];
        public Task<DocumentoFiscalPt> EmitirNotaCreditoDevolucionAsync(Guid facturaId, IReadOnlyList<(int Posicion, int Piezas)> piezas, string descripcion, CancellationToken ct)
        {
            NotasDevolucion.Add((facturaId, piezas));
            return Task.FromResult(new DocumentoFiscalPt(Guid.NewGuid(), "NCRED-1", EstadoNota, EstadoNota != "Timbrado"));
        }
        public string EstadoNota { get; set; } = "Timbrado";
        public Task<DocumentoFiscalPt> SolicitarCancelacionFacturaAsync(Guid facturaId, string motivoSat, string? uuidSustituto, CancellationToken ct) =>
            Task.FromResult(new DocumentoFiscalPt(facturaId, "F-1", "CancelacionPendiente", true));
        public Task<DocumentoFiscalPt?> EstadoDocumentoAsync(bool notaCredito, Guid id, CancellationToken ct) =>
            Task.FromResult<DocumentoFiscalPt?>(new DocumentoFiscalPt(id, "NCRED-1", EstadoNota, EstadoNota != "Timbrado"));
        public Task RegistrarSalidaAsync(Guid pedidoFacturableId, string referencia, IReadOnlyList<(int Posicion, decimal Cantidad)> lineas, CancellationToken ct) => Task.CompletedTask;
        public Task<CancelacionSalidaPt> CancelarSalidaAsync(string referencia, string motivo, CancellationToken ct) => Task.FromResult(new CancelacionSalidaPt(false, false, false));
        public Task<CartaPorteSalida> EmitirCartaPorteAsync(SolicitudCartaPorte solicitud, CancellationToken ct) => Task.FromResult(new CartaPorteSalida(Guid.NewGuid(), "CP-1"));
        public Task<IReadOnlyDictionary<string, FacturaAlbaranRuta>> FacturasDeAlbaranesAsync(IReadOnlyList<string> albaranFolios, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, FacturaAlbaranRuta>>(new Dictionary<string, FacturaAlbaranRuta>());
        public Task<IReadOnlyList<CobroRutaRegistrado>> RegistrarCobrosRutaAsync(Guid sucursalId, IReadOnlyList<CobroRutaAlbaran> cobros, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CobroRutaRegistrado>>([]);
    }

    private sealed class Entorno(EntornoPt t) : IDisposable
    {
        public EntornoPt T { get; } = t;
        public Facturacion Facturacion { get; } = new();
        private static PtAcceso Acceso(Guid usuario) => new(new UsuarioFijo(usuario), new PermisosTodos(), new SucursalesTodas());
        public CrearHojaSalidaHandler Crear() => new(T.Db, Acceso(Preparador), T.Pedidos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public EmitirHojaSalidaHandler Emitir() => new(T.Db, Acceso(Preparador), T.Pedidos, Facturacion, T.Eventos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public CargarPiezaHojaHandler Cargar(Guid? usuario = null) => new(T.Db, Acceso(usuario ?? Preparador), T.Pedidos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public AutorizarHojaSalidaHandler Autorizar(Guid usuario) => new(T.Db, Acceso(usuario), T.Pedidos, Facturacion, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public ObtenerHojaSalidaHandler Obtener() => new(T.Db, Acceso(Preparador), T.Pedidos, Facturacion, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public async Task<Guid> PedidoFacturableAsync() => (await T.Db.PedidosPt.SingleAsync()).PedidoFacturableId;
        public void Dispose() => T.Dispose();
    }

    private static async Task<Entorno> Preparar(int piezas = 3)
    {
        var t = new EntornoPt();
        t.Saldos.Saldos["P1"] = 0m;
        t.AgregarPedido("P1", 1, piezas);
        for (var i = 1; i <= piezas; i++) await t.Escanear($"P1-E{i}", "P1", 1, i.ToString());
        return new Entorno(t);
    }

    private static CrearHojaSalidaCommand Hoja(params string[] etiquetas) =>
        new(EntornoPt.Sucursal, "Obra Mérida norte", false, null, null, null, null, null, null, null, null, [new PedidoCargadoDto("P1", etiquetas)]);

    private static AutorizarHojaSalidaCommand Autorizacion(Guid hoja, string nivel, string? suplencia = null) => new(hoja, nivel, "Cliente con obra detenida", suplencia);

    // ---------------------------------------------------------------- Regla 1 y 3: factura

    [Fact]
    public void Factura_timbrada_de_hoy_o_antes_vale_y_la_de_manana_no()
    {
        var hoy = new DateOnly(2026, 11, 5);
        ReglasSalidaPt.TieneFactura([(false, false, hoy)], hoy).Should().BeTrue();
        ReglasSalidaPt.TieneFactura([(false, false, hoy.AddDays(-3))], hoy).Should().BeTrue();
        ReglasSalidaPt.TieneFactura([(false, false, hoy.AddDays(1))], hoy).Should().BeFalse();
        ReglasSalidaPt.TieneFactura([], hoy).Should().BeFalse();
    }

    [Fact]
    public void PendientePedimento_solo_vale_en_exportacion()
    {
        var hoy = new DateOnly(2026, 11, 5);
        ReglasSalidaPt.TieneFactura([(true, true, null)], hoy).Should().BeTrue();
        ReglasSalidaPt.TieneFactura([(true, false, null)], hoy).Should().BeFalse();
    }

    [Fact]
    public async Task Sin_factura_no_se_emite_y_con_factura_timbrada_hoy_si()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja("P1-E1", "P1-E2", "P1-E3"), default);

        (await e.Invoking(x => x.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default)).Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("PT_SALIDA_SIN_FACTURA");
        (await e.T.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.Cargada)).Should().BeTrue("bloquear no descuenta nada");

        e.Facturacion.Facturas.Add(new(await e.PedidoFacturableAsync(), false, false, Ahora.AddHours(-1)));
        (await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default)).Estado.Should().Be("Emitida");
    }

    [Fact]
    public async Task Factura_timbrada_con_fecha_de_manana_bloquea()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja("P1-E1", "P1-E2", "P1-E3"), default);
        e.Facturacion.Facturas.Add(new(await e.PedidoFacturableAsync(), false, false, Ahora.AddDays(1)));

        (await e.Invoking(x => x.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default)).Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("PT_SALIDA_SIN_FACTURA");
    }

    [Fact]
    public async Task Exportacion_con_factura_en_PendientePedimento_sale_sin_bloqueo()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja("P1-E1", "P1-E2", "P1-E3"), default);
        e.Facturacion.Facturas.Add(new(await e.PedidoFacturableAsync(), true, true, null));

        (await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default)).Estado.Should().Be("Emitida");
    }

    // ---------------------------------------------------------------- Regla 2: excepción y suplencia

    [Fact]
    public async Task Administracion_autoriza_la_excepcion_y_la_hoja_sale_sin_factura()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja("P1-E1", "P1-E2", "P1-E3"), default);

        var autorizada = await e.Autorizar(Guid.NewGuid()).Handle(Autorizacion(hoja.Id, "administracion"), default);
        autorizada.Reglas!.SinFactura.Should().Equal("P1:3");
        autorizada.Reglas.ExcepcionFacturaVigente.Should().BeTrue();
        (await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default)).Estado.Should().Be("Emitida");
    }

    [Fact]
    public async Task Quien_armo_la_hoja_no_puede_autorizarla()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja("P1-E1", "P1-E2", "P1-E3"), default);

        (await e.Invoking(x => x.Autorizar(Preparador).Handle(Autorizacion(hoja.Id, "administracion"), default)).Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("PT_SALIDA_SEPARACION_FUNCIONES");
    }

    [Fact]
    public async Task Quien_cargo_una_pieza_tampoco_puede_autorizarla()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja(), default);
        var cargador = Guid.NewGuid();
        await e.Cargar(cargador).Handle(new CargarPiezaHojaCommand(hoja.Id, "P1-E1"), default);
        // Preparador armó la ruta; quien solo cargó una pieza igual queda fuera.
        (await e.Invoking(x => x.Autorizar(cargador).Handle(Autorizacion(hoja.Id, "credito"), default)).Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("PT_SALIDA_SEPARACION_FUNCIONES");
    }

    [Fact]
    public async Task Planeacion_y_Contraloria_suplen_solo_con_el_motivo_de_la_ausencia()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja("P1-E1", "P1-E2", "P1-E3"), default);

        (await e.Invoking(x => x.Autorizar(Guid.NewGuid()).Handle(Autorizacion(hoja.Id, "planeacion"), default)).Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("PT_MOTIVO_INVALIDO");
        var dto = await e.Autorizar(Guid.NewGuid()).Handle(Autorizacion(hoja.Id, "contraloria", "Director de Administración de vacaciones"), default);
        dto.Reglas!.ExcepcionFactura!.Suplencia.Should().Be("Director de Administración de vacaciones");
        (await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default)).Estado.Should().Be("Emitida");
    }

    [Fact]
    public async Task Autorizar_lo_que_no_hace_falta_se_rechaza()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja("P1-E1", "P1-E2", "P1-E3"), default);
        e.Facturacion.Facturas.Add(new(await e.PedidoFacturableAsync(), false, false, Ahora));

        (await e.Invoking(x => x.Autorizar(Guid.NewGuid()).Handle(Autorizacion(hoja.Id, "administracion"), default)).Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("PT_SALIDA_AUTORIZACION_INNECESARIA");
        (await e.Invoking(x => x.Autorizar(Guid.NewGuid()).Handle(Autorizacion(hoja.Id, "credito"), default)).Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("PT_SALIDA_AUTORIZACION_INNECESARIA", "el pedido sale completo");
    }

    // ---------------------------------------------------------------- Regla 4: salida parcial

    [Fact]
    public void Parcial_compara_lo_cargado_mas_lo_ya_salido_contra_lo_pedido()
    {
        ReglasSalidaPt.EsParcial([(10, 8, 0)]).Should().BeTrue();
        ReglasSalidaPt.EsParcial([(10, 10, 0)]).Should().BeFalse();
        ReglasSalidaPt.EsParcial([(10, 2, 8)]).Should().BeFalse("el resto de una salida anterior completa el pedido");
        ReglasSalidaPt.EsParcial([(5, 5, 0), (3, 0, 0)]).Should().BeTrue("una posición sin cargar");
    }

    [Fact]
    public async Task Parcial_sin_autorizar_bloquea_y_con_Credito_sale()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja("P1-E1", "P1-E2"), default);
        e.Facturacion.Facturas.Add(new(await e.PedidoFacturableAsync(), false, false, Ahora));

        (await e.Invoking(x => x.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default)).Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("PT_SALIDA_PARCIAL_SIN_AUTORIZAR");
        var detalle = await e.Obtener().Handle(new ObtenerHojaSalidaQuery(hoja.Id), default);
        detalle.Reglas!.Parciales.Should().Equal("P1:2");

        await e.Autorizar(Guid.NewGuid()).Handle(Autorizacion(hoja.Id, "credito"), default);
        (await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default)).Estado.Should().Be("Emitida");
    }

    [Fact]
    public async Task Ventas_suple_a_Credito_con_motivo_y_la_autorizacion_caduca_si_cambia_la_carga()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja("P1-E1"), default);
        e.Facturacion.Facturas.Add(new(await e.PedidoFacturableAsync(), false, false, Ahora));

        await e.Autorizar(Guid.NewGuid()).Handle(Autorizacion(hoja.Id, "ventas", "Crédito y Cobranza sin personal hoy"), default);
        await e.Cargar().Handle(new CargarPiezaHojaCommand(hoja.Id, "P1-E2"), default);

        (await e.Invoking(x => x.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default)).Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("PT_SALIDA_PARCIAL_SIN_AUTORIZAR", "se autorizó P1 con 1 pieza y ahora lleva 2");
    }

    [Fact]
    public async Task El_resto_de_un_pedido_ya_embarcado_en_parte_no_es_parcial()
    {
        using var e = await Preparar();
        e.Facturacion.Facturas.Add(new(await e.PedidoFacturableAsync(), false, false, Ahora));
        var primera = await e.Crear().Handle(Hoja("P1-E1", "P1-E2"), default);
        await e.Autorizar(Guid.NewGuid()).Handle(Autorizacion(primera.Id, "credito"), default);
        await e.Emitir().Handle(new EmitirHojaSalidaCommand(primera.Id), default);

        var resto = await e.Crear().Handle(Hoja("P1-E3"), default);
        (await e.Emitir().Handle(new EmitirHojaSalidaCommand(resto.Id), default)).Estado.Should().Be("Emitida");
    }

    // ---------------------------------------------------------------- Regla 5: liquidación del chofer

    private static readonly TimeZoneInfo Merida = TimeZoneInfo.FindSystemTimeZoneById("America/Merida");
    private static readonly TimeZoneInfo Cancun = TimeZoneInfo.FindSystemTimeZoneById("America/Cancun");
    private static readonly HashSet<DateOnly> SinFestivos = [];

    [Fact]
    public void Sucursal_liquida_hasta_el_siguiente_dia_habil_a_las_13_y_Conkal_dos_dias()
    {
        var martes1000 = new DateTimeOffset(2026, 11, 10, 16, 0, 0, TimeSpan.Zero); // 10:00 en Mérida
        ReglasSalidaPt.LimiteLiquidacion(martes1000, Merida, 1, SinFestivos).Should().Be(new DateTimeOffset(2026, 11, 11, 19, 0, 0, TimeSpan.Zero));
        ReglasSalidaPt.LimiteLiquidacion(martes1000, Merida, 2, SinFestivos).Should().Be(new DateTimeOffset(2026, 11, 12, 19, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Sabado_cuenta_como_habil_domingo_y_festivo_no()
    {
        var viernes = new DateTimeOffset(2026, 11, 13, 16, 0, 0, TimeSpan.Zero);
        ReglasSalidaPt.LimiteLiquidacion(viernes, Merida, 1, SinFestivos).Should().Be(new DateTimeOffset(2026, 11, 14, 19, 0, 0, TimeSpan.Zero), "sábado 13:00");
        var sabado = new DateTimeOffset(2026, 11, 14, 16, 0, 0, TimeSpan.Zero);
        // 16-nov-2026 es festivo: del sábado se salta domingo y lunes.
        ReglasSalidaPt.LimiteLiquidacion(sabado, Merida, 1, new HashSet<DateOnly> { new(2026, 11, 16) })
            .Should().Be(new DateTimeOffset(2026, 11, 17, 19, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Cancun_usa_su_propia_hora_de_cierre_de_caja()
    {
        // 23:30 UTC = 17:30 en Mérida (antes del cierre) y 18:30 en Cancún (después del cierre: cuenta como del día siguiente).
        var salida = new DateTimeOffset(2026, 11, 10, 23, 30, 0, TimeSpan.Zero);
        ReglasSalidaPt.LimiteLiquidacion(salida, Merida, 1, SinFestivos).Should().Be(new DateTimeOffset(2026, 11, 11, 19, 0, 0, TimeSpan.Zero), "miércoles 13:00 de Mérida");
        ReglasSalidaPt.LimiteLiquidacion(salida, Cancun, 1, SinFestivos).Should().Be(new DateTimeOffset(2026, 11, 12, 18, 0, 0, TimeSpan.Zero), "jueves 13:00 de Cancún");
    }

    [Fact]
    public void Alerta_a_las_12_horas_habiles_sin_contar_el_domingo()
    {
        var martes1000 = new DateTimeOffset(2026, 11, 10, 16, 0, 0, TimeSpan.Zero);
        ReglasSalidaPt.AlertaLiquidacion(martes1000, Merida, SinFestivos).Should().Be(martes1000.AddHours(12));
        var sabado2000 = new DateTimeOffset(2026, 11, 15, 2, 0, 0, TimeSpan.Zero); // sábado 20:00 en Mérida: 4 h del sábado + 8 h del lunes
        ReglasSalidaPt.AlertaLiquidacion(sabado2000, Merida, SinFestivos).Should().Be(new DateTimeOffset(2026, 11, 16, 14, 0, 0, TimeSpan.Zero));
        ReglasSalidaPt.EstadoPlazo(martes1000.AddHours(13), martes1000.AddHours(12), martes1000.AddDays(1)).Should().Be("Alerta");
    }

    [Fact]
    public void Conkal_toma_sus_dias_de_la_configuracion_por_clave()
    {
        var opciones = new CierreContraEntregaOptions { DiasLiquidacionPorSucursal = new(StringComparer.OrdinalIgnoreCase) { ["MID"] = 2 } };
        (opciones.DiasLiquidacion("mid"), opciones.DiasLiquidacion("CIR"), opciones.DiasLiquidacion(null)).Should().Be((2, 1, 1));
    }

    [Fact]
    public async Task El_worker_avisa_a_CxC_la_contra_entrega_solo_al_cumplir_12_horas_habiles()
    {
        var t = new EntornoPt();
        t.Saldos.Saldos["P1"] = 0m;
        t.AgregarPedido("P1", 1, 1, condicionPago: "Crédito 24 horas");
        t.Pedidos.Pedidos["P1"] = t.Pedidos.Pedidos["P1"] with { ClienteId = Guid.NewGuid(), Total = 1000m };
        await t.Escanear("P1-E1", "P1", 1, "1");
        using var e = new Entorno(t);
        e.Facturacion.Facturas.Add(new(await e.PedidoFacturableAsync(), false, false, Ahora));
        var hoja = await e.Crear().Handle(Hoja("P1-E1"), default);
        await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default);

        CerrarDiaContraEntregaHandler Worker(DateTimeOffset ahora) => new(t.Db, t.Pedidos, t.Eventos, new RelojFijo(ahora), new CalendarioFalso(), Options.Create(new CierreContraEntregaOptions()));
        (await Worker(Ahora.AddHours(11)).Handle(new CerrarDiaContraEntregaCommand(PorAlerta: true), default)).Should().Be(0);
        (await Worker(Ahora.AddHours(12)).Handle(new CerrarDiaContraEntregaCommand(PorAlerta: true), default)).Should().Be(1);
        t.Eventos.Eventos.OfType<ContraEntregaNoLiquidadaEvent>().Should().ContainSingle();
    }
}
