import { describe, expect, it } from 'vitest';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Mirror manual del set de permisos del backend
 * (<c>backend/src/Identidad/Domain/PermisosCanonicos.cs</c>) que el
 * frontend declara en <see cref="PermisosCanonicos"/>. Si el backend
 * agrega/renombra/elimina un código, este test falla y obliga a
 * sincronizar el frontend (o el doc 05 §10.2).
 *
 * <para>Cuando ADR-0017 (codegen TS desde OpenAPI) entre, este test se
 * vuelve redundante y se borra.</para>
 */
const CODIGOS_BACKEND_ESPERADOS = new Set<string>([
  // Infra
  'infra.health.leer',
  'infra.audit_log.leer',
  // Identidad
  'identidad.usuarios.leer',
  'identidad.usuarios.crear',
  'identidad.usuarios.editar',
  'identidad.usuarios.desactivar',
  'identidad.roles.leer',
  'identidad.roles.administrar',
  'identidad.roles.crear',
  'identidad.roles.editar',
  'identidad.roles.eliminar',
  'identidad.roles.asignar-permisos',
  'identidad.roles.grupos-entra-id-gestionar',
  'identidad.permisos.leer',
  'identidad.asignaciones.leer',
  'identidad.asignaciones.administrar',
  // Compras — Requisiciones (kebab-case en autorizar-nivelN: el doc 05
  // §10.2 lo lista con punto, pero el backend usa guion).
  'compras.requisiciones.leer',
  'compras.requisiciones.crear',
  'compras.requisiciones.editar',
  'compras.requisiciones.eliminar',
  'compras.requisiciones.cancelar',
  'compras.requisiciones.cerrar-manual',
  'compras.requisiciones.autorizar-nivel1',
  'compras.requisiciones.autorizar-nivel2',
  'compras.requisiciones.rechazar',
  'compras.requisiciones.editar-de-otros-usuarios',
  'compras.requisiciones.seleccionar-requisitante',
  'compras.requisiciones.ver-todos-departamentos',
  // Compras — Aprobadores
  'compras.aprobadores.administrar',
  // Compras — Órdenes de compra (10 permisos canónicos del submódulo OC,
  // mirror exacto de backend §"Compras — Órdenes de compra"). Mismo
  // criterio de kebab-case que RQ para partes compuestas.
  'compras.ordenes.leer',
  'compras.ordenes.crear',
  'compras.ordenes.crear-sin-rq',
  'compras.ordenes.adjuntar',
  'compras.ordenes.logistica',
  'compras.ordenes.autorizar-nivel1',
  'compras.ordenes.autorizar-nivel2',
  'compras.ordenes.cancelar',
  'compras.ordenes.cancelar-doble',
  'compras.ordenes.reportes-partidas-abiertas',
  // Compras — Configuración (PR-A 2026-05-13, setting AutoGenerarOcAlAutorizar).
  'compras.configuracion.leer',
  'compras.configuracion.editar',
  // Compartido — Catálogos. Tras F-Admin-PR4.5 el frontend mantiene el
  // par leer/administrar canónico aunque la UI vaya migrando a los
  // granulares de Datos Maestros.
  'compartido.catalogos.leer',
  'compartido.catalogos.administrar',
  // Catálogos granulares (F-Admin-PR5.x).
  'catalogos.monedas.gestionar',
  'catalogos.tipos-cambio.gestionar',
  'catalogos.condiciones-pago.gestionar',
  'catalogos.incoterms.gestionar',
  'catalogos.transportistas.gestionar',
  'catalogos.unidades-medida.gestionar',
  // Datos Maestros granulares (F-Admin-PR4.5).
  'datos_maestros.proveedores.gestionar',
  'datos_maestros.articulos.gestionar',
  // Masters auto-provisionables de la ingesta A+W (ADR-0048).
  'datos_maestros.clientes.gestionar',
  'datos_maestros.productos-aw.gestionar',
  // Administración — andamio /admin (F-Admin-PR1.2) y CRUDs (PR2.3+).
  'admin.empresas.leer',
  'admin.empresas.crear',
  'admin.empresas.editar',
  'admin.empresas.desactivar',
  'admin.empresas.sucursales-gestionar',
  'admin.departamentos.gestionar',
  'admin.sucursales.departamentos-gestionar',
  'admin.sucursales.puestos-gestionar',
  'admin.sucursales.usuarios-gestionar',
  'admin.auditoria.leer',
  'admin.series.gestionar',
  'admin.parametros.leer',
  'admin.parametros.editar',
  // Almacén (mirror del backend §"Almacén" — namespace GUID 00000008-*).
  // 30 permisos. Convención kebab-case 3-parte.
  'almacen.almacenes.leer',
  'almacen.almacenes.administrar',
  'almacen.entradas.leer',
  'almacen.entradas.capturar',
  'almacen.entradas.registrar',
  'almacen.entradas.cancelar-borrador',
  'almacen.salidas.leer-propias',
  'almacen.salidas.leer-todas',
  'almacen.salidas.capturar',
  'almacen.salidas.registrar',
  'almacen.salidas.por-vale',
  'almacen.devoluciones-internas.leer',
  'almacen.devoluciones-internas.capturar',
  'almacen.devoluciones-proveedor.iniciar',
  'almacen.devoluciones-proveedor.autorizar',
  'almacen.devoluciones-proveedor.registrar',
  'almacen.inventarios.leer',
  'almacen.inventarios.crear',
  'almacen.inventarios.capturar',
  'almacen.inventarios.aprobar-nivel1',
  'almacen.inventarios.aprobar-nivel2',
  'almacen.inventarios.aprobar-nivel3',
  'almacen.ajustes.manual',
  'almacen.reportes.alfak',
  'almacen.reportes.mp-cnk',
  'almacen.reportes.movimientos',
  'almacen.cierre-mes.ejecutar',
  'almacen.lectura.total',
  'almacen.reservas.administrar',
  'almacen.reorden.leer',
  'almacen.reorden.administrar',
  'almacen.asignaciones.leer',
  'almacen.asignaciones.administrar',
  'almacen.ubicaciones.leer',
  'almacen.ubicaciones.administrar',

  // Cuentas por Pagar (FE-F0+)
  'cuentas_por_pagar.facturas.leer',
  'cuentas_por_pagar.facturas.capturar',
  'cuentas_por_pagar.facturas.editar',
  'cuentas_por_pagar.facturas.cancelar',
  'cuentas_por_pagar.facturas.enviar-revision',
  'cuentas_por_pagar.facturas.liberar-revision',
  'cuentas_por_pagar.facturas.autorizar',
  'cuentas_por_pagar.notas-credito.leer',
  'cuentas_por_pagar.notas-credito.capturar',
  'cuentas_por_pagar.notas-cargo.leer',
  'cuentas_por_pagar.notas-cargo.crear',
  'cuentas_por_pagar.notas-cargo.autorizar',
  'cuentas_por_pagar.notas-cargo.aplicar',
  'cuentas_por_pagar.anticipos.leer',
  'cuentas_por_pagar.anticipos.capturar',
  'cuentas_por_pagar.comprobaciones.leer',
  'cuentas_por_pagar.comprobaciones.capturar',
  'cuentas_por_pagar.comprobaciones.aprobar-nivel1',
  'cuentas_por_pagar.comprobaciones.aprobar-nivel2',
  'cuentas_por_pagar.viaticos.leer',
  'cuentas_por_pagar.viaticos.solicitar',
  'cuentas_por_pagar.viaticos.autorizar-jefe',
  'cuentas_por_pagar.viaticos.autorizar-df',
  'cuentas_por_pagar.viaticos.marcar-pagado',
  'cuentas_por_pagar.viaticos.capturar-comprobacion',
  'cuentas_por_pagar.viaticos.liberar',
  'cuentas_por_pagar.catalogos.aprobadores.administrar',
  'cuentas_por_pagar.catalogos.politicas-viaticos.administrar',
  'cuentas_por_pagar.tc.leer',
  'cuentas_por_pagar.tc.registrar-movimiento',
  'cuentas_por_pagar.tc.cerrar-estado-cuenta',
  'cuentas_por_pagar.tc.administrar',
  'cuentas_por_pagar.tc.disputar',
  'cuentas_por_pagar.proveedores.poner-revision',
  'cuentas_por_pagar.proveedores.liberar-revision',
  'cuentas_por_pagar.proveedores.ajustar-tolerancia',
  'cuentas_por_pagar.reportes.cartera',
  'cuentas_por_pagar.reportes.antiguedad',
  'cuentas_por_pagar.reportes.diot',
  'cuentas_por_pagar.reportes.tc',
  'cuentas_por_pagar.cfdis.leer',
  'cuentas_por_pagar.cfdis.cargar-manual',
  'cuentas_por_pagar.cfdis.descartar',
  'cuentas_por_pagar.reposiciones.leer',
  'cuentas_por_pagar.reposiciones.administrar',
  // Integraciones.Fiscal — admin de PAC + RFCs receptores (PR-2 del
  // módulo Integraciones.Fiscal; namespace GUID 00000006-1xxx en backend).
  'integraciones.fiscal.leer',
  'integraciones.fiscal.administrar',
  // Cuentas por Cobrar (CXC-FE-PR1+). Mirror del backend §"Cuentas por
  // Cobrar". 8 permisos seedeados completos en CXC-PR1.
  'cuentas_por_cobrar.lineas-credito.leer',
  'cuentas_por_cobrar.lineas-credito.gestionar',
  'cuentas_por_cobrar.liberacion.decidir',
  'cuentas_por_cobrar.liberacion.override',
  'cuentas_por_cobrar.cobranza.registrar',
  'cuentas_por_cobrar.cartera.leer',
  'cuentas_por_cobrar.aplicacion-pago.proponer',
  'cuentas_por_cobrar.aplicacion-pago.confirmar',
  // Tesorería / Bancos (TES-FE-PR1). Mirror del backend §"Tesorería"
  // (namespace GUID 0000000b-*). 18 seedeados en TES-PR1 + pasivos.ver
  // en TES-PR3 + cuentas.administrar (TES-7 revisada).
  'tesoreria.cuentas.ver',
  'tesoreria.cuentas.administrar',
  'tesoreria.movimientos.ver',
  'tesoreria.movimientos.registrar',
  'tesoreria.movimientos.ver-cuenta-completa',
  'tesoreria.pagos.aplicar',
  'tesoreria.pagos.revertir',
  'tesoreria.pagos-cuenta.registrar',
  'tesoreria.pagos-cuenta.ligar',
  'tesoreria.corridas.crear',
  'tesoreria.corridas.autorizar',
  'tesoreria.corridas.ejecutar',
  'tesoreria.depositos.confirmar',
  'tesoreria.depositos.rechazar',
  'tesoreria.repp.registrar',
  'tesoreria.conciliacion.operar',
  'tesoreria.conciliacion.cerrar',
  'tesoreria.pasivos.solicitar-cancelacion',
  'tesoreria.pasivos.ver',
  'tesoreria.reportes.ver',
  // Facturación — CFDI 4.0 emitido (FE-F0+). Mirror del backend
  // §"Facturación" (namespace GUID 00000009-*). 24 permisos.
  'facturacion.pedidos.importar',
  'facturacion.pedidos.capturar',
  'facturacion.pedidos.excepciones-resolver',
  'facturacion.facturas.emitir',
  'facturacion.facturas.leer',
  'facturacion.facturas.editar-receptor',
  'facturacion.facturas.editar-articulo',
  'facturacion.anticipos.emitir',
  'facturacion.anticipos.vincular',
  'facturacion.anticipos.leer',
  'facturacion.notas-credito.bonificacion-emitir',
  'facturacion.notas-credito.leer',
  'facturacion.carta-porte.emitir',
  'facturacion.carta-porte.leer',
  'facturacion.repp.emitir',
  'facturacion.cancelaciones.solicitar',
  'facturacion.cancelaciones.consultar',
  // 01-G: reintento (#530) y descarte (#534) de comprobantes fallidos.
  'facturacion.comprobantes.reintentar-timbrado',
  'facturacion.comprobantes.descartar',
  'facturacion.activos.autorizar',
  'facturacion.caja.liquidar',
  'facturacion.caja.administrar',
  'facturacion.caja.operar',
  'facturacion.caja.supervisar',
  'facturacion.caja.leer-todas',
  'facturacion.reportes.leer',
  // Centros de Costo (CECO-FE-PR1; dim3.leer-todos post-renombre CECO-PR4)
  'centros_costo.catalogo.leer',
  'centros_costo.catalogo.administrar',
  'centros_costo.asignaciones.administrar',
  'centros_costo.dim3.leer-todos',
  // Administración — Puestos/Empleados. El #629 (ADM-FE-PR1) los agregó a
  // PermisosCanonicos pero olvidó este set → el test quedó rojo en main
  // (CI no corre vitest de FE, así que nadie lo vio). Verificados contra
  // PermisosCanonicos.cs: 00000005-0007/0008.
  'admin.puestos.gestionar',
  'admin.empleados.gestionar',
]);

