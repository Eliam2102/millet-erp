using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.Compartido.Application.Catalogos.Proveedores;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Catalogos;

/// <summary>
/// PATCH parcial sobre un proveedor (B.5). Convención: nullable null
/// = no tocar, flag <c>limpiarX</c> = setear nullable a null. Cambios
/// permitidos: razonSocial, nombreComercial, rfc, tipoPersona,
/// condicionesPago, moneda preferida, email y telefono. Las cuentas
/// bancarias tienen sus propios comandos (86e3pdmee). Si <c>rfc</c> viene y
/// cambia, único (excepto genéricos SAT, F1-ADM-05) → 409
/// <c>PROVEEDOR_RFC_DUPLICADO</c>.
/// Inmutables: id, clave, claveLegacy.
/// </summary>
public sealed record ActualizarProveedorCommand(
    Guid ProveedorId,
    string? RazonSocial = null,
    string? NombreComercial = null,
    string? Rfc = null,
    TipoPersonaProveedor? TipoPersona = null,
    short? CondicionesPagoDias = null,
    Guid? MonedaPreferidaId = null,
    string? Email = null,
    string? Telefono = null,
    bool LimpiarNombreComercial = false,
    bool LimpiarCondicionesPago = false,
    bool LimpiarMonedaPreferida = false,
    bool LimpiarEmail = false,
    bool LimpiarTelefono = false,
    string? Calle = null,
    string? NumeroExterior = null,
    string? NumeroInterior = null,
    string? Colonia = null,
    string? Ciudad = null,
    string? Municipio = null,
    string? Estado = null,
    string? CodigoPostal = null,
    string? Pais = null,
    string? TaxId = null) : IRequest;

public sealed class ActualizarProveedorValidator : AbstractValidator<ActualizarProveedorCommand>
{
    public ActualizarProveedorValidator()
    {
        RuleFor(c => c.ProveedorId).NotEqual(Guid.Empty);
        RuleFor(c => c.Calle).MaximumLength(254);
        RuleFor(c => c.NumeroExterior).MaximumLength(20);
        RuleFor(c => c.NumeroInterior).MaximumLength(20);
        RuleFor(c => c.Colonia).MaximumLength(254);
        RuleFor(c => c.Ciudad).MaximumLength(100);
        RuleFor(c => c.Municipio).MaximumLength(100);
        RuleFor(c => c.Estado).MaximumLength(100);
        RuleFor(c => c.CodigoPostal).MaximumLength(20);
        RuleFor(c => c.Pais).MaximumLength(3);
        RuleFor(c => c.TaxId).MaximumLength(40);
        RuleFor(c => c.Pais).Matches(@"^([A-Za-z]{3})?$").WithMessage("Escribe el país con tres letras, por ejemplo MEX.");
        RuleFor(c => c.RazonSocial!).NotEmpty().MaximumLength(254)
            .When(c => c.RazonSocial is not null);
        RuleFor(c => c.NombreComercial!).MaximumLength(254)
            .When(c => c.NombreComercial is not null);
        // Vacío es válido aquí: en un proveedor extranjero equivale al RFC genérico (lo resuelve el handler).
        RuleFor(c => c.Rfc!).Length(12, 13).When(c => !string.IsNullOrWhiteSpace(c.Rfc));
        RuleFor(c => c.TipoPersona).IsInEnum().When(c => c.TipoPersona.HasValue);
        RuleFor(c => c.CondicionesPagoDias).InclusiveBetween((short)0, (short)365)
            .When(c => c.CondicionesPagoDias.HasValue);
        RuleFor(c => c.Email!).EmailAddress().MaximumLength(254)
            .When(c => !string.IsNullOrEmpty(c.Email));
        RuleFor(c => c.Telefono!).MaximumLength(50).When(c => c.Telefono is not null);
    }
}

public sealed class ActualizarProveedorHandler : IRequestHandler<ActualizarProveedorCommand>
{
    private readonly CompartidoDbContext _db;

    public ActualizarProveedorHandler(CompartidoDbContext db)
    {
        _db = db;
    }

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

        // RFC único (F1-ADM-05), excepto genéricos SAT: solo se valida si
        // el RFC viene en el body y cambia respecto al actual. Genérico en
        // edición se permite sin más (no hay razón social "nueva" que
        // comparar contra un alta ya existente).
        var rfc = request.Rfc;
        if (rfc is not null && string.IsNullOrWhiteSpace(rfc))
        {
            // 86e3pdmee: el extranjero no tiene RFC; limpiar el campo deja el genérico.
            rfc = proveedor.Origen == OrigenProveedor.Extranjero
                ? Proveedor.RfcGenericoExtranjero
                : throw new BusinessRuleException("PROVEEDOR_RFC_INVALIDO", "El RFC debe tener 12 (moral) o 13 (física) caracteres.");
        }
        if (rfc is not null)
        {
            var rfcNormalizado = rfc.Trim().ToUpperInvariant();
            if (rfcNormalizado != proveedor.Rfc && !Proveedor.EsRfcGenerico(rfcNormalizado))
            {
                var duplicadoRfc = await _db.Proveedores.AsNoTracking()
                    .FirstOrDefaultAsync(
                        p => p.Rfc == rfcNormalizado && p.Id != proveedor.Id, cancellationToken);
                if (duplicadoRfc is not null)
                {
                    throw new ConflictException(
                        "PROVEEDOR_RFC_DUPLICADO",
                        $"Ya existe el proveedor {duplicadoRfc.Clave} · {duplicadoRfc.RazonSocial} " +
                        $"con RFC {rfcNormalizado}.");
                }
            }
        }

        // 86e3pdmee: cambiar el tipo de persona no puede dejar un régimen que ya no le corresponde.
        if (request.TipoPersona is { } nuevoTipo && nuevoTipo != proveedor.TipoPersona)
            await ClasificacionFiscalProveedor.ValidarRegimenAsync(
                _db, nuevoTipo, proveedor.Origen, proveedor.RegimenFiscalCodigo, cancellationToken);

        proveedor.ActualizarDatos(
            razonSocial: request.RazonSocial,
            nombreComercial: request.NombreComercial,
            rfc: rfc,
            tipoPersona: request.TipoPersona,
            condicionesPagoDias: request.CondicionesPagoDias,
            monedaPreferidaId: request.MonedaPreferidaId,
            email: request.Email,
            telefono: request.Telefono,
            limpiarNombreComercial: request.LimpiarNombreComercial,
            limpiarCondicionesPago: request.LimpiarCondicionesPago,
            limpiarMonedaPreferida: request.LimpiarMonedaPreferida,
            limpiarEmail: request.LimpiarEmail,
            limpiarTelefono: request.LimpiarTelefono);

        proveedor.ActualizarDomicilioFiscal(request.Calle, request.NumeroExterior, request.NumeroInterior, request.Colonia, request.Ciudad, request.Municipio, request.Estado, request.CodigoPostal, request.Pais, request.TaxId);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
