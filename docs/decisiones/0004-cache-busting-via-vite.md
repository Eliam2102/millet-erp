# ADR-0004: Cache busting de assets vía Vite

- **Estado**: Aceptada
- **Fecha**: 2026-05-01
- **Decisores**: Eduardo Paredes
- **Etiquetas**: frontend, despliegue, fundación

## Contexto y problema

Cada vez que se despliega una nueva versión del frontend, los navegadores de
los usuarios pueden seguir usando archivos cacheados de la versión anterior
(JavaScript, CSS, fuentes), provocando errores extraños como métodos llamados
que ya no existen, o estilos rotos. En proyectos PHP es común manejar esto
con un parámetro de querystring (`app.css?v=1.2.3`) que el equipo bumpea
manualmente en cada deploy.

Necesitamos una estrategia que sea automática (cero trabajo manual) y
correcta (imposible servir versiones inconsistentes).

## Drivers de la decisión

- Cero pasos manuales en cada despliegue
- Imposible cachear una mezcla de versiones (todos los assets de un deploy van juntos o ninguno)
- Compatibilidad con CDN (los assets pueden cachearse de forma agresiva sin riesgo)

## Opciones consideradas

1. Hashed filenames automáticos de Vite + headers de caché diferenciados
2. Querystring con número de versión manual (estilo PHP)
3. Service Worker que invalida caché en deploy

## Decisión

Se adopta el **mecanismo nativo de Vite**: en cada `npm run build`, Vite genera
nombres de archivo con hash de contenido:

```
dist/assets/index-a1b2c3d4.js
dist/assets/main-e5f6g7h8.css
```

Cualquier cambio en el código produce un hash distinto, así que los nombres
de los archivos cambian. El `index.html` referencia los nombres nuevos. Los
navegadores no pueden servir versiones cacheadas porque el archivo ya tiene
otro nombre.

A esto se le acompaña una política de caché clara en el hosting:

- `index.html` → `Cache-Control: no-cache` (siempre revalidar)
- `assets/*.{js,css,woff2,png,jpg,svg,...}` → `Cache-Control: public, max-age=31536000, immutable`

## Consecuencias

**Positivas**
- Cero trabajo manual en cada deploy: el hash sale del contenido
- Imposible inconsistencia: si el `index.html` se cargó, todos los assets que referencia existen y son los correctos
- CDN puede cachear assets eternamente: nunca cambian (literalmente, son archivos distintos)
- Carga muy rápida en navegaciones repetidas: assets cacheados localmente, solo se descarga `index.html`

**Negativas**
- Configuración inicial de headers de caché en el hosting (Static Web App o Blob + CDN). Es trabajo único.
- Si por error se sirve `index.html` cacheado, se rompe todo. Por eso el header `no-cache` es crítico.

## Descartadas

**Querystring manual** (`app.js?v=1.2.3`). Requiere disciplina humana de
bumpear la versión en cada deploy. Si se olvida, los usuarios ven la versión
vieja. Además, algunos proxies y CDNs ignoran el querystring para caché.

**Service Worker**. Útil para PWAs offline, pero overkill para resolver solo
cache busting. Agrega complejidad (registración, actualización, invalidación)
que no necesitamos en esta etapa.

## Notas de implementación

- Vite ya genera hashed filenames por defecto. No requiere configuración extra.
- Configurar headers de caché al desplegar:
  - Si Static Web App: `staticwebapp.config.json` con reglas por path
  - Si Blob Storage + Front Door/CDN: configurar reglas en el origin o en el CDN
- Verificar después del primer deploy que: (a) `index.html` no se cachea más allá de la sesión, (b) los assets sí cargan con `Cache-Control: immutable`
- En `CLAUDE.md`: documentar que NO se debe usar `?v=` manual en imports
