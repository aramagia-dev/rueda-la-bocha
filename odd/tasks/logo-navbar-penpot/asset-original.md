# Asset logo header — bytes originales user521 (2026-11-11)

- Fuente (intocada): `src/RuedaLaBocha.Web/img/Group(1).png`
  - Tamaño: **11043 bytes** (>2KB ok)
  - Firma: `89-50-4E-47-0D-0A-1A-0A` (PNG válido)
  - IHDR real: **180x180**, 8-bit RGBA (no 350x295: se reporta lo medido, no lo descrito)
  - SHA256: `EFF90112D8FD9B8A3C660E6E2A39592D0C31D224B24CBA38AA45168042573DE3`
  - Contenido verificado visualmente: círculo lima + silueta negra jugadora chilena +
    pelota, fondo blanco. Coincide con el overlay Penpot (`…e303c6975749`).
- Destino: `src/RuedaLaBocha.Web/wwwroot/images/logo-rueda-la-bocha.png`
  - Copia byte-exacta (`copy`, sin recompresión ni recorte): 11043 bytes,
    SHA256 idéntico `EFF9…73DE`. Sin crop de padding blanco (se preserva original).
- Uso: solo header (`<img … width="180" height="180" alt="" …>` + CSS
  `.site-header .brand-mark img { contain }` en caja 48px/40px 1:1, sin recorte ni
  distorsión). Footer intacto con `⚽`. Sin URLs externas en el asset.
