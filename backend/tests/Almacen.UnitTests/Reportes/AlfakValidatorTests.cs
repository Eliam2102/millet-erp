using System;
using Millet.Almacen.Application.Reportes;
using Xunit;

namespace Millet.Almacen.UnitTests.Reportes;

/// <summary>
/// Validador de la consulta ALFAK: el rango de fechas debe ser válido
/// (<c>Desde &lt;= Hasta</c>). Igual pasa; desde posterior a hasta falla.
/// </summary>
public class AlfakValidatorTests
{
    private static readonly AlfakHistorialAlmacenQueryValidator Validador = new();

    [Fact]
    public void DesdeMayorQueHasta_FallaValidacion()
    {
        var query = new AlfakHistorialAlmacenQuery(
            new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 1), null, null);

        var resultado = Validador.Validate(query);

        Assert.False(resultado.IsValid);
        Assert.Contains(resultado.Errors, e =>
            e.PropertyName == "Desde"
            && e.ErrorMessage == "La fecha inicial no puede ser posterior a la final.");
    }

    [Fact]
    public void DesdeIgualAHasta_EsValido()
    {
        var dia = new DateOnly(2026, 10, 14);
        var query = new AlfakHistorialAlmacenQuery(dia, dia, null, null);

        Assert.True(Validador.Validate(query).IsValid);
    }
}
