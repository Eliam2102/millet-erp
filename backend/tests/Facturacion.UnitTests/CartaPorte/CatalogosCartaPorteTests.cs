using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.CartaPorte.Catalogos;
using Millet.Facturacion.Domain.CartaPorte;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.CartaPorte;

/// <summary>
/// Catálogos de Carta Porte (vehículos/operadores): edición, activación
/// idempotente y listados para la pantalla de administración.
/// </summary>
public sealed class CatalogosCartaPorteTests
{
    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    // ---- Dominio ----

    [Fact]
    public void Vehiculo_Actualizar_reemplaza_datos_editables_sin_tocar_placa()
    {
        var v = Vehiculo.Crear(Guid.NewGuid(), "ABC-123", "C2", 2020, "TPAF01", "PERM-1", "Seguros X", "POL-1", 10m);

        v.Actualizar("C2R2", 2023, "TPAF02", "PERM-2", "Seguros Y", "POL-2", 17.5m);

        v.Placa.Should().Be("ABC-123");
        v.ConfigVehicular.Should().Be("C2R2");
        v.AnioModelo.Should().Be(2023);
        v.TipoPermisoSct.Should().Be("TPAF02");
        v.PesoBrutoVehicular.Should().Be(17.5m);
    }

    [Fact]
    public void Vehiculo_Actualizar_valida_config_y_peso()
    {
        var v = Vehiculo.Crear(Guid.NewGuid(), "ABC-123", "C2", 2020);

        var config = () => v.Actualizar("", 2023, null, null, null, null, null);
        config.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VEHICULO_CONFIG_INVALIDA");

        var peso = () => v.Actualizar("C2R2", 2023, null, null, null, null, 0m);
        peso.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VEHICULO_PESO_BRUTO_INVALIDO");
    }

    [Fact]
    public void Vehiculo_Desactivar_y_Activar_son_idempotentes()
    {
        var v = Vehiculo.Crear(Guid.NewGuid(), "ABC-123", "C2", 2020);
        v.Activo.Should().BeTrue();

        v.Desactivar();
        v.Desactivar();
        v.Activo.Should().BeFalse();

        v.Activar();
        v.Activar();
        v.Activo.Should().BeTrue();
    }

    [Fact]
    public void Operador_Actualizar_valida_y_conserva_rfc()
    {
        var o = Operador.Crear(Guid.NewGuid(), "OPER010101AAA", "Juan Pérez", "LIC-1");

        o.Actualizar("Juan Pérez López", "LIC-2");
        o.Rfc.Should().Be("OPER010101AAA");
        o.Nombre.Should().Be("Juan Pérez López");
        o.NumLicencia.Should().Be("LIC-2");

        var act = () => o.Actualizar("", "LIC-3");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "OPERADOR_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Operador_Desactivar_y_Activar_son_idempotentes()
    {
        var o = Operador.Crear(Guid.NewGuid(), "OPER010101AAA", "Juan Pérez", "LIC-1");

        o.Desactivar();
        o.Desactivar();
        o.Activo.Should().BeFalse();

        o.Activar();
        o.Activar();
        o.Activo.Should().BeTrue();
    }

    // ---- Listar ----

    [Fact]
    public async Task ListarVehiculos_filtra_inactivos_por_default_y_ordena_por_placa()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var activo1 = Vehiculo.Crear(empresaId, "ZZZ-999", "C2", 2021);
        var activo2 = Vehiculo.Crear(empresaId, "AAA-111", "C2R2", 2022, pesoBrutoVehicular: 17.5m);
        var inactivo = Vehiculo.Crear(empresaId, "MMM-555", "VL", 2020);
        inactivo.Desactivar();
        db.Vehiculos.AddRange(activo1, activo2, inactivo);
        await db.SaveChangesAsync();

        var handler = new ListarVehiculosHandler(db);

        var soloActivos = await handler.Handle(new ListarVehiculosQuery(), CancellationToken.None);
        soloActivos.Select(v => v.Placa).Should().Equal("AAA-111", "ZZZ-999");
        soloActivos.Single(v => v.Placa == "AAA-111").PesoBrutoVehicular.Should().Be(17.5m);

        var todos = await handler.Handle(new ListarVehiculosQuery(IncluirInactivos: true), CancellationToken.None);
        todos.Should().HaveCount(3);
        todos.Single(v => v.Placa == "MMM-555").Activo.Should().BeFalse();
    }

