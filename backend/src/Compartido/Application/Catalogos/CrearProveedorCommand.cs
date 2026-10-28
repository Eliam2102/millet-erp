using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
/// Alta de proveedor en <c>compartido.proveedores</c> (B.5). UNIQUE
/// (clave) → 422 si choca; RFC único (excepto genéricos SAT,
/// F1-ADM-05) → 409 <c>PROVEEDOR_RFC_DUPLICADO</c>. Validación
/// cross-table de <see cref="MonedaPreferidaId"/> contra
/// <c>compartido.monedas</c>.
/// </summary>
public sealed record CrearProveedorCommand(
    string Clave,
    string RazonSocial,
    string? Rfc,
    TipoPersonaProveedor TipoPersona,
    string? NombreComercial,
    short? CondicionesPagoDias,
    Guid? MonedaPreferidaId,
    string? Email,
    string? Telefono,
    OrigenProveedor? Origen = null,
    string? RegimenFiscalCodigo = null,
    string? ConceptoRetencion = null,
    string? Calle = null,
    string? NumeroExterior = null,
    string? NumeroInterior = null,
    string? Colonia = null,
    string? Ciudad = null,
    string? Municipio = null,
    string? Estado = null,
    string? CodigoPostal = null,
    string? Pais = null,
    string? TaxId = null,
    int? ClaveAw = null) : IRequest<CrearProveedorResponse>;

public sealed record CrearProveedorResponse(
    Guid Id,
    string Clave,
    EstatusCatalogo Estatus,
    Guid? PosibleDuplicadoDeId);

