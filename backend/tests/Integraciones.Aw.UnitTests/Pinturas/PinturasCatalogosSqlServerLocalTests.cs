using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pinturas.Catalogos;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Pinturas;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Integraciones.Aw.UnitTests.Pinturas;

/// <summary>
/// Pruebas de integración local contra SQL Server (<c>MILLET_INTEGRACION</c> con Script 16 v2) para
/// <see cref="PinturasCatalogosAdapter"/>.
/// Valida inserción inicial con usuario ERP (<c>millet_erp_integracion</c>), actualización idempotente,
/// baja lógica (<c>activo = 0</c>) y permisos de solo lectura del Portal (<c>millet_pinturas_portal</c>).
/// Requiere las variables de entorno <c>PINTURAS_SQLSERVER</c> y <c>PINTURAS_SQLSERVER_PORTAL</c>.
/// </summary>
public sealed class PinturasCatalogosSqlServerLocalTests
{
    private static string Erp => Environment.GetEnvironmentVariable(PinturasSqlServerFactAttribute.Variable)!;
    private static string Portal => Environment.GetEnvironmentVariable(PinturasSqlServerFactAttribute.VariablePortal)!;

    private static int _clienteSeed = 980000 + Random.Shared.Next(1, 1000) * 10;
    private static int SiguienteClaveCliente() => Interlocked.Increment(ref _clienteSeed);
    private static string GenerarClaveServicio() => $"CAT-SQL-SER-{Guid.NewGuid():N}"[..30];
    private static string GenerarClaveObra() => $"CAT-SQL-OBRA-{Guid.NewGuid():N}"[..30];
    private static string GenerarClaveEmpleado() => $"CAT-SQL-EMP-{Guid.NewGuid():N}"[..30];

    private sealed class Fabrica(string cs) : IIntegracionSqlConnectionFactory
    {
        public System.Data.Common.DbConnection CreateConnection() => new SqlConnection(cs);
    }

    private static PinturasCatalogosAdapter CrearAdapter(string cs, IClock clock)
    {
        return new PinturasCatalogosAdapter(
            new Fabrica(cs),
            postgres: false,
            Options.Create(new PinturasCatalogosOptions { SqlQueryTimeoutSeconds = 15 }),
            clock,
            NullLogger<PinturasCatalogosAdapter>.Instance);
    }

    private sealed record ClienteDb(
        string RazonSocial,
        string? NombreComercial,
        string? Contacto,
        string? Telefono,
        string? Celular,
        string? Email,
        bool Activo,
        DateTime UpdatedAt);

    private sealed record ServicioDb(
        string Nombre,
        bool Activo,
        DateTime UpdatedAt);

    private sealed record ObraDb(
        string Nombre,
        string Origen,
        string? ReferenciaAw,
        bool Activo,
        DateTime UpdatedAt);

    private sealed record EmpleadoDb(
        string Nombre,
        bool Activo,
        DateTime UpdatedAt);

