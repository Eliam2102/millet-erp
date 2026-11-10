using System.Globalization;
using System.Text;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Conciliaciones.ExtractoParser;
using Xunit.Abstractions;

namespace Millet.Tesoreria.UnitTests.Conciliaciones;

/// <summary>
/// Archivos anonimizados con el formato exacto de los auxiliares reales de Tesorería (correo del 6-nov):
/// mismas columnas, apóstrofos, fechas, rellenos, números y fin de línea; cuentas, referencias e
/// importes son inventados. Los archivos reales no se suben al repo (ver <see cref="ExtractosRealesLocalFactAttribute"/>).
/// </summary>
internal static class ExtractosFixtures
{
    public const decimal SaldoFinalSantander = 286_861.61m;

    private const string EncabezadoSantander =
        "Cuenta,Fecha,Hora,Sucursal,Descripcion,Cargo/Abono,Importe,Saldo,Referencia,Concepto,Banco Participante," +
        "Clabe Beneficiario,Nombre Beneficiario,Cta Ordenante,Nombre Ordenante,Codigo Devolucion,Causa Devolucion," +
        "RFC Beneficiario,RFC Ordenante,Clave de Rastreo,Descripcion Larga";

    /// <summary>Referencia del cargo i (renglones con i % 3 == 2 son cargos).</summary>
    public static string ReferenciaSantander(int i) => $"{100000 + i}F{i % 1000:D3}";
    public static decimal ImporteSantander(int i) => i % 3 == 2 ? 2_000m + i * 13.70m : 1_000m + i * 7.13m;

    /// <summary>Auxiliar Santander: encabezado + <paramref name="renglones"/> movimientos del 1 al 30 de octubre, CRLF, sin salto final.</summary>
    public static string Santander(int renglones = 266)
    {
        decimal neto = Enumerable.Range(0, renglones).Sum(i => i % 3 == 2 ? -ImporteSantander(i) : ImporteSantander(i));
        decimal saldo = SaldoFinalSantander - neto;
        var filas = new List<string> { EncabezadoSantander };
        for (int i = 0; i < renglones; i++)
        {
            int dia = 1 + i * 30 / renglones;
            bool cargo = i % 3 == 2;
            decimal importe = ImporteSantander(i);
            saldo += cargo ? -importe : importe;
            string desc = cargo ? "CARGO TRANSFERENCIA ENLACE" : "DEPOSITO VENTAS DEL DIA";
            string referencia = cargo ? ReferenciaSantander(i) : $"{9000000 + i}";
            string concepto = cargo ? "TRASPASO" : $"AFIL.-{9000000 + i:D9}";
            filas.Add(string.Create(CultureInfo.InvariantCulture,
                $"'65500000001,'{dia:D2}102026',{5 + i % 12:D2}:{i % 60:D2},'0981',{desc,-40},{(cargo ? '-' : '+')},{importe:F2},{saldo:F2},{referencia},{concepto},,,,,,,,,,,{desc,-130}"));
        }
        return string.Join("\r\n", filas);
    }

    /// <summary>
    /// Auxiliar BBVA: sin encabezado, del más reciente al más antiguo, LF con salto final.
    /// Incluye dos depósitos idénticos el mismo día y la palabra SANTANDER en una referencia (CIE).
    /// </summary>
    public static string BbvaAuxiliar(decimal saldoInicial, out decimal saldoFinal, int renglones = 40)
    {
        var filas = new List<string>();
        decimal saldo = saldoInicial;
        for (int i = 0; i < renglones; i++)
        {
            int dia = 1 + i * 30 / renglones;
            bool cargo = i % 10 == 9;
            bool repetido = i is 4 or 5; // mismo día, concepto, referencia e importe
            decimal importe = repetido ? 2_000m : cargo ? 15_000m + i : 500m + i * 3.25m;
            saldo += cargo ? -importe : importe;
            string concepto = cargo ? "PAGO CUENTA DE TERCERO" : repetido || i % 2 == 0 ? "SPEI RECIBIDO" : "CIE INTERBANCARIO";
            string referencia = repetido ? "BNET    0000000001 Transf a cliente" : i % 2 == 1 ? $"SANTANDER 0{i:D16}" : $"20261{i:D2}Transferencia ficticia";
            filas.Add(string.Create(CultureInfo.InvariantCulture,
                $"{dia:D2} oct 2026,0100000000,VENTAS         ,{concepto[..Math.Min(15, concepto.Length)],-15},{concepto,-30},{referencia,-40},{(cargo ? 1 : 0)},{importe:0000000000000.00},0,{saldo:0000000000000.00}"));
        }
        saldoFinal = saldo;
        filas.Reverse();
        return string.Join("\n", filas) + "\n";
    }
}

