using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Domain.Ports.OrdenCompra;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Infrastructure.Oc.Ports;

/// <summary>
/// Implementación real (F5-PR3) de
/// <see cref="IGenerarSolicitudCompraPort"/>: cuando una RQ bifurca y
/// genera saldo no cubierto por almacén, crea una <see cref="OrdenCompra"/>
/// real en <see cref="EstadoOrdenCompra.Borrador"/> con cabecera mínima
/// (sentinels TBD para proveedor / condiciones / uso / almacén), líneas
/// con FK a la línea de RQ origen, y compromete la RQ en la misma TX.
///
/// <para>
/// Reemplaza el stub <c>InMemoryGenerarSolicitudCompraPort</c>. La tabla
/// <c>compras.oc_borrador_stub</c> queda deprecada y vacía tras esta
/// migración (no se borra físicamente — la limpieza física entra en una
/// migración posterior con verificación de que ninguna fila quedó).
/// </para>
///
/// <para>
/// La generación del folio es atómica vía upsert sobre
/// <c>compras.folio_secuencias_oc</c>, idéntico al usado por
/// <c>CrearOrdenCompraVaciaHandler</c> y <c>CrearOrdenCompraDesdeRequisicionHandler</c>.
/// </para>
///
/// <para>
/// PLATFORM-TODO(<![CDATA[<CatalogoSucursales>]]>): el código de sucursal
/// usado en el folio se extrae parseando el folio de la RQ origen
/// (primeros 2-4 chars mayúsculos antes del año). Cuando exista el
/// catálogo de sucursales, hacer lookup directo por <c>SucursalId</c>.
/// </para>
/// </summary>
public sealed partial class GenerarSolicitudCompraDesdeRqPort : IGenerarSolicitudCompraPort
{
    private const string SucursalCodigoPattern = @"^([A-Z]{2,4})\d{4}-\d{6}$";

    [GeneratedRegex(SucursalCodigoPattern, RegexOptions.CultureInvariant)]
    private static partial Regex SucursalCodigoRegex();

    private readonly ComprasDbContext _db;
    private readonly IClock _clock;
    private readonly IArticuloReadPort _articulos;
    private readonly ILogger<GenerarSolicitudCompraDesdeRqPort> _logger;

    public GenerarSolicitudCompraDesdeRqPort(
        ComprasDbContext db,
        IClock clock,
        IArticuloReadPort articulos,
        ILogger<GenerarSolicitudCompraDesdeRqPort> logger)
    {
        _db = db;
        _clock = clock;
        _articulos = articulos;
        _logger = logger;
    }

    public async Task<Guid> GenerarBorradorAsync(
        Guid origenRequisicionId,
        IReadOnlyList<LineaSaldo> saldoNoCubierto,
        CancellationToken cancellationToken)
    {
        if (saldoNoCubierto.Count == 0)
        {
            throw new BusinessRuleException(
                "OC_BORRADOR_DESDE_RQ_SIN_LINEAS",
                "No se puede crear un borrador OC sin líneas de saldo.");
        }

        var rq = await _db.Requisiciones
            .Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == origenRequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición origen '{origenRequisicionId}'.");

        var anio = (short)rq.FechaSolicitud.Year;
        var sucursalCodigo = ExtraerSucursalCodigo(rq.Folio.Valor);

        var siguiente = await GetNextFolioSequenceAsync(
            rq.EmpresaId, rq.SucursalId, anio, cancellationToken);
        var folioStr = $"OC-{sucursalCodigo}{anio}-{siguiente:D6}";
        var folio = Millet.Compras.Domain.Oc.Folio.Parse(folioStr);

        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: rq.EmpresaId,
            folio: folio,
            folioAnio: anio,
            proveedorId: OrdenCompraTbdSentinels.Proveedor,
            sucursalDestinoId: rq.SucursalId,
            condicionesPagoId: OrdenCompraTbdSentinels.CondicionesPago,
            usoPrincipalId: OrdenCompraTbdSentinels.UsoPrincipal,
            compradorTitularId: rq.CreadorId,
            encargadoComprasId: rq.CreadorId,
            fechaDocumento: _clock.HoyLocal(),
            sinRequisicionPrevia: false);

        // GAP-9: resolver naturaleza en batch — las líneas de servicio se
        // excluyen del sub-estado de Recepción. Artículo no encontrado → false.
        var articulos = await _articulos.ObtenerPorIdsAsync(
            saldoNoCubierto.Select(s => s.ArticuloId).Distinct().ToArray(),
            cancellationToken);

        foreach (var saldo in saldoNoCubierto)
        {
            var lineaRq = rq.Lineas.FirstOrDefault(l => l.Id == saldo.LineaRequisicionId)
                ?? throw new EntityNotFoundException(
                    "LINEA_REQUISICION_NO_ENCONTRADA",
                    $"La RQ '{rq.Folio.Valor}' no contiene la línea '{saldo.LineaRequisicionId}'.");

            oc.AgregarLineaDesdeRequisicion(
                lineaId: Guid.CreateVersion7(),
                articuloId: saldo.ArticuloId,
                cantidad: saldo.CantidadSaldo,
                unidadMedida: saldo.UnidadMedida,
                precioUnitario: saldo.PrecioEstimado.Amount,
                departamentoSolicitanteId: rq.DepartamentoId,
                requisicionId: rq.Id,
                lineaRequisicionId: lineaRq.Id,
                esServicio: articulos.TryGetValue(saldo.ArticuloId, out var articulo)
                    && articulo.EsServicio);
        }

        rq.ComprometerEnOc(oc.Id);

        _db.OrdenesCompra.Add(oc);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "OC borrador mínimo creado desde RQ. RqId={RqId} OcId={OcId} Folio={Folio} LineasSaldo={Lineas}",
            origenRequisicionId, oc.Id, oc.Folio.Valor, saldoNoCubierto.Count);

        return oc.Id;
    }

    private static string ExtraerSucursalCodigo(string folioRq)
    {
        var match = SucursalCodigoRegex().Match(folioRq);
        return match.Success
            ? match.Groups[1].Value
            : "MID";
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
