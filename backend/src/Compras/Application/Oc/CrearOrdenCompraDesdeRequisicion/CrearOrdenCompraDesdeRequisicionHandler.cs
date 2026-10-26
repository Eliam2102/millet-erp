using Millet.CentrosCosto.Application.PublicPorts;
using MediatR;
using Millet.Compras.Application.Proveedores;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Domain.Cotizaciones;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Blob;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Inicio;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compras.Application.Oc.CrearOrdenCompraDesdeRequisicion;

/// <summary>
/// Handler del flujo §4.1 (creación 1:1 desde RQ). Pasos:
/// <list type="number">
///   <item>Resuelve empresa + comprador del JWT.</item>
///   <item>Lookup RQ con líneas. Valida: estado = Autorizada, no
///         comprometida (ComprometidaEnOcId IS NULL), sucursal coincide
///         con la cabecera.</item>
///   <item>Valida proveedor activo (C10).</item>
///   <item>Genera folio atómico de OC.</item>
///   <item>Crea agregado <see cref="OrdenCompra"/> con datos heredados.</item>
///   <item>Por cada línea de RQ, agrega 1 línea de OC con FK
///         (requisicion_id, linea_requisicion_id) — precio unitario
///         heredado de la RQ.</item>
///   <item>Marca la RQ como comprometida via
///         <see cref="Requisicion.ComprometerEnOc"/>.</item>
///   <item>SaveChanges (1 TX) + publica <c>RqComprometidaEnOcEvent</c>.</item>
/// </list>
/// </summary>
public sealed class CrearOrdenCompraDesdeRequisicionHandler
    : IRequestHandler<CrearOrdenCompraDesdeRequisicionCommand, CrearOrdenCompraDesdeRequisicionResponse>
{
    private readonly IDim3ElegibilidadPort _dim3;
    private readonly Millet.Compras.Domain.Ports.Catalogos.ICatalogoSatReadPort _catalogos;
    private readonly ComprasDbContext _db;
    private readonly CompartidoDbContext _compartido;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;
    private readonly IPublisher _publisher;
    private readonly IArticuloReadPort _articulos;
    private readonly ICurrentUserPermissions _permisos;
    private readonly IBlobStoragePort _blobGenerico;
    private readonly IAlmacenarBlobPort _blobOc;

    public CrearOrdenCompraDesdeRequisicionHandler(
        ComprasDbContext db, Millet.Compras.Domain.Ports.Catalogos.ICatalogoSatReadPort catalogos,
        IDim3ElegibilidadPort dim3,
        CompartidoDbContext compartido,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IClock clock,
        IPublisher publisher,
        IArticuloReadPort articulos,
        ICurrentUserPermissions permisos,
        IBlobStoragePort blobGenerico,
        IAlmacenarBlobPort blobOc)
    {
        _db = db;
        _catalogos = catalogos;
        _dim3 = dim3;
        _compartido = compartido;
        _currentUser = currentUser;
        _currentEmpresa = currentEmpresa;
        _clock = clock;
        _publisher = publisher;
        _articulos = articulos;
        _permisos = permisos;
        _blobGenerico = blobGenerico;
        _blobOc = blobOc;
    }

    public async Task<CrearOrdenCompraDesdeRequisicionResponse> Handle(
        CrearOrdenCompraDesdeRequisicionCommand command,
        CancellationToken cancellationToken)
    {
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

        var ocId = Guid.CreateVersion7();

        await using var tx = await SaldoCompraRq.BloquearAsync(_db, cancellationToken);

        // Lookup RQ con líneas. EF tracking on porque vamos a mutar
        // ComprometidaEnOcId.
        var rq = await _db.Requisiciones
            .Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == command.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{command.RequisicionId}'.");

        // Decisión 2026-05-13: aceptamos RQs en EnSurtido (bifurcadas con
        // CantidadDeCompra > 0 y sin OC comprometida). Las RQs Autorizada
        // que no bifurcaron (caso degenerado defensivo del agregado) NO
        // tienen saldo de compra, así que no aplica este flujo.
        if (rq.Estado != EstadoRequisicion.EnSurtido)
        {
            throw new BusinessRuleException(
                "RQ_NO_EN_SURTIDO",
                $"Solo se pueden convertir a OC RQs en estado EnSurtido (actual: {rq.Estado}).");
        }
        if (rq.ComprometidaEnOcId is not null)
        {
            throw new BusinessRuleException(
                "RQ_YA_COMPROMETIDA",
                $"La RQ '{rq.Folio.Valor}' ya está comprometida en la OC '{rq.ComprometidaEnOcId}'.");
        }
        if (rq.Lineas.Count == 0)
        {
            throw new BusinessRuleException(
                "RQ_SIN_LINEAS",
                $"La RQ '{rq.Folio.Valor}' no tiene líneas — no se puede crear una OC desde ella.");
        }

        // Filtrar líneas que efectivamente van a compra (CantidadDeCompra > 0).
        // El resto cayó a almacén vía reserva + movimiento al autorizar; no
        // se duplica en la OC. Si todas las líneas tienen 0, la RQ no
        // debería haber llegado a EnSurtido — error defensivo.
        var saldos = await SaldoCompraRq.ObtenerAsync(_db, rq.Lineas.Select(l => l.Id), cancellationToken);
        var lineasDeCompra = rq.Lineas
            .Where(l => saldos.GetValueOrDefault(l.Id) > 0)
            .OrderBy(l => l.Posicion)
            .ToList();
        if (lineasDeCompra.Count == 0)
        {
            throw new BusinessRuleException(
                "RQ_SIN_SALDO_DE_COMPRA",
                $"La RQ '{rq.Folio.Valor}' no tiene líneas con CantidadDeCompra > 0.");
        }

        foreach (var ccId in lineasDeCompra.Select(l => l.CentroCostoId).Distinct())
        {
            if (ccId is not Guid id)
                throw new BusinessRuleException("CECO_INVALIDO", "La línea requiere un centro de costo vigente.");
            await CentroCostoLineaGuard.ValidarAsync(_dim3, id, aplicarAlcance: false, cancellationToken);
        }

        if (command.CotizacionId is null)
        {
            await Cotizaciones.CotizacionesComparativo.ExigirSinCotizacionesAsync(_db, rq.Id, cancellationToken);
        }

        // F1-COM-02 (D1): toda validación de la cotización va ANTES del folio y del compromiso de la RQ.
        Cotizacion? cot = null;
        Millet.SharedKernel.Domain.Adjuntos.Adjunto? adjuntoCot = null;
        if (command.CotizacionId is Guid cotId)
        {
            if (!await _permisos.TieneAsync("compras.cotizaciones.gestionar", cancellationToken))
            {
                throw new ForbiddenException("COT_SIN_PERMISO", "Solo Compras puede elegir una cotización.");
            }

            cot = await _db.Cotizaciones.Include(c => c.Lineas)
                    .FirstOrDefaultAsync(c => c.Id == cotId && c.RequisicionId == rq.Id, cancellationToken)
                ?? throw new EntityNotFoundException("COTIZACION_NO_ENCONTRADA",
                    $"No se encontró la cotización '{cotId}' en la requisición '{rq.Folio.Valor}'.");
            if (cot.ProveedorId != command.ProveedorId)
            {
                throw new BusinessRuleException("COT_PROVEEDOR_DISTINTO",
                    "El proveedor de la orden debe ser el de la cotización elegida.");
            }
            if (!string.Equals(cot.Moneda, command.Moneda, StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessRuleException("COT_MONEDA_DISTINTA",
                    "La moneda de la orden debe ser la de la cotización elegida.");
            }
            if (lineasDeCompra.Any(l => cot.Lineas.All(x => x.LineaRequisicionId != l.Id)))
            {
                throw new BusinessRuleException("COT_PARTIDAS_INCOMPLETAS",
                    "La cotización no cubre todas las partidas por comprar de la requisición.");
            }

            adjuntoCot = await _compartido.Adjuntos.AsNoTracking()
                    .Where(a => a.TipoEntidad == "cotizacion_compra" && a.EntidadId == cot.Id && a.BajaEn == null)
                    .OrderByDescending(a => a.SubidoEn)
                    .FirstOrDefaultAsync(cancellationToken)
                ?? throw new BusinessRuleException("COT_SIN_ARCHIVO", "Adjunta el PDF de la cotización antes de elegirla.");

            var hayOtraElegida = await _db.Cotizaciones.AnyAsync(
                c => c.RequisicionId == rq.Id && c.Id != cot.Id && c.OrdenCompraId != null, cancellationToken);

            // Lanza COT_YA_HAY_ELEGIDA, COT_VENCIDA, COT_YA_ELEGIDA, COT_MOTIVO_REQUERIDO.
            cot.Elegir(ocId, command.MotivoEleccion ?? string.Empty, userId, _clock.UtcNow, FechasInicio.Hoy(_clock),
                hayOtraElegida);
        }

        var condicionesPagoId = command.CondicionesPagoId != Guid.Empty
            ? command.CondicionesPagoId : cot?.CondicionesPagoId ?? Guid.Empty;
        if (condicionesPagoId == Guid.Empty)
            throw new BusinessRuleException("CONDICIONES_PAGO_REQUERIDAS", "Selecciona las condiciones de pago de la orden.");
        if (cot is not null)
            await Cotizaciones.PartidasCotizacion.ValidarPagoAsync(_compartido, condicionesPagoId, cancellationToken);

        // C10 — proveedor activo cross-table.
        var proveedor = await _compartido.Proveedores
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                $"No se encontró proveedor con id '{command.ProveedorId}'.");
        if (proveedor.Estatus != EstatusCatalogo.Activo)
        {
            throw ProveedorNoUtilizable.Error(proveedor.Clave, proveedor.Estatus, $"El proveedor '{proveedor.Clave}' está {proveedor.Estatus}.");
        }

        // Folio atómico.
        var siguiente = await GetNextFolioSequenceAsync(
            empresaId, rq.SucursalId, command.FolioAnio, cancellationToken);
        var folioStr = $"OC-{command.SucursalCodigo}{command.FolioAnio}-{siguiente:D6}";
        var folio = Millet.Compras.Domain.Oc.Folio.Parse(folioStr);

        // Crear OC con sucursal heredada de la RQ.
        var oc = new OrdenCompra(
            id: ocId,
            empresaId: empresaId,
            folio: folio,
            folioAnio: command.FolioAnio,
            proveedorId: command.ProveedorId,
            sucursalDestinoId: rq.SucursalId,
            condicionesPagoId: condicionesPagoId,
            usoPrincipalId: command.UsoPrincipalId,
            compradorTitularId: userId,
            encargadoComprasId: userId,
            fechaDocumento: command.FechaDocumento,
            moneda: command.Moneda,
            tipoCambio: command.TipoCambio,
            sinRequisicionPrevia: false,
            esImportacion: command.EsImportacion,
            observaciones: command.Observaciones,
            fechaEntregaEsperada: command.FechaEntregaEsperada ?? cot?.FechaEntrega);

        await DatosFacturaOcGuard.ValidarAsync(_catalogos, command.UsoCfdiClave, command.FormaPagoClave, cancellationToken);
        oc.ActualizarDatosFactura(command.UsoCfdiClave, command.MetodoPago, command.FormaPagoClave);

        // Heredar solo las líneas con saldo de compra; la cantidad de la
        // línea OC es CantidadDeCompra (lo que el comprador debe pedir al
        // proveedor), no Cantidad original (que incluye la parte cubierta
        // por almacén).
        // GAP-9: resolver naturaleza en batch — las líneas de servicio se
        // excluyen del sub-estado de Recepción. Artículo no encontrado → false.
        var articulos = await _articulos.ObtenerPorIdsAsync(
            lineasDeCompra.Select(l => l.ArticuloId).Distinct().ToArray(),
            cancellationToken);

        oc.HeredarObra(rq.Obra);

        foreach (var lineaRq in lineasDeCompra)
        {
            oc.AgregarLineaDesdeRequisicion(
                lineaId: Guid.CreateVersion7(),
                articuloId: lineaRq.ArticuloId,
                cantidad: saldos[lineaRq.Id],
                unidadMedida: lineaRq.UnidadMedida,
                precioUnitario: cot is null ? lineaRq.PrecioEstimado.Amount : cot.PrecioPorUnidadRequisicion(lineaRq.Id),
                departamentoSolicitanteId: rq.DepartamentoId,
                requisicionId: rq.Id,
                lineaRequisicionId: lineaRq.Id,
                // Fase E PR3: el CC-Máquina se hereda 1:1 de la línea de RQ.
                centroCostoId: lineaRq.CentroCostoId, esActivoFijo: lineaRq.EsActivoFijo, claseActivoId: lineaRq.ClaseActivoId,
                esServicio: articulos.TryGetValue(lineaRq.ArticuloId, out var articulo)
                    && articulo.EsServicio);
        }

        // Comprometer RQ en la misma TX.
        rq.ComprometerEnOc(oc.Id);

        if (cot is not null && adjuntoCot is not null)
        {
            // El PDF vive en el servicio genérico (ADR-0058) y la OC sigue usando el adjunto legado:
            // se copia para que el requisito `cotizacion` de transmitir se cumpla sin captura manual (D1).
            var tipoCotizacionId = await _db.TiposDocumentoOc.AsNoTracking()
                .Where(t => t.Clave == "cotizacion").Select(t => t.Id).FirstAsync(cancellationToken);
            var adjuntoOcId = Guid.CreateVersion7();
            string blobUrl;
            await using (var origen = await _blobGenerico.ObtenerStreamAsync(adjuntoCot.BlobRef, cancellationToken))
            {
                blobUrl = await _blobOc.SubirAsync(adjuntoOcId, origen, adjuntoCot.ContentType, adjuntoCot.NombreArchivo, cancellationToken);
            }
            oc.AdjuntarDocumento(adjuntoOcId, tipoCotizacionId, adjuntoCot.NombreArchivo, blobUrl,
                adjuntoCot.ContentType, adjuntoCot.TamanoBytes, _clock.UtcNow, userId);
        }

        _db.OrdenesCompra.Add(oc);

        // Estado y Outbox se guardan en la misma transacción.
        await _publisher.Publish(
            new RqComprometidaEnOcEvent(
                RequisicionId: rq.Id,
                OrdenCompraId: oc.Id,
                EmpresaId: empresaId,
                OcurridoEn: _clock.UtcNow),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);

        return new CrearOrdenCompraDesdeRequisicionResponse(
            OrdenCompraId: oc.Id,
            Folio: oc.Folio.Valor,
            FolioAnio: oc.FolioAnio,
            LineasHeredadas: oc.Lineas.Count);
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
