import { useRef, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { toast } from 'sonner';
import { ArrowLeft, Loader2 } from 'lucide-react';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { apiRequest, esApiError } from '@/lib/api';
import { Label } from '@/components/ui/label';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useAplicarImportacion, usePerfilado, useVistaPrevia } from '../api/hooks';
import type { CuerpoImportacion, ErrorFila, Perfil, ResultadoAplicar, VistaPrevia } from '../api/types';
import { PerfilReporte } from '../components/PerfilReporte';
import { VistaPreviaTabla } from '../components/VistaPreviaTabla';
import { ArchivoInvalidoError, abrirArchivo, hojaSugerida, prepararHoja, type AliasColumnas, type HojaXlsx } from '../lib/archivo';
import { SELECT_CLASS } from '../lib/estilos';

const PASOS = ['1. Archivo', '2. Perfilado', '3. Vista previa y aplicar'] as const;

function mensajeError(e: unknown): string {
  if (e instanceof ArchivoInvalidoError) return e.message;
  if (esApiError(e)) {
    if (e.status === 403) return 'No tienes permiso para importar.';
    if (e.status >= 500) return `El servidor no pudo procesar el archivo${e.traceId ? ` (código ${e.traceId})` : ''}. Puedes reintentar.`;
    return e.problem.detail ?? e.problem.title;
  }
  return 'No se pudo completar la operación (sin conexión o error inesperado). Puedes reintentar.';
}

