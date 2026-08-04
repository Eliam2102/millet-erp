import { describe, expect, it } from 'vitest';
import {
  accionAgregarLinea,
  accionAprobarNivel1,
  accionAprobarNivel2,
  accionCancelar,
  accionCerrarManual,
  accionEditarCabecera,
  accionEditarLinea,
  accionEditarNotasLinea,
  accionEliminarLinea,
  accionEliminarRequisicion,
  accionRechazar,
  accionTransmitir,
  accionVerDetalle,
} from '@/features/compras/lib/acciones-disponibles';
import {
  Clasificacion,
  EstadoRequisicion,
  NivelAutorizacion,
  Prioridad,
  type RequisicionResponse,
} from '@/features/compras/api/types';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Tests parametrizados de la <b>matriz §6.1 del doc 05</b>. Recorren
 * <i>todas</i> las celdas (cada acción × cada estado) verificando el
 * outcome ✅ / ⚪ / ❌ esperado. Si la matriz cambia en el doc, se
 * actualiza este test en el mismo PR.
 *
 * <para>Convención: <c>'visible'</c>=<c>true</c> y <c>'hab'</c>=<c>true</c>
 * mapean a ✅; <c>'visible'</c>=<c>true</c> y <c>'hab'</c>=<c>false</c>
 * a ⚪; <c>'visible'</c>=<c>false</c> a ❌.</para>
 */

const ESTADOS_TODOS: EstadoRequisicion[] = [
  EstadoRequisicion.Borrador,
  EstadoRequisicion.EnAutorizacion,
  EstadoRequisicion.Autorizada,
  EstadoRequisicion.EnSurtido,
  EstadoRequisicion.Cerrada,
  EstadoRequisicion.Cancelada,
  EstadoRequisicion.Rechazada,
  EstadoRequisicion.Eliminada,
  EstadoRequisicion.CerradaSinSurtir,
  EstadoRequisicion.CerradaSurtidaParcial,
];

const NOMBRES: Record<EstadoRequisicion, string> = {
  [EstadoRequisicion.Borrador]: 'Borrador',
  [EstadoRequisicion.EnAutorizacion]: 'EnAutorizacion',
  [EstadoRequisicion.Autorizada]: 'Autorizada',
  [EstadoRequisicion.EnSurtido]: 'EnSurtido',
  [EstadoRequisicion.Cerrada]: 'Cerrada',
  [EstadoRequisicion.Cancelada]: 'Cancelada',
  [EstadoRequisicion.Rechazada]: 'Rechazada',
  [EstadoRequisicion.Eliminada]: 'Eliminada',
  // ADR-0043: terminales de cierre manual. Aún no entran a ESTADOS_TODOS
  // (la matriz §6.1 los cubrirá en el PR #2, junto con la acción de cierre
  // manual que los hace alcanzables); aquí solo satisfacen el Record exhaustivo.
  [EstadoRequisicion.CerradaSinSurtir]: 'CerradaSinSurtir',
  [EstadoRequisicion.CerradaSurtidaParcial]: 'CerradaSurtidaParcial',
};

function makeRq(
  estado: EstadoRequisicion,
  overrides: Partial<RequisicionResponse> = {},
): RequisicionResponse {
  return {
    id: 'rq-1',
    empresaId: 'e-1',
    folio: 'MID2026-000001',
    folioAnio: 2026,
    clasificacion: Clasificacion.Servicio,
    sucursalId: 's-1',
    departamentoId: 'd-1',
    almacenDestinoId: 'a-1',
    requisitanteId: 'u-1',
    creadorId: 'u-1',
    descripcion: null,
    prioridad: Prioridad.Normal,
    fechaSolicitud: '2026-05-09T10:00:00Z',
    fechaEntregaDeseada: null,
    proveedorSugeridoId: null,
    estado,
    motivoTerminacionId: null,
    motivoTerminacionTexto: null,
    actorTerminacionId: null,
    fechaTerminacion: null,
    version: 1,
    createdAt: '2026-05-09T10:00:00Z',
    updatedAt: '2026-05-09T10:00:00Z',
    lineas: [
      {
        id: 'l-1',
        posicion: 1,
        articuloId: 'art-1',
        cantidad: 1,
        unidadMedida: 'PZA',
        precioEstimadoMonto: 100,
        precioEstimadoMoneda: 'MXN',
        cuentaContableId: null,
        centroCostoId: null,
        proyecto: null,
        fechaRequerida: null,
        notas: null,
        cantDeAlmacen: 0,
        cantDeCompra: 0,
        cantRecibida: 0,
        cantPendiente: 1,
        reservaId: null,
      },
    ],
    autorizaciones: [],
    ...overrides,
  };
}

const TODOS_LOS_PERMISOS = Object.values(PermisosCanonicos);