public sealed class ExtractosRealesTests
{
    private readonly ExtractoParserFactory _factory = new();

    [Fact]
    public void Santander_auxiliar_carga_266_movimientos_del_1_al_30_de_octubre_con_signo_y_saldo_final()
    {
        var res = _factory.ObtenerParser("SANTANDER", null).Parse(ExtractosFixtures.Santander());

        res.TotalLineas.Should().Be(266);
        res.Lineas.Min(l => l.Fecha).Should().Be(new DateOnly(2026, 10, 1));
        res.Lineas.Max(l => l.Fecha).Should().Be(new DateOnly(2026, 10, 30));
        res.SaldoFinal.Should().Be(ExtractosFixtures.SaldoFinalSantander);
        res.Lineas.Select(l => l.LineaHash).Distinct().Should().HaveCount(266);

        var deposito = res.Lineas[0];
        deposito.Descripcion.Should().Be("DEPOSITO VENTAS DEL DIA");
        deposito.Referencia.Should().Be("9000000");
        deposito.Abono.Should().Be(1_000m);
        deposito.Cargo.Should().BeNull();

        var cargo = res.Lineas[2];
        cargo.Descripcion.Should().Be("CARGO TRANSFERENCIA ENLACE");
        cargo.Referencia.Should().Be(ExtractosFixtures.ReferenciaSantander(2));
        cargo.Cargo.Should().Be(ExtractosFixtures.ImporteSantander(2));
        cargo.Abono.Should().BeNull();

        // Saldo corrido: el saldo de cada renglón = anterior + abono − cargo.
        for (int i = 1; i < res.Lineas.Count; i++)
            res.Lineas[i].Saldo.Should().Be(res.Lineas[i - 1].Saldo + (res.Lineas[i].Abono ?? 0) - (res.Lineas[i].Cargo ?? 0));
    }

    [Fact]
    public void Bbva_auxiliar_sin_encabezado_se_ordena_cronologicamente_y_cuadra_el_saldo_final()
    {
        string archivo = ExtractosFixtures.BbvaAuxiliar(242_156.17m, out decimal saldoFinal);

        var res = _factory.ObtenerParser("BBVA", archivo).Parse(archivo);

        res.TotalLineas.Should().Be(40);
        res.Lineas.Select(l => l.NumeroLinea).Should().BeInAscendingOrder();
        res.Lineas.Select(l => l.Fecha).Should().BeInAscendingOrder();
        res.SaldoFinal.Should().Be(saldoFinal);
        res.Lineas.Count(l => l.Cargo.HasValue).Should().Be(4);

        var primera = res.Lineas[0];
        primera.Fecha.Should().Be(new DateOnly(2026, 10, 1));
        primera.Descripcion.Should().Be("SPEI RECIBIDO");
        primera.Abono.Should().Be(500m);
        primera.Saldo.Should().Be(242_656.17m);

        // Dos depósitos idénticos el mismo día son dos movimientos: el saldo los distingue.
        var repetidos = res.Lineas.Where(l => l.Abono == 2_000m).ToList();
        repetidos.Should().HaveCount(2);
        repetidos[0].LineaHash.Should().NotBe(repetidos[1].LineaHash);
    }

    [Fact]
    public void Bbva_auxiliar_con_un_renglon_fuera_de_layout_se_rechaza_completo()
    {
        string archivo = ExtractosFixtures.BbvaAuxiliar(1_000m, out _) + "05 oct 2026,0100000000,VENTAS,X,X,X,7,0000000000001.00,0,0000000000001.00\n";

        var act = () => new BbvaExtractoParser().Parse(archivo);

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EXTRACTO_FORMATO_NO_RECONOCIDO" && e.Message.Contains("perfil BBVA"));
    }

    [Fact]
    public void Archivo_bbva_con_perfil_santander_da_formato_no_reconocido()
    {
        string archivo = ExtractosFixtures.BbvaAuxiliar(1_000m, out _);

        var act = () => _factory.ObtenerParser("SANTANDER", archivo).Parse(archivo);

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EXTRACTO_FORMATO_NO_RECONOCIDO" && e.Message.Contains("Formato no reconocido para el perfil SANTANDER"));
    }

