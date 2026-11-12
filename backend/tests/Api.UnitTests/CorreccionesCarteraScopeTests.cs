using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Api.Endpoints.CuentasPorCobrar;
using Millet.Api.Web;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.UnitTests;

/// <summary>M-237 · ADR-0051: corregir plazo / solicitar / autorizar / rechazar respetan la sucursal de la factura.</summary>
public sealed class CorreccionesCarteraScopeTests
{
    private static readonly Guid Cancun = Guid.NewGuid();
    private static readonly Guid Circuito = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, true)]   // usuario de la sucursal de la factura: pasa
    [InlineData(false, false)]  // usuario de otra sucursal: 403
    [InlineData(true, false)]   // corporativo: pasa aunque no tenga la sucursal
    public async Task Los_tres_flujos_verifican_la_sucursal_de_la_factura(bool corporativo, bool sucursalPropia)
    {
        await using var db = CrearDb();
        var (factura, movimiento, correccion) = Sembrar(db);
        await db.SaveChangesAsync();
        var puerto = new Puerto([new(factura.Id, [sucursalPropia ? Cancun : Circuito])]);
        var scope = Scope(corporativo, puerto);
        var permitido = corporativo || sucursalPropia;

        await Verificar(permitido, () => CorreccionesCarteraScope.VerificarFacturaAsync(scope, factura.Id, default));
        await Verificar(permitido, () => CorreccionesCarteraScope.VerificarMovimientoAsync(db, scope, movimiento.Id, default));
        await Verificar(permitido, () => CorreccionesCarteraScope.VerificarCorreccionAsync(db, scope, correccion.Id, default));
    }

    [Fact]
    public async Task Id_inexistente_solo_lo_atiende_el_corporativo()
    {
        await using var db = CrearDb();
        var scope = Scope(false, new Puerto([]));
        await Assert.ThrowsAsync<ForbiddenException>(() => CorreccionesCarteraScope.VerificarMovimientoAsync(db, scope, Guid.NewGuid(), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => CorreccionesCarteraScope.VerificarCorreccionAsync(db, scope, Guid.NewGuid(), default));
    }

    private static async Task Verificar(bool permitido, Func<Task> accion)
    {
        if (permitido) await accion();
        else await Assert.ThrowsAsync<ForbiddenException>(accion);
    }

    private static (FacturaCartera, MovimientoCartera, CorreccionCartera) Sembrar(CuentasPorCobrarDbContext db)
    {
        var empresa = Guid.NewGuid();
        var factura = FacturaCartera.Crear(empresa, Guid.NewGuid(), Guid.NewGuid(), "XAXX010101000", "Cliente DEMO",
            Guid.NewGuid().ToString(), "FV-DEMO", 1_000m, "MXN", "PPD", Ahora.AddDays(-5), Ahora.AddDays(25));
        db.FacturasCartera.Add(factura);
        var movimiento = new MovimientoCartera(empresa, factura.Id, TipoMovimientoCartera.Pago, Guid.NewGuid(), 100m, Ahora);
        db.MovimientosCartera.Add(movimiento);
        var correccion = CorreccionCartera.SolicitarReversa(empresa, movimiento, "Aplicado a factura equivocada", Guid.NewGuid(), "Solicita", Ahora);
        db.CorreccionesCartera.Add(correccion);
        return (factura, movimiento, correccion);
    }

    private static CuentasPorCobrarDbContext CrearDb() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase($"cxc_scope_{Guid.NewGuid()}").Options, new EmpresaCtx());

    private static DocumentoSucursalScope Scope(bool corporativo, Puerto puerto)
        => new(new Usuario(), new Permisos(corporativo), new Sucursales(), puerto, puerto, puerto, puerto, puerto, puerto, puerto, puerto);

    private sealed class Usuario : ICurrentUserContext
    { public Guid? UserId => Guid.Parse("00000237-0000-0000-0000-000000000001"); public string? UserName => "M237"; }
    private sealed class Permisos(bool corporativo) : ICurrentUserPermissions
    { public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) => ValueTask.FromResult(corporativo); }
    private sealed class Sucursales : IUsuarioSucursalReadPort
    {
        public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid usuarioId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([Cancun]);
        public Task<bool> EstaAsociadoAsync(Guid usuarioId, Guid sucursalId, CancellationToken cancellationToken) => Task.FromResult(sucursalId == Cancun);
    }
    private sealed class Puerto(IReadOnlyList<DocumentoSucursales> documentos) : IComprasSucursalReadPort, IFacturacionSucursalReadPort, ICxpSucursalReadPort, ICxcSucursalReadPort, ITesoreriaSucursalReadPort, IAlmacenSucursalReadPort, IComercioExteriorSucursalReadPort, IProductoTerminadoSucursalReadPort
    {
        public Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct) => Task.FromResult(documentos);
    }
    private sealed class EmpresaCtx : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
