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
        services.AddScoped<Millet.Almacen.Application.PublicPorts.IRecepcionesImportacionReadPort, RecepcionesImportacionReadAdapter>();
        services.AddScoped<Millet.Almacen.Application.PublicPorts.IRecepcionesCostoReadPort, RecepcionesCostoReadAdapter>();
        services.AddScoped<Millet.Almacen.Application.PublicPorts.ICostoMedioReader, CostoMedioReader>();
        // PLATFORM-TODO(<AlmacenCrossModulePorts>): reemplazar cada
        // NoOp* por su adapter real cuando entre la fase correspondiente.
        services.AddScoped<IComprasOcReadPort, NoOpComprasOcReadPort>();
        services.AddScoped<IComprasRequisicionReadPort, NoOpComprasRequisicionReadPort>();
        services.AddScoped<IComprasPedidoVivoReadPort, NoOpComprasPedidoVivoReadPort>();
        services.AddScoped<IUsuarioServicioReadPort, NoOpUsuarioServicioReadPort>();
        services.AddScoped<Millet.Almacen.Application.Recepciones.CancelarRecepcionHandler>(); // también lo usa la cancelación de entrada de la app
        services.AddScoped<SalidaAppAlmacenesGuardado>();
        services.AddScoped<MermaProcesoAppGuardado>();
        services.AddScoped<ConsumoComponentesGuardado>();
        services.AddScoped<DescargaTallerGuardado>();
        services.AddScoped<IComprasCrearRqSistemaPort, NoOpComprasCrearRqSistemaPort>();
        services.AddScoped<IArticuloReadPort, NoOpArticuloReadPort>();
        services.AddScoped<IVarianteVidrioReadPort, NoOpVarianteVidrioReadPort>();
        services.AddScoped<IProveedorReadPort, NoOpProveedorReadPort>();
        services.AddScoped<IAdjuntosEntidadReadPort, NoOpAdjuntosEntidadReadPort>();
        services.AddScoped<ICxpDocumentosReadPort, NoOpCxpDocumentosReadPort>();
        services.AddScoped<ISucursalReadPort, NoOpSucursalReadPort>();
        services.AddScoped<IEmpresaImpresionReadPort, NoOpEmpresaImpresionReadPort>();
        // Estado de capitalización de los activos de una recepción; lo reemplaza AddActivosFijosModule.
        services.AddScoped<IActivoRecepcionReadPort, NoOpActivoRecepcionReadPort>();
        // IEmpleadoReadPort se registra en Program.cs con el adapter real
        // de Compartido (EmpleadoReadAdapter, ADM-PR2) — Almacén no puede
        // referenciar Compartido.Infrastructure sin acoplar el módulo.
        services.AddScoped<IUsuarioReadPort, NoOpUsuarioReadPort>();
        // ITipoCambioReadPort se registra en Program.cs con el adapter real de
        // Compartido (TipoCambioReadAdapter, C1.2), igual que IEmpleadoReadPort.
        services.AddScoped<IConceptoContableReadPort, NoOpConceptoContableReadPort>();
        // Fase E PR5: display del CC-Máquina en salidas. NoOp por defecto; el
        // adapter real (delega en CentrosCosto.IDim3ReadPort) se cablea en Program.cs.
        services.AddScoped<ICentroCostoReadPort, NoOpCentroCostoReadPort>();
        services.AddScoped<IPeriodoContableReadPort, PeriodoContableReadAdapter>();
        services.AddScoped<IInventarioContableReadPort, InventarioContableReadAdapter>();

        // F2-PR2: Open Host Service público (Compras/Requisiciones lo consumen
        // para validar stock + reemplazar el stub InMemoryConsultarStockPort).
        services.AddScoped<IAlmacenSaldoQueryPort, AlmacenSaldoQueryAdapter>();

        // F5-PR1 / 86e3txv0n: avisos por el transporte único de correo; los
        // destinatarios salen de Almacen:Avisos (se enlaza en Program.cs).
        services.AddScoped<Notificaciones.CorreoNotificacionService>();
        services.AddScoped<Notificaciones.AvisosInternosNotificacionService>();
        services.AddScoped<INotificacionService>(sp => sp.GetRequiredService<Notificaciones.AvisosInternosNotificacionService>());
        // Sin adapter de Identidad (pruebas de módulo) nadie recibe avisos; Program.cs registra el real.
        services.AddScoped<IDestinatariosAvisoReadPort, Stubs.NoOpDestinatariosAvisoReadPort>();

        return services;
    }
}
