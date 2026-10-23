using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Ports.Almacen;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.Revision;

/// <summary>
/// P8.4a A3 / CA8.7: decide si una factura de proveedor se detiene por daño en la
/// mercancía (recepción con líneas en MAT-REV) y la envía a revisión con el motivo
/// «Daños» (<c>DANOS_MERCANCIA</c>) para el área Almacén. No libera nada: la
/// liberación es manual por el área. La elegibilidad parcial por línea es G1.4.
/// No llama <c>SaveChangesAsync</c>; lo hace quien lo invoca.
/// </summary>
public sealed class DetencionPorDanoService(
    CuentasPorPagarDbContext db,
    IAlmacenRevisionReadPort almacen,
    IDependenciaRevisoraReadPort dependencias,
    ILogger<DetencionPorDanoService> logger)
{
    public static readonly Guid MotivoDanosId = Guid.Parse("00000007-1001-0000-0000-000000000002");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Ids de línea de OC con material dañado. <see cref="Guid.Empty"/> = línea dañada
    /// sin línea de OC (afecta a toda la OC).
    /// </summary>
    public async Task<IReadOnlySet<Guid>> LineasOcConDanoAsync(
        IReadOnlyCollection<LineaRecepcionPayload> lineas, CancellationToken ct)
    {
        var ids = lineas.Where(l => l.SubAlmacenId is not null).Select(l => l.SubAlmacenId!.Value).Distinct().ToList();
        if (ids.Count == 0) return new HashSet<Guid>();
        var enRevision = await almacen.FiltrarSubAlmacenesEnRevisionAsync(ids, ct);
        return lineas.Where(l => l.SubAlmacenId is Guid s && enRevision.Contains(s))
            .Select(l => l.LineaOcId ?? Guid.Empty).ToHashSet();
    }

    public async Task<IReadOnlySet<Guid>> LineasOcConDanoDeOcAsync(Guid ordenCompraId, CancellationToken ct)
    {
        var recepciones = await db.RecepcionesOcLocal.AsNoTracking()
            .Where(r => r.OrdenCompraId == ordenCompraId).ToListAsync(ct);
        recepciones = recepciones.Concat(db.RecepcionesOcLocal.Local.Where(r => r.OrdenCompraId == ordenCompraId))
            .DistinctBy(r => r.RecepcionId).ToList();
        var lineas = recepciones
            .SelectMany(r => JsonSerializer.Deserialize<List<LineaRecepcionPayload>>(r.LineasJson, JsonOptions) ?? [])
            .ToList();
        return await LineasOcConDanoAsync(lineas, ct);
    }

    public async Task<bool> DetenerSiAplicaAsync(
        Domain.FacturaProveedor.FacturaProveedor factura,
        IReadOnlySet<Guid> lineasOcConDano,
        DateTimeOffset ahora,
        CancellationToken ct)
    {
        if (lineasOcConDano.Count == 0) return false;
        if (factura.Estado is not (EstadoPasivo.Capturada or EstadoPasivo.Autorizada)) return false;
        if (factura.EnRevision)
        {
            logger.LogInformation(
                "Factura {FacturaId} ya está en revisión con otro motivo; no se cambia a Daños.", factura.Id);
            return false;
        }

        // ponytail: sin línea de OC ligada no se puede acotar; se detiene por prudencia (S6).
        var afectada = lineasOcConDano.Contains(Guid.Empty)
            || factura.Lineas.All(l => l.LineaOcId is null)
            || factura.Lineas.Any(l => l.LineaOcId is Guid id && lineasOcConDano.Contains(id));
        if (!afectada) return false;

        var motivo = await db.MotivosRevision.AsNoTracking().FirstOrDefaultAsync(m => m.Id == MotivoDanosId, ct);
        if (motivo is null || !motivo.Activo)
        {
            logger.LogWarning("El motivo de revisión Daños ({MotivoId}) no existe o está inactivo; no se detiene la factura {FacturaId}.",
                MotivoDanosId, factura.Id);
            return false;
        }

        var lista = await dependencias.ListarAsync(ct);
        DependenciaRevisoraDto? Area(string? codigo) => lista.FirstOrDefault(d => d.Activa
            && string.Equals(d.Codigo, codigo, StringComparison.OrdinalIgnoreCase));
        var area = Area(motivo.DependenciaRevisoraDefaultCodigo) ?? Area("CXP");
        var dependenciaId = area?.Id ?? EventListeners.Tesoreria.CancelacionPasivoSolicitadaHandler.DependenciaCxpId;
        if (area is null)
            // PLATFORM-TODO(<DepartamentosRevisores>): crear los departamentos COMPRAS, CXP y DIRECCION_F (admin/Millet).
            logger.LogWarning("No existen los departamentos {Codigo} ni CXP; la factura {FacturaId} se detiene con el área CxP por defecto.",
                motivo.DependenciaRevisoraDefaultCodigo, factura.Id);

        factura.EnviarARevision(MotivoDanosId, dependenciaId, null, ahora);
        logger.LogInformation("Factura {FacturaId} (OC {OrdenCompraId}) detenida por daño en la mercancía.",
            factura.Id, factura.OrdenCompraId);
        return true;
    }
}
