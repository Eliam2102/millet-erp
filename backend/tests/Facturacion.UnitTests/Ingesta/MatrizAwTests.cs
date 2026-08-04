using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Ingesta.ProcesarSolicitudAw;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Ingesta;

public sealed class MatrizAwTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static DatosPedidoAw Datos(string moneda = "MXN", string numero = "AW-1", decimal? ranura = null) => new(
        numero, Guid.NewGuid(), "CLI-1", "Cliente A+W", (short)1,
        ComportamientoFiscal.MostradorInmediato, moneda, null, null, "comentario interno", "15",
        [new LineaPedidoAw("PRD-1", "Producto A+W", "01010101", "H87", 2m, 100m, 0m, false)],
        "{\"pedido\":\"" + numero + "\"}",
        Ranura: ranura);

    private static ClienteFiscalLectura Cliente() =>
        new(Guid.NewGuid(), "AAA010101AAA", "Cliente A+W", "601", "97000", "G03", "01", "PUE", "MXN", false);

    private static SolicitudAw Sol(OperacionAw op, long version = 1, string numero = "AW-1") =>
        new(Guid.NewGuid(), numero, op, version, Ahora);

    private static ProcesarSolicitudAwHandler Handler(
        FacturacionDbContext db, FakeAwWriteBackPort wb, DatosPedidoAw? datos, ClienteFiscalLectura? cliente,
        ClienteFiscalLectura? clienteProvisionado = null, LecturaPedidoAw? lectura = null,
        int pospuestaMaxDias = 7, DateTimeOffset? ahora = null) =>
        new(db, new FakeAwSolicitudesReader(datos, lectura: lectura), wb, new FakeClientesReadPort(cliente),
            new FakeProductosReadPort(), new FakeMasterProvisioningPort(clienteProvisionado),
            new FakeClock(ahora ?? Ahora),
            Options.Create(new AwSolicitudesOptions { PospuestaMaxDias = pospuestaMaxDias }));

    private static async Task<(PedidoFacturable, IngestaControl)> SembrarPedidoAsync(
        FacturacionDbContext db, Guid empresaId, EstadoPedidoFacturable estado)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(empresaId, "AW-1", Guid.NewGuid(), Guid.NewGuid(), "Cliente A+W",
            (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        pedido.AgregarLinea(null, "Producto", "01010101", "H87", 1m, 100m, 0m, false);
        pedido.RecalcularTotal();
        if (estado == EstadoPedidoFacturable.Facturado) pedido.MarcarFacturado(Guid.NewGuid());

        var control = IngestaControl.Crear(empresaId, OrigenPedido.Aw, "AW-1", "hash", 1,
            estado == EstadoPedidoFacturable.Facturado ? EstadoIngesta.Facturado : EstadoIngesta.Importado, pedido.Id, Ahora);

        db.PedidosFacturables.Add(pedido);
        db.IngestaControles.Add(control);
        await db.SaveChangesAsync();
        return (pedido, control);
    }

    // ---- Alta ----

    [Fact]
    public async Task Alta_con_cliente_crea_pedido_control_y_snapshot()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var wb = new FakeAwWriteBackPort();

        var resp = await Handler(db, wb, Datos(), Cliente())
            .Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Alta)), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Aplicada);
        resp.PedidoFacturableId.Should().NotBeNull();
        (await db.PedidosFacturables.SingleAsync()).Origen.Should().Be(OrigenPedido.Aw);
        await db.IngestaControles.SingleAsync();
        await db.PedidosFacturablesSnapshot.SingleAsync();
        wb.Ultimo!.Resultado.Should().Be(ResultadoSolicitudAw.Aplicada);
        wb.Ultimo!.EstadoFacturacion.Should().Be("SinFacturar");
    }

    [Fact]
    public async Task Alta_sin_cliente_pospone_con_excepcion_ClienteNoExiste()
    {
        // FAC-ING-PR3: cliente no provisionable es corregible en el ERP →
        // Pospuesta (la solicitud sigue en la cola), no Rechazada. El control
        // NO se toca: bumpearlo activaría el candado de idempotencia al reintentar.
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var wb = new FakeAwWriteBackPort();

        var resp = await Handler(db, wb, Datos(), cliente: null)
            .Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Alta)), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Pospuesta);
        (await db.ExcepcionesImportacion.SingleAsync()).Motivo.Should().Be(MotivoExcepcion.ClienteNoExiste);
        (await db.PedidosFacturables.CountAsync()).Should().Be(0);
        (await db.IngestaControles.CountAsync()).Should().Be(0);
        wb.Ultimo!.Resultado.Should().Be(ResultadoSolicitudAw.Pospuesta);
    }

    [Fact]
    public async Task Alta_sin_cliente_con_tope_cero_escala_a_rechazo()
    {
        // PospuestaMaxDias = 0 → rechazo inmediato (comportamiento previo).
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var wb = new FakeAwWriteBackPort();

        var resp = await Handler(db, wb, Datos(), cliente: null, pospuestaMaxDias: 0)
            .Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Alta)), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        resp.Motivo.Should().Contain("escalado a rechazo");
        var excepcion = await db.ExcepcionesImportacion.SingleAsync();
        excepcion.Detalle.Should().Contain("escalado a rechazo");
        (await db.IngestaControles.SingleAsync()).Estado.Should().Be(EstadoIngesta.Excepcion);
    }

    [Fact]
    public async Task Alta_config_fixable_pospone_sin_duplicar_excepcion()
    {
        // Canal sin clave_aw (config-fixable del reader): dos ticks → UNA sola
        // excepción abierta en la bandeja (upsert), pospuesta en ambos.
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var wb = new FakeAwWriteBackPort();
        var lectura = LecturaPedidoAw.EsperaConfig(MotivoExcepcion.CanalVentaSinClaveAw,
            "canal_ventas 'Ventas Playa del Carmen' sin canal activo con esa clave_aw en el catálogo");
        var handler = Handler(db, wb, datos: null, cliente: Cliente(), lectura: lectura);

        var r1 = await handler.Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Alta)), CancellationToken.None);
        var r2 = await handler.Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Alta)), CancellationToken.None);

        r1.Resultado.Should().Be(ResultadoSolicitudAw.Pospuesta);
        r2.Resultado.Should().Be(ResultadoSolicitudAw.Pospuesta);
        var excepcion = await db.ExcepcionesImportacion.SingleAsync();
        excepcion.Motivo.Should().Be(MotivoExcepcion.CanalVentaSinClaveAw);
        excepcion.Detalle.Should().Contain("Ventas Playa del Carmen");
        excepcion.Resuelto.Should().BeFalse();
    }

    [Fact]
    public async Task Alta_datos_invalidos_rechaza_como_antes()
    {
        // Datos defectuosos del origen (no config): rechazo inmediato con
        // control en Excepcion — A+W corrige y reenvía con versión nueva.
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var wb = new FakeAwWriteBackPort();
        var lectura = LecturaPedidoAw.DatosInvalidos(MotivoExcepcion.TotalesNoCuadran,
            "total_cantidad de cabecera (3) no cuadra con las líneas (6)");

        var resp = await Handler(db, wb, datos: null, cliente: Cliente(), lectura: lectura)
            .Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Alta)), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        (await db.ExcepcionesImportacion.SingleAsync()).Motivo.Should().Be(MotivoExcepcion.TotalesNoCuadran);
        (await db.IngestaControles.SingleAsync()).Estado.Should().Be(EstadoIngesta.Excepcion);
    }

    [Fact]
    public async Task Alta_aplicada_resuelve_la_excepcion_abierta()
    {
        // El ciclo completo del fix: pospuesta por catálogo → operador corrige
        // → siguiente tick aplica y la excepción de bandeja se resuelve sola.
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var wb = new FakeAwWriteBackPort();
        var lectura = LecturaPedidoAw.EsperaConfig(MotivoExcepcion.CanalVentaSinClaveAw,
            "canal_ventas 'X' sin canal activo con esa clave_aw en el catálogo");
        await Handler(db, wb, datos: null, cliente: Cliente(), lectura: lectura)
            .Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Alta)), CancellationToken.None);

        var resp = await Handler(db, wb, Datos(), Cliente())
            .Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Alta)), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Aplicada);
        (await db.PedidosFacturables.CountAsync()).Should().Be(1);
        var excepcion = await db.ExcepcionesImportacion.SingleAsync();
        excepcion.Resuelto.Should().BeTrue();
        excepcion.ResueltoPor.Should().BeNull();
    }

    [Fact]
    public async Task Alta_sin_cliente_pero_con_autoprovision_crea_pedido()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var wb = new FakeAwWriteBackPort();

        // No existe en el master (cliente: null) pero la auto-provisión A+W lo crea.
        var resp = await Handler(db, wb, Datos(), cliente: null, clienteProvisionado: Cliente())
            .Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Alta)), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Aplicada);
        (await db.PedidosFacturables.CountAsync()).Should().Be(1);
        (await db.ExcepcionesImportacion.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Alta_version_repetida_es_idempotente()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var handler = Handler(db, new FakeAwWriteBackPort(), Datos(), Cliente());

        await handler.Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Alta, version: 1)), CancellationToken.None);
        await handler.Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Alta, version: 1)), CancellationToken.None);

        (await db.PedidosFacturables.CountAsync()).Should().Be(1);
    }

    // ---- Modificación ----

    [Fact]
    public async Task Modificacion_sobre_Importado_refresca_y_sube_version()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await SembrarPedidoAsync(db, empresaId, EstadoPedidoFacturable.Importado);
        var wb = new FakeAwWriteBackPort();

        var resp = await Handler(db, wb, Datos(moneda: "USD"), Cliente())
            .Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Modificacion, version: 2)), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Aplicada);
        var pedido = await db.PedidosFacturables.SingleAsync();
        pedido.Moneda.Should().Be("USD");
        pedido.VersionOrigen.Should().Be(2);
    }

    [Fact]
    public async Task Alta_con_ranura_la_persiste_en_el_pedido()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var wb = new FakeAwWriteBackPort();

        var resp = await Handler(db, wb, Datos(ranura: 250.50m), Cliente())
            .Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Alta)), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Aplicada);
        (await db.PedidosFacturables.SingleAsync()).Ranura.Should().Be(250.50m);
    }

    [Fact]
    public async Task Modificacion_refresca_la_ranura_y_la_limpia_si_ya_no_viene()
    {
        // Cada versión del origen manda: ranura nueva reemplaza; sin ranura → null.
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await SembrarPedidoAsync(db, empresaId, EstadoPedidoFacturable.Importado);
        var wb = new FakeAwWriteBackPort();

        await Handler(db, wb, Datos(ranura: 100m), Cliente())
            .Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Modificacion, version: 2)), CancellationToken.None);
        (await db.PedidosFacturables.SingleAsync()).Ranura.Should().Be(100m);

        await Handler(db, wb, Datos(), Cliente())
            .Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Modificacion, version: 3)), CancellationToken.None);
        (await db.PedidosFacturables.SingleAsync()).Ranura.Should().BeNull();
    }

    [Fact]
    public async Task Modificacion_sobre_Facturado_rechaza_la_factura_manda()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await SembrarPedidoAsync(db, empresaId, EstadoPedidoFacturable.Facturado);
        var wb = new FakeAwWriteBackPort();

        var resp = await Handler(db, wb, Datos(), Cliente())
            .Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Modificacion, version: 2)), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        (await db.ExcepcionesImportacion.SingleAsync()).Motivo.Should().Be(MotivoExcepcion.ModificacionSobreFacturado);
    }

    // ---- Cancelación ----

    [Fact]
    public async Task Cancelacion_sobre_Importado_cancela_el_pedido()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await SembrarPedidoAsync(db, empresaId, EstadoPedidoFacturable.Importado);
        var wb = new FakeAwWriteBackPort();

        var resp = await Handler(db, wb, Datos(), Cliente())
            .Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Cancelacion, version: 2)), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Aplicada);
        (await db.PedidosFacturables.SingleAsync()).Estado.Should().Be(EstadoPedidoFacturable.Cancelado);
        wb.Ultimo!.EstadoFacturacion.Should().Be("Cancelado");
    }

    [Fact]
    public async Task Cancelacion_sobre_Facturado_rechaza_la_factura_manda()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await SembrarPedidoAsync(db, empresaId, EstadoPedidoFacturable.Facturado);
        var wb = new FakeAwWriteBackPort();

        var resp = await Handler(db, wb, Datos(), Cliente())
            .Handle(new ProcesarSolicitudAwCommand(empresaId, Sol(OperacionAw.Cancelacion, version: 2)), CancellationToken.None);

        resp.Resultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        (await db.ExcepcionesImportacion.SingleAsync()).Motivo.Should().Be(MotivoExcepcion.CancelacionSobreFacturado);
    }
}
