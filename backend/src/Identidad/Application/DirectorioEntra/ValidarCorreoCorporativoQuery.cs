using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;

namespace Millet.Identidad.Application.DirectorioEntra;

/// <summary>
/// Validación en línea del correo corporativo en el paso "Acceso" del
/// wizard de alta (plan 15, F2). Responde a la vez para los dos caminos
/// con cuenta Microsoft:
///
/// <list type="bullet">
///   <item><see cref="ValidacionCorreoCorporativoResponse.PuedeVincularCuentaExistente"/>:
///         "Ya tiene cuenta Microsoft".</item>
///   <item><see cref="ValidacionCorreoCorporativoResponse.PuedeCrearCuentaNueva"/>:
///         "Cuenta Microsoft nueva" (el correo está libre).</item>
/// </list>
///
/// Además informa si ya hay un usuario del ERP con ese correo u OID, para
/// que el alta lo reutilice en lugar de duplicarlo (F3), y valida que no
/// esté ya asignado a otro empleado en el catálogo compartido. Solo lectura.
/// </summary>
public sealed record ValidarCorreoCorporativoQuery(string Correo)
    : IRequest<ValidacionCorreoCorporativoResponse>;

public sealed record ValidacionCorreoCorporativoResponse(
    string Correo,
    bool DominioPermitido,
    CuentaEntraResumen? CuentaEntra,
    UsuarioErpResumen? UsuarioErp,
    EmpleadoVinculadoResumen? EmpleadoVinculado,
    bool PuedeVincularCuentaExistente,
    bool PuedeCrearCuentaNueva,
    string? MotivoBloqueo = null);

public sealed record CuentaEntraResumen(string ObjectId, string NombreMostrado, bool Habilitada);

public sealed record UsuarioErpResumen(Guid Id, string Nombre, EstadoAcceso EstadoAcceso, bool Activo);

public sealed record EmpleadoVinculadoResumen(Guid Id, string Clave, string Nombre);

public sealed class ValidarCorreoCorporativoValidator : AbstractValidator<ValidarCorreoCorporativoQuery>
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled);

    public ValidarCorreoCorporativoValidator()
    {
        RuleFor(q => q.Correo)
            .NotEmpty()
            .MaximumLength(254)
            .Must(c => !string.IsNullOrWhiteSpace(c) && EmailRegex.IsMatch(c.Trim()))
            .WithMessage("Correo debe tener formato válido 'local@dominio.tld'.");
    }
}

public sealed class ValidarCorreoCorporativoHandler
    : IRequestHandler<ValidarCorreoCorporativoQuery, ValidacionCorreoCorporativoResponse>
{
    private readonly IEntraDirectorioPort _directorio;
    private readonly IdentidadDbContext _db;
    private readonly CompartidoDbContext _compartido;
    private readonly IOptionsMonitor<EntraDirectorioOptions> _options;

    public ValidarCorreoCorporativoHandler(
        IEntraDirectorioPort directorio,
        IdentidadDbContext db,
        CompartidoDbContext compartido,
        IOptionsMonitor<EntraDirectorioOptions> options)
    {
        _directorio = directorio;
        _db = db;
        _compartido = compartido;
        _options = options;
    }

    public async Task<ValidacionCorreoCorporativoResponse> Handle(
        ValidarCorreoCorporativoQuery query, CancellationToken cancellationToken)
    {
        var correo = query.Correo.Trim();
        var dominioPermitido = _options.CurrentValue.PermiteCorreo(correo);

        var cuenta = await _directorio.BuscarPorCorreoAsync(correo, cancellationToken);

        // Candidatos de usuario en el ERP por correo o por el OID de la cuenta encontrada.
        var candidatos = await _db.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Email == correo || (cuenta != null && u.EntraOid == cuenta.ObjectId))
            .ToListAsync(cancellationToken);

        Usuario? usuario = null;
        bool conflictoCandidatos = candidatos.Count > 1;
        if (candidatos.Count == 1)
        {
            usuario = candidatos[0];
        }

        UsuarioErpResumen? usuarioResumen = usuario is null
            ? null
            : new UsuarioErpResumen(usuario.Id, usuario.Nombre, usuario.EstadoAcceso, usuario.Activo);

        // Buscar si ya existe un empleado en el ERP vinculado a estos usuarios o con este correo
        var candidatoIds = candidatos.Select(c => c.Id).ToList();
        var empleadoQuery = _compartido.Empleados.IgnoreQueryFilters().AsNoTracking();
        if (candidatoIds.Count > 0)
        {
            empleadoQuery = empleadoQuery.Where(e => (e.UsuarioId != null && candidatoIds.Contains(e.UsuarioId.Value)) || e.Email == correo);
        }
        else
        {
            empleadoQuery = empleadoQuery.Where(e => e.Email == correo);
        }

        var empleado = await empleadoQuery
            .Select(e => new EmpleadoVinculadoResumen(e.Id, e.Clave, e.Nombre))
            .FirstOrDefaultAsync(cancellationToken);

        string? motivoBloqueo = null;
        if (!dominioPermitido)
        {
            motivoBloqueo = "DOMINIO_NO_PERMITIDO";
        }
        else if (empleado is not null)
        {
            motivoBloqueo = "USUARIO_YA_VINCULADO";
        }
        else if (usuario is not null && !usuario.Activo)
        {
            motivoBloqueo = "USUARIO_INACTIVO";
        }
        else if (usuario is not null && usuario.EsCuentaTecnica)
        {
            motivoBloqueo = "USUARIO_ES_CUENTA_TECNICA";
        }
        else if (conflictoCandidatos)
        {
            motivoBloqueo = "USUARIO_CORREO_OID_INCONSISTENTE";
        }
        else if (cuenta is not null && !cuenta.Habilitada)
        {
            motivoBloqueo = "ENTRA_CUENTA_DESHABILITADA";
        }

        bool cuentaHabilitada = cuenta is { Habilitada: true };
        bool puedeVincular = dominioPermitido
            && cuentaHabilitada
            && empleado is null
            && (usuario is null || (usuario.Activo && !usuario.EsCuentaTecnica))
            && !conflictoCandidatos;

        bool puedeCrear = dominioPermitido
            && cuenta is null
            && usuario is null
            && empleado is null;

        return new ValidacionCorreoCorporativoResponse(
            correo,
            dominioPermitido,
            cuenta is null ? null : new CuentaEntraResumen(cuenta.ObjectId, cuenta.NombreMostrado, cuenta.Habilitada),
            usuarioResumen,
            empleado,
            PuedeVincularCuentaExistente: puedeVincular,
            PuedeCrearCuentaNueva: puedeCrear,
            MotivoBloqueo: motivoBloqueo);
    }
}
