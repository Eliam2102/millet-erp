using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Catalogos.Proveedores;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.UnitTests.Proveedores;

/// <summary>86e3tactf: bloqueo total del proveedor (marca aparte del estatus; solo desbloquea quien lo pidió o quien tenga el permiso de reemplazo).</summary>
public sealed class BloqueoProveedorTests
{
    private const string Motivo = "Dirección revisa su cuenta";
    private static readonly Guid Contador = Guid.NewGuid();
    private static readonly Guid CxpOtro = Guid.NewGuid();

    private static (CompartidoDbContext Db, Proveedor Proveedor, FakeAudit Audit, FakeEmpresa Empresa) Preparar()
    {
        var empresa = new FakeEmpresa();
        var db = new CompartidoDbContext(
            new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var proveedor = new Proveedor(Guid.NewGuid(), "P01", "Herrajes DEMO", "HER010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        db.Proveedores.Add(proveedor);
        db.SaveChanges();
        return (db, proveedor, new FakeAudit(), empresa);
    }

    private static BloqueoProveedorHandlers Handler(CompartidoDbContext db, FakeEmpresa empresa, FakeAudit audit, Guid usuario, params string[] permisos)
    {
        var p = new FakePermisos();
        foreach (var x in permisos) p.Concedidos.Add(x);
        return new(db, new FakeUser { UserId = usuario }, p, empresa, new FakeClock(), audit);
    }

    [Fact]
    public async Task Bloquear_marca_aparte_del_estatus_y_deja_bitacora_con_motivo()
    {
        var (db, prov, audit, empresa) = Preparar();
        var res = await Handler(db, empresa, audit, Contador).Handle(new BloquearProveedorCommand(prov.Id, Motivo), default);

        Assert.True(res.Bloqueado);
        Assert.True(prov.Bloqueado);
        Assert.Equal(EstatusCatalogo.Activo, prov.Estatus); // el alta no cambia
        Assert.Equal(Contador, prov.BloqueadoPorId);
        Assert.NotNull(prov.BloqueadoEn);
        var registro = Assert.Single(audit.Registros);
        Assert.Equal("proveedor.bloqueado", registro.Operacion);
        Assert.Contains(Motivo, registro.Resumen);
    }

    [Theory]
    [InlineData("")]
    [InlineData("corto")]
    public async Task Bloquear_sin_motivo_valido_no_bloquea(string motivo)
    {
        var (db, prov, audit, empresa) = Preparar();
        Assert.False(new BloquearProveedorValidator().Validate(new BloquearProveedorCommand(prov.Id, motivo)).IsValid);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Handler(db, empresa, audit, Contador).Handle(new BloquearProveedorCommand(prov.Id, motivo), default));
        Assert.Equal("PROVEEDOR_MOTIVO_BLOQUEO_INVALIDO", ex.Code);
        Assert.False(prov.Bloqueado);
        Assert.Empty(audit.Registros);
    }

    [Fact]
    public async Task Otro_usuario_con_el_permiso_de_bloquear_no_desbloquea()
    {
        var (db, prov, audit, empresa) = Preparar();
        await Handler(db, empresa, audit, Contador).Handle(new BloquearProveedorCommand(prov.Id, Motivo), default);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            Handler(db, empresa, audit, CxpOtro).Handle(new DesbloquearProveedorCommand(prov.Id, "Ya se aclaró con el proveedor"), default));
        Assert.Equal("SOLO_DESBLOQUEA_SOLICITANTE", ex.Code);
        Assert.True(prov.Bloqueado);
    }

    [Fact]
    public async Task Quien_lo_pidio_desbloquea_y_queda_bitacora_del_bloqueo_y_del_desbloqueo()
    {
        var (db, prov, audit, empresa) = Preparar();
        await Handler(db, empresa, audit, Contador).Handle(new BloquearProveedorCommand(prov.Id, Motivo), default);
        await Handler(db, empresa, audit, Contador).Handle(new DesbloquearProveedorCommand(prov.Id, "Ya se aclaró con el proveedor"), default);

        Assert.False(prov.Bloqueado);
        Assert.Null(prov.MotivoBloqueo);
        Assert.Equal(["proveedor.bloqueado", "proveedor.desbloqueado"], audit.Registros.Select(r => r.Operacion));
    }

    [Fact]
    public async Task Con_el_permiso_de_reemplazo_desbloquea_en_lugar_del_solicitante_con_motivo()
    {
        var (db, prov, audit, empresa) = Preparar();
        await Handler(db, empresa, audit, Contador).Handle(new BloquearProveedorCommand(prov.Id, Motivo), default);

        await Handler(db, empresa, audit, CxpOtro, "cuentas_por_pagar.bloqueos.liberar-en-lugar")
            .Handle(new DesbloquearProveedorCommand(prov.Id, "El Contador General está de vacaciones"), default);
        Assert.False(prov.Bloqueado);

        // sin motivo no desbloquea ni con el permiso
        await Handler(db, empresa, audit, Contador).Handle(new BloquearProveedorCommand(prov.Id, Motivo), default);
        Assert.False(new DesbloquearProveedorValidator().Validate(new DesbloquearProveedorCommand(prov.Id, "")).IsValid);
    }

    [Fact]
    public async Task Bloquear_dos_veces_o_desbloquear_uno_libre_es_error()
    {
        var (db, prov, audit, empresa) = Preparar();
        var ex1 = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Handler(db, empresa, audit, Contador).Handle(new DesbloquearProveedorCommand(prov.Id, "No estaba bloqueado"), default));
        Assert.Equal("PROVEEDOR_NO_BLOQUEADO", ex1.Code);
        await Handler(db, empresa, audit, Contador).Handle(new BloquearProveedorCommand(prov.Id, Motivo), default);
        var ex2 = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Handler(db, empresa, audit, Contador).Handle(new BloquearProveedorCommand(prov.Id, Motivo), default));
        Assert.Equal("PROVEEDOR_YA_BLOQUEADO", ex2.Code);
    }
}
