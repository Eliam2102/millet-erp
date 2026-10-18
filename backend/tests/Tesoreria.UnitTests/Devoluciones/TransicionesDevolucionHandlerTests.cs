using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Devoluciones;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Devoluciones;

public sealed class TransicionesDevolucionHandlerTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Solicitante = Guid.NewGuid();
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly CuentaDestinoDevolucion Destino = new("BBVA", "032180000118359719", "Cliente SA");

    private static TesoreriaDbContext Db() => new(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaContext());

    private static async Task<(MovimientoBancario, DevolucionCliente)> Escenario(TesoreriaDbContext db, decimal importe = 400m)
    {
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, "MXN");
        var m = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 1_000m, new DateOnly(2026, 10, 17), null, null, null, null, Solicitante, Ahora);
        var d = DevolucionCliente.SolicitarPorDepositoErroneo(Empresa, m, Cliente, importe, 1_000m, "x", Destino, Solicitante, Ahora);
        db.CuentasBancarias.Add(cuenta); db.MovimientosBancarios.Add(m); db.DevolucionesCliente.Add(d);
        await db.SaveChangesAsync();
        return (m, d);
    }

    [Fact]
    public async Task Editar_sobre_el_tope_lanza()
    {
        await using var db = Db(); var (m, d) = await Escenario(db);
        db.DevolucionesCliente.Add(DevolucionCliente.SolicitarPorDepositoErroneo(Empresa, m, Cliente, 500m, 600m, "otra", Destino, Guid.NewGuid(), Ahora));
        await db.SaveChangesAsync();
        var h = new EditarDevolucionHandler(db, new EmpresaContext(), new User(Solicitante), new Permisos(false), new Clientes(), new Clock());
        var act = () => h.Handle(new(d.Id, Cliente, 600m, "x", null, null, null, d.Version), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEV_IMPORTE_EXCEDE_DEPOSITO");
    }

    [Fact]
    public async Task Autorizar_con_el_mismo_usuario_lanza()
    {
        await using var db = Db(); var (_, d) = await Escenario(db);
        var h = new AutorizarDevolucionHandler(db, new EmpresaContext(), new User(Solicitante), new Permisos(false), new Clientes(), new Clock());
        var act = () => h.Handle(new(d.Id, d.Version), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEV_MISMO_USUARIO");
    }

    [Fact]
    public async Task Autorizar_por_el_ultimo_editor_lanza()
    {
        await using var db = Db(); var (_, d) = await Escenario(db);
        var editor = Guid.NewGuid();
        d.Editar(Cliente, 100m, 600m, "x", Destino, editor, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        var h = new AutorizarDevolucionHandler(db, new EmpresaContext(), new User(editor), new Permisos(false), new Clientes(), new Clock());
        var act = () => h.Handle(new(d.Id, d.Version), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEV_MISMO_USUARIO");
    }

    [Fact]
    public async Task Autorizar_con_otro_usuario_deja_autorizada()
    {
        await using var db = Db(); var (_, d) = await Escenario(db);
        var otro = Guid.NewGuid();
        var h = new AutorizarDevolucionHandler(db, new EmpresaContext(), new User(otro), new Permisos(false), new Clientes(), new Clock());
        var r = await h.Handle(new(d.Id, d.Version), default);
        r.Estado.Should().Be(EstadoDevolucionCliente.Autorizada);
        r.AutorizadaPor.Should().Be(otro);
    }

    [Fact]
    public async Task Version_vieja_lanza_concurrencia()
    {
        await using var db = Db(); var (_, d) = await Escenario(db);
        var h = new AutorizarDevolucionHandler(db, new EmpresaContext(), new User(Guid.NewGuid()), new Permisos(false), new Clientes(), new Clock());
        var act = () => h.Handle(new(d.Id, d.Version + 1), default);
        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact]
    public async Task Cancelar_por_tercero_sin_permiso_lanza()
    {
        await using var db = Db(); var (_, d) = await Escenario(db);
        var h = new CancelarDevolucionHandler(db, new EmpresaContext(), new User(Guid.NewGuid()), new Permisos(false), new Clientes(), new Clock());
        var act = () => h.Handle(new(d.Id, "x", d.Version), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEV_NO_ES_SOLICITANTE");
    }

    private sealed class EmpresaContext : ICurrentEmpresaContext
    { public Guid? Current => Empresa; public bool IsBypassed => false; public IDisposable Bypass() => new Noop(); private sealed class Noop : IDisposable { public void Dispose() { } } }
    private sealed class User(Guid id) : ICurrentUserContext { public Guid? UserId => id; public string? UserName => "Prueba"; }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Ahora; }
    private sealed class Permisos(bool permitir) : ICurrentUserPermissions { public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) => ValueTask.FromResult(permitir); }
    private sealed class Clientes : IClienteReadPort
    {
        public Task<IReadOnlyDictionary<Guid, ClienteRefDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ClienteRefDto>>(ids.ToDictionary(i => i, i => new ClienteRefDto(i, "C1", "Cliente SA")));
        public Task<IReadOnlyList<ClienteRefDto>> BuscarAsync(string texto, int limite, CancellationToken ct) => Task.FromResult<IReadOnlyList<ClienteRefDto>>([]);
    }
}
