using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Facturas.Queries;
using Millet.Facturacion.Application.Pedidos;
using Millet.Facturacion.Application.Pedidos.CrearPedidoFacturableManual;
using Millet.Facturacion.Application.Pedidos.EditarPedidoFacturableManual;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Pedidos;

/// <summary>
/// F6.1 (R2): clasificación obligatoria del pedido manual — venta directa
/// (activo / almacenable / servicio) o emergencia. No existe clase
/// «producción» (F6.1-b).
/// </summary>
public sealed class PedidoFacturableManualClasificacionTests
{
    private static PedidoFacturable Manual(
        ClasePedidoManual? clase,
        SubtipoVentaDirecta? subtipo = null,
        string? motivo = null,
        ComportamientoFiscal comportamiento = ComportamientoFiscal.MostradorInmediato,
        string? referencia = null) =>
        PedidoFacturable.CrearManual(
            Guid.NewGuid(), "MAN-1", Guid.NewGuid(), Guid.NewGuid(), "Cliente", 1,
            comportamiento, "MXN", null, null, null, Guid.NewGuid(),
            clase, subtipo, motivo, referencia);

    /// <summary>Simula un pedido manual capturado antes de F6.1 (sin clase).</summary>
    internal static void VolverLegacy(PedidoFacturable pedido)
    {
        typeof(PedidoFacturable).GetProperty(nameof(PedidoFacturable.ClaseManual))!.SetValue(pedido, null);
        typeof(PedidoFacturable).GetProperty(nameof(PedidoFacturable.SubtipoVentaDirecta))!.SetValue(pedido, null);
    }

