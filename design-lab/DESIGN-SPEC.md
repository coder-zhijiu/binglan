# Pogget visual study

This is an isolated visual reconstruction of the currently installed Pogget
container. It does not read or modify Pogget data and contains no file
operations, persistence, desktop embedding, or todo behavior.

## Measured source

- Reference screenshot: `605 × 1013`
- Visible official container: about `353 × 444`
- Runtime values read from the installed app's `DesktopContainer.vina`:
  - Header tag color: `#A0B4E1`
  - Body alpha: `0.32`
  - Header alpha: `0.97`
  - Outer corner radius: `15.5`
  - Header corner radius: `9.2`
  - Shadow blur: `20.16`
  - Shadow alpha: `0.26`
  - Shadow offset: `0.64, 5.12`
  - Font family: `Segoe UI`
  - Text size token: `10`
  - Dynamic D3D11 material: enabled

## Structural measurements

- Outer shell: `353 × 444`
- Header inset: `10`
- Header height: `50`
- Grid: four columns, approximately `75` wide
- File icon: approximately `43 × 43`
- File item row: approximately `92` high

The official program uses a D3D11 dynamic material layer. WPF can reproduce
the geometry and alpha values, but its off-screen render cannot reproduce the
same live wallpaper blur. That difference is intentionally documented rather
than hidden with extra white cards.
