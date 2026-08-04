using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Compras.Application.Oc.Lineas.AgregarLineaDesdeRequisicion;

/// <summary>
/// Handler del flujo §4.2 (agregar líneas desde RQ a OC existente).
/// Valida sucursal coincide, RQ Autorizada + no comprometida, OC en
/// estado editable, OC no tiene SinRequisicionPrevia=true. Hereda
/// líneas, marca la RQ como comprometida, publica el evento.
/// </summary>
public sealed class AgregarLineaDesdeRequisicionHandler
    : IRequestHandler<AgregarLineaDesdeRequisicionCommand, AgregarLineaDesdeRequisicionResponse>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;
    private readonly IPublisher _publisher;
    private readonly IDecimalesUnidadGuard _decimalesGuard;
    private readonly IArticuloReadPort _articulos;

    public AgregarLineaDesdeRequisicionHandler(
        ComprasDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        IClock clock,
        IPublisher publisher,
        IDecimalesUnidadGuard decimalesGuard,
        IArticuloReadPort articulos)
    {
        _db = db;
        _currentEmpresa = currentEmpresa;
        _clock = clock;
        _publisher = publisher;
        _decimalesGuard = decimalesGuard;
        _articulos = articulos;
    }

    public async Task<AgregarLineaDesdeRequisicionResponse> Handle(
        AgregarLineaDesdeRequisicionCommand command,
        CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}'.");

        var rq = await _db.Requisiciones
            .Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == command.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{command.RequisicionId}'.");

        // Decisión 2026-05-13: aceptamos RQs en EnSurtido (bifurcadas) en
        // lugar de Autorizada — alineado con setting AutoGenerarOcAlAutorizar
        // OFF. Ver doc 01-diseno OC §3.bis.1 actualizado.
        if (rq.Estado != EstadoRequisicion.EnSurtido)
        {
            throw new BusinessRuleException(
                "RQ_NO_EN_SURTIDO",
                $"Solo se pueden consolidar RQs en estado EnSurtido (actual: {rq.Estado}).");
        }
        if (rq.ComprometidaEnOcId is not null)
        {
            throw new BusinessRuleException(
                "RQ_YA_COMPROMETIDA",
                $"La RQ '{rq.Folio.Valor}' ya está comprometida en otra OC.");
        }
        if (rq.SucursalId != oc.SucursalDestinoId)
        {
            throw new BusinessRuleException(
                "RQ_SUCURSAL_INCOMPATIBLE",
                $"La RQ '{rq.Folio.Valor}' es de la sucursal '{rq.SucursalId}'; la OC es de '{oc.SucursalDestinoId}' (§10.5).");
        }
        if (rq.Lineas.Count == 0)
        {
            throw new BusinessRuleException(
                "RQ_SIN_LINEAS",
                $"La RQ '{rq.Folio.Valor}' no tiene líneas.");
        }

        // Filtrar líneas con saldo de compra; las cubiertas por almacén
        // no se duplican en la OC (su movimiento de salida ya ocurrió al
        // autorizar).
        var lineasDeCompra = rq.Lineas
            .Where(l => l.CantidadDeCompra > 0)
            .OrderBy(l => l.Posicion)
            .ToList();
        if (lineasDeCompra.Count == 0)
        {
            throw new BusinessRuleException(
                "RQ_SIN_SALDO_DE_COMPRA",
                $"La RQ '{rq.Folio.Valor}' no tiene líneas con CantidadDeCompra > 0.");
        }

        // ADR-0046 Etapa 2: valida los decimales de cada línea heredada contra
        // la unidad de su artículo (defensivo: las líneas de RQ ya se validaron
        // al capturarse). FK NULL → no valida. Un solo round-trip.
        await _decimalesGuard.ValidarAsync(
            lineasDeCompra.Select(l => new CantidadAValidar(l.ArticuloId, l.CantidadDeCompra, l.UnidadMedida)),
            cancellationToken);

        // GAP-9: resolver naturaleza en batch — las líneas de servicio se
        // excluyen del sub-estado de Recepción. Artículo no encontrado → false.
        var articulos = await _articulos.ObtenerPorIdsAsync(
            lineasDeCompra.Select(l => l.ArticuloId).Distinct().ToArray(),
            cancellationToken);

        var lineasAgregadas = 0;
        foreach (var lineaRq in lineasDeCompra)
        {
            oc.AgregarLineaDesdeRequisicion(
                lineaId: Guid.CreateVersion7(),
                articuloId: lineaRq.ArticuloId,
                cantidad: lineaRq.CantidadDeCompra,
                unidadMedida: lineaRq.UnidadMedida,
                precioUnitario: lineaRq.PrecioEstimado.Amount,
                departamentoSolicitanteId: rq.DepartamentoId,
                requisicionId: rq.Id,
                lineaRequisicionId: lineaRq.Id,
                // Fase E PR3: el CC-Máquina se hereda 1:1 de la línea de RQ.
                centroCostoId: lineaRq.CentroCostoId,
                esServicio: articulos.TryGetValue(lineaRq.ArticuloId, out var articulo)
                    && articulo.EsServicio);
            lineasAgregadas++;
        }

        rq.ComprometerEnOc(oc.Id);
        await _db.SaveChangesAsync(cancellationToken);

        await _publisher.Publish(
            new RqComprometidaEnOcEvent(
                RequisicionId: rq.Id,
                OrdenCompraId: oc.Id,
                EmpresaId: empresaId,
                OcurridoEn: _clock.UtcNow),
            cancellationToken);

        return new AgregarLineaDesdeRequisicionResponse(lineasAgregadas);
    }
}
