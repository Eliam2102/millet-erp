import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import {
  useAdjuntos,
  useSubirAdjunto,
  useEnlaceAdjunto,
} from '@/components/erp/adjuntos/api/hooks';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { FormGrid, FormCampo } from '@/components/erp/forms/FormGrid';
import { formatDate } from '@/lib/datetime';
import { AF_BASE, useGuardarActivo } from '../api/hooks';
import { useConservacion, tiposDocumento, type DocumentoActivo } from '../api/conservacion';
import type { Activo } from '../api/types';
export function DocumentosActivo({ activo }: { activo: Activo }) {
  const base = `${AF_BASE}/activos/${activo.id}`;
  const query = useConservacion<DocumentoActivo[]>(`activos/${activo.id}/documentos`);
  const archivos = useAdjuntos(base);
  const subir = useSubirAdjunto(base);
  const descargar = useEnlaceAdjunto(base);
  const guardar = useGuardarActivo();
  const permiso = useHasPermission(P.ActivosFijosDocumentosGestionar);
  const [tipo, setTipo] = useState(0);
  const [reemplaza, setReemplaza] = useState('');
  const [adjunto, setAdjunto] = useState('');
  const [error, setError] = useState('');
  const [pagina, setPagina] = useState(0);
  const vigencia = [1, 2, 3].includes(tipo);
  return (
    <section className="space-y-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="text-lg font-semibold">Documentos y versiones</h2>
      {query.isPending && <p>Cargando documentos…</p>}
      {(query.error || archivos.error) && (
        <p role="alert">{query.error?.message ?? archivos.error?.message}</p>
      )}
      {query.data?.length === 0 && (
        <p className="text-ink-muted">Aún no hay documentos registrados.</p>
      )}
      {query.data?.slice(pagina * 10, pagina * 10 + 10).map((d) => (
        <article key={d.id} className="space-y-2 border-b border-line-row py-3">
          <p>
            {tiposDocumento[d.tipo]} · <span className="font-mono">{d.folio}</span> · Versión{' '}
            {d.numeroVersion}{' '}
            <Badge variant={d.reemplazado ? 'neutral' : 'success'}>
              {d.reemplazado ? 'Reemplazada' : 'Vigente'}
            </Badge>
          </p>
          <p>
            {d.emisor} · Emisión: {formatDate(d.fechaEmision)} · Vigencia:{' '}
            {d.vigenteDesde ? formatDate(d.vigenteDesde) : '—'} a{' '}
            {d.vigenteHasta ? formatDate(d.vigenteHasta) : '—'}
          </p>
          {d.notas && <p>{d.notas}</p>}
          <Button
            variant="outline"
            onClick={() =>
              void descargar
                .mutateAsync({ adjuntoId: d.adjuntoId })
                .catch((e) => setError(String(e)))
            }
          >
            Descargar versión {d.numeroVersion}
          </Button>
          {permiso && !d.reemplazado && (
            <Button
              variant="ghost"
              onClick={() => {
                setTipo(d.tipo);
                setReemplaza(d.id);
                setAdjunto('');
              }}
            >
              Reemplazar documento
            </Button>
          )}
        </article>
      ))}
      <div className="flex gap-2">
        <Button
          variant="outline"
          disabled={pagina === 0}
          title="Página anterior"
          onClick={() => setPagina(pagina - 1)}
        >
          Anterior
        </Button>
        <Button
          variant="outline"
          disabled={(pagina + 1) * 10 >= (query.data?.length ?? 0)}
          title="Página siguiente"
          onClick={() => setPagina(pagina + 1)}
        >
          Siguiente
        </Button>
      </div>
      {permiso && (
        <form
          className="space-y-3"
          onSubmit={async (e) => {
            e.preventDefault();
            const f = new FormData(e.currentTarget);
            setError('');
            try {
              await guardar.mutateAsync({
                ruta: `activos/${activo.id}/documentos`,
                version: activo.version,
                body: {
                  tipo,
                  adjuntoId: adjunto,
                  folio: f.get('folio'),
                  emisor: f.get('emisor'),
                  fechaEmision: f.get('fecha'),
                  vigenteDesde: f.get('desde') || null,
                  vigenteHasta: f.get('hasta') || null,
                  notas: f.get('notas') || null,
                  reemplazaA: reemplaza || null,
                },
              });
              setReemplaza('');
              setAdjunto('');
            } catch (err) {
              setError((err as Error).message);
            }
          }}
        >
          <h3 className="font-semibold">
            {reemplaza ? 'Nueva versión del documento' : 'Registrar documento'}
          </h3>
          <FormGrid>
            <FormCampo span={6}>
              <label className="text-xs">
                Tipo *
                <select
                  value={tipo}
                  disabled={!!reemplaza}
                  onChange={(e) => {
                    setTipo(Number(e.target.value));
                    setAdjunto('');
                  }}
                  className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3"
                >
                  {tiposDocumento.map((t, i) => (
                    <option key={t} value={i}>
                      {t}
                    </option>
                  ))}
                </select>
              </label>
            </FormCampo>
            <FormCampo span={6}>
              <label className="text-xs">
                Archivo *
                <Input
                  type="file"
                  disabled={subir.isUploading}
                  onChange={async (e) => {
                    const archivo = e.target.files?.[0];
                    if (!archivo) return;
                    setError('');
                    try {
                      const r = await subir.subir({
                        archivo,
                        tipoDocumentoId: `0000000f-0003-0000-0000-${String(tipo + 1).padStart(12, '0')}`,
                      });
                      setAdjunto(r.id);
                    } catch (err) {
                      setError((err as Error).message);
                    }
                  }}
                />
              </label>
            </FormCampo>
            <FormCampo span={12}>
              <label className="text-xs">
                Archivo cargado
                <select
                  required
                  value={adjunto}
                  onChange={(e) => setAdjunto(e.target.value)}
                  className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3"
                >
                  <option value="">Seleccione un archivo</option>
                  {archivos.data
                    ?.filter(
                      (x) =>
                        x.tipoDocumentoId ===
                          `0000000f-0003-0000-0000-${String(tipo + 1).padStart(12, '0')}` &&
                        !query.data?.some((d) => d.adjuntoId === x.id),
                    )
                    .map((x) => (
                      <option key={x.id} value={x.id}>
                        {x.nombreArchivo}
                      </option>
                    ))}
                </select>
              </label>
            </FormCampo>
            {(
              [
                ['folio', 'Número o folio', 'text'],
                ['emisor', 'Emisor', 'text'],
                ['fecha', 'Fecha de emisión', 'date'],
                ['desde', 'Vigencia desde', 'date'],
                ['hasta', 'Vigencia hasta', 'date'],
                ['notas', 'Notas', 'text'],
              ] as const
            ).map(([name, label, type]) => (
              <FormCampo key={name} span={6}>
                <label className="text-xs">
                  {label}
                  {name !== 'notas' && (vigencia || !['desde', 'hasta'].includes(name)) ? ' *' : ''}
                  <Input
                    name={name}
                    type={type}
                    required={name !== 'notas' && (vigencia || !['desde', 'hasta'].includes(name))}
                    maxLength={name === 'notas' ? 2000 : name === 'emisor' ? 200 : 100}
                  />
                </label>
              </FormCampo>
            ))}
          </FormGrid>
          <Button
            type="submit"
            disabled={!adjunto || guardar.isPending || subir.isUploading}
            title="Seleccione un archivo cargado para guardar"
          >
            Guardar documento
          </Button>
          {reemplaza && (
            <Button
              type="button"
              variant="ghost"
              onClick={() => {
                setReemplaza('');
                setAdjunto('');
              }}
            >
              Cancelar reemplazo
            </Button>
          )}
        </form>
      )}
      {error && (
        <p role="alert" className="text-danger-fg">
          {error}
        </p>
      )}
    </section>
  );
}
