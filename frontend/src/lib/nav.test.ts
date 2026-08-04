import { describe, expect, it } from 'vitest';
import { Inbox } from 'lucide-react';
import {
  filtrarModuloPorPermisos,
  navSidebarItems,
  type NavModulo,
} from '@/lib/nav';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

describe('filtrarModuloPorPermisos', () => {
  const moduloDePrueba: NavModulo = {
    moduloId: 'test',
    label: 'Test',
    icon: Inbox,
    secciones: [
      {
        label: 'Operación',
        cards: [
          {
            label: 'Pantalla A',
            description: 'Requiere leer',
            to: '/test/a',
            icon: Inbox,
            permission: 'modulo.leer',
          },
          {
            label: 'Pantalla B',
            description: 'Requiere autorizar',
            to: '/test/b',
            icon: Inbox,
            permission: 'modulo.autorizar',
          },
          {
            label: 'Pantalla C',
            description: 'Cualquiera de admin/auditor',
            to: '/test/c',
            icon: Inbox,
            permissionsAny: ['modulo.admin', 'modulo.auditor'],
          },
        ],
      },
      {
        label: 'Configuración',
        cards: [
          {
            label: 'Catálogos',
            description: 'Solo admin',
            to: '/test/admin',
            icon: Inbox,
            permission: 'modulo.admin',
          },
        ],
      },
    ],
  };

  it('sin permisos: descarta todas las cards y secciones', () => {
    const r = filtrarModuloPorPermisos(moduloDePrueba, []);
    expect(r.secciones).toHaveLength(0);
  });

  it('un permiso: deja solo cards que matcheen', () => {
    const r = filtrarModuloPorPermisos(moduloDePrueba, ['modulo.leer']);
    expect(r.secciones).toHaveLength(1);
    expect(r.secciones[0].label).toBe('Operación');
    expect(r.secciones[0].cards).toHaveLength(1);
    expect(r.secciones[0].cards[0].label).toBe('Pantalla A');
  });

  it('permissionsAny: visible si tiene CUALQUIERA de los listados', () => {
    const r = filtrarModuloPorPermisos(moduloDePrueba, ['modulo.auditor']);
    expect(r.secciones).toHaveLength(1);
    expect(r.secciones[0].cards).toHaveLength(1);
    expect(r.secciones[0].cards[0].label).toBe('Pantalla C');
  });

  it('todos los permisos: todas las cards visibles', () => {
    const r = filtrarModuloPorPermisos(moduloDePrueba, [
      'modulo.leer',
      'modulo.autorizar',
      'modulo.admin',
      'modulo.auditor',
    ]);
    expect(r.secciones).toHaveLength(2);
    expect(r.secciones[0].cards).toHaveLength(3); // Operación
    expect(r.secciones[1].cards).toHaveLength(1); // Configuración
  });

  it('sección queda vacía → se omite (no aparece "Configuración" sin cards)', () => {
    const r = filtrarModuloPorPermisos(moduloDePrueba, ['modulo.leer']);
    expect(r.secciones.map((s) => s.label)).toEqual(['Operación']);
  });

  it('no muta el módulo original', () => {
    const original = JSON.parse(JSON.stringify(moduloDePrueba));
    filtrarModuloPorPermisos(moduloDePrueba, ['modulo.leer']);
    expect(JSON.parse(JSON.stringify(moduloDePrueba))).toEqual(original);
  });
});