// ============================================================================
// FILA 1: Editar cabecera — solo Borrador, permiso `editar`
// ============================================================================

describe('accionEditarCabecera (matriz §6.1 fila 1)', () => {
  it.each(ESTADOS_TODOS)(
    'estado=%i con permiso editar: visible/habilitada solo en Borrador',
    (estado) => {
      const result = accionEditarCabecera(makeRq(estado), TODOS_LOS_PERMISOS);
      const esperado = estado === EstadoRequisicion.Borrador;
      expect(result.visible).toBe(esperado);
      expect(result.habilitada).toBe(esperado);
    },
  );

  it('sin permiso editar: oculto siempre', () => {
    expect(
      accionEditarCabecera(makeRq(EstadoRequisicion.Borrador), []),
    ).toEqual({ visible: false, habilitada: false });
  });
});

// ============================================================================
// FILA 2: Agregar línea — solo Borrador, permiso `editar`
// ============================================================================

describe('accionAgregarLinea (matriz §6.1 fila 2)', () => {
  it.each(ESTADOS_TODOS)(
    'estado=%i: visible solo en Borrador',
    (estado) => {
      const result = accionAgregarLinea(makeRq(estado), TODOS_LOS_PERMISOS);
      expect(result.visible).toBe(estado === EstadoRequisicion.Borrador);
    },
  );

  it('sin permiso editar: oculto', () => {
    expect(
      accionAgregarLinea(makeRq(EstadoRequisicion.Borrador), []),
    ).toEqual({ visible: false, habilitada: false });
  });
});

// ============================================================================
// FILA 3 + 4: Editar/Eliminar línea estructural — solo Borrador
// ============================================================================

describe('accionEditarLinea (matriz §6.1 fila 3)', () => {
  it.each(ESTADOS_TODOS)('estado=%i: solo Borrador', (estado) => {
    const result = accionEditarLinea(makeRq(estado), TODOS_LOS_PERMISOS);
    expect(result.visible).toBe(estado === EstadoRequisicion.Borrador);
  });
});

describe('accionEliminarLinea (matriz §6.1 fila 4)', () => {
  it.each(ESTADOS_TODOS)('estado=%i: solo Borrador', (estado) => {
    const result = accionEliminarLinea(makeRq(estado), TODOS_LOS_PERMISOS);
    expect(result.visible).toBe(estado === EstadoRequisicion.Borrador);
  });
});

// ============================================================================
// FILA 5: Editar notas de línea — Borrador, EnAut, Autorizada, EnSurtido
// ============================================================================

describe('accionEditarNotasLinea (matriz §6.1 fila 5)', () => {
  const editables: EstadoRequisicion[] = [
    EstadoRequisicion.Borrador,
    EstadoRequisicion.EnAutorizacion,
    EstadoRequisicion.Autorizada,
    EstadoRequisicion.EnSurtido,
  ];

  it.each(ESTADOS_TODOS)(
    'estado=%i: visible solo en estados no-terminales',
    (estado) => {
      const result = accionEditarNotasLinea(makeRq(estado), TODOS_LOS_PERMISOS);
      expect(result.visible).toBe(editables.includes(estado));
    },
  );

  it('sin permiso editar: oculto en cualquier estado', () => {
    for (const estado of ESTADOS_TODOS) {
      expect(accionEditarNotasLinea(makeRq(estado), []).visible).toBe(false);
    }
  });
});

// ============================================================================
// FILA 6: Transmitir — solo Borrador con ≥1 línea (deshabilitada con tooltip si 0)
// ============================================================================

describe('accionTransmitir (matriz §6.1 fila 6)', () => {
  it('Borrador con líneas: ✅', () => {
    expect(
      accionTransmitir(
        makeRq(EstadoRequisicion.Borrador),
        TODOS_LOS_PERMISOS,
      ),
    ).toEqual({ visible: true, habilitada: true });
  });

  it('Borrador SIN líneas: ⚪ con tooltip', () => {
    const result = accionTransmitir(
      makeRq(EstadoRequisicion.Borrador, { lineas: [] }),
      TODOS_LOS_PERMISOS,
    );
    expect(result.visible).toBe(true);
    expect(result.habilitada).toBe(false);
    expect(result.motivoDeshabilitada).toMatch(/al menos una línea/i);
  });

  it.each(ESTADOS_TODOS.filter((e) => e !== EstadoRequisicion.Borrador))(
    'estado=%i: oculto',
    (estado) => {
      expect(accionTransmitir(makeRq(estado), TODOS_LOS_PERMISOS).visible).toBe(
        false,
      );
    },
  );
});

// ============================================================================
// FILA 7: Aprobar Nivel1 — solo EnAutorizacion, sin haber firmado N1
// ============================================================================

