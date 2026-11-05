using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Entregas;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Application.Salidas;
using Consultas = Millet.ProductoTerminado.Application.Consultas;
using Millet.ProductoTerminado.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.UnitTests.Application;

public sealed class SalidasTests
{
    private sealed class Usuario : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.Parse("00000000-0000-0000-0000-0000000000cc");
        public string? UserName => null;
    }

    private sealed class Permisos : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken ct = default) => ValueTask.FromResult(true);
    }

    private sealed class Sucursales : IUsuarioSucursalReadPort
    {
        public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid usuarioId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([EntornoPt.Sucursal]);
        public Task<bool> EstaAsociadoAsync(Guid usuarioId, Guid sucursalId, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class FacturacionFalsa : IFacturacionSalidaPort
    {
        public List<(string Referencia, IReadOnlyList<(int Posicion, decimal Cantidad)> Lineas)> Salidas { get; } = [];
        public List<SolicitudCartaPorte> CartasPorte { get; } = [];
        public bool Falla { get; set; }

        public Task RegistrarSalidaAsync(Guid pedidoFacturableId, string referencia, IReadOnlyList<(int Posicion, decimal Cantidad)> lineas, CancellationToken ct)
        {
            if (Falla) throw new BusinessRuleException("SALIDA_EXCEDE_PEDIDO", "Excede lo pedido.");
            // Como Facturación: reenviar la misma referencia no duplica.
            if (Salidas.All(s => s.Referencia != referencia)) Salidas.Add((referencia, lineas));
            return Task.CompletedTask;
        }

        public Task<CartaPorteSalida> EmitirCartaPorteAsync(SolicitudCartaPorte solicitud, CancellationToken ct)
        {
            CartasPorte.Add(solicitud);
            return Task.FromResult(new CartaPorteSalida(Guid.NewGuid(), "CP-1"));
        }
    }

    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Vehiculo = Guid.NewGuid(), Operador = Guid.NewGuid();

    private sealed class Entorno(EntornoPt t) : IDisposable
    {
        public EntornoPt T { get; } = t;
        public FacturacionFalsa Facturacion { get; } = new();
        public PtAcceso Acceso { get; } = new(new Usuario(), new Permisos(), new Sucursales());
        public CrearHojaSalidaHandler Crear() => new(T.Db, Acceso, T.Pedidos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public EmitirHojaSalidaHandler Emitir() => new(T.Db, Acceso, T.Pedidos, Facturacion, T.Eventos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public CancelarHojaSalidaHandler Cancelar() => new(T.Db, Acceso, T.Pedidos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public CargarPiezaHojaHandler Cargar() => new(T.Db, Acceso, T.Pedidos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public DescargarPiezaHojaHandler Descargar() => new(T.Db, Acceso, T.Pedidos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public MarcarPiezaRotaHojaHandler Rota() => new(T.Db, Acceso, T.Pedidos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public ConfirmarEntregaHandler Entrega() => new(T.Db, Acceso, T.Pedidos, T.Eventos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public AplicarFacturaPedidoHandler Factura() => new(T.Db, T.Eventos, new RelojFijo(Ahora));
        public ConciliarCobroEntregaHandler Conciliar() => new(T.Db, T.Pedidos, new RelojFijo(Ahora));
        public void Dispose() => T.Dispose();
    }

    /// <summary>Pedido liberado (70) con <paramref name="piezas"/> piezas en PT: E1..En.</summary>
    private static async Task<Entorno> Preparar(int piezas = 3, string pedido = "P1")
    {
        var t = new EntornoPt();
        t.Saldos.Saldos[pedido] = 0m;
        t.AgregarPedido(pedido, 1, piezas);
        for (var i = 1; i <= piezas; i++) await t.Escanear($"{pedido}-E{i}", pedido, 1, i.ToString());
        return new Entorno(t);
    }

    private static CrearHojaSalidaCommand Hoja(IEnumerable<string> etiquetas, string pedido = "P1", bool foranea = false) =>
        new(EntornoPt.Sucursal, "Obra Mérida norte", foranea, foranea ? Vehiculo : null, foranea ? Operador : null, foranea ? 42m : null, foranea ? 850m : null,
            foranea ? "97302" : null, foranea ? "YUC" : null, foranea ? "97000" : null, foranea ? "YUC" : null, [new PedidoCargadoDto(pedido, etiquetas.ToList())]);

    [Fact]
    public async Task Crear_carga_las_piezas_y_arma_el_albaran_por_pedido()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja(["P1-E1", "P1-E2"]), default);

        hoja.Estado.Should().Be("Borrador");
        hoja.Albaranes.Should().ContainSingle().Which.Total.Should().Be(2);
        (await e.T.Db.Piezas.CountAsync(p => p.Estado == EstadoPieza.Cargada)).Should().Be(2);
        (await e.T.Db.Piezas.SingleAsync(p => p.Etiqueta == "P1-E3")).Estado.Should().Be(EstadoPieza.EnPT);
        e.Facturacion.Salidas.Should().BeEmpty("hasta emitir no hay salida");
    }

    [Fact]
    public async Task Emitir_descuenta_las_piezas_registra_la_salida_en_Facturacion_y_pasa_el_pedido_a_71()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja(["P1-E1", "P1-E2", "P1-E3"]), default);
        var dto = await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default);

        dto.Estado.Should().Be("Emitida");
        dto.YaEmitida.Should().BeFalse();
        (await e.T.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.Embarcada)).Should().BeTrue();
        (await e.T.Db.Movimientos.CountAsync(m => m.Tipo == TipoMovimientoPieza.Salida)).Should().Be(3);
        var salida = e.Facturacion.Salidas.Should().ContainSingle().Subject;
        salida.Referencia.Should().Be(dto.Albaranes.Single().Folio);
        salida.Lineas.Should().Equal([(1, 3m)]);
        (await e.T.Db.PedidosPt.SingleAsync()).EnRutaAt.Should().NotBeNull();
        e.T.Eventos.Eventos.OfType<PedidoEnRutaEvent>().Should().ContainSingle().Which.NumeroPedido.Should().Be("P1");
    }

    [Fact]
    public async Task Emitir_otra_vez_no_descuenta_ni_registra_ni_avisa_dos_veces()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja(["P1-E1", "P1-E2"]), default);
        await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default);
        var segunda = await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default);

        segunda.YaEmitida.Should().BeTrue();
        (await e.T.Db.Movimientos.CountAsync(m => m.Tipo == TipoMovimientoPieza.Salida)).Should().Be(2);
        e.Facturacion.Salidas.Should().HaveCount(1);
        e.T.Eventos.Eventos.OfType<PedidoEnRutaEvent>().Should().HaveCount(1);
    }

    [Fact]
    public async Task Embarque_parcial_factura_y_entrega_solo_lo_embarcado_y_deja_el_resto_pendiente()
    {
        // CA5.13: pedido de 30, se embarcan 20.
        using var e = await Preparar(30);
        var hoja = await e.Crear().Handle(Hoja(Enumerable.Range(1, 20).Select(i => $"P1-E{i}")), default);
        await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default);

        e.Facturacion.Salidas.Single().Lineas.Should().Equal([(1, 20m)]);
        var r = await new Consultas.ListarPiezasHandler(e.T.Db, e.Acceso).Handle(new Consultas.ListarPiezasQuery("P1", EntornoPt.Sucursal, 1, 50), default);
        var avance = r.Avance.Single();
        (avance.Cargadas, avance.Entregadas, avance.EnPt).Should().Be((0, 20, 10));

        // Los 10 restantes salen en otra hoja del mismo pedido.
        var resto = await e.Crear().Handle(Hoja(Enumerable.Range(21, 10).Select(i => $"P1-E{i}")), default);
        await e.Emitir().Handle(new EmitirHojaSalidaCommand(resto.Id), default);
        e.Facturacion.Salidas.Select(s => s.Lineas.Single().Cantidad).Should().Equal(20m, 10m);
    }

    [Fact]
    public async Task Una_pieza_no_puede_ir_en_dos_hojas()
    {
        using var e = await Preparar();
        await e.Crear().Handle(Hoja(["P1-E1"]), default);
        var act = () => e.Crear().Handle(Hoja(["P1-E1"]), default);
        // La pieza ya está Cargada; PT5.4 también deja Cargada, así que la guarda es la hoja existente.
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_SALIDA_PIEZA_YA_EN_HOJA");
    }

    [Fact]
    public async Task Pedido_retenido_no_sale()
    {
        using var e = new Entorno(new EntornoPt());
        e.T.AgregarPedido("P2", 1, 1);
        await e.T.Escanear("P2-E1", "P2", 1, "1");
        var act = () => e.Crear().Handle(Hoja(["P2-E1"], "P2"), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_PEDIDO_NO_LIBERADO");
    }

    [Fact]
    public async Task Salida_foranea_emite_la_Carta_Porte_con_vehiculo_operador_y_mercancia()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja(["P1-E1", "P1-E2"], foranea: true), default);
        var dto = await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default);

        dto.CartaPorteFolio.Should().Be("CP-1");
        var cp = e.Facturacion.CartasPorte.Should().ContainSingle().Subject;
        (cp.VehiculoId, cp.OperadorId, cp.PesoKg, cp.DestinoEstado).Should().Be((Vehiculo, Operador, 850m, "YUC"));
        cp.Mercancias.Should().ContainSingle().Which.Descripcion.Should().Contain("2 piezas");

        await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default);
        e.Facturacion.CartasPorte.Should().HaveCount(1, "una Carta Porte por tramo");
    }

    [Fact]
    public async Task Si_Facturacion_falla_tras_la_Carta_Porte_el_reintento_no_emite_otra_y_la_hoja_con_Carta_Porte_no_se_cancela()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja(["P1-E1"], foranea: true), default);
        e.Facturacion.Falla = true;
        var act = () => e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default);
        await act.Should().ThrowAsync<BusinessRuleException>();
        (await e.T.Db.Piezas.SingleAsync(p => p.Etiqueta == "P1-E1")).Estado.Should().Be(EstadoPieza.Cargada, "nada se descuenta si Facturación falla");

        e.Facturacion.Falla = false;
        await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default);
        e.Facturacion.CartasPorte.Should().HaveCount(1);
        e.Facturacion.Salidas.Should().HaveCount(1);
    }

    [Fact]
    public async Task Salida_foranea_sin_datos_de_Carta_Porte_se_rechaza()
    {
        using var e = await Preparar();
        var sinVehiculo = Hoja(["P1-E1"], foranea: true) with { VehiculoId = null };
        var act = () => e.Crear().Handle(sinVehiculo, default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_SALIDA_CARTA_PORTE_INCOMPLETA");
    }

    [Fact]
    public async Task Cancelar_borrador_devuelve_las_piezas_a_PT_y_una_emitida_no_se_cancela()
    {
        using var e = await Preparar();
        var hoja = await e.Crear().Handle(Hoja(["P1-E1", "P1-E2"]), default);
        var cancelada = await e.Cancelar().Handle(new CancelarHojaSalidaCommand(hoja.Id), default);
        cancelada.Estado.Should().Be("Cancelada");
        (await e.T.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.EnPT)).Should().BeTrue();

        // Las mismas piezas pueden cargarse en otra hoja y emitirse; esa ya no se cancela.
        var otra = await e.Crear().Handle(Hoja(["P1-E1", "P1-E2"]), default);
        await e.Emitir().Handle(new EmitirHojaSalidaCommand(otra.Id), default);
        var act = () => e.Cancelar().Handle(new CancelarHojaSalidaCommand(otra.Id), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_SALIDA_NO_CANCELABLE");
    }

    private static CrearHojaSalidaCommand Ruta(params string[] pedidos) =>
        Hoja([], pedidos[0]) with { Pedidos = pedidos.Select(p => new PedidoCargadoDto(p, [])).ToList() };

    private static async Task<Entorno> PrepararRuta(params (string Pedido, int Piezas)[] pedidos)
    {
        var t = new EntornoPt();
        foreach (var (pedido, piezas) in pedidos)
        {
            t.Saldos.Saldos[pedido] = 0m;
            t.AgregarPedido(pedido, 1, piezas);
            for (var i = 1; i <= piezas; i++) await t.Escanear($"{pedido}-E{i}", pedido, 1, i.ToString());
        }
        return new Entorno(t);
    }

    private static Task<HojaSalidaDto> Cargar(Entorno e, Guid hoja, string etiqueta) => e.Cargar().Handle(new CargarPiezaHojaCommand(hoja, etiqueta), default);

    [Fact]
    public async Task CA5_4_ruta_de_3_pedidos_2_completos_y_1_con_pieza_rota_el_albaran_lleva_solo_lo_cargado()
    {
        using var e = await PrepararRuta(("P1", 2), ("P2", 2), ("P3", 3));
        var ruta = await e.Crear().Handle(Ruta("P1", "P2", "P3"), default);
        ruta.Albaranes.Select(a => a.Total).Should().Equal(0, 0, 0);
        ruta.Albaranes.Select(a => a.Disponibles!.Count).Should().Equal(2, 2, 3);

        foreach (var et in new[] { "P1-E1", "P1-E2", "P2-E1", "P2-E2", "P3-E1", "P3-E2" }) await Cargar(e, ruta.Id, et);
        var parcial = await e.Rota().Handle(new MarcarPiezaRotaHojaCommand(ruta.Id, "P3-E3"), default);
        parcial.Albaranes.Single(a => a.NumeroPedido == "P3").Rotas.Should().Equal("P3-E3");

        var dto = await e.Emitir().Handle(new EmitirHojaSalidaCommand(ruta.Id), default);
        dto.Albaranes.Select(a => (a.NumeroPedido, a.Total, a.Esperadas)).Should().Equal(("P1", 2, 2), ("P2", 2, 2), ("P3", 2, 3));
        dto.Albaranes.Single(a => a.NumeroPedido == "P3").Etiquetas.Should().NotContain("P3-E3");
        e.Facturacion.Salidas.Select(s => s.Lineas.Single().Cantidad).Should().Equal(2m, 2m, 2m);
        (await e.T.Db.Piezas.CountAsync(p => p.Estado == EstadoPieza.Embarcada)).Should().Be(6, "el inventario baja solo por lo cargado");
        (await e.T.Db.Piezas.SingleAsync(p => p.Etiqueta == "P3-E3")).Estado.Should().Be(EstadoPieza.EnPT);
        dto.Albaranes.Single(a => a.NumeroPedido == "P3").Rotas.Should().BeEquivalentTo(["P3-E3"], "queda como reposición");
    }

    [Fact]
    public async Task Una_pieza_que_no_es_de_la_ruta_se_rechaza_y_no_cambia_nada()
    {
        using var e = await PrepararRuta(("P1", 1), ("P2", 1));
        var ruta = await e.Crear().Handle(Ruta("P1"), default);
        var act = () => Cargar(e, ruta.Id, "P2-E1");
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_CARGA_FUERA_DE_RUTA");
        var inexistente = () => Cargar(e, ruta.Id, "XX-1");
        (await inexistente.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_CARGA_PIEZA_NO_EXISTE");
        (await e.T.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.EnPT)).Should().BeTrue();
    }

    [Fact]
    public async Task PT5_4_a_pedido_con_saldo_pendiente_no_entra_a_la_ruta_ni_se_carga_si_dejo_de_estar_liberado()
    {
        using var e = await PrepararRuta(("P1", 1));
        e.T.Saldos.Saldos["P2"] = 500m;
        e.T.AgregarPedido("P2", 1, 1);
        await e.T.Escanear("P2-E1", "P2", 1, "1");
        var noSale = () => e.Crear().Handle(Ruta("P1", "P2"), default);
        (await noSale.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_PEDIDO_NO_LIBERADO");

        var ruta = await e.Crear().Handle(Ruta("P1"), default);
        var pedido = await e.T.Db.PedidosPt.SingleAsync(p => p.NumeroPedido == "P1");
        e.T.Db.Entry(pedido).Property(x => x.Liberacion).CurrentValue = EstadoLiberacion.Retenido;
        var act = () => Cargar(e, ruta.Id, "P1-E1");
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_PEDIDO_NO_LIBERADO");
    }

    [Fact]
    public async Task Cargar_dos_veces_o_en_otra_hoja_se_rechaza_y_descargar_devuelve_la_pieza_a_PT()
    {
        using var e = await PrepararRuta(("P1", 2));
        var ruta = await e.Crear().Handle(Ruta("P1"), default);
        var otra = await e.Crear().Handle(Ruta("P1"), default);
        await Cargar(e, ruta.Id, "P1-E1");
        var doble = () => Cargar(e, ruta.Id, "P1-E1");
        (await doble.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_CARGA_YA_CARGADA");
        var enOtra = () => Cargar(e, otra.Id, "P1-E1");
        (await enOtra.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_SALIDA_PIEZA_YA_EN_HOJA");

        var dto = await e.Descargar().Handle(new DescargarPiezaHojaCommand(ruta.Id, "P1-E1"), default);
        dto.Albaranes.Single().Total.Should().Be(0);
        (await e.T.Db.Piezas.SingleAsync(p => p.Etiqueta == "P1-E1")).Estado.Should().Be(EstadoPieza.EnPT);
        (await Cargar(e, ruta.Id, "P1-E1")).Albaranes.Single().Total.Should().Be(1, "se puede volver a cargar");
        var sinCargar = () => e.Descargar().Handle(new DescargarPiezaHojaCommand(ruta.Id, "P1-E2"), default);
        (await sinCargar.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_CARGA_NO_CARGADA");
    }

    [Fact]
    public async Task Pieza_rota_ya_cargada_sale_del_albaran_no_se_vuelve_a_cargar_y_la_reposicion_la_libera()
    {
        using var e = await PrepararRuta(("P1", 2));
        var ruta = await e.Crear().Handle(Ruta("P1"), default);
        await Cargar(e, ruta.Id, "P1-E1");
        var dto = await e.Rota().Handle(new MarcarPiezaRotaHojaCommand(ruta.Id, "P1-E1"), default);
        dto.Albaranes.Single().Total.Should().Be(0);
        (await e.T.Db.Piezas.SingleAsync(p => p.Etiqueta == "P1-E1")).Estado.Should().Be(EstadoPieza.EnPT);
        var act = () => Cargar(e, ruta.Id, "P1-E1");
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_CARGA_PIEZA_ROTA");

        // A+W la vuelve a terminar con la misma etiqueta: el movimiento de reposición es el último.
        var pieza = await e.T.Db.Piezas.SingleAsync(p => p.Etiqueta == "P1-E1");
        e.T.Db.Movimientos.Add(MovimientoPieza.Reposicion(pieza, "reposicion-1", Ahora.AddHours(1)));
        await e.T.Db.SaveChangesAsync();
        (await Cargar(e, ruta.Id, "P1-E1")).Albaranes.Single().Total.Should().Be(1);
    }

    [Fact]
    public async Task Emitir_una_hoja_sin_piezas_cargadas_se_rechaza_y_no_se_edita_una_hoja_emitida()
    {
        using var e = await PrepararRuta(("P1", 1));
        var ruta = await e.Crear().Handle(Ruta("P1"), default);
        var vacia = () => e.Emitir().Handle(new EmitirHojaSalidaCommand(ruta.Id), default);
        (await vacia.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_SALIDA_SIN_PIEZAS");

        await Cargar(e, ruta.Id, "P1-E1");
        await e.Emitir().Handle(new EmitirHojaSalidaCommand(ruta.Id), default);
        var tarde = () => e.Descargar().Handle(new DescargarPiezaHojaCommand(ruta.Id, "P1-E1"), default);
        (await tarde.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_SALIDA_NO_EDITABLE");
    }

    // ---- PT5.6 · Entrega con firma y evidencia, parcial, rechazo en sitio; 115 ----

    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private static ConfirmarEntregaCommand Entrega(Guid hoja, Guid albaran, string[]? rechazadas = null, string? firma = Png, string? foto = null, CobroEntregaDto? cobro = null) =>
        new(hoja, albaran, "Mariana Pech", rechazadas, rechazadas is { Length: > 0 } ? "Vidrio con rayón" : null, firma, foto, cobro);

    /// <summary>Hoja emitida con las <paramref name="cargadas"/> piezas de un pedido de <paramref name="total"/>.</summary>
    private static async Task<(Entorno E, HojaSalidaDto Hoja)> Emitida(int total, int cargadas)
    {
        var e = await Preparar(total);
        var hoja = await e.Crear().Handle(Hoja(Enumerable.Range(1, cargadas).Select(i => $"P1-E{i}")), default);
        return (e, await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja.Id), default));
    }

    [Fact]
    public async Task Entrega_completa_con_firma_deja_las_piezas_entregadas_y_avisa_para_el_costo_de_ventas()
    {
        var (e, hoja) = await Emitida(3, 3);
        using var _ = e;
        var dto = await e.Entrega().Handle(Entrega(hoja.Id, hoja.Albaranes.Single().Id), default);

        var entrega = dto.Albaranes.Single().Entrega!;
        (entrega.Resultado, entrega.Receptor, entrega.Entregadas, entrega.Rechazadas, entrega.TieneFirma, entrega.TieneFoto).Should().Be(("Entregada", "Mariana Pech", 3, 0, true, false));
        dto.EntregasPendientes.Should().Be(0);
        dto.Albaranes.Single().EstadoPedido.Should().Be("Entregado");
        (await e.T.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.Entregada)).Should().BeTrue();
        (await e.T.Db.Movimientos.CountAsync(m => m.Tipo == TipoMovimientoPieza.Entrega)).Should().Be(3);
        var aviso = e.T.Eventos.Eventos.OfType<EntregaConfirmadaEvent>().Should().ContainSingle().Subject;
        aviso.Entregadas.Should().Equal(new PosicionEntregada(1, 3));
        aviso.PedidoCompleto.Should().BeTrue();
        e.T.Eventos.Eventos.OfType<PedidoFacturadoEntregadoEvent>().Should().BeEmpty("sin factura no es 115");
    }

    [Fact]
    public async Task Entrega_sin_firma_ni_foto_se_rechaza_y_con_solo_foto_se_acepta()
    {
        var (e, hoja) = await Emitida(2, 2);
        using var _ = e;
        var sin = () => e.Entrega().Handle(Entrega(hoja.Id, hoja.Albaranes.Single().Id, firma: null), default);
        (await sin.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_ENTREGA_SIN_EVIDENCIA");
        (await e.T.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.Embarcada)).Should().BeTrue("no se confirmó nada");

        var dto = await e.Entrega().Handle(Entrega(hoja.Id, hoja.Albaranes.Single().Id, firma: null, foto: Png), default);
        dto.Albaranes.Single().Entrega!.TieneFoto.Should().BeTrue();
    }

    [Fact]
    public async Task Imagen_que_no_es_imagen_se_rechaza()
    {
        var (e, hoja) = await Emitida(1, 1);
        using var _ = e;
        var act = () => e.Entrega().Handle(Entrega(hoja.Id, hoja.Albaranes.Single().Id, firma: Convert.ToBase64String("no soy una imagen"u8.ToArray())), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_ENTREGA_IMAGEN_INVALIDA");
    }

    [Fact]
    public async Task Entrega_parcial_rechaza_en_sitio_las_piezas_indicadas_y_el_pedido_no_queda_entregado()
    {
        var (e, hoja) = await Emitida(3, 3);
        using var _ = e;
        var dto = await e.Entrega().Handle(Entrega(hoja.Id, hoja.Albaranes.Single().Id, ["P1-E3"]), default);

        var entrega = dto.Albaranes.Single().Entrega!;
        (entrega.Resultado, entrega.Entregadas, entrega.Rechazadas, entrega.MotivoRechazo).Should().Be(("Parcial", 2, 1, "Vidrio con rayón"));
        entrega.EtiquetasRechazadas.Should().Equal("P1-E3");
        (await e.T.Db.Piezas.SingleAsync(p => p.Etiqueta == "P1-E3")).Estado.Should().Be(EstadoPieza.EnRevision);
        (await e.T.Db.Piezas.CountAsync(p => p.Estado == EstadoPieza.Entregada)).Should().Be(2);
        dto.Albaranes.Single().EstadoPedido.Should().Be("71");
        e.T.Eventos.Eventos.OfType<EntregaConfirmadaEvent>().Single().PedidoCompleto.Should().BeFalse();
    }

    [Fact]
    public async Task Rechazo_total_en_sitio_manda_todo_a_revision_y_pide_el_motivo()
    {
        var (e, hoja) = await Emitida(2, 2);
        using var _ = e;
        var albaran = hoja.Albaranes.Single().Id;
        var sinMotivo = () => e.Entrega().Handle(Entrega(hoja.Id, albaran, ["P1-E1", "P1-E2"]) with { MotivoRechazo = " " }, default);
        (await sinMotivo.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_ENTREGA_SIN_MOTIVO");

        var dto = await e.Entrega().Handle(Entrega(hoja.Id, albaran, ["P1-E1", "P1-E2"]), default);
        dto.Albaranes.Single().Entrega!.Resultado.Should().Be("Rechazada");
        (await e.T.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.EnRevision)).Should().BeTrue();
        (await e.T.Db.Movimientos.CountAsync(m => m.Tipo == TipoMovimientoPieza.RechazoEnSitio)).Should().Be(2);
    }

    [Fact]
    public async Task Confirmar_dos_veces_o_una_pieza_ajena_o_una_hoja_sin_emitir_se_rechaza()
    {
        var (e, hoja) = await Emitida(2, 2);
        using var _ = e;
        var albaran = hoja.Albaranes.Single().Id;
        var ajena = () => e.Entrega().Handle(Entrega(hoja.Id, albaran, ["X-9"]), default);
        (await ajena.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_ENTREGA_PIEZA_AJENA");
        await e.Entrega().Handle(Entrega(hoja.Id, albaran), default);
        var doble = () => e.Entrega().Handle(Entrega(hoja.Id, albaran), default);
        (await doble.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_ENTREGA_YA_CONFIRMADA");

        e.T.Saldos.Saldos["P2"] = 0m;
        e.T.AgregarPedido("P2", 1, 1);
        await e.T.Escanear("P2-E1", "P2", 1, "1");
        var borrador = await e.Crear().Handle(Hoja(["P2-E1"], "P2"), default);
        var sinEmitir = () => e.Entrega().Handle(Entrega(borrador.Id, borrador.Albaranes.Single().Id), default);
        (await sinEmitir.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_ENTREGA_HOJA_NO_EMITIDA");
    }

    [Fact]
    public async Task Pedido_facturado_antes_de_entregar_pasa_a_115_al_entregar_y_avisa_una_sola_vez()
    {
        var (e, hoja) = await Emitida(2, 2);
        using var _ = e;
        e.T.Pedidos.Pedidos["P1"] = e.T.Pedidos.Pedidos["P1"] with { Estado = "Facturado" };
        var dto = await e.Entrega().Handle(Entrega(hoja.Id, hoja.Albaranes.Single().Id), default);

        dto.Albaranes.Single().EstadoPedido.Should().Be("115");
        e.T.Eventos.Eventos.OfType<PedidoFacturadoEntregadoEvent>().Should().ContainSingle().Which.Should().Match<PedidoFacturadoEntregadoEvent>(x => x.Estado == "115" && x.NumeroPedido == "P1");
        (await e.Factura().Handle(new AplicarFacturaPedidoCommand(EntornoPt.Empresa, e.T.Pedidos.Pedidos["P1"].PedidoFacturableId), default)).Should().BeFalse("ya estaba en 115");
        e.T.Eventos.Eventos.OfType<PedidoFacturadoEntregadoEvent>().Should().HaveCount(1);
    }

    [Fact]
    public async Task Factura_que_llega_despues_de_la_entrega_completa_pasa_el_pedido_a_115()
    {
        var (e, hoja) = await Emitida(2, 2);
        using var _ = e;
        var factura = e.T.Pedidos.Pedidos["P1"].PedidoFacturableId;
        await e.Entrega().Handle(Entrega(hoja.Id, hoja.Albaranes.Single().Id), default);
        e.T.Eventos.Eventos.OfType<PedidoFacturadoEntregadoEvent>().Should().BeEmpty();

        (await e.Factura().Handle(new AplicarFacturaPedidoCommand(EntornoPt.Empresa, factura), default)).Should().BeTrue();
        e.T.Eventos.Eventos.OfType<PedidoFacturadoEntregadoEvent>().Should().ContainSingle();
        (await e.T.Db.PedidosPt.SingleAsync()).Estado115At.Should().NotBeNull();
    }

    [Fact]
    public async Task Factura_de_un_pedido_con_piezas_rechazadas_en_sitio_no_lo_pasa_a_115()
    {
        var (e, hoja) = await Emitida(3, 3);
        using var _ = e;
        await e.Entrega().Handle(Entrega(hoja.Id, hoja.Albaranes.Single().Id, ["P1-E3"]), default);
        (await e.Factura().Handle(new AplicarFacturaPedidoCommand(EntornoPt.Empresa, e.T.Pedidos.Pedidos["P1"].PedidoFacturableId), default)).Should().BeFalse("faltan piezas por entregar");
        e.T.Eventos.Eventos.OfType<PedidoFacturadoEntregadoEvent>().Should().BeEmpty();
        (await e.T.Db.PedidosPt.SingleAsync()).FacturadoAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Cobro_con_TPV_se_concilia_cuando_la_caja_registra_el_mismo_monto_del_pedido_y_una_sola_vez()
    {
        var (e, hoja) = await Emitida(2, 2);
        using var _ = e;
        var cobro = new CobroEntregaDto(8500m, "TPV", "VOUCHER-0417");
        var dto = await e.Entrega().Handle(Entrega(hoja.Id, hoja.Albaranes.Single().Id, cobro: cobro), default);
        dto.Albaranes.Single().Entrega!.Cobro.Should().Match<CobroEntregaDto>(c => c.Monto == 8500m && c.FormaPago == "TPV" && c.ConciliadoAt == null);

        var comprobante = Guid.NewGuid();
        e.T.Pedidos.Facturas[comprobante] = e.T.Pedidos.Pedidos["P1"].PedidoFacturableId;
        (await e.Conciliar().Handle(new ConciliarCobroEntregaCommand(EntornoPt.Empresa, Guid.NewGuid(), comprobante, 100m), default)).Should().BeFalse("otro monto");
        (await e.Conciliar().Handle(new ConciliarCobroEntregaCommand(EntornoPt.Empresa, Guid.NewGuid(), comprobante, 8500m), default)).Should().BeTrue();
        (await e.Conciliar().Handle(new ConciliarCobroEntregaCommand(EntornoPt.Empresa, Guid.NewGuid(), comprobante, 8500m), default)).Should().BeFalse("ya conciliado");
        (await e.T.Db.Entregas.SingleAsync()).CobroConciliadoAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Cobro_de_una_entrega_rechazada_por_completo_se_rechaza()
    {
        var (e, hoja) = await Emitida(1, 1);
        using var _ = e;
        var act = () => e.Entrega().Handle(Entrega(hoja.Id, hoja.Albaranes.Single().Id, ["P1-E1"], cobro: new CobroEntregaDto(100m, "TPV", null)), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_ENTREGA_COBRO_SIN_ENTREGA");
    }

    [Fact]
    public async Task Evidencia_devuelve_la_firma_y_la_foto_guardadas()
    {
        var (e, hoja) = await Emitida(1, 1);
        using var _ = e;
        await e.Entrega().Handle(Entrega(hoja.Id, hoja.Albaranes.Single().Id, foto: Png), default);
        var ev = await new ObtenerEvidenciaEntregaHandler(e.T.Db, e.Acceso).Handle(new ObtenerEvidenciaEntregaQuery(hoja.Id, hoja.Albaranes.Single().Id), default);
        ev.Firma.Should().StartWith("data:image/png;base64,");
        ev.Foto.Should().StartWith("data:image/png;base64,");
    }
}
