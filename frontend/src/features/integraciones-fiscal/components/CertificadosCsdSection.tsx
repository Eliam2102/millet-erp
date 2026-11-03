import { useState } from 'react';
import { toast } from 'sonner';
import { DocumentoAdjuntosSection } from '@/components/erp/adjuntos/DocumentoAdjuntosSection';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type { CertificadoCsdItem, ProveedorPac } from '@/features/integraciones-fiscal/api/types';
import {
  useCertificadosCsd,
  useRevocarCsd,
} from '@/features/integraciones-fiscal/api/useIntegracionesFiscal';
import { codigoSoporte, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';

const VARIANTE = { Vigente: 'success', Sustituido: 'secondary', Revocado: 'danger' } as const;
const fecha = (iso: string | null) => (iso ? new Date(iso).toLocaleDateString('es-MX') : '—');

/**
 * <c>&lt;CertificadosCsdSection/&gt;</c> — historial de CSD de la empresa
 * (F6.9, R24): alta, sustitución y revocación, avisos enviados y evidencia
 * adjunta por certificado. Revocar deja de timbrar con el CSD en uso.
 */
export function CertificadosCsdSection({
  empresaId,
  proveedor,
}: {
  empresaId: string;
  proveedor: ProveedorPac;
}) {
  const historial = useCertificadosCsd(empresaId, proveedor);
  const puedeAdministrar = useHasPermission(PermisosCanonicos.IntegracionesFiscalAdministrar);
  const [evidencia, setEvidencia] = useState<string | null>(null);
  const certificados = historial.data ?? [];

  return (
    <section className="space-y-3">
      <h3 className="text-sm font-semibold">Historial de certificados (CSD)</h3>
      <p className="text-xs text-muted-foreground">
        Avisos a Fiscal y TI a 30, 15 y 5 días del vencimiento y el día que vence. Con un CSD
        vencido o revocado el ERP no timbra.
      </p>
      {historial.isLoading ? (
        <p role="status" className="text-xs text-muted-foreground">
          Cargando historial…
        </p>
      ) : !certificados.length ? (
        <p className="text-xs text-muted-foreground">
          Aún no hay certificados en el historial. Se registra al capturar el CSD.
        </p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Número de certificado</TableHead>
              <TableHead>Vigencia</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead>Movimientos</TableHead>
              <TableHead>Avisos enviados</TableHead>
              <TableHead>Evidencia</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {certificados.map((c) => (
              <FilaCertificado
                key={c.id}
                c={c}
                abierto={evidencia === c.id}
                onEvidencia={() => setEvidencia((e) => (e === c.id ? null : c.id))}
              />
            ))}
          </TableBody>
        </Table>
      )}
      {evidencia && (
        <DocumentoAdjuntosSection
          base={`/api/v1/integraciones/fiscal/certificados/${evidencia}`}
          tipo="certificado_csd"
          permisos={{
            ver: PermisosCanonicos.IntegracionesFiscalLeer,
            subir: PermisosCanonicos.IntegracionesFiscalAdministrar,
          }}
        />
      )}
      {puedeAdministrar && certificados.some((c) => c.estado === 'Vigente') && (
        <RevocarCsd empresaId={empresaId} proveedor={proveedor} />
      )}
    </section>
  );
}

function FilaCertificado({
  c,
  abierto,
  onEvidencia,
}: {
  c: CertificadoCsdItem;
  abierto: boolean;
  onEvidencia: () => void;
}) {
  return (
    <TableRow>
      <TableCell>
        <p className="font-mono text-xs">{c.numeroSerie}</p>
        {c.rfc && <p className="text-xs text-muted-foreground">{c.rfc}</p>}
      </TableCell>
      <TableCell className="text-xs">
        {fecha(c.vigenteDesde)} – {fecha(c.venceEn)}
      </TableCell>
      <TableCell>
        <Badge variant={VARIANTE[c.estado]}>{c.estado}</Badge>
        {c.motivoRevocacion && <p className="text-xs text-danger-fg">{c.motivoRevocacion}</p>}
      </TableCell>
      <TableCell className="text-xs">
        <p>
          Alta {fecha(c.registradoEn)}
          {c.registradoPor ? ` · ${c.registradoPor}` : ''}
        </p>
        {c.finalizadoEn && (
          <p>
            {c.estado === 'Revocado' ? 'Revocado' : 'Sustituido'} {fecha(c.finalizadoEn)}
            {c.finalizadoPor ? ` · ${c.finalizadoPor}` : ''}
          </p>
        )}
      </TableCell>
      <TableCell className="text-xs">
        {c.alertasEnviadas.length
          ? c.alertasEnviadas.map((d) => (d === 0 ? 'día del vencimiento' : `${d} días`)).join(', ')
          : '—'}
      </TableCell>
      <TableCell>
        <Button variant="ghost" size="sm" onClick={onEvidencia} aria-expanded={abierto}>
          {abierto ? 'Ocultar' : 'Ver evidencia'}
        </Button>
      </TableCell>
    </TableRow>
  );
}

function RevocarCsd({ empresaId, proveedor }: { empresaId: string; proveedor: ProveedorPac }) {
  const [abierto, setAbierto] = useState(false);
  const [motivo, setMotivo] = useState('');
  const idempotencyKey = useFormIdempotencyKey();
  const revocar = useRevocarCsd();

  function enviar() {
    if (motivo.trim() === '') {
      toast.error('Indica el motivo de la revocación.');
      return;
    }
    revocar.mutate(
      { empresaId, proveedor, motivo: motivo.trim(), idempotencyKey },
      {
        onSuccess: () => {
          toast.success('CSD revocado: el ERP ya no timbra con él. Captura un CSD vigente.');
          setAbierto(false);
          setMotivo('');
        },
        onError: (error) =>
          toast.error(esApiError(error) ? error.problem.title : 'No se pudo revocar el CSD.', {
            description: esApiError(error)
              ? (error.problem.detail ?? codigoSoporte(error))
              : undefined,
          }),
      },
    );
  }

  if (!abierto)
    return (
      <Button variant="outline" size="sm" className="text-danger-fg" onClick={() => setAbierto(true)}>
        Revocar CSD en uso
      </Button>
    );

  return (
    <div className="space-y-2 rounded-md border border-dashed border-line p-3">
      <Label htmlFor="motivoRevocacion">Motivo de la revocación *</Label>
      <Input
        id="motivoRevocacion"
        value={motivo}
        maxLength={500}
        onChange={(e) => setMotivo(e.target.value)}
        placeholder="p. ej. llave comprometida o revocado por el SAT"
      />
      <div className="flex justify-end gap-2">
        <Button variant="ghost" size="sm" onClick={() => setAbierto(false)} disabled={revocar.isPending}>
          Cancelar
        </Button>
        <Button variant="destructive" size="sm" onClick={enviar} disabled={revocar.isPending}>
          {revocar.isPending ? 'Revocando…' : 'Revocar'}
        </Button>
      </div>
    </div>
  );
}
