using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Productos;
using Xunit.Abstractions;

namespace Millet.Integraciones.Aw.UnitTests.Productos;

/// <summary>
/// Prueba de humo OPT-IN contra A+W real (ADM-07 paso H). No es integración continua ni extremo a extremo:
/// la corre el dueño con <c>aw-smoke</c> (VPN arriba). Solo aserta ESTRUCTURA; no fija IDs, códigos ni descripciones.
/// Sin AW_SMOKE_HOST/DB/USER/PASS se omite (xunit 2.9 no tiene skip dinámico: retorna temprano con mensaje).
/// </summary>
[Trait("Category", "AwReal")]
public sealed partial class AwProductosSqlOrigenRealTests(ITestOutputHelper salida)
{
    private const int Tamano = 50;

    [GeneratedRegex(@"^[^+]+(\+[^+]+)*$")] private static partial Regex Compuesta();
    [GeneratedRegex(@"^\d+(\.\d+)?(\+\d+(\.\d+)?)*$")] private static partial Regex SoloEspesores();

    // Fábrica propia del test: TrustServerCertificate solo aquí, nunca en código de producción.
    private sealed class FabricaHumo(string cadena) : IIntegracionSqlConnectionFactory
    {
        public DbConnection CreateConnection() => new SqlConnection(cadena);
    }

    private static string? Env(string k) => Environment.GetEnvironmentVariable(k) is { Length: > 0 } v ? v : null;

    [Fact]
    public async Task Lector_real_cumple_estructura_esperada()
    {
        string?[] obligatorias = [Env("AW_SMOKE_HOST"), Env("AW_SMOKE_DB"), Env("AW_SMOKE_USER"), Env("AW_SMOKE_PASS")];
        if (obligatorias.Any(v => v is null))
        {
            salida.WriteLine("OMITIDO: faltan AW_SMOKE_HOST/DB/USER/PASS (usa ~/.local/bin/aw-smoke).");
            return;
        }

        // Builder (no concatenación) por caracteres especiales en la contraseña.
        var cadena = new SqlConnectionStringBuilder
        {
            DataSource = obligatorias[0]!, InitialCatalog = obligatorias[1]!,
            UserID = obligatorias[2]!, Password = obligatorias[3]!,
            Encrypt = SqlConnectionEncryptOption.Mandatory, TrustServerCertificate = true,
            ApplicationIntent = ApplicationIntent.ReadOnly,
        }.ConnectionString;

        var paginas = int.TryParse(Env("AW_SMOKE_PAGINAS"), out var p) ? Math.Clamp(p, 1, 5) : 2;
        var excluidos = Env("AW_SMOKE_TIPOS_EXCLUIDOS")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        var origen = new AwProductosSqlOrigen(new FabricaHumo(cadena),
            Options.Create(new AwProductosOptions { TiposExcluidos = excluidos }),
            NullLogger<AwProductosSqlOrigen>.Instance);

        var filas = new List<AwProductoOrigenFila>();
        // AW_SMOKE_CURSOR: arranca después de ese BA_PRODUKT (p. ej. para llegar a laminados/aislantes, que no están al inicio).
        var cursor = Env("AW_SMOKE_CURSOR");
        if (cursor is not null) int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out _).Should().BeTrue("AW_SMOKE_CURSOR debe ser un entero");
        for (var i = 0; i < paginas; i++)
        {
            var pagina = await origen.LeerPaginaAsync(cursor, Tamano, CancellationToken.None);
            filas.AddRange(pagina.Filas);
            if (pagina.SiguienteCursor is null) break; // última página: puede traer menos de Tamano
            pagina.Filas.Should().HaveCount(Tamano);
            if (cursor is not null) int.Parse(pagina.SiguienteCursor, CultureInfo.InvariantCulture)
                .Should().BeGreaterThan(int.Parse(cursor, CultureInfo.InvariantCulture), "el cursor debe avanzar");
            cursor = pagina.SiguienteCursor;
        }

        filas.Should().NotBeEmpty();
        filas.Select(f => f.ProductoRef).Should().OnlyHaveUniqueItems().And.OnlyContain(r => r != null && r.All(char.IsDigit));

        var errores = new Dictionary<string, int>();
        foreach (var f in filas)
        {
            var r = AwProductoSnapshotMapper.Mapear(f, DateTime.UtcNow);
            if (!r.EsValido) { var tipo = (r.Error ?? "?").Split(" (")[0]; errores[tipo] = errores.GetValueOrDefault(tipo) + 1; }
        }
        errores.Should().BeEmpty("todas las filas deben pasar por el mapper");

        var variantes = filas.SelectMany(f => f.Variantes).ToList();
        variantes.Should().OnlyContain(v => v.EspesorMm != 0 && v.AltoMm != 0 && v.AnchoMm != 0, "una medida es nula o > 0, nunca 0");
        var comps = variantes.Select(v => v.Composicion).Where(c => c is not null).Select(c => c!).ToList();
        comps.Should().OnlyContain(c => Compuesta().IsMatch(c));
        comps.Should().OnlyContain(c => SoloEspesores().IsMatch(c), "la composición solo lleva capas con espesor");

        (await origen.LeerPorReferenciaAsync("0", CancellationToken.None)).Should().BeNull();
        (await origen.LeerPorReferenciaAsync("abc", CancellationToken.None)).Should().BeNull();
        var primera = filas[0];
        var releida = await origen.LeerPorReferenciaAsync(primera.ProductoRef!, CancellationToken.None);
        releida.Should().NotBeNull();
        releida!.Should().BeEquivalentTo(primera);

        // Solo agregados: sin descripciones ni códigos.
        salida.WriteLine($"filas leídas: {filas.Count}; bajas: {filas.Count(f => f.Baja)}; con variante: {filas.Count(f => f.Variantes.Count > 0)}; con composición: {comps.Count}");
        salida.WriteLine("unidades normalizadas: " + string.Join(", ", filas.GroupBy(f => AwProductoSnapshotMapper.NormalizarUnidad(f.UnidadMedida) ?? "(sin equivalencia)")
            .OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}")));
        salida.WriteLine("errores del mapper: " + (errores.Count == 0 ? "ninguno" : string.Join(", ", errores.Select(e => $"{e.Key}={e.Value}"))));
    }
}