    [Fact]
    public async Task ListarOperadores_filtra_inactivos_por_default()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var activo = Operador.Crear(empresaId, "OPER010101AAA", "Juan Pérez", "LIC-1");
        var inactivo = Operador.Crear(empresaId, "OPER020202BBB", "Pedro López", "LIC-2");
        inactivo.Desactivar();
        db.Operadores.AddRange(activo, inactivo);
        await db.SaveChangesAsync();

        var handler = new ListarOperadoresHandler(db);

        (await handler.Handle(new ListarOperadoresQuery(), CancellationToken.None))
            .Should().ContainSingle(o => o.Rfc == "OPER010101AAA");
        (await handler.Handle(new ListarOperadoresQuery(IncluirInactivos: true), CancellationToken.None))
            .Should().HaveCount(2);
    }

    // ---- Actualizar (handlers) ----

    [Fact]
    public async Task ActualizarVehiculo_edita_datos_y_devuelve_item()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var v = Vehiculo.Crear(empresaId, "ABC-123", "C2", 2020);
        db.Vehiculos.Add(v);
        await db.SaveChangesAsync();

        var item = await new ActualizarVehiculoHandler(db).Handle(
            new ActualizarVehiculoCommand(v.Id, ConfigVehicular: "C2R2", AnioModelo: 2024,
                Aseguradora: "Seguros Y", PesoBrutoVehicular: 12.3m),
            CancellationToken.None);

        item.Placa.Should().Be("ABC-123");
        item.ConfigVehicular.Should().Be("C2R2");
        item.AnioModelo.Should().Be(2024);
        item.PesoBrutoVehicular.Should().Be(12.3m);
        item.Activo.Should().BeTrue();
        (await db.Vehiculos.SingleAsync()).ConfigVehicular.Should().Be("C2R2");
    }

    [Fact]
    public async Task ActualizarVehiculo_solo_activo_no_toca_los_datos()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var v = Vehiculo.Crear(empresaId, "ABC-123", "C2", 2020, pesoBrutoVehicular: 10m);
        db.Vehiculos.Add(v);
        await db.SaveChangesAsync();

        var item = await new ActualizarVehiculoHandler(db).Handle(
            new ActualizarVehiculoCommand(v.Id, Activo: false), CancellationToken.None);

        item.Activo.Should().BeFalse();
        item.ConfigVehicular.Should().Be("C2");
        item.PesoBrutoVehicular.Should().Be(10m);
    }

    [Fact]
    public async Task ActualizarVehiculo_inexistente_lanza_NotFound()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var act = () => new ActualizarVehiculoHandler(db).Handle(
            new ActualizarVehiculoCommand(Guid.NewGuid(), Activo: false), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "VEHICULO_NO_ENCONTRADO");
    }

    [Fact]
    public async Task ActualizarOperador_edita_desactiva_y_404_si_no_existe()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var o = Operador.Crear(empresaId, "OPER010101AAA", "Juan Pérez", "LIC-1");
        db.Operadores.Add(o);
        await db.SaveChangesAsync();

        var handler = new ActualizarOperadorHandler(db);

        var editado = await handler.Handle(
            new ActualizarOperadorCommand(o.Id, Nombre: "Juan Pérez López", NumLicencia: "LIC-2"),
            CancellationToken.None);
        editado.Rfc.Should().Be("OPER010101AAA");
        editado.NumLicencia.Should().Be("LIC-2");

        var desactivado = await handler.Handle(
            new ActualizarOperadorCommand(o.Id, Activo: false), CancellationToken.None);
        desactivado.Activo.Should().BeFalse();
        desactivado.Nombre.Should().Be("Juan Pérez López");

        var act = () => handler.Handle(new ActualizarOperadorCommand(Guid.NewGuid(), Activo: true), CancellationToken.None);
        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "OPERADOR_NO_ENCONTRADO");
    }
}
