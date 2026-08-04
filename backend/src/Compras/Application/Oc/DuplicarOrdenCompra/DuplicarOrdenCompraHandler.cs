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

/// <summary>
/// Handler de <see cref="DuplicarOrdenCompraCommand"/> (F6-PR2).
/// Valida estado origen (Cancelada o Rechazada), genera folio nuevo,
/// crea OC en Borrador con cabecera heredada + líneas manuales (sin FK
/// a RQ), setea <c>OcOrigenId</c> + publica
/// <see cref="OrdenCompraDuplicadaEvent"/>.
/// </summary>
public sealed class DuplicarOrdenCompraHandler
    : IRequestHandler<DuplicarOrdenCompraCommand, DuplicarOrdenCompraResponse>
{
    private static readonly EstadoOrdenCompra[] EstadosDuplicables =
    [
        EstadoOrdenCompra.Cancelada,
        EstadoOrdenCompra.Rechazada,
    ];

    private readonly ComprasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;
    private readonly IPublisher _publisher;

    public DuplicarOrdenCompraHandler(
        ComprasDbContext db,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IClock clock,
        IPublisher publisher)
    {
        _db = db;
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

        var siguiente = await GetNextFolioSequenceAsync(
            empresaId, origen.SucursalDestinoId, command.FolioAnio, cancellationToken);
        var folioStr = $"OC-{command.SucursalCodigo}{command.FolioAnio}-{siguiente:D6}";
        var folio = Folio.Parse(folioStr);

        // Cabecera heredada. SinRequisicionPrevia=true porque las líneas
        // se copian como manuales — las RQs originales ya fueron liberadas
        // al cancelar la OC origen.
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
            sinRequisicionPrevia: true,
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

        // Líneas como manuales: sin requisicionId / lineaRequisicionId.
        // El comprador re-selecciona si quiere consolidar nuevas RQs.
        foreach (var lineaOrigen in origen.Lineas.OrderBy(l => l.Posicion))
        {
            nueva.AgregarLineaManual(
                lineaId: Guid.CreateVersion7(),
                articuloId: lineaOrigen.ArticuloId,
                cantidad: lineaOrigen.Cantidad,
                unidadMedida: lineaOrigen.UnidadMedida,
                precioUnitario: lineaOrigen.PrecioUnitario,
                departamentoSolicitanteId: lineaOrigen.DepartamentoSolicitanteId,
                descuento: lineaOrigen.Descuento,
                indicadorImpuestos: lineaOrigen.IndicadorImpuestos,
                descripcionExtendida: lineaOrigen.DescripcionExtendida,
                fechaEntregaLinea: lineaOrigen.FechaEntregaLinea,
                textoAdicional: lineaOrigen.TextoAdicional,
                // GAP-9: heredar el snapshot de naturaleza de la línea origen.
                esServicio: lineaOrigen.EsServicio,
                // Fase E PR3.1: copiar el CC-Máquina. Sin esto la duplicada
                // nacía con CC null saltándose el obligatorio (se crea por
                // dominio, no por el comando validado) y perdía el dato en
                // silencio. La línea origen heredada se copia como manual, y
                // su CC pasa a ser editable — correcto: ya no cuelga de la RQ.
                centroCostoId: lineaOrigen.CentroCostoId);
        }

        _db.OrdenesCompra.Add(nueva);
        await _db.SaveChangesAsync(cancellationToken);

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
