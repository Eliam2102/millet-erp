using FluentValidation;
using MediatR;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Periodos;

/// <summary>
/// Candado de periodo para los movimientos (F1-CON-03, D8/D9). Falla cerrada: un periodo inexistente, no abierto o cerrado no
/// admite movimientos; el 13 de ajustes solo admite movimientos manuales (D3). El rechazo es una <see cref="BusinessRuleException"/>
/// (422) con mensaje para el usuario; <see cref="RechazoAsync"/> lo devuelve sin lanzar para mostrarlo como error de validación.
/// </summary>
// PLATFORM-TODO(<Polizas>): las pólizas manuales y automáticas (C1.3–C1.5) verifican con este mismo componente.
public sealed class VerificadorPeriodoContable(IPeriodoContableConsultaPort periodos)
{
    public async Task<BusinessRuleException?> RechazoAsync(DateOnly fecha, OrigenMovimiento origen, CancellationToken ct) =>
        Rechazo(await periodos.ConsultarPorFechaAsync(fecha, ct), origen);

    public async Task LanzarSiNoAdmiteAsync(DateOnly fecha, OrigenMovimiento origen, CancellationToken ct)
    {
        if (await RechazoAsync(fecha, origen, ct) is { } rechazo) throw rechazo;
    }

    /// <summary>Por número: la única forma de dirigirse al periodo 13.</summary>
    public async Task LanzarSiNoAdmiteAsync(int anio, int numero, OrigenMovimiento origen, CancellationToken ct)
    {
        if (Rechazo(await periodos.ConsultarAsync(anio, numero, ct), origen) is { } rechazo) throw rechazo;
    }

    public static BusinessRuleException? Rechazo(EstadoPeriodoContable p, OrigenMovimiento origen)
    {
        var clave = PeriodoContable.Etiqueta(p.Anio, p.Numero);
        if (!p.Existe)
            return new("CONTAB_PERIODO_INEXISTENTE",
                $"No existe el periodo {clave}: cree el ejercicio {p.Anio} y abra el periodo antes de registrar movimientos con esa fecha.");
        if (p.Estado == EstadoPeriodo.Cerrado)
            return new("CONTAB_PERIODO_CERRADO", $"El periodo {clave} está cerrado; no se pueden registrar movimientos con esa fecha.");
        if (p.Estado != EstadoPeriodo.Abierto)
            return new("CONTAB_PERIODO_NO_ABIERTO", $"El periodo {clave} no se ha abierto; no se pueden registrar movimientos con esa fecha.");
        if (!PeriodoContable.NumeroAdmiteOrigen(p.Numero, origen))
            return new("CONTAB_PERIODO_13_SOLO_MANUAL", $"El periodo {clave} de ajustes de auditoría solo admite movimientos manuales.");
        return null;
    }
}

/// <summary>Mismo contrato que el puerto, por HTTP para la UI: por fecha (1–12) o por año y número (1–13).</summary>
public sealed record ConsultarEstadoPeriodoQuery(DateOnly? Fecha, int? Anio, int? Numero) : IRequest<EstadoPeriodoContable>;

public sealed class ConsultarEstadoPeriodoValidator : AbstractValidator<ConsultarEstadoPeriodoQuery>
{
    public ConsultarEstadoPeriodoValidator()
    {
        RuleFor(q => q).Must(q => q.Fecha is not null
                ? q.Anio is null && q.Numero is null
                : q.Anio is not null && q.Numero is not null)
            .WithName("consulta").WithMessage("Indique la fecha o el año y el número de periodo (no ambos).");
        RuleFor(q => q.Numero).InclusiveBetween(1, PeriodoContable.NumeroAjuste).When(q => q.Numero is not null);
        RuleFor(q => q.Anio).InclusiveBetween(EjercicioContable.AnioMinimo, EjercicioContable.AnioMaximo).When(q => q.Anio is not null);
    }
}

public sealed class ConsultarEstadoPeriodoHandler(IPeriodoContableConsultaPort periodos) : IRequestHandler<ConsultarEstadoPeriodoQuery, EstadoPeriodoContable>
{
    public Task<EstadoPeriodoContable> Handle(ConsultarEstadoPeriodoQuery request, CancellationToken cancellationToken) =>
        request.Fecha is { } f
            ? periodos.ConsultarPorFechaAsync(f, cancellationToken)
            : periodos.ConsultarAsync(request.Anio!.Value, request.Numero!.Value, cancellationToken);
}