    private static async Task<ClienteDb?> LeerClienteAsync(string cs, int clave)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT razon_social, nombre_comercial, contacto, telefono, celular, email, activo, updated_at " +
            "FROM dbo.pinturas_cliente WHERE clave = @c", cn);
        cmd.Parameters.AddWithValue("@c", clave);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new ClienteDb(
            r.GetString(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.GetBoolean(6),
            r.GetDateTime(7));
    }

    private static async Task<ServicioDb?> LeerServicioAsync(string cs, string clave)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT nombre, activo, updated_at FROM dbo.pinturas_servicio WHERE clave = @c", cn);
        cmd.Parameters.AddWithValue("@c", clave);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new ServicioDb(
            r.GetString(0),
            r.GetBoolean(1),
            r.GetDateTime(2));
    }

    private static async Task<ObraDb?> LeerObraAsync(string cs, string clave)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT nombre, origen, referencia_aw, activo, updated_at FROM dbo.pinturas_obra WHERE clave = @c", cn);
        cmd.Parameters.AddWithValue("@c", clave);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new ObraDb(
            r.GetString(0),
            r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.GetBoolean(3),
            r.GetDateTime(4));
    }

    private static async Task<EmpleadoDb?> LeerEmpleadoAsync(string cs, string clave)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT nombre, activo, updated_at FROM dbo.pinturas_empleado WHERE clave = @c", cn);
        cmd.Parameters.AddWithValue("@c", clave);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new EmpleadoDb(
            r.GetString(0),
            r.GetBoolean(1),
            r.GetDateTime(2));
    }

    private static async Task<int> ContarAsync(string cs, string tabla, string columnaClave, object valorClave)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand($"SELECT COUNT(*) FROM dbo.{tabla} WHERE {columnaClave} = @k", cn);
        cmd.Parameters.AddWithValue("@k", valorClave);
        var res = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(res);
    }

    private static async Task<int> EjecutarAsync(string cs, string sql, params (string, object?)[] ps)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync();
    }

    [PinturasSqlServerFact]
    public async Task Insercion_inicial_de_catalogos_con_usuario_erp_escribe_en_las_4_tablas()
    {
        var t0 = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(t0);
        var adapter = CrearAdapter(Erp, clock);

        var clienteClave = SiguienteClaveCliente();
        var servicioClave = GenerarClaveServicio();
        var obraClave = GenerarClaveObra();
        var empleadoClave = GenerarClaveEmpleado();

        var batch = new PinturasCatalogosBatch(
            Clientes:
            [
                new FilaPinturasCliente(
                    Clave: clienteClave,
                    RazonSocial: "Cliente Inicial SA de CV",
                    NombreComercial: "Comercial Inicial",
                    Contacto: "Lic. Inicial",
                    Telefono: "9991234567",
                    Celular: "9997654321",
                    Email: "inicial@cliente.com",
                    Activo: true)
            ],
            Servicios:
            [
                new FilaPinturasServicio(
                    Clave: servicioClave,
                    Nombre: "Servicio Recubrimiento Inicial",
                    Activo: true)
            ],
            Obras:
            [
                new FilaPinturasObra(
                    Clave: obraClave,
                    Nombre: "Torre Residencial Inicial",
                    Origen: FilaPinturasObra.OrigenAw,
                    ReferenciaAw: "AW-1001",
                    Activo: true)
            ],
            Empleados:
            [
                new FilaPinturasEmpleado(
                    Clave: empleadoClave,
                    Nombre: "Empleado Inicial",
                    Activo: true)
            ]);

        var resultado = await adapter.PublicarAsync(batch, CancellationToken.None);

        resultado.ClientesPublicados.Should().Be(1);
        resultado.ServiciosPublicados.Should().Be(1);
        resultado.ObrasPublicadas.Should().Be(1);
        resultado.EmpleadosPublicados.Should().Be(1);
        resultado.TotalPublicados.Should().Be(4);
        resultado.PublicadoAt.Should().Be(t0);

        var cliDb = await LeerClienteAsync(Erp, clienteClave);
        cliDb.Should().NotBeNull();
        cliDb!.RazonSocial.Should().Be("Cliente Inicial SA de CV");
        cliDb.NombreComercial.Should().Be("Comercial Inicial");
        cliDb.Contacto.Should().Be("Lic. Inicial");
        cliDb.Telefono.Should().Be("9991234567");
        cliDb.Celular.Should().Be("9997654321");
        cliDb.Email.Should().Be("inicial@cliente.com");
        cliDb.Activo.Should().BeTrue();
        cliDb.UpdatedAt.Should().Be(t0.UtcDateTime);

        var serDb = await LeerServicioAsync(Erp, servicioClave);
        serDb.Should().NotBeNull();
        serDb!.Nombre.Should().Be("Servicio Recubrimiento Inicial");
        serDb.Activo.Should().BeTrue();
        serDb.UpdatedAt.Should().Be(t0.UtcDateTime);

        var obraDb = await LeerObraAsync(Erp, obraClave);
        obraDb.Should().NotBeNull();
        obraDb!.Nombre.Should().Be("Torre Residencial Inicial");
        obraDb.Origen.Should().Be(FilaPinturasObra.OrigenAw);
        obraDb.ReferenciaAw.Should().Be("AW-1001");
        obraDb.Activo.Should().BeTrue();
        obraDb.UpdatedAt.Should().Be(t0.UtcDateTime);

        var empDb = await LeerEmpleadoAsync(Erp, empleadoClave);
        empDb.Should().NotBeNull();
        empDb!.Nombre.Should().Be("Empleado Inicial");
        empDb.Activo.Should().BeTrue();
        empDb.UpdatedAt.Should().Be(t0.UtcDateTime);
    }

    [PinturasSqlServerFact]
    public async Task Actualizacion_idempotente_actualiza_campos_y_updated_at_sin_duplicar_filas()
    {
        var t0 = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(t0);
        var adapter = CrearAdapter(Erp, clock);

        var clienteClave = SiguienteClaveCliente();
        var servicioClave = GenerarClaveServicio();
        var obraClave = GenerarClaveObra();
        var empleadoClave = GenerarClaveEmpleado();

        var loteInicial = new PinturasCatalogosBatch(
            Clientes: [new FilaPinturasCliente(clienteClave, "Cliente Original", Activo: true)],
            Servicios: [new FilaPinturasServicio(servicioClave, "Servicio Original", Activo: true)],
            Obras: [new FilaPinturasObra(obraClave, "Obra Original", FilaPinturasObra.OrigenAw, "REF-1", Activo: true)],
            Empleados: [new FilaPinturasEmpleado(empleadoClave, "Empleado Original", Activo: true)]);

        await adapter.PublicarAsync(loteInicial, CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(2));
        var t1 = clock.UtcNow;

        var loteActualizado = new PinturasCatalogosBatch(
            Clientes:
            [
                new FilaPinturasCliente(
                    Clave: clienteClave,
                    RazonSocial: "Cliente Actualizado SA de CV",
                    NombreComercial: "Comercial Actualizado",
                    Contacto: "Nuevo Contacto",
                    Telefono: "9999999999",
                    Celular: "8888888888",
                    Email: "actualizado@cliente.com",
                    Activo: true)
            ],
            Servicios:
            [
                new FilaPinturasServicio(
                    Clave: servicioClave,
                    Nombre: "Servicio Recubrimiento Actualizado",
                    Activo: true)
            ],
            Obras:
            [
                new FilaPinturasObra(
                    Clave: obraClave,
                    Nombre: "Torre Residencial Actualizada",
                    Origen: FilaPinturasObra.OrigenPinturas,
                    ReferenciaAw: null,
                    Activo: true)
            ],
            Empleados:
            [
                new FilaPinturasEmpleado(
                    Clave: empleadoClave,
                    Nombre: "Empleado Actualizado",
                    Activo: true)
            ]);

        var resultadoActualizacion = await adapter.PublicarAsync(loteActualizado, CancellationToken.None);
        resultadoActualizacion.TotalPublicados.Should().Be(4);

        (await ContarAsync(Erp, "pinturas_cliente", "clave", clienteClave)).Should().Be(1);
        (await ContarAsync(Erp, "pinturas_servicio", "clave", servicioClave)).Should().Be(1);
        (await ContarAsync(Erp, "pinturas_obra", "clave", obraClave)).Should().Be(1);
        (await ContarAsync(Erp, "pinturas_empleado", "clave", empleadoClave)).Should().Be(1);

        var cliDb = await LeerClienteAsync(Erp, clienteClave);
        cliDb.Should().NotBeNull();
        cliDb!.RazonSocial.Should().Be("Cliente Actualizado SA de CV");
        cliDb.NombreComercial.Should().Be("Comercial Actualizado");
        cliDb.Contacto.Should().Be("Nuevo Contacto");
        cliDb.Telefono.Should().Be("9999999999");
        cliDb.Celular.Should().Be("8888888888");
        cliDb.Email.Should().Be("actualizado@cliente.com");
        cliDb.Activo.Should().BeTrue();
        cliDb.UpdatedAt.Should().Be(t1.UtcDateTime);
        cliDb.UpdatedAt.Should().BeAfter(t0.UtcDateTime);

        var serDb = await LeerServicioAsync(Erp, servicioClave);
        serDb.Should().NotBeNull();
        serDb!.Nombre.Should().Be("Servicio Recubrimiento Actualizado");
        serDb.Activo.Should().BeTrue();
        serDb.UpdatedAt.Should().Be(t1.UtcDateTime);
        serDb.UpdatedAt.Should().BeAfter(t0.UtcDateTime);

        var obraDb = await LeerObraAsync(Erp, obraClave);
        obraDb.Should().NotBeNull();
        obraDb!.Nombre.Should().Be("Torre Residencial Actualizada");
        obraDb.Origen.Should().Be(FilaPinturasObra.OrigenPinturas);
        obraDb.ReferenciaAw.Should().BeNull();
        obraDb.Activo.Should().BeTrue();
        obraDb.UpdatedAt.Should().Be(t1.UtcDateTime);
        obraDb.UpdatedAt.Should().BeAfter(t0.UtcDateTime);

        var empDb = await LeerEmpleadoAsync(Erp, empleadoClave);
        empDb.Should().NotBeNull();
        empDb!.Nombre.Should().Be("Empleado Actualizado");
        empDb.Activo.Should().BeTrue();
        empDb.UpdatedAt.Should().Be(t1.UtcDateTime);
        empDb.UpdatedAt.Should().BeAfter(t0.UtcDateTime);
    }

    [PinturasSqlServerFact]
    public async Task Baja_logica_guarda_activo_falso_sin_borrar_filas_en_las_4_tablas()
    {
        var t0 = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(t0);
        var adapter = CrearAdapter(Erp, clock);

        var clienteClave = SiguienteClaveCliente();
        var servicioClave = GenerarClaveServicio();
        var obraClave = GenerarClaveObra();
        var empleadoClave = GenerarClaveEmpleado();

        var loteInicial = new PinturasCatalogosBatch(
            Clientes: [new FilaPinturasCliente(clienteClave, "Cliente Para Baja", Activo: true)],
            Servicios: [new FilaPinturasServicio(servicioClave, "Servicio Para Baja", Activo: true)],
            Obras: [new FilaPinturasObra(obraClave, "Obra Para Baja", FilaPinturasObra.OrigenAw, "REF-BAJA", Activo: true)],
            Empleados: [new FilaPinturasEmpleado(empleadoClave, "Empleado Para Baja", Activo: true)]);

        await adapter.PublicarAsync(loteInicial, CancellationToken.None);

        clock.Advance(TimeSpan.FromMinutes(30));
        var t1 = clock.UtcNow;

        var loteBaja = new PinturasCatalogosBatch(
            Clientes: [new FilaPinturasCliente(clienteClave, "Cliente Para Baja", Activo: false)],
            Servicios: [new FilaPinturasServicio(servicioClave, "Servicio Para Baja", Activo: false)],
            Obras: [new FilaPinturasObra(obraClave, "Obra Para Baja", FilaPinturasObra.OrigenAw, "REF-BAJA", Activo: false)],
            Empleados: [new FilaPinturasEmpleado(empleadoClave, "Empleado Para Baja", Activo: false)]);

        var resultadoBaja = await adapter.PublicarAsync(loteBaja, CancellationToken.None);
        resultadoBaja.TotalPublicados.Should().Be(4);

        (await ContarAsync(Erp, "pinturas_cliente", "clave", clienteClave)).Should().Be(1, "baja lógica no borra físicamente");
        (await ContarAsync(Erp, "pinturas_servicio", "clave", servicioClave)).Should().Be(1, "baja lógica no borra físicamente");
        (await ContarAsync(Erp, "pinturas_obra", "clave", obraClave)).Should().Be(1, "baja lógica no borra físicamente");
        (await ContarAsync(Erp, "pinturas_empleado", "clave", empleadoClave)).Should().Be(1, "baja lógica no borra físicamente");

        var cliDb = await LeerClienteAsync(Erp, clienteClave);
        cliDb.Should().NotBeNull();
        cliDb!.Activo.Should().BeFalse();
        cliDb.UpdatedAt.Should().Be(t1.UtcDateTime);

        var serDb = await LeerServicioAsync(Erp, servicioClave);
        serDb.Should().NotBeNull();
        serDb!.Activo.Should().BeFalse();
        serDb.UpdatedAt.Should().Be(t1.UtcDateTime);

        var obraDb = await LeerObraAsync(Erp, obraClave);
        obraDb.Should().NotBeNull();
        obraDb!.Activo.Should().BeFalse();
        obraDb.UpdatedAt.Should().Be(t1.UtcDateTime);

        var empDb = await LeerEmpleadoAsync(Erp, empleadoClave);
        empDb.Should().NotBeNull();
        empDb!.Activo.Should().BeFalse();
        empDb.UpdatedAt.Should().Be(t1.UtcDateTime);
    }

    [PinturasSqlServerFact]
    public async Task Portal_puede_leer_los_4_catalogos_pero_no_puede_modificar_ni_borrar()
    {
        var t0 = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(t0);
        var adapter = CrearAdapter(Erp, clock);

        var clienteClave = SiguienteClaveCliente();
        var servicioClave = GenerarClaveServicio();
        var obraClave = GenerarClaveObra();
        var empleadoClave = GenerarClaveEmpleado();

        var lote = new PinturasCatalogosBatch(
            Clientes: [new FilaPinturasCliente(clienteClave, "Cliente Solo Lectura Portal", Contacto: "Contacto Portal", Activo: true)],
            Servicios: [new FilaPinturasServicio(servicioClave, "Servicio Solo Lectura Portal", Activo: true)],
            Obras: [new FilaPinturasObra(obraClave, "Obra Solo Lectura Portal", FilaPinturasObra.OrigenPinturas, Activo: true)],
            Empleados: [new FilaPinturasEmpleado(empleadoClave, "Empleado Solo Lectura Portal", Activo: true)]);

        await adapter.PublicarAsync(lote, CancellationToken.None);

        // 1. SELECT por parte del portal es exitoso en las 4 tablas
        var cliDb = await LeerClienteAsync(Portal, clienteClave);
        cliDb.Should().NotBeNull();
        cliDb!.RazonSocial.Should().Be("Cliente Solo Lectura Portal");

        var serDb = await LeerServicioAsync(Portal, servicioClave);
        serDb.Should().NotBeNull();
        serDb!.Nombre.Should().Be("Servicio Solo Lectura Portal");

        var obraDb = await LeerObraAsync(Portal, obraClave);
        obraDb.Should().NotBeNull();
        obraDb!.Nombre.Should().Be("Obra Solo Lectura Portal");

        var empDb = await LeerEmpleadoAsync(Portal, empleadoClave);
        empDb.Should().NotBeNull();
        empDb!.Nombre.Should().Be("Empleado Solo Lectura Portal");

        // 2. UPDATE no está permitido para el portal (SqlException 229)
        var updateCli = () => EjecutarAsync(Portal, "UPDATE dbo.pinturas_cliente SET razon_social = N'Hack' WHERE clave = @k", ("@k", clienteClave));
        (await updateCli.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229);

        var updateSer = () => EjecutarAsync(Portal, "UPDATE dbo.pinturas_servicio SET nombre = N'Hack' WHERE clave = @k", ("@k", servicioClave));
        (await updateSer.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229);

        var updateObra = () => EjecutarAsync(Portal, "UPDATE dbo.pinturas_obra SET nombre = N'Hack' WHERE clave = @k", ("@k", obraClave));
        (await updateObra.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229);

        var updateEmp = () => EjecutarAsync(Portal, "UPDATE dbo.pinturas_empleado SET nombre = N'Hack' WHERE clave = @k", ("@k", empleadoClave));
        (await updateEmp.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229);

        // 3. DELETE no está permitido para el portal (SqlException 229)
        var deleteCli = () => EjecutarAsync(Portal, "DELETE FROM dbo.pinturas_cliente WHERE clave = @k", ("@k", clienteClave));
        (await deleteCli.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229);

        var deleteSer = () => EjecutarAsync(Portal, "DELETE FROM dbo.pinturas_servicio WHERE clave = @k", ("@k", servicioClave));
        (await deleteSer.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229);

        var deleteObra = () => EjecutarAsync(Portal, "DELETE FROM dbo.pinturas_obra WHERE clave = @k", ("@k", obraClave));
        (await deleteObra.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229);

        var deleteEmp = () => EjecutarAsync(Portal, "DELETE FROM dbo.pinturas_empleado WHERE clave = @k", ("@k", empleadoClave));
        (await deleteEmp.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229);
    }
}
