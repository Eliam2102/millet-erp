namespace Millet.Api.IntegrationTests.Fixtures;

/// <summary>
/// M-452 (86e3zr7c5): consecutivo único por proceso para los folios que siembran las pruebas. Las pruebas de una
/// parte del gate comparten base, y un <c>Random.Shared.Next</c> repetido rompía restricciones únicas como
/// <c>ux_notas_cargo_folio</c> al azar (paradoja del cumpleaños). Arranca en una base alta con un desfase aleatorio
/// para no chocar con la secuencia real (<c>NCG-2026-000001…</c>) ni con corridas locales previas sobre la misma base;
/// dentro del proceso nunca se repite.
/// </summary>
public static class FoliosPrueba
{
    private const int Maximo = 999_999;
    private static int _ultimo = 500_000 + Random.Shared.Next(0, 100_000);

    /// <summary>Siguiente secuencial de 6 dígitos, distinto en cada llamada del proceso (seguro entre hilos).</summary>
    public static int Siguiente()
    {
        var valor = Interlocked.Increment(ref _ultimo);
        if (valor > Maximo)
            throw new InvalidOperationException("Se agotaron los folios de prueba de 6 dígitos en este proceso.");
        return valor;
    }
}
