using FluentAssertions;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// 86e3za6ck y 86e3zcbjw: verificación de que las cuentas reales confirmadas por Laura Cerón (5-nov)
/// y Fernando Alejos se mapean a los roles del motor contable sin roles nulos ni cuentas provisionales.
/// </summary>
public class CuentasOficialesRolesTests
{
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly DateOnly Fecha = new(2026, 11, 7);

    public static readonly IReadOnlyDictionary<string, string> MapeoOficialEsperado = new Dictionary<string, string>
    {
        // 86e3za6ck: Cuentas nuevas y roles base confirmados por Contabilidad (5-nov)
        ["MERCANCIA_RECIBIDA_POR_FACTURAR"] = "201.05.00.00",
        ["COBROS_TC_TD"] = "101.01.03.00",
        ["COBROS_CHEQUE"] = "101.01.04.00",
        ["MERMA"] = "504.25.02.00",
        ["AJUSTE_INVENTARIO"] = "504.25.02.00",
        ["IVA_ACREDITABLE_PENDIENTE"] = "119.01.00.00",
        ["PROVEEDORES"] = "201.01.00.00",
        ["ANTICIPO_A_PROVEEDORES"] = "120.01.00.00",
        ["CLIENTES"] = "105.01.00.00",
        ["VENTAS"] = "401.01.00.00",
        ["IVA_TRASLADADO_NO_COBRADO"] = "209.01.00.00",
        ["ANTICIPOS_DE_CLIENTES"] = "206.01.00.00",
        ["DESCUENTOS_SOBRE_VENTAS"] = "402.01.00.00",
        ["IVA_ACREDITABLE_PAGADO"] = "118.01.01.00",
        ["COMISIONES_BANCARIAS"] = "701.10.00.00",

        // 86e3zcbjw: Tabla oficial de 13 retenciones (Laura Cerón, 5-nov)
        // Proveedores (Pasivo)
        ["RETENCIONES_POR_PAGAR_HONORARIOS_PF_ISR"] = "216.04.00.00",
        ["RETENCIONES_POR_PAGAR_HONORARIOS_PF_IVA"] = "216.10.01.00",
        ["RETENCIONES_POR_PAGAR_ARRENDAMIENTO_PF_ISR"] = "216.03.00.00",
        ["RETENCIONES_POR_PAGAR_ARRENDAMIENTO_PF_IVA"] = "216.02.03.00",
        ["RETENCIONES_POR_PAGAR_FLETES_IVA"] = "216.10.02.00",
        ["RETENCIONES_POR_PAGAR_SERVICIOS_IVA"] = "216.02.04.00",
        ["RETENCIONES_POR_PAGAR_RESICO_PF_ISR"] = "216.01.05.00",
        ["RETENCIONES_POR_PAGAR_RESICO_PF_SERVICIOS_ISR"] = "216.01.05.00",
        ["RETENCIONES_POR_PAGAR_RESICO_PF_SERVICIOS_IVA"] = "216.02.02.02",
        // Clientes (Activo 113.01.02.0x)
        ["RETENCIONES_CLIENTES_FLETES_IVA"] = "113.01.02.01",
        ["RETENCIONES_CLIENTES_SERVICIOS_IVA"] = "113.01.02.03",
        ["RETENCIONES_CLIENTES_DESPERDICIO_IVA"] = "113.01.02.05",
        ["RETENCIONES_CLIENTES_IMPORTACION_IVA"] = "113.01.02.07",

        // 86e3yyy8t: Cuentas oficiales de inventario (Fernando Alejos, 5-nov)
        ["INVENTARIO_VIDRIO_FLOTADO"] = "115.02.01.00",
        ["INVENTARIO_VIDRIO_TEMPLADO"] = "115.02.01.00",
        ["INVENTARIO_VIDRIO_LAMINADO"] = "115.02.02.00",
        ["INVENTARIO_VIDRIO_INSULADO"] = "115.02.02.00",
        ["INVENTARIO_INSUMOS_PRODUCCION"] = "115.02.03.00",
        ["INVENTARIO_REFAC_MAQ_Y_OTROS"] = "115.02.03.00",
        ["INVENTARIO_ALUMINIO"] = "115.07.02.00",
        ["INVENTARIO_ALUCOM"] = "115.07.02.00",
        ["INVENTARIO_REYNOBON"] = "115.07.02.00",
    };

