using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pinturas;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.Pinturas;

/// <summary>
/// Lector real de pedidos de Planta Pintura (I13.9): lee las filas pendientes (<c>erp_resultado IS NULL</c>) de
/// <c>dbo.pinturas_pedido</c> y escribe el acuse en las 4 columnas <c>erp_*</c> de la misma fila (script 16 v2: el
/// ERP tiene SELECT en la tabla y UPDATE solo de esas columnas). Sustituye a <c>StubPlantaPinturaPedidosReader</c>.
/// Un dialecto por bandera: SQL Server (MILLET_INTEGRACION) o PostgreSQL (copia de demo de <c>tools/aw-origen-demo</c>).
/// No hay reenvío: una orden lleva siempre versión 1 y, si se rechaza, Pinturas captura un folio nuevo (doc integration/11).
/// </summary>
public sealed class PinturasPedidosAdapter : IPlantaPinturaPedidosReader
{
    /// <summary>Largo de <c>erp_error</c> (nvarchar(500)).</summary>
    internal const int LargoError = 500;

    private readonly IIntegracionSqlConnectionFactory _fabrica;
    private readonly bool _postgres;
    private readonly IPinturasSucursalResolver _sucursales;
    private readonly PinturasPedidosOptions _opciones;
    private readonly ILogger<PinturasPedidosAdapter> _logger;

    public PinturasPedidosAdapter(
        IIntegracionSqlConnectionFactory fabrica, bool postgres, IPinturasSucursalResolver sucursales,
        IOptions<PinturasPedidosOptions> opciones, ILogger<PinturasPedidosAdapter> logger)
    {
        _fabrica = fabrica;
        _postgres = postgres;
        _sucursales = sucursales;
        _opciones = opciones.Value;
        _logger = logger;
    }

    internal static string SqlPendientes(bool postgres) =>
        $"SELECT {(postgres ? "" : "TOP (@max) ")}p.folio_orden, p.cliente_clave, p.cliente_nombre, p.obra_clave, p.obra_nombre, " +
        "p.comentarios, p.servicio_clave, s.nombre, p.m2, p.precio_m2, p.iva_porcentaje, p.moneda, o.origen, o.referencia_aw " +
        "FROM dbo.pinturas_pedido p " +
        "LEFT JOIN dbo.pinturas_servicio s ON s.clave = p.servicio_clave " +
        "LEFT JOIN dbo.pinturas_obra o ON o.clave = p.obra_clave " +
        $"WHERE p.erp_resultado IS NULL ORDER BY p.creado_at, p.id{(postgres ? " LIMIT @max" : "")}";

    // Solo filas aún pendientes: un acuse nunca pisa otro (relectura tras un acuse fallido o dos instancias del worker).
    internal const string SqlAcuse =
        "UPDATE dbo.pinturas_pedido SET erp_resultado = @resultado, erp_pedido_id = @pedido, erp_error = @error, " +
        "erp_procesado_at = @fecha WHERE folio_orden = @folio AND erp_resultado IS NULL";

