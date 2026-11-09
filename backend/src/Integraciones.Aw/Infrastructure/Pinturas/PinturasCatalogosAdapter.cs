using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pinturas.Catalogos;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Infrastructure.Pinturas;

/// <summary>
/// Adaptador de publicación idempotente de catálogos del ERP hacia la base de datos de integración
/// <c>MILLET_INTEGRACION</c> (tablas <c>dbo.pinturas_cliente</c>, <c>dbo.pinturas_servicio</c>,
/// <c>dbo.pinturas_obra</c> y <c>dbo.pinturas_empleado</c> según Script 16 v2).
/// Soporta dialecto dual: SQL Server (on-prem) y PostgreSQL (demo / local testing).
/// </summary>
public sealed class PinturasCatalogosAdapter : IPinturasCatalogosPublisher
{
    // Límites de longitud según esquema en 16_create_pinturas.sql
    internal const int LargoRazonSocial = 200;
    internal const int LargoNombreComercial = 200;
    internal const int LargoContacto = 200;
    internal const int LargoTelefono = 50;
    internal const int LargoCelular = 50;
    internal const int LargoEmail = 200;

    internal const int LargoServicioClave = 50;
    internal const int LargoServicioNombre = 200;

    internal const int LargoObraClave = 50;
    internal const int LargoObraNombre = 200;
    internal const int LargoObraOrigen = 10;
    internal const int LargoObraReferenciaAw = 50;

    internal const int LargoEmpleadoClave = 50;
    internal const int LargoEmpleadoNombre = 200;

    private readonly IIntegracionSqlConnectionFactory _fabrica;
    private readonly bool _postgres;
    private readonly PinturasCatalogosOptions _opciones;
    private readonly IClock _clock;
    private readonly ILogger<PinturasCatalogosAdapter> _logger;

    public PinturasCatalogosAdapter(
        IIntegracionSqlConnectionFactory fabrica,
        bool postgres,
        IOptions<PinturasCatalogosOptions> opciones,
        IClock clock,
        ILogger<PinturasCatalogosAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _fabrica = fabrica;
        _postgres = postgres;
        _opciones = opciones?.Value ?? new PinturasCatalogosOptions();
        _clock = clock;
        _logger = logger;
    }

    internal static string SqlUpsertCliente(bool postgres) =>
        postgres
            ? "INSERT INTO dbo.pinturas_cliente (clave, razon_social, nombre_comercial, contacto, telefono, celular, email, activo, updated_at) " +
              "VALUES (@clave, @razon_social, @nombre_comercial, @contacto, @telefono, @celular, @email, @activo, @updated_at) " +
              "ON CONFLICT (clave) DO UPDATE SET " +
              "razon_social = EXCLUDED.razon_social, nombre_comercial = EXCLUDED.nombre_comercial, contacto = EXCLUDED.contacto, " +
              "telefono = EXCLUDED.telefono, celular = EXCLUDED.celular, email = EXCLUDED.email, activo = EXCLUDED.activo, updated_at = EXCLUDED.updated_at;"
            : "UPDATE dbo.pinturas_cliente SET " +
              "razon_social = @razon_social, nombre_comercial = @nombre_comercial, contacto = @contacto, telefono = @telefono, celular = @celular, email = @email, activo = @activo, updated_at = @updated_at " +
              "WHERE clave = @clave; " +
              "IF @@ROWCOUNT = 0 BEGIN " +
              "INSERT INTO dbo.pinturas_cliente (clave, razon_social, nombre_comercial, contacto, telefono, celular, email, activo, updated_at) " +
              "VALUES (@clave, @razon_social, @nombre_comercial, @contacto, @telefono, @celular, @email, @activo, @updated_at); " +
              "END;";

    internal static string SqlUpsertServicio(bool postgres) =>
        postgres
            ? "INSERT INTO dbo.pinturas_servicio (clave, nombre, activo, updated_at) " +
              "VALUES (@clave, @nombre, @activo, @updated_at) " +
              "ON CONFLICT (clave) DO UPDATE SET " +
              "nombre = EXCLUDED.nombre, activo = EXCLUDED.activo, updated_at = EXCLUDED.updated_at;"
            : "UPDATE dbo.pinturas_servicio SET " +
              "nombre = @nombre, activo = @activo, updated_at = @updated_at " +
              "WHERE clave = @clave; " +
              "IF @@ROWCOUNT = 0 BEGIN " +
              "INSERT INTO dbo.pinturas_servicio (clave, nombre, activo, updated_at) " +
              "VALUES (@clave, @nombre, @activo, @updated_at); " +
              "END;";

