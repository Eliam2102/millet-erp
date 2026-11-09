using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Application.Pinturas.Catalogos;

namespace Millet.Integraciones.Aw.Infrastructure.Pinturas;

/// <summary>
/// Extractor de catálogos maestros desde el ERP para publicación hacia Planta Pintura (<c>MILLET_INTEGRACION</c>).
/// Proyecta clientes con clave numérica, servicios de recubrimiento, empleados y obras del ERP hacia
/// las estructuras requeridas por el Portal de Pinturas (<c>dbo.pinturas_*</c>).
/// </summary>
public sealed class PinturasCatalogosExtractor : IPinturasCatalogosExtractor
{
    private readonly CompartidoDbContext _db;
    private readonly IPinturasObrasSource _obrasSource;
    private readonly ILogger<PinturasCatalogosExtractor> _logger;

    public PinturasCatalogosExtractor(
        CompartidoDbContext db,
        IPinturasObrasSource? obrasSource = null,
        ILogger<PinturasCatalogosExtractor>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _obrasSource = obrasSource ?? NoOpPinturasObrasSource.Instance;
        _logger = logger ?? NullLogger<PinturasCatalogosExtractor>.Instance;
    }

    /// <summary>
    /// Extrae todos los catálogos maestros vigentes proyectados para Planta Pintura.
    /// </summary>
    public async Task<PinturasCatalogosBatch> ExtraerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var clientes = await ExtraerClientesAsync(cancellationToken);
        var servicios = await ExtraerServiciosAsync(cancellationToken);
        var empleados = await ExtraerEmpleadosAsync(cancellationToken);
        var obras = await ExtraerObrasAsync(cancellationToken);

        var batch = new PinturasCatalogosBatch(clientes, servicios, obras, empleados);

        _logger.LogInformation(
            "[PinturasCatalogos] Extracción de catálogos completada con éxito. Clientes={Clientes}, Servicios={Servicios}, Obras={Obras}, Empleados={Empleados}, Total={Total}",
            batch.Clientes.Count,
            batch.Servicios.Count,
            batch.Obras.Count,
            batch.Empleados.Count,
            batch.TotalElementos);

        return batch;
    }

    private async Task<IReadOnlyList<FilaPinturasCliente>> ExtraerClientesAsync(CancellationToken ct)
    {
        var clientesRaw = await _db.Clientes
            .AsNoTracking()
            .Select(c => new
            {
                c.Clave,
                c.ReferenciaExterna,
                c.RazonSocial,
                c.Telefono,
                c.Email,
                c.Estatus
            })
            .ToListAsync(ct);

        var candidatos = new List<(int Clave, string RazonSocial, string? Telefono, string? Email, bool Activo)>();

        foreach (var c in clientesRaw)
        {
            if (int.TryParse(c.ReferenciaExterna, NumberStyles.Integer, CultureInfo.InvariantCulture, out int clave)
                || int.TryParse(c.Clave, NumberStyles.Integer, CultureInfo.InvariantCulture, out clave))
            {
                candidatos.Add((
                    Clave: clave,
                    RazonSocial: c.RazonSocial?.Trim() ?? string.Empty,
                    Telefono: string.IsNullOrWhiteSpace(c.Telefono) ? null : c.Telefono.Trim(),
                    Email: string.IsNullOrWhiteSpace(c.Email) ? null : c.Email.Trim(),
                    Activo: c.Estatus == EstatusCatalogo.Activo
                ));
            }
        }

        var clientes = candidatos
            .GroupBy(c => c.Clave)
            .Select(g => g
                .OrderByDescending(c => c.Activo)
                .ThenBy(c => c.RazonSocial, StringComparer.Ordinal)
                .First())
            .OrderBy(c => c.Clave)
            .Select(c => new FilaPinturasCliente(
                Clave: c.Clave,
                RazonSocial: c.RazonSocial,
                NombreComercial: null,
                Contacto: null,
                Telefono: c.Telefono,
                Celular: null,
                Email: c.Email,
                Activo: c.Activo))
            .ToList();

        return clientes;
    }

    private async Task<IReadOnlyList<FilaPinturasServicio>> ExtraerServiciosAsync(CancellationToken ct)
    {
        var serviciosRaw = await _db.ProductosAw
            .AsNoTracking()
            .Where(p => p.EsServicioRecubrimiento)
            .Select(p => new
            {
                p.ReferenciaExterna,
                p.Descripcion,
                p.Estatus
            })
            .ToListAsync(ct);

        var servicios = serviciosRaw
            .Where(p => !string.IsNullOrWhiteSpace(p.ReferenciaExterna))
            .Select(p => new
            {
                Clave = p.ReferenciaExterna.Trim(),
                Nombre = p.Descripcion?.Trim() ?? string.Empty,
                Activo = p.Estatus == EstatusCatalogo.Activo
            })
            .GroupBy(p => p.Clave.ToUpperInvariant())
            .Select(g => g
                .OrderByDescending(p => p.Activo)
                .ThenBy(p => p.Clave, StringComparer.Ordinal)
                .First())
            .OrderBy(p => p.Clave, StringComparer.OrdinalIgnoreCase)
            .Select(p => new FilaPinturasServicio(
                Clave: p.Clave,
                Nombre: p.Nombre,
                Activo: p.Activo))
            .ToList();

        return servicios;
    }

    private async Task<IReadOnlyList<FilaPinturasEmpleado>> ExtraerEmpleadosAsync(CancellationToken ct)
    {
        var empleadosRaw = await _db.Empleados
            .AsNoTracking()
            .Select(e => new
            {
                e.Clave,
                e.Nombre,
                e.Estatus
            })
            .ToListAsync(ct);

        var empleados = empleadosRaw
            .Where(e => !string.IsNullOrWhiteSpace(e.Clave))
            .Select(e => new
            {
                Clave = e.Clave.Trim(),
                Nombre = e.Nombre?.Trim() ?? string.Empty,
                Activo = e.Estatus == EstatusCatalogo.Activo
            })
            .GroupBy(e => e.Clave, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(e => e.Activo)
                .ThenBy(e => e.Nombre, StringComparer.Ordinal)
                .First())
            .OrderBy(e => e.Clave, StringComparer.OrdinalIgnoreCase)
            .Select(e => new FilaPinturasEmpleado(
                Clave: e.Clave,
                Nombre: e.Nombre,
                Activo: e.Activo))
            .ToList();

        return empleados;
    }

    private async Task<IReadOnlyList<FilaPinturasObra>> ExtraerObrasAsync(CancellationToken ct)
    {
        if (_obrasSource is null)
        {
            return [];
        }

        var obras = await _obrasSource.ObtenerObrasAsync(ct);
        if (obras is null || obras.Count == 0)
        {
            return [];
        }

        var obrasDeduplicadas = obras
            .Where(o => !string.IsNullOrWhiteSpace(o.Clave))
            .GroupBy(o => o.Clave.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(o => o.Activo)
                .ThenBy(o => o.Nombre, StringComparer.Ordinal)
                .First())
            .OrderBy(o => o.Clave, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return obrasDeduplicadas;
    }
}
