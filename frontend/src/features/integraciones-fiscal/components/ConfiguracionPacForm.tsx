/* eslint-disable react-hooks/incompatible-library -- react-hook-form watch mantiene sincronizado el modo sandbox; React Compiler omite su memoización de forma segura. */
import { useEffect, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Checkbox } from '@/components/ui/checkbox';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  ProveedorPac,
  type ConfiguracionPacResponse,
} from '@/features/integraciones-fiscal/api/types';
import {
  useGuardarConfiguracionPac,
  useTestConexionPac,
} from '@/features/integraciones-fiscal/api/useIntegracionesFiscal';
import {
  GuardarConfiguracionPacSchema,
  type GuardarConfiguracionPacValues,
  type IdentidadSandboxValues,
} from '@/features/integraciones-fiscal/schemas/configuracion-pac';

/**
 * <c>&lt;ConfiguracionPacForm/&gt;</c> — form principal de la configuración
 * del PAC para una empresa.
 *
 * <para>
 * Modo CREAR (existing == null): apiKey es obligatorio. Modo EDITAR
 * (existing != null): apiKey vacío = no rotar; con valor = rota.
 * </para>
 *
 * <para>El botón "Probar conexión" prueba la configuración PERSISTIDA
 * (desde PR-13 el SDK resuelve credenciales por empresa internamente;
 * los campos transitorios del payload se ignoran). Flujo correcto:
 * guardar primero, probar después — el botón se deshabilita con
 * cambios sin guardar para hacerlo evidente (FAC-DET-PR6).</para>
 */
export interface ConfiguracionPacFormProps {
  empresaId: string;
  existing: ConfiguracionPacResponse | null;
}

/** URLs canónicas de FiscalAPI — el switch de modo sandbox alterna entre ambas. */
const FISCALAPI_LIVE_URL = 'https://live.fiscalapi.com';
const FISCALAPI_SANDBOX_URL = 'https://test.fiscalapi.com';

/**
 * Persona moral de prueba de la LCO sintética del SAT
 * (docs.fiscalapi.com/testing-data) — pre-llenado del emisor sandbox.
 * El SAT solo acepta personas de esa lista en el ambiente de pruebas.
 */
const IDENTIDAD_PRUEBA_EKU: IdentidadSandboxValues = {
  rfc: 'EKU9003173C9',
  razonSocial: 'ESCUELA KEMPER URGATE',
  regimenFiscal: '601',
  codigoPostal: '42501',
};

function identidadesIguales(
  a: IdentidadSandboxValues | null | undefined,
  b: IdentidadSandboxValues | null | undefined,
): boolean {
  if (!a || !b) return false;
  return (
    a.rfc === b.rfc &&
    a.razonSocial === b.razonSocial &&
    a.regimenFiscal === b.regimenFiscal &&
    a.codigoPostal === b.codigoPostal
  );
}

