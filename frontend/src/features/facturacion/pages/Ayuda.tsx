import { Link } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  ETIQUETA_ESTADO_PEDIDO,
  ETIQUETA_ESTADO_TIMBRADO,
} from '@/features/facturacion/lib/glosario';

/**
 * <c>Ayuda — Facturación</c> (FE-F10). Referencia de estados y términos del
 * módulo + guía de operación de caja por teclado (a11y). Contenido estático
 * accesible (headings jerárquicos, listas, <c>dl</c>); cubierto por un test
 * axe wcag21aa.
 */
const DESC_ESTADO_PEDIDO: Record<string, string> = {
  Importado: 'Listo para facturar; es la única ventana de edición libre.',
  Bloqueado: 'Un cajero lo está facturando (soft-lock).',
  Facturado: 'Tiene un CFDI vigente. Al cancelar el CFDI vuelve a Importado.',
  Cancelado: 'Cancelado (cancelación de A+W sin CFDI, o anulado).',
  Excepcion: 'Falló la validación de ingesta; se resuelve en la bandeja de excepciones.',
};

const DESC_ESTADO_TIMBRADO: Record<string, string> = {
  Borrador: 'Local, sin timbrar. Editable.',
  PendientePedimento: 'Retenida porque requiere pedimento aún no capturado.',
  TimbradoEnProceso: 'Enviada al PAC; el timbre puede resolverse asíncronamente.',
  Timbrado: 'Timbrada por el SAT: UUID y sellos disponibles. Inmutable.',
  TimbradoFallido: 'Error del PAC; corregible → vuelve a Borrador.',
  CancelacionPendiente: 'Solicitud de cancelación SAT 4.0 en curso.',
  Cancelado: 'Cancelada ante el SAT. El CFDI nunca se borra.',
};

const TERMINOS: ReadonlyArray<{ termino: string; definicion: string }> = [
  {
    termino: 'Anticipo (serie FANT)',
    definicion:
      'Factura de anticipo nominal. Se amortiza en la factura final con una NC (relación 07) emitida de forma atómica al timbrar.',
  },
  {
    termino: 'NC por bonificación',
    definicion:
      'Nota de crédito sobre una factura vigente (relación 01) por bonificación/descuento posterior.',
  },
  {
    termino: 'REPP (complemento de pago)',
    definicion:
      'Recibo electrónico de pago (Pago 2.0). Puede cubrir varias facturas (parcialidades) y calcula ganancia/pérdida cambiaria.',
  },
  {
    termino: 'Carta Porte 3.1',
    definicion:
      'CFDI de Traslado (T) o Ingreso (I) con el complemento de transporte. Soporta "siguiente tramo" que referencia al previo.',
  },
  {
    termino: 'CCE (Comercio Exterior)',
    definicion:
      'Complemento de exportación: tipo de operación, incoterm, TC DOF y datos de aduana por concepto.',
  },
  {
    termino: 'Pedimento',
    definicion:
      'Compuerta por factura: si un concepto lo requiere, la factura queda PendientePedimento hasta capturarlo, y entonces se timbra.',
  },
];

const ATAJOS_CAJA: ReadonlyArray<{ accion: string; teclas: string }> = [
  { accion: 'Avanzar / retroceder entre campos', teclas: 'Tab / Shift+Tab' },
  { accion: 'Abrir y elegir en un selector', teclas: 'Enter / ↑ ↓ / Esc' },
  { accion: 'Confirmar el formulario (emitir/guardar)', teclas: 'Enter en el botón o Ctrl+Enter' },
  { accion: 'Cerrar el panel lateral (Sheet)', teclas: 'Esc' },
  { accion: 'Imprimir el comprobante en pantalla', teclas: 'Ctrl+P' },
];

export function Ayuda() {
  return (
    <div className="mx-auto max-w-4xl space-y-6 px-4 py-8">
      <div data-print="hidden">
        <Button variant="ghost" size="sm" asChild>
          <Link to="/facturacion">
            <ArrowLeft className="mr-1 h-4 w-4" />
            Volver a Facturación
          </Link>
        </Button>
      </div>

      <header className="space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">
          Ayuda — Facturación
        </h1>
        <p className="text-sm text-muted-foreground">
          Estados, términos del módulo y operación de caja por teclado.
        </p>
      </header>

      <section className="space-y-2" aria-labelledby="ayuda-estados-pedido">
        <h2 id="ayuda-estados-pedido" className="text-lg font-medium">
          Estados de un pedido
        </h2>
        <dl className="divide-y rounded-md border">
          {Object.entries(ETIQUETA_ESTADO_PEDIDO).map(([clave, etiqueta]) => (
            <div key={clave} className="grid grid-cols-1 gap-1 px-3 py-2 sm:grid-cols-3">
              <dt className="font-medium">{etiqueta}</dt>
              <dd className="text-sm text-muted-foreground sm:col-span-2">
                {DESC_ESTADO_PEDIDO[clave] ?? ''}
              </dd>
            </div>
          ))}
        </dl>
      </section>

      <section className="space-y-2" aria-labelledby="ayuda-estados-timbrado">
        <h2 id="ayuda-estados-timbrado" className="text-lg font-medium">
          Estados de timbrado del comprobante
        </h2>
        <dl className="divide-y rounded-md border">
          {Object.entries(ETIQUETA_ESTADO_TIMBRADO).map(([clave, etiqueta]) => (
            <div key={clave} className="grid grid-cols-1 gap-1 px-3 py-2 sm:grid-cols-3">
              <dt className="font-medium">{etiqueta}</dt>
              <dd className="text-sm text-muted-foreground sm:col-span-2">
                {DESC_ESTADO_TIMBRADO[clave] ?? ''}
              </dd>
            </div>
          ))}
        </dl>
      </section>

      <section className="space-y-2" aria-labelledby="ayuda-terminos">
        <h2 id="ayuda-terminos" className="text-lg font-medium">
          Términos del módulo
        </h2>
        <dl className="divide-y rounded-md border">
          {TERMINOS.map((t) => (
            <div key={t.termino} className="grid grid-cols-1 gap-1 px-3 py-2 sm:grid-cols-3">
              <dt className="font-medium">{t.termino}</dt>
              <dd className="text-sm text-muted-foreground sm:col-span-2">
                {t.definicion}
              </dd>
            </div>
          ))}
        </dl>
      </section>

      <section className="space-y-2" aria-labelledby="ayuda-caja">
        <h2 id="ayuda-caja" className="text-lg font-medium">
          Operación de caja por teclado
        </h2>
        <p className="text-sm text-muted-foreground">
          La pantalla de cobro y emisión se opera completa sin mouse, para no
          frenar la caja.
        </p>
        <ul className="divide-y rounded-md border">
          {ATAJOS_CAJA.map((a) => (
            <li key={a.accion} className="flex items-center justify-between gap-4 px-3 py-2">
              <span className="text-sm">{a.accion}</span>
              <kbd className="rounded bg-muted px-2 py-0.5 font-mono text-xs">
                {a.teclas}
              </kbd>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
