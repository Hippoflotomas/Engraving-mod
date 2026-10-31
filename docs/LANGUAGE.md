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

**Names** are looked up in order:

1. built-in shapes (below);
2. pictures (PNG files), all under `Documents\ValheimHeraldryMod` (config `PictureRoot`), in this order:
   the server's `EngravingsServer` folder, your `Engravings` folder, Heraldry Mod's `Charges` folder (read as plain
   files, config `UseHeraldryCharges`; Heraldry Mod itself isn't needed), then pictures you've downloaded
   (`EngravingsDownloaded`);
3. the game's **item icons**, by the item's name or prefab name: `a hammer`, `two bronze axes`, `a wolf trophy`.

**Everyone carves the same picture.** When a carving is made, the picture each name resolved to is recorded with
it by fingerprint (a hidden last line, `#charges: wolf=1a2b…`). A player who hasn't got that picture gets it from the
server: the server's own pictures (its `EngravingsServer` folder) arrive without asking; another player's pictures
wait until they're approved with `engrave_get` (see what's waiting with `engrave_pending`). Engraving must be
installed on the server for pictures to be shared.

Matching ignores case, spaces, hyphens and underscores, and plurals are folded (*wolves* → *wolf*, *axes* → *axe*).

Built-in shapes: `circle`, `ring`, `square`, `diamond`, `triangle`, `star`, `star6`, `star8`,
`hexagon`, `octagon`, `cross`, `saltire-cross`, `crescent`/`moon`, `sun`, `heart`, `arrow`,
`valknut`, `triskele` (a triple spiral).

**Images:** PNGs with transparency use their alpha. Fully opaque images are read as dark-on-light
artwork (black = charge), so scanned or hand-drawn engravings work as they are.

## Figures (first version)

```
center: a viking striking, holding an axe and a shield
left: a warrior guarding, holding a sword and a shield
right: a viking walking, holding a spear, facing left
```

A **figure** (`viking`, `warrior`, `man`, `figure`, `person`, bearded; or `shieldmaiden`, `woman`, `maiden`, with a
braid and a longer tunic) is drawn side-on, facing right (`facing left` mirrors
it), as a solid shape whose detail comes from thin cut lines where its parts overlap, plus a belt, boot tops, a helmet
with a nasal guard, and a beard.

**The game's humanoid enemies** use the same rig with their own bodies: `greydwarf`, `troll` (about twice a Viking's
height), `skeleton`, `draugr`, `fuling` (or `goblin`), `fenring` (or `werewolf`). They take the same poses and can hold
the same things, plus a `club`.

**Poses:** `standing` (default), `walking`, `running`, `striking` (weapon raised overhead), `thrusting`, `guarding`
(shield up, weapon ready), `triumphant` (arms raised).

**Your own poses.** Poses are JSON files in `Documents\ValheimHeraldryMod\EngravingPoses` (beside `Engravings`). The
built-in poses are written there as `builtin-poses.json.example`, with notes on every angle: copy it to a `.json`
file, change or add poses, and run `engrave_reload`. A pose with a built-in pose's name replaces it for your new
carvings. Each pose has a `name`, other `words` that ask for it, `lean` and `head`, and angles for `nearArm`
(`upper`, `fore`, `grip`), `farArm` (the same, plus `inFront`), `nearLeg` and `farLeg` (`thigh`, `shin`, `foot`).
Limb angles are degrees from straight down, positive swinging forward (90 = forward, 180 = straight up).

Everyone sees the same figure: a carving records any pose that isn't built in (a hidden `#pose:` line), so other
players don't need your pose file. The preview tool reads poses too (`--poses DIR`, or an `EngravingPoses` folder beside
the script), `--write-poses FILE` writes the built-in ones out, and `--record` shows a script as the mod stores it.

**Holding:** `holding` (or `wielding`, `carrying`, `with`) then any of `axe`, `sword`, `spear`, `hammer`, `club`, `shield`,
joined by `and`. The first weapon goes in the near hand and a shield in the far hand, with that arm drawn over the
shield; a second weapon goes in the far hand.

A lone figure fills its cell. Several figures in one carving share one size (the smallest any of them would get), so a
Viking is the same height whatever he holds, and figures in the same row stand on one ground line.

## Animals

```
left: a wolf howling
center: a deer grazing
right: a boar charging, facing left
```

Four-legged animals share one rig and differ only in their body measurements and features: `wolf`, `deer` (or
`stag`), `boar` (or `pig`), `lox`. Antlers, tusks, a bristling mane and shaggy fur come from the species.

**Poses:** `standing` (default), `walking`, `running` (`galloping`, `fleeing`), `charging`, `grazing`, `howling`,
`leaping`. They live in the same pose files as the humanoid ones, marked `"rig": "quadruped"` (see the example file
for their angles: legs as `nearFore`/`farFore` `upper`, `lower`, `foot` and `nearHind`/`farHind` `thigh`, `shin`,
`foot`; `body`, `neck`, `head`, `tail`, `tailCurl`).

Animals and people in one carving share one scale, so they keep their true relative sizes: a lox dwarfs a Viking.

