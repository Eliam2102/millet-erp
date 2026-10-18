using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Catalogos.Articulos;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.UnitTests.Articulos;

public sealed class ActualizarToleranciaArticuloTests
{
    private const string PermisoTolerancia = "datos_maestros.articulos.tolerancia-editar";

    [Fact]
    public async Task Almacen_ActualizaYLimpia_ConAuditoriaDeActorFechaAnteriorYNuevo()
    {
        var empresa = new FakeEmpresa();
        using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var articulo = new Articulo(Guid.NewGuid(), "DEMO", "Artículo DEMO", "PZA");
        articulo.ActualizarToleranciaRecepcion(5);
        db.Articulos.Add(articulo);
        await db.SaveChangesAsync();
        var permisos = new FakePermisos();
        permisos.Concedidos.Add(PermisoTolerancia);
        var usuario = new FakeUser();
        var clock = new FakeClock();
        var audit = new Auditoria();
        var handler = new ActualizarToleranciaArticuloHandler(db, permisos, usuario, empresa, clock, audit);
        await handler.Handle(new(articulo.Id, 1), default);
        Assert.Equal(1m, articulo.ToleranciaCantidadPorcentaje);
        Assert.Equal(usuario.UserId, audit.UsuarioId);
        Assert.Equal(articulo.Id, audit.EntidadId);
        Assert.Equal("articulo.tolerancia-recepcion", audit.Operacion);
        using var cambios = JsonDocument.Parse(audit.Cambios!);
        Assert.Equal(5m, cambios.RootElement.GetProperty("toleranciaCantidadPorcentaje").GetProperty("antes").GetDecimal());
        Assert.Equal(1m, cambios.RootElement.GetProperty("toleranciaCantidadPorcentaje").GetProperty("despues").GetDecimal());
        using var metadata = JsonDocument.Parse(audit.Metadatos!);
        Assert.Equal(clock.UtcNow, metadata.RootElement.GetProperty("ocurridoEn").GetDateTimeOffset());
        await handler.Handle(new(articulo.Id, 1), default);
        Assert.Equal(1, audit.Cantidad);
        await handler.Handle(new(articulo.Id, null), default);
        Assert.Null(articulo.ToleranciaCantidadPorcentaje);
        Assert.Equal(2, audit.Cantidad);
    }

    [Fact]
    public async Task SinPermiso_LanzaForbidden_SinAuditoria()
    {
        var empresa = new FakeEmpresa();
        using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var articulo = new Articulo(Guid.NewGuid(), "DEMO", "Artículo DEMO", "PZA");
        articulo.ActualizarToleranciaRecepcion(5);
        db.Articulos.Add(articulo);
        await db.SaveChangesAsync();
        var permisos = new FakePermisos();
        permisos.Concedidos.Add("datos_maestros.articulos.gestionar");
        var audit = new Auditoria();
        var handler = new ActualizarToleranciaArticuloHandler(db, permisos, new FakeUser(), empresa, new FakeClock(), audit);
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(articulo.Id, 1), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(articulo.Id, null), default));
        Assert.Equal(5m, articulo.ToleranciaCantidadPorcentaje);
        Assert.Equal(0, audit.Cantidad);
    }

    [Fact]
    public async Task ArticuloInexistente_LanzaNotFound()
    {
        var empresa = new FakeEmpresa();
        using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var permisos = new FakePermisos();
        permisos.Concedidos.Add(PermisoTolerancia);
        var handler = new ActualizarToleranciaArticuloHandler(db, permisos, new FakeUser(), empresa, new FakeClock(), new Auditoria());
        await Assert.ThrowsAsync<EntityNotFoundException>(() => handler.Handle(new(Guid.NewGuid(), 1), default));
    }

    private sealed class Auditoria : IAuditLogWriter
    {
        public Guid? UsuarioId { get; private set; }
        public Guid? EntidadId { get; private set; }
        public string? Operacion { get; private set; }
        public string? Cambios { get; private set; }
        public string? Metadatos { get; private set; }
        public int Cantidad { get; private set; }
        public Task RegistrarAsync(string operacion, string modulo, string entidad, Guid? entidadId, Guid? aggregateRootId,
            string actorNombre, string actorTipo, string? actorEmail, string entidadEtiqueta, string resumen,
            Guid? usuarioId = null, Guid? empresaId = null, string cambios = "{}", string? metadatos = null,
            CancellationToken cancellationToken = default)
        {
            UsuarioId = usuarioId; EntidadId = entidadId; Operacion = operacion; Cambios = cambios; Metadatos = metadatos; Cantidad++;
            return Task.CompletedTask;
        }
    }
}
