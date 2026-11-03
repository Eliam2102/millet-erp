using System.Text.Json;
using Millet.Tesoreria.Application.Depositos;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Depositos;

namespace Millet.Tesoreria.UnitTests.Depositos;

/// <summary>86e3ke6yn: una prueba por rama de la regla de depósito automático.</summary>
public sealed class ReglaDepositoAutomaticoTests
{
    private const string Ref = "VAL-2026-0412";
    private static readonly Guid Importador = Guid.NewGuid();
    private static readonly Guid Proponente = Guid.NewGuid();
    private static readonly DepositoAutomaticoOptions Opciones = new()
    {
        PalabrasCheque = ["CH ", "CHEQUE", "CHQ", "SBC", "DOCTO"],
    };

    private static ExtractoLinea Linea(decimal monto = 48_500m, string? referencia = Ref,
        string descripcion = "SPEI RECIBIDO BBVA VIDRIOS Y ALUMINIOS DEL BAJIO") =>
        ExtractoLinea.Crear(Guid.NewGuid(), 1, new DateOnly(2026, 11, 3), descripcion, referencia, null, monto);

    private static string Facturas(params (decimal importe, decimal? saldo)[] fs) =>
        JsonSerializer.Serialize(fs.Select((f, i) =>
            new PropuestaFacturaPayload(Guid.NewGuid(), $"VEN-{i + 1}", f.importe, f.saldo)));

    private static DepositoConfirmacion Propuesta(decimal monto = 48_500m, string? facturas = null,
        string referencia = Ref, string moneda = "MXN", decimal saldoAFavor = 0m, Guid? sugerido = null,
        Guid? propuestoPor = null) =>
        DepositoConfirmacion.CrearDesdePropuesta(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), referencia, monto, moneda,
            facturas ?? Facturas((monto, 0m)), propuestoPor ?? Proponente, saldoAFavor, sugerido);

    private static ContextoDepositoAutomatico Ctx(bool periodoAbierto = true, Guid? movimiento = null,
        bool devolucion = false, string moneda = "MXN", Guid? importador = null) =>
        new(importador ?? Importador, moneda, periodoAbierto, movimiento, devolucion);

    private static ResultadoReglaDeposito Evaluar(ExtractoLinea linea, IReadOnlyList<DepositoConfirmacion> candidatos,
        ContextoDepositoAutomatico? ctx = null) =>
        ReglaDepositoAutomatico.Evaluar(linea, candidatos, Opciones, ctx ?? Ctx());

    private static ResultadoReglaDeposito.Revision Revision(ResultadoReglaDeposito r, string motivoContiene)
    {
        var rev = r.Should().BeOfType<ResultadoReglaDeposito.Revision>().Subject;
        rev.Motivo.Should().Contain(motivoContiene);
        return rev;
    }

    [Fact]
    public void Referencia_y_monto_exactos_con_una_factura_saldada_se_confirma_sola()
    {
        var p = Propuesta();
        var r = Evaluar(Linea(referencia: " val-2026-0412 "), [p]);

        var auto = r.Should().BeOfType<ResultadoReglaDeposito.AutoConfirmar>().Subject;
        auto.Deposito.Should().BeSameAs(p);
        auto.ReglaTexto.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Cheque_de_250_mil_va_a_revision_por_cheque()
    {
        var p = Propuesta(250_000m);
        Revision(Evaluar(Linea(250_000m, descripcion: "CH 004517 DEPOSITO CONSTRUCTORA DEL NORTE"), [p]), "Cheque")
            .Candidatos.Should().ContainSingle().Which.Should().BeSameAs(p);
    }

    [Fact]
    public void Monto_igual_al_umbral_va_a_revision()
    {
        var p = Propuesta(100_000m);
        Revision(Evaluar(Linea(100_000m), [p]), "umbral");
    }

    [Fact]
    public void Varias_facturas_va_a_revision()
    {
        var p = Propuesta(facturas: Facturas((20_000m, 0m), (28_500m, 0m)));
        Revision(Evaluar(Linea(), [p]), "2 facturas");
    }

    [Fact]
    public void Pago_parcial_va_a_revision()
    {
        var p = Propuesta(facturas: Facturas((48_500m, 12_000m)));
        Revision(Evaluar(Linea(), [p]), "Pago parcial");
    }

    [Fact]
    public void Saldo_desconocido_va_a_revision()
    {
        var p = Propuesta(facturas: Facturas((48_500m, null)));
        Revision(Evaluar(Linea(), [p]), "saldo");
    }

    [Fact]
    public void Anticipo_sin_facturas_va_a_revision()
    {
        var p = Propuesta(facturas: "[]");
        Revision(Evaluar(Linea(), [p]), "Anticipo");
    }

    [Fact]
    public void Saldo_a_favor_por_identificar_va_a_revision()
    {
        var p = Propuesta(saldoAFavor: 1_000m);
        Revision(Evaluar(Linea(), [p]), "Anticipo");
    }

    [Fact]
    public void Diferencia_de_un_peso_va_a_revision()
    {
        var p = Propuesta(48_500m);
        Revision(Evaluar(Linea(48_499m), [p]), "difiere");
    }

    [Fact]
    public void Dos_candidatos_con_la_misma_referencia_va_a_revision()
    {
        var a = Propuesta();
        var b = Propuesta();
        Revision(Evaluar(Linea(), [a, b]), "2 propuestas").Candidatos.Should().HaveCount(2);
    }

    [Fact]
    public void Sin_candidato_con_esa_referencia_queda_sin_coincidencia()
    {
        Evaluar(Linea(), [Propuesta(referencia: "OTRA-REF")]).Should().BeOfType<ResultadoReglaDeposito.SinCoincidencia>();
        Evaluar(Linea(referencia: null), [Propuesta()]).Should().BeOfType<ResultadoReglaDeposito.SinCoincidencia>();
    }

    [Fact]
    public void Otra_moneda_va_a_revision()
    {
        Revision(Evaluar(Linea(), [Propuesta(moneda: "USD")]), "MXN");
        Revision(Evaluar(Linea(), [Propuesta()], Ctx(moneda: "USD")), "MXN");
    }

    [Fact]
    public void Quien_importa_es_quien_propuso_va_a_revision()
    {
        var p = Propuesta(propuestoPor: Importador);
        Revision(Evaluar(Linea(), [p]), "propuso");
    }

    [Fact]
    public void Propuesta_ligada_a_otro_movimiento_va_a_revision()
    {
        var p = Propuesta(sugerido: Guid.NewGuid());
        Revision(Evaluar(Linea(), [p], Ctx(movimiento: Guid.NewGuid())), "otro movimiento");
        Evaluar(Linea(), [p], Ctx(movimiento: p.MovimientoSugeridoId)).Should().BeOfType<ResultadoReglaDeposito.AutoConfirmar>();
    }

    [Fact]
    public void Devolucion_en_curso_o_periodo_cerrado_va_a_revision()
    {
        Revision(Evaluar(Linea(), [Propuesta()], Ctx(devolucion: true)), "devolución");
        Revision(Evaluar(Linea(), [Propuesta()], Ctx(periodoAbierto: false)), "cerrado");
    }
}
