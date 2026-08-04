using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.ComprobacionGastos;

public sealed class ReposicionCajaChicaTests
{
    private static ReposicionCajaChica Emitir(
        decimal monto = 928m,
        int numeroComprobaciones = 2,
        DestinoReposicionCaja destino = DestinoReposicionCaja.CuentaSucursal) =>
        ReposicionCajaChica.Emitir(
            empresaId: Guid.NewGuid(),
            sucursalId: Guid.NewGuid(),
            destino: destino,
            beneficiarioId: Guid.NewGuid(),
            moneda: "mxn",
            montoTotal: monto,
            numeroComprobaciones: numeroComprobaciones,
            esCorteManual: false,
            emitidaPor: Guid.NewGuid(),
            ahora: DateTimeOffset.UtcNow);

    [Fact]
    public void Emitir_normaliza_moneda_y_guarda_datos()
    {
        var r = Emitir(monto: 928m, numeroComprobaciones: 2);
        r.Moneda.Should().Be("MXN");
        r.MontoTotal.Should().Be(928m);
        r.NumeroComprobaciones.Should().Be(2);
        r.EsCorteManual.Should().BeFalse();
    }

    [Fact]
    public void Emitir_rechaza_monto_no_positivo()
    {
        var act = () => Emitir(monto: 0m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "REPO_MONTO_INVALIDO");
    }

    [Fact]
    public void Emitir_rechaza_sin_comprobaciones()
    {
        var act = () => Emitir(numeroComprobaciones: 0);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "REPO_SIN_COMPROBACIONES");
    }

    // ---- Reglas de destino/liga en ComprobacionGastos ----

    private static Domain.ComprobacionGastos.ComprobacionGastos CrearCajaChica() =>
        Domain.ComprobacionGastos.ComprobacionGastos.Crear(
            empresaId: Guid.NewGuid(),
            tipo: TipoComprobacionGastos.ReembolsoCajaChica,
            sucursalId: Guid.NewGuid(),
            responsableId: Guid.NewGuid(),
            fechaInicio: new DateOnly(2026, 7, 1),
            fechaFin: new DateOnly(2026, 7, 15),
            moneda: "MXN",
            observaciones: null,
            ahora: DateTimeOffset.UtcNow,
            destinoReposicion: DestinoReposicionCaja.Responsable);

    [Fact]
    public void Crear_caja_chica_sin_destino_rechaza()
    {
        var act = () => Domain.ComprobacionGastos.ComprobacionGastos.Crear(
            empresaId: Guid.NewGuid(),
            tipo: TipoComprobacionGastos.ReembolsoCajaChica,
            sucursalId: Guid.NewGuid(),
            responsableId: Guid.NewGuid(),
            fechaInicio: new DateOnly(2026, 7, 1),
            fechaFin: new DateOnly(2026, 7, 15),
            moneda: "MXN",
            observaciones: null,
            ahora: DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "COMP_DESTINO_REPOSICION_REQUERIDO");
    }

    [Fact]
    public void Crear_aduanales_con_destino_rechaza()
    {
        var act = () => Domain.ComprobacionGastos.ComprobacionGastos.Crear(
            empresaId: Guid.NewGuid(),
            tipo: TipoComprobacionGastos.GastosAduanales,
            sucursalId: Guid.NewGuid(),
            responsableId: Guid.NewGuid(),
            fechaInicio: new DateOnly(2026, 7, 1),
            fechaFin: new DateOnly(2026, 7, 15),
            moneda: "MXN",
            observaciones: null,
            ahora: DateTimeOffset.UtcNow,
            numeroPedimento: "26-13-3018-000123",
            destinoReposicion: DestinoReposicionCaja.CuentaSucursal);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "COMP_DESTINO_REPOSICION_NO_APLICA");
    }

    [Fact]
    public void AsignarReposicion_requiere_Aplicada()
    {
        var c = CrearCajaChica();
        var act = () => c.AsignarReposicion(Guid.NewGuid());
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "COMP_REPOSICION_NO_APLICADA");
    }

    [Fact]
    public void AsignarReposicion_liga_una_sola_vez()
    {
        var c = CrearCajaChica();
        c.AgregarLinea(
            facturaProveedorId: Guid.NewGuid(), cfdiRecibidoId: null, uuidCfdi: null,
            proveedorId: Guid.NewGuid(), folioProveedor: null,
            fechaCfdi: DateTimeOffset.UtcNow,
            subtotal: 100m, impuestosTrasladados: 16m, retenciones: 0m, total: 116m,
            moneda: "MXN", concepto: "x");
        c.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        c.Aplicar(Guid.NewGuid(), DateTimeOffset.UtcNow);

        var reposicionId = Guid.NewGuid();
        c.AsignarReposicion(reposicionId);
        c.ReposicionId.Should().Be(reposicionId);

        var act = () => c.AsignarReposicion(Guid.NewGuid());
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "COMP_REPOSICION_YA_CUBIERTA");
    }
}