    [Fact]
    public void Santander_con_renglon_sin_fecha_valida_se_rechaza_completo()
    {
        string archivo = ExtractosFixtures.Santander(5) + "\r\n'65500000001,'99999999',05:05,'0981',X,+,1.00,1.00,1,X,,,,,,,,,,,X";

        var act = () => new SantanderExtractoParser().Parse(archivo);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "EXTRACTO_FORMATO_NO_RECONOCIDO");
    }

    [Fact]
    public void Perfil_mifel_existe_y_rechaza_todo_archivo_hasta_tener_el_layout_real()
    {
        var parser = _factory.ObtenerParser("MIFEL", ExtractosFixtures.Santander(3));

        parser.Should().BeOfType<MifelExtractoParser>();
        var act = () => parser.Parse(ExtractosFixtures.Santander(3));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EXTRACTO_FORMATO_NO_RECONOCIDO" && e.Message.Contains("perfil MIFEL"));
    }
}

/// <summary>Corre solo con <c>MILLET_EXTRACTOS_REALES</c> (carpeta con los auxiliares reales); en la CI se omite.</summary>
public sealed class ExtractosRealesLocalFactAttribute : FactAttribute
{
    public const string Variable = "MILLET_EXTRACTOS_REALES";
    public ExtractosRealesLocalFactAttribute()
    {
        string? dir = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            Skip = $"Prueba local con los auxiliares reales de octubre: define {Variable}.";
    }
}

public sealed class ExtractosRealesLocalTests(ITestOutputHelper output)
{
    private static string Leer(string nombre) =>
        File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable(ExtractosRealesLocalFactAttribute.Variable)!, nombre), Encoding.UTF8);

    [ExtractosRealesLocalFact]
    public void Santander_8138_real()
    {
        var res = new ExtractoParserFactory().ObtenerParser("SANTANDER", null).Parse(Leer("AUXILIAR BANCOS SANTANDER 8138.csv"));
        Reportar("SANTANDER 8138", res);

        res.TotalLineas.Should().Be(266);
        res.SaldoFinal.Should().Be(286_861.61m);
        res.Lineas.Min(l => l.Fecha).Should().Be(new DateOnly(2026, 10, 1));
        res.Lineas.Max(l => l.Fecha).Should().Be(new DateOnly(2026, 10, 30));
    }

    [ExtractosRealesLocalFact]
    public void Bbva_4468_real()
    {
        string archivo = Leer("AUXILIAR BBVA 4468.csv");
        var res = new ExtractoParserFactory().ObtenerParser("BBVA", archivo).Parse(archivo);
        Reportar("BBVA 4468", res);

        res.TotalLineas.Should().Be(2060);
        // Estado de cuenta PDF de octubre: saldo final 2,802,291.13, sin movimientos del 31-oct (sábado).
        res.Lineas.Last(l => l.Fecha <= new DateOnly(2026, 10, 30)).Saldo.Should().Be(2_802_291.13m);
        res.Lineas.Where(l => l.Fecha <= new DateOnly(2026, 10, 30)).Count(l => l.Abono.HasValue).Should().Be(1979);
        res.Lineas.Count(l => l.Cargo.HasValue).Should().Be(53);
        res.Lineas.Sum(l => l.Cargo ?? 0).Should().Be(44_465_851.86m);
    }

    private void Reportar(string cuenta, ResultadoParseExtracto res)
    {
        var lineas = res.Lineas;
        decimal corrido = lineas[0].Saldo!.Value + lineas.Skip(1).Sum(l => (l.Abono ?? 0) - (l.Cargo ?? 0));
        output.WriteLine($"{cuenta}: {res.TotalLineas} movimientos ({lineas.Count(l => l.Abono.HasValue)} abonos, " +
            $"{lineas.Count(l => l.Cargo.HasValue)} cargos), {lineas[0].Fecha:yyyy-MM-dd} a {lineas[^1].Fecha:yyyy-MM-dd}, " +
            $"saldo inicial {res.SaldoInicial:N2}, saldo final {res.SaldoFinal:N2}, saldo corrido {corrido:N2}, " +
            $"firmas distintas {lineas.Select(l => l.LineaHash).Distinct().Count()}");
        foreach (var dia in lineas.GroupBy(l => l.Fecha).TakeLast(2))
            output.WriteLine($"  {dia.Key:yyyy-MM-dd}: {dia.Count()} movimientos, saldo al cierre {dia.Last().Saldo:N2}");
    }
}
