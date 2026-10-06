import { useState } from 'react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { useCrearTipoDocumento, useEditarTipoDocumento, useTiposDocumento } from '../../api/dimensiones';
import { mensajeError } from '../../lib/dimensiones';

/** Catálogo de tipos de documento contable. Hasta recibir la lista de Millet solo hay tipos de prueba (clave FIX-…). */
export function TiposDocumentoTab({ puedeAdministrar }: { puedeAdministrar: boolean }) {
  const tipos = useTiposDocumento(true);
  const crear = useCrearTipoDocumento();
  const editar = useEditarTipoDocumento();
  const [clave, setClave] = useState('');
  const [nombre, setNombre] = useState('');
  const [esPrueba, setEsPrueba] = useState(true);
  const [error, setError] = useState('');

  function agregar(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    crear.mutate(
      { clave: clave.trim(), nombre: nombre.trim(), esPrueba },
      { onSuccess: () => { setClave(''); setNombre(''); }, onError: (x) => setError(mensajeError(x)) },
    );
  }

  return (
    <div className="flex flex-col gap-4">
      {puedeAdministrar && (
        <form onSubmit={agregar} className="flex flex-wrap items-end gap-3 rounded-lg border border-dashed border-brand p-4" data-print="hidden">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="td-clave">Clave</Label>
            <Input id="td-clave" required maxLength={20} className="w-40 font-mono" value={clave} onChange={(e) => setClave(e.target.value)} placeholder="FIX-FP" />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="td-nombre">Nombre</Label>
            <Input id="td-nombre" required maxLength={120} className="w-80" value={nombre} onChange={(e) => setNombre(e.target.value)} placeholder="[TIPO DE DOCUMENTO]" />
          </div>
          <div className="flex h-ctl-xl items-center gap-2">
            <Checkbox id="td-prueba" checked={esPrueba} onCheckedChange={(v) => setEsPrueba(v === true)} />
            <Label htmlFor="td-prueba" className="font-normal">De prueba</Label>
          </div>
          <Button type="submit" disabled={crear.isPending}>{crear.isPending ? 'Agregando…' : 'Agregar tipo'}</Button>
          {error && <p role="alert" className="basis-full text-sm text-danger-fg">{error}</p>}
        </form>
      )}
      <div className="overflow-hidden rounded-lg bg-surface-card shadow-card-flat">
        {tipos.isLoading ? (
          <div className="p-4" aria-label="Cargando tipos"><Skeleton className="h-8 w-full" /></div>
        ) : tipos.isError ? (
          <p role="alert" className="p-4 text-sm text-danger-fg">No se pudieron cargar los tipos de documento.</p>
        ) : tipos.data!.length === 0 ? (
          <p className="p-6 text-sm text-ink-muted">Aún no hay tipos de documento. La lista oficial la entrega Contabilidad de Millet.</p>
        ) : (
          <table className="w-full text-sm">
            <thead className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
              <tr><th className="px-3 py-2">Clave</th><th className="px-3 py-2">Nombre</th><th className="px-3 py-2">Estado</th>{puedeAdministrar && <th className="px-3 py-2"><span className="sr-only">Acciones</span></th>}</tr>
            </thead>
            <tbody>
              {tipos.data!.map((t) => (
                <tr key={t.id} className="border-b border-line-row">
                  <td className="px-3 py-2 font-mono text-xs">{t.clave}</td>
                  <td className="px-3 py-2">{t.nombre}</td>
                  <td className="px-3 py-2">
                    <span className="flex gap-1">
                      <Badge variant={t.activo ? 'success' : 'neutral'}>{t.activo ? 'Activo' : 'Inactivo'}</Badge>
                      {t.esPrueba && <Badge variant="warning">Prueba</Badge>}
                    </span>
                  </td>
                  {puedeAdministrar && (
                    <td className="px-3 py-2 text-right">
                      <Button variant="outline" size="sm" disabled={editar.isPending} onClick={() => editar.mutate({ ...t, nuevoActivo: !t.activo })}>
                        {t.activo ? 'Desactivar' : 'Reactivar'}
                      </Button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
