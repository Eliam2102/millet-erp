import { File as NodeFile } from 'node:buffer';

/**
 * `request.formData()` para handlers de MSW en jsdom. Con Node 24, el parser multipart de undici crea
 * los archivos con el `File` global (el de jsdom) y luego exige el de Node; mientras lee, el global es el de Node.
 */
export async function leerFormData(request: Request): Promise<FormData> {
  const previo = globalThis.File;
  globalThis.File = NodeFile as unknown as typeof File;
  try {
    return await request.formData();
  } finally {
    globalThis.File = previo;
  }
}
