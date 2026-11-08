using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Entregas;
using Millet.ProductoTerminado.Application.Liquidacion;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Application.Salidas;
using Millet.ProductoTerminado.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Xunit;

namespace Millet.ProductoTerminado.UnitTests.Application;

public sealed class CancelarAlbaranSalidaTests
{
    private sealed class Usuario : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.Parse("00000000-0000-0000-0000-0000000000cc");
        public string? UserName => "test.user";
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
        public List<(string Referencia, string Motivo)> Cancelaciones { get; } = [];
        public bool Falla { get; set; }
        public bool Facturada { get; set; }

        public Task RegistrarSalidaAsync(Guid pedidoFacturableId, string referencia, IReadOnlyList<(int Posicion, decimal Cantidad)> lineas, CancellationToken ct)
        {
            if (Falla) throw new BusinessRuleException("SALIDA_EXCEDE_PEDIDO", "Excede lo pedido.");
            if (Salidas.All(s => s.Referencia != referencia)) Salidas.Add((referencia, lineas));
            return Task.CompletedTask;
        }

        public Task<CancelacionSalidaPt> CancelarSalidaAsync(string referencia, string motivo, CancellationToken ct)
        {
            Cancelaciones.Add((referencia, motivo));
            // Si la factura ya fue timbrada ante el SAT, Facturación responde con EnRevision = true
            return Task.FromResult(new CancelacionSalidaPt(true, !Facturada, Facturada));
        }

