using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using CabeceraRow = Millet.Integraciones.Aw.Infrastructure.Pedidos.AwSolicitudesSqlReader.CabeceraRow;
using LineaRow = Millet.Integraciones.Aw.Infrastructure.Pedidos.AwSolicitudesSqlReader.LineaRow;
using ComponenteRow = Millet.Integraciones.Aw.Infrastructure.Pedidos.AwSolicitudesSqlReader.ComponenteRow;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.OrigenPg;

/// <summary>Lector PostgreSQL de la copia de demo: solicitudes y datos de pedidos. Comparte solo el mapper puro con el lector real.</summary>
public sealed class AwSolicitudesPgReader : IAwSolicitudesReader
{
    private readonly IIntegracionSqlConnectionFactory _connectionFactory;
    private readonly ISucursalPorClaveAwResolver _sucursales;
    private readonly ICanalVentaPorClaveAwResolver _canalesVenta;
    private readonly AwPedidosOptions _options;
    private readonly ILogger<AwSolicitudesPgReader> _logger;

    public AwSolicitudesPgReader(
        IIntegracionSqlConnectionFactory connectionFactory,
        ISucursalPorClaveAwResolver sucursales,
        ICanalVentaPorClaveAwResolver canalesVenta,
        IOptions<AwPedidosOptions> options,
        ILogger<AwSolicitudesPgReader> logger)
    {
        _connectionFactory = connectionFactory;
        _sucursales = sucursales;
        _canalesVenta = canalesVenta;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SolicitudAw>> LeerPendientesAsync(
        int max, CancellationToken cancellationToken)
    {
        // Pendiente = resultado NULL o Pospuesta(3), en orden (pedido, version)
        // — doc 04 §2. El índice IX_asp_barrido cubre exactamente este scan.
        const string sql = """
            SELECT solicitud_id, numero_pedido, operacion, version, creada_at
              FROM dbo.aw_solicitud_pedido
             WHERE resultado IS NULL OR resultado = 3
             ORDER BY numero_pedido, version
             LIMIT @max
            """;

        return await PgPlumbing.EjecutarAsync(_connectionFactory, _options, _logger, "LeerPendientes", async connection =>
        {
            using var command = PgPlumbing.CrearCommand(connection, sql, _options);
            PgPlumbing.Param(command, "@max", DbType.Int32, max);

            var rows = new List<SolicitudAw>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new SolicitudAw(
                    SolicitudId: reader.GetGuid(0),
                    NumeroPedido: reader.GetString(1),
                    Operacion: (OperacionAw)Convert.ToByte(reader.GetValue(2)),
                    Version: reader.GetInt64(3),
                    CreadaAt: reader.GetFieldValue<DateTimeOffset>(4)));
            }
            return (IReadOnlyList<SolicitudAw>)rows;
        }, cancellationToken);
    }

    public async Task<LecturaPedidoAw> LeerDatosPedidoAsync(
        string numeroPedido, CancellationToken cancellationToken)
    {
        var (cabecera, lineas, componentes) = await PgPlumbing.EjecutarAsync(_connectionFactory, _options, _logger, "LeerDatosPedido", async connection =>
        {
            var cab = await LeerCabeceraAsync(connection, numeroPedido, cancellationToken);
            if (cab is null)
                return ((CabeceraRow?)null, (IReadOnlyList<LineaRow>)Array.Empty<LineaRow>(),
                    (IReadOnlyList<ComponenteRow>)Array.Empty<ComponenteRow>());
            var lin = await LeerLineasAsync(connection, numeroPedido, cancellationToken);
            var com = await LeerComponentesAsync(connection, numeroPedido, cancellationToken);
            return (cab, lin, com);
        }, cancellationToken);

        if (cabecera is null)
        {
            _logger.LogWarning(
                "[AwSolicitudesPgReader] pedido {Pedido} sin cabecera en vw_erp_pedido_cabecera → rechazo",
                numeroPedido);
            return LecturaPedidoAw.DatosInvalidos(MotivoExcepcion.Otro,
                "pedido sin cabecera en vw_erp_pedido_cabecera");
        }

        // La sucursal se resuelve contra compartido.sucursales.clave_aw
        // (rediseño 2026-07-07, doc 04 §4) — la relación es administrable
        // en /admin/empresas sin redeploy ni app settings.
        Guid? sucursalId = cabecera.NumeroSucursal is null
            ? null
            : await _sucursales.ResolverAsync(cabecera.NumeroSucursal, cancellationToken);
        if (sucursalId is null)
        {
            _logger.LogWarning(
                "[AwSolicitudesPgReader] pedido {Pedido}: numero_sucursal '{Valor}' sin sucursal activa con esa clave_aw en el catálogo → pospuesta",
                numeroPedido, cabecera.NumeroSucursal);
            return LecturaPedidoAw.EsperaConfig(MotivoExcepcion.SucursalSinClaveAw,
                $"numero_sucursal '{cabecera.NumeroSucursal}' sin sucursal activa con esa clave_aw en el catálogo");
        }

        // El canal se resuelve igual: canal_ventas trae el GRUPPE crudo de
        // A+W (la vista solo normaliza el caso EDI → 'Ventas Internacionales')
        // y machea contra compartido.canales_venta.clave_aw (FAC-ING-PR2).
        short? canalVentaId = cabecera.CanalVentas is null
            ? null
            : await _canalesVenta.ResolverAsync(cabecera.CanalVentas, cancellationToken);
        if (canalVentaId is null)
        {
            _logger.LogWarning(
                "[AwSolicitudesPgReader] pedido {Pedido}: canal_ventas '{Valor}' sin canal activo con esa clave_aw en el catálogo → pospuesta",
                numeroPedido, cabecera.CanalVentas);
            return LecturaPedidoAw.EsperaConfig(MotivoExcepcion.CanalVentaSinClaveAw,
                $"canal_ventas '{cabecera.CanalVentas}' sin canal activo con esa clave_aw en el catálogo");
        }

        return AwSolicitudesSqlReader.Construir(cabecera, lineas, componentes, sucursalId.Value, canalVentaId.Value, _options, _logger);
    }

    // ------------------------------------------------------------------
    // Queries sobre las vistas (contrato de columnas congelado, doc 04 §3)
    // ------------------------------------------------------------------

    private async Task<CabeceraRow?> LeerCabeceraAsync(
        DbConnection connection, string numeroPedido, CancellationToken ct)
    {
        const string sql = """
            SELECT numero_pedido, numero_sucursal, cliente_ref, cliente_nombre,
                   rfc_cliente, uso_cfdi, metodo_pago, forma_pago, condicion_pago,
                   divisa, obra_id, obra_nombre, notas_pedido, canal_ventas,
                   clase, fecha_transaccion, pedido_sustituido_numero,
                   estado_origen, total_cantidad, total_m2, importe_total,
                   iva_porcentaje, ranura
              FROM dbo.vw_erp_pedido_cabecera
             WHERE numero_pedido = @np
            """;
        using var command = PgPlumbing.CrearCommand(connection, sql, _options);
        PgPlumbing.Param(command, "@np", DbType.String, numeroPedido);

        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return new CabeceraRow(
            NumeroPedido: reader.GetString(0),
            NumeroSucursal: GetStringOrNull(reader, 1),
            ClienteRef: reader.GetString(2),
            ClienteNombre: GetStringOrNull(reader, 3),
            RfcCliente: GetStringOrNull(reader, 4),
            UsoCfdi: GetStringOrNull(reader, 5),
            MetodoPago: GetStringOrNull(reader, 6),
            FormaPago: GetStringOrNull(reader, 7),
            CondicionPago: GetStringOrNull(reader, 8),
            Divisa: GetStringOrNull(reader, 9) ?? "MXN",
            ObraId: reader.IsDBNull(10) ? null : Convert.ToInt64(reader.GetValue(10)),
            ObraNombre: GetStringOrNull(reader, 11),
            NotasPedido: GetStringOrNull(reader, 12),
            CanalVentas: GetStringOrNull(reader, 13),
            Clase: GetStringOrNull(reader, 14),
            FechaTransaccion: reader.IsDBNull(15) ? null : DateOnly.FromDateTime(reader.GetDateTime(15)),
            PedidoSustituidoNumero: reader.IsDBNull(16) ? null : Convert.ToInt64(reader.GetValue(16)),
            EstadoOrigen: GetStringOrNull(reader, 17),
            TotalCantidad: GetDecimalOrNull(reader, 18),
            TotalM2: GetDecimalOrNull(reader, 19),
            ImporteTotal: GetDecimalOrNull(reader, 20),
            IvaPorcentaje: GetDecimalOrNull(reader, 21),
            Ranura: GetDecimalOrNull(reader, 22));
    }

    private async Task<IReadOnlyList<LineaRow>> LeerLineasAsync(
        DbConnection connection, string numeroPedido, CancellationToken ct)
    {
        const string sql = """
            SELECT numero_posicion, producto_ref, descripcion, detalle_procesos,
                   cantidad, unidad_medida, importe_pieza, descuento_porcentaje,
                   descuento, almacen_nivel_1, almacen_nivel_2, almacen_nivel_3,
                   almacen_nivel_4, almacen_id_ubicacion, requiere_pedimento
              FROM dbo.vw_erp_pedido_linea
             WHERE numero_pedido = @np
             ORDER BY numero_posicion
            """;
        using var command = PgPlumbing.CrearCommand(connection, sql, _options);
        PgPlumbing.Param(command, "@np", DbType.String, numeroPedido);

        var rows = new List<LineaRow>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new LineaRow(
                NumeroPosicion: Convert.ToInt32(reader.GetValue(0)),
                ProductoRef: reader.GetString(1),
                Descripcion: GetStringOrNull(reader, 2) ?? string.Empty,
                DetalleProcesos: GetStringOrNull(reader, 3),
                Cantidad: reader.GetDecimal(4),
                UnidadMedida: GetStringOrNull(reader, 5) ?? string.Empty,
                ImportePieza: reader.GetDecimal(6),
                DescuentoPorcentaje: GetDecimalOrNull(reader, 7) ?? 0m,
                Descuento: GetDecimalOrNull(reader, 8) ?? 0m,
                AlmacenNivel1: GetStringOrNull(reader, 9),
                AlmacenNivel2: GetStringOrNull(reader, 10),
                AlmacenNivel3: GetStringOrNull(reader, 11),
                AlmacenNivel4: GetStringOrNull(reader, 12),
                AlmacenIdUbicacion: reader.IsDBNull(13) ? null : Convert.ToInt64(reader.GetValue(13)),
                RequierePedimento: reader.IsDBNull(14) ? null : reader.GetBoolean(14)));
        }
        return rows;
    }

    private async Task<IReadOnlyList<ComponenteRow>> LeerComponentesAsync(
        DbConnection connection, string numeroPedido, CancellationToken ct)
    {
        const string sql = """
            SELECT numero_posicion, producto_ref, descripcion, alto_mm, ancho_mm,
                   m2_por_pieza, importe
              FROM dbo.vw_erp_pedido_componente
             WHERE numero_pedido = @np
             ORDER BY numero_posicion
            """;
        using var command = PgPlumbing.CrearCommand(connection, sql, _options);
        PgPlumbing.Param(command, "@np", DbType.String, numeroPedido);

        var rows = new List<ComponenteRow>();
        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new ComponenteRow(
                NumeroPosicion: Convert.ToInt32(reader.GetValue(0)),
                ProductoRef: GetStringOrNull(reader, 1) ?? string.Empty,
                Descripcion: GetStringOrNull(reader, 2),
                AltoMm: GetDecimalOrNull(reader, 3),
                AnchoMm: GetDecimalOrNull(reader, 4),
                M2PorPieza: GetDecimalOrNull(reader, 5),
                Importe: GetDecimalOrNull(reader, 6)));
        }
        return rows;
    }

    private static string? GetStringOrNull(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static decimal? GetDecimalOrNull(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal));

}
