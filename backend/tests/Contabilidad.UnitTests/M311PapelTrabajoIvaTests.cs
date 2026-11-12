using System.Reflection;
using Millet.Contabilidad.Application.Iva;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>M-311 · papel de trabajo del IVA: la foto no cambia, el acuse es único y las diferencias se cierran al ligar su ajuste. Datos FIX.</summary>
public class M311PapelTrabajoIvaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly (string, string) Pago = ("tesoreria", "pago_factura_proveedor");
    private static readonly DateOnly Dia = new(2026, 10, 20);
    private static readonly DateTimeOffset Ahora = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static SaldoIvaDocumento Compra(decimal @base, decimal iva) =>
        SaldoIvaDocumento.Crear(Empresa, LadoIva.Acreditable, N.DocFacturaProveedor, Guid.NewGuid(), "FP-FIX", "UUID-FIX", "PUE", Guid.NewGuid(),
            Guid.NewGuid(), new DateOnly(2026, 10, 1), "MXN", null, @base, iva, 0m, Guid.NewGuid());

    private static ReclasificacionIva Pagar(SaldoIvaDocumento s, decimal importe)
    {
        var (iva, ret) = s.Calcular(importe);
        return s.Aplicar(Pago, Guid.NewGuid(), Dia, importe, iva, ret, Guid.NewGuid());
    }

    /// <summary>La liquidación del mes como la arma el handler, sin base: documentos con lo que cada pago vigente movió.</summary>
    private static LiquidacionIvaResponse Liquidacion(IReadOnlyCollection<ReclasificacionIva> movimientos, Dictionary<Guid, SaldoIvaDocumento> saldos)
    {
        var vigentes = movimientos.Where(m => m.AnuladaEn is null).ToList();
        var documentos = vigentes.GroupBy(m => m.SaldoIvaDocumentoId).Select(g =>
        {
            var s = saldos[g.Key];
            return new DocumentoLiquidacionIva(s.Id, s.Lado, s.OrigenTipo, s.DocumentoId, s.Folio, s.Uuid, "PUE", s.Fecha, s.Moneda, s.Base, s.Iva,
                g.Sum(m => m.Importe), g.Sum(m => m.IvaMxn), 0m, s.IvaPendiente, s.Saldo, true, []);
        }).ToList();
        var resumen = LiquidacionIvaHandlers.Resumen(vigentes, saldos);
        return new(2026, 10, resumen, [], true, documentos, 0);
    }

    [Fact]
    public void M311_tras_revertir_un_pago_del_mes_la_foto_no_cambia_y_la_liquidacion_viva_si()
    {
        var factura = Compra(11_600m, 1_600m);
        var otra = Compra(5_800m, 800m);
        var pagoFactura = Pagar(factura, 11_600m);
        var pagoOtra = Pagar(otra, 5_800m);
        var saldos = new[] { factura, otra }.ToDictionary(s => s.Id);
        var movimientos = new List<ReclasificacionIva> { pagoFactura, pagoOtra };

        var antes = Liquidacion(movimientos, saldos);
        var foto = LiquidacionIvaFoto.Crear(Empresa, antes, Guid.NewGuid(), "IVA-2026-10", Guid.NewGuid(), Ahora);
        var resumenFoto = foto.Resumen();
        var detalleFoto = foto.Detalle();

        factura.Revertir(pagoFactura, Ahora.AddDays(5)); // el pago se revierte con el mes ya liquidado

        var viva = Liquidacion(movimientos, saldos);
        viva.Resumen.IvaAcreditablePagado.Should().Be(800m, "el mes vivo ya no cuenta el pago revertido");
        foto.Resumen().Should().Be(resumenFoto, "la foto guardada no cambia");
        foto.Resumen().IvaAcreditablePagado.Should().Be(2_400m);
        foto.Detalle().Should().BeEquivalentTo(detalleFoto);
    }

    [Fact]
    public void M311_la_foto_no_tiene_ningun_mutador_publico()
    {
        typeof(LiquidacionIvaFoto).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != nameof(LiquidacionIvaFoto.EmpresaId))
            .Should().OnlyContain(p => p.SetMethod == null || !p.SetMethod.IsPublic, "la foto es de solo lectura desde el código");
        typeof(LiquidacionIvaFoto).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName).Select(m => m.Name).Should().BeEquivalentTo(nameof(LiquidacionIvaFoto.Resumen), nameof(LiquidacionIvaFoto.Detalle), nameof(LiquidacionIvaFoto.Cuadre));
    }

    [Fact]
    public void M311_el_pago_revertido_aparece_como_diferencia_pendiente_y_se_cierra_al_ligar_su_ajuste()
    {
        var factura = Compra(11_600m, 1_600m);
        var pago = Pagar(factura, 11_600m);
        var saldos = new[] { factura }.ToDictionary(s => s.Id);
        var foto = LiquidacionIvaFoto.Crear(Empresa, Liquidacion([pago], saldos), Guid.NewGuid(), "IVA-2026-10", Guid.NewGuid(), Ahora);

        factura.Revertir(pago, Ahora.AddDays(5));
        var cambios = DetectorDiferenciasIva.Comparar(foto.Detalle(), Liquidacion([pago], saldos).Documentos);

        var cambio = cambios.Should().ContainSingle().Subject;
        (cambio.IvaFoto, cambio.IvaActual, cambio.Causa).Should().Be((1_600m, 0m, DetectorDiferenciasIva.CausaPagoRevertido));
        var diferencia = DiferenciaFiscalIva.Detectar(Empresa, foto.Id, cambio, Ahora);
        (diferencia.Estado, diferencia.Diferencia).Should().Be((EstadoDiferenciaFiscalIva.Pendiente, -1_600m));

        var ajuste = Guid.NewGuid();
        diferencia.LigarAjuste(ajuste, Guid.NewGuid(), Ahora.AddDays(6));
        (diferencia.Estado, diferencia.AjustePolizaId).Should().Be((EstadoDiferenciaFiscalIva.Cerrada, ajuste));
        var otraVez = () => diferencia.LigarAjuste(Guid.NewGuid(), Guid.NewGuid(), Ahora);
        otraVez.Should().Throw<ConflictException>().Which.Code.Should().Be("IVA_DIFERENCIA_CERRADA");
    }

    [Fact]
    public void M311_sin_cambios_no_hay_diferencias_y_un_pago_posterior_se_detecta()
    {
        var factura = Compra(11_600m, 1_600m);
        var pago = Pagar(factura, 5_800m);
        var saldos = new[] { factura }.ToDictionary(s => s.Id);
        var foto = LiquidacionIvaFoto.Crear(Empresa, Liquidacion([pago], saldos), Guid.NewGuid(), "IVA-2026-10", Guid.NewGuid(), Ahora);
        DetectorDiferenciasIva.Comparar(foto.Detalle(), Liquidacion([pago], saldos).Documentos).Should().BeEmpty();

        var tardio = Pagar(factura, 5_800m);
        var cambio = DetectorDiferenciasIva.Comparar(foto.Detalle(), Liquidacion([pago, tardio], saldos).Documentos).Should().ContainSingle().Subject;
        (cambio.IvaFoto, cambio.IvaActual, cambio.Causa).Should().Be((800m, 1_600m, DetectorDiferenciasIva.CausaPagoNuevo));
    }

    [Fact]
    public void M311_ligar_el_ajuste_exige_una_poliza()
    {
        var d = DiferenciaFiscalIva.Detectar(Empresa, Guid.NewGuid(), new(Guid.NewGuid(), "FP", "U", 10m, 0m, "x"), Ahora);
        var r = () => d.LigarAjuste(Guid.Empty, Guid.NewGuid(), Ahora);
        r.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("IVA_DIFERENCIA_AJUSTE");
        d.Estado.Should().Be(EstadoDiferenciaFiscalIva.Pendiente);
    }

    [Theory]
    [InlineData("", 10, "IVA_ACUSE_NOMBRE")]
    [InlineData("acuse.pdf", 0, "IVA_ACUSE_TAMANO")]
    [InlineData("acuse.pdf", LiquidacionIvaAcuse.MaxBytes + 1, "IVA_ACUSE_TAMANO")]
    public void M311_el_acuse_valida_nombre_y_tamano(string nombre, int bytes, string codigo)
    {
        var r = () => LiquidacionIvaAcuse.Crear(Empresa, Guid.NewGuid(), nombre, "application/pdf", new byte[bytes], Guid.NewGuid(), Ahora);
        r.Should().Throw<BusinessRuleException>().Which.Code.Should().Be(codigo);
    }

    [Fact]
    public void M311_el_acuse_guarda_la_huella_del_archivo()
    {
        var a = LiquidacionIvaAcuse.Crear(Empresa, Guid.NewGuid(), " acuse.pdf ", "application/pdf", [1, 2, 3], Guid.NewGuid(), Ahora);
        (a.Nombre, a.Sha256.Length).Should().Be(("acuse.pdf", 64));
    }
}
