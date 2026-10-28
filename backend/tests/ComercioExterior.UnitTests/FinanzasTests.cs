using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ComercioExterior.UnitTests;
public sealed class FinanzasTests
{
    private static readonly decimal[] Tercios = [33.34m, 33.33m, 33.33m];
    private static Expediente Nuevo(TipoExpediente tipo = TipoExpediente.Importacion) => new(Guid.NewGuid(), Guid.NewGuid(), "CE-TEST", tipo,
        Guid.NewGuid(), "Prueba financiera", tipo == TipoExpediente.Importacion ? Guid.NewGuid() : null, Guid.NewGuid(), "MXN", Guid.NewGuid(), tipo == TipoExpediente.Exportacion ? ["Cliente"] : []);
    private static BasePartidaCosto[] Partidas() => [new(Guid.Parse("00000000-0000-0000-0000-000000000001"), null, "A", 200000, 2000, 1000, Existencia: 1000, ExistenciaTotal: 1000), new(Guid.Parse("00000000-0000-0000-0000-000000000002"), null, "B", 300000, 6000, 1000, Existencia: 1000, ExistenciaTotal: 1000)];
    private static void Gasto(Expediente e, ConceptoGasto concepto, decimal importe, Guid? id = null, IReadOnlyDictionary<Guid, decimal>? directos = null) =>
        e.GuardarGasto(id ?? Guid.NewGuid(), new(concepto.Id, Guid.NewGuid(), "FAC-1", "MXN", importe, new(2026, 10, 1), ImportesDirectos: directos), concepto, 1, "MXN");
    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, false, "CE_ANTICIPO_PECA_SOLO_IMPUESTOS")]
    [InlineData(false, true, "CE_GASTO_AGENTE_INVALIDO")]
    public void P14_impuestos_y_gastos_del_agente(bool peca, bool otroProveedor, string? error)
    {
        var e = Nuevo(); var agente = Guid.NewGuid();
        var concepto = new ConceptoGasto(e.EmpresaId, "HON", "Honorarios");
        e.GuardarGasto(Guid.NewGuid(), new(concepto.Id, otroProveedor ? Guid.NewGuid() : agente, "HON-1", "MXN", 7500, new(2026, 10, 1)), concepto, 1, "MXN");
        var a = e.RegistrarAnticipo(Guid.NewGuid(), agente, "PED", new(150000, 0, 0), new(2026, 10, 1), null, peca);
        a.Comprobar(new(142500, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 2));
        if (error != null)
        {
            Assert.Equal(error, Assert.Throws<BusinessRuleException>(() => a.AplicarGastos(e.Gastos)).Code);
            Assert.Null(e.Gastos.Single().AnticipoAgenteId);
            return;
        }
        a.AplicarGastos(e.Gastos);
        Assert.Equal(150000, a.Aplicado); Assert.Equal(0, a.Saldo);
        var b = e.RegistrarAnticipo(Guid.NewGuid(), agente, "PED-2", new(150000, 0, 0), new(2026, 10, 1), null);
        b.Comprobar(new(142500, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 2));
        Assert.Equal("CE_GASTO_YA_APLICADO", Assert.Throws<BusinessRuleException>(() => b.AplicarGastos(e.Gastos)).Code);
        Assert.Equal(142500, b.Aplicado);
        Assert.Equal("CE_GASTO_YA_APLICADO", Assert.Throws<BusinessRuleException>(() => e.GuardarGasto(e.Gastos.Single().Id,
            new(concepto.Id, agente, "EDIT", "MXN", 1, new(2026, 10, 1)), concepto, 1, "MXN")).Code);
    }
    [Fact]
    public void P14_saldo_en_cuenta_del_agente_libera_cierre()
    {
        var e = Nuevo();
        var a = e.RegistrarAnticipo(Guid.NewGuid(), Guid.NewGuid(), "PED", new(150000, 0, 0), new(2026, 10, 1), null);
        a.Comprobar(new(142500, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 2));
        a.Resolver("saldo-al-agente", 7500, new(2026, 10, 3), "Cuenta agente", Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(0, a.Saldo);
        Assert.DoesNotContain("Saldo a favor abierto con el agente", e.MotivosFinancieros());
    }
    [Theory]
    [InlineData(160000, 7500, true)]
    [InlineData(142500, 7501, true)]
    [InlineData(142500, 7500, false)]
    public void P14_rechaza_traslado_invalido(decimal aplicado, decimal importe, bool evidencia)
    {
        var e = Nuevo();
        var a = e.RegistrarAnticipo(Guid.NewGuid(), Guid.NewGuid(), "PED", new(150000, 0, 0), new(2026, 10, 1), null);
        a.Comprobar(new(aplicado, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 2));
        Assert.Throws<BusinessRuleException>(() => a.Resolver("saldo-al-agente", importe, new(2026, 10, 3), "Cuenta", evidencia ? Guid.NewGuid() : Guid.Empty, Guid.NewGuid()));
    }
    [Fact]
    public void CA1_valor_distribuye_veinte_y_treinta_mil()
    {
        var e = Nuevo(); var f = new ConceptoGasto(e.EmpresaId, "FLETE", "Flete"); var a = new ConceptoGasto(e.EmpresaId, "HON", "Agente");
        Gasto(e, f, 40000); Gasto(e, a, 10000);
        var d = e.PrepararDistribucion([f, a], Partidas(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal(20000, d.Partidas[0].ImporteMxn); Assert.Equal(30000, d.Partidas[1].ImporteMxn);
        Assert.Equal(20, d.Partidas[0].CostoUnitarioDespues); Assert.Equal(30, d.Partidas[1].CostoUnitarioDespues);
    }
    [Fact]
    public void CA2_peso_para_flete_y_valor_para_agente()
    {
        var e = Nuevo(); var f = new ConceptoGasto(e.EmpresaId, "FLETE", "Flete", BaseDistribucion.Peso); var a = new ConceptoGasto(e.EmpresaId, "HON", "Agente");
        Gasto(e, f, 40000); Gasto(e, a, 10000);
        var d = e.PrepararDistribucion([f, a], Partidas(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal(new decimal[] { 10000, 30000 }, d.Renglones.Where(r => r.ConceptoId == f.Id).Select(r => r.ImporteMxn));
        Assert.Equal(new decimal[] { 4000, 6000 }, d.Renglones.Where(r => r.ConceptoId == a.Id).Select(r => r.ImporteMxn));
    }
    [Fact]
    public void CA3_centavos_suman_exactamente_y_empate_es_estable() => Assert.Equal(Tercios, ProrrateoGastos.Repartir(100, [1, 1, 1]));
    [Theory]
    [InlineData(400, 0, 10000)]
    [InlineData(1000, 10000, 0)]
    [InlineData(0, 0, 10000)]
    public void CA4_gasto_tardio_completo_a_gastos_de_importacion(decimal existencia, decimal inventario, decimal gasto)
    {
        var e = Nuevo(); var c = new ConceptoGasto(e.EmpresaId, "FLETE", "Flete"); Gasto(e, c, 10000);
        var d = e.PrepararDistribucion([c], [Partidas()[0] with { Existencia = existencia, ExistenciaTotal = existencia, CostoPromedioMxn = 100 }], Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal(inventario, d.Partidas[0].InventarioMxn); Assert.Equal(gasto, d.Partidas[0].GastoImportacionMxn);
        Assert.Equal(100 + (existencia > 0 ? inventario / existencia : 0), d.Partidas[0].CostoUnitarioDespues);
        Assert.Equal(existencia < 1000, d.Partidas[0].TieneSalidas);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Partidas_json_historico_conserva_importes_y_publica_ambos_nombres(bool web)
    {
        var opciones = web ? System.Text.Json.JsonSerializerOptions.Web : System.Text.Json.JsonSerializerOptions.Default;
        var anterior = web ? "costoVentasMxn" : "CostoVentasMxn";
        var actual = web ? "gastoImportacionMxn" : "GastoImportacionMxn";
        var historico = System.Text.Json.JsonSerializer.Deserialize<CostoPartidaDistribuido>(
            "{\"" + anterior + "\":6000}", opciones)!;
        Assert.Equal(6000, historico.GastoImportacionMxn);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(historico, opciones);
        Assert.Equal(6000, json.GetProperty(anterior).GetDecimal());
        Assert.Equal(6000, json.GetProperty(actual).GetDecimal());
        Assert.Equal(historico, System.Text.Json.JsonSerializer.Deserialize<CostoPartidaDistribuido>(json, opciones));
    }
    [Fact]
    public void Gasto_tardio_sin_diferencia_conserva_el_indicador_de_salidas()
    {
        var e = Nuevo(); var c = new ConceptoGasto(e.EmpresaId, "FLETE", "Flete"); Gasto(e, c, 10000);
        var partidas = new[] { Partidas()[0] with { Existencia = 400, ExistenciaTotal = 400 } };
        e.ConfirmarDistribucion(e.PrepararDistribucion([c], partidas, Guid.NewGuid(), DateTimeOffset.UtcNow));
        var d = e.PrepararDistribucion([c], partidas, Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.True(d.Partidas[0].TieneSalidas);
        Assert.Equal(0, d.Partidas[0].InventarioMxn); Assert.Equal(0, d.Partidas[0].GastoImportacionMxn);
    }
    [Fact]
    public void CA6_nueva_version_preserva_anterior_y_solo_publica_diferencia()
    {
        var e = Nuevo(); var c = new ConceptoGasto(e.EmpresaId, "FLETE", "Flete"); var id = Guid.NewGuid(); Gasto(e, c, 50000, id);
        var uno = e.PrepararDistribucion([c], Partidas(), Guid.NewGuid(), DateTimeOffset.UtcNow); e.ConfirmarDistribucion(uno);
        Gasto(e, c, 60000, id);
        var dos = e.PrepararDistribucion([c], Partidas(), Guid.NewGuid(), DateTimeOffset.UtcNow); e.ConfirmarDistribucion(dos);
        Assert.Equal(2, e.Distribuciones.Count); Assert.Equal(20000, uno.Partidas[0].ImporteMxn);
        Assert.Equal(4000, dos.Partidas[0].DiferenciaMxn); Assert.Equal(6000, dos.Partidas[1].DiferenciaMxn);
    }
    [Fact]
    public void CA7_iva_no_es_costo()
    {
        var e = Nuevo(); var c = new ConceptoGasto(e.EmpresaId, "IVA", "IVA importación"); Gasto(e, c, 16000);
        var d = e.PrepararDistribucion([c], Partidas(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Empty(d.Renglones); Assert.All(d.Partidas, p => Assert.Equal(0, p.ImporteMxn));
    }
    [Fact]
    public void CA8_y_CA9_saldo_abierto_bloquea_cierre_y_compensacion_lo_resuelve()
    {
        var e = Nuevo(); var agente = Guid.NewGuid();
        var a = e.RegistrarAnticipo(Guid.NewGuid(), agente, "PED-1", new(140000, 9000, 1000), new(2026, 10, 1), "PAGO-1");
        a.Comprobar(new(135000, 6500, 1000), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 2));
        Assert.Equal(7500, a.Saldo); Assert.Contains("Saldo a favor abierto con el agente", e.MotivosFinancieros()); Assert.Throws<BusinessRuleException>(e.Cerrar);
        var destino = Nuevo(); destino.EmpresaId = e.EmpresaId;
        var b = destino.RegistrarAnticipo(Guid.NewGuid(), agente, "PED-2", new(10000, 0, 0), new(2026, 10, 2), null);
        a.Resolver("compensar", 7500, new(2026, 10, 3), "COMP-1", Guid.NewGuid(), Guid.NewGuid(), b);
        Assert.Equal(0, a.Saldo); Assert.Equal(b.Id, a.Resoluciones.Single().AnticipoDestinoId); Assert.Equal(7500, b.CompensacionRecibida);
        e.Cerrar(); Assert.True(e.FinancieroCerrado);
    }
    [Fact]
    public void CA10_exportacion_por_peso_no_genera_inventario()
    {
        var e = Nuevo(TipoExpediente.Exportacion); var c = new ConceptoGasto(e.EmpresaId, "FLETE", "Flete"); Gasto(e, c, 30000);
        var clientes = Enumerable.Range(1, 3).Select(i => new BasePartidaCosto(Guid.NewGuid(), null, "Cliente " + i, 100, i * 100, 10, Guid.NewGuid())).ToArray();
        var d = e.PrepararDistribucion([c], clientes, Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal(new decimal[] { 5000, 10000, 15000 }, d.Partidas.Select(p => p.ImporteMxn));
        Assert.All(d.Partidas, p => { Assert.Equal(0, p.InventarioMxn); Assert.Equal(0, p.GastoImportacionMxn); Assert.NotNull(p.ClienteId); Assert.False(p.TieneSalidas); });
    }
    [Fact]
    public void Directo_exige_suma_exacta_y_no_admite_partidas_ajenas()
    {
        var e = Nuevo(); var c = new ConceptoGasto(e.EmpresaId, "IGI", "Arancel", BaseDistribucion.Directo);
        Gasto(e, c, 100, directos: new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 100 });
        Assert.Throws<BusinessRuleException>(() => e.PrepararDistribucion([c], Partidas(), Guid.NewGuid(), DateTimeOffset.UtcNow));
    }
    [Fact]
    public void Sin_saldo_cierra_comprobado_y_no_inventa_devolucion()
    {
        var e = Nuevo(); var a = e.RegistrarAnticipo(Guid.NewGuid(), Guid.NewGuid(), "PED", new(100, 0, 0), new(2026, 10, 1), null);
        a.Comprobar(new(100, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 2));
        Assert.Equal("Comprobado", a.Estado); Assert.Empty(a.Resoluciones);
        Assert.Throws<BusinessRuleException>(() => a.Resolver("devolucion", 1, new(2026, 10, 2), "Ref", Guid.NewGuid(), Guid.NewGuid()));
    }
    [Fact]
    public void Deuda_con_agente_solo_se_resuelve_con_pago_y_evidencia()
    {
        var e = Nuevo(); var a = e.RegistrarAnticipo(Guid.NewGuid(), Guid.NewGuid(), "PED", new(100, 0, 0), new(2026, 10, 1), null);
        a.Comprobar(new(120, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 2));
        Assert.Equal("Por pagar al agente", a.Estado);
        a.Resolver("pago-complementario", 20, new(2026, 10, 2), "Pago", Guid.NewGuid(), Guid.NewGuid()); Assert.Equal(0, a.Saldo);
    }
}
