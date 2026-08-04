using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Settings;

// ============================================================================
// Settings del módulo Almacén (interruptor de reabasto automático).
// Molde 1:1 de Compras/Application/Settings (ADR-0032): lectura por la
// empresa del JWT con default-sin-persistir; upsert por (empresa_id) solo
// al escribir. El ReordenWorker NO usa estos handlers (lee el flag por la
// empresa del usuario de servicio, con bypass, en su propio ciclo).
// ============================================================================

/// <summary>
/// DTO de respuesta de los settings del módulo Almacén de una empresa.
/// Campos estables al wire (no se expone Id ni timestamps internos).
/// </summary>
/// <param name="EmpresaId">Empresa propietaria de la configuración.</param>
/// <param name="ReabastoAutomaticoActivo">
/// Interruptor operativo del motor de reorden. <c>false</c> (default) = el
/// worker no genera borradores. La config de infraestructura
/// <c>ReordenWorker:Disabled</c> tiene precedencia dura sobre este flag.
/// </param>
public sealed record AlmacenSettingsResponse(
    Guid EmpresaId,
    bool ReabastoAutomaticoActivo);

/// <summary>
/// Query que devuelve los settings del módulo Almacén para la empresa
/// actual (del JWT). Si la fila no existe en BD, retorna la versión
/// default (<c>ReabastoAutomaticoActivo=false</c>) sin persistirla — el
/// upsert solo ocurre al cambiar el valor explícitamente.
/// </summary>
public sealed record ObtenerAlmacenSettingsQuery : IRequest<AlmacenSettingsResponse>;

public sealed class ObtenerAlmacenSettingsHandler
    : IRequestHandler<ObtenerAlmacenSettingsQuery, AlmacenSettingsResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public ObtenerAlmacenSettingsHandler(
        AlmacenDbContext db,
        ICurrentEmpresaContext currentEmpresa)
    {
        _db = db;
        _currentEmpresa = currentEmpresa;
    }

    public async Task<AlmacenSettingsResponse> Handle(
        ObtenerAlmacenSettingsQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var row = await _db.AlmacenSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.EmpresaId == empresaId, cancellationToken);

        return new AlmacenSettingsResponse(
            EmpresaId: empresaId,
            ReabastoAutomaticoActivo: row?.ReabastoAutomaticoActivo ?? false);
    }
}

/// <summary>
/// Command para actualizar (upsert) los settings del módulo Almacén de la
/// empresa actual. Permiso en el endpoint: <c>almacen.reorden.administrar</c>.
///
/// <para>PATCH parcial: solo los campos no-null se aplican.</para>
/// </summary>
/// <param name="ReabastoAutomaticoActivo">
/// Si se provee, sobrescribe el interruptor. <c>null</c> = no tocar.
/// </param>
public sealed record ActualizarAlmacenSettingsCommand(
    bool? ReabastoAutomaticoActivo) : IRequest<AlmacenSettingsResponse>;

/// <summary>
/// Upsert por (empresa_id): si la fila no existe se crea con
/// <see cref="AlmacenSettings.CrearDefault"/> y luego se aplica el patch;
/// si existe se actualizan solo los campos no-null. El cambio queda en el
/// audit_log central (IAuditable, ADR-0008).
/// </summary>
public sealed class ActualizarAlmacenSettingsHandler
    : IRequestHandler<ActualizarAlmacenSettingsCommand, AlmacenSettingsResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;

    public ActualizarAlmacenSettingsHandler(
        AlmacenDbContext db,
        ICurrentEmpresaContext currentEmpresa)
    {
        _db = db;
        _currentEmpresa = currentEmpresa;
    }

    public async Task<AlmacenSettingsResponse> Handle(
        ActualizarAlmacenSettingsCommand command,
        CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var row = await _db.AlmacenSettings
            .FirstOrDefaultAsync(s => s.EmpresaId == empresaId, cancellationToken);

        if (row is null)
        {
            row = AlmacenSettings.CrearDefault(empresaId);
            _db.AlmacenSettings.Add(row);
        }

        if (command.ReabastoAutomaticoActivo is bool flag)
        {
            row.ActualizarReabastoAutomatico(flag);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new AlmacenSettingsResponse(
            EmpresaId: row.EmpresaId,
            ReabastoAutomaticoActivo: row.ReabastoAutomaticoActivo);
    }
}
