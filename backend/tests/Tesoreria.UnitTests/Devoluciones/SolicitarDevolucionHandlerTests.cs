using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Devoluciones;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Devoluciones;

public sealed class SolicitarDevolucionHandlerTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly CuentaDestinoDevolucion Destino = new("BBVA", "032180000118359719", "Cliente SA");

    private static TesoreriaDbContext Db() => new(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaContext());

    private static async Task<MovimientoBancario> Deposito(TesoreriaDbContext db, Guid? beneficiario = null)
    {
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, "MXN");
        var m = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 1_000m, new DateOnly(2026, 10, 17), null, null,
            beneficiario is null ? null : BeneficiarioTipo.Cliente, beneficiario, Usuario, Ahora);
        db.CuentasBancarias.Add(cuenta); db.MovimientosBancarios.Add(m);
        await db.SaveChangesAsync();
        return m;
    }

    private static SolicitarDevolucionDepositoHandler Handler(TesoreriaDbContext db) => new(db, new EmpresaContext(), new User(), new Clientes(), new Clock());

    private static SolicitarDevolucionDepositoCommand Cmd(Guid movimientoId, decimal importe, Guid? cliente = null) =>
        new(movimientoId, cliente ?? Cliente, importe, "Depósito equivocado", Destino.Banco, Destino.Clabe, Destino.Titular);

    private static DevolucionCliente Otra(MovimientoBancario m, decimal importe, bool rechazada = false)
    {
        var d = DevolucionCliente.SolicitarPorDepositoErroneo(Empresa, m, Cliente, importe, m.Monto, "otra", Destino, Guid.NewGuid(), Ahora);
        if (rechazada) d.Rechazar("no", Guid.NewGuid(), Ahora);
        return d;
    }

    [Fact]
    public async Task Tope_acumulado_con_otra_solicitada()
    {
        await using var db = Db(); var m = await Deposito(db);
        db.DevolucionesCliente.Add(Otra(m, 600m)); await db.SaveChangesAsync();
        var excede = () => Handler(db).Handle(Cmd(m.Id, 500m), default);
        await excede.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEV_IMPORTE_EXCEDE_DEPOSITO");
        var ok = await Handler(db).Handle(Cmd(m.Id, 400m), default);
        ok.Estado.Should().Be(EstadoDevolucionCliente.Solicitada);
    }

    [Fact]
    public async Task Una_rechazada_no_cuenta()
    {
        await using var db = Db(); var m = await Deposito(db);
        db.DevolucionesCliente.Add(Otra(m, 600m, rechazada: true)); await db.SaveChangesAsync();
        var ok = await Handler(db).Handle(Cmd(m.Id, 1_000m), default);
        ok.Importe.Should().Be(1_000m);
    }

    [Fact]
    public async Task Deposito_confirmado_se_rechaza()
    {
        await using var db = Db(); var m = await Deposito(db);
        var conf = DepositoConfirmacion.CrearDesdePropuesta(Empresa, Guid.NewGuid(), Cliente, "DEP", 1_000m, "MXN", "[]", Guid.NewGuid());
        conf.Confirmar(m, Guid.NewGuid(), Ahora);
        db.DepositosConfirmacion.Add(conf); await db.SaveChangesAsync();
        var act = () => Handler(db).Handle(Cmd(m.Id, 100m), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEV_DEPOSITO_CONFIRMADO");
    }

    [Fact]
    public async Task Cliente_distinto_al_del_deposito_se_rechaza()
    {
        await using var db = Db(); var m = await Deposito(db, beneficiario: Guid.NewGuid());
        var act = () => Handler(db).Handle(Cmd(m.Id, 100m), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEV_CLIENTE_DISTINTO_AL_DEPOSITO");
    }

    private sealed class EmpresaContext : ICurrentEmpresaContext
    { public Guid? Current => Empresa; public bool IsBypassed => false; public IDisposable Bypass() => new Noop(); private sealed class Noop : IDisposable { public void Dispose() { } } }
    private sealed class User : ICurrentUserContext { public Guid? UserId => Usuario; public string? UserName => "Prueba"; }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Ahora; }
    private sealed class Clientes : IClienteReadPort
    {
        public Task<IReadOnlyDictionary<Guid, ClienteRefDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ClienteRefDto>>(ids.ToDictionary(i => i, i => new ClienteRefDto(i, "C1", "Cliente SA")));
        public Task<IReadOnlyList<ClienteRefDto>> BuscarAsync(string texto, int limite, CancellationToken ct) => Task.FromResult<IReadOnlyList<ClienteRefDto>>([]);
    }
}
