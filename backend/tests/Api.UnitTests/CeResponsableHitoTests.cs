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
    [InlineData("gestion", true)]
    [InlineData("lectura", false)]
    [InlineData("denegado", false)]
    [InlineData("otro-usuario", false)]
    [InlineData("otra-empresa", false)]
    [InlineData("inactivo", false)]
    [InlineData("rol-inactivo", false)]
    [InlineData("corporativo", true)]
    public async Task Responsable_reutiliza_su_acceso_efectivo_sin_conceder_permisos(string perfil, bool permitido)
    {
        var empresa = new Empresa();
        using var identidad = new IdentidadDbContext(new DbContextOptionsBuilder<IdentidadDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        using var catalogos = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        using var ce = new Millet.ComercioExterior.Infrastructure.Persistence.ComercioExteriorDbContext(new DbContextOptionsBuilder<Millet.ComercioExterior.Infrastructure.Persistence.ComercioExteriorDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var usuario = new Usuario(Guid.NewGuid(), Guid.NewGuid().ToString(), "responsable@example.invalid", "Responsable");
        var otro = new Usuario(Guid.NewGuid(), Guid.NewGuid().ToString(), "actor@example.invalid", "Actor");
        var rol = new Rol(Guid.NewGuid(), "CE-global", "Rol de prueba");
        var vacio = new Rol(Guid.NewGuid(), "CE-operativo", "Rol sin bypass");
        var sucursal = new Sucursal(Guid.NewGuid(), empresa.Current!.Value, "CE", "Sucursal CE", TipoSucursal.Planta,
            "Calle prueba", "1", "Centro", "Mérida", "Mérida", "Yucatán", "97000", "México");
        var codigo = perfil == "lectura" ? PermisosCanonicos.ComercioExteriorDocumentosLeerTodasSucursales : PermisosCanonicos.ComercioExteriorDocumentosGestionarTodasSucursales;
        var permiso = new Permiso(Guid.NewGuid(), codigo, "Alcance de prueba");
        identidad.AddRange(usuario, otro, rol, vacio, sucursal, permiso, new RolPermiso(Guid.NewGuid(), rol.Id, permiso.Id));
        identidad.Add(new UsuarioEmpresaRol(Guid.NewGuid(), usuario.Id, perfil == "otra-empresa" ? Guid.NewGuid() : empresa.Current.Value,
            perfil == "otro-usuario" ? vacio.Id : rol.Id));
        identidad.Add(new UsuarioEmpresaRol(Guid.NewGuid(), otro.Id, empresa.Current.Value, rol.Id));
        if (perfil == "inactivo") usuario.Desactivar();
        if (perfil == "rol-inactivo") rol.Desactivar();
        if (perfil == "corporativo") identidad.UsuarioEmpresaAlcances.Add(new UsuarioEmpresaAlcance(Guid.NewGuid(), usuario.Id, empresa.Current.Value, true));
        if (perfil == "denegado") identidad.UsuarioPermisoOverrides.Add(new UsuarioPermisoOverride(Guid.NewGuid(), usuario.Id, empresa.Current.Value, permiso.Id, EfectoPermiso.Denegar, "Prueba"));
        catalogos.Sucursales.Add(sucursal);
        await identidad.SaveChangesAsync(); await catalogos.SaveChangesAsync();
        var adapter = new CatalogosCeReadAdapter(catalogos, identidad, new Reloj(), new UsuarioSucursalReadAdapter(identidad), ce, new PermissionLoader(identidad, empresa));
        if (permitido) await adapter.ValidarResponsableAsync(sucursal.Id, usuario.Id, CancellationToken.None);
        else
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => adapter.ValidarResponsableAsync(sucursal.Id, usuario.Id, CancellationToken.None));
            Assert.Equal("CE_RESPONSABLE_REQUERIDO", error.Code);
        }
    }

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
        catalogos.Sucursales.Add(sucursal);
        await identidad.SaveChangesAsync(); await catalogos.SaveChangesAsync();
        using var ce = new Millet.ComercioExterior.Infrastructure.Persistence.ComercioExteriorDbContext(new DbContextOptionsBuilder<Millet.ComercioExterior.Infrastructure.Persistence.ComercioExteriorDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var adapter = new CatalogosCeReadAdapter(catalogos, identidad, new Reloj(), new UsuarioSucursalReadAdapter(identidad), ce, new PermissionLoader(identidad, empresa));
        if (activo && asociado)
            await adapter.ValidarResponsableAsync(sucursal.Id, usuario.Id, CancellationToken.None);
        else
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => adapter.ValidarResponsableAsync(sucursal.Id, usuario.Id, CancellationToken.None));
            Assert.Equal("CE_RESPONSABLE_REQUERIDO", error.Code);
        }
    }

    [Fact]
    public async Task Recepcion_propone_transporte_y_pedimento_del_expediente_sin_cambiar_la_oc()
    {
        var empresa = new Empresa();
        using var db = new Millet.ComercioExterior.Infrastructure.Persistence.ComercioExteriorDbContext(new DbContextOptionsBuilder<Millet.ComercioExterior.Infrastructure.Persistence.ComercioExteriorDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var actor = Guid.NewGuid(); var oc = Guid.NewGuid();
        var e = new Millet.ComercioExterior.Domain.Expediente(Guid.NewGuid(), empresa.Current!.Value, "IMP-P18", Millet.ComercioExterior.Domain.TipoExpediente.Importacion, Guid.NewGuid(), "Propuesta para recepción", oc, Guid.NewGuid(), "USD", actor, []);
        e.GuardarContenedor(Guid.NewGuid(), new("CONT-P18", null, null, "BL-P18", null, null, null, null));
        e.RegistrarDocumento(Guid.NewGuid(), new(Guid.NewGuid(), "IMP_PEDIMENTO", "Pedimento", true, false), Guid.NewGuid(), null, actor, "PED-P18");
        db.Expedientes.Add(e); await db.SaveChangesAsync();
        var propuesta = await new TransporteImportacionCeReadAdapter(db).ObtenerAsync(oc, CancellationToken.None);
        Assert.Equal("PED-P18", propuesta!.NumeroPedimento);
        Assert.Equal("CONT-P18", Assert.Single(propuesta.Contenedores).Numero);
        Assert.Equal("BL-P18", propuesta.Contenedores[0].BlGuia);
        Assert.Equal(oc, e.OcId);
        var segundo = new Millet.ComercioExterior.Domain.Expediente(Guid.NewGuid(), empresa.Current.Value, "IMP-P22", Millet.ComercioExterior.Domain.TipoExpediente.Importacion, e.SucursalId, "Segundo envío de la OC", oc, e.IncotermId, "USD", actor, []);
        segundo.GuardarContenedor(Guid.NewGuid(), new("CONT-P22", null, null, "BL-P22", null, null, null, null));
        db.Expedientes.Add(segundo); await db.SaveChangesAsync();
        propuesta = await new TransporteImportacionCeReadAdapter(db).ObtenerAsync(oc, CancellationToken.None);
        Assert.Equal("PED-P18", propuesta!.NumeroPedimento);
        Assert.Collection(propuesta.Contenedores, c => Assert.Equal("CONT-P18", c.Numero), c => Assert.Equal("CONT-P22", c.Numero));
    }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = Guid.NewGuid();
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Alcance();
        private sealed class Alcance : IDisposable { public void Dispose() { } }
    }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
}
