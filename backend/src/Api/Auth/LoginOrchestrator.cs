using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth.Models;
using Millet.Identidad.Application;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Api.Auth;

/// <summary>
/// Orquesta el flujo completo de login: validación → lookup/auto-provisión
/// → selección de empresa → emisión de JWT. Comparte la lógica entre el
/// camino real (Entra ID) y el camino de dev (fake oid).
///
/// Selección de empresa (orden de precedencia):
/// <list type="number">
///   <item>Empresa solicitada por el cliente, si tiene asignación.</item>
///   <item><c>UsuarioPreferencia.UltimaEmpresaId</c>, si aún tiene asignación.</item>
///   <item>Auto-seleccionar si el usuario tiene exactamente 1 empresa.</item>
///   <item>Ninguna — JWT con <c>current_empresa_id</c> null. UI muestra selector.</item>
/// </list>
///
/// Auto-provisión: si el <c>EntraOid</c> no existe en BD, crea
/// <c>Usuario</c> + <c>UsuarioPreferencia</c> con datos del token. El usuario
/// queda sin asignaciones; admin las gestiona después (o el bootstrap del
/// SuperAdmin en PR 5).
/// </summary>
public sealed class LoginOrchestrator
{
    private readonly IdentidadDbContext _db;
    private readonly IEntraTokenValidator _entraValidator;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICurrentEmpresaContext _empresaContext;
    private readonly IPermissionCache _permissionCache;
    private readonly IPermissionLoader _permissionLoader;
    private readonly Millet.Compras.Infrastructure.ComprasDbContext _comprasDb;

    public LoginOrchestrator(
        IdentidadDbContext db,
        IEntraTokenValidator entraValidator,
        IJwtTokenService jwtTokenService,
        ICurrentEmpresaContext empresaContext,
        IPermissionCache permissionCache,
        IPermissionLoader permissionLoader,
        Millet.Compras.Infrastructure.ComprasDbContext comprasDb)
    {
        _db = db;
        _entraValidator = entraValidator;
        _jwtTokenService = jwtTokenService;
        _empresaContext = empresaContext;
        _permissionCache = permissionCache;
        _permissionLoader = permissionLoader;
        _comprasDb = comprasDb;
    }

    /// <summary>
    /// Camino real: valida un token de Entra y emite el JWT del API.
    /// </summary>
    public async Task<LoginResponse> LoginWithEntraTokenAsync(
        string entraToken,
        Guid? requestedEmpresaId,
        CancellationToken cancellationToken)
    {
        var entraClaims = await _entraValidator.ValidateAsync(entraToken, cancellationToken);

        return await CompleteLoginAsync(
            entraClaims.Oid,
            entraClaims.Email,
            entraClaims.Name,
            requestedEmpresaId,
            cancellationToken);
    }

    /// <summary>
    /// Camino de dev (#if DEBUG en el endpoint): trata el oid como ya
    /// validado y emite el JWT del API. Solo accesible cuando
    /// <c>Auth:Mode=FakeForLocalDev</c> y compilación Debug.
    /// </summary>
    public Task<LoginResponse> LoginWithFakeOidAsync(
        string entraOid,
        string email,
        string nombre,
        Guid? requestedEmpresaId,
        CancellationToken cancellationToken)
    {
        return CompleteLoginAsync(entraOid, email, nombre, requestedEmpresaId, cancellationToken);
    }

