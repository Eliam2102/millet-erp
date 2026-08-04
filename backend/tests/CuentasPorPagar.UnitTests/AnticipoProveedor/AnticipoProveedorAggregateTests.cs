using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.AnticipoProveedor;

public sealed class AnticipoProveedorAggregateTests
{
    private static Domain.AnticipoProveedor.AnticipoProveedor Capturar(
        decimal monto = 1000m,
        string serie = "FANT",
        string uuid = "5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B") =>
        Domain.AnticipoProveedor.AnticipoProveedor.Capturar(
            empresaId: Guid.NewGuid(),
            cfdiRecibidoId: Guid.NewGuid(),
            uuidCfdi: uuid,
            proveedorId: Guid.NewGuid(),
            serie: serie,
            folioProveedor: "ANT-001",
            fechaCfdi: DateTimeOffset.UtcNow,
            moneda: "MXN",
            tipoCambio: null,
            montoEntregado: monto,
            ordenCompraId: Guid.NewGuid(),
            capturadoPor: Guid.NewGuid(),
            ahora: DateTimeOffset.UtcNow);

    [Fact]
    public void Capturar_nace_Abierto_con_saldo_amortizable_completo()
    {
        var a = Capturar(monto: 1000m);
        a.Estado.Should().Be(EstadoAnticipo.Abierto);
        a.MontoEntregado.Should().Be(1000m);
        a.MontoAmortizado.Should().Be(0m);
        a.SaldoAmortizable.Should().Be(1000m);
    }

    [Fact]
    public void Capturar_normaliza_UUID_a_mayusculas()
    {
        var a = Capturar(uuid: "5fb0f1c2-3e2a-4f0f-9e2e-2c5c2b5c1a2b");
        a.UuidCfdi.Should().Be("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B");
    }

    [Fact]
    public void Capturar_rechaza_serie_diferente_a_FANT()
    {
        var act = () => Capturar(serie: "FAC");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_SERIE_INVALIDA");
    }

    [Fact]
    public void Capturar_rechaza_monto_no_positivo()
    {
        var act = () => Capturar(monto: 0m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_MONTO_INVALIDO");
    }

    [Fact]
    public void Amortizar_parcial_no_cambia_estado()
    {
        var a = Capturar(monto: 1000m);
        a.Amortizar(400m, DateTimeOffset.UtcNow);

        a.Estado.Should().Be(EstadoAnticipo.Abierto);
        a.MontoAmortizado.Should().Be(400m);
        a.SaldoAmortizable.Should().Be(600m);
    }

    [Fact]
    public void Amortizar_total_pasa_a_Amortizado_y_fecha_amortizacion()
    {
        var a = Capturar(monto: 1000m);
        a.Amortizar(1000m, DateTimeOffset.UtcNow);

        a.Estado.Should().Be(EstadoAnticipo.Amortizado);
        a.SaldoAmortizable.Should().Be(0m);
        a.FechaAmortizacion.Should().NotBeNull();
    }

    [Fact]
    public void Amortizar_exceso_lanza_saldo_insuficiente()
    {
        var a = Capturar(monto: 1000m);
        var act = () => a.Amortizar(1001m, DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_SALDO_INSUFICIENTE");
    }

    [Fact]
    public void Cancelar_anticipo_con_amortizacion_parcial_no_permitido()
    {
        var a = Capturar(monto: 1000m);
        a.Amortizar(100m, DateTimeOffset.UtcNow);
        var act = () => a.Cancelar("error", DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_CON_AMORTIZACION_PARCIAL");
    }

    [Fact]
    public void Cancelar_anticipo_intacto_pasa_a_Cancelado()
    {
        var a = Capturar(monto: 1000m);
        a.Cancelar("error captura", DateTimeOffset.UtcNow);
        a.Estado.Should().Be(EstadoAnticipo.Cancelado);
        a.MotivoCancelacion.Should().Be("error captura");
    }
}
