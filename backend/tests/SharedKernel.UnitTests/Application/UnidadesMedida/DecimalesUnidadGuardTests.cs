using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.SharedKernel.UnitTests.Application.UnidadesMedida;

/// <summary>
/// Cubre el guard compartido <see cref="DecimalesUnidadGuard"/> (ADR-0046
/// Etapa 2): resuelve decimales por articuloId en un solo round-trip, lanza
/// <c>CANTIDAD_DECIMALES_EXCEDE_UNIDAD</c> en la primera línea inválida y
/// omite las líneas cuyo artículo no resuelve a una unidad (FK NULL).
/// </summary>
public class DecimalesUnidadGuardTests
{
    private static readonly Guid PiezaArt = Guid.Parse("00000000-0000-0000-0000-0000000000a0");
    private static readonly Guid KgArt = Guid.Parse("00000000-0000-0000-0000-0000000000b0");
    private static readonly Guid LegacyArt = Guid.Parse("00000000-0000-0000-0000-0000000000c0");

    /// <summary>Fake del puerto: mapa fijo articuloId→decimales (artículos no
    /// presentes en el mapa no aparecen en el resultado, como el adapter real).
    /// Cuenta llamadas para verificar el batch (un solo round-trip).</summary>
    private sealed class FakeUnidadMedidaReadPort(IReadOnlyDictionary<Guid, int?> mapa)
        : IUnidadMedidaReadPort
    {
        public int Llamadas { get; private set; }
        public IReadOnlyCollection<Guid>? UltimosIds { get; private set; }

        public Task<IReadOnlyDictionary<Guid, int?>> ObtenerDecimalesPorArticulosAsync(
            IEnumerable<Guid> articuloIds,
            CancellationToken cancellationToken)
        {
            Llamadas++;
            var ids = articuloIds.ToList();
            UltimosIds = ids;
            IReadOnlyDictionary<Guid, int?> resultado = ids
                .Where(mapa.ContainsKey)
                .ToDictionary(id => id, id => mapa[id]);
            return Task.FromResult(resultado);
        }
    }

    private static DecimalesUnidadGuard Guard(out FakeUnidadMedidaReadPort port, params (Guid Id, int? Decimales)[] mapa)
    {
        port = new FakeUnidadMedidaReadPort(mapa.ToDictionary(t => t.Id, t => t.Decimales));
        return new DecimalesUnidadGuard(port);
    }

    [Fact]
    public async Task Pieza_ConDecimal_Lanza_ConCodigoYDecimalesEnMensaje()
    {
        var guard = Guard(out _, (PiezaArt, 0));

        var act = () => guard.ValidarAsync(
            new[] { new CantidadAValidar(PiezaArt, 1.5m, "PZA") },
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BusinessRuleException>();
        ex.Which.Code.Should().Be("CANTIDAD_DECIMALES_EXCEDE_UNIDAD");
        ex.Which.Message.Should().Contain("0").And.Contain("PZA");
    }

    [Fact]
    public async Task Pieza_ConEntero_NoLanza()
    {
        var guard = Guard(out _, (PiezaArt, 0));

        await guard.Invoking(g => g.ValidarAsync(
                new[] { new CantidadAValidar(PiezaArt, 2m, "PZA") },
                CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task Kg_AceptaTresDecimales_RechazaCuatro()
    {
        var guard = Guard(out _, (KgArt, 3));

        await guard.Invoking(g => g.ValidarAsync(
                new[] { new CantidadAValidar(KgArt, 1.250m, "KG") }, CancellationToken.None))
            .Should().NotThrowAsync();

        await guard.Invoking(g => g.ValidarAsync(
                new[] { new CantidadAValidar(KgArt, 1.2505m, "KG") }, CancellationToken.None))
            .Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task FkNull_NoValida_Permite()
    {
        var guard = Guard(out _, (LegacyArt, (int?)null));

        await guard.Invoking(g => g.ValidarAsync(
                new[] { new CantidadAValidar(LegacyArt, 1.5m, "CAJA") }, CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task ArticuloNoResuelto_NoValida_Permite()
    {
        var guard = Guard(out _); // mapa vacío → el artículo no aparece en el resultado

        await guard.Invoking(g => g.ValidarAsync(
                new[] { new CantidadAValidar(PiezaArt, 1.5m, "PZA") }, CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task MultiLinea_UnSoloRoundTrip_ConIdsDistintos()
    {
        var guard = Guard(out var port, (PiezaArt, 0), (KgArt, 3));

        await guard.ValidarAsync(
            new[]
            {
                new CantidadAValidar(PiezaArt, 2m, "PZA"),
                new CantidadAValidar(KgArt, 1.250m, "KG"),
                new CantidadAValidar(KgArt, 0.5m, "KG"),
            },
            CancellationToken.None);

        port.Llamadas.Should().Be(1);
        port.UltimosIds!.Should().BeEquivalentTo(new[] { PiezaArt, KgArt });
    }

    [Fact]
    public async Task MultiLinea_LanzaEnLaPrimeraInvalida()
    {
        var guard = Guard(out _, (PiezaArt, 0), (KgArt, 3));

        var act = () => guard.ValidarAsync(
            new[]
            {
                new CantidadAValidar(KgArt, 1.250m, "KG"),    // válida
                new CantidadAValidar(PiezaArt, 1.5m, "PZA"),  // inválida
            },
            CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("CANTIDAD_DECIMALES_EXCEDE_UNIDAD");
    }

    [Fact]
    public async Task ListaVacia_NoLlamaAlPuerto()
    {
        var guard = Guard(out var port);

        await guard.ValidarAsync(Array.Empty<CantidadAValidar>(), CancellationToken.None);

        port.Llamadas.Should().Be(0);
    }
}
