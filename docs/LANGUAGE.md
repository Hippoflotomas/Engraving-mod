# The engraving language

A carving is described in plain words, one statement per line (`;` or `.` also end a statement).
`#` starts a comment. Words are case-insensitive; text in "quotes" keeps its case.
The parser is forgiving: filler words like *the, of, with, at, on* are skipped, and mistakes are
reported per line instead of stopping the whole carving.

```
# a door panel for the great hall
background recessed
border top and bottom: knotwork
border left and right: ring-chain
center: a sun
top left and top right: a star, small
```

## Background

`background plain | recessed | stippled | hatched`

| word | effect |
|---|---|
| `plain` (default) | Field left at the surface. Relief charges get a cut outline round them. |
| `recessed` | Field cut down, so charges and frame stand proud. |
| `stippled` | Recessed, with punched dots. |
| `hatched` | Recessed, with fine diagonal lines. |

## Borders

`border <sides>: <style>[, options]`

**Sides:** `top`, `bottom`, `left`, `right`, `sides` (left and right), `ends` (top and bottom), `all`
(the default). Combine with `and` or commas. A later statement for a side replaces an earlier one.

**Styles:**

| style | looks like |
|---|---|
| `knotwork` (`plait`, `interlace`) | Over-under plait. `with N strands` (2–6, default 3). |
| `ring-chain` (`borre`) | Borre-style chain: twisting strands, a ring in each eye, a lozenge clasp on each crossing. |
| `runes "text"` | Inscription in Elder Futhark between rails. `between N lines` (default 2), `without lines`. |
| `N lines` | Parallel incised lines. |
| `beads` (`pellets`, `dots`) | A row of domed pellets. |
| `zigzag` (`chevrons`, `wolf-teeth`) | A raised zigzag between rails. |
| `none` | Removes the border on those sides. |

**Options:** `wide`, `narrow`, `N strands`, `between N lines`.

**How borders join.** Where two borders meet they connect through a corner square (ring and boss).
Where a side has no neighbouring border, the band runs straight off the panel edge, and its pattern
repeat is fitted to the band length, so a row of panels carries one continuous band. Field edges
without a border are left open for the same reason.

**Runes.** English is transliterated (*th* → ᚦ, *ng* → ᛜ, *v* → ᚠ, *x* → ᚲᛊ, *y* → ᛁ, …); rune
characters typed directly pass through. Spaces become the ᛬ divider. One inscription given to several
sides flows clockwise (top → right → bottom → left), split in proportion to side length, letters
standing with their tops toward the panel edge as on a runestone. Text that is too long is squeezed
to fit.

## Charges

`[position:] [count] name [arrangement][, modifiers]`

```
center: a wolf
top left and top right: a raven, facing left
bottom: four circles in a ring, sunken
a wolf at the top left
a wolf and two ravens            # several charges share the same cell
center: "My Banner", incised     # quotes for names with odd spelling
```

**Positions** (blazon positions in modern words): `center`/`middle`, `top`, `bottom`, `left`,
`right`, and the corners `top left`, `top right`, `bottom left`, `bottom right` (`top-left` works
too). Several positions can share one statement with `and`. No position means center.

**Automatic size.** The field is split into the rows that are used, and each row into the columns
used in it. A lone charge in the centre gets the whole field; `top` and `bottom` halve it; three
columns in a row get a third each. Every group fills its cell. Statements at the same position overlay
each other (e.g. `center: a ring` then `center: a star, small`).

**Counts:** `a`/`an`/`one` … `twelve`, or digits.

**Arrangements:** `in a grid` (default: the column count giving the largest charges),
`in a ring`, `in a row`, `in a column`.

**Modifiers:**

| word | effect |
|---|---|
| `relief` (default), `raised` | Shape stands proud. |
| `incised`, `outlined`, `engraved` | Only the outline is cut. |
| `sunken`, `hollow` | Whole shape cut below the ground. |
| `small`, `tiny` | 65% / 40% of the automatic size. |
| `flipped`, `mirrored`, `facing left` | Mirror left-right. |
| `rotated N [left/right]`, `upside-down` | Rotate N degrees (left/anticlockwise by default). |

**Names** are looked up in order: built-in shapes, then image folders: server-synced engravings,
the `Engravings` folder, then the heraldry mod's `Charges` folder. Matching ignores case, spaces,
hyphens and underscores, and plurals are folded (*wolves* → *wolf*, *axes* → *axe*).

Built-in shapes: `circle`, `ring`, `square`, `diamond`, `triangle`, `star`, `star6`, `star8`,
`hexagon`, `octagon`, `cross`, `saltire-cross`, `crescent`/`moon`, `sun`, `heart`, `arrow`,
`valknut`, `triskele`.

**Images:** PNGs with transparency use their alpha. Fully opaque images are read as dark-on-light
artwork (black = charge), so scanned or hand-drawn engravings work as they are.

## Panel size

`size W by H` (metres) sets the size for previews. In game the piece being carved decides the size.

## Not built yet

- Posable rigged figures (humanoid, quadruped, bird, serpent, dragon)
- Game item icons as charges
- Interior detail from image shading (only silhouettes are used so far)
