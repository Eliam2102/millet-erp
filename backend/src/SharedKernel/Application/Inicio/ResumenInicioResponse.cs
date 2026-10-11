namespace Millet.SharedKernel.Application.Inicio;

/// <summary>Contrato de lectura del Inicio. Sin identificadores ni datos bancarios personales.</summary>
public sealed record EtapaInicio(string Etiqueta, int Total);
public sealed record DiaFlujoInicio(string Dia, decimal Cobros, decimal Pagos);
public sealed record ResumenInicioResponse(decimal Valor, int Total, string Contexto, int Urgentes = 0,
    string? ValorTexto = null, IReadOnlyList<EtapaInicio>? Etapas = null,
    IReadOnlyList<DiaFlujoInicio>? Serie = null);

/// <summary>Selección del topbar; el pipeline valida asociación antes de consultar el módulo.</summary>
public interface ISucursalSeleccionadaQuery : ISucursalScopedQuery
{
    Guid? SucursalId { get; }
}

public enum PeriodoInicio { Hoy, Semana, Mes }
public static class FechasInicio
{
    public static DateOnly Hoy(IClock clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(clock.UtcNow, "America/Merida").DateTime);
    public static DateOnly Desde(PeriodoInicio periodo, DateOnly hoy) => periodo switch
    {
        PeriodoInicio.Hoy => hoy,
        PeriodoInicio.Semana => hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7)),
        _ => new(hoy.Year, hoy.Month, 1),
    };
}
