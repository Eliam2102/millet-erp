using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Catalogos;

/// <summary>
/// PATCH parcial sobre un proveedor (B.5). Convención: nullable null
/// = no tocar, flag <c>limpiarX</c> = setear nullable a null. Cambios
/// permitidos: razonSocial, nombreComercial, rfc, tipoPersona,
/// condicionesPago, moneda preferida, email, telefono y datos bancarios
/// (banco/clabe/beneficiario — TES-PR3 [T-G1]).
/// Inmutables: id, clave, claveLegacy.
/// </summary>
public sealed record ActualizarProveedorCommand(
    Guid ProveedorId,
    string? RazonSocial,
    string? NombreComercial,
    string? Rfc,
    TipoPersonaProveedor? TipoPersona,
    short? CondicionesPagoDias,
    Guid? MonedaPreferidaId,
    string? Email,
    string? Telefono,
    bool LimpiarNombreComercial,
    bool LimpiarCondicionesPago,
    bool LimpiarMonedaPreferida,
    bool LimpiarEmail,
    bool LimpiarTelefono,
    string? Banco = null,
    string? Clabe = null,
    string? Beneficiario = null,
    bool LimpiarBanco = false,
    bool LimpiarClabe = false,
    bool LimpiarBeneficiario = false) : IRequest;

public sealed class ActualizarProveedorValidator : AbstractValidator<ActualizarProveedorCommand>
{
    public ActualizarProveedorValidator()
    {
        RuleFor(c => c.ProveedorId).NotEqual(Guid.Empty);
        RuleFor(c => c.RazonSocial!).NotEmpty().MaximumLength(254)
            .When(c => c.RazonSocial is not null);
        RuleFor(c => c.NombreComercial!).MaximumLength(254)
            .When(c => c.NombreComercial is not null);
        RuleFor(c => c.Rfc!).Length(12, 13).When(c => c.Rfc is not null);
        RuleFor(c => c.TipoPersona).IsInEnum().When(c => c.TipoPersona.HasValue);
        RuleFor(c => c.CondicionesPagoDias).InclusiveBetween((short)0, (short)365)
            .When(c => c.CondicionesPagoDias.HasValue);
        RuleFor(c => c.Email!).EmailAddress().MaximumLength(254)
            .When(c => !string.IsNullOrEmpty(c.Email));
        RuleFor(c => c.Telefono!).MaximumLength(50).When(c => c.Telefono is not null);
        RuleFor(c => c.Banco!).NotEmpty().MaximumLength(120).When(c => c.Banco is not null);
        RuleFor(c => c.Clabe!).Length(18).Matches(@"^\d{18}$")
            .WithMessage("La CLABE debe tener 18 dígitos.")
            .When(c => c.Clabe is not null);
        RuleFor(c => c.Beneficiario!).NotEmpty().MaximumLength(254)
            .When(c => c.Beneficiario is not null);
    }
}

public sealed class ActualizarProveedorHandler : IRequestHandler<ActualizarProveedorCommand>
{
    private readonly CompartidoDbContext _db;

    public ActualizarProveedorHandler(CompartidoDbContext db) => _db = db;

    public async Task Handle(ActualizarProveedorCommand request, CancellationToken cancellationToken)
    {
        var proveedor = await _db.Proveedores
            .FirstOrDefaultAsync(p => p.Id == request.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                $"No se encontró proveedor con id '{request.ProveedorId}'.");

        // Cross-table: si llega un id de moneda, validar antes de mutar.
        if (request.MonedaPreferidaId is Guid monedaId)
        {
            var existeMoneda = await _db.Monedas.AsNoTracking()
                .AnyAsync(m => m.Id == monedaId, cancellationToken);
            if (!existeMoneda)
            {
                throw new EntityNotFoundException(
                    "MONEDA_NO_ENCONTRADA",
                    $"No existe moneda con id '{monedaId}' en compartido.monedas.");
            }
        }

        proveedor.ActualizarDatos(
            razonSocial: request.RazonSocial,
            nombreComercial: request.NombreComercial,
            rfc: request.Rfc,
            tipoPersona: request.TipoPersona,
            condicionesPagoDias: request.CondicionesPagoDias,
            monedaPreferidaId: request.MonedaPreferidaId,
            email: request.Email,
            telefono: request.Telefono,
            banco: request.Banco,
            clabe: request.Clabe,
            beneficiario: request.Beneficiario,
            limpiarNombreComercial: request.LimpiarNombreComercial,
            limpiarCondicionesPago: request.LimpiarCondicionesPago,
            limpiarMonedaPreferida: request.LimpiarMonedaPreferida,
            limpiarEmail: request.LimpiarEmail,
            limpiarTelefono: request.LimpiarTelefono,
            limpiarBanco: request.LimpiarBanco,
            limpiarClabe: request.LimpiarClabe,
            limpiarBeneficiario: request.LimpiarBeneficiario);

        await _db.SaveChangesAsync(cancellationToken);
    }
}
