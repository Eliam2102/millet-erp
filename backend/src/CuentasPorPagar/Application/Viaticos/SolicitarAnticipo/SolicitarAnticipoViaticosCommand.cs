using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Viaticos.SolicitarAnticipo;

/// <summary>
/// Captura una <see cref="Domain.Viaticos.SolicitudViaticos"/> en estado
/// <c>Solicitada</c> (§7.4.2 paso 1, F7-PR3). El handler:
///
/// <list type="number">
///   <item>Valida que el puesto exista vía <see cref="IPuestoReadPort"/>.</item>
///   <item>Busca <see cref="PoliticaViaticos"/> por
///   <c>(empresa, puesto, tipo_destino)</c>.</item>
///   <item>Calcula tope y marca si excede política (delegado al agregado).</item>
///   <item>Persiste con <c>FechaSolicitud = ahora</c>.</item>
/// </list>
///
/// <para>
/// Si no hay política definida para el puesto + destino, la solicitud
/// se rechaza con <c>VIA_POLITICA_NO_DEFINIDA</c> (bloqueante para
/// asegurar que RH/Dirección cierre el catálogo antes del go-live, §13.1
/// punto 3).
/// </para>
/// </summary>
public sealed record SolicitarAnticipoViaticosCommand(
    Guid EmpleadoId,
    Guid PuestoId,
    Guid JefeDirectoId,
    string Destino,
    TipoDestinoViatico TipoDestino,
    DateOnly FechaSalida,
    DateOnly FechaRegreso,
    string Moneda,
    decimal MontoSolicitado,
    string? JustificacionExceso) : IRequest<SolicitarAnticipoViaticosResponse>;

public sealed record SolicitarAnticipoViaticosResponse(
    Guid Id,
    EstadoSolicitudViaticos Estado,
    decimal TopePolitica,
    bool ExcedePolitica,
    int DiasEstimados,
    int Version);

public sealed class SolicitarAnticipoViaticosValidator
    : AbstractValidator<SolicitarAnticipoViaticosCommand>
{
    public SolicitarAnticipoViaticosValidator()
    {
        RuleFor(c => c.EmpleadoId).NotEmpty();
        RuleFor(c => c.PuestoId).NotEmpty();
        RuleFor(c => c.JefeDirectoId).NotEmpty();
        RuleFor(c => c.Destino).NotEmpty().MaximumLength(400);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.MontoSolicitado).GreaterThan(0);
    }
}

public sealed class SolicitarAnticipoViaticosHandler
    : IRequestHandler<SolicitarAnticipoViaticosCommand, SolicitarAnticipoViaticosResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IPuestoReadPort _puestos;
    private readonly IEmpleadoReadPort _empleados;
    private readonly IClock _clock;

    public SolicitarAnticipoViaticosHandler(
        CuentasPorPagarDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        IPuestoReadPort puestos,
        IEmpleadoReadPort empleados,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _puestos = puestos; _empleados = empleados; _clock = clock;
    }

    public async Task<SolicitarAnticipoViaticosResponse> Handle(
        SolicitarAnticipoViaticosCommand command,
        CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        // P7-H5: empleado y jefe deben existir ACTIVOS en el catálogo de
        // Administración — un GUID arbitrario producía solicitudes que
        // nadie podía firmar (el jefe se resuelve por Empleado.UsuarioId).
        var empleado = await _empleados.ObtenerAsync(command.EmpleadoId, cancellationToken);
        if (empleado is null || !empleado.Activo)
        {
            throw new EntityNotFoundException(
                "VIA_EMPLEADO_NO_EXISTE",
                $"El empleado '{command.EmpleadoId}' no existe o no está activo en el catálogo.");
        }

        var jefe = await _empleados.ObtenerAsync(command.JefeDirectoId, cancellationToken);
        if (jefe is null || !jefe.Activo)
        {
            throw new EntityNotFoundException(
                "VIA_JEFE_NO_EXISTE",
                $"El jefe directo '{command.JefeDirectoId}' no existe o no está activo en el catálogo.");
        }

        // El puesto de la solicitud decide la política — debe ser el del
        // empleado, no uno con tope más generoso.
        if (empleado.PuestoId is Guid puestoEmpleado && puestoEmpleado != command.PuestoId)
        {
            throw new BusinessRuleException(
                "VIA_PUESTO_NO_COINCIDE",
                "El puesto de la solicitud no coincide con el puesto del empleado en el catálogo.");
        }

        var puesto = await _puestos.ObtenerAsync(command.PuestoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "VIA_PUESTO_NO_EXISTE",
                $"No existe el puesto '{command.PuestoId}'.");

        var politica = await _db.PoliticasViaticos
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.PuestoId == command.PuestoId && p.TipoDestino == command.TipoDestino,
                cancellationToken)
            ?? throw new BusinessRuleException(
                "VIA_POLITICA_NO_DEFINIDA",
                $"No hay política de viáticos para puesto '{puesto.Nombre}' destino '{command.TipoDestino}'. " +
                "RH/Dirección debe cerrar el catálogo antes de aceptar la solicitud.");

        var dias = command.FechaRegreso.DayNumber - command.FechaSalida.DayNumber + 1;
        var tope = politica.CalcularTope(dias);

        var solicitud = Domain.Viaticos.SolicitudViaticos.Solicitar(
            empresaId: empresaId,
            empleadoId: command.EmpleadoId,
            puestoId: command.PuestoId,
            jefeDirectoId: command.JefeDirectoId,
            destino: command.Destino,
            tipoDestino: command.TipoDestino,
            fechaSalida: command.FechaSalida,
            fechaRegreso: command.FechaRegreso,
            moneda: command.Moneda,
            montoSolicitado: command.MontoSolicitado,
            topePolitica: tope,
            diasMaxPolitica: politica.DiasMax,
            justificacionExceso: command.JustificacionExceso,
            ahora: _clock.UtcNow);

        _db.SolicitudesViaticos.Add(solicitud);
        await _db.SaveChangesAsync(cancellationToken);

        return new SolicitarAnticipoViaticosResponse(
            Id: solicitud.Id,
            Estado: solicitud.Estado,
            TopePolitica: tope,
            ExcedePolitica: solicitud.ExcedePolitica,
            DiasEstimados: solicitud.DiasEstimados,
            Version: solicitud.Version);
    }
}
