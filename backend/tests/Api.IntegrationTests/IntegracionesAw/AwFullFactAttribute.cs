namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>
/// <c>[Fact]</c> opt-in de las corridas contra la copia local <c>AW_FULL</c>: sin <c>AW_FULL_CONN</c> y
/// <c>AW_FULL_SALIDA</c> xUnit la reporta como OMITIDA (skip visible), no como verde.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AwFullFactAttribute : FactAttribute
{
    public AwFullFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AW_FULL_CONN"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AW_FULL_SALIDA")))
            Skip = "OMITIDO: falta AW_FULL_CONN/AW_FULL_SALIDA (corrida opt-in contra AW_FULL).";
    }
}