describe('PermisosCanonicos — coincidencia con backend', () => {
  const codigos = Object.values(PermisosCanonicos);

  it('cada código del frontend existe en el set esperado del backend', () => {
    for (const code of codigos) {
      expect(CODIGOS_BACKEND_ESPERADOS).toContain(code);
    }
  });

  it('cada código esperado del backend está declarado en el frontend', () => {
    for (const expected of CODIGOS_BACKEND_ESPERADOS) {
      expect(codigos).toContain(expected);
    }
  });

  it('el módulo Compras aporta exactamente 26 permisos (12 de RQ + 1 de aprobadores + 10 de OC + 2 de configuración + 1 de catálogos.leer)', () => {
    const delModulo = codigos.filter(
      (c) => c.startsWith('compras.') || c === 'compartido.catalogos.leer',
    );
    expect(delModulo).toHaveLength(26);
  });

  it('formato modulo.recurso.accion (entre 3 y 4 segmentos)', () => {
    for (const code of codigos) {
      const segmentos = code.split('.');
      expect(segmentos.length).toBeGreaterThanOrEqual(3);
      expect(segmentos.length).toBeLessThanOrEqual(4);
      // Sin mayúsculas (todo lowercase + guiones internos).
      expect(code).toBe(code.toLowerCase());
    }
  });

  it('no duplica códigos en el manifiesto', () => {
    const set = new Set(codigos);
    expect(set.size).toBe(codigos.length);
  });
});