/** Asistente de importación en 3 pasos. Perfilado y vista previa no escriben; aplicar es transaccional e idempotente. */
export function ImportacionPage() {
  const puedeImportar = useHasPermission(PermisosCanonicos.ContabilidadCatalogoImportar);
  const perfilar = usePerfilado();
  const previsualizar = useVistaPrevia();
  const aplicar = useAplicarImportacion();

  const [paso, setPaso] = useState(0);
  const [cuerpo, setCuerpo] = useState<CuerpoImportacion | null>(null);
  const [perfil, setPerfil] = useState<Perfil | null>(null);
  const [vp, setVp] = useState<VistaPrevia | null>(null);
  const [resultado, setResultado] = useState<ResultadoAplicar | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [erroresAplicar, setErroresAplicar] = useState<ErrorFila[]>([]);
  // Una clave por archivo: reintentar el mismo aplicar no duplica; otro archivo, otra clave.
  const clave = useRef(crypto.randomUUID());
  // .xlsx con varias hojas: el usuario elige antes del perfilado. `desplazamiento` = filas previas al encabezado.
  const [libro, setLibro] = useState<{ nombre: string; hojas: HojaXlsx[]; alias: AliasColumnas } | null>(null);
  const [hojaIdx, setHojaIdx] = useState(0);
  const [desplazamiento, setDesplazamiento] = useState(0);
  const [aviso, setAviso] = useState<string | null>(null);

  if (!puedeImportar) {
    return (
      <div className="space-y-3 px-6 py-5">
        <h1 className="text-3xl font-semibold">Importación del catálogo</h1>
        <div role="alert" className="rounded-lg bg-surface-card shadow-card-flat p-4 text-sm text-ink-muted">
          No tienes permiso para importar el catálogo contable (contabilidad.catalogo.importar).
        </div>
      </div>
    );
  }

  async function elegirArchivo(file: File | undefined) {
    if (!file) return;
    setError(null);
    setErroresAplicar([]);
    setPerfil(null); setVp(null); setResultado(null); setLibro(null); setAviso(null); setDesplazamiento(0);
    clave.current = crypto.randomUUID();
    try {
      const abierto = await abrirArchivo(file);
      if (abierto.tipo === 'csv') return perfilarCuerpo(abierto.cuerpo);
      // Alias de columnas vigentes del servidor para reconocer el encabezado; si no se pueden leer, se usa la fila 1.
      let alias: AliasColumnas = {};
      try {
        const { data } = await apiRequest<{ importacion?: { columnas?: AliasColumnas } }>('/api/v1/contabilidad/configuracion-formato');
        alias = data.importacion?.columnas ?? {};
      } catch { /* ver aviso en prepararHoja: sin alias no se reconoce el encabezado */ }
      const l = { nombre: abierto.nombre, hojas: abierto.hojas, alias };
      setLibro(l);
      setHojaIdx(hojaSugerida(l.hojas, alias));
      if (l.hojas.length === 1) analizarHoja(l, 0);
    } catch (e) {
      setCuerpo(null);
      setError(mensajeError(e));
    }
  }

  function perfilarCuerpo(c: CuerpoImportacion) {
    setError(null);
    setCuerpo(c);
    perfilar.mutate(c, {
      onSuccess: (p) => { setPerfil(p); setPaso(1); },
      onError: (e) => setError(mensajeError(e)),
    });
  }

  function analizarHoja(l: NonNullable<typeof libro>, idx: number) {
    const h = prepararHoja(l.nombre, l.hojas[idx], l.alias);
    setDesplazamiento(h.desplazamiento);
    setAviso(
      h.sinEncabezadoReconocido
        ? 'No se reconoció una fila de encabezado en las primeras 15 filas; se usó la fila 1. Si el análisis falla, revisa que la hoja tenga columnas como «Numero» y «Cuenta».'
        : h.filaEncabezado > 1
          ? `Encabezado detectado en la fila ${h.filaEncabezado}; las ${h.filaEncabezado - 1} fila(s) anteriores se ignoraron. Los números de fila de los resultados son los del archivo.`
          : null,
    );
    perfilarCuerpo(h.cuerpo);
  }

  function irAVistaPrevia() {
    if (!cuerpo) return;
    setError(null);
    previsualizar.mutate(cuerpo, {
      onSuccess: (v) => { setVp(v); setPaso(2); },
      onError: (e) => setError(mensajeError(e)),
    });
  }

  function confirmarAplicar() {
    if (!cuerpo || !vp) return;
    setError(null);
    setErroresAplicar([]);
    aplicar.mutate({ cuerpo: { ...cuerpo, huella: vp.huella }, idempotencyKey: clave.current }, {
      // Toast SOLO tras 2xx.
      onSuccess: (r) => {
        setResultado(r);
        toast.success(r.idempotente ? 'Archivo ya aplicado: no hubo cambios' : 'Importación aplicada');
      },
      onError: (e) => {
        if (esApiError(e) && e.code === 'CONTAB_IMPORT_FILAS_CON_ERRORES') {
          setErroresAplicar((e.problem as unknown as { errores?: ErrorFila[] }).errores ?? []);
        }
        setError(mensajeError(e));
      },
    });
  }

  const ocupado = perfilar.isPending || previsualizar.isPending || aplicar.isPending;
  const hayErrores = !vp || !vp.puedeAplicar || vp.resumen.errores > 0;

  return (
    <div className="space-y-5 px-6 py-5">
      <Link to="/contabilidad/catalogo" className="inline-flex items-center gap-1 text-sm text-ink-muted hover:underline" data-print="hidden">
        <ArrowLeft className="size-4" aria-hidden="true" />Catálogo de cuentas
      </Link>
      <div>
        <h1 className="text-3xl font-semibold">Importación del catálogo</h1>
        <p className="text-sm text-ink-muted">Carga un archivo de cuentas en 3 pasos: perfilado de solo lectura, vista previa y aplicación idempotente.</p>
      </div>
      <ol className="flex flex-wrap gap-2 text-sm" aria-label="Pasos">
        {PASOS.map((p, i) => (
          <li key={p} aria-current={i === paso ? 'step' : undefined} className={i === paso ? 'font-semibold' : 'text-ink-muted'}>{p}</li>
        ))}
      </ol>

      {error && (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}

      {paso === 0 && (
        <section className="space-y-3">
          <p className="text-sm text-ink-muted">
            Archivo de texto plano (.csv UTF-8 o .xlsx) con los códigos como TEXTO y sin saldos. Primero se perfila (solo lectura).
          </p>
          <Input
            type="file"
            aria-label="Archivo del catálogo"
            accept=".csv,.xlsx"
            disabled={ocupado}
            onChange={(e) => void elegirArchivo(e.target.files?.[0])}
          />
          {libro && libro.hojas.length > 1 && !perfil && (
            <div className="flex max-w-xl flex-col gap-1.5">
              <Label htmlFor="hoja-xlsx">Hoja del libro</Label>
              <select id="hoja-xlsx" className={`w-full ${SELECT_CLASS}`} value={hojaIdx} onChange={(e) => setHojaIdx(Number(e.target.value))}>
                {libro.hojas.map((h, i) => (
                  <option key={h.nombre} value={i}>{h.nombre} — {h.filasConDatos} filas con datos</option>
                ))}
              </select>
              <p className="text-xs text-ink-muted">El libro tiene {libro.hojas.length} hojas. Elige la que contiene el catálogo de cuentas.</p>
              <div>
                <Button size="lg" onClick={() => analizarHoja(libro, hojaIdx)} disabled={ocupado}>Analizar hoja</Button>
              </div>
            </div>
          )}
          {perfilar.isPending && <p className="flex items-center gap-2 text-sm"><Loader2 className="size-4 animate-spin" aria-hidden="true" />Analizando archivo…</p>}
          {error && cuerpo && !perfilar.isPending && (
            <Button variant="outline" onClick={() => perfilar.mutate(cuerpo, { onSuccess: (p) => { setPerfil(p); setPaso(1); setError(null); }, onError: (e) => setError(mensajeError(e)) })}>
              Reintentar análisis
            </Button>
          )}
        </section>
      )}

      {paso === 1 && perfil && (
        <section className="space-y-4">
          <>
            {aviso && <p role="note" className="rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg">{aviso}</p>}
            <PerfilReporte perfil={perfil} desplazamiento={desplazamiento} />
          </>
          <div className="flex gap-2">
            <Button variant="ghost" size="lg" onClick={() => setPaso(0)} disabled={ocupado}>Elegir otro archivo</Button>
            <Button size="lg" onClick={irAVistaPrevia} disabled={ocupado}>
              {previsualizar.isPending ? 'Generando vista previa…' : 'Continuar a la vista previa'}
            </Button>
          </div>
        </section>
      )}

      {paso === 2 && vp && !resultado && (
        <section className="space-y-4">
          <VistaPreviaTabla vp={vp} desplazamiento={desplazamiento} />
          {erroresAplicar.length > 0 && (
            <ul aria-label="Errores al aplicar" className="list-disc pl-5 text-sm text-danger-fg">
              {erroresAplicar.map((e, i) => <li key={i}>Fila {e.fila + desplazamiento}: {e.mensaje}</li>)}
            </ul>
          )}
          <div className="flex items-center gap-2">
            <Button variant="ghost" size="lg" onClick={() => setPaso(1)} disabled={ocupado}>Volver al perfilado</Button>
            <Button size="lg" onClick={confirmarAplicar} disabled={hayErrores || ocupado} title={hayErrores ? 'El archivo tiene errores: corrígelos y vuelve a generar la vista previa.' : undefined}>
              {aplicar.isPending ? 'Aplicando…' : 'Aplicar importación'}
            </Button>
            {hayErrores && <span className="text-xs text-ink-muted">Corrige los errores del archivo para poder aplicar.</span>}
          </div>
        </section>
      )}

      {resultado && (
        <section aria-label="Resultado" className="space-y-3 rounded-lg bg-surface-card shadow-card-flat p-4">
          {resultado.idempotente ? (
            <p className="font-medium">Idempotente: ya aplicado. Este archivo ya se había importado; no se modificó nada.</p>
          ) : (
            <p className="font-medium">Importación aplicada.</p>
          )}
          {resultado.lote && (
            <dl className="grid grid-cols-2 gap-3 text-sm sm:grid-cols-4">
              <div><dt className="text-xs text-ink-muted">Creadas</dt><dd>{resultado.lote.creadas}</dd></div>
              <div><dt className="text-xs text-ink-muted">Actualizadas</dt><dd>{resultado.lote.actualizadas}</dd></div>
              <div><dt className="text-xs text-ink-muted">Sin cambios</dt><dd>{resultado.lote.sinCambios}</dd></div>
              <div><dt className="text-xs text-ink-muted">Total de filas</dt><dd>{resultado.lote.totalFilas}</dd></div>
            </dl>
          )}
          <Button asChild><Link to="/contabilidad/catalogo">Ver el catálogo</Link></Button>
        </section>
      )}
    </div>
  );
}
