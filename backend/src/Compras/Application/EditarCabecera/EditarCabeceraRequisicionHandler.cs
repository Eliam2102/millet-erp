using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compras.Application.EditarCabecera;

/// <summary>
/// Handler de <see cref="EditarCabeceraRequisicionCommand"/> (B.4).
/// Valida cross-table del proveedor sugerido (mismo patrón que
/// <c>CrearRequisicionHandler</c>), luego invoca
/// <c>Requisicion.EditarCabecera()</c> en el agregado, que valida
/// <c>Estado == Borrador</c> y muta los campos.
/// </summary>
public sealed class EditarCabeceraRequisicionHandler
    : IRequestHandler<EditarCabeceraRequisicionCommand>
{
    private readonly ComprasDbContext _comprasDb;
    private readonly CompartidoDbContext _compartidoDb;

    public EditarCabeceraRequisicionHandler(
        ComprasDbContext comprasDb,
        CompartidoDbContext compartidoDb)
    {
        _comprasDb = comprasDb;
        _compartidoDb = compartidoDb;
    }

    public async Task Handle(
        EditarCabeceraRequisicionCommand request, CancellationToken cancellationToken)
    {
        var requisicion = await _comprasDb.Requisiciones
            .FirstOrDefaultAsync(r => r.Id == request.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{request.RequisicionId}' en la empresa actual.");

        // Validar proveedor sugerido si llega un id (no si llega null o
        // limpiar=true). Mismo error code que CrearRequisicion para que
        // el FE maneje el caso uniformemente.
        if (request.ProveedorSugeridoId is Guid provId)
        {
            var proveedor = await _compartidoDb.Proveedores.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == provId, cancellationToken)
                ?? throw new EntityNotFoundException(
                    "PROVEEDOR_NO_ENCONTRADO",
                    $"No se encontró proveedor con id '{provId}'.");

            if (proveedor.Estatus != EstatusCatalogo.Activo)
            {
                throw new BusinessRuleException(
                    "PROVEEDOR_INACTIVO",
                    $"El proveedor '{proveedor.Clave}' no está activo.");
            }
        }

        requisicion.EditarCabecera(
            descripcion: request.Descripcion,
            fechaEntregaDeseada: request.FechaEntregaDeseada,
            prioridad: request.Prioridad,
            proveedorSugeridoId: request.ProveedorSugeridoId,
            clasificacion: request.Clasificacion,
            limpiarDescripcion: request.LimpiarDescripcion,
            limpiarFechaEntregaDeseada: request.LimpiarFechaEntregaDeseada,
            limpiarProveedorSugeridoId: request.LimpiarProveedorSugeridoId);

        await _comprasDb.SaveChangesAsync(cancellationToken);
    }
}
