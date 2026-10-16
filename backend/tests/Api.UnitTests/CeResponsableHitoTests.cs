using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.ComercioExterior.Infrastructure.PublicAdapters;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.Identidad.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.UnitTests;

public sealed class CeResponsableHitoTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Valida_usuario_activo_y_acceso_a_sucursal_sin_consultar_moneda_o_incoterm(bool activo, bool asociado)
    {
        var empresa = new Empresa();
        using var identidad = new IdentidadDbContext(new DbContextOptionsBuilder<IdentidadDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        using var catalogos = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var usuario = new Usuario(Guid.NewGuid(), Guid.NewGuid().ToString(), "ce@example.invalid", "Responsable CE");
        if (!activo) usuario.Desactivar();
        var rol = new Rol(Guid.NewGuid(), "CE", "CE prueba");
        var sucursal = new Sucursal(Guid.NewGuid(), empresa.Current!.Value, "CE", "Sucursal CE", TipoSucursal.Planta,
            "Calle prueba", "1", "Centro", "Mérida", "Mérida", "Yucatán", "97000", "México");
        identidad.AddRange(usuario, rol, sucursal, new UsuarioEmpresaRol(Guid.NewGuid(), usuario.Id, empresa.Current.Value, rol.Id));
        if (asociado) identidad.UsuarioSucursales.Add(new UsuarioSucursal(Guid.NewGuid(), usuario.Id, sucursal.Id, empresa.Current.Value));
        await identidad.SaveChangesAsync();
        var adapter = new CatalogosCeReadAdapter(catalogos, identidad, new Reloj(), new UsuarioSucursalReadAdapter(identidad));
        if (activo && asociado)
            await adapter.ValidarResponsableAsync(sucursal.Id, usuario.Id, CancellationToken.None);
        else
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => adapter.ValidarResponsableAsync(sucursal.Id, usuario.Id, CancellationToken.None));
            Assert.Equal("CE_RESPONSABLE_REQUERIDO", error.Code);
        }
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = Guid.NewGuid();
        public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
}
