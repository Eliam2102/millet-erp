import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { FileSearch } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Textarea } from '@/components/ui/textarea';
import { useBodyScopedIdempotencyKey } from '@/lib/api';
import { type DocumentoLiquidacionIva, type RecalculoIva, useRecalcularIvaDocumento } from '../../api/liquidacion-iva';
import { documentosCorregibles, motivoCorreccionValido } from '../../lib/correccion-iva';
import { SELECT_CLASS } from '../../lib/estilos';
import { MOTIVO_ANULACION_MAXIMO, mensajePoliza, monto } from '../../lib/polizas';

/**
 * M-209 (86e3zr7mp) · CA4: recalcular el IVA de una factura de proveedor contra su XML. Primero muestra la diferencia por tratamiento
 * (lo contabilizado contra el XML); al aplicar con motivo nace la póliza de ajuste ligada a la misma factura (la original no se anula).
 */
export function RecalcularIvaDocumento({ documentos }: { documentos: DocumentoLiquidacionIva[] }) {
  const corregibles = documentosCorregibles(documentos);
  const recalcular = useRecalcularIvaDocumento();
  const keyFor = useBodyScopedIdempotencyKey();
  const [saldoId, setSaldoId] = useState('');
  const [motivo, setMotivo] = useState('');
  const [previa, setPrevia] = useState<RecalculoIva | null>(null);
  const [error, setError] = useState('');

  if (corregibles.length === 0) return null;

  function ejecutar(aplicar: boolean) {
    setError('');
    const cuerpo = { saldoId, motivo: aplicar ? motivo.trim() : null, aplicar };
    recalcular.mutate(
      { ...cuerpo, idempotencyKey: keyFor(cuerpo) },
      {
        onSuccess: (r) => {
          setPrevia(r);
          if (aplicar) setMotivo('');
        },
        onError: (e) => setError(mensajePoliza(e)),
      },
    );
  }

  const puedeAplicar = !!previa && !previa.sinDiferencia && !previa.polizaAjusteId && motivoCorreccionValido(motivo);

  return (
    <section aria-label="Corregir IVA de un documento" className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="flex items-center gap-2 text-md font-semibold">
        <FileSearch className="size-4 text-brand" strokeWidth={1.6} aria-hidden="true" />
        Corregir IVA de una factura contra su XML
      </h2>
      <div className="flex flex-wrap items-end gap-3">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="iva-corregir-documento">Factura de proveedor</Label>
          <select
            id="iva-corregir-documento"
            className={SELECT_CLASS}
            value={saldoId}
            onChange={(e) => {
              setSaldoId(e.target.value);
              setPrevia(null);
              setError('');
            }}
          >
            <option value="">Elegir factura</option>
            {corregibles.map((d) => (
              <option key={d.saldoId} value={d.saldoId}>
                {(d.folio ?? 'Sin folio') + ' · ' + (d.uuid ?? '')}
              </option>
            ))}
          </select>
        </div>
        <Button
          type="button"
          variant="secondary"
          disabled={!saldoId || recalcular.isPending}
          title={saldoId ? undefined : 'Elige la factura.'}
          onClick={() => ejecutar(false)}
        >
          Ver diferencia contra el XML
        </Button>
      </div>

      {error && (
        <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
          {error}
        </p>
      )}

      {previa && (
        <div className="space-y-3">
          {previa.polizaAjusteId ? (
            <p role="status" className="rounded-md bg-success-bg px-3 py-2.5 text-sm text-success-fg">
              Se registró la póliza de ajuste{' '}
              <Link to="/contabilidad/polizas/$id" params={{ id: previa.polizaAjusteId }} className="font-mono underline">
                {previa.polizaAjusteFolio ?? 'de ajuste'}
              </Link>
              ; la original no se anuló.
            </p>
          ) : previa.sinDiferencia ? (
            <p role="status" className="text-sm text-ink-muted">
              El IVA contabilizado coincide con el XML: no hay nada que corregir.
            </p>
          ) : null}
          {previa.diferencias.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Tratamiento</TableHead>
                  <TableHead className="text-right">IVA contabilizado</TableHead>
                  <TableHead className="text-right">IVA del XML</TableHead>
                  <TableHead className="text-right">Diferencia de IVA</TableHead>
                  <TableHead className="text-right">Diferencia de base</TableHead>
                  <TableHead className="text-right">A pagado</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {previa.diferencias.map((d) => (
                  <TableRow key={d.tratamiento}>
                    <TableCell>{d.tratamiento}</TableCell>
                    <TableCell className="text-right tabular-nums">{monto(d.ivaContabilizado)}</TableCell>
                    <TableCell className="text-right tabular-nums">{monto(d.ivaXml)}</TableCell>
                    <TableCell className="text-right tabular-nums">{monto(d.ivaDiferencia)}</TableCell>
                    <TableCell className="text-right tabular-nums">{monto(d.baseDiferencia)}</TableCell>
                    <TableCell className="text-right tabular-nums">{monto(d.ivaPagado)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
          {!previa.sinDiferencia && !previa.polizaAjusteId && (
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="iva-corregir-motivo">
                Motivo de la corrección<span aria-hidden="true"> *</span>
              </Label>
              <Textarea
                id="iva-corregir-motivo"
                rows={2}
                className="resize-none"
                maxLength={MOTIVO_ANULACION_MAXIMO}
                value={motivo}
                onChange={(e) => setMotivo(e.target.value)}
              />
              <div>
                <Button
                  type="button"
                  disabled={!puedeAplicar || recalcular.isPending}
                  title={puedeAplicar ? undefined : 'El motivo lleva al menos 10 caracteres.'}
                  onClick={() => ejecutar(true)}
                >
                  Aplicar corrección (póliza de ajuste)
                </Button>
              </div>
            </div>
          )}
        </div>
      )}
    </section>
  );
}