## Every creature

Figures come from six rigs. Each creature is a block of body data on its rig (sizes are the game's, so a carving
with several keeps them in proportion). Names in brackets also work.

| rig | creatures | poses |
|---|---|---|
| humanoid | viking (warrior, man), shieldmaiden (woman), greydwarf (greyling), greydwarfbrute (brute), greydwarfshaman (shaman), troll, skeleton, draugr, charred (twitcher), fuling (goblin), berserker (fulingberserker), fulingshaman, dverger (dwarf), surtling (frysling, imp), fenring (werewolf), golem (stonegolem), elder, barka, yagluth, morgen, ghost (spirit), wraith, odin (wanderer), bogwitch (witch), valkyrie, fallenwarrior, jotun (giant), jotunwitch | standing, walking, running, striking, thrusting, guarding, triumphant |
| quadruped | wolf, deer (stag), boar (pig), lox, calf, bear (bjorn), unbjorn, moose (elk), hare, eikthyr, ulv, seal, neck (lizard) | standing, walking, running, charging, grazing, howling, leaping |
| flyer | raven (crow, bird), vulture, chicken (hen), bat, drake (hatchling), dragon (moder, fader), deathsquito (mosquito), seeker | flying, gliding, diving, standing (perched) |
| serpent | serpent (snake), bonemaw, leech | swimming, striking |
| blob | blob (ooze), oozer, growth (tar), bonemass, gjall, abomination | standing, jumping |
| insect | tick, seekersoldier (soldier), queen | standing, walking, attacking |

Items to hold (humanoids): `axe`, `sword`, `spear`, `hammer`, `club`, `staff`, `bow`, `pickaxe`, `shield`, and
`thumbsup` / `thumbsdown` (a hand, not an item). More humanoid poses: aiming (a bow), throwing (a spear), judging.

Things that stand still: `tree` (beech), `pine`, `birch`, `oak`, `rock` (stone, boulder), `copper`, `tin`, `iron`
(scrap pile), `silver`, `obsidian`, `flametal`; a creature's trophy (`a troll trophy`, `the trophy of the elder`)
and a Forsaken's sacrificial stone (`the altar of eikthyr`).

## Scenes

`a scene of war`, `a scene of hunting`, `a scene of gathering` or `a scene of victory` fills the field with a Viking
and a counterpart taken from your own journey, so a hallway can be carved panel by panel as you go:

| scene | the Viking | opposite him |
|---|---|---|
| war | strikes with his best melee weapon (and shield, if he has one) | the furthest-along creature whose trophy you've taken, facing him |
| hunting | shoots his bow or throws his spear, running behind | the furthest-along game animal whose trophy you've taken, fleeing |
| gathering | swings his axe or pickaxe | the furthest-along material you know and have the tool for |
| victory | arms raised, the trophy of the last Forsaken you beat above him | that Forsaken's sacrificial stone |

Nothing is left to chance: when you carve, the open choices are made from your trophies (every one you've ever taken,
not what you carry), the materials you know and the weapons you carry (most damage wins; the one equipped breaks a
tie), and written into the carving: `a scene of war` is stored as `a scene of war against a troll, the viking holding
a sword and a shield`. Everyone then sees the same scene, and it doesn't change when you get better gear.

You can name any part yourself: `a scene of war against a draugr`, `a scene of hunting a deer, the shieldmaiden
holding a spear`, `a scene of gathering copper`, `a scene of victory over the elder`, `bare-handed`. Naming something
you haven't earned (a trophy you've never taken, a material you've never found) stops the carving. A victory before
any Forsaken has fallen isn't an error: the Viking celebrates anyway, and Odin gives him a thumbs down.

Creatures keep their true sizes against each other, shrunk where needed so the Viking isn't lost beside a dragon or a
tree. A ground line runs across the bottom of the field, edge to edge, so a row of scene panels shares one ground;
`without ground` leaves it off. The background, borders, `incised` / `sunken` and other charges all still apply. One
scene per carving.

## The carving window

Two tabs (top right), as in Heraldry Mod. **Basic** builds a carving from dropdowns: background, which sides get a
border (tick boxes), the border's style (with strands, line count and width where they apply, and the rune text for an
inscription), one charge (built-in shapes, then your pictures; item icons are left out of the list, so name them in Advanced), how many and how they're arranged,
where they go, and relief / incised / sunken. The text the choices make is shown underneath, so the words can be
learnt. **Advanced** is the text itself, for anything more (several charge groups, overlays, rotations). Both tabs make
the same text. Opening a piece that's already carved, or writing in Advanced, dims the Basic choices: they don't
describe that carving, and changing one replaces it. The window opens on the tab you used last (config `Editor.Mode`).

## Sides

Each side of a piece carries its own carving: the side you stand on when you open the carving window is the one you
carve. Pieces that use E themselves (doors, gates) are carved with Shift+E.

## Panel size

`size W by H` (metres) sets the size for previews. In game the piece being carved decides the size.

## Not built yet

- Interior detail from image shading (only silhouettes are used so far)
