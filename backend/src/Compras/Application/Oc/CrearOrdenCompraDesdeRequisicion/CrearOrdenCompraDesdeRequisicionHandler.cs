using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
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
    private readonly ComprasDbContext _db;
    private readonly CompartidoDbContext _compartido;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;
    private readonly IPublisher _publisher;
    private readonly IArticuloReadPort _articulos;

    public CrearOrdenCompraDesdeRequisicionHandler(
        ComprasDbContext db,
        CompartidoDbContext compartido,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IClock clock,
        IPublisher publisher,
        IArticuloReadPort articulos)
    {
        _db = db;
        _compartido = compartido;
        _currentUser = currentUser;
        _currentEmpresa = currentEmpresa;
        _clock = clock;
        _publisher = publisher;
        _articulos = articulos;
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

        // C10 — proveedor activo cross-table.
        var proveedor = await _compartido.Proveedores
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                $"No se encontró proveedor con id '{command.ProveedorId}'.");
        if (proveedor.Estatus != EstatusCatalogo.Activo)
        {
            throw new BusinessRuleException(
                "PROVEEDOR_INACTIVO",
                $"El proveedor '{proveedor.Clave}' está {proveedor.Estatus}.");
        }

        // Folio atómico.
        var siguiente = await GetNextFolioSequenceAsync(
            empresaId, rq.SucursalId, command.FolioAnio, cancellationToken);
        var folioStr = $"OC-{command.SucursalCodigo}{command.FolioAnio}-{siguiente:D6}";
        var folio = Millet.Compras.Domain.Oc.Folio.Parse(folioStr);

        // Crear OC con sucursal heredada de la RQ.
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: folio,
            folioAnio: command.FolioAnio,
            proveedorId: command.ProveedorId,
            sucursalDestinoId: rq.SucursalId,
            condicionesPagoId: command.CondicionesPagoId,
            usoPrincipalId: command.UsoPrincipalId,
            compradorTitularId: userId,
            encargadoComprasId: userId,
            fechaDocumento: command.FechaDocumento,
            moneda: command.Moneda,
            tipoCambio: command.TipoCambio,
            sinRequisicionPrevia: false,
            esImportacion: command.EsImportacion,
            observaciones: command.Observaciones,
            fechaEntregaEsperada: command.FechaEntregaEsperada);

        // Heredar solo las líneas con saldo de compra; la cantidad de la
        // línea OC es CantidadDeCompra (lo que el comprador debe pedir al
        // proveedor), no Cantidad original (que incluye la parte cubierta
        // por almacén).
        // GAP-9: resolver naturaleza en batch — las líneas de servicio se
        // excluyen del sub-estado de Recepción. Artículo no encontrado → false.
        var articulos = await _articulos.ObtenerPorIdsAsync(
            lineasDeCompra.Select(l => l.ArticuloId).Distinct().ToArray(),
            cancellationToken);

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
        }

        // Comprometer RQ en la misma TX.
        rq.ComprometerEnOc(oc.Id);

        _db.OrdenesCompra.Add(oc);
        await _db.SaveChangesAsync(cancellationToken);

        // Publish post-commit.
        await _publisher.Publish(
            new RqComprometidaEnOcEvent(
                RequisicionId: rq.Id,
                OrdenCompraId: oc.Id,
                EmpresaId: empresaId,
                OcurridoEn: _clock.UtcNow),
            cancellationToken);

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
