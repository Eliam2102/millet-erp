using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compras.Application.Lineas.AgregarLinea;

/// <summary>
/// Handler de <see cref="AgregarLineaCommand"/>. Carga la
/// <see cref="Requisicion"/> con sus líneas, invoca el método del
/// agregado (que valida estado y reglas) y persiste.
///
/// <para>
/// F7-PR1: valida cross-table que el <c>articuloId</c> exista en
/// <c>compartido.articulos</c> y esté <c>Activo</c>. Si no, 422.
/// </para>
///
/// Si la requisición no existe (o no es accesible para la empresa
/// actual, gracias al global query filter de <c>BaseDbContext</c>),
/// lanza <see cref="EntityNotFoundException"/> → 404 sin revelar
/// existencia (cuidado §8.1 [P0]).
/// </summary>
public sealed class AgregarLineaHandler : IRequestHandler<AgregarLineaCommand, AgregarLineaResponse>
{
    private readonly ComprasDbContext _db;
    private readonly CompartidoDbContext _compartido;
    private readonly IDecimalesUnidadGuard _decimalesGuard;

    public AgregarLineaHandler(
        ComprasDbContext db,
        CompartidoDbContext compartido,
        IDecimalesUnidadGuard decimalesGuard)
    {
        _db = db;
        _compartido = compartido;
        _decimalesGuard = decimalesGuard;
    }

    public async Task<AgregarLineaResponse> Handle(
        AgregarLineaCommand command,
        CancellationToken cancellationToken)
    {
        var requisicion = await _db.Requisiciones
            .Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == command.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{command.RequisicionId}' en la empresa actual.");

        // F7-PR1: validar articulo cross-table (existe + activo).
        var articulo = await _compartido.Articulos
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == command.ArticuloId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ARTICULO_NO_ENCONTRADO",
                $"No se encontró artículo con id '{command.ArticuloId}'.");

        if (articulo.Estatus != EstatusCatalogo.Activo)
        {
            throw new BusinessRuleException(
                "ARTICULO_INACTIVO",
                $"El artículo '{articulo.Clave}' está {articulo.Estatus} y no puede usarse en líneas nuevas.");
        }

        // ADR-0046 Etapa 2: la cantidad no puede exceder los decimales de la
        // unidad del artículo (FK NULL → no valida).
        await _decimalesGuard.ValidarAsync(
            new[] { new CantidadAValidar(command.ArticuloId, command.Cantidad, command.UnidadMedida) },
            cancellationToken);

        var linea = requisicion.AgregarLinea(
            lineaId: Guid.CreateVersion7(),
            articuloId: command.ArticuloId,
            cantidad: command.Cantidad,
            unidadMedida: command.UnidadMedida,
            precioEstimado: Money.Of(command.PrecioEstimadoMonto, command.PrecioEstimadoMoneda),
            cuentaContableId: command.CuentaContableId,
            centroCostoId: command.CentroCostoId,
            proyecto: command.Proyecto,
            fechaRequerida: command.FechaRequerida,
            notas: command.Notas);

        await _db.SaveChangesAsync(cancellationToken);

        return new AgregarLineaResponse(
            Id: linea.Id,
            RequisicionId: requisicion.Id,
            Posicion: linea.Posicion,
            Version: linea.Version);
    }
}
