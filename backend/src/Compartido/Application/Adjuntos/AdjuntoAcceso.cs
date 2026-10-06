using System.Text.Json;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>
/// Autorización y auditoría comunes de las operaciones de adjuntos (contrato adm-11 §3).
/// Orden: tipo de entidad conocido -> permiso de rol -> existencia del padre -> alcance -> operación.
/// Las denegaciones se auditan (F1-ADM-03); las altas/bajas las cubre el interceptor de auditoría.
/// </summary>
public sealed class AdjuntoAcceso
{
    private readonly Dictionary<string, IAdjuntoPropietario> _propietarios;
    private readonly ICurrentUserPermissions _permisos;
    private readonly ICurrentUserContext _usuario;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IAuditLogWriter _audit;

    public AdjuntoAcceso(
        IEnumerable<IAdjuntoPropietario> propietarios,
        ICurrentUserPermissions permisos,
        ICurrentUserContext usuario,
        ICurrentEmpresaContext empresa,
        IAuditLogWriter audit)
    {
        _propietarios = propietarios.ToDictionary(p => p.TipoEntidad, StringComparer.Ordinal);
        _permisos = permisos;
        _usuario = usuario;
        _empresa = empresa;
        _audit = audit;
    }

    /// <summary>Usuario autenticado o 401.</summary>
    public Guid UsuarioId => _usuario.UserId
        ?? throw new UnauthorizedAccessException("Sin usuario autenticado.");

    public IAdjuntoPropietario ObtenerPropietario(string tipoEntidad)
        => _propietarios.TryGetValue(tipoEntidad ?? string.Empty, out var p)
            ? p
            : throw new EntityNotFoundException(
                "ADJUNTO_TIPO_ENTIDAD_DESCONOCIDO",
                $"El tipo de entidad '{tipoEntidad}' no admite adjuntos.");

    /// <summary>Solo la capa de permiso de rol (para operaciones sin padre, p. ej. listar tipos).</summary>
    public async Task<IAdjuntoPropietario> AutorizarPermisoAsync(
        string tipoEntidad, AdjuntoOperacion operacion, CancellationToken ct)
    {
        var propietario = ObtenerPropietario(tipoEntidad);
        var permiso = PermisoDe(propietario, operacion);
        if (!await _permisos.TieneAsync(permiso, ct))
        {
            await AuditarAsync("denegacion", propietario.TipoEntidad, null, null,
                $"Acceso denegado a adjuntos ({operacion}): falta el permiso {permiso}", ct: ct);
            throw new ForbiddenException(
                "ADJUNTO_PERMISO_DENEGADO", "No tienes permiso para esta operación sobre adjuntos.");
        }
        return propietario;
    }

    /// <summary>Cadena completa: permiso, existencia del padre, alcance y operación admitida.</summary>
    public async Task<(IAdjuntoPropietario Propietario, AdjuntoPropietarioInfo Padre)> AutorizarAsync(
        string tipoEntidad, Guid entidadId, AdjuntoOperacion operacion, CancellationToken ct)
    {
        var propietario = await AutorizarPermisoAsync(tipoEntidad, operacion, ct);

        var info = await propietario.ResolverAsync(entidadId, ct)
            ?? throw new EntityNotFoundException(
                propietario.CodigoNoEncontrado,
                $"No se encontró la entidad '{entidadId}' ({propietario.TipoEntidad}).");

        try
        {
            await propietario.VerificarAlcanceAsync(info, ct);
        }
        catch (ForbiddenException ex)
        {
            await AuditarAsync("denegacion", propietario.TipoEntidad, entidadId, null,
                $"Acceso denegado a adjuntos de {info.Etiqueta}: {ex.Code}", info, ct: ct);
            throw;
        }

        if (operacion == AdjuntoOperacion.Subir && !info.PuedeSubir)
        {
            throw new BusinessRuleException(
                "ADJUNTO_ENTIDAD_NO_ADMITE_SUBIDA",
                "La entidad no admite nuevos adjuntos en su estado actual.");
        }

        return (propietario, info);
    }

    /// <summary>Registra un evento de auditoría explícito (descarga, enlace, denegación).</summary>
    public Task AuditarAsync(
        string operacion,
        string tipoEntidad,
        Guid? entidadId,
        Guid? adjuntoId,
        string resumen,
        AdjuntoPropietarioInfo? padre = null,
        Guid? usuarioId = null,
        string? actorNombre = null,
        CancellationToken ct = default)
        => _audit.RegistrarAsync(
            operacion: operacion,
            modulo: "Compartido",
            entidad: "Adjunto",
            entidadId: adjuntoId,
            aggregateRootId: entidadId,
            actorNombre: actorNombre ?? _usuario.UserName ?? "Usuario",
            actorTipo: "usuario",
            actorEmail: _usuario.Email,
            entidadEtiqueta: padre?.Etiqueta ?? tipoEntidad,
            resumen: resumen,
            usuarioId: usuarioId ?? _usuario.UserId,
            empresaId: _empresa.Current,
            cambios: "{}",
            metadatos: JsonSerializer.Serialize(new { tipoEntidad }),
            cancellationToken: ct);

    private static string PermisoDe(IAdjuntoPropietario p, AdjuntoOperacion op) => op switch
    {
        AdjuntoOperacion.Ver => p.PermisoVer,
        AdjuntoOperacion.Subir => p.PermisoSubir,
        _ => p.PermisoBaja,
    };
}
