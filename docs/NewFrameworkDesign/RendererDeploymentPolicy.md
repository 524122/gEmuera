# Renderer Deployment Policy

## Status

Accepted for the current legacy production path. Device-performance evidence is
not yet closed. This policy does not authorize an M2 display-path change or an
M6 renderer experiment.

## Decision

The application-level renderer baseline is Godot Compatibility, using OpenGL
ES 3 on Android. `project.godot` must keep both
`renderer/rendering_method` and `renderer/rendering_method.mobile` set to
`gl_compatibility` for the shipped low-end Android profile.

The product is a 2D, text-heavy console application. It does not require
RenderingDevice-only visual features, while Compatibility broadens support to
older GPUs and avoids the Vulkan/RenderingDevice path used by the Mobile
renderer. ETC2/ASTC import remains enabled for packaged application assets.

This setting is selected before the engine starts. It is not a per-session or
per-game setting, must not be written to save data, and cannot be treated as a
live recovery switch after a rendering failure. A Mobile-renderer artifact is a
separate experiment and requires a separate exported APK and report.

Compatibility is a coverage decision, not a promise of lower frame time on
every device. The OpenGL driver path can have its own first-use work for a
program, texture, or Canvas draw path. A first encounter during the TW wake-up
sequence must therefore be identified in the report as cold or warm; it must
not be mislabeled as a permanent Vulkan-versus-OpenGL result. Any pre-warming
work remains owned by the loading/display path and must be justified by a
measured first-use spike. The renderer policy does not authorize speculative
pre-warming or a display-backend rewrite.

## Non-goals

Changing the Godot renderer can address a Vulkan capability, driver, pipeline,
or GPU-base-cost problem. It does not reduce any of the following by itself:

- ERB execution, lazy loading, regex, SQL, image decode, or other VM work.
- Main-thread display batching, Canvas layout, text shaping, hit-rectangle
  rebuilding, Control fallback construction, or texture upload work.
- Retained display history, overlay indexes, or managed/native memory pressure.

Consequently, a Compatibility export that still stalls during the TW wake-up
sequence is evidence to investigate the display and VM paths, not evidence that
OpenGL is ineffective or that a renderer migration is justified.

## Ownership Boundaries

| Concern | Owner | Rule |
| --- | --- | --- |
| Godot graphics API selection | `project.godot` and release build | Compatibility is the low-end production baseline. |
| Console Canvas versus Controls | `EmueraContent` | This is an application display-backend choice, independent of Vulkan versus OpenGL. |
| Script-visible display semantics | legacy `GameView` | A graphics API switch must not change line, button, image, input, or wait ordering. |
| GPU ColorMatrix composition | `EmueraGpuRenderComponent` | Android currently uses its CPU fallback; renderer selection must not silently change script-visible pixels. |
| Future display replacement | M2/M6 gate | Any DTO-backed or new renderer remains separately flagged and rollbackable. |
| Device report identity | diagnostics/export workflow | Record renderer configuration, actual runtime method/driver when available, APK identity, device, game fixture, and save/input state. |

The existing Canvas backend is already the default console path and avoids the
ordinary one-Control-per-line cost. Complex image and div cases may still use
overlay or Control fallbacks. Renderer selection must therefore be evaluated
alongside fallback counts and visible/retained-line data, rather than from FPS
alone.

When `debug.performance_sampling.enabled=true`, the Godot host appends the
configured desktop/mobile rendering methods, Godot's effective runtime method
and driver when available, plus the active video adapter, vendor, and API
version to `PERF.SAMPLE` once per process. The effective method/driver and
adapter API are runtime evidence, while the project settings remain the
requested renderer; neither field alone proves that a renderer fallback did or
did not occur on a device.
The sample also reports GPU ColorMatrix and text offscreen queue depths as
separate fields, not as a generic texture queue. For each sampling window, the
`frame_max_*` queue fields retain the queue snapshot observed with that
window's longest main-thread frame. They are correlation evidence for a wake-up
stall, not GPU timing or proof of causation.

## TW Wake-up Investigation Protocol

Use a release APK, the same Godot version, resolution, game revision, profile,
save state, and replay/manual input sequence. Compare Mobile and Compatibility
only as separate artifacts. Run a warm-up followed by repeated measurement
windows on each target device; debug/editor profiler data is diagnostic only.

Collect the following during the wake-up transition and the next stable wait:

| Signal | Interpretation |
| --- | --- |
| frame p50/p95/p99 and input latency | User-visible stall and recovery. |
| GPU frame time, draw calls, texture allocation | GPU or driver pressure. |
| `PERF.SAMPLE` | Frame cadence, memory context, and queue snapshot at the longest sampled frame. |
| `PERF.DISPLAY_BRIDGE` | Snapshot, diff, apply time, line additions, data-only updates, and Control fallback count. |
| `PERF.CONSOLE_RENDER` | Canvas draw/hit rebuild cost, visible rows, overlays, parts, and hit rectangles. |
| VM busy time and work units | Interpreter-side work that a renderer switch cannot remove. |
| managed/native/GPU/RSS peaks | Memory pressure, allocation spikes, and potential low-memory fallback activation. |

Classify the result before changing code:

1. Compatibility lowers GPU or total frame p95/p99 while VM and display metrics
   remain stable: retain Compatibility as the low-end graphics solution.
2. Compatibility has little effect while display apply, Canvas draw/hit rebuild,
   fallback, or VM metrics spike: optimize the measured owner; do not attribute
   the stall to Vulkan alone.
3. Compatibility regresses a script-visible image, input, or wait behavior:
   treat it as a release blocker and preserve the legacy display path while the
   renderer-specific defect is isolated.

No numeric pass threshold is declared here. The first representative low-end
and mid-range Android reports must establish approved thresholds before a
performance claim is made.

## Verification and Rollback

1. Static check: the two rendering-method keys remain `gl_compatibility` and
   ETC2/ASTC import remains enabled. `shader_baker/enabled` has no effect on
   Compatibility and is not a renderer-policy gate.
2. Engine check: the Godot 4.7 Mono headless editor loads the project and builds
   its C# solution with the configured renderer settings.
3. Device check: install and launch the release APK on representative low-end
   and mid-range Android devices, then execute the TW wake-up protocol above.
4. Rollback: a renderer-related release regression is rolled back by shipping
   the previously verified APK/configuration. It does not justify changing the
   legacy parser, VM, Console model, or M2/M6 flags.

## Sources

- [Godot official documentation, "Overview of renderers"](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html):
  Mobile uses RenderingDevice/Vulkan-class APIs for newer devices; Compatibility
  uses OpenGL and supports the widest range of hardware, including older mobile
  devices.
- [Godot official documentation, "General optimization"](https://docs.godotengine.org/en/stable/tutorials/performance/general_optimization.html):
  CPU and GPU are separate bottlenecks, so profile and measure on target devices
  before attributing a frame stall to a renderer.
- [Godot official documentation, "Reducing stutter from shader (pipeline) compilations"](https://docs.godotengine.org/en/stable/tutorials/performance/pipeline_compilations.html):
  Compatibility does not support ubershaders, pipeline precompilation, or shader
  baking; a measured first-use shader stall must instead be handled by an
  explicitly justified loading-path warm-up.
- `godot-master` mobile and performance guidance: use Compatibility or Mobile
  for mobile, retain ETC2/ASTC compression, account for Compatibility first-use
  shader/program work, and diagnose with release-device measurements instead of
  optimizing blindly.
