using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
/// que el alta lo reutilice en lugar de duplicarlo (F3). Solo lectura.
/// </summary>
public sealed record ValidarCorreoCorporativoQuery(string Correo)
    : IRequest<ValidacionCorreoCorporativoResponse>;

public sealed record ValidacionCorreoCorporativoResponse(
    string Correo,
    bool DominioPermitido,
    CuentaEntraResumen? CuentaEntra,
    UsuarioErpResumen? UsuarioErp,
    bool PuedeVincularCuentaExistente,
    bool PuedeCrearCuentaNueva);

public sealed record CuentaEntraResumen(string ObjectId, string NombreMostrado, bool Habilitada);

public sealed record UsuarioErpResumen(Guid Id, string Nombre, EstadoAcceso EstadoAcceso, bool Activo);

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
    private readonly IOptionsMonitor<EntraDirectorioOptions> _options;

    public ValidarCorreoCorporativoHandler(
        IEntraDirectorioPort directorio,
        IdentidadDbContext db,
        IOptionsMonitor<EntraDirectorioOptions> options)
    {
        _directorio = directorio;
        _db = db;
        _options = options;
    }

    public async Task<ValidacionCorreoCorporativoResponse> Handle(
        ValidarCorreoCorporativoQuery query, CancellationToken cancellationToken)
    {
        var correo = query.Correo.Trim();
        var dominioPermitido = _options.CurrentValue.PermiteCorreo(correo);

        var cuenta = await _directorio.BuscarPorCorreoAsync(correo, cancellationToken);

        // Usuario del ERP por correo o por el OID de la cuenta encontrada.
        var usuarioQuery = _db.Usuarios.AsNoTracking();
        usuarioQuery = cuenta is null
            ? usuarioQuery.Where(u => u.Email == correo)
            : usuarioQuery.Where(u => u.Email == correo || u.EntraOid == cuenta.ObjectId);
        var usuario = await usuarioQuery
            .Select(u => new UsuarioErpResumen(u.Id, u.Nombre, u.EstadoAcceso, u.Activo))
            .FirstOrDefaultAsync(cancellationToken);

        return new ValidacionCorreoCorporativoResponse(
            correo,
            dominioPermitido,
            cuenta is null ? null : new CuentaEntraResumen(cuenta.ObjectId, cuenta.NombreMostrado, cuenta.Habilitada),
            usuario,
            PuedeVincularCuentaExistente: dominioPermitido && cuenta is { Habilitada: true },
            PuedeCrearCuentaNueva: dominioPermitido && cuenta is null && usuario is null);
    }
}
