using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.Periodos;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Infrastructure.PublicAdapters;

namespace Millet.Contabilidad.Infrastructure;

/// <summary>
/// Wiring de DI del módulo Contabilidad. El DbContext se registra en <c>Program.cs</c> de Api
/// (mismo patrón que los demás módulos). La configuración de formato (<c>Contabilidad:Catalogo</c>)
/// se valida al arrancar: una configuración inconsistente tumba el host con mensaje claro (§20.1).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddContabilidadModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CatalogoOpciones>()
            .Bind(configuration.GetSection(CatalogoOpciones.Seccion))
            .PostConfigure(o => o.AplicarDefaults())
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CatalogoOpciones>, CatalogoOpcionesValidator>();
        services.AddSingleton(sp => new FormatoCatalogo(sp.GetRequiredService<IOptions<CatalogoOpciones>>().Value));
        services.AddScoped<Millet.Contabilidad.Application.Catalogo.SolicitudesCatalogo>();
        services.AddScoped<ICuentaContableReadPort, CuentaContableReadAdapter>();
        services.AddScoped<ISaldosContablesReadPort, SaldosContablesReadAdapter>();
        // CXC-06: pólizas de cobro para la conciliación de cartera de CxC.
        services.AddScoped<ICobrosContabilizadosReadPort, CobrosContabilizadosReadAdapter>();
        services.AddScoped<IRolCuentaSaldoReadPort, RolCuentaSaldoReadAdapter>();
        // F1-CON-09: conciliación de auxiliares contra cuentas de control. Los puertos de auxiliar los hospedan CxP, CxC y Tesorería.
        services.AddScoped<Millet.Contabilidad.Application.ConciliacionAuxiliares.CalculadoraConciliacionAuxiliares>();
        services.AddScoped<IConciliacionAuxiliaresResumenReadPort,
            Millet.Contabilidad.Application.ConciliacionAuxiliares.ConciliacionAuxiliaresResumenAdapter>();

        // F1-CON-02: reglas de dimensión. Los puertos ICentroCostoContabilidadPort e ISucursalContabilidadPort los
        // implementan sus dueños (CentrosCosto, Compartido) y se cablean en Program.cs.
        services.AddOptions<DimensionesOpciones>()
            .Bind(configuration.GetSection(DimensionesOpciones.Seccion))
            .PostConfigure(o => o.AplicarDefaults())
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DimensionesOpciones>, DimensionesOpcionesValidator>();
        services.AddScoped<ValidadorDimensiones>();
        services.AddScoped<IDimensionContableValidacionPort>(sp => sp.GetRequiredService<ValidadorDimensiones>());
        // H1 (86e3z5w25): área y equipo de las salidas de inventario capturadas fuera de Contabilidad (app de Almacenes).
        services.AddScoped<DimensionesSalidaInventario>();
        services.AddScoped<IDimensionesSalidaInventarioPort, DimensionesSalidaInventarioAdapter>();
        // H1 paso 2: área y equipo capturados en CxP (caja chica, viáticos y tarjetas).
        services.AddScoped<DimensionesGasto>();
        services.AddScoped<IDimensionesGastoPort, DimensionesGastoAdapter>();
        // H1 pasos 3 y 4: área y equipo capturados en Tesorería (conceptos bancarios) y Comercio Exterior (costo de importación).
        services.AddScoped<DimensionesCaptura>();
        services.AddScoped<IDimensionesCapturaPort, DimensionesCapturaAdapter>();
        services.AddScoped<AlcanceSucursalContable>();
        services.AddScoped<LecturaMovimientos>();

        // F1-CON-03: contrato público de consulta de periodo y candado de movimientos (falla cerrada, D9).
        services.AddScoped<IPeriodoContableConsultaPort, PeriodoContableConsultaAdapter>();
        services.AddScoped<VerificadorPeriodoContable>();

        // F1-CON-04: pólizas (C1.3), numeración (K10.9), anulación (K10.3) y el puerto del motor contable.
        services.AddScoped<RegistroPolizas>();
        services.AddScoped<LecturaPolizas>();
        services.AddScoped<IPolizaContablePort, PolizaContableAdapter>();
        services.AddScoped<IPolizaContableReadPort, PolizaContableAdapter>();
        services.AddScoped<IReversaPolizaContablePort, PolizaContableAdapter>();
        // C1.5: entrada única de los listeners (anulaciones, caja, consumo) y datos que el motor completa desde pólizas previas.
        services.AddScoped<Millet.Contabilidad.Application.Motor.ProcesadorEventosContables>();
        services.AddScoped<Millet.Contabilidad.Application.Motor.ControlIvaDocumentos>();
        services.AddScoped<Millet.Contabilidad.Application.Motor.IEnriquecedorEventoContable, Millet.Contabilidad.Application.Motor.EnriquecedorPolizasOrigen>();
        // CXC-06: ajuste autorizado del cobro corto (#12); el puerto lo implementa CxC.
        services.AddScoped<Millet.Contabilidad.Application.Motor.IEnriquecedorEventoContable, Millet.Contabilidad.Application.Motor.EnriquecedorAjusteCobro>();
        // F1-ALM-13: después del de pólizas origen (orden de registro = orden de ejecución). IArticuloContabilidadPort lo cablea Program.cs.
        services.AddScoped<Millet.Contabilidad.Application.Motor.IEnriquecedorEventoContable, Millet.Contabilidad.Application.Motor.EnriquecedorGrupoArticulo>();
        // PCV (86e3ke10b): costo de ventas de la entrega de PT. PLATFORM-TODO(<ValuacionPT>): sin valuación (PT5.12) el puerto responde null
        // y la entrega espera en la bandeja como dato faltante; PT5.12 registra su adaptador en Program.cs y reemplaza este.
        services.AddScoped<Millet.Contabilidad.Application.Ports.ICostoProductoTerminadoReadPort, Adapters.CostoProductoTerminadoSinValuacionAdapter>();
        services.AddScoped<Millet.Contabilidad.Application.Motor.IEnriquecedorEventoContable, Millet.Contabilidad.Application.Motor.EnriquecedorCostoVentasPt>();

        // K10.4: pólizas recurrentes con reversa automática; el generador corre también como proceso de fondo.
        services.AddScoped<GeneradorRecurrentes>();
        services.AddScoped<LecturaRecurrentes>();
        services.AddOptions<Workers.RecurrentesOpciones>().Bind(configuration.GetSection(Workers.RecurrentesOpciones.Seccion));
        services.AddHostedService<Workers.PolizasRecurrentesWorker>();

        // K10.5: cuenta puente recepción–factura (observador de pólizas automáticas de C1.4 y reporte de conciliación por OC).
        services.AddOptions<Millet.Contabilidad.Application.CuentaPuente.CuentaPuenteOpciones>()
            .Bind(configuration.GetSection(Millet.Contabilidad.Application.CuentaPuente.CuentaPuenteOpciones.Seccion));
        services.AddScoped<Millet.Contabilidad.Application.CuentaPuente.RegistroMovimientosPuente>();
        services.AddScoped<Millet.Contabilidad.Application.CuentaPuente.LecturaCuentaPuente>();
        services.AddScoped<Millet.Contabilidad.Application.Reportes.LecturaReportesContables>();
        services.AddScoped<Millet.Contabilidad.Application.PublicPorts.IPolizaAutomaticaObservador,
            Millet.Contabilidad.Application.CuentaPuente.ObservadorCuentaPuente>();
        return services;
    }

    private sealed class DimensionesOpcionesValidator : IValidateOptions<DimensionesOpciones>
    {
        public ValidateOptionsResult Validate(string? name, DimensionesOpciones options)
        {
            var errores = options.Validar().ToList();
            return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores.Select(e => $"{DimensionesOpciones.Seccion}: {e}"));
        }
    }

    /// <summary>Convierte cada inconsistencia en un mensaje de arranque legible.</summary>
    private sealed class CatalogoOpcionesValidator : IValidateOptions<CatalogoOpciones>
    {
        public ValidateOptionsResult Validate(string? name, CatalogoOpciones options)
        {
            var errores = options.Validar().ToList();
            return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores.Select(e => $"Contabilidad:Catalogo: {e}"));
        }
    }
}
