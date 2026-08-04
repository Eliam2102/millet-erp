using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Ports.Externos;
using Millet.Almacen.Domain.Ports.Notificaciones;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.Almacen.Infrastructure.Stubs;

namespace Millet.Almacen.Infrastructure;

/// <summary>
/// Wiring DI del módulo Almacén (F0-PR1). El registro del
/// <c>AlmacenDbContext</c> + los interceptors transversales se hace en
/// <c>Program.cs</c> (sigue el patrón de Compras / Integraciones.Aw:
/// cada módulo expone sus servicios pero el DbContext se registra cerca
/// del connection string).
///
/// <para>
/// F0-PR1 sólo wirea los stubs <c>NoOp*</c> de los 9 puertos cross-module
/// listados en el 01-diseno §6.1. Los adapters reales reemplazan estos
/// stubs en sus fases respectivas (F1+). Cada stub lleva un
/// <c>PLATFORM-TODO</c> buscable.
/// </para>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddAlmacenModule(this IServiceCollection services)
    {
        // PLATFORM-TODO(<AlmacenCrossModulePorts>): reemplazar cada
        // NoOp* por su adapter real cuando entre la fase correspondiente.
        services.AddScoped<IComprasOcReadPort, NoOpComprasOcReadPort>();
        services.AddScoped<IComprasRequisicionReadPort, NoOpComprasRequisicionReadPort>();
        services.AddScoped<IComprasPedidoVivoReadPort, NoOpComprasPedidoVivoReadPort>();
        services.AddScoped<IUsuarioServicioReadPort, NoOpUsuarioServicioReadPort>();
        services.AddScoped<IComprasCrearRqSistemaPort, NoOpComprasCrearRqSistemaPort>();
        services.AddScoped<IArticuloReadPort, NoOpArticuloReadPort>();
        services.AddScoped<IProveedorReadPort, NoOpProveedorReadPort>();
        services.AddScoped<ICxpDocumentosReadPort, NoOpCxpDocumentosReadPort>();
        services.AddScoped<ISucursalReadPort, NoOpSucursalReadPort>();
        // IEmpleadoReadPort se registra en Program.cs con el adapter real
        // de Compartido (EmpleadoReadAdapter, ADM-PR2) — Almacén no puede
        // referenciar Compartido.Infrastructure sin acoplar el módulo.
        services.AddScoped<IUsuarioReadPort, NoOpUsuarioReadPort>();
        services.AddScoped<ITipoCambioReadPort, NoOpTipoCambioReadPort>();
        services.AddScoped<IConceptoContableReadPort, NoOpConceptoContableReadPort>();
        // Fase E PR5: display del CC-Máquina en salidas. NoOp por defecto; el
        // adapter real (delega en CentrosCosto.IDim3ReadPort) se cablea en Program.cs.
        services.AddScoped<ICentroCostoReadPort, NoOpCentroCostoReadPort>();
        services.AddScoped<IPeriodoContableReadPort, NoOpPeriodoContableReadPort>();

        // F2-PR2: Open Host Service público (Compras/Requisiciones lo consumen
        // para validar stock + reemplazar el stub InMemoryConsultarStockPort).
        services.AddScoped<IAlmacenSaldoQueryPort, AlmacenSaldoQueryAdapter>();

        // F5-PR1: notificaciones (NoOp stub hasta que el módulo Notificaciones
        // exista — PLATFORM-TODO(<NotificacionService>)).
        services.AddScoped<INotificacionService, NoOpNotificacionService>();

        return services;
    }
}
