using System.Text;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Cargas.Lotes;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.DatosMaestros.Application.Cargas;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Cargas;

namespace Millet.Compartido.UnitTests.Cargas;

/// <summary>M-047 + M-167 (86e3jvvmf): lote común de cargas con un tipo de prueba (balanza simple).</summary>
public sealed class LotesCargaServicioTests
{
    private const string Tipo = "prueba.balanza";
    private const string Subir = "prueba.subir";
    private const string Autorizar = "prueba.autorizar";

    /// <summary>Tipo de prueba: «cuenta» obligatoria, cargo y abono; cuenta 999 no existe; cargos deben igualar abonos.</summary>
    private sealed class ProcesadorPrueba : IProcesadorCarga
    {
        public string Tipo => LotesCargaServicioTests.Tipo;
        public string Nombre => "Balanza de prueba";
        public IReadOnlyList<ColumnaCarga> Columnas { get; } =
            [new("cuenta", "Cuenta contable", "Cta"), new("cargo", "Cargo"), new("abono", "Abono")];
        public IReadOnlyList<string> ColumnasObligatorias { get; } = ["cuenta", "cargo", "abono"];
        public bool RequiereFechaCorte => true;
        public string PermisoSubir => Subir;
        public string PermisoAutorizar => Autorizar;
        public int Aplicaciones { get; private set; }
        public string? FallarAlAplicar { get; set; }
        public LoteCargaContexto? UltimoContexto { get; private set; }

        public Task<IReadOnlyList<ParametroCarga>> ParametrosAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ParametroCarga>>([new("sucursalPorOmision", "Sucursal por omisión",
                "Para los renglones sin sucursal", true, "MID", [new("MID", "Mérida"), new("CUN", "Cancún")])]);

        public Task<ValidacionCarga> ValidarAsync(LoteCargaContexto lote, IReadOnlyList<FilaCarga> filas, CancellationToken ct)
        {
            var errores = filas.Where(f => f.Valores["cuenta"] == "999")
                .Select(f => new ErrorCarga(f.Fila, "cuenta", "AP_CUENTA_NO_EXISTE", "Cuenta no existe en el catálogo")).ToList();
            var cargos = filas.Sum(f => decimal.Parse(f.Valores["cargo"] is { Length: > 0 } c ? c : "0"));
            var abonos = filas.Sum(f => decimal.Parse(f.Valores["abono"] is { Length: > 0 } a ? a : "0"));
            if (cargos != abonos) errores.Add(new(null, "archivo", "AP_NO_CUADRA", $"No cuadra por {Math.Abs(cargos - abonos):N2}"));
            return Task.FromResult(new ValidacionCarga(errores.Count == 0, errores,
                new Dictionary<string, string> { ["cargos"] = cargos.ToString("N2"), ["abonos"] = abonos.ToString("N2") }));
        }

        public Task<AplicacionCarga> AplicarAsync(LoteCargaContexto lote, IReadOnlyList<FilaCarga> filas, CancellationToken ct)
        {
            UltimoContexto = lote;
            if (FallarAlAplicar is { } m) throw new ConflictException("AP_YA_HAY_APERTURA", m);
            Aplicaciones++;
            return Task.FromResult(new AplicacionCarga(Guid.NewGuid().ToString(), "SI-000001", false, new Dictionary<string, string>()));
        }
    }

