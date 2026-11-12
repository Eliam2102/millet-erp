using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Casos;
using Millet.ProductoTerminado.Application.Consultas;
using Millet.ProductoTerminado.Application.Entregas;
using Millet.ProductoTerminado.Application.Liquidacion;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Application.Salidas;
using Millet.ProductoTerminado.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Xunit;

namespace Millet.ProductoTerminado.UnitTests.Application;

/// <summary>F6 · M-251, M-245, M-247, M-248, M-351/F1-PT-06 y M-352 sobre la base en memoria.</summary>
public sealed class F6Tests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 12, 16, 0, 0, TimeSpan.Zero);
    private static readonly Guid Operador = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Otro = Guid.Parse("00000000-0000-0000-0000-0000000000c2");
    private const string Png = "iVBORw0KGgo=";

    private sealed class Facturacion : IFacturacionSalidaPort
    {
        public Dictionary<string, FacturaAlbaranRuta> PorAlbaran { get; } = new();
        public bool CartaVigente { get; set; }
        public string EstadoNota { get; set; } = "Timbrado";
        public List<(Guid Factura, IReadOnlyList<(int Posicion, int Piezas)> Piezas)> Notas { get; } = [];
        public Task RegistrarSalidaAsync(Guid p, string r, IReadOnlyList<(int Posicion, decimal Cantidad)> l, CancellationToken ct) => Task.CompletedTask;
        public Task<CancelacionSalidaPt> CancelarSalidaAsync(string r, string m, CancellationToken ct) => Task.FromResult(new CancelacionSalidaPt(true, true, false));
        public Task<CartaPorteSalida> EmitirCartaPorteAsync(SolicitudCartaPorte s, CancellationToken ct) => Task.FromResult(new CartaPorteSalida(Guid.NewGuid(), "CP-9"));
        public Task<IReadOnlyDictionary<string, FacturaAlbaranRuta>> FacturasDeAlbaranesAsync(IReadOnlyList<string> f, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, FacturaAlbaranRuta>>(PorAlbaran.Where(x => f.Contains(x.Key)).ToDictionary());
        public Task<IReadOnlyList<CobroRutaRegistrado>> RegistrarCobrosRutaAsync(Guid s, IReadOnlyList<CobroRutaAlbaran> c, CancellationToken ct) => Task.FromResult<IReadOnlyList<CobroRutaRegistrado>>([]);
        public Task<IReadOnlyList<FacturaPedidoSalida>> FacturasDePedidosAsync(IReadOnlyList<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FacturaPedidoSalida>>([.. ids.Select(i => new FacturaPedidoSalida(i, false, false, Ahora.AddDays(-1)))]);
        public Task<bool> CartaPorteVigenteAsync(Guid id, CancellationToken ct) => Task.FromResult(CartaVigente);
        public Task<DocumentoFiscalPt> EmitirNotaCreditoDevolucionAsync(Guid f, IReadOnlyList<(int Posicion, int Piezas)> p, string d, CancellationToken ct)
        {
            Notas.Add((f, p));
            return Task.FromResult(new DocumentoFiscalPt(Guid.NewGuid(), "NCRED-77", EstadoNota, EstadoNota != "Timbrado"));
        }
        public Task<DocumentoFiscalPt> SolicitarCancelacionFacturaAsync(Guid f, string m, string? u, CancellationToken ct) => Task.FromResult(new DocumentoFiscalPt(f, "F-1", "CancelacionPendiente", true));
        public Task<DocumentoFiscalPt?> EstadoDocumentoAsync(bool nc, Guid id, CancellationToken ct) => Task.FromResult<DocumentoFiscalPt?>(new DocumentoFiscalPt(id, "NCRED-77", EstadoNota, EstadoNota != "Timbrado"));
    }

    private sealed class E : IDisposable
    {
        public EntornoPt T { get; } = new();
        public Facturacion F { get; } = new();
        public PtAcceso Yo { get; } = new(new UsuarioFijo(Operador), new PermisosTodos(), new SucursalesTodas());
        public PtAcceso El { get; } = new(new UsuarioFijo(Otro), new PermisosTodos(), new SucursalesTodas());
        private readonly RelojFijo _reloj = new(Ahora);
        private static EmpresaFija Emp => new(EntornoPt.Empresa);

        public Task<HojaSalidaDto> CrearHoja(string pedido, params string[] etiquetas) => new CrearHojaSalidaHandler(T.Db, Yo, T.Pedidos, Emp, _reloj)
            .Handle(new CrearHojaSalidaCommand(EntornoPt.Sucursal, "Obra Cancún centro", false, null, null, null, null, null, null, null, null, [new PedidoCargadoDto(pedido, etiquetas)]), default);
        public Task<HojaSalidaDto> Emitir(Guid id) => new EmisorAutorizado(T.Db, T.Pedidos, F, new EmitirHojaSalidaHandler(T.Db, Yo, T.Pedidos, F, T.Eventos, Emp, _reloj), _reloj)
            .Handle(new EmitirHojaSalidaCommand(id), default);
        public Task<HojaSalidaDto> Entregar(Guid hoja, Guid alb, params string[] rechazadas) => new ConfirmarEntregaHandler(T.Db, Yo, T.Pedidos, T.Eventos, Emp, _reloj)
            .Handle(new ConfirmarEntregaCommand(hoja, alb, "Arq. Ramírez", rechazadas, rechazadas.Length > 0 ? "Canto astillado" : null, Png, null, null), default);
        public ResolverCancelacionAlbaranHandler ResolverCancelacion(PtAcceso a) => new(T.Db, a, T.Pedidos, F, Emp, _reloj);
        public SolicitarCancelacionAlbaranHandler SolicitarCancelacion() => new(T.Db, Yo, T.Pedidos, Emp, _reloj);
        public CorregirEntregaHandler Corregir() => new(T.Db, Yo, T.Pedidos, T.Eventos, Emp, _reloj);
        public SolicitarSalidaParcialHandler SolicitarParcial() => new(T.Db, Yo, Emp, _reloj);
        public ResolverSalidaParcialHandler ResolverParcial(PtAcceso a) => new(T.Db, a, T.Liberacion, T.Pedidos, _reloj);
        public RegistrarCasoDanoHandler RegistrarCaso() => new(T.Db, Yo, F, Emp, _reloj);
        public DictaminarCasoHandler Dictaminar(PtAcceso a) => new(T.Db, a, F, _reloj);
        public void Dispose() => T.Dispose();
    }

    private static async Task<E> Pedido(string numero, int esperadas, int escaneadas, decimal? saldo = 0m)
    {
        var e = new E();
        if (saldo is { } s) e.T.Saldos.Saldos[numero] = s;
        e.T.AgregarPedido(numero, 1, esperadas);
        for (var i = 1; i <= escaneadas; i++) await e.T.Escanear($"{numero}-{i}", numero, 1, i.ToString());
        return e;
    }

    // ---- M-251 ----

    [Fact]
    public async Task M251_pieza_escaneada_queda_por_ubicar_con_estacion_de_origen_y_al_ubicar_guarda_almacen()
    {
        using var e = await Pedido("10425068", 2, 2);
        var antes = await new ListarPiezasHandler(e.T.Db, e.Yo).Handle(new ListarPiezasQuery("10425068", null, 1, 50), default);
        antes.Items.Should().OnlyContain(p => p.PorUbicar && p.EstacionOrigen == 2821 && p.CostoPendiente);

        var r = await new UbicarPiezasHandler(e.T.Db, e.Yo, new RelojFijo(Ahora)).Handle(new UbicarPiezasCommand(["10425068-1"], "Nacional", "Pirámide 3"), default);
        r.Single().Almacen.Should().Be("Nacional");
        var despues = await new ListarPiezasHandler(e.T.Db, e.Yo).Handle(new ListarPiezasQuery("10425068", null, 1, 50), default);
        despues.Items.Single(p => p.Etiqueta == "10425068-1").Should().Match<PiezaDto>(p => !p.PorUbicar && p.Lugar == "Pirámide 3");
        (await e.T.Db.Movimientos.CountAsync(m => m.Tipo == TipoMovimientoPieza.Ubicacion)).Should().Be(1);
    }

    [Fact]
    public async Task M251_reescanear_no_duplica_la_entrada()
    {
        using var e = await Pedido("10425069", 1, 1);
        await e.T.Escanear("10425069-1", "10425069", 1, "1", new DateTime(2026, 10, 28, 9, 0, 0));
        (await e.T.Db.Piezas.CountAsync()).Should().Be(1);
    }

    // ---- M-245 ----

    [Fact]
    public async Task M245_ocho_de_diez_sale_solo_con_autorizacion_de_otra_persona_y_conserva_dos_pendientes()
    {
        using var e = await Pedido("80269432", 10, 8);
        var cargar = () => e.CrearHoja("80269432", "80269432-1");
        (await cargar.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_PEDIDO_NO_LIBERADO");

        var etiquetas = Enumerable.Range(1, 8).Select(i => $"80269432-{i}").ToList();
        var sol = await e.SolicitarParcial().Handle(new SolicitarSalidaParcialCommand("80269432", etiquetas, "El cliente necesita lo terminado para la obra"), default);
        var propia = () => e.ResolverParcial(e.Yo).Handle(new ResolverSalidaParcialCommand(sol.Id, true, "credito", null, "Autorizo la parcialidad"), default);
        (await propia.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_SALIDA_SEPARACION_FUNCIONES");

        var ok = await e.ResolverParcial(e.El).Handle(new ResolverSalidaParcialCommand(sol.Id, true, "credito", null, "Cliente al corriente"), default);
        ok.Estado.Should().Be("Autorizada");
        var hoja = await e.CrearHoja("80269432", [.. etiquetas]);
        var emitida = await e.Emitir(hoja.Id);
        emitida.Albaranes.Single().Total.Should().Be(8);
        var avance = await new ListarPiezasHandler(e.T.Db, e.Yo).Handle(new ListarPiezasQuery("80269432", null, 1, 50), default);
        avance.Avance.Single().Pendientes.Should().Be(2);
    }

    [Fact]
    public async Task M245_solicitud_rechazada_no_libera_nada()
    {
        using var e = await Pedido("80269433", 10, 8);
        var sol = await e.SolicitarParcial().Handle(new SolicitarSalidaParcialCommand("80269433", ["80269433-1"], "Prueba de rechazo de parcialidad"), default);
        (await e.ResolverParcial(e.El).Handle(new ResolverSalidaParcialCommand(sol.Id, false, null, null, "Tiene saldo vencido"), default)).Estado.Should().Be("Rechazada");
        (await e.T.Db.PedidosPt.SingleAsync()).Liberacion.Should().BeNull();
    }

    [Fact]
    public async Task M245_pieza_fuera_de_la_autorizacion_no_se_carga()
    {
        using var e = await Pedido("80269434", 10, 8);
        var sol = await e.SolicitarParcial().Handle(new SolicitarSalidaParcialCommand("80269434", ["80269434-1", "80269434-2"], "Solo dos piezas urgentes"), default);
        await e.ResolverParcial(e.El).Handle(new ResolverSalidaParcialCommand(sol.Id, true, "ventas", "Crédito y Cobranza de vacaciones", "Autoriza Ventas"), default);
        var act = () => e.CrearHoja("80269434", "80269434-1", "80269434-3");
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_PIEZA_NO_AUTORIZADA");
    }

    [Fact]
    public async Task Reparto_sale_como_parcial_con_lo_que_ya_esta_en_69_sin_autorizacion()
    {
        var e = new E();
        using var _ = e;
        e.T.Saldos.Saldos["50001"] = 900m;
        e.T.Pedidos.Pedidos["50001"] = new PedidoPtOrigen(Guid.NewGuid(), "50001", EntornoPt.Sucursal, "Abierto", "REPARTO", 1, [new LineaPtOrigen(1, null, "Vidrio", 10, null)]);
        for (var i = 1; i <= 3; i++) await e.T.Escanear($"50001-{i}", "50001", 1, i.ToString());
        var hoja = await e.CrearHoja("50001", "50001-1", "50001-2", "50001-3");
        var emitida = await new EmitirHojaSalidaHandler(e.T.Db, e.Yo, e.T.Pedidos, e.F, e.T.Eventos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora))
            .Handle(new EmitirHojaSalidaCommand(hoja.Id), default);
        emitida.Estado.Should().Be("Emitida");
        emitida.Albaranes.Single().Total.Should().Be(3);
    }

    // ---- M-247 ----

    [Fact]
    public async Task M247_albaran_emitido_no_se_cancela_sin_solicitud_ni_por_quien_la_pidio_y_el_rechazo_lo_deja_intacto()
    {
        using var e = await Pedido("P247", 2, 2);
        var hoja = await e.Emitir((await e.CrearHoja("P247", "P247-1", "P247-2")).Id);
        var alb = hoja.Albaranes.Single();
        var directo = () => new CancelarAlbaranSalidaHandler(e.T.Db, e.Yo, e.T.Pedidos, e.F, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora))
            .Handle(new CancelarAlbaranSalidaCommand(hoja.Id, alb.Id, "Error de carga"), default);
        (await directo.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_ALBARAN_CANCELACION_SIN_SOLICITUD");

        await e.SolicitarCancelacion().Handle(new SolicitarCancelacionAlbaranCommand(hoja.Id, alb.Id, "El camión no salió de patio"), default);
        var propia = () => e.ResolverCancelacion(e.Yo).Handle(new ResolverCancelacionAlbaranCommand(hoja.Id, alb.Id, true, "Lo autorizo yo"), default);
        (await propia.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_SALIDA_SEPARACION_FUNCIONES");

        var rechazada = await e.ResolverCancelacion(e.El).Handle(new ResolverCancelacionAlbaranCommand(hoja.Id, alb.Id, false, "Sí salió, hay acuse"), default);
        rechazada.Albaranes.Single().Cancelacion!.RechazadaPorId.Should().Be(Otro);
        (await e.T.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.Embarcada)).Should().BeTrue();
    }

    [Fact]
    public async Task M247_autorizada_regresa_piezas_a_PT_y_el_historial_dice_quien_pidio_y_quien_autorizo()
    {
        using var e = await Pedido("P248", 2, 2);
        var hoja = await e.Emitir((await e.CrearHoja("P248", "P248-1", "P248-2")).Id);
        var alb = hoja.Albaranes.Single();
        await e.SolicitarCancelacion().Handle(new SolicitarCancelacionAlbaranCommand(hoja.Id, alb.Id, "Pedido equivocado en la ruta"), default);
        var r = await e.ResolverCancelacion(e.El).Handle(new ResolverCancelacionAlbaranCommand(hoja.Id, alb.Id, true, "Confirmado con Salidas"), default);
        var c = r.Albaranes.Single().Cancelacion!;
        (c.SolicitadaPorId, c.AutorizadaPorId).Should().Be((Operador, Otro));
        (await e.T.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.EnPT)).Should().BeTrue();
    }

    [Fact]
    public async Task M247_con_carta_porte_vigente_se_exige_cancelarla_primero()
    {
        using var e = await Pedido("P249", 1, 1);
        var hoja = await e.Emitir((await e.CrearHoja("P249", "P249-1")).Id);
        var s = await e.T.Db.Salidas.SingleAsync();
        s.AsignarCartaPorte(Guid.NewGuid(), "CP-123");
        await e.T.Db.SaveChangesAsync();
        e.F.CartaVigente = true;
        await e.SolicitarCancelacion().Handle(new SolicitarCancelacionAlbaranCommand(hoja.Id, hoja.Albaranes.Single().Id, "Cambio de vehículo"), default);
        var act = () => e.ResolverCancelacion(e.El).Handle(new ResolverCancelacionAlbaranCommand(hoja.Id, hoja.Albaranes.Single().Id, true, "Autorizo"), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_SALIDA_CON_CARTA_PORTE");
    }

    // ---- M-248 ----

    [Fact]
    public async Task M248_corregir_entrega_con_liquidacion_abierta_cambia_piezas_y_conserva_version_anterior()
    {
        using var e = await Pedido("P250", 3, 3);
        var hoja = await e.Emitir((await e.CrearHoja("P250", "P250-1", "P250-2", "P250-3")).Id);
        var alb = hoja.Albaranes.Single();
        await e.Entregar(hoja.Id, alb.Id);
        var r = await e.Corregir().Handle(new CorregirEntregaCommand(hoja.Id, alb.Id, "Arq. Ramírez", ["P250-3"], "Canto astillado", null, Png,
            "El albarán firmado trae una pieza rechazada"), default);
        r.Albaranes.Single().Entrega!.Should().Match<EntregaDto>(x => x.Entregadas == 2 && x.Rechazadas == 1 && x.Versiones!.Count == 1);
        (await e.T.Db.Piezas.SingleAsync(p => p.Etiqueta == "P250-3")).Estado.Should().Be(EstadoPieza.EnRevision);
        (await e.T.Db.PedidosPt.SingleAsync()).EntregadoAt.Should().BeNull();
    }

    [Fact]
    public async Task M248_con_liquidacion_cerrada_se_rechaza()
    {
        using var e = await Pedido("P251", 1, 1);
        var hoja = await e.Emitir((await e.CrearHoja("P251", "P251-1")).Id);
        var alb = hoja.Albaranes.Single();
        await e.Entregar(hoja.Id, alb.Id);
        await new LiquidarRutaPtHandler(e.T.Db, e.Yo, e.T.Pedidos, e.F, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora), e.T.Eventos)
            .Handle(new LiquidarRutaPtCommand(hoja.Id, [new LiquidacionAlbaranInput(alb.Id, true, 0, 0, null)], []), default);
        var act = () => e.Corregir().Handle(new CorregirEntregaCommand(hoja.Id, alb.Id, "X", ["P251-1"], "Rota", null, Png, "Corrección tardía"), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_LIQUIDACION_CERRADA");
    }

    // ---- M-351 / F1-PT-06 / M-352 ----

    [Fact]
    public async Task M351_sin_evidencia_no_se_dictamina_y_quien_registro_no_dictamina()
    {
        using var e = await Pedido("P351", 2, 2);
        var caso = await e.RegistrarCaso().Handle(new RegistrarCasoDanoCommand(["P351-1"], "Se golpeó con la grúa en patio", null), default);
        (await e.T.Db.Piezas.SingleAsync(p => p.Etiqueta == "P351-1")).Estado.Should().Be(EstadoPieza.EnRevision);
        var sinEvidencia = () => e.Dictaminar(e.El).Handle(new DictaminarCasoCommand(caso.Id, "Desecho", "Pieza estrellada", null), default);
        (await sinEvidencia.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_CASO_SIN_EVIDENCIA");

        await new AdjuntarEvidenciaCasoHandler(e.T.Db, e.Yo, e.F).Handle(new AdjuntarEvidenciaCasoCommand(caso.Id, Png), default);
        var propio = () => e.Dictaminar(e.Yo).Handle(new DictaminarCasoCommand(caso.Id, "Desecho", "Pieza estrellada", null), default);
        (await propio.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_CASO_SEPARACION_FUNCIONES");
    }

    [Theory]
    [InlineData("RegresarDisponible", EstadoPieza.EnPT)]
    [InlineData("Desecho", EstadoPieza.Baja)]
    [InlineData("Refabricacion", EstadoPieza.Baja)]
    [InlineData("Reproceso", EstadoPieza.EnReproceso)]
    public async Task M351_dictamen_mueve_la_pieza_con_su_movimiento(string dictamen, EstadoPieza esperado)
    {
        using var e = await Pedido("P352", 1, 1);
        var caso = await e.RegistrarCaso().Handle(new RegistrarCasoDanoCommand(["P352-1"], "Rayón en la cara exterior", Png), default);
        var r = await e.Dictaminar(e.El).Handle(new DictaminarCasoCommand(caso.Id, dictamen, "Revisada por Calidad", dictamen == "Refabricacion" ? "10500001" : null), default);
        r.Estado.Should().Be("Dictaminado");
        (await e.T.Db.Piezas.SingleAsync()).Estado.Should().Be(esperado);
        (await e.T.Db.Movimientos.AnyAsync(m => m.Referencia == caso.Id.ToString() && m.EstadoNuevo == esperado.ToString())).Should().BeTrue();
    }

    [Fact]
    public async Task M351_reproceso_vuelve_a_PT_cuando_AW_la_escanea_de_nuevo()
    {
        using var e = await Pedido("P353", 1, 1);
        var caso = await e.RegistrarCaso().Handle(new RegistrarCasoDanoCommand(["P353-1"], "Canto con desconchado", Png), default);
        await e.Dictaminar(e.El).Handle(new DictaminarCasoCommand(caso.Id, "Reproceso", "Repulir canto", null), default);
        await e.T.Escanear("P353-1", "P353", 1, "1", new DateTime(2026, 10, 29, 8, 0, 0));
        var p = await e.T.Db.Piezas.SingleAsync();
        (p.Estado, p.Almacen).Should().Be((EstadoPieza.EnPT, (AlmacenPt?)null));
    }

    [Fact]
    public async Task M352_rechazo_de_dos_piezas_facturadas_emite_la_nota_ligada_y_no_cierra_mientras_este_pendiente()
    {
        using var e = await Pedido("P354", 3, 3);
        var hoja = await e.Emitir((await e.CrearHoja("P354", "P354-1", "P354-2", "P354-3")).Id);
        var alb = hoja.Albaranes.Single();
        var factura = Guid.NewGuid();
        e.F.PorAlbaran[alb.Folio] = new FacturaAlbaranRuta(alb.Folio, factura, "FCNK-1001", 5000m, null);
        await e.Entregar(hoja.Id, alb.Id, "P354-2", "P354-3");
        var caso = await e.RegistrarCaso().Handle(new RegistrarCasoDanoCommand(["P354-2", "P354-3"], "Rechazadas por el cliente en obra", Png), default);
        caso.Etapa.Should().Be("RechazoEnSitio");
        await e.Dictaminar(e.El).Handle(new DictaminarCasoCommand(caso.Id, "Refabricacion", "No tienen reparación", "10500002"), default);

        e.F.EstadoNota = "TimbradoEnProceso";
        var conNota = await new EmitirNotaCreditoCasoHandler(e.T.Db, e.Yo, e.F).Handle(new EmitirNotaCreditoCasoCommand(caso.Id), default);
        e.F.Notas.Single().Factura.Should().Be(factura);
        e.F.Notas.Single().Piezas.Should().Equal([(1, 2)]);
        conNota.Documento!.Folio.Should().Be("NCRED-77");
        var cerrar = () => new CerrarCasoHandler(e.T.Db, e.Yo, e.F, new RelojFijo(Ahora)).Handle(new CerrarCasoCommand(caso.Id), default);
        (await cerrar.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_CASO_CORRECTIVO_PENDIENTE");

        e.F.EstadoNota = "Timbrado";
        (await cerrar()).Estado.Should().Be("Cerrado");
    }
}
