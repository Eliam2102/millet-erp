using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Api.Seed;

/// <summary>
/// C1.5 · cuentas DEMO para los roles de las plantillas automáticas (V12 pendiente: Contabilidad mapea las reales). Crea las cuentas 990.1x
/// que falten bajo el título DEMO y asigna cada rol que siga SIN cuenta; un rol ya mapeado no se toca. La cuenta puente usa el código
/// DEMO-PUENTE de K10.5 (mismo id determinista), para que el reporte de conciliación por OC lea las pólizas del motor.
/// </summary>
public sealed partial class DemoSesionSeedHostedService
{
    private static readonly (string Codigo, string Nombre, NaturalezaCuenta Naturaleza, CuentaControl Control, string[] Roles)[] CuentasRolesDemo =
    [
        ("990.01.00.00", "DEMO Banco", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["BANCOS", "BANCOS_MN"]),
        ("990.02.00.00", "DEMO Clientes colectiva", NaturalezaCuenta.Deudora, CuentaControl.Clientes, ["CLIENTES"]),
        ("990.03.00.00", "DEMO Gasto afectable", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["CONSUMO", "GASTO"]),
        ("990.05.00.00", "DEMO Inventario", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["INVENTARIO"]),
        ("DEMO-PUENTE", "DEMO Cuenta puente recepción–factura", NaturalezaCuenta.Acreedora, CuentaControl.Ninguna, ["MERCANCIA_RECIBIDA_POR_FACTURAR"]),
        ("990.10.00.00", "DEMO IVA acreditable pendiente de pago", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["IVA_ACREDITABLE_PENDIENTE"]),
        ("990.11.00.00", "DEMO Proveedores colectiva", NaturalezaCuenta.Acreedora, CuentaControl.Proveedores, ["PROVEEDORES"]),
        ("990.12.00.00", "DEMO Retenciones por pagar", NaturalezaCuenta.Acreedora, CuentaControl.Ninguna, ["RETENCIONES_POR_PAGAR"]),
        ("990.13.00.00", "DEMO Anticipo a proveedores", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["ANTICIPO_A_PROVEEDORES"]),
        ("990.14.00.00", "DEMO IVA acreditable pagado", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["IVA_ACREDITABLE_PAGADO"]),
        ("990.15.00.00", "DEMO Ventas", NaturalezaCuenta.Acreedora, CuentaControl.Ninguna, ["VENTAS"]),
        ("990.16.00.00", "DEMO IVA trasladado no cobrado", NaturalezaCuenta.Acreedora, CuentaControl.Ninguna, ["IVA_TRASLADADO_NO_COBRADO"]),
        ("990.17.00.00", "DEMO Anticipos de clientes", NaturalezaCuenta.Acreedora, CuentaControl.Ninguna, ["ANTICIPOS_DE_CLIENTES"]),
        ("990.18.00.00", "DEMO Descuentos sobre ventas", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["DESCUENTOS_SOBRE_VENTAS"]),
        ("990.19.00.00", "DEMO Efectivo en caja cobranza", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["EFECTIVO_CAJA_COBRANZA", "CAJA"]),
        ("990.20.00.00", "DEMO Cobros T/C, T/D", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["COBROS_TC_TD"]),
        ("990.21.00.00", "DEMO Cobros con cheque (cuenta por confirmar)", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["COBROS_CHEQUE"]),
        ("990.22.00.00", "DEMO Ajustes de inventario", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["AJUSTE_INVENTARIO"]),
        ("990.23.00.00", "DEMO Merma", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["MERMA"]),
        ("990.24.00.00", "DEMO Impuestos retenidos por clientes", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["RETENCIONES_CLIENTES"]),
        ("990.25.00.00", "DEMO Cobros con otra forma de pago", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["COBROS_OTRAS_FORMAS"]),
        ("990.26.00.00", "DEMO Deudores funcionarios y empleados (cuenta de prueba)", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["DEUDORES_FUNCIONARIOS_EMPLEADOS"]),
        ("990.27.00.00", "DEMO Comisiones bancarias (cuenta de prueba)", NaturalezaCuenta.Deudora, CuentaControl.Ninguna, ["COMISIONES_BANCARIAS"]),
    ];

    private static async Task SembrarRolesContablesAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<ContabilidadDbContext>();
        var titulo = await db.Cuentas.AsNoTracking().Where(c => c.Id == Id("DEMO-CTA-TITULO"))
            .Select(c => new { c.Id, c.Nivel }).FirstOrDefaultAsync(ct);
        if (titulo is null) return; // sin catálogo DEMO no hay dónde colgar las cuentas

        var roles = await db.RolesCuenta.Where(r => r.EmpresaId == EmpresaId && r.CuentaId == null && r.DeletedAt == null).ToListAsync(ct);
        if (roles.Count == 0) return;

        foreach (var (codigo, nombre, naturaleza, control, codigosRol) in CuentasRolesDemo)
        {
            var pendientes = roles.Where(r => codigosRol.Contains(r.Codigo)).ToList();
            if (pendientes.Count == 0) continue;
            var cuentaId = await db.Cuentas.AsNoTracking().Where(c => c.EmpresaId == EmpresaId && c.Codigo == codigo)
                .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
            if (cuentaId is null)
            {
                // DEMO-PUENTE: mismo id que el sembrador de K10.5, gane quien gane la carrera.
                var cuenta = new CuentaContable(codigo == "DEMO-PUENTE" ? Id("DEMO-K105-CUENTA/" + codigo) : Id("DEMO-C15-CUENTA/" + codigo),
                    codigo, nombre, titulo.Id, titulo.Nivel + 1, naturaleza, TipoCuenta.Afectable, control, null, null) { EmpresaId = EmpresaId };
                db.Cuentas.Add(cuenta);
                await db.SaveChangesAsync(ct);
                cuentaId = cuenta.Id;
            }
            foreach (var rol in pendientes)
                db.RolesCuentaCambios.Add(rol.CambiarCuenta(cuentaId.Value, "DEMO sembrador", Fecha, "C1.5: cuenta DEMO mientras Contabilidad mapea V12"));
        }
        await db.SaveChangesAsync(ct);
    }
}
