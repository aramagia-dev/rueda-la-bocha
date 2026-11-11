# Bloqueo preciso: raster original no exportable a bytes (reversión a estado working)

Fecha: 2026-11-11. El círculo lima vacío NO es el logo: el mark original incluye overlay
raster (silueta negra jugadora chilena + pelota, shape `8ee4bc57-d633-80f7-8008-e303c6975749`,
media `542abcb8-15f9-80f7-8008-e302b529d626` sobre círculo `#D9EF74` 44x44).
Se intentó obtener los bytes genuinos por todas las vías read-only disponibles; todas bloqueadas:

1. `execute_code` sin globales Penpot: `penpot`, `penpotUtils` → `Unknown identifier`
   (servidor MCP v1.5.0; el overview promete esos objetos pero el sandbox no los inyecta).
   Sin API programática de imágenes. No se mutó nada.
2. `export_shape` PNG (`shape` del board y `fill` del overlay): retorna `null` en JSON con
   la imagen solo como payload visual inline; no deja archivo de bytes (verificado en
   `tool-output`: solo aparece el SVG texto del board; los demás ficheros son de sesiones
   hermanas). Inspección visual confirma la silueta, pero no hay bytes persistibles.
3. `export_shape` SVG (shape del overlay y del board): el raster viene como URL externa
   `https://design.penpot.app/assets/by-file-media-id/...` — prohibida en la app
   (no external CDN) y no es base64 embebido.
4. `fetch` directo a esa URL desde `execute`: HTTP **404** (media autenticada; el sandbox
   no tiene sesión Penpot; no se piden credenciales por alcance).
5. `list_mcp_resources`: `resources: []` — el servidor no expone la librería de medios.

Conclusión: sin bytes genuinos no hay asset fiel posible sin inventar un trazado
(prohibido: no fake, no redesign, no imitación emoji). Cambio del círculo vacío
**revertido completo** (header y footer vuelven a `⚽`, CSS original, asset y tests del
intento eliminados) para preservar el estado working hasta el fix completo.
Vía de desbloqueo para el padre: conseguir el PNG del overlay (44x44) por fuera del
sandbox (cliente Penpot con sesión) y entregarlo como `images/logo-marca.png`; entonces
el fix es trivial (img local + tests con assert de figura). IDs de re-export idénticos
a los de `penpot-fuente.md`.

## Comparativa wordmark (sin cambios, documentada)

- Original Penpot: `Rueda la Bocha`, Inter Tight 700 **18px**, `#123A30`, caja 127x22 en
  (137,21); descriptor header `RESERVAS DEPORTIVAS` Inter Tight 700 9px `#66776F`.
- App actual (preservada): `.brand strong` 1.3rem sistema (≈20.8px) `letter-spacing:-.04em`;
  `.brand small` .65rem; copy header `COMPLEJO DE FÚTBOL 5` / footer
  `El punto de encuentro de tu equipo.` (copys de app, fuera de alcance cambiarlos).
- Lockup completo (icono+wordmark) como una sola exportación: no posible sin el raster
  (mismo bloqueo de arriba). Cuando se desbloquee, exportar el grupo brand completo del
  board y usarlo como único `img alt="Rueda la Bocha"` del navbar (solo logo, sin tocar
  spacing/nav).
