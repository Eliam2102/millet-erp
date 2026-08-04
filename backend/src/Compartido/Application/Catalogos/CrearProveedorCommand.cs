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
/// Alta de proveedor en <c>compartido.proveedores</c> (B.5). UNIQUE
/// (clave) → 409 si choca. Validación cross-table de
/// <see cref="MonedaPreferidaId"/> contra <c>compartido.monedas</c>.
/// </summary>
public sealed record CrearProveedorCommand(
    string Clave,
    string RazonSocial,
    string Rfc,
    TipoPersonaProveedor TipoPersona,
    string? NombreComercial,
    short? CondicionesPagoDias,
    Guid? MonedaPreferidaId,
    string? Email,
    string? Telefono) : IRequest<CrearProveedorResponse>;

public sealed record CrearProveedorResponse(Guid Id, string Clave);

public sealed class CrearProveedorValidator : AbstractValidator<CrearProveedorCommand>
{
    public CrearProveedorValidator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.RazonSocial).NotEmpty().MaximumLength(254);
        RuleFor(c => c.Rfc).NotEmpty().Length(12, 13);
        RuleFor(c => c.TipoPersona).IsInEnum();
        RuleFor(c => c.NombreComercial!).MaximumLength(254).When(c => c.NombreComercial is not null);
        RuleFor(c => c.CondicionesPagoDias).InclusiveBetween((short)0, (short)365)
            .When(c => c.CondicionesPagoDias.HasValue);
        RuleFor(c => c.Email!).MaximumLength(254).EmailAddress().When(c => !string.IsNullOrEmpty(c.Email));
        RuleFor(c => c.Telefono!).MaximumLength(50).When(c => c.Telefono is not null);
    }
}

public sealed class CrearProveedorHandler
    : IRequestHandler<CrearProveedorCommand, CrearProveedorResponse>
{
    private readonly CompartidoDbContext _db;

    public CrearProveedorHandler(CompartidoDbContext db) => _db = db;

    public async Task<CrearProveedorResponse> Handle(
        CrearProveedorCommand request, CancellationToken cancellationToken)
    {
        // Cross-table: la moneda preferida debe existir.
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

        // UNIQUE clave: short-circuit con 422 antes de SaveChanges para
        // dar error code legible al FE en lugar de un DbUpdateException
        // genérico al hacer el INSERT.
        var claveExiste = await _db.Proveedores.AsNoTracking()
            .AnyAsync(p => p.Clave == request.Clave, cancellationToken);
        if (claveExiste)
        {
            throw new BusinessRuleException(
                "PROVEEDOR_CLAVE_DUPLICADA",
                $"Ya existe un proveedor con clave '{request.Clave}'.");
        }

        var proveedor = new Proveedor(
            id: Guid.CreateVersion7(),
            clave: request.Clave,
            razonSocial: request.RazonSocial,
            rfc: request.Rfc,
            tipoPersona: request.TipoPersona,
            nombreComercial: request.NombreComercial,
            condicionesPagoDias: request.CondicionesPagoDias,
            monedaPreferidaId: request.MonedaPreferidaId,
            email: request.Email,
            telefono: request.Telefono);

        _db.Proveedores.Add(proveedor);
        await _db.SaveChangesAsync(cancellationToken);

        return new CrearProveedorResponse(proveedor.Id, proveedor.Clave);
    }
}
