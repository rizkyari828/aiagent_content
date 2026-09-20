"""Trusted Blender template: ``local_ai_laptop``.

Renders a deterministic "local AI on a laptop" desk scene as a PNG image
sequence for one storyboard scene. Input is a structured JSON parameter file
only; this script never evaluates caller-supplied source and never accepts a
script path, shell fragment, or free-form expression. The C# provider encodes
the rendered frames to H.264 with FFmpeg.

Invocation (arguments after ``--`` belong to this script)::

    blender -b --factory-startup --python local_ai_laptop.py -- \
        --params <params.json> --frames <frames_dir>

The rendered frames are written as ``frame_0001.png``, ``frame_0002.png`` ...
into the assigned frames directory.
"""

import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Vector


def parse_args():
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    else:
        argv = []
    parser = argparse.ArgumentParser()
    parser.add_argument("--params", required=True)
    parser.add_argument("--frames", required=True)
    return parser.parse_args(argv)


def srgb_to_linear(value):
    if value <= 0.04045:
        return value / 12.92
    return ((value + 0.055) / 1.055) ** 2.4


def hex_channel(text):
    return int(text, 16) / 255.0


def hex_color(value, fallback):
    text = (value or "").lstrip("#").strip()
    if len(text) != 6:
        text = fallback
    try:
        channels = [hex_channel(text[index:index + 2]) for index in (0, 2, 4)]
    except ValueError:
        channels = [hex_channel(fallback[index:index + 2]) for index in (0, 2, 4)]
    return tuple(srgb_to_linear(channel) for channel in channels)


def material(name, color, metallic=0.0, roughness=0.4):
    mat = bpy.data.materials.new(name)
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
    mat.diffuse_color = (*color, 1.0)
    mat.metallic = metallic
    mat.roughness = roughness
    return mat


def add_cube(name, location, scale, mat, bevel=0.08):
    bpy.ops.mesh.primitive_cube_add(location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    if bevel:
        modifier = obj.modifiers.new("Bevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = 3
    return obj


def build_scene(accent_hex, background_hex):
    accent = hex_color(accent_hex, "5fd3c4")
    body = material("Body", hex_color("0d1217", "0d1217"), metallic=0.75, roughness=0.25)
    screen = material("Screen", hex_color("032b3d", "032b3d"), metallic=0.1, roughness=0.2)
    keys = material("Keys", hex_color("040506", "040506"), roughness=0.45)
    desk = material("Desk", hex_color("141416", "141416"), roughness=0.65)
    orb = material("Accent", accent, metallic=0.1, roughness=0.15)

    add_cube("Desk", (0, 0, -0.45), (5.5, 4.0, 0.25), desk, 0.15)
    add_cube("LaptopBase", (0, 0, 0), (2.4, 1.65, 0.12), body, 0.12)
    add_cube("Keyboard", (0, -0.15, 0.15), (1.75, 1.0, 0.025), keys, 0.03)

    frame = add_cube("ScreenFrame", (0, 1.45, 1.72), (2.35, 0.09, 1.45), body, 0.10)
    frame.rotation_euler.x = math.radians(-8)
    panel = add_cube("Screen", (0, 1.34, 1.72), (2.05, 0.025, 1.17), screen, 0.04)
    panel.rotation_euler.x = math.radians(-8)

    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=0.22, location=(0, 1.20, 1.75))
    bpy.context.object.data.materials.append(orb)

    bpy.ops.object.light_add(type="AREA", location=(3, -3, 6))
    key = bpy.context.object
    key.data.energy = 1100
    key.data.shape = "DISK"
    key.data.size = 5

    bpy.ops.object.light_add(type="AREA", location=(-4, 1, 3))
    fill = bpy.context.object
    fill.data.energy = 650
    fill.data.color = (0.15, 0.5, 1.0)
    fill.data.size = 4

    bpy.ops.object.light_add(type="AREA", location=(0, 4, 4))
    rim = bpy.context.object
    rim.data.energy = 850
    rim.data.color = accent
    rim.data.size = 3

    bpy.context.scene.world.color = hex_color(background_hex, "020304")


def add_camera(rig, frame_start, frame_end, orbit_start, orbit_end):
    bpy.ops.object.empty_add(type="PLAIN_AXES", location=(0, 0, 1.0))
    rig = bpy.context.object
    rig.name = "CameraRig"

    bpy.ops.object.camera_add(location=(6.3, -6.3, 4.4))
    camera = bpy.context.object
    bpy.context.scene.camera = camera

    target = Vector((0, 0.4, 1.0))
    direction = target - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    camera.parent = rig

    rig.rotation_euler.z = orbit_start
    rig.keyframe_insert(data_path="rotation_euler", frame=frame_start)
    rig.rotation_euler.z = orbit_end
    rig.keyframe_insert(data_path="rotation_euler", frame=frame_end)


def configure_render(scene, params, frame_start, frame_end, frames_dir):
    preferences = bpy.context.preferences.addons["cycles"].preferences
    preferences.compute_device_type = params["computeBackend"]
    preferences.get_devices()
    enabled = [device for device in preferences.devices if device.type == params["computeBackend"]]
    if not enabled:
        raise RuntimeError(f"no {params['computeBackend']} compute device is available")
    for device in preferences.devices:
        device.use = device.type == params["computeBackend"]

    scene.render.engine = params["renderEngine"]
    scene.cycles.device = "GPU"
    scene.cycles.samples = params["samples"]
    scene.cycles.seed = params["seed"]

    scene.render.resolution_x = params["width"]
    scene.render.resolution_y = params["height"]
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.fps = params["framesPerSecond"]

    scene.frame_start = frame_start
    scene.frame_end = frame_end
    scene.render.filepath = os.path.join(frames_dir, "frame_")


def main():
    args = parse_args()
    with open(args.params, "r", encoding="utf-8") as handle:
        params = json.load(handle)

    frames_dir = os.path.abspath(args.frames)
    os.makedirs(frames_dir, exist_ok=True)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)

    build_scene(params["accent"], params["background"])

    fps = max(1, int(params["framesPerSecond"]))
    frame_count = max(1, int(round(float(params["durationSeconds"]) * fps)))
    frame_start, frame_end = 1, frame_count
    add_camera(
        None,
        frame_start,
        frame_end,
        math.radians(-20),
        math.radians(25),
    )
    configure_render(bpy.context.scene, params, frame_start, frame_end, frames_dir)

    print(
        "local_ai_laptop: rendering",
        frame_count,
        "frames at",
        f"{params['width']}x{params['height']}",
        params["samples"],
        "samples on",
        params["computeBackend"],
    )
    bpy.ops.render.render(animation=True)
    print("local_ai_laptop: render complete")
    return 0


if __name__ == "__main__":
    sys.exit(main())
