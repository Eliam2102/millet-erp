using Millet.Almacen.Domain.Catalogo;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Catalogo;

/// <summary>
/// Tests del agregado <see cref="ConfiguracionReorden"/> (ADR-0047 PR5.A).
/// Invariantes del ctor + EditarPolitica. La unicidad compuesta, la exclusión
/// N1⊕N2 por sucursal y la validación asignación-existe se cubren en integración.
/// </summary>
public class ConfiguracionReordenAggregateTests
{
    [Fact]
    public void Crear_con_datos_validos_inicializa_estado()
    {
        var id = Guid.NewGuid();
        var art = Guid.NewGuid();
        var ent = Guid.NewGuid();

        var c = new ConfiguracionReorden(
            id, art, NivelReorden.Almacen, ent,
            minimo: 5m, maximo: 20m, puntoReorden: 10m,
            autoRequisicion: true, objetivo: ObjetivoReposicion.Maximo);

        c.Id.Should().Be(id);
        c.ArticuloId.Should().Be(art);
        c.Nivel.Should().Be(NivelReorden.Almacen);
        c.EntidadId.Should().Be(ent);
        c.Minimo.Should().Be(5m);
        c.Maximo.Should().Be(20m);
        c.PuntoReorden.Should().Be(10m);
        c.AutoRequisicion.Should().BeTrue();
        c.Objetivo.Should().Be(ObjetivoReposicion.Maximo);
        c.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Fact]
    public void Crear_sin_articulo_falla()
    {
        var act = () => new ConfiguracionReorden(
            Guid.NewGuid(), Guid.Empty, NivelReorden.Sucursal, Guid.NewGuid(),
            1m, 2m, 1m, false, ObjetivoReposicion.Minimo);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("REORDEN_SIN_ARTICULO");
    }

    [Fact]
    public void Crear_sin_entidad_falla()
    {
        var act = () => new ConfiguracionReorden(
            Guid.NewGuid(), Guid.NewGuid(), NivelReorden.Almacen, Guid.Empty,
            1m, 2m, 1m, false, ObjetivoReposicion.Minimo);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("REORDEN_SIN_ENTIDAD");
    }

    [Fact]
    public void Crear_con_maximo_menor_que_minimo_falla()
    {
        var act = () => new ConfiguracionReorden(
            Guid.NewGuid(), Guid.NewGuid(), NivelReorden.Almacen, Guid.NewGuid(),
            minimo: 10m, maximo: 5m, puntoReorden: 7m,
            autoRequisicion: false, objetivo: ObjetivoReposicion.Maximo);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("REORDEN_MAX_MENOR_MIN");
    }

    [Fact]
    public void Crear_con_nivel_negativo_falla()
    {
        var act = () => new ConfiguracionReorden(
            Guid.NewGuid(), Guid.NewGuid(), NivelReorden.Sucursal, Guid.NewGuid(),
            minimo: -1m, maximo: 5m, puntoReorden: 2m,
            autoRequisicion: false, objetivo: ObjetivoReposicion.Minimo);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("REORDEN_NIVELES_NEGATIVOS");
    }

    [Fact]
    public void EditarPolitica_aplica_nuevos_valores_sin_tocar_llave()
    {
        var art = Guid.NewGuid();
        var ent = Guid.NewGuid();
        var c = new ConfiguracionReorden(
            Guid.NewGuid(), art, NivelReorden.Almacen, ent,
            5m, 20m, 10m, true, ObjetivoReposicion.Maximo);

        c.EditarPolitica(minimo: 8m, maximo: 40m, puntoReorden: 12m,
            autoRequisicion: false, objetivo: ObjetivoReposicion.Reorden);

        c.Minimo.Should().Be(8m);
        c.Maximo.Should().Be(40m);
        c.PuntoReorden.Should().Be(12m);
        c.AutoRequisicion.Should().BeFalse();
        c.Objetivo.Should().Be(ObjetivoReposicion.Reorden);
        // Llave inmutable.
        c.ArticuloId.Should().Be(art);
        c.Nivel.Should().Be(NivelReorden.Almacen);
        c.EntidadId.Should().Be(ent);
    }

    [Fact]
    public void Cambiar_estatus_actualiza_el_valor()
    {
        var c = new ConfiguracionReorden(
            Guid.NewGuid(), Guid.NewGuid(), NivelReorden.Sucursal, Guid.NewGuid(),
            5m, 20m, 10m, true, ObjetivoReposicion.Maximo);

        c.CambiarEstatus(EstatusCatalogo.Inactivo);

        c.Estatus.Should().Be(EstatusCatalogo.Inactivo);
    }

    [Theory]
    [InlineData(ObjetivoReposicion.Minimo, 5)]
    [InlineData(ObjetivoReposicion.Maximo, 100)]
    [InlineData(ObjetivoReposicion.Reorden, 20)]
    public void ResolverObjetivo_mapea_el_enum_al_campo_correcto(ObjetivoReposicion objetivo, int esperado)
    {
        var c = new ConfiguracionReorden(
            Guid.NewGuid(), Guid.NewGuid(), NivelReorden.Almacen, Guid.NewGuid(),
            minimo: 5m, maximo: 100m, puntoReorden: 20m,
            autoRequisicion: true, objetivo: objetivo);

        c.ResolverObjetivo().Should().Be(esperado);
    }
}
