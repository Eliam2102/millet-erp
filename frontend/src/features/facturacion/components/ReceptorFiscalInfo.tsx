import { Link } from '@tanstack/react-router';
import { SquarePen } from 'lucide-react';
import { Alert } from '@/components/ui/alert';
import { type DiagnosticoReceptorFiscal } from '../lib/receptor-fiscal-error';
import { cn } from '@/lib/utils';

/**
 * Datos fiscales del receptor — SIEMPRE de solo lectura en los forms de
 * emisión (factura, anticipo, carta porte): vienen fijos del master de
 * clientes, igual que el emisor viene fijo de la empresa. El CTA al
 * catálogo lo controla el permiso `facturacion.facturas.editar-receptor`;
 * la corrección se hace ahí, nunca en el comprobante.
 */
export interface ReceptorFiscalValores {
  rfc: string;
  nombre: string;
  regimenFiscal: string;
  codigoPostal: string;
}

export function ReceptorFiscalInfo({
  valores,
  clienteId,
  puedeCorregirEnCatalogo,
}: {
  valores: ReceptorFiscalValores;
  clienteId: string | null;
  puedeCorregirEnCatalogo: boolean;
}) {
  return (
    <div className="flex items-start justify-between gap-3 rounded-md border bg-muted/30 px-3 py-2">
      <dl className="grid flex-1 grid-cols-2 gap-x-4 gap-y-1.5 sm:grid-cols-4">
        <DatoFiscal etiqueta="RFC" valor={valores.rfc} mono />
        <DatoFiscal etiqueta="Razón social" valor={valores.nombre} />
        <DatoFiscal etiqueta="Régimen fiscal" valor={valores.regimenFiscal} />
        <DatoFiscal etiqueta="CP fiscal" valor={valores.codigoPostal} />
      </dl>
      {puedeCorregirEnCatalogo && clienteId != null && (
        <Link
          to="/admin/datos-maestros/clientes/$id"
          params={{ id: clienteId }}
          className="inline-flex shrink-0 items-center gap-1 text-xs font-medium text-primary hover:underline"
          title="Corregir los datos fiscales en el catálogo de clientes"
        >
          <SquarePen className="size-3.5" aria-hidden />
          Corregir en catálogo
        </Link>
      )}
    </div>
  );
}

/**
 * Banner de bloqueo cuando el receptor está incompleto en el master
 * (gap G12) — la emisión se deshabilita para todos; la liga al catálogo
 * solo aparece con el permiso.
 */
export function ReceptorIncompletoBanner({
  clienteId,
  puedeCorregirEnCatalogo,
  diagnostico,
  textoSinCliente = 'Selecciona un cliente del catálogo para poder emitir.',
  className,
}: {
  clienteId: string | null;
  puedeCorregirEnCatalogo: boolean;
  diagnostico?: DiagnosticoReceptorFiscal;
  textoSinCliente?: string;
  className?: string;
}) {
  return (
    <Alert
      className={cn(
        'rounded-md border-line bg-warning-note-bg text-warning-note-fg px-3 py-2.5 text-sm',
        className,
      )}
    >
      {diagnostico ? (
        <>
          <p className="font-medium">Corrige los datos fiscales del receptor para poder timbrar.</p>
          <ul className="list-disc pl-4">
            {diagnostico.campos.map(({ campo, motivo }, index) => <li key={`${campo}-${index}`}>{motivo}</li>)}
          </ul>
          {diagnostico.clienteId && (
            <Link to="/admin/datos-maestros/clientes/$id" params={{ id: diagnostico.clienteId }}
              className="font-medium underline underline-offset-2">Revisar cliente en Datos Maestros</Link>
          )}
        </>
      ) : clienteId == null ? (
        <>{textoSinCliente}</>
      ) : puedeCorregirEnCatalogo ? (
        <>
          Datos fiscales del cliente incompletos en el master (gap G12:
          régimen y CP no vienen de A+W) —{' '}
          <Link
            to="/admin/datos-maestros/clientes/$id"
            params={{ id: clienteId }}
            className="font-medium underline underline-offset-2"
          >
            complétalos en Datos Maestros → Clientes
          </Link>{' '}
          para poder emitir.
        </>
      ) : (
        <>
          Datos fiscales del cliente incompletos en el master — solicita que
          los completen en Datos Maestros → Clientes para poder emitir.
        </>
      )}
    </Alert>
  );
}

function DatoFiscal({
  etiqueta,
  valor,
  mono,
}: {
  etiqueta: string;
  valor: string;
  mono?: boolean;
}) {
  const vacio = (valor ?? '').trim() === '';
  return (
    <div className="min-w-0">
      <dt className="text-[10px] uppercase tracking-wide text-muted-foreground">
        {etiqueta}
      </dt>
      <dd
        className={cn(
          'truncate text-sm',
          mono && 'font-mono',
          vacio
            ? 'text-amber-600 dark:text-amber-500'
            : 'font-medium text-foreground/90',
        )}
        title={vacio ? undefined : valor}
      >
        {vacio ? 'Falta en el master' : valor}
      </dd>
    </div>
  );
}
