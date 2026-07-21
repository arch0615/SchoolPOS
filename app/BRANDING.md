# LoncherApp — Brand & Style Guide

Keep every surface (parent portal, vendor console, WPF POS) visually consistent.
When adding UI, reuse the tokens and the apple mark below — do not hardcode new hex values.

Tagline: **Facilitando la venta de loncheras escolares**

## Palette

| Role | Hex | Portal CSS var | WPF brush |
|------|-----|----------------|-----------|
| Primary / green (money, positive, primary buttons) | `#57A839` | `--green` | `PrimaryBrush` |
| Green dark (hover / sidebar / sign-out) | `#47912F` web · `#3F7F28` WPF | `--green-dark` | `PrimaryDarkBrush` |
| Accent / orange (secondary emphasis, status) | `#F19234` | `--orange` | `AccentBrush` |
| Orange dark (button hover) | `#E07D1C` | `--orange-dark` | — |
| Apple-mark outline / stem | `#EFA43B` | — | `OrangeBrush` |
| Apple-mark leaf | `#5FB03A` | — | `LeafBrush` |
| Cream (header background, hero card) | `#FBF3DA` | `--cream` | `CreamBrush` |
| Cream line (borders on cream) | `#EFE3C4` | `--cream-line` | — |
| Page background (warm off-white) | `#FAF6EC` | body bg | `SurfaceBrush` |
| Ink (headings, neutral totals) | `#3A3A3A` | `--ink` | `InkBrush` |
| Muted (labels, captions) | `#9A8F76` | `--muted` | `MutedBrush` |
| Danger (errors, low-stock) | `#C0392B` | — | `DangerBrush` |
| Message OK | bg `#EAFAF1` / text `#1E7D48` | `.msg.ok` | — |
| Message error | bg `#FDECEA` / text `#C0392B` | `.msg.error` | — |

**Color conventions**
- Money / revenue / balance / positive → **green** (`PrimaryBrush` / `--green`).
- Secondary money emphasis (e.g. "total recharged", cash-flow net) → **orange** (`AccentBrush` / `--orange`).
- Neutral counts / customer-balances total → **ink**.
- Errors and low-stock → **red** (`DangerBrush`).
- Buttons: primary = green, accent = orange, ghost = light-green tint. Header = cream with the logo.

## Logo assets (portal)

Located in [app/src/SchoolPOS.Portal.Web/wwwroot/img/](src/SchoolPOS.Portal.Web/wwwroot/img/):

- **`loncherapp-logo.svg`** — horizontal lockup (apple mark + `LoncherApp` wordmark). Header use.
- **`loncherapp-hero.svg`** — stacked lockup + tagline. Landing hero / large placements.
- **`favicon.svg`** — apple mark only. Browser tab icon.

The wordmark is `Loncher` in green `#57A839` + `App` in orange `#F19234`, bold (800), font stack
`'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif`.

> The provided source artwork (Canva export) embeds a high-res raster apple (~2 MB each); these
> assets are lightweight vector recreations of the same identity. If exact originals are ever
> required, drop them into the same folder and repoint the `<img>` tags.

## The apple mark (reusable geometry)

`viewBox="0 0 64 64"`. Three sub-paths — outline body + stem (stroked), leaf (filled). This exact
path data is used in the SVG assets **and** re-drawn in WPF `Path` elements so both platforms match.

- **Body** (stroke `#EFA43B`, width `4.5`, `stroke-linejoin: round`, no fill):
  `M32 21 C 27 13, 17 14, 15 24 C 13 33, 18 51, 27 51 C 30 51, 30 49, 32 49 C 34 49, 34 51, 37 51 C 46 51, 51 33, 49 24 C 47 14, 37 13, 32 21 Z`
- **Stem** (stroke `#EFA43B`, width `3.2`, round caps):
  `M33 19 C 35 12, 41 9, 46 12`
- **Leaf** (fill `#5FB03A`):
  `M31 19 C 24 9, 15 11, 20 18 C 24 23, 30 22, 31 19 Z`

**Monochrome variant** (e.g. white on the green POS sidebar): use the same three paths with
`stroke`/`fill` set to `White`.

### WPF snippet (POS screen title)

```xml
<StackPanel Orientation="Horizontal" VerticalAlignment="Center">
    <Viewbox Width="26" Height="26" Margin="0,0,10,0">
        <Canvas Width="64" Height="64">
            <Path Data="M32 21 C 27 13, 17 14, 15 24 C 13 33, 18 51, 27 51 C 30 51, 30 49, 32 49 C 34 49, 34 51, 37 51 C 46 51, 51 33, 49 24 C 47 14, 37 13, 32 21 Z"
                  Stroke="#EFA43B" StrokeThickness="4.5" StrokeLineJoin="Round" />
            <Path Data="M33 19 C 35 12, 41 9, 46 12" Stroke="#EFA43B" StrokeThickness="3.2"
                  StrokeStartLineCap="Round" StrokeEndLineCap="Round" />
            <Path Data="M31 19 C 24 9, 15 11, 20 18 C 24 23, 30 22, 31 19 Z" Fill="#5FB03A" />
        </Canvas>
    </Viewbox>
    <TextBlock Text="…" FontSize="24" FontWeight="Bold" Foreground="{StaticResource InkBrush}" VerticalAlignment="Center" />
</StackPanel>
```

## Where the tokens live

- **Portal** (all web pages): CSS custom properties in `:root` + component styles in
  [Pages/Shared/_Layout.cshtml](src/SchoolPOS.Portal.Web/Pages/Shared/_Layout.cshtml). Use
  `var(--green)` etc. inline; never hardcode hex.
- **WPF POS**: `SolidColorBrush` resources in
  [App.xaml](src/SchoolPOS.Pos.Desktop/App.xaml). Reference via
  `{StaticResource PrimaryBrush}` etc. Button styles: `PrimaryButton` (green), default `Button`.

Spanish-only UI, money as `decimal` in MXN — see project memory / requirements.
