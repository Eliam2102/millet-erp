using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Auditoria;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Catalogos.Proveedores;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Compartido.Application.Ports;

namespace Millet.Compartido.UnitTests.Proveedores;

public sealed class ValidacionProveedoresHandlersTests
{
    private sealed class FakeExpedientePort : IExpedienteProveedorReadPort
    {
        public bool Completo { get; set; } = true;
        public IReadOnlyList<string> Faltantes { get; set; } = [];
        public IReadOnlyList<string> Vencidos { get; set; } = [];

        public Task<ExpedienteProveedorResumen?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ExpedienteProveedorResumen?>(new ExpedienteProveedorResumen(
                proveedorId,
                Completo,
                Faltantes,
                Vencidos,
                []));
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
    public async Task Proveedor_activo_en_revision_queda_bloqueado_para_compras_y_registra_motivo()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        var proveedor = new Proveedor(Guid.NewGuid(), "P01", "Prov 1", "PRO010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        db.Proveedores.Add(proveedor);
        await db.SaveChangesAsync();
        var audit = new FakeAudit();
        var handler = new PonerProveedorEnRevisionHandler(db, new FakeUser(), empresa, new FakeClock(), audit);
        var res = await handler.Handle(new(proveedor.Id, "Revisar expediente bancario"), default);
        Assert.Equal(EstatusCatalogo.EnRevision, res.Estatus);
        Assert.Equal("Revisar expediente bancario", res.MotivoRevision);
        Assert.Equal(EstatusCatalogo.EnRevision, proveedor.Estatus);
        var registro = Assert.Single(audit.Registros);
        Assert.Equal("proveedor.en-revision", registro.Operacion);
        Assert.Contains("Revisar expediente bancario", registro.Resumen);
        Assert.False(new PonerProveedorEnRevisionValidator().Validate(new PonerProveedorEnRevisionCommand(proveedor.Id, "")).IsValid);
    }

    [Fact]
    public async Task ValidarProveedor_NotFound_ThrowsEntityNotFoundException()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        var expediente = new FakeExpedientePort();
        var user = new FakeUser();
        var audit = new FakeAudit();
        var clock = new FakeClock();

        var handler = new ValidarProveedorHandler(db, expediente, user, empresa, clock, audit);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.Handle(new ValidarProveedorCommand(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task ValidarProveedor_ExpedienteIncompleto_ThrowsBusinessRuleException()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        var p = new Proveedor(Guid.NewGuid(), "P01", "Prov 1", "PRO010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);
        db.Proveedores.Add(p);
        await db.SaveChangesAsync();

        var expediente = new FakeExpedientePort
        {
            Completo = false,
            Faltantes = ["CSF", "CARATULA_BANCARIA"]
        };
        var user = new FakeUser();
        var audit = new FakeAudit();
        var clock = new FakeClock();

        var handler = new ValidarProveedorHandler(db, expediente, user, empresa, clock, audit);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.Handle(new ValidarProveedorCommand(p.Id), CancellationToken.None));

        Assert.Equal("PROVEEDOR_EXPEDIENTE_INCOMPLETO", ex.Code);
    }

    [Fact]
    public async Task ValidarProveedor_ExpedienteCompleto_ValidaYRegistraAuditoria()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        var p = new Proveedor(Guid.NewGuid(), "P01", "Prov 1", "PRO010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);
        db.Proveedores.Add(p);
        await db.SaveChangesAsync();

        var expediente = new FakeExpedientePort { Completo = true };
        var user = new FakeUser { UserId = Guid.NewGuid(), UserName = "Analista CxP" };
        var audit = new FakeAudit();
        var clock = new FakeClock();

        var handler = new ValidarProveedorHandler(db, expediente, user, empresa, clock, audit);

        await handler.Handle(new ValidarProveedorCommand(p.Id), CancellationToken.None);

        var actualizado = await db.Proveedores.FindAsync(p.Id);
        Assert.NotNull(actualizado);
        Assert.Equal(EstatusCatalogo.Activo, actualizado.Estatus);
        Assert.Equal(user.UserId, actualizado.ValidadoPorId);
        Assert.Equal(clock.UtcNow, actualizado.ValidadoEn);
        Assert.Contains(audit.Registros, r => r.Operacion == "proveedor.validado");
    }

    [Fact]
    public async Task RechazarProveedor_NotFound_ThrowsEntityNotFoundException()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        var user = new FakeUser();
        var audit = new FakeAudit();
        var clock = new FakeClock();

        var handler = new RechazarProveedorHandler(db, user, empresa, clock, audit);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.Handle(new RechazarProveedorCommand(Guid.NewGuid(), "Motivo valido de rechazo"), CancellationToken.None));
    }

    [Fact]
    public async Task RechazarProveedor_MotivoValido_RechazaYRegistraAuditoria()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        var p = new Proveedor(Guid.NewGuid(), "P02", "Prov 2", "PRO020202BBB", TipoPersonaProveedor.Fisica, EstatusCatalogo.EnRevision);
        db.Proveedores.Add(p);
        await db.SaveChangesAsync();

        var user = new FakeUser { UserId = Guid.NewGuid(), UserName = "Jefe CxP" };
        var audit = new FakeAudit();
        var clock = new FakeClock();

        var handler = new RechazarProveedorHandler(db, user, empresa, clock, audit);

        await handler.Handle(new RechazarProveedorCommand(p.Id, "Expediente con documentación falsa"), CancellationToken.None);

        var actualizado = await db.Proveedores.FindAsync(p.Id);
        Assert.NotNull(actualizado);
        Assert.Equal(EstatusCatalogo.Inactivo, actualizado.Estatus);
        Assert.Equal("Expediente con documentación falsa", actualizado.MotivoRechazo);
        Assert.Equal(user.UserId, actualizado.ValidadoPorId);
        Assert.Equal(clock.UtcNow, actualizado.ValidadoEn);
        Assert.Contains(audit.Registros, r => r.Operacion == "proveedor.rechazado");
    }
}