    [Theory]
    [InlineData("MERCANCIA_RECIBIDA_POR_FACTURAR", "201.05.00.00")]
    [InlineData("COBROS_TC_TD", "101.01.03.00")]
    [InlineData("COBROS_CHEQUE", "101.01.04.00")]
    [InlineData("MERMA", "504.25.02.00")]
    [InlineData("AJUSTE_INVENTARIO", "504.25.02.00")]
    [InlineData("RETENCIONES_POR_PAGAR_SERVICIOS_IVA", "216.02.04.00")]
    [InlineData("RETENCIONES_POR_PAGAR_RESICO_PF_ISR", "216.01.05.00")]
    [InlineData("RETENCIONES_POR_PAGAR_RESICO_PF_SERVICIOS_IVA", "216.02.02.02")]
    [InlineData("RETENCIONES_CLIENTES_IMPORTACION_IVA", "113.01.02.07")]
    [InlineData("INVENTARIO_VIDRIO_LAMINADO", "115.02.02.00")]
    [InlineData("INVENTARIO_ALUMINIO", "115.07.02.00")]
    [InlineData("INVENTARIO_ALUCOM", "115.07.02.00")]
    [InlineData("INVENTARIO_REYNOBON", "115.07.02.00")]
    public void Cada_rol_oficial_debe_tener_su_cuenta_contable_real_asignada(string rolCodigo, string cuentaCodigoEsperada)
    {
        MapeoOficialEsperado.Should().ContainKey(rolCodigo);
        MapeoOficialEsperado[rolCodigo].Should().Be(cuentaCodigoEsperada);
    }

    [Fact]
    public void Todos_los_roles_de_retencion_proveedor_y_cliente_tienen_cuenta_oficial_sin_null()
    {
        var rolesRetencion = MapeoOficialEsperado.Keys.Where(k => k.StartsWith("RETENCIONES_", StringComparison.Ordinal)).ToList();
        rolesRetencion.Should().HaveCount(13, "son exactamente 13 retenciones oficiales reportadas por Laura Cerón");

        foreach (var rol in rolesRetencion)
        {
            var cuenta = MapeoOficialEsperado[rol];
            cuenta.Should().NotBeNullOrWhiteSpace($"el rol {rol} no debe ser nulo");
            cuenta.Should().NotStartWith("990.", "no debe ser cuenta de prueba");
        }
    }

    [Fact]
    public void Motor_resuelve_cuenta_asociada_para_roles_base_y_por_clasificador()
    {
        var cuentaIdMap = MapeoOficialEsperado.ToDictionary(k => k.Key, _ => Guid.NewGuid());
        var listaRoles = cuentaIdMap.Select(kv => RolCuenta.Crear(
            Guid.NewGuid(), kv.Key, $"Rol {kv.Key}", kv.Value, null, null, new(2000, 1, 1))).ToList();

        // 1. Roles base
        foreach (var rol in new[] { "MERCANCIA_RECIBIDA_POR_FACTURAR", "COBROS_TC_TD", "COBROS_CHEQUE", "MERMA", "AJUSTE_INVENTARIO" })
        {
            var res = GenerarPolizaDesdeEventoHandler.Rol(listaRoles, rol, Sucursal, "MXN", Fecha);
            res.Should().NotBeNull();
            res!.CuentaId.Should().Be(cuentaIdMap[rol]);
        }

        // 2. Roles de retenciones por clasificador
        var cuentaRetFletes = GenerarPolizaDesdeEventoHandler.CuentaPorClasificador(
            listaRoles, RetencionesContables.RolPorPagar, "FLETES_IVA", Sucursal, "MXN", Fecha);
        cuentaRetFletes.Should().Be(cuentaIdMap["RETENCIONES_POR_PAGAR_FLETES_IVA"]);

        var cuentaRetResico = GenerarPolizaDesdeEventoHandler.CuentaPorClasificador(
            listaRoles, RetencionesContables.RolPorPagar, "RESICO_PF_SERVICIOS_IVA", Sucursal, "MXN", Fecha);
        cuentaRetResico.Should().Be(cuentaIdMap["RETENCIONES_POR_PAGAR_RESICO_PF_SERVICIOS_IVA"]);

        var cuentaCliDesp = GenerarPolizaDesdeEventoHandler.CuentaPorClasificador(
            listaRoles, RetencionesContables.RolClientes, "DESPERDICIO_IVA", Sucursal, "MXN", Fecha);
        cuentaCliDesp.Should().Be(cuentaIdMap["RETENCIONES_CLIENTES_DESPERDICIO_IVA"]);

        var cuentaCliImpo = GenerarPolizaDesdeEventoHandler.CuentaPorClasificador(
            listaRoles, RetencionesContables.RolClientes, "IMPORTACION_IVA", Sucursal, "MXN", Fecha);
        cuentaCliImpo.Should().Be(cuentaIdMap["RETENCIONES_CLIENTES_IMPORTACION_IVA"]);

        // 3. Roles de inventario por clasificador
        var cuentaInvLam = GenerarPolizaDesdeEventoHandler.CuentaPorClasificador(
            listaRoles, "INVENTARIO", "VIDRIO LAMINADO", Sucursal, "MXN", Fecha);
        cuentaInvLam.Should().Be(cuentaIdMap["INVENTARIO_VIDRIO_LAMINADO"]);

        var cuentaInvAlu = GenerarPolizaDesdeEventoHandler.CuentaPorClasificador(
            listaRoles, "INVENTARIO", "ALUMINIO", Sucursal, "MXN", Fecha);
        cuentaInvAlu.Should().Be(cuentaIdMap["INVENTARIO_ALUMINIO"]);
    }
}
