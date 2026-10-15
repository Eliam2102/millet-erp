using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.ActualizarInformacionImportacion;

public sealed class ActualizarInformacionImportacionHandler
    : IRequestHandler<ActualizarInformacionImportacionCommand>
{
    private readonly ComprasDbContext _db;

    public ActualizarInformacionImportacionHandler(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task Handle(ActualizarInformacionImportacionCommand command, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}'.");

        // NumeroPedimento se IGNORA en este comando — tiene su propio
        // endpoint /numero-pedimento porque es editable post-autorización.
        var info = new InformacionImportacion(
            command.IncotermId,
            command.PaisOrigen,
            command.NumeroContenedor,
            command.CodigoRuta,
            command.SemanaEmbarque,
            numeroPedimento: null,
            tipoOperacion: command.TipoOperacion,
            numeroBl: command.NumeroBl,
            tipoTransporte: command.TipoTransporte,
            agenteAduanalNaviera: command.AgenteAduanalNaviera,
            puertoAduanaEntrada: command.PuertoAduanaEntrada,
            fechaEstimadaArribo: command.FechaEstimadaArribo,
            certificadoOrigen: command.CertificadoOrigen);

        oc.ActualizarInformacionImportacion(info.EsVacio ? null : info);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
