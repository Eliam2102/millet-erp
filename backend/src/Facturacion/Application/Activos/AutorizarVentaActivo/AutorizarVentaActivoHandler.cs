using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Activos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Activos.AutorizarVentaActivo;

/// <summary>
/// Orquesta la autorización de venta de activo fijo: valida que el activo esté
/// dado de alta (IActivosFijosReadPort) y registra la autorización con el snapshot
/// de valor en libros / depreciación para el asiento de baja.
/// </summary>
public sealed class AutorizarVentaActivoHandler : IRequestHandler<AutorizarVentaActivoCommand, AutorizarVentaActivoResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IActivosFijosReadPort _activos;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;

    public AutorizarVentaActivoHandler(
        FacturacionDbContext db, IActivosFijosReadPort activos, ICurrentEmpresaContext empresa, ICurrentUserContext user, IClock clock)
    {
        _db = db;
        _activos = activos;
        _empresa = empresa;
        _user = user;
        _clock = clock;
    }

    public async Task<AutorizarVentaActivoResponse> Handle(AutorizarVentaActivoCommand command, CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");
        if (_user.UserId is not Guid autorizadoPor)
            throw new ForbiddenException("USUARIO_NO_AUTENTICADO", "No hay usuario autenticado (Director de Administración y Finanzas) en el contexto.");

        await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
        var llave = $"AF-LIBRO-{empresaId}";
        if (tx != null) await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({llave}, 0))", cancellationToken);
        if (await _db.AutorizacionesVentaActivo.AnyAsync(x => x.ActivoRef == command.ActivoRef && x.Estado == EstadoAutorizacionActivo.Autorizada, cancellationToken))
            throw new BusinessRuleException("AF_VENTA_PENDIENTE", "Ya existe una autorización de venta abierta para este activo.");
        var activo = await _activos.ObtenerAsync(command.ActivoRef, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ACTIVO_NO_EXISTE",
                $"El activo '{command.ActivoRef}' no está dado de alta como activo fijo; no está disponible para venta.");

        if (activo.BajaPendiente)
            throw new BusinessRuleException("ACTIVO_BAJA_PENDIENTE", "Tiene una solicitud de baja pendiente");

        var autorizacion = AutorizacionVentaActivo.Crear(
            empresaId, activo.ActivoRef, activo.Descripcion, activo.ValorEnLibros + activo.DepreciacionAcumulada, activo.DepreciacionAcumulada,
            activo.EsImportacion, command.PrecioVenta, autorizadoPor, _clock.UtcNow, command.Motivo, command.SolicitadoPor, command.Evidencia);

        _db.AutorizacionesVentaActivo.Add(autorizacion);
        await _db.SaveChangesAsync(cancellationToken);

        if (tx != null) await tx.CommitAsync(cancellationToken);
        return new AutorizarVentaActivoResponse(
            autorizacion.Id, autorizacion.Descripcion, autorizacion.ValorNetoEnLibros, autorizacion.UtilidadOPerdida);
    }
}
