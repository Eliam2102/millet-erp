using Millet.Almacen.Application.Reorden;
using Millet.Almacen.Domain.Catalogo;
using Millet.Catalogos.Domain;

namespace Millet.Almacen.UnitTests.Catalogo;

/// <summary>
/// Tope de cantidades de la configuración de reorden (columnas numeric(14,4)).
/// Un valor de 1e12 debe fallar la validación (400) y no desbordar la columna (500).
/// </summary>
public class ConfiguracionReordenValidatorTests
{
    private const decimal Tope = 9_999_999_999.9999m;
    private const decimal Excedido = 1_000_000_000_000m;

    private static CrearConfiguracionReordenCommand Crear(
        decimal minimo = 1, decimal maximo = 10, decimal puntoReorden = 2, decimal? cantidadFija = null) =>
        new(Guid.NewGuid(), NivelReorden.Almacen, Guid.NewGuid(),
            minimo, maximo, puntoReorden, false, ObjetivoReposicion.Maximo, cantidadFija);

    private static EditarConfiguracionReordenCommand Editar(
        decimal minimo = 1, decimal maximo = 10, decimal puntoReorden = 2, decimal? cantidadFija = null) =>
        new(Guid.NewGuid(), minimo, maximo, puntoReorden, false, ObjetivoReposicion.Maximo, cantidadFija);

    [Fact]
    public void Crear_minimo_en_tope_pasa_y_excedido_falla()
    {
        var v = new CrearConfiguracionReordenValidator();
        v.Validate(Crear(minimo: Tope, maximo: Tope)).IsValid.Should().BeTrue();

        var r = v.Validate(Crear(minimo: Excedido, maximo: Excedido));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == "Minimo");
    }

    [Fact]
    public void Crear_maximo_en_tope_pasa_y_excedido_falla()
    {
        var v = new CrearConfiguracionReordenValidator();
        v.Validate(Crear(maximo: Tope)).IsValid.Should().BeTrue();

        var r = v.Validate(Crear(maximo: Excedido));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == "Maximo");
    }

    [Fact]
    public void Crear_punto_reorden_en_tope_pasa_y_excedido_falla()
    {
        var v = new CrearConfiguracionReordenValidator();
        v.Validate(Crear(puntoReorden: Tope)).IsValid.Should().BeTrue();

        var r = v.Validate(Crear(puntoReorden: Excedido));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == "PuntoReorden");
    }

    [Fact]
    public void Crear_cantidad_fija_en_tope_pasa_y_excedido_falla()
    {
        var v = new CrearConfiguracionReordenValidator();
        v.Validate(Crear(cantidadFija: Tope)).IsValid.Should().BeTrue();

        var r = v.Validate(Crear(cantidadFija: Excedido));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == "CantidadFija");
    }

    [Fact]
    public void Editar_minimo_en_tope_pasa_y_excedido_falla()
    {
        var v = new EditarConfiguracionReordenValidator();
        v.Validate(Editar(minimo: Tope, maximo: Tope)).IsValid.Should().BeTrue();

        var r = v.Validate(Editar(minimo: Excedido, maximo: Excedido));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == "Minimo");
    }

    [Fact]
    public void Editar_maximo_en_tope_pasa_y_excedido_falla()
    {
        var v = new EditarConfiguracionReordenValidator();
        v.Validate(Editar(maximo: Tope)).IsValid.Should().BeTrue();

        var r = v.Validate(Editar(maximo: Excedido));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == "Maximo");
    }

    [Fact]
    public void Editar_punto_reorden_en_tope_pasa_y_excedido_falla()
    {
        var v = new EditarConfiguracionReordenValidator();
        v.Validate(Editar(puntoReorden: Tope)).IsValid.Should().BeTrue();

        var r = v.Validate(Editar(puntoReorden: Excedido));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == "PuntoReorden");
    }

    [Fact]
    public void Editar_cantidad_fija_en_tope_pasa_y_excedido_falla()
    {
        var v = new EditarConfiguracionReordenValidator();
        v.Validate(Editar(cantidadFija: Tope)).IsValid.Should().BeTrue();

        var r = v.Validate(Editar(cantidadFija: Excedido));
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.PropertyName == "CantidadFija");
    }
}
