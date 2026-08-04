using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Catalogos;
using Millet.Facturacion.Infrastructure.Notificaciones;
using Millet.Facturacion.Infrastructure.Pdf;
using Millet.Facturacion.Infrastructure.Stubs;

namespace Millet.Facturacion.Infrastructure;

/// <summary>
/// Wiring DI del módulo Facturación (F0-PR1). El registro del
/// <c>FacturacionDbContext</c> + los interceptors transversales se hace en
/// <c>Program.cs</c> (mismo patrón que Compras / CxP / Almacén: cada módulo
/// expone sus servicios pero el DbContext se registra cerca del connection
/// string y del wiring de outbox).
///
/// <para>
/// F0-PR1 wirea los 7 puertos de lectura del 01-diseño §6.1:
/// </para>
/// <list type="bullet">
///   <item><c>ICatalogosSatReadPort</c> → adapter <b>real</b> sobre
///   <c>CompartidoDbContext</c> (catálogos SAT seedeados por
///   <c>Millet.Catalogos</c>).</item>
///   <item><c>ICfdiRepositorioPort</c> → adapter <b>real</b> en
///   <c>Integraciones.Fiscal</c> (cfdi_archivo, F2-PR1); lo registra
///   <c>AddIntegracionesFiscalModule</c>.</item>
///   <item><c>ICfdiTimbradoPort</c> → adapter <b>real</b>
///   <c>FiscalApiTimbradoAdapter</c> (SDK FiscalAPI, Integraciones.Fiscal) —
///   registro incondicional desde F12-PR3 (los stubs de emisión y el flag
///   <c>Facturacion:Timbrado:UsarPacReal</c> se eliminaron).</item>
///   <item>Los otros (<c>IPeriodoContablePort</c>, etc.) → stubs
///   <c>NoOp*/Stub*</c>, cada uno con su <c>PLATFORM-TODO</c> buscable
///   (ADR-0031), reemplazables por el adapter real en su fase.</item>
/// </list>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddFacturacionModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Catálogos SAT: adapter REAL sobre Compartido (no stub) ---
        // Scoped porque consume CompartidoDbContext (scoped).
        services.AddScoped<ICatalogosSatReadPort, CompartidoCatalogosSatReadAdapter>();

        // --- Master de Cliente/Producto: adapters REALES sobre Compartido ---
        // (ADR-0048 D5/D6 — compartido.clientes + compartido.producto_aw).
        // Deuda <DatosMaestrosFiscal> cerrada (ADR-0048 PR2).
        services.AddScoped<IClientesReadPort, DatosMaestros.ClientesReadAdapter>();
        services.AddScoped<IProductosReadPort, DatosMaestros.ProductosReadAdapter>();

        // --- Datos fiscales del emisor (Administración): adapter REAL sobre ---
        // compartido.empresas/sucursales (FAC-UX-PR1, cierra la pata backend
        // de <EmisorDefaults>).
        services.AddScoped<IEmpresaFiscalReadPort, DatosMaestros.EmpresaFiscalReadAdapter>();

        // --- Canales de venta (Administración): adapter REAL sobre ---
        // compartido.canales_venta (FAC-ING-PR2 — el enum pasó a catálogo).
        // Lo consumen validators (existencia+activo), el detalle de pedido
        // (nombre) y el lookup de selectores.
        services.AddScoped<ICanalesVentaReadPort, DatosMaestros.CanalesVentaReadAdapter>();

        // --- Sucursales (Administración): adapter REAL sobre ---
        // compartido.sucursales (CAJAS-PR1). Lo consumen los validators del
        // CRUD de Cajas (alcance solo con sucursales activas).
        services.AddScoped<ISucursalesReadPort, DatosMaestros.SucursalesReadAdapter>();

        // --- Candado de período (Contabilidad): stub "siempre abierto" ---
        // PLATFORM-TODO(<PeriodoContableCerrado>)
        services.AddScoped<IPeriodoContablePort, NoOpPeriodoContablePort>();

        // --- Emisión fiscal: adapter REAL del puerto ICfdiTimbradoPort ---
        // (SDK FiscalAPI, Integraciones.Fiscal). F12-PR3: registro incondicional
        // — sin flag ni stub; un ambiente sin credenciales PAC falla visible
        // (peor sería un stub emitiendo UUIDs falsos). El CSD lo custodia
        // FiscalAPI (D11); no existe ICsdProvider local.
        services.AddScoped<
            Millet.Integraciones.Fiscal.Domain.Ports.ICfdiTimbradoPort,
            Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter.FiscalApiTimbradoAdapter>();

        // --- Repo de CFDI de emitidos: adapter REAL en Integraciones.Fiscal ---
        // F2-PR1 reemplazó el stub local por `CfdiArchivoRepository`
        // (cfdi_archivo en integraciones_fiscal); lo registra
        // `AddIntegracionesFiscalModule` (que corre antes en Program.cs). CxP
        // custodia sus recibidos por su cuenta (alcance "Separados").

        // --- F2-PR2: PDF (real, QuestPDF) + notificación por correo [stub] ---
        services.AddSingleton<IGenerarPdfFacturaPort, QuestPdfFacturaGenerator>();
        // PLATFORM-TODO(<EnvioCfdiCliente>)
        services.AddSingleton<INotificacionService, NoOpNotificacionService>();

        // --- F3-PR1b: ingesta A+W (cola + write-back) — stubs hasta Integraciones.Aw ---
        // PR7: señal de nudge del worker de ingesta (el endpoint anónimo con
        // API key la dispara; el worker espera señal O timeout de polling).
        services.AddSingleton<Workers.AwSolicitudesTickSignal>();

        // CERRADO por ADR-0048 (PR3/PR5): adapters reales en Integraciones.Aw/Pedidos,
        // registrados en Program.cs vía toggle AwIntegracionDb. Estos stubs quedan
        // como fallback dev sin Hybrid Connection. La pata Planta Pintura del
        // write-back sigue pendiente (<PlantaPinturaOrigenes>).
        services.AddScoped<IAwSolicitudesReader, StubAwSolicitudesReader>();
        services.AddScoped<IAwWriteBackPort, StubAwWriteBackPort>();

        // --- F3-PR2: auto-provisión de master A+W — stub hasta DatosMaestros ---
        // CERRADO por ADR-0048 (PR4): AwMasterProvisioningAdapter (Integraciones.Aw)
        // + ProvisionarClienteDesdeAw/ProvisionarProductoAw (DatosMaestros), vía el
        // mismo toggle. Stub = fallback dev (no auto-crea → bandeja).
        services.AddScoped<IMasterProvisioningPort, NoOpMasterProvisioningPort>();

        // --- F7-PR2: pedimentos del Sistema de Salidas — stub hasta Integraciones.Origenes ---
        // PLATFORM-TODO(<SalidasPedimentos>)
        services.AddScoped<ISalidasPedimentosReader, StubSalidasPedimentosReader>();

        // --- F9: catálogo de Activos Fijos — stub hasta el módulo Activos Fijos ---
        // PLATFORM-TODO(<ActivosFijos>)
        services.AddScoped<IActivosFijosReadPort, NoOpActivosFijosReadPort>();

        // --- F10-PR1: asientos contables — stub hasta el módulo Contabilidad ---
        // PLATFORM-TODO(<ContabilidadAsientos>)
        services.AddScoped<IContabilidadAsientoPort, NoOpContabilidadAsientoPort>();

        // --- F10-PR2: ingesta Planta Pintura — stub hasta Integraciones.Origenes ---
        // PLATFORM-TODO(<PlantaPinturaOrigenes>)
        services.AddScoped<IPlantaPinturaPedidosReader, StubPlantaPinturaPedidosReader>();

        // --- F11: puerto de lectura de CFDIs que Facturación EXPONE (adapter real) ---
        services.AddScoped<IFacturacionCfdiReadPort, Reportes.FacturacionCfdiReadAdapter>();

        // --- CAJAS-PR2: evaluador de alcance de la Capa A (12-cajas.md §9) ---
        // Scoped: consume FacturacionDbContext + ICurrentUserPermissions (Api).
        services.AddScoped<
            Application.Cajas.Alcance.IAlcanceCajaEvaluator,
            Application.Cajas.Alcance.AlcanceCajaEvaluator>();

        // --- CXC-PR3: puerto de saldos de anticipo que Facturación EXPONE a CxC ---
        // (promoción de AnticipoSaldoDetalle a contrato consumible; la interfaz
        // vive en CuentasPorCobrar.Domain.Ports.Facturacion — puerto inverso).
        services.AddScoped<
            Millet.CuentasPorCobrar.Domain.Ports.Facturacion.IFacturacionAnticiposReadPort,
            PublicAdapters.FacturacionAnticiposReadAdapter>();

        return services;
    }
}
