using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.ConciliacionSat;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.ConciliacionSat;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.ConciliacionSat;

/// <summary>
/// FAC-10 · conciliación de CFDI emitidos contra el SAT con el archivo de
/// metadatos (formato de la descarga masiva y del portal). Datos ficticios.
/// </summary>
public sealed class ConciliacionSatTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Lunes = new(2026, 11, 2);
    private static readonly DateOnly Domingo = new(2026, 11, 8);
    private const string RfcEmpresa = "AAA010101AAA";
    private const string RfcCliente = "BBB010101BBB";

    private const string Encabezado =
        "Uuid~RfcEmisor~NombreEmisor~RfcReceptor~NombreReceptor~RfcPac~FechaEmision~FechaCertificacionSat~Monto~EfectoComprobante~Estatus~FechaCancelacion";

    private static string Renglon(string uuid, decimal monto, string estatus = "1", string efecto = "I",
        string receptor = RfcCliente, string emisor = RfcEmpresa, string fechaCancelacion = "") =>
        $"{uuid}~{emisor}~EMPRESA PRUEBA~{receptor}~CLIENTE PRUEBA~SPR190613I52~2026-11-03 10:15:00~2026-11-03 10:15:30~" +
        $"{monto.ToString(System.Globalization.CultureInfo.InvariantCulture)}~{efecto}~{estatus}~{fechaCancelacion}";

    private static byte[] Archivo(params string[] renglones) =>
        Encoding.UTF8.GetBytes(Encabezado + "\r\n" + string.Join("\r\n", renglones) + "\r\n");

    // ---------- Lector ----------

    [Fact]
    public void Lector_ubica_columnas_por_nombre_y_lee_estatus_y_efecto()
    {
        var u = Guid.NewGuid().ToString();
        var archivo = Encoding.UTF8.GetBytes(
            "﻿Estatus~Monto~Uuid~RfcEmisor~RfcReceptor~FechaEmision~EfectoComprobante~FechaCancelacion\n" +
            $"0~1,160.50~{u.ToLowerInvariant()}~{RfcEmpresa.ToLowerInvariant()}~{RfcCliente}~2026-11-03 10:15:00~E~2026-11-05 09:00:00\n");

        var r = LectorMetadataSat.Leer(archivo).Single();

        r.Uuid.Should().Be(u.ToUpperInvariant());
        (r.RfcEmisor, r.Monto, r.Efecto, r.Cancelado).Should().Be((RfcEmpresa, 1160.50m, "E", true));
        r.FechaCancelacion.Should().Be(new DateTime(2026, 11, 5, 9, 0, 0));
    }

    [Fact]
    public void Lector_abre_el_zip_del_paquete_de_la_descarga_masiva()
    {
        using var zip = new MemoryStream();
        using (var a = new ZipArchive(zip, ZipArchiveMode.Create, true))
        using (var s = a.CreateEntry("PAQUETE_01.txt").Open())
            s.Write(Archivo(Renglon(Guid.NewGuid().ToString(), 100m)));

        LectorMetadataSat.Leer(zip.ToArray()).Should().ContainSingle();
    }

    [Fact]
    public void Lector_rechaza_columnas_faltantes_y_renglones_malos_con_el_numero_de_renglon()
    {
        var sinMonto = () => LectorMetadataSat.Leer(Encoding.UTF8.GetBytes("Uuid~RfcEmisor~RfcReceptor~FechaEmision~EfectoComprobante~Estatus\n"));
        sinMonto.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONCILIACION_SAT_COLUMNAS");

        var malo = () => LectorMetadataSat.Leer(Archivo(Renglon(Guid.NewGuid().ToString(), 1m), Renglon("no-es-uuid", 1m)));
        malo.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CONCILIACION_SAT_RENGLONES" && e.Message.Contains("renglón 3"));

        var otroFormato = () => LectorMetadataSat.Leer(Encoding.UTF8.GetBytes("a,b,c\n1,2,3\n"));
        otroFormato.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONCILIACION_SAT_FORMATO");
    }

    // ---------- Comparador ----------

    private static CfdiSatMetadata Sat(string uuid, decimal monto, bool cancelado = false, string efecto = "I", string receptor = RfcCliente) =>
        new(uuid, RfcEmpresa, receptor, null, new DateTime(2026, 11, 3), monto, efecto, cancelado, null);

    private static CfdiErpConciliable Erp(string uuid, decimal total, EstadoTimbrado estado = EstadoTimbrado.Timbrado,
        TipoComprobante tipo = TipoComprobante.Ingreso, bool enPeriodo = true) =>
        new(Guid.NewGuid(), uuid, "FV-1", RfcCliente, tipo, total, estado, enPeriodo);

    [Fact]
    public void Comparador_marca_estatus_total_efecto_y_receptor_distintos_por_separado()
    {
        var u = "AAAAAAAA-0000-0000-0000-000000000001";
        var d = ComparadorConciliacionSat.Comparar(
            [Sat(u, 1160m, cancelado: true, efecto: "E", receptor: "CCC010101CCC")],
            [Erp(u.ToLowerInvariant(), 1100m)]);

        d.Select(x => x.Tipo).Should().BeEquivalentTo([
            TipoDiferenciaSat.EstatusDistinto, TipoDiferenciaSat.TotalDistinto, TipoDiferenciaSat.TipoDistinto, TipoDiferenciaSat.ReceptorDistinto]);
        d.Single(x => x.Tipo == TipoDiferenciaSat.EstatusDistinto).Should().Match<DiferenciaDetectada>(x =>
            x.ValorSat == "Cancelado" && x.ValorErp == "Vigente" && x.Folio == "FV-1");
        d.Single(x => x.Tipo == TipoDiferenciaSat.TotalDistinto).Should().Match<DiferenciaDetectada>(x =>
            x.ValorSat == "1,160.00" && x.ValorErp == "1,100.00");
    }

    [Fact]
    public void Comparador_faltante_en_sat_solo_cuenta_lo_timbrado_en_el_periodo()
    {
        var d = ComparadorConciliacionSat.Comparar([], [
            Erp("AAAAAAAA-0000-0000-0000-000000000002", 10m),
            Erp("AAAAAAAA-0000-0000-0000-000000000003", 10m, enPeriodo: false)]);

        d.Should().ContainSingle().Which.Should().Match<DiferenciaDetectada>(x =>
            x.Tipo == TipoDiferenciaSat.FaltanteEnSat && x.Uuid == "AAAAAAAA-0000-0000-0000-000000000002");
    }

    [Theory]
    [InlineData(EstadoTimbrado.CancelacionPendiente, false, false)]
    [InlineData(EstadoTimbrado.CancelacionPendiente, true, true)]
    [InlineData(EstadoTimbrado.Cancelado, true, false)]
    [InlineData(EstadoTimbrado.Cancelado, false, true)]
    public void Comparador_cancelacion_pendiente_en_el_erp_no_es_diferencia_mientras_el_sat_la_tenga_vigente(
        EstadoTimbrado erp, bool satCancelado, bool hayDiferencia)
    {
        var u = "AAAAAAAA-0000-0000-0000-000000000004";
        ComparadorConciliacionSat.Comparar([Sat(u, 10m, satCancelado)], [Erp(u, 10m, erp)])
            .Any(x => x.Tipo == TipoDiferenciaSat.EstatusDistinto).Should().Be(hayDiferencia);
    }

    // ---------- Corrida completa ----------

    [Fact]
    public async Task FAC_10_b_semana_sin_diferencias_queda_registrada()
    {
        using var db = NewDb();
        var a = await TimbradoAsync(db, Lunes.AddDays(1));
        var b = await TimbradoAsync(db, Lunes.AddDays(3));

        var r = await Handlers(db).Handle(new ConciliarCfdiSatCommand("emitidos.txt",
            Archivo(Renglon(a.Uuid!, a.Total), Renglon(b.Uuid!, b.Total)), Lunes, Domingo), default);

        r.SinDiferencias.Should().BeTrue();
        (r.CfdiSat, r.CfdiErp, r.Coincidentes, r.ConDiferencias, r.DiferenciasPendientes).Should().Be((2, 2, 2, 0, 0));
        (await db.ConciliacionesSat.SingleAsync()).Should().Match<Domain.ConciliacionSat.ConciliacionSat>(c =>
            c.EjecutadaPor == Usuario && c.EjecutadaEn == Ahora && c.Huella.Length == 64 && c.NombreArchivo == "emitidos.txt");
    }

    [Fact]
    public async Task FAC_10_a_y_c_cancelada_en_el_sat_y_faltante_en_el_erp_aparecen_en_la_corrida()
    {
        using var db = NewDb();
        var vigente = await TimbradoAsync(db, Lunes.AddDays(1));
        var sinSat = await TimbradoAsync(db, Lunes.AddDays(2));
        var ajeno = Guid.NewGuid().ToString().ToUpperInvariant();

        var r = await Handlers(db).Handle(new ConciliarCfdiSatCommand("emitidos.txt", Archivo(
            Renglon(vigente.Uuid!, vigente.Total, estatus: "0", fechaCancelacion: "2026-11-05 12:00:00"),
            Renglon(ajeno, 5_000m)), Lunes, Domingo), default);

        r.SinDiferencias.Should().BeFalse();
        r.PorTipo.Select(p => (p.Tipo, p.Total)).Should().BeEquivalentTo([
            (TipoDiferenciaSat.FaltanteEnErp, 1), (TipoDiferenciaSat.FaltanteEnSat, 1), (TipoDiferenciaSat.EstatusDistinto, 1)]);
        (r.CfdiSat, r.CfdiErp, r.Coincidentes, r.ConDiferencias).Should().Be((2, 2, 0, 3));

        var diferencias = await Handlers(db).Handle(new DiferenciasConciliacionSatQuery(r.Id, null, null), default);
        diferencias.Single(d => d.Tipo == TipoDiferenciaSat.EstatusDistinto).ComprobanteId.Should().Be(vigente.Id);
        diferencias.Single(d => d.Tipo == TipoDiferenciaSat.FaltanteEnErp).Uuid.Should().Be(ajeno);
        diferencias.Single(d => d.Tipo == TipoDiferenciaSat.FaltanteEnSat).Folio.Should().Be(sinSat.Folio);
    }

    [Fact]
    public async Task Un_cfdi_timbrado_fuera_del_periodo_se_encuentra_por_uuid_y_no_sale_como_faltante()
    {
        using var db = NewDb();
        var anterior = await TimbradoAsync(db, Lunes.AddDays(-10));

        var r = await Handlers(db).Handle(new ConciliarCfdiSatCommand("emitidos.txt",
            Archivo(Renglon(anterior.Uuid!.ToLowerInvariant(), anterior.Total)), Lunes, Domingo), default);

        r.SinDiferencias.Should().BeTrue();
        r.CfdiErp.Should().Be(0);
    }

    [Fact]
    public async Task El_archivo_de_recibidos_se_rechaza_porque_el_emisor_no_es_la_empresa()
    {
        using var db = NewDb();
        await TimbradoAsync(db, Lunes.AddDays(1));

        var act = () => Handlers(db).Handle(new ConciliarCfdiSatCommand("recibidos.txt",
            Archivo(Renglon(Guid.NewGuid().ToString(), 10m, emisor: "PRO010101PRO", receptor: RfcEmpresa)), Lunes, Domingo), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("CONCILIACION_SAT_OTRO_EMISOR");
        (await db.ConciliacionesSat.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task El_emisor_se_valida_contra_el_rfc_de_la_empresa_aunque_el_erp_no_tenga_timbrados()
    {
        using var db = NewDb();

        // Sin timbrados en el ERP la validación ya no se omite; un RFC distinto al de la empresa (p. ej. de sandbox) no pasa.
        var act = () => Handlers(db).Handle(new ConciliarCfdiSatCommand("otro.txt",
            Archivo(Renglon(Guid.NewGuid().ToString(), 10m, emisor: "TMP010101AAA")), Lunes, Domingo), default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain(RfcEmpresa);
        (await Handlers(db).Handle(new ConciliarCfdiSatCommand("emitidos.txt",
            Archivo(Renglon(Guid.NewGuid().ToString(), 10m, emisor: RfcEmpresa.ToLowerInvariant())), Lunes, Domingo), default))
            .CfdiSat.Should().Be(1, "el RFC del archivo se compara sin importar mayúsculas");
    }

    [Fact]
    public async Task La_diferencia_se_marca_revisada_una_sola_vez_y_deja_de_contar_como_pendiente()
    {
        using var db = NewDb();
        var r = await Handlers(db).Handle(new ConciliarCfdiSatCommand("emitidos.txt",
            Archivo(Renglon(Guid.NewGuid().ToString(), 10m)), Lunes, Domingo), default);
        var d = (await Handlers(db).Handle(new DiferenciasConciliacionSatQuery(r.Id, null, false), default)).Single();

        var marcada = await Handlers(db).Handle(new MarcarDiferenciaSatRevisadaCommand(r.Id, d.Id, "  CFDI emitido desde SAP  "), default);
        var otraVez = () => Handlers(db).Handle(new MarcarDiferenciaSatRevisadaCommand(r.Id, d.Id, null), default);

        (marcada.Revisada, marcada.RevisadaPor, marcada.Nota).Should().Be((true, Usuario, "CFDI emitido desde SAP"));
        marcada.RevisadaPorNombre.Should().Be("Usuario Prueba", "la pantalla muestra quién la revisó");
        d.Moneda.Should().BeNull("un CFDI que solo está en el SAT no trae moneda en los metadatos");
        (await otraVez.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("CONCILIACION_SAT_YA_REVISADA");
        (await Handlers(db).Handle(new ConciliacionSatQuery(r.Id), default)).DiferenciasPendientes.Should().Be(0);
        (await Handlers(db).Handle(new DiferenciasConciliacionSatQuery(r.Id, null, false), default)).Should().BeEmpty();
    }

    [Fact]
    public async Task La_diferencia_de_un_cfdi_del_erp_lleva_su_moneda()
    {
        using var db = NewDb();
        var fa = await TimbradoAsync(db, Lunes.AddDays(1));

        var r = await Handlers(db).Handle(new ConciliarCfdiSatCommand("emitidos.txt",
            Archivo(Renglon(fa.Uuid!, fa.Total + 1m)), Lunes, Domingo), default);

        var d = (await Handlers(db).Handle(new DiferenciasConciliacionSatQuery(r.Id, TipoDiferenciaSat.TotalDistinto, null), default)).Single();
        d.Moneda.Should().Be("MXN");
    }

    [Fact]
    public void El_periodo_no_puede_ir_al_reves_ni_pasar_de_un_trimestre()
    {
        var alReves = () => Domain.ConciliacionSat.ConciliacionSat.Registrar(Empresa, Domingo, Lunes,
            FuenteConciliacionSat.ArchivoMetadata, "x.txt", "h", Usuario, Ahora, 0, 0, []);
        var largo = () => Domain.ConciliacionSat.ConciliacionSat.Registrar(Empresa, Lunes, Lunes.AddDays(120),
            FuenteConciliacionSat.ArchivoMetadata, "x.txt", "h", Usuario, Ahora, 0, 0, []);

        alReves.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONCILIACION_SAT_PERIODO_INVALIDO");
        largo.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONCILIACION_SAT_PERIODO_LARGO");
    }

    // ---------- Apoyo ----------

    private static int _folio;

    /// <summary>Factura de anticipo timbrada el día indicado (mediodía en Mérida, UTC-6).</summary>
    private static async Task<FacturaAnticipo> TimbradoAsync(FacturacionDbContext db, DateOnly dia)
    {
        var n = Interlocked.Increment(ref _folio);
        var fa = FacturaAnticipo.CrearBorrador(Empresa, $"FA-{n}", n, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor(RfcCliente, "CLIENTE PRUEBA", "601", "64000", "G03", "MEX", false),
            new DatosFiscalesEmisor(RfcEmpresa, "EMPRESA PRUEBA", "601", "97000"), "03", "MXN", null, dia.Year, dia.Month,
            TipoAnticipo.ClientesMxp, null, Guid.NewGuid(), 1000m, 0.16m);
        fa.MarcarTimbradoEnProceso();
        fa.MarcarTimbrado(Guid.NewGuid().ToString().ToUpperInvariant(), null, null, null,
            new DateTimeOffset(dia.Year, dia.Month, dia.Day, 18, 0, 0, TimeSpan.Zero), null, null);
        db.FacturasAnticipo.Add(fa);
        await db.SaveChangesAsync();
        return fa;
    }

    private static ConciliacionSatHandlers Handlers(FacturacionDbContext db) =>
        new(db, new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(Empresa, RfcEmpresa, "Empresa prueba", "601", 0.16m, "97000")),
            new FakeUsuariosReadPort(new Dictionary<Guid, string> { [Usuario] = "Usuario Prueba" }), new FakeEmpresaContext(Empresa), new FakeUserContext(Usuario), new FakeClock(Ahora));

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(Empresa));
}
