using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.DatosMaestros.Application.Catalogos;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.UnitTests.Proveedores;

public sealed class ActualizarProveedorBancariosAuditoriaTests
{
    private const string PermisoBancariosEditar = "datos_maestros.proveedores.bancarios-editar";

    private sealed class CapturingAuditWriter : IAuditLogWriter
    {
        public sealed record AuditCall(
            string Operacion,
            string Modulo,
            string Entidad,
            Guid? EntidadId,
            Guid? AggregateRootId,
            string ActorNombre,
            string ActorTipo,
            string? ActorEmail,
            string EntidadEtiqueta,
            string Resumen,
            Guid? UsuarioId,
            Guid? EmpresaId,
            string Cambios,
            string? Metadatos);

        public List<AuditCall> Calls { get; } = [];

        public Task RegistrarAsync(
            string operacion, string modulo, string entidad, Guid? entidadId, Guid? aggregateRootId,
            string actorNombre, string actorTipo, string? actorEmail, string entidadEtiqueta, string resumen,
            Guid? usuarioId = null, Guid? empresaId = null, string cambios = "{}", string? metadatos = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new AuditCall(
                operacion, modulo, entidad, entidadId, aggregateRootId,
                actorNombre, actorTipo, actorEmail, entidadEtiqueta, resumen,
                usuarioId, empresaId, cambios, metadatos));
            return Task.CompletedTask;
        }
    }

    private static CompartidoDbContext CrearDb(ICurrentEmpresaContext? empresa = null)
    {
        var options = new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CompartidoDbContext(options, empresa ?? new FakeEmpresa());
    }

    [Fact]
    public async Task CambioDeClabe_AuditaConMascara_Sin18DigitosEnCambiosNiResumen()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        var p = new Proveedor(
            Guid.NewGuid(), "P01", "Prov 1", "PRO010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        p.ActualizarDatos(
            banco: "BBVA",
            clabe: "012345678901234567",
            beneficiario: "Titular 1");
        db.Proveedores.Add(p);
        await db.SaveChangesAsync();

        var permisos = new FakePermisos();
        permisos.Concedidos.Add(PermisoBancariosEditar);
        var user = new FakeUser { UserId = Guid.NewGuid(), UserName = "Tesorero 1", Email = "tes@test.local" };
        var audit = new CapturingAuditWriter();

        var handler = new ActualizarProveedorHandler(db, permisos, user, empresa, audit);

        var nuevaClabe = "987654321098765432";
        await handler.Handle(new ActualizarProveedorCommand(
            ProveedorId: p.Id,
            Clabe: nuevaClabe), CancellationToken.None);

        Assert.Single(audit.Calls);
        var call = audit.Calls[0];
        Assert.Equal("proveedor.bancarios-cambiados", call.Operacion);
        Assert.Equal("DatosMaestros", call.Modulo);
        Assert.Equal("Proveedor", call.Entidad);
        Assert.Equal(p.Id, call.EntidadId);
        Assert.Equal(p.Id, call.AggregateRootId);
        Assert.Equal("Tesorero 1", call.ActorNombre);
        Assert.Equal("tes@test.local", call.ActorEmail);

        // Sin 18 dígitos seguidos ni en cambios ni en resumen
        Assert.DoesNotMatch(@"\d{18}", call.Cambios);
        Assert.DoesNotMatch(@"\d{18}", call.Resumen);

        // Contiene máscaras esperadas (últimos 4 dígitos)
        Assert.Contains("4567", call.Cambios);
        Assert.Contains("5432", call.Cambios);
        Assert.Contains("4567", call.Resumen);
        Assert.Contains("5432", call.Resumen);
    }

    [Fact]
    public async Task CambioSoloDeBancoOBeneficiario_AuditaConClabeIgual()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        var p = new Proveedor(
            Guid.NewGuid(), "P01", "Prov 1", "PRO010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        p.ActualizarDatos(
            banco: "BBVA",
            clabe: "012345678901234567",
            beneficiario: "Titular 1");
        db.Proveedores.Add(p);
        await db.SaveChangesAsync();

        var permisos = new FakePermisos();
        permisos.Concedidos.Add(PermisoBancariosEditar);
        var user = new FakeUser();
        var audit = new CapturingAuditWriter();

        var handler = new ActualizarProveedorHandler(db, permisos, user, empresa, audit);

        await handler.Handle(new ActualizarProveedorCommand(
            ProveedorId: p.Id,
            Banco: "Santander"), CancellationToken.None);

        Assert.Single(audit.Calls);
        var call = audit.Calls[0];
        Assert.Equal("proveedor.bancarios-cambiados", call.Operacion);
        Assert.Contains("Santander", call.Cambios);
        Assert.Contains("BBVA", call.Cambios);
        // Clabe no cambió, antes y despues son iguales
        Assert.Contains("\"antes\":\"**************4567\",\"despues\":\"**************4567\"", call.Cambios);
    }

    [Fact]
    public async Task RequestConMismosValores_NoAudita()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        var p = new Proveedor(
            Guid.NewGuid(), "P01", "Prov 1", "PRO010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        p.ActualizarDatos(
            banco: "BBVA",
            clabe: "012345678901234567",
            beneficiario: "Titular 1");
        db.Proveedores.Add(p);
        await db.SaveChangesAsync();

        var permisos = new FakePermisos();
        permisos.Concedidos.Add(PermisoBancariosEditar);
        var user = new FakeUser();
        var audit = new CapturingAuditWriter();

        var handler = new ActualizarProveedorHandler(db, permisos, user, empresa, audit);

        await handler.Handle(new ActualizarProveedorCommand(
            ProveedorId: p.Id,
            Banco: "BBVA",
            Clabe: "012345678901234567",
            Beneficiario: "Titular 1"), CancellationToken.None);

        Assert.Empty(audit.Calls);
    }

    [Fact]
    public async Task RequestSinBancarios_NoAudita()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        var p = new Proveedor(
            Guid.NewGuid(), "P01", "Prov 1", "PRO010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        db.Proveedores.Add(p);
        await db.SaveChangesAsync();

        var permisos = new FakePermisos();
        // Sin permiso bancarios-editar, pero solo se cambia email
        var user = new FakeUser();
        var audit = new CapturingAuditWriter();

        var handler = new ActualizarProveedorHandler(db, permisos, user, empresa, audit);

        await handler.Handle(new ActualizarProveedorCommand(
            ProveedorId: p.Id,
            Email: "nuevo@proveedor.com"), CancellationToken.None);

        Assert.Empty(audit.Calls);
        Assert.Equal("nuevo@proveedor.com", p.Email);
    }

    [Fact]
    public async Task SinPermisoBancariosEditar_TocaBancarios_LanzaForbidden_SinAuditoria()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        var p = new Proveedor(
            Guid.NewGuid(), "P01", "Prov 1", "PRO010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        db.Proveedores.Add(p);
        await db.SaveChangesAsync();

        var permisos = new FakePermisos(); // Concedidos está vacío
        var user = new FakeUser();
        var audit = new CapturingAuditWriter();

        var handler = new ActualizarProveedorHandler(db, permisos, user, empresa, audit);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new ActualizarProveedorCommand(
                ProveedorId: p.Id,
                Banco: "Nuevo Banco"), CancellationToken.None));

        Assert.Equal("PROVEEDOR_BANCARIOS_SIN_PERMISO", ex.Code);
        Assert.Empty(audit.Calls);
    }
}
