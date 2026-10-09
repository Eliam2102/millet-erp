using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Compras.Application.Adjuntos;
using Millet.Compras.Infrastructure;
using Millet.CuentasPorPagar.Application.Adjuntos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.UnitTests;

public sealed class AdjuntosP6Tests
{
    [Theory]
    [InlineData("requisicion", AdjuntoOperacion.Ver)]
    [InlineData("requisicion", AdjuntoOperacion.Subir)]
    [InlineData("requisicion", AdjuntoOperacion.Baja)]
    [InlineData("factura_proveedor", AdjuntoOperacion.Ver)]
    [InlineData("factura_proveedor", AdjuntoOperacion.Subir)]
    [InlineData("factura_proveedor", AdjuntoOperacion.Baja)]
    public async Task Cada_propietario_rechaza_sucursal_ajena_y_acepta_propia_y_corporativa(string tipo, AdjuntoOperacion operacion)
    {
        var propia = Guid.NewGuid(); var ajena = Guid.NewGuid();
        using var compras = new ComprasDbContext(new DbContextOptionsBuilder<ComprasDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
        using var cxp = CxpP6TestContext.Crear(new Empresa());
        var permisos = new Permisos();
        IAdjuntoPropietario propietario = tipo == "requisicion"
            ? new RequisicionAdjuntoPropietario(compras, new Usuario(), permisos, new Sucursales(propia))
            : new FacturaProveedorAdjuntoPropietario(cxp, new Usuario(), permisos, new Sucursales(propia));
        var info = new AdjuntoPropietarioInfo(Guid.NewGuid(), Guid.NewGuid(), ajena, true, null, "Documento P6");
        await Assert.ThrowsAsync<ForbiddenException>(() => propietario.VerificarAlcanceAsync(info, operacion, CancellationToken.None));
        await propietario.VerificarAlcanceAsync(info with { SucursalId = propia }, operacion, CancellationToken.None);
        permisos.Corporativo = true;
        await propietario.VerificarAlcanceAsync(info, operacion, CancellationToken.None);
        Assert.EndsWith("adjuntos-ver", propietario.PermisoVer);
        Assert.EndsWith("adjuntos-subir", propietario.PermisoSubir);
        Assert.EndsWith("adjuntos-baja", propietario.PermisoBaja);
        Assert.Contains(operacion == AdjuntoOperacion.Ver ? "leer-todas" : "gestionar-todas", permisos.Ultimo);
    }
    private sealed class Empresa : ICurrentEmpresaContext
    { public Guid? Current => null; public bool IsBypassed => true; public IDisposable Bypass() => throw new NotSupportedException(); }
    private sealed class Usuario : ICurrentUserContext
    { public Guid? UserId => Guid.Parse("00000006-0000-0000-0000-000000000001"); public string? UserName => "P6"; }
    private sealed class Permisos : ICurrentUserPermissions
    {
        public bool Corporativo { get; set; }
        public string Ultimo { get; private set; } = string.Empty;
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) { Ultimo = permiso; return ValueTask.FromResult(Corporativo); }
    }
    private sealed class Sucursales(Guid propia) : IUsuarioSucursalReadPort
    {
        public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid usuarioId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([propia]);
        public Task<bool> EstaAsociadoAsync(Guid usuarioId, Guid sucursalId, CancellationToken cancellationToken) => Task.FromResult(sucursalId == propia);
    }
}
