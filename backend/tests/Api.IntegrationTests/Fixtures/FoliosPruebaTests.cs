using System.Collections.Concurrent;
using FluentAssertions;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Xunit;

namespace Millet.Api.IntegrationTests.Fixtures;

/// <summary>M-452 (86e3zr7c5): los folios sembrados por las pruebas no se repiten dentro del proceso.</summary>
public sealed class FoliosPruebaTests
{
    [Fact]
    public void Llamadas_concurrentes_reciben_folios_distintos_y_validos_para_la_nota_de_cargo()
    {
        var folios = new ConcurrentBag<int>();

        Parallel.For(0, 2_000, _ => folios.Add(FoliosPrueba.Siguiente()));

        folios.Should().OnlyHaveUniqueItems();
        folios.Should().OnlyContain(n => n > 500_000 && n <= 999_999);
        // Cada uno arma un folio NCG válido (el formato no se relajó).
        var muestra = folios.First();
        FolioInternoNotaCargo.FromAnioSecuencial(2026, muestra).Valor.Should().Be($"NCG-2026-{muestra:D6}");
    }
}