public sealed class CrearProveedorValidator : AbstractValidator<CrearProveedorCommand>
{
    public CrearProveedorValidator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Calle).MaximumLength(254);
        RuleFor(c => c.NumeroExterior).MaximumLength(20);
        RuleFor(c => c.NumeroInterior).MaximumLength(20);
        RuleFor(c => c.Colonia).MaximumLength(254);
        RuleFor(c => c.Ciudad).MaximumLength(100);
        RuleFor(c => c.Municipio).MaximumLength(100);
        RuleFor(c => c.Estado).MaximumLength(100);
        RuleFor(c => c.CodigoPostal).MaximumLength(20);
        RuleFor(c => c.Pais).MaximumLength(3);
        RuleFor(c => c.TaxId).MaximumLength(40).WithErrorCode("PROVEEDOR_TAXID_INVALIDO").WithMessage("El Tax ID admite hasta 40 caracteres; corrígelo antes de guardar.");
        RuleFor(c => c.Pais).Matches(@"^([A-Za-z]{3})?$").WithMessage("Escribe el país con tres letras, por ejemplo MEX.");
        RuleFor(c => c.TaxId).Must(string.IsNullOrWhiteSpace).When(c => c.Origen != OrigenProveedor.Extranjero).WithErrorCode("PROVEEDOR_TAXID_SOLO_EXTRANJERO").WithMessage("El Tax ID solo corresponde a proveedores extranjeros.");
        RuleFor(c => c.RazonSocial).NotEmpty().MaximumLength(254);
        RuleFor(c => c.ClaveAw).GreaterThan(0).When(c => c.ClaveAw.HasValue)
            .WithErrorCode("PROVEEDOR_CLAVE_AW_INVALIDA").WithMessage("La clave de A+W es un número entero mayor que cero.");
        // 86e3pdmee: el extranjero no tiene RFC; se guarda el genérico XEXX010101000.
        RuleFor(c => c.Rfc!).NotEmpty().When(c => c.Origen != OrigenProveedor.Extranjero);
        RuleFor(c => c.Rfc!).Length(12, 13).When(c => !string.IsNullOrWhiteSpace(c.Rfc));
        RuleFor(c => c.Origen!.Value).IsInEnum().When(c => c.Origen.HasValue);
        RuleFor(c => c.RegimenFiscalCodigo!).Length(3).When(c => !string.IsNullOrWhiteSpace(c.RegimenFiscalCodigo));
        RuleFor(c => c.ConceptoRetencion!).MaximumLength(80).When(c => c.ConceptoRetencion is not null);
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

        if (request.ClaveAw is int claveAw)
            await ClaveAwProveedor.ValidarUnicaAsync(_db, claveAw, proveedorId: null, cancellationToken);

        // RFC único (F1-ADM-05), excepto genéricos SAT: cualquier estatus
        // cuenta como duplicado. RFC genérico + misma razón social no se
        // rechaza ni se fusiona: se crea en EnRevision para que alguien lo
        // F1-ADM-05 G1.1: todo proveedor nuevo nace en EnRevision (Compras solicita, CxP valida).
        var rfcNormalizado = string.IsNullOrWhiteSpace(request.Rfc)
            ? Proveedor.RfcGenericoExtranjero
            : request.Rfc.Trim().ToUpperInvariant();
        var estatus = EstatusCatalogo.EnRevision;
        Guid? posibleDuplicadoDeId = null;

        if (!Proveedor.EsRfcGenerico(rfcNormalizado))
        {
            var duplicadoRfc = await _db.Proveedores.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Rfc == rfcNormalizado, cancellationToken);
            if (duplicadoRfc is not null)
            {
                throw new ConflictException(
                    "PROVEEDOR_RFC_DUPLICADO",
                    $"Ya existe el proveedor {duplicadoRfc.Clave} · {duplicadoRfc.RazonSocial} " +
                    $"con RFC {rfcNormalizado}.");
            }
        }
        else
        {
            // Varios proveedores legítimos comparten el RFC genérico: se
            // compara la razón social contra todos, no solo contra el primero.
            var razonSocial = request.RazonSocial.Trim().ToUpperInvariant();
#pragma warning disable CA1304, CA1311, CA1862
            posibleDuplicadoDeId = await _db.Proveedores.AsNoTracking()
                .Where(p => p.Rfc == rfcNormalizado && p.RazonSocial.Trim().ToUpper() == razonSocial)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync(cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862
            if (posibleDuplicadoDeId is not null) estatus = EstatusCatalogo.EnRevision;
        }

        var proveedor = new Proveedor(
            id: Guid.CreateVersion7(),
            clave: request.Clave,
            razonSocial: request.RazonSocial,
            rfc: rfcNormalizado,
            tipoPersona: request.TipoPersona,
            estatus: estatus,
            nombreComercial: request.NombreComercial,
            condicionesPagoDias: request.CondicionesPagoDias,
            monedaPreferidaId: request.MonedaPreferidaId,
            email: request.Email,
            telefono: request.Telefono);

        // 86e3pdmee: la clasificación fiscal viaja en la misma transacción del alta.
        if (request.Origen is not null || !string.IsNullOrWhiteSpace(request.RegimenFiscalCodigo) || !string.IsNullOrWhiteSpace(request.ConceptoRetencion))
        {
            var regimen = string.IsNullOrWhiteSpace(request.RegimenFiscalCodigo) ? null : request.RegimenFiscalCodigo.Trim();
            await ClasificacionFiscalProveedor.ValidarRegimenAsync(_db, request.TipoPersona, request.Origen, regimen, cancellationToken);
            proveedor.ActualizarClasificacionFiscal(request.Origen, regimen, request.ConceptoRetencion);
        }

        proveedor.AsignarClaveAw(request.ClaveAw);
        _db.Proveedores.Add(proveedor);
        proveedor.ActualizarDomicilioFiscal(request.Calle, request.NumeroExterior, request.NumeroInterior, request.Colonia, request.Ciudad, request.Municipio, request.Estado, request.CodigoPostal, request.Pais, request.TaxId);
        await _db.SaveChangesAsync(cancellationToken);

        return new CrearProveedorResponse(
            proveedor.Id, proveedor.Clave, proveedor.Estatus, posibleDuplicadoDeId);
    }
}
