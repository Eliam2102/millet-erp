import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useResolverSolicitudCatalogo, useSolicitudesCatalogo } from '../api/hooks';
import type { SolicitudCatalogo } from '../api/types';
import { diferenciasCatalogo, valorDiferencia } from '../lib/diferencias-catalogo';

export function SolicitudesCatalogo() {
  const [estado, setEstado] = useState('Pendiente');
  const [offset, setOffset] = useState(0);
  const consulta = useSolicitudesCatalogo(estado, offset);
  return (
    <section aria-label="Solicitudes del catálogo" className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="text-lg font-semibold">Autorizaciones del catálogo</h2>
      <p className="text-sm text-ink-muted">Revisa los cambios propuestos. El catálogo vigente cambia al autorizar; el rechazo conserva el motivo.</p>
      <Select value={estado} onValueChange={(v) => { setEstado(v); setOffset(0); }}>
        <SelectTrigger aria-label="Estado de solicitudes"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="Pendiente">Pendientes</SelectItem>
          <SelectItem value="Autorizada">Autorizadas</SelectItem>
          <SelectItem value="Rechazada">Rechazadas</SelectItem>
        </SelectContent>
      </Select>
      {consulta.isPending && <p role="status">Cargando solicitudes…</p>}
      {consulta.isError && <p role="alert">No se pudieron cargar las solicitudes. <Button variant="ghost" onClick={() => void consulta.refetch()}>Reintentar</Button></p>}
      {consulta.data?.items.length === 0 && <p>No hay solicitudes en este estado.</p>}
      {consulta.data?.items.map((s) => <Solicitud key={s.id} solicitud={s} />)}
      {consulta.data && <div className="flex items-center justify-between text-xs text-ink-muted">
        <span>{consulta.data.total} solicitudes</span>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" disabled={offset === 0} title={offset === 0 ? 'Primera página' : undefined} onClick={() => setOffset(offset - 10)}>Anterior</Button>
          <Button variant="outline" size="sm" disabled={offset + 10 >= consulta.data.total} title={offset + 10 >= consulta.data.total ? 'Última página' : undefined} onClick={() => setOffset(offset + 10)}>Siguiente</Button>
        </div>
      </div>}
    </section>
  );
}

function Solicitud({ solicitud: s }: { solicitud: SolicitudCatalogo }) {
  const usuarioId = useAuthStore((a) => a.user?.id);
  const permiso = useHasPermission(PermisosCanonicos.ContabilidadCatalogoAutorizar);
  const resolver = useResolverSolicitudCatalogo();
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string | null>(null);
  const propia = usuarioId === s.preparadaPorId;
  const pendiente = s.estado === 'Pendiente';
  function decidir(autorizar: boolean) {
    setError(null);
    resolver.mutate({ id: s.id, version: s.version, autorizar, motivo }, {
      onSuccess: () => toast.success(autorizar ? 'Solicitud autorizada; catálogo actualizado' : 'Solicitud rechazada; motivo guardado'),
      onError: (e) => setError(e.message),
    });
  }
  return (
    <article className="space-y-3 rounded-lg border border-line p-4 text-sm">
      <div className="flex flex-wrap items-center gap-2">
        <h3 className="font-semibold">{s.operacion === 'Reactivacion' ? 'Reactivación' : s.operacion === 'Importacion' ? 'Importación por lote' : s.operacion}</h3>
        <Badge variant={pendiente ? 'warning' : s.estado === 'Autorizada' ? 'success' : 'danger'}>{s.estado}</Badge>
        <span className="text-ink-muted">Preparó {s.preparadaPor} · {new Date(s.preparadaEn).toLocaleString('es-MX')}</span>
      </div>
      <details>
        <summary className="cursor-pointer text-brand">Ver antes y después ({s.cambios.length} cuentas)</summary>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="bg-surface-subtle text-ink-muted"><tr><th className="p-2">Cuenta</th><th className="p-2">Dato</th><th className="p-2">Antes</th><th className="p-2">Después</th></tr></thead>
            <tbody>{s.cambios.flatMap((c) => diferenciasCatalogo(c).map((d) => (
              <tr key={`${c.despues.id}-${d.campo}`} className="border-b border-line-row">
                <td className="p-2"><span className="font-mono">{c.despues.codigo}</span> · {c.despues.nombre}</td>
                <td className="p-2">{d.etiqueta}</td><td className="p-2">{valorDiferencia(d.antes)}</td><td className="p-2">{valorDiferencia(d.despues)}</td>
              </tr>
            )))}</tbody>
          </table>
        </div>
      </details>
      {s.resueltaEn && <p>Resolvió {s.resueltaPor} · {new Date(s.resueltaEn).toLocaleString('es-MX')}</p>}
      {s.motivoRechazo && <p>Motivo: {s.motivoRechazo}</p>}
      {pendiente && !permiso && <p className="text-ink-muted">Se requiere permiso de autorización del DAF para resolver.</p>}
      {pendiente && permiso && <div className="space-y-2">
        {propia && <p className="text-warning-fg">Preparaste esta solicitud. Otra persona debe autorizarla, incluso si eres superadministrador.</p>}
        <Label htmlFor={`motivo-${s.id}`}>Motivo del rechazo</Label>
        <Textarea id={`motivo-${s.id}`} value={motivo} maxLength={1000} onChange={(e) => setMotivo(e.target.value)} />
        <div className="flex gap-2">
          <Button disabled={propia || resolver.isPending} title={propia ? 'Otra persona debe autorizar la solicitud' : resolver.isPending ? 'Resolución en curso' : undefined} onClick={() => decidir(true)}>Autorizar</Button>
          <Button variant="secondary-danger" disabled={!motivo.trim() || resolver.isPending} title={!motivo.trim() ? 'Indica el motivo del rechazo' : resolver.isPending ? 'Resolución en curso' : undefined} onClick={() => decidir(false)}>Rechazar</Button>
        </div>
      </div>}
      {error && <p role="alert" className="text-danger-fg">{error}</p>}
    </article>
  );
}
