using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Clientes;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Xunit.Abstractions;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>
/// Corrida OPT-IN de clientes contra la copia local <c>AW_FULL</c> de A+W (SQL Server, solo lectura, usuario <c>aw_ro</c>)
/// hacia la BD PostgreSQL desechable de las pruebas de integración. Mide el comportamiento real del sincronizador
/// (modo real y modo demo) y escribe SOLO conteos agregados en <c>AW_FULL_SALIDA</c>.
/// Sin <c>AW_FULL_CONN</c> o <c>AW_FULL_SALIDA</c> cada prueba retorna temprano con «OMITIDO».
/// Nunca escribe en SQL Server, nunca llama a <c>aw-sandbox.sh</c> y nunca registra RFC, nombres ni domicilios.
/// La BD de destino debe estar recién creada (<c>tools/validate-integration-isolated.sh</c>).
/// </summary>
[Trait("Category", "AwFull")]
[Collection("AwClientesSync")]
public sealed class AwFullCorridaClientesTests(WebApplicationFactory<Program> factory, ITestOutputHelper salida)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const int Lote = 500;
    private const int ManualesSembrados = 20;
    private const string CodigoMonedaSinEquivalencia = "moneda_sin_equivalencia";
    private static readonly string? ConnEnv = Environment.GetEnvironmentVariable("AW_FULL_CONN") is { Length: > 0 } c ? c : null;
    private static readonly string? SalidaEnv = Environment.GetEnvironmentVariable("AW_FULL_SALIDA") is { Length: > 0 } s ? s : null;
    private static readonly JsonSerializerOptions JsonIndentado = new() { WriteIndented = true };

    /// <summary>Fila mínima de KU_KUNDEN leída en memoria; nunca se serializa ni se registra.</summary>
    private sealed record OrigenUst(int Id, string? UstId, string? Moneda);

    /// <summary>Checksum de una tabla de A+W; sirve para probar que la corrida no alteró la copia.</summary>
    private sealed record HuellaTabla(string Tabla, long Filas, string Checksum);

    [Fact]
    public async Task Corrida_AW_FULL_modo_real()
    {
        if (!Configurado()) return;
        await CorrerAsync("real", o => o);
    }

    [Fact]
    public async Task Corrida_AW_FULL_modo_demo()
    {
        if (!Configurado()) return;
        await CorrerAsync("demo", o => new OrigenConFiscales(o));
    }

    private bool Configurado()
    {
        if (ConnEnv is not null && SalidaEnv is not null) return true;
        salida.WriteLine("OMITIDO: falta AW_FULL_CONN/AW_FULL_SALIDA.");
        return false;
    }

    private async Task CorrerAsync(string modo, Func<IAwClientesOrigen, IAwClientesOrigen> envolver)
    {
        // 1) Destino nuevo: la corrida no admite clientes A+W previos.
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            if (await db.Clientes.AnyAsync(c => c.Origen == OrigenMaster.Aw))
                Assert.Fail("La corrida exige una BD recién creada (tools/validate-integration-isolated.sh).");
        }

        // 2) Huella antes.
        var antes = await HuellaAsync();

        // 3) Lectura de origen en memoria y métricas agregadas.
        var filas = await LeerOrigenAsync();
        var ustVacios = filas.Count(f => string.IsNullOrWhiteSpace(f.UstId));
        var ustGenericos = filas.Count(f => EsGenerico(f.UstId));
        var ustInvalidos = filas.Count(f => !string.IsNullOrWhiteSpace(f.UstId) && !EsGenerico(f.UstId) && RfcCliente.ClaveLigado(f.UstId) is null);
        var gruposUst = filas.Select(f => (f.Id, Clave: RfcCliente.ClaveLigado(f.UstId)))
            .Where(x => x.Clave is not null)
            .GroupBy(x => x.Clave!)
            .ToList();
        var gruposDuplicados = gruposUst.Where(g => g.Count() > 1).ToList();
        var lectura = new
        {
            Total = filas.Count,
            UstVacios = ustVacios,
            UstGenericos = ustGenericos,
            UstInvalidos = ustInvalidos,
            ClavesLigables = gruposUst.Count,
            FilasConClaveLigable = gruposUst.Sum(g => g.Count()),
            GruposClaveDuplicada = gruposDuplicados.Count,
            ClientesEnGruposDuplicados = gruposDuplicados.Sum(g => g.Count()),
            MaximoClientesPorGrupo = gruposDuplicados.Count == 0 ? 0 : gruposDuplicados.Max(g => g.Count()),
            ReferenciasExtraEsperadas = gruposDuplicados.Sum(g => g.Count() - 1),
        };

        // 4) Siembra de 20 manuales: 10 claves de grupos duplicados y 10 claves únicas, por ID mínimo.
        var claveDuplicada = gruposDuplicados.OrderBy(g => g.Min(x => x.Id)).Take(ManualesSembrados / 2).Select(g => g.Key).ToList();
        var claveUnica = gruposUst.Where(g => g.Count() == 1).OrderBy(g => g.Min(x => x.Id)).Take(ManualesSembrados / 2).Select(g => g.Key).ToList();
        var claves = claveDuplicada.Concat(claveUnica).ToList();
        Assert.Equal(ManualesSembrados, claves.Count);
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            for (var i = 0; i < claves.Count; i++)
                db.Clientes.Add(new Cliente(Guid.CreateVersion7(), $"E2E-MAN-{i + 1:00}", $"E2E MANUAL {i + 1:00}", rfc: claves[i]));
            await db.SaveChangesAsync();
        }

        // 5) Barrido 1 y barrido 2 con el origen real (envuelto según el modo).
        var opts = Opts();
        var sw1 = Stopwatch.StartNew();
        var barrido1 = await Barrido(envolver(OrigenReal(opts)), opts);
        var seg1 = sw1.Elapsed.TotalSeconds;
        var sw2 = Stopwatch.StartNew();
        var barrido2 = await Barrido(envolver(OrigenReal(opts)), opts);
        var seg2 = sw2.Elapsed.TotalSeconds;
        Assert.Equal(0, barrido2.Creados);

        // 6) Estado del destino (solo conteos).
        using var scopeDestino = factory.Services.CreateScope();
        using var bypassDestino = scopeDestino.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var dbDestino = scopeDestino.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var clientes = await dbDestino.Clientes.AsNoTracking().ToListAsync();
        var registros = await dbDestino.Set<ClienteSincronizacionAw>().AsNoTracking().ToListAsync();
        var ligados = await dbDestino.Set<ClienteLigadoAw>().AsNoTracking().ToListAsync();
        var ustPorCliente = registros.ToDictionary(r => r.ClienteId, r => r.CandidatoFiscalUstId);

        var rfcDuplicadosErp = clientes.Where(c => RfcCliente.EsLigable(c.Rfc))
            .GroupBy(c => RfcCliente.Normalizar(c.Rfc))
            .Count(g => g.Count() > 1);

        // Claves de ligado por cliente: su RFC y el UST_ID de su registro de origen (sin repetir dentro del cliente).
        var clavesPorCliente = clientes.Select(c =>
        {
            var set = new HashSet<string>();
            if (RfcCliente.ClaveLigado(c.Rfc) is { } rfcClave) set.Add(rfcClave);
            if (ustPorCliente.TryGetValue(c.Id, out var ust) && RfcCliente.ClaveLigado(ust) is { } ustClave) set.Add(ustClave);
            return (Cliente: c, Claves: set);
        }).ToList();
        var porClave = clavesPorCliente.SelectMany(x => x.Claves.Select(k => (Clave: k, x.Cliente))).GroupBy(x => x.Clave).ToList();
        var clavesCompartidasConManual = porClave.Count(g => g.Count() > 1 && g.Any(x => x.Cliente.Origen == OrigenMaster.Manual));
        var clavesRepetidasEntreAw = porClave.Count(g => g.Count() > 1 && g.All(x => x.Cliente.Origen == OrigenMaster.Aw));

        var manualesRfc = clientes.Where(c => c.Origen == OrigenMaster.Manual && RfcCliente.EsLigable(c.Rfc))
            .Select(c => RfcCliente.Normalizar(c.Rfc)).ToHashSet();
        var clavesAw = clavesPorCliente.Where(x => x.Cliente.Origen == OrigenMaster.Aw).SelectMany(x => x.Claves).ToHashSet();
        var pendientes = ligados.Where(l => l.RevisadoEn == null).ToList();
        var conCandidatoManual = 0;
        var conCandidatoAw = 0;
        foreach (var l in pendientes)
        {
            var k = RfcCliente.Normalizar(l.Rfc);
            if (manualesRfc.Contains(k)) conCandidatoManual++;
            else if (clavesAw.Contains(k)) conCandidatoAw++;
        }
        var sinCandidato = pendientes.Count - conCandidatoManual - conCandidatoAw;

        var monedaPorReferencia = filas.ToDictionary(f => f.Id.ToString(), f => f.Moneda ?? "<null>");
        var erroresPorCodigo = barrido1.ErroresPorReferencia.GroupBy(e => e.Codigo).OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());
        var pendientesPorMoneda = barrido1.ErroresPorReferencia.Where(e => e.Codigo == CodigoMonedaSinEquivalencia)
            .GroupBy(e => monedaPorReferencia.GetValueOrDefault(e.Referencia, "<desconocida>"))
            .OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());

        // 7) Huella después.
        var despues = await HuellaAsync();

        // 8) Métricas en JSON (sin RFC, nombres ni domicilios) y aserciones finales.
        var resumen = new
        {
            modo,
            fechaUtc = DateTime.UtcNow,
            huellaAntes = antes,
            huellaDespues = despues,
            barrido1 = Resumen(barrido1, seg1),
            barrido2 = Resumen(barrido2, seg2),
            erroresPorCodigo,
            pendientesPorMoneda,
            lectura,
            manualesSembrados = ManualesSembrados,
            ligadosPendientes = pendientes.Count,
            conflictosConManuales = conCandidatoManual,
            ligadosConCandidatoManual = conCandidatoManual,
            ligadosConCandidatoAw = conCandidatoAw,
            ligadosSinCandidato = sinCandidato,
            rfcDuplicadosErp,
            clavesCompartidasConManual,
            clavesRepetidasEntreAw,
        };
        Directory.CreateDirectory(SalidaEnv!);
        var ruta = Path.Combine(SalidaEnv!, $"clientes-{modo}-{DateTime.UtcNow:yyyyMMddHHmm}.json");
        await File.WriteAllTextAsync(ruta, JsonSerializer.Serialize(resumen, JsonIndentado));
        salida.WriteLine($"modo={modo} leidos={barrido1.Leidos} creados={barrido1.Creados} errores={barrido1.Errores} " +
            $"pendientes={barrido1.Pendientes} creadosBarrido2={barrido2.Creados} rfcDuplicadosErp={rfcDuplicadosErp} " +
            $"clavesCompartidasConManual={clavesCompartidasConManual} clavesRepetidasEntreAw={clavesRepetidasEntreAw}");

        Assert.Equal(antes, despues);
        Assert.Equal(0, rfcDuplicadosErp);
        Assert.Equal(0, clavesCompartidasConManual);
    }

    private static object Resumen(AwClientesEjecucion e, double seg) => new
    {
        e.Leidos, e.Creados, e.Actualizados, e.SinCambios, e.Conflictos, e.Errores, e.Pendientes,
        Estado = e.Estado.ToString(),
        DuracionSeg = Math.Round(seg, 1),
    };

    // ---------- origen ----------
    private static string Limpio(string? valor) => RfcCliente.Normalizar(valor)
        .Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal);

    private static bool EsGenerico(string? ust) => Limpio(ust) is "XAXX010101000" or "XEXX010101000";

    /// <summary>Solo SELECT. Columnas: 0=ID, 10=UST_ID, 16=WAEHRUNG (mismo orden que <c>AwClientesSqlOrigen</c>).</summary>
    private static async Task<List<OrigenUst>> LeerOrigenAsync()
    {
        const string sql = "SELECT ID, UST_ID, WAEHRUNG FROM SYSADM.KU_KUNDEN WHERE ID > 0";
        var lista = new List<OrigenUst>();
        await using var cn = new SqlConnection(Cadena());
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add(new OrigenUst(rd.GetInt32(0),
                rd.IsDBNull(1) ? null : rd.GetString(1),
                rd.IsDBNull(2) ? null : rd.GetString(2).Trim()));
        return lista;
    }

    private static async Task<List<HuellaTabla>> HuellaAsync()
    {
        const string sql =
            "SELECT 'KU_KUNDEN', COUNT_BIG(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM SYSADM.KU_KUNDEN " +
            "UNION ALL SELECT 'KA_ZAHLBED', COUNT_BIG(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM SYSADM.KA_ZAHLBED " +
            "UNION ALL SELECT 'KA_WAEHRUNGEN', COUNT_BIG(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM SYSADM.KA_WAEHRUNGEN";
        var lista = new List<HuellaTabla>();
        await using var cn = new SqlConnection(Cadena());
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 120 };
        await using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            lista.Add(new HuellaTabla(rd.GetString(0), rd.GetInt64(1), rd.IsDBNull(2) ? "null" : rd.GetValue(2).ToString()!));
        return lista;
    }

    /// <summary>TrustServerCertificate SOLO aquí (copia local con certificado autofirmado), nunca en producción.</summary>
    private static string Cadena()
    {
        var b = new SqlConnectionStringBuilder(ConnEnv!)
        {
            Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = true,
            ApplicationIntent = ApplicationIntent.ReadOnly,
        };
        return b.ConnectionString;
    }

    private static AwClientesOptions Opts() => new()
    {
        Origen = AwClientesOrigenTipo.Sql, LecturaHabilitada = true, AplicacionHabilitada = true, TamanoLote = Lote,
        SqlConnectTimeoutSeconds = 15, SqlQueryTimeoutSeconds = 120,
        // Sin '<indf>': así se miden los pendientes por moneda cruda.
        MapeoMoneda = new(StringComparer.OrdinalIgnoreCase) { ["PESOSMX"] = "MXN", ["USD"] = "USD", ["Euro"] = "EUR" },
    };

    private sealed class FabricaSql(string cadena) : IIntegracionSqlConnectionFactory
    {
        public System.Data.Common.DbConnection CreateConnection() => new SqlConnection(cadena);
    }

    private static AwClientesSqlOrigen OrigenReal(AwClientesOptions opts) =>
        new(new FabricaSql(Cadena()), Options.Create(opts), NullLogger<AwClientesSqlOrigen>.Instance);

    /// <summary>Marca las filas como «con fiscales de origen» (modo demo: A+W copia RFC y datos fiscales).</summary>
    private sealed class OrigenConFiscales(IAwClientesOrigen real) : IAwClientesOrigen
    {
        public async Task<AwClienteOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct) =>
            await real.LeerPorReferenciaAsync(referencia, ct) is { } f ? f with { AplicarFiscalesDeOrigen = true } : null;

        public async Task<AwClientesPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
        {
            var p = await real.LeerPaginaAsync(cursor, tamano, ct);
            return new AwClientesPagina(p.Filas.Select(f => f with { AplicarFiscalesDeOrigen = true }).ToList(), p.SiguienteCursor);
        }
    }

    // ---------- plomería (patrón de AwClientesSandboxTests) ----------
    private async Task<T> ConSync<T>(IAwClientesOrigen origen, AwClientesOptions opts,
        Func<AwClientesSincronizador, Task<T>> f)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var services = new ServiceCollection();
        services.AddSingleton(origen);
        var sync = new AwClientesSincronizador(sp.GetRequiredService<IntegracionesAwDbContext>(),
            sp.GetRequiredService<CompartidoDbContext>(), sp.GetRequiredService<AplicarClienteAwService>(),
            services.BuildServiceProvider(), Options.Create(opts), TimeProvider.System,
            NullLogger<AwClientesSincronizador>.Instance);
        return await f(sync);
    }

    private async Task LimpiarVivosAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones
            .Where(e => e.Estado == AwClientesEjecucionEstado.Pendiente || e.Estado == AwClientesEjecucionEstado.EnCurso)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Estado, AwClientesEjecucionEstado.Cancelada));
    }

    private async Task<AwClientesEjecucion> Barrido(IAwClientesOrigen origen, AwClientesOptions opts)
    {
        await LimpiarVivosAsync();
        var id = await ConSync(origen, opts, async s =>
        {
            var i = await s.IniciarBarridoAsync("test-aw-full", CancellationToken.None);
            await s.EjecutarAsync(i, CancellationToken.None);
            return i;
        });
        return await Ejec(id);
    }

    private async Task<AwClientesEjecucion> Ejec(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones
            .AsNoTracking().Include(e => e.ErroresPorReferencia).SingleAsync(e => e.Id == id);
    }
}
