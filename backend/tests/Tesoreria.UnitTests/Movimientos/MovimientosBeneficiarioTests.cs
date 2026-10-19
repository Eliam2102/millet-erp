using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Application.Movimientos;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Movimientos;

/// <summary>C-03: el libro y el detalle resuelven nombre y clave del beneficiario en lote (sin N+1).</summary>
public sealed class MovimientosBeneficiarioTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static TesoreriaDbContext Db() => new(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaContext());

    [Fact]
    public async Task Libro_resuelve_beneficiarios_en_una_sola_llamada_por_puerto()
    {
        await using var db = Db();
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, "MXN");
        var c1 = Guid.NewGuid(); var c2 = Guid.NewGuid();
        db.CuentasBancarias.Add(cuenta);
        foreach (var c in new[] { c1, c2, c1 })
            db.MovimientosBancarios.Add(MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 100m, new DateOnly(2026, 10, 18), null, null, BeneficiarioTipo.Cliente, c, Usuario, Ahora));
        db.MovimientosBancarios.Add(MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 50m, new DateOnly(2026, 10, 18), null, null, null, null, Usuario, Ahora));
        await db.SaveChangesAsync();
        var clientes = new Clientes(); var proveedores = new Proveedores();

        var r = await new MovimientosBancariosHandler(db, new Permisos(), clientes, proveedores).Handle(new MovimientosBancariosQuery(), default);

        clientes.Llamadas.Should().Be(1);
        r.Items.Where(x => x.BeneficiarioRef is not null).Should().OnlyContain(x => x.BeneficiarioNombre == "Razón " + x.BeneficiarioRef && x.BeneficiarioClave == "CL");
        r.Items.Single(x => x.BeneficiarioRef is null).BeneficiarioNombre.Should().BeNull();
    }

    [Fact]
    public async Task Detalle_incluye_nombre_del_beneficiario()
    {
        await using var db = Db();
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, "MXN");
        var cliente = Guid.NewGuid();
        var m = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 100m, new DateOnly(2026, 10, 18), null, null, BeneficiarioTipo.Cliente, cliente, Usuario, Ahora);
        db.CuentasBancarias.Add(cuenta); db.MovimientosBancarios.Add(m); await db.SaveChangesAsync();

        var r = await new MovimientoDetalleHandler(db, new Permisos(), new Clientes(), new Proveedores()).Handle(new MovimientoDetalleQuery(m.Id), default);

        r.Movimiento.BeneficiarioNombre.Should().Be("Razón " + cliente);
    }

    private sealed class EmpresaContext : ICurrentEmpresaContext
    { public Guid? Current => Empresa; public bool IsBypassed => false; public IDisposable Bypass() => new Noop(); private sealed class Noop : IDisposable { public void Dispose() { } } }
    private sealed class Permisos : ICurrentUserPermissions { public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) => ValueTask.FromResult(false); }
    private sealed class Clientes : IClienteReadPort
    {
        public int Llamadas { get; private set; }
        public Task<IReadOnlyDictionary<Guid, ClienteRefDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        {
            Llamadas++;
            return Task.FromResult<IReadOnlyDictionary<Guid, ClienteRefDto>>(ids.ToDictionary(i => i, i => new ClienteRefDto(i, "CL", "Razón " + i)));
        }
        public Task<IReadOnlyList<ClienteRefDto>> BuscarAsync(string texto, int limite, CancellationToken ct) => Task.FromResult<IReadOnlyList<ClienteRefDto>>([]);
    }
    private sealed class Proveedores : IProveedorBancoReadPort
    {
        public Task<ProveedorBancoDto?> ObtenerAsync(Guid proveedorId, CancellationToken ct) => Task.FromResult<ProveedorBancoDto?>(null);
        public Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorBancoDto>>(new Dictionary<Guid, ProveedorBancoDto>());
    }
}
