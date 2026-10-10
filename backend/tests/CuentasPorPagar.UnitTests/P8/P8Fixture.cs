using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Ports.Contabilidad;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.SharedKernel.Application;
using Millet.Administracion.Application.Abstractions;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.CuentasPorPagar.UnitTests.P8;

internal sealed class P8Fixture : ICurrentEmpresaContext, IClock, IPeriodoContablePort, IProveedorReadPort,
    ICurrentUserContext, ICurrentUserPermissions, IUsuarioSucursalReadPort, Millet.CuentasPorPagar.Domain.Ports.Compras.IComprasOcReadPort, IAsyncDisposable
{
    public Guid? Current { get; } = Guid.NewGuid();
    public bool IsBypassed => false;
    public IDisposable Bypass() => throw new NotSupportedException();
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    public HashSet<DateOnly> Cerradas { get; } = [];
    public Task<bool> AdmiteMovimientosAsync(DateOnly f, CancellationToken ct) => Task.FromResult(!Cerradas.Contains(f));
    public Guid? UserId { get; } = Guid.NewGuid(); public string? UserName => "FIX-P8";
    public bool Corporativo { get; set; } = true;
    public Guid Sucursal { get; } = Guid.NewGuid(); public Guid Proveedor { get; } = Guid.NewGuid();
    public ValueTask<bool> TieneAsync(string p, CancellationToken ct = default) => ValueTask.FromResult(Corporativo);
    public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid u, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([Sucursal]);
    public Task<bool> EstaAsociadoAsync(Guid u, Guid s, CancellationToken ct) => Task.FromResult(s == Sucursal);
    public Task<ProveedorDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ProveedorDto?>(new(id, "FIX010101ABC", "Proveedor ficticio P8", null, false, true));
    public Task<ProveedorDto?> ObtenerPorRfcAsync(string r, CancellationToken ct) => ObtenerAsync(Proveedor, ct);
    public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(id => id, _ => "Proveedor ficticio P8"));
    Task<Millet.CuentasPorPagar.Domain.Ports.Compras.OrdenCompraDto?> Millet.CuentasPorPagar.Domain.Ports.Compras.IComprasOcReadPort.ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<Millet.CuentasPorPagar.Domain.Ports.Compras.OrdenCompraDto?>(null);
    public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    public Task<IReadOnlyList<Millet.CuentasPorPagar.Domain.Ports.Compras.OrdenCompraDto>> ListarAutorizadasPorProveedorAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<Millet.CuentasPorPagar.Domain.Ports.Compras.OrdenCompraDto>>([]);
    public CuentasPorPagarDbContext Db { get; }
    public SaldosHistoricos Lector => new(Db, this, this, this, this);
    public P8Fixture() { Db = new(new DbContextOptionsBuilder<CuentasPorPagarDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, this, this, this); Db.Database.EnsureCreated(); }
    public Factura Nueva(string moneda = "MXN", decimal total = 1000, int dia = 1, string? obra = null) {
        var fecha = new DateTimeOffset(2026, 9, dia, 0, 0, 0, TimeSpan.Zero);
        var f = Factura.CapturarSinOc(Current!.Value, null, null, Proveedor, Sucursal, "FIX-P8", null, fecha, fecha,
            new(2026, 10, 15), moneda, null, total, 0, 0, 0, total, "FIX prueba P8", fecha);
        f.AsignarDatosP8(obra, null); return f;
    }
    public async ValueTask DisposeAsync() => await Db.DisposeAsync();
}
