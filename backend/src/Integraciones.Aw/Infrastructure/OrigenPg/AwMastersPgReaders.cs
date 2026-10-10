using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.OrigenPg;

/// <summary>Lectores PostgreSQL de los masters de la copia de demo, para auto-provisión de pedidos.</summary>
public sealed class AwClientesPgReader : IAwClientesReader
{
    private readonly IIntegracionSqlConnectionFactory _connectionFactory;
    private readonly AwPedidosOptions _options;
    private readonly ILogger<AwClientesPgReader> _logger;

    public AwClientesPgReader(
        IIntegracionSqlConnectionFactory connectionFactory,
        IOptions<AwPedidosOptions> options,
        ILogger<AwClientesPgReader> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AwClienteMaster?> LeerClienteAsync(
        string clienteRef, CancellationToken cancellationToken)
    {
        // PLATFORM-TODO(<AwClienteNumRegIdTrib>): cuando A+W agregue
        // num_reg_id_trib (tax id extranjero) a vw_erp_cliente, ampliar el
        // SELECT y mapearlo abajo (hoy llega null → captura en Datos Maestros).
        const string sql = """
            SELECT cliente_ref, razon_social, rfc, calle, colonia, cp,
                   ciudad, estado, pais, telefono
              FROM dbo.vw_erp_cliente
             WHERE cliente_ref = @ref
            """;

        return await PgPlumbing.EjecutarAsync(_connectionFactory, _options, _logger,
            "LeerCliente", async connection =>
            {
                using var command = PgPlumbing.CrearCommand(connection, sql, _options);
                PgPlumbing.Param(command, "@ref", DbType.String, clienteRef);

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) return null;

                return new AwClienteMaster(
                    ClienteRef: reader.GetString(0),
                    RazonSocial: PgPlumbing.GetStringOrNull(reader, 1) ?? string.Empty,
                    Rfc: PgPlumbing.GetStringOrNull(reader, 2),
                    Calle: PgPlumbing.GetStringOrNull(reader, 3),
                    Colonia: PgPlumbing.GetStringOrNull(reader, 4),
                    Cp: PgPlumbing.GetStringOrNull(reader, 5),
                    Ciudad: PgPlumbing.GetStringOrNull(reader, 6),
                    Estado: PgPlumbing.GetStringOrNull(reader, 7),
                    Pais: PgPlumbing.GetStringOrNull(reader, 8),
                    Telefono: PgPlumbing.GetStringOrNull(reader, 9));
            }, cancellationToken);
    }
}

public sealed class AwArticulosPgReader : IAwArticulosReader
{
    private readonly IIntegracionSqlConnectionFactory _connectionFactory;
    private readonly AwPedidosOptions _options;
    private readonly ILogger<AwArticulosPgReader> _logger;

    public AwArticulosPgReader(
        IIntegracionSqlConnectionFactory connectionFactory,
        IOptions<AwPedidosOptions> options,
        ILogger<AwArticulosPgReader> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AwArticuloMaster?> LeerArticuloAsync(
        string articuloRef, CancellationToken cancellationToken)
    {
        // PLATFORM-TODO(<AwArticuloAduana>): cuando A+W agregue fraccion_arancelaria
        // y peso_unitario_kg a vw_erp_articulo, ampliar el SELECT y mapearlos abajo
        // (hoy llegan null → el operador los captura en Datos Maestros).
        const string sql = """
            SELECT producto_ref, descripcion, unidad_medida
              FROM dbo.vw_erp_articulo
             WHERE producto_ref = @ref
            """;

        return await PgPlumbing.EjecutarAsync(_connectionFactory, _options, _logger,
            "LeerArticulo", async connection =>
            {
                using var command = PgPlumbing.CrearCommand(connection, sql, _options);
                PgPlumbing.Param(command, "@ref", DbType.String, articuloRef);

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) return null;

                return new AwArticuloMaster(
                    ProductoRef: reader.GetString(0),
                    Descripcion: PgPlumbing.GetStringOrNull(reader, 1) ?? string.Empty,
                    UnidadMedida: PgPlumbing.GetStringOrNull(reader, 2) ?? string.Empty);
            }, cancellationToken);
    }
}