describe('accionAprobarNivel1 (matriz §6.1 fila 7)', () => {
  it('EnAutorizacion sin firmas: ✅', () => {
    expect(
      accionAprobarNivel1(
        makeRq(EstadoRequisicion.EnAutorizacion),
        TODOS_LOS_PERMISOS,
      ),
    ).toEqual({ visible: true, habilitada: true });
  });

  it('EnAutorizacion CON N1 firmado por mí: oculto (no se firma 2x)', () => {
    const rq = makeRq(EstadoRequisicion.EnAutorizacion, {
      autorizaciones: [
        {
          id: 'a-1',
          nivel: NivelAutorizacion.Nivel1,
          usuarioId: 'autorizador-1',
          fechaHora: '2026-05-09T10:00:00Z',
          notas: null,
        },
      ],
    });
    expect(accionAprobarNivel1(rq, TODOS_LOS_PERMISOS).visible).toBe(false);
  });

  it('EnAutorizacion CON N1 firmado por OTRO: oculto (N1 ya satisfecho; el dominio impide un 2º N1)', () => {
    const rq = makeRq(EstadoRequisicion.EnAutorizacion, {
      autorizaciones: [
        {
          id: 'a-1',
          nivel: NivelAutorizacion.Nivel1,
          usuarioId: 'otro',
          fechaHora: '2026-05-09T10:00:00Z',
          notas: null,
        },
      ],
    });
    expect(accionAprobarNivel1(rq, TODOS_LOS_PERMISOS).visible).toBe(false);
  });

  it.each(ESTADOS_TODOS.filter((e) => e !== EstadoRequisicion.EnAutorizacion))(
    'estado=%i: oculto',
    (estado) => {
      expect(
        accionAprobarNivel1(makeRq(estado), TODOS_LOS_PERMISOS).visible,
      ).toBe(false);
    },
  );

  it('sin permiso autorizar-nivel1: oculto', () => {
    expect(
      accionAprobarNivel1(makeRq(EstadoRequisicion.EnAutorizacion), []).visible,
    ).toBe(false);
  });
});

// ============================================================================
// FILA 8: Aprobar Nivel2 — EnAut + N1 firmado (DESHABILITADA con tooltip si N1 falta)
// ============================================================================

describe('accionAprobarNivel2 (matriz §6.1 fila 8)', () => {
  const rqConN1Firmado = makeRq(EstadoRequisicion.EnAutorizacion, {
    autorizaciones: [
      {
        id: 'a-1',
        nivel: NivelAutorizacion.Nivel1,
        usuarioId: 'jefe',
        fechaHora: '2026-05-09T10:00:00Z',
        notas: null,
      },
    ],
  });

  it('EnAutorizacion + N1 firmado: ✅', () => {
    expect(
      accionAprobarNivel2(rqConN1Firmado, TODOS_LOS_PERMISOS, 'autorizador-2'),
    ).toEqual({ visible: true, habilitada: true });
  });

  it('EnAutorizacion SIN N1 firmado: ⚪ con tooltip "Falta autorización Nivel 1"', () => {
    const result = accionAprobarNivel2(
      makeRq(EstadoRequisicion.EnAutorizacion),
      TODOS_LOS_PERMISOS,
      'autorizador-2',
    );
    expect(result.visible).toBe(true);
    expect(result.habilitada).toBe(false);
    expect(result.motivoDeshabilitada).toBe('Falta autorización Nivel 1');
  });

  it('EnAutorizacion CON N2 ya firmado por mí: oculto', () => {
    const rq = makeRq(EstadoRequisicion.EnAutorizacion, {
      autorizaciones: [
        {
          id: 'a-1',
          nivel: NivelAutorizacion.Nivel1,
          usuarioId: 'jefe',
          fechaHora: '2026-05-09T10:00:00Z',
          notas: null,
        },
        {
          id: 'a-2',
          nivel: NivelAutorizacion.Nivel2,
          usuarioId: 'autorizador-2',
          fechaHora: '2026-05-09T11:00:00Z',
          notas: null,
        },
      ],
    });
    expect(
      accionAprobarNivel2(rq, TODOS_LOS_PERMISOS, 'autorizador-2').visible,
    ).toBe(false);
  });

  it.each(ESTADOS_TODOS.filter((e) => e !== EstadoRequisicion.EnAutorizacion))(
    'estado=%i: oculto',
    (estado) => {
      expect(
        accionAprobarNivel2(makeRq(estado), TODOS_LOS_PERMISOS, 'u-1').visible,
      ).toBe(false);
    },
  );
});

// ============================================================================
// FILA 9: Rechazar — solo EnAutorizacion, permiso `rechazar`
// ============================================================================

describe('accionRechazar (matriz §6.1 fila 9)', () => {
  it.each(ESTADOS_TODOS)('estado=%i: solo EnAutorizacion', (estado) => {
    expect(accionRechazar(makeRq(estado), TODOS_LOS_PERMISOS).visible).toBe(
      estado === EstadoRequisicion.EnAutorizacion,
    );
  });

  it('sin permiso rechazar: oculto', () => {
    expect(
      accionRechazar(makeRq(EstadoRequisicion.EnAutorizacion), []).visible,
    ).toBe(false);
  });
});

