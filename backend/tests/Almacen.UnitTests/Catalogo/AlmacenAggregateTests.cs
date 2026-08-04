using Millet.Almacen.Domain.Catalogo;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Catalogo;

/// <summary>
/// Tests del agregado raíz <see cref="Almacen"/> + entidad hija
/// <see cref="SubAlmacen"/> (F1-PR1). Cubre invariantes del ctor + edit.
/// </summary>
public class AlmacenAggregateTests
{
    [Fact]
    public void Crear_almacen_con_datos_validos_inicializa_estado()
    {
        var id = Guid.NewGuid();
        var sucursalId = Guid.NewGuid();

        var almacen = new Almacen.Domain.Catalogo.Almacen(
            id, "MTY-PRINCIPAL", "Monterrey Principal", sucursalId);

        almacen.Id.Should().Be(id);
        almacen.Clave.Should().Be("MTY-PRINCIPAL");
        almacen.Nombre.Should().Be("Monterrey Principal");
        almacen.SucursalId.Should().Be(sucursalId);
        almacen.Estatus.Should().Be(EstatusCatalogo.Activo);
        almacen.SubAlmacenes.Should().BeEmpty();
    }

    [Fact]
    public void Crear_almacen_con_clave_vacia_falla()
    {
        var act = () => new Almacen.Domain.Catalogo.Almacen(
            Guid.NewGuid(), "", "Nombre", Guid.NewGuid());

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("ALMACEN_CLAVE_INVALIDA");
    }

    [Fact]
    public void Crear_almacen_con_clave_larga_falla()
    {
        var claveDe21 = new string('a', 21);
        var act = () => new Almacen.Domain.Catalogo.Almacen(
            Guid.NewGuid(), claveDe21, "Nombre", Guid.NewGuid());

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("ALMACEN_CLAVE_INVALIDA");
    }

    [Fact]
    public void Crear_almacen_con_sucursal_vacia_falla()
    {
        var act = () => new Almacen.Domain.Catalogo.Almacen(
            Guid.NewGuid(), "MTY", "Monterrey", Guid.Empty);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("ALMACEN_SUCURSAL_INVALIDA");
    }

    [Fact]
    public void Editar_almacen_aplica_nuevos_valores()
    {
        var almacen = new Almacen.Domain.Catalogo.Almacen(
            Guid.NewGuid(), "MTY", "Monterrey", Guid.NewGuid());

        var nuevaSucursal = Guid.NewGuid();
        almacen.Editar("MTY-V2", "Monterrey v2", nuevaSucursal);

        almacen.Clave.Should().Be("MTY-V2");
        almacen.Nombre.Should().Be("Monterrey v2");
        almacen.SucursalId.Should().Be(nuevaSucursal);
    }

    [Fact]
    public void Cambiar_estatus_actualiza_el_valor()
    {
        var almacen = new Almacen.Domain.Catalogo.Almacen(
            Guid.NewGuid(), "MTY", "Monterrey", Guid.NewGuid());

        almacen.CambiarEstatus(EstatusCatalogo.Inactivo);

        almacen.Estatus.Should().Be(EstatusCatalogo.Inactivo);
    }
}

public class SubAlmacenAggregateTests
{
    [Fact]
    public void Crear_sub_almacen_con_datos_validos_inicializa_estado()
    {
        var id = Guid.NewGuid();
        var almacenId = Guid.NewGuid();

        var sub = new SubAlmacen(
            id, almacenId, "ACC-CIR", "Accesorios circulares",
            TipoSubAlmacen.Insumos);

        sub.Id.Should().Be(id);
        sub.AlmacenId.Should().Be(almacenId);
        sub.Clave.Should().Be("ACC-CIR");
        sub.Nombre.Should().Be("Accesorios circulares");
        sub.Tipo.Should().Be(TipoSubAlmacen.Insumos);
        sub.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Fact]
    public void Crear_sub_almacen_sin_almacen_padre_falla()
    {
        var act = () => new SubAlmacen(
            Guid.NewGuid(), Guid.Empty, "ACC-CIR", "Accesorios",
            TipoSubAlmacen.Insumos);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("SUBALMACEN_ALMACEN_INVALIDO");
    }

    [Fact]
    public void Crear_sub_almacen_con_clave_invalida_falla()
    {
        var act = () => new SubAlmacen(
            Guid.NewGuid(), Guid.NewGuid(), "", "Nombre",
            TipoSubAlmacen.Insumos);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("SUBALMACEN_CLAVE_INVALIDA");
    }

    [Theory]
    [InlineData(TipoSubAlmacen.Insumos)]
    [InlineData(TipoSubAlmacen.MaterialesDirectos)]
    [InlineData(TipoSubAlmacen.MaterialEnRevision)]
    [InlineData(TipoSubAlmacen.Transitorio)]
    public void Tipo_sub_almacen_acepta_todos_los_valores_del_enum(TipoSubAlmacen tipo)
    {
        var sub = new SubAlmacen(
            Guid.NewGuid(), Guid.NewGuid(), "X", "Nombre", tipo);

        sub.Tipo.Should().Be(tipo);
    }

    [Fact]
    public void Editar_sub_almacen_aplica_nuevos_valores()
    {
        var sub = new SubAlmacen(
            Guid.NewGuid(), Guid.NewGuid(), "ACC-CIR", "Accesorios circulares",
            TipoSubAlmacen.Insumos);

        sub.Editar("MAT-DIR", "Materiales directos", TipoSubAlmacen.MaterialesDirectos);

        sub.Clave.Should().Be("MAT-DIR");
        sub.Nombre.Should().Be("Materiales directos");
        sub.Tipo.Should().Be(TipoSubAlmacen.MaterialesDirectos);
    }
}
