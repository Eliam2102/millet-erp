using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Application.FoliosTransferencia;
using Millet.Tesoreria.Application.PublicPorts;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.FoliosTransferencia;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;
using Millet.Tesoreria.Infrastructure.PublicAdapters;

namespace Millet.Tesoreria.UnitTests.FoliosTransferencia;

/// <summary>
/// F6.6 (R16, CA6.8): Ingresos emite el folio de confirmación desde un ingreso
/// de cliente; el ingreso deja de estar «por identificar»; varios folios hasta
/// el total; cancelación con motivo; Caja lo consume con el evento del cobro y
/// lo libera si el cobro se cancela.
/// </summary>
public sealed class FoliosTransferenciaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Ingresos = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000f6");
    private static readonly Guid Cliente = Guid.Parse("cccccccc-0000-0000-0000-0000000000f6");
    private static readonly DateTimeOffset Ahora = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Fecha = new(2026, 10, 31);

    [Fact]
    public async Task Criterio_1_emite_TR_000001_vigente_y_el_ingreso_deja_de_estar_por_identificar()
    {
        await using var db = Db();
        var ingreso = Ingreso(db, 8_000m);
        await db.SaveChangesAsync();
        var adapter = new DepositosPorIdentificarReadAdapter(db);
        (await adapter.ObtenerAsync(ingreso.Id, default)).Should().NotBeNull();

        var folio = await Emitir(db, ingreso.Id, 8_000m, "PED-100");

        folio.Folio.Should().Be("TR-000001");
        folio.Estado.Should().Be(EstadoFolioTransferencia.Vigente);
        folio.Importe.Should().Be(8_000m);
        folio.Pedido.Should().Be("PED-100");
        folio.ClienteRazonSocial.Should().Be("Cliente SA");
        (await adapter.ObtenerAsync(ingreso.Id, default)).Should().BeNull();
    }

    [Fact]
    public async Task Criterio_6_varios_folios_hasta_el_total_y_el_que_excede_se_rechaza()
    {
        await using var db = Db();
        var ingreso = Ingreso(db, 8_000m);
        await db.SaveChangesAsync();

        (await Emitir(db, ingreso.Id, 5_000m)).Folio.Should().Be("TR-000001");
        var excede = () => Emitir(db, ingreso.Id, 4_000m);
        (await excede.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("FOLIO_TR_EXCEDE_INGRESO");
        (await Emitir(db, ingreso.Id, 3_000m)).Folio.Should().Be("TR-000002");
    }

    [Fact]
    public async Task Folio_cancelado_libera_su_importe_del_ingreso()
    {
        await using var db = Db();
        var ingreso = Ingreso(db, 8_000m);
        await db.SaveChangesAsync();
        var folio = await Emitir(db, ingreso.Id, 8_000m);

        var cancelado = await new CancelarFolioTransferenciaHandler(db, new User(), new Clientes(), new Clock())
            .Handle(new CancelarFolioTransferenciaCommand(folio.Id, "Abono no corresponde al pedido"), default);

        cancelado.Estado.Should().Be(EstadoFolioTransferencia.Cancelado);
        cancelado.MotivoCancelacion.Should().Be("Abono no corresponde al pedido");
        (await new DepositosPorIdentificarReadAdapter(db).ObtenerAsync(ingreso.Id, default)).Should().NotBeNull();
        (await Emitir(db, ingreso.Id, 8_000m)).Folio.Should().Be("TR-000002");
    }

    [Fact]
    public async Task No_emite_desde_ingreso_en_dolares_de_otro_cliente_o_revertido()
    {
        await using var db = Db();
        var dolares = Ingreso(db, 1_000m, moneda: "USD");
        var deOtro = Ingreso(db, 1_000m, cliente: Guid.NewGuid());
        var revertido = Ingreso(db, 1_000m);
        db.MovimientosBancarios.Add(revertido.CrearContramovimiento(1_000m, Fecha, Ingresos, Ahora, "Duplicado"));
        await db.SaveChangesAsync();

        (await ((Func<Task>)(() => Emitir(db, dolares.Id, 1_000m))).Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("FOLIO_TR_MONEDA");
        (await ((Func<Task>)(() => Emitir(db, deOtro.Id, 1_000m))).Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("FOLIO_TR_CLIENTE_DISTINTO");
        (await ((Func<Task>)(() => Emitir(db, revertido.Id, 1_000m))).Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("FOLIO_TR_INGRESO_REVERTIDO");
    }

    [Fact]
    public async Task Cobro_de_caja_lo_marca_usado_ya_no_se_cancela_y_al_cancelar_el_cobro_vuelve_a_vigente()
    {
        await using var db = Db();
        var ingreso = Ingreso(db, 8_000m);
        await db.SaveChangesAsync();
        var folio = await Emitir(db, ingreso.Id, 8_000m);
        var cobroId = Guid.NewGuid();
        var handlers = new FoliosTransferenciaCobroHandlers(db, new Clock(), NullLogger<FoliosTransferenciaCobroHandlers>.Instance);

        var registrado = new CobroMostradorRegistradoPayload(Empresa, Ahora, cobroId,
            [new CobroFormaPagoPayload("03", 8_000m, "tr-000001"), new CobroFormaPagoPayload("01", 100m)]);
        await handlers.Handle(new MarcarFoliosTransferenciaUsadosCommand(Guid.NewGuid(), registrado), default);
        await handlers.Handle(new MarcarFoliosTransferenciaUsadosCommand(Guid.NewGuid(), registrado), default);

        var usado = await db.FoliosTransferencia.AsNoTracking().SingleAsync(f => f.Id == folio.Id);
        usado.Estado.Should().Be(EstadoFolioTransferencia.Usado);
        usado.CobroMostradorId.Should().Be(cobroId);
        var cancelar = () => new CancelarFolioTransferenciaHandler(db, new User(), new Clientes(), new Clock())
            .Handle(new CancelarFolioTransferenciaCommand(folio.Id, "x"), default);
        (await cancelar.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("FOLIO_TR_USADO");
        db.ChangeTracker.Clear();

        await handlers.Handle(new LiberarFoliosTransferenciaCommand(Guid.NewGuid(),
            new CobroMostradorCanceladoPayload(Empresa, Ahora, cobroId)), default);

        var liberado = await db.FoliosTransferencia.AsNoTracking().SingleAsync(f => f.Id == folio.Id);
        liberado.Estado.Should().Be(EstadoFolioTransferencia.Vigente);
        liberado.CobroMostradorId.Should().BeNull();
    }

    [Fact]
    public async Task Puerto_publico_devuelve_folios_por_numero_y_los_vigentes_del_cliente()
    {
        await using var db = Db();
        var ingreso = Ingreso(db, 8_000m);
        await db.SaveChangesAsync();
        await Emitir(db, ingreso.Id, 5_000m);
        var segundo = await Emitir(db, ingreso.Id, 3_000m);
        await new CancelarFolioTransferenciaHandler(db, new User(), new Clientes(), new Clock())
            .Handle(new CancelarFolioTransferenciaCommand(segundo.Id, "Error de captura"), default);
        var puerto = new ConsultarFoliosTransferenciaHandler(db);

        var porNumero = await puerto.Handle(new ConsultarFoliosTransferenciaQuery(["tr-000001", "TR-000002", "TR-999999"]), default);
        porNumero.Select(f => (f.Folio, f.Estado)).Should().BeEquivalentTo([("TR-000001", "Vigente"), ("TR-000002", "Cancelado")]);

        var vigentes = await puerto.Handle(new FoliosTransferenciaVigentesClienteQuery(Cliente), default);
        vigentes.Should().ContainSingle().Which.Importe.Should().Be(5_000m);
    }

    // --------------------------------------------------- helpers

    private static Task<FolioTransferenciaResponse> Emitir(TesoreriaDbContext db, Guid movimientoId, decimal importe, string? pedido = null) =>
        new EmitirFolioTransferenciaHandler(db, new EmpresaContext(), new User(), new Clientes(), new Clock())
            .Handle(new EmitirFolioTransferenciaCommand(movimientoId, Cliente, importe, pedido), default);

    private static TesoreriaDbContext Db() => new(new DbContextOptionsBuilder<TesoreriaDbContext>()
        .UseInMemoryDatabase($"tes_f66_{Guid.NewGuid()}").Options, new EmpresaContext());

    private static MovimientoBancario Ingreso(TesoreriaDbContext db, decimal monto, Guid? cliente = null, string moneda = "MXN")
    {
        var cuenta = new CuentaBancaria(Empresa, "BBVA", $"01234{Random.Shared.Next(10000, 99999)}", null, moneda);
        db.CuentasBancarias.Add(cuenta);
        var m = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, monto, Fecha, "SPEI-F66", null,
            BeneficiarioTipo.Cliente, cliente, Ingresos, Ahora);
        db.MovimientosBancarios.Add(m);
        return m;
    }

    private sealed class EmpresaContext : ICurrentEmpresaContext
    { public Guid? Current => Empresa; public bool IsBypassed => true; public IDisposable Bypass() => new Noop(); private sealed class Noop : IDisposable { public void Dispose() { } } }
    private sealed class User : ICurrentUserContext { public Guid? UserId => Ingresos; public string? UserName => "Ingresos"; }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Ahora; }
    private sealed class Clientes : IClienteReadPort
    {
        public Task<IReadOnlyDictionary<Guid, ClienteRefDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ClienteRefDto>>(ids.ToDictionary(i => i, i => new ClienteRefDto(i, "C1", "Cliente SA")));
        public Task<IReadOnlyList<ClienteRefDto>> BuscarAsync(string texto, int limite, CancellationToken ct) => Task.FromResult<IReadOnlyList<ClienteRefDto>>([]);
    }
}
