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
        p.ActualizarClasificacionFiscal(OrigenProveedor.Nacional, "601", null);
        db.Proveedores.Add(p);
        await Siembra.TiposProveedorAsync(db);
        await db.SaveChangesAsync();

        var expediente = new FakeExpedientePort
        {
            Completo = false,
            Faltantes = ["constancia_situacion_fiscal", "contrato"],
            Vencidos = ["comprobante_domicilio"],
        };
        var user = new FakeUser();
        var audit = new FakeAudit();
        var clock = new FakeClock();

        var handler = new ValidarProveedorHandler(db, expediente, user, empresa, clock, audit);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.Handle(new ValidarProveedorCommand(p.Id), CancellationToken.None));

        Assert.Equal("PROVEEDOR_EXPEDIENTE_INCOMPLETO", ex.Code);
        // El mensaje nombra los documentos, no sus códigos internos.
        Assert.Contains("Constancia de situación fiscal", ex.Message);
        Assert.Contains("Contrato", ex.Message);
        Assert.Contains("vencidos: Comprobante de domicilio", ex.Message);
        Assert.DoesNotContain("constancia_situacion_fiscal", ex.Message);
    }

    [Fact]
    public async Task ValidarProveedor_SinClasificar_RechazaConClasificacionPendiente()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        var p = new Proveedor(Guid.NewGuid(), "P01", "Prov 1", "PRO010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);
        db.Proveedores.Add(p);
        await db.SaveChangesAsync();

        var handler = new ValidarProveedorHandler(db, new FakeExpedientePort(), new FakeUser(), empresa, new FakeClock(), new FakeAudit());

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.Handle(new ValidarProveedorCommand(p.Id), CancellationToken.None));
        Assert.Equal("PROVEEDOR_CLASIFICACION_PENDIENTE", ex.Code);
        Assert.Equal(EstatusCatalogo.EnRevision, p.Estatus);
    }

    [Fact]
    public async Task ActualizarClasificacion_RegimenDeOtraNaturalezaSeRechaza()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        db.RegimenesFiscales.AddRange(
            new RegimenFiscal(Guid.NewGuid(), "601", "General de Ley Personas Morales", false),
            new RegimenFiscal(Guid.NewGuid(), "612", "Personas Físicas con Actividades Empresariales", true),
            new RegimenFiscal(Guid.NewGuid(), "626", "RESICO", true, aplicaPersonaMoral: true));
        var fisica = new Proveedor(Guid.NewGuid(), "PF", "Pedro Físico", "PEFP010101AAA", TipoPersonaProveedor.Fisica, EstatusCatalogo.EnRevision);
        var moral = new Proveedor(Guid.NewGuid(), "PM", "Moral SA", "MOR010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);
        db.Proveedores.AddRange(fisica, moral);
        await db.SaveChangesAsync();
        var handler = new ActualizarClasificacionFiscalProveedorHandler(db, new FakeUser(), empresa, new FakeAudit());

        var fisica601 = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.Handle(new(fisica.Id, OrigenProveedor.Nacional, "601", null), default));
        Assert.Equal("PROVEEDOR_REGIMEN_NO_APLICA", fisica601.Code);
        var moral612 = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            handler.Handle(new(moral.Id, OrigenProveedor.Nacional, "612", null), default));
        Assert.Equal("PROVEEDOR_REGIMEN_NO_APLICA", moral612.Code);

        // Los válidos y el 626 (ambas naturalezas) pasan; el extranjero no lleva régimen.
        await handler.Handle(new(fisica.Id, OrigenProveedor.Nacional, "612", null), default);
        await handler.Handle(new(moral.Id, OrigenProveedor.Nacional, "601", null), default);
        await handler.Handle(new(moral.Id, OrigenProveedor.Nacional, "626", null), default);
        await handler.Handle(new(fisica.Id, OrigenProveedor.Extranjero, null, "SIN_RETENCION"), default);
    }

    [Fact]
    public async Task CrearProveedor_ExtranjeroSinRfc_GuardaElGenericoYLaClasificacionEnLaMismaAlta()
    {
        using var db = CrearDb();
        var handler = new Millet.DatosMaestros.Application.Catalogos.CrearProveedorHandler(db);
        var cmd = new Millet.DatosMaestros.Application.Catalogos.CrearProveedorCommand(
            "EXT-01", "John Smith", null, TipoPersonaProveedor.Fisica, null, null, null, null, null,
            OrigenProveedor.Extranjero, null, "SIN_RETENCION");
        Assert.True(new Millet.DatosMaestros.Application.Catalogos.CrearProveedorValidator().Validate(cmd).IsValid);

        var res = await handler.Handle(cmd, default);

        var p = await db.Proveedores.SingleAsync(x => x.Id == res.Id);
        Assert.Equal("XEXX010101000", p.Rfc);
        Assert.Equal(OrigenProveedor.Extranjero, p.Origen);
        Assert.Equal("SIN_RETENCION", p.ConceptoRetencion);
        // Un nacional sí exige RFC.
        Assert.False(new Millet.DatosMaestros.Application.Catalogos.CrearProveedorValidator()
            .Validate(cmd with { Origen = OrigenProveedor.Nacional }).IsValid);
    }

    [Fact]
    public async Task ValidarProveedor_RegistraLaVersionDeLaMatrizEnElProveedorYLaBitacora()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        var p = new Proveedor(Guid.NewGuid(), "P01", "Prov 1", "PRO010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);
        p.ActualizarClasificacionFiscal(OrigenProveedor.Nacional, "601", null);
        db.Proveedores.Add(p);
        await Siembra.TiposProveedorAsync(db);
        await db.SaveChangesAsync();
        // En producción el interceptor de metadatos sube Version en cada cambio; aquí se simula (InMemory no lo tiene).
        foreach (var t in db.AdjuntoTiposDocumento.Local) db.Entry(t).Property(nameof(t.Version)).CurrentValue = 2;
        await db.SaveChangesAsync();
        var esperada = await Millet.Compartido.Application.Adjuntos.ExpedienteAdjuntos.VersionMatrizAsync(db, "proveedor", default);
        Assert.Equal(10, esperada);

        await new ValidarProveedorHandler(db, new FakeExpedientePort(), new FakeUser(), empresa, new FakeClock(), new FakeAudit())
            .Handle(new ValidarProveedorCommand(p.Id), default);

        Assert.Equal(esperada, p.ValidadoConMatrizVersion);
    }

    [Fact]
    public async Task ValidarProveedor_ExpedienteCompleto_ValidaYRegistraAuditoria()
    {
        var empresa = new FakeEmpresa();
        using var db = CrearDb(empresa);
        var p = new Proveedor(Guid.NewGuid(), "P01", "Prov 1", "PRO010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.EnRevision);
        p.ActualizarClasificacionFiscal(OrigenProveedor.Nacional, "601", null);
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
