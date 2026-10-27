using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Application;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.UnitTests;
public sealed class DepreciacionTests
{
    private static (Activo a, ClaseActivo clase) Montacargas(decimal acumulada = 0)
    {
        var clase = new ClaseActivo(Guid.NewGuid(), Guid.NewGuid(), "MAQ", "Maquinaria", 10);
        var a = new Activo(Guid.NewGuid(), clase.EmpresaId, "SAP-100", new("Montacargas", clase.Id, null, null, null, new(2026, 1, 1), new(2026, 1, 15), 500000, "MXN", 1,
            new(Guid.NewGuid(), Guid.NewGuid(), "Conkal", Guid.NewGuid(), null), TipoImportacionActivo.No, null, false, "Soporte"));
        a.Capitalizar(Guid.NewGuid()); a.Depreciar(acumulada); return (a, clase);
    }
    [Fact] public void CA1_Enero_cero_febrero_4166_67()
    {
        var (a, c) = Montacargas();
        Assert.Equal(0, CalculoDepreciacion.Calcular(a, c, new(2026, 1, 1)).Importe);
        var f = CalculoDepreciacion.Calcular(a, c, new(2026, 2, 1)); Assert.Equal(4166.67m, f.Importe); Assert.Equal(4166.67m, f.AcumuladaDespues);
    }
    [Fact] public void P7_Mismo_mes_deprecia_enero_y_no_diciembre()
    {
        var (a, c) = Montacargas();
        Assert.Equal(4166.67m, CalculoDepreciacion.Calcular(a, c, new(2026, 1, 1), mismoMes: true).Importe);
        Assert.Equal(0, CalculoDepreciacion.Calcular(a, c, new(2025, 12, 1), mismoMes: true).Importe);
    }
    [Fact] public void P7_Sin_tasa_se_omite_y_se_explica_en_previa()
    {
        var (a, c) = Montacargas();
        c.Editar(c.Nombre, null, null, null, null, null, true);
        var fila = CalculoDepreciacion.Calcular(a, c, new(2026, 2, 1));
        Assert.Equal(0, fila.Importe); Assert.Null(fila.Tasa); Assert.Contains("Tasa por confirmar", fila.Observacion);
        Assert.Throws<BusinessRuleException>(() => c.Editar(c.Nombre, null, null, null, null, null, true, true, true));
    }
    [Theory] [InlineData("151")] [InlineData("159")]
    public void P7_Terrenos_y_obras_no_deprecian_incluso_con_tasa(string clave)
    {
        var (a, c) = Montacargas();
        var semilla = CatalogoClasesMillet.Clases.Single(x => x.Clave == clave);
        c.Editar(semilla.Nombre, 10, null, null, null, null, true, semilla.Depreciable);
        Assert.Equal(0, CalculoDepreciacion.Calcular(a, c, new(2026, 2, 1), mismoMes: true).Importe);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void P7_Carga_inicial_siempre_empieza_despues_del_corte(bool mismoMes)
    {
        var (original, c) = Montacargas();
        var a = new Activo(Guid.NewGuid(), c.EmpresaId, "SAP-CORTE", new("Carga", c.Id, null, null, null,
            new(2026, 1, 1), new(2026, 1, 15), 500000, "MXN", 1, original.Custodia(), TipoImportacionActivo.No, null, false, "SAP"));
        a.CargarLibro(new(2026, 1, 31), 10000);
        Assert.Equal(new DateOnly(2026, 2, 1), CalculoDepreciacion.PrimerMes(a, mismoMes));
        Assert.Equal(0, CalculoDepreciacion.Calcular(a, c, new(2026, 1, 1), mismoMes: mismoMes).Importe);
        Assert.Equal(4166.67m, CalculoDepreciacion.Calcular(a, c, new(2026, 2, 1), mismoMes: mismoMes).Importe);
    }
    [Fact] public void CA4_Residual_cero_permanece_en_libro()
    {
        var (a, c) = Montacargas(499900); a.Depreciar(CalculoDepreciacion.Calcular(a, c, new(2026, 2, 1)).Importe);
        Assert.Equal(0, a.ValorEnLibros); Assert.Equal(EstadoActivo.Activo, a.Estado); Assert.Equal(0, CalculoDepreciacion.Calcular(a, c, new(2026, 3, 1)).Importe);
    }
    [Fact] public void CA5_Mejora_marzo_inicia_abril()
    {
        var (a, c) = Montacargas(); var doc = Guid.NewGuid();
        a.Ajustar(doc, doc, new(2026, 3, 5), 60000, false, "Mejora", Guid.NewGuid());
        Assert.Equal(4166.67m, CalculoDepreciacion.Calcular(a, c, new(2026, 3, 1)).Importe);
        Assert.Equal(4666.67m, CalculoDepreciacion.Calcular(a, c, new(2026, 4, 1)).Importe);
    }
    [Fact] public void CA6_Custodia_al_cierre_no_destino_actual()
    {
        var (a, c) = Montacargas(); var original = a.Custodia(); var cancun = new CustodiaActivo(Guid.NewGuid(), Guid.NewGuid(), "Cancún", Guid.NewGuid(), null);
        var traslado = Guid.NewGuid(); a.Trasladar(traslado, cancun, original.ResponsableId, new(2026,3,20,12,0,0,TimeSpan.Zero)); a.AceptarTraslado(traslado, cancun.ResponsableId, new(2026,3,20,13,0,0,TimeSpan.Zero));
        Assert.Equal(original.SucursalId, CalculoDepreciacion.Calcular(a, c, new(2026,2,1)).SucursalId);
        var marzo = CalculoDepreciacion.Calcular(a, c, new(2026,3,1)); Assert.Equal(cancun.SucursalId, marzo.SucursalId); Assert.Equal(cancun.CentroCostoId, marzo.CentroCostoId);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void CA9_Baja_requiere_evidencia_y_otro_autorizador(bool mismo)
    {
        var actor = Guid.NewGuid(); var op = new OperacionLibro { Tipo = "Baja", Motivo = "Siniestro", SolicitadoPor = "Área DEMO", RegistradoPor = actor, Evidencia = mismo ? "adjunto" : null };
        Assert.Throws<BusinessRuleException>(() => op.Validar(mismo ? actor : Guid.NewGuid(), false));
        op.Evidencia = "adjunto"; op.Validar(Guid.NewGuid(), false);
        var (a, _) = Montacargas(); a.DarDeBaja(new(2026,6,1)); Assert.Equal(EstadoActivo.DadoDeBaja, a.Estado); a.Reintegrar(); Assert.Equal(EstadoActivo.Activo, a.Estado);
    }
    [Fact] public void CA13_Revalorizacion_apagada_y_autorizador_distinto()
    {
        var op = new OperacionLibro { Tipo = "Revalorizacion", Motivo = "Ajuste", RegistradoPor = Guid.NewGuid() };
        Assert.Throws<BusinessRuleException>(() => op.Validar(Guid.NewGuid(), false)); op.Validar(Guid.NewGuid(), true);
    }
    [Fact] public async Task CA3_Periodo_cerrado_se_rechaza_antes_de_contabilizar()
    {
        var libro = new LibroServicio(null!, null!, null!, null!, null!, new PeriodoCerrado(), null!);
        await Assert.ThrowsAsync<BusinessRuleException>(() => libro.PeriodoAsync(new(2026,2,1), default));
    }
    [Fact] public void Adjunto_no_admite_texto_arbitrario_ni_tipo_activo()
    {
        Assert.Throws<BusinessRuleException>(() => EvidenciaBaja.Validar("adjunto.pdf"));
        Assert.Throws<BusinessRuleException>(() => EvidenciaBaja.Validar("data:text/html;base64,PGgxPnBydWViYTwvaDE+"));
        EvidenciaBaja.Validar("data:application/pdf;base64,JVBERi0xLjc=");
    }
    [Fact] public void SAP_acumulada_no_supera_valor_y_conserva_numero()
    {
        var (activo, c) = Montacargas();
        var a = new Activo(Guid.NewGuid(), c.EmpresaId, "SAP-00999", new("Carga", c.Id, null, null, null, new(2025,1,1), new(2025,1,15), 500000, "MXN", 1, activo.Custodia(), TipoImportacionActivo.No, null, false, "SAP"));
        Assert.Throws<BusinessRuleException>(() => a.CargarLibro(new(2026,10,31),500001));
        a.CargarLibro(new(2026,10,31),100000); Assert.Equal("SAP-00999",a.Numero); Assert.Equal(400000,a.ValorEnLibros); Assert.Null(a.PolizaAltaId);
        Assert.Equal(0,CalculoDepreciacion.Calcular(a,c,new(2026,10,1)).Importe); Assert.Equal(4166.67m,CalculoDepreciacion.Calcular(a,c,new(2026,11,1)).Importe);
    }
    private sealed class PeriodoCerrado : IPeriodoContableConsultaPort
    {
        public Task<EstadoPeriodoContable> ConsultarPorFechaAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(new EstadoPeriodoContable(fecha.Year,fecha.Month,EstadoPeriodo.Cerrado,true));
        public Task<EstadoPeriodoContable> ConsultarAsync(int anio,int numero,CancellationToken ct) => ConsultarPorFechaAsync(new(anio,numero,1),ct);
    }
}