// ============================================================================
// FILA 10: Eliminar (RQ) — Borrador o EnAutorizacion, permiso `eliminar`
// ============================================================================

describe('accionEliminarRequisicion (matriz §6.1 fila 10)', () => {
  it.each(ESTADOS_TODOS)(
    'estado=%i: visible solo en Borrador o EnAutorizacion',
    (estado) => {
      const result = accionEliminarRequisicion(
        makeRq(estado),
        TODOS_LOS_PERMISOS,
      );
      const esperado =
        estado === EstadoRequisicion.Borrador ||
        estado === EstadoRequisicion.EnAutorizacion;
      expect(result.visible).toBe(esperado);
    },
  );

  it('sin permiso eliminar: oculto', () => {
    expect(
      accionEliminarRequisicion(makeRq(EstadoRequisicion.Borrador), []).visible,
    ).toBe(false);
  });
});

// ============================================================================
// FILA 11: Cancelar — Autorizada o EnSurtido, permiso `cancelar`
// ============================================================================

describe('accionCancelar (matriz §6.1 fila 11)', () => {
  it.each(ESTADOS_TODOS)(
    'estado=%i: visible solo en Autorizada o EnSurtido',
    (estado) => {
      const result = accionCancelar(makeRq(estado), TODOS_LOS_PERMISOS);
      const esperado =
        estado === EstadoRequisicion.Autorizada ||
        estado === EstadoRequisicion.EnSurtido;
      expect(result.visible).toBe(esperado);
    },
  );

  it('sin permiso cancelar: oculto', () => {
    expect(
      accionCancelar(makeRq(EstadoRequisicion.Autorizada), []).visible,
    ).toBe(false);
  });
});

// ============================================================================
// FILA 12: Ver detalle — siempre con permiso `leer`
// ============================================================================

describe('accionVerDetalle (matriz §6.1 fila 12)', () => {
  it('con permiso leer: siempre visible (independiente de estado)', () => {
    expect(accionVerDetalle([PermisosCanonicos.ComprasRequisicionesLeer]).visible).toBe(
      true,
    );
  });

  it('sin permiso leer: oculto', () => {
    expect(accionVerDetalle([]).visible).toBe(false);
  });
});

// ============================================================================
// Cobertura cross: contar celdas para asegurar que la matriz está completa
// ============================================================================

describe('Cobertura matriz §6.1 (acciones sin parámetros × 10 estados)', () => {
  it('verifica los outcomes de visibilidad para acciones SIN parámetros adicionales (incl. cierre manual y los 2 terminales nuevos)', () => {
    const acciones = [
      { name: 'editarCabecera', fn: accionEditarCabecera, visiblesEn: [EstadoRequisicion.Borrador] },
      { name: 'agregarLinea', fn: accionAgregarLinea, visiblesEn: [EstadoRequisicion.Borrador] },
      { name: 'editarLinea', fn: accionEditarLinea, visiblesEn: [EstadoRequisicion.Borrador] },
      { name: 'eliminarLinea', fn: accionEliminarLinea, visiblesEn: [EstadoRequisicion.Borrador] },
      {
        name: 'editarNotasLinea',
        fn: accionEditarNotasLinea,
        visiblesEn: [
          EstadoRequisicion.Borrador,
          EstadoRequisicion.EnAutorizacion,
          EstadoRequisicion.Autorizada,
          EstadoRequisicion.EnSurtido,
        ],
      },
      { name: 'rechazar', fn: accionRechazar, visiblesEn: [EstadoRequisicion.EnAutorizacion] },
      {
        name: 'eliminarRequisicion',
        fn: accionEliminarRequisicion,
        visiblesEn: [EstadoRequisicion.Borrador, EstadoRequisicion.EnAutorizacion],
      },
      {
        name: 'cancelar',
        fn: accionCancelar,
        visiblesEn: [EstadoRequisicion.Autorizada, EstadoRequisicion.EnSurtido],
      },
      {
        name: 'cerrarManual',
        fn: accionCerrarManual,
        visiblesEn: [EstadoRequisicion.Autorizada, EstadoRequisicion.EnSurtido],
      },
    ];

    for (const a of acciones) {
      for (const estado of ESTADOS_TODOS) {
        const result = a.fn(makeRq(estado), TODOS_LOS_PERMISOS);
        const esperaVisible = a.visiblesEn.includes(estado);
        expect(
          result.visible,
          `[${a.name}] estado=${NOMBRES[estado]} esperaba visible=${esperaVisible}`,
        ).toBe(esperaVisible);
      }
    }
  });
});