    public async Task<IReadOnlyList<PedidoPlantaPintura>> LeerPendientesAsync(int max, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_opciones.SucursalClave))
        {
            _logger.LogWarning("[Pinturas] {Seccion}:SucursalClave sin configurar: no se leen pedidos de Planta Pintura.",
                PinturasPedidosOptions.SectionName);
            return [];
        }
        var sucursal = await _sucursales.ResolverAsync(_opciones.SucursalClave, cancellationToken);
        if (sucursal is not { } sucursalId)
        {
            _logger.LogWarning("[Pinturas] No hay sucursal activa con clave '{Clave}': no se leen pedidos de Planta Pintura.",
                _opciones.SucursalClave);
            return [];
        }

        var filas = new List<FilaPinturasPedido>();
        await using (var conexion = _fabrica.CreateConnection())
        {
            await conexion.OpenAsync(cancellationToken);
            await using var cmd = Comando(conexion, SqlPendientes(_postgres), ("@max", max));
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string? Texto(int i) => reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                filas.Add(new FilaPinturasPedido(
                    FolioOrden: Texto(0)!,
                    ClienteClave: Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture),
                    ClienteNombre: Texto(2)!,
                    ObraClave: Texto(3),
                    ObraNombre: Texto(4),
                    Comentarios: Texto(5),
                    ServicioClave: Texto(6)!,
                    ServicioNombre: Texto(7),
                    M2: Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture),
                    PrecioM2: Convert.ToDecimal(reader.GetValue(9), CultureInfo.InvariantCulture),
                    IvaPorcentaje: Convert.ToDecimal(reader.GetValue(10), CultureInfo.InvariantCulture),
                    Moneda: Texto(11)!,
                    ObraOrigen: Texto(12),
                    ObraReferenciaAw: Texto(13)));
            }
        }

        return filas.Select(f => Mapear(f, sucursalId, _opciones.ComportamientoFiscal)).ToList();
    }

    public async Task MarcarResultadoAsync(
        string numeroPedido, ResultadoAcusePlantaPintura resultado, Guid? pedidoId, string? detalleError,
        DateTimeOffset fecha, CancellationToken cancellationToken)
    {
        await using var conexion = _fabrica.CreateConnection();
        await conexion.OpenAsync(cancellationToken);
        await using var cmd = Comando(conexion, SqlAcuse,
            ("@resultado", _postgres ? (short)resultado : (byte)resultado),
            ("@pedido", pedidoId),
            ("@error", Recortar(detalleError)),
            ("@fecha", fecha.UtcDateTime),
            ("@folio", numeroPedido));
        var filas = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (filas == 0)
            _logger.LogWarning("[Pinturas] Acuse de {Folio} sin efecto: la orden no existe o ya tenía acuse.", numeroPedido);
    }

    /// <summary>
    /// Fila → puerto de Facturación. <c>cliente_clave</c> (int) viaja como texto para resolverse contra
    /// <c>Cliente.ReferenciaExterna</c>; el IVA llega en porcentaje (16) y el puerto lo espera como fracción (0.16).
    /// La obra solo lleva <c>ObraId</c> cuando viene de A+W (<c>pinturas_obra.referencia_aw</c> = id de <c>KA_OBJEKT</c>).
    /// </summary>
    internal static PedidoPlantaPintura Mapear(FilaPinturasPedido f, Guid sucursalId, ComportamientoFiscal comportamiento)
    {
        long? obraId = string.Equals(f.ObraOrigen?.Trim(), "AW", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(f.ObraReferenciaAw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
        var servicio = f.ServicioClave.Trim();
        return new PedidoPlantaPintura(
            NumeroPedido: f.FolioOrden.Trim(),
            SucursalId: sucursalId,
            ClienteRef: f.ClienteClave.ToString(CultureInfo.InvariantCulture),
            ClienteNombre: f.ClienteNombre.Trim(),
            CanalVenta: CanalesVentaConocidos.PlantaPintura,
            ComportamientoFiscal: comportamiento,
            Moneda: f.Moneda.Trim().ToUpperInvariant(),
            ObraId: obraId,
            ObraNombre: Vacio(f.ObraNombre) ?? Vacio(f.ObraClave),
            Comentarios: Vacio(f.Comentarios),
            VersionOrigen: 1,
            EstadoOrigen: null,
            Lineas:
            [
                new LineaPedidoPlantaPintura(
                    ProductoRef: servicio,
                    Descripcion: Vacio(f.ServicioNombre) ?? servicio,
                    ClaveProdServSat: null,
                    ClaveUnidadSat: null,
                    Cantidad: f.M2,
                    Precio: f.PrecioM2,
                    Descuento: 0m,
                    RequierePedimento: false,
                    TasaIva: f.IvaPorcentaje / 100m),
            ],
            PayloadCrudo: JsonSerializer.Serialize(f));
    }

    internal static string? Recortar(string? error) =>
        error is { Length: > LargoError } ? error[..LargoError] : error;

    private static string? Vacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private DbCommand Comando(DbConnection conexion, string sql, params (string Nombre, object? Valor)[] parametros)
    {
        var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = _opciones.SqlQueryTimeoutSeconds;
        foreach (var (nombre, valor) in parametros)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = nombre;
            p.Value = valor ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
        return cmd;
    }
}

/// <summary>Fila de <c>dbo.pinturas_pedido</c> con el nombre del servicio y los datos de la obra. Es también el payload crudo.</summary>
internal sealed record FilaPinturasPedido(
    string FolioOrden,
    long ClienteClave,
    string ClienteNombre,
    string? ObraClave,
    string? ObraNombre,
    string? Comentarios,
    string ServicioClave,
    string? ServicioNombre,
    decimal M2,
    decimal PrecioM2,
    decimal IvaPorcentaje,
    string Moneda,
    string? ObraOrigen,
    string? ObraReferenciaAw);

/// <summary>Sucursal por <c>compartido.sucursales.clave</c>, solo activas.</summary>
public sealed class PinturasSucursalResolver(CompartidoDbContext db) : IPinturasSucursalResolver
{
    public async Task<Guid?> ResolverAsync(string clave, CancellationToken cancellationToken)
    {
        var c = clave.Trim();
        return await db.Sucursales.AsNoTracking()
            .Where(s => s.Clave == c && s.Estatus == EstatusCatalogo.Activo)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
