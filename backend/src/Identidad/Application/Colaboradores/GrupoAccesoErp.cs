using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;

namespace Millet.Identidad.Application.Colaboradores;

/// <summary>
/// Estado de la cuenta frente al grupo de acceso del ERP en Entra
/// (86e3x6cpw, B2; contrato 4 de la pestaña Acceso).
/// </summary>
public static class EstadoGrupoAcceso
{
    public const string Sincronizado = "Sincronizado";
    public const string Pendiente = "Pendiente";
    public const string NoConfigurado = "NoConfigurado";
}

/// <summary>
/// Mantiene la cuenta Microsoft del usuario dentro (activo) o fuera
/// (inactivo) del grupo «ERP Millet · Usuarios» configurado en
/// <c>Entra:GrupoAccesoErpId</c> (86e3x6cpw, B2).
///
/// <para>
/// Nunca rompe la operación principal: un fallo de Graph se registra y la
/// cuenta queda «Pendiente» (lo calcula <see cref="ConsultarAsync"/>
/// comparando la membresía real con <c>Usuario.Activo</c>); volver a
/// activar o desactivar el acceso la sincroniza de nuevo.
/// </para>
/// </summary>
public sealed class GrupoAccesoErpSincronizador
{
    private readonly IdentidadDbContext _identidad;
    private readonly IEntraDirectorioPort _directorio;
    private readonly IOptionsMonitor<EntraDirectorioOptions> _options;
    private readonly ILogger<GrupoAccesoErpSincronizador> _logger;

    public GrupoAccesoErpSincronizador(
        IdentidadDbContext identidad,
        IEntraDirectorioPort directorio,
        IOptionsMonitor<EntraDirectorioOptions> options,
        ILogger<GrupoAccesoErpSincronizador> logger)
    {
        _identidad = identidad;
        _directorio = directorio;
        _options = options;
        _logger = logger;
    }

    /// <summary>Agrega o quita la cuenta del grupo según <c>Usuario.Activo</c>.</summary>
    public async Task<string> SincronizarAsync(Guid usuarioId, CancellationToken ct)
    {
        var grupoId = GrupoConfigurado();
        if (grupoId is null) return EstadoGrupoAcceso.NoConfigurado;

        var usuario = await CargarAsync(usuarioId, ct);
        if (usuario is null || usuario.EsCuentaTecnica) return EstadoGrupoAcceso.NoConfigurado;
        // Cuenta nueva aún sin crear: el worker la agrega al vincular el OID.
        if (usuario.TieneOidPendiente) return EstadoGrupoAcceso.Pendiente;

        try
        {
            if (usuario.Activo)
                await _directorio.AgregarAGrupoAccesoAsync(grupoId, usuario.EntraOid, ct);
            else
                await _directorio.QuitarDeGrupoAccesoAsync(grupoId, usuario.EntraOid, ct);
            return EstadoGrupoAcceso.Sincronizado;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[GrupoAccesoErp] No se pudo {Accion} la cuenta del usuario {UsuarioId} en el grupo de acceso; queda pendiente.",
                usuario.Activo ? "agregar" : "quitar", usuario.Id);
            return EstadoGrupoAcceso.Pendiente;
        }
    }

    /// <summary>Compara la membresía real con lo que corresponde al usuario.</summary>
    public async Task<string> ConsultarAsync(Guid usuarioId, CancellationToken ct)
    {
        var grupoId = GrupoConfigurado();
        if (grupoId is null) return EstadoGrupoAcceso.NoConfigurado;

        var usuario = await CargarAsync(usuarioId, ct);
        if (usuario is null || usuario.EsCuentaTecnica) return EstadoGrupoAcceso.NoConfigurado;
        if (usuario.TieneOidPendiente) return EstadoGrupoAcceso.Pendiente;

        try
        {
            var esMiembro = await _directorio.EsMiembroDeGrupoAccesoAsync(grupoId, usuario.EntraOid, ct);
            return esMiembro == usuario.Activo ? EstadoGrupoAcceso.Sincronizado : EstadoGrupoAcceso.Pendiente;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[GrupoAccesoErp] No se pudo consultar la membresía del usuario {UsuarioId}.", usuario.Id);
            return EstadoGrupoAcceso.Pendiente;
        }
    }

    private string? GrupoConfigurado()
    {
        var grupoId = _options.CurrentValue.GrupoAccesoErpId?.Trim();
        if (!string.IsNullOrEmpty(grupoId)) return grupoId;
        // PLATFORM-TODO(<GrupoAccesoErp>): TI crea el grupo «ERP Millet · Usuarios»
        // asignado a la aplicación y entrega su Object ID para Entra:GrupoAccesoErpId.
        _logger.LogInformation("[GrupoAccesoErp] Entra:GrupoAccesoErpId vacío; el ERP no gestiona el grupo de acceso.");
        return null;
    }

    private Task<Usuario?> CargarAsync(Guid usuarioId, CancellationToken ct) =>
        _identidad.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
}
