using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas;

/// <summary>Una concesión (sucursal?, canal?) del replace-set de alcances administrativos.</summary>
public sealed record UsuarioAlcanceInput(Guid? SucursalId, short? CanalVentaId);

/// <summary>
/// Reemplaza el conjunto de concesiones de alcance de un usuario sin caja
/// (`[Decisión 12-6]`). Lista vacía = quitar restricciones explícitas (el
/// usuario queda sin concesiones: no ve nada salvo por sus cajas o por
/// <c>facturacion.caja.leer-todas</c>).
/// </summary>
public sealed record ReemplazarUsuarioAlcancesCommand(
    Guid UsuarioId,
    IReadOnlyList<UsuarioAlcanceInput> Alcances) : IRequest<UsuarioAlcancesResponse>;

public sealed record UsuarioAlcancesResponse(Guid UsuarioId, int Total);

public sealed class ReemplazarUsuarioAlcancesValidator : AbstractValidator<ReemplazarUsuarioAlcancesCommand>
{
    public ReemplazarUsuarioAlcancesValidator()
    {
        RuleFor(c => c.UsuarioId).NotEmpty();
        RuleForEach(c => c.Alcances).ChildRules(a =>
        {
            a.RuleFor(x => x).Must(x => x.SucursalId is not null || x.CanalVentaId is not null)
                .WithMessage("Cada concesión requiere al menos sucursal o canal (comodín total = permiso leer-todas).");
        });
    }
}

public sealed class ReemplazarUsuarioAlcancesHandler
    : IRequestHandler<ReemplazarUsuarioAlcancesCommand, UsuarioAlcancesResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ISucursalesReadPort _sucursales;
    private readonly ICanalesVentaReadPort _canales;

    public ReemplazarUsuarioAlcancesHandler(
        FacturacionDbContext db,
        ICurrentEmpresaContext empresa,
        ISucursalesReadPort sucursales,
        ICanalesVentaReadPort canales)
    {
        _db = db;
        _empresa = empresa;
        _sucursales = sucursales;
        _canales = canales;
    }

    public async Task<UsuarioAlcancesResponse> Handle(
        ReemplazarUsuarioAlcancesCommand command, CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        var sucursalIds = command.Alcances
            .Where(a => a.SucursalId is Guid id && id != Guid.Empty)
            .Select(a => a.SucursalId!.Value)
            .Distinct()
            .ToArray();
        var invalidas = await _sucursales.FiltrarNoActivasAsync(sucursalIds, cancellationToken);
        if (invalidas.Count > 0)
            throw new BusinessRuleException(
                "USUARIO_ALCANCE_SUCURSAL_NO_ACTIVA",
                $"Sucursales inexistentes o inactivas: {string.Join(", ", invalidas)}.");

        foreach (var canalId in command.Alcances
                     .Where(a => a.CanalVentaId is > 0)
                     .Select(a => a.CanalVentaId!.Value)
                     .Distinct())
        {
            if (!await _canales.ExisteActivoAsync(canalId, cancellationToken))
                throw new BusinessRuleException(
                    "USUARIO_ALCANCE_CANAL_NO_ACTIVO",
                    $"El canal de venta '{canalId}' no existe o está inactivo.");
        }

        // Replace-set completo: el query filter por empresa ya acota las filas.
        var actuales = await _db.UsuariosAlcance
            .Where(a => a.UsuarioId == command.UsuarioId)
            .ToListAsync(cancellationToken);
        _db.UsuariosAlcance.RemoveRange(actuales);

        var nuevas = command.Alcances
            .Select(a => (a.SucursalId, a.CanalVentaId))
            .Distinct()
            .Select(a => UsuarioAlcance.Crear(empresaId, command.UsuarioId, a.SucursalId, a.CanalVentaId))
            .ToList();
        _db.UsuariosAlcance.AddRange(nuevas);

        await _db.SaveChangesAsync(cancellationToken);
        return new UsuarioAlcancesResponse(command.UsuarioId, nuevas.Count);
    }
}
