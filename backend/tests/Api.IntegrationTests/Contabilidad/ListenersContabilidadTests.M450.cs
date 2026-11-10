using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// M-450 (86e3zr7ft): toda póliza automática que carga o abona a costos (500) o gastos (600) lleva el área y el equipo que captura el
/// usuario, así que su línea de plantilla debe tomar el centro de costo del evento (<c>ConCentroCosto</c>). Recorre las plantillas
/// vigentes (las que dejaron las migraciones) y falla si una línea a una cuenta de resultados no lo toma. Una cuenta es de resultados si
/// su rol lo es por definición (lista de abajo) o si el rol apunta hoy a una cuenta 5xx/6xx. Las líneas que aún no tienen pantalla
/// donde capturar el dato quedan en <see cref="SinCapturaPendiente"/>, a la vista, con el motivo.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    /// <summary>Roles que cargan a costos o gastos por definición (C1.5, ALM-13, H1); el sufijo por clasificador se ignora.</summary>
    private static readonly string[] RolesDeResultados =
        ["CONSUMO", "MERMA", "AJUSTE_INVENTARIO", "GASTO", "GASTO_IMPORTACION", "CONCEPTO_BANCARIO", "COMISIONES_BANCARIAS",
         "DIFERENCIAS_EN_COBRO", "COSTO_DE_VENTAS"];

    /// <summary>
    /// Líneas a 500/600 sin pantalla de captura del área y el equipo (decisión pendiente con Contabilidad, M-450): la comisión de la
    /// factura de la terminal, la comisión y la diferencia del cobro con ajuste, y el costo de ventas de producto terminado. Sin el
    /// dato, si la cuenta lo exige, su póliza queda en Rechazos («Dato faltante»).
    /// </summary>
    private static readonly HashSet<(string Evento, string Rol)> SinCapturaPendiente =
    [
        ("facturacion.terminal.factura-banco-ligada.v1", "COMISIONES_BANCARIAS"),
        ("tesoreria.pago-cliente.confirmado.v1", "COMISIONES_BANCARIAS"),
        ("tesoreria.pago-cliente.confirmado.v1", "DIFERENCIAS_EN_COBRO"),
        ("producto_terminado.entrega.confirmada.v1", "COSTO_DE_VENTAS"),
    ];

    [Fact]
    public async Task M450_toda_linea_de_plantilla_a_costos_o_gastos_toma_el_centro_de_costo_del_evento()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();

        var plantillas = (await db.PlantillasContables.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.EmpresaId == EmpresaBootstrapId).ToListAsync())
            .GroupBy(p => p.TipoEvento)
            .Select(g => g.OrderByDescending(p => p.NumeroVersion).First())
            .ToList();
        plantillas.Should().NotBeEmpty();

        // Roles que hoy apuntan a una cuenta 5xx/6xx (genéricos o por clasificador, p. ej. CONCEPTO_BANCARIO_COMISION_BANCARIA).
        var rolesA56 = (await (from r in db.RolesCuenta.IgnoreQueryFilters().AsNoTracking()
                               join c in db.Cuentas.IgnoreQueryFilters().AsNoTracking() on r.CuentaId equals c.Id
                               where r.EmpresaId == EmpresaBootstrapId && r.DeletedAt == null
                               select new { r.Codigo, Cuenta = c.Codigo }).ToListAsync())
            .Where(x => x.Cuenta.StartsWith('5') || x.Cuenta.StartsWith('6'))
            .Select(x => x.Codigo).Distinct().ToList();

        bool DeResultados(string rol, bool porClasificador) =>
            RolesDeResultados.Contains(rol) || rolesA56.Contains(rol)
            || (porClasificador && rolesA56.Any(r => r.StartsWith(rol + "_", StringComparison.Ordinal)));

        var sinCentro = plantillas
            .SelectMany(p => p.Lineas.Select(l => (p.TipoEvento, Linea: l)))
            .Where(x => DeResultados(x.Linea.Rol, x.Linea.RolPorClasificador) && !x.Linea.ConCentroCosto
                && !SinCapturaPendiente.Contains((x.TipoEvento, x.Linea.Rol)))
            .Select(x => $"{x.TipoEvento} · {x.Linea.Rol} ({x.Linea.Campo})")
            .ToList();

        sinCentro.Should().BeEmpty("cada línea a costos o gastos debe llevar el área y el equipo capturados (M-450)");
    }
}
