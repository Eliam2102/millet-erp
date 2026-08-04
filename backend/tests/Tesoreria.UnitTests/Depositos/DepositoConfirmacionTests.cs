using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.UnitTests.Depositos;

/// <summary>
/// Tests del ciclo <c>Pendiente → Confirmada/Rechazada</c> de la
/// confirmación de depósitos (TES-PR7, §3.3 / TES-9). RN-6: confirmar
/// solo desde movimiento de ingreso identificado.
/// </summary>
public sealed class DepositoConfirmacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();

    private static CuentaBancaria Cuenta(string moneda = "MXN") =>
        new(EmpresaId, "BBVA", "0123456789", clabe: null, moneda: moneda);

    private static MovimientoBancario Ingreso(decimal monto = 5_000m, string moneda = "MXN") =>
        MovimientoBancario.RegistrarIngreso(
            EmpresaId, Cuenta(moneda), monto, new DateOnly(2026, 7, 15),
            referenciaBancaria: "SPEI-777", conceptoId: null,
            beneficiarioTipo: BeneficiarioTipo.Cliente, beneficiarioRef: Guid.NewGuid(),
            creadoPor: UsuarioId, ahora: Ahora);

    private static DepositoConfirmacion Propuesta(decimal monto = 5_000m, string moneda = "MXN") =>
        DepositoConfirmacion.CrearDesdePropuesta(
            EmpresaId, Guid.NewGuid(), Guid.NewGuid(), "DEP-123", monto, moneda,
            """[{"FacturaVentaId":"3fa85f64-5717-4562-b3fc-2c963f66afa6","Folio":"VEN-1","ImporteAplicado":5000.00}]""");

    [Fact]
    public void Confirmar_liga_movimiento_y_resuelve()
    {
        var deposito = Propuesta();
        var movimiento = Ingreso();

        deposito.Confirmar(movimiento, UsuarioId, Ahora);

        deposito.Estado.Should().Be(EstadoDepositoConfirmacion.Confirmada);
        deposito.MovimientoId.Should().Be(movimiento.Id);
        deposito.ResueltaPor.Should().Be(UsuarioId);
        deposito.ResueltaEn.Should().Be(Ahora);
    }

    [Fact]
    public void Confirmar_rechaza_movimiento_de_egreso()
    {
        var deposito = Propuesta();
        var egreso = MovimientoBancario.RegistrarPagoProveedor(
            EmpresaId, Cuenta(), Guid.NewGuid(), 5_000m, new DateOnly(2026, 7, 15),
            null, null, UsuarioId, Ahora);

        var act = () => deposito.Confirmar(egreso, UsuarioId, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("DEP_MOVIMIENTO_NO_INGRESO");
    }

    [Fact]
    public void Confirmar_rechaza_movimiento_ya_aplicado()
    {
        var deposito = Propuesta();
        var movimiento = Ingreso();
        movimiento.ActualizarEstadoAplicacion(movimiento.Monto);

        var act = () => deposito.Confirmar(movimiento, UsuarioId, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("DEP_MOVIMIENTO_YA_APLICADO");
    }

    [Fact]
    public void Confirmar_propuesta_exige_monto_exacto()
    {
        var deposito = Propuesta(monto: 5_000m);
        var movimiento = Ingreso(monto: 4_990m);

        var act = () => deposito.Confirmar(movimiento, UsuarioId, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("DEP_MONTO_NO_COINCIDE");
    }

    [Fact]
    public void Confirmar_exige_moneda_igual()
    {
        var deposito = Propuesta(moneda: "USD");
        var movimiento = Ingreso(moneda: "MXN");

        var act = () => deposito.Confirmar(movimiento, UsuarioId, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("DEP_MONEDA_DISTINTA");
    }

    [Fact]
    public void Expectativa_de_caja_admite_monto_aproximado()
    {
        var expectativa = DepositoConfirmacion.CrearExpectativaCaja(
            EmpresaId, Guid.NewGuid(), efectivoDeclarado: 12_345.50m, diaOperacion: new DateOnly(2026, 7, 14));
        var movimiento = Ingreso(monto: 12_000m); // el fondo del día siguiente se quedó en caja

        expectativa.Confirmar(movimiento, UsuarioId, Ahora);

        expectativa.Estado.Should().Be(EstadoDepositoConfirmacion.Confirmada);
        expectativa.ClienteId.Should().BeNull();
        expectativa.DepositoRef.Should().Be("CAJA 2026-07-14");
    }

    [Fact]
    public void Rechazar_exige_motivo_y_resuelve()
    {
        var deposito = Propuesta();

        deposito.Rechazar("El depósito no aparece en banco", UsuarioId, Ahora);

        deposito.Estado.Should().Be(EstadoDepositoConfirmacion.Rechazada);
        deposito.MotivoRechazo.Should().Be("El depósito no aparece en banco");

        var otra = Propuesta();
        var act = () => otra.Rechazar("  ", UsuarioId, Ahora);
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("DEP_MOTIVO_VACIO");
    }

    [Fact]
    public void Resuelta_no_se_vuelve_a_resolver()
    {
        var deposito = Propuesta();
        deposito.Confirmar(Ingreso(), UsuarioId, Ahora);

        var act = () => deposito.Rechazar("cambio de opinión", UsuarioId, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("DEP_YA_RESUELTA");
    }

    [Fact]
    public void MarcarReppTimbrado_solo_sobre_confirmada()
    {
        var deposito = Propuesta();

        var act = deposito.MarcarReppTimbrado;
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("DEP_NO_CONFIRMADA");

        deposito.Confirmar(Ingreso(), UsuarioId, Ahora);
        deposito.MarcarReppTimbrado();
        deposito.ReppTimbrado.Should().BeTrue();
    }
}
