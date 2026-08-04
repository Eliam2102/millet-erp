import { describe, expect, it } from 'vitest';
import {
  accionAdjuntarDocumento,
  accionAgregarLineaManual,
  accionAprobarNivel1,
  accionAprobarNivel2,
  accionCancelar1Firma,
  accionCancelarDobleFirma,
  accionDescargarPdf,
  accionDuplicarOc,
  accionEditarCabecera,
  accionEditarInfoImportacion,
  accionEditarInfoLogistica,
  accionEditarLineaEstructural,
  accionEditarNumeroPedimentoImportacion,
  accionEditarTextoAdicionalLinea,
  accionEliminarLinea,
  accionRechazar,
  accionRemoverAdjunto,
  accionTransmitirAAutorizacion,
  accionVerArbolDocumentos,
  accionVerDetalleReadOnly,
  ACCIONES_OC,
  type AccionFn,
} from '@/features/compras/ordenes/lib/acciones-disponibles';
import {
  EstadoOrdenCompra,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
  type OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Tests parametrizados de la matriz §6.1 del doc 05 OC. Cada test
 * recorre los 7 estados × cada acción × variantes de permiso /
 * sub-estado / flag relevante. Si la matriz cambia, este archivo y
 * <c>acciones-disponibles.ts</c> se actualizan en el mismo PR.
 *
 * <para>Convención: el test name documenta el outcome esperado en
 * lenguaje humano (✅ visible+habilitada, ❌ no visible, ⚪ visible
 * deshabilitada). Si un test falla, el name dice qué se rompió.</para>
 */

const PERMISOS_TODOS: readonly string[] = Object.values(PermisosCanonicos);

const ESTADOS_TODOS = [
  EstadoOrdenCompra.Borrador,
  EstadoOrdenCompra.EnAutorizacionJefeCompras,
  EstadoOrdenCompra.EnAutorizacionDireccion,
  EstadoOrdenCompra.Autorizada,
  EstadoOrdenCompra.Cerrada,
  EstadoOrdenCompra.Cancelada,
  EstadoOrdenCompra.Rechazada,
] as const;

function makeOc(
  overrides: Partial<OrdenCompraDetalleResponse> = {},
): OrdenCompraDetalleResponse {
  return {
    id: 'oc-1',
    empresaId: 'e-1',
    folio: 'OC-MID2026-000001',
    folioAnio: 2026,
    proveedorId: 'p-1',
    sucursalDestinoId: 's-1',
    condicionesPagoId: 'cp-1',
    usoPrincipalId: 'up-1',
    moneda: 'MXN',
    tipoCambio: null,
    compradorTitularId: 'u-1',
    encargadoComprasId: 'u-1',
    observaciones: null,
    sinRequisicionPrevia: false,
    esImportacion: false,
    cotizacionExcepcionada: false,
    fechaDocumento: '2026-05-12',
    fechaContabilizacion: null,
    fechaEntregaEsperada: null,
    estado: EstadoOrdenCompra.Borrador,
    subEstadoRecepcion: SubEstadoRecepcion.SinRecepcion,
    subEstadoFacturacion: SubEstadoFacturacion.SinFactura,
    subEstadoPago: SubEstadoPago.SinPago,
    motivoSinRequisicion: null,
    motivoCancelacion: null,
    motivoRechazoId: null,
    motivoRechazoTexto: null,
    ocOrigenId: null,
    version: 1,
    createdAt: '2026-05-12T00:00:00Z',
    updatedAt: '2026-05-12T00:00:00Z',
    lineas: [],
    ...overrides,
  };
}

// ============================================================================
// 1. Sin ningún permiso → todas las 21 acciones devuelven OCULTO
// ============================================================================

describe('acciones-disponibles — gate de permisos', () => {
  it('sin ningún permiso: TODAS las acciones devuelven { visible: false }', () => {
    for (const accion of ACCIONES_OC) {
      for (const estado of ESTADOS_TODOS) {
        const oc = makeOc({ estado });
        const result = accion.fn(oc, []);
        expect(
          result.visible,
          `${accion.id} en ${EstadoOrdenCompra[estado] ?? estado}: esperaba visible=false sin permisos`,
        ).toBe(false);
      }
    }
  });
});

// ============================================================================
// 2. Recorrido por acción × estado de la matriz §6.1
// ============================================================================

interface CeldaEsperada {
  estado: number;
  /** undefined = solo verificar visible/no-visible (ignorar habilitada). */
  visible: boolean;
  habilitada?: boolean;
}

function expectarCeldas(
  accion: AccionFn,
  permisos: readonly string[],
  celdas: ReadonlyArray<CeldaEsperada>,
  ocOverride?: Partial<OrdenCompraDetalleResponse>,
) {
  for (const c of celdas) {
    const oc = makeOc({ estado: c.estado, ...ocOverride });
    const result = accion(oc, permisos);
    expect(
      result.visible,
      `Estado ${c.estado}: visible esperaba ${c.visible}`,
    ).toBe(c.visible);
    if (c.habilitada !== undefined) {
      expect(
        result.habilitada,
        `Estado ${c.estado}: habilitada esperaba ${c.habilitada}`,
      ).toBe(c.habilitada);
    }
  }
}

describe('Bloque 1 — cabecera + líneas', () => {
  const PERMS_CREAR: readonly string[] = [
    PermisosCanonicos.ComprasOrdenesCrear,
  ];

  it('Editar cabecera: ✅ Borrador, Rechazada · ❌ resto', () => {
    expectarCeldas(accionEditarCabecera, PERMS_CREAR, [
      { estado: EstadoOrdenCompra.Borrador, visible: true, habilitada: true },
      { estado: EstadoOrdenCompra.EnAutorizacionJefeCompras, visible: false },
      { estado: EstadoOrdenCompra.EnAutorizacionDireccion, visible: false },
      { estado: EstadoOrdenCompra.Autorizada, visible: false },
      { estado: EstadoOrdenCompra.Cerrada, visible: false },
      { estado: EstadoOrdenCompra.Cancelada, visible: false },
      { estado: EstadoOrdenCompra.Rechazada, visible: true, habilitada: true },
    ]);
  });

  it('Agregar línea (manual): ✅ Borrador/Rechazada SOLO si SinRq=true', () => {
    expectarCeldas(
      accionAgregarLineaManual,
      PERMS_CREAR,
      [
        { estado: EstadoOrdenCompra.Borrador, visible: true, habilitada: true },
        { estado: EstadoOrdenCompra.Rechazada, visible: true, habilitada: true },
      ],
      { sinRequisicionPrevia: true },
    );
    // Con SinRq=false en Borrador: oculto.
    expectarCeldas(
      accionAgregarLineaManual,
      PERMS_CREAR,
      [{ estado: EstadoOrdenCompra.Borrador, visible: false }],
      { sinRequisicionPrevia: false },
    );
  });

  it('Editar línea (estructural): ✅ Borrador, Rechazada', () => {
    expectarCeldas(accionEditarLineaEstructural, PERMS_CREAR, [
      { estado: EstadoOrdenCompra.Borrador, visible: true, habilitada: true },
      { estado: EstadoOrdenCompra.Autorizada, visible: false },
      { estado: EstadoOrdenCompra.Rechazada, visible: true, habilitada: true },
    ]);
  });

  it('Eliminar línea: ✅ Borrador, Rechazada', () => {
    expectarCeldas(accionEliminarLinea, PERMS_CREAR, [
      { estado: EstadoOrdenCompra.Borrador, visible: true, habilitada: true },
      { estado: EstadoOrdenCompra.Cerrada, visible: false },
      { estado: EstadoOrdenCompra.Rechazada, visible: true, habilitada: true },
    ]);
  });

  it('Editar texto_adicional: ✅ todo no-terminal · ❌ Cerrada/Cancelada', () => {
    expectarCeldas(accionEditarTextoAdicionalLinea, PERMS_CREAR, [
      { estado: EstadoOrdenCompra.Borrador, visible: true, habilitada: true },
      {
        estado: EstadoOrdenCompra.EnAutorizacionJefeCompras,
        visible: true,
        habilitada: true,
      },
      {
        estado: EstadoOrdenCompra.EnAutorizacionDireccion,
        visible: true,
        habilitada: true,
      },
      { estado: EstadoOrdenCompra.Autorizada, visible: true, habilitada: true },
      { estado: EstadoOrdenCompra.Cerrada, visible: false },
      { estado: EstadoOrdenCompra.Cancelada, visible: false },
      { estado: EstadoOrdenCompra.Rechazada, visible: true, habilitada: true },
    ]);
  });
});

describe('Bloque 2 — adjuntos', () => {
  it('Adjuntar documento: ✅ todo no-terminal con permiso `adjuntar`', () => {
    const perms = [PermisosCanonicos.ComprasOrdenesAdjuntar];
    expectarCeldas(accionAdjuntarDocumento, perms, [
      { estado: EstadoOrdenCompra.Borrador, visible: true, habilitada: true },
      {
        estado: EstadoOrdenCompra.EnAutorizacionJefeCompras,
        visible: true,
        habilitada: true,
      },
      { estado: EstadoOrdenCompra.Autorizada, visible: true, habilitada: true },
      { estado: EstadoOrdenCompra.Cerrada, visible: false },
      { estado: EstadoOrdenCompra.Cancelada, visible: false },
      { estado: EstadoOrdenCompra.Rechazada, visible: true, habilitada: true },
    ]);
  });

  it('Adjuntar documento: ❌ con `crear` pero SIN `adjuntar`', () => {
    expectarCeldas(
      accionAdjuntarDocumento,
      [PermisosCanonicos.ComprasOrdenesCrear],
      [{ estado: EstadoOrdenCompra.Borrador, visible: false }],
    );
  });

  it('Remover adjunto: ✅ Borrador, Rechazada · gateado por `crear`', () => {
    expectarCeldas(
      accionRemoverAdjunto,
      [PermisosCanonicos.ComprasOrdenesCrear],
      [
        { estado: EstadoOrdenCompra.Borrador, visible: true, habilitada: true },
        { estado: EstadoOrdenCompra.Autorizada, visible: false },
        { estado: EstadoOrdenCompra.Rechazada, visible: true, habilitada: true },
      ],
    );
  });
});

describe('Bloque 3 — info logística e importación', () => {
  it('Editar info logística: ✅ todo no-terminal · gateado por `crear` O `logistica`', () => {
    expectarCeldas(
      accionEditarInfoLogistica,
      [PermisosCanonicos.ComprasOrdenesLogistica],
      [
        { estado: EstadoOrdenCompra.Borrador, visible: true, habilitada: true },
        { estado: EstadoOrdenCompra.Autorizada, visible: true, habilitada: true },
        { estado: EstadoOrdenCompra.Cerrada, visible: false },
      ],
    );
    // El permiso `crear` también lo abre.
    expectarCeldas(
      accionEditarInfoLogistica,
      [PermisosCanonicos.ComprasOrdenesCrear],
      [
        { estado: EstadoOrdenCompra.Autorizada, visible: true, habilitada: true },
      ],
    );
  });

  it('Editar info importación: ❌ si OC NO es importación', () => {
    expectarCeldas(
      accionEditarInfoImportacion,
      [PermisosCanonicos.ComprasOrdenesLogistica],
      [{ estado: EstadoOrdenCompra.Borrador, visible: false }],
      { esImportacion: false },
    );
  });

  it('Editar info importación (campos completos): ✅ Borrador/Rechazada SI esImportacion=true', () => {
    expectarCeldas(
      accionEditarInfoImportacion,
      [PermisosCanonicos.ComprasOrdenesLogistica],
      [
        { estado: EstadoOrdenCompra.Borrador, visible: true, habilitada: true },
        { estado: EstadoOrdenCompra.EnAutorizacionJefeCompras, visible: false },
        { estado: EstadoOrdenCompra.Autorizada, visible: false },
        { estado: EstadoOrdenCompra.Rechazada, visible: true, habilitada: true },
      ],
      { esImportacion: true },
    );
  });

  it('Editar NumeroPedimento: ✅ todo no-terminal SI esImportacion=true (sin re-auth)', () => {
    expectarCeldas(
      accionEditarNumeroPedimentoImportacion,
      [PermisosCanonicos.ComprasOrdenesLogistica],
      [
        { estado: EstadoOrdenCompra.Borrador, visible: true, habilitada: true },
        { estado: EstadoOrdenCompra.Autorizada, visible: true, habilitada: true },
        { estado: EstadoOrdenCompra.Cerrada, visible: false },
      ],
      { esImportacion: true },
    );
  });
});

describe('Bloque 4 — workflow autorización', () => {
  it('Transmitir: ✅ Borrador, Rechazada · ❌ resto', () => {
    expectarCeldas(
      accionTransmitirAAutorizacion,
      [PermisosCanonicos.ComprasOrdenesCrear],
      [
        { estado: EstadoOrdenCompra.Borrador, visible: true, habilitada: true },
        { estado: EstadoOrdenCompra.EnAutorizacionJefeCompras, visible: false },
        { estado: EstadoOrdenCompra.Autorizada, visible: false },
        { estado: EstadoOrdenCompra.Rechazada, visible: true, habilitada: true },
      ],
    );
  });

  it('Aprobar N1: ✅ SOLO en EnAutorizacionJefeCompras', () => {
    expectarCeldas(
      accionAprobarNivel1,
      [PermisosCanonicos.ComprasOrdenesAutorizarNivel1],
      [
        { estado: EstadoOrdenCompra.Borrador, visible: false },
        {
          estado: EstadoOrdenCompra.EnAutorizacionJefeCompras,
          visible: true,
          habilitada: true,
        },
        { estado: EstadoOrdenCompra.EnAutorizacionDireccion, visible: false },
        { estado: EstadoOrdenCompra.Autorizada, visible: false },
      ],
    );
  });

  it('Aprobar N2: ✅ SOLO en EnAutorizacionDireccion (NUNCA antes)', () => {
    expectarCeldas(
      accionAprobarNivel2,
      [PermisosCanonicos.ComprasOrdenesAutorizarNivel2],
      [
        { estado: EstadoOrdenCompra.Borrador, visible: false },
        { estado: EstadoOrdenCompra.EnAutorizacionJefeCompras, visible: false },
        {
          estado: EstadoOrdenCompra.EnAutorizacionDireccion,
          visible: true,
          habilitada: true,
        },
        { estado: EstadoOrdenCompra.Autorizada, visible: false },
      ],
    );
  });

  it('Rechazar: ✅ EnAutorizacion* (cualquiera de los 2 niveles)', () => {
    expectarCeldas(
      accionRechazar,
      [PermisosCanonicos.ComprasOrdenesAutorizarNivel1],
      [
        { estado: EstadoOrdenCompra.Borrador, visible: false },
        {
          estado: EstadoOrdenCompra.EnAutorizacionJefeCompras,
          visible: true,
          habilitada: true,
        },
        {
          estado: EstadoOrdenCompra.EnAutorizacionDireccion,
          visible: true,
          habilitada: true,
        },
        { estado: EstadoOrdenCompra.Autorizada, visible: false },
      ],
    );
  });
});

describe('Bloque 5 — cancelación + duplicación', () => {
  it('Cancelar 1 firma: ✅ pre-aut + Autorizada SIN recepciones', () => {
    expectarCeldas(
      accionCancelar1Firma,
      [PermisosCanonicos.ComprasOrdenesCancelar],
      [
        { estado: EstadoOrdenCompra.Borrador, visible: true, habilitada: true },
        {
          estado: EstadoOrdenCompra.EnAutorizacionJefeCompras,
          visible: true,
          habilitada: true,
        },
        { estado: EstadoOrdenCompra.Autorizada, visible: true, habilitada: true },
        { estado: EstadoOrdenCompra.Cerrada, visible: false },
        { estado: EstadoOrdenCompra.Cancelada, visible: false },
        { estado: EstadoOrdenCompra.Rechazada, visible: true, habilitada: true },
      ],
      { subEstadoRecepcion: SubEstadoRecepcion.SinRecepcion },
    );
  });

  it('Cancelar 1 firma: ❌ Autorizada CON recepciones (exige doble firma)', () => {
    expectarCeldas(
      accionCancelar1Firma,
      [PermisosCanonicos.ComprasOrdenesCancelar],
      [{ estado: EstadoOrdenCompra.Autorizada, visible: false }],
      { subEstadoRecepcion: SubEstadoRecepcion.Parcial },
    );
  });

  it('Cancelar doble firma: ✅ SOLO Autorizada CON recepciones', () => {
    expectarCeldas(
      accionCancelarDobleFirma,
      [PermisosCanonicos.ComprasOrdenesCancelarDoble],
      [{ estado: EstadoOrdenCompra.Autorizada, visible: true, habilitada: true }],
      { subEstadoRecepcion: SubEstadoRecepcion.Completa },
    );
    // Sin recepciones → oculto (cancelar 1 firma cubre).
    expectarCeldas(
      accionCancelarDobleFirma,
      [PermisosCanonicos.ComprasOrdenesCancelarDoble],
      [{ estado: EstadoOrdenCompra.Autorizada, visible: false }],
      { subEstadoRecepcion: SubEstadoRecepcion.SinRecepcion },
    );
  });

  it('Duplicar OC: ✅ SOLO Cancelada, Rechazada (terminales destructivos)', () => {
    expectarCeldas(
      accionDuplicarOc,
      [PermisosCanonicos.ComprasOrdenesCrear],
      [
        { estado: EstadoOrdenCompra.Borrador, visible: false },
        { estado: EstadoOrdenCompra.Autorizada, visible: false },
        { estado: EstadoOrdenCompra.Cerrada, visible: false },
        { estado: EstadoOrdenCompra.Cancelada, visible: true, habilitada: true },
        { estado: EstadoOrdenCompra.Rechazada, visible: true, habilitada: true },
      ],
    );
  });
});

describe('Bloque 6 — read-only / utilities', () => {
  const PERMS_LEER = [PermisosCanonicos.ComprasOrdenesLeer];

  it('Descargar PDF: ✅ Autorizada, Cerrada · ⚪ Cancelada (histórico) · ❌ resto', () => {
    expectarCeldas(accionDescargarPdf, PERMS_LEER, [
      { estado: EstadoOrdenCompra.Borrador, visible: false },
      { estado: EstadoOrdenCompra.EnAutorizacionJefeCompras, visible: false },
      { estado: EstadoOrdenCompra.Autorizada, visible: true, habilitada: true },
      { estado: EstadoOrdenCompra.Cerrada, visible: true, habilitada: true },
      {
        estado: EstadoOrdenCompra.Cancelada,
        visible: true,
        habilitada: false,
      },
      { estado: EstadoOrdenCompra.Rechazada, visible: false },
    ]);
  });

  it('Cancelada con PDF: tooltip de "histórico" presente', () => {
    const oc = makeOc({ estado: EstadoOrdenCompra.Cancelada });
    const r = accionDescargarPdf(oc, PERMS_LEER);
    expect(r.motivoDeshabilitada).toMatch(/histórico/i);
  });

  it('Ver árbol documentos: ✅ Autorizada/Cerrada/Cancelada · ⚪ resto', () => {
    expectarCeldas(accionVerArbolDocumentos, PERMS_LEER, [
      {
        estado: EstadoOrdenCompra.Borrador,
        visible: true,
        habilitada: false,
      },
      {
        estado: EstadoOrdenCompra.EnAutorizacionJefeCompras,
        visible: true,
        habilitada: false,
      },
      { estado: EstadoOrdenCompra.Autorizada, visible: true, habilitada: true },
      { estado: EstadoOrdenCompra.Cerrada, visible: true, habilitada: true },
      { estado: EstadoOrdenCompra.Cancelada, visible: true, habilitada: true },
    ]);
  });

  it('Ver detalle (read-only): ✅ siempre con `leer`', () => {
    expectarCeldas(accionVerDetalleReadOnly, PERMS_LEER, [
      { estado: EstadoOrdenCompra.Borrador, visible: true, habilitada: true },
      { estado: EstadoOrdenCompra.Cerrada, visible: true, habilitada: true },
      { estado: EstadoOrdenCompra.Cancelada, visible: true, habilitada: true },
      { estado: EstadoOrdenCompra.Rechazada, visible: true, habilitada: true },
    ]);
  });
});

// ============================================================================
// 3. Integridad de la matriz: cobertura
// ============================================================================

describe('matriz §6.1 — cobertura', () => {
  it('ACCIONES_OC contiene las 20 acciones (19 del §6.1 + ver-detalle de coherencia)', () => {
    // §6.1 originalmente listaba 20; "agregar-linea-desde-rq" se
    // retiró tras decisión de scope (el flujo principal es N:1 al
    // crear la OC desde el Sheet). Si se reintroduce en Fase 2, este
    // contador sube de nuevo.
    expect(ACCIONES_OC).toHaveLength(20);
    const ids = new Set(ACCIONES_OC.map((a) => a.id));
    expect(ids.size).toBe(20); // sin duplicados
  });

  it('cada acción es invocable con (oc, permisosTodos) y devuelve un AccionDisponible válido', () => {
    for (const accion of ACCIONES_OC) {
      const oc = makeOc({ estado: EstadoOrdenCompra.Borrador });
      const r = accion.fn(oc, PERMISOS_TODOS);
      expect(typeof r.visible).toBe('boolean');
      expect(typeof r.habilitada).toBe('boolean');
      // Si visible=false, habilitada también debe ser false.
      if (!r.visible) {
        expect(r.habilitada).toBe(false);
      }
    }
  });
});
