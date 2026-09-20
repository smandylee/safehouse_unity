"""Download text-free item art from tarkov.dev for the GEAR screen's LOADOUT slot cards.

Optional development step: the game works without it and never downloads anything itself.

    py -3 tools/import_art.py                 download art for every wearable item not in icon_cache/art yet
    py -3 tools/import_art.py --refresh       download and convert all of it again
    py -3 tools/import_art.py --all           every item in the catalog, not just wearable gear

The stash icons in icon_cache/48 are the game's inventory images, which have the item's short name
(LV-119, AK-12, ...) baked in at a fixed pixel size. That is fine at their native 48 px per cell, but a
slot card is a different size for every item, so the same text comes out a different size on each. This
fetches the 512 px render instead: no text, no background box, the item's own proportions, so a card can
scale it to fit and the name is written once, at one size, by the game.

The images are Escape from Tarkov game art served by tarkov.dev. Keep icon_cache/ private: it is
git-ignored and must not be redistributed.

It needs each item's tarkov.dev id, which the Python project's snapshot holds (data/source/
tarkov_snapshot.json.gz, written by its tools/import_tarkov.py) - pass it with --snapshot if the two
projects are not side by side. Pillow is needed to convert WebP to PNG: py -3 -m pip install pillow
"""
from __future__ import annotations

import argparse
import gzip
import io
import json
import re
import sys
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

PROJECT = Path(__file__).resolve().parent.parent
DATA = PROJECT / "Assets" / "StreamingAssets" / "data"
DEFAULT_SNAPSHOT = PROJECT.parent / "safehouse_v0_1" / "data" / "source" / "tarkov_snapshot.json.gz"
DEFAULT_OUTPUT = PROJECT / "icon_cache" / "art"
USER_AGENT = "SafehouseCampaignTool (private tabletop companion; art import)"
MAX_SIDE = 256  # slot cards are at most ~144 px; 256 keeps them sharp on a scaled screen
WORKERS = 8


def wearable_ids(catalog: dict[str, str]) -> list[str]:
    """Item ids that can go in a loadout slot: everything gear.json lists as a weapon, ammo, gear or med."""
    gear = json.loads((DATA / "gear.json").read_text(encoding="utf-8-sig"))
    ids: list[str] = []
    for table in ("weapons", "ammo", "equipment", "meds"):
        ids.extend(item_id for item_id in gear.get(table, {}) if item_id in catalog)
    return sorted(set(ids))


def load_catalog() -> dict[str, str]:
    """item_id -> display name, from the game's own items.json."""
    document = json.loads((DATA / "items.json").read_text(encoding="utf-8-sig"))
    return {row["item_id"]: row["name"] for row in document["items"]}


# Loose ammunition is named "5.45x39mm BP gs x80" in items.json (the stack size is appended); the game's own
# name for the round has no such suffix.
STACK_SUFFIX = re.compile(r"\s*×\d+$")


def tarkov_id_for(name: str, by_name: dict[str, str]) -> str | None:
    return by_name.get(name) or by_name.get(STACK_SUFFIX.sub("", name))


def load_tarkov_ids(snapshot: Path) -> dict[str, str]:
    """Display name -> tarkov.dev id. Names are how the two projects' ids line up (ours are slugs of them)."""
    with gzip.open(snapshot, "rt", encoding="utf-8") as handle:
        items = json.load(handle)["items"]
    by_name: dict[str, str] = {}
    for item in items:
        by_name.setdefault(item["name"], item["id"])
    return by_name


def fetch(url: str) -> bytes | None:
    """The bytes at url, or None when there is no such image (404). Other failures raise."""
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return response.read()
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise


def convert(image: bytes) -> bytes:
    from PIL import Image

    with Image.open(io.BytesIO(image)) as source:
        art = source.convert("RGBA")
    scale = min(1.0, MAX_SIDE / max(art.size))
    if scale < 1.0:
        art = art.resize((max(1, round(art.width * scale)), max(1, round(art.height * scale))), Image.LANCZOS)
    out = io.BytesIO()
    art.save(out, format="PNG", optimize=True)
    return out.getvalue()


def import_one(job: tuple[str, str], output: Path) -> tuple[str, str]:
    item_id, tarkov_id = job
    try:
        data = fetch(f"https://assets.tarkov.dev/{tarkov_id}-512.webp")
        if data is None:
            return item_id, "missing"
        target = output / f"{item_id}.png"
        temp = target.with_suffix(".tmp")
        temp.write_bytes(convert(data))
        temp.replace(target)  # never leave a half-written file that would later fail to load
        return item_id, "ok"
    except Exception as error:  # one bad item must not stop the other 600
        return item_id, f"failed: {type(error).__name__}: {error}"


def main() -> int:
    parser = argparse.ArgumentParser(description="Download text-free item art from tarkov.dev.")
    parser.add_argument("--snapshot", type=Path, default=DEFAULT_SNAPSHOT, help="Python project's tarkov snapshot")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT, help="Art folder (kept out of git and builds)")
    parser.add_argument("--refresh", action="store_true", help="Download art that already exists again")
    parser.add_argument("--all", action="store_true", help="Every catalog item, not just wearable gear")
    parser.add_argument("--only", nargs="*", metavar="ITEM_ID", help="Just these item ids (for trying it out)")
    args = parser.parse_args()

    try:
        import PIL  # noqa: F401
    except ImportError:
        print("This tool needs Pillow: py -3 -m pip install pillow", file=sys.stderr)
        return 2
    if not args.snapshot.exists():
        print(f"Snapshot not found: {args.snapshot}\nPass the Python project's data/source/tarkov_snapshot.json.gz "
              "with --snapshot.", file=sys.stderr)
        return 2

    catalog = load_catalog()
    tarkov_ids = load_tarkov_ids(args.snapshot)
    wanted = args.only or (sorted(catalog) if args.all else wearable_ids(catalog))
    args.output.mkdir(parents=True, exist_ok=True)

    jobs = []
    unknown = []
    for item_id in wanted:
        tarkov_id = tarkov_id_for(catalog.get(item_id, ""), tarkov_ids)
        if tarkov_id is None:
            unknown.append(item_id)
        elif args.refresh or not (args.output / f"{item_id}.png").exists():
            jobs.append((item_id, tarkov_id))

    print(f"{len(wanted):,} items wanted, {len(jobs):,} to download into {args.output}")
    counts = {"ok": 0, "missing": 0, "failed": 0}
    problems = []
    with ThreadPoolExecutor(max_workers=WORKERS) as pool:
        for done, (item_id, result) in enumerate(pool.map(lambda job: import_one(job, args.output), jobs), 1):
            key = result.split(":")[0]
            counts[key] += 1
            if key != "ok":
                problems.append(f"{item_id}: {result}")
            if done % 50 == 0 or done == len(jobs):
                print(f"  {done:,}/{len(jobs):,}", flush=True)

    print(f"Downloaded {counts['ok']:,}; no text-free image for {counts['missing']:,}; failed {counts['failed']:,}; "
          f"no tarkov.dev id for {len(unknown):,}.")
    for line in problems[:20]:
        print("  " + line)
    if counts["failed"]:
        print("Run the command again to retry the failed ones.")
    return 1 if counts["failed"] else 0


if __name__ == "__main__":
    sys.exit(main())
