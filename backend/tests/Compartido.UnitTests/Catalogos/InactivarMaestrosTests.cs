using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Catalogos;
using Millet.Compartido.Application.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.DatosMaestros.Application.Catalogos;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.UnitTests.Catalogos;

public sealed class InactivarMaestrosTests
{
    private sealed class FuenteFija(params string[] pendientes) : IPendientesMaestroReadPort
    {
        public Task<IReadOnlyList<string>> ObtenerAsync(TipoMaestro tipo, Guid id, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(pendientes);
    }

    private static CompartidoDbContext CrearDb() => new(
        new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeEmpresa());

    private static MaestroEstatusService Servicio(FakeAudit audit, params string[] pendientes)
        => new([new FuenteFija(pendientes)], audit, new FakeUser(), new FakeEmpresa());

    private static Proveedor NuevoProveedor(EstatusCatalogo estatus)
        => new(Guid.NewGuid(), "P-4411", "Vidrios del Bajío SA de CV", "VBA010101AAA", TipoPersonaProveedor.Moral, estatus);

    [Fact]
    public async Task Proveedor_con_OC_abierta_se_rechaza_y_el_mensaje_nombra_la_OC()
    {
        using var db = CrearDb();
        var proveedor = NuevoProveedor(EstatusCatalogo.Activo);
        db.Add(proveedor); await db.SaveChangesAsync();
        var audit = new FakeAudit();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new DesactivarProveedorHandler(db, Servicio(audit, "OC OC-MID2026-000412 (Autorizada).")).Handle(new(proveedor.Id, "Ya no le compramos"), default));

        Assert.Equal("MAESTRO_CON_PENDIENTES", ex.Code);
        Assert.Contains("OC-MID2026-000412", ex.Message);
        Assert.Equal(EstatusCatalogo.Activo, proveedor.Estatus);
        Assert.Empty(audit.Registros);
    }

    [Fact]
    public async Task Proveedor_sin_pendientes_se_inactiva_y_deja_el_motivo_en_la_bitacora()
    {
        using var db = CrearDb();
        var proveedor = NuevoProveedor(EstatusCatalogo.Activo);
        db.Add(proveedor); await db.SaveChangesAsync();
        var audit = new FakeAudit();

        await new DesactivarProveedorHandler(db, Servicio(audit)).Handle(new(proveedor.Id, " Cerró operaciones "), default);

        Assert.Equal(EstatusCatalogo.Inactivo, proveedor.Estatus);
        var registro = Assert.Single(audit.Registros);
        Assert.Equal("proveedor.inactivar", registro.Operacion);
        Assert.Equal(proveedor.Id, registro.EntidadId);
        Assert.Contains("Motivo: Cerró operaciones", registro.Resumen);
    }

    [Fact]
    public async Task Reactivar_proveedor_deja_el_motivo_en_la_bitacora()
    {
        using var db = CrearDb();
        var proveedor = NuevoProveedor(EstatusCatalogo.Inactivo);
        db.Add(proveedor); await db.SaveChangesAsync();
        var audit = new FakeAudit();

        await new ReactivarProveedorHandler(db, Servicio(audit)).Handle(new(proveedor.Id, "Retomamos el suministro"), default);

        Assert.Equal(EstatusCatalogo.Activo, proveedor.Estatus);
        var registro = Assert.Single(audit.Registros);
        Assert.Equal("proveedor.reactivar", registro.Operacion);
        Assert.Contains("Retomamos el suministro", registro.Resumen);
    }

    [Fact]
    public async Task Cliente_con_saldo_o_pedidos_se_rechaza_y_lista_todo_lo_abierto()
    {
        using var db = CrearDb();
        var cliente = new Cliente(Guid.NewGuid(), "C-0087", "Cristales y Aluminio del Norte", rfc: "CAN010101AAA");
        db.Add(cliente); await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new DesactivarClienteHandler(db, Servicio(new FakeAudit(), "Factura A-1180 con saldo por cobrar de 48,250.00 MXN.", "Pedido 77102 por facturar (12,900.00 MXN).")).Handle(new(cliente.Id, "Cuenta cerrada"), default));

        Assert.Contains("A-1180", ex.Message);
        Assert.Contains("77102", ex.Message);
        Assert.Equal(EstatusCatalogo.Activo, cliente.Estatus);
    }

    [Fact]
    public async Task Articulo_con_existencia_se_rechaza_y_sin_pendientes_se_inactiva_y_reactiva_con_motivo()
    {
        using var db = CrearDb();
        var articulo = new Articulo(Guid.NewGuid(), "SIL-310", "Silicón estructural 310 ml", "PZA");
        db.Add(articulo); await db.SaveChangesAsync();
        var audit = new FakeAudit();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            new DesactivarArticuloHandler(db, Servicio(audit, "Tiene existencia en almacén.")).Handle(new(articulo.Id, "Descontinuado"), default));
        Assert.Equal(EstatusCatalogo.Activo, articulo.Estatus);

        await new DesactivarArticuloHandler(db, Servicio(audit)).Handle(new(articulo.Id, "Descontinuado"), default);
        await new ReactivarArticuloHandler(db, Servicio(audit)).Handle(new(articulo.Id, "Se vuelve a surtir"), default);

        Assert.Equal(EstatusCatalogo.Activo, articulo.Estatus);
        Assert.Equal(["articulo.inactivar", "articulo.reactivar"], audit.Registros.Select(r => r.Operacion));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void Sin_motivo_valido_los_seis_comandos_se_rechazan(string motivo)
    {
        var id = Guid.NewGuid();
        Assert.False(new DesactivarProveedorValidator().Validate(new DesactivarProveedorCommand(id, motivo)).IsValid);
        Assert.False(new ReactivarProveedorValidator().Validate(new ReactivarProveedorCommand(id, motivo)).IsValid);
        Assert.False(new DesactivarClienteValidator().Validate(new DesactivarClienteCommand(id, motivo)).IsValid);
        Assert.False(new ReactivarClienteValidator().Validate(new ReactivarClienteCommand(id, motivo)).IsValid);
        Assert.False(new DesactivarArticuloValidator().Validate(new DesactivarArticuloCommand(id, motivo)).IsValid);
        Assert.False(new ReactivarArticuloValidator().Validate(new ReactivarArticuloCommand(id, motivo)).IsValid);
        Assert.True(new ReactivarProveedorValidator().Validate(new ReactivarProveedorCommand(id, "Motivo suficiente")).IsValid);
    }
}
