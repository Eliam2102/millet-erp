using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Catalogos.Proveedores;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.UnitTests.Proveedores;

public sealed class ActualizarToleranciaProveedorTests
{
    [Fact]
    public async Task Cxp_ActualizaYLimpia_ConAuditoriaDeActorFechaAnteriorYNuevo()
    {
        var empresa = new FakeEmpresa();
        using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var proveedor = new Proveedor(Guid.NewGuid(), "DEMO", "Proveedor DEMO", "DEMO010101AA1", TipoPersonaProveedor.Moral);
        proveedor.ActualizarToleranciaFacturaContraOc(5);
        db.Proveedores.Add(proveedor);
        await db.SaveChangesAsync();
        var permisos = new FakePermisos();
        permisos.Concedidos.Add("datos_maestros.proveedores.tolerancia-editar");
        var usuario = new FakeUser();
        var clock = new FakeClock();
        var audit = new Auditoria();
        var handler = new ActualizarToleranciaProveedorHandler(db, permisos, usuario, empresa, clock, audit);
        await handler.Handle(new(proveedor.Id, 1), default);
        Assert.Equal(1m, proveedor.ToleranciaFacturaContraOcMxn);
        Assert.Equal(usuario.UserId, audit.UsuarioId);
        Assert.Equal(proveedor.Id, audit.EntidadId);
        Assert.Equal("proveedor.tolerancia-cambiada", audit.Operacion);
        using var cambios = JsonDocument.Parse(audit.Cambios!);
        Assert.Equal(5m, cambios.RootElement.GetProperty("toleranciaFacturaContraOcMxn").GetProperty("antes").GetDecimal());
        Assert.Equal(1m, cambios.RootElement.GetProperty("toleranciaFacturaContraOcMxn").GetProperty("despues").GetDecimal());
        using var metadata = JsonDocument.Parse(audit.Metadatos!);
        Assert.Equal(clock.UtcNow, metadata.RootElement.GetProperty("ocurridoEn").GetDateTimeOffset());
        await handler.Handle(new(proveedor.Id, 1), default);
        Assert.Equal(1, audit.Cantidad);
        await handler.Handle(new(proveedor.Id, null), default);
        Assert.Null(proveedor.ToleranciaFacturaContraOcMxn);
        Assert.Equal(2, audit.Cantidad);
    }

    [Fact]
    public async Task Compras_NoPuedeCambiarNiLimpiar_SinAuditoria()
    {
        var empresa = new FakeEmpresa();
        using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var proveedor = new Proveedor(Guid.NewGuid(), "DEMO", "Proveedor DEMO", "DEMO010101AA1", TipoPersonaProveedor.Moral);
        proveedor.ActualizarToleranciaFacturaContraOc(5);
        db.Proveedores.Add(proveedor);
        await db.SaveChangesAsync();
        var permisos = new FakePermisos();
        permisos.Concedidos.Add("datos_maestros.proveedores.gestionar");
        var audit = new Auditoria();
        var handler = new ActualizarToleranciaProveedorHandler(db, permisos, new FakeUser(), empresa, new FakeClock(), audit);
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(proveedor.Id, 1), default));
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new(proveedor.Id, null), default));
        Assert.Equal(5m, proveedor.ToleranciaFacturaContraOcMxn);
        Assert.Equal(0, audit.Cantidad);
    }

    [Fact]
    public void Validator_NoPermiteNegativosNiPerdidaDePrecision()
    {
        var validator = new ActualizarToleranciaProveedorValidator();
        Assert.False(validator.Validate(new ActualizarToleranciaProveedorCommand(Guid.NewGuid(), -1)).IsValid);
        Assert.False(validator.Validate(new ActualizarToleranciaProveedorCommand(Guid.NewGuid(), 0.00001m)).IsValid);
        Assert.False(validator.Validate(new ActualizarToleranciaProveedorCommand(Guid.Empty, 1)).IsValid);
        Assert.True(validator.Validate(new ActualizarToleranciaProveedorCommand(Guid.NewGuid(), null)).IsValid);
        Assert.True(validator.Validate(new ActualizarToleranciaProveedorCommand(Guid.NewGuid(), 0)).IsValid);
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
