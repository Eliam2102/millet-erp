using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application;
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
}
