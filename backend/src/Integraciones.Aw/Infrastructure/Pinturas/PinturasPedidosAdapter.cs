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
/// Lector real de pedidos de Planta Pintura (I13.9): lee las órdenes pendientes (<c>erp_resultado IS NULL</c>) de
/// <c>dbo.pinturas_pedido</c> (cabecera) con su detalle de <c>dbo.pinturas_pedido_linea</c> y escribe el acuse en las 4
/// columnas <c>erp_*</c> de la cabecera (script 16 v3: el ERP tiene SELECT en ambas tablas y UPDATE solo de esas columnas).
/// Sustituye a <c>StubPlantaPinturaPedidosReader</c>.
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

    // Las N cabeceras pendientes más antiguas que ya tienen líneas (el portal inserta cabecera y líneas en una transacción;
    // una cabecera sin líneas no se toma), con todas sus líneas, el nombre del servicio y los datos de la obra.
    internal static string SqlPendientes(bool postgres) =>
        "SELECT p.id, p.folio_orden, p.fecha_pedido, p.cliente_clave, p.cliente_nombre, p.obra_clave, p.obra_nombre, " +
        "p.comentarios, p.iva_porcentaje, p.moneda, o.origen, o.referencia_aw, " +
        "l.linea, l.servicio_clave, s.nombre, l.piezas, l.m2, l.precio_m2 " +
        $"FROM (SELECT {(postgres ? "" : "TOP (@max) ")}c.* FROM dbo.pinturas_pedido c " +
        "WHERE c.erp_resultado IS NULL AND EXISTS (SELECT 1 FROM dbo.pinturas_pedido_linea x WHERE x.pedido_id = c.id) " +
        $"ORDER BY c.creado_at, c.id{(postgres ? " LIMIT @max" : "")}) p " +
        "JOIN dbo.pinturas_pedido_linea l ON l.pedido_id = p.id " +
        "LEFT JOIN dbo.pinturas_servicio s ON s.clave = l.servicio_clave " +
        "LEFT JOIN dbo.pinturas_obra o ON o.clave = p.obra_clave " +
        "ORDER BY p.creado_at, p.id, l.linea";

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
            long? idActual = null;
            List<FilaPinturasLinea>? lineas = null;
            while (await reader.ReadAsync(cancellationToken))
            {
                string? Texto(int i) => reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                decimal Decimal(int i) => Convert.ToDecimal(reader.GetValue(i), CultureInfo.InvariantCulture);
                var id = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
                if (id != idActual)
                {
                    idActual = id;
                    lineas = [];
                    filas.Add(new FilaPinturasPedido(
                        FolioOrden: Texto(1)!,
                        FechaPedido: reader.GetValue(2) is DateOnly fecha
                            ? fecha
                            : DateOnly.FromDateTime(Convert.ToDateTime(reader.GetValue(2), CultureInfo.InvariantCulture)),
                        ClienteClave: Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture),
                        ClienteNombre: Texto(4)!,
                        ObraClave: Texto(5),
                        ObraNombre: Texto(6),
                        Comentarios: Texto(7),
                        IvaPorcentaje: Decimal(8),
                        Moneda: Texto(9)!,
                        ObraOrigen: Texto(10),
                        ObraReferenciaAw: Texto(11),
                        Lineas: lineas));
                }
                lineas!.Add(new FilaPinturasLinea(
                    Linea: Convert.ToInt32(reader.GetValue(12), CultureInfo.InvariantCulture),
                    ServicioClave: Texto(13)!,
                    ServicioNombre: Texto(14),
                    Piezas: reader.IsDBNull(15) ? null : Convert.ToInt32(reader.GetValue(15), CultureInfo.InvariantCulture),
                    M2: Decimal(16),
                    PrecioM2: Decimal(17)));
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
    /// Orden → puerto de Facturación: una línea de venta por línea de la orden (servicio, m2, precio por m2; las piezas
    /// quedan en el payload crudo). <c>cliente_clave</c> (int) viaja como texto para resolverse contra
    /// <c>Cliente.ReferenciaExterna</c>; el IVA llega en porcentaje (16) y el puerto lo espera como fracción (0.16).
    /// La obra solo lleva <c>ObraId</c> cuando viene de A+W (<c>pinturas_obra.referencia_aw</c> = id de <c>KA_OBJEKT</c>).
    /// <c>fecha_pedido</c> es la fecha del documento de venta.
    /// </summary>
    internal static PedidoPlantaPintura Mapear(FilaPinturasPedido f, Guid sucursalId, ComportamientoFiscal comportamiento)
    {
        long? obraId = string.Equals(f.ObraOrigen?.Trim(), "AW", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(f.ObraReferenciaAw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
        var tasaIva = f.IvaPorcentaje / 100m;
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
            Lineas: f.Lineas.OrderBy(l => l.Linea).Select(l =>
            {
                var servicio = l.ServicioClave.Trim();
                return new LineaPedidoPlantaPintura(
                    ProductoRef: servicio,
                    Descripcion: Vacio(l.ServicioNombre) ?? servicio,
                    ClaveProdServSat: null,
                    ClaveUnidadSat: null,
                    Cantidad: l.M2,
                    Precio: l.PrecioM2,
                    Descuento: 0m,
                    RequierePedimento: false,
                    TasaIva: tasaIva);
            }).ToList(),
            PayloadCrudo: JsonSerializer.Serialize(f),
            FechaPedido: f.FechaPedido);
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

/// <summary>Cabecera de <c>dbo.pinturas_pedido</c> con los datos de la obra y sus líneas. Es también el payload crudo.</summary>
internal sealed record FilaPinturasPedido(
    string FolioOrden,
    DateOnly FechaPedido,
    long ClienteClave,
    string ClienteNombre,
    string? ObraClave,
    string? ObraNombre,
    string? Comentarios,
    decimal IvaPorcentaje,
    string Moneda,
    string? ObraOrigen,
    string? ObraReferenciaAw,
    IReadOnlyList<FilaPinturasLinea> Lineas);

/// <summary>Línea de <c>dbo.pinturas_pedido_linea</c> con el nombre del servicio.</summary>
internal sealed record FilaPinturasLinea(
    int Linea,
    string ServicioClave,
    string? ServicioNombre,
    int? Piezas,
    decimal M2,
    decimal PrecioM2);

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
