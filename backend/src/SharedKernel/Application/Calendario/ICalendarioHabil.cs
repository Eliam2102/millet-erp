namespace Millet.SharedKernel.Application.Calendario;

public interface ICalendarioHabil
{
    Task<DateTimeOffset> SumarHorasAsync(DateOnly fecha, int horas, CancellationToken ct);
}
