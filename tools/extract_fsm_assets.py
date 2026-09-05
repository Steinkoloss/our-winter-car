r"""Read PlayMaker definitions from installed Unity assets without starting the game.

Requires UnityPy==1.25.3 and TypeTreeGeneratorAPI==0.0.10 (use a separate venv).
Example:
  python tools/extract_fsm_assets.py /path/to/My\ Winter\ Car \
      --match Systems/BankAccount --match Sheets/DebtLetter --out /tmp/economy.json

This is static evidence: values are asset defaults, not a player's save, and
runtime-created objects are absent. It supplements, never replaces, an F9 dump.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct


PARAM_TYPES = (
    "Integer Boolean Float String Color ObjectReference LayerMask Enum Vector2 "
    "Vector3 Vector4 Rect Array Character AnimationCurve FsmFloat FsmInt FsmBool "
    "FsmString FsmGameObject FsmOwnerDefault FunctionCall FsmAnimationCurve "
    "FsmEvent FsmObject FsmColor Unsupported GameObject FsmVector3 LayoutOption "
    "FsmRect FsmEventTarget FsmMaterial FsmTexture Quaternion FsmQuaternion "
    "FsmProperty FsmVector2 FsmTemplateControl FsmVar CustomClass"
).split()


def decode_parameter(data: dict, index: int):
    kind = PARAM_TYPES[data["paramDataType"][index]]
    pos = data["paramDataPos"][index]
    size = data["paramByteDataSize"][index]
    raw = bytes(data["byteData"][pos:pos + size])
    formats = {"FsmFloat": "f", "FsmInt": "i", "FsmBool": "?"}
    if kind in formats:
        fmt = formats[kind]
        width = struct.calcsize("<" + fmt)
        if len(raw) < width + 1:
            raise ValueError("Truncated " + kind)
        return {"type": kind, "value": struct.unpack("<" + fmt, raw[:width])[0],
                "useVariable": bool(raw[width]), "name": raw[width + 1:].decode("utf-8")}
    if kind in ("FsmEvent", "String"):
        return {"type": kind, "value": raw.decode("utf-8")}
    if kind in ("Integer", "Enum", "Float", "Boolean"):
        fmt = {"Integer": "i", "Enum": "i", "Float": "f", "Boolean": "?"}[kind]
        return {"type": kind, "value": struct.unpack("<" + fmt, raw)[0]}
    table = kind[0].lower() + kind[1:] + "Params"
    if kind == "ObjectReference":
        table = "unityObjectParams"
    elif kind in ("FsmMaterial", "FsmTexture"):
        # PlayMaker 1.7 stores both subclasses in its FsmObject table.
        table = "fsmObjectParams"
    if table in data:
        return {"type": kind, "value": data[table][pos]}
    # Preserve unsupported shapes explicitly; an empty value must not look like
    # evidence that an action has no target or no effect.
    return {"type": kind, "dataPosition": pos, "rawHex": raw.hex()}


def decode_actions(data: dict) -> list[dict]:
    starts = data["actionStartIndex"] + [len(data["paramName"])]
    return [{"type": name, "enabled": bool(data["actionEnabled"][i]),
             "parameters": [{"field": data["paramName"][p], **decode_parameter(data, p)}
                            for p in range(starts[i], starts[i + 1])]}
            for i, name in enumerate(data["actionNames"])]


def transitions(values: list[dict]) -> list[dict]:
    return [{"event": v["fsmEvent"]["name"], "to": v["toState"]} for v in values]


def read_script(obj, generator):
    from UnityPy.helpers import TypeTreeHelper
    from UnityPy.streams import EndianBinaryReader

    obj.assets_file.environment.typetree_generator = generator
    node = obj.generate_monobehaviour_node()
    for field in node.traverse():
        # The generator's Unity 5 header omits alignment after m_Enabled and
        # labels List<string> as string. Normalize both before decoding.
        if field.m_Name == "m_Enabled":
            field.m_MetaFlag |= 0x4000
        if (field.m_Type == "string" and field.m_Children
                and field.m_Children[0].m_Children[-1].m_Type == "string"):
            field.m_Type = "vector"
    boost = TypeTreeHelper.read_typetree_boost
    try:
        # The native reader caches unmodified nodes; the Python reader honors
        # the legacy corrections above. Bound the reader to this one object.
        TypeTreeHelper.read_typetree_boost = None
        return TypeTreeHelper.read_typetree(
            node, EndianBinaryReader(obj.get_raw_data(), endian=obj.reader.endian),
            byte_size=obj.byte_size, assetsfile=obj.assets_file)
    finally:
        TypeTreeHelper.read_typetree_boost = boost


def extract(game: Path, matches: list[str]) -> dict:
    import UnityPy
    from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

    data_dir = game / "mywintercar_Data"
    level = data_dir / "level2"
    env = UnityPy.load(str(level))
    objects = list(env.objects)
    version = str(objects[0].version)
    generator = TypeTreeGenerator(version)
    generator.load_local_game(str(game))

    names, transforms, parents = {}, {}, {}
    for obj in objects:
        if obj.type.name == "GameObject":
            names[obj.path_id] = obj.read().m_Name
        elif obj.type.name == "Transform":
            tree = obj.read()
            transforms[obj.path_id] = tree.m_GameObject.m_PathID
            parents[tree.m_GameObject.m_PathID] = tree.m_Father.m_PathID

    paths = {}

    def scene_path(go_id):
        if go_id in paths:
            return paths[go_id]
        parent = transforms.get(parents.get(go_id))
        name = names.get(go_id, "?")
        paths[go_id] = scene_path(parent) + "/" + name if parent else name
        return paths[go_id]

    def references(value):
        if isinstance(value, dict):
            if "m_PathID" in value and value.get("m_FileID") == 0:
                go_id = value["m_PathID"]
                if go_id in names:
                    value["scenePath"] = scene_path(go_id)
            for child in list(value.values()):
                references(child)
        elif isinstance(value, list):
            for child in value:
                references(child)

    records = []
    for obj in objects:
        if obj.type.name != "MonoBehaviour":
            continue
        head = obj.parse_monobehaviour_head()
        path = scene_path(head.m_GameObject.m_PathID)
        if matches and not any(m.lower() in path.lower() for m in matches):
            continue
        if head.m_Script.deref_parse_as_object().m_ClassName != "PlayMakerFSM":
            continue
        fsm = read_script(obj, generator)["fsm"]
        record = {"path": path, "fsmName": fsm["name"], "startState": fsm["startState"],
                  "globalTransitions": transitions(fsm["globalTransitions"]),
                  "variables": {k[0].upper() + k[1:]: [v["name"] for v in vs]
                                for k, vs in fsm["variables"].items()},
                  "variableDefaults": fsm["variables"],
                  "states": [{"name": s["name"], "transitions": transitions(s["transitions"]),
                              "actionTypes": s["actionData"]["actionNames"],
                              "actions": decode_actions(s["actionData"])} for s in fsm["states"]]}
        references(record)
        records.append(record)

    resources = UnityPy.load(str(data_dir / "resources.assets"))
    globals_ = None
    for obj in list(resources.objects):
        if obj.type.name != "MonoBehaviour":
            continue
        head = obj.parse_monobehaviour_head()
        if head.m_Script.deref_parse_as_object().m_ClassName == "PlayMakerGlobals":
            values = read_script(obj, generator)["variables"]
            globals_ = {k[0].upper() + k[1:]: vs for k, vs in values.items()}
            break
    return {"meta": {"source": "installed-assets", "unityVersion": version,
                     "levelSha256": hashlib.sha256(level.read_bytes()).hexdigest(),
                     "values": "asset defaults; not runtime or save state"},
            "fsms": sorted(records, key=lambda f: (f["path"], f["fsmName"])),
            "globalVariables": globals_}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("game", type=Path)
    parser.add_argument("--match", action="append", default=[], help="Path fragment; repeatable")
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    result = extract(args.game, args.match)
    args.out.write_text(json.dumps(result, indent=2, ensure_ascii=False, allow_nan=False) + "\n",
                        encoding="utf-8")
    print(f"Extracted {len(result['fsms'])} FSMs and global definitions to {args.out}")


if __name__ == "__main__":
    main()
