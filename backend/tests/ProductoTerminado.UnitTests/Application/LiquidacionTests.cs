using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Liquidacion;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Application.Salidas;
using Millet.ProductoTerminado.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.UnitTests.Application;

// PT5.7: liquidación del chofer en caja (CA5.6, PT5.7-a) y aviso a CxC de la contra entrega no liquidada (CA5.7).
public sealed class LiquidacionTests
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
        public Task RegistrarSalidaAsync(Guid pedidoFacturableId, string referencia, IReadOnlyList<(int Posicion, decimal Cantidad)> lineas, CancellationToken ct) => Task.CompletedTask;
        public Task<CartaPorteSalida> EmitirCartaPorteAsync(SolicitudCartaPorte solicitud, CancellationToken ct) => Task.FromResult(new CartaPorteSalida(Guid.NewGuid(), "CP-1"));
    }

    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero); // 12:00 en Ciudad de México
    private static readonly Guid Cliente = Guid.NewGuid();

    private sealed class Entorno(EntornoPt t) : IDisposable
    {
        public EntornoPt T { get; } = t;
        public PtAcceso Acceso { get; } = new(new Usuario(), new Permisos(), new Sucursales());
        public LiquidarRutaPtHandler Liquidar() => new(T.Db, Acceso, T.Pedidos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public CerrarDiaContraEntregaHandler CerrarDia() => new(T.Db, T.Pedidos, T.Eventos, new RelojFijo(Ahora));
        public void Dispose() => T.Dispose();
    }

    /// <summary>Hoja emitida con un pedido por condición de pago: P1 «Crédito 24 horas» de $8,500 (CA5.6) y, si se pide, P2 de contado.</summary>
    private static async Task<(Entorno E, HojaSalidaDto Hoja)> HojaEmitida(bool conContado = false)
    {
        var t = new EntornoPt();
        var pedidos = new List<PedidoCargadoDto>();
        foreach (var (p, cond, total) in new[] { ("P1", "Crédito 24 horas", 8500m), ("P2", "CONTADO", 1200m) }.Take(conContado ? 2 : 1))
        {
            t.Saldos.Saldos[p] = 0m;
            t.AgregarPedido(p, 1, 2, condicionPago: cond);
            t.Pedidos.Pedidos[p] = t.Pedidos.Pedidos[p] with { ClienteId = Cliente, Total = total };
            for (var i = 1; i <= 2; i++) await t.Escanear($"{p}-E{i}", p, 1, i.ToString());
            pedidos.Add(new PedidoCargadoDto(p, [$"{p}-E1", $"{p}-E2"]));
        }
        var e = new Entorno(t);
        var acceso = e.Acceso;
        var emp = new EmpresaFija(EntornoPt.Empresa);
        var hoja = await new CrearHojaSalidaHandler(t.Db, acceso, t.Pedidos, emp, new RelojFijo(Ahora)).Handle(
            new CrearHojaSalidaCommand(EntornoPt.Sucursal, "Obra Mérida norte", false, null, null, null, null, null, null, null, null, pedidos), default);
        hoja = await new EmitirHojaSalidaHandler(t.Db, acceso, t.Pedidos, new FacturacionFalsa(), t.Eventos, emp, new RelojFijo(Ahora)).Handle(new EmitirHojaSalidaCommand(hoja.Id), default);
        return (e, hoja);
    }

    private static LiquidacionAlbaranInput Linea(HojaSalidaDto h, string pedido, bool firmado = true, decimal efectivo = 0m, decimal voucher = 0m, string? referencia = null) =>
        new(h.Albaranes.Single(a => a.NumeroPedido == pedido).Id, firmado, efectivo, voucher, referencia);

    [Fact]
    public async Task Voucher_que_cuadra_con_el_pedido_y_albaran_firmado_cierra_la_liquidacion()
    {
        var (e, hoja) = await HojaEmitida();
        using var _ = e;
        var dto = await e.Liquidar().Handle(new LiquidarRutaPtCommand(hoja.Id, [Linea(hoja, "P1", voucher: 8500m, referencia: "TPV-004821")], []), default);

        dto.Estado.Should().Be("Cerrada");
        dto.Diferencia.Should().Be(0m);
        var l = dto.Albaranes.Single();
        (l.ContraEntrega, l.Esperado, l.Voucher, l.Conciliado).Should().Be((true, 8500m, 8500m, true));
    }

    [Fact]
    public async Task Voucher_que_no_coincide_deja_la_diferencia_y_no_cierra_hasta_corregirlo()
    {
        var (e, hoja) = await HojaEmitida();
        using var _ = e;
        var dto = await e.Liquidar().Handle(new LiquidarRutaPtCommand(hoja.Id, [Linea(hoja, "P1", voucher: 8000m, referencia: "TPV-004821")], [new ImprevistoInput("Maniobra extra de descarga", 350m)]), default);

        dto.Estado.Should().Be("ConDiferencia");
        dto.Diferencia.Should().Be(500m);
        dto.Imprevistos.Should().ContainSingle(i => i.Importe == 350m);

        var corregida = await e.Liquidar().Handle(new LiquidarRutaPtCommand(hoja.Id, [Linea(hoja, "P1", efectivo: 500m, voucher: 8000m, referencia: "TPV-004821")], []), default);
        corregida.Estado.Should().Be("Cerrada");
        corregida.Imprevistos.Should().BeEmpty("la segunda captura reemplaza a la primera");
        (await e.T.Db.Liquidaciones.CountAsync()).Should().Be(1);

        var otraVez = () => e.Liquidar().Handle(new LiquidarRutaPtCommand(hoja.Id, [Linea(hoja, "P1", voucher: 1m, referencia: "X")], []), default);
        (await otraVez.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_LIQUIDACION_CERRADA");
    }

    [Fact]
    public async Task Albaran_sin_firma_no_cierra_aunque_el_cobro_cuadre()
    {
        var (e, hoja) = await HojaEmitida();
        using var _ = e;
        var dto = await e.Liquidar().Handle(new LiquidarRutaPtCommand(hoja.Id, [Linea(hoja, "P1", firmado: false, efectivo: 8500m)], []), default);
        dto.Estado.Should().Be("ConDiferencia");
        dto.Albaranes.Single().Conciliado.Should().BeFalse();
    }

    [Fact]
    public async Task Pedido_de_contado_no_espera_cobro_en_ruta()
    {
        var (e, hoja) = await HojaEmitida(conContado: true);
        using var _ = e;
        var sinCobro = await e.Liquidar().Handle(new LiquidarRutaPtCommand(hoja.Id, [Linea(hoja, "P1", efectivo: 8500m), Linea(hoja, "P2")], []), default);
        sinCobro.Estado.Should().Be("Cerrada");
        sinCobro.Albaranes.Single(a => a.NumeroPedido == "P2").Esperado.Should().Be(0m);
    }

    [Fact]
    public async Task Rechaza_voucher_sin_folio_albaranes_incompletos_y_hoja_en_borrador()
    {
        var (e, hoja) = await HojaEmitida(conContado: true);
        using var _ = e;
        var sinFolio = () => e.Liquidar().Handle(new LiquidarRutaPtCommand(hoja.Id, [Linea(hoja, "P1", voucher: 8500m), Linea(hoja, "P2")], []), default);
        (await sinFolio.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_LIQUIDACION_VOUCHER_SIN_REFERENCIA");
        var incompleta = () => e.Liquidar().Handle(new LiquidarRutaPtCommand(hoja.Id, [Linea(hoja, "P1", efectivo: 8500m)], []), default);
        (await incompleta.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_LIQUIDACION_ALBARANES");

        var salida = await e.T.Db.Salidas.SingleAsync();
        e.T.Db.Entry(salida).Property(nameof(SalidaPt.Estado)).CurrentValue = EstadoSalida.Borrador;
        var borrador = () => e.Liquidar().Handle(new LiquidarRutaPtCommand(hoja.Id, [], []), default);
        (await borrador.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_LIQUIDACION_HOJA_NO_EMITIDA");
    }

    [Fact]
    public async Task Cierre_del_dia_avisa_a_CxC_la_contra_entrega_sin_liquidar_una_sola_vez()
    {
        var (e, hoja) = await HojaEmitida(conContado: true);
        using var _ = e;

        (await e.CerrarDia().Handle(new CerrarDiaContraEntregaCommand(), default)).Should().Be(1, "solo P1 es contra entrega");
        var aviso = e.T.Eventos.Eventos.OfType<ContraEntregaNoLiquidadaEvent>().Should().ContainSingle().Subject;
        (aviso.NumeroPedido, aviso.ClienteId, aviso.Total, aviso.HojaSalidaFolio).Should().Be(("P1", Cliente, 8500m, hoja.Folio));

        (await e.CerrarDia().Handle(new CerrarDiaContraEntregaCommand(), default)).Should().Be(0, "ya se avisó");
    }

    [Fact]
    public async Task Cierre_del_dia_no_avisa_si_ya_liquido_con_el_cobro_cuadrado_ni_antes_de_la_salida()
    {
        var (e, hoja) = await HojaEmitida();
        using var _ = e;
        (await e.CerrarDia().Handle(new CerrarDiaContraEntregaCommand(new DateOnly(2026, 11, 4)), default)).Should().Be(0, "la hoja salió después de ese día");

        await e.Liquidar().Handle(new LiquidarRutaPtCommand(hoja.Id, [Linea(hoja, "P1", voucher: 8500m, referencia: "TPV-004821")], []), default);
        (await e.CerrarDia().Handle(new CerrarDiaContraEntregaCommand(), default)).Should().Be(0);
        e.T.Eventos.Eventos.OfType<ContraEntregaNoLiquidadaEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Liquidacion_con_diferencia_sigue_contando_como_no_liquidada_para_el_aviso()
    {
        var (e, hoja) = await HojaEmitida();
        using var _ = e;
        await e.Liquidar().Handle(new LiquidarRutaPtCommand(hoja.Id, [Linea(hoja, "P1", voucher: 8000m, referencia: "TPV-004821")], []), default);
        (await e.CerrarDia().Handle(new CerrarDiaContraEntregaCommand(), default)).Should().Be(1);
    }

    [Theory]
    [InlineData("Crédito 24 horas", true)]
    [InlineData("CREDITO 24 H", true)]
    [InlineData("CONTADO", false)]
    [InlineData("REPARTO", false)]
    [InlineData("Crédito 30 días", false)]
    [InlineData(null, false)]
    public void Reconoce_la_condicion_de_contra_entrega(string? condicion, bool esperado) =>
        LiquidacionRutaPt.EsContraEntrega(condicion).Should().Be(esperado);
}
