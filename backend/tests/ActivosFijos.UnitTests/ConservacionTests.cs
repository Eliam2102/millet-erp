using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Workers;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.UnitTests;
public sealed class ConservacionTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly Guid Contador = Guid.NewGuid();
    private static Activo Activo(string numero = "SAP-00001") => new(Guid.NewGuid(), Empresa, numero,
        new DatosActivo("Equipo", Guid.NewGuid(), null, null, null, new(2026, 1, 1), null, 500000, "MXN", 1, new(Contador, Sucursal, "Nave A", Guid.NewGuid(), null), TipoImportacionActivo.No, null, false, "Factura"));
    private static DocumentoActivoDatos Documento(TipoDocumentoActivo tipo = TipoDocumentoActivo.Garantia) => new(tipo, Guid.NewGuid(), "G-1", "Proveedor", new(2026, 1, 1), new(2026, 1, 1), new(2026, 11, 19), "Evidencia");
    [Fact] public void Garantia_a_30_dias_genera_tipo_anticipado_y_el_dia_de_vencimiento_otro_tipo()
    {
        var a = Activo(); var d = new DocumentoActivo(a, Documento()); var hoy = new DateOnly(2026, 10, 20);
        Assert.Equal(TipoAvisoActivo.PorVencer, AvisoActivo.TipoPendiente(d.VigenteHasta!.Value, hoy, 30));
        Assert.Null(AvisoActivo.TipoPendiente(d.VigenteHasta.Value, hoy.AddDays(-1), 30));
        Assert.Equal(TipoAvisoActivo.Vencido, AvisoActivo.TipoPendiente(d.VigenteHasta.Value, d.VigenteHasta.Value, 30));
        var aviso = new AvisoActivo(a, d.Id, false, TipoAvisoActivo.PorVencer, d.VigenteHasta.Value, hoy);
        Assert.Equal(a.ResponsableId, aviso.ResponsableId); Assert.Equal(a.EmpresaId, aviso.EmpresaId);
        Assert.Equal(10, new AvisosActivosOptions { DiasPorEmpresa = { [Empresa] = 10 } }.Dias(Empresa));
    }
    [Theory] [InlineData(TipoDocumentoActivo.Seguro)] [InlineData(TipoDocumentoActivo.Garantia)] [InlineData(TipoDocumentoActivo.Arrendamiento)]
    public void Documento_con_vigencia_obligatoria_rechaza_fechas_ausentes_o_invertidas(TipoDocumentoActivo tipo)
    {
        Assert.Throws<BusinessRuleException>(() => new DocumentoActivo(Activo(), Documento(tipo) with { VigenteDesde = null, VigenteHasta = null }));
        Assert.Throws<BusinessRuleException>(() => new DocumentoActivo(Activo(), Documento(tipo) with { VigenteHasta = new(2025, 1, 1) }));
    }
    [Fact] public void Reemplazo_conserva_revision_y_archivo_anterior_y_rechaza_reemplazo_obsoleto()
    {
        var a = Activo(); var anterior = new DocumentoActivo(a, Documento()); var archivo = anterior.AdjuntoId;
        var nuevo = new DocumentoActivo(a, Documento() with { ReemplazaA = anterior.Id }, anterior);
        Assert.True(anterior.Reemplazado); Assert.Equal(archivo, anterior.AdjuntoId); Assert.Equal(1, anterior.NumeroVersion);
        Assert.False(nuevo.Reemplazado); Assert.Equal(2, nuevo.NumeroVersion); Assert.Equal(anterior.Id, nuevo.ReemplazaA);
        Assert.Throws<BusinessRuleException>(() => new DocumentoActivo(a, Documento() with { ReemplazaA = anterior.Id }, anterior));
        Assert.Throws<BusinessRuleException>(() => new DocumentoActivo(Activo("OTRO"), Documento() with { ReemplazaA = nuevo.Id }, nuevo));
    }
    [Fact] public void Mantenimiento_ligado_a_factura_no_cambia_valor_ni_crea_movimientos_contables()
    {
        var a = Activo(); var factura = Guid.NewGuid();
        var h = new MantenimientoActivo(a, new(new(2026, 10, 20), TipoMantenimiento.Preventivo, Guid.NewGuid(), "Cambio aceite", 2500, factura, new(2027, 1, 20)));
        Assert.Equal(factura, h.FacturaProveedorId); Assert.Equal(a.Id, h.ActivoId); Assert.Equal(500000m, a.ValorActual); Assert.Empty(a.Historial); Assert.Null(a.PolizaAltaId);
        Assert.Throws<BusinessRuleException>(() => new MantenimientoActivo(a, new(new(2026, 10, 20), TipoMantenimiento.Preventivo, Guid.NewGuid(), "Cambio aceite", -1, null, null)));
    }
    [Fact] public void Conteo_con_tres_esperados_y_sobrante_conserva_los_cuatro_resultados_y_foto_original()
    {
        var activos = new[] { Activo("1"), Activo("2"), Activo("3") };
        var c = new ConteoActivo(Empresa, Sucursal, null, Contador, activos);
        c.Capturar("1", ResultadoConteo.Encontrado, null, "Visto", Contador);
        c.Capturar("2", ResultadoConteo.NoEncontrado, null, "Se revisó nave", Contador);
        c.Capturar("3", ResultadoConteo.OtraUbicacion, "Nave B", "Foto de placa", Contador);
        c.Capturar("SAP-EXTRA", ResultadoConteo.Sobrante, "Nave B", "Equipo sin registro", Contador);
        c.Cerrar(Contador, DateTimeOffset.UtcNow);
        Assert.Equal(4, c.Partidas.Count); Assert.Equal(4, c.Partidas.Select(x => x.Resultado).Distinct().Count());
        Assert.Equal("Nave A", c.Partidas.Single(x => x.Numero == "3").UbicacionEsperada); Assert.Equal("Nave A", activos[2].Ubicacion);
        Assert.Throws<BusinessRuleException>(() => c.Capturar("1", ResultadoConteo.NoEncontrado, null, "Cambiar", Contador));
        Assert.Throws<BusinessRuleException>(() => c.Capturar(null, ResultadoConteo.Sobrante, null, "Agregar", Contador));
        Assert.Throws<BusinessRuleException>(() => c.Cerrar(Contador, DateTimeOffset.UtcNow));
    }
    [Theory] [InlineData(ResultadoConteo.NoEncontrado, "Baja")] [InlineData(ResultadoConteo.OtraUbicacion, "Traslado")] [InlineData(ResultadoConteo.Sobrante, "Alta o investigación")]
    public void Autorizar_exige_otra_persona_y_genera_solo_propuesta_con_evidencia(ResultadoConteo resultado, string propuesta)
    {
        var a = Activo(); var c = new ConteoActivo(Empresa, Sucursal, null, Contador, resultado == ResultadoConteo.Sobrante ? [] : [a]);
        c.Capturar(a.Numero, resultado, "Nave B", "Evidencia captura", Contador); var p = Assert.Single(c.Partidas);
        Assert.Throws<BusinessRuleException>(() => c.Autorizar(p.Id, Guid.NewGuid(), "Revisión", DateTimeOffset.UtcNow));
        c.Cerrar(Contador, DateTimeOffset.UtcNow);
        Assert.Throws<ForbiddenException>(() => c.Autorizar(p.Id, Contador, "Revisión", DateTimeOffset.UtcNow));
        var supervisor = Guid.NewGuid(); c.Autorizar(p.Id, supervisor, "Revisado contra placa", DateTimeOffset.UtcNow);
        Assert.Equal(propuesta, p.Propuesta); Assert.Equal(supervisor, p.AutorizadoPor); Assert.Equal("Revisado contra placa", p.EvidenciaAutorizacion);
        Assert.Equal(EstadoActivo.PorCapitalizar, a.Estado); Assert.Equal("Nave A", a.Ubicacion); Assert.Empty(a.Traslados);
        Assert.Throws<BusinessRuleException>(() => c.Autorizar(p.Id, supervisor, "Repetir", DateTimeOffset.UtcNow));
    }
    [Fact] public void Conteo_no_cierra_con_pendientes_y_rechaza_resultados_incompatibles()
    {
        var a = Activo(); var c = new ConteoActivo(Empresa, Sucursal, null, Contador, [a]);
        Assert.Throws<BusinessRuleException>(() => c.Cerrar(Contador, DateTimeOffset.UtcNow));
        Assert.Throws<BusinessRuleException>(() => c.Capturar(a.Numero, ResultadoConteo.OtraUbicacion, "Nave A", "Igual", Contador));
        Assert.Throws<BusinessRuleException>(() => c.Capturar(a.Numero, ResultadoConteo.Sobrante, null, "Esperado", Contador));
        Assert.Throws<BusinessRuleException>(() => c.Capturar("DESCONOCIDO", ResultadoConteo.Encontrado, null, "Falta", Contador));
        c.Capturar(null, ResultadoConteo.Sobrante, null, "Sin placa", Contador); c.Capturar(null, ResultadoConteo.Sobrante, null, "Otro sin placa", Contador);
        Assert.Equal(3, c.Partidas.Count);
    }
    [Fact] public void Impresion_conserva_numero_SAP_y_registra_fecha()
    { var a = Activo(); var ahora = DateTimeOffset.UtcNow; a.RegistrarImpresion(ahora); Assert.Equal("SAP-00001", a.Numero); Assert.Equal(ahora, a.UltimaImpresionEn); }
}
