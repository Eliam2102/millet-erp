using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Catalogos;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Catalogos;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.UnitTests.DatosMaestros;

public sealed class ReactivarMaestrosTests
{
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Vacio();
        private sealed class Vacio : IDisposable { public void Dispose() { } }
    }
    private sealed class Usuario : ICurrentUserContext { public Guid? UserId { get; } = Guid.NewGuid(); public string? UserName => "Prueba"; }
    private sealed class Auditoria : IAuditLogWriter
    {
        public Task RegistrarAsync(string operacion, string modulo, string entidad, Guid? entidadId, Guid? aggregateRootId, string actorNombre, string actorTipo, string? actorEmail, string entidadEtiqueta, string resumen, Guid? usuarioId = null, Guid? empresaId = null, string cambios = "{}", string? metadatos = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private static MaestroEstatusService Servicio() => new([], new Auditoria(), new Usuario(), new Empresa());

    [Fact]
    public async Task Reactivar_articulo_dos_veces_conserva_identidad_y_estado()
    {
        await using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
        var articulo = new Articulo(Guid.NewGuid(), "FICTICIO", "Artículo ficticio", "PZA", estatus: EstatusCatalogo.Inactivo); db.Add(articulo); await db.SaveChangesAsync();
        var handler = new ReactivarArticuloHandler(db, Servicio()); await handler.Handle(new(articulo.Id, "Vuelve a surtirse"), default); await handler.Handle(new(articulo.Id, "Vuelve a surtirse"), default);
        Assert.Equal(EstatusCatalogo.Activo, articulo.Estatus); Assert.Single(db.Articulos); Assert.False(db.ChangeTracker.HasChanges());
    }
    [Fact]
    public async Task Reactivar_proveedor_dos_veces_conserva_estado_y_no_valida_uno_en_revision()
    {
        await using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
        var proveedor = new Proveedor(Guid.NewGuid(), "FICTICIO", "Proveedor ficticio", "XEXX010101000", TipoPersonaProveedor.Moral, EstatusCatalogo.Inactivo);
        var revision = new Proveedor(Guid.NewGuid(), "REVISION", "Por revisar ficticio", "XEXX010101000", TipoPersonaProveedor.Moral); db.AddRange(proveedor, revision); await db.SaveChangesAsync();
        var handler = new ReactivarProveedorHandler(db, Servicio()); await handler.Handle(new(proveedor.Id, "Documentos al corriente"), default); await handler.Handle(new(proveedor.Id, "Documentos al corriente"), default);
        Assert.Equal(EstatusCatalogo.Activo, proveedor.Estatus); Assert.False(db.ChangeTracker.HasChanges());
        await Assert.ThrowsAsync<BusinessRuleException>(() => handler.Handle(new(revision.Id, "Documentos al corriente"), default)); Assert.Equal(EstatusCatalogo.EnRevision, revision.Estatus);
    }
}