    /// <summary>
    /// Cambio de empresa post-login: el usuario ya autenticado solicita
    /// re-emitir su JWT con un <c>current_empresa_id</c> distinto. A
    /// diferencia del flujo de login, NO hay fallback — si el usuario no
    /// tiene asignación a la empresa solicitada, se lanza
    /// <see cref="ForbiddenException"/> (HTTP 403). También actualiza
    /// <c>UsuarioPreferencia.UltimaEmpresaId</c> para que el próximo login
    /// arranque ahí.
    /// </summary>
    public async Task<LoginResponse> ChangeEmpresaAsync(
        Guid userId,
        Guid empresaId,
        CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        var usuario = await _db.Usuarios
            .FirstOrDefaultAsync(u => u.Id == userId && u.Activo, cancellationToken)
            ?? throw new ForbiddenException(
                "USUARIO_INACTIVO",
                "Usuario no encontrado o inactivo.");

        var empresasAccesibles = await (
            from uer in _db.UsuarioEmpresaRoles
            join e in _db.Set<Empresa>() on uer.EmpresaId equals e.Id
            where uer.UsuarioId == userId
            select new { e.Id, e.Rfc, e.RazonSocial })
            .Distinct()
            .ToListAsync(cancellationToken);

        if (!empresasAccesibles.Exists(x => x.Id == empresaId))
        {
            throw new ForbiddenException(
                "EMPRESA_ACCESS_DENIED",
                $"El usuario no tiene asignación a la empresa {empresaId}.");
        }

        var preferencia = await _db.UsuarioPreferencias
            .FirstOrDefaultAsync(p => p.UsuarioId == userId, cancellationToken);
        if (preferencia is not null && preferencia.UltimaEmpresaId != empresaId)
        {
            preferencia.RegistrarUltimaEmpresa(empresaId);
            await _db.SaveChangesAsync(cancellationToken);
        }

        // Invalidar caché de permisos: la empresa anterior y la nueva tienen
        // sets distintos. El loader re-popula al construir la respuesta.
        await _permissionCache.InvalidateAllForUserAsync(userId, cancellationToken);
        var permisos = await LoadAndCachePermisosAsync(userId, empresaId, cancellationToken);

        var token = _jwtTokenService.CreateAccessToken(usuario, empresaId);

        var empresaInfos = empresasAccesibles
            .Select(e => new EmpresaInfo(e.Id, e.Rfc, e.RazonSocial, EsLaActual: e.Id == empresaId))
            .ToList();

        var comprasSettings = await LoadComprasSettingsAsync(empresaId, cancellationToken);

        return new LoginResponse(
            AccessToken: token.Value,
            ExpiresAt: token.ExpiresAt,
            Usuario: new UsuarioInfo(usuario.Id, usuario.Email, usuario.Nombre),
            Empresas: empresaInfos,
            Permisos: permisos,
            ComprasSettings: comprasSettings);
    }

