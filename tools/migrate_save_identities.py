#!/usr/bin/env python3
"""Migrate one InfraSpace save copy to the normalized Mod identities."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
from typing import Any


OLD_AREA_MOVE = "InfraSpaceAreaMove.AreaMoveMod, AreaMove"
OLD_MULTI_LOGISTICS = (
    "InfraSpaceMultiLogistics.MultiLogisticsMod, InfraSpaceMultiLogistics"
)
OLD_PRODUCTION_FILTER = (
    "InfraSpaceProductionFilter.ProductionFilterMod, ProductionFilter"
)
OLD_ROAD_SNAP = "InfraSpaceRoadSnap.RoadSnapMod, RoadSnap"

NEW_CONSTRUCTION_TOOLS = (
    "InfraSpaceConstructionTools.ConstructionToolsMod, "
    "InfraSpaceConstructionTools"
)
NEW_MULTI_RESOURCE_LOGISTICS = (
    "InfraSpaceMultiResourceLogistics.MultiResourceLogisticsMod, "
    "InfraSpaceMultiResourceLogistics"
)
NEW_PRODUCTION_INSIGHTS = (
    "InfraSpaceProductionInsights.ProductionInsightsMod, "
    "InfraSpaceProductionInsights"
)
NEW_STATION_TOOLS = "InfraSpaceStationTools.StationToolsMod, InfraSpaceStationTools"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_save(path: Path) -> tuple[list[str], dict[str, Any]]:
    with path.open("r", encoding="utf-8-sig") as stream:
        header = [stream.readline() for _ in range(4)]
        if len(header) != 4 or any(not line for line in header):
            raise ValueError("存档头部不完整")
        document = json.load(stream)
    return header, document


def migrate_script_mods(document: dict[str, Any]) -> dict[str, int]:
    script_mods = document.get("scriptMods")
    if not isinstance(script_mods, list):
        raise ValueError("存档中没有可识别的 scriptMods 数组")

    counts: dict[str, int] = {}
    by_type: dict[str, dict[str, Any]] = {}
    for item in script_mods:
        if not isinstance(item, dict) or not isinstance(item.get("$type"), str):
            raise ValueError("scriptMods 包含无法识别的条目")
        type_name = item["$type"]
        counts[type_name] = counts.get(type_name, 0) + 1
        by_type[type_name] = item

    required = (
        OLD_AREA_MOVE,
        OLD_MULTI_LOGISTICS,
        OLD_PRODUCTION_FILTER,
        OLD_ROAD_SNAP,
    )
    invalid = [name for name in required if counts.get(name) != 1]
    if invalid:
        details = ", ".join(f"{name}={counts.get(name, 0)}" for name in invalid)
        raise ValueError(f"旧 Mod 入口数量不符合预期：{details}")

    road_snap = by_type[OLD_ROAD_SNAP]
    area_move = by_type[OLD_AREA_MOVE]
    area_move["$type"] = NEW_CONSTRUCTION_TOOLS
    area_move["suppressRoadMagnet"] = bool(
        road_snap.get("suppressRoadMagnet", False)
    )
    by_type[OLD_MULTI_LOGISTICS]["$type"] = NEW_MULTI_RESOURCE_LOGISTICS
    by_type[OLD_PRODUCTION_FILTER]["$type"] = NEW_PRODUCTION_INSIGHTS

    script_mods[:] = [
        item for item in script_mods if item["$type"] != OLD_ROAD_SNAP
    ]
    if not any(item["$type"] == NEW_STATION_TOOLS for item in script_mods):
        script_mods.append({"$type": NEW_STATION_TOOLS})

    centers = by_type[OLD_MULTI_LOGISTICS].get("centers")
    return {
        "script_mods": len(script_mods),
        "centers": len(centers) if isinstance(centers, dict) else 0,
        "road_snap_suppressed": int(area_move["suppressRoadMagnet"]),
    }


def validate_migrated_document(document: dict[str, Any], expected_centers: int) -> None:
    script_mods = document.get("scriptMods")
    if not isinstance(script_mods, list):
        raise ValueError("迁移结果缺少 scriptMods")

    types = [item.get("$type") for item in script_mods if isinstance(item, dict)]
    expected = {
        NEW_CONSTRUCTION_TOOLS,
        NEW_MULTI_RESOURCE_LOGISTICS,
        NEW_PRODUCTION_INSIGHTS,
        NEW_STATION_TOOLS,
    }
    if set(types) != expected or len(types) != len(expected):
        raise ValueError(f"迁移后的 Mod 入口不符合预期：{types}")

    logistics = next(
        item
        for item in script_mods
        if item.get("$type") == NEW_MULTI_RESOURCE_LOGISTICS
    )
    centers = logistics.get("centers")
    actual_centers = len(centers) if isinstance(centers, dict) else 0
    if actual_centers != expected_centers:
        raise ValueError(
            f"物流中心数量在迁移中发生变化：{expected_centers} -> {actual_centers}"
        )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()

    source = args.source.resolve()
    destination = args.destination.resolve()
    if source == destination:
        raise ValueError("目标必须是新的存档副本，不能覆盖原存档")
    if not source.is_file():
        raise FileNotFoundError(source)
    if destination.exists():
        raise FileExistsError(destination)

    original_hash = sha256(source)
    header, document = load_save(source)

    report = migrate_script_mods(document)
    validate_migrated_document(document, report["centers"])

    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(destination.suffix + ".tmp")
    try:
        with temporary.open("w", encoding="utf-8", newline="\n") as stream:
            stream.writelines(header)
            json.dump(document, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
        written_header, written = load_save(temporary)
        if written_header != header:
            raise ValueError("存档头部在迁移中发生变化")
        validate_migrated_document(written, report["centers"])
        os.replace(temporary, destination)
    finally:
        if temporary.exists():
            temporary.unlink()

    if sha256(source) != original_hash:
        raise RuntimeError("原存档在迁移过程中发生变化")

    print(f"source_sha256={original_hash}")
    print(f"destination={destination}")
    print(f"script_mods={report['script_mods']}")
    print(f"centers={report['centers']}")
    print(f"road_snap_suppressed={report['road_snap_suppressed']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
