using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Ingesta.ImportarPedidoPlantaPintura;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Application.Pinturas;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Pinturas;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.UnitTests.Pinturas;

/// <summary>Corre solo con <c>PINTURAS_SQLSERVER</c> (cadena del ERP) y <c>PINTURAS_SQLSERVER_PORTAL</c>; en la CI se omite.</summary>
public sealed class PinturasSqlServerFactAttribute : FactAttribute
{
    public const string Variable = "PINTURAS_SQLSERVER";
    public const string VariablePortal = "PINTURAS_SQLSERVER_PORTAL";
    public PinturasSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VariablePortal)))
            Skip = $"Prueba local de I13.9 contra SQL Server: define {Variable} y {VariablePortal}.";
    }
}

/// <summary>
/// I13.9 (prueba local): <see cref="PinturasPedidosAdapter"/> contra <c>MILLET_INTEGRACION(_DEV)</c> con el script 16 v2 sin
/// editar. El portal (<c>millet_pinturas_portal</c>: SELECT e INSERT) inserta la orden; el ERP (<c>millet_erp_integracion</c>:
/// SELECT y UPDATE solo de las 4 columnas del acuse) la lee, la ingesta con el handler real sobre Facturación en memoria y
/// escribe el acuse. Cada caso usa sus propios folios <c>I139-*</c>; quedan en la tabla (el portal no puede borrar).
/// </summary>
public sealed class PinturasSqlServerLocalTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Sucursal = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static string Erp => Environment.GetEnvironmentVariable(PinturasSqlServerFactAttribute.Variable)!;
    private static string Portal => Environment.GetEnvironmentVariable(PinturasSqlServerFactAttribute.VariablePortal)!;

    private sealed class Fabrica(string cs) : IIntegracionSqlConnectionFactory
    {
        public System.Data.Common.DbConnection CreateConnection() => new SqlConnection(cs);
    }
    private sealed class SucursalFija : IPinturasSucursalResolver
    {
        public Task<Guid?> ResolverAsync(string clave, CancellationToken ct) => Task.FromResult<Guid?>(Sucursal);
    }
    private sealed class Empresas(Guid? actual) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = actual;
        public bool IsBypassed => Current is null;
        public IDisposable Bypass() => new Nada();
        private sealed class Nada : IDisposable { public void Dispose() { } }
    }
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => Ahora; }

    // Maestros del ERP: existe el cliente 900123 y el servicio de recubrimiento de la prueba; lo demás no existe.
    private sealed class Clientes : IClientesReadPort
    {
        public static readonly Guid Id = Guid.NewGuid();
        public Task<ClienteFiscalLectura?> ObtenerAsync(Guid clienteId, CancellationToken ct) => throw new NotSupportedException();
        public Task<ClienteFiscalLectura?> ResolverPorReferenciaAsync(string referencia, CancellationToken ct) =>
            Task.FromResult(referencia == "900123"
                ? new ClienteFiscalLectura(Id, "AAA010101AAA", "Aluminios SA", "601", "97000", "G03", "01", "PUE", "MXN", false)
                : null);
        public Task<IReadOnlyList<ClienteBusquedaItem>> BuscarAsync(string? rfc, string? razonSocial, int limit, CancellationToken ct) =>
            throw new NotSupportedException();
    }
    private sealed class Productos(string servicio) : IProductosReadPort
    {
        public Task<ProductoFiscalLectura?> ObtenerAsync(Guid productoId, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProductoFiscalLectura?> ResolverPorReferenciaAsync(string referencia, CancellationToken ct) =>
            Task.FromResult(referencia == servicio
                ? new ProductoFiscalLectura(Guid.NewGuid(), "Pintura", "73181100", "MTK", "02", 0.16m, null, null, "PlantaPintura",
                    EsServicioRecubrimiento: true)
                : null);
        public Task<IReadOnlyList<ProductoAwBusquedaItem>> BuscarAsync(string? r, string? d, int limit, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed record Caso(PinturasPedidosAdapter Lector, FacturacionDbContext Db, ImportarPedidoPlantaPinturaHandler Handler, string Servicio);

    private static async Task<Caso> CrearAsync()
    {
        // Catálogo de servicios: lo publica el ERP (SELECT/INSERT/UPDATE).
        var servicio = $"I139-S-{Guid.NewGuid():N}"[..20];
        await EjecutarAsync(Erp, "INSERT INTO dbo.pinturas_servicio (clave, nombre) VALUES (@c, N'I13.9 · Pintura blanco mate')", ("@c", servicio));
        var db = new FacturacionDbContext(new DbContextOptionsBuilder<FacturacionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresas(null));
        var lector = new PinturasPedidosAdapter(new Fabrica(Erp), postgres: false, new SucursalFija(),
            Options.Create(new PinturasPedidosOptions { SucursalClave = "PIN", SqlQueryTimeoutSeconds = 15 }),
            NullLogger<PinturasPedidosAdapter>.Instance);
        return new Caso(lector, db, new ImportarPedidoPlantaPinturaHandler(db, new Clientes(), new Productos(servicio), new Reloj()), servicio);
    }

    private static async Task<string> InsertarOrdenAsync(Caso c, int cliente = 900123, decimal iva = 16m)
    {
        var folio = $"I139-{Guid.NewGuid():N}"[..20];
        await EjecutarAsync(Portal,
            "INSERT INTO dbo.pinturas_pedido (folio_orden, cliente_clave, cliente_nombre, obra_clave, obra_nombre, comentarios, servicio_clave, m2, precio_m2, iva_porcentaje) " +
            "VALUES (@f, @cli, N'Aluminios SA', NULL, NULL, N'Prueba I13.9', @s, 125.5, 85.25, @iva)",
            ("@f", folio), ("@cli", cliente), ("@s", c.Servicio), ("@iva", iva));
        return folio;
    }

    private static async Task<int> EjecutarAsync(string cs, string sql, params (string, object?)[] ps)
    {
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(byte? Resultado, Guid? Pedido, string? Error, DateTime? Fecha)> AcuseAsync(string folio)
    {
        await using var cn = new SqlConnection(Erp);
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT erp_resultado, erp_pedido_id, erp_error, erp_procesado_at FROM dbo.pinturas_pedido WHERE folio_orden = @f", cn);
        cmd.Parameters.AddWithValue("@f", folio);
        await using var r = await cmd.ExecuteReaderAsync();
        (await r.ReadAsync()).Should().BeTrue();
        return (r.IsDBNull(0) ? null : r.GetByte(0), r.IsDBNull(1) ? null : r.GetGuid(1), r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetDateTime(3));
    }

    private static async Task<PedidoPlantaPintura?> LeerAsync(Caso c, string folio) =>
        (await c.Lector.LeerPendientesAsync(1000, default)).SingleOrDefault(p => p.NumeroPedido == folio);

    [PinturasSqlServerFact]
    public async Task CA13_7_lee_la_orden_la_vuelve_facturable_y_acusa_creado()
    {
        var c = await CrearAsync();
        var folio = await InsertarOrdenAsync(c);

        var pedido = await LeerAsync(c, folio);
        pedido.Should().NotBeNull();
        pedido!.ClienteRef.Should().Be("900123");
        pedido.SucursalId.Should().Be(Sucursal);
        pedido.Lineas.Should().ContainSingle().Which.Should().Match<LineaPedidoPlantaPintura>(l =>
            l.ProductoRef == c.Servicio && l.Descripcion == "I13.9 · Pintura blanco mate" && l.Cantidad == 125.5m
            && l.Precio == 85.25m && l.TasaIva == 0.16m);

        var resp = await c.Handler.Handle(new ImportarPedidoPlantaPinturaCommand(Empresa, pedido), default);
        resp.Resultado.Should().Be(ResultadoSolicitudAw.Aplicada);
        var creado = await c.Db.PedidosFacturables.SingleAsync();
        creado.Origen.Should().Be(OrigenPedido.PlantaPintura);
        creado.CanalVentaId.Should().Be(CanalesVentaConocidos.PlantaPintura);
        creado.ClienteId.Should().Be(Clientes.Id);

        await c.Lector.MarcarResultadoAsync(folio, ResultadoAcusePlantaPintura.Creado, creado.Id, null, Ahora, default);
        var acuse = await AcuseAsync(folio);
        acuse.Resultado.Should().Be(1);
        acuse.Pedido.Should().Be(creado.Id);
        acuse.Error.Should().BeNull();
        acuse.Fecha.Should().Be(Ahora.UtcDateTime);
        (await LeerAsync(c, folio)).Should().BeNull("con acuse la orden deja de estar pendiente");

        // Un segundo acuse (otra instancia del worker) no pisa el primero.
        await c.Lector.MarcarResultadoAsync(folio, ResultadoAcusePlantaPintura.Error, null, "tarde", Ahora.AddMinutes(1), default);
        (await AcuseAsync(folio)).Should().Be(acuse);
    }

    [PinturasSqlServerFact]
    public async Task I13_9_a_releida_sin_acuse_no_se_duplica()
    {
        var c = await CrearAsync();
        var folio = await InsertarOrdenAsync(c);

        var primera = await c.Handler.Handle(new ImportarPedidoPlantaPinturaCommand(Empresa, (await LeerAsync(c, folio))!), default);
        // El acuse falló: la fila sigue pendiente y se vuelve a leer.
        var releida = await LeerAsync(c, folio);
        releida.Should().NotBeNull();
        var segunda = await c.Handler.Handle(new ImportarPedidoPlantaPinturaCommand(Empresa, releida!), default);

        segunda.PedidoFacturableId.Should().Be(primera.PedidoFacturableId);
        (await c.Db.PedidosFacturables.CountAsync()).Should().Be(1);
        await c.Lector.MarcarResultadoAsync(folio, ResultadoAcusePlantaPintura.Creado, segunda.PedidoFacturableId, null, Ahora, default);
        (await AcuseAsync(folio)).Resultado.Should().Be(1);
    }

    [PinturasSqlServerFact]
    public async Task CA13_7_cliente_inexistente_va_a_excepciones_y_acusa_error_con_campo_y_valor()
    {
        var c = await CrearAsync();
        var folio = await InsertarOrdenAsync(c, cliente: 900999);

        var resp = await c.Handler.Handle(new ImportarPedidoPlantaPinturaCommand(Empresa, (await LeerAsync(c, folio))!), default);
        resp.MotivoExcepcion.Should().Be(MotivoExcepcion.ClienteNoExiste);
        (await c.Db.ExcepcionesImportacion.SingleAsync()).PedidoRef.Should().Be(folio);
        (await c.Db.PedidosFacturables.CountAsync()).Should().Be(0);

        await c.Lector.MarcarResultadoAsync(folio, ResultadoAcusePlantaPintura.Error, null, resp.Detalle + new string('.', 600), Ahora, default);
        var acuse = await AcuseAsync(folio);
        acuse.Resultado.Should().Be(2);
        acuse.Pedido.Should().BeNull();
        acuse.Error.Should().StartWith("cliente_clave='900999': no existe en el ERP").And.HaveLength(500);
    }

    [PinturasSqlServerFact]
    public async Task El_portal_no_puede_escribir_el_acuse_ni_borrar()
    {
        var c = await CrearAsync();
        var folio = await InsertarOrdenAsync(c);
        var update = () => EjecutarAsync(Portal, "UPDATE dbo.pinturas_pedido SET erp_resultado = 1 WHERE folio_orden = @f", ("@f", folio));
        (await update.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229);
        var delete = () => EjecutarAsync(Portal, "DELETE FROM dbo.pinturas_pedido WHERE folio_orden = @f", ("@f", folio));
        (await delete.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229);
        await c.Lector.MarcarResultadoAsync(folio, ResultadoAcusePlantaPintura.Error, null, "limpieza de la prueba", Ahora, default);
    }
}
