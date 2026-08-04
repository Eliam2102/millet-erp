import { Link } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { EstadoBadge, NaturalezaBadge } from '@/components/erp';
import { Button } from '@/components/ui/button';
import {
  ESTADOS,
  NATURALEZAS,
  TRANSVERSALES,
  type EstadoRequisicion as EstadoKey,
  type Naturaleza as NaturalezaKey,
  type TerminoTransversal,
} from '@/features/compras/lib/glosario';
import {
  EstadoRequisicion,
  Naturaleza,
} from '@/features/compras/api/types';
import { DEFAULT_BANDEJA_SEARCH } from '@/features/compras/lib/bandeja-search-schema';

/**
 * <c>P11 — Página de ayuda del módulo Compras Requisiciones</c>
 * (doc 05 §13.7). Glosario + ciclo de vida + matriz simplificada +
 * FAQ. Accesible desde el botón "?" del topbar (UF7-PR3).
 *
 * <para>Decisión: <b>página estática</b> (no requiere fetch al
 * backend, no tiene CTAs operativos). Es contenido de referencia que
 * el usuario consulta cuando le aparece un término que no entiende
 * (estado raro, naturaleza, "matriz", "bifurcación", etc.).</para>
 *
 * <para>Cuando otros módulos del back-office lleguen, cada uno tendrá
 * su propia <c>/&lt;modulo&gt;/ayuda</c>; esta es la primera y sirve
 * de patrón.</para>
 */
export function Ayuda() {
  return (
    <div className="space-y-6">
      <header className="space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">
          Ayuda — Compras Requisiciones
        </h1>
        <p className="text-sm text-muted-foreground">
          Referencia rápida del módulo: estados, naturalezas, conceptos del
          dominio y preguntas frecuentes. Si tienes una duda específica que
          no aparece aquí, contacta a tu soporte interno.
        </p>
      </header>

      <SeccionGlosarioEstados />
      <SeccionCicloDeVida />
      <SeccionGlosarioNaturalezas />
      <SeccionMatrizSimplificada />
      <SeccionConceptosTransversales />
      <SeccionFaq />

      <div className="border-t pt-4">
        <Button asChild variant="ghost">
          <Link to="/compras/requisiciones" search={DEFAULT_BANDEJA_SEARCH}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            Volver a la bandeja
          </Link>
        </Button>
      </div>
    </div>
  );
}

// ─── Secciones ────────────────────────────────────────────────────

function SeccionGlosarioEstados() {
  // Mapeo del key del glosario al value numérico de EstadoRequisicion
  // para que <EstadoBadge/> reciba el value que espera. Usamos un
  // record explícito en lugar de Record.fromEntries para que TS valide
  // que cubrimos los 10 estados.
  const estadosOrdenados: { key: EstadoKey; value: EstadoRequisicion }[] = [
    { key: 'Borrador', value: EstadoRequisicion.Borrador },
    { key: 'EnAutorizacion', value: EstadoRequisicion.EnAutorizacion },
    { key: 'Autorizada', value: EstadoRequisicion.Autorizada },
    { key: 'EnSurtido', value: EstadoRequisicion.EnSurtido },
    { key: 'Cerrada', value: EstadoRequisicion.Cerrada },
    { key: 'Rechazada', value: EstadoRequisicion.Rechazada },
    { key: 'Cancelada', value: EstadoRequisicion.Cancelada },
    { key: 'Eliminada', value: EstadoRequisicion.Eliminada },
    { key: 'CerradaSinSurtir', value: EstadoRequisicion.CerradaSinSurtir },
    { key: 'CerradaSurtidaParcial', value: EstadoRequisicion.CerradaSurtidaParcial },
  ];
  return (
    <section className="space-y-3">
      <h2 id="estados" className="text-lg font-semibold">
        Estados de una requisición
      </h2>
      <p className="text-sm text-muted-foreground">
        Una requisición pasa por estos 10 estados durante su ciclo de vida.
        Los primeros son del flujo normal; los terminales por intervención
        son rechazo, cancelación, eliminación y cierre manual del jefe de
        almacén (sin surtir o surtida parcialmente, ADR-0043).
      </p>
      <div className="space-y-2">
        {estadosOrdenados.map(({ key, value }) => (
          <article
            key={key}
            className="flex flex-col gap-1 rounded-md border p-3 sm:flex-row sm:items-start sm:gap-3"
          >
            <div className="shrink-0">
              <EstadoBadge tipo="requisicion" estado={value} />
            </div>
            <p className="text-sm">{ESTADOS[key].resumen}</p>
          </article>
        ))}
      </div>
    </section>
  );
}

