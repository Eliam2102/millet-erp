using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Ports;

namespace Millet.Integraciones.Aw.Infrastructure.Pedidos;

/// <summary>
/// Adapter REAL de <see cref="IAwSolicitudesReader"/> (flujo 2, ADR-0048;
/// cierra PLATFORM-TODO(&lt;AwVistaPedidos&gt;)). Lee la tabla-puente
/// <c>dbo.aw_solicitud_pedido</c> y las vistas <c>vw_erp_pedido_*</c> de
/// <c>MILLET_INTEGRACION(_DEV)</c> vía Hybrid Connection (contratos:
/// docs/integration/04 §2–§3).
///
/// <para>
/// Mismo patrón operativo que <c>HybridConnectionAwSqlReader</c> del flujo 1
/// (que NO se toca): connection per-call, timeout duro en OpenAsync,
/// clasificación de errores con <see cref="AwReaderException"/>. El
/// <c>PayloadCrudo</c> del snapshot es la serialización JSON íntegra de lo
/// leído (estructura heredada del diseño JSON 2026-05), incluyendo campos
/// que no mapean a columnas del ERP (detalle_procesos, refs, totales).
/// </para>
/// </summary>
public sealed class AwSolicitudesSqlReader : IAwSolicitudesReader
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IIntegracionSqlConnectionFactory _connectionFactory;
    private readonly ISucursalPorClaveAwResolver _sucursales;
    private readonly ICanalVentaPorClaveAwResolver _canalesVenta;
    private readonly AwPedidosOptions _options;
    private readonly ILogger<AwSolicitudesSqlReader> _logger;

    public AwSolicitudesSqlReader(
        IIntegracionSqlConnectionFactory connectionFactory,
        ISucursalPorClaveAwResolver sucursales,
        ICanalVentaPorClaveAwResolver canalesVenta,
        IOptions<AwPedidosOptions> options,
        ILogger<AwSolicitudesSqlReader> logger)
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
            SELECT TOP(@max) solicitud_id, numero_pedido, operacion, [version], creada_at
              FROM dbo.aw_solicitud_pedido
             WHERE resultado IS NULL OR resultado = 3
             ORDER BY numero_pedido, [version]
            """;

        return await EjecutarAsync("LeerPendientes", async connection =>
        {
            using var command = CrearCommand(connection, sql);
            command.Parameters.Add(new SqlParameter("@max", SqlDbType.Int) { Value = max });

            var rows = new List<SolicitudAw>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new SolicitudAw(
                    SolicitudId: reader.GetGuid(0),
                    NumeroPedido: reader.GetString(1),
                    Operacion: (OperacionAw)reader.GetByte(2),
                    Version: reader.GetInt64(3),
                    CreadaAt: reader.GetFieldValue<DateTimeOffset>(4)));
            }
            return (IReadOnlyList<SolicitudAw>)rows;
        }, cancellationToken);
    }

    public async Task<LecturaPedidoAw> LeerDatosPedidoAsync(
        string numeroPedido, CancellationToken cancellationToken)
    {
        var (cabecera, lineas, componentes) = await EjecutarAsync("LeerDatosPedido", async connection =>
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
                "[AwSolicitudesSqlReader] pedido {Pedido} sin cabecera en vw_erp_pedido_cabecera → rechazo",
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
                "[AwSolicitudesSqlReader] pedido {Pedido}: numero_sucursal '{Valor}' sin sucursal activa con esa clave_aw en el catálogo → pospuesta",
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
                "[AwSolicitudesSqlReader] pedido {Pedido}: canal_ventas '{Valor}' sin canal activo con esa clave_aw en el catálogo → pospuesta",
                numeroPedido, cabecera.CanalVentas);
            return LecturaPedidoAw.EsperaConfig(MotivoExcepcion.CanalVentaSinClaveAw,
                $"canal_ventas '{cabecera.CanalVentas}' sin canal activo con esa clave_aw en el catálogo");
        }

        return Construir(cabecera, lineas, componentes, sucursalId.Value, canalVentaId.Value, _options, _logger);
    }

    /// <summary>
    /// Mapea las filas crudas de las vistas al contrato de Facturación.
    /// Internal + estático para unit tests (mapeos sin SQL). Sucursal y
    /// canal de venta llegan ya resueltos por el caller (catálogos
    /// <c>clave_aw</c>, FAC-ING-PR2); la clase viene traducida por la vista
    /// a nombre del enum <see cref="ComportamientoFiscal"/> y se parsea
    /// directo — el diccionario de options es solo override operativo.
    /// Valor no resoluble → <see cref="LecturaPedidoAw"/> sin datos, con el
    /// motivo para la bandeja y el flag de config-fixable que decide
    /// pospuesta vs rechazo (doc 04 §4, FAC-ING-PR3).
    /// </summary>
    internal static LecturaPedidoAw Construir(
        CabeceraRow cab,
        IReadOnlyList<LineaRow> lineas,
        IReadOnlyList<ComponenteRow> componentes,
        Guid sucursalId,
        short canalVentaId,
        AwPedidosOptions opts,
        ILogger logger)
    {
        if (cab.Clase is null
            || !TryResolverEnum(cab.Clase, opts.MapeoComportamiento, out ComportamientoFiscal comportamiento))
        {
            logger.LogWarning(
                "[AwSolicitudesSqlReader] pedido {Pedido}: clase '{Valor}' no parsea a ComportamientoFiscal ni tiene override en {Seccion}:MapeoComportamiento — regla canal→comportamiento pendiente (gap G14)",
                cab.NumeroPedido, cab.Clase, AwPedidosOptions.SectionName);
            return LecturaPedidoAw.EsperaConfig(MotivoExcepcion.ComportamientoSinRegla,
                $"clase '{cab.Clase}' sin regla hacia ComportamientoFiscal (gap G14)");
        }
        if (lineas.Count == 0)
        {
            logger.LogWarning(
                "[AwSolicitudesSqlReader] pedido {Pedido} sin líneas en vw_erp_pedido_linea → rechazo",
                cab.NumeroPedido);
            return LecturaPedidoAw.DatosInvalidos(MotivoExcepcion.Otro,
                "pedido sin líneas en vw_erp_pedido_linea");
        }

        // PR5 — totales de control (doc 04 §3.1): si la vista de cabecera
        // aporta totales y no cuadran con las líneas leídas, el pedido NO se
        // ingesta con importes potencialmente corruptos → null → bandeja
        // (semántica TotalesNoCuadran; el detalle queda en el log).
        const decimal tolerancia = 0.01m;
        if (cab.TotalCantidad is decimal totalCantidad
            && Math.Abs(lineas.Sum(l => l.Cantidad) - totalCantidad) > tolerancia)
        {
            logger.LogWarning(
                "[AwSolicitudesSqlReader] pedido {Pedido}: total_cantidad de cabecera ({Cabecera}) no cuadra con las líneas ({Lineas}) → rechazo",
                cab.NumeroPedido, totalCantidad, lineas.Sum(l => l.Cantidad));
            return LecturaPedidoAw.DatosInvalidos(MotivoExcepcion.TotalesNoCuadran,
                $"total_cantidad de cabecera ({totalCantidad}) no cuadra con las líneas ({lineas.Sum(l => l.Cantidad)})");
        }
        // Ambos lados de esta comparación son BRUTOS (con IVA): los importes
        // de A+W vienen con impuesto y se comparan ANTES de convertir a neto
        // (FAC-DET-PR2) — cuando importe_total se active (gap G13) el check
        // sigue siendo consistente sin tocar nada aquí.
        if (cab.ImporteTotal is decimal importeTotal)
        {
            var importeLineas = lineas.Sum(l => l.ImportePieza * l.Cantidad - l.Descuento);
            if (Math.Abs(importeLineas - importeTotal) > tolerancia)
            {
                logger.LogWarning(
                    "[AwSolicitudesSqlReader] pedido {Pedido}: importe_total de cabecera ({Cabecera}) no cuadra con las líneas ({Lineas}) → rechazo",
                    cab.NumeroPedido, importeTotal, importeLineas);
                return LecturaPedidoAw.DatosInvalidos(MotivoExcepcion.TotalesNoCuadran,
                    $"importe_total de cabecera ({importeTotal}) no cuadra con las líneas ({importeLineas})");
            }
        }

        // IVA a nivel documento (FAC-DET-PR2): BW_AUFTR_KOPF.FI_MWST1 →
        // KA_MWST; la vista expone el porcentaje en iva_porcentaje y la tasa
        // se transfiere a TODAS las posiciones. A+W entrega porcentaje
        // (16.00) pero se tolera fracción (0.16) por si la vista cambia.
        decimal? tasaDocumento = cab.IvaPorcentaje switch
        {
            null => null,
            > 1m => cab.IvaPorcentaje / 100m,
            _ => cab.IvaPorcentaje,
        };
        if (tasaDocumento is < 0m or > 1m)
        {
            logger.LogWarning(
                "[AwSolicitudesSqlReader] pedido {Pedido}: iva_porcentaje '{Valor}' fuera de rango tras normalizar → rechazo",
                cab.NumeroPedido, cab.IvaPorcentaje);
            return LecturaPedidoAw.DatosInvalidos(MotivoExcepcion.Otro,
                $"iva_porcentaje '{cab.IvaPorcentaje}' fuera de rango tras normalizar");
        }

        // Ranura (RANURA-PR1): descuento a nivel cabecera (KO_FALZ), BRUTO
        // como los demás importes. No participa en la conciliación
        // cabecera-vs-líneas (las líneas no la incluyen); cuando importe_total
        // se active (gap G13) hay que definir si viene neto de ranura y
        // ajustar el check de arriba. 0 se normaliza a null (sin ranura).
        var ranura = cab.Ranura is > 0m ? cab.Ranura : null;
        if (cab.Ranura is < 0m)
        {
            logger.LogWarning(
                "[AwSolicitudesSqlReader] pedido {Pedido}: ranura negativa ({Valor}) → rechazo",
                cab.NumeroPedido, cab.Ranura);
            return LecturaPedidoAw.DatosInvalidos(MotivoExcepcion.Otro,
                $"ranura negativa ({cab.Ranura})");
        }

        var componentesPorPosicion = componentes
            .GroupBy(c => c.NumeroPosicion)
            .ToDictionary(g => g.Key, g => g.ToList());

        var lineasContrato = lineas
            .OrderBy(l => l.NumeroPosicion)
            .Select(l =>
            {
                // Los precios de las vistas vienen CON impuesto (bruto). Con
                // tasa de documento informada, el ERP calcula el neto hacia
                // atrás — así A+W y el ERP no divergen en importes. Redondeo
                // a 6 decimales (límite CFDI para ValorUnitario). Sin tasa,
                // el precio viaja tal cual (comportamiento previo intacto).
                var (precio, descuento) = tasaDocumento is decimal tasa
                    ? (Math.Round(l.ImportePieza / (1m + tasa), 6, MidpointRounding.AwayFromZero),
                       Math.Round(l.Descuento / (1m + tasa), 6, MidpointRounding.AwayFromZero))
                    : (l.ImportePieza, l.Descuento);

                // Centinela de supuestos (FAC-DET-PR2): si el bruto recalculado
                // desde el neto se aleja >$0.01 del original, algún supuesto
                // (descuento neto, redondeo del PAC de A+W) es falso — delatarlo
                // en el primer pedido real, no en la conciliación del cierre.
                if (tasaDocumento is decimal t
                    && Math.Abs(precio * (1m + t) - l.ImportePieza) > 0.01m)
                {
                    logger.LogWarning(
                        "[AwSolicitudesSqlReader] pedido {Pedido} pos {Pos}: bruto recalculado desde neto difiere del original ({Original} vs {Recalculado}) — revisar supuesto bruto/redondeo",
                        cab.NumeroPedido, l.NumeroPosicion, l.ImportePieza, precio * (1m + t));
                }

                return new LineaPedidoAw(
                    ProductoRef: l.ProductoRef,
                    Descripcion: l.Descripcion,
                    // Las claves SAT no existen en A+W — las aporta el master
                    // (compartido.producto_aw) al resolver la línea en el handler.
                    ClaveProdServSat: null,
                    ClaveUnidadSat: null,
                    Cantidad: l.Cantidad,
                    Precio: precio,
                    Descuento: descuento,
                    RequierePedimento: l.RequierePedimento ?? false,
                    BomJson: componentesPorPosicion.TryGetValue(l.NumeroPosicion, out var coms)
                        ? JsonSerializer.Serialize(coms, JsonOpts)
                        : null,
                    TasaIva: tasaDocumento);
            })
            .ToList();

        // Snapshot íntegro (pedido_facturable_snapshot, jsonb): TODO lo leído,
        // incluidos los campos que no mapean a columnas del ERP.
        var payloadCrudo = JsonSerializer.Serialize(
            new { cabecera = cab, lineas, componentes }, JsonOpts);

        return LecturaPedidoAw.Ok(new DatosPedidoAw(
            NumeroPedido: cab.NumeroPedido,
            SucursalId: sucursalId,
            ClienteRef: cab.ClienteRef,
            ClienteNombre: cab.ClienteNombre ?? string.Empty,
            CanalVenta: canalVentaId,
            ComportamientoFiscal: comportamiento,
            Moneda: cab.Divisa,
            ObraId: cab.ObraId,
            ObraNombre: cab.ObraNombre,
            Comentarios: cab.NotasPedido,
            EstadoOrigen: cab.EstadoOrigen,
            Lineas: lineasContrato,
            PayloadCrudo: payloadCrudo,
            Ranura: ranura));
    }

    /// <summary>
    /// Override del diccionario primero (escape hatch operativo por app
    /// settings, sin espacios en la llave); si no, parse directo del nombre
    /// del enum que ya entrega la vista. <c>Enum.IsDefined</c> evita que un
    /// string numérico ("7") se cuele como valor válido del enum.
    /// </summary>
    private static bool TryResolverEnum<TEnum>(
        string valor,
        Dictionary<string, TEnum> overrides,
        out TEnum resultado)
        where TEnum : struct, Enum
    {
        if (overrides.TryGetValue(valor, out resultado))
            return true;

        return Enum.TryParse(valor, ignoreCase: true, out resultado)
            && Enum.IsDefined(resultado);
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
        using var command = CrearCommand(connection, sql);
        command.Parameters.Add(new SqlParameter("@np", SqlDbType.NVarChar, 50) { Value = numeroPedido });

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
        using var command = CrearCommand(connection, sql);
        command.Parameters.Add(new SqlParameter("@np", SqlDbType.NVarChar, 50) { Value = numeroPedido });

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
        using var command = CrearCommand(connection, sql);
        command.Parameters.Add(new SqlParameter("@np", SqlDbType.NVarChar, 50) { Value = numeroPedido });

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

    // ------------------------------------------------------------------
    // Plomería compartida: conexión + clasificación de errores (patrón
    // HybridConnectionAwSqlReader del flujo 1).
    // ------------------------------------------------------------------

    private async Task<T> EjecutarAsync<T>(
        string operacion, Func<DbConnection, Task<T>> accion, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(_options.SqlConnectTimeoutSeconds));
            try
            {
                await connection.OpenAsync(connectCts.Token);
            }
            catch (OperationCanceledException) when (
                connectCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new AwReaderException(
                    $"SQL connect timeout tras {_options.SqlConnectTimeoutSeconds}s " +
                    $"(MILLET_INTEGRACION o Hybrid Connection no respondieron; op={operacion}).",
                    kind: "connect_timeout", isTransient: true);
            }

            return await accion(connection);
        }
        catch (SqlException ex) when (ex.Number == 18456)
        {
            _logger.LogError(ex, "[AwSolicitudesSqlReader] auth failure op={Op} #{Number}", operacion, ex.Number);
            throw new AwReaderException(
                $"SQL Server auth failed (#{ex.Number}): {ex.Message}",
                kind: "auth", isTransient: false, inner: ex);
        }
        catch (SqlException ex) when (ex.Number is -2 or 11 or 121)
        {
            _logger.LogWarning(ex, "[AwSolicitudesSqlReader] timeout op={Op} #{Number}", operacion, ex.Number);
            throw new AwReaderException(
                $"SQL query timeout ({_options.SqlQueryTimeoutSeconds}s).",
                kind: "timeout", isTransient: true, inner: ex);
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "[AwSolicitudesSqlReader] SQL error op={Op} #{Number}", operacion, ex.Number);
            throw new AwReaderException(
                $"SQL Server error (#{ex.Number}): {ex.Message}",
                kind: "connection", isTransient: true, inner: ex);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[AwSolicitudesSqlReader] connection state error op={Op}", operacion);
            throw new AwReaderException(
                $"SQL connection state error: {ex.Message}",
                kind: "connection", isTransient: true, inner: ex);
        }
    }

    private DbCommand CrearCommand(DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = _options.SqlQueryTimeoutSeconds;
        return command;
    }

    private static string? GetStringOrNull(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static decimal? GetDecimalOrNull(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal));

    // ------------------------------------------------------------------
    // Filas crudas de las vistas — espejo 1:1 del contrato de columnas
    // (doc 04 §3). Se serializan tal cual al snapshot (PayloadCrudo).
    // ------------------------------------------------------------------

    internal sealed record CabeceraRow(
        string NumeroPedido,
        string? NumeroSucursal,
        string ClienteRef,
        string? ClienteNombre,
        string? RfcCliente,
        string? UsoCfdi,
        string? MetodoPago,
        string? FormaPago,
        string? CondicionPago,
        string Divisa,
        long? ObraId,
        string? ObraNombre,
        string? NotasPedido,
        string? CanalVentas,
        string? Clase,
        DateOnly? FechaTransaccion,
        long? PedidoSustituidoNumero,
        string? EstadoOrigen,
        decimal? TotalCantidad,
        decimal? TotalM2,
        decimal? ImporteTotal,
        decimal? IvaPorcentaje = null,
        decimal? Ranura = null);

    internal sealed record LineaRow(
        int NumeroPosicion,
        string ProductoRef,
        string Descripcion,
        string? DetalleProcesos,
        decimal Cantidad,
        string UnidadMedida,
        decimal ImportePieza,
        decimal DescuentoPorcentaje,
        decimal Descuento,
        string? AlmacenNivel1,
        string? AlmacenNivel2,
        string? AlmacenNivel3,
        string? AlmacenNivel4,
        long? AlmacenIdUbicacion,
        bool? RequierePedimento);

    internal sealed record ComponenteRow(
        int NumeroPosicion,
        string ProductoRef,
        string? Descripcion,
        decimal? AltoMm,
        decimal? AnchoMm,
        decimal? M2PorPieza,
        decimal? Importe);
}
