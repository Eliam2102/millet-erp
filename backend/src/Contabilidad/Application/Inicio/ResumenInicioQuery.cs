using MediatR;
using Millet.Contabilidad.Application.Periodos;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Inicio;

namespace Millet.Contabilidad.Application.Inicio;

// El periodo pertenece a la empresa completa: no hay un cierre contable por sucursal (F1-CON-03).
public sealed record ResumenInicioQuery : IRequest<ResumenInicioResponse>;
public sealed class ResumenInicioHandler(IMediator mediator, IClock clock)
    : IRequestHandler<ResumenInicioQuery, ResumenInicioResponse>
{
    public async Task<ResumenInicioResponse> Handle(ResumenInicioQuery request, CancellationToken cancellationToken)
    {
        var ejercicios = await mediator.Send(new ListarEjerciciosQuery(), cancellationToken);
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(clock.UtcNow, "America/Merida").DateTime);
        var periodo = ejercicios.SelectMany(e => e.Periodos)
            .FirstOrDefault(p => p.Estado == EstadoPeriodo.Abierto && !p.EsAjuste && p.FechaInicio <= hoy && p.FechaFin >= hoy);
        if (periodo is null) return new(0, 0, "No existe un periodo abierto para la fecha actual", ValorTexto: "Sin periodo abierto");
        var dias = periodo.FechaFin.DayNumber - hoy.DayNumber;
        return new(dias, 1, $"{dias} días para el cierre · empresa completa", dias <= 3 ? 1 : 0,
            $"{periodo.Nombre} {periodo.Anio}");
    }
}