function SeccionCicloDeVida() {
  return (
    <section className="space-y-3">
      <h2 id="ciclo-de-vida" className="text-lg font-semibold">
        Ciclo de vida (diagrama)
      </h2>
      <p className="text-sm text-muted-foreground">
        Transiciones permitidas entre estados. Las flechas con ✕
        representan estados terminales (no admiten más cambios).
      </p>
      <div className="overflow-x-auto rounded-md border bg-muted/30 p-4">
        <pre className="text-xs leading-relaxed font-mono">
{`  Borrador ──[transmitir]──> EnAutorización ──[aprobar matriz]──> Autorizada ──> EnSurtido ──> Cerrada ✕
     │                            │                              │      │
     │                            │              [cerrar manual]─┤      ├──[cancelar]──> Cancelada ✕
     │                            │                              ▼      │
     │                            │           CerradaSinSurtir ✕ / CerradaSurtidaParcial ✕
     │                            │
     │                            └──[rechazar]──> Rechazada ✕
     │
     ├──[eliminar]──> Eliminada ✕
     │
     └──[matriz cumplida sin entrega] ──> Cerrada ✕

Pre-autorización: Borrador, EnAutorización
Post-autorización: Autorizada, EnSurtido
Terminales: Cerrada, Rechazada, Cancelada, Eliminada,
            CerradaSinSurtir, CerradaSurtidaParcial`}
        </pre>
      </div>
    </section>
  );
}

function SeccionGlosarioNaturalezas() {
  const naturalezasOrdenadas: { key: NaturalezaKey; value: Naturaleza }[] = [
    { key: 'Estandar', value: Naturaleza.Estandar },
    { key: 'Servicio', value: Naturaleza.Servicio },
    { key: 'Critico', value: Naturaleza.Critico },
    { key: 'Riesgo', value: Naturaleza.Riesgo },
  ];
  return (
    <section className="space-y-3">
      <h2 id="naturalezas" className="text-lg font-semibold">
        Naturalezas del artículo
      </h2>
      <p className="text-sm text-muted-foreground">
        Cada artículo del catálogo tiene una naturaleza. La naturaleza
        afecta cuántas firmas necesita la RQ y si requiere autorización
        de Nivel 2 sin importar el monto.
      </p>
      <div className="space-y-2">
        {naturalezasOrdenadas.map(({ key, value }) => (
          <article
            key={key}
            className="flex flex-col gap-1 rounded-md border p-3 sm:flex-row sm:items-start sm:gap-3"
          >
            <div className="shrink-0">
              <NaturalezaBadge naturaleza={value} />
            </div>
            <p className="text-sm">{NATURALEZAS[key].resumen}</p>
          </article>
        ))}
      </div>
    </section>
  );
}