export function ConfiguracionPacForm({ empresaId, existing }: ConfiguracionPacFormProps) {
  const canAdministrar = useHasPermission(PermisosCanonicos.IntegracionesFiscalAdministrar);
  const guardar = useGuardarConfiguracionPac();
  const test = useTestConexionPac();
  const idempotencyKey = useFormIdempotencyKey();

  const defaultValues: GuardarConfiguracionPacValues = existing
    ? {
        baseUrl: existing.baseUrl,
        activo: existing.activo,
        emisorSandbox: existing.emisorSandbox ?? undefined,
        receptorSandbox:
          // Receptor igual al emisor se modela con el checkbox, no con
          // campos duplicados en el form.
          existing.receptorSandbox &&
          !identidadesIguales(existing.receptorSandbox, existing.emisorSandbox)
            ? existing.receptorSandbox
            : undefined,
      }
    : {
        baseUrl: 'https://live.fiscalapi.com',
        activo: true,
      };

  const form = useForm<GuardarConfiguracionPacValues>({
    resolver: zodResolver(GuardarConfiguracionPacSchema),
    defaultValues,
  });

  const [identidadesHabilitadas, setIdentidadesHabilitadas] = useState(
    existing?.emisorSandbox != null,
  );
  const [receptorIgualEmisor, setReceptorIgualEmisor] = useState(
    existing?.receptorSandbox == null ||
      identidadesIguales(existing.receptorSandbox, existing.emisorSandbox),
  );

  // Re-inicializa el form si cambia la empresa seleccionada.
  useEffect(() => {
    form.reset(defaultValues);
    setIdentidadesHabilitadas(existing?.emisorSandbox != null);
    setReceptorIgualEmisor(
      existing?.receptorSandbox == null ||
        identidadesIguales(existing.receptorSandbox, existing.emisorSandbox),
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [empresaId, existing?.id]);

  const [apiKeyDraft, setApiKeyDraft] = useState('');

  // CSD del emisor (.cer/.key en base64 + password). Drafts locales igual
  // que la API key: nunca se re-hidratan desde el backend (solo regresa
  // csdConfigurado) y se limpian tras guardar. Las tres piezas viajan
  // juntas o no viaja ninguna.
  const [csdCerDraft, setCsdCerDraft] = useState<string | null>(null);
  const [csdKeyDraft, setCsdKeyDraft] = useState<string | null>(null);
  const [csdPasswordDraft, setCsdPasswordDraft] = useState('');
  const csdDraftCompleto =
    csdCerDraft != null && csdKeyDraft != null && csdPasswordDraft.trim() !== '';
  const csdDraftParcial =
    !csdDraftCompleto &&
    (csdCerDraft != null || csdKeyDraft != null || csdPasswordDraft.trim() !== '');

  async function leerArchivoBase64(
    file: File | undefined,
    set: (base64: string | null) => void,
  ) {
    if (!file) {
      set(null);
      return;
    }
    const bytes = new Uint8Array(await file.arrayBuffer());
    let binario = '';
    for (const b of bytes) binario += String.fromCharCode(b);
    set(btoa(binario));
  }

  function habilitarIdentidades(v: boolean) {
    setIdentidadesHabilitadas(v);
    if (v) {
      form.setValue('emisorSandbox', form.getValues('emisorSandbox') ?? IDENTIDAD_PRUEBA_EKU, {
        shouldDirty: true,
      });
    } else {
      form.setValue('emisorSandbox', undefined, { shouldDirty: true });
      form.setValue('receptorSandbox', undefined, { shouldDirty: true });
      form.clearErrors(['emisorSandbox', 'receptorSandbox']);
    }
  }

  // El test de conexión usa la configuración PERSISTIDA (ver doc del
  // componente): con cambios sin guardar (form dirty, key pegada sin
  // guardar, o configuración aún no creada) probaría otra cosa distinta
  // de lo que el usuario ve — se deshabilita para forzar guardar→probar.
  const hayCambiosSinGuardar =
    existing == null ||
    form.formState.isDirty ||
    apiKeyDraft.trim() !== '' ||
    csdDraftCompleto ||
    csdDraftParcial;

  function onSubmit(values: GuardarConfiguracionPacValues) {
    if (csdDraftParcial) {
      toast.error(
        'El CSD requiere las tres piezas: certificado (.cer), llave privada (.key) y password.',
      );
      return;
    }

    const csdPayload =
      csdCerDraft != null && csdKeyDraft != null && csdPasswordDraft.trim() !== ''
        ? {
            certificadoBase64: csdCerDraft,
            llavePrivadaBase64: csdKeyDraft,
            password: csdPasswordDraft.trim(),
          }
        : null;

    const conIdentidades =
      values.baseUrl === FISCALAPI_SANDBOX_URL && identidadesHabilitadas;
    const emisorSandbox = conIdentidades ? (values.emisorSandbox ?? null) : null;
    const receptorSandbox = conIdentidades
      ? receptorIgualEmisor
        ? emisorSandbox
        : (values.receptorSandbox ?? null)
      : null;

    guardar.mutate(
      {
        empresaId,
        proveedor: ProveedorPac.FiscalApi,
        payload: {
          baseUrl: values.baseUrl,
          apiKey: apiKeyDraft.trim() === '' ? null : apiKeyDraft.trim(),
          activo: values.activo,
          emisorSandbox,
          receptorSandbox,
          csd: csdPayload,
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Configuración guardada.');
          setApiKeyDraft('');
          setCsdCerDraft(null);
          setCsdKeyDraft(null);
          setCsdPasswordDraft('');
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description: error.traceId ? `Código: ${error.traceId}` : undefined,
            });
            return;
          }
          toast.error('Error inesperado al guardar configuración.');
        },
      },
    );
  }

  function handleTest() {
    test.mutate(
      {
        empresaId,
        proveedor: ProveedorPac.FiscalApi,
        payload: {
          baseUrl: form.getValues('baseUrl'),
          apiKey: apiKeyDraft.trim() === '' ? null : apiKeyDraft.trim(),
          timeoutSegundos: 10,
        },
      },
      {
        onSuccess: (res) => {
          if (res.exitosa) {
            toast.success(`Conexión exitosa (${res.tiempoMs} ms).`);
          } else {
            toast.error(`Falla: ${res.mensaje}`, {
              description: `HTTP ${res.statusCode}`,
            });
          }
        },
        onError: () => {
          toast.error('Error inesperado al probar conexión.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-6 p-4">
      <fieldset disabled={!canAdministrar} className="space-y-6">
        {/* === Sección Conexión === */}
        <section className="space-y-3">
          <h3 className="text-sm font-semibold">Conexión</h3>

          {/* Modo sandbox (FAC-DET): alterna la Base URL entre el ambiente
              de pruebas y el productivo de FiscalAPI. Las credenciales
              sk_test SOLO funcionan contra test.fiscalapi.com (validado
              2026-07-08 — live responde 500 con ellas). El input de URL usa
              register(), así que el toggle escribe vía setValue — un
              Controller paralelo sobre el mismo name actualiza el estado
              pero NO el DOM del input no-controlado (fix FAC-DET-PR5). */}
          <div className="flex items-center gap-3">
            <Checkbox
              id="modoSandbox"
              checked={form.watch('baseUrl') === FISCALAPI_SANDBOX_URL}
              onCheckedChange={(v) => {
                form.setValue(
                  'baseUrl',
                  v === true ? FISCALAPI_SANDBOX_URL : FISCALAPI_LIVE_URL,
                  { shouldDirty: true, shouldValidate: true },
                );
                // Fuera de sandbox no hay identidades de prueba (el
                // backend también lo rechaza — invariante del dominio).
                if (v !== true) habilitarIdentidades(false);
              }}
            />
            <Label htmlFor="modoSandbox" className="text-sm">
              Modo sandbox (pruebas — requiere API key <code>sk_test…</code>)
            </Label>
          </div>

          <div className="space-y-1">
            <Label htmlFor="baseUrl">Base URL</Label>
            <Input
              id="baseUrl"
              placeholder="https://api.fiscalapi.com"
              {...form.register('baseUrl')}
            />
            {form.formState.errors.baseUrl && (
              <p className="text-xs text-destructive">
                {form.formState.errors.baseUrl.message}
              </p>
            )}
          </div>

          <div className="space-y-1">
            <Label htmlFor="apiKey">
              API Key {existing ? '(dejar vacío para no rotar)' : ''}
            </Label>
            <Input
              id="apiKey"
              type="password"
              autoComplete="new-password"
              placeholder={existing?.apiKeyConfigured ? '••••' : 'Pegar API key'}
              value={apiKeyDraft}
              onChange={(e) => setApiKeyDraft(e.target.value)}
            />
            {existing?.ultimaRotacionAt && (
              <p className="text-xs text-muted-foreground">
                Última rotación: {new Date(existing.ultimaRotacionAt).toLocaleString()}
              </p>
            )}
          </div>

          <div className="space-y-1">
            <div className="flex items-center gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={handleTest}
                disabled={test.isPending || !canAdministrar || hayCambiosSinGuardar}
              >
                {test.isPending && <Loader2 className="mr-1 h-3 w-3 animate-spin" />}
                Probar conexión
              </Button>
              {existing?.ultimaTestConexionAt && (
                <span className="text-xs text-muted-foreground">
                  Última prueba: {new Date(existing.ultimaTestConexionAt).toLocaleString()}{' '}
                  ({existing.ultimaTestConexionExitosa ? 'OK' : 'falló'})
                </span>
              )}
            </div>
            <p className="text-xs text-muted-foreground">
              {hayCambiosSinGuardar
                ? 'Guarda primero — la prueba usa la configuración guardada, no lo capturado en el form.'
                : 'Prueba la configuración guardada contra FiscalAPI.'}
            </p>
          </div>
        </section>

        {/*
          PR-13: Las secciones "Schedule" y "Resiliencia" se eliminaron.
          El SDK NuGet oficial maneja la resiliencia internamente y los
          workers asíncronos (Submitter + Poller) toman su schedule de
          configuración global del backend.
        */}

        {/* === Identidades de prueba (solo sandbox) === */}
        {form.watch('baseUrl') === FISCALAPI_SANDBOX_URL && (
          <section className="space-y-3">
            <h3 className="text-sm font-semibold">Identidades de prueba</h3>
            <p className="text-xs text-muted-foreground">
              El SAT solo acepta personas de su lista sintética en el ambiente
              de pruebas (docs.fiscalapi.com/testing-data). Al activar esto,
              los CFDI se timbran sustituyendo emisor y receptor por estas
              identidades — los datos reales de la empresa y del cliente no se
              tocan.
            </p>

            <div className="flex items-center gap-3">
              <Checkbox
                id="identidadesSandbox"
                checked={identidadesHabilitadas}
                onCheckedChange={(v) => habilitarIdentidades(v === true)}
              />
              <Label htmlFor="identidadesSandbox" className="text-sm">
                Sustituir emisor/receptor con identidades de prueba
              </Label>
            </div>

            {identidadesHabilitadas && (
              <>
                <div className="space-y-2 rounded-md border p-3">
                  <p className="text-xs font-medium">Emisor de prueba</p>
                  <IdentidadSandboxFields form={form} prefijo="emisorSandbox" />
                </div>

                <div className="flex items-center gap-3">
                  <Checkbox
                    id="receptorIgualEmisor"
                    checked={receptorIgualEmisor}
                    onCheckedChange={(v) => {
                      const igual = v === true;
                      setReceptorIgualEmisor(igual);
                      if (igual) {
                        form.setValue('receptorSandbox', undefined, { shouldDirty: true });
                        form.clearErrors('receptorSandbox');
                      } else {
                        form.setValue(
                          'receptorSandbox',
                          form.getValues('emisorSandbox') ?? IDENTIDAD_PRUEBA_EKU,
                          { shouldDirty: true },
                        );
                      }
                    }}
                  />
                  <Label htmlFor="receptorIgualEmisor" className="text-sm">
                    Receptor: usar la misma identidad que el emisor
                  </Label>
                </div>

                {!receptorIgualEmisor && (
                  <div className="space-y-2 rounded-md border p-3">
                    <p className="text-xs font-medium">Receptor de prueba</p>
                    <IdentidadSandboxFields form={form} prefijo="receptorSandbox" />
                  </div>
                )}
              </>
            )}
          </section>
        )}

        {/* === CSD del emisor (sellos SAT) === */}
        <section className="space-y-3">
          <h3 className="text-sm font-semibold">CSD del emisor (sellos SAT)</h3>
          <p className="text-xs text-muted-foreground">
            FiscalAPI exige el Certificado de Sello Digital en cada timbrado
            (emisión por valores). En sandbox usa el CSD de prueba del SAT
            (docs.fiscalapi.com/testing-data, password{' '}
            <code>12345678a</code>); en productivo, el CSD real de la empresa.
            Se guarda cifrado; para rotarlo vuelve a subir las tres piezas.
          </p>

          {existing?.csdConfigurado ? (
            // Estado prominente: los inputs de archivo NUNCA se re-hidratan
            // (los secretos no regresan al navegador) — esta línea es la
            // única evidencia visible de que el CSD quedó registrado.
            <div className="rounded-md border border-emerald-500/40 bg-emerald-500/10 px-3 py-2 text-sm">
              <span className="font-medium text-emerald-700 dark:text-emerald-400">
                ✓ CSD configurado y validado
              </span>
              {existing.csdActualizadoAt ? (
                <span className="text-muted-foreground">
                  {' — '}actualizado: {new Date(existing.csdActualizadoAt).toLocaleString()}
                </span>
              ) : null}
              <p className="text-xs text-muted-foreground">
                Los campos de archivo quedan vacíos por seguridad (el CSD no
                se re-descarga al navegador). Subir archivos nuevos lo
                reemplaza.
              </p>
            </div>
          ) : (
            <p className="text-xs font-medium text-destructive">
              Sin CSD — el timbrado fallará con CSD_NO_CONFIGURADO hasta
              capturarlo.
            </p>
          )}

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div className="space-y-1">
              <Label htmlFor="csdCer">Certificado (.cer)</Label>
              <Input
                id="csdCer"
                type="file"
                accept=".cer"
                onChange={(e) => void leerArchivoBase64(e.target.files?.[0], setCsdCerDraft)}
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor="csdKey">Llave privada (.key)</Label>
              <Input
                id="csdKey"
                type="file"
                accept=".key"
                onChange={(e) => void leerArchivoBase64(e.target.files?.[0], setCsdKeyDraft)}
              />
            </div>
          </div>

          <div className="space-y-1">
            <Label htmlFor="csdPassword">Password de la llave</Label>
            <Input
              id="csdPassword"
              type="password"
              autoComplete="new-password"
              placeholder={existing?.csdConfigurado ? '••••' : 'Password del CSD'}
              value={csdPasswordDraft}
              onChange={(e) => setCsdPasswordDraft(e.target.value)}
            />
            {csdDraftParcial && (
              <p className="text-xs text-destructive">
                Faltan piezas del CSD: sube .cer, .key y captura el password
                para guardarlo.
              </p>
            )}
          </div>
        </section>

        {/* === Toggle activo === */}
        <section className="space-y-1">
          <div className="flex items-center gap-3">
            <Controller
              control={form.control}
              name="activo"
              render={({ field }) => (
                <Checkbox
                  id="activo"
                  checked={field.value}
                  onCheckedChange={(v) => field.onChange(v === true)}
                />
              )}
            />
            <Label htmlFor="activo" className="text-sm">
              Configuración activa (workers la procesan)
            </Label>
          </div>
        </section>

        <div className="flex justify-end gap-2 border-t pt-4">
          <Button type="submit" disabled={guardar.isPending}>
            {guardar.isPending && <Loader2 className="mr-1 h-3 w-3 animate-spin" />}
            {existing ? 'Guardar cambios' : 'Crear configuración'}
          </Button>
        </div>
      </fieldset>

      {!canAdministrar && (
        <p className="text-xs text-muted-foreground">
          Solo lectura — se requiere el permiso{' '}
          <code>integraciones.fiscal.administrar</code> para editar.
        </p>
      )}
    </form>
  );
}

/** Cuarteto RFC / razón social / régimen / CP de una identidad de prueba. */
function IdentidadSandboxFields({
  form,
  prefijo,
}: {
  form: ReturnType<typeof useForm<GuardarConfiguracionPacValues>>;
  prefijo: 'emisorSandbox' | 'receptorSandbox';
}) {
  const errores = form.formState.errors[prefijo];
  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
      <div className="space-y-1">
        <Label htmlFor={`${prefijo}.rfc`}>RFC</Label>
        <Input
          id={`${prefijo}.rfc`}
          placeholder="EKU9003173C9"
          {...form.register(`${prefijo}.rfc`)}
        />
        {errores?.rfc && (
          <p className="text-xs text-destructive">{errores.rfc.message}</p>
        )}
      </div>
      <div className="space-y-1">
        <Label htmlFor={`${prefijo}.razonSocial`}>Razón social (sin régimen societario)</Label>
        <Input
          id={`${prefijo}.razonSocial`}
          placeholder="ESCUELA KEMPER URGATE"
          {...form.register(`${prefijo}.razonSocial`)}
        />
        {errores?.razonSocial && (
          <p className="text-xs text-destructive">{errores.razonSocial.message}</p>
        )}
      </div>
      <div className="space-y-1">
        <Label htmlFor={`${prefijo}.regimenFiscal`}>Régimen fiscal</Label>
        <Input
          id={`${prefijo}.regimenFiscal`}
          placeholder="601"
          {...form.register(`${prefijo}.regimenFiscal`)}
        />
        {errores?.regimenFiscal && (
          <p className="text-xs text-destructive">{errores.regimenFiscal.message}</p>
        )}
      </div>
      <div className="space-y-1">
        <Label htmlFor={`${prefijo}.codigoPostal`}>Código postal</Label>
        <Input
          id={`${prefijo}.codigoPostal`}
          placeholder="42501"
          {...form.register(`${prefijo}.codigoPostal`)}
        />
        {errores?.codigoPostal && (
          <p className="text-xs text-destructive">{errores.codigoPostal.message}</p>
        )}
      </div>
    </div>
  );
}