    private static void DebeFallar(Action act, string codigo) =>
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == codigo);

    // ----- Dominio -----

    [Fact]
    public void Sin_clase_se_rechaza()
        => DebeFallar(() => Manual(null), "PEDIDO_MANUAL_CLASE_REQUERIDA");

    [Fact]
    public void Clase_fuera_de_catalogo_se_rechaza()
        => DebeFallar(() => Manual((ClasePedidoManual)9), "PEDIDO_MANUAL_CLASE_REQUERIDA");

    [Fact]
    public void Venta_directa_sin_subtipo_se_rechaza()
        => DebeFallar(() => Manual(ClasePedidoManual.VentaDirecta), "PEDIDO_VD_SUBTIPO_REQUERIDO");

    [Fact]
    public void Emergencia_sin_motivo_se_rechaza()
        => DebeFallar(() => Manual(ClasePedidoManual.Emergencia, motivo: "  "), "PEDIDO_EMERGENCIA_MOTIVO_REQUERIDO");

    [Fact]
    public void Emergencia_con_subtipo_se_rechaza()
        => DebeFallar(() => Manual(ClasePedidoManual.Emergencia, SubtipoVentaDirecta.Servicio, "A+W caído"),
            "PEDIDO_EMERGENCIA_SIN_SUBTIPO");

    [Fact]
    public void Activo_con_comportamiento_distinto_se_rechaza()
        => DebeFallar(() => Manual(ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Activo),
            "PEDIDO_ACTIVO_COMPORTAMIENTO");

    [Fact]
    public void VentaActivoFijo_sin_subtipo_activo_se_rechaza()
        => DebeFallar(() => Manual(ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio,
            comportamiento: ComportamientoFiscal.VentaActivoFijo), "PEDIDO_ACTIVO_COMPORTAMIENTO");

    [Fact]
    public void Venta_directa_activo_valida_queda_clasificada()
    {
        var p = Manual(ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Activo,
            comportamiento: ComportamientoFiscal.VentaActivoFijo, referencia: " AW-123 ");

        p.EsVentaDirectaManual.Should().BeTrue();
        p.EsEmergencia.Should().BeFalse();
        p.SubtipoVentaDirecta.Should().Be(SubtipoVentaDirecta.Activo);
        p.ReferenciaPedidoAw.Should().Be("AW-123");
        p.MotivoEmergencia.Should().BeNull();
    }

    [Fact]
    public void Emergencia_valida_es_venta_directa_manual_y_emergencia()
    {
        var p = Manual(ClasePedidoManual.Emergencia, motivo: "A+W fuera de servicio");

        p.EsVentaDirectaManual.Should().BeTrue("la emergencia también se folia con la serie VD");
        p.EsEmergencia.Should().BeTrue();
        p.MotivoEmergencia.Should().Be("A+W fuera de servicio");
    }

    [Fact]
    public void Editar_legacy_exige_clase_y_al_clasificar_queda_facturable()
    {
        var p = Manual(ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio);
        VolverLegacy(p);
        p.EsManualSinClasificar.Should().BeTrue();
        DebeFallar(p.AsegurarClasificadoParaFacturar, "PEDIDO_MANUAL_SIN_CLASIFICAR");

        DebeFallar(() => p.EditarCabecera(p.ClienteId, "Cliente", 1, ComportamientoFiscal.MostradorInmediato,
            "MXN", null, null, null, null, null, null, null), "PEDIDO_MANUAL_CLASE_REQUERIDA");

        p.EditarCabecera(p.ClienteId, "Cliente", 1, ComportamientoFiscal.MostradorInmediato,
            "MXN", null, null, null, ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Almacenable, null, null);
        p.EsManualSinClasificar.Should().BeFalse();
        p.AsegurarClasificadoParaFacturar();
    }

    [Fact]
    public void Pedido_aw_no_es_venta_directa()
    {
        var p = PedidoFacturable.ImportarDesdeAw(Guid.NewGuid(), "AW-1", Guid.NewGuid(), Guid.NewGuid(), "Cliente", 1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        p.EsVentaDirectaManual.Should().BeFalse();
        p.EsManualSinClasificar.Should().BeFalse();
        p.AsegurarClasificadoParaFacturar();
    }

    // ----- Validador (400) -----

    private static CrearPedidoFacturableManualCommand Cmd(
        ClasePedidoManual? clase, SubtipoVentaDirecta? subtipo = null, string? motivo = null,
        ComportamientoFiscal comportamiento = ComportamientoFiscal.MostradorInmediato) => new(
        NumeroPedido: null, SucursalId: Guid.NewGuid(), ClienteId: Guid.NewGuid(), ClienteNombre: "Cliente",
        CanalVenta: 1, ComportamientoFiscal: comportamiento, Moneda: "MXN", ObraId: null, ObraNombre: null,
        Comentarios: null, Lineas: [new PedidoFacturableLineaInput(null, "Producto", null, null, 1m, 100m, 0m, false)],
        ClaseManual: clase, SubtipoVentaDirecta: subtipo, MotivoEmergencia: motivo);

    [Theory]
    [InlineData(null, null, null, ComportamientoFiscal.MostradorInmediato, "PEDIDO_MANUAL_CLASE_REQUERIDA")]
    [InlineData(ClasePedidoManual.VentaDirecta, null, null, ComportamientoFiscal.MostradorInmediato, "PEDIDO_VD_SUBTIPO_REQUERIDO")]
    [InlineData(ClasePedidoManual.Emergencia, null, null, ComportamientoFiscal.MostradorInmediato, "PEDIDO_EMERGENCIA_MOTIVO_REQUERIDO")]
    [InlineData(ClasePedidoManual.Emergencia, SubtipoVentaDirecta.Servicio, "caído", ComportamientoFiscal.MostradorInmediato, "PEDIDO_EMERGENCIA_SIN_SUBTIPO")]
    [InlineData(ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Activo, null, ComportamientoFiscal.MostradorInmediato, "PEDIDO_ACTIVO_COMPORTAMIENTO")]
    [InlineData(ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio, null, ComportamientoFiscal.VentaActivoFijo, "PEDIDO_ACTIVO_COMPORTAMIENTO")]
    public async Task Validador_rechaza_clasificacion_invalida(
        ClasePedidoManual? clase, SubtipoVentaDirecta? subtipo, string? motivo, ComportamientoFiscal comportamiento, string codigo)
    {
        var validator = new CrearPedidoFacturableManualValidator(new FakeCanalesVentaReadPort(todoActivo: true));
        var r = await validator.ValidateAsync(Cmd(clase, subtipo, motivo, comportamiento));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorCode == codigo);
    }

    [Fact]
    public async Task Validador_rechaza_subtipo_fuera_de_catalogo()
    {
        var validator = new CrearPedidoFacturableManualValidator(new FakeCanalesVentaReadPort(todoActivo: true));
        var r = await validator.ValidateAsync(Cmd(ClasePedidoManual.VentaDirecta, (SubtipoVentaDirecta)9));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validador_acepta_emergencia_y_activo_validos()
    {
        var validator = new CrearPedidoFacturableManualValidator(new FakeCanalesVentaReadPort(todoActivo: true));
        (await validator.ValidateAsync(Cmd(ClasePedidoManual.Emergencia, motivo: "A+W caído"))).IsValid.Should().BeTrue();
        (await validator.ValidateAsync(Cmd(ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Activo,
            comportamiento: ComportamientoFiscal.VentaActivoFijo))).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validador_de_edicion_exige_clase()
    {
        var validator = new EditarPedidoFacturableManualValidator(new FakeCanalesVentaReadPort(todoActivo: true));
        var r = await validator.ValidateAsync(new EditarPedidoFacturableManualCommand(
            Guid.NewGuid(), 1, Guid.NewGuid(), "Cliente", 1, ComportamientoFiscal.MostradorInmediato, "MXN",
            null, null, null, [new PedidoFacturableLineaInput(null, "P", null, null, 1m, 1m, 0m, false)]));
        r.Errors.Should().Contain(e => e.ErrorCode == "PEDIDO_MANUAL_CLASE_REQUERIDA");
    }

    // ----- Handlers: permiso de emergencia (403) -----

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(empresaId));

    private static CrearPedidoFacturableManualHandler Crear(FacturacionDbContext db, Guid empresaId, params string[] permisos) =>
        new(db, new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeProductosReadPort(),
            new FakeEmpresaFiscalReadPort(), new FakeCurrentUserPermissions(permisos));

    [Fact]
    public async Task Crear_emergencia_sin_permiso_lanza_Forbidden()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var act = () => Crear(db, empresaId).Handle(Cmd(ClasePedidoManual.Emergencia, motivo: "A+W caído"), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Code.Should().Be("PEDIDO_EMERGENCIA_SIN_PERMISO");
        db.PedidosFacturables.Should().BeEmpty();
    }

    [Fact]
    public async Task Crear_emergencia_con_permiso_persiste_clasificacion()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await Crear(db, empresaId, PermisoEmergenciaPedido.PermisoCapturarEmergencia)
            .Handle(Cmd(ClasePedidoManual.Emergencia, motivo: "A+W caído"), CancellationToken.None);

        var p = await db.PedidosFacturables.SingleAsync();
        p.ClaseManual.Should().Be(ClasePedidoManual.Emergencia);
        p.MotivoEmergencia.Should().Be("A+W caído");
    }

    [Fact]
    public async Task Editar_a_emergencia_sin_permiso_lanza_Forbidden()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var creado = await Crear(db, empresaId).Handle(Cmd(ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio), CancellationToken.None);

        var editar = new EditarPedidoFacturableManualHandler(db, new FakeEmpresaContext(empresaId),
            new FakeProductosReadPort(), new FakeEmpresaFiscalReadPort(), new FakeCurrentUserPermissions());
        var act = () => editar.Handle(new EditarPedidoFacturableManualCommand(
            creado.Id, creado.Version, Guid.NewGuid(), "Cliente", 1, ComportamientoFiscal.MostradorInmediato, "MXN",
            null, null, null, [new PedidoFacturableLineaInput(null, "P", null, null, 1m, 1m, 0m, false)],
            ClasePedidoManual.Emergencia, null, "A+W caído"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // ----- Consultas: proyección y filtro por clase -----

    [Fact]
    public async Task Bandeja_pedidos_proyecta_y_filtra_por_clase()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var crear = Crear(db, empresaId, PermisoEmergenciaPedido.PermisoCapturarEmergencia);
        await crear.Handle(Cmd(ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio), CancellationToken.None);
        await crear.Handle(Cmd(ClasePedidoManual.Emergencia, motivo: "A+W caído"), CancellationToken.None);

        var bandeja = new BandejaPedidosFacturablesHandler(db, new FakeAlcanceCajaEvaluator(), new FakeCurrentUserPermissions());
        var todos = await bandeja.Handle(new BandejaPedidosFacturablesQuery(null, null, 0, 50), CancellationToken.None);
        todos.Items.Should().HaveCount(2);
        todos.Items.Should().Contain(i => i.ClaseManual == "VentaDirecta" && i.SubtipoVentaDirecta == "Servicio" && !i.EsEmergencia);

        var emergencias = await bandeja.Handle(
            new BandejaPedidosFacturablesQuery(null, null, 0, 50, Clase: ClasePedidoManual.Emergencia), CancellationToken.None);
        emergencias.Items.Should().ContainSingle().Which.EsEmergencia.Should().BeTrue();
    }

    [Fact]
    public async Task Bandeja_facturas_marca_emergencia_por_el_pedido_de_origen()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var emergencia = Manual(ClasePedidoManual.Emergencia, motivo: "A+W caído");
        emergencia.EmpresaId = empresaId;
        db.PedidosFacturables.Add(emergencia);
        db.FacturasVenta.Add(Factura(empresaId, "VD-MID-000001", 1, emergencia.Id));
        db.FacturasVenta.Add(Factura(empresaId, "F-000002", 2, null));
        await db.SaveChangesAsync();

        var handler = new BandejaFacturasHandler(db, new FakeAlcanceCajaEvaluator());
        var todas = await handler.Handle(new BandejaFacturasQuery(null, 0, 50), CancellationToken.None);
        todas.Items.Should().HaveCount(2);
        todas.Items.Single(i => i.Folio == "VD-MID-000001").EsEmergencia.Should().BeTrue();
        todas.Items.Single(i => i.Folio == "VD-MID-000001").ClaseManual.Should().Be("Emergencia");
        todas.Items.Single(i => i.Folio == "F-000002").ClaseManual.Should().BeNull();

        var filtradas = await handler.Handle(new BandejaFacturasQuery(null, 0, 50, Clase: ClasePedidoManual.Emergencia), CancellationToken.None);
        filtradas.Items.Should().ContainSingle().Which.Folio.Should().Be("VD-MID-000001");
    }

    private static FacturaVenta Factura(Guid empresaId, string folio, long numero, Guid? pedidoId)
    {
        var receptor = new DatosFiscalesReceptor("XAXX010101000", "Público en general", "616", "76120", "S01", "MEX", true);
        var emisor = new DatosFiscalesEmisor("AAA010101AAA", "Emisor", "601", "76120");
        var f = FacturaVenta.CrearBorrador(empresaId, folio, numero, Guid.NewGuid(), null, null, receptor, emisor,
            "PUE", "01", "MXN", null, 2026, 10, 1, ComportamientoFiscal.MostradorInmediato, pedidoId, null, null, false);
        f.AgregarLinea(null, "01010101", "Producto", "H87", 1, 100, 0, "02", 0.16m, null, null);
        f.RecalcularTotales();
        return f;
    }
}
