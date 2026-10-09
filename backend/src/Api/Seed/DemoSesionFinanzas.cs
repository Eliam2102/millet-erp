using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.Importacion;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Api.Seed;

public sealed partial class DemoSesionSeedHostedService
{
    public static ImportacionRequest CatalogoDemo() => new("DEMO-SESION", "DEMO-catalogo.csv", null,
        ["codigo_origen", "codigo", "nombre", "codigo_padre", "naturaleza", "cuenta_control", "no_afectable_manual"],
        [
            ["DEMO-CTA-TITULO", "990.00.00.00", "DEMO Catálogo ficticio", null, "Deudora", null],
            ["DEMO-CTA-BANCO", "990.01.00.00", "DEMO Banco — Por confirmar V40", "990.00.00.00", "Deudora", null, "Sí"],
            ["DEMO-CTA-CLIENTE", "990.02.00.00", "DEMO Clientes colectiva", "990.00.00.00", "Deudora", "Clientes"],
            ["DEMO-CTA-GASTO", "990.03.00.00", "DEMO Gasto afectable", "990.00.00.00", "Deudora", null],
            ["DEMO-CTA-PENDIENTE", "990.04.00.00", "DEMO Naturaleza por confirmar", "990.00.00.00", null, null],
            ["DEMO-CTA-INVENTARIO", "990.05.00.00", "DEMO Inventario — Por confirmar V40", "990.00.00.00", "Deudora", null, "Sí"],
            ["DEMO-CTA-IVA", "990.06.00.00", "DEMO IVA — Por confirmar V40", "990.00.00.00", "Deudora", null, "Sí"],
        ], null);

    private static async Task SembrarFinanzasAsync(IServiceProvider sp, CancellationToken ct)
    {
        var tesoreria = sp.GetRequiredService<TesoreriaDbContext>();
        foreach (var moneda in new[] { "MXN", "USD" })
        {
            var banco = "DEMO-CTA-" + moneda;
            // NumeroCuenta solo permite alfanuméricos; la clave DEMO-* vive en Banco.
            var numeroCuenta = "DEMOCTA" + moneda;
            if (!await tesoreria.CuentasBancarias.AnyAsync(c => c.EmpresaId == EmpresaId && c.NumeroCuenta == numeroCuenta, ct))
                tesoreria.CuentasBancarias.Add(new CuentaBancaria(EmpresaId, banco, numeroCuenta, null, moneda));
        }
        await tesoreria.SaveChangesAsync(ct);

        var db = sp.GetRequiredService<ContabilidadDbContext>();
        if (!await db.Ejercicios.AnyAsync(e => e.EmpresaId == EmpresaId && e.Anio == 2026, ct))
        {
            var ejercicio = new EjercicioContable(Id("DEMO-EJERCICIO-2026"), 2026) { EmpresaId = EmpresaId };
            var periodos = ejercicio.GenerarPeriodos();
            foreach (var periodo in periodos)
            {
                periodo.EmpresaId = EmpresaId;
                if (periodo.Numero <= 10) periodo.Abrir(null, "DEMO apertura inicial", null, "DEMO sembrador", Fecha);
                if (periodo.Numero <= 9) periodo.Cerrar(periodos.Where(p => p.Numero < periodo.Numero),
                    "DEMO cierre secuencial previo a octubre", null, "DEMO sembrador", Fecha);
            }
            db.Ejercicios.Add(ejercicio);
            db.Periodos.AddRange(periodos);
            await db.SaveChangesAsync(ct);
        }
        // No reabrir ni recerrar periodos operados durante un ensayo.
        if (await db.Importaciones.AnyAsync(l => l.EmpresaId == EmpresaId && l.Fuente == "DEMO-SESION", ct)) return;

        // Reutiliza el motor de importación C1.0 (formato, jerarquía, naturaleza,
        // controles y huella), con muestra ficticia; no incorpora el Excel privado.
        var request = CatalogoDemo();
        var analisis = new ImportadorCatalogo(sp.GetRequiredService<FormatoCatalogo>())
            .Analizar(LectorTabla.Leer(request), request.Fuente, ExistenteCatalogo.Vacio);
        if (!analisis.PuedeAplicar)
            throw new InvalidOperationException("DEMO: catálogo incompatible con el formato configurado: "
                + string.Join("; ", analisis.Hallazgos.Where(h => h.Severidad == "Error").Select(h => h.Mensaje)));
        var codigos = analisis.Filas.Where(f => f.Accion == Accion.Crear).Select(f => f.Codigo!).ToArray();
        if (await db.Cuentas.AnyAsync(c => c.EmpresaId == EmpresaId && codigos.Contains(c.Codigo), ct))
            throw new InvalidOperationException("DEMO: hay cuentas 990.* ajenas al lote DEMO; no se sobrescriben.");
        var lote = new ImportacionCatalogo(Id("DEMO-CATALOGO"), "DEMO-SESION", request.ArchivoNombre, analisis.Huella,
            codigos.Length, codigos.Length, 0, 0, Fecha, "DEMO sembrador") { EmpresaId = EmpresaId };
        db.Importaciones.Add(lote);
        var ids = analisis.Filas.Where(f => f.Accion == Accion.Crear).ToDictionary(f => f.Codigo!, f => Id(f.CodigoOrigen!));
        foreach (var fila in analisis.Filas.Where(f => f.Accion == Accion.Crear))
        {
            db.Cuentas.Add(new CuentaContable(ids[fila.Codigo!], fila.Codigo!, fila.Nombre!,
                fila.PadreCodigo is null ? null : ids[fila.PadreCodigo], fila.Nivel, fila.Naturaleza, fila.Tipo,
                fila.Control, fila.Agrupador, fila.Grupo, fila.Clase, fila.NoAfectableManual) { EmpresaId = EmpresaId });
            db.Origenes.Add(new CuentaContableOrigen(Id("DEMO-ORIGEN/" + fila.CodigoOrigen), ids[fila.Codigo!],
                fila.Fuente, fila.CodigoOrigen!, lote.Id) { EmpresaId = EmpresaId });
        }
        await db.SaveChangesAsync(ct);
    }
}
