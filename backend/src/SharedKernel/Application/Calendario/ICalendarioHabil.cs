namespace Millet.SharedKernel.Application.Calendario;

public interface ICalendarioHabil
{
    Task<int> ContarDiasAsync(DateOnly desde, DateOnly hasta, CancellationToken ct) => throw new NotSupportedException("Implementa el conteo de días hábiles del calendario compartido.");
    Task<DateTimeOffset> SumarHorasAsync(DateOnly fecha, int horas, CancellationToken ct);
}
