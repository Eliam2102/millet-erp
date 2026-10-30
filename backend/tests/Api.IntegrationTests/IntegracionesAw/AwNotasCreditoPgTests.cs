using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.Integraciones.Aw.Infrastructure.NotasCredito;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.Integraciones.Aw.Infrastructure.Retorno;
using Npgsql;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>
/// 86e3wjt5r · tabla puente de notas de crédito (script 17 / schema.sql de la demo) y fila NotaCredito de
/// aw_retorno, contra la copia desechable de demo (PostgreSQL): mismo SQL que Real. Se corren en CI.
/// </summary>
[Collection("AwClientesSync")]
public class AwNotasCreditoPgTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private string _cs = "";

    public async Task InitializeAsync()
    {
        _cs = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")!;
        await EjecutarAsync("DROP SCHEMA IF EXISTS dbo CASCADE; DROP SCHEMA IF EXISTS aw_origen CASCADE");
        await EjecutarAsync(await File.ReadAllTextAsync(Path.Combine(AwOrigenPgTests.DirectorioOrigenDemo(), "schema.sql")));
    }

    public async Task DisposeAsync() =>
        await EjecutarAsync("DROP SCHEMA IF EXISTS dbo CASCADE; DROP SCHEMA IF EXISTS aw_origen CASCADE");

    private static readonly IOptions<AwPedidosOptions> Opciones =
        Options.Create(new AwPedidosOptions { SqlConnectTimeoutSeconds = 5, SqlQueryTimeoutSeconds = 15 });

    private AwNotasCreditoAdapter Adapter() => new(new AwOrigenPg.Fabrica(_cs), Opciones);

    private async Task EjecutarAsync(string sql, params (string, object?)[] ps)
    {
        await using var cn = new NpgsqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private Task InsertarNotaAsync(string numero, long version = 1, decimal importe = 1500m, string tipo = "Bonificacion") =>
        EjecutarAsync(
            "INSERT INTO dbo.aw_solicitud_nota_credito (numero_nota, version, numero_pedido, factura_folio, cliente_ref, tipo_nota, importe, descripcion) " +
            "VALUES (@n, @v, '900101', NULL, 'CLI-1', @t, @i, 'Descuento')",
            ("n", numero), ("v", version), ("t", tipo), ("i", importe));

    [Fact]
    public async Task Una_nota_de_muestra_se_lee_con_todos_sus_campos()
    {
        await InsertarNotaAsync("NC-100");

        var pendientes = await Adapter().LeerNotasPendientesAsync(10, default);

        var n = pendientes.Should().ContainSingle().Subject;
        n.NumeroNota.Should().Be("NC-100");
        n.Version.Should().Be(1);
        n.NumeroPedido.Should().Be("900101");
        n.FacturaFolio.Should().BeNull();
        n.ClienteRef.Should().Be("CLI-1");
        n.Motivo.Should().Be("Bonificacion");
        n.Importe.Should().Be(1500m);
    }

    [Fact]
    public async Task El_acuse_saca_la_nota_de_los_pendientes_y_conserva_el_claim()
    {
        await InsertarNotaAsync("NC-100");
        var nota = (await Adapter().LeerNotasPendientesAsync(10, default)).Single();
        var ncId = Guid.NewGuid();

        await Adapter().EscribirAcuseAsync(new AcuseNotaCreditoAw(nota.SolicitudId, ncId, "PorAutorizar", ResultadoSolicitudAw.Aplicada, null), default);
        // Un acuse posterior sin claim (p. ej. idempotente) no lo degrada a NULL.
        await Adapter().EscribirAcuseAsync(new AcuseNotaCreditoAw(nota.SolicitudId, null, null, ResultadoSolicitudAw.Aplicada, "idempotente"), default);

        (await Adapter().LeerNotasPendientesAsync(10, default)).Should().BeEmpty();
        await using var cn = new NpgsqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT erp_nota_credito_id, estado_erp, resultado, procesada_at FROM dbo.aw_solicitud_nota_credito", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        (await r.ReadAsync()).Should().BeTrue();
        r.GetGuid(0).Should().Be(ncId);
        r.GetString(1).Should().Be("PorAutorizar");
        r.GetInt16(2).Should().Be(1);
        r.IsDBNull(3).Should().BeFalse();
    }

    [Fact]
    public async Task Una_nota_rechazada_no_se_relee_y_su_version_nueva_si()
    {
        await InsertarNotaAsync("NC-100");
        var v1 = (await Adapter().LeerNotasPendientesAsync(10, default)).Single();
        await Adapter().EscribirAcuseAsync(new AcuseNotaCreditoAw(v1.SolicitudId, null, null, ResultadoSolicitudAw.Rechazada, "importe mayor que el saldo"), default);

        await InsertarNotaAsync("NC-100", version: 2, importe: 900m);

        var pendientes = await Adapter().LeerNotasPendientesAsync(10, default);
        pendientes.Should().ContainSingle().Which.Version.Should().Be(2);
    }

    [Fact]
    public async Task La_misma_nota_y_version_no_se_inserta_dos_veces()
    {
        await InsertarNotaAsync("NC-100");

        var act = () => InsertarNotaAsync("NC-100");

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task Las_notas_se_leen_en_orden_por_numero_y_version_y_respetan_el_maximo()
    {
        await InsertarNotaAsync("NC-2");
        await InsertarNotaAsync("NC-1", version: 2);
        await InsertarNotaAsync("NC-1");

        var pendientes = await Adapter().LeerNotasPendientesAsync(2, default);

        pendientes.Select(p => (p.NumeroNota, p.Version)).Should().Equal(("NC-1", 1L), ("NC-1", 2L));
    }

    [Fact]
    public async Task La_nota_timbrada_deja_la_fila_Documento_NotaCredito_en_aw_retorno_una_sola_vez()
    {
        var retorno = new AwRetornoAdapter(new AwOrigenPg.Fabrica(_cs), Opciones, AwRetornoAdapter.CandadoPostgres);
        var servicio = new AwRetornoDocumentoService(retorno, new SinVersiones(), NullLogger<AwRetornoDocumentoService>.Instance);
        var nota = new FacturaTimbradaRetornoPayload("UUID-NC-1", 1500m, "NC-000001", "900101", 3);

        (await servicio.RegistrarNotaCreditoAsync(nota, default)).Should().Be(1);
        (await servicio.RegistrarNotaCreditoAsync(nota, default)).Should().BeNull();   // reentrega del evento

        await using var cn = new NpgsqlConnection(_cs);
        await cn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT concat_ws('|', tipo, version_pedido, tipo_documento, folio, uuid, importe) FROM dbo.aw_retorno WHERE numero_pedido = '900101'", cn);
        (await cmd.ExecuteScalarAsync()).Should().Be("3|3|NotaCredito|NC-000001|UUID-NC-1|1500.00");
    }

    private sealed class SinVersiones : IAwVersionPedidoReadPort
    {
        public Task<long?> VersionAplicadaAsync(string numeroPedido, CancellationToken ct) => Task.FromResult<long?>(null);
    }
}