function SeccionMatrizSimplificada() {
  return (
    <section className="space-y-3">
      <h2 id="matriz" className="text-lg font-semibold">
        Matriz de autorización (simplificada)
      </h2>
      <p className="text-sm text-muted-foreground">
        Reglas de cuándo se requiere cada nivel de firma. Es una versión
        condensada del doc 01 §3.bis — el detalle completo vive en el
        diseño técnico.
      </p>
      <div className="overflow-x-auto rounded-md border">
        <table className="w-full text-sm">
          <thead className="bg-muted/50">
            <tr>
              <th className="px-3 py-2 text-left font-medium">Caso</th>
              <th className="px-3 py-2 text-left font-medium">N1</th>
              <th className="px-3 py-2 text-left font-medium">N2</th>
              <th className="px-3 py-2 text-left font-medium">Almacén</th>
            </tr>
          </thead>
          <tbody className="text-sm">
            {MATRIZ_FILAS.map((f) => (
              <tr key={f.caso} className="border-t">
                <td className="px-3 py-2">{f.caso}</td>
                <td className="px-3 py-2">{f.n1}</td>
                <td className="px-3 py-2">{f.n2}</td>
                <td className="px-3 py-2">{f.almacen}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

const MATRIZ_FILAS = [
  {
    caso: 'Estándar, monto bajo',
    n1: '✅ requerido',
    n2: '— no aplica',
    almacen: '✅ si toca stock',
  },
  {
    caso: 'Estándar, monto alto',
    n1: '✅ requerido',
    n2: '✅ requerido',
    almacen: '✅ si toca stock',
  },
  {
    caso: 'Crítico (cualquier monto)',
    n1: '✅ requerido',
    n2: '✅ requerido',
    almacen: '✅ si toca stock',
  },
  {
    caso: 'Riesgo (cualquier monto)',
    n1: '✅ requerido',
    n2: '✅ + revisión',
    almacen: '✅ si toca stock',
  },
  {
    caso: 'Servicio (sin almacén)',
    n1: '✅ requerido',
    n2: 'según monto',
    almacen: '— no aplica',
  },
];

function SeccionConceptosTransversales() {
  const transversales: TerminoTransversal[] = [
    'Cubrimiento',
    'Matriz',
    'Bifurcacion',
    'Reserva',
  ];
  return (
    <section className="space-y-3">
      <h2 id="conceptos" className="text-lg font-semibold">
        Conceptos del dominio
      </h2>
      <div className="space-y-2">
        {transversales.map((t) => (
          <article
            key={t}
            className="rounded-md border p-3"
          >
            <h3 className="text-sm font-semibold">{t}</h3>
            <p className="mt-1 text-sm text-muted-foreground">
              {TRANSVERSALES[t].resumen}
            </p>
          </article>
        ))}
      </div>
    </section>
  );
}

function SeccionFaq() {
  return (
    <section className="space-y-3">
      <h2 id="faq" className="text-lg font-semibold">
        Preguntas frecuentes
      </h2>
      <div className="space-y-3">
        {FAQ.map((qa) => (
          <article key={qa.q} className="rounded-md border p-3">
            <h3 className="text-sm font-semibold">{qa.q}</h3>
            <p className="mt-1 text-sm text-muted-foreground">{qa.a}</p>
          </article>
        ))}
      </div>
    </section>
  );
}

const FAQ: { q: string; a: string }[] = [
  {
    q: '¿Por qué no puedo agregar líneas a una requisición Autorizada?',
    a: 'Las líneas se congelan al transmitir. Si necesitas agregar algo, debes cancelar la RQ (con motivo) y crear una nueva. Solo se pueden editar las notas de cada línea post-autorización.',
  },
  {
    q: '¿Qué diferencia hay entre Eliminar y Cancelar?',
    a: 'Eliminar es pre-autorización (Borrador o En autorización) y no tiene impacto fiscal ni de stock. Cancelar es post-autorización (Autorizada o En surtido) y sí libera reservas y cancela órdenes de compra borrador asociadas.',
  },
  {
    q: '¿Por qué veo solo "Mis requisiciones" y otros usuarios ven más?',
    a: 'Por permisos. Si tu rol incluye "ver-todos-departamentos", verás las RQs de todos los departamentos; si no, solo las del tuyo o las que captaste tú.',
  },
  {
    q: '¿Quién puede aprobar Nivel 2?',
    a: 'Los autorizadores designados con rol "AutorizadorN2" para el departamento de la RQ. La administración de aprobadores está en Compras → Configuración → Aprobadores y requiere el permiso compras.aprobadores.administrar.',
  },
  {
    q: '¿Puedo recuperar una RQ eliminada o rechazada?',
    a: 'No. Esos estados son terminales; la RQ queda en histórico para auditoría pero no se puede reactivar. Si necesitas el flujo, crea una nueva.',
  },
  {
    q: '¿Qué hago si veo "Esta requisición fue actualizada por otro usuario"?',
    a: 'Es un conflicto de concurrencia: alguien guardó cambios mientras editabas. Click en "Refrescar y revisar" para ver el estado actual y decidir si rehaces tu cambio sobre los datos frescos.',
  },
  {
    q: '¿Cómo imprimo una requisición?',
    a: 'Con Ctrl+P (Cmd+P en Mac) desde la pantalla de detalle. La RQ se imprime en A4 limpia, sin sidebar ni botones de acción.',
  },
];