    internal static string SqlUpsertObra(bool postgres) =>
        postgres
            ? "INSERT INTO dbo.pinturas_obra (clave, nombre, origen, referencia_aw, activo, updated_at) " +
              "VALUES (@clave, @nombre, @origen, @referencia_aw, @activo, @updated_at) " +
              "ON CONFLICT (clave) DO UPDATE SET " +
              "nombre = EXCLUDED.nombre, origen = EXCLUDED.origen, referencia_aw = EXCLUDED.referencia_aw, activo = EXCLUDED.activo, updated_at = EXCLUDED.updated_at;"
            : "UPDATE dbo.pinturas_obra SET " +
              "nombre = @nombre, origen = @origen, referencia_aw = @referencia_aw, activo = @activo, updated_at = @updated_at " +
              "WHERE clave = @clave; " +
              "IF @@ROWCOUNT = 0 BEGIN " +
              "INSERT INTO dbo.pinturas_obra (clave, nombre, origen, referencia_aw, activo, updated_at) " +
              "VALUES (@clave, @nombre, @origen, @referencia_aw, @activo, @updated_at); " +
              "END;";

    internal static string SqlUpsertEmpleado(bool postgres) =>
        postgres
            ? "INSERT INTO dbo.pinturas_empleado (clave, nombre, activo, updated_at) " +
              "VALUES (@clave, @nombre, @activo, @updated_at) " +
              "ON CONFLICT (clave) DO UPDATE SET " +
              "nombre = EXCLUDED.nombre, activo = EXCLUDED.activo, updated_at = EXCLUDED.updated_at;"
            : "UPDATE dbo.pinturas_empleado SET " +
              "nombre = @nombre, activo = @activo, updated_at = @updated_at " +
              "WHERE clave = @clave; " +
              "IF @@ROWCOUNT = 0 BEGIN " +
              "INSERT INTO dbo.pinturas_empleado (clave, nombre, activo, updated_at) " +
              "VALUES (@clave, @nombre, @activo, @updated_at); " +
              "END;";

    internal static string? RecortarOpcional(string? valor, int maximo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var trimmed = valor.Trim();
        return trimmed.Length > maximo ? trimmed[..maximo] : trimmed;
    }

    internal static string RecortarRequerido(string? valor, int maximo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return string.Empty;
        }

