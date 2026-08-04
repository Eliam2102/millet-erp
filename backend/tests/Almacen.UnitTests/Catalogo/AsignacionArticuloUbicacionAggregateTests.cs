using Millet.Almacen.Domain.Catalogo;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Catalogo;

/// <summary>
/// Tests del agregado <see cref="AsignacionArticuloUbicacion"/> (OITW, ADR-0047
/// PR3). Tras PR C la entidad es pura relación artículo↔ubicación + estatus
/// (min/máx/reorden/bandera/objetivo salieron a N1/N2, <see cref="ConfiguracionReorden"/>).
/// Invariantes del ctor + cambio de estatus. La unicidad
/// <c>(ubicacion_id, articulo_id)</c>, el guardrail EN_USO y la fila-en-0 se
/// cubren en integración (Api.IntegrationTests).
/// </summary>
public class AsignacionArticuloUbicacionAggregateTests
{
    [Fact]
    public void Crear_con_datos_validos_inicializa_estado()
    {
        var id = Guid.NewGuid();
        var ubic = Guid.NewGuid();
        var art = Guid.NewGuid();

        var a = new AsignacionArticuloUbicacion(id, ubic, art);

        a.Id.Should().Be(id);
        a.UbicacionId.Should().Be(ubic);
        a.ArticuloId.Should().Be(art);
        a.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Fact]
    public void Crear_sin_ubicacion_falla()
    {
        var act = () => new AsignacionArticuloUbicacion(
            Guid.NewGuid(), Guid.Empty, Guid.NewGuid());

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("ASIGNACION_SIN_UBICACION");
    }

    [Fact]
    public void Crear_sin_articulo_falla()
    {
        var act = () => new AsignacionArticuloUbicacion(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("ASIGNACION_SIN_ARTICULO");
    }

    [Fact]
    public void Cambiar_estatus_actualiza_el_valor()
    {
        var a = new AsignacionArticuloUbicacion(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        a.CambiarEstatus(EstatusCatalogo.Inactivo);

        a.Estatus.Should().Be(EstatusCatalogo.Inactivo);
    }
}