describe('navSidebarItems', () => {
  it('arranca con Inicio (link), seguido de los 10 módulos del back-office', () => {
    expect(navSidebarItems[0]).toMatchObject({ kind: 'link', label: 'Inicio' });
    const modulos = navSidebarItems.filter((i) => i.kind === 'modulo');
    expect(modulos.length).toBe(10);
    expect(modulos.map((m) => m.moduloId)).toEqual([
      'facturacion',
      'cxc',
      'compras',
      'almacen',
      'cxp',
      'tesoreria',
      'centros-costo',
      'activos',
      'contabilidad',
      'reportes',
    ]);
  });

  it('Facturación, CxC, Compras, Almacén, CxP, Tesorería y Centros de Costo son los módulos NO disabled (CeCo llegó con CECO-FE-PR1)', () => {
    const modulos = navSidebarItems.filter((i) => i.kind === 'modulo');
    const habilitados = modulos.filter((m) => !m.disabled);
    expect(habilitados.map((m) => m.moduloId)).toEqual([
      'facturacion',
      'cxc',
      'compras',
      'almacen',
      'cxp',
      'tesoreria',
      'centros-costo',
    ]);
  });

  it('CxC tiene Operación (líneas/liberaciones/cobranza/aplicaciones) y Reportes, gateadas por cuentas_por_cobrar.*', () => {
    const cxc = navSidebarItems.find(
      (i) => i.kind === 'modulo' && i.moduloId === 'cxc',
    );
    expect(cxc).toBeDefined();
    if (cxc?.kind !== 'modulo') throw new Error('cxc no es módulo');
    expect(cxc.disabled).toBeFalsy();
    expect(cxc.secciones.map((s) => s.label)).toEqual([
      'Operación',
      'Reportes',
    ]);
    const operacion = cxc.secciones[0].cards.map((c) => c.to);
    expect(operacion).toEqual([
      '/cxc/lineas-credito',
      '/cxc/liberaciones',
      '/cxc/cobranza',
      '/cxc/anticipos',
      '/cxc/aplicaciones',
      '/cxc/alertas',
    ]);
    const lineas = cxc.secciones[0].cards.find(
      (c) => c.to === '/cxc/lineas-credito',
    );
    expect(lineas?.permission).toBe(
      PermisosCanonicos.CuentasPorCobrarLineasCreditoLeer,
    );
    const liberaciones = cxc.secciones[0].cards.find(
      (c) => c.to === '/cxc/liberaciones',
    );
    expect(liberaciones?.permissionsAny).toEqual([
      PermisosCanonicos.CuentasPorCobrarLiberacionDecidir,
      PermisosCanonicos.CuentasPorCobrarLiberacionOverride,
    ]);
    const reportes = cxc.secciones[1].cards.map((c) => c.to);
    expect(reportes).toEqual(['/cxc/cartera', '/cxc/estado-cuenta']);
  });

  it('Facturación tiene Operación (pedidos/facturas/anticipos/carta-porte), Configuración (cajas) y Reportes', () => {
    const facturacion = navSidebarItems.find(
      (i) => i.kind === 'modulo' && i.moduloId === 'facturacion',
    );
    expect(facturacion).toBeDefined();
    if (facturacion?.kind !== 'modulo')
      throw new Error('facturacion no es módulo');
    expect(facturacion.disabled).toBeFalsy();
    // Configuración (Cajas) llegó en CAJAS-PR5 (#514); este assert quedó
    // desactualizado y pasó desapercibido (el CI no corre tests frontend).
    expect(facturacion.secciones.map((s) => s.label)).toEqual([
      'Operación',
      'Configuración',
      'Reportes',
    ]);
    const operacion = facturacion.secciones[0].cards.map((c) => c.to);
    expect(operacion).toContain('/facturacion/pedidos');
    expect(operacion).toContain('/facturacion/facturas');
    expect(operacion).toContain('/facturacion/anticipos');
    expect(operacion).toContain('/facturacion/carta-porte');
    const facturas = facturacion.secciones[0].cards.find(
      (c) => c.to === '/facturacion/facturas',
    );
    expect(facturas?.permission).toBe(
      PermisosCanonicos.FacturacionFacturasLeer,
    );
  });

  it('Compras tiene Operación (P1+P2) y Configuración (Aprobadores)', () => {
    const compras = navSidebarItems.find(
      (i) => i.kind === 'modulo' && i.moduloId === 'compras',
    );
    expect(compras).toBeDefined();
    if (compras?.kind !== 'modulo') throw new Error('compras no es módulo');
    expect(compras.secciones).toHaveLength(2);
    expect(compras.secciones.map((s) => s.label)).toEqual([
      'Operación',
      'Configuración',
    ]);
    const operacionLabels = compras.secciones[0].cards.map((c) => c.label);
    expect(operacionLabels).toContain('Mis requisiciones');
    expect(operacionLabels).toContain('Pendientes de autorización');
    expect(operacionLabels).toContain('Órdenes de compra');
    const configLabels = compras.secciones[1].cards.map((c) => c.label);
    expect(configLabels).toContain('Aprobadores');
  });

  it('Almacén / Configuración: Sub-almacenes pegada a Almacenes y antes de Ubicaciones', () => {
    const almacen = navSidebarItems.find(
      (i) => i.kind === 'modulo' && i.moduloId === 'almacen',
    );
    if (almacen?.kind !== 'modulo') throw new Error('almacen no es módulo');
    const config = almacen.secciones.find((s) => s.label === 'Configuración');
    expect(config).toBeDefined();
    // Secuencia jerárquica: almacén → sub-almacén → … → rack (ubicaciones).
    expect(config!.cards.map((c) => c.to)).toEqual([
      '/almacen/almacenes',
      '/almacen/sub-almacenes',
      '/almacen/reorden',
      '/almacen/asignaciones',
      '/almacen/ubicaciones',
    ]);
    // "Almacenes" ya sin el sufijo "y sub-almacenes" (redundante desde que
    // Sub-almacenes tiene card propia).
    expect(config!.cards.map((c) => c.label)).toEqual([
      'Almacenes',
      'Sub-almacenes',
      'Reabasto',
      'Ubicación de artículos',
      'Ubicaciones',
    ]);
    const subAlmacenes = config!.cards.find(
      (c) => c.to === '/almacen/sub-almacenes',
    );
    // Mismo gate que su ruta (beforeLoad de sub-almacenes.tsx).
    expect(subAlmacenes?.permission).toBe(
      PermisosCanonicos.AlmacenAlmacenesRead,
    );
  });

  it('Órdenes de compra está gateada por compras.ordenes.leer (UF0-PR1)', () => {
    const compras = navSidebarItems.find(
      (i) => i.kind === 'modulo' && i.moduloId === 'compras',
    );
    if (compras?.kind !== 'modulo') throw new Error('compras no es módulo');
    const ordenes = compras.secciones[0].cards.find(
      (c) => c.to === '/compras/ordenes',
    );
    expect(ordenes).toBeDefined();
    expect(ordenes?.permission).toBe(PermisosCanonicos.ComprasOrdenesLeer);
  });

  it('Pendientes está gateada por permissionsAny (autorizar-N1 OR autorizar-N2)', () => {
    const compras = navSidebarItems.find(
      (i) => i.kind === 'modulo' && i.moduloId === 'compras',
    );
    if (compras?.kind !== 'modulo') throw new Error('compras no es módulo');
    const pendientes = compras.secciones[0].cards.find(
      (c) => c.to === '/compras/pendientes',
    );
    expect(pendientes?.permissionsAny).toEqual([
      PermisosCanonicos.ComprasRequisicionesAutorizarNivel1,
      PermisosCanonicos.ComprasRequisicionesAutorizarNivel2,
    ]);
  });
});