        public Task<CartaPorteSalida> EmitirCartaPorteAsync(SolicitudCartaPorte solicitud, CancellationToken ct)
        {
            CartasPorte.Add(solicitud);
            return Task.FromResult(new CartaPorteSalida(Guid.NewGuid(), "CP-1"));
        }
    }

    private static readonly DateTimeOffset Ahora = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Vehiculo = Guid.NewGuid(), Operador = Guid.NewGuid();

    private sealed class Entorno : IDisposable
    {
        public EntornoPt T { get; }
        public FacturacionFalsa Facturacion { get; } = new();
        public PtAcceso Acceso { get; } = new(new Usuario(), new Permisos(), new Sucursales());

        public Entorno(EntornoPt t) => T = t;

        public CrearHojaSalidaHandler Crear() => new(T.Db, Acceso, T.Pedidos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public EmitirHojaSalidaHandler Emitir() => new(T.Db, Acceso, T.Pedidos, Facturacion, T.Eventos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public CancelarAlbaranSalidaHandler CancelarAlbaran() => new(T.Db, Acceso, T.Pedidos, Facturacion, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public ConfirmarEntregaHandler Entrega() => new(T.Db, Acceso, T.Pedidos, T.Eventos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));
        public LiquidarRutaPtHandler Liquidar() => new(T.Db, Acceso, T.Pedidos, new EmpresaFija(EntornoPt.Empresa), new RelojFijo(Ahora));

        public void Dispose() => T.Dispose();
    }

    private static async Task<Entorno> Preparar(int piezas = 3, string pedido = "P1")
    {
        var t = new EntornoPt();
        t.Saldos.Saldos[pedido] = 0m;
        t.AgregarPedido(pedido, 1, piezas);
        for (var i = 1; i <= piezas; i++) await t.Escanear($"{pedido}-E{i}", pedido, 1, i.ToString());
        return new Entorno(t);
    }

    private static CrearHojaSalidaCommand Hoja(IEnumerable<string> etiquetas, string pedido = "P1") =>
        new(EntornoPt.Sucursal, "Obra Mérida norte", false, null, null, null, null,
            null, null, null, null, [new PedidoCargadoDto(pedido, etiquetas.ToList())]);

    [Fact]
    public async Task CancelarAlbaran_exitoso_reversa_piezas_a_EnPT_y_pedido_regresa_a_70()
    {
        using var e = await Preparar(3, "P1");
        var hojaDto = await e.Crear().Handle(Hoja(["P1-E1", "P1-E2", "P1-E3"], "P1"), default);
        var emitidaDto = await e.Emitir().Handle(new EmitirHojaSalidaCommand(hojaDto.Id), default);

        var albaran = emitidaDto.Albaranes.Single();
        (await e.T.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.Embarcada)).Should().BeTrue();
        var pedidoAntes = await e.T.Db.PedidosPt.SingleAsync(p => p.NumeroPedido == "P1");
        pedidoAntes.EnRutaAt.Should().NotBeNull("pedido debe estar en 71 tras emisión");

        // Cancelar el albarán
        var motivo = "Camión descompuesto, no salió de patio";
        var resultado = await e.CancelarAlbaran().Handle(new CancelarAlbaranSalidaCommand(hojaDto.Id, albaran.Id, motivo), default);

        // Albarán cancelado
        var albResultado = resultado.Albaranes.Single(a => a.Id == albaran.Id);
        albResultado.Cancelado.Should().BeTrue();
        albResultado.MotivoCancelacion.Should().Be(motivo);

        // Piezas regresaron a EnPT
        var piezas = await e.T.Db.Piezas.Where(p => p.PedidoPtId == pedidoAntes.Id).ToListAsync();
        piezas.Should().HaveCount(3);
        piezas.All(p => p.Estado == EstadoPieza.EnPT).Should().BeTrue();

        // MovimientoPieza registrado como reversa por cancelación
        var movs = await e.T.Db.Movimientos.Where(m => m.Tipo == TipoMovimientoPieza.SalidaCancelada).ToListAsync();
        movs.Should().HaveCount(3);
        movs.All(m => m.EstadoAnterior == "Embarcada" && m.EstadoNuevo == "EnPT").Should().BeTrue();

        // Pedido regresa de 71 a 70 (EnRutaAt pasa a null)
        var pedidoDespues = await e.T.Db.PedidosPt.SingleAsync(p => p.NumeroPedido == "P1");
        pedidoDespues.EnRutaAt.Should().BeNull("pedido regresa a 70 porque ya no tiene albaranes vigentes");

        // Como era el único albarán de la hoja, la hoja pasa a Cancelada
        resultado.Estado.Should().Be("Cancelada");
    }

    [Fact]
    public async Task CancelarAlbaran_si_quedan_otros_albaranes_emitidos_vigentes_pedido_sigue_en_71()
    {
        using var e = await Preparar(4, "P1");
        var hoja1 = await e.Crear().Handle(Hoja(["P1-E1", "P1-E2"], "P1"), default);
        var emitida1 = await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja1.Id), default);

        var hoja2 = await e.Crear().Handle(Hoja(["P1-E3", "P1-E4"], "P1"), default);
        var emitida2 = await e.Emitir().Handle(new EmitirHojaSalidaCommand(hoja2.Id), default);

        var pedidoAntes = await e.T.Db.PedidosPt.SingleAsync(p => p.NumeroPedido == "P1");
        pedidoAntes.EnRutaAt.Should().NotBeNull();

        // Cancelamos albarán de hoja1
        var alb1 = emitida1.Albaranes.Single();
        await e.CancelarAlbaran().Handle(new CancelarAlbaranSalidaCommand(hoja1.Id, alb1.Id, "Parcial cancelado"), default);

        // Pedido sigue en 71 porque hoja2 sigue emitida y vigente
        var pedidoDespues = await e.T.Db.PedidosPt.SingleAsync(p => p.NumeroPedido == "P1");
        pedidoDespues.EnRutaAt.Should().NotBeNull("aún tiene albarán vigente de hoja2");

        // Las piezas de hoja1 regresaron a EnPT
        (await e.T.Db.Piezas.CountAsync(p => p.Estado == EstadoPieza.EnPT)).Should().Be(2);
        (await e.T.Db.Piezas.CountAsync(p => p.Estado == EstadoPieza.Embarcada)).Should().Be(2);
    }

    [Fact]
    public async Task CancelarAlbaran_rechaza_si_cuenta_con_factura_timbrada_ante_el_SAT()
    {
        using var e = await Preparar(2, "P1");
        var hojaDto = await e.Crear().Handle(Hoja(["P1-E1", "P1-E2"], "P1"), default);
        var emitidaDto = await e.Emitir().Handle(new EmitirHojaSalidaCommand(hojaDto.Id), default);
        var albaran = emitidaDto.Albaranes.Single();

        // Facturación reporta que ya existe factura timbrada
        e.Facturacion.Facturada = true;

        var act = () => e.CancelarAlbaran().Handle(new CancelarAlbaranSalidaCommand(hojaDto.Id, albaran.Id, "Intento cancelar facturado"), default);
        var ex = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;
        ex.Code.Should().Be("PT_ALBARAN_YA_FACTURADO");
        ex.Message.Should().Contain("El albarán ya cuenta con factura timbrada ante el SAT; debe cancelarse primero el CFDI.");

        // Las piezas siguen Embarcadas y el albarán no se canceló
        (await e.T.Db.Piezas.AllAsync(p => p.Estado == EstadoPieza.Embarcada)).Should().BeTrue();
        var albaranDb = await e.T.Db.Albaranes.SingleAsync(a => a.Id == albaran.Id);
        albaranDb.CanceladoAt.Should().BeNull();
    }

    [Fact]
    public async Task CancelarAlbaran_rechaza_si_albaran_no_pertenece_a_la_hoja()
    {
        using var e = await Preparar(2, "P1");
        var hojaDto = await e.Crear().Handle(Hoja(["P1-E1", "P1-E2"], "P1"), default);

        var act = () => e.CancelarAlbaran().Handle(new CancelarAlbaranSalidaCommand(hojaDto.Id, Guid.NewGuid(), "No existe"), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_ALBARAN_NO_PERTENECE_A_SALIDA");
    }

    [Fact]
    public async Task CancelarAlbaran_rechaza_si_albaran_ya_fue_entregado_en_ruta()
    {
        using var e = await Preparar(2, "P1");
        var hojaDto = await e.Crear().Handle(Hoja(["P1-E1", "P1-E2"], "P1"), default);
        var emitidaDto = await e.Emitir().Handle(new EmitirHojaSalidaCommand(hojaDto.Id), default);
        var albaran = emitidaDto.Albaranes.Single();

        // Confirmar entrega con firma simulada (PNG mínimo)
        byte[] pngFirma = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        await e.Entrega().Handle(new ConfirmarEntregaCommand(hojaDto.Id, albaran.Id, "Juan Perez", [], null, Convert.ToBase64String(pngFirma), null, null), default);

        var act = () => e.CancelarAlbaran().Handle(new CancelarAlbaranSalidaCommand(hojaDto.Id, albaran.Id, "Cancelar entregado"), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_ALBARAN_YA_ENTREGADO");
    }

    [Fact]
    public async Task CancelarAlbaran_rechaza_si_albaran_ya_esta_cancelado()
    {
        using var e = await Preparar(2, "P1");
        var hojaDto = await e.Crear().Handle(Hoja(["P1-E1", "P1-E2"], "P1"), default);
        var emitidaDto = await e.Emitir().Handle(new EmitirHojaSalidaCommand(hojaDto.Id), default);
        var albaran = emitidaDto.Albaranes.Single();

        await e.CancelarAlbaran().Handle(new CancelarAlbaranSalidaCommand(hojaDto.Id, albaran.Id, "Primera cancelacion"), default);

        var act = () => e.CancelarAlbaran().Handle(new CancelarAlbaranSalidaCommand(hojaDto.Id, albaran.Id, "Segunda cancelacion"), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PT_ALBARAN_YA_CANCELADO");
    }
}
