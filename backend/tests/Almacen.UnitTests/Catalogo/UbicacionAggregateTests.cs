using Millet.Almacen.Domain.Catalogo;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Catalogo;

/// <summary>
/// Tests de la entidad <see cref="Ubicacion"/> (Nivel 4, ADR-0047, PR1).
/// Cubre invariantes del ctor + edit. La unicidad de clave
/// <c>(sub_almacen_id, clave)</c> y la integridad jerárquica (FK a
/// sub-almacén) se garantizan en la capa de persistencia (índice único
/// <c>ux_ubicaciones_sub_almacen_clave</c> + FK física + validación del
/// handler), igual que en <see cref="SubAlmacen"/>.
/// </summary>
public class UbicacionAggregateTests
{
    [Fact]
    public void Crear_ubicacion_con_datos_validos_inicializa_estado()
    {
        var id = Guid.NewGuid();
        var subAlmacenId = Guid.NewGuid();

        var ubicacion = new Ubicacion(
            id, subAlmacenId, "A-01-01", "Rack A, nivel 1, posición 1");

        ubicacion.Id.Should().Be(id);
        ubicacion.SubAlmacenId.Should().Be(subAlmacenId);
        ubicacion.Clave.Should().Be("A-01-01");
        ubicacion.Nombre.Should().Be("Rack A, nivel 1, posición 1");
        ubicacion.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Fact]
    public void Crear_ubicacion_sin_sub_almacen_padre_falla()
    {
        var act = () => new Ubicacion(
            Guid.NewGuid(), Guid.Empty, "A-01-01", "Rack A");

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("UBICACION_SUBALMACEN_INVALIDO");
    }

    [Fact]
    public void Crear_ubicacion_con_clave_vacia_falla()
    {
        var act = () => new Ubicacion(
            Guid.NewGuid(), Guid.NewGuid(), "", "Rack A");

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("UBICACION_CLAVE_INVALIDA");
    }

    [Fact]
    public void Crear_ubicacion_con_clave_larga_falla()
    {
        var claveDe21 = new string('a', 21);
        var act = () => new Ubicacion(
            Guid.NewGuid(), Guid.NewGuid(), claveDe21, "Rack A");

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("UBICACION_CLAVE_INVALIDA");
    }

    [Fact]
    public void Crear_ubicacion_con_nombre_vacio_falla()
    {
        var act = () => new Ubicacion(
            Guid.NewGuid(), Guid.NewGuid(), "A-01-01", "");

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("UBICACION_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Editar_ubicacion_aplica_nuevos_valores()
    {
        var ubicacion = new Ubicacion(
            Guid.NewGuid(), Guid.NewGuid(), "A-01-01", "Rack A, nivel 1");

        ubicacion.Editar("B-02-03", "Rack B, nivel 2, posición 3");

        ubicacion.Clave.Should().Be("B-02-03");
        ubicacion.Nombre.Should().Be("Rack B, nivel 2, posición 3");
    }

    [Fact]
    public void Cambiar_estatus_actualiza_el_valor()
    {
        var ubicacion = new Ubicacion(
            Guid.NewGuid(), Guid.NewGuid(), "A-01-01", "Rack A");

        ubicacion.CambiarEstatus(EstatusCatalogo.Inactivo);

        ubicacion.Estatus.Should().Be(EstatusCatalogo.Inactivo);
    }
}
