using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.Tesoreria.Application.Cheques;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Cheques;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests;

/// <summary>M-372 · cheques recibidos: no suman al saldo hasta confirmarse, se emparejan con el estado de cuenta y el rebote compensa.</summary>
public sealed class ChequesRecibidosTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Recepcion = new(2026, 11, 5);

    [Fact]
    public async Task Registrar_no_crea_movimiento_y_el_saldo_no_cambia()
    {
        await using var db = Db();
        var cuenta = await CuentaAsync(db);
        var r = await new RegistrarChequeRecibidoHandler(db, new EmpresaCtx(), new UsuarioCtx(), new Clientes(), new Reloj())
            .Handle(new(cuenta.Id, Cliente, " Banamex ", "001234", "Constructora Sol", 18_500m, Recepcion), default);

        (r.Estado, r.ClienteNombre, r.BancoEmisor).Should().Be((EstadoChequeRecibido.EnFirmePendiente, "Cliente prueba", "Banamex"));
        (await db.MovimientosBancarios.CountAsync()).Should().Be(0);

        var otra = () => new RegistrarChequeRecibidoHandler(db, new EmpresaCtx(), new UsuarioCtx(), new Clientes(), new Reloj())
            .Handle(new(cuenta.Id, Cliente, "Banamex", "001234", "Otro", 1m, Recepcion), default);
        await otra.Should().ThrowAsync<ConflictException>().Where(e => e.Code == "CHEQUE_DUPLICADO");
    }

    [Fact]
    public async Task Confirmar_contra_la_linea_del_estado_de_cuenta_crea_el_ingreso_conciliado()
    {
        await using var db = Db();
        var cuenta = await CuentaAsync(db);
        var cheque = await ChequeAsync(db, cuenta);
        var conc = Conciliacion.Crear(Empresa, cuenta.Id, 2026, 11);
        db.Conciliaciones.Add(conc);
        var linea = ExtractoLinea.Crear(conc.Id, 1, new DateOnly(2026, 11, 7), "DEP CHEQUE 001234", "778812", null, 18_500m);
        var otra = ExtractoLinea.Crear(conc.Id, 2, new DateOnly(2026, 11, 7), "SPEI", "1", null, 999m);
        db.ExtractoLineas.AddRange(linea, otra);
        await db.SaveChangesAsync();

        var candidatas = await new LineasCandidatasChequeHandler(db).Handle(new(cheque.Id), default);
        candidatas.Select(c => c.ExtractoLineaId).Should().Equal(linea.Id);

        var mal = () => Confirmar(db).Handle(new(cheque.Id, otra.Id, null, null), default);
        await mal.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "MATCH_IMPORTE_NO_COINCIDE");

        var r = await Confirmar(db).Handle(new(cheque.Id, linea.Id, null, null), default);
        var mov = await db.MovimientosBancarios.SingleAsync();
        (r.Estado, r.MovimientoId, r.ExtractoLineaId).Should().Be((EstadoChequeRecibido.Disponible, mov.Id, linea.Id));
        (mov.Monto, mov.FechaValor, mov.ReferenciaBancaria, mov.EstadoConciliacion, mov.BeneficiarioRef)
            .Should().Be((18_500m, new DateOnly(2026, 11, 7), "778812", EstadoConciliacionMovimiento.Conciliado, (Guid?)Cliente));
        (linea.Estado, linea.MatchMovimientoId).Should().Be((EstadoExtractoLinea.Emparejada, (Guid?)mov.Id));
    }

    [Fact]
    public async Task Confirmar_a_mano_exige_referencia_y_no_se_confirma_dos_veces()
    {
        await using var db = Db();
        var cuenta = await CuentaAsync(db);
        var cheque = await ChequeAsync(db, cuenta);
        new ConfirmarChequeRecibidoValidator().Validate(new ConfirmarChequeRecibidoCommand(cheque.Id, null, Recepcion, " "))
            .Errors.Should().Contain(e => e.ErrorMessage == "Indica la referencia del banco.");

        await Confirmar(db).Handle(new(cheque.Id, null, new DateOnly(2026, 11, 6), "FOLIO 55"), default);
        (await db.MovimientosBancarios.SingleAsync()).EstadoConciliacion.Should().Be(EstadoConciliacionMovimiento.NoConciliado);
        var otra = () => Confirmar(db).Handle(new(cheque.Id, null, new DateOnly(2026, 11, 6), "FOLIO 55"), default);
        await otra.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CHEQUE_NO_PENDIENTE");
    }

    [Fact]
    public async Task Rebote_de_cheque_disponible_genera_contramovimiento_y_avisa_a_CxC()
    {
        await using var db = Db();
        var cuenta = await CuentaAsync(db);
        var cheque = await ChequeAsync(db, cuenta);
        await Confirmar(db).Handle(new(cheque.Id, null, new DateOnly(2026, 11, 6), "FOLIO 55"), default);
        var pub = new Publicador();

        var r = await new RebotarChequeRecibidoHandler(db, new UsuarioCtx(), new Periodo(), pub, new Clientes(), new Reloj())
            .Handle(new(cheque.Id, new DateOnly(2026, 11, 9), "Fondos insuficientes"), default);

        var movs = await db.MovimientosBancarios.ToListAsync();
        movs.Sum(m => m.Sentido == SentidoMovimiento.Ingreso ? m.Monto : -m.Monto).Should().Be(0m);
        var contra = movs.Single(m => m.Sentido == SentidoMovimiento.Egreso);
        (r.Estado, r.ContramovimientoId, contra.ContramovimientoDe).Should().Be((EstadoChequeRecibido.Rebotado, (Guid?)contra.Id, r.MovimientoId));
        pub.Eventos.Should().ContainSingle().Which.Should().BeOfType<ChequeRebotadoIntegrationEvent>()
            .Which.Should().Match<ChequeRebotadoIntegrationEvent>(e => e.ClienteId == Cliente && e.Contramovimiento && e.Motivo == "Fondos insuficientes");
    }

    [Fact]
    public void Rebote_de_cheque_pendiente_no_mueve_saldo_y_exige_motivo()
    {
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "0110374468", null, "MXN");
        var cheque = ChequeRecibido.Registrar(Empresa, cuenta, Cliente, "Banamex", "9", "Juan", 100m, Recepcion, Usuario, Ahora);
        ((Action)(() => cheque.Rebotar(null, Recepcion, " ", Usuario, Ahora))).Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CHEQUE_MOTIVO_OBLIGATORIO");
        cheque.Rebotar(null, Recepcion, "Firma no coincide", Usuario, Ahora).Should().BeNull();
        cheque.Estado.Should().Be(EstadoChequeRecibido.Rebotado);
        ((Action)(() => cheque.Rebotar(null, Recepcion, "otra", Usuario, Ahora))).Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CHEQUE_YA_REBOTADO");
    }

    // ------------------------------------------------------------ dobles

    private static ConfirmarChequeRecibidoHandler Confirmar(TesoreriaDbContext db) =>
        new(db, new UsuarioCtx(), new Periodo(), new Clientes(), new Reloj());

    private static async Task<CuentaBancaria> CuentaAsync(TesoreriaDbContext db)
    {
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "0110374468", null, "MXN");
        db.CuentasBancarias.Add(cuenta);
        await db.SaveChangesAsync();
        return cuenta;
    }

    private static async Task<ChequeRecibido> ChequeAsync(TesoreriaDbContext db, CuentaBancaria cuenta)
    {
        var cheque = ChequeRecibido.Registrar(Empresa, cuenta, Cliente, "Banamex", "001234", "Constructora Sol", 18_500m, Recepcion, Usuario, Ahora);
        db.ChequesRecibidos.Add(cheque);
        await db.SaveChangesAsync();
        return cheque;
    }

    private static TesoreriaDbContext Db() =>
        new(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaCtx());

    private sealed class EmpresaCtx : ICurrentEmpresaContext
    { public Guid? Current => Empresa; public bool IsBypassed => false; public IDisposable Bypass() => new Noop(); private sealed class Noop : IDisposable { public void Dispose() { } } }
    private sealed class UsuarioCtx : ICurrentUserContext { public Guid? UserId => Usuario; public string? UserName => "Prueba"; }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => Ahora; }
    private sealed class Periodo : IPeriodoContablePort { public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true); }
    private sealed class Publicador : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken) { Eventos.Add(integrationEvent); return Task.CompletedTask; }
    }
    private sealed class Clientes : IClienteReadPort
    {
        public Task<IReadOnlyDictionary<Guid, ClienteRefDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> clienteIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ClienteRefDto>>(clienteIds.Where(i => i == Cliente)
                .ToDictionary(i => i, i => new ClienteRefDto(i, "C-1", "Cliente prueba")));
        public Task<IReadOnlyList<ClienteRefDto>> BuscarAsync(string texto, int limite, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ClienteRefDto>>([]);
    }
}
