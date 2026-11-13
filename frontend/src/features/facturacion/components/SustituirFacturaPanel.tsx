import { problemaConsulta } from '@/components/erp/feedback/problema-consulta';
import { ErrorState } from '@/components/erp';
import { EmitirFacturaForm } from '@/features/facturacion/components/emitir-factura/EmitirFacturaForm';
import { useDatosSustitucion } from '@/features/facturacion/api/useFacturas';
import { prefillSustitucion } from '@/features/facturacion/lib/prefill-sustitucion';
import { estadoUi } from '@/features/facturacion/lib/estados-ui';
import { useNavigate } from '@tanstack/react-router';
import { toast } from 'sonner';

/**
 * <c>&lt;SustituirFacturaPanel/&gt;</c> — FAC-08b. Abre en el detalle el formulario de
 * emisión con los datos de la factura original y la relación 04. Al timbrar, el ERP
 * solicita la cancelación de la original (motivo 01, sustituto = la nueva) y navega a la
 * sustituta.
 */
export function SustituirFacturaPanel({
  facturaId,
  onDone,
  motivo = '01',
}: {
  facturaId: string;
  onDone: () => void;
  /** 01 sustituye con relación 04; 02 y 04 reemiten sin relación una factura ya cancelada. */
  motivo?: string;
}) {
  const navigate = useNavigate();
  const datos = useDatosSustitucion(facturaId, motivo);

  if (datos.isPending) return <p className="text-sm text-ink-muted">Preparando la factura sustituta…</p>;
  if (datos.isError)
    return (
      <ErrorState
        title="No se puede sustituir la factura"
        problem={problemaConsulta(datos.error)}
        onRetry={() => void datos.refetch()}
      />
    );

  return (
    <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat" aria-label="Sustituir factura">
      <h2 className="text-sm font-medium">
        {motivo === '01' ? 'Sustituir' : `Reemitir (motivo ${motivo})`}{' '}
        <span className="font-mono">{datos.data.sustituyeFolio}</span>
      </h2>
      <p role="note" className="rounded-md bg-info-bg px-3 py-2 text-sm text-info-fg">
        {datos.data.pasos?.join(' ')}{' '}
        {datos.data.requiereAceptacion
          ? 'La cancelación requiere aceptación del receptor.'
          : `Sin aceptación del receptor: ${(datos.data.razonesSinAceptacion ?? []).join('; ').toLowerCase()}.`}
      </p>
      <EmitirFacturaForm
        prefill={prefillSustitucion(datos.data)}
        onSuccess={(res) => {
          if (!datos.data.llevaRelacion04) {
            toast.success(`Factura ${res.folio} emitida sin relación (la original ya estaba cancelada).`);
            onDone();
            void navigate({ to: '/facturacion/facturas/$id', params: { id: res.id } });
            return;
          }
          toast.success(
            res.estado === 'Timbrado'
              ? `Sustituta ${res.folio} timbrada. La cancelación de ${datos.data.sustituyeFolio} queda pendiente de autorización.`
              : `Sustituta ${res.folio} guardada (${estadoUi('factura', res.estado).texto}). Al timbrarla se pedirá la cancelación de ${datos.data.sustituyeFolio}.`,
          );
          onDone();
          void navigate({ to: '/facturacion/facturas/$id', params: { id: res.id } });
        }}
        onCancel={onDone}
      />
    </section>
  );
}
