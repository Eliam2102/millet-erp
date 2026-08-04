using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Millet.Almacen.Application.Reportes;
using Xunit;

namespace Millet.Almacen.UnitTests.Reportes;

/// <summary>
/// Test de CONTRATO de los reportes de almacén: cada <c>columnas[].clave</c>
/// debe existir como key serializada (camelCase) en la fila del reporte, y
/// cada key de <c>totales</c> debe ser una clave de columna.
///
/// <para>El front (<c>ReporteShell</c>) hace <c>fila[clave]</c> sensible a
/// mayúsculas → un desalineo de casing deja la celda en "—". El status HTTP
/// sigue siendo 200, así que este test determinista (sin BD, sin ruido
/// frágil-por-datos) es el único que atrapa la regresión de casing.</para>
/// </summary>
public class ReportesContratoTests
{
    // Mismas opciones que la API minimal (System.Text.Json web defaults → camelCase).
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void MpCnk_ColumnasYTotales_MatcheanKeysSerializadas()
    {
        var fila = new ExistenciaMpCnkFila(
            Guid.NewGuid(), Guid.NewGuid(), 100m, 90m, 25m, 2500m,
            "Materiales directos MID-MP", "ACC86013", "ENDUROSHIELD GLASS");

        AssertContrato(
            ExistenciaMpCnkHandler.Columnas,
            fila,
            ExistenciaMpCnkHandler.CalcularTotales([fila]).Keys);
    }

    [Fact]
    public void Alfak_ColumnasYTotales_MatcheanKeysSerializadas()
    {
        var fila = new AlfakHistorialFila(
            Guid.NewGuid(), Guid.NewGuid(), 0m, 40m, 12m, 0m, 0m, 28m, 1250m, 35000m,
            "Materiales directos MID-MP", "ACC86013", "ENDUROSHIELD GLASS");

        AssertContrato(
            AlfakHistorialAlmacenHandler.Columnas,
            fila,
            AlfakHistorialAlmacenHandler.CalcularTotales([fila]).Keys);
    }

    private static void AssertContrato<TFila>(
        IReadOnlyList<ColumnaDescriptor> columnas,
        TFila filaMuestra,
        IEnumerable<string> totalesKeys)
    {
        var json = JsonSerializer.Serialize(filaMuestra, Web);
        using var doc = JsonDocument.Parse(json);
        var keysFila = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

        // (1) cada columna existe como key en la fila serializada → no más "—".
        foreach (var col in columnas)
        {
            Assert.True(
                keysFila.Contains(col.Clave),
                $"Columna '{col.Clave}' no existe como key en la fila serializada. Keys: {string.Join(", ", keysFila)}");
        }

        // (2) cada total se pinta bajo una columna (su key debe ser clave de columna).
        var claves = columnas.Select(c => c.Clave).ToHashSet();
        foreach (var k in totalesKeys)
        {
            Assert.True(
                claves.Contains(k),
                $"Key de totales '{k}' no corresponde a ninguna columna. Columnas: {string.Join(", ", claves)}");
        }
    }
}