        var trimmed = valor.Trim();
        return trimmed.Length > maximo ? trimmed[..maximo] : trimmed;
    }

    public async Task<PinturasPublicacionResultado> PublicarAsync(
        PinturasCatalogosBatch batch,
        CancellationToken cancellationToken)
    {
        var publicadoAt = _clock.UtcNow;

        if (batch is null || batch.EstaVacio)
        {
            _logger.LogDebug("[PinturasCatalogos] Lote de catálogos vacío; no se requiere publicación.");
            return PinturasPublicacionResultado.Vacio(publicadoAt);
        }

        var fechaUtc = publicadoAt.UtcDateTime;
        var sqlCliente = SqlUpsertCliente(_postgres);
        var sqlServicio = SqlUpsertServicio(_postgres);
        var sqlObra = SqlUpsertObra(_postgres);
        var sqlEmpleado = SqlUpsertEmpleado(_postgres);

        int clientesPublicados = 0;
        int serviciosPublicados = 0;
        int obrasPublicadas = 0;
        int empleadosPublicados = 0;

        await using var conexion = _fabrica.CreateConnection();
        await conexion.OpenAsync(cancellationToken);
        await using var tx = await conexion.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var cliente in batch.Clientes)
            {
                await EjecutarUpsertClienteAsync(conexion, tx, sqlCliente, cliente, fechaUtc, cancellationToken);
                clientesPublicados++;
            }

            foreach (var servicio in batch.Servicios)
            {
                await EjecutarUpsertServicioAsync(conexion, tx, sqlServicio, servicio, fechaUtc, cancellationToken);
                serviciosPublicados++;
            }

            foreach (var obra in batch.Obras)
            {
                await EjecutarUpsertObraAsync(conexion, tx, sqlObra, obra, fechaUtc, cancellationToken);
                obrasPublicadas++;
            }

            foreach (var empleado in batch.Empleados)
            {
                await EjecutarUpsertEmpleadoAsync(conexion, tx, sqlEmpleado, empleado, fechaUtc, cancellationToken);
                empleadosPublicados++;
            }

            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "[PinturasCatalogos] Publicación completada con éxito ({Dialecto}). Clientes={Clientes}, Servicios={Servicios}, Obras={Obras}, Empleados={Empleados}, Total={Total}",
                _postgres ? "PostgreSQL" : "SQL Server",
                clientesPublicados,
                serviciosPublicados,
                obrasPublicadas,
                empleadosPublicados,
                clientesPublicados + serviciosPublicados + obrasPublicadas + empleadosPublicados);

            return new PinturasPublicacionResultado(
                clientesPublicados,
                serviciosPublicados,
                obrasPublicadas,
                empleadosPublicados,
                publicadoAt);
        }
        catch (Exception ex)
        {
            try
            {
                await tx.RollbackAsync(cancellationToken);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogWarning(rollbackEx, "[PinturasCatalogos] Error al hacer rollback de la transacción de publicación.");
            }

            _logger.LogError(
                ex,
                "[PinturasCatalogos] Error al publicar lote de catálogos en la base de datos (progreso previo al fallo: Clientes={Clientes}, Servicios={Servicios}, Obras={Obras}, Empleados={Empleados}).",
                clientesPublicados,
                serviciosPublicados,
                obrasPublicadas,
                empleadosPublicados);
            throw;
        }
    }

    private async Task EjecutarUpsertClienteAsync(
        DbConnection conexion,
        DbTransaction tx,
        string sql,
        FilaPinturasCliente c,
        DateTime fechaUtc,
        CancellationToken ct)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.CommandTimeout = _opciones.SqlQueryTimeoutSeconds;

        AgregarParametro(cmd, "@clave", c.Clave);
        AgregarParametro(cmd, "@razon_social", RecortarRequerido(c.RazonSocial, LargoRazonSocial));
        AgregarParametro(cmd, "@nombre_comercial", RecortarOpcional(c.NombreComercial, LargoNombreComercial));
        AgregarParametro(cmd, "@contacto", RecortarOpcional(c.Contacto, LargoContacto));
        AgregarParametro(cmd, "@telefono", RecortarOpcional(c.Telefono, LargoTelefono));
        AgregarParametro(cmd, "@celular", RecortarOpcional(c.Celular, LargoCelular));
        AgregarParametro(cmd, "@email", RecortarOpcional(c.Email, LargoEmail));
        AgregarParametro(cmd, "@activo", c.Activo);
        AgregarParametro(cmd, "@updated_at", fechaUtc);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task EjecutarUpsertServicioAsync(
        DbConnection conexion,
        DbTransaction tx,
        string sql,
        FilaPinturasServicio s,
        DateTime fechaUtc,
        CancellationToken ct)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.CommandTimeout = _opciones.SqlQueryTimeoutSeconds;

        AgregarParametro(cmd, "@clave", RecortarRequerido(s.Clave, LargoServicioClave));
        AgregarParametro(cmd, "@nombre", RecortarRequerido(s.Nombre, LargoServicioNombre));
        AgregarParametro(cmd, "@activo", s.Activo);
        AgregarParametro(cmd, "@updated_at", fechaUtc);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task EjecutarUpsertObraAsync(
        DbConnection conexion,
        DbTransaction tx,
        string sql,
        FilaPinturasObra o,
        DateTime fechaUtc,
        CancellationToken ct)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.CommandTimeout = _opciones.SqlQueryTimeoutSeconds;

        AgregarParametro(cmd, "@clave", RecortarRequerido(o.Clave, LargoObraClave));
        AgregarParametro(cmd, "@nombre", RecortarRequerido(o.Nombre, LargoObraNombre));
        AgregarParametro(cmd, "@origen", RecortarRequerido(o.Origen, LargoObraOrigen));
        AgregarParametro(cmd, "@referencia_aw", RecortarOpcional(o.ReferenciaAw, LargoObraReferenciaAw));
        AgregarParametro(cmd, "@activo", o.Activo);
        AgregarParametro(cmd, "@updated_at", fechaUtc);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task EjecutarUpsertEmpleadoAsync(
        DbConnection conexion,
        DbTransaction tx,
        string sql,
        FilaPinturasEmpleado e,
        DateTime fechaUtc,
        CancellationToken ct)
    {
        await using var cmd = conexion.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.CommandTimeout = _opciones.SqlQueryTimeoutSeconds;

        AgregarParametro(cmd, "@clave", RecortarRequerido(e.Clave, LargoEmpleadoClave));
        AgregarParametro(cmd, "@nombre", RecortarRequerido(e.Nombre, LargoEmpleadoNombre));
        AgregarParametro(cmd, "@activo", e.Activo);
        AgregarParametro(cmd, "@updated_at", fechaUtc);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AgregarParametro(DbCommand cmd, string nombre, object? valor)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = nombre;
        p.Value = valor ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
