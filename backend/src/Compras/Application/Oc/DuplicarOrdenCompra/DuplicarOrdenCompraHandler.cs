using Millet.CentrosCosto.Application.PublicPorts;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Serilog.Context;

namespace Millet.Compras.Application.Oc.DuplicarOrdenCompra;

/// <summary>Duplica el faltante no recibido y conserva el vínculo a RQ para consumir su saldo.</summary>
public sealed class DuplicarOrdenCompraHandler
    : IRequestHandler<DuplicarOrdenCompraCommand, DuplicarOrdenCompraResponse>
{
    private static readonly EstadoOrdenCompra[] EstadosDuplicables =
    [
        EstadoOrdenCompra.Cancelada,
        EstadoOrdenCompra.Rechazada,
    ];

    private readonly IDim3ElegibilidadPort _dim3;
    private readonly ComprasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;
    private readonly IPublisher _publisher;

    public DuplicarOrdenCompraHandler(
        ComprasDbContext db,
        IDim3ElegibilidadPort dim3,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IClock clock,
        IPublisher publisher)
    {
        _db = db;
        _dim3 = dim3;
        _currentUser = currentUser;
        _currentEmpresa = currentEmpresa;
        _clock = clock;
        _publisher = publisher;
    }

    public async Task<DuplicarOrdenCompraResponse> Handle(
        DuplicarOrdenCompraCommand command, CancellationToken cancellationToken)
    {
        using var _origenId = LogContext.PushProperty("OrdenCompraOrigenId", command.OrdenCompraOrigenId);
        using var activity = ComprasActivitySource.Instance.StartActivity("Compras.DuplicarOc");
        activity?.SetTag("compras.oc.origen.id", command.OrdenCompraOrigenId);

        if (_currentUser.UserId is not Guid userId)
        {
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        }
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        await using var tx = await SaldoCompraRq.BloquearAsync(_db, cancellationToken);

        var origen = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraOrigenId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró OC origen '{command.OrdenCompraOrigenId}'.");

        if (!EstadosDuplicables.Contains(origen.Estado))
        {
            throw new BusinessRuleException(
                "OC_DUPLICAR_ESTADO_NO_PERMITIDO",
                $"Solo se pueden duplicar OCs Canceladas o Rechazadas (origen: {origen.Estado}).");
        }

        var lineasPendientes = origen.Lineas.Where(l => l.Cantidad > l.CantidadRecibida)
            .OrderBy(l => l.Posicion).ToList();
        if (lineasPendientes.Count == 0)
            throw new BusinessRuleException("OC_SIN_SALDO_PARA_DUPLICAR", "La orden ya fue recibida por completo; no hay cantidades pendientes para duplicar.");
        if (origen.Estado == EstadoOrdenCompra.Rechazada && origen.Lineas.Any(l => l.LineaRequisicionId.HasValue))
            throw new BusinessRuleException("OC_RECHAZADA_RQ_COMPROMETIDA",
                "La OC rechazada conserva el compromiso de la requisición y no puede duplicarse. Corrige esta OC y vuelve a enviarla a autorización.");
        var saldos = await SaldoCompraRq.ObtenerAsync(_db,
            lineasPendientes.Where(l => l.LineaRequisicionId.HasValue).Select(l => l.LineaRequisicionId!.Value), cancellationToken);
        var rqIds = lineasPendientes.Where(l => l.RequisicionId.HasValue).Select(l => l.RequisicionId!.Value).Distinct().ToArray();
        var rqs = await _db.Requisiciones.Where(r => rqIds.Contains(r.Id)).ToListAsync(cancellationToken);
        if (rqs.Any(r => r.ComprometidaEnOcId.HasValue))
            throw new BusinessRuleException("RQ_YA_COMPROMETIDA", "Una requisición de la OC ya está comprometida en otra orden de compra.");
        foreach (var linea in lineasPendientes)
        {
            if (linea.CentroCostoId is not Guid ccId)
                throw new BusinessRuleException("CECO_INVALIDO", "La línea requiere un centro de costo vigente y dentro de tu alcance.");
            await CentroCostoLineaGuard.ValidarAsync(_dim3, ccId, aplicarAlcance: true, cancellationToken);
            if (linea.LineaRequisicionId is Guid rqLineaId)
            {
                SaldoCompraRq.Validar(linea.Cantidad - linea.CantidadRecibida, saldos.GetValueOrDefault(rqLineaId));
                saldos[rqLineaId] -= linea.Cantidad - linea.CantidadRecibida;
            }
        }

        var siguiente = await GetNextFolioSequenceAsync(
            empresaId, origen.SucursalDestinoId, command.FolioAnio, cancellationToken);
        var folioStr = $"OC-{command.SucursalCodigo}{command.FolioAnio}-{siguiente:D6}";
        var folio = Folio.Parse(folioStr);

        // Conserva los vínculos a RQ para consumir el saldo del faltante.
        var motivoSinRq = $"Duplicada de OC {origen.Folio.Valor}";
        var nueva = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: folio,
            folioAnio: command.FolioAnio,
            proveedorId: origen.ProveedorId,
            sucursalDestinoId: origen.SucursalDestinoId,
            condicionesPagoId: origen.CondicionesPagoId,
            usoPrincipalId: origen.UsoPrincipalId,
            compradorTitularId: userId,
            encargadoComprasId: userId,
            fechaDocumento: command.FechaDocumento,
            moneda: origen.Moneda,
            tipoCambio: origen.TipoCambio,
            sinRequisicionPrevia: origen.SinRequisicionPrevia,
            esImportacion: origen.EsImportacion,
            cotizacionExcepcionada: false,
            observaciones: origen.Observaciones,
            motivoSinRequisicion: motivoSinRq,
            fechaEntregaEsperada: origen.FechaEntregaEsperada,
            ocOrigenId: origen.Id);

        // Heredar info logística e importación si están en el origen.
        if (origen.InformacionLogistica is { } info)
        {
            nueva.ActualizarInformacionLogistica(info);
        }
        if (origen.InformacionImportacion is { } imp)
        {
            nueva.ActualizarInformacionImportacion(imp);
        }

        foreach (var linea in lineasPendientes)
        {
            var cantidad = linea.Cantidad - linea.CantidadRecibida;
            if (linea.RequisicionId is Guid rqId && linea.LineaRequisicionId is Guid lineaRqId)
                nueva.AgregarLineaDesdeRequisicion(Guid.CreateVersion7(), linea.ArticuloId,
                    cantidad, linea.UnidadMedida, linea.PrecioUnitario, linea.DepartamentoSolicitanteId,
                    rqId, lineaRqId, linea.Descuento, linea.IndicadorImpuestos,
                    linea.DescripcionExtendida, linea.FechaEntregaLinea, linea.TextoAdicional,
                    linea.EsServicio, linea.CentroCostoId);
            else
                nueva.AgregarLineaManual(Guid.CreateVersion7(), linea.ArticuloId,
                    cantidad, linea.UnidadMedida, linea.PrecioUnitario, linea.DepartamentoSolicitanteId,
                    linea.Descuento, linea.IndicadorImpuestos, linea.DescripcionExtendida,
                    linea.FechaEntregaLinea, linea.TextoAdicional, linea.EsServicio, linea.CentroCostoId);
        }
        foreach (var rq in rqs) rq.ComprometerEnOc(nueva.Id);

        _db.OrdenesCompra.Add(nueva);

        await _publisher.Publish(
            new OrdenCompraDuplicadaEvent(
                OrdenCompraNuevaId: nueva.Id,
                OrdenCompraOrigenId: origen.Id,
                EmpresaId: empresaId,
                FolioNuevo: nueva.Folio.Valor,
                FolioOrigen: origen.Folio.Valor,
                CompradorTitularId: userId,
                OcurridoEn: _clock.UtcNow),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);

        return new DuplicarOrdenCompraResponse(
            OrdenCompraNuevaId: nueva.Id,
            FolioNuevo: nueva.Folio.Valor,
            OrdenCompraOrigenId: origen.Id,
            FolioOrigen: origen.Folio.Valor);
    }

    private async Task<long> GetNextFolioSequenceAsync(
        Guid empresaId, Guid sucursalId, short anio, CancellationToken cancellationToken)
    {
        var result = await _db.Database
            .SqlQuery<long>($@"
                INSERT INTO compras.folio_secuencias_oc (empresa_id, sucursal_id, anio, siguiente)
                VALUES ({empresaId}, {sucursalId}, {anio}, 2)
                ON CONFLICT (empresa_id, sucursal_id, anio) DO UPDATE
                  SET siguiente = compras.folio_secuencias_oc.siguiente + 1
                RETURNING (siguiente - 1)::bigint AS ""Value""
            ")
            .ToListAsync(cancellationToken);
        return result.Single();
    }
}
