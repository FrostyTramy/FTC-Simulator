# 10 — Graphics, Audio, UI

## Rendering
- Godot Forward+, HDR, ACES/AgX-style tonemapping, auto exposure tuned for an
  indoor arena.
- Global illumination: LightmapGI baked for the static field + arena; SDFGI or
  VoxelGI optional on Ultra for dynamic objects; reflection probes around the
  field (polycarbonate and metal need good reflections).
- SSAO/SSIL, SSR, glow/bloom (subtle), volumetric fog (very subtle haze, arena
  lights), depth of field (broadcast/photo cameras only), TAA or FSR2,
  anisotropic filtering, shadows from main arena lights with good cascade
  setup.
- Quality presets Low / Medium / High / Ultra + custom, auto-detected on first
  run. Low must disable expensive effects and use simpler materials.
- Frame pacing: VSync options, FPS cap, frame time graph in dev overlay.

## Materials (PBR, `config/materials.yaml`)
Physically plausible values; use real reference photos for tuning:
- Anodized aluminum (goBILDA silver/black, REV), steel screws, brass.
- Foam tiles: grey, slightly fuzzy, visible seams/interlocking edges, light
  wear and scuffs (subtle decals), tape lines if the manual has them.
- Field walls: clear polycarbonate with correct transparency, slight
  reflections, edge highlights, dust/scratches subtle.
- Game elements: pollen plastic (glossy or matte as in reality), nectar,
  hive and flower materials per the real field kit colors.
- Rubber (wheels/rollers), compliant wheels, 3D printed plastic (layer lines
  normal map), carbon fiber, polycarbonate plates, cables, zip ties.
- Robot hubs with lit status LEDs and labels (generic, no trademarks unless
  allowed).
- Decals: AprilTags, field markings, alliance colors.

## Arena environment
- Competition venue: field surrounded by a floor area, alliance stations
  (driver stations behind the walls), scoring table, field lights, banners
  (generic/our own designs, no sponsor logos without permission), audience
  stands with low-cost animated crowd (impostors), pits background (optional).
- Alternate environments: practice room/classroom, our team's lab (later).
- HDRI or modeled environment for reflections.

## Cameras
- Driver view (from the alliance station, at eye height, per driver
  position), robot POV (follow + first person), top-down orthographic
  (strategy), free fly cam, broadcast cam (auto framing the action with smooth
  motion, cuts between angles), photo mode (pause, free cam, DOF, export
  high-res PNG).
- Camera shake only in broadcast/replay modes, never in driver view.

## Replays
- From deterministic replay files: scrub timeline, play/pause, 0.1x–4x speed,
  jump to events (scores, fouls), switch cameras during replay, export video
  (via Godot movie writer or ffmpeg if installed; ask before bundling).
- Instant replay hotkey after a scoring event.

## Audio
- Motor sounds synthesized from motor speed and load (pitch ∝ RPM, louder
  under load), different character per motor type; gearbox whine; servo buzz.
- Mechanical: chain/belt noise, flywheel spin-up, intake rollers, pollen
  impacts on foam/walls/plastic (velocity-based variations), robot collisions.
- Match: buzzers and cues (self-made, no copyrighted sounds), crowd ambience
  (optional), announcer callouts (optional, later).
- 3D spatial audio from the active camera; separate volume sliders (master,
  match, robots, ambience, UI).

## UI / UX
- Main menu: Play (practice / match), Code (editor), Robots (library/import),
  Replays, Settings, About.
- Clean, modern style; readable on 1080p and scalable for 1440p/4K; dark theme
  by default; consistent icons; alliance colors used consistently.
- Settings: graphics, audio, controls/gamepads, editor, git account, paths
  (JDK, git), language (English first; Romanian later as an option).
- Onboarding: first-run tutorial: drive the starter bot, open its code,
  change something, run it.
- Accessibility: colorblind-safe alliance indicators (pattern/labels, not just
  red/blue), UI scale, remappable controls, subtitles for audio cues.
- Tooltips everywhere for sim-specific features; "?" links to in-app help.