    private sealed class Entorno
    {
        public FakeEmpresa Empresa { get; } = new();
        public FakeUser Usuario { get; } = new();
        public FakePermisos Permisos { get; } = new();
        public FakeClock Clock { get; } = new();
        public ProcesadorPrueba Procesador { get; } = new();
        public CompartidoDbContext Db { get; }
        public LotesCargaServicio Servicio { get; }
        public Entorno()
        {
            Db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Empresa);
            Permisos.Concedidos.UnionWith([Subir, Autorizar]);
            Servicio = new LotesCargaServicio(Db, [Procesador], Usuario, Permisos, Empresa, Clock);
        }
        public void OtraPersona() { Usuario.UserId = Guid.NewGuid(); Usuario.UserName = "Contralor"; }
    }

    private static byte[] Csv(params string[] lineas) => Encoding.UTF8.GetBytes(string.Join("\n", lineas));
    private static readonly DateOnly Corte = new(2026, 10, 31);
    private static readonly Dictionary<string, string> Mapeo = new() { ["No. cuenta"] = "cuenta", ["Debe"] = "cargo", ["Haber"] = "abono" };
    private static byte[] Cuadrada() => Csv("No. cuenta,Debe,Haber,Notas", "105.01,1000,0,x", "201.01,0,1000,y");

    [Fact]
    public async Task Encabezados_sugiere_mapeo_por_nombre_o_alias()
    {
        var e = new Entorno();
        var r = await e.Servicio.EncabezadosAsync(Tipo, Csv("Cta,Cargo,Otra", "1,1,0"), "b.csv", default);
        r.Encabezados.Should().Equal("Cta", "Cargo", "Otra");
        r.MapeoSugerido.Should().Contain("Cta", "cuenta").And.Contain("Cargo", "cargo").And.NotContainKey("Otra");
    }

    [Fact]
    public async Task Subir_con_mapeo_guarda_huella_filas_y_validacion_y_los_encabezados_no_mapeados_se_ignoran()
    {
        var e = new Entorno();
        var d = await e.Servicio.SubirAsync(Tipo, Cuadrada(), "balanza.csv", Corte, Mapeo, default);

        d.Lote.Estado.Should().Be("Subido");
        d.Lote.TotalFilas.Should().Be(2);
        d.Lote.PuedeAplicar.Should().BeTrue();
        d.Huella.Should().HaveLength(64);
        d.Filas.Should().HaveCount(2);
        d.Filas[0].Fila.Should().Be(2, "es el renglón real del archivo");
        d.Filas[0].Valores["cuenta"].Should().Be("105.01");
        d.Resumen["cargos"].Should().Be("1,000.00");
        d.Historial.Should().ContainSingle(h => h.Accion == "Subido");
        d.PuedeAutorizar.Should().BeFalse("quien subió no autoriza");
    }

    [Fact]
    public async Task Errores_por_fila_y_del_archivo_impiden_autorizar()
    {
        var e = new Entorno();
        var d = await e.Servicio.SubirAsync(Tipo, Csv("No. cuenta,Debe,Haber", "105.01,1000,0", "999,0,700"), "b.csv", Corte, Mapeo, default);

        d.Lote.PuedeAplicar.Should().BeFalse();
        d.Filas.Single(f => f.Fila == 3).Errores.Should().ContainSingle(x => x.Codigo == "AP_CUENTA_NO_EXISTE");
        d.ErroresArchivo.Should().ContainSingle(x => x.Mensaje == "No cuadra por 300.00");
        e.OtraPersona();
        var act = () => e.Servicio.AutorizarAsync(d.Lote.Id, default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("LOTE_CARGA_CON_ERRORES");
        e.Procesador.Aplicaciones.Should().Be(0);
    }

    [Fact]
    public async Task Quien_sube_no_puede_autorizar_y_otra_persona_autoriza_y_aplica_una_vez()
    {
        var e = new Entorno();
        var d = await e.Servicio.SubirAsync(Tipo, Cuadrada(), "b.csv", Corte, Mapeo, default);

        var propia = () => e.Servicio.AutorizarAsync(d.Lote.Id, default);
        (await propia.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().StartWith("Debe autorizar otra persona");

        e.OtraPersona();
        var a = await e.Servicio.AutorizarAsync(d.Lote.Id, default);
        a.Lote.Estado.Should().Be("Aplicado");
        a.Lote.FolioResultado.Should().Be("SI-000001");
        a.Lote.DecididoPor.Should().Be("Contralor");
        a.Historial.Select(h => h.Accion).Should().Equal("Subido", "Autorizado", "Aplicado");
        e.Procesador.Aplicaciones.Should().Be(1);

        var otraVez = () => e.Servicio.AutorizarAsync(d.Lote.Id, default);
        await otraVez.Should().ThrowAsync<ConflictException>();
        e.Procesador.Aplicaciones.Should().Be(1);
    }

    [Fact]
    public async Task El_mismo_archivo_no_abre_otro_lote_mientras_haya_uno_sin_rechazar()
    {
        var e = new Entorno();
        var a = await e.Servicio.SubirAsync(Tipo, Cuadrada(), "b.csv", Corte, Mapeo, default);
        var b = await e.Servicio.SubirAsync(Tipo, Cuadrada(), "copia.csv", Corte, Mapeo, default);
        b.Lote.Id.Should().Be(a.Lote.Id);

        e.OtraPersona();
        await e.Servicio.RechazarAsync(a.Lote.Id, "La balanza es de septiembre", default);
        var c = await e.Servicio.SubirAsync(Tipo, Cuadrada(), "b.csv", Corte, Mapeo, default);
        c.Lote.Id.Should().NotBe(a.Lote.Id);
        (await e.Db.LotesCarga.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Rechazar_exige_motivo_y_queda_en_la_bitacora()
    {
        var e = new Entorno();
        var d = await e.Servicio.SubirAsync(Tipo, Cuadrada(), "b.csv", Corte, Mapeo, default);
        e.OtraPersona();
        var sinMotivo = () => e.Servicio.RechazarAsync(d.Lote.Id, " ", default);
        (await sinMotivo.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("LOTE_CARGA_MOTIVO_REQUERIDO");

        var r = await e.Servicio.RechazarAsync(d.Lote.Id, "La balanza es de septiembre", default);
        r.Lote.Estado.Should().Be("Rechazado");
        r.MotivoRechazo.Should().Be("La balanza es de septiembre");
        r.Historial[^1].Detalle.Should().Be("La balanza es de septiembre");
    }

    [Fact]
    public async Task Si_el_modulo_detiene_la_aplicacion_el_lote_sigue_autorizado_con_el_motivo_y_se_reintenta()
    {
        var e = new Entorno();
        var d = await e.Servicio.SubirAsync(Tipo, Cuadrada(), "b.csv", Corte, Mapeo, default);
        e.OtraPersona();
        e.Procesador.FallarAlAplicar = "Ya hay apertura; use el ajuste único (M-300)";

        var a = await e.Servicio.AutorizarAsync(d.Lote.Id, default);
        a.Lote.Estado.Should().Be("Autorizado");
        a.Lote.UltimoError.Should().Be("Ya hay apertura; use el ajuste único (M-300)");
        a.PuedeReintentar.Should().BeTrue();

        e.Procesador.FallarAlAplicar = null;
        var r = await e.Servicio.ReintentarAsync(d.Lote.Id, default);
        r.Lote.Estado.Should().Be("Aplicado");
        r.Lote.UltimoError.Should().BeNull();
    }

    [Fact]
    public async Task Sin_fecha_de_corte_o_sin_mapear_una_columna_obligatoria_no_se_sube()
    {
        var e = new Entorno();
        var sinCorte = () => e.Servicio.SubirAsync(Tipo, Cuadrada(), "b.csv", null, Mapeo, default);
        (await sinCorte.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("LOTE_CARGA_FECHA_CORTE");
        var incompleto = () => e.Servicio.SubirAsync(Tipo, Cuadrada(), "b.csv", Corte,
            new Dictionary<string, string> { ["No. cuenta"] = "cuenta", ["Debe"] = "cargo" }, default);
        (await incompleto.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("abono");
    }

    [Fact]
    public async Task Sin_permiso_del_tipo_no_se_sube_y_el_tipo_aparece_sin_permisos()
    {
        var e = new Entorno();
        e.Permisos.Concedidos.Clear();
        // QA O1: el tipo se lista sin banderas, para que la pantalla diga «sin permiso» y no «no habilitado».
        var tipo = (await e.Servicio.TiposAsync(default)).Should().ContainSingle().Subject;
        tipo.PuedeSubir.Should().BeFalse();
        tipo.PuedeAutorizar.Should().BeFalse();
        var act = () => e.Servicio.SubirAsync(Tipo, Cuadrada(), "b.csv", Corte, Mapeo, default);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public void El_dominio_tambien_impide_que_quien_subio_autorice()
    {
        var subio = Guid.NewGuid();
        var lote = new LoteCarga(Guid.NewGuid(), Guid.NewGuid(), Tipo, "b.csv", new string('A', 64), Corte, "{}", "[]", 0, subio, "Ana", DateTimeOffset.UtcNow);
        lote.RegistrarValidacion("{}", true, 0);
        var act = () => lote.Autorizar(subio, "Ana", DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("LOTE_CARGA_MISMA_PERSONA");
    }

    [Fact]
    public async Task Parametros_del_tipo_se_capturan_validan_y_llegan_al_modulo_con_los_nombres()
    {
        var e = new Entorno();
        (await e.Servicio.TiposAsync(default)).Single().Parametros.Should().ContainSingle(p => p.Nombre == "sucursalPorOmision");

        var invalido = () => e.Servicio.SubirAsync(Tipo, Cuadrada(), "b.csv", Corte, Mapeo, default,
            new Dictionary<string, string?> { ["sucursalPorOmision"] = "XXX" });
        (await invalido.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("LOTE_CARGA_PARAMETRO_INVALIDO");
        var desconocido = () => e.Servicio.SubirAsync(Tipo, Cuadrada(), "b.csv", Corte, Mapeo, default,
            new Dictionary<string, string?> { ["otro"] = "1" });
        (await desconocido.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("LOTE_CARGA_PARAMETRO_DESCONOCIDO");

        var d = await e.Servicio.SubirAsync(Tipo, Cuadrada(), "b.csv", Corte, Mapeo, default,
            new Dictionary<string, string?> { ["sucursalPorOmision"] = "CUN" });
        d.Parametros["sucursalPorOmision"].Should().Be("CUN");
        e.OtraPersona();
        await e.Servicio.AutorizarAsync(d.Lote.Id, default);

        var ctx = e.Procesador.UltimoContexto!;
        ctx.Parametros["sucursalPorOmision"].Should().Be("CUN");
        ctx.SubidoPorNombre.Should().Be("Ana Prueba");
        ctx.AutorizadoPorNombre.Should().Be("Contralor");
        ctx.AutorizadoPorId.Should().Be(e.Usuario.UserId);
    }

    [Fact]
    public async Task Sin_capturar_el_parametro_se_usa_su_valor_por_omision()
    {
        var e = new Entorno();
        var d = await e.Servicio.SubirAsync(Tipo, Cuadrada(), "b.csv", Corte, Mapeo, default);
        d.Parametros["sucursalPorOmision"].Should().Be("MID");
    }
}