    private async Task<LoginResponse> CompleteLoginAsync(
        string entraOid,
        string? entraEmail,
        string? entraName,
        Guid? requestedEmpresaId,
        CancellationToken cancellationToken)
    {
        // Bypass para que el global query filter por empresa (PR 4) no nos
        // afecte cuando estamos justamente determinando la empresa.
        using var bypass = _empresaContext.Bypass();

        var usuario = await _db.Usuarios
            .FirstOrDefaultAsync(u => u.EntraOid == entraOid, cancellationToken);

        if (usuario is null)
        {
            usuario = await AutoProvisionAsync(entraOid, entraEmail, entraName, cancellationToken);
        }
        else
        {
            if (!usuario.Activo)
            {
                throw new ForbiddenException(
                    "USUARIO_INACTIVO",
                    "El usuario se encuentra inactivo en el sistema. Contacta al administrador.");
            }

            var nuevoEmail = !string.IsNullOrWhiteSpace(entraEmail) ? entraEmail : null;
            var nuevoNombre = !string.IsNullOrWhiteSpace(entraName) ? entraName : null;

            if ((nuevoEmail is not null && nuevoEmail != usuario.Email) ||
                (nuevoNombre is not null && nuevoNombre != usuario.Nombre))
            {
                usuario.ActualizarPerfil(nuevoEmail, nuevoNombre, departamentoId: null, limpiarDepartamento: false);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        var (selectedEmpresaId, empresas) = await SelectEmpresaAsync(
            usuario, requestedEmpresaId, cancellationToken);

        var permisos = await LoadAndCachePermisosAsync(
            usuario.Id, selectedEmpresaId, cancellationToken);

        var token = _jwtTokenService.CreateAccessToken(usuario, selectedEmpresaId);

        var comprasSettings = await LoadComprasSettingsAsync(selectedEmpresaId, cancellationToken);

        return new LoginResponse(
            AccessToken: token.Value,
            ExpiresAt: token.ExpiresAt,
            Usuario: new UsuarioInfo(usuario.Id, usuario.Email, usuario.Nombre),
            Empresas: empresas,
            Permisos: permisos,
            ComprasSettings: comprasSettings);
    }

    /// <summary>
    /// Lee los settings del módulo Compras de la empresa seleccionada para
    /// incluirlos en el payload de login/cambiar-empresa. Si no hay empresa
    /// (usuario sin asignaciones) o si la fila no existe, retorna null /
    /// default. El bypass del empresa context permite leer la fila aunque
    /// el query filter global aún no esté seteado.
    /// </summary>
    private async Task<Millet.Compras.Application.Settings.ComprasSettingsResponse?> LoadComprasSettingsAsync(
        Guid? empresaId,
        CancellationToken cancellationToken)
    {
        if (empresaId is not Guid empresa)
        {
            return null;
        }

        using var bypass = _empresaContext.Bypass();

        var row = await _comprasDb.ComprasSettings
            .AsNoTracking()
            .Where(s => s.EmpresaId == empresa)
            .Select(s => new { s.AutoGenerarOcAlAutorizar })
            .FirstOrDefaultAsync(cancellationToken);

        return new Millet.Compras.Application.Settings.ComprasSettingsResponse(
            EmpresaId: empresa,
            AutoGenerarOcAlAutorizar: row?.AutoGenerarOcAlAutorizar ?? false);
    }

    /// <summary>
    /// Carga los permisos efectivos del usuario en la empresa actual desde
    /// la caché (TTL 5 min, ADR-0007). En miss, consulta el loader y popula
    /// la caché. Si no hay empresa seleccionada (usuario sin asignaciones),
    /// devuelve lista vacía.
    /// </summary>
    private async Task<IReadOnlyList<string>> LoadAndCachePermisosAsync(
        Guid usuarioId,
        Guid? empresaId,
        CancellationToken cancellationToken)
    {
        if (empresaId is not Guid empresa)
        {
            return Array.Empty<string>();
        }

        var cached = await _permissionCache.GetAsync(usuarioId, empresa, cancellationToken);
        if (cached is not null)
        {
            return cached.ToList();
        }

        var fresh = await _permissionLoader.LoadForUserInEmpresaAsync(
            usuarioId, empresa, cancellationToken);
        await _permissionCache.SetAsync(usuarioId, empresa, fresh, cancellationToken);
        return fresh.ToList();
    }

    private async Task<Usuario> AutoProvisionAsync(
        string entraOid,
        string? entraEmail,
        string? entraName,
        CancellationToken cancellationToken)
    {
        var fallbackEmail = !string.IsNullOrWhiteSpace(entraEmail)
            ? entraEmail
            : $"{entraOid}@unknown.local";
        var fallbackName = !string.IsNullOrWhiteSpace(entraName)
            ? entraName
            : entraOid;

        var usuario = new Usuario(Guid.CreateVersion7(), entraOid, fallbackEmail, fallbackName);
        var preferencia = new UsuarioPreferencia(Guid.CreateVersion7(), usuario.Id);

        _db.Usuarios.Add(usuario);
        _db.UsuarioPreferencias.Add(preferencia);
        await _db.SaveChangesAsync(cancellationToken);

        return usuario;
    }

    private async Task<(Guid? selected, IReadOnlyList<EmpresaInfo> empresas)> SelectEmpresaAsync(
        Usuario usuario,
        Guid? requestedEmpresaId,
        CancellationToken cancellationToken)
    {
        // Empresas accesibles vía UsuarioEmpresaRol → join con compartido.empresas.
        var empresasAccesibles = await (
            from uer in _db.UsuarioEmpresaRoles
            join e in _db.Set<Empresa>() on uer.EmpresaId equals e.Id
            where uer.UsuarioId == usuario.Id
            select new { e.Id, e.Rfc, e.RazonSocial })
            .Distinct()
            .ToListAsync(cancellationToken);

        Guid? selected = null;

        if (requestedEmpresaId is { } requested
            && empresasAccesibles.Exists(x => x.Id == requested))
        {
            selected = requested;
        }
        else
        {
            var preferencia = await _db.UsuarioPreferencias
                .FirstOrDefaultAsync(p => p.UsuarioId == usuario.Id, cancellationToken);

            if (preferencia?.UltimaEmpresaId is { } ultima
                && empresasAccesibles.Exists(x => x.Id == ultima))
            {
                selected = ultima;
            }
            else if (empresasAccesibles.Count == 1)
            {
                selected = empresasAccesibles[0].Id;
            }
        }

        if (selected is { } sel)
        {
            var preferencia = await _db.UsuarioPreferencias
                .FirstOrDefaultAsync(p => p.UsuarioId == usuario.Id, cancellationToken);
            if (preferencia is not null && preferencia.UltimaEmpresaId != sel)
            {
                preferencia.RegistrarUltimaEmpresa(sel);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        var empresaInfos = empresasAccesibles
            .Select(e => new EmpresaInfo(e.Id, e.Rfc, e.RazonSocial, EsLaActual: e.Id == selected))
            .ToList();

        return (selected, empresaInfos);
    }
}
